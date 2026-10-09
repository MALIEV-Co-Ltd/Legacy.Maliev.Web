"""Fail closed until an independent Root grant and qualified CRM producer are policy-pinned."""
import argparse
import datetime as dt
import os
from pathlib import Path
import re
import materialize_web_candidate as intake

OWNER = "01a1009c-7d2d-7fc3-a239-2b1d9600a7a5"

def validate(policy, raw, now=None):
    if policy.get("sliceKind") == "career-total-records-v1":
        from career_source_intake import validate_permit
        return validate_permit(policy, raw, now)
    digest = policy.get("nativeAdmissionSha256")
    if policy.get("sliceKind") == "email-change-session-v1":
        return validate_email_session(policy, raw, now)
    if policy.get("sliceKind") == "account-failure-v1":
        return validate_account(policy, raw, now)
    if policy.get("sliceKind") == "country-operation-v1":
        return validate_country(policy, raw, now)
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

def validate_country(policy, raw, now=None):
    digest = policy.get("nativeAdmissionSha256")
    if not isinstance(digest,str) or not re.fullmatch(r"[0-9a-f]{64}",digest):
        raise ValueError("Root country BUILD permit remains unpinned")
    if intake.sha256(raw) != digest: raise ValueError("Country permit raw bytes changed")
    grant=intake.parse_json(raw)
    exact={"owner":OWNER,"issuedBy":"019fc21e-50f0-7112-834f-9fb3b35b9dfe",
           "environment":"github-hosted-linux","sliceKind":"country-operation-v1",
           "sourceBindingSha256":policy["sourceBindingSha256"],"manifestSha256":policy["manifestSha256"],
           "acceptedBase":policy["acceptedBase"],"sourcePins":policy["sourcePins"],
           "allowedPhases":["build"],"phase":policy["phase"]}
    if set(grant)!=set(exact)|{"startsUtc","expiresUtc"} or any(grant[k]!=v for k,v in exact.items()):
        raise ValueError("Exact independent country BUILD context required")
    start=dt.datetime.fromisoformat(grant["startsUtc"].replace("Z","+00:00"))
    end=dt.datetime.fromisoformat(grant["expiresUtc"].replace("Z","+00:00"))
    now=now or dt.datetime.now(dt.timezone.utc)
    if start.utcoffset()!=dt.timedelta(0) or end.utcoffset()!=dt.timedelta(0):
        raise ValueError("UTC country permit required")
    if not start<=now<end or not 0<(end-start).total_seconds()<=1200:
        raise ValueError("Fresh finite single BUILD permit required")
    if os.name!="posix":raise ValueError("Actual Linux runner required")
    return grant

