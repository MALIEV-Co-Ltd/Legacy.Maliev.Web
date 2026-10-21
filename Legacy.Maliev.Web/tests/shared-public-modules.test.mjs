import assert from 'node:assert/strict';
import { access, readFile } from 'node:fs/promises';
import { constants } from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';

const root = new URL('../', import.meta.url);
const sourceRoot = new URL('../wwwroot/src/app/js/', import.meta.url);
const cssRoot = new URL('../wwwroot/src/app/css/', import.meta.url);

const readSource = name => readFile(new URL(name, sourceRoot), 'utf8');

test('production entries keep native service modules in route-owned bundles', async () => {
    const shared = await readFile(new URL('../assets/app-entry.js', import.meta.url), 'utf8');
    const entries = await Promise.all([
        readFile(new URL('../assets/route-service-printing.js', import.meta.url), 'utf8'),
        readFile(new URL('../assets/route-service-finder.js', import.meta.url), 'utf8'),
        readFile(new URL('../assets/route-service-toc.js', import.meta.url), 'utf8'),
        readFile(new URL('../assets/route-service-scanning.js', import.meta.url), 'utf8'),
        readFile(new URL('../assets/route-instant-quotation.js', import.meta.url), 'utf8'),
    ]);
    const entry = entries.join('\n');

    for (const module of [
        'material-comparison.js',
        'service-finder.js',
        'service-toc.js',
        'scanning-workflow.js',
    ]) {
        assert.match(entry, new RegExp(`(?:/|\\.\\.)${module.replace('.', '\\.')}`));
    }

    assert.match(entry, /instant-quotation\.js/);
    assert.doesNotMatch(shared, /material-comparison|service-finder|service-toc|scanning-workflow|instant-quotation/);
    assert.doesNotMatch(entry, /jquery|wowjs|animate\.css/i);
});

