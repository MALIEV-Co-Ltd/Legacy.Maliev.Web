const test = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function runtime(name) {
    const context = vm.createContext({ console, TextEncoder, crypto: crypto.webcrypto });
    context.self = context;
    const file = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation', name + '.js');
    if (fs.existsSync(file)) {
        vm.runInContext(fs.readFileSync(file, 'utf8'), context, { filename: file });
    }
    return context;
}

function featureGraph(features, unresolved) {
    const resolvedFeatures = features || [{ id: 'feature-1', kind: 'hole',
        dimensions: { diameterMm: 5, depthMm: 10 },
        primaryFaceIds: ['face-1'], secondaryFeatureIds: [] }];
    const faceOwners = {};
    const machinableFaceIds = [];
    for (const feature of resolvedFeatures) for (const faceId of feature.primaryFaceIds || []) {
        faceOwners[faceId] = feature.id;
        machinableFaceIds.push(faceId);
    }
    return {
        contract: 'ManufacturingFeatureGraph.v1',
        topologyRevision: 'topology-1',
        features: resolvedFeatures,
        faceOwners,
        machinableFaceIds,
        unresolved: unresolved || []
    };
}

function operationGraph(operations) {
    const normalized = (operations || [
        { id: 'spot-1', featureId: 'feature-1', kind: 'spot_drilling', phase: 'spot', toolConstraints: { toolClass: 'spot_drill' }, predecessors: [] },
        { id: 'operation-1', featureId: 'feature-1', kind: 'drilling', phase: 'drill', toolClass: 'hss_drill', toolConstraints: { toolClass: 'hss_drill', minimumCutLengthMm: 10, minimumReachMm: 10 }, predecessors: ['spot-1'] }
    ]).map(operation => Object.assign({
        toolClass: operation.toolClass || operation.toolConstraints && operation.toolConstraints.toolClass,
        accessAxis: { x: 0, y: 0, z: 1 },
        provenance: { sourceContract: 'ManufacturingFeatureGraph.v1', featureId: operation.featureId }
    }, operation));
    return {
        contract: 'MachiningOperationGraph.v1',
        topologyRevision: 'topology-1',
        toolLibraryVersion: 'test-tools-v1',
        operations: normalized,
        unresolved: []
    };
}

function setupPlan(setups) {
    return {
        contract: 'SetupPlan.v1',
        geometryRevision: 'topology-1',
        setups: setups || [{ id: 'setup-1', operationIds: ['spot-1', 'operation-1'] }]
    };
}

function validPlan(overrides) {
    return Object.assign({
        contract: 'ValidatedManufacturingPlan.v1',
        geometryRevision: 'topology-1',
        requirementsRevision: 'requirements-1',
        plannerVersion: 'cnc-feature-planner-v3',
        toolLibraryVersion: 'test-tools-v1',
        topology: { contract: 'CncCadTopology.v1', revision: 'topology-1', bodies: [], faces: [], edges: [] },
        featureGraph: featureGraph(),
        operationGraph: operationGraph(),
        setupPlan: setupPlan(),
        unresolvedReasons: []
    }, overrides);
}

function assertCode(code, action) {
    assert.throws(action, error => error && error.code === code, code + ' must remain a stable error code');
}

test('canonical identity ignores object insertion order but preserves ordered arrays', async () => {
    const c = runtime('cnc-plan-contracts');
    assert.equal(await c.CncPlanContracts.hash({ b: 2, a: 1 }),
        await c.CncPlanContracts.hash({ a: 1, b: 2 }));
    assert.notEqual(await c.CncPlanContracts.hash({ a: [1, 2] }),
        await c.CncPlanContracts.hash({ a: [2, 1] }));
});
test('authenticated planner revision rejects previously sealed v2 plans', async () => {
    const c = runtime('cnc-plan-contracts'), plan = validPlan({ plannerVersion: 'cnc-feature-planner-v2' });
    plan.planHash = await c.CncPlanContracts.hash(plan);
    await assert.rejects(async () => c.CncPlanContracts.validatePlan(plan), /version/i);
});

