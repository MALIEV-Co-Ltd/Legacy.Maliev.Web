"""Finite numeric observation of the existing full-Test shell; never runs tests."""
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import signal
import stat
import sys
import threading
import time

# Rendered from dotnet-validate e5732037... action.yml Test scalar, solution substituted.
# Raw run40183081238 lines4478–4481 independently confirm all four command lines.
TEST_SCRIPT = (b'if [[ "$USE_LOCAL_MALIEV_DEPENDENCIES" == "true" ]]; then\n'
               b'  export GITHUB_ACTIONS=false\nfi\n'
               b'dotnet test "Legacy.Maliev.Web.slnx" --configuration Release --no-build --no-restore\n')
TEST_SHA = "d282675eaa0a19f988e9034aa28992cda1ce3e1faa99fe5a8768b4b534ed2ff3"
LIMIT = 2**63 - 1
IDENTITY_KEYS = ("pid", "start", "dev", "ino")


def read_small(path, limit=4096):
    with open(path, "rb", buffering=0) as handle:
        data = handle.read(limit + 1)
    if len(data) > limit:
        raise ValueError("size")
    return data.decode("ascii", errors="strict")


def matches_script(path, temporary):
    try:
        path, temporary = Path(path), Path(temporary).resolve(strict=True)
        if path.is_symlink() or not path.resolve(strict=True).is_relative_to(temporary):
            return False
        info = path.stat()
        if not stat.S_ISREG(info.st_mode) or info.st_size != len(TEST_SCRIPT):
            return False
        return hashlib.sha256(path.read_bytes()).hexdigest() == TEST_SHA
    except (OSError, ValueError):
        return False


def numbers(text, allowed):
    result = {}
    for line in text.splitlines():
        match = re.fullmatch(r"([A-Za-z_]+):?\s+([0-9]{1,19})( kB)?", line.strip())
        if match and match[1] in allowed:
            value = int(match[2]) * (1024 if match[3] else 1)
            if value <= LIMIT:
                result[match[1]] = value
    return result


def encode_record(row):
    def check(value):
        if isinstance(value, dict):
            if any(not isinstance(k, str) or not re.fullmatch(r"[A-Za-z_]+", k) for k in value):
                raise ValueError("key")
            for item in value.values():
                check(item)
        elif isinstance(value, list):
            for item in value:
                check(item)
        elif type(value) is not int or not 0 <= value <= LIMIT:
            raise ValueError("number")
    check(row)
    data = (json.dumps(row, separators=(",", ":"), allow_nan=False) + "\n").encode("ascii")
    if len(data) > 4096:
        raise ValueError("record")
    return data


def identity(pid):
    # None means verified missing/exited; {} means unknown and never authorizes cleanup/signals.
    try:
        text = read_small(Path("/proc") / str(int(pid)) / "stat")
    except FileNotFoundError:
        return None
    except (OSError, ValueError):
        return {}
    try:
        if not text.startswith(str(int(pid)) + " ("):
            return {}
        fields = text[text.rindex(")") + 2:].split()
        if fields[0] in ("Z", "X"):
            return None
        executable = os.stat(Path("/proc") / str(int(pid)) / "exe")
        return {"pid": int(pid), "start": int(fields[19]), "dev": executable.st_dev,
                "ino": executable.st_ino, "ppid": int(fields[1])}
    except (OSError, ValueError, IndexError):
        return {}


def same_identity(expected, actual):
    return (isinstance(expected, dict) and isinstance(actual, dict)
            and all(type(expected.get(k)) is int and expected[k] == actual.get(k) for k in IDENTITY_KEYS))


def verified_absence(expected, current):
    # An executable transition within the same process birth is still live, not an exit.
    return current is None or (same_identity(expected, expected) and same_identity(current, current)
                               and any(expected[k] != current[k] for k in ("pid", "start")))


def python_identity():
    info = os.stat("/proc/self/exe")
    return {"dev": info.st_dev, "ino": info.st_ino}


