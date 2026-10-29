"""Frozen Git source/assets and one-file postimage custody; no remote branch fallback."""
import argparse,hashlib,json,pathlib,subprocess
def sha(raw):return hashlib.sha256(raw).hexdigest()
def blob(raw):return hashlib.sha1(b'blob '+str(len(raw)).encode()+b'\0'+raw).hexdigest()
def check(source,manifest,patched=False):
    helper=next((x for x in manifest['sourcePins'] if x['path']=='Legacy.Maliev.Web.Tests/MaterialCompletionObservation.cs'),None)
    if helper is None or helper['gitBlob']!='a825ffef5e25885e8b840dd2eae39077ad24b614' or helper['sha256']!='b8b3bbf31b8d050349245f022f0dcc5dfa847d6cb629332d8c92dda551f9ec4e':raise ValueError('Exact remote observation helper must be bound')
    def git(*a):return subprocess.check_output(['git',*a],cwd=source).decode().strip()
    if git('rev-parse','HEAD')!=manifest['base'] or git('rev-parse','HEAD^{tree}')!=manifest['baseTree']:raise ValueError('Exact reviewed base/tree required')
    checked=[]
    for pin in manifest['sourcePins']:
        p=source/pin['path'];raw=p.read_bytes();expected=manifest['postimageSha256'] if patched and pin['path']==manifest['changedPath'] else pin['sha256']
        if sha(raw)!=expected:raise ValueError('Source custody mismatch: '+pin['path'])
        checked.append({'path':pin['path'],'sha256':expected})
    for pin in manifest['assetGitPins']:
        if blob((source/pin['path']).read_bytes())!=pin['sha']:raise ValueError('Committed asset bytes differ: '+pin['path'])
    permitted=[] if not patched else [manifest['changedPath']]
    changed=git('diff','--name-only','HEAD').splitlines()
    if sorted(changed)!=permitted:raise ValueError('Unexpected tracked source delta')
    dependency_paths={'.dependencies/Legacy.Maliev.ServiceDefaults/':manifest['dependencyPins']['ServiceDefaults'],'.dependencies/Legacy.Maliev.CompatibilityContracts/':manifest['dependencyPins']['CompatibilityContracts']}
    untracked=git('ls-files','--others','--exclude-standard').splitlines()
    if any(not any(p==prefix or p.startswith(prefix) for prefix in dependency_paths) for p in untracked):raise ValueError('Unexpected nonignored untracked source/build input')
    for prefix,head in dependency_paths.items():
        dependency=source/prefix
        if not dependency.exists():raise ValueError('Pinned dependency checkout missing')
        observed=subprocess.check_output(['git','rev-parse','HEAD'],cwd=dependency).decode().strip()
        if observed!=head or subprocess.check_output(['git','status','--porcelain'],cwd=dependency).strip():raise ValueError('Dependency checkout head or worktree differs')
    return {'status':'EXACT_FROZEN_CUSTODY_PASS','base':manifest['base'],'tree':manifest['baseTree'],'patched':patched,'sourcePins':checked,'assets':len(manifest['assetGitPins']),'helperBlob':'a825ffef5e25885e8b840dd2eae39077ad24b614','noLocalHelperSubstitution':True}
def main():
    p=argparse.ArgumentParser();p.add_argument('--source',type=pathlib.Path,required=True);p.add_argument('--manifest',type=pathlib.Path,required=True);p.add_argument('--postimage-root',type=pathlib.Path);p.add_argument('--receipt',type=pathlib.Path,required=True);p.add_argument('--apply',action='store_true');p.add_argument('--verify-patched',action='store_true');a=p.parse_args();m=json.loads(a.manifest.read_text(encoding='utf-8'))
    if a.apply and a.verify_patched:raise ValueError('Separate custody modes required')
    r=check(a.source,m,a.verify_patched)
    if a.apply:
        if a.postimage_root is None:raise ValueError('Frozen postimage required')
        raw=(a.postimage_root/m['changedPath']).read_bytes()
        if sha(raw)!=m['postimageSha256']:raise ValueError('Candidate postimage changed')
        target=a.source/m['changedPath'];target.write_bytes(raw);r=check(a.source,m,True)
    a.receipt.parent.mkdir(parents=True,exist_ok=True);a.receipt.write_text(json.dumps(r,indent=2),encoding='utf-8');print(json.dumps({'status':r['status'],'sourcePins':len(r['sourcePins']),'assets':r['assets'],'patched':r['patched']}))
if __name__=='__main__':main()
