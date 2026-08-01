const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const matcher = require('../wwwroot/src/app/js/finish-color-matcher-core.js');

test('CIEDE2000 matches the published Sharma reference pair', () => {
    const referencePairs = [
        [{ l: 50, a: 2.6772, b: -79.7751 }, { l: 50, a: 0, b: -82.7485 }, 2.0425],
        [{ l: 50, a: 3.1571, b: -77.2803 }, { l: 50, a: 0, b: -82.7485 }, 2.8615],
        [{ l: 50, a: 2.8361, b: -74.0200 }, { l: 50, a: 0, b: -82.7485 }, 3.4412],
        [{ l: 50, a: -1.3802, b: -84.2814 }, { l: 50, a: 0, b: -82.7485 }, 1.0000],
    ];

    referencePairs.forEach(([first, second, expected]) => {
        assert.ok(Math.abs(matcher.deltaE00(first, second) - expected) < 0.0001);
    });
});

test('sRGB conversion uses D50 Lab coordinates', () => {
    const white = matcher.hexToLab('#ffffff');
    const black = matcher.hexToLab('#000000');

    assert.ok(Math.abs(white.l - 100) < 0.001);
    assert.ok(Math.abs(white.a) < 0.01);
    assert.ok(Math.abs(white.b) < 0.01);
    assert.ok(Math.abs(black.l) < 0.001);
    assert.ok(Math.abs(black.a) < 0.001);
    assert.ok(Math.abs(black.b) < 0.001);

    const red = matcher.hexToLab('#ff0000');
    assert.ok(Math.abs(red.l - 54.2917) < 0.001);
    assert.ok(Math.abs(red.a - 80.8125) < 0.001);
    assert.ok(Math.abs(red.b - 69.8851) < 0.001);
});

test('nearest references are ranked by delta E and respect the requested limit', () => {
    const references = Array.from({ length: 25 }, (_, index) => ({
        code: `H000_L${String(index).padStart(2, '0')}_C000`,
        l: index,
        a: 0,
        b: 0,
        hex: '#000000',
    }));

    const ten = matcher.findNearest({ l: 12, a: 0, b: 0 }, references, 10);
    const twenty = matcher.findNearest({ l: 12, a: 0, b: 0 }, references, 20);

    assert.equal(ten.length, 10);
    assert.equal(twenty.length, 20);
    assert.equal(ten[0].code, 'H000_L12_C000');
    assert.equal(ten[0].deltaE, 0);
    assert.ok(ten.every((entry, index) => index === 0 || entry.deltaE >= ten[index - 1].deltaE));
});

test('recommendations choose one best candidate per lightness level and arrange a tonal ladder', () => {
    const references = Array.from({ length: 20 }, (_, index) => [
        {
            code: `H000_L${String(index * 5).padStart(2, '0')}_C000`,
            l: index * 5,
            a: 0,
            b: 0,
            hex: '#000000',
        },
        {
            code: `H180_L${String(index * 5).padStart(2, '0')}_C100`,
            l: index * 5,
            a: -100,
            b: 0,
            hex: '#00FFFF',
        },
    ]).flat();
    const target = { l: 45, a: 0, b: 0 };
    const recommendations = matcher.findRecommendations(target, references, 10);

    assert.equal(recommendations.length, 10);
    assert.equal(new Set(recommendations.map((entry) => entry.l)).size, 10);
    assert.ok(recommendations.every((entry) => entry.code.startsWith('H000_')));
    assert.ok(recommendations.every((entry, index) => index === 0 || entry.l <= recommendations[index - 1].l));
    assert.equal(recommendations.find((entry) => entry.deltaE === 0).code, 'H000_L45_C000');
});

test('saturated tonal recommendations stay in the selected hue family instead of adding grey fillers', () => {
    const target = { l: 35, a: 20.52, b: -56.38 };
    const references = Array.from({ length: 10 }, (_, index) => {
        const lightness = 15 + (index * 8);
        return [
            {
                code: `H290_L${String(lightness).padStart(2, '0')}_C060`,
                l: lightness,
                a: 20.52,
                b: -56.38,
                hex: '#1448CE',
            },
            {
                code: `H240_L${String(lightness).padStart(2, '0')}_C005`,
                l: lightness,
                a: -2.5,
                b: -4.33,
                hex: '#41484D',
            },
        ];
    }).flat();

    const recommendations = matcher.findRecommendations(target, references, 10);

    assert.equal(recommendations.length, 10);
    assert.ok(recommendations.every((entry) => entry.code.startsWith('H290_')));
    assert.ok(recommendations.every((entry, index) => index === 0 || entry.l <= recommendations[index - 1].l));
});

test('primer starting point follows selected reference lightness bands', () => {
    assert.equal(matcher.primerTone(70), 'light');
    assert.equal(matcher.primerTone(69.99), 'medium');
    assert.equal(matcher.primerTone(35.01), 'medium');
    assert.equal(matcher.primerTone(35), 'dark');
});

