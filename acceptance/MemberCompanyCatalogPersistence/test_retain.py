import copy
import datetime
import tempfile
import unittest
from pathlib import Path
from retain import PREFIX, NS, rows, receipt, cleanup, original_run, helper_source, HELPER_SHA256
import xml.etree.ElementTree as ET

class RejectionControls(unittest.TestCase):
    def valid(self):
        return {'surface':'member-company-update','culture':'en','width':1280,'candidateHead':'a'*40,'companyId':1,'selectedCompany':'Synthetic Company Limited','selectedTaxId':'0123456789012','catalogStatus':200,'saveStatus':302,'readbackStatus':200,'companyReadbackStatus':200,'reloadVerified':True,'contactsAndAddressIdentitiesPreserved':True,'manualRegistrarPreserved':True,'deniedLookupStatus':403,'deniedWriteStatus':403,'deniedWriteUnchanged':True,'unavailableStatus':503,'rateLimitedStatus':429,'manualFallbackEditable':True,'syntheticUpstreamOnly':True,'fixtureNullEmailRejectedStatus':400,'fixturePreparedStatus':204,'preparedFixtureEmailPreserved':True,'runtimeRetryPolicy':{'clientName':'catalog','runtimeDefaultsRevision':'3c790ba6414b2a539f24aabb6948549ffd81a86b','handlerCount':2,'standardOptionsKey':'catalog-standard','sharedMaxRetryAttempts':3,'runtimePackageSourceRevision':'02107c65bab30aad9e35b5133ed643eaa77bccd8','confirmedFromActualHandlerChain':True},'browserRequests':[{'query':prefix+' en','language':'en','count':1} for prefix in ['Synthetic','Unavailable','RateLimited']],'observations':[{'logicalUri':'https://data.creden.co/sapi/search/get_suggestion','physicalLoopback':True,'method':'POST','typeSearch':'prefix','query':prefix+' en','language':'en','status':status} for prefix,status in [('Synthetic',200),('Unavailable',503),('RateLimited',429)]]}
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
    def test_reject_unqualified_fixture_preparation(self):
        value=self.valid(); value['fixturePreparedStatus']=200
        with self.assertRaises(ValueError): receipt(value,'en','a'*40)
    def test_reject_unproven_fallback(self):
        value=self.valid(); value['manualFallbackEditable']=False
        with self.assertRaises(ValueError): receipt(value,'en','a'*40)

    def test_accept_observed_retry_counts_metadata_only(self):
        value=self.valid();success,unavailable,limited=value['observations']
        value['observations']=[success]+[copy.deepcopy(unavailable) for _ in range(5)]+[copy.deepcopy(limited) for _ in range(4)]
        self.assertEqual(len(receipt(value,'en','a'*40)['observations']),10)
    def test_accept_exact_runtime_bound_metadata_only(self):
        value=self.valid();success,unavailable,limited=value['observations']
        value['observations']=[success]+[copy.deepcopy(unavailable) for _ in range(16)]+[copy.deepcopy(limited) for _ in range(16)]
        self.assertEqual(len(receipt(value,'en','a'*40)['observations']),33)
    def test_reject_one_phase_over_runtime_bound(self):
        value=self.valid();success,unavailable,limited=value['observations']
        value['observations']=[success]+[copy.deepcopy(unavailable) for _ in range(17)]+[limited]
        with self.assertRaises(ValueError):receipt(value,'en','a'*40)
    def test_reject_missing_first_actual_provider429(self):
        value=self.valid();value['observations'].pop()
        with self.assertRaises(ValueError):receipt(value,'en','a'*40)
    def test_reject_missing_first_actual_provider503(self):
        value=self.valid();value['observations'].pop(1)
        with self.assertRaises(ValueError):receipt(value,'en','a'*40)
    def test_reject_noncontiguous_duplicate_failure_group(self):
        value=self.valid();value['observations'].append(copy.deepcopy(value['observations'][1]))
        with self.assertRaises(ValueError):receipt(value,'en','a'*40)
    def test_reject_duplicate_success_before_failure(self):
        value=self.valid();value['observations'].insert(1,copy.deepcopy(value['observations'][0]))
        with self.assertRaises(ValueError):receipt(value,'en','a'*40)
    def test_reject_cross_culture_provider_attempt(self):
        value=self.valid();value['observations'][1]['language']='th'
        with self.assertRaises(ValueError):receipt(value,'en','a'*40)
    def test_reject_unknown_failure_query(self):
        value=self.valid();value['observations'][1]['query']='Foreign en'
        with self.assertRaises(ValueError):receipt(value,'en','a'*40)
    def test_reject503_as_first429_substitute(self):
        value=self.valid();value['observations'][2]['status']=503
        with self.assertRaises(ValueError):receipt(value,'en','a'*40)
    def test_reject_source_only_runtime_policy(self):
        value=self.valid();value['runtimeRetryPolicy']['confirmedFromActualHandlerChain']=False
        with self.assertRaises(ValueError):receipt(value,'en','a'*40)
    def test_reject_wrong_handler_count(self):
        value=self.valid();value['runtimeRetryPolicy']['handlerCount']=1
        with self.assertRaises(ValueError):receipt(value,'en','a'*40)
    def test_reject_unproven_retry_count(self):
        value=self.valid();value['runtimeRetryPolicy']['sharedMaxRetryAttempts']=4
        with self.assertRaises(ValueError):receipt(value,'en','a'*40)
    def test_reject_multiple_browser_requests_per_phase(self):
        value=self.valid();value['browserRequests'][1]['count']=2
        with self.assertRaises(ValueError):receipt(value,'en','a'*40)
    def test_reject_missing_browser_phase(self):
        value=self.valid();value['browserRequests'].pop()
        with self.assertRaises(ValueError):receipt(value,'en','a'*40)
    def test_reject_wrong_runtime_source_pin(self):
        value=self.valid();value['runtimeRetryPolicy']['runtimeDefaultsRevision']='f'*40
        with self.assertRaises(ValueError):receipt(value,'en','a'*40)
    def test_reject_unknown_runtime_metadata(self):
        value=self.valid();value['runtimeRetryPolicy']['unapproved']='anything'
        with self.assertRaises(ValueError):receipt(value,'en','a'*40)

