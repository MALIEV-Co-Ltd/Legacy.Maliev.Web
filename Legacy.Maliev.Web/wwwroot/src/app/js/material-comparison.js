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

    const normalize = value => value.trim().toLocaleLowerCase();

    function applyFilters() {
        const query = normalize(search.value);
        const selectedProcess = process.value;
        let visibleCount = 0;

        rows.forEach(row => {
            const matchesText = !query || normalize(row.textContent).includes(query);
            const matchesProcess = selectedProcess === "all" || row.dataset.materialGroup === selectedProcess;
            const isVisible = matchesText && matchesProcess;
            row.hidden = !isVisible;
            if (isVisible) {
                visibleCount += 1;
            }
        });

        empty.hidden = visibleCount !== 0;
        count.textContent = countTemplate.replace("{0}", visibleCount.toString());
    }

    search.addEventListener("input", applyFilters);
    process.addEventListener("change", applyFilters);
    reset.addEventListener("click", () => {
        search.value = "";
        process.value = "all";
        applyFilters();
        search.focus();
    });
}());
