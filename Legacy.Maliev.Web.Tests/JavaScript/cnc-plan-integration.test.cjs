const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { webcrypto } = require('node:crypto');

const root = path.resolve(__dirname,
    '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation');

function plain(value) {
    return JSON.parse(JSON.stringify(value));
}

function loadWorker() {
    const messages = [];
    const context = vm.createContext({ console, TextEncoder, crypto: webcrypto });
    context.self = context;
    context.window = context;
    context.location = { search: '' };
    context.postMessage = message => messages.push(plain(message));
    context.importScripts = (...files) => files.forEach(file => vm.runInContext(
        fs.readFileSync(path.join(root, path.basename(file)), 'utf8'), context,
        { filename: file }));
    vm.runInContext(fs.readFileSync(path.join(root, 'cnc-quotation.worker.js'), 'utf8'),
        context, { filename: 'cnc-quotation.worker.js' });
    return { context, messages };
}

function request(material = '6061') {
    const axis = { x: 0, y: 0, z: 1 };
    const feature = { id: 'datum', bodyId: 'body', kind: 'datum',
        primaryFaceIds: ['face'], secondaryFeatureIds: [], accessAxes: [axis],
        evidenceRefs: ['face'] };
    return {
        analysisRevision: 'analysis-1', geometryRevision: 'geometry-1',
        requirementsRevision: 'requirements-1', selectedMaterial: material,
        alloy: material, quantity: 1, includeShipping: true,
        geometry: { geometryRevision: 'geometry-1', orientedSizeMm: { x: 20, y: 20, z: 10 },
            partVolumeMm3: 2000, partSurfaceAreaMm2: 1600,
            cadTopology: { contract: 'CncCadTopology.v1', revision: 'geometry-1',
                sourceKind: 'brep', automaticPlanningEligible: true,
                bodies: [{ id: 'body' }], faces: [{ id: 'face', bodyId: 'body' }], edges: [] },
            manufacturingFeatureGraph: { contract: 'ManufacturingFeatureGraph.v1',
                topologyRevision: 'geometry-1', features: [feature],
                faceOwners: { face: 'datum' }, machinableFaceIds: ['face'], unresolved: [] } },
        setupStock: { stateId: 'stock-in', states: [{ id: 'stock-in' }] },
        fixtureCatalog: { contract: 'CncFixtureCatalog.v1', candidates: [{ fixtureId: 'fixture',
            fixtureCapability: { accessAxes: [axis], obstacles: [] } }] },
        validationStock: { contract: 'CncValidationStock.v1' }
    };
}

function installSuccessfulPipeline(context, counters) {
    context.CncPlanContracts = { ...context.CncPlanContracts, validatePlan: async plan => plan,
        canonicalize: value => plain(value) };
    context.CncProcessCompiler = { compile(featureGraph) {
        counters.compiles++;
        return { contract: 'MachiningOperationGraph.v1',
            topologyRevision: featureGraph.topologyRevision,
            toolLibraryVersion: context.CncToolLibrary.version,
            operations: [{ id: 'face-op', featureId: 'datum', kind: 'facing', phase: 'rough',
                toolClass: 'face_mill', toolId: 'face-40', toolConstraints: {},
                accessAxis: { x: 0, y: 0, z: 1 }, predecessors: [],
                provenance: { sourceContract: 'ManufacturingFeatureGraph.v1', featureId: 'datum' } }],
            unresolved: [] };
    } };
    context.CncSetupPlanner = { plan(input) {
        counters.setupPlans++;
        return { contract: 'SetupPlan.v1', geometryRevision: input.operationGraph.topologyRevision,
            setups: [{ id: 'setup', fixtureId: 'fixture', operationIds: ['face-op'], handlingMinutes: 2,
                machiningMinutes: 3, orientation: { axis: { x: 0, y: 0, z: 1 } } }], operationAssignments: { 'face-op': 'setup' },
            inputStockState: 'stock-in', outputStockState: 'stock-out' };
    } };
    context.CncManufacturingEvidence = {
        prepare() {
            return { setupStock: { stateId: 'stock-in', states: [{ id: 'stock-in' }] },
                fixtureCatalog: { contract: 'CncFixtureCatalog.v2', version: 'test-fixtures-v2',
                    fixtures: [{ id: 'fixture' }], candidates: [{ fixtureId: 'fixture',
                        fixtureCapability: { catalogVersion: 'test-fixtures-v2', fixtureId: 'fixture',
                            maximumToolReachMm: 100, accessAxes: [{ x: 0, y: 0, z: 1 }], obstacles: [] } }] } };
        },
        complete() { return { contract: 'CncValidationStock.v2' }; }
    };
    context.CncPlanning = { validateManufacturingPlan: async input => {
        counters.planValidations++;
        return { valid: true, reviewReasons: [], plan: {
            contract: 'ValidatedManufacturingPlan.v1', geometryRevision: input.topology.revision,
            requirementsRevision: input.requirementsRevision,
            plannerVersion: 'cnc-feature-planner-v3',
            toolLibraryVersion: context.CncToolLibrary.version,
            topology: plain(input.topology), featureGraph: plain(input.featureGraph),
            operationGraph: plain(input.operationGraph), setupPlan: plain(input.setupPlan),
            validationResults: [], unresolvedReasons: [], planHash: 'plan-hash-1'
        } };
    }, commercializeValidatedPlan(input) {
        return { contract: input.plan.contract, planHash: input.plan.planHash,
            geometryRevision: input.plan.geometryRevision,
            operations: input.plan.operationGraph.operations,
            setups: input.plan.setupPlan.setups,
            batchMinutes: 5, cycleMinutesPerPart: input.material === '6061' ? 10 : 20,
            totalMinutesPerPart: input.material === '6061' ? 15 : 25,
            toolingAllowanceBeforeVat: 0, workholdingAllowanceBeforeVat: 0,
            inspectionAllowanceBeforeVat: 0, confidence: 'High', reviewReasons: [] };
    } };
    context.CncQuoteEngine = { quote(input) {
        counters.quotes++;
        return { estimatedPriceBeforeVat: input.material === '6061' ? 1000 : 2000,
            currency: 'THB' };
    } };
}

