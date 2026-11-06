import assert from 'node:assert/strict';
import test from 'node:test';
import { createMaterialCardSelector, materialPreviewCount } from '../wwwroot/src/app/js/instant-quotation/material-card-selector.mjs';

for (const [width, height, count] of [
    [375, 799, 4], [375, 800, 6], [767, 900, 6], [1199, 1100, 6],
    [1280, 820, 6], [1600, 900, 8], [1600, 1000, 10],
]) {
    test(`source responsive preview ${width} × ${height} preserves ${count} cards`, () => {
        assert.equal(materialPreviewCount(width, height), count);
    });
}

function cardFixture() {
    const queued = new Map();
    const listeners = new Map();
    let nextFrame = 0;
    let mutation;
    let resize;
    const originalMutation = globalThis.MutationObserver;
    const originalResize = globalThis.ResizeObserver;
    globalThis.MutationObserver = class {
        constructor(callback) { mutation = callback; }
        observe() {}
        disconnect() {}
    };
    globalThis.ResizeObserver = class {
        constructor(callback) { resize = callback; }
        observe() {}
        disconnect() {}
    };
    const state = {
        selectedKey: 'A', search: '', cardHeight: 40, cardTop: 60,
        secondTop: 160, headingTop: 0,
    };
    const view = {
        innerWidth: 1600, innerHeight: 900,
        requestAnimationFrame(callback) { const id = ++nextFrame; queued.set(id, callback); return id; },
        cancelAnimationFrame(id) { queued.delete(id); },
        addEventListener(name, callback) { listeners.set('window:' + name, callback); },
        removeEventListener(name) { listeners.delete('window:' + name); },
    };
    const scroller = {
        scrollTop: 0, clientHeight: 100,
        getBoundingClientRect() { return { top: 0, bottom: this.clientHeight }; },
        addEventListener(name, callback) { listeners.set('scroll:' + name, callback); },
        removeEventListener(name) { listeners.delete('scroll:' + name); },
        querySelectorAll(selector) { return selector === '[role="radio"]' ? [first, second] : []; },
        querySelector(selector) {
            if (selector === '[aria-checked="true"]') {
                if (state.search && state.search !== state.selectedKey) return null;
                return state.selectedKey === 'A' ? first : second;
            }
            return null;
        },
    };
    const heading = {
        matches(selector) { return selector === '[data-material-category]'; },
        getBoundingClientRect() { return { top: state.headingTop - scroller.scrollTop }; },
    };
    const first = {
        dataset: { materialKey: 'A' }, previousElementSibling: heading, tabIndex: 0,
        getBoundingClientRect() {
            const top = state.cardTop - scroller.scrollTop;
            return { top, bottom: top + state.cardHeight };
        },
    };
    const second = {
        dataset: { materialKey: 'B' }, previousElementSibling: heading, tabIndex: -1,
        getBoundingClientRect() {
            const top = state.secondTop - scroller.scrollTop;
            return { top, bottom: top + 40 };
        },
    };
    const search = { get value() { return state.search; } };
    const root = {
        ownerDocument: { defaultView: view },
        querySelector(selector) {
            if (selector === '[data-material-card-scroll]') return scroller;
            if (selector === '[data-material-search]') return search;
            return null;
        },
    };
    const controller = createMaterialCardSelector(root, { invokeMethodAsync() { return Promise.resolve(); } });
    const flush = () => {
        while (queued.size) {
            const batch = [...queued.values()];
            queued.clear();
            for (const callback of batch) callback();
        }
    };
    const visible = () => {
        const box = scroller.querySelector('[aria-checked="true"]').getBoundingClientRect();
        return box.top >= -1 && box.bottom <= scroller.clientHeight + 1;
    };
    return {
        state, scroller, controller, visible, flush,
        pendingFrames() { return queued.size; },
        scrollTo(top) { scroller.scrollTop = top; listeners.get('scroll:scroll')(); },
        mutation() { mutation(); },
        resize() { resize(); },
        dispose() {
            controller.dispose();
            globalThis.MutationObserver = originalMutation;
            globalThis.ResizeObserver = originalResize;
            assert.equal(queued.size, 0, 'No deferred reveal may survive disposal');
        },
    };
}

test('same-selection price growth repairs the selected card synchronously', () => {
    const f = cardFixture();
    try {
        f.flush();
        f.state.cardHeight = 56;
        f.controller.refresh();
        assert.equal(f.visible(), true);
        assert.equal(f.scroller.scrollTop, 16);
    } finally { f.dispose(); }
});

