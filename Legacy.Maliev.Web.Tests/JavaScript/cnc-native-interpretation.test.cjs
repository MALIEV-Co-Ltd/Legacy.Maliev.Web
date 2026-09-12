const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { webcrypto, createHash } = require('node:crypto');
const workspace = path.resolve(__dirname, '../..');
const scratch = path.join(workspace, '.superpowers/sdd/2026-09-06-cnc-quotation-manufacturing-recovery');
const directory = process.env.CNC_NATIVE_INTERPRETATION_FIXTURES || path.join(scratch, 'native-rotational-band-results');
const boxPath = path.join(__dirname, '../TestAssets/CncNativeInterpretation/native-consumer-fixtures.json');
const digest = x => createHash('sha256').update(x).digest('hex');
function runtime() {
    const c = vm.createContext({ console, TextEncoder, crypto: webcrypto }); c.self = c;
    for (const name of ['cnc-plan-contracts', 'cnc-cad-document', 'cnc-native-topology.worker', 'cnc-topology.worker']) {
        vm.runInContext(fs.readFileSync(path.join(workspace, 'Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation', name + '.js'), 'utf8'), c);
    }
    return c;
}
const identity = { exporterSchema: 'MalievKernelDocument.v1', jsSha256: '0f4759e678eafaf72a85e6a46ba3cf7cd324a88f7882d64d8dffdbaed25f2f90',
    wasmSha256: '191da5c8b62d845a8585ff7d952222b823e744933d5e95fd9bd6a62a6fb866d5' };
function envelope(c, result, sourceBytesHash) {
    return { contract: 'CncNativeImport.v1', nativeSemanticBasis: c.CncNativeInterpretation.semanticBasis, analysisProfile: 'cnc', sourceFormat: 'step',
        tessellation: { ...c.CncCadDocument.importParameters }, buildIdentity: { ...identity }, sourceBytesHash,
        kernelProvenance: structuredClone(result.kernelProvenance), meshes: structuredClone(result.meshes) };
}
// Explicitly injected UNIT TEST loader expectations. These are not a published manifest,
// worker authentication, or browser acceptance. Production currently supplies no expectation.
function expectation(n) { return { sourceBytesHash: n.sourceBytesHash, sourceAttachmentVerified: true, buildIdentity: { ...identity },
    manifestUrl: '/lib/occt-cnc/0f4759e678ea-191da5c8b62d/manifest.js', manifestSha256: 'a'.repeat(64), errorBudgetMm: .01, assessmentTimeLimitMs: 60000 }; }
async function seal(c, n) { n.importRevision = await c.CncCadDocument.hash(n); return n; }
function publicEnvelope(c, variant = 'first') {
    const fixture = JSON.parse(fs.readFileSync(boxPath));
    const n = envelope(c, fixture[variant], fixture.sourceSha256);
    if (variant === 'deadline') n.tessellation.repairAssessmentTimeLimitMs = 0;
    return n;
}
let summary;
function original(c, name) {
    summary ||= JSON.parse(fs.readFileSync(path.join(directory, 'summary.json')));
    const row = summary.rows.find(r => r.file === name + '.step');
    const bytes = fs.readFileSync(path.join(directory, row.file + '.json'));
    assert.equal(digest(bytes), row.resultSha256, 'actual frozen output digest');
    assert.equal(summary.jsSha256, identity.jsSha256); assert.equal(summary.wasmSha256, identity.wasmSha256);
    return envelope(c, JSON.parse(bytes), row.sourceSha256);
}
const saved = { skip: !fs.existsSync(path.join(directory, 'summary.json')) && 'reviewed original payload directory unavailable; set CNC_NATIVE_INTERPRETATION_FIXTURES' };

test('IEEE-754 nextUp and ordered outward accumulation retain exact ULPs', () => {
    const api = runtime().CncNativeInterpretation;
    assert.equal(api.nextUp(0), Number.MIN_VALUE); assert.equal(api.nextUp(-0), Number.MIN_VALUE);
    assert.equal(api.nextUp(Number.MIN_VALUE), 2 * Number.MIN_VALUE); assert.equal(api.nextUp(-Number.MIN_VALUE), -0);
    assert.equal(api.nextUp(1), 1 + Number.EPSILON); assert.equal(api.nextUp(-1), -1 + Number.EPSILON / 2);
    assert.equal(api.nextUp(Number.MAX_VALUE), Infinity); assert.equal(api.outwardSum([.01]), .01);
    assert.equal(api.outwardSum([.01, 0]), api.nextUp(.01)); assert.throws(() => api.outwardSum([]));
});

