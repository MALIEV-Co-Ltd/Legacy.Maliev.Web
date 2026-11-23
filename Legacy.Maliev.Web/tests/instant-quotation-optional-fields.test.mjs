import assert from 'node:assert/strict';
import test from 'node:test';

import { normalizeOptionalQuotationFields, syncOptionalFieldsAfterMutations, wireOptionalQuotationFields } from '../wwwroot/src/app/js/instant-quotation-optional-fields.mjs';

function form(company, taxNumber) {
    const fields = new Map([
        ['Company', { value: company }],
        ['TaxNumber', { value: taxNumber }],
    ]);

    return {
        elements: {
            namedItem(name) {
                return fields.get(name) ?? null;
            },
        },
    };
}

test('dash-only optional fields are cleared before browser serialization', () => {
    const quotation = form(' -- ', '—');

    normalizeOptionalQuotationFields(quotation);

    assert.equal(quotation.elements.namedItem('Company').value, '');
    assert.equal(quotation.elements.namedItem('TaxNumber').value, '');
});

test('real hyphenated values are preserved by browser normalization', () => {
    const quotation = form(' A-B Manufacturing ', '0105559123456');

    normalizeOptionalQuotationFields(quotation);

    assert.equal(quotation.elements.namedItem('Company').value, ' A-B Manufacturing ');
    assert.equal(quotation.elements.namedItem('TaxNumber').value, '0105559123456');
});

test('optional-field synchronization does not unlock a pending or server-locked branch', () => {
    const branch = { value: 'head-office', disabled: true, dataset: { serverLocked: 'false' } };
    const tax = { value: '0105559123456' };
    let busy = true;
    const quotation = {
        elements: { namedItem: name => name === 'TaxNumber' ? tax : null },
        querySelector: selector => selector === '#instant-quote-tax-branch' ? branch : null,
        getAttribute: name => name === 'aria-busy' && busy ? 'true' : null,
    };

    normalizeOptionalQuotationFields(quotation);
    assert.equal(branch.disabled, true);
    assert.equal(branch.dataset.optionalDisabled, 'false');
    busy = false;
    normalizeOptionalQuotationFields(quotation);
    assert.equal(branch.disabled, false);
    branch.dataset.serverLocked = 'true';
    normalizeOptionalQuotationFields(quotation);
    assert.equal(branch.disabled, true);
    assert.equal(branch.dataset.optionalDisabled, 'true');
    branch.dataset.serverLocked = 'false';
    normalizeOptionalQuotationFields(quotation);
    assert.equal(branch.disabled, false);
    assert.equal(branch.dataset.optionalDisabled, 'false');
});

test('replacement tax and company controls retain delegated optional-field behavior', () => {
    const handlers = new Map();
    const fields = new Map([
        ['Company', { name: 'Company', value: 'initial' }],
        ['TaxNumber', { name: 'TaxNumber', value: '' }],
        ['TaxBranchCode', { name: 'TaxBranchCode', value: '', dataset: {} }],
    ]);
    let branch = { id: 'instant-quote-tax-branch', value: 'head-office', dataset: {} };
    const branchField = {};
    const codeField = {};
    const quotation = {
        elements: { namedItem: name => fields.get(name) ?? null },
        querySelector: selector => selector === '#instant-quote-tax-branch' ? branch
            : selector === '[data-instant-tax-branch-field]' ? branchField
                : selector === '[data-instant-tax-branch-code-field]' ? codeField : null,
        getAttribute: () => null,
        addEventListener: (name, handler) => handlers.set(name, handler),
        dispatchEvent: () => true,
    };
    wireOptionalQuotationFields(quotation);

    const tax = { name: 'TaxNumber', value: '0105559123456' };
    fields.set('TaxNumber', tax);
    handlers.get('input')({ target: tax });
    assert.equal(branchField.hidden, false);
    assert.equal(branch.disabled, false);

    branch = { id: 'instant-quote-tax-branch', value: 'branch', dataset: {} };
    handlers.get('change')({ target: branch });
    assert.equal(codeField.hidden, false);
    assert.equal(fields.get('TaxBranchCode').disabled, false);

    const company = { name: 'Company', value: ' -- ' };
    fields.set('Company', company);
    handlers.get('focusout')({ target: company });
    assert.equal(company.value, '');
});

