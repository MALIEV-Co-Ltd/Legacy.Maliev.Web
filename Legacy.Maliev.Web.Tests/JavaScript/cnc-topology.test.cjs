const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { webcrypto } = require('node:crypto');

const webRoot = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot');
const plain = value => JSON.parse(JSON.stringify(value));
const semantic = topology => plain({
    revision: topology.revision,
    bodies: topology.bodies,
    faces: topology.faces.map(({ triangleRange, ...face }) => face),
    edges: topology.edges,
    automaticPlanningEligible: topology.automaticPlanningEligible,
    unresolvedReasons: topology.unresolvedReasons
});

function runtime() {
    const context = vm.createContext({ console, TextEncoder, crypto: webcrypto });
    context.self = context;
    for (const name of ['cnc-plan-contracts', 'cnc-cad-surfaces.worker', 'cnc-topology.worker']) {
        const modulePath = path.join(webRoot, 'src/app/js/cnc-quotation', name + '.js');
        if (fs.existsSync(modulePath)) {
            vm.runInContext(fs.readFileSync(modulePath, 'utf8'), context, { filename: modulePath });
        }
    }
    return context;
}

async function topologyFromStep(fileName, options) {
    const occtPath = path.join(webRoot, 'lib/occt');
    const occt = await require(path.join(occtPath, 'occt-import-js.js'))({
        wasmBinary: fs.readFileSync(path.join(occtPath, 'occt-import-js.wasm'))
    });
    const source = fs.readFileSync(path.resolve(__dirname, '../TestAssets/Cnc', fileName));
    const model = occt.ReadStepFile(source, options);
    const validationModel = occt.ReadStepFile(source, { linearDeflection: 0.1 });
    assert.equal(model.success, true);
    assert.equal(validationModel.success, true);
    const c = runtime();
    assert.equal(typeof c.CncTopology?.build, 'function', 'topology adapter must exist');
    return await c.CncTopology.build({
        meshes: model.meshes,
        validationMeshes: validationModel.meshes,
        validationDeflectionMm: 0.1,
        analyticSurfaces: c.CncCadSurfaces.parseStep(source.toString()),
        bodyCount: model.meshes.length,
        sourceFormat: 'step'
    });
}

test('fixed B-Rep validation mesh is hashed into the topology revision', async () => {
    const topology = await topologyFromStep('axial-threaded-part.step', { linearDeflection: 0.5 });
    assert.match(topology.validationMeshHash, /^[a-f0-9]{64}$/);
    assert.equal(topology.validationMesh.deflectionMm, 0.1);
    const forged = structuredClone(topology);
    forged.validationMesh.vertices[0].x += 0.5;
    assert.notEqual(await runtime().CncTopology.validationMeshHash(forged.validationMesh), topology.validationMeshHash);
    forged.validationMeshHash = await runtime().CncTopology.validationMeshHash(forged.validationMesh);
    assert.notEqual(await runtime().CncTopology.revisionHash(forged), topology.revision);
});

test('STEP topology never falls back to display tessellation for solid validation', async () => {
    const c = runtime();
    const topology = await c.CncTopology.build({ sourceFormat: 'step', bodyCount: 1,
        meshes: [mesh([[[0,0,0], [1,0,0], [0,1,0]]], [{ first: 0, last: 0 }])],
        analyticSurfaces: [support(0)] });
    assert.equal(topology.validationMesh, null);
    assert.deepEqual(plain(topology.unresolvedReasons), ['validation_mesh_required']);
});

function support(centerX, kind = 'plane') {
    return { kind, centerMm: { x: centerX, y: 0, z: 0 }, axis: { x: 0, y: 0, z: 1 },
        loops: [{ vertices: [
            { x: centerX, y: 0, z: 0 }, { x: centerX + 1, y: 0, z: 0 }, { x: centerX, y: 1, z: 0 }
        ] }], orientation: 'forward' };
}

function mesh(triangles, ranges) {
    const positions = triangles.flat(2);
    return { attributes: { position: { array: positions } }, index: null, brep_faces: ranges };
}

async function topologyFromIges(fileName) {
    const occtPath = path.join(webRoot, 'lib/occt');
    const occt = await require(path.join(occtPath, 'occt-import-js.js'))({
        wasmBinary: fs.readFileSync(path.join(occtPath, 'occt-import-js.wasm'))
    });
    const model = occt.ReadIgesFile(fs.readFileSync(path.resolve(__dirname, '../TestAssets', fileName)), null);
    assert.equal(model.success, true);
    return await runtime().CncTopology.build({ meshes: model.meshes, analyticSurfaces: [],
        bodyCount: model.meshes.length, sourceFormat: 'iges' });
}

