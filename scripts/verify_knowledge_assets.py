"""Recheck frozen Knowledge visuals in the current target without staging or network."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import sys

import stage_knowledge_assets as intake

MANIFEST = "docs/migration/knowledge-assets.v1.json"
MANIFEST_BLOB = "af0639089ef33a8c7539da4074c2c0705dba7b10"
SEED = "docs/migration/knowledge-source-intake.v1.json"


def verify(root, reader=None, policy=None):
    root = Path(root)
    policy = intake.load_policy(root) if policy is None else policy
    read = policy["read_asset"] if reader is None else reader
    seed_raw = read(root, SEED)
    raw = read(root, MANIFEST)
    intake.require(intake.git_hash("blob", seed_raw) == intake.SEED_BLOB, "seed-custody")
    intake.require(intake.git_hash("blob", raw) == MANIFEST_BLOB, "manifest-custody")
    seed, manifest = policy["parse_manifest"](seed_raw), policy["parse_manifest"](raw)
    rows, inherited = intake.validate_seed(seed)
    intake.require(manifest.get("schema") == "maliev-knowledge-assets.v1"
                   and manifest.get("sourceCommit") == intake.SOURCE_COMMIT
                   and manifest.get("sourceTree") == intake.SOURCE_TREE
                   and manifest.get("seedBlob") == intake.SEED_BLOB
                   and manifest.get("inherited") == inherited, "manifest-shape")
    measured = manifest.get("rows")
    intake.require(type(measured) is list and len(measured) == len(rows), "manifest-rows")
    count = total = svg_count = 0
    for index, frozen in enumerate(rows + inherited):
        if index < len(rows):
            row = measured[index]
            intake.require(type(row) is dict
                           and {key: value for key, value in row.items() if key != "sha256"} == frozen,
                           "manifest-row")
        data = read(root, frozen["targetPath"])
        intake.verify_blob(data, frozen)
        if index < len(rows):
            intake.require(hashlib.sha256(data).hexdigest() == row.get("sha256"), "asset-sha256")
        if frozen["targetPath"].endswith(".svg"):
            policy["verify_svg"](data)
            svg_count += 1
        count += 1
        total += len(data)
    intake.require((count, svg_count) == (72, 19), "asset-count")
    return {"status": "custody-and-svg-verified", "files": count, "svg": svg_count,
            "bytes": total, "decodedDimensionsVerified": False, "publicHttpVerified": False,
            "releaseAccepted": False}


def main():
    root = Path(__file__).resolve().parents[1]
    try:
        result = verify(root)
    except Exception as error:
        # Never emit file bodies, paths from exceptions or arbitrary XML parser messages.
        category = str(error) if isinstance(error, intake.IntakeError) else "input-read-or-parse"
        print(json.dumps({"status": "rejected", "category": category}, sort_keys=True))
        return 1
    print(json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())
