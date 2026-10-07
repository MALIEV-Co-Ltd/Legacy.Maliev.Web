"""Materialize only the reviewed raw source capsule; never commit candidate C#."""

import argparse
import json
import os
from pathlib import Path
import re
import subprocess

REPOSITORY = "MALIEV-Co-Ltd/Legacy.Maliev.Web"
COUNTRY_POLICY_SHA256 = "2f6d8e628512d63580146c2e07b15d37273467a6c9e3d31d01420aaca20cd8b1"
EXPECTED_POLICY_SHA256 = "100ae2ae0a4a8c25cbf47ae3f4a0e0039e93f211d091ec48b192852e176ba43b"


import sealed_source_capsule as shared

sha256 = shared.digest
parse_json = shared.parse_json
canonical_path = shared.canonical_path
decode_blob = shared.decode_git_blob
NoRedirect = shared.NoRedirect

def load_policy(path):
    raw = Path(path).read_bytes()
    if sha256(raw) not in {EXPECTED_POLICY_SHA256, COUNTRY_POLICY_SHA256}:
        raise ValueError("Reviewed policy raw-byte identity changed")
    return parse_json(raw)

def fetch_blob(oid):
    return shared.fetch_git_blob(REPOSITORY, oid)

def validate_capsule(manifest_bytes, capsule_bytes, policy):
    if sha256(manifest_bytes) != policy["manifestSha256"]:
        raise ValueError("Reviewed manifest digest mismatch")
    manifest = parse_json(manifest_bytes)
    for key in ("owner", "acceptedBase", "sourcePins", "sourceBindingSha256", "sourceFiles", "capsuleRows"):
        if manifest.get(key) != policy[key]: raise ValueError("Reviewed manifest association changed: " + key)
    count = 1952 if policy.get("sliceKind") == "country-operation-v1" else 1983
    if manifest.get("schemaVersion") != 1 or len(manifest["sourceFiles"]) != count:
        raise ValueError("Frozen source inventory changed")
    expected = {}
    aliases = set()
    for row in manifest["sourceFiles"]:
        name = canonical_path(row["path"])
        if name.casefold() in aliases: raise ValueError("Frozen source case collision")
        aliases.add(name.casefold());expected[name] = row
    for row in manifest["capsuleRows"]:
        source = expected.get(row["path"])
        if source is None or any(source[key] != row[key] for key in ("path", "bytes", "sha256")):
            raise ValueError("Postimage is outside the frozen source inventory")
    files = shared.validate_zip(capsule_bytes, manifest["capsuleSha256"], manifest["capsuleBytes"], manifest["capsuleRows"])
    return manifest, files


def verify_source(root, policy, dist_postimage=None):
    head = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
    if head != policy["acceptedBase"]:
        raise ValueError("candidate base changed")
    changed = subprocess.check_output(["git", "-C", str(root), "diff", "HEAD", "--name-only", "-z"]).decode().split("\0")
    allowed = {row["path"] for row in policy["sourceFiles"]}
    extras = subprocess.check_output(["git", "-C", str(root), "ls-files", "--others", "--exclude-standard", "-z"]).decode().split("\0")
    generated = policy.get("qualificationOutputRoots", [])
    unexpected = [name for name in filter(None, extras) if name not in allowed and not any(name.startswith(prefix + "/") for prefix in generated)]
    if unexpected:
        raise ValueError("undeclared untracked candidate source")
    if set(filter(None, changed)) - allowed:
        raise ValueError("tracked base source changed outside reviewed candidate")
    rows = policy["sourceFiles"]
    if dist_postimage is not None:
        if {r["path"] for r in dist_postimage} != set(policy["distPaths"]):
            raise ValueError("asset postimage inventory mismatch")
        rows = [r for r in rows if r["path"] not in policy["distPaths"]] + dist_postimage
    for row in rows:
        target = root / canonical_path(row["path"])
        for parent in (target, *target.parents):
            if parent == root.parent:
                break
            if parent.is_symlink() or (hasattr(parent, "is_junction") and parent.is_junction()):
                raise ValueError("symlink in candidate source path")
        data = target.read_bytes()
        if len(data) != row["bytes"] or sha256(data) != row["sha256"]:
            raise ValueError("materialized source changed: " + row["path"])


def materialize(root, policy, files):
    head = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
    if head != policy["acceptedBase"]:
        raise ValueError("checkout base mismatch")
    if subprocess.check_output(["git", "-C", str(root), "status", "--porcelain", "--untracked-files=all"]):
        raise ValueError("candidate checkout must be clean before materialization")
    # Check every destination before writing any source bytes.
    for path in files:
        target = root / path
        for parent in (target, *target.parents):
            if parent == root.parent:
                break
            if parent.is_symlink() or (hasattr(parent, "is_junction") and parent.is_junction()):
                raise ValueError("symlink in destination")
        if target.exists() and not target.is_file():
            raise ValueError("destination is not a file")
    for path, data in files.items():
        target = root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        if target.exists(): target.unlink()
        shared.write_new(root, path, data)
    verify_source(root, policy)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--policy", type=Path, required=True)
    parser.add_argument("--candidate", type=Path, required=True)
    parser.add_argument("--manifest-blob")
    parser.add_argument("--capsule-blob")
    parser.add_argument("--verify-only", action="store_true")
    parser.add_argument("--receipt", type=Path)
    parser.add_argument("--dist-postimage", type=Path)
    args = parser.parse_args()
    policy = load_policy(args.policy)
    if sha256(Path(shared.__file__).read_bytes()) != policy["sharedExtractorSha256"]:
        raise ValueError("Shared File extractor source changed")
    root = args.candidate.resolve(strict=True)
    transport_commit = os.environ.get("GITHUB_SHA")
    if transport_commit:
        actual = os.environ.get("WEB_REVIEWED_TRANSPORT_SHA", "")
        if not re.fullmatch(r"[0-9a-f]{40}", transport_commit) or actual != transport_commit:
            raise ValueError("transport checkout differs from workflow commit")
    if args.verify_only:
        verify_source(root, policy, parse_json(args.dist_postimage.read_bytes()) if args.dist_postimage else None)
        print("Reviewed raw candidate source remains unchanged (1983 files).")
        return
    if args.receipt is None or args.receipt.exists():
        raise ValueError("fresh evidence receipt path required")
    manifest_bytes = fetch_blob(args.manifest_blob or "")
    capsule_bytes = fetch_blob(args.capsule_blob or "")
    _, files = validate_capsule(manifest_bytes, capsule_bytes, policy)
    materialize(root, policy, files)
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps({"manifestSha256": policy["manifestSha256"], "manifestBlob": args.manifest_blob,
                                      "capsuleBlob": args.capsule_blob, "acceptedBase": policy["acceptedBase"],
                                      "sourcePins": policy["sourcePins"], "sourceFiles": policy["sourceFiles"],
                                      "transportCommit": transport_commit, "nativeValidated": False}, indent=2) + "\n")
    print("Reviewed raw candidate materialized (1983 files); native validation pending.")


if __name__ == "__main__":
    main()
