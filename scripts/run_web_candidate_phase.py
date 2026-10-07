"""Finite Linux phase owner; exact private group, pidfd and retained process cleanup."""
import argparse
import datetime as dt
import json
import os
from pathlib import Path
import select
import shutil
import signal
import subprocess
import time
import check_web_candidate_admission as admission

GRACE_SECONDS=10
stop_requested=False

def request_stop(signum, frame):
    global stop_requested
    stop_requested=True

def remaining_seconds(grant, now=None):
    now=now or dt.datetime.now(dt.timezone.utc)
    expiry=dt.datetime.fromisoformat(grant["expiresUtc"].replace("Z","+00:00"))
    remaining=(expiry-now).total_seconds()
    if remaining<=GRACE_SECONDS:raise ValueError("Permit has insufficient lifetime for owned shutdown")
    return min(1200,remaining-GRACE_SECONDS)

def phase_deadline(grant):
    monotonic_now=time.monotonic()
    wall_now=dt.datetime.now(dt.timezone.utc)
    return monotonic_now+remaining_seconds(grant,wall_now)

def live_members(pid):
    rows=[]
    for entry in Path("/proc").iterdir():
        if not entry.name.isdecimal():continue
        try:
            fields=(entry/"stat").read_text().rsplit(")",1)[1].split()
            if int(fields[2])!=pid or fields[0]=="Z":continue
            row={"pid":int(entry.name),"startTimeTicks":fields[19],"owner":admission.OWNER,"purpose":"private phase member"}
            try:row["executable"]=str((entry/"exe").resolve(strict=True))
            except FileNotFoundError:row["identityObservationFailure"]="exited during executable readback"
            rows.append(row)
        except FileNotFoundError:continue
    return rows

def observe_members(proc,record):
    try:
        rows=live_members(proc.pid)
        for row in rows:record.setdefault("members",{})[str(row["pid"])+":"+row["startTimeTicks"]]=row
        return rows
    except BaseException as error:
        record.setdefault("observationErrors",[]).append(type(error).__name__)
        return None

def signal_owned_group(proc,sig):
    # Leader remains unreaped through all group signals. Private session/group cannot be joined by foreign sessions.
    if os.getpgid(proc.pid)!=proc.pid or os.getsid(proc.pid)!=proc.pid:raise RuntimeError("Private creator group/session ownership changed")
    os.killpg(proc.pid,sig)

def capture_chunk(output,chunk,limit=16*1024**2):
    remaining=max(0,limit-output.tell())
    output.write(chunk[:remaining])
    if len(chunk)>remaining:raise RuntimeError("Hard phase output cap reached")

def shutdown(proc,pidfd,record):
    # Never poll/wait/reap the leader before the final owned signal.
    members=observe_members(proc,record)
    if members is None or members:
        signal_owned_group(proc,signal.SIGTERM)
        end=time.monotonic()+5
        while time.monotonic()<end:
            members=observe_members(proc,record)
            if members==[]:break
            time.sleep(.05)
        if members is None or members:signal_owned_group(proc,signal.SIGKILL)
    proc.wait(timeout=5)
    record["pidfdExited"]=pidfd is not None and bool(select.select([pidfd],[],[],0)[0])
    remaining=observe_members(proc,record)
    record["privateGroupHasNoLiveMembers"]=remaining==[]
    record["cleanupVerified"]=record["pidfdExited"] and remaining==[]
    if not record["cleanupVerified"]:raise RuntimeError("Owned phase cleanup is unverified")

