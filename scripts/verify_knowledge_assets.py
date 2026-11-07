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


# Exact source930 (parent770) removes only these five visible footer labels.
SOURCE930_FILES = frozenset((
    "visual-hole-depths.svg",
    "visual-material-criteria.svg",
    "visual-quotation-package.svg",
    "visual-review-loop.svg",
    "visual-service-inputs.svg",
))
DRAWING_FOOTER = "<text x=\"480\" y=\"457\" font-size=\"15\" text-anchor=\"middle\" fill=\"#53627b\">ILLUSTRATIVE EXAMPLE · NOT A COMPLETE PRODUCTION DRAWING</text>".encode("utf-8")
SOURCE930 = {
    "Legacy.Maliev.Web/wwwroot/src/images/knowledge/visual-hole-depths.svg": (
        "61ed15230758fc1824f5697b9f07f62fb8c2c5b4", "164d84c3ac22ccd0e45c3a1128a526f8e9973709",
        4904, "4b3eef809f4e0df33c51e52880918c38ae5e08b770ec08df6a790f5794a99b27"),
    "Legacy.Maliev.Web/wwwroot/src/images/knowledge/visual-material-criteria.svg": (
        "2e329bf64624671a81f99f79979c76254e5355ee", "292081cd3525db7ed62779483b0b3f9ac39f0efb",
        4163, "28e15bb0fb6ba0e1b1a65426e3790a786f9a363e46ba834a4eee67d4d7e2bd1e"),
    "Legacy.Maliev.Web/wwwroot/src/images/knowledge/visual-quotation-package.svg": (
        "f93ad604fba57ff9f79265ddc7af5e48d2ec15b7", "65e8bd788a08096ed0c47cffa316b7621daac877",
        3156, "79a5880ea032c851ba278456ca846d3a0a8ca7e468208b32fa676c9e62f97a60"),
    "Legacy.Maliev.Web/wwwroot/src/images/knowledge/visual-review-loop.svg": (
        "d4fab4ed787906251b1a15ee0f22d6e629d777c5", "f9c28311541b4a4251c7a0d6b8aa0a8555874e73",
        3053, "ae34525d8ee6021ce2d3cf232b501ef26b993e5c12d5ae3f3123295a2f05f22b"),
    "Legacy.Maliev.Web/wwwroot/src/images/knowledge/visual-service-inputs.svg": (
        "7aeb07d58101f0ba2963e305669e2a0cc6956b43", "4a9275a0e0588269d964db9a30303ce24b97d48c",
        2959, "bb0d02d940631a62fe0e83a2e38483d7f591f5fdecf83fb6239d0c4ae21f6e65"),
}


def historical_bytes(data, frozen):
    path = frozen["targetPath"]
    if path not in SOURCE930:
        return data
    old, _, _, _ = SOURCE930[path]
    intake.require(frozen["blob"] == old, "source930-history")
    position = data.rfind(b"\n</g>")
    intake.require(position >= 0, "source930-boundary")
    return data[:position] + DRAWING_FOOTER + data[position:]


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
    intake.require(set(SOURCE930) == {
        "Legacy.Maliev.Web/wwwroot/src/images/knowledge/" + name for name in SOURCE930_FILES
    }, "source930-mapping")
    evolved = set()
    count = total = svg_count = 0
    for index, frozen in enumerate(rows + inherited):
        if index < len(rows):
            row = measured[index]
            intake.require(type(row) is dict
                           and {key: value for key, value in row.items() if key != "sha256"} == frozen,
                           "manifest-row")
        data = read(root, frozen["targetPath"])
        historical = historical_bytes(data, frozen)
        intake.verify_blob(historical, frozen)
        if index < len(rows):
            intake.require(hashlib.sha256(historical).hexdigest() == row.get("sha256"), "asset-sha256")
        if frozen["targetPath"] in SOURCE930:
            _, blob, size, digest = SOURCE930[frozen["targetPath"]]
            intake.require(len(data) == size and intake.git_hash("blob", data) == blob
                           and hashlib.sha256(data).hexdigest() == digest, "source930-current")
            evolved.add(frozen["targetPath"])
        if frozen["targetPath"].endswith(".svg"):
            policy["verify_svg"](data)
            svg_count += 1
        count += 1
        total += len(data)
    intake.require(evolved == set(SOURCE930), "source930-mapping")
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
