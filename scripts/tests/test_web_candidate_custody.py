from contextlib import ExitStack
import datetime as dt
import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

sys.path.insert(0,str(Path(__file__).parents[1]))
import run_web_candidate_phase as m

class CustodyControls(unittest.TestCase):
    def test_actual_main_delivers_shutdown_sigterm_and_preserves_first_fault(self):
        for fault in (None, RuntimeError('cleanup first'), ValueError('birth first')):
            with self.subTest(fault=fault), tempfile.TemporaryDirectory() as directory:
                root=Path(directory);permit=root/'permit.json';permit.write_bytes(b'{}')
                class Pipe:
                    closed=False
                    def fileno(self):return 88
                    def close(self):self.closed=True
                child=SimpleNamespace(pid=123,stdout=Pipe(),returncode=0)
                def cleanup(proc,pidfd,record):
                    m.request_stop(m.signal.SIGTERM,None)
                    record.update(custodySettled=True,cleanupVerified=True)
                    if isinstance(fault,RuntimeError):raise fault
                original_read=Path.read_text
                def read(path,*args,**kwargs):
                    if 'stat'==path.name:
                        if isinstance(fault,ValueError):raise fault
                        return '123 (stub) '+ ' '.join(['0']*20)
                    return original_read(path,*args,**kwargs)
                args=['phase','--policy','unused','--permit',str(permit),'--evidence',str(root/'evidence'),'--phase','build','--id','late-stop','--','dotnet','build']
                grant={'allowedPhases':['build'],'expiresUtc':(dt.datetime.now(dt.timezone.utc)+dt.timedelta(minutes=10)).isoformat()}
                m.stop_requested=False
                try:
                    with ExitStack() as stack:
                        for context in (patch.object(sys,'argv',args),patch.object(m.admission.intake,'load_policy',return_value={}),patch.object(m.admission,'validate',return_value=grant),patch.object(m.admission,'census'),patch.object(m,'verify_capped_owner',return_value={'expiresUtc':grant['expiresUtc']}),patch.object(m,'remaining_seconds',return_value=100),patch.object(m,'phase_deadline',return_value=m.time.monotonic()+100),patch.object(m.shutil,'which',return_value='dotnet'),patch.object(m.os,'pidfd_open',side_effect=[99,77],create=True),patch.object(m.os,'close'),patch.object(m.os,'set_blocking',create=True),patch.object(m.signal,'signal',return_value=None),patch.object(m.subprocess,'Popen',return_value=child),patch.object(Path,'read_text',read),patch.object(m,'shutdown',side_effect=cleanup),patch.object(m,'observe_members',return_value=[]),patch.object(m.select,'select',side_effect=[([77],[],[]),([],[],[])])):stack.enter_context(context)
                        with self.assertRaises(type(fault) if fault else InterruptedError) as caught:m.main()
                        if fault:self.assertIs(fault,caught.exception)
                    receipt=json.loads((root/'evidence/late-stop.json').read_bytes())
                    self.assertFalse(receipt['phaseSucceeded']);self.assertTrue(receipt['custodySettled'])
                finally:m.stop_requested=False

    def test_cooperating_child_receives_grace_and_never_kill(self):
        events=[];record={}
        child=SimpleNamespace(pid=123,wait=lambda timeout:events.append('reap'))
        with patch.object(m,'observe_members',side_effect=[[{'pid':124}],[{'pid':124}],[],[]]),patch.object(m,'signal_owned_group',side_effect=lambda proc,sig:events.append(sig)),patch.object(m.time,'sleep') as sleep,patch.object(m.select,'select',return_value=([77],[],[])):
            m.shutdown(child,77,record)
        self.assertEqual([m.signal.SIGTERM,'reap'],events);sleep.assert_called_once()

    def test_cancel_during_last_cap_check_never_spawns_sdk(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);permit=root/'permit.json';permit.write_bytes(b'{}')
            context={'expiresUtc':(dt.datetime.now(dt.timezone.utc)+dt.timedelta(minutes=10)).isoformat()}
            calls=0
            def cap(*args):
                nonlocal calls
                calls+=1
                if calls==2:m.request_stop(m.signal.SIGTERM,None)
                return context
            args=['phase','--policy','unused','--permit',str(permit),'--evidence',str(root/'evidence'),'--phase','build','--id','test-cancel','--','dotnet','build']
            m.stop_requested=False
            try:
                with ExitStack() as stack:
                    stack.enter_context(patch.object(sys,'argv',args))
                    stack.enter_context(patch.object(m.admission.intake,'load_policy',return_value={}))
                    stack.enter_context(patch.object(m.admission,'validate',return_value={'allowedPhases':['build'],'expiresUtc':context['expiresUtc']}))
                    stack.enter_context(patch.object(m.admission,'census'))
                    stack.enter_context(patch.object(m,'verify_capped_owner',side_effect=cap))
                    stack.enter_context(patch.object(m,'remaining_seconds',return_value=100))
                    stack.enter_context(patch.object(m,'phase_deadline',return_value=100))
                    stack.enter_context(patch.object(m.shutil,'which',return_value='dotnet'))
                    stack.enter_context(patch.object(m.os,'pidfd_open',return_value=99,create=True))
                    stack.enter_context(patch.object(m.os,'close'))
                    stack.enter_context(patch.object(m.signal,'signal',return_value=None))
                    spawn = stack.enter_context(patch.object(m.subprocess,'Popen'))
                    with self.assertRaises(InterruptedError):m.main()
                    spawn.assert_not_called()
                self.assertEqual(2,calls)
            finally:m.stop_requested=False

    def test_cleanup_faults_and_interrupts_retain_child_until_reaped(self):
        for fault in (RuntimeError('TERM fault'),KeyboardInterrupt()):
            events=[]
            child=SimpleNamespace(pid=123,wait=lambda timeout:events.append('reap'))
            record={}
            def send(proc,sig):
                self.assertIs(child,proc)
                events.append(sig)
                if sig==m.signal.SIGTERM:raise fault
            with self.subTest(fault=type(fault).__name__),patch.object(m.signal,'SIGKILL',9,create=True),patch.object(m,'observe_members',side_effect=[[{'pid':124}],[{'pid':124}],[]]),patch.object(m,'signal_owned_group',side_effect=send),patch.object(m.select,'select',return_value=([77],[],[])):
                m.shutdown(child,77,record)
            self.assertEqual([m.signal.SIGTERM,9,'reap'],events)
            self.assertTrue(record['custodySettled']);self.assertTrue(record['cleanupVerified'])
            self.assertIn(type(fault).__name__,record['cleanupErrors'])

    def test_wait_failure_retries_same_child_without_releasing_custody(self):
        events=[]
        def wait(timeout):
            events.append('wait')
            if len(events)==1:raise TimeoutError()
        child=SimpleNamespace(pid=123,wait=wait);record={}
        with patch.object(m,'observe_members',return_value=[]),patch.object(m.select,'select',return_value=([77],[],[])),patch.object(m.time,'sleep'):
            m.shutdown(child,77,record)
        self.assertEqual(['wait','wait'],events);self.assertTrue(record['custodySettled'])

    def test_actual_main_preserves_birth_failure_through_failed_term_and_close_interrupt(self):
        for close_interrupt in (False,True):
            with tempfile.TemporaryDirectory() as directory:
                root=Path(directory);permit=root/'permit.json';permit.write_bytes(b'{}')
                events=[];birth=ValueError('birth observation failed')
                class Pipe:
                    closed=False
                    attempted=False
                    def fileno(self):return 88
                    def close(self):
                        events.append('pipe-close')
                        if close_interrupt and not self.attempted:
                            self.attempted=True;raise KeyboardInterrupt()
                        self.closed=True
                child=SimpleNamespace(pid=123,stdout=Pipe(),returncode=None)
                def wait(timeout):events.append('reap');child.returncode=-9
                child.wait=wait
                def send(proc,sig):
                    self.assertIs(child,proc);events.append('signal')
                    if sig==m.signal.SIGTERM:raise RuntimeError('TERM failed')
                args=['phase','--policy','unused','--permit',str(permit),'--evidence',str(root/'evidence'),'--phase','build','--id','test-birth','--','dotnet','build']
                grant={'allowedPhases':['build'],'expiresUtc':'2026-10-08T00:20:00+00:00'}
                with ExitStack() as stack:
                    stack.enter_context(self.subTest(close_interrupt=close_interrupt))
                    stack.enter_context(patch.object(sys,'argv',args))
                    stack.enter_context(patch.object(m.admission.intake,'load_policy',return_value={}))
                    stack.enter_context(patch.object(m.admission,'validate',return_value=grant))
                    stack.enter_context(patch.object(m.admission,'census'))
                    stack.enter_context(patch.object(m,'verify_capped_owner',return_value={'expiresUtc':(dt.datetime.now(dt.timezone.utc)+dt.timedelta(minutes=10)).isoformat()}))
                    stack.enter_context(patch.object(m,'remaining_seconds',return_value=100))
                    stack.enter_context(patch.object(m,'phase_deadline',return_value=100))
                    stack.enter_context(patch.object(m.shutil,'which',return_value='dotnet'))
                    stack.enter_context(patch.object(m.os,'pidfd_open',side_effect=[99,77],create=True))
                    stack.enter_context(patch.object(m.os,'close',side_effect=lambda fd:events.append('fd-close')))
                    stack.enter_context(patch.object(m.signal,'signal',return_value=None))
                    stack.enter_context(patch.object(m.signal,'SIGKILL',9,create=True))
                    spawn = stack.enter_context(patch.object(m.subprocess,'Popen',return_value=child))
                    stack.enter_context(patch.object(Path,'read_text',side_effect=birth))
                    stack.enter_context(patch.object(m,'observe_members',side_effect=[[{'pid':124}],[{'pid':124}],[]]))
                    stack.enter_context(patch.object(m,'signal_owned_group',side_effect=send))
                    stack.enter_context(patch.object(m.select,'select',side_effect=[([77],[],[]),([],[],[])]))
                    with self.assertRaises(ValueError) as actual:m.main()
                    self.assertIs(birth,actual.exception);spawn.assert_called_once()
                self.assertTrue(child.stdout.closed)
                self.assertLess(events.index('reap'),events.index('pipe-close'))
                receipt=json.loads((root/'evidence/test-birth.json').read_bytes())
                self.assertEqual('ValueError',receipt['phaseFailure']);self.assertTrue(receipt['custodySettled'])
                self.assertFalse(receipt['phaseSucceeded'])

