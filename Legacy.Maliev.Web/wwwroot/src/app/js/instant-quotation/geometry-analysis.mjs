export const geometryAnalysisLimits = Object.freeze({
  normalProfileSamples: 64,
  highFacetProfileSamples: 24,
  highFacetThreshold: 250000,
  topologyTriangleLimit: 200000,
  minimumDimensionMm: 3,
  maximumDimensionMm: 350,
});

/**
 * Reproduces the legacy production mesh analysis over upload-derived geometry.
 * The result is deterministic Web-pricing input after the Web BFF binds it to
 * the successfully stored upload; it is not an independent server-side mesh
 * verification.
 */
export function analyzeUploadDerivedGeometry(object3D) {
  const triangles = extractWorldSpaceTriangles(object3D);
  const facetCount = triangles.length / 9;
  const profileSamples = facetCount > geometryAnalysisLimits.highFacetThreshold
    ? geometryAnalysisLimits.highFacetProfileSamples
    : geometryAnalysisLimits.normalProfileSamples;

  if (facetCount === 0) return emptyAnalysis();

  let minX = Infinity;
  let minY = Infinity;
  let minZ = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;
  let maxZ = -Infinity;
  let signedVolume = 0;
  let surfaceAreaMm2 = 0;

  for (let offset = 0; offset < triangles.length; offset += 9) {
    const ax = triangles[offset];
    const ay = triangles[offset + 1];
    const az = triangles[offset + 2];
    const bx = triangles[offset + 3];
    const by = triangles[offset + 4];
    const bz = triangles[offset + 5];
    const cx = triangles[offset + 6];
    const cy = triangles[offset + 7];
    const cz = triangles[offset + 8];

    minX = Math.min(minX, ax, bx, cx);
    minY = Math.min(minY, ay, by, cy);
    minZ = Math.min(minZ, az, bz, cz);
    maxX = Math.max(maxX, ax, bx, cx);
    maxY = Math.max(maxY, ay, by, cy);
    maxZ = Math.max(maxZ, az, bz, cz);

    signedVolume += (
      ax * (by * cz - bz * cy)
      - ay * (bx * cz - bz * cx)
      + az * (bx * cy - by * cx)
    ) / 6;

    const ux = bx - ax;
    const uy = by - ay;
    const uz = bz - az;
    const wx = cx - ax;
    const wy = cy - ay;
    const wz = cz - az;
    const crossX = uy * wz - uz * wy;
    const crossY = uz * wx - ux * wz;
    const crossZ = ux * wy - uy * wx;
    surfaceAreaMm2 += Math.hypot(crossX, crossY, crossZ) * 0.5;
  }

  const dimensionXmm = maxX - minX;
  const dimensionYmm = maxY - minY;
  const dimensionZmm = maxZ - minZ;
  const diagonal = Math.hypot(dimensionXmm, dimensionYmm, dimensionZmm);
  let profiles = computeAreaProfiles(triangles, minZ, maxZ, profileSamples);
  const quality = analyzeMeshQuality(triangles, diagonal);
  const minThicknessMm = minimumThickness(
    profiles,
    dimensionXmm,
    dimensionYmm,
    dimensionZmm);
  let volumeMm3 = Math.abs(signedVolume);
  let volumeMethod = 'signed-mesh-volume';
  const boundingBoxVolume = dimensionXmm * dimensionYmm * dimensionZmm;

  // This is the legacy production guard: an open mesh retains its signed
  // volume unless that volume is non-positive or cannot fit in its own bounds.
  if (quality.nonWatertight
      && (volumeMm3 <= 0 || volumeMm3 > boundingBoxVolume * 1.02)) {
    volumeMm3 = boundingBoxVolume * 0.5;
    profiles = null;
    volumeMethod = 'half-bounding-box-fallback';
  }

  const minimumDimension = Math.min(dimensionXmm, dimensionYmm, dimensionZmm);
  const maximumDimension = Math.max(dimensionXmm, dimensionYmm, dimensionZmm);
  const oddlySmall = minimumDimension > 0
    && minimumDimension < geometryAnalysisLimits.minimumDimensionMm;
  const oddlyLarge = maximumDimension > geometryAnalysisLimits.maximumDimensionMm;

  return {
    version: 1,
    dimensionXmm,
    dimensionYmm,
    dimensionZmm,
    volumeMm3,
    surfaceAreaMm2,
    areaProfileMm2: profiles?.area ?? null,
    perimeterProfileMm: profiles?.perimeter ?? null,
    facetCount,
    bodyCount: quality.bodyCount,
    topologyChecked: quality.checked,
    nonWatertight: quality.nonWatertight,
    nonManifold: quality.nonManifold,
    minThicknessMm,
    oddlySmall,
    oddlyLarge,
    volumeMethod,
    dfmCodes: dfmCodes(quality, oddlySmall, oddlyLarge),
  };
}

