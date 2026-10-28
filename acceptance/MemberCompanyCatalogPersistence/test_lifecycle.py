"""Exercise the actual workflow validator with source-bound synthetic TRX metadata."""
import ast
import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import textwrap
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = '.github/workflows/member-company-catalog-persistence.yml'
SOURCE = 'Legacy.Maliev.Web.Tests/BillingProofLifetimeTests.cs'
ACCEPTED = 'a25cf00409b9df2c7164c24f0a399b0752242e2d'
FROZEN = '725e0cad317d8ff7b8dc73d752737f9793c77ac5'
NS = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'


def validator(workflow):
    block = re.search(r'      - name: Require all [^\n]+ exact unique passed lifecycle control rows\n        run: \|\n(.*?)(?=      - name:)', workflow, re.S)
    if not block:
        raise AssertionError('Actual lifecycle validator block required')
    program = re.search(r"python3 -B - <<'PY'\n(.*?)\nPY(?:\n|$)", textwrap.dedent(block.group(1)), re.S)
    if not program:
        raise AssertionError('Bounded offline Python validator required')
    return program.group(1)


def literal_names(program):
    assignment = next(node for node in ast.parse(program).body
                      if isinstance(node, ast.Assign) and any(isinstance(target, ast.Name) and target.id == 'expected' for target in node.targets))
    return ast.literal_eval(assignment.value)


def accepted_names():
    source = subprocess.check_output(['git', 'show', ACCEPTED + ':' + SOURCE], cwd=ROOT, timeout=30).decode('utf-8')
    names = set()
    pattern = r'((?:[ \t]*\[(?:Theory|Fact|InlineData)[^\r\n]*\]\r?\n)+)[ \t]*public (?:async )?(?:Task|void) (\w+)\(([^)]*)\)'
    for attributes, method, parameters in re.findall(pattern, source):
        prefix = 'Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.' + method
        arguments = re.findall(r'\[InlineData\((.*?)\)\]', attributes)
        if not arguments:
            names.add(prefix)
            continue
        parameter_names = [parameter.strip().split()[-1] for parameter in parameters.split(',')]
        for argument in arguments:
            values = json.loads('[' + argument + ']')
            fields = [name + ': ' + (str(value) if isinstance(value, bool) else json.dumps(value))
                      for name, value in zip(parameter_names, values, strict=True)]
            names.add(prefix + '(' + ', '.join(fields) + ')')
    if len(names) != 50:
        raise AssertionError('Exact accepted source must define fifty lifecycle rows')
    return names


class LifecycleValidatorControls(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.program = validator((ROOT / WORKFLOW).read_text())
        frozen = subprocess.check_output(['git', 'show', FROZEN + ':' + WORKFLOW], cwd=ROOT, timeout=30).decode('utf-8')
        cls.original = literal_names(validator(frozen))
        cls.names = accepted_names()
        if len(cls.original) != 31 or not cls.original < cls.names or len(cls.names - cls.original) != 19:
            raise AssertionError('All thirty-one originals plus nineteen accepted additions required')

    def run_validator(self, names=None, outcome='Passed', counter=None):
        root = ET.Element('{' + NS + '}TestRun')
        results = ET.SubElement(root, '{' + NS + '}Results')
        selected = sorted(self.names) if names is None else names
        for index, name in enumerate(selected):
            ET.SubElement(results, '{' + NS + '}UnitTestResult', testName=name,
                          testId=f'{index + 1:08x}-0000-0000-0000-000000000001',
                          executionId=f'{index + 1:08x}-0000-0000-0000-000000000002', outcome=outcome if index == 0 else 'Passed')
        summary = ET.SubElement(root, '{' + NS + '}ResultSummary')
        counts = dict.fromkeys(['failed', 'error', 'timeout', 'aborted', 'inconclusive', 'passedButRunAborted',
                               'notRunnable', 'notExecuted', 'disconnected', 'warning', 'completed', 'inProgress', 'pending'], '0')
        counts.update(total=str(len(selected)), executed=str(len(selected)), passed=str(len(selected)))
        if counter:
            counts.update(counter)
        ET.SubElement(summary, '{' + NS + '}Counters', **counts)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'billing-lifetime-test-results'
            path.mkdir()
            ET.ElementTree(root).write(path / 'control.trx', encoding='utf-8', xml_declaration=True)
            return subprocess.run([sys.executable, '-B', '-c', self.program], cwd=directory,
                                  capture_output=True, text=True, timeout=30)

    def test_accept_all_original_and_accepted_successor_rows(self):
        result = self.run_validator()
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_exact_roster_retains_all_originals_and_nineteen_source_bound_additions(self):
        self.assertEqual(literal_names(self.program), self.names)

    def test_reject_missing_original(self):
        self.assertNotEqual(self.run_validator(sorted(self.names - {min(self.original)})).returncode, 0)

    def test_reject_missing_successor(self):
        self.assertNotEqual(self.run_validator(sorted(self.names - {min(self.names - self.original)})).returncode, 0)

    def test_reject_original_only_inventory(self):
        self.assertNotEqual(self.run_validator(sorted(self.original)).returncode, 0)

    def test_reject_duplicate_row(self):
        names = sorted(self.names)
        names[-1] = names[0]
        self.assertNotEqual(self.run_validator(names).returncode, 0)

    def test_reject_foreign_row(self):
        names = sorted(self.names)
        names[-1] = 'Foreign.Case'
        self.assertNotEqual(self.run_validator(names).returncode, 0)

    def test_reject_failed_or_skipped_row(self):
        for outcome in ['Failed', 'NotExecuted']:
            with self.subTest(outcome=outcome):
                self.assertNotEqual(self.run_validator(outcome=outcome).returncode, 0)

    def test_reject_counter_mismatch(self):
        for counter in [{'total': '31'}, {'executed': '49'}, {'passed': '49'}, {'failed': '1'}, {'notExecuted': '1'}, {'pending': '1'}]:
            with self.subTest(counter=counter):
                self.assertNotEqual(self.run_validator(counter=counter).returncode, 0)


if __name__ == '__main__':
    unittest.main()
