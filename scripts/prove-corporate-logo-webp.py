"""Artifact-only, exact-source, static lossless corporate-logo conversion proof."""
from __future__ import annotations

import hashlib
import binascii
import io
import json
import os
from pathlib import Path
import re
import resource
import subprocess
import sys
import tempfile
import warnings
import time

import PIL
from PIL import Image, ImageDraw, ImageOps, features

MAX_SOURCE_BYTES = 1_048_576
MAX_PIXELS = 8_000_000
MAX_DERIVED_BYTES = 4_194_304
MAX_TOTAL_DERIVED = 16_777_216
SOURCE_PREFIX = "Legacy.Maliev.Web/wwwroot/src/images/customers/"
EXPECTED_VERSION = "13.0.0"
EXPECTED_INPUT_BLOB = "9ab07a0b8bbcfa70e5e0b28095f2b864545c48a3"
WHEEL_NAME = "pillow-13.0.0-cp312-cp312-manylinux_2_27_x86_64.manylinux_2_28_x86_64.whl"
WHEEL_SHA256 = "42d63be3af4f9b951b199b0a9d23365d37ab14687e6d77b74940432c8515c4ca"


def owned_process_ledger(purpose, lease_seconds):
    def identity(pid):
        base = Path("/proc") / str(pid)
        fields = (base / "stat").read_text(encoding="utf-8").rsplit(")", 1)[1].split()
        ticks = int(fields[19])
        executable = (base / "exe").resolve(strict=True)
        stat = executable.stat()
        boot = next(int(row.split()[1]) for row in Path("/proc/stat").read_text().splitlines()
                    if row.startswith("btime "))
        return {"pid": pid, "start_ticks": ticks,
                "actual_start_unix": boot + ticks / os.sysconf("SC_CLK_TCK"),
                "executable": str(executable), "executable_device": stat.st_dev,
                "executable_inode": stat.st_ino}
    parent = identity(os.getppid())
    ledger = {"schema_version": 1, "purpose": purpose,
              "run_id": os.environ["GITHUB_RUN_ID"], "run_attempt": os.environ["GITHUB_RUN_ATTEMPT"],
              "candidate_head": os.environ["LOGO_WEBP_EXPECTED_HEAD"],
              "worker": identity(os.getpid()),
              "supervisor": parent if Path(parent["executable"]).name == "timeout" else None,
              "lease_expires_unix": time.time() + lease_seconds,
              "ports": [], "containers": [], "volumes": [], "persistent_data": False}
    target = Path(os.environ["LOGO_WEBP_VENV"]) / (purpose + "-worker.json")
    target.write_text(json.dumps(ledger, indent=2) + "\n", encoding="utf-8")
    return ledger


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def git_blob(data):
    return hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data,
                        usedforsecurity=False).hexdigest()


def require(condition, code):
    if not condition:
        raise ValueError(code)



ADMISSION_FLOOR_KIB = 3_145_728


def admission_values(available_kib, maximum, current):
    require(type(available_kib) is int and available_kib >= 0, "admission-available-shape")
    require(isinstance(current, str) and re.fullmatch(r"0|[1-9][0-9]*", current) is not None,
            "admission-current-shape")
    require(maximum == "max" or re.fullmatch(r"0|[1-9][0-9]*", maximum) is not None,
            "admission-maximum-shape")
    headroom = None if maximum == "max" else max(0, int(maximum) - int(current))
    admitted = available_kib >= ADMISSION_FLOOR_KIB and (
        headroom is None or headroom >= ADMISSION_FLOOR_KIB * 1024)
    return {"floor_kib": ADMISSION_FLOOR_KIB, "mem_available_kib": available_kib,
            "cgroup_current_bytes": int(current), "cgroup_maximum_bytes": None if maximum == "max" else int(maximum),
            "cgroup_headroom_bytes": headroom, "admitted": admitted, "swap_counted": False}



