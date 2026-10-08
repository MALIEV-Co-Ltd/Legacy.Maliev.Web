"""Stub-only real Linux proof of the existing Web phase owner; never starts SDK."""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import select
import signal
import subprocess
import sys
import time
import run_web_candidate_phase as phase

STUB = """import signal,time
signal.signal(signal.SIGALRM,lambda *args:exit(124))
signal.alarm(25)
signal.signal(signal.SIGTERM,lambda *args:exit(0))
print('ready',flush=True)
while True:time.sleep(.05)
"""

def manager(unit):
    result=subprocess.run(['/usr/bin/systemctl','show',unit,'--no-pager','--property=Id,InvocationID,ControlGroup,ActiveState,RuntimeMaxUSec,KillMode,SendSIGKILL'],stdin=subprocess.DEVNULL,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,check=True,timeout=3)
    return dict(line.split('=',1) for line in result.stdout.decode().splitlines())

def case(name,record=None):
    child=None;pidfd=None;record={} if record is None else record;record.update(case=name,sdkStarted=False,cleanupVerified=False);original=phase.signal_owned_group
    observed=[];first_fault=None
    def send(proc,sig):
        nonlocal first_fault
        observed.append(sig)
        if sig==signal.SIGTERM and first_fault is None and name in {'failed-term','interrupted-term'}:
            first_fault=RuntimeError('injected TERM fault') if name=='failed-term' else KeyboardInterrupt()
            raise first_fault
        return original(proc,sig)
    try:
        child=subprocess.Popen([sys.executable,'-B','-c',STUB],stdin=subprocess.DEVNULL,stdout=subprocess.PIPE,stderr=subprocess.PIPE,start_new_session=True)
        record.update(pid=child.pid,startTicks=None,executable=sys.executable)
        try:
            record.update(startTicks=Path(f'/proc/{child.pid}/stat').read_text().rsplit(')',1)[1].split()[19],executable=os.readlink(f'/proc/{child.pid}/exe'))
        except BaseException as error:
            record['birthObservationFailure']=type(error).__name__
            raise
        pidfd=os.pidfd_open(child.pid)
        deadline=time.monotonic()+5
        while not select.select([child.stdout],[],[],.05)[0]:
            if time.monotonic()>=deadline:raise TimeoutError('Finite stub readiness exceeded')
        if child.stdout.readline()!=b'ready\n':raise ValueError('Actual stub readiness missing')
    finally:
        if child is not None:
            phase.signal_owned_group=send
            try:
                while not record.get('custodySettled'):
                    try:phase.shutdown(child,pidfd,record)
                    except BaseException as error:
                        faults=record.setdefault('cleanupFaults',[])
                        if len(faults)<16:faults.append(type(error).__name__)
            finally:phase.signal_owned_group=original
            # Reuse the sealed extractor's existing exact-child/reader/pipe
            # settlement barrier after the private group is proven terminal.
            phase.admission.intake.shared.recover_fetch_owner(child)
            record.update(exitCode=child.returncode,pipesClosed=child.stdout.closed and child.stderr.closed,pidAbsent=not Path(f'/proc/{child.pid}').exists())
        if pidfd is not None:
            while True:
                try:os.close(pidfd);break
                except InterruptedError:continue
    record['signals']=observed;record['pidfdClosed']=pidfd is not None
    if not all(record.get(k) for k in ('cleanupVerified','pipesClosed','pidAbsent','pidfdClosed')):raise ValueError('Actual owned stub cleanup incomplete')
    if name=='graceful' and signal.SIGKILL in observed:raise ValueError('Cooperating stub was killed without grace')
    if name!='graceful' and signal.SIGKILL not in observed:raise ValueError('Injected failed TERM did not reach independent KILL')
    return record

def main():
    # Install an independent finite lifetime before creating any stub.
    signal.signal(signal.SIGALRM,lambda *args: (_ for _ in ()).throw(TimeoutError('Finite kernel-only harness expired')))
    signal.alarm(90)
    parser=argparse.ArgumentParser();parser.add_argument('--unit',required=True);parser.add_argument('--receipt',type=Path,required=True);args=parser.parse_args()
    if sys.platform!='linux' or os.environ.get('GITHUB_REPOSITORY')!='MALIEV-Co-Ltd/Legacy.Maliev.Web':raise ValueError('Exact isolated hosted Linux Web proof required')
    phase.admission.census()
    show=manager(args.unit);group=show['ControlGroup'];target=Path('/sys/fs/cgroup')/group.lstrip('/');st=target.stat()
    context={'owner':phase.admission.OWNER,'unit':args.unit,'invocationId':show['InvocationID'],'cgroup':group,'device':st.st_dev,'inode':st.st_ino,'hostBootId':Path('/proc/sys/kernel/random/boot_id').read_text().strip(),'expiresUtc':(dt.datetime.now(dt.timezone.utc)+dt.timedelta(minutes=10)).isoformat()}
    raw=(json.dumps(context,indent=2)+'\n').encode();context_path=args.receipt.with_suffix('.context.json');context_path.write_bytes(raw)
    # This local read-only test pin is NOT a Root SDK permit. Both committed
    # SDK authority pins remain null; no call to SDK phase main is made.
    phase.verify_capped_owner({'sdkOwnerContextSha256':hashlib.sha256(raw).hexdigest()},context_path)
    result={'schemaVersion':1,'unit':args.unit,'context':context,'contextSha256':hashlib.sha256(raw).hexdigest(),'harnessProcess':{'pid':os.getpid(),'startTicks':Path('/proc/self/stat').read_text().rsplit(')',1)[1].split()[19],'executable':os.readlink('/proc/self/exe')},'manager':show,'kernelCaps':{name:(target/name).read_text().strip() for name in ('memory.max','memory.swap.max','cpu.max','pids.max')},'proofScope':'Actual capped unit and finite Python stubs only','rootSdkGrantIssued':False,'sdkStarted':False,'applicationAccepted':False,'rows':[]}
    try:
        for name in ('graceful','failed-term','interrupted-term'):
            record={};result['rows'].append(record);case(name,record)
    finally:args.receipt.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    signal.alarm(0)

if __name__=='__main__':main()
