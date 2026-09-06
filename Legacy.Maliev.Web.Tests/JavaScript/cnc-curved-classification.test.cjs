const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot');
const axes = [{x:1,y:0,z:0},{x:0,y:1,z:0},{x:0,y:0,z:1}];

function runtime() {
    const c = vm.createContext({console}); c.self = c;
    vm.runInContext(fs.readFileSync(path.join(root,
        'src/app/js/cnc-quotation/cnc-geometry.worker.js'), 'utf8'), c);
    return c;
}
function curvedStrip() {
    const triangles = [];
    for (let i = 0; i < 16; i++) {
        const a = i * Math.PI / 32, b = (i + 1) * Math.PI / 32;
        const p = [10*Math.cos(a),0,10*Math.sin(a)];
        const q = [10*Math.cos(b),0,10*Math.sin(b)];
        const r = [q[0],30,q[2]], s = [p[0],30,p[2]];
        triangles.push(...p,...q,...r,...p,...r,...s);
    }
    return new Float32Array(triangles);
}
function evidence(c, triangles, visibility) {
    const analysis = c.CncSurfaceAnalysis(triangles, axes);
    for (const cluster of analysis.clusters) {
        cluster.evidence.accessibleTriangleIndexesByDirection = visibility || {
            'positive-z': cluster.memberIndexes.map(i => analysis.records[i].sourceTriangleIndex)
        };
    }
    if (c.CncApplyCurvedFinishingEvidence) c.CncApplyCurvedFinishingEvidence(analysis, axes);
    return analysis;
}

test('curved cross-axis shell exposes ball-finishing regions without changing its coarse type', () => {
    const c = runtime(), analysis = evidence(c, curvedStrip());
    assert.equal(analysis.clusters.length, 1);
    const cluster = analysis.clusters[0].evidence;
    assert.ok(cluster.curvedFinishingByDirection?.['positive-z']?.triangleIndexes.length > 20);
    assert.ok(cluster.curvedFinishingByDirection['positive-z'].areaMm2 > 300);
    assert.equal(cluster.curvedFinishingByDirection['positive-z'].camCertain, false);
    assert.equal(cluster.type, 'unresolved');
});

test('curved evidence excludes wholly hidden and individually occluded triangles', () => {
    const c = runtime();
    const hidden = evidence(c, curvedStrip(), {'positive-z': []}).clusters[0].evidence;
    assert.ok(hidden.curvedFinishingByDirection);
    assert.deepEqual(JSON.parse(JSON.stringify(hidden.curvedFinishingByDirection)), {});
    const partial = evidence(c, curvedStrip(), {'positive-z': [8,9,10,11,12,13]}).clusters[0].evidence;
    assert.deepEqual(Array.from(partial.curvedFinishingByDirection['positive-z'].triangleIndexes).sort((a,b) => a-b), [8,9,10,11,12,13]);
    // Three chord-width rectangular strips, each 30 mm long; the worker must
    // count only those six visible triangles, not the whole curved surface.
    assert.ok(Math.abs(partial.curvedFinishingByDirection['positive-z'].areaMm2
        - 3 * 20 * Math.sin(Math.PI / 64) * 30) < 0.001);
});

test('tilted planar faces and tangent prismatic contours do not request ball finishing', () => {
    const c = runtime();
    const plane = evidence(c, new Float32Array([0,0,0,10,0,10,10,10,10,0,0,0,10,10,10,0,10,0]));
    assert.ok(plane.clusters.every(cluster => cluster.evidence.curvedFinishingByDirection));
    assert.ok(plane.clusters.every(cluster => Object.keys(cluster.evidence.curvedFinishingByDirection).length === 0));
    const prism = evidence(c, curvedStrip(), {'positive-y': Array.from({length:32}, (_,i) => i)});
    assert.deepEqual(JSON.parse(JSON.stringify(prism.clusters[0].evidence.curvedFinishingByDirection)), {});
});

for (const [radii, expectedDiameter] of [[[2,.5],4],[[.5],1],[[],6]]) {
    test(`general curves reuse required fillet tools ${radii.join('/')} before adding a new ball diameter`, () => {
        const c = runtime();
        const cluster = {id:'curve',filletFeatures:radii.map(radiusMm=>({radiusMm,accessibleDirectionIds:['positive-z']})),
            curvedFinishingByDirection:{'positive-z':{triangleIndexes:[1],method:'triangle-normal-variation',camCertain:false}}};
        const field = {surfaceSamples:[{id:10,clusterId:'curve',sourceTriangleIndex:1}],toolAccess:{}};
        // At this consumer boundary, all candidate contacts are independently
        // supplied as clear. The assertion tests scheduling/tool reuse, not CAD.
        const verifier = {contact:()=>true,generalHandoff:()=>null};
        const result = c.CncGeneralBallEvidence([], [cluster],field,axes,verifier,
            [6,4,1].map(diameterMm=>({id:'ball-'+diameterMm,diameterMm})),[]);
        assert.deepEqual(Array.from(result.finishingAccess).map(entry=>entry.toolId),['ball-'+expectedDiameter]);
    });
}

