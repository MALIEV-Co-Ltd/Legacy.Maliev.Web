#!/usr/bin/env python3
"""Read-only, bounded hosted-runner resource receipts; never test acceptance."""
import argparse
import json
import os
import resource
import re
import stat
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


DOTNET_ROLES = {"format", "test", "testhost", "msbuild", "compiler", "producer", "other", "unavailable"}
DOTNET_DLL_ROLES = {"dotnet-format.dll": "format", "testhost.dll": "testhost",
                    "MSBuild.dll": "msbuild", "VBCSCompiler.dll": "compiler",
                    "Legacy.Maliev.CustomerService.Api.dll": "producer",
                    "Legacy.Maliev.AuthService.Api.dll": "producer",
                    "Legacy.Maliev.CatalogService.Api.dll": "producer",
                    "Legacy.Maliev.AccountingService.Api.dll": "producer"}

def dotnet_role(text):
    # Classify only fixed tokens. Never return arguments, paths, environment or arbitrary names.
    if text is None or not text.endswith("\0"):
        return "unavailable"
    arguments = text.split("\0")[:-1]
    if len(arguments) > 256:
        return "unavailable"
    if len(arguments) > 1 and arguments[1] in ("format", "test", "msbuild", "vstest"):
        return {"format": "format", "test": "test", "msbuild": "msbuild", "vstest": "test"}[arguments[1]]
    if len(arguments) > 1 and (arguments[1] == "exec" or arguments[1].endswith(".dll")):
        for argument in arguments[1:]:
            name = argument.rsplit("/", 1)[-1]
            if name.endswith(".dll"):
                return DOTNET_DLL_ROLES.get(name, "other")
    return "other"

def resident_identity(text):
    if text is None:
        return None
    try:
        tail = text[text.rindex(")") + 2:].split()
        return integer(tail[19]), integer(tail[21])
    except (ValueError, IndexError, RuntimeError):
        return None

def dotnet_observation(source, process, page_size):
    before = resident_identity(source.read(process / "stat", 4096))
    role = dotnet_role(source.read(process / "cmdline", 4096))
    after = resident_identity(source.read(process / "stat", 4096))
    if before is None or after is None or before[0] != after[0]:
        return "unavailable", None
    return role, after[1] * page_size

def processes(source, proc=Path("/proc")):
    counts = {name: 0 for name in sorted(COUNTS)}
    total = scanned = 0
    roles = {role: {"count": 0, "rss_observations": 0, "rss_unavailable": 0, "rss_bytes_sum": 0}
             for role in sorted(DOTNET_ROLES)}
    page_size = os.sysconf("SC_PAGE_SIZE")
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
                    if name.strip() == "dotnet":
                        role, rss = dotnet_observation(source, proc / entry.name, page_size)
                        item = roles[role]
                        item["count"] += 1
                        item["rss_observations" if rss is not None else "rss_unavailable"] += 1
                        if rss is not None:
                            item["rss_bytes_sum"] += rss
                if source.exceeded:
                    capped = True
                    break
    except OSError:
        return {"available": False}
    return {"available": True, "observed_processes": total, "entries_examined": min(scanned, MAX_ENTRIES),
            "enumeration_capped": capped, "fixed_name_counts": counts,
            "testhost_named_count_is_not_all_dotnet_testhosts": True,
            "dotnet_roles": {role: {**item, "rss_bytes_sum": item["rss_bytes_sum"] if item["rss_observations"] else None}
                             for role, item in roles.items()},
            "rss_scope": "observed RSS sum; shared pages may be counted more than once; incomplete reads remain unavailable"}

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


# Diagnostic replay is an observation only. It never decides test acceptance.
_DIAG_PREFIX = "TestRequestSender.OnExecutionMessageReceived: Received message: "
_DIAG_SCHEMA = "web-vstest-replay.v1"
_DIAG_REASONS = {
    "trace_missing", "unsafe_file", "extra_generation", "generation_gap",
    "file_truncated", "split_line", "line_cap", "malformed_payload",
    "invalid_case", "conflicting_id", "unmatched_completion", "active_cap",
    "input_cap", "time_cap", "state_invalid", "work_cap", "unknown_trace_name",
}
_DIAG_FQN = re.compile(r"Legacy\.Maliev\.Web\.Tests\.[A-Za-z_][A-Za-z0-9_]*(?:[.+][A-Za-z_][A-Za-z0-9_]*)+\Z")
_DIAG_GUID = re.compile(r"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\Z")
_DIAG_MARKER = "vstest.diag"
_DIAG_BACKUP = "vstest.bak.diag"

