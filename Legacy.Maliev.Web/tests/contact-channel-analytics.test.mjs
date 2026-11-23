import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

const contactComponentSource = await readFile(
    new URL('../Components/Analytics/PublicContactChannelAnalytics.razor', import.meta.url),
    'utf8');
const analyticsHeadSource = await readFile(
    new URL('../Components/Analytics/PublicGoogleTagManagerHead.razor', import.meta.url),
    'utf8');
const cookieConsentSource = await readFile(
    new URL('../Components/Layout/PublicCookieConsent.razor', import.meta.url),
    'utf8');

test('LINE and Messenger clicks emit one exact four-field event on English and Thai routes', () => {
    const cases = [
        {
            href: 'https://line.me/ti/p/@maliev',
            expected: { event: 'line_click', channel: 'line', destination: 'line_oa', context: 'services' },
        },
        {
            href: 'https://m.me/maliev.manufacturing/',
            expected: { event: 'messenger_click', channel: 'messenger', destination: 'facebook_messenger', context: 'services' },
        },
    ];

    for (const locale of ['en', 'th']) {
        for (const scenario of cases) {
            const harness = createContactHarness(`/${locale}/services/cnc-machining`);
            harness.click(scenario.href);

            assert.equal(harness.events.length, 1);
            assert.deepEqual({ ...harness.events[0] }, scenario.expected);
            assert.deepEqual(Object.keys(harness.events[0]).sort(), ['channel', 'context', 'destination', 'event']);
        }
    }
});

test('WhatsApp stays fail closed unless the supported URL carries the application marker', () => {
    const harness = createContactHarness('/en/contact');

    harness.click('https://wa.me/66818030404');
    harness.click('https://api.whatsapp.com/send?phone=66818030404');
    assert.equal(harness.events.length, 0);

    harness.click('https://wa.me/66818030404', {
        'data-maliev-contact-destination': 'whatsapp_business',
    });
    assert.equal(harness.events.length, 1);
    assert.deepEqual(
        { ...harness.events[0] },
        { event: 'whatsapp_click', channel: 'whatsapp', destination: 'whatsapp_business', context: 'contact' });
});

test('unapproved Messenger and generic Facebook destinations emit no event', () => {
    const harness = createContactHarness('/th/contact');

    harness.click('https://m.me/a-different-account');
    harness.click('https://www.facebook.com/maliev.manufacturing/');

    assert.equal(harness.events.length, 0);
});

test('denied consent queues once, grant flushes after consent update, and repeated grant does not duplicate', () => {
    const context = createConsentContext('denied');
    const event = { event: 'messenger_click', channel: 'messenger', destination: 'facebook_messenger', context: 'contact' };

    context.malievAnalytics.emit(event);
    assert.equal(findEvents(context.dataLayer, 'messenger_click').length, 0);

    context.gtag('consent', 'update', deniedOrGranted('granted'));
    context.malievAnalytics.setConsent('granted');
    context.malievAnalytics.setConsent('granted');

    const emitted = findEvents(context.dataLayer, 'messenger_click');
    assert.equal(emitted.length, 1);
    assert.deepEqual({ ...emitted[0] }, event);
    assert.ok(findConsentUpdateIndex(context.dataLayer) < context.dataLayer.indexOf(emitted[0]));
});

test('reject clears a denied-consent queue so a later grant cannot flush it', () => {
    const context = createConsentContext('denied');
    context.malievAnalytics.emit({
        event: 'line_click',
        channel: 'line',
        destination: 'line_oa',
        context: 'contact',
    });

    context.gtag('consent', 'update', deniedOrGranted('denied'));
    context.malievAnalytics.setConsent('denied');
    context.gtag('consent', 'update', deniedOrGranted('granted'));
    context.malievAnalytics.setConsent('granted');

    assert.equal(findEvents(context.dataLayer, 'line_click').length, 0);
});

test('cross-tab denial revokes the bridge and cannot flush or emit events', () => {
    const context = createConsentContext('granted', { user_id: 'customer:1' });
    context.document.cookie = 'other=value; maliev_tracking_consent=denied; preference=yes';
    context.dispatchWindow('focus');
    assert.equal(context.malievAnalytics.isConsentGranted(), false);
    context.malievAnalytics.emit({ event: 'after_cross_tab_denial' });
    assert.equal(findEvents(context.dataLayer, 'after_cross_tab_denial').length, 0);
    assert.ok(context.dataLayer.some(value => value && value[0] === 'consent'
        && value[1] === 'update' && value[2].analytics_storage === 'denied'));
    assert.ok(context.dataLayer.some(value => value && value[0] === 'set' && value[1].user_id === null));
    context.document.cookie = '';
    assert.equal(context.malievAnalytics.isConsentGranted(), false);
});

