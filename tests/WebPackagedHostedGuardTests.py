"""Offline guards; no image, SDK, provider, or container processes are started."""
import importlib.util
from pathlib import Path
import sys
import unittest

sys.dont_write_bytecode = True
path = Path(__file__).resolve().parents[1] / 'scripts/run-web-packaged-hosted.py'
spec = importlib.util.spec_from_file_location('packaged', path)
packaged = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packaged)


class Guards(unittest.TestCase):
    def environment(self):
        return {'GITHUB_ACTIONS': 'true', 'RUNNER_ENVIRONMENT': 'github-hosted',
                'WEB_HEAD': 'a' * 40, 'GITHUB_REPOSITORY': packaged.REPOSITORY}

    def test_local_and_self_hosted_refused(self):
        for environment in ({}, dict(self.environment(), RUNNER_ENVIRONMENT='self-hosted')):
            with self.assertRaises(ValueError):
                packaged.require_hosted(environment)

    def test_non_web_and_unpinned_refused(self):
        for environment in (dict(self.environment(), WEB_HEAD='main'),
                            dict(self.environment(), GITHUB_REPOSITORY='other/repository')):
            with self.assertRaises(ValueError):
                packaged.require_hosted(environment)

    def test_owned_hosted_identity_accepted(self):
        self.assertEqual(packaged.require_hosted(self.environment()), 'a' * 40)

    def test_modified_producer_script_refused(self):
        with self.assertRaises(ValueError):
            packaged.producer_gate({'sha': packaged.PRODUCER_BLOB, 'content': 'cHJpbnQoMSk='})

    def test_scratch_missing_revision_and_not_applicable_receipts_refused(self):
        for receipt in ({}, {'status': 'not-applicable'},
                        {'status': 'accepted', 'sourceRevision': 'b' * 40}):
            with self.assertRaises(ValueError):
                packaged.accepted_receipt(receipt, 'a' * 40)

    def test_corpus_and_immutable_image_required(self):
        receipt = {'status': 'accepted', 'sourceRevision': 'a' * 40,
                   'assetCount': 22, 'fontCount': 12, 'imageId': 'sha256:' + 'b' * 64}
        self.assertEqual(packaged.accepted_receipt(receipt, 'a' * 40), receipt)
        for override in ({'assetCount': 21}, {'fontCount': 0}, {'imageId': 'mutable-tag'}):
            with self.assertRaises(ValueError):
                packaged.accepted_receipt(dict(receipt, **override), 'a' * 40)


if __name__ == '__main__':
    unittest.main()
