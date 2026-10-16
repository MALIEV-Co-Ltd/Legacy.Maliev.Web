#!/usr/bin/env python3
"""Read-only, bounded hosted-runner resource receipts; never test acceptance."""
import argparse
import json
import os
import resource
import select
import signal
import time
from pathlib import Path

MAX_INPUT = 8 * 1024 * 1024
MAX_RECEIPT = 256 * 1024
MAX_PROCESSES = 1024
MAX_ENTRIES = 4096
LEASE = 125 * 60
PERIOD = 10 * 60
COUNTS = {"dotnet", "testhost", "node", "chrome", "chromium", "redis-server", "postgres"}
MEMORY = {"MemTotal", "MemAvailable", "SwapTotal", "SwapFree"}
EVENTS = {"low", "high", "max", "oom", "oom_kill", "oom_group_kill"}
stopping = False

def require(value):
    if not value:
        raise RuntimeError("Hosted resource receipt rejected")

def encoded(value):
    data = (json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n").encode()
    require(len(data) <= MAX_RECEIPT)
    return data

def write_new(path, value):
    with path.open("xb") as stream:
        stream.write(encoded(value))

def atomic(path, value):
    temporary = path.with_name(path.name + ".tmp")
    try:
        with temporary.open("xb") as stream:
            stream.write(encoded(value))
        os.replace(temporary, path)
    finally:
        if temporary.exists():
            temporary.unlink()

def integer(value):
    require(isinstance(value, str) and value.isascii() and value.isdecimal() and len(value) <= 19)
    number = int(value)
    require(0 <= number <= 9223372036854775807)
    return number

class Inputs:
    def __init__(self, deadline):
        self.deadline = deadline
        self.remaining = MAX_INPUT
        self.unavailable = 0
        self.exceeded = False

    def read(self, path, maximum=4096):
        if time.monotonic() >= self.deadline or self.remaining <= 0:
            self.exceeded = True
            return None
        limit = min(maximum, self.remaining)
        try:
            with path.open("rb") as stream:
                data = stream.read(limit + 1)
            self.remaining -= min(len(data), limit)
            if len(data) > limit:
                self.exceeded = True
                return None
            return data.decode("ascii", errors="strict")
        except (OSError, UnicodeError):
            self.unavailable += 1
            return None

def memory(text):
    values = {}
    if text is None:
        return None
    for line in text.splitlines():
        parts = line.split()
        key = parts[0].rstrip(":") if parts else ""
        if key in MEMORY and len(parts) == 3 and parts[2] == "kB":
            values[key] = integer(parts[1]) * 1024
    return values or None

def events(text):
    if text is None:
        return None
    result = {}
    for line in text.splitlines():
        parts = line.split()
        if len(parts) == 2 and parts[0] in EVENTS:
            result[parts[0]] = integer(parts[1])
    return result or None

def bounded_read(path, maximum=4096):
    with path.open("rb") as stream:
        data = stream.read(maximum + 1)
    require(len(data) <= maximum)
    return data

def proc_identity(pid, proc=Path("/proc")):
    stat = bounded_read(proc / str(pid) / "stat").decode("ascii")
    tail = stat[stat.rfind(")") + 2:].split()
    executable = (proc / str(pid) / "exe").stat()
    return {"pid": pid, "start_ticks": integer(tail[19]),
            "exe_device": executable.st_dev, "exe_inode": executable.st_ino}

def processes(source, proc=Path("/proc")):
    counts = {name: 0 for name in sorted(COUNTS)}
    total = scanned = 0
    capped = False
    try:
        with os.scandir(proc) as entries:
            for entry in entries:
                scanned += 1
                if scanned > MAX_ENTRIES:
                    capped = True
                    break
                if not entry.name.isascii() or not entry.name.isdecimal():
                    continue
                total += 1
                if total > MAX_PROCESSES:
                    capped = True
                    total = MAX_PROCESSES
                    break
                name = source.read(proc / entry.name / "comm", 128)
                if name is not None and name.strip() in counts:
                    counts[name.strip()] += 1
                if source.exceeded:
                    capped = True
                    break
    except OSError:
        return {"available": False}
    return {"available": True, "observed_processes": total, "entries_examined": min(scanned, MAX_ENTRIES),
            "enumeration_capped": capped, "fixed_name_counts": counts,
            "testhost_named_count_is_not_all_dotnet_testhosts": True}

def disk(path):
    try:
        value = os.statvfs(path)
        return {"free_bytes": value.f_bavail * value.f_frsize, "free_inodes": value.f_favail}
    except OSError:
        return None

def cgroup(source, proc=Path("/proc"), base=Path("/sys/fs/cgroup")):
    membership = source.read(proc / "self/cgroup")
    if membership is None:
        return None
    candidates = [line[3:] for line in membership.splitlines() if line.startswith("0::")]
    if len(candidates) != 1:
        return None
    relative = Path(candidates[0].lstrip("/"))
    if relative.is_absolute() or ".." in relative.parts:
        return None
    directory = base / relative
    if not directory.resolve().is_relative_to(base.resolve()):
        return None
    result = {}
    for key in ("memory.current", "memory.peak", "memory.max", "pids.current", "pids.max"):
        raw = source.read(directory / key, 128)
        if raw is None:
            result[key] = None
        elif raw.strip() == "max":
            result[key] = "unbounded"
        else:
            result[key] = integer(raw.strip())
    result["memory.events"] = events(source.read(directory / "memory.events"))
    return result

def snapshot(workspace, temporary, source):
    return {"memory_bytes": memory(source.read(Path("/proc/meminfo"), 16384)),
            "cgroup_v2": cgroup(source), "processes": processes(source),
            "disk": {"workspace": disk(workspace), "temporary": disk(temporary)}}

def owner():
    values = {key: os.environ[key] for key in
              ("GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "GITHUB_SHA", "GITHUB_JOB", "MALIEV_BILLING_CANDIDATE_HEAD", "RESOURCE_CHECKPOINT_SCOPE")}
    require(0 < integer(values["GITHUB_RUN_ID"]))
    require(0 < integer(values["GITHUB_RUN_ATTEMPT"]))
    require(len(values["GITHUB_SHA"]) == 40 and all(c in "0123456789abcdef" for c in values["GITHUB_SHA"]))
    require(len(values["MALIEV_BILLING_CANDIDATE_HEAD"]) == 40 and all(c in "0123456789abcdef" for c in values["MALIEV_BILLING_CANDIDATE_HEAD"]))
    scopes = {"validate": {"full"}, "resource_checkpoint_qualification": {"qualification-complete", "qualification-live"}}
    require(values["GITHUB_JOB"] in scopes and values["RESOURCE_CHECKPOINT_SCOPE"] in scopes[values["GITHUB_JOB"]])
    return values

def directory(path, identity):
    temporary = Path(os.environ["RUNNER_TEMP"]).resolve(strict=True)
    expected = temporary / ("web-validation-resources-" + identity["GITHUB_RUN_ID"] + "-" + identity["GITHUB_RUN_ATTEMPT"] + "-" + identity["RESOURCE_CHECKPOINT_SCOPE"])
    require(path == expected and not path.is_symlink())
    if path.exists():
        require(path.is_dir() and path.resolve().parent == temporary)
    return temporary

def sample(path, phase):
    global stopping
    resource.setrlimit(resource.RLIMIT_AS, (256 * 1024 * 1024, 256 * 1024 * 1024))
    resource.setrlimit(resource.RLIMIT_CPU, (20, 20))
    identity = owner()
    temporary = directory(path, identity)
    require(0 <= phase <= 12)
    path.mkdir(exist_ok=True)
    lease_path = path / "lease.json"
    if phase == 0:
        write_new(lease_path, {"owner": identity, "origin": time.monotonic(), "expires": time.time() + LEASE})
    require(lease_path.is_file() and not lease_path.is_symlink() and lease_path.stat().st_size <= 4096)
    lease = json.loads(bounded_read(lease_path))
    require(lease["owner"] == identity and time.time() < lease["expires"])
    expected = lease["origin"] + phase * PERIOD
    process = proc_identity(os.getpid())
    ledger = {"owner": identity, **process, "phase": phase, "expires": lease["expires"], "state": "running"}
    atomic(path / "worker.json", ledger)
    def stop(_signal, _frame):
        global stopping
        stopping = True
    signal.signal(signal.SIGTERM, stop)
    signal.signal(signal.SIGINT, stop)
    try:
        while time.monotonic() < expected:
            if stopping or time.time() >= lease["expires"]:
                return
            time.sleep(min(1, max(0, expected - time.monotonic())))
        if stopping:
            return
        source = Inputs(time.monotonic() + 30)
        value = snapshot(Path(os.environ["GITHUB_WORKSPACE"]).resolve(strict=True), temporary, source)
        receipt = {"schema": "web-hosted-resource-checkpoint.v1", "owner": identity,
                   "phase": phase, "elapsed_seconds": round(time.monotonic() - lease["origin"], 3),
                   "worker": process, "lease_expires": lease["expires"], "resources": value,
                   "input_bytes": MAX_INPUT - source.remaining, "unavailable_reads": source.unavailable,
                   "input_or_time_cap_observed": source.exceeded,
                   "scope": "read-only aggregate resource observation; no test acceptance or cause verdict"}
        write_new(path / ("sample-" + str(phase).zfill(2) + ".json"), receipt)
    finally:
        ledger["state"] = "exited"
        atomic(path / "worker.json", ledger)

def stop_exact(ledger, inspect, open_handle, send, wait, close):
    require(ledger.get("state") in ("running", "exited"))
    expected = {key: ledger[key] for key in ("pid", "start_ticks", "exe_device", "exe_inode")}
    if ledger["state"] == "exited":
        try:
            actual = inspect(ledger["pid"])
        except FileNotFoundError:
            return "exited"
        if actual != expected:
            return "exited-pid-reused"
    try:
        handle = open_handle(ledger["pid"])
    except ProcessLookupError:
        return "absent"
    try:
        try:
            require(inspect(ledger["pid"]) == expected)
        except FileNotFoundError:
            return "absent"
        try:
            send(handle, signal.SIGTERM)
        except ProcessLookupError:
            return "absent"
        if not wait(handle, 10):
            try:
                require(inspect(ledger["pid"]) == expected)
                send(handle, signal.SIGKILL)
            except (FileNotFoundError, ProcessLookupError):
                return "absent"
            require(wait(handle, 5))
        return "stopped"
    finally:
        close(handle)

def cleanup(path):
    identity = owner()
    directory(path, identity)
    ledger_path = path / "worker.json"
    if not ledger_path.exists():
        return
    require(not ledger_path.is_symlink() and ledger_path.stat().st_size <= 4096)
    ledger = json.loads(bounded_read(ledger_path))
    require(ledger["owner"] == identity and isinstance(ledger["pid"], int) and ledger["pid"] > 1 and ledger["pid"] != os.getpid())
    require(hasattr(os, "pidfd_open") and hasattr(signal, "pidfd_send_signal"))
    outcome = stop_exact(ledger, proc_identity, lambda pid: os.pidfd_open(pid),
                         lambda handle, sig: signal.pidfd_send_signal(handle, sig),
                         lambda handle, seconds: bool(select.select([handle], [], [], seconds)[0]), os.close)
    write_new(path / "cleanup.json", {"schema": "web-hosted-resource-cleanup.v1", "owner": identity, "outcome": outcome})

def wait_worker(path, phase):
    identity = owner()
    directory(path, identity)
    require(identity["GITHUB_JOB"] == "resource_checkpoint_qualification" and phase == 1)
    deadline = time.monotonic() + 90
    while time.monotonic() < deadline:
        ledger_path = path / "worker.json"
        if ledger_path.is_file() and not ledger_path.is_symlink():
            ledger = json.loads(bounded_read(ledger_path))
            require(ledger["owner"] == identity)
            if ledger.get("phase") == phase and ledger.get("state") == "running":
                require(proc_identity(ledger["pid"]) == {
                    key: ledger[key] for key in ("pid", "start_ticks", "exe_device", "exe_inode")})
                return
        time.sleep(0.1)
    raise RuntimeError("Hosted background live-worker qualification was not observed")

def verify_released(path):
    identity = owner()
    directory(path, identity)
    require(identity["GITHUB_JOB"] == "resource_checkpoint_qualification")
    ledger_path = path / "worker.json"
    require(ledger_path.is_file() and not ledger_path.is_symlink())
    ledger = json.loads(bounded_read(ledger_path))
    require(ledger["owner"] == identity and isinstance(ledger["pid"], int) and ledger["pid"] > 1)
    expected = {key: ledger[key] for key in ("pid", "start_ticks", "exe_device", "exe_inode")}
    deadline = time.monotonic() + 15
    while time.monotonic() < deadline:
        try:
            if proc_identity(ledger["pid"]) != expected:
                return
        except FileNotFoundError:
            return
        time.sleep(0.1)
    raise RuntimeError("Managed cancellation left the exact qualification worker running")

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("sample", "cleanup", "wait", "released"))
    parser.add_argument("--directory", required=True, type=Path)
    parser.add_argument("--phase", type=int, default=0)
    args = parser.parse_args()
    if args.mode == "sample":
        sample(args.directory, args.phase)
    elif args.mode == "cleanup":
        cleanup(args.directory)
    elif args.mode == "wait":
        wait_worker(args.directory, args.phase)
    else:
        verify_released(args.directory)
