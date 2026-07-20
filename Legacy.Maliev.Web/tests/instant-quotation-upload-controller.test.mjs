import assert from 'node:assert/strict';
import test from 'node:test';

import {
  SUPPORTED_MODEL_EXTENSIONS,
  createUploadController,
  modelExtension,
} from '../wwwroot/src/app/js/instant-quotation/upload-controller.mjs';

const expectedExtensions = ['stl', 'obj', '3mf', 'glb', 'gltf', 'stp', 'step', 'igs', 'iges'];

test('accepts exactly the production model extensions and rejects companion or malformed names', () => {
  assert.deepEqual([...SUPPORTED_MODEL_EXTENSIONS], expectedExtensions);
  for (const extension of expectedExtensions) {
    assert.equal(modelExtension(`part.${extension.toUpperCase()}`), extension);
  }
  for (const name of ['', '.stl', 'part', 'part.stl.exe', 'part.mtl', 'part.bin', 'part.pdf']) {
    assert.throws(() => modelExtension(name), /unsupported model file/i);
  }
});

test('keeps multiple part operations independent and marks browser analysis advisory', async () => {
  const uploads = new Map();
  const transport = ({ partId, signal, onProgress }) => new Promise((resolve, reject) => {
    uploads.set(partId, { resolve, reject, signal, onProgress });
  });
  const controller = createUploadController({
    transport,
    preview: async ({ partId }) => ({ object: { partId }, geometry: { facets: 12 } }),
  });

  const first = controller.add(file('first.stl'));
  const second = controller.add(file('second.step'));
  uploads.get(first.id).onProgress({ loaded: 4, total: 10 });
  uploads.get(second.id).onProgress({ loaded: 9, total: 10 });
  assert.equal(controller.get(first.id).progress, 40);
  assert.equal(controller.get(second.id).progress, 90);

  uploads.get(second.id).resolve({ uploadReference: 'opaque-second' });
  uploads.get(first.id).resolve({ uploadReference: 'opaque-first' });
  await Promise.all([first.completion, second.completion]);

  assert.equal(controller.get(first.id).uploadReference, 'opaque-first');
  assert.equal(controller.get(second.id).uploadReference, 'opaque-second');
  assert.deepEqual(controller.get(first.id).advisoryGeometry, {
    facets: 12,
    authoritative: false,
    isAuthoritative: false,
    authority: 'browser-advisory',
  });
  assert.equal('price' in controller.get(first.id), false);
});

test('cancel and retry use new generations and suppress stale callbacks', async () => {
  const calls = [];
  const transport = ({ operationId, signal, onProgress }) => new Promise((resolve, reject) => {
    calls.push({ operationId, signal, onProgress, resolve, reject });
  });
  const controller = createUploadController({ transport, preview: async () => ({ geometry: {} }) });
  const part = controller.add(file('retry.3mf'));
  controller.cancel(part.id);
  assert.equal(calls[0].signal.aborted, true);
  assert.equal(controller.get(part.id).status, 'cancelled');

  const retry = await controller.retry(part.id);
  assert.notEqual(calls[0].operationId, calls[1].operationId);
  calls[0].onProgress({ loaded: 10, total: 10 });
  calls[0].resolve({ uploadReference: 'stale' });
  calls[1].onProgress({ loaded: 3, total: 10 });
  assert.equal(controller.get(part.id).progress, 30);
  calls[1].resolve({ uploadReference: 'fresh' });
  await retry.completion;

  assert.equal(controller.get(part.id).progress, 100);
  assert.equal(controller.get(part.id).uploadReference, 'fresh');
  assert.equal(controller.get(part.id).status, 'ready');
});

test('remove aborts in-flight work and releases preview and transport resources', async () => {
  let releaseCount = 0;
  let removeCount = 0;
  let pending;
  const controller = createUploadController({
    transport: ({ signal }) => new Promise(resolve => { pending = { resolve, signal }; }),
    preview: async () => ({ object: { dispose: () => { releaseCount += 1; } }, geometry: {} }),
    removeUpload: async reference => {
      assert.equal(reference, 'opaque-ref');
      removeCount += 1;
    },
  });
  const part = controller.add(file('remove.glb'));
  pending.resolve({ uploadReference: 'opaque-ref' });
  await part.completion;
  await controller.remove(part.id);

  assert.equal(pending.signal.aborted, true);
  assert.equal(releaseCount, 1);
  assert.equal(removeCount, 1);
  assert.equal(controller.get(part.id), null);
});

test('transport boundary contains no endpoint, credentials, storage path, or authority promotion', async () => {
  let request;
  const controller = createUploadController({
    transport: async value => { request = value; return { uploadReference: 'opaque' }; },
    preview: async () => ({ geometry: { volume: 123 } }),
  });
  const part = controller.add(file('safe.iges'));
  await part.completion;
  const serialized = JSON.stringify(request);
  assert.doesNotMatch(serialized, /fileservice|bucket|storage|credential|bearer|authoritative/i);
  assert.equal(controller.get(part.id).advisoryGeometry.isAuthoritative, false);
});

