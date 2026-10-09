"""Email intake rejection controls. Synthetic grants never authorize an SDK process."""
import copy
import datetime as dt
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
import uuid
import xml.etree.ElementTree as ET
import ast
from unittest.mock import patch
sys.path.insert(0, str(Path(__file__).parents[1]))
import materialize_web_candidate as intake
import check_web_candidate_admission as admission
import run_web_candidate_phase as phase
import run_web_account_candidate as account

class EmailSessionControls(unittest.TestCase):
    def policy_path(self):
        return Path(os.environ.get("WEB_EMAIL_SESSION_POLICY", str(Path(__file__).parents[1] / "web-email-change-session-policy.json")))

    def policy(self):
        return intake.load_policy(self.policy_path())

    def fixture(self):
        policy = self.policy()
        now = dt.datetime(2026, 10, 9, tzinfo=dt.timezone.utc)
        policy["sdkOwnerContextSha256"] = "a" * 64
        grant = {key: policy[key] for key in ("acceptedBase", "sourcePins", "sourceBindingSha256", "manifestSha256",
                 "producerPrerequisites", "sdkOwnerContextSha256", "allowedPhases", "phases")}
        grant.update(owner=admission.OWNER, issuedBy="019fc21e-50f0-7112-834f-9fb3b35b9dfe",
                     environment="github-hosted-linux", sliceKind="email-change-session-v1", transportSha="b" * 40,
                     runId="123", runAttempt="1", startsUtc=now.isoformat(), expiresUtc=(now + dt.timedelta(minutes=30)).isoformat())
        raw = json.dumps(grant).encode()
        policy["nativeAdmissionSha256"] = intake.sha256(raw)
        return policy, grant, raw, now

    def environment(self):
        return patch.dict(os.environ, WEB_REVIEWED_TRANSPORT_SHA="b" * 40, GITHUB_RUN_ID="123", GITHUB_RUN_ATTEMPT="1")

    def manifest(self, policy):
        obj = {key: policy[key] for key in ("owner", "acceptedBase", "sourcePins", "sourceBindingSha256", "sourceFiles", "capsuleRows", "capsuleSha256", "capsuleBytes")}
        obj["schemaVersion"] = 1
        return obj

    def test_unissued_policy_rejects(self):
        with self.assertRaises(ValueError): admission.validate(self.policy(), b"{}")

    def test_exact_policy_and_old_policy_seals(self):
        self.assertEqual(1985, len(self.policy()["sourceFiles"]))
        self.assertEqual(intake.EMAIL_SESSION_PATHS, {row["path"] for row in self.policy()["capsuleRows"]})
        for name in ("web-candidate-policy.json", "web-account-failure-policy.json", "web-country-policy.json"):
            intake.load_policy(Path(intake.__file__).with_name(name))

    def test_changed_policy_raw_hash(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "changed.json"
            path.write_bytes(self.policy_path().read_bytes() + b" ")
            with self.assertRaises(ValueError): intake.load_policy(path)

    def test_valid_synthetic_grant(self):
        policy, grant, raw, now = self.fixture()
        with self.environment(), patch.object(admission.os, "name", "posix"):
            self.assertEqual(grant, admission.validate(policy, raw, now))

    def test_run_attempt_and_transport_replay(self):
        policy, _, raw, now = self.fixture()
        for key, value in (("GITHUB_RUN_ID", "124"), ("GITHUB_RUN_ATTEMPT", "2"), ("WEB_REVIEWED_TRANSPORT_SHA", "c" * 40)):
            with self.environment(), patch.dict(os.environ, {key: value}), self.assertRaises(ValueError):
                admission.validate(policy, raw, now)

    def test_expired_and_excessive_lease(self):
        policy, grant, raw, now = self.fixture()
        with self.environment(), self.assertRaises(ValueError): admission.validate(policy, raw, now + dt.timedelta(hours=1))
        grant["expiresUtc"] = (now + dt.timedelta(hours=1)).isoformat()
        raw = json.dumps(grant).encode(); policy["nativeAdmissionSha256"] = intake.sha256(raw)
        with self.environment(), self.assertRaises(ValueError): admission.validate(policy, raw, now)

    def test_old_scope_and_changed_commands(self):
        for mutation in (lambda grant: grant.update(sliceKind="account-failure-v1"),
                         lambda grant: grant["phases"][1]["argv"].append("--no-build"),
                         lambda grant: grant.update(acceptedBase="0" * 40),
                         lambda grant: grant.update(sdkOwnerContextSha256="0" * 64)):
            policy, grant, _, now = self.fixture(); grant = copy.deepcopy(grant); mutation(grant)
            raw = json.dumps(grant).encode(); policy["nativeAdmissionSha256"] = intake.sha256(raw)
            with self.environment(), self.assertRaises(ValueError): admission.validate(policy, raw, now)

    def test_changed_consumer_contract(self):
        policy, _, raw, now = self.fixture()
        policy["producerPrerequisites"]["contractFiles"]["Legacy.Maliev.Web.Infrastructure/AccountSessionManager.cs"] = "0" * 64
        with self.environment(), self.assertRaises(ValueError): admission.validate(policy, raw, now)

    def test_extra_missing_and_duplicate_capsule_paths(self):
        for mutate in (lambda obj: obj["capsuleRows"].append(dict(obj["capsuleRows"][0], path="extra.cs")),
                       lambda obj: obj["capsuleRows"].pop(),
                       lambda obj: obj["capsuleRows"].__setitem__(1, copy.deepcopy(obj["capsuleRows"][0]))):
            policy = self.policy(); obj = self.manifest(policy); mutate(obj)
            policy["capsuleRows"] = obj["capsuleRows"]
            raw = json.dumps(obj).encode(); policy["manifestSha256"] = intake.sha256(raw)
            with self.assertRaises(ValueError): intake.validate_capsule(raw, b"", policy)

    def test_changed_inventory_hash_even_when_manifest_rebound(self):
        policy = self.policy(); obj = self.manifest(policy)
        obj["sourceFiles"][0]["sha256"] = "0" * 64
        policy["sourceFiles"] = obj["sourceFiles"]
        raw = json.dumps(obj).encode(); policy["manifestSha256"] = intake.sha256(raw)
        with self.assertRaises(ValueError): intake.validate_capsule(raw, b"", policy)

    def test_baseline_and_dependency_replay(self):
        for key, value in (("acceptedBase", "0" * 40), ("sourcePins", {"ServiceDefaults": "0" * 40})):
            policy = self.policy(); obj = self.manifest(policy); policy[key] = value; obj[key] = value
            raw = json.dumps(obj).encode(); policy["manifestSha256"] = intake.sha256(raw)
            with self.assertRaises(ValueError): intake.validate_capsule(raw, b"", policy)

    def test_build_first_and_prior_cleanup_proof(self):
        policy, grant, _, _ = self.fixture()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            phase.verify_email_phase_history(policy, grant, "email-sdk-install", root)
            with self.assertRaises(FileNotFoundError): phase.verify_email_phase_history(policy, grant, "email-focused", root)
            for row in policy["phases"][:4]:
                receipt = {key: grant[key] for key in ("sourceBindingSha256", "manifestSha256", "transportSha", "runId", "runAttempt")}
                receipt.update({key: policy[key] for key in ("sdkOwnerContextSha256", "nativeAdmissionSha256")})
                receipt.update(phaseSucceeded=True, cleanupVerified=True, phase=row["name"], admittedArgv=row["argv"])
                (root / (row["id"] + ".json")).write_text(json.dumps(receipt))
            phase.verify_email_phase_history(policy, grant, "email-focused", root)
            for key, value in (("cleanupVerified", False), ("phaseSucceeded", False), ("manifestSha256", "0" * 64), ("runAttempt", "2"),
                               ("nativeAdmissionSha256", "0" * 64), ("sdkOwnerContextSha256", "0" * 64)):
                path = root / "email-build.json"; original = path.read_bytes(); receipt = json.loads(original); receipt[key] = value
                path.write_text(json.dumps(receipt))
                with self.assertRaises(ValueError): phase.verify_email_phase_history(policy, grant, "email-focused", root)
                path.write_bytes(original)

    def test_creation_grant_is_distinct_and_nonce_bound(self):
        policy, grant, _, now = self.fixture()
        policy["sdkOwnerContextSha256"] = None
        grant.update(sdkOwnerContextSha256=None, sdkOwnerEnrollment=policy["sdkOwnerEnrollment"], ownerNonce="d" * 32)
        raw = json.dumps(grant).encode(); policy["nativeAdmissionSha256"] = intake.sha256(raw)
        with self.environment(), patch.object(admission.os, "name", "posix"):
            self.assertEqual(grant, admission.validate(policy, raw, now))
        grant["ownerNonce"] = "invalid"
        raw = json.dumps(grant).encode(); policy["nativeAdmissionSha256"] = intake.sha256(raw)
        with self.environment(), self.assertRaises(ValueError): admission.validate(policy, raw, now)

    def test_unissued_creation_cannot_touch_controller(self):
        with patch.object(account.phase.subprocess, "run") as spawn:
            with self.assertRaises(ValueError): account.authorize_creation(self.policy_path(), Path("missing-independent-permit"), Path("unused"))
            spawn.assert_not_called()

    def test_same_job_profile_outputs_reject_missing_foreign_or_duplicate(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); binary = root / "fixture.dll"; binary.write_bytes(b"controlled binary")
            output = root / "outputs"
            output.write_text("MALIEV_PROFILE_AUTH_DLL=" + str(binary))
            with self.assertRaises(ValueError): account.read_profile_outputs(output, root)
            output.write_text("SECRET=" + str(binary))
            with self.assertRaises(ValueError): account.read_profile_outputs(output, root)
            output.write_text("MALIEV_PROFILE_AUTH_DLL=" + str(binary) + "\nMALIEV_PROFILE_AUTH_DLL=" + str(binary))
            with self.assertRaises(ValueError): account.read_profile_outputs(output, root)

    def test_actual_enrolled_owner_still_checks_kernel_and_nonce(self):
        from test_web_candidate_custody import CapControls
        with tempfile.TemporaryDirectory() as directory:
            _, file, proc, cg, target, show, context = CapControls().fixture(Path(directory))
            policy = self.policy()
            grant = {"ownerNonce": "d" * 32, "runId": "123", "runAttempt": "1",
                     "expiresUtc": context["expiresUtc"], "sdkOwnerEnrollment": policy["sdkOwnerEnrollment"]}
            context["unit"] = "web-kernel-123-1-dddddddddddd.service"
            show["Id"] = context["unit"]
            file.write_text(json.dumps(context))
            self.assertEqual(context, phase.verify_capped_owner(policy, file, proc, cg, show, grant=grant))
            grant["ownerNonce"] = "e" * 32
            with self.assertRaises(ValueError): phase.verify_capped_owner(policy, file, proc, cg, show, grant=grant)
            grant["ownerNonce"] = "d" * 32
            (target / "memory.max").write_text("max")
            with self.assertRaises(ValueError): phase.verify_capped_owner(policy, file, proc, cg, show, grant=grant)

    def test_trx_cannot_accept_empty_or_skipped_suite(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "suite.trx"
            for values in ('total="0" executed="0" passed="0"', 'total="2" executed="1" passed="1" notExecuted="1"'):
                path.write_text('<TestRun xmlns="urn:trx"><Counters ' + values + '/></TestRun>')
                with self.assertRaises(ValueError): account.verify_trx(path)

    def trx_fixture(self, root):
        ns = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
        tree = ET.Element(ns + "TestRun")
        definitions = ET.SubElement(tree, ns + "TestDefinitions")
        results = ET.SubElement(tree, ns + "Results")
        entries = ET.SubElement(tree, ns + "TestEntries")
        summary = ET.SubElement(tree, ns + "ResultSummary", outcome="Completed")
        keys = "total executed passed failed error timeout aborted inconclusive passedButRunAborted notRunnable notExecuted disconnected warning completed inProgress pending".split()
        ET.SubElement(summary, ns + "Counters", {key: "48" if key in {"total", "executed", "passed"} else "0" for key in keys})
        dll = root / "Legacy.Maliev.Web.Tests/bin/Release/net10.0/Legacy.Maliev.Web.Tests.dll"
        dll.parent.mkdir(parents=True); dll.write_bytes(b"portable XML parser fixture only; not a native assembly")
        for name, (cls, method) in account.expected_email_cases().items():
            identity, execution = str(uuid.uuid4()), str(uuid.uuid4())
            definition = ET.SubElement(definitions, ns + "UnitTest", id=identity, name=name, storage=str(dll))
            ET.SubElement(definition, ns + "Execution", id=execution)
            ET.SubElement(definition, ns + "TestMethod", className=cls, name=method, codeBase=str(dll))
            ET.SubElement(results, ns + "UnitTestResult", testId=identity, executionId=execution, testName=name, outcome="Passed")
            ET.SubElement(entries, ns + "TestEntry", testId=identity, executionId=execution)
        path = root / "results/focused.trx"; path.parent.mkdir()
        ET.ElementTree(tree).write(path)
        return tree, path, ns

    def test_independent_48_case_roster_positive_parser_control(self):
        with tempfile.TemporaryDirectory() as directory:
            _, path, _ = self.trx_fixture(Path(directory))
            self.assertEqual(48, account.verify_trx(path, True)["passed"])

    def test_exact_root_duplicate_wrong_namespace_wrong_dll_repro(self):
        with tempfile.TemporaryDirectory() as directory:
            tree, path, ns = self.trx_fixture(Path(directory))
            definitions = tree.find(ns + "TestDefinitions")
            results = tree.find(ns + "Results")
            entries = tree.find(ns + "TestEntries")
            for definition in list(definitions)[2:]: definitions.remove(definition)
            for index, definition in enumerate(definitions):
                cls = "Wrong.Namespace." + ("EmailChangeConfirmationSessionHttpTests" if index == 0 else "EmailChangeConfirmationRealSessionTests")
                definition.find(ns + "TestMethod").set("className", cls)
                definition.find(ns + "TestMethod").set("name", "OnlyOneMethod")
                definition.find(ns + "TestMethod").set("codeBase", "wrong.dll")
                definition.set("storage", "wrong.dll")
                definition.set("name", "DUPLICATE")
            for index, result in enumerate(results):
                result.set("testName", "DUPLICATE")
                result.set("executionId", results[0].get("executionId"))
                result.set("testId", definitions[0 if index < 24 else 1].get("id"))
                entries[index].set("testId", result.get("testId"))
                entries[index].set("executionId", result.get("executionId"))
            self.assertEqual(2, len(definitions)); self.assertEqual(48, len(results))
            ET.ElementTree(tree).write(path)
            with self.assertRaises(ValueError): account.verify_trx(path, True)

    def test_trx_individual_join_identity_and_data_mutations(self):
        def change(tree, ns, kind):
            definitions, results, entries = (tree.find(ns + name) for name in ("TestDefinitions", "Results", "TestEntries"))
            if kind == "missing": results.remove(results[-1])
            elif kind == "extra": results.append(ET.fromstring(ET.tostring(results[0])))
            elif kind == "duplicate-execution": results[1].set("executionId", results[0].get("executionId"))
            elif kind == "duplicate-test": results[1].set("testId", results[0].get("testId"))
            elif kind == "duplicate-definition": definitions[1].set("id", definitions[0].get("id"))
            elif kind == "wrong-class": definitions[0].find(ns + "TestMethod").set("className", "Other.EmailChangeConfirmationSessionHttpTests")
            elif kind == "wrong-method": definitions[0].find(ns + "TestMethod").set("name", "OnlyOneMethod")
            elif kind == "wrong-assembly": definitions[0].find(ns + "TestMethod").set("codeBase", "wrong.dll")
            elif kind == "wrong-storage": definitions[0].set("storage", "wrong.dll")
            elif kind == "definition-name": definitions[0].set("name", "DUPLICATE")
            elif kind == "entry-join": entries[0].set("executionId", str(uuid.uuid4()))
            elif kind == "definition-execution": definitions[0].find(ns + "Execution").set("id", str(uuid.uuid4()))
            elif kind == "counter": tree.find(ns + "ResultSummary").find(ns + "Counters").set("passed", "47")
            elif kind == "summary": tree.find(ns + "ResultSummary").set("outcome", "Failed")
            elif kind == "case":
                name = results[0].get("testName").replace('culture: "en"', 'culture: "unknown"')
                results[0].set("testName", name); definitions[0].set("name", name)
            elif kind == "duplicate-case":
                results[1].set("testName", results[0].get("testName")); definitions[1].set("name", results[0].get("testName"))
        for kind in ("missing", "extra", "duplicate-execution", "duplicate-test", "duplicate-definition", "wrong-class", "wrong-method",
                     "wrong-assembly", "wrong-storage", "definition-name", "entry-join", "definition-execution", "counter", "summary", "case", "duplicate-case"):
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as directory:
                tree, path, ns = self.trx_fixture(Path(directory)); change(tree, ns, kind); ET.ElementTree(tree).write(path)
                with self.assertRaises(ValueError): account.verify_trx(path, True)

    def test_consistent_missing_and_extra_cases_still_rejected(self):
        for extra in (False, True):
            with self.subTest(extra=extra), tempfile.TemporaryDirectory() as directory:
                tree, path, ns = self.trx_fixture(Path(directory))
                definitions, results, entries = (tree.find(ns + name) for name in ("TestDefinitions", "Results", "TestEntries"))
                if not extra:
                    definitions.remove(definitions[-1]); results.remove(results[-1]); entries.remove(entries[-1]); count = "47"
                else:
                    identity, execution = str(uuid.uuid4()), str(uuid.uuid4())
                    dll = definitions[0].get("storage")
                    name = "Legacy.Maliev.Web.Tests.UnknownTests.Extra"
                    definition = ET.SubElement(definitions, ns + "UnitTest", id=identity, name=name, storage=dll)
                    ET.SubElement(definition, ns + "Execution", id=execution)
                    ET.SubElement(definition, ns + "TestMethod", className="Legacy.Maliev.Web.Tests.UnknownTests", name="Extra", codeBase=dll)
                    ET.SubElement(results, ns + "UnitTestResult", testId=identity, executionId=execution, testName=name, outcome="Passed")
                    ET.SubElement(entries, ns + "TestEntry", testId=identity, executionId=execution); count = "49"
                counters = tree.find(ns + "ResultSummary").find(ns + "Counters")
                for key in ("total", "executed", "passed"): counters.set(key, count)
                ET.ElementTree(tree).write(path)
                with self.assertRaises(ValueError): account.verify_trx(path, True)

    def test_actual_assembly_hash_changed_after_build_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            _, path, _ = self.trx_fixture(Path(directory))
            with self.assertRaises(ValueError): account.verify_trx(path, True, "0" * 64)

    def test_malformed_sdk_receipt_preserves_primary_and_retains_ledger_fields(self):
        source_path = Path(account.__file__).with_name("launch_web_capped_kernel_proof.py")
        source = source_path.read_bytes(); compile(source, str(source_path), "exec")
        node = next(node for node in ast.parse(source).body if isinstance(node, ast.FunctionDef) and node.name == "collect_sdk_receipt")
        scope = {"json": json}
        exec(compile(ast.Module(body=[node], type_ignores=[]), str(source_path), "exec"), scope)
        for primary in (None, RuntimeError("original dispatch failure")):
            with self.subTest(primary=primary), tempfile.TemporaryDirectory() as directory:
                root = Path(directory); (root / "email-sdk-version.json").write_text("{malformed")
                ledger = {"cleanupVerified": True}
                failure = scope["collect_sdk_receipt"](root, ledger, primary)
                if primary is not None: self.assertIs(primary, failure)
                else: self.assertIsInstance(failure, json.JSONDecodeError)
                self.assertEqual("JSONDecodeError", ledger["sdkReceiptReadFailure"])
                self.assertIsNone(ledger["sdkStarted"])
                self.assertTrue(ledger["cleanupVerified"])

if __name__ == "__main__": unittest.main()
