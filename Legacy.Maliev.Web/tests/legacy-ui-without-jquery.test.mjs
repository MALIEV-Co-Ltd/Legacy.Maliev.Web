import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

const appSource = await readFile(new URL('../wwwroot/src/app/js/app.js', import.meta.url), 'utf8');
const sidebarSource = await readFile(new URL('../wwwroot/src/app/js/sidebar.js', import.meta.url), 'utf8');

test('application initialization keeps tooltips and terms-gated submission without jQuery', () => {
    const checkboxListeners = new Map();
    const formListeners = new Map();
    const checkbox = {
        checked: false,
        addEventListener: (event, handler) => checkboxListeners.set(event, handler),
    };
    const submit = { disabled: false };
    const form = {
        dataset: {},
        querySelector: selector => selector === '[data-terms-checkbox]' ? checkbox : submit,
        addEventListener: (event, handler) => formListeners.set(event, handler),
    };
    const tooltip = {};
    const passwordListeners = new Map();
    const confirmationListeners = new Map();
    const password = createPasswordInput(passwordListeners);
    const confirmation = createPasswordInput(confirmationListeners);
    const passwordPair = {
        dataset: { passwordMismatch: 'Passwords do not match.' },
        querySelector: selector => selector === '[data-password-primary]' ? password : confirmation,
    };
    let tooltipInitializations = 0;
    const document = createDocument({
        '[data-bs-toggle="tooltip"]': [tooltip],
        '[data-terms-gated-form]': [form],
        '[data-password-confirmation]': [passwordPair],
    });
    const context = createBrowserContext(document, {
        bootstrap: {
            Tooltip: {
                getOrCreateInstance: element => {
                    assert.equal(element, tooltip);
                    tooltipInitializations += 1;
                },
            },
        },
    });

    vm.runInContext(appSource, context);

    assert.equal(tooltipInitializations, 1);
    assert.equal(submit.disabled, true);
    checkbox.checked = true;
    checkboxListeners.get('change')();
    assert.equal(submit.disabled, false);
    formListeners.get('submit')();
    assert.equal(form.dataset.submitting, 'true');
    assert.equal(submit.disabled, true);
    assert.equal(typeof context.SubmitFormOnce, 'function');
    assert.equal(typeof context.ScrollToTop, 'function');

    password.value = 'correct-horse';
    confirmation.value = 'wrong-battery';
    confirmationListeners.get('input')();
    assert.equal(confirmation.validationMessage, 'Passwords do not match.');
    confirmation.value = 'correct-horse';
    confirmationListeners.get('input')();
    assert.equal(confirmation.validationMessage, '');
});

test('member sidebar preserves desktop, mobile open, and mobile close states without jQuery', () => {
    const sidebar = createElement();
    const hideButton = createElement();
    const content = createElement();
    const footer = createElement();
    const document = createDocument({
        '.sidebar': [sidebar],
        '.sidebar-hide-button': [hideButton],
        '.content-area': [content],
        '.footer': [footer],
    });
    const listeners = new Map();
    const context = createBrowserContext(document, {
        innerWidth: 1300,
        addEventListener: (event, handler) => listeners.set(event, handler),
    });

    vm.runInContext(sidebarSource, context);

    assert.equal(typeof listeners.get('load'), 'function');
    assert.equal(typeof listeners.get('resize'), 'function');
    context.CheckSidebar();
    assert.equal(sidebar.hidden, false);
    assert.equal(hideButton.hidden, true);

    context.innerWidth = 800;
    context.CheckSidebar();
    assert.equal(sidebar.hidden, true);
    assert.equal(content.hidden, false);

    context.SidebarOpen();
    assert.equal(sidebar.hidden, false);
    assert.equal(sidebar.classList.contains('sidebar-mobile'), true);
    assert.equal(content.hidden, true);
    assert.equal(footer.hidden, true);

    context.SidebarClose();
    assert.equal(sidebar.hidden, true);
    assert.equal(sidebar.classList.contains('sidebar-mobile'), false);
    assert.equal(content.hidden, false);
    assert.equal(footer.hidden, false);
});

