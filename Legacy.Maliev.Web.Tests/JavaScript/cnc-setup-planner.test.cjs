const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const moduleRoot = path.resolve(__dirname,
    '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation');

function runtime(includePlanning = false) {
    const context = vm.createContext({ console, TextEncoder });
    context.self = context;
    context.window = context;
    const names = ['cnc-plan-contracts', 'cnc-setup-planner'];
    if (includePlanning) {
        names.splice(1, 0, 'cnc-quotation-config', 'cnc-material-catalog', 'cnc-tool-library',
            'cnc-reach', 'cnc-fixture-clearance', 'cnc-machine-capability');
        names.push('cnc-planning');
    }
    for (const name of names) {
        const file = name === 'cnc-planning'
            ? path.resolve(__dirname, 'fixtures/cnc-legacy-planning.test-helper.js')
            : path.join(moduleRoot, name + '.js');
        if (fs.existsSync(file)) {
            let source = fs.readFileSync(file, 'utf8');
            if (name === 'cnc-planning') source = source.replace('/* CNC_TEST_INSTRUMENTATION_POINT */',
                'window.CncPlanningDiagnostics = Object.freeze({ plan: plan, planFixture: planFixture });');
            vm.runInContext(source, context, { filename: file });
        }
    }
    return context;
}

const axes = Object.freeze({
    top: Object.freeze({ x: 0, y: 0, z: 1 }),
    flip: Object.freeze({ x: 0, y: 0, z: -1 }),
    side: Object.freeze({ x: 1, y: 0, z: 0 })
});

function feature(id, axis, additions = {}) {
    return {
        id, bodyId: 'body-1', kind: 'hole', primaryFaceIds: ['face-' + id],
        dimensions: { diameterMm: 5, depthMm: 1 },
        secondaryFeatureIds: [], accessAxes: [axis], evidenceRefs: ['face-' + id],
        ...additions
    };
}

function operation(id, featureId, axis, predecessors = [], additions = {}) {
    return {
        id, featureId, kind: 'drilling', phase: 'drill', toolClass: 'hss_drill',
        toolConstraints: { toolClass: 'hss_drill', minimumCutLengthMm: 1, minimumReachMm: 1 }, accessAxis: axis,
        predecessors, provenance: {
            sourceContract: 'ManufacturingFeatureGraph.v1', featureId
        },
        ...additions
    };
}

function fixture(id, orientation, datumFeatureId, datumFaceId, operationIds,
    inputStockState, outputStockState, additions = {}) {
    const fixtureId = 'fixture-' + id;
    return {
        id, orientation, fixtureId, fixtureState: 'vise',
        datumFeatureIds: [datumFeatureId], datumFaceIds: [datumFaceId],
        clampFaceIds: [datumFaceId], supportedOperationIds: operationIds,
        inputStockState, outputStockState,
        handlingMinutes: 3,
        catalogCapability: { catalogVersion: 'test-fixtures-v2', fixtureId,
            maximumOpeningMm: 100, minimumGripMm: 3 },
        datumContact: { topologyFaceId: datumFaceId, surfaceKind: 'plane' },
        clampContact: { topologyFaceId: datumFaceId, surfaceKind: 'plane' },
        fixtureCapability: { catalogVersion: 'test-fixtures-v2', fixtureId,
            accessAxes: [orientation], obstacles: [], maximumToolReachMm: 100 },
        ...additions
    };
}

function stockStates(candidateFixtures) {
    const stateIds = Array.from(new Set(candidateFixtures.flatMap(candidate =>
        [candidate.inputStockState, candidate.outputStockState])));
    return stateIds.map(id => ({ id,
        compatibleFixtureIds: candidateFixtures.map(candidate => candidate.fixtureId),
        availableDatumFaceIds: ['face-top-datum', 'face-flip-face'],
        availableClampFaceIds: ['face-top-datum', 'face-flip-face'] }));
}

