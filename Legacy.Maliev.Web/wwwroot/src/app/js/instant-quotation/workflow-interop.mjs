import { analyzeUploadDerivedGeometry } from './geometry-analysis.mjs';
import { assessThickness, createWallThicknessAnalyzer, policyForMaterial } from './wall-thickness.mjs';

export const supportedPreviewExtensions = Object.freeze([
  'stl', 'obj', '3mf', 'glb', 'gltf', 'stp', 'step', 'igs', 'iges',
]);

const previewColors = Object.freeze({
  Any: '#64748b',
  Natural: '#64748b',
  Clear: '#64748b',
  Black: '#111827',
  White: '#f8fafc',
  Gray: '#6b7280',
  Silver: '#c0c0c0',
  Red: '#dc2626',
  Orange: '#ea580c',
  Yellow: '#eab308',
  Green: '#16a34a',
  Blue: '#2563eb',
  Purple: '#9333ea',
  Pink: '#db2777',
});

export function previewColorForSelection(value) {
  const normalized = typeof value === 'string' ? value.trim() : '';
  return /^#[0-9a-f]{6}$/i.test(normalized)
    ? normalized.toLowerCase()
    : previewColors[normalized] ?? previewColors.Any;
}

const supportedExtensions = new Set(supportedPreviewExtensions);
const STALL_TIMEOUT_MS = 120_000;

// Advisory-only classification. Never relax the server's positive-volume claim.
export function classifyIncompletePreview(file, claim) {
  const dimensions = [claim?.dimensionXmm, claim?.dimensionYmm, claim?.dimensionZmm];
  return extensionOf(file?.name) === 'stl'
    && Number.isSafeInteger(file?.size) && file.size > 0 && file.size <= 8 * 1024 * 1024
    && dimensions.every(value => Number.isFinite(value) && value >= 0 && value <= 350)
    && dimensions.filter(value => value === 0).length === 1
    && claim.volumeMm3 === 0 && Number.isFinite(claim.surfaceAreaMm2) && claim.surfaceAreaMm2 > 0
    && Number.isSafeInteger(claim.facetCount) && claim.facetCount > 0 && claim.facetCount <= 50_000
    && claim.bodyCount === 1 && claim.topologyChecked === true
    && claim.nonWatertight === true && claim.nonManifold === false;
}

export function configureFilePickerForPlatform(input, navigatorLike = globalThis.navigator ?? {}) {
  if (!input) return false;
  const userAgent = String(navigatorLike.userAgent ?? '');
  const platform = String(navigatorLike.platform ?? '');
  const maxTouchPoints = Number(navigatorLike.maxTouchPoints ?? 0);
  const isIosDevice = /iPad|iPhone|iPod/.test(userAgent)
    || (platform === 'MacIntel' && maxTouchPoints > 1);
  if (!isIosDevice) return false;

  // iOS Files identifies STL using a UTI with no reliable HTML accept mapping.
  // Preserve the allowlist for diagnostics, let Files return the selection, and
  // continue to enforce supported extensions before parsing or upload begins.
  input.dataset.acceptedTypes = input.getAttribute?.('accept')
    || input.dataset.acceptedTypes
    || '';
  input.removeAttribute?.('accept');
  return true;
}

export async function createInstantQuotationWorkflowInterop(dotNetStatusReporter = null) {
  const viewerModule = await import('/dist/instant-quotation-viewer.mjs');
  return createWorkflowPreviewInterop({
    loadModel: viewerModule.loadStandaloneModel,
    analyzeGeometry: analyzeUploadDerivedGeometry,
    createViewer: viewerModule.createThreeModelViewer,
    reportStatus: () => dotNetStatusReporter?.invokeMethodAsync('ReportPreviewUnavailableAsync'),
    reportQuoteStarted: () => dotNetStatusReporter?.invokeMethodAsync('ReportQuoteStartedAsync'),
    reportThickness: (...args) => dotNetStatusReporter?.invokeMethodAsync('ReportThicknessStateAsync', ...args),
    reportIncomplete: (...args) => dotNetStatusReporter?.invokeMethodAsync('ReportIncompletePreviewAsync', ...args),
  });
}

