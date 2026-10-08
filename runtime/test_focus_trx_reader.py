import copy,json,pathlib,tempfile,unittest,uuid,xml.etree.ElementTree as ET
import verify_focus_trx as v
class ReaderControls(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.root=pathlib.Path(self.tmp.name)
        self.expected=json.loads(pathlib.Path(__file__).with_name('expected-cases.json').read_text())
        for g,names in self.expected.items():
            d=self.root/g;d.mkdir();root=ET.Element('{'+v.NS['t']+'}TestRun');s=ET.SubElement(root,'{'+v.NS['t']+'}ResultSummary',outcome='Completed');c={k:'0' for k in v.COUNTERS};c.update(total=str(len(names)),executed=str(len(names)),passed=str(len(names)));ET.SubElement(s,'{'+v.NS['t']+'}Counters',**c)
            results=ET.SubElement(root,'{'+v.NS['t']+'}Results')
            for n in names:ET.SubElement(results,'{'+v.NS['t']+'}UnitTestResult',testName=n,executionId=str(uuid.uuid4()),testId='shared-definition-id',outcome='Passed')
            ET.ElementTree(root).write(d/'results.trx',encoding='utf-8',xml_declaration=True)
    def tearDown(self):self.tmp.cleanup()
    def edit(self,fn):
        p=self.root/'failed-boundaries/results.trx';t=ET.parse(p);fn(t);t.write(p,encoding='utf-8')
    def test_exact59_pass_shared_test_definition_ids_allowed(self):self.assertTrue(v.verify(self.root,self.expected)['accepted'])
    def test_missing_group_rejected(self):
        (self.root/'preservation-controls/results.trx').unlink();self.assertFalse(v.verify(self.root,self.expected)['accepted'])
    def test_duplicate_execution_id_rejected(self):
        def change(t):
            rows=t.findall('.//t:UnitTestResult',v.NS);rows[1].set('executionId',rows[0].get('executionId'))
        self.edit(change);self.assertFalse(v.verify(self.root,self.expected)['accepted'])
    def test_wrong_membership_same_count_rejected(self):
        self.edit(lambda t:t.find('.//t:UnitTestResult',v.NS).set('testName','foreign.test'));self.assertFalse(v.verify(self.root,self.expected)['accepted'])
    def test_failed_execution_retained_and_rejected(self):
        def change(t):
            t.find('.//t:UnitTestResult',v.NS).set('outcome','Failed');c=t.find('.//t:Counters',v.NS);c.set('passed','2');c.set('failed','1')
        self.edit(change);r=v.verify(self.root,self.expected);self.assertFalse(r['accepted']);self.assertEqual(1,len(r['groups']['failed-boundaries']['failures']))
    def test_skipped_execution_rejected(self):
        self.edit(lambda t:t.find('.//t:UnitTestResult',v.NS).set('outcome','NotExecuted'));self.assertFalse(v.verify(self.root,self.expected)['accepted'])
    def test_counter_mismatch_rejected(self):
        self.edit(lambda t:t.find('.//t:Counters',v.NS).set('total','4'));self.assertFalse(v.verify(self.root,self.expected)['accepted'])
    def test_foreign_trx_rejected(self):
        (self.root/'foreign.trx').write_text('<unexpected/>');self.assertFalse(v.verify(self.root,self.expected)['accepted'])
    def test_missing_counter_rejected(self):
        self.edit(lambda t:t.find('.//t:Counters',v.NS).attrib.pop('disconnected'));self.assertFalse(v.verify(self.root,self.expected)['accepted'])
    def test_disconnected_counter_rejected(self):
        self.edit(lambda t:t.find('.//t:Counters',v.NS).set('disconnected','1'));self.assertFalse(v.verify(self.root,self.expected)['accepted'])
    def test_plus_prefixed_counter_rejected(self):
        self.edit(lambda t:t.find('.//t:Counters',v.NS).set('total','+3'));self.assertFalse(v.verify(self.root,self.expected)['accepted'])
    def test_blank_execution_id_rejected(self):
        self.edit(lambda t:t.find('.//t:UnitTestResult',v.NS).set('executionId',''));self.assertFalse(v.verify(self.root,self.expected)['accepted'])
    def test_execution_reuse_across_groups_rejected(self):
        a=ET.parse(self.root/'preservation-controls/results.trx').find('.//t:UnitTestResult',v.NS).get('executionId');self.edit(lambda t:t.find('.//t:UnitTestResult',v.NS).set('executionId',a));self.assertFalse(v.verify(self.root,self.expected)['accepted'])
    def test_case_variant_execution_reuse_rejected(self):
        a=ET.parse(self.root/'preservation-controls/results.trx').find('.//t:UnitTestResult',v.NS).get('executionId');self.edit(lambda t:t.find('.//t:UnitTestResult',v.NS).set('executionId',a.upper()));self.assertFalse(v.verify(self.root,self.expected)['accepted'])
if __name__=='__main__':unittest.main()
