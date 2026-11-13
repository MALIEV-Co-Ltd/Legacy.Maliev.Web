const assert = require('node:assert/strict');
const test = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
let THREE;
test.before(async () => { THREE = await import(pathToFileURL(path.resolve(__dirname, '../../Legacy.Maliev.Web/node_modules/three/build/three.module.js')).href); });
const file = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation/printing-spacemouse.mjs');
const load = async () => fs.existsSync(file) ? import(pathToFileURL(file).href) : {};
const flush = () => new Promise(resolve => setImmediate(resolve));

function harness(t, overrides = {}) {
    const listeners = new Map(), attributes = new Map(), timers = new Map(), requests = [];
    const canvas = { tabIndex: -1,
        addEventListener: (name, fn) => listeners.set(name, fn),
        removeEventListener: name => listeners.delete(name),
        setAttribute: (name, value) => { attributes.set(name, value); if (name === 'data-spacemouse-status') overrides.onStatus?.(value); },
        getBoundingClientRect: () => ({ left: 0, top: 0, width: 800, height: 600 }),
    };
    const camera = new THREE.PerspectiveCamera(45, 4 / 3, 0.1, 1000);
    const controls = { target: new THREE.Vector3(), update() {} };
    const model = new THREE.Mesh(new THREE.BoxGeometry(2, 4, 6), new THREE.MeshBasicMaterial());
    t.after(() => { model.geometry.dispose(); model.material.dispose(); });
    let selected = model, instance, fits = 0, resets = 0;
    class Driver {
        constructor(client) { this.client = client; instance = this; this.created = 0; this.closed = 0; this.deleted = 0; this.connectCalls = 0; }
        connect() { this.connectCalls++; if (overrides.connectThrows) throw new Error('controlled connect failure'); if (overrides.connectFalse) return false; if (!overrides.delayedConnect) this.client.onConnect(); return true; }
        create3dmouse(element, applicationName) { assert.equal(element, canvas); this.applicationName = applicationName; this.created++; }
        delete3dmouse() { this.deleted++; if (overrides.deleteThrows) throw new Error('controlled driver deletion'); }
        close() { this.closed++; }
    }
    const platform = {
        fetch: async (url, options) => { requests.push({ url, options }); return { ok: true }; },
        loadDriver: async () => ({ _3Dconnexion: Driver }),
        setTimeout: (fn, ms) => { const id = timers.size + 1; timers.set(id, { fn, ms }); return id; },
        clearTimeout: id => timers.delete(id),
        ...overrides,
    };
    return { Driver, canvas, camera, controls, model, timers, attributes, listeners, requests,
        get driver() { return instance; }, get fits() { return fits; }, get resets() { return resets; },
        select: value => { selected = value; },
        async start() {
            const { createPrintingSpaceMouse } = await load();
            assert.equal(typeof createPrintingSpaceMouse, 'function', 'Printing needs an owned optional driver lifecycle.');
            const owner = createPrintingSpaceMouse({ THREE, canvas, camera, controls,
                getModelObject: () => selected, fitView: () => { if (overrides.fitThrows) throw overrides.fitThrows; fits++; }, resetView: () => resets++,
            }, platform);
            t.after(() => owner.dispose());
            return owner;
        },
    };
}

