"""Hosted-only, immutable LINE source sensitivity and exact TRX proof. No deployment."""
from __future__ import annotations
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import stat
import subprocess
import tempfile
import time
import uuid
import xml.etree.ElementTree as ET

REPOSITORY = "MALIEV-Co-Ltd/Legacy.Maliev.Web"
CONTRACT = "tests/fixtures/line-contact-regression-contract.json"
CONTRACT_SHA256 = "c9f7d03daa28175e61802db1f0a354f55ea3ca022ddffb3f6f9da05f5423f350"
BASELINE = "f843f75adb89fc8b810ff14775dc5125537cc736"
PRODUCERS = (
    "Legacy.Maliev.Web/Components/Analytics/PublicContactChannelAnalytics.razor",
    "Legacy.Maliev.Web/Components/Layout/PublicRouteAssets.cs",
)
TESTS = (
    "Legacy.Maliev.Web.Tests/PublicContactChannelAnalyticsMigrationTests.cs",
    "Legacy.Maliev.Web.Tests/LineContactAssetHttpTests.cs",
    "Legacy.Maliev.Web.Tests/LineContactNormalizationBrowserTests.cs",
)
NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
COUNTERS = set("total executed passed failed error timeout aborted inconclusive passedButRunAborted notRunnable notExecuted disconnected warning completed inProgress pending".split())
PROJECT = "Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj"
MAX_BYTES = 8 * 1024 * 1024


class Rejected(RuntimeError):
    """A bounded proof prerequisite or result was rejected."""


def require(ok, category):
    if not ok:
        raise Rejected(category)


def digest(value):
    return hashlib.sha256(value).hexdigest()


def regular(root, relative):
    root = Path(root)
    require(root.is_absolute() and not root.is_symlink() and root.is_dir(), "root")
    parts = Path(relative).parts
    require(parts and not Path(relative).is_absolute() and all(p not in ("..", ".") for p in parts), "relative-path")
    current = root
    for index, part in enumerate(parts):
        current = current / part
        info = current.lstat()
        require(not stat.S_ISLNK(info.st_mode) and not getattr(info, "st_file_attributes", 0) & 1024, "link")
        require(stat.S_ISDIR(info.st_mode) if index < len(parts) - 1 else stat.S_ISREG(info.st_mode), "file-type")
    require(current.resolve().is_relative_to(root.resolve()), "escape")
    return current


def read(root, relative, limit=MAX_BYTES):
    path = regular(root, relative)
    before = path.stat()
    require(before.st_size <= limit, "file-size")
    descriptor = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
    with os.fdopen(descriptor, "rb") as source:
        opened = os.fstat(source.fileno())
        require((before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns) ==
                (opened.st_dev, opened.st_ino, opened.st_size, opened.st_mtime_ns), "read-race")
        data = source.read(limit + 1)
        after = os.fstat(source.fileno())
    final = path.stat()
    require(len(data) == before.st_size and
            (after.st_size, after.st_mtime_ns) == (before.st_size, before.st_mtime_ns) and
            (final.st_dev, final.st_ino, final.st_size, final.st_mtime_ns) ==
            (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns), "read-race")
    return data


def replace(root, relative, data):
    path = regular(root, relative)
    before = path.stat()
    descriptor = os.open(path, os.O_WRONLY | getattr(os, "O_NOFOLLOW", 0))
    with os.fdopen(descriptor, "wb") as output:
        opened = os.fstat(output.fileno())
        require((before.st_dev, before.st_ino) == (opened.st_dev, opened.st_ino), "write-race")
        output.truncate(0)
        output.write(data)
        output.flush()
        os.fsync(output.fileno())
    require(read(root, relative) == data, "write-readback")


def no_duplicates(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "duplicate-json-key")
        result[key] = value
    return result


def contract(root):
    raw = read(root, CONTRACT, 128 * 1024)
    require(digest(raw) == CONTRACT_SHA256, "contract-hash")
    value = json.loads(raw, object_pairs_hook=no_duplicates)
    require(value["schema"] == "web-line-contact-regression.v1" and value["repository"] == REPOSITORY
            and value["baselineCommit"] == BASELINE, "contract")
    require(set(value["sources"]["baseline"]) == set(PRODUCERS) and
            set(value["sources"]["candidate"]) == set(PRODUCERS + TESTS), "source-roster")
    require(len(value["rows"]) == 22 and len({r["name"] for r in value["rows"]}) == 22 and
            sum(r["baseline"] is not None for r in value["rows"]) == 19 and
            sum(r["baseline"] == "Failed" for r in value["rows"]) == 10 and
            all(r["green"] == "Passed" for r in value["rows"]), "case-roster")
    return value


