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
        vm.runInContext(source, context, { filename: file });
    }
    return context;
}

function steppedPocketPlan(recessFromReverse = false, filletFeatures) {
    const c = runtime();
    const access = (reverse) => Object.fromEntries(Array.from(c.CncToolLibrary.analysisProfiles(), tool => {
        // A broad face and a narrow recess belong to one smooth cluster. The larger
        // cutter can machine the broad sample, but only <= 4 mm can enter the recess.
        const ids = reverse ? (recessFromReverse && tool.diameterMm <= 4 ? [2, 3] : [3])
            : !recessFromReverse && tool.diameterMm <= 4 ? [1, 2] : [1];
        return [tool.id, { reachableSampleIds: ids, tipSampleIds: ids, fluteSampleIds: [] }];
    }));
    return c.CncPlanningDiagnostics.plan({ material: '6061', requirements: { quantity: 1 },
        stock: { stockSizeMm: { x: 22, y: 55, z: 105 }, confidence: 'High' },
        geometry: { bodyCount: 1, boxFillRatio: 0.6, orientedSizeMm: { x: 20, y: 50, z: 100 },
            partVolumeMm3: 30000, partSurfaceAreaMm2: 200,
            orientationCandidates: [
                { id: 'front', toolDirection: { x: 1, y: 0, z: 0 }, projectedFaceCoverage: 0.5 },
                { id: 'back', toolDirection: { x: -1, y: 0, z: 0 }, projectedFaceCoverage: 0.5 }],
            surfaceClusters: [
                { id: 'front', type: 'planar', normal: { x: 1, y: 0, z: 0 }, areaMm2: 100,
                    operationCodes: ['roughing', 'finishing'], filletFeatures },
                { id: 'back', type: 'planar', normal: { x: -1, y: 0, z: 0 }, areaMm2: 100,
                    operationCodes: ['roughing', 'finishing'] }],
            accessibilityField: { surfaceSamples: [
                { id: 1, clusterId: 'front', sourceTriangleIndex: 10, areaMm2: 80 },
                { id: 2, clusterId: 'front', sourceTriangleIndex: 20, areaMm2: 20 },
                { id: 3, clusterId: 'back', sourceTriangleIndex: 30, areaMm2: 100 }],
                toolAccess: { front: access(false), back: access(true) } } } });
}

test('partial access keeps bulk milling and assigns narrow recess cleanup to a fitting cutter', () => {
    const plan = steppedPocketPlan();
    const front = plan.setups.find(setup => setup.id === 'front');
    for (const code of ['roughing', 'finishing']) {
        const operations = plan.operations.filter(operation => operation.code === code && operation.setupNumber === front.number);
        assert.equal(operations.length, 2, code + ' must retain separate bulk and recess work');
        assert.deepEqual(Array.from(operations, operation => operation.toolDiameterMm), [16, 4]);
        assert.deepEqual(Array.from(operations, operation => operation.featureAreaMm2), [80, 20]);
        assert.deepEqual(Array.from(operations, operation => Array.from(operation.featureSampleIds)), [[1], [2]]);
        assert.ok(operations.every(operation => operation.reachable));
    }
});

test('tool cleanup samples and cutting time are counted exactly once per operation phase', () => {
    const plan = steppedPocketPlan();
    for (const code of ['roughing', 'finishing']) {
        const operations = plan.operations.filter(operation => operation.code === code);
        assert.equal(operations.reduce((sum, operation) => sum + operation.featureAreaMm2, 0), 200);
        const samples = Array.from(operations).flatMap(operation => Array.from(operation.featureSampleIds || []));
        assert.deepEqual(samples.slice().sort(), [1, 2, 3]);
        assert.equal(new Set(samples).size, samples.length);
        const tools = runtime().CncToolLibrary;
        assert.ok(Math.abs(operations.reduce((sum, operation) => sum + operation.cuttingMinutes / tools.get(operation.toolId).timeMultiplier, 0)
            - plan[code === 'roughing' ? 'roughingMinutes' : 'finishingMinutes']) < 1e-9);
    }
    assert.equal(new Set(plan.operations.map(operation => operation.id)).size, plan.operations.length);
});

