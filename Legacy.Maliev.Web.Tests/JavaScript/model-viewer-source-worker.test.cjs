const test = require('node:test');
const assert = require('node:assert/strict');
const { runtime } = require('./cnc-model-worker-harness.cjs');

test('GLB analysis retains a sheared world matrix without decomposing it', async () => {
    const worker = runtime();
    const response = await worker.send({
        action: 'analyze',
        jobId: 'sheared-glb',
        meshes: [{
            position: new Float32Array([0, 0, 0, 0, 1, 0, 1, 1, 0]),
            // Column-major x' = x + y; decomposing to rotation and scale loses the shear.
            matrix: new Float32Array([1, 0, 0, 0, 1, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]),
        }],
    });

    assert.equal(response.success, true);
    assert.equal(response.jobId, 'sheared-glb');
    assert.equal(response.modelInfo.min.x, 0);
    assert.equal(response.modelInfo.max.x, 2);
    assert.equal(response.modelInfo.size.x, 2);
    assert.equal(response.modelInfo.size.y, 1);
});

test('a synchronous malformed STL parser error returns a terminal worker reply', async () => {
    const worker = runtime();
    const response = await worker.send({
        action: 'parse',
        jobId: 'malformed-stl',
        extension: 'stl',
        buffer: new ArrayBuffer(2),
    });

    assert.equal(response.jobId, 'malformed-stl');
    assert.equal(response.success, false);
    assert.equal(typeof response.error, 'string');
    assert.notEqual(response.error.length, 0);
});

test('STEP parsing uses finer additive tessellation without changing default or IGES imports', async () => {
    const calls = [];
    const result = () => ({
        success: true,
        meshes: [{
            name: 'closed-tetrahedron',
            attributes: { position: { array: [0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1] } },
            index: { array: [0, 2, 1, 0, 1, 3, 1, 2, 3, 2, 0, 3] }
        }]
    });
    const record = (format, bytes, parameters) => {
        calls.push({ format, bytes: Array.from(bytes), parameters: parameters === null ? null : { ...parameters } });
        return result();
    };
    const worker = runtime({ additiveApi: {
        ReadStepFile: (bytes, parameters) => record('step', bytes, parameters),
        ReadIgesFile: (bytes, parameters) => record('iges', bytes, parameters)
    } });
    const cases = [
        { extension: 'step', analysisProfile: 'additive', format: 'step', parameters: { linearDeflection: 0.02 } },
        { extension: 'stp', analysisProfile: 'additive', format: 'step', parameters: { linearDeflection: 0.02 } },
        { extension: 'step', format: 'step', parameters: null },
        { extension: 'stp', format: 'step', parameters: null },
        { extension: 'igs', analysisProfile: 'additive', format: 'iges', parameters: null },
        { extension: 'iges', analysisProfile: 'additive', format: 'iges', parameters: null }
    ];
    for (const [index, expected] of cases.entries()) {
        const response = await worker.send({
            action: 'parse', jobId: 'step-parameters-' + index,
            extension: expected.extension, analysisProfile: expected.analysisProfile,
            buffer: Uint8Array.from([11, 22, 33]).buffer
        });
        assert.equal(response.success, true);
        assert.equal(response.jobId, 'step-parameters-' + index);
        assert.deepEqual(calls[index], {
            format: expected.format, bytes: [11, 22, 33], parameters: expected.parameters
        });
    }
    assert.equal(calls.length, cases.length);
});