test('new selection waits for completed layout and reveals its category heading', () => {
    const f = cardFixture();
    try {
        f.flush();
        f.state.selectedKey = 'B';
        f.scroller.clientHeight = 80;
        f.state.headingTop = 140;
        f.controller.refresh();
        assert.equal(f.scroller.scrollTop, 0, 'Selection reveal waits for the next animation frame');
        f.flush();
        assert.equal(f.scroller.scrollTop, 120);
        assert.equal(f.visible(), true);
        assert.ok(f.state.headingTop - f.scroller.scrollTop >= -1, 'Category heading remains visible');
    } finally { f.dispose(); }
});

test('same-selection price refresh preserves a deliberate scroll away', () => {
    const f = cardFixture();
    try {
        f.flush();
        f.scrollTo(120);
        f.state.cardHeight = 56;
        f.controller.refresh();
        f.flush();
        assert.equal(f.scroller.scrollTop, 120);
        assert.equal(f.visible(), false);
    } finally { f.dispose(); }
});

test('same-selection refresh never drags a still-visible card back to its heading', () => {
    const f = cardFixture();
    try {
        f.flush();
        f.scrollTo(40);
        assert.equal(f.visible(), true);
        f.controller.refresh();
        f.flush();
        assert.equal(f.scroller.scrollTop, 40);
    } finally { f.dispose(); }
});

test('DOM mutation can schedule repair without changing the selected key', () => {
    const f = cardFixture();
    try {
        f.flush();
        f.state.cardHeight = 56;
        f.mutation();
        f.flush();
        assert.equal(f.visible(), true);
        assert.equal(f.scroller.scrollTop, 16);
    } finally { f.dispose(); }
});

test('stale selection reveal is cancelled when selection changes again', () => {
    const f = cardFixture();
    try {
        f.flush();
        f.state.selectedKey = 'B';
        f.controller.refresh();
        f.state.selectedKey = 'A';
        f.controller.refresh();
        f.flush();
        assert.equal(f.state.selectedKey, 'A');
        assert.equal(f.scroller.scrollTop, 0);
    } finally { f.dispose(); }
});

test('search does not recenter the same selection and disposal cancels pending frames', () => {
    const f = cardFixture();
    try {
        f.flush();
        f.scrollTo(120);
        f.state.search = 'another material';
        f.mutation();
        f.flush();
        assert.equal(f.scroller.scrollTop, 120);
        f.state.search = 'B';
        f.state.selectedKey = 'B';
        f.controller.refresh();
    } finally { f.dispose(); }
});

test('new selection while a search is active never recenters or queues a later reveal', () => {
    const f = cardFixture();
    try {
        f.flush();
        f.scrollTo(120);
        f.state.search = 'B';
        f.state.selectedKey = 'B';
        f.controller.refresh();
        f.flush();
        assert.equal(f.scroller.scrollTop, 120);
        f.state.search = '';
        f.controller.refresh();
        f.flush();
        assert.equal(f.scroller.scrollTop, 120, 'Clearing search must not replay a stale selection reveal');
    } finally { f.dispose(); }
});

test('search starting after a new-selection frame is queued suppresses its reveal', () => {
    const f = cardFixture();
    try {
        f.flush();
        f.state.selectedKey = 'B';
        f.controller.refresh();
        assert.equal(f.scroller.scrollTop, 0);
        f.state.search = 'B';
        f.flush();
        assert.equal(f.scroller.scrollTop, 0, 'Pending frame must recheck the active search');
        f.state.search = '';
        f.controller.refresh();
        f.flush();
        assert.equal(f.scroller.scrollTop, 0, 'Clearing search must not resurrect the suppressed frame');
    } finally { f.dispose(); }
});

test('active search refresh cancels a prior selection reveal before search clears', () => {
    const f = cardFixture();
    try {
        f.flush();
        f.state.selectedKey = 'B';
        f.controller.refresh();
        assert.equal(f.pendingFrames(), 1, 'New selection has a queued reveal');
        f.state.search = 'B';
        f.controller.refresh();
        assert.equal(f.pendingFrames(), 0, 'Filtering cancels the queued reveal immediately');
        f.state.search = '';
        f.controller.refresh();
        f.flush();
        assert.equal(f.scroller.scrollTop, 0, 'Clearing search before the old frame cannot recenter');
    } finally { f.dispose(); }
});

test('selected card made visible by a price layout change does not recenter', () => {
    const f = cardFixture();
    try {
        f.flush();
        f.scrollTo(120);
        assert.equal(f.visible(), false);
        f.state.cardTop = 140;
        f.controller.refresh();
        f.flush();
        assert.equal(f.visible(), true);
        assert.equal(f.scroller.scrollTop, 120);
    } finally { f.dispose(); }
});
