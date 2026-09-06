const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { webcrypto } = require('node:crypto');

const webRoot = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot');
const fixturePath = path.resolve(__dirname, '../TestAssets/Cnc/axial-threaded-part.step');
const modules = ['cnc-plan-contracts', 'cnc-cad-surfaces.worker', 'cnc-topology.worker',
    'cnc-feature-graph.worker', 'cnc-quotation-config', 'cnc-material-catalog',
    'cnc-tool-library', 'cnc-process-compiler', 'cnc-stock', 'cnc-spatial-field.worker',
    'cnc-fixture-catalog', 'cnc-manufacturing-evidence.worker', 'cnc-setup-planner', 'cnc-plan-validator.worker'];

function runtime() {
    const context = vm.createContext({ console, TextEncoder, crypto: webcrypto });
    context.self = context;
    context.window = context;
    for (const name of modules) {
        const source = fs.readFileSync(path.join(webRoot, 'src/app/js/cnc-quotation', name + '.js'), 'utf8');
        vm.runInContext(source, context, { filename: name });
    }
    return context;
}

async function runAcceptance(linearDeflection) {
    const c = runtime();
    const occtRoot = path.join(webRoot, 'lib/occt');
    const occt = await require(path.join(occtRoot, 'occt-import-js.js'))({
        wasmBinary: fs.readFileSync(path.join(occtRoot, 'occt-import-js.wasm'))
    });
    const source = fs.readFileSync(fixturePath);
    const model = occt.ReadStepFile(source, { linearDeflection });
    const validationModel = occt.ReadStepFile(source, { linearDeflection: 0.1 });
    assert.equal(model.success, true);
    assert.equal(validationModel.success, true);
    const topology = await c.CncTopology.build({ meshes: model.meshes, validationMeshes: validationModel.meshes,
        validationDeflectionMm: 0.1,
        analyticSurfaces: c.CncCadSurfaces.parseStep(source.toString()),
        bodyCount: model.meshes.length, sourceFormat: 'step' });
    const featureGraph = c.CncFeatureGraph.build(topology, { modelScaleMm: 40 });
    const operationGraph = c.CncProcessCompiler.compile(featureGraph,
        { toolLibraryVersion: c.CncToolLibrary.version });
    const quotedStock = c.CncStock.selectStock({ alloy: '6061', quantity: 1,
        partSizeMm: { x: 27, y: 26.97, z: 39 }, partVolumeMm3: 5350,
        clampBorderMm: 25, includeShipping: true });
    const preparation = c.CncManufacturingEvidence.prepare(topology, featureGraph, operationGraph, quotedStock);
    let setupPlan = c.CncSetupPlanner.plan({ topology, featureGraph, operationGraph,
        stock: preparation.setupStock, fixtureCatalog: preparation.fixtureCatalog });
    const stock = c.CncManufacturingEvidence.complete(topology, featureGraph, operationGraph, setupPlan, quotedStock);
    const result = await c.CncPlanValidator.validate({ topology, featureGraph, operationGraph,
        setupPlan, stock, requirementsRevision: 'axial-m14-acceptance-v1',
        toolLibraryVersion: c.CncToolLibrary.version });
    assert.equal(result.valid, true, JSON.stringify({ reasons: result.reviewReasons, diagnostics: result.diagnostics,
        paths: stock && stock.operationPaths.map(item => ({ operationId: item.operationId,
            geometry: item.semanticPath.geometry, featureKind: item.semanticPath.featureKind,
            bounds: item.semanticPath.featureBounds, radius: item.semanticPath.radiusMm })) })) ;
    return JSON.parse(JSON.stringify(result.plan));
}

function assertManufacturingOutcome(plan) {
    assert.equal(plan.setupPlan.setups.length, 2);
    const operations = plan.operationGraph.operations;
    const thread = operations.filter(operation => operation.kind === 'thread_milling');
    assert.equal(thread.length, 1);
    assert.equal(thread[0].threadDesignation, 'M14 x 1');
    const features = Object.fromEntries(plan.featureGraph.features.map(feature => [feature.id, feature]));
    assert.equal(features[thread[0].featureId].kind, 'external_thread');
    assert.equal(operations.some(operation => features[operation.featureId].kind === 'internal_thread'), false);
    assert.equal(operations.some(operation => operation.toolClass === 'ball_end_mill'), false);
    assert.equal(operations.some(operation => operation.toolClass === 'flat_end_mill'
        && operation.toolDiameterMm <= 2), false);
    for (const operation of operations.filter(operation => operation.kind === 'finishing')) {
        const validation = plan.validationResults.find(item => item.operationId === operation.id);
        assert.ok(validation && validation.removedCellCount > 0,
            'every finish pass must remove material retained by its versioned rough allowance');
    }
    const chamfer = operations.find(operation => operation.kind === 'chamfering'
        && operation.toolClass === 'chamfer_mill');
    assert.ok(chamfer);
    assert.equal(chamfer.toolId, 'chamfer-6');
    assert.equal(chamfer.toolConstraints.minimumReachMm <= 24, true);
    assert.equal(chamfer.toolConstraints.reachMm, 24);
    assert.equal(plan.terminalResidual.contract, 'CncTerminalResidualValidation.v1');
    assert.equal(plan.terminalResidual.missingTargetCellCount, 0);
    assert.equal(plan.terminalResidual.excessStockCellCount, 0);
    assert.equal(plan.terminalResidual.residualChecksum, plan.terminalResidual.targetChecksum);
}

test('real axial M14 B-Rep finalizes deterministically in two setups', async () => {
    const runs = [];
    for (let index = 0; index < 3; index += 1) {
        const plan = await runAcceptance(0.1);
        assertManufacturingOutcome(plan);
        runs.push(plan.planHash);
    }
    assert.equal(new Set(runs).size, 1, 'three identical imports must produce one canonical plan hash');
});

test('real axial M14 manufacturing plan is invariant to display tessellation', async () => {
    const fine = await runAcceptance(0.1);
    const coarse = await runAcceptance(0.5);
    assertManufacturingOutcome(fine);
    assertManufacturingOutcome(coarse);
    assert.equal(fine.planHash, coarse.planHash);
});