test('required ball-tool reuse never bypasses contact or introduces an unneeded one-millimeter tool', () => {
    const c = runtime();
    const cluster = {id:'curve',filletFeatures:[{radiusMm:2,accessibleDirectionIds:['positive-z']}],
        curvedFinishingByDirection:{'positive-z':{triangleIndexes:[1],method:'triangle-normal-variation',camCertain:false}}};
    const field = {surfaceSamples:[{id:10,clusterId:'curve',sourceTriangleIndex:1}],toolAccess:{}};
    const verifier = {contact:(_sample,_direction,ball)=>ball.diameterMm===1,generalHandoff:()=>null};
    const result = c.CncGeneralBallEvidence([], [cluster],field,axes,verifier,
        [6,4,1].map(diameterMm=>({id:'ball-'+diameterMm,diameterMm})),[]);
    assert.equal(result.finishingAccess.length,0,'unreachable D4 stays unproven; D1 is not a new generic fallback');
    cluster.filletFeatures.push({radiusMm:.5,accessibleDirectionIds:['positive-z']});
    const withRequiredSmallTool = c.CncGeneralBallEvidence([], [cluster],field,axes,verifier,
        [6,4,1].map(diameterMm=>({id:'ball-'+diameterMm,diameterMm})),[]);
    assert.deepEqual(Array.from(withRequiredSmallTool.finishingAccess).map(entry=>entry.toolId),['ball-1']);
});

test('a verified-radius blend with blocked physical ball contact cannot contaminate the existing fillet pass', () => {
    const c = runtime();
    const records = [0,1].map(index=>({sourceTriangleIndex:index,areaMm2:1,normal:{x:0,y:0,z:1},
        centroid:{x:index+.25,y:.25,z:0},vertices:[{x:index,y:0,z:0},{x:index+1,y:0,z:0},{x:index,y:1,z:0}]}));
    const cluster = {id:'pocket',triangleIndexes:[0,1],filletFeatures:[{radiusMm:2,
        triangleIndexes:[0],areaMm2:1,accessibleDirectionIds:['positive-z']}],
        accessibleTriangleIndexesByDirection:{'positive-z':[0,1]},curvedFinishingByDirection:{
            'positive-z':{triangleIndexes:[1],areaMm2:1,method:'triangle-normal-variation',camCertain:false}}};
    const verifier = {concaveRadiusMm:()=>2,contact:sample=>sample.contactPosition.x!==2};
    c.CncExtendVerifiedFilletRegions(records,[cluster],verifier,axes,[{id:'physical-ball4',diameterMm:4}]);
    assert.deepEqual(Array.from(cluster.filletFeatures[0].triangleIndexes),[0],
        'one blocked trim vertex must leave the entire added blend outside the proven legacy region');
    assert.deepEqual(Array.from(cluster.curvedFinishingByDirection['positive-z'].triangleIndexes),[1]);
});

test('actual handle outer shells qualify from both primary setups, not the side recess profile', async () => {
    const c = runtime();
    const occt = await require(path.join(root, 'lib/occt/occt-import-js.js'))({
        wasmBinary: fs.readFileSync(path.join(root, 'lib/occt/occt-import-js.wasm'))
    });
    const model = occt.ReadStepFile(fs.readFileSync(path.resolve(__dirname,
        '../TestAssets/Cnc/handle-counterbore-recesses.step')), null);
    assert.equal(model.success, true);
    const triangles = [];
    for (const mesh of model.meshes) for (const i of mesh.index.array) {
        triangles.push(...mesh.attributes.position.array.slice(i*3,i*3+3));
    }
    const geometry = c.AnalyzeCncGeometry(new Float32Array(triangles), {bodyCount:1});
    for (const id of ['surface-11','surface-12']) {
        const cluster = geometry.surfaceClusters.find(entry => entry.id === id);
        for (const direction of ['positive-z','negative-z']) {
            assert.ok(cluster.curvedFinishingByDirection?.[direction]?.areaMm2 > 100,
                `${id} needs curved finishing from ${direction}`);
            assert.ok(cluster.curvedFinishingByDirection[direction].triangleIndexes.every(index =>
                cluster.accessibleTriangleIndexesByDirection[direction].includes(index)));
        }
    }
    for (const id of ['surface-4','surface-10']) {
        const cluster = geometry.surfaceClusters.find(entry => entry.id === id);
        assert.equal(cluster.curvedFinishingByDirection?.['positive-y'], undefined,
            'axial counterbore/recess walls remain flat-tool profile work');
    }
});