def admission_chain(available_kib, levels):
    require(type(available_kib) is int and available_kib >= 0, "admission-available-shape")
    require(isinstance(levels, list) and 1 <= len(levels) <= 32, "admission-ancestor-bound")
    require(all(level is not None or index == len(levels) - 1 for index, level in enumerate(levels)),
            "admission-root-marker")
    observations = [admission_values(available_kib, *level) if level is not None else
                    {"kind": "root-interfaces-not-applicable"} for level in levels]
    finite = [row["cgroup_headroom_bytes"] for row in observations
              if row.get("cgroup_headroom_bytes") is not None]
    return {"floor_kib": ADMISSION_FLOOR_KIB, "mem_available_kib": available_kib,
            "visible_cgroup_levels": observations,
            "minimum_visible_cgroup_headroom_bytes": min(finite) if finite else None,
            "admitted": available_kib >= ADMISSION_FLOOR_KIB
                        and all(row.get("admitted", True) for row in observations),
            "swap_counted": False}


def verified_cgroup_mount(mountinfo):
    matches = []
    for row in mountinfo.splitlines():
        halves = row.split(" - ")
        if len(halves) != 2:
            continue
        fields, filesystem = halves[0].split(), halves[1].split()
        if len(fields) >= 6 and len(filesystem) >= 3 and fields[4] == "/sys/fs/cgroup":
            require(filesystem[0] == "cgroup2", "admission-cgroup-filesystem")
            matches.append(fields[3])
    require(len(matches) == 1, "admission-cgroup-mount")
    return matches[0] == "/"


def read_cgroup_level(group, is_true_root, reader=None):
    if reader is None:
        def reader(name):
            with (group / name).open(encoding="ascii") as stream:
                value = stream.read(65)
            require(len(value) <= 64, "admission-cgroup-value-bound")
            return value.strip()
    values = []
    for name in ("memory.max", "memory.current"):
        try:
            values.append(reader(name))
        except FileNotFoundError:
            values.append(None)
    if None in values:
        require(is_true_root and values == [None, None], "admission-cgroup-missing-interface")
        try:
            reader("cgroup.events")
        except FileNotFoundError:
            return None
        raise ValueError("admission-root-events-present")
    return tuple(values)


def require_memory_admission(purpose):
    require(purpose in ("controls", "proof"), "admission-purpose")
    receipt = {"schema_version": 1, "purpose": purpose, "status": "FAILED",
               "floor_kib": ADMISSION_FLOOR_KIB, "observed_unix": time.time(),
               "candidate_head": os.environ["LOGO_WEBP_EXPECTED_HEAD"],
               "run_id": os.environ["GITHUB_RUN_ID"], "run_attempt": os.environ["GITHUB_RUN_ATTEMPT"]}
    output = Path(__file__).resolve().parents[1] / "company-logo-webp-admission"
    output.mkdir(exist_ok=True)
    try:
        with Path("/proc/meminfo").open(encoding="ascii") as stream:
            meminfo = stream.read(65_537)
        require(len(meminfo) <= 65_536, "admission-meminfo-bound")
        available = re.findall(r"^MemAvailable:\s+([0-9]+) kB$", meminfo, re.MULTILINE)
        require(len(available) == 1, "admission-meminfo-shape")
        with Path("/proc/self/cgroup").open(encoding="ascii") as stream:
            groups = stream.read(4097)
        require(len(groups) <= 4096, "admission-cgroup-bound")
        unified = [row[3:] for row in groups.splitlines() if row.startswith("0::")]
        require(len(unified) == 1 and unified[0].startswith("/")
                and ".." not in unified[0].split("/"), "admission-cgroup-path")
        with Path("/proc/self/mountinfo").open(encoding="ascii") as stream:
            mountinfo = stream.read(131_073)
        require(len(mountinfo) <= 131_072, "admission-mountinfo-bound")
        mounted_at_true_root = verified_cgroup_mount(mountinfo)
        cgroup_root = Path("/sys/fs/cgroup").resolve(strict=True)
        require(str(cgroup_root) == "/sys/fs/cgroup", "admission-cgroup-mount-path")
        group = (cgroup_root / unified[0].lstrip("/")).resolve(strict=True)
        require(group.is_relative_to(cgroup_root), "admission-cgroup-scope")
        levels = []
        while True:
            require(len(levels) < 32, "admission-ancestor-bound")
            levels.append(read_cgroup_level(group, mounted_at_true_root and group == cgroup_root))
            if group == cgroup_root:
                break
            group = group.parent
        receipt.update(admission_chain(int(available[0]), levels))
        require(receipt["admitted"], "admission-insufficient-memory")
        receipt["status"] = "ADMITTED"
    finally:
        with (output / (purpose + ".json")).open("x", encoding="utf-8") as stream:
            stream.write(json.dumps(receipt, sort_keys=True, indent=2) + "\n")
    return receipt

