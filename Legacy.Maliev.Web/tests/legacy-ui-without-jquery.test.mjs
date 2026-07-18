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
