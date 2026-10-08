"""Pure account transport controls; fixture grants exist only in memory, never SDK admission."""
import copy
import datetime as dt
import json
import io
import stat
import zipfile
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
sys.path.insert(0, str(Path(__file__).parents[1]))
import materialize_web_candidate as intake
import check_web_candidate_admission as admission
import run_web_candidate_phase as phase
import run_web_account_candidate as account
ROOT = Path(__file__).parents[3]
SCRIPTS = Path(__file__).parents[1]

class AccountTransportControls(unittest.TestCase):
    def policy(self):
        return intake.load_policy(SCRIPTS / "web-account-failure-policy.json")

    def fixture(self):
        policy = self.policy()
        now = dt.datetime(2026, 10, 8, tzinfo=dt.timezone.utc)
        policy["sdkOwnerContextSha256"] = "a" * 64
        grant = {"owner": admission.OWNER, "issuedBy": "019fc21e-50f0-7112-834f-9fb3b35b9dfe",
                 "environment": "github-hosted-linux", "sliceKind": "account-failure-v1",
                 "acceptedBase": policy["acceptedBase"], "sourcePins": policy["sourcePins"],
                 "sourceBindingSha256": policy["sourceBindingSha256"], "manifestSha256": policy["manifestSha256"],
                 "producerPrerequisites": policy["producerPrerequisites"], "sdkOwnerContextSha256": "a" * 64,
                 "allowedPhases": policy["allowedPhases"], "phases": policy["phases"],
                 "transportSha": "b" * 40, "runId": "123", "runAttempt": "1",
                 "startsUtc": now.isoformat(), "expiresUtc": (now + dt.timedelta(minutes=30)).isoformat()}
        raw = json.dumps(grant).encode()
        policy["nativeAdmissionSha256"] = intake.sha256(raw)
        return policy, grant, raw, now

    def capsule_fixture(self):
        # Portable synthetic bytes exercise inventory/ZIP rules in protected PR CI.
        # The actual sealed business capsule is checked separately off-repository.
        policy = self.policy()
        files = {"fixture/" + str(n) + ".txt": b"wire fixture" for n in range(6)}
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as archive:
            for name, data in files.items():
                info = zipfile.ZipInfo(name); info.create_system = 3
                info.external_attr = (stat.S_IFREG | 0o644) << 16
                info.compress_type = zipfile.ZIP_DEFLATED
                archive.writestr(info, data)
        raw = stream.getvalue()
        rows = [{"path": name, "bytes": len(data), "sha256": intake.sha256(data)} for name, data in files.items()]
        rows += [{"path": "base/" + str(n) + ".txt", "bytes": 0, "sha256": intake.sha256(b"")} for n in range(1963 - 6)]
        capsule = []
        with zipfile.ZipFile(io.BytesIO(raw)) as archive:
            for info in archive.infolist():
                row = next(row for row in rows if row["path"] == info.filename)
                capsule.append(dict(row, compressedBytes=info.compress_size, crc32=info.CRC,
                                    createSystem=info.create_system, externalAttributes=info.external_attr,
                                    flags=info.flag_bits, compression=info.compress_type, dateTime=list(info.date_time)))
        policy.update(sourceFiles=rows, capsuleRows=capsule)
        manifest = {key: policy[key] for key in ["owner", "acceptedBase", "sourcePins", "sourceBindingSha256", "sourceFiles", "capsuleRows"]}
        manifest.update(schemaVersion=1, capsuleSha256=intake.sha256(raw), capsuleBytes=len(raw))
        encoded = json.dumps(manifest).encode(); policy["manifestSha256"] = intake.sha256(encoded)
        return policy, encoded, raw

    def environment(self):
        return patch.dict(os.environ, WEB_REVIEWED_TRANSPORT_SHA="b" * 40, GITHUB_RUN_ID="123", GITHUB_RUN_ATTEMPT="1")

    def test_status_uses_verified_account_inventory_and_preserves_old_profiles(self):
        policy = self.policy()
        self.assertEqual("Reviewed raw candidate source remains unchanged (1963 files).", intake.source_status(policy))
        self.assertEqual("Reviewed raw candidate materialized (1963 files); native validation pending.", intake.source_status(policy, materialized=True))
        for name in ["web-candidate-policy.json", "web-country-policy.json"]:
            old = intake.load_policy(SCRIPTS / name)
            self.assertEqual("Reviewed raw candidate source remains unchanged (1983 files).", intake.source_status(old))
            self.assertEqual("Reviewed raw candidate materialized (1983 files); native validation pending.", intake.source_status(old, materialized=True))

    def test_old_policies_remain_exact(self):
        self.assertEqual(intake.EXPECTED_POLICY_SHA256, intake.sha256((SCRIPTS / "web-candidate-policy.json").read_bytes()))
        self.assertEqual(intake.COUNTRY_POLICY_SHA256, intake.sha256((SCRIPTS / "web-country-policy.json").read_bytes()))
        intake.load_policy(SCRIPTS / "web-candidate-policy.json")
        intake.load_policy(SCRIPTS / "web-country-policy.json")

    def test_full_inventory_and_six_file_capsule_shape(self):
        policy, manifest, raw = self.capsule_fixture()
        _, files = intake.validate_capsule(manifest, raw, policy)
        self.assertEqual(1963, len(policy["sourceFiles"]))
        self.assertEqual(6, len(files))
        self.assertEqual(1962, policy["baseSourceCount"])
        self.assertNotIn("customerLiteralProducerSha", policy)

    def test_unissued_policy_cannot_admit(self):
        with self.assertRaises(ValueError): admission.validate(self.policy(), b"{}")

    def test_exact_in_memory_account_scope(self):
        policy, grant, raw, now = self.fixture()
        with self.environment(), patch.object(admission.os, "name", "posix"):
            self.assertEqual(grant, admission.validate(policy, raw, now))

    def test_every_bound_grant_field_rejects_drift(self):
        policy, grant, raw, now = self.fixture()
        for field in set(grant) - {"startsUtc", "expiresUtc"}:
            bad = copy.deepcopy(grant); bad[field] = "wrong"
            changed = json.dumps(bad).encode(); configured = dict(policy, nativeAdmissionSha256=intake.sha256(changed))
            with self.subTest(field=field), self.environment(), patch.object(admission.os, "name", "posix"), self.assertRaises(ValueError):
                admission.validate(configured, changed, now)

    def test_run_attempt_and_head_replay_rejected(self):
        policy, grant, raw, now = self.fixture()
        for field in ["GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "WEB_REVIEWED_TRANSPORT_SHA"]:
            with self.subTest(field=field), self.environment(), patch.dict(os.environ, {field: "changed"}), self.assertRaises(ValueError):
                admission.validate(policy, raw, now)

    def test_expired_future_excessive_or_non_utc_rejected(self):
        policy, grant, raw, now = self.fixture()
        for start, end in [(-30, 0), (1, 30), (0, 36)]:
            bad = dict(grant, startsUtc=(now + dt.timedelta(minutes=start)).isoformat(), expiresUtc=(now + dt.timedelta(minutes=end)).isoformat())
            changed = json.dumps(bad).encode(); configured = dict(policy, nativeAdmissionSha256=intake.sha256(changed))
            with self.subTest(start=start, end=end), self.environment(), self.assertRaises(ValueError): admission.validate(configured, changed, now)
        bad = dict(grant, startsUtc="2026-10-08T07:00:00+07:00")
        changed = json.dumps(bad).encode(); configured = dict(policy, nativeAdmissionSha256=intake.sha256(changed))
        with self.environment(), self.assertRaises(ValueError): admission.validate(configured, changed, now)

    def test_wire_contracts_cannot_be_dummy_or_unrelated(self):
        policy, grant, raw, now = self.fixture()
        for field in ["mode", "authEndpoint", "notificationEndpoint", "testPath", "testSha256", "contractFiles", "remoteServiceQualification"]:
            bad = copy.deepcopy(policy); bad["producerPrerequisites"][field] = None
            with self.subTest(field=field), self.environment(), self.assertRaises(ValueError): admission.validate(bad, raw, now)

    def test_unknown_grant_key_duplicate_and_unpinned_owner_rejected(self):
        policy, grant, raw, now = self.fixture()
        for changed in [raw[:-1] + b',"owner":"duplicate"}', json.dumps(dict(grant, extra=True)).encode()]:
            configured = dict(policy, nativeAdmissionSha256=intake.sha256(changed))
            with self.environment(), self.assertRaises(ValueError): admission.validate(configured, changed, now)
        policy["sdkOwnerContextSha256"] = None
        with self.assertRaises(ValueError): admission.validate(policy, raw, now)

    def test_account_policy_and_inventory_drift_rejected(self):
        policy, manifest, raw = self.capsule_fixture()
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "policy.json"; path.write_bytes((SCRIPTS / "web-account-failure-policy.json").read_bytes() + b" ")
            with self.assertRaises(ValueError): intake.load_policy(path)
        for name in ["sourceCount", "acceptedBase", "sourceBindingSha256"]:
            bad = copy.deepcopy(policy); bad[name] = 1 if name == "sourceCount" else "wrong"
            with self.subTest(name=name), self.assertRaises(ValueError): intake.validate_capsule(manifest, raw, bad)
        with self.assertRaises(ValueError): intake.validate_capsule(manifest, raw + b"x", policy)

    def test_account_entry_unissued_rejects_before_permit_owner_or_sdk(self):
        argv = ["account", "--policy", str(SCRIPTS / "web-account-failure-policy.json"),
                "--candidate", "unused", "--permit", "missing", "--sdk-owner-context", "missing",
                "--evidence", "unused", "--phase-id", "account-build"]
        with patch.object(sys, "argv", argv), patch.object(account.admission, "census") as census, patch.object(account.phase, "verify_capped_owner") as owner, patch.object(account.phase, "main") as execute:
            with self.assertRaises(ValueError): account.main()
            census.assert_not_called(); owner.assert_not_called(); execute.assert_not_called()

    def test_phase_wrong_argv_rejected_before_owner_or_sdk(self):
        policy, grant, raw, now = self.fixture()
        with tempfile.TemporaryDirectory() as directory:
            permit = Path(directory) / "test-memory-fixture.json"; permit.write_bytes(raw)
            argv = ["phase", "--policy", "unused", "--permit", str(permit), "--evidence", directory, "--phase", "build", "--id", "account-build", "--", "dotnet", "wrong"]
            with patch.object(sys, "argv", argv), patch.object(phase.admission.intake, "load_policy", return_value=policy), patch.object(phase.admission, "validate", return_value=grant), patch.object(phase.admission, "census"), patch.object(phase, "remaining_seconds", return_value=100), patch.object(phase, "verify_capped_owner") as owner, patch.object(phase.subprocess, "Popen") as spawn:
                with self.assertRaises(ValueError): phase.main()
                owner.assert_not_called(); spawn.assert_not_called()

if __name__ == "__main__": unittest.main()
