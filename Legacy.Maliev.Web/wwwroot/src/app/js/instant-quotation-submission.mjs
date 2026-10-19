const terminalRedirect = '/InstantQuotation/3D-Printing';

export function wireInstantQuotationSubmission(form, submit, fetchImpl, navigate) {
    if (!form || !submit) return () => {};

    let state = 'idle';
    const feedback = form.parentElement?.querySelector('[data-instant-quote-submit-feedback]');
    const submitLabel = submit.querySelector?.('[data-instant-quote-submit-label]');
    const pendingLabel = submit.querySelector?.('[data-instant-quote-submit-pending]');

    function showFeedback(heading, errors = []) {
        if (!feedback) return;
        feedback.querySelector('[data-instant-quote-submit-heading]').textContent = heading;
        const list = feedback.querySelector('[data-instant-quote-submit-errors]');
        list.replaceChildren(...errors.map((message) => {
            const item = form.ownerDocument.createElement('li');
            item.textContent = message;
            return item;
        }));
        feedback.setAttribute('role', 'alert');
        feedback.hidden = false;
        feedback.focus();
    }

    async function onSubmit(event) {
        event.preventDefault();
        if (state !== 'idle' || !form.reportValidity()) return;

        state = 'pending';
        submit.disabled = true;
        if (submitLabel) submitLabel.hidden = true;
        if (pendingLabel) pendingLabel.hidden = false;
        form.setAttribute('aria-busy', 'true');
        if (feedback) feedback.hidden = true;

        try {
            const body = new URLSearchParams(new FormData(form));
            const disabledFields = [...form.querySelectorAll('input, select, textarea')].filter((field) => !field.disabled);
            for (const field of disabledFields) field.disabled = true;
            const response = await fetchImpl(form.action, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    Accept: 'application/json',
                    'Content-Type': 'application/x-www-form-urlencoded;charset=UTF-8',
                },
                body,
            });
            if (!response.ok) throw new Error('submission_response_unconfirmed');
            const result = await response.json();
            if (result?.outcome === 'terminal' && result.redirectUrl === terminalRedirect) {
                state = 'terminal';
                navigate(terminalRedirect);
                return;
            }
            if (result?.outcome === 'retry') {
                const fields = Array.isArray(result.invalidFields) ? result.invalidFields : [];
                for (const field of fields) {
                    form.elements.namedItem(field)?.setAttribute?.('aria-invalid', 'true');
                }
                const errors = Array.isArray(result.errors) ? result.errors.filter((error) => typeof error === 'string') : [];
                showFeedback(form.dataset.submissionRetryHeading, errors.length > 0
                    ? errors : [form.dataset.submissionGenericError]);
                for (const field of disabledFields) field.disabled = false;
                state = 'idle';
                submit.disabled = false;
                if (submitLabel) submitLabel.hidden = false;
                if (pendingLabel) pendingLabel.hidden = true;
                form.removeAttribute('aria-busy');
                return;
            }
        } catch {
            // A failed response can follow a successful write. Never enable a blind resubmit.
        }

        state = 'unconfirmed';
        if (submitLabel) submitLabel.hidden = false;
        if (pendingLabel) pendingLabel.hidden = true;
        showFeedback(form.dataset.submissionUnknownHeading);
    }

    form.addEventListener('submit', onSubmit);
    return () => form.removeEventListener('submit', onSubmit);
}
