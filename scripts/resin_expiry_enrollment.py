"""Fixed Workflows98 custody for the two-row Resin expiry scope; no SDK actions."""
import argparse
import base64
import copy
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import time
import urllib.error
import urllib.request
import urllib.parse

import materialize_web_candidate as intake
import run_web_resin_comparison_expiry as scope

API = "https://api.github.com/repos/MALIEV-Co-Ltd/Legacy.Maliev.Workflows/issues"
PREFIX = "MALIEV_RESIN_EXPIRY_ENROLLMENT_V1\n"
MAX_RESPONSE = 131072
MAX_CONTEXT = 524288
CHALLENGE_SECONDS = 300
MATERIALIZE_RESERVE_SECONDS = 120
LAST_VERIFIED = None


def utcnow():
    return dt.datetime.now(dt.timezone.utc)


def canonical(record):
    return (json.dumps(record, sort_keys=True, separators=(",", ":")) + "\n").encode()


def instant(value):
    if not isinstance(value, str):
        raise ValueError("UTC instant string required")
    result = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    if result.utcoffset() != dt.timedelta(0):
        raise ValueError("UTC instant required")
    return result


def original_record(raw, limit=MAX_RESPONSE):
    if not isinstance(raw, bytes) or not 0 < len(raw) <= limit:
        raise ValueError("Bounded original record bytes required")
    value = intake.parse_json(raw)
    if not isinstance(value, dict):
        raise ValueError("Exact custody record object required")
    return value


def body_policy(policy=None):
    original = intake.load_policy(Path(__file__).with_name("web-resin-comparison-expiry-policy.json"))
    if policy is not None and canonical(policy) != canonical(original):
        raise ValueError("Caller policy differs from immutable source body")
    if original.get("sliceKind") != scope.SCOPE or original.get("nativeAdmissionSha256") is not None:
        raise ValueError("Exact separately enrolled Resin policy body required")
    return original


def custodian(policy):
    actor = policy.get("enrollmentCustodian")
    if (not isinstance(actor, dict) or set(actor) != {"login", "id", "type", "roleBindingSha256"}
        or not isinstance(actor["login"], str) or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9-]{0,38}", actor["login"])
        or type(actor["id"]) is not int or actor["id"] <= 0 or actor["type"] != "User"
        or not isinstance(actor["roleBindingSha256"], str) or not re.fullmatch(r"[0-9a-f]{64}", actor["roleBindingSha256"])):
        raise ValueError("Independent current custodian role/account binding remains unassigned")
    return actor


def issuer(policy):
    actor = custodian(policy)
    return {key: actor[key] for key in ("login", "id", "roleBindingSha256")}


def hosted_binding(policy):
    binding = {"transportSha": os.environ.get("WEB_REVIEWED_TRANSPORT_SHA"),
               "runId": os.environ.get("GITHUB_RUN_ID"), "runAttempt": os.environ.get("GITHUB_RUN_ATTEMPT")}
    if (os.environ.get("GITHUB_REPOSITORY") != "MALIEV-Co-Ltd/Legacy.Maliev.Web"
        or os.environ.get("GITHUB_EVENT_NAME") != "workflow_dispatch"
        or os.environ.get("GITHUB_REF") != "refs/heads/main"
        or binding["transportSha"] != os.environ.get("GITHUB_SHA")
        or not re.fullmatch(r"[0-9a-f]{40}", binding["transportSha"] or "")
        or any(not re.fullmatch(r"[1-9][0-9]*", binding[key] or "") for key in ("runId", "runAttempt"))):
        raise ValueError("Exact protected-main dispatch observation required")
    binding.update(schemaVersion=1, sliceKind=scope.SCOPE,
                   policySha256=intake.RESIN_EXPIRY_POLICY_SHA256,
                   sourceBindingSha256=policy["sourceBindingSha256"], manifestSha256=policy["manifestSha256"],
                   acceptedBase=scope.BASE, controlCommit=scope.CONTROL_SHA)
    return binding


