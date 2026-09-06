const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { webcrypto } = require('node:crypto');
const webRoot = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot');
const source = `ISO-10303-21;HEADER;ENDSEC;DATA;
#1=CARTESIAN_POINT('',(0,0,0));#2=AXIS2_PLACEMENT_3D('',#1,$,$);
#3=CYLINDRICAL_SURFACE('',#2,2.);
#4=(LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.));
#5=GLOBAL_UNIT_ASSIGNED_CONTEXT((#4));ENDSEC;END-ISO-10303-21;`;

function runtime() {
    const imported = [], analyzed = [];
    const c = vm.createContext({ console, TextDecoder, TextEncoder, crypto: webcrypto });
    c.self = c;
    c.location = { search: '?v=cad-boundary-version' };
    c.importScripts = (...urls) => urls.forEach(url => {
        imported.push(url);
        vm.runInContext(fs.readFileSync(path.join(webRoot, url.split('?')[0]), 'utf8'), c, { filename: url });
    });
    vm.runInContext(fs.readFileSync(path.join(webRoot, 'src/app/js/model-viewer/model-viewer.worker.js'), 'utf8'), c);
    const mesh = () => ({
        attributes: { position: { array: [-1,-1,0, 1,-1,0, 1,1,0, -1,1,0] } },
        index: { array: [0,1,2, 0,2,3] }, brep_faces: [{ first: 0, last: 1 }]
    });
    // The OCCT boundary is deliberately fixed; this suite tests metadata transfer,
    // not CAD tessellation. Real THREE decoding/bridges and STEP hint parser run.
    c.occtimportjs = async () => ({
        ReadStepFile: () => ({ success: true, meshes: [mesh(), { ...mesh(), brep_faces: [{ first: 0, last: 0 }, { first: 1, last: 1 }] }] }),
        ReadIgesFile: () => ({ success: true, meshes: [mesh()] })
    });
    c.AnalyzeCncGeometry = (triangles, modelInfo) => {
        analyzed.push({ triangles: Array.from(triangles), modelInfo });
        return { boundaryChecked: true };
    };
    return {
        c, imported, analyzed,
        dispatch: data => new Promise(resolve => {
            c.postMessage = message => resolve(message);
            c.onmessage({ data: { jobId: 1, ...data } });
        })
    };
}
function job(options = {}) {
    return { action: 'parse', extension: 'step', buffer: new TextEncoder().encode(source).buffer,
        analysisProfile: 'cnc', ...options };
}
function plain(value) { return JSON.parse(JSON.stringify(value)); }

for (const [evidence, highlightedCount] of [[[], 0], [[0], 9], [undefined, 9], [null, 9]]) {
    test('worker overlay distinguishes missing visibility from ' + JSON.stringify(evidence), () => {
        const { c } = runtime();
        const result = c.ClassifyCncOverlay({
            meshes: [{ position: [0,0,0, 0,1,0, 0,0,1], matrix: new c.THREE.Matrix4().toArray() }],
            clusters: [{ id: 'wall', triangleIndexes: [0], accessibleTriangleIndexesByDirection: { top: evidence } }],
            selectedSetups: [{ id: 'top', direction: { x: 0, y: 0, z: 1 } }],
            reachableIds: ['wall'], fluteReachKeys: ['top\u0000wall']
        });
        assert.equal(result.highlighted.length, highlightedCount);
        assert.equal(result.dimmed.length, 9 - highlightedCount);
    });
}

test('CNC imports carry the parent cache version and load geometry before ball contact', () => {
    const { imported } = runtime();
    const parser = '/src/app/js/cnc-quotation/cnc-cad-surfaces.worker.js?v=cad-boundary-version';
    const topology = '/src/app/js/cnc-quotation/cnc-topology.worker.js?v=cad-boundary-version';
    const ball = '/src/app/js/cnc-quotation/cnc-ball-rest.worker.js?v=cad-boundary-version';
    assert.ok(imported.includes(parser));
    assert.ok(imported.includes(topology));
    assert.ok(imported.includes(ball));
    assert.ok(imported.indexOf('/src/app/js/cnc-quotation/cnc-geometry.worker.js?v=cad-boundary-version') < imported.indexOf(topology));
    assert.ok(imported.indexOf(topology) < imported.indexOf(ball));
});

test('immediate CNC analysis receives STEP hints and globally offset exact CAD face ranges', async () => {
    const f = runtime();
    const result = await f.dispatch(job());
    assert.equal(result.success, true);
    assert.equal(f.analyzed.length, 1);
    const info = f.analyzed[0].modelInfo;
    assert.equal(info.analyticSurfaces?.length, 1);
    assert.equal(info.analyticSurfaces[0].radiusMm, 2);
    assert.deepEqual(plain(info.cadFaceRanges), [{ first: 0, last: 1 }, { first: 2, last: 2 }, { first: 3, last: 3 }]);
    assert.equal(result.modelInfo.analyticSurfaces, undefined, 'large candidate arrays stay off the UI result');
    assert.ok(result.meshes.every(mesh => mesh.analyticSurfaces === undefined));
});

