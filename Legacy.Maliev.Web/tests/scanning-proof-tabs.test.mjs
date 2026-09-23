import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

const script = await readFile(new URL('../wwwroot/src/app/js/scanning-proof-tabs.js', import.meta.url), 'utf8');

function fixture(initialHash = '') {
    const listeners = new Map();
    const panels = Array.from({ length: 3 }, (_, index) => ({
        id: ['scanning-cad-panel', 'scanning-report-panel', 'scanning-color-panel'][index],
        hidden: false,
        attributes: new Map(),
        setAttribute(name, value) { this.attributes.set(name, value); },
        contains(node) { return node === this; },
    }));
    const tabs = panels.map((panel, index) => ({
        id: ['scanning-tab-cad', 'scanning-tab-report', 'scanning-tab-color'][index],
        tabIndex: index === 0 ? 0 : -1,
        attributes: new Map([['aria-controls', panel.id]]),
        listeners: new Map(),
        getAttribute(name) { return this.attributes.get(name); },
        setAttribute(name, value) { this.attributes.set(name, String(value)); },
        addEventListener(name, callback) { this.listeners.set(name, callback); },
        focus() { this.focused = true; },
        click() { this.listeners.get('click')?.(); },
    }));
    const tablist = {
        hidden: true,
        querySelectorAll() { return tabs; },
        addEventListener(name, callback) { listeners.set(name, callback); },
    };
    const sample = {
        attributes: new Map(),
        querySelector() { return tablist; },
        setAttribute(name, value) { this.attributes.set(name, value); },
    };
    const byId = new Map([[sample.id = 'scanning-sample', sample], ...panels.map(panel => [panel.id, panel])]);
    const windowListeners = new Map();
    const window = { location: { hash: initialHash }, addEventListener(name, callback) { windowListeners.set(name, callback); } };
    const document = { getElementById(id) { return byId.get(id) ?? null; } };
    vm.runInNewContext(script, { document, window });
    return { sample, tablist, tabs, panels, listeners, window, windowListeners };
}

test('proof tabs initialize from hash and expose exactly one panel', () => {
    const view = fixture('#scanning-color-panel');
    assert.equal(view.tablist.hidden, false);
    assert.deepEqual(view.panels.map(panel => panel.hidden), [true, true, false]);
    assert.deepEqual(view.tabs.map(tab => tab.getAttribute('aria-selected')), ['false', 'false', 'true']);
    assert.equal(view.sample.attributes.has('data-proof-tabs-ready'), true);
});

test('proof tabs support keyboard, click and deep-link changes', () => {
    const view = fixture();
    const event = { target: view.tabs[0], key: 'End', preventDefault() { this.prevented = true; } };
    view.listeners.get('keydown')(event);
    assert.equal(event.prevented, true);
    assert.equal(view.tabs[2].focused, true);
    assert.deepEqual(view.panels.map(panel => panel.hidden), [true, true, false]);
    view.tabs[1].click();
    assert.deepEqual(view.panels.map(panel => panel.hidden), [true, false, true]);
    view.window.location.hash = '#scanning-cad-panel';
    view.windowListeners.get('hashchange')();
    assert.deepEqual(view.panels.map(panel => panel.hidden), [false, true, true]);
});
