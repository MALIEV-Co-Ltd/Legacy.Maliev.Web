"""Authored fail-closed controls. Run both normally and with -O in hosted CI."""
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import signal
import tempfile
import unittest
from unittest.mock import patch
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).parents[1]
spec = importlib.util.spec_from_file_location("line_guard", ROOT / "scripts/verify-line-contact-regression.py")
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)
FROZEN = json.loads((ROOT / guard.CONTRACT).read_text(encoding="utf-8"))


class TrxControls(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.assembly = self.root / FROZEN["assembly"]

    def corpus(self, phase="baseline"):
        q = lambda name: guard.NS + name
        root = ET.Element(q("TestRun"), id=str(uuid.uuid4()))
        chosen = [r for r in FROZEN["rows"] if phase == "green" or r["baseline"] is not None]
        summary = ET.SubElement(root, q("ResultSummary"), outcome="Failed" if phase == "baseline" else "Completed")
        counts = {k: "0" for k in guard.COUNTERS}
        counts.update(total=str(len(chosen)), executed=str(len(chosen)),
                      failed="10" if phase == "baseline" else "0",
                      passed="9" if phase == "baseline" else "22")
        ET.SubElement(summary, q("Counters"), **counts)
        results = ET.SubElement(root, q("Results"))
        definitions = ET.SubElement(root, q("TestDefinitions"))
        entries = ET.SubElement(root, q("TestEntries"))
        for index, expected in enumerate(chosen):
            tid = str(uuid.uuid5(uuid.NAMESPACE_URL, expected["name"]))
            eid = str(uuid.uuid5(uuid.NAMESPACE_DNS, phase + str(index)))
            outcome = expected["baseline"] if phase == "baseline" else "Passed"
            result = ET.SubElement(results, q("UnitTestResult"), testId=tid, executionId=eid,
                                   testName=expected["name"], outcome=outcome)
            definition = ET.SubElement(definitions, q("UnitTest"), id=tid,
                                       name=expected["name"], storage=str(self.assembly).lower())
            ET.SubElement(definition, q("Execution"), id=eid)
            ET.SubElement(definition, q("TestMethod"), className=expected["class"],
                          name=expected["method"], codeBase=str(self.assembly))
            ET.SubElement(entries, q("TestEntry"), testId=tid, executionId=eid)
            if outcome == "Failed":
                output = ET.SubElement(result, q("Output"))
                error = ET.SubElement(output, q("ErrorInfo"))
                origin = expected["failure"]
                if expected["method"].startswith("FriendshipRoute"):
                    message = "Assert.Contains() Failure: Item not found in collection"
                elif expected["method"].startswith("RealLineRoute"):
                    message = "The live LINE route must select its source inquiry stylesheet."
                elif expected["method"].startswith("RealClassifier_Owned"):
                    message = "Assert.Single() Failure: The collection was empty"
                else:
                    message = "Assert.Equal() Failure: Values differ\nExpected: 1\nActual:   0"
                ET.SubElement(error, q("Message")).text = message
                ET.SubElement(error, q("StackTrace")).text = (
                    " at " + guard.origin_signature(expected) + " in "
                    + str(self.root / origin["sourcePath"]) + ":line " + str(origin["line"]))
        return root

    def nodes(self, root, container, child):
        return root.find(guard.NS + container).findall(guard.NS + child)

    def verify(self, root, phase="baseline"):
        return guard.verify_trx(ET.tostring(root, encoding="utf-8"), FROZEN, phase, self.assembly, self.root)

    def qualified_run_infos(self, root):
        container = ET.SubElement(root.find(guard.NS + "ResultSummary"), guard.NS + "RunInfos")
        names = [row["name"] for row in FROZEN["rows"] if row["baseline"] == "Failed"]
        self.assertEqual(10, len(names))
        for name in names:
            info = ET.SubElement(container, guard.NS + "RunInfo", outcome="Error")
            ET.SubElement(info, guard.NS + "Text").text = "[xUnit.net 00:01:02.03]     " + name + " [FAIL]"
        return container

    def test_exact_19_negative_and_22_positive_share_case_ids(self):
        baseline, green = self.verify(self.corpus()), self.verify(self.corpus("green"), "green")
        self.assertEqual(19, len(baseline["identities"]))
        self.assertEqual(22, len(green["identities"]))
        self.assertTrue(all(green["identities"][k] == v for k, v in baseline["identities"].items()))

    def test_rejects_xml_and_wrong_phase(self):
        for raw in (b"<bad>", b"<!DOCTYPE x><x/>", b"<!ENTITY x 'y'><x/>"):
            with self.subTest(raw=raw), self.assertRaises(Exception):
                guard.verify_trx(raw, FROZEN, "baseline", self.assembly, self.root)
        with self.assertRaises(guard.Rejected):
            guard.verify_trx(b"<x/>", FROZEN, "other", self.assembly, self.root)
        with self.assertRaises(guard.Rejected):
            self.verify(self.corpus("green"), "baseline")

    def test_rejects_missing_extra_and_malformed_counters(self):
        for field in guard.COUNTERS:
            for value in (None, "-1", "01", "NaN"):
                root = self.corpus()
                counter = root.find(".//" + guard.NS + "Counters")
                if value is None:
                    counter.attrib.pop(field)
                else:
                    counter.set(field, value)
                with self.subTest(field=field, value=value), self.assertRaises(guard.Rejected):
                    self.verify(root)
        root = self.corpus()
        root.find(".//" + guard.NS + "Counters").set("foreign", "0")
        with self.assertRaises(guard.Rejected):
            self.verify(root)

    def test_rejects_each_nonzero_unexpected_counter_and_incomplete_summary(self):
        for field in guard.COUNTERS - {"total", "executed", "failed", "passed"}:
            root = self.corpus()
            root.find(".//" + guard.NS + "Counters").set(field, "1")
            with self.subTest(field=field), self.assertRaises(guard.Rejected):
                self.verify(root)
        for outcome in ("Completed", "Aborted", "InProgress"):
            root = self.corpus()
            root.find(guard.NS + "ResultSummary").set("outcome", outcome)
            with self.subTest(outcome=outcome), self.assertRaises(guard.Rejected):
                self.verify(root)

    def test_rejects_duplicate_missing_extra_results_definitions_entries(self):
        for container, child in (("Results", "UnitTestResult"), ("TestDefinitions", "UnitTest"), ("TestEntries", "TestEntry")):
            for mutation in ("missing", "duplicate", "extra"):
                root = self.corpus()
                parent = root.find(guard.NS + container)
                if mutation == "missing":
                    parent.remove(parent[0])
                else:
                    node = copy.deepcopy(parent[0])
                    if mutation == "extra":
                        node.tag = guard.NS + "Foreign"
                    parent.append(node)
                with self.subTest(container=container, mutation=mutation), self.assertRaises(guard.Rejected):
                    self.verify(root)

    def test_rejects_guid_and_all_join_mutations(self):
        mutations = [
            ("Results", "UnitTestResult", "testId", "not-guid"),
            ("Results", "UnitTestResult", "executionId", str(uuid.uuid4())),
            ("TestDefinitions", "UnitTest", "id", str(uuid.uuid4())),
            ("TestEntries", "TestEntry", "testId", str(uuid.uuid4())),
            ("TestEntries", "TestEntry", "executionId", str(uuid.uuid4())),
        ]
        for container, child, key, value in mutations:
            root = self.corpus()
            self.nodes(root, container, child)[0].set(key, value)
            with self.subTest(container=container, key=key), self.assertRaises(guard.Rejected):
                self.verify(root)
        for attr in ("testId", "executionId"):
            root = self.corpus()
            rows = self.nodes(root, "Results", "UnitTestResult")
            rows[1].set(attr, rows[0].get(attr))
            with self.assertRaises(guard.Rejected):
                self.verify(root)
        root = self.corpus()
        root.set("id", "00000000-0000-0000-0000-000000000000")
        with self.assertRaises(guard.Rejected):
            self.verify(root)

    def test_rejects_names_methods_assembly_and_definition_binding(self):
        for tag, attr, value in (
            ("UnitTestResult", "testName", "Other.Case"),
            ("UnitTestResult", "outcome", "Passed"),
            ("UnitTest", "name", "Other.Case"),
            ("UnitTest", "storage", "/foreign/test.dll"),
            ("TestMethod", "codeBase", "/foreign/test.dll"),
            ("TestMethod", "className", "Other.Class"),
            ("TestMethod", "name", "OtherMethod"),
            ("Execution", "id", str(uuid.uuid4())),
        ):
            root = self.corpus()
            root.find(".//" + guard.NS + tag).set(attr, value)
            with self.subTest(tag=tag, attr=attr), self.assertRaises(guard.Rejected):
                self.verify(root)

    def test_rejects_unknown_error_timeout_setup_and_wrong_origin(self):
        for message in ("System.TimeoutException", "fixture startup failed", "PlaywrightException",
                        "Assert.Single() Failure: The collection was empty", "MSB4166"):
            root = self.corpus()
            root.find(".//" + guard.NS + "Message").text = message
            with self.subTest(message=message), self.assertRaises(guard.Rejected):
                self.verify(root)
        for stack in ("at Other.Class.InitializeAsync()", "at Wrong.Method() in /foreign/File.cs:line 1"):
            root = self.corpus()
            root.find(".//" + guard.NS + "StackTrace").text = stack
            with self.assertRaises(guard.Rejected):
                self.verify(root)
        root = self.corpus()
        root.find(".//" + guard.NS + "StackTrace").text = root.find(".//" + guard.NS + "StackTrace").text.replace(":line 29", ":line 30")
        with self.assertRaises(guard.Rejected):
            self.verify(root)

    def test_rejects_run_info_and_error_on_passing_row(self):
        root = self.corpus()
        ET.SubElement(root.find(guard.NS + "ResultSummary"), guard.NS + "RunInfo")
        with self.assertRaises(guard.Rejected):
            self.verify(root)
        root = self.corpus("green")
        row = self.nodes(root, "Results", "UnitTestResult")[0]
        ET.SubElement(ET.SubElement(row, guard.NS + "Output"), guard.NS + "ErrorInfo")
        with self.assertRaises(guard.Rejected):
            self.verify(root, "green")

    def test_raw_safety_has_no_customer_or_credential_artifact(self):
        guard.safe_raw(b"private@example.test")
        for value in (b"customer@example.com", b"Bearer secret", b"pass" + b"word=secret"):
            with self.subTest(value=value), self.assertRaises(guard.Rejected):
                guard.safe_raw(value)

    def test_empty_run_info_rejects_baseline_and_green_with_fixed_diagnostic(self):
        for phase in ("baseline", "green"):
            root = self.corpus(phase)
            ET.SubElement(root.find(guard.NS + "ResultSummary"), guard.NS + "RunInfo")
            with self.subTest(phase=phase), self.assertRaises(guard.RunInfoRejected) as caught:
                self.verify(root, phase)
            failure = guard.summary_failure(caught.exception)
            self.assertEqual("run-info", failure["category"])
            self.assertEqual(1, failure["runInfo"]["totalNodeCount"])
            self.assertEqual(0, failure["runInfo"]["omittedCount"])

    def test_run_info_counts_only_fixed_known_and_unknown_outcomes(self):
        root = self.corpus()
        summary = root.find(guard.NS + "ResultSummary")
        for outcome in ("Error", "Warning", "Information", "private@example.test"):
            ET.SubElement(summary, guard.NS + "RunInfo", outcome=outcome)
        with self.assertRaises(guard.RunInfoRejected) as caught:
            self.verify(root)
        self.assertEqual({"Error": 1, "Warning": 1, "Information": 1, "unknown": 1},
                         guard.summary_failure(caught.exception)["runInfo"]["outcomeCounts"])

    def test_run_info_diagnostic_never_serializes_sensitive_text_or_attributes(self):
        root = self.corpus()
        info = ET.SubElement(root.find(guard.NS + "ResultSummary"), guard.NS + "RunInfo",
                             outcome="Warning", credential="Bearer secret", customer="private@example.test")
        ET.SubElement(info, guard.NS + "Message", token="pass" + "word=secret").text = \
            "customer@example.com Bearer secret password=secret"
        with self.assertRaises(guard.RunInfoRejected) as caught:
            self.verify(root)
        serialized = json.dumps(guard.summary_failure(caught.exception), sort_keys=True)
        for secret in ("customer@example.com", "private@example.test", "Bearer secret", "pass" + "word=secret"):
            self.assertNotIn(secret, serialized)
        self.assertEqual(1, len(guard.summary_failure(caught.exception)["runInfo"]["messageSamples"]))

    def test_run_info_message_samples_stop_at_eight_and_count_omitted(self):
        root = self.corpus()
        summary = root.find(guard.NS + "ResultSummary")
        for index in range(11):
            info = ET.SubElement(summary, guard.NS + "RunInfo", outcome="Warning")
            ET.SubElement(info, guard.NS + "Message").text = "message " + str(index)
        with self.assertRaises(guard.RunInfoRejected) as caught:
            self.verify(root)
        diagnostic = guard.summary_failure(caught.exception)["runInfo"]
        self.assertEqual(11, diagnostic["totalNodeCount"])
        self.assertEqual(11, diagnostic["outcomeCounts"]["Warning"])
        self.assertEqual(8, len(diagnostic["messageSamples"]))
        self.assertEqual(3, diagnostic["omittedCount"])

    def test_run_info_hashes_decoded_unicode_entity_bytes_not_raw_xml(self):
        root = self.corpus()
        info = ET.SubElement(root.find(guard.NS + "ResultSummary"), guard.NS + "RunInfo", outcome="Error")
        ET.SubElement(info, guard.NS + "Message").text = "ไทย&"
        raw = ET.tostring(root, encoding="utf-8").replace(
            "ไทย".encode("utf-8"), b"&#x0E44;&#x0E17;&#x0E22;")
        with self.assertRaises(guard.RunInfoRejected) as caught:
            guard.verify_trx(raw, FROZEN, "baseline", self.assembly, self.root)
        message = guard.summary_failure(caught.exception)["runInfo"]["messageSamples"][0]
        self.assertEqual(hashlib.sha256("ไทย&".encode("utf-8")).hexdigest(), message["sha256"])
        self.assertEqual(len("ไทย&".encode("utf-8")), message["utf8Length"])

    def test_generic_exceptions_and_forged_diagnostics_cannot_inject_run_info(self):
        for error in (RuntimeError("customer@example.com"), guard.Rejected("run-info")):
            error.diagnostic = {"messages": ["Bearer secret"], "foreign": "pass" + "word=secret"}
            with self.subTest(error=type(error).__name__):
                failure = guard.summary_failure(error)
                self.assertNotIn("runInfo", failure)
                self.assertNotIn("Bearer secret", json.dumps(failure))
        root = self.corpus()
        ET.SubElement(root.find(guard.NS + "ResultSummary"), guard.NS + "RunInfo")
        with self.assertRaises(guard.RunInfoRejected) as caught:
            self.verify(root)
        caught.exception.diagnostic["messageSamples"] = ["Bearer secret"]
        self.assertNotIn("runInfo", guard.summary_failure(caught.exception))

    def test_baseline_run_info_exact_ten_notice_bijection_passes_after_rows(self):
        root = self.corpus()
        self.qualified_run_infos(root)
        proof = self.verify(root)
        self.assertEqual("baseline", proof["phase"])
        self.assertEqual(19, len(proof["identities"]))

    def test_qualified_baseline_notices_never_bypass_case_or_frame_validation(self):
        for mutation, category in (("case", "case-name"), ("outcome", "case-outcome"),
                                   ("message", "error-message"), ("frame", "error-origin")):
            root = self.corpus()
            self.qualified_run_infos(root)
            if mutation == "case":
                root.find(".//" + guard.NS + "UnitTestResult").set("testName", "Other.Case")
            elif mutation == "outcome":
                root.find(".//" + guard.NS + "UnitTestResult").set("outcome", "Passed")
            elif mutation == "message":
                root.find(".//" + guard.NS + "Message").text = "fixture startup failed"
            else:
                frame = root.find(".//" + guard.NS + "StackTrace")
                frame.text = frame.text.replace(":line 29", ":line 290")
            with self.subTest(mutation=mutation), self.assertRaises(guard.Rejected) as caught:
                self.verify(root)
            self.assertEqual(category, str(caught.exception))

    def test_qualified_baseline_rejects_ninth_and_tenth_unsampled_notice_drift(self):
        for index, mutation in ((8, "unexpected"), (9, "unexpected"), (9, "duplicate"),
                                (9, "wrong-outcome"), (9, "nested")):
            root = self.corpus()
            infos = list(self.qualified_run_infos(root))
            if mutation == "unexpected":
                infos[index].find(guard.NS + "Text").text = "[xUnit.net 00:01:02.03]     Other.Case [FAIL]"
            elif mutation == "duplicate":
                infos[index].find(guard.NS + "Text").text = infos[0].find(guard.NS + "Text").text
            elif mutation == "wrong-outcome":
                infos[index].set("outcome", "Warning")
            else:
                ET.SubElement(infos[index].find(guard.NS + "Text"), guard.NS + "Nested")
            with self.subTest(index=index, mutation=mutation), self.assertRaises(guard.RunInfoRejected):
                self.verify(root)

    def test_qualified_baseline_rejects_missing_extra_and_unapproved_attributes(self):
        for mutation in ("missing", "extra", "unknown-attribute", "wrong-container",
                         "direct-child", "foreign-text", "container-attribute", "foreign-element"):
            root = self.corpus()
            container = self.qualified_run_infos(root)
            if mutation == "missing":
                container.remove(container[-1])
            elif mutation == "extra":
                info = ET.SubElement(container, guard.NS + "RunInfo", outcome="Error")
                ET.SubElement(info, guard.NS + "Text").text = "[xUnit.net 00:01:02.03]     Other.Case [FAIL]"
            elif mutation == "unknown-attribute":
                container[-1].set("customer", "private@example.test")
            elif mutation == "wrong-container":
                container.tag = guard.NS + "OtherContainer"
            elif mutation == "direct-child":
                moved = container[-1]
                container.remove(moved)
                root.find(guard.NS + "ResultSummary").append(moved)
            elif mutation == "foreign-text":
                container[-1].tail = "unowned message"
            elif mutation == "container-attribute":
                container.set("customer", "private@example.test")
            else:
                ET.SubElement(container, guard.NS + "Other")
            with self.subTest(mutation=mutation), self.assertRaises(guard.RunInfoRejected):
                self.verify(root)

    def test_qualified_baseline_allows_only_nonidentity_run_info_metadata(self):
        root = self.corpus()
        container = self.qualified_run_infos(root)
        container[0].set("computerName", "build-host")
        container[0].set("timestamp", "2026-10-16T00:00:00.0000000Z")
        self.assertEqual(19, len(self.verify(root)["identities"]))

    def test_green_run_info_remains_immediate_rejection_even_when_notices_match(self):
        root = self.corpus("green")
        self.qualified_run_infos(root)
        root.find(".//" + guard.NS + "UnitTestResult").set("testName", "Other.Case")
        with self.assertRaises(guard.RunInfoRejected):
            self.verify(root, "green")

    def test_foreign_namespace_run_info_is_never_silently_ignored(self):
        for tag in ("RunInfo", "{urn:foreign}RunInfo"):
            root = self.corpus()
            ET.SubElement(root.find(guard.NS + "ResultSummary"), tag, outcome="Error")
            with self.subTest(tag=tag), self.assertRaises(guard.RunInfoRejected):
                self.verify(root)

    def test_baseline_run_info_duplicate_and_missing_are_counted_without_acceptance(self):
        root = self.corpus()
        name = next(row["name"] for row in FROZEN["rows"] if row["baseline"] == "Failed")
        for _ in range(2):
            info = ET.SubElement(root.find(guard.NS + "ResultSummary"), guard.NS + "RunInfo", outcome="Error")
            ET.SubElement(info, guard.NS + "Text").text = "[xUnit.net 01:02:03.04]     " + name + " [FAIL]"
        with self.assertRaises(guard.RunInfoRejected) as caught:
            self.verify(root)
        actual = guard.summary_failure(caught.exception)["runInfo"]["baselineNotices"]
        self.assertEqual((2, 1, 1, 9), (actual["matchedNoticeCount"], actual["uniqueExpectedCount"],
                                      actual["duplicateCount"], actual["missingExpectedCount"]))

    def test_baseline_run_info_unexpected_wrong_outcome_and_malformed_are_distinct(self):
        root = self.corpus()
        summary = root.find(guard.NS + "ResultSummary")
        known = next(row["name"] for row in FROZEN["rows"] if row["baseline"] == "Failed")
        cases = (("Error", "Other.UnfrozenCase", True), ("Warning", known, True),
                 ("Error", known, False))
        for outcome, name, simple in cases:
            info = ET.SubElement(summary, guard.NS + "RunInfo", outcome=outcome)
            text = ET.SubElement(info, guard.NS + "Text")
            text.text = "[xUnit.net 00:00:00.00]     " + name + " [FAIL]"
            if not simple:
                ET.SubElement(text, guard.NS + "Nested").text = "secret"
        with self.assertRaises(guard.RunInfoRejected) as caught:
            self.verify(root)
        actual = guard.summary_failure(caught.exception)["runInfo"]["baselineNotices"]
        self.assertEqual((0, 0, 0, 10, 1, 1, 1), tuple(actual[key] for key in
                         ("matchedNoticeCount", "uniqueExpectedCount", "duplicateCount", "missingExpectedCount",
                          "unexpectedCount", "wrongOutcomeCount", "malformedCount")))

    def test_baseline_run_info_rejects_malformed_prefix_suffix_and_child_shape(self):
        name = next(row["name"] for row in FROZEN["rows"] if row["baseline"] == "Failed")
        for message in ("[xUnit.net 00:00:00.00]    " + name + " [FAIL]",
                        "[xUnit.net 00:00:00.00]     " + name + " [FAIL] extra",
                        "[xUnit.net 00:00:60.00]     " + name + " [FAIL]",
                        "prefix [xUnit.net 00:00:00.00]     " + name + " [FAIL]",
                        "[xUnit.net 00:00:00.00]     " + name + " [FAIL]\n"):
            root = self.corpus()
            info = ET.SubElement(root.find(guard.NS + "ResultSummary"), guard.NS + "RunInfo", outcome="Error")
            ET.SubElement(info, guard.NS + "Text").text = message
            with self.subTest(message=message), self.assertRaises(guard.RunInfoRejected) as caught:
                self.verify(root)
            self.assertEqual(1, guard.summary_failure(caught.exception)["runInfo"]
                             ["baselineNotices"]["malformedCount"])
        root = self.corpus()
        info = ET.SubElement(root.find(guard.NS + "ResultSummary"), guard.NS + "RunInfo", outcome="Error")
        ET.SubElement(info, "Text").text = "[xUnit.net 00:00:00.00]     " + name + " [FAIL]"
        with self.assertRaises(guard.RunInfoRejected) as caught:
            self.verify(root)
        self.assertEqual(1, guard.summary_failure(caught.exception)["runInfo"]["baselineNotices"]["malformedCount"])
        root = self.corpus()
        info = ET.SubElement(root.find(guard.NS + "ResultSummary"), guard.NS + "RunInfo", outcome="Error")
        info.text = "unowned plaintext"
        ET.SubElement(info, guard.NS + "Text").text = "[xUnit.net 00:00:00.00]     " + name + " [FAIL]"
        with self.assertRaises(guard.RunInfoRejected) as caught:
            self.verify(root)
        self.assertEqual(1, guard.summary_failure(caught.exception)["runInfo"]["baselineNotices"]["malformedCount"])

    def test_baseline_run_info_escaped_display_name_contract_is_exact(self):
        self.assertEqual("A\\r\\n\\t\\0B", guard.escape_reporter_name("A\r\n\t\0B"))
        self.assertEqual("เพิ่มเพื่อน MALIEV", guard.escape_reporter_name("เพิ่มเพื่อน MALIEV"))

    def test_green_run_info_does_not_claim_baseline_correspondence(self):
        root = self.corpus("green")
        info = ET.SubElement(root.find(guard.NS + "ResultSummary"), guard.NS + "RunInfo", outcome="Error")
        ET.SubElement(info, guard.NS + "Text").text = "[xUnit.net 00:00:00.00]     Other.Case [FAIL]"
        with self.assertRaises(guard.RunInfoRejected) as caught:
            self.verify(root, "green")
        self.assertNotIn("baselineNotices", guard.summary_failure(caught.exception)["runInfo"])

    def test_run_info_classifier_never_emits_names_or_message_plaintext(self):
        root = self.corpus()
        name = next(row["name"] for row in FROZEN["rows"] if row["baseline"] == "Failed")
        info = ET.SubElement(root.find(guard.NS + "ResultSummary"), guard.NS + "RunInfo",
                             outcome="Error", customer="private@example.test")
        ET.SubElement(info, guard.NS + "Text").text = "[xUnit.net 00:00:00.00]     " + name + " [FAIL]"
        with self.assertRaises(guard.RunInfoRejected) as caught:
            self.verify(root)
        serialized = json.dumps(guard.summary_failure(caught.exception), ensure_ascii=False)
        for secret in (name, "private@example.test", "[xUnit.net", " [FAIL]"):
            self.assertNotIn(secret, serialized)

    def test_run_info_forged_classifier_counters_are_not_copied(self):
        root = self.corpus()
        ET.SubElement(root.find(guard.NS + "ResultSummary"), guard.NS + "RunInfo")
        with self.assertRaises(guard.RunInfoRejected) as caught:
            self.verify(root)
        caught.exception.diagnostic["baselineNotices"] = {"matchedNoticeCount": "secret"}
        self.assertNotIn("runInfo", guard.summary_failure(caught.exception))
        caught.exception.diagnostic["baselineNotices"] = None
        self.assertNotIn("runInfo", guard.summary_failure(caught.exception))


class SourceAndLifecycle(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.evidence = self.root / "evidence"
        self.evidence.mkdir()
        self.frozen = copy.deepcopy(FROZEN)
        self.saved, self.old = {}, {}
        for path in guard.PRODUCERS + guard.TESTS:
            data = ("candidate:" + path).encode()
            destination = self.root / path
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(data)
            self.saved[path] = data
            self.frozen["sources"]["candidate"][path] = guard.digest(data)
        for path in guard.PRODUCERS:
            self.old[path] = ("baseline:" + path).encode()
            self.frozen["sources"]["baseline"][path] = guard.digest(self.old[path])
        self.runner = unittest.mock.Mock()
        self.runner.run.return_value = (0, b"")

    def phase(self, root, directory, frozen, phase, runner):
        names = [r["name"] for r in frozen["rows"] if phase == "green" or r["baseline"] is not None]
        return {"phase": phase, "identities": {n: str(uuid.uuid5(uuid.NAMESPACE_URL, n)) for n in names}}

    def run_proof(self):
        with patch.object(guard, "trx_phase", side_effect=self.phase):
            return guard.execute(self.root, self.evidence, self.frozen, self.runner, self.old.__getitem__)

    def assert_restored(self):
        for path, data in self.saved.items():
            self.assertEqual(data, (self.root / path).read_bytes())

    def test_success_restores_both_producers_and_never_changes_tests(self):
        result = self.run_proof()
        self.assertEqual("green", result["green"]["phase"])
        self.assert_restored()
        self.assertFalse(result["fullSuiteAcceptance"])
        self.assertEqual(["baseline-build", "restored-candidate-build"], [c.args[0] for c in self.runner.run.call_args_list])

    def test_finally_restore_on_command_failure_and_timeout(self):
        for category in ("command-timeout", "build-zero-warnings-errors"):
            self.runner.reset_mock()
            self.runner.run.side_effect = [guard.Rejected(category), (0, b"")]
            with self.subTest(category=category), self.assertRaises(guard.Rejected):
                self.run_proof()
            self.assert_restored()
            receipt = json.loads((self.evidence / "receipt.json").read_text())
            self.assertEqual(category, receipt["primaryFailure"]["category"])

    def test_primary_and_cleanup_failure_both_preserved(self):
        self.runner.run.side_effect = [guard.Rejected("command-timeout"), guard.Rejected("rebuild-failed")]
        with self.assertRaises(guard.Rejected):
            self.run_proof()
        receipt = json.loads((self.evidence / "receipt.json").read_text())
        self.assertEqual("command-timeout", receipt["primaryFailure"]["category"])
        self.assertEqual(["rebuild:Rejected"], receipt["cleanupFailureTypes"])
        self.assert_restored()

    def test_restore_attempts_second_file_after_first_cleanup_error(self):
        original = guard.replace
        failed = []
        def replace(root, path, data):
            if data == self.saved[guard.PRODUCERS[0]] and not failed:
                failed.append(True)
                raise OSError("synthetic restoration refusal")
            original(root, path, data)
        self.runner.run.side_effect = guard.Rejected("primary-failed")
        with patch.object(guard, "replace", side_effect=replace), self.assertRaises(guard.Rejected):
            self.run_proof()
        self.assertEqual(self.saved[guard.PRODUCERS[1]], (self.root / guard.PRODUCERS[1]).read_bytes())
        receipt = json.loads((self.evidence / "receipt.json").read_text())
        self.assertEqual("primary-failed", receipt["primaryFailure"]["category"])
        self.assertIn("restore:OSError", receipt["cleanupFailureTypes"])

    def test_rejects_drift_in_each_source_or_test_before_execution(self):
        for path in guard.PRODUCERS + guard.TESTS:
            target = self.root / path
            target.write_bytes(b"drift")
            with self.subTest(path=path), self.assertRaises(guard.Rejected):
                guard.check_sources(self.root, self.frozen["sources"]["candidate"])
            target.write_bytes(self.saved[path])

    def test_frozen_contract_is_read_without_dynamic_roster_inference(self):
        self.assertEqual(22, len(guard.contract(ROOT)["rows"]))

    def test_contract_hash_and_duplicate_json_keys_refused(self):
        path = self.root / guard.CONTRACT
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b'{"changed":true}')
        with self.assertRaises(guard.Rejected):
            guard.contract(self.root)
        with self.assertRaises(guard.Rejected):
            guard.no_duplicates([("a", 1), ("a", 2)])

    def test_links_foreign_paths_and_missing_files_refused(self):
        for path in ("/foreign/file", "../foreign", "missing"):
            with self.subTest(path=path), self.assertRaises(Exception):
                guard.read(self.root, path)
        target = self.root / "link"
        target.symlink_to(self.root / guard.PRODUCERS[0])
        with self.assertRaises(guard.Rejected):
            guard.read(self.root, "link")

    def test_cross_phase_case_identity_cannot_change(self):
        def phase(*args):
            result = self.phase(*args)
            if args[3] == "green":
                first = next(r["name"] for r in self.frozen["rows"] if r["baseline"] is not None)
                result["identities"][first] = str(uuid.uuid4())
            return result
        with patch.object(guard, "trx_phase", side_effect=phase), self.assertRaises(guard.Rejected):
            guard.execute(self.root, self.evidence, self.frozen, self.runner, self.old.__getitem__)
        self.assert_restored()

    def test_green_checkpoint_copies_only_verified_raw_and_refuses_reuse(self):
        raw_dir = self.evidence / "raw-green"
        raw_dir.mkdir()
        (raw_dir / "green.trx").write_bytes(b"<synthetic/>")
        guard.publish_green(self.root, self.evidence)
        self.assertEqual(b"<synthetic/>", (self.root / "line-contact-candidate-test-results/line-contact-candidate.trx").read_bytes())
        with self.assertRaises(FileExistsError):
            guard.publish_green(self.root, self.evidence)

    def test_wrong_baseline_source_never_overlays_and_restores_candidate(self):
        self.old[guard.PRODUCERS[0]] = b"foreign baseline"
        with self.assertRaises(guard.Rejected):
            self.run_proof()
        self.assert_restored()

    def test_test_drift_after_green_is_rejected_without_repairing_test_bytes(self):
        def phase(*args):
            result = self.phase(*args)
            if args[3] == "green":
                (self.root / guard.TESTS[0]).write_bytes(b"test drift")
            return result
        with patch.object(guard, "trx_phase", side_effect=phase), self.assertRaises(guard.Rejected):
            guard.execute(self.root, self.evidence, self.frozen, self.runner, self.old.__getitem__)
        self.assertEqual(b"test drift", (self.root / guard.TESTS[0]).read_bytes())
        self.assertTrue(all((self.root / p).read_bytes() == self.saved[p] for p in guard.PRODUCERS))

    def test_refuses_local_before_subprocess_or_network(self):
        with patch.dict(os.environ, {"GITHUB_ACTIONS": "false"}, clear=True), patch.object(guard.subprocess, "Popen") as launch:
            with self.assertRaises(guard.Rejected):
                guard.main()
            launch.assert_not_called()


class HostedBindings(unittest.TestCase):
    def test_owned_event_head_and_cwd_fences(self):
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            root, temp = base / "repo", base / "temp"
            root.mkdir()
            temp.mkdir()
            path = temp / "event.json"
            event = {"number": 477, "repository": {"full_name": guard.REPOSITORY},
                     "pull_request": {"head": {"sha": "2" * 40, "repo": {"full_name": guard.REPOSITORY}},
                                      "base": {"repo": {"full_name": guard.REPOSITORY}}}}
            environment = {"GITHUB_ACTIONS": "true", "RUNNER_ENVIRONMENT": "github-hosted", "RUNNER_OS": "Linux",
                           "GITHUB_REPOSITORY": guard.REPOSITORY, "GITHUB_WORKSPACE": str(root),
                           "GITHUB_SERVER_URL": "https://github.com", "GITHUB_API_URL": "https://api.github.com",
                           "GITHUB_EVENT_PATH": str(path), "RUNNER_TEMP": str(temp), "GITHUB_EVENT_NAME": "pull_request",
                           "LINE_CONTACT_HEAD_SHA": "2" * 40, "GITHUB_SHA": "0" * 40, "GITHUB_REF": "refs/pull/477/merge"}
            path.write_text(json.dumps(event))
            git = lambda arg: "https://github.com/" + guard.REPOSITORY if arg == "origin" else "0" * 40 if arg == "HEAD" else "1" * 40
            with patch.object(guard.Path, "cwd", return_value=root):
                self.assertEqual("2" * 40, guard.hosted(root, environment, git)["candidateSha"])
                for key, value in (("GITHUB_REPOSITORY", "foreign/repo"), ("RUNNER_ENVIRONMENT", "self-hosted"),
                                   ("GITHUB_WORKSPACE", str(base)), ("GITHUB_REF", "refs/pull/478/merge"),
                                   ("GITHUB_SHA", "3" * 40), ("LINE_CONTACT_HEAD_SHA", "4" * 40),
                                   ("GH_HOST", "foreign.example"), ("GITHUB_SERVER_URL", "https://foreign.example")):
                    changed = dict(environment, **{key: value})
                    with self.subTest(key=key), self.assertRaises(guard.Rejected):
                        guard.hosted(root, changed, git)
                event["pull_request"]["head"]["repo"]["full_name"] = "foreign/repo"
                path.write_text(json.dumps(event))
                with self.assertRaises(guard.Rejected):
                    guard.hosted(root, environment, git)


class OwnedCommands(unittest.TestCase):
    def simulate(self, timeout=False, returncode=0, output=b"0 Warning(s)\n0 Error(s)", cleanup_failure=False):
        with tempfile.TemporaryDirectory() as temporary:
            process = unittest.mock.Mock(pid=42)
            process.wait.return_value = returncode
            def launch(command, **kwargs):
                kwargs["stdout"].write(output)
                return process
            def kill(group, sig):
                if sig == 0:
                    raise ProcessLookupError()
                if cleanup_failure:
                    raise PermissionError("synthetic")
            class FakeSession:
                def __init__(self, leader, token):
                    self.members = {42: {"pid": 42, "startTicks": 123, "sid": 42, "pgid": 42}}
                def observe(self):
                    return None
                def stop(self):
                    failures = []
                    for sig in (signal.SIGTERM, signal.SIGKILL):
                        try:
                            kill(42, sig)
                        except BaseException as error:
                            failures.append(guard.summary_failure(error))
                    return failures
                def finish(self):
                    try:
                        kill(42, 0)
                    except ProcessLookupError:
                        return
                    raise guard.Rejected("observed-session-still-present")
            runner = guard.Runner(Path(temporary), Path(temporary))
            clock = [0, 999, 1000, 1001] if timeout else [0, 1000, 1001]
            with patch.object(guard, "Session", FakeSession), \
                 patch.object(guard.subprocess, "Popen", side_effect=launch) as popen, \
                 patch.object(guard.shutil, "which", return_value="/usr/bin/dotnet"), \
                 patch.object(guard, "birth", return_value=(42, 42, 123)), \
                 patch.object(guard.os, "readlink", return_value="/usr/bin/dotnet"), \
                 patch.object(guard.os, "waitid", return_value=None if timeout else object()), \
                 patch.object(guard.os, "killpg", side_effect=kill) as terminate, \
                 patch.object(guard.time, "sleep"), \
                 patch.object(guard.time, "monotonic", side_effect=clock):
                try:
                    result = runner.run("unit-command", ["dotnet", "never-executed"], 10, build=True)
                    error = None
                except guard.Rejected as rejected:
                    result, error = None, rejected
                self.assertTrue(popen.call_args.kwargs["start_new_session"])
                self.assertEqual([42] * len(terminate.call_args_list), [c.args[0] for c in terminate.call_args_list])
                self.assertEqual("1", popen.call_args.kwargs["env"]["MSBUILDDISABLENODEREUSE"])
                process.wait.assert_called_once()
                return result, error, runner.records

    def test_cancelled_owner_never_launches_rebuild_or_other_command(self):
        with tempfile.TemporaryDirectory() as temporary, patch.object(guard.subprocess, "Popen") as launch:
            runner = guard.Runner(Path(temporary), Path(temporary))
            runner.cancelled = True
            with self.assertRaises(guard.Rejected):
                runner.run("refused", ["never-executed"], 1, build=True)
            launch.assert_not_called()

    def test_owned_session_and_zero_warning_build(self):
        result, error, records = self.simulate()
        self.assertIsNone(error)
        self.assertEqual(0, result[0])
        self.assertEqual(123, records[0]["startTicks"])

    def test_timeout_cleanup_and_primary_record(self):
        _, error, records = self.simulate(timeout=True)
        self.assertIsNotNone(error)
        self.assertTrue(records[0]["timedOut"])
        self.assertEqual("command-timeout", records[0]["primaryFailure"]["category"])

    def test_nonzero_warning_exit_and_cleanup_rejected(self):
        for arguments in ({"output": b"1 Warning(s)\n0 Error(s)"}, {"returncode": 1}, {"cleanup_failure": True}):
            with self.subTest(arguments=arguments):
                _, error, _ = self.simulate(**arguments)
                self.assertIsNotNone(error)


class V2Controls(unittest.TestCase):
    def corpus(self):
        harness = TrxControls()
        harness.setUp()
        self.addCleanup(harness.doCleanups)
        return harness

    def lifecycle(self):
        harness = SourceAndLifecycle()
        harness.setUp()
        self.addCleanup(harness.doCleanups)
        return harness

    def test_origin_29_never_matches_290_or_noncanonical_decimal(self):
        h = self.corpus()
        for replacement in (":line 290", ":line 029", ":line 29suffix"):
            root = h.corpus()
            node = root.find(".//" + guard.NS + "StackTrace")
            node.text = node.text.replace(":line 29", replacement)
            with self.subTest(replacement=replacement), self.assertRaises(guard.Rejected):
                h.verify(root)

    def test_origin_namespace_path_method_signature_and_split_frame(self):
        h = self.corpus()
        root = h.corpus()
        original = root.find(".//" + guard.NS + "StackTrace").text
        changes = [
            original.replace("Legacy.Maliev.Web.Tests.", "Foreign.Legacy.Maliev.Web.Tests."),
            original.replace(str(h.root), "/foreign" + str(h.root)),
            original.replace(".cs:line", ".cs.backup:line"),
            original.replace("FriendshipRoute_SelectsSourceInquiryAssets(", "WrongMethod("),
            original.replace("(String route)", "()"),
            original.replace(" in ", "\n in "),
        ]
        for text in changes:
            root = h.corpus()
            root.find(".//" + guard.NS + "StackTrace").text = text
            with self.subTest(text=text), self.assertRaises(guard.Rejected):
                h.verify(root)

    def test_origin_stack_and_frame_bounds(self):
        h = self.corpus()
        for value in ("x" * 32769, "\n".join(["x"] * 129), "x" * 6145):
            root = h.corpus()
            root.find(".//" + guard.NS + "StackTrace").text = value
            with self.assertRaises(guard.Rejected):
                h.verify(root)

    def test_single_actual_xml_encoding_and_dtd_entity_refusal(self):
        h = self.corpus()
        text = ET.tostring(h.corpus(), encoding="unicode")
        for raw in (
            text.encode("utf-16le"), text.encode("utf-16be"), text.encode("utf-16"),
            text.encode("utf-32"), b"\x00<x/>",
            ('<?xml version="1.0" encoding="utf-16"?>' + text).encode(),
            ('<!DOCTYPE TestRun [<!ENTITY x "synthetic">]>' + text).encode(),
            ('<!ENTITY x "synthetic">' + text).encode(),
            text.replace("Assert.Contains", "&unknown;").encode(),
        ):
            with self.subTest(prefix=raw[:12]), self.assertRaises(Exception):
                guard.verify_trx(raw, FROZEN, "baseline", h.assembly, h.root)
        valid = b"\xef\xbb\xbf" + text.encode("utf-8")
        self.assertEqual(19, len(guard.verify_trx(valid, FROZEN, "baseline", h.assembly, h.root)["identities"]))

    def test_quoted_escaped_unicode_xml_entity_credentials(self):
        examples = [
            b'{"access_token":"synthetic"}', b'{"CLIENT_' + b'SECRET' + b'" : "synthetic"}',
            br'{\"password\":\"synthetic\"}', br'{"\u0061ccess_token":"synthetic"}',
            b'{&quot;access_token&quot;:&quot;synthetic&quot;}',
            b'{"&#97;ccess_token":"synthetic"}', br'{"x":"customer\u0040example.com"}',
        ]
        for raw in examples:
            with self.subTest(raw=raw), self.assertRaises(guard.Rejected):
                guard.safe_raw(raw)
        nested = '{"access_token":"synthetic"}'
        for _ in range(8):
            nested = json.dumps(nested)
        with self.assertRaises(guard.Rejected):
            guard.safe_raw(nested.encode())

    def test_invalid_wrong_origin_unsafe_raw_never_persists(self):
        h = self.corpus()
        evidence = h.root / "proof"
        evidence.mkdir()
        h.assembly.parent.mkdir(parents=True)
        h.assembly.write_bytes(b"synthetic assembly")
        good = h.corpus()
        wrong = h.corpus()
        node = wrong.find(".//" + guard.NS + "StackTrace")
        node.text = node.text.replace(":line 29", ":line 290")
        unsafe = h.corpus()
        ET.SubElement(unsafe.find(guard.NS + "ResultSummary"), guard.NS + "StdOut").text = '{"access_token":"synthetic"}'
        for raw in (b"<bad>", ET.tostring(wrong), ET.tostring(unsafe)):
            runner = unittest.mock.Mock()
            def run(name, command, timeout):
                directory = Path(command[command.index("--results-directory") + 1])
                (directory / "actual.trx").write_bytes(raw)
                return 1, b""
            runner.run.side_effect = run
            with self.assertRaises(Exception):
                guard.trx_phase(h.root, evidence, FROZEN, "baseline", runner)
            self.assertFalse((evidence / "raw-baseline").exists())
            self.assertEqual([], list(h.root.glob(".line-contact-owned-*")))

    def test_runner_primary_cleanup_and_evidence_io_preserved(self):
        h = OwnedCommands()
        with patch.object(guard.Path, "write_text", side_effect=OSError("access_token=synthetic")):
            _, error, _ = h.simulate(timeout=True, cleanup_failure=True)
        self.assertIsInstance(error, guard.ProofFailure)
        summary = str(error)
        self.assertIn("command-timeout", summary)
        self.assertIn("evidence-io", summary)
        self.assertIn("PermissionError", summary)
        self.assertNotIn("access_token=synthetic", summary)

    def test_receipt_io_does_not_replace_primary_or_restore_failure(self):
        h = self.lifecycle()
        h.runner.run.side_effect = [guard.Rejected("command-timeout"), guard.Rejected("rebuild-failed")]
        with patch.object(guard.Path, "write_text", side_effect=OSError("private payload")), self.assertRaises(guard.ProofFailure) as captured:
            h.run_proof()
        summary = str(captured.exception)
        self.assertIn("command-timeout", summary)
        self.assertIn("rebuild:Rejected", summary)
        self.assertIn("evidence-io", summary)
        self.assertNotIn("private payload", summary)
        h.assert_restored()

    def test_signal_handlers_precede_first_git_and_defer_only_registration(self):
        for spawning in (False, True):
            with self.subTest(spawning=spawning), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                callbacks, seen = {}, []
                def install(sig, handler):
                    callbacks[sig] = handler
                    return signal.SIG_DFL
                def run(runner, name, command, timeout, build=False):
                    self.assertIn(signal.SIGTERM, callbacks)
                    self.assertIn(signal.SIGINT, callbacks)
                    seen.append(name)
                    runner.spawning = spawning
                    if spawning:
                        callbacks[signal.SIGTERM](signal.SIGTERM, None)
                        self.assertTrue(runner.cancelled)
                        runner.spawning = False
                        raise guard.Rejected("owned-run-interrupted")
                    callbacks[signal.SIGTERM](signal.SIGTERM, None)
                def hosted(root, environment, git):
                    return git("HEAD")
                environment = {"GITHUB_ACTIONS": "true", "RUNNER_ENVIRONMENT": "github-hosted",
                               "GITHUB_REPOSITORY": guard.REPOSITORY, "GITHUB_WORKSPACE": str(root)}
                with patch.dict(os.environ, environment, clear=True), patch.object(guard.Path, "cwd", return_value=root), \
                     patch.object(guard.signal, "signal", side_effect=install), patch.object(guard, "hosted", side_effect=hosted), \
                     patch.object(guard.Runner, "run", autospec=True, side_effect=run), patch.object(guard.subprocess, "Popen") as launch:
                    with self.assertRaises(guard.ProofFailure):
                        guard.main()
                    launch.assert_not_called()
                self.assertEqual(["git-head"], seen)

    def session(self):
        rows = {
            42: {"pid": 42, "ppid": 1, "pgid": 42, "sid": 42, "startTicks": 123, "state": "R", "executable": "/owned/dotnet"},
            43: {"pid": 43, "ppid": 42, "pgid": 99, "sid": 42, "startTicks": 124, "state": "R", "executable": "/owned/node"},
        }
        session = guard.Session(42, (42, 42, 123))
        actual_send = session.send
        def graceful_fixture(pid, sig):
            actual_send(pid, sig)
            if sig == signal.SIGTERM and pid in rows:
                rows[pid]["state"] = "Z"
        session.send = graceful_fixture
        return session, rows

    def test_same_sid_moved_group_is_observed_and_exact_pid_stopped(self):
        session, rows = self.session()
        with patch.object(session, "pids", return_value=list(rows)), \
             patch.object(session, "process", side_effect=lambda pid: copy.deepcopy(rows.get(pid))), \
             patch.object(guard.os, "kill") as stop, patch.object(guard.time, "sleep"):
            session.observe()
            failures = session.stop()
            self.assertFalse(failures)
            self.assertEqual({42, 43}, {call.args[0] for call in stop.call_args_list})
            self.assertIn(43, session.members)
            rows.clear()
            session.finish()

    def test_pid_reuse_executable_change_and_observed_escape_refused(self):
        for change in ({"startTicks": 999}, {"executable": "/foreign/exe"}, {"sid": 777}):
            session, rows = self.session()
            with patch.object(session, "pids", return_value=list(rows)), \
                 patch.object(session, "process", side_effect=lambda pid: copy.deepcopy(rows.get(pid))), \
                 patch.object(guard.os, "kill") as stop:
                session.observe()
                rows[43].update(change)
                with self.subTest(change=change), self.assertRaises(guard.Rejected):
                    session.observe()
                if "sid" not in change:
                    with self.assertRaises(guard.Rejected):
                        session.send(43, signal.SIGTERM)
                    stop.assert_not_called()


class V3ForcedKillControls(unittest.TestCase):
    def test_forced_child_kill_rejects_even_with_exit_zero_and_verified_absence(self):
        for forced in (False, True):
            with self.subTest(forced=forced), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                rows = {
                    42: {"pid": 42, "ppid": 1, "pgid": 42, "sid": 42, "startTicks": 123,
                         "state": "Z", "executable": None},
                    43: {"pid": 43, "ppid": 42, "pgid": 99, "sid": 42, "startTicks": 124,
                         "state": "R", "executable": "/owned/child"},
                }
                session = guard.Session(42, (42, 42, 123))
                process = unittest.mock.Mock(pid=42)
                def wait(timeout):
                    rows.pop(42, None)
                    return 0
                process.wait.side_effect = wait
                def launch(command, **kwargs):
                    kwargs["stdout"].write(b"0 Warning(s)\n0 Error(s)")
                    return process
                def kill(pid, sig):
                    self.assertEqual(43, pid)
                    if sig == signal.SIGKILL or (sig == signal.SIGTERM and not forced):
                        rows.pop(pid, None)
                runner = guard.Runner(root, root)
                with patch.object(guard, "Session", return_value=session), \
                     patch.object(session, "pids", side_effect=lambda: list(rows)), \
                     patch.object(session, "process", side_effect=lambda pid: copy.deepcopy(rows.get(pid))), \
                     patch.object(guard.subprocess, "Popen", side_effect=launch), \
                     patch.object(guard.shutil, "which", return_value="/owned/dotnet"), \
                     patch.object(guard, "birth", return_value=(42, 42, 123)), \
                     patch.object(guard.os, "kill", side_effect=kill) as signals, \
                     patch.object(guard.os, "waitid", return_value=object()), patch.object(guard.time, "sleep"):
                    if forced:
                        with self.assertRaises(guard.ProofFailure) as caught:
                            runner.run("forced-child-control", ["dotnet", "never-executed"], 10, build=True)
                        self.assertIn("forced-kill-used", str(caught.exception))
                    else:
                        self.assertEqual(0, runner.run("graceful-control", ["dotnet", "never-executed"], 10, build=True)[0])
                self.assertEqual(0, runner.records[0]["exitCode"])
                self.assertTrue(runner.records[0]["observedSessionAbsent"])
                self.assertEqual(forced, runner.records[0].get("forcedKillUsed", False))
                self.assertEqual(forced, any(call.args[1] == signal.SIGKILL for call in signals.call_args_list))
                self.assertEqual({}, rows)


if __name__ == "__main__":
    unittest.main()
