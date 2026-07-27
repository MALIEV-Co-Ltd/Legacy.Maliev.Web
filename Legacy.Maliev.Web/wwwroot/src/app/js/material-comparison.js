(function () {
    "use strict";

    const root = document.getElementById("material-comparison");
    if (!root) {
        return;
    }

    const search = root.querySelector("#material-search");
    const process = root.querySelector("#material-process");
    const reset = root.querySelector("#material-reset");
    const count = root.querySelector("#material-result-count");
    const empty = root.querySelector("[data-material-empty]");
    const rows = Array.from(root.querySelectorAll("[data-material-row]"));
    const countTemplate = count.dataset.countTemplate || "{0}";
    const detailsUrl = root.dataset.materialDetailsUrl || "/src/data/printing-material-details.json";
    const isThai = (document.documentElement.lang || "").toLowerCase().startsWith("th");
    const copy = isThai ? {
        view: "ดูรายละเอียดวัสดุ",
        close: "ปิดรายละเอียดวัสดุ",
        loading: "กำลังโหลดรายละเอียดวัสดุ…",
        description: "รายละเอียด",
        properties: "คุณสมบัติโดยทั่วไป",
        colors: "สีที่มีข้อมูลอ้างอิงในไทย",
        availability: "การมีจำหน่าย",
        application: "ตัวอย่างการใช้งาน",
        noColors: "สีขึ้นกับเกรดและสต็อกของผู้จำหน่าย โปรดยืนยันตัวเลือกก่อนสั่งผลิต",
        loadError: "โหลดรายละเอียดวัสดุไม่สำเร็จ กรุณาลองใหม่"
    } : {
        view: "View material details",
        close: "Close material details",
        loading: "Loading material details…",
        description: "Description",
        properties: "Typical properties",
        colors: "Thailand-available colour reference",
        availability: "Availability",
        application: "Typical application",
        noColors: "Colour depends on supplier grade and stock; confirm the option before ordering",
        loadError: "Material details could not be loaded. Please try again"
    };
    let materialDetailsPromise;
    const detailStates = new Map();

    const normalize = value => value.trim().toLocaleLowerCase();

    function localized(value) {
        if (value && typeof value === "object") {
            return value[isThai ? "th" : "en"] || value.en || value.th || "";
        }

        return value == null ? "" : String(value);
    }

    function createElement(tagName, className, text) {
        const element = document.createElement(tagName);
        if (className) {
            element.className = className;
        }
        if (text !== undefined) {
            element.textContent = text;
        }
        return element;
    }

    function loadMaterialDetails() {
        if (!materialDetailsPromise) {
            materialDetailsPromise = fetch(detailsUrl, {
                credentials: "same-origin",
                headers: { Accept: "application/json" }
            }).then(response => {
                if (!response.ok) {
                    throw new Error("Material detail request failed");
                }

                return response.json();
            }).then(details => {
                if (!details || typeof details !== "object" || Array.isArray(details)) {
                    throw new Error("Material detail payload is invalid");
                }

                return details;
            }).catch(error => {
                materialDetailsPromise = null;
                throw error;
            });
        }

        return materialDetailsPromise;
    }

    function collapse(state) {
        state.button.setAttribute("aria-expanded", "false");
        state.detailRow.hidden = true;
    }

    function pushMaterialEvent(state) {
        if (state.viewed) {
            return;
        }

        state.viewed = true;
        if (typeof window.malievPushDiagnosticEvent === "function") {
            window.malievPushDiagnosticEvent("material_detail_viewed", {
                material_id: state.key,
                process: state.row.dataset.materialGroup || "unknown",
                locale: isThai ? "th" : "en",
                source: "material_comparison"
            });
            return;
        }

        window.dataLayer = window.dataLayer || [];
        window.dataLayer.push({
            event: "material_detail_viewed",
            material_id: state.key,
            process: state.row.dataset.materialGroup || "unknown",
            locale: isThai ? "th" : "en",
            source: "material_comparison"
        });
    }

    function renderLoading(state) {
        state.card.replaceChildren(createElement("p", "service-material-detail-status", copy.loading));
    }

    function renderError(state) {
        const message = createElement("p", "service-material-detail-status service-material-detail-status-error", copy.loadError);
        const retry = createElement("button", "service-material-detail-retry", copy.view);
        retry.type = "button";
        retry.addEventListener("click", () => expand(state));
        message.append(" ", retry);
        state.card.replaceChildren(message);
    }

    function renderDetails(state, detail) {
        state.card.replaceChildren();

        const media = createElement("figure", "service-material-detail-card__media");
        const image = createElement("img");
        image.src = localized(detail.image);
        image.alt = localized(detail.alt);
        image.width = 768;
        image.height = 512;
        image.loading = "lazy";
        image.decoding = "async";
        media.append(image);
        const application = createElement("figcaption", "service-material-detail-card__caption", localized(detail.application));
        media.append(application);

        const content = createElement("div", "service-material-detail-card__content");
        const heading = createElement("div", "service-material-detail-card__heading");
        heading.append(createElement("h3", null, state.key));
        const close = createElement("button", "service-material-detail-close", copy.close);
        close.type = "button";
        close.addEventListener("click", () => {
            collapse(state);
            state.button.focus();
        });
        heading.append(close);
        content.append(heading);
        content.append(createElement("p", "service-material-detail-card__description", localized(detail.description)));

        const propertiesHeading = createElement("h4", null, copy.properties);
        content.append(propertiesHeading);
        const properties = createElement("dl", "service-material-detail-properties");
        (Array.isArray(detail.properties) ? detail.properties : []).forEach(property => {
            const propertyGroup = createElement("div", "service-material-detail-property");
            propertyGroup.append(createElement("dt", null, localized(property.label)));
            propertyGroup.append(createElement("dd", null, localized(property.value)));
            properties.append(propertyGroup);
        });
        content.append(properties);

        const colorsHeading = createElement("h4", null, copy.colors);
        content.append(colorsHeading);
        const colors = createElement("ul", "service-material-detail-colors");
        const colorValues = detail.colors && Array.isArray(detail.colors[isThai ? "th" : "en"])
            ? detail.colors[isThai ? "th" : "en"]
            : [];
        if (colorValues.length) {
            colorValues.forEach(color => colors.append(createElement("li", null, color)));
        } else {
            colors.append(createElement("li", "service-material-detail-colors__empty", copy.noColors));
        }
        content.append(colors);
        content.append(createElement("p", "service-material-detail-card__availability", localized(detail.availability)));

        state.card.append(media, content);
    }

    async function expand(state) {
        state.button.setAttribute("aria-expanded", "true");
        state.detailRow.hidden = false;
        pushMaterialEvent(state);
        if (state.detail) {
            return;
        }

        renderLoading(state);
        try {
            const details = await loadMaterialDetails();
            state.detail = details[state.key];
            if (!state.detail) {
                throw new Error("Material detail is missing");
            }

            renderDetails(state, state.detail);
        } catch (error) {
            renderError(state);
        }
    }

    function initializeDetailRow(row) {
        const key = row.dataset.materialKey;
        const nameCell = row.querySelector("th");
        if (!key || !nameCell) {
            return;
        }

        const detailId = `material-detail-${key.toLowerCase().replace(/[^a-z0-9]+/g, "-")}`;
        const button = createElement("button", "service-material-toggle");
        button.type = "button";
        button.setAttribute("data-material-toggle", "");
        button.setAttribute("aria-expanded", "false");
        button.setAttribute("aria-controls", detailId);
        button.append(nameCell.querySelector("strong"), nameCell.querySelector("small"));
        button.append(createElement("span", "service-material-toggle__hint", copy.view));
        nameCell.dataset.materialNameCell = "true";
        nameCell.replaceChildren(button);

        const detailRow = document.createElement("tr");
        detailRow.setAttribute("data-material-detail-row", "");
        detailRow.setAttribute("data-material-detail-for", key);
        detailRow.hidden = true;
        const detailCell = document.createElement("td");
        detailCell.colSpan = 5;
        const card = createElement("article", "service-material-detail-card");
        card.id = detailId;
        card.setAttribute("aria-live", "polite");
        detailCell.append(card);
        detailRow.append(detailCell);
        row.after(detailRow);

        const state = { key, row, detailRow, button, card, detail: null };
        detailStates.set(key, state);
        button.addEventListener("click", () => {
            if (button.getAttribute("aria-expanded") === "true") {
                collapse(state);
            } else {
                expand(state);
            }
        });
    }

    rows.forEach(initializeDetailRow);

    function applyFilters() {
        const query = normalize(search ? search.value : "");
        const selectedProcess = process ? process.value : "all";
        let visibleCount = 0;

        rows.forEach(row => {
            const matchesText = !query || normalize(row.textContent).includes(query);
            const matchesProcess = selectedProcess === "all" || row.dataset.materialGroup === selectedProcess;
            const isVisible = matchesText && matchesProcess;
            row.hidden = !isVisible;
            const state = detailStates.get(row.dataset.materialKey);
            if (state && !isVisible) {
                collapse(state);
            }
            if (isVisible) {
                visibleCount += 1;
            }
        });

        if (empty) {
            empty.hidden = visibleCount !== 0;
        }
        if (count) {
            count.textContent = countTemplate.replace("{0}", visibleCount.toString());
        }
    }

    if (search) {
        search.addEventListener("input", applyFilters);
    }
    if (process) {
        process.addEventListener("change", applyFilters);
    }
    if (reset) {
        reset.addEventListener("click", () => {
            if (search) {
                search.value = "";
            }
            if (process) {
                process.value = "all";
            }
            applyFilters();
            if (search) {
                search.focus();
            }
        });
    }
}());
