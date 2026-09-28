import assert from 'node:assert/strict';
import test from 'node:test';

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
    const field = { invalid: false, setAttribute() { this.invalid = true; } };
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
        elements: { namedItem: () => field },
        attributes: new Map(),
        reportValidity: () => true,
        setAttribute(name, value) { this.attributes.set(name, value); },
        removeAttribute(name) { this.attributes.delete(name); },
        addEventListener(name, listener) { listeners.set(name, listener); },
        removeEventListener(name) { listeners.delete(name); },
    };
    const submit = { disabled: false };
    const navigations = [];
    const restoreFormData = globalThis.FormData;
    globalThis.FormData = class extends Array {
        constructor() { super(); this.push(['FirstName', 'Ada']); }
    };
    const dispose = wireInstantQuotationSubmission(form, submit, fetchImpl, (url) => navigations.push(url));
    return {
        form, submit, heading, list, field, feedback, navigations,
        async submitForm() {
            let prevented = false;
            await listeners.get('submit')({ preventDefault() { prevented = true; } });
            assert.equal(prevented, true);
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
        assert.equal(page.feedback.focused, true);
        assert.deepEqual(page.navigations, []);
    } finally { page.dispose(); }
});
