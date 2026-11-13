import assert from 'node:assert/strict';
import test from 'node:test';

import {
  BoxGeometry,
  BufferGeometry,
  Float32BufferAttribute,
  Group,
  InstancedMesh,
  Mesh,
} from 'three';

import { loadStandaloneModel } from '../wwwroot/src/app/js/instant-quotation/model-viewer.mjs';

import {
  analyzeUploadDerivedGeometry,
  geometryAnalysisLimits,
} from '../wwwroot/src/app/js/instant-quotation/geometry-analysis.mjs';

test('analyzes indexed triangles in world space with the production geometry contract', () => {
  const object = new Group();
  object.position.set(10, 20, 30);
  object.scale.set(2, 3, 4);
  object.add(tetrahedronMesh());

  const result = analyzeUploadDerivedGeometry(object);

  assert.deepEqual(geometryAnalysisLimits, {
    normalProfileSamples: 64,
    highFacetProfileSamples: 24,
    highFacetThreshold: 250000,
    topologyTriangleLimit: 200000,
    minimumDimensionMm: 3,
    maximumDimensionMm: 350,
  });
  assert.equal(result.version, 1);
  assert.equal(result.dimensionXmm, 2);
  assert.equal(result.dimensionYmm, 3);
  assert.equal(result.dimensionZmm, 4);
  assert.equal(result.volumeMm3, 4);
  assert.ok(Math.abs(result.surfaceAreaMm2 - (13 + Math.sqrt(61))) < 1e-10);
  assert.equal(result.facetCount, 4);
  assert.equal(result.areaProfileMm2.length, 64);
  assert.equal(result.perimeterProfileMm.length, 64);
  assert.equal(result.unsupportedAreaProfileMm2.length, 64);
  assert.ok(result.minThicknessMm > 0);
  assert.equal(result.nonWatertight, false);
  assert.equal(result.nonManifold, false);
  assert.equal(result.bodyCount, 1);
  assert.equal(result.topologyChecked, true);
  assert.equal(result.oddlySmall, false);
  assert.equal(result.oddlyLarge, false);
  assert.equal(result.volumeMethod, 'signed-mesh-volume');
  assert.deepEqual(result.dfmCodes, []);
  assert.doesNotThrow(() => JSON.stringify(result));
});

test('preserves a plausible signed volume for a non-watertight upload', () => {
  const mesh = triangleSoupMesh(tetrahedronTriangles().slice(9));

  const result = analyzeUploadDerivedGeometry(mesh);

  assert.equal(result.nonWatertight, true);
  assert.equal(result.volumeMm3, 1 / 6);
  assert.equal(result.volumeMethod, 'signed-mesh-volume');
  assert.equal(result.areaProfileMm2.length, 64);
  assert.equal(result.perimeterProfileMm.length, 64);
});

test('uses the half-bounding-box fallback only for an impossible non-watertight volume', () => {
  const triangles = tetrahedronTriangles();
  const exaggerated = [
    ...triangles,
    ...triangles,
    ...triangles,
    ...triangles,
    ...triangles,
    ...triangles,
    ...triangles,
    // An open triangle inside the same bounds makes this a non-watertight soup.
    0.2, 0.2, 0.2, 0.3, 0.2, 0.2, 0.2, 0.3, 0.2,
  ];

  const baseline = analyzeUploadDerivedGeometry(triangleSoupMesh(triangles));
  const result = analyzeUploadDerivedGeometry(triangleSoupMesh(exaggerated));

  assert.equal(result.nonWatertight, true);
  assert.equal(result.volumeMm3, 0.5);
  assert.equal(result.volumeMethod, 'half-bounding-box-fallback');
  assert.equal(result.areaProfileMm2, null);
  assert.equal(result.perimeterProfileMm, null);
  assert.equal(result.unsupportedAreaProfileMm2, null);
  assert.ok(Math.abs(result.minThicknessMm - baseline.minThicknessMm) < 1e-12);
});

test('quantizes topology, reports disconnected bodies and non-manifold edges, and emits production DFM codes', () => {
  const first = tetrahedronTriangles();
  const second = tetrahedronTriangles().map((value, index) => index % 3 === 0 ? value + 400 : value);
  const sharedEdgeThirdFace = [0, 0, 0, 1, 0, 0, 0.5, 0, -1];
  const object = triangleSoupMesh([...first, ...second, ...sharedEdgeThirdFace]);

  const result = analyzeUploadDerivedGeometry(object);

  assert.equal(result.nonWatertight, true);
  assert.equal(result.nonManifold, true);
  assert.equal(result.bodyCount, 2);
  assert.equal(result.oddlySmall, false);
  assert.equal(result.oddlyLarge, true);
  assert.deepEqual(result.dfmCodes, [
    'non-watertight',
    'non-manifold',
    'multiple-bodies',
    'dimension-too-large',
  ]);
});

