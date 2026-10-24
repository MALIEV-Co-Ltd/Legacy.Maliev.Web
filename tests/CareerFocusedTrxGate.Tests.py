"""Synthetic parser controls; these do not constitute executed Career evidence."""
import copy
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("career_gate", ROOT / "scripts/verify-career-focused-trx.py")
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)
NS = gate.gate.NS


class CareerFocusedControls(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.directory = Path(self.tmp.name)
        self.assembly = self.directory / "SyntheticParserMarker.dll"
        self.assembly.write_bytes(b"Parser marker only; not a native assembly")

    def fixture(self, suite):
        names = gate.roster(suite)
        root = ET.Element(NS + "TestRun")
        summary = ET.SubElement(root, NS + "ResultSummary", outcome="Completed")
        ET.SubElement(summary, NS + "Counters", {key: str(len(names)) if key in ("total", "executed", "passed") else "0" for key in gate.gate.FIELDS})
        results = ET.SubElement(root, NS + "Results")
        definitions = ET.SubElement(root, NS + "TestDefinitions")
        entries = ET.SubElement(root, NS + "TestEntries")
        for name in names:
            cls, method = name.split("(", 1)[0].rsplit(".", 1)
            test, execution = str(uuid.uuid4()), str(uuid.uuid4())
            definition = ET.SubElement(definitions, NS + "UnitTest", id=test,
                                       name=gate.gate.native_definition_display(name), storage=str(self.assembly).lower())
            ET.SubElement(definition, NS + "Execution", id=execution)
            ET.SubElement(definition, NS + "TestMethod", className=cls, name=method, codeBase=str(self.assembly))
            ET.SubElement(results, NS + "UnitTestResult", testId=test, executionId=execution, testName=name, outcome="Passed")
            ET.SubElement(entries, NS + "TestEntry", testId=test, executionId=execution)
        return root

    def verify(self, root, suite="consumer"):
        path = self.directory / "focus.trx"
        ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)
        return gate.verify(path, suite, self.assembly)

    def test_exact_consumer240_and_original4_joined_rows_pass(self):
        for suite, count in (("consumer", 240), ("original", 4)):
            with self.subTest(suite=suite):
                self.assertEqual(count, self.verify(self.fixture(suite), suite)["passed"])

    def test_retained213_and_new27_identity_partition_is_fixed(self):
        names = gate.roster("consumer")
        new = [name for name in names if name.startswith((
            "Legacy.Maliev.Web.Tests.CareerPageCountCompatibilityTests.",
            "Legacy.Maliev.Web.Tests.CareerDetailLevelCompatibilityTests."))]
        self.assertEqual(27, len(new))
        self.assertEqual(213, len(set(names) - set(new)))
        self.assertEqual(24, sum("Detail_ActualTypedClient_ResolvesOriginalSeparateLevelLookup(" in name for name in names))

    def test_parameter_substitution_is_rejected_even_with_valid_native_joins(self):
        root = self.fixture("consumer")
        result = next(row for row in root.find(NS + "Results") if "(" in row.attrib["testName"])
        name = result.attrib["testName"].split("(", 1)[0] + '(differentParameter: "unreviewed")'
        result.set("testName", name)
        definition = next(row for row in root.find(NS + "TestDefinitions") if row.attrib["id"] == result.attrib["testId"])
        definition.set("name", gate.gate.native_definition_display(name))
        with self.assertRaisesRegex(ValueError, "parameterized execution identities"):
            self.verify(root)

    def test_missing_duplicate_failed_skipped_and_extra_rows_refuse(self):
        for mutation in ("missing", "duplicate", "failed", "skipped", "extra"):
            root = self.fixture("consumer")
            rows = root.find(NS + "Results")
            if mutation == "missing": rows.remove(rows[0])
            if mutation == "duplicate": rows[1].attrib.update(rows[0].attrib)
            if mutation == "failed": rows[0].set("outcome", "Failed")
            if mutation == "skipped": rows[0].set("outcome", "NotExecuted")
            if mutation == "extra": rows.append(copy.deepcopy(rows[0]))
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                self.verify(root)

    def test_counter_definition_execution_and_assembly_mutations_refuse(self):
        for mutation in ("counter", "definition", "execution", "entry", "assembly"):
            root = self.fixture("original")
            definition = root.find(NS + "TestDefinitions")[0]
            if mutation == "counter": root.find(NS + "ResultSummary/" + NS + "Counters").set("warning", "1")
            if mutation == "definition": definition.set("name", "unbound")
            if mutation == "execution": definition.find(NS + "Execution").set("id", str(uuid.uuid4()))
            if mutation == "entry": root.find(NS + "TestEntries")[0].set("testId", str(uuid.uuid4()))
            if mutation == "assembly": definition.find(NS + "TestMethod").set("codeBase", str(self.directory / "Foreign.dll"))
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                self.verify(root, "original")

    def test_infrastructure_notices_and_nested_rows_refuse(self):
        for mutation in ("RunInfo", "ErrorInfo", "TestRun"):
            root = self.fixture("consumer")
            ET.SubElement(root, NS + mutation)
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                self.verify(root)

    def test_changed_manifest_refuses_before_candidate_qualification(self):
        with patch.object(Path, "read_bytes", return_value=b"{}"):
            with self.assertRaisesRegex(ValueError, "manifest changed"):
                gate.roster("consumer")


if __name__ == "__main__":
    unittest.main()
