const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function planCurved({ typed = false, blocked = false, empty = false } = {}) {
    const c = vm.createContext({ console }); c.window = c;
    for (const name of ['cnc-quotation-config', 'cnc-material-catalog', 'cnc-tool-library',
        'cnc-reach', 'cnc-fixture-clearance', 'cnc-machine-capability', 'cnc-planning']) {
        const file = name === 'cnc-planning'
            ? path.resolve(__dirname, 'fixtures/cnc-legacy-planning.test-helper.js')
            : path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation', name + '.js');
        let source = fs.readFileSync(file, 'utf8');
        if (name === 'cnc-planning') source = source.replace('/* CNC_TEST_INSTRUMENTATION_POINT */',
            'window.CncPlanningDiagnostics = Object.freeze({ plan: plan, planFixture: planFixture });');
        vm.runInContext(source, c);
    }
    const access = ids => Object.fromEntries(Array.from(c.CncToolLibrary.analysisProfiles(), tool =>
        [tool.id, { reachableSampleIds: ids, tipSampleIds: ids, fluteSampleIds: [] }]));
    return c.CncPlanningDiagnostics.plan({ material: '6061', requirements: { quantity: 1 },
        stock: { stockSizeMm: { x: 45, y: 55, z: 25 } },
        geometry: { bodyCount: 1, orientedSizeMm: { x: 40, y: 50, z: 20 },
            partVolumeMm3: 20000, partSurfaceAreaMm2: 300,
            orientationCandidates: [
                { id: 'front', toolDirection: { x: 0, y: 0, z: 1 }, projectedFaceCoverage: .5 },
                { id: 'back', toolDirection: { x: 0, y: 0, z: -1 }, projectedFaceCoverage: .5 }],
            surfaceClusters: [{ id: 'shell', type: typed ? 'freeform' : 'unresolved', areaMm2: 300,
                filletFeatures: [], accessibleDirectionIds: ['front', 'back'],
                ...(empty ? {curvedFinishingByDirection: {}} : typed ? {} : { curvedFinishingByDirection: {
                    front: { triangleIndexes: [10], areaMm2: 100, method: 'triangle-normal-variation', camCertain: false },
                    back: { triangleIndexes: [30], areaMm2: 100, method: 'triangle-normal-variation', camCertain: false }
                } }) }],
            generalBallFinishingAccess: [
                { directionId: 'front', toolId: 'ns-alb225-4', sampleIds: blocked ? [2] : [1, 2], triangleIndexes: [10, 20], method: 'sampled-ball-contact', camCertain: false },
                { directionId: 'back', toolId: 'ns-alb225-4', sampleIds: [3], triangleIndexes: [30], method: 'sampled-ball-contact', camCertain: false }],
            accessibilityField: { surfaceSamples: [
                { id: 1, clusterId: 'shell', sourceTriangleIndex: 10, areaMm2: 100 },
                { id: 2, clusterId: 'shell', sourceTriangleIndex: 20, areaMm2: 100 },
                { id: 3, clusterId: 'shell', sourceTriangleIndex: 30, areaMm2: 100 }],
                toolAccess: { front: access(blocked ? [2] : [1, 2]), back: access([3]) } } }
    });
}

test('general freeform reach becomes actual ball operations in both owning setups', () => {
    const plan = planCurved({ typed: true });
    const balls = plan.operations.filter(op => op.code === 'freeform_finishing' && op.reachable);
    assert.equal(new Set(balls.map(op => op.setupNumber)).size, 2);
    assert.deepEqual(Array.from(balls).flatMap(op => Array.from(op.featureSampleIds)).sort(), [1, 2, 3]);
    assert.ok(balls.every(op => op.toolFamily === 'ball_end_mill'));
});

