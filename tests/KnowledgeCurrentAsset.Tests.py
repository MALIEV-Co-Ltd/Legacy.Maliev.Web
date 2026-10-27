"""Current-target Knowledge custody rejection controls, offline and bounded."""
import importlib.util
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
SPEC = importlib.util.spec_from_file_location("knowledge_current", ROOT / "scripts/verify_knowledge_assets.py")
GUARD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(GUARD)


class KnowledgeCurrentAssetTests(unittest.TestCase):
    def test_actual_current_target_passes_structural_custody_only(self):
        result = GUARD.verify(ROOT)
        self.assertEqual((72, 19, 4245608), (result["files"], result["svg"], result["bytes"]))
        for key in ("decodedDimensionsVerified", "publicHttpVerified", "releaseAccepted"):
            self.assertFalse(result[key])

    def reject_changed(self, path, transform, category):
        policy = GUARD.intake.load_policy(ROOT)
        actual_read = policy["read_asset"]

        def read(root, relative):
            raw = actual_read(root, relative)
            return transform(raw) if relative == path else raw

        with self.assertRaisesRegex(GUARD.intake.IntakeError, "^" + category + "$"):
            GUARD.verify(ROOT, reader=read, policy=policy)

    def test_same_length_photo_tamper_is_rejected(self):
        self.reject_changed("Legacy.Maliev.Web/wwwroot/src/images/knowledge/photos/chatter.webp",
                            lambda raw: bytes([raw[0] ^ 1]) + raw[1:], "source-blob")

    def test_inherited_diagram_tamper_is_rejected(self):
        self.reject_changed("Legacy.Maliev.Web/wwwroot/src/images/knowledge/cnc-wall-stiffness.svg",
                            lambda raw: raw + b" ", "source-blob")

    def test_manifest_and_seed_tamper_are_rejected(self):
        for path, category in ((GUARD.MANIFEST, "manifest-custody"), (GUARD.SEED, "seed-custody")):
            with self.subTest(path=path):
                self.reject_changed(path, lambda raw: raw + b" ", category)

    def test_actual_svg_policy_visits_all_nineteen_diagrams(self):
        policy = GUARD.intake.load_policy(ROOT)
        actual_svg = policy["verify_svg"]
        visited = []

        def svg(raw):
            actual_svg(raw)
            visited.append(GUARD.intake.git_hash("blob", raw))

        policy["verify_svg"] = svg
        GUARD.verify(ROOT, policy=policy)
        self.assertEqual(19, len(visited))
        self.assertEqual(19, len(set(visited)))


    def test_all_five_source930_assets_reconstruct_the_frozen_inventory(self):
        policy = GUARD.intake.load_policy(ROOT)
        seed = policy["parse_manifest"](policy["read_asset"](ROOT, GUARD.SEED))
        manifest = policy["parse_manifest"](policy["read_asset"](ROOT, GUARD.MANIFEST))
        rows, _ = GUARD.intake.validate_seed(seed)
        visited = set()
        for frozen, measured in zip(rows, manifest["rows"]):
            path = frozen["targetPath"]
            if path not in GUARD.SOURCE930:
                continue
            with self.subTest(path=path):
                current = policy["read_asset"](ROOT, path)
                historical = GUARD.historical_bytes(current, frozen)
                GUARD.intake.verify_blob(historical, frozen)
                self.assertEqual(measured["sha256"], GUARD.hashlib.sha256(historical).hexdigest())
                old, blob, size, digest = GUARD.SOURCE930[path]
                self.assertEqual(old, frozen["blob"])
                self.assertEqual((blob, size, digest),
                                 (GUARD.intake.git_hash("blob", current), len(current),
                                  GUARD.hashlib.sha256(current).hexdigest()))
                self.assertNotIn(GUARD.DRAWING_FOOTER, current)
                self.assertEqual(1, historical.count(GUARD.DRAWING_FOOTER))
                visited.add(path)
        self.assertEqual(set(GUARD.SOURCE930), visited)
        self.assertEqual(5, len(visited))

    def test_source930_geometry_tamper_is_rejected(self):
        for path in GUARD.SOURCE930:
            with self.subTest(path=path):
                self.reject_changed(path, lambda raw: raw.replace(b'960 480', b'961 480', 1),
                                    "source-blob")

    def test_source930_missing_insertion_boundary_is_rejected(self):
        path = next(iter(GUARD.SOURCE930))
        self.reject_changed(path, lambda raw: raw.replace(b"\n</g>", b"\n </g>"),
                            "source930-boundary")

    def test_source930_reconstructed_history_tamper_is_rejected(self):
        with patch.object(GUARD, "DRAWING_FOOTER", GUARD.DRAWING_FOOTER + b" "):
            with self.assertRaisesRegex(GUARD.intake.IntakeError, "^source-blob$"):
                GUARD.verify(ROOT)

    def test_source930_unknown_or_missing_mapping_is_rejected(self):
        for changed in (dict(GUARD.SOURCE930, unknown=next(iter(GUARD.SOURCE930.values()))),
                        dict(list(GUARD.SOURCE930.items())[1:])):
            with self.subTest(keys=tuple(changed)):
                with patch.object(GUARD, "SOURCE930", changed):
                    with self.assertRaisesRegex(GUARD.intake.IntakeError, "^source930-mapping$"):
                        GUARD.verify(ROOT)

    def test_source930_mismatched_historical_binding_is_rejected(self):
        changed = dict(GUARD.SOURCE930)
        path = next(iter(changed))
        old, blob, size, digest = changed[path]
        changed[path] = ("0" * 40, blob, size, digest)
        with patch.object(GUARD, "SOURCE930", changed):
            with self.assertRaisesRegex(GUARD.intake.IntakeError, "^source930-history$"):
                GUARD.verify(ROOT)

    def test_source930_each_current_digest_and_size_is_required(self):
        path = next(iter(GUARD.SOURCE930))
        old, blob, size, digest = GUARD.SOURCE930[path]
        for replacement in ((old, "0" * 40, size, digest),
                            (old, blob, size + 1, digest),
                            (old, blob, size, "0" * 64)):
            with self.subTest(binding=replacement):
                changed = dict(GUARD.SOURCE930)
                changed[path] = replacement
                with patch.object(GUARD, "SOURCE930", changed):
                    with self.assertRaisesRegex(GUARD.intake.IntakeError, "^source930-current$"):
                        GUARD.verify(ROOT)


if __name__ == "__main__":
    unittest.main()