def check_sources(root, expected):
    for path, sha in expected.items():
        require(re.fullmatch("[0-9a-f]{64}", sha) and digest(read(root, path)) == sha, "source-hash")


def guid(value):
    try:
        require(str(uuid.UUID(value)) == value and uuid.UUID(value).int != 0, "guid")
    except (ValueError, AttributeError, TypeError) as error:
        raise Rejected("guid") from error
    return value


def one(parent, name):
    found = parent.findall(NS + name)
    require(len(found) == 1, "trx-" + name)
    return found[0]


def xml_root(raw):
    require(len(raw) <= MAX_BYTES and b"\x00" not in raw, "xml-encoding")
    try:
        text = raw.decode("utf-8-sig", "strict")
    except UnicodeError:
        raise Rejected("xml-encoding") from None
    require("\ufeff" not in text, "xml-encoding")
    declaration = re.match(r"\A<\?xml\b([^?]*)\?>", text)
    if declaration:
        encodings = re.findall(r"\bencoding\s*=\s*['\"]([^'\"]+)['\"]", declaration.group(1), re.I)
        require(len(encodings) <= 1 and all(e.lower() == "utf-8" for e in encodings), "xml-encoding")
    require(not re.search(r"<!\s*(?:DOCTYPE|ENTITY)\b", text, re.I), "xml-declaration")
    return ET.fromstring(text)


ORIGIN_ARGUMENTS = {
    "FriendshipRoute_SelectsSourceInquiryAssets": "String route",
    "RealLineRoute_ServesOwnedInquiryAssetsAndKeepsNoindexFallback": "String culture, String heading",
    "AssertLineDiagnostic": "JsonElement events",
    "RealClassifier_ConsentDecisionControlsQueuedLineClicksWithoutFriendOrLeadPromotion": "String culture, String decision",
}
FRAME = re.compile(r"[ \t]*at (?P<signature>[A-Za-z_][A-Za-z0-9_.]{0,1024}\([^()\r\n]{0,256}\)) in (?P<file>[^:\r\n]{1,4096}):line (?P<line>[1-9][0-9]{0,8})[ \t]*")


def origin_signature(row):
    origin = row["failure"]
    return row["class"] + "." + origin["originMethod"] + "(" + ORIGIN_ARGUMENTS[origin["originMethod"]] + ")"


def verify_origin(stack, row, source_root):
    require(len(stack) <= 32768, "error-origin")
    lines = stack.splitlines()
    require(len(lines) <= 128 and all(len(line) <= 6144 for line in lines), "error-origin")
    origin = row["failure"]
    expected_file = str(source_root / origin["sourcePath"])
    matches = [FRAME.fullmatch(line) for line in lines]
    require(any(m is not None and m["signature"] == origin_signature(row) and
                m["file"] == expected_file and m["line"] == str(origin["line"]) for m in matches), "error-origin")


class ProofFailure(Rejected):
    def __init__(self, summary):
        self.summary = summary
        super().__init__("proof-failed")

    def __str__(self):
        return json.dumps(self.summary, sort_keys=True)


