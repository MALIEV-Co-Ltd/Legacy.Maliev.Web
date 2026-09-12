const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const { runtime } = require('./cnc-model-worker-harness.cjs');
const fixtureRoot = path.resolve(__dirname, '../TestAssets/CncNativeInterpretation');
const plain = x => JSON.parse(JSON.stringify(x));
function pageRuntime() {
    const page = fs.readFileSync(path.resolve(__dirname, '../../Legacy.Maliev.Web/Pages/InstantQuotation/CNC-Machining.cshtml'), 'utf8');
    const items = new Map(), parses = [], deferred = [], estimates = [];
    const utils = { IncreasePendingTask() {}, DecreasePendingTask() {}, RegisterItem(id, file) { items.set(id, { id, file }); },
        GetItem: id => items.get(id), GetActiveId: () => 1, SetActiveItem() {}, SetItemPricingPending() {},
        SetItemParsed(id, object, info) { items.get(id).modelInfo = info; }, SetItemPreview() {} };
    const c = vm.createContext({ console, utils, nextItemId: 1, maxUploadBytes: 1e8, parseHandles: {},
        cncSourceParses: new WeakMap(), nextCncSourceGeneration: 0,
        document: { getElementById: () => null }, alert() {},
        viewer: { ParseFile(file, callback, options) { const handle = { cancel() {} }; parses.push({ file, callback, options, handle }); return handle; } },
        IsAllowedRouteModelFile: () => true, TrackQuoteStarted() {}, DefaultCncRequirements: () => ({}), SyncCncItemControls() {},
        UpdateEmptyHint() {}, NotifyQuotationStateChanged() {}, RenderCurrentCncQuotation() {},
        EnqueuePartProcessing: (id, start) => start(), FinishPartProcessing() {}, QueueModelUpload() {},
        setTimeout: callback => deferred.push(callback),
        GetCncEstimate(id) { estimates.push(id); return Promise.resolve(); },
        CncRequirementInput: () => ({}), CncRequirementsForItem: () => ({}), CncFinishForItem: () => null });
    for (const name of ['BeginCncSourceParse', 'OwnsCncSourceParse', 'CaptureCncNativeAssociation', 'CncNativeAssociationForItem',
        'AddModel', 'CncGeometryForItem', 'BuildCncWorkerEstimateRequest']) {
        const start = page.indexOf('        function ' + name + '(');
        if (start < 0) continue; // Existing page runs before the new lifecycle helpers are implemented.
        const next = page.indexOf('\n        function ', start + 1);
        vm.runInContext(page.slice(start, next).replaceAll('@maxUploadMb', '100'), c);
    }
    function add() { c.AddModel([{ name: 'ring.step', size: 100 }]); return items.get(c.nextItemId - 1); }
    function deliver(result, index = parses.length - 1) { parses[index].callback(plain(result.modelInfo), {}, null, plain(result.cncGeometry)); }
    return { c, items, parses, deferred, estimates, add, deliver };
}
async function parsed(f, name = 'ring') {
    const bytes = fs.readFileSync(path.join(fixtureRoot, name + '.step'));
    const preview = await f.send({ action: 'parse', jobId: 'dispatch', extension: 'step', analysisProfile: 'cnc',
        buffer: Uint8Array.from(bytes).buffer, deferCncAnalysis: true, retainNativeAnalysis: true });
    assert.equal(preview.stage, 'native-preview');
    const result = await f.send({ action: 'continue-native-analysis', jobId: 'dispatch' });
    assert.equal(result.success, true);
    return result;
}
test('retained native worker enumerates ring owners without enabling manufacturing', async () => {
    const f = runtime(), result = await parsed(f), g = result.cncGeometry.manufacturingFeatureGraph;
    assert.equal(g.features.length, 4, 'native owners must survive the manufacturing eligibility gate');
    assert.deepEqual(g.features.map(x => x.kind).sort(), ['datum', 'datum', 'hole', 'outside_profile']);
    assert.equal(g.recognitionReadiness.status, 'ready');
    assert.equal(g.automaticPlanningEligible, false);
    assert.equal(result.cncGeometry.cadTopology.automaticPlanningEligible, false);
    assert.equal(Object.keys(g.faceOwners).length, 4);
    assert.equal(g.features.every(x => x.accessAxes.length === 0 && x.machiningRequired === null), true);
});

