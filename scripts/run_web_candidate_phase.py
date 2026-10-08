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
    # Retain the unreaped leader and every handle until the private group is
    # empty and the exact Popen child is reaped. Faults/cancellation never
    # transfer ownership or permit new work; each attempt is bounded.
    recovery_deadline=time.monotonic()+10
    def retain(error):
        errors=record.setdefault("cleanupErrors",[])
        if len(errors)<16:errors.append(type(error).__name__)
    while True:
        try:
            members=observe_members(proc,record)
        except BaseException as error:
            retain(error);members=None
        if members is None or members:
            term_sent=False
            try:signal_owned_group(proc,signal.SIGTERM);term_sent=True
            except BaseException as error:retain(error)
            try:
                members=observe_members(proc,record)
            except BaseException as error:
                retain(error);members=None
            if term_sent and members:
                grace_deadline=time.monotonic()+5
                while members and time.monotonic()<grace_deadline:
                    try:time.sleep(.05)
                    except BaseException as error:retain(error)
                    try:members=observe_members(proc,record)
                    except BaseException as error:
                        retain(error);members=None
            if members is None or members:
                try:signal_owned_group(proc,signal.SIGKILL)
                except BaseException as error:retain(error)
            # Never reap while descendants may still require the retained
            # private group identity. A failed TERM cannot skip KILL/observation.
            try:members=observe_members(proc,record)
            except BaseException as error:
                retain(error);members=None
        if members==[]:
            try:
                proc.wait(timeout=1)
                record["leaderReaped"]=True
            except BaseException as error:
                retain(error)
            if record.get("leaderReaped"):
                # No live group can be released even when pidfd setup failed.
                record["custodySettled"]=True
                record["privateGroupHasNoLiveMembers"]=True
                if pidfd is None:
                    record["pidfdExited"]=False;record["cleanupVerified"]=False
                    raise RuntimeError("Missing pidfd proof after exact child settlement")
                try:
                    record["pidfdExited"]=bool(select.select([pidfd],[],[],0)[0])
                except BaseException as error:
                    retain(error);record["pidfdExited"]=False
                record["cleanupVerified"]=record["pidfdExited"]
                if not record["cleanupVerified"]:raise RuntimeError("Exited child pidfd proof unavailable")
                return
        if time.monotonic()>=recovery_deadline:
            record["cleanupOnlyContainment"]=True
        try:time.sleep(.05)
        except BaseException as error:retain(error)