def main():
    parser=argparse.ArgumentParser();parser.add_argument("--policy",type=Path,required=True);parser.add_argument("--permit",type=Path,required=True)
    parser.add_argument("--evidence",type=Path,required=True);parser.add_argument("--phase",required=True);parser.add_argument("--id",required=True);parser.add_argument("command",nargs=argparse.REMAINDER)
    args=parser.parse_args();policy=admission.intake.load_policy(args.policy);raw=args.permit.read_bytes()
    grant=admission.validate(policy,raw);admission.census();duration=remaining_seconds(grant)
    if not args.command or args.phase not in grant["allowedPhases"]:raise ValueError("Unadmitted phase/command")
    command=args.command[1:] if args.command[0]=="--" else args.command
    if policy.get("sliceKind")=="country-operation-v1":
        phase=grant["phase"]
        if args.id!=phase["id"] or args.phase!=phase["name"] or command!=phase["argv"]:
            raise ValueError("Exact country BUILD argv and phase identity required")
    executable=shutil.which(command[0])
    if not executable:raise ValueError("Executable unavailable")
    if not hasattr(os,"pidfd_open"):raise ValueError("Actual Linux pidfd support required")
    probe=os.pidfd_open(os.getpid());os.close(probe)
    args.evidence.mkdir(parents=True,exist_ok=True)
    if not args.id or any(c not in "abcdefghijklmnopqrstuvwxyz0123456789-" for c in args.id):raise ValueError("Noncanonical phase evidence ID")
    receipt=args.evidence/(args.id+".json");log=args.evidence/(args.id+".log")
    if receipt.exists() or log.exists():raise ValueError("Fresh phase evidence paths required")
    record={"owner":admission.OWNER,"phase":args.phase,"expiresUtc":grant["expiresUtc"],"command":command,"executable":executable,"persistentData":False,"ports":[],"cleanupVerified":False,"phaseSucceeded":False}
    proc=None;pidfd=None
    previous={sig:signal.signal(sig,request_stop) for sig in (signal.SIGTERM,signal.SIGINT)}
    try:
        with log.open("xb") as output:
            # Reobserve admission immediately before creating the single private phase group.
            grant=admission.validate(policy,raw);admission.census();duration=remaining_seconds(grant)
            command[0]=executable
            end=phase_deadline(grant)
            proc=subprocess.Popen(command,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,start_new_session=True)
            record["pid"]=proc.pid
            record["startTimeTicks"]=None
            receipt.write_text(json.dumps(record,indent=2)+"\n",encoding="utf-8")
            pidfd=os.pidfd_open(proc.pid)
            try:
                record["startTimeTicks"]=(Path("/proc")/str(proc.pid)/"stat").read_text().rsplit(")",1)[1].split()[19]
            except BaseException as error:
                record["identityObservationFailure"]=type(error).__name__
                raise
            os.set_blocking(proc.stdout.fileno(),False)
            stdout_open=True
            while True:
                if stop_requested:raise InterruptedError("Owned phase cancellation requested")
                if time.monotonic()>=end:raise TimeoutError("Phase deadline reserves shutdown before permit expiry")
                ready=select.select([pidfd]+([proc.stdout.fileno()] if stdout_open else []),[],[],.1)[0]
                if proc.stdout.fileno() in ready:
                    chunk=os.read(proc.stdout.fileno(),65536)
                    if chunk:capture_chunk(output,chunk)
                    else:stdout_open=False
                observe_members(proc,record)
                if pidfd in ready:break
    except BaseException as error:
        record["phaseFailure"]=type(error).__name__
        raise
    finally:
        try:
            if proc is not None:
                shutdown(proc,pidfd,record)
                record["exitCode"]=proc.returncode
                with log.open("ab") as tail:
                    while select.select([proc.stdout.fileno()],[],[],0)[0]:
                        chunk=os.read(proc.stdout.fileno(),65536)
                        if not chunk:break
                        capture_chunk(tail,chunk)
                record["phaseSucceeded"]=record["exitCode"]==0 and record["cleanupVerified"] and "phaseFailure" not in record
        except BaseException as error:
            record["cleanupErrors"]=[type(error).__name__]
            record["phaseSucceeded"]=False
            raise
        finally:
            close_errors=[]
            closers=[]
            if proc is not None and proc.stdout is not None:closers.append(proc.stdout.close)
            if pidfd is not None:closers.append(lambda:os.close(pidfd))
            for close in closers:
                try:close()
                except BaseException as error:close_errors.append(type(error).__name__)
            if close_errors:
                record.setdefault("cleanupErrors",[]).extend(close_errors)
                record["phaseSucceeded"]=False
            try:
                receipt.write_text(json.dumps(record,indent=2)+"\n",encoding="utf-8")
            finally:
                for sig,handler in previous.items():signal.signal(sig,handler)
            if close_errors:raise RuntimeError("Owned handle close failed; inspect receipt")
    if record.get("exitCode")!=0:raise SystemExit("Phase failed; exact output and ownership receipt retained")

if __name__=="__main__":main()
