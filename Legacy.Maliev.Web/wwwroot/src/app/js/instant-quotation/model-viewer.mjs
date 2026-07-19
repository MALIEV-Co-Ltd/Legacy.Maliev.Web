import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { OBJLoader } from 'three/addons/loaders/OBJLoader.js';
import { STLLoader } from 'three/addons/loaders/STLLoader.js';
import { ThreeMFLoader } from 'three/addons/loaders/3MFLoader.js';

const bodyPalette = Object.freeze([
  '#2563eb', '#e11d48', '#059669', '#d97706', '#7c3aed', '#0891b2', '#db2777', '#65a30d',
]);
const maximumTopologyTriangles = 200000;
const fallbackMaterialOwnership = new WeakMap();

export function stableBodyColor(index) {
  const safeIndex = Number.isSafeInteger(index) && index >= 0 ? index : 0;
  return bodyPalette[safeIndex % bodyPalette.length];
}

/** Applies stable vertex colors to connected shells, including shells sharing one mesh. */
export function colorDisconnectedBodies(object) {
  const meshes = [];
  object?.traverse?.(child => {
    if (child.isMesh && child.geometry?.getAttribute?.('position')) meshes.push(child);
  });
  const aggregateTriangles = meshes.reduce(
    (sum, mesh) => sum + geometryTriangleCount(mesh.geometry), 0);
  const analyses = aggregateTriangles > maximumTopologyTriangles
    ? meshes.map(mesh => ({
        count: geometryTriangleCount(mesh.geometry) > 0 ? 1 : 0,
        componentByTriangle: [],
        vertexIndices: [],
        skipped: true,
      }))
    : meshes.map(mesh => analyzeMeshComponents(mesh.geometry));
  const bodyCount = analyses.reduce((sum, analysis) => sum + analysis.count, 0);
  if (bodyCount <= 1) return bodyCount;

  let bodyOffset = 0;
  analyses.forEach((analysis, meshIndex) => {
    if (analysis.skipped) applyBoundedMeshColor(meshes[meshIndex], bodyOffset);
    else applyComponentColors(meshes[meshIndex], analysis, bodyOffset);
    bodyOffset += analysis.count;
  });
  return bodyCount;
}

export function validateStandaloneGltfDocument(source) {
  let document;
  try {
    document = JSON.parse(source);
  } catch (error) {
    throw new TypeError(`Malformed GLTF JSON: ${errorMessage(error)}`);
  }
  for (const resource of [...(document.buffers ?? []), ...(document.images ?? [])]) {
    if (resource?.uri === undefined) continue;
    if (typeof resource.uri !== 'string' || !/^data:/i.test(resource.uri.trim())) {
      throw new TypeError('Standalone GLTF supports embedded data URIs only.');
    }
  }
  return document;
}

export function createCanvasResizeController({ renderer, camera, canvas, devicePixelRatio }) {
  let previousWidth = null;
  let previousHeight = null;
  let previousRatio = null;
  return Object.freeze({
    sync() {
      const width = Math.max(1, Math.round(canvas.clientWidth));
      const height = Math.max(1, Math.round(canvas.clientHeight));
      const ratio = Math.max(1, Math.min(Number(devicePixelRatio?.()) || 1, 2));
      if (width === previousWidth && height === previousHeight && ratio === previousRatio) return false;
      if (ratio !== previousRatio) renderer.setPixelRatio(ratio);
      renderer.setSize(width, height, false);
      camera.aspect = width / height;
      camera.updateProjectionMatrix();
      previousWidth = width;
      previousHeight = height;
      previousRatio = ratio;
      return true;
    },
  });
}

