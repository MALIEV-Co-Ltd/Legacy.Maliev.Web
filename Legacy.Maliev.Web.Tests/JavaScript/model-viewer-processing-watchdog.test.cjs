const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const sourcePath = path.resolve(__dirname,
    '../../Legacy.Maliev.Web/wwwroot/src/app/js/model-viewer/model-viewer.js');

function createElement(tag = 'div') {
    const children = [];
    const classes = new Set();
    return {
        tagName: tag.toUpperCase(), children, dataset: {}, style: {}, hidden: false,
        value: '', textContent: '', id: '', className: '',
        classList: {
            add: (...names) => names.forEach(name => classes.add(name)),
            remove: (...names) => names.forEach(name => classes.delete(name)),
            toggle: (name, force) => force === undefined
                ? (classes.has(name) ? (classes.delete(name), false) : (classes.add(name), true))
                : (force ? classes.add(name) : classes.delete(name), force),
            contains: name => classes.has(name)
        },
        appendChild(child) { children.push(child); return child; },
        replaceChildren(...next) { children.splice(0, children.length, ...next); },
        addEventListener() {}, setAttribute(name, value) { this[name] = String(value); },
        removeAttribute(name) { delete this[name]; },
        remove() {}, querySelector(selector) {
            if (selector === '.iq-thumb-status-text') {
                return children.find(child => child.className === 'iq-thumb-status-text') || null;
            }
            return null;
        },
        querySelectorAll() { return []; }
    };
}

function runtime(process = 'cnc') {
    let nextTimer = 1;
    const timers = new Map();
    const timerDelays = new Map();
    const workers = [];
    const elements = new Map();
    const root = createElement();
    root.dataset.process = process;
    elements.set('instant-quotation-component', root);
    elements.set('parts-strip', createElement());
    elements.set('order-summary-items-collection', createElement());
    elements.set('submit-gate', createElement('input'));
    const document = {
        documentElement: { clientWidth: 1280 },
        getElementById(id) { return elements.get(id) || null; },
        createElement(tag) {
            const element = createElement(tag);
            Object.defineProperty(element, 'id', {
                get() { return this._id || ''; },
                set(value) { this._id = String(value); elements.set(this._id, this); }
            });
            return element;
        },
        querySelectorAll() { return []; }, querySelector() { return null; }
    };
    class FakeWorker {
        constructor(url) { this.url = url; this.messages = []; this.terminated = false; workers.push(this); }
        postMessage(message) { this.messages.push(message); }
        terminate() { this.terminated = true; }
    }
    const context = vm.createContext({ console, document, Intl, Number, Object, Array, Math,
        Worker: FakeWorker, alert() {} });
    context.window = context;
    context.innerWidth = 1280;
    context.addEventListener = () => {};
    context.setTimeout = context.window.setTimeout = (callback, delay) => {
        const id = nextTimer++;
        timers.set(id, callback);
        timerDelays.set(id, delay);
        return id;
    };
    context.clearTimeout = context.window.clearTimeout = id => {
        timers.delete(id);
        timerDelays.delete(id);
    };
    vm.runInContext(fs.readFileSync(sourcePath, 'utf8'), context, { filename: sourcePath });
    const viewer = { CaptureView() {}, ShowObject() {}, DisposeObject() {}, AdjustCanvasSize() {} };
    const utils = new context.ModelViewerUtils('en', 'THB', viewer);
    function register(id) {
        utils.RegisterItem(id, { name: id + '.step', size: 10 });
        return utils.GetItem(id);
    }
    function expireAll() {
        const callbacks = [...timers.values()];
        timers.clear();
        callbacks.forEach(callback => callback());
    }
    return { utils, register, expireAll, timers, timerDelays, elements, workers, context };
}

test('queued geometry beyond the presentation deadline stays queued without an error', () => {
    const { utils, register, expireAll, timers } = runtime('cnc');
    const item = register('1');
    utils.SetItemProcessingStage('1', 'geometry', 'queued');
    assert.equal(timers.size, 0);
    expireAll();
    assert.equal(item.errorMessage, null);
    assert.equal(item.processingStages.geometry, 'queued');
});

test('long sequential stages do not share a whole-item wall clock', () => {
    const { utils, register, expireAll, timers } = runtime('cnc');
    const item = register('1');
    for (const stage of ['geometry', 'analysis', 'stock', 'planning', 'pricing']) {
        utils.SetItemProcessingStage('1', stage, 'processing');
        assert.equal(timers.size, 0);
        expireAll();
        utils.SetItemProcessingStage('1', stage, 'ready');
    }
    assert.equal(item.errorMessage, null);
});

