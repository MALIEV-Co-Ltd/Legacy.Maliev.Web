const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const context = vm.createContext({ console });
context.self = context;
vm.runInContext(fs.readFileSync(path.resolve(__dirname,
    '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-spatial-field.worker.js'), 'utf8'), context);
const axes = [{ x: 1, y: 0, z: 0 }, { x: 0, y: 1, z: 0 }, { x: 0, y: 0, z: 1 }];

function box(min, max, reversed = false) {
    const p = [[min[0], min[1], min[2]], [max[0], min[1], min[2]],
        [max[0], max[1], min[2]], [min[0], max[1], min[2]],
        [min[0], min[1], max[2]], [max[0], min[1], max[2]],
        [max[0], max[1], max[2]], [min[0], max[1], max[2]]];
    const faces = [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4],
        [3, 7, 6, 2], [0, 4, 7, 3], [1, 2, 6, 5]];
    return faces.flatMap(([a, b, c, d]) => (reversed ? [a, c, b, a, d, c] : [a, b, c, a, c, d])
        .flatMap(index => p[index]));
}

function build(triangles) {
    return context.CncSpatialField.build(new Float32Array(triangles), axes);
}

function occupied(field, x, y, z) {
    const cell = [x, y, z].map((value, axis) => Math.floor(
        (value - field.frameOrigin[['x', 'y', 'z'][axis]]) / field.cellSizeMm));
    const index = cell[0] + field.dimensions.x * (cell[1] + field.dimensions.y * cell[2]);
    return !!(field._occupancyBits[index >>> 5] & (1 << (index & 31)));
}

test('overlapping closed bodies form occupied union, not an empty intersection', () => {
    const field = build([...box([0, 0, 0], [1, 1, 1]), ...box([0.5, 0, 0], [1.5, 1, 1])]);
    for (const x of [0.25, 0.75, 1.25]) assert.ok(occupied(field, x, 0.37, 0.41), `Missing material at x=${x}`);
    assert.equal(field.oddCrossingLines, 0);
    assert.equal(field.degraded, false);
});

test('touching bodies have no artificial internal boundary or missing second body', () => {
    const field = build([...box([0, 0, 0], [1, 1, 1]), ...box([1, 0, 0], [2, 1, 1])]);
    for (const x of [0.5, 0.99, 1.01, 1.5]) assert.ok(occupied(field, x, 0.37, 0.41));
    assert.equal(field.oddCrossingLines, 0);
    assert.ok(field.surfaceSamples.every(sample => {
        const p = sample.position;
        return p.x < 0.9 || p.x > 1.1 || p.y < 0.05 || p.y > 0.95 || p.z < 0.05 || p.z > 0.95;
    }));
});

test('disjoint solids retain the air gap between them', () => {
    const field = build([...box([0, 0, 0], [1, 1, 1]), ...box([1.5, 0, 0], [2.5, 1, 1])]);
    assert.ok(occupied(field, 0.5, 0.37, 0.41));
    assert.equal(occupied(field, 1.25, 0.37, 0.41), false);
    assert.ok(occupied(field, 2, 0.37, 0.41));
});

test('inward oriented cavity shell preserves real internal air', () => {
    const field = build([...box([0, 0, 0], [1, 1, 1]), ...box([0.25, 0.25, 0.25], [0.75, 0.75, 0.75], true)]);
    assert.ok(occupied(field, 0.1, 0.37, 0.41));
    assert.equal(occupied(field, 0.5, 0.37, 0.41), false);
    assert.ok(occupied(field, 0.9, 0.37, 0.41));
    assert.equal(field.degraded, false);
});

test('global reversed winding still combines overlapping solids', () => {
    const field = build([...box([0, 0, 0], [1, 1, 1], true), ...box([0.5, 0, 0], [1.5, 1, 1], true)]);
    for (const x of [0.25, 0.75, 1.25]) assert.ok(occupied(field, x, 0.37, 0.41));
    assert.equal(field.degraded, false);
});

test('duplicate closed shells and shared triangulation edges retain valid material', () => {
    const triangles = box([0, 0, 0], [1, 1, 1]);
    const field = build([...triangles, ...triangles]);
    assert.equal(field.oddCrossingLines, 0);
    assert.ok(occupied(field, 0.5, 0.37, 0.41));
    assert.equal(occupied(field, 1.01, 0.37, 0.41), false);
});

