const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function stock() {
    const context = vm.createContext({});
    context.window = context;
    for (const name of ['cnc-quotation-config', 'cnc-material-catalog', 'cnc-stock']) {
        vm.runInContext(fs.readFileSync(path.join(__dirname,
            '../wwwroot/src/app/js/cnc-quotation', name + '.js'), 'utf8'), context);
    }
    return context.CncStock;
}

test('6061 stock procurement includes shipping once per order', () => {
    const result = stock().selectStock({ alloy: '6061', quantity: 2,
        partSizeMm: { x: 125, y: 125, z: 20 }, partVolumeMm3: 312500,
        flatPlateEligible: false, clampBorderMm: 25 });
    assert.equal(result.stockSizeMm.z, 25);
    assert.equal(result.pieces, 2);
    assert.equal(result.supplierShippingBeforeVat, 200);
    assert.equal(result.landedStockBeforeVat,
        result.bufferedPiecePriceBeforeVat * 2 + 200);
});

test('certified rotational disc chooses feasible round stock', () => {
    const result = stock().selectStock({ material: '6061', alloy: '6061', quantity: 1,
        partSizeMm: { x: 23, y: 23, z: 5 }, partVolumeMm3: 2077,
        flatPlateEligible: true,
        rotationalEvidence: { eligible: true, diameterMm: 23, lengthMm: 5, confidence: 'High' } });
    assert.equal(result.stockShape, 'round');
    assert.equal(result.strategy, 'round_bar');
    assert.equal(result.selectionReason, 'lowest_conservative_score');
});
