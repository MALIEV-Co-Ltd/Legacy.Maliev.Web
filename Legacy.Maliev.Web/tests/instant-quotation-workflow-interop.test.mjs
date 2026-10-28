import assert from 'node:assert/strict';
import test from 'node:test';

import {
  configureFilePickerForPlatform,
  createWorkflowPreviewInterop,
  previewColorForSelection,
  supportedPreviewExtensions,
} from '../wwwroot/src/app/js/instant-quotation/workflow-interop.mjs';
import { createModelViewer } from '../wwwroot/src/app/js/instant-quotation/model-viewer.mjs';

function deferred() {
  let resolve;
  let reject;
  const promise = new Promise((ok, fail) => { resolve = ok; reject = fail; });
  return { promise, resolve, reject };
}

function disposableObject() {
  let disposed = false;
  return {
    traverse(visitor) {
      visitor({
        isMesh: true,
        geometry: { dispose() { disposed = true; } },
        material: { dispose() { disposed = true; } },
      });
    },
    get disposed() { return disposed; },
  };
}

function modelFile(name, bytes = [1, 2, 3]) {
  return {
    name,
    arrayBuffer: async () => new Uint8Array(bytes).buffer,
  };
}

function harness(
  loadModel = async () => disposableObject(),
  analyzeGeometry = () => ({ version: 1, dimensionZMm: 10, volumeMm3: 1000 })) {
  const calls = [];
  const statuses = [];
  const viewer = {
    addPart: (...args) => calls.push(['add', ...args]),
    select: (...args) => calls.push(['select', ...args]),
    promotePart: (...args) => calls.push(['promote', ...args]),
    remove: (...args) => calls.push(['remove', ...args]),
    setColor: (...args) => calls.push(['color', ...args]),
    reset: () => calls.push(['reset']),
    fit: () => calls.push(['fit']),
    fullscreen: () => calls.push(['fullscreen']),
    snapshot: id => {
      calls.push(['snapshot', id]);
      if (id === 'missing') throw new RangeError('Unknown viewer part.');
      return `data:image/webp;base64,${id}`;
    },
    dispose: () => calls.push(['dispose']),
  };
  return {
    calls,
    statuses,
    viewer,
    interop: createWorkflowPreviewInterop({
      loadModel,
      analyzeGeometry,
      createViewer: () => viewer,
      reportStatus: status => statuses.push(status),
    }),
  };
}

test('empty owner uses original viewport branches and owns coalesced resize and replacement cleanup', () => {
  const listeners = new Map();
  const viewportListeners = new Map();
  const frames = new Map();
  let frameId = 0;
  let overflowY = 'auto';
  let flexDirection = 'row';
  let top = 73.6;
  const view = {
    innerHeight: 900,
    visualViewport: {
      height: 844,
      addEventListener: (key, fn) => viewportListeners.set(key, fn),
      removeEventListener: (key, fn) => { if (viewportListeners.get(key) === fn) viewportListeners.delete(key); },
    },
    getComputedStyle: element => element === document.body ? { overflowY } : { display: 'grid', flexDirection },
    addEventListener: (key, fn) => listeners.set(key, fn),
    removeEventListener: (key, fn) => { if (listeners.get(key) === fn) listeners.delete(key); },
    requestAnimationFrame: fn => { frames.set(++frameId, fn); return frameId; },
    cancelAnimationFrame: id => frames.delete(id),
  };
  const document = { defaultView: view, body: {} };
  const makeOwner = () => {
    const properties = new Map();
    return {
      ownerDocument: document,
      getBoundingClientRect: () => ({ top }),
      querySelector: () => null,
      style: { setProperty: (key, value) => properties.set(key, value), removeProperty: key => properties.delete(key) },
      properties,
    };
  };
  const makeInput = owner => ({ dataset: {}, closest: () => owner, addEventListener() {}, removeEventListener() {} });
  const { interop } = harness();
  const owner = makeOwner();
  const input = makeInput(owner);
  const height = () => owner.properties.get('--iq-empty-viewport-height');
  interop.bindInput(input);
  assert.equal(height(), '770px');
  view.visualViewport.height = 280;
  overflowY = 'hidden';
  interop.bindInput(input);
  assert.equal(height(), '420px');
  flexDirection = 'column';
  interop.bindInput(input);
  assert.equal(height(), '240px');
  top = -20;
  view.visualViewport.height = 0;
  listeners.get('resize')();
  listeners.get('orientationchange')();
  viewportListeners.get('resize')();
  assert.equal(frames.size, 1);
  const [id, callback] = frames.entries().next().value;
  frames.delete(id);
  callback();
  assert.equal(height(), '900px');
  listeners.get('resize')();
  const next = makeOwner();
  interop.bindInput(makeInput(next));
  assert.equal(owner.properties.size, 0);
  assert.equal(frames.size, 0);
  assert.equal(listeners.size, 2);
  listeners.get('resize')();
  interop.dispose();
  assert.equal(next.properties.size, 0);
  assert.equal(frames.size, 0);
  assert.equal(listeners.size, 0);
  assert.equal(viewportListeners.size, 0);
});

test('supports the exact nine standalone quotation extensions', () => {
  assert.deepEqual(supportedPreviewExtensions, [
    'stl', 'obj', '3mf', 'glb', 'gltf', 'stp', 'step', 'igs', 'iges',
  ]);
});

for (const extension of supportedPreviewExtensions) {
  test(`admits a selected .${extension} file to preview analysis before upload`, async () => {
    const selected = modelFile(`fixture.${extension}`);
    const loaded = [];
    const { interop, statuses } = harness(async file => {
      loaded.push(file);
      return disposableObject();
    });

    const [key] = interop.beginSelection({ files: [selected] }, Number.POSITIVE_INFINITY, 'millimetres');
    const claim = await interop.getGeometryClaim(key);

    assert.deepEqual(loaded, [selected]);
    assert.equal(typeof claim.sha256, 'string');
    assert.equal(claim.sha256.length, 64);
    assert.deepEqual(statuses, []);
  });
}

test('iOS file picker removes the unsupported accept filter but retains the application allowlist', () => {
  const attributes = new Map([['accept', '.stl,model/stl,application/sla']]);
  const input = {
    dataset: {},
    getAttribute: name => attributes.get(name) ?? null,
    removeAttribute: name => attributes.delete(name),
  };

  assert.equal(configureFilePickerForPlatform(input, {
    userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_5 like Mac OS X)',
    platform: 'iPhone',
    maxTouchPoints: 5,
  }), true);
  assert.equal(input.dataset.acceptedTypes, '.stl,model/stl,application/sla');
  assert.equal(input.getAttribute('accept'), null);
});

test('iPadOS Mac-touch fallback is enabled while desktop browsers retain accept filtering', () => {
  const createInput = () => {
    const attributes = new Map([['accept', '.stl,model/stl']]);
    return {
      dataset: {},
      getAttribute: name => attributes.get(name) ?? null,
      removeAttribute: name => attributes.delete(name),
    };
  };
  const ipad = createInput();
  const desktop = createInput();

  assert.equal(configureFilePickerForPlatform(ipad, {
    userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)',
    platform: 'MacIntel',
    maxTouchPoints: 5,
  }), true);
  assert.equal(ipad.getAttribute('accept'), null);
  assert.equal(configureFilePickerForPlatform(desktop, {
    userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)',
    platform: 'MacIntel',
    maxTouchPoints: 0,
  }), false);
  assert.equal(desktop.getAttribute('accept'), '.stl,model/stl');
  assert.equal(desktop.dataset.acceptedTypes, undefined);
});

test('returns a same-file upload-derived geometry claim with lowercase SHA-256', async () => {
  const bytes = new TextEncoder().encode('legacy-geometry-source');
  const file = {
    name: 'claim.stl',
    arrayBuffer: async () => bytes.buffer.slice(0),
  };
  const expectedGeometry = {
    version: 1,
    dimensionXmm: 10,
    dimensionYmm: 20,
    dimensionZmm: 30,
    volumeMm3: 6000,
  };
  const { interop } = harness(async () => disposableObject(), () => expectedGeometry);
  const [key] = interop.beginSelection({ files: [file] });
  assert.deepEqual(await interop.getGeometryClaim(key), {
    ...expectedGeometry,
    sha256: '52af3eb361bcd7105f22ad173ae6dadf1674de3201e79ba6f55e11f45dd06bcf',
    gltfUnitPolicy: { version: 0, unit: 0 },
  });
});

test('review thumbnails use admitted browser previews and preserve fallback when unavailable', async () => {
  const { interop, calls } = harness();
  const [key] = interop.beginSelection({ files: [modelFile('part.stl')] });
  await interop.getGeometryClaim(key);
  interop.attach({});
  interop.admit(key, 'part-a');
  const admitted = { dataset: { partId: 'part-a' }, src: '/fallback.svg' };
  const unavailable = { dataset: { partId: 'missing' }, src: '/fallback.svg' };
  const root = { querySelectorAll: () => [admitted, unavailable] };

  assert.equal(interop.renderReviewThumbnails(root), 1);
  assert.equal(admitted.src, 'data:image/webp;base64,part-a');
  assert.equal(unavailable.src, '/fallback.svg');
  assert.deepEqual(calls.filter(call => call[0] === 'snapshot'), [
    ['snapshot', 'part-a'],
    ['snapshot', 'missing'],
  ]);
});

test('part appearance updates select the requested part, apply a canonical preview color, and refresh only its thumbnail', async () => {
  const { interop, calls } = harness();
  const [firstKey, secondKey] = interop.beginSelection({
    files: [modelFile('first.stl'), modelFile('second.stl')],
  });
  await Promise.all([interop.getGeometryClaim(firstKey), interop.getGeometryClaim(secondKey)]);
  interop.attach({});
  interop.admit(firstKey, 'part-a');
  interop.admit(secondKey, 'part-b');

  assert.equal(interop.updatePartAppearance('part-b', 'Blue'), 'data:image/webp;base64,part-b');
  assert.deepEqual(calls.slice(-3), [
    ['select', 'part-b'],
    ['color', '#2563eb'],
    ['snapshot', 'part-b'],
  ]);
});