test('collapsed CAD seam triangles do not invent topology defects', () => {
  const closed = tetrahedronTriangles();
  const collapsedSeam = [0, 0, 0, 1, 0, 0, 0, 0, 0];

  const result = analyzeUploadDerivedGeometry(triangleSoupMesh([...closed, ...collapsedSeam]));

  assert.equal(result.nonWatertight, false);
  assert.equal(result.nonManifold, false);
  assert.equal(result.bodyCount, 1);
  assert.deepEqual(
    result.dfmCodes.filter(code => ['non-watertight', 'non-manifold', 'multiple-bodies'].includes(code)),
    []);
});

test('genuine open and multiply shared edges remain topology defects', () => {
  const closed = tetrahedronTriangles();
  const open = analyzeUploadDerivedGeometry(triangleSoupMesh(closed.slice(9)));
  const shared = analyzeUploadDerivedGeometry(triangleSoupMesh([...closed, ...closed.slice(0, 9)]));

  assert.equal(open.nonWatertight, true);
  assert.equal(open.nonManifold, false);
  assert.equal(shared.nonManifold, true);
});

test('bounds thin-part thickness by the smallest overall model extent', () => {
  const result = analyzeUploadDerivedGeometry(new Mesh(new BoxGeometry(100, 100, 0.5)));

  assert.ok(result.minThicknessMm > 0);
  assert.ok(result.minThicknessMm <= 0.5);
  assert.equal(result.oddlySmall, false);
  assert.equal(result.oddlyLarge, false);
  assert.deepEqual(result.dfmCodes, []);
});

test('ignores degenerate cap slices when estimating a sloped ring wall', () => {
  const result = analyzeUploadDerivedGeometry(slopedCapRingMesh());

  assert.ok(result.dimensionXmm >= 33 && result.dimensionXmm <= 33.6);
  assert.ok(result.dimensionYmm >= 33 && result.dimensionYmm <= 33.6);
  assert.ok(result.dimensionZmm >= 5.9 && result.dimensionZmm <= 6.1);
  assert.ok(result.minThicknessMm >= 3.7 && result.minThicknessMm <= 3.9);
});

test('uses 24 profile samples and skips topology above the production limits', () => {
  const triangleCount = 250001;
  const positions = new Float32Array(triangleCount * 9);
  for (let offset = 0; offset < positions.length; offset += 9) {
    positions.set([0, 0, 0, 1, 0, 0, 0, 1, 1], offset);
  }

  const result = analyzeUploadDerivedGeometry(triangleSoupMesh(positions));

  assert.equal(result.facetCount, triangleCount);
  assert.equal(result.areaProfileMm2.length, 24);
  assert.equal(result.perimeterProfileMm.length, 24);
  assert.equal(result.topologyChecked, false);
  assert.equal(result.nonWatertight, false);
  assert.equal(result.nonManifold, false);
  assert.equal(result.bodyCount, 1);
});

test('returns the exact zero geometry shape for an object without triangles', () => {
  assert.deepEqual(analyzeUploadDerivedGeometry(new Group()), {
    version: 1,
    dimensionXmm: 0,
    dimensionYmm: 0,
    dimensionZmm: 0,
    volumeMm3: 0,
    surfaceAreaMm2: 0,
    areaProfileMm2: null,
    perimeterProfileMm: null,
    unsupportedAreaProfileMm2: null,
    facetCount: 0,
    bodyCount: 0,
    topologyChecked: false,
    nonWatertight: false,
    nonManifold: false,
    minThicknessMm: 0,
    oddlySmall: false,
    oddlyLarge: false,
    volumeMethod: 'signed-mesh-volume',
    dfmCodes: [],
  });
});

test('advisory extraction refuses a real InstancedMesh instead of measuring its base once', () => {
  const mesh = new InstancedMesh(tetrahedronMesh().geometry, undefined, 2);
  try {
    assert.throws(() => analyzeUploadDerivedGeometry(mesh), /unsupported.*node geometry/i);
  } finally {
    mesh.geometry.dispose();
    mesh.material.dispose();
    mesh.dispose();
  }
});