test('actual nine originals pass contract and explicit expectation; default loader stays untrusted', saved, async () => {
    const c = runtime();
    for (const name of ['Component4', 'Component5', 'Component6', 'Cover Plate_25.4mm', 'PART 1_26082026', 'Pulley_Becket', 'Pulley_Sheave', 'ring', 'UADL150']) {
        const n = await seal(c, original(c, name));
        const assessment = c.CncNativeInterpretation.assess(n, expectation(n));
        assert.equal(assessment.validContract, true, name + ': ' + assessment.reasons.join());
        assert.equal(assessment.interpretationAccepted, true, name + ': ' + assessment.reasons.join());
        assert.equal(c.CncNativeInterpretation.assess(n).interpretationAccepted, false);
        const topology = await c.CncTopology.build({ nativeImport: n }, expectation(n));
        assert.equal(topology.nativeInterpretation.interpretationAccepted, true, name + ': ' + topology.unresolvedReasons.join());
        assert.equal(topology.automaticPlanningEligible, false);
        assert.ok(topology.unresolvedReasons.includes('native_trim_consumer_unsupported'));
        assert.ok(!topology.unresolvedReasons.includes('native_repair_review_required'));
        assert.equal(topology.cadDocument.nativeImport, n, 'raw same-import evidence retained');
        assert.equal((await c.CncTopology.build({ nativeImport: n, nativeInterpretationExpectation: expectation(n) })).nativeInterpretation.interpretationAccepted, false,
            'transported JSON cannot set the explicit caller expectation');
    }
});

test('recomputed envelopes reject policy/source/build/expectation substitutions', async () => {
    const c = runtime(), n = await seal(c, publicEnvelope(c)), trusted = expectation(n);
    const mutations = [x => x.buildIdentity.jsSha256 = 'b'.repeat(64), x => x.sourceBytesHash = 'b'.repeat(64),
        x => x.sourceFormat = 'iges', x => x.kernelProvenance.nativeInterpretation.policy.errorBudgetMm = .02,
        x => x.kernelProvenance.nativeInterpretation.policy.assessmentTimeLimitMs = 59999,
        x => x.kernelProvenance.nativeInterpretation.policy.units = 'inch', x => x.tessellation.repairErrorBudgetMm = .02,
        x => x.kernelProvenance.sourceCoverage.complete = false, x => x.kernelProvenance.nativeInterpretation.guarantee = 'sampled'];
    for (const mutate of mutations) { const bad = structuredClone(n); mutate(bad); await seal(c, bad); assert.equal(c.CncNativeInterpretation.assess(bad, trusted).interpretationAccepted, false); }
    for (const change of [{ manifestUrl: 'https://third-party.invalid/manifest.js' }, { sourceAttachmentVerified: false }, { errorBudgetMm: .02 }, { buildIdentity: {} }]) {
        assert.equal(c.CncNativeInterpretation.assess(n, { ...trusted, ...change }).interpretationAccepted, false);
    }
    const old = structuredClone(n); delete old.nativeSemanticBasis; assert.throws(() => c.CncNativeInterpretation.project(old), /semantic-basis/);
});

