import assert from 'node:assert/strict';
import test from 'node:test';

import {
  BoxGeometry,
  BufferGeometry,
  Float32BufferAttribute,
  Group,
  Mesh,
} from 'three';

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