test('native input cannot choose legacy recognizers by setting manufacturing eligibility', async () => {
    const f = runtime(), result = await parsed(f), topology = plain(result.cncGeometry.cadTopology);
    topology.automaticPlanningEligible = true;
    assert.throws(() => f.c.CncFeatureGraph.build(topology), /native_recognition_requires_local_dispatch/);
});

test('page rejects removed or reused item callbacks before mutating model information', () => {
    const p = pageRuntime(); p.add(); const old = p.parses[0];
    const replacement = { id: 1, file: { name: 'new.step' } }; p.items.set(1, replacement);
    old.callback({ size: { x: 1, y: 1, z: 1 } }, {}, null, {});
    assert.equal(replacement.modelInfo, undefined, 'old parse must not mutate a replacement sharing its ID');
    assert.equal(p.deferred.length, 0, 'old source must never schedule pricing');
});

test('owned callback binds native evidence independently of later payload across material A/B/A', async () => {
    const f = runtime(), result = await parsed(f), p = pageRuntime(), item = p.add();
    p.deliver(result);
    const q = runtime(); q.c.importScripts('/src/app/js/cnc-quotation/cnc-quotation.worker.js');
    const importerCount = f.state.nativeImports;
    for (const [index, alloy] of ['6061', '304', '6061'].entries()) {
        item.analysisRevision = 'analysis-' + index; item.cncRequirementsRevision = 'requirements-' + index;
        const request = p.c.BuildCncWorkerEstimateRequest(item, alloy, 1, true);
        assert.ok(request.nativeSourceAssociation, 'page request needs its independently captured association');
        const estimate = await q.c.CncQuotationWorker.estimate(plain(request));
        assert.equal(estimate.quote, null);
        assert.equal(estimate.analysisRevision, item.analysisRevision);
        assert.equal(estimate.featureGraph.features.length, 4);
        assert.ok(estimate.reviewReasons.includes('native_machining_role_unverified'));
    }
    assert.equal(q.c.CncQuotationWorker.counters.nativeGeometryValidations, 1);
    assert.equal(f.state.nativeImports, importerCount);
    item.modelInfo.cncGeometry.nativeSourceAssociation = { sourceVerified: true };
    const request = p.c.BuildCncWorkerEstimateRequest(item, '6061', 1, true);
    assert.notEqual(request.nativeSourceAssociation.sourceVerified, true, 'later payload cannot rebind source');
    delete request.nativeSourceAssociation;
    assert.ok((await q.c.CncQuotationWorker.estimate(plain(request))).reviewReasons.includes('native_source_association_required'));
});