test('face/bound/coedge/metric/check/effect inverse mutations fail after resealing', async () => {
    const c = runtime(), n = await seal(c, publicEnvelope(c));
    const mutations = [v => v.faces.pop(), v => v.faces.push(structuredClone(v.faces[0])), v => v.faces[0].sourceOccurrenceId = v.faces[1].sourceOccurrenceId,
        v => v.sourceBounds.pop(), v => v.sourceBounds[0].boundSense = !v.sourceBounds[0].boundSense,
        v => v.sourceBounds[0].coedgeIds.pop(), v => v.sourceBounds[0].wireId = v.sourceBounds[1].wireId,
        v => v.metrics.rows[0].coedgeId = v.metrics.rows[1].coedgeId, v => v.metrics.required++, v => v.metrics.rows.pop(),
        v => v.metrics.rows[0].postRangeLast += .1, v => v.metrics.rows[0].sourceOrientation ^= 1,
        v => v.metrics.rows[0].metricMethod = 'sampled', v => v.metrics.rows[0].pathTermsMm.reverse(),
        v => v.metrics.rows[0].upperBoundMm = 0, v => v.metrics.rows[0].pathTermsMm[0].kind = 'explicit-kind-unavailable',
        v => v.nativeChecks.rows[0].subjectIds = [v.faces[1].faceId], v => v.nativeChecks.rows.pop(),
        v => v.nativeChecks.rows[0].method = v.nativeChecks.rows[1].method, v => v.effects[0].nativeCheckIds.pop(),
        v => v.effects[0].obligationIds = [v.metrics.rows.at(-1).obligationId], v => v.faces[0].effects = [],
        v => v.tolerances.maximumMm += .001, v => v.tolerances.rows[0].phase = 'final-added'];
    for (const [i, mutate] of mutations.entries()) { const bad = structuredClone(n); mutate(bad.kernelProvenance.nativeInterpretation); await seal(c, bad);
        assert.equal(c.CncNativeInterpretation.assess(bad, expectation(n)).interpretationAccepted, false, 'mutation ' + i); }
    const excessive = structuredClone(n), row = excessive.kernelProvenance.nativeInterpretation.metrics.rows[0];
    row.pathTermsMm[0].upperBoundMm = .01; row.pathTermsMm[1].upperBoundMm = 0; row.upperBoundMm = c.CncNativeInterpretation.nextUp(.01); await seal(c, excessive);
    assert.equal(c.CncNativeInterpretation.assess(excessive, expectation(n)).interpretationAccepted, false, 'one ULP above policy');
});

test('periodic bounds/domains/scale/typed composition and warning lineage are joined', saved, async () => {
    const c = runtime(), n = await seal(c, original(c, 'UADL150'));
    const mutations = [v => v.periodicConversions[0].placementScale = 2, v => v.periodicConversions[0].afterDomain[0] += 1,
        v => v.periodicMetrics.rows[0].pathTermsMm[0].upperBoundMm = 0, v => v.periodicConversions.pop(),
        v => v.metrics.rows.find(r => r.faceId === v.periodicConversions[0].faceId).pathTermsMm.pop(),
        v => v.diagnostics.rows[0].nativeDeliveryId = v.diagnostics.rows[1].nativeDeliveryId,
        v => v.diagnostics.rows[0].affectedFaceIds = v.diagnostics.rows[1].affectedFaceIds,
        v => v.diagnostics.rows[0].operationId = v.diagnostics.rows[1].operationId,
        v => v.diagnostics.rows[0].nativeDeliveryId = null, v => v.diagnostics.captureFailures = 1];
    for (const [i, mutate] of mutations.entries()) { const bad = structuredClone(n); mutate(bad.kernelProvenance.nativeInterpretation); await seal(c, bad);
        assert.equal(c.CncNativeInterpretation.assess(bad, expectation(n)).interpretationAccepted, false, 'periodic/diagnostic mutation ' + i); }
});

test('actual Component6 connector proof mutations cannot retain positive authority', saved, async () => {
    const c = runtime(), n = await seal(c, original(c, 'Component6'));
    const mutations = [v => v.connectorProofs.pop(), v => v.connectorProofs.push(structuredClone(v.connectorProofs[0])),
        v => v.connectorProofs[0].sourceIdentity = 'new-native-identity-minus-two', v => v.connectorProofs[0].sourceVertexItemId = v.connectorProofs[1].sourceVertexItemId,
        v => v.connectorProofs[0].sourceFaceItemId = v.connectorProofs[1].sourceFaceItemId, v => v.connectorProofs[0].postCoedgeOccurrence++,
        v => v.connectorProofs[0].normalizedLoopOccurrence++, v => v.connectorProofs[0].normalizedLoopCoedgeOccurrence++,
        v => v.connectorProofs[0].endVertexId = 'foreign', v => v.connectorProofs[0].metricMethod = 'point-sample',
        v => v.connectorProofs[0].nativeCheckIds.pop(), v => v.connectorProofs[0].obligationId = v.connectorProofs[1].obligationId,
        v => v.metrics.rows.find(r => r.kind === 'new-degenerate-connector').sourceRange = { first: 0, last: 0 },
        v => v.metrics.rows.find(r => r.kind === 'new-degenerate-connector').pathTermsMm[0].kind = 'endpoint-range-sliver',
        v => v.effects.find(e => e.kind === 'bounded-singular-connector').coedgeIds = [v.metrics.rows[0].coedgeId]];
    for (const [i, mutate] of mutations.entries()) { const bad = structuredClone(n); mutate(bad.kernelProvenance.nativeInterpretation); await seal(c, bad);
        assert.equal(c.CncNativeInterpretation.assess(bad, expectation(n)).interpretationAccepted, false, 'connector mutation ' + i); }
});

