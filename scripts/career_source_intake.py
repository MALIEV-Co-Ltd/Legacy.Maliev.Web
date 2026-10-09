"""Exact three-path Career enrollment; no old permit authorizes this slice."""
import datetime as dt
import json
import os
from pathlib import Path
import re
import shutil
import sys
import tempfile
import uuid
import xml.etree.ElementTree as ET
from collections import Counter, defaultdict
import materialize_web_candidate as intake
import check_web_candidate_admission as admission

KIND = "career-total-records-v1"
BASE = "b290163d41398cab02ee3741abd98b9c00343a34"
POSTIMAGES = {
    "Legacy.Maliev.Web.Application/CareerContracts.cs": "1987dc78eee95d4adc7857f2b5133b66f8deb2b1c93cf129153615d8fbb3a000",
    "Legacy.Maliev.Web.Tests/CareerClientTests.cs": "1d32c11a1055a0e7b81459cea2f066c32638f1a24541f5d9c8cb3b0346f18852",
    "Legacy.Maliev.Web.Tests/CareerIndexStaticSsrRouteTests.cs": "791399416f30182e8d7834bbb9a167b66cae8d413062ee71047e262591950526",
}
SHARED_MEMBERDATA = {
    ("Legacy.Maliev.Web.Tests.InstantQuotationUploadClientTests", "GeometryProvenance_InvalidOrUnboundClaimCannotBePromoted"): 20,
    ("Legacy.Maliev.Web.Tests.CncAuthenticatedProfileTests", "MissingOrMismatchedNestedRelationships_FailClosed"): 10,
}


def validate_source_policy(policy):
    rows = policy["sourceFiles"]
    if (policy.get("sliceKind") != KIND or policy.get("acceptedBase") != BASE
        or len(rows) != 3 or {r["path"]: r["sha256"] for r in rows} != POSTIMAGES
        or len(policy["capsuleRows"]) != 3 or {r["path"] for r in policy["capsuleRows"]} != set(POSTIMAGES)
        or set(policy["preimages"]) != set(POSTIMAGES)
        or policy["sourcePins"] != {"ServiceDefaults": "3c790ba6414b2a539f24aabb6948549ffd81a86b",
                                    "CompatibilityContracts": "78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7"}
        or intake.sha256(json.dumps(rows, sort_keys=True, separators=(",", ":")).encode()) != policy["sourceBindingSha256"]):
        raise ValueError("Exact reviewed Career source/base/binding required")


def validate_permit(policy, raw, now=None):
    validate_source_policy(policy)
    if not re.fullmatch(r"[0-9a-f]{64}", policy.get("nativeAdmissionSha256") or ""):
        raise ValueError("Independent Career permit remains unissued")
    if intake.sha256(raw) != policy["nativeAdmissionSha256"]:
        raise ValueError("Career permit raw identity changed")
    enrollment = {"backend": "launch_web_capped_kernel_proof-v1", "memoryMaxBytes": 3221225472,
                  "swapMaxBytes": 0, "cpuQuotaPercent": 100, "tasksMax": 512, "leaseSeconds": 2100}
    if policy.get("sdkOwnerContextSha256") is not None or policy.get("sdkOwnerEnrollment") != enrollment:
        raise ValueError("Distinct same-job Career capped owner creation required")
    grant = intake.parse_json(raw)
    expected = {"owner": admission.OWNER, "issuedBy": "019fc21e-50f0-7112-834f-9fb3b35b9dfe",
                "environment": "github-hosted-linux", "sliceKind": KIND,
                **{k: policy[k] for k in ("acceptedBase", "sourcePins", "sourceBindingSha256", "manifestSha256",
                   "sdkOwnerContextSha256", "sdkOwnerEnrollment", "allowedPhases", "phases")},
                "transportSha": os.environ.get("WEB_REVIEWED_TRANSPORT_SHA"),
                "runId": os.environ.get("GITHUB_RUN_ID"), "runAttempt": os.environ.get("GITHUB_RUN_ATTEMPT"),
                "ownerNonce": grant.get("ownerNonce")}
    if (not re.fullmatch(r"[0-9a-f]{40}", expected["transportSha"] or "")
        or any(not re.fullmatch(r"[1-9][0-9]*", expected[k] or "") for k in ("runId", "runAttempt"))
        or not re.fullmatch(r"[0-9a-f]{32}", expected["ownerNonce"] or "")
        or set(grant) != set(expected) | {"startsUtc", "expiresUtc"}
        or any(grant[k] != value for k, value in expected.items())):
        raise ValueError("Exact fresh Career scope/run/commands/nonce required")
    start = dt.datetime.fromisoformat(grant["startsUtc"].replace("Z", "+00:00"))
    end = dt.datetime.fromisoformat(grant["expiresUtc"].replace("Z", "+00:00"))
    now = now or dt.datetime.now(dt.timezone.utc)
    if (os.name != "posix" or start.utcoffset() != dt.timedelta(0) or end.utcoffset() != dt.timedelta(0)
        or not start <= now < end or not 0 < (end - start).total_seconds() <= 2100):
        raise ValueError("Actual Linux and finite unrenewed Career lease required")
    return grant


