import os,stat,unittest
from types import SimpleNamespace
from unittest.mock import patch
import owned_receipt_file as owned

class ReceiptFileControls(unittest.TestCase):
    def row(self,**changes):
        values=dict(st_mode=stat.S_IFREG|0o600,st_nlink=1,st_uid=1000,st_size=3,st_dev=1,st_ino=5,st_mtime_ns=8);values.update(changes);return SimpleNamespace(**values)
    def test_no_follow_bounded_stable_owner_read_and_close(self):
        with patch.object(os,'O_NOFOLLOW',0x100000,create=True),patch.object(os,'O_CLOEXEC',0x200000,create=True),patch.object(os,'getuid',return_value=1000,create=True),patch.object(os,'open',return_value=42) as opening,patch.object(os,'fstat',side_effect=[self.row(),self.row()]),patch.object(os,'read',side_effect=[b'abc',b'']),patch.object(os,'close') as close:
            self.assertEqual(b'abc',owned.read_receipt('/private/receipt'))
            self.assertTrue(opening.call_args.args[1]&os.O_NOFOLLOW);close.assert_called_once_with(42)
    def test_link_foreign_oversize_and_race_fail_closed(self):
        bad=[self.row(st_mode=stat.S_IFLNK),self.row(st_nlink=2),self.row(st_uid=999),self.row(st_size=16385)]
        for before in bad:
            with self.subTest(before=before),patch.object(os,'O_NOFOLLOW',0x100000,create=True),patch.object(os,'O_CLOEXEC',0x200000,create=True),patch.object(os,'getuid',return_value=1000,create=True),patch.object(os,'open',return_value=42),patch.object(os,'fstat',return_value=before),patch.object(os,'close') as close:
                with self.assertRaises(ValueError):owned.read_receipt('/private/receipt')
                close.assert_called_once_with(42)
        with patch.object(os,'O_NOFOLLOW',0x100000,create=True),patch.object(os,'O_CLOEXEC',0x200000,create=True),patch.object(os,'getuid',return_value=1000,create=True),patch.object(os,'open',return_value=42),patch.object(os,'fstat',side_effect=[self.row(),self.row(st_mtime_ns=9)]),patch.object(os,'read',side_effect=[b'abc',b'']),patch.object(os,'close'):
            with self.assertRaises(ValueError):owned.read_receipt('/private/receipt')
    def test_ack_is_exclusive_nofollow_and_close_even_on_short_write(self):
        with patch.object(os,'O_NOFOLLOW',0x100000,create=True),patch.object(os,'O_CLOEXEC',0x200000,create=True),patch.object(os,'open',return_value=43) as opening,patch.object(os,'write',return_value=1),patch.object(os,'close') as close:
            with self.assertRaises(OSError):owned.acknowledge('/private/receipt.ack')
            self.assertTrue(opening.call_args.args[1]&os.O_EXCL);self.assertTrue(opening.call_args.args[1]&os.O_NOFOLLOW);close.assert_called_once_with(43)

if __name__=='__main__':unittest.main()
