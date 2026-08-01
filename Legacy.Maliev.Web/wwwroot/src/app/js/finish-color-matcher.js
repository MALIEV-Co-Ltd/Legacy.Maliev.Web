(function () {
    'use strict';

    const root = document.querySelector('[data-finish-color-matcher]');
    const core = window.MalievFinishColorMatcherCore;
    const atlas = window.MalievHlcColourAtlas;

    if (!root || !core || !atlas) {
        return;
    }

    if (root.dataset.matcherInitialized === 'true') {
        return;
    }
    root.dataset.matcherInitialized = 'true';

    const elements = {
        color: root.querySelector('[data-color-input]'),
        hex: root.querySelector('[data-hex-input]'),
        sheenPreview: root.querySelector('[data-sheen-preview]'),
        sheenStage: root.querySelector('[data-sheen-stage]'),
        sheenCanvas: root.querySelector('[data-sheen-canvas]'),
        previewSheen: root.querySelector('[data-preview-sheen]'),
        results: root.querySelector('[data-color-results]'),
        status: root.querySelector('[data-result-status]'),
        selectionStatus: root.querySelector('[data-selection-status]'),
        closestPosition: root.querySelector('[data-closest-position]'),
        selectedCode: root.querySelector('[data-selected-code]'),
        selectedHex: root.querySelector('[data-selected-hex]'),
        selectedSheen: root.querySelector('[data-selected-sheen]'),
        selectedDelta: root.querySelector('[data-selected-delta]'),
        selectedPantone: root.querySelector('[data-selected-pantone]'),
        selectedSwatch: root.querySelector('[data-selected-swatch]'),
        primerGuidance: root.querySelector('[data-primer-guidance]'),
        clearGuidance: root.querySelector('[data-clear-guidance]'),
        pantone: root.querySelector('[data-pantone-input]'),
        quote: root.querySelector('[data-quote-link]'),
        imageInput: root.querySelector('[data-image-input]'),
        imageStatus: root.querySelector('[data-image-status]'),
        imagePanel: root.querySelector('[data-image-panel]'),
        imageCanvas: root.querySelector('[data-image-canvas]'),
        imageCursor: root.querySelector('[data-image-cursor]'),
        imageName: root.querySelector('[data-image-name]'),
    };
    const references = atlas.colours.map(([code, l, a, b, hex]) => ({ code, l, a, b, hex }));
    const culture = root.dataset.culture || 'en';
    const numberFormat = new Intl.NumberFormat(culture, { maximumFractionDigits: 2, minimumFractionDigits: 2 });
    const analytics = core.createDiagnosticTracker(culture, (event) => {
        window.dataLayer = window.dataLayer || [];
        window.dataLayer.push(event);
    });
    let selectedReference = null;
    let activeResults = [];
    let closestReferenceCode = '';
    const samplePoint = { x: 0.5, y: 0.5 };
    const pbrPreview = window.MalievFinishColorMatcherPreview?.create({
        stage: elements.sheenStage,
        canvas: elements.sheenCanvas,
    });

    function text(name) {
        return root.dataset[name] || '';
    }

    function formatText(template, replacements) {
        return Object.entries(replacements).reduce(
            (result, [name, value]) => result.replaceAll(`{${name}}`, value),
            template);
    }

    function matchQuality(reference) {
        if (reference.deltaE <= 3) {
            return 'close';
        }

        if (reference.deltaE <= 10) {
            return 'noticeable';
        }

        return 'large_difference';
    }

    function selectionFields(reference) {
        return {
            step_number: activeResults.findIndex((item) => item.code === reference.code) + 1,
            selection_type: reference.code === closestReferenceCode ? 'closest' : 'tonal_alternative',
            sheen: sheenValue(),
            match_quality: matchQuality(reference),
        };
    }

    function sheenValue() {
        return root.querySelector('input[name="finish-sheen"]:checked').value;
    }

    function sheenLabel(value) {
        return text(`sheen${value.charAt(0).toUpperCase()}${value.slice(1)}`);
    }

    function normalizeHex(value) {
        let normalized = value.trim();
        if (!normalized.startsWith('#')) {
            normalized = `#${normalized}`;
        }

        return /^#[0-9a-f]{6}$/i.test(normalized) ? normalized.toUpperCase() : null;
    }

    function updateQuoteLink() {
        if (!selectedReference) {
            return;
        }

        elements.quote.href = core.buildQuoteUrl({
            baseUrl: elements.quote.dataset.baseUrl,
            culture,
            sourceHex: elements.hex.value,
            reference: selectedReference,
            pantone: elements.pantone.value,
            sheen: sheenValue(),
        });
    }

    function updateFinishGuidance() {
        if (!selectedReference) {
            return;
        }

        const sheen = sheenValue();
        const label = sheenLabel(sheen);
        const pantone = elements.pantone.value.trim();
        elements.sheenPreview.dataset.sheen = sheen;
        elements.previewSheen.textContent = formatText(text('previewTemplate'), { sheen: label });
        pbrPreview?.update(selectedReference.hex, sheen);
        elements.selectedSheen.textContent = label;
        elements.selectedPantone.textContent = pantone || text('noPantone');
        const primer = core.primerTone(selectedReference.l);
        const primerValue = text(`primer${primer.charAt(0).toUpperCase()}${primer.slice(1)}`);
        const primerTemplate = text('primerTemplate');
        const [primerPrefix, ...primerSuffixParts] = primerTemplate.split('{primer}');
        const primerHighlight = document.createElement('span');
        primerHighlight.className = 'finish-primer-value';
        primerHighlight.textContent = primerValue;
        elements.primerGuidance.replaceChildren(
            document.createTextNode(primerPrefix || ''),
            primerHighlight,
            document.createTextNode(primerSuffixParts.join('{primer}')));
        elements.clearGuidance.textContent = text(`clear${sheen.charAt(0).toUpperCase()}${sheen.slice(1)}`);
        updateQuoteLink();
    }

    function announceSelection(reference) {
        elements.selectionStatus.textContent = formatText(text('selectionStatus'), {
            code: reference.code,
            sheen: sheenLabel(sheenValue()),
            delta: numberFormat.format(reference.deltaE),
        });
    }

    function selectReference(reference) {
        selectedReference = reference;
        elements.selectedCode.textContent = reference.code;
        elements.selectedHex.textContent = elements.hex.value;
        elements.selectedDelta.textContent = `ΔE00 ${numberFormat.format(reference.deltaE)}`;
        elements.selectedSwatch.style.backgroundColor = reference.hex;
        elements.results.querySelectorAll('[data-reference-code]').forEach((button) => {
            const isSelected = button.dataset.referenceCode === reference.code;
            button.classList.toggle('is-selected', isSelected);
            button.setAttribute('aria-pressed', String(isSelected));
            button.tabIndex = isSelected ? 0 : -1;
        });
        updateFinishGuidance();
        announceSelection(reference);
    }

    function renderResults() {
        const fragment = document.createDocumentFragment();
        elements.results.replaceChildren();

        const closestReference = activeResults.reduce((closest, reference) => reference.deltaE < closest.deltaE ? reference : closest);
        closestReferenceCode = closestReference.code;

        activeResults.forEach((reference) => {
            const item = document.createElement('li');
            const button = document.createElement('button');
            const swatch = document.createElement('span');
            const copy = document.createElement('span');
            const title = document.createElement('span');
            const code = document.createElement('strong');
            const badge = document.createElement('span');
            const values = document.createElement('span');
            const hexValue = document.createElement('span');
            const lightnessValue = document.createElement('span');
            const delta = document.createElement('span');
            const position = formatText(text('lightnessPosition'), { lightness: numberFormat.format(reference.l) });
            const isClosest = reference.code === closestReference.code;

            button.type = 'button';
            button.className = 'finish-match-card';
            button.classList.toggle('is-closest-reference', isClosest);
            button.dataset.referenceCode = reference.code;
            button.setAttribute('aria-pressed', 'false');
            button.tabIndex = isClosest ? 0 : -1;
            button.setAttribute('aria-label', formatText(text('selectTemplate'), {
                code: reference.code,
                delta: numberFormat.format(reference.deltaE),
                position,
            }));
            swatch.className = 'finish-match-swatch';
            swatch.style.backgroundColor = reference.hex;
            copy.className = 'finish-match-copy';
            title.className = 'finish-match-title';
            code.textContent = reference.code;
            badge.className = 'finish-closest-badge';
            badge.textContent = text('closestLabel');
            badge.hidden = !isClosest;
            values.className = 'finish-match-values';
            hexValue.textContent = reference.hex;
            lightnessValue.textContent = `L* ${numberFormat.format(reference.l)}`;
            delta.className = 'finish-match-delta';
            delta.textContent = `ΔE00 ${numberFormat.format(reference.deltaE)}`;
            title.append(code, badge);
            values.append(hexValue, lightnessValue);
            copy.append(title, values);
            button.append(swatch, copy, delta);
            button.addEventListener('click', () => {
                const selectionChanged = selectedReference?.code !== reference.code;
                selectReference(reference);
                analytics.trackRecommendation(selectionFields(reference), selectionChanged);
            });
            item.append(button);
            fragment.append(item);

        });

        elements.results.append(fragment);
        const stillVisible = activeResults.find((reference) => reference.code === selectedReference?.code);
        selectReference(stillVisible || closestReference);
        const closestIndex = activeResults.findIndex((reference) => reference.code === closestReference.code);
        elements.closestPosition.textContent = formatText(text('closestPosition'), {
            position: String(closestIndex + 1),
            count: String(activeResults.length),
        });
        if (window.matchMedia('(max-width: 47.99rem)').matches) {
            const selectedCard = elements.results.querySelector('[aria-pressed="true"]');
            elements.results.scrollLeft = Math.max(
                0,
                selectedCard.offsetLeft - ((elements.results.clientWidth - selectedCard.offsetWidth) / 2));
        }
        elements.status.textContent = formatText(text('resultsStatus'), {
            count: String(activeResults.length),
            hex: elements.hex.value,
        });
    }

    function matchColor(hex) {
        elements.color.value = hex;
        elements.hex.value = hex;
        elements.sheenPreview.style.setProperty('--finish-color', hex);
        selectedReference = null;
        activeResults = core.findRecommendations(core.hexToLab(hex), references, 10);
        renderResults();
    }

    function applyHexInput() {
        analytics.ensureStarted('hex');
        const normalized = normalizeHex(elements.hex.value);
        if (!normalized) {
            elements.hex.setCustomValidity(text('invalidHex'));
            elements.hex.reportValidity();
            analytics.trackError('hex');
            return;
        }

        elements.hex.setCustomValidity('');
        matchColor(normalized);
    }

    function updateSampleCursor() {
        elements.imageCursor.style.left = `${samplePoint.x * 100}%`;
        elements.imageCursor.style.top = `${samplePoint.y * 100}%`;
    }

    function setSamplePoint(x, y) {
        samplePoint.x = Math.max(0, Math.min(1, x));
        samplePoint.y = Math.max(0, Math.min(1, y));
        updateSampleCursor();
    }

    function sampleCanvas() {
        const canvas = elements.imageCanvas;
        const x = Math.round(samplePoint.x * (canvas.width - 1));
        const y = Math.round(samplePoint.y * (canvas.height - 1));
        const context = canvas.getContext('2d', { willReadFrequently: true });
        const radius = 2;
        const startX = Math.max(0, x - radius);
        const startY = Math.max(0, y - radius);
        const width = Math.min(canvas.width - startX, radius * 2 + 1);
        const height = Math.min(canvas.height - startY, radius * 2 + 1);
        const pixels = context.getImageData(startX, startY, width, height).data;
        const hex = core.averagePixelColor(pixels);

        if (hex) {
            matchColor(hex);
        }
    }

    function showImageStatus(message, isReady) {
        elements.imageStatus.textContent = message;
        elements.imageStatus.hidden = false;
        elements.imageStatus.classList.toggle('is-ready', isReady);
    }

    function loadImage(file) {
        if (!file || !file.type.startsWith('image/')) {
            showImageStatus(text('invalidImage'), false);
            analytics.trackError('image');
            return;
        }

        const image = new Image();
        const objectUrl = URL.createObjectURL(file);
        image.onload = function () {
            const maxDimension = 1000;
            const scale = Math.min(1, maxDimension / Math.max(image.naturalWidth, image.naturalHeight));
            const canvas = elements.imageCanvas;
            canvas.width = Math.max(1, Math.round(image.naturalWidth * scale));
            canvas.height = Math.max(1, Math.round(image.naturalHeight * scale));
            canvas.getContext('2d').drawImage(image, 0, 0, canvas.width, canvas.height);
            elements.imageName.textContent = file.name;
            elements.imagePanel.hidden = false;
            setSamplePoint(0.5, 0.5);
            showImageStatus(text('imageReady'), true);
            elements.imagePanel.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            URL.revokeObjectURL(objectUrl);
        };
        image.onerror = function () {
            URL.revokeObjectURL(objectUrl);
            showImageStatus(text('invalidImage'), false);
            analytics.trackError('image');
        };
        image.src = objectUrl;
    }

    elements.color.addEventListener('input', () => {
        analytics.ensureStarted('color_picker');
        matchColor(elements.color.value.toUpperCase());
    });
    elements.hex.addEventListener('change', applyHexInput);
    elements.hex.addEventListener('keydown', (event) => {
        if (event.key === 'Enter') {
            event.preventDefault();
            applyHexInput();
        }
    });
    elements.pantone.addEventListener('input', () => {
        analytics.ensureStarted('pantone');
        updateFinishGuidance();
    });
    root.querySelectorAll('input[name="finish-sheen"]').forEach((input) => input.addEventListener('change', () => {
        analytics.ensureStarted('sheen');
        updateFinishGuidance();
        announceSelection(selectedReference);
    }));
    elements.imageInput.addEventListener('change', () => {
        analytics.ensureStarted('image');
        loadImage(elements.imageInput.files[0]);
    });
    elements.imageCanvas.addEventListener('click', (event) => {
        const rectangle = elements.imageCanvas.getBoundingClientRect();
        setSamplePoint(
            (event.clientX - rectangle.left) / rectangle.width,
            (event.clientY - rectangle.top) / rectangle.height);
        sampleCanvas();
    });
    elements.imageCanvas.addEventListener('keydown', (event) => {
        const step = event.shiftKey ? 0.1 : 0.02;
        const movement = {
            ArrowLeft: [-step, 0],
            ArrowRight: [step, 0],
            ArrowUp: [0, -step],
            ArrowDown: [0, step],
        }[event.key];

        if (movement) {
            event.preventDefault();
            setSamplePoint(samplePoint.x + movement[0], samplePoint.y + movement[1]);
        } else if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            sampleCanvas();
        }
    });

    elements.results.addEventListener('keydown', (event) => {
        const currentButton = event.target.closest('[data-reference-code]');
        if (!currentButton) {
            return;
        }

        const buttons = Array.from(elements.results.querySelectorAll('[data-reference-code]'));
        const currentIndex = buttons.indexOf(currentButton);
        const movement = {
            ArrowLeft: -1,
            ArrowUp: -1,
            ArrowRight: 1,
            ArrowDown: 1,
        }[event.key];
        let nextIndex = currentIndex;

        if (movement) {
            nextIndex = (currentIndex + movement + buttons.length) % buttons.length;
        } else if (event.key === 'Home') {
            nextIndex = 0;
        } else if (event.key === 'End') {
            nextIndex = buttons.length - 1;
        } else {
            return;
        }

        event.preventDefault();
        const nextButton = buttons[nextIndex];
        const reference = activeResults.find((item) => item.code === nextButton.dataset.referenceCode);
        const selectionChanged = selectedReference?.code !== reference.code;
        selectReference(reference);
        nextButton.focus();
        analytics.trackRecommendation(selectionFields(reference), selectionChanged);
    });

    root.querySelectorAll('[data-guidance-topic]').forEach((details) => details.addEventListener('toggle', () => {
        const topic = details.dataset.guidanceTopic;
        if (!details.open) {
            return;
        }

        analytics.trackGuidance(topic);
    }));

    elements.quote.addEventListener('click', () => {
        if (!selectedReference) {
            return;
        }

        analytics.trackQuote(selectionFields(selectedReference), Boolean(elements.pantone.value.trim()));
    });

    if ('IntersectionObserver' in window) {
        const observer = new IntersectionObserver((entries) => {
            if (entries.some((entry) => entry.isIntersecting && entry.intersectionRatio >= 0.5)) {
                analytics.trackViewed();
                observer.disconnect();
            }
        }, { threshold: 0.5 });
        observer.observe(root);
    } else {
        analytics.trackViewed();
    }

    window.addEventListener('pagehide', () => pbrPreview?.dispose(), { once: true });

    matchColor(elements.hex.value);
}());
