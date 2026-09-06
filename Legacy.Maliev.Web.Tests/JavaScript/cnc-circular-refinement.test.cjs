const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const x = { x: 1, y: 0, z: 0 }, y = { x: 0, y: 1, z: 0 }, z = { x: 0, y: 0, z: 1 };
const cutter = { family: 'flat_end_mill', diameterMm: 10, usableCutLengthMm: 20,
    reachMm: 25, shankDiameterMm: 10, holderDiameterMm: 20 };

function fixture(radius = 5.5, obstruction = [], exhaustive = false) {
    const c = vm.createContext({ console });
    c.self = c;
    let source = fs.readFileSync(path.resolve(__dirname,
        '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-geometry.worker.js'), 'utf8');
    if (exhaustive) {
        // Keep identical analytic candidates/rim proofs, but independently remove
        // both optimizations so every section uses the exhaustive disk primitive.
        const helper = /    function sweepClear\(centre, approach, section, ignored\) \{[\s\S]*?\n    \}/;
        assert.ok(helper.test(source));
        source = source.replace(helper, `    function sweepClear(centre, approach, section, ignored) {
        return CncHoleEntryDirections(records, centre, approach, section.radius,
            section.start, section.start, ignored, [1]).length > 0;
    }`);
        const dominated = 'if (section.radius <= clearedRadius) { return true; }';
        assert.ok(source.includes(dominated));
        source = source.replace(dominated, '/* Exhaustive reference checks every section. */');
    }
    vm.runInContext(source, c);
    const triangles = [];
    for (let i = 0; i < 32; i++) {
        const a = i * Math.PI / 16, b = (i + 1) * Math.PI / 16;
        const p = [radius * Math.cos(a), radius * Math.sin(a), 0];
        const q = [p[0], p[1], 10];
        const r = [radius * Math.cos(b), radius * Math.sin(b), 10];
        const s = [r[0], r[1], 0];
        triangles.push(...p, ...q, ...r, ...p, ...r, ...s);
    }
    // A floor fan lies behind the positive-z approach and may borrow the opening.
    triangles.push(0, 0, 0, radius * Math.cos(-0.1), radius * Math.sin(-0.1), 0,
        radius * Math.cos(0.1), radius * Math.sin(0.1), 0, ...obstruction);
    const analysis = c.CncSurfaceAnalysis(triangles, [x, y, z]);
    for (const cluster of analysis.clusters) {
        cluster.evidence.filletFeatures = c.CncLocalFilletFeatures(cluster, analysis.records, 1);
        cluster.evidence.triangleIndexes = cluster.memberIndexes.map(index => analysis.records[index].sourceTriangleIndex);
    }
    const clusters = analysis.clusters.map(cluster => cluster.evidence);
    const wall = { sourceTriangleIndex: 8, clusterId: clusters.find(s => s.triangleIndexes.includes(8)).id,
        contactPosition: { x: radius / Math.sqrt(2), y: radius / Math.sqrt(2), z: 5 },
        normal: analysis.records[8].normal };
    const floor = { sourceTriangleIndex: 64, clusterId: clusters.find(s => s.triangleIndexes.includes(64)).id,
        contactPosition: { x: radius - 0.07, y: 0, z: 0 }, normal: z };
    return { c, records: analysis.records, clusters, wall, floor };
}

test('verified concave circular strips publish their measured centre', () => {
    const f = fixture();
    const features = f.clusters.flatMap(cluster => cluster.filletFeatures);
    assert.equal(features.length, 1);
    assert.ok(features[0].centerMm, 'circular contact needs the measured centre, not the cluster centroid');
    assert.ok(Math.hypot(features[0].centerMm.x, features[0].centerMm.y) < 1e-7);
    assert.ok(Math.abs(features[0].radiusMm - 5.5) < 1e-7);
});

test('10 mm flat flute reaches a verified 11 mm circular opening at a facet seam', () => {
    const f = fixture();
    assert.equal(f.c.CncCircularToolContactVerifier(f.records, f.clusters)(f.wall, z, cutter), true);
});

