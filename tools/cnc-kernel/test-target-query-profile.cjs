// Diagnostic-only baseline/profile comparison. No provider grant or quote path.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),crypto=require('node:crypto'),vm=require('node:vm');
const hash=x=>crypto.createHash('sha256').update(x).digest('hex');
// Execute the unchanged existing import-comparison function, including its
// enumerated timing/invocation exclusions and complete semantic-ledger checks.
const original=fs.readFileSync(path.join(__dirname,'test-target-query.cjs'),'utf8'),a=original.indexOf('function deterministic(imported){'),b=original.indexOf('\n(async()=>{',a);assert.ok(a>=0&&b>a);
const deterministic=vm.runInNewContext('('+original.slice(a,b).trim()+')',{assert,structuredClone});
function semantic(result){const value=structuredClone(result);if('elapsedMs'in value){assert.ok(Number.isFinite(value.elapsedMs)&&value.elapsedMs>=0);delete value.elapsedMs;}return value;}
function checkProfile(p,request,result){
 assert.equal(p.contract,'NativeTargetQueryProfile.v1');assert.equal(p.status,'available');assert.equal(p.diagnosticOnly,true);
 assert.equal(p.hashAuthority,'caller-diagnostic-label-independently-verified-by-harness');assert.equal(p.diagnosticRequestHash,hash(JSON.stringify(request)));
 for(const key of ['sourceGeneration','sourceBytesHash','nativeImportRevision','topologyRevision'])assert.equal(p[key],request.binding[key]);
 for(const key of ['sessionId','batchId','mode'])assert.equal(p[key],request[key]);
 assert.equal(p.requestedPoints,request.points.length);assert.equal(p.counters.points,result.evaluatedPointCount);assert.equal(p.overflow,false);assert.equal(p.truncated,false);
 assert.ok(Buffer.byteLength(JSON.stringify(p))<32768);
 let sum=0;for(const stats of Object.values(p.phases)){for(const n of Object.values(stats))assert.ok(Number.isSafeInteger(n)&&n>=0);assert.ok(stats.exclusiveNs<=stats.inclusiveNs);assert.ok(stats.maxNs<=stats.inclusiveNs);sum+=stats.exclusiveNs;}
 assert.equal(sum,p.phases.batch.inclusiveNs,'exclusive tree allocation equals measured batch');assert.equal(p.phases.query.calls,result.evaluatedPointCount);
 if(request.mode==='membership')assert.equal(p.phases.distance.calls,0);
 const states={IN:0,ON:0,OUT:0,UNKNOWN:0};for(const row of result.points)if(row.membership.status!=='not-requested')++states[row.membership.state];
 for(const [name,n]of Object.entries(states))assert.equal(p.counters[name],n);
 const c=p.counters,ph=p.phases;
 assert.equal(c.lineBoxes,ph.lineBoxReject.calls);assert.ok(c.lineBoxesRejected<=c.lineBoxes);
 assert.equal(c.lineEdgeCandidates,ph.lineEdgePrepare.calls);assert.ok(ph.lineEdgeExtrema.calls<=ph.lineEdgePrepare.calls);
 assert.equal(c.lineVertexCandidates,ph.lineVertex.calls);
 assert.ok(c.lineExtremaDone+c.lineExtremaNotDone<=ph.lineEdgeExtrema.calls);
 if(!ph.lineEdgeExtrema.unwinds)assert.equal(c.lineExtremaDone+c.lineExtremaNotDone,ph.lineEdgeExtrema.calls);
 assert.ok(c.lineExtremaParallel<=c.lineExtremaDone);assert.ok(c.lineAcceptedEdges<=c.lineEdgeCandidates);
 assert.ok(c.lineNearEdgeExtrema>=c.lineAcceptedEdges);assert.ok(c.lineNbExt>=c.lineNearEdgeExtrema);
 assert.equal(p.supportBuckets[3].reduce((n,s)=>n+s.calls,0),ph.lineEdgeExtrema.calls,'actual curve-type attribution covers every ExtCC constructor');
 assert.equal(Object.keys(ph).length,28);assert.equal(p.supportBuckets.length,6);
 assert.ok(ph.genericEccPrepare.calls<=ph.genericEcc3d.calls);
 assert.ok(ph.genericEccLength1.calls<=ph.genericEccPrepare.calls);assert.ok(ph.genericEccLength2.calls<=ph.genericEccLength1.calls);
 if(!ph.genericEccLength1.unwinds)assert.equal(ph.genericEccLength1.calls,ph.genericEccLength2.calls);
 assert.equal(p.supportBuckets[4].reduce((n,s)=>n+s.calls,0),ph.genericEccLength1.calls);
 assert.equal(p.supportBuckets[5].reduce((n,s)=>n+s.calls,0),ph.genericEccLength2.calls);
}
async function main(){
 const [baselinePath,profilePath,sourcePath,manifestPath,resultPath]=process.argv.slice(2).map(p=>path.resolve(p));
 const manifestBytes=fs.readFileSync(manifestPath),manifest=JSON.parse(manifestBytes),bytes=fs.readFileSync(sourcePath);assert.equal(hash(bytes),manifest.sourceSha256);assert.ok(manifest.batches.length>0&&manifest.batches.length<=16);
 assert.ok(!fs.existsSync(resultPath),'write-once result directory');fs.mkdirSync(resultPath,{recursive:true});const receipts=[];
 const save=(name,data)=>{const raw=JSON.stringify(data)+'\n';fs.writeFileSync(path.join(resultPath,name+'.json'),raw,{flag:'wx'});return hash(raw);};
 const all=[];
 for(const [kind,entry]of [['baseline',baselinePath],['profile',profilePath]]){
  const runtimeHash=hash(fs.readFileSync(entry)),wasmHash=hash(fs.readFileSync(path.join(path.dirname(entry),'occt-import-js.wasm')));
  assert.equal(runtimeHash,manifest.runtimes[kind].js);assert.equal(wasmHash,manifest.runtimes[kind].wasm);
  const api=await require(entry)({locateFile:n=>path.join(path.dirname(entry),n)});assert.equal(typeof api.QueryNativeTargetBatchProfile,kind==='profile'?'function':'undefined');assert.equal(typeof api.ReadLastNativeTargetQueryProfile,kind==='profile'?'function':'undefined');
  const start=performance.now(),imported=api.ReadStepFileWithTarget(bytes,{linearUnit:'millimeter',repairErrorBudgetMm:.01,repairDiagnostics:true}),parseMs=performance.now()-start;
  save(kind+'-import',imported);assert.equal(imported.success,true);assert.equal(imported.nativeTargetSession.status,'available');const id=imported.nativeTargetSession.sessionId,binding=manifest.binding;
  assert.equal(binding.sourceBytesHash,hash(bytes));assert.equal(api.BindNativeTargetSource(id,bytes,binding).byteEqualityVerified,true);
  const outputs=[],importHash=hash(JSON.stringify(imported));
  try {
   for(const batch of manifest.batches){
    assert.equal(hash(JSON.stringify(batch.points)),batch.pointsSha256);assert.ok(batch.points.length>0&&batch.points.length<=256);
    const request={contract:'NativeTargetQueryBatch.v1',sessionId:id,binding,batchId:batch.batchId,policyVersion:'native-target-numerical-v1',mode:batch.mode,coordinateSpace:'import-world-mm',points:batch.points};
    const requestHash=hash(JSON.stringify(request));save(kind+'-'+batch.batchId+'-request',request);
    const before=performance.now(),result=kind==='profile'?api.QueryNativeTargetBatchProfile(request,requestHash):api.QueryNativeTargetBatch(request),wallMs=performance.now()-before;
    const outputHash=save(kind+'-'+batch.batchId+'-result',result);assert.equal(result.evaluatedPointCount,batch.points.length);assert.deepEqual(result.points.map(x=>x.pointId),batch.points.map(x=>x.pointId));
    assert.deepEqual(result.points.map(x=>x.point),batch.points.map(x=>x.point));let profile=null;
    if(kind==='profile'){profile=api.ReadLastNativeTargetQueryProfile();save(kind+'-'+batch.batchId+'-profile',profile);checkProfile(profile,request,result);}
    if(batch.expected)for(let i=0;i<batch.expected.length;++i){const expected=batch.expected[i],row=result.points[i];if(expected.state){if(row.membership.status==='available')assert.equal(row.membership.state,expected.state);else{assert.equal(expected.state,'OUT');assert.equal(row.membership.reason,'classifier_out_completion_unobservable');assert.equal(row.membership.rawNativeState,'OUT');}}
     if(expected.distanceMm!==undefined){assert.equal(row.boundaryDistance.status,'available');assert.ok(Math.abs(row.boundaryDistance.valueMm-expected.distanceMm)<1e-6);}}
    const receipt={kind,batch:batch.batchId,points:batch.points.length,requestHash,outputHash,wallMs,nativeElapsedMs:result.elapsedMs,profileBatchMs:profile?profile.phases.batch.inclusiveNs/1e6:null,states:result.points.reduce((m,p)=>(m[p.membership.state]=(m[p.membership.state]||0)+1,m),{}),rssBytes:process.memoryUsage().rss};receipts.push(receipt);outputs.push(result);console.log('PROFILE BATCH '+JSON.stringify(receipt));
   }
   assert.equal(hash(JSON.stringify(imported)),importHash,'queries preserve existing imported output');
   const sample={contract:'NativeTargetQueryBatch.v1',sessionId:id,binding,batchId:'negative',policyVersion:'native-target-numerical-v1',mode:'membership',coordinateSpace:'import-world-mm',points:manifest.batches[0].points.slice(0,1)};
   for(const mutate of [x=>x.binding={...binding,topologyRevision:'wrong'},x=>x.diagnosticRequestHash='not-an-ordinary-field',x=>x.points=[],x=>x.points[0]={pointId:'bad',point:[NaN,0,0]}]){
    const request=structuredClone(sample);mutate(request);const ordinary=api.QueryNativeTargetBatch(request),observed=kind==='profile'?api.QueryNativeTargetBatchProfile(request,hash(JSON.stringify(request))):ordinary;assert.deepEqual(observed,ordinary);assert.equal(observed.status,'unavailable');if(kind==='profile')assert.equal(api.ReadLastNativeTargetQueryProfile().status,'unavailable');
   }
   if(kind==='profile'){
    // Invalid label cannot alter ordinary native evaluation, only attribution.
    const ordinary=api.QueryNativeTargetBatch(sample),invalid=api.QueryNativeTargetBatchProfile(sample,'bad-label');assert.deepEqual(semantic(invalid),semantic(ordinary));assert.equal(api.ReadLastNativeTargetQueryProfile().status,'unavailable');
   }
  }finally{assert.equal(api.ReleaseNativeTarget(id).released,true);assert.equal(api.ReleaseNativeTarget(id).released,false);}
  all.push({kind,imported,outputs});receipts.push({kind,parseMs,imports:1,runtimeHash,wasmHash});
 }
 assert.deepEqual(deterministic(all[0].imported),deterministic(all[1].imported),'all deterministic existing import fields');
 for(let i=0;i<manifest.batches.length;++i)assert.deepEqual(semantic(all[0].outputs[i]),semantic(all[1].outputs[i]),manifest.batches[i].batchId+' complete ordinary query equality except elapsedMs');
 save('comparison',{status:'passed',sourceSha256:hash(bytes),manifestSha256:hash(manifestBytes),importsPerRuntime:1,batches:manifest.batches.length,exclusions:['Query.elapsedMs','existing import timing diagnostics and monotonic invocation ID explicitly validated by test-target-query.cjs deterministic()'],receipts});
 console.log('PASS baseline/profile deterministic semantics, correlated bounded sidecars and source/binding/lifetime negatives');
}
main().catch(e=>{console.error(e);process.exitCode=1;});