test('Printing binds the real navigation callbacks to its camera, controls and current model', async t => {
    const h = harness(t), owner = await h.start(); await owner.ready;
    assert.equal(h.attributes.get('data-spacemouse-status'), 'connected');
    assert.equal(h.driver.created, 1);
    assert.equal(h.requests[0].url, 'https://127.51.68.120:8181/3dconnexion/nlproxy');
    assert.equal(h.driver.applicationName, 'MALIEV 3D Viewer');
    assert.equal(h.requests[0].options.cache, 'no-store');
    const client = h.driver.client;
    assert.deepEqual(client.getCoordinateSystem(), [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);
    assert.deepEqual(client.getFrontView(), [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);
    assert.deepEqual(client.getModelExtents(), [-1, -2, -3, 1, 2, 3]);
    client.setTarget([3, 4, 5]); assert.deepEqual(h.controls.target.toArray(), [3, 4, 5]);
    client.setFov(Math.PI / 3); assert.ok(Math.abs(h.camera.fov - 60) < 1e-9);
    client.setViewMatrix(new THREE.Matrix4().makeTranslation(10, 20, 30).toArray());
    assert.deepEqual(h.camera.position.toArray(), [10, 20, 30]);
    client.setActiveCommand('MALIEV_VIEW_FIT'); client.setActiveCommand('MALIEV_VIEW_RESET');
    assert.equal(h.fits, 1); assert.equal(h.resets, 1);
    assert.equal(client.getUnitsToMeters(), 0.001);
    client.setLookFrom([10, 0, 0]); client.setLookDirection([-1, 0, 0]); client.setLookAperture(0.1);
    assert.ok(new THREE.Vector3().fromArray(client.getLookAt()).distanceTo(new THREE.Vector3(1, 0, 0)) < 1e-6);
    client.setSelectionOnly(true); assert.equal(client.getSelectionEmpty(), false);
    assert.deepEqual(client.getSelectionExtents(), [-1, -2, -3, 1, 2, 3]);
    h.select(null); assert.deepEqual(client.getModelExtents(), [0, 0, 0, 0, 0, 0]);
    assert.equal(h.timers.size, 0);
});

for (const mode of ['unavailable', 'network-rejection']) test(`Printing ${mode} leaves ordinary controls untouched`, async t => {
    let loads = 0;
    const h = harness(t, { fetch: async () => { if (mode === 'network-rejection') throw new TypeError('offline'); return { ok: false }; },
        loadDriver: async () => { loads++; throw new Error('must not load'); } });
    const owner = await h.start(); await owner.ready;
    assert.equal(h.attributes.get('data-spacemouse-status'), 'unavailable');
    assert.equal(loads, 0); assert.equal(h.listeners.size, 0); assert.equal(h.timers.size, 0);
    assert.deepEqual(h.controls.target.toArray(), [0, 0, 0]);
});

test('a stalled availability probe aborts at1500ms and releases its timer', async t => {
    let signal;
    const h = harness(t, { fetch: (_url, options) => { signal = options.signal; return new Promise(() => {}); } });
    const owner = await h.start();
    const timer = [...h.timers.values()].find(value => value.ms === 1500); assert.ok(timer);
    timer.fn(); await owner.ready;
    assert.equal(signal.aborted, true); assert.equal(h.timers.size, 0);
    assert.equal(h.attributes.get('data-spacemouse-status'), 'unavailable');
});

test('disposal aborts a pending probe and ignores its late success', async t => {
    let resolve, signal, loads = 0;
    const h = harness(t, { fetch: (_url, options) => { signal = options.signal; return new Promise(done => { resolve = done; }); },
        loadDriver: async () => { loads++; } });
    const owner = await h.start(); owner.dispose(); await owner.ready;
    resolve({ ok: true }); await flush();
    assert.equal(signal.aborted, true); assert.equal(loads, 0); assert.equal(h.timers.size, 0);
    assert.equal(h.listeners.size, 0);
});

test('disposal during SDK loading cannot attach a late driver', async t => {
    let resolve;
    const h = harness(t, { loadDriver: () => new Promise(done => { resolve = done; }) });
    const owner = await h.start(); await flush(); owner.dispose();
    resolve({ _3Dconnexion: class { constructor() { assert.fail('late construction'); } } });
    await owner.ready; assert.equal(h.listeners.size, 0); assert.equal(h.timers.size, 0);
});

test('disposal releases controller/listener ownership and closes a late established session', async t => {
    const h = harness(t, { delayedConnect: true }), owner = await h.start(); await owner.ready;
    const driver = h.driver; owner.dispose(); owner.dispose();
    assert.equal(driver.deleted, 1); assert.equal(h.listeners.size, 0); assert.equal(h.timers.size, 0);
    const closed = driver.closed;
    driver.client.onConnect(); driver.client.on3dmouseCreated(); driver.client.setTarget([9, 9, 9]);
    driver.client.setActiveCommand('MALIEV_VIEW_FIT');
    assert.equal(driver.created, 0); assert.ok(driver.closed > closed);
    assert.deepEqual(h.controls.target.toArray(), [0, 0, 0]); assert.equal(h.fits, 0);
});

test('the finite connection deadline releases a driver that never connects', async t => {
    const h = harness(t, { delayedConnect: true }), owner = await h.start(); await owner.ready;
    const driver = h.driver, timer = [...h.timers.values()].find(value => value.ms === 5000); assert.ok(timer);
    timer.fn(); assert.equal(h.attributes.get('data-spacemouse-status'), 'unavailable');
    assert.equal(h.listeners.size, 0); assert.equal(h.timers.size, 0); assert.ok(driver.closed > 0);
    driver.client.onConnect(); assert.equal(driver.created, 0);
});

test('a driver deletion error cannot retain our timers, listener or current callbacks', async t => {
    const h = harness(t, { deleteThrows: true }), owner = await h.start(); await owner.ready;
    const driver = h.driver; assert.doesNotThrow(() => owner.dispose());
    assert.equal(h.listeners.size, 0); assert.equal(h.timers.size, 0); assert.ok(driver.closed > 0);
    driver.client.setTarget([7, 8, 9]); assert.deepEqual(h.controls.target.toArray(), [0, 0, 0]);
});

for (const mode of ['connectFalse', 'connectThrows']) test(`a driver ${mode} releases optional ownership`, async t => {
    const h = harness(t, { [mode]: true }), owner = await h.start(); await owner.ready;
    assert.equal(h.attributes.get('data-spacemouse-status'), 'unavailable');
    assert.equal(h.listeners.size, 0); assert.equal(h.timers.size, 0); assert.ok(h.driver.closed > 0);
});

test('an unavailable SDK module leaves Printing available and releases probe resources', async t => {
    const h = harness(t, { loadDriver: async () => { throw new TypeError('SDK loading unavailable'); } });
    const owner = await h.start(); await owner.ready;
    assert.equal(h.attributes.get('data-spacemouse-status'), 'unavailable');
    assert.equal(h.listeners.size, 0); assert.equal(h.timers.size, 0);
});

test('a replaced owner cannot mutate the replacement camera or overwrite its status', async t => {
    const h = harness(t, { delayedConnect: true }), old = await h.start(); await old.ready;
    const prior = h.driver; old.dispose();
    const current = await h.start(); await current.ready;
    h.driver.client.onConnect(); h.driver.client.setTarget([1, 2, 3]);
    prior.client.onConnect(); prior.client.onDisconnect(); prior.client.setTarget([9, 9, 9]);
    assert.equal(prior.created, 0); assert.ok(prior.closed > 0);
    assert.deepEqual(h.controls.target.toArray(), [1, 2, 3]);
    assert.equal(h.attributes.get('data-spacemouse-status'), 'connected');
});

test('current callback failures preserve the original error identity', async t => {
    const original = new Error('current view command failure');
    const h = harness(t, { fitThrows: original }), owner = await h.start(); await owner.ready;
    assert.throws(() => h.driver.client.setActiveCommand('MALIEV_VIEW_FIT'), error => error === original);
});

test('synchronous disposal during connecting cannot start a new SDK transport', async t => {
    let owner, release;
    const h = harness(t, {
        loadDriver: () => new Promise(resolve => { release = resolve; }),
        onStatus: value => { if (value === 'connecting') owner.dispose(); },
    });
    owner = await h.start(); await flush(); release({ _3Dconnexion: h.Driver }); await owner.ready;
    assert.equal(h.driver.connectCalls, 0);
    assert.equal(h.driver.created, 0); assert.ok(h.driver.closed > 0);
    assert.equal(h.listeners.size, 0); assert.equal(h.timers.size, 0);
});