test('late stale success removes its remote upload and disposes its preview object', async () => {
  const uploads = [];
  const previews = [];
  const removed = [];
  let disposed = 0;
  const controller = createUploadController({
    transport: () => deferredInto(uploads),
    preview: () => deferredInto(previews),
    removeUpload: async reference => removed.push(reference),
  });
  const first = controller.add(file('stale.stl'));
  controller.cancel(first.id);
  const replacement = await controller.retry(first.id);
  uploads[0].resolve({ uploadReference: 'stale-reference' });
  previews[0].resolve({ object: { dispose: () => { disposed += 1; } }, geometry: {} });
  await first.completion;
  assert.deepEqual(removed, ['stale-reference']);
  assert.equal(disposed, 1);

  uploads[1].resolve({ uploadReference: 'fresh-reference' });
  previews[1].resolve({ geometry: {} });
  await replacement.completion;
  assert.equal(controller.get(first.id).uploadReference, 'fresh-reference');
});

test('partial upload success is remotely cleaned when preview fails', async () => {
  const removed = [];
  const controller = createUploadController({
    transport: async () => ({ uploadReference: 'partial-reference' }),
    preview: async () => { throw new Error('preview failed'); },
    removeUpload: async reference => removed.push(reference),
  });
  const part = controller.add(file('partial.obj'));
  await part.completion;
  assert.deepEqual(removed, ['partial-reference']);
  assert.equal(controller.get(part.id).status, 'failed');
  assert.equal(controller.get(part.id).uploadReference, null);
});

test('retrying a ready part cleans the replaced upload and preview first', async () => {
  const removed = [];
  let disposed = 0;
  let uploadNumber = 0;
  const controller = createUploadController({
    transport: async () => ({ uploadReference: `reference-${++uploadNumber}` }),
    preview: async () => ({ object: { dispose: () => { disposed += 1; } }, geometry: {} }),
    removeUpload: async reference => removed.push(reference),
  });
  const part = controller.add(file('ready.glb'));
  await part.completion;
  const replacement = await controller.retry(part.id);
  assert.deepEqual(removed, ['reference-1']);
  assert.equal(disposed, 1);
  await replacement.completion;
  assert.equal(controller.get(part.id).uploadReference, 'reference-2');
});

test('failed remote removal keeps retryable state until cleanup succeeds', async () => {
  let attempts = 0;
  const controller = createUploadController({
    transport: async () => ({ uploadReference: 'retry-removal' }),
    preview: async () => ({ geometry: {} }),
    removeUpload: async () => {
      attempts += 1;
      if (attempts === 1) throw new Error('temporary cleanup failure');
    },
  });
  const part = controller.add(file('cleanup.3mf'));
  await part.completion;
  await assert.rejects(controller.remove(part.id), /temporary cleanup failure/);
  assert.equal(controller.get(part.id).status, 'cleanup-failed');
  assert.equal(controller.get(part.id).uploadReference, 'retry-removal');
  await controller.remove(part.id);
  assert.equal(controller.get(part.id), null);
});

test('dispose cancels operations, cleans remote uploads, and deeply releases previews', async () => {
  const removed = [];
  const counts = { geometry: 0, material: 0, texture: 0 };
  const controller = createUploadController({
    transport: async ({ partId }) => ({ uploadReference: `reference-${partId}` }),
    preview: async () => ({ object: previewResources(counts), geometry: {} }),
    removeUpload: async reference => removed.push(reference),
  });
  const one = controller.add(file('one.stl'));
  const two = controller.add(file('two.stl'));
  await Promise.all([one.completion, two.completion]);
  const result = await controller.dispose();
  assert.deepEqual(removed.sort(), ['reference-part-1', 'reference-part-2']);
  assert.deepEqual(counts, { geometry: 2, material: 2, texture: 2 });
  assert.deepEqual(result.failures, []);
  assert.deepEqual(controller.list(), []);
});

test('dispose waits for aborted in-flight work and cleans a late remote success', async () => {
  const uploads = [];
  const previews = [];
  const removed = [];
  let transportSignal;
  const controller = createUploadController({
    transport: ({ signal }) => { transportSignal = signal; return deferredInto(uploads); },
    preview: () => deferredInto(previews),
    removeUpload: async reference => removed.push(reference),
  });
  controller.add(file('pending.step'));
  const disposal = controller.dispose();
  assert.equal(transportSignal.aborted, true);
  uploads[0].resolve({ uploadReference: 'late-dispose-reference' });
  previews[0].resolve({ geometry: {} });
  const result = await disposal;
  assert.deepEqual(removed, ['late-dispose-reference']);
  assert.deepEqual(result.failures, []);
});

function file(name) {
  return { name, size: 128, arrayBuffer: async () => new ArrayBuffer(8) };
}

function deferredInto(collection) {
  let resolve;
  let reject;
  const promise = new Promise((accept, decline) => { resolve = accept; reject = decline; });
  collection.push({ resolve, reject });
  return promise;
}

function previewResources(counts) {
  const texture = { isTexture: true, dispose: () => { counts.texture += 1; } };
  const material = { map: texture, dispose: () => { counts.material += 1; } };
  const child = { geometry: { dispose: () => { counts.geometry += 1; } }, material };
  return { traverse: callback => callback(child) };
}
