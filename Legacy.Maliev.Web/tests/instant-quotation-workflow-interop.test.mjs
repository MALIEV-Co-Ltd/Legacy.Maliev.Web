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
        snapshot: key => { calls.push(['snapshot', owner, key]); return 'data:image/png;base64,aW1hZ2U='; },
        reset: () => calls.push(['reset', owner]),
        fit: () => calls.push(['fit', owner]),
        fullscreen: () => calls.push(['fullscreen', owner]),
        dispose: () => calls.push(['dispose', owner]),
      };
    },
    thicknessAnalyzer: { analyze: async () => null, dispose() {} },
  });
  try {
    const [key] = interop.beginSelection({ files: [selected] });
    await interop.getGeometryClaim(key);
    interop.attach({});
    assert.equal(interop.getLocalThumbnail(key), null);
    assert.equal(interop.retainIncompletePreview(key, {}), true);
    assert.equal(interop.getLocalThumbnail(key), 'data:image/png;base64,aW1hZ2U=');
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
      return 'data:image/png;base64,aW1hZ2U=';
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
    assert.equal(interop.captureFailedThumbnail(keys[1]), 'data:image/png;base64,aW1hZ2U=');
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
    snapshot: size => { sizes.push(size); return 'data:image/png;base64,aW1hZ2U='; },
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
    const [key] = interop.beginSelection({ files: [modelFile('pending.glb')] });
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

test('admitting another pending file does not steal the explicitly selected local preview', async () => {
  const { interop, calls } = harness();
  try {
    const [first, last] = interop.beginSelection({ files: [modelFile('first.glb'), modelFile('last.glb')] });
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
    const [key] = interop.beginSelection({ files: [modelFile('removed.glb')] });
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
    const [key] = interop.beginSelection({ files: [modelFile('held.glb')] });
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
    const keys = interop.beginSelection({ files });
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
    snapshot: () => 'data:image/png;base64,aW1hZ2U=',
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
    });
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
