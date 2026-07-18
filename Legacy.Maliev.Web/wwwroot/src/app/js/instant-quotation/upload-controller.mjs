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

    const upload = callAsPromise(() => options.transport(request));
    const preview = callAsPromise(() => options.preview(Object.freeze({
      partId: part.id,
      operationId,
      file: part.file,
      extension: part.extension,
      signal: abortController.signal,
    })));

    const completion = Promise.all([upload, preview]).then(([uploaded, previewed]) => {
      if (!isCurrent()) return snapshot(part);
      if (typeof uploaded?.uploadReference !== 'string' || uploaded.uploadReference.length === 0) {
        throw new TypeError('Upload transport returned no opaque reference.');
      }
      part.uploadReference = uploaded.uploadReference;
      part.previewObject = previewed?.object ?? null;
      part.advisoryGeometry = Object.freeze({
        ...(previewed?.geometry ?? {}),
        authoritative: false,
        isAuthoritative: false,
        authority: 'browser-advisory',
      });
      part.progress = 100;
      part.status = 'ready';
      return snapshot(part);
    }).catch(error => {
      if (!isCurrent()) return snapshot(part);
      part.error = error instanceof Error ? error.message : String(error);
      part.status = 'failed';
      return snapshot(part);
    });

    return Object.freeze({ id: part.id, operationId, completion });
  }

  function cancel(id) {
    const part = requirePart(id);
    part.abortController?.abort();
    part.generation += 1;
    part.status = 'cancelled';
    return snapshot(part);
  }

  function retry(id) {
    const part = requirePart(id);
    part.abortController?.abort();
    return start(part);
  }

  async function remove(id) {
    const part = requirePart(id);
    part.abortController?.abort();
    part.generation += 1;
    parts.delete(id);
    disposePreview(part.previewObject);
    if (part.uploadReference && typeof options.removeUpload === 'function') {
      await options.removeUpload(part.uploadReference);
    }
  }

  function get(id) {
    const part = parts.get(id);
    return part ? snapshot(part) : null;
  }

  function list() {
    return Object.freeze([...parts.values()].map(snapshot));
  }

  function dispose() {
    for (const part of parts.values()) {
      part.abortController?.abort();
      disposePreview(part.previewObject);
    }
    parts.clear();
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
  if (typeof object?.dispose === 'function') object.dispose();
}

function callAsPromise(callback) {
  try {
    return Promise.resolve(callback());
  } catch (error) {
    return Promise.reject(error);
  }
}
