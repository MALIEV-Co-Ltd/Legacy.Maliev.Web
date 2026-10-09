"""Pure Career intake controls; synthetic grants and bytes never run an SDK."""
import copy
import datetime as dt
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import uuid
import unittest
import xml.etree.ElementTree as ET
from unittest.mock import patch
sys.path.insert(0, str(Path(__file__).parents[1]))
import career_source_intake as career
import materialize_web_candidate as intake
import check_web_candidate_admission as admission
import run_web_account_candidate as account
import run_web_candidate_phase as phase
import test_web_candidate_email_session as email_controls


class CareerControls(unittest.TestCase):
    def policy(self):
        return intake.load_policy(Path(career.__file__).with_name("web-career-source-policy.json"))

    def grant(self):
        policy = self.policy()
        now = dt.datetime.now(dt.timezone.utc)
        grant = {k: policy[k] for k in ("acceptedBase", "sourcePins", "sourceBindingSha256", "manifestSha256",
                 "sdkOwnerContextSha256", "sdkOwnerEnrollment", "allowedPhases", "phases")}
        grant.update(owner=admission.OWNER, issuedBy="019fc21e-50f0-7112-834f-9fb3b35b9dfe", environment="github-hosted-linux",
                     sliceKind=career.KIND, transportSha="b" * 40, runId="123", runAttempt="1", ownerNonce="d" * 32,
                     startsUtc=now.isoformat(), expiresUtc=(now + dt.timedelta(minutes=30)).isoformat())
        raw = json.dumps(grant).encode()
        policy["nativeAdmissionSha256"] = intake.sha256(raw)
        return policy, grant, raw, now

    def environment(self):
        return patch.dict(os.environ, WEB_REVIEWED_TRANSPORT_SHA="b" * 40, GITHUB_SHA="b" * 40,
                          GITHUB_RUN_ID="123", GITHUB_RUN_ATTEMPT="1")

    def test_exact_three_source_seals_and_old_policies(self):
        career.validate_source_policy(self.policy())
        self.assertEqual(20, len(career.expected_cases()))
        for name in ("web-candidate-policy.json", "web-country-policy.json", "web-account-failure-policy.json", "web-email-change-session-policy.json"):
            intake.load_policy(Path(career.__file__).with_name(name))

    def test_unissued_cannot_fetch_write_or_allocate(self):
        with patch.object(intake, "fetch_blob") as fetch, patch.object(phase.subprocess, "Popen") as launch:
            with self.assertRaises(ValueError):
                career.authorize_creation(self.policy(), Path("missing-permit"), Path("unused"))
            fetch.assert_not_called()
            launch.assert_not_called()

    def test_distinct_synthetic_creation_grant(self):
        policy, grant, raw, now = self.grant()
        with self.environment(), patch.object(career.os, "name", "posix"):
            self.assertEqual(grant, admission.validate(policy, raw, now))

    def test_scope_source_run_commands_nonce_replay(self):
        for key, value in (("sliceKind", "email-change-session-v1"), ("acceptedBase", "0" * 40),
                           ("runId", "124"), ("runAttempt", "2"), ("transportSha", "c" * 40),
                           ("ownerNonce", "invalid"), ("sourceBindingSha256", "0" * 64), ("phases", [])):
            policy, grant, _, now = self.grant()
            grant[key] = value
            raw = json.dumps(grant).encode(); policy["nativeAdmissionSha256"] = intake.sha256(raw)
            with self.subTest(key=key), self.environment(), patch.object(career.os, "name", "posix"), self.assertRaises(ValueError):
                admission.validate(policy, raw, now)

    def test_expired_excessive_lease_and_changed_caps(self):
        policy, grant, raw, now = self.grant()
        with self.environment(), patch.object(career.os, "name", "posix"), self.assertRaises(ValueError):
            admission.validate(policy, raw, now + dt.timedelta(hours=1))
        for value in (3600, 0):
            grant["expiresUtc"] = (now + dt.timedelta(seconds=value)).isoformat()
            raw = json.dumps(grant).encode(); policy["nativeAdmissionSha256"] = intake.sha256(raw)
            with self.environment(), patch.object(career.os, "name", "posix"), self.assertRaises(ValueError):
                admission.validate(policy, raw, now)
        policy["sdkOwnerEnrollment"]["memoryMaxBytes"] *= 2
        with self.assertRaises(ValueError): admission.validate(policy, raw, now)

    def test_career_requires_successful_build_before_focused(self):
        policy, grant, _, _ = self.grant()
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(FileNotFoundError):
                phase.verify_email_phase_history(policy, grant, "career-focused", Path(directory))

    def trx(self, root):
        with patch.object(account, "expected_email_cases", return_value=career.expected_cases()):
            tree, path, ns = email_controls.EmailSessionControls().trx_fixture(root)
        for key in ("total", "executed", "passed"):
            tree.find(ns + "ResultSummary").find(ns + "Counters").set(key, "20")
        for row in tree.find(ns + "TestDefinitions"):
            row.set("storage", row.get("storage").lower())
        ET.ElementTree(tree).write(path)
        return tree, path, ns

    def test_exact_twenty_career_parser_control_and_storage_normalization(self):
        with tempfile.TemporaryDirectory() as directory:
            _, path, _ = self.trx(Path(directory))
            self.assertEqual(20, account.verify_trx(path, True, expected_cases=career.expected_cases(), normalize_storage=True)["passed"])

    def test_dirty_counters_wrong_method_join_and_assembly_rejected(self):
        for kind in ("counter", "method", "join", "assembly"):
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as directory:
                tree, path, ns = self.trx(Path(directory))
                if kind == "counter": tree.find(ns + "ResultSummary").find(ns + "Counters").set("notExecuted", "1")
                if kind == "method": tree.find(ns + "TestDefinitions")[0].find(ns + "TestMethod").set("name", "Wrong")
                if kind == "join": tree.find(ns + "TestEntries")[0].set("executionId", "0" * 32)
                if kind == "assembly": tree.find(ns + "TestDefinitions")[0].find(ns + "TestMethod").set("codeBase", "wrong.dll")
                ET.ElementTree(tree).write(path)
                with self.assertRaises(ValueError):
                    account.verify_trx(path, True, expected_cases=career.expected_cases(), normalize_storage=True)

    def overlay(self, root, finalize=None):
        subprocess.run(["git", "init", "-q", str(root)], check=True)
        subprocess.run(["git", "-C", str(root), "config", "core.autocrlf", "false"], check=True)
        policy = self.policy(); files = {}
        for index, name in enumerate(career.POSTIMAGES):
            target = root / name; target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(b"pre" + bytes([index]))
            policy["preimages"][name] = intake.sha256(target.read_bytes()); files[name] = b"post" + bytes([index])
        subprocess.run(["git", "-C", str(root), "add", "."], check=True)
        subprocess.run(["git", "-C", str(root), "-c", "user.name=control", "-c", "user.email=control@example.invalid", "commit", "-qm", "fixture"], check=True)
        base = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
        post = {name: intake.sha256(data) for name, data in files.items()}
        policy["acceptedBase"] = base
        policy["sourceFiles"] = [{"path": name, "bytes": len(data), "sha256": post[name]} for name, data in files.items()]
        policy["sourceBindingSha256"] = intake.sha256(json.dumps(policy["sourceFiles"], sort_keys=True, separators=(",", ":")).encode())
        with patch.object(career, "BASE", base), patch.object(career, "POSTIMAGES", post):
            career.materialize_career(root, policy, files, finalize)
        return files

    def test_atomic_overlay_cleans_staging(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "candidate"; root.mkdir()
            files = self.overlay(root)
            for name, data in files.items(): self.assertEqual(data, (root / name).read_bytes())
            self.assertEqual([], list(root.parent.glob("career-overlay-*")))

    def test_custody_receipt_failure_rolls_back_and_cleans_staging(self):
        def fail(): raise OSError("controlled final receipt fault")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "candidate"; root.mkdir()
            with self.assertRaises(OSError): self.overlay(root, fail)
            self.assertEqual(b"", subprocess.check_output(["git", "-C", str(root), "diff"]))
            self.assertEqual([], list(root.parent.glob("career-overlay-*")))

    def test_late_replacement_fault_restores_all_prior_files(self):
        original = os.replace
        calls = []
        def replace(source, target):
            calls.append(str(source))
            if str(source).endswith("1.new"):
                raise OSError("controlled late overlay failure")
            return original(source, target)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "candidate"; root.mkdir()
            with patch.object(career.os, "replace", side_effect=replace), self.assertRaises(OSError):
                self.overlay(root)
            self.assertTrue(any(name.endswith("0.old") for name in calls))
            self.assertEqual(b"", subprocess.check_output(["git", "-C", str(root), "diff"]))
            self.assertEqual([], list(root.parent.glob("career-overlay-*")))

    def test_rollback_fault_retains_recovery_until_fixture_cleanup(self):
        original = os.replace
        def replace(source, target):
            if str(source).endswith(("1.new", "0.old")):
                raise OSError("controlled overlay and recovery fault")
            return original(source, target)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "candidate"; root.mkdir()
            with patch.object(career.os, "replace", side_effect=replace), self.assertRaises(BaseExceptionGroup):
                self.overlay(root)
            retained = list(root.parent.glob("career-overlay-*"))
            self.assertEqual(1, len(retained))
            self.assertTrue((retained[0] / "0.old").is_file())

    def full_fixture(self, root):
        tree, path, ns = self.trx(root)
        definitions, results, entries = (tree.find(ns + tag) for tag in ("TestDefinitions", "Results", "TestEntries"))
        dll = root / "Legacy.Maliev.Web.Tests/bin/Release/net10.0/Legacy.Maliev.Web.Tests.dll"
        for (cls, member), count in career.SHARED_MEMBERDATA.items():
            identity = str(uuid.uuid4())
            first_execution = str(uuid.uuid4())
            prefix = cls + "." + member
            definition = ET.SubElement(definitions, ns + "UnitTest", id=identity, name=prefix, storage=str(dll).lower())
            ET.SubElement(definition, ns + "Execution", id=first_execution)
            ET.SubElement(definition, ns + "TestMethod", className=cls, name=member, codeBase=str(dll))
            for index in range(count):
                execution = first_execution if index == 0 else str(uuid.uuid4())
                ET.SubElement(results, ns + "UnitTestResult", testId=identity, executionId=execution,
                              testName=prefix + f"(controlledData: {index})", outcome="Passed")
                ET.SubElement(entries, ns + "TestEntry", testId=identity, executionId=execution)
        identity, execution = str(uuid.uuid4()), str(uuid.uuid4())
        cls, member = "Legacy.Maliev.Web.Tests.ControlledForeignTests", "LongInlineCase"
        name = cls + "." + member + '(value: "' + "x" * 500 + '")'
        definition = ET.SubElement(definitions, ns + "UnitTest", id=identity, name=name[:444] + "···", storage=str(dll).lower())
        ET.SubElement(definition, ns + "Execution", id=execution)
        ET.SubElement(definition, ns + "TestMethod", className=cls, name=member, codeBase=str(dll))
        ET.SubElement(results, ns + "UnitTestResult", testId=identity, executionId=execution, testName=name, outcome="Passed")
        ET.SubElement(entries, ns + "TestEntry", testId=identity, executionId=execution)
        for name, count in career.NATIVE_DISPLAY_ALIASES.items():
            cls, member = name.split("(", 1)[0].rsplit(".", 1)
            for _ in range(count):
                identity, execution = str(uuid.uuid4()), str(uuid.uuid4())
                definition = ET.SubElement(definitions, ns + "UnitTest", id=identity, name=name, storage=str(dll).lower())
                ET.SubElement(definition, ns + "Execution", id=execution)
                ET.SubElement(definition, ns + "TestMethod", className=cls, name=member, codeBase=str(dll))
                ET.SubElement(results, ns + "UnitTestResult", testId=identity, executionId=execution, testName=name, outcome="Passed")
                ET.SubElement(entries, ns + "TestEntry", testId=identity, executionId=execution)
        for key in ("total", "executed", "passed"):
            tree.find(ns + "ResultSummary").find(ns + "Counters").set(key, "61")
        ET.ElementTree(tree).write(path)
        return tree, path, ns, intake.sha256(dll.read_bytes())

    def test_native_full_shared_definitions_exact_truncated_representation(self):
        with tempfile.TemporaryDirectory() as directory:
            _, path, _, digest = self.full_fixture(Path(directory))
            before = path.read_bytes()
            self.assertEqual(61, career.verify_suite(path, 61, digest)["passed"])
            self.assertEqual(before, path.read_bytes())

    def test_full_counter_join_execution_and_assembly_rejections(self):
        for kind in ("counter", "entry", "definition-execution", "duplicate-execution", "assembly", "shared-class"):
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as directory:
                tree, path, ns, digest = self.full_fixture(Path(directory))
                definitions, results, entries = (tree.find(ns + tag) for tag in ("TestDefinitions", "Results", "TestEntries"))
                if kind == "counter": tree.find(ns + "ResultSummary").find(ns + "Counters").set("notExecuted", "1")
                if kind == "entry": entries[0].set("testId", str(uuid.uuid4()))
                if kind == "definition-execution": definitions[-2].find(ns + "Execution").set("id", str(uuid.uuid4()))
                if kind == "duplicate-execution": results[-1].set("executionId", results[0].get("executionId"))
                if kind == "assembly": digest = "0" * 64
                if kind == "shared-class": definitions[-2].find(ns + "TestMethod").set("className", "Legacy.Maliev.Web.Tests.UnknownTests")
                ET.ElementTree(tree).write(path)
                with self.assertRaises(ValueError): career.verify_suite(path, 61, digest)

    def test_literal_display_identity_rejects_arbitrary_normalization(self):
        for kind in ("alternate-ellipsis", "wrong-prefix", "wrong-length", "career-display"):
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as directory:
                tree, path, ns, digest = self.full_fixture(Path(directory))
                definitions = tree.find(ns + "TestDefinitions")
                long_definition = next(row for row in definitions if row.find(ns + "TestMethod").get("className") == "Legacy.Maliev.Web.Tests.ControlledForeignTests")
                if kind == "alternate-ellipsis": long_definition.set("name", long_definition.get("name")[:-3] + "...")
                if kind == "wrong-prefix": long_definition.set("name", long_definition.get("name").replace("xxx", "yyy", 1))
                if kind == "wrong-length": long_definition.set("name", long_definition.get("name")[:-4] + "···")
                if kind == "career-display": definitions[0].set("name", definitions[0].get("name") + " ")
                ET.ElementTree(tree).write(path)
                with self.assertRaises(ValueError): career.verify_suite(path, 61, digest)

    def test_foreign_display_alias_count_and_unknown_duplicate_rejected(self):
        for known in (True, False):
            with self.subTest(known=known), tempfile.TemporaryDirectory() as directory:
                tree, path, ns, digest = self.full_fixture(Path(directory))
                definitions, results = (tree.find(ns + tag) for tag in ("TestDefinitions", "Results"))
                if known:
                    name = results[-1].get("testName") + "changed"
                    results[-1].set("testName", name); definitions[-1].set("name", name)
                else:
                    results[-1].set("testName", results[0].get("testName"))
                ET.ElementTree(tree).write(path)
                with self.assertRaises(ValueError): career.verify_suite(path, 61, digest)


if __name__ == "__main__":
    unittest.main()