def challenge(policy, now=None):
    import secrets
    policy = body_policy(policy)
    now = (now or utcnow()).replace(microsecond=0)
    record = hosted_binding(policy)
    record.update(ownerNonce=secrets.token_hex(16), createdUtc=now.isoformat(),
                  deadlineUtc=(now + dt.timedelta(seconds=CHALLENGE_SECONDS)).isoformat())
    return canonical(record)


def check_challenge(policy, raw, now, initial):
    record = original_record(raw)
    expected = hosted_binding(policy)
    if (set(record) != set(expected) | {"ownerNonce", "createdUtc", "deadlineUtc"}
        or any(type(record[key]) is not type(value) or record[key] != value for key, value in expected.items())
        or not isinstance(record["ownerNonce"], str) or not re.fullmatch(r"[0-9a-f]{32}", record["ownerNonce"])):
        raise ValueError("Exact original challenge source/run/nonce required")
    start, end = instant(record["createdUtc"]), instant(record["deadlineUtc"])
    if (end - start).total_seconds() != CHALLENGE_SECONDS or now < start or (initial and not now < end):
        raise ValueError("Original five-minute admission challenge exhausted")
    return record


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        raise ValueError("Custody redirects forbidden")


def api_read(url, deadline):
    # Only fixed issue/list/comment routes. Public read surface; no SDK token.
    list_pattern = re.escape(API) + r"/98/comments\?per_page=100&since=[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}%3A[0-9]{2}%3A[0-9]{2}Z"
    if not (re.fullmatch(list_pattern, url) or re.fullmatch(re.escape(API) + r"/comments/[1-9][0-9]*", url)):
        raise ValueError("Fixed Workflows98 custody endpoint required")
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise TimeoutError("Original custody read budget exhausted")
    request = urllib.request.Request(url, headers={"Accept": "application/vnd.github+json", "User-Agent": "maliev-resin-expiry-custody", "X-GitHub-Api-Version": "2022-11-28"})
    opener = urllib.request.build_opener(NoRedirect())
    with opener.open(request, timeout=min(5, remaining)) as response:
        if response.status != 200 or response.geturl() != url:
            raise ValueError("Exact successful custody origin required")
        raw = bytearray()
        while True:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise TimeoutError("Finite custody response deadline exhausted")
            if response.isclosed():
                if response.length not in (None, 0):
                    raise ValueError("Truncated custody response")
                break
            # A socket read is bounded by the remaining total budget, not only
            # the original open timeout. Chunk/cumulative caps are independent.
            response.fp.raw._sock.settimeout(min(5, remaining))
            chunk = response.read1(min(8192, MAX_RESPONSE + 1 - len(raw)))
            if time.monotonic() >= deadline:
                raise TimeoutError("Finite custody response deadline exhausted")
            if not chunk:
                break
            raw.extend(chunk)
            if len(raw) > MAX_RESPONSE:
                raise ValueError("Custody response byte cap exceeded")
        return bytes(raw)


