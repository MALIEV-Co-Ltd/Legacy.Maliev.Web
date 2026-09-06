const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { webcrypto } = require('node:crypto');

const moduleRoot = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation');
const plain = value => JSON.parse(JSON.stringify(value));
const z = { x: 0, y: 0, z: 1 };
function runtime() {
    const c = vm.createContext({ console, TextEncoder, crypto: webcrypto });
    c.self = c; c.window = c;
    for (const name of ['cnc-plan-contracts', 'cnc-topology.worker', 'cnc-feature-graph.worker',
        'cnc-quotation-config', 'cnc-material-catalog', 'cnc-tool-library', 'cnc-process-compiler',
        'cnc-stock', 'cnc-spatial-field.worker', 'cnc-fixture-catalog',
        'cnc-manufacturing-evidence.worker', 'cnc-setup-planner', 'cnc-plan-validator.worker']) {
        vm.runInContext(fs.readFileSync(path.join(moduleRoot, name + '.js'), 'utf8'), c, { filename: name });
    }
    return c;
}
function physicalGuards(c) {
    // Exercise the actual private guards used by validateInternal, without
    // replacing their implementations or adding production test-only exports.
    const source = fs.readFileSync(path.join(moduleRoot, 'cnc-plan-validator.worker.js'), 'utf8')
        .replace('root.CncPlanValidator =', 'root.physicalGuards = { completeTool, canonicalSemanticPath }; root.CncPlanValidator =');
    vm.runInContext(source, c);
    return c.physicalGuards;
}
function face(id, points, normal, kind = 'plane') {
    const vertices = points.map(p => ({ x: p[0], y: p[1], z: p[2] }));
    return { id, bodyId: 'body-1', orientation: 'forward', adjacentFaceIds: [],
        loops: [{ vertices }], surface: { kind, axis: normal, normal,
            centerMm: vertices[0], boundsMm: {
                min: Object.fromEntries(['x', 'y', 'z'].map(k => [k, Math.min(...vertices.map(p => p[k]))])),
                max: Object.fromEntries(['x', 'y', 'z'].map(k => [k, Math.max(...vertices.map(p => p[k]))])) } } };
}
function topology(faces) {
    const owners = new Map();
    for (const f of faces) for (const loop of f.loops) loop.vertices.forEach((p, i, points) => {
        const key = [JSON.stringify(p), JSON.stringify(points[(i + 1) % points.length])].sort().join('|');
        if (!owners.has(key)) owners.set(key, []);
        owners.get(key).push(f);
    });
    for (const group of owners.values()) for (const f of group) {
        f.adjacentFaceIds = [...new Set(f.adjacentFaceIds.concat(group.filter(g => g !== f).map(g => g.id)))].sort();
    }
    return { contract: 'CncCadTopology.v1', revision: 'analytic-fixture-v1', sourceKind: 'brep',
        automaticPlanningEligible: true, unresolvedReasons: [], bodies: [{ id: 'body-1' }], faces,
        edges: [...owners].map(([id, group]) => ({ id, faceIds: group.map(f => f.id) })) };
}
function prismFaces() {
    return [
        face('bottom', [[0,0,0],[0,20,0],[30,20,0],[30,0,0]], { x: 0, y: 0, z: -1 }),
        face('top', [[0,0,10],[30,0,10],[30,20,10],[0,20,10]], z),
        face('front', [[0,0,0],[30,0,0],[30,0,10],[0,0,10]], { x: 0, y: -1, z: 0 }),
        face('back', [[0,20,0],[0,20,10],[30,20,10],[30,20,0]], { x: 0, y: 1, z: 0 }),
        face('left', [[0,0,0],[0,0,10],[0,20,10],[0,20,0]], { x: -1, y: 0, z: 0 }),
        face('right', [[30,0,0],[30,20,0],[30,20,10],[30,0,10]], { x: 1, y: 0, z: 0 })
    ];
}
async function importedPrism(c, open = false) {
    const faces = prismFaces().filter(f => !open || f.id !== 'top');
    const positions = faces.flatMap(f => [0,1,2,0,2,3].flatMap(i => Object.values(f.loops[0].vertices[i])));
    const mesh = { attributes: { position: { array: positions } },
        brep_faces: faces.map((_, i) => ({ first: i * 2, last: i * 2 + 1 })) };
    return c.CncTopology.build({ meshes: [mesh], validationMeshes: [mesh], validationDeflectionMm: 0.1,
        sourceFormat: 'step', bodyCount: 1,
        analyticSurfaces: faces.map(f => ({ ...f.surface, loops: f.loops, orientation: 'forward' })) });
}
function holeTopology(depth = 100, span = Math.PI * 2) {
    const circle = axial => Array.from({ length: span < 6 ? 5 : 16 }, (_, i) => {
        const angle = span * i / (span < 6 ? 4 : 16);
        return { x: 2.5 * Math.cos(angle), y: 2.5 * Math.sin(angle), z: axial };
    });
    const cylinder = { id: 'bore-wall', bodyId: 'body-1', orientation: 'reversed',
        adjacentFaceIds: ['lower-cap', 'upper-cap'],
        loops: [0, depth].map(axial => ({ vertices: circle(axial), closed: span > 6 })),
        surface: { kind: 'cylinder', radiusMm: 2.5, axis: z, centerMm: { x: 0, y: 0, z: 0 },
            angularSpanRadians: span, boundsMm: { min: { x: -2.5, y: -2.5, z: 0 }, max: { x: 2.5, y: 2.5, z: depth } } } };
    const caps = [0, depth].map((axial, i) => ({ id: i ? 'upper-cap' : 'lower-cap', bodyId: 'body-1',
        orientation: 'forward', adjacentFaceIds: ['bore-wall'], loops: [{ vertices: circle(axial), closed: span > 6 }],
        surface: { kind: 'plane', axis: z, normal: i ? z : { x: 0, y: 0, z: -1 } } }));
    return { ...topology([]), faces: [cylinder, ...caps] };
}
function slotTopology() {
    const w = 8.23 / 2;
    const faces = [
        face('bottom', [[-10,-15,0],[-10,15,0],[10,15,0],[10,-15,0]], { x: 0, y: 0, z: -1 }),
        face('slot-floor', [[-w,-15,6],[w,-15,6],[w,15,6],[-w,15,6]], z),
        face('slot-left', [[-w,-15,6],[-w,15,6],[-w,15,10],[-w,-15,10]], { x: 1, y: 0, z: 0 }),
        face('slot-right', [[w,-15,6],[w,-15,10],[w,15,10],[w,15,6]], { x: -1, y: 0, z: 0 }),
        face('lip-left', [[-10,-15,10],[-w,-15,10],[-w,15,10],[-10,15,10]], z),
        face('lip-right', [[w,-15,10],[10,-15,10],[10,15,10],[w,15,10]], z),
        face('outer-left', [[-10,-15,0],[-10,-15,10],[-10,15,10],[-10,15,0]], { x: -1, y: 0, z: 0 }),
        face('outer-right', [[10,-15,0],[10,15,0],[10,15,10],[10,-15,10]], { x: 1, y: 0, z: 0 })
    ];
    for (const y of [-15, 15]) faces.push(face('end-' + y,
        [[-10,y,0],[10,y,0],[10,y,10],[w,y,10],[w,y,6],[-w,y,6],[-w,y,10],[-10,y,10]],
        { x: 0, y: Math.sign(y), z: 0 }));
    return topology(faces);
}
test('open five-face CAD shell cannot acquire a synthesized validation cap', async () => {
    const t = await importedPrism(runtime(), true);
    assert.equal(t.automaticPlanningEligible, false, 'open CAD shell must be ineligible');
    assert.equal(t.validationMesh.watertight, false);
    assert.equal(t.validationMesh.triangles.length, 10, 'validation cannot add a missing face');
    assert.ok(t.unresolvedReasons.includes('nonwatertight_validation_mesh'));
});
test('complete CAD cannot authorize an entire missing validation face', async () => {
    const c = runtime(), faces = prismFaces();
    const mesh = source => ({ attributes: { position: { array: source.flatMap(f =>
        [0,1,2,0,2,3].flatMap(i => Object.values(f.loops[0].vertices[i]))) } },
        brep_faces: source.map((_, i) => ({ first: i * 2, last: i * 2 + 1 })) });
    const t = await c.CncTopology.build({ meshes: [mesh(faces)],
        validationMeshes: [mesh(faces.filter(f => f.id !== 'top'))], validationDeflectionMm: 0.1,
        sourceFormat: 'step', bodyCount: 1,
        analyticSurfaces: faces.map(f => ({ ...f.surface, loops: f.loops, orientation: 'forward' })) });
    assert.equal(t.automaticPlanningEligible, false);
    assert.equal(t.validationMesh.watertight, false);
    assert.equal(t.validationMesh.triangles.length, 10);
});
test('tiny coplanar validation gap outside the exact CAD trim cannot be capped', async () => {
    const c = runtime(), faces = prismFaces();
    const extra = prismFaces().filter(f => f.id !== 'top').map(f => ({ ...f,
        loops: [{ vertices: f.loops[0].vertices.map(p => ({ x: 40 + p.x / 300,
            y: 40 + p.y / 200, z: 9.9 + p.z / 100 })) }] }));
    const mesh = source => ({ attributes: { position: { array: source.flatMap(f =>
        [0,1,2,0,2,3].flatMap(i => Object.values(f.loops[0].vertices[i]))) } },
        brep_faces: source.map((_, i) => ({ first: i * 2, last: i * 2 + 1 })) });
    const t = await c.CncTopology.build({ meshes: [mesh(faces)],
        validationMeshes: [mesh(faces.concat(extra))], validationDeflectionMm: 0.1,
        sourceFormat: 'step', bodyCount: 1,
        analyticSurfaces: faces.map(f => ({ ...f.surface, loops: f.loops, orientation: 'forward' })) });
    assert.equal(t.automaticPlanningEligible, false);
    assert.equal(t.validationMesh.watertight, false);
    assert.equal(t.validationMesh.triangles.length, 22);
});
test('matched complete face partitions cannot lend their trim to an unassigned coplanar gap', async () => {
    const c = runtime(), faces = prismFaces();
    const positions = faces.flatMap(f => [0,1,2,0,2,3].flatMap(i => Object.values(f.loops[0].vertices[i])));
    const partitions = faces.map((_, i) => ({ first: i * 2, last: i * 2 + 1 }));
    const t = await c.CncTopology.build({ meshes: [{ attributes: { position: { array: positions } }, brep_faces: partitions }],
        validationMeshes: [{ attributes: { position: { array: positions.concat([40,40,10,40.1,40,10,40,40.1,10]) } }, brep_faces: partitions }],
        validationDeflectionMm: 0.1, sourceFormat: 'step', bodyCount: 1,
        analyticSurfaces: faces.map(f => ({ ...f.surface, loops: f.loops, orientation: 'forward' })) });
    assert.equal(t.automaticPlanningEligible, false);
    assert.equal(t.validationMesh.triangles.length, 13);
});
test('quarter cylinder cannot generate a hole or speculative cutting operations', () => {
    const c = runtime();
    const graph = c.CncFeatureGraph.build(holeTopology(10, Math.PI / 2));
    assert.equal(graph.features.filter(f => f.kind === 'hole').length, 0, 'partial cylinder is not a bore');
    assert.deepEqual(plain(c.CncProcessCompiler.compile(graph, {}).operations), []);
});

