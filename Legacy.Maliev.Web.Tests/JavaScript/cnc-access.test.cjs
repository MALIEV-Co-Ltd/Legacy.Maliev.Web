const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function runtime() {
    const context = vm.createContext({ console });
    context.window = context;
    context.self = context;
    for (const name of ['cnc-quotation-config', 'cnc-material-catalog', 'cnc-tool-library',
        'cnc-reach', 'cnc-fixture-clearance', 'cnc-machine-capability', 'cnc-planning',
        'cnc-geometry.worker']) {
        const file = name === 'cnc-planning'
            ? path.resolve(__dirname, 'fixtures/cnc-legacy-planning.test-helper.js')
            : path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation', name + '.js');
        let source = fs.readFileSync(file, 'utf8');
        if (name === 'cnc-planning') source = source.replace('/* CNC_TEST_INSTRUMENTATION_POINT */',
            'window.CncPlanningDiagnostics = Object.freeze({ plan: plan, planFixture: planFixture });');
        vm.runInContext(source, context, { filename: file });
    }
    return context;
}

const x = { x: 1, y: 0, z: 0 };
const y = { x: 0, y: 1, z: 0 };
const z = { x: 0, y: 0, z: 1 };

for (const [name, visible, expected] of [
    ['confirmed occlusion', [], []],
    ['partial visibility', [20], [2]],
    ['missing projection', undefined, [1, 2]]
]) {
    test('field flute coverage respects ' + name + ' on trusted CAD triangles', () => {
        const c = runtime();
        const result = c.CncReach.evaluate({ material: '6061',
            tools: [{ id: 'flat', family: 'flat_end_mill', diameterMm: 4,
                usableCutLengthMm: 20, reachMm: 30, holderDiameterMm: 20,
                operations: ['roughing'], materials: ['metal'] }],
            setups: [{ id: 'front', number: 1, direction: z }],
            geometry: { surfaceClusters: [{ id: 'recess', type: 'planar', normal: x,
                areaMm2: 20, operationCodes: ['roughing'],
                accessibleTriangleIndexesByDirection: { front: visible } }],
            accessibilityField: { surfaceSamples: [
                { id: 1, clusterId: 'recess', areaMm2: 10, sourceTriangleIndex: 10 },
                { id: 2, clusterId: 'recess', areaMm2: 10, sourceTriangleIndex: 20 }],
                toolAccess: { front: { flat: {
                    reachableSampleIds: [1, 2], fluteSampleIds: [1, 2], tipSampleIds: [] } } } } }
        });
        const record = result.records[0];
        assert.deepEqual(Array.from(record.reachableSampleIds), expected);
        assert.equal(record.reachable, expected.length > 0);
    });
}

test('tool contact is projected onto the CAD triangle rather than the occupied voxel centre', () => {
    const c = runtime();
    const analysis = c.CncSurfaceAnalysis(new Float32Array([0,0,0, 10,0,0, 0,10,0]), [x,y,z]);
    const field = { surfaceSamples: [
        { id: 1, position: {x:2,y:3,z:-0.2}, normal:z },
        { id: 2, position: {x:-0.2,y:3,z:-0.2}, normal:z }
    ] };
    c.CncAssignFieldSamplesToClusters(field, [{id:'floor', triangleIndexes:[0]}], analysis.records);
    assert.deepEqual(JSON.parse(JSON.stringify(field.surfaceSamples.map(s=>s.contactPosition))),
        [{x:2,y:3,z:0},{x:0,y:3,z:0}]);
    assert.equal(field.surfaceSamples[0].position.z, -0.2, 'retain stable voxel evidence');
});

function pocketPatch(radius, offset, convex = false) {
    const triangles = [];
    for (let i = 0; i < 12; i++) {
        const a = i * Math.PI / 24, b = (i + 1) * Math.PI / 24;
        const p = [offset + radius * Math.cos(a), radius * Math.sin(a), 0];
        const q = [p[0], p[1], 10];
        const r = [offset + radius * Math.cos(b), radius * Math.sin(b), 10];
        const s = [r[0], r[1], 0];
        triangles.push(...p, ...(convex ? r : q), ...(convex ? q : r),
            ...p, ...(convex ? s : r), ...(convex ? r : s));
    }
    // Tangent pocket wall shares the first axial seam, so the smooth-component
    // detector merges planar and curved geometry before local radius recovery.
    const p=[offset+radius,0,0], q=[offset+radius,0,10];
    const a=[offset+radius,-5,10], b=[offset+radius,-5,0];
    triangles.push(...p,...(convex?q:a),...(convex?a:q),...p,...(convex?a:b),...(convex?b:a));
    return triangles;
}

test('local concave fillets retain measured radii inside smooth surface groups', () => {
    const c = runtime();
    const triangles = new Float32Array([...pocketPatch(0.5, 0), ...pocketPatch(2, 5), ...pocketPatch(1, 12, true)]);
    const analysis = c.CncSurfaceAnalysis(triangles, [x, y, z]);
    assert.equal(analysis.clusters.length,3);
    assert.ok(analysis.clusters.every(cluster=>cluster.memberIndexes.length===26));
    c.CncFeatureProxies(analysis.clusters, {}, {x:0,y:0,z:0}, analysis.records);
    const radii = analysis.clusters.flatMap(s => s.evidence.filletFeatures || []).map(f => f.radiusMm).sort((a,b)=>a-b);
    assert.equal(radii.length, 2);
    assert.ok(Math.abs(radii[0] - 0.5) < 0.002);
    assert.ok(Math.abs(radii[1] - 2) < 0.002);
});

