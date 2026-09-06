const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { webcrypto } = require('node:crypto');

const moduleRoot = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation');

function runtime() {
    const context = vm.createContext({ console, TextEncoder, crypto: webcrypto });
    context.self = context; context.window = context;
    for (const name of ['cnc-plan-contracts', 'cnc-quotation-config', 'cnc-material-catalog',
        'cnc-tool-library', 'cnc-spatial-field.worker', 'cnc-topology.worker', 'cnc-fixture-catalog', 'cnc-plan-validator.worker']) {
        vm.runInContext(fs.readFileSync(path.join(moduleRoot, name + '.js'), 'utf8'), context);
    }
    return context;
}

function cubeValidationMesh(minimum, maximum) {
    const vertices = [
        { x: minimum.x, y: minimum.y, z: minimum.z }, { x: maximum.x, y: minimum.y, z: minimum.z },
        { x: maximum.x, y: maximum.y, z: minimum.z }, { x: minimum.x, y: maximum.y, z: minimum.z },
        { x: minimum.x, y: minimum.y, z: maximum.z }, { x: maximum.x, y: minimum.y, z: maximum.z },
        { x: maximum.x, y: maximum.y, z: maximum.z }, { x: minimum.x, y: maximum.y, z: maximum.z }
    ];
    return { contract: 'CncBrepValidationMesh.v1', resolutionMm: 0.5, deflectionMm: 0.1,
        source: 'occt_brep_validation_tessellation_v1', bodyAssociations: [{ bodyId: 'body', validationMeshIndex: 0 }],
        watertight: true, vertices,
        triangles: [[0,2,1],[0,3,2],[4,5,6],[4,6,7],[0,1,5],[0,5,4],
            [3,7,6],[3,6,2],[0,4,7],[0,7,3],[1,2,6],[1,6,5]] };
}

async function sealedInput() {
    const c = runtime();
    const source = input();
    source.topology.validationMeshHash = await c.CncTopology.validationMeshHash(source.topology.validationMesh);
    source.topology.revision = await c.CncTopology.revisionHash(source.topology);
    const revision = source.topology.revision;
    source.featureGraph.topologyRevision = revision;
    source.operationGraph.topologyRevision = revision;
    source.setupPlan.geometryRevision = revision;
    source.stock.targetSolid.geometryRevision = revision;
    source.stock.operationPaths.forEach(path => { path.geometryRevision = revision; });
    return { c, source };
}

