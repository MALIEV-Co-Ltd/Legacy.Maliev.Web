#!/usr/bin/env python3
"""Five guard groups for the Root-selected parser; hosted execution remains pending.

Fixtures assemble minimal PDF objects/xref tables in memory. They do not use a
browser, application endpoint, production renderer, network or customer data.
"""
from __future__ import annotations

import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "scripts" / "verify-preliminary-pdf-proof.py"
SPEC = importlib.util.spec_from_file_location("preliminary_pdf_proof", SOURCE)
assert SPEC is not None and SPEC.loader is not None
proof = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(proof)


def minimal_pdf(page_count: int) -> bytes:
    """Generate a valid unencrypted PDF with known page-tree cardinality."""
    content_id = page_count + 3
    objects = [
        b"<< /Type /Catalog /Pages 2 0 R >>",
        ("<< /Type /Pages /Count %d /Kids [%s] >>" % (
            page_count, " ".join(f"{index + 3} 0 R" for index in range(page_count)))).encode("ascii"),
    ]
    objects.extend(
        f"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> /Contents {content_id} 0 R >>".encode("ascii")
        for _ in range(page_count)
    )
    objects.append(b"<< /Length 0 >>\nstream\n\nendstream")
    data = bytearray(b"%PDF-1.7\n%\xe2\xe3\xcf\xd3\n")
    offsets = []
    for number, body in enumerate(objects, start=1):
        offsets.append(len(data))
        data.extend(f"{number} 0 obj\n".encode("ascii") + body + b"\nendobj\n")
    xref = len(data)
    data.extend(f"xref\n0 {len(objects) + 1}\n0000000000 65535 f \n".encode("ascii"))
    for offset in offsets:
        data.extend(f"{offset:010} 00000 n \n".encode("ascii"))
    data.extend(f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n".encode("ascii"))
    return bytes(data)


def manifest(count: int, data: bytes) -> dict[str, object]:
    return {
        "schema": "preliminary-popup-pdf-v1", "partCount": count,
        "minimumPages": 1 if count == 2 else 2, "pdfFile": f"parts-{count:02}.pdf",
        "byteLength": len(data), "sha256": hashlib.sha256(data).hexdigest(),
        "rawPageCount": len(proof.PAGE_MARKER.findall(data)), "format": "A4",
        "printBackground": True,
        "fixtureBoundary": "owned-program-kestrel-in-memory-upload-native-analysis-production-popup",
    }


def write_case(root: Path, count: int, data: bytes) -> None:
    (root / f"parts-{count:02}.pdf").write_bytes(data)
    (root / f"parts-{count:02}.json").write_text(json.dumps(manifest(count, data)), encoding="utf-8")


def write_valid_packet(root: Path) -> None:
    write_case(root, 2, minimal_pdf(1))
    write_case(root, 10, minimal_pdf(2))


class PreliminaryPdfProofGuardTests(unittest.TestCase):
    def test_valid_one_and_two_page_packets_report_exact_bytes_and_structure(self) -> None:
        # Resource-policy controls use an offline resource API double; never cap
        # the guard runner or start a worker during these boundary controls.
        class ResourceBoundary:
            RLIMIT_AS, RLIMIT_CPU, RLIM_INFINITY = 1, 2, -1

            def __init__(self):
                self.limits = {1: (-1, -1), 2: (-1, -1)}

            def getrlimit(self, kind):
                return self.limits[kind]

            def setrlimit(self, kind, limits):
                self.limits[kind] = limits

        resource = ResourceBoundary()
        self.assertEqual({"addressSpaceBytes": 256 * 1024 * 1024, "cpuSeconds": 30},
                         proof.apply_worker_limits(resource, platform="linux"))
        self.assertEqual((256 * 1024 * 1024,) * 2, resource.limits[1])
        self.assertEqual((30, 30), resource.limits[2])
        with self.assertRaisesRegex(proof.ProofError, "worker-resource-caps-unavailable"):
            proof.apply_worker_limits(resource, platform="win32")

        class RefusedResource(ResourceBoundary):
            def setrlimit(self, kind, limits):
                raise OSError("synthetic denied resource API")

        class UnappliedResource(ResourceBoundary):
            def setrlimit(self, kind, limits):
                pass

        for denied in (RefusedResource(), UnappliedResource()):
            with self.assertRaisesRegex(proof.ProofError, "worker-resource-caps-unavailable"):
                proof.apply_worker_limits(denied, platform="linux")

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            write_valid_packet(root)
            result = proof.verify_packet(root)
            self.assertEqual("passed", result["status"])
            self.assertEqual([2, 10], [row["partCount"] for row in result["cases"]])
            self.assertEqual([1, 2], [row["structuralPageCount"] for row in result["cases"]])
            for row in result["cases"]:
                data = (root / row["pdfFile"]).read_bytes()
                self.assertEqual(hashlib.sha256(data).hexdigest(), row["sha256"])
                self.assertEqual(len(data), row["byteLength"])
            self.assertNotIn("pdfBytes", result)

    def test_one_page_ten_part_export_cannot_pass_with_extra_raw_marker(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            write_valid_packet(root)
            # Valid PDF with one real page plus a comment raw marker: a regex-only
            # gate sees2; independent page-tree count must still reject it.
            data = minimal_pdf(1) + b"% /Type /Page \n"
            write_case(root, 10, data)
            self.assertEqual(2, manifest(10, data)["rawPageCount"])
            with self.assertRaisesRegex(proof.ProofError, "structural-page-floor-failed"):
                proof.verify_packet(root)

    def test_crafted_markers_and_truncated_document_are_rejected(self) -> None:
        broken = [
            b"%PDF-1.7\n/Type /Page \n/Type /Page \n%%EOF\n",
            minimal_pdf(2).split(b"xref\n", 1)[0] + b"%%EOF\n",
        ]
        for data in broken:
            with self.subTest(kind="crafted" if data.startswith(b"%PDF-1.7\n/Type") else "truncated"):
                with tempfile.TemporaryDirectory() as directory:
                    root = Path(directory)
                    write_valid_packet(root)
                    write_case(root, 10, data)
                    with self.assertRaises(proof.ProofError):
                        proof.verify_packet(root)

    def test_forged_missing_duplicate_and_wrong_hash_manifests_rejected(self) -> None:
        mutations = {
            "forged-floor": lambda root, row: row.update(minimumPages=1),
            "wrong-case": lambda root, row: row.update(partCount=2),
            "wrong-options": lambda root, row: row.update(printBackground=False),
            "wrong-schema": lambda root, row: row.update(schema="anything"),
            "wrong-hash": lambda root, row: row.update(sha256="0" * 64),
            "wrong-length": lambda root, row: row.update(byteLength=row["byteLength"] + 1),
            "wrong-raw-count": lambda root, row: row.update(rawPageCount=3),
        }
        for kind, mutate in mutations.items():
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                write_valid_packet(root)
                path = root / "parts-10.json"
                row = json.loads(path.read_text(encoding="utf-8"))
                mutate(root, row)
                path.write_text(json.dumps(row), encoding="utf-8")
                with self.assertRaises(proof.ProofError):
                    proof.load_packet(root)
        for kind in ("missing", "duplicate-file", "duplicate-json-key"):
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                write_valid_packet(root)
                if kind == "missing":
                    (root / "parts-10.json").unlink()
                elif kind == "duplicate-file":
                    (root / "parts-10-copy.json").write_bytes((root / "parts-10.json").read_bytes())
                else:
                    path = root / "parts-10.json"
                    text = path.read_text(encoding="utf-8")
                    path.write_text(text[:-1] + ', "partCount": 10}', encoding="utf-8")
                with self.assertRaises(proof.ProofError):
                    proof.load_packet(root)

    def test_traversal_symlink_nonregular_and_oversize_rejected_before_parser(self) -> None:
        for kind in ("traversal", "symlink", "nonregular", "oversize"):
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as directory:
                root = Path(directory) / "packet"
                root.mkdir()
                write_valid_packet(root)
                if kind == "traversal":
                    path = root / "parts-10.json"
                    row = json.loads(path.read_text(encoding="utf-8"))
                    row["pdfFile"] = "../outside.pdf"
                    path.write_text(json.dumps(row), encoding="utf-8")
                elif kind == "symlink":
                    outside = Path(directory) / "outside.pdf"
                    outside.write_bytes(minimal_pdf(2))
                    (root / "parts-10.pdf").unlink()
                    # Deliberately fail if hosted filesystem cannot make a link;
                    # never skip the filesystem rejection contract.
                    (root / "parts-10.pdf").symlink_to(outside)
                elif kind == "nonregular":
                    (root / "parts-10.pdf").unlink()
                    (root / "parts-10.pdf").mkdir()
                else:
                    with (root / "parts-10.pdf").open("wb") as stream:
                        stream.truncate(proof.MAX_PDF_BYTES + 1)
                with self.assertRaises(proof.ProofError):
                    proof.load_packet(root)


if __name__ == "__main__":
    unittest.main()
