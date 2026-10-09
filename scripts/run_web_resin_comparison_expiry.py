"""Closed paired real-expiry experiment; reuse the existing hosted kernel/phase owner."""
import argparse
import datetime as dt
import json
import os
from pathlib import Path
import re
import sys

import check_web_candidate_admission as admission
import materialize_web_candidate as intake
import run_web_candidate_phase as phase
from collections import Counter
import uuid
import xml.etree.ElementTree as ET

SCOPE = "resin-comparison-expiry-v1"
BASE = "80b69dd81a7dce23c3ac07ef507ac5ae16a92d9b"
CONTROL_SHA = "6261787b83b4e6b391146da3cd325298ac98ce84"
TEST_PATH = "Legacy.Maliev.Web.Tests/InstantQuotationMaterialPricingProgressTests.cs"
CLASS = "Legacy.Maliev.Web.Tests.InstantQuotationMaterialPricingProgressTests"
METHOD = "ResinComparisonBudget_BothOverloadsPreserveSelectedQuoteAfterRealExpiry"
ENROLLMENT = {"backend": "launch_web_capped_kernel_proof-v1", "memoryMaxBytes": 3221225472,
              "swapMaxBytes": 0, "cpuQuotaPercent": 100, "tasksMax": 512, "leaseSeconds": 2100}
PINS = {"ServiceDefaults": "3c790ba6414b2a539f24aabb6948549ffd81a86b",
        "CompatibilityContracts": "78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7"}
COUNTERS = {"total", "executed", "passed", "failed", "error", "timeout", "aborted", "inconclusive",
            "passedButRunAborted", "notRunnable", "notExecuted", "disconnected", "warning", "completed", "inProgress", "pending"}


def expected_phases():
    project = "Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj"
    return [
        {"id": "resin-sdk-install", "name": "static", "argv": ["bash", ".dependencies/dotnet-installer/externals/install-dotnet.sh", "--version", "10.0.401", "--install-dir", ".dependencies/resin-sdk", "--no-path"]},
        {"id": "resin-sdk-version", "name": "static", "argv": ["dotnet", "--version"]},
        {"id": "resin-restore", "name": "static", "argv": ["dotnet", "restore", project]},
        {"id": "resin-build", "name": "build", "argv": ["dotnet", "build", project, "-c", "Release", "--no-restore", "-warnaserror", "-m:1", "-nr:false", "-p:UseSharedCompilation=false"]},
        {"id": "resin-focused", "name": "focused", "argv": ["dotnet", "test", project, "-c", "Release", "--no-build", "--no-restore", "-p:CI=false", "-p:VSTestCollect=", "-p:RunSettingsFilePath=", "--filter", "FullyQualifiedName=" + CLASS + "." + METHOD, "--logger", "trx;LogFileName=paired-expiry.trx", "--results-directory", "resin-comparison-expiry-results"]},
    ]


def expected_cases():
    return {f"{CLASS}.{METHOD}(displayProgress: {flag})": (CLASS, METHOD) for flag in ("False", "True")}


