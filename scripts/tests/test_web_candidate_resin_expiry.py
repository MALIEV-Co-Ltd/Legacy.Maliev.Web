"""Actor-free controls. Synthetic grants are in-memory and never activate the policy."""
import copy
import datetime as dt
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import uuid
import xml.etree.ElementTree as ET

sys.path.insert(0, str(Path(__file__).parents[1]))
import materialize_web_candidate as intake
import check_web_candidate_admission as admission
import run_web_resin_comparison_expiry as resin
import run_web_candidate_phase as phase

SCRIPTS = Path(__file__).parents[1]


class ResinExpiryControls(unittest.TestCase):
    def policy(self):
        return intake.load_policy(SCRIPTS / "web-resin-comparison-expiry-policy.json")

    def fixture(self):
        policy = self.policy()
        policy["enrollmentCustodian"] = {"login": "fixture-custodian", "id": 42, "type": "User", "roleBindingSha256": "a"*64}
        now = dt.datetime(2026, 10, 9, tzinfo=dt.timezone.utc)
        grant = {key: policy[key] for key in ("acceptedBase", "sourcePins", "sourceBindingSha256", "manifestSha256", "sdkOwnerEnrollment", "sdkOwnerContextSha256", "phases", "allowedPhases", "controlCommit")}
        grant.update(owner=admission.OWNER, issuedBy={key: policy["enrollmentCustodian"][key] for key in ("login", "id", "roleBindingSha256")}, environment="github-hosted-linux",
                     sliceKind=resin.SCOPE, transportSha="b"*40, runId="123", runAttempt="1", ownerNonce="a"*32,
                     startsUtc=now.isoformat(), expiresUtc=(now+dt.timedelta(minutes=30)).isoformat())
        raw=json.dumps(grant).encode(); policy["nativeAdmissionSha256"]=intake.sha256(raw)
        return policy, grant, raw, now

    def environment(self):
        return patch.dict(os.environ, WEB_REVIEWED_TRANSPORT_SHA="b"*40, GITHUB_SHA="b"*40, GITHUB_RUN_ID="123", GITHUB_RUN_ATTEMPT="1")

    def test_unissued_source_rejects_before_any_native_actor(self):
        with patch.object(admission, "census", side_effect=AssertionError("unexpected census")), patch.object(phase.subprocess, "Popen", side_effect=AssertionError("unexpected dispatch")):
            with self.assertRaises(ValueError):
                admission.validate(self.policy(), b"{}")
            with tempfile.TemporaryDirectory() as directory:
                permit=Path(directory)/"permit"; permit.write_bytes(b"{}")
                with self.assertRaises(ValueError):
                    resin.authorize_creation(SCRIPTS/"web-resin-comparison-expiry-policy.json", permit, Path(directory))

    def test_exact_synthetic_permit_positive(self):
        policy, grant, raw, now=self.fixture()
        with self.environment(), patch.object(os, "name", "posix"):
            self.assertEqual(grant, resin._validate_permit(policy, raw, now))

    def test_each_bound_field_even_rehashed_rejects(self):
        policy, grant, raw, now=self.fixture()
        for field in set(grant)-{"startsUtc", "expiresUtc"}:
            bad=copy.deepcopy(grant); bad[field]="wrong"
            changed=json.dumps(bad).encode(); configured=dict(policy, nativeAdmissionSha256=intake.sha256(changed))
            with self.subTest(field=field), self.environment(), patch.object(os,"name","posix"), self.assertRaises(ValueError):
                resin._validate_permit(configured, changed, now)

    def test_old_scope_filter_suite_or_owner_caps_cannot_be_rebound(self):
        for field in ("sliceKind", "phases", "sdkOwnerEnrollment", "controlCommit", "sourcePins", "allowedPhases"):
            policy, grant, raw, now=self.fixture()
            if field=="phases":
                policy[field][-1]["argv"][-5]="FullyQualifiedName~InstantQuotation"
            else: policy[field]="old-or-broader"
            with self.subTest(field=field), self.environment(), patch.object(os,"name","posix"), self.assertRaises(ValueError):
                resin._validate_permit(policy, raw, now)

    def test_expiry_future_excessive_wrong_run_and_wrong_head(self):
        policy, grant, raw, now=self.fixture()
        for start,end in ((1,30),(-30,0),(0,36)):
            bad=dict(grant,startsUtc=(now+dt.timedelta(minutes=start)).isoformat(),expiresUtc=(now+dt.timedelta(minutes=end)).isoformat())
            changed=json.dumps(bad).encode()
            with self.subTest(start=start,end=end),self.environment(),patch.object(os,"name","posix"),self.assertRaises(ValueError):
                resin._validate_permit(dict(policy,nativeAdmissionSha256=intake.sha256(changed)),changed,now)
        for variable in ("GITHUB_RUN_ID","GITHUB_RUN_ATTEMPT","GITHUB_SHA","WEB_REVIEWED_TRANSPORT_SHA"):
            with self.subTest(variable=variable),self.environment(),patch.dict(os.environ,{variable:"changed"}),self.assertRaises(ValueError):
                resin._validate_permit(policy,raw,now)

    def trx(self, directory):
        root=ET.Element("{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}TestRun")
        ns="{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
        summary=ET.SubElement(root,ns+"ResultSummary",outcome="Completed")
        fields={"total","executed","passed","failed","error","timeout","aborted","inconclusive","passedButRunAborted","notRunnable","notExecuted","disconnected","warning","completed","inProgress","pending"}
        ET.SubElement(summary,ns+"Counters",**{key:"2" if key in {"total","executed","passed"} else "0" for key in fields})
        definitions,results,entries=[ET.SubElement(root,ns+name) for name in ("TestDefinitions","Results","TestEntries")]
        dll=directory/"Legacy.Maliev.Web.Tests/bin/Release/net10.0/Legacy.Maliev.Web.Tests.dll"
        dll.parent.mkdir(parents=True); dll.write_bytes(b"synthetic built assembly")
        for name,(cls,method) in resin.expected_cases().items():
            identity,execution=str(uuid.uuid4()),str(uuid.uuid4())
            definition=ET.SubElement(definitions,ns+"UnitTest",id=identity,name=name,storage=str(dll))
            ET.SubElement(definition,ns+"Execution",id=execution)
            ET.SubElement(definition,ns+"TestMethod",className=cls,name=method,codeBase=str(dll))
            ET.SubElement(results,ns+"UnitTestResult",testId=identity,executionId=execution,testName=name,outcome="Passed")
            ET.SubElement(entries,ns+"TestEntry",testId=identity,executionId=execution)
        path=directory/"resin-comparison-expiry-results/paired-expiry.trx";path.parent.mkdir()
        ET.ElementTree(root).write(path)
        return root,path,ns,intake.sha256(dll.read_bytes())

    def test_two_authored_rows_positive(self):
        with tempfile.TemporaryDirectory() as directory:
            tree,path,ns,digest=self.trx(Path(directory))
            self.assertEqual(2,resin.verify_trx(path,digest)["passed"])

    def test_reader_missing_hash_and_consistent_unrelated_third_row(self):
        with tempfile.TemporaryDirectory() as directory:
            tree,path,ns,digest=self.trx(Path(directory))
            for bad in (None,"", "wrong", "0"*64):
                with self.subTest(hash=bad),self.assertRaises(ValueError): resin.verify_trx(path,bad)
            definitions,results,entries=[tree.find(ns+name) for name in ("TestDefinitions","Results","TestEntries")]
            identity,execution=str(uuid.uuid4()),str(uuid.uuid4())
            name="Legacy.Maliev.Web.Tests.Other.Unrelated";dll=definitions[0].get("storage")
            definition=ET.SubElement(definitions,ns+"UnitTest",id=identity,name=name,storage=dll)
            ET.SubElement(definition,ns+"Execution",id=execution)
            ET.SubElement(definition,ns+"TestMethod",className="Legacy.Maliev.Web.Tests.Other",name="Unrelated",codeBase=dll)
            ET.SubElement(results,ns+"UnitTestResult",testId=identity,executionId=execution,testName=name,outcome="Passed")
            ET.SubElement(entries,ns+"TestEntry",testId=identity,executionId=execution)
            for key in ("total","executed","passed"): tree.find(ns+"ResultSummary").find(ns+"Counters").set(key,"3")
            ET.ElementTree(tree).write(path)
            with self.assertRaises(ValueError): resin.verify_trx(path,digest)

    def test_trx_causal_mutations_reject(self):
        for kind in ("missing","extra","consistent-missing","duplicate-case","case","entry","definition","class","assembly","hash","counter","outcome"):
            with self.subTest(kind=kind),tempfile.TemporaryDirectory() as directory:
                tree,path,ns,digest=self.trx(Path(directory))
                definitions,results,entries=[tree.find(ns+name) for name in ("TestDefinitions","Results","TestEntries")]
                if kind=="missing": results.remove(results[-1])
                if kind=="extra": results.append(copy.deepcopy(results[0]))
                if kind=="consistent-missing":
                    for container in (definitions,results,entries): container.remove(container[-1])
                    counters=tree.find(ns+"ResultSummary").find(ns+"Counters")
                    for field in ("total","executed","passed"): counters.set(field,"1")
                if kind in {"case","duplicate-case"}:
                    name=results[1].get("testName") if kind=="duplicate-case" else "wrong-case"
                    results[0].set("testName",name);definitions[0].set("name",name)
                if kind=="entry": entries[0].set("executionId",str(uuid.uuid4()))
                if kind=="definition": definitions[0].find(ns+"Execution").set("id",str(uuid.uuid4()))
                if kind=="class": definitions[0].find(ns+"TestMethod").set("className","Wrong.Class")
                if kind=="assembly": definitions[0].set("storage","wrong.dll")
                if kind=="hash": digest="0"*64
                if kind=="counter": tree.find(ns+"ResultSummary").find(ns+"Counters").set("passed","1")
                if kind=="outcome": results[0].set("outcome","Skipped")
                ET.ElementTree(tree).write(path)
                with self.assertRaises(ValueError): resin.verify_trx(path,digest)

    def final_fixture(self, directory):
        policy=self.policy();source=policy["sourceBindingSha256"]
        ledger=dict(authorityScope=resin.SCOPE,cleanupVerified=True,managerUnitAbsent=True,sdkStarted=True,deferredSignals=[],unit="web-kernel-123-1-aaaaaaaaaaaa.service",invocationId="original",sourceBindingSha256=source,runId="123",runAttempt="1",executionHead="b"*40,removedCaches=["sdk","cache"])
        worker=dict(unit=ledger["unit"],context={"invocationId":"original"},sourceBindingSha256=source,runId="123",runAttempt="1",transportSha="b"*40,phasesSucceeded=True,controlCommit=resin.CONTROL_SHA,testAssemblySha256="d"*64,trx={key:2 if key in {"total","executed","passed"} else 0 for key in resin.COUNTERS})
        (directory/"launcher.json").write_text(json.dumps(ledger));(directory/"kernel-proof.json").write_text(json.dumps(worker))
        for row in resin.expected_phases():
            receipt=dict(phaseSucceeded=True,cleanupVerified=True,custodySettled=True,leaderReaped=True,pidfdExited=True,privateGroupHasNoLiveMembers=True,admittedArgv=row["argv"],phase=row["name"],existingSdkOwner=worker["context"],sourceBindingSha256=source,runId="123",runAttempt="1",transportSha="b"*40,manifestSha256=policy["manifestSha256"],nativeAdmissionSha256=policy["nativeAdmissionSha256"])
            (directory/(row["id"]+".json")).write_text(json.dumps(receipt))
        return ledger,worker

    def test_final_cleanup_positive_and_unsettled_inverse(self):
        for kind in ("positive","unit-present","wrong-invocation","cache-retained","cancelled","worker-failed","phase-unsettled","wrong-command","wrong-source","trx-missing","assembly-missing","bool-counter"):
            with self.subTest(kind=kind),tempfile.TemporaryDirectory() as directory:
                root=Path(directory);ledger,worker=self.final_fixture(root)
                if kind=="unit-present": ledger["managerUnitAbsent"]=False
                if kind=="wrong-invocation": worker["context"]["invocationId"]="reused"
                if kind=="cache-retained": ledger["removedCaches"].pop()
                if kind=="cancelled": ledger["deferredSignals"]=[15]
                if kind=="worker-failed": worker["phasesSucceeded"]=False
                if kind=="trx-missing": worker.pop("trx")
                if kind=="assembly-missing": worker.pop("testAssemblySha256")
                if kind=="bool-counter": worker["trx"]["failed"]=False
                (root/"launcher.json").write_text(json.dumps(ledger));(root/"kernel-proof.json").write_text(json.dumps(worker))
                path=root/"resin-focused.json";receipt=json.loads(path.read_text())
                if kind=="phase-unsettled": receipt["leaderReaped"]=False
                if kind=="wrong-command": receipt["admittedArgv"]=["dotnet","test"]
                if kind=="wrong-source": receipt["sourceBindingSha256"]="wrong"
                path.write_text(json.dumps(receipt))
                if kind=="positive":
                    with patch("resin_expiry_enrollment.validate_final_context", return_value={}), patch("resin_expiry_enrollment.safe_read", return_value=b"{}"), patch.dict(os.environ, RESIN_ENROLLMENT_CONTEXT="synthetic"):
                        self.assertTrue(resin.verify_final(root))
                else:
                    with self.assertRaises(ValueError): resin.verify_final(root)

    def test_same_source_prior_phase_cleanup_blocks_next(self):
        policy,grant,raw,now=self.fixture()
        current={key:"a"*64 for key in ("contextSha256","challengeSha256","commentSha256","permitSha256","policySha256")}
        current.update(commentId=1234,observedUtc=(now+dt.timedelta(seconds=2)).isoformat(),initial=False)
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            with self.assertRaises(FileNotFoundError): phase.verify_email_phase_history(policy,grant,"resin-restore",root)
            for row in resin.expected_phases()[:2]:
                receipt={key:grant[key] for key in ("sourceBindingSha256","manifestSha256","transportSha","runId","runAttempt","sdkOwnerContextSha256")}
                receipt.update(nativeAdmissionSha256=policy["nativeAdmissionSha256"],phaseSucceeded=True,cleanupVerified=True,phase=row["name"],admittedArgv=row["argv"])
                receipt["enrollment"]=dict(current,observedUtc=now.isoformat(),initial=row["id"]=="resin-sdk-install")
                (root/(row["id"]+".json")).write_text(json.dumps(receipt))
            with patch("resin_expiry_enrollment.last_receipt",return_value=current), patch("resin_expiry_enrollment.original_challenge_deadline",return_value=now+dt.timedelta(seconds=300)):
                phase.verify_email_phase_history(policy,grant,"resin-restore",root)
            receipt["cleanupVerified"]=False
            (root/"resin-sdk-version.json").write_text(json.dumps(receipt))
            with patch("resin_expiry_enrollment.last_receipt",return_value=current), patch("resin_expiry_enrollment.original_challenge_deadline",return_value=now+dt.timedelta(seconds=300)), self.assertRaises(ValueError):
                phase.verify_email_phase_history(policy,grant,"resin-restore",root)

    def test_coherent_wrong_source_receipts_cannot_qualify_policy(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);ledger,worker=self.final_fixture(root)
            ledger["sourceBindingSha256"]="c"*64;worker["sourceBindingSha256"]="c"*64
            (root/"launcher.json").write_text(json.dumps(ledger));(root/"kernel-proof.json").write_text(json.dumps(worker))
            for row in resin.expected_phases():
                path=root/(row["id"]+".json");receipt=json.loads(path.read_text());receipt["sourceBindingSha256"]="c"*64;path.write_text(json.dumps(receipt))
            with self.assertRaises(ValueError): resin.verify_final(root)

    def test_enrolled_resin_owner_nonce_generation_and_kernel_limits(self):
        from test_web_candidate_custody import CapControls
        for mutation in ("positive","nonce","generation","memory","swap","cpu","tasks"):
            with self.subTest(mutation=mutation),tempfile.TemporaryDirectory() as directory:
                _,file,proc,cg,target,show,context=CapControls().fixture(Path(directory))
                policy=self.policy()
                grant=dict(ownerNonce="d"*32,runId="123",runAttempt="1",expiresUtc=context["expiresUtc"],sdkOwnerEnrollment=policy["sdkOwnerEnrollment"])
                context["unit"]="web-kernel-123-1-dddddddddddd.service";show["Id"]=context["unit"]
                if mutation=="nonce": grant["ownerNonce"]="e"*32
                if mutation=="generation": context["inode"]+=1
                if mutation=="memory": (target/"memory.max").write_text("max")
                if mutation=="swap": (target/"memory.swap.max").write_text("1024")
                if mutation=="cpu": (target/"cpu.max").write_text("200000 100000")
                if mutation=="tasks": (target/"pids.max").write_text("513")
                file.write_text(json.dumps(context))
                if mutation=="positive": self.assertEqual(context,phase.verify_capped_owner(policy,file,proc,cg,show,grant=grant))
                else:
                    with self.assertRaises(ValueError): phase.verify_capped_owner(policy,file,proc,cg,show,grant=grant)

    def test_rebound_broader_phase_never_dispatches(self):
        policy,grant,raw,now=self.fixture()
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory); permit=root/"permit";permit.write_bytes(raw)
            argv=["phase","--policy","synthetic","--permit",str(permit),"--evidence",str(root),"--phase","focused","--id","resin-focused","--","dotnet","test"]
            with patch.object(sys,"argv",argv),patch.object(intake,"load_policy",return_value=policy),patch.object(admission,"validate",return_value=grant),patch.object(admission,"census"),patch.object(phase,"remaining_seconds",return_value=100),patch.object(phase.subprocess,"Popen") as actor:
                with self.assertRaises(ValueError): phase.main()
                actor.assert_not_called()


if __name__=="__main__": unittest.main()
