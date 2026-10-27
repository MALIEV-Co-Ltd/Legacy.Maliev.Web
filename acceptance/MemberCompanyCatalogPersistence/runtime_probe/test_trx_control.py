import unittest
from trx_control import verify

class TrxControls(unittest.TestCase):
    def document(self):
        return b'''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results><UnitTestResult testName="N.Probe.ObserveHttpAssemblyWithoutBackend" testId="11111111-1111-1111-1111-111111111111" executionId="22222222-2222-2222-2222-222222222222" outcome="Passed"/></Results><TestDefinitions><UnitTest id="11111111-1111-1111-1111-111111111111" name="N.Probe.ObserveHttpAssemblyWithoutBackend"><Execution id="22222222-2222-2222-2222-222222222222"/><TestMethod className="N.Probe" name="ObserveHttpAssemblyWithoutBackend" codeBase="/safe/tests.dll"/></UnitTest></TestDefinitions><TestEntries><TestEntry testId="11111111-1111-1111-1111-111111111111" executionId="22222222-2222-2222-2222-222222222222"/></TestEntries><ResultSummary><Counters total="1" executed="1" passed="1" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0"/></ResultSummary></TestRun>'''
    def test_only_exact_single_passed_test_and_assembly_qualify(self):
        raw=self.document();self.assertEqual(1,verify(raw,'N.Probe','/safe/tests.dll')['passed'])
        for bad in (raw.replace(b'outcome="Passed"',b'outcome="Failed"'),raw.replace(b'passed="1"',b'passed="0"'),raw.replace(b'/safe/tests.dll',b'/foreign/tests.dll'),raw.replace(b'className="N.Probe"',b'className="N.Other"'),raw.replace(b'executionId="22222222-2222-2222-2222-222222222222"',b'executionId="33333333-3333-3333-3333-333333333333"',1),b'<!DOCTYPE x>'+raw,raw*2000):
            with self.subTest(bad=bad[:40]),self.assertRaises(ValueError):verify(bad,'N.Probe','/safe/tests.dll')

if __name__=='__main__':unittest.main()