# Strict raw TRX joins adapted from the existing account verifier; no account worker import or execution.
def verify_trx(path, assembly_sha256):
    if not isinstance(assembly_sha256, str) or not re.fullmatch(r"[0-9a-f]{64}", assembly_sha256):
        raise ValueError("Mandatory actual built assembly SHA256 required")
    root = ET.parse(path).getroot()
    ns = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
    def one(parent, name):
        rows = parent.findall(ns + name)
        if len(rows) != 1:
            raise ValueError("Exactly one TRX " + name + " required")
        return rows[0]
    if root.tag != ns + "TestRun":
        raise ValueError("Exact TRX namespace required")
    summary = one(root, "ResultSummary")
    counters = one(summary, "Counters")
    fields = {"total", "executed", "passed", "failed", "error", "timeout", "aborted", "inconclusive",
              "passedButRunAborted", "notRunnable", "notExecuted", "disconnected", "warning", "completed", "inProgress", "pending"}
    if set(counters.attrib) != fields or any(not re.fullmatch(r"0|[1-9][0-9]*", value) for value in counters.attrib.values()):
        raise ValueError("Exact canonical TRX counters required")
    counts = {key: int(value) for key, value in counters.attrib.items()}
    containers = [(one(root, name), ns + child) for name, child in
                  (("Results", "UnitTestResult"), ("TestDefinitions", "UnitTest"), ("TestEntries", "TestEntry"))]
    if any(any(row.tag != child for row in container) for container, child in containers):
        raise ValueError("Unknown extra TRX execution/definition/entry row")
    results, definitions, entries = [list(container) for container, _ in containers]
    if (summary.attrib.get("outcome") != "Completed" or not results
        or any(counts[key] != len(results) for key in ("total", "executed", "passed"))
        or any(counts[key] for key in fields - {"total", "executed", "passed"})
        or len(definitions) != len(results) or len(entries) != len(results)):
        raise ValueError("Summary/definitions/results/entries/counters must be complete and consistent")
    def unique_guids(rows, key):
        values = [row.attrib.get(key, "") for row in rows]
        if len(set(values)) != len(values) or any(str(uuid.UUID(value)) != value for value in values):
            raise ValueError("Unique canonical TRX " + key + " required")
        return values
    test_ids = unique_guids(definitions, "id")
    result_ids = unique_guids(results, "testId")
    executions = unique_guids(results, "executionId")
    if set(test_ids) != set(result_ids) or set(unique_guids(entries, "testId")) != set(test_ids):
        raise ValueError("Exact TRX definition/result/entry join required")
    entry_pairs = {(row.attrib["testId"], row.attrib["executionId"]) for row in entries}
    if entry_pairs != {(row.attrib["testId"], row.attrib["executionId"]) for row in results}:
        raise ValueError("TRX execution entry associations changed")
    actual_dll = (path.parent.parent / "Legacy.Maliev.Web.Tests/bin/Release/net10.0/Legacy.Maliev.Web.Tests.dll").resolve(strict=True)
    if assembly_sha256 is not None and intake.sha256(actual_dll.read_bytes()) != assembly_sha256:
        raise ValueError("Actual built Web test assembly changed after build")
    by_id = {row.attrib["id"]: row for row in definitions}
    expected = expected_cases()
    authored = []
    names = []
    target_classes = {value[0] for value in expected.values()}
    for result in results:
        definition = by_id[result.attrib["testId"]]
        method = one(definition, "TestMethod")
        execution = one(definition, "Execution")
        name = result.attrib.get("testName")
        cls, method_name = method.attrib.get("className"), method.attrib.get("name")
        if (result.attrib.get("outcome") != "Passed" or definition.attrib.get("name") != name
            or execution.attrib.get("id") != result.attrib["executionId"]
            or not isinstance(cls, str) or not cls.startswith("Legacy.Maliev.Web.Tests.")
            or Path(method.attrib.get("codeBase", "")).resolve() != actual_dll
            or Path(definition.attrib.get("storage", "")).resolve() != actual_dll):
            raise ValueError("Exact passing Web assembly/class/definition/execution identity required")
        names.append(name)
        if cls in target_classes or name in expected:
            if name not in expected or expected[name] != (cls, method_name):
                raise ValueError("Unknown or mismatched authored theory/data case")
            authored.append(name)
    if len(set(names)) != len(names):
        raise ValueError("Duplicate executed test display case")
    if Counter(authored) != Counter(expected.keys()):
        raise ValueError("Exact independent two authored theory/data cases required")
    if Counter(names) != Counter(expected.keys()):
        raise ValueError("Focused qualification admits exactly the authored two cases and no extras")
    return counts


