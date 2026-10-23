const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const sourcePath = path.resolve(__dirname,
    '../../Legacy.Maliev.Web/wwwroot/src/app/js/model-viewer/model-viewer.js');
const source = fs.readFileSync(sourcePath, 'utf8');
const flush = () => new Promise(resolve => setImmediate(resolve));

function runtime() {
    const timers = [];
    const context = vm.createContext({ console, Float32Array, Uint32Array, ArrayBuffer,
        setTimeout(callback) { timers.push(callback); return timers.length; },
        clearTimeout() {} });
    context.window = context;
    vm.runInContext(source, context, { filename: sourcePath });
    // Match the existing watchdog harness: exercise the actual ParseFile closure without WebGL.
    const begin = source.indexOf('    this.ParseFile = function (file, onReady, options) {');
    const end = source.indexOf('    // Displays the given (already-parsed) object3D', begin);
    assert.ok(begin >= 0 && end > begin);
    const parser = vm.runInContext('(function () {' + source.slice(begin, end)
        + 'return this; }).call({})', context);
    context.ReadArrayBuffer = (file, ready) => ready(new ArrayBuffer(4));
    context.AddCadPreviewEdges = () => {};
    return { context, parser, timers };
}

for (const cancelled of [false, true]) {
    test('worker parse yields before geometry construction; cancelled=' + cancelled, async () => {
        const { context, parser, timers } = runtime();
        let resolveParse, builds = 0;
        const cancellations = [], ready = [];
        context.ModelParseWorkerManager = {
            Submit: () => ({ jobId: 'parse-1', promise: new Promise(resolve => { resolveParse = resolve; }) }),
            Cancel: id => cancellations.push(id)
        };
        context.BuildGroupFromMeshBuffersAsync = async () => {
            builds++;
            return { userData: {}, traverse() {} };
        };
        const handle = parser.ParseFile({ name: 'part.step' }, (...args) => ready.push(args));
        resolveParse({ meshes: [], modelInfo: { bodyCount: 1 } });
        await flush();
        assert.equal(builds, 0, 'parse completion must give pending input a task boundary');
        assert.equal(ready.length, 0);
        assert.equal(timers.length, 1);
        if (cancelled) handle.cancel();
        timers.shift()();
        await flush();
        assert.equal(builds, cancelled ? 0 : 1);
        assert.equal(ready.length, cancelled ? 0 : 1);
        assert.deepEqual(cancellations, cancelled ? ['parse-1'] : []);
    });
}

test('3MF decodes on the window and transfers only analysis copies, preserving the preview', async () => {
    const { context, parser } = runtime();
    const position = new Float32Array([0, 0, 0, 1, 0, 0, 0, 1, 0]);
    const index = new Uint32Array([0, 1, 2]);
    const matrix = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 3, 4, 5, 1];
    let decoded = 0, updated = 0, normals = 0, resolveAnalysis, request, transferred;
    const geometry = { attributes: { position: { array: position } }, index: { array: index },
        computeVertexNormals() { normals++; this.attributes.normal = { count: 3 }; } };
    const object = { userData: {}, updateWorldMatrix() { updated++; },
        traverse(visit) { visit({ isMesh: true, geometry, matrixWorld: { elements: matrix } }); } };
    context.THREE = { ThreeMFLoader: class { parse(buffer) {
        assert.ok(buffer instanceof ArrayBuffer); decoded++; return object;
    } } };
    context.ModelParseWorkerManager = { Submit() { assert.fail('3MF XML must not enter the parse worker'); } };
    context.ModelAnalysisWorkerManager = {
        Submit(message, transfer) {
            request = message;
            assert.equal(message.action, 'analyze');
            assert.equal(message.analysisProfile, 'additive');
            assert.equal(updated, 1, 'world matrices must be current before copying');
            assert.notEqual(message.meshes[0].position.buffer, position.buffer);
            assert.notEqual(message.meshes[0].index.buffer, index.buffer);
            assert.deepEqual(Array.from(message.meshes[0].matrix), matrix);
            assert.equal(transfer.length, 3);
            transferred = structuredClone(message, { transfer });
            return { jobId: 'analysis-1', promise: new Promise(resolve => { resolveAnalysis = resolve; }) };
        },
        Cancel() { assert.fail('completed analysis should not be cancelled'); }
    };
    const ready = [];
    parser.ParseFile({ name: 'component.3mf' }, (...args) => ready.push(args));
    assert.equal(decoded, 1);
    assert.equal(request.meshes[0].position.byteLength, 0, 'the analysis copy actually transferred');
    assert.deepEqual(Array.from(transferred.meshes[0].position), Array.from(position));
    assert.deepEqual(Array.from(transferred.meshes[0].index), Array.from(index));
    assert.equal(position.byteLength, 36, 'live preview positions remain attached');
    assert.equal(index.byteLength, 12, 'live preview indices remain attached');
    assert.equal(ready.length, 0, 'analysis still owns completion');
    resolveAnalysis({ modelInfo: { bodyCount: 1 } });
    await flush();
    assert.equal(ready.length, 1);
    assert.equal(ready[0][1], object);
    assert.equal(ready[0][0].sourceFormat, '3mf');
    assert.equal(normals, 1, 'normal preparation precedes publication');
});

test('preview normal preparation preserves authored normals and shared indexed geometry', () => {
    const { context } = runtime();
    let computed = 0, disposed = 0;
    const authored = { attributes: { position: {}, normal: { authored: true } },
        computeVertexNormals() { assert.fail('authored normals must survive'); } };
    const expanded = [];
    const shared = { attributes: { position: {} }, index: {},
        dispose() { disposed++; },
        toNonIndexed() {
            const geometry = { attributes: { position: {} }, computeVertexNormals() {
                computed++; this.attributes.normal = {};
            } };
            expanded.push(geometry);
            return geometry;
        } };
    const children = [{ isMesh: true, geometry: authored }, { isMesh: true, geometry: shared },
        { isMesh: true, geometry: shared }, { isMesh: false, geometry: shared }];
    const object = { traverse(visit) { children.forEach(visit); } };
    context.EnsurePreviewGeometryNormals(object);
    assert.equal(children[0].geometry, authored);
    assert.equal(computed, 2);
    assert.equal(disposed, 0, 'a shared 3MF component geometry must remain usable');
    assert.equal(children[3].geometry, shared);
    assert.notEqual(children[1].geometry, children[2].geometry);
    assert.equal(expanded.length, 2);
    context.EnsurePreviewGeometryNormals(object);
    assert.equal(computed, 2, 'prepared normals are stable across repeated publication');
});
