const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),assert=require('node:assert/strict');
const overlay=fs.readFileSync(path.join(__dirname,'apply-target-query-overlay.cjs'),'utf8');
// Controlled upstream import-boundary fixture. The complete real pinned tree is
// separately rebuilt by apply-overlay; here drift/repeat/optional-path behavior is isolated.
const before=`#include "kernel-repair-export.hpp"
HierarchyWriter (emscripten::val& meshesArr, MalievKernel::DocumentCoverage& document) :
    MalievKernel::DocumentCoverage& mDocument;
            mDocument.Associate(mesh, meshObj, kernelContext);
static emscripten::val ImportFile (ImporterPtr importer, const emscripten::val& buffer, const ImportParams& params)
    Importer::Result importResult = importer->LoadFile (bufferArr, params);
    HierarchyWriter hierarchyWriter (meshesArr, documentCoverage);
    resultObj.set ("meshes", meshesArr);
emscripten::val ReadStepFile (const emscripten::val& buffer, const emscripten::val& params) {
    return ImportFile (importer, buffer, importParams);
}
emscripten::val ReadIgesFile (const emscripten::val& buffer, const emscripten::val& params) {
    return ImportFile (importer, buffer, importParams);
}
EMSCRIPTEN_BINDINGS (occtimportjs)
{
}`;
function execute(input,revision='c2148e54b456b571238d35cac037d304053d64b2'){
 let text=input;const copies=[];
 const recording={readFileSync(){return text;},writeFileSync(file,value){text=value;},copyFileSync(from,to){copies.push({from,to,bytes:fs.readFileSync(from)});}};
 vm.runInNewContext(overlay,{require:name=>name==='node:fs'?recording:name==='node:child_process'?{execFileSync:()=>revision+'\n'}:require(name),process:{argv:['node','overlay','/isolated-pinned-source']},__dirname},{filename:'actual apply-target-query-overlay.cjs'});return {text,copies};
}
const result=execute(before);assert.equal(result.copies.length,2);for(const row of result.copies)assert.deepEqual(row.bytes,fs.readFileSync(path.join(__dirname,path.basename(row.to))));
assert.equal((result.text.match(/->LoadFile\s*\(/g)||[]).length,1,'exact one native load site remains');
assert.ok(result.text.includes('if (capture) capture->Source(bufferArr);'));
assert.ok(result.text.includes('if (capture) resultObj.set("nativeTargetSession", capture->Finish(provenance));'));
assert.ok(result.text.includes('mTargetCapture->Occurrence(source ? source->KernelShape() : TopoDS_Shape()'));
assert.ok(result.text.includes('ImportFile(importer, buffer, importParams, &capture)'));
assert.equal((result.text.match(/return ImportFile \(importer, buffer, importParams\);/g)||[]).length,2,'old import call sites remain nonretaining');
for(const name of ['ReadStepFileWithTarget','BindNativeTargetSource','QueryNativeTargetBatch','ReleaseNativeTarget'])assert.ok(result.text.includes('function("'+name+'"'));
assert.throws(()=>execute(result.text),/drift\/repeated/);assert.throws(()=>execute(before.replace('mDocument.Associate(mesh, meshObj, kernelContext);','missing')),/drift\/repeated/);assert.throws(()=>execute(before,'other-revision'),/Unexpected upstream/);
// Execute the actual top-level overlay's call-dispatch portion, preserving order.
const base=fs.readFileSync(path.join(__dirname,'apply-overlay.cjs'),'utf8'),start=base.indexOf('child.execFileSync(process.execPath');assert.ok(start>=0);const calls=[];
vm.runInNewContext(base.slice(start),{child:{execFileSync:(node,args)=>calls.push(path.basename(args[0]))},process:{execPath:process.execPath},path,__dirname,source:'/isolated-pinned-source'});
assert.deepEqual(calls,['apply-document-overlay.cjs','apply-repair-overlay.cjs','apply-target-query-overlay.cjs']);
console.log('PASS target overlay: exact optional capture, four API bindings, copied headers, old-call preservation, drift/repeat rejection, final dispatch order');
