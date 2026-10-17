const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname, '../wwwroot/src/app/js/home-hero-carousel.js'), 'utf8');
const flush = async () => { for (let i = 0; i < 20; i++) await Promise.resolve(); };

function fixture(options = {}) {
    const recorded = [], pendingAnimations = [];
    class Node {
        constructor() { this.dataset = {}; this.events = new Map(); this.attrs = {}; this.hidden = false; this.textContent = ''; }
        addEventListener(name, handler) { if (!this.events.has(name)) this.events.set(name, new Set()); this.events.get(name).add(handler); }
        removeEventListener(name, handler) { this.events.get(name)?.delete(handler); }
        emit(name, extra = {}) { this.events.get(name)?.forEach(handler => handler({ target: this, ...extra })); }
        setAttribute(name, value) { this.attrs[name] = value; }
        focus() { root.emit('focusin', { target: this }); this.focused = true; }
        animate(frames, timing) {
            if (options.animationFailure) throw new Error('unsupported');
            let finish;
            const finished = options.holdAnimations ? new Promise(resolve => { finish = resolve; pendingAnimations.push(resolve); }) : Promise.resolve();
            const animation = { finished, cancel() { this.cancelled = true; finish?.(); } };
            recorded.push({ frames, timing, animation });
            return animation;
        }
    }
    const root = new Node();
    root.contains = node => node === root || panels.includes(node) || node === toggle || node === controls;
    Object.assign(root.dataset, { pauseLabel: 'Pause automatic slides', playLabel: 'Resume automatic slides', motionPausedLabel: 'Automatic slides paused' });
    const panels = ['assembly', 'partnership', 'journey'].map((name, i) => {
        const panel = new Node(); panel.dataset.heroScene = name; panel.hidden = i !== 0;
        panel.querySelectorAll = selector => Array.from({ length: selector.includes('journey-images') ? 4 : 1 }, () => new Node());
        return panel;
    });
    const choices = panels.map(() => new Node());
    const controls = new Node(); controls.hidden = true;
    const toggle = new Node(), previous = new Node(), next = new Node(), status = new Node();
    const toggleLabel = new Node();
    if (options.compact) toggle.querySelector = () => toggleLabel;
    const selectors = new Map([['[data-hero-controls]', controls], ['[data-hero-toggle]', toggle], ['[data-hero-prev]', previous], ['[data-hero-next]', next], ['[data-hero-status]', status]]);
    if (options.compact) { selectors.delete('[data-hero-prev]'); selectors.delete('[data-hero-next]'); }
    if (options.missingControl) selectors.delete('[data-hero-toggle]');
    root.querySelector = selector => selectors.get(selector);
    root.querySelectorAll = selector => selector === '[data-hero-scene]' ? panels : choices;
    const document = new Node(); document.querySelector = () => root; document.hidden = false;
    const motion = new Node(); motion.matches = !!options.reduced;
    const window = new Node(); window.matchMedia = () => motion;
    const timers = new Map(); let clock = 0, now = 0;
    window.setTimeout = (callback, delay) => { const id = ++clock; timers.set(id, { callback, delay, due: now + delay }); return id; };
    window.clearTimeout = id => timers.delete(id);
    let observer;
    window.IntersectionObserver = class {
        constructor(callback) { this.callback = callback; observer = this; }
        observe() {} disconnect() { this.disconnected = true; }
    };
    const context = vm.createContext({ document, window, Set, Promise });
    const start = () => vm.runInContext(source, context);
    start();
    return { root, panels, choices, controls, toggle, toggleLabel, previous, next, status, document, window, motion, timers, recorded, observer, start,
        async tick() { const [id, item] = [...timers.entries()].sort((a, b) => a[1].due - b[1].due)[0] || []; assert.ok(item, 'a timer must be scheduled'); now = item.due; timers.delete(id); item.callback(); await flush(); },
        async advance(milliseconds) { now += milliseconds; const due = [...timers.entries()].filter(([, item]) => item.due <= now); for (const [id, item] of due) { timers.delete(id); item.callback(); } await flush(); },
        async finishAnimations() { const pending = pendingAnimations.splice(0); pending.forEach(finish => finish()); await flush(); },
        active() { return panels.findIndex(panel => !panel.hidden); },
        stop() { window.emit('pagehide', { persisted: false }); }
    };
}


