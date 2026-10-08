"""Shipping exact-head receipt/TRX gate; bootstrap provenance main 25c33e2.
Run --self-test for pure metadata causal negatives; no native resource allocation.
"""
import copy
import datetime
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import unittest
import uuid
import xml.etree.ElementTree as ET

DATASET = "thailand-geography-json:b8b3fb91c7df1129ff5b43cb46f7fcffadd2156b"
METHOD = "Legacy.Maliev.Web.Tests.ThaiLookupShippingPersistenceTests.ShippingTuple_NormalSave_ApiReadbackAndReloadPreserveManualFields"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def equal_fields(data, expected, message):
    require(all(data.get(key) == value and type(data.get(key)) is type(value)
                for key, value in expected.items()), message)


def validate_rows(root):
    rows = root.findall(".//t:UnitTestResult", NS)
    expected = {f'{METHOD}(culture: "en", width: 1280)', f'{METHOD}(culture: "th", width: 375)'}
    require(len(rows) == 2 and {row.get("testName") for row in rows} == expected
            and all(row.get("outcome") == "Passed" for row in rows), "Two exact unique passed shipping rows required")
    counters = root.find(".//t:Counters", NS)
    required = dict(total="2", executed="2", passed="2", failed="0", error="0", timeout="0", aborted="0",
                    inconclusive="0", passedButRunAborted="0", notRunnable="0", notExecuted="0",
                    disconnected="0", warning="0", completed="0", inProgress="0", pending="0")
    require(counters is not None and all(counters.get(k) == v for k, v in required.items()),
            "Shipping TRX counters must represent two completed passed cases")


def validate_receipt(receipt, candidate, culture, width):
    equal_fields(receipt, dict(surface="member-shipping", culture=culture, width=width, candidateHead=candidate,
                              catalogStatus=200, saveStatus=302, readbackStatus=200, reloadVerified=True,
                              billingPreserved=True, manualDetailPreserved=True, datasetVersion=DATASET),
                 "Incomplete or foreign shipping business receipt")
    proof = receipt.get("shippingBackendCleanup", {})
    equal_fields(proof, dict(schema=1, surface="member-shipping", culture=culture, candidateHead=candidate,
                            owner="web-billing-proof", complete=True, graphReleased=True, expired=False,
                            readerFailureRejected=False, expiryJoined=True, sharedAuthorityExcluded=True),
                 "Actual shipping graph cleanup receipt incomplete")
    run = proof.get("run", "")
    require(re.fullmatch("[0-9a-f]{32}", run) is not None, "Original run identity required")
    graph = proof.get("graph", {})
    require(uuid.UUID(graph.get("run", "")).hex == run, "Graph/backend original run join required")
    equal_fields(graph, dict(owner="web-billing-proof", released=True, retained=False,
                            expired=False, readerFailureRejected=False), "Unreleased or foreign ownership graph")
    require(graph.get("expiryFailure") is None, "Expiry failure rejects proof")
    children = graph.get("children", [])
    require(len(children) == 3 and all(child.get("dispatched") is True and child.get("identityCaptured") is True
            and child.get("exitVerified") is True and child.get("readersClosed") is True
            and child.get("released") is True for child in children), "Three original child readers/handles must be settled")
    hosts = graph.get("hosts", [])
    require(bool(hosts) and all(host.get("startupComplete") is True and host.get("disposal") == "RanToCompletion"
                              for host in hosts), "Original frontend host teardown must be settled")
    resources = proof.get("resources", [])
    require(len(resources) == 1 and graph.get("backends") == resources, "Original backend graph join required")
    backend = resources[0]
    name = "billing-proof-" + run + "-postgres"
    equal_fields(backend, dict(backend="postgres", localEndpoint="unix:///var/run/docker.sock",
                              databaseSynthetic=True, name=name, owner="web-billing-proof", run=run,
                              baselineAbsent=True, startDispatched=True, startupSettled=True,
                              captureVerified=True, absenceVerified=True, sdkReleased=True),
                 "Original shipping backend cleanup incomplete")
    cid, database = backend.get("originalId", ""), backend.get("database", "")
    require(re.fullmatch("[0-9a-f]{64}", cid) is not None
            and re.fullmatch("profile_contract_[0-9a-f]{32}", database) is not None
            and bool(backend.get("daemon")), "Original CID/database/daemon required")
    original = backend.get("original", {})
    equal_fields(original, dict(id=cid, name="/" + name, owner="web-billing-proof", run=run,
                                expiresUtc=backend.get("expiresUtc"), configuredImage="postgres:18-alpine",
                                envelopeValid=True, initEnabled=True, memoryBytes=536870912,
                                swapBytes=536870912, nanoCpus=1000000000,
                                tmpfs="/var/lib/postgresql:rw,size=268435456", loopbackPorts=True,
                                persistentData=False), "Original creation/resource envelope mismatch")
    require(re.fullmatch("sha256:[0-9a-f]{64}", original.get("imageId", "")) is not None
            and re.fullmatch("[0-9a-f]{64}", original.get("envelopeSignatureSha256", "")) is not None,
            "Original image/signature identities required")
    created = datetime.datetime.fromisoformat(original["createdUtc"].replace("Z", "+00:00"))
    expires = datetime.datetime.fromisoformat(original["expiresUtc"].replace("Z", "+00:00"))
    require(created.utcoffset() == datetime.timedelta(0) and expires.utcoffset() == datetime.timedelta(0)
            and created < expires, "Original UTC creation/expiry ordering required")
    return run, cid, database