for (const process of ['cnc', 'printing']) {
    test(process + ' active upload has a bounded recoverable watchdog', () => {
        const { utils, register, expireAll, timers } = runtime(process);
        const item = register('1');
        const uploadAttempt = utils.BeginItemUpload('1');
        assert.equal(timers.size, 1);
        expireAll();
        assert.equal(item.watchdogError.stage, 'upload');
        assert.match(item.errorMessage, /too long/i);
        utils.SetItemUploadComplete('1', true, uploadAttempt);
        assert.equal(item.errorMessage, null);
        assert.equal(item.uploadComplete, true);
    });
}

test('active thumbnail timeout settles as preview unavailable without clearing unrelated failures', () => {
    const { utils, register, expireAll } = runtime('cnc');
    const item = register('1');
    const firstThumbnailAttempt = utils.BeginItemThumbnail('1');
    expireAll();
    assert.equal(item.watchdogError.stage, 'thumbnail');
    utils.SetItemThumbnailComplete('1', false, firstThumbnailAttempt);
    assert.equal(item.errorMessage, null);
    assert.equal(item.thumbnailAvailable, false);
    utils.SetItemFailed('1', 'Security scan rejected this file');
    const secondThumbnailAttempt = utils.BeginItemThumbnail('1');
    expireAll();
    utils.SetItemThumbnailComplete('1', true, secondThumbnailAttempt);
    assert.equal(item.errorMessage, 'Security scan rejected this file');
});

test('removal and reused ids reject stale stage callbacks', () => {
    const { utils, register, expireAll, timers } = runtime('cnc');
    const first = register('1');
    utils.BeginItemUpload('1');
    assert.equal(timers.size, 1);
    utils.RemoveItem('1');
    assert.equal(timers.size, 0);
    const second = register('1');
    utils.BeginItemUpload('1');
    expireAll();
    assert.notEqual(first, second);
    assert.equal(first.errorMessage, null);
    assert.equal(second.watchdogError.stage, 'upload');
});

test('multiple queued files and worker-queue waits never arm presentation timers', () => {
    const { utils, register, timers } = runtime('cnc');
    for (let id = 1; id <= 8; id++) {
        register(String(id));
        utils.SetItemProcessingStage(String(id), 'geometry', 'queued');
    }
    assert.equal(timers.size, 0);
});

test('restarting a stage creates a new attempt and stale timeout cannot poison it', () => {
    const { utils, register, timers } = runtime('cnc');
    const item = register('1');
    utils.BeginItemUpload('1');
    const stale = [...timers.values()][0];
    const currentAttempt = utils.BeginItemUpload('1');
    stale();
    assert.equal(item.errorMessage, null);
    assert.equal(timers.size, 1);
    utils.SetItemUploadComplete('1', true, currentAttempt);
    assert.equal(timers.size, 0);
});

test('watchdog expiry and settlement do not decrement shared pending work', () => {
    const { utils, register, expireAll, elements } = runtime('printing');
    register('1');
    const submit = elements.get('submit-gate');
    utils.IncreasePendingTask();
    utils.IncreasePendingTask();
    const uploadAttempt = utils.BeginItemUpload('1');
    expireAll();
    utils.SetItemUploadComplete('1', true, uploadAttempt);
    utils.DecreasePendingTask();
    assert.equal(submit.disabled, true, 'one owner is still pending');
    utils.DecreasePendingTask();
    assert.equal(submit.disabled, false, 'the second owner settles exactly once');
    utils.DecreasePendingTask();
    assert.equal(submit.disabled, false, 'an extra settlement cannot underflow the counter');
});

test('concurrent stage timeouts clear independently', () => {
    const { utils, register, expireAll } = runtime('cnc');
    const item = register('1');
    const uploadAttempt = utils.BeginItemUpload('1');
    const thumbnailAttempt = utils.BeginItemThumbnail('1');
    expireAll();
    assert.deepEqual(Object.keys(item.watchdogErrors).sort(), ['thumbnail', 'upload']);
    utils.SetItemUploadComplete('1', true, uploadAttempt);
    assert.match(item.errorMessage, /too long/i, 'thumbnail remains timed out');
    utils.SetItemThumbnailComplete('1', false, thumbnailAttempt);
    assert.equal(item.errorMessage, null);
});