test('a partially visible cluster can allocate its remaining samples to a different setup', () => {
    const plan = steppedPocketPlan(true);
    const back = plan.setups.find(setup => setup.id === 'back');
    for (const code of ['roughing', 'finishing']) {
        const cleanup = plan.operations.find(operation => operation.code === code && operation.toolDiameterMm === 4);
        assert.ok(cleanup, code + ' must not lose the recess after allocating its parent cluster');
        assert.equal(cleanup.setupNumber, back.number);
        assert.deepEqual(Array.from(cleanup.featureSampleIds), [2]);
        assert.equal(cleanup.featureAreaMm2, 20);
    }
});

function freeformPlan(prismaticContourAxis) {
    const c = runtime();
    return c.CncPlanningDiagnostics.plan({ material: '6061', requirements: { quantity: 1 },
        stock: { stockSizeMm: { x: 25, y: 55, z: 65 } },
        geometry: { bodyCount: 1, orientedSizeMm: { x: 20, y: 50, z: 60 },
            partVolumeMm3: 10000, partSurfaceAreaMm2: 2000,
            orientationCandidates: [{ id: 'front', toolDirection: { x: 1, y: 0, z: 0 }, projectedFaceCoverage: 0.5 }],
            surfaceClusters: [{ id: 'curved', type: 'freeform', areaMm2: 2000,
                filletFeatures: [], prismaticContourAxis, accessibleDirectionIds: ['front'] }] } });
}

function detailCutterPlan(shared = true, extra = '') {
    const c = runtime(), z = { x: 0, y: 0, z: 1 };
    const samples = [
        { id: 1, clusterId: 'bulk', areaMm2: 800 },
        { id: 2, clusterId: shared ? 'slot' : 'pocket', areaMm2: 110 },
        { id: 3, clusterId: shared ? 'slot' : 'groove', areaMm2: 89 },
        { id: 4, clusterId: shared ? 'slot' : 'corner', areaMm2: 1 }
    ];
    if (extra) { samples.push({ id: 5, clusterId: 'other-face', areaMm2: 400 }); }
    const access = Object.fromEntries(Array.from(c.CncToolLibrary.analysisProfiles(), t => {
        const ids = t.diameterMm <= 4 ? [1, 2, 3, 4] : t.diameterMm <= 6 ? [1, 2, 3]
            : t.diameterMm <= 10 ? [1, 2] : [1];
        if (extra && t.diameterMm <= 10) { ids.push(5); }
        return [t.id, { reachableSampleIds: ids, tipSampleIds: ids, fluteSampleIds: [] }];
    }));
    return c.CncPlanningDiagnostics.plan({ material: '6061', requirements: { quantity: 1 },
        stock: { stockSizeMm: { x: 100, y: 100, z: 100 } },
        geometry: { bodyCount: 1, orientedSizeMm: { x: 90, y: 90, z: 90 },
            partVolumeMm3: 500000, partSurfaceAreaMm2: 1000,
            orientationCandidates: [{ id: 'front', toolDirection: z, projectedFaceCoverage: 1 }],
            surfaceClusters: [...new Set(samples.map(s => s.clusterId))].map(id => ({ id, type: 'planar', normal: z,
                adjacentClusterIds: extra === 'connected' ? (id === 'slot' ? ['other-face'] : id === 'other-face' ? ['slot'] : [])
                    : extra === 'via-bulk' ? (id === 'slot' ? ['bulk'] : id === 'bulk' ? ['slot', 'other-face'] : ['bulk']) : [],
                areaMm2: samples.filter(s => s.clusterId === id).reduce((sum, s) => sum + s.areaMm2, 0),
                operationCodes: ['roughing', 'finishing'] })),
            accessibilityField: { surfaceSamples: samples, toolAccess: { front: access } } } });
}