function input(features, operations, additions = {}) {
    const faceOwners = {};
    const machinableFaceIds = [];
    for (const item of features) {
        for (const faceId of item.primaryFaceIds) {
            faceOwners[faceId] = item.id;
            machinableFaceIds.push(faceId);
        }
    }
    const defaultFixtures = [
        fixture('top', axes.top, 'top-datum', 'face-top-datum',
            operations.filter(item => item.accessAxis.z === 1).map(item => item.id),
            'stock-initial', 'stock-after-top'),
        fixture('flip', axes.flip, 'flip-face', 'face-flip-face',
            operations.filter(item => item.accessAxis.z === -1).map(item => item.id),
            'stock-after-top', 'stock-final')
    ];
    return {
        topology: { contract: 'CncCadTopology.v1', revision: 'topology-1',
            sourceKind: 'brep', automaticPlanningEligible: true, bodies: [{ id: 'body-1' }],
            faces: features.flatMap(item => item.primaryFaceIds.map(id => ({ id, bodyId: 'body-1',
                surface: { kind: 'plane', normal: item.accessAxes[0] }, validationVolume: {
                    minimum: { x: 0, y: 0, z: 0 }, maximum: { x: 10, y: 10, z: 1 } } }))),
            edges: [] },
        featureGraph: { contract: 'ManufacturingFeatureGraph.v1',
            topologyRevision: 'topology-1', features, faceOwners, machinableFaceIds, unresolved: [] },
        operationGraph: { contract: 'MachiningOperationGraph.v1',
            topologyRevision: 'topology-1', toolLibraryVersion: 'test-tools-v1',
            operations, unresolved: [] },
        stock: { id: 'stock-1', stateId: 'stock-initial', states: stockStates(defaultFixtures) },
        fixtureCatalog: { contract: 'CncFixtureCatalog.v2', version: 'test-fixtures-v2',
            candidates: defaultFixtures },
        ...additions
    };
}

function axialInput() {
    const features = [
        feature('top-datum', axes.top, { kind: 'datum' }),
        feature('top-hole', axes.top),
        feature('external-m14', axes.top, { kind: 'external_thread',
            threadDesignation: 'M14 x 1', nominalDiameterMm: 14, pitchMm: 1,
            majorDiameterPreparationRequired: false,
            leadInChamferRequired: false }),
        feature('flip-face', axes.flip, { kind: 'datum' })
    ];
    const operations = [
        operation('top-face', 'top-datum', axes.top, [], { kind: 'facing', phase: 'rough',
            toolClass: 'face_mill', toolConstraints: { toolClass: 'face_mill' } }),
        operation('top-spot', 'top-hole', axes.top, [], { kind: 'spot_drilling', phase: 'spot',
            toolClass: 'spot_drill', toolConstraints: { toolClass: 'spot_drill' } }),
        operation('top-drill', 'top-hole', axes.top, ['top-spot']),
        operation('external-thread', 'external-m14', axes.top, [], { kind: 'thread_milling',
            phase: 'thread', toolClass: 'thread_mill', toolConstraints: { toolClass: 'thread_mill',
                threadDesignation: 'M14 x 1', nominalDiameterMm: 14, pitchMm: 1,
                majorDiameterPreparationRequired: false, leadInChamferRequired: false } }),
        operation('flip-face', 'flip-face', axes.flip, [], { kind: 'facing', phase: 'rough',
            toolClass: 'face_mill', toolConstraints: { toolClass: 'face_mill' } })
    ];
    return input(features, operations);
}

test('axial M14 part resolves to top and flip only with exact coverage', () => {
    const c = runtime();
    assert.equal(typeof c.CncSetupPlanner?.plan, 'function');
    const source = axialInput();
    const result = c.CncSetupPlanner.plan(source);
    assert.equal(result.contract, 'SetupPlan.v1');
    assert.equal(result.geometryRevision, 'topology-1');
    assert.equal(result.setups.length, 2);
    assert.equal(result.setups.every(setup => setup.operationIds.length > 0), true);
    assert.deepEqual(Array.from(result.setups, setup => setup.orientation.id),
        ['axis:+0.000000,+0.000000,+1.000000', 'axis:+0.000000,+0.000000,-1.000000']);
    assert.equal(new Set(result.setups.flatMap(setup => Array.from(setup.operationIds))).size,
        source.operationGraph.operations.length);
    assert.equal(Object.keys(result.operationAssignments).length,
        source.operationGraph.operations.length);
});

