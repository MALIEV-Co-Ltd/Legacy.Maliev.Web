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

function createConsentContext(initialConsent, userIdConfiguration = null, queuedEvents = [], withLoader = false) {
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
    const insertedScripts = [];
    const document = {
        querySelector: () => insertedScripts[0] ?? null,
        getElementsByTagName: () => [{ parentNode: { insertBefore: loader => insertedScripts.push(loader) } }],
        createElement: () => ({ setAttribute() {} }),
    };
    const context = vm.createContext({ console, document, insertedScripts });
    context.window = context;
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
