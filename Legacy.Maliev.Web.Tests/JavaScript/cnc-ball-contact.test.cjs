const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const up = { x: 0, y: 0, z: 1 };
const tool = { family: 'ball_end_mill', diameterMm: 1, usableCutLengthMm: .75,
    underNeckLengthMm: 5, neckDiameterMm: .95, shankDiameterMm: 4,
    reachMm: 14, holderDiameterMm: 20 };

function runtime(triangles, hints = [], faces = [{ first: 0, last: 23 }]) {
    const c = vm.createContext({ console }); c.self = c;
    for (const name of ['cnc-geometry.worker', 'cnc-ball-rest.worker']) {
        const file = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation', name + '.js');
        if (fs.existsSync(file)) vm.runInContext(fs.readFileSync(file, 'utf8'), c);
    }
    assert.equal(typeof c.CncBallRest?.createVerifier, 'function', 'ball contact needs its own physical envelope');
    const { records } = c.CncSurfaceAnalysis(triangles,
        [{ x: 1, y: 0, z: 0 }, { x: 0, y: 1, z: 0 }, up]);
    const verifier = c.CncBallRest.createVerifier(records, hints, faces);
    return { c, records, verifier };
}

function curvedStrip(point, uCount, vCount) {
    const result = [];
    for (let u = 0; u < uCount; u++) for (let v = 0; v < vCount; v++) {
        const a = point(u/uCount, v/vCount), b = point((u+1)/uCount, v/vCount);
        const cc = point((u+1)/uCount, (v+1)/vCount), d = point(u/uCount, (v+1)/vCount);
        result.push(...a, ...b, ...cc, ...a, ...cc, ...d);
    }
    return result;
}

const cylinderHint = { kind: 'cylinder', sourceId: 'cylinder', radiusMm: .5,
    centerMm: { x: 0, y: 0, z: 0 }, axis: { x: 0, y: 1, z: 0 } };
function cylinder() {
    return curvedStrip((u,v) => { const a = -Math.PI + u * Math.PI;
        return [.5*Math.cos(a), -2+v*4, .5*Math.sin(a)]; }, 12, 1);
}

test('radius-matched ball contact uses the analytic concave cylinder, not its inscribed chords', () => {
    const f = runtime(cylinder(), [cylinderHint]);
    const record = f.records[10];
    const sample = { sourceTriangleIndex: record.sourceTriangleIndex,
        contactPosition: record.centroid, normal: record.normal };
    const pose = f.verifier.contact(sample, up, tool);
    assert.ok(pose, 'a physical R0.5 ball must fit the modeled R0.5 strip');
    assert.ok(Math.abs(pose.center.x) < 1e-8 && Math.abs(pose.center.z) < 1e-8);
    assert.equal(pose.surfaceKind, 'cylinder');
});

test('analytic contact cannot ignore a real roof or a larger physical shank', () => {
    const roof = [-1,-1,1, 1,-1,1, 0,1,1];
    const f = runtime([...cylinder(), ...roof], [cylinderHint]);
    const record = f.records[10];
    const sample = { sourceTriangleIndex: record.sourceTriangleIndex,
        contactPosition: record.centroid, normal: record.normal };
    assert.equal(f.verifier.contact(sample, up, tool), null);
    const side = [-1,-1,4, -1,1,4, -1,0,8];
    const g = runtime([...cylinder(), ...side], [cylinderHint]);
    assert.equal(g.verifier.contact(sample, up, tool), null, 'the 4 mm shank must not inherit the 1 mm ball envelope');
});

test('wrong analytic radius and a ball larger than the fillet do not grant clearance', () => {
    const f = runtime(cylinder(), [{ ...cylinderHint, radiusMm: .6 }]);
    const record = f.records[10];
    const sample = { sourceTriangleIndex: record.sourceTriangleIndex,
        contactPosition: record.centroid, normal: record.normal };
    assert.equal(f.verifier.contact(sample, up, tool), null);
    const g = runtime(cylinder(), [cylinderHint]);
    assert.equal(g.verifier.contact(sample, up, { ...tool, diameterMm: 2 }), null);
});

