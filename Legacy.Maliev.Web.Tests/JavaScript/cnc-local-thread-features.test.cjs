const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot');
let fixture;
async function plate() {
    if (fixture) return fixture;
    const c = vm.createContext({console}); c.self = c;
    for (const file of ['cnc-geometry.worker','cnc-cad-surfaces.worker'])
        vm.runInContext(fs.readFileSync(path.join(root,'src/app/js/cnc-quotation',file+'.js'),'utf8'),c);
    const source = fs.readFileSync(path.resolve(__dirname,'../TestAssets/Cnc/threaded-cover-plate.step'));
    const occt = await require(path.join(root,'lib/occt/occt-import-js.js'))({wasmBinary:fs.readFileSync(path.join(root,'lib/occt/occt-import-js.wasm'))});
    const model = occt.ReadStepFile(source,null); assert.equal(model.success,true);
    const triangles=[];
    for (const mesh of model.meshes) for (const index of mesh.index.array)
        triangles.push(...mesh.attributes.position.array.slice(index*3,index*3+3));
    const array = new Float32Array(triangles), axes=c.CncChooseAxes(array).axes;
    const analysis=c.CncSurfaceAnalysis(array,axes);
    const hints=c.CncCadSurfaces.parseStep(source.toString());
    const proxies=c.CncFeatureProxies(analysis.clusters,{eligible:false},analysis.origin,analysis.records,axes,hints);
    fixture={c,analysis,proxies,hints,axes,array}; return fixture;
}
test('a long concave channel does not become a drilled bore',async()=>{
    const {proxies}=await plate();
    assert.equal(proxies.holes.length,2);
    assert.ok(proxies.holes.every(h=>Math.abs(h.diameterMm-5)<.1));
});
test('coaxial blind bores separated by solid stock remain distinct holes',async()=>{
    const {c}=await plate(), axis={x:1,y:0,z:0};
    const bore=(id,x)=>({memberIndexes:[],radialNormalSign:-1,normalConsistency:0,evidence:{
        id,type:'cylindrical',axis,centroid:{x,y:0,z:0},radiusMm:2.5,
        axialDepthMm:6,isInternal:true,confidence:'High'}});
    const proxies=c.CncFeatureProxies([bore('left-bore',-15),bore('right-bore',15)],
        {eligible:false},{x:0,y:0,z:0},null,[axis],[]);
    assert.equal(proxies.holes.length,2,'solid stock between opposing blind bores must split the features');
    assert.deepEqual(JSON.parse(JSON.stringify(proxies.holes.map(h=>Array.from(h.surfaceClusterIds)).sort())),
        [['left-bore'],['right-bore']]);
});
test('a nonrotational plate exposes its two local modeled M6 threads',async()=>{
    const {proxies,analysis}=await plate();
    assert.equal(proxies.threads.length,2);
    for(const thread of proxies.threads){
        // Modeled roots include relief beyond the nominal M6 major diameter.
        assert.ok(Math.abs(thread.majorDiameterMm-6.158)<.02);
        assert.ok(Math.abs(thread.pitchMm-1)<.05);
        assert.ok(thread.depthMm>5);
        assert.ok(thread.entryDirections.some(d=>d.z>.99));
        assert.ok(thread.surfaceClusterIds.length>5);
        assert.ok(thread.surfaceClusterIds.every(id=>analysis.clusters.find(c=>c.evidence.id===id).evidence.featureType==='thread'));
    }
});
test('verified 90 degree countersinks are chamfers, never ball finishing patches',async()=>{
    const {c,proxies,analysis,axes,hints}=await plate();
    assert.equal(hints.filter(h=>h.kind==='cone').length,2);
    assert.equal(proxies.chamfers.length,2);
    for(const chamfer of proxies.chamfers){
        assert.ok(Math.abs(chamfer.includedAngleDegrees-90)<.001);
        assert.ok(chamfer.majorDiameterMm>chamfer.minorDiameterMm);
        assert.ok(chamfer.entryDirections.some(d=>d.z>.99));
        assert.ok(chamfer.verifiedPilotDepthBelowMm>1.2);
        assert.ok(Math.abs(chamfer.pilotDiameterMm-5.035)<.02);
    }
    for(const cluster of analysis.clusters) cluster.evidence.accessibleTriangleIndexesByDirection={'positive-z':cluster.memberIndexes.map(i=>analysis.records[i].sourceTriangleIndex)};
    c.CncApplyCurvedFinishingEvidence(analysis,axes);
    for(const feature of [...proxies.chamfers,...proxies.threads]) for(const id of feature.surfaceClusterIds){
        const cluster=analysis.clusters.find(c=>c.evidence.id===id).evidence;
        assert.deepEqual(Object.keys(cluster.curvedFinishingByDirection),[]);
        assert.equal(cluster.filletFeatures.length,0);
    }
});

