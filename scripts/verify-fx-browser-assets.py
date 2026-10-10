"""Preserve frozen FX assets, admitting only exact protected-main PR500/507 deltas."""
import subprocess

BASELINE = '224bcde1d1fc7e915f4d0d0de9b3863bcae4fcff'
ROOT = 'Legacy.Maliev.Web/wwwroot/dist'
# path: (frozen blob, accepted blob, immutable accepted commit)
ACCEPTED = {
    ROOT+'/site.min.css': ('0c666dc5e6ccb728dc9d5c264edadcae37ac9ed4', '2a5e800062834c8874fa1622df740126de21a35b', '5faedcf470b65edd2b3fcefe5e296da0007bcb22'),
    ROOT+'/route-instant-quotation.css': ('c99b6a0251622681206d76bbce3cfd11621fce8c', '2295704db8f9ca14d5f4bc8fc2e3a72af8030209', 'be467d3a4702881a27db78f3ead8d0cb187e908e'),
}


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
    verify(tree(BASELINE), tree('HEAD'), {revision: tree(revision) for _, _, revision in ACCEPTED.values()})
    # Also refuse tracked working changes: the native host must consume committed assets.
    subprocess.run(['git', 'diff', '--exit-code', 'HEAD', '--', ROOT], check=True, timeout=30)
    print('Frozen FX asset tree verified; only exact PR500/507 content deltas admitted')


if __name__ == '__main__':
    main()
