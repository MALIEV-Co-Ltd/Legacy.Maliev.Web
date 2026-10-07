import copy
import datetime as dt
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).parents[1]))
import check_web_candidate_admission as m

class AdmissionControls(unittest.TestCase):
    def fixture(self):
        now = dt.datetime(2026, 10, 8, tzinfo=dt.timezone.utc)
        policy = {"sourceBindingSha256": "b" * 64, "manifestSha256": "c" * 64, "acceptedBase": "d" * 40,
                  "customerLiteralProducerSha": "e" * 40}
        grant = {"owner": m.OWNER, "environment": "github-hosted-linux", "sourceBindingSha256": policy["sourceBindingSha256"],
                 "manifestSha256": policy["manifestSha256"], "acceptedBase": policy["acceptedBase"],
                 "customerLiteralProducerSha": policy["customerLiteralProducerSha"],
                 "allowedPhases": ["assets", "build", "focused", "suite", "coverage", "static", "native"],
                 "startsUtc": now.isoformat(), "expiresUtc": (now + dt.timedelta(minutes=90)).isoformat()}
        raw = json.dumps(grant).encode(); policy["nativeAdmissionSha256"] = m.intake.sha256(raw)
        return now, policy, grant, raw

    def test_exact_pinned_permit(self):
        now, policy, grant, raw = self.fixture()
        with patch.object(m.os, "name", "posix"): self.assertEqual(grant, m.validate(policy, raw, now))

    def test_missing_policy_prerequisites(self):
        now, policy, grant, raw = self.fixture()
        for name in ("nativeAdmissionSha256", "customerLiteralProducerSha"):
            bad = dict(policy); bad[name] = None
            with self.subTest(name=name), self.assertRaises(ValueError): m.validate(bad, raw, now)

    def test_unreviewed_self_minted_permit(self):
        now, policy, grant, raw = self.fixture()
        grant["expiresUtc"] = (now + dt.timedelta(minutes=30)).isoformat()
        with self.assertRaises(ValueError): m.validate(policy, json.dumps(grant).encode(), now)

    def test_scope_controls(self):
        now, policy, grant, raw = self.fixture()
        for field in ("owner", "environment", "acceptedBase", "sourceBindingSha256", "manifestSha256", "customerLiteralProducerSha", "allowedPhases"):
            bad = dict(grant); bad[field] = "changed"; changed = json.dumps(bad).encode()
            configured = dict(policy, nativeAdmissionSha256=m.intake.sha256(changed))
            with self.subTest(field=field), self.assertRaises(ValueError), patch.object(m.os, "name", "posix"):
                m.validate(configured, changed, now)

    def test_expired_future_and_overlong_permits(self):
        now, policy, grant, raw = self.fixture()
        for start, end in ((-90, 0), (1, 90), (0, 91)):
            bad = dict(grant, startsUtc=(now + dt.timedelta(minutes=start)).isoformat(), expiresUtc=(now + dt.timedelta(minutes=end)).isoformat())
            changed = json.dumps(bad).encode(); configured = dict(policy, nativeAdmissionSha256=m.intake.sha256(changed))
            with self.subTest(start=start,end=end), self.assertRaises(ValueError): m.validate(configured, changed, now)

    def test_duplicate_grant_keys(self):
        now, policy, grant, raw = self.fixture(); changed = raw[:-1] + b',"owner":"duplicate"}'
        policy["nativeAdmissionSha256"] = m.intake.sha256(changed)
        with self.assertRaises(ValueError): m.validate(policy, changed, now)

    def test_windows_cannot_substitute_for_linux(self):
        now, policy, grant, raw = self.fixture()
        with patch.object(m.os, "name", "nt"), self.assertRaises(ValueError): m.validate(policy, raw, now)

    def test_memory_floor_and_worker_census(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); (root / "meminfo").write_text("MemAvailable: 4194304 kB\n")
            m.census(root)
            (root / "meminfo").write_text("MemAvailable: 4194303 kB\n")
            with self.assertRaises(ValueError): m.census(root)
            (root / "meminfo").write_text("MemAvailable: 4194304 kB\n")
            worker = root / "999"; worker.mkdir()
            for name in ("dotnet", "testhost", "MSBuild", "VBCSCompiler", "datacollector", "esbuild"):
                (worker / "comm").write_text(name)
                with self.subTest(name=name), self.assertRaises(ValueError): m.census(root)
            (worker / "comm").write_text("user-application"); m.census(root)

if __name__ == "__main__": unittest.main()