test('feature graph rejects duplicate primary face ownership', () => {
    const c = runtime('cnc-plan-contracts');
    assert.throws(() => c.CncPlanContracts.validateFeatureGraph({
        contract: 'ManufacturingFeatureGraph.v1', topologyRevision: 'g1',
        features: [
            { id: 'f1', kind: 'chamfer', primaryFaceIds: ['face-1'], secondaryFeatureIds: [] },
            { id: 'f2', kind: 'freeform_patch', primaryFaceIds: ['face-1'], secondaryFeatureIds: [] }
        ], unresolved: []
    }), error => error && error.code === 'duplicate_primary_face_owner' && /face-1.*owned/i.test(error.message));
});

test('feature graph rejects unknown kinds and broken secondary feature references', () => {
    const c = runtime('cnc-plan-contracts');
    assertCode('unknown_feature_kind', () => c.CncPlanContracts.validateFeatureGraph(featureGraph([
        { id: 'feature-1', kind: 'imagined_feature', primaryFaceIds: ['face-1'], secondaryFeatureIds: [] }
    ])));
    assertCode('broken_feature_reference', () => c.CncPlanContracts.validateFeatureGraph(featureGraph([
        { id: 'feature-1', kind: 'hole', primaryFaceIds: ['face-1'], secondaryFeatureIds: ['missing-feature'] }
    ])));
});

test('feature graph requires exact exclusive face ownership and unique machinable face IDs', () => {
    const c = runtime('cnc-plan-contracts');
    const valid = featureGraph();
    for (const mutate of [
        graph => { graph.machinableFaceIds.push('face-1'); },
        graph => { delete graph.faceOwners['face-1']; },
        graph => { graph.faceOwners['face-1'] = 'missing-feature'; },
        graph => { graph.faceOwners['face-2'] = 'feature-1'; graph.machinableFaceIds.push('face-2'); },
        graph => { graph.features[0].primaryFaceIds = ['face-2']; },
        graph => { graph.features[0].primaryFaceIds = ['']; graph.machinableFaceIds = [''];
            graph.faceOwners = { '': 'feature-1' }; }
    ]) {
        const graph = structuredClone(valid);
        mutate(graph);
        assertCode('broken_feature_reference', () => c.CncPlanContracts.validateFeatureGraph(graph));
    }
    const duplicatePrimary = structuredClone(valid);
    duplicatePrimary.features[0].primaryFaceIds = ['face-1', 'face-1'];
    assertCode('duplicate_primary_face_owner', () => c.CncPlanContracts.validateFeatureGraph(duplicatePrimary));
    const resolvedSlotWithoutPrimaryFaces = featureGraph([{
        id: 'slot-1', kind: 'slot', dimensions: { widthMm: 8, depthMm: 4 },
        primaryFaceIds: [], secondaryFeatureIds: []
    }]);
    assertCode('broken_feature_reference', () =>
        c.CncPlanContracts.validateFeatureGraph(resolvedSlotWithoutPrimaryFaces));
    c.CncPlanContracts.validateFeatureGraph(valid);
});

test('operation graph rejects duplicate operations and broken predecessors', () => {
    const c = runtime('cnc-plan-contracts');
    assertCode('duplicate_operation', () => c.CncPlanContracts.validateOperationGraph(operationGraph([
        { id: 'operation-1', featureId: 'feature-1', kind: 'drilling', phase: 'finish', predecessors: [] },
        { id: 'operation-2', featureId: 'feature-1', kind: 'drilling', phase: 'finish', predecessors: [] }
    ])));
    assertCode('broken_predecessor', () => c.CncPlanContracts.validateOperationGraph(operationGraph([
        { id: 'operation-1', featureId: 'feature-1', kind: 'drilling', phase: 'finish', predecessors: ['missing-operation'] }
    ])));
});

