"""Synthetic custody regressions; no network, browser, or production authority."""
import copy
import hashlib
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("company_assets", ROOT / "scripts/verify_company_release_assets.py")
guard = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(guard)
SVG = b'<svg xmlns="http://www.w3.org/2000/svg"><path d="M0 0"/></svg>'


class AssetCustodyTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        self.entry = {
            "sourceCommit": "f981dce7682606b1c13f04b271163447f3f2709e",
            "sourcePath": "Maliev.Web/wwwroot/src/images/customers/synthetic.svg",
            "sourceBlob": hashlib.sha1(b"blob " + str(len(SVG)).encode() + b"\0" + SVG).hexdigest(),
            "targetPath": "Legacy.Maliev.Web/wwwroot/src/images/customers/synthetic.svg",
            "bytes": len(SVG),
            "sha256": hashlib.sha256(SVG).hexdigest(),
        }
        self.path = self.root / self.entry["targetPath"]
        self.path.parent.mkdir(parents=True)
        self.path.write_bytes(SVG)

    def verify(self, entries=None):
        return guard.verify_assets(self.root, entries if entries is not None else [self.entry])

    def replace_svg(self, data):
        self.path.write_bytes(data)
        self.entry.update(bytes=len(data), sha256=hashlib.sha256(data).hexdigest(),
                          sourceBlob=hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest())

    def test_valid_svg_measures_raw_git_blob_and_sha256(self):
        result = self.verify()
        self.assertEqual({"count": 1, "bytes": len(SVG), "svgCount": 1}, result)

    def test_missing_asset_is_not_a_successful_custody_check(self):
        self.path.unlink()
        with self.assertRaisesRegex(guard.AssetError, "^asset-read$"):
            self.verify()

    def test_same_size_byte_tampering_rejects_sha256(self):
        self.path.write_bytes(SVG.replace(b"M0", b"M1"))
        with self.assertRaisesRegex(guard.AssetError, "^asset-sha256$"):
            self.verify()

    def test_declared_size_must_match_measured_bytes(self):
        self.entry["bytes"] += 1
        with self.assertRaisesRegex(guard.AssetError, "^asset-size$"):
            self.verify()

    def test_git_blob_is_independently_required_even_when_sha256_matches(self):
        self.entry["sourceBlob"] = "0" * 40
        with self.assertRaisesRegex(guard.AssetError, "^asset-git-blob$"):
            self.verify()

    def test_path_escape_absolute_backslash_and_wrong_destination_rejected(self):
        for target in ("../outside.svg", "/tmp/outside.svg", "C:/outside.svg",
                       "Legacy.Maliev.Web\\wwwroot\\src\\images\\customers\\synthetic.svg",
                       "Legacy.Maliev.Web/wwwroot/src/images/customers/../outside.svg",
                       "Legacy.Maliev.Web/wwwroot/src/images/customers/other.svg"):
            row = {**self.entry, "targetPath": target}
            with self.subTest(target=target), self.assertRaisesRegex(guard.AssetError, "^asset-path$"):
                self.verify([row])

    def test_duplicate_destination_is_rejected_before_reading(self):
        with self.assertRaisesRegex(guard.AssetError, "^asset-duplicate$"):
            self.verify([self.entry, copy.deepcopy(self.entry)])

    def test_unknown_source_commit_and_unbounded_size_are_rejected(self):
        for patch in ({"sourceCommit": "0" * 40}, {"sourceCommit": []}, {"bytes": True}, {"bytes": 0},
                      {"bytes": 8 * 1024 * 1024 + 1}, {"unexpected": "ignored"}):
            with self.subTest(patch=patch), self.assertRaisesRegex(guard.AssetError, "^asset-entry$"):
                self.verify([{**self.entry, **patch}])

    def test_dtd_entities_and_non_utf8_xml_are_rejected_before_parse(self):
        for data in (b'<!DOCTYPE svg [<!ENTITY x "x">]>' + SVG,
                     b'<!ENTITY x "x">' + SVG, SVG.decode().encode("utf-16"), b"\xff" + SVG):
            self.replace_svg(data)
            with self.subTest(data=data[:20]), self.assertRaises(guard.AssetError):
                self.verify()

    def test_script_event_foreign_object_and_external_links_are_rejected(self):
        bodies = ('<script>bad()</script>', '<g onload="bad()"/>',
                  '<foreignObject/>', '<image href="https://example.test/a.png"/>',
                  '<g xml:base="https://example.test/"><use href="#x"/></g>',
                  '<iframe xmlns="http://www.w3.org/1999/xhtml" srcdoc="unsafe"/>',
                  '<image href="javascript:bad()"/>',
                  '<style>@import "https://example.test/a.css";</style>',
                  '<path style="fill:url(https://example.test/a.svg#x)"/>',
                  '<style>.a{fill:u\\72l(https://example.test/a)}</style>')
        for body in bodies:
            self.replace_svg(('<svg xmlns="http://www.w3.org/2000/svg">' + body + '</svg>').encode())
            with self.subTest(body=body), self.assertRaisesRegex(guard.AssetError, "^svg-unsafe$"):
                self.verify()

    def test_xml_declaration_does_not_allow_stylesheet_or_model_pis(self):
        declaration = b'<?xml version="1.0" encoding="utf-8"?>'
        self.replace_svg(declaration + SVG)
        self.assertEqual(1, self.verify()["svgCount"])
        for prefix in (b"", declaration):
            for target in (b"xml-stylesheet", b"xml-model"):
                data = prefix + b'<?' + target + b' href="https://example.test/external"?>' + SVG
                self.replace_svg(data)
                with self.subTest(prefix=prefix, target=target), self.assertRaisesRegex(guard.AssetError, "^svg-unsafe$"):
                    self.verify()
        for data in (b'<?XML version="1.0"?>' + SVG, declaration + declaration + SVG):
            self.replace_svg(data)
            with self.subTest(data=data[:80]), self.assertRaisesRegex(guard.AssetError, "^svg-unsafe$"):
                self.verify()

    def test_local_fragment_paint_and_links_remain_supported(self):
        self.replace_svg(b'<svg xmlns="http://www.w3.org/2000/svg"><defs><path id="x"/></defs><use href="#x" style="fill:url(#x)"/></svg>')
        self.assertEqual(1, self.verify()["svgCount"])

    def test_malformed_and_wrong_root_xml_rejected(self):
        for data in (b"<svg>", b"<html/>", b'<svg xmlns="https://example.test"/>'):
            self.replace_svg(data)
            with self.subTest(data=data), self.assertRaises(guard.AssetError):
                self.verify()

    def test_nested_link_is_rejected_without_following_it(self):
        outside = self.root / "outside"
        outside.mkdir()
        linked = self.path.parent / "linked.svg"
        linked.symlink_to(self.path)
        row = {**self.entry, "sourcePath": self.entry["sourcePath"].replace("synthetic", "linked"),
               "targetPath": self.entry["targetPath"].replace("synthetic", "linked")}
        with self.assertRaisesRegex(guard.AssetError, "^asset-link$"):
            self.verify([row])

    def test_manifest_duplicate_keys_and_changed_frozen_bytes_rejected(self):
        with self.assertRaisesRegex(guard.AssetError, "^manifest-duplicate-key$"):
            guard.parse_manifest(b'{"schema":1,"schema":2}')
        with self.assertRaisesRegex(guard.AssetError, "^manifest-hash$"):
            guard.verify_release(self.root, b"{}")

    def test_manifest_requires_exact_fifty_nine_distinct_source_mappings(self):
        with self.assertRaisesRegex(guard.AssetError, "^manifest-shape$"):
            guard.validate_manifest({"schema": "maliev-company-release-assets.v1", "assets": []})


if __name__ == "__main__":
    unittest.main()
