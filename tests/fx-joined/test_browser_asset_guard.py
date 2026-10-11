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
        guard.verify(self.baseline, guard.tree('HEAD'), self.provenance)

    def test_git_reader_timeout_propagates_without_accepting_empty_tree(self):
        with patch.object(guard.subprocess, 'check_output', side_effect=subprocess.TimeoutExpired('git', 30)) as reader:
            with self.assertRaises(subprocess.TimeoutExpired):
                guard.tree('HEAD')
            self.assertEqual(reader.call_args.kwargs['timeout'], 30)

    def test_working_tree_check_timeout_propagates_without_acceptance(self):
        with patch.object(guard, 'tree', side_effect=lambda revision: self.accepted if revision == 'HEAD' else self.baseline if revision == guard.BASELINE else self.provenance[revision]), patch.object(guard.subprocess, 'run', side_effect=subprocess.TimeoutExpired('git', 30)) as reader:
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


if __name__ == '__main__':
    unittest.main()
