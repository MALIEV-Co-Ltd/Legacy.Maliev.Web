(function (root, factory) {
    const api = factory();

    if (typeof module === 'object' && module.exports) {
        module.exports = api;
    }

    root.MalievFinishColorMatcherCore = api;
}(typeof globalThis !== 'undefined' ? globalThis : this, function () {
    'use strict';

    const radians = Math.PI / 180;
    const degrees = 180 / Math.PI;

    function normalizeHue(value) {
        return value < 0 ? value + 360 : value;
    }

    function deltaE00(first, second) {
        const lBar = (first.l + second.l) / 2;
        const c1 = Math.hypot(first.a, first.b);
        const c2 = Math.hypot(second.a, second.b);
        const cBar = (c1 + c2) / 2;
        const cBar7 = Math.pow(cBar, 7);
        const g = 0.5 * (1 - Math.sqrt(cBar7 / (cBar7 + Math.pow(25, 7))));
        const a1Prime = (1 + g) * first.a;
        const a2Prime = (1 + g) * second.a;
        const c1Prime = Math.hypot(a1Prime, first.b);
        const c2Prime = Math.hypot(a2Prime, second.b);
        const h1Prime = c1Prime === 0 ? 0 : normalizeHue(Math.atan2(first.b, a1Prime) * degrees);
        const h2Prime = c2Prime === 0 ? 0 : normalizeHue(Math.atan2(second.b, a2Prime) * degrees);
        const deltaLPrime = second.l - first.l;
        const deltaCPrime = c2Prime - c1Prime;
        let deltaHuePrime = h2Prime - h1Prime;

        if (c1Prime * c2Prime === 0) {
            deltaHuePrime = 0;
        } else if (deltaHuePrime > 180) {
            deltaHuePrime -= 360;
        } else if (deltaHuePrime < -180) {
            deltaHuePrime += 360;
        }

        const deltaHPrime = 2 * Math.sqrt(c1Prime * c2Prime) * Math.sin((deltaHuePrime / 2) * radians);
        const lBarPrime = (first.l + second.l) / 2;
        const cBarPrime = (c1Prime + c2Prime) / 2;
        let hBarPrime;

        if (c1Prime * c2Prime === 0) {
            hBarPrime = h1Prime + h2Prime;
        } else if (Math.abs(h1Prime - h2Prime) <= 180) {
            hBarPrime = (h1Prime + h2Prime) / 2;
        } else if (h1Prime + h2Prime < 360) {
            hBarPrime = (h1Prime + h2Prime + 360) / 2;
        } else {
            hBarPrime = (h1Prime + h2Prime - 360) / 2;
        }

        const t = 1
            - (0.17 * Math.cos((hBarPrime - 30) * radians))
            + (0.24 * Math.cos(2 * hBarPrime * radians))
            + (0.32 * Math.cos((3 * hBarPrime + 6) * radians))
            - (0.20 * Math.cos((4 * hBarPrime - 63) * radians));
        const deltaTheta = 30 * Math.exp(-Math.pow((hBarPrime - 275) / 25, 2));
        const cBarPrime7 = Math.pow(cBarPrime, 7);
        const rC = 2 * Math.sqrt(cBarPrime7 / (cBarPrime7 + Math.pow(25, 7)));
        const sL = 1 + (0.015 * Math.pow(lBarPrime - 50, 2)) / Math.sqrt(20 + Math.pow(lBarPrime - 50, 2));
        const sC = 1 + 0.045 * cBarPrime;
        const sH = 1 + 0.015 * cBarPrime * t;
        const rT = -Math.sin(2 * deltaTheta * radians) * rC;
        const lTerm = deltaLPrime / sL;
        const cTerm = deltaCPrime / sC;
        const hTerm = deltaHPrime / sH;

        return Math.sqrt(
            Math.pow(lTerm, 2)
            + Math.pow(cTerm, 2)
            + Math.pow(hTerm, 2)
            + rT * cTerm * hTerm);
    }

    function hexToLab(hex) {
        if (!/^#[0-9a-f]{6}$/i.test(hex)) {
            throw new TypeError('A six-digit hexadecimal color is required.');
        }

        const channels = [1, 3, 5].map((offset) => parseInt(hex.slice(offset, offset + 2), 16) / 255)
            .map((channel) => channel <= 0.04045 ? channel / 12.92 : Math.pow((channel + 0.055) / 1.055, 2.4));
        const [red, green, blue] = channels;
        const x = (0.4360747 * red + 0.3850649 * green + 0.1430804 * blue) / 0.96422;
        const y = 0.2225045 * red + 0.7168786 * green + 0.0606169 * blue;
        const z = (0.0139322 * red + 0.0971045 * green + 0.7141733 * blue) / 0.82521;
        const transform = (value) => value > 216 / 24389
            ? Math.cbrt(value)
            : (841 / 108) * value + 4 / 29;
        const fx = transform(x);
        const fy = transform(y);
        const fz = transform(z);

        return {
            l: 116 * fy - 16,
            a: 500 * (fx - fy),
            b: 200 * (fy - fz),
        };
    }

    function findNearest(color, references, limit) {
        return references
            .map((reference) => ({ ...reference, deltaE: deltaE00(color, reference) }))
            .sort((left, right) => left.deltaE - right.deltaE || left.code.localeCompare(right.code))
            .slice(0, limit);
    }

    function findRecommendations(color, references, limit) {
        const scoredReferences = references
            .map((reference) => ({ ...reference, deltaE: deltaE00(color, reference) }))
            .sort((left, right) => left.deltaE - right.deltaE || left.code.localeCompare(right.code));
        const closestReference = scoredReferences[0];

        if (!closestReference || limit <= 0) {
            return [];
        }

        const chroma = (value) => Math.hypot(value.a, value.b);
        const hue = (value) => {
            const degrees = Math.atan2(value.b, value.a) * 180 / Math.PI;
            return degrees < 0 ? degrees + 360 : degrees;
        };
        const hueDistance = (left, right) => {
            const distance = Math.abs(left - right);
            return Math.min(distance, 360 - distance);
        };
        const targetChroma = chroma(color);
        const anchorChroma = chroma(closestReference);
        const anchorHue = hue(closestReference);
        const isNeutral = targetChroma < 8 || anchorChroma < 8;
        const minimumTonalChroma = Math.max(6, anchorChroma * 0.12);
        const enrich = (reference) => ({
            ...reference,
            chroma: chroma(reference),
            hueDistance: hueDistance(hue(reference), anchorHue),
        });
        const enriched = scoredReferences.map(enrich);
        let family = enriched.filter((reference) => isNeutral
            ? reference.chroma <= 12
            : reference.hueDistance <= 20 && reference.chroma >= minimumTonalChroma);

        if (family.length < limit && !isNeutral) {
            family = enriched.filter((reference) => reference.hueDistance <= 30 && reference.chroma >= minimumTonalChroma);
        }

        const familyScore = (reference) => reference.deltaE
            + (isNeutral ? reference.chroma * 0.15 : reference.hueDistance * 0.35)
            + Math.abs(reference.chroma - anchorChroma) * 0.08;
        const bestAtEachLightness = new Map();

        family.forEach((reference) => {
            const current = bestAtEachLightness.get(reference.l);
            if (!current || familyScore(reference) < familyScore(current)
                || (familyScore(reference) === familyScore(current) && reference.code.localeCompare(current.code) < 0)) {
                bestAtEachLightness.set(reference.l, reference);
            }
        });

        const recommendations = Array.from(bestAtEachLightness.values())
            .sort((left, right) => left.deltaE - right.deltaE || left.code.localeCompare(right.code))
            .slice(0, limit);

        if (recommendations.length < limit) {
            const selectedCodes = new Set(recommendations.map((reference) => reference.code));
            family
                .filter((reference) => !selectedCodes.has(reference.code))
                .sort((left, right) => familyScore(left) - familyScore(right) || left.code.localeCompare(right.code))
                .slice(0, limit - recommendations.length)
                .forEach((reference) => recommendations.push(reference));
        }

        return recommendations
            .sort((left, right) => right.l - left.l || left.deltaE - right.deltaE || left.code.localeCompare(right.code));
    }

    function primerTone(lightness) {
        if (lightness >= 70) {
            return 'light';
        }

        if (lightness <= 35) {
            return 'dark';
        }

        return 'medium';
    }

    function averagePixelColor(pixels) {
        const totals = [0, 0, 0];
        let count = 0;

        for (let index = 0; index < pixels.length; index += 4) {
            if (pixels[index + 3] > 0) {
                totals[0] += pixels[index];
                totals[1] += pixels[index + 1];
                totals[2] += pixels[index + 2];
                count += 1;
            }
        }

        if (count === 0) {
            return null;
        }

        return `#${totals
            .map((total) => Math.round(total / count).toString(16).padStart(2, '0'))
            .join('')}`.toUpperCase();
    }

    function buildQuoteUrl(options) {
        const parameters = new URLSearchParams({
            item: '3d-printing',
            culture: options.culture,
            finish_hex: options.sourceHex.toUpperCase(),
            finish_hlc: options.reference.code,
            finish_lab: [options.reference.l, options.reference.a, options.reference.b]
                .map((value) => Number(value).toFixed(3))
                .join(','),
            finish_sheen: options.sheen,
        });

        if (options.pantone && options.pantone.trim()) {
            parameters.set('finish_pantone', options.pantone.trim());
        }

        return `${options.baseUrl}?${parameters.toString()}`;
    }

    function buildDiagnosticEvent(eventName, fields, locale) {
        const event = {
            event: eventName,
            service_id: '3d_printing',
            intent: 'finish_color_matcher',
            locale: locale === 'th' ? 'th' : 'en',
            source: 'finishing_and_color',
        };
        const inputMethods = ['color_picker', 'default', 'guidance', 'hex', 'image', 'pantone', 'recommendation', 'sheen'];
        const selectionTypes = ['closest', 'tonal_alternative'];
        const sheens = ['matte', 'satin', 'gloss'];
        const matchQualities = ['close', 'noticeable', 'large_difference'];
        const guidanceTopics = ['color_science', 'painting_plan', 'pantone_handoff'];
        const inputMethod = fields?.input_method;
        const selectionType = fields?.selection_type;
        const sheen = fields?.sheen;
        const matchQuality = fields?.match_quality;
        const stepNumber = fields?.step_number;

        if (eventName === 'finish_matcher_viewed') {
            return event;
        }

        if (eventName === 'finish_matcher_started' && inputMethods.includes(inputMethod)) {
            return { ...event, input_method: inputMethod };
        }

        if (eventName === 'finish_matcher_guidance_opened' && guidanceTopics.includes(fields?.guidance_topic)) {
            return { ...event, guidance_topic: fields.guidance_topic };
        }

        if (eventName === 'finish_matcher_error' && ['hex', 'image'].includes(inputMethod)) {
            return { ...event, failure_category: 'validation', input_method: inputMethod };
        }

        const hasValidSelection = Number.isInteger(stepNumber)
            && stepNumber >= 1
            && stepNumber <= 10
            && selectionTypes.includes(selectionType)
            && sheens.includes(sheen)
            && matchQualities.includes(matchQuality);

        if (eventName === 'finish_matcher_recommendation_selected' && hasValidSelection) {
            return {
                ...event,
                step_number: stepNumber,
                selection_type: selectionType,
                sheen,
                match_quality: matchQuality,
            };
        }

        if (eventName === 'finish_matcher_quote_clicked'
            && hasValidSelection
            && inputMethods.includes(inputMethod)
            && typeof fields?.has_pantone === 'boolean') {
            return {
                ...event,
                step_number: stepNumber,
                selection_type: selectionType,
                sheen,
                match_quality: matchQuality,
                input_method: inputMethod,
                has_pantone: fields.has_pantone,
            };
        }

        return null;
    }

    function createDiagnosticTracker(locale, emit) {
        let viewed = false;
        let activationMethod = '';
        let quoteClicked = false;
        const guidanceTopics = new Set();

        function push(eventName, fields) {
            const event = buildDiagnosticEvent(eventName, fields || {}, locale);
            if (event) {
                emit(event);
            }
        }

        function trackViewed() {
            if (viewed) {
                return;
            }

            viewed = true;
            push('finish_matcher_viewed');
        }

        function ensureStarted(inputMethod) {
            trackViewed();
            if (!activationMethod) {
                activationMethod = inputMethod;
                push('finish_matcher_started', { input_method: inputMethod });
            }

            return activationMethod;
        }

        function trackRecommendation(fields, selectionChanged) {
            ensureStarted('recommendation');
            if (selectionChanged) {
                push('finish_matcher_recommendation_selected', fields);
            }
        }

        function trackGuidance(topic) {
            ensureStarted('guidance');
            if (!guidanceTopics.has(topic)) {
                guidanceTopics.add(topic);
                push('finish_matcher_guidance_opened', { guidance_topic: topic });
            }
        }

        function trackError(inputMethod) {
            ensureStarted(inputMethod);
            push('finish_matcher_error', { input_method: inputMethod });
        }

        function trackQuote(fields, hasPantone) {
            const inputMethod = ensureStarted('default');
            if (quoteClicked) {
                return;
            }

            quoteClicked = true;
            push('finish_matcher_quote_clicked', {
                ...fields,
                input_method: inputMethod,
                has_pantone: Boolean(hasPantone),
            });
        }

        return { trackViewed, ensureStarted, trackRecommendation, trackGuidance, trackError, trackQuote };
    }

    return { deltaE00, hexToLab, findNearest, findRecommendations, primerTone, averagePixelColor, buildQuoteUrl, buildDiagnosticEvent, createDiagnosticTracker };
}));