test('policy2 measures preserve retry acceptance and reject changed copies, policy and retry chronology', saved, async () => {
    const c = runtime(), n = await seal(c, original(c, 'Cover Plate_25.4mm'));
    const measure = n.meshes[0].kernelBody.nativeRegionMeasures;
    assert.equal(measure.integrationAttempts.length, 3); assert.ok(measure.estimatedRelativeVolumeError > measure.requestedRelativeError);
    const mutations = [m => m.policyVersion = 1, m => m.units = 'mm2', m => m.value = 0, m => m.value = -1, m => m.centroidMm = [0, 0],
        m => m.centroidAbsoluteErrorMm = .001, m => m.estimatedRelativeVolumeError = .000100001, m => m.integrationAttempts = [],
        m => m.integrationAttempts.push(structuredClone(m.integrationAttempts[2])), m => m.integrationAttempts.reverse(),
        m => m.integrationAttempts[0].reason = 'native_integration_failed', m => m.integrationAttempts[0].estimatedRelativeVolumeError = null,
        m => m.integrationAttempts[0].status = 'available', m => m.requestedRelativeError = .0001];
    for (const [i, mutate] of mutations.entries()) { const bad = structuredClone(n); mutate(bad.meshes[0].kernelBody.nativeRegionMeasures);
        bad.kernelProvenance.nativeInterpretation.body.nativeRegionMeasures = structuredClone(bad.meshes[0].kernelBody.nativeRegionMeasures); await seal(c, bad);
        assert.equal(c.CncNativeInterpretation.assess(bad, expectation(n)).interpretationAccepted, false, 'measure mutation ' + i); }
    const copy = structuredClone(n); copy.kernelProvenance.nativeInterpretation.body.nativeRegionMeasures.value += 1; await seal(c, copy);
    assert.equal(c.CncNativeInterpretation.assess(copy, expectation(n)).interpretationAccepted, false);
});

test('invocation/audit/six exact legacy times are nonsemantic at all three embeddings without mutation', async () => {
    const c = runtime(), n = await seal(c, publicEnvelope(c)), changed = structuredClone(n), v = changed.kernelProvenance.nativeInterpretation;
    v.binding.nativeInvocationId = '987'; v.audit.marks[0].elapsedMs += 100; v.audit.marks[0].remainingMs = -20;
    v.audit.firstCancellation = { name: 'observed', boundaryIndex: -1, elapsedMs: 70000, remainingMs: -10000 };
    v.audit.slowMetrics[0].status = 'observational-only';
    for (const b of changed.kernelProvenance.repairAssessment.boundaries) {
        if (b.correlatedDiagnostics) { b.correlatedDiagnostics.wallMilliseconds = 123; b.correlatedDiagnostics.processCpuMilliseconds = null; }
        if (b.metricDispatch) for (const k of ['compositionWallMilliseconds', 'cylinderWallMilliseconds', 'rationalWallMilliseconds', 'highAxisWallMilliseconds']) b.metricDispatch[k] = 456;
    }
    const raw = JSON.stringify(changed); await seal(c, changed); assert.equal(JSON.stringify(changed), raw);
    assert.equal(changed.importRevision, n.importRevision);
    const a = await c.CncTopology.build({ nativeImport: n }, expectation(n)), b = await c.CncTopology.build({ nativeImport: changed }, expectation(n));
    assert.equal(a.revision, b.revision); assert.equal(a.validationMeshHash, b.validationMeshHash);
    const pa = c.CncPlanContracts.topologyEvidence(a), pb = c.CncPlanContracts.topologyEvidence(b);
    assert.equal(await c.CncPlanContracts.hash(pa), await c.CncPlanContracts.hash(pb));
    assert.equal(await c.CncPlanContracts.hash(pa), await c.CncPlanContracts.hash(c.CncPlanContracts.topologyEvidence(pa)), 'legitimate sealed second projection');
    const forged = structuredClone(a); forged.cadDocument.nativeSemanticProjection = c.CncNativeInterpretation.semanticBasis;
    assert.throws(() => c.CncPlanContracts.topologyEvidence(forged), /projected-binding/);
    await assert.rejects(c.CncTopology.revisionHash(forged), /projected-binding/);
    const outside = structuredClone(n); outside.kernelProvenance.audit = { elapsedMs: 9 }; assert.notEqual(await c.CncCadDocument.hash(outside), n.importRevision);
    const semantic = structuredClone(n); semantic.meshes[0].brep_faces[0].nativeRotationalBand.reason = 'different-reason';
    assert.notEqual(await c.CncCadDocument.hash(semantic), n.importRevision, 'rotational evidence is retained semantically');
});