test('STEP faces retain analytic identity and canonical adjacency across tessellation deflection', async () => {
    const first = await topologyFromStep('axial-threaded-part.step', { linearDeflection: 0.1 });
    const second = await topologyFromStep('axial-threaded-part.step', { linearDeflection: 0.5 });
    assert.equal(first.contract, 'CncCadTopology.v1');
    assert.equal(first.sourceKind, 'brep');
    assert.ok(first.faces.some(face => face.surface.kind !== 'unknown'), 'analytic supports are retained');
    assert.deepEqual(plain(first.faces.map(face => ({
        id: face.id,
        surface: face.surface,
        adjacentFaceIds: face.adjacentFaceIds
    }))), plain(second.faces.map(face => ({
        id: face.id,
        surface: face.surface,
        adjacentFaceIds: face.adjacentFaceIds
    }))));
    assert.match(first.revision, /^[a-f0-9]{64}$/);
    assert.equal(JSON.stringify(first).includes('Uint'), false, 'topology is structured-clone-safe data');
});

test('mesh source is explicit and cannot masquerade as B-Rep topology', async () => {
    const c = runtime();
    assert.equal(typeof c.CncTopology?.build, 'function', 'topology adapter must exist');
    const topology = await c.CncTopology.build({ meshes: [], analyticSurfaces: [],
        bodyCount: 1, sourceFormat: 'stl' });
    assert.equal(topology.sourceKind, 'mesh');
    assert.equal(topology.automaticPlanningEligible, false);
});

test('diagnostic triangle ranges do not alter a CAD topology revision', async () => {
    const c = runtime();
    const support = [{ faceIndex: 0, kind: 'plane', centerMm: { x: 0, y: 0, z: 0 },
        axis: { x: 0, y: 0, z: 1 }, loops: [{ vertices: [
            { x: 0, y: 0, z: 0 }, { x: 1, y: 0, z: 0 }, { x: 0, y: 1, z: 0 }
        ] }], orientation: 'forward' }];
    const first = await c.CncTopology.build({ sourceFormat: 'step', bodyCount: 1,
        meshes: [mesh([[[0,0,0], [1,0,0], [0,1,0]]], [{ first: 0, last: 0 }])], analyticSurfaces: support });
    const second = await c.CncTopology.build({ sourceFormat: 'step', bodyCount: 1,
        meshes: [mesh([[[0,0,0], [1,0,0], [0,1,0]], [[0,0,0], [1,0,0], [0,1,0]]], [{ first: 1, last: 1 }])], analyticSurfaces: support });
    assert.equal(first.revision, second.revision);
    assert.notDeepEqual(plain(first.faces[0].triangleRange), plain(second.faces[0].triangleRange));
});

test('topology preserves the signed plane normal needed for thread polarity', async () => {
    const c = runtime();
    const semanticSupport = support(0);
    semanticSupport.axis = { x: 0, y: 0, z: -1 };
    const topology = await c.CncTopology.build({ sourceFormat: 'step', bodyCount: 1,
        meshes: [mesh([[[0,0,0], [1,0,0], [0,1,0]]], [{ first: 0, last: 0 }])],
        analyticSurfaces: [semanticSupport] });
    assert.equal(topology.automaticPlanningEligible, false,
        'an open one-triangle semantic fixture cannot become an automatic manufacturing solid');
    assert.deepEqual(plain(topology.unresolvedReasons), ['validation_mesh_required']);
    assert.deepEqual(plain(topology.faces[0].surface.normal), { x: 0, y: 0, z: -1 });
});

test('IGES without importer-backed semantic faces is explicitly ineligible', async () => {
    const topology = await topologyFromIges('cube.iges');
    assert.equal(topology.sourceKind, 'brep');
    assert.equal(topology.automaticPlanningEligible, false);
    assert.deepEqual(plain(topology.faces), []);
    assert.deepEqual(plain(topology.unresolvedReasons), ['iges_semantic_face_support_unavailable']);
});

test('unmatched STEP B-Rep face evidence fails closed', async () => {
    const topology = await runtime().CncTopology.build({ sourceFormat: 'step', bodyCount: 1,
        meshes: [mesh([[[0,0,0], [1,0,0], [0,1,0]]], [{ first: 0, last: 0 }])], analyticSurfaces: [support(10)] });
    assert.equal(topology.automaticPlanningEligible, false);
    assert.deepEqual(plain(topology.faces), []);
    assert.deepEqual(plain(topology.unresolvedReasons), ['unmatched_brep_face_support']);
});

