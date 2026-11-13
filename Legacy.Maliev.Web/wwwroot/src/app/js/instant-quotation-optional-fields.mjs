const dashOnly = /^[-\u2010-\u2015\u2212]+$/;
const initialDisabled = new WeakMap();

function syncTaxBranch(form) {
    const tax = form?.elements?.namedItem('TaxNumber');
    const branch = form?.querySelector?.('#instant-quote-tax-branch');
    const code = form?.elements?.namedItem('TaxBranchCode');
    const branchField = form?.querySelector?.('[data-instant-tax-branch-field]');
    const codeField = form?.querySelector?.('[data-instant-tax-branch-code-field]');
    const hasTax = Boolean(String(tax?.value ?? '').trim());
    const hasBranch = hasTax && branch?.value === 'branch';

    if (branchField) branchField.hidden = !hasTax;
    if (codeField) codeField.hidden = !hasBranch;
    for (const [field, visible] of [[branch, hasTax], [code, hasBranch]]) {
        if (!field) continue;
        if (!initialDisabled.has(field)) initialDisabled.set(field, field.disabled);
        field.disabled = initialDisabled.get(field) || !visible;
    }
    if (!hasBranch && code && !code.readOnly) code.value = '';
}

export function normalizeOptionalQuotationFields(form) {
    ['Company', 'TaxNumber'].forEach((name) => {
        const field = form?.elements?.namedItem(name);
        if (field && !field.readOnly && dashOnly.test(String(field.value ?? '').trim())) {
            field.value = '';
            field.dispatchEvent?.(new Event('input', { bubbles: true }));
        }
    });
    syncTaxBranch(form);
}

export function wireOptionalQuotationFields(form, submit) {
    if (!form) {
        return;
    }

    ['Company', 'TaxNumber'].forEach((name) => {
        form.elements?.namedItem(name)?.addEventListener?.(
            'blur',
            () => normalizeOptionalQuotationFields(form));
    });
    form.elements?.namedItem('TaxNumber')?.addEventListener?.('input', () => syncTaxBranch(form));
    form.querySelector?.('#instant-quote-tax-branch')?.addEventListener?.('change', () => {
        syncTaxBranch(form);
        form.dispatchEvent?.(new Event('input', { bubbles: true }));
    });
    submit?.addEventListener?.('click', () => normalizeOptionalQuotationFields(form));
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
    syncTaxBranch(form);
}