test('opposed pocket setups own milling operations and radius-matched ball finishing', () => {
    const c = runtime(), reverse = {x:-1,y:0,z:0};
    const plan = c.CncPlanningDiagnostics.plan({material:'6061',requirements:{quantity:1},
        stock:{stockSizeMm:{x:12,y:55,z:105},confidence:'High'},geometry:{bodyCount:1,
            orientedSizeMm:{x:10,y:50,z:100},partVolumeMm3:30000,partSurfaceAreaMm2:8100,
            orientationCandidates:[{id:'positive-x',toolDirection:x,projectedFaceCoverage:0.5},
                {id:'negative-x',toolDirection:reverse,projectedFaceCoverage:0.5}],
            surfaceClusters:[{id:'front',type:'planar',normal:x,areaMm2:4000,accessibleDirectionIds:['positive-x']},
                {id:'back',type:'planar',normal:reverse,areaMm2:4000,accessibleDirectionIds:['negative-x']},
                {id:'pocket',type:'unresolved',areaMm2:100,accessibleDirectionIds:['negative-x'],
                    filletFeatures:[{radiusMm:0.5,areaMm2:10,accessibleDirectionIds:['negative-x']},
                        {radiusMm:0.5,areaMm2:5,accessibleDirectionIds:['negative-x']},
                        {radiusMm:2,areaMm2:20,accessibleDirectionIds:['negative-x']}]}]}});
    assert.equal(plan.setups.length,2);
    for (const setup of plan.setups) {
        const ops = plan.operations.filter(o=>o.setupNumber===setup.number);
        for (const code of ['facing','roughing','finishing']) assert.ok(ops.some(o=>o.code===code), setup.id+' missing '+code);
        assert.deepEqual(Array.from(setup.operationIds),Array.from(ops,o=>o.id));
    }
    const balls=plan.operations.filter(o=>o.code==='freeform_finishing');
    assert.deepEqual(Array.from(balls,o=>o.toolDiameterMm).sort((a,b)=>a-b),[1,4]);
    assert.ok(balls.every(o=>o.reachable && plan.setups.find(s=>s.number===o.setupNumber).direction.x===-1));
    assert.ok(balls.every(o=>o.cuttingMinutes>0));
    const stockRemoval=plan.operations.filter(o=>['roughing','facing'].includes(o.code));
    assert.ok(Math.abs(stockRemoval.reduce((sum,o)=>sum+o.cuttingMinutes,0)-plan.roughingMinutes)<1e-9);
    assert.ok(plan.setups.every(s=>Math.abs(s.minutes-43-plan.operations.filter(o=>o.setupNumber===s.number)
        .reduce((sum,o)=>sum+o.minutes,0))<1e-9));
    assert.equal(new Set(plan.operations.map(o=>o.id)).size,plan.operations.length);
});

test('side pocket fillets use their owning setup rather than the first candidate axis', () => {
    const c=runtime(), reverse={x:-1,y:0,z:0};
    const plan=c.CncPlanningDiagnostics.plan({material:'6061',requirements:{quantity:1},
        stock:{stockSizeMm:{x:55,y:55,z:105},confidence:'High'},geometry:{bodyCount:1,
            orientedSizeMm:{x:50,y:50,z:100},partVolumeMm3:100000,partSurfaceAreaMm2:10000,
            orientationCandidates:[{id:'positive-x',toolDirection:x,projectedFaceCoverage:0.4},
                {id:'negative-x',toolDirection:reverse,projectedFaceCoverage:0.4},
                {id:'positive-y',toolDirection:y,projectedFaceCoverage:0.2}],
            surfaceClusters:[{id:'front',type:'planar',normal:x,areaMm2:4000,accessibleDirectionIds:['positive-x']},
                {id:'back',type:'planar',normal:reverse,areaMm2:4000,accessibleDirectionIds:['negative-x']},
                {id:'pocket',type:'unresolved',areaMm2:2000,accessibleDirectionIds:['positive-y'],
                    filletFeatures:[{radiusMm:2,axis:x,areaMm2:100,accessibleDirectionIds:['positive-y']}]}]}});
    const ball=plan.operations.find(o=>o.code==='freeform_finishing');
    assert.ok(ball,'Missing side-pocket ball finishing');
    assert.equal(ball.toolDiameterMm,4);
    assert.ok(ball.reachable);
    assert.equal(plan.setups.find(s=>s.number===ball.setupNumber).id,'positive-y');
});

