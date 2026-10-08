import copy
import datetime as dt
import json
import io
import stat
import zipfile
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).parents[1]))
import check_web_candidate_admission as admission
import materialize_web_candidate as intake
import run_web_candidate_phase as phase

class CountryControls(unittest.TestCase):
    def capsule_fixture(self):
        policy,_,_,_=self.fixture()
        files={f'country/{n}.txt':b'source control\n' for n in range(3)}
        stream=io.BytesIO()
        with zipfile.ZipFile(stream,'w') as archive:
            for name,raw in files.items():
                entry=zipfile.ZipInfo(name);entry.create_system=3;entry.external_attr=(stat.S_IFREG|0o644)<<16
                entry.compress_type=zipfile.ZIP_DEFLATED
                archive.writestr(entry,raw)
        capsule=stream.getvalue()
        source=[dict(path=n,bytes=len(v),sha256=intake.sha256(v)) for n,v in files.items()]
        source += [dict(path=f'base/{n}.txt',bytes=0,sha256=intake.sha256(b'')) for n in range(1949)]
        rows=[]
        with zipfile.ZipFile(io.BytesIO(capsule)) as archive:
            for entry in archive.infolist():
                row=next(r for r in source if r['path']==entry.filename)
                rows.append(dict(row,compressedBytes=entry.compress_size,crc32=entry.CRC,createSystem=entry.create_system,externalAttributes=entry.external_attr,flags=entry.flag_bits,compression=entry.compress_type,dateTime=list(entry.date_time)))
        policy.update(sourceFiles=source,capsuleRows=rows)
        manifest={k:policy[k] for k in ('owner','acceptedBase','sourcePins','sourceBindingSha256','sourceFiles','capsuleRows')}
        manifest.update(schemaVersion=1,capsuleBytes=len(capsule),capsuleSha256=intake.sha256(capsule))
        raw=json.dumps(manifest).encode();policy['manifestSha256']=intake.sha256(raw)
        return policy,raw,capsule

    def fixture(self):
        policy=intake.load_policy(Path(intake.__file__).with_name('web-country-policy.json'))
        now=dt.datetime(2026,10,8,tzinfo=dt.timezone.utc)
        grant={"owner":admission.OWNER,"issuedBy":"019fc21e-50f0-7112-834f-9fb3b35b9dfe",
               "environment":"github-hosted-linux","sliceKind":"country-operation-v1",
               "sourceBindingSha256":policy["sourceBindingSha256"],"manifestSha256":policy["manifestSha256"],
               "acceptedBase":policy["acceptedBase"],"sourcePins":policy["sourcePins"],
               "allowedPhases":["build"],"phase":policy["phase"],"startsUtc":now.isoformat(),
               "expiresUtc":(now+dt.timedelta(minutes=10)).isoformat()}
        raw=json.dumps(grant).encode();policy['nativeAdmissionSha256']=intake.sha256(raw)
        return policy,grant,raw,now

    def test_exact_independent_build_requires_no_customer(self):
        policy,grant,raw,now=self.fixture()
        self.assertNotIn('customerLiteralProducerSha',policy)
        with patch.object(admission.os,'name','posix'):
            self.assertEqual(grant,admission.validate(policy,raw,now))

    def test_unpinned_real_policy_refuses_execution(self):
        policy,grant,raw,now=self.fixture();policy['nativeAdmissionSha256']=None
        with self.assertRaises(ValueError):admission.validate(policy,raw,now)

    def test_changed_permit_bytes_refused(self):
        policy,grant,raw,now=self.fixture()
        with self.assertRaises(ValueError):admission.validate(policy,raw+b' ',now)

    def test_each_exact_context_field_is_enforced(self):
        for key in ('owner','issuedBy','environment','sliceKind','sourceBindingSha256','manifestSha256','acceptedBase','sourcePins','allowedPhases','phase'):
            policy,grant,raw,now=self.fixture();grant[key]='changed';raw=json.dumps(grant).encode()
            policy['nativeAdmissionSha256']=intake.sha256(raw)
            with self.subTest(key=key),self.assertRaises(ValueError):admission.validate(policy,raw,now)

    def test_extra_fields_refused(self):
        policy,grant,raw,now=self.fixture();grant['customerLiteralProducerSha']='a'*40
        raw=json.dumps(grant).encode();policy['nativeAdmissionSha256']=intake.sha256(raw)
        with self.assertRaises(ValueError):admission.validate(policy,raw,now)

    def test_finite_utc_window(self):
        for start,end in ((0,0),(1,600),(0,1201),(-600,0)):
            policy,grant,raw,now=self.fixture()
            grant['startsUtc']=(now+dt.timedelta(seconds=start)).isoformat()
            grant['expiresUtc']=(now+dt.timedelta(seconds=end)).isoformat()
            raw=json.dumps(grant).encode();policy['nativeAdmissionSha256']=intake.sha256(raw)
            with self.subTest(start=start,end=end),self.assertRaises(ValueError):admission.validate(policy,raw,now)

    def test_independent_inventory_and_capsule_accepted(self):
        policy,raw,capsule=self.capsule_fixture()
        manifest,files=intake.validate_capsule(raw,capsule,policy)
        self.assertEqual(1952,len(manifest['sourceFiles']))
        self.assertEqual(3,len(files))

    def test_old_inventory_rule_not_widened(self):
        policy,raw,capsule=self.capsule_fixture();policy.pop('sliceKind')
        with self.assertRaises(ValueError):intake.validate_capsule(raw,capsule,policy)

    def test_independent_policy_drift_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            file=Path(directory)/'policy.json';file.write_bytes(b'{"sliceKind":"country-operation-v1"}')
            with self.assertRaises(ValueError):intake.load_policy(file)

    def test_exact_argv_and_phase_rejected_before_lookup_or_spawn(self):
        policy,grant,raw,now=self.fixture()
        for args in (['--phase','build','--id','country-build-1','--','dotnet','test'],
                     ['--phase','build','--id','other-id','--']+policy['phase']['argv']):
            with tempfile.TemporaryDirectory() as directory:
                permit=Path(directory)/'permit.json';permit.write_bytes(raw)
                argv=['runner','--policy','unused','--permit',str(permit),'--evidence',str(Path(directory)/'evidence')]+args
                with patch.object(sys,'argv',argv),patch.object(intake,'load_policy',return_value=policy),patch.object(admission,'validate',return_value=grant),patch.object(admission,'census'),patch.object(phase,'remaining_seconds',return_value=590),patch.object(phase.shutil,'which') as lookup,patch.object(phase.subprocess,'Popen') as spawn:
                    with self.assertRaises(ValueError):phase.main()
                    lookup.assert_not_called();spawn.assert_not_called()

if __name__=='__main__':unittest.main()