test('a residual triangle sample cannot create a setup', () => {
    const c = runtime();
    const source = axialInput();
    source.geometryDiagnostics = { surfaceSamples: [{ id: 99, direction: axes.side }] };
    assert.equal(c.CncSetupPlanner.plan(source).setups.length, 2);
});

test('cross-axis explicit hole requires a third setup', () => {
    const c = runtime();
    const source = axialInput();
    source.featureGraph.features.push(feature('side-hole', axes.side));
    source.featureGraph.faceOwners['face-side-hole'] = 'side-hole';
    source.featureGraph.machinableFaceIds.push('face-side-hole');
    source.topology.faces.push({ id: 'face-side-hole', bodyId: 'body-1' });
    source.operationGraph.operations.push(
        operation('side-hole-spot', 'side-hole', axes.side, [], { kind: 'spot_drilling', phase: 'spot',
            toolClass: 'spot_drill', toolConstraints: { toolClass: 'spot_drill' } }),
        operation('side-hole-drill', 'side-hole', axes.side, ['side-hole-spot']));
    source.fixtureCatalog.candidates[1].outputStockState = 'stock-after-flip';
    source.fixtureCatalog.candidates.push(
        fixture('side', axes.side, 'top-datum', 'face-top-datum',
            ['side-hole-spot', 'side-hole-drill'], 'stock-after-flip', 'stock-final'));
    source.stock.states = stockStates(source.fixtureCatalog.candidates);
    const result = c.CncSetupPlanner.plan(source);
    assert.equal(result.setups.length, 3);
    assert.ok(Array.from(result.setups[2].operationIds).every(id => id.startsWith('side-hole')));
});

test('predecessors are ordered before dependants and every operation is assigned once', () => {
    const c = runtime();
    const result = c.CncSetupPlanner.plan(axialInput());
    const flattened = result.setups.flatMap(setup => Array.from(setup.operationIds));
    assert.ok(flattened.indexOf('top-spot') < flattened.indexOf('top-drill'));
    assert.equal(flattened.length, new Set(flattened).size);
});

test('fixture datum and clamp infeasibility fails closed without speculative setup', () => {
    const c = runtime();
    const source = axialInput();
    source.fixtureCatalog.candidates[0].clampContact = { topologyFaceId: 'missing-face', surfaceKind: 'plane' };
    assert.throws(() => c.CncSetupPlanner.plan(source), error =>
        error && error.code === 'invalid_fixture_evidence');
});

test('missing or invalid access evidence fails closed', () => {
    const c = runtime();
    const source = axialInput();
    source.operationGraph.operations[0].accessAxis = null;
    assert.throws(() => c.CncSetupPlanner.plan(source), error =>
        error && error.code === 'operation_access_axis_required');
});

test('missing semantic topology or fixture evidence fails closed', () => {
    const c = runtime();
    for (const mutate of [
        source => { delete source.topology; },
        source => { source.topology.contract = 'MeshTopology.v1'; },
        source => { source.topology.sourceKind = 'mesh'; },
        source => { source.topology.automaticPlanningEligible = false; },
        source => { source.topology.revision = 'other'; },
        source => { source.featureGraph.topologyRevision = 'other'; },
        source => { delete source.fixtureCatalog; },
        source => { source.fixtureCatalog.candidates = []; }
    ]) {
        const source = axialInput();
        mutate(source);
        assert.throws(() => c.CncSetupPlanner.plan(source));
    }
});

test('topology must contain unique bodies and faces covering every feature primary face', () => {
    const c = runtime();
    for (const mutate of [
        source => { source.topology.bodies = []; },
        source => { source.topology.faces = []; },
        source => { source.topology.bodies.push({ id: 'body-1' }); },
        source => { source.topology.bodies[0].id = ''; },
        source => { source.topology.faces.push({ ...source.topology.faces[0] }); },
        source => { source.topology.faces[0].id = ''; },
        source => { source.topology.faces = source.topology.faces.filter(face => face.id !== 'face-top-hole'); },
        source => { source.featureGraph.features[0].bodyId = 'body-other'; }
    ]) {
        const source = axialInput();
        mutate(source);
        assert.throws(() => c.CncSetupPlanner.plan(source), error =>
            error && error.code === 'semantic_topology_required');
    }
});