function input() {
    const axis = { x: 0, y: 0, z: 1 };
    const volume = { minimum: { x: 4, y: 4, z: 7.75 }, maximum: { x: 8, y: 8, z: 8.25 } };
    const loops = [{ vertices: [{ x: 4, y: 4, z: 8 }, { x: 8, y: 4, z: 8 },
        { x: 8, y: 8, z: 8 }, { x: 4, y: 8, z: 8 }] }];
    const feature = { id: 'datum', bodyId: 'body', kind: 'datum', primaryFaceIds: ['face'],
        secondaryFeatureIds: [], accessAxes: [axis], evidenceRefs: ['face'] };
    const operation = { id: 'face-op', featureId: feature.id, kind: 'facing', phase: 'rough',
        toolClass: 'face_mill', toolId: 'face-50', toolDiameterMm: 50,
        toolConstraints: { toolClass: 'face_mill', selectedToolId: 'face-50',
            maximumDiameterMm: 50, usableCutLengthMm: 4, reachMm: 35 },
        accessAxis: axis, predecessors: [], provenance: {
            sourceContract: 'ManufacturingFeatureGraph.v1', featureId: feature.id } };
    const setup = { id: 'setup-1', sequence: 1, orientation: { axis }, datumFaceIds: ['face'],
        clampFaceIds: ['face'], fixtureId: 'vise-100-standard', inputStockState: 'stock-0', outputStockState: 'stock-1',
        fixtureCapability: { catalogVersion: 'cnc-local-fixtures-2026-09-05-v2', fixtureId: 'vise-100-standard',
            maximumToolReachMm: 120, accessAxes: [axis], obstacles: [{ minimum: { x: 2, y: 2, z: -17.35 }, maximum: { x: 10, y: 10, z: 7.65 } }] }, operationIds: [operation.id] };
    return {
        topology: { contract: 'CncCadTopology.v1', revision: 'geometry-1', sourceKind: 'brep',
            automaticPlanningEligible: true, bodies: [{ id: 'body' }], faces: [{ id: 'face', bodyId: 'body',
                surface: { kind: 'plane', normal: axis }, loops, adjacentFaceIds: [], validationVolume: volume }], edges: [],
            validationMesh: cubeValidationMesh({ x: 4, y: 4, z: 4 }, { x: 8, y: 8, z: 8 }) },
        featureGraph: { contract: 'ManufacturingFeatureGraph.v1', topologyRevision: 'geometry-1',
            features: [feature], faceOwners: { face: feature.id }, machinableFaceIds: ['face'], unresolved: [] },
        operationGraph: { contract: 'MachiningOperationGraph.v1', topologyRevision: 'geometry-1',
            toolLibraryVersion: 'cnc-tools-hss-spotting-2026-09-03-r3', operations: [operation], unresolved: [] },
        setupPlan: { contract: 'SetupPlan.v1', geometryRevision: 'geometry-1', setups: [setup],
            operationAssignments: { [operation.id]: setup.id }, inputStockState: 'stock-0', outputStockState: 'stock-1' },
        stock: { contract: 'CncValidationStock.v2', occupancyVersion: 2, resolutionMm: 0.5, toleranceMm: 0.05,
            supplierBlank: { source: 'quoted_supplier_blank', shape: 'block', dimensionsMm: { x: 4, y: 4, z: 8 }, originMm: { x: 4, y: 4, z: 4 } },
            targetSolid: { geometryRevision: 'geometry-1', sourceKind: 'brep', bodyIds: ['body'],
                topologyFaceIds: ['face'], bounds: { minimum: { x: 4, y: 4, z: 4 }, maximum: { x: 8, y: 8, z: 8 } } },
            states: [{ id: 'stock-0' }, { id: 'stock-1' }],
            operationPaths: [{ contract: 'CncOperationSemanticPath.v1', geometryRevision: 'geometry-1',
                operationId: operation.id, featureId: feature.id, setupId: setup.id,
                inputStockState: 'stock-0', outputStockState: 'stock-1', toolId: operation.toolId,
                toolClass: operation.toolClass, semanticPath: { operationKind: 'facing', phase: 'rough',
                    accessAxis: axis, topologyFaceIds: ['face'], featureKind: 'datum',
                    featureBounds: { minimum: { x: 4, y: 4, z: 4 }, maximum: { x: 8, y: 8, z: 12 } },
                    removalPolicyVersion: 'cnc-semantic-removal-2026-09-05-v1', finishAllowanceMm: 0,
                    toolPath: { contract: 'CncCanonicalToolPath.v1', toolId: 'face-50',
                        cutterDiameterMm: 50, usableCutLengthMm: 4, reachMm: 35,
                        shankDiameterMm: 20, holderDiameterMm: 50 },
                    geometry: 'planar_facing', axisName: 'z', axisSign: 1, targetPlaneMm: 8,
                    planarRegions: [{ kind: 'polygon_loops', loops: [loops[0].vertices] }] } }] },
        requirementsRevision: 'requirements-1', toolLibraryVersion: 'cnc-tools-hss-spotting-2026-09-03-r3'
    };
}

test('validator seals a plan only after deriving positive removal from a semantic path', async () => {
    const { c, source } = await sealedInput();
    const result = await c.CncPlanValidator.validate(source);
    assert.equal(result.valid, true, JSON.stringify(result.reviewReasons));
    const evidence = result.plan.validationResults[0];
    assert.ok(evidence.removedCellCount > 0);
    assert.equal(evidence.toolId, 'face-50');
    assert.match(evidence.rawSweepChecksum, /^[0-9a-f]{8}$/);
    assert.match(evidence.boundaryContactMaskChecksum, /^[0-9a-f]{8}$/);
    assert.match(evidence.validatedRemovalMaskChecksum, /^[0-9a-f]{8}$/);
    assert.equal(evidence.validatedRemovalCellCount, evidence.removedCellCount);
    assert.ok(Object.isFrozen(evidence.validatedRemovalRuns));
});