def materialize_career(root, policy, files, finalize=None):
    validate_source_policy(policy)
    if set(files) != set(POSTIMAGES):
        raise ValueError("Exact three Career files required")
    if (intake.subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip() != BASE
        or intake.subprocess.check_output(["git", "-C", str(root), "status", "--porcelain", "--untracked-files=all"])):
        raise ValueError("Exact clean Career checkout required")
    before = {}
    for name, data in files.items():
        target = root / intake.canonical_path(name)
        if any(p.is_symlink() or (hasattr(p, "is_junction") and p.is_junction()) for p in (target, *target.parents)):
            raise ValueError("Career destination link rejected")
        before[name] = target.read_bytes()
        if intake.sha256(before[name]) != policy["preimages"][name] or intake.sha256(data) != POSTIMAGES[name]:
            raise ValueError("Exact Career pre/post image required")
    temporary = Path(tempfile.mkdtemp(prefix="career-overlay-", dir=root.parent))
    written = []
    settled = False
    try:
        for index, (name, data) in enumerate(files.items()):
            (temporary / (str(index) + ".old")).write_bytes(before[name])
            (temporary / (str(index) + ".new")).write_bytes(data)
        for index, name in enumerate(files):
            if (root / name).read_bytes() != before[name]:
                raise ValueError("Career preimage changed before replacement")
            os.replace(temporary / (str(index) + ".new"), root / name)
            written.append((index, name))
        intake.verify_source(root, policy)
        if finalize is not None:
            finalize()
        settled = True
    except BaseException as primary:
        failures = []
        for index, name in reversed(written):
            try:
                os.replace(temporary / (str(index) + ".old"), root / name)
                if (root / name).read_bytes() != before[name]:
                    raise ValueError("Career rollback bytes changed")
            except BaseException as error:
                failures.append(error)
        if failures:
            raise BaseExceptionGroup("Career rollback failed; recovery retained at " + str(temporary), [primary, *failures])
        settled = True
        raise
    finally:
        if settled:
            shutil.rmtree(temporary)
            if temporary.exists():
                raise ValueError("Career staging cleanup unverified")


def materialize_with_receipt(root, policy, files, args, transport_commit):
    def finalize():
        args.receipt.parent.mkdir(parents=True, exist_ok=True)
        with args.receipt.open("x", encoding="utf-8") as stream:
            stream.write(json.dumps({"manifestSha256": policy["manifestSha256"], "manifestBlob": args.manifest_blob,
                                    "capsuleBlob": args.capsule_blob, "acceptedBase": policy["acceptedBase"],
                                    "sourcePins": policy["sourcePins"], "sourceFiles": policy["sourceFiles"],
                                    "transportCommit": transport_commit, "nativeValidated": False}, indent=2) + "\n")
    materialize_career(root, policy, files, finalize)


def authorize_creation(policy, permit_path, candidate):
    if not policy.get("nativeAdmissionSha256"):
        raise ValueError("Independent Career owner creation permit remains unissued")
    grant = validate_permit(policy, permit_path.read_bytes())
    if os.environ.get("WEB_REVIEWED_TRANSPORT_SHA") != os.environ.get("GITHUB_SHA"):
        raise ValueError("Exact protected Career transport required")
    intake.verify_source(candidate, policy)
    admission.census()
    return policy, grant


