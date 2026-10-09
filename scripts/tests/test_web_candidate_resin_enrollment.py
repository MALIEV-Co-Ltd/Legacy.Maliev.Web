"""Pure custody controls: synthetic origin only, no API or resource execution."""
import base64
import copy
import datetime as dt
import hashlib
import os
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch, MagicMock

sys.path.insert(0, str(Path(__file__).parents[1]))
import resin_expiry_enrollment as enroll
import materialize_web_candidate as intake
import run_web_resin_comparison_expiry as resin
import check_web_candidate_admission as admission
import run_web_candidate_phase as phase


class EnrollmentControls(unittest.TestCase):
    def setUp(self):
        self.now = dt.datetime(2026, 10, 9, tzinfo=dt.timezone.utc)
        self.policy = enroll.body_policy()
        self.policy["enrollmentCustodian"] = {"login": "fixture-custodian", "id": 42,
                                            "type": "User", "roleBindingSha256": "a"*64}
        self.env = {"GITHUB_REPOSITORY": "MALIEV-Co-Ltd/Legacy.Maliev.Web", "GITHUB_EVENT_NAME": "workflow_dispatch",
                    "GITHUB_REF": "refs/heads/main", "GITHUB_SHA": "b"*40, "WEB_REVIEWED_TRANSPORT_SHA": "b"*40,
                    "GITHUB_RUN_ID": "123", "GITHUB_RUN_ATTEMPT": "1"}
        self.environ = patch.dict(os.environ, self.env)
        self.environ.start(); self.addCleanup(self.environ.stop)

    def bundle(self):
        with patch.object(enroll, "body_policy", return_value=self.policy):
            challenge = enroll.challenge(self.policy, self.now)
        pending = intake.parse_json(challenge)
        grant = {key: self.policy[key] for key in ("acceptedBase", "sourcePins", "sourceBindingSha256", "manifestSha256",
                 "sdkOwnerEnrollment", "sdkOwnerContextSha256", "phases", "allowedPhases", "controlCommit")}
        grant.update(owner=admission.OWNER, issuedBy=enroll.issuer(self.policy), environment="github-hosted-linux",
                     sliceKind=resin.SCOPE, transportSha="b"*40, runId="123", runAttempt="1", ownerNonce=pending["ownerNonce"],
                     startsUtc=self.now.isoformat(), expiresUtc=(self.now+dt.timedelta(minutes=30)).isoformat())
        permit = enroll.canonical(grant)
        record = dict(enroll.hosted_binding(self.policy), ownerNonce=pending["ownerNonce"],
                      challengeSha256=intake.sha256(challenge), deadlineUtc=pending["deadlineUtc"], issuedBy=grant["issuedBy"],
                      permitBlob=hashlib.sha1(b"blob "+str(len(permit)).encode()+b"\0"+permit).hexdigest(),
                      permitSha256=intake.sha256(permit), enrolledUtc=grant["startsUtc"], permitExpiresUtc=grant["expiresUtc"])
        comment = {"id": 1234, "url": enroll.API+"/comments/1234", "issue_url": enroll.API+"/98",
                   "user": {"login": "fixture-custodian", "id": 42, "type": "User"},
                   "created_at": self.now.isoformat(), "updated_at": self.now.isoformat(),
                   "body": enroll.PREFIX+enroll.canonical(record).decode()}
        raw_comment = enroll.canonical(comment)
        context = {"schemaVersion": 1, "challenge": base64.b64encode(challenge).decode(),
                   "comment": base64.b64encode(raw_comment).decode(), "permit": base64.b64encode(permit).decode(),
                   "firstVerifiedUtc": self.now.isoformat()}
        return challenge, comment, raw_comment, permit, enroll.canonical(context)

    def test_actual_null_custodian_rejects_before_network_or_dispatch(self):
        policy = enroll.body_policy()
        with patch.object(enroll, "api_read", side_effect=AssertionError("network")), patch.object(phase.subprocess, "Popen", side_effect=AssertionError("actor")):
            with self.assertRaises(ValueError): enroll.require_context(policy, b"{}", initial=True)
            with self.assertRaises(ValueError): admission.validate(policy, b"{}")

    def test_source_challenge_is_fixed_run_and_exact_original_five_minutes(self):
        with patch.object(enroll, "body_policy", return_value=self.policy):
            raw = enroll.challenge(self.policy, self.now+dt.timedelta(microseconds=999999))
        row = enroll.check_challenge(self.policy, raw, self.now, True)
        self.assertEqual(self.now, enroll.instant(row["createdUtc"]))
        self.assertEqual(300, (enroll.instant(row["deadlineUtc"])-self.now).total_seconds())
        with self.assertRaises(ValueError): enroll.check_challenge(self.policy, raw, self.now+dt.timedelta(seconds=300), True)

    def test_origin_and_complete_original_permit_positive(self):
        challenge, comment, raw, permit, context = self.bundle()
        with patch.object(os, "name", "posix"):
            self.assertEqual(resin.SCOPE, enroll.private_validate(self.policy, challenge, raw, permit, self.now, True)["sliceKind"])

    def test_wrong_author_id_login_type_and_selfclaimed_issuer_reject(self):
        challenge, comment, raw, permit, context = self.bundle()
        for key, value in (("id", 43), ("id", True), ("login", "another-custodian"), ("type", "Bot")):
            bad = copy.deepcopy(comment); bad["user"][key] = value
            with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                enroll.check_comment(self.policy, challenge, enroll.canonical(bad), self.now, True)

    def test_wrong_issue_comment_id_or_original_timestamp_reject(self):
        challenge, comment, raw, permit, context = self.bundle()
        for key, value in (("issue_url", enroll.API+"/99"), ("url", enroll.API+"/comments/99"),
                           ("id", True), ("updated_at", (self.now+dt.timedelta(seconds=1)).isoformat()),
                           ("created_at", (self.now-dt.timedelta(seconds=1)).isoformat())):
            bad = dict(comment); bad[key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                enroll.check_comment(self.policy, challenge, enroll.canonical(bad), self.now, True)

    def test_all_record_run_source_nonce_digest_joins_reject_coherent_rebind(self):
        challenge, comment, raw, permit, context = self.bundle()
        original = intake.parse_json(comment["body"][len(enroll.PREFIX):].encode())
        for key in ("transportSha", "runId", "runAttempt", "ownerNonce", "policySha256", "sourceBindingSha256", "manifestSha256", "challengeSha256", "acceptedBase", "controlCommit", "issuedBy"):
            bad = dict(original); bad[key] = "changed"
            forged = dict(comment, body=enroll.PREFIX+enroll.canonical(bad).decode())
            with self.subTest(key=key), self.assertRaises(ValueError):
                enroll.check_comment(self.policy, challenge, enroll.canonical(forged), self.now, True)

    def test_raw_permit_digest_gitblob_and_full_scope_cannot_be_rebound(self):
        challenge, comment, raw, permit, context = self.bundle()
        for field in ("phases", "sourcePins", "sdkOwnerEnrollment", "ownerNonce", "allowedPhases", "sliceKind"):
            grant = intake.parse_json(permit); grant[field] = "changed"; changed = enroll.canonical(grant)
            record = intake.parse_json(comment["body"][len(enroll.PREFIX):].encode())
            record["permitSha256"] = intake.sha256(changed)
            record["permitBlob"] = hashlib.sha1(b"blob "+str(len(changed)).encode()+b"\0"+changed).hexdigest()
            forged = dict(comment, body=enroll.PREFIX+enroll.canonical(record).decode())
            with self.subTest(field=field), patch.object(os,"name","posix"), self.assertRaises(ValueError):
                enroll.private_validate(self.policy, challenge, enroll.canonical(forged), changed, self.now, True)
        with self.assertRaises(ValueError): enroll.private_validate(self.policy, challenge, raw, permit+b" ", self.now, True)

    def test_current_issuer_tuple_rejects_float_boolean_and_historical_aliases(self):
        challenge,comment,raw,permit,context=self.bundle()
        for value in (42.0, True, "42"):
            record=intake.parse_json(comment["body"][len(enroll.PREFIX):].encode())
            record["issuedBy"]["id"]=value
            bad=dict(comment,body=enroll.PREFIX+enroll.canonical(record).decode())
            with self.subTest(value=value),self.assertRaises(ValueError):
                enroll.check_comment(self.policy,challenge,enroll.canonical(bad),self.now,True)
        record=intake.parse_json(comment["body"][len(enroll.PREFIX):].encode())
        record["issuedBy"]="019fc21e-50f0-7112-834f-9fb3b35b9dfe"
        with self.assertRaises(ValueError):
            enroll.check_comment(self.policy,challenge,enroll.canonical(dict(comment,body=enroll.PREFIX+enroll.canonical(record).decode())),self.now,True)

    def test_replayed_actual_head_run_or_attempt_rejects_original_challenge(self):
        challenge, comment, raw, permit, context = self.bundle()
        for key in self.env:
            with self.subTest(key=key), patch.dict(os.environ, {key: "changed"}), self.assertRaises(ValueError):
                enroll.check_challenge(self.policy, challenge, self.now, True)

    def test_live_context_refreshes_clock_after_read_before_initial_admission(self):
        challenge, comment, raw, permit, context = self.bundle()
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)/"context"; path.write_bytes(context)
            with patch.dict(os.environ, RESIN_ENROLLMENT_CONTEXT=str(path)), patch.object(enroll,"body_policy",return_value=self.policy), patch.object(enroll,"api_read",return_value=raw), patch.object(enroll,"utcnow",side_effect=[self.now,self.now+dt.timedelta(seconds=300)]), patch.object(os,"name","posix"), self.assertRaises(ValueError):
                enroll.require_context(self.policy, permit, initial=True)
            self.assertIsNone(enroll.LAST_VERIFIED)

    def test_live_context_changed_deleted_or_denied_origin_never_replaced(self):
        challenge, comment, raw, permit, context = self.bundle()
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/"context"; path.write_bytes(context)
            for result in (raw+b" ", ValueError("deleted or denied")):
                with self.subTest(result=type(result).__name__), patch.dict(os.environ,RESIN_ENROLLMENT_CONTEXT=str(path)), patch.object(enroll,"body_policy",return_value=self.policy), patch.object(enroll,"api_read",side_effect=result if isinstance(result,Exception) else None,return_value=result), self.assertRaises(ValueError):
                    enroll.require_context(self.policy,permit,self.now,True)

    def test_actual_context_receipt_binds_original_bytes(self):
        challenge,comment,raw,permit,context=self.bundle()
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/"context";path.write_bytes(context)
            with patch.dict(os.environ,RESIN_ENROLLMENT_CONTEXT=str(path)),patch.object(enroll,"body_policy",return_value=self.policy),patch.object(enroll,"api_read",return_value=raw),patch.object(os,"name","posix"):
                enroll.require_context(self.policy,permit,self.now,True)
            receipt=enroll.last_receipt()
            self.assertEqual(intake.sha256(context),receipt["contextSha256"])
            self.assertIs(receipt["initial"],True)
            receipt["commentId"]=99
            self.assertEqual(1234,enroll.last_receipt()["commentId"])

    def admissions(self, context, challenge, raw, permit):
        expected={"contextSha256":intake.sha256(context),"challengeSha256":intake.sha256(challenge),"commentId":1234,
                  "commentSha256":intake.sha256(raw),"permitSha256":intake.sha256(permit),"policySha256":intake.RESIN_EXPIRY_POLICY_SHA256}
        return [dict(expected,observedUtc=(self.now+dt.timedelta(seconds=i)).isoformat(),initial=i in (0,1)) for i in range(6)]

    def test_final_historical_admissions_not_live_expiry_but_origin_reobserved(self):
        challenge,comment,raw,permit,context=self.bundle()
        with patch.object(enroll,"body_policy",return_value=self.policy),patch.object(enroll,"api_read",return_value=raw),patch.object(os,"name","posix"):
            result=enroll.validate_final_context(self.policy,context,self.admissions(context,challenge,raw,permit))
        self.assertIs(result["finalOriginVerified"],True)
        self.assertIs(result["originalAdmissionTimesVerified"],True)

    def test_final_context_initial_sdk_receipt_source_and_lifetime_inverses(self):
        challenge,comment,raw,permit,context=self.bundle()
        rows=self.admissions(context,challenge,raw,permit)
        mutations=((0,"initial",False),(1,"initial",False),(2,"initial",True),(1,"observedUtc",(self.now+dt.timedelta(seconds=300)).isoformat()),
                   (5,"observedUtc",(self.now+dt.timedelta(minutes=31)).isoformat()),(3,"contextSha256","c"*64),(2,"commentId",True))
        for index,key,value in mutations:
            bad=copy.deepcopy(rows);bad[index][key]=value
            with self.subTest(index=index,key=key),patch.object(enroll,"body_policy",return_value=self.policy),patch.object(enroll,"api_read",return_value=raw),patch.object(os,"name","posix"),self.assertRaises(ValueError):
                enroll.validate_final_context(self.policy,context,bad)

    def test_final_changed_original_comment_rejects_after_physical_cleanup(self):
        challenge,comment,raw,permit,context=self.bundle()
        with patch.object(enroll,"body_policy",return_value=self.policy),patch.object(enroll,"api_read",return_value=raw+b" "),self.assertRaises(ValueError):
            enroll.validate_final_context(self.policy,context,self.admissions(context,challenge,raw,permit))

    def test_since_query_finds_current_record_without_reading_509_historical_comments(self):
        challenge,comment,raw,permit,context=self.bundle();urls=[]
        def read(url,deadline):
            urls.append(url)
            return enroll.canonical([comment]) if "?" in url else raw
        with tempfile.TemporaryDirectory() as directory,patch.object(enroll,"body_policy",return_value=self.policy),patch.object(enroll,"utcnow",return_value=self.now),patch.object(enroll,"api_read",side_effect=read),patch.object(intake,"fetch_blob",return_value=permit),patch.object(resin,"os",SimpleNamespace(name="posix",environ=os.environ)):
            context_path=Path(directory)/"context";permit_path=Path(directory)/"permit"
            enroll.wait_for_enrollment(self.policy,challenge,context_path,permit_path)
            self.assertEqual(permit,permit_path.read_bytes())
            self.assertEqual(enroll.API+"/98/comments?per_page=100&since=2026-10-09T00%3A00%3A00Z",urls[0])
            self.assertEqual(enroll.API+"/comments/1234",urls[1])

    def test_truncated_or_ambiguous_new_comment_cohort_fails_closed(self):
        challenge,comment,raw,permit,context=self.bundle()
        for values in ([{}]*100,[comment,comment]):
            with self.subTest(size=len(values)),patch.object(enroll,"body_policy",return_value=self.policy),patch.object(enroll,"utcnow",return_value=self.now),patch.object(enroll,"api_read",return_value=enroll.canonical(values)),patch.object(intake,"fetch_blob",side_effect=AssertionError("fetch")),self.assertRaises(ValueError):
                enroll.wait_for_enrollment(self.policy,challenge,"unused","unused")

    def test_original_wait_reserve_cannot_reset_after_challenge_delay(self):
        challenge,comment,raw,permit,context=self.bundle()
        with patch.object(enroll,"body_policy",return_value=self.policy),patch.object(enroll,"utcnow",return_value=self.now+dt.timedelta(seconds=180)),patch.object(enroll,"api_read",side_effect=AssertionError("read")),self.assertRaises(TimeoutError):
            enroll.wait_for_enrollment(self.policy,challenge,"unused","unused")

    def test_poll_interval_is_ten_seconds_inside_original_reserve(self):
        challenge,comment,raw,permit,context=self.bundle()
        with patch.object(enroll,"body_policy",return_value=self.policy),patch.object(enroll,"utcnow",return_value=self.now),patch.object(enroll,"api_read",return_value=b"[]"),patch.object(enroll.time,"sleep",side_effect=RuntimeError("stop")) as sleep,self.assertRaises(RuntimeError):
            enroll.wait_for_enrollment(self.policy,challenge,"unused","unused")
        self.assertGreater(sleep.call_args.args[0],9)
        self.assertLessEqual(sleep.call_args.args[0],10)

    def test_api_url_escape_redirect_denial_and_bytes_are_closed(self):
        with self.assertRaises(ValueError):enroll.api_read("https://example.invalid",0)
        with self.assertRaises(ValueError):enroll.NoRedirect().redirect_request(None,None,None,None,None,None)
        response=MagicMock();response.__enter__.return_value=response;response.status=200
        url=enroll.API+"/comments/1234";response.geturl.return_value=url
        response.isclosed.return_value=False;response.read1.return_value=b"x"*(enroll.MAX_RESPONSE+1)
        opener=MagicMock();opener.open.return_value=response
        with patch.object(enroll.urllib.request,"build_opener",return_value=opener),self.assertRaises(ValueError):
            enroll.api_read(url,enroll.time.monotonic()+5)
        request=opener.open.call_args.args[0]
        self.assertNotIn("Authorization",dict(request.header_items()))
        response.status=403
        with patch.object(enroll.urllib.request,"build_opener",return_value=opener),self.assertRaises(ValueError):enroll.api_read(url,enroll.time.monotonic()+5)

    def test_api_closed_truncated_and_read_crossing_deadline_reject(self):
        url=enroll.API+"/comments/1234";response=MagicMock();response.__enter__.return_value=response
        response.status=200;response.geturl.return_value=url;response.isclosed.return_value=True;response.length=5
        opener=MagicMock();opener.open.return_value=response
        with patch.object(enroll.urllib.request,"build_opener",return_value=opener),self.assertRaises(ValueError):enroll.api_read(url,enroll.time.monotonic()+5)
        response.isclosed.return_value=False;response.read1.return_value=b"{}"
        with patch.object(enroll.urllib.request,"build_opener",return_value=opener),patch.object(enroll.time,"monotonic",side_effect=[0,0,6]),self.assertRaises(TimeoutError):enroll.api_read(url,5)

    def test_real_launcher_resin_forwarded_argv_meets_worker_binding(self):
        import ast
        source=(Path(__file__).parents[1]/"launch_web_capped_kernel_proof.py").read_text()
        tree=ast.parse(source);main=next(row for row in tree.body if isinstance(row,ast.FunctionDef) and row.name=="main")
        nodes=[];capture=False
        for row in main.body:
            if isinstance(row,ast.Assign) and any(isinstance(target,ast.Name) and target.id=="forwarded" for target in row.targets):capture=True
            if capture and isinstance(row,ast.FunctionDef):break
            if capture:nodes.append(row)
        namespace={"os":os,"account":True,"resin":True}
        with patch.dict(os.environ,PATH="synthetic",HOME="synthetic",RUNNER_ENVIRONMENT="github-hosted",RESIN_ENROLLMENT_CONTEXT="synthetic-context"):
            exec(compile(ast.Module(nodes,type_ignores=[]),"actual-launcher-forwarding","exec"),namespace)
        worker={argument[len("--setenv="):].split("=",1)[0]:argument.split("=",2)[2] for argument in namespace["forwarded"]}
        worker["GITHUB_REPOSITORY"]="MALIEV-Co-Ltd/Legacy.Maliev.Web"
        self.assertNotIn("GH_TOKEN",worker)
        with patch.dict(os.environ,worker,clear=True):self.assertEqual("123",enroll.hosted_binding(self.policy)["runId"])

    def test_later_phase_requires_real_successful_first_sdk_receipt(self):
        challenge,comment,raw,permit,context=self.bundle();grant=intake.parse_json(permit)
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(FileNotFoundError):phase.verify_email_phase_history(self.policy,grant,"resin-sdk-version",Path(directory))
            row=self.policy["phases"][0]
            receipt={key:self.policy[key] for key in ("sourceBindingSha256","manifestSha256","sdkOwnerContextSha256","nativeAdmissionSha256")}
            receipt.update({key:grant[key] for key in ("transportSha","runId","runAttempt")})
            receipt.update(phaseSucceeded=False,cleanupVerified=True,phase=row["name"],admittedArgv=row["argv"])
            (Path(directory)/"resin-sdk-install.json").write_bytes(enroll.canonical(receipt))
            with self.assertRaises(ValueError):phase.verify_email_phase_history(self.policy,grant,"resin-sdk-version",Path(directory))

    def test_history_original_custody_positive_and_each_identity_inverse(self):
        challenge,comment,raw,permit,context=self.bundle();grant=intake.parse_json(permit)
        current=self.admissions(context,challenge,raw,permit)[2]
        with tempfile.TemporaryDirectory() as directory:
            evidence=Path(directory)
            context_path=evidence/"context";context_path.write_bytes(context)
            rows=[]
            for i,row in enumerate(self.policy["phases"][:2]):
                receipt={key:self.policy[key] for key in ("sourceBindingSha256","manifestSha256","sdkOwnerContextSha256","nativeAdmissionSha256")}
                receipt.update({key:grant[key] for key in ("transportSha","runId","runAttempt")})
                receipt.update(phaseSucceeded=True,cleanupVerified=True,phase=row["name"],admittedArgv=row["argv"],enrollment=dict(current,observedUtc=(self.now+dt.timedelta(seconds=i)).isoformat(),initial=i==0))
                rows.append(receipt)
                (evidence/(row["id"]+".json")).write_bytes(enroll.canonical(receipt))
            with patch.dict(os.environ,RESIN_ENROLLMENT_CONTEXT=str(context_path)),patch.object(enroll,"last_receipt",return_value=current):
                phase.verify_email_phase_history(self.policy,grant,"resin-restore",evidence)
                for key in ("contextSha256","challengeSha256","commentId","commentSha256","permitSha256","policySha256"):
                    bad=copy.deepcopy(rows[0]);bad["enrollment"][key]=True if key=="commentId" else "c"*64
                    (evidence/"resin-sdk-install.json").write_bytes(enroll.canonical(bad))
                    with self.subTest(key=key),self.assertRaises(ValueError):phase.verify_email_phase_history(self.policy,grant,"resin-restore",evidence)

    def test_history_missing_extra_initial_and_observation_inverses(self):
        challenge,comment,raw,permit,context=self.bundle();grant=intake.parse_json(permit)
        current=self.admissions(context,challenge,raw,permit)[1]
        row=self.policy["phases"][0]
        receipt={key:self.policy[key] for key in ("sourceBindingSha256","manifestSha256","sdkOwnerContextSha256","nativeAdmissionSha256")}
        receipt.update({key:grant[key] for key in ("transportSha","runId","runAttempt")})
        receipt.update(phaseSucceeded=True,cleanupVerified=True,phase=row["name"],admittedArgv=row["argv"],enrollment=dict(current,observedUtc=self.now.isoformat(),initial=True))
        with tempfile.TemporaryDirectory() as directory,patch.object(enroll,"last_receipt",return_value=current):
            evidence=Path(directory)
            for variant in (None,{},dict(receipt["enrollment"],extra=True),dict(receipt["enrollment"],initial=1),dict(receipt["enrollment"],initial=False),
                            dict(receipt["enrollment"],observedUtc=(self.now-dt.timedelta(seconds=1)).isoformat()),dict(receipt["enrollment"],observedUtc=(self.now+dt.timedelta(seconds=2)).isoformat())):
                bad=dict(receipt,enrollment=variant)
                (evidence/"resin-sdk-install.json").write_bytes(enroll.canonical(bad))
                with self.subTest(variant=variant),self.assertRaises(ValueError):phase.verify_email_phase_history(self.policy,grant,"resin-sdk-version",evidence)

    def test_coherently_rewritten_prior_context_stops_actual_main_before_popen(self):
        challenge,comment,raw,permit,context=self.bundle();grant=intake.parse_json(permit)
        current=self.admissions(context,challenge,raw,permit)[2]
        row=self.policy["phases"][0]
        receipt={key:self.policy[key] for key in ("sourceBindingSha256","manifestSha256","sdkOwnerContextSha256","nativeAdmissionSha256")}
        receipt.update({key:grant[key] for key in ("transportSha","runId","runAttempt")})
        receipt.update(phaseSucceeded=True,cleanupVerified=True,phase=row["name"],admittedArgv=row["argv"],enrollment=dict(current,contextSha256="c"*64,challengeSha256="c"*64,commentSha256="c"*64,permitSha256="c"*64,observedUtc=self.now.isoformat(),initial=True))
        with tempfile.TemporaryDirectory() as directory:
            evidence=Path(directory);permit_path=evidence/"permit";permit_path.write_bytes(permit)
            (evidence/"resin-sdk-install.json").write_bytes(enroll.canonical(receipt))
            selected=self.policy["phases"][1]
            argv=["phase","--policy","synthetic","--permit",str(permit_path),"--evidence",str(evidence),"--phase",selected["name"],"--id",selected["id"],"--",*selected["argv"]]
            with patch.object(sys,"argv",argv),patch.object(intake,"load_policy",return_value=self.policy),patch.object(admission,"validate",return_value=grant),patch.object(admission,"census"),patch.object(phase,"remaining_seconds",return_value=300),patch.object(enroll,"last_receipt",return_value=current),patch.object(phase.subprocess,"Popen",side_effect=AssertionError("unrelated dispatch")) as actor,self.assertRaises(ValueError):
                phase.main()
            actor.assert_not_called()

    def test_reversed_or_expired_initial_history_stops_actual_main_before_popen(self):
        challenge,comment,raw,permit,context=self.bundle();grant=intake.parse_json(permit)
        for offsets,current_offset in (([2,1],6),([300,301],302),([400,401],402)):
            with self.subTest(offsets=offsets),tempfile.TemporaryDirectory() as directory:
                evidence=Path(directory);permit_path=evidence/"permit";permit_path.write_bytes(permit)
                context_path=evidence/"context";context_path.write_bytes(context)
                current=self.admissions(context,challenge,raw,permit)[2]
                current["observedUtc"]=(self.now+dt.timedelta(seconds=current_offset)).isoformat()
                for i,row in enumerate(self.policy["phases"][:2]):
                    receipt={key:self.policy[key] for key in ("sourceBindingSha256","manifestSha256","sdkOwnerContextSha256","nativeAdmissionSha256")}
                    receipt.update({key:grant[key] for key in ("transportSha","runId","runAttempt")})
                    receipt.update(phaseSucceeded=True,cleanupVerified=True,phase=row["name"],admittedArgv=row["argv"],enrollment=dict(current,observedUtc=(self.now+dt.timedelta(seconds=offsets[i])).isoformat(),initial=i==0))
                    (evidence/(row["id"]+".json")).write_bytes(enroll.canonical(receipt))
                selected=self.policy["phases"][2]
                argv=["phase","--policy","synthetic","--permit",str(permit_path),"--evidence",str(evidence),"--phase",selected["name"],"--id",selected["id"],"--",*selected["argv"]]
                with patch.dict(os.environ,RESIN_ENROLLMENT_CONTEXT=str(context_path)),patch.object(sys,"argv",argv),patch.object(intake,"load_policy",return_value=self.policy),patch.object(admission,"validate",return_value=grant),patch.object(admission,"census"),patch.object(phase,"remaining_seconds",return_value=300),patch.object(enroll,"last_receipt",return_value=current),patch.object(phase.subprocess,"Popen",side_effect=AssertionError("dispatch")) as actor,self.assertRaises(ValueError):
                    phase.main()
                actor.assert_not_called()

    def test_history_original_deadline_uses_current_bound_context_not_permit_start(self):
        challenge,comment,raw,permit,context=self.bundle()
        current=self.admissions(context,challenge,raw,permit)[2]
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/"context";path.write_bytes(context)
            with patch.dict(os.environ,RESIN_ENROLLMENT_CONTEXT=str(path)):
                self.assertEqual(self.now+dt.timedelta(seconds=300),enroll.original_challenge_deadline(self.policy,current))
                mutated=intake.parse_json(context);mutated["firstVerifiedUtc"]=(self.now+dt.timedelta(seconds=1)).isoformat()
                path.write_bytes(enroll.canonical(mutated))
                with self.assertRaises(ValueError):enroll.original_challenge_deadline(self.policy,current)

    def test_actual_phase_preallocation_block_stops_popen_after_origin_expiry(self):
        import ast
        from types import SimpleNamespace
        tree=ast.parse((Path(__file__).parents[1]/"run_web_candidate_phase.py").read_text())
        main=next(row for row in tree.body if isinstance(row,ast.FunctionDef) and row.name=="main")
        final_try=next(row for row in main.body if isinstance(row,ast.Try))
        block=final_try.body[0].body
        cutoff=next(i for i,row in enumerate(block) if isinstance(row,ast.Assign) and any(isinstance(target,ast.Name) and target.id=="proc" for target in row.targets))
        actor=MagicMock();failure=ValueError("original challenge expired during authority read")
        namespace=dict(phase.__dict__)
        namespace.update(policy=self.policy,raw=b"synthetic",owner_context={},owner_arguments={},
                         args=SimpleNamespace(id="resin-sdk-install",sdk_owner_context=None),record={},
                         verify_capped_owner=lambda *a,**k:{},remaining_seconds=lambda _:300,
                         command=["bash"],executable="synthetic",subprocess=SimpleNamespace(Popen=actor,PIPE=-1,STDOUT=-2))
        with patch.object(admission,"validate",return_value={}),patch.object(admission,"census"),patch.object(enroll,"require_context",side_effect=failure),self.assertRaises(ValueError) as caught:
            exec(compile(ast.Module(block[:cutoff+1],type_ignores=[]),"actual-phase-allocation-block","exec"),namespace)
        self.assertIs(caught.exception,failure);actor.assert_not_called()

    def test_actual_unit_preallocation_block_stops_systemd_after_origin_expiry(self):
        import ast
        tree=ast.parse((Path(__file__).parents[1]/"launch_web_capped_kernel_proof.py").read_text())
        main=next(row for row in tree.body if isinstance(row,ast.FunctionDef) and row.name=="main")
        dispatch_try=next(row for row in main.body if isinstance(row,ast.Try))
        # Execute the exact final Resin check through its adjacent actor call,
        # after earlier source/census/cache admission has already completed.
        checks=[i for i,row in enumerate(dispatch_try.body) if isinstance(row,ast.If) and isinstance(row.test,ast.Name) and row.test.id=="resin"]
        start=checks[-1];end=next(i for i,row in enumerate(dispatch_try.body[start:],start) if isinstance(row,ast.Expr) and isinstance(row.value,ast.Call) and isinstance(row.value.func,ast.Name) and row.value.func.id=="command")
        actor=MagicMock();failure=ValueError("original challenge expired during authority read")
        namespace={"resin":True,"admission":admission,"selected_policy":Path("synthetic"),"args":SimpleNamespace(permit=SimpleNamespace(read_bytes=lambda:b"synthetic")),"ledger":{},"command":actor}
        with patch.object(intake,"load_policy",return_value=self.policy),patch.object(enroll,"require_context",side_effect=failure),self.assertRaises(ValueError) as caught:
            exec(compile(ast.Module(dispatch_try.body[start:end+1],type_ignores=[]),"actual-unit-allocation-block","exec"),namespace)
        self.assertIs(caught.exception,failure);actor.assert_not_called()


if __name__ == "__main__":unittest.main()