def verified_monitor(ledger, parent_pid, monitor_pid):
    parent, monitor = identity(parent_pid), identity(monitor_pid)
    executable = python_identity()
    return (monitor_pid not in (parent_pid, os.getpid())
            and ledger["monitor"]["pid"] == monitor_pid
            and same_identity(ledger["parent"], parent)
            and same_identity(ledger["monitor"], monitor)
            and monitor.get("ppid") == parent_pid
            and ledger["monitor"].get("ppid") == parent_pid
            and ledger["python"] == executable
            and all(monitor[k] == executable[k] for k in ("dev", "ino")))


def descendants(anchor):
    result, pending, seen, omitted = [], [anchor], {anchor["pid"]}, 0
    while pending and len(result) < 64:
        parent = pending.pop(0)
        if not same_identity(parent, identity(parent["pid"])):
            continue
        try:
            children = read_small(Path("/proc") / str(parent["pid"]) / "task" / str(parent["pid"]) / "children")
        except (OSError, ValueError):
            omitted = 1
            continue
        for token in children.split():
            if not token.isascii() or not token.isdecimal() or len(token) > 10:
                continue
            pid = int(token)
            if pid in seen:
                continue
            seen.add(pid)
            child = identity(pid)
            if not child or child["ppid"] != parent["pid"]:
                continue
            if len(result) == 64:
                omitted = 1
                continue
            result.append(child)
            pending.append(child)
    if pending:
        omitted = 1
    return result, omitted


def cgroup_relative(text):
    for line in text.splitlines():
        if line.startswith("0::/"):
            relative = line[4:]
            if ".." in PurePosixPath(relative).parts or relative.startswith("/"):
                raise ValueError("membership")
            return relative
    raise ValueError("membership")


def cgroup_directory(anchor):
    root = Path("/sys/fs/cgroup").resolve(strict=True)
    relative = cgroup_relative(read_small(Path("/proc") / str(anchor["pid"]) / "cgroup"))
    directory = (root / relative).resolve(strict=True)
    if not directory.is_relative_to(root):
        raise ValueError("membership")
    return directory


def sample(anchor, workspace, temporary, sequence):
    row = {"event": 2, "sequence": sequence, "monotonic_ns": time.monotonic_ns(), "flags": 0}
    try:
        row["memory"] = numbers(read_small("/proc/meminfo", 16384), {"MemAvailable", "SwapFree"})
        row["host"] = numbers(read_small("/proc/vmstat", 32768), {"oom_kill"})
        group = cgroup_directory(anchor)
        row["cgroup"] = {}
        for filename, key in (("memory.current", "current"), ("memory.max", "maximum")):
            value = read_small(group / filename).strip()
            row["cgroup"][key] = LIMIT if value == "max" else int(value)
            if value == "max":
                row["flags"] |= 8  # LIMIT is the explicitly unbounded cgroup sentinel.
        for filename, key in (("memory.events", "events"), ("memory.events.local", "local")):
            row["cgroup"][key] = numbers(read_small(group / filename), {"low", "high", "max", "oom", "oom_kill", "oom_group_kill"})
    except (OSError, ValueError):
        row["flags"] |= 1
    for directory, key in ((workspace, "workspace"), (temporary, "temporary")):
        try:
            disk = os.statvfs(directory)
            row[key] = {"bytes": disk.f_bavail * disk.f_frsize, "inodes": disk.f_favail}
        except OSError:
            row["flags"] |= 2
    children, omitted = descendants(anchor)
    totals = {"rss": 0, "threads": 0, "count": 0}
    totals.update(rss_known=0, threads_known=0)
    shown = []
    for child in children:
        try:
            values = numbers(read_small(Path("/proc") / str(child["pid"]) / "status"), {"VmRSS", "VmHWM", "Threads"})
            current = identity(child["pid"])
            if (not same_identity(child, current) or current["ppid"] != child["ppid"]
                    or not same_identity(anchor, identity(anchor["pid"]))):
                continue
            for source, target in (("VmRSS", "rss"), ("Threads", "threads")):
                if source in values:
                    totals[target] += values[source]
                    totals[target + "_known"] += 1
                else:
                    row["flags"] |= 16  # A partial sum is not a zero-valued missing measurement.
            totals["count"] += 1
            if len(shown) < 8:
                shown.append({"pid": child["pid"], "start": child["start"], **values})
        except (OSError, ValueError):
            row["flags"] |= 4
    row.update(owned=totals, shown=shown, omitted=omitted)
    row["observer"] = numbers(read_small("/proc/self/status"), {"VmRSS", "VmHWM", "Threads"})
    return row


