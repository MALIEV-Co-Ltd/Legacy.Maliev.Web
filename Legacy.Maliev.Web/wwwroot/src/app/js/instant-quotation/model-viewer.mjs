import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { OBJLoader } from 'three/addons/loaders/OBJLoader.js';
import { STLLoader } from 'three/addons/loaders/STLLoader.js';
import { ThreeMFLoader } from 'three/addons/loaders/3MFLoader.js';

const bodyPalette = Object.freeze([
  '#2563eb', '#e11d48', '#059669', '#d97706', '#7c3aed', '#0891b2', '#db2777', '#65a30d',
]);

export function stableBodyColor(index) {
  const safeIndex = Number.isSafeInteger(index) && index >= 0 ? index : 0;
  return bodyPalette[safeIndex % bodyPalette.length];
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
    const bodyCount = Number.isSafeInteger(metadata.bodyCount) && metadata.bodyCount > 0
      ? metadata.bodyCount
      : 1;
    parts.set(id, { object, bodyCount, camera: null });
    adapter.hide?.(object);
    if (bodyCount > 1) {
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

  return Object.freeze({ addPart, select, remove, setColor, reset, fit, fullscreen, handleKey, dispose });
}

/** Creates the production Three.js viewer. Call only inside the quotation island. */
export function createThreeModelViewer(canvas) {
  if (!(canvas instanceof HTMLCanvasElement)) throw new TypeError('A canvas is required.');
  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0xf4f7fb);
  const camera = new THREE.PerspectiveCamera(45, 1, 0.1, 100000);
  camera.position.set(80, 80, 80);
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: false });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
  const controls = new OrbitControls(camera, canvas);
  controls.enableDamping = true;
  scene.add(new THREE.HemisphereLight(0xffffff, 0x334155, 2));
  const keyLight = new THREE.DirectionalLight(0xffffff, 2.5);
  keyLight.position.set(1, 2, 3);
  scene.add(keyLight);
  let frame = 0;

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
    disposeControls() { controls.dispose(); },
    disposeRenderer() { renderer.dispose(); },
    loseContext() { renderer.forceContextLoss(); },
    cancelAnimation() { cancelAnimationFrame(frame); },
  };
  const viewer = createModelViewer({ adapter, eventTarget: canvas });

  function render() {
    const width = Math.max(1, canvas.clientWidth);
    const height = Math.max(1, canvas.clientHeight);
    if (canvas.width !== width || canvas.height !== height) {
      renderer.setSize(width, height, false);
      camera.aspect = width / height;
      camera.updateProjectionMatrix();
    }
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
    const result = await new GLTFLoader().parseAsync(input, '');
    return result.scene;
  }
  if (['stp', 'step', 'igs', 'iges'].includes(extension)) return loadCadModel(buffer, extension, signal);
  throw new TypeError('Unsupported standalone model file.');
}

async function loadCadModel(buffer, extension, signal) {
  const occt = await ensureOcct();
  if (signal?.aborted) throw new DOMException('Operation cancelled.', 'AbortError');
  const bytes = new Uint8Array(buffer);
  const result = extension === 'igs' || extension === 'iges'
    ? occt.ReadIgesFile(bytes, null)
    : occt.ReadStepFile(bytes, null);
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

let occtPromise;
function ensureOcct() {
  if (occtPromise) return occtPromise;
  occtPromise = new Promise((resolve, reject) => {
    const initialize = () => globalThis.occtimportjs({ locateFile: name => `/lib/occt/${name}` }).then(resolve, reject);
    if (typeof globalThis.occtimportjs === 'function') { initialize(); return; }
    const script = document.createElement('script');
    script.src = '/lib/occt/occt-import-js.js';
    script.async = true;
    script.addEventListener('load', initialize, { once: true });
    script.addEventListener('error', () => reject(new Error('CAD preview support failed to load.')), { once: true });
    document.head.append(script);
  });
  return occtPromise;
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
  if (typeof object?.traverse === 'function') {
    object.traverse(child => {
      child.geometry?.dispose?.();
      for (const material of asArray(child.material)) {
        for (const value of Object.values(material ?? {})) {
          if (value?.isTexture) value.dispose();
        }
        material?.dispose?.();
      }
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
