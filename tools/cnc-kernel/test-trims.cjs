// Candidate runtime topology gate. No triangulation is used to reconstruct trims.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const near = (a, b, tolerance = 1e-4) => a.forEach((v, i) => assert.ok(Math.abs(v - b[i]) <= tolerance, `${a} != ${b}`));
const affine = (m, p) => [0, 1, 2].map(i => m[i * 4 + 3] + p.reduce((s, x, j) => s + m[i * 4 + j] * x, 0));
function curvePoint(curve, t) {
  if (curve.type === 'line') return curve.origin.map((v, i) => v + t * curve.direction[i]);
  if (curve.type === 'circle' || curve.type === 'ellipse') {
    const x = (curve.radius ?? curve.majorRadius) * Math.cos(t);
    const y = (curve.radius ?? curve.minorRadius) * Math.sin(t);
    return curve.origin.map((v, i) => v + x * curve.xDirection[i] + y * curve.yDirection[i]);
  }
  return null;
}
function surfacePoint(surface, uv) {
  const [u, v] = uv;
  let x, y, z;
  if (surface.type === 'plane') [x, y, z] = [u, v, 0];
  else if (surface.type === 'cylinder') [x, y, z] = [surface.radius * Math.cos(u), surface.radius * Math.sin(u), v];
  // Native parameterization follows pinned OCCT ElSLib::*D0, not mesh fitting.
  else if (surface.type === 'cone') {
    const r = surface.referenceRadius + v * Math.sin(surface.semiAngleRadians);
    [x, y, z] = [r * Math.cos(u), r * Math.sin(u), v * Math.cos(surface.semiAngleRadians)];
  } else if (surface.type === 'sphere') {
    const r = surface.radius * Math.cos(v);
    [x, y, z] = [r * Math.cos(u), r * Math.sin(u), surface.radius * Math.sin(v)];
  } else if (surface.type === 'torus') {
    const r = surface.majorRadius + surface.minorRadius * Math.cos(v);
    [x, y, z] = [r * Math.cos(u), r * Math.sin(u), surface.minorRadius * Math.sin(v)];
  }
  else return null;
  return surface.origin.map((p, i) => p + x * surface.xDirection[i] + y * surface.yDirection[i] + z * surface.axis[i]);
}
function verifyPlacement(result) {
  let checked = 0;
  const coverage = {};
  for (const mesh of result.meshes) {
    const edges = new Map(mesh.kernelTopology.edges.map(e => [e.edgeId, e]));
    const vertices = new Map(mesh.kernelTopology.vertices.map(v => [v.vertexId, v]));
    for (const face of mesh.brep_faces) for (const wire of face.trims.wires) for (const use of wire.coedges) {
      if (use.status !== 'complete') continue;
      const edge = edges.get(use.edgeId);
      const [a, b] = use.orientation === 1 ? [...use.range].reverse() : use.range;
      for (const [t, id] of [[a, use.startVertexId], [b, use.endVertexId]]) {
        const uv = curvePoint(use.pcurve, t);
        const p = uv && surfacePoint(face.trims.surfaceBasis, uv);
        if (p) {
          near(affine(face.trims.surfaceToImportWorld3x4, p), vertices.get(id).worldPoint, Math.max(1e-4, edge.tolerance * 5)); checked++;
          const kind = face.trims.surfaceBasis.type;
          coverage[kind] = (coverage[kind] || 0) + 1;
        }
      }
      if (edge.sameParameter && !edge.degenerated) {
        const t = (a + b) / 2, uv = curvePoint(use.pcurve, t), local3d = curvePoint(edge.curve3d, t);
        const localSurface = uv && surfacePoint(face.trims.surfaceBasis, uv);
        if (localSurface && local3d) near(affine(face.trims.surfaceToImportWorld3x4, localSurface), affine(edge.curveToImportWorld3x4, local3d), Math.max(1e-4, edge.tolerance * 5));
      }
    }
  }
  assert.ok(checked > 0, 'native curve/surface placement must have numeric coverage');
  return coverage;
}
(async () => {
  const candidate = path.resolve(process.argv[2]);
  const fixtures = path.resolve(process.argv[3]);
  const api = await require(candidate)({ wasmBinary: fs.readFileSync(candidate.replace(/\.js$/, '.wasm')) });
  const result = api.ReadStepFile(fs.readFileSync(path.join(fixtures, 'cube-units/cube-mm.step')), null);
  assert.equal(result.meshes[0].kernelTopology?.schema, 'MalievKernelTopology.v1', 'RED: ordered native trim export absent');
  for (const mesh of result.meshes) {
    const topology = mesh.kernelTopology;
    assert.equal(topology.status, 'complete');
    assert.equal(topology.edges.length, 12);
    assert.equal(topology.vertices.length, 8);
    const edges = new Map(topology.edges.map(e => [e.edgeId, e]));
    const vertices = new Map(topology.vertices.map(v => [v.vertexId, v]));
    for (const vertex of vertices.values()) {
      assert.equal(vertex.status, 'complete');
      assert.equal(vertex.worldPoint.length, 3);
    }
    for (const edge of edges.values()) {
      assert.equal(edge.adjacency, 'two-face');
      assert.equal(edge.uses.length, 2);
      assert.equal(edge.curve3d.type, 'line');
      assert.equal(edge.curveToImportWorld3x4.length, 12);
    }
    for (const face of mesh.brep_faces) {
      assert.equal(face.trims.status, 'complete');
      assert.equal(face.trims.surfaceBasis.type, 'plane');
      assert.equal(face.trims.wires.length, 1);
      for (const wire of face.trims.wires) {
        assert.equal(wire.role, 'outer');
        assert.equal(wire.complete, true);
        assert.equal(wire.coedges.length, 4);
        wire.coedges.forEach((use, i) => {
          assert.equal(use.pcurve.status, 'exact');
          assert.equal(use.pcurve.type, 'line');
          assert.ok(edges.has(use.edgeId));
          assert.ok(vertices.has(use.startVertexId));
          assert.equal(use.endVertexId, wire.coedges[(i + 1) % 4].startVertexId);
          assert.ok(edges.get(use.edgeId).uses.some(u => u.coedgeId === use.coedgeId && u.faceId === face.faceId));
        });
      }
    }
  }
  assert.equal(result.kernelProvenance.completeCadDocument, true, 'the cube has one fully covered resolved solid occurrence');
  assert.equal(result.kernelProvenance.documentCoverage.faceCoverageStatus, 'complete');
  verifyPlacement(result);
  const cone = api.ReadStepFile(fs.readFileSync(path.join(fixtures, 'conical-surface/conical-surface.step')), null);
  assert.ok(verifyPlacement(cone).cone > 0, 'cone parameterization must have numeric coverage');
  for (const [fixture, unit] of [['cube-m.step', 'millimeter'], ['cube-in.step', 'millimeter'], ['cube-mm.step', 'meter']]) {
    verifyPlacement(api.ReadStepFile(fs.readFileSync(path.join(fixtures, 'cube-units', fixture)), { linearUnit: unit }));
  }
  const publicBrep = api.ReadBrepFile(fs.readFileSync(path.join(fixtures, 'cax-if-brep/as1_pe_203.brep')), null);
  verifyPlacement(publicBrep);
  const faces = publicBrep.meshes.flatMap(m => m.brep_faces);
  assert.ok(faces.some(f => f.trims.wires.some(w => w.role === 'inner')), 'public fixture must exercise inner trims');
  // The assembly's cylinders are split into two faces; use the unsplit cylinders
  // in the conical fixture to exercise a genuine same-face periodic seam.
  const seamFaces = cone.meshes.flatMap(m => m.brep_faces);
  const seams = cone.meshes.flatMap(m => m.kernelTopology.edges).filter(e => e.adjacency === 'same-face-seam'
    && seamFaces.some(f => f.faceId === e.uses[0].faceId && f.trims.status === 'complete' && f.trims.surfaceBasis.type === 'cylinder'));
  assert.ok(seams.length > 0, 'public fixture must exercise periodic seam edge identity');
  for (const edge of seams) {
    assert.equal(edge.uses.length, 2);
    assert.equal(edge.uses[0].faceId, edge.uses[1].faceId);
    assert.notEqual(edge.uses[0].coedgeId, edge.uses[1].coedgeId);
    assert.deepEqual(edge.uses.map(use => use.orientation).sort(), [0, 1],
      'a seam requires opposite native coedge orientations');
    const face = seamFaces.find(f => f.faceId === edge.uses[0].faceId);
    const uses = face.trims.wires.flatMap(w => w.coedges).filter(u => u.edgeId === edge.edgeId);
    assert.equal(uses.length, 2);
    assert.notDeepEqual(uses[0].pcurve, uses[1].pcurve, 'orientation must select distinct seam pcurves');
  }
  console.log('PASS: native cube/inner trims, seam identity, shared edges, vertices, ordered coedges and numeric curve/surface placements');
})().catch(error => { console.error(error); process.exitCode = 1; });
