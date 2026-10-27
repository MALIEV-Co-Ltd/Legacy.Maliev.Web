"""Current source/receipt metadata controls; synthetic controls are never native proof."""
import copy
import importlib.util
import json
from pathlib import Path
import sys
import unittest

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import retain as legacy
spec=importlib.util.spec_from_file_location('company_historical_controls',HERE/'test_retain.py')
historical=importlib.util.module_from_spec(spec);spec.loader.exec_module(historical)

def assembly(role,name,revision):
    return dict(role=role,assemblyName=name,revision=revision,version='1.0.0.0',mvid='11111111-1111-1111-1111-111111111111',sha256='a'*64)

def evidence(candidate='b'*40):
    packages=[dict(id=name,version=version,revision=revision,sha512='A'*86+'==') for name,version,revision in [('Microsoft.Extensions.Http.Resilience','10.11.0','25f07afa51034e37b334210642d7dc5f191c142d'),('Microsoft.Extensions.Http','10.0.13','4099f83977f4d29604a9aeb971fb0a7bc1d5f699'),('Polly.Core','8.4.2','077cf388fc5a85a7f6cd0fdcb2b68d17a898019e')]]
    verifications=[]
    for index,(package,project) in enumerate((p,q) for p in packages for q in ('companyTests','webTests')):
        receipt=dict(identity=package['id']+'.'+package['version'],pid=100+index,startTicks=str(1000+index),bootId='11111111-1111-1111-1111-111111111111',executable='/usr/share/dotnet/dotnet',timeoutSeconds=30,outputLimitBytes=65536,terminalVerified=True,pipeEof=True,pidfdAcquired=True,verified=True,exitCode=0,outputSha256='a'*64,outputBytes=128,contentHash=package['sha512'],elapsedMilliseconds=10)
        verifications.append(dict(id=package['id'],project=project,assetsContentHash=package['sha512'],archiveHash='B'*86+'==',cacheArchiveHash='B'*86+'==',metadataContentHash=package['sha512'],signaturePresent=True,verification=receipt))
    return dict(schemaVersion=2,candidateHead=candidate,profileSha256='c'*64,sourceChecked=True,
        sourceRevisions=dict(defaults='4ffba0639620e7e923d331317f3f28501526b573',contracts='78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7'),
        assemblies=[assembly('defaults','Legacy.Maliev.ServiceDefaults','4ffba0639620e7e923d331317f3f28501526b573'),assembly('resilience','Microsoft.Extensions.Http.Resilience','25f07afa51034e37b334210642d7dc5f191c142d'),assembly('http','Microsoft.Extensions.Http','4099f83977f4d29604a9aeb971fb0a7bc1d5f699'),assembly('polly','Polly.Core','077cf388fc5a85a7f6cd0fdcb2b68d17a898019e')],
        packageSources=packages,packageVerifications=verifications,
        assets=[dict(project=name,sha256='d'*64) for name in ['web-tests','company-tests']],
        buildLogs=[dict(project=name,sha256='e'*64,warnings=0,errors=0) for name in ['web-tests','company-tests']],
        generatedDefaultsMetadata=dict(sha256='f'*64,revision='4ffba0639620e7e923d331317f3f28501526b573'))

def current_cleanup():
    value=historical.CleanupRejectionControls().valid()
    for child in value['children']:child['cleanupStage']='released'
    return value

def current_receipt():
    value=historical.RejectionControls().valid();value['candidateHead']='b'*40
    policy=value['runtimeRetryPolicy'];ev=evidence()
    policy.update(runtimeDefaultsRevision=ev['sourceRevisions']['defaults'],runtimePackageSourceRevision=ev['assemblies'][1]['revision'],
        assemblyEvidence=copy.deepcopy(ev['assemblies']),linkedHandlerOrder=[0,1],linkedHandlerOrderVerified=True,buildEvidenceSha256='1'*64,sourceProfileSha256=ev['profileSha256'])
    return value

