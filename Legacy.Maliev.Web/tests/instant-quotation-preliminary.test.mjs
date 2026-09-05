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