test('rotates three scenes after eight readable seconds with a persistent pause control', async () => {
    const f = fixture();
    assert.equal(f.controls.hidden, false);
    assert.equal(f.toggle.events.get('click').size, 1);
    assert.equal(f.toggle.textContent, 'Pause automatic slides');
    assert.equal([...f.timers.values()][0].delay, 8000);
    for (const expected of [1, 2, 0]) { await f.tick(); assert.equal(f.active(), expected); assert.equal(f.panels.filter(p => !p.inert).length, 1); }
    assert.ok(f.recorded.some(a => a.frames.some(frame => frame.clipPath)));
    assert.ok(f.recorded.some(a => a.frames.some(frame => frame.transform?.includes('rotate'))));
    f.stop(); assert.equal(f.timers.size, 0);
});

test('hover pauses with the persistent control visible and leaving starts a full interval', () => {
    const f = fixture(); f.root.emit('pointerenter'); assert.equal(f.timers.size, 0);
    assert.equal(f.controls.hidden, false);
    f.root.emit('pointerleave'); assert.equal([...f.timers.values()][0].delay, 8000); f.stop();
});

test('focus pauses while reading a hero link and resumes only after leaving the hero', () => {
    const f = fixture(); f.root.emit('focusin'); assert.equal(f.timers.size, 0);
    f.root.emit('focusout', { relatedTarget: f.panels[0] }); assert.equal(f.timers.size, 0);
    f.root.emit('focusout', { relatedTarget: null }); assert.equal(f.timers.size, 1); f.stop();
});

test('reduced motion keeps a static readable hero with a disabled explanatory control', () => {
    const f = fixture({ reduced: true }); assert.equal(f.timers.size, 0);
    assert.equal(f.active(), 0); assert.equal(f.recorded.length, 0);
    assert.equal(f.toggle.disabled, true); assert.equal(f.toggle.textContent, 'Automatic slides paused'); f.stop();
});

test('visibility and offscreen state suspend rotation', () => {
    const f = fixture(); f.document.hidden = true; f.document.emit('visibilitychange'); assert.equal(f.timers.size, 0);
    f.document.hidden = false; f.document.emit('visibilitychange'); assert.equal(f.timers.size, 1);
    f.observer.callback([{ target: f.root, isIntersecting: false }]); assert.equal(f.timers.size, 0);
    f.observer.callback([{ target: f.root, isIntersecting: true }]); assert.equal(f.timers.size, 1); f.stop();
});

test('motion preference changes settle transitions and animation failures remain readable', async () => {
    const f = fixture({ animationFailure: true }); await f.tick(); assert.equal(f.active(), 1);
    f.motion.matches = true; f.motion.emit('change'); assert.equal(f.timers.size, 0);
    f.motion.matches = false; f.motion.emit('change'); assert.equal(f.timers.size, 1); f.stop();
});

test('initialization is idempotent and teardown releases timers observers and listeners', () => {
    const f = fixture(); f.start(); assert.equal(f.timers.size, 1);
    f.stop(); assert.equal(f.timers.size, 0); assert.equal(f.observer.disconnected, true);
    f.root.emit('pointerleave'); assert.equal(f.timers.size, 0);
    assert.ok([...f.root.events.values()].every(handlers => handlers.size === 0));
    assert.ok([...f.toggle.events.values()].every(handlers => handlers.size === 0));
    assert.equal(f.toggle.disabled, true);
});

test('back-forward cache suspension preserves rotation on restore', async () => {
    const f = fixture(); f.window.emit('pagehide', { persisted: true }); assert.equal(f.timers.size, 0);
    f.window.emit('pageshow'); assert.equal(f.timers.size, 1); await f.tick(); assert.equal(f.active(), 1); f.stop();
});