test('quotation worker exposes no legacy cluster planning surface', () => {
    const { context } = loadWorker();
    assert.equal(context.CncPlanningDiagnostics, undefined);
    assert.equal(context.CncPlanning.plan, undefined);
    assert.equal(context.CncPlanning.planFixture, undefined);
    assert.equal(typeof context.CncPlanning.validateManufacturingPlan, 'function');
});

test('material changes reuse one validated manufacturing plan', async () => {
    const worker = loadWorker();
    const counters = { compiles: 0, setupPlans: 0, planValidations: 0, quotes: 0 };
    installSuccessfulPipeline(worker.context, counters);
    const aluminum = await worker.context.estimate(request('6061'), () => {});
    const steel = await worker.context.estimate(request('SUS304'), () => {});
    assert.equal(aluminum.status, 'finalized', JSON.stringify(aluminum));
    assert.equal(steel.status, 'finalized', JSON.stringify(steel));
    assert.equal(counters.planValidations, 1);
    assert.equal(counters.compiles, 1);
    assert.equal(counters.setupPlans, 1);
    assert.equal(aluminum.validatedPlan.planHash, steel.validatedPlan.planHash);
    assert.notEqual(aluminum.quote.estimatedPriceBeforeVat,
        steel.quote.estimatedPriceBeforeVat);
    const aluminumAgain = await worker.context.estimate(request('6061'), () => {});
    assert.equal(aluminumAgain.quote.estimatedPriceBeforeVat,
        aluminum.quote.estimatedPriceBeforeVat);
    assert.equal(counters.quotes, 2);
});
test('real imported M14 worker repeats and switches material with one authenticated plan validation', async () => {
    const { context: c } = loadWorker();
    vm.runInContext(fs.readFileSync(path.join(root, 'cnc-cad-surfaces.worker.js'), 'utf8'), c);
    const occtRoot = path.resolve(root, '../../../../lib/occt');
    const occt = await require(path.join(occtRoot, 'occt-import-js.js'))({
        wasmBinary: fs.readFileSync(path.join(occtRoot, 'occt-import-js.wasm')) });
    const source = fs.readFileSync(path.resolve(__dirname, '../TestAssets/Cnc/axial-threaded-part.step'));
    const model = occt.ReadStepFile(source, { linearDeflection: 0.1 });
    assert.equal(model.success, true);
    const topology = await c.CncTopology.build({ meshes: model.meshes, validationMeshes: model.meshes,
        validationDeflectionMm: 0.1, sourceFormat: 'step', bodyCount: model.meshes.length,
        analyticSurfaces: c.CncCadSurfaces.parseStep(source.toString()) });
    const featureGraph = c.CncFeatureGraph.build(topology, { modelScaleMm: 40 });
    const input = { analysisRevision: 'real-cache-1', geometryRevision: topology.revision,
        requirementsRevision: 'real-cache-requirements', alloy: '6061', selectedMaterial: '6061',
        quantity: 1, includeShipping: true, clampBorderMm: 25, topology, featureGraph,
        geometry: { geometryRevision: topology.revision, orientedSizeMm: { x: 27, y: 26.97, z: 39 },
            partVolumeMm3: 5350, partSurfaceAreaMm2: 3000 } };
    const first = await c.estimate(input, () => {});
    assert.equal(first.status, 'finalized', JSON.stringify(first.reviewReasons));
    const tampered = plain(input);
    tampered.topology.validationMesh.vertices[0].x += 1;
    const rejected = await c.estimate(tampered, () => {});
    assert.equal(rejected.status, 'review_required');
    assert.equal(rejected.quote, null);
    assert.ok(rejected.reviewReasons.includes('validation_mesh_mismatch'), JSON.stringify(rejected.reviewReasons));
    const second = await c.estimate({ ...input, analysisRevision: 'real-cache-2' }, () => {});
    assert.equal(second.status, 'finalized', JSON.stringify(second.reviewReasons));
    const steel = await c.estimate({ ...input, analysisRevision: 'real-cache-3', alloy: 'SUS304', selectedMaterial: 'SUS304' }, () => {});
    assert.equal(steel.status, 'finalized', JSON.stringify(steel.reviewReasons));
    assert.equal(typeof first.validatedPlan.planHash, 'string');
    assert.equal(first.validatedPlan.planHash, second.validatedPlan.planHash);
    assert.equal(first.validatedPlan.planHash, steel.validatedPlan.planHash);
    assert.equal(c.counters.planValidations, 1);
    assert.equal(c.counters.quotes, 2);
    assert.notEqual(first.quote.estimatedPriceBeforeVat, steel.quote.estimatedPriceBeforeVat);
});

