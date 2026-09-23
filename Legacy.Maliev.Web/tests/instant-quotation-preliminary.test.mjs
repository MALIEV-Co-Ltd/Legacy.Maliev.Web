import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import vm from 'node:vm';

function loadPreviewHarness() {
  const source = fs.readFileSync(
    path.join(process.cwd(), 'wwwroot', 'src', 'app', 'js', 'instant-quotation-preliminary.js'),
    'utf8',
  );
  let written = '';
  const events = [];
  const popup = {
    document: {
      open() {},
      write(value) { written = value; },
      close() {},
    },
    focus() {},
  };
  const window = {
    CSS: { escape: value => String(value) },
    open() { return popup; },
    malievAnalytics: { emit(event) { events.push(event); } },
    setTimeout,
  };
  const document = {
    querySelector() { return null; },
  };
  const context = vm.createContext({ window, document, Intl, Date, setTimeout });
  vm.runInContext(source, context, { filename: 'instant-quotation-preliminary.js' });
  return { open: window.malievPreliminaryQuotation.open, get written() { return written; }, events };
}

function snapshot() {
  return {
    locale: 'en',
    currency: 'THB',
    logoDataUri: 'data:image/png;base64,canonical-logo',
    text: {
      preliminaryQuotation: 'Preliminary quotation',
      preliminaryQuotationNote: 'Review only · print or download as PDF',
      preliminaryQuotationPrint: 'Print',
      preliminaryQuotationDownloadPdf: 'Download as PDF',
      preliminaryQuotationDownloadPdfHint: 'Opens the print dialog; choose Save as PDF',
      preliminaryQuotationClose: 'Close',
      reviewOnly: 'Preliminary quotation for review only.',
      generated: 'Generated from the current estimate',
      preliminaryQuotationGenerated: 'Prepared',
      preliminaryQuotationParts: 'Part details',
      parts: 'Parts',
      part: 'Part',
      material: 'Material',
      color: 'Color',
      buildPreference: 'Build preference',
      quantity: 'Quantity',
      unitPrice: 'Unit price',
      lineSubtotal: 'Subtotal',
      technicalFilamentMinimum: 'Technical filament preparation minimum included',
      printTime: 'Estimated print time per part',
      day: 'day',
      days: 'days',
      hour: 'hr',
      hours: 'hrs',
      minute: 'min',
      minutes: 'mins',
      dimensions: 'Dimensions',
      volume: 'Volume',
      surfaceArea: 'Surface area',
      minThickness: 'Min. thickness',
      dfmAnalysis: 'DFM Analysis',
      dfmClear: 'No automatic DFM warnings were found.',
      summaryTitle: 'Price and lead-time summary',
      printingSubtotal: '3D printing subtotal',
      minimumOrderMinimum: 'minimum',
      surcharge: 'Minimum order surcharge',
      shipping: 'Estimated shipping',
      vat: 'VAT',
      leadTime: 'Lead time',
      afterConfirmed: 'after order confirmation',
      grandTotal: 'Grand total',
      reviewIntro: 'Review each item before entering your contact details.',
    },
    parts: [{
      partId: 'part-a',
      partNumber: 1,
      fileName: 'bracket.stl',
      material: 'PLA',
      color: 'Black',
      buildPreference: 'Standard',
      quantity: 2,
      unitPrice: 100,
      subtotal: 200,
      technicalFilamentMinimumApplied: true,
      technicalFilamentMinimumAdjustment: 300,
      printTimeMinutes: 60,
      dimensionXmm: 10,
      dimensionYmm: 20,
      dimensionZmm: 30,
      volumeCm3: 6,
      surfaceAreaCm2: 12,
      minThicknessMm: 1,
      dfmWarnings: [],
    }],
    totals: {
      subtotal: 200,
      minimumOrderPrice: 500,
      minimumOrderSurcharge: 50,
      shipping: 100,
      vat: 24.5,
      total: 374.5,
      leadTimeMinimumDays: 3,
      leadTimeMaximumDays: 5,
    },
  };
}

test('preliminary quotation opens a localized A4 review document with safe summary data', () => {
  const harness = loadPreviewHarness();
  harness.open(snapshot());

  assert.match(harness.written, /@page \{ size: A4/);
  assert.match(harness.written, /PrintPreliminaryQuotation/);
  assert.match(harness.written, /DownloadPreliminaryQuotationPdf/);
  assert.match(harness.written, /Estimated print time per part/);
  assert.match(harness.written, />1 hr</);
  assert.match(harness.written, /Technical filament preparation minimum included/);
  assert.match(harness.written, /300\.00 THB/);
  assert.match(harness.written, /DFM Analysis/);
  assert.match(harness.written, /156px/);
  assert.match(harness.written, /object-fit: contain/);
  assert.match(harness.written, /break-inside: avoid/);
  assert.match(harness.written, /window\.print\(\)/);
  assert.match(harness.written, /data:image\/png;base64,canonical-logo/);
  assert.doesNotMatch(harness.written, /access_token|refresh_token|storagePath|sessionId/i);
  assert.equal(harness.events.length, 1);
  assert.equal(harness.events[0].event, 'preliminary_quotation_opened');
  assert.equal(harness.events[0].file_count, 1);
});

test('preliminary quotation formats long and invalid print durations without changing numeric inputs', () => {
  const harness = loadPreviewHarness();
  const value = snapshot();
  const cases = [
    [1, '1 min'],
    [60, '1 hr'],
    [917.4, '15 hrs 17 mins'],
    [1501, '1 day 1 hr 1 min'],
    [0, '—'],
  ];

  for (const [minutes, label] of cases) {
    value.parts[0].printTimeMinutes = minutes;
    harness.open(value);
    assert.ok(harness.written.includes('>' + label + '<'), `Expected duration ${label}`);
    assert.equal(value.parts[0].printTimeMinutes, minutes);
  }
});

test('preliminary quotation uses Thai duration units', () => {
  const harness = loadPreviewHarness();
  const value = snapshot();
  value.locale = 'th';
  Object.assign(value.text, {
    day: 'วัน', days: 'วัน', hour: 'ชม.', hours: 'ชม.', minute: 'นาที', minutes: 'นาที',
  });
  value.parts[0].printTimeMinutes = 1501;

  harness.open(value);

  assert.match(harness.written, />1 วัน 1 ชม\. 1 นาที</);
});

test('preliminary quotation omits the technical filament minimum when it is not applied', () => {
  const harness = loadPreviewHarness();
  const value = snapshot();
  value.parts[0].technicalFilamentMinimumApplied = false;
  harness.open(value);

  assert.doesNotMatch(harness.written, /Technical filament preparation minimum included/);
});

test('preliminary quotation refuses an empty or missing-part snapshot', () => {
  const harness = loadPreviewHarness();
  harness.open({ ...snapshot(), parts: [] });
  assert.equal(harness.written, '');
  assert.deepEqual(harness.events, []);
});