test('member workspace drawer is keyboard accessible without inline scripts', () => {
    const navigation = createElement();
    const openButton = createInteractiveElement();
    const closeButton = createInteractiveElement();
    const backdrop = createInteractiveElement();
    backdrop.hidden = true;
    const workspace = {
        querySelector: selector => ({
            '#member-navigation': navigation,
            '[data-member-navigation-open]': openButton,
            '[data-member-navigation-close]': closeButton,
            '[data-member-navigation-backdrop]': backdrop,
        })[selector] ?? null,
    };
    const documentListeners = new Map();
    const document = createDocument({ '[data-member-workspace]': [workspace] });
    document.body.classList = createElement().classList;
    document.addEventListener = (event, handler) => documentListeners.set(event, handler);

    vm.runInContext(appSource, createBrowserContext(document));
    openButton.listeners.get('click')();
    assert.equal(navigation.classList.contains('is-open'), true);
    assert.equal(openButton.attributes.get('aria-expanded'), 'true');
    assert.equal(backdrop.hidden, false);
    assert.equal(closeButton.focused, true);

    documentListeners.get('keydown')({ key: 'Escape' });
    assert.equal(navigation.classList.contains('is-open'), false);
    assert.equal(openButton.attributes.get('aria-expanded'), 'false');
    assert.equal(backdrop.hidden, true);
    assert.equal(openButton.focused, true);
});

test('enterprise recaptcha form obtains a token before native submission', async () => {
    const listeners = new Map();
    const tokenInput = { value: '' };
    let nativeSubmissions = 0;
    const form = {
        dataset: { recaptchaSiteKey: 'site-key', recaptchaAction: 'contact_submit' },
        querySelector: selector => selector === '[data-recaptcha-response]' ? tokenInput : null,
        addEventListener: (event, handler) => listeners.set(event, handler),
        checkValidity: () => true,
        submit: () => { nativeSubmissions += 1; },
    };
    const document = createDocument({
        '[data-recaptcha-enterprise-form]': [form],
    });
    const context = createBrowserContext(document, {
        grecaptcha: {
            enterprise: {
                ready: callback => callback(),
                execute: async (siteKey, options) => {
                    assert.equal(siteKey, 'site-key');
                    assert.equal(options.action, 'contact_submit');
                    return 'browser-token';
                },
            },
        },
    });

    vm.runInContext(appSource, context);
    let prevented = false;
    listeners.get('submit')({ preventDefault: () => { prevented = true; } });
    await new Promise(resolve => setImmediate(resolve));

    assert.equal(prevented, true);
    assert.equal(tokenInput.value, 'browser-token');
    assert.equal(form.dataset.recaptchaVerified, 'true');
    assert.equal(nativeSubmissions, 1);
});

test('quotation upload emits the controlled analytics event before native submission', () => {
    const listeners = new Map();
    const responseInput = { value: 'browser-token' };
    const filesInput = { files: [{ name: 'part.stl' }, { name: 'drawing.pdf' }] };
    const serviceInput = { value: 'cnc_machining' };
    const events = [];
    let nativeSubmissions = 0;
    const form = {
        dataset: {
            recaptchaSiteKey: 'site-key',
            recaptchaAction: 'submit',
            recaptchaVerified: 'true',
            uploadAnalytics: 'true',
        },
        querySelector: selector => ({
            '[data-recaptcha-response]': responseInput,
            '[data-upload-files]': filesInput,
            '[data-upload-service]': serviceInput,
        })[selector] ?? null,
        addEventListener: (event, handler) => listeners.set(event, handler),
        checkValidity: () => true,
        submit: () => { nativeSubmissions += 1; },
    };
    const document = createDocument({ '[data-recaptcha-enterprise-form]': [form] });
    const context = createBrowserContext(document, {
        malievAnalytics: { emit: event => events.push(event) },
        setTimeout: callback => callback(),
    });

    vm.runInContext(appSource, context);
    let prevented = false;
    listeners.get('submit')({ preventDefault: () => { prevented = true; } });

    assert.equal(prevented, true);
    assert.equal(nativeSubmissions, 1);
    assert.equal(events.length, 1);
    assert.equal(events[0].event, 'file_upload_start');
    assert.equal(events[0].service, 'cnc_machining');
    assert.equal(events[0].file_count, 2);
});