test('a cone without an identified adjoining bore has no pilot-depth certificate',async()=>{
    const {c,analysis,axes,hints}=await plate();
    const cones=analysis.clusters.filter(cluster=>cluster.evidence.featureType==='chamfer');
    const result=c.CncFeatureProxies(cones,{eligible:false},analysis.origin,analysis.records,axes,hints);
    assert.equal(result.holes.length,0);
    assert.equal(result.chamfers.length,2);
    assert.ok(result.chamfers.every(chamfer=>chamfer.verifiedPilotDepthBelowMm===undefined));
    assert.ok(result.chamfers.every(chamfer=>chamfer.pilotDiameterMm===undefined));
});

test('untransformed STEP cone hints cannot classify a translated face',async()=>{
    const {c,analysis,hints}=await plate();
    const cone=analysis.clusters.find(cl=>cl.evidence.featureType==='chamfer');
    const displaced=hints.filter(h=>h.centerMm).map(h=>({...h,
        centerMm:{...h.centerMm,x:h.centerMm.x+1000}}));
    assert.equal(c.CncMatchLocalSupport(cone,analysis.records,displaced,'cone'),null);
});

test('vertices on a cone do not certify a face with incompatible normals',async()=>{
    const {c,analysis,hints}=await plate();
    const cone=analysis.clusters.find(cl=>cl.evidence.featureType==='chamfer');
    const incompatible=analysis.records.map(record=>({...record,normal:{x:0,y:0,z:1}}));
    assert.equal(c.CncMatchLocalSupport(cone,incompatible,hints,'cone'),null);
});

test('periodic annular grooves are not a helical thread',async()=>{
    const {c}=await plate(),records=[],clusters=[];
    const axis={x:0,y:0,z:1},center={x:0,y:0,z:0};
    // Eight repeated full circular roots. Axial repetition alone looks like a
    // thread, but no angular advance exists and the helical phase must reject it.
    for(let row=0;row<32;row++){
        const memberIndexes=[];
        for(let angle=0;angle<24;angle++){
            const point=(a,z)=>({x:(2.5+.5*Math.sin(Math.PI*z)**2)*Math.cos(a),y:(2.5+.5*Math.sin(Math.PI*z)**2)*Math.sin(a),z});
            const a=angle*Math.PI/12,b=(angle+1)*Math.PI/12,z=row/4,next=(row+1)/4;
            memberIndexes.push(records.length);
            records.push({vertices:[point(a,z),point(b,z),point(b,next)]});
            memberIndexes.push(records.length);
            records.push({vertices:[point(a,z),point(b,next),point(a,next)]});
        }
        clusters.push({memberIndexes,evidence:{id:'ring-'+row}});
    }
    const holes=[{id:'bore',diameterMm:5,depthMm:8}];
    const groups=[{axis,centroid:center,candidates:clusters.map(cluster=>({cluster}))}];
    assert.equal(c.CncLocalModeledThreads(holes,groups,clusters,records).length,0);
});

test('a roof above a verified chamfer remains an entry obstruction',async()=>{
    const {c,analysis,hints}=await plate();
    const cone=hints.find(h=>h.kind==='cone'),center=cone.centerMm;
    const roof={vertices:[{x:center.x-20,y:center.y-20,z:center.z+20},
        {x:center.x+20,y:center.y-20,z:center.z+20},{x:center.x,y:center.y+20,z:center.z+20}]};
    const entries=c.CncHoleEntryDirections([...analysis.records,roof],center,cone.axis,5.6,1.6,1.6,null,[1]);
    assert.equal(entries.length,0);
});

