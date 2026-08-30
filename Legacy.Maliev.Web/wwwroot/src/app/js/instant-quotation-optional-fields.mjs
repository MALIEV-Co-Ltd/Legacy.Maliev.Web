const dashOnly = /^[-\u2010-\u2015\u2212]+$/;

export function normalizeOptionalQuotationFields(form) {
    ['Company', 'TaxNumber'].forEach((name) => {
        const field = form?.elements?.namedItem(name);
        if (field && dashOnly.test(String(field.value ?? '').trim())) {
            field.value = '';
        }
    });
}

export function wireOptionalQuotationFields(form) {
    if (!form) {
        return;
    }

    ['Company', 'TaxNumber'].forEach((name) => {
        form.elements?.namedItem(name)?.addEventListener?.(
            'blur',
            () => normalizeOptionalQuotationFields(form));
    });
    form.addEventListener?.('click', (event) => {
        if (event.target?.closest?.('button[type="submit"], input[type="submit"]')) {
            normalizeOptionalQuotationFields(form);
        }
    });
    form.addEventListener?.('keydown', (event) => {
        if (event.key === 'Enter') {
            normalizeOptionalQuotationFields(form);
        }
    });
}