def verify_capped_owner(policy,context_path,proc_root=Path("/proc"),cgroup_root=Path("/sys/fs/cgroup"),show=None):
    # Read-only adapter to the existing hosted_owner SDK unit. This function
    # allocates no unit, cgroup, daemon or SDK; absent enrollment fails closed.
    if proc_root==Path("/proc") and (os.name!="posix" or os.geteuid()==0):
        raise ValueError("Unprivileged existing Linux SDK unit required")
    digest=policy.get("sdkOwnerContextSha256")
    if context_path is None or not isinstance(digest,str) or len(digest)!=64:
        raise ValueError("Root-pinned existing capped SDK owner is required")
    raw=Path(context_path).read_bytes()
    if len(raw)>131072 or admission.intake.sha256(raw)!=digest:
        raise ValueError("Existing SDK owner context bytes changed")
    context=admission.intake.parse_json(raw)
    required={"owner","unit","invocationId","cgroup","device","inode","hostBootId","expiresUtc"}
    if set(context)!=required or context["owner"]!=admission.OWNER:
        raise ValueError("Exact externally enrolled Web SDK owner required")
    unit=context["unit"]
    if not isinstance(unit,str) or len(unit)>160 or any(c not in "abcdefghijklmnopqrstuvwxyz0123456789-_." for c in unit) or not unit.endswith(".service"):
        raise ValueError("Bounded exact SDK unit identity required")
    group=context["cgroup"]
    if not isinstance(group,str) or not group.startswith("/") or group=="/" or any(part in (".","..","") for part in group[1:].split("/")):
        raise ValueError("Exact private SDK cgroup required")
    if (proc_root/"self/cgroup").read_text().strip()!="0::"+group:
        raise ValueError("Phase must already inhabit externally owned SDK cgroup")
    if (proc_root/"sys/kernel/random/boot_id").read_text().strip()!=context["hostBootId"]:
        raise ValueError("Existing owner host boot changed")
    target=cgroup_root/group.lstrip("/")
    before=target.stat()
    if (before.st_dev,before.st_ino)!=(context["device"],context["inode"]):
        raise ValueError("Externally owned cgroup generation changed")
    # Same kernel limits as the reviewed hosted_owner SDK service: 3G, zero
    # swap, one CPU, 512 tasks. These are observed before either SDK spawn.
    maximum=(target/"memory.max").read_text().strip()
    quota,period=(target/"cpu.max").read_text().split()
    tasks=(target/"pids.max").read_text().strip()
    if not maximum.isdecimal() or not 0<int(maximum)<=3*1024**3 or (target/"memory.swap.max").read_text().strip()!="0":
        raise ValueError("Actual finite SDK memory/swap caps required")
    if not quota.isdecimal() or not period.isdecimal() or not 0<int(quota)<=int(period):
        raise ValueError("Actual one-CPU SDK cap required")
    if not tasks.isdecimal() or not 0<int(tasks)<=512:
        raise ValueError("Actual finite SDK task cap required")
    if show is None:
        result=subprocess.run(["/usr/bin/systemctl","show",unit,"--no-pager","--property=Id,InvocationID,ControlGroup,ActiveState,RuntimeMaxUSec,KillMode,SendSIGKILL"],stdin=subprocess.DEVNULL,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,check=True,timeout=3)
        show=dict(line.split("=",1) for line in result.stdout.decode("utf-8").splitlines())
    if show.get("Id")!=unit or show.get("InvocationID")!=context["invocationId"] or show.get("ControlGroup")!=group or show.get("ActiveState")!="active":
        raise ValueError("Existing exact manager invocation changed")
    if show.get("RuntimeMaxUSec") not in {"35min","2100s"} or show.get("KillMode")!="control-group" or show.get("SendSIGKILL")!="yes":
        raise ValueError("Existing finite whole-unit expiry/cleanup required")
    end=dt.datetime.fromisoformat(context["expiresUtc"].replace("Z","+00:00"))
    now=dt.datetime.now(dt.timezone.utc)
    if end.utcoffset()!=dt.timedelta(0) or not 40<(end-now).total_seconds()<=2100:
        raise ValueError("Finite unrenewed existing SDK owner expiry required")
    after=target.stat()
    if (after.st_dev,after.st_ino)!=(before.st_dev,before.st_ino):
        raise ValueError("SDK cgroup generation changed during admission")
    return context

