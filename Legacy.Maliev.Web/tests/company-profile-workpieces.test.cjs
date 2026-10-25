const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname, '../wwwroot/src/app/js/company-profile-workpieces.js'), 'utf8');
const flush = async () => { for (let i = 0; i < 12; i++) await Promise.resolve(); };

function fixture(options = {}) {
    const effects = [];
    class Node {
        constructor() { this.dataset = {}; this.events = new Map(); this.attrs = {}; this.style = {}; this.hidden = false; }
        addEventListener(k, f) { if (!this.events.has(k)) this.events.set(k, new Set()); this.events.get(k).add(f); }
        removeEventListener(k, f) { this.events.get(k)?.delete(f); }
        emit(k, extra = {}) { this.events.get(k)?.forEach(f => f({ target: this, ...extra })); }
        setAttribute(k, v) { this.attrs[k] = v; }
        animate(frames, timing) {
            let resolve;
            const finished = options.deferred ? new Promise(r => { resolve = r; }) : Promise.resolve();
            const effect = { frames, timing, finished, resolve, cancel() { this.cancelled = true; resolve?.(); } };
            effects.push(effect); return effect;
        }
    }
    const root = new Node(); Object.assign(root.dataset, { pauseLabel: 'Pause', playLabel: 'Play' });
    const photos = Array.from({ length: 4 }, (_, i) => { const p = new Node(); p.hidden = i !== 0; if (options.noAnimation) p.animate = undefined; return p; });
    const choices = photos.map(() => new Node());
    const controls = new Node(); controls.hidden = true;
    const toggle = new Node();
    root.querySelectorAll = s => s === '[data-workpiece-photo]' ? photos : choices;
    root.querySelector = s => s === '[data-workpiece-controls]' ? controls : toggle;
    const document = new Node(); document.querySelector = () => root; document.hidden = false;
    const motion = new Node(); motion.matches = !!options.reduced;
    const window = new Node(); window.matchMedia = () => motion;
    const timers = new Map(); let id = 0;
    window.setTimeout = (callback, delay) => { timers.set(++id, { callback, delay }); return id; };
    window.clearTimeout = key => timers.delete(key);
    let observer;
    window.IntersectionObserver = class { constructor(callback) { this.callback = callback; observer = this; } observe() {} disconnect() { this.disconnected = true; } };
    const context = vm.createContext({ document, window, Set, Promise });
    const start = () => vm.runInContext(source, context); start();
    return { root, photos, choices, controls, toggle, document, window, motion, timers, effects, observer, start,
        async tick() { const t = [...timers.values()][0]; assert.ok(t); timers.clear(); t.callback(); await flush(); },
        stop() { window.emit('pagehide', { persisted: false }); }
    };
}

test('workpiece gallery rotates four photos every six seconds with a 650ms opacity-only fade', async () => {
    const f = fixture();
    for (const expected of [1, 2, 3, 0]) {
        assert.equal([...f.timers.values()][0].delay, 6000);
        await f.tick(); assert.equal(f.photos.findIndex(p => !p.hidden), expected);
        assert.equal(f.photos.filter(p => !p.hidden).length, 1);
    }
    assert.ok(f.effects.every(e => e.timing.duration === 650 && e.frames.every(frame => Object.keys(frame).join() === 'opacity')));
    f.stop();
});

test('outgoing photo stays opaque beneath incoming photo until the fade completes', async () => {
    const f = fixture({ deferred: true }); await f.tick();
    assert.equal(f.photos[0].hidden, false); assert.equal(f.photos[1].hidden, false);
    assert.equal(f.photos[0].style.zIndex, '1'); assert.equal(f.photos[1].style.zIndex, '2');
    f.effects[0].resolve(); await flush(); assert.equal(f.photos[0].hidden, true); assert.equal(f.photos[1].hidden, false); f.stop();
});

