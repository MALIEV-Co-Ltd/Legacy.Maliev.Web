const test = require('node:test'), assert = require('node:assert/strict');
const { runtime, envelope, topology, expectation } = require('./cnc-native-region-fixtures.cjs');
test('public native disk, annulus and ellipse planes preserve actual numerical measures and wires', async () => {
    const c = runtime();
    for (const [name, area] of [['disk-cylinder', Math.PI * 64], ['ring', Math.PI * (22.25 ** 2 - 10.7 ** 2)], ['ellipse-cap', null]]) {
        const n = await envelope(c, name), t = await topology(c, n), p = await c.CncNativeRegions.build(t);
        assert.equal(t.nativeInterpretation.interpretationAccepted, true, JSON.stringify(t.nativeInterpretation));
        assert.equal(p.sourceVerified, true);
        const planes = p.regions.filter(r => r.supportType === 'plane'); assert.equal(planes.length, 2);
        for (const r of planes) {
            const expectedArea = area === null ? Math.PI * 25 / Math.abs(r.support.orientedNormal[2]) : area;
            assert.equal(r.status, 'available', r.reason); assert.ok(Math.abs(r.measures.value - expectedArea) < 1e-6);
            assert.equal(r.measures.centroidErrorStatus, 'not-estimated'); assert.equal(r.measures.centroidAbsoluteErrorMm, null);
            assert.equal(r.band.status, 'unavailable'); assert.equal(r.band.reason, 'unsupported_support');
            assert.equal(r.band.precision.outwardEnclosure, false); assert.ok(Object.isFrozen(r.measures));
            assert.equal(r.wires.filter(w => w.role === 'inner').length, name === 'ring' ? 1 : 0);
        }
    }
});
test('strict native band validation rejects raw corruption after recomputing import identity', async () => {
    const c = runtime(), n = await envelope(c, 'ring'), expected = expectation(n);
    const mutations = [b => delete b.schema, b => b.units = 'inch', b => b.methodVersion++, b => b.bodyId = 'foreign', b => b.faceId = 'foreign',
        b => b.precision.outwardEnclosure = true, b => b.precision.epsilonFactor = 65, b => b.frame.origin[0]++, b => b.parameters.referenceRadius++,
        b => b.wireOccurrences[0].orderedCoedgeIds.pop(), b => b.wireOccurrences.push(structuredClone(b.wireOccurrences[0])),
        b => b.sides[0].occurrences.pop(), b => b.sides[0].occurrences[0].nativeRange[0] -= 1, b => b.sides[0].occurrences[0].orientation ^= 1,
        b => b.sides[1].orderedCoedgeIds = b.sides[0].orderedCoedgeIds, b => b.seamPairs[0].edgeId = b.boundaryComponents[0].edgeIds[0],
        b => b.seamPairs.push(structuredClone(b.seamPairs[0])), b => b.boundaryComponents.pop(), b => b.boundaryComponents[0].edgeIds.push('foreign'),
        b => b.uCoverage.kind = 'partial', b => b.uCoverage.coveredIntervals[0][1] -= .1, b => b.profileInterval.axialMm[1]++,
        b => b.polarity.radial = b.polarity.radial === 'inward' ? 'outward' : 'inward', b => b.helicalBoundaryStatus = 'oblique-or-helical',
        b => b.seamPairs[0] = null, b => b.sides[0] = null, b => b.boundaryComponents[0] = null, b => b.wireOccurrences[0] = null];
    for (const [i, mutate] of mutations.entries()) {
        const bad = structuredClone(n), f = bad.meshes[0].brep_faces.find(f => f.nativeRotationalBand.status === 'available'); mutate(f.nativeRotationalBand);
        const t = await topology(c, bad, expected), p = await c.CncNativeRegions.build(t), r = p.regions.find(r => r.faceId === f.faceId);
        assert.equal(r.status, 'unavailable', 'mutation ' + i); assert.match(r.reason, /native_band_invalid/);
        const d = await c.CncNativePrismaticRecognition.recognize(t);
        assert.equal(d.features.find(x => x.primaryFaceIds.includes(f.faceId)).kind, 'unresolved');
        assert.equal(d.features.length, 4);
    }
});
test('diagnostics require the actual positive topology instance and recheck mutation binding', async () => {
    const c = runtime(), n = await envelope(c, 'ring'), t = await topology(c, n);
    assert.equal((await c.CncNativeRegions.build(t)).sourceVerified, true);
    const copied = structuredClone(t); copied.nativeInterpretation.interpretationAccepted = true;
    assert.equal((await c.CncNativeRegions.build(copied)).sourceVerified, false);
    // Explicit no-expectation path.
    const missing = await c.CncTopology.build({ nativeImport: structuredClone(n) });
    assert.equal((await c.CncNativeRegions.build(missing)).sourceVerified, false);
    t.cadDocument.nativeImport.meshes[0].brep_faces[0].nativeRotationalBand.reason = 'corrupt';
    assert.equal((await c.CncNativeRegions.build(t)).sourceVerified, false);
    assert.equal(missing.automaticPlanningEligible, false);
});
test('ordinary STEP seam precision is capability-limited and unsupported ellipse extrusion remains nonpositive', async () => {
    const c = runtime(), n = await envelope(c, 'ring-default-precision'), t = await topology(c, n), p = await c.CncNativeRegions.build(t);
    assert.equal(p.sourceVerified, true);
    assert.equal(p.regions.filter(r => r.status === 'available').length, 2);
    for (const r of p.regions.filter(r => r.supportType === 'cylinder')) {
        assert.equal(r.reason, 'native_band_unavailable:parameter_boundary_ambiguous');
        assert.equal(r.band.status, 'unavailable'); assert.equal(r.precision.faceTolerance, 1e-7);
    }
    const coedges = n.meshes[0].brep_faces.find(f => f.support.type === 'cylinder').trims.wires[0].coedges;
    assert.ok(coedges.some(u => u.pcurve.origin[0] === 6.28318530718));
    assert.ok(coedges.some(u => u.range[1] === 2 * Math.PI));
    const extrusion = await topology(c, await envelope(c, 'ellipse-prism'));
    assert.equal(extrusion.nativeInterpretation.interpretationAccepted, false);
    assert.ok(extrusion.nativeInterpretation.reasons.includes('source-oriented-cycle-changed'));
    assert.ok((await c.CncNativeRegions.build(extrusion)).regions.every(r => r.reason === 'native_region_source_unverified'));
});
test('native source edge and coedge corruption remains nonpositive and display vertices do not supply region area', async () => {
    const c = runtime(), n = await envelope(c, 'ring'), expected = expectation(n);
    for (const mutate of [x => x.meshes[0].kernelTopology.edges[0].uses.pop(), x => x.meshes[0].brep_faces[0].trims.wires[0].coedges[0].coedgeId = 'foreign']) {
        const bad = structuredClone(n); mutate(bad);
        const t = await topology(c, bad, expected); assert.equal((await c.CncNativeRegions.build(t)).sourceVerified, false);
    }
    const changed = structuredClone(n); changed.meshes[0].attributes.position.array.fill(0);
    const t = await topology(c, changed, expected), p = await c.CncNativeRegions.build(t);
    assert.equal(p.sourceVerified, true);
    assert.ok(p.regions.filter(r => r.supportType === 'plane').every(r => r.measures.value > 1000));
});
test('missing native evidence preserves every identifiable face on copied and locally mutated topology', async () => {
    const c = runtime(), n = await envelope(c, 'ring');
    const mutations = ['nativeRegionMeasures', 'support', 'trims', 'assemblyInstance', 'precision', 'nativeRotationalBand'].map(key => t => delete t.cadDocument.nativeImport.meshes[0].brep_faces[0][key]);
    mutations.push(t => t.cadDocument.nativeImport.meshes[0].kernelTopology = null,
        t => t.cadDocument.nativeImport.meshes[0].brep_faces = null,
        t => t.cadDocument.nativeImport.meshes[0].brep_faces[0] = null,
        t => t.cadDocument.nativeImport.meshes = null,
        t => t.cadDocument.nativeImport = null);
    for (const copied of [false, true]) {
        for (const [i, mutate] of mutations.entries()) {
            const original = await topology(c, structuredClone(n)), t = copied ? structuredClone(original) : original; mutate(t);
            const d = await c.CncNativePrismaticRecognition.recognize(t);
            assert.equal(d.projection.sourceVerified, false, `mutation ${i} copied=${copied}`);
            assert.equal(d.features.length, 4); assert.equal(new Set(d.features.flatMap(f => f.primaryFaceIds)).size, 4);
            assert.ok(d.features.every(f => f.kind === 'unresolved' && f.required));
            assert.ok(d.projection.regions.every(r => r.status === 'unavailable' && r.reason === 'native_region_source_unverified'));
            if (i >= 7) assert.ok(d.structuralIssues.length > 0);
        }
    }
    const empty = await c.CncNativePrismaticRecognition.recognize({ faces: [{ nativeFace: null }], cadDocument: null });
    assert.equal(empty.features.length, 0); assert.ok(empty.structuralIssues.some(i => i.reason === 'native_region_face_identity_missing'));
});
