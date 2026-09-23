import assert from 'node:assert/strict';
import test from 'node:test';
import {
  BufferGeometry,
  Float32BufferAttribute,
  Group,
  InterleavedBuffer,
  InterleavedBufferAttribute,
  Mesh,
  MeshStandardMaterial,
} from 'three';
import {
  attachWallThicknessEvidence,
  createModelViewer,
  renderWallThicknessOverlay,
} from '../wwwroot/src/app/js/instant-quotation/model-viewer.mjs';
import {
  assessThickness,
  createWallThicknessAnalyzer,
  policyForMaterial,
  thicknessMeshes,
} from '../wwwroot/src/app/js/instant-quotation/wall-thickness.mjs';
import {
  createWorkflowPreviewInterop,
} from '../wwwroot/src/app/js/instant-quotation/workflow-interop.mjs';

function part() {
  const geometry = new BufferGeometry();
  geometry.setAttribute('position', new Float32BufferAttribute([
    0, 0, 0, 1, 0, 0, 0, 1, 0,
  ], 3));
  const root = new Group();
  const mesh = new Mesh(geometry, new MeshStandardMaterial({ color: '#64748b' }));
  root.add(mesh);
  return { root, mesh };
}

function evidence(reading = 0.45) {
  return {
    summary: { state: 'complete', measuredAreaRatio: 1, minMm: reading },
    fields: [Float32Array.from([reading, reading, reading])],
    oppositeSurfacePatches: Float32Array.from([
      0, 0, 0, 1, 0, 0, 0, 1, 0, reading, 0.6, 0.9, 1.2,
    ]),
    sampleThicknessMm: Float32Array.from([reading]),
    sampleAreaMm2: Float32Array.from([1]),
  };
}

test('wall evidence copies geometry for worker transfer and preserves authored mesh buffers', () => {
  const { root, mesh } = part();
  root.position.set(2, 3, 4);
  const [copy] = thicknessMeshes(root);
  assert.notEqual(copy.position.buffer, mesh.geometry.getAttribute('position').array.buffer);
  assert.equal(copy.matrix[12], 2);
  assert.equal(copy.matrix[13], 3);
  assert.equal(copy.matrix[14], 4);
  copy.position[0] = 50;
  assert.equal(mesh.geometry.getAttribute('position').getX(0), 0);
});

test('wall evidence reads interleaved positions without copying stride padding', () => {
  const { root, mesh } = part();
  const interleaved = new InterleavedBuffer(Float32Array.from([
    99, 0, 0, 0, 99, 1, 0, 0, 99, 0, 1, 0,
  ]), 4);
  mesh.geometry.setAttribute('position', new InterleavedBufferAttribute(interleaved, 3, 1));
  mesh.geometry.setIndex([0, 1, 2]);
  const [copy] = thicknessMeshes(root);
  assert.deepEqual(Array.from(copy.position), [0, 0, 0, 1, 0, 0, 0, 1, 0]);
  assert.deepEqual(Array.from(copy.index), [0, 1, 2]);
  assert.equal(interleaved.array[0], 99);
});

test('one worker runs at a time and abort releases queued and active jobs', async () => {
  const workers = [];
  const analyzer = createWallThicknessAnalyzer({
    workerFactory: () => {
      const worker = {
        postMessage(message, transfer) { this.message = message; this.transfer = transfer; },
        terminate() { this.terminated = true; },
      };
      workers.push(worker);
      return worker;
    },
  });
  const first = part().root;
  const second = part().root;
  const abort = new AbortController();
  const firstResult = analyzer.analyze(first);
  const cancelled = analyzer.analyze(second, abort.signal);
  assert.equal(workers.length, 1);
  assert.equal(workers[0].transfer.length, 2);
  abort.abort();
  assert.equal(await cancelled, null);
  workers[0].onmessage({ data: { evidence: evidence() } });
  assert.equal((await firstResult).summary.minMm, 0.45);
  assert.equal(workers[0].terminated, true);
  assert.equal(workers.length, 1);
  analyzer.dispose();
});

test('material-specific advisory warns without treating incomplete coverage as safe', () => {
  assert.equal(policyForMaterial('M68').thinMm, 0.6);
  assert.equal(policyForMaterial('PLA').thinMm, 0.8);
  assert.equal(assessThickness(evidence(0.7), 'PLA').hasThinRegion, true);
  assert.equal(assessThickness(evidence(0.7), 'M68').hasThinRegion, false);
  const partial = evidence();
  partial.summary.measuredAreaRatio = 0.5;
  assert.equal(assessThickness(partial, 'PLA').reliable, false);
});