test('validator rejects a catalog tool shorter than the operation cutting depth or reach', async () => {
    for (const requirement of ['minimumCutLengthMm', 'minimumReachMm']) {
        const { c, source } = await sealedInput();
        source.operationGraph.operations[0].toolConstraints[requirement] = 100;
        const result = await c.CncPlanValidator.validate(source);
        assert.equal(result.valid, false, requirement + ' must be checked against the catalog tool');
        assert.ok(result.reviewReasons.includes('tool_envelope_invalid'));
    }
});

test('validator fails closed when the operation semantic path is missing', async () => {
    const { c, source } = await sealedInput(); source.stock.operationPaths = [];
    const result = await c.CncPlanValidator.validate(source);
    assert.deepEqual(Array.from(result.reviewReasons), ['operation_path_invalid']);
});

test('validator rejects caller-authored topology face substitutions', async () => {
    const { c, source } = await sealedInput(); source.stock.operationPaths[0].semanticPath.topologyFaceIds = ['other'];
    const result = await c.CncPlanValidator.validate(source);
    assert.deepEqual(Array.from(result.reviewReasons), ['operation_path_invalid']);
});

test('validator rejects a caller-enlarged semantic sweep instead of trusting blank-wide bounds', async () => {
    const { c, source } = await sealedInput();
    source.stock.operationPaths[0].semanticPath.featureBounds = {
        minimum: { x: 0, y: 0, z: 0 }, maximum: { x: 12, y: 12, z: 12 }
    };
    const result = await c.CncPlanValidator.validate(source);
    assert.deepEqual(Array.from(result.reviewReasons), ['operation_path_invalid']);
});

test('validator rejects unquoted or mismatched supplier blank evidence', async () => {
    const { c, source } = await sealedInput(); source.stock.supplierBlank.source = 'caller_guess';
    const result = await c.CncPlanValidator.validate(source);
    assert.deepEqual(Array.from(result.reviewReasons), ['stock_evidence_invalid']);
});

test('validator rejects a fixture capability that cannot reach the selected tool envelope', async () => {
    const { c, source } = await sealedInput(); source.setupPlan.setups[0].fixtureCapability.maximumToolReachMm = 20;
    const result = await c.CncPlanValidator.validate(source);
    assert.deepEqual(Array.from(result.reviewReasons), ['fixture_validation_evidence_required']);
});

test('validator rejects a caller-invented fixture catalog version', async () => {
    const { c, source } = await sealedInput();
    source.setupPlan.setups[0].fixtureCapability.catalogVersion = 'caller-fixtures-v999';
    const result = await c.CncPlanValidator.validate(source);
    assert.deepEqual(Array.from(result.reviewReasons), ['fixture_validation_evidence_required']);
});

test('validator rejects caller-inflated fixture reach', async () => {
    const { c, source } = await sealedInput();
    source.setupPlan.setups[0].fixtureCapability.maximumToolReachMm = 10000;
    const result = await c.CncPlanValidator.validate(source);
    assert.deepEqual(Array.from(result.reviewReasons), ['fixture_validation_evidence_required']);
});

test('validator rejects omitted fixture obstacles', async () => {
    const { c, source } = await sealedInput();
    delete source.setupPlan.setups[0].fixtureCapability.obstacles;
    const result = await c.CncPlanValidator.validate(source);
    assert.deepEqual(Array.from(result.reviewReasons), ['fixture_validation_evidence_required']);
});