test('discarded side candidates cannot leave orphan operations under renumbered primary setups', () => {
    const c=runtime(), reverse={x:-1,y:0,z:0};
    const access=ids=>Object.fromEntries(Array.from(c.CncToolLibrary.analysisProfiles(),tool=>[tool.id,
        {reachableSampleIds:ids,tipSampleIds:ids,fluteSampleIds:[],reachableAreaMm2:ids.length*100}]));
    const plan=c.CncPlanningDiagnostics.plan({material:'6061',requirements:{quantity:1},
        stock:{stockSizeMm:{x:12,y:55,z:105},confidence:'High'},geometry:{bodyCount:1,boxFillRatio:0.6,
            orientedSizeMm:{x:10,y:50,z:100},partVolumeMm3:30000,partSurfaceAreaMm2:8100,
            orientationCandidates:[{id:'positive-y',toolDirection:y,projectedFaceCoverage:0.1},
                {id:'positive-x',toolDirection:x,projectedFaceCoverage:0.5},
                {id:'negative-x',toolDirection:reverse,projectedFaceCoverage:0.5}],
            surfaceClusters:[{id:'front',type:'planar',normal:x,areaMm2:4000,operationCodes:['facing','roughing','finishing']},
                {id:'back',type:'planar',normal:reverse,areaMm2:4000,operationCodes:['facing','roughing','finishing']},
                {id:'pocket',type:'unresolved',areaMm2:100,operationCodes:['roughing','finishing']}],
            accessibilityField:{surfaceSamples:[{id:1,clusterId:'front',areaMm2:4000},
                {id:2,clusterId:'back',areaMm2:4000},{id:3,clusterId:'pocket',areaMm2:100}],
                toolAccess:{'positive-x':access([1]),'negative-x':access([2]),'positive-y':access([3])}}}});
    assert.equal(plan.setups.length,2);
    for(const op of plan.operations)assert.ok(plan.setups.find(s=>s.number===op.setupNumber).operationIds.includes(op.id),op.id+' has no owner');
});

test('an occluded transverse fillet remains an explicit unreachable operation', () => {
    const c=runtime();
    const plan=c.CncPlanningDiagnostics.plan({material:'6061',requirements:{quantity:1},
        stock:{stockSizeMm:{x:12,y:55,z:105},confidence:'High'},geometry:{bodyCount:1,
            orientedSizeMm:{x:10,y:50,z:100},partVolumeMm3:30000,partSurfaceAreaMm2:8000,
            orientationCandidates:[{id:'positive-x',toolDirection:x,projectedFaceCoverage:0.5},
                {id:'negative-x',toolDirection:{x:-1,y:0,z:0},projectedFaceCoverage:0.5}],
            surfaceClusters:[{id:'front',type:'planar',normal:x,areaMm2:4000},
                {id:'pocket',type:'planar',normal:{x:-1,y:0,z:0},areaMm2:4000,
                    filletFeatures:[{radiusMm:2,axis:y,areaMm2:10,accessibleDirectionIds:[]}]}]}});
    const ball=plan.operations.find(o=>o.code==='freeform_finishing');
    assert.ok(ball,'Detected fillet was silently dropped');
    assert.equal(ball.reachable,false);
    assert.ok(plan.reviewReasons.includes('unreachable_tool_access'));
});

test('display triangles do not invent an internal thread in the real axial M14 fixture', async () => {
    const occtPath=path.resolve(__dirname,'../../Legacy.Maliev.Web/wwwroot/lib/occt');
    const occt=await require(path.join(occtPath,'occt-import-js.js'))({
        wasmBinary:fs.readFileSync(path.join(occtPath,'occt-import-js.wasm'))});
    const model=occt.ReadStepFile(fs.readFileSync(path.resolve(__dirname,'../TestAssets/Cnc/axial-threaded-part.step')),null);
    const triangles=[];
    for(const mesh of model.meshes) for(const i of mesh.index.array)
        triangles.push(...mesh.attributes.position.array.slice(i*3,i*3+3));
    const c=runtime(),geometry=c.AnalyzeCncGeometry(triangles,{bodyCount:1,volume:6000});
    assert.equal(geometry.threadProxies.length,0);
    const hole=geometry.holeProxies[0];
    assert.ok(Math.abs(hole.diameterMm-9)<0.02);
    // The modeled M14 body has a real through bore, but triangle analysis remains diagnostic
    // and cannot authorize either the external thread or a semantic manufacturing plan.
    assert.equal(hole.entryDirections.length,2);
    assert.deepEqual(Array.from(hole.entryDirections,entry=>entry.z).sort(),[-1,1]);
    assert.ok(Math.abs(hole.depthMm-39)<0.001);
    assert.equal(geometry.manufacturingFeatureGraph,undefined,
        'display triangles remain diagnostic and cannot authorize manufacturing operations');
});
function holeReach(context, direction, entries) {
    return context.CncReach.evaluate({
        material: '6061',
        tools: [{ id: 'drill', family: 'drill', diameterMm: 6.6, usableCutLengthMm: 20,
            reachMm: 35, shankDiameterMm: 6.6, holderDiameterMm: 20,
            enabled: true, operations: ['drilling'], materials: ['metal'], confidence: 'High' }],
        setups: [{ id: 'test', number: 1, direction }],
        geometry: {
            surfaceClusters: [{ id: 'hole', type: 'cylindrical', axis: y, featureAxis: x,
                operationCodes: ['drilling'], openingWidthMm: 6.6, requiredDepthMm: 5,
                featureEntryDirections: entries, confidence: 'High' }],
            accessibilityField: { surfaceSamples: [{ id: 1, clusterId: 'hole', areaMm2: 1 }],
                toolAccess: { test: { drill: { reachableSampleIds: [1], tipSampleIds: [1] } } } }
        }
    }).records[0];
}