test('datum evidence must name datum-owned topology faces compatible with stock', () => {
    const c = runtime();
    for (const mutate of [
        source => { source.fixtureCatalog.candidates[0].datumFeatureIds = ['external-m14']; },
        source => { source.fixtureCatalog.candidates[0].datumFaceIds = ['face-external-m14']; },
        source => { source.stock.states[0].availableDatumFaceIds = []; },
        source => { source.fixtureCatalog.candidates[0].clampFaceIds = ['missing-face']; }
    ]) {
        const source = axialInput();
        mutate(source);
        assert.throws(() => c.CncSetupPlanner.plan(source), error =>
            error && error.code === 'invalid_fixture_evidence');
    }
});

test('stock state transitions are explicit and sequence compatible', () => {
    const c = runtime();
    const source = axialInput();
    source.fixtureCatalog.candidates[1].inputStockState = 'stock-unreachable';
    source.stock.states.push({ id: 'stock-unreachable', compatibleFixtureIds: ['fixture-flip'],
        availableDatumFaceIds: ['face-flip-face'], availableClampFaceIds: ['face-flip-face'] });
    assert.throws(() => c.CncSetupPlanner.plan(source), error =>
        error && error.code === 'invalid_stock_transition');
});

test('search rejects a cheaper assignment whose output cannot feed the next setup', () => {
    const c = runtime();
    const source = axialInput();
    const topOps = ['top-face', 'top-spot', 'top-drill', 'external-thread'];
    source.fixtureCatalog.candidates = [
        fixture('top-dead-end', axes.top, 'top-datum', 'face-top-datum', topOps,
            'stock-initial', 'stock-dead-end', { handlingMinutes: 1 }),
        fixture('top-valid', axes.top, 'top-datum', 'face-top-datum', topOps,
            'stock-initial', 'stock-after-top', { handlingMinutes: 5 }),
        fixture('flip', axes.flip, 'flip-face', 'face-flip-face', ['flip-face'],
            'stock-after-top', 'stock-final', { handlingMinutes: 1 })
    ];
    source.stock.states = stockStates(source.fixtureCatalog.candidates);
    const result = c.CncSetupPlanner.plan(source);
    assert.equal(result.setups[0].handlingMinutes, 5);
    assert.deepEqual(Array.from(result.setups, setup => [setup.inputStockState, setup.outputStockState]), [
        ['stock-initial', 'stock-after-top'], ['stock-after-top', 'stock-final']
    ]);
});

test('ambiguous fixture identities are rejected independent of input order', () => {
    const c = runtime();
    const source = axialInput();
    source.fixtureCatalog.candidates.push({ ...source.fixtureCatalog.candidates[0] });
    assert.throws(() => c.CncSetupPlanner.plan(source), error =>
        error && error.code === 'ambiguous_fixture_candidate');
});

test('canonical setup plan is invariant to feature operation and candidate ordering', () => {
    const c = runtime();
    const left = axialInput();
    const right = axialInput();
    right.featureGraph.features.reverse();
    right.operationGraph.operations.reverse();
    right.fixtureCatalog.candidates = left.fixtureCatalog.candidates.slice().reverse();
    assert.equal(JSON.stringify(c.CncSetupPlanner.plan(left)),
        JSON.stringify(c.CncSetupPlanner.plan(right)));
});

test('candidate evidence sets are canonical regardless of member order', () => {
    const c = runtime();
    const left = axialInput();
    Object.assign(left.fixtureCatalog.candidates[0], {
        datumFeatureIds: ['top-datum', 'flip-face'],
        datumFaceIds: ['face-top-datum', 'face-flip-face'],
        clampFaceIds: ['face-top-datum', 'face-flip-face'],
        supportedToolClasses: ['hss_drill', 'face_mill', 'spot_drill', 'thread_mill'],
        supportedOperationKinds: ['thread_milling', 'drilling', 'spot_drilling', 'facing'],
        allowedAccessAxes: [axes.top, axes.flip]
    });
    const expected = JSON.stringify(c.CncSetupPlanner.plan(left));
    for (const field of ['supportedOperationIds', 'supportedToolClasses',
        'supportedOperationKinds', 'allowedAccessAxes']) {
        const right = structuredClone(left);
        right.fixtureCatalog.candidates[0][field].reverse();
        assert.equal(JSON.stringify(c.CncSetupPlanner.plan(right)), expected, field);
    }
});