test('validator rejects a canonical sweep that would consume the independently classified target solid', async () => {
    const { c, source } = await sealedInput();
    source.topology.validationMesh = cubeValidationMesh({ x: 4, y: 4, z: 4 }, { x: 8, y: 8, z: 10 });
    source.stock.targetSolid.bounds.maximum.z = 10;
    source.topology.validationMeshHash = await c.CncTopology.validationMeshHash(source.topology.validationMesh);
    source.topology.revision = await c.CncTopology.revisionHash(source.topology);
    source.featureGraph.topologyRevision = source.topology.revision;
    source.operationGraph.topologyRevision = source.topology.revision;
    source.setupPlan.geometryRevision = source.topology.revision;
    source.stock.targetSolid.geometryRevision = source.topology.revision;
    source.stock.operationPaths[0].geometryRevision = source.topology.revision;
    const result = await c.CncPlanValidator.validate(source);
    assert.deepEqual(Array.from(result.reviewReasons), ['tool_target_collision']);
});

test('validator rejects a forged validation mesh even when the caller preserves the topology revision', async () => {
    const { c, source } = await sealedInput();
    source.topology.validationMesh.vertices[0].x += 0.5;
    const result = await c.CncPlanValidator.validate(source);
    assert.deepEqual(Array.from(result.reviewReasons), ['validation_mesh_mismatch']);
});

test('validator rejects terminal stock that still contains non-target material', async () => {
    const { c, source } = await sealedInput();
    source.stock.supplierBlank.dimensionsMm.x = 12;
    source.stock.supplierBlank.originMm.x = 0;
    source.stock.operationPaths[0].semanticPath.featureBounds.minimum.x = 0;
    source.stock.operationPaths[0].semanticPath.featureBounds.maximum.x = 12;
    const result = await c.CncPlanValidator.validate(source);
    assert.equal(result.valid, false);
    assert.deepEqual(Array.from(result.reviewReasons), ['terminal_residual_mismatch']);
});

test('validator rejects an operation whose claimed diameter does not match its catalog cutter', async () => {
    const { c, source } = await sealedInput();
    source.operationGraph.operations[0].toolDiameterMm = 12;
    const result = await c.CncPlanValidator.validate(source);
    assert.deepEqual(Array.from(result.reviewReasons), ['tool_envelope_invalid']);
});

test('thread milling sweep follows helix phase so pitch changes the removed cells', () => {
    const c = runtime();
    const dimensions = { x: 24, y: 24, z: 24 };
    const makeState = () => ({ origin: { x: -6, y: -6, z: 0 }, dimensions,
        resolutionMm: 0.5, toleranceMm: 0.05, totalCells: 24 ** 3,
        bits: new Uint32Array(Math.ceil((24 ** 3) / 32)).fill(0xffffffff) });
    const operation = { kind: 'thread_milling', accessAxis: { x: 0, y: 0, z: 1 } };
    const feature = { kind: 'external_thread', pitchMm: 1, nominalDiameterMm: 10 };
    const semantic = { geometry: 'brep_thread_groove', axisName: 'z', axisSign: 1,
        accessAxis: { x: 0, y: 0, z: 1 },
        lateralNames: ['x', 'y'], centerMm: { x: 0, y: 0, z: 0 }, radiusMm: 5,
        majorRadiusMm: 5, minorRadiusMm: 5 - 0.6134, cutterCenterRadiusMm: 7 - 0.6134,
        pitchMm: 1, handedness: 'right', minimumAxialMm: 0, maximumAxialMm: 12,
        toolPath: { contract: 'CncCanonicalToolPath.v1', toolId: 'thread-mill-4',
            cutterDiameterMm: 4, usableCutLengthMm: 12, reachMm: 28,
            shankDiameterMm: 4, holderDiameterMm: 25, referencePoint: 'cutter_tip',
            orientationAxis: { x: 0, y: 0, z: 1 } },
        featureBounds: { minimum: { x: -6, y: -6, z: 0 }, maximum: { x: 6, y: 6, z: 12 } },
        targetClassifierContract: 'CncBrepValidationMesh.v1' };
    const tool = c.CncToolLibrary.get('thread-mill-4');
    const one = c.CncPlanValidator.deriveOperationSweep(makeState(), operation, feature, semantic, 0.05, null, tool);
    semantic.pitchMm = 2;
    semantic.minorRadiusMm = 5 - 0.6134 * 2;
    semantic.cutterCenterRadiusMm = semantic.minorRadiusMm + 2;
    const two = c.CncPlanValidator.deriveOperationSweep(makeState(), operation, feature, semantic, 0.05, null, tool);
    assert.notEqual(c.CncSpatialField.occupancyChecksum(one.state), c.CncSpatialField.occupancyChecksum(two.state));
});