test('requirements-only changes invalidate the manufacturing plan and quote caches', async () => {
    const worker = loadWorker();
    const counters = { compiles: 0, setupPlans: 0, planValidations: 0, quotes: 0 };
    installSuccessfulPipeline(worker.context, counters);
    const first = request();
    const second = request();
    second.requirementsRevision = 'requirements-2';
    const firstResult = await worker.context.estimate(first, () => {});
    const secondResult = await worker.context.estimate(second, () => {});
    assert.equal(firstResult.status, 'finalized', JSON.stringify(firstResult));
    assert.equal(secondResult.status, 'finalized', JSON.stringify(secondResult));
    assert.equal(firstResult.validatedPlan.requirementsRevision, 'requirements-1');
    assert.equal(secondResult.validatedPlan.requirementsRevision, 'requirements-2');
    assert.equal(counters.planValidations, 2);
    assert.equal(counters.quotes, 2);
});

test('same geometry revision with different canonical topology cannot reuse a cached plan', async () => {
    const worker = loadWorker();
    const counters = { compiles: 0, setupPlans: 0, planValidations: 0, quotes: 0 };
    installSuccessfulPipeline(worker.context, counters);
    const first = request();
    assert.equal((await worker.context.estimate(first, () => {})).status, 'finalized');
    const changed = request();
    changed.geometry.cadTopology.faces[0].surfaceType = 'changed-without-revision';
    const result = await worker.context.estimate(changed, () => {});
    assert.equal(result.status, 'review_required');
    assert.equal(result.quote, null);
    assert.deepEqual(plain(result.reviewReasons), ['cnc_cached_plan_evidence_mismatch']);
    assert.equal(counters.planValidations, 1);
    assert.equal(counters.quotes, 1);
});

test('unresolved recognition evidence is preserved exactly and emits no quote', async () => {
    const worker = loadWorker();
    const source = request();
    source.geometry.manufacturingFeatureGraph.unresolved = [
        { featureId: null, reason: 'ambiguous_thread_evidence', required: true }
    ];
    worker.context.CncProcessCompiler = { compile(graph) {
        return { contract: 'MachiningOperationGraph.v1', topologyRevision: graph.topologyRevision,
            toolLibraryVersion: worker.context.CncToolLibrary.version, operations: [],
            unresolved: plain(graph.unresolved) };
    } };
    let quoted = false;
    worker.context.CncQuoteEngine = { quote() { quoted = true; } };
    const result = await worker.context.estimate(source, () => {});
    assert.equal(result.status, 'review_required');
    assert.equal(result.quote, null);
    assert.equal(quoted, false);
    assert.deepEqual(plain(result.reviewReasons), ['ambiguous_thread_evidence']);
});

