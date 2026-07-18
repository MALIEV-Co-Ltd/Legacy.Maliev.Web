function initializeApplication() {
    document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(function (element) {
        window.bootstrap.Tooltip.getOrCreateInstance(element);
    });

    document.querySelectorAll('[data-terms-gated-form]').forEach(function (form) {
        var termsCheckbox = form.querySelector('[data-terms-checkbox]');
        var termsSubmit = form.querySelector('[data-terms-submit]');
        if (!termsCheckbox || !termsSubmit) {
            return;
        }

        function updateTermsSubmitState() {
            termsSubmit.disabled = !termsCheckbox.checked || form.dataset.submitting === 'true';
        }

        termsCheckbox.addEventListener('change', updateTermsSubmitState);
        form.addEventListener('submit', function () {
            if (!termsCheckbox.checked) {
                return;
            }

            form.dataset.submitting = 'true';
            termsSubmit.disabled = true;
        });
        updateTermsSubmitState();
    });

    document.querySelectorAll('[data-password-confirmation]').forEach(function (container) {
        var password = container.querySelector('[data-password-primary]');
        var confirmation = container.querySelector('[data-password-confirm]');
        if (!password || !confirmation) {
            return;
        }

        function validatePasswordConfirmation() {
            var mismatch = confirmation.value.length > 0 && confirmation.value !== password.value;
            confirmation.setCustomValidity(mismatch ? container.dataset.passwordMismatch : '');
        }

        password.addEventListener('input', validatePasswordConfirmation);
        confirmation.addEventListener('input', validatePasswordConfirmation);
        validatePasswordConfirmation();
    });

    document.querySelectorAll('[data-recaptcha-enterprise-form]').forEach(function (form) {
        var responseInput = form.querySelector('[data-recaptcha-response]');
        var uploadFiles = form.querySelector('[data-upload-files]');
        var uploadService = form.querySelector('[data-upload-service]');
        var uploadStartEmitted = false;
        if (!responseInput || !form.dataset.recaptchaSiteKey) {
            return;
        }

        form.addEventListener('submit', function (event) {
            if (form.dataset.recaptchaVerified === 'true') {
                if (form.dataset.uploadAnalytics === 'true'
                    && !uploadStartEmitted
                    && uploadFiles
                    && uploadFiles.files.length > 0
                    && window.malievAnalytics
                    && typeof window.malievAnalytics.emit === 'function') {
                    uploadStartEmitted = true;
                    window.malievAnalytics.emit({
                        event: 'file_upload_start',
                        service: uploadService ? uploadService.value : 'custom_manufacturing',
                        file_count: uploadFiles.files.length
                    });
                    event.preventDefault();
                    window.setTimeout(function () { form.submit(); }, 150);
                }
                return;
            }

            event.preventDefault();
            window.grecaptcha.enterprise.ready(function () {
                window.grecaptcha.enterprise.execute(
                    form.dataset.recaptchaSiteKey,
                    { action: form.dataset.recaptchaAction }).then(function (token) {
                        responseInput.value = token;
                        form.dataset.recaptchaVerified = 'true';
                        if (typeof form.requestSubmit === 'function') {
                            form.requestSubmit();
                        } else {
                            form.submit();
                        }
                    });
            });
        });
    });

    document.querySelectorAll('[data-member-workspace]').forEach(function (workspace) {
        var navigation = workspace.querySelector('#member-navigation');
        var openButton = workspace.querySelector('[data-member-navigation-open]');
        var closeButton = workspace.querySelector('[data-member-navigation-close]');
        var backdrop = workspace.querySelector('[data-member-navigation-backdrop]');
        if (!navigation || !openButton || !backdrop) {
            return;
        }

        function closeMemberNavigation() {
            navigation.classList.remove('is-open');
            document.body.classList.remove('workspace-drawer-open');
            openButton.setAttribute('aria-expanded', 'false');
            backdrop.hidden = true;
            openButton.focus();
        }

        function openMemberNavigation() {
            navigation.classList.add('is-open');
            document.body.classList.add('workspace-drawer-open');
            openButton.setAttribute('aria-expanded', 'true');
            backdrop.hidden = false;
            if (closeButton) {
                closeButton.focus();
            }
        }

        openButton.addEventListener('click', openMemberNavigation);
        if (closeButton) {
            closeButton.addEventListener('click', closeMemberNavigation);
        }
        backdrop.addEventListener('click', closeMemberNavigation);
        document.addEventListener('keydown', function (event) {
            if (event.key === 'Escape' && navigation.classList.contains('is-open')) {
                closeMemberNavigation();
            }
        });
    });
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initializeApplication, { once: true });
} else {
    initializeApplication();
}

