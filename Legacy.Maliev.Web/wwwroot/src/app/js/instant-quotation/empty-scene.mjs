import * as THREE from 'three';

const SWEEP = 1.68, REST = 1.20, ATTACK = 0.26, DECAY = 0.39;
const ROWS = 16, GAP = 0.014, AMP = 0.80, VARY = 0.86, BASE = 0.60, BEVEL = 0.008;
const TINT = 0.12, CONTRAST = 1, SEAM = 0.60, FOV = 12;
const CLEAR_W = 0.08, CLEAR_H = 0.08, FEATHER = 0.12, VIEW_H = 10;
const CYCLE = SWEEP + ATTACK + DECAY + REST;

function smoothstep(a, b, x) {
  const t = Math.min(1, Math.max(0, (x - a) / (b - a)));
  return t * t * (3 - 2 * t);
}

export function emptySceneMask(x, y, viewWidth, viewHeight) {
  const distance = Math.hypot(x / (CLEAR_W * viewWidth / 2), y / (CLEAR_H * viewHeight / 2));
  return smoothstep(1, 1 + FEATHER * 1.6, distance);
}

export function emptySceneRelief(time, tile, boost = 0) {
  const local = ((time % CYCLE) + CYCLE) % CYCLE - tile.u * SWEEP;
  let pulse = 0;
  if (local >= 0 && local <= ATTACK + DECAY) {
    pulse = local < ATTACK
      ? smoothstep(0, ATTACK, local)
      : Math.pow(1 - (local - ATTACK) / DECAY, 1.7);
  }
  return pulse * (1 - VARY + VARY * tile.gain) * AMP * (1 + 0.30 * boost) * tile.mask
    + tile.rest * BASE;
}

