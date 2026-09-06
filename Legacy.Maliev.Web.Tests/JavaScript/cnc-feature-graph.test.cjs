const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { webcrypto } = require('node:crypto');

const webRoot = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot');
const fixtureRoot = path.resolve(__dirname, '../TestAssets/Cnc');
const plain = value => JSON.parse(JSON.stringify(value));

function runtime() {
    const context = vm.createContext({ console, TextEncoder, crypto: webcrypto });
    context.self = context;
    for (const name of ['cnc-plan-contracts', 'cnc-cad-surfaces.worker',
        'cnc-topology.worker', 'cnc-feature-graph.worker']) {
        const modulePath = path.join(webRoot, 'src/app/js/cnc-quotation', name + '.js');
        if (fs.existsSync(modulePath)) {
            vm.runInContext(fs.readFileSync(modulePath, 'utf8'), context, { filename: modulePath });
        }
    }
    return context;
}

async function featureGraphFromStep(fileName) {
    const occtPath = path.join(webRoot, 'lib/occt');
    const occt = await require(path.join(occtPath, 'occt-import-js.js'))({
        wasmBinary: fs.readFileSync(path.join(occtPath, 'occt-import-js.wasm'))
    });
    const source = fs.readFileSync(path.join(fixtureRoot, fileName));
    const model = occt.ReadStepFile(source, null);
    const validationModel = occt.ReadStepFile(source, { linearDeflection: 0.1 });
    assert.equal(model.success, true);
    assert.equal(validationModel.success, true);
    const c = runtime();
    assert.equal(typeof c.CncFeatureGraph?.build, 'function', 'feature graph module must exist');
    const topology = await c.CncTopology.build({
        meshes: model.meshes,
        validationMeshes: validationModel.meshes,
        validationDeflectionMm: 0.1,
        analyticSurfaces: c.CncCadSurfaces.parseStep(source.toString()),
        bodyCount: model.meshes.length,
        sourceFormat: 'step'
    });
    assert.equal(topology.automaticPlanningEligible, true);
    return plain(c.CncFeatureGraph.build(topology, { modelScaleMm: 50 }));
}

function syntheticM14Topology() {
    const thread = { groupId: 'stem-thread', majorDiameterMm: 13.89, pitchMm: 1,
        isInternal: false, handedness: 'right', axis: { x: 0, y: 0, z: 1 } };
    const face = (id, kind, extra = {}) => ({ id, bodyId: 'body-1', orientation: 'forward',
        adjacentFaceIds: [], loops: [{ signature: id, vertices: [
            { x: 0, y: 0, z: 0 }, { x: 1, y: 0, z: 0 }, { x: 0, y: 1, z: 0 }
        ] }], surface: { kind, ...extra } });
    const faces = [
        face('face-thread-crest', 'revolution', { threadEvidence: thread }),
        face('face-thread-flank', 'revolution', { threadEvidence: thread }),
        face('face-thread-root', 'revolution', { threadEvidence: thread }),
        face('face-lead-chamfer', 'cone', { halfAngleRadians: Math.PI / 4,
            axis: { x: 0, y: 0, z: 1 }, radiusMm: 7 }),
        face('face-base', 'plane', { axis: { x: 0, y: 0, z: 1 } })
    ];
    for (let index = 0; index < 3; index += 1) {
        faces[index].adjacentFaceIds = faces.slice(0, 3).filter((_, other) => other !== index).map(item => item.id);
    }
    return { contract: 'CncCadTopology.v1', revision: 'm14-revision', sourceKind: 'brep',
        automaticPlanningEligible: true, unresolvedReasons: [], bodies: [{ id: 'body-1' }], faces, edges: [] };
}