test('transport rejects identity, face partition and semantic tampering even after outer rehash', async () => {
    const f = runtime(), result = await parsed(f), p = pageRuntime(), item = p.add(); p.deliver(result);
    item.analysisRevision = 'a'; item.cncRequirementsRevision = 'r';
    const original = plain(p.c.BuildCncWorkerEstimateRequest(item, '6061', 1, true));
    const q = runtime(); q.c.importScripts('/src/app/js/cnc-quotation/cnc-quotation.worker.js');
    assert.equal((await q.c.CncQuotationWorker.estimate(structuredClone(original))).featureGraph.features.length, 4, 'populate the content cache before mutations');
    for (const [name, mutate] of [
        ['source', r => { r.topology.cadDocument.nativeImport.sourceBytesHash = 'a'.repeat(64); }],
        ['import', r => { r.topology.cadDocument.nativeImport.importRevision = 'a'.repeat(64); }],
        ['topology', r => { r.topology.faces[0].surface.radiusMm = 999; }],
        ['diagnostic', r => { r.geometry.nativeFeatureDiagnostics.revision = 'a'.repeat(64); }],
        ['omitted', r => { r.featureGraph.features.pop(); }],
        ['foreign', r => { r.featureGraph.features[0].primaryFaceIds = ['foreign']; }],
        ['double', r => { r.featureGraph.features[0].primaryFaceIds.push(r.featureGraph.features[1].primaryFaceIds[0]); }],
        ['access', r => { r.featureGraph.features.find(x => x.kind === 'hole').accessAxes = [{ x: 0, y: 0, z: 1 }]; }],
        ['runtime', r => { r.nativeSourceAssociation.sourceExpectation.buildIdentity.jsSha256 = 'a'.repeat(64); }]
    ]) {
        const request = structuredClone(original); mutate(request);
        request.geometry.cadTopology = structuredClone(request.topology);
        request.geometry.manufacturingFeatureGraph = structuredClone(request.featureGraph);
        const estimate = await q.c.CncQuotationWorker.estimate(request);
        assert.equal(estimate.quote, null, name);
        assert.equal(estimate.featureGraph, undefined, name + ' must not expose accepted native owners');
        assert.ok(estimate.reviewReasons.some(x => /native_.*mismatch/.test(x)), name + ': ' + estimate.reviewReasons);
    }
    const request = structuredClone(original), diagnostic = request.geometry.nativeFeatureDiagnostics;
    diagnostic.features.find(x => x.kind === 'hole').dimensions.diameterMm = 999;
    delete diagnostic.revision; diagnostic.revision = await q.c.CncPlanContracts.hash(diagnostic);
    request.nativeSourceAssociation.diagnosticRevision = diagnostic.revision;
    request.featureGraph.nativeDiagnosticRevision = diagnostic.revision;
    delete request.featureGraph.revision; request.featureGraph.revision = await q.c.CncPlanContracts.hash(request.featureGraph);
    request.nativeSourceAssociation.featureGraphRevision = request.featureGraph.revision;
    request.geometry.manufacturingFeatureGraph = structuredClone(request.featureGraph);
    const rejected = await q.c.CncQuotationWorker.estimate(request);
    assert.ok(rejected.reviewReasons.includes('native_transported_recognition_mismatch'));
});

test('adapter preserves identifiable faces when optional native metadata is malformed', async () => {
    const f = runtime(), result = await parsed(f);
    for (const remove of [t => { delete t.cadDocument.nativeImport.kernelProvenance; },
        t => { delete t.cadDocument; }, t => { delete t.cadDocument.nativeImport.meshes[0].brep_faces[0].nativeRegionMeasures; }]) {
        const topology = plain(result.cncGeometry.cadTopology); remove(topology);
        const derived = await f.c.CncNativeDispatch.buildLocal(topology);
        assert.equal(derived.graph.features.length, 4);
        assert.equal(derived.graph.features.every(x => x.kind === 'unresolved'), true);
        assert.equal(derived.graph.recognitionReadiness.sourceVerified, false);
        assert.equal(derived.graph.automaticPlanningEligible, false);
    }
});

test('conflicting top-level and geometry aliases reject even after a valid cache hit', async () => {
    const f = runtime(), result = await parsed(f), p = pageRuntime(), item = p.add(); p.deliver(result);
    item.analysisRevision = 'a'; item.cncRequirementsRevision = 'r';
    const request = plain(p.c.BuildCncWorkerEstimateRequest(item, '6061', 1, true));
    const q = runtime(); q.c.importScripts('/src/app/js/cnc-quotation/cnc-quotation.worker.js');
    assert.equal((await q.c.CncQuotationWorker.estimate(request)).featureGraph.features.length, 4);
    for (const change of [r => { r.geometry.manufacturingFeatureGraph.features.pop(); },
        r => { r.geometry.cadTopology.faces.pop(); }]) {
        const altered = structuredClone(request); change(altered);
        const rejected = await q.c.CncQuotationWorker.estimate(altered);
        assert.equal(rejected.featureGraph, undefined);
        assert.ok(rejected.reviewReasons.includes('native_payload_alias_mismatch'));
    }
});

