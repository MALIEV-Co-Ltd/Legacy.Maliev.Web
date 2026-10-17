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


    def test_dotnet_role_projection_never_returns_private_arguments(self):
        cases = (("dotnet\0format\0private-project\0", "format"),
                 ("dotnet\0test\0private-project\0", "test"),
                 ("dotnet\0exec\0--runtimeconfig\0/private/runtime.json\0/private/testhost.dll\0", "testhost"),
                 ("dotnet\0/private/MSBuild.dll\0/private/secret\0", "msbuild"),
                 ("dotnet\0exec\0/private/VBCSCompiler.dll\0private-pipe\0", "compiler"),
                 ("dotnet\0/private/Legacy.Maliev.AuthService.Api.dll\0--private\0", "producer"),
                 ("dotnet\0/private/customer.dll\0testhost.dll\0", "other"),
                 ("dotnet\0unknown\0testhost.dll\0", "other"))
        for raw, expected in cases:
            with self.subTest(expected=expected):
                result = checkpoint.dotnet_role(raw)
                self.assertEqual(expected, result)
                self.assertNotIn("private", result)
        for raw in (None, "dotnet\0format", "dotnet\0" + "a\0" * 257):
            self.assertEqual("unavailable", checkpoint.dotnet_role(raw))

    @staticmethod
    def process_stat(birth=100, pages=3):
        tail = ["S"] + ["0"] * 21
        tail[19], tail[21] = str(birth), str(pages)
        return "7 (dotnet (private name)) " + " ".join(tail) + "\n"

    def test_rss_parser_keeps_only_birth_and_resident_pages(self):
        self.assertEqual((100, 3), checkpoint.resident_identity(self.process_stat()))
        for raw in (None, "", "7 (private) S", self.process_stat(pages=-1),
                    self.process_stat(birth="private")):
            self.assertIsNone(checkpoint.resident_identity(raw))

    def test_dotnet_pid_reuse_returns_unavailable_without_identity_or_arguments(self):
        observations = iter((self.process_stat(), "dotnet\0format\0private\0", self.process_stat(birth=101)))
        class Source:
            def read(self, path, maximum=4096):
                return next(observations)
        self.assertEqual(("unavailable", None),
                         checkpoint.dotnet_observation(Source(), Path("/not-read"), 4096))

    def test_dotnet_census_aggregates_fixed_roles_and_rss_only(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for number, command, pages in ((1, "format", 3), (2, "test", 5)):
                directory = root / str(number)
                directory.mkdir()
                (directory / "comm").write_text("dotnet\n")
                (directory / "stat").write_text(self.process_stat(pages=pages))
                (directory / "cmdline").write_bytes(("dotnet\0" + command + "\0private-customer\0").encode())
            with patch.object(checkpoint.os, "sysconf", return_value=4096):
                result = checkpoint.processes(checkpoint.Inputs(time.monotonic() + 5), root)
            self.assertEqual(2, result["fixed_name_counts"]["dotnet"])
            self.assertEqual({"count": 1, "rss_observations": 1, "rss_unavailable": 0, "rss_bytes_sum": 12288},
                             result["dotnet_roles"]["format"])
            self.assertEqual(20480, result["dotnet_roles"]["test"]["rss_bytes_sum"])
            self.assertIsNone(result["dotnet_roles"]["other"]["rss_bytes_sum"])
            self.assertNotIn("private", json.dumps(result))
            self.assertNotIn("pid", json.dumps(result))
            self.assertNotIn(temporary, json.dumps(result))

    def test_unreadable_dotnet_rss_is_unavailable_not_zero(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "1").mkdir()
            (root / "1/comm").write_text("dotnet\n")
            result = checkpoint.processes(checkpoint.Inputs(time.monotonic() + 5), root)
            item = result["dotnet_roles"]["unavailable"]
            self.assertEqual((1, 0, 1, None),
                             (item["count"], item["rss_observations"], item["rss_unavailable"], item["rss_bytes_sum"]))

    def test_role_read_budget_is_existing_bounded_input(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "1").mkdir()
            (root / "1/comm").write_text("dotnet\n")
            (root / "1/stat").write_text(self.process_stat())
            (root / "1/cmdline").write_bytes(b"x" * 4097)
            with patch.object(checkpoint.os, "sysconf", return_value=4096):
                result = checkpoint.processes(checkpoint.Inputs(time.monotonic() + 5), root)
            self.assertTrue(result["enumeration_capped"])
            item = result["dotnet_roles"]["unavailable"]
            self.assertEqual((1, 1, 0, 12288),
                             (item["count"], item["rss_observations"], item["rss_unavailable"], item["rss_bytes_sum"]))

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
                with self.assertRaisesRegex(RuntimeError, "Required cancellation"):
                    checkpoint.verify_released(root)
                send.assert_not_called()

    @staticmethod
    def upload_manifest(successful=1):
        return [{"phase": phase, "outcome": "success" if phase < successful else "skipped",
                 "artifact_id": str(100 + phase) if phase < successful else ""} for phase in range(13)]

    def test_qualification_requires_exact_baseline_and_skipped_later_uploads(self):
        checkpoint.validate_upload_manifest(self.upload_manifest(), "qualification-live")
        for change in ("missing", "unknown", "late-id", "late-success", "zero-baseline"):
            with self.subTest(change=change):
                manifest = self.upload_manifest()
                if change == "missing":
                    manifest.pop()
                elif change == "unknown":
                    manifest[1]["outcome"] = ""
                elif change == "late-id":
                    manifest[1]["artifact_id"] = "101"
                elif change == "late-success":
                    manifest[1] = {"phase": 1, "outcome": "success", "artifact_id": "101"}
                else:
                    manifest[0]["artifact_id"] = "0"
                with self.assertRaises(RuntimeError):
                    checkpoint.validate_upload_manifest(manifest, "qualification-live")

    def test_full_manifest_requires_contiguous_success_and_real_artifact_ids(self):
        for successful in (1, 7, 13):
            checkpoint.validate_upload_manifest(self.upload_manifest(successful), "full")
        manifest = self.upload_manifest(3)
        manifest[1] = {"phase": 1, "outcome": "skipped", "artifact_id": ""}
        with self.assertRaises(RuntimeError):
            checkpoint.validate_upload_manifest(manifest, "full")

    def test_terminal_receipt_uses_fixed_inner_upload_fields_only(self):
        identity = {"GITHUB_JOB": "resource_checkpoint_qualification", "RESOURCE_CHECKPOINT_SCOPE": "qualification-live"}
        worker = {"owner": identity, "pid": 17, "start_ticks": 100, "exe_device": 2, "exe_inode": 3,
                  "phase": 1, "state": "exited"}
        environment = {"PRIVATE_VALUE": "never copied"}
        for item in self.upload_manifest():
            prefix = "RESOURCE_UPLOAD_" + str(item["phase"]).zfill(2)
            environment[prefix + "_OUTCOME"] = item["outcome"]
            environment[prefix + "_ID"] = item["artifact_id"]
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "worker.json").write_text(json.dumps(worker))
            with patch.object(checkpoint, "owner", return_value=identity), \
                 patch.object(checkpoint, "directory"), patch.dict(checkpoint.os.environ, environment, clear=True):
                checkpoint.terminal(root)
            receipt = json.loads((root / "quiescence.json").read_bytes())
            self.assertEqual(self.upload_manifest(), receipt["uploads"])
            self.assertNotIn("PRIVATE_VALUE", json.dumps(receipt))
            self.assertEqual(worker, receipt["worker"])

    def prepare_quiescent_receipts(self, root):
        identity = {"GITHUB_JOB": "resource_checkpoint_qualification", "RESOURCE_CHECKPOINT_SCOPE": "qualification-live",
                    "GITHUB_RUN_ID": "17", "GITHUB_RUN_ATTEMPT": "1", "GITHUB_SHA": "a" * 40,
                    "MALIEV_BILLING_CANDIDATE_HEAD": "b" * 40}
        worker = {"owner": identity, "pid": 17, "start_ticks": 100, "exe_device": 2, "exe_inode": 3,
                  "phase": 1, "state": "exited"}
        receipt = {"schema": "web-hosted-resource-quiescence.v1", "owner": identity,
                   "worker": worker, "uploads": self.upload_manifest()}
        (root / "worker.json").write_text(json.dumps(worker))
        (root / "quiescence.json").write_text(json.dumps(receipt))
        (root / "cleanup.json").write_text(json.dumps({"schema": "web-hosted-resource-cleanup.v1",
                                                       "owner": identity, "outcome": "stopped"}))
        return identity, worker, receipt

    def test_quiescence_rejects_changed_owner_and_final_ledger(self):
        for changed in ("owner", "ledger"):
            with self.subTest(changed=changed), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                identity, worker, receipt = self.prepare_quiescent_receipts(root)
                if changed == "owner":
                    receipt["owner"] = {"RESOURCE_CHECKPOINT_SCOPE": "other"}
                    (root / "quiescence.json").write_text(json.dumps(receipt))
                else:
                    (root / "worker.json").write_text(json.dumps({**worker, "start_ticks": 101}))
                with patch.object(checkpoint, "owner", return_value=identity), \
                     patch.object(checkpoint, "directory"), patch.object(checkpoint, "proc_identity") as inspect:
                    with self.assertRaises(RuntimeError):
                        checkpoint.verify_quiescence(root)
                    inspect.assert_not_called()

    def test_quiescence_rechecks_live_final_worker_without_signaling(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            identity, worker, receipt = self.prepare_quiescent_receipts(root)
            actual = {key: worker[key] for key in ("pid", "start_ticks", "exe_device", "exe_inode")}
            with patch.object(checkpoint, "owner", return_value=identity), \
                 patch.object(checkpoint, "directory"), \
                 patch.object(checkpoint, "proc_identity", return_value=actual), \
                 patch.object(checkpoint.signal, "pidfd_send_signal") as send:
                with self.assertRaises(RuntimeError):
                    checkpoint.verify_quiescence(root)
                send.assert_not_called()
            self.assertFalse((root / "verification.json").exists())

    def test_quiescence_requires_normal_cleanup_receipt(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            identity, worker, receipt = self.prepare_quiescent_receipts(root)
            (root / "cleanup.json").rename(root / "emergency-cleanup.json")
            with patch.object(checkpoint, "owner", return_value=identity), patch.object(checkpoint, "directory"):
                with self.assertRaises(RuntimeError):
                    checkpoint.verify_quiescence(root)

    def test_quiescence_accepts_absent_final_worker_and_retains_artifact_ids(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            identity, worker, receipt = self.prepare_quiescent_receipts(root)
            with patch.object(checkpoint, "owner", return_value=identity), \
                 patch.object(checkpoint, "directory"), \
                 patch.object(checkpoint, "proc_identity", side_effect=FileNotFoundError()):
                checkpoint.verify_quiescence(root)
            verified = json.loads((root / "verification.json").read_bytes())
            self.assertEqual(self.upload_manifest(), verified["uploads"])

    def test_missing_final_receipt_fails_at_existing_fifteen_second_bound(self):
        identity = {"GITHUB_JOB": "resource_checkpoint_qualification", "RESOURCE_CHECKPOINT_SCOPE": "qualification-live"}
        with tempfile.TemporaryDirectory() as temporary:
            with patch.object(checkpoint, "owner", return_value=identity), \
                 patch.object(checkpoint, "directory"), \
                 patch.object(checkpoint.time, "monotonic", side_effect=[0, 16]), \
                 patch.object(checkpoint.time, "sleep") as sleep:
                with self.assertRaises(RuntimeError):
                    checkpoint.verify_quiescence(Path(temporary))
                sleep.assert_not_called()

    def test_cleanup_receipt_names_cannot_escape_the_two_fixed_names(self):
        with self.assertRaises(RuntimeError):
            checkpoint.cleanup(Path("/unused"), "../unowned.json")

    def test_manifest_rejects_pending_cancelled_and_forged_upload_entries(self):
        for changed in ("pending", "cancelled", "extra", "missing-id", "wrong-phase"):
            with self.subTest(changed=changed):
                manifest = self.upload_manifest()
                if changed in ("pending", "cancelled"):
                    manifest[1]["outcome"] = "in_progress" if changed == "pending" else "cancelled"
                elif changed == "extra":
                    manifest[1]["unreviewed"] = "value"
                elif changed == "missing-id":
                    del manifest[0]["artifact_id"]
                else:
                    manifest[1]["phase"] = 2
                with self.assertRaises(RuntimeError):
                    checkpoint.validate_upload_manifest(manifest, "qualification-live")

    def test_quiescence_rejects_stale_attempt_and_later_sample(self):
        for changed in ("stale-attempt", "later-sample"):
            with self.subTest(changed=changed), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                identity, worker, receipt = self.prepare_quiescent_receipts(root)
                if changed == "stale-attempt":
                    receipt["owner"] = {**identity, "GITHUB_RUN_ATTEMPT": "2"}
                    (root / "quiescence.json").write_text(json.dumps(receipt))
                else:
                    (root / "sample-01.json").write_text("{}")
                with patch.object(checkpoint, "owner", return_value=identity), \
                     patch.object(checkpoint, "directory"), patch.object(checkpoint, "proc_identity") as inspect:
                    with self.assertRaises(RuntimeError):
                        checkpoint.verify_quiescence(root)
                    inspect.assert_not_called()

    def test_released_worker_cannot_hide_a_pending_upload_in_quiescence(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            identity, worker, receipt = self.prepare_quiescent_receipts(root)
            receipt["uploads"][1]["outcome"] = "in_progress"
            (root / "quiescence.json").write_text(json.dumps(receipt))
            with patch.object(checkpoint, "owner", return_value=identity), \
                 patch.object(checkpoint, "directory"), \
                 patch.object(checkpoint, "proc_identity", side_effect=FileNotFoundError()) as inspect:
                with self.assertRaises(RuntimeError):
                    checkpoint.verify_quiescence(root)
                inspect.assert_not_called()
            self.assertFalse((root / "verification.json").exists())

if __name__ == "__main__":
    unittest.main()
