"""Offline orchestration guards. These tests never establish native actor acceptance."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import Mock, patch
import xml.etree.ElementTree as ET

sys.dont_write_bytecode = True
script = Path(__file__).resolve().parents[2] / 'scripts/run-fx-joined-hosted.py'
spec = importlib.util.spec_from_file_location('fx_orchestration', script)
orchestration = importlib.util.module_from_spec(spec)
spec.loader.exec_module(orchestration)


class OrchestrationGuards(unittest.TestCase):
    def arguments(self):
        arguments = ['runner', '--graph', 'fixture.json', '--output', 'fixture-output']
        for name in ('web', 'auth', 'catalog', 'web-runtime', 'auth-runtime', 'catalog-runtime'):
            arguments += ['--' + name + '-root', 'fixture-root']
        return arguments

    def test_local_invocation_cannot_start_process_or_provider(self):
        with patch.object(sys, 'argv', self.arguments()), patch.dict('os.environ', {}, clear=True), \
             patch.object(orchestration.subprocess, 'run') as process:
            with self.assertRaisesRegex(RuntimeError, 'hosted-only'):
                orchestration.main()
            process.assert_not_called()

    def test_hosted_missing_runtime_pin_cannot_start_sdk_container_or_http(self):
        candidate = {'issuanceCommit': None, 'issuanceDefaultsCommit': None}
        with patch.object(sys, 'argv', self.arguments()), patch.dict('os.environ', {'GITHUB_ACTIONS': 'true'}, clear=True), \
             patch.object(Path, 'read_text', return_value=json.dumps(candidate)), \
             patch.object(orchestration.subprocess, 'run') as process:
            with self.assertRaisesRegex(ValueError, 'successor'):
                orchestration.main()
            process.assert_not_called()

    def test_wrong_pin_refuses_before_any_mutation(self):
        with patch.object(orchestration, 'git', return_value='b' * 40) as git:
            with self.assertRaisesRegex(ValueError, 'mismatch'):
                orchestration.verify_checkout(Path('fixture-root'), 'a' * 40)
            self.assertEqual(1, git.call_count)

    def test_dirty_checkout_is_preserved(self):
        with patch.object(orchestration, 'git', side_effect=['a' * 40, ' M unrelated.cs']) as git:
            with self.assertRaisesRegex(ValueError, 'Clean committed'):
                orchestration.verify_checkout(Path('fixture-root'), 'a' * 40)
            self.assertEqual(['status', '--porcelain'], list(git.call_args.args[1:]))

    def test_nonowned_container_is_never_stopped(self):
        inspection = [{'Config': {'Labels': {'maliev.fx.run': 'another-run'}}}]
        with patch.object(orchestration, 'checked', return_value=Mock(stdout=json.dumps(inspection))) as command:
            with self.assertRaisesRegex(RuntimeError, 'not owned'):
                orchestration.stop_owned_container('fixture-container', 'expected-run')
            self.assertEqual(1, command.call_count)
            self.assertEqual(['docker', 'inspect', 'fixture-container'], command.call_args.args[0])

    def test_command_failure_does_not_echo_sensitive_arguments(self):
        sensitive = 'disposable-credential-value'
        arguments = ['docker', 'run', '-e', 'POSTGRES_PASSWORD=' + sensitive]
        with patch.object(orchestration.subprocess, 'run', side_effect=subprocess.CalledProcessError(1, arguments)):
            with self.assertRaises(RuntimeError) as caught:
                orchestration.checked(arguments)
            self.assertNotIn(sensitive, str(caught.exception))

    def test_assertion_output_redacts_generated_fixture_credentials(self):
        secret = 'disposable-credential-value'
        self.assertEqual('failed with [REDACTED]', orchestration.sanitize('failed with ' + secret, [secret]))

    def test_skipped_trx_cannot_count_as_native_green(self):
        document = ET.ElementTree(ET.fromstring('<TestRun xmlns="urn:trx"><Counters total="4" executed="3" passed="3" notExecuted="1"/></TestRun>'))
        with patch.object(orchestration.ET, 'parse', return_value=document):
            with self.assertRaisesRegex(RuntimeError, 'skipped'):
                orchestration.verify_trx('fixture.trx', 4)

    def test_arbitrary_failed_tests_cannot_count_as_missing_grant_red(self):
        document = ET.ElementTree(ET.fromstring('<TestRun xmlns="urn:trx"><Counters total="1" executed="1" passed="0" failed="1"/><UnitTestResult outcome="Failed"><Message>database unavailable</Message></UnitTestResult></TestRun>'))
        with patch.object(orchestration.ET, 'parse', return_value=document):
            with self.assertRaisesRegex(RuntimeError, 'missing source-owned grant'):
                orchestration.verify_trx('fixture.trx', 1, True)


if __name__ == '__main__':
    unittest.main()
