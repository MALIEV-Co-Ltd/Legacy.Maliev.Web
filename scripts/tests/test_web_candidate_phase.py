import datetime as dt
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0,str(Path(__file__).parents[1]))
import run_web_candidate_phase as m
import prepare_qualified_web_profile as profile

class PhaseControls(unittest.TestCase):
    def test_remaining_lifetime_reserves_cleanup(self):
        now=dt.datetime(2026,10,8,tzinfo=dt.timezone.utc)
        grant={"expiresUtc":(now+dt.timedelta(seconds=35)).isoformat()}
        self.assertEqual(25,m.remaining_seconds(grant,now))

    def test_no_launch_when_cleanup_cannot_fit(self):
        now=dt.datetime(2026,10,8,tzinfo=dt.timezone.utc)
        for seconds in (-1,0,5,10):
            with self.subTest(seconds=seconds),self.assertRaises(ValueError):
                m.remaining_seconds({"expiresUtc":(now+dt.timedelta(seconds=seconds)).isoformat()},now)

    def test_deadline_is_anchored_before_launch(self):
        with patch.object(m.time,"monotonic",return_value=100),patch.object(m,"remaining_seconds",return_value=25):
            self.assertEqual(125,m.phase_deadline({}))

    def test_phase_lifetime_has_20_minute_cap(self):
        now=dt.datetime(2026,10,8,tzinfo=dt.timezone.utc)
        self.assertEqual(1200,m.remaining_seconds({"expiresUtc":(now+dt.timedelta(minutes=90)).isoformat()},now))

    def test_actual_shutdown_preserves_leader_until_signals_end(self):
        class Process:
            pid=123
            def wait(self,timeout):events.append("reap")
        events=[];record={}
        with patch.object(m,'observe_members',side_effect=[[{"pid":124}],[],[]]),patch.object(m,'signal_owned_group',side_effect=lambda proc,sig:events.append("signal")),patch.object(m.select,'select',return_value=([77],[],[])):
            m.shutdown(Process(),77,record)
        self.assertTrue(record['cleanupVerified']);self.assertEqual(["signal","reap"],events)

    def test_unverified_pidfd_never_claims_cleanup(self):
        class Process:
            pid=123
            def wait(self,timeout):pass
        record={}
        with patch.object(m,'observe_members',return_value=[]),self.assertRaises(RuntimeError):m.shutdown(Process(),None,record)
        self.assertFalse(record['cleanupVerified'])

    def test_hard_output_cap(self):
        import io
        output=io.BytesIO();m.capture_chunk(output,b"123456",limit=8)
        with self.assertRaises(RuntimeError):m.capture_chunk(output,b"7890",limit=8)
        self.assertEqual(b"12345678",output.getvalue())

    def test_group_signal_checks_session_and_group(self):
        from types import SimpleNamespace
        for group,session in ((124,123),(123,124)):
            with patch.object(m.os,'getpgid',return_value=group,create=True),patch.object(m.os,'getsid',return_value=session,create=True),patch.object(m.os,'killpg',create=True) as kill,self.assertRaises(RuntimeError):
                m.signal_owned_group(SimpleNamespace(pid=123),m.signal.SIGTERM)
            kill.assert_not_called()

    def test_cancellation_is_deferred_until_owner_is_retained(self):
        m.stop_requested=False
        m.request_stop(m.signal.SIGTERM,None)
        self.assertTrue(m.stop_requested)
        m.stop_requested=False

    def fixture(self):
        raw=b"$root = Split-Path $PSScriptRoot -Parent\n$customer = Pinned-Checkout 'Legacy.Maliev.CustomerService' 'profile-producer' 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'\n"
        policy={"profilePreparationSha256":profile.intake.sha256(raw),"originalCustomerProducerPin":"a"*40,"customerLiteralProducerSha":"b"*40}
        return raw,policy

    def test_qualified_pin_changes_only_preparation_adapter(self):
        raw,policy=self.fixture();adapted=profile.prepare_bytes(policy,raw,Path('/owned-candidate'))
        self.assertIn(b"'"+b"b"*40+b"'",adapted);self.assertNotIn(b"a"*40,adapted)
        self.assertIn(("$root = '"+str(Path("/owned-candidate"))+"'").encode(),adapted);self.assertIn(b"a"*40,raw)

    def test_changed_preparation_source_refused(self):
        raw,policy=self.fixture()
        with self.assertRaises(ValueError):profile.prepare_bytes(policy,raw+b"changed",Path('/owned'))

    def test_unqualified_actual_producer_refused(self):
        raw,policy=self.fixture();policy['customerLiteralProducerSha']=None
        with self.assertRaises(ValueError):profile.prepare_bytes(policy,raw,Path('/owned'))

    def test_ambiguous_pin_refused(self):
        raw,policy=self.fixture();raw+=b"a"*40;policy['profilePreparationSha256']=profile.intake.sha256(raw)
        with self.assertRaises(ValueError):profile.prepare_bytes(policy,raw,Path('/owned'))

if __name__=='__main__':unittest.main()
