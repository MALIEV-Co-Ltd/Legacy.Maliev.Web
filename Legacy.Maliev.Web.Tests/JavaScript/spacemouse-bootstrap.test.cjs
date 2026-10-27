const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const sourcePath = path.resolve(__dirname,
    '../../Legacy.Maliev.Web/wwwroot/src/app/js/model-viewer/spacemouse-bootstrap.module.js');
const source = fs.readFileSync(sourcePath, 'utf8')
    .replace(/^import\s+\{\s*_3Dconnexion\s*\}\s+from\s+['"][^'"]+['"];\s*/, '');
const flush = () => new Promise(resolve => setImmediate(resolve));

function runtime(options = {}) {
    const children = [], attributes = new Map(), listeners = new Map(), timers = new Map(), calls = [], requests = [];
    let nextTimer = 1;
    const toolbar = {
        querySelector() { return children.find(child => child.attributes.has('data-spacemouse-help')) || null; },
        appendChild(child) { children.push(child); }
    };
    const canvas = {
        parentElement: { querySelector: () => toolbar },
        setAttribute(name, value) { attributes.set(name, value); }
    };
    const document = {
        readyState: options.loading ? 'loading' : 'complete',
        documentElement: { lang: options.culture || 'en' },
        getElementById: () => canvas,
        createElement() { return { attributes: new Map(), setAttribute(name, value) { this.attributes.set(name, value); } }; },
        addEventListener(name, callback, settings) { listeners.set(name, { callback, settings }); }
    };
    function Driver() {}
    const window = {
        viewer: options.noViewer ? undefined : { EnableSpaceMouse: value => calls.push(value) },
        setTimeout(callback, delay) { const id = nextTimer++; timers.set(id, { callback, delay }); return id; },
        clearTimeout(id) { timers.delete(id); }
    };
    const context = vm.createContext({
        window, document, AbortController, _3Dconnexion: Driver,
        fetch(url, init) {
            requests.push({ url, init });
            return options.fetch ? options.fetch(url, init) : Promise.resolve({ ok: options.available === true });
        }
    });
    vm.runInContext(source, context, { filename: sourcePath });
    return { children, attributes, listeners, timers, calls, requests, Driver };
}

for (const available of [true, false]) {
    test('availability probe selects the optional driver without retaining its timeout; available=' + available, async () => {
        const state = runtime({ available });
        await flush();
        assert.deepEqual(state.calls, [available ? state.Driver : null]);
        assert.equal(state.requests.length, 1);
        assert.equal(state.requests[0].url, 'https://127.51.68.120:8181/3dconnexion/nlproxy');
        assert.equal(state.requests[0].init.cache, 'no-store');
        assert.equal(state.timers.size, 0);
        assert.equal(state.children.length, 1);
    });
}

test('network rejection leaves optional navigation unavailable and releases the probe timer', async () => {
    const state = runtime({ fetch: async () => { throw new Error('driver unavailable'); } });
    await flush();
    assert.deepEqual(state.calls, [null]);
    assert.equal(state.timers.size, 0);
});

test('the 1500ms abort bounds a stalled probe and keeps the help surface reachable', async () => {
    const state = runtime({ fetch: (url, { signal }) => new Promise((resolve, reject) => {
        signal.addEventListener('abort', () => reject(new Error('probe aborted')), { once: true });
    }) });
    assert.equal(state.timers.size, 1);
    const timer = [...state.timers.values()][0];
    assert.equal(timer.delay, 1500);
    timer.callback();
    await flush();
    assert.deepEqual(state.calls, [null]);
    assert.equal(state.timers.size, 0);
    assert.equal(state.children[0].href, '/help/spacemouse.html');
});

for (const [culture, label] of [['en', 'SpaceMouse help'], ['th', 'การใช้งาน SpaceMouse']]) {
    test('DOM-ready help is discoverable, localized and not duplicated; culture=' + culture, async () => {
        const state = runtime({ loading: true, culture });
        assert.equal(state.requests.length, 0);
        assert.equal(state.children.length, 0);
        const ready = state.listeners.get('DOMContentLoaded');
        assert.equal(ready.settings.once, true);
        await ready.callback();
        await ready.callback();
        assert.equal(state.children.length, 1);
        const link = state.children[0];
        assert.equal(link.href, '/help/spacemouse.html');
        assert.equal(link.target, '_blank');
        assert.equal(link.rel, 'noopener');
        assert.equal(link.title, label);
        assert.equal(link.attributes.get('aria-label'), label);
        assert.equal(link.className, 'iq-tool-btn');
        assert.equal(state.timers.size, 0);
    });
}

test('a missing viewer does not initiate a driver probe', async () => {
    const state = runtime({ noViewer: true });
    await flush();
    assert.equal(state.requests.length, 0);
    assert.equal(state.calls.length, 0);
    assert.equal(state.timers.size, 0);
    assert.equal(state.children.length, 1);
});