test('unrelated rerenders preserve admitted thumbnail bytes while appearance and camera changes refresh only their part', async () => {
  const { interop, calls } = harness();
  const keys = interop.beginSelection({ files: [modelFile('a.stl'), modelFile('b.stl')] });
  await Promise.all(keys.map(key => interop.getGeometryClaim(key)));
  const listeners = new Map();
  interop.attach({ addEventListener: (type, listener) => listeners.set(type, listener),
    removeEventListener: type => listeners.delete(type) });
  interop.admit(keys[0], 'part-a');
  interop.admit(keys[1], 'part-b');
  const images = ['part-a', 'part-b'].map(partId => ({ dataset: { partId }, src: '/fallback.svg' }));
  const root = { querySelectorAll: () => images };
  interop.renderReviewThumbnails(root);
  const snapshots = id => calls.filter(call => call[0] === 'snapshot' && call[1] === id).length;
  assert.equal(snapshots('part-a'), 1);
  assert.equal(snapshots('part-b'), 1);
  interop.updatePartAppearance('part-a', 'Red');
  interop.renderReviewThumbnails(root);
  assert.equal(snapshots('part-a'), 2);
  assert.equal(snapshots('part-b'), 1);
  interop.select('part-a');
  listeners.get('pointerup')();
  interop.renderReviewThumbnails(root);
  assert.equal(snapshots('part-a'), 3);
  assert.equal(snapshots('part-b'), 1);
  interop.reset();
  interop.renderReviewThumbnails(root);
  interop.fit();
  interop.renderReviewThumbnails(root);
  assert.equal(snapshots('part-a'), 5);
  assert.equal(snapshots('part-b'), 1);
  interop.dispose();
  assert.equal(listeners.size, 0);
});

test('replacement review images receive exact cached bytes without recapturing the unchanged part', async () => {
  const { interop, calls } = harness();
  const keys = interop.beginSelection({ files: [modelFile('single.stl'), modelFile('multi.stl')] });
  await Promise.all(keys.map(key => interop.getGeometryClaim(key)));
  interop.attach({});
  interop.admit(keys[0], 'single');
  interop.admit(keys[1], 'multi');
  const images = () => ['single', 'multi'].map(partId => ({ dataset: { partId }, src: '/fallback.svg' }));
  let current = images();
  const root = { querySelectorAll: () => current };
  assert.equal(interop.renderReviewThumbnails(root), 2);
  const retained = current[1].src;
  interop.updatePartAppearance('single', 'Red');
  current = images();
  assert.equal(current[1].src, '/fallback.svg');
  assert.equal(interop.renderReviewThumbnails(root), 2);
  assert.equal(current[1].src, retained);
  assert.equal(current[0].src, 'data:image/webp;base64,single');
  assert.equal(calls.filter(call => call[0] === 'snapshot' && call[1] === 'multi').length, 1);
  assert.equal(calls.filter(call => call[0] === 'snapshot' && call[1] === 'single').length, 2);
  interop.dispose();
});

function thumbnailMutations() {
  const original = globalThis.MutationObserver;
  const observers = [];
  globalThis.MutationObserver = class {
    constructor(callback) { this.callback = callback; observers.push(this); }
    observe(root, options) { this.root = root; this.options = options; }
    disconnect() { this.disconnected = true; }
  };
  const image = partId => ({
    dataset: { partId }, isConnected: true, src: '/fallback.svg',
    getAttribute(name) { assert.equal(name, 'src'); return this.src; },
  });
  const root = images => ({
    isConnected: true, images,
    querySelectorAll() { return this.images; },
    contains(node) { return this.images.includes(node); },
  });
  return {
    observers, image, root,
    deliver() { for (const observer of observers.filter(item => !item.disconnected)) observer.callback([]); },
    restore() { globalThis.MutationObserver = original; },
  };
}

test('render replacement and src reset restore cached thumbnails before another server render', async () => {
  const mutations = thumbnailMutations();
  const { interop, calls, viewer } = harness();
  viewer.snapshot = id => {
    calls.push(['snapshot', id]);
    const color = id === 'single' ? calls.findLast(call => call[0] === 'color')?.[1] ?? 'initial' : 'source';
    return `data:image/webp;base64,${id}-${color}`;
  };
  try {
    const keys = interop.beginSelection({ files: [modelFile('single.stl'), modelFile('multi.stl')] });
    await Promise.all(keys.map(key => interop.getGeometryClaim(key)));
    interop.attach({});
    interop.admit(keys[0], 'single');
    interop.admit(keys[1], 'multi');
    const root = mutations.root(['single', 'multi'].map(mutations.image));
    interop.renderReviewThumbnails(root);
    const retained = root.images[1].src;
    interop.updatePartAppearance('single', 'Red');
    root.images = ['single', 'multi'].map(mutations.image);
    mutations.deliver();
    assert.equal(root.images[1].src, retained);
    assert.equal(root.images[0].src, 'data:image/webp;base64,single-#dc2626');
    root.images[1].src = '/fallback.svg';
    mutations.deliver();
    assert.equal(root.images[1].src, retained);
    let writes = 0;
    Object.defineProperty(root.images[1], 'src', { get: () => retained, set: () => { writes += 1; } });
    mutations.deliver();
    interop.renderReviewThumbnails(root);
    assert.equal(writes, 0);
    assert.equal(mutations.observers.length, 1);
    assert.deepEqual(mutations.observers[0].options,
      { childList: true, subtree: true, attributes: true, attributeFilter: ['src'] });
    assert.equal(calls.filter(call => call[0] === 'snapshot' && call[1] === 'multi').length, 1);
    assert.equal(calls.filter(call => call[0] === 'snapshot' && call[1] === 'single').length, 2);
  } finally { interop.dispose(); mutations.restore(); }
});

test('thumbnail mutation callbacks cannot resurrect invalidated released or disconnected images', async () => {
  const mutations = thumbnailMutations();
  const { interop, calls } = harness();
  try {
    const keys = interop.beginSelection({ files: [modelFile('a.stl'), modelFile('b.stl')] });
    await Promise.all(keys.map(key => interop.getGeometryClaim(key)));
    interop.attach({});
    interop.admit(keys[0], 'part-a');
    interop.admit(keys[1], 'part-b');
    const root = mutations.root(['part-a', 'part-b'].map(mutations.image));
    interop.renderReviewThumbnails(root);
    assert.equal(mutations.observers.length, 1);
    interop.select('part-a');
    interop.reset(); // Camera invalidation removes these bytes before recapture.
    interop.release(keys[1]);
    root.images = ['part-a', 'part-b', 'unknown'].map(mutations.image);
    mutations.deliver();
    assert.deepEqual(root.images.map(image => image.src), ['/fallback.svg', '/fallback.svg', '/fallback.svg']);
    assert.equal(calls.filter(call => call[0] === 'snapshot').length, 2);
    interop.renderReviewThumbnails(root);
    root.images[0] = mutations.image('part-a');
    root.images[0].isConnected = false;
    mutations.deliver();
    assert.equal(root.images[0].src, '/fallback.svg');
    root.images[0].isConnected = true;
    root.isConnected = false;
    mutations.deliver();
    assert.equal(root.images[0].src, '/fallback.svg');
    root.isConnected = true;
    interop.quarantine(keys[0]);
    mutations.deliver();
    assert.equal(root.images[0].src, '/fallback.svg');
  } finally { interop.dispose(); mutations.restore(); }
});

test('thumbnail observer root replacement and disposal reject late callbacks and release ownership', async () => {
  const mutations = thumbnailMutations();
  const { interop } = harness();
  try {
    const [key] = interop.beginSelection({ files: [modelFile('a.stl')] });
    await interop.getGeometryClaim(key);
    interop.attach({});
    interop.admit(key, 'part-a');
    const first = mutations.root([mutations.image('part-a')]);
    interop.renderReviewThumbnails(first);
    const old = mutations.observers[0];
    const second = mutations.root([mutations.image('part-a')]);
    interop.renderReviewThumbnails(second);
    assert.equal(old?.disconnected, true);
    first.images[0].src = '/fallback.svg';
    old.callback([]);
    assert.equal(first.images[0].src, '/fallback.svg');
    const released = mutations.observers[1];
    interop.release(key);
    assert.equal(released.disconnected, true);
    second.images[0].src = '/fallback.svg';
    released.callback([]);
    assert.equal(second.images[0].src, '/fallback.svg');
    const [replacement] = interop.beginSelection({ files: [modelFile('replacement.stl')] });
    await interop.getGeometryClaim(replacement);
    interop.admit(replacement, 'part-a');
    released.callback([]);
    assert.equal(second.images[0].src, '/fallback.svg');
    interop.renderReviewThumbnails(second);
    const current = mutations.observers[2];
    interop.dispose();
    assert.equal(current.disconnected, true);
    second.images[0].src = '/fallback.svg';
    current.callback([]);
    assert.equal(second.images[0].src, '/fallback.svg');
    interop.renderReviewThumbnails(second);
    assert.equal(mutations.observers.length, 3);
  } finally { interop.dispose(); mutations.restore(); }
});

test('empty viewer detach releases old canvas listeners before reattach and disposal', () => {
  const { interop, calls } = harness();
  function canvas() {
    const foreign = () => {};
    const listeners = new Map([['pointerup', new Set([foreign])]]);
    return {
      listeners,
      addEventListener(type, listener) {
        if (!listeners.has(type)) listeners.set(type, new Set());
        listeners.get(type).add(listener);
      },
      removeEventListener(type, listener) {
        listeners.get(type)?.delete(listener);
        if (listeners.get(type)?.size === 0) listeners.delete(type);
      },
      assertForeignOnly() {
        assert.equal(listeners.size, 1);
        assert.deepEqual([...listeners.get('pointerup')], [foreign]);
      },
    };
  }
  const first = canvas();
  const second = canvas();
  try {
    interop.attach(first);
    assert.equal(first.listeners.size, 3);
    assert.equal(interop.detachEmptyViewer(), true);
    first.assertForeignOnly();
    interop.attach(second);
    first.assertForeignOnly();
    assert.equal(second.listeners.size, 3);
    interop.dispose();
    first.assertForeignOnly();
    second.assertForeignOnly();
    assert.equal(calls.filter(call => call[0] === 'dispose').length, 2);
  } finally { interop.dispose(); }
});

test('unavailable and released previews cannot supply a cached thumbnail', async () => {
  const { interop, calls } = harness();
  const [key] = interop.beginSelection({ files: [modelFile('a.stl')] });
  await interop.getGeometryClaim(key);
  interop.attach({});
  interop.admit(key, 'part-a');
  const unavailable = { dataset: { partId: 'missing' }, src: '/fallback.svg' };
  const admitted = { dataset: { partId: 'part-a' }, src: '/fallback.svg' };
  const root = { querySelectorAll: () => [admitted, unavailable] };
  interop.renderReviewThumbnails(root);
  interop.renderReviewThumbnails(root);
  assert.equal(calls.filter(call => call[0] === 'snapshot' && call[1] === 'missing').length, 2);
  assert.equal(calls.filter(call => call[0] === 'snapshot' && call[1] === 'part-a').length, 1);
  interop.release(key);
  const [replacement] = interop.beginSelection({ files: [modelFile('replacement.stl')] });
  await interop.getGeometryClaim(replacement);
  interop.admit(replacement, 'part-a');
  admitted.src = '/fallback.svg';
  // Reusing a part ID for new geometry must not reuse the old admission's bytes.
  interop.renderReviewThumbnails(root);
  assert.equal(calls.filter(call => call[0] === 'snapshot' && call[1] === 'part-a').length, 2);
});

