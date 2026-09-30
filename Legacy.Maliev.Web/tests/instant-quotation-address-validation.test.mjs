import assert from 'node:assert/strict';
import test from 'node:test';

import {
    buildingContainsAddressComponents,
    normalizeAddressForComparison,
    wireInstantQuotationAddressValidation,
} from '../wwwroot/src/app/js/instant-quotation-address-validation.mjs';

test('profile readiness gate cannot be cleared by editing otherwise valid fields', () => {
    const submit = { disabled: false };
    const form = fakeForm(new Map(), { hidden: true }, submit);
    form.dataset.profileUnavailable = 'true';
    wireInstantQuotationAddressValidation(form, submit);
    assert.equal(submit.disabled, true);
    form.dispatch('input');
    assert.equal(submit.disabled, true);
});

test('address comparison normalizes Thai punctuation and rejects two distinct repeated components', () => {
    assert.equal(normalizeAddressForComparison(' 155, ซอย วงศ์สว่าง11 '), '155ซอยวงศสวาง11');
    assert.equal(buildingContainsAddressComponents(
        'หอพักชายอุทัยวรรณ2, เลขที่ 155, ซอย วงศ์สว่าง11, แขวงวงศ์สว่าง, เขตบางซื่อ จังหวัดกรุงเทพมหานคร 10800',
        ['155, ซอย วงศ์สว่าง11', 'แขวงวงศ์สว่าง', 'บางซื่อ', 'กรุงเทพมหานคร', '10800']), true);
});

test('one repeated component remains valid for a long organization name', () => {
    assert.equal(buildingContainsAddressComponents(
        'โรงเรียนวรนารีเฉลิม จังหวัดสงขลา ในพระอุปถัมภ์สมเด็จพระเจ้าบรมวงศ์เธอ เจ้าฟ้ากัลยาณิวัฒนา กรมพระนราธิวาสราชนครินทร์ บดินทรเชษฐภคินี',
        ['เลขที่ 1 ถนนปละท่า', 'บ่อยาง', 'เมืองสงขลา', 'สงขลา', '90000']), false);
    assert.equal(buildingContainsAddressComponents(
        'อาคารบางซื่อ',
        ['', 'บางซื่อ', 'บางซื่อ', '', '']), false);
});

test('live building validation shows the localized field error and disables submit until corrected', () => {
    const fields = new Map([
        ['BillingBuilding', control('')],
        ['BillingStreet1', control('155 ซอย วงศ์สว่าง11')],
        ['BillingStreet2', control('แขวงวงศ์สว่าง')],
        ['BillingCity', control('บางซื่อ')],
        ['BillingProvince', control('กรุงเทพมหานคร')],
        ['BillingPostalCode', control('10800')],
        ['ShipToBillingAddress', control('true', true)],
        ['ShippingBuilding', control('')],
        ['ShippingStreet1', control('')],
        ['ShippingStreet2', control('')],
        ['ShippingCity', control('')],
        ['ShippingProvince', control('')],
        ['ShippingPostalCode', control('')],
    ]);
    const error = { hidden: true, textContent: '' };
    const submit = { disabled: false };
    const form = fakeForm(fields, error, submit);

    wireInstantQuotationAddressValidation(form);
    const building = fields.get('BillingBuilding');
    building.value = 'หอพักชายอุทัยวรรณ2, เลขที่ 155, ซอย วงศ์สว่าง11, แขวงวงศ์สว่าง, เขตบางซื่อ จังหวัดกรุงเทพมหานคร 10800';
    building.dispatch('input');

    assert.equal(building.validationMessage, 'ช่องนี้มีรายละเอียดที่อยู่ กรุณาย้ายไปกรอกในช่องแยกด้านล่าง');
    assert.equal(building.attributes.get('aria-invalid'), 'true');
    assert.equal(error.hidden, false);
    assert.equal(error.textContent, building.validationMessage);
    assert.equal(submit.disabled, true);

    building.value = 'โรงเรียนวรนารีเฉลิม จังหวัดสงขลา ในพระอุปถัมภ์สมเด็จพระเจ้าบรมวงศ์เธอ เจ้าฟ้ากัลยาณิวัฒนา กรมพระนราธิวาสราชนครินทร์ บดินทรเชษฐภคินี';
    building.dispatch('input');

    assert.equal(building.validationMessage, '');
    assert.equal(building.attributes.has('aria-invalid'), false);
    assert.equal(error.hidden, true);
    assert.equal(submit.disabled, false);
});

