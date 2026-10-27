import json,unittest
from claim_testhost import claim,parse,OwnedClaimError

class ClaimControls(unittest.TestCase):
    def setUp(self):
        assembly=dict(name='Legacy.Maliev.Web.Tests',path='/workspace/web.dll',mvid='11111111-1111-1111-1111-111111111111',sha256='b'*64)
        self.row=dict(pid=23,startTicks='345',bootId='11111111-1111-1111-1111-111111111111',executable='/usr/share/dotnet/dotnet',parentId='22',assemblyName='Microsoft.Extensions.Http',assemblyVersion='10.0.0.0',informationalVersion='10.0.14+0427b9abbead3fa64c6655eb4290c537397131fa',mvid='0d527044-86c2-40e8-aa46-f5fb1a756de6',assemblyPath='/usr/share/dotnet/shared/Microsoft.AspNetCore.App/10.0.14/Microsoft.Extensions.Http.dll',sha256='a'*64,testAssembly=assembly,observerAssembly=dict(assembly),backendAllocated=False)
        self.actual={k:self.row[k] for k in ('pid','startTicks','bootId','executable','parentId')};self.raw=json.dumps(self.row).encode();self.launcher={'pid':21}
    def test_claim_acknowledges_only_after_double_identity_and_pidfd(self):
        events=[]
        def lookup(pid):events.append('identity');return self.actual
        result,fd=claim(self.raw,self.launcher,lookup,lambda *_:True,lambda pid:(events.append('pidfd') or 42),lambda fd:events.append('close'),lambda:events.append('ack'))
        self.assertEqual((self.row,42),(result,fd));self.assertEqual(['identity','pidfd','identity','ack'],events)
    def test_reuse_or_failed_ack_closes_exact_fd_and_never_claims(self):
        for failure in ('reused','ack'):
            events=[];calls=[]
            def lookup(pid):
                calls.append(pid)
                return {**self.actual,'startTicks':'346'} if failure=='reused' and len(calls)==2 else self.actual
            def ack():
                events.append('ack')
                if failure=='ack':raise OSError('ack failed')
            if failure=='reused':
                with self.assertRaises(ValueError):claim(self.raw,self.launcher,lookup,lambda *_:True,lambda _:42,lambda fd:events.append(('close',fd)),ack)
                self.assertEqual(('close',42),events[-1]);self.assertNotIn('ack',events)
            else:
                with self.assertRaises(OwnedClaimError) as caught:claim(self.raw,self.launcher,lookup,lambda *_:True,lambda _:42,lambda fd:events.append(('close',fd)),ack)
                self.assertEqual(42,caught.exception.fd);self.assertEqual(self.row,caught.exception.receipt);self.assertNotIn(('close',42),events)
    def test_malformed_or_foreign_identity_never_opens_pidfd(self):
        for raw in (b'{"pid":23,"pid":24}',b'{"x":NaN}',b'x'*16385,b'{}'):
            calls=[]
            with self.assertRaises((ValueError,UnicodeError)):claim(raw,self.launcher,lambda _:self.actual,lambda *_:True,lambda _:calls.append('open'),lambda _:None,lambda:None)
            self.assertEqual([],calls)
        with self.assertRaises(ValueError):claim(self.raw,self.launcher,lambda _:self.actual,lambda *_:False,lambda _:self.fail('must not open'),lambda _:None,lambda:None)

if __name__=='__main__':unittest.main()