test('development retries reject an old canvas generation before claiming the new preview', async () => {
  const { interop, calls } = harness();
  const [key] = interop.beginSelection({ files: [modelFile('a.stl')] });
  await interop.getGeometryClaim(key);
  const oldCanvas = { isConnected: true, dataset: { localDemoGeneration: '1' } };
  assert.equal(interop.retainDevelopmentPreview(key, oldCanvas, 1), true);
  const nextKey = interop.retry(key);
  await interop.getGeometryClaim(nextKey);
  assert.equal(interop.retainDevelopmentPreview(nextKey, oldCanvas, 2), false);
  oldCanvas.isConnected = false;
  assert.equal(interop.retainDevelopmentPreview(nextKey, oldCanvas, 1), false);
  const newCanvas = { isConnected: true, dataset: { localDemoGeneration: '2' } };
  assert.equal(interop.retainDevelopmentPreview(nextKey, newCanvas, 2), true);
  assert.equal(newCanvas.dataset.localDemoReady, 'true');
  assert.equal(interop.retainDevelopmentPreview(nextKey,
    { isConnected: true, dataset: { localDemoGeneration: '2' } }, 2), false);
  assert.equal(calls.filter(call => call[0] === 'add' && call[1] === nextKey).length, 1);
  interop.dispose();
  assert.equal(newCanvas.dataset.localDemoReady, undefined);
});

test('ordinary viewer listeners are released when a development preview replaces its canvas', async () => {
  const { interop } = harness();
  const listeners = [new Set(), new Set()];
  const canvas = index => ({
    addEventListener: type => listeners[index].add(type),
    removeEventListener: type => listeners[index].delete(type),
  });
  interop.attach(canvas(0));
  assert.equal(listeners[0].size, 3);
  const [key] = interop.beginSelection({ files: [modelFile('a.stl')] });
  await interop.getGeometryClaim(key);
  assert.equal(interop.retainDevelopmentPreview(key,
    { isConnected: true, dataset: { localDemoGeneration: '1' } }, 1), true);
  assert.equal(listeners[0].size, 0);
  const nextKey = interop.retry(key);
  await interop.getGeometryClaim(nextKey);
  interop.admit(nextKey, 'part-a');
  interop.attach(canvas(1));
  assert.equal(listeners[1].size, 3);
  interop.dispose();
  assert.deepEqual(listeners.map(value => value.size), [0, 0]);
});

test('preview colors accept custom hex and fail closed to the neutral material color', () => {
  assert.equal(previewColorForSelection('#Aa00fF'), '#aa00ff');
  assert.equal(previewColorForSelection('Blue'), '#2563eb');
  assert.equal(previewColorForSelection('not-a-production-color'), '#64748b');
});

test('quarantined and released previews cannot yield stale geometry claims', async () => {
  const pending = deferred();
  const file = {
    name: 'stale.stl',
    arrayBuffer: async () => new Uint8Array([1, 2, 3]).buffer,
  };
  const { interop } = harness(() => pending.promise);
  const [key] = interop.beginSelection({ files: [file] });
  interop.quarantine(key);
  pending.resolve(disposableObject());
  await assert.rejects(interop.getGeometryClaim(key), /Geometry analysis is unavailable/);
  interop.release(key);
  await assert.rejects(interop.getGeometryClaim(key), /Unknown preview correlation/);
});

test('starts duplicate-name previews in selection order without waiting for parsing', async () => {
  const first = deferred();
  const second = deferred();
  const objectA = disposableObject();
  const objectB = disposableObject();
  const files = [modelFile('same.stl'), modelFile('same.stl')];
  let call = 0;
  const { calls, interop } = harness(() => [first.promise, second.promise][call++]);
  interop.attach({});
  const keys = await interop.beginSelection({ files }, Number.POSITIVE_INFINITY, 'millimetres');
  assert.equal(keys.length, 2);
  assert.notEqual(keys[0], keys[1]);
  interop.admit(keys[0], 'part-a');
  interop.admit(keys[1], 'part-b');
  second.resolve(objectB);
  first.resolve(objectA);
  await Promise.all([first.promise, second.promise]);
  await Promise.all(keys.map(key => interop.getGeometryClaim(key)));
  assert.deepEqual(
    calls.filter(item => item[0] === 'add')
      .map(item => [item[1], item[2]])
      .sort(([left], [right]) => left.localeCompare(right)),
    [['part-a', objectA], ['part-b', objectB]]);
  assert.equal(objectB.disposed, false);
  assert.equal(objectA.disposed, false);
});

test('capture listener snapshots each FileList before server interop and rejected batches are discarded', async () => {
  let capture;
  const input = {
    files: [],
    addEventListener(type, listener, options) {
      assert.equal(type, 'change');
      assert.equal(options.capture, true);
      capture = listener;
    },
    removeEventListener() {},
  };
  const loaded = [];
  const { interop } = harness(async file => { loaded.push(file); return disposableObject(); });
  interop.bindInput(input);
  const first = modelFile('first.stl');
  const rejected = modelFile('rejected.obj');
  input.files = [first];
  capture();
  input.files = [rejected];
  capture();
  const keys = interop.beginSelection(input);
  interop.discardSelection();
  await Promise.resolve();
  assert.equal(keys.length, 1);
  assert.deepEqual(loaded, [first]);
  assert.deepEqual(interop.beginSelection({ files: [] }), []);
});

test('oversized selections are skipped before preview loading while smaller files proceed', async () => {
  const oversized = { ...modelFile('large.stl'), size: 201 * 1024 * 1024 };
  const accepted = { ...modelFile('small.stl'), size: 10 * 1024 * 1024 };
  const loaded = [];
  const { interop } = harness(async file => { loaded.push(file); return disposableObject(); });

  const keys = interop.beginSelection({ files: [oversized, accepted] }, 200 * 1024 * 1024);
  assert.equal(keys.length, 1);
  await interop.getGeometryClaim(keys[0]);
  assert.deepEqual(loaded, [accepted]);
});

test('admits successful previews and releases failed, removed, cancelled, and stale previews', async () => {
  const objects = [disposableObject(), disposableObject(), disposableObject()];
  const { calls, interop } = harness(async (_file, { signal }) => {
    if (signal.aborted) throw new DOMException('cancelled', 'AbortError');
    return objects.shift();
  });
  interop.attach({});
  const [ok, failed, cancelled] = await interop.beginSelection({
    files: [modelFile('a.stl'), modelFile('b.stl'), modelFile('c.stl')],
  });
  interop.admit(ok, 'part-a');
  interop.release(failed);
  interop.release(cancelled);
  await interop.getGeometryClaim(ok);
  assert.deepEqual(calls.filter(call => call[0] === 'add').map(call => call[1]), ['part-a']);
  interop.remove('part-a');
  assert.ok(calls.some(call => call[0] === 'remove' && call[1] === 'part-a'));
});

test('retry creates a new key and stale completion cannot replace it', async () => {
  const oldLoad = deferred();
  const newLoad = deferred();
  let invocation = 0;
  const { calls, interop } = harness(() => invocation++ === 0 ? oldLoad.promise : newLoad.promise);
  interop.attach({});
  const [oldKey] = await interop.beginSelection({ files: [modelFile('part.step')] });
  interop.quarantine(oldKey);
  const newKey = interop.retry(oldKey);
  assert.notEqual(newKey, oldKey);
  interop.admit(newKey, 'part-new');
  oldLoad.resolve(disposableObject());
  newLoad.resolve(disposableObject());
  await Promise.all([oldLoad.promise, newLoad.promise]);
  await interop.getGeometryClaim(newKey);
  assert.deepEqual(calls.filter(call => call[0] === 'add').map(call => call[1]), ['part-new']);
});

test('terminal release deletes correlation and clears retry access', async () => {
  const { interop } = harness();
  const [key] = await interop.beginSelection({ files: [modelFile('released.stl')] });
  interop.release(key);
  assert.throws(() => interop.retry(key), /Unknown preview correlation/);
  assert.throws(() => interop.admit(key, 'part-released'), /Unknown preview correlation/);
});

test('parser failures report persistent advisory state without raw error details', async () => {
  const { calls, statuses, interop } = harness(async () => { throw new Error('parser detail'); });
  interop.attach({});
  const [key] = await interop.beginSelection({ files: [modelFile('bad.obj')] });
  interop.admit(key, 'server-part');
  await assert.rejects(interop.getGeometryClaim(key), /Geometry analysis is unavailable/);
  assert.equal(interop.status(), 'unavailable');
  assert.deepEqual(statuses, ['unavailable']);
  interop.reset();
  interop.fit();
  interop.fullscreen();
  assert.deepEqual(calls.slice(-3).map(call => call[0]), ['reset', 'fit', 'fullscreen']);
});

test('attached remove delegates disposal exactly once and wrapper disposal does not repeat it', async () => {
  let removals = 0;
  let viewerDisposals = 0;
  const object = disposableObject();
  const viewer = {
    addPart() {},
    remove(_partId) { removals += 1; object.traverse(item => item.geometry?.dispose?.()); },
    dispose() { viewerDisposals += 1; },
  };
  const interop = createWorkflowPreviewInterop({
    loadModel: async () => object,
    analyzeGeometry: () => ({ version: 1 }),
    createViewer: () => viewer,
    reportStatus() {},
  });
  interop.attach({});
  const [key] = await interop.beginSelection({ files: [modelFile('attached.stl')] });
  interop.admit(key, 'attached-part');
  await interop.getGeometryClaim(key);
  interop.remove('attached-part');
  interop.dispose();
  assert.equal(removals, 1);
  assert.equal(viewerDisposals, 1);
});

test('attached parts with shared resources preserve Task4A identity-dedup across remove and disposal', async () => {
  const counts = { geometry: 0, material: 0, texture: 0 };
  const texture = { isTexture: true, dispose() { counts.texture += 1; } };
  const material = { map: texture, dispose() { counts.material += 1; } };
  const geometry = { dispose() { counts.geometry += 1; } };
  const object = () => ({
    traverse(visitor) { visitor({ isMesh: true, geometry, material }); },
  });
  const objects = [object(), object()];
  const viewer = createModelViewer({ adapter: {} });
  const interop = createWorkflowPreviewInterop({
    loadModel: async () => objects.shift(),
    analyzeGeometry: () => ({ version: 1 }),
    createViewer: () => viewer,
    reportStatus() {},
  });
  interop.attach({});
  const keys = await interop.beginSelection({ files: [modelFile('a.stl'), modelFile('b.stl')] });
  interop.admit(keys[0], 'part-a');
  interop.admit(keys[1], 'part-b');
  await Promise.all(keys.map(key => interop.getGeometryClaim(key)));
  interop.remove('part-a');
  interop.dispose();
  assert.deepEqual(counts, { geometry: 1, material: 1, texture: 1 });
});