test('deferred CNC snapshot round-trip preserves one hint array and each mesh face range', async () => {
    const f = runtime();
    const preview = await f.dispatch(job({ deferCncAnalysis: true }));
    assert.equal(preview.success, true);
    assert.equal(f.analyzed.length, 0);
    assert.equal(preview.analysisMeshes.filter(mesh => Array.isArray(mesh.analyticSurfaces)).length, 1);
    assert.equal(preview.analysisMeshes.filter(mesh => Array.isArray(mesh.canonicalValidationMeshes)).length, 1,
        'the fixed validation tessellation must cross the deferred worker boundary exactly once');
    assert.deepEqual(plain(preview.analysisMeshes[1].cadFaceRanges), [{ first: 0, last: 0 }, { first: 1, last: 1 }]);
    const snapshot = structuredClone(preview.analysisMeshes);
    const result = await f.dispatch({ action: 'analyze', meshes: snapshot, analysisProfile: 'cnc' });
    assert.equal(result.success, true);
    assert.equal(f.analyzed[0].modelInfo.analyticSurfaces[0].radiusMm, 2);
    assert.equal(f.analyzed[0].modelInfo.validationMeshes.length, 2,
        'deferred analysis must receive the fixed validation tessellation, not reuse display triangles');
    assert.deepEqual(plain(f.analyzed[0].modelInfo.cadFaceRanges), [{ first: 0, last: 1 }, { first: 2, last: 2 }, { first: 3, last: 3 }]);
    assert.equal(f.analyzed[0].triangles.length, 36);
});

test('deferred IGES snapshot preserves B-Rep source identity without STEP hints', async () => {
    const f = runtime();
    const preview = await f.dispatch(job({ extension: 'iges', deferCncAnalysis: true }));
    assert.equal(preview.success, true);
    const result = await f.dispatch({ action: 'analyze', meshes: structuredClone(preview.analysisMeshes), analysisProfile: 'cnc' });
    assert.equal(result.success, true);
    assert.equal(result.cncGeometry.cadTopology.sourceKind, 'brep');
    assert.equal(result.cncGeometry.cadTopology.automaticPlanningEligible, false);
    assert.deepEqual(Array.from(result.cncGeometry.cadTopology.unresolvedReasons), ['iges_semantic_face_support_unavailable']);
});

test('additive STEP and CNC IGES do not parse or forward STEP analytic hints', async () => {
    for (const options of [{ analysisProfile: 'additive' }, { extension: 'iges' }]) {
        const f = runtime();
        let parsed = 0;
        f.c.CncCadSurfaces = { parseStep() { parsed++; throw new Error('STEP hints must not be requested'); } };
        const result = await f.dispatch(job(options));
        assert.equal(result.success, true);
        assert.equal(parsed, 0);
        assert.equal(result.analysisMeshes ?? null, null);
        if (options.analysisProfile === 'additive') {
            assert.equal(f.analyzed.length, 0);
            assert.equal(result.cncGeometry, null);
        } else {
            assert.deepEqual(plain(f.analyzed[0].modelInfo.analyticSurfaces ?? null), []);
            assert.equal(result.cncGeometry.cadTopology.sourceKind, 'brep', 'IGES remains a B-Rep source without STEP hints');
        }
    }
});

test('invalid or out-of-mesh CAD ranges cannot cross into the next mesh', async () => {
    const f = runtime();
    const object = await f.c.RunParseJob(job());
    object.children[0].geometry.userData.cadFaceRanges = [{ first: 0, last: 20 }, { first: -1, last: 0 }, { first: .5, last: 1 }];
    await f.c.AnalyzeCncObject(object, { marker: 'test' });
    assert.ok(Array.isArray(f.analyzed[0].modelInfo.cadFaceRanges));
    assert.deepEqual(plain(f.analyzed[0].modelInfo.cadFaceRanges), [{ first: 2, last: 2 }, { first: 3, last: 3 }]);
});

test('deferred world transforms preserve triangle provenance without transforming local STEP hints', async () => {
    const f = runtime();
    const object = await f.c.RunParseJob(job());
    object.children[1].position.x = 10;
    const buffers = f.c.ExtractAnalysisMeshBuffers(object);
    const rebuilt = f.c.BuildObject3DFromMeshBuffers(structuredClone(buffers));
    await f.c.AnalyzeCncObject(rebuilt, {});
    assert.equal(f.analyzed[0].triangles[18], 9);
    assert.equal(f.analyzed[0].modelInfo.analyticSurfaces?.length, 1);
    assert.equal(f.analyzed[0].modelInfo.analyticSurfaces[0].centerMm.x, 0,
        'local hints remain candidates; unsupported instance transforms are not guessed');
    assert.deepEqual(plain(f.analyzed[0].modelInfo.cadFaceRanges), [{ first: 0, last: 1 }, { first: 2, last: 2 }, { first: 3, last: 3 }]);
});

test('overlapping face ranges and incomplete triangles fail closed for analytic provenance', async () => {
    const f = runtime();
    const object = await f.c.RunParseJob(job());
    object.children[0].geometry.userData.cadFaceRanges = [{ first: 0, last: 1 }, { first: 1, last: 1 }];
    await f.c.AnalyzeCncObject(object, {});
    assert.deepEqual(plain(f.analyzed[0].modelInfo.cadFaceRanges), [{ first: 2, last: 2 }, { first: 3, last: 3 }]);
    object.children[0].geometry.setIndex([0,1,2,3]);
    await f.c.AnalyzeCncObject(object, {});
    assert.deepEqual(plain(f.analyzed[1].modelInfo.cadFaceRanges), []);
});

test('unsupported STEP units disable hints without breaking the regular CAD decode', async () => {
    const f = runtime();
    const result = await f.dispatch(job({ buffer: new TextEncoder().encode(source.replace('.MILLI.', '$')).buffer }));
    assert.equal(result.success, true);
    assert.equal(result.meshes.length, 2);
    assert.deepEqual(plain(f.analyzed[0].modelInfo.analyticSurfaces), []);
});
