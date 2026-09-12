'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const catalogPath = path.resolve(__dirname, '../..', 'tools/cnc-catalog/catalog.json');
const fresh = () => structuredClone(require(catalogPath));
const validate = value => require('../../tools/cnc-catalog/validate.cjs').validateCatalog(value);
const byId = (catalog, id) => catalog.tools.find(tool => tool.id === id);
test('local catalog exists and passes structural and provenance validation', () => {
    const catalog = require(catalogPath);
    const { validateCatalog } = require('../../tools/cnc-catalog/validate.cjs');
    assert.deepEqual(validateCatalog(catalog), []);
});

test('long neck records preserve short flutes independently of neck length', () => {
    const catalog = fresh();
    const thin = byId(catalog, 'misumi-tsc-em2lb-row-d1-lu30');
    assert.equal(thin.geometry.diameterMm, 1);
    assert.equal(thin.geometry.fluteLengthMm, 1.5);
    assert.equal(thin.geometry.underNeckLengthMm, 30);
    assert.equal(thin.geometry.neckDiameterMm, 0.95);
    assert.equal(thin.geometry.overallLengthMm, 70);
    assert.equal(thin.orderCode, null, 'internal row ID must not invent a configured SKU');
    const wide = byId(catalog, 'misumi-tsc-em2lb-row-d6-lu60');
    assert.equal(wide.geometry.fluteLengthMm, 9);
    assert.equal(wide.geometry.underNeckLengthMm, 60);
    assert.equal(wide.geometry.overallLengthMm, 100);
    assert.equal(byId(catalog, 'misumi-tsc-em2s-row-d16').geometry.fluteLengthMm, 32);
});

test('spotting and NACHI drills retain real lengths without creating tip or reach geometry', () => {
    const catalog = fresh();
    const drills = [62910, 62912, 62916].map(code => byId(catalog, `osg-spot-${code}`));
    assert.deepEqual(drills.map(t => [t.geometry.diameterMm, t.geometry.fluteLengthMm, t.geometry.overallLengthMm]),
        [[10, 30, 93], [12, 36, 108], [16, 41, 118]]);
    assert.ok(drills.every(t => t.geometry.pointDiameterMm === null && t.geometry.maxAxialDepthMm === null));
    const drill = byId(catalog, 'nachi-sdp-12p0');
    assert.equal(drill.orderCode, 'SDP12.0');
    assert.equal(drill.geometry.fluteLengthMm, 111);
    assert.equal(drill.geometry.overallLengthMm, 149);
    assert.equal(drill.geometry.pointLengthMm, 3.6);
    assert.equal(drill.geometry.includedAngleDegrees, 118);
    assert.equal(drill.geometry.shankDiameterMm, null);
});

test('face cutter bore and functional length are not a shank or assembled projection', () => {
    const cutter = byId(fresh(), 'mitsubishi-asx445-050a04r');
    assert.equal(cutter.geometry.diameterMm, 50);
    assert.equal(cutter.geometry.maxCuttingDiameterMm, 63);
    assert.equal(cutter.geometry.connectionBoreDiameterMm, 22);
    assert.equal(cutter.geometry.functionalLengthMm, 40);
    assert.equal(cutter.geometry.shankDiameterMm, null);
    assert.equal(cutter.geometry.overallLengthMm, null);
    assert.equal(cutter.geometry.maxAxialDepthMm, 6);
});

test('ball geometry and engraving tip uncertainties remain explicit', () => {
    const catalog = fresh();
    const ball = byId(catalog, 'ns-alb225-05011');
    assert.equal(ball.geometry.ballRadiusMm, 0.5);
    assert.equal(ball.geometry.fluteLengthMm, 0.75);
    assert.equal(ball.geometry.underNeckLengthMm, 5);
    const engraving = byId(catalog, 'harvey-engraving-993230');
    assert.equal(engraving.geometry.includedAngleDegrees, 60);
    assert.equal(engraving.geometry.fluteLengthMm, 2.7178);
    assert.equal(engraving.geometry.pointDiameterMm, null, 'web thickness is not tip diameter');
    assert.equal(engraving.geometry.diameterMm, null);
});

