"""Keep the source-pin scanner exception bound to one historical line and path."""
import hashlib
import json
from pathlib import Path
import re
import tomllib
import unittest

ROOT = Path(__file__).parents[2]

class SourcePinSecretScanControls(unittest.TestCase):
    def test_exception_is_one_rule_one_path_and_exact_verified_inventory_pin(self):
        config = tomllib.loads((ROOT / ".gitleaks.toml").read_text(encoding="utf-8"))
        self.assertEqual({"useDefault": True}, config["extend"])
        self.assertEqual(3, len(config["allowlists"]))
        rule = config["allowlists"][0]
        self.assertEqual(["generic-api-key"], rule["targetRules"])
        self.assertEqual("AND", rule["condition"])
        self.assertEqual("line", rule["regexTarget"])
        self.assertEqual(1, len(rule["paths"]))
        self.assertEqual(1, len(rule["regexes"]))
        policy = json.loads((ROOT / "scripts/web-account-failure-policy.json").read_text(encoding="utf-8"))
        path = "Legacy.Maliev.Web.Infrastructure/CustomerAuthenticationClient.cs"
        digest = policy["producerPrerequisites"]["contractFiles"][path]
        inventory = {row["path"]: row["sha256"] for row in policy["sourceFiles"]}
        self.assertEqual(inventory[path], digest)
        line = '      "' + path + '": "' + digest + '",'
        self.assertIsNotNone(re.fullmatch(rule["regexes"][0], line))
        self.assertIsNotNone(re.fullmatch(rule["regexes"][0], "\n" + line))
        self.assertIsNotNone(re.fullmatch(rule["paths"][0], "scripts/web-account-failure-policy.json"))
        for other_path in ["scripts/other-policy.json", "prefix/scripts/web-account-failure-policy.json",
                           "scripts/web-account-failure-policy.json.backup"]:
            self.assertIsNone(re.fullmatch(rule["paths"][0], other_path))
        for other_line in [line.replace(digest, hashlib.sha256(b"negative control").hexdigest()),
                           line.replace(path, "OtherAuthenticationClient.cs"), line + ' extra',
                           line.replace('",', '"')]:
            self.assertIsNone(re.fullmatch(rule["regexes"][0], other_line))

    def test_email_session_exceptions_are_exact_verified_source_pins(self):
        config = tomllib.loads((ROOT / ".gitleaks.toml").read_text(encoding="utf-8"))
        policy_path = "scripts/web-email-change-session-policy.json"
        policy = json.loads((ROOT / policy_path).read_text(encoding="utf-8"))
        expected = [
            ("scripts/prepare-member-auth-indexing-proof.ps1", policy["profilePreparationFiles"]),
            ("Legacy.Maliev.Web.Infrastructure/CustomerAuthenticationClient.cs", policy["producerPrerequisites"]["contractFiles"]),
        ]
        inventory = {row["path"]: row["sha256"] for row in policy["sourceFiles"]}
        for rule, (path, pins) in zip(config["allowlists"][1:], expected, strict=True):
            with self.subTest(path=path):
                self.assertEqual(["generic-api-key"], rule["targetRules"])
                self.assertEqual("AND", rule["condition"])
                self.assertEqual("line", rule["regexTarget"])
                self.assertEqual(1, len(rule["paths"]))
                self.assertEqual(1, len(rule["regexes"]))
                digest = pins[path]
                self.assertEqual(inventory[path], digest)
                line = f'      "{path}": "{digest}",'
                for accepted in [line, "\n" + line, "\r\n" + line]:
                    self.assertIsNotNone(re.fullmatch(rule["regexes"][0], accepted))
                self.assertIsNotNone(re.fullmatch(rule["paths"][0], policy_path))
                for rejected in ["scripts/other-policy.json", "prefix/" + policy_path, policy_path + ".backup"]:
                    self.assertIsNone(re.fullmatch(rule["paths"][0], rejected))
                for rejected in [line.replace(digest, hashlib.sha256(b"negative control").hexdigest()),
                                 line.replace(path, "OtherAuthenticationClient.cs"), line + " extra",
                                 '"api_key": "synthetic-unrelated-credential-control"']:
                    self.assertIsNone(re.fullmatch(rule["regexes"][0], rejected))

if __name__ == "__main__": unittest.main()
