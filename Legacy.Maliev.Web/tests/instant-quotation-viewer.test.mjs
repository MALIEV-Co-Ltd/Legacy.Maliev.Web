import assert from 'node:assert/strict';
import test from 'node:test';

import {
  colorDisconnectedBodies,
  createCanvasResizeController,
  createModelViewer,
  createOcctLoader,
  loadStandaloneModel,
  stableBodyColor,
  validateStandaloneGltfDocument,
} from '../wwwroot/src/app/js/instant-quotation/model-viewer.mjs';
import {
  BufferGeometry,
  Float32BufferAttribute,
  Mesh,
  MeshStandardMaterial,
  Texture,
} from 'three';
import {
  analyzeAdvisoryGeometry,
  geometryAnalysisLimits,
} from '../wwwroot/src/app/js/instant-quotation/geometry-analysis.mjs';

test('geometry analysis preserves production sampling, topology, and DFM limits', () => {
  assert.deepEqual(geometryAnalysisLimits, {
    normalProfileSamples: 64,
    highFacetProfileSamples: 24,
    highFacetThreshold: 250000,
    topologyTriangleLimit: 200000,
    minimumDimensionMm: 3,
    maximumDimensionMm: 350,
  });
  const small = analyzeAdvisoryGeometry({
    facets: 200000,
    dimensions: { x: 2.99, y: 20, z: 20 },
    watertight: false,
    bodyCount: 2,
  });
  const large = analyzeAdvisoryGeometry({
    facets: 250001,
    dimensions: { x: 351, y: 20, z: 20 },
    watertight: true,
    bodyCount: 1,
  });
  assert.equal(small.profileSamples, 64);
  assert.equal(large.profileSamples, 24);
  assert.equal(small.topologyChecked, true);
  assert.equal(large.topologyChecked, false);
  assert.equal(small.isAuthoritative, false);
  assert.equal(small.volumeMethod, 'bounding-box-fallback');
  assert.deepEqual(small.dfm.map(item => item.code), ['dimension-too-small', 'non-watertight', 'multiple-bodies']);
  assert.deepEqual(large.dfm.map(item => item.code), ['dimension-too-large']);
});

test('non-watertight advisory volume uses the production half-bounding-box fallback', () => {
  const result = analyzeAdvisoryGeometry({
    facets: 12,
    dimensions: { x: 10, y: 10, z: 10 },
    watertight: false,
  });
  assert.equal(result.volumeMethod, 'bounding-box-fallback');
  assert.equal(result.volume, 500);
});

test('oriented triangle-plane sampling derives tapered area and perimeter profiles', () => {
  const result = analyzeAdvisoryGeometry({
    facets: 6,
    dimensions: { x: 10, y: 10, z: 10 },
    watertight: true,
    volume: 1000 / 3,
    triangles: squarePyramidTriangles(),
  });
  assert.equal(result.areaProfile.length, 64);
  assert.equal(result.perimeterProfile.length, 64);
  assert.ok(result.areaProfile[32] > 20 && result.areaProfile[32] < 30);
  assert.ok(result.perimeterProfile[32] > 18 && result.perimeterProfile[32] < 22);
  assert.notEqual(result.areaProfile[32], 100);
  assert.notEqual(result.perimeterProfile[32], 40);
});

test('viewer retains camera state per part and exposes orbit keyboard alternatives', () => {
  const adapter = createAdapter();
  const viewer = createModelViewer({ adapter });
  viewer.addPart('a', disposableObject());
  viewer.addPart('b', disposableObject());
  viewer.select('a');
  adapter.cameraState = { position: [1, 2, 3], target: [0, 0, 0] };
  viewer.select('b');
  adapter.cameraState = { position: [9, 8, 7], target: [1, 1, 1] };
  viewer.select('a');
  assert.deepEqual(adapter.cameraState, { position: [1, 2, 3], target: [0, 0, 0] });

  viewer.handleKey({ key: 'ArrowLeft', preventDefault() {} });
  viewer.handleKey({ key: '+', preventDefault() {} });
  assert.deepEqual(adapter.orbits, [-1]);
  assert.deepEqual(adapter.zooms, [1]);
  viewer.reset();
  viewer.fit();
  viewer.fullscreen();
  assert.equal(adapter.resets, 1);
  assert.equal(adapter.fits, 3);
  assert.equal(adapter.fullscreens, 1);
});

test('disconnected-body colors are stable and color lock prevents material changes', () => {
  assert.equal(stableBodyColor(0), stableBodyColor(0));
  assert.notEqual(stableBodyColor(0), stableBodyColor(1));
  const adapter = createAdapter();
  const viewer = createModelViewer({ adapter });
  viewer.addPart('multi', disposableObject(), { bodyCount: 3 });
  assert.deepEqual(adapter.bodyColors, [stableBodyColor(0), stableBodyColor(1), stableBodyColor(2)]);
  assert.equal(viewer.setColor('#ffffff'), false);
  assert.equal(adapter.colors.length, 0);
});