test('surface visibility cannot authorize drilling perpendicular to the feature axis', () => {
    const record = holeReach(runtime(), y, [x]);
    assert.equal(record.reachable, false);
    assert.equal(record.limitingFactor, 'orientation');
});

test('a short thread does not authorize a pilot beyond the drill cutting length', () => {
    const c=runtime(), reverse={x:0,y:0,z:-1};
    const plan=c.CncPlanningDiagnostics.plan({material:'6061',
        stock:{stockSizeMm:{x:25,y:25,z:85},confidence:'High'},requirements:{quantity:1},
        geometry:{bodyCount:1,orientedSizeMm:{x:20,y:20,z:80},partVolumeMm3:20000,
            partSurfaceAreaMm2:8000,geometryConfidence:'High',
            orientationCandidates:[{id:'positive-z',toolDirection:z,projectedFaceCoverage:0.5},
                {id:'negative-z',toolDirection:reverse,projectedFaceCoverage:0.5}],
            holeProxies:[{id:'h',surfaceClusterIds:['h'],axis:z,diameterMm:6.6,depthMm:70,entryDirections:[z,reverse]}],
            threadProxies:[{id:'t',surfaceClusterIds:['h'],axis:z,majorDiameterMm:8,minorDiameterMm:6.6,axialDepthMm:5}],
            surfaceClusters:[{id:'front',type:'planar',normal:z,areaMm2:1000},
                {id:'back',type:'planar',normal:reverse,areaMm2:1000},
                {id:'h',type:'cylindrical',axis:z,areaMm2:1000}]}});
    assert.equal(plan.operations.find(o=>o.code==='drilling').reachable,false);
    assert.equal(plan.operations.find(o=>o.code==='thread_milling').reachable,true);
});

test('a visible bore wall cannot authorize drilling through a blocked entry', () => {
    const record = holeReach(runtime(), x, []);
    assert.equal(record.reachable, false);
    assert.equal(record.limitingFactor, 'body_occlusion');
});

test('verified axial entry takes precedence over unrelated fitted surface axis', () => {
    const record = holeReach(runtime(), x, [x]);
    assert.equal(record.reachable, true);
    assert.equal(record.orientationCosine, 1);
});

test('curved tangent wall is not hidden by an unrelated fitted radial horizon', () => {
    const c = runtime();
    const records = c.CncTriangleRecords([0,0,0, 0,2,0, 0,0,2]);
    const cluster = { memberIndexes: [0], radialFitDeviationRatio: 0,
        evidence: { id: 'wall', type: 'cylindrical', axis: y, centroid: {x:0,y:0,z:10} } };
    c.CncApplyDirectionalVisibility({ records, clusters: [cluster] }, [x,y,z]);
    assert.equal(cluster.evidence.accessibleTriangleIndexesByDirection['positive-z'].length, 1);
});

test('hole entry checks the full bore disk, including an off-centre obstruction', () => {
    const c = runtime();
    // The cap covers half the disk above the hole, but does not cover its centre.
    const records = c.CncTriangleRecords([0.4,-4,6, 4,-4,6, 4,4,6, 0.4,-4,6, 4,4,6, 0.4,4,6]);
    const entries = c.CncHoleEntryDirections(records, {x:0,y:0,z:0}, z, 3.3, -2, 2);
    assert.equal(entries.length, 1);
    assert.equal(entries[0].z, -1);
});

test('a cylinder horizon is rejected when its fitted axis contradicts the actual face normals', () => {
    const c=runtime();
    // A slightly skewed tessellation of an X-extruded curve, incorrectly fitted to Z.
    const records=c.CncTriangleRecords([0,0,0, 11,0.03,0, 0,1,1]);
    const cluster={memberIndexes:[0],radialFitDeviationRatio:0,
        evidence:{id:'curve',type:'cylindrical',axis:z,centroid:{x:20,y:0,z:0}}};
    c.CncApplyDirectionalVisibility({records,clusters:[cluster]},[x,y,z]);
    assert.equal(cluster.evidence.accessibleTriangleIndexesByDirection['positive-x'].length,1);
});

test('an unobstructed through bore accepts both axial entries', () => {
    const c = runtime();
    const entries = c.CncHoleEntryDirections([], {x:0,y:0,z:0}, z, 3.3, -2, 2);
    assert.equal(entries.length, 2);
});

