const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const webRoot = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot');
const plain = value => JSON.parse(JSON.stringify(value));

function runtime() {
    const c = vm.createContext({ console });
    c.self = c;
    c.window = c;
    vm.runInContext(fs.readFileSync(path.join(webRoot, 'src/vendor/three/three.js'), 'utf8'), c);
    for (const name of ['cnc-quotation-config', 'cnc-material-catalog', 'cnc-tool-library', 'cnc-stock',
        'cnc-spatial-field.worker', 'cnc-geometry.worker', 'cnc-ball-rest.worker']) {
        vm.runInContext(fs.readFileSync(path.join(webRoot, 'src/app/js/cnc-quotation', name + '.js'), 'utf8'), c,
            { filename: name });
    }
    return c;
}

function box(c) {
    return new c.THREE.BoxGeometry(6, 4, 4).toNonIndexed().attributes.position.array;
}

const info = { volume: 96, bodyCount: 1, nonWatertight: false, nonManifold: false,
    analyticSurfaces: [], cadFaceRanges: [] };
const retained = ['bodyCount', 'orientedSizeMm', 'principalAxes', 'orientationEvidence', 'orientationCandidates',
    'analysisLimits', 'rotationalEvidence', 'planarAreaRatio', 'boxFillRatio', 'topBottomPlanarCoverage',
    'legacyFeatureEvidenceDiagnosticOnly', 'holeProxies', 'threadProxies', 'chamferProxies', 'pocketProxies',
    'undercutRisk', 'deepFeatureRisk', 'flatPlateEligible', 'weakGripRisk', 'geometryConfidence', 'reviewReasons'];

function summary(value) {
    return plain(Object.fromEntries(retained.map(key => [key, value[key]])));
}

function stock(c, geometry) {
    return plain(c.CncStock.selectStock({ alloy: '6061', quantity: 1, partSizeMm: geometry.orientedSizeMm,
        partVolumeMm3: 96, flatPlateEligible: geometry.flatPlateEligible, principalAxes: geometry.principalAxes,
        rotationalEvidence: geometry.rotationalEvidence, analysisLimits: geometry.analysisLimits,
        clampBorderMm: 25, includeShipping: false }));
}

test('explicit manufacturing summary never invokes the excluded diagnostic passes', () => {
    const c = runtime();
    const excluded = () => { throw new Error('diagnostic pass invoked'); };
    c.CncSpatialField = { build: excluded, classifyToolAccess: excluded, serialize: excluded };
    c.CncBallRest = { createVerifier: excluded };
    c.CncAssignFieldSamplesToClusters = excluded;
    c.CncFlatToolContactVerifier = excluded;
    c.CncExtendVerifiedFilletRegions = excluded;
    c.CncGeneralBallEvidence = excluded;
    const result = c.AnalyzeCncGeometry(box(c), info, { mode: 'manufacturing_summary' });
    assert.deepEqual(plain(result.orientedSizeMm), { x: 6, y: 4, z: 4 });
    assert.equal(result.boxFillRatio, 1);
    assert.equal(result.accessibilityField, null);
    for (const key of ['ballRestHandoffs', 'ballRestFinishingAccess', 'generalBallRestHandoffs',
        'generalBallFinishingAccess', 'stockFacingRequirements']) assert.deepEqual(plain(result[key]), []);
    assert.deepEqual(plain(result.generalBallEvidenceLimits), {});
});

test('summary and default full analysis preserve stock, proxies, reviews and confidence on real mesh input', () => {
    const c = runtime(), triangles = box(c);
    const compact = c.AnalyzeCncGeometry(triangles, info, { mode: 'manufacturing_summary' });
    assert.equal(compact.accessibilityField, null);
    assert.equal(compact.geometryConfidence, 'High');
    assert.deepEqual(plain(compact.reviewReasons), []);
    const full = c.AnalyzeCncGeometry(triangles, info);
    assert.ok(full.accessibilityField, 'default callers retain full diagnostic analysis');
    assert.ok(Object.keys(full.accessibilityField.toolAccess).length > 0);
    assert.deepEqual(summary(compact), summary(full));
    assert.deepEqual(stock(c, compact), stock(c, full));
});

test('null, empty, full and unknown options retain the diagnostic field build', () => {
    const c = runtime();
    // Field construction is expensive; the real default pipeline is covered above.
    const diagnosticBoundary = new Error('diagnostic field requested');
    c.CncSpatialField = { ...c.CncSpatialField, build: () => { throw diagnosticBoundary; } };
    for (const options of [null, {}, { mode: 'full' }, { mode: 'unknown' }]) {
        assert.throws(() => c.AnalyzeCncGeometry(box(c), info, options), error => error === diagnosticBoundary);
    }
});

test('summary retains imported quality warnings instead of promoting incomplete geometry', () => {
    const c = runtime();
    const result = c.AnalyzeCncGeometry(box(c), { ...info, nonWatertight: true, nonManifold: true,
        bodyCount: 2 }, { mode: 'manufacturing_summary' });
    assert.equal(result.geometryConfidence, 'Low');
    assert.deepEqual(plain(result.reviewReasons), ['non_watertight_geometry', 'non_manifold_geometry', 'multi_body_geometry']);
    assert.equal(result.bodyCount, 2);
});
