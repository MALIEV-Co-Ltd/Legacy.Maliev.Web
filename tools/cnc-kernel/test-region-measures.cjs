const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
(async () => {
  const candidate = path.resolve(process.argv[2]);
  const fixtures = path.resolve(process.argv[3]);
  const api = await require(candidate)({ wasmBinary: fs.readFileSync(candidate.replace(/\.js$/, '.wasm')) });
  const bytes = fs.readFileSync(path.join(fixtures, 'cube-units/cube-mm.step'));
  function partition(parsed) {
    assert.equal(parsed.success, true);
    const ids = new Set();
    for (const mesh of parsed.meshes) {
      for (const [row, owner, area] of [[mesh.kernelBody.nativeRegionMeasures, mesh, false], ...mesh.brep_faces.map(f => [f.nativeRegionMeasures, f, true])]) {
        assert.equal(row.schema, 'MalievNativeRegionMeasures.v1');
        assert.equal(row.policyVersion, 2);
        assert.equal(row.acceptanceRelativeError, .0001);
        assert.equal(row.initialRequestedRelativeError, .0001);
        const id = area ? owner.faceId : owner.bodyId;
        assert.ok(!ids.has(id)); ids.add(id);
        assert.equal(row.bodyId, mesh.bodyId);
        if (area) assert.equal(row.faceId, owner.faceId);
        assert.ok(['available', 'unavailable'].includes(row.status));
        const error = row[area ? 'estimatedRelativeAreaError' : 'estimatedRelativeVolumeError'];
        if (row.status === 'available') {
          assert.ok(Number.isFinite(row.value) && row.value > 0);
          assert.ok(row.centroidMm.length === 3 && row.centroidMm.every(Number.isFinite));
          assert.ok(Number.isFinite(error) && error >= 0 && error <= .0001);
          assert.equal(row.reason, null);
        } else {
          assert.equal(typeof row.reason, 'string'); assert.ok(row.reason.length > 0);
          assert.equal(row.value, null); assert.equal(row.centroidMm, null); assert.equal(error, null);
        }
        assert.equal(row.centroidAbsoluteErrorMm, null);
        assert.equal(row.centroidErrorStatus, 'not-estimated');
        if(!area){
          assert.ok(Array.isArray(row.integrationAttempts)&&row.integrationAttempts.length<=3);
          row.integrationAttempts.forEach((attempt,i)=>{
            assert.equal(attempt.requestedRelativeError,[.0001,.00001,.000001][i]);
            if(i<row.integrationAttempts.length-1){
              assert.equal(attempt.status,'unavailable');assert.equal(attempt.reason,'integration_target_not_met');
              assert.ok(attempt.estimatedRelativeVolumeError>.0001);
            }
          });
          if(row.integrationAttempts.length===0)assert.equal(row.requestedRelativeError,null);
          else assert.equal(row.requestedRelativeError,row.integrationAttempts.at(-1).requestedRelativeError);
          if(row.status==='available'){
            assert.ok(row.integrationAttempts.length>0);
            assert.equal(row.integrationAttempts.at(-1).estimatedRelativeVolumeError,error);
          }
        }
      }
    }
  }
  const result = api.ReadStepFile(bytes, null);
  partition(result);
  assert.equal(result.success, true);
  for (const mesh of result.meshes) {
    const measure = mesh.kernelBody.nativeRegionMeasures;
    assert.equal(measure?.status, 'available', 'same-import native volume must be available');
    assert.equal(measure.quantity, 'volume');
    assert.equal(measure.units, 'mm3');
    assert.ok(Math.abs(measure.value - 1000000000) < .01);
    for (const face of mesh.brep_faces) {
      const area = face.nativeRegionMeasures;
      assert.equal(area.status, 'available');
      assert.equal(area.faceId, face.faceId);
      assert.equal(area.bodyId, mesh.bodyId);
      assert.equal(area.units, 'mm2');
      assert.ok(Math.abs(area.value - 1000000) < .001);
      assert.equal(area.centroidErrorStatus, 'not-estimated');
      assert.equal(area.centroidAbsoluteErrorMm, null);
      assert.equal(area.coordinateSpace, 'import-world');
      assert.ok(area.estimatedRelativeAreaError <= area.requestedRelativeError);
      assert.equal(Object.hasOwn(area, 'importRevision'), false);
    }
  }
  for (const unit of ['m', 'in']) {
    const normalized = api.ReadStepFile(fs.readFileSync(path.join(fixtures, `cube-units/cube-${unit}.step`)), null);
    partition(normalized);
    assert.ok(Math.abs(normalized.meshes[0].kernelBody.nativeRegionMeasures.value - 1000000000) < .01, 'source units normalized once');
  }
  const meters = api.ReadStepFile(bytes, { linearUnit: 'meter' });
  partition(meters);
  for (const mesh of meters.meshes) {
    assert.equal(mesh.kernelBody.nativeRegionMeasures.reason, 'millimeter_units_unverified');
    for (const face of mesh.brep_faces) assert.equal(face.nativeRegionMeasures.reason, 'millimeter_units_unverified');
  }
  const brep=api.ReadBrepFile(fs.readFileSync(path.join(fixtures,'cax-if-brep/as1_pe_203.brep')), {linearUnit:'millimeter'});
  partition(brep);
  for(const mesh of brep.meshes){
    assert.equal(mesh.kernelBody.nativeRegionMeasures.status,'unavailable');
    for(const face of mesh.brep_faces)assert.equal(face.nativeRegionMeasures.reason,'millimeter_units_unverified');
  }
  const source = bytes.toString();
  const shell = source.replace("ADVANCED_BREP_SHAPE_REPRESENTATION('',(#11,#33),#363)", "MANIFOLD_SURFACE_SHAPE_REPRESENTATION('',(#11,#33),#363)")
    .replace("MANIFOLD_SOLID_BREP('',#34)", "SHELL_BASED_SURFACE_MODEL('',(#34))");
  assert.notEqual(shell, source);
  const open = shell.replace("CLOSED_SHELL('',(#35,#155,#255,#302,#349,#356))", "OPEN_SHELL('',(#35,#155,#255,#302,#349))");
  for (const text of [shell, open]) {
    const parsed = api.ReadStepFile(Buffer.from(text), null); partition(parsed);
    assert.equal(parsed.meshes[0].kernelBody.nativeRegionMeasures.status, 'unavailable');
    assert.ok(parsed.meshes[0].brep_faces.every(f => f.nativeRegionMeasures.status === 'available'));
  }
  assert.equal(api.ReadStepFile(Buffer.from('malformed'), null).success, false);
  const again=api.ReadStepFile(bytes, null);partition(again);
  assert.deepEqual(again.meshes[0].kernelBody.nativeRegionMeasures, result.meshes[0].kernelBody.nativeRegionMeasures, 'import reset does not retain earlier unavailable state');
  console.log('PASS: same-import native area and volume exporter');
})().catch(error => { console.error(error); process.exitCode = 1; });
