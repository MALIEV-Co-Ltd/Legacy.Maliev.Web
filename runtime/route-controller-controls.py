"""Pure mocked controller boundaries; no native/SDK/systemd/browser launches."""
import argparse
import contextlib
import datetime as dt
import importlib.util
import io
import json
import os
from pathlib import Path
import signal
import tempfile
import unittest
from unittest import mock

spec = importlib.util.spec_from_file_location("route", Path(__file__).with_name("run_focus.py"))
route = importlib.util.module_from_spec(spec)
spec.loader.exec_module(route)

class Child:
    pid = 999999999
    def __init__(self, live):
        self.live = live
        self.stdout = io.BytesIO()
        self.stdout.fileno = lambda: 99
        self.stopped = False
    def poll(self): return None if self.live and not self.stopped else 0
    def wait(self, timeout=None): self.stopped = True; return 0
    def terminate(self): self.stopped = True
    def kill(self): self.stopped = True

class Selector:
    def register(self, stream, _events): self.stream = stream
    def select(self, timeout): return [(argparse.Namespace(fileobj=self.stream), 1)]
    def unregister(self, _stream): pass
    def close(self): pass

class Boundaries(unittest.TestCase):
    def phase_overflow(self, live):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); child = Child(live)
            chunks = iter([b'x' * 65536] * 257)
            with mock.patch.object(route.subprocess, 'Popen', return_value=child), mock.patch.object(route.selectors, 'DefaultSelector', return_value=Selector()), mock.patch.object(route.os, 'read', side_effect=lambda *_: next(chunks)):
                with self.assertRaisesRegex(ValueError, 'hard16MiB'):
                    route.phase(['fake'], root, root, 'overflow', dt.datetime.now(dt.timezone.utc) + dt.timedelta(seconds=120))
            self.assertEqual((root/'overflow.log').stat().st_size, 16777216)
            self.assertEqual(json.loads((root/'overflow.json').read_text())['logBytes'], 16777216)
            self.assertTrue(child.stdout.closed)
            self.assertIsNotNone(child.poll())
    def test_fast_exit_output_is_capped(self): self.phase_overflow(False)
    def test_live_output_is_capped_and_child_stopped(self): self.phase_overflow(True)
    def test_expired_phase_budget_spawns_nothing(self):
        with mock.patch.object(route.subprocess, 'Popen') as popen:
            with self.assertRaisesRegex(ValueError, 'budget'):
                route.phase(['fake'], Path('.'), Path('.'), 'unused', dt.datetime.now(dt.timezone.utc))
            popen.assert_not_called()
    def test_phase_primary_survives_receipt_error(self):
        original = AssertionError('original-phase')
        with tempfile.TemporaryDirectory() as temporary:
            with mock.patch.object(route.subprocess, 'Popen', side_effect=original), mock.patch.object(route, 'write', side_effect=OSError('receipt')), contextlib.redirect_stderr(io.StringIO()):
                with self.assertRaises(AssertionError) as raised:
                    route.phase(['fake'], Path(temporary), Path(temporary), 'failure', dt.datetime.now(dt.timezone.utc)+dt.timedelta(seconds=120))
            self.assertIs(raised.exception, original)

    def controller_case(self, *, cancellation=False, cancellation_stage="enroll", marker_failure=False, cleanup_failure=False, receipt_failure=False):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); evidence = root/'evidence'; permit_path=root/'permit.json';permit_path.write_text('{}')
            a=argparse.Namespace(policy=root/'policy', permit=permit_path, source=root, evidence=evidence)
            permit={'routeSourceSha256':'a'*64,'oneUseNonce':'b'*32,'expiresUtc':(dt.datetime.now(dt.timezone.utc)+dt.timedelta(seconds=120)).isoformat()}
            handlers={}; original=AssertionError('original-enrollment'); cleanup_calls=[]
            def handler_registration(signum, handler):
                previous=handlers.get(signum, signal.SIG_DFL);handlers[signum]=handler;return previous
            def enroll(*args, **kwargs):
                if cancellation:
                    if cancellation_stage == "enroll": handlers[signal.SIGTERM](signal.SIGTERM, None)
                    else: (evidence/"worker-result.json").write_text("{}")
                elif not marker_failure: raise original
            def clean(_unit, _context, receipt):
                cleanup_calls.append(True)
                if cancellation and cancellation_stage == 'cleanup': handlers[signal.SIGTERM](signal.SIGTERM, None)
                if cleanup_failure: raise OSError('cleanup')
                receipt['physicalAbsence']=True;receipt['managerState']='inactive'
            real_marker=route.create_marker
            def marker(item, value):
                if marker_failure:
                    owner=item['path']/'.route-owner';owner.write_text('partial')
                    stat=owner.stat();item['markerIdentity']=(stat.st_dev,stat.st_ino)
                    raise original
                real_marker(item,value)
                if cancellation and cancellation_stage == 'cache': handlers[signal.SIGTERM](signal.SIGTERM, None)
            real_write=route.write
            def write(path,value):
                if receipt_failure and path.name=='cleanup.json':raise OSError('receipt')
                real_write(path,value)
            with contextlib.ExitStack() as stack:
                for patch in [mock.patch.object(route,'authority',return_value=({},permit,{})), mock.patch.object(route,'census',return_value={}),mock.patch.object(route.signal,'signal',side_effect=handler_registration),mock.patch.object(route,'create_marker',side_effect=marker),mock.patch.object(route,'write',side_effect=write),mock.patch.object(route.subprocess,'run',side_effect=enroll),mock.patch.object(route,'identity',return_value={'unit':'fake'}),mock.patch.object(route,'manager',return_value={'ActiveState':'inactive'}),mock.patch.object(route,'clean_unit',side_effect=clean),mock.patch.object(route.os,'getuid',return_value=1000,create=True),mock.patch.object(route.os,'getgid',return_value=1000,create=True),mock.patch.dict(os.environ,{'HOME':'/fake/runner','GITHUB_RUN_ID':'1','GITHUB_SHA':'c'*40,'GITHUB_ACTIONS':'true','GITHUB_RUN_ATTEMPT':'1','GITHUB_REPOSITORY':'MALIEV-Co-Ltd/Legacy.Maliev.Web','FOCUS_JOB_STARTED_UNIX':str(int(dt.datetime.now(dt.timezone.utc).timestamp()))})]:stack.enter_context(patch)
                stack.enter_context(contextlib.redirect_stderr(io.StringIO()))
                with self.assertRaises(route.ControllerCancelled if cancellation else AssertionError) as raised:route.controller(a)
                if not cancellation:self.assertIs(raised.exception,original)
            if not cleanup_failure:
                self.assertFalse((evidence/'browser-cache').exists())
                self.assertFalse((evidence/'sdk-cache').exists())
            else:
                self.assertTrue((evidence/'browser-cache').exists()) # Preserved while unit cleanup uncertain.
            self.assertEqual(bool(cleanup_calls), not marker_failure and not (cancellation and cancellation_stage == "cache"))
            if not receipt_failure:
                receipt=json.loads((evidence/'cleanup.json').read_text())
                self.assertIn('originalError',receipt)
                if cancellation:self.assertEqual(receipt['deferredSignals'],[signal.SIGTERM])
    def test_enrollment_sigterm_is_deferred_until_owned_cleanup(self):self.controller_case(cancellation=True)
    def test_cache_window_sigterm_cleans_before_enrollment(self):self.controller_case(cancellation=True,cancellation_stage="cache")
    def test_cleanup_window_sigterm_delivered_after_absence(self):self.controller_case(cancellation=True,cancellation_stage="cleanup")
    def test_partial_marker_is_exactly_removed_before_enrollment(self):self.controller_case(marker_failure=True)
    def test_controller_primary_survives_cleanup_failure(self):self.controller_case(cleanup_failure=True)
    def test_controller_primary_survives_receipt_failure(self):self.controller_case(receipt_failure=True)
    def test_null_authority_prevents_apply_spawn_enrollment(self):
        a=argparse.Namespace(policy=Path(__file__).with_name('admission-policy.json'),permit=Path('absent'),source=Path('absent'),evidence=Path('absent'))
        with mock.patch.object(route.sys,'platform','linux'),mock.patch.dict(os.environ,{'GITHUB_ACTIONS':'true','GITHUB_RUN_ID':'1'}),mock.patch.object(route.subprocess,'Popen') as popen,mock.patch.object(route.subprocess,'run') as run,mock.patch.object(route,'phase') as phase:
            with self.assertRaisesRegex(ValueError,'Root authority missing'):route.controller(a)
            popen.assert_not_called();run.assert_not_called();phase.assert_not_called()

if __name__ == '__main__': unittest.main()
