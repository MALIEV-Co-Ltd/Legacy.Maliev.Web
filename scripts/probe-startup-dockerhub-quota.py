#!/usr/bin/env python3
"""Fixed original Docker Hub snapshot only; no pull, retry, credentials or route changes."""
import argparse
import datetime as dt
import email.utils
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import urllib.error
import urllib.request

IMAGE = "redis:8.4-alpine"
AUTH = "https://auth.docker.io/token?service=registry.docker.io&scope=repository%3Alibrary%2Fredis%3Apull"
MANIFEST = "https://registry-1.docker.io/v2/library/redis/manifests/8.4-alpine"
BODY_LIMIT = 16384
WORKER_SECONDS = 20
INSPECT_SECONDS = 3
LIMITATIONS = ["Snapshot only; not guaranteed subsequent SDK capacity or same quota bucket.",
               "No historical reset time inferred; subsequent SDK failure remains fatal; no retry.",
               "Default Missing cache policy applies only to this exact image; other images remain separate."]


def utc():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def empty(reason="Unknown"):
    return {"schema": 2, "image": IMAGE, "probeUtc": utc(), "serviceDateUtc": None,
            "authHttpStatus": None, "registryHttpStatus": None, "limit": None,
            "remaining": None, "windowSeconds": None, "retryAfter": None,
            "rateLimitReset": None, "cache": "NotChecked",
            "cacheInspection": {"category": "NotChecked", "exitCode": None}, "eligible": False,
            "reason": reason, "limitations": LIMITATIONS}


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def one(headers, name):
    values = headers.get_all(name, [])
    if len(values) > 1:
        raise ValueError("HeaderShape")
    return values[0] if values else None


def date(value):
    if value is None:
        return None
    if len(value) > 64:
        raise ValueError("HeaderShape")
    parsed = email.utils.parsedate_to_datetime(value)
    if parsed.tzinfo is None or parsed.utcoffset() != dt.timedelta(0):
        raise ValueError("HeaderShape")
    return parsed.astimezone(dt.timezone.utc).isoformat()


def quota(value):
    if value is None:
        return None
    match = re.fullmatch(r"([0-9]{1,9});w=([0-9]{1,9})", value.strip())
    if not match or int(match[2]) == 0:
        raise ValueError("HeaderShape")
    return int(match[1]), int(match[2])


def metadata(headers, result):
    result["serviceDateUtc"] = date(one(headers, "Date"))
    limit, remaining = quota(one(headers, "RateLimit-Limit")), quota(one(headers, "RateLimit-Remaining"))
    if limit is not None and remaining is not None:
        if limit[1] != remaining[1] or remaining[0] > limit[0]:
            raise ValueError("HeaderShape")
        result.update(limit=limit[0], remaining=remaining[0], windowSeconds=limit[1])
    elif limit is not None or remaining is not None:
        raise ValueError("HeaderShape")
    retry = one(headers, "Retry-After")
    if retry is not None:
        result["retryAfter"] = int(retry) if re.fullmatch(r"[0-9]{1,9}", retry) else date(retry)
    reset = one(headers, "RateLimit-Reset")
    if reset is not None:
        if not re.fullmatch(r"[0-9]{1,12}", reset):
            raise ValueError("HeaderShape")
        result["rateLimitReset"] = int(reset)  # Opaque numeric header, never a computed retry time.


def probe_http(opener=None):
    result = empty()
    # build_opener retains standard environment ProxyHandler and default TLS validation.
    opener = opener or urllib.request.build_opener(NoRedirect())
    try:
        request = urllib.request.Request(AUTH, method="GET", headers={"Accept": "application/json"})
        try:
            with opener.open(request, timeout=6) as response:
                result["authHttpStatus"] = response.status
                if response.status != 200:
                    result["reason"] = "AuthHttpFailure"
                    return result
                raw = response.read(BODY_LIMIT + 1)
        except urllib.error.HTTPError as error:
            result["authHttpStatus"] = error.code
            error.close()
            result["reason"] = "AuthHttpFailure"
            return result
        if len(raw) > BODY_LIMIT:
            result["reason"] = "AuthUnavailable"
            return result
        payload = json.loads(raw)
        token = payload.get("token") if isinstance(payload, dict) else None
        if not isinstance(token, str) or not re.fullmatch(r"[A-Za-z0-9_.-]{1,8192}", token):
            result["reason"] = "AuthUnavailable"
            return result
        request = urllib.request.Request(MANIFEST, method="HEAD", headers={
            "Authorization": "Bearer " + token,
            "Accept": "application/vnd.oci.image.index.v1+json,application/vnd.docker.distribution.manifest.list.v2+json,application/vnd.docker.distribution.manifest.v2+json"})
        try:
            with opener.open(request, timeout=6) as response:
                result["registryHttpStatus"] = response.status
                metadata(response.headers, result)
        except urllib.error.HTTPError as error:
            result["registryHttpStatus"] = error.code
            try:
                metadata(error.headers, result)
            finally:
                error.close()
        result["reason"] = "HttpFailure"
        if result["registryHttpStatus"] == 429:
            result["reason"] = "QuotaRejected"
        elif result["registryHttpStatus"] == 200:
            if result["remaining"] is None:
                result["reason"] = "QuotaUnknown"
            elif result["remaining"] == 0:
                result["reason"] = "QuotaExhausted"
            else:
                result.update(eligible=True, reason="QuotaAvailable")
    except (ValueError, TypeError, UnicodeError):
        result.update(eligible=False, reason="MalformedResponse")
    except (OSError, urllib.error.URLError):
        result.update(eligible=False, reason="TransportUnavailable")
    return result


