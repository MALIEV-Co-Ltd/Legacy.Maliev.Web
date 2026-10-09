"""Materialize only the reviewed raw source capsule; never commit candidate C#."""

import argparse
import json
import os
from pathlib import Path
import re
import subprocess

REPOSITORY = "MALIEV-Co-Ltd/Legacy.Maliev.Web"
COUNTRY_POLICY_SHA256 = "c920c1a0aef5782020f0c2621d59dfed500804d0ed4ba38c79d529bfeb251c2d"
ACCOUNT_POLICY_SHA256 = "411c09afea2ca4181c15c2c5ba88239ac9f39c2ce7db1537a1cbf17ab4432f3a"
ACCOUNT_SOURCE_COUNT = 1963
EMAIL_SESSION_POLICY_SHA256 = "14b6bde2b3a1f17746c141e2d739d749dccf9efb9f43ea905f22b5e13d4d9d6b"
EMAIL_SESSION_BASE = "66d220af825a6d4829d1b97512e21b6486a08c87"
EMAIL_SESSION_PATHS = {
    "Legacy.Maliev.Web/Pages/Account/ChangeEmailConfirmation.cshtml.cs",
    "Legacy.Maliev.Web/Components/Pages/Account/ChangeEmailConfirmationPage.razor",
    "Legacy.Maliev.Web.Tests/EmailChangeConfirmationSessionHttpTests.cs",
    "Legacy.Maliev.Web.Tests/EmailChangeConfirmationRealSessionTests.cs",
}
TAX_POLICY_SHA256 = '4248baabc2e2571f620f85edddb82251e8aceb73aa65e2645e421ff7746b37f0'
EXPECTED_POLICY_SHA256 = "100ae2ae0a4a8c25cbf47ae3f4a0e0039e93f211d091ec48b192852e176ba43b"


import sealed_source_capsule as shared

sha256 = shared.digest
parse_json = shared.parse_json
canonical_path = shared.canonical_path
decode_blob = shared.decode_git_blob
NoRedirect = shared.NoRedirect

def load_policy(path):
    raw = Path(path).read_bytes()
    if sha256(raw) not in {EXPECTED_POLICY_SHA256, COUNTRY_POLICY_SHA256, ACCOUNT_POLICY_SHA256, EMAIL_SESSION_POLICY_SHA256, TAX_POLICY_SHA256}:
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
    if policy.get("sliceKind") == "optional-tax-build-v1":
        paths = {"Legacy.Maliev.Web/Pages/InstantQuotation/3D-Printing.cshtml.cs", "Legacy.Maliev.Web.Tests/InstantQuotationSubmissionEndpointTests.OptionalTax.cs"}
        if (policy["acceptedBase"] != "2f8bed1c4e8315ecd3e0afdfa3f1b960f0e7ffc8"
            or len(manifest["sourceFiles"]) != 2002 or len(manifest["capsuleRows"]) != 2
            or {row["path"] for row in manifest["capsuleRows"]} != paths
            or policy.get("reviewedSourceManifestSha256") != "2a49d01763a049cd6a58dc237d7b18fc062eb07587d16297527d4ba62695eb30"
            or policy.get("sourceReviewSha256") != "75aac863454cc9f63e4a6896b05169bfbdc9459988f62e6be59a194c5c4d1c77"
            or policy["sourcePins"] != {"ServiceDefaults": "3c790ba6414b2a539f24aabb6948549ffd81a86b", "CompatibilityContracts": "78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7"}
            or sha256(json.dumps(manifest["sourceFiles"], sort_keys=True, separators=(",", ":")).encode()) != policy["sourceBindingSha256"]):
            raise ValueError("Exact reviewed tax two-post current source binding required")
        count = 2002
    elif policy.get("sliceKind") == "email-change-session-v1":
        if (policy.get("sourceCount") != 1985 or policy.get("baseSourceCount") != 1983
            or policy.get("acceptedBase") != EMAIL_SESSION_BASE
            or policy.get("sourcePins") != {"ServiceDefaults": "3c790ba6414b2a539f24aabb6948549ffd81a86b",
                                            "CompatibilityContracts": "78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7"}
            or len(manifest["capsuleRows"]) != 4
            or {row["path"] for row in manifest["capsuleRows"]} != EMAIL_SESSION_PATHS):
            raise ValueError("Exact four-path email session source/base/pins required")
        binding = sha256(json.dumps(manifest["sourceFiles"], sort_keys=True, separators=(",", ":")).encode())
        if binding != policy["sourceBindingSha256"]:
            raise ValueError("Email session inventory binding changed")
        count = 1985
    elif policy.get("sliceKind") == "account-failure-v1":
        if policy.get("sourceCount") != ACCOUNT_SOURCE_COUNT:
            raise ValueError("Exact account inventory count required")
        count = ACCOUNT_SOURCE_COUNT
    else:
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


def source_status(policy, materialized=False):
    count = len(policy["sourceFiles"]) if policy.get("sliceKind") in {"account-failure-v1", "email-change-session-v1"} else 1983
    if materialized:
        return f"Reviewed raw candidate materialized ({count} files); native validation pending."
    return f"Reviewed raw candidate source remains unchanged ({count} files)."


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
        print(source_status(policy))
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
    print(source_status(policy, materialized=True))


if __name__ == "__main__":
    main()
