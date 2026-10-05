import importlib.util
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

spec = importlib.util.spec_from_file_location("pricing_control", Path(__file__).parents[1] / "scripts/verify-comparison-callback-regression.py")
control = importlib.util.module_from_spec(spec)
spec.loader.exec_module(control)


class Guards(unittest.TestCase):
    def fixture(self, mutate=None):
        root = ET.Element("TestRun", xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
        summary = ET.SubElement(root, "ResultSummary")
        ET.SubElement(summary, "Counters", total="4", executed="4", failed="4", passed="0", notExecuted="0", error="0")
        results = ET.SubElement(root, "Results")
        for method in control.METHODS:
            for status in ("Pending", "Completed"):
                row = ET.SubElement(results, "UnitTestResult", testName=f"{control.TEST_CLASS}.{method}(gatedStatus: {status})", outcome="Failed")
                output = ET.SubElement(row, "Output")
                error = ET.SubElement(output, "ErrorInfo")
                ET.SubElement(error, "Message").text = ("Assert.False() Failure" if "AbsoluteDeadline" in method
                                                        else "Assert.NotNull() Failure: Value is null")
        if mutate:
            mutate(root)
        return root

    def verify(self, root):
        with tempfile.TemporaryDirectory(prefix="maliev-pricing-control-") as directory:
            path = Path(directory) / "control.trx"
            ET.ElementTree(root).write(path, encoding="utf-8")
            return control.verify_negative_trx(path)

    def test_accepts_exact_expected_failure_corpus(self):
        self.assertEqual(4, self.verify(self.fixture())["expectedFailures"])

    def test_rejects_skipped_control(self):
        with self.assertRaises(RuntimeError):
            self.verify(self.fixture(lambda root: root.find("ResultSummary/Counters").set("notExecuted", "1")))

    def test_rejects_unexpected_counter(self):
        with self.assertRaises(RuntimeError):
            self.verify(self.fixture(lambda root: root.find("ResultSummary/Counters").set("error", "1")))

    def test_rejects_passing_result(self):
        with self.assertRaises(RuntimeError):
            self.verify(self.fixture(lambda root: root.find("Results/UnitTestResult").set("outcome", "Passed")))

    def test_rejects_unrelated_failure(self):
        with self.assertRaises(RuntimeError):
            self.verify(self.fixture(lambda root: setattr(root.find("Results/UnitTestResult/Output/ErrorInfo/Message"), "text", "System.TimeoutException")))

    def test_refuses_local_execution_before_network_or_sdk(self):
        with patch.dict(os.environ, {"GITHUB_ACTIONS": "false"}), patch.object(control.subprocess, "run") as execute, patch.object(control.subprocess, "check_output") as fetch:
            with self.assertRaisesRegex(RuntimeError, "owned hosted Web workspace"):
                control.main()
            execute.assert_not_called()
            fetch.assert_not_called()

    def test_rejects_duplicate_row(self):
        def duplicate(root):
            rows = root.findall("Results/UnitTestResult")
            rows[1].set("testName", rows[0].get("testName"))
        with self.assertRaises(RuntimeError):
            self.verify(self.fixture(duplicate))

    def test_rejects_wrong_status(self):
        def wrong_status(root):
            row = root.find("Results/UnitTestResult")
            row.set("testName", row.get("testName").replace("Pending", "Unavailable"))
        with self.assertRaises(RuntimeError):
            self.verify(self.fixture(wrong_status))

    def test_rejects_unqualified_method(self):
        def unqualified(root):
            row = root.find("Results/UnitTestResult")
            row.set("testName", row.get("testName").replace(control.TEST_CLASS + ".", ""))
        with self.assertRaises(RuntimeError):
            self.verify(self.fixture(unqualified))

    def test_rejects_nonzero_build_warning_summary(self):
        completed = control.subprocess.CompletedProcess([], 0, "0 Warning(s)\n1 Warning(s)\n0 Error(s)")
        with tempfile.TemporaryDirectory(prefix="maliev-pricing-build-") as directory:
            with patch.object(control.subprocess, "run", return_value=completed), self.assertRaises(RuntimeError):
                control.run(Path(directory), Path(directory), "guard-build", ["never-executed"], build=True)


if __name__ == "__main__":
    unittest.main()