test('a complete-slot cutter absorbs intermediate sizes even when its initial sample area is smaller', () => {
    const plan = detailCutterPlan();
    for (const code of ['roughing', 'finishing']) {
        const ops = plan.operations.filter(o => o.code === code);
        assert.deepEqual(Array.from(ops, o => o.toolDiameterMm), [16, 4]);
        assert.deepEqual(Array.from(ops[1].featureSampleIds).sort(), [2, 3, 4]);
        assert.equal(ops[1].featureAreaMm2, 200);
    }
});

test('a small cutter for an unrelated corner does not absorb larger independent pockets', () => {
    const plan = detailCutterPlan(false);
    for (const code of ['roughing', 'finishing']) {
        const ops = plan.operations.filter(o => o.code === code);
        assert.deepEqual(Array.from(ops, o => o.toolDiameterMm), [16, 10, 6, 4]);
        assert.deepEqual(Array.from(ops.find(o => o.toolDiameterMm === 4).featureSampleIds), [4]);
    }
});

for (const extra of ['disconnected', 'via-bulk']) {
    test('a mixed slot/pocket allocation retains its larger cutter when ' + extra, () => {
        const plan = detailCutterPlan(true, extra);
        for (const code of ['roughing', 'finishing']) {
            const ops = plan.operations.filter(o => o.code === code);
            assert.equal(ops.find(o => o.featureSampleIds.includes(5)).toolDiameterMm, 10);
            assert.equal(ops.find(o => o.featureSampleIds.includes(4)).toolDiameterMm, 4);
        }
    });
}

test('adjacent unfinished slot walls and floor consolidate across their CAD face boundaries', () => {
    const plan = detailCutterPlan(true, 'connected');
    for (const code of ['roughing', 'finishing']) {
        const ops = plan.operations.filter(o => o.code === code);
        assert.deepEqual(Array.from(ops, o => o.toolDiameterMm), [16, 4]);
        assert.deepEqual(Array.from(ops[1].featureSampleIds).sort(), [2, 3, 4, 5]);
    }
});

test('verified facing owns its surface once and removes the separate finishing charge', () => {
    const c = runtime(), z = { x: 0, y: 0, z: 1 };
    function plan(facing, blocked = false, normal = z) {
        const ids = [1];
        const access = Object.fromEntries(Array.from(c.CncToolLibrary.analysisProfiles(), t => {
            const reachable = blocked && t.family === 'face_mill' ? [] : ids;
            return [t.id, { reachableSampleIds: reachable, tipSampleIds: reachable, fluteSampleIds: [] }];
        }));
        return c.CncPlanningDiagnostics.plan({ material: '6061', requirements: { quantity: 1 },
            stock: { stockSizeMm: { x: 100, y: 100, z: 12 } },
            geometry: { bodyCount: 1, orientedSizeMm: { x: 100, y: 100, z: 10 },
                partVolumeMm3: 100000, partSurfaceAreaMm2: 10000,
                orientationCandidates: [{ id: 'front', toolDirection: z, projectedFaceCoverage: 1 }],
                surfaceClusters: [{ id: 'face', type: 'planar', normal, areaMm2: 10000,
                    operationCodes: facing ? ['facing', 'roughing', 'finishing'] : ['roughing', 'finishing'] }],
                accessibilityField: { surfaceSamples: [{ id: 1, clusterId: 'face', areaMm2: 10000 }],
                    toolAccess: { front: access } } } });
    }
    const faced = plan(true), milled = plan(false);
    assert.deepEqual(Array.from(faced.operations, o => o.code), ['facing', 'deburring']);
    assert.ok(faced.operations[0].cuttingMinutes > 0, 'facing still pays for stock removal');
    assert.equal(faced.finishingMinutes, 0, 'a finished face does not pay for end-mill finishing again');
    assert.ok(faced.totalMinutesPerPart < milled.totalMinutesPerPart);
    const inaccessibleFace = plan(true, true);
    assert.ok(inaccessibleFace.operations.some(o => o.code === 'finishing' && o.reachable),
        'a blocked face mill cannot remove required end-mill finishing');
    const slope = plan(true, false, { x: 0.3, y: 0, z: Math.sqrt(0.91) });
    assert.ok(slope.operations.some(o => o.code === 'finishing'),
        'facing cannot finish a plane tilted relative to the spindle');
});