class CurrentConsumerControls(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        path=HERE/'current_consumer.py'
        cls.current=None
        if path.exists():
            spec=importlib.util.spec_from_file_location('current_company_consumer',path)
            cls.current=importlib.util.module_from_spec(spec);spec.loader.exec_module(cls.current)
    def consumer(self):
        if self.current is None:self.fail('Strict current Company consumer is missing')
        return self.current
    def test_current_helper_positive_and_historical_refusal(self):
        d=self.consumer();path=HERE.parents[1]/'Legacy.Maliev.Web.Tests/BillingProofLifetime.cs'
        self.assertEqual('680ff227f0e4e395c44f81f40e69c845d015b637661f6e68c8c5b8828dfba14b',d.current_helper_source(path))
        with self.assertRaises(ValueError):legacy.helper_source(path)
    def test_current_cleanup_preserves_raw_and_stage(self):
        d=self.consumer();value=current_cleanup();before=copy.deepcopy(value);result=d.current_cleanup(value,legacy)
        self.assertEqual(before,value);self.assertTrue(all(c['cleanupStage']=='released' for c in result['children']))
        with self.assertRaises(ValueError):legacy.cleanup(value)
    def test_every_intermediate_stage_and_wrong_type_is_refused(self):
        d=self.consumer()
        for stage in ['not-started','verify-no-live-child','capture-identity','observe-exit','request-graceful-stop','wait-graceful-exit','kill-exact','wait-exit','verify-exit','join-readers','close-readers','close-handle',None,True,1,'Released','unknown']:
            for index in range(3):
                value=current_cleanup();value['children'][index]['cleanupStage']=stage
                with self.subTest(stage=stage,index=index),self.assertRaises(ValueError):d.current_cleanup(value,legacy)
    def test_released_stage_never_substitutes_settlement_identity_or_expiry(self):
        d=self.consumer()
        for flag in ['dispatched','identityCaptured','exitVerified','readersClosed','released']:
            value=current_cleanup();value['children'][1][flag]=False
            with self.assertRaises(ValueError):d.current_cleanup(value,legacy)
        for flag in ['readerFailureRejected','retained','expired']:
            value=current_cleanup();value[flag]=True
            with self.assertRaises(ValueError):d.current_cleanup(value,legacy)
        value=current_cleanup();value['children'][0]['noChildVerified']=True
        with self.assertRaises(ValueError):d.current_cleanup(value,legacy)
    def test_missing_or_extra_stage_schema_refused(self):
        d=self.consumer()
        for mutate in ['missing','extra']:
            value=current_cleanup()
            if mutate=='missing':value['children'][0].pop('cleanupStage')
            else:value['children'][0]['other']='unknown'
            with self.assertRaises(ValueError):d.current_cleanup(value,legacy)
    def test_current_policy_projection_retains_current_provenance(self):
        d=self.consumer();value=current_receipt();before=copy.deepcopy(value)
        result=d.current_receipt(value,'en','b'*40,evidence(),'1'*64,legacy)
        self.assertEqual(before,value);self.assertEqual(before,result)
        with self.assertRaises(ValueError):legacy.receipt(value,'en','b'*40)
    def test_wrong_loaded_assembly_hash_mvid_source_or_profile_is_refused(self):
        d=self.consumer()
        for role in range(4):
            for field,bad in [('sha256','0'*64),('mvid','22222222-2222-2222-2222-222222222222'),('revision','0'*40)]:
                value=current_receipt();value['runtimeRetryPolicy']['assemblyEvidence'][role][field]=bad
                with self.assertRaises(ValueError):d.current_receipt(value,'en','b'*40,evidence(),'1'*64,legacy)
        value=current_receipt();value['runtimeRetryPolicy']['sourceProfileSha256']='0'*64
        with self.assertRaises(ValueError):d.current_receipt(value,'en','b'*40,evidence(),'1'*64,legacy)
    def test_handler_and_both_options_bounds_cannot_be_replaced_with_source_claims(self):
        d=self.consumer()
        for field,bad in [('handlerCount',1),('handlerCount',True),('linkedHandlerOrder',[1,0]),('linkedHandlerOrderVerified',False),('pipelineOrderFromPinnedSource',['catalog-standard','-standard']),('confirmedFromActualHandlerChain',False)]:
            value=current_receipt();value['runtimeRetryPolicy'][field]=bad
            with self.assertRaises(ValueError):d.current_receipt(value,'en','b'*40,evidence(),'1'*64,legacy)
        for index in range(2):
            value=current_receipt();value['runtimeRetryPolicy']['pipelineOptions'][index]['maxRetryAttempts']=True
            with self.assertRaises(ValueError):d.current_receipt(value,'en','b'*40,evidence(),'1'*64,legacy)
    def test_build_source_wrong_revision_grants_or_counters_refused(self):
        d=self.consumer();d.validate_build_evidence(evidence(),'b'*40)
        for key in ['candidateHead','sourceChecked']:
            value=evidence();value[key]='wrong'
            with self.assertRaises(ValueError):d.validate_build_evidence(value,'b'*40)
        value=evidence();value['assemblies'][1]['revision']='02107c65bab30aad9e35b5133ed643eaa77bccd8'
        with self.assertRaises(ValueError):d.validate_build_evidence(value,'b'*40)
        value=evidence();value['buildLogs'][0]['warnings']=1
        with self.assertRaises(ValueError):d.validate_build_evidence(value,'b'*40)
    def test_package_verifier_receipts_refuse_missing_or_forged_proof(self):
        d=self.consumer();d.validate_build_evidence(evidence(),'b'*40)
        for field,bad in [('verified',False),('terminalVerified',False),('pipeEof',False),('pidfdAcquired',False),('exitCode',1),('contentHash','B'*86+'=='),('startTicks','0'),('outputBytes',65537),('timeoutSeconds',31),('identity','Other.1.0')]:
            value=evidence();value['packageVerifications'][0]['verification'][field]=bad
            with self.assertRaises(ValueError):d.validate_build_evidence(value,'b'*40)
        for field,bad in [('signaturePresent',False),('metadataContentHash','B'*86+'=='),('cacheArchiveHash','C'*86+'=='),('project','webTests')]:
            value=evidence();value['packageVerifications'][0][field]=bad
            with self.assertRaises(ValueError):d.validate_build_evidence(value,'b'*40)
        value=evidence();value['packageVerifications'][1]['verification'].update(pid=100,startTicks='1000')
        with self.assertRaises(ValueError):d.validate_build_evidence(value,'b'*40)
    def test_duplicate_json_and_nonfinite_values_refused(self):
        d=self.consumer()
        for raw in [b'{"x":1,"x":2}',b'{"x":NaN}']:
            with self.assertRaises(ValueError):d.parse(raw)
    def test_signed_package_requires_verified_content_and_matching_archive_domains(self):
        import base64,hashlib,io,json,tempfile,zipfile
        from unittest.mock import patch
        spec=importlib.util.spec_from_file_location('current_build_hash_diagnostic',HERE/'capture_build_source.py');capture=importlib.util.module_from_spec(spec);spec.loader.exec_module(capture)
        with tempfile.TemporaryDirectory(prefix='company-hash-control-',dir=HERE) as temp:
            name='Microsoft.Extensions.Http.Resilience';version='10.11.0';key=name+'/'+version;revision='25f07afa51034e37b334210642d7dc5f191c142d';member='lib/net10.0/'+name+'.dll';package=Path(temp)/name.lower()/version;(package/'lib/net10.0').mkdir(parents=True)
            nuspec=('<package><metadata><repository type="git" url="https://github.com/dotnet/extensions" commit="'+revision+'" /></metadata></package>').encode();payload=b'synthetic dll';stream=io.BytesIO()
            with zipfile.ZipFile(stream,'w') as archive:archive.writestr('.signature.p7s',b'synthetic only');archive.writestr(member,payload);archive.writestr(name+'.nuspec',nuspec)
            raw=stream.getvalue();archive_hash=base64.b64encode(hashlib.sha512(raw).digest()).decode();content_hash=base64.b64encode(b'x'*64).decode();stem=name.lower()+'.'+version
            (package/(stem+'.nupkg')).write_bytes(raw);(package/(stem+'.nupkg.sha512')).write_text(archive_hash);(package/member).write_bytes(payload);(package/(name.lower()+'.nuspec')).write_bytes(nuspec)
            (package/'.nupkg.metadata').write_text(json.dumps({'version':2,'contentHash':content_hash,'source':'https://api.nuget.org/v3/index.json'}))
            assets={'libraries':{key:{'sha512':'sha512-'+content_hash,'path':name.lower()+'/'+version}},'packageFolders':{temp:{}},'targets':{'net10.0':{key:{'runtime':{member:{}}}}}}
            def verified(path,identity,receipt):receipt.update(verified=True,terminalVerified=True,pipeEof=True,pidfdAcquired=True,exitCode=0,contentHash=content_hash);return content_hash
            with patch.object(capture,'verify_signed_package',side_effect=verified):
                diagnostics={};record,dllhash=capture.package_record(assets,name,version,revision,diagnostics)
                self.assertEqual(content_hash,record['sha512']);self.assertEqual(hashlib.sha256(payload).hexdigest(),dllhash);self.assertNotEqual(diagnostics['archiveHash'],diagnostics['assetsContentHash'])
                duplicate=io.BytesIO()
                with zipfile.ZipFile(duplicate,'w') as archive:archive.writestr('.signature.p7s',b'synthetic only');archive.writestr(member,payload);archive.writestr(name+'.nuspec',nuspec);archive.writestr(name.lower()+'.nuspec',nuspec)
                bad=duplicate.getvalue();(package/(stem+'.nupkg')).write_bytes(bad);(package/(stem+'.nupkg.sha512')).write_text(base64.b64encode(hashlib.sha512(bad).digest()).decode())
                with self.assertRaises(ValueError):capture.package_record(assets,name,version,revision,{})
                (package/(stem+'.nupkg')).write_bytes(raw);(package/(stem+'.nupkg.sha512')).write_text(archive_hash)
                (package/(stem+'.nupkg.sha512')).write_text(content_hash)
                with self.assertRaises(ValueError):capture.package_record(assets,name,version,revision,{})
                (package/(stem+'.nupkg.sha512')).write_text(archive_hash);(package/'.nupkg.metadata').write_text(json.dumps({'contentHash':archive_hash}))
                with self.assertRaises(ValueError):capture.package_record(assets,name,version,revision,{})
                (package/'.nupkg.metadata').write_text(json.dumps({'contentHash':content_hash}));(package/(name.lower()+'.nuspec')).write_bytes(nuspec+b' ')
                with self.assertRaises(ValueError):capture.package_record(assets,name,version,revision,{})
                (package/(name.lower()+'.nuspec')).write_bytes(nuspec)
                def unverified(path,identity,receipt):receipt.update(verified=False,terminalVerified=True,pipeEof=True,pidfdAcquired=True,exitCode=0,contentHash=content_hash);return content_hash
                with patch.object(capture,'verify_signed_package',side_effect=unverified):
                    with self.assertRaises(ValueError):capture.package_record(assets,name,version,revision,{})
                def wronghash(path,identity,receipt):receipt.update(verified=True,terminalVerified=True,pipeEof=True,pidfdAcquired=True,exitCode=0,contentHash=archive_hash);return archive_hash
                with patch.object(capture,'verify_signed_package',side_effect=wronghash):
                    with self.assertRaises(ValueError):capture.package_record(assets,name,version,revision,{})
    def test_nuget_verifier_output_requires_unique_positive_identity(self):
        import base64
        import nuget_signed_verification as verifier
        identity='Microsoft.Extensions.Http.Resilience.10.11.0';digest=base64.b64encode(b'x'*64).decode();good=(f"Content hash: {digest}\nSuccessfully verified package '{identity}'.\n").encode()
        self.assertEqual(digest,verifier.parse_output(good,0,identity))
        for raw,code in [(f'Content hash: {digest}\n'.encode(),0),(good,1),(good+b'Package signature validation failed.\n',0),(good+b'error NU3008: invalid\n',0),(good+b'warning NU3018: untrusted\n',0),(good+good,0),(good+b"Successfully verified package 'Other.1.0'.\n",0),(good.replace(identity.encode(),b'Other.1.0'),0),(b'x'*65537,0)]:
            with self.assertRaises(ValueError):verifier.parse_output(raw,code,identity)
    def test_nuget_verifier_owned_process_is_bounded_and_terminated_by_pidfd(self):
        import base64,os,signal,tempfile
        from unittest.mock import patch
        import nuget_signed_verification as verifier
        identity='Microsoft.Extensions.Http.Resilience.10.11.0';digest=base64.b64encode(b'x'*64).decode();good=(f"Content hash: {digest}\nSuccessfully verified package '{identity}'.\n").encode()
        class Stream:
            def fileno(self):return 42
            def close(self):pass
        class Process:
            pid=32123
            def __init__(self,alive):self.stdout=Stream();self.returncode=None if alive else 0
            def poll(self):return self.returncode
            def wait(self,timeout):self.returncode=-15;return self.returncode
        class Selector:
            def register(self,*args):pass
            def unregister(self,*args):pass
            def close(self):pass
            def select(self,*args):return [(type('Key',(),{'fd':42,'fileobj':object()})(),None)]
        with tempfile.TemporaryDirectory(prefix='verifier-control-',dir=HERE) as root:
            exe=str((Path(root)/'dotnet').resolve());ident=dict(pid=32123,startTicks='1000',bootId='11111111-1111-1111-1111-111111111111',executable=exe)
            for overflow in (False,True):
                proc=Process(overflow);reads=[b'x'*65537] if overflow else [good,b''];receipt={};signals=[]
                with patch.object(verifier.sys,'platform','linux'),patch.dict(os.environ,{'DOTNET_ROOT':root}),patch.object(verifier.os,'pidfd_open',side_effect=[91,92],create=True),patch.object(verifier.os,'close'),patch.object(verifier.os,'set_blocking'),patch.object(verifier.os,'read',side_effect=reads),patch.object(verifier.signal,'pidfd_send_signal',side_effect=lambda fd,sig:signals.append((fd,sig)),create=True),patch.object(verifier.selectors,'DefaultSelector',return_value=Selector()),patch.object(verifier.subprocess,'Popen',return_value=proc) as spawn,patch.object(verifier,'proc_identity',return_value=ident):
                    if overflow:
                        with self.assertRaises(ValueError):verifier.verify_signed_package(Path(root)/'p.nupkg',identity,receipt)
                        self.assertEqual([(92,signal.SIGTERM)],signals);self.assertTrue(receipt['terminalVerified']);self.assertFalse(receipt['verified'])
                    else:
                        self.assertEqual(digest,verifier.verify_signed_package(Path(root)/'p.nupkg',identity,receipt));self.assertTrue(receipt['verified']);self.assertTrue(receipt['terminalVerified']);self.assertTrue(receipt['pipeEof']);self.assertEqual([],signals)
                    self.assertEqual(['nuget','verify'],spawn.call_args.args[0][1:3]);self.assertEqual(['--all','--verbosity','normal'],spawn.call_args.args[0][-3:])
    def test_verifier_preflight_and_deadline_fail_closed(self):
        import os,signal,subprocess,tempfile
        from unittest.mock import patch
        import nuget_signed_verification as verifier
        class Stream:
            def fileno(self):return 42
            def close(self):self.closed=True
        class Process:
            pid=32123
            def __init__(self):self.stdout=Stream();self.returncode=None;self.waits=0
            def poll(self):return self.returncode
            def wait(self,timeout):
                self.waits+=1
                if self.waits==1:raise subprocess.TimeoutExpired('dotnet',timeout)
                self.returncode=-9;return self.returncode
        class Selector:
            def close(self):pass
            def register(self,*args):pass
            def select(self,*args):return []
        with tempfile.TemporaryDirectory(prefix='verifier-deadline-',dir=HERE) as root:
            exe=str((Path(root)/'dotnet').resolve());ident=dict(pid=32123,startTicks='1000',bootId='11111111-1111-1111-1111-111111111111',executable=exe)
            with patch.object(verifier.sys,'platform','linux'),patch.dict(os.environ,{'DOTNET_ROOT':root}),patch.object(verifier.os,'pidfd_open',side_effect=OSError('denied'),create=True),patch.object(verifier.signal,'pidfd_send_signal',create=True),patch.object(verifier.subprocess,'Popen') as spawn:
                with self.assertRaises(OSError):verifier.verify_signed_package(Path(root)/'p.nupkg','x.1',{})
                spawn.assert_not_called()
            proc=Process();receipt={};signals=[]
            with patch.object(verifier.sys,'platform','linux'),patch.dict(os.environ,{'DOTNET_ROOT':root}),patch.object(verifier.os,'pidfd_open',side_effect=[91,92],create=True),patch.object(verifier.os,'close'),patch.object(verifier.os,'set_blocking'),patch.object(verifier.signal,'SIGKILL',9,create=True),patch.object(verifier.signal,'pidfd_send_signal',side_effect=lambda fd,sig:signals.append((fd,sig)),create=True),patch.object(verifier.selectors,'DefaultSelector',return_value=Selector()),patch.object(verifier.subprocess,'Popen',return_value=proc),patch.object(verifier,'proc_identity',return_value=ident),patch.object(verifier.time,'monotonic',side_effect=[0,31,32]):
                with self.assertRaisesRegex(ValueError,'deadline'):verifier.verify_signed_package(Path(root)/'p.nupkg','x.1',receipt)
            self.assertEqual([(92,signal.SIGTERM),(92,9)],signals);self.assertTrue(receipt['terminalVerified']);self.assertFalse(receipt['verified']);self.assertFalse(receipt['pipeEof']);self.assertTrue(proc.stdout.closed)
    def test_build_capture_rejects_non_cli_binary_without_execution(self):
        path=HERE/'capture_build_source.py'
        if not path.exists():self.fail('Static current build/source evidence capture is missing')
        spec=importlib.util.spec_from_file_location('current_build_capture',path);capture=importlib.util.module_from_spec(spec);spec.loader.exec_module(capture)
        for raw in [b'not a DLL',b'MZ'+b'\0'*100]:
            with self.assertRaises(ValueError):capture.inspect_assembly(raw)
        source=b'<package><metadata><repository type="git" url="https://github.com/dotnet/extensions" commit="'+b'a'*40+b'" /></metadata></package>'
        self.assertEqual('a'*40,capture.xml_repository(source,'https://github.com/dotnet/extensions'))
        with self.assertRaises(ValueError):capture.xml_repository(source,'https://github.com/foreign/source')
    def test_static_cli_module_and_assembly_identity_parser(self):
        import struct,uuid
        spec=importlib.util.spec_from_file_location('current_build_capture_valid',HERE/'capture_build_source.py');capture=importlib.util.module_from_spec(spec);spec.loader.exec_module(capture)
        raw=bytearray(4096)
        def put(at,fmt,*values):struct.pack_into('<'+fmt,raw,at,*values)
        raw[:2]=b'MZ';put(60,'I',128);raw[128:132]=b'PE\0\0';put(134,'H',1);put(148,'H',240);put(152,'H',0x20b);put(152+112+14*8,'II',0x2000,72)
        put(392+8,'IIII',1536,0x2000,1536,512);put(512,'I',72);put(520,'II',0x2080,512)
        base=640;raw[base:base+4]=b'BSJB';put(base+4,'HH',1,1);put(base+12,'I',12);raw[base+16:base+28]=b'v4.0.30319\0\0';put(base+28,'HH',0,3)
        put(base+32,'II',80,64);raw[base+40:base+44]=b'#~\0\0';put(base+44,'II',144,16);raw[base+52:base+60]=b'#GUID\0\0\0';put(base+60,'II',160,9);raw[base+68:base+80]=b'#Strings\0\0\0\0'
        tables=base+80;put(tables+8,'Q',(1<<32)|1);put(tables+24,'II',1,1);put(tables+32,'HHHHH',0,1,1,0,0);put(tables+42,'IHHHHIHHH',0,1,2,3,4,0,0,1,0)
        identifier=uuid.UUID('12345678-1234-5678-90ab-1234567890ab');raw[base+144:base+160]=identifier.bytes_le;raw[base+160:base+169]=b'\0Fixture\0'
        observed=capture.inspect_assembly(bytes(raw));self.assertEqual('Fixture',observed['assemblyName']);self.assertEqual('1.2.3.4',observed['version']);self.assertEqual(str(identifier),observed['mvid'])
        raw[base+116]=255
        with self.assertRaises(ValueError):capture.inspect_assembly(bytes(raw))
    def test_closed_current_source_profile_authenticates_actual_candidate_bytes(self):
        d=self.consumer()
        if not (HERE/'current-profile.json').exists():self.fail('Closed current source profile has not been sealed')
        profile=d.authenticate_bytes(HERE.parents[1]);self.assertIs(False,profile['sdkGranted']);self.assertIs(False,profile['runtimeQualified'])
    def test_changed_source_controls_archives_and_loaded_verifier_cannot_be_resealed(self):
        import tempfile,shutil
        d=self.consumer()
        if not d.EXPECTED_FILES:self.fail('Exact current source/control bindings are missing')
        root=HERE.parents[1]
        with tempfile.TemporaryDirectory(prefix='.company-source-controls-',dir=root.parent) as temp:
            target=Path(temp)/'source'
            for name in [*d.EXPECTED_FILES,d.MODULE,d.PROFILE]:
                destination=target/name;destination.parent.mkdir(parents=True,exist_ok=True)
                shutil.copyfile(root/name,destination)
            for name in ['Legacy.Maliev.Web.Tests/BillingProofLifetime.cs','acceptance/MemberCompanyCatalogPersistence/test_retain.py','acceptance/MemberCompanyCatalogPersistence/test_current_consumer.py',next(iter(d.ARCHIVES.values()))['path'],d.MODULE]:
                raw=(target/name).read_bytes();(target/name).write_bytes(raw+b'\n')
                with self.subTest(name=name),self.assertRaises(ValueError):d.authenticate_bytes(target)
                (target/name).write_bytes(raw)
            profile=d.parse((target/d.PROFILE).read_bytes());profile['sdkGranted']=0;(target/d.PROFILE).write_text(json.dumps(profile))
            with self.assertRaises(ValueError):d.authenticate_bytes(target)
    def test_six_actual_observation_control_identity_gate_rejects_missing_or_skipped_rows(self):
        import tempfile,uuid,xml.etree.ElementTree as ET
        d=self.consumer()
        if not hasattr(d,'observation_controls'):self.fail('Separate actual six-row observation-control gate is missing')
        ns='http://microsoft.com/schemas/VisualStudio/TeamTest/2010';tag=lambda x:'{'+ns+'}'+x
        root=ET.Element(tag('TestRun'));results=ET.SubElement(root,tag('Results'));definitions=ET.SubElement(root,tag('TestDefinitions'));entries=ET.SubElement(root,tag('TestEntries'));summary=ET.SubElement(root,tag('ResultSummary'))
        counters=ET.SubElement(summary,tag('Counters'),total='6',executed='6',passed='6',**{key:'0' for key in d.NONPASSING})
        for name in d.OBSERVATION_NAMES:
            test=str(uuid.uuid4());execution=str(uuid.uuid4());ET.SubElement(results,tag('UnitTestResult'),testId=test,executionId=execution,testName=name,outcome='Passed');definition=ET.SubElement(definitions,tag('UnitTest'),id=test,name=name);ET.SubElement(definition,tag('Execution'),id=execution);ET.SubElement(entries,tag('TestEntry'),testId=test,executionId=execution)
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'source-controls.trx';ET.ElementTree(root).write(path)
            self.assertEqual(6,d.observation_controls(path)['passed'])
            counters.set('notExecuted','1');ET.ElementTree(root).write(path)
            with self.assertRaises(ValueError):d.observation_controls(path)
            counters.set('notExecuted','0');results[0].set('outcome','Failed');ET.ElementTree(root).write(path)
            with self.assertRaises(ValueError):d.observation_controls(path)

    def test_original_journey_and_all_unmodified_helper_suffix_bytes_are_preserved(self):
        d=self.consumer();name='acceptance/MemberCompanyCatalogPersistence/Tests/MemberCompanyCatalogPersistenceTests.cs'
        original=d.read(HERE.parents[1],d.ARCHIVES[name]['path']);actual=(HERE/'Tests/MemberCompanyCatalogPersistenceTests.cs').read_bytes()
        start=b'/// <summary>Passive observation only;'
        suffix=b'/// <summary>Own synchronous request listener; retains only known synthetic phase counts and joins closure before host cleanup.</summary>'
        self.assertEqual(1,original.count(suffix));self.assertEqual(1,actual.count(suffix),'Original Company browser observer suffix was removed')
        self.assertEqual(original.split(start)[0],actual.split(start)[0])
        self.assertEqual(original[original.index(suffix):],actual[actual.index(suffix):])
        import re
        declarations=lambda raw:re.findall(rb'\b(?:class|record)\s+([A-Za-z_][A-Za-z0-9_]*)',raw)
        self.assertEqual(declarations(original),declarations(actual),'Whole-file original helper declaration roster must be preserved')
    def test_current_observer_exact_regex_assertions_are_xunit_analyzer_compatible(self):
        raw=(HERE/'Tests/CurrentCompanySourceObservation.cs').read_text()
        self.assertIn(r'Assert.Matches("\\A[a-f0-9]{40}\\z", candidate ?? string.Empty);',raw)
        self.assertIn(r'Assert.Matches("\\A[a-f0-9]{40}\\z", parts[1]);',raw)
        self.assertNotIn('Assert.True(Regex.IsMatch(',raw)

if __name__=='__main__':
    if '--historical' in sys.argv:
        import current_consumer as current
        from historical_controls import historical_suite
        current.authenticate_sources()
        with historical_suite() as original:
            suite=unittest.TestSuite([unittest.defaultTestLoader.loadTestsFromTestCase(CurrentConsumerControls),original])
            result=unittest.TextTestRunner().run(suite)
        sys.exit(not result.wasSuccessful())
    unittest.main()