test('remove and dispose release geometry, material, textures, controls, renderer, listeners, RAF and GPU context', () => {
  const adapter = createAdapter();
  const listeners = [];
  const eventTarget = {
    addEventListener: (type, handler) => listeners.push({ type, handler }),
    removeEventListener: (type, handler) => {
      const index = listeners.findIndex(item => item.type === type && item.handler === handler);
      if (index >= 0) listeners.splice(index, 1);
    },
  };
  const objectA = disposableObject();
  const objectB = disposableObject();
  const viewer = createModelViewer({ adapter, eventTarget });
  viewer.addPart('a', objectA);
  viewer.addPart('b', objectB);
  viewer.remove('a');
  assert.equal(objectA.disposeCount(), 1);
  viewer.dispose();
  assert.equal(objectB.disposeCount(), 1);
  assert.equal(adapter.controlsDisposed, 1);
  assert.equal(adapter.rendererDisposed, 1);
  assert.equal(adapter.contextLost, 1);
  assert.equal(adapter.rafCancelled, 1);
  assert.equal(listeners.length, 0);
});

test('viewer recursively disposes mesh geometry, materials, and textures', () => {
  const counts = { geometry: 0, material: 0, texture: 0 };
  const texture = { isTexture: true, dispose: () => { counts.texture += 1; } };
  const material = { map: texture, dispose: () => { counts.material += 1; } };
  const child = {
    geometry: { dispose: () => { counts.geometry += 1; } },
    material,
  };
  const object = { traverse: callback => callback(child) };
  const viewer = createModelViewer({ adapter: createAdapter() });
  viewer.addPart('resource-part', object);
  viewer.remove('resource-part');
  assert.deepEqual(counts, { geometry: 1, material: 1, texture: 1 });
});

test('one mesh with disconnected triangle shells receives stable per-body vertex colors', () => {
  const geometry = new BufferGeometry();
  geometry.setAttribute('position', new Float32BufferAttribute([
    0, 0, 0, 1, 0, 0, 0, 1, 0,
    10, 0, 0, 11, 0, 0, 10, 1, 0,
  ], 3));
  const originalMaterial = new MeshStandardMaterial({ color: '#ffffff' });
  const mesh = new Mesh(geometry, originalMaterial);
  const root = { traverse: callback => callback(mesh) };
  const count = colorDisconnectedBodies(root);
  const colors = geometry.getAttribute('color');
  assert.equal(count, 2);
  assert.equal(mesh.material, originalMaterial);
  assert.equal(mesh.material.vertexColors, true);
  assert.deepEqual([colors.getX(0), colors.getY(0), colors.getZ(0)], [colors.getX(1), colors.getY(1), colors.getZ(1)]);
  assert.notDeepEqual([colors.getX(0), colors.getY(0), colors.getZ(0)], [colors.getX(3), colors.getY(3), colors.getZ(3)]);
});

test('disconnected-body analysis skips vertex walking above the 200k topology cap', () => {
  let vertexReads = 0;
  const position = {
    count: 600003,
    getX() { vertexReads += 1; throw new Error('topology walk exceeded cap'); },
    getY() { vertexReads += 1; throw new Error('topology walk exceeded cap'); },
    getZ() { vertexReads += 1; throw new Error('topology walk exceeded cap'); },
  };
  const geometry = {
    getAttribute: name => name === 'position' ? position : null,
    getIndex: () => null,
  };
  const root = { traverse: callback => callback({ isMesh: true, geometry }) };
  assert.equal(colorDisconnectedBodies(root), 1);
  assert.equal(vertexReads, 0);
});

test('shared original materials and textures remain owned and dispose exactly once', () => {
  let materialDisposals = 0;
  let textureDisposals = 0;
  const texture = new Texture();
  texture.dispose = () => { textureDisposals += 1; };
  const material = new MeshStandardMaterial({ color: '#ffffff' });
  material.map = texture;
  material.dispose = () => { materialDisposals += 1; };
  const first = new Mesh(singleTriangleGeometry(0), material);
  const second = new Mesh(singleTriangleGeometry(10), material);
  const root = { traverse: callback => { callback(first); callback(second); } };

  assert.equal(colorDisconnectedBodies(root), 2);
  assert.equal(first.material, material);
  assert.equal(second.material, material);
  const viewer = createModelViewer({ adapter: createAdapter() });
  viewer.addPart('shared-resources', root);
  viewer.remove('shared-resources');
  assert.equal(materialDisposals, 1);
  assert.equal(textureDisposals, 1);
});