for (const row of [
    { name: 'native refusal', native: false, jquery: null, allowed: false, checks: ['native', 'report'] },
    { name: 'ambient jQuery refusal does not replace native validation', native: true, jquery: false, allowed: true, checks: ['native'] },
    { name: 'ambient jQuery success cannot admit native refusal', native: false, jquery: true, allowed: false, checks: ['native', 'report'] },
    { name: 'native success ignores ambient jQuery', native: true, jquery: true, allowed: true, checks: ['native'] },
    { name: 'native-only success', native: true, jquery: null, allowed: true, checks: ['native'] },
    { name: 'ambient jQuery success cannot replace absent native validation', native: null, jquery: true, allowed: false, checks: [] },
    { name: 'no validation mechanism', native: null, jquery: null, allowed: false, checks: [] },
]) {
    test(`native submit-once guard validates before locking: ${row.name}`, () => {
        const probe = createSubmissionProbe();
        probe.state.valid = row.native;
        if (row.native === null) delete probe.form.checkValidity;
        const context = createBrowserContext(probe.document);
        if (row.jquery !== null) {
            context.jQuery = form => {
                assert.equal(form, probe.form);
                return { valid: () => { probe.state.checks.push('jquery'); return row.jquery; } };
            };
            context.jQuery.fn = { valid() {} };
        }
        vm.runInContext(appSource, context);

        assert.equal(context.SubmitFormOnce('source-form', 'source-submit', ''), row.allowed);
        assert.deepEqual(probe.state.checks, row.checks);
        assert.equal(probe.state.posts, row.allowed ? 1 : 0);
        assert.equal(probe.submit.disabled, row.allowed);
        assert.equal(probe.form.dataset.submitting, row.allowed ? 'true' : undefined);
        assert.equal(probe.attributes.get('aria-busy'), row.allowed ? 'true' : undefined);
        if (row.allowed) {
            assert.equal(probe.state.lockedAtPost, true);
            assert.equal(context.SubmitFormOnce('source-form', 'source-submit', ''), false);
            assert.equal(probe.state.posts, 1);
        }
    });
}

test('enterprise recaptcha refuses to request a token when no validator exists', () => {
    const probe = createSubmissionProbe();
    delete probe.form.checkValidity;
    probe.form.dataset.recaptchaSiteKey = 'site-key';
    let tokenRequests = 0;
    const context = createBrowserContext(probe.document, {
        grecaptcha: { enterprise: {
            ready: callback => callback(),
            execute: async () => { tokenRequests += 1; return 'source-token'; },
        } },
    });
    vm.runInContext(appSource, context);
    probe.listeners.get('submit')({ preventDefault() {} });

    assert.equal(tokenRequests, 0);
    assert.equal(probe.state.posts, 0);
    assert.equal(probe.submit.disabled, false);
    assert.equal(probe.form.dataset.recaptchaPending, undefined);
});

