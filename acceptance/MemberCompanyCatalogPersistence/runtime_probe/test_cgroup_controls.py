import stat,unittest
from types import SimpleNamespace
from cgroup_controls import current_group,populated,same_directory

class CgroupControls(unittest.TestCase):
    def test_only_one_safe_v2_membership_is_admitted(self):
        self.assertEqual('/runner.slice/task-1',current_group('0::/runner.slice/task-1\n'))
        self.assertEqual('/',current_group('0::/\n'))
        for bad in ('1:cpu:/foreign\n','0::/a/../b\n','0::/a//b\n','0::/a\n0::/b\n','0::/a b\n'):
            with self.subTest(bad=bad),self.assertRaises(ValueError):current_group(bad)
    def test_populated_evidence_is_exact_and_unambiguous(self):
        self.assertFalse(populated('populated 0\nfrozen 0\n'));self.assertTrue(populated('populated 1\n'))
        for bad in ('','populated 2\n','populated 0\npopulated 1\n','other 0\n','populated -1\n'):
            with self.subTest(bad=bad),self.assertRaises(ValueError):populated(bad)
    def test_directory_generation_changes_are_detected(self):
        row=SimpleNamespace(st_dev=1,st_ino=2,st_uid=3,st_mode=stat.S_IFDIR|0o700)
        self.assertTrue(same_directory(row,row));self.assertFalse(same_directory(row,SimpleNamespace(st_dev=1,st_ino=9,st_uid=3,st_mode=row.st_mode)));self.assertFalse(same_directory(SimpleNamespace(),SimpleNamespace()))

if __name__=='__main__':unittest.main()
