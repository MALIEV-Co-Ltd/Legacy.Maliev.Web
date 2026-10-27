"""Six exact committed About customer logos; artifact-only lossless conversion."""
from __future__ import annotations

import importlib.util
import json
import os
from pathlib import Path
import re
import resource
import sys
import tempfile
import time
import warnings

ROOT = Path(__file__).resolve().parents[1]
SHARED_BLOB = "1a729ae1167675b07717b8809fa9c493ae32e24d"
MANIFEST_BLOB = "35e5cf98997ccb2baa13563fcca8f5212fcdde06"
INPUT_PREFIX = "tests/about-customer-row-source-inputs/"
OUTPUT_PREFIX = "Legacy.Maliev.Web/wwwroot/src/images/customers/"
SLUGS = ("alten-korea", "forth", "do-day-dream", "thai-agri-foods",
         "sahamitr-pressure-container", "crown-ceramics")
METADATA_KEYS = ("format", "width", "height", "orientation", "icc_sha256",
                 "exif_bytes", "exif_sha256", "xmp_bytes", "xmp_sha256")


def require(condition, code):
    if not condition:
        raise ValueError(code)


def load_shared():
    path = ROOT / "scripts/prove-corporate-logo-webp.py"
    raw = read_bounded(path, 65_536)
    import hashlib
    blob = hashlib.sha1(b"blob " + str(len(raw)).encode("ascii") + b"\0" + raw,
                       usedforsecurity=False).hexdigest()
    require(blob == SHARED_BLOB, "shared-converter-identity")
    spec = importlib.util.spec_from_file_location("reviewed_corporate_logo_proof", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def read_bounded(path, maximum):
    with path.open("rb") as stream:
        data = stream.read(maximum + 1)
    require(len(data) <= maximum, "read-bound")
    return data


def read_manifest(shared, raw):
    require(len(raw) <= 16_384 and shared.git_blob(raw) == MANIFEST_BLOB, "manifest-identity")
    manifest = json.loads(raw)
    require(set(manifest) == {"schema_version", "source_commits", "conversion", "assets"}
            and manifest["schema_version"] == 1
            and manifest["source_commits"] == [
                "d7bb4f884786b4bb6460c5003d1da43c7ce1578f",
                "d5385fdd683e443668bcef10e5e1e5286e43e707",
                "edf4e45d7ffce2f55b0d607ca82d51e81c330781"],
            "manifest-schema")
    require(tuple(row["slug"] for row in manifest["assets"]) == SLUGS, "six-source-order")
    for row in manifest["assets"]:
        suffix = ".jpg" if row["slug"] == "do-day-dream" else ".png"
        require(row["source_path"] == INPUT_PREFIX + row["slug"] + suffix
                and row["original_path"] == "Maliev.Web/wwwroot/src/images/customers/" + row["slug"] + suffix
                and row["derived_path"] == OUTPUT_PREFIX + row["slug"] + ".webp",
                "closed-path-binding")
    require(sum(row["source_bytes"] for row in manifest["assets"]) == 27_985, "six-source-size")
    return manifest


def read_source(shared, root, row):
    relative = row["source_path"]
    require(relative == INPUT_PREFIX + row["slug"] + (".jpg" if row["slug"] == "do-day-dream" else ".png")
            and row["slug"] in SLUGS, "closed-source-path")
    path = root / relative
    require(path.resolve(strict=True) == path and path.is_file() and not path.is_symlink(),
            "source-path-ownership")
    data = read_bounded(path, 16_384)
    require(len(data) == row["source_bytes"]
            and shared.git_blob(data) == row["source_git_blob"]
            and shared.sha256(data) == row["source_sha256"], "source-bytes-identity")
    return data


def require_metadata(expected, actual):
    require(all(actual[key] == expected[key] for key in METADATA_KEYS), "source-metadata-identity")
    require(actual["frames"] == 1 and actual["loop"] is None and actual["durations_ms"] == [0],
            "source-static-identity")


def require_memory_admission(shared, purpose):
    # Keep the reviewed admission algorithm and every visible ancestor, with a distinct receipt.
    require(purpose in ("customer-row-controls", "customer-row"), "admission-purpose")
    receipt = {"schema_version": 1, "purpose": purpose, "status": "FAILED",
               "floor_kib": shared.ADMISSION_FLOOR_KIB, "observed_unix": time.time(),
               "candidate_head": os.environ["LOGO_WEBP_EXPECTED_HEAD"],
               "run_id": os.environ["GITHUB_RUN_ID"], "run_attempt": os.environ["GITHUB_RUN_ATTEMPT"]}
    output = ROOT / "company-logo-webp-admission"
    output.mkdir(exist_ok=True)
    try:
        meminfo = read_bounded(Path("/proc/meminfo"), 65_536).decode("ascii")
        available = re.findall(r"^MemAvailable:\s+([0-9]+) kB$", meminfo, re.MULTILINE)
        require(len(available) == 1, "admission-meminfo-shape")
        groups = read_bounded(Path("/proc/self/cgroup"), 4096).decode("ascii")
        unified = [row[3:] for row in groups.splitlines() if row.startswith("0::")]
        require(len(unified) == 1 and unified[0].startswith("/")
                and ".." not in unified[0].split("/"), "admission-cgroup-path")
        mountinfo = read_bounded(Path("/proc/self/mountinfo"), 131_072).decode("ascii")
        mounted_at_true_root = shared.verified_cgroup_mount(mountinfo)
        cgroup_root = Path("/sys/fs/cgroup").resolve(strict=True)
        require(str(cgroup_root) == "/sys/fs/cgroup", "admission-cgroup-mount-path")
        group = (cgroup_root / unified[0].lstrip("/")).resolve(strict=True)
        require(group.is_relative_to(cgroup_root), "admission-cgroup-scope")
        levels = []
        while True:
            require(len(levels) < 32, "admission-ancestor-bound")
            levels.append(shared.read_cgroup_level(group, mounted_at_true_root and group == cgroup_root))
            if group == cgroup_root:
                break
            group = group.parent
        receipt.update(shared.admission_chain(int(available[0]), levels))
        require(receipt["admitted"], "admission-insufficient-memory")
        receipt["status"] = "ADMITTED"
    finally:
        with (output / (purpose + ".json")).open("x", encoding="utf-8") as stream:
            stream.write(json.dumps(receipt, sort_keys=True, indent=2) + "\n")
    return receipt


def main():
    output = ROOT / "company-logo-webp-proof/customer-row"
    output.mkdir(exist_ok=False)
    receipt = {"schema_version": 1, "status": "FAILED", "asset_count": 6,
               "persistent_data": False, "resources": "one foreground process; no children/network"}
    try:
        shared = load_shared()
        receipt["resource_ledger"] = shared.owned_process_ledger("customer-row", 65)
        receipt["admission"] = require_memory_admission(shared, "customer-row")
        resource.setrlimit(resource.RLIMIT_AS, (805_306_368, 805_306_368))
        resource.setrlimit(resource.RLIMIT_CPU, (45, 45))
        warnings.simplefilter("error", shared.Image.DecompressionBombWarning)
        shared.Image.MAX_IMAGE_PIXELS = shared.MAX_PIXELS
        require(shared.PIL.__version__ == shared.EXPECTED_VERSION and shared.features.check("webp"),
                "tool-identity")
        wheel = Path(os.environ["LOGO_WEBP_VENV"]) / "wheels" / shared.WHEEL_NAME
        require(wheel.is_file() and not wheel.is_symlink()
                and shared.sha256(read_bounded(wheel, 16_777_216)) == shared.WHEEL_SHA256,
                "tool-wheel-identity")
        head = os.environ["LOGO_WEBP_EXPECTED_HEAD"]
        require(re.fullmatch(r"[0-9a-f]{40}", head) is not None, "head-shape")
        # Exact SHA checkout is detached; no Git child or ambient branch resolution.
        require(read_bounded(ROOT / ".git/HEAD", 128).decode("ascii").strip() == head,
                "exact-head-binding")
        require(all(re.fullmatch(r"[1-9][0-9]*", os.environ[key]) is not None
                    for key in ("GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT")), "run-binding")
        raw_manifest = read_bounded(ROOT / "tests/about-customer-row-webp-inputs.json", 16_384)
        manifest = read_manifest(shared, raw_manifest)
        receipt.update(candidate_head=head, run_id=os.environ["GITHUB_RUN_ID"],
                       run_attempt=os.environ["GITHUB_RUN_ATTEMPT"],
                       manifest_git_blob=shared.git_blob(raw_manifest),
                       manifest_sha256=shared.sha256(raw_manifest), converter_git_blob=SHARED_BLOB,
                       pillow=shared.PIL.__version__, webp=shared.features.version("webp"),
                       python=sys.version.split()[0], wheel_sha256=shared.WHEEL_SHA256,
                       source_commits=manifest["source_commits"])
        with tempfile.TemporaryDirectory(prefix=".staging-", dir=output) as temporary:
            payload = Path(temporary) / "qualified"
            payload.mkdir()
            results, total = [], 0
            for row in manifest["assets"]:
                data = read_source(shared, ROOT, row)
                metadata, rgba, _ = shared.inspect_static(data)
                try:
                    require_metadata(row, metadata)
                finally:
                    rgba.close()
                target = payload / row["derived_path"]
                target.parent.mkdir(parents=True, exist_ok=True)
                converted, derived = shared.convert_static(data, target)
                require_metadata(row, converted)
                require(converted["decoded_rgba_sha256"] == metadata["decoded_rgba_sha256"]
                        and converted["alpha_sha256"] == metadata["alpha_sha256"],
                        "source-reread-identity")
                total += derived["bytes"]
                require(derived["bytes"] <= 65_536 and total <= 393_216, "six-output-bound")
                results.append(dict(row, decoded_rgba_sha256=converted["decoded_rgba_sha256"],
                                    alpha_sha256=converted["alpha_sha256"], derived=derived,
                                    equivalence="EXACT_RGBA_ALPHA_DIMENSIONS_ORIENTATION_ICC_EXIF_XMP"))
            # Same reviewed source/derived visual inspection function; no runtime outputs.
            shared.contact_sheet(ROOT, manifest["assets"], payload)
            shared.write_json(payload / "six-source-proof.json", dict(receipt, assets=results, status="PASS"))
            payload.rename(output / "qualified")
        receipt.update(status="PASS", assets=results, total_derived_bytes=total)
    except Exception as error:
        receipt["failure_type"] = type(error).__name__
    finally:
        with (output / "receipt.json").open("x", encoding="utf-8") as stream:
            stream.write(json.dumps(receipt, ensure_ascii=False, sort_keys=True, indent=2) + "\n")
    return 0 if receipt["status"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