test('explicit partial angular span cannot be overwritten by contradictory seam incidence', async () => {
    const c = runtime(), points = [{x:2.5,y:0,z:0},{x:0,y:2.5,z:0},{x:0,y:2.5,z:10},{x:2.5,y:0,z:10}];
    const mesh = { attributes: { position: { array: [0,1,2,0,2,3].flatMap(i => Object.values(points[i])) } },
        brep_faces: [{ first: 0, last: 1 }] };
    const t = await c.CncTopology.build({ meshes: [mesh], validationMeshes: [mesh], validationDeflectionMm: 0.1,
        sourceFormat: 'step', bodyCount: 1, analyticSurfaces: [{ kind: 'cylinder', orientation: 'reversed',
            axis: z, centerMm: {x:0,y:0,z:0}, radiusMm: 2.5, angularSpanRadians: Math.PI/2,
            loops: [{ vertices: points, edgeIds: [1,2,3,2] }] }] });
    assert.equal(t.faces[0].surface.angularSpanRadians, Math.round(Math.PI/2*10000)/10000);
    assert.equal(t.faces[0].surface.closureEvidence, undefined);
});
test('100 mm finite hole preserves depth and rejects insufficient drill cutting length and reach', () => {
    const c = runtime();
    const graph = c.CncFeatureGraph.build(holeTopology());
    const hole = graph.features.find(f => f.kind === 'hole');
    assert.equal(hole?.dimensions.depthMm, 100, 'hole depth must derive from the axial boundary interval');
    const result = c.CncProcessCompiler.compile(graph, {});
    assert.equal(result.operations.filter(o => o.featureId === hole.id).length, 0);
    assert.ok(result.unresolved.some(r => r.reason === 'unsupported_hss_drill'));
});
test('hole compiler cannot replace unresolved depth with zero reach', () => {
    const c = runtime();
    const graph = c.CncFeatureGraph.build(holeTopology(10));
    const hole = graph.features.find(f => f.kind === 'hole');
    delete hole.dimensions.depthMm;
    const result = c.CncProcessCompiler.compile(graph, {});
    assert.equal(result.operations.filter(o => o.featureId === hole.id).length, 0);
    assert.ok(result.unresolved.some(r => r.reason === 'hole_depth_required'));
});
test('operation contract rejects drilling constraints that omit the authenticated hole depth', () => {
    const c = runtime(), graph = c.CncFeatureGraph.build(holeTopology(10));
    const operations = c.CncProcessCompiler.compile(graph, {});
    const drill = operations.operations.find(o => o.kind === 'drilling');
    delete drill.toolConstraints.minimumReachMm;
    assert.throws(() => c.CncPlanContracts.validateOperationGraph(operations, graph), /hole.*depth/i);
});
test('compiler rejects feature depth contradicting its retained cylindrical interval', () => {
    const c = runtime(), graph = c.CncFeatureGraph.build(holeTopology(100));
    const hole = graph.features.find(f => f.kind === 'hole');
    hole.dimensions.depthMm = 10;
    const result = c.CncProcessCompiler.compile(graph, {});
    assert.equal(result.operations.filter(o => o.featureId === hole.id).length, 0);
    assert.ok(result.unresolved.some(r => r.reason === 'hole_depth_mismatch'));
});
test('operation contract rejects a short drilling chain against a retained 100 mm closure', () => {
    const c = runtime(), graph = c.CncFeatureGraph.build(holeTopology(10));
    const operations = c.CncProcessCompiler.compile(graph, {});
    graph.features.find(f => f.kind === 'hole').cylindricalClosure.maximumAxialMm = 100;
    assert.throws(() => c.CncPlanContracts.validateOperationGraph(operations, graph), /hole.*depth/i);
});
test('physical hole path rejects mutually forged feature and closure depths against CAD', () => {
    const c = runtime(), t = holeTopology(100), graph = c.CncFeatureGraph.build(holeTopology(10));
    const operations = c.CncProcessCompiler.compile(graph, {}), hole = graph.features.find(f => f.kind === 'hole');
    const drill = operations.operations.find(o => o.kind === 'drilling');
    t.faces.forEach(f => { f.validationVolume = { minimum: {x:-2.75,y:-2.75,z:-0.5}, maximum: {x:2.75,y:2.75,z:100.5} }; });
    const index = Object.fromEntries(t.faces.map(f => [f.id, f]));
    assert.equal(physicalGuards(c).canonicalSemanticPath(hole, drill, index,
        { minimum: {x:-5,y:-5,z:-1}, maximum: {x:5,y:5,z:101} }, {}), null);
});
test('physical drill catalog guard rejects short flutes and reach for the authoritative interval', () => {
    const c = runtime(), t = holeTopology(100), graph = c.CncFeatureGraph.build(holeTopology(10));
    const operations = c.CncProcessCompiler.compile(graph, {}), hole = graph.features.find(f => f.kind === 'hole');
    const drill = operations.operations.find(o => o.kind === 'drilling');
    const index = Object.fromEntries(t.faces.map(f => [f.id, f])), guards = physicalGuards(c);
    // Catalog tool is genuinely 40 mm flute / 50 mm reach. Caller minima stay 10.
    hole.dimensions.depthMm = 100; hole.cylindricalClosure.maximumAxialMm = 100;
    assert.equal(guards.completeTool(drill, c.CncToolLibrary.version, hole, index), null);
});
for (const invalid of ['finite full-span CAD', 'perpendicular boundary support']) test('physical hole authentication requires ' + invalid, () => {
    const c = runtime(), t = holeTopology(10), graph = c.CncFeatureGraph.build(t);
    const hole = graph.features.find(f => f.kind === 'hole');
    const drill = c.CncProcessCompiler.compile(graph, {}).operations.find(o => o.kind === 'drilling');
    const index = Object.fromEntries(t.faces.map(f => [f.id, f])), guards = physicalGuards(c);
    assert.ok(guards.completeTool(drill, c.CncToolLibrary.version, hole, index), 'authentic 10 mm bore accepts the catalog drill');
    if (invalid === 'finite full-span CAD') { delete t.faces[0].surface.angularSpanRadians; }
    else { t.faces.slice(1).forEach(f => { f.surface.normal = {x:1,y:0,z:0}; }); }
    assert.equal(guards.completeTool(drill, c.CncToolLibrary.version, hole, index), null);
});
test('datum compiler requires explicit facing evidence for a machining request', async () => {
    const c = runtime(), graph = c.CncFeatureGraph.build(await importedPrism(c));
    for (const f of graph.features) { f.machiningRequired = true; delete f.facingEvidence; }
    const operations = c.CncProcessCompiler.compile(graph, {});
    assert.equal(operations.operations.length, 0);
    assert.ok(operations.unresolved.every(r => r.reason === 'datum_facing_evidence_required'));
});
test('six-face prism selects one primary datum family instead of six facing axes', async () => {
    const c = runtime(), t = await importedPrism(c);
    assert.equal(t.automaticPlanningEligible, true);
    const graph = c.CncFeatureGraph.build(t);
    const operations = c.CncProcessCompiler.compile(graph, {});
    assert.equal(operations.operations.filter(o => o.kind === 'facing').length, 2,
        'only the selected opposing primary datums require facing');
    assert.ok(operations.operations.every(o => Math.abs(o.accessAxis.z) === 1));
    const stock = c.CncStock.selectStock({ alloy: '6061', quantity: 1, partSizeMm: { x: 30, y: 20, z: 10 },
        partVolumeMm3: 6000, clampBorderMm: 25, includeShipping: true });
    const evidence = c.CncManufacturingEvidence.prepare(t, graph, operations, stock);
    const setups = c.CncSetupPlanner.plan({ topology: t, featureGraph: graph, operationGraph: operations,
        stock: evidence.setupStock, fixtureCatalog: evidence.fixtureCatalog });
    assert.equal(setups.setups.length, 2);
});
test('truthful open 8.23 mm slot derives one complete 6 mm rough and finish band', () => {
    const c = runtime(), graph = c.CncFeatureGraph.build(slotTopology());
    const slot = graph.features.find(f => f.kind === 'slot' && f.primaryFaceIds.includes('slot-floor'));
    assert.ok(slot, 'open slot floor and opposing walls must form one feature');
    assert.equal(slot.dimensions.widthMm, 8.23);
    assert.equal(slot.dimensions.depthMm, 4, 'slot depth comes from wall-to-floor extent');
    assert.deepEqual(plain(slot.accessAxes), [z]);
    assert.equal(slot.cornerEnvelope.kind, 'open_ended_corridor');
    assert.deepEqual(plain(slot.primaryFaceIds), ['slot-floor', 'slot-left', 'slot-right']);
    const operations = c.CncProcessCompiler.compile(graph, {}).operations.filter(o => o.featureId === slot.id);
    assert.deepEqual(plain(operations.map(o => [o.phase, o.toolDiameterMm])), [['rough', 6], ['finish', 6]]);
});

