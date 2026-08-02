(function () {
    "use strict";

    document.querySelectorAll("[data-member-order-form]").forEach(function (form) {
        var material = form.querySelector("[data-order-material]");
        var color = form.querySelector("[data-order-color]");
        var finish = form.querySelector("[data-order-finish]");
        var endpoint = form.dataset.optionsEndpoint;
        var requestSequence = 0;

        if (!material || !endpoint || (!color && !finish)) {
            return;
        }

        function replaceOptions(select, items) {
            if (!select) {
                return;
            }

            var placeholder = select.options[0].cloneNode(true);
            select.replaceChildren(placeholder);
            items.forEach(function (item) {
                if (!Number.isInteger(item.id) || item.id <= 0 || typeof item.name !== "string") {
                    return;
                }

                var option = document.createElement("option");
                option.value = String(item.id);
                option.textContent = item.name;
                select.append(option);
            });
            select.disabled = items.length === 0;
        }

        function failClosed() {
            replaceOptions(color, []);
            replaceOptions(finish, []);
        }

        material.addEventListener("change", function () {
            var materialId = Number.parseInt(material.value, 10);
            var sequence = ++requestSequence;
            failClosed();
            if (!Number.isInteger(materialId) || materialId <= 0) {
                return;
            }

            fetch(endpoint + "?materialId=" + encodeURIComponent(materialId), {
                credentials: "same-origin",
                headers: { Accept: "application/json" }
            }).then(function (response) {
                if (!response.ok) {
                    throw new Error("Material options unavailable");
                }
                return response.json();
            }).then(function (payload) {
                if (sequence !== requestSequence || !payload || typeof payload !== "object") {
                    return;
                }
                replaceOptions(color, Array.isArray(payload.colors) ? payload.colors : []);
                replaceOptions(finish, Array.isArray(payload.surfaceFinishes) ? payload.surfaceFinishes : []);
            }).catch(function () {
                if (sequence === requestSequence) {
                    failClosed();
                }
            });
        });
    });
}());