def owned_worker(args, policy, grant):
    import run_web_account_candidate as account
    import run_web_candidate_phase as phase
    from run_web_capped_kernel_proof import manager
    show = manager(args.unit)
    target = Path("/sys/fs/cgroup") / show["ControlGroup"].lstrip("/")
    st = target.stat()
    context = {"owner": admission.OWNER, "unit": args.unit, "invocationId": show["InvocationID"],
               "cgroup": show["ControlGroup"], "device": st.st_dev, "inode": st.st_ino,
               "hostBootId": Path("/proc/sys/kernel/random/boot_id").read_text().strip(), "expiresUtc": grant["expiresUtc"]}
    context_path = args.evidence / "sdk-owner-context.json"
    with context_path.open("x", encoding="utf-8") as stream:
        stream.write(json.dumps(context, indent=2) + "\n")
    phase.verify_capped_owner(policy, context_path, grant=grant)
    root = args.candidate.resolve(strict=True)
    for name, pin in policy["profilePreparationFiles"].items():
        if intake.sha256((root / name).read_bytes()) != pin:
            raise ValueError("Exact Career suite preparation source required")
    for name, pin in policy["sourcePins"].items():
        directory = root / ".dependencies" / ("Legacy.Maliev." + name)
        if (phase.subprocess.check_output(["git", "-C", str(directory), "rev-parse", "HEAD"], text=True).strip() != pin
            or phase.subprocess.check_output(["git", "-C", str(directory), "status", "--porcelain"])):
            raise ValueError("Exact clean Career dependency projection required")
    installer = root / ".dependencies/dotnet-installer/externals/install-dotnet.sh"
    if (phase.subprocess.check_output(["git", "-C", str(installer.parent.parent), "rev-parse", "HEAD"], text=True).strip() != policy["sdkInstaller"]["commit"]
        or intake.sha256(installer.read_bytes()) != policy["sdkInstaller"]["sha256"]):
        raise ValueError("Exact Career SDK installer required")
    outputs = args.evidence / "profile.outputs"
    with outputs.open("x"):
        pass
    os.environ.update(GITHUB_ENV=str(outputs), MalievWorkspaceRoot=str(root / ".dependencies"), GITHUB_ACTIONS="false",
                      MALIEV_WEB_STARTUP_PROOF="hosted-owned", MSBUILDDISABLENODEREUSE="1",
                      DOTNET_CLI_HOME=str(root / ".dependencies/email-sdk/cli-home"),
                      NUGET_PACKAGES=str(root / ".dependencies/email-sdk/packages"), DOTNET_CLI_USE_MSBUILD_SERVER="0",
                      PLAYWRIGHT_BROWSERS_PATH=str(args.evidence / "browser-cache"))
    scanner = root / ".dependencies/email-sdk/scanner-bin"
    scanner.mkdir(exist_ok=False)
    os.environ.update(GOTOOLCHAIN="go1.26.9", GOMAXPROCS="1", GOFLAGS="-p=1", GOBIN=str(scanner),
                      GOPATH=str(root / ".dependencies/email-sdk/go-cache"),
                      PATH=str(scanner) + os.pathsep + os.environ["PATH"])
    record = {"context": context, "contextSha256": intake.sha256(context_path.read_bytes()), "nativeValidated": False,
              "harnessProcess": {"pid": os.getpid(), "startTicks": Path("/proc/self/stat").read_text().rsplit(")", 1)[1].split()[19],
                                 "executable": os.readlink("/proc/self/exe")}}
    previous = sys.argv
    primary = None
    try:
        for row in policy["phases"]:
            sys.argv = ["career", "--policy", str(args.policy), "--candidate", str(root), "--permit", str(args.permit),
                        "--sdk-owner-context", str(context_path), "--evidence", str(args.evidence), "--phase-id", row["id"]]
            account.main()
            log = (args.evidence / (row["id"] + ".log")).read_text()
            if row["id"] == "career-sdk-install":
                sdk = root / ".dependencies/email-sdk"
                os.environ.update(DOTNET_ROOT=str(sdk), PATH=str(sdk) + os.pathsep + os.environ["PATH"])
            if row["id"] == "career-sdk-version" and log.strip() != policy["sdkInstaller"]["version"]:
                raise ValueError("Actual Career SDK version mismatch")
            if row["id"] == "career-build":
                if not re.search(r"\b0 Warning\(s\)", log) or not re.search(r"\b0 Error\(s\)", log):
                    raise ValueError("Actual Career build zero warnings/errors required")
                record["testAssemblySha256"] = intake.sha256((root / "Legacy.Maliev.Web.Tests/bin/Release/net10.0/Legacy.Maliev.Web.Tests.dll").read_bytes())
            if row["id"].startswith("career-prepare-") and re.search(r"\b(?:warning|error) [A-Z]+\d+\s*:", log):
                raise ValueError("Prepared Career suite services reported warnings/errors")
            if row["id"] == "career-prepare-billing":
                os.environ.update(account.read_profile_outputs(outputs, root))
            if row["id"] in {"career-focused", "career-suite"}:
                focused = row["id"] == "career-focused"
                trx = root / ("career-test-results/career-focused.trx" if focused else "career-suite-results/career-suite.trx")
                counts = (account.verify_trx(trx, True, record["testAssemblySha256"], expected_cases=expected_cases(), normalize_storage=True)
                          if focused else verify_suite(trx, policy["suiteCount"], record["testAssemblySha256"]))
                if counts["total"] != policy["focusedCount" if focused else "suiteCount"]:
                    raise ValueError("Exact Career focused/full suite count required")
                record[row["id"]] = counts
            if row["id"] == "career-packages":
                for name in ("Legacy.Maliev.Web", "Legacy.Maliev.Web.Application", "Legacy.Maliev.Web.Infrastructure", "Legacy.Maliev.Web.Tests"):
                    if f"The given project `{name}` has no vulnerable packages" not in log:
                        raise ValueError("Exact four Career dependency vulnerability results required")
        record["nativeValidated"] = True
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
            sys.stderr.write(json.dumps({"workerReceiptWriteFailure": type(error).__name__,
                                        "originalFailure": type(primary).__name__}) + "\n")
    if primary is not None:
        raise primary


