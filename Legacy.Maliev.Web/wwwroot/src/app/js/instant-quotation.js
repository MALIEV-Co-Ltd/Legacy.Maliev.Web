import {
    requestInstantEstimate,
    wireInstantEstimate,
} from './instant-quotation-controller.mjs';
import { wireOptionalQuotationFields } from './instant-quotation-optional-fields.mjs';
import {
    navigateInstantQuotationValidationField,
    wireInstantQuotationAddressValidation,
} from './instant-quotation-address-validation.mjs';
import { wireInstantQuotationSubmission } from './instant-quotation-submission.mjs';

document.addEventListener('click', (event) => {
    navigateInstantQuotationValidationField(event, window.location);
});

const form = document.querySelector('[data-instant-estimate]');
const status = document.getElementById('estimate-status');
const breakdown = document.getElementById('estimate-breakdown');
const submit = form?.querySelector('button[type="submit"]');
const outputs = {
    heading: document.getElementById('estimate-heading'),
    unitPrice: document.getElementById('unit-price'),
    subtotal: document.getElementById('subtotal'),
    printTime: document.getElementById('print-time'),
    shipping: document.getElementById('shipping'),
    vat: document.getElementById('vat'),
    grandTotal: document.getElementById('grand-total'),
};

if (form instanceof HTMLFormElement
    && status
    && breakdown
    && submit instanceof HTMLButtonElement
    && Object.values(outputs).every(Boolean)) {
    wireInstantEstimate({
        form,
        status,
        breakdown,
        submit,
        outputs,
        readValues: () => {
            const values = new FormData(form);
            return {
                material: value(values, 'material'),
                height: value(values, 'height'),
                volume: value(values, 'volume'),
                footprint: value(values, 'footprint'),
                quantity: value(values, 'quantity'),
                areaProfile: value(values, 'areaProfile'),
                perimeterProfile: value(values, 'perimeterProfile'),
            };
        },
        calculate: (values, signal) =>
            requestInstantEstimate(values, signal, window.fetch.bind(window), window.location.href),
    });
}

const wiredCustomerForms = new WeakSet();
const wiredCustomerDetails = new WeakSet();

function wireCustomerValidationLinks(customerDetails) {
    if (!customerDetails || wiredCustomerDetails.has(customerDetails)) {
        return;
    }

    wiredCustomerDetails.add(customerDetails);
    customerDetails.addEventListener('click', (event) => {
        if (event.defaultPrevented || event.button !== 0
            || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) {
            return;
        }

        const link = event.target?.closest?.('a[data-instant-quote-invalid-field]');
        if (!link || !customerDetails.contains(link)
            || (link.target && link.target !== '_self') || link.hasAttribute('download')) {
            return;
        }

        let destination;
        let targetId;
        try {
            destination = new URL(link.href, window.location.href);
            targetId = decodeURIComponent(destination.hash.slice(1));
        } catch {
            return;
        }

        if (destination.origin !== window.location.origin
            || destination.pathname !== window.location.pathname
            || destination.search !== window.location.search || !targetId) {
            return;
        }

        const currentForm = customerDetails.querySelector('form[data-optional-customer-fields]');
        if (!currentForm) {
            return;
        }

        const field = Array.from(currentForm.elements)
            .find((candidate) => candidate.id === targetId);
        if (!field || field.disabled || !field.matches('input, select, textarea')) {
            return;
        }

        event.preventDefault();
        if (destination.hash !== window.location.hash) {
            window.history.pushState(window.history.state, '', destination.href);
        }
        field.focus();
    });
}

function wireCustomerForms(root) {
    const forms = [];
    if (root?.matches?.('form[data-optional-customer-fields]')) {
        forms.push(root);
    }
    root?.querySelectorAll?.('form[data-optional-customer-fields]').forEach((candidate) => forms.push(candidate));
    forms.forEach((customerForm) => {
        if (wiredCustomerForms.has(customerForm)) {
            return;
        }

        wiredCustomerForms.add(customerForm);
        wireCustomerValidationLinks(customerForm.closest('[data-workflow-customer-details-content]'));
        wireOptionalQuotationFields(customerForm);
        const customerSubmit = document.querySelector('[data-instant-quote-submit]');
        wireInstantQuotationAddressValidation(customerForm, customerSubmit);
        if (customerForm.id === 'instant-quotation-form') {
            wireInstantQuotationSubmission(
                customerForm,
                customerSubmit,
                window.fetch.bind(window),
                (url) => { window.location.href = url; });
        }
    });
}

wireCustomerForms(document);
new MutationObserver((records) => records.forEach((record) =>
    record.addedNodes.forEach((node) => wireCustomerForms(node))))
    .observe(document.documentElement, { childList: true, subtree: true });

function value(values, key) {
    return values.get(key)?.toString() ?? '';
}