test('hover and focus suspend automatic changes; Play explicitly releases the focus pause', () => {
    const f = fixture(); f.root.emit('pointerenter'); assert.equal(f.timers.size, 0);
    f.root.emit('pointerleave'); assert.equal(f.timers.size, 1);
    f.root.emit('focusin'); f.root.emit('pointerleave'); assert.equal(f.timers.size, 0);
    f.toggle.emit('click'); assert.equal(f.timers.size, 1); f.stop();
});

test('reduced motion keeps manual selection but starts neither timers nor fades', async () => {
    const f = fixture({ reduced: true }); assert.equal(f.toggle.disabled, true); assert.equal(f.timers.size, 0);
    f.choices[3].emit('click'); await flush(); assert.equal(f.photos[3].hidden, false); assert.equal(f.effects.length, 0); f.stop();
});

test('offscreen/hidden state pauses rotation and entering the viewport preloads the gallery', () => {
    const f = fixture(); f.observer.callback([{ target: f.root, isIntersecting: false }]); assert.equal(f.timers.size, 0);
    f.observer.callback([{ target: f.root, isIntersecting: true }]); assert.ok(f.photos.every(p => p.loading === 'eager'));
    f.document.hidden = true; f.document.emit('visibilitychange'); assert.equal(f.timers.size, 0);
    f.document.hidden = false; f.document.emit('visibilitychange'); assert.equal(f.timers.size, 1); f.stop();
});

test('interrupted fades cannot reveal a stale photo', async () => {
    const f = fixture({ deferred: true }); f.choices[1].emit('click'); f.choices[2].emit('click');
    f.effects.forEach(e => e.resolve()); await flush();
    assert.equal(f.photos.filter(p => !p.hidden).length, 1); assert.equal(f.photos[2].hidden, false); f.stop();
});

test('unavailable animation API leaves a working manual gallery and teardown releases resources', async () => {
    const f = fixture({ noAnimation: true }); f.start(); assert.equal(f.timers.size, 1);
    f.choices[2].emit('click'); await flush(); assert.equal(f.photos[2].hidden, false);
    f.stop(); assert.equal(f.timers.size, 0); assert.equal(f.controls.hidden, true); assert.equal(f.observer.disconnected, true);
    assert.ok([...f.root.events.values()].every(handlers => handlers.size === 0));
});

test('back-forward cache and changed motion preference preserve a usable static photo', async () => {
    const f = fixture(); f.window.emit('pagehide', { persisted: true }); assert.equal(f.timers.size, 0);
    f.window.emit('pageshow'); assert.equal(f.timers.size, 1);
    f.motion.matches = true; f.motion.emit('change'); assert.equal(f.timers.size, 0);
    f.choices[1].emit('click'); await flush(); assert.equal(f.photos[1].hidden, false); f.stop();
});

test('explicit user pause survives visibility, offscreen and bfcache transitions', () => {
    const f = fixture();
    f.toggle.emit('click');
    assert.equal(f.toggle.dataset.paused, 'true');
    assert.equal(f.timers.size, 0);
    f.observer.callback([{ target: f.root, isIntersecting: false }]);
    f.observer.callback([{ target: f.root, isIntersecting: true }]);
    f.document.hidden = true;
    f.document.emit('visibilitychange');
    f.document.hidden = false;
    f.document.emit('visibilitychange');
    f.window.emit('pagehide', { persisted: true });
    f.window.emit('pageshow');
    assert.equal(f.timers.size, 0);
    assert.equal(f.toggle.dataset.paused, 'true');
    f.toggle.emit('click');
    assert.equal(f.toggle.dataset.paused, 'false');
    assert.equal(f.timers.size, 1);
    f.stop();
});

test('late intersection observation after teardown cannot restart rotation', () => {
    const f = fixture();
    f.stop();
    f.observer.callback([{ target: f.root, isIntersecting: true }]);
    assert.equal(f.timers.size, 0);
    assert.equal(f.controls.hidden, true);
    assert.equal(f.root.dataset.workpieceReady, 'stopped');
});