def verify_suite(path, expected_count, assembly_sha256):
    """Join native executions to definitions; foreign display text is never normalized."""
    root = ET.parse(path).getroot()
    dll = (path.parent.parent / "Legacy.Maliev.Web.Tests/bin/Release/net10.0/Legacy.Maliev.Web.Tests.dll").resolve(strict=True)
    if intake.sha256(dll.read_bytes()) != assembly_sha256:
        raise ValueError("Actual full-suite built assembly digest changed")
    return _verify_suite_tree(root, expected_count, dll)


def _verify_suite_tree(root, expected_count, dll):
    """Pure structural control; caller must separately prove actual assembly bytes."""
    ns = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
    def one(parent, tag):
        rows = parent.findall(ns + tag)
        if len(rows) != 1:
            raise ValueError("Exact one native " + tag + " required")
        return rows[0]
    if root.tag != ns + "TestRun":
        raise ValueError("Exact native TRX namespace required")
    summary = one(root, "ResultSummary")
    counters = one(summary, "Counters")
    fields = {"total", "executed", "passed", "failed", "error", "timeout", "aborted", "inconclusive",
              "passedButRunAborted", "notRunnable", "notExecuted", "disconnected", "warning", "completed", "inProgress", "pending"}
    if (summary.get("outcome") != "Completed" or set(counters.attrib) != fields
        or any(not re.fullmatch(r"0|[1-9][0-9]*", value) for value in counters.attrib.values())
        or any(int(value) != (expected_count if key in {"total", "executed", "passed"} else 0) for key, value in counters.attrib.items())):
        raise ValueError("Exact complete full-suite counters required")
    containers = [(one(root, name), ns + child) for name, child in
                  (("Results", "UnitTestResult"), ("TestDefinitions", "UnitTest"), ("TestEntries", "TestEntry"))]
    if any(any(row.tag != tag for row in parent) for parent, tag in containers):
        raise ValueError("Unknown native execution/definition/entry row")
    results, definitions, entries = [list(parent) for parent, _ in containers]
    def unique(rows, key):
        values = [row.get(key, "") for row in rows]
        if len(set(values)) != len(values) or any(str(uuid.UUID(value)) != value for value in values):
            raise ValueError("Unique canonical native " + key + " required")
        return set(values)
    definition_ids = unique(definitions, "id")
    unique(results, "executionId")
    unique(entries, "executionId")
    result_pairs = {(row.get("testId"), row.get("executionId")) for row in results}
    if (len(results) != expected_count or len(entries) != expected_count
        or result_pairs != {(row.get("testId"), row.get("executionId")) for row in entries}
        or {row.get("testId") for row in results} != definition_ids):
        raise ValueError("Exact native execution/entry/definition joins required")
    by_id = {row.get("id"): row for row in definitions}
    groups = defaultdict(list)
    authored = expected_cases()
    matched = set()
    names = Counter()
    for result in results:
        definition = by_id[result.get("testId")]
        method = one(definition, "TestMethod")
        cls, member = method.get("className", ""), method.get("name", "")
        prefix = cls + "." + member
        name = result.get("testName", "")
        display = definition.get("name", "")
        if (result.get("outcome") != "Passed" or not cls.startswith("Legacy.Maliev.Web.Tests.") or not member
            or not (name == prefix or name.startswith(prefix + "("))
            or not (display == prefix or display.startswith(prefix + "("))
            or Path(method.get("codeBase", "")).resolve() != dll
            or str(Path(definition.get("storage", "")).resolve()).casefold() != str(dll).casefold()):
            raise ValueError("Passing native assembly/class/member/display identity required")
        if ((cls, member) in SHARED_MEMBERDATA and display != prefix
            or (cls, member) not in SHARED_MEMBERDATA and display != name
            and not (len(name) > 447 and display == name[:444] + "···")):
            raise ValueError("Exact literal native definition display representation required")
        names[name] += 1
        groups[result.get("testId")].append(result)
        if cls in {value[0] for value in authored.values()} or name in authored:
            if name not in authored or authored[name] != (cls, member) or display != name or name in matched:
                raise ValueError("Exact unnormalized Career authored display row required")
            matched.add(name)
    shared = {}
    for identity, rows in groups.items():
        definition = by_id[identity]
        if one(definition, "Execution").get("id") not in {row.get("executionId") for row in rows}:
            raise ValueError("Native definition execution association changed")
        if len(rows) > 1:
            method = one(definition, "TestMethod")
            key = (method.get("className"), method.get("name"))
            if key in shared:
                raise ValueError("Duplicate shared native MemberData definition")
            shared[key] = len(rows)
    if (shared != SHARED_MEMBERDATA or {name: count for name, count in names.items() if count > 1} != NATIVE_DISPLAY_ALIASES
        or matched != set(authored)):
        raise ValueError("Exact two existing shared MemberData definitions and all20Career rows required")
    return {key: int(value) for key, value in counters.attrib.items()}