test('inherited unresolved evidence is rejected when omitted duplicated conflicting or operation-coexisting', async () => {
    const worker = loadWorker();
    const featureGraph = request().geometry.manufacturingFeatureGraph;
    featureGraph.unresolved = [{ featureId: 'datum', reason: 'recognition_gap', required: false }];
    const operation = { id: 'op', featureId: 'datum' };
    for (const unresolved of [
        [],
        [featureGraph.unresolved[0], featureGraph.unresolved[0]],
        [{ featureId: 'datum', reason: 'different', required: false }],
        [{ featureId: 'datum', reason: 'recognition_gap', required: true }],
        [featureGraph.unresolved[0]]
    ]) {
        const operations = unresolved.length === 1
            && unresolved[0].reason === 'recognition_gap' ? [operation] : [];
        assert.throws(() => worker.context.CncQuotationWorker.assertInheritedRecognitionReasons(
            featureGraph, { unresolved, operations }),
        error => error && error.code === 'cnc_inherited_recognition_mismatch');
    }
    const duplicatedSource = plain(featureGraph);
    duplicatedSource.unresolved.push(plain(duplicatedSource.unresolved[0]));
    assert.throws(() => worker.context.CncQuotationWorker.assertInheritedRecognitionReasons(
        duplicatedSource, { unresolved: plain(duplicatedSource.unresolved), operations: [] }),
    error => error && error.code === 'cnc_inherited_recognition_mismatch');
    assert.throws(() => worker.context.CncQuotationWorker.assertInheritedRecognitionReasons(
        featureGraph, { unresolved: [{ featureId: 'other-feature',
            reason: 'recognition_gap', required: false }], operations: [] }),
    error => error && error.code === 'cnc_inherited_recognition_mismatch');
});

test('final validation cannot omit inherited non-required recognition evidence', async () => {
    const worker = loadWorker();
    const source = request();
    source.geometry.manufacturingFeatureGraph.unresolved = [
        { featureId: null, reason: 'diagnostic_recognition_gap', required: false }
    ];
    const counters = { compiles: 0, setupPlans: 0, planValidations: 0, quotes: 0 };
    installSuccessfulPipeline(worker.context, counters);
    const originalCompile = worker.context.CncProcessCompiler.compile;
    worker.context.CncProcessCompiler.compile = graph => {
        const result = originalCompile(graph);
        result.unresolved = plain(graph.unresolved);
        return result;
    };
    const originalValidate = worker.context.CncPlanning.validateManufacturingPlan;
    worker.context.CncPlanning.validateManufacturingPlan = async input => {
        const result = await originalValidate(input);
        result.plan.featureGraph.unresolved = [];
        return result;
    };
    const result = await worker.context.estimate(source, () => {});
    assert.equal(result.status, 'review_required');
    assert.equal(result.quote, null);
    assert.deepEqual(plain(result.reviewReasons), ['cnc_inherited_recognition_mismatch']);
    assert.equal(counters.quotes, 0);
});

test('progress messages expose stages only and the worker posts one finalized envelope', async () => {
    const worker = loadWorker();
    installSuccessfulPipeline(worker.context,
        { compiles: 0, setupPlans: 0, planValidations: 0, quotes: 0 });
    await worker.context.onmessage({ data: { requestId: 'request-1', progressive: true,
        request: request() } });
    for (const message of worker.messages.filter(item => item.progress)) {
        assert.deepEqual(Object.keys(message).sort(),
            ['analysisRevision', 'progress', 'requestId', 'stage', 'success']);
        assert.equal(message.analysisRevision, 'analysis-1');
        assert.equal(Object.keys(message).some(key =>
            /price|quote|stock|setup|minute|amount|value/i.test(key)), false);
    }
    assert.equal(worker.messages.at(-1).analysisRevision, 'analysis-1');
    assert.equal(worker.messages.at(-1).estimate.analysisRevision, 'analysis-1');
    assert.equal(worker.messages.at(-1).estimate.status, 'finalized');
    assert.equal(worker.messages.at(-1).estimate.quote.estimatedPriceBeforeVat, 1000);
});