def inspect_cache(run=subprocess.run):
    try:
        response = run(["docker", "image", "inspect", IMAGE, "--format", "{{.Id}}"],
                       capture_output=True, timeout=INSPECT_SECONDS, check=False)
        if type(response.returncode) is int and response.returncode == 0 and re.fullmatch(rb"sha256:[0-9a-f]{64}\r?\n?", response.stdout):
            return "Present", {"category": "ImagePresent", "exitCode": 0}
        # Docker CLI TemplateInspector.Flush emits one LF even for a missing image.
        # Permit only its empty/single-newline forms, never arbitrary stripped stdout.
        if type(response.returncode) is int and response.returncode == 1 and response.stdout in {b"", b"\n", b"\r\n"} and response.stderr.strip() in {
                b"Error response from daemon: No such image: redis:8.4-alpine",
                b"Error: No such image: redis:8.4-alpine"}:
            return "Absent", {"category": "ImageAbsent", "exitCode": 1}
        code = response.returncode if type(response.returncode) is int and -255 <= response.returncode <= 255 else None
        return "Unavailable", {"category": "UnrecognizedResponse", "exitCode": code}
    except subprocess.TimeoutExpired:
        return "Unavailable", {"category": "Timeout", "exitCode": None}
    except FileNotFoundError:
        return "Unavailable", {"category": "ExecutableUnavailable", "exitCode": None}
    except OSError:
        return "Unavailable", {"category": "InspectionOsError", "exitCode": None}


REASONS = {"Unknown", "AuthHttpFailure", "AuthUnavailable", "MalformedResponse", "TransportUnavailable",
           "HttpFailure", "QuotaRejected", "QuotaUnknown", "QuotaExhausted", "QuotaAvailable",
           "CacheAvailable", "CacheUnavailable", "WorkerUnavailable"}


