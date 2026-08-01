(function () {
    "use strict";

    // Shared diagnostic event bridge. Values are deliberately scalar and limited
    // to stable, non-identifying fields so page-specific flows can be measured
    // without duplicating the persisted-lead conversion emitter.
    if (typeof window.malievPushDiagnosticEvent !== "function") {
        window.malievPushDiagnosticEvent = function (eventName, fields) {
            if (!eventName || typeof eventName !== "string") {
                return;
            }

            var event = { event: eventName };
            Object.keys(fields || {}).forEach(function (key) {
                var value = fields[key];
                if (value === null || value === undefined || typeof value === "object" || typeof value === "function") {
                    return;
                }

                if (typeof value === "string") {
                    value = value.trim().toLowerCase().replace(/[^a-z0-9_>,.-]/g, "").substring(0, 120);
                    if (!value) {
                        return;
                    }
                }

                event[key] = value;
            });

            window.dataLayer = window.dataLayer || [];
            window.dataLayer.push(event);
        };
    }

    var countryCodeByTimeZone = {
        "Asia/Bangkok": "TH",
        "Asia/Ho_Chi_Minh": "VN",
        "Asia/Manila": "PH",
        "Asia/Singapore": "SG",
        "Asia/Kuala_Lumpur": "MY",
        "Asia/Jakarta": "ID",
        "Asia/Tokyo": "JP",
        "Asia/Seoul": "KR",
        "Asia/Shanghai": "CN",
        "Asia/Taipei": "TW",
        "Asia/Kolkata": "IN",
        "Australia/Sydney": "AU",
        "Europe/London": "GB",
        "Europe/Paris": "FR",
        "Europe/Berlin": "DE",
        "America/New_York": "US",
        "America/Chicago": "US",
        "America/Denver": "US",
        "America/Los_Angeles": "US",
        "America/Toronto": "CA"
    };

    function normaliseCountryName(value) {
        return (value || "").trim().toLocaleLowerCase();
    }

    function getCountryCode() {
        var locales = Array.isArray(navigator.languages) ? navigator.languages : [];
        locales = locales.concat(navigator.language || []);

        for (var i = 0; i < locales.length; i += 1) {
            var match = String(locales[i]).match(/[-_]([A-Za-z]{2})$/);
            if (match) {
                return match[1].toUpperCase();
            }
        }

        try {
            return countryCodeByTimeZone[Intl.DateTimeFormat().resolvedOptions().timeZone] || "";
        } catch (error) {
            return "";
        }
    }

    function findCountryOption(select, countryCode) {
        if (!countryCode) {
            return null;
        }

        var countryNames = [];
        try {
            var displayNames = new Intl.DisplayNames(["en"], { type: "region" });
            countryNames.push(displayNames.of(countryCode) || "");
            if (navigator.language && navigator.language !== "en") {
                displayNames = new Intl.DisplayNames([navigator.language], { type: "region" });
                countryNames.push(displayNames.of(countryCode) || "");
            }
        } catch (error) {
            countryNames = [];
        }

        var normalisedNames = countryNames.map(normaliseCountryName).filter(Boolean);
        var options = Array.prototype.slice.call(select.options);
        return options.find(function (option) {
            return option.dataset.countryCode === countryCode
                || normalisedNames.indexOf(normaliseCountryName(option.value || option.textContent)) >= 0;
        }) || null;
    }

    function autoPopulateCountry(select) {
        if (!select || select.value || select.dataset.countryAutoApplied === "true") {
            return;
        }

        var option = findCountryOption(select, getCountryCode());
        if (!option) {
            return;
        }

        select.value = option.value;
        select.dataset.countryAutoApplied = "true";
        select.dispatchEvent(new Event("change", { bubbles: true }));
    }

    function attachEmailValidation(form) {
        form.querySelectorAll("input[type='email'][data-live-email]").forEach(function (input) {
            var field = input.closest(".inquiry-field, .form-floating");
            var feedback = field ? field.querySelector("[data-email-feedback]") : null;

            function updateValidity(force) {
                var value = input.value.trim();
                var shouldShow = force || input.dataset.emailTouched === "true";
                input.setCustomValidity("");

                if (value && !input.validity.valid) {
                    input.setCustomValidity(input.dataset.liveEmailInvalid || "Please enter a valid email address.");
                }

                if (!feedback) {
                    return;
                }

                var invalid = shouldShow && value && !input.validity.valid;
                feedback.hidden = !invalid;
                feedback.textContent = invalid ? (input.dataset.liveEmailInvalid || "Please enter a valid email address.") : "";
                input.setAttribute("aria-invalid", invalid ? "true" : "false");
            }

            input.addEventListener("blur", function () {
                input.dataset.emailTouched = "true";
                updateValidity(true);
            });
            input.addEventListener("input", function () {
                input.dataset.emailTouched = "true";
                updateValidity(false);
            });
            updateValidity(false);
        });
    }

    function formatFileSize(bytes) {
        return (bytes / (1024 * 1024)).toFixed(2) + " MB";
    }

    function replaceInputFiles(input, files) {
        if (typeof DataTransfer === "undefined") {
            return;
        }

        var transfer = new DataTransfer();
        Array.prototype.forEach.call(files, function (file) {
            transfer.items.add(file);
        });
        input.files = transfer.files;
        input.dispatchEvent(new Event("change", { bubbles: true }));
    }

    function attachUploadDropzone(form) {
        var input = form.querySelector("#file-upload");
        var dropzone = form.querySelector("[data-upload-dropzone]");
        var list = form.querySelector("[data-upload-file-list]");
        if (!input || !dropzone || !list) {
            return;
        }

        function renderFiles() {
            list.innerHTML = "";
            Array.prototype.forEach.call(input.files || [], function (file, index) {
                var item = document.createElement("li");
                var name = document.createElement("span");
                var remove = document.createElement("button");
                name.textContent = file.name + " (" + formatFileSize(file.size) + ")";
                remove.type = "button";
                remove.className = "inquiry-upload__remove";
                remove.textContent = input.dataset.removeFileLabel || "Remove";
                remove.setAttribute("aria-label", (input.dataset.removeFileLabel || "Remove") + " " + file.name);
                remove.addEventListener("click", function () {
                    var files = Array.prototype.slice.call(input.files || []);
                    files.splice(index, 1);
                    replaceInputFiles(input, files);
                });
                item.appendChild(name);
                item.appendChild(remove);
                list.appendChild(item);
            });

            list.hidden = !input.files || input.files.length === 0;
            if (typeof window.CheckFileSize === "function") {
                window.CheckFileSize();
            }
        }

        input.addEventListener("change", renderFiles);
        ["dragenter", "dragover"].forEach(function (eventName) {
            dropzone.addEventListener(eventName, function (event) {
                event.preventDefault();
                dropzone.classList.add("is-dragover");
            });
        });
        ["dragleave", "drop"].forEach(function (eventName) {
            dropzone.addEventListener(eventName, function (event) {
                event.preventDefault();
                dropzone.classList.remove("is-dragover");
            });
        });
        dropzone.addEventListener("drop", function (event) {
            if (event.dataTransfer && event.dataTransfer.files && event.dataTransfer.files.length) {
                replaceInputFiles(input, event.dataTransfer.files);
            }
        });
        renderFiles();
    }

    function initialiseInquiryEnhancements() {
        document.querySelectorAll("select[data-auto-country]").forEach(autoPopulateCountry);
        document.querySelectorAll("form").forEach(function (form) {
            if (form.querySelector("input[type='email'][data-live-email]")) {
                attachEmailValidation(form);
            }

            if (form.querySelector("[data-upload-dropzone]")) {
                attachUploadDropzone(form);
            }
        });
    }

    function onReady() {
        initialiseInquiryEnhancements();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", onReady, { once: true });
    } else {
        onReady();
    }
}());


