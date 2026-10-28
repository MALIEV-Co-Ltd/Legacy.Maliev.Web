"""Require the exact current accepted FX assets; retain frozen historical controls."""
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
CURRENT_COMMIT = '2c7562fc8234c08f2f6bfc5556765ebb5cc4ef5d'
CURRENT_TREE = '8ed927a3772af7c9730597e1b51b92c1320802c7'
CURRENT_FIXTURE_SHA256 = '4c18336ffdf81cf62a5547e04a43011078d829cd70cb533e50875568ef99cdb0'


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
    verify_current(tree('HEAD'), tree(CURRENT_COMMIT), tree_identity(CURRENT_COMMIT))
    # Also refuse tracked working changes: the native host must consume committed assets.
    subprocess.run(['git', 'diff', '--exit-code', 'HEAD', '--', ROOT], check=True, timeout=30)
    print('Current accepted FX asset tree verified; all 43 exact committed assets required')


if __name__ == '__main__':
    main()