test('coincident entry faces preserve material until the last enclosing body exits', () => {
    const field = build([...box([0, 0, 0], [1, 1, 1]), ...box([0, 0, 0], [1.5, 1, 1])]);
    assert.ok(occupied(field, 0.5, 0.37, 0.41));
    assert.ok(occupied(field, 1.25, 0.37, 0.41));
    assert.equal(field.degraded, false);
});

test('isolated duplicate faces report an unbalanced shell instead of silently claiming valid topology', () => {
    const triangles = box([0, 0, 0], [1, 1, 1]);
    const field = build([...triangles, ...triangles.slice(72, 90)]);
    assert.ok(field.oddCrossingLines > 0);
    assert.equal(field.degraded, true);
});

test('material from another closed body can occupy part of a real cavity', () => {
    const field = build([...box([0, 0, 0], [1, 1, 1]),
        ...box([0.25, 0.25, 0.25], [0.75, 0.75, 0.75], true),
        ...box([0.4, 0.3, 0.3], [0.6, 0.7, 0.7])]);
    assert.equal(occupied(field, 0.3, 0.37, 0.41), false);
    assert.ok(occupied(field, 0.5, 0.37, 0.41));
    assert.equal(occupied(field, 0.7, 0.37, 0.41), false);
    assert.equal(field.degraded, false);
});

test('sealed cavity remains empty but does not become a machining surface', () => {
    const field = build([...box([0, 0, 0], [1, 1, 1]),
        ...box([0.25, 0.25, 0.25], [0.75, 0.75, 0.75], true)]);
    assert.equal(occupied(field, 0.5, 0.37, 0.41), false);
    assert.ok(field.surfaceSamples.every(({ position: p }) =>
        p.x < 0.05 || p.x > 0.95 || p.y < 0.05 || p.y > 0.95 || p.z < 0.05 || p.z > 0.95));
    assert.ok(field.enclosedAirCellCount > 0);
    assert.ok(Math.abs(field.enclosedAirVolumeMm3 - 0.125) < 0.015);
    const serialized = context.CncSpatialField.serialize(field);
    assert.equal(serialized.enclosedAirCellCount, field.enclosedAirCellCount);
    assert.equal(serialized.enclosedAirVolumeMm3, field.enclosedAirVolumeMm3);
    assert.equal(context.CncSpatialField.isExteriorAir(field, { x: 0.5, y: 0.5, z: 0.5 }), false);
    assert.equal(context.CncSpatialField.isExteriorAir(field, { x: 0.1, y: 0.5, z: 0.5 }), false);
    assert.equal(context.CncSpatialField.isExteriorAir(field, { x: -1, y: 0.5, z: 0.5 }), true);
});

test('open pocket inner walls remain exterior-connected machining surfaces', () => {
    const field = build([...box([0, 0, 0], [1, 1, 1]),
        ...box([0.25, 0.25, 0.25], [1, 0.75, 0.75], true)]);
    assert.equal(occupied(field, 0.5, 0.37, 0.41), false);
    assert.ok(field.surfaceSamples.some(({ position: p, normal: n }) =>
        p.x > 0.3 && p.x < 0.7 && p.y > 0.22 && p.y < 0.28 && p.z > 0.3 && p.z < 0.7 && n.y > 0.9));
    assert.equal(field.enclosedAirCellCount, 0);
    assert.equal(field.enclosedAirVolumeMm3, 0);
    assert.equal(context.CncSpatialField.isExteriorAir(field, { x: 0.5, y: 0.5, z: 0.5 }), true);
});

test('exterior queries use world coordinates for translated rotated-frame pockets', () => {
    const triangles = [...box([3, 4, 5], [4, 5, 6]),
        ...box([3.25, 4.25, 5.25], [4, 4.75, 5.75], true)];
    const field = context.CncSpatialField.build(new Float32Array(triangles),
        [{ x: 0, y: 1, z: 0 }, { x: -1, y: 0, z: 0 }, { x: 0, y: 0, z: 1 }]);
    assert.equal(context.CncSpatialField.isExteriorAir(field, { x: 3.5, y: 4.5, z: 5.5 }), true);
    assert.equal(context.CncSpatialField.isExteriorAir(field, { x: 3.1, y: 4.5, z: 5.5 }), false);
    assert.equal(context.CncSpatialField.isExteriorAir(field, { x: 4.1, y: 4.5, z: 5.5 }), true);
    assert.equal(context.CncSpatialField.isExteriorAir(field, { x: NaN, y: 4.5, z: 5.5 }), false);
});