test('explicit submission blocker survives watchdog recovery and later registration', () => {
    const { utils, register, expireAll, elements } = runtime('printing');
    register('1');
    utils.BlockSubmission();
    const uploadAttempt = utils.BeginItemUpload('1');
    expireAll();
    utils.SetItemUploadComplete('1', true, uploadAttempt);
    register('2');
    assert.equal(utils.HasError(), true);
    assert.equal(elements.get('submit-gate').disabled, true);
    utils.ClearError();
    assert.equal(utils.HasError(), false);
});

test('ThrowError blocker survives unrelated watchdog settlement', () => {
    const { utils, register, expireAll } = runtime('printing');
    register('1');
    utils.ThrowError();
    const thumbnailAttempt = utils.BeginItemThumbnail('1');
    expireAll();
    utils.SetItemThumbnailComplete('1', true, thumbnailAttempt);
    assert.equal(utils.HasError(), true);
});

test('late upload success and failure cannot settle a restarted stage attempt', () => {
    const { utils, register, expireAll, timers } = runtime('cnc');
    const item = register('1');
    const first = utils.BeginItemUpload('1');
    const second = utils.BeginItemUpload('1');
    assert.equal(utils.SetItemUploadComplete('1', true, first), false);
    assert.equal(utils.SetItemUploadComplete('1', false, first), false);
    assert.equal(item.uploadComplete, false);
    assert.equal(timers.size, 1);
    expireAll();
    assert.equal(item.watchdogError.stageAttempt, second.stageAttempt);
});

test('late settlement cannot mutate a re-registered item with the same id', () => {
    const { utils, register, timers } = runtime('cnc');
    register('1');
    const first = utils.BeginItemThumbnail('1');
    const replacement = register('1');
    const second = utils.BeginItemThumbnail('1');
    assert.equal(utils.SetItemThumbnailComplete('1', true, first), false);
    assert.equal(replacement.thumbnailComplete, false);
    assert.equal(timers.size, 1);
    assert.equal(utils.SetItemThumbnailComplete('1', true, second), true);
});

test('tokenless legacy settlement cannot clear a token-owned stage', () => {
    const { utils, register, timers } = runtime('printing');
    const item = register('1');
    const attempt = utils.BeginItemUpload('1');
    assert.equal(utils.SetItemUploadComplete('1', true), false);
    assert.equal(item.uploadComplete, false);
    assert.equal(timers.size, 1);
    assert.equal(utils.SetItemUploadComplete('1', true, attempt), true);
});

test('worker queue wait is untimed and each actual dispatch gets a fresh unchanged deadline', async () => {
    const { context, workers, timers, timerDelays } = runtime('cnc');
    const manager = context.CreateModelWorkerManager(1);
    const first = manager.Submit({ kind: 'parse' });
    const second = manager.Submit({ kind: 'parse' });
    const secondRejected = assert.rejects(second.promise, /too complex/i);
    assert.equal(workers.at(-1).messages.length, 1);
    assert.equal(timers.size, 1, 'only the dispatched job owns a deadline');
    assert.deepEqual([...timerDelays.values()], [120000]);
    workers.at(-1).onmessage({ data: { jobId: first.jobId, success: true } });
    await first.promise;
    assert.equal(workers.at(-1).messages.at(-1).jobId, second.jobId);
    assert.equal(timers.size, 1, 'queued job receives a fresh deadline only at dispatch');
    assert.deepEqual([...timerDelays.values()], [120000]);
    [...timers.values()][0]();
    await secondRejected;
    assert.equal(workers.at(-2).terminated, true, 'timed-out active worker is replaced');
});

test('native intermediate preview reserves the same slot and deadline until acknowledged', async () => {
    const { context, workers, timers } = runtime('cnc');
    const manager = context.CreateModelWorkerManager(1);
    let acknowledge;
    const job = manager.Submit({ action: 'parse' }, [], () => new Promise(resolve => { acknowledge = resolve; }));
    const queued = manager.Submit({ action: 'parse' });
    const worker = workers.at(-1), originalTimer = [...timers.keys()][0];
    worker.onmessage({ data: { jobId: job.jobId, success: true, stage: 'native-preview' } });
    await Promise.resolve();
    assert.equal(worker.messages.length, 1);
    assert.deepEqual([...timers.keys()], [originalTimer]);
    acknowledge();
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(worker.messages.at(-1).action, 'continue-native-analysis');
    assert.equal(worker.messages.at(-1).jobId, job.jobId);
    assert.deepEqual([...timers.keys()], [originalTimer]);
    worker.onmessage({ data: { jobId: job.jobId, success: true } });
    await job.promise;
    assert.equal(worker.messages.at(-1).jobId, queued.jobId);
    worker.onmessage({ data: { jobId: queued.jobId, success: true } });
    await queued.promise;
});

