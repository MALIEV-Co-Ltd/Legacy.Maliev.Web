"""Adapt the existing hosted systemd owner boundary for finite stub proof only."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import pwd
import signal
import subprocess
import time
import uuid


def command(*argv):
    return subprocess.run(argv, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                          stderr=subprocess.PIPE, check=True, timeout=45 if 'stop' in argv else 10).stdout.decode()


def observe(unit):
    raw = command('sudo', '-n', 'systemctl', 'show', unit, '--no-pager',
                  '--property=Id,Description,InvocationID,MainPID,ControlGroup,ActiveState,SubState,Result,ExecMainStatus,ExecMainStartTimestampMonotonic,ExecMainExitTimestampMonotonic,LoadState')
    result = dict(line.split('=', 1) for line in raw.splitlines())
    if result['Id'] != unit:
        raise ValueError('Exact manager unit identity changed')
    return result


def members(group):
    if not group or not group.startswith('/system.slice/web-kernel-') or '..' in group:
        raise ValueError('Exact private cgroup identity required')
    root = Path('/sys/fs/cgroup') / group.lstrip('/')
    return [pid for path in root.rglob('cgroup.procs')
            for pid in path.read_text().split()] if root.exists() else []


def main():
    def interrupted(*_):
        raise InterruptedError('Hosted launcher interrupted; settle exact owner')
    signal.signal(signal.SIGTERM, interrupted)
    signal.signal(signal.SIGINT, interrupted)
    parser = argparse.ArgumentParser()
    parser.add_argument('--evidence', type=Path, required=True)
    args = parser.parse_args()
    if os.uname().sysname != 'Linux' or os.geteuid() == 0 or os.environ.get('GITHUB_REPOSITORY') != 'MALIEV-Co-Ltd/Legacy.Maliev.Web':
        raise ValueError('Unprivileged isolated hosted Web runner required')
    import check_web_candidate_admission as admission
    admission.census()
    if not Path('/sys/fs/cgroup/cgroup.controllers').exists():
        raise ValueError('Actual unified kernel caps unavailable')
    run = os.environ['GITHUB_RUN_ID']; attempt = os.environ['GITHUB_RUN_ATTEMPT']
    if not run.isdecimal() or not attempt.isdecimal():
        raise ValueError('Exact hosted run identity required')
    unit = f'web-kernel-{run}-{attempt}-{uuid.uuid4().hex[:12]}.service'
    description = f'WebKernelProof:{uuid.uuid4().hex}'
    script = Path(__file__).with_name('run_web_capped_kernel_proof.py').resolve()
    evidence = args.evidence.resolve(); evidence.mkdir(exist_ok=False)
    receipt = evidence / 'kernel-proof.json'
    ledger = {'unit': unit, 'description': description, 'runId': run,
              'runAttempt': attempt, 'executionHead': os.environ['GITHUB_SHA'],
              'script': str(script), 'scriptSha256': hashlib.sha256(script.read_bytes()).hexdigest(),
              'argv': ['/usr/bin/python3', '-B', str(script), '--unit', unit, '--receipt', str(receipt)],
              'persistentData': False, 'sdkStarted': False, 'dispatchAttempted': False,
              'cleanupVerified': False, 'expirySeconds': 2100}
    def save():
        (evidence / 'launcher.json').write_text(json.dumps(ledger, indent=2) + '\n')
    save()
    if observe(unit)['LoadState'] != 'not-found':
        raise ValueError('Refuse an existing manager owner')
    failure = None; invocation = None
    try:
        ledger['dispatchAttempted'] = True; save()
        command('sudo', '-n', 'systemd-run', f'--unit={unit}', f'--description={description}',
                '--service-type=exec', '--remain-after-exit',
                f'--property=User={pwd.getpwuid(os.getuid()).pw_name}',
                '--property=MemoryMax=3G', '--property=MemorySwapMax=0',
                '--property=CPUQuota=100%', '--property=TasksMax=512',
                '--property=RuntimeMaxSec=2100', '--property=TimeoutStopSec=30',
                '--property=KillMode=control-group', '--property=SendSIGKILL=yes',
                '--property=LimitCORE=0', '--property=LimitFSIZE=268435456',
                '--setenv=GITHUB_REPOSITORY=MALIEV-Co-Ltd/Legacy.Maliev.Web',
                '--setenv=PYTHONDONTWRITEBYTECODE=1', *ledger['argv'])
        deadline = time.monotonic() + 120
        while time.monotonic() < deadline:
            state = observe(unit)
            if state['Description'] != description:
                raise ValueError('Manager ownership nonce changed')
            if state['InvocationID']:
                if invocation and invocation != state['InvocationID']:
                    raise ValueError('Manager invocation changed')
                invocation = state['InvocationID']; ledger['invocationId'] = invocation
            if int(state['MainPID']) > 0 and 'harnessProcess' not in ledger:
                pid = int(state['MainPID'])
                ledger['harnessProcess'] = {'pid': pid, 'startTicks': None, 'executable': '/usr/bin/python3'}
                try:
                    ledger['harnessProcess'].update(startTicks=Path(f'/proc/{pid}/stat').read_text().rsplit(')', 1)[1].split()[19], executable=os.readlink(f'/proc/{pid}/exe'))
                except FileNotFoundError:
                    ledger['harnessProcess']['observationFailure'] = 'exited during manager readback'
                save()
            if state['SubState'] == 'exited' or state['ActiveState'] in {'failed', 'inactive'}:
                ledger['terminal'] = state; save()
                if not invocation or int(state['ExecMainStartTimestampMonotonic']) <= 0 or int(state['ExecMainExitTimestampMonotonic']) < int(state['ExecMainStartTimestampMonotonic']) or members(state['ControlGroup']):
                    raise ValueError('Actual terminal process evidence missing')
                if state['Result'] != 'success' or state['ExecMainStatus'] != '0':
                    raise ValueError('Actual kernel proof failed')
                break
            time.sleep(.25)
        else:
            raise TimeoutError('Finite stub proof deadline exceeded')
    except BaseException as error:
        failure = error; ledger['failure'] = type(error).__name__
    finally:
        deferred = []
        def defer(signum, _frame):
            if len(deferred) < 16: deferred.append(signum)
        signal.signal(signal.SIGTERM, defer)
        signal.signal(signal.SIGINT, defer)
        # A failed dispatch reply is ambiguous. Recover only the exact nonce
        # registered before dispatch; never stop a foreign/reused invocation.
        try:
            state = observe(unit)
            if state['LoadState'] == 'not-found':
                ledger['cleanupVerified'] = True
            else:
                if state['Description'] != description or (invocation and state['InvocationID'] != invocation):
                    raise ValueError('Uncertain ownership; retain finite lease')
                ledger['cleanupBefore'] = state
                deadline = time.monotonic() + 100
                while True:
                    try:
                        current = observe(unit)
                        if current['LoadState'] == 'not-found':
                            after = current; ledger['cleanupAfter'] = after; break
                        if current['LoadState'] != 'not-found' and (current['Description'] != description or (invocation and current['InvocationID'] != invocation)):
                            raise ValueError('Owner changed during cleanup')
                        command('sudo', '-n', 'systemctl', 'stop', unit)
                        after = observe(unit); ledger['cleanupAfter'] = after
                        break
                    except (subprocess.SubprocessError, OSError):
                        if time.monotonic() >= deadline: raise
                        time.sleep(.25)
                group = state['ControlGroup']
                if after['ActiveState'] not in {'inactive', 'failed'} or members(group):
                    raise ValueError('Actual owned unit remains live')
                if after['ActiveState'] == 'failed':
                    command('sudo', '-n', 'systemctl', 'reset-failed', unit)
                    after = observe(unit); ledger['cleanupAfter'] = after
                if after['LoadState'] != 'not-found':
                    raise ValueError('Exact disposable manager unit remains registered')
                ledger['managerUnitAbsent'] = True
                ledger['cleanupVerified'] = True
                if receipt.exists():
                    ledger['harnessProcess'] = json.loads(receipt.read_text()).get('harnessProcess')
        except BaseException as error:
            ledger['cleanupFailure'] = type(error).__name__
            if failure is None: failure = error
        ledger['deferredSignals'] = deferred
        if deferred and failure is None:
            failure = InterruptedError('Deferred hosted cancellation after owner settlement')
            ledger['failure'] = type(failure).__name__
        save()
    if failure is not None: raise failure


if __name__ == '__main__':
    main()
