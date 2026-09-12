const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
(async () => {
  const candidate = path.resolve(process.argv[2]);
  const api = await require(candidate)();
  const bytes = fs.readFileSync(path.resolve(__dirname, '../../Maliev.Web.Tests/TestAssets/Cnc/box-20x30x40.step'));
  const options = { linearUnit: 'millimeter', linearDeflectionType: 'absolute_value', linearDeflection: .1 };
  const read = extra => api.ReadStepFile(bytes, { ...options, ...extra });
  const baseline = read({});
  assert.equal(baseline.success, true);
  assert.equal(baseline.kernelProvenance.repairAssessment.status, 'not-requested');
  const diagnostic = read({ repairErrorBudgetMm: .01, repairDiagnostics: true });
  const repair = diagnostic.kernelProvenance.repairAssessment;
  assert.equal(diagnostic.success, true);
  assert.equal(repair.errorBudgetMm, .01);
  assert.equal(repair.eligible, true);
  assert.equal(repair.status, 'verified-unchanged-or-tolerance-only');
  assert.ok(repair.boundaries.length > 0);
  for (const boundary of repair.boundaries) {
    assert.equal(boundary.changedSourceGeometryCount, 0);
    assert.equal(boundary.exceededResidualCount, 0);
    assert.equal(boundary.unavailableResidualCount, 0);
    assert.ok(boundary.items.length > 0);
    for (const residual of boundary.residuals) {
      assert.ok(Number.isInteger(residual.nativeFaceId));
      assert.ok(Number.isInteger(residual.nativeEdgeId));
      assert.ok(residual.upperBoundMm >= residual.lowerBoundMm);
      assert.ok(residual.upperBoundMm <= .01);
    }
  }
  const compact = read({ repairErrorBudgetMm: .01 });
  assert.deepEqual(compact.meshes, baseline.meshes);
  for (const boundary of compact.kernelProvenance.repairAssessment.boundaries) {
    assert.equal(boundary.items, undefined);
    assert.equal(boundary.residuals, undefined);
  }
  const reset = read({});
  assert.equal(reset.kernelProvenance.repairAssessment.status, 'not-requested');
  assert.equal(reset.kernelProvenance.repairAssessment.boundaries.length, 0);
  for (const budget of [0, -1, NaN, Infinity])
    assert.equal(read({ repairErrorBudgetMm: budget }).kernelProvenance.repairAssessment.status, 'not-requested');
  assert.equal(api.ReadStepFile(Buffer.from('malformed'), options).success, false);
  console.log('PASS: native repair no-warning positive, unchanged geometry/mesh, compact/diagnostic boundary, invalid policy, reset and malformed import');
  console.log(JSON.stringify({ repairBytes: {
    default: Buffer.byteLength(JSON.stringify(baseline.kernelProvenance.repairAssessment)),
    compact: Buffer.byteLength(JSON.stringify(compact.kernelProvenance.repairAssessment)),
    diagnostic: Buffer.byteLength(JSON.stringify(repair))
  }}));
})().catch(error => { console.error(error); process.exitCode = 1; });