test('standalone textual GLTF permits embedded data only and rejects external URIs', () => {
  assert.doesNotThrow(() => validateStandaloneGltfDocument(JSON.stringify({
    buffers: [{ uri: 'data:application/octet-stream;base64,AAAA' }],
    images: [{ uri: 'data:image/png;base64,AAAA' }],
  })));
  for (const uri of ['buffer.bin', '/buffer.bin', '//cdn.example/buffer.bin', 'https://example/buffer.bin']) {
    assert.throws(
      () => validateStandaloneGltfDocument(JSON.stringify({ buffers: [{ uri }] })),
      /embedded data URIs/i);
    assert.throws(
      () => validateStandaloneGltfDocument(JSON.stringify({ images: [{ uri }] })),
      /embedded data URIs/i);
  }
});

test('textual GLTF loading rejects an external resource before invoking the loader', async () => {
  const source = new TextEncoder().encode(JSON.stringify({
    asset: { version: '2.0' },
    buffers: [{ uri: 'companion.bin', byteLength: 4 }],
  }));
  const file = {
    name: 'external.gltf',
    arrayBuffer: async () => source.buffer,
  };
  await assert.rejects(loadStandaloneModel(file), /embedded data URIs/i);
});

test('canvas resize caches CSS dimensions and DPR without repeated DPR 2 setSize calls', () => {
  const calls = [];
  const renderer = {
    setPixelRatio: value => calls.push(['ratio', value]),
    setSize: (width, height, updateStyle) => calls.push(['size', width, height, updateStyle]),
  };
  const camera = { aspect: 0, updateProjectionMatrix() { calls.push(['projection']); } };
  const canvas = { clientWidth: 400, clientHeight: 200 };
  const resize = createCanvasResizeController({ renderer, camera, canvas, devicePixelRatio: () => 2 });
  assert.equal(resize.sync(), true);
  assert.equal(resize.sync(), false);
  assert.deepEqual(calls, [['ratio', 2], ['size', 400, 200, false], ['projection']]);
});

test('OCCT loader clears a rejected promise so a transient script failure can retry', async () => {
  const scripts = [];
  const globalObject = {};
  const documentObject = {
    createElement: () => ({
      addEventListener(type, handler) { this[type] = handler; },
    }),
    head: { append: script => scripts.push(script) },
  };
  const loader = createOcctLoader({ documentObject, globalObject });
  const first = loader.load();
  scripts[0].error();
  await assert.rejects(first, /failed to load/i);
  const second = loader.load();
  assert.equal(scripts.length, 2);
  globalObject.occtimportjs = async () => ({ recovered: true });
  scripts[1].load();
  assert.deepEqual(await second, { recovered: true });
});

function createAdapter() {
  let cameraState = { position: [0, 0, 5], target: [0, 0, 0] };
  return {
    orbits: [], zooms: [], colors: [], bodyColors: [], resets: 0, fits: 0, fullscreens: 0,
    controlsDisposed: 0, rendererDisposed: 0, contextLost: 0, rafCancelled: 0,
    get cameraState() { return structuredClone(cameraState); },
    set cameraState(value) { cameraState = structuredClone(value); },
    getCameraState() { return this.cameraState; },
    setCameraState(value) { this.cameraState = value; },
    show() {}, hide() {},
    orbit(value) { this.orbits.push(value); },
    zoom(value) { this.zooms.push(value); },
    reset() { this.resets += 1; },
    fit() { this.fits += 1; },
    fullscreen() { this.fullscreens += 1; },
    setColor(value) { this.colors.push(value); },
    setBodyColors(_object, values) { this.bodyColors = values; },
    disposeControls() { this.controlsDisposed += 1; },
    disposeRenderer() { this.rendererDisposed += 1; },
    loseContext() { this.contextLost += 1; },
    cancelAnimation() { this.rafCancelled += 1; },
  };
}

function disposableObject() {
  let count = 0;
  return { dispose() { count += 1; }, disposeCount: () => count };
}

function squarePyramidTriangles() {
  const a = [0, 0, 0];
  const b = [10, 0, 0];
  const c = [10, 10, 0];
  const d = [0, 10, 0];
  const top = [5, 5, 10];
  return [
    ...a, ...c, ...b, ...a, ...d, ...c,
    ...a, ...b, ...top, ...b, ...c, ...top,
    ...c, ...d, ...top, ...d, ...a, ...top,
  ];
}

function singleTriangleGeometry(offset) {
  const geometry = new BufferGeometry();
  geometry.setAttribute('position', new Float32BufferAttribute([
    offset, 0, 0, offset + 1, 0, 0, offset, 1, 0,
  ], 3));
  return geometry;
}