def validate(result):
    if not isinstance(result, dict) or set(result) != set(empty()):
        raise ValueError("ReceiptShape")
    if type(result["schema"]) is not int or result["schema"] != 2 or result["image"] != IMAGE or result["limitations"] != LIMITATIONS:
        raise ValueError("ReceiptIdentity")
    if result["reason"] not in REASONS or result["cache"] not in {"NotChecked", "Absent", "Present", "Unavailable"} or type(result["eligible"]) is not bool:
        raise ValueError("ReceiptType")
    inspection = result["cacheInspection"]
    if not isinstance(inspection, dict) or set(inspection) != {"category", "exitCode"}:
        raise ValueError("InspectionShape")
    states = {"NotChecked": "NotChecked", "ImagePresent": "Present", "ImageAbsent": "Absent",
              "Timeout": "Unavailable", "ExecutableUnavailable": "Unavailable",
              "InspectionOsError": "Unavailable", "UnrecognizedResponse": "Unavailable"}
    category, code = inspection["category"], inspection["exitCode"]
    if not isinstance(category, str) or category not in states or states[category] != result["cache"]:
        raise ValueError("InspectionCategory")
    if code is not None and (type(code) is not int or not -255 <= code <= 255):
        raise ValueError("InspectionCode")
    if category == "ImagePresent" and code != 0 or category == "ImageAbsent" and code != 1:
        raise ValueError("InspectionIdentity")
    if category in {"NotChecked", "Timeout", "ExecutableUnavailable", "InspectionOsError"} and code is not None:
        raise ValueError("InspectionIdentity")
    for key in ("authHttpStatus", "registryHttpStatus"):
        if result[key] is not None and (type(result[key]) is not int or not 100 <= result[key] <= 599):
            raise ValueError("ReceiptStatus")
    for key in ("limit", "remaining", "windowSeconds", "rateLimitReset"):
        if result[key] is not None and (type(result[key]) is not int or not 0 <= result[key] < 10**12):
            raise ValueError("ReceiptNumber")
    for key in ("probeUtc", "serviceDateUtc"):
        if result[key] is None and key == "serviceDateUtc":
            continue
        if not isinstance(result[key], str) or len(result[key]) > 40 or dt.datetime.fromisoformat(result[key]).utcoffset() != dt.timedelta(0):
            raise ValueError("ReceiptDate")
    retry = result["retryAfter"]
    if retry is not None and not (type(retry) is int and 0 <= retry < 10**9):
        if not isinstance(retry, str) or len(retry) > 40 or dt.datetime.fromisoformat(retry).utcoffset() != dt.timedelta(0):
            raise ValueError("ReceiptRetry")
    no_http = all(result[key] is None for key in (
        "authHttpStatus", "registryHttpStatus", "serviceDateUtc", "limit", "remaining",
        "windowSeconds", "retryAfter", "rateLimitReset"))
    if result["cache"] == "Present":
        if result["reason"] != "CacheAvailable" or not result["eligible"] or not no_http:
            raise ValueError("CacheReceiptCoherence")
    elif result["cache"] == "Unavailable":
        if result["reason"] != "CacheUnavailable" or result["eligible"] or not no_http:
            raise ValueError("CacheReceiptCoherence")
    elif result["reason"] in {"CacheAvailable", "CacheUnavailable"}:
        raise ValueError("CacheReceiptCoherence")
    if result["reason"] == "QuotaAvailable" and not result["eligible"]:
        raise ValueError("ReceiptAdmission")
    if result["eligible"]:
        cache = result["reason"] == "CacheAvailable" and result["cache"] == "Present"
        available = (result["cache"] in {"NotChecked", "Absent"}
                     and result["reason"] == "QuotaAvailable" and result["authHttpStatus"] == 200
                     and result["registryHttpStatus"] == 200 and type(result["remaining"]) is int
                     and type(result["limit"]) is int and 0 < result["remaining"] <= result["limit"]
                     and type(result["windowSeconds"]) is int and result["windowSeconds"] > 0)
        if not cache and not available:
            raise ValueError("ReceiptAdmission")
    return result


def bounded_probe(run=subprocess.run):
    try:
        response = run([sys.executable, "-B", str(Path(__file__).resolve()), "--worker"],
                       stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=WORKER_SECONDS, check=False)
        if response.returncode == 0 and len(response.stdout) <= 8192:
            result = validate(json.loads(response.stdout))
            if result["cache"] == "NotChecked" and result["reason"] not in {"CacheAvailable", "CacheUnavailable"}:
                return result
    except (OSError, subprocess.TimeoutExpired, ValueError, TypeError, UnicodeError):
        pass
    return empty("WorkerUnavailable")


class Parser(argparse.ArgumentParser):
    def error(self, message):
        raise ValueError("InvalidArguments")


def main(argv=None, network=bounded_probe, cache=inspect_cache):
    argv = sys.argv[1:] if argv is None else argv
    if argv == ["--worker"]:
        # Only a bounded allowlisted receipt leaves this worker. Never print token/body/exception.
        try:
            result = validate(probe_http())
        except Exception:
            result = empty("WorkerUnavailable")
        sys.stdout.write(json.dumps(result))
        return 0
    parser = Parser(allow_abbrev=False)
    parser.add_argument("--receipt", required=True)
    parser.add_argument("--check-local-cache", action="store_true")
    try:
        args = parser.parse_args(argv)
        path = Path(args.receipt).absolute()
        if any(parent.is_symlink() for parent in path.parents):
            return 1
        # Reserve before any read: stale/concurrent receipts cannot start another attempt or overwrite evidence.
        fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    except (OSError, ValueError):
        return 1
    with os.fdopen(fd, "w", encoding="utf-8") as output:
        result = empty()
        try:
            state, inspection = cache() if args.check_local_cache else ("NotChecked", result["cacheInspection"])
            if state == "Present":
                result.update(cache=state, eligible=True, reason="CacheAvailable")
            elif state == "Unavailable" or state not in {"NotChecked", "Absent"}:
                result.update(cache="Unavailable", reason="CacheUnavailable")
            else:
                result = validate(network())
                result["cache"] = state
            result["cacheInspection"] = inspection
        except Exception:
            result = empty("WorkerUnavailable")
        output.write(json.dumps(validate(result), sort_keys=True) + "\n")
        output.flush()
        os.fsync(output.fileno())
    return 0 if result["eligible"] else 1


if __name__ == "__main__":
    try:
        code = main()
    except Exception:
        # A partial/empty exclusive receipt fails closed. Never expose IO paths or exception text.
        code = 1
    raise SystemExit(code)
