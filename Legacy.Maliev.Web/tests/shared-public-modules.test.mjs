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