class CapControls(unittest.TestCase):
    def fixture(self,root):
        proc=root/'proc';cg=root/'cgroup';target=cg/'owned/sdk.service'
        (proc/'self').mkdir(parents=True);(proc/'sys/kernel/random').mkdir(parents=True);target.mkdir(parents=True)
        (proc/'self/cgroup').write_text('0::/owned/sdk.service\n');(proc/'sys/kernel/random/boot_id').write_text('boot-id\n')
        for name,value in {'memory.max':str(3*1024**3),'memory.swap.max':'0','cpu.max':'100000 100000','pids.max':'512'}.items():(target/name).write_text(value)
        st=target.stat();context={'owner':m.admission.OWNER,'unit':'sdk.service','invocationId':'actual-invocation','cgroup':'/owned/sdk.service','device':st.st_dev,'inode':st.st_ino,'hostBootId':'boot-id','expiresUtc':(dt.datetime.now(dt.timezone.utc)+dt.timedelta(minutes=10)).isoformat()}
        raw=json.dumps(context).encode();file=root/'context.json';file.write_bytes(raw)
        policy={'sdkOwnerContextSha256':m.admission.intake.sha256(raw)}
        show={'Id':'sdk.service','InvocationID':'actual-invocation','ControlGroup':'/owned/sdk.service','ActiveState':'active','RuntimeMaxUSec':'35min','KillMode':'control-group','SendSIGKILL':'yes'}
        return policy,file,proc,cg,target,show,context

    def test_observed_existing_unit_caps_pass(self):
        with tempfile.TemporaryDirectory() as directory:
            p,f,proc,cg,target,show,expected=self.fixture(Path(directory))
            self.assertEqual(expected,m.verify_capped_owner(p,f,proc,cg,show))

    def test_uncapped_kernel_values_fail(self):
        for name,value in (('memory.max','max'),('memory.max',str(4*1024**3)),('memory.swap.max','max'),('cpu.max','max 100000'),('cpu.max','200000 100000'),('pids.max','max')):
            with self.subTest(name=name,value=value),tempfile.TemporaryDirectory() as directory:
                p,f,proc,cg,target,show,_=self.fixture(Path(directory));(target/name).write_text(value)
                with self.assertRaises(ValueError):m.verify_capped_owner(p,f,proc,cg,show)

    def test_changed_identity_or_expiry_manager_rejected(self):
        for name,value in (('InvocationID','other'),('RuntimeMaxUSec','infinity'),('ControlGroup','/foreign'),('KillMode','process'),('ActiveState','inactive')):
            with self.subTest(name=name),tempfile.TemporaryDirectory() as directory:
                p,f,proc,cg,target,show,_=self.fixture(Path(directory));show[name]=value
                with self.assertRaises(ValueError):m.verify_capped_owner(p,f,proc,cg,show)

    def test_unenrolled_host_or_context_mutation_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            p,f,proc,cg,target,show,_=self.fixture(Path(directory))
            (proc/'self/cgroup').write_text('0::/foreign\n')
            with self.assertRaises(ValueError):m.verify_capped_owner(p,f,proc,cg,show)
            f.write_bytes(f.read_bytes()+b' ')
            with self.assertRaises(ValueError):m.verify_capped_owner(p,f,proc,cg,show)
        with self.assertRaises(ValueError):m.verify_capped_owner({},None)

if __name__=='__main__':unittest.main()
