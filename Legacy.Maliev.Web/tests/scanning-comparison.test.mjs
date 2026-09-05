import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

const moduleUrl = new URL('../wwwroot/src/app/js/scanning-comparison.js', import.meta.url);

class Element {
    constructor() {
        this.attributes = new Map();
        this.dataset = {};
        this.hidden = true;
        this.listeners = new Map();
        this.styleValues = new Map();
        this.style = { setProperty: (name, value) => this.styleValues.set(name, value) };
        this.pointerCapture = null;
        this.focused = false;
    }

    addEventListener(name, listener) { this.listeners.set(name, listener); }
    setAttribute(name, value) { this.attributes.set(name, String(value)); }
    getAttribute(name) { return this.attributes.get(name) ?? null; }
    removeAttribute(name) { this.attributes.delete(name); }
    contains(target) { return target === this; }
    focus() { this.focused = true; }
    setPointerCapture(id) { this.pointerCapture = id; }
    hasPointerCapture(id) { return this.pointerCapture === id; }
    releasePointerCapture() { this.pointerCapture = null; }
    getBoundingClientRect() { return { left: 100, width: 400 }; }
    dispatch(name, event = {}) {
        const normalized = {
            preventDefault() { this.defaultPrevented = true; },
            ...event,
        };
        this.listeners.get(name)?.(normalized);
        return normalized;
    }
}

const createFixture = async () => {
    const stage = new Element();
    const handle = new Element();
    const handleVisual = new Element();
    handle.querySelector = selector => selector === 'span' ? handleVisual : null;
    const controls = new Element();
    const buttons = ['compare', 'scan', 'cad', 'side'].map(mode => {
        const button = new Element();
        button.dataset.comparisonMode = mode;
        return button;
    });
    const comparison = new Element();
    comparison.dataset.mode = 'side';
    comparison.dataset.scanLabel = 'Scan';
    comparison.dataset.cadLabel = 'CAD';
    comparison.querySelector = selector => ({
        '[data-comparison-stage]': stage,
        '[data-comparison-handle]': handle,
        '[data-comparison-controls]': controls,
    })[selector] ?? null;
    comparison.querySelectorAll = selector => selector === '[data-comparison-mode]' ? buttons : [];
    const context = vm.createContext({
        document: { querySelectorAll: selector => selector === '[data-scanning-comparison]' ? [comparison] : [] },
    });
    vm.runInContext(await readFile(moduleUrl, 'utf8'), context);
    return { comparison, stage, handle, controls, buttons };
};

test('comparison initializes progressively and supports all keyboard positions and view modes', async () => {
    const { comparison, handle, controls, buttons } = await createFixture();

    assert.equal(comparison.dataset.mode, 'compare');
    assert.equal(controls.hidden, false);
    assert.equal(handle.hidden, false);
    assert.equal(handle.getAttribute('aria-valuenow'), '50');
    assert.equal(handle.getAttribute('aria-valuetext'), '50% Scan, 50% CAD');

    for (const [key, expected] of [['Home', '0'], ['End', '100'], ['ArrowLeft', '99'], ['PageDown', '89'], ['ArrowUp', '90']]) {
        const event = handle.dispatch('keydown', { key });
        assert.equal(event.defaultPrevented, true);
        assert.equal(handle.getAttribute('aria-valuenow'), expected);
    }

    buttons.find(button => button.dataset.comparisonMode === 'scan').dispatch('click');
    assert.equal(comparison.dataset.mode, 'scan');
    assert.equal(handle.hidden, true);
    assert.deepEqual(buttons.map(button => button.getAttribute('aria-pressed')), ['false', 'true', 'false', 'false']);
});

test('primary pointer dragging moves the divider only in compare mode and keeps keyboard focus', async () => {
    const { comparison, stage, handle, buttons } = await createFixture();

    const down = stage.dispatch('pointerdown', { pointerId: 7, isPrimary: true, button: 0, clientX: 200, target: stage });
    assert.equal(down.defaultPrevented, true);
    assert.equal(handle.focused, true);
    assert.equal(handle.getAttribute('data-pointer-focus'), '');
    assert.equal(handle.getAttribute('aria-valuenow'), '25');
    stage.dispatch('pointermove', { pointerId: 7, clientX: 400 });
    stage.dispatch('pointerup', { pointerId: 7, clientX: 400 });
    assert.equal(handle.getAttribute('aria-valuenow'), '75');
    assert.equal(stage.pointerCapture, null);

    buttons.find(button => button.dataset.comparisonMode === 'cad').dispatch('click');
    stage.dispatch('pointerdown', { pointerId: 8, isPrimary: true, button: 0, clientX: 120, target: stage });
    assert.equal(comparison.dataset.mode, 'cad');
    assert.equal(handle.getAttribute('aria-valuenow'), '75');
});