export function createOcctLoader({ documentObject, globalObject }) {
  let pending = null;
  return Object.freeze({
    load() {
      if (pending) return pending;
      const attempt = new Promise((resolve, reject) => {
        const initialize = () => {
          if (typeof globalObject.occtimportjs !== 'function') {
            reject(new Error('CAD preview support failed to initialize.'));
            return;
          }
          Promise.resolve(globalObject.occtimportjs({
            locateFile: name => `/lib/occt/${name}`,
          })).then(resolve, reject);
        };
        if (typeof globalObject.occtimportjs === 'function') { initialize(); return; }
        const script = documentObject.createElement('script');
        script.src = '/lib/occt/occt-import-js.js';
        script.async = true;
        script.addEventListener('load', initialize, { once: true });
        script.addEventListener('error', () => reject(new Error('CAD preview support failed to load.')), { once: true });
        documentObject.head.append(script);
      });
      pending = attempt.catch(error => {
        pending = null;
        throw error;
      });
      return pending;
    },
  });
}

/** Viewer state coordinator with an injected adapter for deterministic testing. */
export function createModelViewer({ adapter, eventTarget = null }) {
  if (!adapter) throw new TypeError('A viewer adapter is required.');
  const parts = new Map();
  let activeId = null;
  let disposed = false;
  const keyHandler = event => handleKey(event);
  eventTarget?.addEventListener('keydown', keyHandler);

  function addPart(id, object, metadata = {}) {
    assertActive();
    if (!id || !object || parts.has(id)) throw new TypeError('A unique part and object are required.');
    const declaredBodyCount = Number.isSafeInteger(metadata.bodyCount) && metadata.bodyCount > 0
      ? metadata.bodyCount
      : 1;
    const detectedBodyCount = Number(adapter.colorDisconnectedBodies?.(object)) || 0;
    const bodyCount = Math.max(declaredBodyCount, detectedBodyCount);
    parts.set(id, { object, bodyCount, camera: null });
    adapter.hide?.(object);
    if (bodyCount > 1 && detectedBodyCount <= 1) {
      adapter.setBodyColors?.(object, Array.from({ length: bodyCount }, (_, index) => stableBodyColor(index)));
    }
    if (activeId === null) select(id);
  }

  function select(id) {
    assertActive();
    const next = requirePart(id);
    if (activeId === id) return;
    if (activeId !== null) {
      const current = requirePart(activeId);
      current.camera = clone(adapter.getCameraState?.());
      adapter.hide?.(current.object);
    }
    activeId = id;
    adapter.show?.(next.object);
    if (next.camera) adapter.setCameraState?.(clone(next.camera));
    else adapter.fit?.(next.object);
  }

  function remove(id) {
    assertActive();
    const part = requirePart(id);
    const wasActive = activeId === id;
    parts.delete(id);
    adapter.hide?.(part.object);
    disposeObject(part.object);
    if (wasActive) {
      activeId = null;
      const replacement = parts.keys().next().value;
      if (replacement) select(replacement);
    }
  }

  function setColor(color) {
    assertActive();
    if (activeId === null) return false;
    const part = requirePart(activeId);
    if (part.bodyCount > 1) return false;
    adapter.setColor?.(part.object, color);
    return true;
  }

  function reset() {
    assertActive();
    adapter.reset?.(activeId === null ? null : requirePart(activeId).object);
  }

  function fit() {
    assertActive();
    adapter.fit?.(activeId === null ? null : requirePart(activeId).object);
  }

  function fullscreen() {
    assertActive();
    return adapter.fullscreen?.();
  }

  function snapshot(id) {
    assertActive();
    const previousId = activeId;
    select(id);
    const image = adapter.snapshot?.() ?? null;
    if (previousId !== null && previousId !== id) select(previousId);
    return image;
  }

  function handleKey(event) {
    if (disposed) return false;
    const actions = {
      ArrowLeft: () => adapter.orbit?.(-1, 0),
      ArrowRight: () => adapter.orbit?.(1, 0),
      ArrowUp: () => adapter.orbit?.(0, -1),
      ArrowDown: () => adapter.orbit?.(0, 1),
      '+': () => adapter.zoom?.(1),
      '=': () => adapter.zoom?.(1),
      '-': () => adapter.zoom?.(-1),
      '0': reset,
      Home: fit,
    };
    const action = actions[event?.key];
    if (!action) return false;
    event.preventDefault?.();
    action();
    return true;
  }

  function dispose() {
    if (disposed) return;
    eventTarget?.removeEventListener('keydown', keyHandler);
    for (const part of parts.values()) disposeObject(part.object);
    parts.clear();
    activeId = null;
    adapter.cancelAnimation?.();
    adapter.disposeControls?.();
    adapter.disposeRenderer?.();
    adapter.loseContext?.();
    disposed = true;
  }

  function assertActive() {
    if (disposed) throw new Error('Viewer is disposed.');
  }

  function requirePart(id) {
    const part = parts.get(id);
    if (!part) throw new RangeError('Unknown viewer part.');
    return part;
  }

  return Object.freeze({ addPart, select, remove, setColor, reset, fit, fullscreen, snapshot, handleKey, dispose });
}

