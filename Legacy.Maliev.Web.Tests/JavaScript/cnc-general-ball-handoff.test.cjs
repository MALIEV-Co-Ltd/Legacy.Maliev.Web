const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const up = { x: 0, y: 0, z: 1 };
const ball = { id: 'ns-alb225-4', family: 'ball_end_mill', diameterMm: 4,
    usableCutLengthMm: 8, underNeckLengthMm: 8, neckDiameterMm: 4,
    shankDiameterMm: 6, reachMm: 20, holderDiameterMm: 20 };
const flat = { id: 'flat-6x18', family: 'flat_end_mill', diameterMm: 6,
    usableCutLengthMm: 18, shankDiameterMm: 6, reachMm: 30, holderDiameterMm: 32 };
function runtime(triangles) {
    const c = vm.createContext({ console }); c.self = c;
    for (const name of ['cnc-geometry.worker', 'cnc-ball-rest.worker']) {
        vm.runInContext(fs.readFileSync(path.resolve(__dirname,
            '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation', name + '.js'), 'utf8'), c);
    }
    const records = c.CncTriangleRecords(new Float32Array(triangles));
    const verifier = c.CncBallRest.createVerifier(records, [], []);
    verifier.generate = (clusters, field) => c.CncGeneralBallEvidence(records, clusters, field,
        [{ x: 1, y: 0, z: 0 }, { x: 0, y: 1, z: 0 }, up], verifier, [ball], [flat]);
    return verifier;
}
const floor = [-10,-10,0, 10,-10,0, 10,10,0, -10,-10,0, 10,10,0, -10,10,0];
const highBoss = [30,30,10, 31,30,10, 30,31,10];
const sample = { id: 1, sourceTriangleIndex: 0, contactPosition: { x: 0, y: 0, z: 0 }, normal: up };

test('a general handoff proves the larger ball shank fits the flat-cleared stock cylinder', () => {
    const verifier = runtime([...floor, ...highBoss]);
    assert.ok(verifier.contact(sample, up, ball));
    assert.equal(verifier.handoff(sample, up, ball, flat), null);
    const certificate = verifier.generalHandoff(sample, up, ball, flat, { stockTopMm: 12 });
    assert.ok(certificate, 'the shank below raw stock top is clear only after the matching flat sweep');
    assert.equal(certificate.requiresFacing, false);
    assert.equal(certificate.stockTopMm, 12);
    assert.equal(certificate.stockClearanceBasis, 'prepared-cylinder-and-stock-top');
    const shift = Math.hypot(certificate.preparationTipMm.x - certificate.ballCenterMm.x,
        certificate.preparationTipMm.y - certificate.ballCenterMm.y);
    assert.ok(shift + 3 <= 3 + 1e-5, 'the entire D6 shank, not just the D4 ball, fits');
});

test('steep curved-profile preparation is bounded and split later instead of capped to a tiny fillet allowance', () => {
    const slope = [-10,-10,-10, 10,-10,10, 10,10,10, -10,-10,-10, 10,10,10, -10,10,-10];
    const verifier = runtime(slope);
    const certificate = verifier.generalHandoff(sample, up, ball, flat, { stockTopMm: 12 });
    assert.ok(certificate);
    assert.ok(certificate.residualAxialCapMm > .75 && certificate.residualAxialCapMm <= 4);
    assert.equal(certificate.preparationToolId, 'flat-6x18');
    assert.equal(certificate.ballToolId, 'ns-alb225-4');
    assert.equal(certificate.camCertain, false);
});

test('general stock handoff rejects unverified stock height, oversized shank and a stock-buried holder', () => {
    const verifier = runtime([...floor, ...highBoss]);
    assert.equal(verifier.generalHandoff(sample, up, ball, flat, { stockTopMm: 9 }), null);
    assert.equal(verifier.generalHandoff(sample, up, ball, flat, { stockTopMm: NaN }), null);
    assert.equal(verifier.generalHandoff(sample, up, { ...ball, shankDiameterMm: 8 }, flat, { stockTopMm: 12 }), null);
    assert.equal(verifier.generalHandoff(sample, up, ball, flat, { stockTopMm: 21 }), null);
    assert.equal(verifier.generalHandoff(sample, up, ball, { ...flat, diameterMm: 3 }, { stockTopMm: 12 }), null);
});

test('confirmed CAD occlusion cannot be overridden by a general stock preparation certificate', () => {
    const verifier = runtime([...floor, -5,-5,3, 5,-5,3, 0,5,3]);
    assert.equal(verifier.generalHandoff(sample, up, ball, flat, { stockTopMm: 5 }), null);
});

