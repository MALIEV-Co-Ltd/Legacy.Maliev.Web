import json,os,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
import run_hosted

class HostedEntryControls(unittest.TestCase):
    def test_failed_first_probe_retains_refusal_and_never_runs_second(self):
        with tempfile.TemporaryDirectory(prefix='runtime-entry-',dir=Path(__file__).parent) as path:
            with patch.object(run_hosted.sys,'platform','linux'),patch.dict(os.environ,{'GITHUB_ACTIONS':'true','GITHUB_WORKSPACE':path}),patch.object(run_hosted,'run_one',side_effect=PermissionError('unavailable')) as runner:
                with self.assertRaises(PermissionError):run_hosted.main()
            self.assertEqual(1,runner.call_count);self.assertEqual('web',runner.call_args.args[0])
            refusal=json.loads((Path(path)/'member-company-retained/web-runtime-probe-admission-refusal.json').read_text())
            self.assertEqual(dict(role='web',failure='PermissionError',actualRuntimeAccepted=False,backendAllocated=False),refusal)
    def test_existing_evidence_directory_is_never_overwritten(self):
        with tempfile.TemporaryDirectory(prefix='runtime-entry-',dir=Path(__file__).parent) as path:
            (Path(path)/'member-company-retained').mkdir()
            with patch.object(run_hosted.sys,'platform','linux'),patch.dict(os.environ,{'GITHUB_ACTIONS':'true','GITHUB_WORKSPACE':path}),patch.object(run_hosted,'run_one') as runner:
                with self.assertRaises(FileExistsError):run_hosted.main()
                runner.assert_not_called()

if __name__=='__main__':unittest.main()