test('continuous floor contact reaches the opening without snapping the tool centre to a voxel', () => {
    const f = fixture();
    assert.equal(f.c.CncCircularToolContactVerifier(f.records, f.clusters)(f.floor, z, cutter), true);
});

test('circular refinement refuses smaller openings and unrelated or non-flat contacts', () => {
    const narrow = fixture(4.5);
    assert.equal(narrow.c.CncCircularToolContactVerifier(narrow.records, narrow.clusters)(narrow.wall, z, cutter), false);
    const f = fixture(), verify = f.c.CncCircularToolContactVerifier(f.records, f.clusters);
    assert.equal(verify(f.wall, x, cutter), false);
    assert.equal(verify(f.wall, z, { ...cutter, family: 'ball_end_mill' }), false);
    assert.equal(verify({ ...f.wall, sourceTriangleIndex: 999 }, z, cutter), false);
    assert.equal(verify({ ...f.floor, contactPosition: { ...f.floor.contactPosition, z: 5 } }, z, cutter), false);
});

test('circular contact retains off-centre obstructions and roofs even inside the owning cluster', () => {
    const f = fixture(5.5, [2, 0, 8, 3, 0, 8, 2, 1, 8]);
    f.clusters.find(cluster => cluster.filletFeatures.length).triangleIndexes.push(65);
    assert.equal(f.c.CncCircularToolContactVerifier(f.records, f.clusters)(f.wall, z, cutter), false);
    assert.equal(f.c.CncCircularToolContactVerifier(f.records, f.clusters)(f.floor, z, cutter), false);
    const lip = fixture(5.5, [2, 0, 5, 2, 1, 8, 2, -1, 8]);
    lip.clusters.find(cluster => cluster.filletFeatures.length).triangleIndexes.push(65);
    assert.equal(lip.c.CncCircularToolContactVerifier(lip.records, lip.clusters)(lip.wall, z, cutter), false);
});

test('omitted cylinder facets are recovered only from their own verified cluster', () => {
    const f = fixture();
    const cluster = f.clusters.find(cluster => cluster.filletFeatures.length);
    const feature = cluster.filletFeatures[0];
    feature.triangleIndexes = feature.triangleIndexes.filter(index => index !== 8);
    assert.equal(f.c.CncCircularToolContactVerifier(f.records, f.clusters)(f.wall, z, cutter), true);
    cluster.triangleIndexes = cluster.triangleIndexes.filter(index => index !== 8);
    assert.equal(f.c.CncCircularToolContactVerifier(f.records, f.clusters)(f.wall, z, cutter), false);
});

test('circular contact checks shank and holder only after their actual axial start', () => {
    const f = fixture(), verify = f.c.CncCircularToolContactVerifier(f.records, f.clusters);
    assert.equal(verify(f.floor, z, { ...cutter, usableCutLengthMm: 2, shankDiameterMm: 12 }), false);
    assert.equal(verify(f.floor, z, { ...cutter, usableCutLengthMm: 2, reachMm: 3 }), false);
    assert.equal(verify(f.floor, z, { ...cutter, usableCutLengthMm: 10, reachMm: 10 }), true);
});

test('verified terminal rim chords refine to the circle but an inward roof stays solid', () => {
    const a = Math.PI / 4, b = a + Math.PI / 16, radius = 5.5;
    const edge = [radius * Math.cos(a), radius * Math.sin(a), 10,
        radius * Math.cos(b), radius * Math.sin(b), 10];
    const rim = fixture(radius, [...edge, (radius + 4) * Math.cos(a), (radius + 4) * Math.sin(a), 10]);
    const verify = rim.c.CncCircularToolContactVerifier(rim.records, rim.clusters);
    assert.equal(verify(rim.wall, z, cutter), true);
    assert.equal(verify(rim.wall, z, { ...cutter, usableCutLengthMm: 1, reachMm: 2, holderDiameterMm: 12 }), false);
    const roof = fixture(radius, [...edge, 0, 0, 10]);
    assert.equal(roof.c.CncCircularToolContactVerifier(roof.records, roof.clusters)(roof.wall, z, cutter), false);
});