test('spotting entry checks the full cutter and holder, not only the pilot diameter',async()=>{
    const {c}=await plate(), center={x:0,y:0,z:0},axis={x:0,y:0,z:1};
    const wall={vertices:[{x:5.5,y:-1,z:1},{x:5.5,y:1,z:1},{x:5.5,y:0,z:3}]};
    const result=c.CncSpotEntryEvidence([wall],center,axis,0,0,null,[1]);
    assert.equal(result.byDiameterMm['10'].length,1);
    assert.equal(result.byDiameterMm['12'].length,0);
    assert.equal(result.byDiameterMm['16'].length,0);
    const holderWall={vertices:[{x:11,y:-1,z:21},{x:11,y:1,z:21},{x:11,y:0,z:23}]};
    const holder=c.CncSpotEntryEvidence([holderWall],center,axis,0,0,null,[1]);
    assert.equal(holder.byDiameterMm['10'].length,0);
});

test('mirroring a modeled right-hand thread changes its detected handedness',async()=>{
    const {c,array,hints,proxies}=await plate();
    assert.ok(proxies.threads.every(thread=>thread.handedness==='right'));
    const reflected=new Float32Array(array.length);
    for(let offset=0;offset<array.length;offset+=9){
        // Reflect X and reverse triangle winding, preserving outward normals.
        for(const [target,source] of [[0,0],[3,6],[6,3]]){
            reflected[offset+target]=-array[offset+source];
            reflected[offset+target+1]=array[offset+source+1];
            reflected[offset+target+2]=array[offset+source+2];
        }
    }
    const axes=c.CncChooseAxes(reflected).axes;
    const analysis=c.CncSurfaceAnalysis(reflected,axes);
    const mirroredHints=hints.filter(hint=>hint.centerMm&&hint.axis).map(hint=>({...hint,
        centerMm:{...hint.centerMm,x:-hint.centerMm.x},axis:{...hint.axis,x:-hint.axis.x}}));
    const mirrored=c.CncFeatureProxies(analysis.clusters,{eligible:false},analysis.origin,analysis.records,axes,mirroredHints);
    assert.equal(mirrored.threads.length,2);
    assert.ok(mirrored.threads.every(thread=>thread.handedness==='left'));
    assert.ok(mirrored.threads.every(thread=>Math.abs(thread.pitchMm-1)<.01));
});

test('a rotational external helix publishes its measured pitch and external ownership',async()=>{
    const {c}=await plate(), axis={x:0,y:0,z:1}, center={x:0,y:0,z:0};
    const records=[], memberIndexes=[], rows=40, segments=24, pitch=2;
    const point=(angle,z)=>{
        const phase=2*Math.PI*z/pitch+angle;
        const radius=5+0.5*(1+Math.cos(phase))/2;
        return {x:radius*Math.cos(angle),y:radius*Math.sin(angle),z};
    };
    const add=(vertices)=>{
        const centroid=vertices.reduce((sum,p)=>({x:sum.x+p.x/3,y:sum.y+p.y/3,z:sum.z+p.z/3}),{x:0,y:0,z:0});
        const radial=Math.hypot(centroid.x,centroid.y)||1;
        memberIndexes.push(records.length);
        records.push({vertices,centroid,normal:{x:centroid.x/radial,y:centroid.y/radial,z:0},
            areaMm2:0.1,neighbors:[],sourceTriangleIndex:records.length});
    };
    for(let row=0;row<rows;row++)for(let segment=0;segment<segments;segment++){
        const a=segment*2*Math.PI/segments,b=(segment+1)*2*Math.PI/segments;
        const z=row*8/rows,next=(row+1)*8/rows;
        add([point(a,z),point(b,z),point(b,next)]);
        add([point(a,z),point(b,next),point(a,next)]);
    }
    const cluster={memberIndexes,radialMinimumMm:5,radialMaximumMm:5.5,
        radialNormalSign:1,radialFitDeviationRatio:0.001,normalConsistency:0.2,
        evidence:{id:'external-helix',type:'cylindrical',axis,centroid:center,radiusMm:5.25,
            axialDepthMm:8,areaMm2:records.length*0.1,confidence:'Medium'}};
    const proxies=c.CncFeatureProxies([cluster],{eligible:true,axis,diameterMm:11,lengthMm:8,
        circularFaceCoverage:0.9,radialDeviationRatio:0.001},center,records,[axis],[]);
    assert.equal(proxies.threads.length,1);
    assert.equal(proxies.threads[0].isInternal,false);
    assert.ok(Math.abs(proxies.threads[0].pitchMm-pitch)<0.02,JSON.stringify(proxies.threads[0]));
});

