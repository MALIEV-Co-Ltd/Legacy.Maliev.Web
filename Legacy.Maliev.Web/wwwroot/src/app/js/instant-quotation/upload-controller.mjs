export const SUPPORTED_MODEL_EXTENSIONS = Object.freeze([
  'stl', 'obj', '3mf', 'glb', 'gltf', 'stp', 'step', 'igs', 'iges',
]);

const supported = new Set(SUPPORTED_MODEL_EXTENSIONS);

export function modelExtension(name) {
  const value = typeof name === 'string' ? name.trim() : '';
  const separator = value.lastIndexOf('.');
  const extension = separator > 0 && separator < value.length - 1
    ? value.slice(separator + 1).toLowerCase()
    : '';
  if (!supported.has(extension)) {
    throw new TypeError('Unsupported model file.');
  }
  return extension;
}

/**
 * Coordinates independent preview and same-origin-ready upload operations.
 * The caller owns the transport URL, antiforgery policy, and exact FileService
 * adapter. This module deliberately knows none of those wire details.
 */
export function createUploadController(options) {
  if (typeof options?.transport !== 'function' || typeof options?.preview !== 'function') {
    throw new TypeError('Upload transport and preview functions are required.');
  }

  const parts = new Map();
  const deferredCleanup = new Set();
  let nextPartId = 1;
  let nextOperationId = 1;

  function add(file) {
    const extension = modelExtension(file?.name);
    if (!Number.isFinite(file?.size) || file.size < 0 || typeof file.arrayBuffer !== 'function') {
      throw new TypeError('Malformed model file.');
    }
    const id = `part-${nextPartId++}`;
    const part = {
      id,
      file,
      extension,
      generation: 0,
      operationId: null,
      abortController: null,
      status: 'queued',
      progress: 0,
      uploadReference: null,
      advisoryGeometry: null,
      previewObject: null,
      error: null,
    };
    parts.set(id, part);
    return start(part);
  }

  function start(part) {
    const generation = ++part.generation;
    const operationId = `upload-${nextOperationId++}`;
    const abortController = new AbortController();
    Object.assign(part, {
      operationId,
      abortController,
      status: 'uploading',
      progress: 0,
      error: null,
    });

    const isCurrent = () => parts.get(part.id) === part
      && part.generation === generation
      && !abortController.signal.aborted;
    const request = Object.freeze({
      partId: part.id,
      operationId,
      file: part.file,
      extension: part.extension,
      signal: abortController.signal,
      onProgress(event) {
        if (!isCurrent()) return;
        const loaded = Number(event?.loaded);
        const total = Number(event?.total);
        if (Number.isFinite(loaded) && Number.isFinite(total) && total > 0) {
          part.progress = Math.max(0, Math.min(100, Math.round((loaded / total) * 100)));
        }
      },
    });

    const upload = callAsPromise(() => options.transport(request)).then(uploaded => {
      if (typeof uploaded?.uploadReference !== 'string' || uploaded.uploadReference.length === 0) {
        throw new TypeError('Upload transport returned no opaque reference.');
      }
      return uploaded;
    });
    const preview = callAsPromise(() => options.preview(Object.freeze({
      partId: part.id,
      operationId,
      file: part.file,
      extension: part.extension,
      signal: abortController.signal,
    })));

    const completion = Promise.allSettled([upload, preview]).then(async ([uploadResult, previewResult]) => {
      const resource = {
        uploadReference: uploadResult.status === 'fulfilled'
          ? uploadResult.value.uploadReference
          : null,
        previewObject: previewResult.status === 'fulfilled'
          ? previewResult.value?.object ?? null
          : null,
      };
      const operationError = uploadResult.status === 'rejected'
        ? uploadResult.reason
        : previewResult.status === 'rejected'
          ? previewResult.reason
          : null;

      if (operationError || !isCurrent()) {
        try {
          await cleanupResource(resource);
        } catch (cleanupError) {
          if (isCurrent()) {
            assignResource(part, resource);
            part.status = 'cleanup-failed';
            part.error = errorMessage(cleanupError);
          } else {
            deferredCleanup.add(resource);
          }
          return snapshot(part);
        }
        if (isCurrent()) {
          part.error = errorMessage(operationError);
          part.status = 'failed';
        }
        return snapshot(part);
      }

      assignResource(part, resource);
      part.advisoryGeometry = Object.freeze({
        ...(previewResult.value?.geometry ?? {}),
        authoritative: false,
        isAuthoritative: false,
        authority: 'browser-advisory',
      });
      part.progress = 100;
      part.status = 'ready';
      return snapshot(part);
    });

    part.completion = completion;

    return Object.freeze({ id: part.id, operationId, completion });
  }

  function cancel(id) {
    const part = requirePart(id);
    part.abortController?.abort();
    part.generation += 1;
    part.status = 'cancelled';
    return snapshot(part);
  }

  async function retry(id) {
    const part = requirePart(id);
    part.abortController?.abort();
    part.generation += 1;
    if (part.uploadReference || part.previewObject) {
      part.status = 'cleaning';
      try {
        await cleanupAssignedResource(part);
      } catch (error) {
        part.status = 'cleanup-failed';
        part.error = errorMessage(error);
        throw error;
      }
    }
    return start(part);
  }

  async function remove(id) {
    const part = requirePart(id);
    part.abortController?.abort();
    part.generation += 1;
    part.status = 'removing';
    try {
      await cleanupAssignedResource(part);
    } catch (error) {
      part.status = 'cleanup-failed';
      part.error = errorMessage(error);
      throw error;
    }
    parts.delete(id);
  }

  function get(id) {
    const part = parts.get(id);
    return part ? snapshot(part) : null;
  }

  function list() {
    return Object.freeze([...parts.values()].map(snapshot));
  }

  async function dispose() {
    const failures = [];
    const candidates = [...parts.values()];
    for (const part of candidates) {
      part.abortController?.abort();
      part.generation += 1;
    }
    await Promise.allSettled(candidates.map(part => part.completion).filter(Boolean));
    for (const part of candidates) {
      try {
        await cleanupAssignedResource(part);
        parts.delete(part.id);
      } catch (error) {
        part.status = 'cleanup-failed';
        part.error = errorMessage(error);
        failures.push(Object.freeze({ partId: part.id, error: part.error }));
      }
    }
    for (const resource of [...deferredCleanup]) {
      try {
        await cleanupResource(resource);
        deferredCleanup.delete(resource);
      } catch (error) {
        failures.push(Object.freeze({ partId: null, error: errorMessage(error) }));
      }
    }
    return Object.freeze({ failures: Object.freeze(failures) });
  }

  async function cleanupAssignedResource(part) {
    const resource = {
      uploadReference: part.uploadReference,
      previewObject: part.previewObject,
    };
    await cleanupResource(resource);
    part.uploadReference = null;
    part.previewObject = null;
    part.advisoryGeometry = null;
  }

  async function cleanupResource(resource) {
    if (resource.uploadReference) {
      if (typeof options.removeUpload !== 'function') {
        throw new Error('Remote upload cleanup is unavailable.');
      }
      await options.removeUpload(resource.uploadReference);
    }
    disposePreview(resource.previewObject);
  }

  function assignResource(part, resource) {
    part.uploadReference = resource.uploadReference;
    part.previewObject = resource.previewObject;
  }

  function requirePart(id) {
    const part = parts.get(id);
    if (!part) throw new RangeError('Unknown upload part.');
    return part;
  }

  return Object.freeze({ add, cancel, retry, remove, get, list, dispose });
}

function snapshot(part) {
  return Object.freeze({
    id: part.id,
    name: part.file.name,
    size: part.file.size,
    extension: part.extension,
    operationId: part.operationId,
    status: part.status,
    progress: part.progress,
    uploadReference: part.uploadReference,
    advisoryGeometry: part.advisoryGeometry,
    error: part.error,
  });
}

function disposePreview(object) {
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
        if (!material || disposedMaterials.has(material)) continue;
        disposedMaterials.add(material);
        for (const value of Object.values(material ?? {})) {
          if (value?.isTexture && !disposedTextures.has(value)) {
            disposedTextures.add(value);
            value.dispose?.();
          }
        }
        material.dispose?.();
      }
    });
  }
  object?.dispose?.();
}

function callAsPromise(callback) {
  try {
    return Promise.resolve(callback());
  } catch (error) {
    return Promise.reject(error);
  }
}

function asArray(value) {
  if (!value) return [];
  return Array.isArray(value) ? value : [value];
}

function errorMessage(error) {
  if (error === null || error === undefined) return null;
  return error instanceof Error ? error.message : String(error);
}
