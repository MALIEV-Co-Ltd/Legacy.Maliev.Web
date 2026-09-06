const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const up = { x: 0, y: 0, z: 1 };
const cutter = { family: 'flat_end_mill', diameterMm: 2, usableCutLengthMm: 4,
    reachMm: 14, shankDiameterMm: 2, holderDiameterMm: 20 };

function fixture(obstruction = []) {
    const c = vm.createContext({ console });
    c.self = c;
    vm.runInContext(fs.readFileSync(path.resolve(__dirname,
        '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-geometry.worker.js'), 'utf8'), c);
    // z = -0.75x: the cutter's rim at x=0,z=0 is tangent to the slope.
    // A sample-centred cutter intersects the rising negative-x side instead.
    const triangles = [-10,-10,7.5, 10,-10,-7.5, 10,10,-7.5,
        -10,-10,7.5, 10,10,-7.5, -10,10,7.5, ...obstruction];
    const analysis = c.CncSurfaceAnalysis(triangles,
        [{ x: 1, y: 0, z: 0 }, { x: 0, y: 1, z: 0 }, up]);
    const sample = { sourceTriangleIndex: 0, contactPosition: { x: 0, y: 0, z: 0 },
        normal: { x: 0.6, y: 0, z: 0.8 } };
    assert.equal(typeof c.CncFlatToolContactVerifier, 'function', 'continuous flat contact must support slopes');
    return { c, sample, verify: c.CncFlatToolContactVerifier(analysis.records, []) };
}

test('a flat cutting edge reaches a slope without requiring a smaller sample-centred tool', () => {
    const f = fixture();
    assert.equal(f.verify(f.sample, up, cutter), true);
    assert.equal(f.verify(f.sample, up, { ...cutter, diameterMm: 10, shankDiameterMm: 10 }), true);
});

test('sloped contact does not ignore a roof or an adjacent obstruction', () => {
    for (const obstruction of [[0.5,-0.5,1, 1.5,-0.5,1, 1,0.5,1],
        [1,-0.5,0.2, 1,0.5,0.2, 1,0,2]]) {
        const f = fixture(obstruction);
        assert.equal(f.verify(f.sample, up, cutter), false);
    }
});

test('the full shank and holder must clear the slope at their respective axial starts', () => {
    const f = fixture();
    assert.equal(f.verify(f.sample, up, { ...cutter, usableCutLengthMm: 0.1, shankDiameterMm: 8 }), false);
    assert.equal(f.verify(f.sample, up, { ...cutter, usableCutLengthMm: 0.1, reachMm: 0.2 }), false);
    assert.equal(f.verify(f.sample, up, { ...cutter, shankDiameterMm: 8, usableCutLengthMm: 4 }), true);
});

test('flat refinement does not authorize a ball tool, back-facing contact or unknown geometry', () => {
    const f = fixture();
    assert.equal(f.verify(f.sample, up, { ...cutter, family: 'ball_end_mill' }), false);
    assert.equal(f.verify(f.sample, { x: 0, y: 0, z: -1 }, cutter), false);
    assert.equal(f.verify({ ...f.sample, sourceTriangleIndex: 99 }, up, cutter), false);
    assert.equal(f.verify(f.sample, up, { ...cutter, reachMm: 2 }), false);
});