/** Creates the production Three.js viewer. Call only inside the quotation island. */
export function createThreeModelViewer(canvas) {
  if (!(canvas instanceof HTMLCanvasElement)) throw new TypeError('A canvas is required.');
  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0xf4f7fb);
  const camera = new THREE.PerspectiveCamera(45, 1, 0.1, 100000);
  camera.position.set(80, 80, 80);
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: false });
  const controls = new OrbitControls(camera, canvas);
  controls.enableDamping = true;
  scene.add(new THREE.HemisphereLight(0xffffff, 0x334155, 2));
  const keyLight = new THREE.DirectionalLight(0xffffff, 2.5);
  keyLight.position.set(1, 2, 3);
  scene.add(keyLight);
  let frame = 0;
  const resize = createCanvasResizeController({
    renderer,
    camera,
    canvas,
    devicePixelRatio: () => window.devicePixelRatio,
  });

  const adapter = {
    show(object) { scene.add(object); object.visible = true; },
    hide(object) { object.visible = false; scene.remove(object); },
    getCameraState() {
      return { position: camera.position.toArray(), target: controls.target.toArray() };
    },
    setCameraState(state) {
      camera.position.fromArray(state.position);
      controls.target.fromArray(state.target);
      controls.update();
    },
    orbit(horizontal, vertical) {
      controls.rotateLeft(horizontal * 0.12);
      controls.rotateUp(vertical * 0.12);
      controls.update();
    },
    zoom(direction) {
      camera.position.lerp(controls.target, direction > 0 ? 0.12 : -0.12);
      controls.update();
    },
    reset(object) { frameObject(object, camera, controls); },
    fit(object) { frameObject(object, camera, controls); },
    fullscreen() { return canvas.requestFullscreen?.(); },
    snapshot() {
      resize.sync();
      controls.update();
      renderer.render(scene, camera);
      return canvas.toDataURL('image/png');
    },
    setColor(object, color) {
      object.traverse(child => {
        if (child.isMesh && child.material?.color) child.material.color.set(color);
      });
    },
    setBodyColors(object, colors) {
      let index = 0;
      object.traverse(child => {
        if (!child.isMesh) return;
        const color = colors[index % colors.length];
        for (const material of asArray(child.material)) material?.color?.set(color);
        index += 1;
      });
    },
    colorDisconnectedBodies,
    disposeControls() { controls.dispose(); },
    disposeRenderer() { renderer.dispose(); },
    loseContext() { renderer.forceContextLoss(); },
    cancelAnimation() { cancelAnimationFrame(frame); },
  };
  const viewer = createModelViewer({ adapter, eventTarget: canvas });

  function render() {
    resize.sync();
    controls.update();
    renderer.render(scene, camera);
    frame = requestAnimationFrame(render);
  }
  render();
  return viewer;
}

