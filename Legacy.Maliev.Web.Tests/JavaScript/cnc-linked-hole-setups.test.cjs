const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function planner() {
    const context = vm.createContext({ console });
    context.window = context;
    for (const name of ['cnc-quotation-config', 'cnc-material-catalog', 'cnc-tool-library',
        'cnc-reach', 'cnc-fixture-clearance', 'cnc-machine-capability', 'cnc-planning']) {
        const file = name === 'cnc-planning'
            ? path.resolve(__dirname, 'fixtures/cnc-legacy-planning.test-helper.js')
            : path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation', name + '.js');
        let source = fs.readFileSync(file, 'utf8');
        if (name === 'cnc-planning') {
            source = source.replace('/* CNC_TEST_INSTRUMENTATION_POINT */',
                'window.assignForTest = assignOperations; window.CncPlanningDiagnostics = Object.freeze({ plan: plan, planFixture: planFixture });');
        }
        vm.runInContext(source, context);
    }
    return context;
}

function assign(spotSides, tapSide, spotToolId = 'spot-drill-12') {
    const context = planner();
    const axis = { x: 0, y: 0, z: 1 };
    const setups = [1, 2].map(number => ({ id: 'setup-' + number, number,
        direction: { x: 0, y: 0, z: number === 1 ? 1 : -1 }, operationIds: [], toolIds: [] }));
    const operations = spotSides.map((side, index) => ({
        id: 'spot-' + index, code: 'spot_drilling', toolFamily: 'spot_drill',
        drillHoleId: 'hole', featureClusterIds: ['mouth-' + index], featureAxis: axis,
        includedAngleDegrees: 90, majorDiameterMm: 11.2, minorDiameterMm: 5, estimatedMinutes: 1
    }));
    operations.push({ id: 'drill', code: 'drilling', toolFamily: 'drill', drillHoleId: 'hole',
        featureClusterIds: ['bore'], featureAxis: axis, targetToolDiameterMm: 5, estimatedMinutes: 1 });
    const record = (code, toolId, number, clusterId) => ({ operationCode: code, toolId,
        analysisProfileId: context.CncToolLibrary.get(toolId).analysisProfileId,
        setupNumber: number, setupId: 'setup-' + number, clusterId, reachable: true, confidence: 'High' });
    const records = [1, 2].map(number => record('drilling', 'hss-drill-5p0', number, 'bore'));
    spotSides.forEach((number, index) => records.push(record('spot_drilling', spotToolId, number, 'mouth-' + index)));
    if (tapSide) {
        operations.push({ id: 'tap', code: 'tapping', toolFamily: 'tap', drillHoleId: 'hole',
            featureClusterIds: ['thread'], featureAxis: axis, targetToolDiameterMm: 6, pitchMm: 1, estimatedMinutes: 1 });
        records.push(record('tapping', 'tap-m6p0x1p0', tapSide, 'thread'));
    }
    context.assignForTest(operations, setups, { records }, { surfaceClusters: [] }, '6061', true, {});
    return operations;
}

test('a through-hole is drilled from the side that also admits its linked spotting operation', () => {
    const operations = assign([2]);
    assert.equal(operations.find(operation => operation.id === 'drill').setupNumber, 2);
    assert.equal(operations.find(operation => operation.id === 'spot-0').setupNumber, 2);
    assert.ok(operations.every(operation => operation.reachable));
});

test('opposite mouth spots keep their own reachable setups while one drill covers the bore', () => {
    const operations = assign([1, 2]);
    assert.equal(operations.find(operation => operation.id === 'spot-0').setupNumber, 1);
    assert.equal(operations.find(operation => operation.id === 'spot-1').setupNumber, 2);
    assert.equal(operations.filter(operation => operation.code === 'drilling').length, 1);
    assert.equal(operations.find(operation => operation.id === 'drill').setupNumber, 1);
    assert.ok(operations.every(operation => operation.reachable));
});

test('tap entry breaks a drill-side tie without forcing the opposite mouth spot onto that side', () => {
    const operations = assign([1, 2], 2);
    assert.equal(operations.find(operation => operation.id === 'drill').setupNumber, 2);
    assert.equal(operations.find(operation => operation.id === 'tap').setupNumber, 2);
    assert.equal(operations.find(operation => operation.id === 'spot-0').setupNumber, 1);
    assert.equal(operations.find(operation => operation.id === 'spot-1').setupNumber, 2);
    assert.ok(operations.every(operation => operation.reachable));
});

test('a larger spot cutter cannot inherit reach from a smaller physical cutter', () => {
    const operations = assign([1], null, 'spot-drill-10');
    assert.equal(operations.find(operation => operation.id === 'spot-0').reachable, false);
    assert.equal(operations.find(operation => operation.id === 'spot-0').toolDiameterMm, null);
});

test('an HSS drill is not actionable when its required spotting operation is unreachable', () => {
    const operations = assign([1], null, 'spot-drill-10');
    const spot = operations.find(operation => operation.id === 'spot-0');
    const drill = operations.find(operation => operation.id === 'drill');

    assert.ok(operations.indexOf(spot) < operations.indexOf(drill));
    assert.equal(spot.reachable, false);
    assert.equal(drill.toolId, 'hss-drill-5p0');
    assert.equal(drill.reachable, false);
});
