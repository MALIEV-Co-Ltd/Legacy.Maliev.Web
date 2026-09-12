const assert=require('node:assert/strict'),path=require('node:path'),fs=require('node:fs'),crypto=require('node:crypto');
const hash=x=>crypto.createHash('sha256').update(x).digest('hex'),clone=x=>JSON.parse(JSON.stringify(x));
const placed=p=>{const a=[1,2,3].map(x=>x/Math.sqrt(14)),c=Math.cos(.713),s=Math.sin(.713),dot=a.reduce((n,x,i)=>n+x*p[i],0),cross=[a[1]*p[2]-a[2]*p[1],a[2]*p[0]-a[0]*p[2],a[0]*p[1]-a[1]*p[0]];return p.map((x,i)=>c*x+s*cross[i]+(1-c)*dot*a[i]+[17,-23,41][i]);};
function deterministic(imported){
 const value=structuredClone(imported);delete value.nativeTargetSession;
 assert.match(value.kernelProvenance.nativeInterpretation.binding.nativeInvocationId,/^[1-9][0-9]*$/);delete value.kernelProvenance.nativeInterpretation.binding.nativeInvocationId;
 const audit=value.kernelProvenance.nativeInterpretation.audit,assessment=value.kernelProvenance.repairAssessment;
 function measured(o,key){assert.ok(Number.isFinite(o[key])&&o[key]>=0,'existing measured timing '+key);delete o[key];}
 for(const phase of Object.values(audit.phases)){measured(phase,'inclusiveMs');measured(phase,'selfMs');}
 measured(audit.counters,'identityMs');for(const mark of audit.marks){measured(mark,'elapsedMs');measured(mark,'remainingMs');}
 assert.equal(audit.firstCancellation,null);assert.equal(audit.cpuTimeMs,null);
 assert.ok(audit.slowMetrics.length<=20);let previous=Infinity;
 for(const row of audit.slowMetrics){assert.ok(Number.isFinite(row.elapsedMs)&&row.elapsedMs>=0&&row.elapsedMs<=previous);previous=row.elapsedMs;
  const ledger=value.kernelProvenance.nativeInterpretation.metrics.rows.find(r=>r.obligationId==='boundary-'+row.boundaryIndex+'/'+row.obligationId);assert.ok(ledger,'timing row refers to complete semantic ledger: '+row.obligationId);assert.equal(row.method,ledger.metricMethod);assert.equal(row.status,'bounded-within-budget');assert.equal(ledger.status,'bounded');
  const boundary=assessment.boundaries[row.boundaryIndex];assert.ok(boundary);const candidates=boundary.residuals.filter(r=>r.referencePath===ledger.referencePath&&r.metricMethod===row.method&&'boundary-'+row.boundaryIndex+'/item-'+r.nativeEdgeId===ledger.sourceEdgeItemId&&'boundary-'+row.boundaryIndex+'/item-'+r.nativeFaceId===ledger.sourceFaceItemId&&r.first===ledger.postRangeFirst&&r.last===ledger.postRangeLast&&r.coedgeOrientation===ledger.sourceOrientation);assert.ok(candidates.some(r=>r.intervalCount===row.intervals&&r.status===row.status),'timing intervals/status match exact native residual');
 }
 delete audit.slowMetrics; // Timing-ranked top-20 selection, not the complete semantic ledger.
 for(const boundary of assessment.boundaries){measured(boundary.correlatedDiagnostics,'wallMilliseconds');assert.equal(boundary.correlatedDiagnostics.processCpuMilliseconds,null);
  for(const key of ['compositionWallMilliseconds','cylinderWallMilliseconds','rationalWallMilliseconds','highAxisWallMilliseconds'])measured(boundary.metricDispatch,key);
 }
 return value;
}
(async()=>{
 const candidate=path.resolve(process.argv[2]),root=path.resolve(process.argv[3]);
 const api=await require(candidate)({locateFile:name=>path.join(path.dirname(candidate),name)});
 for(const name of ['ReadStepFileWithTarget','BindNativeTargetSource','QueryNativeTargetBatch','ReleaseNativeTarget'])assert.equal(typeof api[name],'function','required retained native API: '+name);
 const options={linearUnit:'millimeter',repairErrorBudgetMm:.01,repairDiagnostics:true};
 const manifests=['native-common-route-fixtures.json','region-fixtures.json','finite-curved-fixtures.json','analytic-role-fixtures.json'].map(name=>({name,value:JSON.parse(fs.readFileSync(path.join(root,name)))}));
 function source(name){const manifest=manifests.find(m=>m.value.sources.some(s=>s.file===name));assert.ok(manifest,name);const row=manifest.value.sources.find(s=>s.file===name),bytes=fs.readFileSync(path.join(root,name));assert.equal(hash(bytes),row.sha256,name+' source bytes');return {bytes,sha256:row.sha256,manifest:manifest.name};}
 function binding(s){return {contract:'NativeTargetSourceBinding.v1',sourceGeneration:s.sha256,nativeImportRevision:'actual-test-import-'+s.sha256,topologyRevision:'test-topology-'+s.sha256,sourceBytesHash:s.sha256};}
 function request(id,b,points){return {contract:'NativeTargetQueryBatch.v1',sessionId:id,binding:b,batchId:'ordered-batch-a',policyVersion:'native-target-numerical-v1',mode:'membership-and-distance',coordinateSpace:'import-world-mm',points:points.map((point,i)=>({pointId:'point-'+i,point}))};}
 const cases=[];
 for(const suffix of ['', '-placed','-reversed'])cases.push({name:'native-common-route-cuboid'+suffix+'.step',placed:suffix==='-placed',points:[[17,13,11],[40,13,11],[35,27,29],[2,13,11],[2,3,11],[2,3,5]],distances:[6,8,13,0,0,0],states:['IN','OUT','OUT','ON','ON','ON']});
 for(const suffix of ['', '-placed'])cases.push({name:'native-common-route-cavity'+suffix+'.step',placed:suffix==='-placed',points:[[17,13,11],[7,13,11],[17,13,17],[20,13,11]],distances:[3,5,3,0],states:['OUT','IN','OUT','ON']});
 cases.push({name:'disk-cylinder.step',points:[[0,0,3],[0,0,9],[8,0,3]],distances:[3,3,0],states:['IN','OUT','ON']});
 cases.push({name:'ring.step',points:[[0,0,7.4],[16,0,7.4],[22.25,0,7.4]],distances:[10.7,5.3,0],states:['OUT','IN','ON']});
 for(const suffix of ['', '-placed','-reversed'])cases.push({name:'finite-curved-sphere-outward'+suffix+'.step',placed:suffix==='-placed',points:[[1,1,1],[0,1,1],[.5,1,1]],distances:[2-Math.sqrt(3),.5,0],states:['IN','OUT','ON']});
 cases.push({name:'analytic-rotational-groove.step',points:[[0,0,5],[9,0,5],[14,0,5]],distances:[5,0,Math.sqrt(13)],states:['IN','ON','OUT']});
 let checks=0,witnessed=0,inside=0,on=0;const receipts=[],unavailable=[];let retained;
 for(const c of cases){const s=source(c.name),start=performance.now(),imported=api.ReadStepFileWithTarget(s.bytes,options),parseMs=performance.now()-start;
  assert.equal(imported.success,true,c.name);assert.equal(imported.nativeTargetSession.status,'available',c.name+': '+JSON.stringify(imported.nativeTargetSession));
  const record=imported.nativeTargetSession,id=record.sessionId,b=binding(s),points=c.points.map(p=>c.placed?placed(p):p),batch=request(id,b,points);
  assert.equal(record.applicationInterpretationApproved,false);assert.equal(record.sourceBindingStatus,'unbound');assert.equal(record.allFaceIds.length,imported.meshes[0].brep_faces.length);assert.equal(new Set(record.allFaceIds).size,record.allFaceIds.length);
  assert.equal(api.QueryNativeTargetBatch(batch).reason,'session_unbound');const bad=Buffer.from(s.bytes);bad[0]^=1;assert.equal(api.BindNativeTargetSource(id,bad,b).reason,'source_bytes_mismatch');
  const bound=api.BindNativeTargetSource(id,s.bytes,b);assert.equal(bound.byteEqualityVerified,true);assert.equal(bound.bindingAuthority,'caller-labels-bound-to-exact-imported-bytes');assert.equal(bound.digestVerified,undefined);assert.equal(api.BindNativeTargetSource(id,s.bytes,b).status,'bound');
  const before=hash(JSON.stringify(imported)),result=api.QueryNativeTargetBatch(batch);assert.equal(hash(JSON.stringify(imported)),before,'queries do not mutate emitted source arrays');
  assert.equal(result.pointCount,points.length);assert.equal(result.evaluatedPointCount,points.length);assert.deepEqual(result.points.map(x=>x.point),points);assert.deepEqual(result.points.map(x=>x.pointId),batch.points.map(x=>x.pointId));assert.deepEqual(result.binding,b);assert.deepEqual(result.allFaceIds,record.allFaceIds);
  assert.equal(result.formalIntervalCertificate,false);assert.equal(result.certifiedDistanceErrorBoundMm,null);assert.equal(result.machiningAuthorized,false);assert.equal(result.classificationToleranceMm,1e-7);assert.equal(result.distanceDeflectionMm,1e-7);
  for(let i=0;i<points.length;++i){const row=result.points[i];assert.equal(row.boundaryDistance.status,'available',c.name+' distance '+i);assert.ok(Math.abs(row.boundaryDistance.valueMm-c.distances[i])<1e-6,c.name+' distance '+i+': '+row.boundaryDistance.valueMm+' expected '+c.distances[i]);
   assert.ok(['complete','ambiguous_or_unavailable'].includes(row.boundaryDistance.nearestSupportStatus));if(row.boundaryDistance.nearestSupportStatus==='complete')assert.ok(row.boundaryDistance.nearestSupports.length>0,'complete nearest metadata is nonempty');
   for(const support of row.boundaryDistance.nearestSupports){assert.ok(['face','edge','vertex'].includes(support.kind));const owned=support.kind==='face'?record.allFaceIds:imported.meshes[0].kernelTopology[support.kind==='edge'?'edges':'vertices'].map(x=>x[support.kind+'Id']);assert.ok(support.sourceIds.length>0&&support.sourceIds.every(x=>owned.includes(x)),'nearest support uses actual exported identity');}
   if(row.membership.status==='available'){assert.equal(row.membership.state,c.states[i],c.name+' membership '+i);if(c.states[i]==='OUT'){++witnessed;assert.ok(record.allFaceIds.includes(row.membership.sourceFaceId));assert.equal(row.membership.completionEvidence,'source-face-witnessed-OUT');}else if(c.states[i]==='IN')++inside;else ++on;}
   else {assert.equal(c.states[i],'OUT');assert.equal(row.membership.state,'UNKNOWN');assert.equal(row.membership.rawNativeState,'OUT');assert.equal(row.membership.reason,'classifier_out_completion_unobservable');unavailable.push({source:c.name,index:i,point:points[i]});}++checks;
  }
  const legacy=api.ReadStepFile(s.bytes,options);
  assert.ok(BigInt(legacy.kernelProvenance.nativeInterpretation.binding.nativeInvocationId)>BigInt(imported.kernelProvenance.nativeInterpretation.binding.nativeInvocationId),'independent imports advance the existing native nonce');
  if(process.env.CNC_TARGET_QUERY_RECEIPT_DIR){const dir=path.resolve(process.env.CNC_TARGET_QUERY_RECEIPT_DIR);fs.mkdirSync(dir,{recursive:true});for(const [kind,data]of [['retained',imported],['legacy',legacy]])fs.writeFileSync(path.join(dir,c.name+'.'+kind+'.json'),JSON.stringify(data)+'\n',{flag:'wx'});}
  assert.deepEqual(deterministic(imported),deterministic(legacy),c.name+' all deterministic legacy fields equal; only enumerated pre-existing clock diagnostics excluded');assert.equal(legacy.nativeTargetSession,undefined);
  assert.deepEqual(api.QueryNativeTargetBatch(batch).points,result.points,'owning session survives later unrelated import and audit reset');
  receipts.push({source:c.name,sourceSha256:s.sha256,manifest:s.manifest,outputSha256:hash(JSON.stringify(imported)),parseMs,batchMs:result.elapsedMs,evaluatedPointCount:result.evaluatedPointCount,status:result.status,rssBytes:process.memoryUsage().rss,wasmMemoryBytes:api.HEAPU8?.buffer.byteLength??null});
  if(!retained)retained={id,b,s,batch,result};else {assert.equal(api.ReleaseNativeTarget(id).released,true);assert.equal(api.ReleaseNativeTarget(id).released,false);assert.equal(api.QueryNativeTargetBatch(batch).reason,'session_unavailable');}
  console.log('PASS source '+c.name+' '+JSON.stringify(receipts.at(-1)));
 }
 assert.ok(witnessed>0&&inside>0&&on>0,'nonvacuous actual IN ON and witnessed OUT');
 const {id,b,s,batch}=retained;
 for(const change of [x=>x.binding.sourceGeneration+='x',x=>x.binding.sourceBytesHash='b'.repeat(64),x=>x.binding.nativeImportRevision+='x',x=>x.binding.topologyRevision+='x',x=>x.policyVersion='unknown',x=>x.mode='nearest-support',x=>x.coordinateSpace='stock-mm',x=>x.bodyId='forged',x=>x.allFaceIds=[],x=>x.points=[],x=>x.points=Array.from({length:257},(_,i)=>({pointId:String(i),point:[0,0,0]})),x=>x.points[0].point=[NaN,0,0],x=>x.points[0].point=[Infinity,0,0],x=>x.points[0].point=['0',0,0],x=>x.points[1].pointId=x.points[0].pointId,x=>x.points[0].pointId='',x=>delete x.points[0].point]){
  const wrong=clone(batch);change(wrong);const rejected=api.QueryNativeTargetBatch(wrong);assert.equal(rejected.status,'unavailable');assert.equal(rejected.points,undefined);++checks;
 }
 for(const key of ['sourceGeneration','sourceBytesHash','nativeImportRevision','topologyRevision']){const changed=clone(b);changed[key]=key==='sourceBytesHash'?'b'.repeat(64):changed[key]+'x';assert.equal(api.BindNativeTargetSource(id,s.bytes,changed).reason,'binding_mismatch');++checks;}
 const reverse=clone(batch);reverse.points.reverse();const reordered=api.QueryNativeTargetBatch(reverse);assert.deepEqual(reordered.points.map(p=>p.pointId),reverse.points.map(p=>p.pointId));
 for(const mode of ['membership','boundary-distance']){const x=api.QueryNativeTargetBatch({...batch,mode});assert.ok(x.points.every(p=>(mode==='membership'?p.boundaryDistance:p.membership).status==='not-requested'));assert.ok(x.points.every(p=>mode!=='membership'||p.boundaryDistance.valueMm===null));}
 const sessions=[id];for(let i=0;i<3;++i){const r=api.ReadStepFileWithTarget(s.bytes,options);assert.equal(r.nativeTargetSession.status,'available');sessions.push(r.nativeTargetSession.sessionId);}
 const overflow=api.ReadStepFileWithTarget(s.bytes,options);assert.equal(overflow.success,true);assert.equal(overflow.nativeTargetSession.reason,'session_capacity_exceeded');assert.deepEqual(api.QueryNativeTargetBatch(batch).points,retained.result.points);
 for(const x of sessions)assert.equal(api.ReleaseNativeTarget(x).released,true);assert.equal(api.QueryNativeTargetBatch(batch).reason,'session_unavailable');
 const again=api.ReadStepFileWithTarget(s.bytes,options);assert.equal(again.nativeTargetSession.status,'available');assert.ok(!sessions.includes(again.nativeTargetSession.sessionId));const released=api.ReleaseNativeTarget(again.nativeTargetSession.sessionId);assert.equal(released.retainedSourceBytes,0);assert.equal(released.liveSessions,0);
 const noAudit=api.ReadStepFileWithTarget(s.bytes,{linearUnit:'millimeter'});assert.equal(noAudit.success,true);assert.equal(noAudit.nativeTargetSession.reason,'native_interpretation_prerequisites_unavailable');
 const wrongUnits=api.ReadStepFileWithTarget(s.bytes,{...options,linearUnit:'centimeter'});assert.equal(wrongUnits.success,true);assert.equal(wrongUnits.nativeTargetSession.status,'unavailable','non-mm actual import cannot allocate a target session');
 const invalid=api.ReadStepFileWithTarget(Buffer.from('not STEP'),options);assert.equal(invalid.success,false);assert.equal(invalid.nativeTargetSession.status,'unavailable');assert.equal(api.ReleaseNativeTarget('unknown').liveSessions,0);
 console.log('RECEIPTS '+JSON.stringify({checks,sources:receipts,unavailable,witnessed,inside,on,maximumResidentSetBytes:process.resourceUsage().maxRSS*1024}));
 console.log('PASS '+cases.length+' sources; '+checks+' point/negative checks; deterministic output equality with enumerated timing/invocation exclusions; lifetime/capacity/binding/batch/release assertions');
})().catch(e=>{console.error(e);process.exitCode=1;});