for (const declaration of ['used', 'required', 'undeclared']) {
  test(`actual GLTF loading with ${declaration} GPU instancing cannot become base-only advisory physics`, async () => {
    const object = await loadStandaloneModel(gltfGeometryFile({ declaration }), { gltfUnitPolicy: { version: 1, unit: 0 } });
    try {
      let instanced = false;
      object.traverse(child => { if (child.isInstancedMesh) instanced = true; });
      assert.equal(instanced, true);
      assert.throws(() => analyzeUploadDerivedGeometry(object), /unsupported.*node geometry/i);
    } finally {
      disposeLoadedGeometry(object);
    }
  });
}

test('actual core GLTF loading preserves native printing units despite extras.gltfExtensions collision', async () => {
  const object = await loadStandaloneModel(gltfGeometryFile({ metadata: true }), { gltfUnitPolicy: { version: 1, unit: 0 } });
  try {
    let collision = false;
    object.traverse(child => {
      if (child.userData?.gltfExtensions?.EXT_mesh_gpu_instancing?.note === 'freeform extras collision') {
        collision = true;
      }
    });
    assert.equal(collision, true);
    const result = analyzeUploadDerivedGeometry(object);
    assert.equal(result.facetCount, 4);
    assert.ok(Math.abs(result.dimensionXmm - 1) < 1e-10);
    assert.ok(Math.abs(result.dimensionYmm - 1) < 1e-10);
    assert.ok(Math.abs(result.dimensionZmm - 1) < 1e-10);
    assert.ok(Math.abs(result.volumeMm3 - 1 / 6) < 1e-10);
  } finally {
    disposeLoadedGeometry(object);
  }
});

test('actual GLTF loading preserves unknown optional node extensions with core fallback', async () => {
  const object = await loadStandaloneModel(gltfGeometryFile({ nodeExtension: 'UNKNOWN_geometry' }), { gltfUnitPolicy: { version: 1, unit: 0 } });
  try {
    const result = analyzeUploadDerivedGeometry(object);
    assert.equal(result.facetCount, 4);
    assert.ok(Math.abs(result.volumeMm3 - 1 / 6) < 1e-10);
  } finally {
    disposeLoadedGeometry(object);
  }
});

test('freeform parent gltfExtensions metadata does not imply rendered instancing', () => {
  const object = new Group();
  object.userData.gltfExtensions = {
    EXT_mesh_gpu_instancing: { note: 'extras collision, not actual InstancedMesh' },
    MSFT_lod: { ids: [1] },
  };
  object.add(tetrahedronMesh());
  try {
    const result = analyzeUploadDerivedGeometry(object);
    assert.equal(result.facetCount, 4);
    assert.ok(Math.abs(result.volumeMm3 - 1 / 6) < 1e-10);
  } finally {
    disposeLoadedGeometry(object);
  }
});

function gltfGeometryFile({ declaration, metadata = false, nodeExtension } = {}) {
  const positions = tetrahedronTriangles();
  const values = new Float32Array([...positions, 0, 0, 0, 10, 0, 0]);
  const document = {
    asset: { version: '2.0' },
    scene: 0,
    scenes: [{ nodes: [0] }],
    nodes: [{ mesh: 0 }],
    meshes: [{ primitives: [{ attributes: { POSITION: 0 } }] }],
    buffers: [{ byteLength: values.byteLength }],
    bufferViews: [
      { buffer: 0, byteOffset: 0, byteLength: positions.length * 4 },
      { buffer: 0, byteOffset: positions.length * 4, byteLength: 24 },
    ],
    accessors: [
      { bufferView: 0, componentType: 5126, count: positions.length / 3, type: 'VEC3',
        min: [0, 0, 0], max: [1, 1, 1] },
      { bufferView: 1, componentType: 5126, count: 2, type: 'VEC3' },
    ],
  };
  if (declaration) {
    document.nodes[0].extensions = { EXT_mesh_gpu_instancing: { attributes: { TRANSLATION: 1 } } };
    if (declaration !== 'undeclared') document.extensionsUsed = ['EXT_mesh_gpu_instancing'];
    if (declaration === 'required') document.extensionsRequired = ['EXT_mesh_gpu_instancing'];
  }
  if (nodeExtension) {
    document.nodes[0].extensions = { [nodeExtension]: {} };
    document.extensionsUsed = [nodeExtension];
  }
  if (metadata) {
    document.extensionsUsed = ['KHR_lights_punctual', 'KHR_materials_unlit'];
    document.extensions = { KHR_lights_punctual: { lights: [{ type: 'point' }] } };
    document.nodes[0].extensions = { KHR_lights_punctual: { light: 0 } };
    document.nodes[0].extras = {
      extensions: { EXT_mesh_gpu_instancing: { note: 'metadata only' } },
      gltfExtensions: { EXT_mesh_gpu_instancing: { note: 'freeform extras collision' } },
    };
    document.materials = [{ extensions: { KHR_materials_unlit: {} } }];
    document.meshes[0].primitives[0].material = 0;
  }
  // A real GLB avoids network fetches and browser-only progress events in the Node loader tests.
  const encoded = new TextEncoder().encode(JSON.stringify(document));
  const jsonLength = Math.ceil(encoded.length / 4) * 4;
  const buffer = new ArrayBuffer(12 + 8 + jsonLength + 8 + values.byteLength);
  const view = new DataView(buffer);
  view.setUint32(0, 0x46546c67, true);
  view.setUint32(4, 2, true);
  view.setUint32(8, buffer.byteLength, true);
  view.setUint32(12, jsonLength, true);
  view.setUint32(16, 0x4e4f534a, true);
  new Uint8Array(buffer, 20, jsonLength).fill(0x20);
  new Uint8Array(buffer, 20, encoded.length).set(encoded);
  const binaryHeader = 20 + jsonLength;
  view.setUint32(binaryHeader, values.byteLength, true);
  view.setUint32(binaryHeader + 4, 0x004e4942, true);
  new Uint8Array(buffer, binaryHeader + 8).set(new Uint8Array(values.buffer));
  return { name: 'geometry.glb', arrayBuffer: async () => buffer };
}