test('dispose aborts pending parsing and disposes the viewer exactly once', async () => {
  const pending = deferred();
  const { calls, interop } = harness(() => pending.promise);
  interop.attach({});
  await interop.beginSelection({ files: [modelFile('pending.3mf')] });
  interop.dispose();
  interop.dispose();
  assert.equal(calls.filter(call => call[0] === 'dispose').length, 1);
});

test('unadmitted cleanup disposes shared geometry, materials, and textures once by identity', async () => {
  const counts = { geometry: 0, material: 0, texture: 0 };
  const texture = { isTexture: true, dispose() { counts.texture += 1; } };
  const material = { map: texture, dispose() { counts.material += 1; } };
  const geometry = { dispose() { counts.geometry += 1; } };
  const object = () => ({
    traverse(visitor) { visitor({ isMesh: true, geometry, material }); },
  });
  const objects = [object(), object()];
  const { interop } = harness(async () => objects.shift());
  const keys = await interop.beginSelection({ files: [modelFile('a.stl'), modelFile('b.stl')] });
  await Promise.all(keys.map(key => interop.getGeometryClaim(key)));
  interop.release(keys[0]);
  interop.release(keys[1]);
  assert.deepEqual(counts, { geometry: 1, material: 1, texture: 1 });
});

test('bulk select-all uses the native indeterminate property without changing checked state or unrelated inputs', () => {
  const { interop } = harness();
  const input = { type: 'checkbox', checked: false, indeterminate: false };
  try {
    interop.setBulkSelectionState(input, true);
    assert.equal(input.indeterminate, true);
    assert.equal(input.checked, false);
    input.checked = true;
    interop.setBulkSelectionState(input, false);
    assert.equal(input.indeterminate, false);
    assert.equal(input.checked, true);
    const text = { type: 'text', value: 'unchanged' };
    interop.setBulkSelectionState(text, true);
    assert.deepEqual(text, { type: 'text', value: 'unchanged' });
    interop.dispose();
    interop.setBulkSelectionState(input, true);
    assert.equal(input.indeterminate, false);
  } finally { interop.dispose(); }
});

for (const terminal of ['release', 'quarantine', 'dispose']) {
  test(`${terminal} settles a pending claim before an abort-ignoring loader completes`, async () => {
    const pending = deferred();
    const object = disposableObject();
    let analyzed = 0;
    const { interop, calls, statuses } = harness(() => pending.promise, () => {
      analyzed += 1;
      return { version: 1, volumeMm3: 1000 };
    });
    let deadline;
    try {
      interop.attach({});
      const [key] = interop.beginSelection({ files: [modelFile('pending.stl')] });
      const claim = interop.getGeometryClaim(key);
      await Promise.resolve();
      if (terminal === 'dispose') interop.dispose();
      else interop[terminal](key);
      await assert.rejects(Promise.race([claim, new Promise((_, reject) => {
        deadline = setTimeout(() => reject(new Error('Claim did not settle after termination.')), 1000);
      })]), /Geometry analysis is unavailable/);
      pending.resolve(object);
      await new Promise(resolve => setImmediate(resolve));
      assert.equal(object.disposed, true);
      assert.equal(analyzed, 0);
      assert.deepEqual(calls.filter(call => call[0] === 'add'), []);
      assert.deepEqual(statuses, []);
    } finally {
      clearTimeout(deadline);
      pending.resolve(object);
      interop.dispose();
    }
  });
}

test('a synchronous failed loader preserves its neighbour selection and yields no geometry', async () => {
  const { interop } = harness(file => {
    if (file.name === 'bad.stl') throw new Error('private decoder detail');
    return disposableObject();
  });
  try {
    const keys = interop.beginSelection({ files: [modelFile('bad.stl'), modelFile('good.stl')] });
    assert.equal(keys.length, 2);
    await assert.rejects(interop.getGeometryClaim(keys[0]), /Geometry analysis is unavailable/);
    assert.equal((await interop.getGeometryClaim(keys[1])).volumeMm3, 1000);
  } finally { interop.dispose(); }
});

for (const action of ['View', 'Remove']) {
  test(`late selected-part reveal preserves a focused ${action} inside an oversized compact card`, () => {
    const { interop } = harness();
    const oldStyle = globalThis.getComputedStyle;
    const button = { getBoundingClientRect: () => ({ left: 200, right: 528, top: 270, bottom: 314 }) };
    const tile = { dataset: { partId: 'selected' }, ownerDocument: { activeElement: button },
      contains: element => element === button,
      getBoundingClientRect: () => ({ left: 180, right: 860, top: 210, bottom: 348 }) };
    const rail = { scrollLeft: 0, scrollTop: 200,
      getBoundingClientRect: () => ({ left: 164, right: 879, top: 256, bottom: 325 }),
      querySelector: () => null, querySelectorAll: () => [tile] };
    globalThis.getComputedStyle = () => ({ overflowY: 'auto' });
    try {
      assert.equal(interop.revealPart({ querySelector: () => rail }, 'selected'), true);
      assert.equal(rail.scrollTop, 200);
      assert.equal(rail.scrollLeft, 0);
      assert.equal(tile.ownerDocument.activeElement, button);
    } finally {
      globalThis.getComputedStyle = oldStyle;
      interop.dispose();
    }
  });
}

test('selected-part reveal scrolls its focused action instead of the oversized card edge', () => {
  const { interop } = harness();
  const oldStyle = globalThis.getComputedStyle;
  const button = { getBoundingClientRect: () => ({ left: 200, right: 528, top: 300, bottom: 344 }) };
  const tile = { dataset: { partId: 'selected' }, ownerDocument: { activeElement: button },
    contains: element => element === button,
    getBoundingClientRect: () => ({ left: 180, right: 860, top: 206, bottom: 344 }) };
  const rail = { scrollLeft: 0, scrollTop: 200,
    getBoundingClientRect: () => ({ left: 164, right: 879, top: 256, bottom: 325 }),
    querySelector: () => null, querySelectorAll: () => [tile] };
  globalThis.getComputedStyle = () => ({ overflowY: 'auto' });
  try {
    assert.equal(interop.revealPart({ querySelector: () => rail }, 'selected'), true);
    assert.equal(rail.scrollTop, 219);
    assert.equal(rail.scrollLeft, 0);
    assert.equal(tile.ownerDocument.activeElement, button);
  } finally {
    globalThis.getComputedStyle = oldStyle;
    interop.dispose();
  }
});

test('focus in a different card does not replace the selected whole-card reveal target', () => {
  const { interop } = harness();
  const oldStyle = globalThis.getComputedStyle;
  const foreignButton = { getBoundingClientRect: () => { throw new Error('Foreign focus must not be measured.'); } };
  const tile = { dataset: { partId: 'selected' }, ownerDocument: { activeElement: foreignButton },
    contains: () => false,
    getBoundingClientRect: () => ({ left: 500, right: 650, top: 300, bottom: 350 }) };
  const rail = { scrollLeft: 0, scrollTop: 0,
    getBoundingClientRect: () => ({ left: 0, right: 400, top: 0, bottom: 200 }),
    querySelector: () => null, querySelectorAll: () => [tile] };
  globalThis.getComputedStyle = () => ({ overflowY: 'auto' });
  try {
    assert.equal(interop.revealPart({ querySelector: () => rail }, 'selected'), true);
    assert.equal(rail.scrollLeft, 250);
    assert.equal(rail.scrollTop, 150);
    assert.equal(tile.ownerDocument.activeElement, foreignButton);
  } finally {
    globalThis.getComputedStyle = oldStyle;
    interop.dispose();
  }
});

test('wrapped selected-part reveal keeps scrolling scoped to its focused action', () => {
  const { interop } = harness();
  const oldStyle = globalThis.getComputedStyle;
  const oldHeight = globalThis.innerHeight;
  const scrolls = [];
  const button = { getBoundingClientRect: () => ({ left: 20, right: 200, top: 1100, bottom: 1144 }),
    scrollIntoView: options => scrolls.push(options) };
  const tile = { dataset: { partId: 'selected' }, ownerDocument: { activeElement: button },
    contains: element => element === button,
    getBoundingClientRect: () => ({ left: 0, right: 390, top: 950, bottom: 1150 }),
    scrollIntoView: () => { throw new Error('The focused action owns this reveal.'); } };
  const rail = { querySelectorAll: () => [tile],
    getBoundingClientRect: () => ({ left: 0, right: 390, top: 0, bottom: 1200 }) };
  globalThis.getComputedStyle = () => ({ overflowY: 'visible' });
  globalThis.innerHeight = 1000;
  try {
    assert.equal(interop.revealPart({ querySelector: () => rail }, 'selected'), true);
    assert.deepEqual(scrolls, [{ block: 'nearest', inline: 'nearest', behavior: 'auto' }]);
    assert.equal(tile.ownerDocument.activeElement, button);
  } finally {
    globalThis.getComputedStyle = oldStyle;
    if (oldHeight === undefined) delete globalThis.innerHeight;
    else globalThis.innerHeight = oldHeight;
    interop.dispose();
  }
});

test('local reveal changes only the part rail scroll and respects its sticky upload control', () => {
  const { interop } = harness();
  const oldStyle = globalThis.getComputedStyle;
  const add = { getBoundingClientRect: () => ({ left: 0, right: 100 }) };
  const tile = { dataset: { partId: 'part-b' },
    getBoundingClientRect: () => ({ left: 500, right: 650, top: 300, bottom: 350 }) };
  const rail = {
    scrollLeft: 0, scrollTop: 0,
    getBoundingClientRect: () => ({ left: 0, right: 400, top: 0, bottom: 200 }),
    querySelector: selector => selector === '.iq-compact-dropzone' ? add : null,
    querySelectorAll: () => [tile],
  };
  const root = { querySelector: () => rail };
  globalThis.getComputedStyle = () => ({ position: 'sticky' });
  try {
    assert.equal(interop.revealPart(root, 'part-b'), true);
    assert.equal(rail.scrollLeft, 250);
    assert.equal(rail.scrollTop, 150);
    rail.scrollLeft = 200; rail.scrollTop = 100;
    tile.getBoundingClientRect = () => ({ left: 80, right: 180, top: -30, bottom: 20 });
    assert.equal(interop.revealPart(root, 'part-b'), true);
    assert.equal(rail.scrollLeft, 180);
    assert.equal(rail.scrollTop, 70);
    assert.equal(interop.revealPart(root, 'unknown'), false);
    interop.dispose();
    assert.equal(interop.revealPart(root, 'part-b'), false);
    assert.equal(rail.scrollLeft, 180);
    assert.equal(rail.scrollTop, 70);
  } finally {
    globalThis.getComputedStyle = oldStyle;
    interop.dispose();
  }
});


