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

function createConsentContext(initialConsent) {
    const script = extractFirstScript(analyticsHeadSource)
        .replace("var consentState = '@Model.ConsentState';", `var consentState = '${initialConsent}';`)
        // These existing queue cases render an anonymous model, so no identity block is emitted.
        .replace(/@if \(Model\.CanTrack && !string\.IsNullOrEmpty\(Model\.UserIdConfigurationJson\)\)\s*\{\s*<text>window\.gtag\('set', @\(\(MarkupString\)Model\.UserIdConfigurationJson\)\);<\/text>\s*\}/, '')
        .replace(/\s*@\(\(MarkupString\)Model\.QueuedEventScript\)\s*/, '\n');
    const context = vm.createContext({ console });
    context.window = context;
    vm.runInContext(script, context);
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


test('confirmed LINE friendship emits only the four public conversion fields and never reads a profile', async () => {
    const bridgeSource = await readFile(
        new URL('../Components/Pages/Contact/LineContactPage.razor', import.meta.url), 'utf8');
    const events = [];
    let profileReads = 0;
    let tokenReads = 0;
    const script = bridgeSource.match(/<script>([\s\S]*?)<\/script>/)[1]
        .replace('var liffId = @((MarkupString)JsonSerializer.Serialize(liffId));', 'var liffId = "1234567890-TestBridge";')
        .replace(/@T\("(?:[^"\\]|\\.)*",\s*"(?:[^"\\]|\\.)*"\)/g, 'fixture status');
    const context = vm.createContext({
        document: { getElementById: () => ({ textContent: '' }), body: { dataset: {} } },
        location: { href: 'https://maliev.com/contact/line' },
        malievAnalytics: { emit: event => events.push({ ...event }) },
        liff: {
            init: async () => {}, isLoggedIn: () => true,
            getFriendship: async () => ({ friendFlag: true, userId: 'private-line-subject', email: 'private@example.test', displayName: 'Private Name', accessToken: 'private-token' }),
            getProfile: () => { profileReads++; return { userId: 'private-line-subject' }; },
            getAccessToken: () => { tokenReads++; return 'private-token'; },
        },
    });
    context.window = context;
    vm.runInContext(script, context);
    await new Promise(resolve => setImmediate(resolve));
    assert.deepEqual(events, [{ event: 'line_friend_confirmed', channel: 'line', destination: 'line_oa', conversion_tier: 'secondary' }]);
    assert.equal(JSON.stringify(events).includes('private'), false);
    assert.equal(profileReads, 0, 'Friendship confirmation must not request a LINE profile.');
    assert.equal(tokenReads, 0, 'Friendship conversion must not request a LINE token.');
});

test('an unconfirmed LINE friendship emits no conversion even with provider identity fields', async () => {
    const bridgeSource = await readFile(
        new URL('../Components/Pages/Contact/LineContactPage.razor', import.meta.url), 'utf8');
    const events = [];
    const script = bridgeSource.match(/<script>([\s\S]*?)<\/script>/)[1]
        .replace('var liffId = @((MarkupString)JsonSerializer.Serialize(liffId));', 'var liffId = "1234567890-TestBridge";')
        .replace(/@T\("(?:[^"\\]|\\.)*",\s*"(?:[^"\\]|\\.)*"\)/g, 'fixture status');
    const context = vm.createContext({
        document: { getElementById: () => ({ textContent: '' }), body: { dataset: {} } },
        location: { href: 'https://maliev.com/contact/line' },
        malievAnalytics: { emit: event => events.push(event) },
        liff: { init: async () => {}, isLoggedIn: () => true,
            getFriendship: async () => ({ friendFlag: false, userId: 'private-line-subject' }) },
    });
    context.window = context;
    vm.runInContext(script, context);
    await new Promise(resolve => setImmediate(resolve));
    assert.deepEqual(events, []);
});

test('consent revocation clears identity explicitly and does not publish later LINE events', () => {
    const context = createConsentContext('granted');
    context.malievAnalytics.setConsent('denied');
    context.malievAnalytics.emit({ event: 'line_friend_confirmed', channel: 'line', destination: 'line_oa', conversion_tier: 'secondary' });
    const clearing = context.dataLayer.filter(value => value && value[0] === 'set');
    assert.equal(clearing.length, 1);
    assert.deepEqual({ ...clearing[0][1] }, { user_id: null });
    assert.equal(findEvents(context.dataLayer, 'line_friend_confirmed').length, 0);
});
