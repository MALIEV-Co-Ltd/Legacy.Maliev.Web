const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),crypto=require('node:crypto'),vm=require('node:vm');
const profile=require('./apply-target-query-profile-overlay.cjs');
assert.equal(Object.keys(profile.expected).length,10,'3D specialization and shared generic source must join exact opt-in closure');
const root=process.argv[2];assert.ok(root,'usage: test-target-query-profile-overlay.cjs <pristine composed root>');
const hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const source=Object.fromEntries(Object.keys(profile.expected).map(f=>[f,fs.readFileSync(path.join(root,f),'utf8')]));
const result=profile.instrument(source);
assert.equal(Object.keys(result).length,10);
for(const f of Object.keys(source)) { assert.notEqual(result[f],source[f]);assert.equal(hash(source[f]),profile.expected[f]); }
assert.throws(()=>profile.instrument(result),/source hash/);
for(const f of Object.keys(source)) {
 assert.throws(()=>profile.instrument({...source,[f]:source[f]+'\n'}),/source hash/);
 assert.throws(()=>profile.instrument({...source,[f]:''}),/source hash/);
}
assert.throws(()=>profile.replaceOnce('a a','a','b'),/anchor count/);
assert.throws(()=>profile.replaceOnce('x','a','b'),/anchor count/);
assert.equal(profile.replaceOnce('a','a','b'),'b');
for(const file of ['apply-overlay.cjs','build.ps1'])assert.ok(!fs.readFileSync(path.join(__dirname,file),'utf8').includes('apply-target-query-profile-overlay'));
assert.ok(result['occt-import-js/src/kernel-target-query-session.hpp'].includes('"coordinateSpace","points"}'));
assert.ok(result['occt-import-js/src/js-interface.cpp'].includes('QueryNativeTargetBatchProfile'));
assert.ok(result['occt/src/IntCurvesFace/IntCurvesFace_Intersector.cxx'].includes('../BRepClass3d/kernel-target-query-profile.hpp'));
function actualApply(input,wrongRevision=false){
 const writes=[],sandboxRoot=path.resolve('/profile-pinned-test'),recording={
  readFileSync(file,encoding){const key=path.relative(sandboxRoot,file).split(path.sep).join('/');return key in input?input[key]:fs.readFileSync(file,encoding);},
  writeFileSync(file,value){writes.push({file:path.relative(sandboxRoot,file).split(path.sep).join('/'),bytes:Buffer.from(value)});}
 },module={exports:{}};
 vm.runInNewContext(fs.readFileSync(path.join(__dirname,'apply-target-query-profile-overlay.cjs'),'utf8'),{module,require:name=>name==='node:fs'?recording:name==='node:child_process'?{execFileSync:(command,args)=>{assert.equal(command,'git');assert.deepEqual(Array.from(args.slice(-2)),['rev-parse','HEAD']);return wrongRevision?'wrong':args[1].endsWith('occt-import-js')?'c2148e54b456b571238d35cac037d304053d64b2':'d2abb6d844231cb8f29be6894440874a4700e4a5';}}:require(name),__dirname,console:{log(){}},process:{argv:[]}});
 try{module.exports.apply(sandboxRoot);return writes;}catch(e){assert.equal(writes.length,0,'all guards precede writes');throw e;}
}
const copies=actualApply(source);assert.equal(copies.length,12);assert.equal(new Set(copies.map(x=>x.file)).size,12);
const headers=copies.filter(x=>x.file.endsWith('kernel-target-query-profile.hpp'));assert.equal(headers.length,2);for(const row of headers)assert.deepEqual(row.bytes,fs.readFileSync(path.join(__dirname,'kernel-target-query-profile.hpp')));
for(const row of copies.filter(x=>!x.file.endsWith('kernel-target-query-profile.hpp')))assert.equal(row.bytes.toString(),result[row.file]);
assert.throws(()=>actualApply(source,true),/Unexpected upstream/);assert.throws(()=>actualApply(result),/source hash/);
for(const f of Object.keys(source))assert.throws(()=>actualApply({...source,[f]:source[f]+'\n'}),/source hash/);
// Existing native computations and declared result schema stay present, with
// no changed extrema lifetime or extra native Perform/Load calls.
for(const f of Object.keys(source))for(const token of ['Extrema_ExtPS Ext(P, GA, TolU, TolV);','data->classifier.Perform(p,Precision::Confusion());','data->distance.Perform()','HICS.Perform(','SolidExplorer.Segment(P,L,Par)','SolidExplorer.OtherSegment(P,L,Par)','PointInTheFace (face, APoint'])assert.equal(result[f].split(token).length,source[f].split(token).length,f+' unchanged native invocation '+token);
assert.ok(result['occt/src/BRepClass3d/BRepClass3d_SolidExplorer.cxx'].includes('Extrema_ExtPS Ext(P, GA, TolU, TolV);\n      profileExtrema.Stop();\n      //\n      if (Ext.IsDone()'));
for(const token of ['theBox.IsOut (myL)','Extrema_ExtCC ExtCC(C, myLC, f, l, myLC.FirstParameter(), myLC.LastParameter());','ExtCC.IsDone()','ExtCC.IsParallel()','ExtCC.NbExt()','ExtCC.SquareDistance(i) < EdgeTSq','myEP.Append(EP);','myVP.Append(VP);'])for(const file of ['occt/src/BRepClass3d/BRepClass3d_BndBoxTree.hxx','occt/src/BRepClass3d/BRepClass3d_BndBoxTree.cxx'])assert.equal(result[file].split(token).length,source[file].split(token).length,'unchanged original predicate/result observation '+token);
assert.ok(result['occt/src/BRepClass3d/BRepClass3d_BndBoxTree.cxx'].includes('Extrema_ExtCC ExtCC(C, myLC, f, l, myLC.FirstParameter(), myLC.LastParameter());\n    profileEdgeExtrema.Stop();'));
const ecc='occt/src/Extrema/Extrema_ECC_0.cxx',generic='occt/src/Extrema/Extrema_GenExtCC.gxx';
assert.ok(result[ecc].includes('#define MALIEV_TARGET_PROFILE_ECC3D\n#include <Extrema_GenExtCC.gxx>\n#undef MALIEV_TARGET_PROFILE_ECC3D'));
const twoD=fs.readFileSync(path.join(root,'occt/src/Extrema/Extrema_ECC2d_0.cxx'),'utf8');assert.equal(hash(twoD),'2471dc78fdf78a9f19cc3b9209af8c447780d20bd4231dac0214e1808a308696');assert.ok(!twoD.includes('MALIEV_TARGET_PROFILE_ECC3D'));
const noHooks=result[generic].replace(/#ifdef MALIEV_TARGET_PROFILE_ECC3D\n[^]*?\n#endif\n/g,'');
const nonempty=text=>text.split(/\r?\n/).filter(line=>line.trim()).join('\n');assert.equal(nonempty(noHooks),nonempty(source[generic]),'marker-absent shared source preserves every original nonempty line');
for(const token of ['anL[0] = GCPnts_AbscissaPoint::Length(C1);','anL[1] = GCPnts_AbscissaPoint::Length(C2);','aFinder.SetLocalParams(aFirstBorderInterval, aSecondBorderInterval);','aFinder.Perform(GetSingleSolutionFlag());'])assert.equal(result[generic].split(token).length,source[generic].split(token).length);
assert.ok(result[generic].includes('aFinder.SetLocalParams(aFirstBorderInterval, aSecondBorderInterval);\n#ifdef MALIEV_TARGET_PROFILE_ECC3D'));
console.log('PASS profile overlay: ten exact targets, 3D-only guarded generic hooks, unchanged 2D statements, hash drift/repeat, missing/duplicate anchors, unchanged default dispatch and strict schema');