export function createWorkflowPreviewInterop({
  loadModel,
  analyzeGeometry,
  createViewer,
  reportStatus = () => {},
  reportQuoteStarted = () => {},
  reportThickness = () => {},
  reportIncomplete = () => {},
  thicknessAnalyzer = createWallThicknessAnalyzer(),
}) {
  if (typeof loadModel !== 'function'
      || typeof analyzeGeometry !== 'function'
      || typeof createViewer !== 'function') {
    throw new TypeError('Preview loader, geometry analysis, and viewer factories are required.');
  }

  const previews = new Map();
  const partKeys = new Map();
  const thumbnails = new Map();
  let selectedPartId = null;
  let thumbnailCanvas = null;
  const invalidateCameraThumbnail = () => thumbnails.delete(selectedPartId);
  const disposedResources = {
    geometries: new WeakSet(),
    materials: new WeakSet(),
    textures: new WeakSet(),
  };
  const guardedResources = new WeakSet();
  const viewerDisposedResources = new WeakSet();
  let viewer = null;
  let incompleteEntry = null;
  let incompleteViewer = null;
  let activeLocalPreviewKey = null;
  let developmentViewer = null;
  let developmentCanvas = null;
  const developmentKeys = new Set();
  let disposed = false;
  let availability = 'ready';
  let nextKey = 0;
  let quoteStarted = false;
  let inputElement = null;
  let inputChangeHandler = null;
  let partsRail = null;
  let partsWheelHandler = null;
  let inputDropZone = null;
  let inputDropHandler = null;
  const selectionSnapshots = [];

  function bindInput(input) {
    assertActive();
    configureFilePickerForPlatform(input);
    bindPartsWheel(input?.closest?.('.instant-quote__workflow')?.querySelector?.('[data-workflow-parts]') ?? null);
    if (inputElement === input) return;
    unbindInput();
    inputElement = input;
    inputChangeHandler = () => selectionSnapshots.push({
      files: Array.from(input.files ?? []),
      unit: input.closest?.('.instant-quote__workflow')?.querySelector?.('[data-workflow-gltf-unit]')?.value ?? null,
    });
    inputDropZone = inputElement?.closest?.('.instant-quote__workflow') ?? null;
    inputDropHandler = event => {
      const transfer = event.dataTransfer;
      if (!transfer || !(Array.from(transfer.types ?? []).includes('Files') || transfer.files?.length)) return;
      event.preventDefault();
      if (event.type !== 'drop' || inputElement.disabled
          || inputDropZone?.getAttribute('aria-busy') === 'true' || !transfer.files?.length) return;
      inputElement.files = transfer.files;
      inputElement.dispatchEvent(new Event('change', { bubbles: true }));
    };
    try {
      inputElement?.addEventListener?.('change', inputChangeHandler, { capture: true });
      inputDropZone?.addEventListener('dragover', inputDropHandler);
      inputDropZone?.addEventListener('drop', inputDropHandler);
    } catch (primary) {
      unbindInput();
      throw primary;
    }
  }

  function unbindInput() {
    inputElement?.removeEventListener?.('change', inputChangeHandler, { capture: true });
    inputDropZone?.removeEventListener('dragover', inputDropHandler);
    inputDropZone?.removeEventListener('drop', inputDropHandler);
    inputElement = null;
    inputChangeHandler = null;
    inputDropZone = null;
    inputDropHandler = null;
  }

  function bindPartsWheel(rail) {
    if (partsRail === rail) return;
    unbindPartsWheel();
    partsRail = rail;
    if (!rail) return;
    partsWheelHandler = event => {
      if (disposed || partsRail !== rail || event.defaultPrevented || event.cancelable === false
          || event.ctrlKey || event.metaKey || event.altKey || event.shiftKey) return;
      if (event.target?.closest?.('input, select, textarea, [contenteditable], [role="slider"], [role="spinbutton"]')) return;
      const selection = globalThis.getSelection?.();
      if (selection && !selection.isCollapsed && rail.contains(selection.anchorNode)) return;
      // Decide per gesture: the narrow wrapped layout and any vertical overflow stay native.
      if (rail.scrollHeight > rail.clientHeight || rail.scrollWidth <= rail.clientWidth) return;
      let delta = event.deltaY || event.deltaX;
      if (!Number.isFinite(delta) || !delta) return;
      if (event.deltaMode === 1) delta *= 16;
      else if (event.deltaMode === 2) delta *= rail.clientWidth;
      const before = rail.scrollLeft;
      const target = Math.max(0, Math.min(rail.scrollWidth - rail.clientWidth, before + delta));
      if (target === before) return;
      rail.scrollLeft = target;
      // A pinned or removed rail must not swallow a gesture it did not actually consume.
      if (rail.scrollLeft !== before) event.preventDefault();
    };
    rail.addEventListener('wheel', partsWheelHandler, { passive: false });
  }

  function unbindPartsWheel() {
    partsRail?.removeEventListener('wheel', partsWheelHandler);
    partsRail = null;
    partsWheelHandler = null;
  }

  function beginSelection(input, maximumFileSize = Number.POSITIVE_INFINITY, selectedUnit = null) {
    assertActive();
    const selection = selectionSnapshots.shift() ?? {
      files: Array.from(input?.files ?? []),
      unit: selectedUnit ?? input?.closest?.('.instant-quote__workflow')?.querySelector?.('[data-workflow-gltf-unit]')?.value ?? null,
    };
    return selection.files.filter(file => !Number.isFinite(maximumFileSize) || file.size <= maximumFileSize)
      .map(file => startPreview(file, selection.unit));
  }

  function discardSelection() {
    const selection = selectionSnapshots.shift();
    if (selection) selection.files.length = 0;
  }

  function startPreview(file, selectedUnit) {
    const key = `preview-${++nextKey}`;
    const controller = new AbortController();
    const entry = {
      key,
      file,
      selectedUnit,
      gltfUnitPolicy: Object.freeze(['glb', 'gltf'].includes(extensionOf(file?.name))
        ? { version: 1, unit: selectedUnit === 'metres' ? 1 : 0 }
        : { version: 0, unit: 0 }),
      controller,
      object: null,
      loadedObject: null,
      settleCompletion: null,
      geometryClaim: null,
      failureCategory: null,
      completion: null,
      partId: null,
      released: false,
      quarantined: false,
      attached: false,
      thicknessEvidence: null,
      evidenceCompleted: false,
      incompleteRevision: 0,
      previewOnly: false,
      thicknessMaterial: '',
      thicknessVisible: false,
      thicknessRevision: 0,
      stallTimer: null,
    };
    previews.set(key, entry);
    if (!supportedExtensions.has(extensionOf(file?.name))) {
      entry.failureCategory = 'unreadable';
      setUnavailable();
      return key;
    }

    if (['glb', 'gltf'].includes(extensionOf(file?.name))
        && selectedUnit !== 'millimetres' && selectedUnit !== 'metres') {
      entry.failureCategory = 'unit_required';
      entry.completion = Promise.resolve(null);
      setUnavailable();
      return key;
    }

    if (!quoteStarted) {
      quoteStarted = true;
      try {
        Promise.resolve(reportQuoteStarted()).catch(() => {});
      } catch {
        // Intent measurement must not block model parsing or upload.
      }
    }

    const operation = Promise.all([
      Promise.resolve().then(() => loadModel(file, { signal: controller.signal, gltfUnitPolicy: entry.gltfUnitPolicy })).then(object => {
        if (isCurrent(entry)) entry.loadedObject = object;
        else disposeModel(object, disposedResources);
        return object;
      }),
      sha256(file, controller.signal),
    ]).then(([object, hash]) => {
      if (!isCurrent(entry)) {
        disposeModel(object, disposedResources);
        return null;
      }
      try {
        entry.geometryClaim = Object.freeze({
          ...analyzeGeometry(object),
          sha256: hash,
          gltfUnitPolicy: entry.gltfUnitPolicy,
        });
        entry.previewOnly = classifyIncompletePreview(file, entry.geometryClaim);
      } catch (error) {
        disposeModel(object, disposedResources);
        failPreview(entry, error);
        return null;
      }
      completePreview(entry, object);
      // Zero-volume planar inputs outside the exceptional budget/shape contract
      // remain rejected. Do not send them to the larger normal-part worker budget.
      if (!entry.previewOnly && entry.geometryClaim.volumeMm3 === 0
          && [entry.geometryClaim.dimensionXmm, entry.geometryClaim.dimensionYmm,
            entry.geometryClaim.dimensionZmm].some(value => value === 0)) return entry.geometryClaim;
      Promise.resolve(thicknessAnalyzer.analyze(object, controller.signal)).then(evidence => {
        if (!isCurrent(entry)) return;
        entry.thicknessEvidence = evidence;
        entry.evidenceCompleted = true;
        if (entry.attached && evidence) {
          if (entry.thicknessVisible) thumbnails.delete(entry.partId);
          viewer?.setThicknessEvidence?.(entry.partId ?? entry.key, evidence);
        }
        notifyThickness(entry);
        notifyIncomplete(entry);
      }).catch(() => {
        if (isCurrent(entry)) {
          entry.evidenceCompleted = true;
          notifyThickness(entry);
          notifyIncomplete(entry);
        }
      });
      return entry.geometryClaim;
    }).catch(error => {
      failPreview(entry, error);
      entry.controller?.abort();
      disposeModel(entry.loadedObject, disposedResources);
      entry.loadedObject = null;
      return null;
    });
    entry.completion = new Promise((resolve, reject) => {
      let completed = false;
      entry.settleCompletion = (value, error = null) => {
        if (completed) return;
        completed = true;
        clearTimeout(entry.stallTimer);
        entry.stallTimer = null;
        entry.settleCompletion = null;
        if (error) reject(error);
        else resolve(value);
      };
      entry.stallTimer = setTimeout(() => {
        if (!isCurrent(entry)) return;
        const error = new Error('Preview processing timed out.');
        failPreview(entry, error);
        entry.controller.abort();
        disposeModel(entry.loadedObject, disposedResources);
        entry.loadedObject = null;
        entry.settleCompletion?.(null, error);
      }, STALL_TIMEOUT_MS);
      operation.then(value => entry.settleCompletion?.(value),
        error => entry.settleCompletion?.(null, error));
    });
    return key;
  }

  async function getGeometryClaim(key) {
    assertActive();
    const entry = requirePreview(key);
    const claim = await entry.completion;
    if (!claim || !isCurrent(entry)) {
      throw new Error('Geometry analysis is unavailable.');
    }
    return claim;
  }

  function isCurrent(entry) {
    return !disposed
      && !entry.released
      && !entry.quarantined
      && previews.get(entry.key) === entry
      && !entry.controller?.signal.aborted;
  }

  function completePreview(entry, object) {
    if (disposed || entry.released || entry.quarantined || previews.get(entry.key) !== entry) {
      disposeModel(object, disposedResources);
      return;
    }

    guardModelDisposal(object, guardedResources, viewerDisposedResources);
    entry.object = object;
    entry.loadedObject = null;
    attachAdmittedPreview(entry);
    if (activeLocalPreviewKey === entry.key) attachLocalPreview(entry);
  }

  function failPreview(entry, error) {
    if (!isCurrent(entry) || isCancellation(error)) return;
    const message = String(error?.message ?? error ?? '');
    // Advisory categories never carry decoder text or authorize an upload/price.
    entry.failureCategory = /^(?:geometry_complexity_exceeded|triangle_budget_exceeded|layer_budget_exceeded|simulation_timeout)\b/.test(message)
      || /^(?:Preview processing timed out\.|This file is too complex to process\.)/.test(message)
      ? 'complexity'
      : /^(?:Unable to load |CAD preview support failed|worker_unavailable\b|Failed to fetch dynamically imported module|Loading chunk |WebGL)/.test(message)
        || ['NetworkError', 'NotReadableError', 'SecurityError', 'OperationError'].includes(error?.name)
        ? 'unavailable' : 'unreadable';
    setUnavailable();
  }

  function getFailureCategory(key) {
    return previews.get(key)?.failureCategory ?? null;
  }

  function attach(canvas) {
    assertActive();
    if (!viewer) {
      unbindThumbnailCanvas();
      viewer = createViewer(canvas);
      thumbnailCanvas = canvas;
      for (const type of ['pointerup', 'wheel', 'keydown']) canvas?.addEventListener?.(type, invalidateCameraThumbnail);
    }
    for (const entry of previews.values()) attachAdmittedPreview(entry);
    if (activeLocalPreviewKey) selectLocalPreview(activeLocalPreviewKey);
  }

  function detachEmptyViewer() {
    if (disposed || partKeys.size !== 0 || [...previews.values()].some(entry => entry.attached)) return false;
    unbindThumbnailCanvas();
    viewer?.dispose();
    viewer = null;
    return true;
  }

  function admit(key, partId, material = '') {
    assertActive();
    const entry = requirePreview(key);
    if (entry.released || entry.quarantined || entry.previewOnly || developmentKeys.has(key) || !partId) return false;
    if (entry.attached && !entry.partId) viewer.promotePart(entry.key, partId);
    entry.partId = partId;
    if (activeLocalPreviewKey === key) activeLocalPreviewKey = null;
    thumbnails.delete(partId);
    selectedPartId ??= partId;
    entry.thicknessMaterial = material;
    entry.file = null;
    partKeys.set(partId, key);
    attachAdmittedPreview(entry);
    notifyThickness(entry);
    return true;
  }

  function attachAdmittedPreview(entry) {
    if (!viewer || !entry.object || !entry.partId || entry.released || entry.quarantined || entry.attached) return;
    viewer.addPart(entry.partId, entry.object);
    entry.attached = true;
    if (entry.thicknessEvidence) viewer.setThicknessEvidence?.(entry.partId, entry.thicknessEvidence);
    if (activeLocalPreviewKey && !previews.get(activeLocalPreviewKey)?.attached)
      selectLocalPreview(activeLocalPreviewKey);
  }

  function quarantine(key) {
    const entry = previews.get(key);
    if (!entry || entry.released || entry.quarantined) return;
    thumbnails.delete(entry.partId);
    if (developmentKeys.has(key)) {
      release(key);
      return;
    }
    if (incompleteEntry === entry) {
      release(key);
      return;
    }
    if (activeLocalPreviewKey === key) activeLocalPreviewKey = null;
    entry.settleCompletion?.(null);
    entry.controller?.abort();
    clearTimeout(entry.stallTimer);
    entry.stallTimer = null;
    if (entry.attached && viewer) viewer.remove(entry.partId ?? entry.key);
    else disposeModel(entry.object ?? entry.loadedObject, disposedResources);
    entry.loadedObject = null;
    entry.object = null;
    entry.geometryClaim = null;
    entry.attached = false;
    entry.quarantined = true;
    if (activeLocalPreviewKey) selectLocalPreview(activeLocalPreviewKey);
  }

  function release(key) {
    const entry = previews.get(key);
    if (!entry) return;
    if (activeLocalPreviewKey === key) activeLocalPreviewKey = null;
    thumbnails.delete(entry.partId);
    if (selectedPartId === entry.partId) selectedPartId = null;
    const failures = [];
    const attempt = action => { try { action(); } catch (error) { failures.push(error); } };
    // Drop ownership before calling external disposers, and attempt each owned release.
    previews.delete(key);
    entry.released = true;
    const developmentOwned = developmentKeys.has(key);
    let developmentDetached = false;
    attempt(() => {
      detachDevelopmentPreview(key);
      developmentDetached = developmentOwned;
    });
    if (incompleteEntry === entry) {
      incompleteEntry = null;
      const ownedViewer = incompleteViewer;
      incompleteViewer = null;
      attempt(() => ownedViewer?.dispose());
    }
    attempt(() => entry.settleCompletion?.(null));
    clearTimeout(entry.stallTimer);
    entry.stallTimer = null;
    attempt(() => entry.controller?.abort());
    if (entry.partId) partKeys.delete(entry.partId);
    if (entry.attached && viewer) attempt(() => viewer.remove(entry.partId ?? entry.key));
    else if (!developmentDetached) attempt(() => disposeModel(entry.object ?? entry.loadedObject, disposedResources));
    entry.loadedObject = null;
    entry.object = null;
    entry.geometryClaim = null;
    entry.file = null;
    entry.controller = null;
    if (activeLocalPreviewKey) attempt(() => selectLocalPreview(activeLocalPreviewKey));
    if (failures.length === 1) throw failures[0];
    if (failures.length > 1) throw new AggregateError(failures, 'Preview release failed.');
  }

  function retry(key) {
    assertActive();
    const previous = requirePreview(key);
    const file = previous.file;
    const selectedUnit = previous.selectedUnit;
    if (!file) throw new Error('The preview source is no longer available.');
    release(key);
    return startPreview(file, selectedUnit);
  }

  function select(partId) {
    assertActive();
    if (!partKeys.has(partId) || !viewer) return false;
    activeLocalPreviewKey = null;
    viewer.select(partId);
    selectedPartId = partId;
    const entry = previews.get(partKeys.get(partId));
    if (entry) notifyThickness(entry);
    return true;
  }

  function attachLocalPreview(entry) {
    if (!viewer || !entry.object || entry.partId || entry.previewOnly || !isCurrent(entry)) return false;
    if (!entry.attached) {
      // Local keys are renderer-only; only admit() can populate the authoritative correlation map.
      viewer.addPart(entry.key, entry.object);
      entry.attached = true;
      if (entry.thicknessEvidence) viewer.setThicknessEvidence?.(entry.key, entry.thicknessEvidence);
    }
    viewer.select(entry.key);
    return true;
  }

  function selectLocalPreview(key) {
    if (disposed) return false;
    const entry = previews.get(key);
    if (!entry || !isCurrent(entry) || entry.partId) return false;
    activeLocalPreviewKey = key;
    if (incompleteEntry === entry && incompleteViewer) {
      incompleteViewer.select(key);
      return true;
    }
    const selected = attachLocalPreview(entry);
    if (!selected) viewer?.clearSelection?.();
    return selected;
  }

  function captureFailedThumbnail(key, canvas = null) {
    const entry = previews.get(key);
    if (!entry || !isCurrent(entry) || entry.partId || entry.previewOnly || !entry.object) return null;
    const object = entry.object;
    entry.object = null;
    let attached = false;
    try {
      if (!viewer && canvas) attach(canvas);
      if (!viewer) return null;
      // This temporary local renderer key is never entered in admitted partKeys.
      if (!entry.attached) viewer.addPart(key, object);
      attached = true;
      guardModelDisposal(object, guardedResources, viewerDisposedResources);
      const source = viewer.snapshot?.(key, 192);
      return typeof source === 'string' && source.startsWith('data:image/png;base64,')
        && source.length <= 262144 ? source : null;
    } catch {
      return null; // Thumbnail/WebGL failure must not replace the actual upload failure.
    } finally {
      if (attached) {
        try { viewer.remove(key); }
        catch { /* Release remaining owned resources below. */ }
        finally { entry.attached = false; }
      }
      try { quarantine(key); }
      finally {
        try { disposeModel(object, disposedResources); }
        catch { setUnavailable(); } // Cleanup reporting must not replace the actual upload failure.
      }
    }
  }

  function getLocalThumbnail(key) {
    if (disposed || incompleteEntry?.key !== key || !incompleteViewer || !isCurrent(incompleteEntry)) return null;
    const source = incompleteViewer.snapshot?.(key, 192);
    return typeof source === 'string' && source.startsWith('data:image/png;base64,')
      && source.length <= 262144 ? source : null;
  }

  function notifyThickness(entry) {
    if (!entry.partId || !isCurrent(entry)) return;
    const evidence = entry.thicknessEvidence;
    const policy = policyForMaterial(entry.thicknessMaterial);
    const assessment = assessThickness(evidence, entry.thicknessMaterial);
    const available = !!(evidence?.fields?.some(field => field.some(Number.isFinite))
      && evidence.summary?.state !== 'unavailable');
    try {
      Promise.resolve(reportThickness(
        entry.partId,
        available,
        assessment?.hasThinRegion === true,
        !!evidence && assessment?.reliable === false,
        policy.process,
        policy.thinMm,
        assessment?.reliable && Number.isFinite(evidence?.summary?.minMm)
          ? evidence.summary.minMm : null,
        ++entry.thicknessRevision)).catch(() => {});
    } catch {
      // Advisory UI reporting must not alter upload or quotation authority.
    }
  }

  function setThicknessMaterial(partId, material) {
    const entry = previews.get(partKeys.get(partId));
    if (!entry) return false;
    entry.thicknessMaterial = material;
    if (entry.thicknessVisible && viewer) {
      thumbnails.delete(partId);
      viewer.select(partId);
      selectedPartId = partId;
      viewer.setThicknessVisible?.(true, policyForMaterial(material));
    }
    notifyThickness(entry);
    return true;
  }

  function toggleThickness(partId) {
    const entry = previews.get(partKeys.get(partId));
    if (!entry?.thicknessEvidence || !viewer) return false;
    thumbnails.delete(partId);
    viewer.select(partId);
    selectedPartId = partId;
    entry.thicknessVisible = !entry.thicknessVisible;
    if (!viewer.setThicknessVisible?.(
      entry.thicknessVisible, policyForMaterial(entry.thicknessMaterial))) {
      entry.thicknessVisible = false;
    }
    notifyThickness(entry);
    return entry.thicknessVisible;
  }

  function remove(partId) {
    const key = partKeys.get(partId);
    if (key) release(key);
  }

  function setBulkSelectionState(input, mixed) {
    if (disposed || input?.type !== 'checkbox') return;
    input.indeterminate = mixed === true;
  }


  function revealPart(root, partId) {
    if (disposed) return false;
    const rail = root?.querySelector?.('[data-workflow-part-list]');
    const tile = [...(rail?.querySelectorAll?.('[data-workflow-part]') ?? [])]
      .find(element => element.dataset.partId === partId);
    if (!tile) return false;
    const bounds = rail.getBoundingClientRect();
    const focused = tile.ownerDocument?.activeElement;
    // A late render must not scroll an oversized card over its keyboard-focused action.
    const revealTarget = focused && tile.contains(focused) ? focused : tile;
    const target = revealTarget.getBoundingClientRect();
    if (globalThis.getComputedStyle?.(rail)?.overflowY === 'visible') {
      if (target.top < 0 || target.bottom > globalThis.innerHeight)
        revealTarget.scrollIntoView?.({ block: 'nearest', inline: 'nearest', behavior: 'auto' });
      return true;
    }
    const add = rail.querySelector?.('.iq-compact-dropzone');
    const left = add && globalThis.getComputedStyle?.(add)?.position === 'sticky'
      ? Math.max(bounds.left, add.getBoundingClientRect().right) : bounds.left;
    if (target.left < left) rail.scrollLeft = Math.max(0, rail.scrollLeft + target.left - left);
    else if (target.right > bounds.right) rail.scrollLeft += target.right - bounds.right;
    if (target.top < bounds.top) rail.scrollTop = Math.max(0, rail.scrollTop + target.top - bounds.top);
    else if (target.bottom > bounds.bottom) rail.scrollTop += target.bottom - bounds.bottom;
    return true;
  }

  function selectedViewer() {
    return activeLocalPreviewKey && incompleteEntry?.key === activeLocalPreviewKey ? incompleteViewer : viewer;
  }
  function reset() { thumbnails.delete(selectedPartId); selectedViewer()?.reset(); }
  function fit() { thumbnails.delete(selectedPartId); selectedViewer()?.fit(); }
  function fullscreen() { return selectedViewer()?.fullscreen(); }
  function renderReviewThumbnails(root) {
    if (!viewer || !root?.querySelectorAll) return 0;
    let rendered = 0;
    for (const image of root.querySelectorAll('[data-review-thumbnail][data-part-id], [data-workflow-thumbnail][data-part-id]')) {
      let source = null;
      try {
        source = thumbnails.get(image.dataset.partId) ?? viewer.snapshot?.(image.dataset.partId);
      } catch {
        continue;
      }
      if (typeof source !== 'string' || !source.startsWith('data:image/')) continue;
      thumbnails.set(image.dataset.partId, source);
      image.src = source;
      rendered += 1;
    }
    return rendered;
  }

  function updatePartAppearance(partId, color) {
    if (!viewer || !partKeys.has(partId)) return null;
    viewer.select(partId);
    selectedPartId = partId;
    thumbnails.delete(partId);
    viewer.setColor(previewColorForSelection(color));
    const source = viewer.snapshot?.(partId);
    if (typeof source === 'string' && source.startsWith('data:image/')) thumbnails.set(partId, source);
    return typeof source === 'string' && source.startsWith('data:image/') ? source : null;
  }
  function status() { return availability; }

  function isIncompletePreview(key) {
    const entry = previews.get(key);
    return !!(entry?.previewOnly && entry.object && isCurrent(entry) && !entry.partId);
  }

  function retainIncompletePreview(key, canvas) {
    if (!isIncompletePreview(key) || disposed) return false;
    const entry = previews.get(key);
    if (incompleteEntry === entry) return true;
    if (incompleteEntry) release(incompleteEntry.key);
    incompleteViewer = createViewer(canvas);
    incompleteEntry = entry;
    entry.file = null; // Retain bounded geometry, not another copy of selected bytes.
    // A planar sheet has no inside: display either winding only in this local
    // exceptional viewer. Owned loader materials require no clone or extra lifetime.
    entry.object.traverse?.(child => {
      if (!child.isMesh) return;
      for (const material of Array.isArray(child.material) ? child.material : [child.material]) {
        if (material) { material.side = 2; material.needsUpdate = true; } // THREE.DoubleSide
      }
    });
    incompleteViewer.addPart(key, entry.object);
    incompleteViewer.select(key);
    notifyIncomplete(entry);
    return true;
  }


  function retainDevelopmentPreview(key, canvas, generation) {
    assertActive();
    if (!canvas?.isConnected || canvas.dataset?.localDemoGeneration !== String(generation)) return false;
    const entry = requirePreview(key);
    const claim = entry.geometryClaim;
    if (!isCurrent(entry) || !entry.object || entry.partId || entry.previewOnly
        || !Number.isFinite(claim?.volumeMm3) || claim.volumeMm3 <= 0) return false;
    if (developmentKeys.has(key)) return developmentCanvas === canvas;
    // The ordinary uploading canvas is gone in the zero-admitted-part error state.
    if (viewer && partKeys.size === 0) {
      const unusedViewer = viewer;
      viewer = null;
      unbindThumbnailCanvas();
      unusedViewer.dispose();
    }
    const ownedViewer = developmentViewer ?? createViewer(canvas);
    try {
      ownedViewer.addPart(key, entry.object, { bodyCount: claim.bodyCount });
      guardModelDisposal(entry.object, guardedResources, viewerDisposedResources);
      ownedViewer.select(key);
      developmentViewer = ownedViewer;
      developmentCanvas = canvas;
      developmentKeys.add(key);
      if (canvas?.dataset) canvas.dataset.localDemoReady = 'true';
      return true;
    } catch (primary) {
      guardModelDisposal(entry.object, guardedResources, viewerDisposedResources);
      try { ownedViewer.remove(key); } catch { /* The original attach failure remains primary. */ }
      if (!developmentViewer) {
        try { ownedViewer.dispose(); } catch { /* Do not replace the original attach failure. */ }
      }
      try { disposeModel(entry.object, disposedResources); } catch { /* Keep the original attach failure. */ }
      entry.quarantined = true;
      try { entry.controller?.abort(); } catch { /* Keep the original attach failure. */ }
      entry.object = null;
      entry.geometryClaim = null;
      throw primary;
    }
  }

  function updateDevelopmentPreview(key, color) {
    assertActive();
    if (!developmentKeys.has(key) || !developmentViewer) return false;
    const entry = previews.get(key);
    try {
      developmentViewer.select(key);
      developmentViewer.setColor(previewColorForSelection(color));
      return true;
    } finally {
      // The viewer may replace materials while applying the colour.
      guardModelDisposal(entry?.object, guardedResources, viewerDisposedResources);
    }
  }

  function detachDevelopmentPreview(key) {
    if (!developmentKeys.delete(key)) return;
    const ownedViewer = developmentViewer;
    const failures = [];
    try { ownedViewer?.remove(key); } catch (error) { failures.push(error); }
    if (developmentKeys.size === 0) {
      developmentViewer = null;
      if (developmentCanvas?.dataset) delete developmentCanvas.dataset.localDemoReady;
      developmentCanvas = null;
      try { ownedViewer?.dispose(); } catch (error) { failures.push(error); }
    }
    if (failures.length === 1) throw failures[0];
    if (failures.length > 1) throw new AggregateError(failures, 'Development preview detach failed.');
  }

  function notifyIncomplete(entry) {
    if (incompleteEntry !== entry || !isCurrent(entry) || !entry.evidenceCompleted) return;
    try {
      Promise.resolve(reportIncomplete(entry.key, ++entry.incompleteRevision, true)).catch(() => {});
    } catch {
      // Local preview cannot authorize admission or pricing.
    }
  }

  function removeIncompletePreview(key) {
    if (incompleteEntry?.key !== key) return false;
    release(key);
    return true;
  }

  function getIncompletePreviewState() {
    return incompleteEntry ? { key: incompleteEntry.key, revision: incompleteEntry.incompleteRevision,
      incomplete: incompleteEntry.evidenceCompleted } : null;
  }

  function setUnavailable() {
    if (availability === 'unavailable') return;
    availability = 'unavailable';
    try {
      Promise.resolve(reportStatus('unavailable')).catch(() => {});
    } catch {
      // Status reporting is advisory and cannot affect server authority.
    }
  }

  function unbindThumbnailCanvas() {
    const canvas = thumbnailCanvas;
    thumbnailCanvas = null;
    for (const type of ['pointerup', 'wheel', 'keydown'])
      canvas?.removeEventListener?.(type, invalidateCameraThumbnail);
  }

  function dispose() {
    if (disposed) return;
    disposed = true;
    const failures = [];
    const attempt = action => { try { action(); } catch (error) { failures.push(error); } };
    attempt(unbindThumbnailCanvas);
    thumbnails.clear();
    attempt(unbindInput);
    for (const key of [...previews.keys()]) attempt(() => release(key));
    for (const selection of selectionSnapshots) selection.files.length = 0;
    selectionSnapshots.length = 0;
    attempt(unbindPartsWheel);
    partKeys.clear();
    const ownedViewer = viewer;
    viewer = null;
    attempt(() => ownedViewer?.dispose());
    attempt(() => thicknessAnalyzer.dispose?.());
    if (failures.length === 1) throw failures[0];
    if (failures.length > 1) throw new AggregateError(failures, 'Preview disposal failed.');
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
    getGeometryClaim,
    getFailureCategory,
    bindInput,
    discardSelection,
    attach,
    detachEmptyViewer,
    admit,
    quarantine,
    release,
    retry,
    select,
    selectLocalPreview,
    getLocalThumbnail,
    captureFailedThumbnail,
    setThicknessMaterial,
    toggleThickness,
    remove,
    reset,
    fit,
    fullscreen,
    renderReviewThumbnails,
    revealPart,
    setBulkSelectionState,
    updatePartAppearance,
    isIncompletePreview,
    retainIncompletePreview,
    retainDevelopmentPreview,
    updateDevelopmentPreview,
    removeIncompletePreview,
    getIncompletePreviewState,
    status,
    dispose,
  });
}

async function sha256(file, signal) {
  if (typeof file?.arrayBuffer !== 'function') {
    throw new TypeError('The selected file cannot be hashed.');
  }
  const bytes = await file.arrayBuffer();
  if (signal?.aborted) throw new DOMException('Operation cancelled.', 'AbortError');
  const digest = await globalThis.crypto.subtle.digest('SHA-256', bytes);
  return Array.from(new Uint8Array(digest), value => value.toString(16).padStart(2, '0')).join('');
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
export { createMaterialCardSelector } from './material-card-selector.mjs';