test('denial present before initialization blocks stale identity and loader', () => {
    const context = createConsentContext('granted', { user_id: 'customer:1' }, [], true,
        'maliev_tracking_consent=denied');
    assert.equal(context.malievAnalytics.isConsentGranted(), false);
    assert.equal(context.insertedScripts.length, 0);
    assert.equal(context.dataLayer[0][2].analytics_storage, 'denied');
    assert.equal(context.dataLayer.some(value => value && value[0] === 'set'
        && value[1] && value[1].user_id === 'customer:1'), false);
});

test('a throwing analytics provider cannot keep local consent granted', () => {
    const context = createConsentContext('granted');
    context.document.cookie = 'maliev_tracking_consent=denied';
    context.gtag = () => { throw new Error('unavailable'); };
    assert.equal(context.malievAnalytics.isConsentGranted(), false);
    context.malievAnalytics.emit({ event: 'after_throwing_provider' });
    assert.equal(findEvents(context.dataLayer, 'after_throwing_provider').length, 0);
});

test('a refused grant does not send granted consent or load the tag manager', () => {
    const handlers = {}, updates = [];
    let closed = 0;
    const accept = { dataset: { cookieString: 'consent=yes' }, addEventListener: (_, callback) => { handlers.accept = callback; } };
    const reject = { addEventListener: (_, callback) => { handlers.reject = callback; } };
    const dialog = { querySelector: selector => selector.includes('accept') ? accept : reject,
        addEventListener() {}, showModal() {}, close() { closed++; }, remove() {} };
    let loads = 0;
    const document = { querySelector: () => dialog, documentElement: { classList: { add() {}, remove() {} } } };
    Object.defineProperty(document, 'cookie', { get: () => 'maliev_tracking_consent=denied', set() {} });
    const window = { dataLayer: [], gtag: (...args) => updates.push(args),
        malievAnalytics: { canGrant: () => false, setConsent() { throw new Error('must not grant'); } },
        malievLoadGoogleTagManager: () => { loads++; } };
    const script = cookieConsentSource.match(/<script>([\s\S]*?)<\/script>/)[1];
    vm.runInNewContext(script, { document, window });
    handlers.accept();
    handlers.accept();
    assert.equal(updates.length, 0);
    assert.equal(loads, 0);
    assert.equal(closed, 0);
});

test('a silently refused consent cookie does not grant tracking or dismiss the dialog', () => {
    const handlers = {}, updates = [];
    let closed = 0, loads = 0;
    const accept = { dataset: { cookieString: 'consent=yes; path=/' }, addEventListener: (_, callback) => { handlers.accept = callback; } };
    const reject = { addEventListener() {} };
    const dialog = { querySelector: selector => selector.includes('accept') ? accept : reject,
        addEventListener() {}, showModal() {}, close() { closed++; }, remove() {} };
    const document = { querySelector: () => dialog, documentElement: { classList: { add() {}, remove() {} } } };
    Object.defineProperty(document, 'cookie', { get: () => '', set() {} });
    const window = { dataLayer: [], gtag: (...args) => updates.push(args),
        malievAnalytics: { canGrant: () => true, setConsent() {}, isConsentGranted: () => true },
        malievLoadGoogleTagManager: () => { loads++; } };
    vm.runInNewContext(cookieConsentSource.match(/<script>([\s\S]*?)<\/script>/)[1], { document, window });
    handlers.accept();
    assert.equal(updates.length, 0);
    assert.equal(loads, 0);
    assert.equal(closed, 0);
});

test('direct tag manager loading refuses a recorded denial', () => {
    const context = createConsentContext('denied', null, [], true, 'maliev_tracking_consent=denied');
    context.malievLoadGoogleTagManager();
    assert.equal(context.insertedScripts.length, 0);
});

test('a rejected cookie write still revokes current consent and keeps the dialog open', () => {
    const handlers = {}, updates = [];
    let closed = 0;
    const accept = { dataset: {}, addEventListener() {} };
    const reject = { addEventListener: (_, callback) => { handlers.reject = callback; } };
    const dialog = { querySelector: selector => selector.includes('accept') ? accept : reject,
        addEventListener() {}, showModal() {}, close() { closed++; }, remove() {} };
    const document = { querySelector: () => dialog, documentElement: { classList: { add() {}, remove() {} } } };
    Object.defineProperty(document, 'cookie', { get: () => '', set() { throw new Error('blocked'); } });
    const window = { dataLayer: [], gtag: (...args) => updates.push(args),
        malievAnalytics: { setConsent: state => updates.push(['bridge', state]) } };
    const script = cookieConsentSource.match(/<script>([\s\S]*?)<\/script>/)[1];
    vm.runInNewContext(script, { document, window });
    handlers.reject();
    handlers.reject();
    assert.equal(updates.filter(value => value[0] === 'bridge' && value[1] === 'denied').length, 2);
    assert.equal(updates.filter(value => value[0] === 'consent' && value[2].analytics_storage === 'denied').length, 2);
    assert.equal(closed, 0);
});

