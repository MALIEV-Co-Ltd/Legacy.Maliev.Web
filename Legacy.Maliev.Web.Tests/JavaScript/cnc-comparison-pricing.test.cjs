const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { webcrypto } = require('node:crypto');

const root = path.resolve(__dirname,
    '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation');

function runtime() {
    const context = vm.createContext({ console, TextEncoder, crypto: webcrypto });
    context.self = context;
    context.window = context;
    for (const name of ['cnc-plan-contracts', 'cnc-quotation-config', 'cnc-material-catalog', 'cnc-tool-library',
        'cnc-stock', 'cnc-reach', 'cnc-fixture-clearance', 'cnc-machine-capability',
        'cnc-setup-planner', 'cnc-planning', 'cnc-engine']) {
        vm.runInContext(fs.readFileSync(path.join(root, name + '.js'), 'utf8'), context,
            { filename: name + '.js' });
    }
    return context;
}

function geometry() {
    return { geometryRevision: 'geometry-1', orientedSizeMm: { x: 40, y: 50, z: 20 },
        partVolumeMm3: 20000, partSurfaceAreaMm2: 10100 };
}

async function validatedPlan(context) {
    const axis = { x: 0, y: 0, z: 1 };
    const plan = { contract: 'ValidatedManufacturingPlan.v1',
        geometryRevision: 'geometry-1', requirementsRevision: 'requirements-1',
        plannerVersion: 'cnc-feature-planner-v3',
        toolLibraryVersion: context.CncToolLibrary.version,
        topology: { contract: 'CncCadTopology.v1', revision: 'geometry-1',
            bodies: [{ id: 'body' }], faces: [{ id: 'face', bodyId: 'body' }], edges: [] },
        featureGraph: { contract: 'ManufacturingFeatureGraph.v1',
            topologyRevision: 'geometry-1', features: [{ id: 'datum', bodyId: 'body',
                kind: 'datum', primaryFaceIds: ['face'], secondaryFeatureIds: [],
                accessAxes: [axis], evidenceRefs: ['face'] }],
            faceOwners: { face: 'datum' }, machinableFaceIds: ['face'], unresolved: [] },
        operationGraph: { contract: 'MachiningOperationGraph.v1',
            topologyRevision: 'geometry-1', toolLibraryVersion: context.CncToolLibrary.version,
            operations: [{ id: 'face-op', featureId: 'datum', kind: 'facing',
                phase: 'rough', toolClass: 'face_mill', toolId: 'face-40',
                toolConstraints: {}, accessAxis: axis, predecessors: [],
                provenance: { sourceContract: 'ManufacturingFeatureGraph.v1',
                    featureId: 'datum' } }], unresolved: [] },
        setupPlan: { contract: 'SetupPlan.v1', geometryRevision: 'geometry-1',
            setups: [{ id: 'setup', fixtureId: 'fixture', operationIds: ['face-op'],
                handlingMinutes: 2, machiningMinutes: 0 }],
            operationAssignments: { 'face-op': 'setup' } },
        validationResults: [], unresolvedReasons: [] };
    plan.planHash = await context.CncPlanContracts.hash(plan);
    return plan;
}

async function quote(context, alloy) {
    const shape = geometry();
    const plan = await validatedPlan(context);
    const stock = context.CncStock.selectStock({ alloy, quantity: 1,
        partSizeMm: shape.orientedSizeMm, partVolumeMm3: shape.partVolumeMm3,
        clampBorderMm: 25, includeShipping: true });
    const commercialPlan = context.CncPlanning.commercializeValidatedPlan({
        plan, material: alloy, geometry: shape, stock
    });
    return context.CncQuoteEngine.quote({ quantity: 1, material: alloy,
        geometry: shape, requirementsRevision: 'requirements-1', stock, plan, commercialPlan });
}

test('material comparisons reuse plan identity and vary only commercial pricing', async () => {
    const context = runtime();
    const aluminum = await quote(context, '6061');
    const steel = await quote(context, 'SUS304');
    assert.notEqual(aluminum.estimatedPriceBeforeVat, steel.estimatedPriceBeforeVat);
    assert.equal(aluminum.manufacturingEvidence.setupCount,
        steel.manufacturingEvidence.setupCount);
    assert.deepEqual(Array.from(aluminum.manufacturingEvidence.toolFamilies),
        Array.from(steel.manufacturingEvidence.toolFamilies));
});

test('quote engine rejects legacy stale or forged manufacturing plans', async () => {
    const context = runtime();
    const shape = geometry();
    const stock = context.CncStock.selectStock({ alloy: '6061', quantity: 1,
        partSizeMm: shape.orientedSizeMm, partVolumeMm3: shape.partVolumeMm3,
        clampBorderMm: 25, includeShipping: true });
    await assert.rejects(() => context.CncQuoteEngine.quote({ quantity: 1, material: '6061',
        geometry: shape, stock, plan: { setups: [] }, commercialPlan: {} }),
    error => error && error.code === 'cnc_validated_plan_required');
    const plan = await validatedPlan(context);
    plan.geometryRevision = 'stale';
    await assert.rejects(() => context.CncQuoteEngine.quote({ quantity: 1, material: '6061',
        geometry: shape, stock, plan, commercialPlan: {} }),
    error => error && error.code === 'cnc_validated_plan_required');
    const forged = await validatedPlan(context);
    forged.featureGraph.features[0].evidenceRefs = ['forged-after-sealing'];
    const forgedCommercial = context.CncPlanning.commercializeValidatedPlan({
        plan: forged, material: '6061', geometry: shape, stock
    });
    await assert.rejects(() => context.CncQuoteEngine.quote({ quantity: 1,
        material: '6061', geometry: shape, requirementsRevision: 'requirements-1',
        stock, plan: forged, commercialPlan: forgedCommercial }),
    error => error && error.code === 'cnc_validated_plan_required');
});
