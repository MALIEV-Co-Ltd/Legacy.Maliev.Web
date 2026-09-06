const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const context = vm.createContext({ console });
context.window = context;
for (const name of ['cnc-quotation-config', 'cnc-material-catalog', 'cnc-tool-library']) {
    const file = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation', name + '.js');
    vm.runInContext(fs.readFileSync(file, 'utf8'), context, { filename: file });
}
const catalog = context.CncToolLibrary;

// Independent manufacturer dimensions: OSG List 1200, EDP 62910/62912/62916.
// Incorrect point geometry or conflating flute length with the cone depth can
// select a physically unsuitable tool for a combined spot/chamfer operation.
for (const [diameter, edp, fluteLength, overallLength, minimumHole, reach] of [
    [10, '62910', 30, 93, 2.1, 63],
    [12, '62912', 36, 108, 2.1, 78],
    [16, '62916', 41, 118, 3, 88]
]) {
    test(`OSG ${edp} remains selectable for spotting and chamfering with its physical cone envelope`, () => {
        const id = 'spot-drill-' + diameter;
        const entry = catalog.get(id);
        assert.ok(entry, `Missing ${id}`);
        for (const operation of ['spot_drilling', 'chamfering']) {
            assert.ok(catalog.compatible(operation, '6061').some(tool => tool.id === id));
        }
        assert.equal(entry.family, 'spot_drill');
        assert.equal(entry.manufacturer, 'OSG');
        assert.equal(entry.catalogueCode, edp);
        assert.equal(entry.toolMaterial, 'HSS');
        assert.equal(entry.surfaceTreatment, 'BRIGHT');
        assert.equal(entry.diameterMm, diameter);
        assert.equal(entry.shankDiameterMm, diameter);
        assert.equal(entry.includedAngleDegrees, 90);
        assert.equal(entry.pointDiameterMm, 0);
        assert.equal(entry.directSpotting, true);
        assert.equal(entry.minimumHoleDiameterMm, minimumHole);
        assert.equal(entry.fluteLengthMm, fluteLength);
        assert.equal(entry.overallLengthMm, overallLength);
        assert.equal(entry.usableCutLengthMm, diameter / 2);
        assert.equal(entry.holderEngagementMm, 30);
        assert.equal(entry.reachMm, reach);
        assert.equal(entry.holderDiameterMm, 25);
        assert.equal(entry.requiresHolderVerification, true);
        assert.match(entry.sourceUrl, /^https:\/\/osgtool\.com\//);
        assert.ok(catalog.analysisProfile(entry.analysisProfileId).diameterMm >= diameter,
            'A smaller field proxy must not represent the spotting-drill diameter');
    });
}

test('combined spot/chamfer candidates do not include the replaced direct countersinks', () => {
    const candidates = catalog.compatible('chamfering', '6061');
    assert.equal(candidates.some(tool => tool.directCountersink), false);
    assert.deepEqual(Array.from(candidates.filter(tool => tool.directSpotting), tool => tool.diameterMm), [10, 12, 16]);
});
