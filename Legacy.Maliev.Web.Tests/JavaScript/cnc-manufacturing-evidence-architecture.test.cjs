const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const moduleRoot = path.resolve(__dirname,
    '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation');

function source(name) {
    return fs.readFileSync(path.join(moduleRoot, name + '.js'), 'utf8');
}

test('production planner exposes no legacy diagnostic plan surface', () => {
    const planning = source('cnc-planning');
    assert.doesNotMatch(planning,
        /CncPlanningDiagnostics|function\s+planFixture\b|function\s+plan\s*\(|clusterFeatureEvidence|weightedReachSetups/);
    assert.doesNotMatch(planning,
        /surfaceClusters|operationCodes|curvedFinishingByDirection|unresolved_ball/);
});

test('manufacturing evidence publishes semantic paths, not caller-certified removal cells', () => {
    const evidence = source('cnc-manufacturing-evidence.worker');
    assert.match(evidence, /CncFixtureCatalog\.v2/);
    assert.match(evidence, /supplierBlank/);
    assert.match(evidence, /targetSolid/);
    assert.match(evidence, /semanticPath/);
    assert.doesNotMatch(evidence, /removedRuns|coveredByPriorOperationIds|workholdingEvidence:\s*\{\s*verified:\s*true/);
});

test('validator derives removal from semantic paths and requires positive finish stock', () => {
    const validator = source('cnc-plan-validator.worker');
    assert.match(validator, /deriveOperationSweep/);
    assert.match(validator, /finish_allowance_required|positive_finish_removal_required/);
    assert.match(validator, /canonicalSemanticPath/);
    assert.match(validator, /targetSolidOccupancy/);
    assert.match(validator, /toolAssemblyClear/);
    assert.match(validator, /fixtureCatalog\.capability/);
    assert.doesNotMatch(validator, /recomputeSweep\(current, volume, sweep\.removedRuns\)/);
});

test('model worker builds validation occupancy from a fixed tessellation independent of display geometry', () => {
    const modelWorker = fs.readFileSync(path.resolve(moduleRoot, '../model-viewer/model-viewer.worker.js'), 'utf8');
    const topology = source('cnc-topology.worker');
    assert.match(modelWorker, /canonicalValidationMeshes/);
    assert.match(modelWorker, /linearDeflection:\s*0\.1/);
    assert.match(topology, /occt_brep_validation_tessellation_v1/);
});

test('browser-local worker selects supplier stock before building a validated plan', () => {
    const worker = source('cnc-quotation.worker');
    const stock = worker.indexOf('CncStock.selectStock');
    const plan = worker.indexOf('buildValidatedPlan');
    assert.ok(stock >= 0 && plan >= 0 && stock < worker.indexOf('await buildValidatedPlan', plan));
});
