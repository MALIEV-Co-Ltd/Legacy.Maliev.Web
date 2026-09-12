const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path');
const { createHash } = require('node:crypto');
const { runtime } = require('./cnc-model-worker-harness.cjs');
const webRoot = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot');
const fixture = fs.readFileSync(path.resolve(__dirname, '../TestAssets/Cnc/box-20x30x40.step'));
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const job = (extra = {}) => ({ action: 'parse', jobId: 'native-1', analysisProfile: 'cnc', extension: 'step', buffer: Uint8Array.from(fixture).buffer, ...extra });

test('pinned manifest and both byte streams fail closed and loader retries after failure', async () => {
    for (const name of ['manifest.js', 'occt-import-js.js', 'occt-import-js.wasm']) {
        const f = runtime(); f.state.corrupt = name;
        await assert.rejects(f.c.EnsureCncOcct(), /integrity mismatch/);
        assert.equal(f.factories.length, 0);
        f.state.corrupt = null;
        const loaded = await f.c.EnsureCncOcct();
        assert.equal(loaded.manifestSha256, sha(fs.readFileSync(path.join(webRoot, loaded.manifestUrl))));
        assert.equal(loaded.identity.jsSha256, '0f4759e678eafaf72a85e6a46ba3cf7cd324a88f7882d64d8dffdbaed25f2f90');
        assert.equal(Object.isFrozen(loaded.identity.sourceSha256), true);
        assert.equal(Object.keys(loaded.identity.sourceSha256).length, 25);
    }
    const f = runtime(); f.state.missing = 'occt-import-js.wasm';
    await assert.rejects(f.c.EnsureCncOcct(), /Unable to load/);
});

test('real parse establishes source authority; reconstructed or modified JSON cannot choose it', async () => {
    const f = runtime(), object = await f.c.RunParseJob(job());
    const topology = (await f.c.AnalyzeCncObject(object, f.c.AnalyzeObject(object))).cadTopology;
    assert.equal(topology.nativeInterpretation.interpretationAccepted, true);
    assert.equal(topology.automaticPlanningEligible, false);
    const transported = structuredClone(f.c.ExtractAnalysisMeshBuffers(object));
    transported[0].nativeImport.expectation = { sourceAttachmentVerified: true };
    const reconstructed = f.c.BuildObject3DFromMeshBuffers(transported);
    const untrusted = (await f.c.AnalyzeCncObject(reconstructed, f.c.AnalyzeObject(reconstructed))).cadTopology;
    assert.equal(untrusted.nativeInterpretation.interpretationAccepted, false);
    assert.ok(untrusted.unresolvedReasons.includes('native_loader_source_expectation_missing'));
    assert.equal(untrusted.automaticPlanningEligible, false);
    assert.ok(f.imports.filter(url => url.includes('/cnc-quotation/')).every(url => url.endsWith('?v=runtime-promotion')));
    const quotation = runtime(); quotation.c.importScripts('/src/app/js/cnc-quotation/cnc-quotation.worker.js');
    const evidence = quotation.c.CncPlanContracts.topologyEvidence(topology);
    assert.equal(evidence.cadDocument.nativeImport.nativeSemanticBasis, 'maliev-native-semantic-v2');
    assert.equal(typeof await quotation.c.CncPlanContracts.hash(evidence), 'string');
    assert.ok(quotation.imports.includes('/src/app/js/cnc-quotation/cnc-cad-document.js?v=runtime-promotion'));
});

test('real staged job retains verified source after actual transfer and consumes continuation exactly once', async () => {
    const f = runtime();
    const preview = await f.send(job({ deferCncAnalysis: true, retainNativeAnalysis: true }));
    assert.equal(preview.stage, 'native-preview');
    assert.equal(preview.analysisMeshes, null, 'same-worker analysis must not clone an unused native envelope');
    assert.ok(preview.meshes[0].position.length > 0);
    assert.ok(f.c.retainedCncJob.object3D.children[0].geometry.attributes.position.array.length > 0);
    const stale = await f.send({ action: 'continue-native-analysis', jobId: 'wrong' });
    assert.equal(stale.success, false);
    const final = await f.send({ action: 'continue-native-analysis', jobId: 'native-1' });
    assert.equal(final.success, true);
    assert.equal(final.cncGeometry.cadTopology.nativeInterpretation.interpretationAccepted, true);
    assert.equal(final.cncGeometry.cadTopology.automaticPlanningEligible, false);
    assert.equal(f.c.retainedCncJob, null);
    assert.equal((await f.send({ action: 'continue-native-analysis', jobId: 'native-1' })).success, false);
});

test('source mutation and policy disagreement are import integrity failures, then valid import resets', async () => {
    const f = runtime(); f.state.mutateSource = true;
    await assert.rejects(f.c.RunParseJob(job()), /source integrity/);
    f.state.mutateSource = false; f.state.mutatePolicy = true;
    await assert.rejects(f.c.RunParseJob(job()), /import integrity/);
    f.state.mutatePolicy = false;
    f.state.captureMalformed = true;
    const malformed = await f.send(job({ buffer: new TextEncoder().encode('invalid STEP').buffer }));
    f.state.captureMalformed = false;
    assert.equal(f.diagnostics.length, 1);
    assert.match(f.diagnostics[0], /Incorrect syntax: unexpected TYPE, expecting STEP/);
    assert.equal(malformed.success, false);
    const valid = await f.send(job());
    assert.equal(valid.success, true);
    assert.equal(valid.cncGeometry.cadTopology.nativeInterpretation.interpretationAccepted, true);
});

test('CNC and additive cached factories remain isolated in both loading orders', async () => {
    for (const first of ['cnc', 'additive']) {
        const f = runtime();
        const cnc = () => f.c.EnsureCncOcct(), additive = () => f.c.EnsureOcct();
        if (first === 'cnc') { await cnc(); await additive(); } else { await additive(); await cnc(); }
        await cnc(); await additive();
        assert.deepEqual(f.factories.sort(), ['additive', 'cnc']);
        const ordinary = await f.c.RunParseJob(job({ analysisProfile: 'additive' }));
        assert.equal(ordinary.userData.nativeImport, undefined);
    }
});
