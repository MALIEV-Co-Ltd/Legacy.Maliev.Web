"""Parser controls only; synthetic XML and marker files are not native test evidence."""
import copy
import importlib.util
from pathlib import Path
import tempfile
import unittest
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("focused_gate", ROOT / "scripts/verify-focused-web-trx.py")
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)
NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
FIELDS = "total executed passed failed error timeout aborted inconclusive passedButRunAborted notRunnable notExecuted disconnected warning completed inProgress pending".split()


class FocusedGateControls(unittest.TestCase):
    def fixture(self, directory, suite):
        dll = directory / "Legacy.Maliev.Web.Tests.dll"
        dll.write_bytes(b"Synthetic parser control marker, not a native assembly")
        root = ET.Element(NS + "TestRun")
        summary = ET.SubElement(root, NS + "ResultSummary", outcome="Completed")
        total = 59 if suite == "fdm" else 10
        ET.SubElement(summary, NS + "Counters", {k: str(total) if k in ("total", "executed", "passed") else "0" for k in FIELDS})
        results = ET.SubElement(root, NS + "Results")
        definitions = ET.SubElement(root, NS + "TestDefinitions")
        entries = ET.SubElement(root, NS + "TestEntries")
        for cls, methods in gate.ROSTERS[suite].items():
            for method, rows in methods.items():
                for index in range(rows):
                    if suite == "sidebar":
                        culture, width = [(c, w) for c in ("en", "th") for w in (320, 375, 820, 992, 1280)][index]
                        name = f'{cls}.{method}(culture: "{culture}", width: {width})'
                    else:
                        name = f"{cls}.{method}(syntheticControl: {index})"
                    test, execution = str(uuid.uuid4()), str(uuid.uuid4())
                    definition = ET.SubElement(definitions, NS + "UnitTest", id=test, name=name, storage=str(dll).lower())
                    ET.SubElement(definition, NS + "Execution", id=execution)
                    ET.SubElement(definition, NS + "TestMethod", className=cls, name=method, codeBase=str(dll))
                    ET.SubElement(results, NS + "UnitTestResult", testId=test, executionId=execution, testName=name, outcome="Passed")
                    ET.SubElement(entries, NS + "TestEntry", testId=test, executionId=execution)
        return root, dll

    def verify(self, root, directory, dll, suite):
        path = directory / "focused.trx"
        ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)
        return gate.verify(path, suite, dll)

    def test_exact_frozen_roster_counts(self):
        self.assertEqual([10, 10, 9, 26, 4], [
            sum(gate.ROSTERS["fdm"]["Legacy.Maliev.Web.Tests." + cls].values())
            for cls in ("FdmSimulationModelPathsTests", "FdmSimulationSupportTests",
                        "FdmSimulationScenarioTests", "FdmSimulationProfileTests",
                        "FdmOriginalServiceEligibilityBoundaryTests")])
        self.assertEqual(10, sum(next(iter(gate.ROSTERS["sidebar"].values())).values()))

    def test_both_complete_suites_accept_lowercase_native_storage(self):
        for suite, total in (("fdm", 59), ("sidebar", 10)):
            with self.subTest(suite=suite), tempfile.TemporaryDirectory() as temp:
                directory = Path(temp)
                root, dll = self.fixture(directory, suite)
                self.assertEqual(total, self.verify(root, directory, dll, suite)["passed"])

    def test_every_missing_counter_and_dirty_nonpass_counter_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            baseline, dll = self.fixture(directory, "fdm")
            for field in FIELDS:
                with self.subTest(missing=field):
                    root = copy.deepcopy(baseline)
                    del root.find(NS + "ResultSummary/" + NS + "Counters").attrib[field]
                    with self.assertRaises(ValueError):
                        self.verify(root, directory, dll, "fdm")
                with self.subTest(dirty=field):
                    root = copy.deepcopy(baseline)
                    root.find(NS + "ResultSummary/" + NS + "Counters").set(field, "58" if field in ("total", "executed", "passed") else "1")
                    with self.assertRaises(ValueError):
                        self.verify(root, directory, dll, "fdm")

    def test_identity_join_outcome_namespace_roster_and_assembly_rejections(self):
        mutations = {
            "namespace": lambda r: setattr(r, "tag", "TestRun"),
            "summary": lambda r: r.find(NS + "ResultSummary").set("outcome", "Aborted"),
            "missing-result": lambda r: r.find(NS + "Results").remove(r.find(NS + "Results")[0]),
            "extra-definition": lambda r: ET.SubElement(r.find(NS + "TestDefinitions"), NS + "UnitTest"),
            "duplicate-test-id": lambda r: r.find(NS + "Results")[1].set("testId", r.find(NS + "Results")[0].get("testId")),
            "duplicate-execution-id": lambda r: r.find(NS + "Results")[1].set("executionId", r.find(NS + "Results")[0].get("executionId")),
            "entry-join": lambda r: r.find(NS + "TestEntries")[0].set("executionId", str(uuid.uuid4())),
            "definition-execution": lambda r: r.find(NS + "TestDefinitions")[0].find(NS + "Execution").set("id", str(uuid.uuid4())),
            "outcome": lambda r: r.find(NS + "Results")[0].set("outcome", "Failed"),
            "unknown-class": lambda r: r.find(NS + "TestDefinitions")[0].find(NS + "TestMethod").set("className", "Foreign.Tests"),
            "unknown-method": lambda r: r.find(NS + "TestDefinitions")[0].find(NS + "TestMethod").set("name", "ForeignMethod"),
            "definition-name": lambda r: r.find(NS + "TestDefinitions")[0].set("name", "ForeignName"),
            "foreign-codebase": lambda r: r.find(NS + "TestDefinitions")[0].find(NS + "TestMethod").set("codeBase", "Foreign.dll"),
            "foreign-storage": lambda r: r.find(NS + "TestDefinitions")[0].set("storage", "Foreign.dll"),
        }
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            baseline, dll = self.fixture(directory, "fdm")
            for label, mutate in mutations.items():
                with self.subTest(label=label):
                    root = copy.deepcopy(baseline)
                    mutate(root)
                    with self.assertRaises(ValueError):
                        self.verify(root, directory, dll, "fdm")

    def test_unknown_sidebar_viewport_cannot_replace_required_row(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            root, dll = self.fixture(directory, "sidebar")
            result = root.find(NS + "Results")[0]
            changed = result.get("testName").replace("width: 320", "width: 999")
            result.set("testName", changed)
            root.find(NS + "TestDefinitions")[0].set("name", changed)
            with self.assertRaises(ValueError):
                self.verify(root, directory, dll, "sidebar")


if __name__ == "__main__":
    unittest.main()
