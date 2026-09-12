const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
(async () => {
  const candidate = path.resolve(process.argv[2]);
  const fixtures = path.resolve(process.argv[3]);
  const api = await require(candidate)({ wasmBinary: fs.readFileSync(candidate.replace(/\.js$/, '.wasm')) });
  const source = fs.readFileSync(path.join(fixtures, 'cube-units/cube-mm.step'), 'utf8');
  const result = api.ReadStepFile(Buffer.from(source), null);
  assert.equal(result.success, true);
  assert.equal(result.meshes.length, 1);
  const mesh = result.meshes[0], body = mesh.kernelBody;
  assert.equal(body?.schema, 'MalievKernelBody.v1', 'native source body evidence required');
  assert.equal(body.sourceKind, 'solid');
  assert.equal(body.membershipStatus, 'complete');
  assert.equal(body.validity.isValid, true);
  assert.equal(body.infinitePointState, 'OUT');
  assert.equal(body.boundedSolidEvidence, true);
  assert.equal(body.shells.length, 1);
  assert.equal(body.shells[0].closureCheck, 0);
  assert.equal(body.shells[0].orientationCheck, 0);
  assert.deepEqual([...body.shells[0].faceIds].sort(), mesh.brep_faces.map(f => f.faceId).sort());
  for (const face of mesh.brep_faces) {
    assert.equal(face.membershipStatus, 'complete');
    assert.equal(face.solidId, body.bodyId);
    assert.deepEqual(face.shellMemberships, [body.shells[0].shellId]);
  }
  assert.equal(result.kernelProvenance.completeCadDocument, true, 'a fully resolved one-leaf assembly wrapper has complete bounded coverage');
  const shell = source.replace("ADVANCED_BREP_SHAPE_REPRESENTATION('',(#11,#33),#363)",
    "MANIFOLD_SURFACE_SHAPE_REPRESENTATION('',(#11,#33),#363)")
    .replace("MANIFOLD_SOLID_BREP('',#34)", "SHELL_BASED_SURFACE_MODEL('',(#34))");
  assert.notEqual(shell, source, 'pinned fixture transformation must apply');
  const open = shell.replace("CLOSED_SHELL('',(#35,#155,#255,#302,#349,#356))",
    "OPEN_SHELL('',(#35,#155,#255,#302,#349))");
  assert.notEqual(open, shell);
  for (const [text, count] of [[shell, 6], [open, 5]]) {
    const parsed = api.ReadStepFile(Buffer.from(text), null);
    assert.equal(parsed.success, true);
    assert.equal(parsed.meshes.length, 1);
    const m = parsed.meshes[0];
    assert.equal(m.brep_faces.length, count);
    assert.equal(m.kernelBody.sourceKind, 'standalone-shell');
    assert.equal(m.kernelBody.membershipStatus, 'complete');
    assert.equal(m.kernelBody.boundedSolidEvidence, false, 'even a closed shell is not a solid');
    assert.equal(parsed.kernelProvenance.completeCadDocument, false);
    if (count === 5) assert.notEqual(m.kernelBody.shells[0].closureCheck, 0);
    for (const face of m.brep_faces) assert.equal(face.solidId, null);
  }
  const brokenSolid = source.replace("CLOSED_SHELL('',(#35,#155,#255,#302,#349,#356))",
    "CLOSED_SHELL('',(#35,#155,#255,#302,#349))");
  const broken = api.ReadStepFile(Buffer.from(brokenSolid), null);
  assert.equal(broken.success, true);
  assert.equal(broken.meshes[0].brep_faces.length, 5);
  assert.equal(broken.meshes[0].kernelBody.boundedSolidEvidence, false, 'open boundary cannot certify a bounded solid');
  console.log('PASS: native solid/shell kind, open boundary rejection, shell membership and kernel checks');
})().catch(error => { console.error(error); process.exitCode = 1; });