test('imperial dimensions and pitch use exact inch conversion, independent of tap envelope', () => {
    const catalog = fresh();
    const tap = byId(catalog, 'osg-imperial-tap-1430100');
    assert.equal(tap.geometry.overallLengthMm, 63.5);
    assert.equal(tap.geometry.threadLengthMm, 16.383);
    assert.equal(tap.geometry.fluteLengthMm, null);
    assert.equal(tap.geometry.diameterMm, null);
    assert.equal(tap.thread.nominalDiameterMm, 6.35);
    assert.equal(tap.thread.pitchMm, 1.27);
    const threadMill = byId(catalog, 'osg-thread-mill-4100000811');
    assert.equal(threadMill.geometry.diameterMm, 4.572);
    assert.equal(threadMill.thread.nominalDiameterMm, 6.35);
    assert.equal(threadMill.geometry.fluteLengthMm, 10.2108);
    threadMill.thread.pitchMm = 20;
    assert.match(validate(catalog).join('\n'), /inch pitch conversion/);
});

test('every requested family remains visible and long-neck gaps cannot masquerade as verified coverage', () => {
    const catalog = fresh();
    assert.equal(new Set(catalog.requestedCoverage.map(c => c.family)).size, 9);
    const neck = catalog.requestedCoverage.find(c => c.family === 'flat_end_mill' && c.variant === 'long_neck');
    assert.equal(neck.status, 'partial');
    assert.match(neck.gaps.join('\n'), /D16/);
    assert.ok(neck.verifiedToolIds.every(id => byId(catalog, id).geometry.diameterMm <= 12));
    const empty = structuredClone(catalog);
    empty.requestedCoverage[0].verifiedToolIds = [];
    assert.match(validate(empty).join('\n'), /catalogued coverage has no verified records/);
    const missingDiameter = structuredClone(catalog);
    const spots = missingDiameter.requestedCoverage.find(c => c.family === 'spot_drill');
    spots.verifiedToolIds.pop();
    assert.match(validate(missingDiameter).join('\n'), /catalogued coverage lacks diameter 16/);
    const wrongVariant = structuredClone(catalog);
    wrongVariant.requestedCoverage.find(c => c.variant === 'long_neck').verifiedToolIds.push('misumi-tsc-em2s-row-d16');
    assert.match(validate(wrongVariant).join('\n'), /coverage references wrong variant/);
    neck.status = 'catalogued';
    assert.match(validate(catalog).join('\n'), /catalogued coverage has gaps/);
});

test('schema rejects unknown fields, unknown-as-zero and non-finite dimensions', () => {
    for (const value of [0, -2, Infinity, NaN, '30']) {
        const catalog = fresh();
        catalog.tools[0].geometry.fluteLengthMm = value;
        assert.ok(validate(catalog).length > 0, `invalid dimension accepted: ${value}`);
    }
    const catalog = fresh();
    catalog.tools[0].geometry.reachMm = 80;
    assert.match(validate(catalog).join('\n'), /unexpected field reachMm/);
});

test('finite facts need exact field provenance and unavailable sources cannot verify them', () => {
    const missing = fresh();
    missing.tools[0].sourceEvidence[0].fields = ['manufacturer'];
    assert.match(validate(missing).join('\n'), /fact without field provenance geometry.fluteLengthMm/);
    const unavailable = fresh();
    unavailable.sources[0].status = 'unavailable';
    assert.match(validate(unavailable).join('\n'), /unavailable or unknown source/);
    const invented = fresh();
    invented.tools[0].sourceEvidence[0].fields.push('geometry.pointDiameterMm');
    assert.match(validate(invented).join('\n'), /missing\/null fact/);
});

test('manufacturer sources must have valid calendar dates and official URLs', () => {
    for (const date of ['2026-99-99', '2026-02-30']) {
        const source = fresh(); source.sources[0].checkedOn = date;
        assert.doesNotThrow(() => validate(source));
        assert.match(validate(source).join('\n'), /invalid checked date/);
        const catalog = fresh(); catalog.checkedOn = date;
        assert.doesNotThrow(() => validate(catalog));
        assert.match(validate(catalog).join('\n'), /invalid catalog checked date/);
    }
    const catalog = fresh();
    catalog.sources[0].sourceUrl = 'https://example.com/reseller-invented-geometry';
    assert.match(validate(catalog).join('\n'), /non-primary source URL/);
});