def check_comment(policy, challenge_raw, comment_raw, now=None, initial=False):
    now = now or utcnow()
    actor = custodian(policy)
    pending = check_challenge(policy, challenge_raw, now, initial)
    comment = original_record(comment_raw)
    identity = comment.get("user", {})
    if (type(comment.get("id")) is not int or comment["id"] <= 0
        or comment.get("url") != API + "/comments/" + str(comment["id"])
        or comment.get("issue_url") != API + "/98"
        or identity.get("login") != actor["login"] or type(identity.get("id")) is not int
        or identity["id"] != actor["id"] or identity.get("type") != "User"
        or comment.get("created_at") != comment.get("updated_at")
        or not instant(pending["createdUtc"]) <= instant(comment.get("created_at")) <= now
        or instant(comment["created_at"]) >= instant(pending["deadlineUtc"])):
        raise ValueError("Independently bound Workflows98 author and matching original timestamps/body required")
    text = comment.get("body")
    if not isinstance(text, str) or not text.startswith(PREFIX):
        raise ValueError("Exact enrollment record prefix required")
    raw = text[len(PREFIX):].encode()
    enrollment = original_record(raw)
    expected = dict(hosted_binding(policy), ownerNonce=pending["ownerNonce"],
                    challengeSha256=intake.sha256(challenge_raw), deadlineUtc=pending["deadlineUtc"],
                    issuedBy=issuer(policy))
    if (raw != canonical(enrollment) or set(enrollment) != set(expected) | {"permitBlob", "permitSha256", "enrolledUtc", "permitExpiresUtc"}
        or any(canonical({key: enrollment[key]}) != canonical({key: value}) for key, value in expected.items())
        or not isinstance(enrollment["permitBlob"], str) or not re.fullmatch(r"[0-9a-f]{40}", enrollment["permitBlob"])
        or not isinstance(enrollment["permitSha256"], str) or not re.fullmatch(r"[0-9a-f]{64}", enrollment["permitSha256"])
        or not instant(pending["createdUtc"]) <= instant(enrollment["enrolledUtc"]) <= instant(comment["created_at"])
        or not 0 < (instant(enrollment["permitExpiresUtc"]) - instant(enrollment["enrolledUtc"])).total_seconds() <= 2100):
        raise ValueError("Exact canonical enrollment challenge/permit/source/finite lifetime required")
    return comment, enrollment


def private_validate(policy, challenge_raw, comment_raw, permit_raw, now, initial=False):
    comment, enrollment = check_comment(policy, challenge_raw, comment_raw, now, initial)
    if (intake.sha256(permit_raw) != enrollment["permitSha256"]
        or hashlib.sha1(b"blob " + str(len(permit_raw)).encode() + b"\0" + permit_raw).hexdigest() != enrollment["permitBlob"]):
        raise ValueError("Original same-repository Git blob/raw permit identity changed")
    grant = original_record(permit_raw)
    pending = original_record(challenge_raw)
    if (grant.get("ownerNonce") != pending["ownerNonce"] or grant.get("startsUtc") != enrollment["enrolledUtc"]
        or grant.get("expiresUtc") != enrollment["permitExpiresUtc"]):
        raise ValueError("Exact enrolled permit nonce/lifetime required")
    validated = copy.deepcopy(policy)
    validated["nativeAdmissionSha256"] = enrollment["permitSha256"]
    scope._validate_permit(validated, permit_raw, now)
    for key in ("phases", "allowedPhases", "sourcePins", "sdkOwnerEnrollment"):
        if canonical({key: grant.get(key)}) != canonical({key: policy[key]}):
            raise ValueError("Typed full permit source/phase/resource scope changed")
    return grant


def safe_read(path, limit):
    path = Path(path)
    if path.is_symlink() or not path.is_file():
        raise ValueError("Exact regular custody file required")
    with path.open("rb") as stream:
        raw = stream.read(limit + 1)
    if len(raw) > limit:
        raise ValueError("Custody file byte cap exceeded")
    return raw


def context_parts(context):
    if not isinstance(context, dict) or set(context) != {"schemaVersion", "challenge", "comment", "permit", "firstVerifiedUtc"} or type(context["schemaVersion"]) is not int or context["schemaVersion"] != 1:
        raise ValueError("Exact immutable custody context shape required")
    raw = []
    for name in ("challenge", "comment", "permit"):
        if not isinstance(context[name], str):
            raise ValueError("Original base64 custody bytes required")
        data = base64.b64decode(context[name], validate=True)
        if not data or len(data) > MAX_RESPONSE or base64.b64encode(data).decode() != context[name]:
            raise ValueError("Canonical bounded original custody bytes required")
        raw.append(data)
    return raw