test('shipped markup has a native labeled pause control and process uses four distinct enclosure photos', () => {
    const markup = fs.readFileSync(path.resolve(__dirname, '../Components/Pages/Home/HomeHero.razor'), 'utf8');
    assert.match(markup, /<button type="button"[^>]*data-hero-toggle/);
    assert.match(markup, /aria-controls="home-hero-panels"/);
    assert.match(markup, /data-hero-controls hidden/);
    const home = fs.readFileSync(path.resolve(__dirname, '../Components/Pages/Home/HomeContent.razor'), 'utf8');
    const process = home.slice(home.indexOf('<ol class="landing-process-grid">'), home.indexOf('</ol>', home.indexOf('<ol class="landing-process-grid">')));
    assert.equal((process.match(/class="landing-step-photo"/g) || []).length, 4);
    assert.doesNotMatch(process, /landing-step-icon|camera-bracket|capability-cnc/);
    for (const name of ['home-enclosure-sketch.webp','home-enclosure-cad.webp','home-enclosure-prototypes.webp','home-enclosure-inspection.webp']) assert.ok(process.includes(name));
});

test('does not rotate at 7999 ms and rotates at the exact 8000 ms deadline', async () => {
    const f = fixture();
    await f.advance(7999); assert.equal(f.active(), 0);
    await f.advance(1); assert.equal(f.active(), 1);
    await f.advance(7999); assert.equal(f.active(), 1);
    await f.advance(1); assert.equal(f.active(), 2); f.stop();
});

test('the next eight-second readable dwell starts only after entrance settles', async () => {
    const f = fixture({ holdAnimations: true });
    await f.advance(8000); assert.equal(f.timers.size, 0);
    await f.finishAnimations(); assert.equal(f.active(), 1); assert.equal(f.timers.size, 0);
    await f.finishAnimations(); assert.equal(f.timers.size, 1);
    assert.equal([...f.timers.values()][0].delay, 8000);
    await f.advance(7999); assert.equal(f.active(), 1);
    f.stop(); await flush(); assert.equal(f.timers.size, 0);
});

test('overlapping pause reasons do not resume until every reason is cleared', () => {
    const f = fixture();
    f.root.emit('pointerenter'); f.root.emit('focusin');
    f.root.emit('pointerleave'); assert.equal(f.timers.size, 0);
    f.root.emit('focusout', { relatedTarget: null }); assert.equal(f.timers.size, 1); f.stop();
});

test('user pause survives leaving focus and hover until explicitly resumed', async () => {
    const f = fixture();
    f.toggle.emit('click');
    assert.equal(f.root.dataset.carouselUserPaused, 'true');
    assert.equal(f.toggle.textContent, 'Resume automatic slides');
    f.root.emit('pointerenter'); f.root.emit('focusin');
    f.root.emit('pointerleave'); f.root.emit('focusout', { relatedTarget: null });
    assert.equal(f.timers.size, 0);
    await f.advance(24000); assert.equal(f.active(), 0);
    f.toggle.emit('click');
    assert.equal(f.root.dataset.carouselUserPaused, 'false');
    assert.equal(f.toggle.textContent, 'Pause automatic slides');
    await f.advance(7999); assert.equal(f.active(), 0);
    await f.advance(1); assert.equal(f.active(), 1); f.stop();
});

test('user pause survives motion changes and back-forward restoration', () => {
    const f = fixture(); f.toggle.emit('click');
    f.motion.matches = true; f.motion.emit('change');
    assert.equal(f.toggle.disabled, true); assert.equal(f.timers.size, 0);
    f.motion.matches = false; f.motion.emit('change');
    assert.equal(f.toggle.disabled, false); assert.equal(f.toggle.textContent, 'Resume automatic slides');
    f.window.emit('pagehide', { persisted: true }); f.window.emit('pageshow');
    assert.equal(f.timers.size, 0); assert.equal(f.root.dataset.carouselUserPaused, 'true');
    f.toggle.emit('click'); assert.equal(f.timers.size, 1); f.stop();
});

test('missing persistent pause control keeps the readable static fallback', () => {
    const f = fixture({ missingControl: true });
    assert.equal(f.timers.size, 0); assert.equal(f.active(), 0);
    assert.equal(f.root.dataset.carouselReady, undefined);
});
