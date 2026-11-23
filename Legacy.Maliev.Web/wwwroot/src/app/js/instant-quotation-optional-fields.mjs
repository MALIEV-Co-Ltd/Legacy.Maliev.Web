const dashOnly = /^[-\u2010-\u2015\u2212]+$/;
const submitOwners = new WeakMap();
const initialDisabled = new WeakMap();

export function syncTaxBranch(form) {
    const tax = form?.elements?.namedItem('TaxNumber');
    const branch = form?.querySelector?.('#instant-quote-tax-branch');
    const code = form?.elements?.namedItem('TaxBranchCode');
    const branchField = form?.querySelector?.('[data-instant-tax-branch-field]');
    const codeField = form?.querySelector?.('[data-instant-tax-branch-code-field]');
    // Interactive renders can temporarily remove either control. Their absence
    // does not mean that the customer cleared their tax or branch selection.
    if (!tax || !branch) return;
    const hasTax = Boolean(String(tax?.value ?? '').trim());
    const hasBranch = hasTax && branch?.value === 'branch';

    if (branchField) branchField.hidden = !hasTax;
    if (codeField) codeField.hidden = !hasBranch;
    for (const [field, visible] of [[branch, hasTax], [code, hasBranch]]) {
        if (!field) continue;
        const busy = form.getAttribute?.('aria-busy') === 'true';
        if (!initialDisabled.has(field)) initialDisabled.set(field, Boolean(field.disabled) && !busy);
        const declaredLock = field.dataset?.serverLocked;
        const locked = declaredLock === undefined ? initialDisabled.get(field) : declaredLock === 'true';
        const logicallyDisabled = locked || !visible;
        if (field.dataset) field.dataset.optionalDisabled = logicallyDisabled ? 'true' : 'false';
        field.disabled = logicallyDisabled || busy;
    }
    if (!hasBranch && code && !code.readOnly) code.value = '';
}

// The page's existing child-list observer owns this work for its lifetime.
export function syncOptionalFieldsAfterMutations(records) {
    const forms = new Set();
    for (const record of records) {
        const form = record.target?.closest?.('form[data-optional-customer-fields]');
        if (form && form.isConnected !== false) forms.add(form);
    }
    for (const form of forms) {
        syncTaxBranch(form);
        const EventType = form.ownerDocument?.defaultView?.Event ?? Event;
        form.dispatchEvent?.(new EventType('input', { bubbles: true }));
    }
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

    // Inputs can be replaced without replacing the form during an interactive render.
    form.addEventListener?.('focusout', (event) => {
        if (event.target?.name === 'Company' || event.target?.name === 'TaxNumber') {
            normalizeOptionalQuotationFields(form);
        }
    });
    form.addEventListener?.('input', (event) => {
        if (event.target?.name === 'TaxNumber') syncTaxBranch(form);
    });
    form.addEventListener?.('change', (event) => {
        if (event.target?.id !== 'instant-quote-tax-branch') return;
        syncTaxBranch(form);
        form.dispatchEvent?.(new Event('input', { bubbles: true }));
    });
    if (submit) {
        // The external button can outlive a replaced form.
        const wasWired = submitOwners.has(submit);
        submitOwners.set(submit, form);
        if (!wasWired) {
            submit.addEventListener?.('click', () => {
                const attachedForm = submit.form ?? submitOwners.get(submit);
                if (attachedForm?.isConnected !== false) normalizeOptionalQuotationFields(attachedForm);
            });
        }
    }
    form.addEventListener?.('submit', () => normalizeOptionalQuotationFields(form), { capture: true });
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
