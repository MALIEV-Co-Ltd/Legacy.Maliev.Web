(function (root) {
    'use strict';

    var QUANTUM = 10000;
    function snap(value) { return Number.isFinite(value) ? Math.round(value * QUANTUM) / QUANTUM : 0; }
    function point(value) { return value && Number.isFinite(value.x) && Number.isFinite(value.y) && Number.isFinite(value.z) ? { x: snap(value.x), y: snap(value.y), z: snap(value.z) } : null; }
    function pointKey(value) { return value.x + ',' + value.y + ',' + value.z; }
    function compare(left, right) { for (var i = 0; i < left.length; i += 1) { if (left[i] < right[i]) { return -1; } if (left[i] > right[i]) { return 1; } } return 0; }
    function direction(value) {
        var result = point(value), length;
        if (!result || !((length = Math.hypot(result.x, result.y, result.z)) > 0)) { return null; }
        result.x = snap(result.x / length); result.y = snap(result.y / length); result.z = snap(result.z / length);
        if ((result.x || result.y || result.z) < 0) { result.x = -result.x; result.y = -result.y; result.z = -result.z; }
        return result;
    }
    function signedDirection(value, reversed) {
        var result = point(value), length;
        if (!result || !((length = Math.hypot(result.x, result.y, result.z)) > 0)) { return null; }
        var sign = reversed ? -1 : 1;
        return { x: snap(result.x * sign / length), y: snap(result.y * sign / length), z: snap(result.z * sign / length) };
    }
    function canonicalLoop(input, inputEdgeIds) {
        var vertices = (Array.isArray(input) ? input : []).map(point).filter(Boolean);
        if (vertices.length < 1) { return null; }
        var keys = vertices.map(pointKey), variants = [];
        [keys, keys.slice().reverse()].forEach(function (candidate) { for (var start = 0; start < candidate.length; start += 1) { variants.push(candidate.slice(start).concat(candidate.slice(0, start))); } });
        variants.sort(compare);
        var output = { vertices: variants[0].map(function (key) { var values = key.split(',').map(Number); return { x: values[0], y: values[1], z: values[2] }; }), signature: variants[0].join('|') };
        var edgeIds = (Array.isArray(inputEdgeIds) ? inputEdgeIds : []).filter(function (id) { return Number.isSafeInteger(id) && id > 0; }).map(String).sort();
        if (edgeIds.length) { output.edgeIds = edgeIds; }
        return output;
    }
    function bounds(vertices) {
        if (!vertices.length) { return null; }
        return vertices.reduce(function (result, vertex) { ['x', 'y', 'z'].forEach(function (axis) { result.min[axis] = Math.min(result.min[axis], vertex[axis]); result.max[axis] = Math.max(result.max[axis], vertex[axis]); }); return result; }, { min: { x: Infinity, y: Infinity, z: Infinity }, max: { x: -Infinity, y: -Infinity, z: -Infinity } });
    }
    function loopBounds(loops) { return bounds(loops.flatMap(function (loop) { return loop.vertices; })); }
    function boundsKey(value) { return value ? ['x', 'y', 'z'].map(function (axis) { return value.min[axis] + ':' + value.max[axis]; }).join('|') : ''; }
    function surface(input, loops) {
        input = input || {};
        var output = { kind: typeof input.kind === 'string' ? input.kind.toLowerCase() : 'unresolved' }, center = point(input.centerMm), axis = direction(input.axis);
        if (center) { output.centerMm = center; } if (axis) { output.axis = axis; }
        ['radiusMm', 'minorRadiusMm', 'halfAngleRadians', 'angularSpanRadians'].forEach(function (name) { if (Number.isFinite(input[name])) { output[name] = snap(input[name]); } });
        if (output.kind === 'cylinder' && (!Number.isFinite(output.angularSpanRadians)
            || Math.abs(output.angularSpanRadians - Math.PI * 2) < 0.0001) && loops.some(function (loop) {
            return values(loop.edgeIds).length >= 3 && new Set(loop.edgeIds).size < loop.edgeIds.length;
        })) {
            output.angularSpanRadians = snap(Math.PI * 2);
            output.closureEvidence = 'brep_periodic_seam';
        }
        var faceBounds = loopBounds(loops); if (faceBounds) { output.boundsMm = faceBounds; } return output;
    }
    function supportFace(input) {
        var loops = (Array.isArray(input && input.loops) ? input.loops : []).map(function (loop) { return canonicalLoop(loop && loop.vertices, loop && loop.edgeIds); }).filter(Boolean).sort(function (left, right) { return left.signature.localeCompare(right.signature); });
        if (!loops.length) { return null; }
        var faceSurface = surface(input, loops);
        var matchVertices = (Array.isArray(input && input.matchVertices) ? input.matchVertices : []).map(point).filter(Boolean);
        if (matchVertices.length && faceSurface.kind === 'swept') {
            var unique = new Map(); matchVertices.forEach(function (vertex) { unique.set(pointKey(vertex), vertex); });
            matchVertices = Array.from(unique.values()).sort(function (left, right) { return pointKey(left).localeCompare(pointKey(right)); });
            faceSurface.boundsMm = bounds(matchVertices);
        }
        var normal = faceSurface.kind === 'plane' ? signedDirection(input.axis, input.orientation === 'reversed') : null;
        if (normal) { faceSurface.normal = normal; }
        return faceSurface.kind === 'unresolved' ? null : { surface: faceSurface, loops: loops, orientation: input.orientation === 'reversed' ? 'reversed' : 'forward', _matchVertices: matchVertices };
    }
    function semanticKey(face) { return JSON.stringify({ surface: face.surface, loops: face.loops.map(function (loop) { return loop.signature; }), orientation: face.orientation }); }
    function values(value) { return Array.isArray(value) || ArrayBuffer.isView(value) ? value : []; }
    function finitePoint(point) {
        return point && Number.isFinite(point.x) && Number.isFinite(point.y) && Number.isFinite(point.z);
    }
    function positions(mesh) { var attribute = mesh && mesh.attributes && mesh.attributes.position, result = values(attribute && attribute.array); return result.length ? result : values(mesh && mesh.position); }
    function indexes(mesh) { var index = mesh && mesh.index, result = values(index && index.array); return result.length ? result : values(index); }
    function ranges(mesh) { return Array.isArray(mesh && mesh.brep_faces) ? mesh.brep_faces : (Array.isArray(mesh && mesh.cadFaceRanges) ? mesh.cadFaceRanges : []); }
    function faceVertices(mesh, range) {
        if (!Number.isSafeInteger(range && range.first) || !Number.isSafeInteger(range.last) || range.first < 0 || range.last < range.first) { return []; }
        var source = positions(mesh), map = indexes(mesh), result = [];
        for (var triangle = range.first; triangle <= range.last; triangle += 1) for (var corner = 0; corner < 3; corner += 1) {
            var ordinal = triangle * 3 + corner, index = map.length ? map[ordinal] : ordinal, offset = Number(index) * 3;
            if (!Number.isSafeInteger(index) || offset + 2 >= source.length) { return []; }
            result.push(point({ x: Number(source[offset]), y: Number(source[offset + 1]), z: Number(source[offset + 2]) }));
        }
        return result.every(Boolean) ? result : [];
    }
    function boundsMatch(left, right) {
        if (!left || !right) { return false; }
        var tolerance = Math.max(0.001, Math.hypot(left.max.x - left.min.x, left.max.y - left.min.y, left.max.z - left.min.z) * 0.0001);
        return ['x', 'y', 'z'].every(function (axis) { return Math.abs(left.min[axis] - right.min[axis]) <= tolerance && Math.abs(left.max[axis] - right.max[axis]) <= tolerance; });
    }
    function loopVerticesMatch(vertices, loops, faceBounds) {
        var tolerance = Math.max(0.001, Math.hypot(faceBounds.max.x - faceBounds.min.x, faceBounds.max.y - faceBounds.min.y, faceBounds.max.z - faceBounds.min.z) * 0.0001);
        return loops.flatMap(function (loop) { return loop.vertices; }).every(function (supportVertex) {
            return vertices.some(function (faceVertex) {
                return Math.abs(faceVertex.x - supportVertex.x) <= tolerance && Math.abs(faceVertex.y - supportVertex.y) <= tolerance && Math.abs(faceVertex.z - supportVertex.z) <= tolerance;
            });
        });
    }
    function vectorBetween(left, right) { return { x: left.x - right.x, y: left.y - right.y, z: left.z - right.z }; }
    function dot(left, right) { return left.x * right.x + left.y * right.y + left.z * right.z; }
    function boundaryError(vertices, candidate, tolerance) {
        var boundary = candidate.loops.flatMap(function (loop) { return loop.vertices; });
        if (!boundary.length) { return Infinity; }
        return boundary.reduce(function (sum, supportVertex) {
            var nearest = vertices.reduce(function (minimum, faceVertex) {
                return Math.min(minimum, Math.hypot(faceVertex.x - supportVertex.x, faceVertex.y - supportVertex.y, faceVertex.z - supportVertex.z));
            }, Infinity);
            return sum + nearest;
        }, 0) / boundary.length / tolerance;
    }
    function supportError(vertices, candidate, faceBounds) {
        var surface = candidate.surface, scale = Math.max(1, Math.hypot(faceBounds.max.x - faceBounds.min.x, faceBounds.max.y - faceBounds.min.y, faceBounds.max.z - faceBounds.min.z));
        var tolerance = Math.max(0.003, scale * 0.0005), maximum = 0;
        var supportBounds = surface.boundsMm;
        var overlaps = supportBounds && ['x', 'y', 'z'].every(function (axis) { return faceBounds.max[axis] + tolerance >= supportBounds.min[axis] && supportBounds.max[axis] + tolerance >= faceBounds.min[axis]; });
        function boundsError() {
            if (!supportBounds) { return Infinity; }
            return ['x', 'y', 'z'].reduce(function (sum, axis) {
                return sum + Math.abs(faceBounds.min[axis] - supportBounds.min[axis]) + Math.abs(faceBounds.max[axis] - supportBounds.max[axis]);
            }, 0) / scale;
        }
        if (surface.kind === 'plane' && surface.centerMm && surface.axis) {
            if (!overlaps) { return Infinity; }
            vertices.forEach(function (vertex) { maximum = Math.max(maximum, Math.abs(dot(vectorBetween(vertex, surface.centerMm), surface.axis))); });
            return maximum <= tolerance ? maximum / tolerance + boundsError() + boundaryError(vertices, candidate, tolerance) : Infinity;
        }
        if ((surface.kind === 'cylinder' || surface.kind === 'cone') && surface.centerMm && surface.axis && Number.isFinite(surface.radiusMm)) {
            if (!overlaps) { return Infinity; }
            vertices.forEach(function (vertex) {
                var relative = vectorBetween(vertex, surface.centerMm), axial = dot(relative, surface.axis);
                var radial = Math.sqrt(Math.max(0, dot(relative, relative) - axial * axial));
                var expected = surface.radiusMm;
                if (surface.kind === 'cone') {
                    var delta = Math.abs(axial) * Math.tan(surface.halfAngleRadians || 0);
                    maximum = Math.max(maximum, Math.min(Math.abs(radial - (expected + delta)), Math.abs(radial - Math.max(0, expected - delta))));
                } else { maximum = Math.max(maximum, Math.abs(radial - expected)); }
            });
            return maximum <= tolerance ? maximum / tolerance + boundsError() + boundaryError(vertices, candidate, tolerance) : Infinity;
        }
        if (!supportBounds) { return Infinity; }
        var unionMin = 0, unionMax = 0;
        ['x', 'y', 'z'].forEach(function (axis) {
            unionMin += Math.abs(faceBounds.min[axis] - supportBounds.min[axis]);
            unionMax += Math.abs(faceBounds.max[axis] - supportBounds.max[axis]);
        });
        return overlaps ? (unionMin + unionMax) / scale + boundaryError(vertices, candidate, tolerance) : Infinity;
    }
    function matchedFaces(mesh, byBounds, available, used) {
        var output = [];
        for (var index = 0; index < ranges(mesh).length; index += 1) {
            var triangleRange = ranges(mesh)[index], vertices = faceVertices(mesh, triangleRange), faceBounds = bounds(vertices);
            var scored = available.filter(function (entry) { return !used.has(entry.supportIndex); }).map(function (entry) { return { entry: entry, error: supportError(vertices, entry.candidate, faceBounds) }; }).filter(function (match) { return Number.isFinite(match.error); }).sort(function (left, right) { return left.error - right.error || left.entry.supportIndex - right.entry.supportIndex; });
            if (!scored.length) { return { unresolvedReason: 'unmatched_brep_face_support' }; }
            if (scored.length > 1 && Math.abs(scored[0].error - scored[1].error) <= 0.0000000001) { return { unresolvedReason: 'ambiguous_duplicate_semantic_face' }; }
            var selected = scored[0].entry;
            var uniqueSamples = new Map(); vertices.forEach(function (vertex) { uniqueSamples.set(pointKey(vertex), vertex); });
            used.add(selected.supportIndex); output.push(Object.assign({}, selected.candidate, { triangleRange: triangleRange, analysisSamples: Array.from(uniqueSamples.values()) }));
        }
        return output.length ? { faces: output } : { unresolvedReason: 'unmatched_brep_face_support' };
    }
    function canonicalBrep(meshes, supports) {
        var bodies = [], available = supports.map(function (support, supportIndex) { var candidate = supportFace(support); return candidate ? { candidate: candidate, supportIndex: supportIndex } : null; }).filter(Boolean), byBounds = new Map(), used = new Set();
        available.forEach(function (entry) {
            var key = boundsKey(entry.candidate.surface.boundsMm);
            if (!byBounds.has(key)) { byBounds.set(key, []); }
            byBounds.get(key).push(entry);
        });
        for (var index = 0; index < meshes.length; index += 1) {
            var match = matchedFaces(meshes[index], byBounds, available, used); if (match.unresolvedReason) { return match; }
            var faces = match.faces;
            faces.forEach(function (face) { delete face._matchVertices; face._key = semanticKey(face); }); faces.sort(function (left, right) { return left._key.localeCompare(right._key); });
            bodies.push({ faces: faces, sourceMeshIndex: index,
                key: faces.map(function (face) { return face._key; }).join('||') });
        }
        if (used.size !== supports.length) { return { unresolvedReason: 'unmatched_brep_face_support' }; }
        bodies.sort(function (left, right) { return left.key.localeCompare(right.key); });
        var faces = [], bodyOutput = [], bodyAssociations = [];
        bodies.forEach(function (body, bodyIndex) {
            var bodyId = 'body-' + (bodyIndex + 1);
            bodyOutput.push({ id: bodyId });
            bodyAssociations.push({ bodyId: bodyId, validationMeshIndex: body.sourceMeshIndex });
            body.faces.forEach(function (face, faceIndex) { face.id = 'face-' + (bodyIndex + 1) + '-' + (faceIndex + 1); face.bodyId = bodyId; face.adjacentFaceIds = []; delete face._key; faces.push(face); });
        });
        return { bodies: bodyOutput, faces: faces, bodyAssociations: bodyAssociations };
    }
    function attachValidationVolumes(faces) {
        faces.forEach(function (face) {
            var value = face.surface && face.surface.boundsMm;
            if (!value) { return; }
            var minimum = {}, maximum = {};
            var surface = face.surface || {}, axis = surface.axis, center = surface.centerMm;
            var radius = Number(surface.radiusMm);
            var analyticCylinder = surface.kind === 'cylinder' && finitePoint(axis)
                && finitePoint(center) && Number.isFinite(radius) && radius > 0;
            var axisLength = analyticCylinder
                ? Math.sqrt(axis.x * axis.x + axis.y * axis.y + axis.z * axis.z) : 0;
            var axialValues = analyticCylinder ? values(face.loops).reduce(function (items, loop) {
                return items.concat(values(loop.vertices).filter(finitePoint).map(function (point) {
                    return ((point.x - center.x) * axis.x + (point.y - center.y) * axis.y
                        + (point.z - center.z) * axis.z) / axisLength;
                }));
            }, []) : [];
            ['x', 'y', 'z'].forEach(function (axis) {
                var sourceMinimum = value.min[axis], sourceMaximum = value.max[axis];
                if (analyticCylinder && axialValues.length) {
                    var component = surface.axis[axis] / axisLength;
                    var first = center[axis] + Math.min.apply(Math, axialValues) * component;
                    var last = center[axis] + Math.max.apply(Math, axialValues) * component;
                    var radialExtent = radius * Math.sqrt(Math.max(0, 1 - component * component));
                    sourceMinimum = Math.min(first, last) - radialExtent;
                    sourceMaximum = Math.max(first, last) + radialExtent;
                }
                var span = sourceMaximum - sourceMinimum, padding = Math.max(0.25, span * 0.005);
                minimum[axis] = snap(sourceMinimum - padding); maximum[axis] = snap(sourceMaximum + padding);
            });
            face.validationVolume = { minimum: minimum, maximum: maximum };
        });
    }
    function canonicalEdges(faces) {
        var owners = new Map(), uses = new Map(), byId = new Map(); faces.forEach(function (face) { byId.set(face.id, face); });
        faces.forEach(function (face) { face.loops.forEach(function (loop) {
            if (Array.isArray(loop.edgeIds) && loop.edgeIds.length) { loop.edgeIds.forEach(function (edgeId) { var key = 'brep:' + edgeId; if (!owners.has(key)) { owners.set(key, new Set()); } owners.get(key).add(face.id); uses.set(key, (uses.get(key) || 0) + 1); }); return; }
            if (loop.vertices.length < 2) { return; } loop.vertices.forEach(function (vertex, index, vertices) { var key = [pointKey(vertex), pointKey(vertices[(index + 1) % vertices.length])].sort().join('|'); if (!owners.has(key)) { owners.set(key, new Set()); } owners.get(key).add(face.id); uses.set(key, (uses.get(key) || 0) + 1); });
        }); });
        return Array.from(owners.entries()).sort(function (left, right) { return left[0].localeCompare(right[0]); }).map(function (entry) { var faceIds = Array.from(entry[1]).sort(); faceIds.forEach(function (faceId) { var face = byId.get(faceId); face.adjacentFaceIds = Array.from(new Set(face.adjacentFaceIds.concat(faceIds.filter(function (id) { return id !== faceId; })))).sort(); }); return { id: 'edge:' + entry[0], faceIds: faceIds, useCount: uses.get(entry[0]) }; });
    }
    function canonicalValidationMesh(meshes, faces, deflectionMm, bodyAssociations, cadClosed) {
        var vertices = [], vertexIds = new Map(), triangles = [], edges = new Map();
        // Match the fixed-deflection OCCT face partitions independently of display
        // tessellation. Only existing triangles on the exact trimmed CAD face may
        // authenticate a numerical seam; an infinite support plane is insufficient.
        var trimSupports = [], trimUsed = new Set(), trimValid = true;
        meshes.forEach(function (mesh, meshIndex) {
            var body = values(bodyAssociations).find(function (entry) { return entry.validationMeshIndex === meshIndex; });
            var available = faces.map(function (face, index) { return { candidate: face, supportIndex: index }; })
                .filter(function (entry) { return body && entry.candidate.bodyId === body.bodyId; });
            var match = matchedFaces(mesh, new Map(), available, trimUsed);
            if (match.unresolvedReason) { trimValid = false; return; }
            values(match.faces).forEach(function (face) {
                var samples = faceVertices(mesh, face.triangleRange);
                if (face.surface.kind !== 'plane') { return; }
                trimSupports.push({ face: face, samples: samples });
            });
        });
        if (!trimValid || trimUsed.size !== faces.length) { trimSupports = []; }
        function nearTrim(p, support, lateral) {
            function cross(a, b, c) { return (b[lateral[0]] - a[lateral[0]]) * (c[lateral[1]] - a[lateral[1]])
                - (b[lateral[1]] - a[lateral[1]]) * (c[lateral[0]] - a[lateral[0]]); }
            function segmentDistance(a, b) {
                var dx = b[lateral[0]] - a[lateral[0]], dy = b[lateral[1]] - a[lateral[1]], length2 = dx * dx + dy * dy;
                var t = length2 ? Math.max(0, Math.min(1, ((p[lateral[0]] - a[lateral[0]]) * dx + (p[lateral[1]] - a[lateral[1]]) * dy) / length2)) : 0;
                return Math.hypot(p[lateral[0]] - a[lateral[0]] - t * dx, p[lateral[1]] - a[lateral[1]] - t * dy);
            }
            for (var i = 0; i < support.samples.length; i += 3) {
                var a = support.samples[i], b = support.samples[i + 1], c = support.samples[i + 2];
                var signs = [cross(a, b, p), cross(b, c, p), cross(c, a, p)];
                if (Math.abs(cross(a, b, c)) > 1e-10 && (signs.every(function (s) { return s >= 0; }) || signs.every(function (s) { return s <= 0; }))) { return true; }
                if (Math.min(segmentDistance(a, b), segmentDistance(b, c), segmentDistance(c, a)) <= deflectionMm / 2) { return true; }
            }
            return false;
        }
        function boundedSeam(points, support, planeAxis) {
            var lateral = ['x', 'y', 'z'].filter(function (name) { return name !== planeAxis; }), area = 0;
            if (points.length > 8) { return false; }
            for (var i = 1; i < points.length - 1; i++) {
                var a = points[0], b = points[i], c = points[i + 1];
                area += Math.abs((b[lateral[0]] - a[lateral[0]]) * (c[lateral[1]] - a[lateral[1]])
                    - (b[lateral[1]] - a[lateral[1]]) * (c[lateral[0]] - a[lateral[0]])) / 2;
                if (area > 32 * deflectionMm * deflectionMm) { return false; }
                var length = Math.max(Math.hypot(b.x-a.x,b.y-a.y,b.z-a.z), Math.hypot(c.x-a.x,c.y-a.y,c.z-a.z), Math.hypot(c.x-b.x,c.y-b.y,c.z-b.z));
                var steps = Math.ceil(length / (deflectionMm / 2));
                if (steps > 64) { return false; }
                // Distance to the retained trim is 1-Lipschitz: the sampling
                // radius plus allowed distance never exceeds one deflection.
                for (var u = 0; u <= steps; u++) for (var v = 0; v <= steps - u; v++) {
                    var p = {}; ['x', 'y', 'z'].forEach(function (name) { p[name] = a[name] + (b[name]-a[name])*u/steps + (c[name]-a[name])*v/steps; });
                    if (!nearTrim(p, support, lateral)) { return false; }
                }
            }
            return area > 0;
        }
        function vertexId(value) {
            var snapped = point(value), key = snapped && pointKey(snapped);
            if (!key) { return null; }
            if (!vertexIds.has(key)) { vertexIds.set(key, vertices.length); vertices.push(snapped); }
            return vertexIds.get(key);
        }
        meshes.forEach(function (mesh) {
            var source = positions(mesh), map = indexes(mesh), count = map.length ? map.length : source.length / 3;
            for (var ordinal = 0; ordinal + 2 < count; ordinal += 3) {
                var ids = [];
                for (var corner = 0; corner < 3; corner += 1) {
                    var index = map.length ? Number(map[ordinal + corner]) : ordinal + corner, offset = index * 3;
                    ids.push(vertexId({ x: Number(source[offset]), y: Number(source[offset + 1]), z: Number(source[offset + 2]) }));
                }
                if (ids.some(function (id) { return id == null; }) || new Set(ids).size !== 3) { continue; }
                triangles.push(ids);
                [[ids[0], ids[1]], [ids[1], ids[2]], [ids[2], ids[0]]].forEach(function (edge) {
                    var key = edge.slice().sort(function (a, b) { return a - b; }).join(':'); edges.set(key, (edges.get(key) || 0) + 1);
                });
            }
        });
        var boundary = Array.from(edges.entries()).filter(function (entry) { return entry[1] === 1; });
        // A numerical seam may be reconciled only after CAD edge incidence proves
        // the input shell is closed. A display mesh can never supply CAD closure.
        if (boundary.length && cadClosed && Array.from(edges.values()).every(function (count) { return count <= 2; })) {
            var adjacency = new Map();
            boundary.forEach(function (entry) { var ids = entry[0].split(':').map(Number);
                ids.forEach(function (id, index) { if (!adjacency.has(id)) { adjacency.set(id, []); } adjacency.get(id).push(ids[1 - index]); }); });
            var remaining = new Set(boundary.map(function (entry) { return entry[0]; })), loops = [];
            while (remaining.size) {
                var firstEdge = remaining.values().next().value, firstIds = firstEdge.split(':').map(Number), loop = [firstIds[0]], prior = firstIds[0], current = firstIds[1];
                remaining.delete(firstEdge);
                while (current !== loop[0] && loop.length <= boundary.length) {
                    loop.push(current); var next = values(adjacency.get(current)).find(function (id) {
                        return id !== prior && remaining.has([current, id].sort(function (a, b) { return a - b; }).join(':'));
                    });
                    if (next == null) { break; }
                    remaining.delete([current, next].sort(function (a, b) { return a - b; }).join(':')); prior = current; current = next;
                }
                if (current === loop[0] && loop.length >= 3) { loops.push(loop); }
            }
            loops.forEach(function (loop) {
                var points = loop.map(function (id) { return vertices[id]; });
                var planeAxis = ['x', 'y', 'z'].find(function (name) { return points.every(function (p) { return Math.abs(p[name] - points[0][name]) <= 0.001; }); });
                var supported = planeAxis && trimSupports.filter(function (support) { var face = support.face, normal = face.surface.normal;
                    return face.surface.kind === 'plane' && normal && Math.abs(normal[planeAxis]) > 0.999
                        && finitePoint(face.surface.centerMm)
                        && Math.abs(face.surface.centerMm[planeAxis] - points[0][planeAxis]) <= 0.001
                        && boundedSeam(points, support, planeAxis);
                }).length === 1;
                if (!supported) { return; }
                for (var index = 1; index < loop.length - 1; index++) {
                    var ids = [loop[0], loop[index], loop[index + 1]]; triangles.push(ids);
                    [[ids[0], ids[1]], [ids[1], ids[2]], [ids[2], ids[0]]].forEach(function (edge) {
                        var key = edge.slice().sort(function (a, b) { return a - b; }).join(':'); edges.set(key, (edges.get(key) || 0) + 1);
                    });
                }
            });
        }
        return { contract: 'CncBrepValidationMesh.v1', resolutionMm: 0.5,
            deflectionMm: Number(deflectionMm), source: 'occt_brep_validation_tessellation_v1',
            bodyAssociations: bodyAssociations, vertices: vertices, triangles: triangles,
            boundaryEdgeCount: Array.from(edges.values()).filter(function (count) { return count === 1; }).length,
            nonmanifoldEdgeCount: Array.from(edges.values()).filter(function (count) { return count > 2; }).length,
            watertight: triangles.length > 3 && cadClosed && Array.from(edges.values()).every(function (count) { return count === 2; }) };
    }
    function validationMeshPayload(mesh) { return mesh && { contract: mesh.contract, source: mesh.source,
        resolutionMm: mesh.resolutionMm, deflectionMm: mesh.deflectionMm,
        bodyAssociations: mesh.bodyAssociations, vertices: mesh.vertices, triangles: mesh.triangles }; }
    async function validationMeshHash(mesh) { return root.CncPlanContracts.hash(validationMeshPayload(mesh)); }
    function revisionPayload(topology) { return { contract: topology.contract, sourceKind: topology.sourceKind, automaticPlanningEligible: topology.automaticPlanningEligible, unresolvedReasons: topology.unresolvedReasons, bodies: topology.bodies, faces: topology.faces.map(function (face) { return { id: face.id, bodyId: face.bodyId, surface: face.surface, loops: face.loops, orientation: face.orientation, adjacentFaceIds: face.adjacentFaceIds }; }), edges: topology.edges, validationMeshHash: topology.validationMeshHash }; }
    async function revisionHash(topology) { return root.CncPlanContracts.hash(revisionPayload(topology)); }
    async function build(input) {
        input = input || {};
        var format = String(input.sourceFormat || '').toLowerCase(), sourceKind = /^(step|stp|iges|igs)$/.test(format) ? 'brep' : 'mesh', meshes = Array.isArray(input.meshes) ? input.meshes : [],
            validationMeshes = Array.isArray(input.validationMeshes) ? input.validationMeshes : [], result = null, reasons = [];
        if (sourceKind === 'mesh') { reasons.push('mesh_source'); }
        else if (format === 'iges' || format === 'igs') { reasons.push('iges_semantic_face_support_unavailable'); }
        else { result = canonicalBrep(meshes, Array.isArray(input.analyticSurfaces) ? input.analyticSurfaces : []); if (!result || result.unresolvedReason) { reasons.push(result && result.unresolvedReason ? result.unresolvedReason : 'unmatched_brep_face_support'); result = null; } }
        if (result) { attachValidationVolumes(result.faces); }
        if (result && (!validationMeshes.length || Number(input.validationDeflectionMm) !== 0.1)) {
            reasons.push('validation_mesh_required');
        }
        var bodyAssociations = result && validationMeshes.length === result.bodies.length
            ? result.bodyAssociations : [];
        var cadEdges = result ? canonicalEdges(result.faces) : [];
        var cadClosed = cadEdges.length > 0 && cadEdges.every(function (edge) { return edge.useCount === 2; });
        var topology = { contract: 'CncCadTopology.v1', sourceKind: sourceKind, automaticPlanningEligible: !!result && reasons.length === 0, unresolvedReasons: reasons, bodies: result ? result.bodies : [], faces: result ? result.faces : [], edges: cadEdges,
            validationMesh: result && validationMeshes.length && Number(input.validationDeflectionMm) === 0.1
                ? canonicalValidationMesh(validationMeshes, result.faces, input.validationDeflectionMm, bodyAssociations, cadClosed) : null };
        if (result && topology.validationMesh && !topology.validationMesh.watertight) {
            topology.automaticPlanningEligible = false; topology.unresolvedReasons.push('nonwatertight_validation_mesh');
        }
        if (!root.CncPlanContracts || typeof root.CncPlanContracts.hash !== 'function') { throw new Error('CNC topology requires CncPlanContracts.hash.'); }
        topology.validationMeshHash = topology.validationMesh ? await validationMeshHash(topology.validationMesh) : null;
        topology.revision = await revisionHash(topology); return topology;
    }
    root.CncTopology = Object.freeze({ build: build, validationMeshHash: validationMeshHash,
        revisionHash: revisionHash });
}(typeof self !== 'undefined' ? self : globalThis));
