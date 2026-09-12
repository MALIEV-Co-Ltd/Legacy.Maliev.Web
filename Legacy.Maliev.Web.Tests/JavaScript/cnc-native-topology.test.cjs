const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { webcrypto } = require('node:crypto');
const root = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot');
const candidate = require('./cnc-native-region-fixtures.cjs').asset;
function runtime() {
    const c = vm.createContext({ console, TextEncoder, TextDecoder, crypto: webcrypto }); c.self = c;
    for (const name of ['cnc-plan-contracts', 'cnc-cad-document', 'cnc-native-topology.worker', 'cnc-topology.worker']) {
        const file = path.join(root, 'src/app/js/cnc-quotation', name + '.js');
        if (fs.existsSync(file)) vm.runInContext(fs.readFileSync(file, 'utf8'), c);
    }
    return c;
}
const manifestContext = { self: {} }; vm.runInNewContext(fs.readFileSync(path.join(candidate, 'manifest.js'), 'utf8'), manifestContext);
const identity = manifestContext.self.CncNativeBuild;
let apiPromise;
async function envelope(c, file = path.resolve(__dirname, '../TestAssets/Cnc/box-20x30x40.step')) {
    apiPromise ||= require(path.join(candidate, 'occt-import-js.js'))({ wasmBinary: fs.readFileSync(path.join(candidate, 'occt-import-js.wasm')) });
    const bytes = fs.readFileSync(file), api = await apiPromise;
    return c.CncCadDocument.create(bytes, 'step', identity, api.ReadStepFile(bytes, c.CncCadDocument.importParameters));
}
test('native adapter preserves complete same-import data, direct IDs and doubles', async () => {
    const c = runtime(); assert.ok(c.CncCadDocument, 'RED: native document envelope missing');
    const n = await envelope(c), t = await c.CncTopology.build({ nativeImport: n });
    assert.equal(t.cadDocument.contract, 'CadDocument.v2');
    assert.equal(t.faces.length, 6);
    assert.deepEqual(Array.from(t.faces, f => f.id), Array.from(n.meshes[0].brep_faces, f => f.faceId));
    assert.equal(t.cadDocument.nativeImport, n);
    assert.equal(t.faces[0].loops[0].edgeIds[0], n.meshes[0].brep_faces[0].trims.wires[0].coedges[0].edgeId);
    assert.equal(t.validationMesh.source, 'occt_native_same_import_v1');
    assert.equal(t.automaticPlanningEligible, false, 'consumer certification is separate');
    assert.deepEqual(Array.from(t.unresolvedReasons), ['native_loader_source_expectation_missing', 'native_trim_consumer_unsupported']);
    assert.equal(t.nativeInterpretation.interpretationAccepted, false, 'direct decoder fixture has no verified worker source attachment');
    const clone = structuredClone(n); clone.meshes[0].attributes.position.array[0] = 0.123456789012345;
    clone.importRevision = await c.CncCadDocument.hash(clone);
    const changed = await c.CncTopology.build({ nativeImport: clone });
    assert.equal(changed.validationMesh.vertices[0].x, 0.123456789012345);
    assert.notEqual(changed.revision, t.revision);
});
test('native malformed payload never falls back; ownership, ranges and units fail closed', async () => {
    const c = runtime(); assert.ok(c.CncCadDocument);
    const n = await envelope(c);
    for (const mutate of [x => x.meshes[0].brep_faces[1].first = 0,
        x => x.meshes[0].kernelTopology.edges[0].uses.pop(),
        x => x.meshes[0].brep_faces[0].trims.wires[0].coedges[0].startVertexId = 'missing',
        x => x.kernelProvenance.unitsStatus = 'unknown', x => x.sourceFormat = 'brep']) {
        const bad = structuredClone(n); mutate(bad); bad.importRevision = await c.CncCadDocument.hash(bad);
        const t = await c.CncTopology.build({ nativeImport: bad, analyticSurfaces: [{ type: 'plane' }] });
        assert.equal(t.automaticPlanningEligible, false);
        assert.ok(t.unresolvedReasons.some(r => r !== 'native_trim_consumer_unsupported'));
    }
    assert.ok((await c.CncTopology.build({ nativeImport: null })).unresolvedReasons.includes('native_provenance_missing'));
    assert.ok((await c.CncTopology.build({ meshes: n.meshes, sourceFormat: 'step', analyticSurfaces: [] })).unresolvedReasons.includes('native_provenance_missing'),
        'native meshes without their envelope must not enter the legacy scoring branch');
});
test('native revision covers pcurves, orientation, build, ownership and triangle data', async () => {
    const c = runtime(); assert.ok(c.CncCadDocument); const n = await envelope(c);
    const expectedReasons = ['native_import_revision_mismatch', 'native_contract_invalid:face-source',
        'native_import_revision_mismatch', 'native_contract_invalid:measure-ownership', 'native_import_revision_mismatch'];
    for (const mutate of [x => x.meshes[0].brep_faces[0].trims.wires[0].coedges[0].pcurve.origin[0] += .001,
        x => x.meshes[0].brep_faces[0].orientation = 0, x => x.buildIdentity.jsSha256 = 'a'.repeat(64),
        x => x.meshes[0].bodyId = 'other', x => x.meshes[0].index.array[0] = (x.meshes[0].index.array[0] + 1) % 3]) {
        const changed = structuredClone(n); mutate(changed);
        assert.notEqual(await c.CncCadDocument.hash(changed), n.importRevision);
        const rejected = await c.CncTopology.build({ nativeImport: changed });
        assert.ok(rejected.unresolvedReasons.includes(expectedReasons.shift()));
        assert.equal(rejected.automaticPlanningEligible, false);
        assert.notEqual(rejected.nativeInterpretation && rejected.nativeInterpretation.interpretationAccepted, true);
    }
});
test('original native face inventories retain curved supports and repair evidence', {
    skip: !fs.existsSync('Z:/PART 1_26082026.step') || !fs.existsSync('Z:/Component6.step') ? 'private original CAD corpus is not mounted' : false
}, async () => {
    const c = runtime(); assert.ok(c.CncCadDocument);
    for (const [file, count, hash] of [
        ['Z:/PART 1_26082026.step', 74, '4aded8f19c66c7a6fd526f88bbd8ec96a2b0f8796f915e7f4b633c259235ee0c'],
        ['Z:/Component6.step', 350, 'dbd2fc8260b4ac07df38c8e6dec6404f06b82fdf11a3f8d659689c7a0410d0f2']]) {
        const n = await envelope(c, file), t = await c.CncTopology.build({ nativeImport: n });
        assert.equal(n.sourceBytesHash, hash);
        assert.equal(t.faces.length, count); assert.equal(new Set(t.faces.map(f => f.id)).size, count);
        assert.ok(t.unresolvedReasons.includes('native_repair_review_required'));
        assert.ok(!t.unresolvedReasons.includes('native_topology_partial'), t.unresolvedReasons.join(','));
        assert.ok(!t.unresolvedReasons.includes('native_membership_incomplete'), t.unresolvedReasons.join(','));
        assert.equal(t.cadDocument.coverage.faceCoverageStatus, 'complete');
        assert.ok(t.faces.some(f => f.surface.kind === 'unsupported'));
        assert.ok(t.faces.filter(f => f.surface.kind === 'cylinder').every(f => f.surface.angularSpanRadians === null));
    }
});

