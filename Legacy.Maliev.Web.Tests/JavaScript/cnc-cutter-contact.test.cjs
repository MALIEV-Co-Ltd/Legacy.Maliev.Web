const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function slot(width, family = 'flat_end_mill', roof = false, reach = 20, start = 10) {
    const c = vm.createContext({ console });
    c.self = c;
    vm.runInContext(fs.readFileSync(path.resolve(__dirname,
        '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-spatial-field.worker.js'), 'utf8'), c);
    const dimensions = { x: 40, y: 40, z: 20 };
    const bits = new Uint32Array(Math.ceil(40 * 40 * 20 / 32));
    const index = (x, y, z) => x + 40 * (y + 40 * z);
    for (let z = 0; z < 12; z++) for (let y = 0; y < 40; y++) for (let x = 0; x < 40; x++) {
        if (z <= 2 || x < start || x >= start + width || (roof && z === 11)) {
            const id = index(x, y, z);
            bits[id >>> 5] |= 1 << (id & 31);
        }
    }
    const sample = { id: index(start, 20, 2), normal: { x: 0, y: 0, z: 1 }, areaMm2: 1 };
    const field = { dimensions, cellSizeMm: 1, _occupancyBits: bits,
        axes: [{ x: 1, y: 0, z: 0 }, { x: 0, y: 1, z: 0 }, { x: 0, y: 0, z: 1 }],
        surfaceSamples: [sample] };
    const tool = { id: 'cutter', family, diameterMm: 10, usableCutLengthMm: reach,
        reachMm: reach, shankDiameterMm: 10, holderDiameterMm: 20 };
    return { sample, access: c.CncSpatialField.classifyToolAccess(field, [tool])['positive-z'].cutter };
}

test('flat cutter reaches a slot floor with its cutting edge, not only its centre', () => {
    const { sample, access } = slot(11);
    assert.deepEqual(Array.from(access.tipSampleIds), [sample.id]);
});

test('edge contact does not allow a cutter into a slot narrower than its diameter', () => {
    assert.equal(slot(9).access.reachableSampleIds.length, 0);
});

test('edge contact still checks the complete axial approach and holder envelope', () => {
    assert.equal(slot(11, 'flat_end_mill', true).access.reachableSampleIds.length, 0);
    assert.equal(slot(11, 'flat_end_mill', false, 3).access.reachableSampleIds.length, 0);
});

test('drill axes cannot shift sideways to claim an offset contact point', () => {
    assert.equal(slot(11, 'drill').access.reachableSampleIds.length, 0);
});

test('moving the centre beyond the radial grid edge cannot bypass collisions', () => {
    assert.equal(slot(11, 'flat_end_mill', true, 20, 1).access.reachableSampleIds.length, 0);
    assert.equal(slot(9, 'flat_end_mill', false, 20, 1).access.reachableSampleIds.length, 0);
    assert.equal(slot(11, 'flat_end_mill', false, 3, 1).access.reachableSampleIds.length, 0);
});

test('cached ray thresholds preserve every cutter, shank and holder transition', () => {
    const c = vm.createContext({ console });
    c.self = c;
    const source = fs.readFileSync(path.resolve(__dirname,
        '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-spatial-field.worker.js'), 'utf8');
    vm.runInContext(source.replace('build: build,', 'build: build, ray: toolCentrePathClear,'), c);
    const dimensions = { x: 9, y: 11, z: 13 }, counts = [9, 11, 13];
    const field = { dimensions, cellSizeMm: 0.7 };
    const distances = Float32Array.from({ length: 9 * 11 * 13 }, (_, i) => (i * 17 % 59) + 0.25);
    const tools = [
        { diameterMm: 1, usableCutLengthMm: 1.4, reachMm: 2.8, shankDiameterMm: 4, holderDiameterMm: 10 },
        { diameterMm: 10, usableCutLengthMm: 2.1, reachMm: 4.2, shankDiameterMm: 6, holderDiameterMm: 12 }
    ];
    function reference(coordinates, axis, sign, tool) {
        const point = coordinates.map(Math.round);
        point[axis] += sign;
        let outside = 0;
        for (let i = 0; i < 3; i++) if (i !== axis) {
            const bounded = Math.max(0, Math.min(counts[i] - 1, point[i]));
            outside += (point[i] - bounded) ** 2;
            point[i] = bounded;
        }
        for (let step = 0; step < counts[axis] + 2; step++) {
            if (point[axis] < 0 || point[axis] >= counts[axis]) return true;
            const depth = (step + 1) * field.cellSizeMm;
            const diameter = depth <= tool.usableCutLengthMm + 1e-8 ? tool.diameterMm
                : depth <= tool.reachMm + 1e-8 ? tool.shankDiameterMm : tool.holderDiameterMm;
            const required = Math.max(1, diameter * 0.5 / field.cellSizeMm * 3);
            const index = point[0] + dimensions.x * (point[1] + dimensions.y * point[2]);
            if (distances[index] ** 2 + outside * 9 <= required ** 2) return false;
            point[axis] += sign;
        }
        return true;
    }
    for (let axis = 0; axis < 3; axis++) for (const sign of [-1, 1]) for (const tool of tools) {
        for (let i = 0; i < 120; i++) {
            const coordinates = [(i * 7 % 15) - 3.4, (i * 11 % 17) - 3.2, (i * 13 % 19) - 3.1];
            assert.equal(c.CncSpatialField.ray(field, distances, coordinates, axis, sign, tool),
                reference(coordinates, axis, sign, tool));
        }
    }
});
