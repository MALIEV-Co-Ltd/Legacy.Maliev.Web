import assert from 'node:assert/strict';
import test, { after, before } from 'node:test';
import { readFile } from 'node:fs/promises';
import { Matrix4, Vector3 } from 'three';
import { thicknessMeshes } from '../wwwroot/src/app/js/instant-quotation/wall-thickness.mjs';
import { loadStandaloneModel } from '../wwwroot/src/app/js/instant-quotation/model-viewer.mjs';
import { analyzeUploadDerivedGeometry } from '../wwwroot/src/app/js/instant-quotation/geometry-analysis.mjs';
import { createWorkflowPreviewInterop } from '../wwwroot/src/app/js/instant-quotation/workflow-interop.mjs';

// Three's FileLoader emits browser progress events for real embedded data-URI buffers.
// Node 24 has Event but no ProgressEvent; keep this harness shim local to this test process.
const progressEventDescriptor = Object.getOwnPropertyDescriptor(globalThis, 'ProgressEvent');
const needsProgressEvent = typeof globalThis.ProgressEvent !== 'function';
before(() => {
  if (!needsProgressEvent) return;
  Object.defineProperty(globalThis, 'ProgressEvent', {
    configurable: true,
    writable: true,
    value: class ProgressEvent extends Event {
      constructor(type, init = {}) {
        super(type);
        this.lengthComputable = init.lengthComputable ?? false;
        this.loaded = init.loaded ?? 0;
        this.total = init.total ?? 0;
      }
    },
  });
});
after(() => {
  if (!needsProgressEvent) return;
  if (progressEventDescriptor) Object.defineProperty(globalThis, 'ProgressEvent', progressEventDescriptor);
  else delete globalThis.ProgressEvent;
});

