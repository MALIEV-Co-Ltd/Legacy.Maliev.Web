const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
(async () => {
  const api = await require(path.resolve(process.argv[2]))();
  const upstreamFixtures = path.resolve(process.argv[3]);
  const cncFixtures = process.argv[6] ? path.resolve(process.argv[6]) :
    path.resolve(__dirname, '../../Maliev.Web.Tests/TestAssets/Cnc');
  const bytes = fs.readFileSync(path.join(cncFixtures, 'box-20x30x40.step'));
  const options = { linearUnit: 'millimeter', repairErrorBudgetMm: .01, repairDiagnostics: false };
  const imported = api.ReadStepFile(bytes, options);
  assert.equal(imported.success, true);
  const contract = imported.kernelProvenance.nativeInterpretation;
  assert.ok(contract, 'actual compact Export must provide native interpretation independently of diagnostics');
  assert.equal(contract.schema, 'MalievNativeInterpretation.v1');
  assert.equal(contract.status, 'native-interpreted');
  assert.equal(contract.guarantee, 'trusted-native-interpretation');
  assert.equal(contract.policy.errorBudgetMm, .01);
  assert.equal(contract.metrics.required, 24);
  assert.equal(contract.metrics.bounded, 24);
  assert.equal(contract.metrics.unassessed, 0);
  assert.equal(contract.nativeChecks.required, 48);
  assert.equal(contract.nativeChecks.passed, 48);
  assert.equal(contract.nativeChecks.failed, 0);
  assert.equal(contract.nativeChecks.unavailable, 0);
  assert.equal(contract.nativeChecks.notEvaluated, 0);
  assert.equal(contract.faces.length, 6);
  assert.equal(contract.sourceBounds.length, 6);
  assert.equal(new Set(contract.faces.map(face => face.faceId)).size, 6);
  assert.equal(new Set(contract.sourceBounds.map(bound => bound.sourceBoundOccurrenceId)).size, 6);
  assert.ok(contract.faces.every(face => face.status === 'checked' && face.sourceOccurrenceId && face.bodyId));
  assert.ok(contract.sourceBounds.every(bound => bound.faceId && bound.wireId));
  assert.ok(contract.sourceBounds.every(bound =>
    bound.coedgeAssociationStatus === 'complete-bidirectional-occurrence-join' &&
    bound.coedgeIds.length > 0 && bound.edgeIds.length > 0));
  assert.deepEqual(new Set(contract.effects.map(effect => effect.kind)), new Set(['unchanged']));
  assert.ok(contract.effects.every(effect => effect.status === 'checked' && effect.faceId &&
    effect.joinStatus === 'complete' && effect.obligationIds.length > 0 &&
    effect.coedgeIds.length > 0 && effect.nativeCheckIds.length > 0));
  assert.ok(contract.metrics.rows.every(row => row.pathTermsMm.length > 0 &&
    row.pathTermsMm.every(term => term.termId && term.kind !== 'explicit-kind-unavailable' &&
      Number.isFinite(term.upperBoundMm)) && Number.isFinite(row.upperBoundMm) &&
    row.referencePath !== 'not-applicable' && row.metricMethod !== 'not-applicable' &&
    row.sourceFaceItemId && row.sourceEdgeItemId && row.faceId && row.wireId &&
    row.coedgeId && row.edgeId &&
    row.associationStatus === 'complete-unique-native-occurrence' &&
    Number.isFinite(row.postRangeFirst) && Number.isFinite(row.postRangeLast) &&
    row.sourceRange && Number.isFinite(row.sourceRange.first) &&
    Number.isFinite(row.sourceRange.last) &&
    row.rangeIdentityStatus === 'source-and-post-ranges-captured'));
  assert.ok(contract.nativeChecks.rows.every(row => row.subjectKind &&
    row.subjectIdentityStatus === 'complete' && row.subjectIds.length === 1 &&
    row.offendingEdgeIdentityStatus === 'not-applicable'));
  assert.equal(contract.manufacturingEligibility, 'not-assessed');
  assert.equal(Object.hasOwn(contract, 'eligible'), false);
  assert.equal(contract.diagnostics.required, 0);
  assert.equal(contract.diagnostics.unclassified, 0);
  assert.equal(contract.diagnostics.sourceIdentityFailures, 0);
  assert.equal(contract.diagnostics.captureFailures, 0);

  const multiWireBytes = fs.readFileSync(path.join(
    cncFixtures, 'counterbore-pocket-bracket.step'));
  const multiWire = api.ReadStepFile(multiWireBytes, options).kernelProvenance.nativeInterpretation;
  const boundsPerFace = new Map();
  for (const bound of multiWire.sourceBounds)
    boundsPerFace.set(bound.faceId, (boundsPerFace.get(bound.faceId) || 0) + 1);
  assert.ok([...boundsPerFace.values()].some(count => count > 1),
    'actual compact export must distinguish multiple wires on one face');
  assert.equal(new Set(multiWire.sourceBounds.map(bound => bound.wireId)).size,
    multiWire.sourceBounds.length);
  assert.ok(multiWire.nativeChecks.rows.every(row =>
    row.subjectKind !== 'wire' || row.subjectIds.length === 1));

  if (process.argv[4]) {
    const periodicBytes = fs.readFileSync(path.resolve(process.argv[4]));
    const periodicSuccess = api.ReadStepFile(periodicBytes, options)
      .kernelProvenance.nativeInterpretation;
    assert.equal(periodicSuccess.status,
      'native-interpreted-with-bounded-repair');
    assert.ok(periodicSuccess.periodicMetrics.required > 0);
    assert.equal(periodicSuccess.periodicMetrics.bounded,
      periodicSuccess.periodicMetrics.required);
    assert.ok(periodicSuccess.periodicMetrics.rows.every(row =>
      row.status === 'bounded' && row.operationId && row.faceId &&
      row.sourceFaceItemId &&
      row.associationStatus === 'complete-periodic-operation-face-association' &&
      row.referencePath === 'periodic-local-surface-through-native-face-placement' &&
      row.metricMethod === 'native-common-parameter-rational-polynomial-v1+native-placement-scale' &&
      row.pathTermsMm.length === 1 &&
      row.pathTermsMm[0].kind === 'periodic-world-displacement' &&
      Number.isFinite(row.pathTermsMm[0].upperBoundMm)));
    const deadline = api.ReadStepFile(periodicBytes, {
      ...options, repairAssessmentTimeLimitMs: 0
    }).kernelProvenance.nativeInterpretation;
    assert.ok(deadline.periodicConversions.length > 0,
      'deadline regression fixture must exercise actual periodic export');
    assert.ok(deadline.metrics.unassessed > 0);
    assert.equal(deadline.metrics.required,
      deadline.metrics.evaluated + deadline.metrics.unassessed);
    assert.ok(deadline.metrics.rows.some(row => row.status === 'unassessed' &&
      row.reason === 'assessment-monotonic-time-limit'));
    assert.equal(deadline.metrics.rows.some(row => row.status === 'unavailable' &&
      row.reason === 'periodic world-placement upper bound unavailable'), false,
      'periodic serialization must not overwrite skipped residual state');
  }
  if (process.argv[5]) {
    const changedBytes = fs.readFileSync(path.resolve(process.argv[5]));
    const changedContract = api.ReadStepFile(changedBytes, options)
      .kernelProvenance.nativeInterpretation;
    const routeKinds = new Set(['source-null-pcurve-construction',
      'bounded-existing-pcurve-replacement', 'bounded-endpoint-range-adjustment']);
    const changedEffects = changedContract.effects.filter(effect => routeKinds.has(effect.kind));
    assert.ok(changedEffects.length > 0,
      'changed-route fixture must exercise an actual compact pcurve/range effect');
    assert.ok(changedEffects.every(effect => effect.joinStatus === 'complete' &&
      effect.obligationIds.length === 1 && effect.coedgeIds.length === 1 &&
      effect.nativeCheckIds.length > 0));
    assert.ok(changedContract.metrics.rows.some(row =>
      row.referencePath === 'old-lift-via-source-3d-to-new-lift' ||
      row.referencePath === 'post-lift-to-unchanged-source-3d-plus-endpoint-sliver'));
  }

  const belowObservedTolerance = api.ReadStepFile(bytes, {
    ...options,
    repairErrorBudgetMm: 1e-8
  }).kernelProvenance.nativeInterpretation;
  assert.equal(belowObservedTolerance.status, 'review-required');
  assert.equal(belowObservedTolerance.tolerances.exceeds, belowObservedTolerance.tolerances.required);
  assert.ok(belowObservedTolerance.reasons.includes('observed-tolerances-incomplete-or-excessive'));

  const belowMetricPath = api.ReadStepFile(bytes, {
    ...options,
    repairErrorBudgetMm: 1e-15
  }).kernelProvenance.nativeInterpretation;
  assert.equal(belowMetricPath.status, 'review-required');
  assert.ok(belowMetricPath.metrics.unavailable > 0);
  assert.ok(belowMetricPath.reasons.includes('metric-obligations-incomplete'));
  assert.ok(belowMetricPath.reasons.includes('composed-metric-paths-incomplete'));

  assert.equal(api.ReadStepFile(bytes, { linearUnit: 'millimeter' }).kernelProvenance.nativeInterpretation.status, 'not-requested');
  for (const repairErrorBudgetMm of [-.01, Number.NaN]) {
    const notRequested = api.ReadStepFile(bytes, {
      linearUnit: 'millimeter', repairErrorBudgetMm
    }).kernelProvenance.nativeInterpretation;
    assert.equal(notRequested.status, 'not-requested');
    assert.equal(notRequested.policy.errorBudgetMm, null);
    assert.equal(Object.hasOwn(notRequested, 'metrics'), false);
  }

  const igesBytes = fs.readFileSync(path.join(upstreamFixtures, 'cube-10x10mm/Cube 10x10.igs'));
  const iges = api.ReadIgesFile(igesBytes, options).kernelProvenance.nativeInterpretation;
  assert.equal(iges.status, 'not-requested');
  assert.equal(iges.policy.errorBudgetMm, null);
  assert.equal(Object.hasOwn(iges, 'metrics'), false);

  assert.equal(api.ReadStepFile(Buffer.from('malformed'), options).success, false);
  const again = api.ReadStepFile(bytes, options).kernelProvenance.nativeInterpretation;
  assert.equal(again.status, 'native-interpreted');
  assert.notEqual(again.binding.nativeInvocationId, contract.binding.nativeInvocationId);
  assert.equal(again.diagnostics.required, 0);
  console.log('PASS: actual compact native interpretation Export, unchanged box, complete ledgers and reset');
})().catch(error => { console.error(error); process.exitCode = 1; });
