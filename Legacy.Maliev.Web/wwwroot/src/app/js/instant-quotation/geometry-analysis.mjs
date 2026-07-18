export const geometryAnalysisLimits = Object.freeze({
  normalProfileSamples: 64,
  highFacetProfileSamples: 24,
  highFacetThreshold: 250000,
  topologyTriangleLimit: 200000,
  minimumDimensionMm: 3,
  maximumDimensionMm: 350,
});

/**
 * Produces preview-only compatibility hints. This object must never be sent to
 * the pricing boundary as authoritative geometry.
 */
export function analyzeAdvisoryGeometry(input) {
  const facets = finiteNonNegative(input?.facets);
  const dimensions = normalizeDimensions(input?.dimensions);
  const profileSamples = facets > geometryAnalysisLimits.highFacetThreshold
    ? geometryAnalysisLimits.highFacetProfileSamples
    : geometryAnalysisLimits.normalProfileSamples;
  const topologyChecked = facets <= geometryAnalysisLimits.topologyTriangleLimit
    && typeof input?.watertight === 'boolean';
  const watertight = topologyChecked ? input.watertight : null;
  const bodyCount = positiveInteger(input?.bodyCount, 1);
  const profiles = sampleProfiles(input?.triangles, profileSamples);
  const volume = finiteNonNegative(input?.volume);
  const volumeMethod = watertight === true && volume > 0
    ? 'signed-mesh-volume'
    : 'bounding-box-fallback';

  const result = {
    authoritative: false,
    isAuthoritative: false,
    authority: 'browser-advisory',
    dimensions,
    facets,
    bodyCount,
    watertight,
    topologyChecked,
    profileSamples,
    areaProfile: Object.freeze(profiles.area),
    perimeterProfile: Object.freeze(profiles.perimeter),
    volume: volumeMethod === 'signed-mesh-volume'
      ? volume
      : dimensions.x * dimensions.y * dimensions.z * (watertight === false ? 0.5 : 1),
    volumeMethod,
    dfm: Object.freeze(createDfmAdvice(dimensions, watertight, bodyCount)),
  };

  return Object.freeze(result);
}

function createDfmAdvice(dimensions, watertight, bodyCount) {
  const advice = [];
  const values = Object.values(dimensions);
  if (values.some(value => value > 0 && value < geometryAnalysisLimits.minimumDimensionMm)) {
    advice.push(adviceItem('dimension-too-small', 'warning'));
  }
  if (values.some(value => value > geometryAnalysisLimits.maximumDimensionMm)) {
    advice.push(adviceItem('dimension-too-large', 'warning'));
  }
  if (watertight === false) {
    advice.push(adviceItem('non-watertight', 'warning'));
  }
  if (bodyCount > 1) {
    advice.push(adviceItem('multiple-bodies', 'warning'));
  }
  return advice;
}

function adviceItem(code, severity) {
  return Object.freeze({ code, severity, authoritative: false });
}

function sampleProfiles(triangles, count) {
  if (!Array.isArray(triangles) || triangles.length < 9 || triangles.length % 9 !== 0) {
    return { area: Array(count).fill(0), perimeter: Array(count).fill(0) };
  }

  let minZ = Infinity;
  let maxZ = -Infinity;
  for (let index = 2; index < triangles.length; index += 3) {
    const z = Number(triangles[index]);
    if (!Number.isFinite(z)) return { area: Array(count).fill(0), perimeter: Array(count).fill(0) };
    minZ = Math.min(minZ, z);
    maxZ = Math.max(maxZ, z);
  }
  const height = maxZ - minZ;
  if (!(height > 0)) return { area: Array(count).fill(0), perimeter: Array(count).fill(0) };

  // Bounded oriented plane cuts preserve the legacy preview graph. Each
  // crossing contributes an oriented shoelace edge and contour length; no
  // browser result is promoted beyond advisory UI.
  const sums = new Float64Array(count);
  const perimeter = new Float64Array(count);
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
    const first = Math.max(0, Math.ceil(((triangleMin - minZ) / height) * (count - 1)));
    const last = Math.min(count - 1, Math.floor(((triangleMax - minZ) / height) * (count - 1)));

    for (let sample = first; sample <= last; sample += 1) {
      const level = minZ + (sample / (count - 1)) * height;
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
      perimeter[sample] += Math.hypot(edgeX, edgeY);
    }
  }

  return {
    area: Array.from(sums, value => Math.abs(value) * 0.5),
    perimeter: Array.from(perimeter),
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

function normalizeDimensions(value) {
  return Object.freeze({
    x: finiteNonNegative(value?.x),
    y: finiteNonNegative(value?.y),
    z: finiteNonNegative(value?.z),
  });
}

function finiteNonNegative(value) {
  const number = Number(value);
  return Number.isFinite(number) && number >= 0 ? number : 0;
}

function positiveInteger(value, fallback) {
  const number = Number(value);
  return Number.isSafeInteger(number) && number > 0 ? number : fallback;
}