function extractWorldSpaceTriangles(object3D) {
  const triangles = [];
  if (!object3D?.traverse) return triangles;

  object3D.updateWorldMatrix?.(true, true);
  object3D.traverse(child => {
    const position = child?.geometry?.attributes?.position;
    if (!child?.isMesh || !position) return;

    const index = child.geometry.index;
    const count = index ? index.count : position.count;
    for (let offset = 0; offset < count; offset += 1) {
      const vertexIndex = index ? index.getX(offset) : offset;
      const vertex = applyMatrixWorld(
        position.getX(vertexIndex),
        position.getY(vertexIndex),
        position.getZ(vertexIndex),
        child.matrixWorld?.elements);
      triangles.push(vertex[0], vertex[1], vertex[2]);
    }
  });
  return triangles;
}

function applyMatrixWorld(x, y, z, elements) {
  if (!elements || elements.length !== 16) return [x, y, z];
  const inverseW = 1 / (elements[3] * x + elements[7] * y + elements[11] * z + elements[15]);
  return [
    (elements[0] * x + elements[4] * y + elements[8] * z + elements[12]) * inverseW,
    (elements[1] * x + elements[5] * y + elements[9] * z + elements[13]) * inverseW,
    (elements[2] * x + elements[6] * y + elements[10] * z + elements[14]) * inverseW,
  ];
}

function computeAreaProfiles(triangles, minZ, maxZ, samples) {
  const height = maxZ - minZ;
  if (height <= 0 || triangles.length < 9) return null;

  const sums = new Float64Array(samples);
  const perimeters = new Float64Array(samples);
  for (let offset = 0; offset < triangles.length; offset += 9) {
    const ax = triangles[offset];
    const ay = triangles[offset + 1];
    const az = triangles[offset + 2];
    const bx = triangles[offset + 3];
    const by = triangles[offset + 4];
    const bz = triangles[offset + 5];
    const cx = triangles[offset + 6];
    const cy = triangles[offset + 7];
    const cz = triangles[offset + 8];
    const triangleMin = Math.min(az, bz, cz);
    const triangleMax = Math.max(az, bz, cz);
    if (triangleMax <= triangleMin) continue;

    let normalX = (by - ay) * (cz - az) - (bz - az) * (cy - ay);
    let normalY = (bz - az) * (cx - ax) - (bx - ax) * (cz - az);
    const normalLength = Math.hypot(normalX, normalY);
    if (normalLength < 1e-9) continue;
    normalX /= normalLength;
    normalY /= normalLength;
    const directionX = -normalY;
    const directionY = normalX;
    const first = Math.max(0, Math.ceil(((triangleMin - minZ) / height) * (samples - 1)));
    const last = Math.min(samples - 1, Math.floor(((triangleMax - minZ) / height) * (samples - 1)));

    for (let sample = first; sample <= last; sample += 1) {
      const level = minZ + (sample / (samples - 1)) * height;
      const segment = trianglePlaneSegment(
        ax, ay, az, bx, by, bz, cx, cy, cz, level);
      if (!segment) continue;
      const edgeX = segment[2] - segment[0];
      const edgeY = segment[3] - segment[1];
      let [startX, startY, endX, endY] = segment;
      if (edgeX * directionX + edgeY * directionY < 0) {
        [startX, startY, endX, endY] = [endX, endY, startX, startY];
      }
      sums[sample] += startX * endY - endX * startY;
      perimeters[sample] += Math.hypot(edgeX, edgeY);
    }
  }

  return {
    area: Array.from(sums, value => Math.abs(value) * 0.5),
    perimeter: Array.from(perimeters),
  };
}