test('plan rejects uncovered operations, empty setups, unresolved required features, and revision mismatches', () => {
    const c = runtime('cnc-plan-contracts');
    assertCode('unassigned_operation', () => c.CncPlanContracts.validatePlan(validPlan({
        setupPlan: setupPlan([{ id: 'setup-1', operationIds: [] }])
    })));
    assertCode('empty_setup', () => c.CncPlanContracts.validatePlan(validPlan({
        featureGraph: featureGraph([{ id: 'unresolved-1', kind: 'unresolved', required: false,
            primaryFaceIds: ['face-1'], secondaryFeatureIds: [] }]),
        operationGraph: operationGraph([]),
        setupPlan: setupPlan([{ id: 'setup-1', operationIds: [] }])
    })));
    assertCode('unresolved_required_feature', () => c.CncPlanContracts.validatePlan(validPlan({
        featureGraph: featureGraph(undefined, [{ id: 'feature-2', required: true, reason: 'ambiguous' }])
    })));
    const unresolvedSlotOperations = operationGraph([]);
    unresolvedSlotOperations.unresolved = [{ featureId: 'slot-1',
        reason: 'unsupported_prismatic_tool', required: true }];
    assertCode('unresolved_required_feature', () => c.CncPlanContracts.validatePlan(validPlan({
        featureGraph: featureGraph([{ id: 'slot-1', kind: 'slot',
            dimensions: { widthMm: 8, depthMm: 4 }, primaryFaceIds: ['face-1'],
            secondaryFeatureIds: [] }]),
        operationGraph: unresolvedSlotOperations,
        setupPlan: setupPlan([{ id: 'setup-1', operationIds: [] }])
    })));
    assertCode('revision_mismatch', () => c.CncPlanContracts.validatePlan(validPlan({
        operationGraph: Object.assign(operationGraph(), { topologyRevision: 'topology-2' })
    })));
});

test('plan requires exact versioned contracts and a shared non-empty topology revision', () => {
    const c = runtime('cnc-plan-contracts');
    assert.equal(typeof c.CncPlanContracts.validateTopology, 'function');
    for (const override of [
        { contract: 'ValidatedManufacturingPlan.v2' },
        { geometryRevision: '' },
        { topology: { contract: 'CncCadTopology.v2', revision: 'topology-1' } },
        { topology: { contract: 'CncCadTopology.v1', revision: '' } },
        { featureGraph: Object.assign(featureGraph(), { contract: 'ManufacturingFeatureGraph.v2' }) },
        { operationGraph: Object.assign(operationGraph(), { contract: 'MachiningOperationGraph.v2' }) },
        { setupPlan: Object.assign(setupPlan(), { contract: 'SetupPlan.v2' }) }
    ]) {
        assertCode('revision_mismatch', () => c.CncPlanContracts.validatePlan(validPlan(override)));
    }
});

test('plan rejects setup operation IDs that do not exist in the operation graph', () => {
    const c = runtime('cnc-plan-contracts');
    assertCode('broken_feature_reference', () => c.CncPlanContracts.validatePlan(validPlan({
        operationGraph: operationGraph([]),
        setupPlan: setupPlan([{ id: 'setup-1', operationIds: ['missing-operation'] }])
    })));
});

test('plan rejects a resolved slot with no operations even when another feature is fully assigned', () => {
    const c = runtime('cnc-plan-contracts');
    const slot = { id: 'slot-1', kind: 'slot', dimensions: { widthMm: 8, depthMm: 4 },
        primaryFaceIds: ['face-1'], secondaryFeatureIds: [] };
    const hole = { id: 'hole-1', kind: 'hole', dimensions: { diameterMm: 5, depthMm: 10 },
        primaryFaceIds: ['face-2'], secondaryFeatureIds: [] };
    const spot = { id: 'spot-1', featureId: 'hole-1', kind: 'spot_drilling', phase: 'spot',
        toolClass: 'spot_drill', toolConstraints: { toolClass: 'spot_drill' }, predecessors: [] };
    const drill = { id: 'drill-1', featureId: 'hole-1', kind: 'drilling', phase: 'drill',
        toolClass: 'hss_drill', toolConstraints: { toolClass: 'hss_drill' }, predecessors: ['spot-1'] };
    assertCode('broken_feature_reference', () => c.CncPlanContracts.validatePlan(validPlan({
        featureGraph: featureGraph([slot, hole]),
        operationGraph: operationGraph([spot, drill]),
        setupPlan: setupPlan([{ id: 'setup-1', operationIds: ['spot-1', 'drill-1'] }])
    })));
});

