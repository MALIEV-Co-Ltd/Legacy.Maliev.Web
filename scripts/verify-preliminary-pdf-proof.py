#!/usr/bin/env python3
"""Root-selected test-only PDF proof. Install and native acceptance remain hosted-only.

No PDF rendering, network calls, or producer replacement. The public CLI isolates
untrusted PDF parsing in one owned child with a finite45-second watchdog.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timedelta, timezone
import hashlib
import io
import json
import logging
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import tempfile

MAX_PDF_BYTES = 16 * 1024 * 1024
MAX_MANIFEST_BYTES = 16 * 1024
WORKER_SECONDS = 45
WORKER_MEMORY_BYTES = 256 * 1024 * 1024
WORKER_CPU_SECONDS = 30
CASES = {2: 1, 10: 2}
SCHEMA = "preliminary-popup-pdf-v1"
BOUNDARY = "owned-program-kestrel-in-memory-upload-native-analysis-production-popup"
MANIFEST_KEYS = {
    "schema", "partCount", "minimumPages", "pdfFile", "byteLength", "sha256",
    "rawPageCount", "format", "printBackground", "fixtureBoundary",
}
PAGE_MARKER = re.compile(rb"/Type\s*/Page(?:\s|/|>)")


class ProofError(Exception):
    """Fixed diagnostic category only; never embed input content or identity."""

    def __init__(self, code: str):
        super().__init__(code)
        self.worker: dict[str, object] | None = None


def unique_object(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result = {}
    for key, value in pairs:
        if key in result:
            raise ProofError("duplicate-json-key")
        result[key] = value
    return result


def read_regular(path: Path, maximum: int) -> bytes:
    """Reject links/type/size before reading; check opened descriptor identity too."""
    try:
        before = path.lstat()
        if not stat.S_ISREG(before.st_mode) or before.st_size > maximum:
            raise ProofError("invalid-file-type-or-size")
        if path.is_symlink() or path.absolute() != path.resolve(strict=True):
            raise ProofError("indirect-file-path")
        descriptor = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
        with os.fdopen(descriptor, "rb") as stream:
            opened = os.fstat(stream.fileno())
            if not stat.S_ISREG(opened.st_mode) or opened.st_size > maximum:
                raise ProofError("invalid-opened-file")
            if (before.st_dev, before.st_ino) != (opened.st_dev, opened.st_ino):
                raise ProofError("changed-file-identity")
            data = stream.read(maximum + 1)
            after = os.fstat(stream.fileno())
        if len(data) > maximum or len(data) != before.st_size or after.st_size != before.st_size:
            raise ProofError("changed-file-size")
        return data
    except ProofError:
        raise
    except (OSError, ValueError, RuntimeError) as error:
        raise ProofError("unreadable-file") from error


def load_packet(root: Path) -> list[tuple[dict[str, object], bytes]]:
    """Validate every case's exact file and manifest boundary before parsing any PDF."""
    expected = {f"parts-{count:02}.{extension}" for count in CASES for extension in ("pdf", "json")}
    try:
        if root.is_symlink() or root.absolute() != root.resolve(strict=True) or not root.is_dir():
            raise ProofError("invalid-root")
        if {entry.name for entry in root.iterdir()} != expected:
            raise ProofError("unexpected-or-missing-case-files")
    except ProofError:
        raise
    except (OSError, ValueError, RuntimeError) as error:
        raise ProofError("unreadable-root") from error
    packet = []
    for count, floor in CASES.items():
        name = f"parts-{count:02}.pdf"
        try:
            manifest = json.loads(
                read_regular(root / f"parts-{count:02}.json", MAX_MANIFEST_BYTES).decode("utf-8"),
                object_pairs_hook=unique_object,
            )
        except ProofError:
            raise
        except (UnicodeError, ValueError, TypeError) as error:
            raise ProofError("invalid-manifest-json") from error
        if not isinstance(manifest, dict) or set(manifest) != MANIFEST_KEYS:
            raise ProofError("invalid-manifest-fields")
        exact = {"schema": SCHEMA, "partCount": count, "minimumPages": floor,
                 "pdfFile": name, "format": "A4", "printBackground": True, "fixtureBoundary": BOUNDARY}
        if any(type(manifest[key]) is not type(value) or manifest[key] != value for key, value in exact.items()):
            raise ProofError("invalid-case-contract")
        if type(manifest["byteLength"]) is not int or not 0 < manifest["byteLength"] <= MAX_PDF_BYTES:
            raise ProofError("invalid-manifest-length")
        if type(manifest["rawPageCount"]) is not int or manifest["rawPageCount"] < floor:
            raise ProofError("invalid-manifest-page-floor")
        digest = manifest["sha256"]
        if not isinstance(digest, str) or re.fullmatch(r"[0-9a-f]{64}", digest) is None:
            raise ProofError("invalid-manifest-digest")
        data = read_regular(root / name, MAX_PDF_BYTES)
        if len(data) != manifest["byteLength"] or hashlib.sha256(data).hexdigest() != digest:
            raise ProofError("bytes-do-not-match-manifest")
        if not data.startswith(b"%PDF-"):
            raise ProofError("invalid-pdf-signature")
        if len(PAGE_MARKER.findall(data)) != manifest["rawPageCount"]:
            raise ProofError("raw-page-count-mismatch")
        packet.append((manifest, data))
    return packet