test('an equal-diameter flat preparation can contain the complete ball and same-diameter shank', () => {
    const verifier = runtime([...floor, ...highBoss]);
    const larger = { ...ball, id: 'ball-6', diameterMm: 6, usableCutLengthMm: 12,
        underNeckLengthMm: 12, neckDiameterMm: 6, reachMm: 24 };
    assert.ok(verifier.generalHandoff(sample, up, larger, flat, { stockTopMm: 14 }));
});

test('verified R3 tool uses its own physical envelope and staged aluminium cutting conditions', () => {
    const c = vm.createContext({ console }); c.self = c;
    for (const name of ['cnc-quotation-config', 'cnc-material-catalog', 'cnc-tool-library']) {
        vm.runInContext(fs.readFileSync(path.resolve(__dirname,
            '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation', name + '.js'), 'utf8'), c);
    }
    const tool = c.CncToolLibrary.get('ns-alb225-6');
    assert.ok(tool);
    assert.equal(tool.catalogueCode, '01-00638-30001');
    assert.equal(tool.usableCutLengthMm, 12);
    assert.equal(tool.shankDiameterMm, 6);
    assert.equal(tool.reachMm, 24, 'configured projection must not borrow the 90 mm overall length');
    const policy = c.CncToolLibrary.estimateBallRestPasses({ material: '6061', diameterMm: 6,
        residualAxialCapMm: 2.5, areaMm2: 60, spindleLimitRpm: 6000 });
    assert.equal(policy.feedMmPerMinute, 1500);
    assert.equal(policy.axialStepMm, 1);
    assert.equal(policy.passes.length, 3);
    assert.equal(policy.passes[2].axialOffsetMm, 0);
});

test('worker certificate generation covers only the explicitly visible curved samples and exports the facing prerequisite', () => {
    const verifier = runtime([...floor, ...highBoss]);
    const clusters = [{ id: 'skin', curvedFinishingByDirection: {
        'positive-z': { triangleIndexes: [0], method: 'triangle-normal-variation', camCertain: false },
        'negative-z': { triangleIndexes: [], method: 'triangle-normal-variation', camCertain: false }
    } }];
    const field = { surfaceSamples: [ { ...sample, clusterId: 'skin', areaMm2: 2 },
        { ...sample, id: 2, clusterId: 'other', sourceTriangleIndex: 1 } ], toolAccess: {} };
    const result = verifier.generate(clusters, field);
    assert.equal(result.handoffs.length, 1);
    assert.equal(result.handoffs[0].sampleId, 1);
    assert.equal(result.handoffs[0].directionId, 'positive-z');
    assert.equal(result.handoffs[0].requiresFacing, true);
    assert.equal(result.finishingAccess.length, 1);
    assert.deepEqual(Array.from(result.finishingAccess[0].sampleIds), [1]);
    assert.equal(result.stockFacingRequirements[0].planeProjectionMm, 10);
    assert.equal(result.stockFacingRequirements[0].method, 'model-exterior-plane');
});

test('a bounded worker never promotes unchecked samples to stock certificates through their shared face', () => {
    const verifier = runtime(floor);
    const clusters = [{ id: 'skin', curvedFinishingByDirection: {
        'positive-z': { triangleIndexes: [0], method: 'triangle-normal-variation', camCertain: false }
    } }];
    const field = { surfaceSamples: Array.from({ length: 513 }, (_, index) =>
        ({ ...sample, id: index + 1, clusterId: 'skin', areaMm2: 1 })), toolAccess: {} };
    const result = verifier.generate(clusters, field);
    assert.equal(result.finishingAccess[0].sampleIds.length, 513);
    assert.equal(result.handoffs.length, 512);
    assert.equal(result.handoffs.some(entry => entry.sampleId === 513), false);
    assert.equal(result.limits.omittedHandoffSamples, 1);
});

test('samples already prepared by a main flat cutter need contact but no redundant rest search', () => {
    const verifier = runtime(floor);
    const clusters = [{id:'skin',curvedFinishingByDirection:{'positive-z':{
        triangleIndexes:[0],method:'triangle-normal-variation',camCertain:false}}}];
    const field = {surfaceSamples:[{...sample,clusterId:'skin',areaMm2:2}],
        toolAccess:{'positive-z':{'flat-6x18':{reachableSampleIds:[1]}}}};
    let searches = 0;
    const original = verifier.generalHandoff;
    verifier.generalHandoff = (...args) => { searches++; return original(...args); };
    const result = verifier.generate(clusters,field);
    assert.equal(searches,0);
    assert.equal(result.handoffs.length,0);
    assert.deepEqual(Array.from(result.finishingAccess[0].sampleIds),[1]);
});