test('plan rejects ball-end operations sourced from specialized or cylindrical features', () => {
    const c = runtime('cnc-plan-contracts');
    for (const feature of [
        { id: 'feature-1', kind: 'chamfer', primaryFaceIds: ['face-1'], secondaryFeatureIds: [] },
        { id: 'feature-1', kind: 'internal_thread', primaryFaceIds: ['face-1'], secondaryFeatureIds: [] },
        { id: 'feature-1', kind: 'hole', surfaceKind: 'cylinder', primaryFaceIds: ['face-1'], secondaryFeatureIds: [] }
    ]) {
        assertCode('broken_feature_reference', () => c.CncPlanContracts.validatePlan(validPlan({
            featureGraph: featureGraph([feature]),
            operationGraph: operationGraph([{ id: 'ball-1', featureId: 'feature-1', kind: 'finishing', phase: 'finish', toolClass: 'ball_end_mill', toolConstraints: { toolClass: 'ball_end_mill' }, predecessors: [] }]),
            setupPlan: setupPlan([{ id: 'setup-1', operationIds: ['ball-1'] }])
        })));
    }
});

test('plan enforces spot-drill threading chains and thread-milling policy', () => {
    const c = runtime('cnc-plan-contracts');
    const spot = { id: 'spot-1', featureId: 'feature-1', kind: 'spot_drilling', phase: 'spot', toolConstraints: { toolClass: 'spot_drill' }, predecessors: [] };
    const drill = { id: 'drill-1', featureId: 'feature-1', kind: 'drilling', phase: 'drill', toolClass: 'hss_drill', toolConstraints: { toolClass: 'hss_drill' }, predecessors: ['spot-1'] };
    const tapWithoutDrill = { id: 'tap-1', featureId: 'feature-1', kind: 'tapping', phase: 'thread', toolConstraints: { toolClass: 'tap' }, predecessors: [] };
    const tapping = Object.assign({}, tapWithoutDrill, { predecessors: ['drill-1'] });
    const assignments = operations => setupPlan([{ id: 'setup-1', operationIds: operations.map(operation => operation.id) }]);

    assertCode('broken_predecessor', () => c.CncPlanContracts.validatePlan(validPlan({
        featureGraph: featureGraph([{ id: 'feature-1', kind: 'internal_thread', nominalDiameterMm: 6, primaryFaceIds: ['face-1'], secondaryFeatureIds: [] }]),
        operationGraph: operationGraph([spot, drill, tapWithoutDrill]),
        setupPlan: assignments([spot, drill, tapWithoutDrill])
    })));
    assertCode('broken_feature_reference', () => c.CncPlanContracts.validatePlan(validPlan({
        featureGraph: featureGraph([{ id: 'feature-1', kind: 'external_thread', nominalDiameterMm: 14, primaryFaceIds: ['face-1'], secondaryFeatureIds: [] }]),
        operationGraph: operationGraph([spot, drill, tapping]),
        setupPlan: assignments([spot, drill, tapping])
    })));
    assertCode('broken_feature_reference', () => c.CncPlanContracts.validatePlan(validPlan({
        featureGraph: featureGraph([{ id: 'feature-1', kind: 'internal_thread', nominalDiameterMm: 16, primaryFaceIds: ['face-1'], secondaryFeatureIds: [] }]),
        operationGraph: operationGraph([spot, drill, tapping]),
        setupPlan: assignments([spot, drill, tapping])
    })));
});

