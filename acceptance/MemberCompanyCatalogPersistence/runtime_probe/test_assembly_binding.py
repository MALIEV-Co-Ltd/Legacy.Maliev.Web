import copy,unittest
from pathlib import Path
from assembly_binding import bind,WEB,COMPANY,COPIED_WEB

class AssemblyBindingControls(unittest.TestCase):
    def test_actual_test_outputs_and_company_web_copy_must_match(self):
        root='/workspace';web=dict(name='Legacy.Maliev.Web.Tests',path=str(Path(root)/WEB),mvid='web',sha256='w');company=dict(name='MemberCompanyCatalogPersistence.Tests',path=str(Path(root)/COMPANY),mvid='company',sha256='c');copied={**web,'path':str(Path(root)/COPIED_WEB)}
        rows={WEB:b'web',COMPANY:b'company',COPIED_WEB:b'web'}
        def read(_,path,cap):self.assertEqual(16*1024*1024,cap);return rows[path]
        def inspect(raw):return dict(assemblyName='Legacy.Maliev.Web.Tests' if raw==b'web' else 'MemberCompanyCatalogPersistence.Tests',mvid='web' if raw==b'web' else 'company',sha256='w' if raw==b'web' else 'c')
        self.assertTrue(bind(dict(testAssembly=web,observerAssembly=web),'web',root,read,inspect));self.assertTrue(bind(dict(testAssembly=company,observerAssembly=copied),'company',root,read,inspect))
        altered=copy.deepcopy(copied);altered['sha256']='different'
        with self.assertRaises(ValueError):bind(dict(testAssembly=company,observerAssembly=altered),'company',root,read,inspect)
        altered=copy.deepcopy(copied);altered['path']='/foreign'
        with self.assertRaises(ValueError):bind(dict(testAssembly=company,observerAssembly=altered),'company',root,read,inspect)
        rows[COPIED_WEB]=b'company'
        with self.assertRaises(ValueError):bind(dict(testAssembly=company,observerAssembly=copied),'company',root,read,inspect)

if __name__=='__main__':unittest.main()