_DIAG_SIDECAR = re.compile(
    r"vstest\.(host|datacollector)\.[0-9]{2}-[0-9]{2}-[0-9]{2}_"
    r"[0-9]{2}-[0-9]{2}-[0-9]{2}_[0-9]{5}_[1-9][0-9]{0,9}(?:\.bak)?\.diag\Z"
)

def _diag_role(name):
    if name in (_DIAG_MARKER, _DIAG_BACKUP):
        return "console"
    if not isinstance(name, str) or len(name) > 128:
        return None
    match = _DIAG_SIDECAR.fullmatch(name)
    return match.group(1) if match is not None else None


def _diag_case(value):
    if not isinstance(value, dict):
        return None
    identifier, fqn = value.get("Id"), value.get("FullyQualifiedName")
    if not isinstance(identifier, str) or not isinstance(fqn, str):
        return None
    identifier = identifier.lower()
    if not _DIAG_GUID.fullmatch(identifier) or len(fqn) > 384 or not _DIAG_FQN.fullmatch(fqn):
        return None
    return {"id": identifier, "fqn": fqn}

def _diag_number(value):
    return type(value) is int and 0 <= value <= 9223372036854775807

def _diag_json(raw):
    # Reject excessive nesting before invoking the JSON decoder. Strings and
    # escapes are counted without retaining a second representation.
    depth = 0
    quoted = escaped = False
    for character in raw:
        if quoted:
            if escaped:
                escaped = False
            elif character == "\\":
                escaped = True
            elif character == '"':
                quoted = False
        elif character == '"':
            quoted = True
        elif character in "[{":
            depth += 1
            require(depth <= 32)
        elif character in "]}":
            depth -= 1
            require(depth >= 0)
    require(depth == 0 and not quoted)
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result)
            result[key] = value
        return result
    return json.loads(raw, object_pairs_hook=unique)

