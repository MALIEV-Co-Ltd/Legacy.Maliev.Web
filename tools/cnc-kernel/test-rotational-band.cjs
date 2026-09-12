const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
function validate(result){
 assert.equal(result.success,true);let available=0,unavailable=0;
 const seenFaces=new Set();
 for(const mesh of result.meshes){
  const edges=new Map(mesh.kernelTopology.edges.map(e=>[e.edgeId,e]));
  for(const face of mesh.brep_faces){
   const r=face.nativeRotationalBand;assert.ok(r);assert.equal(r.schema,'MalievNativeRotationalBand.v1');assert.equal(r.methodVersion,1);
   assert.equal(r.bodyId,mesh.bodyId);assert.equal(r.faceId,face.faceId);assert.ok(!seenFaces.has(r.faceId));seenFaces.add(r.faceId);
   assert.ok(['available','unavailable'].includes(r.status));assert.equal(r.precision.policy,'affine-axis-roundoff-v1');assert.equal(r.precision.epsilonFactor,64);assert.equal(r.precision.outwardEnclosure,false);
   const wires=new Map(face.trims.wires.map(w=>[w.wireId,w])),uses=new Map(face.trims.wires.flatMap(w=>w.coedges.map(u=>[u.coedgeId,{...u,wireId:w.wireId}])));
   assert.deepEqual(r.wireOccurrences.map(w=>[w.wireId,w.orderedCoedgeIds]),face.trims.wires.map(w=>[w.wireId,w.coedges.map(u=>u.coedgeId)]));
   if(r.status==='unavailable'){
    ++unavailable;assert.equal(typeof r.reason,'string');assert.ok(r.reason.length);for(const key of ['frame','parameters','uCoverage','profileInterval','sides','seamPairs','boundaryComponents','polarity'])assert.equal(r[key],null);continue;
   }
   ++available;assert.equal(r.reason,null);assert.equal(r.sides.length,4);assert.equal(face.trims.status,'complete');
   assert.deepEqual(r.sides.flatMap(s=>s.orderedCoedgeIds).sort(),[...uses.keys()].sort());
   for(const side of r.sides){
    assert.ok(wires.has(side.wireId));assert.equal(side.occurrences.length,side.orderedCoedgeIds.length);
    for(const [i,id] of side.orderedCoedgeIds.entries()){
     const use=uses.get(id);assert.ok(use);assert.equal(use.wireId,side.wireId);assert.equal(side.edgeIds[i],use.edgeId);assert.equal(side.occurrences[i].coedgeId,id);assert.equal(side.occurrences[i].edgeId,use.edgeId);assert.deepEqual(side.occurrences[i].nativeRange,use.range);assert.equal(side.occurrences[i].orientation,use.orientation);
     const edge=edges.get(use.edgeId);assert.ok(edge);assert.equal(edge.uses.filter(e=>e.faceId===r.faceId&&e.wireId===side.wireId&&e.coedgeId===id).length,1);
    }
   }
   const seamIds=new Set();for(const pair of r.seamPairs){
    const a=uses.get(pair.firstCoedgeId),b=uses.get(pair.secondCoedgeId);assert.ok(a&&b);assert.notEqual(a.coedgeId,b.coedgeId);assert.equal(a.edgeId,pair.edgeId);assert.equal(b.edgeId,pair.edgeId);assert.ok(a.seam&&b.seam);assert.notEqual(a.orientation,b.orientation);assert.ok(!seamIds.has(a.coedgeId)&&!seamIds.has(b.coedgeId));seamIds.add(a.coedgeId);seamIds.add(b.coedgeId);
   }
   for(const boundary of r.boundaryComponents){const side=r.sides[boundary.sideIndex];assert.ok(side);assert.equal(boundary.wireId,side.wireId);assert.deepEqual(boundary.orderedCoedgeIds,side.orderedCoedgeIds);assert.deepEqual(boundary.edgeIds,side.edgeIds);for(const id of boundary.orderedCoedgeIds)assert.ok(!seamIds.has(id));}
   assert.deepEqual([...r.boundaryComponents.flatMap(b=>b.orderedCoedgeIds),...seamIds].sort(),[...uses.keys()].sort());
   if(r.uCoverage.kind==='complete-revolution'){assert.equal(r.boundaryComponents.length,2);assert.ok(r.seamPairs.length>0);assert.ok(r.boundaryComponents.every(b=>b.kind==='ring'));}
   else{assert.equal(r.uCoverage.kind,'partial');assert.equal(r.boundaryComponents.length,4);assert.equal(r.seamPairs.length,0);}
   for(const values of [r.frame.origin,r.frame.axis,r.frame.xDirection,r.frame.yDirection,r.uCoverage.liftedInterval,r.profileInterval.nativeV,r.profileInterval.radialMm,r.profileInterval.axialMm])assert.ok(values.every(Number.isFinite));
  }
 }
 return {available,unavailable};
}
module.exports={validate};
if(require.main===module)(async()=>{
 const api=await require(path.resolve(process.argv[2]))();const fixtures=path.resolve(process.argv[3]);
 const cube=fs.readFileSync(path.join(fixtures,'cube-units/cube-mm.step'));
 const first=api.ReadStepFile(cube,null);assert.deepEqual(validate(first),{available:0,unavailable:6});
 if(process.argv[4]){
  const positive=api.ReadStepFile(fs.readFileSync(process.argv[4]),{linearUnit:'millimeter'});assert.ok(validate(positive).available>0);
  const mutate=fn=>{const copy=structuredClone(positive);const mesh=copy.meshes.find(m=>m.brep_faces.some(f=>f.nativeRotationalBand.status==='available'));const face=mesh.brep_faces.find(f=>f.nativeRotationalBand.status==='available');fn(copy,mesh,face);assert.throws(()=>validate(copy));};
  mutate((r,m,f)=>{f.nativeRotationalBand.bodyId='foreign';});mutate((r,m,f)=>{f.nativeRotationalBand.faceId='foreign';});mutate((r,m,f)=>{delete f.nativeRotationalBand;});mutate((r,m,f)=>{m.brep_faces.push(f);});
  mutate((r,m,f)=>{f.nativeRotationalBand.sides[0].wireId='foreign';});mutate((r,m,f)=>{f.nativeRotationalBand.sides[0].orderedCoedgeIds[0]='foreign';});mutate((r,m,f)=>{f.nativeRotationalBand.boundaryComponents[0].orderedCoedgeIds=['foreign'];});mutate((r,m,f)=>{f.nativeRotationalBand.sides[0].occurrences[0].nativeRange=[0,0];});mutate((r,m,f)=>{m.kernelTopology.edges.find(e=>e.edgeId===f.nativeRotationalBand.sides[0].edgeIds[0]).uses=[];});
 }
 assert.equal(api.ReadStepFile(Buffer.from('invalid'),null).success,false);assert.deepEqual(validate(api.ReadStepFile(cube,null)),validate(first));
 const nonmm=api.ReadStepFile(cube,{linearUnit:'meter'});validate(nonmm);assert.ok(nonmm.meshes.every(m=>m.brep_faces.every(f=>f.nativeRotationalBand.reason==='millimeter_units_unverified')));
 console.log('PASS rotational band actual exporter, exact memberships, mutations and reset');
})().catch(e=>{console.error(e);process.exitCode=1;});