test('an explicit valid grant updates consent before loading the tag manager', () => {
    const handlers = {}, order = [];
    const accept = { dataset: { cookieString: 'consent=yes' }, addEventListener: (_, callback) => { handlers.accept = callback; } };
    const reject = { addEventListener() {} };
    const dialog = { querySelector: selector => selector.includes('accept') ? accept : reject,
        addEventListener() {}, showModal() {}, close() {}, remove() {} };
    const document = { querySelector: () => dialog, cookie: '', documentElement: { classList: { add() {}, remove() {} } } };
    const window = { dataLayer: [], gtag: () => order.push('update'),
        malievAnalytics: { canGrant: () => true, setConsent: () => order.push('bridge'), isConsentGranted: () => true },
        malievLoadGoogleTagManager: () => order.push('loader') };
    const script = cookieConsentSource.match(/<script>([\s\S]*?)<\/script>/)[1];
    vm.runInNewContext(script, { document, window });
    handlers.accept();
    assert.deepEqual(order, ['update', 'bridge', 'loader']);
});

test('denial arriving during grant compensates and never loads the tag manager', () => {
    const context = createConsentContext('denied');
    context.malievAnalytics.setConsent('denied');
    const handlers = {};
    const accept = { dataset: { cookieString: 'consent=yes' }, addEventListener: (_, callback) => { handlers.accept = callback; } };
    const reject = { addEventListener() {} };
    const dialog = { querySelector: selector => selector.includes('accept') ? accept : reject,
        addEventListener() {}, showModal() {}, close() {}, remove() {} };
    context.document.querySelector = () => dialog;
    context.document.documentElement = { classList: { add() {}, remove() {} } };
    let loads = 0;
    context.malievLoadGoogleTagManager = () => { loads++; };
    const originalGtag = context.gtag;
    context.gtag = function (...args) {
        originalGtag(...args);
        if (args[0] === 'consent' && args[1] === 'update' && args[2].analytics_storage === 'granted')
            context.document.cookie = 'maliev_tracking_consent=denied';
    };
    vm.runInContext(cookieConsentSource.match(/<script>([\s\S]*?)<\/script>/)[1], context);
    handlers.accept();
    const updates = context.dataLayer.filter(value => value && value[0] === 'consent' && value[1] === 'update');
    assert.equal(updates.at(-1)[2].analytics_storage, 'denied');
    assert.equal(context.malievAnalytics.isConsentGranted(), false);
    assert.equal(loads, 0);
});

test('recorded denial rejects a pending queue and cannot be overridden by a stale grant', () => {
    const context = createConsentContext('denied');
    context.malievAnalytics.emit({ event: 'pending_before_denial' });
    context.document.cookie = 'maliev_tracking_consent=denied';
    context.malievAnalytics.setConsent('granted');
    assert.equal(context.malievAnalytics.isConsentGranted(), false);
    context.document.cookie = '';
    context.malievAnalytics.setConsent('granted');
    assert.equal(findEvents(context.dataLayer, 'pending_before_denial').length, 0);
});

test('events emitted after an explicit rejection are dropped instead of retained', () => {
    const context = createConsentContext('denied');

    context.gtag('consent', 'update', deniedOrGranted('denied'));
    context.malievAnalytics.setConsent('denied');
    context.malievAnalytics.emit({
        event: 'messenger_click',
        channel: 'messenger',
        destination: 'facebook_messenger',
        context: 'contact',
    });
    context.gtag('consent', 'update', deniedOrGranted('granted'));
    context.malievAnalytics.setConsent('granted');

    assert.equal(findEvents(context.dataLayer, 'messenger_click').length, 0);
});