def expected_cases():
    return {key: tuple(value) for key, value in AUTHORED_CASES.items()}

NATIVE_DISPLAY_ALIASES = {
  "Legacy.Maliev.Web.Tests.CncFileTransportTests.Upload_InvalidObjectShapeRequiresReconciliation(item: \"{\\\"Bucket\\\":\\\"maliev-instant-quotations\\\",\\\"Object\"···)": 3,
  "Legacy.Maliev.Web.Tests.CncFileFinalizationClientTests.InvalidPath_NeverSends(path: \"2026-9-6/66666666-1111-2222-3333-444444444444/1111\"···)": 2,
  "Legacy.Maliev.Web.Tests.CncSignedLinkClientTests.GetAsync_RejectsNonFinalizedCoordinatesBeforeTransport(objectName: \"instant-quotation/2026-9-7/11111111-1111-4111-8111\"···)": 2,
  "Legacy.Maliev.Web.Tests.CncFileFinalizationClientTests.Link_LocationMustIdentifyExactResourceAtTrustedOrigin(location: \"https://quotations.example/quotationrequests/files\"···, accepted: False)": 3
}

AUTHORED_CASES = {
  "Legacy.Maliev.Web.Tests.CareerClientTests.Listing_UsesLegacyCompatibleCareerRoutesAndWireShape": [
    "Legacy.Maliev.Web.Tests.CareerClientTests",
    "Listing_UsesLegacyCompatibleCareerRoutesAndWireShape"
  ],
  "Legacy.Maliev.Web.Tests.CareerClientTests.Offer_NotFoundIsAvailableWithoutInventingData": [
    "Legacy.Maliev.Web.Tests.CareerClientTests",
    "Offer_NotFoundIsAvailableWithoutInventingData"
  ],
  "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests.Host_DeclaresTheCareerRouteAndRetainsItsRazorRollbackSource": [
    "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests",
    "Host_DeclaresTheCareerRouteAndRetainsItsRazorRollbackSource"
  ],
  "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests.CareerRoute_RendersLocalizedServiceBackedStaticSsrWithSeoAndAnalytics(culture: \"en\", title: \"Career | MALIEV\", description: \"Explore MALIEV career opportunities in engineering\"···, heading: \"Job Offers\")": [
    "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests",
    "CareerRoute_RendersLocalizedServiceBackedStaticSsrWithSeoAndAnalytics"
  ],
  "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests.CareerRoute_RendersLocalizedServiceBackedStaticSsrWithSeoAndAnalytics(culture: \"th\", title: \"ตำแหน่งงาน | MALIEV\", description: \"ดูตำแหน่งงานกับ MALIEV และเรียนรู้วิธีสมัครงานด้าน\"···, heading: \"ตำแหน่งงาน\")": [
    "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests",
    "CareerRoute_RendersLocalizedServiceBackedStaticSsrWithSeoAndAnalytics"
  ],
  "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests.CareerRoute_RendersOriginalTotalRecordsWireThroughTypedClient(culture: \"en\", heading: \"Job Offers\")": [
    "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests",
    "CareerRoute_RendersOriginalTotalRecordsWireThroughTypedClient"
  ],
  "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests.CareerRoute_RendersOriginalTotalRecordsWireThroughTypedClient(culture: \"th\", heading: \"ตำแหน่งงาน\")": [
    "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests",
    "CareerRoute_RendersOriginalTotalRecordsWireThroughTypedClient"
  ],
  "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests.LocalReviewCareerRoute_HidesOnlyTheExactAspireFixture": [
    "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests",
    "LocalReviewCareerRoute_HidesOnlyTheExactAspireFixture"
  ],
  "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests.CareerRoute_NormalizesSortAndIndexAndPreservesCanonicalDocumentLinks": [
    "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests",
    "CareerRoute_NormalizesSortAndIndexAndPreservesCanonicalDocumentLinks"
  ],
  "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests.CareerRoute_RejectsOutOfRangePageSizeBeforeCallingCareerService(size: \"0\")": [
    "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests",
    "CareerRoute_RejectsOutOfRangePageSizeBeforeCallingCareerService"
  ],
  "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests.CareerRoute_RejectsOutOfRangePageSizeBeforeCallingCareerService(size: \"-1\")": [
    "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests",
    "CareerRoute_RejectsOutOfRangePageSizeBeforeCallingCareerService"
  ],
  "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests.CareerRoute_RejectsOutOfRangePageSizeBeforeCallingCareerService(size: \"101\")": [
    "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests",
    "CareerRoute_RejectsOutOfRangePageSizeBeforeCallingCareerService"
  ],
  "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests.CareerRoute_RejectsOutOfRangePageSizeBeforeCallingCareerService(size: \"2147483648\")": [
    "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests",
    "CareerRoute_RejectsOutOfRangePageSizeBeforeCallingCareerService"
  ],
  "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests.CareerRoute_RejectsMalformedPageSize": [
    "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests",
    "CareerRoute_RejectsMalformedPageSize"
  ],
  "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests.DisabledCareerRoute_UsesTheRetainedRazorFallback": [
    "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests",
    "DisabledCareerRoute_UsesTheRetainedRazorFallback"
  ],
  "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests.DisabledCareerRoute_RejectsInvalidPageSizeBeforeCallingCareerService(size: \"invalid-size\")": [
    "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests",
    "DisabledCareerRoute_RejectsInvalidPageSizeBeforeCallingCareerService"
  ],
  "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests.DisabledCareerRoute_RejectsInvalidPageSizeBeforeCallingCareerService(size: \"0\")": [
    "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests",
    "DisabledCareerRoute_RejectsInvalidPageSizeBeforeCallingCareerService"
  ],
  "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests.DisabledCareerRoute_RejectsInvalidPageSizeBeforeCallingCareerService(size: \"-1\")": [
    "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests",
    "DisabledCareerRoute_RejectsInvalidPageSizeBeforeCallingCareerService"
  ],
  "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests.DisabledCareerRoute_RejectsInvalidPageSizeBeforeCallingCareerService(size: \"101\")": [
    "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests",
    "DisabledCareerRoute_RejectsInvalidPageSizeBeforeCallingCareerService"
  ],
  "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests.DisabledCareerRoute_RejectsInvalidPageSizeBeforeCallingCareerService(size: \"2147483648\")": [
    "Legacy.Maliev.Web.Tests.CareerIndexStaticSsrRouteTests",
    "DisabledCareerRoute_RejectsInvalidPageSizeBeforeCallingCareerService"
  ]
}
