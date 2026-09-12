'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const h=require('./cnc-native-region-fixtures.cjs');
const {runtime:modelRuntime}=require('./cnc-model-worker-harness.cjs');
const {deliveryPage}=require('./cnc-native-thread-page-harness.cjs');
// Explicit pre-promotion replay only. Without this option every public source
// goes through the pinned model worker and real callback/quotation receiver.
const candidateEvidence=process.env.CNC_NATIVE_THREAD_CANDIDATE_EVIDENCE;
const parsedSources=new Map();
const names=['thread-external-rh','thread-external-lh','thread-external-rh-placed','thread-external-rh-reversed','thread-external-rh-placed-reversed','thread-external-lh-placed-reversed'];
async function candidate(name){
 if(!candidateEvidence){
  if(!parsedSources.has(name))parsedSources.set(name,(async()=>{
   const bytes=fs.readFileSync(path.join(h.fixtureRoot,name+'.step'));
   const manifest=JSON.parse(fs.readFileSync(path.join(h.fixtureRoot,'thread-fixtures.json')));
   assert.equal(h.digest(bytes),manifest.sources.find(r=>r.file===name+'.step').sha256);
   const worker=modelRuntime(),parsed=await worker.send({action:'parse',jobId:name,extension:'step',analysisProfile:'cnc',buffer:Uint8Array.from(bytes).buffer,retainNativeAnalysis:true});
   assert.equal(parsed.success,true);assert.equal(worker.state.nativeImports,1);
   assert.equal(parsed.cncGeometry.nativeSourceAssociation.sourceExpectation.sourceBytesHash,h.digest(bytes));
   return parsed;
  })());
  const parsed=await parsedSources.get(name),c=h.runtime(),n=structuredClone(parsed.cncGeometry.cadTopology.cadDocument.nativeImport);
  const topology=await h.topology(c,n,parsed.cncGeometry.nativeSourceAssociation.sourceExpectation);
  assert.equal(topology.nativeInterpretation.interpretationAccepted,true);
  return {c,n,topology,parsed};
 }
 const scratch=path.resolve(candidateEvidence),auditPath=path.join(scratch,'native-thread-transform-candidate-audit.json');
 const metadata=JSON.parse(fs.readFileSync(path.join(scratch,'native-thread-transform-candidate.json')));
 const audit=JSON.parse(fs.readFileSync(auditPath)),row=audit.rows.find(r=>r.source===name+'.step');
 const bytes=fs.readFileSync(path.join(scratch,name+'-transform-candidate-worker.json'));
 assert.equal(h.digest(bytes),row.outputSha256);assert.equal(h.digest(fs.readFileSync(path.join(h.fixtureRoot,row.source))),row.sourceSha256);
 for(const [file,digest] of [['occt-import-js.js',metadata.identity.jsSha256],['occt-import-js.wasm',metadata.identity.wasmSha256],['manifest.js',metadata.manifestSha256]])assert.equal(h.digest(fs.readFileSync(path.join(metadata.destination,file))),digest);
 const n=JSON.parse(bytes).cncGeometry.cadTopology.cadDocument.nativeImport;
 assert.deepEqual(n.buildIdentity,metadata.identity);assert.equal(n.sourceBytesHash,row.sourceSha256);
 const c=h.runtime(),expected={sourceBytesHash:row.sourceSha256,sourceAttachmentVerified:true,buildIdentity:metadata.identity,
  manifestUrl:'/lib/occt-cnc/'+path.basename(metadata.destination)+'/manifest.js',manifestSha256:metadata.manifestSha256,errorBudgetMm:.01,assessmentTimeLimitMs:60000};
 const topology=await h.topology(c,n,expected);assert.equal(topology.nativeInterpretation.interpretationAccepted,true);
 return {c,n,topology};
}
for(const name of names)test('paired native radial tubes retain complete '+name+' ownership',async()=>{
 const {c,topology,parsed}=await candidate(name),d=await c.CncNativePrismaticRecognition.recognize(topology);
 assert.equal(d.projection.sourceVerified,true);const owners=d.features.flatMap(f=>Array.from(f.primaryFaceIds));
 assert.equal(owners.length,23);assert.equal(new Set(owners).size,23);
 const threads=d.features.filter(f=>f.kind==='external_thread');assert.equal(threads.length,1,'a tighter cylinder tube must not erase unchanged finite flank geometry');
 const t=threads[0];assert.equal(t.primaryFaceIds.length,21);assert.equal(t.threadEvidence.flanks.length,10);
 assert.equal(t.handedness,name.includes('-lh')?'left':'right');assert.equal(t.startCount,1);
 assert.ok(Math.abs(t.measuredLeadMm-1)<1e-9);assert.ok(Math.abs(t.measuredPitchMm-1)<1e-9);assert.ok(Math.abs(t.dimensions.fullDepthMm-4)<1e-8);
 assert.ok(Math.abs(t.measuredMinorDiameterMm-12.741)<1e-10);assert.ok(Math.abs(t.measuredMajorDiameterMm-13.884)<1e-10);
 const placed=name.includes('-placed'),a=[1,2,3].map(x=>x/Math.sqrt(14)),angle=.713;
 const expectedAxis=placed?[a[1]*Math.sin(angle)+a[0]*a[2]*(1-Math.cos(angle)),-a[0]*Math.sin(angle)+a[1]*a[2]*(1-Math.cos(angle)),Math.cos(angle)+a[2]*a[2]*(1-Math.cos(angle))]:[0,0,1];
 const origin=placed?[17,-23,41]:[0,0,0],direction=Array.from(t.axisLine.unitDirection),offset=Array.from(t.axisLine.originMm,(x,i)=>x-origin[i]);
 assert.ok(Math.abs(Math.abs(direction.reduce((s,x,i)=>s+x*expectedAxis[i],0))-1)<1e-12);
 const along=offset.reduce((s,x,i)=>s+x*expectedAxis[i],0);assert.ok(Math.hypot(...offset.map((x,i)=>x-along*expectedAxis[i]))<1e-9);
 assert.equal(Object.values(t.faceRoles).filter(r=>r==='flank').length,10);
 for(const [id,range] of Object.entries(t.faceAxialCoverageMm)){
  const regions=Array.from(t.faceSubregions[id]).sort((a,b)=>a.axialIntervalMm[0]-b.axialIntervalMm[0]);
  assert.equal(regions[0].axialIntervalMm[0],range[0]);assert.equal(regions.at(-1).axialIntervalMm[1],range[1]);
  for(let i=1;i<regions.length;i++)assert.equal(regions[i-1].axialIntervalMm[1],regions[i].axialIntervalMm[0]);
  assert.ok(regions.every(r=>r.required&&r.trimPredicate.nativeTrim&&r.nativeFaceId===id));
 }
 for(const flank of t.threadEvidence.flanks){
  const p=flank.profile,r=p.radialCompatibility;assert.ok(r);assert.equal(r.proofs.length,2);
  assert.deepEqual(Array.from(r.actualRadialEnvelopeMm),Array.from(p.radialEnvelopeMm));
  assert.equal(r.coverage[0].vDomain[0],r.representedVDomain[0]);assert.equal(r.coverage.at(-1).vDomain[1],r.representedVDomain[1]);
  for(let i=1;i<r.coverage.length;i++)assert.equal(r.coverage[i-1].vDomain[1],r.coverage[i].vDomain[0]);
  assert.ok(r.coverage.every(cell=>cell.continuationMm>=cell.continuationSegments.reduce((sum,s)=>sum+s.displacementBoundMm,0)));
  assert.ok(r.coverage.every(cell=>cell.continuationSegments.every(s=>s.displacementBoundMm>=s.vDistance*s.derivativeBoundMmPerV)));
  for(const proof of r.proofs){
   assert.ok(proof.flankTubeMm>0);assert.ok(proof.seedTubeMm<1e-5);
   assert.ok(proof.pairedTubeMm>=proof.flankTubeMm+proof.seedTubeMm);
   assert.notEqual(proof.flankMetricRef,proof.seedMetricRef);assert.ok(proof.sourceEdgeItemId);
   assert.ok(proof.maximumContinuationMm<=Math.min(...proof.endpointContinuationLimitsMm));
  }
 }
 if(name==='thread-external-rh'){
  const f=t.threadEvidence.flanks.find(f=>f.faceId==='body-0/face-17');
  assert.ok(f.profile.radialEnvelopeMm[0]<=6.370498459108329,'the actual cubic radial departure is not projected onto the cylinder');
  assert.equal(f.profile.radiusA,6.3705);assert.equal(f.profile.radiusB,6.942);
  assert.ok(f.profile.radialCompatibility.proofs.some(p=>p.flankTubeMm>.009 && p.seedTubeMm<1e-6));
 }
 assert.equal(d.automaticPlanningEligible,false);
 if(parsed){
  const graph=parsed.cncGeometry.manufacturingFeatureGraph,thread=graph.features.find(f=>f.kind==='external_thread');assert.ok(thread);
  const receiver=modelRuntime();receiver.c.importScripts('/src/app/js/cnc-quotation/cnc-quotation.worker.js');
  const estimate=await receiver.c.CncQuotationWorker.estimate(structuredClone(deliveryPage().deliver(parsed)));
  assert.equal(estimate.quote,null);assert.equal(receiver.state.nativeImports,0);
  const rederived=estimate.featureGraph.features.find(f=>f.kind==='external_thread');assert.ok(rederived);
  for(const field of ['threadEvidence','faceSubregions','faceRoles','dimensions','axisLine','handedness','startCount'])
   assert.deepEqual(JSON.parse(JSON.stringify(rederived[field])),JSON.parse(JSON.stringify(thread[field])));
 }
});
test('paired radial admission rejects missing metrics, wrong source edge, partial domains and unbounded endpoint continuation',async()=>{
 const {c,topology}=await candidate('thread-external-rh'),math=c.CncNativeThreadEvidence,captured=[];
 c.CncNativeThreadEvidence={...math,pairedRadial(...args){captured.push(args);return math.pairedRadial(...args);}};
 const positive=await c.CncNativePrismaticRecognition.recognize(topology);assert.equal(positive.features.filter(f=>f.kind==='external_thread').length,1);assert.equal(captured.length,10);
 const args=captured.find(a=>a[0].faceId==='body-0/face-17'),id=args[2][0].entry.use.coedgeId;
 const generated=args.slice();generated[3]=new Map(args[3]);
 for(const [key,value] of generated[3])generated[3].set(key,value.map(r=>({...r,referencePath:'post-lift-to-unchanged-source-3d-plus-endpoint-sliver',sourceRange:{first:0,last:0}})));
 const generatedProof=math.pairedRadial(...generated);
 assert.equal(generatedProof.proofs.length,2);
 for(const p of generatedProof.proofs){assert.deepEqual(Array.from(p.priorFlankPcurveDomain),[0,0]);assert.deepEqual(Array.from(p.sourceParameterDomain),Array.from(p.flankParameterDomain));assert.ok(p.flankPostLiftMetricRefs.length);}
 for(const mutation of ['unsupported-method','missing-post-domain']){
  const changed=generated.slice();changed[3]=new Map(generated[3]);const row=structuredClone(changed[3].get(id)[0]);
  if(mutation==='unsupported-method')row.referencePath='unassessed-post-domain';else row.pathTermsMm=[];
  changed[3].set(id,[row]);assert.throws(()=>math.pairedRadial(...changed),/metric_domain_unavailable/);
 }
 for(const mutation of ['missing','wrong-edge','partial']){
  const changed=args.slice(),rows=new Map(args[3]);changed[3]=rows;
  if(mutation==='missing')rows.delete(id);
  else{const row=structuredClone(rows.get(id)[0]);if(mutation==='wrong-edge')row.sourceEdgeItemId='different-native-source-edge';else row.postRangeLast=.5;rows.set(id,[row]);}
  assert.throws(()=>math.pairedRadial(...changed),/native_thread_paired_radial_(metric_unavailable|correspondence)/);
 }
 const missingSide=args.slice();missingSide[2]=args[2].slice(0,1);assert.throws(()=>math.pairedRadial(...missingSide),/row_domain/);
 const expanded=args.slice();expanded[1]=math.profile(args[0],args[2][0].seed.support,[6.3705,6.942]);
 expanded[1].trimUVBounds[1][1]+=.1;
 assert.throws(()=>math.pairedRadial(...expanded),/domain_uncovered/,'partial paired coverage does not certify a larger row domain');
 const partial=captured.find(a=>a[0].faceId==='body-0/face-1'),changed=partial.slice(),face=structuredClone(partial[0]);
 const use=face.trims.wires.flatMap(w=>w.coedges).find(u=>u.edgeId==='body-0/edge-6');use.pcurve.poles[0][1]-=.1;
 changed[0]=face;changed[1]=math.profile(face,partial[2][0].seed.support,[6.3705,6.942]);
 changed[2]=partial[2].map(connection=>{
  const wire=face.trims.wires.find(w=>w.wireId===connection.entry.wire.wireId);
  return {...connection,entry:{wire,use:wire.coedges.find(u=>u.coedgeId===connection.entry.use.coedgeId)}};
 });
 assert.throws(()=>math.pairedRadial(...changed),/endpoint_continuation_unavailable/,'same vertex identity cannot license a long unsupported continuation');
 c.CncNativeThreadEvidence={...math,pairedRadial(){throw new Error('native_thread_paired_radial_metric_unavailable');}};
 const rejected=await c.CncNativePrismaticRecognition.recognize(topology);
 assert.equal(rejected.features.some(f=>f.kind==='external_thread'),false);
 assert.equal(new Set(rejected.features.flatMap(f=>Array.from(f.primaryFaceIds))).size,23);
 const flanks=captured.map(a=>a[0]);assert.equal(flanks.length,10);
 for(const f of flanks){
  const owner=rejected.features.find(o=>o.primaryFaceIds.includes(f.faceId));assert.equal(owner.kind,'unresolved');assert.equal(owner.required,true);
  assert.equal(owner.reason,'native_thread_paired_radial_metric_unavailable');assert.deepEqual(Array.from(owner.primaryFaceIds),[f.faceId]);
  const regions=rejected.projection.regions.filter(r=>r.faceId===f.faceId);assert.equal(regions.length,1);
  assert.equal(owner.regionId,regions[0].regionId);assert.equal(owner.regionEvidence.regionId,regions[0].regionId);
  assert.ok(rejected.projection.provenance.nativeImportRevision);assert.equal(owner.regionEvidence.nativeImportRevision,rejected.projection.provenance.nativeImportRevision);
 }
});
module.exports={candidate};

