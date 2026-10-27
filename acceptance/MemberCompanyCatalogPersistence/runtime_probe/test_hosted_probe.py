import contextlib,json,os,stat,sys,tempfile,unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch
import hosted_probe as probe

class HostedProbeControls(unittest.TestCase):
    def test_output_binding_imports_only_the_authenticated_source_parent(self):
        current=SimpleNamespace(read=object());capture=SimpleNamespace(inspect_assembly=object())
        with patch.dict(sys.modules,{'current_consumer':current,'capture_build_source':capture}),patch.object(sys,'path',[]),patch.object(probe,'bind_assemblies',return_value=True) as bind:
            self.assertTrue(probe.verify_output_assemblies({},'web','/root'))
            self.assertEqual(str(Path(probe.__file__).resolve().parent.parent),sys.path[0]);bind.assert_called_once_with({},'web','/root',current.read,capture.inspect_assembly)
    def test_group_entry_occurs_before_exec_and_short_write_exits(self):
        with patch.object(os,'write',return_value=1) as writer,patch.object(os,'_exit',side_effect=SystemExit) as stop:
            probe.enter_owned_group(44);writer.assert_called_once_with(44,b'0');stop.assert_not_called()
        with patch.object(os,'write',return_value=0),patch.object(os,'_exit',side_effect=SystemExit) as stop:
            with self.assertRaises(SystemExit):probe.enter_owned_group(44)
            stop.assert_called_once_with(126)
    def test_pidfd_errors_do_not_count_as_process_exit(self):
        self.assertFalse(probe.terminal_events([],42));self.assertTrue(probe.terminal_events([(42,1)],42));self.assertTrue(probe.terminal_events([(42,17)],42))
        for events in ([(42,8)],[(42,32)],[(43,1)],[(42,1),(42,1)],[(42,64)]):
            with self.subTest(events=events),self.assertRaises(ValueError):probe.terminal_events(events,42)
    def test_mocked_lifecycle_requires_empty_group_before_cleanup(self):
        for mode in ('success','populated','spawn-failure','pidfd-failure','output-overflow','testhost-timeout','cgroup-failure','claim-failure','launcher-outside-cgroup','deadline','trx-invalid','wrong-assembly'):
            group_empty=mode not in ('populated','cgroup-failure','claim-failure','wrong-assembly')
            with self.subTest(mode=mode),tempfile.TemporaryDirectory(prefix='runtime-lifecycle-',dir=Path(__file__).parent) as root:
                root=Path(root).resolve();out=root/'member-company-retained';out.mkdir();observed={'pid':23,'startTicks':'456'}
                class FakeProcess:
                    pid=21;returncode=0
                    stdout=SimpleNamespace(fileno=lambda:77,close=lambda:None)
                    def poll(self):return self.returncode
                class FakeSelector:
                    def __init__(self):self.reads=0
                    def register(self,*_):pass
                    def unregister(self,*_):pass
                    def close(self):pass
                    def select(self,*_):self.reads+=1;return [(SimpleNamespace(fd=77,fileobj='pipe'),None)]
                class FakeLease:
                    membership='/owned'
                    def __init__(self,receipt):self.receipt=receipt;self.parent_fd=41;self.group_fd=42
                    def create(self):
                        if mode=='cgroup-failure':raise PermissionError('delegation unavailable')
                        return self
                    def attachment_fd(self):return 44
                    def release(self):
                        if not group_empty:raise ValueError('still populated')
                        self.receipt.update(cgroupEmptyVerified=True,cgroupRemoved=True);self.parent_fd=None;self.group_fd=None
                def fake_settle(fd,terminal,send,wait,receipt):receipt.update(terminalVerified=True,descendantSettlement='unproved')
                def fake_claim(raw,launcher,lookup,descended,pidfd,close,ack):
                    if mode=='claim-failure':raise ValueError('identity changed')
                    try:ack()
                    except Exception as error:raise probe.OwnedClaimError(observed,93,type(error).__name__) from error
                    return observed,93
                identity=dict(pid=21,startTicks='123',bootId='b',executable=probe.DOTNET,parentId='1')
                original_lstat=Path.lstat
                def safe_lstat(path,*args,**kwargs):
                    if path.name.startswith('owned-runtime-probe-') or path.name=='.member-company-runtime-probe':
                        value=original_lstat(path,*args,**kwargs);return SimpleNamespace(st_mode=stat.S_IFDIR|0o700,st_uid=1000,st_dev=value.st_dev,st_ino=value.st_ino)
                    return original_lstat(path,*args,**kwargs)
                patches=[patch.object(probe.sys,'platform','linux'),patch.dict(os.environ,{'GITHUB_ACTIONS':'true','GITHUB_WORKSPACE':str(root)},clear=True),patch.object(Path,'lstat',autospec=True,side_effect=safe_lstat),patch.object(os,'getuid',return_value=1000,create=True),patch.object(os,'geteuid',return_value=1000,create=True),patch.object(os,'pidfd_open',side_effect=[91,OSError('pidfd denied')] if mode=='pidfd-failure' else [91,92],create=True),patch.object(os,'close'),patch.object(os,'set_blocking'),patch.object(os,'read',side_effect=[b'output',b'']),patch.object(probe.signal,'pidfd_send_signal',create=True),patch.object(probe,'bounded_text',return_value='0::/owned\n'),patch.object(probe,'available_kib',return_value=5000000),patch.object(probe,'birth_evidence',return_value={'startUtcApprox':'synthetic'}),patch.object(probe,'CgroupLease',FakeLease),patch.object(probe,'identity',return_value=identity),patch.object(probe,'current_group',return_value='/foreign' if mode=='launcher-outside-cgroup' else '/owned'),patch.object(probe,'read_receipt',return_value=json.dumps(observed).encode()),patch.object(probe,'read_trx',return_value=b'synthetic'),patch.object(probe,'verify_trx',side_effect=ValueError('bad trx') if mode=='trx-invalid' else None,return_value={'passed':1}),patch.object(probe,'verify_output_assemblies',side_effect=ValueError('wrong assembly') if mode=='wrong-assembly' else None,return_value=True),patch.object(probe,'claim',side_effect=fake_claim),patch.object(probe,'acknowledge'),patch.object(probe,'terminal',return_value=True),patch.object(probe,'wait_terminal',return_value=mode!='testhost-timeout'),patch.object(probe,'settle',side_effect=fake_settle),patch.object(probe,'LIMIT',4 if mode=='output-overflow' else probe.LIMIT),patch.object(probe.selectors,'DefaultSelector',return_value=FakeSelector())]
                with contextlib.ExitStack() as stack:
                    for item in patches:stack.enter_context(item)
                    if mode=='deadline':stack.enter_context(patch.object(probe.time,'monotonic',side_effect=[0,61,62]))
                    spawn=stack.enter_context(patch.object(probe.subprocess,'Popen',return_value=FakeProcess(),side_effect=OSError('spawn failed') if mode=='spawn-failure' else None))
                    if mode=='success':
                        summary=probe.run_one('company',root,out);self.assertTrue(summary['success']);self.assertEqual('owned-cgroup-empty',summary['descendantSettlement']);self.assertEqual('verified',summary['privateCleanup'])
                    elif mode=='populated':
                        with self.assertRaises(ValueError):probe.run_one('company',root,out)
                        summary=json.loads((out/'company-runtime-probe.json').read_text());self.assertFalse(summary['success']);self.assertEqual('unproved',summary['descendantSettlement']);self.assertEqual('ValueError',summary['cgroupCleanupFailure']);self.assertEqual('ValueError',summary['privateCleanupFailure'])
                    elif mode=='spawn-failure':
                        with self.assertRaises(OSError):probe.run_one('company',root,out)
                        summary=json.loads((out/'company-runtime-probe.json').read_text());self.assertFalse(summary['success']);self.assertEqual('OSError',summary['failure']);self.assertEqual('owned-cgroup-empty',summary['descendantSettlement']);self.assertEqual('verified',summary['privateCleanup'])
                    else:
                        with self.assertRaises((ValueError,OSError,probe.OwnedClaimError)):probe.run_one('company',root,out)
                        summary=json.loads((out/'company-runtime-probe.json').read_text());self.assertFalse(summary['success'])
                        if mode=='output-overflow':self.assertEqual(6,summary['outputBytes']);self.assertFalse(summary['pipeEof'])
                        if mode=='pidfd-failure':self.assertFalse(summary['launcher']['terminalVerified'])
                        if mode=='cgroup-failure':self.assertEqual('not-started',summary['descendantSettlement']);spawn.assert_not_called()
                        if mode=='claim-failure':self.assertEqual('unproved',summary['descendantSettlement']);self.assertIsNone(summary['testHost'])
                        if mode=='wrong-assembly':self.assertEqual('OwnedClaimError',summary['failure']);self.assertTrue(summary['testHost']['cleanup']['terminalVerified'])
                        if mode=='deadline':self.assertEqual('TimeoutError',summary['failure']);self.assertIsNone(summary['testHost'])
                    if mode!='cgroup-failure':self.assertEqual((44,),spawn.call_args.kwargs['pass_fds']);self.assertTrue(callable(spawn.call_args.kwargs['preexec_fn']))
    def test_non_hosted_preflight_never_spawns(self):
        with patch.object(probe.sys,'platform','win32'),patch.object(probe.subprocess,'Popen') as spawn:
            with self.assertRaises(ValueError):probe.run_one('company','/tmp','/tmp')
            spawn.assert_not_called()
    def test_unreviewed_injection_or_changed_identity_never_spawns(self):
        for changed_uid,injection in ((True,False),(False,True)):
            environment={'GITHUB_ACTIONS':'true'}
            if injection:environment['DOTNET_STARTUP_HOOKS']='/foreign/hook'
            with self.subTest(changed_uid=changed_uid,injection=injection),patch.object(probe.sys,'platform','linux'),patch.dict(os.environ,environment,clear=True),patch.object(os,'pidfd_open',create=True),patch.object(probe.signal,'pidfd_send_signal',create=True),patch.object(os,'getuid',return_value=1000,create=True),patch.object(os,'geteuid',return_value=0 if changed_uid else 1000,create=True),patch.object(probe.subprocess,'Popen') as spawn:
                with self.assertRaises(ValueError):probe.run_one('company','/tmp','/tmp')
                spawn.assert_not_called()
    def test_private_setup_failure_retains_receipt_and_preserves_existing_parent(self):
        with tempfile.TemporaryDirectory(prefix='runtime-setup-',dir=Path(__file__).parent) as value:
            root=Path(value);out=root/'member-company-retained';out.mkdir();foreign=root/'.member-company-runtime-probe';foreign.mkdir();(foreign/'foreign').write_text('preserve')
            with self.assertRaises(FileExistsError):probe.create_private(root,out,'web')
            self.assertTrue((foreign/'foreign').exists());record=json.loads((out/'web-runtime-probe-setup-refusal.json').read_text());self.assertEqual('unproved',record['cleanup']);self.assertEqual('FileExistsError',record['failure'])
    def test_private_creation_error_removes_only_the_new_empty_parent(self):
        with tempfile.TemporaryDirectory(prefix='runtime-setup-',dir=Path(__file__).parent) as value:
            root=Path(value);out=root/'member-company-retained';out.mkdir();original=Path.lstat
            def owned_stat(path,*args,**kwargs):
                row=original(path,*args,**kwargs)
                if path.name=='.member-company-runtime-probe':return SimpleNamespace(st_mode=stat.S_IFDIR|0o700,st_uid=1000,st_dev=row.st_dev,st_ino=row.st_ino)
                return row
            with patch.object(Path,'lstat',autospec=True,side_effect=owned_stat),patch.object(os,'getuid',return_value=1000,create=True),patch.object(probe.tempfile,'mkdtemp',side_effect=OSError('no space')):
                with self.assertRaises(OSError):probe.create_private(root,out,'web')
            self.assertFalse((root/'.member-company-runtime-probe').exists());record=json.loads((out/'web-runtime-probe-setup-refusal.json').read_text());self.assertEqual('verified',record['cleanup']);self.assertEqual('OSError',record['failure'])
    def test_only_pinned_host_owned_known_files_are_removed(self):
        with tempfile.TemporaryDirectory(prefix='runtime-probe-control-',dir=Path(__file__).parent) as root:
            private=Path(root)/'private';private.mkdir();(private/'observation.json').write_text('synthetic');(private/'observation.json.ack').write_text('ok')
            with patch.object(os,'getuid',return_value=private.stat().st_uid,create=True):probe.cleanup_private(private,True,True)
            self.assertFalse(private.exists())
    def test_unknown_file_or_unclaimed_host_is_preserved(self):
        with tempfile.TemporaryDirectory(prefix='runtime-probe-control-',dir=Path(__file__).parent) as root:
            private=Path(root)/'private';private.mkdir();known=private/'observation.json';known.write_text('synthetic')
            with self.assertRaises(ValueError):probe.cleanup_private(private,True,False)
            self.assertTrue(known.exists())
            with self.assertRaises(ValueError):probe.cleanup_private(private,False,True)
            self.assertTrue(known.exists());foreign=private/'unexpected';foreign.write_text('preserve')
            with self.assertRaises(ValueError):probe.cleanup_private(private,True,True)
            self.assertTrue(foreign.exists());self.assertTrue(known.exists())

if __name__=='__main__':unittest.main()