SAFE_CATEGORIES = frozenset(["absence","admission-custody","after","api","ascii","assembly","base","baseline","baseline-build","baseline-source","build","build-zero-warnings-errors","c9f7d03daa28175e61802db1f0a354f55ea3ca022ddffb3f6f9da05f5423f350","cancelled-no-new-command","candidate","case-name","case-outcome","case-roster","category","checked-head","class","command-output-size","command-timeout","contract","contract-hash","counters","counts","cross-phase-case-identity","custody-executable","custody-generation","custody-observation-bound","custody-stat-size","custody-uncertain","definition","dotnet","duplicate-guid","duplicate-json-key","early-trx-readback","entry-join","error-corpus","error-info","error-message","error-origin","escape","event-kind","event-path","event-repository","evidence-io","executable","executable-change","execution-join","f843f75adb89fc8b810ff14775dc5125537cc736","failure","false","fetch","fetch-","file","file-size","file-type","get-url","gh","git","git-","git-origin","github-hosted","green","guid","head","hosted-owner","id","identities","leader-generation","line","line-contact-candidate-test-results","line-contact-regression-proof","link","linux-owned-session","method","name","namespace","number","observed-session-escape","observed-session-still-present","origin","outcome","output-limit","owned-run-interrupted","passing-error","pgid","phase","phase-counters","pid","pid-reuse","posix","ppid","pr-origin","primary-failed","proof-failed","push","raw-","rb","read-race","rebuild-failed","relative-path","remote","replace","repo","repository","restored-candidate-build","rev-parse","root","row-count","rows","run-info","saved-source","schema","session-owner","sha","sid","signal-restore","signature","single-trx","source-hash","source-roster","sources","stage","stat","state","stop","storage","strict","test","test-exit","test-join","tree","true","trx","trx-","trx-Counters","trx-Execution","trx-Message","trx-ResultSummary","trx-Results","trx-StackTrace","trx-TestDefinitions","trx-TestEntries","trx-TestMethod","type","unsafe-encoding","unsafe-normalization","unsafe-raw","utf-8","utf-8-sig","wait","waitid","wb","workspace","write-race","write-readback","xml-declaration","xml-encoding"])


def summary_failure(error):
    if isinstance(error, ProofFailure):
        return error.summary
    return {"type": type(error).__name__,
            "category": str(error) if isinstance(error, Rejected) and str(error) in SAFE_CATEGORIES else None}


def evidence_write(path, value, failures):
    try:
        path.write_text(json.dumps(value, sort_keys=True, indent=2), encoding="utf-8")
    except BaseException as error:
        failures.append({"stage": "evidence-io", "failure": summary_failure(error)})


def verify_trx(raw, frozen, phase, assembly, source_root):
    require(phase in ("baseline", "green"), "phase")
    root = xml_root(raw)
    require(root.tag == NS + "TestRun", "namespace")
    guid(root.get("id", ""))
    rows = {r["name"]: r for r in frozen["rows"] if phase == "green" or r["baseline"] is not None}
    expected_failed = 10 if phase == "baseline" else 0
    summary = one(root, "ResultSummary")
    counts_node = one(summary, "Counters")
    require(set(counts_node.attrib) == COUNTERS and
            all(re.fullmatch("0|[1-9][0-9]*", v) for v in counts_node.attrib.values()), "counters")
    counts = {k: int(v) for k, v in counts_node.attrib.items()}
    expected = {k: 0 for k in COUNTERS}
    expected.update(total=len(rows), executed=len(rows), failed=expected_failed, passed=len(rows)-expected_failed)
    require(counts == expected and summary.get("outcome") == ("Failed" if expected_failed else "Completed"), "phase-counters")
    require(not summary.findall(".//" + NS + "RunInfo") and not root.findall(".//" + NS + "RunInfo"), "run-info")
    require(len(root.findall(".//" + NS + "ErrorInfo")) == expected_failed, "error-corpus")
    groups = []
    for container, child in (("Results", "UnitTestResult"), ("TestDefinitions", "UnitTest"), ("TestEntries", "TestEntry")):
        group = list(one(root, container))
        require(len(group) == len(rows) and all(n.tag == NS + child for n in group) and
                len(root.findall(".//" + NS + child)) == len(rows), "row-count")
        groups.append(group)
    results, definitions, entries = groups
    def ids(group, attribute):
        values = [guid(n.get(attribute, "")) for n in group]
        require(len(set(values)) == len(rows), "duplicate-guid")
        return set(values)
    result_ids = ids(results, "testId")
    require(result_ids == ids(definitions, "id") == ids(entries, "testId"), "test-join")
    require(ids(results, "executionId") == ids(entries, "executionId"), "execution-join")
    require({(r.get("testId"), r.get("executionId")) for r in results} ==
            {(e.get("testId"), e.get("executionId")) for e in entries}, "entry-join")
    definitions = {d.get("id"): d for d in definitions}
    actual, identities = set(), {}
    for result in results:
        name = result.get("testName", "")
        require(name in rows and name not in actual, "case-name")
        actual.add(name)
        frozen_row = rows[name]
        definition = definitions[result.get("testId")]
        method = one(definition, "TestMethod")
        execution = one(definition, "Execution")
        require(definition.get("name") == name and execution.get("id") == result.get("executionId") and
                method.get("className") == frozen_row["class"] and method.get("name") == frozen_row["method"] and
                method.get("codeBase") == str(assembly) and
                definition.get("storage") in (str(assembly), str(assembly).lower()), "definition")
        outcome = frozen_row["baseline"] if phase == "baseline" else frozen_row["green"]
        require(result.get("outcome") == outcome, "case-outcome")
        errors = result.findall(".//" + NS + "ErrorInfo")
        if outcome == "Failed":
            require(len(errors) == 1, "error-info")
            message = one(errors[0], "Message").text or ""
            stack = one(errors[0], "StackTrace").text or ""
            origin = frozen_row["failure"]
            require(re.search(origin["messagePattern"], message) is not None and
                    not re.search("TimeoutException|timed out|OperationCanceled|TaskCanceled|OutOfMemory|PlaywrightException|HttpRequestException|fixture|InitializeAsync|MSB[0-9]", message, re.I), "error-message")
            verify_origin(stack, frozen_row, source_root)
        else:
            require(not errors, "passing-error")
        identities[name] = result.get("testId")
    require(actual == set(rows), "case-roster")
    return {"phase": phase, "counts": counts, "identities": identities, "trxSha256": digest(raw)}


