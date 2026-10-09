"""Bounded comparison provenance; never substitutes for strict billing/cleanup gates."""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import uuid
import xml.etree.ElementTree as ET

SETTINGS = "tests/web-fx-generated-inclusive.runsettings"
SETTINGS_SHA256 = "73f436bbdca7731a39d1d87f48f4b82ef0386d81f20bfa004f3c240c0290ed5c"
CLASS = "Legacy.Maliev.Web.Tests.ThaiLookupBillingPersistenceTests"
METHOD = "BillingTuple_NormalSave_ApiReadbackAndReloadPreserveManualFields"
CASES = {f'{CLASS}.{METHOD}(culture: "{culture}", width: {width})' for culture, width in (("en", 1280), ("th", 375))}
COUNTERS = set("total executed passed failed error timeout aborted inconclusive passedButRunAborted notRunnable notExecuted disconnected warning completed inProgress pending".split())
NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
OUTCOMES = {"Passed", "Failed", "NotExecuted", "Aborted", "Timeout", "Error", "Inconclusive", "NotRunnable"}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def native(path):
    raw = path.read_bytes()
    if len(raw) > 8_000_000 or b"<!DOCTYPE" in raw.upper() or b"<!ENTITY" in raw.upper() or b"\0" in raw:
        raise ValueError("UnsafeTrx")
    root = ET.fromstring(raw)
    if root.tag != NS + "TestRun":
        raise ValueError("TrxNamespace")
    def one(parent, name):
        found = parent.findall(NS + name)
        if len(found) != 1:
            raise ValueError("TrxShape")
        return found[0]
    summary = one(root, "ResultSummary")
    counts = one(summary, "Counters").attrib
    if set(counts) != COUNTERS or any(not re.fullmatch(r"0|[1-9][0-9]*", value) or int(value) > 2 for value in counts.values()):
        raise ValueError("TrxCounters")
    groups = [list(one(root, name)) for name in ("Results", "TestDefinitions", "TestEntries")]
    results, definitions, entries = groups
    if any(len(group) != 2 for group in groups) or {r.get("testName") for r in results} != CASES:
        raise ValueError("TrxRoster")
    if any(r.tag != NS + "UnitTestResult" or r.get("outcome") not in OUTCOMES for r in results) or any(r.tag != NS + "TestEntry" for r in entries):
        raise ValueError("TrxOutcome")
    def ids(group, key):
        values = [row.get(key, "") for row in group]
        if len(set(values)) != 2 or any(str(uuid.UUID(value)) != value for value in values):
            raise ValueError("TrxIdentity")
        return set(values)
    if ids(results, "testId") != ids(definitions, "id") or ids(entries, "testId") != ids(results, "testId"):
        raise ValueError("TrxIdentity")
    if ids(results, "executionId") != ids(entries, "executionId") or {(r.get("testId"), r.get("executionId")) for r in results} != {(r.get("testId"), r.get("executionId")) for r in entries}:
        raise ValueError("TrxIdentity")
    by_id = {row.get("id"): row for row in definitions}
    for row in results:
        definition = by_id[row.get("testId")]
        method = one(definition, "TestMethod")
        if definition.tag != NS + "UnitTest" or definition.get("name") != row.get("testName") or method.get("className") != CLASS or method.get("name") != METHOD or one(definition, "Execution").get("id") != row.get("executionId"):
            raise ValueError("TrxDefinition")
    counter_for = {"Passed": "passed", "Failed": "failed", "NotExecuted": "notExecuted", "Aborted": "aborted", "Timeout": "timeout", "Error": "error", "Inconclusive": "inconclusive", "NotRunnable": "notRunnable"}
    actual_counts = {key: 0 for key in COUNTERS}
    actual_counts["total"] = 2
    actual_counts["executed"] = sum(row.get("outcome") not in {"NotExecuted", "NotRunnable"} for row in results)
    for row in results:
        actual_counts[counter_for[row.get("outcome")]] += 1
    if counts != {key: str(value) for key, value in actual_counts.items()}:
        raise ValueError("TrxCounterConcordance")
    expected_summary = "Completed" if actual_counts["passed"] + actual_counts["notExecuted"] == 2 else "Failed"
    if summary.get("outcome") != expected_summary:
        raise ValueError("TrxSummaryConcordance")
    expected = {key: "2" if key in {"total", "executed", "passed"} else "0" for key in COUNTERS}
    passed = all(row.get("outcome") == "Passed" for row in results)
    collectors = root.findall(".//" + NS + "CollectorDataEntries/" + NS + "Collector")
    if any(row.get("uri") != "datacollector://microsoft/CoverletCodeCoverage/1.0" or row.get("collectorDisplayName") != "XPlat code coverage" for row in collectors) or len(collectors) > 1:
        raise ValueError("TrxCollectorIdentity")
    return {"sha256": hashlib.sha256(raw).hexdigest(), "counters": counts,
            "collectorRecorded": len(collectors) == 1,
            "cases": [{"name": row.get("testName"), "outcome": row.get("outcome")} for row in results],
            "twoCasesPassed": passed and counts == expected and summary.get("outcome") == "Completed"}