test('instant quotation events retain exact payloads through consent queue, flush, and rejection', () => {
    const payloads = [
        { event: 'file_upload_start', service: '3d_printing', file_count: 2 },
        { event: 'upload_failure', service: '3d_printing', failure_category: 'validation', file_count: 1 },
        { event: 'estimate_shown', service: '3d_printing' },
        { event: 'review_reached', service: '3d_printing' },
        { event: 'request_quote', intent_type: 'quotation_request', service: '3d_printing', transaction_id: 'quotation-724', submission_status: 'persisted', has_files: true, file_upload_completed: true },
        { event: 'file_upload_complete', service: '3d_printing', transaction_id: 'quotation-724' },
    ];
    const granted = createConsentContext('denied');
    for (const payload of payloads) {
        granted.malievAnalytics.emit(payload);
        assert.equal(findEvents(granted.dataLayer, payload.event).length, 0);
    }

    granted.gtag('consent', 'update', deniedOrGranted('granted'));
    granted.malievAnalytics.setConsent('granted');
    granted.malievAnalytics.setConsent('granted');
    for (const payload of payloads) {
        const emitted = findEvents(granted.dataLayer, payload.event);
        assert.equal(emitted.length, 1);
        assert.deepEqual({ ...emitted[0] }, payload);
        assert.deepEqual(Object.keys(emitted[0]).sort(), Object.keys(payload).sort());
    }

    const rejected = createConsentContext('denied');
    for (const payload of payloads) {
        rejected.malievAnalytics.emit(payload);
    }
    rejected.gtag('consent', 'update', deniedOrGranted('denied'));
    rejected.malievAnalytics.setConsent('denied');
    rejected.gtag('consent', 'update', deniedOrGranted('granted'));
    rejected.malievAnalytics.setConsent('granted');
    for (const payload of payloads) {
        assert.equal(findEvents(rejected.dataLayer, payload.event).length, 0);
    }
});

test('consented identity and queued lead/journey precede the single ready marker and permitted loader', () => {
    const queued = [
        { event: 'maliev_lead_submitted', transaction_id: 'request-714' },
        { event: 'generate_lead', transaction_id: 'request-714' },
        { event: 'maliev_quote_decision', transaction_id: 'quotation-714' },
    ];
    const context = createConsentContext('granted', { user_id: 'customer:1' }, queued, true);
    const layer = context.dataLayer;
    assert.equal(layer[0][0], 'consent');
    assert.equal(layer[0][1], 'default');
    assert.deepEqual({ ...layer[1][1] }, { user_id: 'customer:1' });
    assert.deepEqual(Array.from(layer).filter(value => value.event).map(value => value.event),
        [...queued.map(value => value.event), 'maliev_analytics_ready', 'gtm.js']);
    const ready = findEvents(layer, 'maliev_analytics_ready');
    assert.equal(ready.length, 1);
    assert.deepEqual(Object.keys(ready[0]), ['event']);
    context.malievLoadGoogleTagManager();
    context.malievLoadGoogleTagManager();
    assert.equal(context.insertedScripts.length, 1);
    assert.equal(findEvents(layer, 'gtm.js').length, 1);
});

test('withheld identity stays withheld on first-document grant while queued events and ready flush once before loader', () => {
    const queued = [{ event: 'maliev_lead_submitted' }, { event: 'generate_lead' }, { event: 'maliev_quote_decision' }];
    const context = createConsentContext('denied', null, queued, true);
    assert.equal(context.insertedScripts.length, 0);
    assert.equal(context.dataLayer.filter(value => value.event).length, 0);
    assert.equal(context.dataLayer.filter(value => value[0] === 'set').length, 0);
    context.gtag('consent', 'update', deniedOrGranted('granted'));
    context.malievAnalytics.setConsent('granted');
    context.malievLoadGoogleTagManager();
    context.malievAnalytics.setConsent('granted');
    context.malievLoadGoogleTagManager();
    assert.deepEqual(Array.from(context.dataLayer).filter(value => value.event).map(value => value.event),
        [...queued.map(value => value.event), 'maliev_analytics_ready', 'gtm.js']);
    assert.equal(context.dataLayer.filter(value => value[0] === 'set').length, 0);
    assert.equal(context.insertedScripts.length, 1);
});

test('revocation clears identity and drops events without ready, lead, identity or loader replay on regrant', () => {
    const context = createConsentContext('granted', { user_id: 'customer:1' }, [{ event: 'generate_lead' }], true);
    context.gtag('consent', 'update', deniedOrGranted('denied'));
    context.malievAnalytics.setConsent('denied');
    context.malievAnalytics.emit({ event: 'after_revocation' });
    context.gtag('consent', 'update', deniedOrGranted('granted'));
    context.malievAnalytics.setConsent('granted');
    context.malievLoadGoogleTagManager();
    const identity = context.dataLayer.filter(value => value[0] === 'set');
    assert.equal(identity.length, 2);
    assert.deepEqual({ ...identity[0][1] }, { user_id: 'customer:1' });
    assert.deepEqual({ ...identity[1][1] }, { user_id: null });
    assert.equal(findEvents(context.dataLayer, 'after_revocation').length, 0);
    assert.equal(findEvents(context.dataLayer, 'generate_lead').length, 1);
    assert.equal(findEvents(context.dataLayer, 'maliev_analytics_ready').length, 1);
    assert.equal(findEvents(context.dataLayer, 'gtm.js').length, 1);
    assert.equal(context.insertedScripts.length, 1);
});