test('overlay blends only measured thin faces and clips opposite patch to struck face', () => {
  const { root, mesh } = part();
  const sourceMaterial = mesh.material;
  const mismatched = evidence();
  mismatched.fields[0] = new Float32Array(2);
  assert.equal(attachWallThicknessEvidence(root, mismatched), false);
  assert.equal(mesh.material, sourceMaterial);
  assert.equal(attachWallThicknessEvidence(root, evidence()), true);
  assert.notEqual(mesh.material, sourceMaterial);
  assert.equal(renderWallThicknessOverlay(root, true, policyForMaterial('PLA')), true);
  const shader = {
    uniforms: {},
    vertexShader: '#include <common>\n#include <begin_vertex>',
    fragmentShader: '#include <common>\n#include <color_fragment>',
  };
  mesh.material.onBeforeCompile(shader, {});
  assert.match(shader.fragmentShader, /smoothstep/);
  assert.match(shader.fragmentShader, /mix\(diffuseColor\.rgb/);
  const patch = root.userData.wallThicknessPatchMesh;
  assert.equal(patch.visible, true);
  assert.equal(patch.geometry.getAttribute('position').count, 3);
  assert.equal(patch.geometry.getAttribute('wallThicknessMm').count, 3);
  renderWallThicknessOverlay(root, false, policyForMaterial('PLA'));
  assert.equal(patch.visible, false);
  assert.equal(mesh.material.userData.wallThicknessOverlay.active, false);
});

test('meshes sharing geometry retain independent world-space wall evidence', () => {
  const { root, mesh } = part();
  const other = new Mesh(mesh.geometry, mesh.material);
  other.position.z = 1;
  root.add(other);
  const separate = evidence();
  separate.fields.push(Float32Array.from([0.95, 0.95, 0.95]));
  assert.equal(attachWallThicknessEvidence(root, separate), true);
  assert.notEqual(mesh.geometry, other.geometry);
  assert.equal(mesh.geometry.getAttribute('wallThicknessMm').getX(0), separate.fields[0][0]);
  assert.equal(other.geometry.getAttribute('wallThicknessMm').getX(0), separate.fields[1][0]);
});

test('failed overlay activation is not restored when changing selected parts', () => {
  const requests = [];
  const viewer = createModelViewer({ adapter: {
    setThickness(_object, visible) { requests.push(visible); return false; },
  } });
  viewer.addPart('a', part().root);
  viewer.addPart('b', part().root);
  assert.equal(viewer.setThicknessVisible(true, policyForMaterial('PLA')), false);
  viewer.select('b');
  viewer.select('a');
  assert.equal(requests.at(-1), false);
  viewer.dispose();
});

test('admitted preview publishes and toggles advisory independently of quote geometry', async () => {
  const { root } = part();
  const calls = [];
  const states = [];
  const viewer = {
    addPart(...args) { calls.push(['addPart', ...args]); },
    setThicknessEvidence(...args) { calls.push(['evidence', ...args]); },
    setThicknessVisible(...args) { calls.push(['visible', ...args]); return true; },
    select(...args) { calls.push(['select', ...args]); },
    remove() {},
    dispose() {},
  };
  const interop = createWorkflowPreviewInterop({
    loadModel: async () => root,
    analyzeGeometry: () => ({ version: 1, volumeMm3: 123 }),
    createViewer: () => viewer,
    thicknessAnalyzer: { analyze: async () => evidence(), dispose() {} },
    reportThickness: (...args) => states.push(args),
  });
  const [key] = interop.beginSelection({
    files: [{ name: 'part.stl', arrayBuffer: async () => new Uint8Array([1, 2]).buffer }],
  });
  const claim = await interop.getGeometryClaim(key);
  assert.equal(claim.volumeMm3, 123);
  interop.attach({});
  interop.admit(key, 'part-1', 'PLA');
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(states.at(-1)[1], true);
  assert.equal(states.at(-1)[2], true);
  assert.equal(interop.toggleThickness('part-1'), true);
  assert.equal(calls.some(call => call[0] === 'visible' && call[1] === true), true);
  interop.setThicknessMaterial('part-1', 'M68');
  assert.equal(states.at(-1)[2], true);
  assert.equal(states.every((state, index) => index === 0 || state[7] > states[index - 1][7]), true);
  interop.dispose();
});