test('a retained local failure thumbnail and selection never create an admitted part binding', async () => {
  const selected = { ...modelFile('open.stl'), size: 100 };
  const calls = [];
  let count = 0;
  const interop = createWorkflowPreviewInterop({
    loadModel: async () => disposableObject(),
    analyzeGeometry: () => ({ dimensionXmm: 20, dimensionYmm: 20, dimensionZmm: 0, volumeMm3: 0,
      surfaceAreaMm2: 400, facetCount: 2, bodyCount: 1, topologyChecked: true,
      nonWatertight: true, nonManifold: false }),
    createViewer: () => {
      const owner = ++count;
      return {
        addPart: key => calls.push(['add', owner, key]),
        select: key => calls.push(['select', owner, key]),
        snapshot: key => { calls.push(['snapshot', owner, key]); return 'data:image/webp;base64,aW1hZ2U='; },
        reset: () => calls.push(['reset', owner]),
        fit: () => calls.push(['fit', owner]),
        fullscreen: () => calls.push(['fullscreen', owner]),
        dispose: () => calls.push(['dispose', owner]),
      };
    },
    thicknessAnalyzer: { analyze: async () => null, dispose() {} },
  });
  try {
    const [key] = interop.beginSelection({ files: [selected] }, Number.POSITIVE_INFINITY, 'millimetres');
    await interop.getGeometryClaim(key);
    interop.attach({});
    assert.equal(interop.getLocalThumbnail(key), null);
    assert.equal(interop.retainIncompletePreview(key, {}), true);
    assert.equal(interop.getLocalThumbnail(key), 'data:image/webp;base64,aW1hZ2U=');
    assert.equal(interop.admit(key, 'invented-part'), false);
    assert.equal(interop.select('invented-part'), false);
    assert.equal(interop.selectLocalPreview(key), true);
    interop.reset(); interop.fit();
    assert.deepEqual(calls.filter(call => call[0] === 'reset' || call[0] === 'fit'), [['reset', 2], ['fit', 2]]);
    assert.deepEqual(calls.filter(call => call[0] === 'add' && call[1] === 1), []);
    interop.release(key);
    assert.equal(interop.getLocalThumbnail(key), null);
    assert.equal(interop.selectLocalPreview(key), false);
  } finally { interop.dispose(); }
  assert.equal(interop.getLocalThumbnail('stale'), null);
  assert.equal(interop.selectLocalPreview('stale'), false);
});


test('an empty detached stage releases its renderer but never disposes an admitted sibling', async () => {
  const { interop, calls } = harness();
  try {
    interop.attach({});
    assert.equal(interop.detachEmptyViewer(), true);
    assert.equal(calls.filter(call => call[0] === 'dispose').length, 1);
    interop.attach({});
    const [key] = interop.beginSelection({ files: [modelFile('healthy.stl')] });
    await interop.getGeometryClaim(key);
    assert.equal(interop.admit(key, 'healthy'), true);
    assert.equal(interop.detachEmptyViewer(), false);
    assert.equal(interop.select('healthy'), true);
    assert.equal(calls.filter(call => call[0] === 'dispose').length, 1);
  } finally { interop.dispose(); }
});


test('a failed renderable local preview snapshots at bounded size and restores the healthy viewer on context loss', async () => {
  const objects = new Map(), shown = [], dimensions = [];
  let losesContext = false;
  const viewer = createModelViewer({ adapter: {
    show: object => shown.push(object), hide() {}, fit() {}, snapshot: maximum => {
      dimensions.push(maximum);
      if (losesContext) throw new Error('synthetic context loss');
      return 'data:image/webp;base64,aW1hZ2U=';
    },
  } });
  const interop = createWorkflowPreviewInterop({
    loadModel: async file => { const object = disposableObject(); objects.set(file.name, object); return object; },
    analyzeGeometry: () => ({ volumeMm3: 1000 }),
    createViewer: () => viewer,
    thicknessAnalyzer: { analyze: async () => null, dispose() {} },
  });
  try {
    interop.attach({});
    const keys = interop.beginSelection({ files: [modelFile('good.stl'), modelFile('bad.stl'), modelFile('lost.stl')] });
    await Promise.all(keys.map(key => interop.getGeometryClaim(key)));
    interop.admit(keys[0], 'healthy');
    assert.equal(interop.captureFailedThumbnail(keys[1]), 'data:image/webp;base64,aW1hZ2U=');
    assert.deepEqual(dimensions, [192]);
    assert.equal(shown.at(-1), objects.get('good.stl'));
    assert.equal(objects.get('good.stl').disposed, false);
    assert.equal(objects.get('bad.stl').disposed, true);
    assert.equal(interop.admit(keys[1], 'fake-server-part'), false);
    assert.equal(interop.select(keys[1]), false);
    losesContext = true;
    assert.equal(interop.captureFailedThumbnail(keys[2]), null);
    assert.equal(shown.at(-1), objects.get('good.stl'));
    assert.equal(objects.get('lost.stl').disposed, true);
    assert.equal(objects.get('good.stl').disposed, false);
    assert.equal(interop.select('healthy'), true);
  } finally { interop.dispose(); }
});


for (const [message, expected] of [
  ['Unable to load 3D file processing support.', 'unavailable'],
  ['CAD preview support failed to initialize.', 'unavailable'],
  ['worker_unavailable', 'unavailable'],
  ['Preview processing timed out.', 'complexity'],
  ['simulation_timeout', 'complexity'],
  ['geometry_complexity_exceeded', 'complexity'],
  ['Unable to read this 3D file.', 'unreadable'],
]) {
  test(`local advisory failure maps ${message} to fixed ${expected} without leaking decoder detail`, async () => {
    const { interop } = harness(async () => { throw new Error(message + ' private-customer.stl'); });
    const [key] = interop.beginSelection({ files: [modelFile('failed.stl')] });
    try {
      await assert.rejects(interop.getGeometryClaim(key), /Geometry analysis is unavailable/);
      assert.equal(interop.getFailureCategory(key), expected);
      assert.equal(interop.getFailureCategory('foreign-key'), null);
      interop.release(key);
      assert.equal(interop.getFailureCategory(key), null);
    } finally { interop.dispose(); }
  });
}

test('a failed preview category cannot replace its healthy neighbour or claim', async () => {
  const { interop } = harness(async file => {
    if (file.name === 'bad.stl') throw new Error('Unable to load private support.');
    return disposableObject();
  });
  const [healthy, failed] = interop.beginSelection({ files: [modelFile('healthy.stl'), modelFile('bad.stl')] });
  try {
    const claim = await interop.getGeometryClaim(healthy);
    await assert.rejects(interop.getGeometryClaim(failed));
    assert.equal(interop.getFailureCategory(healthy), null);
    assert.equal(interop.getFailureCategory(failed), 'unavailable');
    assert.strictEqual(await interop.getGeometryClaim(healthy), claim);
  } finally { interop.dispose(); }
});

test('bounded snapshots validate their size before changing the viewer selection', () => {
  const sizes = [], shown = [];
  const viewer = createModelViewer({ adapter: {
    show: object => shown.push(object), hide() {}, fit() {},
    snapshot: size => { sizes.push(size); return 'data:image/webp;base64,aW1hZ2U='; },
  } });
  const good = disposableObject(), other = disposableObject();
  try {
    viewer.addPart('good', good); viewer.addPart('other', other);
    for (const size of [0, 193, Infinity, NaN, 1.5]) assert.throws(() => viewer.snapshot('other', size), RangeError);
    assert.equal(shown.at(-1), good);
    assert.deepEqual(sizes, []);
    viewer.snapshot('other', 192);
    assert.deepEqual(sizes, [192]);
    assert.equal(shown.at(-1), good);
  } finally { viewer.dispose(); }
});

test('pending local selection is render-only and promotes exactly once after real admission', async () => {
  const object = disposableObject();
  const { interop, calls } = harness(async () => object);
  try {
    const [key] = interop.beginSelection({ files: [modelFile('pending.glb')] }, Number.POSITIVE_INFINITY, 'millimetres');
    await interop.getGeometryClaim(key);
    interop.attach({});
    assert.equal(interop.selectLocalPreview(key), true);
    assert.equal(interop.select(key), false);
    assert.deepEqual(calls.filter(call => call[0] === 'add').map(call => call.slice(1)), [[key, object]]);
    assert.equal(interop.admit(key, 'server-part', 'PLA'), true);
    assert.deepEqual(calls.filter(call => call[0] === 'promote'), [['promote', key, 'server-part']]);
    assert.equal(calls.filter(call => call[0] === 'add').length, 1);
    assert.equal(interop.selectLocalPreview(key), false);
    assert.equal(interop.select('server-part'), true);
    assert.equal(object.disposed, false);
    interop.release(key);
    assert.deepEqual(calls.filter(call => call[0] === 'remove'), [['remove', 'server-part']]);
    assert.equal(interop.select('server-part'), false);
  } finally { interop.dispose(); }
});


for (const completion of ['before-local-attachment', 'after-local-attachment', 'after-admission']) {
  test(`completed thickness evidence survives local preview promotion: ${completion}`, async () => {
    const analysis = deferred();
    const object = disposableObject();
    const evidence = {
      fields: [new Float32Array([1, 1, 1])],
      summary: { state: 'complete', minMm: 1, measuredAreaRatio: 1 },
    };
    const attached = [];
    const reports = [];
    const viewer = createModelViewer({ adapter: {
      attachThickness(model, value) {
        assert.equal(model, object);
        assert.equal(value, evidence);
        attached.push(value);
        return true;
      },
      setThickness: (_model, visible) => visible && attached.length > 0,
    } });
    const interop = createWorkflowPreviewInterop({
      loadModel: async () => object,
      analyzeGeometry: () => ({ version: 1, dimensionZMm: 1, volumeMm3: 400 }),
      createViewer: () => viewer,
      thicknessAnalyzer: { analyze: () => analysis.promise, dispose() {} },
      reportThickness: (...args) => reports.push(args),
    });
    const complete = async () => {
      analysis.resolve(evidence);
      await analysis.promise;
      await Promise.resolve();
    };
    try {
      const [key] = interop.beginSelection({ files: [modelFile('one-millimeter.stl')] });
      await interop.getGeometryClaim(key);
      if (completion === 'before-local-attachment') await complete();
      interop.attach({});
      assert.equal(interop.selectLocalPreview(key), true);
      assert.equal(interop.select(key), false);
      if (completion === 'after-local-attachment') await complete();
      assert.equal(interop.admit(key, 'server-part', 'ABS'), true);
      if (completion === 'after-admission') await complete();
      assert.equal(attached.length, 1);
      assert.equal(reports.at(-1)[0], 'server-part');
      assert.equal(reports.at(-1)[1], true);
      assert.equal(reports.at(-1)[2], false);
      assert.equal(interop.toggleThickness('server-part'), true);
      assert.equal(interop.toggleThickness('server-part'), false);
      interop.release(key);
      assert.equal(interop.select('server-part'), false);
      assert.equal(object.disposed, true);
    } finally {
      analysis.resolve(evidence);
      await analysis.promise;
      interop.dispose();
    }
  });
}

