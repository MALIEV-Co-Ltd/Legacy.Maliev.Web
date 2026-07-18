import assert from 'node:assert/strict';
import test from 'node:test';
import {
    requestInstantEstimate,
    wireInstantEstimate,
} from '../wwwroot/src/app/js/instant-quotation-controller.mjs';

test('recalculation hides stale results until the replacement succeeds', async () => {
    const fixture = createFixture();
    const replacement = deferred();
    fixture.calculate = () => replacement.promise;
    const controller = wire(fixture);

    const submission = fixture.submit();

    assert.equal(fixture.breakdown.hidden, true);
    assert.equal(fixture.status.textContent, 'Calculating');
    assert.equal(fixture.form.attributes.get('aria-busy'), 'true');
    assert.equal(fixture.button.disabled, true);
    assert.equal(fixture.outputs.heading.textContent, 'Previous quote');

    replacement.resolve(success('PLA', 645));
    await submission;

    assert.equal(fixture.breakdown.hidden, false);
    assert.equal(fixture.outputs.heading.textContent, 'PLA · Quantity 1');
    assert.equal(fixture.outputs.grandTotal.textContent, '645.00 THB');
    assert.equal(fixture.status.textContent, 'Calculated');
    assert.equal(fixture.form.attributes.has('aria-busy'), false);
    assert.equal(fixture.button.disabled, false);
    controller.dispose();
});

test('failure keeps stale results hidden and restores the localized ready state', async () => {
    const fixture = createFixture();
    fixture.calculate = async () => { throw new Error('service unavailable'); };
    const controller = wire(fixture);

    await fixture.submit();

    assert.equal(fixture.breakdown.hidden, true);
    assert.equal(fixture.status.textContent, 'Localized failure');
    assert.equal(fixture.form.attributes.has('aria-busy'), false);
    assert.equal(fixture.button.disabled, false);
    controller.dispose();
});

test('overlapping submissions abort and suppress the late stale response', async () => {
    const fixture = createFixture();
    const first = deferred();
    const second = deferred();
    const signals = [];
    fixture.calculate = (_values, signal) => {
        signals.push(signal);
        return signals.length === 1 ? first.promise : second.promise;
    };
    const controller = wire(fixture);

    const firstSubmission = fixture.submit();
    fixture.values.material = 'M68';
    const secondSubmission = fixture.submit();

    assert.equal(signals[0].aborted, true);
    first.resolve(success('PLA', 645));
    await firstSubmission;
    assert.equal(fixture.breakdown.hidden, true);
    assert.equal(fixture.outputs.heading.textContent, 'Previous quote');
    assert.equal(fixture.button.disabled, true);

    second.resolve(success('M68', 910));
    await secondSubmission;
    assert.equal(fixture.breakdown.hidden, false);
    assert.equal(fixture.outputs.heading.textContent, 'M68 · Quantity 1');
    assert.equal(fixture.outputs.grandTotal.textContent, '910.00 THB');
    assert.equal(fixture.button.disabled, false);
    controller.dispose();
});

test('requestInstantEstimate fails closed for estimate and total endpoint failures', async () => {
    const values = defaultValues();
    const signal = new AbortController().signal;
    const failedEstimate = async () => response(false, {});

    await assert.rejects(
        requestInstantEstimate(values, signal, failedEstimate, 'https://example.test/quote'),
        /Estimate request failed/);

    const failedTotal = sequence(
        response(true, estimateResponse()),
        response(false, {}));
    await assert.rejects(
        requestInstantEstimate(values, signal, failedTotal, 'https://example.test/quote'),
        /Order total request failed/);
});

function wire(fixture) {
    const controller = wireInstantEstimate({
        form: fixture.form,
        status: fixture.status,
        breakdown: fixture.breakdown,
        submit: fixture.button,
        outputs: fixture.outputs,
        readValues: () => ({ ...fixture.values }),
        calculate: (...arguments_) => fixture.calculate(...arguments_),
    });
    fixture.submit = () => fixture.form.handler({ preventDefault() {} });
    return controller;
}

function createFixture() {
    const form = {
        attributes: new Map(),
        dataset: {
            calculating: 'Calculating',
            calculated: 'Calculated',
            failed: 'Localized failure',
            quantityLabel: 'Quantity',
            minutesLabel: 'minutes',
        },
        addEventListener(_event, handler) { this.handler = handler; },
        removeEventListener() {},
        removeAttribute(name) { this.attributes.delete(name); },
        reportValidity() { return true; },
        setAttribute(name, value) { this.attributes.set(name, value); },
    };
    return {
        form,
        status: { textContent: 'Ready' },
        breakdown: { hidden: false },
        button: { disabled: false },
        outputs: {
            heading: { textContent: 'Previous quote' },
            unitPrice: { textContent: 'old' },
            subtotal: { textContent: 'old' },
            printTime: { textContent: 'old' },
            shipping: { textContent: 'old' },
            vat: { textContent: 'old' },
            grandTotal: { textContent: 'old' },
        },
        values: defaultValues(),
        calculate: async () => success('PLA', 645),
    };
}

function defaultValues() {
    return {
        material: 'PLA',
        height: '30',
        volume: '20000',
        footprint: '400',
        quantity: '1',
        areaProfile: '',
        perimeterProfile: '',
    };
}

function success(material, finalOrderPrice) {
    return {
        values: { ...defaultValues(), material },
        estimate: estimateResponse(),
        total: { shipping: 100, vat: 42, finalOrderPrice },
        quantity: 1,
    };
}

function estimateResponse() {
    return {
        success: true,
        process: 'fdm',
        unitPrice: 500,
        subtotal: 500,
        subtotalThb: 500,
        weightGrams: 12,
        boundingCm3: 20,
        printTimeMinutes: 25,
    };
}

function response(ok, body) {
    return { ok, json: async () => body };
}

function sequence(...responses) {
    let index = 0;
    return async () => responses[index++];
}

function deferred() {
    let resolve;
    let reject;
    const promise = new Promise((resolvePromise, rejectPromise) => {
        resolve = resolvePromise;
        reject = rejectPromise;
    });
    return { promise, resolve, reject };
}