test('verified bracket blend faces extend existing radius-matched fillets instead of duplicate general finishing', async () => {
    const c = runtime();
    c.window = c;
    for (const name of ['cnc-quotation-config','cnc-material-catalog','cnc-tool-library',
        'cnc-cad-surfaces.worker','cnc-ball-rest.worker']) {
        vm.runInContext(fs.readFileSync(path.join(root,'src/app/js/cnc-quotation',name+'.js'),'utf8'),c);
    }
    const occt = await require(path.join(root,'lib/occt/occt-import-js.js'))({
        wasmBinary:fs.readFileSync(path.join(root,'lib/occt/occt-import-js.wasm'))
    });
    const source = fs.readFileSync(path.resolve(__dirname,'../TestAssets/Cnc/counterbore-pocket-bracket.step'));
    const model = occt.ReadStepFile(source,null), triangles = [], faceRanges = [];
    assert.equal(model.success,true);
    let offset = 0;
    for (const mesh of model.meshes) {
        for (const face of mesh.brep_faces) faceRanges.push({first:face.first+offset,last:face.last+offset});
        offset += mesh.index.array.length/3;
        for (const index of mesh.index.array) triangles.push(...mesh.attributes.position.array.slice(index*3,index*3+3));
    }
    const analysis = c.CncSurfaceAnalysis(new Float32Array(triangles),axes);
    c.CncApplyDirectionalVisibility(analysis,axes);
    c.CncApplyCurvedFinishingEvidence(analysis,axes);
    c.CncFeatureProxies(analysis.clusters,{},analysis.origin,analysis.records,axes);
    const clusters = analysis.clusters.map(entry=>entry.evidence);
    const originalDirections = clusters.map(entry=>Array.from(entry.filletFeatures || [])
        .map(feature=>Array.from(feature.accessibleDirectionIds || [])));
    const verifier = c.CncBallRest.createVerifier(analysis.records,c.CncCadSurfaces.parseStep(source.toString()),faceRanges);
    const cluster = clusters.find(entry=>entry.id==='surface-26');
    const originalRadii = Array.from(cluster.filletFeatures).map(feature=>feature.radiusMm);
    const originalTriangles = cluster.filletFeatures.map(feature=>Array.from(feature.triangleIndexes));
    const originalGeneral = new Set(cluster.curvedFinishingByDirection['positive-x'].triangleIndexes);
    if (c.CncExtendVerifiedFilletRegions) c.CncExtendVerifiedFilletRegions(analysis.records,clusters,verifier,axes,
        [1,4].map(diameter=>c.CncToolLibrary.ballRestTool(diameter)));
    assert.deepEqual(clusters.map(entry=>Array.from(entry.filletFeatures || [])
        .map(feature=>Array.from(feature.accessibleDirectionIds || []))),originalDirections,
    'expanding blend ownership must not steal an existing setup direction');
    assert.ok(cluster.filletFeatures.find(feature=>Math.abs(feature.radiusMm-.5)<.001).triangleIndexes.length>12,
        'fully clear R0.5 blend triangles continue the existing D1 finishing region');
    assert.deepEqual(Array.from(cluster.filletFeatures).map(feature=>feature.radiusMm),originalRadii,
        'do not invent or change matched feature radii');
    cluster.filletFeatures.forEach((feature,i)=>{
        assert.ok(originalTriangles[i].every(index=>feature.triangleIndexes.includes(index)),
            'the original legacy strip remains intact');
        for (const index of feature.triangleIndexes) if (!originalTriangles[i].includes(index)) {
            const record = analysis.records.find(record=>record.sourceTriangleIndex===index);
            const tool = c.CncToolLibrary.ballRestTool(Math.round(feature.radiusMm*2));
            for (const point of [record.centroid,...record.vertices]) {
                assert.ok(verifier.contact({sourceTriangleIndex:index,contactPosition:point,normal:record.normal},axes[0],tool),
                    'each added blend vertex and centroid must have physical matched-ball clearance');
            }
        }
    });
    const retained = new Set(cluster.curvedFinishingByDirection['positive-x'].triangleIndexes);
    for (const index of originalGeneral) if (!retained.has(index)) {
        const radius = verifier.concaveRadiusMm(index);
        assert.ok(radius && originalRadii.some(expected=>Math.abs(expected-radius)<.001));
        assert.ok(cluster.filletFeatures.some(feature=>feature.triangleIndexes.includes(index)));
    }
    assert.ok(retained.size>0,'unmatched B-spline and unrelated-radius faces are not silently swallowed');
    assert.ok(Array.from(retained).every(index=>cluster.accessibleTriangleIndexesByDirection['positive-x'].includes(index)));
});