test('malformed audit and raw binding reject before normalization', async () => {
    const c = runtime(), n = await seal(c, publicEnvelope(c));
    for (const mutate of [v => delete v.binding.nativeInvocationId, v => v.binding.nativeInvocationId = 'same-envelope-invocation',
        v => v.audit.schema = 'unknown', v => v.audit.semanticRole = 'ignore-everything', v => v.audit.extra = 1,
        v => v.audit.marks[0].extra = 1, v => v.audit.slowMetrics = Array(21).fill(v.audit.slowMetrics[0]),
        v => v.audit.firstCancellation = { ...v.audit.marks[0] }, v => v.audit.counters.identityMs = NaN,
        v => v.audit.phases = [], v => v.audit.marks[0].sequence = 5]) {
        const bad = structuredClone(n); mutate(bad.kernelProvenance.nativeInterpretation);
        await assert.rejects(c.CncCadDocument.hash(bad), /native_contract_invalid/);
        assert.equal(c.CncNativeInterpretation.assess(bad, expectation(n)).validContract, false);
    }
});

test('source bound declaration and compensated wire representation retain their distinct meaning', async () => {
    const c = runtime(), n = await seal(c, publicEnvelope(c));
    const bad = structuredClone(n), explicit = bad.kernelProvenance.nativeInterpretation.sourceBounds[0]; explicit.isOuterBound = true;
    bad.meshes[0].brep_faces.flatMap(f => f.trims.wires).find(w => w.wireId === explicit.wireId).role = 'inner';
    await seal(c, bad); assert.equal(c.CncNativeInterpretation.assess(bad, expectation(n)).interpretationAccepted, false);
    // Captured-record fixture for a compensated representation reversal; source cycles remain
    // a producer attestation. This does not claim the box importer emitted this variant.
    const changed = structuredClone(n), b = changed.kernelProvenance.nativeInterpretation.sourceBounds[0];
    b.insertionParentOrientation ^= 1; b.expectedNormalizedWireOrientation ^= 1; b.beforeNormalizedWireOrientation ^= 1;
    b.representationOrientationChanged = b.beforeNormalizedWireOrientation !== b.finalNormalizedWireOrientation;
    await seal(c, changed); assert.equal(c.CncNativeInterpretation.assess(changed, expectation(n)).interpretationAccepted, true);
});

