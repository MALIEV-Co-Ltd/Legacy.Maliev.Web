"""Review proposal: Root-bound, single transient SDK unit; no shell fallback."""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import selectors
import signal
import subprocess
import sys
import time
import uuid

BASE = "66d220af825a6d4829d1b97512e21b6486a08c87"
WORKERS = {"dotnet", "testhost", "MSBuild", "VBCSCompiler", "datacollector", "esbuild"}
REVIEWED = {"run_focus.py", "verify_custody.py", "verify_focus_trx.py", "expected-cases.json", "custody-manifest.json", ".github/workflows/web-real-upload-focused.yml"}
PERMIT_FIELDS = {"owner", "issuer", "base", "scope", "correctionPatchSha256", "expectedExecutions", "custodyManifestSha256", "routeSourceSha256", "repository", "oneUseNonce", "githubRunAttempt", "notBeforeUtc", "expiresUtc"}

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def write(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")

def utc(value):
    parsed = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.utcoffset() != dt.timedelta(0):
        raise ValueError("UTC authority timestamps required")
    return parsed

def expiry(permit):
    # First workflow step records the job clock; setup time consumes this lease too.
    job_start = int(os.environ["FOCUS_JOB_STARTED_UNIX"])
    now = dt.datetime.now(dt.timezone.utc)
    start = dt.datetime.fromtimestamp(job_start, dt.timezone.utc)
    if not 0 <= (now - start).total_seconds() < 2060:
        raise ValueError("Actual job clock missing, future or exhausted")
    return min(utc(permit["expiresUtc"]), start + dt.timedelta(seconds=2100))

def census():
    memory = dict(re.findall(r"^(MemAvailable):\s+(\d+)", Path("/proc/meminfo").read_text(), re.M))
    if int(memory.get("MemAvailable", "0")) < 4194304:
        raise ValueError("4096 MiB memory guard failed")
    for entry in Path("/proc").iterdir():
        if entry.name.isdecimal():
            try:
                name = (entry / "comm").read_text().strip()
            except FileNotFoundError:
                continue
            if name in WORKERS:
                raise ValueError("Existing SDK or asset worker blocks hosted admission")
    return {"memAvailableKiB": int(memory["MemAvailable"]), "sdkOrAssetWorkers": 0}

def authority(a):
    if sys.platform != "linux" or os.environ.get("GITHUB_ACTIONS") not in {"true", "false"} or not os.environ.get("GITHUB_RUN_ID"):
        raise ValueError("Linux hosted runner required")
    policy = json.loads(a.policy.read_text())
    expected = {"memoryAvailableKiB": 4194304, "memoryMaxBytes": 3221225472,
                "swapMaxBytes": 0, "cpuQuotaPercent": 100, "tasksMax": 512,
                "leaseSeconds": 2100, "phaseSeconds": 1200, "reserveSeconds": 40,
                "logMaxBytes": 16777216}
    if any(policy.get(k) != v for k, v in expected.items()) or policy.get("base") != BASE:
        raise ValueError("Reviewed unchanged admission bounds required")
    if policy.get("scope") != "single-corrected-real-upload-attempt-no-full-suite-no-producer-hosts" or policy.get("expectedExecutions") != {"failed-boundaries": 3, "preservation-controls": 56} or policy.get("correctionPatchSha256") != "854464bef9731d68e0d734d60a33800ec3d4a882b1569c90161004507fb53e76":
        raise ValueError("Hardcoded single corrected attempt required; no wildcard phase grant")
    if not re.fullmatch(r"[0-9a-f]{64}", policy.get("approvedPermitSha256") or ""):
        raise ValueError("Independent Root authority missing; no enrollment permitted")
    if digest(a.permit) != policy["approvedPermitSha256"]:
        raise ValueError("Exact externally approved Root permit required")
    if set(policy["reviewedFiles"]) != REVIEWED:
        raise ValueError("Complete reviewed route file set required")
    route_map = {}
    for name, seal in policy["reviewedFiles"].items():
        reviewed = a.policy.parent.parent / name if name.startswith(".github/") else a.policy.parent / name
        if not re.fullmatch(r"[0-9a-f]{64}", seal or "") or digest(reviewed) != seal:
            raise ValueError("Reviewed route source seal missing or changed: " + name)
        route_map[name] = seal
    for folder in ("custody", "postimage"):
        directory = a.policy.parent / folder
        if not directory.is_dir() or directory.is_symlink():
            raise ValueError("Frozen custody directory required")
        for path in sorted(directory.rglob("*")):
            if path.is_symlink():
                raise ValueError("Custody symlink not permitted")
            if path.is_file():
                route_map[path.relative_to(a.policy.parent).as_posix()] = digest(path)
    route_source_sha = hashlib.sha256(json.dumps(route_map, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    permit = json.loads(a.permit.read_text())
    if set(permit) != PERMIT_FIELDS or type(permit.get("githubRunAttempt")) is not int:
        raise ValueError("Exact Root permit schema required; unknown fields rejected")
    manifest = json.loads((a.policy.parent / "custody-manifest.json").read_text())
    for key in ("owner", "issuer", "base", "scope", "correctionPatchSha256", "expectedExecutions"):
        if permit.get(key) != policy[key]:
            raise ValueError("Root permit scope mismatch: " + key)
    if permit.get("custodyManifestSha256") != digest(a.policy.parent / "custody-manifest.json"):
        raise ValueError("Root permit must bind full frozen custody")
    if permit.get("routeSourceSha256") != route_source_sha or permit.get("repository") != "MALIEV-Co-Ltd/Legacy.Maliev.Web" or os.environ.get("GITHUB_REPOSITORY") != permit["repository"] or os.environ.get("GITHUB_RUN_ATTEMPT") != "1" or permit.get("githubRunAttempt") != 1 or not re.fullmatch(r"[0-9a-f]{32}", permit.get("oneUseNonce") or ""):
        raise ValueError("Root permit must bind exact reviewed route source, repository, nonce and first attempt")
    now = dt.datetime.now(dt.timezone.utc)
    start, end = utc(permit["notBeforeUtc"]), utc(permit["expiresUtc"])
    if not start <= now < end or not 40 < (end - now).total_seconds() <= 2100 or not 0 < (end - start).total_seconds() <= 2100:
        raise ValueError("Finite unrenewed Root lease required")
    if manifest["base"] != BASE or manifest["expectedExecutions"] != policy["expectedExecutions"]:
        raise ValueError("Custody scope mismatch")
    if manifest["dependencyPins"] != {"ServiceDefaults": "3c790ba6414b2a539f24aabb6948549ffd81a86b", "CompatibilityContracts": "78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7"}:
        raise ValueError("Exact dependency pair required")
    if (expiry(permit) - now).total_seconds() <= 40:
        raise ValueError("Job/Root cleanup reserve exhausted")
    return policy, permit, manifest

def manager(unit):
    raw = subprocess.check_output(["sudo", "-n", "systemctl", "show", unit, "--no-pager",
        "--property=Id,InvocationID,ControlGroup,ActiveState,RuntimeMaxUSec,KillMode,SendSIGKILL,ExecMainStartTimestampMonotonic,ExecMainPID"], timeout=3, text=True)
    return dict(line.split("=", 1) for line in raw.splitlines() if "=" in line)

def identity(unit):
    info = manager(unit)
    group = info.get("ControlGroup", "")
    if not group.startswith("/system.slice/") or info.get("Id") != unit or not re.fullmatch(r"[0-9a-f]{32}", info.get("InvocationID", "")):
        raise ValueError("Exact transient unit identity unavailable")
    target = Path("/sys/fs/cgroup") / group.lstrip("/")
    stat = target.stat()
    quota, period = (target / "cpu.max").read_text().split()
    if (target / "memory.max").read_text().strip() != "3221225472" or (target / "memory.swap.max").read_text().strip() != "0" or not quota.isdecimal() or not 0 < int(quota) <= int(period) or (target / "pids.max").read_text().strip() != "512":
        raise ValueError("Actual SDK unit resource caps mismatch")
    if info.get("RuntimeMaxUSec") not in {"35min", "2100s"} or info.get("KillMode") != "control-group" or info.get("SendSIGKILL") != "yes":
        raise ValueError("Actual whole-unit lease mismatch")
    return {"unit": unit, "invocationId": info["InvocationID"], "cgroup": group,
            "device": stat.st_dev, "inode": stat.st_ino,
            "actualStartMonotonicUsec": info["ExecMainStartTimestampMonotonic"],
            "workerPid": int(info["ExecMainPID"]), "executable": str(Path(sys.executable).resolve()),
            "hostBootId": Path("/proc/sys/kernel/random/boot_id").read_text().strip()}

def same(context):
    return identity(context["unit"]) == context

def report_secondary(errors):
    for error in errors:
        try:
            print("Secondary cleanup/receipt failure: " + str(error), file=sys.stderr)
        except BaseException:
            pass

def finish(primary, secondary):
    report_secondary(secondary)
    if primary is not None:
        raise primary.with_traceback(primary.__traceback__)
    if secondary:
        raise secondary[0].with_traceback(secondary[0].__traceback__)

def phase(command, source, evidence, name, expires, maximum=1200, allow_failure=False):
    remaining = int((expires - dt.datetime.now(dt.timezone.utc)).total_seconds())
    budget = min(maximum, remaining - 40)
    if budget <= 0:
        raise ValueError("No admitted phase budget remains")
    log = evidence / (name + ".log")
    child = output = selector = None
    primary = None
    secondary = []
    observed = {}
    code = None
    count = 0
    started = time.monotonic()
    try:
        output = log.open("xb")
        child = subprocess.Popen(command, cwd=source, stdin=subprocess.DEVNULL,
                                 stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        observed = {"pid": child.pid, "createdUtc": dt.datetime.now(dt.timezone.utc).isoformat(), "argvExecutable": command[0]}
        try:
            raw = Path("/proc", str(child.pid), "stat").read_text()
            observed["actualProcStartTicks"] = raw[raw.rfind(")") + 2:].split()[19]
            observed["actualExecutable"] = os.readlink("/proc/" + str(child.pid) + "/exe")
        except FileNotFoundError:
            observed["actualProcIdentity"] = "exited-before-observation"
        selector = selectors.DefaultSelector()
        selector.register(child.stdout, selectors.EVENT_READ)
        eof = False
        while not eof or child.poll() is None:
            if time.monotonic() - started >= budget:
                raise TimeoutError("Owned phase exceeded finite wall bound: " + name)
            for key, _ in selector.select(timeout=.2):
                chunk = os.read(key.fileobj.fileno(), 65536)
                if not chunk:
                    selector.unregister(child.stdout)
                    eof = True
                    continue
                admitted = min(len(chunk), 16777216 - count)
                output.write(chunk[:admitted])
                count += admitted
                if admitted != len(chunk):
                    raise ValueError("Owned phase exceeded hard16MiB output bound: " + name)
        code = child.wait(timeout=3)
        if code and not allow_failure:
            raise ValueError("Phase failed: " + name)
    except BaseException as failure:
        primary = failure
    finally:
        try:
            if child is not None and child.poll() is None:
                child.terminate()
                try:
                    child.wait(timeout=2)
                except subprocess.TimeoutExpired:
                    child.kill()
                    child.wait(timeout=3)
        except BaseException as failure:
            secondary.append(failure)
        for resource in (selector, child.stdout if child is not None else None, output):
            if resource is not None:
                try:
                    resource.close()
                except BaseException as failure:
                    secondary.append(failure)
        try:
            write(evidence / (name + ".json"), {"command": command, "exitCode": code,
                  "error": None if primary is None else type(primary).__name__ + ": " + str(primary),
                  "secondaryErrors": [str(e) for e in secondary], "ownedProcess": observed,
                  "directHandleExited": child is None or child.poll() is not None,
                  "elapsedSeconds": time.monotonic() - started, "logBytes": count,
                  "logSha256": digest(log) if log.is_file() else None})
        except BaseException as failure:
            secondary.append(failure)
    finish(primary, secondary)
    return code

def worker(a):
    policy, permit, manifest = authority(a)
    barrier = a.evidence / "unit-context.json"
    limit = time.monotonic() + 12
    while not barrier.exists():
        if time.monotonic() >= limit:
            raise ValueError("Manager identity barrier missing")
        time.sleep(.1)
    context = json.loads(barrier.read_text())
    if not same(context) or not any(line.endswith(":" + context["cgroup"]) for line in Path("/proc/self/cgroup").read_text().splitlines()):
        raise ValueError("SDK worker not in exact reviewed unit; no shell fallback")
    root = a.policy.parent
    expires = expiry(permit)
    os.environ.update(GITHUB_ACTIONS="false", DOTNET_CLI_UI_LANGUAGE="en", CI="false",
        MSBUILDDISABLENODEREUSE="1", DOTNET_CLI_USE_MSBUILD_SERVER="0",
        MalievWorkspaceRoot=str(a.source / ".dependencies"), MALIEV_WEB_STARTUP_PROOF="hosted-owned",
        PLAYWRIGHT_BROWSERS_PATH=str(a.evidence / "browser-cache"))
    for name, sha in manifest["dependencyPins"].items():
        repo = a.source / ".dependencies" / ("Legacy.Maliev.ServiceDefaults" if name == "ServiceDefaults" else "Legacy.Maliev.CompatibilityContracts")
        actual = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo, timeout=5, text=True).strip()
        if actual != sha or subprocess.check_output(["git", "status", "--porcelain"], cwd=repo, timeout=5):
            raise ValueError("Exact clean dependency checkout required")
    common = [sys.executable, str(root / "verify_custody.py"), "--source", str(a.source), "--manifest", str(root / "custody-manifest.json")]
    phase(common + ["--postimage-root", str(root / "postimage"), "--receipt", str(a.evidence / "custody-initial.json"), "--apply"], a.source, a.evidence, "custody-initial", expires)
    tooling = a.source.parent / "tooling"
    installer = tooling / "externals/install-dotnet.sh"
    if subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=tooling, timeout=5, text=True).strip() != "a98b56852c35b8e3190ac28c8c2271da59106c68" or digest(installer) != "082f7685e156738a1b2e2ed8381a621870d4ce8e8c59278034556f05c186eb2e":
        raise ValueError("Exact pinned setup-dotnet installer bytes required")
    write(a.evidence / "sdk-installer-custody.json", {"commit": "a98b56852c35b8e3190ac28c8c2271da59106c68", "path": "externals/install-dotnet.sh", "gitBlob": "bd13ffa6656fe776c95b561fe4df640918867523", "sha256": digest(installer)})
    sdk = a.evidence / "sdk-cache"
    phase(["bash", str(installer), "--version", "10.0.401", "--install-dir", str(sdk), "--no-path"], a.source, a.evidence, "sdk-install", expires)
    os.environ["DOTNET_ROOT"] = str(sdk)
    os.environ["PATH"] = str(sdk) + os.pathsep + os.environ["PATH"]
    for executable, wanted in (("dotnet", "10.0.401"), ("node", "v24.21.0"), ("npm", "11.19.0")):
        phase([executable, "--version"], a.source, a.evidence, executable + "-version", expires, 30)
        if (a.evidence / (executable + "-version.log")).read_text().strip() != wanted:
            raise ValueError("Exact native/asset tool version required: " + executable)
    phase(["npm", "run", "ci"], a.source / "Legacy.Maliev.Web", a.evidence, "assets", expires)
    phase(["git", "diff", "--exit-code", "--", "Legacy.Maliev.Web/wwwroot/dist"], a.source, a.evidence, "dist-unchanged", expires)
    phase(common + ["--verify-patched", "--receipt", str(a.evidence / "custody-after-assets.json")], a.source, a.evidence, "custody-after-assets", expires)
    project = "Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj"
    phase(["dotnet", "build", project, "--configuration", "Release"], a.source, a.evidence, "build", expires)
    summary = (a.evidence / "build.log").read_text(errors="replace")
    warnings = re.findall(r"^\s*(\d+) Warning\(s\)\s*$", summary, re.M)
    errors = re.findall(r"^\s*(\d+) Error\(s\)\s*$", summary, re.M)
    if not warnings or not errors or any(int(n) != 0 for n in warnings + errors):
        raise ValueError("Build requires observed zero warnings and zero errors")
    phase(["pwsh", "-NoProfile", "-File", "./Legacy.Maliev.Web.Tests/bin/Release/net10.0/playwright.ps1", "install", "--with-deps", "chromium"], a.source, a.evidence, "chromium", expires, 480)
    browser_identities = []
    if os.uname().machine != "x86_64":
        raise ValueError("Reviewed Ubuntu x64 browser route required")
    for folder, platform, executable in (("chromium-1228", "chrome-linux64", "chrome"), ("chromium_headless_shell-1228", "chrome-headless-shell-linux64", "chrome-headless-shell")):
        installed = a.evidence / "browser-cache" / folder / platform / executable
        if not installed.is_file() or installed.is_symlink():
            raise ValueError("Exact owned Chromium revision1228 executable required")
        phase([str(installed), "--version"], a.source, a.evidence, folder + "-version", expires, 30)
        output = (a.evidence / (folder + "-version.log")).read_text().strip()
        if not re.fullmatch(r"[^\r\n]+\s149\.0\.7827\.55", output):
            raise ValueError("Exact actual Chromium149.0.7827.55 required")
        browser_identities.append({"path": str(installed), "versionOutput": output, "revision": 1228, "sha256": digest(installed)})
    write(a.evidence / "browser-identity.json", browser_identities)
    results = a.evidence / "trx"
    for group, key in (("failed-boundaries", "focusedFilter"), ("preservation-controls", "controlsFilter")):
        phase(["dotnet", "test", project, "--configuration", "Release", "--no-build", "--no-restore",
              "-p:CI=false", "-p:VSTestCollect=", "-p:RunSettingsFilePath=", "--filter", manifest[key],
              "--logger", "trx", "--results-directory", str(results / group)],
              a.source, a.evidence, group, expires, allow_failure=True)
    phase([sys.executable, str(root / "verify_focus_trx.py"), "--results-root", str(results),
           "--expected", str(root / "expected-cases.json"), "--receipt", str(a.evidence / "strict-trx.json")],
          a.source, a.evidence, "strict-trx", expires)
    write(a.evidence / "worker-result.json", {"status": "FOCUSED_BOUNDARIES_AND_CONTROLS_PASSED", "fullSuiteExecuted": False})

class ControllerCancelled(RuntimeError):
    pass

def clean_unit(unit, context, cleanup):
    info = manager(unit)
    if info.get("ActiveState") in {"active", "activating", "deactivating"}:
        if context is None:
            context = identity(unit)
        if not same(context):
            raise ValueError("Cleanup ownership changed; resource preserved")
        cleanup["ownerContext"] = context
        subprocess.run(["sudo", "-n", "systemctl", "stop", unit], check=True, timeout=5)
    if context is not None:
        cleanup["ownerContext"] = context
    target = Path("/sys/fs/cgroup") / (context["cgroup"].lstrip("/") if context else "system.slice/" + unit)
    if target.exists():
        if context is None or (target.stat().st_dev, target.stat().st_ino) != (context["device"], context["inode"]):
            raise ValueError("Cleanup cgroup identity changed")
        if "populated 1" in (target / "cgroup.events").read_text():
            if not same(context):
                raise ValueError("Cleanup invocation changed before exact termination")
            subprocess.run(["sudo", "-n", "systemctl", "kill", "--signal=SIGKILL", "--kill-whom=all", unit], check=True, timeout=3)
    subprocess.run(["sudo", "-n", "systemctl", "reset-failed", unit], check=False, timeout=3, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    end = time.monotonic() + 3
    while target.exists() and time.monotonic() < end:
        time.sleep(.1)
    absent = not target.exists() and not target.is_symlink()
    cleanup["managerState"] = manager(unit).get("ActiveState")
    if not absent or cleanup["managerState"] in {"active", "activating", "deactivating"}:
        raise ValueError("Exact unit cleanup did not settle")
    cleanup["physicalAbsence"] = True

def create_marker(item, marker):
    with (item["path"] / ".route-owner").open("x", encoding="ascii") as handle:
        stat = os.fstat(handle.fileno())
        item["markerIdentity"] = (stat.st_dev, stat.st_ino)
        handle.write(marker)
    item["marked"] = True

def remove_cache(path, evidence, marker, marked, cleanup, key, marker_identity=None):
    if path.is_symlink() or path.resolve().parent != evidence:
        raise ValueError("Cache cleanup ownership uncertain; preserved")
    if marked:
        owner = path / ".route-owner"
        if owner.is_symlink() or not owner.is_file() or owner.read_text(encoding="ascii") != marker:
            raise ValueError("Cache marker uncertain; preserved")
        shutil.rmtree(path)
    else:
        # A partially written marker can be removed only by its captured inode.
        owner = path / ".route-owner"
        if marker_identity is not None:
            stat = owner.lstat()
            if owner.is_symlink() or not owner.is_file() or (stat.st_dev, stat.st_ino) != marker_identity:
                raise ValueError("Partial marker ownership changed; preserved")
            owner.unlink()
        # Empty-only removal never traverses uncertain children.
        path.rmdir()
    cleanup[key] = not path.exists() and not path.is_symlink()
    if not cleanup[key]:
        raise ValueError("Owned cache removal not observed")

def controller(a):
    _, permit, _ = authority(a)
    admission = census()
    a.evidence.mkdir(parents=False, exist_ok=False)
    unit = "web-real-upload-" + uuid.uuid4().hex + ".service"
    context = None
    enrollment_attempted = False
    caches = []
    marker = digest(a.permit)
    cleanup = {"unit": unit, "physicalAbsence": False}
    primary = None
    secondary = []
    cancellation = []
    handlers = {}
    def defer(signum, _frame):
        cancellation.append(signum)
    def checkpoint():
        if cancellation:
            raise ControllerCancelled("Controller signal" + str(cancellation[0]) + " deferred until ownership acquired")
    try:
        for signum in (signal.SIGTERM, signal.SIGINT):
            handlers[signum] = signal.signal(signum, defer)
        write(a.evidence / "admission.json", {**admission, "actualGithubRunId": os.environ["GITHUB_RUN_ID"], "actualRouteCommit": os.environ["GITHUB_SHA"], "routeSourceSha256": permit["routeSourceSha256"], "oneUseNonce": permit["oneUseNonce"], "expiresUtc": expiry(permit).isoformat()})
        for name, key in (("browser-cache", "browserCacheAbsent"), ("sdk-cache", "sdkCacheAbsent")):
            checkpoint()
            path = a.evidence / name
            path.mkdir()
            item = {"path": path, "marked": False, "key": key, "markerIdentity": None}
            caches.append(item)
            create_marker(item, marker)
        checkpoint()
        invocation = [sys.executable, str(Path(__file__).resolve()), "--worker", "--policy", str(a.policy),
                      "--permit", str(a.permit), "--source", str(a.source), "--evidence", str(a.evidence)]
        env = ["--setenv=" + key + "=" + os.environ[key] for key in ("GITHUB_ACTIONS", "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "GITHUB_SHA", "GITHUB_REPOSITORY", "FOCUS_JOB_STARTED_UNIX", "PATH", "HOME")]
        enrollment_attempted = True
        subprocess.run(["sudo", "-n", "systemd-run", "--unit=" + unit, "--service-type=exec",
            "--property=User=" + str(os.getuid()), "--property=Group=" + str(os.getgid()),
            "--property=WorkingDirectory=" + str(a.source), "--property=RuntimeMaxSec=2100",
            "--property=MemoryMax=3G", "--property=MemorySwapMax=0", "--property=CPUQuota=100%",
            "--property=TasksMax=512", "--property=KillMode=control-group", "--property=SendSIGKILL=yes",
            "--property=TimeoutStopSec=2", *env, "--", *invocation], check=True, timeout=10)
        context = identity(unit)
        checkpoint()
        write(a.evidence / "unit-context.json", context)
        expires = expiry(permit)
        while manager(unit).get("ActiveState") in {"active", "activating"}:
            checkpoint()
            if dt.datetime.now(dt.timezone.utc) >= expires - dt.timedelta(seconds=40):
                raise TimeoutError("Root lease cleanup reserve reached")
            time.sleep(.5)
        checkpoint()
        if not (a.evidence / "worker-result.json").exists():
            raise ValueError("Focused worker failed; inspect raw phase receipts")
    except BaseException as failure:
        primary = failure
    finally:
        if enrollment_attempted:
            try:
                clean_unit(unit, context, cleanup)
            except BaseException as failure:
                secondary.append(failure)
        else:
            cleanup["physicalAbsence"] = True
            cleanup["managerState"] = "not-enrolled"
        # Never delete caches potentially still used by an unconfirmed unit.
        if cleanup["physicalAbsence"]:
            for item in caches:
                try:
                    remove_cache(item["path"], a.evidence, marker, item["marked"], cleanup, item["key"], item["markerIdentity"])
                except BaseException as failure:
                    secondary.append(failure)
        if cancellation and primary is None:
            primary = ControllerCancelled("Controller cancellation delivered after cleanup")
        cleanup["originalError"] = None if primary is None else type(primary).__name__ + ": " + str(primary)
        cleanup["secondaryErrors"] = [str(e) for e in secondary]
        cleanup["deferredSignals"] = cancellation.copy()
        try:
            write(a.evidence / "cleanup.json", cleanup)
        except BaseException as failure:
            secondary.append(failure)
        for signum, previous in handlers.items():
            try:
                signal.signal(signum, previous)
            except BaseException as failure:
                secondary.append(failure)
    if cancellation and primary is None:
        primary = ControllerCancelled("Controller cancellation delivered after cleanup")
    finish(primary, secondary)

def main():
    parser = argparse.ArgumentParser()
    for name in ("policy", "permit", "source", "evidence"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--worker", action="store_true")
    a = parser.parse_args()
    for name in ("policy", "permit", "source", "evidence"):
        setattr(a, name, getattr(a, name).resolve())
    worker(a) if a.worker else controller(a)

if __name__ == "__main__":
    main()