class VstestDiagnosticReplay:
    """Bounded projection of observed events, never a current-test verdict."""
    def __init__(self, owner, persisted=None):
        self._state = {
            "schema": _DIAG_SCHEMA, "owner": dict(owner), "cursor": None,
            "epoch": 0, "ordinal": 0, "reasons": [], "events": [],
            "pending": [], "history_truncated": False, "seen": False,
            "discard_line": False, "owned_files": [],
        }
        if persisted is not None:
            self._validate(persisted, owner)
            self._state = json.loads(json.dumps(persisted))

    @staticmethod
    def _validate(value, owner):
        keys = {"schema", "owner", "cursor", "epoch", "ordinal", "reasons",
                "events", "pending", "history_truncated", "seen",
                "discard_line", "owned_files"}
        require(isinstance(value, dict) and set(value) == keys)
        require(value["schema"] == _DIAG_SCHEMA and value["owner"] == owner)
        require(_diag_number(value["epoch"]) and _diag_number(value["ordinal"]))
        require(all(type(value[key]) is bool for key in ("history_truncated", "seen", "discard_line")))
        require(isinstance(value["reasons"], list) and len(value["reasons"]) <= len(_DIAG_REASONS))
        require(all(isinstance(reason, str) and reason in _DIAG_REASONS for reason in value["reasons"]))
        cursor = value["cursor"]
        if cursor is not None:
            require(isinstance(cursor, dict) and set(cursor) == {"device", "inode", "offset"})
            require(all(_diag_number(number) for number in cursor.values()))
        for key, maximum in (("pending", 128), ("events", 64), ("owned_files", 16)):
            require(isinstance(value[key], list) and len(value[key]) <= maximum)
        for item in value["pending"]:
            require(isinstance(item, dict) and set(item) == {"id", "fqn"})
            require(_diag_case({"Id": item["id"], "FullyQualifiedName": item["fqn"]}) == item)
        for event in value["events"]:
            require(isinstance(event, dict) and set(event) == {"id", "fqn", "kind", "epoch", "ordinal", "offset"})
            require(_diag_case({"Id": event["id"], "FullyQualifiedName": event["fqn"]}) is not None)
            require(event["kind"] in ("start", "completion"))
            require(all(_diag_number(event[key]) for key in ("epoch", "ordinal", "offset")))
        for item in value["owned_files"]:
            require(isinstance(item, dict) and set(item) == {"device", "inode"})
            require(all(_diag_number(number) for number in item.values()))
        encoded(value)

    def state(self):
        self._validate(self._state, self._state["owner"])
        return json.loads(json.dumps(self._state))

    def receipt(self):
        state = self._state
        return {
            "schema": _DIAG_SCHEMA, "history_complete": state["seen"] and not state["reasons"],
            "history_truncated": state["history_truncated"],
            "reasons": list(state["reasons"]) if state["seen"] or state["reasons"] else ["trace_missing"],
            "events": json.loads(json.dumps(state["events"])),
            "observed_uncompleted": sorted({item["fqn"] for item in state["pending"]}) if not state["reasons"] else [],
            "scope": "observed starts/completions only; absent completion does not prove current execution or cause",
        }

    def gap(self, reason):
        require(reason in _DIAG_REASONS)
        state = self._state
        if reason not in state["reasons"]:
            state["reasons"].append(reason)
        state["epoch"] += 1
        state["pending"] = []

    def _event(self, kind, case, offset):
        state = self._state
        state["ordinal"] += 1
        state["events"].append({**case, "kind": kind, "epoch": state["epoch"],
                                "ordinal": state["ordinal"], "offset": offset})
        if len(state["events"]) > 64:
            del state["events"][0]
            state["history_truncated"] = True

    def _chunk(self, chunk, offset):
        if not isinstance(chunk, dict):
            self.gap("malformed_payload")
            return
        starts, completed = chunk.get("ActiveTests") or [], chunk.get("NewTestResults") or []
        if not isinstance(starts, list) or not isinstance(completed, list) or len(starts) + len(completed) > 1024:
            self.gap("malformed_payload")
            return
        for raw in starts:
            case = _diag_case(raw)
            if case is None:
                self.gap("invalid_case")
                continue
            previous = next((item for item in self._state["pending"] if item["id"] == case["id"]), None)
            if previous is not None:
                if previous != case:
                    self.gap("conflicting_id")
                continue
            if not self._state["reasons"]:
                if len(self._state["pending"]) >= 128:
                    self.gap("active_cap")
                else:
                    self._state["pending"].append(case)
            self._event("start", case, offset)
        for raw in completed:
            case = _diag_case(raw.get("TestCase")) if isinstance(raw, dict) else None
            if case is None:
                self.gap("invalid_case")
                continue
            if case not in self._state["pending"]:
                self.gap("unmatched_completion")
            else:
                self._state["pending"].remove(case)
            self._event("completion", case, offset)

    def _line(self, raw, offset):
        try:
            text = raw.decode("utf-8", errors="strict")
            if _DIAG_PREFIX not in text:
                return
            require(text.startswith("TpTrace Verbose:"))
            payload = _diag_json(text.split(_DIAG_PREFIX, 1)[1])
            require(isinstance(payload, dict))
            kind = payload.get("MessageType")
            if kind == "TestExecution.StatsChange":
                self._chunk(payload.get("Payload"), offset)
            elif kind == "TestExecution.Completed":
                final = payload.get("Payload")
                require(isinstance(final, dict))
                if final.get("LastRunTests") is not None:
                    self._chunk(final["LastRunTests"], offset)
        except (RuntimeError, ValueError, TypeError, RecursionError, UnicodeError):
            self.gap("malformed_payload")

    def _open(self, path):
        require(_diag_role(path.name) is not None)
        handle = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
        try:
            item = os.fstat(handle)
            require(stat.S_ISREG(item.st_mode) and item.st_nlink == 1)
            identity = {"device": item.st_dev, "inode": item.st_ino}
            if identity not in self._state["owned_files"]:
                require(len(self._state["owned_files"]) < 16)
                self._state["owned_files"].append(identity)
            return handle, identity, item.st_size
        except BaseException:
            os.close(handle)
            raise

    def _consume(self, path, source, offset):
        handle, identity, size = self._open(path)
        try:
            if size < offset:
                self.gap("file_truncated")
                offset = 0
            if time.monotonic() >= source.deadline:
                self.gap("time_cap")
                source.exceeded = True
                return identity, offset
            limit = min(65536, source.remaining)
            if limit <= 0:
                self.gap("input_cap")
                source.exceeded = True
                return identity, offset
            os.lseek(handle, offset, os.SEEK_SET)
            raw = os.read(handle, limit)
            source.remaining -= len(raw)
            consumed = 0
            for line in raw.splitlines(keepends=True):
                if not line.endswith(b"\n"):
                    if len(line) >= 65536 or self._state["discard_line"]:
                        self.gap("line_cap")
                        self._state["discard_line"] = True
                        consumed += len(line)
                    # Otherwise reread this same verified inode/offset next poll.
                    break
                consumed += len(line)
                if self._state["discard_line"]:
                    self._state["discard_line"] = False
                    continue
                self._line(line, offset + consumed)
            next_offset = offset + consumed
            if source.remaining == 0 and next_offset < size:
                self.gap("input_cap")
                source.exceeded = True
            elif len(raw) == limit and consumed < len(raw) and source.remaining < 65536 and next_offset < size:
                self.gap("input_cap")
                source.exceeded = True
            return identity, next_offset
        finally:
            os.close(handle)

    def poll(self, path, source):
        require(path.name == _DIAG_MARKER)
        state = self._state
        try:
            # Bounded census also records diagnostic sidecar identity for cleanup,
            # without persisting filenames, command lines or raw log data.
            count = 0
            with os.scandir(path.parent) as entries:
                for entry in entries:
                    count += 1
                    if count > MAX_ENTRIES:
                        self.gap("extra_generation")
                        return
                    if entry.name.startswith("vstest") and ".diag" in entry.name:
                        if _diag_role(entry.name) is None:
                            self.gap("unknown_trace_name")
                            continue
                        handle, _, _ = self._open(Path(entry.path))
                        os.close(handle)
            if not path.exists():
                if state["seen"]:
                    self.gap("trace_missing")
                return
            current = path.lstat()
            if not stat.S_ISREG(current.st_mode):
                self.gap("unsafe_file")
                return
            current_identity = {"device": current.st_dev, "inode": current.st_ino}
            cursor = state["cursor"]
            offset = 0
            if cursor is not None:
                old_identity = {key: cursor[key] for key in ("device", "inode")}
                if current_identity == old_identity:
                    offset = cursor["offset"]
                else:
                    backup = path.with_name(_DIAG_BACKUP)
                    old = backup.lstat() if backup.exists() else None
                    if old is not None and stat.S_ISREG(old.st_mode) and {"device": old.st_dev, "inode": old.st_ino} == old_identity:
                        _, old_offset = self._consume(backup, source, cursor["offset"])
                        if old_offset < old.st_size:
                            self.gap("generation_gap")
                    else:
                        self.gap("generation_gap")
                    if state["discard_line"]:
                        self.gap("split_line")
                    state["discard_line"] = False
            identity, offset = self._consume(path, source, offset)
            state["cursor"] = {**identity, "offset": offset}
            state["seen"] = True
        except (OSError, RuntimeError):
            self.gap("unsafe_file")


