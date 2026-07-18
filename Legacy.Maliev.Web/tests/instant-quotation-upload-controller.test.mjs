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

  const retry = controller.retry(part.id);
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

function file(name) {
  return { name, size: 128, arrayBuffer: async () => new ArrayBuffer(8) };
}