function threadSweepFixture(c) {
    const resolutionMm = 0.5;
    const dimensions = { x: 48, y: 48, z: 24 };
    const totalCells = dimensions.x * dimensions.y * dimensions.z;
    const state = { origin: { x: -12, y: -12, z: 0 }, dimensions,
        resolutionMm, toleranceMm: 0.05, totalCells,
        bits: new Uint32Array(Math.ceil(totalCells / 32)).fill(0xffffffff) };
    const targetBits = new Uint32Array(state.bits.length);
    for (let index = 0; index < totalCells; index += 1) {
        const plane = dimensions.x * dimensions.y;
        const z = Math.floor(index / plane);
        const remainder = index - z * plane;
        const y = Math.floor(remainder / dimensions.x);
        const x = remainder - y * dimensions.x;
        const px = state.origin.x + (x + 0.5) * resolutionMm;
        const py = state.origin.y + (y + 0.5) * resolutionMm;
        if (Math.hypot(px, py) <= 6 && z >= 2 && z <= 21) {
            targetBits[index >>> 5] |= 1 << (index & 31);
        }
    }
    const target = { state: { ...state, bits: targetBits }, triangles: [] };
    const feature = { kind: 'external_thread', handedness: 'right', pitchMm: 1,
        nominalDiameterMm: 14 };
    const operation = { kind: 'thread_milling', accessAxis: { x: 0, y: 0, z: 1 } };
    const semantic = { geometry: 'brep_thread_groove', axisName: 'z', axisSign: 1,
        accessAxis: { x: 0, y: 0, z: 1 },
        lateralNames: ['x', 'y'], centerMm: { x: 0, y: 0, z: 0 }, radiusMm: 7,
        majorRadiusMm: 7, minorRadiusMm: 7 - 0.6134, cutterCenterRadiusMm: 9 - 0.6134,
        pitchMm: 1, handedness: 'right', minimumAxialMm: 0, maximumAxialMm: 12,
        toolPath: { contract: 'CncCanonicalToolPath.v1', toolId: 'thread-mill-4',
            cutterDiameterMm: 4, usableCutLengthMm: 12, reachMm: 28,
            shankDiameterMm: 4, holderDiameterMm: 25, referencePoint: 'cutter_tip',
            orientationAxis: { x: 0, y: 0, z: 1 } },
        featureBounds: { minimum: { x: -7, y: -7, z: 0 }, maximum: { x: 7, y: 7, z: 12 } },
        targetClassifierContract: 'CncBrepValidationMesh.v1' };
    return { state, target, feature, operation, semantic,
        threadMill: c.CncToolLibrary.get('thread-mill-4'),
        oneMill: c.CncToolLibrary.get('flat-1-standard') };
}