def main():
    parser=argparse.ArgumentParser();parser.add_argument("--policy",type=Path,required=True);parser.add_argument("--permit",type=Path,required=True)
    parser.add_argument("--evidence",type=Path,required=True);parser.add_argument("--phase",required=True);parser.add_argument("--id",required=True);parser.add_argument("--sdk-owner-context",type=Path);parser.add_argument("command",nargs=argparse.REMAINDER)
    args=parser.parse_args();policy=admission.intake.load_policy(args.policy);raw=args.permit.read_bytes()
    grant=admission.validate(policy,raw);admission.census();duration=remaining_seconds(grant)
    if not args.command or args.phase not in grant["allowedPhases"]:raise ValueError("Unadmitted phase/command")
    command=args.command[1:] if args.command[0]=="--" else args.command
    if policy.get("sliceKind")=="country-operation-v1":
        phase=grant["phase"]
        if args.id!=phase["id"] or args.phase!=phase["name"] or command!=phase["argv"]:
            raise ValueError("Exact country BUILD argv and phase identity required")
    owner_context=verify_capped_owner(policy,args.sdk_owner_context)
    executable=shutil.which(command[0])
    if not executable:raise ValueError("Executable unavailable")
    if not hasattr(os,"pidfd_open"):raise ValueError("Actual Linux pidfd support required")
    probe=os.pidfd_open(os.getpid());os.close(probe)
    args.evidence.mkdir(parents=True,exist_ok=True)
    if not args.id or any(c not in "abcdefghijklmnopqrstuvwxyz0123456789-" for c in args.id):raise ValueError("Noncanonical phase evidence ID")
    receipt=args.evidence/(args.id+".json");log=args.evidence/(args.id+".log")
    if receipt.exists() or log.exists():raise ValueError("Fresh phase evidence paths required")
    record={"owner":admission.OWNER,"phase":args.phase,"expiresUtc":grant["expiresUtc"],"command":command,"executable":executable,"persistentData":False,"ports":[],"cleanupVerified":False,"phaseSucceeded":False}
    record["existingSdkOwner"]=owner_context
    proc=None;pidfd=None;first_failure=None
    previous={sig:signal.signal(sig,request_stop) for sig in (signal.SIGTERM,signal.SIGINT)}
    try:
        with log.open("xb") as output:
            # Reobserve admission immediately before creating the single private phase group.
            grant=admission.validate(policy,raw);admission.census();duration=remaining_seconds(grant)
            if verify_capped_owner(policy,args.sdk_owner_context)!=owner_context:
                raise ValueError("Existing unrenewed SDK owner context changed")
            command[0]=executable
            owner_expiry=dt.datetime.fromisoformat(owner_context["expiresUtc"].replace("Z","+00:00"))
            owner_budget=(owner_expiry-dt.datetime.now(dt.timezone.utc)).total_seconds()-40
            if owner_budget<=0:raise ValueError("Existing owner lacks finite shutdown reserve")
            end=min(phase_deadline(grant),time.monotonic()+owner_budget)
            if stop_requested:raise InterruptedError("Cancellation observed before SDK creation")
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
        first_failure=error
        record["phaseFailure"]=type(error).__name__
        raise
    finally:
        cleanup_failure=None
        if proc is not None:
            while not record.get("custodySettled"):
                try:shutdown(proc,pidfd,record)
                except BaseException as error:
                    if cleanup_failure is None:cleanup_failure=error
                    errors=record.setdefault("cleanupErrors",[])
                    if len(errors)<16:errors.append(type(error).__name__)
                    record["phaseSucceeded"]=False
                    # Retry only settlement of this same child/group/handles.
                    # No permit renewal, new work or cleanup ownership transfer.
            record["exitCode"]=proc.returncode
            try:
                with log.open("ab") as tail:
                    while select.select([proc.stdout.fileno()],[],[],0)[0]:
                        chunk=os.read(proc.stdout.fileno(),65536)
                        if not chunk:break
                        capture_chunk(tail,chunk)
            except BaseException as error:
                if cleanup_failure is None:cleanup_failure=error
                record.setdefault("cleanupErrors",[]).append(type(error).__name__)
        # The same handles remain retained through independent close retries;
        # no live process can reach this point before terminal/reap proof.
        if proc is not None and proc.stdout is not None:
            while not proc.stdout.closed:
                try:proc.stdout.close()
                except BaseException as error:
                    if cleanup_failure is None:cleanup_failure=error
        if pidfd is not None:
            import errno
            while True:
                try:os.close(pidfd);break
                except OSError as error:
                    if error.errno==errno.EBADF:break
                    if cleanup_failure is None:cleanup_failure=error
                except BaseException as error:
                    if cleanup_failure is None:cleanup_failure=error
        record["phaseSucceeded"]=record.get("exitCode")==0 and record.get("cleanupVerified",False) and first_failure is None and cleanup_failure is None
        try:receipt.write_text(json.dumps(record,indent=2)+"\n",encoding="utf-8")
        except BaseException as error:
            if cleanup_failure is None:cleanup_failure=error
        for sig,handler in previous.items():
            try:signal.signal(sig,handler)
            except BaseException as error:
                if cleanup_failure is None:cleanup_failure=error
        # The phase's original failure wins over every cleanup interruption.
        if first_failure is None and cleanup_failure is not None:raise cleanup_failure
    if record.get("exitCode")!=0:raise SystemExit("Phase failed; exact output and ownership receipt retained")

if __name__=="__main__":main()
