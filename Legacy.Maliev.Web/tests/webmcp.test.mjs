import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

const sourceUrl = new URL('../wwwroot/src/app/js/webmcp.js', import.meta.url);

async function loadSource() {
    return readFile(sourceUrl, 'utf8').catch(() => '');
}

async function executeWebMcp({ pathname = '/', supported = true, form = null } = {}) {
    const registrations = [];
    const listeners = new Map();
    const location = {
        origin: 'https://www.maliev.com',
        pathname,
        href: `https://www.maliev.com${pathname}`,
    };
    const document = {
        modelContext: supported
            ? {
                registerTool(tool, options) {
                    registrations.push({ tool, options });
                },
            }
            : undefined,
        querySelector(selector) {
            return selector === '#instant-quotation-form' ? form : null;
        },
    };
    const window = {
        location,
        addEventListener(name, callback) {
            listeners.set(name, callback);
        },
    };
    const context = vm.createContext({
        AbortController,
        Array,
        document,
        Error,
        Object,
        Promise,
        URL,
        window,
    });

    vm.runInContext(await loadSource(), context);
    await Promise.resolve();

    return { registrations, listeners, location };
}

test('registers only the approved public tools with closed input schemas', async () => {
    const environment = await executeWebMcp({ pathname: '/instantquotation/3d-printing' });

    assert.deepEqual(
        environment.registrations.map(entry => entry.tool.name),
        [
            'maliev.list_services',
            'maliev.open_service',
            'maliev.start_quotation',
            'maliev.review_quotation_form',
        ]);
    for (const entry of environment.registrations) {
        assert.equal(entry.tool.inputSchema.additionalProperties, false);
        assert.ok(entry.options.signal instanceof AbortSignal);
    }
});

test('lists and opens only allow-listed same-origin public routes', async () => {
    const environment = await executeWebMcp();
    const [listServices, openService, startQuotation] = environment.registrations.map(entry => entry.tool);

    const result = await listServices.execute({});
    assert.equal(result.status, 'ok');
    assert.equal(result.services.length, 8);
    assert.ok(result.services.every(service => service.url.startsWith('https://www.maliev.com/')));
    assert.equal(listServices.annotations.readOnlyHint, true);

    const navigation = await openService.execute({ service: 'cnc-machining' });
    assert.equal(navigation.status, 'navigating');
    assert.equal(navigation.url, 'https://www.maliev.com/services/cnc-machining');
    assert.equal(environment.location.href, navigation.url);
    assert.throws(() => openService.execute({ service: 'https://evil.example' }), /Unsupported service/);

    const quotation = await startQuotation.execute({ process: '3d-printing' });
    assert.equal(quotation.url, 'https://www.maliev.com/instantquotation/3d-printing');
    assert.throws(() => startQuotation.execute({ process: 'cnc-machining' }), /Unsupported quotation process/);
});

test('reviews only form completeness and never exposes field values', async () => {
    const privateValue = 'private.customer@example.com';
    const invalidControls = [
        { getAttribute: name => name === 'name' ? 'FirstName' : null, value: privateValue },
        { getAttribute: name => name === 'name' ? 'BillingPostalCode' : null, value: '10110' },
    ];
    const environment = await executeWebMcp({
        pathname: '/instantquotation/3d-printing',
        form: {
            checkValidity: () => false,
            querySelectorAll: selector => selector === ':invalid' ? invalidControls : [],
        },
    });
    const reviewTool = environment.registrations[3].tool;
    const review = await reviewTool.execute({});

    assert.equal(review.status, 'incomplete');
    assert.equal(review.formPresent, true);
    assert.equal(review.valid, false);
    assert.deepEqual(Array.from(review.missingFields), ['FirstName', 'BillingPostalCode']);
    assert.equal(JSON.stringify(review).includes(privateValue), false);
    assert.equal(reviewTool.annotations.readOnlyHint, true);
});

test('does not register review outside quotation routes and unregisters on pagehide', async () => {
    const environment = await executeWebMcp({ pathname: '/services/3d-printing' });

    assert.deepEqual(
        environment.registrations.map(entry => entry.tool.name),
        ['maliev.list_services', 'maliev.open_service', 'maliev.start_quotation']);
    const signal = environment.registrations[0].options.signal;
    environment.listeners.get('pagehide')();
    assert.equal(signal.aborted, true);

    const unsupported = await executeWebMcp({ supported: false });
    assert.equal(unsupported.registrations.length, 0);
});