test('directional curved patches replace only their own flat finishing work', () => {
    const plan = planCurved();
    const balls = plan.operations.filter(op => op.code === 'freeform_finishing' && op.reachable);
    assert.deepEqual(Array.from(balls).flatMap(op => Array.from(op.featureSampleIds)).sort(), [1, 3]);
    const flats = plan.operations.filter(op => op.code === 'finishing' && op.reachable);
    assert.deepEqual(Array.from(flats).flatMap(op => Array.from(op.featureSampleIds)), [2]);
    assert.ok(balls.every(op => op.cuttingMinutes > 0));
});

test('unreachable curved patch remains an explicit review operation, not flat finished', () => {
    const plan = planCurved({ blocked: true });
    const missing = plan.operations.find(op => op.code === 'freeform_finishing' && !op.reachable);
    assert.ok(missing);
    assert.equal(missing.toolId, null, 'a review region cannot invent an unverified cutter');
    assert.equal(missing.toolDiameterMm, null);
    assert.ok(plan.reviewReasons.includes('unreachable_tool_access'));
    assert.equal(plan.operations.filter(op => op.code === 'finishing').some(op => (op.featureSampleIds || []).includes(1)), false);
});

test('small curved-contact boundary residue does not become a fake tool-review operation', () => {
    const c = vm.createContext({ console }); c.window = c;
    for (const name of ['cnc-quotation-config', 'cnc-material-catalog', 'cnc-tool-library',
        'cnc-reach', 'cnc-fixture-clearance', 'cnc-machine-capability', 'cnc-planning']) {
        const file = name === 'cnc-planning'
            ? path.resolve(__dirname, 'fixtures/cnc-legacy-planning.test-helper.js')
            : path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation', name + '.js');
        let source = fs.readFileSync(file, 'utf8');
        if (name === 'cnc-planning') source = source.replace('/* CNC_TEST_INSTRUMENTATION_POINT */',
            'window.CncPlanningDiagnostics = Object.freeze({ plan: plan, planFixture: planFixture });');
        vm.runInContext(source, c);
    }
    const samples = Array.from({ length: 100 }, (_, index) => ({
        id: index + 1, clusterId: 'shell', sourceTriangleIndex: 10, areaMm2: 1
    }));
    const coveredIds = samples.slice(1).map(sample => sample.id);
    const access = Object.fromEntries(Array.from(c.CncToolLibrary.analysisProfiles(), tool =>
        [tool.id, { reachableSampleIds: coveredIds, tipSampleIds: coveredIds, fluteSampleIds: [] }]));
    const plan = c.CncPlanningDiagnostics.plan({ material: '6061', requirements: { quantity: 1 },
        stock: { stockSizeMm: { x: 45, y: 55, z: 25 } },
        geometry: { bodyCount: 1, orientedSizeMm: { x: 40, y: 50, z: 20 },
            partVolumeMm3: 20000, partSurfaceAreaMm2: 100,
            orientationCandidates: [
                { id: 'front', toolDirection: { x: 0, y: 0, z: 1 }, projectedFaceCoverage: 1 }],
            surfaceClusters: [{ id: 'shell', type: 'unresolved', areaMm2: 100,
                filletFeatures: [], accessibleDirectionIds: ['front'], curvedFinishingByDirection: {
                    front: { triangleIndexes: [10], areaMm2: 100,
                        method: 'triangle-normal-variation', camCertain: false }
                } }],
            generalBallFinishingAccess: [{ directionId: 'front', toolId: 'ns-alb225-4',
                sampleIds: coveredIds, triangleIndexes: [10], method: 'sampled-ball-contact', camCertain: false }],
            accessibilityField: { surfaceSamples: samples, toolAccess: { front: access } } }
    });

    assert.equal(plan.operations.some(op => op.code === 'freeform_finishing' && !op.reachable), false);
    assert.ok(plan.operations.some(op => op.code === 'freeform_finishing' && op.toolId === 'ns-alb225-4'));
});

test('authoritative empty curved patches do not resurrect legacy whole-cluster ball work', () => {
    const plan = planCurved({typed: true, empty: true});
    assert.equal(plan.operations.some(op => op.sampledSurfaceFinishing), false);
    assert.ok(plan.operations.some(op => op.code === 'finishing'));
});
