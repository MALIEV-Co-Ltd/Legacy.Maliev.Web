"""Small synthetic encoder/rejection controls; not evidence for the eighteen real artworks."""
import importlib.util
import io
import binascii
import struct
import zlib
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("corporate_logo_proof", ROOT / "scripts/prove-corporate-logo-webp.py")
proof = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(proof)


def encoded(image, format="PNG", **options):
    output = io.BytesIO()
    image.save(output, format=format, **options)
    return output.getvalue()


def png_chunk(kind, body):
    return len(body).to_bytes(4, "big") + kind + body + (binascii.crc32(kind + body) & 0xffffffff).to_bytes(4, "big")


class CorporateLogoProofControls(unittest.TestCase):

    def test_memory_admission_preserves_three_gib_floor_and_cgroup_headroom(self):
        floor = proof.ADMISSION_FLOOR_KIB
        self.assertEqual(3_145_728, floor)
        self.assertTrue(proof.admission_values(floor, "max", "0")["admitted"])
        self.assertFalse(proof.admission_values(floor - 1, "max", "0")["admitted"])
        self.assertTrue(proof.admission_values(floor, str(floor * 1024 + 7), "7")["admitted"])
        self.assertFalse(proof.admission_values(floor, str(floor * 1024 + 6), "7")["admitted"])
        self.assertFalse(proof.admission_values(floor, "1", "2")["admitted"])
        for maximum, current in (("-1", "0"), ("max", "-1"), ("bad", "0")):
            with self.subTest(maximum=maximum, current=current), self.assertRaises(ValueError):
                proof.admission_values(floor, maximum, current)


    def test_memory_admission_checks_tight_ancestors_of_an_unlimited_leaf(self):
        floor = proof.ADMISSION_FLOOR_KIB
        tight_parent = str(floor * 1024 - 1)
        self.assertFalse(proof.admission_chain(floor, [("max", "0"), (tight_parent, "0")])["admitted"])
        self.assertTrue(proof.admission_chain(floor, [("max", "7"), (str(floor * 1024 + 9), "9")])["admitted"])
        for levels in ([], [("max", "0")] * 33, [("max", "0"), ("malformed", "0")]):
            with self.subTest(levels=len(levels)), self.assertRaises(ValueError):
                proof.admission_chain(floor, levels)


    def test_true_root_absence_exception_is_narrow_and_preserves_measured_limits(self):
        floor = proof.ADMISSION_FLOOR_KIB
        def missing(name):
            raise FileNotFoundError(name)
        self.assertIsNone(proof.read_cgroup_level(Path("/unused"), True, missing))
        with self.assertRaises(ValueError):
            proof.read_cgroup_level(Path("/unused"), False, missing)
        def partial(name):
            if name == "memory.max":
                raise FileNotFoundError(name)
            return "0"
        with self.assertRaises(ValueError):
            proof.read_cgroup_level(Path("/unused"), True, partial)
        def delegated(name):
            if name == "cgroup.events":
                return "populated 1"
            raise FileNotFoundError(name)
        with self.assertRaises(ValueError):
            proof.read_cgroup_level(Path("/unused"), True, delegated)
        def denied(name):
            raise PermissionError(name)
        with self.assertRaises(PermissionError):
            proof.read_cgroup_level(Path("/unused"), True, denied)
        self.assertTrue(proof.admission_chain(floor, [None])["admitted"])
        self.assertFalse(proof.admission_chain(floor - 1, [None])["admitted"])
        self.assertFalse(proof.admission_chain(floor, [(str(floor * 1024 - 1), "0"), None])["admitted"])
        with self.assertRaises(ValueError):
            proof.admission_chain(floor, [None, ("max", "0")])

    def test_true_root_exception_requires_verified_cgroup2_mount_root(self):
        native = "31 24 0:28 / /sys/fs/cgroup rw - cgroup2 cgroup rw"
        self.assertTrue(proof.verified_cgroup_mount(native))
        self.assertFalse(proof.verified_cgroup_mount(native.replace(" / /sys", " /delegated /sys")))
        for value in ("", native + "\n" + native, native.replace(" - cgroup2 ", " - tmpfs ")):
            with self.subTest(value=value), self.assertRaises(ValueError):
                proof.verified_cgroup_mount(value)

    def test_lossless_preserves_invisible_rgb_partial_alpha_and_dimensions(self):
        with Image.new("RGBA", (2, 2)) as image, tempfile.TemporaryDirectory() as temp:
            image.putdata([(17, 31, 53, 0), (71, 97, 113, 128), (13, 19, 29, 255), (255, 255, 255, 255)])
            source = encoded(image)
            target = Path(temp) / "proof.webp"
            metadata, derived = proof.convert_static(source, target)
            with Image.open(target) as result, result.convert("RGBA") as rgba:
                self.assertEqual(image.tobytes(), rgba.tobytes())
            self.assertEqual((2, 2), (metadata["width"], metadata["height"]))
            self.assertEqual(proof.git_blob(target.read_bytes()), derived["git_blob"])

    def test_jpeg_icc_and_orientation_are_preserved_without_autorotation(self):
        with Image.new("RGB", (3, 2), "#335577") as image, tempfile.TemporaryDirectory() as temp:
            exif = Image.Exif()
            exif[274] = 6
            source = encoded(image, "JPEG", exif=exif, icc_profile=b"exact-source-icc-control")
            metadata, _ = proof.convert_static(source, Path(temp) / "proof.webp")
            self.assertEqual(6, metadata["orientation"])
            self.assertEqual((3, 2), (metadata["width"], metadata["height"]))

    def test_static_gif_without_animation_metadata_is_supported(self):
        with Image.new("RGB", (2, 2), "#123456") as image, tempfile.TemporaryDirectory() as temp:
            metadata, _ = proof.convert_static(encoded(image, "GIF"), Path(temp) / "proof.webp")
            self.assertEqual(1, metadata["frames"])
            self.assertIsNone(metadata["loop"])

    def test_animated_gif_is_rejected_instead_of_flattened(self):
        with Image.new("RGB", (2, 2), "red") as first, Image.new("RGB", (2, 2), "blue") as second:
            data = encoded(first, "GIF", save_all=True, append_images=[second], duration=[80, 120], loop=0)
            with self.assertRaisesRegex(ValueError, "unsupported-animation"):
                proof.inspect_static(data)

    def test_single_frame_timing_or_loop_metadata_is_not_silently_discarded(self):
        with Image.new("RGB", (2, 2), "red") as image:
            data = encoded(image, "GIF", duration=80, loop=0)
            with self.assertRaisesRegex(ValueError, "unsupported-animation-metadata"):
                proof.inspect_static(data)

    def test_cmyk_source_is_rejected_instead_of_relabeling_its_color_profile(self):
        with Image.new("CMYK", (2, 2), (0, 255, 255, 0)) as image:
            with self.assertRaisesRegex(ValueError, "unsupported-source-mode"):
                proof.inspect_static(encoded(image, "JPEG"))

    def test_grayscale_icc_is_not_relabelled_as_an_rgb_profile(self):
        with Image.new("L", (2, 2), 128) as image:
            with self.assertRaisesRegex(ValueError, "unsupported-source-profile-mode"):
                proof.inspect_static(encoded(image, icc_profile=b"grayscale-profile"))

    def test_png_color_interpretation_metadata_is_rejected_before_conversion(self):
        chunks = {
            b"gAMA": (45455).to_bytes(4, "big"),
            b"cHRM": b"".join(value.to_bytes(4, "big") for value in (31270, 32900, 64000, 33000, 30000, 60000, 15000, 6000)),
            b"sRGB": bytes([0]),
            b"cICP": bytes([1, 13, 0, 1]),
            b"mDCV": bytes(24),
            b"cLLI": bytes(8),
        }
        with Image.new("RGB", (2, 2), "red") as image:
            baseline = encoded(image)
            for kind, body in chunks.items():
                with self.subTest(chunk=kind):
                    data = baseline[:33] + png_chunk(kind, body) + baseline[33:]
                    with self.assertRaisesRegex(ValueError, "unsupported-png-color-metadata"):
                        proof.inspect_static(data)

    def test_16_bit_rgb_and_rgba_png_cannot_be_certified_after_pillow_reduction(self):
        for color_type, channels, mode in ((2, 3, "RGB"), (6, 4, "RGBA")):
            with self.subTest(mode=mode):
                header = struct.pack(">IIBBBBB", 1, 1, 16, color_type, 0, 0, 0)
                scanline = b"\0" + b"\x12\x34" * channels
                data = (b"\x89PNG\r\n\x1a\n" + png_chunk(b"IHDR", header)
                        + png_chunk(b"IDAT", zlib.compress(scanline)) + png_chunk(b"IEND", b""))
                with Image.open(io.BytesIO(data)) as image:
                    self.assertEqual(mode, image.mode)
                with self.assertRaisesRegex(ValueError, "unsupported-png-bit-depth"):
                    proof.inspect_static(data)

    def test_changed_source_git_binding_and_path_traversal_are_rejected(self):
        with tempfile.TemporaryDirectory() as temp, Image.new("RGB", (2, 2), "blue") as image:
            root = Path(temp).resolve()
            data = encoded(image)
            relative = proof.SOURCE_PREFIX + "control.png"
            path = root / relative
            path.parent.mkdir(parents=True)
            path.write_bytes(data)
            asset = {"source_path": relative, "source_git_blob": proof.git_blob(data),
                     "source_bytes": len(data), "derived_path": relative[:-4] + ".webp"}
            self.assertEqual(data, proof.read_source(root, asset))
            with self.assertRaisesRegex(ValueError, "source-git-identity"):
                proof.read_source(root, dict(asset, source_git_blob="0" * 40))
            with self.assertRaisesRegex(ValueError, "source-path-shape"):
                proof.read_source(root, dict(asset, source_path=proof.SOURCE_PREFIX + "../control.png",
                                            derived_path=proof.SOURCE_PREFIX + "../control.webp"))

    def test_decoded_pixel_bound_is_enforced_before_conversion(self):
        with Image.new("RGB", (2, 2), "blue") as image, mock.patch.object(proof, "MAX_PIXELS", 3):
            with self.assertRaisesRegex(ValueError, "decoded-size-bound"):
                proof.inspect_static(encoded(image))

    def test_wrong_pixels_from_an_encoder_are_rejected(self):
        with Image.new("RGBA", (2, 2), "red") as source_image, Image.new("RGBA", (2, 2), "blue") as wrong:
            source = encoded(source_image)
            wrong_bytes = encoded(wrong, "WEBP", lossless=True, exact=True)
            with tempfile.TemporaryDirectory() as temp:
                target = Path(temp) / "proof.webp"
                with mock.patch.object(Image.Image, "save", lambda self, path, **kwargs: Path(path).write_bytes(wrong_bytes)):
                    with self.assertRaisesRegex(ValueError, "derived-pixel-equivalence"):
                        proof.convert_static(source, target)

    def test_missing_icc_from_an_encoder_is_rejected(self):
        with Image.new("RGBA", (2, 2), "red") as image:
            source = encoded(image, icc_profile=b"source-icc-must-remain")
            missing_icc = encoded(image, "WEBP", lossless=True, exact=True)
            with tempfile.TemporaryDirectory() as temp:
                target = Path(temp) / "proof.webp"
                with mock.patch.object(Image.Image, "save", lambda self, path, **kwargs: Path(path).write_bytes(missing_icc)):
                    with self.assertRaisesRegex(ValueError, "derived-icc"):
                        proof.convert_static(source, target)


    def test_post_idat_png_xmp_is_loaded_and_retained(self):
        xmp = b'<x:xmpmeta xmlns:x="adobe:ns:meta/">exact trailing XMP control</x:xmpmeta>'
        with Image.new("RGBA", (2, 2), "#123456") as image, tempfile.TemporaryDirectory() as temp:
            baseline = encoded(image)
            source = baseline[:-12] + png_chunk(
                b"iTXt", b"XML:com.adobe.xmp" + bytes(5) + xmp) + baseline[-12:]
            with Image.open(io.BytesIO(source)) as lazy:
                self.assertNotIn("xmp", lazy.info)
                lazy.load()
                self.assertEqual(xmp, lazy.info["xmp"])
            _, rgba, metadata = proof.inspect_static(source)
            rgba.close()
            self.assertEqual(xmp, metadata["xmp"])
            target = Path(temp) / "proof.webp"
            proof.convert_static(source, target)
            with Image.open(target) as result:
                result.load()
                self.assertEqual(xmp, result.info["xmp"])

    def test_dropped_post_idat_png_xmp_from_encoder_is_rejected(self):
        xmp = b'<x:xmpmeta xmlns:x="adobe:ns:meta/">must remain</x:xmpmeta>'
        with Image.new("RGBA", (2, 2), "red") as image, tempfile.TemporaryDirectory() as temp:
            baseline = encoded(image)
            source = baseline[:-12] + png_chunk(
                b"iTXt", b"XML:com.adobe.xmp" + bytes(5) + xmp) + baseline[-12:]
            missing_xmp = encoded(image, "WEBP", lossless=True, exact=True)
            target = Path(temp) / "proof.webp"
            with mock.patch.object(Image.Image, "save", lambda self, path, **kwargs: Path(path).write_bytes(missing_xmp)):
                with self.assertRaisesRegex(ValueError, "derived-xmp"):
                    proof.convert_static(source, target)

    def test_animation_or_lossy_chunks_cannot_masquerade_as_static_lossless_output(self):
        with Image.new("RGBA", (2, 2), "red") as image:
            valid = encoded(image, "WEBP", lossless=True, exact=True)
            proof.require_static_lossless_webp(valid)
            with self.assertRaises(ValueError):
                proof.require_static_lossless_webp(valid.replace(b"VP8L", b"VP8 ", 1))
            appended = valid[8:] + b"ANIM" + (6).to_bytes(4, "little") + bytes(6)
            animated = b"RIFF" + len(appended).to_bytes(4, "little") + appended
            with self.assertRaises(ValueError):
                proof.require_static_lossless_webp(animated)


if __name__ == "__main__":
    if os.environ.get("LOGO_WEBP_VENV"):
        proof.owned_process_ledger("controls", 95)
        proof.resource.setrlimit(proof.resource.RLIMIT_AS, (805_306_368, 805_306_368))
        proof.resource.setrlimit(proof.resource.RLIMIT_CPU, (60, 60))
        proof.require_memory_admission("controls")
    unittest.main()