test('validation controls the submit button rendered outside the form', () => {
    const fields = new Map([
        ['FirstName', control('')],
        ['BillingBuilding', control('')],
        ['BillingStreet1', control('155 ซอย วงศ์สว่าง11')],
        ['BillingStreet2', control('แขวงวงศ์สว่าง')],
        ['BillingCity', control('บางซื่อ')],
        ['BillingProvince', control('กรุงเทพมหานคร')],
        ['BillingPostalCode', control('10800')],
        ['ShipToBillingAddress', control('true', true)],
        ['ShippingBuilding', control('')],
        ['ShippingStreet1', control('')],
        ['ShippingStreet2', control('')],
        ['ShippingCity', control('')],
        ['ShippingProvince', control('')],
        ['ShippingPostalCode', control('')],
    ]);
    const submit = { disabled: false };
    const form = fakeForm(fields, { hidden: true }, null);
    fields.get('FirstName').validationMessage = 'required';
    wireInstantQuotationAddressValidation(form, submit);

    assert.equal(submit.disabled, true);
    fields.get('FirstName').validationMessage = '';
    form.dispatch('input');
    assert.equal(submit.disabled, false);

    fields.get('BillingBuilding').value = '155 ซอย วงศ์สว่าง11 แขวงวงศ์สว่าง เขตบางซื่อ';
    fields.get('BillingBuilding').dispatch('input');

    assert.equal(submit.disabled, true);
});

test('locked populated building is preserved without applying new-entry address heuristics', () => {
    const building = control('Stored Street Bangkok');
    building.readOnly = true;
    const fields = new Map([
        ['BillingBuilding', building], ['BillingStreet1', control('Stored Street')],
        ['BillingCity', control('Bangkok')], ['ShipToBillingAddress', control('true', true)],
    ]);
    const submit = { disabled: false };
    const form = fakeForm(fields, { hidden: true }, submit);
    wireInstantQuotationAddressValidation(form, submit);
    building.dispatch('input');
    assert.equal(building.validationMessage, '');
    assert.equal(submit.disabled, false);
});

function control(value, checked = false) {
    const listeners = new Map();
    return {
        value,
        checked,
        validationMessage: '',
        attributes: new Map(),
        addEventListener(type, listener) {
            const registered = listeners.get(type) ?? [];
            registered.push(listener);
            listeners.set(type, registered);
        },
        dispatch(type) {
            (listeners.get(type) ?? []).forEach((listener) => listener({ target: this }));
        },
        setCustomValidity(message) { this.validationMessage = message; },
        setAttribute(name, attributeValue) { this.attributes.set(name, attributeValue); },
        removeAttribute(name) { this.attributes.delete(name); },
    };
}

function fakeForm(fields, error, submit) {
    const listeners = new Map();
    return {
        dataset: {
            addressValidationMessage: 'ช่องนี้มีรายละเอียดที่อยู่ กรุณาย้ายไปกรอกในช่องแยกด้านล่าง',
        },
        elements: { namedItem: (name) => fields.get(name) ?? null },
        addEventListener(type, listener) { listeners.set(type, listener); },
        dispatch(type) { listeners.get(type)?.({ target: this }); },
        querySelector(selector) {
            if (selector === '[data-address-validation-for="BillingBuilding"]') return error;
            if (selector === '[data-instant-quote-submit]') return submit;
            return null;
        },
        checkValidity() {
            return [...fields.values()].every((field) => !field.validationMessage);
        },
    };
}
