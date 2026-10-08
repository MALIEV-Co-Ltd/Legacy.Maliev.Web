"""Account-only adapter. Caller must already inhabit the genuine externally owned SDK unit."""
import argparse
import os
from pathlib import Path
import re
import sys
import check_web_candidate_admission as admission
import materialize_web_candidate as intake
import run_web_candidate_phase as phase


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--policy", type=Path, required=True)
    parser.add_argument("--candidate", type=Path, required=True)
    parser.add_argument("--permit", type=Path, required=True)
    parser.add_argument("--sdk-owner-context", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--phase-id", required=True)
    args = parser.parse_args()
    policy = intake.load_policy(args.policy)
    if policy.get("sliceKind") != "account-failure-v1":
        raise ValueError("Distinct account policy required")
    if not policy.get("nativeAdmissionSha256") or not policy.get("sdkOwnerContextSha256"):
        raise ValueError("Actual independently pinned permit/SDK owner absent; no SDK launch")
    actual = os.environ.get("WEB_REVIEWED_TRANSPORT_SHA", "")
    if not re.fullmatch(r"[0-9a-f]{40}", actual) or actual != os.environ.get("GITHUB_SHA"):
        raise ValueError("Exact reviewed workflow transport required")
    admission.census()
    grant = admission.validate(policy, args.permit.read_bytes())
    phase.verify_capped_owner(policy, args.sdk_owner_context)
    if intake.sha256(Path(intake.shared.__file__).read_bytes()) != policy["sharedExtractorSha256"]:
        raise ValueError("Shared extractor raw bytes changed")
    rows = [row for row in grant["phases"] if row["id"] == args.phase_id]
    if len(rows) != 1: raise ValueError("Exact unique account phase required")
    row = rows[0]
    root = args.candidate.resolve(strict=True)
    intake.verify_source(root, policy)
    resolved = {key: str(getattr(args, key).resolve()) for key in ("policy", "permit", "evidence", "sdk_owner_context")}
    previous_argv, previous_directory = sys.argv, Path.cwd()
    try:
        os.chdir(root)
        sys.argv = ["run_web_candidate_phase", "--policy", resolved["policy"],
                    "--permit", resolved["permit"], "--evidence", resolved["evidence"],
                    "--sdk-owner-context", resolved["sdk_owner_context"],
                    "--phase", row["name"], "--id", row["id"], "--", *row["argv"]]
        phase.main()  # Existing capped-owner verifier, floor/exclusion, finite lease, pidfd and finally cleanup.
    finally:
        sys.argv = previous_argv
        os.chdir(previous_directory)
    intake.verify_source(root, policy)

if __name__ == "__main__": main()
