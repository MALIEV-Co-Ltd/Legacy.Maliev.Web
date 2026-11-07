"""Offline negative controls; no browser, network, Git process, or application startup."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("knowledge_intake", ROOT / "scripts/stage_knowledge_assets.py")
GUARD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(GUARD)


class KnowledgeAssetIntakeTests(unittest.TestCase):
    def seed(self):
        return json.loads((ROOT / "docs/migration/knowledge-source-intake.v1.json").read_text(encoding="utf-8"))

    def test_frozen_seed_has_exact_59_assets_11_documents_and_two_inherited(self):
        raw = (ROOT / "docs/migration/knowledge-source-intake.v1.json").read_bytes()
        self.assertEqual("5fd485eaecf9ef343e588ce2b737088d8bddd778", GUARD.git_hash("blob", raw))
        rows, inherited = GUARD.validate_seed(self.seed())
        self.assertEqual((70, 2), (len(rows), len(inherited)))

    def test_dropped_row_is_refused(self):
        seed = self.seed()
        seed["rows"].pop()
        with self.assertRaisesRegex(GUARD.IntakeError, "^seed-count$"):
            GUARD.validate_seed(seed)

    def test_changed_pair_is_refused_without_count_change(self):
        seed = self.seed()
        row = next(row for row in seed["rows"] if row["sourcePath"].endswith("-small.webp"))
        row["sourcePath"] = row["sourcePath"].replace("-small.webp", "-orphan-small.webp")
        with self.assertRaisesRegex(GUARD.IntakeError, "^photo-pairs$"):
            GUARD.validate_seed(seed)

    def test_case_colliding_target_is_refused(self):
        seed = self.seed()
        seed["rows"][1]["targetPath"] = seed["rows"][0]["targetPath"].upper()
        with self.assertRaisesRegex(GUARD.IntakeError, "^seed-paths$"):
            GUARD.validate_seed(seed)

    def test_traversal_absolute_and_backslash_are_refused(self):
        for value in ("../image.svg", "/image.svg", "a//b", "a/./b", "a\\b", "C:/image.svg"):
            with self.subTest(value=value), self.assertRaises(GUARD.IntakeError):
                GUARD.safe_relative(value)

    def test_same_size_tamper_and_wrong_size_are_refused(self):
        row = {"bytes": 3, "blob": "f2ba8f84ab5c1bce84a7b441cb1959cfc7093b7f"}
        GUARD.verify_blob(b"abc", row)
        for raw in (b"abd", b"ab"):
            with self.subTest(raw=raw), self.assertRaisesRegex(GUARD.IntakeError, "^source-blob$"):
                GUARD.verify_blob(raw, row)

    def test_binary_tree_preserves_exact_blob_and_directory_modes(self):
        sha = bytes.fromhex("2234ca7b5497fd584ea73bcea836ba2b73ab1b45")
        self.assertEqual({"image.svg": (b"100644", sha.hex())},
                         GUARD.parse_tree(b"100644 image.svg\0" + sha))

    def test_malformed_duplicate_or_traversal_tree_names_are_refused(self):
        sha = b"\x01" * 20
        row = b"100644 image.svg\0" + sha
        for raw in (row[:-1], row + row, b"100644 ../image.svg\0" + sha):
            with self.subTest(raw=raw), self.assertRaises(GUARD.IntakeError):
                GUARD.parse_tree(raw)

    def test_tree_path_rejects_changed_blob_missing_entry_and_symlink_mode(self):
        expected = "2234ca7b5497fd584ea73bcea836ba2b73ab1b45"
        for table in (
            {"image.svg": (b"100644", "f22982c9b91ecc5c711528ebe51529dc7e5440e2")},
            {},
            {"image.svg": (b"120000", expected)},
        ):
            objects = GUARD.SourceObjects(Path("/unused"), lambda *args: b"")
            objects.trees[GUARD.SOURCE_TREE] = table
            with self.subTest(table=table), self.assertRaises(GUARD.IntakeError):
                objects.bind_path("image.svg", expected)

    def test_exclusive_output_never_overwrites_existing_bytes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            GUARD.write_new(root, "nested/image", b"original")
            with self.assertRaises(FileExistsError):
                GUARD.write_new(root, "nested/image", b"changed")
            self.assertEqual(b"original", (root / "nested/image").read_bytes())

    def test_export_commit_claim_is_not_trusted_without_actual_object_hash(self):
        objects = GUARD.SourceObjects(Path("/unused"), lambda *args: b"tree " + GUARD.SOURCE_TREE.encode() + b"\n")
        with self.assertRaisesRegex(GUARD.IntakeError, "^object-custody$"):
            objects.bind_commit()

    def test_output_cannot_reuse_checkout_or_existing_directory(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            source, target = root / "source", root / "target"
            source.mkdir()
            target.mkdir()
            for output in (target, target / "stage", source / "stage", root):
                with self.subTest(output=output), self.assertRaises(GUARD.IntakeError):
                    GUARD.distinct_roots(source, target, output)
            self.assertEqual(root / "stage", GUARD.distinct_roots(source, target, root / "stage")[2])

    def test_bounded_reader_refuses_oversized_and_linked_file(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            (root / "file").write_bytes(b"abc")
            self.assertEqual(b"abc", GUARD.bounded_read(root, "file", 3))
            with self.assertRaisesRegex(GUARD.IntakeError, "^read-size$"):
                GUARD.bounded_read(root, "file", 2)
            try:
                (root / "link").symlink_to(root / "file")
            except (OSError, NotImplementedError):
                return
            with self.assertRaisesRegex(GUARD.IntakeError, "^read-link$"):
                GUARD.bounded_read(root, "link", 3)

    def test_existing_structural_svg_policy_is_used_not_grep(self):
        policy = GUARD.load_policy(ROOT)
        policy["verify_svg"](b'<svg xmlns="http://www.w3.org/2000/svg"><path d="M0 0"/></svg>')
        for raw in (
            b'<svg xmlns="http://www.w3.org/2000/svg"><script/></svg>',
            b'<svg xmlns="http://www.w3.org/2000/svg"><g onload="x"/></svg>',
            b'<svg xmlns="http://www.w3.org/2000/svg"><image href="https://example.invalid/a"/></svg>',
            b'<!DOCTYPE svg [<!ENTITY a "x">]><svg xmlns="http://www.w3.org/2000/svg"/>',
        ):
            with self.subTest(raw=raw), self.assertRaises(policy["AssetError"]):
                policy["verify_svg"](raw)


if __name__ == "__main__":
    unittest.main()