def main():
    candidate = os.environ.get("MALIEV_SHIPPING_CANDIDATE_HEAD", "")
    require(re.fullmatch("[0-9a-f]{40}", candidate) is not None, "Exact shipping candidate required")
    head = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
    parents = subprocess.check_output(["git", "show", "-s", "--format=%P", "HEAD"], text=True).split()
    require(head == candidate or len(parents) == 2 and parents[1] == candidate,
            "Checkout must be exact candidate or its PR merge second parent")
    require(subprocess.run(["git", "diff", "--quiet", "HEAD", "--"]).returncode == 0,
            "Tracked candidate source and generated assets must remain unchanged")
    files = list(Path("shipping-persistence-test-results").glob("*.trx"))
    require(len(files) == 1, "Exactly one focused shipping TRX required")
    validate_rows(ET.parse(files[0]).getroot())
    directory = Path("Legacy.Maliev.Web.Tests/bin/Release/net10.0/TestResults/shipping-persistence")
    require(sorted(path.name for path in directory.glob("*.json")) == ["en.json", "th.json"],
            "Exactly English and Thai shipping receipts required")
    identities = []
    for culture, width in (("en", 1280), ("th", 375)):
        receipt = json.loads((directory / f"{culture}.json").read_text())
        identities.append(validate_receipt(receipt, candidate, culture, width))
        screenshot = directory / f"{culture}.png"
        require(screenshot.is_file() and screenshot.read_bytes()[:8] == bytes([137,80,78,71,13,10,26,10]),
                "Synthetic reload PNG required")
    require(all(len({identity[index] for identity in identities}) == 2 for index in range(3)),
            "Cultures must use distinct original runs, CIDs and databases")
    print("Verified two real shipping cases, original graph/backend cleanup and exact candidate lineage")


def synthetic_receipt():
    run, cid = "1" * 32, "2" * 64
    original = dict(id=cid, name="/billing-proof-" + run + "-postgres", owner="web-billing-proof", run=run,
                    createdUtc="2026-10-08T00:00:00Z", expiresUtc="2026-10-08T00:30:00Z",
                    configuredImage="postgres:18-alpine", envelopeValid=True, initEnabled=True,
                    memoryBytes=536870912, swapBytes=536870912, nanoCpus=1000000000,
                    tmpfs="/var/lib/postgresql:rw,size=268435456", loopbackPorts=True, persistentData=False,
                    imageId="sha256:" + "3" * 64, envelopeSignatureSha256="4" * 64)
    backend = dict(backend="postgres", localEndpoint="unix:///var/run/docker.sock", databaseSynthetic=True,
                   name="billing-proof-" + run + "-postgres", owner="web-billing-proof", run=run,
                   baselineAbsent=True, startDispatched=True, startupSettled=True, captureVerified=True,
                   absenceVerified=True, sdkReleased=True, originalId=cid, database="profile_contract_" + "5" * 32,
                   daemon="synthetic-daemon", expiresUtc=original["expiresUtc"], original=original)
    graph = dict(owner="web-billing-proof", run=str(uuid.UUID(run)), released=True, retained=False, expired=False,
                 readerFailureRejected=False, expiryFailure=None, backends=[backend],
                 children=[dict(dispatched=True, identityCaptured=True, exitVerified=True, readersClosed=True, released=True) for _ in range(3)],
                 hosts=[dict(startupComplete=True, disposal="RanToCompletion")])
    proof = dict(schema=1, surface="member-shipping", culture="en", candidateHead="a" * 40,
                 owner="web-billing-proof", complete=True, graphReleased=True, expired=False,
                 readerFailureRejected=False, expiryJoined=True, sharedAuthorityExcluded=True,
                 run=run, resources=[backend], graph=graph)
    return dict(surface="member-shipping", culture="en", width=1280, candidateHead="a" * 40,
                catalogStatus=200, saveStatus=302, readbackStatus=200, reloadVerified=True,
                billingPreserved=True, manualDetailPreserved=True, datasetVersion=DATASET,
                shippingBackendCleanup=proof)


