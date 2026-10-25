"""Observer controls only: no SDK, application hosts, or production PATH changes."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "scripts/web-full-test-observer.py"
HOOK = ROOT / "scripts/web-full-test-observer.bash"
FROZEN = (b'if [[ "$USE_LOCAL_MALIEV_DEPENDENCIES" == "true" ]]; then\n'
          b'  export GITHUB_ACTIONS=false\nfi\n'
          b'dotnet test "Legacy.Maliev.Web.slnx" --configuration Release --no-build --no-restore\n')
MOD = None
RESOURCES = []


def record_resource(process, purpose):
    birth = MOD.identity(process.pid)
    row = {"owner": 723, "purpose": purpose, "pid": process.pid, "birth": birth, "birthUnobserved": int(birth is None)}
    RESOURCES.append(row)
    return row


def record_exit(process, row):
    row.update(exit=process.poll(), absent=int(MOD.verified_absence(row["birth"], MOD.identity(process.pid))))


class Portable(unittest.TestCase):
    def test_implementation_exists(self):
        self.assertTrue(SOURCE.is_file(), "bounded full-test observer is missing")

    def setUp(self):
        global MOD
        if not SOURCE.is_file():
            if self._testMethodName != "test_implementation_exists":
                self.skipTest("implementation missing")
            return
        if MOD is None:
            spec = importlib.util.spec_from_file_location("observer", SOURCE)
            MOD = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(MOD)

    def test_frozen_bytes_and_sha(self):
        self.assertEqual(MOD.TEST_SCRIPT, FROZEN)
        self.assertEqual(MOD.TEST_SHA, hashlib.sha256(FROZEN).hexdigest())
        self.assertEqual(MOD.TEST_SHA, "d282675eaa0a19f988e9034aa28992cda1ce3e1faa99fe5a8768b4b534ed2ff3")

    def test_match_is_exact_bounded_and_scoped(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "step.sh"
            p.write_bytes(FROZEN)
            self.assertTrue(MOD.matches_script(p, Path(d)))
            self.assertFalse(MOD.matches_script(p, Path(d) / "other"))
            for data in (FROZEN + b"\n", FROZEN.replace(b"Web.slnx", b"Other.slnx"), b"x" * 4097):
                p.write_bytes(data)
                self.assertFalse(MOD.matches_script(p, Path(d)))
            p.unlink()
            self.assertFalse(MOD.matches_script(p, Path(d)))

    def test_numeric_parser_never_exports_unknown_or_private_values(self):
        text = "VmRSS: 12 kB\nThreads: 4\nSecret: 99\nVmHWM: private\n"
        self.assertEqual(MOD.numbers(text, {"VmRSS", "Threads", "VmHWM"}), {"VmRSS": 12288, "Threads": 4})
        self.assertEqual(MOD.numbers("Threads: -1\nVmRSS: " + "9" * 50, {"Threads", "VmRSS"}), {})

    def test_cgroup_membership_is_contained(self):
        self.assertEqual(MOD.cgroup_relative("0::/"), "")
        self.assertEqual(MOD.cgroup_relative("0::/owned/job"), "owned/job")
        for text in ("0::/../private", "0:://private", "1:cpu:/private", "private-sentinel"):
            with self.assertRaises(ValueError):
                MOD.cgroup_relative(text)

    def test_record_is_numeric_and_finite(self):
        self.assertLessEqual(len(MOD.encode_record({"event": 1, "nested": [{"rss": 4}]})), 4096)
        for row in ({"private": "sentinel"}, {"n": float("nan")}, {"n": -1}, {"n": 2**64}, {"n": [0] * 4096}):
            with self.assertRaises(ValueError):
                MOD.encode_record(row)

    def test_identity_requires_all_birth_fields(self):
        a = {"pid": 5, "start": 7, "dev": 8, "ino": 9}
        self.assertTrue(MOD.same_identity(a, dict(a)))
        for key in a:
            b = dict(a)
            b[key] += 1
            self.assertFalse(MOD.same_identity(a, b))
        self.assertFalse(MOD.same_identity(a, None))

    def test_identity_parser_missing_denied_and_zombie(self):
        from types import SimpleNamespace
        fields = ["S", "4"] + ["0"] * 17 + ["71"]
        text = "5 (private process name) " + " ".join(fields)
        with mock.patch.object(MOD, "read_small", return_value=text), mock.patch.object(MOD.os, "stat", return_value=SimpleNamespace(st_dev=8, st_ino=9)):
            self.assertEqual(MOD.identity(5), {"pid": 5, "start": 71, "dev": 8, "ino": 9, "ppid": 4})
        for bad in ("malformed", text.replace(") S", ") Z")):
            with mock.patch.object(MOD, "read_small", return_value=bad):
                self.assertEqual(MOD.identity(5), None if ") Z" in bad else {})
        with mock.patch.object(MOD, "read_small", side_effect=PermissionError()):
            self.assertEqual(MOD.identity(5), {})
        with mock.patch.object(MOD, "read_small", side_effect=FileNotFoundError()):
            self.assertIsNone(MOD.identity(5))

    def test_actual_membership_and_python_executable_are_required(self):
        parent = {"pid": 11, "start": 21, "dev": 1, "ino": 2, "ppid": 10}
        monitor = {"pid": 12, "start": 22, "dev": 3, "ino": 4, "ppid": 11}
        ledger = {"parent": parent, "monitor": monitor, "python": {"dev": 3, "ino": 4}}
        def check(actual, executable):
            with mock.patch.object(MOD, "identity", side_effect=lambda pid: parent if pid == 11 else actual), mock.patch.object(MOD, "python_identity", return_value=executable):
                return MOD.verified_monitor(ledger, 11, 12)
        self.assertTrue(check(monitor, {"dev": 3, "ino": 4}))
        for actual, executable in ((dict(monitor, ppid=99), {"dev": 3, "ino": 4}),
                                   (dict(monitor, ino=99), {"dev": 3, "ino": 4}),
                                   (monitor, {"dev": 3, "ino": 99}), ({}, {"dev": 3, "ino": 4})):
            self.assertFalse(check(actual, executable))

    def test_unknown_is_never_verified_absence(self):
        birth = {"pid": 12, "start": 22, "dev": 3, "ino": 4, "ppid": 11}
        self.assertFalse(MOD.verified_absence(birth, {}))
        self.assertFalse(MOD.verified_absence(birth, birth))
        self.assertFalse(MOD.verified_absence(birth, dict(birth, dev=99)))
        self.assertFalse(MOD.verified_absence(birth, dict(birth, ino=99)))
        self.assertTrue(MOD.verified_absence(birth, None))
        self.assertTrue(MOD.verified_absence(birth, dict(birth, start=23)))

    def test_stop_never_signals_forged_membership_or_unknown_identity(self):
        parent = {"pid": 11, "start": 21, "dev": 1, "ino": 2, "ppid": 10}
        monitor = {"pid": 12, "start": 22, "dev": 3, "ino": 4, "ppid": 11}
        ledger = {"parent": parent, "monitor": monitor, "python": {"dev": 3, "ino": 4}}
        for actual, executable in ((dict(monitor, ppid=99), {"dev": 3, "ino": 4}),
                                   (monitor, {"dev": 3, "ino": 99}), ({}, {"dev": 3, "ino": 4})):
            with mock.patch.object(MOD, "load_ledger", return_value=ledger), mock.patch.object(MOD.os, "getppid", return_value=11), mock.patch.object(MOD, "identity", side_effect=lambda pid: parent if pid == 11 else actual), mock.patch.object(MOD, "python_identity", return_value=executable), mock.patch.object(MOD.os, "pidfd_open", create=True) as signal_handle:
                with self.assertRaises(ValueError):
                    MOD.stop(Path("unused"), 11, 12)
                signal_handle.assert_not_called()

    def test_post_gate_requires_activation_metric_and_cleanup(self):
        parent = {"pid": 11, "start": 21, "dev": 1, "ino": 2, "ppid": 10}
        monitor = {"pid": 12, "start": 22, "dev": 3, "ino": 4, "ppid": 11}
        ledger = {"owner": 723, "parent": parent, "monitor": monitor, "python": {"dev": 3, "ino": 4},
                  "born_ns": 10, "lease_ns": 1000000000, "run": 0, "attempt": 0,
                  "memory_limit": 64 * 1024**2, "cpu_seconds": 15, "ancestors": []}
        receipt = {"owner": 723, "parent": parent, "monitor": monitor, "python": ledger["python"], "observed_ns": 20}
        files = {"owner.json": ledger, "ready.json": receipt,
                 "cleanup.json": dict(receipt, absent=1, observed_ns=40)}
        metric = {"event": 2, "sequence": 0, "monotonic_ns": 15, "owner": 723, "run": 0, "attempt": 0,
                  "parent": parent, "monitor": monitor}
        with tempfile.TemporaryDirectory() as d:
            directory = Path(d)
            def exercise(rows, current=None, missing=None):
                def read(path):
                    if path.name == missing:
                        raise FileNotFoundError()
                    return files[path.name]
                with mock.patch.object(MOD, "private_directory"), mock.patch.object(MOD, "read_owned_json", side_effect=read), mock.patch.object(MOD, "metric_rows", return_value=rows), mock.patch.object(MOD, "identity", return_value=current), mock.patch.object(MOD, "owner_labels", return_value={"run": 0, "attempt": 0}), mock.patch.object(MOD, "python_identity", return_value=ledger["python"]):
                    return MOD.post_gate(directory, "success")
            self.assertEqual(exercise([metric]), 0)
            for rows, current, missing in (([], None, None), ([dict(metric, run=9)], None, None),
                                           ([dict(metric, monotonic_ns=5)], None, None),
                                           ([metric], {}, None), ([metric], monitor, None),
                                           ([metric], None, "ready.json"), ([metric], None, "cleanup.json")):
                with self.assertRaises((ValueError, OSError)):
                    exercise(rows, current, missing)
            for key, value in (("owner", 999), ("python", {"dev": 3, "ino": 99})):
                original = ledger[key]
                ledger[key] = value
                with self.assertRaises(ValueError):
                    exercise([metric])
                ledger[key] = original
            with self.assertRaises((ValueError, OSError)):
                MOD.post_gate(directory / "missing", "success")
            self.assertEqual(MOD.post_gate(directory / "missing", "failure"), 3)

    def test_descendant_budget_and_changed_parent(self):
        anchor = {"pid": 1, "start": 7, "dev": 8, "ino": 9, "ppid": 0}
        def birth(pid):
            return dict(anchor) if pid == 1 else {"pid": pid, "start": pid, "dev": 8, "ino": 9, "ppid": 1}
        def children(path):
            return " ".join(str(n) for n in range(2, 73)) if Path(path).parts[-4] == "1" else ""
        with mock.patch.object(MOD, "identity", side_effect=birth), mock.patch.object(MOD, "read_small", side_effect=children):
            rows, omitted = MOD.descendants(anchor)
            self.assertEqual(len(rows), 64)
            self.assertEqual(omitted, 1)
        with mock.patch.object(MOD, "identity", return_value=None):
            self.assertEqual(MOD.descendants(anchor), ([], 0))

    def test_source_does_not_change_shared_stdout_or_use_assert_for_guards(self):
        source = SOURCE.read_text()
        self.assertNotIn("set_blocking", source)
        self.assertNotIn("F_SETFL", source)
        self.assertNotIn("assert ", source)
        self.assertNotIn("cmdline", source)
        self.assertNotIn("readlink", source)
        hook = HOOK.read_text()
        self.assertNotIn("trap ", hook.replace("trap -p EXIT", "").replace("trap - EXIT", "").replace("trap '_web_observer_cleanup \"$?\"' EXIT", ""))


@unittest.skipUnless(sys.platform == "linux", "NOT RUN: requires actual Linux /proc and Bash")
class Linux(unittest.TestCase):
    def run_shell(self, code, expected, *, hooked=True, extra=None, signal_number=None, bootstrap=None):
        import signal
        with tempfile.TemporaryDirectory() as d:
            base = Path(d)
            script = base / "step.sh"
            script.write_bytes(FROZEN if code is None else code)
            stub = base / "dotnet"
            stub.write_text("#!/bin/bash\nset -e\n" + (extra or "exit " + str(expected)) + "\n")
            stub.chmod(0o700)
            env = os.environ.copy()
            env.pop("BASH_ENV", None)
            env.pop("_WEB_FULL_OBSERVER_ACTIVE", None)
            env.update(RUNNER_TEMP=d, GITHUB_WORKSPACE=d, USE_LOCAL_MALIEV_DEPENDENCIES="true")
            # Test-only actor-free stub; production never changes PATH.
            env["PATH"] = d + os.pathsep + env["PATH"]
            if hooked:
                env["BASH_ENV"] = str(HOOK)
            if bootstrap is not None:
                boot = base / "bootstrap.bash"
                boot.write_text(bootstrap)
                env["BASH_ENV"] = str(boot)
            p = subprocess.Popen(["bash", "--noprofile", "--norc", "-eo", "pipefail", str(script)], env=env,
                                 stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            resource = record_resource(p, 1)
            try:
                if signal_number:
                    if hooked:
                        deadline = time.monotonic() + 2
                        while not (base / "full-test-observer-results/owner.json").exists() and time.monotonic() < deadline:
                            time.sleep(.01)
                    else:
                        time.sleep(.2)
                    p.send_signal(signal_number)
                output, errors = p.communicate(timeout=7)
            finally:
                if p.poll() is None:
                    p.kill()  # Exact retained control-process handle only.
                    p.communicate(timeout=3)
                record_exit(p, resource)
            self.last_status = p.returncode
            if expected is not None:
                self.assertEqual(p.returncode, expected, errors.decode())
            result = base / "full-test-observer-results"
            if result.exists():
                ledger = json.loads((result / "owner.json").read_text())
                RESOURCES.append({"owner": 723, "purpose": 2, "pid": ledger["monitor"]["pid"],
                                  "birth": ledger["monitor"], "lease_ns": ledger["lease_ns"]})
                deadline = time.monotonic() + 1.5
                while MOD.identity(ledger["monitor"]["pid"]) is not None and time.monotonic() < deadline:
                    time.sleep(.02)
                self.assertTrue(MOD.verified_absence(ledger["monitor"], MOD.identity(ledger["monitor"]["pid"])), "observer absence is not verified")
                RESOURCES[-1]["absent"] = 1
                if not signal_number:
                    self.assertEqual(json.loads((result / "cleanup.json").read_text())["absent"], 1)
                for line in (result / "metrics.jsonl").read_bytes().splitlines() if (result / "metrics.jsonl").exists() else []:
                    MOD.encode_record(json.loads(line))
            return output, errors, result.exists()

    def setUp(self):
        Portable.setUp(self)

    def test_original_shell_normal_and_nonzero(self):
        for status in (0, 7, 143):
            _, _, activated = self.run_shell(None, status)
            self.assertTrue(activated)

    def test_mismatch_is_inert(self):
        _, _, activated = self.run_shell(b"exit 0\n", 0)
        self.assertFalse(activated)

    def test_reentrancy_and_original_argv(self):
        stub = ('test "$*" = "test Legacy.Maliev.Web.slnx --configuration Release --no-build --no-restore"\n'
                'test "$_WEB_FULL_OBSERVER_ACTIVE" = 1\n'
                'bash -c \'test "$_WEB_FULL_OBSERVER_ACTIVE" = 1\'\nexit 0')
        bootstrap = ('source "' + str(HOOK) + '"\n') * 2
        _, _, activated = self.run_shell(None, 0, extra=stub, bootstrap=bootstrap)
        self.assertTrue(activated)

    def test_term_status_matches_unhooked_control(self):
        import signal
        for hooked in (False, True):
            self.run_shell(None, -signal.SIGTERM, hooked=hooked,
                           extra="sleep 0.8; exit 0", signal_number=signal.SIGTERM)

    def test_int_and_hup_status_match_unhooked_control(self):
        import signal
        for number in (signal.SIGINT, signal.SIGHUP):
            self.run_shell(None, None, hooked=False, extra="sleep 0.8; exit 0", signal_number=number)
            status = self.last_status
            self.run_shell(None, status, extra="sleep 0.8; exit 0", signal_number=number)

    def test_forced_parent_exit_does_not_hold_stdout(self):
        import signal
        start = time.monotonic()
        self.run_shell(None, -signal.SIGKILL, extra="sleep 0.8; exit 0", signal_number=signal.SIGKILL)
        self.assertLess(time.monotonic() - start, 4)

    def test_existing_exit_trap_refused(self):
        _, _, activated = self.run_shell(None, 97, bootstrap='trap ":" EXIT\nsource "' + str(HOOK) + '"\n')
        self.assertFalse(activated)

    def test_preexisting_bash_env_refused_without_exposing_it(self):
        with tempfile.TemporaryDirectory() as d:
            env = os.environ.copy()
            env["BASH_ENV"] = "private-sentinel"
            p = subprocess.run([sys.executable, "-B", str(SOURCE), "preflight", d], env=env,
                               capture_output=True, timeout=3)
            self.assertEqual(p.returncode, 2)
            self.assertEqual(p.stdout + p.stderr, b"")

    def observer_control(self, body, *, drain=True):
        with tempfile.TemporaryDirectory() as d:
            directory = Path(d) / "owned"
            directory.mkdir(mode=0o700)
            code = ('import importlib.util,os; from pathlib import Path; '
                    's=importlib.util.spec_from_file_location("o",' + repr(str(SOURCE)) + '); '
                    'o=importlib.util.module_from_spec(s); s.loader.exec_module(o); ' + body)
            p = subprocess.Popen([sys.executable, "-B", "-c", code, str(directory)],
                                 stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            resource = record_resource(p, 3)
            try:
                if not drain:
                    p.wait(timeout=3)
                out, err = p.communicate(timeout=3)
                self.assertEqual(p.returncode, 0, err.decode())
                self.assertLessEqual(len(out), 120 * 4096)
                self.assertEqual(err, b"")
                if not drain:
                    self.assertGreater((directory / "metrics.jsonl").stat().st_size, len(out), "control must actually block the stdout writer")
            finally:
                if p.poll() is None:
                    p.kill()
                    p.communicate(timeout=3)
                record_exit(p, resource)

    def test_lease_and_blocked_stdout_are_finite(self):
        self.observer_control('import sys; p=Path(sys.argv[1]); o.observe(os.getppid(),p,p,p,lease=.4,interval=.001)', drain=False)

    def test_measurement_exception_is_sanitized_and_exits(self):
        self.observer_control('import sys; p=Path(sys.argv[1]); '
                              'o.sample=lambda *a: (_ for _ in ()).throw(RuntimeError("private-sentinel")); '
                              'o.observe(os.getppid(),p,p,p,lease=.4,interval=.01)')

    def test_wrong_monitor_birth_never_signalled(self):
        with tempfile.TemporaryDirectory() as d:
            directory = Path(d)
            directory.chmod(0o700)
            child = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(2)"])
            resource = record_resource(child, 4)
            try:
                birth = MOD.identity(child.pid)
                birth["start"] += 1
                MOD.write_new(directory / "owner.json", {"owner": 723, "parent": MOD.identity(os.getpid()), "monitor": birth,
                              "python": MOD.python_identity(), "born_ns": time.monotonic_ns(), "lease_ns": 1000000000,
                              "memory_limit": 64 * 1024**2, "cpu_seconds": 15, "ancestors": [], **MOD.owner_labels()})
                with mock.patch.object(MOD.os, "getppid", return_value=os.getpid()):
                    with self.assertRaises(ValueError):
                        MOD.stop(directory, os.getpid(), child.pid)
                self.assertIsNone(child.poll())
            finally:
                child.terminate()
                child.wait(timeout=3)
                record_exit(child, resource)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--require-linux", action="store_true")
    parser.add_argument("--report")
    options = parser.parse_args()
    if options.require_linux and sys.platform != "linux":
        parser.error("Linux controls NOT RUN; actual hosted Linux is mandatory")
    result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromModule(sys.modules[__name__]))
    if options.report:
        path = Path(options.report)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps({"run": result.testsRun, "failures": len(result.failures),
                                    "errors": len(result.errors), "notRun": len(result.skipped),
                                    "linuxExecuted": sys.platform == "linux", "resources": RESOURCES}) + "\n")
    sys.exit(0 if result.wasSuccessful() else 1)