test('plan keeps spot-drill predecessor chains within their feature', async () => {
    const c = runtime('cnc-plan-contracts');
    const holeSpot = { id: 'hole-spot', featureId: 'hole-1', kind: 'spot_drilling', phase: 'spot', toolConstraints: { toolClass: 'spot_drill' }, predecessors: [] };
    const holeDrill = { id: 'hole-drill', featureId: 'hole-1', kind: 'drilling', phase: 'drill',
        toolClass: 'hss_drill', toolConstraints: { toolClass: 'hss_drill', minimumCutLengthMm: 10, minimumReachMm: 10 }, predecessors: ['hole-spot'] };
    const threadDrill = { id: 'thread-drill', featureId: 'thread-1', kind: 'drilling', phase: 'drill', toolClass: 'hss_drill', toolConstraints: { toolClass: 'hss_drill' }, predecessors: ['hole-spot'] };
    const threadTap = { id: 'thread-tap', featureId: 'thread-1', kind: 'tapping', phase: 'thread',
        toolConstraints: { toolClass: 'tap', threadDesignation: 'M6 x 1', nominalDiameterMm: 6, pitchMm: 1 },
        predecessors: ['thread-drill'] };
    const graph = featureGraph([
        { id: 'hole-1', kind: 'hole', dimensions: { diameterMm: 5, depthMm: 10 }, primaryFaceIds: ['face-1'], secondaryFeatureIds: [] },
        { id: 'thread-1', kind: 'internal_thread', threadDesignation: 'M6 x 1',
            nominalDiameterMm: 6, pitchMm: 1, pilotDiameterMm: 5,
            primaryFaceIds: ['face-2'], secondaryFeatureIds: [] }
    ]);
    const crossFeaturePlan = validPlan({
        featureGraph: graph,
        operationGraph: operationGraph([holeSpot, holeDrill, threadDrill, threadTap]),
        setupPlan: setupPlan([{ id: 'setup-1',
            operationIds: ['hole-spot', 'hole-drill', 'thread-drill', 'thread-tap'] }])
    });
    crossFeaturePlan.planHash = await c.CncPlanContracts.hash(crossFeaturePlan);
    assertCode('broken_predecessor', () => c.CncPlanContracts.validatePlan(crossFeaturePlan));

    const threadSpot = Object.assign({}, holeSpot, { id: 'thread-spot', featureId: 'thread-1' });
    const sameFeatureDrill = Object.assign({}, threadDrill, { predecessors: ['thread-spot'] });
    const sameFeaturePlan = validPlan({
        featureGraph: graph,
        operationGraph: operationGraph([holeSpot, holeDrill, threadSpot, sameFeatureDrill, threadTap]),
        setupPlan: setupPlan([{ id: 'setup-1',
            operationIds: ['hole-spot', 'hole-drill', 'thread-spot', 'thread-drill', 'thread-tap'] }])
    });
    sameFeaturePlan.planHash = await c.CncPlanContracts.hash(sameFeaturePlan);
    await c.CncPlanContracts.validatePlan(sameFeaturePlan);
});

test('plan hash must match the canonical validated plan', async () => {
    const c = runtime('cnc-plan-contracts');
    const plan = validPlan();
    plan.planHash = await c.CncPlanContracts.hash(plan);
    await c.CncPlanContracts.validatePlan(plan);
    await assert.rejects(() => c.CncPlanContracts.validatePlan(Object.assign({}, plan, {
        planHash: '0'.repeat(64)
    })), error => error && error.code === 'plan_hash_mismatch');
    await assert.rejects(() => c.CncPlanContracts.validatePlan(Object.assign({}, plan, {
        requirementsRevision: 'requirements-2'
    })), error => error && error.code === 'plan_hash_mismatch');
});

test('final plan validation rejects recursive cluster and sample evidence', () => {
    const c = runtime('cnc-plan-contracts');
    for (const mutate of [
        plan => { plan.operationGraph.operations[0].toolConstraints.clusterIds = ['cluster-1']; },
        plan => { plan.operationGraph.operations[0].provenance.details = { sampleIds: ['sample-1'] }; },
        plan => { plan.featureGraph.features[0].evidence = { sourceTriangleIndexes: [1] }; }
    ]) {
        const plan = validPlan();
        mutate(plan);
        assertCode('broken_feature_reference', () => c.CncPlanContracts.validatePlan(plan));
    }
});

