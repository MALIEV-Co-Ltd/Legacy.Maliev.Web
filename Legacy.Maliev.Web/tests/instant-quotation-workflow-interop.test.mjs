import assert from 'node:assert/strict';
import test from 'node:test';

import {
  createWorkflowPreviewInterop,
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
    reset: () => calls.push(['reset']),
    fit: () => calls.push(['fit']),
    fullscreen: () => calls.push(['fullscreen']),
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