def birth(pid):
    # Keep the leader unreaped until its exclusively created process session is stopped.
    tail = Path("/proc", str(pid), "stat").read_text().rsplit(")", 1)[1].split()
    return (int(tail[2]), int(tail[3]), int(tail[19]))


class Session:
    """Bounded observation of this exclusive SID, never universal detached-descendant custody."""
    def __init__(self, leader, token):
        self.leader, self.token = leader, token
        self.members, self.issues = {}, []
        self.forced_kill = False

    def process(self, pid):
        try:
            raw = Path("/proc", str(pid), "stat").read_text()
            require(len(raw) <= 8192, "custody-stat-size")
            fields = raw.rsplit(")", 1)[1].split()
            row = {"pid": pid, "ppid": int(fields[1]), "pgid": int(fields[2]), "sid": int(fields[3]),
                   "startTicks": int(fields[19]), "state": fields[0], "executable": None}
            if row["sid"] == self.leader or pid in self.members:
                try:
                    row["executable"] = os.readlink("/proc/" + str(pid) + "/exe")
                except FileNotFoundError:
                    require(row["state"] == "Z", "custody-executable")
            return row
        except FileNotFoundError:
            return None

    def pids(self):
        entries = list(Path("/proc").iterdir())
        require(len(entries) <= 8192, "custody-observation-bound")
        return [int(entry.name) for entry in entries if entry.name.isdecimal()]

    def observe(self):
        started = time.monotonic()
        for pid in self.pids():
            require(time.monotonic() - started < 1, "custody-observation-bound")
            row = self.process(pid)
            if row is None or row["sid"] != self.leader:
                continue
            if row["pid"] == self.leader and row["startTicks"] != self.token[2]:
                self.issues.append("leader-generation")
                continue
            previous = self.members.get(row["pid"])
            if previous and previous["startTicks"] != row["startTicks"]:
                self.issues.append("pid-reuse")
                continue
            if previous and previous["executable"] and row["executable"] and previous["executable"] != row["executable"]:
                self.issues.append("executable-change")
                continue
            self.members[row["pid"]] = row
        for pid, known in list(self.members.items()):
            row = self.process(pid)
            if row is not None and row["startTicks"] == known["startTicks"] and row["sid"] != self.leader:
                self.issues.append("observed-session-escape")
        require(not self.issues, "custody-uncertain")

    def send(self, pid, sig):
        known = self.members[pid]
        current = self.process(pid)
        if current is None:
            return
        require(current["startTicks"] == known["startTicks"], "custody-generation")
        require(current["executable"] == known["executable"] or current["state"] == "Z", "custody-executable")
        if current["sid"] != self.leader:
            self.issues.append("observed-session-escape")
        if current["state"] != "Z":
            # Exact observed generation/executable is rechecked immediately; no name/group/tree kill.
            if sig == signal.SIGKILL:
                self.forced_kill = True
            os.kill(pid, sig)

    def stop(self):
        failures = []
        for sig in (signal.SIGTERM, signal.SIGKILL):
            try:
                self.observe()
            except BaseException as error:
                failures.append(summary_failure(error))
            for pid in list(self.members):
                try:
                    self.send(pid, sig)
                except BaseException as error:
                    failures.append(summary_failure(error))
            if sig == signal.SIGTERM:
                time.sleep(0.2)
        if self.forced_kill:
            failures.append({"type": "Rejected", "category": "forced-kill-used"})
        return failures

    def finish(self):
        deadline = time.monotonic() + 3
        while time.monotonic() < deadline:
            self.observe()
            remaining = [self.process(pid) for pid in self.members]
            if all(row is None for row in remaining):
                return
            time.sleep(0.05)
        raise Rejected("observed-session-still-present")


