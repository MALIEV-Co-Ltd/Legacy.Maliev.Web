(function (scope) {
    'use strict';

    var ANALYSIS_VERSION = 'additive-browser-physical-v1';
    var MIN_GRID_AXIS = 24;
    var MAX_GRID_AXIS = 512;
    var MAX_GRID_CELLS = 262144;
    // The pricing surrogate is intentionally smaller than the uploaded/displayed mesh.
    // Keeping the analysis surface below this budget makes slicing predictable while the
    // customer's original geometry remains untouched for preview and order submission.
    var MAX_TRIANGLES = 250000;
    var MAX_LAYERS = 10000;
    var MAX_LAYER_TRIANGLE_INTERSECTIONS = 4000000;
    var EPSILON = 0.000001;

    function finite(value, fallback) {
        value = Number(value);
        return Number.isFinite(value) ? value : fallback;
    }

    function positive(value, fallback) {
        value = finite(value, fallback);
        return value > 0 ? value : fallback;
    }

    function transformPoint(matrix, x, y, z) {
        if (!matrix || matrix.length !== 16) { return [x, y, z]; }
        return [
            (matrix[0] * x) + (matrix[4] * y) + (matrix[8] * z) + matrix[12],
            (matrix[1] * x) + (matrix[5] * y) + (matrix[9] * z) + matrix[13],
            (matrix[2] * x) + (matrix[6] * y) + (matrix[10] * z) + matrix[14],
        ];
    }

    function meshTriangleCount(meshes) {
        return (meshes || []).reduce(function (total, mesh) {
            var positions = mesh && mesh.position;
            var indices = mesh && mesh.index;
            if (!positions) { return total; }
            return total + Math.floor((indices ? indices.length : positions.length / 3) / 3);
        }, 0);
    }

    function visitMeshTriangles(meshes, visitor) {
        (meshes || []).forEach(function (mesh) {
            var positions = mesh && mesh.position;
            var indices = mesh && mesh.index;
            if (!positions || positions.length < 9) { return; }
            var count = indices ? indices.length : positions.length / 3;
            for (var cursor = 0; cursor + 2 < count; cursor += 3) {
                var triangle = new Array(9);
                for (var corner = 0; corner < 3; corner += 1) {
                    var vertex = indices ? Number(indices[cursor + corner]) : cursor + corner;
                    var offset = vertex * 3;
                    var point = transformPoint(
                        mesh.matrix,
                        Number(positions[offset]),
                        Number(positions[offset + 1]),
                        Number(positions[offset + 2]));
                    triangle[(corner * 3)] = point[0];
                    triangle[(corner * 3) + 1] = point[1];
                    triangle[(corner * 3) + 2] = point[2];
                }
                if (triangle.every(Number.isFinite)) { visitor(triangle); }
            }
        });
    }

    function flattenTrianglesUnchecked(meshes) {
        var requestedTriangleCount = meshTriangleCount(meshes);
        var triangles = new Float64Array(requestedTriangleCount * 9);
        var triangleCursor = 0;
        visitMeshTriangles(meshes, function (triangle) {
            triangles.set(triangle, triangleCursor);
            triangleCursor += 9;
        });
        if (triangleCursor === 0) { throw new Error('geometry_empty'); }
        return triangles.subarray(0, triangleCursor);
    }

    function sourceBounds(meshes) {
        var bounds = { minX: Infinity, minY: Infinity, minZ: Infinity, maxX: -Infinity, maxY: -Infinity, maxZ: -Infinity };
        visitMeshTriangles(meshes, function (triangle) {
            for (var offset = 0; offset < 9; offset += 3) {
                bounds.minX = Math.min(bounds.minX, triangle[offset]);
                bounds.minY = Math.min(bounds.minY, triangle[offset + 1]);
                bounds.minZ = Math.min(bounds.minZ, triangle[offset + 2]);
                bounds.maxX = Math.max(bounds.maxX, triangle[offset]);
                bounds.maxY = Math.max(bounds.maxY, triangle[offset + 1]);
                bounds.maxZ = Math.max(bounds.maxZ, triangle[offset + 2]);
            }
        });
        if (!Number.isFinite(bounds.minX)) { throw new Error('geometry_empty'); }
        return bounds;
    }

    function clusterMeshTriangles(meshes, bounds, cellSize) {
        var representatives = new Map();
        var uniqueTriangles = new Map();
        function clusteredVertex(x, y, z) {
            var key = Math.floor((x - bounds.minX) / cellSize) + ','
                + Math.floor((y - bounds.minY) / cellSize) + ','
                + Math.floor((z - bounds.minZ) / cellSize);
            var representative = representatives.get(key);
            if (!representative) {
                representative = [x, y, z];
                representatives.set(key, representative);
            }
            return { key: key, point: representative };
        }
        visitMeshTriangles(meshes, function (triangle) {
            var vertices = [
                clusteredVertex(triangle[0], triangle[1], triangle[2]),
                clusteredVertex(triangle[3], triangle[4], triangle[5]),
                clusteredVertex(triangle[6], triangle[7], triangle[8]),
            ];
            if (vertices[0].key === vertices[1].key
                || vertices[1].key === vertices[2].key
                || vertices[2].key === vertices[0].key) { return; }
            var key = vertices.map(function (vertex) { return vertex.key; }).sort().join('|');
            if (!uniqueTriangles.has(key)) {
                uniqueTriangles.set(key, vertices.reduce(function (values, vertex) {
                    return values.concat(vertex.point);
                }, []));
            }
        });
        var flattened = new Float64Array(uniqueTriangles.size * 9);
        var cursor = 0;
        uniqueTriangles.forEach(function (triangle) {
            flattened.set(triangle, cursor);
            cursor += 9;
        });
        if (cursor === 0) { throw new Error('geometry_requires_review'); }
        return flattened;
    }

    function simplifyMeshesToTriangleBudget(meshes, targetTriangles) {
        var bounds = sourceBounds(meshes);
        var maximumSpan = Math.max(
            bounds.maxX - bounds.minX,
            bounds.maxY - bounds.minY,
            bounds.maxZ - bounds.minZ);
        if (!(maximumSpan > EPSILON)) { throw new Error('geometry_empty'); }
        var cellSize = maximumSpan / Math.max(2, Math.sqrt(Math.max(12, targetTriangles) / 12));
        var simplified = clusterMeshTriangles(meshes, bounds, Math.max(EPSILON, cellSize));
        for (var attempt = 0; attempt < 10 && simplified.length / 9 > targetTriangles; attempt += 1) {
            cellSize *= 1.45;
            simplified = clusterMeshTriangles(meshes, bounds, cellSize);
        }
        return simplified;
    }

    function estimateLayerTriangleIntersections(triangles, layerHeight) {
        var workload = 0;
        for (var triangleOffset = 0; triangleOffset < triangles.length; triangleOffset += 9) {
            var minimum = Math.min(triangles[triangleOffset + 2], triangles[triangleOffset + 5], triangles[triangleOffset + 8]);
            var maximum = Math.max(triangles[triangleOffset + 2], triangles[triangleOffset + 5], triangles[triangleOffset + 8]);
            workload += Math.max(1, Math.ceil((maximum - minimum) / layerHeight) + 1);
        }
        return workload;
    }

    function preparePricingTriangles(meshes, options) {
        options = options || {};
        var sourceTriangleCount = meshTriangleCount(meshes);
        if (!(sourceTriangleCount > 0)) { throw new Error('geometry_empty'); }
        var maxTriangles = Math.max(12, Math.floor(positive(options.maxTriangles, MAX_TRIANGLES)));
        var maxIntersections = Math.max(12, Math.floor(positive(
            options.maxLayerTriangleIntersections,
            MAX_LAYER_TRIANGLE_INTERSECTIONS)));
        var layerHeight = positive(options.layerHeightMm, 0.2);
        var targetTriangles = Math.min(sourceTriangleCount, maxTriangles);
        var triangles = sourceTriangleCount > targetTriangles
            ? simplifyMeshesToTriangleBudget(meshes, targetTriangles)
            : flattenTrianglesUnchecked(meshes);
        var workload = estimateLayerTriangleIntersections(triangles, layerHeight);
        for (var attempt = 0; attempt < 8 && workload > maxIntersections; attempt += 1) {
            targetTriangles = Math.max(12, Math.floor(
                (triangles.length / 9) * (maxIntersections / workload) * 0.82));
            triangles = simplifyMeshesToTriangleBudget(meshes, targetTriangles);
            workload = estimateLayerTriangleIntersections(triangles, layerHeight);
        }
        if (triangles.length / 9 > maxTriangles || workload > maxIntersections) {
            throw new Error('geometry_requires_review');
        }
        validateClosedManifold(triangles);
        return {
            triangles: triangles,
            sourceTriangleCount: sourceTriangleCount,
            analysisTriangleCount: triangles.length / 9,
            estimatedLayerTriangleIntersections: workload,
            simplified: triangles.length / 9 < sourceTriangleCount,
        };
    }

    function flattenTriangles(meshes) {
        return preparePricingTriangles(meshes).triangles;
    }

    function quantize(value) { return Math.round(value * 1000); }
    function point3dKey(x, y, z) {
        return quantize(x) + ',' + quantize(y) + ',' + quantize(z);
    }

    function validateClosedManifold(triangles) {
        var vertexIds = new Map();
        var edgeCounts = new Map();
        var nextVertexId = 0;
        function vertexId(offset) {
            var key = point3dKey(triangles[offset], triangles[offset + 1], triangles[offset + 2]);
            var existing = vertexIds.get(key);
            if (existing !== undefined) { return existing; }
            var id = nextVertexId++;
            vertexIds.set(key, id);
            return id;
        }
        for (var triangleOffset = 0; triangleOffset < triangles.length; triangleOffset += 9) {
            var vertices = [vertexId(triangleOffset), vertexId(triangleOffset + 3), vertexId(triangleOffset + 6)];
            if (vertices[0] === vertices[1] || vertices[1] === vertices[2] || vertices[2] === vertices[0]) {
                continue;
            }
            for (var edge = 0; edge < 3; edge += 1) {
                var first = vertices[edge];
                var second = vertices[(edge + 1) % 3];
                var minimum = Math.min(first, second);
                var maximum = Math.max(first, second);
                // Cantor pairing stays below Number.MAX_SAFE_INTEGER for the admitted
                // vertex budget and avoids retaining two long coordinate strings per edge.
                var key = (maximum * (maximum + 1) / 2) + minimum;
                var count = (edgeCounts.get(key) || 0) + 1;
                edgeCounts.set(key, count);
            }
        }
        if (Array.from(edgeCounts.values()).some(function (count) { return count === 1; })) {
            throw new Error('geometry_requires_review');
        }
    }

    function sectionSegments(triangles, triangleOffsets, plane) {
        var unique = new Map();
        function intersectionKey(firstOffset, secondOffset, planeZ) {
            var first = point3dKey(triangles[firstOffset], triangles[firstOffset + 1], triangles[firstOffset + 2]);
            var second = point3dKey(triangles[secondOffset], triangles[secondOffset + 1], triangles[secondOffset + 2]);
            // A slice through a vertex belongs to that vertex. Other intersections
            // belong to a shared mesh edge, not a rounded XY coordinate that may
            // merge distinct nearby closed contours.
            if (triangles[firstOffset + 2] === planeZ) { return 'v:' + first; }
            if (triangles[secondOffset + 2] === planeZ) { return 'v:' + second; }
            return first < second ? 'e:' + first + '|' + second : 'e:' + second + '|' + first;
        }
        triangleOffsets.forEach(function (triangleOffset) {
            var intersections = [];
            var keys = [];
            for (var edge = 0; edge < 3; edge += 1) {
                var firstOffset = triangleOffset + (edge * 3);
                var secondOffset = triangleOffset + (((edge + 1) % 3) * 3);
                var firstZ = triangles[firstOffset + 2];
                var secondZ = triangles[secondOffset + 2];
                var crosses = (firstZ <= plane && secondZ > plane)
                    || (secondZ <= plane && firstZ > plane);
                if (!crosses) { continue; }
                // Adjacent triangles traverse their shared edge in opposite directions.
                // Interpolating from opposite ends can put an identical crossing on
                // different sides of the 0.001 mm point-key rounding boundary, leaving
                // an otherwise closed STEP section with two dangling endpoints.
                var lowerOffset = firstZ < secondZ ? firstOffset : secondOffset;
                var upperOffset = firstZ < secondZ ? secondOffset : firstOffset;
                var fraction = (plane - triangles[lowerOffset + 2])
                    / (triangles[upperOffset + 2] - triangles[lowerOffset + 2]);
                intersections.push([
                    triangles[lowerOffset] + ((triangles[upperOffset] - triangles[lowerOffset]) * fraction),
                    triangles[lowerOffset + 1] + ((triangles[upperOffset + 1] - triangles[lowerOffset + 1]) * fraction),
                ]);
                keys.push(intersectionKey(lowerOffset, upperOffset, plane));
            }
            if (intersections.length !== 2) { return; }
            var firstKey = keys[0];
            var secondKey = keys[1];
            if (firstKey === secondKey) { return; }
            var ordered = firstKey < secondKey
                ? [firstKey, secondKey, intersections[0], intersections[1]]
                : [secondKey, firstKey, intersections[1], intersections[0]];
            unique.set(ordered[0] + '|' + ordered[1], {
                firstKey: ordered[0], secondKey: ordered[1], first: ordered[2], second: ordered[3],
            });
        });
        return Array.from(unique.values());
    }

    function buildLoops(segments) {
        var adjacency = new Map();
        function add(key, index) {
            if (!adjacency.has(key)) { adjacency.set(key, []); }
            adjacency.get(key).push(index);
        }
        segments.forEach(function (segment, index) {
            add(segment.firstKey, index);
            add(segment.secondKey, index);
        });
        if (Array.from(adjacency.values()).some(function (edges) { return edges.length !== 2; })) {
            throw new Error('geometry_requires_review');
        }
        var visited = new Uint8Array(segments.length);
        var loops = [];
        for (var startIndex = 0; startIndex < segments.length; startIndex += 1) {
            if (visited[startIndex]) { continue; }
            var start = segments[startIndex];
            var startKey = start.firstKey;
            var currentKey = startKey;
            var edgeIndex = startIndex;
            var loop = [];
            while (loop.length <= segments.length) {
                var edge = segments[edgeIndex];
                visited[edgeIndex] = 1;
                var atFirst = edge.firstKey === currentKey;
                loop.push(atFirst ? edge.first : edge.second);
                currentKey = atFirst ? edge.secondKey : edge.firstKey;
                if (currentKey === startKey) { break; }
                var candidates = adjacency.get(currentKey) || [];
                edgeIndex = candidates.find(function (candidate) { return !visited[candidate]; });
                if (edgeIndex === undefined) { throw new Error('geometry_requires_review'); }
            }
            if (loop.length < 3 || currentKey !== startKey) { throw new Error('geometry_requires_review'); }
            loops.push(loop);
        }
        return loops;
    }

    function signedArea(loop) {
        var twiceArea = 0;
        for (var index = 0; index < loop.length; index += 1) {
            var current = loop[index];
            var next = loop[(index + 1) % loop.length];
            twiceArea += (current[0] * next[1]) - (next[0] * current[1]);
        }
        return twiceArea / 2;
    }

    function perimeter(loop) {
        var length = 0;
        for (var index = 0; index < loop.length; index += 1) {
            var current = loop[index];
            var next = loop[(index + 1) % loop.length];
            length += Math.hypot(next[0] - current[0], next[1] - current[1]);
        }
        return length;
    }

    function boundsOf(triangles) {
        var bounds = { minX: Infinity, minY: Infinity, minZ: Infinity, maxX: -Infinity, maxY: -Infinity, maxZ: -Infinity };
        for (var offset = 0; offset < triangles.length; offset += 3) {
            bounds.minX = Math.min(bounds.minX, triangles[offset]);
            bounds.minY = Math.min(bounds.minY, triangles[offset + 1]);
            bounds.minZ = Math.min(bounds.minZ, triangles[offset + 2]);
            bounds.maxX = Math.max(bounds.maxX, triangles[offset]);
            bounds.maxY = Math.max(bounds.maxY, triangles[offset + 1]);
            bounds.maxZ = Math.max(bounds.maxZ, triangles[offset + 2]);
        }
        return bounds;
    }

    function buildLayers(triangles, layerHeight) {
        var bounds = boundsOf(triangles);
        var height = bounds.maxZ - bounds.minZ;
        var layerCount = Math.ceil((height / layerHeight) - EPSILON);
        if (!(layerCount > 0) || layerCount > MAX_LAYERS) { throw new Error('layer_budget_exceeded'); }
        var workload = 0;
        var byMinimum = [];
        var byMaximum = [];
        for (var triangleOffset = 0; triangleOffset < triangles.length; triangleOffset += 9) {
            var minimum = Math.min(triangles[triangleOffset + 2], triangles[triangleOffset + 5], triangles[triangleOffset + 8]);
            var maximum = Math.max(triangles[triangleOffset + 2], triangles[triangleOffset + 5], triangles[triangleOffset + 8]);
            workload += Math.max(1, Math.ceil((maximum - minimum) / layerHeight) + 1);
            byMinimum.push({ offset: triangleOffset, z: minimum });
            byMaximum.push({ offset: triangleOffset, z: maximum });
        }
        if (workload > MAX_LAYER_TRIANGLE_INTERSECTIONS) {
            throw new Error('geometry_complexity_exceeded');
        }
        byMinimum.sort(function (first, second) { return first.z - second.z; });
        byMaximum.sort(function (first, second) { return first.z - second.z; });
        var active = new Set();
        var minimumCursor = 0;
        var maximumCursor = 0;
        var layers = [];
        for (var index = 0; index < layerCount; index += 1) {
            var bottom = bounds.minZ + (index * layerHeight);
            var thickness = Math.min(layerHeight, bounds.maxZ - bottom);
            var plane = bottom + (thickness / 2);
            while (minimumCursor < byMinimum.length && byMinimum[minimumCursor].z <= plane) {
                active.add(byMinimum[minimumCursor].offset);
                minimumCursor += 1;
            }
            while (maximumCursor < byMaximum.length && byMaximum[maximumCursor].z <= plane) {
                active.delete(byMaximum[maximumCursor].offset);
                maximumCursor += 1;
            }
            var segments = sectionSegments(triangles, active, plane);
            if (segments.length === 0) { throw new Error('geometry_slice_empty'); }
            var loops = buildLoops(segments);
            layers.push({
                z: plane - bounds.minZ,
                thickness: thickness,
                loops: loops,
                area: Math.abs(loops.reduce(function (sum, loop) { return sum + signedArea(loop); }, 0)),
                perimeter: loops.reduce(function (sum, loop) { return sum + perimeter(loop); }, 0),
                loopCount: loops.length,
            });
        }
        return { layers: layers, bounds: bounds };
    }

    function createMask(cellCount) { return new Uint32Array(Math.ceil(cellCount / 32)); }
    function maskHas(mask, index) { return (mask[index >>> 5] & (1 << (index & 31))) !== 0; }
    function maskSet(mask, index) { mask[index >>> 5] |= 1 << (index & 31); }

    function rasterizeLoops(loops, grid) {
        var mask = createMask(grid.columns * grid.rows);
        for (var y = 0; y < grid.rows; y += 1) {
            var py = grid.minY + ((y + 0.5) * grid.cellDepth);
            var crossings = [];
            loops.forEach(function (loop) {
                for (var current = 0, previous = loop.length - 1; current < loop.length; previous = current++) {
                    var first = loop[previous];
                    var second = loop[current];
                    if (!((first[1] <= py && second[1] > py) || (second[1] <= py && first[1] > py))) { continue; }
                    crossings.push(first[0] + ((second[0] - first[0]) * (py - first[1]) / (second[1] - first[1])));
                }
            });
            crossings.sort(function (a, b) { return a - b; });
            for (var index = 0; index + 1 < crossings.length; index += 2) {
                var start = Math.max(0, Math.ceil(((crossings[index] - grid.minX) / grid.cellWidth) - 0.5));
                var end = Math.min(grid.columns - 1, Math.floor(((crossings[index + 1] - grid.minX) / grid.cellWidth) - 0.5));
                for (var x = start; x <= end; x += 1) { maskSet(mask, (y * grid.columns) + x); }
            }
        }
        return mask;
    }

    function adaptiveGrid(bounds, build) {
        var width = Math.max(EPSILON, bounds.maxX - bounds.minX);
        var depth = Math.max(EPSILON, bounds.maxY - bounds.minY);
        var support = build.support || {};
        var smallestLineWidth = Math.min(
            positive(build.outerWallLineWidthMm, 0.42),
            positive(build.innerWallLineWidthMm, 0.45),
            positive(build.solidInfillLineWidthMm, 0.42),
            positive(support.bodyLineWidthMm, 0.45),
            positive(support.interfaceLineWidthMm, 0.42));
        var targetCell = Math.max(0.05, smallestLineWidth / 2);
        var columns = Math.max(MIN_GRID_AXIS, Math.min(MAX_GRID_AXIS, Math.ceil(width / targetCell)));
        var rows = Math.max(MIN_GRID_AXIS, Math.min(MAX_GRID_AXIS, Math.ceil(depth / targetCell)));
        if (columns * rows > MAX_GRID_CELLS) {
            var scale = Math.sqrt(MAX_GRID_CELLS / (columns * rows));
            columns = Math.max(MIN_GRID_AXIS, Math.floor(columns * scale));
            rows = Math.max(MIN_GRID_AXIS, Math.floor(rows * scale));
        }
        return {
            minX: bounds.minX, minY: bounds.minY, columns: columns, rows: rows,
            cellWidth: width / columns, cellDepth: depth / rows,
        };
    }

    function buildOccupancy(layers, bounds, build) {
        var support = build.support || {};
        var grid = adaptiveGrid(bounds, build);
        var cellWidth = grid.cellWidth;
        var cellDepth = grid.cellDepth;
        var cellArea = cellWidth * cellDepth;
        var masks = layers.map(function (layer) {
            var mask = rasterizeLoops(layer.loops, grid);
            // STL loop winding is not reliable across disconnected shells. The even-odd
            // occupancy mask is orientation-independent, so it owns printable cross-section
            // area instead of allowing signed loop areas to cancel valid material.
            layer.area = sumMask(mask) * cellArea;
            return mask;
        });
        var body = layers.map(function () { return createMask(grid.columns * grid.rows); });
        var interfaces = layers.map(function () { return createMask(grid.columns * grid.rows); });
        if (!support.enabled) { return { model: masks, body: body, interfaces: interfaces, cellArea: cellArea, grid: grid }; }
        var angle = positive(support.overhangAngleFromHorizontalDegrees, 30) * Math.PI / 180;
        var reach = positive(layers[0].thickness, 0.2) / Math.tan(angle);
        // A whole coarse cell can be much wider than the physical overhang reach.
        // Rounding upward therefore treats unsupported material as anchored and erases
        // support. Only complete cell offsets that fit inside the reach are admissible.
        var toleranceX = Math.max(0, Math.floor(reach / cellWidth));
        var toleranceY = Math.max(0, Math.floor(reach / cellDepth));
        var gapLayers = Math.max(1, Math.ceil(finite(support.topZGapMm, layers[0].thickness) / layers[0].thickness));
        function hasPrevious(mask, x, y) {
            for (var oy = -toleranceY; oy <= toleranceY; oy += 1) {
                for (var ox = -toleranceX; ox <= toleranceX; ox += 1) {
                    var sx = x + ox;
                    var sy = y + oy;
                    if (sx >= 0 && sx < grid.columns && sy >= 0 && sy < grid.rows
                        && maskHas(mask, (sy * grid.columns) + sx)) { return true; }
                }
            }
            return false;
        }
        for (var layerIndex = 1; layerIndex < layers.length; layerIndex += 1) {
            for (var y = 0; y < grid.rows; y += 1) {
                for (var x = 0; x < grid.columns; x += 1) {
                    var cell = (y * grid.columns) + x;
                    if (!maskHas(masks[layerIndex], cell) || hasPrevious(masks[layerIndex - 1], x, y)) { continue; }
                    var contactLayer = layerIndex - gapLayers;
                    if (contactLayer < 0) { continue; }
                    for (var supportLayer = contactLayer; supportLayer >= 0; supportLayer -= 1) {
                        if (maskHas(masks[supportLayer], cell)) { break; }
                        maskSet(body[supportLayer], cell);
                        if (contactLayer - supportLayer < Math.max(1, Number(support.interfaceLayers) || 1)) {
                            maskSet(interfaces[supportLayer], cell);
                        }
                    }
                }
            }
        }
        return { model: masks, body: body, interfaces: interfaces, cellArea: cellArea, grid: grid };
    }

    function stadiumArea(width, height) {
        return (width * height) - (((4 - Math.PI) * height * height) / 4);
    }

    function moveSeconds(length, acceleration, speed) {
        if (!(length > 0)) { return 0; }
        var accelerateDistance = (speed * speed) / (2 * acceleration);
        if ((2 * accelerateDistance) <= length) {
            return (2 * speed / acceleration) + ((length - (2 * accelerateDistance)) / speed);
        }
        return 2 * Math.sqrt(length / acceleration);
    }

    function roleTemplate() {
        return {
            outerWall: { depositedMm3: 0, seconds: 0 },
            innerWall: { depositedMm3: 0, seconds: 0 },
            internalSolid: { depositedMm3: 0, seconds: 0 },
            sparseInfill: { depositedMm3: 0, seconds: 0 },
            bridge: { depositedMm3: 0, seconds: 0 },
            supportBody: { depositedMm3: 0, seconds: 0 },
            supportInterface: { depositedMm3: 0, seconds: 0 },
            adhesion: { depositedMm3: 0, seconds: 0 },
        };
    }

    function sumMask(mask) {
        var count = 0;
        for (var index = 0; index < mask.length; index += 1) {
            var word = mask[index] >>> 0;
            word -= (word >>> 1) & 0x55555555;
            word = (word & 0x33333333) + ((word >>> 2) & 0x33333333);
            count += (((word + (word >>> 4)) & 0x0f0f0f0f) * 0x01010101) >>> 24;
        }
        return count;
    }

    function maskPaths(mask, excluded, occupancy, lineWidth, density, vertical) {
        if (!(density > 0)) { return []; }
        var grid = occupancy.grid;
        var spacing = lineWidth / Math.min(1, density);
        var paths = [];
        var axisMinimum = vertical ? grid.minX : grid.minY;
        var axisMaximum = axisMinimum + (vertical
            ? grid.columns * grid.cellWidth
            : grid.rows * grid.cellDepth);
        for (var coordinate = axisMinimum + (spacing / 2); coordinate < axisMaximum; coordinate += spacing) {
            var fixed = vertical
                ? Math.max(0, Math.min(grid.columns - 1, Math.floor((coordinate - grid.minX) / grid.cellWidth)))
                : Math.max(0, Math.min(grid.rows - 1, Math.floor((coordinate - grid.minY) / grid.cellDepth)));
            var limit = vertical ? grid.rows : grid.columns;
            var cursor = 0;
            while (cursor < limit) {
                function occupied(index) {
                    var cell = vertical ? (index * grid.columns) + fixed : (fixed * grid.columns) + index;
                    return maskHas(mask, cell) && !(excluded && maskHas(excluded, cell));
                }
                while (cursor < limit && !occupied(cursor)) { cursor += 1; }
                var start = cursor;
                while (cursor < limit && occupied(cursor)) { cursor += 1; }
                if (cursor <= start) { continue; }
                var first = vertical
                    ? [coordinate, grid.minY + (start * grid.cellDepth)]
                    : [grid.minX + (start * grid.cellWidth), coordinate];
                var second = vertical
                    ? [coordinate, grid.minY + (cursor * grid.cellDepth)]
                    : [grid.minX + (cursor * grid.cellWidth), coordinate];
                paths.push({ start: first, end: second, length: Math.hypot(second[0] - first[0], second[1] - first[1]) });
            }
        }
        return paths;
    }

    function loopPaths(layer, targetLength) {
        if (!(targetLength > 0) || layer.loops.length === 0) { return []; }
        var lengths = layer.loops.map(perimeter);
        var total = lengths.reduce(function (sum, value) { return sum + value; }, 0);
        return layer.loops.map(function (loop, index) {
            return { start: loop[0], end: loop[0], length: total > 0 ? targetLength * lengths[index] / total : 0 };
        }).filter(function (path) { return path.length > 0; });
    }

    function scalePaths(paths, targetLength, fallback) {
        if (!(targetLength > 0)) { return []; }
        var total = paths.reduce(function (sum, path) { return sum + path.length; }, 0);
        if (!(total > 0)) { return [{ start: fallback, end: fallback, length: targetLength }]; }
        var scale = targetLength / total;
        return paths.map(function (path) {
            return { start: path.start, end: path.end, length: path.length * scale };
        });
    }

    function addRolePaths(target, source, role, width) {
        source.forEach(function (path) {
            if (path.length > 0) { target.push({ role: role, width: width, start: path.start, end: path.end, length: path.length }); }
        });
    }

    function simulateFdm(request) {
        var triangles = request.triangles || flattenTriangles(request.meshes);
        var build = request.build || {};
        var layerHeight = positive(build.layerHeightMm, 0.2);
        var sliced = request.sliced || buildLayers(triangles, layerHeight);
        var layers = sliced.layers;
        var supportProfile = build.support || {};
        if (supportProfile.enabled && String(supportProfile.mode || 'normal').toLowerCase() !== 'normal') {
            throw new Error('support_mode_unsupported');
        }
        var occupancy = request.occupancy || buildOccupancy(layers, sliced.bounds, build);
        var roles = roleTemplate();
        var layerRows = [];
        var layerPaths = [];
        var wallLoops = Math.max(1, Math.round(positive(build.wallLoops, 2)));
        var outerWidth = Math.max(layerHeight, positive(build.outerWallLineWidthMm, 0.42));
        var innerWidth = Math.max(layerHeight, positive(build.innerWallLineWidthMm, 0.45));
        var solidWidth = Math.max(layerHeight, positive(build.solidInfillLineWidthMm, 0.42));
        var sparseDensity = Math.max(0, Math.min(1, finite(build.sparseInfillDensity, 0.15)));
        var topLayers = Math.max(0, Math.round(finite(build.topShellLayers, 5)));
        var bottomLayers = Math.max(0, Math.round(finite(build.bottomShellLayers, 3)));
        layers.forEach(function (layer, layerIndex) {
            var row = roleTemplate();
            var available = layer.area * layer.thickness;
            var outer = Math.min(available, layer.perimeter * outerWidth * layer.thickness);
            row.outerWall.depositedMm3 = outer;
            available -= outer;
            var innerLength = 0;
            for (var wall = 1; wall < wallLoops; wall += 1) {
                innerLength += Math.max(0, layer.perimeter - (2 * Math.PI * innerWidth * wall * Math.max(1, layer.loopCount)));
            }
            var inner = Math.min(available, innerLength * innerWidth * layer.thickness);
            row.innerWall.depositedMm3 = inner;
            available -= inner;
            var solid = layerIndex < bottomLayers || layerIndex >= layers.length - topLayers;
            if (solid) { row.internalSolid.depositedMm3 = available; }
            else { row.sparseInfill.depositedMm3 = available * sparseDensity; }
            if (layerIndex === 0 && positive(build.brimWidthMm, 0) > 0) {
                row.adhesion.depositedMm3 = layer.perimeter * positive(build.brimWidthMm, 0) * layer.thickness;
            }
            var paths = [];
            var fallback = layer.loops[0] && layer.loops[0][0] ? layer.loops[0][0] : [sliced.bounds.minX, sliced.bounds.minY];
            Object.keys(row).forEach(function (role) {
                if (role === 'supportBody' || role === 'supportInterface') { return; }
                var width = role === 'outerWall' || role === 'adhesion' ? outerWidth
                    : role === 'innerWall' ? innerWidth
                    : solidWidth;
                var targetLength = row[role].depositedMm3 / stadiumArea(width, layer.thickness);
                var candidates = role === 'outerWall' || role === 'innerWall' || role === 'adhesion'
                    ? loopPaths(layer, targetLength)
                    : scalePaths(maskPaths(
                        occupancy.model[layerIndex], null, occupancy, width,
                        role === 'sparseInfill' ? sparseDensity : 1,
                        (layerIndex & 1) === 1), targetLength, fallback);
                addRolePaths(paths, candidates, role, width);
            });
            var bodyWidth = Math.max(layer.thickness, positive(supportProfile.bodyLineWidthMm, 0.45));
            var interfaceWidth = Math.max(layer.thickness, positive(supportProfile.interfaceLineWidthMm, 0.42));
            var bodyPaths = maskPaths(
                occupancy.body[layerIndex], occupancy.interfaces[layerIndex], occupancy, bodyWidth,
                Math.max(0, Math.min(1, finite(supportProfile.bodyDensity, 0.15))), (layerIndex & 1) === 1);
            var interfacePaths = maskPaths(
                occupancy.interfaces[layerIndex], null, occupancy, interfaceWidth,
                Math.max(0, Math.min(1, finite(supportProfile.interfaceDensity, 0.8))), (layerIndex & 1) === 1);
            row.supportBody.depositedMm3 = bodyPaths.reduce(function (sum, path) { return sum + path.length * bodyWidth * layer.thickness; }, 0);
            row.supportInterface.depositedMm3 = interfacePaths.reduce(function (sum, path) { return sum + path.length * interfaceWidth * layer.thickness; }, 0);
            addRolePaths(paths, bodyPaths, 'supportBody', bodyWidth);
            addRolePaths(paths, interfacePaths, 'supportInterface', interfaceWidth);
            Object.keys(row).forEach(function (role) {
                roles[role].depositedMm3 += row[role].depositedMm3;
            });
            layerRows.push(row);
            layerPaths.push(paths);
        });

        var speeds = build.speeds || {};
        var motion = build.motion || {};
        var acceleration = positive(motion.printingAccelerationMmPerSecondSquared, 20000);
        var maximumXY = positive(motion.maximumXYSpeedMmPerSecond, 500);
        var maximumFlow = positive(motion.maximumVolumetricFlowMm3PerSecond, 12);
        var minimumLayerTime = Math.max(0, finite(motion.minimumLayerTimeSeconds, 0));
        var speedByRole = {
            outerWall: positive(speeds.outerWall, 60), innerWall: positive(speeds.innerWall, 150),
            internalSolid: positive(speeds.solidInfill, 180), sparseInfill: positive(speeds.sparseInfill, 180),
            bridge: positive(speeds.bridge, 50), supportBody: positive(speeds.supportBody, 150),
            supportInterface: positive(speeds.supportInterface, 80), adhesion: positive(speeds.outerWall, 60),
        };
        var travelSeconds = 0;
        var coolingSeconds = 0;
        var previousEnd = null;
        var previousZ = 0;
        var travelAcceleration = positive(motion.travelAccelerationMmPerSecondSquared, 9000);
        var travelSpeed = Math.min(maximumXY, positive(speeds.travel, 500));
        var maximumZ = positive(motion.maximumZSpeedMmPerSecond, 20);
        var retractLength = Math.max(0, finite(motion.retractLengthMm, 0));
        var retractSpeed = positive(motion.retractSpeedMmPerSecond, 40);
        var firstLayerMultiplier = Math.max(EPSILON, finite(motion.firstLayerSpeedMultiplier, 1));
        layerPaths.forEach(function (paths, layerIndex) {
            var row = layerRows[layerIndex];
            var layerTravel = 0;
            var layerDeposition = 0;
            paths.forEach(function (path) {
                var z = layers[layerIndex].z;
                if (previousEnd) {
                    var distance = Math.hypot(path.start[0] - previousEnd[0], path.start[1] - previousEnd[1]);
                    var seconds = Math.max(moveSeconds(distance, travelAcceleration, travelSpeed), Math.abs(z - previousZ) / maximumZ);
                    if (distance > EPSILON && retractLength > 0) { seconds += (2 * retractLength) / retractSpeed; }
                    layerTravel += seconds;
                }
                var area = stadiumArea(path.width, layers[layerIndex].thickness);
                var cap = Math.min(maximumXY, speedByRole[path.role], maximumFlow / area);
                if (layerIndex === 0) { cap *= firstLayerMultiplier; }
                cap = Math.max(EPSILON, cap);
                var pathSeconds = moveSeconds(path.length, acceleration, cap);
                row[path.role].seconds += pathSeconds;
                roles[path.role].seconds += pathSeconds;
                layerDeposition += pathSeconds;
                previousEnd = path.end;
                previousZ = z;
            });
            travelSeconds += layerTravel;
            coolingSeconds += Math.max(0, minimumLayerTime - layerTravel - layerDeposition);
        });
        var preparationSeconds = Math.max(0, finite(motion.setupSeconds, 30))
            + Math.max(Math.max(0, finite(motion.nozzleHeatSeconds, 60)), Math.max(0, finite(motion.bedHeatSeconds, 90)))
            + Math.max(0, finite(motion.machinePrepareCompensationSeconds, 0))
            + Math.max(0, finite(motion.machineLoadFilamentSeconds, 0));
        var roleNames = Object.keys(roles);
        var totalDeposited = roleNames.reduce(function (sum, role) { return sum + roles[role].depositedMm3; }, 0);
        var supportDeposited = roles.supportBody.depositedMm3 + roles.supportInterface.depositedMm3;
        var depositionSeconds = roleNames.reduce(function (sum, role) { return sum + roles[role].seconds; }, 0);
        return {
            analysisVersion: ANALYSIS_VERSION,
            materialId: request.materialId,
            layerCount: layers.length,
            roles: roles,
            totalDepositedMm3: totalDeposited,
            supportDepositedMm3: supportDeposited,
            travelSeconds: travelSeconds,
            coolingSeconds: coolingSeconds,
            preparationSeconds: preparationSeconds,
            totalSeconds: depositionSeconds + travelSeconds + coolingSeconds + preparationSeconds,
        };
    }

    function interpolate(values, fraction, fallback) {
        if (!Array.isArray(values) || values.length === 0) { return fallback; }
        if (values.length === 1) { return Math.abs(Number(values[0])); }
        var position = Math.max(0, Math.min(1, fraction)) * (values.length - 1);
        var lower = Math.floor(position);
        var upper = Math.min(values.length - 1, lower + 1);
        var ratio = position - lower;
        return (Math.abs(Number(values[lower])) * (1 - ratio)) + (Math.abs(Number(values[upper])) * ratio);
    }

    function simulateResin(request) {
        var layerHeight = 0.05;
        var supportDensity = 0.15;
        var height = positive(request.heightMm, 0);
        if (!(height > 0)) { throw new Error('geometry_empty'); }
        var modelLayerCount = Math.max(1, Math.ceil(height / layerHeight));
        var uniformArea = Math.abs(finite(request.volumeMm3, 0)) / height;
        var areas = [];
        for (var index = 0; index < modelLayerCount; index += 1) {
            areas.push(interpolate(request.areaProfileMm2, (index + 0.5) / modelLayerCount, uniformArea));
        }
        var demand = new Array(modelLayerCount).fill(0);
        var explicit = Array.isArray(request.unsupportedAreaProfileMm2) && request.unsupportedAreaProfileMm2.length > 0;
        if (explicit) {
            request.unsupportedAreaProfileMm2.forEach(function (area, profileIndex, profile) {
                if (profileIndex === 0) { return; }
                var layer = Math.round(profileIndex / (profile.length - 1) * (modelLayerCount - 1));
                demand[layer] += Math.abs(Number(area));
            });
        } else {
            for (var layer = 1; layer < modelLayerCount; layer += 1) {
                demand[layer] = Math.max(0, areas[layer] - areas[layer - 1]);
            }
        }
        var supportMm3 = 0;
        demand.forEach(function (area, layer) {
            if (!(area > 0) || layer <= 0) { return; }
            supportMm3 += area * supportDensity * layerHeight * layer;
        });
        var modelMl = Math.abs(finite(request.volumeMm3, 0)) / 1000;
        var supportMl = supportMm3 / 1000;
        var requiresSupport = supportMl > 0;
        var raftThickness = requiresSupport ? 1 : 0;
        var raftMl = requiresSupport ? Math.max(0, finite(request.footprintMm2, 0)) * 1.1 * raftThickness / 1000 : 0;
        var wasteMl = (modelMl + supportMl + raftMl) * 0.1;
        var layerCount = Math.max(1, Math.ceil((height + raftThickness) / layerHeight));
        var bottomLayers = Math.min(layerCount, 6);
        var normalCycle = 2.3 + (8 / 1) + (8 / 2.5);
        var printMinutes = ((layerCount * normalCycle) + (bottomLayers * (32.5 - 2.3))) / 60;
        return {
            analysisVersion: 'resin-browser-physical-v1',
            layerCount: layerCount,
            printMinutes: printMinutes,
            modelResinMl: modelMl,
            supportResinMl: supportMl,
            raftResinMl: raftMl,
            wasteResinMl: wasteMl,
            totalResinMl: modelMl + supportMl + raftMl + wasteMl,
        };
    }

    var registeredModels = new Map();
    var slicedGeometry = new Map();
    var api = {
        preparePricingTriangles: preparePricingTriangles,
        simulateFdm: simulateFdm,
        simulateResin: simulateResin,
    };
    scope.MalievAdditiveSimulation = api;
    scope.onmessage = function (event) {
        var data = event.data || {};
        try {
            if (data.action === 'register') {
                registeredModels.set(String(data.modelId), data.meshes || []);
                scope.postMessage({ jobId: data.jobId, success: true });
                return;
            }
            if (data.action === 'dispose') {
                registeredModels.delete(String(data.modelId));
                Array.from(slicedGeometry.keys()).forEach(function (key) {
                    if (key.indexOf(String(data.modelId) + '|') === 0) { slicedGeometry.delete(key); }
                });
                scope.postMessage({ jobId: data.jobId, success: true });
                return;
            }
            var request = data.request || {};
            if (data.process !== 'resin') {
                var registeredMeshes = registeredModels.get(String(data.modelId));
                if (!registeredMeshes) { throw new Error('geometry_not_registered'); }
                var build = request.build || {};
                var geometryKey = String(data.modelId) + '|' + JSON.stringify({
                    layerHeightMm: build.layerHeightMm,
                    wallLoops: build.wallLoops,
                    sparseInfillDensity: build.sparseInfillDensity,
                    outerWallLineWidthMm: build.outerWallLineWidthMm,
                    innerWallLineWidthMm: build.innerWallLineWidthMm,
                    sparseInfillLineWidthMm: build.sparseInfillLineWidthMm,
                    solidInfillLineWidthMm: build.solidInfillLineWidthMm,
                    topShellLayers: build.topShellLayers,
                    bottomShellLayers: build.bottomShellLayers,
                    brimWidthMm: build.brimWidthMm,
                    support: build.support
                });
                var cachedGeometry = slicedGeometry.get(geometryKey);
                if (!cachedGeometry) {
                    var layerHeight = positive(build.layerHeightMm, 0.2);
                    var prepared = preparePricingTriangles(registeredMeshes, { layerHeightMm: layerHeight });
                    var sliced = buildLayers(prepared.triangles, layerHeight);
                    cachedGeometry = {
                        sliced: sliced,
                        occupancy: buildOccupancy(sliced.layers, sliced.bounds, build),
                        analysis: prepared,
                    };
                    slicedGeometry.set(geometryKey, cachedGeometry);
                }
                request.triangles = cachedGeometry.analysis.triangles;
                request.sliced = cachedGeometry.sliced;
                request.occupancy = cachedGeometry.occupancy;
            }
            var result = data.process === 'resin' ? simulateResin(request) : simulateFdm(request);
            if (data.process !== 'resin') {
                result.sourceTriangleCount = cachedGeometry.analysis.sourceTriangleCount;
                result.analysisTriangleCount = cachedGeometry.analysis.analysisTriangleCount;
                result.analysisMeshSimplified = cachedGeometry.analysis.simplified;
            }
            scope.postMessage({ jobId: data.jobId, success: true, result: result });
        } catch (error) {
            scope.postMessage({ jobId: data.jobId, success: false, error: error && error.message ? error.message : 'simulation_failed' });
        }
    };
})(self);
