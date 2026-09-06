const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function loadPlanner() {
    const context = vm.createContext({console});
    context.window = context;
    for (const name of ['cnc-quotation-config', 'cnc-material-catalog', 'cnc-tool-library',
        'cnc-reach', 'cnc-fixture-clearance', 'cnc-machine-capability', 'cnc-planning']) {
        const file = name === 'cnc-planning'
            ? path.resolve(__dirname, 'fixtures/cnc-legacy-planning.test-helper.js')
            : path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation', name + '.js');
        let source = fs.readFileSync(file, 'utf8');
        if (name === 'cnc-planning') {
            // Expose the actual private boundaries only inside this test VM.
            source = source.replace('/* CNC_TEST_INSTRUMENTATION_POINT */',
                'window.assignForReview = assignOperations; window.handoffForReview = applyCurvedStockHandoffs; window.CncPlanningDiagnostics = Object.freeze({ plan: plan, planFixture: planFixture });');
        }
        vm.runInContext(source, context);
    }
    return context;
}

function setup() {
    return {id: 'front', number: 1, direction: {x: 0, y: 0, z: 1}, operationIds: [], toolIds: []};
}

test('unrelated curved evidence cannot delete untouched unpartitioned roughing work', () => {
    const context = loadPlanner();
    const operations = [{id: 'unpartitioned-rough', code: 'roughing', toolFamily: 'flat_end_mill',
        toolId: 'flat-10-2d', toolDiameterMm: 10, setupNumber: 1, reachable: true,
        featureSampleIds: [], featureAreaMm2: 250, estimatedMinutes: 12}];
    const before = JSON.stringify(operations);
    const setups = [setup()];
    context.handoffForReview(operations, setups, {
        accessibilityField: {surfaceSamples: []},
        generalBallRestHandoffs: [{sampleId: 999, directionId: 'other-setup'}]
    }, '6061', 12000);
    assert.equal(JSON.stringify(operations), before,
        'only a source actually emptied by a successful handoff may be removed');
    assert.deepEqual(Array.from(setups[0].operationIds), ['unpartitioned-rough']);
});

test('ball cutter consolidation retains a faster cutter when extra cutting exceeds tool-change cost', () => {
    const context = loadPlanner();
    const setups = [setup()];
    const operations = [{id: 'curved-shell', code: 'freeform_finishing', toolFamily: 'ball_end_mill',
        sampledSurfaceFinishing: true, featureClusterIds: ['shell'], featureTriangleIndexes: [10, 20],
        featureAreaMm2: 30000, estimatedMinutes: 0, allowedDirectionIds: ['front']}];
    const records = [['ns-alb225-6', [1]], ['ns-alb225-4', [1, 2]]].map(([toolId, ids]) => ({
        clusterId: 'shell', setupId: 'front', setupNumber: 1, toolId,
        operationCode: 'freeform_finishing', reachable: true, reachableSampleIds: ids,
        fieldSampleCount: ids.length, accessEvidence: 'sampled-ball-contact', confidence: 'Medium'
    }));
    context.assignForReview(operations, setups, {records}, {
        surfaceClusters: [{id: 'shell', areaMm2: 30000}],
        accessibilityField: {surfaceSamples: [
            {id: 1, clusterId: 'shell', sourceTriangleIndex: 10, areaMm2: 10000},
            {id: 2, clusterId: 'shell', sourceTriangleIndex: 20, areaMm2: 20000}
        ]}
    }, '6061', true, {roughing: 1000, finishing: 1000});
    const owners = new Map(operations.flatMap(operation =>
        Array.from(operation.featureSampleIds || []).map(id => [id, operation])));
    assert.equal(owners.get(1).toolId, 'ns-alb225-6',
        'D4 can reach sample 1, but its additional cutting is not free');
    assert.equal(owners.get(2).toolId, 'ns-alb225-4');
    assert.equal(operations.length, 2);
    assert.ok(operations.every(operation => operation.reachable));
});

test('ordinary curved finishing without a rest transfer retains the market cutting-time floor', () => {
    const context = loadPlanner();
    const access = Object.fromEntries(Array.from(context.CncToolLibrary.analysisProfiles()).map(tool =>
        [tool.id, {reachableSampleIds: [1, 2, 3], tipSampleIds: [1, 2, 3], fluteSampleIds: []}]));
    const curvedCluster = (id, triangleIndex, areaMm2) => ({id, type: 'unresolved', areaMm2,
        filletFeatures: [], accessibleDirectionIds: ['front'], curvedFinishingByDirection: {
            front: {triangleIndexes: [triangleIndex], areaMm2, method: 'triangle-normal-variation', camCertain: false}
        }});
    const plan = context.CncPlanningDiagnostics.plan({material: '6061', requirements: {quantity: 1},
        stock: {stockSizeMm: {x: 45, y: 55, z: 25}}, geometry: {
            bodyCount: 1, orientedSizeMm: {x: 40, y: 50, z: 20},
            partVolumeMm3: 20000, partSurfaceAreaMm2: 30100,
            orientationCandidates: [{id: 'front', toolDirection: {x: 0, y: 0, z: 1}, projectedFaceCoverage: 1}],
            surfaceClusters: [curvedCluster('large-radius', 10, 10000), curvedCluster('small-radius', 20, 20000),
                {id: 'stock-face', type: 'planar', areaMm2: 100, normal: {x: 0, y: 0, z: 1}, accessibleDirectionIds: ['front']}],
            generalBallFinishingAccess: [
                {directionId: 'front', toolId: 'ns-alb225-6', sampleIds: [1], triangleIndexes: [10],
                    method: 'sampled-ball-contact', camCertain: false},
                {directionId: 'front', toolId: 'ns-alb225-4', sampleIds: [2], triangleIndexes: [20],
                    method: 'sampled-ball-contact', camCertain: false}
            ], generalBallRestHandoffs: [],
            accessibilityField: {surfaceSamples: [
                {id: 1, clusterId: 'large-radius', sourceTriangleIndex: 10, areaMm2: 10000},
                {id: 2, clusterId: 'small-radius', sourceTriangleIndex: 20, areaMm2: 20000},
                {id: 3, clusterId: 'stock-face', sourceTriangleIndex: 30, areaMm2: 100}
            ], toolAccess: {front: access}}
        }});
    const balls = Array.from(plan.operations).filter(operation => operation.sampledSurfaceFinishing && operation.reachable);
    assert.equal(balls.length, 2);
    const expected = new Map([['ns-alb225-6', 10000 / (3000 * 0.2)],
        ['ns-alb225-4', 20000 / ((12000 / 14000) * 2000 * 0.15)]]);
    for (const ball of balls) {
        assert.equal(ball.stockHandoff, undefined, 'the floor must not depend on a flat-to-ball stock transfer');
        assert.ok(ball.cuttingMinutes >= expected.get(ball.toolId) - 1e-8,
            'generic planar finishing rates must not undercut the ball feed/stepover estimate');
        assert.ok(ball.finishingPolicy, 'market finishing assumptions must remain inspectable');
        assert.ok(Math.abs(ball.finishingPolicy.cuttingMinutes - expected.get(ball.toolId)) < 1e-8);
    }
});
