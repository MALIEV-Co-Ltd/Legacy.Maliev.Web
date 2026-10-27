"""Closed six-input intake guards; no encoder or network execution."""
import copy
import importlib.util
import json
import os
from pathlib import Path
import resource
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("customer_row_proof", ROOT / "scripts/prove-about-customer-row-webp.py")
proof = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(proof)


class CustomerRowControls(unittest.TestCase):
    shared = None

    def setUp(self):
        self.raw = (ROOT / "tests/about-customer-row-webp-inputs.json").read_bytes()
        self.manifest = proof.read_manifest(self.shared, self.raw)

    def test_exact_six_sources_are_accepted_without_changing_the_old_eighteen(self):
        self.assertEqual(proof.SLUGS, tuple(row["slug"] for row in self.manifest["assets"]))
        self.assertEqual(27_985, sum(row["source_bytes"] for row in self.manifest["assets"]))
        for row in self.manifest["assets"]:
            self.assertEqual(row["source_bytes"], len(proof.read_source(self.shared, ROOT, row)))
            self.assertTrue(set(proof.METADATA_KEYS).issubset(row))
        crown = self.manifest["assets"][-1]
        self.assertEqual("crown-ceramics", crown["slug"])
        self.assertEqual((234, 119, 11291, 180),
                         (crown["width"], crown["height"], crown["source_bytes"], crown["exif_bytes"]))
        self.assertEqual("ced9feec0d3626b1336947d8f89c994f962b119a5753a739236f80dda548ee6d",
                         crown["exif_sha256"])
        jpeg = self.manifest["assets"][2]
        self.assertEqual("JPEG", jpeg["format"])
        self.assertEqual((76, 74), (jpeg["width"], jpeg["height"]))

    def test_manifest_drift_is_rejected(self):
        for kind in ("missing", "duplicate", "reordered", "foreign-source", "foreign-output", "dimensions", "metadata", "source-commit", "superseded-index"):
            changed = copy.deepcopy(self.manifest)
            if kind == "missing":
                changed["assets"].pop()
            elif kind == "duplicate":
                changed["assets"][-1] = changed["assets"][0]
            elif kind == "reordered":
                changed["assets"].reverse()
            elif kind == "foreign-source":
                changed["assets"][0]["source_git_blob"] = "0" * 40
            elif kind == "foreign-output":
                changed["assets"][0]["derived_path"] = "Legacy.Maliev.Web/wwwroot/foreign.webp"
            elif kind == "dimensions":
                changed["assets"][0]["width"] += 1
            elif kind == "metadata":
                changed["assets"][0]["xmp_sha256"] = "0" * 64
            elif kind == "superseded-index":
                changed["assets"][-1]["slug"] = "index-creative-village"
            else:
                changed["source_commits"][0] = "0" * 40
            with self.subTest(kind=kind), self.assertRaisesRegex(ValueError, "manifest-identity"):
                proof.read_manifest(self.shared, json.dumps(changed).encode("utf-8"))

    def test_source_byte_drift_and_size_drift_are_rejected(self):
        row = self.manifest["assets"][0]
        source = proof.read_source(self.shared, ROOT, row)
        with tempfile.TemporaryDirectory(dir=os.environ["LOGO_WEBP_VENV"]) as temporary:
            root = Path(temporary)
            path = root / row["source_path"]
            path.parent.mkdir(parents=True)
            for changed in (source[:-1], source + b"x", bytes([source[0] ^ 1]) + source[1:]):
                path.write_bytes(changed)
                with self.assertRaisesRegex(ValueError, "source-bytes-identity"):
                    proof.read_source(self.shared, root, row)

    def test_symlink_and_unknown_source_are_rejected(self):
        row = self.manifest["assets"][0]
        with tempfile.TemporaryDirectory(dir=os.environ["LOGO_WEBP_VENV"]) as temporary:
            root = Path(temporary)
            path = root / row["source_path"]
            path.parent.mkdir(parents=True)
            path.symlink_to(ROOT / row["source_path"])
            with self.assertRaisesRegex(ValueError, "source-path-ownership"):
                proof.read_source(self.shared, root, row)
        changed = dict(row, slug="../unknown", source_path="tests/about-customer-row-source-inputs/../unknown.png")
        with self.assertRaisesRegex(ValueError, "closed-source-path"):
            proof.read_source(self.shared, ROOT, changed)

    def test_metadata_drift_is_rejected(self):
        row = self.manifest["assets"][0]
        actual = dict(row, frames=1, loop=None, durations_ms=[0])
        proof.require_metadata(row, actual)
        for key in proof.METADATA_KEYS:
            changed = dict(actual)
            changed[key] = "drift"
            with self.subTest(key=key), self.assertRaisesRegex(ValueError, "source-metadata-identity"):
                proof.require_metadata(row, changed)
        for key, value in (("frames", 2), ("loop", 0), ("durations_ms", [1])):
            with self.subTest(key=key), self.assertRaisesRegex(ValueError, "source-static-identity"):
                proof.require_metadata(row, dict(actual, **{key: value}))

    def test_unchanged_memory_floor_and_ancestor_checks_are_used(self):
        floor = 3_145_728
        self.assertEqual(floor, self.shared.ADMISSION_FLOOR_KIB)
        self.assertFalse(self.shared.admission_chain(floor - 1, [("max", "0")])["admitted"])
        self.assertFalse(self.shared.admission_chain(floor, [(str(floor * 1024 - 1), "0"), None])["admitted"])
        self.assertTrue(self.shared.admission_chain(floor, [(str(floor * 1024), "0"), None])["admitted"])


if __name__ == "__main__":
    shared = proof.load_shared()
    ledger = shared.owned_process_ledger("customer-row-controls", 65)
    admission = proof.require_memory_admission(shared, "customer-row-controls")
    resource.setrlimit(resource.RLIMIT_AS, (805_306_368, 805_306_368))
    resource.setrlimit(resource.RLIMIT_CPU, (15, 15))
    CustomerRowControls.shared = shared
    result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(CustomerRowControls))
    shared.write_json(ROOT / "company-logo-webp-proof/customer-row-controls.json",
                      {"status": "PASS" if result.wasSuccessful() else "FAILED",
                       "tests_run": result.testsRun, "failures": len(result.failures), "errors": len(result.errors),
                       "resource_ledger": ledger, "admission": admission})
    raise SystemExit(0 if result.wasSuccessful() else 1)