test('admitting another pending file does not steal the explicitly selected local preview', async () => {
  const { interop, calls } = harness();
  try {
    const [first, last] = interop.beginSelection({ files: [modelFile('first.glb'), modelFile('last.glb')] }, Number.POSITIVE_INFINITY, 'millimetres');
    await Promise.all([interop.getGeometryClaim(first), interop.getGeometryClaim(last)]);
    interop.attach({});
    assert.equal(interop.selectLocalPreview(last), true);
    const selected = calls.filter(call => call[0] === 'select').length;
    interop.admit(first, 'server-first');
    assert.equal(calls.filter(call => call[0] === 'select').length, selected);
    interop.admit(last, 'server-last');
    assert.deepEqual(calls.filter(call => call[0] === 'promote'), [['promote', last, 'server-last']]);
    assert.equal(interop.select('server-last'), true);
  } finally { interop.dispose(); }
});

test('removing a selected pending preview prevents local selection and server admission resurrection', async () => {
  const { interop, calls } = harness();
  try {
    const [key] = interop.beginSelection({ files: [modelFile('removed.glb')] }, Number.POSITIVE_INFINITY, 'millimetres');
    await interop.getGeometryClaim(key);
    interop.attach({});
    assert.equal(interop.selectLocalPreview(key), true);
    interop.release(key);
    assert.equal(interop.selectLocalPreview(key), false);
    assert.throws(() => interop.admit(key, 'fabricated-part'), /Unknown preview correlation/);
    assert.equal(interop.select('fabricated-part'), false);
    assert.deepEqual(calls.filter(call => call[0] === 'remove'), [['remove', key]]);
    assert.equal(calls.some(call => call[0] === 'promote'), false);
  } finally { interop.dispose(); }
});

test('local View before parsing returns cannot admit an object and late removal disposes the real result', async () => {
  const loaded = deferred();
  const object = disposableObject();
  const { interop, calls } = harness(() => loaded.promise);
  try {
    const [key] = interop.beginSelection({ files: [modelFile('held.glb')] }, Number.POSITIVE_INFINITY, 'millimetres');
    const claim = interop.getGeometryClaim(key);
    const rejected = assert.rejects(claim, /Geometry analysis is unavailable/);
    interop.attach({});
    assert.equal(interop.selectLocalPreview(key), false);
    interop.release(key);
    loaded.resolve(object);
    await rejected;
    await loaded.promise;
    await Promise.resolve();
    assert.equal(object.disposed, true);
    assert.equal(calls.some(call => call[0] === 'add' || call[0] === 'promote'), false);
  } finally { loaded.resolve(object); interop.dispose(); }
});

test('nine existing parsers start once while completion consumers and local selection remain independently owned', async () => {
  const gate = deferred();
  const objects = Array.from({ length: 9 }, () => disposableObject());
  const loaded = [];
  const actual = [];
  const { interop } = harness(file => {
    const index = loaded.push(file) - 1;
    const task = gate.promise.then(() => objects[index]);
    actual.push(task);
    return task;
  });
  const files = Array.from({ length: 9 }, (_, index) => modelFile(`copy-${index}.glb`));
  let claims = [];
  try {
    const keys = interop.beginSelection({ files }, Number.POSITIVE_INFINITY, 'millimetres');
    claims = keys.map(key => interop.getGeometryClaim(key));
    const observed = Promise.allSettled(claims);
    interop.attach({});
    await Promise.resolve();
    assert.equal(loaded.length, 9);
    assert.deepEqual(loaded, files);
    assert.equal(interop.selectLocalPreview(keys[0]), false);
    interop.release(keys[0]);
    gate.resolve();
    const results = await observed;
    assert.equal(results[0].status, 'rejected');
    assert.equal(results.slice(1).every(result => result.status === 'fulfilled'), true);
    await Promise.all(actual);
    assert.equal(loaded.length, 9);
    assert.equal(actual.length, 9);
  } finally {
    gate.resolve();
    interop.dispose();
    await Promise.allSettled(claims);
    await Promise.allSettled(actual);
  }
  assert.equal(objects.every(object => object.disposed), true);
});

test('wrapped mobile parts reveal only an off-viewport selected tile through actual page scrolling', () => {
  const { interop } = harness();
  const oldStyle = globalThis.getComputedStyle;
  const oldHeight = globalThis.innerHeight;
  const scrolls = [];
  let bounds = { left: 0, right: 390, top: 1200, bottom: 1300 };
  const tile = { dataset: { partId: 'new-tenth' }, getBoundingClientRect: () => bounds,
    scrollIntoView: options => scrolls.push(options) };
  const rail = { querySelectorAll: () => [tile], getBoundingClientRect: () => ({ left: 0, right: 390, top: -500, bottom: 1300 }) };
  globalThis.getComputedStyle = () => ({ overflowY: 'visible' });
  globalThis.innerHeight = 1000;
  try {
    assert.equal(interop.revealPart({ querySelector: () => rail }, 'new-tenth'), true);
    assert.deepEqual(scrolls, [{ block: 'nearest', inline: 'nearest', behavior: 'auto' }]);
    bounds = { left: 0, right: 390, top: 500, bottom: 600 };
    assert.equal(interop.revealPart({ querySelector: () => rail }, 'new-tenth'), true);
    assert.equal(scrolls.length, 1);
  } finally {
    globalThis.getComputedStyle = oldStyle;
    if (oldHeight === undefined) delete globalThis.innerHeight;
    else globalThis.innerHeight = oldHeight;
    interop.dispose();
  }
});

test('a selected unready local file never displays another admitted mesh and preserves its eventual identity', async () => {
  const held = deferred();
  const first = disposableObject(), middle = disposableObject(), last = disposableObject();
  const visible = new Set();
  let camera = { position: [1, 2, 3], target: [0, 0, 0] };
  const viewer = createModelViewer({ adapter: {
    show: object => visible.add(object), hide: object => visible.delete(object), fit() {},
    getCameraState: () => structuredClone(camera),
    setCameraState: value => { camera = structuredClone(value); },
    snapshot: () => 'data:image/webp;base64,aW1hZ2U=',
  } });
  const interop = createWorkflowPreviewInterop({
    loadModel: file => file.name === 'last.glb' ? held.promise : Promise.resolve(file.name === 'first.glb' ? first : middle),
    analyzeGeometry: () => ({ volumeMm3: 1000 }),
    createViewer: () => viewer,
    thicknessAnalyzer: { analyze: async () => null, dispose() {} },
  });
  let claims = [];
  try {
    const [firstKey, middleKey, lastKey] = interop.beginSelection({
      files: [modelFile('first.glb'), modelFile('middle.glb'), modelFile('last.glb')],
    }, Number.POSITIVE_INFINITY, 'millimetres');
    claims = [interop.getGeometryClaim(firstKey), interop.getGeometryClaim(middleKey), interop.getGeometryClaim(lastKey)];
    interop.attach({});
    assert.equal(interop.selectLocalPreview(lastKey), false);
    await claims[0];
    assert.equal(interop.admit(firstKey, 'first-real'), true);
    await claims[1];
    assert.equal(interop.admit(middleKey, 'middle-real'), true);
    interop.release(firstKey);
    assert.equal(first.disposed, true);
    assert.equal(visible.size, 0);
    assert.equal(interop.select(lastKey), false);
    viewer.snapshot('middle-real', 192);
    assert.equal(visible.size, 0);
    held.resolve(last);
    await claims[2];
    assert.deepEqual([...visible], [last]);
    camera = { position: [7, 8, 9], target: [1, 1, 1] };
    assert.equal(interop.admit(lastKey, 'last-real'), true);
    assert.deepEqual([...visible], [last]);
    assert.deepEqual(camera, { position: [7, 8, 9], target: [1, 1, 1] });
    assert.equal(middle.disposed, false);
    assert.equal(last.disposed, false);
    assert.equal(interop.select(lastKey), false);
    assert.equal(interop.select('last-real'), true);
  } finally {
    held.resolve(last);
    interop.dispose();
    await Promise.allSettled(claims);
  }
  assert.equal(first.disposed, true);
  assert.equal(middle.disposed, true);
  assert.equal(last.disposed, true);
});

function wheelRail({ width = 300, contentWidth = 900, height = 120, contentHeight = 120 } = {}) {
  const listeners = new Map();
  const rail = {
    clientWidth: width, scrollWidth: contentWidth, clientHeight: height, scrollHeight: contentHeight,
    scrollLeft: 0,
    addEventListener(type, listener, options) {
      assert.equal(type, 'wheel');
      assert.deepEqual(options, { passive: false });
      listeners.set(type, listener);
    },
    removeEventListener(type, listener) {
      if (listeners.get(type) === listener) listeners.delete(type);
    },
    contains: () => true,
  };
  const input = {
    files: [], dataset: {},
    closest: () => ({ querySelector: () => rail, addEventListener() {}, removeEventListener() {} }),
    addEventListener() {}, removeEventListener() {},
  };
  function wheel(overrides = {}) {
    const event = { deltaX: 0, deltaY: 120, deltaMode: 0,
      target: { closest: () => null }, prevented: false,
      preventDefault() { this.prevented = true; }, ...overrides };
    listeners.get('wheel')?.(event);
    return event;
  }
  return { rail, input, listeners, wheel };
}

test('parts wheel resets from a revealed end and translates forward, reverse, line and page gestures', () => {
  const { interop } = harness();
  const fixture = wheelRail();
  try {
    interop.bindInput(fixture.input);
    fixture.rail.scrollLeft = 600;
    fixture.rail.scrollLeft = 0;
    assert.equal(fixture.wheel().prevented, true);
    assert.equal(fixture.rail.scrollLeft, 120);
    assert.equal(fixture.wheel({ deltaY: -20 }).prevented, true);
    assert.equal(fixture.rail.scrollLeft, 100);
    fixture.wheel({ deltaY: 2, deltaMode: 1 });
    assert.equal(fixture.rail.scrollLeft, 132);
    fixture.wheel({ deltaY: 1, deltaMode: 2 });
    assert.equal(fixture.rail.scrollLeft, 432);
    fixture.wheel({ deltaY: 1000 });
    assert.equal(fixture.rail.scrollLeft, 600);
    assert.equal(fixture.wheel().prevented, false);
    fixture.rail.scrollLeft = 0;
    assert.equal(fixture.wheel({ deltaY: -120 }).prevented, false);
    for (const tag of ['button', 'a', 'label']) {
      fixture.rail.scrollLeft = 0;
      assert.equal(fixture.wheel({ target: { closest: selector => selector.split(', ').includes(tag) ? {} : null } }).prevented, true);
      assert.equal(fixture.rail.scrollLeft, 120);
    }
  } finally { interop.dispose(); }
  assert.equal(fixture.listeners.size, 0);
});