test('periodic per-conversion1024 cap and optional connector world composition', saved, async () => {
    const c = runtime(), uadl = await seal(c, original(c, 'UADL150'));
    uadl.kernelProvenance.nativeInterpretation.periodicConversions[0].intervalCount = 1024; await seal(c, uadl);
    assert.equal(c.CncNativeInterpretation.assess(uadl, expectation(uadl)).interpretationAccepted, true);
    uadl.kernelProvenance.nativeInterpretation.periodicConversions[0].intervalCount = 1025; await seal(c, uadl);
    assert.equal(c.CncNativeInterpretation.assess(uadl, expectation(uadl)).interpretationAccepted, false);
    const n = await seal(c, original(c, 'Component4')), v = n.kernelProvenance.nativeInterpretation, proof = v.connectorProofs[0];
    const op = { operationId: 'periodic-0', method: 'native-common-parameter-rational-polynomial-v1', disposition: 'kernel-trusted-reparameterization',
        sourceFaceEntityNumber: v.sourceBounds.find(b => b.faceId === proof.faceId).sourceFaceEntityNumber, sourceSurfaceEntityNumber: 1,
        beforeDomain: [0, 1, 0, 1], afterDomain: [0, 1, 0, 1], localUpperBound: 1e-7, intervalCount: 1, faceId: proof.faceId, placementScale: 1,
        worldUpperBoundMm: c.CncNativeInterpretation.nextUp(1e-7) };
    // Synthetic composition test on actual connector records, not a new native geometry claim.
    v.periodicConversions = [op];
    const row = structuredClone(uadl.kernelProvenance.nativeInterpretation.periodicMetrics.rows[0]);
    Object.assign(row, { faceId: proof.faceId, sourceFaceItemId: proof.sourceFaceItemId, upperBoundMm: op.worldUpperBoundMm });
    row.pathTermsMm[0].upperBoundMm = op.worldUpperBoundMm;
    v.periodicMetrics = { required: 1, evaluated: 1, unassessed: 0, bounded: 1, exceeds: 0, unavailable: 0, rows: [row] };
    for (const metric of v.metrics.rows.filter(r => r.faceId === proof.faceId)) {
        metric.pathTermsMm.push({ termId: metric.obligationId + '/term-' + metric.pathTermsMm.length, kind: 'periodic-world-displacement', upperBoundMm: op.worldUpperBoundMm });
        metric.upperBoundMm = c.CncNativeInterpretation.outwardSum(metric.pathTermsMm.map(t => t.upperBoundMm));
    }
    await seal(c, n); const assessed = c.CncNativeInterpretation.assess(n, expectation(n));
    assert.equal(assessed.interpretationAccepted, true, assessed.reasons.join());
    v.metrics.rows.find(r => r.connectorProofId === proof.connectorProofId).pathTermsMm.pop(); await seal(c, n);
    assert.equal(c.CncNativeInterpretation.assess(n, expectation(n)).interpretationAccepted, false);
});

test('unavailable measures and null-delivery review remain valid nonpositive evidence', saved, async () => {
    const c = runtime(), n = await seal(c, original(c, 'ring'));
    for (const [reason, estimate] of [['native_integration_failed', null], ['invalid_error_estimate', -.1], ['nonpositive_mass', -.1], ['nonfinite_integration_result', null]]) {
        const changed = structuredClone(n), m = changed.meshes[0].kernelBody.nativeRegionMeasures;
        Object.assign(m, { status: 'unavailable', reason, value: null, centroidMm: null, estimatedRelativeVolumeError: null,
            integrationAttempts: [{ requestedRelativeError: .0001, status: 'unavailable', reason, estimatedRelativeVolumeError: estimate }] });
        changed.kernelProvenance.nativeInterpretation.body.nativeRegionMeasures = structuredClone(m); await seal(c, changed);
        assert.equal(c.CncNativeInterpretation.assess(changed, expectation(changed)).interpretationAccepted, true, reason + ' does not revoke import interpretation');
    }
    const negative = await seal(c, original(c, 'UADL150')), v = negative.kernelProvenance.nativeInterpretation;
    v.status = 'review-required'; v.reasons = ['unclassified-transfer-diagnostics']; v.diagnostics.classified--; v.diagnostics.unclassified++;
    Object.assign(v.diagnostics.rows[0], { nativeDeliveryId: null, disposition: 'unclassified', affectedFaceIds: [] });
    negative.kernelProvenance.documentCoverage.sourceTransfer.diagnostics[0].nativeDeliveryId = null; await seal(c, negative);
    assert.equal(c.CncNativeInterpretation.assess(negative).validContract, true);
    assert.equal(c.CncNativeInterpretation.assess(negative, expectation(negative)).interpretationAccepted, false);
    delete v.diagnostics.rows; await seal(c, negative); assert.equal(c.CncNativeInterpretation.assess(negative).validContract, false);
});