class CleanupRejectionControls(unittest.TestCase):
    def valid(self):
        now = datetime.datetime.now(datetime.timezone.utc)
        run = 'a'*32; cid = 'b'*64; expiry = (now+datetime.timedelta(minutes=50)).isoformat()
        original = {'id':cid,'createdUtc':now.isoformat(),'imageId':'sha256:'+'c'*64,'name':'/billing-proof-'+run+'-postgres','owner':'web-billing-proof','run':run,'expiresUtc':expiry,'configuredImage':'postgres:18-alpine','envelopeValid':True,'initEnabled':True,'envelopeSignatureSha256':'d'*64,'memoryBytes':536870912,'swapBytes':536870912,'nanoCpus':1000000000,'tmpfs':'/var/lib/postgresql:rw,size=268435456','loopbackPorts':True,'persistentData':False}
        backend = {'backend':'postgres','localEndpoint':'unix:///var/run/docker.sock','databaseSynthetic':True,'name':'billing-proof-'+run+'-postgres','database':'profile_contract_'+'e'*32,'owner':'web-billing-proof','run':run,'expiresUtc':expiry,'daemon':'synthetic-daemon','originalId':cid,'baselineAbsent':True,'startDispatched':True,'startupSettled':True,'captureVerified':True,'absenceVerified':True,'sdkReleased':True,'original':original}
        return {'owner':'web-billing-proof','run':'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','expiresUtc':(now+datetime.timedelta(minutes=30)).isoformat(),'released':True,'readerFailureRejected':False,'retained':False,'expired':False,'expiryFailure':None,'policy':'Same-owner 1800-second expiry task closes admission, frontends and actual case work before dependent cleanup; unresolved ownership is quarantined, never accepted as release.','hosts':[{'startupComplete':True,'disposal':'RanToCompletion'} for _ in range(11)],'children':[{'dispatched':True,'identityCaptured':True,'exitVerified':True,'noChildVerified':False,'readersClosed':True,'released':True,'process':{'pid':2001+index,'birth':(now+datetime.timedelta(seconds=1)).isoformat(),'executable':'/opt/hostedtoolcache/dotnet/dotnet','startRefused':False,'ExpiresUtc':(now+datetime.timedelta(minutes=30,seconds=1)).isoformat(),'stdout':'RanToCompletion','stderr':'RanToCompletion','stdoutClosed':True,'stderrClosed':True}} for index in range(3)],'backends':[backend]}
    def test_accept_cleanup_metadata_control(self):
        self.assertTrue(cleanup(self.valid())['released'])
    def test_actual_helper_serialization_shape_and_hash(self):
        helper = Path(__file__).resolve().parents[2]/'Legacy.Maliev.Web.Tests/BillingProofLifetime.cs'
        self.assertEqual(helper_source(helper),HELPER_SHA256)
        source = helper.read_text()
        self.assertIn('private readonly Guid run = Guid.NewGuid();',source)
        self.assertIn('owner = "web-billing-proof",\n                run,',source)
        self.assertIn('new BillingDockerBackend(database, run.ToString("N"))',source)
        self.assertIn('run = backend.Run,',source)
        self.assertIn('run = original.Run,',source)
        self.assertIn('pid, birth, executable, startRefused, ExpiresUtc, stdout',source)
        value=self.valid(); retained=cleanup(value)
        self.assertEqual(retained['run'],value['run'])
        self.assertEqual(retained['backends'][0]['run'],original_run(value['run']))
    def test_reject_changed_helper_source(self):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'BillingProofLifetime.cs';path.write_text('changed serialization source')
            with self.assertRaises(ValueError):helper_source(path)
    def test_accept_canonical_n_without_rewriting_d(self):
        value=self.valid();value['run']=original_run(value['run'])
        self.assertEqual(cleanup(value)['run'],value['run'])
    def test_reject_noncanonical_owner_uuid_forms(self):
        canonical=self.valid()['run']
        for malformed in [canonical.upper(),'{'+canonical+'}',' '+canonical,canonical+'\n',canonical.replace('-','',1),canonical.replace('-','x',1),True,None,17]:
            with self.subTest(value=malformed):
                value=self.valid();value['run']=malformed
                with self.assertRaises(ValueError):cleanup(value)
    def test_reject_wellformed_foreign_owner_uuid(self):
        value=self.valid();value['run']='ffffffff-ffff-ffff-ffff-ffffffffffff'
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_d_backend_label_even_if_same_uuid(self):
        value=self.valid();value['backends'][0]['run']=value['run']
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_original_label_foreign_uuid(self):
        value=self.valid();value['backends'][0]['original']['run']='f'*32
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_backend_name_foreign_uuid(self):
        value=self.valid();value['backends'][0]['name']='billing-proof-'+'f'*32+'-postgres'
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_original_name_foreign_generation(self):
        value=self.valid();value['backends'][0]['original']['name']='/billing-proof-'+'f'*32+'-postgres'
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_foreign_backend_context(self):
        value=self.valid();value['backends'][0]['localEndpoint']='tcp://127.0.0.1:2375'
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_missing_original_daemon(self):
        value=self.valid();value['backends'][0]['daemon']=''
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_unknown_process_source_field(self):
        value=self.valid();value['children'][0]['process']['arguments']='unapproved'
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_wrong_child_source_executable(self):
        for executable in ['/bin/sh','dotnet','/opt/../dotnet','/opt/dotnet\n']:
            with self.subTest(executable=executable):
                value=self.valid();value['children'][0]['process']['executable']=executable
                with self.assertRaises(ValueError):cleanup(value)
    def test_reject_unknown_child_expiry_casing(self):
        value=self.valid();process=value['children'][0]['process'];process['expiresUtc']=process.pop('ExpiresUtc')
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_child_generation_after_owner_expiry(self):
        value=self.valid();value['children'][0]['process']['birth']=value['expiresUtc']
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_child_generation_before_backend_birth(self):
        value=self.valid();value['children'][0]['process']['birth']=value['backends'][0]['original']['createdUtc']
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_child_expiry_before_birth(self):
        value=self.valid();value['children'][0]['process']['ExpiresUtc']=value['children'][0]['process']['birth']
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_noninteger_child_pid(self):
        for pid in [True,2001.0,'2001',0,-1]:
            with self.subTest(pid=pid):
                value=self.valid();value['children'][0]['process']['pid']=pid
                with self.assertRaises(ValueError):cleanup(value)
    def test_reject_duplicate_child_identity(self):
        value=self.valid();value['children'][1]['process']=copy.deepcopy(value['children'][0]['process'])
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_unsettled_original_child_readers(self):
        for field,new in [('stdout','Faulted'),('stderr','Running'),('stdoutClosed',False),('stderrClosed',False),('startRefused',True)]:
            with self.subTest(field=field):
                value=self.valid();value['children'][0]['process'][field]=new
                with self.assertRaises(ValueError):cleanup(value)
    def test_reject_original_allocation_after_owner_deadline(self):
        value=self.valid();value['backends'][0]['original']['createdUtc']=value['expiresUtc']
        with self.assertRaises(ValueError):cleanup(value)
    def test_reject_malformed_original_signature(self):
        for signature in ['','d'*63,'D'*64,'d'*64+'\n']:
            with self.subTest(signature=signature):
                value=self.valid();value['backends'][0]['original']['envelopeSignatureSha256']=signature
                with self.assertRaises(ValueError):cleanup(value)
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