async function importedCorridor(c, length = 30) {
    const faces = slotTopology().faces;
    faces.forEach(f => f.loops[0].vertices.forEach(p => { p.y *= length / 30; }));
    const positions = [], ranges = [];
    for (const f of faces) {
        const points = f.loops[0].vertices;
        // These six triangles tile the concave U ends, without filling the opening.
        const indices = points.length === 8 ? [[0,1,4],[0,4,5],[1,2,3],[1,3,4],[0,5,6],[0,6,7]]
            : [[0,1,2],[0,2,3]];
        const first = positions.length / 9;
        for (const ids of indices) {
            const [a,b,d] = ids.map(i => points[i]);
            const cross = { x: (b.y-a.y)*(d.z-a.z)-(b.z-a.z)*(d.y-a.y),
                y: (b.z-a.z)*(d.x-a.x)-(b.x-a.x)*(d.z-a.z),
                z: (b.x-a.x)*(d.y-a.y)-(b.y-a.y)*(d.x-a.x) };
            if (['x','y','z'].reduce((sum,k) => sum + cross[k]*f.surface.normal[k], 0) < 0) ids.reverse();
            positions.push(...ids.flatMap(i => Object.values(points[i])));
        }
        ranges.push({ first, last: positions.length / 9 - 1 });
    }
    const mesh = { attributes: { position: { array: positions } }, brep_faces: ranges };
    return c.CncTopology.build({ meshes: [mesh], validationMeshes: [mesh], validationDeflectionMm: 0.1,
        sourceFormat: 'step', bodyCount: 1,
        analyticSurfaces: faces.map(f => ({ ...f.surface, loops: f.loops, orientation: 'forward' })) });
}

