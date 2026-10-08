import copy
import datetime
import tempfile
import unittest
from pathlib import Path
from retain import PREFIX, NS, rows, receipt, cleanup
import xml.etree.ElementTree as ET

class RejectionControls(unittest.TestCase):
    def valid(self):
        return {'surface':'member-company-update','culture':'en','width':1280,'candidateHead':'a'*40,'companyId':1,'selectedCompany':'Synthetic Company Limited','selectedTaxId':'0123456789012','catalogStatus':200,'saveStatus':302,'readbackStatus':200,'companyReadbackStatus':200,'reloadVerified':True,'contactsAndAddressIdentitiesPreserved':True,'manualRegistrarPreserved':True,'deniedLookupStatus':403,'deniedWriteStatus':403,'deniedWriteUnchanged':True,'unavailableStatus':503,'rateLimitedStatus':429,'manualFallbackEditable':True,'syntheticUpstreamOnly':True,'observations':[{'logicalUri':'https://data.creden.co/sapi/search/get_suggestion','physicalLoopback':True,'method':'POST','typeSearch':'prefix','query':prefix+' en','language':'en','status':status} for prefix,status in [('Synthetic',200),('Unavailable',503),('RateLimited',429)]]}
    def test_accept_control_metadata_only(self):
        self.assertEqual(receipt(self.valid(),'en','a'*40)['companyId'],1)
    def test_reject_foreign_candidate(self):
        with self.assertRaises(ValueError): receipt(self.valid(),'en','b'*40)
    def test_reject_unknown_public_field(self):
        value=self.valid(); value['credential']='synthetic-never-retained'
        with self.assertRaises(ValueError): receipt(value,'en','a'*40)
    def test_reject_provider_redirect_origin(self):
        value=self.valid(); value['observations'][0]['logicalUri']='http://127.0.0.1/'
        with self.assertRaises(ValueError): receipt(value,'en','a'*40)
    def test_reject_retry_or_cache_masked_attempt(self):
        value=self.valid(); value['observations'].append(copy.deepcopy(value['observations'][0]))
        with self.assertRaises(ValueError): receipt(value,'en','a'*40)
    def test_reject_missing_permission_no_mutation_proof(self):
        value=self.valid(); value['deniedWriteUnchanged']=False
        with self.assertRaises(ValueError): receipt(value,'en','a'*40)
    def test_reject_partial_native_success(self):
        with tempfile.TemporaryDirectory() as directory:
            root=ET.Element('{'+NS['t']+'}TestRun'); results=ET.SubElement(root,'{'+NS['t']+'}Results')
            for culture,width,outcome in [('en',1280,'Passed'),('th',375,'Failed')]:
                ET.SubElement(results,'{'+NS['t']+'}UnitTestResult', testName=PREFIX+f'(culture: "{culture}", width: {width})',outcome=outcome)
            path=Path(directory)/'control.trx'; ET.ElementTree(root).write(path)
            with self.assertRaises(ValueError): rows(path)
    def test_reject_unproven_fallback(self):
        value=self.valid(); value['manualFallbackEditable']=False
        with self.assertRaises(ValueError): receipt(value,'en','a'*40)

class CleanupRejectionControls(unittest.TestCase):
    def valid(self):
        now = datetime.datetime.now(datetime.timezone.utc)
        run = 'a'*32; cid = 'b'*64; expiry = (now+datetime.timedelta(minutes=50)).isoformat()
        original = {'id':cid,'createdUtc':now.isoformat(),'imageId':'sha256:'+'c'*64,'name':'/billing-proof-'+run+'-postgres','owner':'web-billing-proof','run':run,'expiresUtc':expiry,'configuredImage':'postgres:18-alpine','envelopeValid':True,'initEnabled':True,'envelopeSignatureSha256':'d'*64,'memoryBytes':536870912,'swapBytes':536870912,'nanoCpus':1000000000,'tmpfs':'/var/lib/postgresql:rw,size=268435456','loopbackPorts':True,'persistentData':False}
        backend = {'backend':'postgres','localEndpoint':'unix:///var/run/docker.sock','databaseSynthetic':True,'name':'billing-proof-'+run+'-postgres','database':'profile_contract_'+'e'*32,'owner':'web-billing-proof','run':run,'expiresUtc':expiry,'daemon':'synthetic-daemon','originalId':cid,'baselineAbsent':True,'startDispatched':True,'startupSettled':True,'captureVerified':True,'absenceVerified':True,'sdkReleased':True,'original':original}
        return {'owner':'web-billing-proof','run':run,'expiresUtc':(now+datetime.timedelta(minutes=30)).isoformat(),'released':True,'readerFailureRejected':False,'retained':False,'expired':False,'expiryFailure':None,'policy':'Same-owner 1800-second expiry task closes admission, frontends and actual case work before dependent cleanup; unresolved ownership is quarantined, never accepted as release.','hosts':[{'startupComplete':True,'disposal':'RanToCompletion'} for _ in range(11)],'children':[{'dispatched':True,'identityCaptured':True,'exitVerified':True,'noChildVerified':False,'readersClosed':True,'released':True,'process':{}} for _ in range(3)],'backends':[backend]}
    def test_accept_cleanup_metadata_control(self):
        self.assertTrue(cleanup(self.valid())['released'])
    def test_reject_omitted_provider_or_frontend(self):
        value=self.valid();value['hosts'].pop()
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_unstarted_provider(self):
        value=self.valid();value['hosts'][0]['startupComplete']=False
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_undispatched_child(self):
        value=self.valid();value['children'][0]['dispatched']=False
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_foreign_backend_run(self):
        value=self.valid();value['backends'][0]['run']='f'*32
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_unapproved_backend_public_field(self):
        value=self.valid();value['backends'][0]['credential']='synthetic-never-retained'
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_invalid_owner_deadline(self):
        value=self.valid();value['expiresUtc']='invalid'
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_foreign_original_id(self):
        value=self.valid();value['backends'][0]['original']['id']='f'*64
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_backend_expiry_replacement(self):
        value=self.valid();value['backends'][0]['expiresUtc']=value['expiresUtc']
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_missing_original_image(self):
        value=self.valid();value['backends'][0]['original']['imageId']=''
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_native_pending_counter(self):
        self.native_fault('pending')
    def test_reject_native_missing_identity(self):
        self.native_fault('testId')
    def test_reject_native_duplicate_identity(self):
        self.native_fault('duplicate')
    def native_fault(self, fault):
        with tempfile.TemporaryDirectory() as directory:
            root=ET.Element('{'+NS['t']+'}TestRun');results=ET.SubElement(root,'{'+NS['t']+'}Results')
            for index,(culture,width) in enumerate([('en',1280),('th',375)]):
                identity=('1' if index == 0 or fault == 'duplicate' else '2')*8+'-1111-1111-1111-'+'1'*12
                ET.SubElement(results,'{'+NS['t']+'}UnitTestResult',testName=PREFIX+f'(culture: "{culture}", width: {width})',outcome='Passed',testId='' if fault == 'testId' else identity,executionId=identity)
            counters={key:'0' for key in ['failed','error','timeout','aborted','inconclusive','passedButRunAborted','notRunnable','notExecuted','disconnected','warning','completed','inProgress','pending']};counters.update(total='2',executed='2',passed='2')
            if fault == 'pending':counters['pending']='1'
            ET.SubElement(root,'{'+NS['t']+'}Counters',**counters)
            path=Path(directory)/'control.trx';ET.ElementTree(root).write(path)
            with self.assertRaises(ValueError):rows(path)

if __name__ == '__main__': unittest.main()