test('an empty fillet list does not authorize flat finishing on a sculpted freeform surface', () => {
    const plan = freeformPlan();
    assert.ok(plan.reachMatrix.some(record => record.operationCode === 'freeform_finishing'));
    assert.equal(plan.reachMatrix.some(record => record.operationCode === 'finishing' && record.reachable), false);
});

test('verified prismatic contour geometry retains reachable flat finishing', () => {
    const plan = freeformPlan({ x: 1, y: 0, z: 0 });
    assert.ok(plan.operations.some(operation => operation.code === 'finishing' && operation.reachable
        && operation.clusterIds.includes('curved')));
});

test('local ball finishing replaces only overlapping narrow-tool work, not broad flat faces', () => {
    const plan = steppedPocketPlan(false, [{ radiusMm: 2, axis: { x: 0, y: 0, z: 1 },
        areaMm2: 20, triangleIndexes: [20], accessibleDirectionIds: ['front'] }]);
    const front = plan.setups.find(setup => setup.id === 'front');
    const finishing = plan.operations.filter(operation => operation.setupNumber === front.number
        && ['finishing', 'freeform_finishing'].includes(operation.code));
    const tools = runtime().CncToolLibrary;
    const weightedArea = operation => operation.cuttingMinutes / tools.get(operation.toolId).timeMultiplier
        / plan.finishingMinutes * 200;
    assert.equal(weightedArea(finishing.find(operation => operation.toolId === 'flat-16-2d')), 80);
    assert.equal(weightedArea(finishing.find(operation => operation.toolId === 'flat-4-2d')), 0);
    assert.equal(weightedArea(finishing.find(operation => operation.toolId === 'ball-4-2d')), 20);
});

test('consolidated ball passes preserve the replacement area of each local strip', () => {
    const plan = steppedPocketPlan(false, [
        { radiusMm: 2, axis: { x: 0, y: 0, z: 1 }, areaMm2: 5,
            triangleIndexes: [20], accessibleDirectionIds: ['front'] },
        { radiusMm: 2, axis: { x: 0, y: 0, z: 1 }, areaMm2: 15,
            triangleIndexes: [10], accessibleDirectionIds: ['front'] }]);
    const front = plan.setups.find(setup => setup.id === 'front');
    const finishing = plan.operations.filter(operation => operation.setupNumber === front.number
        && ['finishing', 'freeform_finishing'].includes(operation.code));
    const tools = runtime().CncToolLibrary;
    const weightedArea = operation => operation.cuttingMinutes / tools.get(operation.toolId).timeMultiplier
        / plan.finishingMinutes * 200;
    assert.equal(finishing.filter(operation => operation.code === 'freeform_finishing').length, 1);
    assert.equal(weightedArea(finishing.find(operation => operation.toolId === 'flat-16-2d')), 65);
    assert.equal(weightedArea(finishing.find(operation => operation.toolId === 'flat-4-2d')), 15);
    assert.equal(weightedArea(finishing.find(operation => operation.toolId === 'ball-4-2d')), 20);
});