function trianglePlaneSegment(ax, ay, az, bx, by, bz, cx, cy, cz, level) {
  const points = [];
  addPlaneEdgeCrossing(points, ax, ay, az, bx, by, bz, level);
  addPlaneEdgeCrossing(points, bx, by, bz, cx, cy, cz, level);
  addPlaneEdgeCrossing(points, cx, cy, cz, ax, ay, az, level);
  return points.length < 4 ? null : points.slice(0, 4);
}

function addPlaneEdgeCrossing(points, x0, y0, z0, x1, y1, z1, level) {
  const distance0 = z0 - level;
  const distance1 = z1 - level;
  if ((distance0 > 0 && distance1 > 0) || (distance0 < 0 && distance1 < 0)) return;
  if (distance0 === distance1) return;
  const ratio = distance0 / (distance0 - distance1);
  if (ratio < 0 || ratio > 1) return;
  points.push(x0 + (x1 - x0) * ratio, y0 + (y1 - y0) * ratio);
}

function analyzeMeshQuality(triangles, diagonal) {
  const triangleCount = triangles.length / 9;
  if (triangleCount === 0) {
    return { nonWatertight: false, nonManifold: false, bodyCount: 0, checked: false };
  }
  if (triangleCount > geometryAnalysisLimits.topologyTriangleLimit) {
    return { nonWatertight: false, nonManifold: false, bodyCount: 1, checked: false };
  }

  const grid = Math.max((diagonal || 0) * 1e-5, 1e-4);
  const parents = new Map();
  const edges = new Map();
  const vertexKey = (x, y, z) => [
    Math.round(x / grid),
    Math.round(y / grid),
    Math.round(z / grid),
  ].join('_');
  const addNode = key => {
    if (!parents.has(key)) parents.set(key, key);
  };
  const find = key => {
    let current = key;
    while (parents.get(current) !== current) {
      parents.set(current, parents.get(parents.get(current)));
      current = parents.get(current);
    }
    return current;
  };
  const union = (left, right) => {
    const leftRoot = find(left);
    const rightRoot = find(right);
    if (leftRoot !== rightRoot) parents.set(leftRoot, rightRoot);
  };
  const addEdge = (left, right) => {
    if (left === right) return;
    const key = left < right ? `${left}|${right}` : `${right}|${left}`;
    edges.set(key, (edges.get(key) || 0) + 1);
  };

  for (let offset = 0; offset < triangles.length; offset += 9) {
    const first = vertexKey(triangles[offset], triangles[offset + 1], triangles[offset + 2]);
    const second = vertexKey(triangles[offset + 3], triangles[offset + 4], triangles[offset + 5]);
    const third = vertexKey(triangles[offset + 6], triangles[offset + 7], triangles[offset + 8]);
    addNode(first);
    addNode(second);
    addNode(third);
    union(first, second);
    union(second, third);
    addEdge(first, second);
    addEdge(second, third);
    addEdge(third, first);
  }

  let nonWatertight = false;
  let nonManifold = false;
  for (const count of edges.values()) {
    if (count === 1) nonWatertight = true;
    else if (count > 2) nonManifold = true;
  }
  const roots = new Set();
  for (const key of parents.keys()) roots.add(find(key));
  return {
    nonWatertight,
    nonManifold,
    bodyCount: roots.size,
    checked: true,
  };
}

function minimumThickness(profiles, dimensionXmm, dimensionYmm, dimensionZmm) {
  let minimum = null;
  if (profiles) {
    for (let index = 0; index < profiles.area.length; index += 1) {
      if (profiles.perimeter[index] > 1e-6 && profiles.area[index] > 0) {
        const thickness = (2 * profiles.area[index]) / profiles.perimeter[index];
        minimum = minimum === null ? thickness : Math.min(minimum, thickness);
      }
    }
  }
  if (minimum !== null) return minimum;
  const fallback = Math.min(
    dimensionXmm || Infinity,
    dimensionYmm || Infinity,
    dimensionZmm || Infinity);
  return Number.isFinite(fallback) ? fallback : 0;
}

function dfmCodes(quality, oddlySmall, oddlyLarge) {
  const codes = [];
  if (quality.nonWatertight) codes.push('non-watertight');
  if (quality.nonManifold) codes.push('non-manifold');
  if (quality.bodyCount > 1) codes.push('multiple-bodies');
  if (oddlySmall) codes.push('dimension-too-small');
  if (oddlyLarge) codes.push('dimension-too-large');
  return codes;
}

function emptyAnalysis() {
  return {
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
  };
}