function createContactHarness(pathname) {
    const events = [];
    const listeners = new Map();
    class BrowserElement {}

    const document = {
        baseURI: 'https://maliev.com/',
        addEventListener: (name, handler) => listeners.set(name, handler),
    };
    const context = vm.createContext({
        console,
        document,
        Element: BrowserElement,
        URL,
        setTimeout: callback => callback(),
        location: { pathname, assign: () => {} },
        malievAnalytics: { emit: event => events.push(event) },
    });
    context.window = context;
    vm.runInContext(extractFirstScript(contactComponentSource).replaceAll('@@', '@'), context);

    return {
        events,
        click(href, attributes = {}) {
            const anchor = new BrowserElement();
            anchor.href = href;
            anchor.closest = selector => selector === 'a[href]' ? anchor : null;
            anchor.hasAttribute = name => Object.hasOwn(attributes, name);
            anchor.getAttribute = name => attributes[name] ?? (name === 'target' ? '_blank' : null);
            listeners.get('click')({
                target: anchor,
                button: 0,
                metaKey: false,
                ctrlKey: false,
                shiftKey: false,
                altKey: false,
                defaultPrevented: false,
                preventDefault: () => assert.fail('External channel links must not be intercepted.'),
            });
        },
    };
}

function createConsentContext(initialConsent, userIdConfiguration = null, queuedEvents = [], withLoader = false, initialCookie = '') {
    const identityBlock = analyticsHeadSource.match(/    @if \(Model\.CanTrack && !string\.IsNullOrEmpty\(Model\.UserIdConfigurationJson\)\)\n    \{([\s\S]*?)\n    \}/);
    assert.ok(identityBlock, 'Expected the server-gated identity block in the actual head component.');
    const identityScript = initialConsent === 'granted' && userIdConfiguration
        ? identityBlock[1].replaceAll('<text>', '').replaceAll('</text>', '')
            .replace('@((MarkupString)Model.UserIdConfigurationJson)', JSON.stringify(userIdConfiguration))
        : '';
    const queuedScript = queuedEvents.map(value => 'window.malievAnalytics.emit(' + JSON.stringify(value) + ');').join('\n');
    const script = extractFirstScript(analyticsHeadSource)
        .replace(identityBlock[0], identityScript)
        .replace("var consentState = '@Model.ConsentState';", "var consentState = '" + initialConsent + "';")
        .replace(/\s*@\(\(MarkupString\)Model\.QueuedEventScript\)\s*/, '\n' + queuedScript + '\n');
    const insertedScripts = [], windowEvents = {};
    const document = {
        cookie: initialCookie,
        addEventListener: () => {},
        querySelector: () => insertedScripts[0] ?? null,
        getElementsByTagName: () => [{ parentNode: { insertBefore: loader => insertedScripts.push(loader) } }],
        createElement: () => ({ setAttribute() {} }),
    };
    const context = vm.createContext({ console, document, insertedScripts });
    context.window = context;
    context.addEventListener = (name, callback) => { windowEvents[name] = callback; };
    context.dispatchWindow = name => windowEvents[name]();
    vm.runInContext(script, context, { timeout: 1000 });
    if (withLoader) {
        const scripts = [...analyticsHeadSource.matchAll(/<script[^>]*>([\s\S]*?)<\/script>/gi)];
        assert.equal(scripts.length, 2, 'Expected the existing distinct head bootstrap and deferred loader scripts.');
        vm.runInContext(scripts[1][1], context, { timeout: 1000 });
    }
    return context;
}

function extractFirstScript(source) {
    const match = source.match(/<script[^>]*>([\s\S]*?)<\/script>/i);
    assert.ok(match, 'Expected an inline script in the Razor component.');
    return match[1];
}

function deniedOrGranted(state) {
    return {
        ad_storage: state,
        analytics_storage: state,
        ad_user_data: state,
        ad_personalization: state,
    };
}

function findEvents(dataLayer, eventName) {
    return dataLayer.filter(value => value && value.event === eventName);
}

function findConsentUpdateIndex(dataLayer) {
    return dataLayer.findIndex(value =>
        value && typeof value.length === 'number' && value[0] === 'consent' && value[1] === 'update');
}