test('seven same-axis holes share a setup and drilling precedes bulk milling', () => {
    const c = runtime();
    const holes = Array.from({length:7}, (_,n) => ({ id:'h'+n, surfaceClusterIds:['h'+n],
        axis:x, diameterMm:6.6, depthMm:5, entryDirections:[x],
        spotEntryEvidence:{byDiameterMm:{10:[x],12:[x],16:[x]},holderDiameterMm:25,
            holderStartAboveEntryMm:20} }));
    const geometry = { bodyCount:1, orientedSizeMm:{x:10,y:50,z:100},
        partVolumeMm3:30000, partSurfaceAreaMm2:11000, geometryConfidence:'High',
        orientationCandidates:[{id:'positive-x',toolDirection:x,projectedFaceCoverage:0.5},
            {id:'negative-x',toolDirection:{x:-1,y:0,z:0},projectedFaceCoverage:0.4}],
        holeProxies:holes,
        surfaceClusters:[{id:'front',type:'planar',normal:x,areaMm2:4000,confidence:'High'},
            {id:'back',type:'planar',normal:{x:-1,y:0,z:0},areaMm2:4000,confidence:'High'},
            ...holes.map(h=>({id:h.id,type:'cylindrical',axis:x,areaMm2:100,confidence:'High'}))] };
    const plan = c.CncPlanningDiagnostics.plan({geometry,material:'6061',
        stock:{stockSizeMm:{x:12,y:55,z:105},stockVolumeMm3:69300,confidence:'High'},requirements:{quantity:1}});
    const drills = plan.operations.filter(o=>o.code==='drilling');
    assert.equal(drills.length,7);
    assert.equal(new Set(drills.map(o=>o.setupNumber)).size,1);
    assert.ok(drills.every(o=>o.toolDiameterMm===6.6 && o.reachable));
    const codes = plan.operations.filter(o=>o.setupNumber===drills[0].setupNumber).map(o=>o.code);
    assert.ok(codes.indexOf('facing') < codes.indexOf('drilling'));
    assert.ok(codes.lastIndexOf('drilling') < codes.indexOf('roughing'));
    assert.ok(codes.indexOf('roughing') < codes.indexOf('finishing'));
});

test('faceted translated through-bore keeps both entries open at its nominal drill diameter', () => {
    const c = runtime();
    const triangles = [];
    const point = (radius, angle, height) => [10 + radius * Math.cos(angle), -8 + radius * Math.sin(angle), height];
    const quad = (a,b,d,e) => triangles.push(...a,...b,...d,...a,...d,...e);
    for (let n=0;n<26;n++) {
        const a=n*Math.PI*2/26, b=(n+1)*Math.PI*2/26;
        quad(point(3.3,a,0),point(3.3,a,5),point(3.3,b,5),point(3.3,b,0));
        quad(point(10,a,0),point(10,b,0),point(10,b,5),point(10,a,5));
        quad(point(3.3,a,5),point(10,a,5),point(10,b,5),point(3.3,b,5));
        quad(point(3.3,a,0),point(3.3,b,0),point(10,b,0),point(10,a,0));
    }
    const analysis = c.CncSurfaceAnalysis(triangles,[x,y,z]);
    const holes = c.CncFeatureProxies(analysis.clusters,{eligible:false},analysis.origin,analysis.records).holes;
    assert.equal(holes.length,1);
    assert.ok(Math.abs(holes[0].diameterMm-6.6)<0.001);
    assert.equal(holes[0].entryDirections.length,2);
});

test('verified bore uses the actual 6.6 mm drill rather than a representative 1 mm SKU', () => {
    const c = runtime();
    const result = c.CncReach.evaluate({material:'6061',setups:[{id:'top',direction:z}],
        geometry:{surfaceClusters:[{id:'bore',type:'cylindrical',axis:z,featureAxis:z,
            operationCodes:['drilling'],openingWidthMm:6.5999999,requiredDepthMm:5,
            featureEntryDirections:[z]}]}});
    assert.ok(result.records.some(r=>r.reachable&&r.toolId==='hss-drill-6p6'));
    assert.ok(result.records.every(r=>!r.reachable||Math.abs(r.toolDiameterMm-6.6)<0.01));
});

test('tangent contact cannot escape through the cap over a hidden slot wall', () => {
    const c = runtime();
    const records = c.CncTriangleRecords([
        0,0,0, 0,2,0, 0,0,2,
        -2,-2,3, 2,-2,3, 2,4,3, -2,-2,3, 2,4,3, -2,4,3
    ]);
    const wall = {memberIndexes:[0],radialFitDeviationRatio:0,
        evidence:{id:'slot',type:'cylindrical',axis:y,centroid:{x:0,y:0,z:10}}};
    c.CncApplyDirectionalVisibility({records,clusters:[wall]},[x,y,z]);
    assert.equal(wall.evidence.accessibleTriangleIndexesByDirection['positive-z'].length,0);
});

test('page forwards opposed-face coverage from the geometry worker to the planner', () => {
    const c = runtime();
    const page = fs.readFileSync(path.resolve(__dirname,'../../Legacy.Maliev.Web/Pages/InstantQuotation/CNC-Machining.cshtml'),'utf8');
    const start = page.indexOf('function CncGeometryForItem(');
    const end = page.indexOf('\n        function ', start + 1);
    vm.runInContext(page.slice(start,end),c);
    const result = c.CncGeometryForItem({modelInfo:{size:{x:10,y:50,z:100},volume:30000,
        cncGeometry:{topBottomPlanarCoverage:0.525}}});
    assert.equal(result.topBottomPlanarCoverage,0.525);
});