test('slot compiler refuses unresolved corner semantics even with width and depth', () => {
    const c = runtime(), graph = c.CncFeatureGraph.build(slotTopology());
    const slot = graph.features.find(f => f.kind === 'slot');
    delete slot.cornerEnvelope;
    const result = c.CncProcessCompiler.compile(graph, {});
    assert.equal(result.operations.filter(o => o.featureId === slot.id).length, 0);
    assert.ok(result.unresolved.some(r => r.reason === 'prismatic_corner_envelope_required'));
});

test('reference datums remain available for non-primary machining access', async () => {
    const c = runtime(), t = await importedCorridor(c, 12);
    const graph = c.CncFeatureGraph.build(t), operations = c.CncProcessCompiler.compile(graph, {});
    assert.ok(graph.features.some(f => f.kind === 'datum' && f.machiningRequired === false && f.accessAxes[0].z === 1));
    const prep = c.CncManufacturingEvidence.prepare(t, graph, operations,
        { stockSizeMm: { x: 20.5, y: 12.5, z: 12.5 } });
    assert.ok(prep, 'reference datum contact eligibility is independent from facing requirements');
});

for (const [kind, length] of [['slot', 30], ['pocket', 15]]) test('bounded open ' + kind + ' finalizes with target-safe immutable rough and finish sweeps', async () => {
    const c = runtime(), t = await importedCorridor(c, length);
    assert.equal(t.automaticPlanningEligible, true, JSON.stringify(t.unresolvedReasons));
    const featureGraph = c.CncFeatureGraph.build(t), operationGraph = c.CncProcessCompiler.compile(featureGraph, {});
    const feature = featureGraph.features.find(f => f.kind === kind);
    assert.ok(feature, 'bounded open corridor must have truthful feature semantics');
    const quotedStock = { stockShape: 'block', stockSizeMm: { x: 20.5, y: length + 0.5, z: 12.5 } };
    const prep = c.CncManufacturingEvidence.prepare(t, featureGraph, operationGraph, quotedStock);
    const setupPlan = c.CncSetupPlanner.plan({ topology: t, featureGraph, operationGraph,
        stock: prep.setupStock, fixtureCatalog: prep.fixtureCatalog });
    const stock = c.CncManufacturingEvidence.complete(t, featureGraph, operationGraph, setupPlan, quotedStock);
    const result = await c.CncPlanValidator.validate({ topology: t, featureGraph, operationGraph, setupPlan, stock,
        requirementsRevision: 'bounded-corridor-v1', toolLibraryVersion: c.CncToolLibrary.version });
    assert.equal(result.valid, true, JSON.stringify({ reasons: result.reviewReasons, diagnostics: result.diagnostics }));
    const paths = stock.operationPaths.filter(p => p.featureId === feature.id);
    assert.equal(paths.length, 2);
    for (const path of paths) {
        const evidence = result.plan.validationResults.find(e => e.operationId === path.operationId);
        assert.ok(evidence.removedCellCount > 0);
        assert.ok(evidence.trajectorySampleCount > 0);
        assert.ok(Object.isFrozen(evidence.validatedRemovalRuns));
        assert.equal(evidence.validatedRemovalCellCount, evidence.removedCellCount);
    }
    assert.equal(result.plan.terminalResidual.missingTargetCellCount, 0);
    assert.equal(result.plan.terminalResidual.excessStockCellCount, 0);
    const forged = plain(stock);
    forged.operationPaths.find(p => p.featureId === feature.id).semanticPath.floorPlaneMm -= 2;
    const invalid = await c.CncPlanValidator.validate({ topology: t, featureGraph, operationGraph, setupPlan, stock: forged,
        requirementsRevision: 'bounded-corridor-v1', toolLibraryVersion: c.CncToolLibrary.version });
    assert.equal(invalid.valid, false);
    assert.ok(invalid.reviewReasons.includes('operation_path_invalid'));
});

