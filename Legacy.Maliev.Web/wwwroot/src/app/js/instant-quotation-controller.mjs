export function wireInstantEstimate({
    form,
    status,
    breakdown,
    submit,
    outputs,
    readValues,
    calculate,
}) {
    let activeRequest;

    const handleSubmit = async event => {
        event.preventDefault();
        if (!form.reportValidity()) {
            return;
        }

        activeRequest?.abort();
        const request = new AbortController();
        activeRequest = request;
        breakdown.hidden = true;
        form.setAttribute('aria-busy', 'true');
        submit.disabled = true;
        status.textContent = form.dataset.calculating ?? '';
        const values = readValues();

        try {
            const result = await calculate(values, request.signal);
            if (activeRequest !== request) {
                return;
            }

            outputs.heading.textContent =
                `${result.values.material} · ${form.dataset.quantityLabel ?? ''} ${result.quantity}`;
            outputs.unitPrice.textContent = money(result.estimate.unitPrice);
            outputs.subtotal.textContent = money(result.estimate.subtotal);
            outputs.printTime.textContent =
                `${Number(result.estimate.printTimeMinutes).toLocaleString()} ${form.dataset.minutesLabel ?? ''}`;
            outputs.shipping.textContent = money(result.total.shipping);
            outputs.vat.textContent = money(result.total.vat);
            outputs.grandTotal.textContent = money(result.total.finalOrderPrice);
            breakdown.hidden = false;
            status.textContent = form.dataset.calculated ?? '';
        } catch (error) {
            if (activeRequest !== request || isAbortError(error)) {
                return;
            }

            breakdown.hidden = true;
            status.textContent = form.dataset.failed ?? '';
        } finally {
            if (activeRequest === request) {
                activeRequest = undefined;
                form.removeAttribute('aria-busy');
                submit.disabled = false;
            }
        }
    };

    form.addEventListener('submit', handleSubmit);
    return {
        dispose() {
            activeRequest?.abort();
            form.removeEventListener('submit', handleSubmit);
        },
    };
}

export async function requestInstantEstimate(values, signal, fetchImpl, locationHref) {
    const estimateQuery = new URLSearchParams({
        handler: 'GetEstimate',
        material: values.material,
        dimensionZ: values.height,
        volume: values.volume,
        footprint: values.footprint,
        quantity: values.quantity,
        areaProfile: values.areaProfile,
        perimeterProfile: values.perimeterProfile,
        currency: 'THB',
    });
    const estimateResponse = await fetchImpl(endpoint(locationHref, estimateQuery), {
        headers: { Accept: 'application/json' },
        signal,
    });
    if (!estimateResponse.ok) {
        throw new Error('Estimate request failed');
    }

    const estimate = await estimateResponse.json();
    if (!estimate.success) {
        throw new Error('This material cannot be estimated');
    }

    const quantity = Number(values.quantity);
    const totalQuery = new URLSearchParams({
        handler: 'GetOrderTotal',
        processes: estimate.process,
        subtotals: estimate.subtotalThb,
        totalWeightGrams: estimate.weightGrams * quantity,
        totalBoundingCm3: estimate.boundingCm3 * quantity,
        currency: 'THB',
    });
    const totalResponse = await fetchImpl(endpoint(locationHref, totalQuery), {
        headers: { Accept: 'application/json' },
        signal,
    });
    if (!totalResponse.ok) {
        throw new Error('Order total request failed');
    }

    return {
        values,
        estimate,
        total: await totalResponse.json(),
        quantity,
    };
}

function endpoint(locationHref, parameters) {
    const url = new URL(locationHref);
    url.search = parameters.toString();
    return url;
}

function money(value) {
    return `${Number(value).toLocaleString('en-US', {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    })} THB`;
}

function isAbortError(error) {
    return error instanceof DOMException && error.name === 'AbortError';
}
