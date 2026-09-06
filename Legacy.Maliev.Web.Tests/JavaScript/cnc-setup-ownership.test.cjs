const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function runtime() {
    const context = vm.createContext({ console });
    context.window = context;
    for (const name of ['cnc-quotation-config', 'cnc-material-catalog', 'cnc-tool-library',
        'cnc-reach', 'cnc-fixture-clearance', 'cnc-machine-capability', 'cnc-planning']) {
        const file = name === 'cnc-planning'
            ? path.resolve(__dirname, 'fixtures/cnc-legacy-planning.test-helper.js')
            : path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation', name + '.js');
        let source = fs.readFileSync(file, 'utf8');
        if (name === 'cnc-planning') source = source.replace('/* CNC_TEST_INSTRUMENTATION_POINT */',
            'window.CncPlanningDiagnostics = Object.freeze({ plan: plan, planFixture: planFixture });');
        vm.runInContext(source, context);
    }
    return context;
}

for (const blocked of [false, true]) {
    test('recess tool consolidation ' + (blocked ? 'retains a separately reachable patch' : 'uses one verified cutter without losing work'), () => {
        const c = runtime(), z = { x: 0, y: 0, z: 1 };
        const access = Object.fromEntries(Array.from(c.CncToolLibrary.analysisProfiles(), tool => {
            const ids = tool.id === 'guhring-6734-10' ? (blocked ? [2, 3] : [1, 2, 3])
                : tool.diameterMm > 10 ? [1] : [1, 2];
            return [tool.id, { reachableSampleIds: ids, tipSampleIds: ids, fluteSampleIds: [] }];
        }));
        const plan = c.CncPlanningDiagnostics.plan({ material: '6061', requirements: { quantity: 1 },
            stock: { stockSizeMm: { x: 25, y: 30, z: 70 } },
            geometry: { bodyCount: 1, orientedSizeMm: { x: 20, y: 25, z: 60 },
                partVolumeMm3: 25000, partSurfaceAreaMm2: 100,
                orientationCandidates: [{ id: 'front', toolDirection: z, projectedFaceCoverage: 1 }],
                surfaceClusters: [{ id: 'recess', type: 'planar', normal: z, areaMm2: 100,
                    operationCodes: ['roughing', 'finishing'] }],
                accessibilityField: { surfaceSamples: [
                    { id: 1, clusterId: 'recess', areaMm2: 1 },
                    { id: 2, clusterId: 'recess', areaMm2: 80 },
                    { id: 3, clusterId: 'recess', areaMm2: 19 }], toolAccess: { front: access } } }
        });
        for (const code of ['roughing', 'finishing']) {
            const ops = plan.operations.filter(op => op.code === code);
            assert.equal(ops.length, blocked ? 2 : 1);
            assert.ok(ops.some(op => op.toolId === 'guhring-6734-10'));
            assert.deepEqual(Array.from(ops).flatMap(op => Array.from(op.featureSampleIds)).sort(), [1, 2, 3]);
            assert.equal(ops.reduce((total, op) => total + op.featureAreaMm2, 0), 100);
        }
    });
}

test('a later recess setup does not take exterior work already reachable in the main setups', () => {
    const c = runtime();
    const z = { x: 0, y: 0, z: 1 }, back = { x: 0, y: 0, z: -1 }, y = { x: 0, y: 1, z: 0 };
    const samples = [
        { id: 1, clusterId: 'front', areaMm2: 2000 },
        { id: 2, clusterId: 'back', areaMm2: 2000 },
        { id: 3, clusterId: 'exterior', areaMm2: 100 },
        { id: 4, clusterId: 'exterior', areaMm2: 200 },
        { id: 5, clusterId: 'recess', areaMm2: 100 },
        { id: 6, clusterId: 'exterior', areaMm2: 0.1 }
    ];
    const access = ids => Object.fromEntries(Array.from(c.CncToolLibrary.analysisProfiles(), tool =>
        [tool.id, { reachableSampleIds: ids, tipSampleIds: ids, fluteSampleIds: [] }]));
    const plan = c.CncPlanningDiagnostics.plan({ material: '6061', requirements: { quantity: 1 },
        stock: { stockSizeMm: { x: 50, y: 75, z: 35 }, confidence: 'High' },
        geometry: { bodyCount: 1, orientedSizeMm: { x: 40, y: 65, z: 25 },
            partVolumeMm3: 30000, partSurfaceAreaMm2: 4400,
            orientationCandidates: [
                { id: 'front', toolDirection: z, projectedFaceCoverage: 0.45 },
                { id: 'back', toolDirection: back, projectedFaceCoverage: 0.45 },
                { id: 'side', toolDirection: y, projectedFaceCoverage: 0.1 }],
            surfaceClusters: [
                { id: 'front', type: 'planar', normal: z, areaMm2: 2000 },
                { id: 'back', type: 'planar', normal: back, areaMm2: 2000 },
                { id: 'exterior', type: 'unresolved', areaMm2: 300 },
                { id: 'recess', type: 'unresolved', areaMm2: 100 }],
            accessibilityField: { surfaceSamples: samples, toolAccess: {
                front: access([1, 3]), back: access([2, 4]), side: access([3, 4, 5, 6]) } } }
    });
    assert.equal(plan.setupCount, 3);
    const side = plan.setups.find(setup => setup.id === 'side');
    assert.equal(side.number, 3);
    assert.deepEqual(Array.from(plan.deferredSurfaceSampleIds), [6],
        'sub-resolution exterior seam residue stays explicit, not charged as remachining or claimed complete');
    for (const code of ['roughing', 'finishing']) {
        const operations = plan.operations.filter(operation => operation.code === code);
        const sideWork = operations.filter(operation => operation.setupNumber === side.number);
        assert.deepEqual(Array.from(sideWork).flatMap(operation => Array.from(operation.featureSampleIds)), [5]);
        const owned = Array.from(operations).flatMap(operation => Array.from(operation.featureSampleIds));
        assert.equal(new Set(owned).size, owned.length);
        assert.ok(owned.includes(3) && owned.includes(4), 'exterior work must be retained, not discarded');
    }
});
