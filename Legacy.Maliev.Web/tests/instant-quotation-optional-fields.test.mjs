import assert from 'node:assert/strict';
import test from 'node:test';

import { normalizeOptionalQuotationFields } from '../wwwroot/src/app/js/instant-quotation-optional-fields.mjs';

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