def private_directory(directory):
    info = directory.lstat()
    if (not stat.S_ISDIR(info.st_mode) or directory.is_symlink() or info.st_uid != os.getuid()
            or stat.S_IMODE(info.st_mode) != 0o700):
        raise ValueError("ownership")


def write_new(path, row):
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    try:
        os.write(fd, encode_record(row))
    finally:
        os.close(fd)


def read_owned(path, limit):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
    try:
        info = os.fstat(fd)
        if (not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid()
                or stat.S_IMODE(info.st_mode) != 0o600 or info.st_size > limit):
            raise ValueError("file")
        data = os.read(fd, limit + 1)
        if len(data) > limit:
            raise ValueError("file")
        return data
    finally:
        os.close(fd)


def read_owned_json(path):
    row = json.loads(read_owned(path, 4096))
    encode_record(row)
    return row


def owner_labels():
    result = {}
    for name, key in (("GITHUB_RUN_ID", "run"), ("GITHUB_RUN_ATTEMPT", "attempt")):
        value = os.environ.get(name, "0")
        if not value.isascii() or not value.isdecimal() or len(value) >= 19:
            raise ValueError("owner")
        result[key] = int(value)
    return result


def load_ledger(directory):
    private_directory(directory)
    ledger = read_owned_json(directory / "owner.json")
    if (ledger["owner"] != 723 or ledger["python"] != python_identity()
            or not same_identity(ledger["parent"], ledger["parent"])
            or not same_identity(ledger["monitor"], ledger["monitor"])
            or ledger["monitor"]["pid"] == ledger["parent"]["pid"]
            or ledger["monitor"]["ppid"] != ledger["parent"]["pid"]
            or any(ledger["monitor"][k] != ledger["python"][k] for k in ("dev", "ino"))
            or not 0 < ledger["born_ns"] <= time.monotonic_ns()
            or not 0 < ledger["lease_ns"] <= 3600 * 10**9
            or ledger["memory_limit"] != 64 * 1024**2 or ledger["cpu_seconds"] != 15
            or any(ledger[k] != v for k, v in owner_labels().items())):
        raise ValueError("ledger")
    return ledger


def receipt(ledger):
    return {"owner": 723, "parent": ledger["parent"], "monitor": ledger["monitor"],
            "python": ledger["python"], "observed_ns": time.monotonic_ns()}


def check_receipt(row, ledger):
    if (row["owner"] != 723 or row["parent"] != ledger["parent"] or row["monitor"] != ledger["monitor"]
            or row["python"] != ledger["python"]
            or not ledger["born_ns"] <= row["observed_ns"] <= time.monotonic_ns()):
        raise ValueError("receipt")


def metric_rows(directory):
    data = read_owned(directory / "metrics.jsonl", 120 * 4096)
    if not data.endswith(b"\n"):
        raise ValueError("metrics")
    lines = data.splitlines()
    if not 1 <= len(lines) <= 120 or any(len(line) >= 4096 for line in lines):
        raise ValueError("metrics")
    return [json.loads(line) for line in lines]