test('final plan accepts ball finishing only for a certified feature with a matching envelope', async () => {
    const c = runtime('cnc-plan-contracts');
    const feature = { id: 'feature-1', kind: 'fillet', certification: 'non_prismatic_curvature',
        dimensions: { radiusMm: 3, depthMm: 8 }, primaryFaceIds: ['face-1'], secondaryFeatureIds: [] };
    const ball = { id: 'ball-1', featureId: 'feature-1', kind: 'ball_end_finishing', phase: 'finish',
        toolClass: 'ball_end_mill', toolConstraints: { toolClass: 'ball_end_mill',
            certification: 'non_prismatic_curvature', maximumDiameterMm: 6,
            minimumCutLengthMm: 8, minimumReachMm: 8 }, predecessors: [] };
    const make = candidate => validPlan({ featureGraph: featureGraph([candidate]),
        operationGraph: operationGraph([structuredClone(ball)]),
        setupPlan: setupPlan([{ id: 'setup-1', operationIds: ['ball-1'] }]) });

    for (const candidate of [
        { ...feature, certification: undefined },
        { ...feature, dimensions: { radiusMm: 3 } },
        { ...feature, dimensions: { radiusMm: 3, requiredReachMm: 8 } }
    ]) assertCode('broken_feature_reference', () => c.CncPlanContracts.validatePlan(make(candidate)));

    const badEnvelope = make(feature);
    badEnvelope.operationGraph.operations[0].toolConstraints.maximumDiameterMm = 8;
    assertCode('broken_feature_reference', () => c.CncPlanContracts.validatePlan(badEnvelope));

    const valid = make(feature);
    valid.planHash = await c.CncPlanContracts.hash(valid);
    await c.CncPlanContracts.validatePlan(valid);
});

test('final plan enforces complete internal and external thread process templates', async () => {
    const c = runtime('cnc-plan-contracts');
    const axis = { x: 0, y: 0, z: 1 };
    const op = (id, featureId, kind, phase, toolClass, predecessors, constraints = {}) => ({
        id, featureId, kind, phase, toolClass, accessAxis: axis, predecessors,
        toolConstraints: { toolClass, ...constraints },
        provenance: { sourceContract: 'ManufacturingFeatureGraph.v1', featureId }
    });
    const planFor = (feature, operations) => validPlan({
        featureGraph: featureGraph([feature]), operationGraph: operationGraph(operations),
        setupPlan: setupPlan([{ id: 'setup-1', operationIds: operations.map(item => item.id) }])
    });

    const external = { id: 'external', kind: 'external_thread', threadDesignation: 'M14 x 1',
        nominalDiameterMm: 14, pitchMm: 1, majorDiameterPreparationRequired: true,
        leadInChamferRequired: true, dimensions: { depthMm: 18, includedAngleDegrees: 90 },
        primaryFaceIds: ['face-1'], secondaryFeatureIds: [] };
    const externalThreadOnly = op('thread', 'external', 'thread_milling', 'thread', 'thread_mill', [],
        { threadDesignation: 'M14 x 1', nominalDiameterMm: 14, pitchMm: 1,
            majorDiameterPreparationRequired: true, leadInChamferRequired: true });
    assertCode('broken_predecessor', () => c.CncPlanContracts.validatePlan(
        planFor(external, [externalThreadOnly])));

    const m6 = { id: 'm6', kind: 'internal_thread', threadDesignation: 'M6 x 1',
        nominalDiameterMm: 6, pitchMm: 1, pilotDiameterMm: 5,
        primaryFaceIds: ['face-1'], secondaryFeatureIds: [] };
    const m6ThreadMill = op('thread', 'm6', 'thread_milling', 'thread', 'thread_mill', [],
        { threadDesignation: 'M6 x 1', nominalDiameterMm: 6, pitchMm: 1 });
    assertCode('broken_feature_reference', () => c.CncPlanContracts.validatePlan(
        planFor(m6, [m6ThreadMill])));

    const m16 = { id: 'm16', kind: 'internal_thread', threadDesignation: 'M16 x 2',
        nominalDiameterMm: 16, pitchMm: 2, pilotDiameterMm: 14,
        primaryFaceIds: ['face-1'], secondaryFeatureIds: [] };
    const m16ThreadOnly = op('thread', 'm16', 'thread_milling', 'thread', 'thread_mill', [],
        { threadDesignation: 'M16 x 2', nominalDiameterMm: 16, pitchMm: 2 });
    assertCode('broken_predecessor', () => c.CncPlanContracts.validatePlan(
        planFor(m16, [m16ThreadOnly])));

    const m16Spot = op('m16-spot', 'm16', 'spot_drilling', 'spot', 'spot_drill', []);
    const m16Bore = op('m16-bore', 'm16', 'bore_preparation', 'drill', 'flat_end_mill', ['m16-spot']);
    const m16ThreadExtra = { ...m16ThreadOnly, predecessors: ['m16-bore', 'm16-spot'] };
    assertCode('broken_predecessor', () => c.CncPlanContracts.validatePlan(
        planFor(m16, [m16Spot, m16Bore, m16ThreadExtra])));

    const spot = op('spot', 'm6', 'spot_drilling', 'spot', 'spot_drill', []);
    const drill = op('drill', 'm6', 'drilling', 'drill', 'hss_drill', ['spot']);
    const tap = op('tap', 'm6', 'tapping', 'thread', 'tap', ['drill'],
        { threadDesignation: 'M6 x 1', nominalDiameterMm: 6, pitchMm: 1 });
    const foreignFeature = { id: 'hole', kind: 'hole', primaryFaceIds: ['face-2'], secondaryFeatureIds: [] };
    const foreignSpot = op('foreign-spot', 'hole', 'spot_drilling', 'spot', 'spot_drill', []);
    const foreignPlan = validPlan({ featureGraph: featureGraph([m6, foreignFeature]),
        operationGraph: operationGraph([{ ...spot, predecessors: ['foreign-spot'] }, drill, tap, foreignSpot]),
        setupPlan: setupPlan([{ id: 'setup-1', operationIds: ['spot', 'drill', 'tap', 'foreign-spot'] }]) });
    assertCode('broken_predecessor', () => c.CncPlanContracts.validatePlan(foreignPlan));
    assertCode('broken_predecessor', () => c.CncPlanContracts.validatePlan(
        planFor(m6, [spot, drill, { ...tap, predecessors: ['drill', 'drill'] }])));
    assertCode('broken_feature_reference', () => c.CncPlanContracts.validatePlan(
        planFor(m6, [{ ...spot, phase: 'thread' }, drill, tap])));
    const valid = planFor(m6, [spot, drill, tap]);
    valid.planHash = await c.CncPlanContracts.hash(valid);
    await c.CncPlanContracts.validatePlan(valid);
});

