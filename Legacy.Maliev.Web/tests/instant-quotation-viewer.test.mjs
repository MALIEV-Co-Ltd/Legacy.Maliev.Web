import assert from 'node:assert/strict';
import test from 'node:test';
import * as viewerModule from '../wwwroot/src/app/js/instant-quotation/model-viewer.mjs';

import {
  addCadPreviewEdges,
  buildCadModel,
  colorDisconnectedBodies,
  createCanvasResizeController,
  createModelViewer,
  createOcctLoader,
  ensurePreviewGeometryNormals,
  frameObject,
  loadStandaloneModel,
  orientGltfForPrinting,
  stableBodyColor,
  validateCadTessellation,
  validateStandaloneGltfDocument,
} from '../wwwroot/src/app/js/instant-quotation/model-viewer.mjs';
import {
  Box3,
  BoxGeometry,
  BufferGeometry,
  Float32BufferAttribute,
  Frustum,
  Matrix4,
  Mesh,
  MeshStandardMaterial,
  PerspectiveCamera,
  Texture,
  Vector3,
} from 'three';

test('indexed preview geometry is expanded and flat-shaded before analysis', () => {
  const geometry = new BufferGeometry();
  geometry.setAttribute('position', new Float32BufferAttribute([
    0, 0, 0, 1, 0, 0, 0, 1, 0,
    0, 0, 0, 0, 1, 0, 0, 0, 1,
  ], 3));
  geometry.setIndex([0, 1, 2, 0, 3, 4]);
  const material = new MeshStandardMaterial({ color: '#ffffff' });
  const mesh = new Mesh(geometry, material);
  const root = { traverse: callback => callback(mesh) };

  ensurePreviewGeometryNormals(root);

  assert.equal(mesh.geometry.index, null);
  assert.equal(mesh.geometry.getAttribute('normal').count, mesh.geometry.getAttribute('position').count);
  assert.equal(material.flatShading, true);
  assert.equal(material.version > 0, true);
});

test('CAD tessellation keeps native normals and B-rep face ranges for edge rendering', () => {
  const nativeNormals = [
    0, 0, -1, 0, 0, -1, 0, 0, -1, 0, 0, -1,
    0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1,
  ];
  const model = buildCadModel({
    success: true,
    meshes: [{
      attributes: {
        position: { array: cubeVertices() },
        normal: { array: nativeNormals },
      },
      index: { array: cubeIndices() },
      brep_faces: cubeFaceRanges(),
    }],
  });
  const mesh = model.children[0];

  ensurePreviewGeometryNormals(model);

  assert.notEqual(mesh.geometry.index, null);
  assert.deepEqual(Array.from(mesh.geometry.getAttribute('normal').array), nativeNormals);
  assert.deepEqual(mesh.geometry.userData.cadFaceRanges, cubeFaceRanges());
});