def verify_packet(root: Path) -> dict[str, object]:
    packet = load_packet(root)
    # Imports only in the worker/explicit guards. No best-effort parser fallback.
    try:
        import pypdf
        if pypdf.__version__ != "6.19.0":
            raise ProofError("unexpected-parser-version")
        from pypdf import PdfReader
    except ImportError as error:
        raise ProofError("parser-unavailable") from error
    logging.disable(logging.CRITICAL)
    receipts = []
    for manifest, data in packet:
        try:
            reader = PdfReader(io.BytesIO(data), strict=True)
            if reader.is_encrypted:
                raise ProofError("encrypted-pdf")
            count = len(reader.pages)
            if count < manifest["minimumPages"]:
                raise ProofError("structural-page-floor-failed")
            # Force each page's basic type/boxes access; never execute embedded actions.
            for page in reader.pages:
                if page.get("/Type") != "/Page":
                    raise ProofError("invalid-page-type")
                _ = page.mediabox
        except ProofError:
            raise
        except Exception as error:
            raise ProofError("strict-pdf-parse-failed") from error
        receipts.append({
            "partCount": manifest["partCount"], "minimumPages": manifest["minimumPages"],
            "pdfFile": manifest["pdfFile"], "byteLength": len(data),
            "sha256": hashlib.sha256(data).hexdigest(),
            "rawPageCount": manifest["rawPageCount"], "structuralPageCount": count,
            "format": manifest["format"], "printBackground": manifest["printBackground"],
        })
    return {"schema": "preliminary-popup-pdf-verification-v1", "status": "passed",
            "parser": "pypdf", "parserVersion": "6.19.0", "strict": True, "cases": receipts}


def apply_worker_limits(resource_module=None, platform: str | None = None) -> dict[str, int]:
    """Ubuntu-only proposed boundary; fail before parser import if caps cannot apply."""
    if (sys.platform if platform is None else platform) != "linux":
        raise ProofError("worker-resource-caps-unavailable")
    try:
        if resource_module is None:
            import resource as resource_module
        for kind, maximum in (
            (resource_module.RLIMIT_AS, WORKER_MEMORY_BYTES),
            (resource_module.RLIMIT_CPU, WORKER_CPU_SECONDS),
        ):
            _, inherited_hard = resource_module.getrlimit(kind)
            ceiling = maximum if inherited_hard == resource_module.RLIM_INFINITY else min(maximum, inherited_hard)
            if ceiling <= 0:
                raise ProofError("worker-resource-caps-unavailable")
            resource_module.setrlimit(kind, (ceiling, ceiling))
            soft, hard = resource_module.getrlimit(kind)
            if not 0 < soft <= maximum or not 0 < hard <= maximum:
                raise ProofError("worker-resource-caps-unavailable")
        memory, _ = resource_module.getrlimit(resource_module.RLIMIT_AS)
        cpu, _ = resource_module.getrlimit(resource_module.RLIMIT_CPU)
        return {"addressSpaceBytes": memory, "cpuSeconds": cpu}
    except ProofError:
        raise
    except (ImportError, AttributeError, OSError, ValueError) as error:
        raise ProofError("worker-resource-caps-unavailable") from error


def proc_start_ticks(pid: int) -> str | None:
    try:
        # /proc field22, after the parenthesized comm field; exact child PID only.
        return (Path("/proc") / str(pid) / "stat").read_text().rsplit(")", 1)[1].split()[19]
    except (OSError, ValueError, IndexError):
        return None


def stop_owned_worker(process: subprocess.Popen, ownership: dict[str, object]) -> None:
    """Only the exact owned Popen handle, graceful2s then forced3s/reap."""
    if process.poll() is not None:
        ownership["cleanup"] = "observed-exited-and-reaped"
        ownership["exitCode"] = process.returncode
        return
    observed = ownership["procStartTicks"]
    current = proc_start_ticks(process.pid)
    if observed is not None and current is not None and observed != current:
        raise ProofError("worker-process-identity-changed")
    ownership["cleanup"] = "terminate-requested"
    process.terminate()
    try:
        process.communicate(timeout=2)
        ownership["cleanup"] = "graceful-exit-reaped"
    except subprocess.TimeoutExpired:
        if process.poll() is None:
            process.kill()
            ownership["cleanup"] = "exact-handle-killed"
        try:
            process.communicate(timeout=3)
        except subprocess.TimeoutExpired as error:
            ownership["cleanup"] = "reap-deadline-failed"
            raise ProofError("worker-cleanup-failed") from error
        ownership["cleanup"] = "forced-exit-reaped"
    ownership["exitCode"] = process.returncode