def validate_account(policy, raw, now=None):
    digest = policy.get("nativeAdmissionSha256")
    owner_digest = policy.get("sdkOwnerContextSha256")
    if any(not isinstance(value, str) or not re.fullmatch(r"[0-9a-f]{64}", value)
           for value in (digest, owner_digest)):
        raise ValueError("Independent account permit and actual SDK owner remain unpinned")
    if intake.sha256(raw) != digest:
        raise ValueError("Account permit bytes differ from reviewed policy")
    prerequisites = policy.get("producerPrerequisites", {})
    if not isinstance(prerequisites, dict) or prerequisites.get("mode") != "controlled-auth-mail-wire-contracts" or prerequisites.get("remoteServiceQualification") is not False:
        raise ValueError("Account-only Auth/mail wire contract scope required")
    contract_paths = {"Legacy.Maliev.Web.Infrastructure/CustomerAuthenticationClient.cs",
                      "Legacy.Maliev.Web.Infrastructure/NotificationClient.cs",
                      "Legacy.Maliev.Web.Application/AccountContracts.cs",
                      "Legacy.Maliev.Web.Application/NotificationContracts.cs"}
    contracts = prerequisites.get("contractFiles", {})
    inventory = {row["path"]: row["sha256"] for row in policy["sourceFiles"]}
    if not isinstance(contracts, dict) or set(contracts) != contract_paths or any(inventory.get(path) != value for path, value in contracts.items()):
        raise ValueError("Pinned producer/consumer wire bytes required")
    test_path = "Legacy.Maliev.Web.Tests/AccountFailureParityHttpTests.cs"
    if (prerequisites.get("authEndpoint") != "auth/v1/customer-self-service/password-reset/request"
        or prerequisites.get("notificationEndpoint") != "notifications/v1/email/NoReply"
        or prerequisites.get("testPath") != test_path
        or prerequisites.get("testSha256") != inventory.get(test_path)):
        raise ValueError("Exact account wire endpoints and test required")
    grant = intake.parse_json(raw)
    expected = {"owner": OWNER, "issuedBy": "019fc21e-50f0-7112-834f-9fb3b35b9dfe",
                "environment": "github-hosted-linux", "sliceKind": "account-failure-v1",
                "acceptedBase": policy["acceptedBase"], "sourcePins": policy["sourcePins"],
                "sourceBindingSha256": policy["sourceBindingSha256"], "manifestSha256": policy["manifestSha256"],
                "producerPrerequisites": prerequisites, "sdkOwnerContextSha256": owner_digest,
                "allowedPhases": policy["allowedPhases"], "phases": policy["phases"],
                "transportSha": os.environ.get("WEB_REVIEWED_TRANSPORT_SHA"),
                "runId": os.environ.get("GITHUB_RUN_ID"), "runAttempt": os.environ.get("GITHUB_RUN_ATTEMPT")}
    if (not re.fullmatch(r"[0-9a-f]{40}", expected["transportSha"] or "")
        or not re.fullmatch(r"[1-9][0-9]*", expected["runId"] or "")
        or not re.fullmatch(r"[1-9][0-9]*", expected["runAttempt"] or "")):
        raise ValueError("Actual exact hosted run/head association required")
    if set(grant) != set(expected) | {"startsUtc", "expiresUtc"} or any(grant[k] != value for k, value in expected.items()):
        raise ValueError("Account permit exact scope/run/commands changed")
    start = dt.datetime.fromisoformat(grant["startsUtc"].replace("Z", "+00:00"))
    end = dt.datetime.fromisoformat(grant["expiresUtc"].replace("Z", "+00:00"))
    now = now or dt.datetime.now(dt.timezone.utc)
    if (start.utcoffset() != dt.timedelta(0) or end.utcoffset() != dt.timedelta(0)
        or not start <= now < end or not 0 < (end - start).total_seconds() <= 2100):
        raise ValueError("Finite nonrenewed account permit required")
    if os.name != "posix": raise ValueError("Actual hosted Linux required")
    return grant

