"""Offline positive/negative controls; no SDK, browser, containers or network."""
import copy
import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch
import subprocess
import tempfile

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
        guard.verify_candidate(guard.tree('HEAD'), guard.tree(guard.CANDIDATE_COMMIT),
                               guard.CANDIDATE_COMMIT, guard.source_tree_identity(guard.CANDIDATE_COMMIT),
                               guard.tree_identity(guard.CANDIDATE_COMMIT))

    def test_git_reader_timeout_propagates_without_accepting_empty_tree(self):
        with patch.object(guard.subprocess, 'check_output', side_effect=subprocess.TimeoutExpired('git', 30)) as reader:
            with self.assertRaises(subprocess.TimeoutExpired):
                guard.tree('HEAD')
            self.assertEqual(reader.call_args.kwargs['timeout'], 30)

    def test_working_tree_check_timeout_propagates_without_acceptance(self):
        current = guard.current_contract()
        candidate = guard.candidate_contract()
        with patch.object(guard, 'tree', side_effect=lambda ref: current if ref == guard.CURRENT_COMMIT else candidate), patch.object(guard, 'tree_identity', side_effect=lambda ref: guard.CURRENT_TREE if ref == guard.CURRENT_COMMIT else guard.CANDIDATE_ASSET_TREE), patch.object(guard, 'source_tree_identity', return_value=guard.CANDIDATE_SOURCE_TREE), patch.object(guard.subprocess, 'run', side_effect=subprocess.TimeoutExpired('git', 30)) as reader:
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
        self.assertEqual('a01c586e3355d9e451b7aa5aa95c9347d82a2014', guard.CURRENT_COMMIT)
        self.assertEqual('72d3d8ebfdfe6e1420baaf0f32b106934f869d34', guard.CURRENT_TREE)
        current = guard.current_contract()
        self.assertEqual(43, len(current))
        self.assertEqual(current, guard.tree(guard.CURRENT_COMMIT))
        self.assertEqual(guard.CURRENT_TREE, guard.tree_identity(guard.CURRENT_COMMIT))
        guard.verify_current(current, current, guard.CURRENT_TREE)
        for obsolete in (self.baseline, self.accepted,
                         guard.tree('2c7562fc8234c08f2f6bfc5556765ebb5cc4ef5d'), {}):
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
        candidate = guard.candidate_contract()
        with patch.object(guard, 'tree', side_effect=lambda ref: current if ref == guard.CURRENT_COMMIT else candidate), patch.object(guard, 'tree_identity', side_effect=lambda ref: guard.CURRENT_TREE if ref == guard.CURRENT_COMMIT else guard.CANDIDATE_ASSET_TREE), patch.object(guard, 'source_tree_identity', return_value=guard.CANDIDATE_SOURCE_TREE), patch.object(guard.subprocess, 'run', side_effect=subprocess.CalledProcessError(1, 'git')):
            with self.assertRaises(subprocess.CalledProcessError): guard.main()

    def reviewed_candidate_inputs(self):
        candidate = guard.candidate_contract()
        return (candidate, guard.tree(guard.CANDIDATE_COMMIT), guard.CANDIDATE_COMMIT,
                guard.source_tree_identity(guard.CANDIDATE_COMMIT),
                guard.tree_identity(guard.CANDIDATE_COMMIT))

    def test_reviewed_candidate_is_exact_integration_with_one_viewer_delta(self):
        candidate, provenance, revision, source_tree, asset_tree = self.reviewed_candidate_inputs()
        guard.verify_candidate(candidate, provenance, revision, source_tree, asset_tree)
        baseline = guard.current_contract()
        self.assertEqual(43, len(candidate))
        self.assertEqual(set(baseline), set(candidate))
        self.assertEqual([guard.ROOT+'/instant-quotation-viewer.mjs'],
                         [name for name in baseline if baseline[name] != candidate[name]])
        self.assertEqual(candidate, guard.tree('HEAD'))

    def test_reviewed_candidate_refuses_old_viewer_and_every_second_delta(self):
        candidate, *provenance = self.reviewed_candidate_inputs()
        baseline = guard.current_contract()
        old_viewer = dict(candidate)
        old_viewer[guard.ROOT+'/instant-quotation-viewer.mjs'] = baseline[guard.ROOT+'/instant-quotation-viewer.mjs']
        with self.assertRaises(ValueError): guard.verify_candidate(old_viewer, *provenance)
        for name in candidate:
            if name == guard.ROOT+'/instant-quotation-viewer.mjs': continue
            changed = dict(candidate); changed[name] = ('100644', 'f'*40)
            with self.subTest(name=name), self.assertRaises(ValueError):
                guard.verify_candidate(changed, *provenance)

    def test_reviewed_candidate_every_content_mode_missing_empty_and_extra_refused(self):
        candidate, *provenance = self.reviewed_candidate_inputs()
        for name, (_, blob) in candidate.items():
            for entry in (('100644', '0'*40), ('100755', blob), ('120000', blob),
                          ('100644', 'e69de29bb2d1d6434b8b29ae775ad8c2e48c5391'), None):
                changed = dict(candidate)
                if entry is None: del changed[name]
                else: changed[name] = entry
                with self.subTest(name=name, entry=entry), self.assertRaises(ValueError):
                    guard.verify_candidate(changed, *provenance)
        changed = dict(candidate, **{guard.ROOT+'/foreign.js': ('100644', 'f'*40)})
        with self.assertRaises(ValueError): guard.verify_candidate(changed, *provenance)

    def test_reviewed_candidate_wrong_commit_full_tree_and_asset_tree_refused(self):
        candidate, provenance, revision, source_tree, asset_tree = self.reviewed_candidate_inputs()
        for wrong in [(provenance, 'f'*40, source_tree, asset_tree),
                      (provenance, revision, 'f'*40, asset_tree),
                      (provenance, revision, source_tree, 'f'*40)]:
            with self.subTest(wrong=wrong[1:]), self.assertRaises(ValueError):
                guard.verify_candidate(candidate, *wrong)

    def test_reviewed_candidate_every_provenance_entry_tampering_refused(self):
        candidate, provenance, revision, source_tree, asset_tree = self.reviewed_candidate_inputs()
        for name, (_, blob) in provenance.items():
            for entry in (('100644', 'f'*40), ('100755', blob), None):
                changed = dict(provenance)
                if entry is None: del changed[name]
                else: changed[name] = entry
                with self.subTest(name=name, entry=entry), self.assertRaises(ValueError):
                    guard.verify_candidate(candidate, changed, revision, source_tree, asset_tree)
        for changed in ({}, guard.current_contract(), dict(provenance, **{guard.ROOT+'/foreign.js': ('100644', 'f'*40)})):
            with self.assertRaises(ValueError):
                guard.verify_candidate(candidate, changed, revision, source_tree, asset_tree)

    def test_reviewed_candidate_fixture_tampering_refused(self):
        with patch.object(guard.Path, 'read_bytes', return_value=b'{}'):
            with self.assertRaises(ValueError): guard.candidate_contract()

    def test_reviewed_candidate_source_tree_timeout_propagates(self):
        with patch.object(guard.subprocess, 'check_output', side_effect=subprocess.TimeoutExpired('git', 30)) as reader:
            with self.assertRaises(subprocess.TimeoutExpired):
                guard.source_tree_identity(guard.CANDIDATE_COMMIT)
            self.assertEqual(reader.call_args.kwargs['timeout'], 30)

    def test_reviewed_candidate_main_reads_only_fixed_revisions_and_head(self):
        candidate, provenance, revision, source_tree, asset_tree = self.reviewed_candidate_inputs()
        current = guard.current_contract()
        def assets(ref):
            self.assertIn(ref, ('HEAD', guard.CURRENT_COMMIT, guard.CANDIDATE_COMMIT))
            return current if ref == guard.CURRENT_COMMIT else candidate
        def identity(ref):
            self.assertIn(ref, (guard.CURRENT_COMMIT, guard.CANDIDATE_COMMIT))
            return guard.CURRENT_TREE if ref == guard.CURRENT_COMMIT else asset_tree
        with patch.object(guard, 'tree', side_effect=assets) as reads, patch.object(guard, 'tree_identity', side_effect=identity), patch.object(guard, 'source_tree_identity', return_value=source_tree), patch.object(guard.subprocess, 'run') as clean, patch.object(guard.subprocess, 'check_output', return_value=b'') as untracked:
            guard.main()
            self.assertEqual(1, sum(call.args == ('HEAD',) for call in reads.call_args_list))
            self.assertIn('HEAD', clean.call_args.args[0])
            self.assertEqual(30, clean.call_args.kwargs['timeout'])
            untracked.assert_called_once_with(['git', 'ls-files', '--others', '--', guard.ROOT], timeout=30)

    def test_reviewed_candidate_untracked_served_asset_refused(self):
        asset_root = Path.cwd()/guard.ROOT
        self.assertTrue(asset_root.resolve().is_relative_to(Path.cwd().resolve()))
        with tempfile.TemporaryDirectory(prefix='fx-untracked-refusal-', dir=asset_root) as directory:
            self.assertTrue(Path(directory).resolve().is_relative_to(asset_root.resolve()))
            foreign = Path(directory)/'synthetic-foreign.js'
            foreign.write_text('/* disposable asset refusal control */\n', encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'Untracked files'):
                guard.main()

    def test_reviewed_candidate_untracked_reader_timeout_propagates(self):
        candidate = guard.candidate_contract()
        current = guard.current_contract()
        with patch.object(guard, 'tree', side_effect=lambda ref: current if ref == guard.CURRENT_COMMIT else candidate), patch.object(guard, 'tree_identity', side_effect=lambda ref: guard.CURRENT_TREE if ref == guard.CURRENT_COMMIT else guard.CANDIDATE_ASSET_TREE), patch.object(guard, 'source_tree_identity', return_value=guard.CANDIDATE_SOURCE_TREE), patch.object(guard.subprocess, 'run'), patch.object(guard.subprocess, 'check_output', side_effect=subprocess.TimeoutExpired('git', 30)) as reader:
            with self.assertRaises(subprocess.TimeoutExpired): guard.main()
            self.assertEqual(reader.call_args.kwargs['timeout'], 30)


if __name__ == '__main__':
    unittest.main()