test('holes with a common reverse entry are consolidated instead of taking each first match', () => {
    const c=runtime(), reverse={x:-1,y:0,z:0};
    const holes=[{id:'a',surfaceClusterIds:['a'],axis:x,diameterMm:6.6,depthMm:5,entryDirections:[reverse]},
        {id:'b',surfaceClusterIds:['b'],axis:x,diameterMm:6.6,depthMm:5,entryDirections:[x,reverse]}];
    const plan=c.CncPlanningDiagnostics.plan({material:'6061',requirements:{quantity:1},
        stock:{stockSizeMm:{x:12,y:55,z:105},confidence:'High'},geometry:{bodyCount:1,
            orientedSizeMm:{x:10,y:50,z:100},partVolumeMm3:30000,partSurfaceAreaMm2:10000,
            orientationCandidates:[{id:'positive-x',toolDirection:x,projectedFaceCoverage:0.5},
                {id:'negative-x',toolDirection:reverse,projectedFaceCoverage:0.5}],holeProxies:holes,
            surfaceClusters:[{id:'front',type:'planar',normal:x,areaMm2:4000},
                {id:'back',type:'planar',normal:reverse,areaMm2:4000},
                ...holes.map(h=>({id:h.id,type:'cylindrical',axis:x,areaMm2:100}))]}});
    const drills=plan.operations.filter(o=>o.code==='drilling');
    assert.equal(drills.length,2);
    assert.equal(new Set(drills.map(o=>o.setupNumber)).size,1);
    assert.equal(plan.setups.find(s=>s.number===drills[0].setupNumber).direction.x,-1);
});

test('near-tangent back facets of a curved handle remain beyond the visibility horizon', () => {
    const c=runtime(), triangles=[], centroids=[];
    for (let n=0;n<24;n++) {
        const a=n*Math.PI*2/24,b=(n+1)*Math.PI*2/24;
        const p=[-20,7*Math.cos(a),5*Math.sin(a)],q=[20,7*Math.cos(a),5*Math.sin(a)];
        const r=[20,7*Math.cos(b),5*Math.sin(b)],s=[-20,7*Math.cos(b),5*Math.sin(b)];
        triangles.push(...p,...q,...r,...p,...r,...s);
        centroids.push(p.map((v,i)=>(v+q[i]+r[i])/3),p.map((v,i)=>(v+r[i]+s[i])/3));
    }
    const mesh=new Float32Array(triangles), axes=c.CncChooseAxes(mesh).axes;
    const axisIndex=axes.reduce((best,axis,i)=>Math.abs(axis.z)>Math.abs(axes[best].z)?i:best,0);
    const axis=axes[axisIndex],sign=axis.z>0?1:-1;
    const directionId=(sign>0?'positive-':'negative-')+['x','y','z'][axisIndex];
    const depths=centroids.map(p=>sign*(p[0]*axis.x+p[1]*axis.y+p[2]*axis.z));
    const analysis=c.CncSurfaceAnalysis(mesh,axes);
    c.CncApplyDirectionalVisibility(analysis,axes);
    const visible=analysis.clusters.flatMap(cluster=>cluster.evidence.accessibleTriangleIndexesByDirection[directionId]);
    assert.ok(visible.length>0);
    assert.ok(visible.every(index=>depths[index]>=-0.5));
});

test('flute contact tolerates a local faceting seam but not a real overhanging lip', () => {
    const c=runtime();
    function visibility(lip) {
        const records=c.CncTriangleRecords([
            0,0,0, 11,0.033,0, 0,0,2,
            11,-2,-2, 11,lip,-2, 11,lip,4,
            11,-2,-2, 11,lip,4, 11,-2,4
        ]);
        const cluster={memberIndexes:[0],radialFitDeviationRatio:1,
            evidence:{id:'wall',type:'cylindrical',axis:z,centroid:{x:0,y:0,z:0}}};
        c.CncApplyDirectionalVisibility({records,clusters:[cluster]},[x,y,z]);
        return cluster.evidence.accessibleTriangleIndexesByDirection['positive-x'].length;
    }
    assert.equal(visibility(0.033),1);
    assert.equal(visibility(0.5),0);
});

test('field samples use the nearest CAD patch rather than a distant cluster centroid', () => {
    const c=runtime();
    const records=c.CncTriangleRecords([0,0,0, 0,10,0, 0,0,10, 2,0,0, 2,0,1, 2,1,0]);
    const clusters=[{id:'wall',centroid:{x:0,y:100,z:100},triangleIndexes:[0]},
        {id:'near-centroid',centroid:{x:2,y:0,z:0},triangleIndexes:[1]}];
    const field={cellSizeMm:0.25,surfaceSamples:[{id:1,position:{x:0.05,y:1,z:1},normal:{x:0.707,y:0.707,z:0}}]};
    c.CncAssignFieldSamplesToClusters(field,clusters,records);
    assert.equal(field.surfaceSamples[0].clusterId,'wall');
    assert.equal(field.surfaceSamples[0].normal.x,1);
    assert.equal(field.surfaceSamples[0].normal.y,0);
});

test('back-facing planar patches cannot escape around an edge through offset rays', () => {
    const c=runtime();
    const vertices=[[0,0,0],[2,0,0],[2,2,0],[0,2,0],[0,0,1],[2,0,1],[2,2,1],[0,2,1]];
    const faces=[[0,2,1],[0,3,2],[4,5,6],[4,6,7],[0,1,5],[0,5,4],
        [1,2,6],[1,6,5],[2,3,7],[2,7,6],[3,0,4],[3,4,7]];
    const records=c.CncTriangleRecords(faces.flatMap(face=>face.flatMap(index=>vertices[index])));
    const wall={memberIndexes:[0],evidence:{id:'back',type:'planar',normal:{x:0,y:0,z:-1},centroid:{x:0,y:0,z:0}}};
    c.CncApplyDirectionalVisibility({records,clusters:[wall]},[x,y,z]);
    assert.equal(wall.evidence.accessibleTriangleIndexesByDirection['positive-z'].length,0);
    assert.equal(wall.evidence.accessibleTriangleIndexesByDirection['negative-z'].length,1);
});