test('preview rejection, cancellation and timeout release retained native worker and never continue stale jobs', async () => {
    for (const outcome of ['reject', 'reject-null', 'reject-undefined', 'cancel', 'timeout']) {
        const { context, workers, timers } = runtime('cnc');
        const manager = context.CreateModelWorkerManager(1);
        let resolvePreview, rejectPreview;
        const job = manager.Submit({ action: 'parse' }, [], () => new Promise((resolve, reject) => { resolvePreview = resolve; rejectPreview = reject; }));
        const rejected = assert.rejects(job.promise);
        const worker = workers.at(-1);
        worker.onmessage({ data: { jobId: job.jobId, success: true, stage: 'native-preview' } });
        await Promise.resolve();
        if (outcome === 'reject') rejectPreview(new Error('preview failed'));
        if (outcome === 'reject-null') rejectPreview(null);
        if (outcome === 'reject-undefined') rejectPreview(undefined);
        if (outcome === 'cancel') manager.Cancel(job.jobId);
        if (outcome === 'timeout') [...timers.values()][0]();
        await rejected;
        resolvePreview();
        await new Promise(resolve => setImmediate(resolve));
        assert.equal(worker.terminated, true);
        assert.equal(worker.messages.length, 1);
        assert.equal(timers.size, 0);
    }
});

test('actual ParseFile cannot publish a delayed native preview after timeout or worker error', async () => {
    for (const outcome of ['timeout', 'error']) {
        const f = runtime('cnc'), { context, workers, timers, timerDelays } = f;
        context.ModelParseWorkerManager = context.CreateModelWorkerManager(1);
        context.ReadArrayBuffer = (file, ready) => ready(new ArrayBuffer(4));
        context.EnsurePreviewGeometryNormals = () => {};
        context.ApplyMultiBodyPreviewColors = () => {};
        context.AddCadPreviewEdges = () => {};
        let releaseBuild, builds = 0;
        context.BuildGroupFromMeshBuffersAsync = () => { builds++; return new Promise(resolve => { releaseBuild = resolve; }); };
        const source = fs.readFileSync(sourcePath, 'utf8');
        const begin = source.indexOf('    this.ParseFile = function (file, onReady, options) {');
        const end = source.indexOf('    // Displays the given (already-parsed) object3D', begin);
        assert.ok(begin >= 0 && end > begin);
        const parser = vm.runInContext('(function () {' + source.slice(begin, end) + 'return this; }).call({})', context);
        const ready = [], previews = [], snapshots = [];
        const options = { analysisProfile: 'cnc', onPreviewReady: (info, object) => {
            previews.push(object); snapshots.push(object); return Promise.resolve();
        } };
        const settle = (...args) => ready.push(args);
        const flush = () => new Promise(resolve => setImmediate(resolve));
        const releasePreviewDelay = () => {
            const id = [...timerDelays.keys()].find(key => timerDelays.get(key) === 0);
            assert.ok(id); const callback = timers.get(id); timers.delete(id); timerDelays.delete(id); callback();
        };
        parser.ParseFile({ name: 'box.step' }, settle, options);
        const worker = workers.at(-1), jobId = worker.messages[0].jobId;
        worker.onmessage({ data: { jobId, success: true, stage: 'native-preview', meshes: [], modelInfo: { bodyCount: 1 } } });
        await flush(); releasePreviewDelay(); await flush();
        assert.equal(builds, 1);
        if (outcome === 'timeout') [...timers.values()][0](); else worker.onerror();
        await flush();
        assert.equal(ready.length, 1); assert.equal(ready[0][0], null); assert.equal(typeof ready[0][2], 'string');
        releaseBuild({ userData: {} }); await flush();
        assert.equal(ready.length, 1, 'failure remains terminal after late display construction');
        assert.equal(previews.length, 0); assert.equal(snapshots.length, 0);
        assert.equal(worker.messages.length, 1, 'failed worker never receives continuation');
        assert.equal(worker.terminated, true);

        context.BuildGroupFromMeshBuffersAsync = async () => ({ userData: {} });
        parser.ParseFile({ name: 'next.step' }, settle, options);
        const next = workers.at(-1), nextId = next.messages.at(-1).jobId;
        next.onmessage({ data: { jobId: nextId, success: true, stage: 'native-preview', meshes: [], modelInfo: { bodyCount: 1 } } });
        await flush(); releasePreviewDelay(); await flush();
        assert.equal(next.messages.at(-1).action, 'continue-native-analysis');
        next.onmessage({ data: { jobId: nextId, success: true, modelInfo: { bodyCount: 1 }, cncGeometry: { current: true } } });
        await flush();
        assert.equal(ready.length, 2); assert.equal(ready[1][3].current, true);
        assert.equal(previews.length, 1); assert.equal(snapshots.length, 1);
    }
});