/** Empty-state decoration only. Never receives files, preview models, or pricing callbacks. */
export function createEmptyScene(root, {
  windowObject = globalThis,
  documentObject = globalThis.document,
  createRenderer = canvas => new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true }),
} = {}) {
  if (!root || !documentObject) throw new TypeError('An empty-scene root and document are required.');
  const media = windowObject.matchMedia?.('(prefers-reduced-motion: reduce)');
  const label = root.closest('label');
  const layers = [];
  let disposed = false, pageHidden = false, frame = null, resizeTimer = null, boost = 0;
  const started = windowObject.performance.now();
  const matrix = new THREE.Matrix4(), quaternion = new THREE.Quaternion();
  const scaleVector = new THREE.Vector3(1, 1, 1), position = new THREE.Vector3(), color = new THREE.Color();
  root.dataset.cubeReady = 'false';
  root.dataset.hexReady = 'false';

  function dimensions(canvas) {
    const width = Math.round(canvas.clientWidth), height = Math.round(canvas.clientHeight);
    // Refuse pathological decorative allocations without changing the usable form.
    if (!(width > 0 && height > 0 && width <= 16384 && height <= 16384)) return null;
    return { width, height };
  }

  function own(layer, resource) { layer.resources.add(resource); return resource; }

  function releaseAll(actions) {
    let first;
    for (const action of actions) {
      try { action(); } catch (error) { first ??= error; }
    }
    if (first) throw first;
  }

  function releaseLayer(layer) {
    if (layer.released) return;
    layer.released = true;
    layer.active = false;
    root.dataset[layer.kind + 'Ready'] = 'false';
    const resources = [...layer.resources];
    layer.resources.clear();
    releaseAll([
      ...resources.map(resource => () => resource.dispose()),
      () => layer.renderer?.dispose(),
      () => layer.renderer?.forceContextLoss(),
      () => layer.scene.clear(),
    ]);
  }

  function disable(layer) {
    root.dataset[layer.kind + 'Ready'] = 'false';
    try { releaseLayer(layer); } catch { /* Decoration cannot fail the upload control. */ }
  }

  function sizeRenderer(layer, size) {
    layer.renderer.setPixelRatio(Math.min(Math.max(Number(windowObject.devicePixelRatio) || 1, 1), 2));
    layer.renderer.setSize(size.width, size.height, false);
  }

  function lights(scene, cube) {
    const key = new THREE.DirectionalLight(0xffffff, 0.72);
    key.position.set(...(cube ? [3, 6, 2.5] : [-4, 5, 6]));
    scene.add(key, new THREE.AmbientLight(0xffffff, 0.28),
      new THREE.HemisphereLight(0xffffff, 0xa9adb2, 0.38));
  }

  function sizeCube(layer) {
    const size = dimensions(layer.canvas);
    if (!size) return false;
    const aspect = size.width / size.height, d = 1.6;
    layer.camera.left = -d * aspect;
    layer.camera.right = d * aspect;
    layer.camera.top = d;
    layer.camera.bottom = -d;
    layer.camera.updateProjectionMatrix();
    sizeRenderer(layer, size);
    return true;
  }

  function buildHoneycomb(layer) {
    const size = dimensions(layer.canvas);
    if (!size) return false;
    const aspect = size.width / size.height, viewWidth = VIEW_H * aspect;
    const radius = (VIEW_H / ROWS) / Math.sqrt(3);
    const xPitch = 1.5 * radius, yPitch = Math.sqrt(3) * radius;
    const nx = Math.ceil((viewWidth * 0.62 + 2 * radius) / xPitch) + 1;
    const ny = Math.ceil((VIEW_H * 0.62 + 2 * radius) / yPitch) + 1;
    if ((2 * nx + 1) * (2 * ny + 1) > 8192) return false;
    if (layer.mesh) {
      layer.scene.remove(layer.mesh);
      const previousMesh = layer.mesh;
      layer.mesh = null;
      layer.resources.delete(previousMesh);
      previousMesh.dispose();
    }
    if (layer.geometry) {
      layer.resources.delete(layer.geometry);
      layer.geometry.dispose();
    }
    const inset = radius * (1 - GAP * 3), bevel = Math.max(0.0001, inset * BEVEL);
    const shape = new THREE.Shape();
    for (let index = 0; index < 6; index += 1) {
      const angle = Math.PI / 3 * index;
      if (index === 0) shape.moveTo((inset - bevel) * Math.cos(angle), (inset - bevel) * Math.sin(angle));
      else shape.lineTo((inset - bevel) * Math.cos(angle), (inset - bevel) * Math.sin(angle));
    }
    shape.closePath();
    layer.geometry = own(layer, new THREE.ExtrudeGeometry(shape, {
      depth: radius * 0.34, steps: 1, curveSegments: 1,
      bevelEnabled: true, bevelSegments: 2, bevelThickness: bevel, bevelSize: bevel,
    }));
    layer.geometry.center();
    const hash = index => { const value = Math.sin(index * 127.1 + 311.7) * 43758.5453; return value - Math.floor(value); };
    const tiles = [];
    let minX = Infinity, maxX = -Infinity;
    for (let column = -nx; column <= nx; column += 1) {
      for (let row = -ny; row <= ny; row += 1) {
        const x = column * xPitch;
        const y = row * yPitch + (Math.abs(column) % 2 ? yPitch / 2 : 0);
        if (Math.abs(x) > viewWidth / 2 + 1.5 * radius || Math.abs(y) > VIEW_H / 2 + 1.5 * radius) continue;
        const index = tiles.length;
        tiles.push({ x, y, gain: 0.45 + hash(index * 3.7) * 0.55,
          rest: (hash(index * 9.1) - 0.5) * 2, mask: emptySceneMask(x, y, viewWidth, VIEW_H) });
        minX = Math.min(minX, x);
        maxX = Math.max(maxX, x);
      }
    }
    if (!tiles.length) return false;
    for (const tile of tiles) tile.u = (tile.x - minX) / Math.max(0.001, maxX - minX);
    layer.tiles = tiles;
    layer.radius = radius;
    layer.mesh = own(layer, new THREE.InstancedMesh(layer.geometry, layer.material, tiles.length));
    layer.mesh.frustumCulled = false;
    layer.scene.add(layer.mesh);
    layer.back.scale.set(viewWidth * 2.2, VIEW_H * 2.2, 1);
    layer.back.position.z = -radius * 1.2;
    layer.camera.aspect = aspect;
    layer.camera.position.set(0, 0, (VIEW_H / 2) / Math.tan(THREE.MathUtils.degToRad(FOV) / 2));
    layer.camera.lookAt(0, 0, 0);
    layer.camera.updateProjectionMatrix();
    sizeRenderer(layer, size);
    return true;
  }

  function initialize(kind) {
    const layer = { kind, scene: new THREE.Scene(), resources: new Set(), active: true, released: false };
    layers.push(layer);
    try {
      layer.canvas = root.querySelector('[data-empty-' + kind + ']');
      if (!layer.canvas || !dimensions(layer.canvas)) throw new Error('Decoration has no visible canvas.');
      layer.renderer = createRenderer(layer.canvas);
      lights(layer.scene, kind === 'cube');
      if (kind === 'cube') {
        layer.camera = new THREE.OrthographicCamera(-1.6, 1.6, 1.6, -1.6, 0.1, 100);
        layer.camera.position.set(4, 4, 4);
        layer.camera.lookAt(0, 0, 0);
        layer.cube = new THREE.Mesh(own(layer, new THREE.BoxGeometry(1.3, 1.3, 1.3)),
          own(layer, new THREE.MeshLambertMaterial({ color: 0xa8adb2 })));
        layer.cube.position.y = 0.4;
        layer.scene.add(layer.cube);
        if (!sizeCube(layer)) throw new Error('Decoration has no visible canvas.');
      } else {
        layer.camera = new THREE.PerspectiveCamera(FOV, 1, 0.1, 200);
        layer.material = own(layer, new THREE.MeshLambertMaterial({ color: 0xffffff }));
        layer.backMaterial = own(layer, new THREE.MeshBasicMaterial({ color: 0xe6eaee }));
        layer.back = new THREE.Mesh(own(layer, new THREE.PlaneGeometry(1, 1)), layer.backMaterial);
        layer.scene.add(layer.back);
        if (!buildHoneycomb(layer)) throw new Error('Decoration exceeds its bounded scene.');
      }
      root.dataset[kind + 'Ready'] = 'true';
    } catch { disable(layer); }
  }

  function visible() {
    return !disposed && !pageHidden && !documentObject.hidden && root.isConnected
      && (!root.getClientRects || root.getClientRects().length > 0);
  }

  function draw(time, still) {
    const seconds = still ? CYCLE * 0.995 : (time - started) / 1000;
    const want = label?.matches(':hover') || label?.classList.contains('iq-drag-over') ? 1 : 0;
    boost += ((still ? 0 : want) - boost) * 0.08;
    for (const layer of layers) {
      if (!layer.active) continue;
      try {
        if (layer.kind === 'cube') {
          layer.cube.rotation.y = still ? 0 : time / 4200;
          layer.cube.rotation.x = still ? 0 : 0.18 + Math.sin(time / 5200) * 0.16;
          layer.cube.position.y = still ? 0.4 : 0.4 + Math.sin(time / 1700) * 0.12;
        } else {
          const amp = AMP * (1 + 0.30 * boost), tint = TINT * (1 + 1.20 * boost);
          const shadeScale = Math.max(0.0001, 2 * (amp + BASE) * layer.radius);
          for (let index = 0; index < layer.tiles.length; index += 1) {
            const tile = layer.tiles[index], z = emptySceneRelief(seconds, tile, boost) * layer.radius;
            position.set(tile.x, tile.y, z);
            matrix.compose(position, quaternion, scaleVector);
            layer.mesh.setMatrixAt(index, matrix);
            const n = Math.min(1, Math.max(0, 0.5 + z / shadeScale));
            const value = 1 - CONTRAST * 0.16 + CONTRAST * 0.16 * (n * 2 - 1) + CONTRAST * 0.10 * n;
            color.setRGB(value * (1 - tint * 0.10), value * (1 - tint * 0.04), value);
            layer.mesh.setColorAt(index, color);
          }
          layer.mesh.instanceMatrix.needsUpdate = true;
          if (layer.mesh.instanceColor) layer.mesh.instanceColor.needsUpdate = true;
          const seam = 1 - SEAM * 0.16;
          layer.backMaterial.color.setRGB(seam * (1 - tint * 0.12), seam * (1 - tint * 0.05), seam);
        }
        layer.renderer.render(layer.scene, layer.camera);
      } catch { disable(layer); }
    }
  }

  function stop() {
    if (frame !== null) windowObject.cancelAnimationFrame(frame);
    frame = null;
  }

  function animate(time) {
    frame = null;
    if (!visible()) { root.dataset.motion = 'paused'; return; }
    draw(time, false);
    if (layers.some(layer => layer.active)) frame = windowObject.requestAnimationFrame(animate);
    else root.dataset.motion = 'failed';
  }

  function refresh() {
    stop();
    if (!visible()) { root.dataset.motion = 'paused'; return; }
    const still = media?.matches === true;
    draw(windowObject.performance.now(), still);
    root.dataset.motion = layers.some(layer => layer.active) ? (still ? 'still' : 'running') : 'failed';
    if (!still && layers.some(layer => layer.active)) frame = windowObject.requestAnimationFrame(animate);
  }

  function resized() {
    if (resizeTimer !== null) windowObject.clearTimeout(resizeTimer);
    resizeTimer = windowObject.setTimeout(() => {
      resizeTimer = null;
      if (!visible()) return;
      for (const layer of layers) {
        if (!layer.active) continue;
        try {
          if (!(layer.kind === 'cube' ? sizeCube(layer) : buildHoneycomb(layer))) disable(layer);
        } catch { disable(layer); }
      }
      refresh();
    }, 150);
  }

  function pageHide() { pageHidden = true; stop(); root.dataset.motion = 'paused'; }
  function pageShow() { pageHidden = false; refresh(); }
  const observer = windowObject.ResizeObserver ? new windowObject.ResizeObserver(resized) : null;

  function dispose() {
    if (disposed) return;
    disposed = true;
    releaseAll([
      stop,
      () => { if (resizeTimer !== null) windowObject.clearTimeout(resizeTimer); resizeTimer = null; },
      () => observer?.disconnect(),
      () => documentObject.removeEventListener('visibilitychange', refresh),
      () => media?.removeEventListener('change', refresh),
      () => windowObject.removeEventListener('pagehide', pageHide),
      () => windowObject.removeEventListener('pageshow', pageShow),
      () => windowObject.removeEventListener('resize', resized),
      ...layers.map(layer => () => releaseLayer(layer)),
      () => { root.dataset.motion = 'disposed'; },
    ]);
  }

  try {
    initialize('cube');
    initialize('hex');
    documentObject.addEventListener('visibilitychange', refresh);
    media?.addEventListener('change', refresh);
    windowObject.addEventListener('pagehide', pageHide);
    windowObject.addEventListener('pageshow', pageShow);
    windowObject.addEventListener('resize', resized);
    observer?.observe(root);
    refresh();
    return Object.freeze({ dispose });
  } catch (primary) {
    try { dispose(); } catch { /* Preserve the original construction failure. */ }
    throw primary;
  }
}
