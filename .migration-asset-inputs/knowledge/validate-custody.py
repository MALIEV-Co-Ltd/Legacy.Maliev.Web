"""Review-only knowledge byte/SVG custody; no HTTP, decoding, or release acceptance."""
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("knowledge_stage", ROOT / "scripts/stage_knowledge_assets.py")
GUARD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(GUARD)
require = GUARD.require
raw = GUARD.bounded_read(ROOT, "docs/migration/knowledge-assets.v1.json", 1024 * 1024)
require(GUARD.git_hash("blob", raw) == "af0639089ef33a8c7539da4074c2c0705dba7b10", "manifest-custody")
manifest = json.loads(raw)
seed_raw = GUARD.bounded_read(ROOT, "docs/migration/knowledge-source-intake.v1.json", 1024 * 1024)
require(GUARD.git_hash("blob", seed_raw) == GUARD.SEED_BLOB, "seed-custody")
seed = json.loads(seed_raw)
rows, inherited = GUARD.validate_seed(seed)
require(manifest["sourceCommit"] == GUARD.SOURCE_COMMIT and manifest["sourceTree"] == GUARD.SOURCE_TREE
        and manifest["seedBlob"] == GUARD.SEED_BLOB and len(manifest["rows"]) == 70
        and manifest["inherited"] == inherited, "manifest-binding")
require(all(manifest[key] is False for key in ("decodedDimensionsVerified", "publicHttpVerified", "rightsCertified", "releaseAccepted")), "acceptance-flags")
expected = {row["targetPath"]: row for row in rows}
require(len(expected) == 70 and len({row["targetPath"] for row in manifest["rows"]}) == 70, "manifest-roster")
policy = GUARD.load_policy(ROOT)
counts = {"webp": 0, "svg": 0, "provenance": 0}
for row in manifest["rows"]:
    frozen = expected.get(row["targetPath"])
    require(frozen is not None and all(row[key] == frozen[key] for key in ("sourcePath", "targetPath", "blob", "bytes", "kind", "targetPreimage")), "manifest-row")
    data = GUARD.bounded_read(ROOT, row["targetPath"], 8 * 1024 * 1024)
    GUARD.verify_blob(data, row)
    require(hashlib.sha256(data).hexdigest() == row["sha256"], "asset-sha256")
    if row["kind"] == "svg":
        policy["verify_svg"](data)
    counts[row["kind"]] += 1
for row in inherited:
    GUARD.verify_blob(GUARD.bounded_read(ROOT, row["targetPath"], 8 * 1024 * 1024), row)
require(counts == {"webp": 42, "svg": 17, "provenance": 11}, "kind-counts")
print(json.dumps({"status": "byte-svg-custody-only", "rows": 70, "counts": counts, "http": False, "decoded": False, "releaseAccepted": False}, sort_keys=True))
