"""Adapt only the qualified Customer Git pin without changing frozen Web source bytes."""
import argparse
from pathlib import Path
import subprocess
import tempfile
import materialize_web_candidate as intake

def prepare_bytes(policy, raw, root):
    if intake.sha256(raw) != policy["profilePreparationSha256"]: raise ValueError("Profile preparation source changed")
    pin = policy.get("customerLiteralProducerSha")
    if not pin or len(pin) != 40 or any(c not in "0123456789abcdef" for c in pin): raise ValueError("Qualified Customer producer missing")
    text = raw.decode("utf-8-sig")
    old = policy["originalCustomerProducerPin"]
    if text.count(old) != 1: raise ValueError("Original producer pin is ambiguous")
    marker = "$root = Split-Path $PSScriptRoot -Parent"
    if text.count(marker) != 1: raise ValueError("Preparation root declaration changed")
    path = str(root).replace("'", "''")
    text = text.replace(marker, "$root = '" + path + "'").replace(old, pin)
    return text.encode("utf-8")

def main():
    parser = argparse.ArgumentParser(); parser.add_argument("--policy",type=Path,required=True); parser.add_argument("--candidate",type=Path,required=True)
    args=parser.parse_args();policy=intake.load_policy(args.policy);root=args.candidate.resolve(strict=True)
    raw=(root/"scripts/prepare-profile-producer-boundary.ps1").read_bytes()
    adapted=prepare_bytes(policy,raw,root)
    with tempfile.TemporaryDirectory(prefix="web-profile-pin-") as directory:
        script=Path(directory)/"prepare-qualified-profile.ps1";script.write_bytes(adapted)
        subprocess.run(["pwsh","-NoProfile","-File",str(script)],check=True)
    actual=subprocess.check_output(["git","-C",str(root/".dependencies/profile-producer"),"rev-parse","HEAD"],text=True).strip()
    if actual!=policy["customerLiteralProducerSha"]:raise ValueError("Actual producer checkout differs from qualified pin")
    print("Actual Customer checkout verified at independently qualified literal producer pin")

if __name__=="__main__":main()
