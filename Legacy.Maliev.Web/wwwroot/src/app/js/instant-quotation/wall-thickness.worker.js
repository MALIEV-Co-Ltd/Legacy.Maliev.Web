// Independently authored browser-worker wall-thickness analysis.
// Measures a bounded set of triangle centroids along their inward face normals.
(function (root, factory) {
    var api = factory();
    if (typeof module === 'object' && module.exports) { module.exports = api; }
    root.MalievWallThickness = api;
}(typeof self !== 'undefined' ? self : globalThis, function () {
    'use strict';

    var DEFAULTS = Object.freeze({
        maxSamples: 200000,
        maxLeafTriangles: 12,
        maximumFacingSkips: 4,
        minimumFacingCosine: 0.5
    });

    function now() {
        return typeof performance !== 'undefined' && typeof performance.now === 'function'
            ? performance.now() : Date.now();
    }

    function normalizeOptions(requested) {
        requested = requested || {};
        return {
            maxSamples: Math.max(1, Math.floor(Number(requested.maxSamples) || DEFAULTS.maxSamples)),
            maxLeafTriangles: Math.max(2, Math.floor(Number(requested.maxLeafTriangles) || DEFAULTS.maxLeafTriangles)),
            maximumFacingSkips: Math.max(1, Math.floor(Number(requested.maximumFacingSkips) || DEFAULTS.maximumFacingSkips)),
            minimumFacingCosine: Math.max(0, Math.min(1,
                Number.isFinite(Number(requested.minimumFacingCosine))
                    ? Number(requested.minimumFacingCosine) : DEFAULTS.minimumFacingCosine))
        };
    }

    function transformedPoint(position, vertexIndex, matrix) {
        var offset = vertexIndex * 3;
        var x = Number(position[offset]);
        var y = Number(position[offset + 1]);
        var z = Number(position[offset + 2]);
        if (!matrix || matrix.length !== 16) { return [x, y, z]; }
        return [
            matrix[0] * x + matrix[4] * y + matrix[8] * z + matrix[12],
            matrix[1] * x + matrix[5] * y + matrix[9] * z + matrix[13],
            matrix[2] * x + matrix[6] * y + matrix[10] * z + matrix[14]
        ];
    }

    function triangleRecord(meshIndex, triangleIndex, sourceVertexIndexes, a, b, c) {
        var abx = b[0] - a[0];
        var aby = b[1] - a[1];
        var abz = b[2] - a[2];
        var acx = c[0] - a[0];
        var acy = c[1] - a[1];
        var acz = c[2] - a[2];
        var crossX = aby * acz - abz * acy;
        var crossY = abz * acx - abx * acz;
        var crossZ = abx * acy - aby * acx;
        var crossLength = Math.hypot(crossX, crossY, crossZ);
        var inverseLength = crossLength > 0 ? 1 / crossLength : 0;
        return {
            meshIndex: meshIndex,
            triangleIndex: triangleIndex,
            sourceVertexIndexes: sourceVertexIndexes,
            ax: a[0], ay: a[1], az: a[2],
            bx: b[0], by: b[1], bz: b[2],
            cx: c[0], cy: c[1], cz: c[2],
            centreX: (a[0] + b[0] + c[0]) / 3,
            centreY: (a[1] + b[1] + c[1]) / 3,
            centreZ: (a[2] + b[2] + c[2]) / 3,
            normalX: crossX * inverseLength,
            normalY: crossY * inverseLength,
            normalZ: crossZ * inverseLength,
            area: crossLength * 0.5,
            minX: Math.min(a[0], b[0], c[0]), maxX: Math.max(a[0], b[0], c[0]),
            minY: Math.min(a[1], b[1], c[1]), maxY: Math.max(a[1], b[1], c[1]),
            minZ: Math.min(a[2], b[2], c[2]), maxZ: Math.max(a[2], b[2], c[2])
        };
    }

    function normalizeTriangles(meshes) {
        var candidates = [];
        var rawTriangleCount = 0;
        var minX = Infinity; var minY = Infinity; var minZ = Infinity;
        var maxX = -Infinity; var maxY = -Infinity; var maxZ = -Infinity;

        (Array.isArray(meshes) ? meshes : []).forEach(function (mesh, meshIndex) {
            var position = mesh && mesh.position;
            if (!position || position.length < 9) { return; }
            var index = mesh.index || null;
            var vertexReferenceCount = index ? index.length : position.length / 3;
            var triangleCount = Math.floor(vertexReferenceCount / 3);
            rawTriangleCount += triangleCount;
            for (var triangleIndex = 0; triangleIndex < triangleCount; triangleIndex += 1) {
                var base = triangleIndex * 3;
                var ia = index ? Number(index[base]) : base;
                var ib = index ? Number(index[base + 1]) : base + 1;
                var ic = index ? Number(index[base + 2]) : base + 2;
                if (ia < 0 || ib < 0 || ic < 0
                    || ia * 3 + 2 >= position.length || ib * 3 + 2 >= position.length
                    || ic * 3 + 2 >= position.length) { continue; }
                var a = transformedPoint(position, ia, mesh.matrix);
                var b = transformedPoint(position, ib, mesh.matrix);
                var c = transformedPoint(position, ic, mesh.matrix);
                var record = triangleRecord(meshIndex, triangleIndex, [ia, ib, ic], a, b, c);
                candidates.push(record);
                minX = Math.min(minX, record.minX); minY = Math.min(minY, record.minY); minZ = Math.min(minZ, record.minZ);
                maxX = Math.max(maxX, record.maxX); maxY = Math.max(maxY, record.maxY); maxZ = Math.max(maxZ, record.maxZ);
            }
        });

        var diagonal = candidates.length > 0
            ? Math.hypot(maxX - minX, maxY - minY, maxZ - minZ) : 0;
        var minimumArea = Math.max(diagonal * diagonal * 1e-14, 1e-12);
        var triangles = candidates.filter(function (triangle) { return triangle.area >= minimumArea; });
        var reasons = [];
        if (triangles.length < rawTriangleCount) { reasons.push('degenerate-triangles'); }
        return {
            triangles: triangles,
            triangleCount: rawTriangleCount,
            diagonal: diagonal,
            bounds: { minX: minX, minY: minY, minZ: minZ, maxX: maxX, maxY: maxY, maxZ: maxZ },
            reasons: reasons
        };
    }

    function selectAreaQuantiles(triangles, maximum) {
        if (triangles.length <= maximum) {
            return triangles.map(function (_, index) { return index; });
        }
        var totalArea = triangles.reduce(function (sum, triangle) { return sum + triangle.area; }, 0);
        var selected = [];
        var selectedSet = new Set();
        var cursor = 0;
        var cumulative = triangles[0].area;
        for (var sample = 0; sample < maximum; sample += 1) {
            var target = totalArea * ((sample + 0.5) / maximum);
            while (cursor < triangles.length - 1 && cumulative < target) {
                cursor += 1;
                cumulative += triangles[cursor].area;
            }
            var chosen = cursor;
            while (chosen < triangles.length && selectedSet.has(chosen)) { chosen += 1; }
            if (chosen >= triangles.length) {
                chosen = cursor - 1;
                while (chosen >= 0 && selectedSet.has(chosen)) { chosen -= 1; }
            }
            if (chosen >= 0 && !selectedSet.has(chosen)) {
                selectedSet.add(chosen);
                selected.push(chosen);
            }
        }
        if (selected.length < maximum) {
            for (var index = 0; index < triangles.length && selected.length < maximum; index += 1) {
                if (!selectedSet.has(index)) { selectedSet.add(index); selected.push(index); }
            }
        }
        selected.sort(function (left, right) { return left - right; });
        return selected;
    }

    function nodeBounds(triangles, indexes) {
        var result = { minX: Infinity, minY: Infinity, minZ: Infinity, maxX: -Infinity, maxY: -Infinity, maxZ: -Infinity };
        indexes.forEach(function (index) {
            var triangle = triangles[index];
            result.minX = Math.min(result.minX, triangle.minX); result.maxX = Math.max(result.maxX, triangle.maxX);
            result.minY = Math.min(result.minY, triangle.minY); result.maxY = Math.max(result.maxY, triangle.maxY);
            result.minZ = Math.min(result.minZ, triangle.minZ); result.maxZ = Math.max(result.maxZ, triangle.maxZ);
        });
        return result;
    }

    function buildBvh(triangles, maximumLeafSize) {
        function build(indexes) {
            var bounds = nodeBounds(triangles, indexes);
            if (indexes.length <= maximumLeafSize) {
                bounds.indexes = indexes;
                return bounds;
            }
            var sizeX = bounds.maxX - bounds.minX;
            var sizeY = bounds.maxY - bounds.minY;
            var sizeZ = bounds.maxZ - bounds.minZ;
            var property = sizeX >= sizeY && sizeX >= sizeZ ? 'centreX'
                : (sizeY >= sizeZ ? 'centreY' : 'centreZ');
            indexes.sort(function (left, right) { return triangles[left][property] - triangles[right][property]; });
            var middle = Math.floor(indexes.length / 2);
            bounds.left = build(indexes.slice(0, middle));
            bounds.right = build(indexes.slice(middle));
            return bounds;
        }
        return build(triangles.map(function (_, index) { return index; }));
    }

    function rayBoxEntry(node, ox, oy, oz, dx, dy, dz, near, far) {
        var minimum = near;
        var maximum = far;
        var origins = [ox, oy, oz];
        var directions = [dx, dy, dz];
        var minimums = [node.minX, node.minY, node.minZ];
        var maximums = [node.maxX, node.maxY, node.maxZ];
        for (var axis = 0; axis < 3; axis += 1) {
            var direction = directions[axis];
            if (Math.abs(direction) < 1e-14) {
                if (origins[axis] < minimums[axis] || origins[axis] > maximums[axis]) { return Infinity; }
                continue;
            }
            var inverse = 1 / direction;
            var t1 = (minimums[axis] - origins[axis]) * inverse;
            var t2 = (maximums[axis] - origins[axis]) * inverse;
            if (t1 > t2) { var swap = t1; t1 = t2; t2 = swap; }
            minimum = Math.max(minimum, t1);
            maximum = Math.min(maximum, t2);
            if (maximum < minimum) { return Infinity; }
        }
        return minimum;
    }

    function intersectTriangle(triangle, ox, oy, oz, dx, dy, dz, near, far) {
        var edgeOneX = triangle.bx - triangle.ax;
        var edgeOneY = triangle.by - triangle.ay;
        var edgeOneZ = triangle.bz - triangle.az;
        var edgeTwoX = triangle.cx - triangle.ax;
        var edgeTwoY = triangle.cy - triangle.ay;
        var edgeTwoZ = triangle.cz - triangle.az;
        var px = dy * edgeTwoZ - dz * edgeTwoY;
        var py = dz * edgeTwoX - dx * edgeTwoZ;
        var pz = dx * edgeTwoY - dy * edgeTwoX;
        var determinant = edgeOneX * px + edgeOneY * py + edgeOneZ * pz;
        if (Math.abs(determinant) < 1e-12) { return Infinity; }
        var inverse = 1 / determinant;
        var tx = ox - triangle.ax;
        var ty = oy - triangle.ay;
        var tz = oz - triangle.az;
        var u = (tx * px + ty * py + tz * pz) * inverse;
        if (u < -1e-10 || u > 1 + 1e-10) { return Infinity; }
        var qx = ty * edgeOneZ - tz * edgeOneY;
        var qy = tz * edgeOneX - tx * edgeOneZ;
        var qz = tx * edgeOneY - ty * edgeOneX;
        var v = (dx * qx + dy * qy + dz * qz) * inverse;
        if (v < -1e-10 || u + v > 1 + 1e-10) { return Infinity; }
        var distance = (edgeTwoX * qx + edgeTwoY * qy + edgeTwoZ * qz) * inverse;
        return distance > near && distance <= far ? distance : Infinity;
    }

    function firstBvhHit(bvh, triangles, ox, oy, oz, dx, dy, dz, near, far, originatingIndex) {
        var bestDistance = far;
        var bestTriangle = null;
        var bestTriangleIndex = -1;
        var stack = [{ node: bvh, entry: rayBoxEntry(bvh, ox, oy, oz, dx, dy, dz, near, far) }];
        while (stack.length > 0) {
            var current = stack.pop();
            if (current.entry === Infinity || current.entry > bestDistance) { continue; }
            var node = current.node;
            if (node.indexes) {
                for (var leafIndex = 0; leafIndex < node.indexes.length; leafIndex += 1) {
                    var triangleIndex = node.indexes[leafIndex];
                    if (triangleIndex === originatingIndex) { continue; }
                    var candidate = intersectTriangle(triangles[triangleIndex], ox, oy, oz, dx, dy, dz, near, bestDistance);
                    if (candidate < bestDistance) {
                        bestDistance = candidate;
                        bestTriangle = triangles[triangleIndex];
                        bestTriangleIndex = triangleIndex;
                    }
                }
                continue;
            }
            var leftEntry = rayBoxEntry(node.left, ox, oy, oz, dx, dy, dz, near, bestDistance);
            var rightEntry = rayBoxEntry(node.right, ox, oy, oz, dx, dy, dz, near, bestDistance);
            if (leftEntry < rightEntry) {
                if (rightEntry !== Infinity) { stack.push({ node: node.right, entry: rightEntry }); }
                if (leftEntry !== Infinity) { stack.push({ node: node.left, entry: leftEntry }); }
            } else {
                if (leftEntry !== Infinity) { stack.push({ node: node.left, entry: leftEntry }); }
                if (rightEntry !== Infinity) { stack.push({ node: node.right, entry: rightEntry }); }
            }
        }
        return bestTriangle ? { distance: bestDistance, triangle: bestTriangle,
            triangleIndex: bestTriangleIndex } : null;
    }

    function measureSample(triangleIndex, triangles, bvh, options, diagonal) {
        var sample = triangles[triangleIndex];
        var epsilon = Math.max(diagonal * 1e-7, 1e-5);
        var dx = -sample.normalX;
        var dy = -sample.normalY;
        var dz = -sample.normalZ;
        var ox = sample.centreX + dx * epsilon;
        var oy = sample.centreY + dy * epsilon;
        var oz = sample.centreZ + dz * epsilon;
        var near = epsilon;
        var far = diagonal + epsilon;
        for (var attempt = 0; attempt < options.maximumFacingSkips; attempt += 1) {
            var hit = firstBvhHit(bvh, triangles, ox, oy, oz, dx, dy, dz, near, far, triangleIndex);
            if (!hit) { return null; }
            var alignment = Math.abs(hit.triangle.normalX * dx
                + hit.triangle.normalY * dy + hit.triangle.normalZ * dz);
            if (alignment >= options.minimumFacingCosine) {
                return { thickness: hit.distance + epsilon, oppositeTriangleIndex: hit.triangleIndex };
            }
            near = hit.distance + epsilon;
        }
        return null;
    }

    function clipPatchPolygon(polygon, distanceFromBoundary) {
        if (!polygon.length) { return polygon; }
        var clipped = [];
        var previous = polygon[polygon.length - 1];
        var previousDistance = distanceFromBoundary(previous);
        polygon.forEach(function (current) {
            var currentDistance = distanceFromBoundary(current);
            var previousInside = previousDistance >= -1e-7;
            var currentInside = currentDistance >= -1e-7;
            if (previousInside !== currentInside) {
                var fraction = previousDistance / (previousDistance - currentDistance);
                clipped.push({
                    x: previous.x + (current.x - previous.x) * fraction,
                    y: previous.y + (current.y - previous.y) * fraction,
                    z: previous.z + (current.z - previous.z) * fraction,
                    thickness: previous.thickness + (current.thickness - previous.thickness) * fraction
                });
            }
            if (currentInside) { clipped.push(current); }
            previous = current;
            previousDistance = currentDistance;
        });
        return clipped;
    }

    // A coarse opposite face can miss a narrow wall at its centroid. Clip the
    // source projection to the actual hit triangle and retain the ray distance
    // at each corner: one thin centroid must not paint a long, thick wedge red.
    function oppositeSurfacePatches(triangles, measured, fields) {
        var patches = [];
        measured.forEach(function (entry) {
            if (entry.thickness >= 0.8 || entry.oppositeTriangleIndex == null) { return; }
            var source = triangles[entry.triangleIndex];
            var opposite = triangles[entry.oppositeTriangleIndex];
            if (!opposite) { return; }
            var normalDot = source.normalX * opposite.normalX
                + source.normalY * opposite.normalY + source.normalZ * opposite.normalZ;
            if (normalDot > -0.5) { return; }
            var oppositeField = fields[opposite.meshIndex];
            var oppositeMaximum = opposite.sourceVertexIndexes.reduce(function (maximum, index) {
                var value = oppositeField[index];
                return Number.isFinite(value) ? Math.max(maximum, value) : Infinity;
            }, 0);
            var dx = -source.normalX; var dy = -source.normalY; var dz = -source.normalZ;
            var denominator = opposite.normalX * dx + opposite.normalY * dy + opposite.normalZ * dz;
            if (Math.abs(denominator) < 0.5) { return; }
            var points = [[source.ax, source.ay, source.az],
                [source.bx, source.by, source.bz], [source.cx, source.cy, source.cz]];
            var projected = [];
            points.forEach(function (point) {
                var distance = (opposite.normalX * (opposite.ax - point[0])
                    + opposite.normalY * (opposite.ay - point[1])
                    + opposite.normalZ * (opposite.az - point[2])) / denominator;
                if (Number.isFinite(distance)) {
                    projected.push({ x: point[0] + dx * distance,
                        y: point[1] + dy * distance, z: point[2] + dz * distance,
                        thickness: distance });
                }
            });
            if (projected.length !== 3) { return; }
            var corners = [[opposite.ax, opposite.ay, opposite.az],
                [opposite.bx, opposite.by, opposite.bz], [opposite.cx, opposite.cy, opposite.cz]];
            for (var edge = 0; edge < 3 && projected.length; edge += 1) {
                var a = corners[edge]; var b = corners[(edge + 1) % 3];
                var edgeX = b[0] - a[0]; var edgeY = b[1] - a[1]; var edgeZ = b[2] - a[2];
                projected = clipPatchPolygon(projected, function (point) {
                    var px = point.x - a[0]; var py = point.y - a[1]; var pz = point.z - a[2];
                    return (edgeY * pz - edgeZ * py) * opposite.normalX
                        + (edgeZ * px - edgeX * pz) * opposite.normalY
                        + (edgeX * py - edgeY * px) * opposite.normalZ;
                });
            }
            projected = clipPatchPolygon(projected, function (point) { return point.thickness; });
            for (var corner = 1; corner + 1 < projected.length; corner += 1) {
                var face = [projected[0], projected[corner], projected[corner + 1]];
                var abx = face[1].x - face[0].x; var aby = face[1].y - face[0].y; var abz = face[1].z - face[0].z;
                var acx = face[2].x - face[0].x; var acy = face[2].y - face[0].y; var acz = face[2].z - face[0].z;
                var area = Math.hypot(aby * acz - abz * acy, abz * acx - abx * acz, abx * acy - aby * acx);
                if (area <= 1e-10) { continue; }
                face.forEach(function (point) { patches.push(point.x, point.y, point.z); });
                face.forEach(function (point) { patches.push(point.thickness); });
                patches.push(oppositeMaximum);
            }
        });
        return Float32Array.from(patches);
    }

    function setMinimum(field, vertexIndex, value) {
        var previous = field[vertexIndex];
        if (!Number.isFinite(previous) || value < previous) { field[vertexIndex] = value; }
    }

    function weightedPercentile(entries, fraction) {
        if (entries.length === 0) { return null; }
        var totalWeight = entries.reduce(function (sum, entry) { return sum + entry.area; }, 0);
        var target = totalWeight * fraction;
        var cumulative = 0;
        for (var index = 0; index < entries.length; index += 1) {
            cumulative += entries[index].area;
            if (cumulative >= target) { return entries[index].thickness; }
        }
        return entries[entries.length - 1].thickness;
    }

    function interpolateSampledField(triangles, measured, fields, bounds) {
        if (measured.length === 0) { return; }
        var cells = Math.max(4, Math.min(64, Math.ceil(Math.cbrt(measured.length))));
        var sizeX = Math.max(bounds.maxX - bounds.minX, 1e-9);
        var sizeY = Math.max(bounds.maxY - bounds.minY, 1e-9);
        var sizeZ = Math.max(bounds.maxZ - bounds.minZ, 1e-9);
        var grid = new Map();
        function coordinate(value, minimum, size) {
            return Math.max(0, Math.min(cells - 1, Math.floor(((value - minimum) / size) * cells)));
        }
        function coordinates(triangle) {
            return [coordinate(triangle.centreX, bounds.minX, sizeX),
                coordinate(triangle.centreY, bounds.minY, sizeY),
                coordinate(triangle.centreZ, bounds.minZ, sizeZ)];
        }
        function key(x, y, z) { return x + '|' + y + '|' + z; }
        measured.forEach(function (entry) {
            var point = coordinates(triangles[entry.triangleIndex]);
            var cellKey = key(point[0], point[1], point[2]);
            if (!grid.has(cellKey)) { grid.set(cellKey, []); }
            grid.get(cellKey).push(entry);
        });
        triangles.forEach(function (triangle, triangleIndex) {
            var field = fields[triangle.meshIndex];
            if (triangle.sourceVertexIndexes.every(function (vertexIndex) { return Number.isFinite(field[vertexIndex]); })) { return; }
            var point = coordinates(triangle);
            var best = null;
            var bestDistance = Infinity;
            for (var x = Math.max(0, point[0] - 1); x <= Math.min(cells - 1, point[0] + 1); x += 1) {
                for (var y = Math.max(0, point[1] - 1); y <= Math.min(cells - 1, point[1] + 1); y += 1) {
                    for (var z = Math.max(0, point[2] - 1); z <= Math.min(cells - 1, point[2] + 1); z += 1) {
                        var candidates = grid.get(key(x, y, z)) || [];
                        candidates.forEach(function (candidate) {
                            var source = triangles[candidate.triangleIndex];
                            var agreement = Math.abs(source.normalX * triangle.normalX
                                + source.normalY * triangle.normalY + source.normalZ * triangle.normalZ);
                            if (agreement < 0.7) { return; }
                            var distance = Math.pow(source.centreX - triangle.centreX, 2)
                                + Math.pow(source.centreY - triangle.centreY, 2)
                                + Math.pow(source.centreZ - triangle.centreZ, 2);
                            if (distance < bestDistance) { bestDistance = distance; best = candidate; }
                        });
                    }
                }
            }
            if (!best) { return; }
            triangle.sourceVertexIndexes.forEach(function (vertexIndex) {
                setMinimum(field, vertexIndex, best.thickness);
            });
        });
    }

    // The ray result belongs to a face, but STL triangles usually duplicate their
    // vertices. Average display values at coincident vertices on the same surface so
    // the colour ramp interpolates across faces. Keep the measured samples untouched:
    // only this visual field is smoothed, and sharp corners/opposite walls stay apart.
    function smoothDisplayField(meshes, triangles, fields) {
        if (triangles.length > 150000) { return; }
        var buckets = new Map();
        // Indexed vertices already carry a shared value. Their minimum adjacent
        // measurement is meaningful, and one index cannot hold two crease colours.
        triangles = triangles.filter(function (triangle) { return !meshes[triangle.meshIndex].index; });
        function vertexKey(triangle, point) {
            return triangle.meshIndex + '|' + point[0] + '|' + point[1] + '|' + point[2];
        }
        triangles.forEach(function (triangle) {
            var field = fields[triangle.meshIndex];
            var values = triangle.sourceVertexIndexes.map(function (index) { return field[index]; })
                .filter(Number.isFinite);
            if (!values.length) { return; }
            var value = values.reduce(function (sum, entry) { return sum + entry; }, 0) / values.length;
            var weight = Math.sqrt(triangle.area);
            if (!(weight > 0)) { return; }
            var points = [[triangle.ax, triangle.ay, triangle.az],
                [triangle.bx, triangle.by, triangle.bz], [triangle.cx, triangle.cy, triangle.cz]];
            points.forEach(function (point) {
                var key = vertexKey(triangle, point);
                var groups = buckets.get(key);
                if (!groups) { groups = []; buckets.set(key, groups); }
                var group = groups.find(function (entry) {
                    return entry.nx * triangle.normalX + entry.ny * triangle.normalY
                        + entry.nz * triangle.normalZ >= 0.8;
                });
                if (!group) {
                    group = { nx: triangle.normalX, ny: triangle.normalY, nz: triangle.normalZ,
                        sum: 0, weight: 0 };
                    groups.push(group);
                }
                group.sum += value * weight;
                group.weight += weight;
            });
        });
        triangles.forEach(function (triangle) {
            var field = fields[triangle.meshIndex];
            var points = [[triangle.ax, triangle.ay, triangle.az],
                [triangle.bx, triangle.by, triangle.bz], [triangle.cx, triangle.cy, triangle.cz]];
            points.forEach(function (point, corner) {
                var groups = buckets.get(vertexKey(triangle, point)) || [];
                var group = groups.find(function (entry) {
                    return entry.nx * triangle.normalX + entry.ny * triangle.normalY
                        + entry.nz * triangle.normalZ >= 0.8;
                });
                if (group && group.weight > 0) {
                    field[triangle.sourceVertexIndexes[corner]] = group.sum / group.weight;
                }
            });
        });
    }

    function measureField(meshes, normalized, selected, bvh, options, startedAt) {
        var fields = (Array.isArray(meshes) ? meshes : []).map(function (mesh) {
            var field = new Float32Array(mesh && mesh.position ? mesh.position.length / 3 : 0);
            field.fill(NaN);
            return field;
        });
        var measured = [];
        var totalSelectedArea = 0;
        selected.forEach(function (triangleIndex) {
            var triangle = normalized.triangles[triangleIndex];
            totalSelectedArea += triangle.area;
            var sample = measureSample(triangleIndex, normalized.triangles, bvh, options, normalized.diagonal);
            if (!sample || !Number.isFinite(sample.thickness)) { return; }
            var value = sample.thickness;
            measured.push({ triangleIndex: triangleIndex, thickness: value, area: triangle.area,
                oppositeTriangleIndex: sample.oppositeTriangleIndex });
            triangle.sourceVertexIndexes.forEach(function (vertexIndex) {
                setMinimum(fields[triangle.meshIndex], vertexIndex, value);
            });
        });
        if (selected.length < normalized.triangles.length) {
            interpolateSampledField(normalized.triangles, measured, fields, normalized.bounds);
        }
        smoothDisplayField(meshes, normalized.triangles, fields);
        var patches = oppositeSurfacePatches(normalized.triangles, measured, fields);
        var sorted = measured.slice().sort(function (left, right) { return left.thickness - right.thickness; });
        var measuredArea = measured.reduce(function (sum, entry) { return sum + entry.area; }, 0);
        var allArea = normalized.triangles.reduce(function (sum, triangle) { return sum + triangle.area; }, 0);
        var reasons = normalized.reasons.slice();
        if (selected.length < normalized.triangles.length) { reasons.push('sampled-triangles'); }
        if (measured.length < selected.length) { reasons.push('unmeasured-regions'); }
        var state = measured.length === 0 ? 'unavailable'
            : (selected.length < normalized.triangles.length ? 'sampled' : 'complete');
        return {
            summary: {
                method: 'normal-ray', version: 1, state: state,
                sampled: selected.length < normalized.triangles.length,
                triangleCount: normalized.triangleCount,
                validTriangleCount: normalized.triangles.length,
                sampledTriangleCount: selected.length,
                measuredSampleCount: measured.length,
                unmeasuredSampleCount: selected.length - measured.length,
                totalAreaMm2: allArea,
                measuredAreaMm2: measuredArea,
                measuredAreaRatio: totalSelectedArea > 0 ? measuredArea / totalSelectedArea : 0,
                minMm: sorted.length ? sorted[0].thickness : null,
                p05Mm: weightedPercentile(sorted, 0.05),
                medianMm: weightedPercentile(sorted, 0.5),
                p95Mm: weightedPercentile(sorted, 0.95),
                maxMm: sorted.length ? sorted[sorted.length - 1].thickness : null,
                durationMs: Math.max(0, now() - startedAt),
                reasons: Array.from(new Set(reasons))
            },
            fields: fields,
            oppositeSurfacePatches: patches,
            sampleThicknessMm: Float32Array.from(measured.map(function (entry) { return entry.thickness; })),
            sampleAreaMm2: Float32Array.from(measured.map(function (entry) { return entry.area; }))
        };
    }

    function unavailableResult(meshes, normalized, startedAt) {
        var fields = (Array.isArray(meshes) ? meshes : []).map(function (mesh) {
            var field = new Float32Array(mesh && mesh.position ? mesh.position.length / 3 : 0);
            field.fill(NaN);
            return field;
        });
        return {
            summary: {
                method: 'normal-ray', version: 1, state: 'unavailable', sampled: false,
                triangleCount: normalized.triangleCount, validTriangleCount: 0,
                sampledTriangleCount: 0, measuredSampleCount: 0, unmeasuredSampleCount: 0,
                totalAreaMm2: 0, measuredAreaMm2: 0, measuredAreaRatio: 0,
                minMm: null, p05Mm: null, medianMm: null, p95Mm: null, maxMm: null,
                durationMs: Math.max(0, now() - startedAt), reasons: Array.from(new Set(normalized.reasons.concat('no-valid-triangles')))
            },
            fields: fields,
            oppositeSurfacePatches: new Float32Array(0),
            sampleThicknessMm: new Float32Array(0),
            sampleAreaMm2: new Float32Array(0)
        };
    }

    function analyze(meshes, requested) {
        var startedAt = now();
        var options = normalizeOptions(requested);
        var normalized = normalizeTriangles(meshes);
        if (normalized.triangles.length === 0) { return unavailableResult(meshes, normalized, startedAt); }
        var selected = selectAreaQuantiles(normalized.triangles, options.maxSamples);
        var bvh = buildBvh(normalized.triangles, options.maxLeafTriangles);
        return measureField(meshes, normalized, selected, bvh, options, startedAt);
    }

    return {
        analyze: analyze,
        _test: { intersectTriangle: intersectTriangle, buildBvh: buildBvh,
            normalizeTriangles: normalizeTriangles,
            selectAreaQuantiles: selectAreaQuantiles, smoothDisplayField: smoothDisplayField }
    };
}));