def diagnostic_tick(replay, trace, source, work):
    """One existing worker wake; byte budget is shared, work excludes sleeping."""
    if work >= 10 or time.process_time() >= 15 or source.remaining <= 256 * 1024:
        reason = "input_cap" if source.remaining <= 256 * 1024 else "work_cap"
        if reason not in replay.state()["reasons"]:
            replay.gap(reason)
        return max(work, 10)
    started = time.monotonic()
    source.deadline = started + min(1, 10 - work)
    # Reserve input capacity for the state and aggregate resource observation.
    source.remaining -= 256 * 1024
    try:
        replay.poll(trace, source)
    finally:
        source.remaining += 256 * 1024
    return work + max(0, time.monotonic() - started)


def diagnostic_writers_settled(source, identities, proc=Path("/proc")):
    """Linux stock net10 writers inherit this UID; never infer from step exit."""
    try:
        require(os.name == "posix" and os.geteuid() != 0)
        require(time.monotonic() < source.deadline and time.process_time() < 15)
        census = processes(source, proc)
        require(census.get("available") is True and not census["enumeration_capped"])
        require(census["fixed_name_counts"]["dotnet"] == 0 and
                census["fixed_name_counts"]["testhost"] == 0)
        require(not source.exceeded and source.unavailable == 0)
        uid = os.geteuid()
        require(_diag_number(census.get("entries_examined")) and census["entries_examined"] <= MAX_ENTRIES)
        entries_seen, process_count = census["entries_examined"], 0
        with os.scandir(proc) as entries:
            for entry in entries:
                entries_seen += 1
                require(entries_seen <= MAX_ENTRIES)
                require(time.monotonic() < source.deadline and time.process_time() < 15)
                if not entry.name.isascii() or not entry.name.isdecimal():
                    continue
                process_count += 1
                require(process_count <= MAX_PROCESSES)
                process = proc / entry.name
                before = resident_identity(source.read(process / "stat", 4096))
                status = source.read(process / "status", 16384)
                require(before is not None and status is not None)
                uid_rows = [line.split() for line in status.splitlines() if line.startswith("Uid:")]
                require(len(uid_rows) == 1 and len(uid_rows[0]) == 5)
                uids = [integer(value) for value in uid_rows[0][1:]]
                # No custom/elevated host is admitted by this writer contract.
                require(uid not in uids or all(value == uid for value in uids))
                if uids[1] == uid and int(entry.name) != os.getpid():
                    with os.scandir(process / "fd") as descriptors:
                        for descriptor in descriptors:
                            entries_seen += 1
                            require(entries_seen <= MAX_ENTRIES)
                            require(time.monotonic() < source.deadline and time.process_time() < 15)
                            require(descriptor.name.isascii() and descriptor.name.isdecimal())
                            # Stat follows the numeric fd, never reads or emits its
                            # target path. Even renamed/native holders are caught.
                            observed = descriptor.stat(follow_symlinks=True)
                            require({"device": observed.st_dev, "inode": observed.st_ino} not in identities)
                after = resident_identity(source.read(process / "stat", 4096))
                require(after is not None and before[0] == after[0])
        require(not source.exceeded and source.unavailable == 0)
        return True
    except (OSError, RuntimeError, ValueError, TypeError, KeyError):
        return False