test('catalog facts and quoting assumptions cannot turn on machining authorization', () => {
    const catalog = fresh();
    assert.ok(catalog.tools.every(t => t.machiningAuthorized === false));
    assert.ok(catalog.quotingAssemblyAssumptions.every(a => a.machiningAuthorized === false));
    assert.equal(catalog.quotingAssemblyAssumptions[0].status, 'unverified_quoting_assumption');
    catalog.tools[0].machiningAuthorized = true;
    assert.ok(validate(catalog).length);
    const unsafe = fresh(); unsafe.quotingAssemblyAssumptions[0].machiningAuthorized = true;
    assert.ok(validate(unsafe).length);
});

test('duplicates, unknown coverage references and physically inconsistent records fail closed', () => {
    const duplicate = fresh(); duplicate.tools[1].id = duplicate.tools[0].id;
    assert.match(validate(duplicate).join('\n'), /duplicate tool id/);
    const badRef = fresh(); badRef.requestedCoverage[0].verifiedToolIds = ['missing-tool'];
    assert.match(validate(badRef).join('\n'), /wrong\/unverified tool/);
    const physical = fresh(); physical.tools[0].geometry.fluteLengthMm = 1000;
    assert.match(validate(physical).join('\n'), /flute exceeds overall length/);
    const mislabeled = fresh(); mislabeled.tools[0].recordStatus = 'requested_unverified';
    assert.match(validate(mislabeled).join('\n'), /requested coverage must not contain manufacturer facts/);
});

test('schema keyword drift fails explicitly instead of silently skipping checks', () => {
    const { validateSchema } = require('../../tools/cnc-catalog/validate.cjs');
    assert.throws(() => validateSchema(1, { unsupportedKeyword: true }), /Unsupported schema keyword/);
});

test('standalone validator requires all nine families and unique coverage keys', () => {
    const catalog = fresh();
    const index = catalog.requestedCoverage.findIndex(c => c.family === 'engraving_v_bit');
    catalog.requestedCoverage[index] = structuredClone(catalog.requestedCoverage.find(c => c.family === 'face_mill'));
    assert.match(validate(catalog).join('\n'), /duplicate coverage key/);
    assert.match(validate(catalog).join('\n'), /missing requested family engraving_v_bit/);
});

test('larger long-neck rows preserve measured columns and unknown neck diameter', () => {
    for (const [d, flute, lu, overall] of [[8, 12, 42, 100], [10, 15, 45, 100], [12, 20, 50, 110]]) {
        const tool = byId(fresh(), `misumi-vac-pem2lb-row-d${d}-lu${lu}`);
        assert.ok(tool);
        assert.deepEqual([tool.geometry.diameterMm, tool.geometry.fluteLengthMm, tool.geometry.underNeckLengthMm, tool.geometry.overallLengthMm], [d, flute, lu, overall]);
        assert.equal(tool.geometry.neckDiameterMm, null);
        assert.equal(tool.orderCode, null);
    }
});

test('metric single-form threading support does not invent M14x1 approval or pitch bounds', () => {
    const catalog = fresh();
    const tool = byId(catalog, 'harvey-thread-mill-826530');
    assert.ok(tool);
    assert.deepEqual([tool.geometry.diameterMm, tool.geometry.fluteLengthMm, tool.geometry.neckDiameterMm], [4.8, 1.109, 2.88]);
    assert.equal(tool.thread.pitchMm, null);
    assert.equal(tool.threadUsage.external, true);
    assert.equal(tool.threadUsage.internal, true);
    assert.equal(tool.threadUsage.minimumPitchMm, null);
    assert.equal(tool.threadUsage.maximumPitchMm, null);
    assert.equal(tool.machiningAuthorized, false);
    assert.match(catalog.requestedCoverage.find(c => c.family === 'thread_mill').gaps.join('\n'), /M14x1/);
    tool.threadUsage.minimumPitchMm = 1;
    assert.match(validate(catalog).join('\n'), /fact without field provenance threadUsage.minimumPitchMm/);
});