test('thread milling raw sweep is rasterized from the resolved cutter envelope', () => {
    const c = runtime();
    const fixture = threadSweepFixture(c);
    fixture.state.resolutionMm = 2;
    fixture.state.dimensions = { x: 12, y: 12, z: 20 };
    fixture.state.totalCells = fixture.state.dimensions.x * fixture.state.dimensions.y * fixture.state.dimensions.z;
    fixture.state.bits = new Uint32Array(Math.ceil(fixture.state.totalCells / 32)).fill(0xffffffff);
    const four = c.CncPlanValidator.deriveOperationSweep(fixture.state, fixture.operation,
        fixture.feature, fixture.semantic, 0.05, null, fixture.threadMill);
    const oneSemantic = structuredClone(fixture.semantic);
    oneSemantic.cutterCenterRadiusMm = oneSemantic.minorRadiusMm + fixture.oneMill.diameterMm / 2;
    oneSemantic.toolPath = { ...oneSemantic.toolPath, toolId: fixture.oneMill.id,
        cutterDiameterMm: fixture.oneMill.diameterMm,
        usableCutLengthMm: fixture.oneMill.usableCutLengthMm,
        reachMm: fixture.oneMill.reachMm,
        shankDiameterMm: fixture.oneMill.shankDiameterMm,
        holderDiameterMm: fixture.oneMill.holderDiameterMm };
    const one = c.CncPlanValidator.deriveOperationSweep(fixture.state, fixture.operation,
        fixture.feature, oneSemantic, 0.05, null, fixture.oneMill);
    assert.notEqual(four.rawSweepCellCount, one.rawSweepCellCount);
    assert.notEqual(four.rawSweepChecksum, one.rawSweepChecksum);
    assert.ok(four.assemblySweepCellCounts.cutter > one.assemblySweepCellCounts.cutter);
    assert.ok(four.assemblySweepCellCounts.shank > 0);
    assert.ok(four.assemblySweepCellCounts.holder > 0);
    assert.ok(four.trajectorySampleCount > 1);
});

test('thread milling handedness changes the sampled helical sweep', () => {
    const c = runtime();
    const fixture = threadSweepFixture(c);
    const right = c.CncPlanValidator.deriveOperationSweep(fixture.state, fixture.operation,
        fixture.feature, fixture.semantic, 0.05, null, fixture.threadMill);
    const leftSemantic = structuredClone(fixture.semantic);
    leftSemantic.handedness = 'left';
    const left = c.CncPlanValidator.deriveOperationSweep(fixture.state, fixture.operation,
        { ...fixture.feature, handedness: 'left' }, leftSemantic, 0.05, null, fixture.threadMill);
    assert.notEqual(right.rawSweepChecksum, left.rawSweepChecksum);
});

test('correct M14 thread-mill trajectory passes target feasibility before stock removal', () => {
    const c = runtime();
    const fixture = threadSweepFixture(c);
    const result = c.CncPlanValidator.deriveOperationSweep(fixture.state, fixture.operation,
        fixture.feature, fixture.semantic, 0.05, fixture.target, fixture.threadMill);
    assert.equal(result.targetViolationCount, 0);
    assert.ok(result.boundaryContactCellCount >= 0);
    assert.ok(result.removedCellCount > 0);
});

test('wrong resolved cutter diameter cannot borrow the M14 target as a clipping oracle', () => {
    const c = runtime();
    const fixture = threadSweepFixture(c);
    const undersizedPath = structuredClone(fixture.semantic);
    undersizedPath.cutterCenterRadiusMm = undersizedPath.minorRadiusMm + fixture.oneMill.diameterMm / 2;
    const result = c.CncPlanValidator.deriveOperationSweep(fixture.state, fixture.operation,
        fixture.feature, undersizedPath, 0.05, fixture.target, fixture.threadMill);
    assert.ok(result.targetViolationCount > 0);
    assert.equal(result.removedCellCount, 0, 'collision must fail before any stock mutation');
    assert.deepEqual(Array.from(result.state.bits), Array.from(fixture.state.bits));
});

test('offset and overcut thread trajectories report physical target collisions', () => {
    const c = runtime();
    const fixture = threadSweepFixture(c);
    const offset = structuredClone(fixture.semantic);
    offset.centerMm.x = -2;
    const offsetResult = c.CncPlanValidator.deriveOperationSweep(fixture.state, fixture.operation,
        fixture.feature, offset, 0.05, fixture.target, fixture.threadMill);
    assert.ok(offsetResult.targetViolationCount > 0);

    const overcut = structuredClone(fixture.semantic);
    overcut.cutterCenterRadiusMm -= 1.5;
    const overcutResult = c.CncPlanValidator.deriveOperationSweep(fixture.state, fixture.operation,
        fixture.feature, overcut, 0.05, fixture.target, fixture.threadMill);
    assert.ok(overcutResult.targetViolationCount > 0);
    assert.equal(overcutResult.removedCellCount, 0);
});

