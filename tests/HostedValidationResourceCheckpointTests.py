import importlib.util
import json
from pathlib import Path
import signal
import tempfile
import time
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("checkpoint", Path(__file__).resolve().parents[1] / "scripts/hosted-validation-resource-checkpoint.py")
checkpoint = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checkpoint)

class ResourceCheckpointTests(unittest.TestCase):
    def test_unavailable_memory_is_not_zero(self):
        self.assertIsNone(checkpoint.memory(None))
        self.assertEqual({"MemTotal": 4096, "MemAvailable": 0}, checkpoint.memory("MemTotal: 4 kB\nMemAvailable: 0 kB\nPrivateField: secret kB\n"))

    def test_cgroup_counters_allow_only_fixed_numeric_fields(self):
        self.assertEqual({"oom": 1, "oom_kill": 2}, checkpoint.events("oom 1\noom_kill 2\nprivate secret\n"))
        with self.assertRaises(RuntimeError):
            checkpoint.events("oom -1\n")

    def test_fixed_process_names_do_not_emit_other_comm_text(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for number, name in ((1, "dotnet"), (2, "private-customer-name"), (3, "testhost")):
                (root / str(number)).mkdir()
                (root / str(number) / "comm").write_text(name + "\n")
            result = checkpoint.processes(checkpoint.Inputs(time.monotonic() + 5), root)
            self.assertEqual(3, result["observed_processes"])
            self.assertEqual(1, result["fixed_name_counts"]["dotnet"])
            self.assertEqual(1, result["fixed_name_counts"]["testhost"])
            self.assertNotIn("private", json.dumps(result))

    def test_process_enumeration_caps_and_reports_incompleteness(self):
        with tempfile.TemporaryDirectory() as temporary, patch.object(checkpoint, "MAX_PROCESSES", 2):
            root = Path(temporary)
            for number in (1, 2, 3):
                (root / str(number)).mkdir()
                (root / str(number) / "comm").write_text("dotnet\n")
            result = checkpoint.processes(checkpoint.Inputs(time.monotonic() + 5), root)
            self.assertTrue(result["enumeration_capped"])
            self.assertEqual(2, result["observed_processes"])

    def test_input_growth_is_capped_before_decoding(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "sample"
            path.write_bytes(b"x" * 33)
            source = checkpoint.Inputs(time.monotonic() + 5)
            self.assertIsNone(source.read(path, 32))
            self.assertTrue(source.exceeded)

    def test_receipt_byte_bound_rejects_instead_of_truncating(self):
        with patch.object(checkpoint, "MAX_RECEIPT", 32):
            with self.assertRaises(RuntimeError):
                checkpoint.encoded({"data": "x" * 33})

    def test_receipt_write_is_exclusive(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "sample.json"
            checkpoint.write_new(path, {"phase": 0})
            with self.assertRaises(FileExistsError):
                checkpoint.write_new(path, {"phase": 1})
            self.assertEqual({"phase": 0}, json.loads(path.read_bytes()))

    def test_cgroup_traversal_is_unavailable_without_reading_outside(self):
        class Source:
            def read(self, path, maximum=4096):
                return "0::/../../private\n"
        self.assertIsNone(checkpoint.cgroup(Source()))

    def test_exact_cleanup_gracefully_stops_owned_handle(self):
        identity = {"pid": 17, "start_ticks": 100, "exe_device": 2, "exe_inode": 3}
        sent, closed = [], []
        outcome = checkpoint.stop_exact({**identity, "state": "running"}, lambda pid: identity,
                                       lambda pid: 7, lambda handle, sig: sent.append((handle, sig)),
                                       lambda handle, seconds: True, closed.append)
        self.assertEqual("stopped", outcome)
        self.assertEqual([(7, signal.SIGTERM)], sent)
        self.assertEqual([7], closed)

    def test_exact_cleanup_rechecks_identity_before_force(self):
        identity = {"pid": 17, "start_ticks": 100, "exe_device": 2, "exe_inode": 3}
        observations = iter([identity, {**identity, "start_ticks": 101}])
        sent, closed = [], []
        with self.assertRaises(RuntimeError):
            checkpoint.stop_exact({**identity, "state": "running"}, lambda pid: next(observations),
                                  lambda pid: 7, lambda handle, sig: sent.append((handle, sig)),
                                  lambda handle, seconds: False, closed.append)
        self.assertEqual([(7, signal.SIGTERM)], sent)
        self.assertEqual([7], closed)

    def test_mismatched_birth_is_preserved_without_signaling(self):
        identity = {"pid": 17, "start_ticks": 100, "exe_device": 2, "exe_inode": 3}
        sent, closed = [], []
        with self.assertRaises(RuntimeError):
            checkpoint.stop_exact({**identity, "state": "running"},
                                  lambda pid: {**identity, "start_ticks": 101}, lambda pid: 7,
                                  lambda handle, sig: sent.append(sig), lambda handle, seconds: True, closed.append)
        self.assertEqual([], sent)
        self.assertEqual([7], closed)

    def test_exited_pid_reuse_is_not_signaled(self):
        identity = {"pid": 17, "start_ticks": 100, "exe_device": 2, "exe_inode": 3}
        sent = []
        outcome = checkpoint.stop_exact({**identity, "state": "exited"},
                                       lambda pid: {**identity, "start_ticks": 101},
                                       lambda pid: self.fail("No handle should be acquired"),
                                       lambda handle, sig: sent.append(sig),
                                       lambda handle, seconds: True, lambda handle: None)
        self.assertEqual("exited-pid-reused", outcome)
        self.assertEqual([], sent)

    def test_process_exit_during_graceful_stop_is_already_absent(self):
        identity = {"pid": 17, "start_ticks": 100, "exe_device": 2, "exe_inode": 3}
        closed = []
        def gone(handle, sig):
            raise ProcessLookupError()
        outcome = checkpoint.stop_exact({**identity, "state": "running"}, lambda pid: identity,
                                       lambda pid: 7, gone, lambda handle, seconds: self.fail("No wait needed"), closed.append)
        self.assertEqual("absent", outcome)
        self.assertEqual([7], closed)

    def test_owner_uses_fixed_environment_and_rejects_cross_job_scope(self):
        environment = {"GITHUB_RUN_ID": "17", "GITHUB_RUN_ATTEMPT": "2", "GITHUB_SHA": "a" * 40,
                       "GITHUB_JOB": "resource_checkpoint_qualification", "MALIEV_BILLING_CANDIDATE_HEAD": "b" * 40,
                       "RESOURCE_CHECKPOINT_SCOPE": "qualification-live", "PRIVATE_VALUE": "never copied"}
        with patch.dict(checkpoint.os.environ, environment, clear=True):
            identity = checkpoint.owner()
            self.assertNotIn("PRIVATE_VALUE", identity)
            self.assertEqual("qualification-live", identity["RESOURCE_CHECKPOINT_SCOPE"])
            with patch.dict(checkpoint.os.environ, {"GITHUB_JOB": "validate"}):
                with self.assertRaises(RuntimeError):
                    checkpoint.owner()

    def test_receipt_directory_is_scoped_to_exact_run_attempt_and_mode(self):
        with tempfile.TemporaryDirectory() as temporary, patch.dict(checkpoint.os.environ, {"RUNNER_TEMP": temporary}):
            root = Path(temporary).resolve()
            identity = {"GITHUB_RUN_ID": "17", "GITHUB_RUN_ATTEMPT": "2", "RESOURCE_CHECKPOINT_SCOPE": "qualification-live"}
            expected = root / "web-validation-resources-17-2-qualification-live"
            self.assertEqual(root, checkpoint.directory(expected, identity))
            with self.assertRaises(RuntimeError):
                checkpoint.directory(root / "web-validation-resources-17-2-full", identity)

    def test_live_wait_rejects_nonqualification_without_process_observation(self):
        with patch.object(checkpoint, "owner", return_value={"GITHUB_JOB": "validate"}), \
             patch.object(checkpoint, "directory"), \
             patch.object(checkpoint, "proc_identity") as inspect:
            with self.assertRaises(RuntimeError):
                checkpoint.wait_worker(Path("/unused"), 1)
            inspect.assert_not_called()

    def test_managed_release_observes_absent_and_reused_workers_without_signaling(self):
        identity = {"GITHUB_JOB": "resource_checkpoint_qualification"}
        worker = {"pid": 17, "start_ticks": 100, "exe_device": 2, "exe_inode": 3}
        for observation in (FileNotFoundError(), {**worker, "start_ticks": 101}):
            with self.subTest(observation=type(observation).__name__), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                (root / "worker.json").write_text(json.dumps({"owner": identity, **worker}))
                with patch.object(checkpoint, "owner", return_value=identity), \
                     patch.object(checkpoint, "directory"), \
                     patch.object(checkpoint.time, "monotonic", return_value=0), \
                     patch.object(checkpoint, "proc_identity", side_effect=[observation]), \
                     patch.object(checkpoint.signal, "pidfd_send_signal") as send:
                    checkpoint.verify_released(root)
                    send.assert_not_called()

    def test_managed_release_rejects_live_worker_before_cleanup_can_hide_it(self):
        identity = {"GITHUB_JOB": "resource_checkpoint_qualification"}
        worker = {"pid": 17, "start_ticks": 100, "exe_device": 2, "exe_inode": 3}
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "worker.json").write_text(json.dumps({"owner": identity, **worker}))
            with patch.object(checkpoint, "owner", return_value=identity), \
                 patch.object(checkpoint, "directory"), \
                 patch.object(checkpoint.time, "monotonic", side_effect=[0, 0, 16]), \
                 patch.object(checkpoint.time, "sleep"), \
                 patch.object(checkpoint, "proc_identity", return_value=worker), \
                 patch.object(checkpoint.signal, "pidfd_send_signal") as send:
                with self.assertRaisesRegex(RuntimeError, "Managed cancellation"):
                    checkpoint.verify_released(root)
                send.assert_not_called()

if __name__ == "__main__":
    unittest.main()