def safe_file(root, relative):
    require(isinstance(relative, str) and relative.startswith(SOURCE_PREFIX), "source-path-prefix")
    require(re.fullmatch(re.escape(SOURCE_PREFIX) + r"[a-z0-9-]+\.(png|jpg|jpeg|gif)", relative) is not None,
            "source-path-shape")
    path = root / relative
    require(path.resolve(strict=True) == path and path.is_file() and not path.is_symlink(), "source-path-ownership")
    return path


def read_source(root, asset):
    require(set(asset) == {"source_path", "source_git_blob", "source_bytes", "derived_path"},
            "asset-schema")
    require(re.fullmatch(r"[0-9a-f]{40}", asset["source_git_blob"]) is not None, "source-git-shape")
    require(type(asset["source_bytes"]) is int and 0 < asset["source_bytes"] <= MAX_SOURCE_BYTES,
            "source-size-bound")
    expected_output = re.sub(r"\.(png|jpg|jpeg|gif)$", ".webp", asset["source_path"])
    require(asset["derived_path"] == expected_output, "derived-path-binding")
    path = safe_file(root, asset["source_path"])
    with path.open("rb") as stream:
        data = stream.read(MAX_SOURCE_BYTES + 1)
    require(len(data) == asset["source_bytes"] <= MAX_SOURCE_BYTES, "source-size-identity")
    require(git_blob(data) == asset["source_git_blob"], "source-git-identity")
    return data


def png_preflight_info(data):
    if not data.startswith(b"\x89PNG\r\n\x1a\n"):
        return
    require(len(data) >= 33 and data[8:16] == b"\0\0\0\rIHDR", "source-png-ihdr")
    color_chunks = []
    offset, count, ended = 8, 0, False
    while offset < len(data):
        require(offset + 12 <= len(data) and count < 256, "source-png-chunk-bound")
        size = int.from_bytes(data[offset:offset + 4], "big")
        kind = data[offset + 4:offset + 8]
        end = offset + 12 + size
        require(end <= len(data), "source-png-chunk-size")
        crc = int.from_bytes(data[end - 4:end], "big")
        require(binascii.crc32(data[offset + 4:end - 4]) & 0xffffffff == crc, "source-png-crc")
        if kind in (b"gAMA", b"cHRM", b"sRGB", b"cICP", b"mDCV", b"cLLI"):
            value = data[offset + 8:end - 4]
            color_chunks.append({"kind": kind.decode("ascii"), "bytes": size,
                                 "sha256": sha256(value), "value_hex": value.hex() if size <= 32 else None})
        require(not (65 <= kind[0] <= 90) or kind in (b"IHDR", b"PLTE", b"IDAT", b"IEND"),
                "unsupported-png-critical-chunk")
        count += 1
        offset = end
        if kind == b"IEND":
            require(size == 0 and offset == len(data), "source-png-end")
            ended = True
            break
    require(ended, "source-png-missing-end")
    return {"width": int.from_bytes(data[16:20], "big"), "height": int.from_bytes(data[20:24], "big"),
            "bit_depth": data[24], "color_type": data[25], "color_chunks": color_chunks}


def require_supported_png(data):
    header = png_preflight_info(data)
    if header is not None:
        require(header["bit_depth"] in (1, 2, 4, 8), "unsupported-png-bit-depth")
        require(not header["color_chunks"], "unsupported-png-color-metadata")