test('sub-micrometre CAD trim drift can identify a curved face without admitting a larger surface mismatch', () => {
    function displaced(delta) { return cylinder().map((v,index)=>index%3===1?v:v*(.5-delta)/.5); }
    const f=runtime(displaced(.00045),[cylinderHint]);
    const record=f.records[10];
    const sample={sourceTriangleIndex:record.sourceTriangleIndex,contactPosition:record.centroid,normal:record.normal};
    assert.ok(f.verifier.contact(sample,up,tool));
    const g=runtime(displaced(.002),[cylinderHint]);
    const other=g.records[10];
    assert.equal(g.verifier.contact({...sample,contactPosition:other.centroid},up,tool),null);
    const actual=runtime(displaced(.0005),[cylinderHint,{...cylinderHint,sourceId:'actual-smaller-radius',radiusMm:.4995}]);
    const exact=actual.records[10];
    assert.equal(actual.verifier.contact({...sample,contactPosition:exact.centroid},up,tool),null,
        'the exact smaller-radius face must not borrow a more generous nearby hint');
});

test('a matched ball fits a verified toroidal blend without ignoring neighboring faces', () => {
    const hint = { kind: 'torus', sourceId: 'torus', radiusMm: 3, minorRadiusMm: .5,
        centerMm: { x: 0, y: 0, z: 0 }, axis: up };
    const triangles = curvedStrip((u,v) => {
        // Reverse phi so normals point into the concave cavity, not into stock.
        const theta = -.2 + .4*u, phi = Math.PI+.5-v;
        const r = 3+.5*Math.cos(phi);
        return [r*Math.cos(theta), r*Math.sin(theta), .5*Math.sin(phi)];
    }, 6, 6);
    const f = runtime(triangles, [hint], [{ first: 0, last: triangles.length / 9 - 1 }]);
    const record = f.records[40];
    const sample = { sourceTriangleIndex: record.sourceTriangleIndex,
        contactPosition: record.centroid, normal: record.normal };
    const direction = { x: 1, y: 0, z: 0 };
    const pose = f.verifier.contact(sample, direction, tool);
    assert.ok(pose);
    assert.equal(pose.surfaceKind, 'torus');
    assert.ok(Math.abs(Math.hypot(pose.center.x, pose.center.y)-3) < 1e-8);
    const g = runtime([...triangles, 4,-1,-1, 4,1,-1, 4,0,1], [hint], [{ first: 0, last: triangles.length / 9 - 1 }]);
    assert.equal(g.verifier.contact(sample, direction, tool), null);
});

test('an unrelated planar floor cannot borrow a curved face hint, even with coincident vertices', () => {
    const z = -Math.sqrt(.24);
    const floor = [-.1,-1,z, .1,-1,z, .1,1,z, -.1,-1,z, .1,1,z, -.1,1,z];
    const f = runtime([...cylinder(), ...floor], [cylinderHint], [{ first: 0, last: 23 }, { first: 24, last: 25 }]);
    const record = f.records[10];
    const sample = { sourceTriangleIndex: record.sourceTriangleIndex, contactPosition: record.centroid, normal: record.normal };
    assert.equal(f.verifier.contact(sample, up, tool), null);
    const noProvenance = runtime(cylinder(), [cylinderHint], []);
    assert.equal(noProvenance.verifier.contact(sample, up, tool), null);
    const moved = runtime(cylinder(), [cylinderHint]);
    assert.equal(moved.verifier.contact({ ...sample, contactPosition: { x: 100, y: 0, z: 0 } }, up, tool), null);
});

test('a handoff requires a real preceding flat sweep and keeps the wide shank above faced stock', () => {
    const f = runtime(cylinder(), [cylinderHint]);
    const record = f.records[10];
    const sample = { sourceTriangleIndex: record.sourceTriangleIndex,
        contactPosition: record.centroid, normal: record.normal };
    const prep = { diameterMm: 2, usableCutLengthMm: 4, shankDiameterMm: 4, reachMm: 16, holderDiameterMm: 20 };
    assert.equal(typeof f.verifier.handoff, 'function');
    const handoff = f.verifier.handoff(sample, up, tool, prep);
    assert.ok(handoff);
    assert.ok(handoff.residualAxialCapMm > 0 && handoff.residualAxialCapMm <= .75);
    assert.equal(handoff.requiresFacing, true);
    assert.equal(handoff.requiresPreparation, true);
    assert.equal(handoff.camCertain, false, 'a sampled quoting estimate is not a CAM certificate');
    assert.equal(f.verifier.handoff(sample, up, tool, { ...prep, diameterMm: .5 }), null);
    // A remote high boss does not obstruct final-CAD contact but prevents assuming
    // that a wide shank is clear of the original stock below the facing plane.
    const g = runtime([...cylinder(), 10,10,6, 11,10,6, 10,11,6], [cylinderHint]);
    assert.ok(g.verifier.contact(sample, up, tool));
    assert.equal(g.verifier.handoff(sample, up, tool, prep), null);
});