test('parts wheel leaves native vertical, wrapped, modified, consumed and interactive gestures alone', () => {
  const { interop } = harness();
  const fixture = wheelRail();
  try {
    interop.bindInput(fixture.input);
    for (const overrides of [
      { defaultPrevented: true }, { ctrlKey: true }, { metaKey: true },
      { altKey: true }, { shiftKey: true }, { deltaY: 0, deltaX: 0 },
      { target: { closest: selector => {
        assert.equal(selector, 'input, select, textarea, [contenteditable], [role="slider"], [role="spinbutton"]');
        return {};
      } } },
    ]) {
      assert.equal(fixture.wheel(overrides).prevented, false);
      assert.equal(fixture.rail.scrollLeft, 0);
    }
    fixture.rail.scrollHeight = 240;
    assert.equal(fixture.wheel().prevented, false);
    fixture.rail.scrollHeight = 120;
    fixture.rail.scrollWidth = 300;
    assert.equal(fixture.wheel().prevented, false);
    fixture.rail.scrollWidth = 900;
    const selection = globalThis.getSelection;
    try {
      globalThis.getSelection = () => ({ isCollapsed: false, anchorNode: {} });
      assert.equal(fixture.wheel().prevented, false);
    } finally {
      if (selection === undefined) delete globalThis.getSelection;
      else globalThis.getSelection = selection;
    }
    assert.equal(fixture.rail.scrollLeft, 0);
  } finally { interop.dispose(); }
});

test('parts wheel observes actual movement and replaces or disposes only its owned listener', () => {
  const { interop } = harness();
  const first = wheelRail(), second = wheelRail();
  try {
    interop.bindInput(first.input);
    interop.bindInput(first.input);
    assert.equal(first.listeners.size, 1);
    const oldHandler = first.listeners.get('wheel');
    first.input.closest = () => ({ querySelector: () => second.rail, addEventListener() {}, removeEventListener() {} });
    interop.bindInput(first.input);
    assert.equal(first.listeners.size, 0);
    assert.equal(second.listeners.size, 1);
    Object.defineProperty(second.rail, 'scrollLeft', { get: () => 0, set() {}, configurable: true });
    assert.equal(second.wheel().prevented, false);
    interop.bindInput({ files: [], dataset: {}, addEventListener() {}, removeEventListener() {} });
    assert.equal(second.listeners.size, 0);
    const event = { deltaY: 120, target: { closest: () => null }, preventDefault() { throw new Error('stale listener consumed'); } };
    interop.dispose();
    oldHandler(event);
  } finally { interop.dispose(); }
});

test('quote intent starts once at first size and extension accepted file before parse/hash', async () => {
  const order = [];
  const interop = createWorkflowPreviewInterop({
    loadModel: async () => { order.push('parse'); throw new Error('unreadable private-model.stl'); },
    analyzeGeometry: () => ({}),
    createViewer: () => ({}),
    reportQuoteStarted: () => { order.push('started'); },
  });
  try {
    interop.beginSelection({ files: [{ ...modelFile('too-large.stl'), size: 11 }] }, 10);
    interop.beginSelection({ files: [{ ...modelFile('unsupported.exe'), size: 1 }] }, 10);
    assert.deepEqual(order, []);
    const keys = interop.beginSelection({ files: [{ ...modelFile('accepted.stl'), size: 1 }] }, 10);
    await assert.rejects(interop.getGeometryClaim(keys[0]));
    const [second] = interop.beginSelection({ files: [{ ...modelFile('second.obj'), size: 1 }] }, 10);
    await assert.rejects(interop.getGeometryClaim(second));
    assert.deepEqual(order, ['started', 'parse', 'parse']);
  } finally { interop.dispose(); }
});

test('a failing quote intent callback never blocks parsing', async () => {
  let parsed = false;
  const interop = createWorkflowPreviewInterop({
    loadModel: async () => { parsed = true; return disposableObject(); },
    analyzeGeometry: () => ({ version: 1, dimensionZMm: 10, volumeMm3: 1000 }),
    createViewer: () => ({}),
    reportQuoteStarted: () => { throw new Error('analytics unavailable'); },
  });
  try {
    const [key] = interop.beginSelection({ files: [modelFile('accepted.stl')] });
    await interop.getGeometryClaim(key);
    assert.equal(parsed, true);
  } finally { interop.dispose(); }
});

test('empty scene wave has a complete sweep and a still rest without flattening the centre', async () => {
  const { emptySceneRelief, emptySceneMask } = await import('../wwwroot/src/app/js/instant-quotation/empty-scene.mjs');
  const leading = { u: 0, gain: 1, rest: 0, mask: 1 };
  const trailing = { ...leading, u: 1 };
  assert.equal(emptySceneRelief(0, leading), 0);
  assert.ok(Math.abs(emptySceneRelief(0.26, leading) - 0.8) < 1e-12);
  assert.ok(Math.abs(emptySceneRelief(1.94, trailing) - 0.8) < 1e-12);
  for (const time of [2.34, 3, 3.529]) {
    assert.equal(emptySceneRelief(time, leading), 0);
    assert.equal(emptySceneRelief(time, trailing), 0);
  }
  assert.equal(emptySceneMask(0, 0, 16, 10), 0);
  assert.equal(emptySceneMask(0.64, 0, 16, 10), 0);
  assert.equal(emptySceneMask(1, 0, 16, 10), 1);
  assert.equal(emptySceneRelief(0.26, { ...leading, rest: 0.5, mask: 0 }), 0.3);
});

test('empty decoration pauses hidden work and reduced motion renders only the still scene', async () => {
  const { createEmptyScene } = await import('../wwwroot/src/app/js/instant-quotation/empty-scene.mjs');
  const f = emptySceneFixture();
  const scene = createEmptyScene(f.root, f.options);
  assert.equal(f.frames.size, 1);
  f.tick(260);
  assert.ok(f.renderers.every(renderer => renderer.draws > 0));
  f.document.hidden = true;
  f.document.dispatchEvent(new Event('visibilitychange'));
  assert.equal(f.frames.size, 0);
  const draws = f.renderers.map(renderer => renderer.draws);
  f.document.hidden = false;
  f.document.dispatchEvent(new Event('visibilitychange'));
  assert.equal(f.frames.size, 1);
  f.media.matches = true;
  f.media.dispatchEvent(new Event('change'));
  assert.equal(f.frames.size, 0);
  assert.ok(f.renderers.every((renderer, index) => renderer.draws > draws[index]));
  const mesh = f.renderers[1].lastScene.children.find(child => child.isInstancedMesh);
  const before = mesh.instanceMatrix.array.slice();
  f.window.dispatchEvent(new Event('pagehide'));
  f.window.dispatchEvent(new Event('pageshow'));
  assert.equal(f.frames.size, 0);
  assert.deepEqual(mesh.instanceMatrix.array, before);
  scene.dispose();
  assert.equal(f.listeners.size, 0);
});

test('empty decoration releases frames resize timers and every GPU resource once', async () => {
  const { createEmptyScene } = await import('../wwwroot/src/app/js/instant-quotation/empty-scene.mjs');
  const f = emptySceneFixture();
  const scene = createEmptyScene(f.root, f.options);
  f.tick(0);
  const resources = new Set();
  for (const renderer of f.renderers) renderer.lastScene.traverse(object => {
    if (object.isInstancedMesh) resources.add(object);
    if (object.geometry) resources.add(object.geometry);
    for (const material of Array.isArray(object.material) ? object.material : [object.material]) {
      if (material) resources.add(material);
    }
  });
  let released = 0;
  for (const resource of resources) resource.addEventListener('dispose', () => { released += 1; });
  f.window.dispatchEvent(new Event('resize'));
  assert.equal(f.timers.size, 1);
  scene.dispose();
  scene.dispose();
  assert.equal(f.frames.size, 0);
  assert.equal(f.timers.size, 0);
  assert.equal(f.listeners.size, 0);
  assert.equal(f.observerDisconnected(), true);
  assert.equal(released, resources.size);
  assert.ok(f.renderers.every(renderer => renderer.disposals === 1 && renderer.losses === 1));
});

test('empty decoration isolates a failed renderer from its healthy sibling and upload affordance', async () => {
  const { createEmptyScene } = await import('../wwwroot/src/app/js/instant-quotation/empty-scene.mjs');
  for (const failed of [0, 1]) {
    const f = emptySceneFixture();
    f.options.createRenderer = canvas => {
      if (canvas === f.canvases[failed]) throw new Error('synthetic renderer refusal');
      return f.makeRenderer();
    };
    const scene = createEmptyScene(f.root, f.options);
    f.tick(260);
    assert.equal(f.renderers.length, 1);
    assert.ok(f.renderers[0].draws > 0);
    assert.equal(f.root.dataset.cubeReady, failed === 0 ? 'false' : 'true');
    scene.dispose();
    assert.equal(f.frames.size, 0);
    assert.equal(f.listeners.size, 0);
  }
  const f = emptySceneFixture();
  const primary = new Error('synthetic listener refusal');
  f.window.addEventListener = () => { throw primary; };
  assert.throws(() => createEmptyScene(f.root, f.options), error => error === primary);
  assert.equal(f.root.dataset.cubeReady, 'false');
  assert.equal(f.root.dataset.hexReady, 'false');
  assert.equal(f.listeners.size, 0);
  assert.ok(f.renderers.every(renderer => renderer.disposals === 1 && renderer.losses === 1));
});

test('empty decoration attempts remaining releases even when one renderer disposal throws', async () => {
  const { createEmptyScene } = await import('../wwwroot/src/app/js/instant-quotation/empty-scene.mjs');
  const f = emptySceneFixture();
  const scene = createEmptyScene(f.root, f.options);
  f.renderers[0].dispose = () => { f.renderers[0].disposals += 1; throw new Error('synthetic disposal refusal'); };
  assert.throws(() => scene.dispose(), /synthetic disposal refusal/);
  assert.ok(f.renderers.every(renderer => renderer.disposals === 1 && renderer.losses === 1));
  assert.equal(f.frames.size, 0);
  assert.equal(f.listeners.size, 0);
});


