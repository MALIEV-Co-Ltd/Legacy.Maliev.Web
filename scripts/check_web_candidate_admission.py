"""Fail closed until an independent Root grant and qualified CRM producer are policy-pinned."""
import argparse
import datetime as dt
import os
from pathlib import Path
import re
import materialize_web_candidate as intake

OWNER = "01a1009c-7d2d-7fc3-a239-2b1d9600a7a5"

def validate(policy, raw, now=None):
    digest = policy.get("nativeAdmissionSha256")
    producer = policy.get("customerLiteralProducerSha")
    if not isinstance(digest, str) or not re.fullmatch(r"[0-9a-f]{64}", digest):
        raise ValueError("No independently reviewed Root hosted permit is pinned")
    if not isinstance(producer, str) or not re.fullmatch(r"[0-9a-f]{40}", producer):
        raise ValueError("Qualified Customer literal producer is not pinned")
    if intake.sha256(raw) != digest:
        raise ValueError("Hosted permit hash differs from reviewed policy")
    grant = intake.parse_json(raw)
    now = now or dt.datetime.now(dt.timezone.utc)
    start = dt.datetime.fromisoformat(grant["startsUtc"].replace("Z", "+00:00"))
    end = dt.datetime.fromisoformat(grant["expiresUtc"].replace("Z", "+00:00"))
    if not start <= now < end or (end - start).total_seconds() > 5400:
        raise ValueError("Finite Root hosted permit expired or excessive")
    exact = {"owner": OWNER, "environment": "github-hosted-linux", "sourceBindingSha256": policy["sourceBindingSha256"],
             "manifestSha256": policy["manifestSha256"], "acceptedBase": policy["acceptedBase"],
             "customerLiteralProducerSha": producer, "allowedPhases": ["assets", "build", "focused", "suite", "coverage", "static", "native"]}
    if any(grant.get(k) != v for k, v in exact.items()):
        raise ValueError("Hosted permit scope differs from reviewed policy")
    if os.name != "posix": raise ValueError("Actual Linux runner is required")
    return grant

def census(proc_root=Path("/proc")):
    values = dict(re.findall(r"^(MemAvailable):\s+(\d+)", (proc_root / "meminfo").read_text(), re.M))
    if int(values.get("MemAvailable", "0")) < 4194304: raise ValueError("4096 MiB memory guard failed")
    names = {"dotnet", "testhost", "MSBuild", "VBCSCompiler", "datacollector", "esbuild"}
    for entry in proc_root.iterdir():
        if not entry.name.isdecimal(): continue
        try: name = (entry / "comm").read_text().strip()
        except FileNotFoundError: continue
        if name in names: raise ValueError("Existing SDK or asset worker blocks hosted admission")

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--policy", required=True, type=Path)
    parser.add_argument("--blob")
    parser.add_argument("--permit", type=Path, required=True)
    args = parser.parse_args()
    policy = intake.load_policy(args.policy)
    # Missing prerequisites reject before fetching or writing any permit.
    if not policy.get("nativeAdmissionSha256") or not policy.get("customerLiteralProducerSha"):
        raise ValueError("Root hosted permit and qualified Customer producer remain unpinned")
    census()
    raw = intake.fetch_blob(args.blob) if args.blob else args.permit.read_bytes()
    validate(policy, raw); census()
    if args.blob:
        if args.permit.exists(): raise ValueError("Fresh owned permit path required")
        args.permit.write_bytes(raw)
    print("Exact independently reviewed Root hosted permit and resource census verified")

if __name__ == "__main__": main()