for (const mode of ['active fallback', 'retained callback', 'active upload delay']) {
    test(`recaptcha revalidates changed fields before native POST: ${mode}`, async () => {
        const probe = createSubmissionProbe();
        probe.form.dataset.recaptchaSiteKey = 'site-key';
        probe.form.dataset.recaptchaAction = 'submit';
        let releaseToken;
        const token = new Promise(resolve => { releaseToken = resolve; });
        const delayed = [];
        let tokenRequests = 0;
        const context = createBrowserContext(probe.document, {
            grecaptcha: { enterprise: {
                ready: callback => callback(),
                execute: (siteKey, options) => {
                    assert.equal(siteKey, 'site-key');
                    assert.equal(options.action, 'submit');
                    tokenRequests += 1;
                    return token;
                },
            } },
            setTimeout: callback => delayed.push(callback),
            malievAnalytics: { emit() {} },
        });
        if (mode === 'retained callback') probe.document.querySelectorAll = () => [];
        if (mode === 'active upload delay') {
            probe.form.dataset.uploadAnalytics = 'true';
            const originalQuery = probe.form.querySelector;
            probe.form.querySelector = selector => {
                if (selector === '[data-upload-files]') return { files: [{}] };
                if (selector === '[data-upload-service]') return { value: 'custom_manufacturing' };
                return originalQuery(selector);
            };
            probe.form.requestSubmit = () => probe.listeners.get('submit')({ preventDefault() {} });
        }
        vm.runInContext(appSource, context);
        if (mode === 'retained callback') {
            context.GuardRecaptchaSubmit('source-form', 'source-submit', 'site-key', 'submit', '', null);
        }
        probe.listeners.get('submit')({ preventDefault() {} });
        assert.equal(tokenRequests, 1);
        assert.equal(probe.form.dataset.recaptchaPending, 'true');
        assert.equal(probe.submit.disabled, true);
        assert.equal(probe.state.posts, 0);

        if (mode !== 'active upload delay') probe.state.valid = false;
        releaseToken('source-token');
        await new Promise(resolve => setImmediate(resolve));
        if (mode === 'active upload delay') {
            assert.equal(delayed.length, 1);
            probe.state.valid = false;
            delayed[0]();
        }

        assert.equal(probe.state.posts, 0);
        assert.equal(probe.state.checks.at(-1), 'report');
        assert.equal(probe.tokenInput.value, '');
        assert.equal(probe.form.dataset.recaptchaPending, undefined);
        assert.equal(probe.form.dataset.recaptchaVerified, undefined);
        assert.equal(probe.form.dataset.submitting, undefined);
        assert.equal(probe.submit.disabled, false);
        assert.equal(probe.attributes.has('aria-busy'), false);
    });
}

for (const order of ['automatic then explicit', 'explicit then automatic', 'repeated automatic']) {
    test(`native single-submit registration admits the first submit: ${order}`, () => {
        const harness = createNativeSingleSubmitHarness();
        const form = harness.forms[0];
        if (order === 'explicit then automatic') harness.context.GuardSingleSubmit(form.id);
        harness.ready();
        if (order === 'automatic then explicit') harness.context.GuardSingleSubmit(form.id);
        if (order === 'repeated automatic') harness.context.initializeApplication();
        harness.context.GuardSingleSubmit(form.id);

        assert.equal(form.submit.disabled, false);
        assert.equal(form.dataset.submitting, undefined);
        assert.equal(form.attributes.has('aria-busy'), false);
        const first = form.dispatchSubmit();
        assert.equal(first.prevented, 0, 'The first native POST must remain allowed.');
        assert.equal(form.nativePosts, 1);
        assert.equal(form.dataset.submitting, 'true');
        assert.equal(form.attributes.get('aria-busy'), 'true');
        assert.equal(form.submit.disabled, false, 'Keep the native submitter and its formaction enabled.');
        const second = form.dispatchSubmit();
        assert.equal(second.prevented, 1, 'One guard must reject the duplicate exactly once.');
        assert.equal(form.nativePosts, 1);
        assert.equal(form.listeners.length, 1);
    });
}

test('native single-submit registration keeps distinct forms independent', () => {
    const harness = createNativeSingleSubmitHarness();
    const other = createNativeSingleSubmitForm('other-login');
    harness.forms.push(other);
    harness.ready();
    for (const form of harness.forms) harness.context.GuardSingleSubmit(form.id);

    const first = harness.forms[0];
    assert.equal(first.dispatchSubmit().prevented, 0);
    assert.equal(other.dataset.submitting, undefined);
    assert.equal(other.attributes.has('aria-busy'), false);
    assert.equal(other.dispatchSubmit().prevented, 0);
    assert.equal(first.dispatchSubmit().prevented, 1);
    assert.equal(other.dispatchSubmit().prevented, 1);
    assert.equal(first.nativePosts, 1);
    assert.equal(other.nativePosts, 1);
});

test('native single-submit registration admits a replacement form with the same ID', () => {
    const harness = createNativeSingleSubmitHarness();
    harness.ready();
    const original = harness.forms[0];
    assert.equal(original.dispatchSubmit().prevented, 0);
    const replacement = createNativeSingleSubmitForm(original.id);
    harness.forms[0] = replacement;
    harness.context.GuardSingleSubmit(replacement.id);
    harness.context.initializeApplication();

    assert.equal(replacement.dataset.submitting, undefined);
    assert.equal(replacement.dispatchSubmit().prevented, 0);
    assert.equal(replacement.dispatchSubmit().prevented, 1);
    assert.equal(replacement.nativePosts, 1);
    assert.equal(original.nativePosts, 1);
    assert.equal(original.listeners.length, 1);
    assert.equal(replacement.listeners.length, 1);
});