test('non-hover whole-workflow drag cues change real hex relief and tint without owning file events', async () => {
  const { createEmptyScene } = await import('../wwwroot/src/app/js/instant-quotation/empty-scene.mjs');
  const f = emptySceneFixture();
  const scene = createEmptyScene(f.root, f.options);
  assert.notEqual(f.zone, f.label);
  const hex = () => f.renderers[1].lastScene.children.find(child => child.isInstancedMesh);
  const snapshot = () => ({
    height: hex().instanceMatrix.array[14],
    tint: hex().instanceColor.array[0] / hex().instanceColor.array[2],
  });
  const send = type => {
    const event = new Event(type, { cancelable: true });
    Object.defineProperty(event, 'dataTransfer', { get() { throw new Error('No file inspection is permitted.'); } });
    event.stopPropagation = () => { throw new Error('No propagation cancellation is permitted.'); };
    assert.equal(f.zone.dispatchEvent(event), true);
    assert.equal(event.defaultPrevented, false);
  };
  f.tick(260);
  const baseline = snapshot();
  send('dragenter');
  send('dragenter');
  send('dragover');
  f.tick(260);
  const dragging = snapshot();
  assert.ok(dragging.height > baseline.height);
  assert.ok(dragging.tint < baseline.tint);
  send('dragleave');
  f.tick(260);
  assert.ok(snapshot().height > dragging.height, 'Leaving a nested child must keep the outer drag cue active.');
  send('drop');
  f.tick(260);
  assert.deepEqual(snapshot(), baseline);
  send('dragover');
  f.tick(260);
  assert.ok(snapshot().height > baseline.height);
  send('dragleave');
  f.tick(260);
  assert.deepEqual(snapshot(), baseline);
  send('dragenter');
  f.tick(260);
  f.document.hidden = true;
  f.document.dispatchEvent(new Event('visibilitychange'));
  f.document.hidden = false;
  f.document.dispatchEvent(new Event('visibilitychange'));
  f.tick(260);
  assert.deepEqual(snapshot(), baseline);
  send('dragenter');
  f.tick(260);
  f.window.dispatchEvent(new Event('pagehide'));
  f.window.dispatchEvent(new Event('pageshow'));
  f.tick(260);
  assert.deepEqual(snapshot(), baseline);
  assert.equal([...f.listeners].filter(entry => entry.value === f.zone).length, 4);
  const draws = f.renderers.map(renderer => renderer.draws);
  scene.dispose();
  send('dragenter');
  send('dragover');
  send('dragleave');
  send('drop');
  f.tick(260);
  assert.deepEqual(f.renderers.map(renderer => renderer.draws), draws);
  assert.equal(f.listeners.size, 0);
  assert.equal(f.frames.size, 0);

  const partial = emptySceneFixture();
  const add = partial.zone.addEventListener;
  const primary = new Error('Synthetic second drag-listener refusal.');
  let registrations = 0;
  partial.zone.addEventListener = (...args) => {
    registrations += 1;
    if (registrations === 2) throw primary;
    add(...args);
  };
  assert.throws(() => createEmptyScene(partial.root, partial.options), error => error === primary);
  assert.equal(partial.listeners.size, 0);
  assert.equal(partial.frames.size, 0);
  assert.ok(partial.renderers.every(renderer => renderer.disposals === 1 && renderer.losses === 1));
});

function emptySceneFixture() {
  const listeners = new Set();
  function target() {
    const value = new EventTarget();
    const add = value.addEventListener.bind(value);
    const remove = value.removeEventListener.bind(value);
    value.addEventListener = (type, listener) => { listeners.add({ value, type, listener }); add(type, listener); };
    value.removeEventListener = (type, listener) => {
      for (const entry of listeners) if (entry.value === value && entry.type === type && entry.listener === listener) listeners.delete(entry);
      remove(type, listener);
    };
    return value;
  }
  const frames = new Map();
  const timers = new Map();
  const window = target();
  const document = target();
  const media = target();
  const label = target();
  const zone = target();
  label.matches = () => false;
  media.matches = false;
  document.hidden = false;
  window.devicePixelRatio = 3;
  window.matchMedia = () => media;
  window.performance = { now: () => 0 };
  let serial = 0;
  window.requestAnimationFrame = callback => { frames.set(++serial, callback); return serial; };
  window.cancelAnimationFrame = id => frames.delete(id);
  window.setTimeout = callback => { timers.set(++serial, callback); return serial; };
  window.clearTimeout = id => timers.delete(id);
  let disconnected = false;
  window.ResizeObserver = class {
    observe() {}
    disconnect() { disconnected = true; }
  };
  const canvases = [
    { clientWidth: 240, clientHeight: 180 },
    { clientWidth: 640, clientHeight: 400 },
  ];
  const root = {
    dataset: {}, isConnected: true,
    querySelector: selector => selector.includes('cube') ? canvases[0] : canvases[1],
    closest: selector => selector === 'label' ? label : zone,
  };
  const renderers = [];
  function makeRenderer() {
    const renderer = {
      draws: 0, disposals: 0, losses: 0,
      setPixelRatio(ratio) { assert.equal(ratio, 2); },
      setSize() {},
      render(scene) { this.draws += 1; this.lastScene = scene; },
      dispose() { this.disposals += 1; },
      forceContextLoss() { this.losses += 1; },
    };
    renderers.push(renderer);
    return renderer;
  }
  return {
    root, canvases, renderers, frames, timers, listeners, window, document, media, label, zone, makeRenderer,
    observerDisconnected: () => disconnected,
    tick(time) {
      const callbacks = [...frames.values()];
      frames.clear();
      for (const callback of callbacks) callback(time);
    },
    options: { windowObject: window, documentObject: document, createRenderer: makeRenderer },
  };
}

test('whole-workflow file drops feed the existing input change snapshot exactly once', async () => {
  const { interop } = harness();
  const zone = new EventTarget();
  const input = new EventTarget();
  input.closest = selector => selector === '.instant-quote__workflow' ? zone : null;
  input.files = [];
  input.disabled = false;
  zone.getAttribute = () => 'false';
  const first = { ...modelFile('drop-first.stl'), size: 1 };
  const second = { ...modelFile('drop-second.obj'), size: 1 };
  const dropped = [first, second];
  let changes = 0;
  try {
    interop.bindInput(input);
    input.addEventListener('change', () => { changes += 1; });
    const over = new Event('dragover', { cancelable: true });
    Object.defineProperty(over, 'dataTransfer', { value: { types: ['Files'], files: [] } });
    assert.equal(zone.dispatchEvent(over), false);
    const drop = new Event('drop', { cancelable: true });
    Object.defineProperty(drop, 'dataTransfer', { value: { types: ['Files'], files: dropped } });
    assert.equal(zone.dispatchEvent(drop), false);
    assert.equal(input.files, dropped);
    assert.equal(changes, 1);
    input.files = [{ ...modelFile('later.stl'), size: 1 }];
    const keys = interop.beginSelection(input, 1);
    assert.equal(keys.length, 2);
    await Promise.all(keys.map(key => interop.getGeometryClaim(key)));
    assert.equal(changes, 1);
  } finally { interop.dispose(); }
});

test('whole-workflow drops ignore non-files and refuse disabled or busy uploads without changing selection', () => {
  const { interop } = harness();
  const zone = new EventTarget();
  const input = new EventTarget();
  const original = [{ ...modelFile('retained.stl'), size: 1 }];
  input.files = original;
  input.closest = () => zone;
  let busy = 'false';
  zone.getAttribute = () => busy;
  let changes = 0;
  const send = transfer => {
    const event = new Event('drop', { cancelable: true });
    Object.defineProperty(event, 'dataTransfer', { value: transfer });
    return zone.dispatchEvent(event);
  };
  try {
    interop.bindInput(input);
    input.addEventListener('change', () => { changes += 1; });
    assert.equal(send({ types: ['text/plain'], files: [] }), true);
    assert.equal(send({ types: ['Files'], files: [] }), false);
    input.disabled = true;
    assert.equal(send({ types: ['Files'], files: [modelFile('disabled.stl')] }), false);
    input.disabled = false;
    busy = 'true';
    assert.equal(send({ types: ['Files'], files: [modelFile('busy.stl')] }), false);
    assert.equal(input.files, original);
    assert.equal(changes, 0);
  } finally { interop.dispose(); }
});

test('whole-workflow drop bindings move with the bound input and release on disposal', async () => {
  const { interop } = harness();
  const zone = new EventTarget();
  const replacementZone = new EventTarget();
  const input = new EventTarget();
  const replacement = new EventTarget();
  input.closest = () => zone;
  replacement.closest = () => replacementZone;
  input.files = [];
  replacement.files = [];
  zone.getAttribute = replacementZone.getAttribute = () => 'false';
  let changes = 0;
  replacement.addEventListener('change', () => { changes += 1; });
  const send = target => {
    const event = new Event('drop', { cancelable: true });
    Object.defineProperty(event, 'dataTransfer', {
      value: { types: ['Files'], files: [{ ...modelFile('current.stl'), size: 1 }] },
    });
    return target.dispatchEvent(event);
  };
  try {
    interop.bindInput(input);
    interop.bindInput(replacement);
    interop.bindInput(replacement);
    assert.equal(send(zone), true);
    assert.deepEqual(input.files, []);
    assert.equal(send(replacementZone), false);
    assert.equal(changes, 1);
    interop.dispose();
    assert.equal(send(replacementZone), true);
    assert.equal(changes, 1);
  } finally { interop.dispose(); }
  const primary = new Error('Synthetic preview cleanup refusal.');
  const failed = harness(async () => ({
    traverse(visitor) { visitor({ geometry: { dispose() { throw primary; } } }); },
  }));
  const failedInput = new EventTarget();
  const failedZone = new EventTarget();
  failedInput.files = [];
  failedInput.closest = () => failedZone;
  failedZone.getAttribute = () => 'false';
  try {
    failed.interop.bindInput(failedInput);
    const [key] = failed.interop.beginSelection({ files: [{ ...modelFile('cleanup.stl'), size: 1 }] });
    await failed.interop.getGeometryClaim(key);
    assert.throws(() => failed.interop.dispose(), error => error === primary);
    assert.equal(send(failedZone), true);
    assert.deepEqual(failedInput.files, []);
  } finally { failed.interop.dispose(); }
});

test('empty decoration shares the viewer entry owner instead of the workflow entry', async () => {
  const viewer = await import('../wwwroot/src/app/js/instant-quotation/model-viewer.mjs');
  const workflow = await import('../wwwroot/src/app/js/instant-quotation/workflow-interop.mjs');
  const emptyScene = await import('../wwwroot/src/app/js/instant-quotation/empty-scene.mjs');
  assert.equal(typeof viewer.createEmptyScene, 'function');
  assert.equal(viewer.createEmptyScene, emptyScene.createEmptyScene);
  assert.equal(Object.hasOwn(workflow, 'createEmptyScene'), false);
});
