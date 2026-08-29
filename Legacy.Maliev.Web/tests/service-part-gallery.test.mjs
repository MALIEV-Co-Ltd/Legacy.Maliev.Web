import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

const root = new URL('../', import.meta.url);

test('printing route owns the production part gallery module', async () => {
    const entry = await readFile(new URL('../assets/route-service-printing.js', import.meta.url), 'utf8');
    assert.match(entry, /service-part-gallery\.js/);
});

test('gallery expansion loads deferred responsive images and exposes all tiles once', async () => {
    const listeners = new Map();

    class Element {
        constructor() {
            this.attributes = new Map();
            this.dataset = {};
            this.hidden = true;
        }

        setAttribute(name, value) { this.attributes.set(name, value); }
        getAttribute(name) { return this.attributes.get(name) ?? null; }
        removeAttribute(name) {
            this.attributes.delete(name);
            if (name === 'data-src') delete this.dataset.src;
            if (name === 'data-srcset') delete this.dataset.srcset;
        }
    }

    class Button extends Element {
        addEventListener(name, listener) { listeners.set(name, listener); }
    }

    const images = Array.from({ length: 12 }, (_, index) => {
        const image = new Element();
        image.dataset.src = `/part-${index}.webp`;
        image.dataset.srcset = `/part-${index}-640.webp 640w, /part-${index}.webp 1536w`;
        return image;
    });
    const tiles = Array.from({ length: 12 }, () => new Element());
    const toggle = new Button();
    toggle.hidden = false;
    toggle.setAttribute('aria-controls', 'printing-part-gallery-extra');
    toggle.setAttribute('aria-expanded', 'false');
    const gallery = new Element();
    gallery.querySelectorAll = selector => selector === '[data-part-gallery-extra]' ? tiles : images;
    const controls = new Element();

    const context = vm.createContext({
        HTMLButtonElement: Button,
        HTMLElement: Element,
        document: {
            querySelector: selector => selector === '[data-part-gallery-toggle]' ? toggle : controls,
            getElementById: id => id === 'printing-part-gallery-extra' ? gallery : null,
        },
    });

    const source = await readFile(new URL('../wwwroot/src/app/js/service-part-gallery.js', import.meta.url), 'utf8');
    vm.runInContext(source, context);

    assert.equal(controls.hidden, false);
    assert.deepEqual(tiles.map(tile => tile.getAttribute('aria-hidden')), Array(12).fill('true'));
    listeners.get('click')();
    assert.equal(gallery.hidden, false);
    assert.equal(toggle.hidden, true);
    assert.equal(toggle.getAttribute('aria-expanded'), 'true');
    assert.deepEqual(tiles.map(tile => tile.getAttribute('aria-hidden')), Array(12).fill('false'));
    assert.deepEqual(images.map(image => image.src), images.map((_, index) => `/part-${index}.webp`));
    assert.deepEqual(images.map(image => image.srcset), images.map((_, index) => `/part-${index}-640.webp 640w, /part-${index}.webp 1536w`));
    assert.deepEqual(images.map(image => image.dataset), Array.from({ length: 12 }, () => ({})));
});