test('candidate handling time is required finite nonnegative evidence', () => {
    const c = runtime();
    for (const mutate of [
        candidate => { delete candidate.handlingMinutes; },
        candidate => { candidate.handlingMinutes = Number.NaN; },
        candidate => { candidate.handlingMinutes = -1; },
        candidate => { candidate.handlingMinutes = '3'; }
    ]) {
        const source = axialInput();
        mutate(source.fixtureCatalog.candidates[0]);
        assert.throws(() => c.CncSetupPlanner.plan(source), error =>
            error && error.code === 'invalid_fixture_evidence');
    }
});

test('search minimizes setup count before handling cost and canonical identity', () => {
    const c = runtime();
    const source = axialInput();
    const topOps = ['top-face', 'top-spot', 'top-drill', 'external-thread'];
    source.fixtureCatalog.candidates = [
        fixture('top-partial-a', axes.top, 'top-datum', 'face-top-datum',
            ['top-face', 'top-spot'], 'stock-initial', 'stock-after-top', { handlingMinutes: 1 }),
        fixture('top-partial-b', axes.top, 'top-datum', 'face-top-datum',
            ['top-drill', 'external-thread'], 'stock-initial', 'stock-after-top', { handlingMinutes: 1 }),
        fixture('top-expensive', axes.top, 'top-datum', 'face-top-datum', topOps,
            'stock-initial', 'stock-after-top', { handlingMinutes: 9 }),
        fixture('top-cheap', axes.top, 'top-datum', 'face-top-datum', topOps,
            'stock-initial', 'stock-after-top', { handlingMinutes: 4 }),
        fixture('flip', axes.flip, 'flip-face', 'face-flip-face', ['flip-face'],
            'stock-after-top', 'stock-final', { handlingMinutes: 2 })
    ];
    source.stock.states = stockStates(source.fixtureCatalog.candidates);
    const result = c.CncSetupPlanner.plan(source);
    assert.equal(result.setups.length, 2);
    assert.equal(result.setups[0].handlingMinutes, 4);
    assert.deepEqual(Array.from(result.setups[0].operationIds),
        ['external-thread', 'top-face', 'top-spot', 'top-drill']);
});

test('production CncPlanning delegates exclusively to CncSetupPlanner', () => {
    const c = runtime(true);
    const source = axialInput();
    source.production = true;
    const originalPlanner = c.CncSetupPlanner;
    let plannerCalls = 0;
    c.CncSetupPlanner = Object.freeze({ plan(input) {
        plannerCalls += 1;
        return originalPlanner.plan(input);
    } });
    const result = c.CncPlanningDiagnostics.plan(source);
    assert.equal(plannerCalls, 1);
    assert.equal(result.setupCount, 2);
    assert.equal(result.setupPlan.contract, 'SetupPlan.v1');
    assert.equal(result.setupPlanningPending, false);
    const missingGraphs = axialInput();
    missingGraphs.production = true;
    delete missingGraphs.operationGraph;
    assert.throws(() => c.CncPlanningDiagnostics.plan(missingGraphs), error =>
        error && error.code === 'manufacturing_feature_graph_required');
    const unresolved = axialInput();
    unresolved.production = true;
    unresolved.featureGraph.features.push(feature('unresolved-face', axes.top,
        { kind: 'unresolved', unresolvedReason: 'unclassified_brep_face' }));
    unresolved.featureGraph.faceOwners['face-unresolved-face'] = 'unresolved-face';
    unresolved.featureGraph.machinableFaceIds.push('face-unresolved-face');
    unresolved.topology.faces.push({ id: 'face-unresolved-face', bodyId: 'body-1' });
    unresolved.operationGraph.unresolved.push({ featureId: 'unresolved-face',
        reason: 'unclassified_brep_face', required: true });
    assert.throws(() => c.CncPlanningDiagnostics.plan(unresolved), error =>
        error && error.code === 'unresolved_required_feature');
});