test('a tangent planar triangle needs clear interior coverage, not only a visible centroid', () => {
    const c=runtime();
    function visible(withCap) {
        const vertices=[0,0,0,10,0,0,0,0,10];
        // The raised feature hides only the far interior of the target triangle.
        // Its centroid at z=3.33 remains clear; the inset near z=8.67 does not.
        if(withCap)vertices.push(8,-2,7,8,2,7,8,2,12,8,-2,7,8,2,12,8,-2,12);
        const records=c.CncTriangleRecords(vertices);
        const wall={memberIndexes:[0],evidence:{id:'floor',type:'planar',normal:y,centroid:{x:10/3,y:0,z:10/3}}};
        c.CncApplyDirectionalVisibility({records,clusters:[wall]},[x,y,z]);
        return wall.evidence.accessibleTriangleIndexesByDirection['positive-x'].length;
    }
    assert.equal(visible(false),1,'An unobstructed wall must retain flute access');
    assert.equal(visible(true),0,'A partly hidden triangle must not be painted wholly accessible');
});

test('a long tangent triangle cannot hide an occluded corner beyond proportional inset probes', () => {
    const c=runtime();
    const records=c.CncTriangleRecords([
        0,0,0,100,0,0,0,0,100,
        110,-2,95,110,2,95,110,2,110,
        110,-2,95,110,2,110,110,-2,110]);
    const wall={memberIndexes:[0],evidence:{id:'long-floor',type:'planar',normal:y,centroid:{x:100/3,y:0,z:100/3}}};
    c.CncApplyDirectionalVisibility({records,clusters:[wall]},[x,y,z]);
    assert.equal(wall.evidence.accessibleTriangleIndexesByDirection['positive-x'].length,0);
});

test('near-corner tangent probes preserve an unobstructed coplanar tessellation seam', () => {
    const c=runtime();
    const mesh=new Float32Array([0,0,0,100,0,0,100,0,100,0,0,0,100,0,100,0,0,100]);
    const analysis=c.CncSurfaceAnalysis(mesh,[x,y,z]);
    c.CncApplyDirectionalVisibility(analysis,[x,y,z]);
    const visible=analysis.clusters.flatMap(cluster=>cluster.evidence.accessibleTriangleIndexesByDirection['positive-x']);
    assert.deepEqual(Array.from(visible).sort(),[0,1]);
});

test('an open reversed triangle is visible from either side without a solid winding assumption', () => {
    const c=runtime();
    const analysis=c.CncSurfaceAnalysis(new Float32Array([0,0,0,0,10,0,10,0,0]),[x,y,z]);
    c.CncApplyDirectionalVisibility(analysis,[x,y,z]);
    const visible=analysis.clusters[0].evidence.accessibleTriangleIndexesByDirection;
    assert.deepEqual(Array.from(visible['positive-z']),[0]);
    assert.deepEqual(Array.from(visible['negative-z']),[0]);
});

test('an open bent tube keeps visible and tangent patches using its local ray horizon', () => {
    const c=runtime(), centres=[], rings=[], radials=[], triangles=[], triangleRadials=[];
    for(let px=-24;px<=24;px+=4)centres.push([px,8*Math.pow(px/24,2)]);
    for(let index=0;index<centres.length;index++){
        const before=centres[Math.max(0,index-1)],after=centres[Math.min(centres.length-1,index+1)];
        const tx=after[0]-before[0],ty=after[1]-before[1],length=Math.hypot(tx,ty);
        const ring=[],vectors=[];
        for(let segment=0;segment<64;segment++){
            const angle=segment*Math.PI/32;
            const radial={x:-ty/length*Math.cos(angle),y:tx/length*Math.cos(angle),z:Math.sin(angle)};
            vectors.push(radial);ring.push([centres[index][0]+4*radial.x,centres[index][1]+4*radial.y,4*radial.z]);
        }
        rings.push(ring);radials.push(vectors);
    }
    const average=(a,b,c)=>({x:(a.x+b.x+c.x)/3,y:(a.y+b.y+c.y)/3,z:(a.z+b.z+c.z)/3});
    for(let ring=0;ring<rings.length-1;ring++)for(let segment=0;segment<64;segment++){
        const next=(segment+1)%64,a=rings[ring][segment],b=rings[ring+1][segment],d=rings[ring][next],e=rings[ring+1][next];
        triangles.push(...a,...b,...e,...a,...e,...d);
        triangleRadials.push(average(radials[ring][segment],radials[ring+1][segment],radials[ring+1][next]),
            average(radials[ring][segment],radials[ring+1][next],radials[ring][next]));
    }
    const mesh=new Float32Array(triangles),axes=c.CncChooseAxes(mesh).axes;
    const analysis=c.CncSurfaceAnalysis(mesh,axes);
    c.CncApplyDirectionalVisibility(analysis,axes);
    const spatialFile=path.resolve(__dirname,'../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-spatial-field.worker.js');
    vm.runInContext(fs.readFileSync(spatialFile,'utf8'),c,{filename:spatialFile});
    const clusters=analysis.clusters.map(cluster=>cluster.evidence);
    const field=c.CncSpatialField.build(mesh,{axes},{geometryToleranceMm:0.5,minimumCutterDiameterMm:1});
    c.CncAssignFieldSamplesToClusters(field,clusters,analysis.records);
    c.CncSpatialField.classifyToolAccess(field,c.CncToolLibrary.analysisProfiles());
    const axisIndex=axes.reduce((best,axis,index)=>Math.abs(axis.y)>Math.abs(axes[best].y)?index:best,0);
    const axis=axes[axisIndex],direction=axis.y>=0?axis:{x:-axis.x,y:-axis.y,z:-axis.z};
    const directionId=(axis.y>=0?'positive-':'negative-')+['x','y','z'][axisIndex];
    const reachable=new Set(analysis.clusters.flatMap(cluster=>cluster.evidence.accessibleTriangleIndexesByDirection[directionId]));
    const dot=radial=>radial.x*direction.x+radial.y*direction.y+radial.z*direction.z;
    const visible=triangleRadials.map((radial,index)=>({dot:dot(radial),index})).filter(entry=>entry.dot>0.25);
    const horizon=triangleRadials.map((radial,index)=>({dot:dot(radial),index})).filter(entry=>Math.abs(entry.dot)<=0.25);
    assert.equal(visible.filter(entry=>reachable.has(entry.index)).length,visible.length);
    assert.ok(horizon.filter(entry=>reachable.has(entry.index)).length>=horizon.length*0.58);
});