test('every numerical measure field and semantic family remains in all three hash boundaries', async () => {
    const c = runtime(), n = await seal(c, publicEnvelope(c)), topology = await c.CncTopology.build({ nativeImport: n }, expectation(n));
    const originalPlanHash = await c.CncPlanContracts.hash(c.CncPlanContracts.topologyEvidence(topology));
    const mutations = Object.keys(n.meshes[0].kernelBody.nativeRegionMeasures).map(key => x => {
        const m = x.meshes[0].kernelBody.nativeRegionMeasures, old = m[key];
        m[key] = old === null ? 'changed-null' : typeof old === 'string' ? old + '-changed' : typeof old === 'number' ? old + 1 : typeof old === 'boolean' ? !old : [];
    });
    mutations.push(x => x.kernelProvenance.nativeInterpretation.metrics.rows[0].upperBoundMm += .0001,
        x => x.kernelProvenance.nativeInterpretation.nativeChecks.rows[0].reusedAnalyzerResult = !x.kernelProvenance.nativeInterpretation.nativeChecks.rows[0].reusedAnalyzerResult,
        x => x.kernelProvenance.nativeInterpretation.sourceBounds[0].sourceBoundEntityNumber++,
        x => x.kernelProvenance.nativeInterpretation.effects[0].kind = 'unknown',
        x => x.kernelProvenance.nativeInterpretation.tolerances.rows[0].valueMm += .0001,
        x => x.meshes[0].brep_faces[0].nativeRotationalBand.methodVersion++);
    for (const [i, mutate] of mutations.entries()) {
        const changed = structuredClone(n); mutate(changed); await seal(c, changed);
        assert.notEqual(changed.importRevision, n.importRevision, 'import field ' + i);
        const t = { ...topology, cadDocument: { ...topology.cadDocument, nativeImport: changed } };
        assert.notEqual(await c.CncTopology.revisionHash(t), topology.revision, 'topology field ' + i);
        assert.notEqual(await c.CncPlanContracts.hash(c.CncPlanContracts.topologyEvidence(t)), originalPlanHash, 'plan field ' + i);
    }
});

test('actual unchanged/deadline/malformed/reset/not-requested exporter fixtures retain distinct states', async () => {
    const c = runtime(), fixture = JSON.parse(fs.readFileSync(boxPath));
    assert.equal(fixture.jsSha256, identity.jsSha256); assert.equal(fixture.wasmSha256, identity.wasmSha256);
    for (const key of ['first', 'reset', 'afterIges']) {
        const n = await seal(c, envelope(c, fixture[key], fixture.sourceSha256));
        assert.equal(c.CncNativeInterpretation.assess(n, expectation(n)).interpretationAccepted, true);
    }
    const deadline = await seal(c, publicEnvelope(c, 'deadline'));
    assert.equal(c.CncNativeInterpretation.assess(deadline).validContract, true);
    assert.equal(c.CncNativeInterpretation.assess(deadline).status, 'review-required');
    assert.equal(c.CncNativeInterpretation.assess(deadline, expectation(deadline)).interpretationAccepted, false);
    assert.equal(fixture.malformed.success, false);
    const unrequested = await seal(c, envelope(c, fixture.unrequested, fixture.sourceSha256));
    assert.equal(c.CncNativeInterpretation.assess(unrequested).validContract, true);
    assert.equal(c.CncNativeInterpretation.assess(unrequested).status, 'not-requested');
    const iges = envelope(c, fixture.iges, fixture.igesSourceSha256); iges.sourceFormat = 'iges'; await seal(c, iges);
    assert.equal(c.CncNativeInterpretation.assess(iges).validContract, true);
    assert.equal(c.CncNativeInterpretation.assess(iges).status, 'not-requested');
    assert.equal(Object.hasOwn(iges.kernelProvenance.nativeInterpretation, 'metrics'), false);
});

test('export failure preserves conservative empty ledgers and null diagnostic counts', async () => {
    const c = runtime(), n = publicEnvelope(c, 'deadline'), v = n.kernelProvenance.nativeInterpretation;
    // Exact ExportNativeInterpretationFailure wire variant synthesized from captured inventory.
    v.status = 'review-required'; v.reasons = ['native-interpretation-export-failed']; v.exportFailure = 'captured exception fixture';
    for (const key of ['metrics', 'periodicMetrics']) v[key] = { required: v[key].required, evaluated: 0, unassessed: v[key].required, bounded: 0, exceeds: 0, unavailable: 0, rows: [] };
    v.nativeChecks = { required: v.nativeChecks.required, passed: 0, failed: 0, unavailable: 0, notEvaluated: v.nativeChecks.required, rows: [] };
    v.tolerances = { required: v.tolerances.required, evaluated: 0, unassessed: v.tolerances.required, within: 0, exceeds: 0, invalid: 0, maximumMm: null, rows: [] };
    v.diagnostics = { status: 'unavailable-after-export-failure', required: null, classified: null, unclassified: null, sourceIdentityFailures: 0, captureFailures: 0 };
    for (const key of ['faces', 'sourceBounds', 'effects', 'periodicConversions']) v[key] = [];
    await seal(c, n); assert.equal(c.CncNativeInterpretation.assess(n).validContract, true); assert.equal(c.CncNativeInterpretation.assess(n, expectation(n)).interpretationAccepted, false);
    v.diagnostics.required = 0; await seal(c, n); assert.equal(c.CncNativeInterpretation.assess(n).validContract, false);
});

