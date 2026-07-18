import assert from 'node:assert/strict';
import test from 'node:test';

import {
  createModelViewer,
  stableBodyColor,
} from '../wwwroot/src/app/js/instant-quotation/model-viewer.mjs';
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