class Runner:
    def __init__(self, root, directory):
        self.root, self.directory = root, directory
        self.records = []
        self.cancelled, self.spawning = False, False

    def run(self, name, command, timeout, build=False):
        require(not self.cancelled, "cancelled-no-new-command")
        require(os.name == "posix" and hasattr(os, "waitid") and hasattr(os, "WNOWAIT"), "linux-owned-session")
        environment = dict(os.environ, PYTHONDONTWRITEBYTECODE="1", MSBUILDDISABLENODEREUSE="1", DOTNET_CLI_USE_MSBUILD_SERVER="0")
        if build:
            environment["GITHUB_ACTIONS"] = "false"
        executable = shutil.which(command[0])
        require(executable is not None, "executable")
        started = time.monotonic()
        record = {"name": name, "timeoutSeconds": timeout, "timedOut": False, "cleanupFailed": False}
        process, session, primary, secondary = None, None, None, []
        output = tempfile.TemporaryFile()
        try:
            try:
                self.spawning = True
                try:
                    process = subprocess.Popen(command, cwd=self.root, env=environment, stdout=output,
                                               stderr=subprocess.STDOUT, start_new_session=True)
                    token = birth(process.pid)
                    require(token[0] == token[1] == process.pid, "session-owner")
                    session = Session(process.pid, token)
                    session.observe()
                    record.update(pid=process.pid, startTicks=token[2], configuredExecutable=os.path.realpath(executable),
                                  persistentData=False)
                finally:
                    self.spawning = False
                require(not self.cancelled, "owned-run-interrupted")
                observed = time.monotonic()
                while os.waitid(os.P_PID, process.pid, os.WEXITED | os.WNOWAIT | os.WNOHANG) is None:
                    if time.monotonic() - started >= timeout:
                        record["timedOut"] = True
                        raise Rejected("command-timeout")
                    require(os.fstat(output.fileno()).st_size <= MAX_BYTES, "command-output-size")
                    if time.monotonic() - observed >= 0.5:
                        session.observe()
                        observed = time.monotonic()
                    time.sleep(0.05)
            except BaseException as error:
                primary = error
            finally:
                self.spawning = False
                if process is not None:
                    if session is None:
                        try:
                            token = birth(process.pid)
                            require(token[0] == token[1] == process.pid, "session-owner")
                            session = Session(process.pid, token)
                        except BaseException as error:
                            secondary.append({"stage": "admission-custody", "failure": summary_failure(error)})
                    if session is not None:
                        try:
                            secondary.extend({"stage": "stop", "failure": failure} for failure in session.stop())
                        except BaseException as error:
                            secondary.append({"stage": "stop", "failure": summary_failure(error)})
                    try:
                        record["exitCode"] = process.wait(timeout=5)
                    except BaseException as error:
                        secondary.append({"stage": "wait", "failure": summary_failure(error)})
                    if session is not None:
                        record["observedSessionAbsent"] = False
                        try:
                            session.finish()
                            record["observedSessionAbsent"] = True
                        except BaseException as error:
                            secondary.append({"stage": "absence", "failure": summary_failure(error)})
                        record["forcedKillUsed"] = bool(getattr(session, "forced_kill", False))
                        record["observedMembers"] = list(session.members.values())
                        record["custodyIssues"] = list(getattr(session, "issues", []))
                        record["custodyScope"] = "Observed original-SID members only; no universal detached-newSID guarantee"
                raw, size = b"", 0
                try:
                    output.seek(0, os.SEEK_END)
                    size = output.tell()
                    if size > MAX_BYTES:
                        secondary.append({"stage": "output-limit", "failure": {"type": "Rejected", "category": "command-output-size"}})
                    output.seek(0)
                    raw = output.read(MAX_BYTES)
                    if build:
                        text = raw.decode("utf-8", "replace")
                        warnings = re.findall(r"\b([0-9]+) Warning\(s\)", text)
                        errors = re.findall(r"\b([0-9]+) Error\(s\)", text)
                        record["strictBuild"] = bool(warnings and errors and all(v == "0" for v in warnings + errors))
                        if primary is None and (not record["strictBuild"] or record.get("exitCode") != 0):
                            primary = Rejected("build-zero-warnings-errors")
                    record.update(outputBytes=size, outputSha256=digest(raw), cleanupFailed=bool(secondary),
                                  primaryFailure=summary_failure(primary) if primary else None, cleanupFailures=secondary)
                except BaseException as error:
                    secondary.append({"stage": "capture-io", "failure": summary_failure(error)})
                self.records.append(record)
        finally:
            try:
                output.close()
            except BaseException as error:
                secondary.append({"stage": "capture-close", "failure": summary_failure(error)})
        record["cleanupFailed"] = bool(secondary)
        evidence_write(self.directory / (name + ".json"), record, secondary)
        record["cleanupFailed"] = bool(secondary)
        if primary is not None or secondary:
            raise ProofFailure({"primaryFailure": summary_failure(primary) if primary else None,
                                "secondaryFailures": secondary}) from None
        return record["exitCode"], raw


