"""Pure controls drive the real verifier CLI with synthetic receipts/TRX; no SDK actors."""
import copy,hashlib,json,os,pathlib,runpy,tempfile,types,unittest
from unittest.mock import patch
ROOT=pathlib.Path(__file__).resolve().parents[2]
SCRIPT=ROOT/'scripts/verify-billing-persistence-proof.py'
HEAD='a'*40
RUNS={'en':'1'*32,'th':'2'*32}
DATASET='thailand-geography-json:b8b3fb91c7df1129ff5b43cb46f7fcffadd2156b'
class CustodyControls(unittest.TestCase):
 def build(self,root):
  d=root/'Legacy.Maliev.Web.Tests/bin/Release/net10.0/TestResults/billing-persistence';(d/'attempts').mkdir(parents=True)
  t=root/'billing-persistence-test-results';t.mkdir()
  rows=[]
  for culture,width in (('en',1280),('th',375)):
   run=RUNS[culture];image=b'\x89PNG\r\n\x1a\nsynthetic-'+culture.encode();name=f'{culture}-{run}'
   (d/f'{culture}.png').write_bytes(image);(d/'attempts'/f'{name}.png').write_bytes(image)
   original={'owner':'web-billing-proof','run':run,'id':'3'*64,'name':'/billing-proof-'+run+'-postgres','expiresUtc':'synthetic-fixed-expiry','envelopeValid':True,'initEnabled':True,'loopbackPorts':True,'persistentData':False}
   resource={'backend':'postgres','databaseSynthetic':True,'owner':original['owner'],'run':run,'originalId':original['id'],'name':original['name'][1:],'expiresUtc':original['expiresUtc'],'original':original,**{k:True for k in ('baselineAbsent','startDispatched','startupSettled','captureVerified','absenceVerified','sdkReleased')}}
   cleanup={'schema':1,'surface':'member-billing','culture':culture,'candidateHead':HEAD,'owner':'web-billing-proof','run':run,'complete':True,'graphReleased':True,'expiryJoined':True,'sharedAuthorityExcluded':True,'expired':False,'readerFailureRejected':False,'resources':[resource]}
   receipt={'surface':'member-billing','culture':culture,'width':width,'candidateHead':HEAD,'catalogStatus':200,'saveStatus':302,'readbackStatus':200,'reloadVerified':True,'shippingPreserved':True,'manualDetailPreserved':True,'datasetVersion':DATASET,'businessComplete':True,'attemptRun':run,'attemptScreenshot':{'file':name+'.png','bytes':len(image),'sha256':hashlib.sha256(image).hexdigest()},'billingBackendCleanup':cleanup}
   self.write(d,culture,receipt)
   name=f'Legacy.Maliev.Web.Tests.ThaiLookupBillingPersistenceTests.BillingTuple_NormalSave_ApiReadbackAndReloadPreserveManualFields(culture: &quot;{culture}&quot;, width: {width})'
   rows.append(f'<UnitTestResult outcome="Passed" testName="{name}"><Output><StdOut>billing-attempt-run {run}</StdOut></Output></UnitTestResult>')
  trx='<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>'+''.join(rows)+'</Results></TestRun>'
  (t/'synthetic.trx').write_text(trx)
  return d,t/'synthetic.trx'
 def write(self,d,culture,receipt):
  b=json.dumps(receipt,separators=(',',':')).encode()
  (d/f'{culture}.json').write_bytes(b)
  (d/'attempts'/f'{culture}-{RUNS[culture]}.json').write_bytes(b)
 def execute(self,change=None,rejected=True):
  with tempfile.TemporaryDirectory(prefix='billing-custody-') as temp:
   root=pathlib.Path(temp);d,trx=self.build(root)
   if change:change(d,trx)
   cwd=os.getcwd()
   try:
    os.chdir(root)
    with patch.dict(os.environ,{'MALIEV_BILLING_CANDIDATE_HEAD':HEAD}),patch('subprocess.check_output',side_effect=[HEAD,'']),patch('subprocess.run',return_value=types.SimpleNamespace(returncode=0)):
     if rejected:
      with self.assertRaises((SystemExit,ValueError,TypeError)):runpy.run_path(str(SCRIPT),run_name='__main__')
     else:runpy.run_path(str(SCRIPT),run_name='__main__')
   finally:os.chdir(cwd)
 def mutate(self,path,value):
  def change(d,trx):
   r=json.loads((d/'th.json').read_text());node=r
   for k in path[:-1]:node=node[k]
   node[path[-1]]=value;self.write(d,'th',r)
  return change
 def test_exact_same_case_business_cleanup_archive_and_image_pass(self):self.execute(rejected=False)
 def test_stale_focused_alias_current_cleanup_rejected(self):self.execute(self.mutate(['attemptRun'],'4'*32))
 def test_missing_business_current_failure_rejected(self):self.execute(self.mutate(['businessComplete'],False))
 def test_business_integer_true_rejected(self):self.execute(self.mutate(['businessComplete'],1))
 def test_cross_case_cleanup_rejected(self):self.execute(self.mutate(['billingBackendCleanup','run'],RUNS['en']))
 def test_resource_run_mismatch_rejected(self):self.execute(self.mutate(['billingBackendCleanup','resources',0,'run'],RUNS['en']))
 def test_foreign_original_identity_rejected(self):self.execute(self.mutate(['billingBackendCleanup','resources',0,'original','id'],'4'*64))
 def test_unreleased_cleanup_rejected(self):self.execute(self.mutate(['billingBackendCleanup','complete'],False))
 def test_archive_overwrite_rejected(self):self.execute(lambda d,t:(d/'attempts'/f"th-{RUNS['th']}.json").write_bytes(b'{}'))
 def test_missing_archive_rejected(self):self.execute(lambda d,t:(d/'attempts'/f"th-{RUNS['th']}.json").unlink())
 def test_archived_screenshot_overwrite_rejected(self):self.execute(lambda d,t:(d/'attempts'/f"th-{RUNS['th']}.png").write_bytes(b'foreign'))
 def test_current_screenshot_stale_rejected(self):self.execute(lambda d,t:(d/'th.png').write_bytes((d/'en.png').read_bytes()))
 def test_foreign_screenshot_path_rejected(self):self.execute(self.mutate(['attemptScreenshot','file'],'../en.png'))
 def test_screenshot_size_mismatch_rejected(self):self.execute(self.mutate(['attemptScreenshot','bytes'],1))
 def test_missing_case_marker_rejected(self):self.execute(lambda d,t:t.write_text(t.read_text().replace('billing-attempt-run '+RUNS['th'],'')))
 def test_wrong_case_marker_rejected(self):self.execute(lambda d,t:t.write_text(t.read_text().replace('billing-attempt-run '+RUNS['th'],'billing-attempt-run '+'4'*32)))
 def test_duplicate_case_marker_rejected(self):self.execute(lambda d,t:t.write_text(t.read_text().replace('billing-attempt-run '+RUNS['th'],'billing-attempt-run '+RUNS['th']+'\nbilling-attempt-run '+RUNS['th'])))
 def test_failed_native_case_not_accepted_by_previous_receipt(self):self.execute(lambda d,t:t.write_text(t.read_text().replace('outcome="Passed"','outcome="Failed"',1)))
 def test_missing_current_receipt_rejected(self):self.execute(lambda d,t:(d/'th.json').unlink())
 def test_producer_never_merges_prior_alias_and_archives_create_new(self):
  s=(ROOT/'Legacy.Maliev.Web.Tests/BillingProofLifetime.cs').read_text()
  method=s.split('private async Task PersistBackendProofAsync()',1)[1].split('private void DisposeExpirySource()',1)[0]
  self.assertNotIn('File.Exists(file)',method);self.assertNotIn('ReadAllTextAsync(file,',method)
  self.assertIn('businessProof!.DeepClone()',method);self.assertEqual(method.count('FileMode.CreateNew'),2)
  self.assertIn('body?.IsCompletedSuccessfully == true',method);self.assertIn('screenshot = completed ? businessScreenshot : null;',method)
 def test_artifact_failure_guard_does_not_swallow_cleanup_refusal(self):
  s=(ROOT/'Legacy.Maliev.Web.Tests/BillingProofLifetime.cs').read_text()
  method=s.split('private async Task PersistBackendProofAsync()',1)[1].split('private void DisposeExpirySource()',1)[0]
  self.assertLess(method.index('catch (Exception failure)'),method.index('if (released && (!complete'))
  self.assertLess(method.index('await File.WriteAllTextAsync(file'),method.index('catch (Exception failure)'))
  self.assertIn('body?.IsFaulted == true || body?.IsCanceled == true',method)
if __name__=='__main__':unittest.main()
