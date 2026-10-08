"""Dispatch ambiguity must settle only the preregistered exact stub owner."""
import json
import os
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).parents[1]))
if os.name == 'nt':
    sys.modules.setdefault('pwd', SimpleNamespace(getpwuid=lambda _: SimpleNamespace(pw_name='runner')))
import launch_web_capped_kernel_proof as owner


class KernelOwnerControls(unittest.TestCase):
    def run_ambiguous(self, foreign=False, transient=False):
        with tempfile.TemporaryDirectory() as directory:
            evidence = Path(directory) / 'evidence'
            calls = []; observations = 0
            def observe(unit):
                nonlocal observations
                observations += 1
                ledger = json.loads((evidence / 'launcher.json').read_text())
                return {'Id':unit,'LoadState':'not-found' if observations == 1 or observations >= 4 else 'loaded',
                        'Description':'foreign' if foreign else ledger['description'],
                        'InvocationID':'actual-generation','ControlGroup':'/system.slice/'+unit,
                        'ActiveState':'inactive' if observations >= 4 else 'active'}
            stopped = 0
            def command(*argv):
                nonlocal stopped
                calls.append(argv)
                if 'systemd-run' in argv:
                    raise owner.subprocess.TimeoutExpired(argv, 10)
                if 'stop' in argv:
                    stopped += 1
                    if transient and stopped == 1:
                        raise owner.subprocess.TimeoutExpired(argv, 45)
                return ''
            original_exists = Path.exists
            def exists(path):
                return True if str(path).replace('\\','/') == '/sys/fs/cgroup/cgroup.controllers' else original_exists(path)
            with patch.dict(os.environ, GITHUB_REPOSITORY='MALIEV-Co-Ltd/Legacy.Maliev.Web', GITHUB_RUN_ID='123', GITHUB_RUN_ATTEMPT='1', GITHUB_SHA='a'*40), patch.object(owner.os,'uname',create=True,return_value=SimpleNamespace(sysname='Linux')), patch.object(owner.os,'geteuid',create=True,return_value=1000), patch.object(owner.os,'getuid',create=True,return_value=1000), patch.object(owner,'observe',side_effect=observe), patch.object(owner,'command',side_effect=command), patch.object(owner,'members',return_value=[]), patch.object(Path,'exists',exists), patch.object(owner.signal,'signal'), patch.object(owner.time,'sleep'), patch('check_web_candidate_admission.census'), patch.object(sys,'argv',['launcher','--evidence',str(evidence)]):
                with self.assertRaises(owner.subprocess.TimeoutExpired): owner.main()
            return json.loads((evidence/'launcher.json').read_text()), calls

    def test_ambiguous_dispatch_is_cleaned_without_erasing_failure(self):
        ledger,calls=self.run_ambiguous()
        self.assertTrue(ledger['cleanupVerified'])
        self.assertEqual('TimeoutExpired',ledger['failure'])
        self.assertTrue(any('stop' in argv for argv in calls))

    def test_foreign_nonce_is_never_stopped(self):
        ledger,calls=self.run_ambiguous(foreign=True)
        self.assertFalse(ledger['cleanupVerified'])
        self.assertFalse(any('stop' in argv for argv in calls))

    def test_stop_timeout_reobserves_disappeared_unit(self):
        ledger,calls=self.run_ambiguous(transient=True)
        self.assertTrue(ledger['cleanupVerified'])
        self.assertEqual('TimeoutExpired',ledger['failure'])


if __name__ == '__main__': unittest.main()