test('a rotational external thread transitively owns its tessellated flank and root fragments',async()=>{
    const {c}=await plate(), axis={x:0,y:0,z:1}, center={x:0,y:0,z:0};
    const cluster=(id,type,minimum,maximum,adjacent)=>({memberIndexes:[],
        radialMinimumMm:minimum,radialMaximumMm:maximum,radialNormalSign:1,
        radialFitDeviationRatio:0,normalConsistency:.2,evidence:{id,type,axis,
            centroid:center,radiusMm:(minimum+maximum)/2,axialDepthMm:12,areaMm2:100,
            adjacentClusterIds:adjacent,isInternal:false,confidence:'Medium'}});
    const clusters=[cluster('thread-crests','cylindrical',6.2,7,['thread-flank']),
        cluster('thread-flank','freeform',6,6.8,['thread-crests','thread-root']),
        cluster('thread-root','freeform',5.8,6.2,['thread-flank'])];
    const proxies=c.CncFeatureProxies(clusters,{eligible:true,axis,diameterMm:20,lengthMm:20,
        circularFaceCoverage:.9,radialDeviationRatio:0},center,null,[axis],[]);
    assert.equal(proxies.threads.length,1);
    assert.deepEqual(Array.from(proxies.threads[0].surfaceClusterIds).sort(),
        ['thread-crests','thread-flank','thread-root']);
    assert.ok(clusters.every(item=>item.evidence.featureType==='thread'));
});

test('coaxial stepped internal cylinders without helical phase are not invented as a thread',async()=>{
    const {c}=await plate(), axis={x:0,y:0,z:1}, center={x:0,y:0,z:0};
    const rings=[4,4.5,5].map((radius,index)=>({memberIndexes:[],radialMinimumMm:radius,
        radialMaximumMm:radius,radialNormalSign:-1,radialFitDeviationRatio:0,
        normalConsistency:0,evidence:{id:'internal-ring-'+index,type:'cylindrical',axis,
            centroid:center,radiusMm:radius,axialDepthMm:8,areaMm2:100,isInternal:true,
            confidence:'High'}}));
    const proxies=c.CncFeatureProxies(rings,{eligible:true,axis,diameterMm:30,lengthMm:20,
        circularFaceCoverage:0.9,radialDeviationRatio:0},center,null,[axis],[]);
    assert.equal(proxies.threads.length,0);
});

test('a verified convex 90 degree edge cone is owned by chamfering',async()=>{
    const {c}=await plate(), axis={x:0,y:0,z:1}, center={x:0,y:0,z:0};
    const records=[], memberIndexes=[], segments=24;
    const point=(radius,z,angle)=>({x:radius*Math.cos(angle),y:radius*Math.sin(angle),z});
    for(let segment=0;segment<segments;segment++){
        const a=segment*2*Math.PI/segments,b=(segment+1)*2*Math.PI/segments;
        const triangles=[[point(5,0,a),point(6,1,a),point(6,1,b)],
            [point(5,0,a),point(6,1,b),point(5,0,b)]];
        for(const vertices of triangles){
            const centroid=vertices.reduce((sum,p)=>({x:sum.x+p.x/3,y:sum.y+p.y/3,z:sum.z+p.z/3}),{x:0,y:0,z:0});
            const radial=Math.hypot(centroid.x,centroid.y)||1;
            memberIndexes.push(records.length);
            records.push({vertices,centroid,normal:{x:centroid.x/radial/Math.SQRT2,
                y:centroid.y/radial/Math.SQRT2,z:-1/Math.SQRT2},areaMm2:1,
                neighbors:[],sourceTriangleIndex:records.length});
        }
    }
    const cluster={memberIndexes,radialMinimumMm:5,radialMaximumMm:6,radialNormalSign:1,
        radialFitDeviationRatio:0,normalConsistency:0,evidence:{id:'external-edge-cone',
            type:'conical',axis,centroid:{x:0,y:0,z:.5},radiusMm:5.5,axialDepthMm:1,
            areaMm2:records.length,confidence:'High'}};
    const proxies=c.CncFeatureProxies([cluster],{eligible:false},center,records,[axis],
        [{kind:'cone',axis,centerMm:center,radiusMm:5,halfAngleRadians:Math.PI/4}]);
    assert.equal(proxies.chamfers.length,1);
    assert.equal(cluster.evidence.featureType,'chamfer');
});
