"""Pure source policy checks; never writes /etc or invokes apt/SDK/browser."""
import importlib.util
from pathlib import Path
import unittest

path = Path(__file__).resolve().parents[1] / "scripts/configure-hosted-ubuntu-browser-sources.py"
spec = importlib.util.spec_from_file_location("hosted_browser_sources", path)
policy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(policy)

def stanza(suites):
    return ("Types: deb\nURIs: " + policy.MIRROR + "\nSuites: " + suites
            + "\nComponents: main restricted universe multiverse\nSigned-By: " + policy.KEYRING + "\n")

class SourcePolicyTests(unittest.TestCase):
    def test_combined_suites_split_only_security_uri(self):
        result = policy.configure(stanza("noble noble-updates noble-backports noble-security"))
        self.assertEqual(stanza("noble noble-updates noble-backports") + "\n"
                         + stanza("noble-security").replace(policy.MIRROR, policy.SECURITY), result)

    def test_separate_security_preserves_other_stanza_and_comments(self):
        main = "# owned runner source\n" + stanza("noble noble-updates noble-backports")
        result = policy.configure(main + "\n" + stanza("noble-security"))
        self.assertEqual(main + "\n" + stanza("noble-security").replace(policy.MIRROR, policy.SECURITY), result)
        for header, separator, footer in [("# runner header\n\n", "\n", ""),
                                          ("# first header\n\n# second header\n\n", "\n# pocket note\n\n", "\n# trailing note\n")]:
            with self.subTest(header=header, separator=separator, footer=footer):
                source = header + main + separator + stanza("noble-security") + footer
                expected = header + main + separator + stanza("noble-security").replace(policy.MIRROR, policy.SECURITY) + footer
                self.assertEqual(expected, policy.configure(source))

    def test_unexpected_or_mixed_sources_fail_closed(self):
        valid = stanza("noble noble-updates noble-backports noble-security")
        invalid = [valid.replace(policy.MIRROR, "http://untrusted.test/ubuntu"),
                   valid.replace(policy.KEYRING, "/tmp/untrusted.gpg"),
                   valid + "Trusted: yes\n", valid + "URIs: " + policy.MIRROR + "\n",
                   valid.replace("noble-security", "noble-proposed"),
                   valid.replace("noble-security", "noble-security noble-security"),
                   valid.replace("main restricted universe multiverse", "main"),
                   stanza("noble noble-security") + "\n" + stanza("noble-updates noble-backports"),
                   stanza("noble noble-updates noble-backports"),
                   "# comments only\n\n# still no source\n",
                   "# header\n\n" + valid + "\n" + valid + "\n" + valid,
                   "# header\n\n" + valid + "\nUnknown: field\n"]
        for source in invalid:
            with self.subTest(source=source), self.assertRaises(ValueError):
                policy.configure(source)

    def test_no_http_or_signature_bypass_added(self):
        result = policy.configure(stanza("noble noble-updates noble-backports noble-security"))
        self.assertNotIn("http://", result)
        self.assertEqual(2, result.count("Signed-By: " + policy.KEYRING))
        self.assertNotIn("Trusted:", result)
        self.assertNotIn("Check-Valid-Until:", result)

if __name__ == "__main__":
    unittest.main(verbosity=2)
