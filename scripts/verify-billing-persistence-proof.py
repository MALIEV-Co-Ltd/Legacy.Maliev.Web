"""Fail closed on missing/duplicate billing rows, receipts or unrelated candidate HEAD."""
import json
import hashlib
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
case_runs = {}
for culture, width in (("en", 1280), ("th", 375)):
    matching = [row for row in rows if "ThaiLookupBillingPersistenceTests.BillingTuple_NormalSave_ApiReadbackAndReloadPreserveManualFields" in row.attrib.get("testName", "")
                and f'culture: "{culture}"' in row.attrib["testName"] and f"width: {width}" in row.attrib["testName"]]
    if len(matching) != 1:
        raise SystemExit("Missing or duplicate culture/viewport billing case")
    markers = [line for line in matching[0].findtext("t:Output/t:StdOut", default="", namespaces=ns).splitlines()
               if line.startswith("billing-attempt-run ")]
    if len(markers) != 1 or not re.fullmatch(r"billing-attempt-run [0-9a-f]{32}", markers[0]):
        raise SystemExit("Exactly one owned attempt marker required in each billing case")
    case_runs[culture] = markers[0].split(" ")[1]
if len(set(case_runs.values())) != 2:
    raise SystemExit("Billing case attempts must be distinct")
directory = Path("Legacy.Maliev.Web.Tests/bin/Release/net10.0/TestResults/billing-persistence")
if sorted(path.name for path in directory.glob("*.json")) != ["en.json", "th.json"]:
    raise SystemExit("Exactly English and Thai billing receipts required")
for culture, width in (("en", 1280), ("th", 375)):
    alias = directory / f"{culture}.json"
    if alias.is_symlink() or not alias.is_file() or alias.stat().st_size > 16384:
        raise SystemExit("Bounded original billing receipt required")
    alias_bytes = alias.read_bytes()
    receipt = json.loads(alias_bytes)
    attempt = case_runs[culture]
    if receipt.get("businessComplete") is not True or receipt.get("attemptRun") != attempt:
        raise SystemExit("Billing business receipt must join this exact TRX case attempt")
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
    cleanup = receipt.get("billingBackendCleanup", {})
    if not isinstance(cleanup, dict) or any(cleanup.get(key) != value for key, value in {
        "schema": 1, "surface": "member-billing", "culture": culture, "candidateHead": candidate,
        "owner": "web-billing-proof", "run": attempt,
    }.items()):
        raise SystemExit("Billing cleanup must join the same case attempt")
    if any(cleanup.get(key) is not True for key in ("complete", "graphReleased", "expiryJoined", "sharedAuthorityExcluded")) \
            or any(cleanup.get(key) is not False for key in ("expired", "readerFailureRejected")):
        raise SystemExit("Incomplete billing attempt cleanup")
    resources = cleanup.get("resources")
    if not isinstance(resources, list) or len(resources) != 1 or not isinstance(resources[0], dict):
        raise SystemExit("Exactly one owned billing backend required")
    resource = resources[0]
    original = resource.get("original", {})
    if not isinstance(original, dict) or resource.get("backend") != "postgres" or resource.get("databaseSynthetic") is not True \
            or resource.get("owner") != "web-billing-proof" or original.get("owner") != resource.get("owner") \
            or resource.get("run") != attempt or original.get("run") != attempt \
            or not re.fullmatch(r"[0-9a-f]{64}", resource.get("originalId", "")) \
            or resource.get("originalId") != original.get("id") \
            or not resource.get("expiresUtc") or resource.get("expiresUtc") != original.get("expiresUtc") \
            or original.get("name") != "/" + resource.get("name", ""):
        raise SystemExit("Billing backend original identity must join the case attempt")
    if any(resource.get(key) is not True for key in ("baselineAbsent", "startDispatched", "startupSettled", "captureVerified", "absenceVerified", "sdkReleased")) \
            or any(original.get(key) is not True for key in ("envelopeValid", "initEnabled", "loopbackPorts")) \
            or original.get("persistentData") is not False:
        raise SystemExit("Billing backend closure or creation envelope incomplete")
    archive_directory = directory / "attempts"
    archive = archive_directory / f"{culture}-{attempt}.json"
    if archive_directory.is_symlink() or archive.is_symlink() or not archive.is_file() \
            or archive.stat().st_size > 16384 or archive.read_bytes() != alias_bytes:
        raise SystemExit("Current billing alias must match its immutable attempt archive")
    image = receipt.get("attemptScreenshot", {})
    if not isinstance(image, dict) or image.get("file") != f"{culture}-{attempt}.png" \
            or type(image.get("bytes")) is not int or not 0 < image["bytes"] <= 16 * 1024 * 1024 \
            or not isinstance(image.get("sha256"), str) or not re.fullmatch(r"[0-9a-f]{64}", image["sha256"]):
        raise SystemExit("Bounded same-attempt screenshot binding required")
    for screenshot in (directory / f"{culture}.png", archive_directory / image["file"]):
        if screenshot.is_symlink() or not screenshot.is_file() or screenshot.stat().st_size != image["bytes"] \
                or hashlib.sha256(screenshot.read_bytes()).hexdigest() != image["sha256"]:
            raise SystemExit("Original and archived billing screenshots must match this attempt")
print("Verified two real member billing cases with exact candidate lineage and reload receipts")
