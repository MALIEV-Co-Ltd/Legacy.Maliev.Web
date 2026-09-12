const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
// Execute the production successful callback and request builder. UI/upload-only
// effects are inert; source-generation ownership and association are not mocked.
function deliveryPage() {
    const page = fs.readFileSync(path.resolve(__dirname, '../../Legacy.Maliev.Web/Pages/InstantQuotation/CNC-Machining.cshtml'), 'utf8');
    const items = new Map(), callbacks = [];
    const utils = { IncreasePendingTask() {}, DecreasePendingTask() {}, RegisterItem(id, file) { items.set(id, { id, file }); },
        GetItem: id => items.get(id), GetActiveId: () => 1, SetActiveItem() {}, SetItemPricingPending() {},
        SetItemParsed(id, object, info) { items.get(id).modelInfo = info; }, SetItemPreview() {} };
    const c = vm.createContext({ console, utils, nextItemId: 1, maxUploadBytes: 1e8, parseHandles: {},
        cncSourceParses: new WeakMap(), nextCncSourceGeneration: 0, document: { getElementById: () => null }, alert() {},
        viewer: { ParseFile(file, callback) { callbacks.push(callback); return { cancel() {} }; } },
        IsAllowedRouteModelFile: () => true, TrackQuoteStarted() {}, DefaultCncRequirements: () => ({}), SyncCncItemControls() {},
        UpdateEmptyHint() {}, NotifyQuotationStateChanged() {}, RenderCurrentCncQuotation() {},
        EnqueuePartProcessing: (id, start) => start(), FinishPartProcessing() {}, QueueModelUpload() {}, setTimeout() {},
        CncRequirementInput: () => ({}), CncRequirementsForItem: () => ({}), CncFinishForItem: () => null });
    for (const name of ['BeginCncSourceParse', 'OwnsCncSourceParse', 'CaptureCncNativeAssociation', 'CncNativeAssociationForItem',
        'AddModel', 'CncGeometryForItem', 'BuildCncWorkerEstimateRequest']) {
        const start = page.indexOf('        function ' + name + '('), end = page.indexOf('\n        function ', start + 1);
        if (start < 0 || end < 0) throw new Error('production callback harness function missing: ' + name);
        vm.runInContext(page.slice(start, end).replaceAll('@maxUploadMb', '100'), c);
    }
    return { deliver(result) {
        c.AddModel([{ name: 'public-thread.step', size: 100 }]);
        callbacks.at(-1)(structuredClone(result.modelInfo), {}, null, structuredClone(result.cncGeometry));
        const item = items.get(1); item.analysisRevision = 'thread-analysis'; item.cncRequirementsRevision = 'thread-requirements';
        return JSON.parse(JSON.stringify(c.BuildCncWorkerEstimateRequest(item, '6061', 1, true)));
    } };
}
module.exports = { deliveryPage };