class CausalControls(unittest.TestCase):
    def test_valid_receipt(self):
        validate_receipt(synthetic_receipt(), "a" * 40, "en", 1280)

    def test_reject_corrupted_business_and_original_cleanup(self):
        changes = [("surface", "member-billing"), ("candidateHead", "b" * 40), ("saveStatus", 200),
                   ("reloadVerified", False), ("billingPreserved", False), ("manualDetailPreserved", False),
                   ("shippingBackendCleanup.complete", False), ("shippingBackendCleanup.expiryJoined", False),
                   ("shippingBackendCleanup.graph.retained", True), ("shippingBackendCleanup.graph.expired", True),
                   ("shippingBackendCleanup.graph.expiryFailure", "failure"),
                   ("shippingBackendCleanup.graph.children.0.readersClosed", False),
                   ("shippingBackendCleanup.graph.hosts.0.disposal", "WaitingForActivation"),
                   ("shippingBackendCleanup.resources.0.absenceVerified", False),
                   ("shippingBackendCleanup.resources.0.sdkReleased", False),
                   ("shippingBackendCleanup.resources.0.original.initEnabled", False),
                   ("shippingBackendCleanup.resources.0.original.id", "6" * 64)]
        for path, value in changes:
            with self.subTest(path=path):
                receipt = copy.deepcopy(synthetic_receipt())
                target = receipt
                keys = path.split(".")
                for key in keys[:-1]:
                    target = target[int(key)] if isinstance(target, list) else target[key]
                target[keys[-1]] = value
                with self.assertRaises(ValueError):
                    validate_receipt(receipt, "a" * 40, "en", 1280)

    def test_reject_missing_duplicate_foreign_or_failed_rows(self):
        def document(names, outcomes):
            root = ET.Element("{" + NS["t"] + "}TestRun")
            for name, outcome in zip(names, outcomes):
                ET.SubElement(root, "{" + NS["t"] + "}UnitTestResult", testName=name, outcome=outcome)
            ET.SubElement(root, "{" + NS["t"] + "}Counters", **dict(total="2", executed="2", passed="2", failed="0",
                error="0", timeout="0", aborted="0", inconclusive="0", passedButRunAborted="0", notRunnable="0",
                notExecuted="0", disconnected="0", warning="0", completed="0", inProgress="0", pending="0"))
            return root
        names = [f'{METHOD}(culture: "en", width: 1280)', f'{METHOD}(culture: "th", width: 375)']
        validate_rows(document(names, ["Passed", "Passed"]))
        for bad_names, bad_outcomes in [(names[:1], ["Passed"]), ([names[0]] * 2, ["Passed"] * 2),
                                       ([names[0], "foreign"], ["Passed"] * 2), (names, ["Passed", "Failed"]),
                                       (names, ["Passed", "NotExecuted"])]:
            with self.assertRaises(ValueError):
                validate_rows(document(bad_names, bad_outcomes))
        root = document(names, ["Passed", "Passed"])
        root.find("t:Counters", NS).set("pending", "1")
        with self.assertRaises(ValueError):
            validate_rows(root)


if __name__ == "__main__":
    if sys.argv[1:] == ["--self-test"]:
        unittest.main(argv=[sys.argv[0]])
    elif sys.argv[1:]:
        raise SystemExit("Only --self-test is supported")
    else:
        try:
            main()
        except (ValueError, KeyError, TypeError) as failure:
            raise SystemExit(str(failure)) from None
