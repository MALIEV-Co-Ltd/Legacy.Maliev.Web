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