def require_context(policy, permit_raw, now=None, initial=False):
    global LAST_VERIFIED
    LAST_VERIFIED = None
    clock_override = now
    policy = body_policy(policy)
    custodian(policy)  # Nullable principal rejects before network or actors.
    path = os.environ.get("RESIN_ENROLLMENT_CONTEXT")
    if not path:
        raise ValueError("Original enrolled context absent")
    context_raw = safe_read(path, MAX_CONTEXT)
    context = original_record(context_raw, MAX_CONTEXT)
    challenge_raw, comment_raw, retained_permit = context_parts(context)
    now = now or utcnow()
    first = instant(context["firstVerifiedUtc"])
    pending = check_challenge(policy, challenge_raw, now, initial)
    if not instant(pending["createdUtc"]) <= first < instant(pending["deadlineUtc"]) or first > now or retained_permit != permit_raw:
        raise ValueError("Original preallocation observation/permit custody changed")
    comment, _ = check_comment(policy, challenge_raw, comment_raw, now, initial)
    # Re-read exact retained object before EACH unit/SDK/phase admission. No
    # fallback after deletion/denial/edit or adoption of a new matching comment.
    actual = api_read(API + "/comments/" + str(comment["id"]), time.monotonic() + 5)
    if actual != comment_raw:
        raise ValueError("Original custody response/body changed or disappeared")
    observed = utcnow() if clock_override is None else clock_override
    # The optional 'now' argument is a source-only clock seam. Runtime always
    # observes AFTER the bounded origin read, including preallocation expiry.
    check_challenge(policy, challenge_raw, observed, initial)
    grant = private_validate(policy, challenge_raw, actual, permit_raw, observed, initial)
    if safe_read(path, MAX_CONTEXT) != context_raw:
        raise ValueError("Custody context changed during authority readback")
    LAST_VERIFIED = {"contextSha256": intake.sha256(context_raw), "challengeSha256": intake.sha256(challenge_raw),
                     "commentId": comment["id"], "commentSha256": intake.sha256(comment_raw),
                     "permitSha256": intake.sha256(permit_raw), "policySha256": intake.RESIN_EXPIRY_POLICY_SHA256,
                     "observedUtc": observed.isoformat(), "initial": initial}
    return grant


def last_receipt():
    if LAST_VERIFIED is None:
        raise ValueError("No actual custody observation for this actor admission")
    return copy.deepcopy(LAST_VERIFIED)


def original_challenge_deadline(policy, receipt):
    path = os.environ.get("RESIN_ENROLLMENT_CONTEXT")
    if not path:
        raise ValueError("Original enrolled context absent during history admission")
    raw = safe_read(path, MAX_CONTEXT)
    if intake.sha256(raw) != receipt["contextSha256"]:
        raise ValueError("History context differs from currently verified custody")
    challenge_raw, _, _ = context_parts(original_record(raw, MAX_CONTEXT))
    if intake.sha256(challenge_raw) != receipt["challengeSha256"]:
        raise ValueError("History challenge differs from currently verified custody")
    pending = check_challenge(policy, challenge_raw, instant(receipt["observedUtc"]), False)
    return instant(pending["deadlineUtc"])


def validate_final_context(policy, context_raw, admissions, live_origin=True):
    policy = body_policy(policy)
    custodian(policy)
    context = original_record(context_raw, MAX_CONTEXT)
    challenge_raw, comment_raw, permit_raw = context_parts(context)
    pending = original_record(challenge_raw)
    first = instant(context["firstVerifiedUtc"])
    check_challenge(policy, challenge_raw, first, True)
    original, _ = check_comment(policy, challenge_raw, comment_raw, first, True)
    if live_origin:
        actual = api_read(API + "/comments/" + str(original["id"]), time.monotonic() + 5)
        if actual != comment_raw:
            raise ValueError("Original final custody response/body changed or disappeared")
    expected = {"contextSha256": intake.sha256(context_raw), "challengeSha256": intake.sha256(challenge_raw),
                "commentId": original["id"], "commentSha256": intake.sha256(comment_raw),
                "permitSha256": intake.sha256(permit_raw), "policySha256": intake.RESIN_EXPIRY_POLICY_SHA256}
    if len(admissions) != 1 + len(scope.expected_phases()):
        raise ValueError("Actual unit and all exact phase admission observations required")
    previous = first
    for index, receipt in enumerate(admissions):
        if (not isinstance(receipt, dict) or set(receipt) != set(expected) | {"observedUtc", "initial"}
            or any(type(receipt[key]) is not type(value) or receipt[key] != value for key, value in expected.items())
            or type(receipt["initial"]) is not bool or receipt["initial"] is not (index in (0, 1))):
            raise ValueError("Exact original unit/SDK/phase custody receipt join required")
        observed = instant(receipt["observedUtc"])
        if observed < previous:
            raise ValueError("Original admission observations reversed")
        private_validate(policy, challenge_raw, comment_raw, permit_raw, observed, index in (0, 1))
        previous = observed
    return {**expected, "finalOriginVerified": live_origin, "originalAdmissionTimesVerified": True}


