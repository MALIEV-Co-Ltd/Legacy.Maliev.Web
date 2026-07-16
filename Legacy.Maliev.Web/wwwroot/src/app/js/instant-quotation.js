import {
    requestInstantEstimate,
    wireInstantEstimate,
} from './instant-quotation-controller.mjs';

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

function value(values, key) {
    return values.get(key)?.toString() ?? '';
}
