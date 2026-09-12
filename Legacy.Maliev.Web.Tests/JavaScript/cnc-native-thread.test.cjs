const test = require('node:test'), assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path');
const h = require('./cnc-native-region-fixtures.cjs');
const { runtime: modelRuntime } = require('./cnc-model-worker-harness.cjs');
const { deliveryPage } = require('./cnc-native-thread-page-harness.cjs');
const savedRoot = path.resolve(__dirname, '../../.superpowers/sdd/2026-09-06-cnc-quotation-manufacturing-recovery/native-rotational-band-results');
const originals = [
    { file: 'PART 1_26082026.step', hash: '962358e16261a32ba4c32a693205a068fa7483d7dd95fdb6568b613ec87870f2', source: '4aded8f19c66c7a6fd526f88bbd8ec96a2b0f8796f915e7f4b633c259235ee0c', count: 2, faces: 74 },
    { file: 'Component6.step', hash: 'aa24f5fd52b5a0569bb01f22b32dbd2003e1d34a340a94d316f883cc1518879d', source: 'dbd2fc8260b4ac07df38c8e6dec6404f06b82fdf11a3f8d659689c7a0410d0f2', count: 8, faces: 350 },
    { file: 'Cover Plate_25.4mm.step', hash: 'cba7cc34fd49c2267373afbfd2d925f527d378791031db7a06f0ace1106468c1', source: '9b829284631a7479c917c7493c13e08655400392769aed977ed079d7d027e921', count: 2, faces: 70, partialEnd: true }
];
test('complete affine cylinder UV defines signed lead independently of traversal', () => {
    const c = h.runtime(), p = { type: 'bspline', degree: 1, rational: false, periodic: false,
        poles: [[0, 0], [2 * Math.PI, -1]], weights: [1, 1], knots: [0, 1], multiplicities: [2, 2] };
    const r = c.CncNativeThreadEvidence.affine(p, [0, 1]), I = c.CncNativeThreadEvidence.interval;
    const lead = I.mul(I.div(r.delta[1], r.delta[0]), [I.down(2 * Math.PI), I.up(2 * Math.PI)]);
    assert.ok(lead[0] <= -1 && lead[1] >= -1); assert.ok(lead[1] - lead[0] < 1e-12);
    assert.throws(() => c.CncNativeThreadEvidence.affine({ ...p, degree: 3 }, [0, 1]), /curve_unsupported/);
});
function independentFlank() {
    const k = 4 * (Math.sqrt(2) - 1) / 3, a = [[5, 0, 0], [5, 5 * k, 1 / 12], [5 * k, 5, 1 / 6], [0, 5, .25]];
    const dz = -1 / Math.sqrt(3), b = a.map(p => [p[0] * 6 / 5, p[1] * 6 / 5, p[2] + dz]);
    const poles = Array.from({ length: 4 }, (_, i) => a.map((p, j) => p.map((v, d) => ((3 - i) * v + i * b[j][d]) / 3)));
    const edges = [[[0, 0], [1, 0]], [[1, 0], [1, 1]], [[1, 1], [0, 1]], [[0, 1], [0, 0]]];
    return { orientation: 1, trims: { surfaceToImportWorld3x4: [1,0,0,0,0,1,0,0,0,0,1,0],
        surfaceBasis: { type: 'bspline', uDegree: 3, vDegree: 3, uPeriodic: false, vPeriodic: false, uRational: false, vRational: false,
            poles, weights: poles.map(row => row.map(() => 1)), uKnots: [0, 1], vKnots: [0, 1], uMultiplicities: [4, 4], vMultiplicities: [4, 4] },
        wires: [{ coedges: edges.map(([a,b]) => ({ pcurve: { type: 'line', origin: a, direction: b.map((x,i)=>x-a[i]) }, range: [0, 1] })) }] } };
}
const independentSupport = { origin: [0,0,0], axis: [0,0,1], xDirection: [1,0,0], yDirection: [0,1,0], direct: true, orientationSign: -1 };
test('shared flank admission requires that exact coedge helix and compatible whole-boundary continuation', () => {
    const c = h.runtime(), f = independentFlank(), profile = c.CncNativeThreadEvidence.profile(f, independentSupport, [5,6], .01);
    const use = { coedgeId:'flank/use',edgeId:'shared',orientation:0,startVertexId:'a',endVertexId:'b',
        pcurve:{type:'line',origin:[0,0],direction:[0,1]},range:[0,1] };
    const observation = {edgeId:'shared',coedgeId:'seed/use',wireId:'seed/wire',startVertexId:'a',endVertexId:'b',
        endpointsUV:[[[0,0],[0,0]],[[Math.PI/2,Math.PI/2],[.25,.25]]],sourceCompatibleLeadMm:[.99,1.01],sourceErrorMm:.001};
    const seed = {face:{faceId:'seed'},support:independentSupport,observations:[observation]};
    const rows = [{status:'bounded',faceId:'flank',wireId:'flank/wire',coedgeId:use.coedgeId,edgeId:use.edgeId,upperBoundMm:.001,obligationId:'native-metric'}];
    f.faceId='flank';
    const invoke = () => c.CncNativeThreadEvidence.sharedBoundary(f,{wire:{wireId:'flank/wire'},use},
        {faceId:'seed',wireId:'seed/wire',coedgeId:'seed/use'},seed,profile,independentSupport,rows);
    assert.ok(invoke().maximumAxialMismatchMm < 1e-10);
    // A different valid helix on the same cylinder is never enough.
    observation.edgeId='separate-helix'; assert.throws(invoke,/shared_boundary_not_helical/); observation.edgeId='shared';
    observation.coedgeId='other-occurrence'; assert.throws(invoke,/shared_boundary_not_helical/); observation.coedgeId='seed/use';
    observation.endpointsUV[1][1]=[0,0]; assert.throws(invoke,/shared_boundary_continuation/); observation.endpointsUV[1][1]=[.25,.25];
    const originalModel=profile.axialAffineModel;
    profile.axialAffineModel={...originalModel,valueAtOriginMm:[.125,.125],vSlopeMm:[0,0]};
    observation.sourceErrorMm=.2;
    assert.throws(invoke,/shared_boundary_continuation/,'a source tube must not disguise zero whole-boundary advance');
    profile.axialAffineModel=originalModel;observation.sourceErrorMm=.001;
    observation.sourceCompatibleLeadMm=[-.1,.1]; assert.throws(invoke,/shared_boundary_not_helical/);
    assert.throws(() => c.CncNativeThreadEvidence.compatibleBoundaryLead([[.99,1.01],[-1.01,-.99]]),/lead_incompatible/);
    assert.throws(() => c.CncNativeThreadEvidence.compatibleBoundaryLead([[.99,1.01],[1.99,2.01]]),/lead_incompatible/);
});
function recurrenceInput(starts, lead = 1, error = 1e-8) {
    const observations = [], flanks = [-1, 1].map(side => ({ face: { faceId: 'flank-' + side }, reference: independentSupport,
        profile: { axialPerRadialSlope: [side, side] }, edgeIds: [] }));
    for (const [j, side] of [-1, 1].entries()) for (let n = 0; n < starts; n++) {
        const id = 'edge-' + side + '-' + n, phase = j * .2 + n * Math.abs(lead) / starts;
        flanks[j].edgeIds.push(id);
        observations.push({ edgeId: id, coedgeId: id + '/use', sourceErrorMm: error,
            endpointsUV: [[[.1,.1],[phase,phase]], [[8 * Math.PI + .1,8 * Math.PI + .1],[phase + 4 * lead,phase + 4 * lead]]] });
    }
    return { seeds: [{ support: { ...independentSupport, radius: 5 }, observations }], flanks };
}
test('independent two-sided recurrence distinguishes RH/LH, multiple starts and ambiguous overlap', () => {
    const c = h.runtime();
    for (const hand of [1,-1]) for (const count of [1,2,3]) {
        const f = recurrenceInput(count, hand), lead = hand > 0 ? [.999999,1.000001] : [-1.000001,-.999999];
        const r = c.CncNativeThreadRecognition.measureSectionRecurrence(f.seeds,f.flanks,independentSupport,lead,5);
        assert.equal(r.startCount,count); assert.equal(r.sections.length,2);
        for (const seed of f.seeds) for (const o of seed.observations) o.endpointsUV.reverse();
        assert.equal(c.CncNativeThreadRecognition.measureSectionRecurrence(f.seeds,f.flanks,independentSupport,lead,5).startCount,count);
        // Reverse the proper native axis frame: both angular and axial
        // ordinates reverse, preserving intrinsic lead/chirality.
        for (const seed of f.seeds) {
            seed.support = { ...seed.support, axis: [0,0,-1], yDirection: [0,-1,0] };
            for (const o of seed.observations) o.endpointsUV = o.endpointsUV.map(p => p.map(v => [-v[1],-v[0]]));
        }
        assert.equal(c.CncNativeThreadRecognition.measureSectionRecurrence(f.seeds,f.flanks,independentSupport,lead,5).startCount,count);
    }
    const ambiguous = recurrenceInput(2,1,.3);
    assert.equal(c.CncNativeThreadRecognition.measureSectionRecurrence(ambiguous.seeds,ambiguous.flanks,independentSupport,[.9,1.1],5).startCount,null);
    const incomplete = recurrenceInput(1); incomplete.seeds[0].observations.pop();
    assert.equal(c.CncNativeThreadRecognition.measureSectionRecurrence(incomplete.seeds,incomplete.flanks,independentSupport,[.99,1.01],5).startCount,null);
});
test('compatible designation requires independent starts, pitch and opposite full-coefficient profile sides', () => {
    const c = h.runtime(), slope = 1 / Math.sqrt(3), flanks = [-1,1].map(sign => ({ profile: {
        axialPerRadialSlope: [sign*slope,sign*slope], coefficientResidualMm: 1e-12, ruledResidualMm: 1e-12 } }));
    const match = c.CncNativeThreadRecognition.compatibleNominal;
    assert.equal(match(false,[6.3705,6.942],[.999,1.001],{startCount:1},flanks).designation,'M14x1');
    assert.equal(match(false,[6.3705,6.942],[1.099,1.101],{startCount:1},flanks),null);
    assert.equal(match(false,[6.3705,6.942],[.999,1.001],{startCount:null},flanks),null);
    flanks[0].profile.axialPerRadialSlope = [-.7,-.7];
    assert.equal(match(false,[6.3705,6.942],[.999,1.001],{startCount:1},flanks),null);
});
test('a partial runout or extra freeform terminal cannot be replaced by seed-union depth', () => {
    const c = h.runtime(), uses = [{ edgeId: 'first' },{ edgeId: 'last' }];
    const flank = { face: { faceId: 'flank', trims: { wires: [{ coedges: uses }] } },
        reference: independentSupport, profile: { trimAxialEnvelopeMm: [0,4] } };
    const faces = new Map([['bottom',{support:{type:'plane',origin:[0,0,0],orientedNormal:[0,0,-1]}}],
        ['top',{support:{type:'plane',origin:[0,0,4],orientedNormal:[0,0,1]}}]]);
    const edges = new Map([['first',{uses:[{faceId:'flank'},{faceId:'bottom'}]}],['last',{uses:[{faceId:'flank'},{faceId:'top'}]}]]);
    const measure = () => c.CncNativeThreadRecognition.measureFiniteTerminalDomain([], [flank],faces,edges,independentSupport,.001);
    assert.deepEqual(Array.from(measure().intervalMm),[0,4]);
    faces.set('top',{support:{type:'cone'}}); assert.equal(measure(),null);
    faces.set('top',{support:{type:'bspline'}}); assert.equal(measure(),null);
    edges.get('last').uses.pop(); assert.equal(measure(),null);
});
test('a native smooth through hole has no thread seed or thread owner', async () => {
    const c = h.runtime(), n = await h.envelope(c,'ring'), topology = await h.topology(c,n);
    const d = await c.CncNativePrismaticRecognition.recognize(topology);
    assert.equal(d.features.filter(f => /thread$/.test(f.kind)).length,0);
    assert.equal(d.features.filter(f => f.kind === 'hole').length,1);
});
test('proper arbitrary import transform preserves full ruled measurements and native face polarity', () => {
    const c = h.runtime(), f = independentFlank(), angle = .719, co = Math.cos(angle), si = Math.sin(angle);
    f.trims.surfaceToImportWorld3x4 = [co,0,si,17,si,0,-co,-23,0,1,0,41];
    const support = { ...independentSupport, origin: [17,-23,41], axis: [si,-co,0], xDirection: [co,si,0], yDirection: [0,0,1] };
    const base = c.CncNativeThreadEvidence.profile(independentFlank(), independentSupport, [5,6], .01);
    const moved = c.CncNativeThreadEvidence.profile(f, support, [5,6], .01);
    assert.ok(Math.abs(moved.axialPerRadialSlope[0] - base.axialPerRadialSlope[0]) < 1e-10);
    assert.ok(moved.orientedRadialNormalDot[1] < 0);
    f.orientation = 0;
    const reversed = c.CncNativeThreadEvidence.profile(f, { ...support, orientationSign: 1 }, [5,6], .01);
    assert.ok(reversed.orientedRadialNormalDot[0] > 0);
});
test('complete ruled cubic proof encloses the radial profile and inward normal without point samples', () => {
    const c = h.runtime(), p = c.CncNativeThreadEvidence.profile(independentFlank(), independentSupport, [5,6], .01);
    assert.ok(p.radialEnvelopeMm[0] >= 4.99 && p.radialEnvelopeMm[1] <= 6.01);
    assert.ok(p.orientedRadialNormalDot[1] < 0); assert.ok(p.boundedCells < 1024);
    assert.ok(p.axialPerRadialSlope[0] <= -1 / Math.sqrt(3) && p.axialPerRadialSlope[1] >= -1 / Math.sqrt(3));
});
test('fresh public STEP traverses native model worker, owned callback, receiver rederivation and cache', async () => {
    const manifest = JSON.parse(fs.readFileSync(path.join(h.fixtureRoot, 'thread-fixtures.json'))), row = manifest.sources[0];
    assert.equal(h.digest(fs.readFileSync(path.join(h.fixtureRoot, manifest.generator))), manifest.generatorSha256);
    const bytes = fs.readFileSync(path.join(h.fixtureRoot, row.file)); assert.equal(h.digest(bytes), row.sha256);
    const worker = modelRuntime();
    const preview = await worker.send({ action: 'parse', jobId: 'thread', extension: 'step', analysisProfile: 'cnc',
        buffer: Uint8Array.from(bytes).buffer, deferCncAnalysis: true, retainNativeAnalysis: true });
    assert.equal(preview.stage, 'native-preview');
    const parsed = await worker.send({ action: 'continue-native-analysis', jobId: 'thread' }); assert.equal(parsed.success, true);
    const graph = parsed.cncGeometry.manufacturingFeatureGraph, thread = graph.features.find(f => f.kind === 'external_thread');
    assert.ok(thread); assert.equal(graph.features.length, 3); assert.equal(thread.primaryFaceIds.length, 21);
    assert.equal(thread.dimensions.fullDepthMm, 4); assert.equal(thread.startCount, 1); assert.equal(thread.handedness, 'right');
    assert.equal(thread.nominalThread.designation, 'M14x1'); assert.ok(Math.abs(thread.measuredLeadMm - 1) < 1e-10);
    assert.equal(Object.keys(graph.faceOwners).length, 23); assert.equal(graph.automaticPlanningEligible, false);
    const receiver = modelRuntime(); receiver.c.importScripts('/src/app/js/cnc-quotation/cnc-quotation.worker.js');
    const request = deliveryPage().deliver(parsed), estimate = await receiver.c.CncQuotationWorker.estimate(structuredClone(request));
    assert.equal(estimate.quote, null); assert.ok(estimate.reviewReasons.includes('native_machining_role_unverified'));
    const received = estimate.featureGraph.features.find(f => f.kind === 'external_thread');
    assert.deepEqual(JSON.parse(JSON.stringify(received.faceSubregions)), JSON.parse(JSON.stringify(thread.faceSubregions)));
    assert.equal(received.dimensions.fullDepthMm, 4); assert.equal(receiver.c.CncQuotationWorker.counters.nativeGeometryValidations, 1);
    await receiver.c.CncQuotationWorker.estimate(structuredClone(request));
    assert.equal(receiver.c.CncQuotationWorker.counters.nativeGeometryValidations, 1);
    const altered = structuredClone(request); altered.featureGraph.features.find(f => f.kind === 'external_thread').dimensions.fullDepthMm = 99;
    altered.geometry.manufacturingFeatureGraph = structuredClone(altered.featureGraph);
    const rejected = await receiver.c.CncQuotationWorker.estimate(altered); assert.equal(rejected.featureGraph, undefined);
    assert.equal(worker.state.nativeImports, 1);
    // Exercise the real trusted entry point with the production admission
    // predicate instrumented at its arithmetic boundary, not a forged source.
    const c = h.runtime(), n = parsed.cncGeometry.cadTopology.cadDocument.nativeImport;
    const topology = await h.topology(c,n), math = c.CncNativeThreadEvidence;
    for (const predicate of ['sharedBoundary','compatibleBoundaryLead']) {
        let calls=0;
        c.CncNativeThreadEvidence = { ...math, [predicate](...args) { calls++; return math[predicate](...args); } };
        const positive = await c.CncNativePrismaticRecognition.recognize(topology);
        assert.equal(positive.projection.sourceVerified,true); assert.ok(calls>0);
        assert.equal(positive.features.filter(f=>f.kind==='external_thread').length,1);
        c.CncNativeThreadEvidence = { ...math, [predicate]() { throw new Error('native_thread_shared_boundary_test_rejection'); } };
        const blocked = await c.CncNativePrismaticRecognition.recognize(topology);
        assert.equal(blocked.projection.sourceVerified,true);
        assert.equal(blocked.features.some(f=>f.kind==='external_thread'),false,'rejected admission must precede connectivity and ownership');
        assert.equal(new Set(blocked.features.flatMap(f=>Array.from(f.primaryFaceIds))).size,23);
        const flanks = n.meshes.flatMap(m=>m.brep_faces).filter(f=>f.trims.surfaceBasis.type==='bspline');
        assert.equal(flanks.length,10,'the independent public fixture has ten finite spline flank patches');
        for (const f of flanks) {
            const owner=blocked.features.find(o=>o.primaryFaceIds.includes(f.faceId));
            assert.equal(owner.kind,'unresolved');assert.equal(owner.required,true);
            assert.equal(owner.reason,'native_thread_shared_boundary_test_rejection');
            assert.deepEqual(Array.from(owner.primaryFaceIds),[f.faceId]);
            const regions=blocked.projection.regions.filter(r=>r.faceId===f.faceId);
            assert.equal(regions.length,1);
            assert.equal(owner.regionId,regions[0].regionId);
            assert.equal(owner.regionEvidence.regionId,regions[0].regionId);
            assert.ok(blocked.projection.provenance.nativeImportRevision);
            assert.equal(owner.regionEvidence.nativeImportRevision,blocked.projection.provenance.nativeImportRevision);
        }
    }
});
test('ruled proof rejects twisted coefficients and a near-axis patch instead of hiding them in radial bounds', () => {
    const c = h.runtime(), twisted = independentFlank(); twisted.trims.surfaceBasis.poles[1][1][0] += .1;
    assert.throws(() => c.CncNativeThreadEvidence.profile(twisted, independentSupport, [5,6], .01), /flank_profile_unsupported/);
    const axial = independentFlank(); axial.trims.surfaceBasis.poles.forEach(row => row.forEach(p => { p[0] = 0; p[1] = 0; }));
    assert.throws(() => c.CncNativeThreadEvidence.profile(axial, independentSupport, [5,6], .01), /native_thread_/);
});
test('independent LH owner rederives through the receiver; unavailable connector keeps every native face unresolved', async () => {
    const manifest=JSON.parse(fs.readFileSync(path.join(h.fixtureRoot,'thread-fixtures.json')));
    for (const row of manifest.sources) assert.equal(h.digest(fs.readFileSync(path.join(h.fixtureRoot,row.file))),row.sha256);
    for (const name of ['thread-external-lh','thread-nonhelical-connector']) {
        const bytes=fs.readFileSync(path.join(h.fixtureRoot,name+'.step')), worker=modelRuntime();
        const parsed=await worker.send({action:'parse',jobId:name,extension:'step',analysisProfile:'cnc',buffer:Uint8Array.from(bytes).buffer,retainNativeAnalysis:true});
        assert.equal(parsed.success,true);
        const g=parsed.cncGeometry.manufacturingFeatureGraph,n=parsed.cncGeometry.cadTopology.cadDocument.nativeImport;
        const owners=g.features.flatMap(f=>Array.from(f.primaryFaceIds));
        assert.equal(new Set(owners).size,owners.length);assert.equal(g.automaticPlanningEligible,false);
        if(name==='thread-nonhelical-connector') {
            assert.equal(n.kernelProvenance.nativeInterpretation.status,'review-required');
            assert.ok(n.kernelProvenance.nativeInterpretation.reasons.includes('metric-obligations-incomplete'));
            assert.equal(owners.length,31);assert.ok(g.features.every(f=>f.kind==='unresolved' && f.required));
            assert.ok(g.features.every(f=>f.reason==='native_region_source_unverified'));
            continue; // Deliberately not evidence of trusted negative flank admission.
        }
        assert.equal(n.kernelProvenance.nativeInterpretation.status,'native-interpreted');
        const thread=g.features.find(f=>f.kind==='external_thread');assert.ok(thread);
        assert.equal(owners.length,23);assert.equal(thread.primaryFaceIds.length,21);
        assert.equal(thread.handedness,'left');assert.equal(thread.startCount,1);
        assert.ok(Math.abs(thread.measuredLeadMm-1)<1e-10);assert.ok(Math.abs(thread.measuredPitchMm-1)<1e-10);
        assert.equal(thread.dimensions.fullDepthMm,4);
        for(const [id,range] of Object.entries(thread.faceAxialCoverageMm)) {
            const regions=Array.from(thread.faceSubregions[id]).sort((a,b)=>a.axialIntervalMm[0]-b.axialIntervalMm[0]);
            assert.equal(regions[0].axialIntervalMm[0],range[0]);assert.equal(regions.at(-1).axialIntervalMm[1],range[1]);
            for(let i=1;i<regions.length;i++)assert.equal(regions[i-1].axialIntervalMm[1],regions[i].axialIntervalMm[0]);
        }
        const receiver=modelRuntime();receiver.c.importScripts('/src/app/js/cnc-quotation/cnc-quotation.worker.js');
        const estimate=await receiver.c.CncQuotationWorker.estimate(structuredClone(deliveryPage().deliver(parsed)));
        const rederived=estimate.featureGraph.features.find(f=>f.kind==='external_thread');
        assert.equal(estimate.quote,null);assert.equal(rederived.handedness,'left');
        assert.deepEqual(JSON.parse(JSON.stringify(rederived.threadEvidence)),JSON.parse(JSON.stringify(thread.threadEvidence)));
        assert.deepEqual(JSON.parse(JSON.stringify(rederived.faceSubregions)),JSON.parse(JSON.stringify(thread.faceSubregions)));
    }
});
for (const original of originals) test('saved native ' + original.file + ' owns physically distinct complete thread regions', {
    skip: !fs.existsSync(path.join(savedRoot, original.file + '.json')) ? 'Private saved evidence is not distributed; mandatory public thread coverage runs separately.' : false
}, async () => {
    const bytes = fs.readFileSync(path.join(savedRoot, original.file + '.json'));
    assert.equal(h.digest(bytes), original.hash);
    assert.equal(h.digest(fs.readFileSync(path.join('Z:/', original.file))), original.source);
    const c = h.runtime(), raw = JSON.parse(bytes);
    const n = { contract: 'CncNativeImport.v1', nativeSemanticBasis: c.CncNativeInterpretation.semanticBasis,
        analysisProfile: 'cnc', sourceFormat: 'step', tessellation: { ...c.CncCadDocument.importParameters }, buildIdentity: h.historicalIdentity,
        sourceBytesHash: original.source, kernelProvenance: raw.kernelProvenance, meshes: raw.meshes };
    const topology = await h.topology(c, n, h.historicalExpectation(n)), diagnostic = await c.CncNativePrismaticRecognition.recognize(topology);
    assert.equal(diagnostic.projection.sourceVerified, true);
    const threads = diagnostic.features.filter(f => ['internal_thread', 'external_thread'].includes(f.kind));
    assert.equal(threads.length, original.count, 'native thread ownership must not remain one unresolved owner per helical face');
    const owned = diagnostic.features.flatMap(f => Array.from(f.primaryFaceIds));
    assert.equal(owned.length, original.faces); assert.equal(new Set(owned).size, original.faces);
    assert.equal(diagnostic.automaticPlanningEligible, false);
    for (const thread of threads) {
        assert.equal(thread.startCount, 1);
        assert.equal(thread.nominalThread.designation, original.count === 8 || original.partialEnd ? 'M6x1' : thread.kind === 'external_thread' ? 'M14x1' : 'M24x1.5');
        assert.equal(thread.nominalThread.fitClass, null);
        assert.equal(thread.dimensions.runoutLengthMm,null);
        assert.equal(thread.runoutExtentStatus,'not-derived-no-additional-patch-claim');
        assert.equal(thread.recognitionIssues.includes('native_thread_runout_extent_unresolved'),false);
        for (const [id, range] of Object.entries(thread.faceAxialCoverageMm)) {
            const regions = Array.from(thread.faceSubregions[id]).sort((a,b) => a.axialIntervalMm[0] - b.axialIntervalMm[0]);
            assert.equal(regions[0].axialIntervalMm[0],range[0]);
            assert.equal(regions.at(-1).axialIntervalMm[1],range[1]);
            for (let i=1;i<regions.length;i++) assert.equal(regions[i-1].axialIntervalMm[1],regions[i].axialIntervalMm[0]);
            assert.ok(regions.every(r => r.required && r.trimPredicate.nativeTrim && r.nativeFaceId === id));
        }
        assert.equal(thread.handedness, original.count !== 8 && thread.kind === 'external_thread' ? 'left' : 'right');
        assert.ok(Object.values(thread.faceRoles).includes('flank'));
        const nativeFaces = new Map(n.meshes.flatMap(m => m.brep_faces.map(f => [f.faceId,f])));
        assert.ok(thread.primaryFaceIds.every(id => !['cone','sphere','plane'].includes(nativeFaces.get(id).support.type)), 'nearby chamfers, drill tips and planar work must not be swallowed as thread/runout');
        if (original.partialEnd) {
            assert.equal(thread.dimensions.fullDepthMm, 8.9175);
            assert.ok(thread.recognitionIssues.includes('thread_entry_end_unresolved'), 'finite thread cuts do not prove an accessible mouth through neighboring lead-in geometry');
            assert.equal(thread.entryEnds.some(e => e.kind === 'mouth'),false);
            continue;
        }
        const expectedDepth = original.count === 8 ? 16 : thread.kind === 'external_thread' ? 15 : 21;
        assert.ok(Math.abs(thread.dimensions.fullDepthMm - expectedDepth) < 1e-8,
            'full thread depth must exclude the smooth native-cylinder tail');
        if (original.count === 8 || thread.kind === 'internal_thread') {
            assert.equal(thread.entryEnds.filter(e => e.kind === 'mouth').length,1);
            assert.ok(thread.boreOrProfileExtentMm > thread.dimensions.fullDepthMm);
            assert.ok(Object.values(thread.faceSubregions).some(regions => regions.some(r => r.role === 'smooth-cylinder-extension' && r.required)));
            assert.ok(thread.recognitionIssues.includes('native_thread_smooth_extension_requires_machining_role'));
        }
    }
    if (original.count === 8) assert.equal(new Set(threads.flatMap(t => t.entryEnds.filter(e => e.kind === 'mouth').map(e => e.wireId))).size,8);
});
