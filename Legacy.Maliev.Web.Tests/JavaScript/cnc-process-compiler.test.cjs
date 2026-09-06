const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const crypto = require('node:crypto');

const moduleRoot = path.resolve(__dirname,
    '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation');
const z = Object.freeze({ x: 0, y: 0, z: 1 });

function runtime(includePlanning = false) {
    const context = vm.createContext({ console, TextEncoder, crypto: crypto.webcrypto });
    context.self = context;
    context.window = context;
    const names = ['cnc-plan-contracts', 'cnc-quotation-config', 'cnc-material-catalog',
        'cnc-tool-library', 'cnc-process-compiler'];
    if (includePlanning) {
        names.push('cnc-reach', 'cnc-fixture-clearance', 'cnc-machine-capability',
            'cnc-setup-planner', 'cnc-planning');
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

function featureGraph(features, unresolved = []) {
    const faceOwners = {};
    const machinableFaceIds = [];
    for (const feature of features) {
        for (const faceId of feature.primaryFaceIds || []) {
            faceOwners[faceId] = feature.id;
            machinableFaceIds.push(faceId);
        }
    }
    return {
        contract: 'ManufacturingFeatureGraph.v1', topologyRevision: 'topology-1',
        features, faceOwners, machinableFaceIds, unresolved
    };
}

function thread(id, kind, designation, diameter, pitch, additions = {}) {
    return {
        id, kind, threadDesignation: designation, nominalDiameterMm: diameter,
        pitchMm: pitch, pilotDiameterMm: diameter <= 12 ? diameter - pitch : diameter - 2,
        primaryFaceIds: ['face-' + id], secondaryFeatureIds: [], accessAxes: [z],
        ...(kind === 'external_thread'
            ? { majorDiameterPreparationRequired: false, leadInChamferRequired: false }
            : {}),
        ...additions
    };
}

test('tapped M6 chain is spot drill tap in one feature DAG', () => {
    const c = runtime();
    assert.equal(typeof c.CncProcessCompiler?.compile, 'function');
    const graph = featureGraph([thread('thread-1', 'internal_thread', 'M6 x 1', 6, 1,
        { pilotDiameterMm: 5 })]);
    const operations = Array.from(c.CncProcessCompiler.compile(graph, {}).operations);
    assert.deepEqual(operations.map(operation => operation.kind),
        ['spot_drilling', 'drilling', 'tapping']);
    assert.deepEqual(Array.from(operations[1].predecessors), [operations[0].id]);
    assert.deepEqual(Array.from(operations[2].predecessors), [operations[1].id]);
    assert.ok(operations.every(operation => operation.featureId === 'thread-1'));
    assert.equal(operations[1].toolConstraints.nominalDiameterMm, 5);
    assert.equal(operations[2].toolConstraints.threadDesignation, 'M6 x 1');
});

test('external M14 and internal M16 use thread milling with nominal designation and pitch', () => {
    const c = runtime();
    const operations = Array.from(c.CncProcessCompiler.compile(featureGraph([
        thread('external', 'external_thread', 'M14 x 1', 14, 1),
        thread('internal', 'internal_thread', 'M16 x 2', 16, 2, { pilotDiameterMm: 14 })
    ]), {}).operations);
    const threadMills = operations.filter(operation => operation.kind === 'thread_milling');
    assert.equal(threadMills.length, 2);
    assert.equal(operations.filter(operation => operation.kind === 'tapping').length, 0);
    assert.deepEqual(threadMills.map(operation => [operation.toolConstraints.threadDesignation,
        operation.toolConstraints.pitchMm]), [['M14 x 1', 1], ['M16 x 2', 2]]);
    assert.deepEqual(operations.filter(operation => operation.featureId === 'internal')
        .map(operation => operation.kind), ['spot_drilling', 'bore_preparation', 'thread_milling']);
});

test('internal M16 bore preparation is a valid predecessor for thread milling', async () => {
    const c = runtime();
    const features = featureGraph([
        thread('internal', 'internal_thread', 'M16 x 2', 16, 2, { pilotDiameterMm: 14 })
    ]);
    const operations = c.CncProcessCompiler.compile(features, {});
    const plan = {
        contract: 'ValidatedManufacturingPlan.v1', geometryRevision: 'topology-1',
        requirementsRevision: 'requirements-1', plannerVersion: 'cnc-feature-planner-v3',
        toolLibraryVersion: operations.toolLibraryVersion,
        topology: { contract: 'CncCadTopology.v1', revision: 'topology-1',
            bodies: [], faces: [], edges: [] },
        featureGraph: features, operationGraph: operations,
        setupPlan: { contract: 'SetupPlan.v1', geometryRevision: 'topology-1',
            setups: [{ id: 'setup-1', operationIds: Array.from(operations.operations, item => item.id) }] },
        unresolvedReasons: []
    };
    plan.planHash = await c.CncPlanContracts.hash(plan);
    await c.CncPlanContracts.validatePlan(plan);
});

test('8.23 mm slot selects one slot cutter band without a diameter staircase', () => {
    const c = runtime();
    const operations = Array.from(c.CncProcessCompiler.compile(featureGraph([{
        id: 'slot-1', kind: 'slot', dimensions: { widthMm: 8.23, depthMm: 4 },
        cornerEnvelope: { kind: 'open_ended_corridor', maximumDiameterMm: 8.23, openEndCount: 2 },
        primaryFaceIds: ['face-slot'], secondaryFeatureIds: [], accessAxes: [z]
    }]), {}).operations);
    assert.deepEqual(operations.map(operation => [operation.phase,
        operation.toolConstraints.maximumDiameterMm]), [['rough', 6], ['finish', 6]]);
});

test('a proven bulk and corner constraint creates only one bulk and one rest band per phase', () => {
    const c = runtime();
    const operations = Array.from(c.CncProcessCompiler.compile(featureGraph([{
        id: 'pocket-1', kind: 'pocket',
        dimensions: { widthMm: 30, depthMm: 6, internalCornerRadiusMm: 3 },
        primaryFaceIds: ['face-pocket'], secondaryFeatureIds: [], accessAxes: [z]
    }]), {}).operations);
    assert.deepEqual(operations.map(operation => [operation.phase,
        operation.toolConstraints.maximumDiameterMm]),
    [['rough', 16], ['rough', 6], ['finish', 16], ['finish', 6]]);
    assert.deepEqual(Array.from(operations[2].predecessors), [operations[1].id]);
    assert.deepEqual(Array.from(operations[3].predecessors), [operations[2].id]);
});

test('prismatic features without complete corridor and depth evidence emit no speculative cutter', () => {
    const c = runtime();
    for (const dimensions of [{ depthMm: 4 }, { widthMm: 8.23 }, {}]) {
        const graph = c.CncProcessCompiler.compile(featureGraph([{
            id: 'slot-1', kind: 'slot', dimensions,
            primaryFaceIds: ['face-slot'], secondaryFeatureIds: [], accessAxes: [z]
        }]), {});
        assert.deepEqual(Array.from(graph.operations), []);
        assert.ok(Array.from(graph.unresolved).some(item =>
            item.reason === 'prismatic_envelope_required'));
    }
});

test('a required rest band without a feasible tool fails closed before emitting bulk work', () => {
    const c = runtime();
    const graph = c.CncProcessCompiler.compile(featureGraph([{
        id: 'pocket-1', kind: 'pocket',
        dimensions: { widthMm: 30, depthMm: 20, internalCornerRadiusMm: 0.2 },
        primaryFaceIds: ['face-pocket'], secondaryFeatureIds: [], accessAxes: [z]
    }]), {});
    assert.deepEqual(Array.from(graph.operations), []);
    assert.ok(Array.from(graph.unresolved).some(item =>
        item.reason === 'unsupported_prismatic_rest_tool'));
});

test('thread and chamfer features compile no tiny flat or ball operation', () => {
    const c = runtime();
    const operations = Array.from(c.CncProcessCompiler.compile(featureGraph([
        thread('external', 'external_thread', 'M14 x 1', 14, 1),
        { id: 'chamfer-1', kind: 'chamfer', dimensions: { includedAngleDegrees: 90 },
            primaryFaceIds: ['face-chamfer'], secondaryFeatureIds: [], accessAxes: [z] }
    ]), {}).operations);
    assert.equal(operations.some(operation => operation.toolClass === 'ball_end_mill'), false);
    assert.equal(operations.some(operation => operation.toolClass === 'flat_end_mill'
        && operation.toolConstraints.maximumDiameterMm <= 2), false);
    assert.ok(operations.some(operation => operation.toolClass === 'chamfer_mill'));
    assert.equal(JSON.stringify(operations).includes('cluster'), false);
    assert.equal(JSON.stringify(operations).includes('sample'), false);
});

test('ball finishing requires certified non-prismatic curvature with a complete envelope and is deduplicated', () => {
    const c = runtime();
    const graph = c.CncProcessCompiler.compile(featureGraph([
        { id: 'uncertified', kind: 'freeform_patch', primaryFaceIds: ['face-u'],
            secondaryFeatureIds: [], accessAxes: [z] },
        { id: 'certified', kind: 'fillet', certification: 'non_prismatic_curvature',
            dimensions: { radiusMm: 3, depthMm: 8 }, primaryFaceIds: ['face-c'],
            secondaryFeatureIds: [], accessAxes: [z] }
    ]), {});
    const operations = Array.from(graph.operations);
    assert.equal(operations.filter(operation => operation.featureId === 'uncertified').length, 0);
    assert.deepEqual(Array.from(graph.unresolved, item => ({ ...item })), [{ featureId: 'uncertified',
        reason: 'uncertified_freeform_feature', required: true }]);
    assert.equal(operations.filter(operation => operation.featureId === 'certified'
        && operation.toolClass === 'ball_end_mill').length, 1);
});

test('certified curvature without radius or reach evidence emits no default ball tool', () => {
    const c = runtime();
    for (const dimensions of [{ radiusMm: 3 }, { depthMm: 8 },
        { radiusMm: 3, requiredReachMm: 8 }, {}]) {
        const graph = c.CncProcessCompiler.compile(featureGraph([{
            id: 'curved', kind: 'freeform_patch', certification: 'non_prismatic_curvature',
            dimensions, primaryFaceIds: ['face-curved'], secondaryFeatureIds: [], accessAxes: [z]
        }]), {});
        assert.deepEqual(Array.from(graph.operations), []);
        assert.ok(Array.from(graph.unresolved).some(item =>
            item.reason === 'certified_curvature_envelope_required'));
    }
});

test('external thread preparation and lead-in are explicit predecessor phases when required', () => {
    const c = runtime();
    const operations = Array.from(c.CncProcessCompiler.compile(featureGraph([
        thread('external', 'external_thread', 'M14 x 1', 14, 1, {
            majorDiameterPreparationRequired: true, leadInChamferRequired: true,
            dimensions: { depthMm: 18, includedAngleDegrees: 90 }
        })
    ]), {}).operations);
    assert.deepEqual(operations.map(operation => operation.kind),
        ['major_diameter_preparation', 'chamfering', 'thread_milling']);
    assert.deepEqual(Array.from(operations[1].predecessors), [operations[0].id]);
    assert.deepEqual(Array.from(operations[2].predecessors), [operations[1].id]);
    assert.equal(operations[2].toolConstraints.threadDesignation, 'M14 x 1');
    assert.equal(operations[2].toolConstraints.pitchMm, 1);

    const prepared = Array.from(c.CncProcessCompiler.compile(featureGraph([
        thread('prepared', 'external_thread', 'M14 x 1', 14, 1)
    ]), {}).operations);
    assert.deepEqual(prepared.map(operation => operation.kind), ['thread_milling']);
    assert.equal(prepared[0].toolConstraints.majorDiameterPreparationRequired, false);
    assert.equal(prepared[0].toolConstraints.leadInChamferRequired, false);
});

test('compiler validates the feature contract and fails closed on unresolved graph input', () => {
    const c = runtime();
    assert.throws(() => c.CncProcessCompiler.compile({ contract: 'bad' }, {}),
        error => error && error.code === 'revision_mismatch');
    const graph = c.CncProcessCompiler.compile(featureGraph([], [
        { featureId: 'feature-x', reason: 'unclassified_brep_face', required: true }
    ]), {});
    assert.deepEqual(Array.from(graph.operations), []);
    assert.deepEqual(Array.from(graph.unresolved, item => ({ ...item })), [
        { featureId: 'feature-x', reason: 'unclassified_brep_face', required: true }
    ]);
});

test('production planning requires and validates feature and operation graphs before geometry planning', () => {
    const c = runtime(true);
    assert.throws(() => c.CncPlanningDiagnostics.plan({ production: true }),
        error => error && error.code === 'manufacturing_feature_graph_required');
    const features = featureGraph([
        { id: 'datum-1', kind: 'datum', primaryFaceIds: ['face-datum'],
            machiningRequired: true, facingEvidence: { source: 'brep_primary_datum_selection',
                faceIds: ['face-datum'], accessAxis: z },
            secondaryFeatureIds: [], accessAxes: [z] },
        thread('thread-1', 'internal_thread', 'M6 x 1', 6, 1)
    ]);
    const operations = c.CncProcessCompiler.compile(features, {});
    const fixture = { id: 'fixture-top', orientation: z, fixtureId: 'fixture-vise', fixtureState: 'vise',
        datumFeatureIds: ['datum-1'], datumFaceIds: ['face-datum'], clampFaceIds: ['face-datum'],
        supportedOperationIds: Array.from(operations.operations, operation => operation.id),
        inputStockState: 'stock-initial', outputStockState: 'stock-final',
        handlingMinutes: 3,
        catalogCapability: { catalogVersion: 'test-fixtures-v2', fixtureId: 'fixture-vise',
            maximumOpeningMm: 100, minimumGripMm: 3 },
        datumContact: { topologyFaceId: 'face-datum', surfaceKind: 'plane' },
        clampContact: { topologyFaceId: 'face-datum', surfaceKind: 'plane' },
        fixtureCapability: { catalogVersion: 'test-fixtures-v2', fixtureId: 'fixture-vise',
            maximumToolReachMm: 100, accessAxes: [z], obstacles: [] } };
    const stockState = id => ({ id, compatibleFixtureIds: ['fixture-vise'],
        availableDatumFaceIds: ['face-datum'], availableClampFaceIds: ['face-datum'] });
    const input = { production: true,
        topology: { contract: 'CncCadTopology.v1', revision: 'topology-1', sourceKind: 'brep',
            automaticPlanningEligible: true, bodies: [{ id: 'body-1' }],
            faces: [{ id: 'face-datum', bodyId: 'body-1', surface: { kind: 'plane', normal: z },
                    validationVolume: { minimum: { x: 0, y: 0, z: 0 }, maximum: { x: 10, y: 10, z: 1 } } },
                { id: 'face-thread-1', bodyId: 'body-1' }], edges: [] },
        featureGraph: features, operationGraph: operations,
        stock: { stateId: 'stock-initial', states: [stockState('stock-initial'), stockState('stock-final')] },
        fixtureCatalog: { contract: 'CncFixtureCatalog.v2', version: 'test-fixtures-v2', candidates: [fixture] } };
    Object.defineProperty(input, 'geometry', { get() { throw new Error('legacy geometry path was read'); } });
    const plan = c.CncPlanningDiagnostics.plan(input);
    assert.equal(plan.featureGraph, features);
    assert.equal(plan.operationGraph, operations);
    assert.deepEqual(Array.from(plan.operations, operation => operation.kind),
        ['facing', 'spot_drilling', 'drilling', 'tapping']);
    assert.equal(plan.setupPlan.contract, 'SetupPlan.v1');
    assert.equal(plan.setupCount, 1);
    assert.equal(plan.setupPlanningPending, false);
});

test('production planning rejects arbitrary operations and recursive legacy provenance before setup planning', () => {
    const c = runtime(true);
    const features = featureGraph([thread('thread-1', 'internal_thread', 'M6 x 1', 6, 1)]);
    const valid = c.CncProcessCompiler.compile(features, {});
    const clone = value => JSON.parse(JSON.stringify(value));
    const rejects = mutate => {
        const graph = clone(valid);
        mutate(graph.operations[0]);
        assert.throws(() => c.CncPlanningDiagnostics.plan({ production: true,
            featureGraph: features, operationGraph: graph }),
        error => error && error.code === 'broken_feature_reference');
    };
    rejects(operation => { operation.featureId = 'missing-feature'; });
    rejects(operation => { operation.kind = 'legacy_cluster_roughing'; });
    rejects(operation => { delete operation.toolClass; });
    rejects(operation => { operation.provenance.sourceContract = 'LegacyClusterEvidence.v1'; });
    rejects(operation => { operation.provenance.details = { sampleIds: ['sample-1'] }; });
    rejects(operation => { operation.toolConstraints.clusterId = 'cluster-1'; });
    const legacyFeatures = clone(features);
    legacyFeatures.features[0].clusterIds = ['cluster-1'];
    assert.throws(() => c.CncPlanningDiagnostics.plan({ production: true,
        featureGraph: legacyFeatures, operationGraph: valid }),
    error => error && error.code === 'broken_feature_reference');
});

test('production validation rejects finishing without a same-feature rough predecessor', () => {
    const c = runtime(true);
    const features = featureGraph([{
        id: 'slot-1', kind: 'slot', dimensions: { widthMm: 8.23, depthMm: 4 },
        cornerEnvelope: { kind: 'open_ended_corridor', maximumDiameterMm: 8.23, openEndCount: 2 },
        primaryFaceIds: ['face-slot'], secondaryFeatureIds: [], accessAxes: [z]
    }]);
    const graph = c.CncProcessCompiler.compile(features, {});
    graph.operations.find(operation => operation.phase === 'finish').predecessors = [];
    assert.throws(() => c.CncPlanningDiagnostics.plan({ production: true,
        featureGraph: features, operationGraph: graph }),
    error => error && error.code === 'broken_predecessor');
});
