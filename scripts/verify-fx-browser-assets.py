"""Require the reviewed Printing asset candidate; retain accepted historical controls."""
import hashlib
import json
from pathlib import Path
import subprocess

BASELINE = '224bcde1d1fc7e915f4d0d0de9b3863bcae4fcff'
ROOT = 'Legacy.Maliev.Web/wwwroot/dist'
# path: (frozen blob, accepted blob, immutable accepted commit)
ACCEPTED = {
    ROOT+'/site.min.css': ('0c666dc5e6ccb728dc9d5c264edadcae37ac9ed4', '2a5e800062834c8874fa1622df740126de21a35b', '5faedcf470b65edd2b3fcefe5e296da0007bcb22'),
    ROOT+'/route-instant-quotation.css': ('c99b6a0251622681206d76bbce3cfd11621fce8c', '2295704db8f9ca14d5f4bc8fc2e3a72af8030209', 'be467d3a4702881a27db78f3ead8d0cb187e908e'),
}
CURRENT_COMMIT = 'a01c586e3355d9e451b7aa5aa95c9347d82a2014'
CURRENT_TREE = '72d3d8ebfdfe6e1420baaf0f32b106934f869d34'
CURRENT_FIXTURE_SHA256 = '891af1aeffe157c624e3621c269628585c5f9a6ce165596b33d66f2c96ac3c0d'
CANDIDATE_COMMIT = '86cb00d864177685f7c8020f878488ef039e8818'
CANDIDATE_SOURCE_TREE = 'eb565fbdd476ed99ef24ea23f52c3cee88b2e3fb'
CANDIDATE_ASSET_TREE = '3cb5ffacfccf3c3a0d026ff1872b44001f4d4e36'
CANDIDATE_FIXTURE_SHA256 = '465b7973ff68980ef524b695eef323e89b48137e52504fec617d88c6326e119d'
CANDIDATE_VIEWER_BLOB = '78eb0f20ce88105e297f103cde7a5b06e32cb564'


def current_contract():
    content = (Path(__file__).resolve().parents[1]/'tests/fx-joined/current-accepted-assets.json').read_bytes()
    if hashlib.sha256(content).hexdigest() != CURRENT_FIXTURE_SHA256:
        raise ValueError('Current accepted asset fixture identity mismatch')
    contract = json.loads(content)
    if contract['acceptedCommit'] != CURRENT_COMMIT or contract['assetTree'] != CURRENT_TREE:
        raise ValueError('Current accepted asset provenance mismatch')
    return {path: tuple(entry) for path, entry in contract['assets'].items()}


def tree_identity(revision):
    return subprocess.check_output(['git', 'rev-parse', revision+':'+ROOT], timeout=30).decode('ascii').strip()


def verify_current(candidate, provenance, provenance_tree):
    expected = current_contract()
    if provenance_tree != CURRENT_TREE or provenance != expected:
        raise ValueError('Current accepted committed asset provenance mismatch')
    if candidate != expected:
        raise ValueError('Complete exact current asset paths, modes and blobs required')


def candidate_contract():
    content = (Path(__file__).resolve().parents[1]/'tests/fx-joined/reviewed-candidate-printing758-assets.json').read_bytes()
    if hashlib.sha256(content).hexdigest() != CANDIDATE_FIXTURE_SHA256:
        raise ValueError('Reviewed candidate asset fixture identity mismatch')
    contract = json.loads(content)
    metadata = {
        'schemaVersion': 1,
        'scope': 'reviewed-candidate-only',
        'baselineAcceptedCommit': CURRENT_COMMIT,
        'baselineAssetTree': CURRENT_TREE,
        'featureIntegrationCommit': CANDIDATE_COMMIT,
        'featureIntegrationTree': CANDIDATE_SOURCE_TREE,
        'assetTree': CANDIDATE_ASSET_TREE,
        'onlyChangedPath': ROOT+'/instant-quotation-viewer.mjs',
    }
    if set(contract) != set(metadata) | {'assets'} or any(contract[key] != value for key, value in metadata.items()):
        raise ValueError('Reviewed candidate source provenance mismatch')
    assets = {path: tuple(entry) for path, entry in contract['assets'].items()}
    expected = current_contract()
    expected[ROOT+'/instant-quotation-viewer.mjs'] = ('100644', CANDIDATE_VIEWER_BLOB)
    if len(assets) != 43 or assets != expected:
        raise ValueError('Only the exact reviewed Printing viewer delta is permitted')
    return assets


def source_tree_identity(revision):
    return subprocess.check_output(['git', 'rev-parse', revision+'^{tree}'], timeout=30).decode('ascii').strip()


def verify_candidate(candidate, provenance, revision, source_tree, asset_tree):
    expected = candidate_contract()
    if revision != CANDIDATE_COMMIT or source_tree != CANDIDATE_SOURCE_TREE or asset_tree != CANDIDATE_ASSET_TREE:
        raise ValueError('Reviewed integration commit and source/asset trees required')
    if provenance != expected:
        raise ValueError('Reviewed integration asset provenance mismatch')
    if candidate != expected:
        raise ValueError('Complete exact reviewed candidate paths, modes and blobs required')


def tree(revision):
    raw = subprocess.check_output(['git', 'ls-tree', '-r', '-z', revision, '--', ROOT], timeout=30)
    result = {}
    for entry in raw.split(b'\0'):
        if not entry:
            continue
        metadata, path = entry.decode('utf-8').split('\t', 1)
        mode, kind, identity = metadata.split()
        if kind != 'blob':
            raise ValueError('Only committed asset blobs permitted')
        result[path] = (mode, identity)
    if not result:
        raise ValueError('Complete committed asset tree required')
    return result


def verify(baseline, candidate, provenance):
    if set(baseline) != set(candidate):
        raise ValueError('Asset additions/deletions refused')
    for path, (original, approved, revision) in ACCEPTED.items():
        if baseline.get(path) != ('100644', original):
            raise ValueError('Frozen original asset identity changed')
        if provenance.get(revision, {}).get(path) != ('100644', approved):
            raise ValueError('Accepted commit provenance mismatch')
    for path, entry in candidate.items():
        if entry == baseline[path]:
            continue
        approved = ACCEPTED.get(path)
        if approved is None or entry != ('100644', approved[1]):
            raise ValueError('Unapproved asset content or mode: '+path)


def main():
    verify_current(current_contract(), tree(CURRENT_COMMIT), tree_identity(CURRENT_COMMIT))
    verify_candidate(tree('HEAD'), tree(CANDIDATE_COMMIT), CANDIDATE_COMMIT,
                     source_tree_identity(CANDIDATE_COMMIT), tree_identity(CANDIDATE_COMMIT))
    # Also refuse tracked working changes: the native host must consume committed assets.
    subprocess.run(['git', 'diff', '--exit-code', 'HEAD', '--', ROOT], check=True, timeout=30)
    # Untracked files, including ignored files, are also served from this directory.
    if subprocess.check_output(['git', 'ls-files', '--others', '--', ROOT], timeout=30):
        raise ValueError('Untracked files under served assets refused')
    print('Reviewed candidate FX asset tree verified; all 43 exact committed assets required')


if __name__ == '__main__':
    main()