def inspect_static(data):
    require_supported_png(data)
    with Image.open(io.BytesIO(data), formats=("PNG", "JPEG", "GIF")) as image:
        require(image.format in ("PNG", "JPEG", "GIF"), "source-format")
        require(image.mode in ("1", "L", "LA", "P", "RGB", "RGBA"), "unsupported-source-mode")
        frames = getattr(image, "n_frames", 1)
        require(0 < image.width <= 8192 and 0 < image.height <= 8192
                and image.width * image.height <= MAX_PIXELS, "decoded-size-bound")
        # This bounded lane does not flatten or approximate animation/timing/loop semantics.
        require(frames == 1 and not getattr(image, "is_animated", False), "unsupported-animation")
        # PNG decoders discover trailing metadata (including post-IDAT iTXt) at load_end.
        # Decode only after size/static bounds, then freeze all source metadata.
        image.load()
        require("loop" not in image.info and image.info.get("duration", 0) in (None, 0),
                "unsupported-animation-metadata")
        icc = image.info.get("icc_profile", b"")
        exif = image.info.get("exif", b"")
        xmp = image.info.get("xmp", b"")
        require(isinstance(icc, bytes) and len(icc) <= 524_288, "icc-bound")
        require(not icc or image.mode in ("RGB", "RGBA", "P"), "unsupported-source-profile-mode")
        require(isinstance(exif, bytes) and len(exif) <= 262_144, "exif-bound")
        if isinstance(xmp, str):
            xmp = xmp.encode("utf-8")
        require(isinstance(xmp, bytes) and len(xmp) <= 262_144, "xmp-bound")
        orientation = image.getexif().get(274, 1)
        require(type(orientation) is int and 1 <= orientation <= 8, "orientation-shape")
        rgba = image.convert("RGBA")
        pixels = rgba.tobytes()
        return {
            "format": image.format, "source_mode": image.mode, "width": image.width, "height": image.height,
            "frames": frames, "loop": None, "durations_ms": [0], "orientation": orientation,
            "icc_sha256": sha256(icc), "decoded_rgba_sha256": sha256(pixels),
            "alpha_sha256": sha256(rgba.getchannel("A").tobytes()),
        }, rgba, {"icc_profile": icc, "exif": exif, "xmp": xmp}


def require_static_lossless_webp(data):
    require(len(data) >= 20 and data[:4] == b"RIFF" and data[8:12] == b"WEBP"
            and int.from_bytes(data[4:8], "little") + 8 == len(data), "derived-riff")
    offset, chunks = 12, []
    while offset < len(data):
        require(offset + 8 <= len(data) and len(chunks) < 32, "derived-chunk-bound")
        kind = data[offset:offset + 4]
        size = int.from_bytes(data[offset + 4:offset + 8], "little")
        if kind == b"VP8X":
            require(size == 10 and offset + 18 <= len(data) and data[offset + 8] & 2 == 0,
                    "derived-animation-flag")
        offset += 8 + size + (size % 2)
        require(offset <= len(data), "derived-chunk-size")
        chunks.append(kind)
    require(offset == len(data) and chunks.count(b"VP8L") == 1
            and all(kind in (b"VP8X", b"VP8L", b"ICCP", b"EXIF", b"XMP ") for kind in chunks),
            "derived-static-lossless-chunks")


def convert_static(data, target):
    source, rgba, metadata = inspect_static(data)
    try:
        rgba.save(target, format="WEBP", lossless=True, exact=True, quality=100, method=6, **metadata)
    finally:
        rgba.close()
    require(target.stat().st_size <= MAX_DERIVED_BYTES, "derived-size-bound")
    result = target.read_bytes()
    require_static_lossless_webp(result)
    with Image.open(io.BytesIO(result), formats=("WEBP",)) as decoded:
        require(decoded.format == "WEBP" and getattr(decoded, "n_frames", 1) == 1
                and not getattr(decoded, "is_animated", False), "derived-static-format")
        # Pillow exposes a decoder loop default even for static files; absence of ANIM/ANMF
        # chunks above, rather than that synthetic info field, proves no timing/loop data.
        require(decoded.size == (source["width"], source["height"]), "derived-dimensions")
        require(decoded.info.get("icc_profile", b"") == metadata["icc_profile"], "derived-icc")
        require(decoded.getexif().get(274, 1) == source["orientation"], "derived-orientation")
        require(decoded.info.get("xmp", b"") == metadata["xmp"], "derived-xmp")
        with decoded.convert("RGBA") as pixels:
            require(sha256(pixels.tobytes()) == source["decoded_rgba_sha256"], "derived-pixel-equivalence")
            require(sha256(pixels.getchannel("A").tobytes()) == source["alpha_sha256"], "derived-alpha-equivalence")
    return source, {"sha256": sha256(result), "git_blob": git_blob(result), "bytes": len(result)}


