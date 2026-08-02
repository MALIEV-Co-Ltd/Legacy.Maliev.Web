import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

const source = await readFile(new URL('../wwwroot/src/app/js/member-order-form.js', import.meta.url), 'utf8');

function createSelect() {
    const createPlaceholder = () => ({ placeholder: true, cloneNode: createPlaceholder });
    const placeholder = createPlaceholder();
    return {
        disabled: false,
        options: [placeholder],
        replaceChildren(...items) { this.options = items; },
        append(item) { this.options.push(item); },
    };
}

test('member order material options use same-origin JSON and text-only option labels', async () => {
    let change;
    let requested;
    const material = {
        value: '20',
        addEventListener(name, handler) { if (name === 'change') change = handler; },
    };
    const color = createSelect();
    const finish = createSelect();
    const form = {
        dataset: { optionsEndpoint: '/member/orders/material-options' },
        querySelector(selector) {
            return {
                '[data-order-material]': material,
                '[data-order-color]': color,
                '[data-order-finish]': finish,
            }[selector] ?? null;
        },
    };
    const context = vm.createContext({
        document: {
            querySelectorAll: selector => selector === '[data-member-order-form]' ? [form] : [],
            createElement: () => ({ value: '', textContent: '' }),
        },
        fetch: async (url, options) => {
            requested = { url, options };
            return {
                ok: true,
                json: async () => ({
                    colors: [{ id: 30, name: '<img src=x onerror=alert(1)>' }],
                    surfaceFinishes: [{ id: 40, name: 'As printed' }],
                }),
            };
        },
        encodeURIComponent,
        Number,
        Error,
    });

    vm.runInContext(source, context);
    change();
    await new Promise(resolve => setImmediate(resolve));

    assert.equal(requested.url, '/member/orders/material-options?materialId=20');
    assert.equal(requested.options.credentials, 'same-origin');
    assert.equal(color.disabled, false);
    assert.equal(color.options[1].value, '30');
    assert.equal(color.options[1].textContent, '<img src=x onerror=alert(1)>');
    assert.equal(finish.options[1].textContent, 'As printed');
});

test('member order material options fail closed on a dependency error', async () => {
    let change;
    const material = {
        value: '20',
        addEventListener(name, handler) { if (name === 'change') change = handler; },
    };
    const color = createSelect();
    const form = {
        dataset: { optionsEndpoint: '/member/orders/material-options' },
        querySelector(selector) {
            return selector === '[data-order-material]' ? material : selector === '[data-order-color]' ? color : null;
        },
    };
    const context = vm.createContext({
        document: {
            querySelectorAll: () => [form],
            createElement: () => ({ value: '', textContent: '' }),
        },
        fetch: async () => ({ ok: false }),
        encodeURIComponent,
        Number,
        Error,
    });

    vm.runInContext(source, context);
    change();
    await new Promise(resolve => setImmediate(resolve));

    assert.equal(color.disabled, true);
    assert.equal(color.options.length, 1);
});