def coverage_from_original(trx, results, mode):
    """Join original collector attachments; permit only their one byte-identical VSTest mirror."""
    formats = ("coverage.json", "coverage.cobertura.xml")
    if results.is_symlink() or any(parent.is_symlink() for parent in results.parents) or not results.is_dir():
        raise ValueError("CoverageRoot")
    base = results.resolve(strict=True)
    inventory = {name: [] for name in formats}
    nodes = 0
    for parent, directories, files in os.walk(results, followlinks=False):
        for name in directories + files:
            path = Path(parent) / name
            nodes += 1
            if nodes > 256 or path.is_symlink() or not path.resolve(strict=True).is_relative_to(base):
                raise ValueError("CoverageInventory")
            if name in formats:
                if not stat.S_ISREG(path.lstat().st_mode) or not 0 < path.stat().st_size <= 32 * 1024 * 1024:
                    raise ValueError("CoverageFile")
                inventory[name].append(path)
    if mode == "focused":
        if any(inventory.values()):
            raise ValueError("CoverageUnexpected")
        return {name: [] for name in formats}, {"collectorAttachmentVerified": False, "mirrorVerified": False, "mirrorCount": 0}
    if mode != "collected" or trx.parent != results or trx.is_symlink():
        raise ValueError("CoverageMode")
    raw = trx.read_bytes()
    if len(raw) > 8_000_000 or b"<!DOCTYPE" in raw.upper() or b"<!ENTITY" in raw.upper() or b"\0" in raw:
        raise ValueError("CoverageTrx")
    root = ET.fromstring(raw)
    deployments = root.findall(NS + "TestSettings/" + NS + "Deployment")
    collectors = root.findall(NS + "ResultSummary/" + NS + "CollectorDataEntries/" + NS + "Collector")
    if root.tag != NS + "TestRun" or len(deployments) != 1 or len(collectors) != 1:
        raise ValueError("CoverageAttachmentShape")
    deployment = deployments[0].get("runDeploymentRoot", "")
    collector = collectors[0]
    agent = collector.get("agentName", "")
    if any(not re.fullmatch(r"[A-Za-z0-9_][A-Za-z0-9_.-]{0,119}", value) or value in {".", ".."} for value in (deployment, agent)):
        raise ValueError("CoverageAttachmentComponent")
    if trx.name != deployment + "_net10.0.trx" or collector.get("uri") != "datacollector://microsoft/CoverletCodeCoverage/1.0" or collector.get("collectorDisplayName") != "XPlat code coverage":
        raise ValueError("CoverageAttachmentIdentity")
    attachments = collector.findall(NS + "UriAttachments/" + NS + "UriAttachment")
    hrefs = []
    for attachment in attachments:
        links = list(attachment)
        if len(links) != 1 or links[0].tag != NS + "A" or set(links[0].attrib) != {"href"}:
            raise ValueError("CoverageAttachmentLink")
        hrefs.append(links[0].get("href"))
    if len(hrefs) != 2 or set(hrefs) != {agent + "/" + name for name in formats}:
        raise ValueError("CoverageAttachmentRoster")
    original = {name: results / deployment / "In" / agent / name for name in formats}
    mirrors = []
    projected = {}
    for name in formats:
        if original[name] not in inventory[name] or len(inventory[name]) not in {1, 2}:
            raise ValueError("CoverageOriginalMissingOrSurplus")
        canonical = original[name].read_bytes()
        if not 0 < len(canonical) <= 32 * 1024 * 1024:
            raise ValueError("CoverageOriginalBytes")
        copied = [path for path in inventory[name] if path != original[name]]
        if copied:
            mirror = copied[0]
            if mirror.parent.parent != results or not re.fullmatch(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", mirror.parent.name) or str(uuid.UUID(mirror.parent.name)) != mirror.parent.name:
                raise ValueError("CoverageMirrorGeometry")
            if mirror.read_bytes() != canonical:
                raise ValueError("CoverageMirrorBytes")
            mirrors.append(mirror.parent)
        projected[name] = [{"sha256": hashlib.sha256(canonical).hexdigest(), "bytes": len(canonical)}]
    if len(mirrors) not in {0, 2} or mirrors and mirrors[0] != mirrors[1]:
        raise ValueError("CoverageMirrorCohort")
    return projected, {"collectorAttachmentVerified": True, "mirrorVerified": bool(mirrors), "mirrorCount": 1 if mirrors else 0}


def main():
    root = Path(__file__).resolve().parents[1]
    destination = root / "billing-comparison-results"
    destination.mkdir(exist_ok=True)
    mode = os.environ.get("BILLING_COMPARISON_MODE")
    if mode not in {"focused", "collected"}:
        raise SystemExit("Exact comparison mode required")
    report = {"schema": 1, "mode": mode, "configurationQualified": False, "native": None,
              "unavailable": [], "fullCoverageAcceptance": False,
              "limits": ["Two fresh-process billing cases only; not full process history or full coverage floors.",
                         "Strict original billing receipts, watchdog and lifecycle controls remain separate required gates."]}
    try:
        candidate = os.environ.get("MALIEV_BILLING_CANDIDATE_HEAD", "")
        executed = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
        if not re.fullmatch(r"[0-9a-f]{40}", candidate) or executed != candidate:
            raise ValueError("CandidateMismatch")
        for key in ("GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT"):
            if not re.fullmatch(r"[1-9][0-9]*", os.environ.get(key, "")):
                raise ValueError("RunCustody")
        report.update(candidateHead=candidate, executedSource=executed, runId=os.environ["GITHUB_RUN_ID"], runAttempt=os.environ["GITHUB_RUN_ATTEMPT"])
        settings = root / SETTINGS
        if hashlib.sha256(settings.read_bytes().replace(b"\r\n", b"\n")).hexdigest() != SETTINGS_SHA256:
            raise ValueError("CollectorSettingsChanged")
        project = root / "Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj"
        package = [row for row in ET.parse(project).findall(".//PackageReference") if row.get("Include") == "coverlet.collector"]
        if len(package) != 1 or package[0].get("Version") != "6.0.4":
            raise ValueError("CollectorVersionChanged")
        evaluated = json.loads((destination / "evaluated-properties.private.json").read_text(encoding="utf-8"))["Properties"]
        expected = {"CI": "false", "VSTestCollect": "", "RunSettingsFilePath": ""} if mode == "focused" else {"CI": "true", "VSTestCollect": "XPlat Code Coverage", "RunSettingsFilePath": str(settings)}
        if evaluated != expected:
            raise ValueError("EvaluatedCollectorMismatch")
        report.update(configurationQualified=True, collector="none" if mode == "focused" else "XPlat Code Coverage",
                      collectorVersion="6.0.4", settingsSource=SETTINGS, settingsCanonicalSha256=SETTINGS_SHA256, projectSha256=sha(project))
    except (OSError, ValueError, KeyError, TypeError, ET.ParseError, subprocess.CalledProcessError):
        report["unavailable"].append("ConfigurationUnavailable")
    try:
        results = root / "billing-persistence-test-results"
        files = list(results.glob("*.trx"))
        if len(files) != 1:
            raise ValueError("TrxUnavailable")
        report["native"] = native(files[0])
        if report["native"]["collectorRecorded"] != (mode == "collected"):
            raise ValueError("CollectorActivationMismatch")
        report["coverage"], report["coverageCustody"] = coverage_from_original(files[0], results, mode)
    except (OSError, ValueError, KeyError, TypeError, ET.ParseError):
        report["unavailable"].append("OriginalResultUnavailable")
    (destination / "comparison.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    if not report["configurationQualified"] or report["unavailable"]:
        raise SystemExit("Comparison provenance incomplete; original artifacts retained")


if __name__ == "__main__":
    main()