test('review and error worker messages preserve analysis revision and reject missing revision', async () => {
    const worker = loadWorker();
    const stale = request();
    stale.analysisRevision = 'analysis-review';
    stale.geometryRevision = 'stale';
    await worker.context.onmessage({ data: { requestId: 'review-request', request: stale } });
    const review = worker.messages.at(-1);
    assert.equal(review.analysisRevision, 'analysis-review');
    assert.equal(review.estimate.analysisRevision, 'analysis-review');
    assert.equal(review.estimate.status, 'review_required');

    const missing = request();
    delete missing.analysisRevision;
    await worker.context.onmessage({ data: { requestId: 'error-request', request: missing } });
    const error = worker.messages.at(-1);
    assert.equal(Object.prototype.hasOwnProperty.call(error, 'analysisRevision'), true);
    assert.equal(error.analysisRevision, null);
    assert.equal(error.success, false);
    assert.equal(error.errorCode, 'analysis_revision_required');
});

test('concurrent stale and current worker messages remain isolated by analysis revision', async () => {
    const worker = loadWorker();
    installSuccessfulPipeline(worker.context,
        { compiles: 0, setupPlans: 0, planValidations: 0, quotes: 0 });
    const stale = request();
    stale.analysisRevision = 'analysis-old';
    stale.geometryRevision = 'stale';
    const current = request();
    current.analysisRevision = 'analysis-current';
    await Promise.all([
        worker.context.onmessage({ data: { requestId: 'old', progressive: true, request: stale } }),
        worker.context.onmessage({ data: { requestId: 'current', progressive: true, request: current } })
    ]);
    const oldMessages = worker.messages.filter(message => message.requestId === 'old');
    const currentMessages = worker.messages.filter(message => message.requestId === 'current');
    assert.equal(oldMessages.length > 0, true);
    assert.equal(currentMessages.length > 0, true);
    assert.equal(oldMessages.every(message => message.analysisRevision === 'analysis-old'), true);
    assert.equal(currentMessages.every(message => message.analysisRevision === 'analysis-current'), true);
});

test('stale geometry revisions fail closed before validation or pricing', async () => {
    const worker = loadWorker();
    const source = request();
    source.geometryRevision = 'stale';
    const result = await worker.context.estimate(source, () => {});
    assert.equal(result.status, 'review_required');
    assert.equal(result.quote, null);
    assert.deepEqual(plain(result.reviewReasons), ['revision_mismatch']);
});

test('legacy caller-authored occupancy and fixture certificates fail closed without a quote', async () => {
    const worker = loadWorker();
    const axis = { x: 0, y: 0, z: 1 };
    const source = request();
    source.requirementsRevision = 'real-pipeline-requirements';
    source.geometry.cadTopology.faces[0].validationVolume = {
        minimum: { x: 0, y: 0, z: 1 }, maximum: { x: 2, y: 2, z: 2 }
    };
    const operationId = 'legacy-operation';
    const initial = { contract: 'CncSparseOccupancy.v1', version: 1,
        resolutionMm: 1, toleranceMm: 0.05, origin: { x: 0, y: 0, z: 0 },
        dimensions: { x: 2, y: 2, z: 2 }, occupiedRuns: [[0, 8]] };
    const final = { ...initial, origin: { ...initial.origin },
        dimensions: { ...initial.dimensions }, occupiedRuns: [[0, 4]] };
    source.validationStock = { contract: 'CncValidationStock.v1', occupancyVersion: 1,
        resolutionMm: 1, toleranceMm: 0.05,
        states: [{ id: 'stock-in', occupancy: initial },
            { id: 'stock-out', occupancy: final }],
        operationSweeps: [{ contract: 'CncOperationSweep.v1',
            geometryRevision: 'geometry-1', occupancyVersion: 1,
            resolutionMm: 1, toleranceMm: 0.05, operationId,
            featureId: 'datum', setupId: 'legacy-setup',
            inputStockState: 'stock-in', outputStockState: 'stock-out',
            topologyFaceIds: ['face'], removedRuns: [[4, 4]] }]
    };
    const result = await worker.context.estimate(source, () => {});
    assert.equal(result.status, 'review_required');
    assert.equal(result.quote, null);
    assert.deepEqual(plain(result.reviewReasons), ['manufacturing_validation_evidence_required']);
});