function corridorSweep(c, obstruction, toolOverrides = {}) {
    const state = { origin: { x: -20, y: -20, z: 0 }, dimensions: { x: 40, y: 40, z: 55 },
        resolutionMm: 1, totalCells: 40*40*55, bits: new Uint32Array(40*40*55/32).fill(0xffffffff) };
    const target = { state: { ...state, bits: new Uint32Array(state.bits.length) } };
    for (let z = 0; z < 55; z++) for (let y = 0; y < 40; y++) for (let x = 0; x < 40; x++) {
        const p = { x: -20+x+0.5, y: -20+y+0.5, z: z+0.5 };
        if (obstruction(p)) { const i = x+40*(y+40*z); target.state.bits[i>>>5] |= 1 << (i&31); }
    }
    const semantic = { geometry: 'prismatic_corridor', axisName: 'z', axisSign: 1, lateralNames: ['x','y'],
        transverseName: 'x', longitudinalName: 'y', minimumTransverseMm: -4.115, maximumTransverseMm: 4.115,
        minimumLongitudinalMm: -15, maximumLongitudinalMm: 15, floorPlaneMm: 6, entryPlaneMm: 10,
        finishAllowanceMm: 0, accessAxis: z, targetClassifierContract: 'CncBrepValidationMesh.v1',
        toolPath: { referencePoint: 'cutter_tip', orientationAxis: z } };
    const tool = { diameterMm: 6, usableCutLengthMm: 18, reachMm: 30, shankDiameterMm: 6, holderDiameterMm: 32, ...toolOverrides };
    return { state, result: c.CncPlanValidator.deriveOperationSweep(state, { kind: 'finishing' }, { kind: 'slot' },
        semantic, 0.05, target, tool) };
}