test('thread milling checks the complete cutter, shank and holder spans from an explicit tool reference', () => {
    const c = runtime();
    const fixture = threadSweepFixture(c);
    fixture.state.resolutionMm = 1;
    fixture.state.dimensions = { x: 24, y: 24, z: 52 };
    fixture.state.totalCells = fixture.state.dimensions.x * fixture.state.dimensions.y * fixture.state.dimensions.z;
    fixture.state.bits = new Uint32Array(Math.ceil(fixture.state.totalCells / 32)).fill(0xffffffff);
    const targetBits = new Uint32Array(fixture.state.bits.length);
    const targetCenters = [18, 30, 45].map(z => ({ x: fixture.semantic.cutterCenterRadiusMm, y: 0, z }));
    for (let index = 0; index < fixture.state.totalCells; index += 1) {
        const p = (() => {
            const plane = fixture.state.dimensions.x * fixture.state.dimensions.y;
            const z = Math.floor(index / plane);
            const remainder = index - z * plane;
            const y = Math.floor(remainder / fixture.state.dimensions.x);
            const x = remainder - y * fixture.state.dimensions.x;
            return { x: fixture.state.origin.x + (x + 0.5) * fixture.state.resolutionMm,
                y: fixture.state.origin.y + (y + 0.5) * fixture.state.resolutionMm,
                z: fixture.state.origin.z + (z + 0.5) * fixture.state.resolutionMm };
        })();
        if (targetCenters.some(targetCenter => Math.abs(p.x - targetCenter.x) <= 1.5
            && Math.abs(p.y - targetCenter.y) <= 1.5 && Math.abs(p.z - targetCenter.z) <= 1.5)) {
            targetBits[index >>> 5] |= 1 << (index & 31);
        }
    }
    const target = { state: { ...fixture.state, bits: targetBits }, triangles: [] };
    const result = c.CncPlanValidator.deriveOperationSweep(fixture.state, fixture.operation,
        fixture.feature, fixture.semantic, 0.05, target, fixture.threadMill);
    assert.equal(result.assemblyAxialSpansMm.cutter, 12);
    assert.equal(result.toolReference, 'cutter_tip');
    assert.equal(JSON.stringify(result.toolOrientationAxis), JSON.stringify({ x: 0, y: 0, z: 1 }));
    assert.ok(result.targetViolationCellCounts.cutter > 0,
        'target in the cutter-body span beyond the old pitch/4 slice must collide');
    assert.ok(result.targetViolationCellCounts.shank > 0, 'target intersecting the shank span must collide');
    assert.ok(result.targetViolationCellCounts.holder > 0, 'target intersecting the holder span must collide');
    assert.equal(result.removedCellCount, 0);
});

test('validated removal evidence applies immutable runs without target-oracle mutation', () => {
    const c = runtime();
    const fixture = threadSweepFixture(c);
    const result = c.CncPlanValidator.deriveOperationSweep(fixture.state, fixture.operation,
        fixture.feature, fixture.semantic, 0.05, fixture.target, fixture.threadMill);
    assert.equal(result.validatedRemovalCellCount, result.removedCellCount);
    assert.match(result.validatedRemovalMaskChecksum, /^[0-9a-f]{8}$/);
    assert.match(result.boundaryContactMaskChecksum, /^[0-9a-f]{8}$/);
    assert.ok(Object.isFrozen(result.validatedRemovalRuns));
    const replay = new Uint32Array(fixture.state.bits);
    for (const run of result.validatedRemovalRuns) {
        for (let index = run.start; index < run.start + run.length; index += 1) {
            replay[index >>> 5] &= ~(1 << (index & 31));
        }
    }
    assert.deepEqual(Array.from(replay), Array.from(result.state.bits));
});