function facetedHelicalM14Topology(options = {}) {
    const faces = [];
    const centerX = options.centerX || 0;
    const idPrefix = options.idPrefix || 'helix';
    const segments = 24;
    const rows = 16;
    const pitchMm = 1;
    const point = (segment, row) => {
        const angle = segment * Math.PI * 2 / segments;
        const z = row * pitchMm / 4;
        const phase = angle - Math.PI * 2 * z / pitchMm;
        const radius = 6.45 + 0.55 * (1 + Math.cos(phase)) / 2;
        return { x: centerX + radius * Math.cos(angle), y: radius * Math.sin(angle), z };
    };
    const add = vertices => {
        const id = idPrefix + '-face-' + String(faces.length + 1).padStart(4, '0');
        faces.push({ id, bodyId: 'body-1', orientation: 'forward', adjacentFaceIds: [],
            loops: [{ signature: id, vertices }], surface: { kind: 'plane' } });
    };
    for (let row = 0; row < rows; row += 1) {
        for (let segment = 0; segment < segments; segment += 1) {
            const next = (segment + 1) % segments;
            add([point(segment, row), point(next, row), point(next, row + 1)]);
            add([point(segment, row), point(next, row + 1), point(segment, row + 1)]);
        }
    }
    const edgeOwners = new Map();
    const key = point => [point.x, point.y, point.z].map(value => value.toFixed(6)).join(',');
    for (const face of faces) {
        const points = face.loops[0].vertices;
        for (let index = 0; index < points.length; index += 1) {
            const edge = [key(points[index]), key(points[(index + 1) % points.length])].sort().join('|');
            if (!edgeOwners.has(edge)) edgeOwners.set(edge, []);
            edgeOwners.get(edge).push(face);
        }
    }
    for (const owners of edgeOwners.values()) for (const owner of owners) {
        owner.adjacentFaceIds.push(...owners.filter(candidate => candidate !== owner).map(candidate => candidate.id));
        owner.adjacentFaceIds = [...new Set(owner.adjacentFaceIds)].sort();
    }
    return { contract: 'CncCadTopology.v1', revision: 'faceted-m14-revision', sourceKind: 'brep',
        automaticPlanningEligible: true, unresolvedReasons: [], bodies: [{ id: 'body-1' }], faces, edges: [] };
}

test('axial threaded topology owns the complete external thread as M14 x 1', () => {
    const c = runtime();
    const graph = plain(c.CncFeatureGraph.build(facetedHelicalM14Topology(), { modelScaleMm: 50 }));
    const threads = graph.features.filter(feature => feature.kind === 'external_thread');
    assert.equal(threads.length, 1);
    assert.equal(threads[0].threadDesignation, 'M14 x 1');
    assert.equal(threads[0].pitchMm, 1);
    assert.equal(graph.features.filter(feature => feature.kind === 'internal_thread').length, 0);
    assert.ok(threads[0].primaryFaceIds.length > 1);
});

test('connected B-Rep helix is recognized without a triangle-cluster annotation', () => {
    const c = runtime();
    const graph = plain(c.CncFeatureGraph.build(facetedHelicalM14Topology(), { modelScaleMm: 50 }));
    const threads = graph.features.filter(feature => feature.kind === 'external_thread');
    assert.equal(threads.length, 1);
    assert.equal(threads[0].threadDesignation, 'M14 x 1');
    assert.ok(threads[0].primaryFaceIds.length > 100);
    assert.equal(JSON.stringify(graph).includes('cluster'), false);
});

test('a plane carrying an ungrounded thread tag cannot create a thread', () => {
    const c = runtime();
    const topology = syntheticM14Topology();
    topology.faces = [topology.faces[0]];
    topology.faces[0].surface.kind = 'plane';
    const graph = plain(c.CncFeatureGraph.build(topology, { modelScaleMm: 50 }));
    assert.equal(graph.features.some(feature => /thread$/.test(feature.kind)), false);
    assert.ok(graph.unresolved.some(item => item.reason === 'unresolved_thread_geometry'));
});