def check_metrics(rows, ledger):
    if not rows:
        raise ValueError("metrics")
    previous = ledger["born_ns"]
    for sequence, row in enumerate(rows):
        encode_record(row)
        if (row["event"] != 2 or row["sequence"] != sequence or row["owner"] != 723
                or any(row[k] != ledger[k] for k in ("run", "attempt"))
                or not previous <= row["monotonic_ns"] <= min(time.monotonic_ns(), ledger["born_ns"] + ledger["lease_ns"])):
            raise ValueError("metrics")
        previous = row["monotonic_ns"]
    if rows[0]["parent"] != ledger["parent"] or rows[0]["monitor"] != ledger["monitor"]:
        raise ValueError("metrics")


def observe(parent_pid, workspace, temporary, directory, lease=3600, interval=30):
    import resource
    if not 0 < lease <= 3600 or not 0 < interval <= 30:
        raise ValueError("lease")
    parent, own = identity(parent_pid), identity(os.getpid())
    if not parent or not own or own["ppid"] != parent_pid:
        raise ValueError("parent")
    private_directory(directory)
    resource.setrlimit(resource.RLIMIT_AS, (64 * 1024**2, 64 * 1024**2))
    resource.setrlimit(resource.RLIMIT_CPU, (15, 15))
    os.nice(19)
    born = time.monotonic_ns()
    ledger = {"owner": 723, "parent": parent, "monitor": own, "born_ns": born,
              "lease_ns": int(lease * 10**9), "memory_limit": 64 * 1024**2, "cpu_seconds": 15,
              "python": python_identity(), **owner_labels()}
    ledger["ancestors"] = []
    ancestor = parent
    for _ in range(8):
        ancestor = identity(ancestor["ppid"])
        if not ancestor:
            break
        ledger["ancestors"].append(ancestor)
    write_new(directory / "owner.json", ledger)
    stopped = threading.Event()
    signal.signal(signal.SIGTERM, lambda *_: stopped.set())
    signal.signal(signal.SIGINT, lambda *_: stopped.set())

    def measurements():
        try:
            fd = os.open(directory / "metrics.jsonl", os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
            try:
                for sequence in range(120):
                    if stopped.is_set():
                        break
                    row = sample(parent, workspace, temporary, sequence)
                    row.update(owner=723, run=ledger["run"], attempt=ledger["attempt"])
                    if sequence == 0:
                        row.update(parent=parent, monitor=own, ancestors=ledger["ancestors"])
                    data = encode_record(row)
                    os.write(fd, data)
                    # This daemon may block on stdout; the independent lease/parent watcher never waits for it.
                    os.write(1, data)
                    if stopped.wait(interval):
                        break
            finally:
                os.close(fd)
        except Exception:
            stopped.set()

    threading.stack_size(256 * 1024)
    threading.Thread(target=measurements, daemon=True).start()
    expires = time.monotonic() + lease
    while not stopped.wait(.1):
        if (time.monotonic() >= expires or (directory / "stop").exists()
                or not same_identity(parent, identity(parent_pid))):
            break
    # Avoid interpreter stream/thread finalization hanging on a full inherited pipe.
    os._exit(0)


def stop(directory, parent_pid, monitor_pid):
    ledger = load_ledger(directory)
    if (os.getppid() != parent_pid or ledger["monitor"]["pid"] != monitor_pid
            or not same_identity(ledger["parent"], identity(parent_pid))):
        raise ValueError("parent")
    monitor = ledger["monitor"]
    if monitor_pid in (os.getpid(), parent_pid):
        raise ValueError("monitor")
    active = identity(monitor_pid)
    if active is not None and not verified_monitor(ledger, parent_pid, monitor_pid):
        raise ValueError("membership")
    fd = os.open(directory / "stop", os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    os.close(fd)
    deadline = time.monotonic() + 1
    while verified_monitor(ledger, parent_pid, monitor_pid) and time.monotonic() < deadline:
        time.sleep(.05)
    if verified_monitor(ledger, parent_pid, monitor_pid):
        # pidfd pins the verified monitor; no PID or group-signalling fallback.
        fd = os.pidfd_open(monitor["pid"])
        try:
            if not verified_monitor(ledger, parent_pid, monitor_pid):
                raise ValueError("birth")
            signal.pidfd_send_signal(fd, signal.SIGTERM)
            time.sleep(.2)
            if verified_monitor(ledger, parent_pid, monitor_pid):
                signal.pidfd_send_signal(fd, signal.SIGKILL)
                deadline = time.monotonic() + .2
                while same_identity(monitor, identity(monitor["pid"])) and time.monotonic() < deadline:
                    time.sleep(.01)
        finally:
            os.close(fd)
    absent = verified_absence(monitor, identity(monitor_pid))
    write_new(directory / "cleanup.json", {**receipt(ledger), "absent": int(absent)})
    return 0 if absent else 2


def ready(directory, monitor_pid, parent_pid):
    if os.getppid() != parent_pid:
        raise ValueError("caller")
    deadline = time.monotonic() + 1
    while time.monotonic() < deadline:
        try:
            ledger = load_ledger(directory)
            rows = metric_rows(directory)
            check_metrics(rows, ledger)
            if verified_monitor(ledger, parent_pid, monitor_pid):
                write_new(directory / "ready.json", receipt(ledger))
                return 0
        except (OSError, ValueError, KeyError):
            pass
        time.sleep(.02)
    return 2


def unavailable(directory):
    private_directory(directory)
    write_new(directory / "unavailable.json", {"event": 4, "flags": 8})


def post_gate(directory, outcome):
    if outcome not in ("success", "failure", "cancelled", "skipped"):
        raise ValueError("outcome")
    if not directory.exists():
        if outcome != "success":
            return 3  # NotStarted: existing Validate failure remains a failure, never acceptance.
        raise ValueError("activation")
    ledger = load_ledger(directory)
    if (directory / "unavailable.json").exists():
        raise ValueError("unavailable")
    activated = read_owned_json(directory / "ready.json")
    cleanup = read_owned_json(directory / "cleanup.json")
    check_receipt(activated, ledger)
    check_receipt(cleanup, ledger)
    rows = metric_rows(directory)
    check_metrics(rows, ledger)
    if (not rows[0]["monotonic_ns"] <= activated["observed_ns"] <= cleanup["observed_ns"]
            or cleanup["absent"] != 1 or not verified_absence(ledger["monitor"], identity(ledger["monitor"]["pid"]))):
        raise ValueError("cleanup")
    return 0


def main():
    args = sys.argv[1:]
    if args[0] == "check" and len(args) == 3:
        return 0 if matches_script(args[1], args[2]) else 3
    if sys.platform != "linux":
        return 2
    if args[0] == "preflight" and len(args) == 2:
        return 2 if ("BASH_ENV" in os.environ or "_WEB_FULL_OBSERVER_ACTIVE" in os.environ
                     or (Path(args[1]) / "full-test-observer-results").exists()) else 0
    if args[0] == "run" and len(args) == 5:
        observe(int(args[1]), Path(args[2]), Path(args[3]), Path(args[4]))
    if args[0] == "stop" and len(args) == 4:
        return stop(Path(args[1]), int(args[2]), int(args[3]))
    if args[0] == "ready" and len(args) == 4:
        return ready(Path(args[1]), int(args[2]), int(args[3]))
    if args[0] == "unavailable" and len(args) == 2:
        unavailable(Path(args[1]))
        return 0
    if args[0] == "verify" and len(args) == 3:
        try:
            result = post_gate(Path(args[2]) / "full-test-observer-results", args[1])
        except Exception:
            result = 2
        os.write(1, encode_record({"event": 6, "status": result}))
        return 0 if result in (0, 3) else 2
    return 2


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:
        # No raw exception, argument, path or environment output.
        sys.exit(2)
