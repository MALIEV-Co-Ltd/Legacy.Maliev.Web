import contextlib,os,stat,unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch
import cgroup_lease as lease

class LeaseControls(unittest.TestCase):
    def fake(self):
        value=lease.CgroupLease.__new__(lease.CgroupLease);value.receipt={'cgroupEmptyVerified':False,'cgroupRemoved':False};value.parent_fd=41;value.group_fd=42;value.name='codex-runtime-probe-test';value.created=True;value.generation=SimpleNamespace();return value
    def test_only_one_exact_cgroup2_mount_is_admitted(self):
        lease.mounted_v2('1 2 0:1 / /sys/fs/cgroup rw - cgroup2 cgroup rw\n')
        for value in ('','1 2 0:1 / /sys/fs/cgroup rw - tmpfs tmpfs rw\n','bad','1 2 0:1 / /sys/fs/cgroup rw - cgroup2 cgroup rw\n1 2 0:1 / /sys/fs/cgroup rw - cgroup2 cgroup rw\n'):
            with self.subTest(value=value),self.assertRaises(ValueError):lease.mounted_v2(value)
    def test_populated_or_unverified_group_is_never_removed(self):
        for same,events,procs in [(False,'populated 0',''),(True,'populated 1',''),(True,'populated 0','123')]:
            group=self.fake()
            with self.subTest(same=same,events=events,procs=procs),patch.object(group,'_same',return_value=same),patch.object(group,'_read',side_effect=lambda name:events if name=='cgroup.events' else procs),patch.object(lease.os,'rmdir') as remove,patch.object(lease.os,'close') as close:
                with self.assertRaises(ValueError):group.release()
                remove.assert_not_called();self.assertFalse(group.receipt['cgroupRemoved']);self.assertEqual(2,close.call_count)
    def test_only_empty_exact_owned_group_is_removed(self):
        group=self.fake()
        with patch.object(group,'_same',return_value=True),patch.object(group,'_read',side_effect=lambda name:'populated 0' if name=='cgroup.events' else ''),patch.object(lease.os,'rmdir') as remove,patch.object(lease.os,'stat',side_effect=FileNotFoundError),patch.object(lease.os,'close'):
            group.release()
        remove.assert_called_once_with('codex-runtime-probe-test',dir_fd=41);self.assertTrue(group.receipt['cgroupEmptyVerified']);self.assertTrue(group.receipt['cgroupRemoved'])
    def test_replacement_after_removal_is_not_accepted(self):
        group=self.fake()
        with patch.object(group,'_same',return_value=True),patch.object(group,'_read',side_effect=lambda name:'populated 0' if name=='cgroup.events' else ''),patch.object(lease.os,'rmdir'),patch.object(lease.os,'stat',return_value=object()),patch.object(lease.os,'close'):
            with self.assertRaises(ValueError):group.release()
        self.assertFalse(group.receipt['cgroupRemoved'])
    def test_creation_sets_only_child_caps_and_releases_empty_group(self):
        metadata=SimpleNamespace(st_mode=stat.S_IFDIR|0o700,st_uid=1000,st_dev=1,st_ino=8)
        def read(name):return 'populated 0' if name=='cgroup.events' else '' if name=='cgroup.procs' else lease.LIMITS[name]
        with patch.object(lease.sys,'platform','linux'),patch.dict(os.environ,{'GITHUB_ACTIONS':'true'}),patch.object(os,'O_DIRECTORY',0x10000,create=True),patch.object(os,'O_NOFOLLOW',0x20000,create=True),patch.object(os,'O_CLOEXEC',0x40000,create=True),patch.object(os,'geteuid',return_value=1000,create=True),patch.object(lease,'bounded_text',side_effect=['1 2 0:1 / /sys/fs/cgroup rw - cgroup2 cgroup rw\n','0::/\n']),patch.object(Path,'is_symlink',return_value=False),patch.object(Path,'is_dir',return_value=True),patch.object(os,'open',side_effect=[41,42]),patch.object(os,'mkdir') as mkdir,patch.object(os,'fstat',return_value=metadata),patch.object(os,'write',side_effect=lambda fd,raw:len(raw)) as write,patch.object(os,'close'),patch.object(lease.CgroupLease,'_same',return_value=True),patch.object(lease.CgroupLease,'_open_control',return_value=55) as control,patch.object(lease.CgroupLease,'_read',side_effect=read),patch.object(os,'rmdir') as remove,patch.object(os,'stat',side_effect=FileNotFoundError):
            receipt={};group=lease.CgroupLease(receipt).create();self.assertEqual(55,group.attachment_fd());group.release()
        self.assertEqual(1,mkdir.call_count);self.assertEqual(3,write.call_count);self.assertEqual({'memory.max','cpu.max','pids.max','cgroup.procs'},set(call.args[0] for call in control.call_args_list));self.assertTrue(receipt['cgroupRemoved']);self.assertFalse(receipt['globalControllerMutation']);remove.assert_called_once()
    def test_missing_child_controller_refuses_and_removes_only_empty_owned_group(self):
        metadata=SimpleNamespace(st_mode=stat.S_IFDIR|0o700,st_uid=1000,st_dev=1,st_ino=8)
        patches=[patch.object(lease.sys,'platform','linux'),patch.dict(os.environ,{'GITHUB_ACTIONS':'true'}),patch.object(os,'O_DIRECTORY',0x10000,create=True),patch.object(os,'O_NOFOLLOW',0x20000,create=True),patch.object(os,'O_CLOEXEC',0x40000,create=True),patch.object(os,'geteuid',return_value=1000,create=True),patch.object(lease,'bounded_text',side_effect=['1 2 0:1 / /sys/fs/cgroup rw - cgroup2 cgroup rw\n','0::/\n']),patch.object(Path,'is_symlink',return_value=False),patch.object(Path,'is_dir',return_value=True),patch.object(os,'open',side_effect=[41,42]),patch.object(os,'mkdir'),patch.object(os,'fstat',return_value=metadata),patch.object(os,'close'),patch.object(lease.CgroupLease,'_same',return_value=True),patch.object(lease.CgroupLease,'_open_control',side_effect=PermissionError('no controller')),patch.object(lease.CgroupLease,'_read',side_effect=lambda name:'populated 0' if name=='cgroup.events' else ''),patch.object(os,'stat',side_effect=FileNotFoundError)]
        with contextlib.ExitStack() as stack:
            for item in patches:stack.enter_context(item)
            remove=stack.enter_context(patch.object(os,'rmdir'))
            receipt={}
            with self.assertRaises(PermissionError):lease.CgroupLease(receipt).create()
        self.assertTrue(receipt['cgroupCreateFailure']);self.assertTrue(receipt['cgroupRemoved']);self.assertFalse(receipt['globalControllerMutation']);remove.assert_called_once()

if __name__=='__main__':unittest.main()