test('quotation URL carries a complete encoded color handoff', () => {
    const url = new URL(matcher.buildQuoteUrl({
        baseUrl: '/Quotation/Index',
        culture: 'th',
        sourceHex: '#A2474F',
        reference: { code: 'H010_L40_C035', l: 40, a: 34.468, b: 6.078 },
        pantone: 'PANTONE 18-1540 TCX',
        sheen: 'satin',
    }), 'https://www.maliev.com');

    assert.equal(url.pathname, '/Quotation/Index');
    assert.equal(url.searchParams.get('item'), '3d-printing');
    assert.equal(url.searchParams.get('culture'), 'th');
    assert.equal(url.searchParams.get('finish_hex'), '#A2474F');
    assert.equal(url.searchParams.get('finish_hlc'), 'H010_L40_C035');
    assert.equal(url.searchParams.get('finish_lab'), '40.000,34.468,6.078');
    assert.equal(url.searchParams.get('finish_pantone'), 'PANTONE 18-1540 TCX');
    assert.equal(url.searchParams.get('finish_sheen'), 'satin');
});

test('matcher analytics exposes only the approved scalar funnel contract', () => {
    assert.deepEqual(matcher.buildDiagnosticEvent('finish_matcher_viewed', { pantone: 'secret', hex: '#ffffff' }, 'en'), {
        event: 'finish_matcher_viewed',
        service_id: '3d_printing',
        intent: 'finish_color_matcher',
        locale: 'en',
        source: 'finishing_and_color',
    });

    assert.deepEqual(matcher.buildDiagnosticEvent('finish_matcher_quote_clicked', {
        step_number: 5,
        selection_type: 'closest',
        sheen: 'satin',
        match_quality: 'close',
        input_method: 'hex',
        has_pantone: true,
        pantone: 'PANTONE 18-1540 TCX',
        source_hex: '#A2474F',
        file_name: 'customer-part.png',
    }, 'th'), {
        event: 'finish_matcher_quote_clicked',
        service_id: '3d_printing',
        intent: 'finish_color_matcher',
        locale: 'th',
        source: 'finishing_and_color',
        step_number: 5,
        selection_type: 'closest',
        sheen: 'satin',
        match_quality: 'close',
        input_method: 'hex',
        has_pantone: true,
    });

    assert.equal(matcher.buildDiagnosticEvent('finish_matcher_quote_clicked', {
        step_number: 11,
        selection_type: 'closest',
        sheen: 'satin',
        match_quality: 'close',
        input_method: 'hex',
        has_pantone: true,
    }, 'en'), null);
    assert.equal(matcher.buildDiagnosticEvent('finish_matcher_unknown', {}, 'en'), null);
});

test('matcher analytics orders and deduplicates the diagnostic funnel', () => {
    const events = [];
    const tracker = matcher.createDiagnosticTracker('en', (event) => events.push(event));
    const selection = {
        step_number: 5,
        selection_type: 'closest',
        sheen: 'gloss',
        match_quality: 'close',
    };

    tracker.trackViewed();
    tracker.trackViewed();
    tracker.ensureStarted('hex');
    tracker.ensureStarted('sheen');
    tracker.trackRecommendation(selection, false);
    tracker.trackRecommendation({ ...selection, step_number: 4, selection_type: 'tonal_alternative' }, true);
    tracker.trackGuidance('painting_plan');
    tracker.trackGuidance('painting_plan');
    tracker.trackError('hex');
    tracker.trackQuote(selection, true);
    tracker.trackQuote(selection, true);

    assert.deepEqual(events.map((event) => event.event), [
        'finish_matcher_viewed',
        'finish_matcher_started',
        'finish_matcher_recommendation_selected',
        'finish_matcher_guidance_opened',
        'finish_matcher_error',
        'finish_matcher_quote_clicked',
    ]);
    assert.equal(events[1].input_method, 'hex');
    assert.equal(events.at(-1).input_method, 'hex');
    assert.equal(events.at(-1).has_pantone, true);
});

test('image sampling averages visible pixels and ignores transparent pixels', () => {
    const pixels = new Uint8ClampedArray([
        16, 32, 48, 255,
        32, 64, 96, 255,
        255, 255, 255, 0,
    ]);

    assert.equal(matcher.averagePixelColor(pixels), '#183048');
    assert.equal(matcher.averagePixelColor(new Uint8ClampedArray([0, 0, 0, 0])), null);
});

test('bundled HLC atlas preserves the verified publisher metadata and records', () => {
    const sandbox = { window: {} };
    const dataPath = path.resolve(__dirname, '../wwwroot/src/app/js/hlc-colour-atlas-data.js');
    vm.runInNewContext(fs.readFileSync(dataPath, 'utf8'), sandbox);
    const atlas = sandbox.window.MalievHlcColourAtlas;

    assert.equal(atlas.version, '2.03');
    assert.equal(atlas.publisher, 'freieFarbe e.V.');
    assert.equal(atlas.license, 'CC BY-ND 4.0');
    assert.equal(atlas.colours.length, 2041);
    assert.deepEqual(Array.from(atlas.colours[0]), ['H000_L00_C000', 0, 0, 0, '#000000']);
    assert.deepEqual(Array.from(atlas.colours.at(-1)), ['H360_L95_C005', 95, 5, 0, '#FAEDF1']);
});

