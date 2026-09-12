const test = require('node:test'), assert = require('node:assert/strict');
const { runtime, envelope, topology } = require('./cnc-native-region-fixtures.cjs');
test('public 21.4 x 14.8 ring has one hole, one outside band and two distinct planar candidates', async () => {
    const c = runtime();
    for (const name of ['ring', 'ring-transformed', 'ring-flipped-axis']) {
        const t = await topology(c, await envelope(c, name)), d = await c.CncNativePrismaticRecognition.recognize(t);
        assert.equal(d.projection.sourceVerified, true); assert.equal(d.features.length, 4);
        assert.deepEqual(Array.from(d.features, f => f.kind).sort(), ['datum', 'datum', 'hole', 'outside_profile']);
        const h = d.features.find(f => f.kind === 'hole');
        assert.ok(Math.abs(h.dimensions.diameterMm - 21.4) < 1e-10); assert.ok(Math.abs(h.dimensions.depthMm - 14.8) < 1e-10);
        assert.equal(h.candidateAccessEnds.length, 2); assert.ok(h.candidateAccessEnds.every(e => e.toolAccessVerified === false));
        assert.equal(h.accessAxes.length, 0); assert.ok(t.faces.every(f => f.loops.every(l => l.vertices.length === 0)));
        assert.ok(d.features.filter(f => f.kind === 'datum').every(f => f.machiningRequired === null));
        assert.equal(t.automaticPlanningEligible, false);
        assert.throws(() => c.CncFeatureGraph.build(t), /native_recognition_requires_local_dispatch/);
    }
});
test('stepped native bore stays unresolved by stage and partial cylinder is never a full outside band', async () => {
    const c = runtime(), t = await topology(c, await envelope(c, 'stepped-bore')), d = await c.CncNativePrismaticRecognition.recognize(t);
    assert.equal(d.projection.sourceVerified, true); assert.equal(d.features.filter(f => f.kind === 'hole').length, 0);
    assert.equal(d.features.filter(f => f.reason === 'native_hole_opening_or_stepped_chain_unsupported').length, 2);
    assert.equal(new Set(d.features.flatMap(f => f.primaryFaceIds)).size, t.faces.length);
    const partial = await topology(c, await envelope(c, 'partial-cylinder')), regions = await c.CncNativeRegions.build(partial);
    const r = regions.regions.find(r => r.supportType === 'cylinder');
    assert.equal(r.status, 'available', r.reason); assert.equal(r.band.uCoverage.kind, 'partial');
    const bad = structuredClone(partial.cadDocument.nativeImport), band = bad.meshes[0].brep_faces.find(f => f.support.type === 'cylinder').nativeRotationalBand;
    band.uCoverage.kind = 'complete-revolution'; band.uCoverage.liftedInterval = [0, 2 * Math.PI]; band.uCoverage.coveredIntervals = [[0, 2 * Math.PI]]; band.uCoverage.winding = 1;
    const forged = await c.CncNativePrismaticRecognition.recognize(await topology(c, bad));
    assert.equal(forged.features.filter(f => f.kind === 'outside_profile').length, 0);
    assert.ok(forged.unresolved.some(f => f.reason === 'native_band_invalid:angular-coverage'));
});
test('capability-unavailable inward or unknown outer-wire neighbor cannot turn a stepped bore into an opening', async () => {
    const c = runtime(), n = await envelope(c, 'stepped-bore');
    for (const unknown of [false, true]) {
        const bad = structuredClone(n), larger = bad.meshes[0].brep_faces.find(f => f.support.type === 'cylinder' && f.support.radius === 8);
        const b = larger.nativeRotationalBand;
        b.status = 'unavailable'; b.reason = 'parameter_boundary_ambiguous';
        for (const key of ['frame', 'parameters', 'uCoverage', 'profileInterval', 'sides', 'seamPairs', 'boundaryComponents', 'polarity']) b[key] = null;
        if (unknown) { larger.support.type = 'unsupported'; larger.support.reason = 'test-unsupported-native-support'; b.supportType = 'unsupported'; b.reason = 'unsupported_support'; }
        const d = await c.CncNativePrismaticRecognition.recognize(await topology(c, bad));
        assert.equal(d.projection.sourceVerified, true, 'capability limit does not revoke valid native interpretation');
        assert.equal(d.features.filter(f => f.kind === 'hole').length, 0);
        const smaller = d.projection.regions.find(r => r.supportType === 'cylinder' && r.support.radius === 5);
        assert.equal(d.features.find(f => f.regionId === smaller.regionId).reason, 'native_hole_opening_or_stepped_chain_unsupported');
        assert.equal(new Set(d.features.flatMap(f => f.primaryFaceIds)).size, n.meshes[0].brep_faces.length);
    }
    // An unavailable OUTWARD band is a known native exterior role: keep the
    // reviewed split-edge positive while rejecting missing/unknown roles above.
    const split = await c.CncNativePrismaticRecognition.recognize(await topology(c, await envelope(c, 'ring-split-edges')));
    assert.equal(split.features.filter(f => f.kind === 'hole').length, 1);
    assert.ok(split.projection.regions.some(r => r.supportType === 'cylinder' && r.support.orientationSign === 1 && r.band.status === 'unavailable'));
});
test('opposite coaxial blind holes retain distinct required unresolved owners and never become a through hole', async () => {
    const c = runtime(), t = await topology(c, await envelope(c, 'opposite-blind')), d = await c.CncNativePrismaticRecognition.recognize(t);
    assert.equal(d.features.filter(f => f.kind === 'hole').length, 0);
    const inward = d.projection.regions.filter(r => r.band && r.band.status === 'available' && r.band.polarity.radial === 'inward');
    assert.equal(inward.length, 2);
    for (const r of inward) assert.equal(d.features.find(f => f.regionId === r.regionId).reason, 'native_hole_opening_or_stepped_chain_unsupported');
    assert.equal(new Set(d.features.flatMap(f => f.primaryFaceIds)).size, t.faces.length);
});
test('native ownership order follows stable IDs rather than input face iteration', async () => {
    const c = runtime(), n = await envelope(c, 'ring'), first = await c.CncNativePrismaticRecognition.recognize(await topology(c, n));
    const reversed = structuredClone(n); reversed.meshes[0].brep_faces.reverse();
    const second = await c.CncNativePrismaticRecognition.recognize(await topology(c, reversed));
    const roles = d => Array.from(d.features, f => [f.featureId, f.kind, f.primaryFaceIds[0], f.dimensions]);
    assert.deepEqual(JSON.parse(JSON.stringify(roles(first))), JSON.parse(JSON.stringify(roles(second))));
});
test('split native ring edges join the complete planar wire despite reversed neighbor order', async () => {
    const c = runtime(), t = await topology(c, await envelope(c, 'ring-split-edges')), d = await c.CncNativePrismaticRecognition.recognize(t);
    const holes = d.features.filter(f => f.kind === 'hole'); assert.equal(holes.length, 1);
    assert.ok(holes[0].candidateAccessEnds.every(e => e.edgeIds.length === 2));
    assert.equal(d.features.filter(f => f.kind === 'datum').length, 2);
    assert.equal(d.features.filter(f => f.kind === 'unresolved').length, 1, 'unsupported outer pcurve remains owned');
    const r = d.projection.regions.find(r => r.regionId === holes[0].regionId);
    assert.ok(r.boundaries.every(b => b.neighbors.length === 2));
});
test('actual promoted model-worker source expectation dispatches native owners without manufacturing eligibility', async () => {
    const fs = require('node:fs'), path = require('node:path');
    const f = require('./cnc-model-worker-harness.cjs').runtime(), bytes = fs.readFileSync(path.resolve(__dirname, '../TestAssets/CncNativeInterpretation/ring.step'));
    const object = await f.c.RunParseJob({ extension: 'step', analysisProfile: 'cnc', buffer: bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) });
    const geometry = await f.c.AnalyzeCncObject(object, f.c.AnalyzeObject(object));
    const diagnostic = await f.c.CncNativePrismaticRecognition.recognize(geometry.cadTopology);
    assert.equal(diagnostic.projection.sourceVerified, true);
    assert.equal(diagnostic.features.filter(feature => feature.kind === 'hole').length, 1);
    const graph = geometry.manufacturingFeatureGraph;
    assert.equal(graph.features.length, 4); assert.equal(geometry.cadTopology.automaticPlanningEligible, false);
    assert.equal(graph.automaticPlanningEligible, false);
    assert.equal(graph.nativeDiagnosticRevision, diagnostic.revision);
    assert.equal(graph.provenance.nativeImportRevision, geometry.cadTopology.cadDocument.nativeImport.importRevision);
    assert.equal(graph.provenance.topologyRevision, geometry.cadTopology.revision);
    assert.ok(graph.features.every(feature => feature.machiningRequired === null && feature.accessAxes.length === 0
        && graph.unresolved.some(issue => issue.featureId === feature.id && issue.required === true)));
    const copy = structuredClone(geometry.cadTopology);
    assert.equal((await f.c.CncNativePrismaticRecognition.recognize(copy)).features.filter(feature => feature.kind !== 'unresolved').length, 0);
});