test('shared modules preserve native DOM, accessibility, and reduced-motion contracts', async () => {
    const sources = await Promise.all([
        readSource('material-comparison.js'),
        readSource('service-finder.js'),
        readSource('service-toc.js'),
        readSource('scanning-workflow.js'),
    ]);
    const combined = sources.join('\n');

    assert.doesNotMatch(combined, /\$\(|jQuery|jquery|wowjs|animate\.css/i);
    assert.match(combined, /addEventListener/);
    assert.match(combined, /aria-(?:expanded|controls|live)/);
    assert.match(await readSource('scanning-workflow.js'), /prefers-reduced-motion/);
    assert.match(await readSource('scanning-workflow.js'), /IntersectionObserver/);
    assert.match(await readSource('service-toc.js'), /data-service-toc-list/);
    assert.match(await readSource('material-comparison.js'), /printing-material-details\.json/);
    assert.match(await readSource('service-finder.js'), /data-finder-(?:step|service|answer)/);
});

test('material detail data points only at same-origin public image assets', async () => {
    const details = JSON.parse(await readFile(new URL('../wwwroot/src/data/printing-material-details.json', import.meta.url), 'utf8'));

    assert.ok(Object.keys(details).length >= 20);
    for (const [key, detail] of Object.entries(details)) {
        assert.ok(detail.image, `${key} should provide a material image`);
        assert.match(detail.image, /^\/src\/images\/services\/printing\/materials\/[a-z0-9-]+\.webp$/);
        await access(new URL(`./wwwroot${detail.image}`, root), constants.R_OK);
    }
});

test('material detail properties keep each label and value in the same grid item', async () => {
    const source = await readSource('material-comparison.js');
    const styles = await readFile(new URL('../wwwroot/src/app/css/service-pages.css', import.meta.url), 'utf8');

    assert.match(source, /const propertyGroup = createElement\("div", "service-material-detail-property"\)/);
    assert.match(source, /propertyGroup\.append\(createElement\("dt", null, localized\(property\.label\)\)\)/);
    assert.match(source, /propertyGroup\.append\(createElement\("dd", null, localized\(property\.value\)\)\)/);
    assert.match(source, /properties\.append\(propertyGroup\)/);
    assert.match(styles, /\.service-material-detail-properties\s*>\s*\.service-material-detail-property\s*\{\s*display:\s*grid;/);
});

test('service presentation styles are bundled through deterministic route entries', async () => {
    const shared = await readFile(new URL('../assets/site-entry.css', import.meta.url), 'utf8');
    const entry = await readFile(new URL('../assets/route-services-index.css', import.meta.url), 'utf8');
    assert.match(entry, /service-finder\.css/);
    assert.match(entry, /service-pages\.css/);
    assert.doesNotMatch(shared, /service-(?:finder|pages)\.css/);
    assert.match(await readFile(new URL('service-finder.css', cssRoot), 'utf8'), /prefers-reduced-motion/);
    assert.match(await readFile(new URL('service-pages.css', cssRoot), 'utf8'), /scanning-workflow/);
});

test('scanning workflow reveals every step immediately when reduced motion is preferred', async () => {
    const classes = () => {
        const values = new Set();
        return {
            add: value => values.add(value),
            contains: value => values.has(value),
        };
    };
    const steps = Array.from({ length: 3 }, () => ({ classList: classes() }));
    const timeline = {
        dataset: {},
        classList: classes(),
        querySelectorAll: selector => selector === '[data-scanning-step]' ? steps : [],
    };
    const document = {
        documentElement: { classList: classes() },
        querySelectorAll: selector => selector === '[data-scanning-workflow]' ? [timeline] : [],
    };
    const context = vm.createContext({
        document,
        window: { matchMedia: () => ({ matches: true }) },
    });
    context.window = context;
    context.window.matchMedia = () => ({ matches: true });

    vm.runInContext(await readSource('scanning-workflow.js'), context);

    assert.equal(timeline.classList.contains('is-active'), true);
    assert.equal(timeline.dataset.scanningWorkflowRevealed, 'true');
    assert.deepEqual(steps.map(step => step.classList.contains('is-visible')), [true, true, true]);
});

for (const activation of ['pointer', 'keyboard']) {
    for (const reducedMotion of [false, true]) {
        test(`TOC ${activation} activation preserves native navigation and scrolls the focused destination after panel layout (${reducedMotion})`, async () => {
            const harness = await tocHarness({ reducedMotion });
            harness.open();
            const event = harness.activate(1, { detail: activation === 'keyboard' ? 0 : 1 });
            assert.equal(event.defaultPrevented, false);
            assert.equal(harness.toc.dataset.tocOpen, 'false');
            assert.equal(harness.targets[1].getAttribute('tabindex'), null);
            assert.deepEqual(harness.actions, []);
            assert.equal(harness.frames.size, 1);
            harness.flush();
            assert.deepEqual(harness.actions, [
                { kind: 'focus', target: 'section-2', panelOpen: false, preventScroll: true },
                { kind: 'scroll', target: 'section-2', panelOpen: false, block: 'start',
                    behavior: reducedMotion ? 'instant' : 'smooth' },
            ]);
            assert.equal(harness.targets[1].getAttribute('tabindex'), '-1');
        });
    }
}

for (const modifiers of [
    { button: 1 },
    { ctrlKey: true },
    { metaKey: true },
    { shiftKey: true },
    { altKey: true },
    { defaultPrevented: true },
]) {
    test(`TOC leaves modified or already-cancelled activation unchanged: ${JSON.stringify(modifiers)}`, async () => {
        const harness = await tocHarness();
        harness.open();
        harness.activate(1, modifiers);
        assert.equal(harness.toc.dataset.tocOpen, 'true');
        assert.equal(harness.links()[1].getAttribute('aria-current'), null);
        harness.flush();
        assert.deepEqual(harness.actions, []);
    });
}

test('TOC uses a heading-owned fragment destination without adding a positive tab stop', async () => {
    const harness = await tocHarness({ headingTarget: true });
    harness.open();
    harness.activate(1);
    harness.flush();
    assert.equal(harness.actions[0].target, 'heading-2');
    assert.equal(harness.actions[1].target, 'heading-2');
    assert.equal(harness.targets[1].getAttribute('tabindex'), '-1');
});

test('TOC abandons navigation after its destination is detached', async () => {
    const harness = await tocHarness();
    harness.open();
    harness.activate(1);
    harness.targets[1].isConnected = false;
    harness.flush();
    assert.deepEqual(harness.actions, []);
});

test('TOC cancels its owned navigation frame on pagehide', async () => {
    const harness = await tocHarness();
    harness.open();
    harness.activate(1);
    harness.pagehide();
    assert.equal(harness.frames.size, 0);
    harness.flush();
    assert.deepEqual(harness.actions, []);
});

test('TOC reopening cancels a previous activation rather than stealing focus later', async () => {
    const harness = await tocHarness();
    harness.open();
    harness.activate(1);
    harness.toggle.listeners.click({});
    harness.flush();
    assert.equal(harness.toc.dataset.tocOpen, 'true');
    assert.deepEqual(harness.actions.filter(action => action.kind === 'scroll'), []);
});

test('TOC superseding activation keeps only the latest destination frame', async () => {
    const harness = await tocHarness();
    harness.open();
    harness.activate(0);
    harness.activate(1);
    assert.equal(harness.frames.size, 1);
    harness.flush();
    assert.deepEqual(harness.actions.map(action => action.target), ['section-2', 'section-2']);
});

test('TOC resumes after back-forward cache restore and cancels the next pagehide frame', async () => {
    const harness = await tocHarness();
    harness.open();
    harness.activate(1);
    harness.pagehide();
    harness.pageshow();
    harness.open();
    harness.activate(0);
    harness.flush();
    assert.deepEqual(harness.actions.map(action => action.target), ['section-1', 'section-1']);
    harness.open();
    harness.activate(1);
    harness.pagehide();
    assert.equal(harness.frames.size, 0);
});

// Executes the real source module with an explicit frame clock. This verifies DOM/event
// sequencing only; actual browser tests remain the proof of native URL/history and scroll.
async function tocHarness({ reducedMotion = false, headingTarget = false } = {}) {
    const actions = [];
    const frames = new Map();
    const windowListeners = new Map();
    let frameId = 0;
    let toc;
    const element = (id = '') => {
        const attributes = new Map();
        const classes = new Set();
        return {
            id, children: [], listeners: {}, dataset: {}, isConnected: true, textContent: id,
            classList: {
                add: value => classes.add(value),
                remove: value => classes.delete(value),
                contains: value => classes.has(value),
            },
            setAttribute: (name, value) => attributes.set(name, value),
            getAttribute: name => attributes.get(name) ?? null,
            removeAttribute: name => { attributes.delete(name); },
            hasAttribute: name => attributes.has(name),
            addEventListener(name, callback) { this.listeners[name] = callback; },
            append(child) { this.children.push(child); },
            focus(options) {
                actions.push({ kind: 'focus', target: this.id, panelOpen: toc.dataset.tocOpen === 'true',
                    preventScroll: options?.preventScroll === true });
            },
            scrollIntoView(options) {
                actions.push({ kind: 'scroll', target: this.id, panelOpen: toc.dataset.tocOpen === 'true',
                    ...options });
            },
            getBoundingClientRect: () => ({ top: 1000, bottom: 1100, left: 0, right: 200, height: 100 }),
        };
    };
    const sections = [element('section-1'), element(headingTarget ? '' : 'section-2')];
    const headings = [element('heading-1'), element('heading-2')];
    const targets = [sections[0], headingTarget ? headings[1] : sections[1]];
    sections.forEach((section, index) => {
        section.querySelector = selector => selector === 'h2' ? headings[index] : { textContent: 'Summary' };
    });
    const page = {
        isConnected: true,
        querySelectorAll: () => sections,
    };
    const list = element();
    Object.assign(list, { scrollLeft: 0, scrollWidth: 200, clientWidth: 200, scrollTo() {} });
    const toggle = element('toggle');
    const current = element();
    const preview = element();
    const previewTitle = element();
    const previewSummary = element();
    toc = element('toc');
    toc.getBoundingClientRect = () => ({ top: 0, bottom: 50, height: 50, left: 0, right: 390 });
    toc.closest = () => page;
    toc.contains = () => true;
    toc.querySelector = selector => ({
        '[data-service-toc-list]': list,
        '[data-service-toc-toggle]': toggle,
        '[data-service-toc-current]': current,
        '[data-service-toc-preview]': preview,
        '[data-service-toc-preview-title]': previewTitle,
        '[data-service-toc-preview-summary]': previewSummary,
    })[selector];
    const document = {
        readyState: 'complete',
        querySelectorAll: () => [toc],
        createElement: () => element(),
        addEventListener() {},
    };
    const window = {
        innerHeight: 900,
        location: { hash: '' },
        history: {
            pushState() { throw new Error('TOC must retain native history.'); },
            replaceState() { throw new Error('TOC must retain native history.'); },
        },
        matchMedia: () => ({ matches: reducedMotion }),
        requestAnimationFrame(callback) { const id = ++frameId; frames.set(id, callback); return id; },
        cancelAnimationFrame: id => frames.delete(id),
        addEventListener: (name, callback) => windowListeners.set(name, callback),
    };
    vm.runInNewContext(await readSource('service-toc.js'), { document, window });
    const flush = () => {
        const pending = [...frames.entries()];
        frames.clear();
        for (const [, callback] of pending) callback();
    };
    const links = () => list.children.map(item => item.children[0]);
    flush();
    actions.length = 0;
    return {
        toc, toggle, targets, frames, actions, links, flush,
        open() { toggle.listeners.click({}); flush(); actions.length = 0; },
        activate(index, properties = {}) {
            const event = {
                button: 0, detail: 1, defaultPrevented: false,
                ctrlKey: false, metaKey: false, shiftKey: false, altKey: false,
                preventDefault() { this.defaultPrevented = true; },
                ...properties,
            };
            links()[index].listeners.click(event);
            return event;
        },
        pagehide() { windowListeners.get('pagehide')?.(); },
        pageshow() { windowListeners.get('pageshow')?.(); },
    };
}