class DiagnosticCleanupRejected(RuntimeError):
    def __init__(self, receipt):
        super().__init__("Hosted diagnostic cleanup rejected")
        self.receipt = receipt

def cleanup_diagnostics(path, identity, outcome):
    """Only after full action terminal and independent collector quiescence."""
    receipt = {
        "schema": "web-vstest-cleanup.v1", "owner": dict(identity),
        "status": "writer_uncertainty", "removed_count": 0,
        "remaining": "preserved_or_unverified", "partial_cleanup": False,
        "writer_contract": "ubuntu-net10-stock-same-uid",
        "retention": {
            "policy": "ephemeral_hosted_job_teardown",
            "default_job_timeout_minutes": 360,
            "observed_job_start_utc": None, "exact_deadline_utc": None,
            "teardown": "pending_job_end_teardown",
            "scope": "existing default job timeout policy; runner teardown not observed",
        },
    }
    try:
        settled = _cleanup_diagnostics(path, identity, outcome, receipt)
        receipt["status"] = "settled_and_removed" if settled else "not_started_no_raw_files"
        receipt["remaining"] = "no_recognized_raw_files_observed"
        return receipt
    except (OSError, RuntimeError, ValueError, TypeError, RecursionError):
        receipt["partial_cleanup"] = receipt["removed_count"] > 0
        raise DiagnosticCleanupRejected(receipt) from None

