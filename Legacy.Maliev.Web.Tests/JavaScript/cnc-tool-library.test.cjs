const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function library() {
    const context = vm.createContext({ console });
    context.window = context;
    for (const name of ['cnc-quotation-config', 'cnc-material-catalog', 'cnc-tool-library']) {
        const file = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation', name + '.js');
        vm.runInContext(fs.readFileSync(file, 'utf8'), context, { filename: file });
    }
    return context.CncToolLibrary;
}

test('verified long aluminium cutter has its own bounded clearance envelope', () => {
    const tools = library();
    const cutter = tools.compatible('roughing', '6061').find(t => t.id === 'guhring-6734-10');
    assert.ok(cutter, 'the verified market cutter must be available for aluminium');
    assert.equal(cutter.usableCutLengthMm, 50);
    assert.equal(cutter.overallLengthMm, 100);
    assert.equal(cutter.shankDiameterMm, 10);
    assert.ok(cutter.reachMm < cutter.overallLengthMm, 'overall length is not holder clearance');
    const envelope = tools.analysisProfile(cutter.analysisProfileId);
    assert.equal(envelope.usableCutLengthMm, 50);
    assert.equal(envelope.diameterMm, 10);
    assert.equal(envelope.holderDiameterMm, cutter.holderDiameterMm);
    assert.ok(!tools.compatible('roughing', 'SUS304').some(t => t.id === cutter.id));
});

test('physical 10 mm flat mills use 10 mm clearance instead of a 16 mm obstruction envelope', () => {
    const tools = library();
    for (const id of ['flat-10-standard', 'flat-10-2d', 'flat-10-10d']) {
        const tool = tools.get(id);
        const profile = tools.analysisProfile(tool.analysisProfileId);
        assert.equal(tool.diameterMm, 10);
        assert.equal(profile.diameterMm, 10, id + ' must not inherit a 16 mm diameter proxy');
        assert.ok(profile.usableCutLengthMm <= tool.usableCutLengthMm);
        assert.ok(profile.reachMm <= tool.reachMm);
    }
});

test('adding 10 mm clearance keeps catalog budgets, reach classes and other diameter mappings', () => {
    const tools = library();
    assert.ok(tools.analysisProfiles().length <= 40);
    assert.ok(tools.planningTools().length <= 60);
    assert.ok(tools.list().every(tool => tools.analysisProfile(tool.analysisProfileId)));
    assert.ok(Object.isFrozen(tools.analysisProfiles()));
    assert.ok(tools.analysisProfiles().every(profile => Object.isFrozen(profile)));
    assert.ok(tools.planningTools().some(tool => tool.id === 'flat-4x70'));
    for (const [id, profileId] of [
        ['flat-3-2d', 'analysis-flat-4-2d'],
        ['flat-5-2d', 'analysis-flat-8-2d'],
        ['flat-6x18', 'flat-6x18'],
        ['flat-8-2d', 'analysis-flat-8-2d'],
        ['flat-12-2d', 'analysis-flat-16-2d'],
        ['flat-16-2d', 'analysis-flat-16-2d'],
        ['flat-10-4d', 'analysis-flat-10-2d'],
        ['flat-4x70', 'flat-4x70']
    ]) {
        assert.equal(tools.get(id).analysisProfileId, profileId);
    }
});

test('planning candidates never borrow a smaller flat cutter clearance envelope', () => {
    const tools = library();
    assert.ok(tools.get('indexable-25-standard'));
    assert.ok(tools.list().some(tool => tool.family === 'slot_cutter' && tool.cutterWidthMm === 4));
    assert.ok(tools.planningTools().some(tool => tool.diameterMm === 10 && tool.family === 'flat_end_mill'));
    for (const tool of tools.planningTools()) {
        const profile = tools.analysisProfile(tool.analysisProfileId);
        if (profile.family === 'flat_end_mill') {
            assert.ok(profile.diameterMm >= tool.diameterMm,
                tool.id + ' cannot use clearance for a smaller physical cutter');
        }
    }
    assert.ok(tools.planningTools().some(tool => tool.family === 'indexable_end_mill'));
});

test('compatible fallback cannot resurrect unsupported larger cutter diameters', () => {
    const tools = library();
    for (const operation of ['roughing', 'finishing', 'slotting']) {
        for (const material of ['6061', 'POM']) {
            const candidates = tools.compatible(operation, material);
            assert.ok(Object.isFrozen(candidates));
            for (const tool of candidates) {
                const profile = tools.analysisProfile(tool.analysisProfileId);
                if (profile.family === 'flat_end_mill') {
                    assert.ok(profile.diameterMm >= tool.diameterMm,
                        operation + ' fallback must not select ' + tool.id + ' using a smaller profile');
                }
            }
        }
    }
    assert.ok(tools.compatible('roughing', '6061').some(tool => tool.id === 'flat-10-standard'));
    assert.ok(tools.compatible('roughing', '6061').some(tool => tool.id === 'indexable-16-standard'));
});
