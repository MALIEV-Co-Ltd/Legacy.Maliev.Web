// Deterministic, worker-local CNC occupancy and surface field.
// The source mesh only defines the solid boundary. Downstream evidence is indexed by field cells,
// never by source triangle, so equivalent CAD tessellations produce the same analysis surface.
(function (root) {
    'use strict';

    var FIELD_VERSION = 1;
    var MAX_AXIS_CELLS = 384;
    var MAX_FIELD_CELLS = 6000000;
    var MAX_RESPONSE_SURFACE_SAMPLES = 12000;
    var EPSILON = 1e-8;

    function vector(x, y, z) { return { x: x, y: y, z: z }; }
    function add(left, right) { return vector(left.x + right.x, left.y + right.y, left.z + right.z); }
    function scale(value, amount) { return vector(value.x * amount, value.y * amount, value.z * amount); }
    function dot(left, right) { return (left.x * right.x) + (left.y * right.y) + (left.z * right.z); }
    function length(value) { return Math.sqrt(dot(value, value)); }
    function normalize(value) {
        var magnitude = length(value);
        return magnitude > EPSILON ? scale(value, 1 / magnitude) : vector(0, 0, 0);
    }

    function framePoint(point, axes) {
        return vector(dot(point, axes[0]), dot(point, axes[1]), dot(point, axes[2]));
    }

    function worldPoint(point, axes) {
        return add(add(scale(axes[0], point.x), scale(axes[1], point.y)), scale(axes[2], point.z));
    }

    function transformedTriangles(triangles, axes) {
        var result = new Array(triangles.length / 3);
        for (var index = 0; index < triangles.length; index += 3) {
            result[index / 3] = framePoint(vector(triangles[index], triangles[index + 1], triangles[index + 2]), axes);
        }
        return result;
    }

    function fieldBounds(vertices) {
        var minimum = vector(Infinity, Infinity, Infinity);
        var maximum = vector(-Infinity, -Infinity, -Infinity);
        vertices.forEach(function (point) {
            minimum.x = Math.min(minimum.x, point.x);
            minimum.y = Math.min(minimum.y, point.y);
            minimum.z = Math.min(minimum.z, point.z);
            maximum.x = Math.max(maximum.x, point.x);
            maximum.y = Math.max(maximum.y, point.y);
            maximum.z = Math.max(maximum.z, point.z);
        });
        return { minimum: minimum, maximum: maximum };
    }

    function chooseResolution(bounds, options) {
        options = options || {};
        var spans = [
            Math.max(0, bounds.maximum.x - bounds.minimum.x),
            Math.max(0, bounds.maximum.y - bounds.minimum.y),
            Math.max(0, bounds.maximum.z - bounds.minimum.z)
        ];
        var positiveSpans = spans.filter(function (span) { return span > EPSILON; });
        var minimumSpan = positiveSpans.length > 0 ? Math.min.apply(Math, positiveSpans) : 1;
        var maximumSpan = positiveSpans.length > 0 ? Math.max.apply(Math, positiveSpans) : 1;
        var tolerance = Number.isFinite(options.geometryToleranceMm) && options.geometryToleranceMm > 0
            ? options.geometryToleranceMm : 0.5;
        var minimumCutterDiameter = Number.isFinite(options.minimumCutterDiameterMm) && options.minimumCutterDiameterMm > 0
            ? options.minimumCutterDiameterMm : 6;
        var requestedCellSize = Math.min(tolerance, minimumCutterDiameter / 6, minimumSpan / 96);
        requestedCellSize = Math.max(requestedCellSize, 0.02);
        var boundedCellSize = Math.max(requestedCellSize, maximumSpan / (MAX_AXIS_CELLS - 2));
        var dimensions = dimensionsFor(bounds, boundedCellSize);
        var totalCells = dimensions.x * dimensions.y * dimensions.z;
        while (totalCells > MAX_FIELD_CELLS) {
            boundedCellSize *= Math.max(1.005, Math.cbrt(totalCells / MAX_FIELD_CELLS));
            dimensions = dimensionsFor(bounds, boundedCellSize);
            totalCells = dimensions.x * dimensions.y * dimensions.z;
        }
        return {
            cellSizeMm: boundedCellSize,
            requestedCellSizeMm: requestedCellSize,
            resolutionLimited: boundedCellSize > requestedCellSize + EPSILON
        };
    }

    function dimensionsFor(bounds, cellSizeMm) {
        return {
            x: Math.max(3, Math.min(MAX_AXIS_CELLS, Math.ceil((bounds.maximum.x - bounds.minimum.x) / cellSizeMm) + 2)),
            y: Math.max(3, Math.min(MAX_AXIS_CELLS, Math.ceil((bounds.maximum.y - bounds.minimum.y) / cellSizeMm) + 2)),
            z: Math.max(3, Math.min(MAX_AXIS_CELLS, Math.ceil((bounds.maximum.z - bounds.minimum.z) / cellSizeMm) + 2))
        };
    }

    function linearIndex(x, y, z, dimensions) {
        return x + (dimensions.x * (y + (dimensions.y * z)));
    }

    function setBit(bits, index) {
        bits[index >>> 5] |= (1 << (index & 31));
    }

    function hasBit(bits, index) {
        return (bits[index >>> 5] & (1 << (index & 31))) !== 0;
    }

    function twoDimensionalBarycentric(y, z, a, b, c) {
        var denominator = ((b.z - c.z) * (a.y - c.y)) + ((c.y - b.y) * (a.z - c.z));
        if (Math.abs(denominator) <= EPSILON) { return null; }
        var first = (((b.z - c.z) * (y - c.y)) + ((c.y - b.y) * (z - c.z))) / denominator;
        var second = (((c.z - a.z) * (y - c.y)) + ((a.y - c.y) * (z - c.z))) / denominator;
        var third = 1 - first - second;
        var weights = [first, second, third];
        var edges = [[b, c], [c, a], [a, b]];
        // Half-open projected triangles own a shared edge exactly once. Merely
        // deduplicating crossing positions loses multiplicity where solids overlap.
        for (var index = 0; index < weights.length; index++) {
            if (weights[index] < -1e-10) { return null; }
            if (Math.abs(weights[index]) > 1e-10) { continue; }
            var from = edges[index][denominator > 0 ? 0 : 1];
            var to = edges[index][denominator > 0 ? 1 : 0];
            var dy = to.y - from.y;
            var dz = to.z - from.z;
            if (!(dz > 0 || (dz === 0 && dy < 0))) { return null; }
        }
        return weights;
    }

    function collectLineCrossings(vertices, origin, cellSizeMm, dimensions) {
        var crossings = new Map();
        for (var triangleIndex = 0; triangleIndex < vertices.length; triangleIndex += 3) {
            var a = vertices[triangleIndex];
            var b = vertices[triangleIndex + 1];
            var c = vertices[triangleIndex + 2];
            var projectedArea = ((b.y - a.y) * (c.z - a.z)) - ((b.z - a.z) * (c.y - a.y));
            if (Math.abs(projectedArea) <= EPSILON) { continue; }
            var winding = projectedArea > 0 ? 1 : -1;
            var minimumY = Math.max(1, Math.floor((Math.min(a.y, b.y, c.y) - origin.y) / cellSizeMm));
            var maximumY = Math.min(dimensions.y - 2, Math.ceil((Math.max(a.y, b.y, c.y) - origin.y) / cellSizeMm));
            var minimumZ = Math.max(1, Math.floor((Math.min(a.z, b.z, c.z) - origin.z) / cellSizeMm));
            var maximumZ = Math.min(dimensions.z - 2, Math.ceil((Math.max(a.z, b.z, c.z) - origin.z) / cellSizeMm));
            for (var zIndex = minimumZ; zIndex <= maximumZ; zIndex++) {
                var z = origin.z + ((zIndex + 0.5) * cellSizeMm);
                for (var yIndex = minimumY; yIndex <= maximumY; yIndex++) {
                    var y = origin.y + ((yIndex + 0.5) * cellSizeMm);
                    var weights = twoDimensionalBarycentric(y, z, a, b, c);
                    if (!weights) { continue; }
                    var x = (a.x * weights[0]) + (b.x * weights[1]) + (c.x * weights[2]);
                    var key = yIndex + (dimensions.y * zIndex);
                    var line = crossings.get(key);
                    if (!line) { line = []; crossings.set(key, line); }
                    line.push({ x: x, winding: winding });
                }
            }
        }
        return crossings;
    }

    function fillOccupancy(crossings, origin, cellSizeMm, dimensions) {
        var totalCells = dimensions.x * dimensions.y * dimensions.z;
        var bits = new Uint32Array(Math.ceil(totalCells / 32));
        var oddCrossingLines = 0;
        crossings.forEach(function (values, key) {
            values.sort(function (left, right) { return left.x - right.x; });
            var unique = [];
            values.forEach(function (value) {
                var previous = unique[unique.length - 1];
                if (!previous || Math.abs(previous.x - value.x) > cellSizeMm * 1e-6) {
                    unique.push({ x: value.x, winding: value.winding });
                } else {
                    previous.winding += value.winding;
                }
            });
            var yIndex = key % dimensions.y;
            var zIndex = Math.floor(key / dimensions.y);
            var winding = 0;
            for (var crossing = 0; crossing < unique.length; crossing++) {
                winding += unique[crossing].winding;
                if (winding === 0 || crossing + 1 >= unique.length) { continue; }
                // Nonzero winding fills the union of overlapping, consistently
                // oriented shells. Oppositely oriented inner shells retain cavities;
                // reversing the complete model changes only the winding's sign.
                var start = Math.max(1, Math.ceil(((unique[crossing].x - origin.x) / cellSizeMm) - 0.5));
                var end = Math.min(dimensions.x - 2, Math.floor(((unique[crossing + 1].x - origin.x) / cellSizeMm) - 0.5));
                for (var xIndex = start; xIndex <= end; xIndex++) {
                    setBit(bits, linearIndex(xIndex, yIndex, zIndex, dimensions));
                }
            }
            // Keep the existing diagnostic field/wire shape; an unbalanced signed
            // crossing line, like an odd parity line, indicates an open boundary.
            if (winding !== 0) { oddCrossingLines++; }
        });
        return { bits: bits, oddCrossingLines: oddCrossingLines };
    }

    function encodeRuns(bits, totalCells) {
        var runs = [];
        var runStart = -1;
        for (var index = 0; index < totalCells; index++) {
            var occupied = hasBit(bits, index);
            if (occupied && runStart < 0) { runStart = index; }
            if (!occupied && runStart >= 0) {
                runs.push([runStart, index - runStart]);
                runStart = -1;
            }
        }
        if (runStart >= 0) { runs.push([runStart, totalCells - runStart]); }
        return runs;
    }

    function fieldChecksum(bits, dimensions, cellSizeMm) {
        var hash = 2166136261;
        function mix(value) {
            hash ^= value >>> 0;
            hash = Math.imul(hash, 16777619) >>> 0;
        }
        mix(dimensions.x); mix(dimensions.y); mix(dimensions.z);
        mix(Math.round(cellSizeMm * 1000000));
        for (var index = 0; index < bits.length; index++) { mix(bits[index]); }
        return ('00000000' + hash.toString(16)).slice(-8);
    }

    function cellCoordinates(index, dimensions) {
        var z = Math.floor(index / (dimensions.x * dimensions.y));
        var remainder = index - (z * dimensions.x * dimensions.y);
        var y = Math.floor(remainder / dimensions.x);
        return [remainder - (y * dimensions.x), y, z];
    }

    function axisDimension(dimensions, axisIndex) {
        return [dimensions.x, dimensions.y, dimensions.z][axisIndex];
    }

    function buildSliceDistance(field, axisIndex) {
        var dimensions = field.dimensions;
        var bits = field._occupancyBits;
        var distances = new Float32Array(dimensions.x * dimensions.y * dimensions.z);
        var uAxis = (axisIndex + 1) % 3;
        var vAxis = (axisIndex + 2) % 3;
        var axisCount = axisDimension(dimensions, axisIndex);
        var uCount = axisDimension(dimensions, uAxis);
        var vCount = axisDimension(dimensions, vAxis);
        var strides = [1, dimensions.x, dimensions.x * dimensions.y];
        var axisStride = strides[axisIndex];
        var uStride = strides[uAxis];
        var vStride = strides[vAxis];
        var costs = new Float64Array(vCount);
        var sites = new Int32Array(vCount);
        var boundaries = new Float64Array(vCount + 1);
        var extrema = {
            uCount: uCount,
            vCount: vCount,
            uMinimum: new Int16Array(axisCount * vCount),
            uMaximum: new Int16Array(axisCount * vCount),
            vMinimum: new Int16Array(axisCount * uCount),
            vMaximum: new Int16Array(axisCount * uCount),
            sliceUMinimum: new Int16Array(axisCount).fill(-1),
            sliceUMaximum: new Int16Array(axisCount).fill(-1),
            sliceVMinimum: new Int16Array(axisCount).fill(-1),
            sliceVMaximum: new Int16Array(axisCount).fill(-1)
        };

        for (var axis = 0; axis < axisCount; axis++) {
            var sliceStart = axis * axisStride;
            // First separable pass: squared distance to the nearest occupied
            // cell in each u-row. Binary seeds need only two linear sweeps.
            for (var v = 0; v < vCount; v++) {
                var rowStart = sliceStart + v * vStride;
                var previous = -Infinity;
                for (var u = 0; u < uCount; u++) {
                    var index = rowStart + u * uStride;
                    if (hasBit(bits, index)) { previous = u; }
                    distances[index] = (u - previous) * (u - previous);
                }
                var next = Infinity;
                for (var reverseU = uCount - 1; reverseU >= 0; reverseU--) {
                    var reverseIndex = rowStart + reverseU * uStride;
                    if (hasBit(bits, reverseIndex)) { next = reverseU; }
                    var candidate = (next - reverseU) * (next - reverseU);
                    if (candidate < distances[reverseIndex]) { distances[reverseIndex] = candidate; }
                }
                var rowId = axis * vCount + v;
                extrema.uMinimum[rowId] = Number.isFinite(next) ? next : -1;
                extrema.uMaximum[rowId] = Number.isFinite(previous) ? previous : -1;
                if (Number.isFinite(next)) {
                    extrema.sliceUMinimum[axis] = extrema.sliceUMinimum[axis] < 0
                        ? next : Math.min(extrema.sliceUMinimum[axis], next);
                    extrema.sliceUMaximum[axis] = Math.max(extrema.sliceUMaximum[axis], previous);
                    if (extrema.sliceVMinimum[axis] < 0) { extrema.sliceVMinimum[axis] = v; }
                    extrema.sliceVMaximum[axis] = v;
                }
            }
            // Second pass: lower envelope of squared-distance parabolas along
            // v. Each site is inserted and removed at most once, so the exact
            // Euclidean transform stays O(cells), independent of cutter radius.
            for (var column = 0; column < uCount; column++) {
                var columnStart = sliceStart + column * uStride;
                var lastSite = -1;
                var minimumV = -1;
                var maximumV = -1;
                for (var q = 0; q < vCount; q++) {
                    var cost = distances[columnStart + q * vStride];
                    costs[q] = cost;
                    if (cost === 0) {
                        if (minimumV < 0) { minimumV = q; }
                        maximumV = q;
                    }
                    if (!Number.isFinite(cost)) { continue; }
                    var intersection = -Infinity;
                    while (lastSite >= 0) {
                        var site = sites[lastSite];
                        intersection = ((cost + q * q) - (costs[site] + site * site)) / (2 * (q - site));
                        if (intersection > boundaries[lastSite]) { break; }
                        lastSite--;
                    }
                    lastSite++;
                    sites[lastSite] = q;
                    boundaries[lastSite] = lastSite === 0 ? -Infinity : intersection;
                    boundaries[lastSite + 1] = Infinity;
                }
                var columnId = axis * uCount + column;
                extrema.vMinimum[columnId] = minimumV;
                extrema.vMaximum[columnId] = maximumV;
                var activeSite = 0;
                for (var target = 0; target < vCount; target++) {
                    var outputIndex = columnStart + target * vStride;
                    if (lastSite < 0) {
                        distances[outputIndex] = Infinity;
                        continue;
                    }
                    while (activeSite < lastSite && boundaries[activeSite + 1] < target) { activeSite++; }
                    var delta = target - sites[activeSite];
                    // Retain the caller's historical units: three units per cell.
                    distances[outputIndex] = 3 * Math.sqrt(delta * delta + costs[sites[activeSite]]);
                }
            }
        }
        distances._outsideExtrema = extrema;
        return distances;
    }

    function outsideSliceDistanceSquared(distanceField, slice, u, v) {
        var extrema = distanceField._outsideExtrema;
        if (!extrema) { return null; }
        var alongU = u < 0 || u >= extrema.uCount;
        if (!alongU && v >= 0 && v < extrema.vCount) { return null; }
        var cache = distanceField._outsideDistanceCache;
        if (!cache) {
            cache = { lines: new Map(), lastKey: null, lastValues: null };
            distanceField._outsideDistanceCache = cache;
        }
        var key = u + ',' + v;
        var values;
        if (cache.lastKey === key) {
            values = cache.lastValues;
        } else {
            values = cache.lines.get(key);
            if (values) {
                cache.lines.delete(key);
            } else {
                if (cache.lines.size >= 1024) { cache.lines.delete(cache.lines.keys().next().value); }
                values = new Float64Array(extrema.uMinimum.length / extrema.vCount);
                values.fill(NaN);
            }
            cache.lines.set(key, values);
            cache.lastKey = key;
            cache.lastValues = values;
        }
        if (!Number.isNaN(values[slice])) { return values[slice]; }
        var rows = alongU ? extrema.vCount : extrema.uCount;
        var entries = alongU
            ? (u < 0 ? extrema.uMinimum : extrema.uMaximum)
            : (v < 0 ? extrema.vMinimum : extrema.vMaximum);
        var outside = alongU ? u : v;
        var across = alongU ? v : u;
        var start = slice * rows;
        var minimum = Infinity;
        var sliceExtreme = alongU
            ? (u < 0 ? extrema.sliceUMinimum : extrema.sliceUMaximum)
            : (v < 0 ? extrema.sliceVMinimum : extrema.sliceVMaximum);
        if (sliceExtreme && sliceExtreme[slice] < 0) {
            values[slice] = Infinity;
            return Infinity;
        }
        // Every occupied row is at least this far from an outside coordinate.
        // Keeping this radial term avoids scanning hundreds of rows when the
        // query is far outside a flat boundary. Missing optional metadata keeps
        // the original nonnegative-radial bound for custom distance fields.
        var minimumRadialSquared = sliceExtreme ? Math.pow(outside - sliceExtreme[slice], 2) : 0;
        // Beyond a radial boundary the closest occupied cell in each row is
        // its extreme towards the query. Taking the minimum over rows is exact,
        // including corner queries outside both radial bounds, without padding.
        var left = Math.min(rows - 1, Math.floor(across));
        var right = Math.max(0, Math.floor(across) + 1);
        while (left >= 0 || right < rows) {
            var leftDistance = left >= 0 ? across - left : Infinity;
            var rightDistance = right < rows ? right - across : Infinity;
            var takeLeft = leftDistance <= rightDistance;
            var transverse = takeLeft ? leftDistance : rightDistance;
            // Visit the nearest rows first. Once their transverse distance plus
            // the slice's unavoidable radial bound reaches the current minimum,
            // no remaining row can improve it. The cached value remains exact.
            if (transverse * transverse + minimumRadialSquared >= minimum) { break; }
            var row = takeLeft ? left-- : right++;
            var extreme = entries[start + row];
            if (extreme < 0) { continue; }
            var radial = outside - extreme;
            minimum = Math.min(minimum, radial * radial + transverse * transverse);
        }
        values[slice] = minimum * 9;
        return values[slice];
    }

    function toolCentrePathClear(field, distanceField, coordinates, axisIndex, sign, tool) {
        var dimensions = field.dimensions;
        var rounded = coordinates.map(function (value) { return Math.round(value); });
        rounded[axisIndex] += sign;
        var axisCount = axisDimension(dimensions, axisIndex);
        var firstSlice = rounded[axisIndex];
        if (firstSlice < 0 || firstSlice >= axisCount) { return true; }
        var queryU = rounded[(axisIndex + 1) % 3];
        var queryV = rounded[(axisIndex + 2) % 3];
        // Radial coordinates do not change along this approach ray. Resolve its
        // boundary extension once, not at every axial cell and candidate centre.
        var outsideSquared = 0;
        for (var localAxis = 0; localAxis < 3; localAxis++) {
            if (localAxis === axisIndex) { continue; }
            var bounded = Math.max(0, Math.min(axisDimension(dimensions, localAxis) - 1, rounded[localAxis]));
            outsideSquared += Math.pow(rounded[localAxis] - bounded, 2);
            rounded[localAxis] = bounded;
        }
        var axisStride = [1, dimensions.x, dimensions.x * dimensions.y][axisIndex];
        var baseIndex = linearIndex(rounded[0], rounded[1], rounded[2], dimensions) - firstSlice * axisStride;
        var requiredSquared = [tool.diameterMm, tool.shankDiameterMm, tool.holderDiameterMm]
            .map(function (diameter) {
                var required = Math.max(1, (diameter * 0.5 / field.cellSizeMm) * 3);
                return required * required;
            });
        var outsideClearanceSquared = outsideSquared * 9;
        function blockedAt(slice, required) {
            // A centre outside a radial grid boundary can still have its cutter
            // overlapping the part (or a roof). Only axial exit proves clearance.
            var distance = distanceField[baseIndex + slice * axisStride];
            var clearanceSquared = distance * distance + outsideClearanceSquared;
            if (clearanceSquared <= required) {
                // The clamped extension is only a lower bound: it omits the
                // cross term between the outside offset and occupied cells set
                // back from the boundary. Fast-accept it, but verify a rejection
                // with the exact slice distance before inventing a small tool.
                var exactSquared = outsideSquared > 0
                    ? outsideSliceDistanceSquared(distanceField, slice, queryU, queryV) : null;
                return exactSquared === null || exactSquared <= required;
            }
            return false;
        }
        var cache = distanceField._axialBlockedCache;
        if (!cache) { cache = distanceField._axialBlockedCache = new Map(); }
        function rangeClear(from, to, required) {
            if (from >= to) { return true; }
            var key = axisIndex + ':' + queryU + ',' + queryV + ':' + required;
            var entry = cache.get(key);
            if (entry) { cache.delete(key); }
            else {
                if (cache.size >= 4096) { cache.delete(cache.keys().next().value); }
                entry = { scanned: 0, prefix: null };
            }
            cache.set(key, entry);
            // Do not expand a cheap collision into a full-axis scan. Only a
            // previously expensive range promotes this radial line to a prefix.
            if (!entry.prefix && entry.scanned >= 16) {
                var prefix = new Uint16Array(axisCount + 1);
                for (var slice = 0; slice < axisCount; slice++) {
                    prefix[slice + 1] = prefix[slice] + (blockedAt(slice, required) ? 1 : 0);
                }
                entry.prefix = prefix;
            }
            var first = firstSlice + sign * from;
            var last = firstSlice + sign * (to - 1);
            if (entry.prefix) {
                return entry.prefix[Math.max(first, last) + 1] === entry.prefix[Math.min(first, last)];
            }
            var scanned = 0;
            for (var step = from; step < to; step++) {
                scanned++;
                if (blockedAt(firstSlice + sign * step, required)) {
                    entry.scanned = Math.max(entry.scanned, scanned);
                    return false;
                }
            }
            entry.scanned = Math.max(entry.scanned, scanned);
            return true;
        }
        var available = sign > 0 ? axisCount - firstSlice : firstSlice + 1;
        function stepsWithin(length) {
            var limit = length + EPSILON;
            if (!(limit >= 0)) { return 0; }
            var steps = Math.min(available, Math.max(0, Math.floor(limit / field.cellSizeMm)));
            // Preserve the scalar multiplication predicate at floating-point
            // section boundaries rather than relying on division rounding.
            while (steps > 0 && steps * field.cellSizeMm > limit) { steps--; }
            while (steps < available && (steps + 1) * field.cellSizeMm <= limit) { steps++; }
            return steps;
        }
        var cutterEnd = stepsWithin(tool.usableCutLengthMm);
        var shankEnd = Math.max(cutterEnd, stepsWithin(tool.reachMm));
        var ends = [cutterEnd, shankEnd, available];
        var from = 0;
        var pendingFrom = 0;
        var pendingEnd = 0;
        var pendingRequired = null;
        for (var section = 0; section < 3; section++) {
            var end = ends[section];
            if (end > from) {
                // Equal adjoining cutter/shank/holder envelopes are one range,
                // including across a zero-length section. A cold ray must not
                // promote its own second section into a full-axis prefix scan.
                if (pendingRequired !== null && pendingRequired !== requiredSquared[section]) {
                    if (!rangeClear(pendingFrom, pendingEnd, pendingRequired)) { return false; }
                    pendingFrom = from;
                }
                pendingRequired = requiredSquared[section];
                pendingEnd = end;
            }
            from = end;
        }
        return rangeClear(pendingFrom, pendingEnd, pendingRequired);
    }

    function toolPathClear(field, distanceField, sample, axisIndex, sign, tool, contactMode, pathCache) {
        function clearAt(centre) {
            var rounded = centre.map(function (value) { return Math.round(value); });
            var key = rounded[0] >= 0 && rounded[0] < field.dimensions.x
                && rounded[1] >= 0 && rounded[1] < field.dimensions.y
                && rounded[2] >= 0 && rounded[2] < field.dimensions.z
                ? linearIndex(rounded[0], rounded[1], rounded[2], field.dimensions) : null;
            var cache = pathCache;
            var outside = key === null;
            if (cache && outside) {
                if (!pathCache._outside) { pathCache._outside = new Map(); }
                cache = pathCache._outside;
                key = rounded.join(',');
            }
            if (cache && cache.has(key)) {
                var cached = cache.get(key);
                if (outside) { cache.delete(key); cache.set(key, cached); }
                return cached;
            }
            var clear = toolCentrePathClear(field, distanceField, centre, axisIndex, sign, tool);
            if (cache) {
                if (outside && cache.size >= 4096) { cache.delete(cache.keys().next().value); }
                cache.set(key, clear);
            }
            return clear;
        }
        var coordinates = cellCoordinates(sample.id, field.dimensions);
        if (sample.contactPosition && field.origin) {
            var position = framePoint(sample.contactPosition, field.axes);
            var origin = field.frameOrigin || framePoint(field.origin, field.axes);
            coordinates = ['x', 'y', 'z'].map(function (axis) {
                return (position[axis] - origin[axis]) / field.cellSizeMm - 0.5;
            });
        }
        var radiusCells = tool.diameterMm * 0.5 / field.cellSizeMm;
        if (contactMode === 'flute') {
            for (var localAxis = 0; localAxis < 3; localAxis++) {
                if (localAxis === axisIndex) { continue; }
                coordinates[localAxis] += dot(sample.normal, field.axes[localAxis]) * (radiusCells + 1.25);
            }
        }
        if (clearAt(coordinates)) { return true; }
        if (field._refineToolContact
            && field._refineToolContact(sample, scale(field.axes[axisIndex], sign), tool)) { return true; }
        // A flat cutting disk can contact a floor away from its centre. Testing
        // only the sample-centred envelope invents small-tool cleanup rings at
        // every pocket wall. Search centres that still cover this floor sample;
        // every candidate must clear the entire cutter, shank and holder path.
        // Drills and ball tips do not have a flat contact disk, nor do tilted faces.
        if (contactMode !== 'tip' || tool.family !== 'flat_end_mill'
            || dot(sample.normal, scale(field.axes[axisIndex], sign)) < 0.99) { return false; }
        var uAxis = (axisIndex + 1) % 3;
        var vAxis = (axisIndex + 2) % 3;
        var extent = Math.floor(radiusCells);
        var radiusSquared = radiusCells * radiusCells;
        var first = coordinates.map(function (value) { return Math.round(value); });
        first[axisIndex] += sign;
        var required = Math.max(1, radiusCells * 3);
        var strides = [1, field.dimensions.x, field.dimensions.x * field.dimensions.y];
        var uCount = axisDimension(field.dimensions, uAxis);
        var vCount = axisDimension(field.dimensions, vAxis);
        var firstIndex = linearIndex(first[0], first[1], first[2], field.dimensions);
        var firstSliceInside = first[axisIndex] >= 0 && first[axisIndex] < axisDimension(field.dimensions, axisIndex);
        var firstDiameter = field.cellSizeMm <= tool.usableCutLengthMm + EPSILON ? tool.diameterMm
            : (field.cellSizeMm <= tool.reachMm + EPSILON ? tool.shankDiameterMm : tool.holderDiameterMm);
        var firstRequired = Math.max(1, (firstDiameter * 0.5 / field.cellSizeMm) * 3);
        var firstRequiredSquared = firstRequired * firstRequired;
        function diskCandidateClear(u, v) {
            if (u * u + v * v > radiusSquared || (u === 0 && v === 0)) { return false; }
            var queryU = first[uAxis] + u;
            var queryV = first[vAxis] + v;
            var radialInside = queryU >= 0 && queryU < uCount && queryV >= 0 && queryV < vCount;
            if (radialInside && firstSliceInside
                && distanceField[firstIndex + u * strides[uAxis] + v * strides[vAxis]] <= required) { return false; }
            if (!radialInside && firstSliceInside) {
                // Apply the same first-slice rejection as the complete ray
                // before allocating candidate centres and cache entries.
                // Axial exit still bypasses this test; radial exit never does.
                var boundedU = Math.max(0, Math.min(uCount - 1, queryU));
                var boundedV = Math.max(0, Math.min(vCount - 1, queryV));
                var boundaryIndex = firstIndex + (boundedU - first[uAxis]) * strides[uAxis]
                    + (boundedV - first[vAxis]) * strides[vAxis];
                var boundaryDistance = distanceField[boundaryIndex];
                var outsideSquared = (queryU - boundedU) * (queryU - boundedU)
                    + (queryV - boundedV) * (queryV - boundedV);
                if (boundaryDistance * boundaryDistance + outsideSquared * 9 <= firstRequiredSquared) {
                    var exactSquared = outsideSliceDistanceSquared(distanceField, first[axisIndex], queryU, queryV);
                    if (exactSquared === null || exactSquared <= firstRequiredSquared) { return false; }
                }
            }
            var centre = coordinates.slice();
            centre[uAxis] += u;
            centre[vAxis] += v;
            return clearAt(centre);
        }
        // Try widely separated valid centres before the complete raster search.
        // A positive radial escape should not require visiting thousands of
        // blocked negative offsets. Every candidate uses the identical full
        // cutter/shank/holder gate, and the complete fallback remains unchanged.
        var diagonal = Math.floor(radiusCells / Math.sqrt(2));
        var earlyOffsets = [[extent, 0], [-extent, 0], [0, extent], [0, -extent],
            [diagonal, diagonal], [diagonal, -diagonal], [-diagonal, diagonal], [-diagonal, -diagonal]];
        for (var candidate = 0; candidate < earlyOffsets.length; candidate++) {
            if (diskCandidateClear(earlyOffsets[candidate][0], earlyOffsets[candidate][1])) { return true; }
        }
        function diskHasBlockingSeed() {
            var bits = field._occupancyBits;
            if (!bits || bits.length < Math.ceil(field.dimensions.x * field.dimensions.y * field.dimensions.z / 32)
                || !firstSliceInside || first[uAxis] < 0 || first[uAxis] >= uCount
                || first[vAxis] < 0 || first[vAxis] >= vCount) { return false; }
            // Near a half-cell, adding an integer offset can round differently
            // because of floating-point addition. Keep the exhaustive fallback.
            if ([uAxis, vAxis].some(function (axis) {
                return Math.abs(coordinates[axis] - Math.floor(coordinates[axis]) - 0.5) <= 1e-8;
            })) { return false; }
            // Every searched integer offset lies within this squared radius.
            // Inflate it to cover Float32 EDT rounding, including the mixed
            // clamped-distance plus exact outside-offset fast-accept predicate.
            // Near-equality deliberately cannot prove impossibility.
            var inflation = 1 + Math.pow(2, -23);
            var maximumClearanceSquared = 9 * Math.floor(radiusSquared) * inflation * inflation;
            if (!Number.isFinite(maximumClearanceSquared)) { return false; }
            var sectionRadiiSquared = [tool.diameterMm, tool.shankDiameterMm, tool.holderDiameterMm].map(function (diameter) {
                var radius = Math.max(1, (diameter * 0.5 / field.cellSizeMm) * 3);
                return radius * radius;
            });
            var slice = first[axisIndex];
            var index = firstIndex;
            var step = 0;
            var axisCount = axisDimension(field.dimensions, axisIndex);
            while (slice >= 0 && slice < axisCount) {
                var depth = (step + 1) * field.cellSizeMm;
                var section = depth <= tool.usableCutLengthMm + EPSILON ? 0
                    : (depth <= tool.reachMm + EPSILON ? 1 : 2);
                // This seed is on the original rounded radial centre. If the
                // active tool section covers every possible offset to it, no
                // disk candidate can clear this slice, including outside ones.
                if (sectionRadiiSquared[section] >= maximumClearanceSquared && hasBit(bits, index)) { return true; }
                slice += sign;
                index += sign * strides[axisIndex];
                step++;
            }
            return false;
        }
        if (diskHasBlockingSeed()) { return false; }
        var exactRegionEvidence = distanceField._outsideExtrema && field._occupancyBits
            && field._occupancyBits.length >= Math.ceil(field.dimensions.x * field.dimensions.y * field.dimensions.z / 32)
            && firstSliceInside && Number.isFinite(extent)
            && [uAxis, vAxis].every(function (axis) {
                return Math.abs(coordinates[axis] - Math.floor(coordinates[axis]) - 0.5) > 1e-8;
            });
        if (exactRegionEvidence) {
            var regionInflation = 1 + Math.pow(2, -23);
            var regionRequired = [tool.diameterMm, tool.shankDiameterMm, tool.holderDiameterMm].map(function (diameter) {
                return Math.max(1, (diameter * 0.5 / field.cellSizeMm) * 3);
            });
            function regionBlocked(minU, maxU, minV, maxV, representativeU, representativeV) {
                var spanU = Math.max(representativeU - minU, maxU - representativeU);
                var spanV = Math.max(representativeV - minV, maxV - representativeV);
                var cornerDistance = 3 * Math.sqrt(spanU * spanU + spanV * spanV);
                var queryU = first[uAxis] + representativeU;
                var queryV = first[vAxis] + representativeV;
                var inside = queryU >= 0 && queryU < uCount && queryV >= 0 && queryV < vCount;
                var index = firstIndex + representativeU * strides[uAxis] + representativeV * strides[vAxis];
                var slice = first[axisIndex];
                var axisCount = axisDimension(field.dimensions, axisIndex);
                var step = 0;
                while (slice >= 0 && slice < axisCount) {
                    var depth = (step + 1) * field.cellSizeMm;
                    var section = depth <= tool.usableCutLengthMm + EPSILON ? 0
                        : (depth <= tool.reachMm + EPSILON ? 1 : 2);
                    // Exact nearest-distance is 1-Lipschitz. Its value at this
                    // representative plus the farthest corner distance bounds
                    // every candidate in the rectangle. Never substitute the
                    // clamped outside lower bound for this required upper bound.
                    var outsideDistance = inside ? null : outsideSliceDistanceSquared(distanceField, slice, queryU, queryV);
                    var nearestUpper = inside ? distanceField[index] * regionInflation
                        : (outsideDistance === null ? Infinity : Math.sqrt(outsideDistance));
                    // A second inflation covers candidate Float32 storage and
                    // the mixed clamped/outside predicate in the actual ray gate.
                    if ((nearestUpper + cornerDistance) * regionInflation <= regionRequired[section]) { return true; }
                    slice += sign;
                    index += sign * strides[axisIndex];
                    step++;
                }
                return false;
            }
            var regions = [[-extent, extent, -extent, extent]];
            while (regions.length > 0) {
                var region = regions.pop();
                var minU = region[0], maxU = region[1], minV = region[2], maxV = region[3];
                var nearestU = Math.max(minU, 0, -maxU);
                var nearestV = Math.max(minV, 0, -maxV);
                if (nearestU * nearestU + nearestV * nearestV > radiusSquared) { continue; }
                var middleU = Math.floor((minU + maxU) / 2);
                var middleV = Math.floor((minV + maxV) / 2);
                if (diskCandidateClear(middleU, middleV)) { return true; }
                if (minU === maxU && minV === maxV) { continue; }
                if (regionBlocked(minU, maxU, minV, maxV, middleU, middleV)) { continue; }
                // Subdivision retains every integer candidate exactly. Regions
                // without an obstruction proof eventually reach single cells,
                // which still use the unchanged complete tool-envelope gate.
                if (maxU - minU >= maxV - minV) {
                    regions.push([minU, middleU, minV, maxV], [middleU + 1, maxU, minV, maxV]);
                } else {
                    regions.push([minU, maxU, minV, middleV], [minU, maxU, middleV + 1, maxV]);
                }
            }
            return false;
        }
        for (var u = -extent; u <= extent; u++) {
            for (var v = -extent; v <= extent; v++) {
                if (diskCandidateClear(u, v)) { return true; }
            }
        }
        return false;
    }

    function supportsTipContact(tool) {
        return ['face_mill', 'flat_end_mill', 'ball_end_mill', 'chamfer_mill', 'drill', 'tap', 'thread_mill']
            .indexOf(tool.family) >= 0;
    }

    function supportsFluteContact(tool) {
        return ['flat_end_mill', 'ball_end_mill', 'tap', 'thread_mill'].indexOf(tool.family) >= 0;
    }

    function classifyToolAccess(field, tools) {
        if (!field || !Array.isArray(tools)) { return {}; }
        var result = {};
        var labels = ['x', 'y', 'z'];
        for (var axisIndex = 0; axisIndex < 3; axisIndex++) {
            var distanceField = buildSliceDistance(field, axisIndex);
            [1, -1].forEach(function (sign) {
                var directionId = (sign > 0 ? 'positive-' : 'negative-') + labels[axisIndex];
                var direction = scale(field.axes[axisIndex], sign);
                result[directionId] = {};
                tools.forEach(function (tool) {
                    var pathCache = new Map();
                    var reachable = [];
                    var tip = [];
                    var flute = [];
                    var blocked = [];
                    var reachableAreaMm2 = 0;
                    field.surfaceSamples.forEach(function (sample) {
                        var alignment = dot(sample.normal, direction);
                        var mode = null;
                        if (supportsTipContact(tool) && alignment >= 0.35) { mode = 'tip'; }
                        else if (supportsFluteContact(tool) && Math.abs(alignment) <= 0.35) { mode = 'flute'; }
                        var clear = mode && toolPathClear(field, distanceField, sample, axisIndex, sign, tool, mode, pathCache);
                        if (clear) {
                            reachable.push(sample.id);
                            reachableAreaMm2 += sample.areaMm2;
                            if (mode === 'tip') { tip.push(sample.id); }
                            else { flute.push(sample.id); }
                        } else {
                            blocked.push(sample.id);
                        }
                    });
                    result[directionId][tool.id] = {
                        reachableSampleIds: reachable,
                        tipSampleIds: tip,
                        fluteSampleIds: flute,
                        blockedSampleIds: blocked,
                        reachableAreaMm2: reachableAreaMm2
                    };
                });
            });
        }
        field.toolAccess = result;
        return result;
    }

    function exteriorAir(field) {
        var dimensions = field.dimensions;
        var planeSize = dimensions.x * dimensions.y;
        var totalCells = planeSize * dimensions.z;
        var occupied = field._occupancyBits;
        var exterior = new Uint32Array(occupied.length);
        // The padded corner is outside every source solid. Six-connected flood
        // fill preserves cavities without inventing an opening across a diagonal.
        var queue = new Uint32Array(totalCells);
        var head = 0;
        var tail = 1;
        queue[0] = 0;
        setBit(exterior, 0);
        function visit(index) {
            if (hasBit(occupied, index) || hasBit(exterior, index)) { return; }
            setBit(exterior, index);
            queue[tail++] = index;
        }
        while (head < tail) {
            var index = queue[head++];
            var x = index % dimensions.x;
            var y = Math.floor(index / dimensions.x) % dimensions.y;
            if (x > 0) { visit(index - 1); }
            if (x + 1 < dimensions.x) { visit(index + 1); }
            if (y > 0) { visit(index - dimensions.x); }
            if (y + 1 < dimensions.y) { visit(index + dimensions.x); }
            if (index >= planeSize) { visit(index - planeSize); }
            if (index + planeSize < totalCells) { visit(index + planeSize); }
        }
        var occupiedCount = 0;
        for (var word = 0; word < occupied.length; word++) {
            var bits = occupied[word];
            bits -= (bits >>> 1) & 0x55555555;
            bits = (bits & 0x33333333) + ((bits >>> 2) & 0x33333333);
            occupiedCount += (((bits + (bits >>> 4)) & 0x0f0f0f0f) * 0x01010101) >>> 24;
        }
        field.enclosedAirCellCount = totalCells - occupiedCount - tail;
        field.enclosedAirVolumeMm3 = field.enclosedAirCellCount * Math.pow(field.cellSizeMm, 3);
        field._exteriorAirBits = exterior;
        return exterior;
    }

    function isExteriorAir(field, point) {
        if (!field || !point || !Number.isFinite(point.x) || !Number.isFinite(point.y) || !Number.isFinite(point.z)) {
            return false;
        }
        var local = framePoint(point, field.axes);
        var x = Math.floor((local.x - field.frameOrigin.x) / field.cellSizeMm);
        var y = Math.floor((local.y - field.frameOrigin.y) / field.cellSizeMm);
        var z = Math.floor((local.z - field.frameOrigin.z) / field.cellSizeMm);
        var dimensions = field.dimensions;
        if (x < 0 || y < 0 || z < 0 || x >= dimensions.x || y >= dimensions.y || z >= dimensions.z) { return true; }
        return hasBit(field._exteriorAirBits || exteriorAir(field), linearIndex(x, y, z, dimensions));
    }

    function surfaceSamples(field) {
        var dimensions = field.dimensions;
        var axes = field.axes;
        var cellSizeMm = field.cellSizeMm;
        var bits = field._occupancyBits;
        var exterior = field._exteriorAirBits || exteriorAir(field);
        var directions = [
            [-1, 0, 0, axes[0], -1], [1, 0, 0, axes[0], 1],
            [0, -1, 0, axes[1], -1], [0, 1, 0, axes[1], 1],
            [0, 0, -1, axes[2], -1], [0, 0, 1, axes[2], 1]
        ];
        var boundaryIndexes = [];
        for (var z = 1; z < dimensions.z - 1; z++) {
            for (var y = 1; y < dimensions.y - 1; y++) {
                for (var x = 1; x < dimensions.x - 1; x++) {
                    var index = linearIndex(x, y, z, dimensions);
                    if (!hasBit(bits, index)) { continue; }
                    var boundary = directions.some(function (direction) {
                        return hasBit(exterior, linearIndex(x + direction[0], y + direction[1], z + direction[2], dimensions));
                    });
                    if (boundary) { boundaryIndexes.push(index); }
                }
            }
        }

        var stride = Math.max(1, Math.ceil(boundaryIndexes.length / MAX_RESPONSE_SURFACE_SAMPLES));
        var samples = [];
        for (var sourceIndex = 0; sourceIndex < boundaryIndexes.length; sourceIndex += stride) {
            var cellIndex = boundaryIndexes[sourceIndex];
            var zIndex = Math.floor(cellIndex / (dimensions.x * dimensions.y));
            var remainder = cellIndex - (zIndex * dimensions.x * dimensions.y);
            var yIndex = Math.floor(remainder / dimensions.x);
            var xIndex = remainder - (yIndex * dimensions.x);
            var normal = vector(0, 0, 0);
            var exposedFaces = 0;
            directions.forEach(function (direction) {
                if (hasBit(exterior, linearIndex(xIndex + direction[0], yIndex + direction[1], zIndex + direction[2], dimensions))) {
                    normal = add(normal, scale(direction[3], direction[4]));
                    exposedFaces++;
                }
            });
            var localPosition = vector(
                field.frameOrigin.x + ((xIndex + 0.5) * cellSizeMm),
                field.frameOrigin.y + ((yIndex + 0.5) * cellSizeMm),
                field.frameOrigin.z + ((zIndex + 0.5) * cellSizeMm));
            samples.push({
                id: cellIndex,
                position: worldPoint(localPosition, axes),
                normal: normalize(normal),
                areaMm2: exposedFaces * cellSizeMm * cellSizeMm * stride
            });
        }
        field.surfaceSampleStride = stride;
        field.surfaceCellCount = boundaryIndexes.length;
        return samples;
    }

    function build(triangles, frame, options) {
        var axes = frame && Array.isArray(frame.axes) ? frame.axes : frame;
        if (!Array.isArray(axes) || axes.length !== 3 || !triangles || triangles.length < 9) {
            return null;
        }
        var vertices = transformedTriangles(triangles, axes);
        var bounds = fieldBounds(vertices);
        var resolution = chooseResolution(bounds, options);
        var cellSizeMm = resolution.cellSizeMm;
        var dimensions = dimensionsFor(bounds, cellSizeMm);
        var origin = vector(
            bounds.minimum.x - cellSizeMm,
            bounds.minimum.y - cellSizeMm,
            bounds.minimum.z - cellSizeMm);
        var crossings = collectLineCrossings(vertices, origin, cellSizeMm, dimensions);
        var occupancy = fillOccupancy(crossings, origin, cellSizeMm, dimensions);
        var totalCells = dimensions.x * dimensions.y * dimensions.z;
        var field = {
            version: FIELD_VERSION,
            axes: axes,
            frameOrigin: origin,
            origin: worldPoint(origin, axes),
            cellSizeMm: cellSizeMm,
            requestedCellSizeMm: resolution.requestedCellSizeMm,
            dimensions: dimensions,
            resolutionLimited: resolution.resolutionLimited,
            degraded: occupancy.oddCrossingLines > Math.max(4, crossings.size * 0.01),
            oddCrossingLines: occupancy.oddCrossingLines,
            _occupancyBits: occupancy.bits
        };
        field.occupancyRuns = encodeRuns(occupancy.bits, totalCells);
        field.checksum = fieldChecksum(occupancy.bits, dimensions, cellSizeMm);
        field.surfaceSamples = surfaceSamples(field);
        return field;
    }

    function serialize(field) {
        if (!field) { return null; }
        return {
            version: field.version,
            origin: field.origin,
            axes: field.axes,
            cellSizeMm: field.cellSizeMm,
            requestedCellSizeMm: field.requestedCellSizeMm,
            dimensions: field.dimensions,
            surfaceSamples: field.surfaceSamples,
            surfaceSampleStride: field.surfaceSampleStride,
            surfaceCellCount: field.surfaceCellCount,
            occupancyRuns: field.occupancyRuns,
            toolAccess: field.toolAccess || {},
            checksum: field.checksum,
            resolutionLimited: field.resolutionLimited,
            degraded: field.degraded,
            oddCrossingLines: field.oddCrossingLines,
            enclosedAirCellCount: field.enclosedAirCellCount,
            enclosedAirVolumeMm3: field.enclosedAirVolumeMm3
        };
    }

    function occupancyState(source) {
        source = source || {};
        var dimensions = source.dimensions || {};
        var origin = source.origin || {};
        var totalCells = Number(dimensions.x) * Number(dimensions.y) * Number(dimensions.z);
        if (source.contract !== 'CncSparseOccupancy.v1' || source.version !== FIELD_VERSION
            || !Number.isSafeInteger(dimensions.x) || dimensions.x <= 0
            || !Number.isSafeInteger(dimensions.y) || dimensions.y <= 0
            || !Number.isSafeInteger(dimensions.z) || dimensions.z <= 0
            || !Number.isSafeInteger(totalCells) || totalCells <= 0 || totalCells > MAX_FIELD_CELLS
            || !Number.isFinite(source.resolutionMm) || source.resolutionMm <= 0
            || !Number.isFinite(source.toleranceMm) || source.toleranceMm < 0
            || !Number.isFinite(origin.x) || !Number.isFinite(origin.y) || !Number.isFinite(origin.z)
            || !Array.isArray(source.occupiedRuns)) {
            return null;
        }
        var bits = new Uint32Array(Math.ceil(totalCells / 32));
        var previousEnd = 0;
        for (var index = 0; index < source.occupiedRuns.length; index++) {
            var run = source.occupiedRuns[index];
            if (!Array.isArray(run) || run.length !== 2 || !Number.isSafeInteger(run[0])
                || !Number.isSafeInteger(run[1]) || run[0] < previousEnd || run[1] <= 0
                || run[0] + run[1] > totalCells) {
                return null;
            }
            for (var cell = run[0]; cell < run[0] + run[1]; cell++) { setBit(bits, cell); }
            previousEnd = run[0] + run[1];
        }
        return { contract: source.contract, version: source.version,
            resolutionMm: source.resolutionMm, toleranceMm: source.toleranceMm,
            origin: { x: origin.x, y: origin.y, z: origin.z },
            dimensions: { x: dimensions.x, y: dimensions.y, z: dimensions.z },
            totalCells: totalCells, bits: bits };
    }

    function removeOccupancyRuns(state, runs) {
        if (!state || !state.bits || !Array.isArray(runs)) { return null; }
        var bits = new Uint32Array(state.bits);
        var previousEnd = 0;
        for (var index = 0; index < runs.length; index++) {
            var run = runs[index];
            if (!Array.isArray(run) || run.length !== 2 || !Number.isSafeInteger(run[0])
                || !Number.isSafeInteger(run[1]) || run[0] < previousEnd || run[1] <= 0
                || run[0] + run[1] > state.totalCells) {
                return null;
            }
            for (var cell = run[0]; cell < run[0] + run[1]; cell++) {
                if (!hasBit(bits, cell)) { return null; }
                bits[cell >>> 5] &= ~(1 << (cell & 31));
            }
            previousEnd = run[0] + run[1];
        }
        return Object.assign({}, state, { bits: bits });
    }

    function validOccupancyState(state) {
        if (!state || !state.bits || !state.origin || !state.dimensions
            || !Number.isFinite(state.origin.x) || !Number.isFinite(state.origin.y)
            || !Number.isFinite(state.origin.z) || !Number.isSafeInteger(state.dimensions.x)
            || state.dimensions.x <= 0 || !Number.isSafeInteger(state.dimensions.y)
            || state.dimensions.y <= 0 || !Number.isSafeInteger(state.dimensions.z)
            || state.dimensions.z <= 0 || !Number.isFinite(state.resolutionMm)
            || state.resolutionMm <= 0 || !Number.isFinite(state.toleranceMm)
            || state.toleranceMm < 0) { return false; }
        var totalCells = state.dimensions.x * state.dimensions.y * state.dimensions.z;
        return Number.isSafeInteger(totalCells) && totalCells === state.totalCells
            && state.bits.length === Math.ceil(totalCells / 32);
    }

    function occupancyChecksum(state) {
        if (!validOccupancyState(state)) { return null; }
        var hash = 2166136261;
        var buffer = new ArrayBuffer(8);
        var view = new DataView(buffer);
        function mix(value) {
            hash ^= value >>> 0;
            hash = Math.imul(hash, 16777619) >>> 0;
        }
        function mixNumber(value) {
            view.setFloat64(0, value, true);
            mix(view.getUint32(0, true));
            mix(view.getUint32(4, true));
        }
        mix(state.version);
        mixNumber(state.resolutionMm); mixNumber(state.toleranceMm);
        mixNumber(state.origin.x); mixNumber(state.origin.y); mixNumber(state.origin.z);
        mix(state.dimensions.x); mix(state.dimensions.y); mix(state.dimensions.z);
        for (var index = 0; index < state.bits.length; index++) { mix(state.bits[index]); }
        return ('00000000' + hash.toString(16)).slice(-8);
    }

    function equalOccupancy(left, right) {
        if (!validOccupancyState(left) || !validOccupancyState(right)
            || left.version !== right.version
            || left.resolutionMm !== right.resolutionMm || left.toleranceMm !== right.toleranceMm
            || !left.origin || !right.origin || left.origin.x !== right.origin.x
            || left.origin.y !== right.origin.y || left.origin.z !== right.origin.z
            || !left.dimensions || !right.dimensions || left.dimensions.x !== right.dimensions.x
            || left.dimensions.y !== right.dimensions.y || left.dimensions.z !== right.dimensions.z
            || left.totalCells !== right.totalCells || left.bits.length !== right.bits.length) { return false; }
        for (var index = 0; index < left.bits.length; index++) {
            if (left.bits[index] !== right.bits[index]) { return false; }
        }
        return true;
    }

    root.CncSpatialField = Object.freeze({
        build: build,
        isExteriorAir: isExteriorAir,
        surfaceSamples: surfaceSamples,
        encodeRuns: encodeRuns,
        checksum: fieldChecksum,
        occupancyChecksum: occupancyChecksum,
        occupancyState: occupancyState,
        removeOccupancyRuns: removeOccupancyRuns,
        equalOccupancy: equalOccupancy,
        classifyToolAccess: classifyToolAccess,
        serialize: serialize
    });
}(self));