test('guided matcher source preserves the explanation, finish brief, and keyboard sampling contracts', () => {
    const pagePath = path.resolve(__dirname, '../Components/Pages/Services/FinishingAndColorPage.razor');
    const scriptPath = path.resolve(__dirname, '../wwwroot/src/app/js/finish-color-matcher.js');
    const previewPath = path.resolve(__dirname, '../wwwroot/src/app/js/finish-color-matcher-preview.js');
    const corePath = path.resolve(__dirname, '../wwwroot/src/app/js/finish-color-matcher-core.js');
    const stylePath = path.resolve(__dirname, '../wwwroot/src/app/css/service-pages.css');
    const page = fs.readFileSync(pagePath, 'utf8');
    const script = fs.readFileSync(scriptPath, 'utf8');
    const preview = fs.readFileSync(previewPath, 'utf8');
    const coreSource = fs.readFileSync(corePath, 'utf8');
    const style = fs.readFileSync(stylePath, 'utf8');

    assert.match(page, /10 guided HLC references/);
    assert.match(page, /Lower ΔE00 means a closer screen-to-reference match/);
    assert.match(page, /data-closest-position/);
    assert.match(page, /data-selection-status/);
    assert.match(page, /aria-describedby="finish-results-help"/);
    assert.match(page, /data-guidance-topic="color_science"/);
    assert.match(page, /data-guidance-topic="painting_plan"/);
    assert.match(page, /data-guidance-topic="pantone_handoff"/);
    assert.doesNotMatch(page, /data-show-more/);
    assert.match(page, /What HLC, Lab, and ΔE00 mean/);
    assert.match(page, /data-selected-sheen/);
    assert.match(page, /data-sheen-canvas/);
    assert.match(page, /data-sheen-preview/);
    assert.match(page, /data-sheen-canvas/);
    assert.doesNotMatch(page, /finish-preview-chip/);
    assert.doesNotMatch(page, /Three\.js · PBR/);
    assert.match(page, /data-primer-guidance/);
    assert.match(page, /Do not calculate a paint formula from a screen HEX value/);
    assert.match(script, /findRecommendations\(core\.hexToLab\(hex\), references, 10\)/);
    assert.match(script, /selectedCard\.offsetLeft/);
    assert.match(coreSource, /finish_matcher_viewed/);
    assert.match(coreSource, /finish_matcher_started/);
    assert.match(coreSource, /finish_matcher_recommendation_selected/);
    assert.match(coreSource, /finish_matcher_guidance_opened/);
    assert.match(coreSource, /finish_matcher_quote_clicked/);
    assert.match(coreSource, /finish_matcher_error/);
    assert.match(script, /analytics\.trackQuote\(selectionFields\(selectedReference\), Boolean\(elements\.pantone\.value\.trim\(\)\)\)/);
    assert.match(script, /pbrPreview\?\.update\(selectedReference\.hex, sheen\)/);
    assert.match(script, /finish-primer-value/);
    assert.match(script, /button\.tabIndex = isClosest \? 0 : -1/);
    assert.match(script, /elements\.selectionStatus\.textContent/);
    assert.match(script, /elements\.results\.addEventListener\('keydown'/);
    assert.match(script, /ArrowRight: 1/);
    assert.match(preview, /new THREE\.MeshPhysicalMaterial/);
    assert.match(preview, /clearcoatRoughness/);
    assert.match(preview, /THREE\.ACESFilmicToneMapping/);
    assert.match(preview, /convertSRGBToLinear/);
    assert.match(preview, /new THREE\.PMREMGenerator/);
    assert.match(preview, /antialias: true/);
    assert.match(preview, /targetRotation\.x = -0\.2 - \(y \* 0\.42\)/);
    assert.match(preview, /targetRotation\.y = -0\.16 \+ \(x \* 0\.52\)/);
    assert.match(preview, /prefers-reduced-motion: reduce/);
    assert.match(preview, /webglcontextlost/);
    assert.match(style, /\.finish-match-list \{[^}]*repeat\(2, minmax\(16rem, 1fr\)\)/);
    assert.match(style, /\.finish-match-title strong \{[^}]*white-space: nowrap/);
    assert.match(style, /\.finish-match-card\.is-closest-reference/);
    assert.match(style, /\.finish-attribution \{[^}]*font-size: \.75rem/);
    assert.match(style, /\.finish-sheen-preview \{ order: 7/);
    assert.match(style, /\.finish-sheen-stage \{ min-height: 10\.5rem/);
    assert.match(style, /::-webkit-color-swatch \{[^}]*border-radius/);
    assert.match(style, /\.finish-primer-guidance \{[^}]*font-weight: 400/);
    assert.match(style, /\.finish-primer-guidance \.finish-primer-value \{[^}]*display: inline[^}]*font-weight: 600/);
    assert.doesNotMatch(style, /@keyframes finish-sheen-tilt/);
    assert.match(script, /ArrowLeft/);
    assert.match(script, /showImageStatus\(text\('invalidImage'\), false\)/);
});