test('disconnected same-labelled helices remain separate thread features', () => {
    const c = runtime();
    const left = facetedHelicalM14Topology({ centerX: -15, idPrefix: 'left' });
    const right = facetedHelicalM14Topology({ centerX: 15, idPrefix: 'right' });
    const evidence = centerX => ({ groupId: 'duplicate-label', majorDiameterMm: 13.89, pitchMm: 1,
        isInternal: false, handedness: 'right', axis: { x: 0, y: 0, z: 1 },
        centerMm: { x: centerX, y: 0, z: 0 } });
    for (const face of left.faces) face.surface.threadEvidence = evidence(-15);
    for (const face of right.faces) face.surface.threadEvidence = evidence(15);
    const topology = { ...left, revision: 'two-thread-revision', faces: [...left.faces, ...right.faces] };
    const graph = plain(c.CncFeatureGraph.build(topology, { modelScaleMm: 80 }));
    assert.equal(graph.features.filter(feature => feature.kind === 'external_thread').length, 2);
});

test('an isolated or unsupported cone is unresolved rather than chamfered', () => {
    const c = runtime();
    const topology = syntheticM14Topology();
    topology.faces = [topology.faces.find(face => face.id === 'face-lead-chamfer')];
    topology.faces[0].adjacentFaceIds = [];
    const graph = plain(c.CncFeatureGraph.build(topology, { modelScaleMm: 50 }));
    assert.equal(graph.features.some(feature => feature.kind === 'chamfer'), false);
    assert.ok(graph.unresolved.some(item => item.reason === 'unresolved_conical_feature'));
});

test('a countersink cone is owned by its hole chain, not a chamfer feature', () => {
    const c = runtime();
    const topology = syntheticM14Topology();
    const cylinder = topology.faces[0];
    cylinder.id = 'counterbore-wall';
    const ring = (z, radius = 2.5) => ({ closed: true, vertices: Array.from({ length: 16 }, (_, index) => ({
        x: radius * Math.cos(index * Math.PI / 8), y: radius * Math.sin(index * Math.PI / 8), z })) });
    cylinder.surface = { kind: 'cylinder', radiusMm: 2.5, axis: { x: 0, y: 0, z: 1 },
        centerMm: { x: 0, y: 0, z: 0 }, angularSpanRadians: Math.PI * 2 };
    cylinder.loops = [ring(0), ring(10)];
    cylinder.orientation = 'reversed';
    cylinder.adjacentFaceIds = ['counterbore-cone', 'counterbore-base'];
    const cone = topology.faces[3];
    cone.id = 'counterbore-cone';
    cone.orientation = 'reversed';
    cone.adjacentFaceIds = ['counterbore-wall'];
    cone.loops = [ring(10), ring(11, 3.5)];
    const base = { id: 'counterbore-base', bodyId: 'body-1', orientation: 'forward',
        adjacentFaceIds: ['counterbore-wall'], loops: [ring(0)],
        surface: { kind: 'plane', normal: { x: 0, y: 0, z: 1 } } };
    topology.faces = [cylinder, cone, base];
    const graph = plain(c.CncFeatureGraph.build(topology, { modelScaleMm: 50 }));
    assert.equal(graph.features.some(feature => feature.kind === 'chamfer'), false);
    assert.equal(graph.features.filter(feature => feature.kind === 'hole').length, 1);
    assert.deepEqual(graph.features.find(feature => feature.kind === 'hole').primaryFaceIds.sort(),
        ['counterbore-cone', 'counterbore-wall']);
});

test('duplicate topology face IDs fail closed before ownership', () => {
    const c = runtime();
    const topology = syntheticM14Topology();
    topology.faces[1].id = topology.faces[0].id;
    const graph = plain(c.CncFeatureGraph.build(topology, { modelScaleMm: 50 }));
    assert.deepEqual(graph.features, []);
    assert.deepEqual(graph.unresolved, [{ reason: 'duplicate_topology_face_id', required: true }]);
});