test('a vertical wall corner does not become a bottom fillet because a side-slot setup exists', () => {
    const c = runtime();
    const y = { x: 0, y: 1, z: 0 }, reverse = { x: 0, y: -1, z: 0 }, x = { x: 1, y: 0, z: 0 };
    const plan = c.CncPlanningDiagnostics.plan({ material: '6061', requirements: { quantity: 1 },
        stock: { stockSizeMm: { x: 70, y: 30, z: 70 }, confidence: 'High' },
        geometry: { bodyCount: 1, orientedSizeMm: { x: 63, y: 20, z: 63 },
            partVolumeMm3: 30000, partSurfaceAreaMm2: 9050,
            orientationCandidates: [
                { id: 'front', toolDirection: y, projectedFaceCoverage: 0.45 },
                { id: 'back', toolDirection: reverse, projectedFaceCoverage: 0.45 },
                { id: 'side-slot', toolDirection: x, projectedFaceCoverage: 0.1 }],
            surfaceClusters: [
                { id: 'front', type: 'planar', normal: y, areaMm2: 4000, accessibleDirectionIds: ['front'] },
                { id: 'back', type: 'planar', normal: reverse, areaMm2: 4000, accessibleDirectionIds: ['back'] },
                { id: 'side-slot', type: 'planar', normal: x, areaMm2: 1000, accessibleDirectionIds: ['side-slot'] },
                { id: 'wall-corner', type: 'unresolved', areaMm2: 50, accessibleDirectionIds: ['front'],
                    filletFeatures: [{ radiusMm: 0.6, axis: y, areaMm2: 5, accessibleDirectionIds: [] }] }] } });
    assert.ok(plan.setups.some(setup => setup.id === 'side-slot'));
    assert.equal(plan.operations.filter(operation => operation.code === 'freeform_finishing').length, 0);
});

function directionalResiduePlan(sharedPatch) {
    const c = runtime();
    const directions = [{ x: 1, y: 0, z: 0 }, { x: -1, y: 0, z: 0 },
        { x: 0, y: 1, z: 0 }, { x: 0, y: -1, z: 0 }, { x: 0, y: 0, z: 1 }, { x: 0, y: 0, z: -1 }];
    const names = ['positive-x', 'negative-x', 'positive-y', 'negative-y', 'positive-z', 'negative-z'];
    const access = ids => Object.fromEntries(Array.from(c.CncToolLibrary.analysisProfiles(), tool => [tool.id,
        { reachableSampleIds: ids, tipSampleIds: ids, fluteSampleIds: [] }]));
    return c.CncPlanningDiagnostics.plan({ material: '6061', requirements: { quantity: 1 },
        stock: { stockSizeMm: { x: 22, y: 55, z: 65 } },
        geometry: { bodyCount: 2, orientedSizeMm: { x: 20, y: 50, z: 60 },
            partVolumeMm3: 10000, partSurfaceAreaMm2: 2000,
            orientationCandidates: names.map((id, index) => ({ id, toolDirection: directions[index],
                projectedFaceCoverage: index < 2 ? 0.5 : 0.01 })),
            surfaceClusters: [
                { id: 'front', type: 'planar', normal: directions[0], areaMm2: 1000, operationCodes: ['finishing'] },
                { id: 'back', type: 'planar', normal: directions[1], areaMm2: 1000, operationCodes: ['finishing'] }],
            accessibilityField: { surfaceSamples: [
                { id: 1, clusterId: 'front', areaMm2: sharedPatch ? 986 : 944 },
                { id: 2, clusterId: 'back', areaMm2: 1000 },
                ...(sharedPatch ? [3] : [3, 4, 5, 6]).map(id => ({ id, clusterId: 'front', areaMm2: 14 }))],
                toolAccess: Object.fromEntries(names.map((id, index) => [id, access([sharedPatch && index >= 2 ? 3 : index + 1])])) } } });
}

test('discarded setup residue stays within one cumulative per-cluster and global budget', () => {
    const plan = directionalResiduePlan(false);
    // Four independent 1.4% front patches cannot all be discarded as individually
    // small: that would silently discard 5.6% of the front and 2.8% globally.
    assert.equal(plan.setups.length, 5);
    assert.equal(plan.unmachinableFieldAreaRatio, 0);
    assert.equal(plan.hasSignificantUnmachinableSurface, false);
    const samples = plan.operations.filter(operation => operation.code === 'finishing')
        .flatMap(operation => Array.from(operation.featureSampleIds || []));
    assert.equal([3, 4, 5, 6].filter(id => samples.includes(id)).length, 3);
});

test('alternative directions reaching the same residue patch spend its budget only once', () => {
    const plan = directionalResiduePlan(true);
    assert.equal(plan.setups.length, 2);
    assert.equal(plan.unmachinableFieldAreaRatio, 0);
    assert.equal(plan.hasSignificantUnmachinableSurface, false);
});
