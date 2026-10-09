"""Account-only adapter. Caller must already inhabit the genuine externally owned SDK unit."""
import argparse
import os
from pathlib import Path
import re
import sys
import json
import re
import xml.etree.ElementTree as ET
import uuid
from collections import Counter
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
    if policy.get("sliceKind") not in {"account-failure-v1", "email-change-session-v1", "career-total-records-v1"}:
        raise ValueError("Distinct account policy required")
    if not policy.get("nativeAdmissionSha256") or (not policy.get("sdkOwnerContextSha256") and not policy.get("sdkOwnerEnrollment")):
        raise ValueError("Actual independently pinned permit/SDK owner absent; no SDK launch")
    actual = os.environ.get("WEB_REVIEWED_TRANSPORT_SHA", "")
    if not re.fullmatch(r"[0-9a-f]{40}", actual) or actual != os.environ.get("GITHUB_SHA"):
        raise ValueError("Exact reviewed workflow transport required")
    admission.census()
    grant = admission.validate(policy, args.permit.read_bytes())
    phase.verify_capped_owner(policy, args.sdk_owner_context, grant=grant)
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

def authorize_creation(policy_path, permit_path, candidate):
    policy = intake.load_policy(policy_path)
    if policy.get("sliceKind") == "career-total-records-v1":
        from career_source_intake import authorize_creation as career_authorize
        return career_authorize(policy, permit_path, candidate)
    if policy.get("sliceKind") != "email-change-session-v1" or not policy.get("sdkOwnerEnrollment"):
        raise ValueError("Exact new email owner-creation authority required")
    if not policy.get("nativeAdmissionSha256"):
        raise ValueError("Independent same-job owner creation grant remains unissued")
    grant = admission.validate(policy, permit_path.read_bytes())
    if grant.get("sdkOwnerContextSha256") is not None or not grant.get("ownerNonce"):
        raise ValueError("Old pre-enrolled grants cannot create an SDK owner")
    actual = os.environ.get("WEB_REVIEWED_TRANSPORT_SHA")
    if actual != os.environ.get("GITHUB_SHA"):
        raise ValueError("Exact protected transport source required")
    intake.verify_source(candidate, policy)
    admission.census()
    return policy, grant

def read_profile_outputs(path, root):
    allowed = {"MALIEV_PROFILE_PRODUCER_DLL", "MALIEV_PROFILE_SEED_DLL", "MALIEV_PROFILE_AUTH_DLL",
               "MALIEV_PROFILE_AUTH_SEED_DLL", "MALIEV_MEMBER_AUTH_DLL", "MALIEV_MEMBER_AUTH_SEED_DLL",
               "MALIEV_SCB_ACCOUNTING_DLL", "MALIEV_BILLING_CATALOG_DLL"}
    values = {}
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        key, value = line.split("=", 1)
        binary = Path(value).resolve(strict=True)
        if key not in allowed or key in values or not binary.is_relative_to(root) or binary.suffix != ".dll":
            raise ValueError("Exact same-job prepared profile output required")
        values[key] = value
    if set(values) != allowed:
        raise ValueError("Existing complete profile producer outputs missing")
    return values

def expected_email_cases():
    # Independent source-authored roster; never infer membership from received TRX.
    namespace = "Legacy.Maliev.Web.Tests."
    cases = {}
    for renderer in (True, False):
        for culture in ("en", "th"):
            prefix = f'rendererEnabled: {renderer}, culture: "{culture}"'
            cls = namespace + "EmailChangeConfirmationSessionHttpTests"
            methods = [("CompletedEmailChange_ClearsCurrentCookieBeforeLoginRedirect", prefix)]
            methods += [("RejectedUnavailableOrUnauthorizedEmailChange_DoesNotSignOut",
                         prefix + f", available: {available}, authorized: {authorized}")
                        for available, authorized in ((True, True), (False, True), (True, False))]
            methods += [("IncompleteEmailChange_DoesNotCompleteOrSignOut", prefix + f', query: "{query}"')
                        for query in ("email=new%40example.com", "token=opaque-challenge")]
            for method, arguments in methods:
                cases[f"{cls}.{method}({arguments})"] = (cls, method)
            cls = namespace + "EmailChangeConfirmationRealSessionTests"
            method = "Confirmation_MutatesOnlySuccessfulCurrentSession"
            for outcome in ("success", "rejected", "unavailable", "unauthorized", "email-only", "token-only"):
                cases[f'{cls}.{method}(renderer: {renderer}, culture: "{culture}", outcome: "{outcome}")'] = (cls, method)
    if len(cases) != 48:
        raise ValueError("Independent authored email roster changed")
    return cases