test('final plan rejects extra dependencies in prismatic bulk and rest chains', () => {
    const c = runtime('cnc-plan-contracts');
    const feature = { id: 'slot', kind: 'slot', dimensions: { widthMm: 8, depthMm: 4,
        internalCornerRadiusMm: 2 }, primaryFaceIds: ['face-1'], secondaryFeatureIds: [] };
    const operation = (id, kind, phase, basis, predecessors) => ({ id, featureId: 'slot', kind,
        phase, toolClass: 'flat_end_mill', predecessors, accessAxis: { x: 0, y: 0, z: 1 },
        toolConstraints: { toolClass: 'flat_end_mill', constraintBasis: basis,
            maximumDiameterMm: basis === 'feature_corridor' ? 6 : 4,
            minimumCutLengthMm: 4, minimumReachMm: 4 },
        provenance: { sourceContract: 'ManufacturingFeatureGraph.v1', featureId: 'slot' } });
    const operations = [
        operation('rough-bulk', 'roughing', 'rough', 'feature_corridor', []),
        operation('rough-rest', 'roughing', 'rough', 'proven_internal_corner', ['rough-bulk']),
        operation('finish-bulk', 'finishing', 'finish', 'feature_corridor', ['rough-rest', 'rough-bulk']),
        operation('finish-rest', 'finishing', 'finish', 'proven_internal_corner', ['finish-bulk'])
    ];
    assertCode('broken_predecessor', () => c.CncPlanContracts.validatePlan(validPlan({
        featureGraph: featureGraph([feature]), operationGraph: operationGraph(operations),
        setupPlan: setupPlan([{ id: 'setup-1', operationIds: operations.map(item => item.id) }])
    })));
});

