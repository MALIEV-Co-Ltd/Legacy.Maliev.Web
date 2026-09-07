const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const page = fs.readFileSync(path.resolve(__dirname, '../../Legacy.Maliev.Web/Pages/InstantQuotation/CNC-Machining.cshtml'), 'utf8');
function requirementsContext(isCncQuotation = true) {
    const context = vm.createContext({ isCncQuotation });
    vm.runInContext(page.slice(page.indexOf('        function DefaultCncRequirements()'),
        page.indexOf('        function SyncCncMachineCapabilityBadge(')), context);
    vm.runInContext(page.slice(page.indexOf('        function CncRequirementInput('),
        page.indexOf('        function CncDimensions(')), context);
    return context;
}

for (const proxies of [[{ id: 'thread-1', majorDiameterMm: 6, pitchMm: 1 }], { count: 2 }]) {
    test('detected threads remain advisory without adding an extra requested tap: ' + JSON.stringify(proxies), () => {
        const c = requirementsContext();
        const item = { modelInfo: { cncGeometry: { threadProxies: proxies } } };
        assert.equal(c.CncRequirementsForItem(item).threadMode, 'none');
        assert.equal(c.CncRequirementInput(item).threads.length, 0);
    });
}

test('detection preserves manual thread specifications and unrelated requirements', () => {
    const c = requirementsContext();
    const item = { modelInfo: { cncGeometry: { threadProxies: [{ id: 'thread-1' }] } },
        cncRequirements: { ...c.DefaultCncRequirements(), threadMode: 'note', threadNote: 'M6 x 1, depth 12 mm', note: 'Keep this datum', roughness: '1.6' } };
    const result = c.CncRequirementsForItem(item);
    assert.equal(result.threadMode, 'note');
    assert.equal(result.threadNote, 'M6 x 1, depth 12 mm');
    assert.equal(result.note, 'Keep this datum');
    assert.equal(result.roughness, '1.6');
});

test('unthreaded parts, pending geometry and additive parts keep their own defaults', () => {
    for (const geometry of [undefined, {}, { threadProxies: [] }, { threadProxies: { count: 0 } }]) {
        const c = requirementsContext();
        assert.equal(c.CncRequirementsForItem({ modelInfo: { cncGeometry: geometry } }).threadMode, 'none');
    }
    const additive = requirementsContext(false);
    assert.equal(additive.CncRequirementsForItem({ modelInfo: { cncGeometry: { threadProxies: [{ id: 'thread-1' }] } } }).threadMode, 'none');
});