for (const [part, obstruction, overrides] of [
    ['cutter', p => p.x > 1 && p.x < 5 && Math.abs(p.y) < 3 && p.z > 8 && p.z < 15, {}],
    ['shank', p => Math.abs(p.x) < 2 && Math.abs(p.y) < 3 && p.z > 25 && p.z < 29, {}],
    ['holder', p => p.x > 8 && p.x < 12 && Math.abs(p.y) < 3 && p.z > 38 && p.z < 45, {}],
    ['oversized cutter', p => Math.abs(p.x) > 4.115 && Math.abs(p.x) < 10 && Math.abs(p.y) < 15 && p.z < 10, { diameterMm: 10 }]
]) test('prismatic raw sweep fails closed on ' + part + ' interference without clipping target', () => {
    const { state, result } = corridorSweep(runtime(), obstruction, overrides);
    assert.equal(result.removedCellCount, 0);
    if (part === 'oversized cutter') assert.equal(result.rawSweepCellCount, 0, 'tool cannot fit the corridor');
    else assert.ok(result.targetViolationCellCounts?.[part] > 0, JSON.stringify(result.targetViolationCellCounts));
    assert.deepEqual(Array.from(result.state.bits), Array.from(state.bits));
});

test('prismatic non-cutting assembly cannot pass through unremoved supplier stock', () => {
    const { state, result } = corridorSweep(runtime(), () => false);
    assert.equal(result.removedCellCount, 0, 'shank and holder are not stock-removal tools');
    assert.ok(result.stockViolationCount > 0);
    assert.deepEqual(Array.from(result.state.bits), Array.from(state.bits));
});
