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
    remove: (...args) => calls.push(['remove', ...args]),
    setColor: (...args) => calls.push(['color', ...args]),
    reset: () => calls.push(['reset']),
    fit: () => calls.push(['fit']),
    fullscreen: () => calls.push(['fullscreen']),
    snapshot: id => {
      calls.push(['snapshot', id]);
      if (id === 'missing') throw new RangeError('Unknown viewer part.');
      return `data:image/png;base64,${id}`;
    },
    dispose: () => calls.push(['dispose']),
  };
  return {
    calls,
    statuses,
    interop: createWorkflowPreviewInterop({
      loadModel,
      analyzeGeometry,
      createViewer: () => viewer,
      reportStatus: status => statuses.push(status),
    }),
  };
}

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

    const [key] = interop.beginSelection({ files: [selected] });
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
  assert.equal(admitted.src, 'data:image/png;base64,part-a');
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

  assert.equal(interop.updatePartAppearance('part-b', 'Blue'), 'data:image/png;base64,part-b');
  assert.deepEqual(calls.slice(-3), [
    ['select', 'part-b'],
    ['color', '#2563eb'],
    ['snapshot', 'part-b'],
  ]);
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
  const keys = await interop.beginSelection({ files });
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
    interop.beginSelection({ files: [{ ...modelFile('second.obj'), size: 1 }] }, 10);
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
    closest: () => ({ matches: () => false, classList: { contains: () => false } }),
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
    root, canvases, renderers, frames, timers, listeners, window, document, media, makeRenderer,
    observerDisconnected: () => disconnected,
    tick(time) {
      const callbacks = [...frames.values()];
      frames.clear();
      for (const callback of callbacks) callback(time);
    },
    options: { windowObject: window, documentObject: document, createRenderer: makeRenderer },
  };
}