def wait_for_enrollment(policy, challenge_raw, context_path, permit_path):
    policy = body_policy(policy)
    custodian(policy)  # Unassigned observed account never becomes authority.
    pending = check_challenge(policy, challenge_raw, utcnow(), True)
    stop_utc = instant(pending["deadlineUtc"]) - dt.timedelta(seconds=MATERIALIZE_RESERVE_SECONDS)
    remaining = (stop_utc - utcnow()).total_seconds()
    if remaining <= 0:
        raise TimeoutError("Original admission reserve exhausted before custody read")
    deadline = time.monotonic() + remaining
    while time.monotonic() < deadline:
        since = instant(pending["createdUtc"]).strftime("%Y-%m-%dT%H:%M:%SZ")
        list_url = API + "/98/comments?per_page=100&since=" + urllib.parse.quote(since, safe="")
        raw = api_read(list_url, min(deadline, time.monotonic() + 5))
        values = intake.parse_json(raw)
        if not isinstance(values, list) or len(values) > 100:
            raise ValueError("Bounded issue custody comment page required")
        # The fixed original challenge 'since' excludes historical issue rows.
        # Never silently accept a potentially truncated new observation cohort.
        if len(values) == 100:
            raise ValueError("Original since cohort reached fixed comment inventory bound")
        candidates = []
        for value in values:
            text = value.get("body") if isinstance(value, dict) else None
            if isinstance(text, str) and text.startswith(PREFIX):
                record = original_record(text[len(PREFIX):].encode())
                if record.get("challengeSha256") == intake.sha256(challenge_raw):
                    candidates.append(value)
        if len(candidates) > 1:
            raise ValueError("Ambiguous enrollment for original challenge")
        if candidates:
            identifier = candidates[0].get("id")
            if type(identifier) is not int or identifier <= 0:
                raise ValueError("Original comment identity required")
            comment_raw = api_read(API + "/comments/" + str(identifier), min(deadline, time.monotonic() + 5))
            _, enrollment = check_comment(policy, challenge_raw, comment_raw, utcnow(), True)
            permit_raw = intake.fetch_blob(enrollment["permitBlob"])
            now = utcnow()
            private_validate(policy, challenge_raw, comment_raw, permit_raw, now, True)
            if now >= stop_utc:
                raise TimeoutError("Original admission materialization reserve exhausted")
            context = {"schemaVersion": 1, "challenge": base64.b64encode(challenge_raw).decode(),
                       "comment": base64.b64encode(comment_raw).decode(), "permit": base64.b64encode(permit_raw).decode(),
                       "firstVerifiedUtc": now.isoformat()}
            for path, raw in ((Path(context_path), canonical(context)), (Path(permit_path), permit_raw)):
                with path.open("xb") as stream:
                    stream.write(raw)
            return
        time.sleep(min(10, max(0, deadline - time.monotonic())))
    raise TimeoutError("No enrolled independent custodian before original admission reserve")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--challenge", type=Path, required=True)
    parser.add_argument("--wait", action="store_true")
    parser.add_argument("--context", type=Path)
    parser.add_argument("--permit", type=Path)
    args = parser.parse_args()
    policy = body_policy()
    if args.wait:
        if args.context is None or args.permit is None:
            raise ValueError("Owned context and original permit paths required")
        wait_for_enrollment(policy, safe_read(args.challenge, MAX_RESPONSE), args.context, args.permit)
    else:
        with args.challenge.open("xb") as stream:
            stream.write(challenge(policy))


if __name__ == "__main__":
    main()