def hosted(root, environ, git):
    require(environ.get("GITHUB_ACTIONS") == "true" and environ.get("RUNNER_ENVIRONMENT") == "github-hosted" and
            environ.get("GITHUB_REPOSITORY") == REPOSITORY and environ.get("RUNNER_OS") == "Linux" and
            environ.get("GITHUB_SERVER_URL") == "https://github.com" and
            environ.get("GITHUB_API_URL") == "https://api.github.com" and
            environ.get("GH_HOST", "github.com") == "github.com", "hosted-owner")
    require(str(root) == environ.get("GITHUB_WORKSPACE") and Path.cwd() == root and root.is_absolute() and
            root.resolve() == root and not root.is_symlink(), "workspace")
    event_path = Path(environ["GITHUB_EVENT_PATH"])
    temporary = Path(environ["RUNNER_TEMP"]).resolve()
    require(event_path.is_absolute() and event_path.is_relative_to(temporary), "event-path")
    event = json.loads(read(temporary, str(event_path.relative_to(temporary)), 512 * 1024), object_pairs_hook=no_duplicates)
    require(event["repository"]["full_name"] == REPOSITORY, "event-repository")
    candidate, event_sha = environ.get("LINE_CONTACT_HEAD_SHA", ""), environ.get("GITHUB_SHA", "")
    require(re.fullmatch("[0-9a-f]{40}", candidate) and re.fullmatch("[0-9a-f]{40}", event_sha), "sha")
    if environ.get("GITHUB_EVENT_NAME") == "pull_request":
        pr = event["pull_request"]
        require(pr["head"]["repo"]["full_name"] == pr["base"]["repo"]["full_name"] == REPOSITORY and
                pr["head"]["sha"] == candidate and environ.get("GITHUB_REF") == "refs/pull/" + str(event["number"]) + "/merge", "pr-origin")
    else:
        require(environ.get("GITHUB_EVENT_NAME") == "push" and event["after"] == candidate == event_sha and
                environ.get("GITHUB_REF", "").startswith("refs/heads/"), "event-kind")
    require(git("origin") in ("https://github.com/" + REPOSITORY, "https://github.com/" + REPOSITORY + ".git"), "git-origin")
    checked = git("HEAD")
    tree = git("HEAD^{tree}")
    require(checked == event_sha and re.fullmatch("[0-9a-f]{40}", tree), "checked-head")
    return {"eventSha": event_sha, "candidateSha": candidate, "checkedHead": checked, "checkedTree": tree}


def build_command():
    return ["dotnet", "build", PROJECT, "--configuration", "Release", "--no-restore",
            "--disable-build-servers", "-warnaserror", "-p:CI=false",
            "-p:UseSharedCompilation=false", "-nodeReuse:false"]


