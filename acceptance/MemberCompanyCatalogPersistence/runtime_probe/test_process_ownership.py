import unittest
from pathlib import Path
from unittest.mock import patch
from process_ownership import descended,identity

class OwnershipControls(unittest.TestCase):
    def test_stat_with_parentheses_is_bound_to_pid_birth_and_executable(self):
        fields=['S','10']+['0']*17+['123']
        stat='23 (host (worker)) '+' '.join(fields)
        def read(path):return '11111111-1111-1111-1111-111111111111' if str(path).endswith('boot_id') else stat
        with patch.object(Path,'read_text',autospec=True,side_effect=read),patch('process_ownership.os.readlink',return_value='/usr/share/dotnet/dotnet'),patch('process_ownership.os.path.realpath',return_value='/usr/share/dotnet/dotnet'),patch('process_ownership.os.path.isabs',return_value=True):
            self.assertEqual(dict(pid=23,startTicks='123',bootId='11111111-1111-1111-1111-111111111111',executable='/usr/share/dotnet/dotnet',parentId='10'),identity(23))
            with self.assertRaises(ValueError):identity(24)
    def test_ancestry_is_bounded_and_requires_same_launcher_generation(self):
        def row(pid,parent,ticks='100',boot='b'):return dict(pid=pid,parentId=str(parent),startTicks=ticks,bootId=boot,executable='/usr/share/dotnet/dotnet')
        launcher=row(10,1,'500');graph={10:launcher,20:row(20,10),30:row(30,20)}
        self.assertTrue(descended(30,launcher,graph.__getitem__))
        self.assertFalse(descended(30,{**launcher,'startTicks':'501'},graph.__getitem__))
        self.assertFalse(descended(30,{**launcher,'bootId':'other'},graph.__getitem__))
        self.assertFalse(descended(30,launcher,graph.__getitem__,maximum=2))
        graph[20]=row(20,30)
        self.assertFalse(descended(30,launcher,graph.__getitem__))
        graph[20]=row(20,10,boot='other')
        self.assertFalse(descended(30,launcher,graph.__getitem__))
        del graph[20]
        self.assertFalse(descended(30,launcher,graph.__getitem__))
        with self.assertRaises(ValueError):descended(30,launcher,graph.__getitem__,maximum=17)

if __name__=='__main__':unittest.main()