test('public box policy and expectation cannot jointly contradict actual import duration', async () => {
    const c = runtime(), n = publicEnvelope(c);
    n.kernelProvenance.nativeInterpretation.policy.assessmentTimeLimitMs = 59999;
    const requested = { ...expectation(n), assessmentTimeLimitMs: 59999 };
    await seal(c, n);
    const assessment = c.CncNativeInterpretation.assess(n, requested);
    assert.equal(assessment.validContract, false);
    assert.ok(assessment.reasons.includes('native_contract_invalid:policy-options-duration'));
    const topology = await c.CncTopology.build({ nativeImport: n }, requested);
    assert.ok(topology.unresolvedReasons.includes('native_contract_invalid:policy-options-duration'));
    assert.equal(topology.automaticPlanningEligible, false);
    const deadline = await seal(c, publicEnvelope(c, 'deadline'));
    assert.equal(deadline.tessellation.repairAssessmentTimeLimitMs, 0, 'actual exporter deadline override');
    assert.equal(c.CncNativeInterpretation.assess(deadline).validContract, true);
});

test('public deadline tolerance rows, observed counts and maximum distinguish malformed from incomplete', async () => {
    const c = runtime(), n = await seal(c, publicEnvelope(c, 'deadline'));
    const mutations = [t => t.rows[0] = null, t => t.rows[1] = structuredClone(t.rows[0]),
        t => t.rows[0].valueMm = .02, t => t.maximumMm = .02, t => t.rows[0].phase = 'unknown',
        t => t.rows[0].captured = null, t => t.rows[0].valueMm = -1, t => t.rows[0].captured = false,
        t => t.rows[0].status = 'within', t => t.rows[0].nativeItemId = ''];
    for (const [i, mutate] of mutations.entries()) {
        const bad = structuredClone(n); mutate(bad.kernelProvenance.nativeInterpretation.tolerances); await seal(c, bad);
        assert.equal(c.CncNativeInterpretation.assess(bad).validContract, false, 'negative tolerance mutation ' + i);
        const topology = await c.CncTopology.build({ nativeImport: bad });
        assert.ok(topology.unresolvedReasons.some(r => r.startsWith('native_contract_invalid:tolerance') || r === 'native_contract_invalid:uncaptured-tolerance'), 'worker mutation ' + i);
    }
    for (const [captured, value] of [[true, .02], [true, -1], [true, null], [false, null]]) {
        const changed = structuredClone(n), t = changed.kernelProvenance.nativeInterpretation.tolerances;
        Object.assign(t.rows[0], { captured, valueMm: value });
        t.within--;
        if (!captured) { t.evaluated--; t.unassessed++; }
        else if (value === null || value < 0) t.invalid++;
        else { t.exceeds++; t.maximumMm = .02; }
        await seal(c, changed); const a = c.CncNativeInterpretation.assess(changed);
        assert.equal(a.validContract, true, JSON.stringify({ captured, value, reasons: a.reasons }));
        assert.equal(a.interpretationAccepted, false);
    }
    const uncaptured = structuredClone(n), t = uncaptured.kernelProvenance.nativeInterpretation.tolerances;
    t.rows.forEach(r => Object.assign(r, { captured: false, valueMm: null }));
    Object.assign(t, { evaluated: 0, unassessed: t.required, within: 0, exceeds: 0, invalid: 0, maximumMm: null });
    await seal(c, uncaptured); assert.equal(c.CncNativeInterpretation.assess(uncaptured).validContract, true);
});
