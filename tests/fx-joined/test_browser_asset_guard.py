"""Offline positive/negative controls; no SDK, browser, containers or network."""
import copy
import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch
import subprocess

path = Path(__file__).resolve().parents[2]/'scripts/verify-fx-browser-assets.py'
spec = importlib.util.spec_from_file_location('asset_guard', path)
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)


class AssetGuardControls(unittest.TestCase):
    def setUp(self):
        self.baseline = guard.tree(guard.BASELINE)
        self.provenance = {revision: guard.tree(revision) for _, _, revision in guard.ACCEPTED.values()}
        self.accepted = copy.deepcopy(self.baseline)
        for name, (_, blob, _) in guard.ACCEPTED.items():
            self.accepted[name] = ('100644', blob)

    def test_frozen_original_and_both_exact_accepted_deltas(self):
        guard.verify(self.baseline, self.baseline, self.provenance)
        guard.verify(self.baseline, self.accepted, self.provenance)

    def test_actual_current_committed_tree(self):
        guard.verify_current(guard.tree('HEAD'), guard.tree(guard.CURRENT_COMMIT),
                             guard.tree_identity(guard.CURRENT_COMMIT))

    def test_git_reader_timeout_propagates_without_accepting_empty_tree(self):
        with patch.object(guard.subprocess, 'check_output', side_effect=subprocess.TimeoutExpired('git', 30)) as reader:
            with self.assertRaises(subprocess.TimeoutExpired):
                guard.tree('HEAD')
            self.assertEqual(reader.call_args.kwargs['timeout'], 30)

    def test_working_tree_check_timeout_propagates_without_acceptance(self):
        current = guard.current_contract()
        with patch.object(guard, 'tree', return_value=current), patch.object(guard, 'tree_identity', return_value=guard.CURRENT_TREE), patch.object(guard.subprocess, 'run', side_effect=subprocess.TimeoutExpired('git', 30)) as reader:
            with self.assertRaises(subprocess.TimeoutExpired):
                guard.main()
            self.assertEqual(reader.call_args.kwargs['timeout'], 30)

    def test_each_exact_delta_independently(self):
        for name in guard.ACCEPTED:
            candidate = copy.deepcopy(self.baseline)
            candidate[name] = self.accepted[name]
            guard.verify(self.baseline, candidate, self.provenance)

    def test_mutations_modes_additions_and_deletions_rejected(self):
        for name in self.baseline:
            for entry in [('100644', '0'*40), ('100755', self.accepted[name][1])]:
                candidate = copy.deepcopy(self.accepted); candidate[name] = entry
                with self.subTest(name=name, entry=entry), self.assertRaises(ValueError):
                    guard.verify(self.baseline, candidate, self.provenance)
        for add in [True, False]:
            candidate = copy.deepcopy(self.accepted)
            if add: candidate[guard.ROOT+'/foreign.css'] = ('100644', 'a'*40)
            else: del candidate[next(iter(candidate))]
            with self.assertRaises(ValueError): guard.verify(self.baseline, candidate, self.provenance)

    def test_baseline_and_accepted_commit_provenance_mutations_rejected(self):
        for name, (_, _, revision) in guard.ACCEPTED.items():
            baseline = copy.deepcopy(self.baseline); baseline[name] = ('100644', 'f'*40)
            with self.assertRaises(ValueError): guard.verify(baseline, self.accepted, self.provenance)
            provenance = copy.deepcopy(self.provenance); provenance[revision][name] = ('100644', 'f'*40)
            with self.assertRaises(ValueError): guard.verify(self.baseline, self.accepted, provenance)

    def test_current_contract_requires_exact_complete_accepted_snapshot(self):
        current = guard.current_contract()
        self.assertEqual(43, len(current))
        self.assertEqual(current, guard.tree(guard.CURRENT_COMMIT))
        self.assertEqual(guard.CURRENT_TREE, guard.tree_identity(guard.CURRENT_COMMIT))
        guard.verify_current(current, current, guard.CURRENT_TREE)
        for obsolete in (self.baseline, self.accepted, {}):
            with self.subTest(obsolete=len(obsolete)), self.assertRaises(ValueError):
                guard.verify_current(obsolete, current, guard.CURRENT_TREE)

    def test_every_current_content_mode_missing_and_empty_blob_refused(self):
        current = guard.current_contract()
        for name, (mode, blob) in current.items():
            for entry in (('100644', '0'*40), ('100755', blob), ('120000', blob),
                          ('100644', 'e69de29bb2d1d6434b8b29ae775ad8c2e48c5391'), None):
                candidate = dict(current)
                if entry is None: del candidate[name]
                else: candidate[name] = entry
                with self.subTest(name=name, entry=entry), self.assertRaises(ValueError):
                    guard.verify_current(candidate, current, guard.CURRENT_TREE)

    def test_current_unknown_addition_and_every_partial_transition_refused(self):
        current = guard.current_contract()
        extra = dict(current, **{guard.ROOT+'/foreign.js': ('100644', 'a'*40)})
        with self.assertRaises(ValueError): guard.verify_current(extra, current, guard.CURRENT_TREE)
        for name, entry in current.items():
            if self.accepted.get(name) == entry: continue
            candidate = dict(current)
            if name in self.accepted: candidate[name] = self.accepted[name]
            else: del candidate[name]
            with self.subTest(name=name), self.assertRaises(ValueError):
                guard.verify_current(candidate, current, guard.CURRENT_TREE)

    def test_current_provenance_tree_and_each_entry_tampering_refused(self):
        current = guard.current_contract()
        with self.assertRaises(ValueError): guard.verify_current(current, current, 'f'*40)
        extra = dict(current, **{guard.ROOT+'/foreign.js': ('100644', 'a'*40)})
        for provenance in ({}, self.baseline, self.accepted, extra):
            with self.subTest(provenance=len(provenance)), self.assertRaises(ValueError):
                guard.verify_current(current, provenance, guard.CURRENT_TREE)
        for name, (_, blob) in current.items():
            for entry in (('100644', 'f'*40), ('100755', blob), None):
                provenance = dict(current)
                if entry is None: del provenance[name]
                else: provenance[name] = entry
                with self.subTest(name=name, entry=entry), self.assertRaises(ValueError):
                    guard.verify_current(provenance, provenance, guard.CURRENT_TREE)

    def test_current_fixture_tampering_refused(self):
        with patch.object(guard.Path, 'read_bytes', return_value=b'{}'):
            with self.assertRaises(ValueError): guard.current_contract()

    def test_current_tree_identity_timeout_propagates(self):
        with patch.object(guard.subprocess, 'check_output', side_effect=subprocess.TimeoutExpired('git', 30)) as reader:
            with self.assertRaises(subprocess.TimeoutExpired): guard.tree_identity(guard.CURRENT_COMMIT)
            self.assertEqual(reader.call_args.kwargs['timeout'], 30)

    def test_tracked_working_change_failure_propagates(self):
        current = guard.current_contract()
        with patch.object(guard, 'tree', return_value=current), patch.object(guard, 'tree_identity', return_value=guard.CURRENT_TREE), patch.object(guard.subprocess, 'run', side_effect=subprocess.CalledProcessError(1, 'git')):
            with self.assertRaises(subprocess.CalledProcessError): guard.main()


if __name__ == '__main__':
    unittest.main()
