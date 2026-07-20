"""Generate deterministic, first-party CAD fixtures for issue #153 validation."""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import re
import shutil
import struct
import zipfile
from pathlib import Path

import cadquery as cq
from OCP.IGESControl import IGESControl_Writer

VERTICES = (
    (0.0, 0.0, 0.0),
    (10.0, 0.0, 0.0),
    (10.0, 10.0, 0.0),
    (0.0, 10.0, 0.0),
    (0.0, 0.0, 10.0),
    (10.0, 0.0, 10.0),
    (10.0, 10.0, 10.0),
    (0.0, 10.0, 10.0),
)
TRIANGLES = (
    (0, 2, 1),
    (0, 3, 2),
    (4, 5, 6),
    (4, 6, 7),
    (0, 1, 5),
    (0, 5, 4),
    (1, 2, 6),
    (1, 6, 5),
    (2, 3, 7),
    (2, 7, 6),
    (3, 0, 4),
    (3, 4, 7),
)
MIME_TYPES = {
    ".stl": "model/stl",
    ".obj": "model/obj",
    ".3mf": "application/vnd.ms-package.3dmanufacturing-3dmodel+xml",
    ".step": "model/step",
    ".stp": "model/step",
    ".iges": "model/iges",
    ".igs": "model/iges",
    ".glb": "model/gltf-binary",
    ".gltf": "model/gltf+json",
}


def write_ascii_stl(path: Path) -> None:
    lines = ["solid maliev_cube_10mm"]
    for a, b, c in TRIANGLES:
        vertex_a = " ".join(format(value, ".1f") for value in VERTICES[a])
        vertex_b = " ".join(format(value, ".1f") for value in VERTICES[b])
        vertex_c = " ".join(format(value, ".1f") for value in VERTICES[c])
        lines.extend(
            (
                "  facet normal 0 0 0",
                "    outer loop",
                f"      vertex {vertex_a}",
                f"      vertex {vertex_b}",
                f"      vertex {vertex_c}",
                "    endloop",
                "  endfacet",
            )
        )
    lines.append("endsolid maliev_cube_10mm")
    path.write_text("\n".join(lines) + "\n", encoding="ascii", newline="\n")


def write_obj(path: Path) -> None:
    lines = ["# MALIEV-authored deterministic 10 mm cube"]
    lines.extend(f"v {x:.1f} {y:.1f} {z:.1f}" for x, y, z in VERTICES)
    lines.extend(f"f {a + 1} {b + 1} {c + 1}" for a, b, c in TRIANGLES)
    path.write_text("\n".join(lines) + "\n", encoding="ascii", newline="\n")


def mesh_buffer() -> bytes:
    positions = b"".join(struct.pack("<3f", *vertex) for vertex in VERTICES)
    indices = b"".join(
        struct.pack("<H", index) for triangle in TRIANGLES for index in triangle
    )
    return positions + indices


def gltf_document(buffer_uri: str | None, byte_length: int) -> dict[str, object]:
    buffer: dict[str, object] = {"byteLength": byte_length}
    if buffer_uri is not None:
        buffer["uri"] = buffer_uri
    return {
        "asset": {
            "generator": "MALIEV issue-153 fixture generator v1",
            "version": "2.0",
        },
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": [{"mesh": 0, "name": "cube-10mm"}],
        "meshes": [
            {"primitives": [{"attributes": {"POSITION": 0}, "indices": 1, "mode": 4}]}
        ],
        "buffers": [buffer],
        "bufferViews": [
            {"buffer": 0, "byteLength": 96, "byteOffset": 0, "target": 34962},
            {"buffer": 0, "byteLength": 72, "byteOffset": 96, "target": 34963},
        ],
        "accessors": [
            {
                "bufferView": 0,
                "componentType": 5126,
                "count": 8,
                "max": [10.0, 10.0, 10.0],
                "min": [0.0, 0.0, 0.0],
                "type": "VEC3",
            },
            {"bufferView": 1, "componentType": 5123, "count": 36, "type": "SCALAR"},
        ],
    }


def compact_json(value: object) -> bytes:
    return json.dumps(
        value, ensure_ascii=True, separators=(",", ":"), sort_keys=True
    ).encode("utf-8")


def write_gltf(path: Path) -> None:
    payload = mesh_buffer()
    uri = "data:application/octet-stream;base64," + base64.b64encode(payload).decode(
        "ascii"
    )
    path.write_bytes(compact_json(gltf_document(uri, len(payload))) + b"\n")


def write_glb(path: Path) -> None:
    binary = mesh_buffer()
    document = compact_json(gltf_document(None, len(binary)))
    document += b" " * ((-len(document)) % 4)
    binary += b"\x00" * ((-len(binary)) % 4)
    total_length = 12 + 8 + len(document) + 8 + len(binary)
    path.write_bytes(
        struct.pack("<4sII", b"glTF", 2, total_length)
        + struct.pack("<I4s", len(document), b"JSON")
        + document
        + struct.pack("<I4s", len(binary), b"BIN\x00")
        + binary
    )


