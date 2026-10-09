"""Adapt the existing hosted systemd owner boundary for finite stub proof only."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import pwd
import signal
import subprocess
import sys
import time
import uuid
import shutil
import datetime as dt


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


def collect_sdk_receipt(evidence, ledger, failure):
    scope = ledger.get('authorityScope', 'email-change-session-v1')
    if scope not in {'email-change-session-v1', 'optional-tax-build-v1'}:
        ledger['sdkStarted'] = None
        error = ValueError('Unsupported exact SDK receipt scope')
        return failure if failure is not None else error
    path = evidence / ('tax-sdk-version.json' if scope == 'optional-tax-build-v1' else 'email-sdk-version.json')
    try:
        if not path.exists():
            ledger['sdkStarted'] = False
            return failure
        if path.is_symlink() or not path.is_file():
            raise ValueError('Exact regular SDK phase receipt required')
        with path.open('rb') as stream:
            raw = stream.read(1048577)
        if len(raw) > 1048576:
            raise ValueError('SDK receipt exceeds bounded final read')
        receipt = json.loads(raw)
        if not isinstance(receipt, dict):
            raise ValueError('SDK receipt object required')
        ledger['sdkStarted'] = 'pid' in receipt
    except BaseException as error:
        ledger['sdkReceiptReadFailure'] = type(error).__name__
        ledger['sdkStarted'] = None
        if failure is None: failure = error
    return failure

def main():
    def interrupted(*_):
        raise InterruptedError('Hosted launcher interrupted; settle exact owner')
    signal.signal(signal.SIGTERM, interrupted)
    signal.signal(signal.SIGINT, interrupted)
    parser = argparse.ArgumentParser()
    parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--account-policy', type=Path)
    parser.add_argument('--permit', type=Path)
    parser.add_argument('--candidate', type=Path)
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
    account = args.account_policy is not None
    grant = None
    if account:
        if args.permit is None or args.candidate is None:
            raise ValueError('Account source and independent creation grant required')
        from run_web_account_candidate import authorize_creation
        _, grant = authorize_creation(args.account_policy, args.permit, args.candidate)
    nonce = grant['ownerNonce'][:12] if account else uuid.uuid4().hex[:12]
    unit = f'web-kernel-{run}-{attempt}-{nonce}.service'
    description = f'WebKernelProof:{uuid.uuid4().hex}'
    script = Path(__file__).with_name('run_web_account_candidate.py' if account else 'run_web_capped_kernel_proof.py').resolve()
    evidence = args.evidence.resolve(); evidence.mkdir(exist_ok=False)
    receipt = evidence / 'kernel-proof.json'
    ledger = {'unit': unit, 'description': description, 'runId': run,
              'runAttempt': attempt, 'executionHead': os.environ['GITHUB_SHA'],
              'script': str(script), 'scriptSha256': hashlib.sha256(script.read_bytes()).hexdigest(),
              'argv': ['/usr/bin/python3', '-B', str(script), '--unit', unit, '--receipt', str(receipt)],
              'persistentData': False, 'sdkStarted': False, 'dispatchAttempted': False,
              'cleanupVerified': False, 'expirySeconds': 2100}
    if account:
        ledger['argv'] = ['/usr/bin/python3', '-B', str(script), '--owned-worker', '--unit', unit, '--receipt', str(receipt),
                          '--policy', str(args.account_policy.resolve()), '--permit', str(args.permit.resolve()),
                          '--candidate', str(args.candidate.resolve()), '--evidence', str(evidence)]
        ledger['authorityScope'] = grant.get('sliceKind', 'email-change-session-v1')
    forwarded = (['--setenv=' + key + '=' + os.environ[key] for key in
                  ('GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT', 'GITHUB_SHA', 'WEB_REVIEWED_TRANSPORT_SHA', 'RUNNER_ENVIRONMENT', 'PATH', 'HOME')]
                 if account else [])
    if account and grant.get("sliceKind") == "optional-tax-build-v1":
        proof_pin = os.environ.get("WEB_TAX_ADMISSION_PROOF_SHA256")
        if not proof_pin:raise ValueError("Actual external tax admission proof absent")
        forwarded.append("--setenv=WEB_TAX_ADMISSION_PROOF_SHA256=" + proof_pin)
    def save():
        (evidence / 'launcher.json').write_text(json.dumps(ledger, indent=2) + '\n')
    save()
    def owned_group(state):
        group = state['ControlGroup'] or ledger.get('cgroup')
        if not group and receipt.exists():
            context = json.loads(receipt.read_text())['context']
            if context['unit'] != unit or context['invocationId'] != state['InvocationID']:
                raise ValueError('Retained actual kernel receipt identity changed')
            group = context['cgroup']
        if group != '/system.slice/' + unit:
            raise ValueError('Retained exact manager cgroup required')
        return group
    if observe(unit)['LoadState'] != 'not-found':
        raise ValueError('Refuse an existing manager owner')
    failure = None; invocation = None; caches = []
    try:
        if account:
            for cache in (args.candidate.resolve() / '.dependencies/email-sdk', evidence / 'browser-cache'):
                if cache.is_symlink() or cache.parent.is_symlink():
                    raise ValueError('Private account cache path changed')
                cache.mkdir(exist_ok=False)
                identity = cache.stat()
                caches.append((cache, identity.st_dev, identity.st_ino))
                (cache / '.account-owner').write_text(grant['ownerNonce'])
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
                '--setenv=PYTHONDONTWRITEBYTECODE=1', *forwarded, *ledger['argv'])
        remaining = (dt.datetime.fromisoformat(grant['expiresUtc'].replace('Z', '+00:00')) - dt.datetime.now(dt.timezone.utc)).total_seconds() - 40 if account else 120
        if remaining <= 0:
            raise ValueError('Independent owner grant shutdown reserve exhausted')
        deadline = time.monotonic() + min(2060, remaining)
        while time.monotonic() < deadline:
            state = observe(unit)
            if state['Description'] != description:
                raise ValueError('Manager ownership nonce changed')
            if state['ControlGroup']:
                ledger['cgroup'] = owned_group(state)
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
                if not invocation or int(state['ExecMainStartTimestampMonotonic']) <= 0 or int(state['ExecMainExitTimestampMonotonic']) < int(state['ExecMainStartTimestampMonotonic']) or members(owned_group(state)):
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
                group = owned_group(state)
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
        if ledger['cleanupVerified']:
            for cache, device, inode in caches:
                try:
                    actual = cache.stat()
                    if cache.is_symlink() or (actual.st_dev, actual.st_ino) != (device, inode):
                        raise ValueError('Private account cache ownership changed; preserved')
                    if (cache / '.account-owner').exists():
                        if (cache / '.account-owner').read_text() != grant['ownerNonce']:
                            raise ValueError('Private account cache marker changed; preserved')
                        shutil.rmtree(cache)
                    else:
                        cache.rmdir()  # Only remove our exact empty partial creation.
                    if cache.exists() or cache.is_symlink():
                        raise ValueError('Private account cache removal unverified')
                    ledger.setdefault('removedCaches', []).append(str(cache))
                except BaseException as error:
                    ledger['cacheCleanupFailure'] = type(error).__name__
                    if failure is None: failure = error
        ledger['deferredSignals'] = deferred
        if account:
            failure = collect_sdk_receipt(evidence, ledger, failure)
        if deferred and failure is None:
            failure = InterruptedError('Deferred hosted cancellation after owner settlement')
            ledger['failure'] = type(failure).__name__
        try:
            save()
        except BaseException as error:
            # Cleanup evidence durability can fail after exact settlement.
            # Preserve the first exception object and retain a separate bounded
            # log receipt; never turn an uncertain write into cleanup success.
            report = {'unit': unit, 'finalReceiptWriteFailure': type(error).__name__,
                      'originalFailure': type(failure).__name__ if failure else None,
                      'cleanupVerified': ledger['cleanupVerified'],
                      'cleanupFailure': ledger.get('cleanupFailure'),
                      'managerUnitAbsent': ledger.get('managerUnitAbsent', False)}
            try:
                sys.stderr.write(json.dumps(report) + '\n'); sys.stderr.flush()
            except BaseException:
                pass
            if failure is None: failure = error
    if failure is not None: raise failure


if __name__ == '__main__':
    main()
