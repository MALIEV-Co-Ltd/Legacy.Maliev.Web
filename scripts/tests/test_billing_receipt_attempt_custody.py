"""Pure controls drive the real verifier CLI with synthetic receipts/TRX; no SDK actors."""
import copy,hashlib,json,os,pathlib,runpy,tempfile,types,unittest
from unittest.mock import patch
import ast
import contextlib
import io
import textwrap
import xml.etree.ElementTree as ET
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

# Independent literal roster from BillingProofLifetimeTests at this candidate.
LIFECYCLE_ROWS = (
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.StartupFailure_RecognizesOnlyTheExistingPrivateBoundary(exceptionType: \"System.IO.IOException\", expected: \"IOException\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.StartupFailure_RecognizesOnlyTheExistingPrivateBoundary(exceptionType: \"System.Net.Sockets.SocketException\", expected: \"SocketException\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.StartupFailure_RecognizesOnlyTheExistingPrivateBoundary(exceptionType: \"Microsoft.AspNetCore.Connections.AddressInUseExcep\"···, expected: \"AddressInUseException\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.StartupFailure_RecognizesOnlyTheExistingPrivateBoundary(exceptionType: \"Microsoft.Extensions.Options.OptionsValidationExce\"···, expected: \"OptionsValidationException\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.StartupFailure_RecognizesOnlyTheExistingPrivateBoundary(exceptionType: \"System.InvalidOperationException\", expected: \"InvalidOperationException\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.StartupFailure_RecognizesOnlyTheExistingPrivateBoundary(exceptionType: \"Npgsql.NpgsqlException\", expected: \"NpgsqlException\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.StartupFailure_RecognizesOnlyTheExistingPrivateBoundary(exceptionType: \"Npgsql.PostgresException\", expected: \"PostgresException\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.StartupFailure_DiscardsMalformedForeignOversizedAndArbitraryText",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.StartupFailure_DrainRetainsOnlyFixedStderrClassificationAcrossChunks(observeStartupFailure: False, expected: \"NotObserved\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.StartupFailure_DrainRetainsOnlyFixedStderrClassificationAcrossChunks(observeStartupFailure: True, expected: \"IOException\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.StartupFailure_ReaderFailureCannotReplaceTheOriginalExitedChildAssertion(cancelled: False)",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.StartupFailure_ReaderFailureCannotReplaceTheOriginalExitedChildAssertion(cancelled: True)",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.Expiry_JoinsAdmittedSynchronousBodyBeforeDependencyRelease",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.Expiry_JoinsAllocationReservationAndReleasesLateResourceExactlyOnce(dependency: False)",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.Expiry_JoinsAllocationReservationAndReleasesLateResourceExactlyOnce(dependency: True)",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.ProvenNoLiveChild_ClosesOriginalWrapperWithoutLiveExecutableLookup(state: \"exited\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.ProvenNoLiveChild_ClosesOriginalWrapperWithoutLiveExecutableLookup(state: \"refused\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.ActualIdleExpiry_ClosesOriginalChildBeforeDependency_AndReleasesOwner",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.ActualExpiry_DelayedFrontendShutdownRetainsBackendAndCaseWorkUntilSettled",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.AcquisitionFault_AfterAllocationRetainsOriginalChildForCleanup(stage: \"start\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.AcquisitionFault_AfterAllocationRetainsOriginalChildForCleanup(stage: \"identity\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.AcquisitionFault_AfterAllocationRetainsOriginalChildForCleanup(stage: \"readers\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.FailedCleanup_RetainsOwnershipAndDependencies_ThenExactRetryCanRelease(stage: \"kill\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.FailedCleanup_RetainsOwnershipAndDependencies_ThenExactRetryCanRelease(stage: \"reap\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.FailedCleanup_RetainsOwnershipAndDependencies_ThenExactRetryCanRelease(stage: \"readers\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.FailedCleanup_RetainsOwnershipAndDependencies_ThenExactRetryCanRelease(stage: \"reader-close\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.FailedCleanup_RetainsOwnershipAndDependencies_ThenExactRetryCanRelease(stage: \"handle-close\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.FailedHostDisposal_IsStickyAndCannotBecomeNoOpClearance",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.InterruptedHostStartup_RefusesHostDisposalAndDependencyRelease",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.SettledChildReaders_BeforeHandleAndDependencyRelease_AllHostsAttemptedFirst",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.SynchronousBlockedHostRelease_IsBoundedRetainedAndReusesOriginalAttempt",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.SettledReaderFault_ReleasesResourcesButRepeatDisposalStillRejectsProof",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.BackendGenerationChange_RefusesMutationAndAbsenceReceipt(stage: \"daemon\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.BackendGenerationChange_RefusesMutationAndAbsenceReceipt(stage: \"id\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.BackendGenerationChange_RefusesMutationAndAbsenceReceipt(stage: \"created\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.BackendGenerationChange_RefusesMutationAndAbsenceReceipt(stage: \"owner\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.BackendGenerationChange_RefusesMutationAndAbsenceReceipt(stage: \"envelope\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.BackendGenerationChange_RefusesMutationAndAbsenceReceipt(stage: \"init\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.BackendGenerationChange_RefusesMutationAndAbsenceReceipt(stage: \"signature\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.BackendExactOriginalRemoval_IsReobservedAfterSdkDisposal",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.BackendSdkCreationIdUnavailable_RefusesNameBasedAdoptionAndCleanup",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.BackendSdkIdentityLostBeforeDisposal_RefusesMutation",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.BackendUnsettledRemoval_RefusesAbsenceAndSdkRelease",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.ChildCleanupFailure_ReportsOnlyItsBoundedStageAndRetainsDependencies(failure: \"graceful-stop\", stage: \"request-graceful-stop\", exceptionType: \"IOException\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.ChildCleanupFailure_ReportsOnlyItsBoundedStageAndRetainsDependencies(failure: \"handle-close\", stage: \"close-handle\", exceptionType: \"IOException\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.ChildCleanupFailure_ReportsOnlyItsBoundedStageAndRetainsDependencies(failure: \"identity\", stage: \"capture-identity\", exceptionType: \"IOException\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.ChildCleanupFailure_ReportsOnlyItsBoundedStageAndRetainsDependencies(failure: \"kill\", stage: \"kill-exact\", exceptionType: \"IOException\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.ChildCleanupFailure_ReportsOnlyItsBoundedStageAndRetainsDependencies(failure: \"reader-close\", stage: \"close-readers\", exceptionType: \"IOException\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.ChildCleanupFailure_ReportsOnlyItsBoundedStageAndRetainsDependencies(failure: \"readers\", stage: \"join-readers\", exceptionType: \"TimeoutException\")",
    "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.ChildCleanupFailure_ReportsOnlyItsBoundedStageAndRetainsDependencies(failure: \"reap\", stage: \"wait-exit\", exceptionType: \"TimeoutException\")",
)
LIFECYCLE_COUNTERS = {
    'total': '50', 'executed': '50', 'passed': '50', 'failed': '0', 'error': '0', 'timeout': '0',
    'aborted': '0', 'inconclusive': '0', 'passedButRunAborted': '0', 'notRunnable': '0', 'notExecuted': '0',
    'disconnected': '0', 'warning': '0', 'completed': '0', 'inProgress': '0', 'pending': '0',
}
LIFECYCLE_NS = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'


class LifecycleRosterControls(unittest.TestCase):
    def guard(self):
        workflow = (ROOT / '.github/workflows/billing-persistence.yml').read_text(encoding='utf-8')
        marker = '      - name: Require all fifty exact unique passed lifecycle control rows\n'
        self.assertEqual(1, workflow.count(marker))
        block = workflow.split(marker, 1)[1].split('\n      - name:', 1)[0]
        body = block.split("          python3 -B - <<'PY'\n", 1)[1].split('\n          PY', 1)[0]
        return textwrap.dedent(body)

    def document(self):
        root = ET.Element('{' + LIFECYCLE_NS + '}TestRun')
        results = ET.SubElement(root, '{' + LIFECYCLE_NS + '}Results')
        for name in LIFECYCLE_ROWS:
            ET.SubElement(results, '{' + LIFECYCLE_NS + '}UnitTestResult', testName=name, outcome='Passed')
        summary = ET.SubElement(root, '{' + LIFECYCLE_NS + '}ResultSummary')
        ET.SubElement(summary, '{' + LIFECYCLE_NS + '}Counters', LIFECYCLE_COUNTERS)
        return root

    def execute(self, root, files=('synthetic-lifecycle.trx',)):
        # Execute the actual inline gate against real XML; no files, subprocess or SDK actors.
        with patch('pathlib.Path.glob', return_value=list(files)), \
                patch('xml.etree.ElementTree.parse', return_value=ET.ElementTree(root)), \
                contextlib.redirect_stdout(io.StringIO()):
            exec(compile(self.guard(), '<billing-lifecycle-workflow>', 'exec'), {})

    def test_current_50_literal_names_match_the_actual_inline_roster(self):
        assignment = next(node for node in ast.parse(self.guard()).body
                          if isinstance(node, ast.Assign)
                          and any(isinstance(target, ast.Name) and target.id == 'expected' for target in node.targets))
        self.assertEqual(50, len(LIFECYCLE_ROWS))
        self.assertEqual(50, len(set(LIFECYCLE_ROWS)))
        self.assertEqual(set(LIFECYCLE_ROWS), ast.literal_eval(assignment.value))

    def test_all_50_unique_passed_rows_and_exact_counters_are_accepted(self):
        self.execute(self.document())

    def test_missing_duplicate_and_foreign_rows_are_rejected(self):
        tag = '{' + LIFECYCLE_NS + '}'
        for defect in ('missing', 'duplicate', 'foreign'):
            with self.subTest(defect=defect):
                root = self.document()
                results = root.find(tag + 'Results')
                rows = list(results)
                if defect == 'missing':
                    results.remove(rows[-1])
                else:
                    rows[-1].set('testName', rows[0].get('testName') if defect == 'duplicate' else 'Foreign.Control')
                with self.assertRaisesRegex(SystemExit, '^Missing, duplicate, foreign or unsuccessful lifecycle control row$'):
                    self.execute(root)


        for raw, displayed in (
            ("Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.StartupFailure_RecognizesOnlyTheExistingPrivateBoundary(exceptionType: \"Microsoft.AspNetCore.Connections.AddressInUseException\", expected: \"AddressInUseException\")",
             "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.StartupFailure_RecognizesOnlyTheExistingPrivateBoundary(exceptionType: \"Microsoft.AspNetCore.Connections.AddressInUseExcep\"···, expected: \"AddressInUseException\")"),
            ("Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.StartupFailure_RecognizesOnlyTheExistingPrivateBoundary(exceptionType: \"Microsoft.Extensions.Options.OptionsValidationException\", expected: \"OptionsValidationException\")",
             "Legacy.Maliev.Web.Tests.BillingProofLifetimeTests.StartupFailure_RecognizesOnlyTheExistingPrivateBoundary(exceptionType: \"Microsoft.Extensions.Options.OptionsValidationExce\"···, expected: \"OptionsValidationException\")"),
        ):
            with self.subTest(untruncated=raw):
                root = self.document()
                matching = [row for row in root.findall('.//' + tag + 'UnitTestResult')
                            if row.get('testName') == displayed]
                self.assertEqual(1, len(matching))
                matching[0].set('testName', raw)
                with self.assertRaisesRegex(SystemExit, '^Missing, duplicate, foreign or unsuccessful lifecycle control row$'):
                    self.execute(root)

    def test_nonpassed_rows_cannot_be_accepted_by_matching_counters(self):
        for outcome in ('Failed', 'NotExecuted', 'Error'):
            with self.subTest(outcome=outcome):
                root = self.document()
                root.find('.//{' + LIFECYCLE_NS + '}UnitTestResult').set('outcome', outcome)
                with self.assertRaisesRegex(SystemExit, '^Missing, duplicate, foreign or unsuccessful lifecycle control row$'):
                    self.execute(root)

    def test_every_counter_is_required_and_exact(self):
        for key in LIFECYCLE_COUNTERS:
            for missing in (False, True):
                with self.subTest(counter=key, missing=missing):
                    root = self.document()
                    counters = root.find('.//{' + LIFECYCLE_NS + '}Counters')
                    if missing:
                        del counters.attrib[key]
                    else:
                        counters.set(key, '1')
                    with self.assertRaisesRegex(SystemExit, '^Lifecycle TRX counters do not match fifty completed Passed rows$'):
                        self.execute(root)

    def test_missing_or_multiple_trx_files_are_rejected(self):
        for files in ((), ('one.trx', 'two.trx')):
            with self.subTest(files=files):
                with self.assertRaisesRegex(SystemExit, '^Exactly one lifecycle TRX required$'):
                    self.execute(self.document(), files)

if __name__=='__main__':unittest.main()
