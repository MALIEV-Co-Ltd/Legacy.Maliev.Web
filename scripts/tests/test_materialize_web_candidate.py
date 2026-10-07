"""Execute raw capsule rejection controls without allocating SDK or helper workers."""
import base64
import copy
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import stat
import tempfile
import unittest
import zipfile
from unittest.mock import patch
import sys
sys.path.insert(0,str(Path(__file__).parents[1]))

spec = importlib.util.spec_from_file_location("intake", Path(__file__).parents[1] / "materialize_web_candidate.py")
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

class CapsuleControls(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.files = {f"source/{n}.txt": b"Thai: \xe0\xb8\x97\xe0\xb8\x94\xe0\xb8\xaa\xe0\xb8\xad\xe0\xb8\x9a\r\n" for n in range(103)}
        cls.base_manifest = {"schemaVersion": 1, "owner": "test-owner", "sourceBindingSha256": "b" * 64,
                             "acceptedBase": "a" * 40, "sourcePins": {"Defaults": "c" * 40}}

    def fixture(self, files=None, alter=None):
        files = self.files if files is None else files
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name, value in files.items():
                info = zipfile.ZipInfo(name); info.create_system = 3
                info.external_attr = (stat.S_IFREG | 0o644) << 16
                info.compress_type = zipfile.ZIP_DEFLATED
                archive.writestr(info, value)
        raw = stream.getvalue()
        manifest = copy.deepcopy(self.base_manifest)
        manifest.update(sourceFiles=[{"path": n, "sha256": m.sha256(v), "bytes": len(v)} for n, v in files.items()],
                        capsuleBytes=len(raw), capsuleSha256=m.sha256(raw))
        source_rows = list(manifest["sourceFiles"])
        source_rows += [{"path": f"base/{n}.txt", "sha256": "0" * 64, "bytes": 0} for n in range(1983-len(source_rows))]
        manifest["sourceFiles"] = source_rows
        manifest["capsuleRows"] = []
        with zipfile.ZipFile(io.BytesIO(raw)) as archive:
            for info in archive.infolist():
                row=next(r for r in source_rows if r["path"]==info.filename)
                manifest["capsuleRows"].append(dict(row,compressedBytes=info.compress_size,crc32=info.CRC,createSystem=info.create_system,externalAttributes=info.external_attr,flags=info.flag_bits,compression=info.compress_type,dateTime=list(info.date_time)))
        if alter: alter(manifest)
        data = json.dumps(manifest).encode()
        policy = {k: manifest[k] for k in ("owner", "sourceBindingSha256", "acceptedBase", "sourcePins", "sourceFiles", "capsuleRows")}
        policy["manifestSha256"] = m.sha256(data)
        return data, raw, policy

    def test_exact_utf8_crlf_roundtrip(self):
        manifest, raw, policy = self.fixture()
        _, actual = m.validate_capsule(manifest, raw, policy)
        self.assertEqual(self.files, actual)

    def test_unsafe_paths(self):
        for name in ("../escape", "/absolute", "C:/drive", "source\\escape", ".git/config", "source//empty", "source/./dot"):
            with self.subTest(name=name), self.assertRaises(ValueError): m.canonical_path(name)

    def test_duplicate_json_rejected(self):
        with self.assertRaises(ValueError): m.parse_json(b'{"owner":1,"owner":2}')

    def test_owner_binding_base_and_pins(self):
        for key in ("owner", "sourceBindingSha256", "acceptedBase", "sourcePins"):
            data, raw, policy = self.fixture(); policy[key] = "changed"
            with self.subTest(key=key), self.assertRaises(ValueError): m.validate_capsule(data, raw, policy)

    def test_manifest_hash_mismatch(self):
        data, raw, policy = self.fixture(); policy["manifestSha256"] = "0" * 64
        with self.assertRaises(ValueError): m.validate_capsule(data, raw, policy)

    def test_archive_hash_and_size_mismatch(self):
        data, raw, policy = self.fixture()
        for altered in (raw + b"x", bytes([raw[0] ^ 1]) + raw[1:]):
            with self.assertRaises(ValueError): m.validate_capsule(data, altered, policy)

    def test_case_collision_rejected(self):
        files = dict(self.files); files.pop("source/0.txt"); files["SOURCE/1.txt"] = b"collision"
        data, raw, policy = self.fixture(files)
        with self.assertRaises(ValueError): m.validate_capsule(data, raw, policy)

    def test_file_hash_mismatch(self):
        data, raw, policy = self.fixture(alter=lambda obj: obj["sourceFiles"][0].update(sha256="0" * 64))
        with self.assertRaises(ValueError): m.validate_capsule(data, raw, policy)

    def test_symlink_archive_rejected(self):
        data, raw, policy = self.fixture()
        stream = io.BytesIO()
        with zipfile.ZipFile(io.BytesIO(raw)) as original, zipfile.ZipFile(stream, "w") as archive:
            for number, info in enumerate(original.infolist()):
                if number == 0: info.external_attr = (stat.S_IFLNK | 0o777) << 16
                archive.writestr(info, original.read(info))
        obj = json.loads(data); changed = stream.getvalue(); obj.update(capsuleBytes=len(changed), capsuleSha256=m.sha256(changed))
        data = json.dumps(obj).encode(); policy["manifestSha256"] = m.sha256(data)
        with self.assertRaises(ValueError): m.validate_capsule(data, changed, policy)

    def test_missing_inventory_rejected(self):
        data,raw,policy=self.fixture();changed=json.loads(data);changed["sourceFiles"].pop();data=json.dumps(changed).encode();policy["sourceFiles"]=changed["sourceFiles"];policy["manifestSha256"]=m.sha256(data)
        with self.assertRaises(ValueError):m.validate_capsule(data,raw,policy)

    def test_git_api_blob_identity(self):
        raw = b"raw\r\n"; oid = hashlib.sha1(b"blob 5\0" + raw).hexdigest()
        response = json.dumps(dict(sha=oid, encoding="base64", size=5, content=base64.b64encode(raw).decode())).encode()
        self.assertEqual(raw, m.decode_blob(response, oid))
        with self.assertRaises(ValueError): m.decode_blob(response, "0" * 40)

    def test_dirty_checkout_rejected_before_write(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with patch.object(m.subprocess, "check_output", side_effect=["a" * 40 + "\n", b" M unrelated"]):
                with self.assertRaises(ValueError): m.materialize(root, {"acceptedBase": "a" * 40}, {"new.txt": b"never"})
            self.assertFalse((root / "new.txt").exists())

    def test_redirect_refused(self):
        with self.assertRaises(ValueError): m.NoRedirect().redirect_request(None, None, 302, "", {}, "https://elsewhere.invalid")

if __name__ == "__main__": unittest.main()
