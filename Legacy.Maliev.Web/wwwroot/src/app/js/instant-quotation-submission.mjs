const terminalRedirect = '/InstantQuotation/3D-Printing';

export function wireInstantQuotationSubmission(form, submit, fetchImpl, navigate, emitDiagnostic) {
    if (!form || !submit) return () => {};

    let state = 'idle';
    const feedback = form.parentElement?.querySelector('[data-instant-quote-submit-feedback]');

    function recordDiagnostic(payload) {
        try {
            const result = emitDiagnostic
                ? emitDiagnostic(payload)
                : form.ownerDocument?.defaultView?.malievAnalytics?.emit?.(payload);
            Promise.resolve(result).catch(() => {});
        } catch {
            // Measurement must never change the submission or retry outcome.
        }
    }

    function recordSubmissionError(category) {
        recordDiagnostic({
            event: 'quote_submission_error',
            service: '3d_printing',
            failure_category: category,
        });
    }

    function onSubmitClick() {
        // Native constraint validation can prevent the submit event entirely.
        if (state === 'idle' && form.checkValidity && !form.checkValidity()) {
            recordSubmissionError('validation');
        }
    }

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
        if (state !== 'idle') return;
        if (!form.reportValidity()) {
            recordSubmissionError('validation');
            return;
        }

        state = 'pending';
        const hasFiles = form.dataset.quoteHasFiles === 'true';
        recordDiagnostic({
            event: 'quote_submit_attempt',
            service: '3d_printing',
            has_files: hasFiles,
            file_upload_completed: hasFiles && form.dataset.quoteFileUploadCompleted === 'true',
        });
        submit.disabled = true;
        form.setAttribute('aria-busy', 'true');
        if (feedback) feedback.hidden = true;

        try {
            const response = await fetchImpl(form.action, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    Accept: 'application/json',
                    'Content-Type': 'application/x-www-form-urlencoded;charset=UTF-8',
                },
                body: new URLSearchParams(new FormData(form)),
            });
            if (!response.ok) throw new Error('submission_response_unconfirmed');
            const result = await response.json();
            if (result?.outcome === 'terminal' && result.redirectUrl === terminalRedirect) {
                state = 'terminal';
                navigate(terminalRedirect);
                return;
            }
            if (result?.outcome === 'retry') {
                recordSubmissionError('validation');
                const fields = Array.isArray(result.invalidFields) ? result.invalidFields : [];
                for (const field of fields) {
                    form.elements.namedItem(field)?.setAttribute?.('aria-invalid', 'true');
                }
                const errors = Array.isArray(result.errors) ? result.errors.filter((error) => typeof error === 'string') : [];
                showFeedback(form.dataset.submissionRetryHeading, errors.length > 0
                    ? errors : [form.dataset.submissionGenericError]);
                state = 'idle';
                submit.disabled = false;
                form.removeAttribute('aria-busy');
                return;
            }
        } catch {
            // A failed response can follow a successful write. Never enable a blind resubmit.
        }

        recordSubmissionError('network');
        state = 'unconfirmed';
        showFeedback(form.dataset.submissionUnknownHeading);
    }

    form.addEventListener('submit', onSubmit);
    submit.addEventListener('click', onSubmitClick);
    return () => {
        form.removeEventListener('submit', onSubmit);
        submit.removeEventListener('click', onSubmitClick);
    };
}