/** Loads one standalone model. GLTF companion files and OBJ MTL are not claimed. */
export async function loadStandaloneModel(file, { signal } = {}) {
  const extension = file.name.slice(file.name.lastIndexOf('.') + 1).toLowerCase();
  const buffer = await file.arrayBuffer();
  if (signal?.aborted) throw new DOMException('Operation cancelled.', 'AbortError');
  if (extension === 'stl') {
    const geometry = new STLLoader().parse(buffer);
    return new THREE.Mesh(geometry, defaultMaterial());
  }
  if (extension === 'obj') return new OBJLoader().parse(new TextDecoder().decode(buffer));
  if (extension === '3mf') return new ThreeMFLoader().parse(buffer);
  if (extension === 'glb' || extension === 'gltf') {
    const input = extension === 'gltf' ? new TextDecoder().decode(buffer) : buffer;
    if (extension === 'gltf') validateStandaloneGltfDocument(input);
    const result = await new GLTFLoader().parseAsync(input, '');
    return orientGltfForPrinting(result.scene);
  }
  if (['stp', 'step', 'igs', 'iges'].includes(extension)) return loadCadModel(buffer, extension, signal);
  throw new TypeError('Unsupported standalone model file.');
}

async function loadCadModel(buffer, extension, signal) {
  const occt = await ensureOcct();
  if (signal?.aborted) throw new DOMException('Operation cancelled.', 'AbortError');
  const bytes = new Uint8Array(buffer);
  const result = validateCadTessellation(extension === 'igs' || extension === 'iges'
    ? occt.ReadIgesFile(bytes, null)
    : occt.ReadStepFile(bytes, null));
  const group = new THREE.Group();
  for (const mesh of result.meshes ?? []) {
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.Float32BufferAttribute(mesh.attributes.position.array, 3));
    if (mesh.index?.array) geometry.setIndex(mesh.index.array);
    geometry.computeVertexNormals();
    group.add(new THREE.Mesh(geometry, defaultMaterial()));
  }
  return group;
}

export function orientGltfForPrinting(scene) {
  if (!scene?.rotation) throw new TypeError('Unable to read this glTF/GLB file.');
  scene.rotation.x = Math.PI / 2;
  return scene;
}

export function validateCadTessellation(result) {
  if (!result?.success) throw new TypeError('Unable to tessellate this CAD file.');
  return result;
}

let productionOcctLoader;
function ensureOcct() {
  productionOcctLoader ??= createOcctLoader({ documentObject: document, globalObject: globalThis });
  return productionOcctLoader.load();
}

function frameObject(object, camera, controls) {
  if (!object) return;
  const box = new THREE.Box3().setFromObject(object);
  if (box.isEmpty()) return;
  const center = box.getCenter(new THREE.Vector3());
  const size = box.getSize(new THREE.Vector3()).length();
  controls.target.copy(center);
  camera.position.copy(center).add(new THREE.Vector3(size, size * 0.8, size));
  camera.near = Math.max(size / 1000, 0.01);
  camera.far = Math.max(size * 100, 1000);
  camera.updateProjectionMatrix();
  controls.update();
}

function defaultMaterial() {
  return new THREE.MeshStandardMaterial({ color: 0x64748b, roughness: 0.64, metalness: 0.04 });
}

function disposeObject(object) {
  const disposedGeometries = new Set();
  const disposedMaterials = new Set();
  const disposedTextures = new Set();
  if (typeof object?.traverse === 'function') {
    object.traverse(child => {
      if (child.geometry && !disposedGeometries.has(child.geometry)) {
        disposedGeometries.add(child.geometry);
        child.geometry.dispose?.();
      }
      for (const material of asArray(child.material)) {
        disposeMaterial(material, disposedMaterials, disposedTextures);
      }
      const ownership = fallbackMaterialOwnership.get(child);
      for (const material of ownership?.originals ?? []) {
        disposeMaterial(material, disposedMaterials, disposedTextures);
      }
      fallbackMaterialOwnership.delete(child);
    });
  }
  object?.dispose?.();
}

function asArray(value) {
  if (!value) return [];
  return Array.isArray(value) ? value : [value];
}

function clone(value) {
  return value === undefined ? null : structuredClone(value);
}