def _cleanup_diagnostics(path, identity, outcome, receipt):
    require(outcome in ("success", "failure", "skipped"))
    directory(path, identity)
    source = Inputs(time.monotonic() + 30)
    state_path = path / "diagnostic-replay.json"
    require(not state_path.is_symlink())
    if not state_path.exists():
        examined = 0
        with os.scandir(path) as entries:
            for entry in entries:
                examined += 1
                require(examined <= MAX_ENTRIES)
                require(not (entry.name.startswith("vstest") and ".diag" in entry.name))
        return False
    raw_state = source.read(state_path, MAX_RECEIPT)
    require(raw_state is not None)
    replay = VstestDiagnosticReplay(identity, json.loads(raw_state))
    owned = replay.state()["owned_files"]
    pending = []
    examined = 0
    with os.scandir(path) as entries:
        for entry in entries:
            examined += 1
            require(examined <= MAX_ENTRIES)
            if not entry.name.startswith("vstest") or ".diag" not in entry.name:
                continue
            require(_diag_role(entry.name) is not None)
            target = Path(entry.path)
            handle = os.open(target, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
            try:
                observed = os.fstat(handle)
                item = {"device": observed.st_dev, "inode": observed.st_ino}
                require(stat.S_ISREG(observed.st_mode) and observed.st_nlink == 1 and
                        observed.st_uid == os.geteuid() and item in owned)
                require(len(pending) < 16)
                pending.append((target, item))
            finally:
                os.close(handle)
    # Verify ALL candidate identities and the complete bounded writer proof
    # before the first mutation. Every later unlink gets a fresh proof using
    # this SAME byte budget/deadline. Late uncertainty can mean partial cleanup.
    for target, expected in pending:
        actual = target.lstat()
        require(stat.S_ISREG(actual.st_mode) and actual.st_nlink == 1 and
                actual.st_uid == os.geteuid() and
                {"device": actual.st_dev, "inode": actual.st_ino} == expected)
    identities = [item for _, item in pending]
    require(diagnostic_writers_settled(source, identities))
    for target, expected in pending:
        require(diagnostic_writers_settled(source, identities))
        actual = target.lstat()
        require(stat.S_ISREG(actual.st_mode) and actual.st_nlink == 1 and
                actual.st_uid == os.geteuid() and
                {"device": actual.st_dev, "inode": actual.st_ino} == expected)
        target.unlink()
        receipt["removed_count"] += 1
        require(not target.exists() and not target.is_symlink())
    return True

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
        source = Inputs(time.monotonic() + 30)
        replay = None
        work = 0.0
        trace = path / _DIAG_MARKER
        state_path = path / "diagnostic-replay.json"
        if identity["RESOURCE_CHECKPOINT_SCOPE"] == "full":
            began = time.monotonic()
            replay = VstestDiagnosticReplay(identity)
            if state_path.exists() or state_path.is_symlink():
                try:
                    require(not state_path.is_symlink())
                    raw_state = source.read(state_path, MAX_RECEIPT)
                    require(raw_state is not None)
                    replay = VstestDiagnosticReplay(identity, json.loads(raw_state))
                except (OSError, RuntimeError, ValueError, TypeError, RecursionError):
                    replay.gap("state_invalid")
            work = time.monotonic() - began
        while time.monotonic() < expected:
            if stopping or time.time() >= lease["expires"]:
                return
            if replay is not None:
                if work < 10:
                    work = diagnostic_tick(replay, trace, source, work)
                    began = time.monotonic()
                    atomic(state_path, replay.state())
                    work += max(0, time.monotonic() - began)
            time.sleep(min(1, max(0, expected - time.monotonic())))
        if stopping:
            return
        if replay is not None:
            work = diagnostic_tick(replay, trace, source, work)
            began = time.monotonic()
            atomic(state_path, replay.state())
            work += max(0, time.monotonic() - began)
        source.deadline = time.monotonic() + max(0, 30 - work)
        value = snapshot(Path(os.environ["GITHUB_WORKSPACE"]).resolve(strict=True), temporary, source)
        receipt = {"schema": "web-hosted-resource-checkpoint.v1", "owner": identity,
                   "phase": phase, "elapsed_seconds": round(time.monotonic() - lease["origin"], 3),
                   "worker": process, "lease_expires": lease["expires"], "resources": value,
                   "input_bytes": MAX_INPUT - source.remaining, "unavailable_reads": source.unavailable,
                   "input_or_time_cap_observed": source.exceeded,
                   "scope": "read-only aggregate resource observation; no test acceptance or cause verdict"}
        if replay is not None:
            receipt["diagnostic"] = replay.receipt()
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

def cleanup(path, receipt_name="cleanup.json"):
    require(receipt_name in ("cleanup.json", "emergency-cleanup.json"))
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
    write_new(path / receipt_name, {"schema": "web-hosted-resource-cleanup.v1", "owner": identity, "outcome": outcome})

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
    raise RuntimeError("Required cancellation and exact-owner cleanup left the exact checkpoint worker running")

def validate_upload_manifest(manifest, scope):
    require(isinstance(manifest, list) and len(manifest) == 13)
    skipped = False
    for phase, item in enumerate(manifest):
        require(isinstance(item, dict) and set(item) == {"phase", "outcome", "artifact_id"})
        require(item["phase"] == phase and item["outcome"] in ("success", "skipped"))
        require(isinstance(item["artifact_id"], str))
        if item["outcome"] == "success":
            require(not skipped and integer(item["artifact_id"]) > 0)
        else:
            skipped = True
            require(item["artifact_id"] == "")
        if phase == 0:
            require(item["outcome"] == "success")
        if scope != "full" and phase > 0:
            require(item["outcome"] == "skipped")

def final_ledger(path, identity):
    source = path / "worker.json"
    require(source.is_file() and not source.is_symlink())
    ledger = json.loads(bounded_read(source))
    require(ledger["owner"] == identity and isinstance(ledger["pid"], int) and ledger["pid"] > 1)
    require(ledger["state"] in ("running", "exited") and 0 <= ledger["phase"] <= 12)
    return ledger

def terminal(path):
    identity = owner()
    directory(path, identity)
    manifest = [{"phase": phase,
                 "outcome": os.environ["RESOURCE_UPLOAD_" + str(phase).zfill(2) + "_OUTCOME"],
                 "artifact_id": os.environ["RESOURCE_UPLOAD_" + str(phase).zfill(2) + "_ID"]}
                for phase in range(13)]
    validate_upload_manifest(manifest, identity["RESOURCE_CHECKPOINT_SCOPE"])
    if identity["RESOURCE_CHECKPOINT_SCOPE"] != "full":
        require(not (path / "sample-01.json").exists())
    value = {"schema": "web-hosted-resource-quiescence.v1", "owner": identity,
             "worker": final_ledger(path, identity), "uploads": manifest,
             "scope": "last inner composite step; preceding upload actions returned; no later upload step"}
    require(not (path / "quiescence.json").exists())
    atomic(path / "quiescence.json", value)

def verify_quiescence(path):
    identity = owner()
    directory(path, identity)
    deadline = time.monotonic() + 15
    receipt_path = path / "quiescence.json"
    while not receipt_path.is_file():
        require(not receipt_path.is_symlink() and time.monotonic() < deadline)
        time.sleep(0.1)
    require(not receipt_path.is_symlink())
    receipt = json.loads(bounded_read(receipt_path, MAX_RECEIPT))
    require(receipt["schema"] == "web-hosted-resource-quiescence.v1" and receipt["owner"] == identity)
    validate_upload_manifest(receipt["uploads"], identity["RESOURCE_CHECKPOINT_SCOPE"])
    ledger = final_ledger(path, identity)
    require(receipt["worker"] == ledger)
    cleanup_path = path / "cleanup.json"
    require(cleanup_path.is_file() and not cleanup_path.is_symlink())
    cleaned = json.loads(bounded_read(cleanup_path))
    require(cleaned["schema"] == "web-hosted-resource-cleanup.v1" and cleaned["owner"] == identity)
    require(cleaned["outcome"] in ("stopped", "exited", "exited-pid-reused", "absent"))
    if identity["RESOURCE_CHECKPOINT_SCOPE"] != "full":
        require(not (path / "sample-01.json").exists())
    expected = {key: ledger[key] for key in ("pid", "start_ticks", "exe_device", "exe_inode")}
    try:
        require(proc_identity(ledger["pid"]) != expected)
    except FileNotFoundError:
        pass
    verified = {"schema": "web-hosted-resource-stop-verification.v1",
                "owner": identity, "worker": expected, "uploads": receipt["uploads"],
                "scope": "required exact-owner cleanup, final composite quiescence and independent final-ledger release"}
    if identity["RESOURCE_CHECKPOINT_SCOPE"] == "full":
        try:
            verified["diagnostic_cleanup"] = cleanup_diagnostics(
                path, identity, os.environ.get("WEB_FULL_DOTNET_OUTCOME"))
        except DiagnosticCleanupRejected as error:
            verified["diagnostic_cleanup"] = error.receipt
            verified["scope"] = "collector release observed; diagnostic cleanup NOT accepted"
            write_new(path / "verification.json", verified)
            raise RuntimeError("Hosted diagnostic cleanup rejected") from None
    write_new(path / "verification.json", verified)

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("sample", "cleanup", "wait", "released", "terminal", "quiescence", "emergency-cleanup"))
    parser.add_argument("--directory", required=True, type=Path)
    parser.add_argument("--phase", type=int, default=0)
    args = parser.parse_args()
    if args.mode == "sample":
        sample(args.directory, args.phase)
    elif args.mode == "cleanup":
        cleanup(args.directory)
    elif args.mode == "wait":
        wait_worker(args.directory, args.phase)
    elif args.mode == "released":
        verify_released(args.directory)
    elif args.mode == "terminal":
        terminal(args.directory)
    elif args.mode == "quiescence":
        verify_quiescence(args.directory)
    else:
        cleanup(args.directory, "emergency-cleanup.json")