def contact_sheet(root, assets, payload):
    width, height = 1200, 960
    with Image.new("RGB", (width, height), "#f0f0f0") as sheet:
        draw = ImageDraw.Draw(sheet)
        for index, asset in enumerate(assets):
            x, y = (index % 3) * 400, (index // 3) * 160
            label = Path(asset["source_path"]).stem
            draw.text((x + 8, y + 6), label + " | source / lossless WebP", fill="#111111")
            for column, path in enumerate((root / asset["source_path"], payload / asset["derived_path"])):
                with Image.open(path) as opened, ImageOps.exif_transpose(opened) as oriented:
                    with oriented.convert("RGBA") as thumbnail:
                        thumbnail.thumbnail((180, 115), Image.Resampling.LANCZOS)
                        left = x + column * 200 + 10
                        top = y + 30
                        for row in range(12):
                            for cell in range(18):
                                color = "#ffffff" if (row + cell) % 2 else "#cccccc"
                                draw.rectangle((left + cell * 10, top + row * 10,
                                                left + cell * 10 + 9, top + row * 10 + 9), fill=color)
                        sheet.paste(thumbnail, (left, top), thumbnail)
        sheet.save(payload / "contact-sheet.png", format="PNG")


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def main():
    root = Path(__file__).resolve().parents[1]
    output = root / "company-logo-webp-proof"
    output.mkdir(exist_ok=False)
    receipt = {"schema_version": 1, "status": "FAILED", "resources": "foreground process only; no background workers"}
    try:
        receipt["resource_ledger"] = owned_process_ledger("proof", 305)
        receipt["admission"] = require_memory_admission("proof")
        resource.setrlimit(resource.RLIMIT_AS, (805_306_368, 805_306_368))
        resource.setrlimit(resource.RLIMIT_CPU, (120, 120))
        warnings.simplefilter("error", Image.DecompressionBombWarning)
        Image.MAX_IMAGE_PIXELS = MAX_PIXELS
        require(PIL.__version__ == EXPECTED_VERSION and features.check("webp"), "tool-identity")
        wheel = Path(os.environ["LOGO_WEBP_VENV"]) / "wheels" / WHEEL_NAME
        require(wheel.is_file() and wheel.stat().st_size <= 16_777_216, "tool-wheel-bound")
        require(sha256(wheel.read_bytes()) == WHEEL_SHA256, "tool-wheel-digest")
        expected_head = os.environ["LOGO_WEBP_EXPECTED_HEAD"]
        require(re.fullmatch(r"[0-9a-f]{40}", expected_head) is not None, "head-shape")
        head = subprocess.run(["git", "--no-optional-locks", "rev-parse", "HEAD"], cwd=root,
                              check=True, capture_output=True, text=True, timeout=5).stdout.strip()
        require(head == expected_head, "exact-head-binding")
        run, attempt = os.environ["GITHUB_RUN_ID"], os.environ["GITHUB_RUN_ATTEMPT"]
        require(re.fullmatch(r"[1-9][0-9]*", run) and re.fullmatch(r"[1-9][0-9]*", attempt), "run-binding")
        receipt.update(candidate_head=head, run_id=run, run_attempt=attempt,
                       pillow=PIL.__version__, webp=features.version("webp"), python=sys.version.split()[0],
                       wheel=WHEEL_NAME, wheel_sha256=WHEEL_SHA256)
        input_path = root / "tests/corporate-logo-webp-inputs.json"
        raw_manifest = input_path.read_bytes()
        require(len(raw_manifest) <= 49_152, "input-manifest-bound")
        require(git_blob(raw_manifest) == EXPECTED_INPUT_BLOB, "input-manifest-git-binding")
        manifest = json.loads(raw_manifest)
        require(set(manifest) == {"schema_version", "source_candidate", "source_component", "asset_count", "assets"}
                and manifest["schema_version"] == 1 and manifest["asset_count"] == 18
                and len(manifest["assets"]) == 18, "input-manifest-schema")
        require(manifest["source_candidate"] == "9093de0f5a0a45239100eb4c07ae42106732a873", "source-candidate-binding")
        require(manifest["source_component"] == {"path": "Legacy.Maliev.Web/Components/Pages/About/CorporateCustomers.razor",
                                                 "git_blob": "6c3f49a3c232f2f9b1a32fead5675420ebc4ae59"}, "source-component-binding")
        assets = manifest["assets"]
        require(len({asset["source_path"] for asset in assets}) == 18
                and len({asset["derived_path"] for asset in assets}) == 18, "unique-assets")
        receipt.update(input_sha256=sha256(raw_manifest), input_git_blob=git_blob(raw_manifest),
                       source_candidate=manifest["source_candidate"], asset_count=18)
        inventory, rejected = [], []
        for asset in assets:
            data = read_source(root, asset)
            row = dict(asset, source_sha256=sha256(data))
            try:
                row["png_preflight"] = png_preflight_info(data)
                metadata, rgba, _ = inspect_static(data)
                rgba.close()
                row.update(metadata, supported=True)
            except ValueError as error:
                row.update(supported=False, rejection=str(error))
                rejected.append(asset["source_path"])
            inventory.append(row)
        write_json(output / "source-inventory.json", dict(receipt, assets=inventory))
        require(not rejected, "unsupported-source-assets")
        with tempfile.TemporaryDirectory(prefix=".staging-", dir=output) as temp:
            payload = Path(temp) / "qualified"
            payload.mkdir()
            result, total = [], 0
            for asset, source_row in zip(assets, inventory, strict=True):
                data = read_source(root, asset)
                require(sha256(data) == source_row["source_sha256"], "source-reread-sha256")
                target = payload / asset["derived_path"]
                target.parent.mkdir(parents=True, exist_ok=True)
                metadata, derived = convert_static(data, target)
                require(metadata["decoded_rgba_sha256"] == source_row["decoded_rgba_sha256"], "source-reread-identity")
                total += derived["bytes"]
                require(total <= MAX_TOTAL_DERIVED, "total-derived-bound")
                result.append(dict(source_row, derived=derived, equivalence="EXACT_RGBA_ALPHA_DIMENSIONS_ORIENTATION_ICC_XMP"))
            contact_sheet(root, assets, payload)
            qualified = dict(receipt, status="PASS", assets=result, derived_bytes=total,
                             animation_policy="Fail closed; this lane supports only single-frame sources without loop/duration metadata.",
                             preview="Contact sheet is a review aid, not publishable artwork; decoded pixels and metadata are authoritative.",
                             contact_sheet_sha256=sha256((payload / "contact-sheet.png").read_bytes()))
            write_json(payload / "manifest.json", qualified)
            write_json(payload / "source-bindings.json", manifest)
            (payload / "README.txt").write_text(
                "Artifact-only proof. Root must verify exact run/head, all18 source/derived digests, pixel/alpha/ICC/orientation proof and contact sheet before publishing generated bytes.\n",
                encoding="utf-8")
            payload.rename(output / "qualified")
        receipt.update(status="PASS", manifest_sha256=sha256((output / "qualified/manifest.json").read_bytes()),
                       qualified_directory="qualified")
        print(json.dumps(receipt, sort_keys=True))
    except Exception as error:
        receipt["failure_category"] = type(error).__name__
        receipt["failure_code"] = str(error) if isinstance(error, ValueError) and re.fullmatch(r"[a-z-]{1,80}", str(error)) else "conversion-failed"
        raise
    finally:
        write_json(output / "receipt.json", receipt)


if __name__ == "__main__":
    main()