test('native CAD face boundaries render as depth-tested edges with recessed faces', () => {
  const model = buildCadModel({
    success: true,
    meshes: [{
      attributes: { position: { array: cubeVertices() } },
      index: { array: cubeIndices() },
      brep_faces: cubeFaceRanges(),
    }],
  });
  const mesh = model.children[0];

  assert.equal(addCadPreviewEdges(model), 1);

  const edges = mesh.children[0];
  assert.equal(edges.isLineSegments, true);
  assert.equal(edges.userData.cadFeatureEdges, true);
  assert.equal(edges.userData.cadEdgeSource, 'brep-face-boundaries');
  assert.equal(edges.geometry.getAttribute('position').count, 24);
  assert.equal(edges.material.depthTest, true);
  assert.equal(edges.material.depthWrite, false);
  assert.equal(mesh.material.polygonOffset, true);
  assert.equal(mesh.material.polygonOffsetFactor, 1);
  assert.equal(mesh.material.polygonOffsetUnits, 1);
  assert.equal(addCadPreviewEdges(model), 0);
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

test('review snapshot renders the requested part and restores the active part', () => {
  const adapter = createAdapter();
  const viewer = createModelViewer({ adapter });
  viewer.addPart('a', disposableObject());
  viewer.addPart('b', disposableObject());
  viewer.select('a');

  assert.equal(viewer.snapshot('b'), 'data:image/png;base64,preview');
  assert.equal(adapter.snapshots, 1);
  viewer.setColor('#123456');
  assert.equal(adapter.colors.length, 1);
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

test('aggregate topology cap skips every mesh when two 150k meshes exceed 200k together', () => {
  let vertexReads = 0;
  const first = fakeLargeMesh(150000, () => { vertexReads += 1; });
  const second = fakeLargeMesh(150000, () => { vertexReads += 1; });
  const root = { traverse: callback => { callback(first); callback(second); } };
  assert.equal(colorDisconnectedBodies(root), 2);
  assert.equal(vertexReads, 0);
});

test('aggregate fallback gives shared materials distinct colors and disposes every owner once', () => {
  let originalDisposals = 0;
  let replacementDisposals = 0;
  let textureDisposals = 0;
  const texture = new Texture();
  texture.dispose = () => { textureDisposals += 1; };
  const original = new MeshStandardMaterial({ color: '#ffffff', map: texture });
  original.dispose = () => { originalDisposals += 1; };
  const first = fakeLargeMesh(150000, () => {}, original);
  const second = fakeLargeMesh(150000, () => {}, original);
  const root = { traverse: callback => { callback(first); callback(second); } };

  assert.equal(colorDisconnectedBodies(root), 2);
  assert.notEqual(first.material, original);
  assert.notEqual(second.material, original);
  assert.notEqual(first.material, second.material);
  assert.equal(`#${first.material.color.getHexString()}`, stableBodyColor(0));
  assert.equal(`#${second.material.color.getHexString()}`, stableBodyColor(1));
  first.material.dispose = () => { replacementDisposals += 1; };
  second.material.dispose = () => { replacementDisposals += 1; };

  const viewer = createModelViewer({ adapter: createAdapter() });
  viewer.addPart('aggregate-fallback', root);
  viewer.remove('aggregate-fallback');
  assert.equal(originalDisposals, 1);
  assert.equal(replacementDisposals, 2);
  assert.equal(textureDisposals, 1);
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

test('glTF uses the production Y-up to printing Z-up rotation before geometry analysis', () => {
  const scene = { rotation: { x: 0 } };
  assert.equal(orientGltfForPrinting(scene), scene);
  assert.equal(scene.rotation.x, Math.PI / 2);
});

test('CAD loading rejects missing or unsuccessful OCCT tessellation results', () => {
  assert.throws(() => validateCadTessellation(null), /Unable to tessellate/i);
  assert.throws(() => validateCadTessellation({ success: false, meshes: [] }), /Unable to tessellate/i);
  const result = { success: true, meshes: [] };
  assert.equal(validateCadTessellation(result), result);
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
  await assert.rejects(loadStandaloneModel(file, { gltfUnitPolicy: { version: 1, unit: 0 } }), /embedded data URIs/i);
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

test('aspect reframe settles for 180 ms and ignores DPR-only changes and subpixel churn', () => {
  let time = 0;
  let ratio = 1;
  const reframes = [];
  const renderer = { setPixelRatio() {}, setSize() {} };
  const camera = { aspect: 1, updateProjectionMatrix() {} };
  const canvas = { clientWidth: 400, clientHeight: 200 };
  const resize = createCanvasResizeController({
    renderer, camera, canvas, devicePixelRatio: () => ratio,
    now: () => time, onAspectSettled: () => reframes.push(camera.aspect),
  });
  resize.sync();
  ratio = 2;
  time = 200;
  resize.sync();
  canvas.clientWidth = 401;
  time = 400;
  resize.sync();
  time = 600;
  resize.sync();
  assert.deepEqual(reframes, []);
  canvas.clientWidth = 200;
  resize.sync();
  time = 779;
  resize.sync();
  assert.deepEqual(reframes, []);
  canvas.clientWidth = 100;
  resize.sync();
  time = 958;
  resize.sync();
  assert.deepEqual(reframes, []);
  time = 959;
  resize.sync();
  assert.deepEqual(reframes, [0.5]);
  time = 2000;
  resize.sync();
  assert.deepEqual(reframes, [0.5]);
});

test('settled portrait resize frames the selected mesh through the production camera formula', () => {
  const mesh = new Mesh(new BoxGeometry(120, 60, 20), new MeshStandardMaterial());
  mesh.position.set(42, -17, 5);
  mesh.rotation.set(0.2, 0.4, 0.1);
  const camera = new PerspectiveCamera(45, 1, 0.1, 100000);
  const controls = {
    target: new Vector3(),
    update() { camera.lookAt(this.target); camera.updateMatrixWorld(true); },
  };
  let fits = 0;
  const adapter = {
    show() {}, hide() {},
    fit(object) { fits += 1; frameObject(object, camera, controls); },
  };
  const viewer = createModelViewer({ adapter });
  let time = 0;
  const canvas = { clientWidth: 600, clientHeight: 300 };
  const resize = createCanvasResizeController({
    renderer: { setPixelRatio() {}, setSize() {} }, camera, canvas,
    devicePixelRatio: () => 1, now: () => time, onAspectSettled: () => viewer.fit(),
  });
  resize.sync();
  viewer.addPart('selected', mesh);
  const initialPosition = camera.position.toArray();
  canvas.clientWidth = 150;
  resize.sync();
  time = 180;
  resize.sync();
  assert.equal(fits, 2);
  assert.notDeepEqual(camera.position.toArray(), initialPosition);
  const bounds = new Box3().setFromObject(mesh);
  assert.deepEqual(controls.target.toArray(), bounds.getCenter(new Vector3()).toArray());
  for (const aspect of [0.25, 0.5, 2]) {
    camera.aspect = aspect;
    frameObject(mesh, camera, controls);
    const frustum = new Frustum().setFromProjectionMatrix(
      new Matrix4().multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse));
    for (const x of [bounds.min.x, bounds.max.x]) {
      for (const y of [bounds.min.y, bounds.max.y]) {
        for (const z of [bounds.min.z, bounds.max.z]) {
          assert.equal(frustum.containsPoint(new Vector3(x, y, z)), true);
        }
      }
    }
  }
  viewer.dispose();
});

test('aspect reframe uses the current selection and empty viewers do not fit a missing object', () => {
  const fitted = [];
  const adapter = { show() {}, hide() {}, fit: object => fitted.push(object) };
  const viewer = createModelViewer({ adapter });
  viewer.fit();
  assert.deepEqual(fitted, []);
  const first = disposableObject();
  const second = disposableObject();
  viewer.addPart('first', first);
  viewer.addPart('second', second);
  let time = 0;
  const canvas = { clientWidth: 600, clientHeight: 300 };
  const resize = createCanvasResizeController({
    renderer: { setPixelRatio() {}, setSize() {} },
    camera: { aspect: 1, updateProjectionMatrix() {} }, canvas,
    now: () => time, onAspectSettled: () => viewer.fit(),
  });
  resize.sync();
  canvas.clientWidth = 150;
  resize.sync();
  viewer.select('second');
  time = 180;
  resize.sync();
  assert.equal(fitted.at(-1), second);
  viewer.dispose();
  assert.equal(first.disposeCount(), 1);
  assert.equal(second.disposeCount(), 1);
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

test('production preview scene retains the source neutral background and four-light rig', () => {
  const scene = viewerModule.createPreviewScene();
  assert.equal(scene.isScene, true);
  assert.equal(scene.background.getHex(), 0xf7f9fc);
  assert.equal(scene.children.length, 4);
  const [hemisphere, key, fill, rim] = scene.children;
  assert.equal(hemisphere.isHemisphereLight, true);
  assert.equal(hemisphere.color.getHex(), 0xffffff);
  assert.equal(hemisphere.groundColor.getHex(), 0xd6dfe6);
  assert.equal(hemisphere.intensity, 0.56);
  for (const [light, color, intensity, position] of [
    [key, 0xffffff, 0.64, [50, 30, 60]],
    [fill, 0xdbe6ef, 0.26, [-50, -15, 30]],
    [rim, 0xb8c8d8, 0.1, [-20, 50, -30]],
  ]) {
    assert.equal(light.isDirectionalLight, true);
    assert.equal(light.color.getHex(), color);
    assert.equal(light.intensity, intensity);
    assert.deepEqual(light.position.toArray(), position);
  }
});

test('production capture renders white then restores the exact live background and camera', () => {
  const scene = viewerModule.createPreviewScene();
  const liveBackground = scene.background;
  const camera = new PerspectiveCamera(45, 1, 0.1, 100000);
  camera.position.set(12, 23, 34);
  const renders = [];
  const renderer = {
    render(actualScene, actualCamera) {
      assert.equal(actualScene, scene);
      assert.equal(actualCamera, camera);
      renders.push(actualScene.background.getHex());
    },
  };
  for (const image of ['data:image/png;base64,preview', 'data:,']) {
    const canvas = {
      toDataURL(type) {
        assert.equal(type, 'image/png');
        assert.equal(scene.background.getHex(), 0xffffff);
        return image;
      },
    };
    assert.equal(viewerModule.capturePreviewSnapshot({ scene, camera, renderer, canvas }),
      image === 'data:,' ? null : image);
    assert.equal(scene.background, liveBackground);
    assert.deepEqual(camera.position.toArray(), [12, 23, 34]);
  }
  assert.deepEqual(renders, [0xffffff, 0xf7f9fc, 0xffffff, 0xf7f9fc]);
});

test('bounded failed-preview capture scales the white thumbnail and restores the live scene', () => {
  const scene = viewerModule.createPreviewScene();
  const liveBackground = scene.background;
  const renders = [];
  const draws = [];
  const thumbnail = {
    getContext(type) {
      assert.equal(type, '2d');
      return { drawImage(...args) { draws.push(args); } };
    },
    toDataURL(type) {
      assert.equal(type, 'image/png');
      assert.equal(scene.background.getHex(), 0xffffff);
      return 'data:image/png;base64,bounded';
    },
  };
  const canvas = { width: 1200, height: 600, ownerDocument: {
    createElement(tag) { assert.equal(tag, 'canvas'); return thumbnail; },
  } };
  const result = viewerModule.capturePreviewSnapshot({
    scene, camera: new PerspectiveCamera(), canvas, maximumDimension: 192,
    renderer: { render() { renders.push(scene.background.getHex()); } },
  });
  assert.equal(result, 'data:image/png;base64,bounded');
  assert.equal(thumbnail.width, 192);
  assert.equal(thumbnail.height, 96);
  assert.deepEqual(draws, [[canvas, 0, 0, 192, 96]]);
  assert.equal(scene.background, liveBackground);
  assert.deepEqual(renders, [0xffffff, 0xf7f9fc]);
});

test('bounded preview context failure still restores and redraws the live scene', () => {
  const scene = viewerModule.createPreviewScene();
  const liveBackground = scene.background;
  const renders = [];
  assert.throws(() => viewerModule.capturePreviewSnapshot({
    scene, camera: new PerspectiveCamera(), maximumDimension: 192,
    renderer: { render() { renders.push(scene.background.getHex()); } },
    canvas: { width: 200, height: 100, ownerDocument: {
      createElement() { return { getContext() { return null; } }; },
    } },
  }), /Thumbnail rendering is unavailable/);
  assert.equal(scene.background, liveBackground);
  assert.deepEqual(renders, [0xffffff, 0xf7f9fc]);
});

test('production capture restores and redraws the live background after render or encoding failure', () => {
  for (const phase of ['render', 'encode']) {
    const scene = viewerModule.createPreviewScene();
    const liveBackground = scene.background;
    const primary = new Error('synthetic capture failure');
    const renders = [];
    const renderer = {
      render() {
        renders.push(scene.background.getHex());
        if (phase === 'render' && renders.length === 1) throw primary;
      },
    };
    const canvas = { toDataURL() { throw primary; } };
    assert.throws(
      () => viewerModule.capturePreviewSnapshot({ scene, camera: new PerspectiveCamera(), renderer, canvas }),
      error => error === primary);
    assert.equal(scene.background, liveBackground);
    assert.deepEqual(renders, [0xffffff, 0xf7f9fc]);
  }
});

test('production capture retains both failures if the live redraw also fails', () => {
  const scene = viewerModule.createPreviewScene();
  const liveBackground = scene.background;
  const primary = new Error('synthetic capture failure');
  const redraw = new Error('synthetic redraw failure');
  let renders = 0;
  const renderer = { render() { renders += 1; if (renders === 2) throw redraw; } };
  assert.throws(
    () => viewerModule.capturePreviewSnapshot({
      scene, camera: new PerspectiveCamera(), renderer,
      canvas: { toDataURL() { throw primary; } },
    }),
    error => error instanceof AggregateError
      && error.errors.length === 2 && error.errors[0] === primary && error.errors[1] === redraw);
  assert.equal(scene.background, liveBackground);
  assert.equal(renders, 2);
});

test('a failed background-part capture restores the prior selected part and its camera', () => {
  const adapter = createAdapter();
  const primary = new Error('synthetic snapshot failure');
  adapter.snapshot = () => { throw primary; };
  const viewer = createModelViewer({ adapter });
  const first = disposableObject();
  const second = disposableObject();
  try {
    viewer.addPart('first', first);
    viewer.addPart('second', second);
    viewer.select('first');
    adapter.cameraState = { position: [3, 4, 5], target: [1, 2, 3] };
    assert.throws(() => viewer.snapshot('second'), error => error === primary);
    assert.deepEqual(adapter.cameraState, { position: [3, 4, 5], target: [1, 2, 3] });
    const changed = [];
    adapter.setColor = object => changed.push(object);
    assert.equal(viewer.setColor('#123456'), true);
    assert.deepEqual(changed, [first]);
  } finally {
    viewer.dispose();
  }
  assert.equal(first.disposeCount(), 1);
  assert.equal(second.disposeCount(), 1);
});

function createAdapter() {
  let cameraState = { position: [0, 0, 5], target: [0, 0, 0] };
  return {
    orbits: [], zooms: [], colors: [], bodyColors: [], resets: 0, fits: 0, fullscreens: 0,
    controlsDisposed: 0, rendererDisposed: 0, contextLost: 0, rafCancelled: 0, snapshots: 0,
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
    snapshot() { this.snapshots += 1; return 'data:image/png;base64,preview'; },
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

function cubeVertices() {
  return [
    0, 0, 0,
    1, 0, 0,
    1, 1, 0,
    0, 1, 0,
    0, 0, 1,
    1, 0, 1,
    1, 1, 1,
    0, 1, 1,
  ];
}

function cubeIndices() {
  return [
    0, 2, 1, 0, 3, 2,
    4, 5, 6, 4, 6, 7,
    0, 1, 5, 0, 5, 4,
    1, 2, 6, 1, 6, 5,
    2, 3, 7, 2, 7, 6,
    3, 0, 4, 3, 4, 7,
  ];
}

function cubeFaceRanges() {
  return [
    { first: 0, last: 1 },
    { first: 2, last: 3 },
    { first: 4, last: 5 },
    { first: 6, last: 7 },
    { first: 8, last: 9 },
    { first: 10, last: 11 },
  ];
}

function singleTriangleGeometry(offset) {
  const geometry = new BufferGeometry();
  geometry.setAttribute('position', new Float32BufferAttribute([
    offset, 0, 0, offset + 1, 0, 0, offset, 1, 0,
  ], 3));
  return geometry;
}

function fakeLargeMesh(triangleCount, onVertexRead, material = new MeshStandardMaterial()) {
  const position = {
    count: triangleCount * 3,
    getX() { onVertexRead(); throw new Error('aggregate topology walk exceeded cap'); },
    getY() { onVertexRead(); throw new Error('aggregate topology walk exceeded cap'); },
    getZ() { onVertexRead(); throw new Error('aggregate topology walk exceeded cap'); },
  };
  return {
    isMesh: true,
    material,
    geometry: {
      getAttribute: name => name === 'position' ? position : null,
      getIndex: () => null,
      dispose() {},
    },
  };
}

test('standalone additive STEP imports request finer tessellation and leave IGES and cancellation unchanged', async () => {
  const documentDescriptor = Object.getOwnPropertyDescriptor(globalThis, 'document');
  const factoryDescriptor = Object.getOwnPropertyDescriptor(globalThis, 'occtimportjs');
  const calls = [];
  const result = () => ({
    success: true,
    meshes: [{ attributes: { position: { array: cubeVertices() } }, index: { array: cubeIndices() } }],
  });
  const record = (format, bytes, parameters) => {
    calls.push({ format, bytes: Array.from(bytes), parameters });
    return result();
  };
  try {
    globalThis.document = {};
    globalThis.occtimportjs = async () => ({
      ReadStepFile: (bytes, parameters) => record('step', bytes, parameters),
      ReadIgesFile: (bytes, parameters) => record('iges', bytes, parameters),
    });
    // A separate module instance keeps this controlled API out of the production-loader cache.
    const viewer = await import('../wwwroot/src/app/js/instant-quotation/model-viewer.mjs?step-import-parameters');
    for (const extension of ['step', 'stp', 'igs', 'iges']) {
      const object = await viewer.loadStandaloneModel({
        name: 'controlled.' + extension,
        arrayBuffer: async () => Uint8Array.from([11, 22, 33]).buffer,
      });
      object.traverse(child => {
        child.geometry?.dispose();
        for (const material of [].concat(child.material ?? [])) material.dispose();
      });
    }
    assert.deepEqual(calls, [
      { format: 'step', bytes: [11, 22, 33], parameters: { linearDeflection: 0.02 } },
      { format: 'step', bytes: [11, 22, 33], parameters: { linearDeflection: 0.02 } },
      { format: 'iges', bytes: [11, 22, 33], parameters: null },
      { format: 'iges', bytes: [11, 22, 33], parameters: null },
    ]);

    const cancellation = new AbortController();
    cancellation.abort();
    await assert.rejects(viewer.loadStandaloneModel({
      name: 'cancelled.step',
      arrayBuffer: async () => Uint8Array.from([11, 22, 33]).buffer,
    }, { signal: cancellation.signal }), { name: 'AbortError' });
    assert.equal(calls.length, 4);
  } finally {
    if (documentDescriptor) Object.defineProperty(globalThis, 'document', documentDescriptor);
    else delete globalThis.document;
    if (factoryDescriptor) Object.defineProperty(globalThis, 'occtimportjs', factoryDescriptor);
    else delete globalThis.occtimportjs;
  }
});

test('promoting a local render key preserves its object, camera and selection without disposing it', () => {
  const adapter = createAdapter();
  const object = disposableObject();
  const viewer = createModelViewer({ adapter });
  try {
    viewer.addPart('preview-local', object);
    adapter.cameraState = { position: [3, 4, 5], target: [1, 1, 1] };
    const fits = adapter.fits;
    viewer.promotePart('preview-local', 'server-part');
    assert.equal(object.disposeCount(), 0);
    assert.equal(adapter.fits, fits);
    assert.deepEqual(adapter.cameraState, { position: [3, 4, 5], target: [1, 1, 1] });
    assert.throws(() => viewer.select('preview-local'), /Unknown/);
    viewer.select('server-part');
    assert.equal(adapter.fits, fits);
    viewer.remove('server-part');
    assert.equal(object.disposeCount(), 1);
  } finally { viewer.dispose(); }
});

test('local render promotion refuses a duplicate server key without losing either owned object', () => {
  const first = disposableObject();
  const second = disposableObject();
  const viewer = createModelViewer({ adapter: createAdapter() });
  try {
    viewer.addPart('preview-local', first);
    viewer.addPart('server-part', second);
    assert.throws(() => viewer.promotePart('preview-local', 'server-part'), /unique/);
    viewer.select('preview-local');
    viewer.select('server-part');
    assert.equal(first.disposeCount(), 0);
    assert.equal(second.disposeCount(), 0);
  } finally { viewer.dispose(); }
  assert.equal(first.disposeCount(), 1);
  assert.equal(second.disposeCount(), 1);
});
