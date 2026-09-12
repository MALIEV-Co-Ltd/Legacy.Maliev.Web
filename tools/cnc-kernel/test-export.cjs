// Run against a candidate pair; no installed runtime is overwritten.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const candidate = path.resolve(process.argv[2]);
const fixtures = path.resolve(process.argv[3]);
const repoFixtures = path.resolve(__dirname, '../../Maliev.Web.Tests/TestAssets');
const near = (actual, expected, tolerance = 1e-5) => assert.ok(Math.abs(actual - expected) <= tolerance, `${actual} != ${expected}`);
const dot = (a, b) => a.reduce((sum, value, i) => sum + value * b[i], 0);
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
function verify(result) {
  assert.equal(result.kernelProvenance.schema, 'MalievKernelFaces.v1');
  assert.equal(result.kernelProvenance.documentCoverage.schema, 'MalievKernelDocument.v1');
  const ids = new Set();
  for (const mesh of result.meshes) {
    let nextTriangle = 0;
    for (const face of mesh.brep_faces) {
      assert.ok(!ids.has(face.faceId)); ids.add(face.faceId);
      assert.equal(face.first, nextTriangle);
      assert.ok(face.last >= face.first - 1);
      nextTriangle = face.last + 1;
      assert.equal(face.bodyId, mesh.bodyId);
      assert.ok(['resolved', 'ambiguous_or_missing'].includes(face.assemblyInstance.status));
      assert.ok(['complete', 'partial'].includes(face.adjacency.status));
      assert.ok(['complete', 'partial'].includes(face.trims.status));
      assert.equal(face.placement3x4.length, 12);
      const positions = mesh.attributes.position.array;
      assert.equal(face.precision?.status, 'available', 'native precision diagnostics must be exported');
      for (const key of ['faceTolerance', 'maxEdgeTolerance', 'maxVertexTolerance', 'maxTopologyTolerance', 'triangulationDeflection', 'maxNodeSurfaceDeviation'])
        assert.ok(Number.isFinite(face.precision[key]) && face.precision[key] >= 0, key);
      assert.equal(face.toleranceBounds?.method, 'exact-bounds-expanded-by-native-topology-tolerance');
      const maxTolerance = Math.max(face.precision.faceTolerance, face.precision.maxEdgeTolerance, face.precision.maxVertexTolerance);
      near(face.precision.maxTopologyTolerance, maxTolerance, 1e-14);
      for (let axis = 0; axis < 3; axis++) {
        near(face.toleranceBounds.min[axis], face.bounds.min[axis] - maxTolerance, 1e-10);
        near(face.toleranceBounds.max[axis], face.bounds.max[axis] + maxTolerance, 1e-10);
      }
      const tolerance = face.precision.maxTopologyTolerance + 1e-5;
      assert.ok(face.precision.maxNodeSurfaceDeviation <= tolerance + face.precision.triangulationDeflection,
        `${face.faceId}: node/UV discrepancy exceeds native topology tolerance plus stored mesh deflection`);
      for (let t = face.first; t <= face.last; t++) {
        if (face.support.type === 'plane') {
          const vertices = [0, 1, 2].map(corner => {
            const offset = mesh.index.array[t * 3 + corner] * 3;
            return positions.slice(offset, offset + 3);
          });
          const normal = cross(vertices[1].map((v, i) => v - vertices[0][i]),
            vertices[2].map((v, i) => v - vertices[0][i]));
          if (Math.hypot(...normal) > 1e-10) assert.ok(dot(normal, face.support.orientedNormal) > 0,
            'plane normal must agree with its oriented triangle range');
        }
        for (let corner = 0; corner < 3; corner++) {
          const index = mesh.index.array[t * 3 + corner];
          const p = positions.slice(index * 3, index * 3 + 3);
          p.forEach((v, axis) => assert.ok(v >= face.toleranceBounds.min[axis] - 1e-5 && v <= face.toleranceBounds.max[axis] + 1e-5, `${face.faceId}: node outside native tolerance bounds`));
          if (face.support.type === 'plane') near(dot(p.map((v, i) => v - face.support.origin[i]), face.support.axis), 0, tolerance);
          if (face.support.type === 'cylinder') {
            const delta = p.map((v, i) => v - face.support.origin[i]);
            near(Math.sqrt(Math.max(0, dot(delta, delta) - dot(delta, face.support.axis) ** 2)), face.support.radius, tolerance);
          }
        }
      }
    }
    assert.equal(nextTriangle * 3, mesh.index.array.length);
  }
  return ids;
}
(async () => {
  const diagnostics = [];
  const api = await require(candidate)({ wasmBinary: fs.readFileSync(candidate.replace(/\.js$/, '.wasm')), print: text => diagnostics.push(text), printErr: text => diagnostics.push(text) });
  for (const name of ['ReadStepFile', 'ReadIgesFile', 'ReadBrepFile']) assert.equal(typeof api[name], 'function');
  const cube = api.ReadStepFile(fs.readFileSync(path.join(fixtures, 'cube-units/cube-mm.step')), null);
  assert.equal(cube.success, true);
  assert.equal(cube.kernelProvenance?.schema, 'MalievKernelFaces.v1', 'RED: shipped kernel has no direct face export');
  const ids = verify(cube);
  for (const mesh of cube.meshes) {
    for (const face of mesh.brep_faces) {
      assert.equal(face.support.type, 'plane');
      assert.equal(face.bodyId, mesh.bodyId);
      assert.ok(face.last >= face.first);
      assert.equal(face.trims.status, 'complete');
      assert.equal(face.support.coordinateSpace, 'import-world');
      assert.equal(face.orientationStatus, 'resolved');
      face.bounds.min.forEach(v => assert.ok(v >= -1e-5 && v <= 1000 + 1e-5));
      face.bounds.max.forEach(v => assert.ok(v >= -1e-5 && v <= 1000 + 1e-5));
    }
  }
  assert.equal(ids.size, 6);
  for (const fixture of ['cube-m.step', 'cube-in.step']) {
    const normalized = api.ReadStepFile(fs.readFileSync(path.join(fixtures, 'cube-units', fixture)), { linearUnit: 'millimeter' });
    verify(normalized);
    assert.equal(normalized.kernelProvenance.millimeterOutput, true);
    const max = Math.max(...normalized.meshes.flatMap(m => m.brep_faces.flatMap(f => f.bounds.max)));
    near(max, 1000);
  }
  const nonMm = api.ReadStepFile(fs.readFileSync(path.join(fixtures, 'cube-units/cube-mm.step')), { linearUnit: 'meter' });
  assert.equal(nonMm.kernelProvenance.millimeterOutput, false);
  near(Math.max(...nonMm.meshes.flatMap(m => m.brep_faces.flatMap(f => f.bounds.max))), 1);
  const round = api.ReadStepFile(fs.readFileSync(path.join(repoFixtures, 'Cnc/round-disc-23x23x5.step')), null);
  verify(round);
  // This fixture is FACETED_BREP: its round silhouette is not an analytic cylinder.
  assert.ok(round.meshes.every(m => m.brep_faces.every(f => f.support.type === 'plane')));
  const analyticRound = api.ReadStepFile(fs.readFileSync(path.join(fixtures, 'conical-surface/conical-surface.step')), null);
  verify(analyticRound);
  assert.ok(analyticRound.meshes.some(m => m.brep_faces.some(f => f.support.type === 'cylinder')));
  const bracket = api.ReadStepFile(fs.readFileSync(path.join(repoFixtures, 'Cnc/counterbore-pocket-bracket.step')), null);
  verify(bracket);
  const imprecisePlane = bracket.meshes[0].brep_faces[42];
  near(imprecisePlane.bounds.min[0], -39.5);
  near(imprecisePlane.bounds.max[0], -39.5);
  near(imprecisePlane.precision.maxVertexTolerance, 0.0027718602101571674, 1e-10);
  assert.ok(imprecisePlane.precision.maxNodeSurfaceDeviation > 0.0022);
  const sourceVertex = bracket.meshes[0].kernelTopology.vertices.find(v => v.vertexId === 'body-0/vertex-107');
  near(sourceVertex.worldPoint[0], -39.4977954545967, 1e-10);
  assert.ok(imprecisePlane.trims.wires.flatMap(w => w.coedges).some(e => e.startVertexId === sourceVertex.vertexId || e.endVertexId === sourceVertex.vertexId));
  // A mismatched support or an omitted tolerance envelope must still fail.
  const wrongSupport = structuredClone(cube);
  wrongSupport.meshes[0].brep_faces[0].support.origin = wrongSupport.meshes[0].brep_faces[0].support.origin.map(
    (v, i) => v + wrongSupport.meshes[0].brep_faces[0].support.axis[i]);
  assert.throws(() => verify(wrongSupport));
  const hiddenTolerance = structuredClone(bracket);
  hiddenTolerance.meshes[0].brep_faces[42].toleranceBounds = structuredClone(imprecisePlane.bounds);
  hiddenTolerance.meshes[0].brep_faces[42].toleranceBounds.method = imprecisePlane.toleranceBounds.method;
  assert.throws(() => verify(hiddenTolerance));
  const samePlanes = new Map();
  for (const face of bracket.meshes.flatMap(m => m.brep_faces)) {
    if (face.support.type !== 'plane') continue;
    const n = face.support.axis;
    const sign = n.find(v => Math.abs(v) > 1e-8) < 0 ? -1 : 1;
    const key = [...n.map(v => v * sign), dot(n, face.support.origin) * sign].map(v => (Math.abs(v) < 1e-5 ? 0 : v).toFixed(5)).join(',');
    samePlanes.set(key, [...(samePlanes.get(key) || []), face.faceId]);
  }
  assert.ok([...samePlanes.values()].some(list => list.length > 1), 'fixture must exercise duplicate geometric plane support with distinct direct identities');
  const beforeBad = diagnostics.length;
  const bad = api.ReadStepFile(new Uint8Array([1, 2, 3]), null);
  assert.ok(diagnostics.slice(beforeBad).some(line => /error|syntax/i.test(line)), 'expected invalid STEP diagnostic is captured');
  const brepBytes = fs.readFileSync(path.join(fixtures, 'cax-if-brep/as1_pe_203.brep'));
  for (const params of [null, { linearUnit: 'millimeter' }, { linearUnit: 'meter' }]) {
    const brep = api.ReadBrepFile(brepBytes, params);
    assert.equal(brep.success, true);
    assert.equal(brep.kernelProvenance.millimeterOutput, null);
    assert.equal(brep.kernelProvenance.unitsStatus, 'unverified');
  }
  // BREP serializes origin, axis, X and Y independently. Reverse only the
  // reference axis: X/Y parameterization and geometry stay intact while the
  // frame becomes indirect (GeomTools_SurfaceSet gp_Ax3 reader).
  const brepLines = brepBytes.toString('utf8').split(/\r?\n/);
  const surfaceStart = brepLines.findIndex(line => /^Surfaces \d+$/.test(line));
  assert.ok(surfaceStart >= 0);
  let indirectPlanes = 0;
  for (let i = surfaceStart + 1; i < brepLines.length; i++) {
    if (/^TShapes /.test(brepLines[i])) break;
    const parts = brepLines[i].trim().split(/\s+/);
    if (parts.length !== 13 || parts[0] !== '1' || !parts.every(p => Number.isFinite(Number(p)))) continue;
    for (let axis = 4; axis <= 6; axis++) parts[axis] = String(-Number(parts[axis]));
    brepLines[i] = parts.join(' ');
    indirectPlanes++;
  }
  assert.ok(indirectPlanes > 0);
  const indirect = api.ReadBrepFile(Buffer.from(brepLines.join('\n')), null);
  assert.equal(indirect.success, true);
  verify(indirect);
  assert.ok(indirect.meshes.some(m => m.brep_faces.some(f => f.support.type === 'plane' && f.support.direct === false)));
  assert.equal(bad.success, false);
  assert.equal(bad.kernelProvenance, undefined);
  console.log('PASS: direct face identity, complete triangle ranges, numeric plane/cylinder support, bounds, unit normalization, duplicate supports and invalid import');
})().catch(error => { console.error(error); process.exitCode = 1; });