def write_3mf(path: Path) -> None:
    vertices = "".join(
        f'<vertex x="{x:.1f}" y="{y:.1f}" z="{z:.1f}"/>' for x, y, z in VERTICES
    )
    triangles = "".join(
        f'<triangle v1="{a}" v2="{b}" v3="{c}"/>' for a, b, c in TRIANGLES
    )
    model = (
        '<?xml version="1.0" encoding="UTF-8"?>'
        '<model unit="millimeter" xml:lang="en-US" xmlns="http://schemas.microsoft.com/3dmanufacturing/core/2015/02">'
        '<metadata name="Title">MALIEV deterministic 10 mm cube</metadata>'
        '<resources><object id="1" type="model"><mesh>'
        f"<vertices>{vertices}</vertices><triangles>{triangles}</triangles>"
        '</mesh></object></resources><build><item objectid="1"/></build></model>'
    ).encode()
    entries = (
        (
            "[Content_Types].xml",
            b'<?xml version="1.0" encoding="UTF-8"?>'
            b'<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
            b'<Default Extension="rels" '
            b'ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
            b'<Default Extension="model" '
            b'ContentType="application/vnd.ms-package.3dmanufacturing-3dmodel+xml"/>'
            b"</Types>",
        ),
        (
            "_rels/.rels",
            b'<?xml version="1.0" encoding="UTF-8"?>'
            b'<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
            b'<Relationship Target="/3D/3dmodel.model" Id="rel0" '
            b'Type="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel"/>'
            b"</Relationships>",
        ),
        ("3D/3dmodel.model", model),
    )
    with zipfile.ZipFile(path, "w") as archive:
        for name, payload in entries:
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 0
            info.external_attr = 0
            archive.writestr(info, payload, compresslevel=9)


def write_exchange_formats(valid: Path) -> None:
    shape = cq.Workplane("XY").box(10, 10, 10, centered=(False, False, False)).val()
    step_path = valid / "cube-10mm.step"
    cq.exporters.export(shape, str(step_path), exportType="STEP")
    step_payload = re.sub(
        rb"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}",
        b"1970-01-01T00:00:00",
        step_path.read_bytes(),
    )
    step_path.write_bytes(re.sub(rb"[ \t]+(?=\r?\n)", b"", step_payload))
    shutil.copyfile(step_path, valid / "cube-10mm.stp")

    iges_path = valid / "cube-10mm.iges"
    writer = IGESControl_Writer()
    if not writer.AddShape(shape.wrapped):
        raise RuntimeError(
            "OpenCascade rejected the deterministic cube for IGES export."
        )
    if not writer.Write(str(iges_path)):
        raise RuntimeError(
            "OpenCascade failed to write the deterministic IGES fixture."
        )
    iges_path.write_bytes(
        re.sub(
            rb"\d{8}\.\d{6}",
            b"19700101.000000",
            iges_path.read_bytes(),
        )
    )
    shutil.copyfile(iges_path, valid / "cube-10mm.igs")


def write_manifest(root: Path) -> None:
    fixtures = []
    for extension, mime_type in MIME_TYPES.items():
        path = root / "valid" / f"cube-10mm{extension}"
        payload = path.read_bytes()
        fixtures.append(
            {
                "path": path.relative_to(root).as_posix(),
                "extension": extension,
                "mimeType": mime_type,
                "alternateAcceptedMimeType": "application/octet-stream",
                "bytes": len(payload),
                "sha256": hashlib.sha256(payload).hexdigest(),
                "expectedBoundingBoxMm": [10.0, 10.0, 10.0],
                "expectedPartCount": 1,
            }
        )
    manifest = {
        "schemaVersion": 1,
        "generatorVersion": 1,
        "generatorCommand": (
            "poetry --directory <GeometryService> run python "
            "<web-worktree>/tests/instant-quotation/fixtures/generate-fixtures.py"
        ),
        "generatorRuntime": "CadQuery 2.7.0 with OpenCascade 7.8.1.1",
        "origin": "MALIEV-authored",
        "containsThirdPartyContent": False,
        "containsCustomerData": False,
        "containsProductionData": False,
        "license": "LicenseRef-MALIEV-Internal-Test-Fixture",
        "geometry": "axis-aligned 10 mm cube at origin",
        "fixtures": fixtures,
    }
    (root / "fixture-manifest.json").write_bytes(
        json.dumps(manifest, indent=2).encode("utf-8") + b"\n"
    )


def generate(root: Path) -> None:
    valid = root / "valid"
    valid.mkdir(parents=True, exist_ok=True)
    write_ascii_stl(valid / "cube-10mm.stl")
    write_obj(valid / "cube-10mm.obj")
    write_3mf(valid / "cube-10mm.3mf")
    write_gltf(valid / "cube-10mm.gltf")
    write_glb(valid / "cube-10mm.glb")
    write_exchange_formats(valid)
    write_manifest(root)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parent)
    arguments = parser.parse_args()
    generate(arguments.output.resolve())
