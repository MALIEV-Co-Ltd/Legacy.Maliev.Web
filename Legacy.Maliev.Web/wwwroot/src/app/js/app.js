$(window).ready(function () {
    // enable bootstrap tooltip everywhere
    $('[data-bs-toggle="tooltip"]').tooltip();

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
});

$(window).resize(function () {
});

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

// show preview image of the file
function PreviewImageFile(targetElementID) {
    var preview = document.getElementById(targetElementID);
    var file = document.querySelector('input[type=file]').files[0];
    var reader = new FileReader();

    // when loaded
    reader.onloadend = function () {
        preview.src = reader.result;
    };

    // if file is selected
    if (file) {
        reader.readAsDataURL(file);
    } else {
        preview.src = "";
    }
}

// scroll to the top of the page
function ScrollToTop() {
    document.body.scrollTop = 0;
    document.documentElement.scrollTop = 0;
}

// force input type='number' to be limited within given range
function ForceInputNumberInRange(inputElement, minValue, maxValue) {
    if (inputElement.value < minValue) inputElement.value = minValue;
    if (inputElement.value > maxValue) inputElement.value = maxValue;
}

// convert float/double to two decimal point
function ToTwoDecimalPoint(value) {
    var valueString = value.toString();
    valueString = valueString.replace(',', '');
    var result = Math.round(valueString * 100) / 100;
    result = parseFloat(result).toFixed(2);
    return result;
}

// convert utc datetime to client local datetime
Date.prototype.ToLocalDate = function () {
    var offset = new Date().getTimezoneOffset() * 60000;
    return new Date(this.getTime() - offset).toLocaleDateString();
}

Date.prototype.ToLocalTime = function () {
    var offset = new Date().getTimezoneOffset() * 60000;
    var localDate = new Date(this.getTime() - offset);
    return localDate.toLocaleTimeString(navigator.language, { hour: '2-digit', minute: '2-digit', hour12: false })
}

Date.prototype.ToLocalDateTime = function () {
    var offset = new Date().getTimezoneOffset() * 60000;
    var localDate = new Date(this.getTime() - offset);
    return localDate.toLocaleString();
}

// perform automatic conversion when element is decorated with class
$(document).ready(function () {
    $('.convert-to-localtime').each(function (i, obj) {
        if (obj.nodeName == "INPUT") {
            var utcDate = new Date(obj.value);
            obj.value = utcDate.ToLocalTime();
        }
        else {
            var utcDate = new Date(obj.innerText);
            obj.innerText = utcDate.ToLocalTime();
        }
    });

    $('.convert-to-localdate').each(function (i, obj) {
        if (obj.nodeName == "INPUT") {
            var utcDate = new Date(obj.value);
            obj.value = utcDate.ToLocalDate();
        }
        else {
            var utcDate = new Date(obj.innerText);
            obj.innerText = utcDate.ToLocalDate();
        }
    });

    $('.convert-to-localdatetime').each(function (i, obj) {
        if (obj.nodeName == "INPUT") {
            var utcDate = new Date(obj.value);
            obj.value = utcDate.ToLocalDateTime();
        }
        else {
            var utcDate = new Date(obj.innerText);
            obj.innerText = utcDate.ToLocalDateTime();
        }
    });
});
