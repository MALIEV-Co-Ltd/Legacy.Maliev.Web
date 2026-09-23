import './wall-thickness-presentation.js';

const presentation = globalThis.MalievWallThicknessPresentation;
const maximumTriangles = 400_000;
const analysisTimeoutMs = 45_000;

export const policyForMaterial = material => presentation.policyForMaterial(material);
export const assessThickness = (evidence, material) => evidence
  ? presentation.assess(
      evidence.summary,
      evidence.sampleThicknessMm,
      evidence.sampleAreaMm2,
      policyForMaterial(material))
  : null;

export function thicknessMeshes(object) {
  const sources = [];
  let triangles = 0;
  let vertices = 0;
  object?.updateWorldMatrix?.(true, true);
  object?.traverse?.(child => {
    const position = child?.geometry?.getAttribute?.('position');
    if (!child?.isMesh || !position) return;
    const index = child.geometry.getIndex?.();
    if (position.itemSize < 3 || !Number.isSafeInteger(position.count)
        || (index && !Number.isSafeInteger(index.count))) {
      triangles = maximumTriangles + 1;
      return;
    }
    triangles += Math.floor((index?.count ?? position.count) / 3);
    vertices += position.count;
    sources.push({ child, position, index });
  });
  if (triangles > maximumTriangles || vertices > maximumTriangles * 3) return null;
  return sources.map(({ child, position, index }) => {
    // Accessors honor interleaved stride, offset, and normalized input attributes.
    const coordinates = new Float32Array(position.count * 3);
    for (let vertex = 0; vertex < position.count; vertex += 1) {
      coordinates[vertex * 3] = position.getX(vertex);
      coordinates[vertex * 3 + 1] = position.getY(vertex);
      coordinates[vertex * 3 + 2] = position.getZ(vertex);
    }
    const indices = index ? new Uint32Array(index.count) : null;
    for (let vertex = 0; vertex < (index?.count ?? 0); vertex += 1) {
      indices[vertex] = index.getX(vertex);
    }
    return {
      position: coordinates,
      index: indices,
      matrix: new Float32Array(child.matrixWorld.elements),
    };
  });
}

// One disposable worker at a time bounds browser CPU and memory for multi-file uploads.
export function createWallThicknessAnalyzer({
  workerFactory = () => new Worker('/src/app/js/instant-quotation/wall-thickness-runner.worker.js?v=2'),
  timeoutMs = analysisTimeoutMs,
} = {}) {
  const pending = [];
  let active = null;
  let disposed = false;

  function analyze(object, signal) {
    if (disposed || signal?.aborted) return Promise.resolve(null);
    return new Promise(resolve => {
      const job = { object, signal, resolve, worker: null, timer: null, onAbort: null, completed: false };
      job.onAbort = () => {
        const index = pending.indexOf(job);
        if (index >= 0) pending.splice(index, 1);
        if (active === job) finish(job, null);
        else resolve(null);
      };
      signal?.addEventListener?.('abort', job.onAbort, { once: true });
      pending.push(job);
      pump();
    });
  }

  function pump() {
    if (active || disposed) return;
    while (pending.length) {
      const job = pending.shift();
      if (job.signal?.aborted) { finish(job, null); continue; }
      active = job;
      try {
        const meshes = thicknessMeshes(job.object);
        if (!meshes?.length) { finish(job, null); return; }
        job.worker = workerFactory();
        job.worker.onmessage = event => finish(job, event.data?.evidence ?? null);
        job.worker.onerror = () => finish(job, null);
        job.timer = setTimeout(() => finish(job, null), timeoutMs);
        const transfer = meshes.flatMap(mesh => [
          mesh.position.buffer,
          mesh.matrix.buffer,
          ...(mesh.index ? [mesh.index.buffer] : []),
        ]);
        job.worker.postMessage({ meshes }, transfer);
      } catch {
        finish(job, null);
      }
      return;
    }
  }

  function finish(job, evidence) {
    if (job.completed) return;
    job.completed = true;
    clearTimeout(job.timer);
    job.signal?.removeEventListener?.('abort', job.onAbort);
    job.worker?.terminate?.();
    job.resolve(evidence);
    if (active === job) active = null;
    pump();
  }

  function dispose() {
    disposed = true;
    if (active) finish(active, null);
    for (const job of pending.splice(0)) finish(job, null);
  }

  return Object.freeze({ analyze, dispose });
}