def _validate_permit(policy, raw, now=None):
    if (policy.get("sliceKind") != SCOPE or policy.get("acceptedBase") != BASE
        or policy.get("controlCommit") != CONTROL_SHA or policy.get("sourcePins") != PINS
        or policy.get("sdkOwnerEnrollment") != ENROLLMENT or policy.get("sdkOwnerContextSha256") is not None
        or policy.get("phases") != expected_phases() or policy.get("allowedPhases") != ["static", "build", "focused"]):
        raise ValueError("Exact paired expiry source/commands/owner scope required")
    digest = policy.get("nativeAdmissionSha256")
    if not isinstance(digest, str) or not re.fullmatch(r"[0-9a-f]{64}", digest):
        raise ValueError("Independent paired expiry permit remains unpinned")
    if intake.sha256(raw) != digest:
        raise ValueError("Paired expiry permit raw bytes changed")
    grant = intake.parse_json(raw)
    expected = {key: policy[key] for key in ("acceptedBase", "sourcePins", "sourceBindingSha256", "manifestSha256", "sdkOwnerEnrollment", "sdkOwnerContextSha256", "phases", "allowedPhases", "controlCommit")}
    import resin_expiry_enrollment as enrollment
    expected.update(owner=admission.OWNER, issuedBy=enrollment.issuer(policy),
                    environment="github-hosted-linux", sliceKind=SCOPE,
                    transportSha=os.environ.get("WEB_REVIEWED_TRANSPORT_SHA"),
                    runId=os.environ.get("GITHUB_RUN_ID"), runAttempt=os.environ.get("GITHUB_RUN_ATTEMPT"))
    if (not re.fullmatch(r"[0-9a-f]{40}", expected["transportSha"] or "")
        or expected["transportSha"] != os.environ.get("GITHUB_SHA")
        or any(not re.fullmatch(r"[1-9][0-9]*", expected[key] or "") for key in ("runId", "runAttempt"))):
        raise ValueError("Actual protected transport/run association required")
    if (set(grant) != set(expected) | {"ownerNonce", "startsUtc", "expiresUtc"}
        or any(enrollment.canonical({key: grant[key]}) != enrollment.canonical({key: value}) for key, value in expected.items())
        or not re.fullmatch(r"[0-9a-f]{32}", grant.get("ownerNonce", ""))):
        raise ValueError("Paired expiry permit exact fields/scope changed")
    start = dt.datetime.fromisoformat(grant["startsUtc"].replace("Z", "+00:00"))
    end = dt.datetime.fromisoformat(grant["expiresUtc"].replace("Z", "+00:00"))
    now = now or dt.datetime.now(dt.timezone.utc)
    if (start.utcoffset() != dt.timedelta(0) or end.utcoffset() != dt.timedelta(0)
        or not start <= now < end or not 0 < (end - start).total_seconds() <= 2100 or os.name != "posix"):
        raise ValueError("Finite nonrenewed hosted Linux expiry permit required")
    return grant


def validate(policy, raw, now=None):
    import resin_expiry_enrollment as enrollment
    return enrollment.require_context(policy, raw, now)


def authorize_creation(policy_path, permit_path, candidate):
    policy = intake.load_policy(policy_path)
    import resin_expiry_enrollment as enrollment
    grant = enrollment.require_context(policy, permit_path.read_bytes(), initial=True)
    intake.verify_source(candidate, policy)
    admission.census()
    return policy, grant