test('STEP geometric support matching accepts face vertices within tolerance', async () => {
    const near = support(0);
    near.loops[0].vertices = near.loops[0].vertices.map(({ x, y, z }) => ({ x: x + 0.0005, y, z }));
    const topology = await runtime().CncTopology.build({ sourceFormat: 'step', bodyCount: 1,
        meshes: [mesh([[[0,0,0], [1,0,0], [0,1,0]]], [{ first: 0, last: 0 }])], analyticSurfaces: [near] });
    assert.equal(topology.automaticPlanningEligible, false,
        'support matching succeeds, but an open one-triangle fixture still fails solid validation');
    assert.equal(topology.faces.length, 1);
    assert.deepEqual(plain(topology.unresolvedReasons), ['validation_mesh_required']);
});

test('multi-body topology is invariant to mesh and analytic-support order', async () => {
    const c = runtime();
    const left = mesh([[[0,0,0], [1,0,0], [0,1,0]]], [{ first: 0, last: 0 }]);
    const right = mesh([[[10,0,0], [11,0,0], [10,1,0]]], [{ first: 0, last: 0 }]);
    const first = await c.CncTopology.build({ sourceFormat: 'step', bodyCount: 2,
        meshes: [left, right], analyticSurfaces: [support(10), support(0)] });
    const second = await c.CncTopology.build({ sourceFormat: 'step', bodyCount: 2,
        meshes: [right, left], analyticSurfaces: [support(0), support(10)] });
    assert.deepEqual(semantic(first), semantic(second));
    assert.deepEqual(plain(first.faces.map(face => face.surface.centerMm.x)), [0, 10]);
});

test('face semantics are invariant to B-Rep face-record and support permutation', async () => {
    const c = runtime();
    const triangles = [
        [[0,0,0], [1,0,0], [0,1,0]],
        [[10,0,0], [11,0,0], [10,1,0]]
    ];
    const first = await c.CncTopology.build({ sourceFormat: 'step', bodyCount: 1,
        meshes: [mesh(triangles, [{ first: 0, last: 0 }, { first: 1, last: 1 }])],
        analyticSurfaces: [support(10), support(0)] });
    const second = await c.CncTopology.build({ sourceFormat: 'step', bodyCount: 1,
        meshes: [mesh(triangles, [{ first: 1, last: 1 }, { first: 0, last: 0 }])],
        analyticSurfaces: [support(0), support(10)] });
    assert.deepEqual(semantic(first), semantic(second));
    assert.deepEqual(plain(first.faces.map(face => face.surface.centerMm.x)), [0, 10]);
});

test('duplicate indistinguishable semantic bodies and faces fail closed identically under permutation', async () => {
    const c = runtime();
    const duplicate = mesh([[[0,0,0], [1,0,0], [0,1,0]]], [{ first: 0, last: 0 }]);
    const bodyFirst = await c.CncTopology.build({ sourceFormat: 'step', bodyCount: 2,
        meshes: [duplicate, structuredClone(duplicate)], analyticSurfaces: [support(0), support(0)] });
    const bodySecond = await c.CncTopology.build({ sourceFormat: 'step', bodyCount: 2,
        meshes: [structuredClone(duplicate), duplicate], analyticSurfaces: [support(0), support(0)] });
    const faceFirst = await c.CncTopology.build({ sourceFormat: 'step', bodyCount: 1,
        meshes: [mesh([[[0,0,0], [1,0,0], [0,1,0]], [[0,0,0], [1,0,0], [0,1,0]]], [{ first: 0, last: 0 }, { first: 1, last: 1 }])], analyticSurfaces: [support(0), support(0)] });
    const faceSecond = await c.CncTopology.build({ sourceFormat: 'step', bodyCount: 1,
        meshes: [mesh([[[0,0,0], [1,0,0], [0,1,0]], [[0,0,0], [1,0,0], [0,1,0]]], [{ first: 1, last: 1 }, { first: 0, last: 0 }])], analyticSurfaces: [support(0), support(0)] });
    [bodyFirst, bodySecond, faceFirst, faceSecond].forEach(topology => {
        assert.equal(topology.automaticPlanningEligible, false);
        assert.deepEqual(plain(topology.unresolvedReasons), ['ambiguous_duplicate_semantic_face']);
    });
    assert.deepEqual(semantic(bodyFirst), semantic(bodySecond));
    assert.deepEqual(semantic(faceFirst), semantic(faceSecond));
});
