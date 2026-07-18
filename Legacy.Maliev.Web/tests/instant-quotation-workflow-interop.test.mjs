import assert from 'node:assert/strict';
import test from 'node:test';

import {
  createWorkflowPreviewInterop,
  supportedPreviewExtensions,
} from '../wwwroot/src/app/js/instant-quotation/workflow-interop.mjs';

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

function harness(loadModel = async () => disposableObject()) {
  const calls = [];
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
    interop: createWorkflowPreviewInterop({ loadModel, createViewer: () => viewer }),
  };
}

test('supports the exact nine standalone quotation extensions', () => {
  assert.deepEqual(supportedPreviewExtensions, [
    'stl', 'obj', '3mf', 'glb', 'gltf', 'stp', 'step', 'igs', 'iges',
  ]);
});

test('starts duplicate-name previews in selection order without waiting for parsing', async () => {
  const first = deferred();
  const second = deferred();
  const files = [{ name: 'same.stl' }, { name: 'same.stl' }];
  let call = 0;
  const { interop } = harness(() => [first.promise, second.promise][call++]);
  const keys = await interop.beginSelection({ files });
  assert.equal(keys.length, 2);
  assert.notEqual(keys[0], keys[1]);
  interop.admit(keys[0], 'part-a');
  interop.admit(keys[1], 'part-b');
  second.resolve(disposableObject());
  first.resolve(disposableObject());
  await Promise.all([first.promise, second.promise]);
  await Promise.resolve();
});

test('admits successful previews and releases failed, removed, cancelled, and stale previews', async () => {
  const objects = [disposableObject(), disposableObject(), disposableObject()];
  const { calls, interop } = harness(async (_file, { signal }) => {
    if (signal.aborted) throw new DOMException('cancelled', 'AbortError');
    return objects.shift();
  });
  interop.attach({});
  const [ok, failed, cancelled] = await interop.beginSelection({
    files: [{ name: 'a.stl' }, { name: 'b.stl' }, { name: 'c.stl' }],
  });
  interop.admit(ok, 'part-a');
  interop.release(failed);
  interop.release(cancelled);
  await Promise.resolve();
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
  const [oldKey] = await interop.beginSelection({ files: [{ name: 'part.step' }] });
  interop.release(oldKey);
  const newKey = interop.retry(oldKey);
  assert.notEqual(newKey, oldKey);
  interop.admit(newKey, 'part-new');
  oldLoad.resolve(disposableObject());
  newLoad.resolve(disposableObject());
  await Promise.all([oldLoad.promise, newLoad.promise]);
  await Promise.resolve();
  assert.deepEqual(calls.filter(call => call[0] === 'add').map(call => call[1]), ['part-new']);
});

test('parser and module-adapter failures stay advisory and controls remain callable', async () => {
  const { calls, interop } = harness(async () => { throw new Error('parser detail'); });
  interop.attach({});
  const [key] = await interop.beginSelection({ files: [{ name: 'bad.obj' }] });
  interop.admit(key, 'server-part');
  await Promise.resolve();
  assert.equal(interop.status(), 'unavailable');
  interop.reset();
  interop.fit();
  interop.fullscreen();
  assert.deepEqual(calls.slice(-3).map(call => call[0]), ['reset', 'fit', 'fullscreen']);
});

test('dispose aborts pending parsing and disposes the viewer exactly once', async () => {
  const pending = deferred();
  const { calls, interop } = harness(() => pending.promise);
  interop.attach({});
  await interop.beginSelection({ files: [{ name: 'pending.3mf' }] });
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
  const keys = await interop.beginSelection({ files: [{ name: 'a.stl' }, { name: 'b.stl' }] });
  await Promise.resolve();
  interop.release(keys[0]);
  interop.release(keys[1]);
  assert.deepEqual(counts, { geometry: 1, material: 1, texture: 1 });
});