def verify_final(evidence):
    policy = intake.load_policy(Path(__file__).with_name("web-resin-comparison-expiry-policy.json"))
    if (policy.get("sliceKind") != SCOPE or policy.get("acceptedBase") != BASE
        or policy.get("controlCommit") != CONTROL_SHA or policy.get("phases") != expected_phases()):
        raise ValueError("Exact immutable paired expiry qualification policy required")
    ledger = intake.parse_json((evidence / "launcher.json").read_bytes())
    worker = intake.parse_json((evidence / "kernel-proof.json").read_bytes())
    if (ledger.get("authorityScope") != SCOPE or ledger.get("cleanupVerified") is not True
        or ledger.get("managerUnitAbsent") is not True or ledger.get("sdkStarted") is not True
        or ledger.get("deferredSignals") != [] or any(key in ledger for key in ("failure", "cleanupFailure", "cacheCleanupFailure", "sdkReceiptReadFailure"))
        or worker.get("phasesSucceeded") is not True or "failure" in worker
        or worker.get("unit") != ledger.get("unit") or worker.get("context", {}).get("invocationId") != ledger.get("invocationId")
        or worker.get("sourceBindingSha256") != ledger.get("sourceBindingSha256")
        or worker.get("sourceBindingSha256") != policy["sourceBindingSha256"]
        or worker.get("runId") != ledger.get("runId") or worker.get("runAttempt") != ledger.get("runAttempt")
        or worker.get("transportSha") != ledger.get("executionHead")
        or not re.fullmatch(r"[0-9a-f]{64}", worker.get("sourceBindingSha256", ""))
        or not re.fullmatch(r"[0-9a-f]{64}", worker.get("testAssemblySha256", ""))
        or worker.get("controlCommit") != CONTROL_SHA
        or set(worker.get("trx", {})) != COUNTERS
        or any(type(value) is not int for value in worker.get("trx", {}).values())
        or worker.get("trx", {}).get("total") != 2 or worker.get("trx", {}).get("executed") != 2
        or worker.get("trx", {}).get("passed") != 2
        or any(value != 0 for key, value in worker.get("trx", {}).items() if key not in {"total", "executed", "passed"})
        or len(ledger.get("removedCaches", [])) != 2):
        raise ValueError("Actual paired phase and final original owner/cache settlement required")
    for row in expected_phases():
        receipt = intake.parse_json((evidence / (row["id"] + ".json")).read_bytes())
        if (receipt.get("phaseSucceeded") is not True or receipt.get("cleanupVerified") is not True
            or receipt.get("custodySettled") is not True or receipt.get("leaderReaped") is not True
            or receipt.get("pidfdExited") is not True or receipt.get("privateGroupHasNoLiveMembers") is not True
            or receipt.get("admittedArgv") != row["argv"]
            or receipt.get("phase") != row["name"]
            or receipt.get("existingSdkOwner") != worker.get("context")
            or receipt.get("manifestSha256") != policy["manifestSha256"]
            or receipt.get("nativeAdmissionSha256") != policy["nativeAdmissionSha256"]
            or any(receipt.get(key) != worker.get(key) for key in ("sourceBindingSha256", "runId", "runAttempt", "transportSha"))):
            raise ValueError("Each exact owned phase must settle before acceptance")
    import resin_expiry_enrollment as enrollment
    context = enrollment.safe_read(os.environ["RESIN_ENROLLMENT_CONTEXT"], enrollment.MAX_CONTEXT)
    admissions = [ledger.get("enrollment")]
    admissions.extend(intake.parse_json((evidence / (row["id"] + ".json")).read_bytes()).get("enrollment") for row in expected_phases())
    enrollment.validate_final_context(policy, context, admissions)
    return True


