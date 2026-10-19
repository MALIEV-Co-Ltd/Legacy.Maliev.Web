import assert from 'node:assert/strict';
import test from 'node:test';

import { wireInstantQuotationAddressValidation } from '../wwwroot/src/app/js/instant-quotation-address-validation.mjs';

import { wireInstantQuotationSubmission } from '../wwwroot/src/app/js/instant-quotation-submission.mjs';

function harness(fetchImpl) {
    const heading = { textContent: '' };
    const list = { children: [], replaceChildren(...children) { this.children = children; } };
    const feedback = {
        hidden: true,
        focused: false,
        role: null,
        setAttribute(name, value) { if (name === 'role') this.role = value; },
        focus() { this.focused = true; },
        querySelector(selector) {
            return selector === '[data-instant-quote-submit-heading]' ? heading : list;
        },
    };
    const field = { name: 'FirstName', value: 'Ada', disabled: false, invalid: false, setAttribute() { this.invalid = true; } };
    const controls = [
        field,
        { name: '__RequestVerificationToken', value: 'test-only-token', disabled: false },
        { name: 'Country', value: 'TH', disabled: true },
        { name: 'Country', value: 'TH', disabled: false },
        { name: 'Description', value: 'รายละเอียด', disabled: false },
    ];
    const listeners = new Map();
    const form = {
        action: 'https://localhost/InstantQuotation/3D-Printing?handler=SubmitRequest',
        dataset: {
            submissionRetryHeading: 'Correct customer details',
            submissionUnknownHeading: 'Do not resubmit; contact support',
            submissionGenericError: 'Correct this field',
        },
        parentElement: { querySelector: () => feedback },
        ownerDocument: { createElement: () => ({ textContent: '' }) },
        elements: { namedItem: (name) => controls.find((control) => control.name === name) ?? null },
        querySelectorAll: () => controls,
        attributes: new Map(),
        reportValidity: () => true,
        checkValidity: () => true,
        getAttribute(name) { return this.attributes.get(name) ?? null; },
        setAttribute(name, value) { this.attributes.set(name, value); },
        removeAttribute(name) { this.attributes.delete(name); },
        addEventListener(name, listener) { listeners.set(name, [...(listeners.get(name) ?? []), listener]); },
        removeEventListener(name, listener) { listeners.set(name, (listeners.get(name) ?? []).filter((current) => current !== listener)); },
    };
    const submitLabel = { hidden: false };
    const pendingLabel = { hidden: true };
    const submit = {
        disabled: false,
        querySelector(selector) {
            return selector === '[data-instant-quote-submit-label]' ? submitLabel : pendingLabel;
        },
    };
    const navigations = [];
    const restoreFormData = globalThis.FormData;
    globalThis.FormData = class extends Array {
        constructor() { super(); this.push(...controls.filter((control) => !control.disabled).map((control) => [control.name, control.value])); }
    };
    wireInstantQuotationAddressValidation(form, submit);
    const dispose = wireInstantQuotationSubmission(form, submit, fetchImpl, (url) => navigations.push(url));
    return {
        form, submit, submitLabel, pendingLabel, heading, list, field, controls, feedback, navigations,
        async submitForm() {
            let prevented = false;
            await Promise.all((listeners.get('submit') ?? []).map((listener) =>
                listener({ preventDefault() { prevented = true; } })));
            assert.equal(prevented, true);
        },
        revalidate() {
            for (const listener of listeners.get('input') ?? []) listener();
        },
        dispose() { dispose(); globalThis.FormData = restoreFormData; },
    };
}

test('safe validation retry preserves form state and re-enables submit', async () => {
    const page = harness(async () => ({
        ok: true,
        json: async () => ({ outcome: 'retry', errors: ['First name is too long'], invalidFields: ['FirstName'] }),
    }));
    try {
        await page.submitForm();
        assert.equal(page.submit.disabled, false);
        assert.equal(page.submitLabel.hidden, false);
        assert.equal(page.pendingLabel.hidden, true);
        assert.equal(page.form.attributes.has('aria-busy'), false);
        assert.equal(page.feedback.focused, true);
        assert.equal(page.feedback.role, 'alert');
        assert.equal(page.field.invalid, true);
        assert.equal(page.list.children[0].textContent, 'First name is too long');
        assert.deepEqual(page.navigations, []);
    } finally { page.dispose(); }
});

test('terminal response navigates once and prevents a duplicate submit', async () => {
    let calls = 0;
    const page = harness(async () => {
        calls++;
        return { ok: true, json: async () => ({ outcome: 'terminal', redirectUrl: '/InstantQuotation/3D-Printing' }) };
    });
    try {
        await page.submitForm();
        await page.submitForm();
        assert.equal(calls, 1);
        assert.equal(page.submit.disabled, true);
        assert.deepEqual(page.navigations, ['/InstantQuotation/3D-Printing']);
        page.revalidate();
        assert.equal(page.submit.disabled, true);
        assert.ok(page.controls.every((control) => control.disabled));
        assert.equal(page.submitLabel.hidden, true);
        assert.equal(page.pendingLabel.hidden, false);
    } finally { page.dispose(); }
});

test('unconfirmed network outcome locks submit instead of risking duplicate creation', async () => {
    let calls = 0;
    const page = harness(async () => { calls++; throw new Error('connection lost'); });
    try {
        await page.submitForm();
        await page.submitForm();
        assert.equal(calls, 1);
        assert.equal(page.submit.disabled, true);
        assert.equal(page.heading.textContent, 'Do not resubmit; contact support');
        page.revalidate();
        assert.equal(page.submit.disabled, true);
        assert.ok(page.controls.every((control) => control.disabled));
        assert.equal(page.submitLabel.hidden, false);
        assert.equal(page.pendingLabel.hidden, true);
        assert.equal(page.feedback.focused, true);
        assert.deepEqual(page.navigations, []);
    } finally { page.dispose(); }
});

test('pending submission locks fields after capturing the complete request body', async () => {
    let complete;
    let request;
    let calls = 0;
    let pending;
    const page = harness((_url, options) => {
        calls++;
        request = options;
        return new Promise((resolve) => { complete = resolve; });
    });
    try {
        pending = page.submitForm();
        assert.equal(page.submit.disabled, true);
        assert.equal(page.form.attributes.get('aria-busy'), 'true');
        page.revalidate();
        assert.equal(page.submit.disabled, true);
        assert.ok(page.controls.every((control) => control.disabled));
        assert.equal(page.submitLabel.hidden, true);
        assert.equal(page.pendingLabel.hidden, false);
        assert.equal([...request.body].length, 4);
        assert.equal(request.body.get('FirstName'), 'Ada');
        assert.equal(request.body.get('__RequestVerificationToken'), 'test-only-token');
        assert.equal(request.body.get('Country'), 'TH');
        assert.equal(request.body.get('Description'), 'รายละเอียด');
        await page.submitForm();
        assert.equal(calls, 1);
        complete({ ok: true, json: async () => ({ outcome: 'retry', errors: [], invalidFields: [] }) });
        await pending;
        assert.deepEqual(page.controls.map((control) => control.disabled), [false, false, true, false, false]);
        assert.equal(page.submit.disabled, false);
        assert.equal(page.submitLabel.hidden, false);
        assert.equal(page.pendingLabel.hidden, true);
    } finally {
        complete?.({ ok: true, json: async () => ({ outcome: 'retry' }) });
        await pending;
        page.dispose();
    }
});
