const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot');
const up = { x: 0, y: 0, z: 1 };
const cutter = { family: 'flat_end_mill', diameterMm: 6, usableCutLengthMm: 18,
    reachMm: 30, shankDiameterMm: 6, holderDiameterMm: 24 };

function context() {
    const c = vm.createContext({ console });
    c.self = c;
    vm.runInContext(fs.readFileSync(path.join(root,
        'src/app/js/cnc-quotation/cnc-geometry.worker.js'), 'utf8'), c);
    return c;
}

test('cluster adjacency comes from shared mesh edges, not proximity or a common body', () => {
    const c = context();
    const records = c.CncTriangleRecords([0,0,0, 1,0,0, 0,1,0,
        1,0,0, 0,0,0, 0,0,1, 10,0,0, 11,0,0, 10,1,0]);
    const clusters = ['wall', 'floor', 'separate'].map((id, index) => ({ evidence: { id }, memberIndexes: [index] }));
    c.CncPublishClusterAdjacency(clusters, records);
    assert.deepEqual(clusters.map(cluster => Array.from(cluster.evidence.adjacentClusterIds)), [['floor'], ['wall'], []]);
    const bounded = [{ evidence: { id: 'unknown' } }];
    c.CncPublishClusterAdjacency(bounded, []);
    assert.equal(bounded[0].evidence.adjacentClusterIds, undefined);
});

function fixture(obstruction = []) {
    const c = context();
    // Two parallel slot walls, 8.23 mm apart and 5.37 mm deep.
    const triangles = [0,-10,-5.37, 0,10,-5.37, 0,10,0,
        0,-10,-5.37, 0,10,0, 0,-10,0,
        8.23,-10,-5.37, 8.23,10,0, 8.23,10,-5.37,
        8.23,-10,-5.37, 8.23,-10,0, 8.23,10,0, ...obstruction];
    const records = c.CncTriangleRecords(triangles);
    const sample = { sourceTriangleIndex: 0, contactPosition: { x: 0, y: 0, z: -4.294 },
        normal: { x: 1, y: 0, z: 0 } };
    return { sample, verify: c.CncFlatToolContactVerifier(records, []) };
}

test('parallel flute contact admits a fitting slot cutter without voxel-margin cleanup', () => {
    const f = fixture();
    assert.equal(f.verify(f.sample, up, cutter), true);
    assert.equal(f.verify(f.sample, up, { ...cutter, diameterMm: 10, shankDiameterMm: 10 }), false);
});

test('parallel flute refinement retains roofs and adjacent obstacles', () => {
    for (const obstruction of [[2,-1,1, 4,-1,1, 3,1,1],
        [3,-1,-3, 3,1,-3, 3,0,-1]]) {
        const f = fixture(obstruction);
        assert.equal(f.verify(f.sample, up, cutter), false);
    }
});

test('parallel flute refinement checks enlarged shank and holder at their actual starts', () => {
    const f = fixture();
    assert.equal(f.verify(f.sample, up, { ...cutter, usableCutLengthMm: 2, shankDiameterMm: 12 }), false);
    assert.equal(f.verify(f.sample, up, { ...cutter, usableCutLengthMm: 2, reachMm: 3 }), false);
    assert.equal(f.verify(f.sample, up, { ...cutter, usableCutLengthMm: 6, shankDiameterMm: 12 }), true);
});

test('parallel flute refinement does not authorize back-facing contact or unsupported evidence', () => {
    const f = fixture();
    assert.equal(f.verify({ ...f.sample, normal: { x: 0.8, y: 0, z: -0.6 } }, up, cutter), false);
    assert.equal(f.verify(f.sample, up, { ...cutter, family: 'ball_end_mill' }), false);
    assert.equal(f.verify({ ...f.sample, sourceTriangleIndex: 99 }, up, cutter), false);
    assert.equal(f.verify(f.sample, up, { ...cutter, reachMm: 2 }), false);
});

test('the actual cover-plate slot-end contact fits six millimetres but rejects ten', async () => {
    const source = fs.readFileSync(path.resolve(__dirname, '../TestAssets/Cnc/threaded-cover-plate.step'));
    const occt = await require(path.join(root, 'lib/occt/occt-import-js.js'))({
        wasmBinary: fs.readFileSync(path.join(root, 'lib/occt/occt-import-js.wasm')) });
    const model = occt.ReadStepFile(source, null);
    assert.equal(model.success, true);
    const triangles = [];
    for (const mesh of model.meshes) {
        for (const index of mesh.index.array) {
            triangles.push(...mesh.attributes.position.array.slice(index * 3, index * 3 + 3));
        }
    }
    const c = context();
    const records = c.CncTriangleRecords(new Float32Array(triangles));
    // This CAD-wall point was the lone sampled contact that forced a 4 mm pass.
    const sample = { sourceTriangleIndex: 5069,
        contactPosition: { x: 11.891350687262289, y: 18.74541167949956, z: -4.293978676121895 },
        normal: { x: 0.7390089591705462, y: -0.6736955976297203, z: 0 } };
    const verify = c.CncFlatToolContactVerifier(records, []);
    assert.equal(verify(sample, up, cutter), true);
    assert.equal(verify(sample, up, { ...cutter, diameterMm: 10, shankDiameterMm: 10 }), false);
});

test('swept-disk broad phase prunes distant surfaces without changing exact clearance', () => {
    const c = context();
    const triangles = [];
    for (let x = 0; x < 32; x++) {
        for (let y = 0; y < 32; y++) {
            const z = (x + y) % 4;
            triangles.push(x * 10,y * 10,z, x * 10 + 2,y * 10,z, x * 10,y * 10 + 2,z);
        }
    }
    const records = c.CncTriangleRecords(triangles);
    const verify = c.CncToolSweepVerifier(records);
    const approaches = [up, { x: 0, y: 0, z: -1 }, { x: 1, y: 0, z: 0 },
        { x: -1, y: 0, z: 0 }, { x: 0, y: 1, z: 0 }, { x: 0, y: -1, z: 0 }];
    for (const approach of approaches) {
        for (const centre of [{ x: 10.5, y: 0.5, z: 0 }, { x: 155, y: 155, z: -2 },
            { x: 310.5, y: 310.5, z: 1 }, { x: -1, y: 100.5, z: 1 }]) {
            for (const radius of [0.5, 3, 12]) {
                for (const start of [0, 2]) {
                    const section = { radius, start };
                    for (const ignored of [null, new Set([32, 1023])]) {
                        const expected = c.CncHoleEntryDirections(records, centre, approach,
                            radius, start, start, ignored, [1]).length > 0;
                        assert.equal(verify(centre, approach, section, ignored), expected,
                            JSON.stringify({ approach, centre, section, ignored: ignored && [...ignored] }));
                    }
                }
            }
        }
    }
    // Count deterministic bounding arithmetic, not wall-clock time: repeatedly
    // querying one local patch must not rescan all 1,024 scattered surfaces.
    let bounds = 0;
    c.Math = Object.create(Math);
    c.Math.max = (...values) => { bounds++; return Math.max(...values); };
    for (let i = 0; i < 20; i++) {
        assert.equal(verify({ x: 10.5, y: 0.5, z: 0 }, up, { radius: 0.5, start: 0 }, null), false);
    }
    assert.ok(bounds < 5000, `local queries performed ${bounds} bounding max operations`);
});
