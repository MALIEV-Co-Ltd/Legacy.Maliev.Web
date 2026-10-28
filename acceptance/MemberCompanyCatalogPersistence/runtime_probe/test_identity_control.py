import copy,unittest
from identity_control import validate

class IdentityControls(unittest.TestCase):
    def test_exact_owned_process_and_assembly_only(self):
        assembly=dict(name='Legacy.Maliev.Web.Tests',path='/workspace/web.dll',mvid='11111111-1111-1111-1111-111111111111',sha256='b'*64)
        row=dict(pid=23,startTicks='345',bootId='11111111-1111-1111-1111-111111111111',executable='/usr/share/dotnet/dotnet',parentId='22',assemblyName='Microsoft.Extensions.Http',assemblyVersion='10.0.0.0',informationalVersion='10.0.14+0427b9abbead3fa64c6655eb4290c537397131fa',mvid='0d527044-86c2-40e8-aa46-f5fb1a756de6',assemblyPath='/usr/share/dotnet/shared/Microsoft.AspNetCore.App/10.0.14/Microsoft.Extensions.Http.dll',sha256='a'*64,testAssembly=assembly,observerAssembly=copy.deepcopy(assembly),backendAllocated=False)
        actual={k:row[k] for k in ('pid','startTicks','bootId','executable','parentId')}
        self.assertEqual(row,validate(row,actual,21,lambda child,parent:(child,parent)==(23,21)))
        for key,bad in [('pid',21),('startTicks','0'),('bootId','x'),('bootId','00000000-0000-0000-0000-000000000000'),('parentId','0'),('backendAllocated',True),('assemblyName','Other'),('assemblyPath','/tmp/Microsoft.Extensions.Http.dll'),('sha256','z'*64),('mvid','00000000-0000-0000-0000-000000000000'),('informationalVersion','10+secret/unsafe')]:
            altered=copy.deepcopy(row);altered[key]=bad
            with self.subTest(key=key),self.assertRaises(ValueError):validate(altered,actual,21,lambda *_:True)
        with self.assertRaises(ValueError):validate(row,{**actual,'startTicks':'346'},21,lambda *_:True)
        with self.assertRaises(ValueError):validate(row,actual,21,lambda *_:False)
        with self.assertRaises(ValueError):validate({**row,'extra':True},actual,21,lambda *_:True)
        altered=copy.deepcopy(row);altered['testAssembly']['sha256']='z'*64
        with self.assertRaises(ValueError):validate(altered,actual,21,lambda *_:True)

if __name__=='__main__':unittest.main()