function createNativeSingleSubmitHarness() {
    const forms = [createNativeSingleSubmitForm('customer-login')];
    const readyListeners = [];
    const document = createDocument({ '[data-single-submit]': forms });
    document.readyState = 'loading';
    document.getElementById = id => forms.find(form => form.id === id) ?? null;
    document.addEventListener = (name, listener) => {
        if (name === 'DOMContentLoaded') readyListeners.push(listener);
    };
    const context = createBrowserContext(document);
    vm.runInContext(appSource, context);
    return {
        context, forms,
        ready() {
            document.readyState = 'complete';
            for (const listener of readyListeners.splice(0)) listener();
        },
    };
}

function createNativeSingleSubmitForm(id) {
    const form = {
        id, dataset: {}, attributes: new Map(), listeners: [], nativePosts: 0,
        submit: { disabled: false },
        setAttribute(name, value) { this.attributes.set(name, value); },
        querySelector() { return this.submit; },
        addEventListener(name, listener) {
            if (name === 'submit') this.listeners.push(listener);
        },
        dispatchSubmit() {
            const event = { prevented: 0, preventDefault() { this.prevented += 1; } };
            for (const listener of this.listeners) listener(event);
            if (event.prevented === 0) this.nativePosts += 1;
            return event;
        },
    };
    return form;
}

function createSubmissionProbe() {
    const listeners = new Map();
    const attributes = new Map();
    const state = { valid: true, posts: 0, lockedAtPost: false, checks: [] };
    const tokenInput = { value: '' };
    const submit = {
        disabled: false,
        setAttribute() {},
        removeAttribute() {},
    };
    const form = {
        dataset: {},
        addEventListener: (event, handler) => listeners.set(event, handler),
        querySelector: selector => selector === '[data-recaptcha-response]' ? tokenInput
            : selector === 'button[type="submit"], input[type="submit"]' ? submit : null,
        checkValidity: () => { state.checks.push('native'); return state.valid; },
        reportValidity: () => state.checks.push('report'),
        setAttribute: (name, value) => attributes.set(name, value),
        removeAttribute: name => attributes.delete(name),
        submit: () => {
            state.lockedAtPost = form.dataset.submitting === 'true' && submit.disabled;
            state.posts += 1;
        },
    };
    const document = createDocument({ '[data-recaptcha-enterprise-form]': [form] });
    document.getElementById = id => id === 'source-form' ? form : id === 'source-submit' ? submit : null;
    return { form, submit, tokenInput, document, state, listeners, attributes };
}

function createBrowserContext(document, windowOverrides = {}) {
    const context = vm.createContext({
        console,
        Date,
        document,
        FileReader: class {},
        navigator: { language: 'en-US' },
        ...windowOverrides,
    });
    context.window = context;
    return context;
}

function createDocument(elementsBySelector) {
    return {
        readyState: 'complete',
        body: { scrollTop: 0 },
        documentElement: { scrollTop: 0 },
        querySelector: selector => elementsBySelector[selector]?.[0] ?? null,
        querySelectorAll: selector => elementsBySelector[selector] ?? [],
        getElementById: () => null,
    };
}

function createElement() {
    const classes = new Set();
    return {
        hidden: false,
        classList: {
            add: value => classes.add(value),
            remove: value => classes.delete(value),
            contains: value => classes.has(value),
        },
    };
}

function createInteractiveElement() {
    const element = createElement();
    element.attributes = new Map();
    element.listeners = new Map();
    element.focused = false;
    element.addEventListener = (event, handler) => element.listeners.set(event, handler);
    element.setAttribute = (name, value) => element.attributes.set(name, value);
    element.focus = () => { element.focused = true; };
    return element;
}

function createPasswordInput(listeners) {
    return {
        value: '',
        validationMessage: '',
        addEventListener: (event, handler) => listeners.set(event, handler),
        setCustomValidity(message) {
            this.validationMessage = message;
        },
    };
}