test('an interior elongated planar floor is recognized as a slot from topology bounds', () => {
    const c = runtime();
    const plane = (id, z, vertices) => ({ id, bodyId: 'body-1', orientation: 'forward',
        adjacentFaceIds: [], loops: [{ signature: id, vertices }],
        surface: { kind: 'plane', normal: { x: 0, y: 0, z: 1 },
            boundsMm: { min: { x: Math.min(...vertices.map(point => point.x)), y: Math.min(...vertices.map(point => point.y)), z },
                max: { x: Math.max(...vertices.map(point => point.x)), y: Math.max(...vertices.map(point => point.y)), z } } } });
    const rectangle = (z, x, y) => [{ x: -x, y: -y, z }, { x, y: -y, z }, { x, y, z }, { x: -x, y: y, z }];
    const faces = [plane('bottom', 0, rectangle(0, 10, 5)), plane('slot-floor', 5, rectangle(5, 8, 1)),
        plane('top', 10, rectangle(10, 10, 5))];
    const graph = plain(c.CncFeatureGraph.build({ contract: 'CncCadTopology.v1', revision: 'slot-revision',
        sourceKind: 'brep', automaticPlanningEligible: true, unresolvedReasons: [], bodies: [{ id: 'body-1' }],
        faces, edges: [] }, { modelScaleMm: 20 }));
    const slot = graph.features.find(feature => feature.kind === 'slot');
    assert.ok(slot);
    assert.deepEqual(slot.primaryFaceIds, ['slot-floor']);
});

test('model worker asserts the published graph is face-based and cluster-free', () => {
    const source = fs.readFileSync(path.join(webRoot, 'src/app/js/model-viewer/model-viewer.worker.js'), 'utf8');
    assert.match(source, /AssertManufacturingFeatureGraph\(geometry\.manufacturingFeatureGraph\)/);
    assert.match(source, /clusterIds.*sampleIds/);
});

test('every machinable face has one primary owner', async () => {
    const graph = await featureGraphFromStep('axial-threaded-part.step');
    const owned = graph.features.flatMap(feature => feature.primaryFaceIds);
    assert.equal(new Set(owned).size, owned.length);
    assert.deepEqual([...owned].sort(), graph.machinableFaceIds.slice().sort());
    assert.deepEqual(Object.keys(graph.faceOwners).sort(), graph.machinableFaceIds.slice().sort());
});

test('chamfer and thread faces are never generic curved features', async () => {
    const c = runtime();
    const topology = facetedHelicalM14Topology();
    const graph = plain(c.CncFeatureGraph.build(topology, { modelScaleMm: 50 }));
    const specialized = new Set(graph.features
        .filter(feature => ['external_thread', 'internal_thread', 'chamfer'].includes(feature.kind))
        .flatMap(feature => feature.primaryFaceIds));
    for (const generic of graph.features.filter(feature =>
        ['fillet', 'freeform_patch'].includes(feature.kind))) {
        assert.ok(generic.primaryFaceIds.every(id => !specialized.has(id)));
    }
    const serialized = JSON.stringify(graph);
    for (const legacyField of ['clusterIds', 'sampleIds', 'operationCodes', 'curvedFinishingByDirection']) {
        assert.equal(serialized.includes(legacyField), false, legacyField + ' must remain diagnostic-only');
    }
});

test('ineligible topology fails closed without speculative features', () => {
    const c = runtime();
    const graph = c.CncFeatureGraph.build({
        contract: 'CncCadTopology.v1', revision: 'mesh-revision', sourceKind: 'mesh',
        automaticPlanningEligible: false, unresolvedReasons: ['mesh_source'], bodies: [], faces: [], edges: []
    }, { modelScaleMm: 50 });
    assert.equal(graph.contract, 'ManufacturingFeatureGraph.v1');
    assert.deepEqual(plain(graph.features), []);
    assert.deepEqual(plain(graph.unresolved), [{ reason: 'topology_not_eligible', required: true }]);
});