// Arithmetic-boundary construction, deliberately not a trusted native import.
// Consecutive cubic spans represent exactly linear segments with declining
// tangential derivative. Shared labels cannot cover their excessive gap.
function continuationFixture(limit=2e-7,uDrift=0,fullMultiplicity=false){
 const c=h.runtime(),math=c.CncNativeThreadEvidence,d=1e-4,knots=[0,d,2*d,3*d],slopes=[.0014,.0014,.0009],zSlope=.00002;
 const row=[];let y=0;
 for(let span=0;span<3;span++){
  for(let j=span?1:0;j<4;j++){const v=knots[span]+j*d/3;row.push([5,y+slopes[span]*j*d/3,zSlope*v]);}
  y+=slopes[span]*d;
 }
 const controls=fullMultiplicity?[...row.slice(0,4),...row.slice(3,7),...row.slice(6,10)]:row;
 const poles=Array.from({length:4},(_,i)=>controls.map(p=>[p[0]*(1+i/15),p[1]*(1+i/15),p[2]-i/(3*Math.sqrt(3))]));
 const pc=(origin,direction)=>({type:'line',origin,direction});
 const uses=[
  {coedgeId:'flank/left',edgeId:'left',startVertexId:'a',endVertexId:'b',orientation:0,pcurve:pc([0,0],[uDrift,1]),range:[0,d]},
  {coedgeId:'flank/top',edgeId:'top',startVertexId:'b',endVertexId:'c',orientation:0,pcurve:pc([3*d*uDrift,3*d],[1-6*d*uDrift,0]),range:[0,1]},
  {coedgeId:'flank/right',edgeId:'right',startVertexId:'c',endVertexId:'e',orientation:0,pcurve:pc([1,0],[-uDrift,1]),range:[0,d]},
  {coedgeId:'flank/bottom',edgeId:'bottom',startVertexId:'e',endVertexId:'a',orientation:0,pcurve:pc([1,0],[-1,0]),range:[0,1]}
 ];
 const wire={wireId:'flank/wire',coedges:uses},face={faceId:'flank',orientation:0,trims:{surfaceBasis:{type:'bspline',uDegree:3,vDegree:3,uPeriodic:false,vPeriodic:false,uRational:false,vRational:false,
  poles,weights:poles.map(r=>r.map(()=>1)),uKnots:[0,1],vKnots:knots,uMultiplicities:[4,4],vMultiplicities:fullMultiplicity?[4,4,4,4]:[4,3,3,4]},surfaceToImportWorld3x4:[1,0,0,0,0,1,0,0,0,0,1,0],wires:[wire]}};
 const support={origin:[0,0,0],axis:[0,0,1],xDirection:[1,0,0],yDirection:[0,1,0],direct:true,orientationSign:1};
 const metrics=new Map(),vertices=new Map(['a','b','c','e'].map(id=>[id,{status:'complete',tolerance:limit/2,worldPoint:[0,0,0]}]));
 function metric(use,wireId,faceId){return {status:'bounded',faceId,wireId,coedgeId:use.coedgeId,edgeId:use.edgeId,sourceEdgeItemId:use.edgeId,obligationId:use.coedgeId+'/metric',associationStatus:'complete-unique-native-occurrence',rangeIdentityStatus:'source-and-post-ranges-captured',postRangeFirst:0,postRangeLast:d,sourceRange:{first:0,last:d},upperBoundMm:.001,referencePath:'exact-unchanged-trim-with-independent-edge-residual',pathTermsMm:[{kind:'post-trim-lift-residual',termId:use.coedgeId+'/term',upperBoundMm:.001}]};}
 const connections=[uses[0],uses[2]].map((use,i)=>{
  const seedUse={...use,coedgeId:'seed/'+i},seedWire={wireId:'seed/'+i+'/wire',coedges:[seedUse]},seedFace={faceId:'seed/'+i,trims:{wires:[seedWire]}};
  metrics.set(use.coedgeId,[metric(use,wire.wireId,face.faceId)]);metrics.set(seedUse.coedgeId,[metric(seedUse,seedWire.wireId,seedFace.faceId)]);
  return {entry:{wire,use},neighbor:{coedgeId:seedUse.coedgeId,wireId:seedWire.wireId},seed:{face:seedFace,support:{...support,radius:i?6:5},observations:[{edgeId:use.edgeId,coedgeId:seedUse.coedgeId,wireId:seedWire.wireId}]}};
 });
 return {math,args:[face,math.profile(face,support,[5,6]),connections,metrics,vertices]};
}
test('paired continuation accumulates declining derivatives across every crossed knot span',()=>{
 const {math,args}=continuationFixture();
 assert.throws(()=>math.pairedRadial(...args),/endpoint_continuation_unavailable/,
  'actual 2.3003477997902837e-7 displacement exceeds 2e-7; destination-only bound incorrectly passes');
 const relaxed=continuationFixture(1),r=relaxed.math.pairedRadial(...relaxed.args),bound=r.coverage.at(-1).continuationMm;
 assert.ok(bound>=2.3003477997902837e-7&&bound<2.301e-7,'the bound accumulates both finite derivatives, not a destination-only shortcut');
});
test('paired continuation certificate covers the full anchor-to-cell affine U image',()=>{
 const {math,args}=continuationFixture(1,.002),r=math.pairedRadial(...args);
 const cell=r.coverage.find(c=>c.vDomain[0]>=2e-4&&c.vDomain[1]>=3e-4);
 assert.ok(cell);assert.ok(cell.continuationPathNormalizedUDomain,'a continued cell must expose its actually assessed full U path');
 assert.ok(cell.continuationPathNormalizedUDomain[0]<=2e-7,'the anchor U is outside the destination-cell U interval');
 assert.ok(cell.continuationPathNormalizedUDomain[1]>=6e-7);
 assert.ok(cell.continuationSegments.length>=2,'both crossed spans must contribute');
 assert.ok(cell.continuationSegments[0].vDomain[0]<=1e-4);
 assert.equal(cell.continuationSegments.at(-1).vDomain[1],cell.vDomain[1]);
 for(let i=1;i<cell.continuationSegments.length;i++)assert.equal(cell.continuationSegments[i-1].vDomain[1],cell.continuationSegments[i].vDomain[0]);
 assert.ok(cell.continuationMm>=cell.continuationSegments.reduce((sum,s)=>sum+s.displacementBoundMm,0));
});
test('paired continuation rejects crossed knots without a complete C0 premise',()=>{
 const {math,args}=continuationFixture(1,0,true);
 assert.throws(()=>math.pairedRadial(...args),/continuation_discontinuous/,
  'full-multiplicity knots do not establish C0, even with equal apparent endpoint values');
});
