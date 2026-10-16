const dashOnly = /^[-\u2010-\u2015\u2212]+$/;
const originalDisabled = new WeakMap();

function notifyInput(field) {
    if (typeof field?.dispatchEvent !== 'function') return;
    const EventType = field.ownerDocument?.defaultView?.Event ?? globalThis.Event;
    field.dispatchEvent(new EventType('input', { bubbles: true }));
}

export function syncOptionalQuotationTaxBranch(form) {
    const taxNumber = form?.elements?.namedItem('TaxNumber');
    const branch = form?.querySelector?.('#instant-quote-tax-branch') ?? form?.elements?.namedItem('TaxBranch');
    const code = form?.elements?.namedItem('TaxBranchCode');
    if (!taxNumber || !branch || !code) return;

    for (const field of [branch, code]) {
        if (!originalDisabled.has(field)) originalDisabled.set(field, Boolean(field.disabled));
    }
    const hasTax = String(taxNumber.value ?? '').trim().length > 0;
    const hasBranch = hasTax && branch.value === 'branch';
    const branchField = form.querySelector?.('[data-instant-tax-branch-field]');
    const codeField = form.querySelector?.('[data-instant-tax-branch-code-field]');
    if (branchField) branchField.hidden = !hasTax;
    if (codeField) codeField.hidden = !hasBranch;
    branch.disabled = !hasTax || originalDisabled.get(branch);
    code.disabled = !hasBranch || originalDisabled.get(code);
    if (!hasBranch && !code.readOnly && code.value !== '') {
        code.value = '';
        notifyInput(code);
    }
}

export function normalizeOptionalQuotationFields(form) {
    ['Company', 'TaxNumber'].forEach((name) => {
        const field = form?.elements?.namedItem(name);
        if (field && !field.readOnly && dashOnly.test(String(field.value ?? '').trim())) {
            field.value = '';
            notifyInput(field);
        }
    });
    syncOptionalQuotationTaxBranch(form);
}

export function wireOptionalQuotationFields(form, submit) {
    if (!form) {
        return;
    }

    normalizeOptionalQuotationFields(form);
    form.elements?.namedItem('TaxNumber')?.addEventListener?.('input', () => syncOptionalQuotationTaxBranch(form));
    form.elements?.namedItem('TaxNumber')?.addEventListener?.('change', () => syncOptionalQuotationTaxBranch(form));
    form.elements?.namedItem('TaxBranch')?.addEventListener?.('change', () => syncOptionalQuotationTaxBranch(form));
    submit?.addEventListener?.('click', () => normalizeOptionalQuotationFields(form));

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