def validate_email_session(policy, raw, now=None):
    # New scope cannot inherit any previously issued account/Career grant.
    owner_digest = policy.get("sdkOwnerContextSha256")
    enrollment = policy.get("sdkOwnerEnrollment")
    creates_owner = owner_digest is None and enrollment == {
        "backend": "launch_web_capped_kernel_proof-v1", "memoryMaxBytes": 3221225472,
        "swapMaxBytes": 0, "cpuQuotaPercent": 100, "tasksMax": 512, "leaseSeconds": 2100}
    if (not isinstance(policy.get("nativeAdmissionSha256"), str)
        or not re.fullmatch(r"[0-9a-f]{64}", policy["nativeAdmissionSha256"])
        or (not creates_owner and (not isinstance(owner_digest, str) or not re.fullmatch(r"[0-9a-f]{64}", owner_digest)))):
        raise ValueError("Independent email session permit and SDK owner remain unpinned")
    if intake.sha256(raw) != policy["nativeAdmissionSha256"]:
        raise ValueError("Email session permit raw bytes changed")
    prerequisites = policy.get("producerPrerequisites", {})
    inventory = {row["path"]: row["sha256"] for row in policy["sourceFiles"]}
    contracts = {"Legacy.Maliev.Web.Infrastructure/AccountSessionManager.cs",
                 "Legacy.Maliev.Web.Infrastructure/AccountSessionStore.cs",
                 "Legacy.Maliev.Web.Infrastructure/CustomerAuthenticationClient.cs",
                 "Legacy.Maliev.Web.Infrastructure/ServiceCollectionExtensions.cs",
                 "Legacy.Maliev.Web.Application/AccountContracts.cs"}
    if (prerequisites.get("mode") != "controlled-auth-revoke-wire"
        or prerequisites.get("remoteServiceQualification") is not False
        or prerequisites.get("authEndpoint") != "auth/v1/revoke"
        or set(prerequisites.get("contractFiles", {})) != contracts
        or any(inventory.get(path) != digest for path, digest in prerequisites["contractFiles"].items())):
        raise ValueError("Exact email session/Auth consumer contracts required")
    expected = {"owner": OWNER, "issuedBy": "019fc21e-50f0-7112-834f-9fb3b35b9dfe",
                "environment": "github-hosted-linux", "sliceKind": "email-change-session-v1",
                "acceptedBase": policy["acceptedBase"], "sourcePins": policy["sourcePins"],
                "sourceBindingSha256": policy["sourceBindingSha256"], "manifestSha256": policy["manifestSha256"],
                "producerPrerequisites": prerequisites, "sdkOwnerContextSha256": owner_digest,
                "allowedPhases": policy["allowedPhases"], "phases": policy["phases"],
                "transportSha": os.environ.get("WEB_REVIEWED_TRANSPORT_SHA"),
                "runId": os.environ.get("GITHUB_RUN_ID"), "runAttempt": os.environ.get("GITHUB_RUN_ATTEMPT")}
    if (not re.fullmatch(r"[0-9a-f]{40}", expected["transportSha"] or "")
        or any(not re.fullmatch(r"[1-9][0-9]*", expected[key] or "") for key in ("runId", "runAttempt"))):
        raise ValueError("Actual hosted email run/head association required")
    grant = intake.parse_json(raw)
    if creates_owner:
        expected["sdkOwnerEnrollment"] = enrollment
        if not re.fullmatch(r"[0-9a-f]{32}", grant.get("ownerNonce", "")):
            raise ValueError("Independent one-use SDK owner nonce required")
        expected["ownerNonce"] = grant["ownerNonce"]
    if set(grant) != set(expected) | {"startsUtc", "expiresUtc"} or any(grant[k] != value for k, value in expected.items()):
        raise ValueError("Email session scope/run/commands changed")
    start = dt.datetime.fromisoformat(grant["startsUtc"].replace("Z", "+00:00"))
    end = dt.datetime.fromisoformat(grant["expiresUtc"].replace("Z", "+00:00"))
    now = now or dt.datetime.now(dt.timezone.utc)
    if (start.utcoffset() != dt.timedelta(0) or end.utcoffset() != dt.timedelta(0)
        or not start <= now < end or not 0 < (end - start).total_seconds() <= 2100):
        raise ValueError("Finite nonrenewed email session grant required")
    if os.name != "posix":
        raise ValueError("Actual hosted Linux required")
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
    if not policy.get("nativeAdmissionSha256") or (policy.get("sliceKind") not in {"country-operation-v1", "account-failure-v1", "email-change-session-v1", "career-total-records-v1"} and not policy.get("customerLiteralProducerSha")):
        # Account scope has its own strict Auth/mail prerequisite validator; old branches are unchanged.
        raise ValueError("Root hosted permit and qualified Customer producer remain unpinned")
    census()
    raw = intake.fetch_blob(args.blob) if args.blob else args.permit.read_bytes()
    validate(policy, raw); census()
    if args.blob:
        if args.permit.exists(): raise ValueError("Fresh owned permit path required")
        args.permit.write_bytes(raw)
    print("Exact independently reviewed Root hosted permit and resource census verified")

if __name__ == "__main__": main()
