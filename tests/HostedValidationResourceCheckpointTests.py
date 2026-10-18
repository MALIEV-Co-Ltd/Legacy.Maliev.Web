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


class VstestDiagnosticReplayTests(unittest.TestCase):
    """Qualification contract for passive VSTest event replay, never test acceptance.

    Proposed API in the existing checkpoint module:
      VstestDiagnosticReplay(owner, persisted=None)
      .poll(exact_trace_path, existing_Inputs)
      .state() -> JSON-safe projection/cursors, never raw diagnostic bytes
      .receipt() -> events, observed_uncompleted, history_complete,
                    history_truncated, reasons

    Every poll shares its caller's Inputs byte/time budget. A fresh replay object
    restored from state represents the next existing checkpoint phase.
    """
    OWNER = {
        "GITHUB_RUN_ID": "39064342206", "GITHUB_RUN_ATTEMPT": "1",
        "GITHUB_SHA": "3b503afa1a677fa7d2c663e23a774f728055f064",
        "GITHUB_JOB": "validate",
        "MALIEV_BILLING_CANDIDATE_HEAD": "e97747f8132e2fc1e05e857081e8211802e90149",
        "RESOURCE_CHECKPOINT_SCOPE": "full",
    }
    A = "Legacy.Maliev.Web.Tests.ReplayFixture.First"
    B = "Legacy.Maliev.Web.Tests.ReplayFixture.Second"
    ID_A = "11111111-1111-4111-8111-111111111111"
    ID_B = "22222222-2222-4222-8222-222222222222"
    POISON = "PRIVATE_CUSTOMER_TOKEN_592"

    def replay(self, persisted=None):
        return checkpoint.VstestDiagnosticReplay(dict(self.OWNER), persisted=persisted)

    def budget(self, remaining=None):
        result = checkpoint.Inputs(time.monotonic() + 30)
        if remaining is not None:
            result.remaining = remaining
        return result

    def case(self, identifier, fqn):
        return {"Id": identifier, "FullyQualifiedName": fqn,
                "DisplayName": self.POISON, "Source": "/private/" + self.POISON,
                "ExecutorUri": "executor://" + self.POISON,
                "Traits": [{"Name": self.POISON, "Value": self.POISON}]}

    def line(self, starts=(), completed=(), final=False):
        chunk = {"ActiveTests": list(starts),
                 "NewTestResults": [{"TestCase": case, "Outcome": 1,
                                     "ErrorMessage": self.POISON,
                                     "ErrorStackTrace": self.POISON,
                                     "Messages": [{"Text": self.POISON}]}
                                    for case in completed],
                 "TestRunStatistics": {"ExecutedTests": len(completed)},
                 "UnknownPrivateField": self.POISON}
        payload = {"LastRunTests": chunk, "TestRunCompleteArgs":
                   {"Error": self.POISON}} if final else chunk
        message = {"Version": 7,
                   "MessageType": "TestExecution.Completed" if final
                                  else "TestExecution.StatsChange",
                   "Payload": payload}
        prefix = ("TpTrace Verbose: 0 : 123, 1, 2026/10/18, 00:00:00.000, "
                  "100, vstest.console, TestRequestSender.OnExecutionMessageReceived: "
                  "Received message: ")
        return (prefix + json.dumps(message, separators=(",", ":")) + "\n").encode()

    def append(self, path, data):
        with path.open("ab") as stream:
            stream.write(data)

    def restore(self, replay):
        state = json.loads(json.dumps(replay.state()))
        return self.replay(state)

    def safe_projection(self, replay):
        text = json.dumps({"state": replay.state(), "receipt": replay.receipt()},
                          sort_keys=True)
        self.assertNotIn(self.POISON, text)
        for forbidden in ("DisplayName", "ErrorMessage", "ErrorStackTrace",
                          "ExecutorUri", "UnknownPrivateField", "raw_json",
                          "raw_tail", "base64", "currently_executing", "root_cause",
                          "TpTrace", "Received message:", '"Payload"', "CUSTOMER_TOKEN"):
            self.assertNotIn(forbidden, text)
        self.assertLessEqual(len(checkpoint.encoded(replay.state())), checkpoint.MAX_RECEIPT)
        self.assertLessEqual(len(checkpoint.encoded(replay.receipt())), checkpoint.MAX_RECEIPT)

    def test_incremental_active_tests_survive_phase_restore_until_observed_completion(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "vstest.diag"
            replay = self.replay()
            self.append(path, self.line(starts=[self.case(self.ID_A, self.A)]))
            replay.poll(path, self.budget())
            replay = self.restore(replay)
            self.append(path, self.line(starts=[self.case(self.ID_B, self.B)],
                                        completed=[self.case(self.ID_A, self.A)]))
            replay.poll(path, self.budget())
            result = replay.receipt()
            self.assertEqual([self.B], result["observed_uncompleted"])
            self.assertTrue(result["history_complete"])
            self.assertEqual([("start", self.A), ("start", self.B), ("completion", self.A)],
                             [(event["kind"], event["fqn"]) for event in result["events"]])
            self.safe_projection(replay)

    def test_final_run_chunk_removes_only_observed_matching_test(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "vstest.diag"
            replay = self.replay()
            self.append(path, self.line(starts=[self.case(self.ID_A, self.A)]))
            replay.poll(path, self.budget())
            self.append(path, self.line(completed=[self.case(self.ID_A, self.A)], final=True))
            replay.poll(path, self.budget())
            self.assertEqual([], replay.receipt()["observed_uncompleted"])
            self.safe_projection(replay)

    def test_missing_completion_is_observed_uncompleted_without_execution_verdict(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "vstest.diag"
            replay = self.replay()
            self.append(path, self.line(starts=[self.case(self.ID_A, self.A)]))
            replay.poll(path, self.budget())
            replay = self.restore(replay)
            replay.poll(path, self.budget())
            self.assertEqual([self.A], replay.receipt()["observed_uncompleted"])
            self.assertEqual(1, len(replay.receipt()["events"]))
            self.safe_projection(replay)

    def test_duplicate_start_id_is_idempotent_but_conflicting_fqn_is_incomplete(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "vstest.diag"
            replay = self.replay()
            first = self.line(starts=[self.case(self.ID_A, self.A)])
            self.append(path, first + first)
            replay.poll(path, self.budget())
            self.assertEqual(1, len(replay.receipt()["events"]))
            self.append(path, self.line(starts=[self.case(self.ID_A, self.B)]))
            replay.poll(path, self.budget())
            self.assertFalse(replay.receipt()["history_complete"])
            self.assertTrue(replay.receipt()["reasons"])
            self.assertEqual([], replay.receipt()["observed_uncompleted"])
            self.safe_projection(replay)

    def test_unmatched_completion_does_not_invent_start(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "vstest.diag"
            replay = self.replay()
            self.append(path, self.line(completed=[self.case(self.ID_A, self.A)]))
            replay.poll(path, self.budget())
            result = replay.receipt()
            self.assertFalse(result["history_complete"])
            self.assertEqual([], result["observed_uncompleted"])
            self.assertNotIn("start", [event["kind"] for event in result["events"]])
            self.safe_projection(replay)

    def test_single_rotation_continues_old_inode_before_new_file(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "vstest.diag"
            backup = path.with_name("vstest.bak.diag")
            replay = self.replay()
            self.append(path, self.line(starts=[self.case(self.ID_A, self.A)]))
            replay.poll(path, self.budget())
            self.append(path, self.line(completed=[self.case(self.ID_A, self.A)]))
            path.rename(backup)
            self.append(path, self.line(starts=[self.case(self.ID_B, self.B)]))
            replay.poll(path, self.budget())
            self.assertTrue(replay.receipt()["history_complete"])
            self.assertEqual([self.B], replay.receipt()["observed_uncompleted"])
            self.safe_projection(replay)

    def test_missing_rotation_generation_retains_prefix_and_separates_later_events(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "vstest.diag"
            backup = path.with_name("vstest.bak.diag")
            replay = self.replay()
            self.append(path, self.line(starts=[self.case(self.ID_A, self.A)]))
            replay.poll(path, self.budget())
            path.rename(Path(temporary) / "old-generation-held-open")
            self.append(backup, b"unobserved generation\n")
            self.append(path, self.line(starts=[self.case(self.ID_B, self.B)]))
            replay.poll(path, self.budget())
            result = replay.receipt()
            self.assertFalse(result["history_complete"])
            self.assertTrue(result["reasons"])
            self.assertEqual([], result["observed_uncompleted"])
            self.assertEqual([self.A, self.B], [event["fqn"] for event in result["events"]])
            self.assertLess(result["events"][0]["epoch"], result["events"][1]["epoch"])
            self.safe_projection(replay)

    def test_same_inode_truncation_is_sticky_incomplete(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "vstest.diag"
            replay = self.replay()
            self.append(path, self.line(starts=[self.case(self.ID_A, self.A)]))
            replay.poll(path, self.budget())
            with path.open("wb") as stream:
                stream.write(b"\n")
            replay.poll(path, self.budget())
            replay = self.restore(replay)
            self.append(path, self.line(starts=[self.case(self.ID_B, self.B)]))
            replay.poll(path, self.budget())
            self.assertFalse(replay.receipt()["history_complete"])
            self.assertEqual([], replay.receipt()["observed_uncompleted"])
            self.safe_projection(replay)

    def test_split_private_field_never_persists_raw_partial_bytes(self):
        raw = self.line(starts=[self.case(self.ID_A, self.A)])
        sentinel = self.POISON.encode()
        starts = [index for index in range(len(raw)) if raw.startswith(sentinel, index)]
        for position in starts:
            for delta in (1, len(sentinel) // 2, len(sentinel) - 1):
                with self.subTest(position=position, delta=delta), tempfile.TemporaryDirectory() as temporary:
                    path = Path(temporary) / "vstest.diag"
                    replay = self.replay()
                    split = position + delta
                    self.append(path, raw[:split])
                    replay.poll(path, self.budget())
                    self.safe_projection(replay)
                    # A restored phase may fail closed at a split boundary; it must not
                    # persist the private tail or manufacture a completed event.
                    replay = self.restore(replay)
                    self.append(path, raw[split:])
                    replay.poll(path, self.budget())
                    self.safe_projection(replay)
                    self.assertNotIn("completion", [event["kind"] for event in replay.receipt()["events"]])

    def test_partial_line_rotation_marks_gap_and_never_joins_foreign_tail(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "vstest.diag"
            replay = self.replay()
            raw = self.line(starts=[self.case(self.ID_A, self.A)])
            split = raw.index(self.POISON.encode()) + 5
            self.append(path, raw[:split])
            replay.poll(path, self.budget())
            path.rename(Path(temporary) / "lost-partial")
            self.append(path, raw[split:])
            replay.poll(path, self.budget())
            self.assertFalse(replay.receipt()["history_complete"])
            self.assertEqual([], replay.receipt()["observed_uncompleted"])
            self.safe_projection(replay)

    def test_byte_budget_is_shared_across_polls_and_prefix_survives_exhaustion(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "vstest.diag"
            replay = self.replay()
            first = self.line(starts=[self.case(self.ID_A, self.A)])
            self.append(path, first)
            budget = self.budget(len(first) + 32)
            replay.poll(path, budget)
            remaining = budget.remaining
            self.assertLess(remaining, len(first) + 32)
            self.append(path, self.line(starts=[self.case(self.ID_B, self.B)]))
            replay.poll(path, budget)
            self.assertGreaterEqual(budget.remaining, 0)
            self.assertLessEqual(budget.remaining, remaining)
            result = replay.receipt()
            self.assertFalse(result["history_complete"])
            self.assertIn(self.A, [event["fqn"] for event in result["events"]])
            self.safe_projection(replay)

    def test_expired_observation_budget_does_not_consume_new_events(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "vstest.diag"
            replay = self.replay()
            self.append(path, self.line(starts=[self.case(self.ID_A, self.A)]))
            budget = checkpoint.Inputs(time.monotonic() - 1)
            replay.poll(path, budget)
            self.assertEqual([], replay.receipt()["events"])
            self.assertFalse(replay.receipt()["history_complete"])
            self.safe_projection(replay)

    def test_oversize_or_malformed_payload_is_fixed_gap_without_raw_exception(self):
        for raw in (b"x" * (65536 + 1) + b"\n",
                    b"TestRequestSender.OnExecutionMessageReceived: Received message: {"
                    + self.POISON.encode() + b"}\n",
                    self.line(starts=[self.case(self.ID_A, self.A)]).replace(
                        b'"Payload":{', b'"Payload":' + b"[" * 70 + b"{", 1)):
            with self.subTest(size=len(raw)), tempfile.TemporaryDirectory() as temporary:
                path = Path(temporary) / "vstest.diag"
                replay = self.replay()
                self.append(path, raw)
                try:
                    replay.poll(path, self.budget())
                except Exception as error:
                    self.fail("Diagnostic parser must return a fixed gap, not exception type "
                              + type(error).__name__)
                self.assertFalse(replay.receipt()["history_complete"])
                self.safe_projection(replay)

    def test_only_fixed_namespace_identifier_fqns_are_projected(self):
        invalid = ("Other.Tests.Customer.Name", self.A + "(private@example.test)",
                   self.A + "\n" + self.POISON, "Legacy.Maliev.Web.Tests." + self.POISON + "/x",
                   "Legacy.Maliev.Web.Tests.ReplayFixture.M\u00e9thod")
        for fqn in invalid:
            with self.subTest(fqn=fqn), tempfile.TemporaryDirectory() as temporary:
                path = Path(temporary) / "vstest.diag"
                replay = self.replay()
                self.append(path, self.line(starts=[self.case(self.ID_A, fqn)]))
                replay.poll(path, self.budget())
                self.assertEqual([], replay.receipt()["events"])
                self.safe_projection(replay)

    def test_event_ring_is_bounded_and_reports_truncation_without_losing_last_events(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "vstest.diag"
            replay = self.replay()
            for number in range(40):
                identifier = "00000000-0000-4000-8000-" + str(number).zfill(12)
                case = self.case(identifier, self.A)
                self.append(path, self.line(starts=[case]) + self.line(completed=[case]))
            budget = self.budget()
            for _ in range(4):
                replay.poll(path, budget)
            result = replay.receipt()
            self.assertLessEqual(len(result["events"]), 64)
            self.assertTrue(result["history_truncated"])
            self.assertEqual("completion", result["events"][-1]["kind"])
            self.assertEqual([], result["observed_uncompleted"])
            self.safe_projection(replay)

    def test_foreign_or_corrupt_state_is_rejected_without_echoing_values(self):
        replay = self.replay()
        state = replay.state()
        for value in ({"owner": {**self.OWNER, "GITHUB_RUN_ATTEMPT": "2"}},
                      {**state, "owner": {**self.OWNER, "GITHUB_SHA": "0" * 40}},
                      {**state, "unapproved_private": self.POISON}):
            with self.subTest(keys=sorted(value)):
                try:
                    self.replay(value)
                except RuntimeError as error:
                    self.assertNotIn(self.POISON, str(error))
                else:
                    self.fail("Foreign or non-schema replay state accepted")

    def test_missing_trace_is_unavailable_not_a_complete_empty_run(self):
        with tempfile.TemporaryDirectory() as temporary:
            replay = self.replay()
            replay.poll(Path(temporary) / "vstest.diag", self.budget())
            result = replay.receipt()
            self.assertEqual([], result["events"])
            self.assertFalse(result["history_complete"])
            self.assertTrue(result["reasons"])
            self.safe_projection(replay)

    def test_existing_resource_caps_and_worker_lease_remain_unchanged(self):
        self.assertEqual(8 * 1024 * 1024, checkpoint.MAX_INPUT)
        self.assertEqual(256 * 1024, checkpoint.MAX_RECEIPT)
        self.assertEqual(125 * 60, checkpoint.LEASE)
        self.assertEqual(10 * 60, checkpoint.PERIOD)
        source = Path(checkpoint.__file__).read_text()
        self.assertIn("resource.RLIMIT_AS, (256 * 1024 * 1024, 256 * 1024 * 1024)", source)
        self.assertIn("resource.RLIMIT_CPU, (20, 20)", source)
        self.assertIn("time.monotonic() + 30", source)


    def test_trace_symlink_is_preserved_without_reading_private_target(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            target = root / "private-target"
            target.write_bytes(self.line(starts=[self.case(self.ID_A, self.A)]))
            path = root / "vstest.diag"
            path.symlink_to(target)
            replay = self.replay()
            replay.poll(path, self.budget())
            self.assertTrue(path.is_symlink())
            self.assertEqual([], replay.receipt()["events"])
            self.assertFalse(replay.receipt()["history_complete"])
            self.safe_projection(replay)

    def test_active_id_bound_cannot_grow_state_without_limit(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "vstest.diag"
            replay = self.replay()
            budget = self.budget()
            for number in range(129):
                identifier = "00000000-0000-4000-8000-" + str(number).zfill(12)
                self.append(path, self.line(starts=[self.case(identifier, self.A)]))
                replay.poll(path, budget)
            result = replay.receipt()
            self.assertFalse(result["history_complete"])
            self.assertEqual([], result["observed_uncompleted"])
            self.safe_projection(replay)

    def test_unknown_extra_trace_generation_is_gap_without_reading_it(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            path = root / "vstest.diag"
            self.append(path, self.line(starts=[self.case(self.ID_A, self.A)]))
            replay = self.replay()
            replay.poll(path, self.budget())
            extra = root / "vstest.bak.diag9c7489f0-f315-4f91-beb1-7a22820bdffc"
            extra.write_text(self.POISON)
            replay.poll(path, self.budget())
            self.assertFalse(replay.receipt()["history_complete"])
            self.assertTrue(extra.exists())
            self.safe_projection(replay)


if __name__ == "__main__":
    unittest.main()
