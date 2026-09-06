const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const context = vm.createContext({ console });
context.self = context;
vm.runInContext(fs.readFileSync(path.resolve(__dirname,
    '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-spatial-field.worker.js'), 'utf8'), context);

function roundPocket(diameter, { roof = false, reach = 20 } = {}) {
    const dimensions = { x: 64, y: 64, z: 24 };
    const origin = { x: -8, y: -8, z: -1 };
    const cellSizeMm = 0.25;
    const bits = new Uint32Array(Math.ceil(dimensions.x * dimensions.y * dimensions.z / 32));
    const indexAt = (x, y, z) => x + dimensions.x * (y + dimensions.y * z);
    const radius = diameter / 2;
    for (let z = 0; z < dimensions.z; z++) {
        const pz = origin.z + (z + 0.5) * cellSizeMm;
        for (let y = 0; y < dimensions.y; y++) {
            const py = origin.y + (y + 0.5) * cellSizeMm;
            for (let x = 0; x < dimensions.x; x++) {
                const px = origin.x + (x + 0.5) * cellSizeMm;
                // Analytic solid: four-mm plate, circular recess from z=1 to4.
                // Optional roof seals the approach without altering the wall.
                const material = pz >= 0 && pz < 4
                    && (pz < 1 || px * px + py * py >= radius * radius || (roof && pz >= 3.5));
                if (material) {
                    const id = indexAt(x, y, z);
                    bits[id >>> 5] |= 1 << (id & 31);
                }
            }
        }
    }
    const samples = Array.from({ length: 24 }, (_, i) => {
        const angle = i * Math.PI / 12;
        const cos = Math.cos(angle), sin = Math.sin(angle);
        const x = Math.floor(((radius + cellSizeMm) * cos - origin.x) / cellSizeMm);
        const y = Math.floor(((radius + cellSizeMm) * sin - origin.y) / cellSizeMm);
        const z = Math.floor((2 - origin.z) / cellSizeMm);
        const id = indexAt(x, y, z);
        assert.ok(bits[id >>> 5] & (1 << (id & 31)), `Fixture wall sample ${i} must be material`);
        return { id, normal: { x: -cos, y: -sin, z: 0 }, areaMm2: cellSizeMm ** 2,
            contactPosition: { x: radius * cos, y: radius * sin, z: 2 } };
    });
    const field = { dimensions, origin, frameOrigin: origin, cellSizeMm, _occupancyBits: bits,
        axes: [{ x: 1, y: 0, z: 0 }, { x: 0, y: 1, z: 0 }, { x: 0, y: 0, z: 1 }],
        surfaceSamples: samples };
    const tool = { id: 'flat-10', family: 'flat_end_mill', diameterMm: 10,
        usableCutLengthMm: reach, reachMm: reach, shankDiameterMm: 10, holderDiameterMm: 20 };
    return { samples, access: context.CncSpatialField.classifyToolAccess(field, [tool])['positive-z']['flat-10'] };
}

test('ten-mm flat end mill reaches eleven-mm recess walls at every azimuth', () => {
    const { samples, access } = roundPocket(11);
    const reached = new Set(access.fluteSampleIds);
    const missedAngles = samples.flatMap((sample, i) => reached.has(sample.id) ? [] : [i * 15]);
    assert.deepEqual(missedAngles, [], 'Flute clearance must not depend on grid azimuth');
});

test('ten-mm cutter cannot fit a nine-mm circular recess at any azimuth', () => {
    assert.equal(roundPocket(9).access.reachableSampleIds.length, 0);
});

test('radial flute contact cannot bypass a recess roof or an oversized holder', () => {
    assert.equal(roundPocket(11, { roof: true }).access.reachableSampleIds.length, 0);
    assert.equal(roundPocket(11, { reach: 0.5 }).access.reachableSampleIds.length, 0);
});