test('single-sided disk sweep does not change the default bidirectional hole contract', () => {
    const f = fixture(5.5, [-1, -1, 8, 1, -1, 8, 0, 1, 8]);
    const records = [f.records[65]];
    const both = f.c.CncHoleEntryDirections(records, { x: 0, y: 0, z: 5 }, z, 1, 0, 0);
    assert.equal(both.length, 1);
    assert.equal(both[0].z, -1);
    assert.equal(f.c.CncHoleEntryDirections(records, { x: 0, y: 0, z: 5 }, z, 1, 0, 0, null, [1]).length, 0);
});

test('a clear cutter sweep does not rescan its narrower shank or a holder beyond the part', () => {
    const f = fixture(), precise = f.c.CncHoleEntryDirections;
    let calls = 0, visited = 0;
    f.c.CncHoleEntryDirections = function (records, ...args) {
        calls++;
        visited += records.length;
        return precise(records, ...args);
    };
    assert.equal(f.c.CncCircularToolContactVerifier(f.records, f.clusters)(f.floor, z, cutter), true);
    assert.ok(calls <= 1, 'dominated or axially empty sections need no precise sweep');
    assert.ok(visited < f.records.length, 'proved irrelevant triangles need no polygon clipping');
});

test('cached bounds and sweep dominance match exhaustive checks across obstructions, axes and signs', () => {
    function rotate(f, axis) {
        const vector = v => {
            const values = [v.x, v.y, v.z];
            return { x: values[axis % 3], y: values[(axis + 1) % 3], z: values[(axis + 2) % 3] };
        };
        const point = p => { const v = vector(p); return { x: v.x + 17, y: v.y - 23, z: v.z + 41 }; };
        for (const record of f.records) {
            record.vertices = record.vertices.map(point);
            record.normal = vector(record.normal);
        }
        for (const cluster of f.clusters) for (const feature of cluster.filletFeatures) {
            feature.centerMm = point(feature.centerMm);
            feature.axis = vector(feature.axis);
        }
        for (const sample of [f.floor, f.wall]) {
            sample.contactPosition = point(sample.contactPosition);
            sample.normal = vector(sample.normal);
        }
        return vector(z);
    }
    const obstructions = [[], [-2, -2, 3, 2, -2, 3, 0, 2, 3],
        [2, 0, 8, 3, 0, 8, 2, 1, 8], [20, 0, 8, 21, 0, 8, 20, 1, 8],
        [-2, -2, -2, 2, -2, -2, 0, 2, -2], [7, 0, 30, 8, 0, 30, 7, 1, 30]];
    const tools = [cutter, { ...cutter, diameterMm: 8, shankDiameterMm: 6 },
        { ...cutter, usableCutLengthMm: 2, shankDiameterMm: 12 },
        { ...cutter, usableCutLengthMm: 2, reachMm: 3 },
        { ...cutter, usableCutLengthMm: 2, reachMm: 3, shankDiameterMm: 6, holderDiameterMm: 8 }];
    for (const radius of [4.5, 5.5]) for (const obstruction of obstructions) for (let axis = 0; axis < 3; axis++) {
        const actual = fixture(radius, obstruction), reference = fixture(radius, obstruction, true);
        const direction = rotate(actual, axis);
        rotate(reference, axis);
        const optimized = actual.c.CncCircularToolContactVerifier(actual.records, actual.clusters);
        const exhaustive = reference.c.CncCircularToolContactVerifier(reference.records, reference.clusters);
        for (const sign of [1, -1]) for (const tool of tools) for (const contact of ['wall', 'floor']) {
            const approach = { x: sign * direction.x, y: sign * direction.y, z: sign * direction.z };
            assert.equal(optimized(actual[contact], approach, tool), exhaustive(reference[contact], approach, tool),
                JSON.stringify({ radius, obstruction, axis, sign, tool, contact }));
        }
    }
});
