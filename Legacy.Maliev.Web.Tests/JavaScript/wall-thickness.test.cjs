const test = require('node:test');
const assert = require('node:assert/strict');
const thickness = require('../../Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation/wall-thickness.worker.js');

const identity = Float32Array.from([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);

function meshFromTriangles(triangles) {
  return { position: Float32Array.from(triangles.flat(2)), index: null, matrix: identity };
}

function quad(a, b, c, d, reverse = false) {
  return reverse ? [[a, c, b], [a, d, c]] : [[a, b, c], [a, c, d]];
}

function boxMesh(width, depth, height, options = {}) {
  const p000 = [0, 0, 0];
  const p100 = [width, 0, 0];
  const p110 = [width, depth, 0];
  const p010 = [0, depth, 0];
  const p001 = [0, 0, height];
  const p101 = [width, 0, height];
  const p111 = [width, depth, height];
  const p011 = [0, depth, height];
  const triangles = [
    ...quad(p000, p010, p110, p100),
    ...quad(p001, p101, p111, p011, options.reverseFarFace === true),
    ...quad(p000, p100, p101, p001),
    ...quad(p100, p110, p111, p101),
    ...quad(p110, p010, p011, p111),
    ...quad(p010, p000, p001, p011),
  ];
  if (options.coincident === true) triangles.push(triangles[0].map((point) => [...point]));
  if (options.collapsed === true) triangles.push([[0, 0, 0], [1, 1, 1], [1, 1, 1]]);
  return meshFromTriangles(triangles);
}

function indexedBoxMesh(width, depth, height) {
  const position = Float32Array.from([
    0, 0, 0, width, 0, 0, width, depth, 0, 0, depth, 0,
    0, 0, height, width, 0, height, width, depth, height, 0, depth, height,
  ]);
  const index = Uint32Array.from([
    0, 3, 2, 0, 2, 1, 4, 5, 6, 4, 6, 7,
    0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5,
    2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7,
  ]);
  return { position, index, matrix: identity };
}

function openSheetMesh(width, depth) {
  return meshFromTriangles(quad([0, 0, 0], [width, 0, 0], [width, depth, 0], [0, depth, 0]));
}

function wedgeMesh() {
  const low = 1;
  const high = 3;
  const length = 20;
  const depth = 10;
  const a = [0, 0, 0]; const b = [length, 0, 0];
  const c = [length, depth, 0]; const d = [0, depth, 0];
  const e = [0, 0, low]; const f = [length, 0, high];
  const g = [length, depth, high]; const h = [0, depth, low];
  return meshFromTriangles([
    ...quad(a, d, c, b), ...quad(e, f, g, h),
    ...quad(a, b, f, e), ...quad(b, c, g, f),
    ...quad(c, d, h, g), ...quad(d, a, e, h),
  ]);
}

function tessellatedSlab(cells, height = 2) {
  const triangles = [];
  for (let y = 0; y < cells; y += 1) {
    for (let x = 0; x < cells; x += 1) {
      const a = [x, y, 0]; const b = [x + 1, y, 0];
      const c = [x + 1, y + 1, 0]; const d = [x, y + 1, 0];
      const e = [x, y, height]; const f = [x + 1, y, height];
      const g = [x + 1, y + 1, height]; const h = [x, y + 1, height];
      triangles.push(...quad(a, d, c, b), ...quad(e, f, g, h));
    }
  }
  const n = cells;
  for (let i = 0; i < cells; i += 1) {
    triangles.push(...quad([i, 0, 0], [i + 1, 0, 0], [i + 1, 0, height], [i, 0, height]));
    triangles.push(...quad([n, i, 0], [n, i + 1, 0], [n, i + 1, height], [n, i, height]));
    triangles.push(...quad([i + 1, n, 0], [i, n, 0], [i, n, height], [i + 1, n, height]));
    triangles.push(...quad([0, i + 1, 0], [0, i, 0], [0, i, height], [0, i + 1, height]));
  }
  return meshFromTriangles(triangles);
}

function deterministicSummary(summary) {
  const copy = { ...summary };
  delete copy.durationMs;
  return copy;
}

test('parallel slab measures the known wall along opposing face normals', () => {
  const result = thickness.analyze([boxMesh(20, 12, 2)], { maxSamples: 10_000 });
  assert.equal(result.summary.state, 'complete');
  assert.ok(result.summary.measuredAreaRatio >= 0.99);
  assert.ok(Math.abs(result.summary.minMm - 2) < 1e-4);
});

test('thin groove marks the corresponding region on a coarse opposing face without painting the whole face', () => {
  const outer = quad([0, 0, 0], [20, 0, 0], [20, 0, 2], [0, 0, 2]);
  const groove = quad([0, 0.75, 0], [20, 0.75, 0], [20, 0.75, 0.5], [0, 0.75, 0.5], true);
  const far = quad([0, 3.5, 0], [20, 3.5, 0], [20, 3.5, 2], [0, 3.5, 2], true);
  const result = thickness.analyze([meshFromTriangles([...outer, ...groove, ...far])]);
  assert.ok(result.fields[0][0] > 0.8, 'the coarse outer face centroid misses the thin groove');
  const patches = result.oppositeSurfacePatches;
  assert.ok(patches && patches.length > 0, 'the thin inner face must mark its opposite surface');
  assert.equal(patches.length % 13, 0);
  for (let offset = 0; offset < patches.length; offset += 13) {
    assert.ok(Math.min(...patches.slice(offset + 9, offset + 12)) < 0.8);
    assert.ok(patches[offset + 12] > 0.8, 'the opposite coarse face cannot show this region from its vertex field');
    for (let corner = 0; corner < 3; corner += 1) {
      assert.ok(Math.abs(patches[offset + corner * 3 + 1]) < 1e-4);
      assert.ok(patches[offset + corner * 3 + 2] <= 0.5 + 1e-4);
    }
  }
});

test('opposite-face highlighting never extends beyond the face struck by the thickness ray', () => {
  const target = [[0, 0, 0], [2, 0, 0], [0, 0, 2]];
  const source = [[-1, 0.5, -1], [0, 0.5, 2], [2, 0.5, 0]];
  const result = thickness.analyze([meshFromTriangles([target, source])]);
  assert.ok(result.oppositeSurfacePatches.length > 0);
  assert.equal(result.oppositeSurfacePatches.length % 13, 0);
  for (let offset = 0; offset < result.oppositeSurfacePatches.length; offset += 13) {
    for (let corner = 0; corner < 3; corner += 1) {
      const x = result.oppositeSurfacePatches[offset + corner * 3];
      const z = result.oppositeSurfacePatches[offset + corner * 3 + 2];
      assert.ok(x >= -1e-5 && z >= -1e-5 && x + z <= 2 + 1e-5,
        `projected corner escaped the target face: (${x}, ${z})`);
    }
  }
});

test('projected thin-wall triangles retain changing local thickness for a grey-to-red transition', () => {
  const target = [[0, 0, 0], [2, 0, 0], [0, 0, 2]];
  const slopedSource = [[-1, 0.3, -1], [0, 0.5, 2], [2, 1.2, 0]];
  const result = thickness.analyze([meshFromTriangles([target, slopedSource])]);
  const patches = result.oppositeSurfacePatches;
  let foundTransition = false;
  for (let offset = 0; offset < patches.length; offset += 13) {
    if (Math.abs(patches[offset + 1]) > 1e-5) continue;
    const values = patches.slice(offset + 9, offset + 12);
    if (Math.min(...values) < 0.8 && Math.max(...values) > 0.8) foundTransition = true;
  }
  assert.ok(foundTransition, 'the target face needs per-corner readings spanning the thin-wall limit');
});

test('reversed far winding remains measurable', () => {
  const result = thickness.analyze([boxMesh(20, 12, 2, { reverseFarFace: true })]);
  assert.ok(Math.abs(result.summary.minMm - 2) < 1e-4);
});

test('open sheet is unmeasured rather than zero or safe', () => {
  const result = thickness.analyze([openSheetMesh(20, 20)]);
  assert.equal(result.summary.measuredSampleCount, 0);
  assert.equal(result.summary.state, 'unavailable');
  assert.ok(Array.from(result.fields[0]).every(Number.isNaN));
});

test('indexed shared vertices retain the minimum adjacent-face thickness', () => {
  const result = thickness.analyze([indexedBoxMesh(5, 5, 1)]);
  assert.ok(result.fields[0][0] <= 1.0001);
  assert.ok(result.fields[0][0] >= 0.9999);
});

test('display field blends duplicated STL vertices across a smooth face while retaining sharp corners', () => {
  const mesh = meshFromTriangles([
    [[0, 0, 0], [1, 0, 0], [1, 1, 0]],
    [[0, 0, 0], [1, 1, 0], [0, 1, 0]],
    [[0, 0, 0], [0, 0, 1], [1, 0, 0]],
  ]);
  const triangles = thickness._test.normalizeTriangles([mesh]).triangles;
  const field = Float32Array.from([1, 1, 1, 3, 3, 3, 5, 5, 5]);
  thickness._test.smoothDisplayField([mesh], triangles, [field]);
  assert.ok(Math.abs(field[0] - 2) < 1e-5);
  assert.ok(Math.abs(field[3] - 2) < 1e-5);
  assert.equal(field[2], 2);
  assert.equal(field[4], 2);
  assert.equal(field[6], 5);
  assert.equal(field[8], 5);
});

test('coincident faces do not create epsilon-sized thickness', () => {
  const result = thickness.analyze([boxMesh(20, 12, 2, { coincident: true })]);
  assert.ok(result.summary.minMm > 1.99);
});

test('collapsed triangles are excluded and diagnosed', () => {
  const result = thickness.analyze([boxMesh(20, 12, 2, { collapsed: true })]);
  assert.ok(result.summary.reasons.includes('degenerate-triangles'));
  assert.equal(result.summary.validTriangleCount, 12);
});

test('tapered wall returns finite deterministic normal-ray measurements', () => {
  const first = thickness.analyze([wedgeMesh()]);
  const second = thickness.analyze([wedgeMesh()]);
  assert.ok(Number.isFinite(first.summary.minMm));
  assert.deepEqual(deterministicSummary(first.summary), deterministicSummary(second.summary));
});

test('sampling is deterministic above the cap', () => {
  const mesh = tessellatedSlab(20);
  const first = thickness.analyze([mesh], { maxSamples: 200 });
  const second = thickness.analyze([mesh], { maxSamples: 200 });
  assert.equal(first.summary.state, 'sampled');
  assert.deepEqual(deterministicSummary(first.summary), deterministicSummary(second.summary));
  assert.deepEqual(first.fields.map(field => Array.from(field)), second.fields.map(field => Array.from(field)));
});

test('a mesh above 250000 triangles selects bounded sampled mode', () => {
  const result = thickness.analyze([tessellatedSlab(250)], { maxSamples: 2_000 });
  assert.ok(result.summary.triangleCount > 250_000);
  assert.equal(result.summary.sampledTriangleCount, 2_000);
  assert.equal(result.summary.state, 'sampled');
});