test('a merged contour receives flat finishing instead of an invented ball operation', () => {
    const c=runtime();
    const geometry={bodyCount:1,orientedSizeMm:{x:20,y:50,z:60},partVolumeMm3:10000,
        partSurfaceAreaMm2:2000,orientationCandidates:[{id:'positive-x',toolDirection:x,projectedFaceCoverage:0.5}],
        surfaceClusters:[{id:'slot',type:'freeform',areaMm2:2000,filletFeatures:[],prismaticContourAxis:x,
            accessibleDirectionIds:['positive-x']} ]};
    const plan=c.CncPlanningDiagnostics.plan({geometry,material:'6061',stock:{stockSizeMm:{x:25,y:55,z:65}},requirements:{quantity:1}});
    assert.ok(plan.operations.some(o=>o.code==='finishing'&&o.reachable&&o.clusterIds.includes('slot')));
});

test('a smaller skewed envelope cannot claim the exact parallel machining datums', () => {
    const c=runtime(), angle=2.21*Math.PI/180;
    const supports=[{normal:x,offset:0,area:100},{normal:x,offset:10,area:100},
        {normal:y,offset:0,area:1000},{normal:y,offset:5,area:1000}];
    const exact=c.CncFrameOrientationEvidence(supports,[x,y,z],{volume:100});
    const skewed=c.CncFrameOrientationEvidence(supports,[{x:Math.cos(angle),y:0,z:Math.sin(angle)},y,
        {x:-Math.sin(angle),y:0,z:Math.cos(angle)}],{volume:99});
    assert.equal(c.CncCompareOrientationEvidence(exact,skewed),-1);
});

test('isolated field residue does not add a setup but a small distinct feature still does', () => {
    const c=runtime();
    function plan(distinct) {
        const access=ids=>Object.fromEntries(Array.from(c.CncToolLibrary.analysisProfiles(),t=>[t.id,
            {reachableSampleIds:ids,tipSampleIds:ids,fluteSampleIds:[]} ]));
        return c.CncPlanningDiagnostics.plan({material:'6061',requirements:{quantity:1},stock:{stockSizeMm:{x:22,y:55,z:65}},
            geometry:{bodyCount:2,orientedSizeMm:{x:20,y:50,z:60},partVolumeMm3:10000,partSurfaceAreaMm2:2001,
                orientationCandidates:[{id:'positive-x',toolDirection:x,projectedFaceCoverage:0.5},
                    {id:'negative-x',toolDirection:{x:-1,y:0,z:0},projectedFaceCoverage:0.5},
                    {id:'positive-y',toolDirection:y,projectedFaceCoverage:0.01}],
                surfaceClusters:[{id:'front',type:'planar',normal:x,areaMm2:1001,operationCodes:['finishing']},
                    {id:'back',type:'planar',normal:{x:-1,y:0,z:0},areaMm2:1000,operationCodes:['finishing']},
                    ...(distinct?[{id:'small-pocket',type:'planar',normal:y,areaMm2:1,operationCodes:['finishing']}]:[])],
                accessibilityField:{surfaceSamples:[{id:1,clusterId:'front',areaMm2:1000},
                    {id:2,clusterId:'back',areaMm2:1000},{id:3,clusterId:distinct?'small-pocket':'front',areaMm2:1}],
                    toolAccess:{'positive-x':access([1]),'negative-x':access([2]),'positive-y':access([3])}}}});
    }
    assert.equal(plan(false).setups.length,2);
    assert.equal(plan(true).setups.length,3);
});
