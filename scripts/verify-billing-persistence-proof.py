"""Fail closed on missing/duplicate billing rows, receipts or unrelated candidate HEAD."""
import json
import os
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

candidate = os.environ.get("MALIEV_BILLING_CANDIDATE_HEAD", "")
if not re.fullmatch(r"[0-9a-f]{40}", candidate):
    raise SystemExit("Exact billing candidate SHA required")
head = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
parents = subprocess.check_output(["git", "show", "-s", "--format=%P", "HEAD"], text=True).split()
if head != candidate and (len(parents) != 2 or parents[1] != candidate):
    raise SystemExit("Billing checkout must be candidate or its PR merge second parent")
if subprocess.run(["git", "diff", "--quiet", "HEAD", "--"]).returncode != 0:
    raise SystemExit("Billing proof requires unchanged tracked candidate source and generated assets")
files = list(Path("billing-persistence-test-results").glob("*.trx"))
if len(files) != 1:
    raise SystemExit("Exactly one focused billing TRX required")
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
root = ET.parse(files[0]).getroot()
rows = root.findall(".//t:UnitTestResult", ns)
if len(rows) != 2 or any(row.attrib.get("outcome") != "Passed" for row in rows):
    raise SystemExit("Exactly two passed billing rows required; skipped rows are not acceptance")
for culture, width in (("en", 1280), ("th", 375)):
    matching = [row for row in rows if "ThaiLookupBillingPersistenceTests.BillingTuple_NormalSave_ApiReadbackAndReloadPreserveManualFields" in row.attrib.get("testName", "")
                and f'culture: "{culture}"' in row.attrib["testName"] and f"width: {width}" in row.attrib["testName"]]
    if len(matching) != 1:
        raise SystemExit("Missing or duplicate culture/viewport billing case")
directory = Path("Legacy.Maliev.Web.Tests/bin/Release/net10.0/TestResults/billing-persistence")
if sorted(path.name for path in directory.glob("*.json")) != ["en.json", "th.json"]:
    raise SystemExit("Exactly English and Thai billing receipts required")
for culture, width in (("en", 1280), ("th", 375)):
    receipt = json.loads((directory / f"{culture}.json").read_text())
    if any(receipt.get(key) != value for key, value in {
        "surface": "member-billing", "culture": culture, "width": width, "candidateHead": candidate,
        "catalogStatus": 200, "saveStatus": 302, "readbackStatus": 200,
        "reloadVerified": True, "shippingPreserved": True, "manualDetailPreserved": True,
    }.items()):
        raise SystemExit("Incomplete or foreign billing receipt")
    if receipt.get("datasetVersion") != "thailand-geography-json:b8b3fb91c7df1129ff5b43cb46f7fcffadd2156b":
        raise SystemExit("Billing receipt must use licensed real Catalog geography")
    if not (directory / f"{culture}.png").is_file():
        raise SystemExit("Missing synthetic reload screenshot")
print("Verified two real member billing cases with exact candidate lineage and reload receipts")
