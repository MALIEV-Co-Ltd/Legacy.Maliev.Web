import assert from 'node:assert/strict';
import test from 'node:test';

import { wireInstantQuotationSubmission } from '../wwwroot/src/app/js/instant-quotation-submission.mjs';

function harness(fetchImpl, emitDiagnostic) {
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
    const submitListeners = new Map();
    const submit = { disabled: false,
        addEventListener(name, listener) { submitListeners.set(name, listener); },
        removeEventListener(name) { submitListeners.delete(name); },
    };
    const navigations = [];
    const restoreFormData = globalThis.FormData;
    globalThis.FormData = class extends Array {
        constructor() { super(); this.push(['FirstName', 'Ada']); }
    };
    const diagnostics = [];
    const dispose = wireInstantQuotationSubmission(form, submit, fetchImpl, (url) => navigations.push(url),
        emitDiagnostic === null ? undefined : emitDiagnostic ?? (payload => diagnostics.push(payload)));
    return {
        form, submit, heading, list, field, feedback, navigations, diagnostics,
        clickSubmit() { submitListeners.get('click')?.(); },
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

test('diagnostics keep exact scalar contract for each deliberate retry and validation error', async () => {
    const page = harness(async () => ({ ok: true, json: async () => ({
        outcome: 'retry', errors: ['private-customer-value'], invalidFields: ['Email'],
    }) }));
    page.form.dataset.quoteHasFiles = 'true';
    page.form.dataset.quoteFileUploadCompleted = 'true';
    try {
        await page.submitForm();
        await page.submitForm();
        assert.deepEqual(page.diagnostics, [
            { event: 'quote_submit_attempt', service: '3d_printing', has_files: true, file_upload_completed: true },
            { event: 'quote_submission_error', service: '3d_printing', failure_category: 'validation' },
            { event: 'quote_submit_attempt', service: '3d_printing', has_files: true, file_upload_completed: true },
            { event: 'quote_submission_error', service: '3d_printing', failure_category: 'validation' },
        ]);
        assert.equal(JSON.stringify(page.diagnostics).includes('private-customer-value'), false);
    } finally { page.dispose(); }
});

test('native invalid final click reports validation without an attempt or network request', async () => {
    let calls = 0;
    const page = harness(async () => { calls++; });
    page.form.checkValidity = () => false;
    try {
        page.clickSubmit();
        assert.equal(calls, 0);
        assert.deepEqual(page.diagnostics, [
            { event: 'quote_submission_error', service: '3d_printing', failure_category: 'validation' },
        ]);
    } finally { page.dispose(); }
});

test('invalid scripted submit reports validation and never attempts persistence', async () => {
    let calls = 0;
    const page = harness(async () => { calls++; });
    page.form.reportValidity = () => false;
    try {
        await page.submitForm();
        assert.equal(calls, 0);
        assert.deepEqual(page.diagnostics, [
            { event: 'quote_submission_error', service: '3d_printing', failure_category: 'validation' },
        ]);
    } finally { page.dispose(); }
});

test('network diagnostics preserve unconfirmed lock and suppress duplicate attempts', async () => {
    const page = harness(async () => { throw new Error('private-network-error'); });
    // No workflow evidence exists in the SSR fallback.
    page.form.dataset.quoteFileUploadCompleted = 'true';
    try {
        await page.submitForm();
        await page.submitForm();
        assert.deepEqual(page.diagnostics, [
            { event: 'quote_submit_attempt', service: '3d_printing', has_files: false, file_upload_completed: false },
            { event: 'quote_submission_error', service: '3d_printing', failure_category: 'network' },
        ]);
        assert.equal(page.submit.disabled, true);
    } finally { page.dispose(); }
});

test('pending and terminal submissions emit one attempt and never a conversion diagnostic', async () => {
    let finish;
    const response = new Promise(resolve => { finish = resolve; });
    const page = harness(() => response);
    try {
        const pending = page.submitForm();
        const duplicate = page.submitForm();
        finish({ ok: true, json: async () => ({ outcome: 'terminal', redirectUrl: '/InstantQuotation/3D-Printing' }) });
        await Promise.all([pending, duplicate]);
        await page.submitForm();
        assert.deepEqual(page.diagnostics, [
            { event: 'quote_submit_attempt', service: '3d_printing', has_files: false, file_upload_completed: false },
        ]);
        assert.equal(page.navigations.length, 1);
    } finally { page.dispose(); }
});

test('analytics exceptions cannot block successful submission or unlock an unknown outcome', async () => {
    const page = harness(async () => ({
        ok: true, json: async () => ({ outcome: 'terminal', redirectUrl: '/InstantQuotation/3D-Printing' }),
    }), () => { throw new Error('analytics unavailable'); });
    try {
        await page.submitForm();
        assert.equal(page.navigations.length, 1);
        assert.equal(page.submit.disabled, true);
    } finally { page.dispose(); }
});

test('default native form adapter uses the existing consent sink with no customer fields', async () => {
    const received = [];
    const page = harness(async () => ({
        ok: true, json: async () => ({ outcome: 'terminal', redirectUrl: '/InstantQuotation/3D-Printing' }),
    }), null);
    page.form.ownerDocument.defaultView = { malievAnalytics: { emit: payload => received.push(payload) } };
    try {
        await page.submitForm();
        assert.deepEqual(received, [
            { event: 'quote_submit_attempt', service: '3d_printing', has_files: false, file_upload_completed: false },
        ]);
        assert.equal(JSON.stringify(received).includes('Ada'), false);
    } finally { page.dispose(); }
});