test('full native geometry cache evicts old entries and excludes evidence exceeding its byte budget', async () => {
    const f = runtime(), result = await parsed(f), p = pageRuntime(), item = p.add(); p.deliver(result);
    item.analysisRevision = 'a'; item.cncRequirementsRevision = 'r';
    const request = plain(p.c.BuildCncWorkerEstimateRequest(item, '6061', 1, true));
    const q = runtime(); q.c.importScripts('/src/app/js/cnc-quotation/cnc-quotation.worker.js');
    for (let i = 0; i < 5; i++) {
        const next = structuredClone(request); next.nativeSourceAssociation.sourceGeneration = 'item-' + i;
        assert.equal((await q.c.CncQuotationWorker.estimate(next)).featureGraph.features.length, 4);
    }
    assert.ok(q.c.nativeGeometryCache.size <= 4, 'full native payloads need their own small entry bound');
    const first = structuredClone(request); first.nativeSourceAssociation.sourceGeneration = 'item-0';
    const count = q.c.CncQuotationWorker.counters.nativeGeometryValidations;
    await q.c.CncQuotationWorker.estimate(first);
    assert.equal(q.c.CncQuotationWorker.counters.nativeGeometryValidations, count + 1, 'oldest geometry was evicted');
    // Exercise the real serialized-size admission path without allocating a giant CAD file.
    q.c.MAX_NATIVE_CACHE_BYTES = 1;
    const oversized = structuredClone(request); oversized.nativeSourceAssociation.sourceGeneration = 'over-budget';
    assert.equal((await q.c.CncQuotationWorker.estimate(oversized)).featureGraph.features.length, 4);
    assert.equal(q.c.nativeGeometryCache.size, 0, 'oversized evidence remains valid but is not retained');
    assert.equal(q.state.nativeImports, 0);
});

test('native request markers prevent legacy fallback even when topology markers or supplied graph are missing', async () => {
    const q = runtime(); q.c.importScripts('/src/app/js/cnc-quotation/cnc-quotation.worker.js');
    let legacyCalls = 0; const legacy = q.c.CncFeatureGraph;
    q.c.CncFeatureGraph = { build(...args) { legacyCalls++; return legacy.build(...args); } };
    const result = await q.c.CncQuotationWorker.estimate({ analysisRevision: 'a', geometryRevision: 'r',
        topology: { contract: 'CncCadTopology.v1', revision: 'r', automaticPlanningEligible: false, faces: [], unresolvedReasons: ['missing'] },
        nativeSourceAssociation: { contract: 'NativeSourceAssociation.v1' }, geometry: {} });
    assert.equal(result.quote, null);
    assert.equal(legacyCalls, 0, 'native request cannot enter a legacy recognizer before transport rejection');
});

test('unavailable native regions retain incomplete face ownership and required reasons', async () => {
    const f = runtime(), result = await parsed(f, 'ring-default-precision'), graph = result.cncGeometry.manufacturingFeatureGraph;
    assert.equal(result.cncGeometry.cadTopology.nativeInterpretation.interpretationAccepted, true);
    assert.equal(graph.features.length, 4);
    assert.equal(graph.recognitionReadiness.status, 'incomplete');
    assert.ok(graph.unresolved.some(x => x.reason === 'native_band_unavailable:parameter_boundary_ambiguous' && x.required));
});

test('same item retry and replacement after success cannot dispatch stale geometry', async () => {
    const f = runtime(), result = await parsed(f), p = pageRuntime(), item = p.add();
    p.c.BeginCncSourceParse(item);
    p.deliver(result);
    assert.equal(item.modelInfo, undefined);
    const removed = pageRuntime(); removed.add(); removed.items.delete(1); removed.deliver(result);
    assert.equal(removed.deferred.length, 0);
    const fresh = pageRuntime(), current = fresh.add(); fresh.deliver(result);
    fresh.items.set(1, { id: 1, file: { name: 'replacement.step' } });
    fresh.deferred.forEach(callback => callback());
    assert.equal(fresh.estimates.length, 0);
    assert.equal(fresh.c.CncNativeAssociationForItem(current), null);
});