def verify_trx(path, focused=False, assembly_sha256=None, expected_cases=None, normalize_storage=False):
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
    expected = expected_email_cases() if expected_cases is None else expected_cases
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
            or (str(Path(definition.attrib.get("storage", "")).resolve()).casefold() != str(actual_dll).casefold()
                if normalize_storage else Path(definition.attrib.get("storage", "")).resolve() != actual_dll)):
            raise ValueError("Exact passing Web assembly/class/definition/execution identity required")
        names.append(name)
        if cls in target_classes or name in expected:
            if name not in expected or expected[name] != (cls, method_name):
                raise ValueError("Unknown or mismatched authored theory/data case")
            authored.append(name)
    if len(set(names)) != len(names):
        raise ValueError("Duplicate executed test display case")
    if Counter(authored) != Counter(expected.keys()):
        raise ValueError("Exact independent 48 authored theory/data cases required")
    if focused and Counter(names) != Counter(expected.keys()):
        raise ValueError("Focused qualification admits exactly the authored 48 cases and no extras")
    return counts

def owned_worker():
    parser = argparse.ArgumentParser()
    for name in ("policy", "permit", "candidate", "evidence", "receipt"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--unit", required=True); parser.add_argument("--owned-worker", action="store_true")
    args = parser.parse_args()
    policy, grant = authorize_creation(args.policy, args.permit, args.candidate)
    if policy.get("sliceKind") == "career-total-records-v1":
        from career_source_intake import owned_worker as career_worker
        return career_worker(args, policy, grant)
    from run_web_capped_kernel_proof import manager
    show = manager(args.unit)
    group = show["ControlGroup"]
    target = Path("/sys/fs/cgroup") / group.lstrip("/")
    st = target.stat()
    context = {"owner": admission.OWNER, "unit": args.unit, "invocationId": show["InvocationID"], "cgroup": group,
               "device": st.st_dev, "inode": st.st_ino, "hostBootId": Path("/proc/sys/kernel/random/boot_id").read_text().strip(),
               "expiresUtc": grant["expiresUtc"]}
    context_path = args.evidence / "sdk-owner-context.json"
    with context_path.open("x", encoding="utf-8") as output:
        output.write(json.dumps(context, indent=2) + "\n")
    phase.verify_capped_owner(policy, context_path, grant=grant)
    record = {"context": context, "contextSha256": intake.sha256(context_path.read_bytes()), "profileBinariesPrepared": False,
              "harnessProcess": {"pid": os.getpid(), "startTicks": Path("/proc/self/stat").read_text().rsplit(")", 1)[1].split()[19],
                                 "executable": os.readlink("/proc/self/exe")}}
    args.receipt.write_text(json.dumps(record, indent=2) + "\n")
    root = args.candidate.resolve(strict=True)
    dependency_receipt = {}
    for name, pin in policy["sourcePins"].items():
        directory = root / ".dependencies" / ("Legacy.Maliev." + name)
        head = phase.subprocess.check_output(["git", "-C", str(directory), "rev-parse", "HEAD"], text=True, timeout=10).strip()
        dirty = phase.subprocess.check_output(["git", "-C", str(directory), "status", "--porcelain"], timeout=10)
        if head != pin or dirty:
            raise ValueError("Exact clean dependency projection required")
        dependency_receipt[name] = {"path": str(directory), "head": head}
    installer = root / ".dependencies/dotnet-installer/externals/install-dotnet.sh"
    installer_head = phase.subprocess.check_output(["git", "-C", str(installer.parent.parent), "rev-parse", "HEAD"], text=True, timeout=10).strip()
    if installer_head != policy["sdkInstaller"]["commit"] or intake.sha256(installer.read_bytes()) != policy["sdkInstaller"]["sha256"]:
        raise ValueError("Exact source-bound SDK installer required")
    (args.evidence / "dependency-projections.json").write_text(json.dumps(dependency_receipt, indent=2) + "\n")
    profile_env = args.evidence / "profile.outputs"
    with profile_env.open("x"): pass
    os.environ.update(GITHUB_ENV=str(profile_env), MalievWorkspaceRoot=str(root / ".dependencies"), GITHUB_ACTIONS="false",
                      MALIEV_WEB_STARTUP_PROOF="hosted-owned", MSBUILDDISABLENODEREUSE="1",
                      DOTNET_CLI_HOME=str(root / ".dependencies/email-sdk/cli-home"),
                      NUGET_PACKAGES=str(root / ".dependencies/email-sdk/packages"), DOTNET_CLI_USE_MSBUILD_SERVER="0",
                      PLAYWRIGHT_BROWSERS_PATH=str(args.evidence / "browser-cache"))
    previous = sys.argv
    primary = None
    try:
        for row in policy["phases"]:
            sys.argv = ["account", "--policy", str(args.policy), "--candidate", str(root), "--permit", str(args.permit),
                        "--sdk-owner-context", str(context_path), "--evidence", str(args.evidence), "--phase-id", row["id"]]
            main()
            if row["id"] == "email-build":
                record["testAssemblySha256"] = intake.sha256((root / "Legacy.Maliev.Web.Tests/bin/Release/net10.0/Legacy.Maliev.Web.Tests.dll").read_bytes())
            if row["id"].startswith("email-prepare-"):
                if re.search(r"\b(?:warning|error) [A-Z]+\d+\s*:", (args.evidence / (row["id"] + ".log")).read_text()):
                    raise ValueError("Prepared producer build warnings/errors are not accepted")
            if row["id"] == "email-sdk-install":
                sdk = root / ".dependencies/email-sdk"
                os.environ.update(DOTNET_ROOT=str(sdk), PATH=str(sdk) + os.pathsep + os.environ["PATH"])
            if row["id"] == "email-sdk-version" and (args.evidence / "email-sdk-version.log").read_text().strip() != policy["sdkInstaller"]["version"]:
                raise ValueError("Actual pinned SDK version mismatch")
            if row["id"] == "email-prepare-billing":
                outputs = read_profile_outputs(profile_env, root)
                os.environ.update(outputs)
                proof = {"sourceBindingSha256": policy["sourceBindingSha256"], "runId": grant["runId"], "runAttempt": grant["runAttempt"],
                         "binarySha256": {key: intake.sha256(Path(value).read_bytes()) for key, value in outputs.items()},
                         "preparationScripts": policy["profilePreparationFiles"], "remoteServiceQualification": False,
                         "proofKind": "same-job-existing-prepared-binaries; suite acceptance is recorded separately"}
                (args.evidence / "qualified-profile.json").write_text(json.dumps(proof, indent=2) + "\n")
                record["profileBinariesPrepared"] = True
            if row["id"] in {"email-focused", "email-suite"}:
                focused = row["id"] == "email-focused"
                trx = root / ("email-change-test-results/email-change-focused.trx" if focused else "email-change-suite-results/email-change-suite.trx")
                record[row["id"]] = verify_trx(trx, focused, record["testAssemblySha256"])
        record["phasesSucceeded"] = True
    except BaseException as error:
        primary = error
        record["failure"] = type(error).__name__
    finally:
        sys.argv = previous
        try:
            args.receipt.write_text(json.dumps(record, indent=2) + "\n")
        except BaseException as error:
            if primary is None:
                primary = error
            sys.stderr.write(json.dumps({"workerReceiptWriteFailure": type(error).__name__, "originalFailure": type(primary).__name__}) + "\n")
    if primary is not None:
        raise primary

if __name__ == "__main__":
    owned_worker() if "--owned-worker" in sys.argv else main()