// Prevents reCAPTCHA callbacks from bypassing the normal in-flight button state
// when they invoke the browser's native form.submit() method.
function SubmitFormOnce(formId, buttonId, submittingText) {
    var form = document.getElementById(formId);
    var submitButton = document.getElementById(buttonId);
    if (!form || !submitButton || form.dataset.submitting === 'true') {
        return false;
    }

    form.dataset.submitting = 'true';
    form.setAttribute('aria-busy', 'true');
    submitButton.disabled = true;
    submitButton.setAttribute('aria-disabled', 'true');
    if (submittingText) {
        if (submitButton.tagName === 'INPUT') {
            submitButton.value = submittingText;
        } else {
            submitButton.textContent = submittingText;
        }
    }

    form.submit();
    return true;
}

function PreviewImageFile(targetElementID) {
    var preview = document.getElementById(targetElementID);
    var fileInput = document.querySelector('input[type=file]');
    var file = fileInput?.files[0];
    var reader = new FileReader();

    reader.onloadend = function () {
        preview.src = reader.result;
    };

    if (file) {
        reader.readAsDataURL(file);
    } else {
        preview.src = '';
    }
}

function ScrollToTop() {
    document.body.scrollTop = 0;
    document.documentElement.scrollTop = 0;
}

function ForceInputNumberInRange(inputElement, minValue, maxValue) {
    if (inputElement.value < minValue) inputElement.value = minValue;
    if (inputElement.value > maxValue) inputElement.value = maxValue;
}

function ToTwoDecimalPoint(value) {
    var valueString = value.toString().replace(',', '');
    var result = Math.round(valueString * 100) / 100;
    return parseFloat(result).toFixed(2);
}

Date.prototype.ToLocalDate = function () {
    var offset = new Date().getTimezoneOffset() * 60000;
    return new Date(this.getTime() - offset).toLocaleDateString();
};

Date.prototype.ToLocalTime = function () {
    var offset = new Date().getTimezoneOffset() * 60000;
    var localDate = new Date(this.getTime() - offset);
    return localDate.toLocaleTimeString(navigator.language, { hour: '2-digit', minute: '2-digit', hour12: false });
};

Date.prototype.ToLocalDateTime = function () {
    var offset = new Date().getTimezoneOffset() * 60000;
    var localDate = new Date(this.getTime() - offset);
    return localDate.toLocaleString();
};

function convertUtcElementsToLocalTime() {
    convertElements('.convert-to-localtime', 'ToLocalTime');
    convertElements('.convert-to-localdate', 'ToLocalDate');
    convertElements('.convert-to-localdatetime', 'ToLocalDateTime');
}

function convertElements(selector, converter) {
    document.querySelectorAll(selector).forEach(function (element) {
        var value = element.nodeName === 'INPUT' ? element.value : element.innerText;
        var converted = new Date(value)[converter]();
        if (element.nodeName === 'INPUT') {
            element.value = converted;
        } else {
            element.innerText = converted;
        }
    });
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', convertUtcElementsToLocalTime, { once: true });
} else {
    convertUtcElementsToLocalTime();
}

window.SubmitFormOnce = SubmitFormOnce;
window.PreviewImageFile = PreviewImageFile;
window.ScrollToTop = ScrollToTop;
window.ForceInputNumberInRange = ForceInputNumberInRange;
window.ToTwoDecimalPoint = ToTwoDecimalPoint;
