const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const context = vm.createContext({ console });
context.self = context;
// Expose the real private algorithm in this test context only; the production
// API remains unchanged and the cutter-clearance integration owns its callers.
const source = fs.readFileSync(path.resolve(__dirname,
    '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-spatial-field.worker.js'), 'utf8');
vm.runInContext(source.replace('root.CncSpatialField =',
    'root.TestDistanceField = buildSliceDistance; root.CncSpatialField ='), context);

function indexAt(point, dimensions) {
    return point[0] + dimensions.x * (point[1] + dimensions.y * point[2]);
}

function field(dimensions, seeds) {
    const bits = new Uint32Array(Math.ceil(dimensions.x * dimensions.y * dimensions.z / 32));
    for (const seed of seeds) {
        const index = indexAt(seed, dimensions);
        bits[index >>> 5] |= 1 << (index & 31);
    }
    return { dimensions, _occupancyBits: bits };
}

test('radial clearance is rotation independent for equal Euclidean distances', () => {
    const input = field({ x: 1, y: 30, z: 30 }, [[0, 1, 1]]);
    const distances = context.TestDistanceField(input, 0);
    for (const point of [[0, 14, 1], [0, 6, 13], [0, 13, 6]]) {
        assert.ok(Math.abs(distances[indexAt(point, input.dimensions)] - 39) < 1e-5,
            `Expected thirteen-cell clearance at ${point}`);
    }
    assert.ok(Math.abs(distances[indexAt([0, 4, 4], input.dimensions)] - 3 * Math.sqrt(18)) < 1e-5);
});

for (const axis of [0, 1, 2]) {
    test(`slice distance on axis ${axis} matches nearest occupied cell, without cross-slice leakage`, () => {
        const dimensions = { x: 7, y: 8, z: 9 };
        const seeds = [[0, 0, 0], [3, 3, 3], [6, 7, 8], [1, 4, 3], [3, 0, 7]];
        const input = field(dimensions, seeds);
        const before = input._occupancyBits.slice();
        const distances = context.TestDistanceField(input, axis);
        for (let z = 0; z < dimensions.z; z++) {
            for (let y = 0; y < dimensions.y; y++) {
                for (let x = 0; x < dimensions.x; x++) {
                    const point = [x, y, z];
                    const candidates = seeds.filter(seed => seed[axis] === point[axis]);
                    const expected = candidates.length === 0 ? Infinity : 3 * Math.sqrt(Math.min(...candidates.map(seed =>
                        (seed[0] - x) ** 2 + (seed[1] - y) ** 2 + (seed[2] - z) ** 2)));
                    const actual = distances[indexAt(point, dimensions)];
                    assert.ok(actual === expected || Math.abs(actual - expected) < 1e-5,
                        `${point}: expected ${expected}, actual ${actual}`);
                }
            }
        }
        assert.deepEqual(input._occupancyBits, before);
    });
}

test('empty slices have infinite clearance and fully occupied slices have zero clearance', () => {
    const dimensions = { x: 3, y: 4, z: 5 };
    const seeds = [];
    for (let y = 0; y < dimensions.y; y++) for (let z = 0; z < dimensions.z; z++) seeds.push([1, y, z]);
    const distances = context.TestDistanceField(field(dimensions, seeds), 0);
    for (let y = 0; y < dimensions.y; y++) {
        for (let z = 0; z < dimensions.z; z++) {
            assert.equal(distances[indexAt([0, y, z], dimensions)], Infinity);
            assert.equal(distances[indexAt([1, y, z], dimensions)], 0);
            assert.equal(distances[indexAt([2, y, z], dimensions)], Infinity);
        }
    }
});