const original = "{\"scene\":0,\"scenes\":[{\"nodes\":[0]}],\"asset\":{\"version\":\"2.0\",\"generator\":\"https://github.com/mikedh/trimesh\"},\"accessors\":[{\"componentType\":5125,\"type\":\"SCALAR\",\"bufferView\":0,\"count\":36,\"max\":[7],\"min\":[0]},{\"componentType\":5126,\"type\":\"VEC3\",\"byteOffset\":0,\"bufferView\":1,\"count\":8,\"max\":[5.0,5.0,5.0],\"min\":[-5.0,-5.0,-5.0]}],\"meshes\":[{\"name\":\"geometry_0\",\"extras\":{\"shape\":\"box\",\"extents\":[10.0,10.0,10.0]},\"primitives\":[{\"attributes\":{\"POSITION\":1},\"indices\":0,\"mode\":4}]}],\"nodes\":[{\"name\":\"world\",\"children\":[1]},{\"name\":\"geometry_0\",\"mesh\":0}],\"buffers\":[{\"uri\":\"data:application/octet-stream;base64,AQAAAAMAAAAAAAAABAAAAAEAAAAAAAAAAAAAAAMAAAACAAAAAgAAAAQAAAAAAAAAAQAAAAcAAAADAAAABQAAAAEAAAAEAAAABQAAAAcAAAABAAAAAwAAAAcAAAACAAAABgAAAAQAAAACAAAAAgAAAAcAAAAGAAAABgAAAAUAAAAEAAAABwAAAAUAAAAGAAAAAACgwAAAoMAAAKDAAACgwAAAoMAAAKBAAACgwAAAoEAAAKDAAACgwAAAoEAAAKBAAACgQAAAoMAAAKDAAACgQAAAoMAAAKBAAACgQAAAoEAAAKDAAACgQAAAoEAAAKBA\",\"byteLength\":240}],\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":144},{\"buffer\":0,\"byteOffset\":144,\"byteLength\":96}]}";
function cubeFile(name = 'part.gltf') {
  const document = JSON.parse(original);
  document.nodes[0].scale = [0.01, 0.01, 0.01];
  const bytes = new TextEncoder().encode(JSON.stringify(document));
  return { name, arrayBuffer: async () => bytes.buffer.slice(0) };
}
for (const [unit, size, volume] of [['millimetres', 0.1, 0.001], ['metres', 100, 1000000]]) {
  test(`actual glTF loader and advisory use explicit ${unit} before measuring`, async () => {
    const object = await loadStandaloneModel(cubeFile(), {
      gltfUnitPolicy: Object.freeze({ version: 1, unit: unit === 'metres' ? 1 : 0 }),
    });
    try {
      const claim = analyzeUploadDerivedGeometry(object);
      assert.ok(Math.abs(claim.dimensionXmm - size) < 0.00001);
      assert.ok(Math.abs(claim.dimensionYmm - size) < 0.00001);
      assert.ok(Math.abs(claim.dimensionZmm - size) < 0.00001);
      assert.ok(Math.abs(claim.volumeMm3 - volume) < Math.max(0.000001, volume * 0.000001));
    } finally {
      object.traverse(child => {
        child.geometry?.dispose();
        for (const material of Array.isArray(child.material) ? child.material : [child.material]) material?.dispose();
      });
    }
  });
}
test('missing glTF selection refuses before parser or raw hash reads', async () => {
  let reads = 0, loads = 0;
  const interop = createWorkflowPreviewInterop({
    loadModel: () => { loads++; throw new Error('Unexpected parser.'); },
    analyzeGeometry: () => { throw new Error('Unexpected analysis.'); },
    createViewer: () => { throw new Error('Unexpected viewer.'); },
    thicknessAnalyzer: { analyze: async () => null, dispose() {} },
  });
  const file = { name: 'part.glb', arrayBuffer() { reads++; throw new Error('Unexpected read.'); } };
  try {
    const [key] = interop.beginSelection({ files: [file] });
    await assert.rejects(interop.getGeometryClaim(key));
    assert.equal(interop.getFailureCategory(key), 'unit_required');
    assert.equal(reads, 0);
    assert.equal(loads, 0);
  } finally { interop.dispose(); }
});
test('embedded glTF buffer views are decoded without a network fetch', async () => {
  const originalFetch = globalThis.fetch;
  let requests = 0;
  globalThis.fetch = () => { requests++; return Promise.reject(new Error('Network access is forbidden.')); };
  let object;
  try {
    object = await loadStandaloneModel(cubeFile(), { gltfUnitPolicy: { version: 1, unit: 1 } });
    const claim = analyzeUploadDerivedGeometry(object);
    assert.ok(Math.abs(claim.dimensionXmm - 100) < 0.00001);
    assert.ok(Math.abs(claim.volumeMm3 - 1000000) < 1);
    assert.equal(requests, 0);
  } finally {
    globalThis.fetch = originalFetch;
    object?.traverse(child => {
      child.geometry?.dispose();
      for (const material of Array.isArray(child.material) ? child.material : [child.material]) material?.dispose();
    });
  }
});
for (const [name, mutate] of [
  ['declared length mismatch', document => { document.buffers[0].byteLength--; }],
  ['invalid base64', document => { document.buffers[0].uri = 'data:application/octet-stream;base64,!!!!'; }],
  ['external buffer', document => { document.buffers[0].uri = 'https://example.invalid/model.bin'; }],
  ['negative view offset', document => { document.bufferViews[0].byteOffset = -1; }],
  ['view beyond buffer', document => { document.bufferViews[1].byteLength = 241; }],
]) {
  test(`embedded glTF refuses ${name} without fetching`, async () => {
    const document = JSON.parse(original);
    mutate(document);
    const bytes = new TextEncoder().encode(JSON.stringify(document));
    const originalFetch = globalThis.fetch;
    let requests = 0;
    globalThis.fetch = () => { requests++; return Promise.reject(new Error('Network access is forbidden.')); };
    try {
      await assert.rejects(loadStandaloneModel({ name: 'invalid.gltf', arrayBuffer: async () => bytes.buffer },
        { gltfUnitPolicy: { version: 1, unit: 1 } }));
      assert.equal(requests, 0);
    } finally { globalThis.fetch = originalFetch; }
  });
}
test('selection-time units survive later dropdown change and retry without rescaling the old scene', async () => {
  let unit = 'metres';
  const policyReads = [];
  const file = cubeFile();
  const input = {
    files: [file],
    closest: () => ({
      querySelector: selector => selector === '[data-workflow-gltf-unit]' ? { value: unit } : null,
      addEventListener() {}, removeEventListener() {},
    }),
    addEventListener(name, handler) { if (name === 'change') this.change = handler; },
    removeEventListener() {},
  };
  const interop = createWorkflowPreviewInterop({
    loadModel: (selected, options) => { policyReads.push(options.gltfUnitPolicy); return loadStandaloneModel(selected, options); },
    analyzeGeometry: analyzeUploadDerivedGeometry,
    createViewer: () => { throw new Error('No renderer is needed.'); },
    thicknessAnalyzer: { analyze: async () => null, dispose() {} },
  });
  try {
    interop.bindInput(input);
    input.change();
    unit = 'millimetres';
    const [key] = interop.beginSelection(input);
    const first = await interop.getGeometryClaim(key);
    const retry = interop.retry(key);
    const repeated = await interop.getGeometryClaim(retry);
    const [next] = interop.beginSelection(input);
    const nextClaim = await interop.getGeometryClaim(next);
    assert.equal(first.gltfUnitPolicy.unit, 1);
    assert.equal(repeated.gltfUnitPolicy.unit, 1);
    assert.equal(nextClaim.gltfUnitPolicy.unit, 0);
    assert.ok(Math.abs(first.dimensionXmm - 100) < 0.00001);
    assert.equal(first.dimensionXmm, repeated.dimensionXmm);
    assert.ok(Math.abs(nextClaim.dimensionXmm - 0.1) < 0.00001);
    assert.equal(first.sha256, repeated.sha256);
    assert.equal(first.sha256, nextClaim.sha256);
    assert.ok(policyReads.every(Object.isFrozen));
  } finally { interop.dispose(); }
});

test('a mixed selection keeps STL millimetres while glTF uses its explicit metres', async () => {
  const stl = new TextEncoder().encode('solid sheet\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 10 0 0\nvertex 0 10 0\nendloop\nendfacet\nendsolid sheet');
  const interop = createWorkflowPreviewInterop({
    loadModel: loadStandaloneModel,
    analyzeGeometry: analyzeUploadDerivedGeometry,
    createViewer: () => { throw new Error('No renderer is needed.'); },
    thicknessAnalyzer: { analyze: async () => null, dispose() {} },
  });
  try {
    const keys = interop.beginSelection({ files: [
      cubeFile(),
      { name: 'sheet.stl', size: stl.length, arrayBuffer: async () => stl.buffer.slice(0) },
    ] }, Number.POSITIVE_INFINITY, 'metres');
    const [gltf, sheet] = await Promise.all(keys.map(key => interop.getGeometryClaim(key)));
    assert.equal(gltf.gltfUnitPolicy.version, 1);
    assert.equal(gltf.gltfUnitPolicy.unit, 1);
    assert.equal(sheet.gltfUnitPolicy.version, 0);
    assert.equal(sheet.gltfUnitPolicy.unit, 0);
    assert.equal(sheet.dimensionXmm, 10);
    assert.equal(sheet.dimensionYmm, 10);
  } finally { interop.dispose(); }
});

for (const [kind, nativeWidth, nativeVolume] of [
  ['morph', 0.15, 0.0015],
  ['skin', 0.2, 0.002],
  ['morph-skin', 0.3, 0.003],
]) {
  for (const binary of [false, true]) {
    for (const unit of [0, 1]) {
      test(`real ${kind} ${binary ? 'GLB' : 'glTF'} uses rendered default pose once in unit ${unit}`, async () => {
        const source = await readFile(new URL(
          `../../Legacy.Maliev.Web.Tests/TestAssets/GltfUnits/default-${kind}.gltf`, import.meta.url), 'utf8');
        const bytes = deformationBytes(source, binary);
        const object = await loadStandaloneModel({
          name: binary ? 'default.glb' : 'default.gltf',
          arrayBuffer: async () => bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength),
        }, { gltfUnitPolicy: { version: 1, unit } });
        try {
          const scale = unit === 1 ? 1000 : 1;
          const before = object.scale.toArray();
          let deformed = false;
          object.traverse(child => {
            if (child.isMesh) {
              assert.equal(child.isSkinnedMesh === true, kind.includes('skin'));
              if (kind.includes('morph')) assert.equal(child.morphTargetInfluences[0], 0.5);
              deformed = true;
            }
          });
          assert.equal(deformed, true);
          const first = analyzeUploadDerivedGeometry(object);
          const repeated = analyzeUploadDerivedGeometry(object);
          for (const claim of [first, repeated]) {
            assert.equal(claim.facetCount, 12);
            assert.ok(Math.abs(claim.dimensionXmm - nativeWidth * scale) < 0.0001);
            assert.ok(Math.abs(claim.dimensionYmm - 0.1 * scale) < 0.0001);
            assert.ok(Math.abs(claim.dimensionZmm - 0.1 * scale) < 0.0001);
            assert.ok(Math.abs(claim.volumeMm3 - nativeVolume * scale ** 3)
              < Math.max(1e-8, nativeVolume * scale ** 3 * 1e-6));
            assert.equal(claim.nonWatertight, false);
            assert.equal(claim.volumeMethod, 'signed-mesh-volume');
          }
          const meshes = thicknessMeshes(object);
          assert.equal(meshes.length, 1);
          const points = [];
          for (const mesh of meshes) {
            const matrix = new Matrix4().fromArray(mesh.matrix);
            for (let index = 0; index < mesh.position.length; index += 3) {
              points.push(new Vector3(...mesh.position.slice(index, index + 3)).applyMatrix4(matrix));
            }
          }
          for (const [axis, expected] of [['x', nativeWidth], ['y', 0.1], ['z', 0.1]]) {
            assert.ok(Math.abs(Math.max(...points.map(point => point[axis]))
              - Math.min(...points.map(point => point[axis])) - expected * scale) < 0.0001);
          }
          const minimumX = kind === 'morph' ? -0.055 : kind === 'skin' ? 0.02 : -0.03;
          assert.ok(Math.abs(Math.min(...points.map(point => point.x)) - minimumX * scale) < 0.0001);
          assert.ok(Math.abs(Math.min(...points.map(point => point.y)) + 0.09 * scale) < 0.0001);
          assert.ok(Math.abs(Math.min(...points.map(point => point.z)) + 0.02 * scale) < 0.0001);
          assert.deepEqual(object.scale.toArray(), before);
          assert.deepEqual(repeated, first);
        } finally {
          object.traverse(child => {
            child.geometry?.dispose();
            child.skeleton?.dispose();
            for (const material of Array.isArray(child.material) ? child.material : [child.material]) material?.dispose();
          });
        }
      });
    }
  }
}

// Same embedded-buffer fixture bytes are consumed by the C# trusted reader tests.
function deformationBytes(source, binary) {
  if (!binary) return Buffer.from(source);
  const document = JSON.parse(source);
  const content = Buffer.from(document.buffers[0].uri.split(',')[1], 'base64');
  delete document.buffers[0].uri;
  const json = Buffer.from(JSON.stringify(document));
  const padded = (json.length + 3) & ~3;
  const bytes = Buffer.alloc(12 + 8 + padded + 8 + content.length);
  bytes.writeUInt32LE(0x46546c67, 0);
  bytes.writeUInt32LE(2, 4);
  bytes.writeUInt32LE(bytes.length, 8);
  bytes.writeUInt32LE(padded, 12);
  bytes.writeUInt32LE(0x4e4f534a, 16);
  bytes.fill(0x20, 20, 20 + padded);
  json.copy(bytes, 20);
  bytes.writeUInt32LE(content.length, 20 + padded);
  bytes.writeUInt32LE(0x004e4942, 24 + padded);
  content.copy(bytes, 28 + padded);
  return bytes;
}
