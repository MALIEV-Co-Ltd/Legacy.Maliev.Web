export const supportedPreviewExtensions = Object.freeze([
  'stl', 'obj', '3mf', 'glb', 'gltf', 'stp', 'step', 'igs', 'iges',
]);

const supportedExtensions = new Set(supportedPreviewExtensions);

export async function createInstantQuotationWorkflowInterop(dotNetStatusReporter = null) {
  const viewerModule = await import('/dist/instant-quotation-viewer.mjs');
  return createWorkflowPreviewInterop({
    loadModel: viewerModule.loadStandaloneModel,
    createViewer: viewerModule.createThreeModelViewer,
    reportStatus: () => dotNetStatusReporter?.invokeMethodAsync('ReportPreviewUnavailableAsync'),
  });
}

export function createWorkflowPreviewInterop({ loadModel, createViewer, reportStatus = () => {} }) {
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
  const guardedResources = new WeakSet();
  const viewerDisposedResources = new WeakSet();
  let viewer = null;
  let disposed = false;
  let availability = 'ready';
  let nextKey = 0;
  let inputElement = null;
  let inputChangeHandler = null;
  const selectionSnapshots = [];

  function bindInput(input) {
    assertActive();
    if (inputElement === input) return;
    unbindInput();
    inputElement = input;
    inputChangeHandler = () => selectionSnapshots.push(Array.from(input.files ?? []));
    inputElement?.addEventListener?.('change', inputChangeHandler, { capture: true });
  }

  function unbindInput() {
    inputElement?.removeEventListener?.('change', inputChangeHandler, { capture: true });
    inputElement = null;
    inputChangeHandler = null;
  }

  function beginSelection(input) {
    assertActive();
    const files = selectionSnapshots.shift() ?? Array.from(input?.files ?? []);
    return files.map(startPreview);
  }

  function discardSelection() {
    const files = selectionSnapshots.shift();
    if (files) files.length = 0;
  }

  function startPreview(file) {
    const key = `preview-${++nextKey}`;
    const controller = new AbortController();
    const entry = {
      key,
      file,
      controller,
      object: null,
      partId: null,
      released: false,
      quarantined: false,
      attached: false,
    };
    previews.set(key, entry);
    if (!supportedExtensions.has(extensionOf(file?.name))) {
      setUnavailable();
      return key;
    }

    Promise.resolve(loadModel(file, { signal: controller.signal }))
      .then(object => completePreview(entry, object))
      .catch(error => failPreview(entry, error));
    return key;
  }

  function completePreview(entry, object) {
    if (disposed || entry.released || entry.quarantined || previews.get(entry.key) !== entry) {
      disposeModel(object, disposedResources);
      return;
    }

    guardModelDisposal(object, guardedResources, viewerDisposedResources);
    entry.object = object;
    attachAdmittedPreview(entry);
  }

  function failPreview(entry, error) {
    if (entry.released || entry.quarantined || isCancellation(error)) return;
    setUnavailable();
  }

  function attach(canvas) {
    assertActive();
    if (!viewer) viewer = createViewer(canvas);
    for (const entry of previews.values()) attachAdmittedPreview(entry);
  }

  function admit(key, partId) {
    assertActive();
    const entry = requirePreview(key);
    if (entry.released || entry.quarantined || !partId) return false;
    entry.partId = partId;
    entry.file = null;
    partKeys.set(partId, key);
    attachAdmittedPreview(entry);
    return true;
  }

  function attachAdmittedPreview(entry) {
    if (!viewer || !entry.object || !entry.partId || entry.released || entry.quarantined || entry.attached) return;
    viewer.addPart(entry.partId, entry.object);
    entry.attached = true;
  }

  function quarantine(key) {
    const entry = previews.get(key);
    if (!entry || entry.released || entry.quarantined) return;
    entry.controller?.abort();
    if (entry.attached && viewer) viewer.remove(entry.partId);
    else if (entry.object) disposeModel(entry.object, disposedResources);
    entry.object = null;
    entry.attached = false;
    entry.quarantined = true;
  }

  function release(key) {
    const entry = previews.get(key);
    if (!entry) return;
    previews.delete(key);
    entry.released = true;
    entry.controller?.abort();
    if (entry.partId) partKeys.delete(entry.partId);
    if (entry.attached && viewer) viewer.remove(entry.partId);
    else if (entry.object) disposeModel(entry.object, disposedResources);
    entry.object = null;
    entry.file = null;
    entry.controller = null;
  }

  function retry(key) {
    assertActive();
    const previous = requirePreview(key);
    const file = previous.file;
    if (!file) throw new Error('The preview source is no longer available.');
    release(key);
    return startPreview(file);
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

  function setUnavailable() {
    if (availability === 'unavailable') return;
    availability = 'unavailable';
    try {
      Promise.resolve(reportStatus('unavailable')).catch(() => {});
    } catch {
      // Status reporting is advisory and cannot affect server authority.
    }
  }

  function dispose() {
    if (disposed) return;
    disposed = true;
    for (const key of [...previews.keys()]) release(key);
    for (const files of selectionSnapshots) files.length = 0;
    selectionSnapshots.length = 0;
    unbindInput();
    partKeys.clear();
    viewer?.dispose();
    viewer = null;
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
    bindInput,
    discardSelection,
    attach,
    admit,
    quarantine,
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

function guardModelDisposal(object, guarded, disposed) {
  object?.traverse?.(child => {
    guardResource(child.geometry, guarded, disposed);
    for (const material of Array.isArray(child.material) ? child.material : [child.material]) {
      if (!material) continue;
      for (const value of Object.values(material)) {
        if (value?.isTexture) guardResource(value, guarded, disposed);
      }
      guardResource(material, guarded, disposed);
    }
  });
}

function guardResource(resource, guarded, disposed) {
  if (!resource || guarded.has(resource) || typeof resource.dispose !== 'function') return;
  guarded.add(resource);
  const originalDispose = resource.dispose;
  try {
    resource.dispose = function disposeResourceOnce(...args) {
      if (disposed.has(resource)) return;
      disposed.add(resource);
      return originalDispose.apply(this, args);
    };
  } catch {
    // Standard Three.js resources are mutable; immutable test doubles stay untouched.
  }
}