test('zero-operation features require a canonical feature-specific unresolved reason', () => {
    const c = runtime('cnc-plan-contracts');
    const slot = { id: 'slot-1', kind: 'slot', dimensions: { widthMm: 8, depthMm: 4 },
        primaryFaceIds: ['face-1'], secondaryFeatureIds: [] };
    const rejects = (feature, featureId, reason) => {
        const graph = operationGraph([]);
        graph.unresolved = [{ featureId, reason, required: true }];
        assertCode('broken_feature_reference', () =>
            c.CncPlanContracts.validateOperationGraph(graph, featureGraph([feature])));
    };

    rejects(slot, 'slot-1', 'anything-goes');
    rejects(slot, 'another-feature', 'unsupported_prismatic_tool');
    rejects(slot, 'slot-1', 'prismatic_envelope_required');
    rejects(slot, 'slot-1', 'prismatic_rest_envelope_required');
    rejects({ id: 'slot-1', kind: 'internal_thread', threadDesignation: 'M6 x 1',
        nominalDiameterMm: 6, pitchMm: 1, pilotDiameterMm: 5,
        primaryFaceIds: ['face-1'], secondaryFeatureIds: [] },
    'slot-1', 'unsupported_ball_end_mill');

    const missingEnvelope = { ...slot, dimensions: {} };
    const missingEnvelopeGraph = operationGraph([]);
    missingEnvelopeGraph.unresolved = [{ featureId: 'slot-1',
        reason: 'prismatic_envelope_required', required: true }];
    c.CncPlanContracts.validateOperationGraph(missingEnvelopeGraph,
        featureGraph([missingEnvelope]));

    const missingRestEnvelope = { ...slot, restMaterialRequired: true };
    const missingRestGraph = operationGraph([]);
    missingRestGraph.unresolved = [{ featureId: 'slot-1',
        reason: 'prismatic_rest_envelope_required', required: true }];
    c.CncPlanContracts.validateOperationGraph(missingRestGraph,
        featureGraph([missingRestEnvelope]));

    const explicitlyUnresolved = { id: 'slot-1', kind: 'unresolved',
        unresolvedReason: 'unresolved_thread_geometry', primaryFaceIds: [],
        secondaryFeatureIds: [] };
    const explicitlyUnresolvedGraph = operationGraph([]);
    explicitlyUnresolvedGraph.unresolved = [{ featureId: 'slot-1',
        reason: 'unresolved_thread_geometry', required: true }];
    c.CncPlanContracts.validateOperationGraph(explicitlyUnresolvedGraph,
        featureGraph([explicitlyUnresolved]));
});

test('contract identifiers reject whitespace-only text', () => {
    const c = runtime('cnc-plan-contracts');
    const blankPrimary = featureGraph([{ id: 'slot-1', kind: 'slot',
        dimensions: { widthMm: 8, depthMm: 4 }, primaryFaceIds: ['   '],
        secondaryFeatureIds: [] }]);
    assertCode('broken_feature_reference', () => c.CncPlanContracts.validateFeatureGraph(blankPrimary));

    const blankFeature = featureGraph([{ id: '   ', kind: 'unresolved', primaryFaceIds: [],
        secondaryFeatureIds: [] }]);
    assertCode('broken_feature_reference', () => c.CncPlanContracts.validateFeatureGraph(blankFeature));

    const blankOperation = operationGraph();
    blankOperation.operations[0].id = '\t';
    assertCode('broken_feature_reference', () =>
        c.CncPlanContracts.validateOperationGraph(blankOperation, featureGraph()));
    assertCode('broken_feature_reference', () => c.CncPlanContracts.validateOperationGraph(
        operationGraph([{ id: '  ', featureId: '\t', kind: 'drilling', phase: 'drill',
            toolClass: 'hss_drill', toolConstraints: { toolClass: 'hss_drill' },
            predecessors: [] }])));

    const blankSetup = validPlan();
    blankSetup.setupPlan.setups[0].id = '  ';
    assertCode('broken_feature_reference', () => c.CncPlanContracts.validatePlan(blankSetup));
});