test('removing an upload-failed item restores a healthy sibling', () => {
    const { utils, register, elements } = runtime('printing');
    register('healthy');
    register('failed');
    const failedUpload = utils.BeginItemUpload('failed');
    utils.SetItemUploadComplete('failed', false, failedUpload);
    assert.equal(utils.HasError(), true);
    utils.RemoveItem('failed');
    assert.equal(utils.HasError(), false);
    assert.equal(elements.get('submit-gate').disabled, false);
});

test('removing an unrelated item preserves another item pricing failure and a global blocker', () => {
    const { utils, register } = runtime('printing');
    register('healthy');
    register('failed');
    utils.SetItemOwnedFailure('failed', 'pricing', 7, 'Estimate rejected');
    utils.BlockSubmission();
    utils.RemoveItem('healthy');
    assert.equal(utils.HasError(), true);
    utils.ClearItemOwnedFailure('failed', 'pricing');
    assert.equal(utils.HasError(), true, 'global blocker remains independent');
    utils.ClearError();
    assert.equal(utils.HasError(), false);
});

test('current pricing retry clears only its item-owned pricing failure', () => {
    const { utils, register } = runtime('printing');
    const recovered = register('recovered');
    const unrelated = register('unrelated');
    utils.SetItemOwnedFailure('recovered', 'pricing', 1, 'First estimate failed');
    utils.SetItemOwnedFailure('unrelated', 'pricing', 2, 'Other estimate failed');
    utils.ClearItemOwnedFailure('recovered', 'pricing');
    assert.equal(recovered.errorMessage, null);
    assert.equal(unrelated.errorMessage, 'Other estimate failed');
    assert.equal(utils.HasError(), true);
});

test('repeated pricing failures replace their owned display before a successful retry clears it', () => {
    const { utils, register } = runtime('printing');
    const item = register('1');
    utils.SetItemOwnedFailure('1', 'pricing', 1, 'Server rejected estimate A');
    utils.SetItemOwnedFailure('1', 'pricing', 2, 'Network estimate failure B');
    assert.equal(item.errorMessage, 'Network estimate failure B');
    utils.ClearItemOwnedFailure('1', 'pricing');
    assert.equal(item.errorMessage, null);
    assert.equal(utils.HasError(), false);
});

test('same-message terminal failure is not cleared by owned pricing recovery', () => {
    const { utils, register } = runtime('printing');
    const item = register('1');
    utils.SetItemFailed('1', 'Shared failure text');
    utils.SetItemOwnedFailure('1', 'pricing', 1, 'Shared failure text');
    utils.ClearItemOwnedFailure('1', 'pricing');
    assert.equal(item.errorMessage, 'Shared failure text');
    assert.equal(utils.HasError(), true);
});

test('same-message owned pricing recovery does not clear a thumbnail watchdog', () => {
    const { utils, register, expireAll } = runtime('printing');
    const item = register('1');
    const thumbnailAttempt = utils.BeginItemThumbnail('1');
    expireAll();
    const timeoutMessage = item.errorMessage;
    utils.SetItemOwnedFailure('1', 'pricing', 1, timeoutMessage);
    utils.ClearItemOwnedFailure('1', 'pricing');
    assert.equal(item.errorMessage, timeoutMessage);
    utils.SetItemThumbnailComplete('1', false, thumbnailAttempt);
    assert.equal(item.errorMessage, null);
});
