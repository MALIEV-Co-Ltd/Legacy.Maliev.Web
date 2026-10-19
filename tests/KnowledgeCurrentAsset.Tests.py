"""Current-target Knowledge custody rejection controls, offline and bounded."""
import importlib.util
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
SPEC = importlib.util.spec_from_file_location("knowledge_current", ROOT / "scripts/verify_knowledge_assets.py")
GUARD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(GUARD)


class KnowledgeCurrentAssetTests(unittest.TestCase):
    def test_actual_current_target_passes_structural_custody_only(self):
        result = GUARD.verify(ROOT)
        self.assertEqual((72, 19, 4246293), (result["files"], result["svg"], result["bytes"]))
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


if __name__ == "__main__":
    unittest.main()
