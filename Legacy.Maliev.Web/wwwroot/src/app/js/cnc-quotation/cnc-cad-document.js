(function (root) {
    'use strict';
    var parameters = Object.freeze({ linearUnit: 'millimeter', linearDeflectionType: 'absolute_value', linearDeflection: 0.1,
        repairErrorBudgetMm: 0.01, repairAssessmentTimeLimitMs: 60000, repairDiagnostics: false });
    var basis = 'maliev-native-semantic-v2';
    var object = function (x) { return !!x && typeof x === 'object' && !Array.isArray(x); };
    var text = function (x) { return typeof x === 'string' && x.length > 0; };
    var count = function (x) { return Number.isSafeInteger(x) && x >= 0; };
    var nonnegative = function (x) { return Number.isFinite(x) && x >= 0; };
    var equal = function (a, b) { return JSON.stringify(root.CncPlanContracts.canonicalize(a)) === JSON.stringify(root.CncPlanContracts.canonicalize(b)); };
    function requireValue(condition, reason) { if (!condition) { throw new Error('native_contract_invalid:' + reason); } }
    function json(value, seen) {
        if (value === null || typeof value === 'string' || typeof value === 'boolean') { return true; }
        if (typeof value === 'number') { return Number.isFinite(value); }
        if (typeof value !== 'object') { return false; }
        if (!Array.isArray(value) && Object.prototype.toString.call(value) !== '[object Object]') { return false; }
        if (Array.isArray(value) && Array.from(value).some(function (item) { return item === undefined; })) { return false; }
        seen = seen || new Set(); if (seen.has(value)) { return false; } seen.add(value);
        var valid = Object.keys(value).every(function (key) { return json(value[key], seen); }); seen.delete(value); return valid;
    }
    function keys(value, names) { return object(value) && equal(Object.keys(value).sort(), names.split(' ').sort()); }
    function setEqual(a, b) { return Array.isArray(a) && Array.isArray(b) && a.length === b.length && new Set(a).size === a.length && a.every(function (x) { return b.includes(x); }); }
    function index(rows, key) {
        requireValue(Array.isArray(rows), key + '-array'); var result = new Map();
        rows.forEach(function (row) { requireValue(object(row) && text(row[key]) && !result.has(row[key]), key + '-unique'); result.set(row[key], row); }); return result;
    }
    // Exact IEEE-754 nextafter toward +infinity, including zero and subnormals.
    function nextUp(x) {
        if (Number.isNaN(x) || x === Infinity) { return x; }
        if (x === 0) { return Number.MIN_VALUE; }
        var view = new DataView(new ArrayBuffer(8)); view.setFloat64(0, x, false);
        var bits = view.getBigUint64(0, false); view.setBigUint64(0, x > 0 ? bits + 1n : bits - 1n, false); return view.getFloat64(0, false);
    }
    function outwardSum(terms) {
        requireValue(Array.isArray(terms) && terms.length > 0 && terms.every(nonnegative), 'outward-terms');
        return terms.slice(1).reduce(function (sum, term) { return nextUp(sum + term); }, terms[0]);
    }
    function auditValid(a) {
        requireValue(keys(a, 'schema semanticRole clock unit origin cpuTimeMs cpuTimeStatus phases counters marks slowMetrics firstCancellation')
            && a.schema === 'MalievNativeInterpretationAudit.v1' && a.semanticRole === 'excluded-by-MalievNativeInterpretationSemanticProjection.v1'
            && a.clock === 'steady-monotonic' && a.unit === 'millisecond' && a.origin === 'Configure' && a.cpuTimeMs === null
            && a.cpuTimeStatus === 'unavailable-in-current-wasm-runtime', 'audit-schema');
        requireValue(object(a.phases) && Object.entries(a.phases).every(function (pair) {
            var p = pair[1]; return text(pair[0]) && keys(p, 'calls exits inclusiveMs selfMs') && count(p.calls) && count(p.exits) && nonnegative(p.inclusiveMs) && nonnegative(p.selfMs);
        }), 'audit-phases');
        requireValue(object(a.counters) && ['identityCalls', 'identityCandidates', 'identityMs', 'captureCalls'].every(function (k) { return Object.hasOwn(a.counters, k); })
            && Object.entries(a.counters).every(function (p) { return text(p[0]) && (p[0] === 'identityMs' ? nonnegative(p[1]) : count(p[1])); }), 'audit-counters');
        function mark(m, sequence) { return keys(m, 'name boundaryIndex elapsedMs remainingMs' + (sequence ? ' sequence' : '')) && text(m.name)
            && Number.isSafeInteger(m.boundaryIndex) && m.boundaryIndex >= -1 && nonnegative(m.elapsedMs) && Number.isFinite(m.remainingMs); }
        requireValue(Array.isArray(a.marks) && a.marks.every(function (m, i) { return mark(m, true) && m.sequence === i; })
            && (a.firstCancellation === null || mark(a.firstCancellation, false)), 'audit-marks');
        requireValue(Array.isArray(a.slowMetrics) && a.slowMetrics.length <= 20 && a.slowMetrics.every(function (m) {
            return keys(m, 'boundaryIndex obligationId method status intervals elapsedMs') && Number.isSafeInteger(m.boundaryIndex) && m.boundaryIndex >= -1
                && text(m.obligationId) && text(m.method) && text(m.status) && count(m.intervals) && nonnegative(m.elapsedMs);
        }), 'audit-slow-metrics');
    }
    function rawProjectionValidation(n) {
        requireValue(json(n), 'finite-json'); requireValue(n.nativeSemanticBasis === basis, 'semantic-basis');
        var p = n.kernelProvenance, interpretation = p && p.nativeInterpretation;
        if (interpretation) {
            requireValue(p.schema === 'MalievKernelFaces.v1' && interpretation.schema === 'MalievNativeInterpretation.v1', 'interpretation-schema');
            var b = interpretation.binding;
            requireValue(object(b) && b.scope === 'enclosing-CncNativeImport.v1-source-and-recomputed-revision'
                && b.identities === 'invocation-local-native-occurrences' && typeof b.nativeInvocationId === 'string' && /^\d+$/.test(b.nativeInvocationId), 'binding');
            auditValid(interpretation.audit);
        }
        var repair = p && p.repairAssessment;
        if (repair && repair.schema === 'MalievKernelRepair.v1' && Array.isArray(repair.boundaries)) {
            repair.boundaries.forEach(function (b) {
                [['correlatedDiagnostics', ['wallMilliseconds', 'processCpuMilliseconds']], ['metricDispatch', ['compositionWallMilliseconds', 'cylinderWallMilliseconds', 'rationalWallMilliseconds', 'highAxisWallMilliseconds']]].forEach(function (entry) {
                    if (b[entry[0]]) { entry[1].forEach(function (k) { if (Object.hasOwn(b[entry[0]], k)) {
                        var v = b[entry[0]][k]; requireValue(nonnegative(v) || (k === 'processCpuMilliseconds' && v === null), 'legacy-timing');
                    } }); }
                });
            });
        }
    }
    function project(n) {
        rawProjectionValidation(n); var result = JSON.parse(JSON.stringify(n)), p = result.kernelProvenance;
        if (p.nativeInterpretation) { p.nativeInterpretation.binding.nativeInvocationId = 'same-envelope-invocation'; delete p.nativeInterpretation.audit; }
        if (p.repairAssessment && p.repairAssessment.schema === 'MalievKernelRepair.v1' && Array.isArray(p.repairAssessment.boundaries)) {
            p.repairAssessment.boundaries.forEach(function (b) {
                if (b.correlatedDiagnostics) { delete b.correlatedDiagnostics.wallMilliseconds; delete b.correlatedDiagnostics.processCpuMilliseconds; }
                if (b.metricDispatch) { ['compositionWallMilliseconds', 'cylinderWallMilliseconds', 'rationalWallMilliseconds', 'highAxisWallMilliseconds'].forEach(function (k) { delete b.metricDispatch[k]; }); }
            });
        }
        return result;
    }
    function projectDocument(document) {
        if (!document || !document.nativeImport) { return document; }
        requireValue(!Object.hasOwn(document, 'nativeSemanticProjection') || document.nativeSemanticProjection === basis, 'semantic-basis');
        // Sealed topologyEvidence is already a semantic object. Raw import admission never
        // accepts this marker or the normalized invocation in place of a native binding.
        if (document.nativeSemanticProjection === basis) {
            var n = document.nativeImport, p = n.kernelProvenance, v = p && p.nativeInterpretation;
            requireValue(json(n) && n.nativeSemanticBasis === basis, 'semantic-basis');
            if (v) {
                requireValue(p.schema === 'MalievKernelFaces.v1' && v.schema === 'MalievNativeInterpretation.v1' && !Object.hasOwn(v, 'audit')
                    && v.binding && v.binding.nativeInvocationId === 'same-envelope-invocation'
                    && v.binding.scope === 'enclosing-CncNativeImport.v1-source-and-recomputed-revision'
                    && v.binding.identities === 'invocation-local-native-occurrences', 'projected-binding');
            }
            var repair = p && p.repairAssessment;
            if (repair && repair.schema === 'MalievKernelRepair.v1' && Array.isArray(repair.boundaries)) {
                repair.boundaries.forEach(function (b) {
                    requireValue(!b.correlatedDiagnostics || !['wallMilliseconds', 'processCpuMilliseconds'].some(function (k) { return Object.hasOwn(b.correlatedDiagnostics, k); }), 'projected-timing');
                    requireValue(!b.metricDispatch || !['compositionWallMilliseconds', 'cylinderWallMilliseconds', 'rationalWallMilliseconds', 'highAxisWallMilliseconds'].some(function (k) { return Object.hasOwn(b.metricDispatch, k); }), 'projected-timing');
                });
            }
            return document;
        }
        var result = Object.assign({}, document); result.nativeImport = project(document.nativeImport); result.nativeSemanticProjection = basis; return result;
    }
    var measureReasons = 'millimeter_units_unverified native_face_unavailable native_surface_unavailable unsupported_face_orientation native_face_invalid supported_solid_unavailable incomplete_face_membership invalid_solid_membership native_shell_not_closed_or_oriented single_shell_solid_required native_faces_unavailable native_solid_invalid native_solid_not_bounded_or_oriented native_integration_failed kernel_body_export_failed nonfinite_integration_result nonpositive_mass invalid_error_estimate integration_target_not_met'.split(' ');
    function measure(m, bodyId, faceId) {
        var area = faceId !== undefined, errorKey = area ? 'estimatedRelativeAreaError' : 'estimatedRelativeVolumeError';
        var common = 'schema policyId policyVersion bodyId status reason quantity units unitProvenance coordinateSpace method acceptanceRelativeError initialRequestedRelativeError requestedRelativeError errorSemantics centroidErrorStatus centroidAbsoluteErrorMm nativeSupportRequired skipShared value centroidMm ';
        requireValue(keys(m, common + errorKey + (area ? ' faceId' : ' onlyClosed integrationAttempts')), 'measure-fields');
        requireValue(m.schema === 'MalievNativeRegionMeasures.v1' && m.policyId === 'native-adaptive-mass-properties' && m.policyVersion === 2
            && m.bodyId === bodyId && (!area || m.faceId === faceId) && m.quantity === (area ? 'area' : 'volume') && m.units === (area ? 'mm2' : 'mm3')
            && m.method === (area ? 'BRepGProp.SurfaceProperties-adaptive' : 'BRepGProp.VolumeProperties-adaptive')
            && m.unitProvenance === 'same-import-verified-millimeter-normalization-required' && m.coordinateSpace === 'import-world'
            && m.acceptanceRelativeError === 0.0001 && m.initialRequestedRelativeError === 0.0001
            && m.errorSemantics === 'native-successive-integration-estimate-max-face' && m.centroidErrorStatus === 'not-estimated'
            && m.centroidAbsoluteErrorMm === null && m.nativeSupportRequired === true && m.skipShared === false, 'measure-policy');
        var available = m.status === 'available';
        requireValue(available ? m.reason === null && Number.isFinite(m.value) && m.value > 0 && Array.isArray(m.centroidMm) && m.centroidMm.length === 3
            && m.centroidMm.every(Number.isFinite) && nonnegative(m[errorKey]) && m[errorKey] <= 0.0001
            : m.status === 'unavailable' && measureReasons.includes(m.reason) && m.value === null && m.centroidMm === null && m[errorKey] === null, 'measure-result');
        var numerical = ['nonfinite_integration_result', 'nonpositive_mass', 'invalid_error_estimate', 'integration_target_not_met'];
        if (area) { requireValue(m.requestedRelativeError === ((available || numerical.includes(m.reason)) ? 0.0001 : null), 'area-request'); return available; }
        requireValue(m.onlyClosed === true && Array.isArray(m.integrationAttempts) && m.integrationAttempts.length <= 3, 'volume-attempts');
        var attempts = m.integrationAttempts;
        if (!attempts.length) { requireValue(!available && m.requestedRelativeError === null && !numerical.includes(m.reason) && m.reason !== 'native_integration_failed', 'volume-empty'); return false; }
        attempts.forEach(function (a, i) {
            requireValue(keys(a, 'requestedRelativeError status reason estimatedRelativeVolumeError') && a.requestedRelativeError === [0.0001, 0.00001, 0.000001][i], 'volume-attempt-request');
            var last = i === attempts.length - 1, e = a.estimatedRelativeVolumeError;
            if (!last) { requireValue(a.status === 'unavailable' && a.reason === 'integration_target_not_met' && Number.isFinite(e) && e > 0.0001, 'volume-retry'); return; }
            requireValue(a.status === m.status && a.reason === m.reason && a.requestedRelativeError === m.requestedRelativeError, 'volume-last');
            if (available) { requireValue(e === m.estimatedRelativeVolumeError, 'volume-estimate'); }
            else {
                requireValue(numerical.concat('native_integration_failed').includes(a.reason), 'volume-attempt-reason');
                if (a.reason === 'integration_target_not_met') { requireValue(attempts.length === 3 && Number.isFinite(e) && e > 0.0001, 'volume-exhaustion'); }
                else if (a.reason === 'invalid_error_estimate') { requireValue(Number.isFinite(e) && e < 0, 'volume-negative-estimate'); }
                else if (a.reason === 'native_integration_failed') { requireValue(e === null, 'volume-exception'); }
                else { requireValue(e === null || Number.isFinite(e), 'volume-failed-estimate'); }
            }
        }); return available;
    }
    function metricPartition(ledger) {
        requireValue(object(ledger) && Array.isArray(ledger.rows) && ['required', 'evaluated', 'unassessed', 'bounded', 'exceeds', 'unavailable'].every(function (k) { return count(ledger[k]); }), 'metric-partition');
        requireValue(ledger.rows.length === ledger.required && ledger.required === ledger.evaluated + ledger.unassessed
            && ledger.evaluated === ledger.bounded + ledger.exceeds + ledger.unavailable, 'metric-counts');
        ['bounded', 'exceeds', 'unavailable', 'unassessed'].forEach(function (s) { requireValue(ledger.rows.filter(function (r) { return r.status === s; }).length === ledger[s], 'metric-status-count'); });
        index(ledger.rows, 'obligationId');
    }
    function measures(n, v) {
        requireValue(Array.isArray(n.meshes), 'measure-ownership');
        n.meshes.forEach(function (m) {
            requireValue(m.kernelBody && m.kernelBody.bodyId === m.bodyId && Array.isArray(m.brep_faces), 'measure-ownership');
            measure(m.kernelBody.nativeRegionMeasures, m.bodyId);
            m.brep_faces.forEach(function (f) { requireValue(f.bodyId === m.bodyId, 'measure-ownership'); measure(f.nativeRegionMeasures, m.bodyId, f.faceId); });
        });
        if (v.body) { var body = n.meshes.find(function (m) { return m.bodyId === v.body.bodyId; }); requireValue(body && equal(v.body, body.kernelBody), 'body-copy'); }
    }
    function negativePartitions(n, v) {
        var c = v.nativeChecks;
        requireValue(object(c) && Array.isArray(c.rows) && ['required', 'passed', 'failed', 'unavailable', 'notEvaluated'].every(function (k) { return count(c[k]); })
            && c.required === c.rows.length && c.required === c.passed + c.failed + c.unavailable + c.notEvaluated, 'check-counts');
        index(c.rows, 'checkId');
        ['passed', 'failed', 'unavailable', 'not-evaluated'].forEach(function (s) { requireValue(c.rows.filter(function (r) { return r.status === s; }).length === c[s === 'not-evaluated' ? 'notEvaluated' : s], 'check-status-count'); });
        var t = v.tolerances;
        requireValue(object(t) && Array.isArray(t.rows) && ['required', 'evaluated', 'unassessed', 'within', 'exceeds', 'invalid'].every(function (k) { return count(t[k]); })
            && t.required === t.rows.length && t.required === t.evaluated + t.unassessed && t.evaluated === t.within + t.exceeds + t.invalid, 'tolerance-counts');
        // Recount the native ToleranceLedger.Observe branches, including legitimate
        // incomplete and invalid observations. Its maximum excludes invalid values.
        var observed = { evaluated: 0, unassessed: 0, within: 0, exceeds: 0, invalid: 0 }, maximum = 0, items = new Set();
        t.rows.forEach(function (row) {
            requireValue(keys(row, 'nativeItemId phase captured valueMm') && text(row.nativeItemId) && ['before', 'after', 'final-added'].includes(row.phase)
                && typeof row.captured === 'boolean' && (row.valueMm === null || Number.isFinite(row.valueMm)), 'tolerance-row');
            var key = row.nativeItemId + '|' + row.phase;
            requireValue(!items.has(key), 'tolerance-unique'); items.add(key);
            if (!row.captured) { requireValue(row.valueMm === null, 'uncaptured-tolerance'); observed.unassessed++; return; }
            observed.evaluated++;
            if (row.valueMm === null || row.valueMm < 0) { observed.invalid++; return; }
            maximum = Math.max(maximum, row.valueMm);
            observed[row.valueMm > v.policy.errorBudgetMm ? 'exceeds' : 'within']++;
        });
        requireValue(Object.keys(observed).every(function (key) { return observed[key] === t[key]; })
            && t.maximumMm === (observed.evaluated > 0 ? maximum : null), 'tolerance-observed-partition');
        var d = v.diagnostics, raw = index(n.kernelProvenance.documentCoverage.sourceTransfer.diagnostics, 'diagnosticOccurrenceId');
        requireValue(object(d) && Array.isArray(d.rows) && ['required', 'classified', 'unclassified', 'sourceIdentityFailures', 'captureFailures'].every(function (k) { return count(d[k]); })
            && d.required === d.rows.length && d.required === raw.size && d.required === d.classified + d.unclassified, 'diagnostic-partition');
        index(d.rows, 'diagnosticOccurrenceId');
        requireValue(d.rows.filter(function (r) { return r.disposition === 'checked-native-operation-effects'; }).length === d.classified
            && d.rows.filter(function (r) { return r.disposition === 'unclassified'; }).length === d.unclassified, 'diagnostic-status-count');
        d.rows.forEach(function (r) { var original = raw.get(r.diagnosticOccurrenceId);
            requireValue(original && r.nativeDeliveryId === original.nativeDeliveryId && (r.nativeDeliveryId === null || /^native-delivery-\d+$/.test(r.nativeDeliveryId))
                && Array.isArray(r.affectedFaceIds) && r.affectedFaceIds.every(text), 'diagnostic-lineage');
        });
    }
    function fallback(v) {
        requireValue(v.status === 'review-required' && text(v.exportFailure), 'export-fallback');
        ['metrics', 'periodicMetrics'].forEach(function (key) { var x = v[key];
            requireValue(x && count(x.required) && x.unassessed === x.required && x.evaluated === 0 && x.bounded === 0 && x.exceeds === 0 && x.unavailable === 0 && equal(x.rows, []), 'fallback-metrics');
        });
        var c = v.nativeChecks, t = v.tolerances, d = v.diagnostics;
        requireValue(c && count(c.required) && c.notEvaluated === c.required && c.passed === 0 && c.failed === 0 && c.unavailable === 0 && equal(c.rows, []), 'fallback-checks');
        requireValue(t && count(t.required) && t.unassessed === t.required && t.evaluated === 0 && t.within === 0 && t.exceeds === 0 && t.invalid === 0 && t.maximumMm === null && equal(t.rows, []), 'fallback-tolerances');
        requireValue(d && d.status === 'unavailable-after-export-failure' && d.required === null && d.classified === null && d.unclassified === null
            && count(d.sourceIdentityFailures) && count(d.captureFailures) && !Object.hasOwn(d, 'rows'), 'fallback-diagnostics');
        ['faces', 'sourceBounds', 'effects', 'periodicConversions'].forEach(function (k) { requireValue(equal(v[k], []), 'fallback-inventory'); });
    }
    function assess(n, expectation) {
        var status = 'contract-invalid';
        try {
            rawProjectionValidation(n);
            var p = n.kernelProvenance, v = p.nativeInterpretation;
            if (!v) { return { validContract: true, interpretationAccepted: false, status: 'unsupported-runtime', reasons: ['native_interpretation_unavailable'] }; }
            status = v.status;
            requireValue(['native-interpreted', 'native-interpreted-with-bounded-repair', 'review-required', 'invalid', 'unsupported', 'not-requested'].includes(status)
                && Array.isArray(v.reasons) && v.reasons.every(text) && v.guarantee === 'trusted-native-interpretation' && v.manufacturingEligibility === 'not-assessed', 'interpretation-status');
            requireValue(object(v.policy) && v.policy.id === 'maliev-native-step-interpretation' && v.policy.version === 1, 'policy');
            measures(n, v);
            if (status === 'not-requested') { requireValue(v.policy.errorBudgetMm === null, 'not-requested-budget'); return { validContract: true, interpretationAccepted: false, status: status, reasons: v.reasons.slice() }; }
            var policy = v.policy;
            requireValue(policy.errorBudgetMm === 0.01 && policy.budgetSource === 'explicit-caller-policy' && policy.units === 'millimeter'
                && count(policy.assessmentTimeLimitMs) && policy.assessmentTimeLimitMs <= 60000
                && policy.occtRevision === 'd2abb6d844231cb8f29be6894440874a4700e4a5' && policy.importerRevision === 'c2148e54b456b571238d35cac037d304053d64b2', 'policy');
            requireValue(n.tessellation && policy.assessmentTimeLimitMs === n.tessellation.repairAssessmentTimeLimitMs, 'policy-options-duration');
            var positive = status === 'native-interpreted' || status === 'native-interpreted-with-bounded-repair';
            if (!positive && v.reasons.includes('native-interpretation-export-failed')) {
                fallback(v);
                return { validContract: true, interpretationAccepted: false, status: status, reasons: v.reasons.slice() };
            }
            metricPartition(v.metrics); metricPartition(v.periodicMetrics);
            negativePartitions(n, v);
            // Incomplete native work stays visible; it cannot enter the positive-only proof branch.
            if (!positive) { requireValue(v.reasons.length > 0, 'negative-reasons'); return { validContract: true, interpretationAccepted: false, status: status, reasons: v.reasons.slice() }; }
            requireValue(v.reasons.length === 0 && n.contract === 'CncNativeImport.v1' && n.analysisProfile === 'cnc' && /^(step|stp)$/.test(n.sourceFormat)
                && p.schema === 'MalievKernelFaces.v1' && p.identityScope === 'single-import-result' && p.millimeterOutput === true && p.unitsStatus === 'normalized-import'
                && equal(n.tessellation, parameters), 'envelope');
            var d = p.documentCoverage, transfer = d.sourceTransfer, coverage = p.sourceCoverage;
            requireValue(coverage.schema === 'MalievKernelSourceCoverage.v1' && coverage.status === 'complete_supported_single_solid' && coverage.complete === true
                && coverage.repairAssessmentIsSeparate === true && equal(coverage.reasons, []) && transfer.structurallySupportedSingleSolidSource === true, 'source-coverage');
            requireValue(n.meshes.length === 1 && equal(v.body, n.meshes[0].kernelBody), 'body-copy');
            var mesh = n.meshes[0], faceMap = index(mesh.brep_faces, 'faceId'), edgeMap = index(mesh.kernelTopology.edges, 'edgeId');
            var nativeFaces = index(v.faces, 'faceId'); index(v.faces, 'nativeItemId');
            requireValue(setEqual(Array.from(faceMap.keys()), Array.from(nativeFaces.keys())), 'face-universe');
            var wires = new Map(), coedges = new Map(), sourceFaces = d.occurrences.flatMap(function (o) { return o.sourceFaces; });
            index(sourceFaces, 'sourceFaceOccurrenceId');
            faceMap.forEach(function (f) {
                var item = nativeFaces.get(f.faceId), source = sourceFaces.filter(function (s) { return s.emittedFaceIds.includes(f.faceId); });
                requireValue(item.status === 'checked' && item.bodyId === mesh.bodyId && f.bodyId === mesh.bodyId && [0, 1].includes(item.sourceOrientation)
                    && item.sourceOrientation === item.finalOrientation && item.finalOrientation === f.orientation && source.length === 1
                    && source[0].sourceFaceOccurrenceId === item.sourceOccurrenceId && setEqual(f.assemblyInstance.sourceFaceOccurrenceCandidates, [item.sourceOccurrenceId]), 'face-source');
                f.trims.wires.forEach(function (w) {
                    requireValue(text(w.wireId) && !wires.has(w.wireId), 'wire-unique'); wires.set(w.wireId, { wire: w, face: f });
                    w.coedges.forEach(function (c) { requireValue(text(c.coedgeId) && !coedges.has(c.coedgeId) && edgeMap.has(c.edgeId), 'coedge-unique'); coedges.set(c.coedgeId, { use: c, wire: w, face: f }); });
                });
                measure(f.nativeRegionMeasures, mesh.bodyId, f.faceId);
            });
            measure(mesh.kernelBody.nativeRegionMeasures, mesh.bodyId);
            var bounds = index(v.sourceBounds, 'sourceBoundOccurrenceId');
            requireValue(bounds.size === wires.size && new Set(v.sourceBounds.map(function (b) { return b.wireId; })).size === wires.size, 'bound-universe');
            bounds.forEach(function (b) {
                var owned = wires.get(b.wireId), expected = b.loopKind === 'StepShape_EdgeLoop' ? (b.boundSense === b.effectiveFaceSameSense ? 0 : 1) : (b.boundSense ? 0 : 1);
                requireValue(owned && owned.face.faceId === b.faceId && ['StepShape_EdgeLoop', 'StepShape_PolyLoop'].includes(b.loopKind)
                    && ['sourceFaceEntityNumber', 'sourceBoundEntityNumber', 'sourceLoopEntityNumber'].every(function (k) { return count(b[k]) && b[k] > 0; })
                    && ['isOuterBound', 'boundSense', 'sourceFaceSameSense', 'effectiveFaceSameSense', 'representationOrientationChanged'].every(function (k) { return typeof b[k] === 'boolean'; })
                    && b.sourceFaceSameSense === (owned.face.orientation === 0) && [0, 1].includes(b.insertionParentOrientation)
                    && b.preAddWireOrientation === expected && b.expectedPreAddWireOrientation === expected
                    && b.expectedNormalizedWireOrientation === (expected ^ b.insertionParentOrientation) && b.beforeNormalizedWireOrientation === b.expectedNormalizedWireOrientation
                    && b.finalNormalizedWireOrientation === owned.wire.orientation && b.representationOrientationChanged === (b.beforeNormalizedWireOrientation !== b.finalNormalizedWireOrientation)
                    && b.composedCoedgeCyclePreserved === true && b.coedgeAssociationStatus === 'complete-bidirectional-occurrence-join'
                    && setEqual(b.coedgeIds, owned.wire.coedges.map(function (c) { return c.coedgeId; }))
                    && setEqual(b.edgeIds, Array.from(new Set(owned.wire.coedges.map(function (c) { return c.edgeId; }))))
                    // Generic STEP FACE_BOUND may describe the actual outer wire. Only the
                    // explicit FACE_OUTER_BOUND subtype carries a source outer declaration.
                    && (!b.isOuterBound || owned.wire.role === 'outer') && ['outer', 'inner'].includes(owned.wire.role), 'source-bound');
            });
            var metrics = index(v.metrics.rows, 'obligationId'), periodic = index(v.periodicConversions, 'operationId'), periodicRows = index(v.periodicMetrics.rows, 'obligationId'), terms = new Set();
            requireValue(v.metrics.required > 0 && v.metrics.bounded === v.metrics.required && v.periodicMetrics.bounded === v.periodicMetrics.required
                && periodic.size === periodicRows.size, 'positive-metrics');
            var routes = { 'exact-unchanged-trim-with-independent-edge-residual': ['post-trim-lift-residual', 'endpoint-range-sliver'],
                'post-lift-to-unchanged-source-3d-plus-endpoint-sliver': ['post-trim-lift-residual', 'endpoint-range-sliver'],
                'old-lift-via-source-3d-to-new-lift': ['post-trim-lift-residual', 'source-trim-lift-residual', 'endpoint-range-sliver'],
                'whole-native-degenerate-lift-to-preserved-source-vertex': ['degenerate-point-image-residual'] };
            function bounded(row, kinds) {
                requireValue(row.status === 'bounded' && row.reason === '' && row.composition === 'ordered-outward-add-v1'
                    && Array.isArray(row.pathTermsMm) && equal(row.pathTermsMm.map(function (t) { return t.kind; }), kinds), 'metric-route-terms');
                row.pathTermsMm.forEach(function (t, i) { requireValue(t.termId === row.obligationId + '/term-' + i && !terms.has(t.termId) && nonnegative(t.upperBoundMm), 'metric-term'); terms.add(t.termId); });
                requireValue(nonnegative(row.upperBoundMm) && row.upperBoundMm <= 0.01 && outwardSum(row.pathTermsMm.map(function (t) { return t.upperBoundMm; })) === row.upperBoundMm, 'metric-outward-bound');
            }
            periodic.forEach(function (op) {
                var row = periodicRows.get(op.operationId), f = nativeFaces.get(op.faceId);
                requireValue(row && f && op.method === 'native-common-parameter-rational-polynomial-v1' && op.disposition === 'kernel-trusted-reparameterization'
                    && count(op.sourceSurfaceEntityNumber) && op.sourceSurfaceEntityNumber > 0 && v.sourceBounds.some(function (b) { return b.faceId === op.faceId && b.sourceFaceEntityNumber === op.sourceFaceEntityNumber; })
                    && Array.isArray(op.beforeDomain) && op.beforeDomain.length === 4 && op.beforeDomain.every(Number.isFinite) && equal(op.beforeDomain, op.afterDomain)
                    && op.beforeDomain[0] < op.beforeDomain[1] && op.beforeDomain[2] < op.beforeDomain[3]
                    && nonnegative(op.localUpperBound) && op.localUpperBound <= 0.005 && Number.isFinite(op.placementScale) && op.placementScale > 0
                    && count(op.intervalCount) && op.intervalCount > 0 && op.intervalCount <= 1024 && op.worldUpperBoundMm === nextUp(op.placementScale * op.localUpperBound)
                    && row.kind === 'periodic-reparameterization' && row.operationId === op.operationId && row.faceId === op.faceId && row.sourceFaceItemId === f.nativeItemId
                    && row.associationStatus === 'complete-periodic-operation-face-association' && row.referencePath === 'periodic-local-surface-through-native-face-placement'
                    && row.metricMethod === 'native-common-parameter-rational-polynomial-v1+native-placement-scale' && row.upperBoundMm === op.worldUpperBoundMm
                    && ['sourceEdgeItemId', 'sourceVertexItemId', 'connectorProofId', 'wireId', 'coedgeId', 'edgeId', 'sourceRange'].every(function (k) { return row[k] === null; }), 'periodic');
                bounded(row, ['periodic-world-displacement']);
            });
            var consumed = new Set();
            metrics.forEach(function (row) {
                var owned = coedges.get(row.coedgeId), face = nativeFaces.get(row.faceId), kinds = routes[row.referencePath];
                requireValue(owned && face && owned.face.faceId === row.faceId && owned.wire.wireId === row.wireId && owned.use.edgeId === row.edgeId
                    && !consumed.has(row.coedgeId) && row.sourceFaceItemId === face.nativeItemId && row.associationStatus === 'complete-unique-native-occurrence'
                    && [0, 1].includes(row.sourceOrientation) && (row.sourceOrientation ^ owned.face.orientation) === owned.use.orientation
                    && row.postRangeFirst === owned.use.range[0] && row.postRangeLast === owned.use.range[1] && row.operationId === null && kinds, 'metric-occurrence');
                consumed.add(row.coedgeId);
                var connector = row.kind === 'new-degenerate-connector';
                if (connector) { requireValue(row.sourceEdgeItemId === null && row.sourceRange === null && text(row.sourceVertexItemId) && text(row.connectorProofId)
                    && row.rangeIdentityStatus === 'post-range-captured;source-range-unavailable' && row.metricMethod === 'outward-degenerate-point-image'
                    && row.referencePath === 'whole-native-degenerate-lift-to-preserved-source-vertex', 'connector-metric'); }
                else { var method = '(?:correlated|shared-(?:polynomial|cylinder-trig|rational-polynomial|high-axis-rational)(?:-then-correlated)?)';
                    requireValue(row.kind === 'source-coedge' && text(row.sourceEdgeItemId) && row.sourceVertexItemId === null && row.connectorProofId === null
                        && object(row.sourceRange) && Number.isFinite(row.sourceRange.first) && Number.isFinite(row.sourceRange.last) && row.rangeIdentityStatus === 'source-and-post-ranges-captured'
                        && new RegExp(row.referencePath === 'old-lift-via-source-3d-to-new-lift' ? '^new:' + method + ';old:' + method + '$' : '^' + method + '$').test(row.metricMethod)
                        && row.referencePath !== 'whole-native-degenerate-lift-to-preserved-source-vertex', 'ordinary-metric'); }
                var ops = v.periodicConversions.filter(function (op) { return op.faceId === row.faceId; });
                bounded(row, kinds.concat(ops.map(function () { return 'periodic-world-displacement'; })));
                ops.forEach(function (op, i) { requireValue(row.pathTermsMm[kinds.length + i].upperBoundMm === op.worldUpperBoundMm, 'periodic-coedge-composition'); });
            });
            requireValue(setEqual(Array.from(consumed), Array.from(coedges.keys())), 'metric-inverse');
            var checks = index(v.nativeChecks.rows, 'checkId'), expectedChecks = new Set(), checkTuples = new Set();
            faceMap.forEach(function (f) {
                ['IntersectWires', 'ClassifyWires', 'OrientationOfWires', 'IsUnorientable'].forEach(function (m) { expectedChecks.add([f.faceId, 'face', f.faceId, 'BRepCheck_Face.' + m].join('|')); });
                f.trims.wires.forEach(function (w) { ['Closed', 'Closed2d', 'Orientation', 'SelfIntersect'].forEach(function (m) { expectedChecks.add([f.faceId, 'wire', w.wireId, 'BRepCheck_Wire.' + m].join('|')); }); });
            });
            requireValue(v.nativeChecks.required === checks.size && v.nativeChecks.passed === checks.size && checks.size > 0
                && ['failed', 'unavailable', 'notEvaluated'].every(function (k) { return v.nativeChecks[k] === 0; }), 'check-counts');
            checks.forEach(function (c) {
                requireValue(Array.isArray(c.subjectIds) && c.subjectIds.length === 1 && c.subjectIdentityStatus === 'complete' && c.status === 'passed' && c.nativeStatus === 0
                    && c.reason === '' && typeof c.reusedAnalyzerResult === 'boolean' && equal(c.offendingEdgeIds, []) && c.offendingEdgeIdentityStatus === 'not-applicable', 'native-check');
                var tuple = [c.faceId, c.subjectKind, c.subjectIds[0], c.method].join('|'); requireValue(expectedChecks.has(tuple) && !checkTuples.has(tuple), 'check-universe'); checkTuples.add(tuple);
            }); requireValue(checkTuples.size === expectedChecks.size, 'check-universe');
            function faceChecks(faceId) { return v.nativeChecks.rows.filter(function (c) { return c.faceId === faceId; }).map(function (c) { return c.checkId; }); }
            var effects = index(v.effects, 'effectId');
            nativeFaces.forEach(function (f) { requireValue(setEqual(f.effects, v.effects.filter(function (e) { return e.faceId === f.faceId; }).map(function (e) { return e.effectId; })) && f.effects.length > 0, 'effect-inverse'); });
            effects.forEach(function (e) {
                requireValue(nativeFaces.has(e.faceId) && e.status === 'checked' && e.joinStatus === 'complete'
                    && ['unchanged', 'tolerance-only', 'source-null-pcurve-construction', 'bounded-existing-pcurve-replacement', 'bounded-endpoint-range-adjustment', 'bounded-singular-connector'].includes(e.kind)
                    && setEqual(e.nativeCheckIds, faceChecks(e.faceId)) && Array.isArray(e.obligationIds) && e.obligationIds.length > 0
                    && setEqual(e.coedgeIds, e.obligationIds.map(function (id) { var row = metrics.get(id); requireValue(row && row.faceId === e.faceId, 'effect-obligation'); return row.coedgeId; })), 'effect');
                if (!['unchanged', 'tolerance-only'].includes(e.kind)) { requireValue(e.obligationIds.length === 1, 'changed-effect-occurrence'); }
                e.obligationIds.forEach(function (id) { var row = metrics.get(id);
                    if (e.kind === 'bounded-singular-connector') { requireValue(row.kind === 'new-degenerate-connector', 'effect-connector-route'); }
                    if (e.kind === 'bounded-existing-pcurve-replacement') { requireValue(row.referencePath === 'old-lift-via-source-3d-to-new-lift', 'effect-replacement-route'); }
                });
            });
            var toleranceRows = v.tolerances.rows, toleranceMap = new Map();
            requireValue(Array.isArray(toleranceRows) && toleranceRows.length > 0 && v.tolerances.required === toleranceRows.length && v.tolerances.evaluated === toleranceRows.length
                && v.tolerances.within === toleranceRows.length && ['unassessed', 'exceeds', 'invalid'].every(function (k) { return v.tolerances[k] === 0; }), 'tolerance-counts');
            toleranceRows.forEach(function (t) { var key = t.nativeItemId + '|' + t.phase;
                requireValue(text(t.nativeItemId) && ['before', 'after', 'final-added'].includes(t.phase) && !toleranceMap.has(key) && t.captured === true && nonnegative(t.valueMm) && t.valueMm <= 0.01, 'tolerance'); toleranceMap.set(key, t.valueMm);
            }); requireValue(v.tolerances.maximumMm === Math.max.apply(Math, toleranceRows.map(function (t) { return t.valueMm; })), 'tolerance-maximum');
            function pair(item) { requireValue(toleranceMap.has(item + '|before') && toleranceMap.has(item + '|after'), 'tolerance-pair'); }
            nativeFaces.forEach(function (f) { pair(f.nativeItemId); requireValue(toleranceMap.get(f.nativeItemId + '|after') === faceMap.get(f.faceId).precision.faceTolerance, 'face-tolerance'); });
            metrics.forEach(function (r) { if (r.sourceEdgeItemId) { pair(r.sourceEdgeItemId); requireValue(toleranceMap.get(r.sourceEdgeItemId + '|after') === edgeMap.get(r.edgeId).tolerance, 'edge-tolerance'); } });
            var proofs = index(v.connectorProofs, 'connectorProofId'), connectorMetrics = v.metrics.rows.filter(function (r) { return r.kind === 'new-degenerate-connector'; }), proofOccurrences = new Set(), normalizedOccurrences = new Set();
            requireValue(proofs.size === connectorMetrics.length && v.effects.filter(function (e) { return e.kind === 'bounded-singular-connector'; }).length === proofs.size, 'connector-universe');
            proofs.forEach(function (proof) {
                var row = metrics.get(proof.obligationId), owned = coedges.get(proof.coedgeId), item = /^(boundary-\d+)\/item-(\d+)$/.exec(proof.sourceFaceItemId);
                requireValue(row && owned && item && /^boundary-\d+\/connector-\d+$/.test(proof.connectorProofId) && proof.connectorProofId.startsWith(item[1] + '/')
                    && proof.status === 'complete' && proof.sourceIdentity === 'new-native-identity-minus-one' && proof.sourceEdgeItemId === null
                    && typeof proof.sourceVertexItemId === 'string' && proof.sourceVertexItemId.startsWith(item[1] + '/item-')
                    && row.kind === 'new-degenerate-connector' && row.connectorProofId === proof.connectorProofId
                    && ['sourceFaceItemId', 'sourceVertexItemId', 'faceId', 'wireId', 'coedgeId', 'edgeId', 'referencePath', 'metricMethod'].every(function (k) { return proof[k] === row[k]; })
                    && ['postCoedgeOccurrence', 'normalizedLoopOccurrence', 'normalizedLoopCoedgeOccurrence'].every(function (k) { return count(proof[k]); })
                    && proof.obligationId === item[1] + '/post-face-item:' + item[2] + '/coedge-occurrence:' + proof.postCoedgeOccurrence
                    && owned.face.trims.wires[proof.normalizedLoopOccurrence] === owned.wire && owned.wire.coedges[proof.normalizedLoopCoedgeOccurrence] === owned.use
                    && proof.startVertexId === proof.endVertexId && text(proof.startVertexId) && owned.use.startVertexId === proof.startVertexId && owned.use.endVertexId === proof.endVertexId
                    && edgeMap.get(proof.edgeId).degenerated === true && setEqual(proof.nativeCheckIds, faceChecks(proof.faceId)), 'connector-proof');
                var occurrence = proof.sourceFaceItemId + '|' + proof.postCoedgeOccurrence, normalized = proof.sourceFaceItemId + '|' + proof.normalizedLoopOccurrence + '|' + proof.normalizedLoopCoedgeOccurrence;
                requireValue(!proofOccurrences.has(occurrence) && !normalizedOccurrences.has(normalized), 'connector-occurrence-unique'); proofOccurrences.add(occurrence); normalizedOccurrences.add(normalized); pair(proof.sourceVertexItemId);
                requireValue(v.effects.filter(function (e) { return e.kind === 'bounded-singular-connector' && setEqual(e.obligationIds, [proof.obligationId]) && setEqual(e.coedgeIds, [proof.coedgeId]); }).length === 1, 'connector-effect');
            });
            connectorMetrics.forEach(function (r) { requireValue(proofs.has(r.connectorProofId) && proofs.get(r.connectorProofId).obligationId === r.obligationId, 'connector-inverse'); });
            var rawDiagnostics = index(transfer.diagnostics, 'diagnosticOccurrenceId'), diagnostics = index(v.diagnostics.rows, 'diagnosticOccurrenceId');
            requireValue(rawDiagnostics.size === diagnostics.size && v.diagnostics.required === diagnostics.size && v.diagnostics.classified === diagnostics.size
                && v.diagnostics.unclassified === 0 && v.diagnostics.sourceIdentityFailures === 0 && v.diagnostics.captureFailures === 0
                && transfer.failureCount === 0 && transfer.warningCount === rawDiagnostics.size, 'diagnostic-partition');
            diagnostics.forEach(function (row) {
                var raw = rawDiagnostics.get(row.diagnosticOccurrenceId);
                requireValue(raw && raw.severity === 'warning' && count(raw.entityNumber) && raw.entityNumber > 0 && /^native-delivery-\d+$/.test(raw.nativeDeliveryId)
                    && row.nativeDeliveryId === raw.nativeDeliveryId && row.disposition === 'checked-native-operation-effects' && Array.isArray(row.affectedFaceIds)
                    && row.affectedFaceIds.length > 0 && setEqual(row.affectedFaceIds, Array.from(new Set(row.affectedFaceIds))) && row.affectedFaceIds.every(function (id) { return nativeFaces.has(id); }), 'diagnostic-lineage');
                if (periodic.has(row.operationId)) { var op = periodic.get(row.operationId); requireValue(raw.entityNumber === op.sourceSurfaceEntityNumber && setEqual(row.affectedFaceIds, [op.faceId]), 'diagnostic-periodic'); }
                else { requireValue(/^intersection-\d+$/.test(row.operationId), 'diagnostic-operation'); }
            });
            // This expectation belongs to the verified loader/source attachment boundary. It is
            // deliberately absent from the envelope and cannot be selected by imported JSON.
            var trusted = expectation && expectation.sourceBytesHash === n.sourceBytesHash && expectation.sourceAttachmentVerified === true
                && expectation.manifestUrl && /^\/lib\/occt-cnc\/[a-f0-9]+-[a-f0-9]+\/manifest\.js$/.test(expectation.manifestUrl)
                && /^[a-f0-9]{64}$/.test(expectation.manifestSha256 || '') && equal(expectation.buildIdentity, n.buildIdentity)
                && expectation.errorBudgetMm === 0.01 && expectation.assessmentTimeLimitMs === policy.assessmentTimeLimitMs;
            return { validContract: true, interpretationAccepted: !!trusted, status: status, reasons: trusted ? [] : ['native_loader_source_expectation_missing'] };
        } catch (error) {
            if (!(error instanceof Error) || !error.message.startsWith('native_contract_invalid:')) { return { validContract: false, interpretationAccepted: false, status: 'contract-invalid', reasons: ['native_contract_invalid:structure'] }; }
            return { validContract: false, interpretationAccepted: false, status: 'contract-invalid', reasons: [error.message] };
        }
    }
    // Arrays intentionally remain JS doubles. Display Float32 buffers are not source evidence.
    function payload(value) {
        value = project(value);
        return { contract: value.contract, nativeSemanticBasis: value.nativeSemanticBasis, sourceBytesHash: value.sourceBytesHash, sourceFormat: value.sourceFormat,
            buildIdentity: value.buildIdentity, tessellation: value.tessellation, analysisProfile: value.analysisProfile,
            kernelProvenance: value.kernelProvenance, meshes: value.meshes };
    }
    async function hash(value) { return root.CncPlanContracts.hash(payload(value)); }
    async function create(bytes, format, buildIdentity, result) {
        var digest = await root.crypto.subtle.digest('SHA-256', bytes);
        var sourceBytesHash = Array.from(new Uint8Array(digest), function (b) { return b.toString(16).padStart(2, '0'); }).join('');
        var envelope = { contract: 'CncNativeImport.v1', nativeSemanticBasis: basis, analysisProfile: 'cnc', sourceBytesHash: sourceBytesHash,
            sourceFormat: String(format).toLowerCase(), buildIdentity: buildIdentity, tessellation: parameters,
            kernelProvenance: result.kernelProvenance, meshes: result.meshes };
        envelope.importRevision = await hash(envelope); return envelope;
    }
    root.CncNativeInterpretation = Object.freeze({ assess: assess, project: project, projectDocument: projectDocument, nextUp: nextUp, outwardSum: outwardSum, semanticBasis: basis });
    root.CncCadDocument = Object.freeze({ create: create, hash: hash, importParameters: parameters, semanticProjection: project, projectDocument: projectDocument });
}(typeof self !== 'undefined' ? self : globalThis));
