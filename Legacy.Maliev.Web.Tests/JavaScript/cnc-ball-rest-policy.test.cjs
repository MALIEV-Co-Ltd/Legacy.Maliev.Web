const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function library() {
    const c = vm.createContext({ console });
    c.self = c;
    for (const name of ['cnc-quotation-config', 'cnc-material-catalog', 'cnc-tool-library']) {
        vm.runInContext(fs.readFileSync(path.resolve(__dirname,
            '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation', name + '.js'), 'utf8'), c);
    }
    assert.equal(typeof c.CncToolLibrary.estimateBallRestPasses, 'function');
    return c.CncToolLibrary;
}

test('market ball rest policy costs ordered layers instead of one heavy finishing pass', () => {
    const result = library().estimateBallRestPasses({ material: '6061', diameterMm: 1,
        residualAxialCapMm: 0.75, areaMm2: 45, spindleLimitRpm: 12000 });
    assert.ok(result);
    assert.equal(result.tool.catalogueCode, '01-00638-05011');
    assert.equal(result.tool.shankDiameterMm, 4, 'a 1 mm cutting diameter is not a 1 mm shank');
    assert.equal(result.tool.underNeckLengthMm, 5);
    assert.equal(result.axialStepMm, 0.25, 'the .75 mm flute leaves .25 mm above the ball centre');
    assert.deepEqual(Array.from(result.passes, pass => pass.axialOffsetMm), [0.5, 0.25, 0]);
    assert.equal(result.feedMmPerMinute, 900, 'feed follows the capped spindle, not the uncapped catalog rpm');
    assert.deepEqual(Array.from(result.passes, pass => pass.stepoverMm), [0.3, 0.3, 0.05]);
    assert.ok(Math.abs(result.cuttingMinutes - 4 / 3) < 1e-10);
    assert.equal(result.requiresStockAndClearanceCertificate, true, 'cutting data alone must not suppress a flat operation');
    assert.equal(result.materialBasis, 'aluminium-alloy-estimate');
});

test('the R2 ball uses its own published cutting conditions and costs its final finishing pass', () => {
    const result = library().estimateBallRestPasses({ material: '7075', diameterMm: 4,
        residualAxialCapMm: 0.75, areaMm2: 300, spindleLimitRpm: 20000 });
    assert.equal(result.tool.catalogueCode, '01-00638-20001');
    assert.equal(result.spindleRpm, 14000);
    assert.equal(result.feedMmPerMinute, 2000);
    assert.deepEqual(Array.from(result.passes, pass => pass.axialOffsetMm), [0.375, 0]);
    assert.deepEqual(Array.from(result.passes, pass => pass.stepoverMm), [1.5, 0.15]);
    assert.ok(Math.abs(result.cuttingMinutes - 1.1) < 1e-10);
    assert.equal(result.materialBasis, 'manufacturer-listed-alloy');
});

test('unsupported materials, unknown tool sizes and malformed stock bounds cannot borrow aluminium cutting conditions', () => {
    const tools = library();
    const input = { material: '6061', diameterMm: 1, residualAxialCapMm: 0.2,
        areaMm2: 20, spindleLimitRpm: 12000 };
    for (const change of [{ material: 'SUS304' }, { material: 'POM' }, { material: 'C3604' },
        { material: 'unknown' }, { diameterMm: 2 }, { residualAxialCapMm: NaN },
        { residualAxialCapMm: -1 }, { areaMm2: 0 }, { spindleLimitRpm: null }]) {
        assert.equal(tools.estimateBallRestPasses({ ...input, ...change }), null);
    }
});

test('zero proven residual retains one finishing pass and results are immutable', () => {
    const result = library().estimateBallRestPasses({ material: '6061', diameterMm: 1,
        residualAxialCapMm: 0, areaMm2: 45, spindleLimitRpm: 12000 });
    assert.equal(result.passes.length, 1);
    assert.equal(result.cuttingMinutes, 1);
    assert.ok(Object.isFrozen(result));
    assert.ok(Object.isFrozen(result.tool));
    assert.ok(Object.isFrozen(result.passes[0]));
});