function analyzeMeshComponents(geometry) {
  const position = geometry.getAttribute('position');
  const index = geometry.getIndex?.();
  const triangleCount = Math.floor((index?.count ?? position.count) / 3);
  if (triangleCount === 0) return { count: 0, componentByTriangle: [] };
  if (triangleCount > maximumTopologyTriangles) {
    return { count: 1, componentByTriangle: [], vertexIndices: [], skipped: true };
  }
  const parents = Array.from({ length: triangleCount }, (_, value) => value);
  const firstTriangleByVertex = new Map();
  const vertexIndices = [];

  for (let triangle = 0; triangle < triangleCount; triangle += 1) {
    const vertices = [];
    for (let corner = 0; corner < 3; corner += 1) {
      const vertexIndex = index ? index.getX(triangle * 3 + corner) : triangle * 3 + corner;
      vertices.push(vertexIndex);
      const key = `${quantize(position.getX(vertexIndex))},${quantize(position.getY(vertexIndex))},${quantize(position.getZ(vertexIndex))}`;
      const prior = firstTriangleByVertex.get(key);
      if (prior === undefined) firstTriangleByVertex.set(key, triangle);
      else union(parents, triangle, prior);
    }
    vertexIndices.push(vertices);
  }

  const componentIndex = new Map();
  const componentByTriangle = parents.map((_, triangle) => {
    const root = find(parents, triangle);
    if (!componentIndex.has(root)) componentIndex.set(root, componentIndex.size);
    return componentIndex.get(root);
  });
  return { count: componentIndex.size, componentByTriangle, vertexIndices, skipped: false };
}

function geometryTriangleCount(geometry) {
  const position = geometry.getAttribute('position');
  const index = geometry.getIndex?.();
  const count = Number(index?.count ?? position?.count ?? 0);
  return Number.isFinite(count) && count > 0 ? Math.floor(count / 3) : 0;
}

function applyComponentColors(mesh, analysis, bodyOffset) {
  const position = mesh.geometry.getAttribute('position');
  const colors = new Float32Array(position.count * 3);
  analysis.componentByTriangle.forEach((component, triangle) => {
    const color = new THREE.Color(stableBodyColor(bodyOffset + component));
    for (const vertexIndex of analysis.vertexIndices[triangle]) {
      colors[vertexIndex * 3] = color.r;
      colors[vertexIndex * 3 + 1] = color.g;
      colors[vertexIndex * 3 + 2] = color.b;
    }
  });
  mesh.geometry.setAttribute('color', new THREE.Float32BufferAttribute(colors, 3));
  for (const material of asArray(mesh.material)) {
    material.vertexColors = true;
    material.color?.set(0xffffff);
    material.needsUpdate = true;
  }
}

function applyBoundedMeshColor(mesh, bodyIndex) {
  let ownership = fallbackMaterialOwnership.get(mesh);
  if (!ownership) {
    const originals = asArray(mesh.material);
    const replacements = originals.map(material => material.clone());
    ownership = { originals, replacements };
    fallbackMaterialOwnership.set(mesh, ownership);
    mesh.material = replacements.length === 1 ? replacements[0] : replacements;
  }
  for (const material of ownership.replacements) {
    material.vertexColors = false;
    material.color?.set(stableBodyColor(bodyIndex));
    material.needsUpdate = true;
  }
}

function disposeMaterial(material, disposedMaterials, disposedTextures) {
  if (!material || disposedMaterials.has(material)) return;
  disposedMaterials.add(material);
  for (const value of Object.values(material)) {
    if (value?.isTexture && !disposedTextures.has(value)) {
      disposedTextures.add(value);
      value.dispose();
    }
  }
  material.dispose?.();
}

function find(parents, value) {
  let current = value;
  while (parents[current] !== current) {
    parents[current] = parents[parents[current]];
    current = parents[current];
  }
  return current;
}

function union(parents, left, right) {
  const leftRoot = find(parents, left);
  const rightRoot = find(parents, right);
  if (leftRoot !== rightRoot) parents[rightRoot] = leftRoot;
}

function quantize(value) {
  return Math.round(value * 1e6);
}

function errorMessage(error) {
  return error instanceof Error ? error.message : String(error);
}