def owned_worker():
    parser = argparse.ArgumentParser()
    for name in ("policy", "permit", "candidate", "evidence", "receipt"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--unit", required=True)
    parser.add_argument("--owned-worker", action="store_true")
    args = parser.parse_args()
    policy, grant = authorize_creation(args.policy, args.permit, args.candidate)
    from run_web_capped_kernel_proof import manager
    show = manager(args.unit)
    target = Path("/sys/fs/cgroup") / show["ControlGroup"].lstrip("/")
    st = target.stat()
    context = {"owner": admission.OWNER, "unit": args.unit, "invocationId": show["InvocationID"], "cgroup": show["ControlGroup"],
               "device": st.st_dev, "inode": st.st_ino, "hostBootId": Path("/proc/sys/kernel/random/boot_id").read_text().strip(), "expiresUtc": grant["expiresUtc"]}
    context_path = args.evidence / "sdk-owner-context.json"
    with context_path.open("x", encoding="utf-8") as stream:
        stream.write(json.dumps(context) + "\n")
    phase.verify_capped_owner(policy, context_path, grant=grant)
    record = {key: grant[key] for key in ("sourceBindingSha256", "runId", "runAttempt", "transportSha")}
    record.update(unit=args.unit, context=context, controlCommit=CONTROL_SHA, phasesSucceeded=False,
                  harnessProcess={"pid": os.getpid(), "startTicks": Path("/proc/self/stat").read_text().rsplit(")", 1)[1].split()[19], "executable": os.readlink("/proc/self/exe")})
    args.receipt.write_text(json.dumps(record) + "\n")
    root = args.candidate.resolve(strict=True)
    for name, pin in PINS.items():
        dependency = root / ".dependencies" / ("Legacy.Maliev." + name)
        if (phase.subprocess.check_output(["git", "-C", str(dependency), "rev-parse", "HEAD"], text=True, timeout=10).strip() != pin
            or phase.subprocess.check_output(["git", "-C", str(dependency), "status", "--porcelain"], timeout=10)):
            raise ValueError("Clean exact dependency projection required")
    installer = root / ".dependencies/dotnet-installer/externals/install-dotnet.sh"
    if (phase.subprocess.check_output(["git", "-C", str(installer.parent.parent), "rev-parse", "HEAD"], text=True, timeout=10).strip() != policy["sdkInstaller"]["commit"]
        or intake.sha256(installer.read_bytes()) != policy["sdkInstaller"]["sha256"]):
        raise ValueError("Exact reviewed SDK installer source required")
    sdk = root / ".dependencies/resin-sdk"
    os.environ.update(GITHUB_ACTIONS="false", MalievWorkspaceRoot=str(root / ".dependencies"),
                      MSBUILDDISABLENODEREUSE="1", DOTNET_CLI_USE_MSBUILD_SERVER="0",
                      DOTNET_CLI_HOME=str(sdk / "cli-home"), NUGET_PACKAGES=str(sdk / "packages"))
    previous_argv, previous_directory = sys.argv, Path.cwd()
    primary = None
    try:
        os.chdir(root)
        for row in expected_phases():
            intake.verify_source(root, policy)
            sys.argv = ["resin", "--policy", str(args.policy), "--permit", str(args.permit), "--evidence", str(args.evidence),
                        "--sdk-owner-context", str(context_path), "--phase", row["name"], "--id", row["id"], "--", *row["argv"]]
            phase.main()
            intake.verify_source(root, policy)
            if row["id"] == "resin-sdk-install":
                os.environ.update(DOTNET_ROOT=str(sdk), PATH=str(sdk) + os.pathsep + os.environ["PATH"])
            if row["id"] == "resin-sdk-version" and (args.evidence / "resin-sdk-version.log").read_text().strip() != policy["sdkInstaller"]["version"]:
                raise ValueError("Exact actual SDK version required")
            if row["id"] == "resin-build":
                record["testAssemblySha256"] = intake.sha256((root / "Legacy.Maliev.Web.Tests/bin/Release/net10.0/Legacy.Maliev.Web.Tests.dll").read_bytes())
            if row["id"] == "resin-focused":
                record["trx"] = verify_trx(root / "resin-comparison-expiry-results/paired-expiry.trx", record["testAssemblySha256"])
        record["phasesSucceeded"] = True
    except BaseException as error:
        primary = error
        record["failure"] = type(error).__name__
    finally:
        sys.argv = previous_argv
        os.chdir(previous_directory)
        try:
            args.receipt.write_text(json.dumps(record) + "\n")
        except BaseException as error:
            if primary is None:
                primary = error
    if primary is not None:
        raise primary


if __name__ == "__main__":
    if "--verify-final" in sys.argv:
        parser = argparse.ArgumentParser()
        parser.add_argument("--verify-final", type=Path, required=True)
        verify_final(parser.parse_args().verify_final)
    else:
        owned_worker()