def test_command(frozen, phase, directory):
    methods = {r["class"] + "." + r["method"] for r in frozen["rows"] if phase == "green" or r["baseline"] is not None}
    return ["dotnet", "test", PROJECT, "--configuration", "Release", "--no-build", "--no-restore",
            "--disable-build-servers", "-p:UseSharedCompilation=false", "-nodeReuse:false",
            "-p:CI=false", "-p:VSTestCollect=", "-p:RunSettingsFilePath=", "--filter",
            "|".join("FullyQualifiedName=" + m for m in sorted(methods)),
            "--logger", "trx", "--results-directory", str(directory)]


def safe_raw(raw):
    require(len(raw) <= MAX_BYTES and b"\x00" not in raw, "unsafe-encoding")
    try:
        text = raw.decode("utf-8-sig", "strict")
    except UnicodeError:
        raise Rejected("unsafe-encoding") from None
    import html
    escape = re.compile(r'\\(?:u[0-9a-fA-F]{4}|["\\/bfnrt])')
    for _ in range(4):
        normalized = html.unescape(text)
        normalized = escape.sub(lambda m: json.loads('"' + m.group(0) + '"'), normalized)
        require(len(normalized) <= MAX_BYTES and "\x00" not in normalized and
                not any(0xD800 <= ord(c) <= 0xDFFF for c in normalized), "unsafe-normalization")
        if normalized == text:
            break
        text = normalized
    else:
        raise Rejected("unsafe-normalization")
    credential = r'''(?ix)(?<![a-z0-9_])["']?(?:access[_-]?token|refresh[_-]?token|id[_-]?token|client[_-]?secret|password|authorization|api[_-]?key)["']?\s*[:=]'''
    require(not re.search(credential, text) and not re.search(r"Bearer\s+\S+|eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", text, re.I), "unsafe-raw")
    emails = re.findall(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", text)
    require(all(e.lower() in {"private@example.test", "info@maliev.com", "manufacturing@maliev.com",
                             "nda@maliev.com", "career@maliev.com", "support@maliev.com"} for e in emails), "unsafe-raw")


def trx_phase(root, directory, frozen, phase, runner):
    with tempfile.TemporaryDirectory(prefix=".line-contact-owned-", dir=root) as temporary:
        result_dir = Path(temporary)
        code, _ = runner.run(phase + "-tests", test_command(frozen, phase, result_dir), 480)
        paths = list(result_dir.rglob("*.trx"))
        require(len(paths) == 1, "single-trx")
        raw = read(root, str(paths[0].relative_to(root)))
        require(code == (1 if phase == "baseline" else 0), "test-exit")
        result = verify_trx(raw, frozen, phase, root / frozen["assembly"], root)
        safe_raw(raw)
        result["assemblySha256"] = digest(read(root, frozen["assembly"], 128 * 1024 * 1024))
        target = directory / ("raw-" + phase)
        target.mkdir(mode=0o700)
        (target / (phase + ".trx")).write_bytes(raw)
        return result


def failure(error):
    return summary_failure(error)


def execute(root, directory, frozen, runner, fetch, identity=None):
    saved = {path: read(root, path) for path in PRODUCERS}
    require(all(digest(saved[p]) == frozen["sources"]["candidate"][p] for p in PRODUCERS), "saved-source")
    primary, cleanup, baseline, green, rebuilt = None, [], None, None, False
    try:
        check_sources(root, frozen["sources"]["candidate"])
        originals = {}
        for path in PRODUCERS:
            raw = fetch(path)
            require(digest(raw) == frozen["sources"]["baseline"][path] and raw != saved[path], "baseline-source")
            originals[path] = raw
        for path, raw in originals.items():
            replace(root, path, raw)
        check_sources(root, {**frozen["sources"]["candidate"], **frozen["sources"]["baseline"]})
        runner.run("baseline-build", build_command(), 900, build=True)
        baseline = trx_phase(root, directory, frozen, "baseline", runner)
        check_sources(root, {**frozen["sources"]["candidate"], **frozen["sources"]["baseline"]})
    except BaseException as error:
        primary = error
    finally:
        for path in PRODUCERS:
            try:
                replace(root, path, saved[path])
            except BaseException as error:
                cleanup.append("restore:" + type(error).__name__)
        try:
            check_sources(root, frozen["sources"]["candidate"])
            runner.run("restored-candidate-build", build_command(), 900, build=True)
            rebuilt = True
        except BaseException as error:
            cleanup.append("rebuild:" + type(error).__name__)
    receipt = {"schema": "web-line-contact-proof.v1", "baseline": baseline, "green": None,
               "contractSha256": CONTRACT_SHA256, "baselineSourceCommit": BASELINE,
               "sourceHashes": frozen["sources"], "overlayPaths": list(PRODUCERS),
               "restoredStrictBuildPassed": rebuilt, "finalSourcesRestored": False,
               "primaryFailure": failure(primary) if primary else None, "cleanupFailureTypes": cleanup,
               **(identity or {}),
               "fullSuiteAcceptance": False, "providerFriendshipAcceptance": False, "wholeOriginalSourceShaClosure": False}
    try:
        if primary is None and not cleanup:
            green = trx_phase(root, directory, frozen, "green", runner)
            require(all(green["identities"].get(k) == v for k, v in baseline["identities"].items()), "cross-phase-case-identity")
            check_sources(root, frozen["sources"]["candidate"])
            receipt["green"] = green
    except BaseException as error:
        primary = error
        receipt["primaryFailure"] = failure(error)
    finally:
        try:
            check_sources(root, frozen["sources"]["candidate"])
            receipt["finalSourcesRestored"] = True
        except BaseException as error:
            cleanup.append("final-source:" + type(error).__name__)
        evidence_write(directory / "receipt.json", receipt, cleanup)
    if primary is not None or cleanup:
        raise ProofFailure({"primaryFailure": summary_failure(primary) if primary else None,
                            "secondaryFailures": cleanup}) from None
    return receipt


def publish_green(root, directory):
    raw = read(root, str((directory / "raw-green/green.trx").relative_to(root)))
    safe_raw(raw)
    target = root / "line-contact-candidate-test-results"
    target.mkdir(mode=0o700, exist_ok=False)
    (target / "line-contact-candidate.trx").write_bytes(raw)
    require(read(root, "line-contact-candidate-test-results/line-contact-candidate.trx") == raw, "early-trx-readback")


def main():
    root = Path(os.environ.get("GITHUB_WORKSPACE", ""))
    require(os.environ.get("GITHUB_ACTIONS") == "true" and os.environ.get("RUNNER_ENVIRONMENT") == "github-hosted" and
            os.environ.get("GITHUB_REPOSITORY") == REPOSITORY and Path.cwd() == root and root.is_absolute(), "hosted-owner")
    directory = root / "line-contact-regression-proof"
    directory.mkdir(mode=0o700, exist_ok=False)
    runner = Runner(root, directory)
    def interrupted(signum, frame):
        runner.cancelled = True
        if not runner.spawning:
            raise Rejected("owned-run-interrupted")
    previous, primary, secondary = {}, None, []
    try:
        for sig in (signal.SIGTERM, signal.SIGINT):
            previous[sig] = signal.signal(sig, interrupted)
        def git(argument):
            command = ["git", "remote", "get-url", "origin"] if argument == "origin" else ["git", "rev-parse", argument]
            code, raw = runner.run("git-" + ("origin" if argument == "origin" else "tree" if "tree" in argument else "head"), command, 15)
            require(code == 0, "git")
            return raw.decode("ascii").strip()
        identity = hosted(root, os.environ, git)
        frozen = contract(root)
        check_sources(root, frozen["sources"]["candidate"])
        regular(root, frozen["assembly"])
        def fetch(path):
            code, raw = runner.run("fetch-" + str(PRODUCERS.index(path)), [
                "gh", "api", "--hostname", "github.com", "repos/" + REPOSITORY + "/contents/" + path + "?ref=" + BASELINE,
                "-H", "Accept: application/vnd.github.raw+json"], 60)
            require(code == 0, "fetch")
            return raw
        receipt = execute(root, directory, frozen, runner, fetch, identity)
        publish_green(root, directory)
        evidence_write(directory / "receipt.json", receipt, secondary)
    except BaseException as error:
        primary = error
    finally:
        for sig, handler in previous.items():
            try:
                signal.signal(sig, handler)
            except BaseException as error:
                secondary.append({"stage": "signal-restore", "failure": summary_failure(error)})
    if primary is not None or secondary:
        raise ProofFailure({"primaryFailure": summary_failure(primary) if primary else None,
                            "secondaryFailures": secondary}) from None
    print("Owned LINE baseline19 and candidate22 qualified; source restored. Full/provider/source closure remain false.")


if __name__ == "__main__":
    main()