test('actual manager and ParseFile continuation delivers owned native evidence through page to quotation message handler', async () => {
    const p = pageRuntime(), f = runtime(), q = runtime(), actions = [];
    q.c.importScripts('/src/app/js/cnc-quotation/cnc-quotation.worker.js');
    p.c.window = p.c; p.c.setTimeout = setTimeout; p.c.clearTimeout = clearTimeout;
    p.c.Worker = class {
        constructor() {
            f.c.postMessage = (message, transfer) => {
                const copied = structuredClone(message, { transfer: transfer || [] });
                queueMicrotask(() => this.onmessage({ data: copied }));
            };
        }
        postMessage(message, transfer) { actions.push(message.action); f.c.onmessage({ data: structuredClone(message, { transfer: transfer || [] }) }); }
        terminate() { throw new Error('unexpected worker termination'); }
    };
    const source = fs.readFileSync(path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot/src/app/js/model-viewer/model-viewer.js'), 'utf8');
    vm.runInContext(source, p.c);
    p.c.ModelParseWorkerManager = p.c.CreateModelWorkerManager(1);
    p.c.ReadArrayBuffer = (file, ready) => ready(Uint8Array.from(fs.readFileSync(path.join(fixtureRoot, file.name))).buffer);
    p.c.EnsurePreviewGeometryNormals = p.c.ApplyMultiBodyPreviewColors = p.c.AddCadPreviewEdges = () => {};
    p.c.BuildGroupFromMeshBuffersAsync = async meshes => { assert.ok(meshes[0].position.length); return { userData: {} }; };
    p.c.requestAnimationFrame = callback => setTimeout(callback, 0);
    p.c.UpdateSnapshot = p.c.RenderCncActiveMetrics = () => {};
    const begin = source.indexOf('    this.ParseFile = function (file, onReady, options) {');
    const end = source.indexOf('    // Displays the given (already-parsed) object3D', begin);
    const parser = vm.runInContext('(function () {' + source.slice(begin, end) + 'return this; }).call({})', p.c);
    parser.AdjustCanvasSize = () => {}; p.c.viewer = parser;
    let resolveEstimate;
    const completed = new Promise(resolve => { resolveEstimate = resolve; });
    p.c.GetCncEstimate = async id => {
        const item = p.items.get(id); item.analysisRevision = 'owned-analysis'; item.cncRequirementsRevision = 'owned-requirements';
        const request = plain(p.c.BuildCncWorkerEstimateRequest(item, '6061', 1, true));
        const messages = [];
        q.c.postMessage = message => messages.push(plain(message));
        await q.c.onmessage({ data: { requestId: 'page-request-1', progressive: true, request } });
        resolveEstimate({ request, messages });
    };
    p.add();
    const { request, messages } = await completed;
    assert.deepEqual(actions, ['parse', 'continue-native-analysis']);
    assert.equal(f.state.nativeImports, 1);
    const final = messages.at(-1);
    assert.equal(final.success, true); assert.equal(final.requestId, 'page-request-1');
    assert.equal(final.analysisRevision, 'owned-analysis'); assert.equal(final.estimate.quote, null);
    assert.equal(final.estimate.featureGraph.features.length, 4);
    assert.equal(request.nativeSourceAssociation.sourceGeneration, 'cnc-source-1');
    assert.equal(final.estimate.operationGraph.operations.length, 0);
    assert.equal(final.estimate.featureGraph.recognitionReadiness.policy.errorBudgetMm, 0.01);
    assert.equal(q.state.nativeImports, 0);
});

module.exports = { parsed, plain, pageRuntime };