function disposeLoadedGeometry(object) {
  const geometries = new Set();
  const materials = new Set();
  object.traverse(child => {
    if (child.geometry) geometries.add(child.geometry);
    for (const material of Array.isArray(child.material) ? child.material : [child.material]) {
      if (material) materials.add(material);
    }
    if (child.isInstancedMesh) child.dispose();
  });
  for (const geometry of geometries) geometry.dispose();
  for (const material of materials) material.dispose();
}

function tetrahedronMesh() {
  const geometry = new BufferGeometry();
  geometry.setAttribute('position', new Float32BufferAttribute([
    0, 0, 0,
    1, 0, 0,
    0, 1, 0,
    0, 0, 1,
  ], 3));
  geometry.setIndex([
    0, 2, 1,
    0, 1, 3,
    0, 3, 2,
    1, 2, 3,
  ]);
  return new Mesh(geometry);
}

function slopedCapRingMesh() {
  const segments = 128;
  const outerRadius = 16.65;
  const innerRadius = 12.84;
  const height = 6.014;
  const capRise = 0.20;
  const triangles = [];
  const vertex = (radius, angle, z) => [radius * Math.cos(angle), radius * Math.sin(angle), z];
  const append = (a, b, c) => triangles.push(...a, ...b, ...c);

  for (let segment = 0; segment < segments; segment += 1) {
    const angle0 = (Math.PI * 2 * segment) / segments;
    const angle1 = (Math.PI * 2 * (segment + 1)) / segments;
    const outerBottom0 = vertex(outerRadius, angle0, 0);
    const outerBottom1 = vertex(outerRadius, angle1, 0);
    const outerTop0 = vertex(outerRadius, angle0, height - capRise);
    const outerTop1 = vertex(outerRadius, angle1, height - capRise);
    const innerBottom0 = vertex(innerRadius, angle0, capRise);
    const innerBottom1 = vertex(innerRadius, angle1, capRise);
    const innerTop0 = vertex(innerRadius, angle0, height);
    const innerTop1 = vertex(innerRadius, angle1, height);

    append(outerBottom0, outerBottom1, outerTop1);
    append(outerBottom0, outerTop1, outerTop0);
    append(innerBottom0, innerTop0, innerTop1);
    append(innerBottom0, innerTop1, innerBottom1);
    append(outerBottom0, innerBottom1, outerBottom1);
    append(outerBottom0, innerBottom0, innerBottom1);
    append(outerTop0, outerTop1, innerTop1);
    append(outerTop0, innerTop1, innerTop0);
  }

  return triangleSoupMesh(triangles);
}

function tetrahedronTriangles() {
  return [
    0, 0, 0, 0, 1, 0, 1, 0, 0,
    0, 0, 0, 1, 0, 0, 0, 0, 1,
    0, 0, 0, 0, 0, 1, 0, 1, 0,
    1, 0, 0, 0, 1, 0, 0, 0, 1,
  ];
}

function triangleSoupMesh(positions) {
  const geometry = new BufferGeometry();
  geometry.setAttribute('position', new Float32BufferAttribute(positions, 3));
  return new Mesh(geometry);
}
