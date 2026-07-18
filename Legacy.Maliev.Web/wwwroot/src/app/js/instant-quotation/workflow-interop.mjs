export const supportedPreviewExtensions = Object.freeze([
  'stl', 'obj', '3mf', 'glb', 'gltf', 'stp', 'step', 'igs', 'iges',
]);

const supportedExtensions = new Set(supportedPreviewExtensions);

export async function createInstantQuotationWorkflowInterop() {
  const viewerModule = await import('/dist/instant-quotation-viewer.mjs');
  return createWorkflowPreviewInterop({
    loadModel: viewerModule.loadStandaloneModel,
    createViewer: viewerModule.createThreeModelViewer,
  });
}

export function createWorkflowPreviewInterop({ loadModel, createViewer }) {
  if (typeof loadModel !== 'function' || typeof createViewer !== 'function') {
    throw new TypeError('Preview loader and viewer factories are required.');
  }

  const previews = new Map();
  const partKeys = new Map();
  const disposedResources = {
    geometries: new WeakSet(),
    materials: new WeakSet(),
    textures: new WeakSet(),
  };
  let viewer = null;
  let disposed = false;
  let availability = 'ready';
  let nextKey = 0;
  let statusElement = null;
  let unavailableMessage = '';

  function beginSelection(input) {
    assertActive();
    const files = Array.from(input?.files ?? []);
    return files.map(startPreview);
  }

  function startPreview(file) {
    const extension = extensionOf(file?.name);
    const key = `preview-${++nextKey}`;
    const controller = new AbortController();
    const entry = {
      key,
      file,
      controller,
      object: null,
      partId: null,
      released: false,
      attached: false,
    };
    previews.set(key, entry);
    if (!supportedExtensions.has(extension)) {
      availability = 'unavailable';
      return key;
    }

    Promise.resolve(loadModel(file, { signal: controller.signal }))
      .then(object => completePreview(entry, object))
      .catch(error => failPreview(entry, error));
    return key;
  }

  function completePreview(entry, object) {
    if (disposed || entry.released || previews.get(entry.key) !== entry) {
      disposeModel(object, disposedResources);
      return;
    }

    entry.object = object;
    attachAdmittedPreview(entry);
  }

  function failPreview(entry, error) {
    if (entry.released || isCancellation(error)) return;
    availability = 'unavailable';
    renderAvailability();
  }

  function attach(canvas, nextStatusElement = null, nextUnavailableMessage = '') {
    assertActive();
    statusElement = nextStatusElement;
    unavailableMessage = String(nextUnavailableMessage ?? '');
    if (!viewer) viewer = createViewer(canvas);
    for (const entry of previews.values()) attachAdmittedPreview(entry);
    renderAvailability();
  }

  function admit(key, partId) {
    assertActive();
    const entry = requirePreview(key);
    if (entry.released || !partId) return false;
    entry.partId = partId;
    partKeys.set(partId, key);
    attachAdmittedPreview(entry);
    return true;
  }

  function attachAdmittedPreview(entry) {
    if (!viewer || !entry.object || !entry.partId || entry.released || entry.attached) return;
    viewer.addPart(entry.partId, entry.object);
    entry.attached = true;
  }

  function release(key) {
    const entry = previews.get(key);
    if (!entry || entry.released) return;
    entry.released = true;
    entry.controller.abort();
    if (entry.partId) partKeys.delete(entry.partId);
    if (entry.attached && viewer) viewer.remove(entry.partId);
    else if (entry.object) disposeModel(entry.object, disposedResources);
    entry.object = null;
  }

  function retry(key) {
    assertActive();
    const previous = requirePreview(key);
    release(key);
    return startPreview(previous.file);
  }

  function select(partId) {
    assertActive();
    if (!partKeys.has(partId) || !viewer) return false;
    viewer.select(partId);
    return true;
  }

  function remove(partId) {
    const key = partKeys.get(partId);
    if (key) release(key);
  }

  function reset() { viewer?.reset(); }
  function fit() { viewer?.fit(); }
  function fullscreen() { return viewer?.fullscreen(); }
  function status() { return availability; }

  function renderAvailability() {
    if (availability === 'unavailable' && statusElement && unavailableMessage) {
      statusElement.textContent = unavailableMessage;
    }
  }

  function dispose() {
    if (disposed) return;
    disposed = true;
    for (const entry of previews.values()) {
      entry.controller.abort();
      if (!entry.attached && entry.object) disposeModel(entry.object, disposedResources);
      entry.object = null;
      entry.released = true;
    }
    previews.clear();
    partKeys.clear();
    viewer?.dispose();
    viewer = null;
    statusElement = null;
  }

  function assertActive() {
    if (disposed) throw new Error('Preview interop is disposed.');
  }

  function requirePreview(key) {
    const entry = previews.get(key);
    if (!entry) throw new RangeError('Unknown preview correlation.');
    return entry;
  }

  return Object.freeze({
    beginSelection,
    attach,
    admit,
    release,
    retry,
    select,
    remove,
    reset,
    fit,
    fullscreen,
    status,
    dispose,
  });
}

function extensionOf(name) {
  const value = String(name ?? '');
  const separator = value.lastIndexOf('.');
  return separator < 0 ? '' : value.slice(separator + 1).toLowerCase();
}

function isCancellation(error) {
  return error?.name === 'AbortError';
}

function disposeModel(object, disposedResources) {
  object?.traverse?.(child => {
    disposeOnce(child.geometry, disposedResources.geometries);
    for (const material of Array.isArray(child.material) ? child.material : [child.material]) {
      if (!material) continue;
      for (const value of Object.values(material)) {
        if (value?.isTexture) disposeOnce(value, disposedResources.textures);
      }
      disposeOnce(material, disposedResources.materials);
    }
  });
}

function disposeOnce(resource, disposed) {
  if (!resource || disposed.has(resource)) return;
  disposed.add(resource);
  resource.dispose?.();
}