function workerRuntime() {
    const c = runtime(), imports = [];
    c.location = { search: '?v=native-test' };
    c.importScripts = (...urls) => urls.forEach(url => {
        imports.push(url);
        vm.runInContext(fs.readFileSync(path.join(root, url.split('?')[0]), 'utf8'), c);
    });
    vm.runInContext(fs.readFileSync(path.join(root, 'src/app/js/model-viewer/model-viewer.worker.js'), 'utf8'), c);
    return { c, imports };
}
test('actual native envelope survives transfer and display deflection/placement changes', async () => {
    const { c } = workerRuntime(), n = await envelope(c);
    const expected = await c.CncTopology.build({ nativeImport: n });
    for (const deflection of [.01, .5]) {
        const api = await apiPromise;
        const display = api.ReadStepFile(fs.readFileSync(path.resolve(__dirname, '../TestAssets/Cnc/box-20x30x40.step')),
            { linearUnit: 'millimeter', linearDeflectionType: 'absolute_value', linearDeflection: deflection });
        const object = c.OcctResultToGroup(display); object.userData.nativeImport = structuredClone(n);
        object.children[0].position.x = 71.123456789;
        const buffers = c.ExtractAnalysisMeshBuffers(object);
        const cloned = structuredClone(buffers, { transfer: Array.from(c.TransferablesFor(buffers)) });
        // Actual main-thread bridge forwards records unchanged and transfers display buffers only.
        const forwarded = structuredClone(cloned, { transfer: cloned.flatMap(m => [m.position.buffer, m.index.buffer, m.matrix.buffer]) });
        const restored = c.BuildObject3DFromMeshBuffers(forwarded);
        assert.deepEqual(JSON.parse(JSON.stringify(restored.userData.nativeImport)), JSON.parse(JSON.stringify(n)));
        const actual = await c.CncTopology.build({ nativeImport: restored.userData.nativeImport });
        assert.equal(actual.revision, expected.revision);
        assert.equal(actual.validationMeshHash, expected.validationMeshHash);
    }
});
test('static immutable loader verifies pair and keeps default and CNC factories separate', async () => {
    const { c, imports } = workerRuntime();
    const ordinaryImports = c.importScripts;
    c.importScripts = (...urls) => urls.forEach(url => {
        if (url.endsWith('/occt-import-js.js')) {
            imports.push(url); const name = url.includes('/occt-cnc/') ? 'native' : 'default';
            c.occtimportjs = async options => ({ name, options });
        } else ordinaryImports(url);
    });
    c.fetch = async url => {
        const bytes = fs.readFileSync(path.join(root, url));
        return { ok: true, arrayBuffer: async () => bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) };
    };
    const [native, ordinary] = await Promise.all([c.EnsureCncOcct(), c.EnsureOcct()]);
    assert.equal(native.api.name, 'native'); assert.equal(ordinary.name, 'default');
    assert.equal((await c.EnsureOcct()).name, 'default');
    assert.equal((await c.EnsureCncOcct()).api.name, 'native');
    assert.equal(native.api.options.wasmBinary.byteLength, native.identity.wasmBytes);
    assert.ok(imports.every(url => !url.startsWith('blob:')));
    c.cncOcctPromise = null;
    c.fetch = async () => ({ ok: true, arrayBuffer: async () => new Uint8Array([0]).buffer });
    await assert.rejects(c.EnsureCncOcct(), /integrity mismatch/);
});
test('malformed source ranges and zero-triangle native faces stay visible', async () => {
    const c = runtime(), n = await envelope(c);
    const zero = structuredClone(n); zero.meshes[0].brep_faces[0].last = -1;
    zero.importRevision = await c.CncCadDocument.hash(zero);
    const t = await c.CncTopology.build({ nativeImport: zero });
    assert.equal(t.faces.length, 6);
    assert.ok(t.unresolvedReasons.includes('native_face_numerical_coverage_missing'));
    assert.ok(t.unresolvedReasons.includes('native_validation_range_invalid'));
    const duplicate = structuredClone(n);
    duplicate.meshes[0].brep_faces[1].support = structuredClone(duplicate.meshes[0].brep_faces[0].support);
    duplicate.importRevision = await c.CncCadDocument.hash(duplicate);
    const duplicates = await c.CncTopology.build({ nativeImport: duplicate });
    assert.equal(duplicates.faces.length, 6); assert.notEqual(duplicates.faces[0].id, duplicates.faces[1].id);
});
test('null and malformed native records return scoped reasons without throwing', async () => {
    const c = runtime(), n = await envelope(c);
    for (const mutate of [x => x.meshes.push(null), x => x.meshes[0].brep_faces.push(null),
        x => x.meshes[0].kernelTopology.edges.push(null), x => x.meshes[0].kernelTopology.vertices.push(null),
        x => x.meshes[0].brep_faces[0].trims.wires.push(null),
        x => x.meshes[0].brep_faces[0].trims.wires[0].coedges.push(null),
        x => x.kernelProvenance.documentCoverage.occurrences.push(null),
        x => x.kernelProvenance.documentCoverage.occurrences[0].sourceFaces.push(null),
        x => x.meshes[0].attributes.position.array[0] = NaN]) {
        const bad = structuredClone(n); mutate(bad);
        const t = await c.CncTopology.build({ nativeImport: bad });
        assert.equal(t.automaticPlanningEligible, false);
        assert.ok(t.unresolvedReasons.includes('native_payload_structure_invalid'));
    }
});
test('required native mode keeps mesh uploads explicitly classified as mesh sources', async () => {
    const c = runtime();
    const t = await c.CncTopology.build({ requireNative: true, sourceFormat: 'mesh' });
    assert.equal(t.sourceKind, 'mesh'); assert.equal(t.automaticPlanningEligible, false);
    assert.ok(t.unresolvedReasons.includes('mesh_source'));
});
test('typed native geometry and complete identity sets reject reciprocal forgeries', async () => {
    const c = runtime(), n = await envelope(c);
    for (const mutate of [x => x.meshes[0].brep_faces[0].support.orientedNormal = [0, 0, 0],
        x => x.meshes[0].brep_faces[0].support.axis = [0, 0, 0],
        x => x.meshes[0].brep_faces[0].trims.wires[0].coedges[0].pcurve = { status: 'exact', type: 'line' },
        x => x.meshes[0].kernelTopology.edges[0].curve3d.direction = [0, 0, 0],
        x => { const f = x.meshes[0].brep_faces[0]; f.support.type = 'cylinder'; f.support.radius = -1; },
        x => { const u = x.meshes[0].brep_faces[0].trims.wires[0].coedges[0]; const old = u.coedgeId; u.coedgeId = ''; x.meshes[0].kernelTopology.edges.flatMap(e => e.uses).find(e => e.coedgeId === old).coedgeId = ''; },
        x => { const d = x.kernelProvenance.documentCoverage; d.roots.push(d.roots[0]); d.freeRootCount++; },
        x => { const d = x.kernelProvenance.documentCoverage; d.roots = []; d.freeRootCount = 0; }]) {
        const bad = structuredClone(n); mutate(bad); bad.importRevision = await c.CncCadDocument.hash(bad);
        const t = await c.CncTopology.build({ nativeImport: bad });
        assert.ok(t.unresolvedReasons.some(r => r !== 'native_trim_consumer_unsupported'), t.unresolvedReasons.join(','));
    }
});