def run_worker(root: Path) -> dict[str, object]:
    if sys.platform != "linux":
        raise ProofError("worker-resource-caps-unavailable")
    started = datetime.now(timezone.utc)
    process = subprocess.Popen(
        [sys.executable, str(Path(__file__).resolve()), "--worker", "--root", str(root)],
        stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
    )
    ownership = {
        "owner": "verify-preliminary-pdf-proof", "pid": process.pid,
        "startedAtUtc": started.isoformat(),
        "expiryUtc": (started + timedelta(seconds=WORKER_SECONDS + 5)).isoformat(),
        "procStartTicks": proc_start_ticks(process.pid),
        "wallDeadlineSeconds": WORKER_SECONDS,
        "proposedAddressSpaceBytes": WORKER_MEMORY_BYTES,
        "proposedCpuSeconds": WORKER_CPU_SECONDS,
    }
    try:
        try:
            stdout, _ = process.communicate(timeout=WORKER_SECONDS)
        except subprocess.TimeoutExpired as error:
            raise ProofError("parser-watchdog-expired") from error
        if len(stdout) > 16384:
            raise ProofError("invalid-worker-receipt-size")
        try:
            result = json.loads(stdout.decode("utf-8"), object_pairs_hook=unique_object)
        except (UnicodeError, ValueError, TypeError) as error:
            raise ProofError("invalid-worker-receipt") from error
        if process.returncode != 0 or not isinstance(result, dict) or result.get("status") != "passed":
            code = result.get("errorCode") if isinstance(result, dict) else None
            raise ProofError(code if code in ERROR_CODES else "worker-failed")
        result["ownedWorker"] = ownership
        return result
    except ProofError as error:
        error.worker = ownership
        raise
    finally:
        try:
            stop_owned_worker(process, ownership)
        except ProofError as error:
            error.worker = ownership
            raise
        finally:
            if process.stdout is not None:
                process.stdout.close()


ERROR_CODES = {
    "duplicate-json-key", "invalid-file-type-or-size", "indirect-file-path", "invalid-opened-file",
    "changed-file-identity", "changed-file-size", "unreadable-file", "invalid-root",
    "unexpected-or-missing-case-files", "unreadable-root", "invalid-manifest-json",
    "invalid-manifest-fields", "invalid-case-contract", "invalid-manifest-length",
    "invalid-manifest-page-floor", "invalid-manifest-digest", "bytes-do-not-match-manifest",
    "invalid-pdf-signature", "raw-page-count-mismatch", "unexpected-parser-version",
    "parser-unavailable", "encrypted-pdf", "structural-page-floor-failed", "invalid-page-type",
    "strict-pdf-parse-failed", "parser-watchdog-expired", "invalid-worker-receipt-size",
    "invalid-worker-receipt", "worker-failed", "worker-resource-caps-unavailable",
    "worker-process-identity-changed", "worker-cleanup-failed",
}


def write_receipt(path: Path, result: dict[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=path.parent, delete=False) as stream:
            temporary = Path(stream.name)
            json.dump(result, stream, indent=2)
            stream.write("\n")
        os.replace(temporary, path)
    finally:
        if temporary is not None and temporary.exists():
            temporary.unlink()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--receipt", type=Path)
    parser.add_argument("--worker", action="store_true", help=argparse.SUPPRESS)
    args = parser.parse_args()
    if not args.worker and args.receipt is None:
        parser.error("--receipt is required")
    try:
        if sys.version_info < (3, 11):
            raise ProofError("unsupported-python-version")
        if args.worker:
            limits = apply_worker_limits()
            result = verify_packet(args.root)
            result["resourceLimits"] = limits
        else:
            if args.receipt.resolve().is_relative_to(args.root.resolve()):
                raise ProofError("receipt-inside-input-root")
            result = run_worker(args.root)
        status = 0
    except ProofError as error:
        result = {"schema": "preliminary-popup-pdf-verification-v1", "status": "failed", "errorCode": str(error)}
        if error.worker is not None:
            result["ownedWorker"] = error.worker
        status = 1
    except Exception:
        result = {"schema": "preliminary-popup-pdf-verification-v1", "status": "failed", "errorCode": "unexpected-boundary-failure"}
        status = 1
    if args.worker:
        print(json.dumps(result))
    else:
        write_receipt(args.receipt, result)
        print(json.dumps({"status": result["status"], "caseCount": len(result.get("cases", [])),
                          "errorCode": result.get("errorCode")}))
    return status


if __name__ == "__main__":
    raise SystemExit(main())
