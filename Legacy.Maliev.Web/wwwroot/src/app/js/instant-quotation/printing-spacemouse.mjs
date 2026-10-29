import navigationApi from '../model-viewer/spacemouse-navigation.js';

const sdkModuleUrl = '/src/vendor/@3dconnexion/3dconnexionjs/3dconnexion.module.min.js';

function guardCallback(owner, name) {
  return (...args) => {
    if (!owner.active || !owner.callbacks) {
      if (name === 'onConnect') {
        try { owner.driver?.deref()?.close?.(); } catch { /* A late optional session cannot reclaim the viewer. */ }
      }
      return;
    }
    if (name === 'onConnect') owner.connected?.();
    return owner.callbacks[name](...args);
  };
}

/** Owns the optional driver for one Printing viewer, independently of window.viewer. */
export function createPrintingSpaceMouse(options, {
  fetch: fetchDriver = globalThis.fetch,
  loadDriver = () => import(sdkModuleUrl),
  setTimeout: schedule = globalThis.setTimeout,
  clearTimeout: cancel = globalThis.clearTimeout,
} = {}) {
  let canvas = options.canvas;
  let navigationOptions = options;
  options = null;
  const probe = new AbortController();
  let disposed = false;
  const callbackOwner = { active: true, callbacks: null, driver: null, connected: null };
  let probeTimer = null;
  let connectionTimer = null;
  let finishProbe = null;
  let navigation = null;
  let driver = null;

  function status(value) { canvas.setAttribute('data-spacemouse-status', value); }
  function clearConnectionTimer() {
    if (connectionTimer !== null) cancel(connectionTimer);
    connectionTimer = null;
  }
  function releaseProbe() {
    if (probeTimer !== null) cancel(probeTimer);
    probeTimer = null;
    probe.abort();
    finishProbe?.(null);
    finishProbe = null;
  }
  function closeDriver(owned) {
    try { owned?.close?.(); } catch { /* Optional transport failure must not retain the Printing viewer. */ }
  }
  function releaseDriver() {
    clearConnectionTimer();
    callbackOwner.active = false;
    callbackOwner.callbacks = null;
    callbackOwner.connected = null;
    const ownedNavigation = navigation, ownedDriver = driver;
    navigation = null;
    driver = null;
    try { ownedNavigation?.disconnect(); } catch { /* Continue releasing our resources if the SDK cannot delete its controller. */ }
    finally { closeDriver(ownedDriver); }
  }

  status('probing');
  const ready = (async () => {
    let response;
    try {
      response = await Promise.race([
        fetchDriver('https://127.51.68.120:8181/3dconnexion/nlproxy', { cache: 'no-store', signal: probe.signal }),
        new Promise(resolve => {
          finishProbe = resolve;
          probeTimer = schedule(() => { probe.abort(); resolve(null); }, 1500);
        }),
      ]);
    } finally { releaseProbe(); }
    if (disposed) return;
    if (!response?.ok) { status('unavailable'); return; }
    const { _3Dconnexion: Driver } = await loadDriver();
    if (disposed) return;
    if (typeof Driver !== 'function') throw new TypeError('The optional driver is unavailable.');

    class OwnedDriver extends Driver {
      constructor(client) {
        // Printing keeps Three's Y-up world; the shared legacy viewer advertises Z-up.
        const printingBasis = () => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        client.getCoordinateSystem = printingBasis;
        client.getFrontView = printingBasis;
        if (callbackOwner.active) callbackOwner.callbacks = client;
        callbackOwner.connected = clearConnectionTimer;
        super(Object.fromEntries(Object.keys(client).map(name => [name, guardCallback(callbackOwner, name)])));
        callbackOwner.driver = new WeakRef(this);
        driver = this;
      }
      connect(...args) {
        if (!callbackOwner.active) return false;
        return super.connect(...args);
      }
    }
    navigation = navigationApi.createSpaceMouseNavigation(navigationOptions);
    connectionTimer = schedule(() => {
      releaseDriver();
      if (!disposed) status('unavailable');
    }, 5000);
    if (!navigation.connect(OwnedDriver)) {
      releaseDriver();
      if (!disposed) status('unavailable');
    }
  })().catch(() => {
    if (disposed) return;
    releaseDriver();
    status('unavailable');
  });

  function dispose() {
    if (disposed) return;
    disposed = true;
    releaseProbe();
    releaseDriver();
    navigationOptions = null;
    canvas = null;
  }
  // The SDK's private timeout=0 XHR has no public abort API. A late session is closed above;
  // cancelling that SDK request itself requires its completion or the browser context ending.
  return Object.freeze({ ready, dispose });
}