test('same-form replacements resynchronize optional controls without unlocking busy or server-locked fields', () => {
    const fields = new Map([
        ['TaxNumber', { value: '0105559123456' }],
        ['TaxBranchCode', { value: '00001', dataset: {} }],
    ]);
    let branch = { value: 'branch', dataset: { serverLocked: 'true' } };
    const branchField = {}, codeField = {};
    let busy = false, changes = 0;
    const quotation = {
        elements: { namedItem: name => fields.get(name) ?? null },
        querySelector: selector => selector === '#instant-quote-tax-branch' ? branch
            : selector === '[data-instant-tax-branch-field]' ? branchField
                : selector === '[data-instant-tax-branch-code-field]' ? codeField : null,
        getAttribute: name => name === 'aria-busy' && busy ? 'true' : null,
        dispatchEvent: () => { changes++; },
    };
    const mutation = { target: { closest: () => quotation } };
    syncOptionalFieldsAfterMutations([mutation]);
    assert.equal(branch.disabled, true);
    assert.equal(codeField.hidden, false);

    fields.set('TaxNumber', { value: '' });
    syncOptionalFieldsAfterMutations([mutation, mutation]);
    assert.equal(branchField.hidden, true);
    assert.equal(codeField.hidden, true);
    assert.equal(fields.get('TaxBranchCode').disabled, true);
    assert.equal(fields.get('TaxBranchCode').value, '');
    assert.equal(changes, 2);

    fields.set('TaxNumber', { value: '0105559123456' });
    branch = { value: 'branch', dataset: { serverLocked: 'false' } };
    busy = true;
    syncOptionalFieldsAfterMutations([mutation]);
    assert.equal(branch.disabled, true);
    assert.equal(fields.get('TaxBranchCode').disabled, true);
    assert.equal(branchField.hidden, false);
    assert.equal(codeField.hidden, false);
});

test('incomplete replacement preserves branch code until the controlling fields return', () => {
    const fields = new Map([
        ['TaxNumber', { value: '0105559123456' }],
        ['TaxBranchCode', { value: '12345', dataset: {} }],
    ]);
    let branch = { value: 'branch', dataset: {} };
    const quotation = {
        elements: { namedItem: name => fields.get(name) ?? null },
        querySelector: selector => selector === '#instant-quote-tax-branch' ? branch : null,
        getAttribute: () => null,
        dispatchEvent: () => true,
    };
    const mutation = { target: { closest: () => quotation } };

    fields.delete('TaxNumber');
    syncOptionalFieldsAfterMutations([mutation]);
    assert.equal(fields.get('TaxBranchCode').value, '12345');
    fields.set('TaxNumber', { value: '0105559123456' });
    branch = null;
    syncOptionalFieldsAfterMutations([mutation]);
    assert.equal(fields.get('TaxBranchCode').value, '12345');
    branch = { value: 'branch', dataset: {} };
    syncOptionalFieldsAfterMutations([mutation]);
    assert.equal(fields.get('TaxBranchCode').value, '12345');
    fields.get('TaxNumber').value = '';
    syncOptionalFieldsAfterMutations([mutation]);
    assert.equal(fields.get('TaxBranchCode').value, '');
});

test('reusing a submit button does not normalize a detached form', () => {
    const submit = new EventTarget();
    const detached = form('--', '');
    const active = form('--', '');
    wireOptionalQuotationFields(detached, submit);
    wireOptionalQuotationFields(active, submit);

    submit.dispatchEvent(new Event('click'));

    assert.equal(detached.elements.namedItem('Company').value, '--');
    assert.equal(active.elements.namedItem('Company').value, '');
});
