// Browser-worker-only CNC geometry evidence. These facts are deliberately heuristic: they
// support conservative quoting review, not CAD feature recognition, tolerancing, or CAM.

var CNC_MAX_SAMPLED_TRIANGLES = 50000;
var CNC_JACOBI_ITERATIONS = 18;
var CNC_EPSILON = 1e-9;
var CNC_EDGE_QUANTIZATION_MM = 0.0001;
var CNC_CLUSTER_NORMAL_DOT = 0.94;
var CNC_ROTATIONAL_RADIAL_DEVIATION_LIMIT = 0.035;
var CNC_ROTATIONAL_EXTENT_DIFFERENCE_LIMIT = 0.05;
var CNC_ROTATIONAL_COVERAGE_MINIMUM = 0.60;
var CNC_ROTATIONAL_ANGLE_BINS = 24;
var CNC_MAX_BOUNDARY_EDGES = 4096;
var CNC_MAX_SURFACE_CLUSTERS = 256;
var CNC_NORMAL_BUCKET_SCALE = 8;
var CNC_MAX_NORMAL_CLASSES_PER_EDGE = Math.pow((CNC_NORMAL_BUCKET_SCALE * 2) + 1, 3);
var CNC_VISIBILITY_GRID_SIZE = 72;
var CNC_MINIMUM_MILLING_TOOL_RADIUS_MM = 0.5;
var CNC_SMOOTH_NEIGHBOR_COSINE = 0.99;
var CNC_FLUTE_TANGENT_ALIGNMENT = 0.20;
var CNC_THIN_PLATE_RULES = self.CncQuotationConfig && self.CncQuotationConfig.thinPlate;

function CncVector(x, y, z) {
    return { x: x, y: y, z: z };
}

function CncDot(a, b) {
    return a.x * b.x + a.y * b.y + a.z * b.z;
}

function CncCross(a, b) {
    return CncVector(
        a.y * b.z - a.z * b.y,
        a.z * b.x - a.x * b.z,
        a.x * b.y - a.y * b.x);
}

function CncLength(vector) {
    return Math.sqrt(CncDot(vector, vector));
}

function CncNormalize(vector) {
    var length = CncLength(vector);
    return length > CNC_EPSILON ? CncVector(vector.x / length, vector.y / length, vector.z / length) : null;
}

function CncNegate(vector) {
    return CncVector(-vector.x, -vector.y, -vector.z);
}

function CncSubtract(a, b) {
    return CncVector(a.x - b.x, a.y - b.y, a.z - b.z);
}

function CncScale(vector, scalar) {
    return CncVector(vector.x * scalar, vector.y * scalar, vector.z * scalar);
}

function CncAdd(a, b) {
    return CncVector(a.x + b.x, a.y + b.y, a.z + b.z);
}

function CncAxis(x, y, z) {
    return CncVector(x, y, z);
}

function CncCloneVector(vector) {
    return vector ? CncVector(vector.x, vector.y, vector.z) : null;
}

function CncSampleTriangles(triangles) {
    var triangleCount = Math.floor(triangles.length / 9);
    var step = Math.max(1, Math.ceil(triangleCount / CNC_MAX_SAMPLED_TRIANGLES));
    var sampled = [];
    for (var triangle = 0; triangle < triangleCount; triangle += step) {
        var offset = triangle * 9;
        sampled.push(
            triangles[offset], triangles[offset + 1], triangles[offset + 2],
            triangles[offset + 3], triangles[offset + 4], triangles[offset + 5],
            triangles[offset + 6], triangles[offset + 7], triangles[offset + 8]);
    }
    return { triangles: sampled, sampled: step > 1 };
}

function CncJacobiAxes(covariance) {
    var matrix = [
        [covariance[0][0], covariance[0][1], covariance[0][2]],
        [covariance[1][0], covariance[1][1], covariance[1][2]],
        [covariance[2][0], covariance[2][1], covariance[2][2]]
    ];
    var vectors = [[1, 0, 0], [0, 1, 0], [0, 0, 1]];

    for (var iteration = 0; iteration < CNC_JACOBI_ITERATIONS; iteration++) {
        var p = 0;
        var q = 1;
        var maximum = Math.abs(matrix[0][1]);
        if (Math.abs(matrix[0][2]) > maximum) { p = 0; q = 2; maximum = Math.abs(matrix[0][2]); }
        if (Math.abs(matrix[1][2]) > maximum) { p = 1; q = 2; maximum = Math.abs(matrix[1][2]); }
        if (maximum < CNC_EPSILON) { break; }

        var app = matrix[p][p];
        var aqq = matrix[q][q];
        var apq = matrix[p][q];
        var phi = 0.5 * Math.atan2(2 * apq, aqq - app);
        var cosine = Math.cos(phi);
        var sine = Math.sin(phi);

        for (var index = 0; index < 3; index++) {
            if (index === p || index === q) { continue; }
            var ip = matrix[index][p];
            var iq = matrix[index][q];
            matrix[index][p] = matrix[p][index] = cosine * ip - sine * iq;
            matrix[index][q] = matrix[q][index] = sine * ip + cosine * iq;
        }
        matrix[p][p] = cosine * cosine * app - 2 * sine * cosine * apq + sine * sine * aqq;
        matrix[q][q] = sine * sine * app + 2 * sine * cosine * apq + cosine * cosine * aqq;
        matrix[p][q] = 0;
        matrix[q][p] = 0;

        for (var vectorRow = 0; vectorRow < 3; vectorRow++) {
            var vp = vectors[vectorRow][p];
            var vq = vectors[vectorRow][q];
            vectors[vectorRow][p] = cosine * vp - sine * vq;
            vectors[vectorRow][q] = sine * vp + cosine * vq;
        }
    }

    return [
        CncNormalize(CncVector(vectors[0][0], vectors[1][0], vectors[2][0])) || CncAxis(1, 0, 0),
        CncNormalize(CncVector(vectors[0][1], vectors[1][1], vectors[2][1])) || CncAxis(0, 1, 0),
        CncNormalize(CncVector(vectors[0][2], vectors[1][2], vectors[2][2])) || CncAxis(0, 0, 1)
    ];
}

function CncCovarianceAxes(triangles) {
    // STL and other triangle soups repeat the same corner once per incident triangle. Those
    // repetitions depend on face triangulation, so deduplicate sampled vertices before PCA.
    // The positions still come directly from the triangle mesh; this merely prevents a chosen
    // diagonal from biasing a rectangular part's orientation.
    var vertices = [];
    var seen = new Set();
    for (var sourceIndex = 0; sourceIndex < triangles.length; sourceIndex += 3) {
        var key = triangles[sourceIndex] + '|' + triangles[sourceIndex + 1] + '|' + triangles[sourceIndex + 2];
        if (!seen.has(key)) {
            seen.add(key);
            vertices.push(triangles[sourceIndex], triangles[sourceIndex + 1], triangles[sourceIndex + 2]);
        }
    }
    var count = vertices.length / 3;
    if (count < 3) { return [CncAxis(1, 0, 0), CncAxis(0, 1, 0), CncAxis(0, 0, 1)]; }
    var centroid = CncVector(0, 0, 0);
    for (var i = 0; i < vertices.length; i += 3) {
        centroid.x += vertices[i];
        centroid.y += vertices[i + 1];
        centroid.z += vertices[i + 2];
    }
    centroid.x /= count;
    centroid.y /= count;
    centroid.z /= count;

    var covariance = [[0, 0, 0], [0, 0, 0], [0, 0, 0]];
    for (var vertex = 0; vertex < vertices.length; vertex += 3) {
        var x = vertices[vertex] - centroid.x;
        var y = vertices[vertex + 1] - centroid.y;
        var z = vertices[vertex + 2] - centroid.z;
        covariance[0][0] += x * x; covariance[0][1] += x * y; covariance[0][2] += x * z;
        covariance[1][1] += y * y; covariance[1][2] += y * z;
        covariance[2][2] += z * z;
    }
    covariance[1][0] = covariance[0][1];
    covariance[2][0] = covariance[0][2];
    covariance[2][1] = covariance[1][2];
    return CncJacobiAxes(covariance);
}

function CncExtents(triangles, axes) {
    var minimum = [Infinity, Infinity, Infinity];
    var maximum = [-Infinity, -Infinity, -Infinity];
    for (var i = 0; i < triangles.length; i += 3) {
        var vertex = CncVector(triangles[i], triangles[i + 1], triangles[i + 2]);
        for (var axisIndex = 0; axisIndex < 3; axisIndex++) {
            var projection = CncDot(vertex, axes[axisIndex]);
            if (projection < minimum[axisIndex]) { minimum[axisIndex] = projection; }
            if (projection > maximum[axisIndex]) { maximum[axisIndex] = projection; }
        }
    }
    var dimensions = [
        Math.max(0, maximum[0] - minimum[0]),
        Math.max(0, maximum[1] - minimum[1]),
        Math.max(0, maximum[2] - minimum[2])
    ];
    return { dimensions: dimensions, volume: dimensions[0] * dimensions[1] * dimensions[2] };
}

function CncCanonicalNormal(normal) {
    var values = [normal.x, normal.y, normal.z];
    for (var index = 0; index < values.length; index++) {
        if (Math.abs(values[index]) <= CNC_EPSILON) { continue; }
        return values[index] < 0
            ? CncVector(-normal.x, -normal.y, -normal.z)
            : normal;
    }
    return normal;
}

function CncPlanarSupports(triangles) {
    var buckets = new Map();
    for (var index = 0; index < triangles.length; index += 9) {
        var a = CncVector(triangles[index], triangles[index + 1], triangles[index + 2]);
        var b = CncVector(triangles[index + 3], triangles[index + 4], triangles[index + 5]);
        var c = CncVector(triangles[index + 6], triangles[index + 7], triangles[index + 8]);
        var cross = CncCross(
            CncVector(b.x - a.x, b.y - a.y, b.z - a.z),
            CncVector(c.x - a.x, c.y - a.y, c.z - a.z));
        var twiceArea = CncLength(cross);
        if (twiceArea <= CNC_EPSILON) { continue; }
        var normal = CncCanonicalNormal(CncNormalize(cross));
        var normalKey = [normal.x, normal.y, normal.z].map(function (value) {
            return Math.round(value * 10000);
        }).join('|');
        var offset = CncDot(normal, a);
        var key = normalKey + '|' + Math.round(offset * 1000);
        var bucket = buckets.get(key);
        if (bucket) { bucket.area += twiceArea * 0.5; }
        else {
            buckets.set(key, {
                normal: normal,
                offset: offset,
                area: twiceArea * 0.5
            });
        }
    }

    return Array.from(buckets.values());
}

function CncDominantFaceFrames(planarSupports) {
    var normalBuckets = new Map();
    planarSupports.forEach(function (support) {
        var key = [support.normal.x, support.normal.y, support.normal.z].map(function (value) {
            return Math.round(value * 10000);
        }).join('|');
        var bucket = normalBuckets.get(key);
        if (bucket) { bucket.area += support.area; }
        else { normalBuckets.set(key, { normal: support.normal, area: support.area }); }
    });

    var normals = Array.from(normalBuckets.values())
        .sort(function (left, right) { return right.area - left.area; })
        .slice(0, 12);
    var frames = [];
    normals.forEach(function (first, firstIndex) {
        for (var secondIndex = firstIndex + 1; secondIndex < normals.length; secondIndex++) {
            var second = normals[secondIndex];
            var alignment = CncDot(first.normal, second.normal);
            if (Math.abs(alignment) > 0.08) { continue; }
            var yAxis = CncNormalize(CncVector(
                second.normal.x - (first.normal.x * alignment),
                second.normal.y - (first.normal.y * alignment),
                second.normal.z - (first.normal.z * alignment)));
            var zAxis = yAxis && CncNormalize(CncCross(first.normal, yAxis));
            if (yAxis && zAxis) { frames.push([first.normal, yAxis, zAxis]); }
        }
    });
    return frames;
}

function CncFrameOrientationEvidence(planarSupports, axes, extents) {
    var opposingParallelAreaMm2 = 0;
    var primaryDatumAreaMm2 = 0;
    var parallelPlaneLevelCount = 0;
    var supportedAxisCount = 0;

    axes.forEach(function (axis) {
        var levels = new Map();
        planarSupports.forEach(function (support) {
            var alignment = CncDot(support.normal, axis);
            // Datum parallelism is a machining constraint, not a silhouette similarity.
            // A two-degree envelope rotation can close a long narrow tool corridor.
            if (Math.abs(alignment) < 0.999999) { return; }
            var signedOffset = alignment < 0 ? -support.offset : support.offset;
            var key = Math.round(signedOffset * 1000);
            levels.set(key, (levels.get(key) || 0) + support.area);
        });
        var areas = Array.from(levels.values());
        parallelPlaneLevelCount += areas.length;
        if (areas.length >= 2) {
            supportedAxisCount++;
            var totalArea = areas.reduce(function (total, area) { return total + area; }, 0);
            var largestArea = Math.max.apply(Math, areas);
            opposingParallelAreaMm2 += totalArea - largestArea;
        }
        areas.forEach(function (area) {
            if (area > primaryDatumAreaMm2) { primaryDatumAreaMm2 = area; }
        });
    });

    return {
        strategy: opposingParallelAreaMm2 > CNC_EPSILON
            ? 'opposing-planar-support'
            : 'minimum-envelope-fallback',
        opposingParallelAreaMm2: opposingParallelAreaMm2,
        primaryDatumAreaMm2: primaryDatumAreaMm2,
        parallelPlaneLevelCount: parallelPlaneLevelCount,
        predictedSetupPenalty: 3 - supportedAxisCount,
        boundingVolumeMm3: extents.volume
    };
}

function CncCompareOrientationEvidence(left, right) {
    var descending = [
        'opposingParallelAreaMm2',
        'primaryDatumAreaMm2',
        'parallelPlaneLevelCount'
    ];
    for (var index = 0; index < descending.length; index++) {
        var key = descending[index];
        var tolerance = Math.max(CNC_EPSILON, Math.max(Math.abs(left[key]), Math.abs(right[key])) * 0.000001);
        if (left[key] > right[key] + tolerance) { return -1; }
        if (right[key] > left[key] + tolerance) { return 1; }
    }
    if (left.predictedSetupPenalty !== right.predictedSetupPenalty) {
        return left.predictedSetupPenalty - right.predictedSetupPenalty;
    }
    var volumeTolerance = Math.max(CNC_EPSILON, Math.max(left.boundingVolumeMm3, right.boundingVolumeMm3) * 0.000001);
    if (left.boundingVolumeMm3 + volumeTolerance < right.boundingVolumeMm3) { return -1; }
    if (right.boundingVolumeMm3 + volumeTolerance < left.boundingVolumeMm3) { return 1; }
    return 0;
}

function CncAlignFrameToUploadedAxes(axes) {
    var uploaded = [CncAxis(1, 0, 0), CncAxis(0, 1, 0), CncAxis(0, 0, 1)];
    var permutations = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];
    var best = permutations[0];
    var bestScore = -Infinity;
    permutations.forEach(function (permutation) {
        var score = permutation.reduce(function (total, sourceIndex, targetIndex) {
            return total + Math.abs(CncDot(axes[sourceIndex], uploaded[targetIndex]));
        }, 0);
        if (score > bestScore + CNC_EPSILON) { best = permutation; bestScore = score; }
    });
    return best.map(function (sourceIndex, targetIndex) {
        var axis = axes[sourceIndex];
        return CncDot(axis, uploaded[targetIndex]) < 0
            ? CncVector(-axis.x, -axis.y, -axis.z)
            : axis;
    });
}

function CncChooseAxes(triangles) {
    var uploadedAxes = [CncAxis(1, 0, 0), CncAxis(0, 1, 0), CncAxis(0, 0, 1)];
    var principalAxes = CncCovarianceAxes(triangles);
    var planarSupports = CncPlanarSupports(triangles);
    var candidates = [
        { axes: uploadedAxes, alignToUploaded: false },
        { axes: principalAxes, alignToUploaded: false }
    ].concat(CncDominantFaceFrames(planarSupports).map(function (axes) {
        return { axes: axes, alignToUploaded: true };
    }));
    candidates.forEach(function (candidate) {
        if (candidate.alignToUploaded) {
            candidate.axes = CncAlignFrameToUploadedAxes(candidate.axes);
        }
        candidate.extents = CncExtents(triangles, candidate.axes);
        candidate.evidence = CncFrameOrientationEvidence(planarSupports, candidate.axes, candidate.extents);
    });
    var selected = {
        axes: candidates[0].axes,
        extents: candidates[0].extents,
        evidence: candidates[0].evidence
    };
    for (var index = 1; index < candidates.length; index++) {
        if (CncCompareOrientationEvidence(candidates[index].evidence, selected.evidence) < 0) {
            selected = {
                axes: candidates[index].axes,
                extents: candidates[index].extents,
                evidence: candidates[index].evidence
            };
        }
    }
    return { axes: selected.axes, extents: selected.extents, evidence: selected.evidence };
}

function CncTriangleEvidence(triangles, axes) {
    var totalArea = 0;
    var planarArea = 0;
    var directionAreas = [0, 0, 0, 0, 0, 0];
    var nonPlanarArea = 0;
    var cylindricalNormalArea = 0;

    for (var i = 0; i < triangles.length; i += 9) {
        var a = CncVector(triangles[i], triangles[i + 1], triangles[i + 2]);
        var b = CncVector(triangles[i + 3], triangles[i + 4], triangles[i + 5]);
        var c = CncVector(triangles[i + 6], triangles[i + 7], triangles[i + 8]);
        var normal = CncCross(CncVector(b.x - a.x, b.y - a.y, b.z - a.z), CncVector(c.x - a.x, c.y - a.y, c.z - a.z));
        var twiceArea = CncLength(normal);
        if (twiceArea <= CNC_EPSILON) { continue; }
        var area = twiceArea * 0.5;
        normal = CncNormalize(normal);
        totalArea += area;
        var strongest = 0;
        for (var axis = 0; axis < 3; axis++) {
            var alignment = CncDot(normal, axes[axis]);
            var absoluteAlignment = Math.abs(alignment);
            if (absoluteAlignment > strongest) { strongest = absoluteAlignment; }
            if (alignment > 0) { directionAreas[axis * 2] += area * alignment; }
            else { directionAreas[(axis * 2) + 1] += area * -alignment; }
        }
        if (strongest >= 0.98) { planarArea += area; }
        else {
            nonPlanarArea += area;
            if (strongest <= 0.35) { cylindricalNormalArea += area; }
        }
    }
    return {
        totalArea: totalArea,
        planarArea: planarArea,
        nonPlanarArea: nonPlanarArea,
        cylindricalNormalArea: cylindricalNormalArea,
        directionAreas: directionAreas
    };
}

function CncQuantizedVertexKey(vertex) {
    return [vertex.x, vertex.y, vertex.z].map(function (value) {
        return Math.round(value / CNC_EDGE_QUANTIZATION_MM);
    }).join('|');
}

function CncTriangleRecord(triangles, offset, sourceTriangleOffset) {
    var vertices = [
            CncVector(triangles[offset], triangles[offset + 1], triangles[offset + 2]),
            CncVector(triangles[offset + 3], triangles[offset + 4], triangles[offset + 5]),
            CncVector(triangles[offset + 6], triangles[offset + 7], triangles[offset + 8])
    ];
    var cross = CncCross(CncSubtract(vertices[1], vertices[0]), CncSubtract(vertices[2], vertices[0]));
    var twiceArea = CncLength(cross);
    if (twiceArea <= CNC_EPSILON) { return null; }
    return {
        vertices: vertices,
        normal: CncScale(cross, 1 / twiceArea),
        areaMm2: twiceArea * 0.5,
        centroid: CncScale(CncAdd(CncAdd(vertices[0], vertices[1]), vertices[2]), 1 / 3),
        sourceTriangleIndex: (sourceTriangleOffset || 0) + Math.floor(offset / 9),
        neighbors: []
    };
}

function CncTriangleEdgeKeys(record) {
    var keys = [];
    for (var edgeIndex = 0; edgeIndex < 3; edgeIndex++) {
        var firstKey = CncQuantizedVertexKey(record.vertices[edgeIndex]);
        var secondKey = CncQuantizedVertexKey(record.vertices[(edgeIndex + 1) % 3]);
        keys.push(firstKey < secondKey ? firstKey + '~' + secondKey : secondKey + '~' + firstKey);
    }
    return keys;
}

function CncNormalClassKey(normal) {
    return [normal.x, normal.y, normal.z].map(function (value) {
        return Math.round(value * CNC_NORMAL_BUCKET_SCALE);
    }).join('|');
}

function CncTriangleRecords(triangles, sourceTriangleOffset) {
    var records = [];
    var edges = new Map();
    for (var offset = 0; offset < triangles.length; offset += 9) {
        var record = CncTriangleRecord(triangles, offset, sourceTriangleOffset);
        if (!record) { continue; }
        var recordIndex = records.length;
        records.push(record);
        CncTriangleEdgeKeys(record).forEach(function (edgeKey) {
            if (!edges.has(edgeKey)) { edges.set(edgeKey, new Map()); }
            var representatives = edges.get(edgeKey);
            representatives.forEach(function (representative) {
                if (CncDot(record.normal, records[representative.recordIndex].normal) >= CNC_CLUSTER_NORMAL_DOT) {
                    records[recordIndex].neighbors.push(representative.recordIndex);
                    records[representative.recordIndex].neighbors.push(recordIndex);
                }
            });
            var normalClass = CncNormalClassKey(record.normal);
            if (representatives.has(normalClass)) {
                representatives.get(normalClass).count += 1;
            } else {
                representatives.set(normalClass, { recordIndex: recordIndex, count: 1 });
            }
        });
    }
    records.edgeKeyCount = edges.size;
    records.boundaryEdgeClasses = new Map();
    edges.forEach(function (representatives, edgeKey) {
        representatives.forEach(function (representative, normalClass) {
            if (representative.count !== 1) { return; }
            if (!records.boundaryEdgeClasses.has(edgeKey)) {
                records.boundaryEdgeClasses.set(edgeKey, new Set());
            }
            records.boundaryEdgeClasses.get(edgeKey).add(normalClass);
        });
    });
    return records;
}

function CncClusterTriangles(records) {
    var visited = new Array(records.length).fill(false);
    var clusters = [];
    for (var start = 0; start < records.length; start++) {
        if (visited[start]) { continue; }
        var members = [];
        var pending = [start];
        visited[start] = true;
        while (pending.length > 0) {
            var current = pending.pop();
            members.push(current);
            records[current].neighbors.forEach(function (neighbor) {
                if (!visited[neighbor] && CncDot(records[current].normal, records[neighbor].normal) >= CNC_CLUSTER_NORMAL_DOT) {
                    visited[neighbor] = true;
                    pending.push(neighbor);
                }
            });
        }
        clusters.push(members);
    }
    return clusters;
}

function CncRadialVector(point, origin, axis) {
    var relative = CncSubtract(point, origin);
    return CncSubtract(relative, CncScale(axis, CncDot(relative, axis)));
}

function CncClassifyCluster(records, memberIndexes, axes, origin, clusterIndex) {
    var totalArea = 0;
    var weightedCentroid = CncVector(0, 0, 0);
    var weightedNormal = CncVector(0, 0, 0);
    memberIndexes.forEach(function (index) {
        var record = records[index];
        totalArea += record.areaMm2;
        weightedCentroid = CncAdd(weightedCentroid, CncScale(record.centroid, record.areaMm2));
        weightedNormal = CncAdd(weightedNormal, CncScale(record.normal, record.areaMm2));
    });
    var centroid = totalArea > CNC_EPSILON ? CncScale(weightedCentroid, 1 / totalArea) : CncVector(0, 0, 0);
    var averageNormal = CncNormalize(weightedNormal);
    var normalConsistency = totalArea > CNC_EPSILON ? CncLength(weightedNormal) / totalArea : 0;
    var bestAxis = axes[0];
    var bestAxisScore = -Infinity;
    var bestAxialAlignment = 1;
    var bestRadialAlignment = 0;
    var bestRadialSign = 0;
    var localBestAxis = axes[0];
    var localBestAxisScore = -Infinity;
    var localBestAxialAlignment = 1;
    var localBestRadialAlignment = 0;
    var localBestRadialSign = 0;

    axes.forEach(function (axis) {
        var axialAlignment = 0;
        var radialAlignment = 0;
        var radialSign = 0;
        var localRadialAlignment = 0;
        var localRadialSign = 0;
        memberIndexes.forEach(function (index) {
            var record = records[index];
            var radial = CncNormalize(CncRadialVector(record.centroid, origin, axis));
            var radialDot = radial ? CncDot(record.normal, radial) : 0;
            var localRadial = CncNormalize(CncRadialVector(record.centroid, centroid, axis));
            var localRadialDot = localRadial ? CncDot(record.normal, localRadial) : 0;
            axialAlignment += Math.abs(CncDot(record.normal, axis)) * record.areaMm2;
            radialAlignment += Math.abs(radialDot) * record.areaMm2;
            radialSign += radialDot * record.areaMm2;
            localRadialAlignment += Math.abs(localRadialDot) * record.areaMm2;
            localRadialSign += localRadialDot * record.areaMm2;
        });
        axialAlignment /= Math.max(totalArea, CNC_EPSILON);
        radialAlignment /= Math.max(totalArea, CNC_EPSILON);
        radialSign /= Math.max(totalArea, CNC_EPSILON);
        localRadialAlignment /= Math.max(totalArea, CNC_EPSILON);
        localRadialSign /= Math.max(totalArea, CNC_EPSILON);
        var score = radialAlignment - axialAlignment;
        if (score > bestAxisScore) {
            bestAxisScore = score;
            bestAxis = axis;
            bestAxialAlignment = axialAlignment;
            bestRadialAlignment = radialAlignment;
            bestRadialSign = radialSign;
        }
        var localScore = localRadialAlignment - axialAlignment;
        if (localScore > localBestAxisScore) {
            localBestAxisScore = localScore;
            localBestAxis = axis;
            localBestAxialAlignment = axialAlignment;
            localBestRadialAlignment = localRadialAlignment;
            localBestRadialSign = localRadialSign;
        }
    });

    var type = 'unresolved';
    if (normalConsistency >= 0.985) { type = 'planar'; }
    else if (bestRadialAlignment >= 0.85 && bestAxialAlignment <= 0.35) { type = 'cylindrical'; }
    else if (bestRadialAlignment >= 0.65 && bestAxialAlignment <= 0.75) { type = 'conical'; }
    else if (normalConsistency < 0.45) { type = 'freeform'; }

    var radialMinimum = Infinity;
    var radialMaximum = 0;
    var axialMinimum = Infinity;
    var axialMaximum = -Infinity;
    var radialSamples = [];
    var seenVertices = new Set();
    memberIndexes.forEach(function (index) {
        records[index].vertices.forEach(function (vertex) {
            var vertexKey = CncQuantizedVertexKey(vertex);
            if (seenVertices.has(vertexKey)) { return; }
            seenVertices.add(vertexKey);
            var radialExtent = CncLength(CncRadialVector(vertex, origin, bestAxis));
            var axialPosition = CncDot(CncSubtract(vertex, origin), bestAxis);
            radialMinimum = Math.min(radialMinimum, radialExtent);
            radialMaximum = Math.max(radialMaximum, radialExtent);
            axialMinimum = Math.min(axialMinimum, axialPosition);
            axialMaximum = Math.max(axialMaximum, axialPosition);
            radialSamples.push({ axial: axialPosition, radial: radialExtent });
        });
    });

    var meanAxial = radialSamples.reduce(function (sum, sample) { return sum + sample.axial; }, 0) / Math.max(1, radialSamples.length);
    var meanRadial = radialSamples.reduce(function (sum, sample) { return sum + sample.radial; }, 0) / Math.max(1, radialSamples.length);
    var covariance = 0;
    var axialVariance = 0;
    radialSamples.forEach(function (sample) {
        covariance += (sample.axial - meanAxial) * (sample.radial - meanRadial);
        axialVariance += (sample.axial - meanAxial) * (sample.axial - meanAxial);
    });
    var radialSlope = axialVariance > CNC_EPSILON ? covariance / axialVariance : 0;
    var radialIntercept = meanRadial - radialSlope * meanAxial;
    var radialResiduals = radialSamples.map(function (sample) {
        return Math.abs(sample.radial - (radialIntercept + radialSlope * sample.axial));
    }).sort(function (left, right) { return left - right; });
    var robustResidual = radialResiduals.length > 0
        ? radialResiduals[Math.min(radialResiduals.length - 1, Math.floor(radialResiduals.length * 0.95))]
        : 0;
    var radialFitDeviationRatio = radialMaximum > CNC_EPSILON ? robustResidual / radialMaximum : 0;
    var localRadialSamples = [];
    var localAxialMinimum = Infinity;
    var localAxialMaximum = -Infinity;
    seenVertices.clear();
    memberIndexes.forEach(function (index) {
        records[index].vertices.forEach(function (vertex) {
            var vertexKey = CncQuantizedVertexKey(vertex);
            if (seenVertices.has(vertexKey)) { return; }
            seenVertices.add(vertexKey);
            localRadialSamples.push(CncLength(CncRadialVector(vertex, centroid, localBestAxis)));
            var axialPosition = CncDot(CncSubtract(vertex, centroid), localBestAxis);
            localAxialMinimum = Math.min(localAxialMinimum, axialPosition);
            localAxialMaximum = Math.max(localAxialMaximum, axialPosition);
        });
    });
    var localRadius = localRadialSamples.reduce(function (sum, value) { return sum + value; }, 0)
        / Math.max(1, localRadialSamples.length);
    var localResiduals = localRadialSamples.map(function (value) {
        return Math.abs(value - localRadius);
    }).sort(function (left, right) { return left - right; });
    var localRobustResidual = localResiduals.length > 0
        ? localResiduals[Math.min(localResiduals.length - 1, Math.floor(localResiduals.length * 0.95))]
        : 0;
    var localFitDeviationRatio = localRadius > CNC_EPSILON ? localRobustResidual / localRadius : 0;
    var localCylinder = localBestRadialAlignment >= 0.85 && localBestAxialAlignment <= 0.35
        && localFitDeviationRatio <= 0.15 && localRadius > CNC_EPSILON
        ? {
            axis: CncCloneVector(localBestAxis),
            radiusMm: localRadius,
            diameterMm: localRadius * 2,
            axialDepthMm: localAxialMaximum > localAxialMinimum
                ? localAxialMaximum - localAxialMinimum : 0,
            radialNormalSign: localBestRadialSign,
            isInternal: localBestRadialSign <= -0.35
        }
        : null;
    var directionAreas = [0, 0, 0, 0, 0, 0];
    memberIndexes.forEach(function (index) {
        var record = records[index];
        axes.forEach(function (axis, axisIndex) {
            var alignment = CncDot(record.normal, axis);
            var directionIndex = axisIndex * 2 + (alignment >= 0 ? 0 : 1);
            directionAreas[directionIndex] += record.areaMm2 * Math.abs(alignment);
        });
    });

    var confidence = type === 'planar' || type === 'cylindrical'
        ? (memberIndexes.length >= 4 ? 'High' : 'Medium')
        : (type === 'conical' ? 'Medium' : 'Low');
    return {
        evidence: {
            id: 'surface-' + (clusterIndex + 1),
            type: type,
            areaMm2: totalArea,
            centroid: centroid,
            normal: type === 'planar' ? averageNormal : null,
            axis: type === 'cylindrical' || type === 'conical' ? CncCloneVector(bestAxis) : null,
            radialExtentMm: type === 'cylindrical' || type === 'conical' ? radialMaximum : 0,
            radiusMm: type === 'cylindrical' || type === 'conical' ? meanRadial : 0,
            diameterMm: type === 'cylindrical' || type === 'conical' ? meanRadial * 2 : 0,
            isInternal: (type === 'cylindrical' || type === 'conical') && bestRadialSign <= -0.35,
            localCylinder: localCylinder,
            axialDepthMm: axialMaximum > axialMinimum ? axialMaximum - axialMinimum : 0,
            confidence: confidence
        },
        memberIndexes: memberIndexes,
        directionAreas: directionAreas,
        triangleCount: memberIndexes.length,
        axialMinimum: axialMinimum,
        axialMaximum: axialMaximum,
        axialNormalAlignment: bestAxialAlignment,
        radialNormalAlignment: bestRadialAlignment,
        radialMinimumMm: radialMinimum === Infinity ? 0 : radialMinimum,
        radialMaximumMm: radialMaximum,
        radialDeviationRatio: radialMaximum > CNC_EPSILON ? (radialMaximum - radialMinimum) / radialMaximum : 0,
        radialFitDeviationRatio: radialFitDeviationRatio,
        radialNormalSign: bestRadialSign,
        normalConsistency: normalConsistency,
        axisCandidate: CncCloneVector(bestAxis)
    };
}

function CncTriangleSoupCentroid(triangles) {
    var weighted = CncVector(0, 0, 0);
    var totalArea = 0;
    for (var offset = 0; offset < triangles.length; offset += 9) {
        var a = CncVector(triangles[offset], triangles[offset + 1], triangles[offset + 2]);
        var b = CncVector(triangles[offset + 3], triangles[offset + 4], triangles[offset + 5]);
        var c = CncVector(triangles[offset + 6], triangles[offset + 7], triangles[offset + 8]);
        var area = CncLength(CncCross(CncSubtract(b, a), CncSubtract(c, a))) * 0.5;
        if (area <= CNC_EPSILON) { continue; }
        weighted = CncAdd(weighted, CncScale(CncAdd(CncAdd(a, b), c), area / 3));
        totalArea += area;
    }
    return totalArea > CNC_EPSILON ? CncScale(weighted, 1 / totalArea) : CncVector(0, 0, 0);
}

function CncNormalizeSummaryType(cluster) {
    var type = cluster.evidence.type;
    if (type === 'planar' && cluster.radialNormalAlignment >= 0.85 && cluster.axialNormalAlignment <= 0.35) {
        type = 'cylindrical';
    }
    cluster.evidence.type = type;
    cluster.evidence.normal = type === 'planar' ? cluster.evidence.normal : null;
    cluster.evidence.axis = type === 'cylindrical' || type === 'conical'
        ? CncCloneVector(cluster.axisCandidate) : null;
    cluster.memberIndexes = [];
    return cluster;
}

function CncMergeClusterSummary(target, source, forceUnresolved) {
    var previousArea = target.evidence.areaMm2;
    var sourceArea = source.evidence.areaMm2;
    var combinedArea = previousArea + sourceArea;
    var targetType = target.evidence.type;
    var sourceType = source.evidence.type;
    var mergedType = !forceUnresolved && targetType === sourceType ? targetType : 'unresolved';

    target.evidence.centroid = CncScale(CncAdd(
        CncScale(target.evidence.centroid, previousArea),
        CncScale(source.evidence.centroid, sourceArea)), 1 / Math.max(combinedArea, CNC_EPSILON));
    if (mergedType === 'planar' && target.evidence.normal && source.evidence.normal) {
        target.evidence.normal = CncNormalize(CncAdd(
            CncScale(target.evidence.normal, previousArea),
            CncScale(source.evidence.normal, sourceArea)));
    } else {
        target.evidence.normal = null;
    }
    target.evidence.type = mergedType;
    target.evidence.axis = mergedType === 'cylindrical' || mergedType === 'conical'
        ? CncCloneVector(target.evidence.axis || source.evidence.axis) : null;
    target.evidence.areaMm2 = combinedArea;
    target.triangleCount += source.triangleCount;
    target.directionAreas = target.directionAreas.map(function (area, index) {
        return area + source.directionAreas[index];
    });
    target.axialMinimum = Math.min(target.axialMinimum, source.axialMinimum);
    target.axialMaximum = Math.max(target.axialMaximum, source.axialMaximum);
    target.radialMinimumMm = Math.min(target.radialMinimumMm, source.radialMinimumMm);
    target.radialMaximumMm = Math.max(target.radialMaximumMm, source.radialMaximumMm);
    target.axialNormalAlignment = (
        target.axialNormalAlignment * previousArea + source.axialNormalAlignment * sourceArea)
        / Math.max(combinedArea, CNC_EPSILON);
    target.radialNormalAlignment = (
        target.radialNormalAlignment * previousArea + source.radialNormalAlignment * sourceArea)
        / Math.max(combinedArea, CNC_EPSILON);
    target.radialNormalSign = (
        target.radialNormalSign * previousArea + source.radialNormalSign * sourceArea)
        / Math.max(combinedArea, CNC_EPSILON);
    target.normalConsistency = (
        target.normalConsistency * previousArea + source.normalConsistency * sourceArea)
        / Math.max(combinedArea, CNC_EPSILON);
    target.radialFitDeviationRatio = Math.max(target.radialFitDeviationRatio, source.radialFitDeviationRatio);
    target.radialDeviationRatio = target.radialMaximumMm > CNC_EPSILON
        ? (target.radialMaximumMm - target.radialMinimumMm) / target.radialMaximumMm : 0;
    target.evidence.radialExtentMm = mergedType === 'cylindrical' || mergedType === 'conical'
        ? target.radialMaximumMm : 0;
    target.evidence.radiusMm = mergedType === 'cylindrical' || mergedType === 'conical'
        ? (target.radialMinimumMm + target.radialMaximumMm) * 0.5 : 0;
    target.evidence.diameterMm = target.evidence.radiusMm * 2;
    target.evidence.isInternal = (mergedType === 'cylindrical' || mergedType === 'conical')
        && target.radialNormalSign <= -0.35;
    target.evidence.axialDepthMm = target.axialMaximum > target.axialMinimum
        ? target.axialMaximum - target.axialMinimum : 0;
    target.evidence.confidence = target.evidence.confidence === 'Low' || source.evidence.confidence === 'Low'
        ? 'Low' : (target.evidence.confidence === 'Medium' || source.evidence.confidence === 'Medium' ? 'Medium' : 'High');
    target.memberIndexes = [];
    return target;
}

function CncBoundedSurfaceAnalysis(triangles, axes, origin) {
    var summaries = [];
    var summaryParents = [];
    var boundaryEdges = new Map();
    var peakTopologyTriangles = 0;
    var peakChunkEdgeKeys = 0;
    var peakBoundaryEdges = 0;
    var clusterOverflowed = false;
    var boundaryOverflowed = false;
    var overflowIndex = -1;
    var realSummaryCount = 0;
    var valuesPerChunk = CNC_MAX_SAMPLED_TRIANGLES * 9;

    function findSummary(index) {
        var root = index;
        while (summaryParents[root] !== root) { root = summaryParents[root]; }
        while (summaryParents[index] !== index) {
            var parent = summaryParents[index];
            summaryParents[index] = root;
            index = parent;
        }
        return root;
    }

    function mergeSummaryRoots(indexes) {
        var roots = Array.from(new Set(indexes.map(findSummary))).sort(function (left, right) { return left - right; });
        var targetIndex = roots[0];
        for (var index = 1; index < roots.length; index++) {
            var sourceIndex = roots[index];
            CncMergeClusterSummary(summaries[targetIndex], summaries[sourceIndex], false);
            summaries[sourceIndex] = null;
            summaryParents[sourceIndex] = targetIndex;
        }
        return targetIndex;
    }

    function createSummary(cluster) {
        var index = summaries.length;
        summaries.push(CncNormalizeSummaryType(cluster));
        summaryParents.push(index);
        realSummaryCount += 1;
        return index;
    }

    function mergeIntoOverflow(cluster) {
        clusterOverflowed = true;
        if (overflowIndex < 0) {
            overflowIndex = summaries.length;
            summaries.push(CncNormalizeSummaryType(cluster));
            summaries[overflowIndex].evidence.type = 'unresolved';
            summaries[overflowIndex].evidence.normal = null;
            summaries[overflowIndex].evidence.axis = null;
            summaryParents.push(overflowIndex);
        } else {
            CncMergeClusterSummary(summaries[overflowIndex], cluster, true);
        }
        return overflowIndex;
    }

    function boundaryMatches(record, edgeKey) {
        var matches = [];
        var representatives = boundaryEdges.get(edgeKey);
        if (!representatives) { return matches; }
        representatives.forEach(function (representative) {
            if (CncDot(record.normal, representative.normal) >= CNC_CLUSTER_NORMAL_DOT) {
                matches.push(findSummary(representative.summaryIndex));
            }
        });
        return matches;
    }

    function isChunkBoundaryEdge(record, edgeKey, records) {
        var classes = records.boundaryEdgeClasses.get(edgeKey);
        return classes ? classes.has(CncNormalClassKey(record.normal)) : false;
    }

    function retainBoundaryRepresentative(record, edgeKey, summaryIndex) {
        if (summaryIndex === overflowIndex) { return; }
        var representatives = boundaryEdges.get(edgeKey);
        if (representatives) {
            boundaryEdges.delete(edgeKey);
        } else {
            representatives = new Map();
        }
        var normalClass = CncNormalClassKey(record.normal);
        var existing = representatives.get(normalClass);
        if (existing && CncDot(record.normal, existing.normal) >= CNC_CLUSTER_NORMAL_DOT) {
            summaryIndex = mergeSummaryRoots([summaryIndex, existing.summaryIndex]);
        } else if (!existing) {
            representatives.set(normalClass, {
                normal: CncCloneVector(record.normal),
                summaryIndex: summaryIndex
            });
        }
        representatives.forEach(function (representative) {
            representative.summaryIndex = findSummary(representative.summaryIndex);
        });
        boundaryEdges.set(edgeKey, representatives);
        while (boundaryEdges.size > CNC_MAX_BOUNDARY_EDGES) {
            boundaryOverflowed = true;
            boundaryEdges.delete(boundaryEdges.keys().next().value);
        }
        peakBoundaryEdges = Math.max(peakBoundaryEdges, boundaryEdges.size);
    }

    for (var start = 0; start < triangles.length; start += valuesPerChunk) {
        var chunk = triangles.slice(start, Math.min(triangles.length, start + valuesPerChunk));
        var records = CncTriangleRecords(chunk, Math.floor(start / 9));
        var components = CncClusterTriangles(records);
        var componentByRecord = new Array(records.length);
        var componentTargets = new Array(components.length);
        peakTopologyTriangles = Math.max(peakTopologyTriangles, records.length);
        peakChunkEdgeKeys = Math.max(peakChunkEdgeKeys, records.edgeKeyCount || 0);

        components.forEach(function (members, componentIndex) {
            members.forEach(function (recordIndex) { componentByRecord[recordIndex] = componentIndex; });
            var linkedSummaries = [];
            members.forEach(function (recordIndex) {
                var record = records[recordIndex];
                CncTriangleEdgeKeys(record).forEach(function (edgeKey) {
                    if (!isChunkBoundaryEdge(record, edgeKey, records)) { return; }
                    linkedSummaries = linkedSummaries.concat(boundaryMatches(record, edgeKey));
                });
            });
            var cluster = CncNormalizeSummaryType(CncClassifyCluster(records, members, axes, origin, 0));
            if (linkedSummaries.length > 0) {
                var linkedTarget = mergeSummaryRoots(linkedSummaries);
                CncMergeClusterSummary(summaries[linkedTarget], cluster, false);
                componentTargets[componentIndex] = linkedTarget;
            } else if (realSummaryCount < CNC_MAX_SURFACE_CLUSTERS - 1) {
                componentTargets[componentIndex] = createSummary(cluster);
            } else {
                componentTargets[componentIndex] = mergeIntoOverflow(cluster);
            }
        });

        records.forEach(function (record, recordIndex) {
            var target = findSummary(componentTargets[componentByRecord[recordIndex]]);
            CncTriangleEdgeKeys(record).forEach(function (edgeKey) {
                if (!isChunkBoundaryEdge(record, edgeKey, records)) { return; }
                retainBoundaryRepresentative(record, edgeKey, target);
            });
        });
    }

    var clusters = [];
    summaries.forEach(function (summary, index) {
        if (!summary || findSummary(index) !== index) { return; }
        summary.evidence.id = 'surface-' + (clusters.length + 1);
        clusters.push(summary);
    });
    if (boundaryOverflowed && clusters.length > 0) {
        var degraded = clusters[0];
        for (var clusterIndex = 1; clusterIndex < clusters.length; clusterIndex++) {
            CncMergeClusterSummary(degraded, clusters[clusterIndex], true);
        }
        degraded.evidence.id = 'surface-1';
        degraded.evidence.type = 'unresolved';
        degraded.evidence.normal = null;
        degraded.evidence.axis = null;
        degraded.evidence.radialExtentMm = 0;
        degraded.evidence.topologyDegraded = true;
        clusters = [degraded];
    }
    return {
        records: [],
        clusters: clusters,
        limits: {
            applied: true,
            maxTopologyTriangles: CNC_MAX_SAMPLED_TRIANGLES,
            peakTopologyTriangles: peakTopologyTriangles,
            maxChunkEdgeKeys: CNC_MAX_SAMPLED_TRIANGLES * 3,
            peakChunkEdgeKeys: peakChunkEdgeKeys,
            maxBoundaryEdges: CNC_MAX_BOUNDARY_EDGES,
            peakBoundaryEdges: peakBoundaryEdges,
            maxNormalClassesPerEdge: CNC_MAX_NORMAL_CLASSES_PER_EDGE,
            maxSurfaceClusters: CNC_MAX_SURFACE_CLUSTERS,
            clusterOverflowed: clusterOverflowed,
            boundaryOverflowed: boundaryOverflowed,
            topologyDegraded: boundaryOverflowed
        }
    };
}

function CncSurfaceAnalysis(triangles, axes) {
    var triangleCount = Math.floor(triangles.length / 9);
    var origin = CncTriangleSoupCentroid(triangles);
    if (triangleCount <= CNC_MAX_SAMPLED_TRIANGLES) {
        var records = CncTriangleRecords(triangles);
        return {
            origin: origin,
            records: records,
            aggregated: false,
            clusters: CncClusterTriangles(records).map(function (members, index) {
                return CncClassifyCluster(records, members, axes, origin, index);
            }),
            limits: {
                applied: false,
                maxTopologyTriangles: CNC_MAX_SAMPLED_TRIANGLES,
                peakTopologyTriangles: records.length,
                maxChunkEdgeKeys: CNC_MAX_SAMPLED_TRIANGLES * 3,
                peakChunkEdgeKeys: records.edgeKeyCount || 0,
                maxBoundaryEdges: CNC_MAX_BOUNDARY_EDGES,
                peakBoundaryEdges: 0,
                maxNormalClassesPerEdge: CNC_MAX_NORMAL_CLASSES_PER_EDGE,
                maxSurfaceClusters: CNC_MAX_SURFACE_CLUSTERS,
                clusterOverflowed: false,
                boundaryOverflowed: false,
                topologyDegraded: false
            }
        };
    }

    var bounded = CncBoundedSurfaceAnalysis(triangles, axes, origin);
    return {
        origin: origin,
        records: bounded.records,
        aggregated: true,
        clusters: bounded.clusters,
        limits: bounded.limits
    };
}

function CncVisibilityDirectionId(axisIndex, positive) {
    return (positive ? 'positive-' : 'negative-') + ['x', 'y', 'z'][axisIndex];
}

// Builds six conservative orthographic depth fields from the real triangle soup. A cluster is
// externally accessible from a direction when at least one interior face sample has a clear tool
// line back to the setup plane. Side-cutting walls are therefore reachable even when their normal
// is perpendicular to the spindle, while caps and intervening bodies still block the tool line.
function CncApplyDirectionalVisibility(surfaceAnalysis, axes) {
    if (!surfaceAnalysis || surfaceAnalysis.aggregated || surfaceAnalysis.records.length === 0) { return; }
    var records = surfaceAnalysis.records;
    var visibilityOrigin = surfaceAnalysis.origin || records.reduce(function (sum, record) {
        return CncAdd(sum, CncScale(record.centroid, 1 / records.length));
    }, CncVector(0, 0, 0));
    var visibilityWinding = records.reduce(function (sum, record) {
        return sum + CncDot(record.normal, CncSubtract(record.centroid, visibilityOrigin)) * record.areaMm2;
    }, 0) < -CNC_EPSILON ? -1 : 1;
    // Signed normals distinguish the rear of a solid only when the mesh actually
    // encloses one. Open sheets and uncapped tubes have no reliable inside/outside;
    // keep their winding-independent local ray and cutter-envelope checks instead.
    var visibilityEdges = new Map();
    records.forEach(function (record) {
        CncTriangleEdgeKeys(record).forEach(function (key) {
            visibilityEdges.set(key, (visibilityEdges.get(key) || 0) + 1);
        });
    });
    var hasClosedVisibilityShell = Array.from(visibilityEdges.values()).every(function (count) { return count === 2; });
    records.hasClosedVisibilityShell = hasClosedVisibilityShell;
    var reliableCylinderAxes = new Map();
    var samplesForRecord = function (record) {
        return [record.centroid, record.vertices[0], record.vertices[1], record.vertices[2]];
    };

    surfaceAnalysis.clusters.forEach(function (cluster) {
        var cylinderAxis = cluster.evidence.type === 'cylindrical' && cluster.evidence.axis
            ? CncNormalize(cluster.evidence.axis) : null;
        // A cylindrical surface's normals must be perpendicular to its axis. A low
        // radial fit error alone can also describe unrelated merged/swept surfaces.
        // Reject that analytic shortcut and retain the local cutter collision checks.
        if (cylinderAxis && cluster.memberIndexes.every(function (recordIndex) {
            return Math.abs(CncDot(records[recordIndex].normal, cylinderAxis)) <= 0.1;
        })) {
            reliableCylinderAxes.set(cluster, cylinderAxis);
        }
        cluster.evidence.triangleIndexes = cluster.memberIndexes.map(function (recordIndex) {
            return records[recordIndex].sourceTriangleIndex;
        });
        cluster.evidence.accessibleDirectionIds = [];
        cluster.evidence.accessibleTriangleIndexesByDirection = {};
    });

    axes.forEach(function (axis, axisIndex) {
        [true, false].forEach(function (positive) {
            var direction = positive ? axis : CncNegate(axis);
            var directionId = CncVisibilityDirectionId(axisIndex, positive);
            var firstPlaneAxis = axes[(axisIndex + 1) % 3];
            var secondPlaneAxis = axes[(axisIndex + 2) % 3];
            var firstMinimum = Infinity;
            var firstMaximum = -Infinity;
            var secondMinimum = Infinity;
            var secondMaximum = -Infinity;
            records.forEach(function (record) {
                samplesForRecord(record).forEach(function (point) {
                    var first = CncDot(point, firstPlaneAxis);
                    var second = CncDot(point, secondPlaneAxis);
                    firstMinimum = Math.min(firstMinimum, first);
                    firstMaximum = Math.max(firstMaximum, first);
                    secondMinimum = Math.min(secondMinimum, second);
                    secondMaximum = Math.max(secondMaximum, second);
                });
            });
            var firstSpan = Math.max(firstMaximum - firstMinimum, CNC_EPSILON);
            var secondSpan = Math.max(secondMaximum - secondMinimum, CNC_EPSILON);
            var binFor = function (value, minimum, span) {
                return Math.min(CNC_VISIBILITY_GRID_SIZE - 1, Math.max(0, Math.floor(
                    (value - minimum) / span * CNC_VISIBILITY_GRID_SIZE)));
            };
            var projected = records.map(function (record) {
                var vertices = record.vertices.map(function (point) {
                    return {
                        first: CncDot(point, firstPlaneAxis),
                        second: CncDot(point, secondPlaneAxis),
                        depth: CncDot(point, direction)
                    };
                });
                return {
                    vertices: vertices,
                    point: {
                        first: CncDot(record.centroid, firstPlaneAxis),
                        second: CncDot(record.centroid, secondPlaneAxis),
                        depth: CncDot(record.centroid, direction)
                    },
                    firstMinimum: Math.min(vertices[0].first, vertices[1].first, vertices[2].first),
                    firstMaximum: Math.max(vertices[0].first, vertices[1].first, vertices[2].first),
                    secondMinimum: Math.min(vertices[0].second, vertices[1].second, vertices[2].second),
                    secondMaximum: Math.max(vertices[0].second, vertices[1].second, vertices[2].second)
                };
            });
            var recordsByCell = new Map();
            projected.forEach(function (triangle, recordIndex) {
                var firstStart = binFor(triangle.firstMinimum, firstMinimum, firstSpan);
                var firstEnd = binFor(triangle.firstMaximum, firstMinimum, firstSpan);
                var secondStart = binFor(triangle.secondMinimum, secondMinimum, secondSpan);
                var secondEnd = binFor(triangle.secondMaximum, secondMinimum, secondSpan);
                for (var firstBin = firstStart; firstBin <= firstEnd; firstBin += 1) {
                    for (var secondBin = secondStart; secondBin <= secondEnd; secondBin += 1) {
                        var key = firstBin + '|' + secondBin;
                        if (!recordsByCell.has(key)) { recordsByCell.set(key, []); }
                        recordsByCell.get(key).push(recordIndex);
                    }
                }
            });
            var depthAtProjectedPoint = function (triangle, point) {
                var a = triangle.vertices[0];
                var b = triangle.vertices[1];
                var c = triangle.vertices[2];
                var denominator = ((b.second - c.second) * (a.first - c.first))
                    + ((c.first - b.first) * (a.second - c.second));
                if (Math.abs(denominator) <= CNC_EPSILON) { return null; }
                var firstWeight = (((b.second - c.second) * (point.first - c.first))
                    + ((c.first - b.first) * (point.second - c.second))) / denominator;
                var secondWeight = (((c.second - a.second) * (point.first - c.first))
                    + ((a.first - c.first) * (point.second - c.second))) / denominator;
                var thirdWeight = 1 - firstWeight - secondWeight;
                var insideTolerance = -1e-7;
                if (firstWeight < insideTolerance || secondWeight < insideTolerance || thirdWeight < insideTolerance) {
                    return null;
                }
                return (firstWeight * a.depth) + (secondWeight * b.depth) + (thirdWeight * c.depth);
            };
            var depthTolerance = Math.max(firstSpan, secondSpan, 1) * 1e-5;
            var silhouetteClearance = Math.max(firstSpan, secondSpan, 1) * 1e-4;
            var toolLineCandidates = function (record) {
                var alignment = CncDot(record.normal, direction);
                var lateralNormal = CncSubtract(record.normal, CncScale(direction, alignment));
                var lateralDirection = CncLength(lateralNormal) <= CNC_EPSILON
                    ? null
                    : CncNormalize(lateralNormal);
                // Project one parallel tool line through the triangle interior. Sampling near a
                // vertex lets the ray escape beside a pocket lip and then incorrectly colors the
                // entire recessed triangle as reachable. Parallel clearance rays around the tool
                // centre reserve the radius of the smallest supported milling cutter. A side wall
                // can be cut from either side of the tool axis; testing both offsets keeps access
                // independent of imported triangle winding while retaining the same collision test.
                if (lateralDirection) {
                    var tangentDirection = CncNormalize(CncCross(direction, lateralDirection));
                    // Opposed CAD face tessellations can use different chord divisions.
                    // Compensate only for the measured drift of a nearly axial facet,
                    // capped at 10% of the smallest cutter radius. Do not waive occlusion:
                    // a real lip still has to clear every cutter-envelope sample.
                    var axialDepths = record.vertices.map(function (point) { return CncDot(point, direction); });
                    var facetDrift = Math.abs(alignment) <= 0.01
                        ? Math.min(CNC_MINIMUM_MILLING_TOOL_RADIUS_MM * 0.1,
                            (Math.max.apply(null, axialDepths) - Math.min.apply(null, axialDepths))
                            * Math.abs(alignment) / CncLength(lateralNormal)) : 0;
                    return [-1, 1].map(function (side) {
                        var toolCenter = CncAdd(record.centroid, CncScale(
                            lateralDirection,
                            side * (CNC_MINIMUM_MILLING_TOOL_RADIUS_MM + silhouetteClearance + facetDrift)));
                        return [
                            toolCenter,
                            CncAdd(toolCenter, CncScale(lateralDirection, CNC_MINIMUM_MILLING_TOOL_RADIUS_MM)),
                            CncAdd(toolCenter, CncScale(lateralDirection, -CNC_MINIMUM_MILLING_TOOL_RADIUS_MM)),
                            CncAdd(toolCenter, CncScale(tangentDirection, CNC_MINIMUM_MILLING_TOOL_RADIUS_MM)),
                            CncAdd(toolCenter, CncScale(tangentDirection, -CNC_MINIMUM_MILLING_TOOL_RADIUS_MM))
                        ];
                    });
                }
                return [[
                    record.centroid,
                    CncAdd(record.centroid, CncScale(firstPlaneAxis, CNC_MINIMUM_MILLING_TOOL_RADIUS_MM)),
                    CncAdd(record.centroid, CncScale(firstPlaneAxis, -CNC_MINIMUM_MILLING_TOOL_RADIUS_MM)),
                    CncAdd(record.centroid, CncScale(secondPlaneAxis, CNC_MINIMUM_MILLING_TOOL_RADIUS_MM)),
                    CncAdd(record.centroid, CncScale(secondPlaneAxis, -CNC_MINIMUM_MILLING_TOOL_RADIUS_MM))
                ]];
            };
            var hasClearToolLine = function (recordIndex, ignoredRecordIndexes, contactPoint) {
                var record = contactPoint
                    ? Object.assign({}, records[recordIndex], { centroid: contactPoint }) : records[recordIndex];
                return toolLineCandidates(record).some(function (candidate) {
                    return candidate.every(function (sample) {
                        return hasClearProjectedLine(sample, ignoredRecordIndexes);
                    });
                });
            };
            var hasClearProjectedLine = function (sample, ignoredRecordIndexes) {
                var point = {
                    first: CncDot(sample, firstPlaneAxis),
                    second: CncDot(sample, secondPlaneAxis),
                    depth: CncDot(sample, direction)
                };
                var key = binFor(point.first, firstMinimum, firstSpan) + '|'
                    + binFor(point.second, secondMinimum, secondSpan);
                return !(recordsByCell.get(key) || []).some(function (candidateIndex) {
                    if (ignoredRecordIndexes.has(candidateIndex)) { return false; }
                    var candidateDepth = depthAtProjectedPoint(projected[candidateIndex], point);
                    return candidateDepth !== null && candidateDepth > point.depth + depthTolerance;
                });
            };
            var requiresLocalProjectionHorizon = function (cluster) {
                var evidence = cluster && cluster.evidence;
                return !!evidence && (evidence.type === 'cylindrical' || evidence.type === 'conical');
            };
            var isBehindReliableStraightHorizon = function (cluster, record) {
                var evidence = cluster && cluster.evidence;
                // Keep the exact analytic cutoff for cylindrical regions. Conical summaries
                // can represent the coarse global fit of a bent/swept CAD face (as on the
                // UADL150 handle), so those must use the local projection test below.
                if (!evidence || evidence.type !== 'cylindrical' || !evidence.axis || !evidence.centroid
                    || cluster.radialFitDeviationRatio > 0.30) {
                    return false;
                }
                var surfaceAxis = reliableCylinderAxes.get(cluster);
                if (!surfaceAxis) { return false; }
                var approach = CncSubtract(direction, CncScale(surfaceAxis, CncDot(direction, surfaceAxis)));
                var radial = CncSubtract(record.centroid, evidence.centroid);
                radial = CncSubtract(radial, CncScale(surfaceAxis, CncDot(radial, surfaceAxis)));
                approach = CncNormalize(approach);
                radial = CncNormalize(radial);
                return !!approach && !!radial && CncDot(radial, approach) < -0.02;
            };
            surfaceAnalysis.clusters.forEach(function (cluster) {
                var exposedTriangleIndexes = cluster.memberIndexes.filter(function (recordIndex) {
                    if (hasClosedVisibilityShell
                        && visibilityWinding * CncDot(records[recordIndex].normal, direction) < -CNC_FLUTE_TANGENT_ALIGNMENT) {
                        return false;
                    }
                    // Flute-clearance samples can ignore the target triangle and its immediate
                    // tessellation neighbours because those facets form the same local cutter
                    // contact patch. The projection-horizon ray must ignore only the target:
                    // at a curved silhouette an adjacent facet can be the real front skin that
                    // blocks an undercut, as it is on the swept UADL150 handle.
                    var localSurfaceIndexes = new Set([recordIndex]);
                    records[recordIndex].neighbors.forEach(function (neighborIndex) {
                        localSurfaceIndexes.add(neighborIndex);
                    });
                    var projectionHorizonIgnoredIndexes = new Set([recordIndex]);
                    records[recordIndex].neighbors.forEach(function (neighborIndex) {
                        // A smooth CAD face is tessellated into adjacent facets whose projected
                        // depths differ by tiny amounts. Those facets are the same local contact
                        // patch, not separate stock in front of the cutter. Keep sharp folds and
                        // undercut neighbours as occluders.
                        if (CncDot(records[recordIndex].normal, records[neighborIndex].normal)
                            >= CNC_SMOOTH_NEIGHBOR_COSINE) {
                            projectionHorizonIgnoredIndexes.add(neighborIndex);
                        }
                    });
                    var directionAlignment = Math.abs(CncDot(records[recordIndex].normal, direction));
                    // Only a genuinely axial extrusion is exempt from the fitted radial
                    // horizon. Near-tangent facets on the back of a curved handle are still
                    // undercuts; the broader contact-angle tolerance must not promote them.
                    if (directionAlignment > 1e-4
                        && isBehindReliableStraightHorizon(cluster, records[recordIndex])) { return false; }
                    // Cylindrical/conical evidence may describe a bent or swept CAD face. A
                    // single fitted axis and centroid cannot define its visibility horizon.
                    // For a non-tangent curved contact, require the projected centroid ray to
                    // reach this local patch before considering offset flute-contact rays. This
                    // prevents the cutter escaping around the silhouette into the rear half.
                    // A tangent wall is cut by the flute with the tool centre offset beside the
                    // CAD face. Sending the projection ray through the face centroid crosses the
                    // opposite skin of a cylinder and creates alternating red tessellation bands.
                    // The offset tool-line candidates below are the correct collision test there.
                    if (requiresLocalProjectionHorizon(cluster)
                        && directionAlignment > CNC_FLUTE_TANGENT_ALIGNMENT
                        && !hasClearProjectedLine(
                            records[recordIndex].centroid,
                            projectionHorizonIgnoredIndexes)) {
                        return false;
                    }
                    // Tangent flute paths may ignore only genuinely smooth neighbours. Ignoring
                    // every adjacent facet lets the offset cutter centre escape through a sharp
                    // fold and incorrectly reach an undercut.
                    var toolLineIgnoredIndexes = directionAlignment <= CNC_FLUTE_TANGENT_ALIGNMENT
                        ? projectionHorizonIgnoredIndexes
                        : localSurfaceIndexes;
                    var locallyClear = hasClearToolLine(recordIndex, toolLineIgnoredIndexes);
                    // Red/green describes directional access to the CAD face. Near a planar
                    // silhouette the cutter-envelope offsets belong to remaining-stock analysis;
                    // they must not create alternating red tessellation bands when the tool
                    // centreline reaches the face itself.
                    var visible = locallyClear || (cluster.evidence.type === 'planar'
                        && hasClearProjectedLine(records[recordIndex].centroid, localSurfaceIndexes));
                    if (!visible || cluster.evidence.type !== 'planar' || directionAlignment > 1e-4) { return visible; }
                    // Long planar triangles can straddle the shadow of a raised feature.
                    // Probe corners just inside the numerical ray tolerance, not a fixed
                    // fraction of triangle length: a proportional inset can skip several
                    // millimetres of hidden material. The small physical inset avoids exact
                    // tessellation seams while preserving flute/centreline alternatives.
                    var target = records[recordIndex];
                    return target.vertices.every(function (vertex) {
                        var toInterior = CncSubtract(target.centroid, vertex);
                        var length = CncLength(toInterior);
                        var fraction = length > CNC_EPSILON ? Math.min(1, depthTolerance * 4 / length) : 1;
                        var point = CncAdd(vertex, CncScale(toInterior, fraction));
                        return hasClearToolLine(recordIndex, toolLineIgnoredIndexes, point)
                            || hasClearProjectedLine(point, localSurfaceIndexes);
                    });
                }).map(function (recordIndex) {
                    return records[recordIndex].sourceTriangleIndex;
                });
                cluster.evidence.accessibleTriangleIndexesByDirection[directionId] = exposedTriangleIndexes;
                if (exposedTriangleIndexes.length > 0) {
                    cluster.evidence.accessibleDirectionIds.push(directionId);
                }
            });
        });
    });
}

function CncRotationalCandidate(triangles, axes, dimensions, origin, axisIndex, clusterAnalysis) {
    var axis = axes[axisIndex];
    var firstRadialAxis = axes[(axisIndex + 1) % 3];
    var secondRadialAxis = axes[(axisIndex + 2) % 3];
    var radialByBin = new Array(CNC_ROTATIONAL_ANGLE_BINS).fill(0);
    var maximumRadius = 0;
    var axialMinimum = Infinity;
    var axialMaximum = -Infinity;
    for (var vertexIndex = 0; vertexIndex < triangles.length; vertexIndex += 3) {
        var vertex = CncVector(triangles[vertexIndex], triangles[vertexIndex + 1], triangles[vertexIndex + 2]);
        var relative = CncSubtract(vertex, origin);
        var first = CncDot(relative, firstRadialAxis);
        var second = CncDot(relative, secondRadialAxis);
        var radius = Math.sqrt(first * first + second * second);
        var angle = Math.atan2(second, first);
        if (angle < 0) { angle += Math.PI * 2; }
        var bin = Math.min(CNC_ROTATIONAL_ANGLE_BINS - 1, Math.floor(angle / (Math.PI * 2) * CNC_ROTATIONAL_ANGLE_BINS));
        radialByBin[bin] = Math.max(radialByBin[bin], radius);
        maximumRadius = Math.max(maximumRadius, radius);
        var axial = CncDot(relative, axis);
        axialMinimum = Math.min(axialMinimum, axial);
        axialMaximum = Math.max(axialMaximum, axial);
    }

    var populated = radialByBin.filter(function (radius) { return radius >= maximumRadius * 0.85; });
    var angularCoverage = populated.length / CNC_ROTATIONAL_ANGLE_BINS;
    var relevantArea = 0;
    var rotationalClusterArea = 0;
    var outwardRotationalArea = 0;
    var weightedRadialDeviation = 0;
    clusterAnalysis.forEach(function (cluster) {
        var evidence = cluster.evidence;
        var axialCap = evidence.type === 'planar' && evidence.normal
            && Math.abs(CncDot(evidence.normal, axis)) >= 0.98;
        if (!axialCap) { relevantArea += evidence.areaMm2; }
        var coaxialRotational = (evidence.type === 'cylindrical' || evidence.type === 'conical')
            && evidence.axis
            && Math.abs(CncDot(evidence.axis, axis)) >= 0.98;
        if (!coaxialRotational) { return; }
        rotationalClusterArea += evidence.areaMm2;
        if (cluster.radialNormalSign >= 0.35) {
            outwardRotationalArea += evidence.areaMm2;
            weightedRadialDeviation += cluster.radialFitDeviationRatio * evidence.areaMm2;
        }
    });
    var clusterCoverage = relevantArea > CNC_EPSILON ? rotationalClusterArea / relevantArea : 0;
    var circularFaceCoverage = Math.min(angularCoverage, clusterCoverage);
    var radialDeviationRatio = outwardRotationalArea > CNC_EPSILON
        ? weightedRadialDeviation / outwardRotationalArea
        : 1;
    var firstExtent = dimensions[(axisIndex + 1) % 3];
    var secondExtent = dimensions[(axisIndex + 2) % 3];
    var extentDifferenceRatio = Math.max(firstExtent, secondExtent) > CNC_EPSILON
        ? Math.abs(firstExtent - secondExtent) / Math.max(firstExtent, secondExtent)
        : 1;
    // Detailed external threads are often tessellated into many oblique helical facets. The
    // circular envelope can therefore be precise while classified coaxial-surface coverage falls
    // just below the general threshold. Accept that narrower case without relaxing radial fit or
    // equal-extent protection for genuinely non-round parts.
    var preciseFragmentedEnvelope = radialDeviationRatio <= 0.005
        && circularFaceCoverage >= 0.45;
    var eligible = radialDeviationRatio <= CNC_ROTATIONAL_RADIAL_DEVIATION_LIMIT
        && extentDifferenceRatio <= CNC_ROTATIONAL_EXTENT_DIFFERENCE_LIMIT
        && (circularFaceCoverage >= CNC_ROTATIONAL_COVERAGE_MINIMUM || preciseFragmentedEnvelope);
    return {
        eligible: eligible,
        axis: axis,
        diameterMm: maximumRadius * 2,
        lengthMm: axialMaximum > axialMinimum ? axialMaximum - axialMinimum : 0,
        radialDeviationRatio: radialDeviationRatio,
        circularFaceCoverage: circularFaceCoverage,
        confidence: eligible && circularFaceCoverage >= 0.85 && radialDeviationRatio <= 0.02 ? 'High' : (eligible ? 'Medium' : 'Low'),
        extentDifferenceRatio: extentDifferenceRatio
    };
}

function CncRotationalEvidence(triangles, axes, dimensions, origin, clusterAnalysis) {
    var candidates = axes.map(function (_, axisIndex) {
        return CncRotationalCandidate(triangles, axes, dimensions, origin, axisIndex, clusterAnalysis);
    });
    candidates.sort(function (left, right) {
        if (left.eligible !== right.eligible) { return left.eligible ? -1 : 1; }
        if (left.circularFaceCoverage !== right.circularFaceCoverage) { return right.circularFaceCoverage - left.circularFaceCoverage; }
        if (left.radialDeviationRatio !== right.radialDeviationRatio) { return left.radialDeviationRatio - right.radialDeviationRatio; }
        return left.extentDifferenceRatio - right.extentDifferenceRatio;
    });
    var selected = candidates[0] || {
        eligible: false, axis: null, diameterMm: 0, lengthMm: 0,
        radialDeviationRatio: 1, circularFaceCoverage: 0, confidence: 'Low'
    };
    return {
        eligible: selected.eligible,
        axis: CncCloneVector(selected.axis),
        diameterMm: selected.diameterMm,
        lengthMm: selected.lengthMm,
        radialDeviationRatio: selected.radialDeviationRatio,
        circularFaceCoverage: selected.circularFaceCoverage,
        confidence: selected.confidence
    };
}

function CncOpposedPlanarCoverage(surfaceClusters, axes, minimumDimension) {
    var totalArea = surfaceClusters.reduce(function (total, cluster) { return total + cluster.areaMm2; }, 0);
    if (totalArea <= CNC_EPSILON) { return 0; }
    var bestCoverage = 0;
    var bestDimensionDifference = Infinity;
    axes.forEach(function (axis) {
        var positiveArea = 0;
        var negativeArea = 0;
        var positiveCentroid = 0;
        var negativeCentroid = 0;
        surfaceClusters.forEach(function (cluster) {
            if (cluster.type !== 'planar' || !cluster.normal) { return; }
            var alignment = CncDot(cluster.normal, axis);
            if (alignment >= 0.98) {
                positiveArea += cluster.areaMm2;
                positiveCentroid += CncDot(cluster.centroid, axis) * cluster.areaMm2;
            } else if (alignment <= -0.98) {
                negativeArea += cluster.areaMm2;
                negativeCentroid += CncDot(cluster.centroid, axis) * cluster.areaMm2;
            }
        });
        if (positiveArea <= CNC_EPSILON || negativeArea <= CNC_EPSILON) { return; }
        var separation = Math.abs((positiveCentroid / positiveArea) - (negativeCentroid / negativeArea));
        var dimensionDifference = Math.abs(separation - minimumDimension);
        var coverage = (positiveArea + negativeArea) / totalArea;
        if (dimensionDifference < bestDimensionDifference) {
            bestDimensionDifference = dimensionDifference;
            bestCoverage = coverage;
        }
    });
    return bestCoverage;
}

// Sweep the bore disk toward each entry, not a ray from a cylindrical wall sample.
// Clip every potential obstruction at the bore bottom, then test its projected polygon
// against the disk. This includes off-centre obstructions which a centre ray misses.
function CncHoleEntryDirections(records, center, axis, radius, minimum, maximum, ignored, signs) {
    var first = CncNormalize(CncCross(axis, Math.abs(axis.z) < 0.9 ? CncVector(0, 0, 1) : CncVector(0, 1, 0)));
    var second = CncCross(axis, first);
    var tolerance = Math.max(1e-5, radius * 1e-4);
    return (signs || [1, -1]).filter(function (sign) {
        var bottom = (sign > 0 ? minimum : -maximum) + tolerance;
        return !records.some(function (record, index) {
            if (ignored && ignored.has(index)) { return false; }
            var vertices = record.vertices.map(function (point) {
                var delta = CncSubtract(point, center);
                return { x: CncDot(delta, first), y: CncDot(delta, second), depth: CncDot(delta, axis) * sign };
            });
            var polygon = [];
            vertices.forEach(function (point, vertexIndex) {
                var next = vertices[(vertexIndex + 1) % vertices.length];
                var inside = point.depth > bottom;
                var nextInside = next.depth > bottom;
                if (inside) { polygon.push(point); }
                if (inside !== nextInside) {
                    var fraction = (bottom - point.depth) / (next.depth - point.depth);
                    polygon.push({ x: point.x + fraction * (next.x - point.x), y: point.y + fraction * (next.y - point.y) });
                }
            });
            if (polygon.length === 0) { return false; }
            var positive = false;
            var negative = false;
            var minDistanceSquared = Infinity;
            polygon.forEach(function (a, vertexIndex) {
                var b = polygon[(vertexIndex + 1) % polygon.length];
                var dx = b.x - a.x;
                var dy = b.y - a.y;
                var lengthSquared = dx * dx + dy * dy;
                var fraction = lengthSquared > CNC_EPSILON
                    ? Math.max(0, Math.min(1, -(a.x * dx + a.y * dy) / lengthSquared)) : 0;
                var px = a.x + fraction * dx;
                var py = a.y + fraction * dy;
                minDistanceSquared = Math.min(minDistanceSquared, px * px + py * py);
                var cross = a.x * b.y - a.y * b.x;
                positive = positive || cross > CNC_EPSILON;
                negative = negative || cross < -CNC_EPSILON;
            });
            var containsCenter = polygon.length >= 3 && positive !== negative;
            return containsCenter || minDistanceSquared < Math.pow(Math.max(0, radius - tolerance), 2);
        });
    }).map(function (sign) { return CncScale(axis, sign); });
}

// Conservative full-cylinder clearance above the feature mouth. This intentionally
// does not infer a large spotting cutter's access from the smaller pilot corridor.
function CncSpotEntryEvidence(records, center, axis, minimum, maximum, ignored, signs) {
    var evidence = { byDiameterMm: {}, holderDiameterMm: 25, holderStartAboveEntryMm: 20 };
    [10, 12, 16].forEach(function (diameter) {
        evidence.byDiameterMm[String(diameter)] = (signs || [1, -1]).filter(function (sign) {
            var entry = sign > 0 ? maximum : minimum;
            var holderEntry = entry + sign * evidence.holderStartAboveEntryMm;
            return CncHoleEntryDirections(records, center, axis, diameter * 0.5, entry, entry, ignored, [sign]).length > 0
                && CncHoleEntryDirections(records, center, axis, evidence.holderDiameterMm * 0.5,
                    holderEntry, holderEntry, ignored, [sign]).length > 0;
        }).map(function (sign) { return CncScale(axis, sign); });
    });
    return evidence;
}

// Smooth connected components can include a pocket floor, walls and several fillet radii.
// Recover local cylindrical strips from adjacent facets instead of fitting that entire
// component to one cylinder about its centroid. Three agreeing axial seams are required;
// isolated circumcircles on toroidal/freeform tessellation are not radius evidence.
function CncLocalFilletFeatures(cluster, records, windingSign) {
    var groups = [];
    var members = new Set(cluster.memberIndexes);
    cluster.memberIndexes.forEach(function (index) {
        var first = records[index];
        first.neighbors.forEach(function (neighbor) {
            if (neighbor <= index || !members.has(neighbor)) { return; }
            var second = records[neighbor];
            var normalDot = CncDot(first.normal, second.normal);
            if (normalDot >= 0.99999 || normalDot <= 0.5
                || CncDot(CncSubtract(second.normal, first.normal),
                    CncSubtract(second.centroid, first.centroid)) * windingSign >= 0) { return; }
            var samePoint = function (a, b) { return CncLength(CncSubtract(a, b)) < 1e-5; };
            var shared = first.vertices.filter(function (a) { return second.vertices.some(function (b) { return samePoint(a, b); }); });
            if (shared.length !== 2) { return; }
            var axis = CncNormalize(CncSubtract(shared[1], shared[0]));
            if (!axis) { return; }
            var other = function (face) { return face.vertices.find(function (point) {
                return !shared.some(function (entry) { return samePoint(point, entry); });
            }); };
            var u = CncRadialVector(other(first), shared[0], axis);
            var v = CncRadialVector(other(second), shared[0], axis);
            var cross = CncCross(u, v);
            var square = CncDot(cross, cross);
            if (square < 1e-12) { return; }
            var offset = CncScale(CncAdd(CncScale(CncCross(v, cross), CncDot(u, u)),
                CncScale(CncCross(cross, u), CncDot(v, v))), 1 / (2 * square));
            var radius = CncLength(offset);
            var center = CncRadialVector(CncAdd(shared[0], offset), CncVector(0, 0, 0), axis);
            var tolerance = Math.max(0.0001, radius * 0.0001);
            var group = groups.find(function (candidate) {
                return Math.abs(CncDot(candidate.axis, axis)) > 0.99999
                    && Math.abs(candidate.radiusMm - radius) <= tolerance
                    && CncLength(CncSubtract(candidate.center, center)) <= tolerance;
            });
            if (!group) {
                group = { axis: axis, center: center, radiusMm: radius, seams: 0, indexes: new Set() };
                groups.push(group);
            }
            group.seams++;
            group.indexes.add(index);
            group.indexes.add(neighbor);
        });
    });
    return groups.filter(function (group) {
        if (group.seams < 3) { return false; }
        var positions = Array.from(group.indexes).flatMap(function (index) { return records[index].vertices; });
        var normals = Array.from(group.indexes).map(function (index) { return records[index].normal; });
        var depths = positions.map(function (point) { return CncDot(point, group.axis); });
        // Tiny strips on a torus can have nearly parallel seams but are not a straight
        // cylindrical fillet. Require an axial span and validate every contributing vertex.
        return normals.some(function (normal) { return CncDot(normal, normals[0]) < 0.9; })
            && Math.max.apply(null, depths) - Math.min.apply(null, depths) >= group.radiusMm
            && positions.every(function (point) {
                return Math.abs(CncLength(CncRadialVector(point, group.center, group.axis)) - group.radiusMm)
                    <= Math.max(0.0002, group.radiusMm * 0.0002);
            });
    }).map(function (group) {
        var indexes = Array.from(group.indexes);
        var sourceIndexes = indexes.map(function (index) { return records[index].sourceTriangleIndex; });
        var visible = cluster.evidence.accessibleTriangleIndexesByDirection;
        return {
            radiusMm: group.radiusMm,
            centerMm: CncCloneVector(group.center),
            axis: group.axis,
            areaMm2: indexes.reduce(function (sum, index) { return sum + records[index].areaMm2; }, 0),
            triangleIndexes: sourceIndexes,
            accessibleDirectionIds: visible ? Object.keys(visible).filter(function (id) {
                // Radius-matched ball finishing must reach the whole cylindrical strip,
                // not merely some other face in the containing smooth component.
                var allowed = new Set(visible[id]);
                return sourceIndexes.every(function (index) { return allowed.has(index); });
            }) : undefined
        };
    });
}

function CncToolSweepVerifier(records) {
    var projectedByApproach = new Map();
    function buildBoundsTree(items) {
        if (!items.length) { return null; }
        var node = { minimumX: Infinity, maximumX: -Infinity,
            minimumY: Infinity, maximumY: -Infinity, maximumDepth: -Infinity };
        items.forEach(function (bounds) {
            node.minimumX = Math.min(node.minimumX, bounds.minimumX);
            node.maximumX = Math.max(node.maximumX, bounds.maximumX);
            node.minimumY = Math.min(node.minimumY, bounds.minimumY);
            node.maximumY = Math.max(node.maximumY, bounds.maximumY);
            node.maximumDepth = Math.max(node.maximumDepth, bounds.maximumDepth);
        });
        if (items.length <= 8) { node.items = items; return node; }
        var alongX = node.maximumX - node.minimumX >= node.maximumY - node.minimumY;
        items.sort(function (a, b) {
            return alongX ? (a.minimumX + a.maximumX) - (b.minimumX + b.maximumX)
                : (a.minimumY + a.maximumY) - (b.minimumY + b.maximumY);
        });
        var middle = Math.floor(items.length / 2);
        node.left = buildBoundsTree(items.slice(0, middle));
        node.right = buildBoundsTree(items.slice(middle));
        return node;
    }
    function sweepClear(centre, approach, section, ignored) {
        var key = [approach.x, approach.y, approach.z].join(',');
        var projected = projectedByApproach.get(key);
        if (!projected) {
            var first = CncNormalize(CncCross(approach, Math.abs(approach.z) < 0.9 ? CncVector(0, 0, 1) : CncVector(0, 1, 0)));
            var second = CncCross(approach, first);
            projected = { first: first, second: second, maximumDepth: -Infinity, records: records.map(function (record, index) {
                var bounds = { index: index, minimumX: Infinity, maximumX: -Infinity,
                    minimumY: Infinity, maximumY: -Infinity, maximumDepth: -Infinity };
                record.vertices.forEach(function (point) {
                    var px = CncDot(point, first), py = CncDot(point, second), depth = CncDot(point, approach);
                    bounds.minimumX = Math.min(bounds.minimumX, px);
                    bounds.maximumX = Math.max(bounds.maximumX, px);
                    bounds.minimumY = Math.min(bounds.minimumY, py);
                    bounds.maximumY = Math.max(bounds.maximumY, py);
                    bounds.maximumDepth = Math.max(bounds.maximumDepth, depth);
                });
                return bounds;
            }) };
            projected.records.forEach(function (bounds) { projected.maximumDepth = Math.max(projected.maximumDepth, bounds.maximumDepth); });
            projected.tree = buildBoundsTree(projected.records.slice());
            projectedByApproach.set(key, projected);
        }
        var start = CncDot(centre, approach) + section.start;
        if (projected.maximumDepth <= start) { return true; }
        var cx = CncDot(centre, projected.first), cy = CncDot(centre, projected.second);
        var radiusSquared = section.radius * section.radius;
        function overlaps(bounds) {
            if (bounds.maximumDepth <= start) { return false; }
            var dx = Math.max(bounds.minimumX - cx, 0, cx - bounds.maximumX);
            var dy = Math.max(bounds.minimumY - cy, 0, cy - bounds.maximumY);
            // Bounds use the full radius and unadjusted axial start, so all
            // tolerance-boundary decisions stay with the original precise test.
            return dx * dx + dy * dy <= radiusSquared;
        }
        var candidates = [];
        function visit(node) {
            if (!node || !overlaps(node)) { return; }
            if (node.items) {
                node.items.forEach(function (bounds) {
                    if ((!ignored || !ignored.has(bounds.index)) && overlaps(bounds)) {
                        candidates.push(records[bounds.index]);
                    }
                });
                return;
            }
            visit(node.left);
            visit(node.right);
        }
        // Parent bounds only prune disjoint subtrees. Every surviving leaf
        // retains the same disk/depth predicate and exact triangle narrow phase.
        visit(projected.tree);
        // Ignore membership was tested using original record indexes above;
        // filtered-array indexes must never be reused against that set.
        return candidates.length === 0 || CncHoleEntryDirections(candidates, centre, approach,
            section.radius, section.start, section.start, null, [1]).length > 0;
    }
    return sweepClear;
}

// Refine only verified concave circles: the inscribed tessellation chords and
// voxel-centred envelopes are not the smooth CAD wall. Every other triangle still
// participates in the full cutter/shank/holder swept-disk obstruction check.
function CncCircularToolContactVerifier(records, surfaceClusters) {
    var bySourceIndex = new Map();
    records.forEach(function (record, index) { bySourceIndex.set(record.sourceTriangleIndex, index); });
    var sweepClear = CncToolSweepVerifier(records);
    var circles = [];
    (surfaceClusters || []).forEach(function (cluster) {
        (cluster.filletFeatures || []).forEach(function (feature) {
            if (!feature.centerMm || !feature.axis || !(feature.radiusMm > 0)) { return; }
            var axis = CncNormalize(feature.axis);
            if (!axis) { return; }
            var indexes = (feature.triangleIndexes || []).map(function (index) { return bySourceIndex.get(index); });
            if (indexes.length === 0 || indexes.some(function (index) { return index === undefined; })) { return; }
            var tolerance = Math.max(1e-4, feature.radiusMm * 1e-4);
            var depths = indexes.flatMap(function (index) {
                return records[index].vertices.map(function (point) { return CncDot(point, axis); });
            });
            var minimum = Math.min.apply(null, depths);
            var maximum = Math.max.apply(null, depths);
            function liesOnCylinder(index) {
                var record = records[index];
                return record && Math.abs(CncDot(record.normal, axis)) <= 1e-4
                    && record.vertices.every(function (point) {
                        var depth = CncDot(point, axis);
                        return depth >= minimum - tolerance && depth <= maximum + tolerance
                            && Math.abs(CncLength(CncRadialVector(point, feature.centerMm, axis)) - feature.radiusMm) <= tolerance;
                    });
            }
            if (maximum - minimum <= tolerance || !indexes.every(liesOnCylinder)) { return; }
            var ignored = new Set(indexes);
            // Radius recovery can omit a strip's terminal facet. Extend only to
            // its own cluster's cylinder wall, never floors, roofs or inner lips.
            (cluster.triangleIndexes || []).forEach(function (sourceIndex) {
                var index = bySourceIndex.get(sourceIndex);
                if (index !== undefined && liesOnCylinder(index)) { ignored.add(index); }
            });
            var terminalEdges = [];
            ignored.forEach(function (index) {
                records[index].vertices.forEach(function (point, vertexIndex, vertices) {
                    var next = vertices[(vertexIndex + 1) % vertices.length];
                    var depth = CncDot(point, axis);
                    if (Math.abs(CncDot(next, axis) - depth) <= tolerance
                        && Math.min(Math.abs(depth - minimum), Math.abs(depth - maximum)) <= tolerance) {
                        terminalEdges.push([point, next]);
                    }
                });
            });
            records.forEach(function (record, index) {
                if (Math.abs(CncDot(record.normal, axis)) < 0.99999) { return; }
                var depth = CncDot(record.vertices[0], axis);
                if (Math.min(Math.abs(depth - minimum), Math.abs(depth - maximum)) > tolerance
                    || !record.vertices.every(function (point) { return Math.abs(CncDot(point, axis) - depth) <= tolerance; })) { return; }
                var rim = terminalEdges.some(function (edge) {
                    var first = record.vertices.findIndex(function (point) { return CncLength(CncSubtract(point, edge[0])) <= 1e-5; });
                    var second = record.vertices.findIndex(function (point) { return CncLength(CncSubtract(point, edge[1])) <= 1e-5; });
                    if (first < 0 || second < 0 || first === second) { return false; }
                    var third = record.vertices[3 - first - second];
                    var chord = CncSubtract(edge[1], edge[0]);
                    var centerSide = CncDot(CncCross(chord, CncSubtract(feature.centerMm, edge[0])), axis);
                    var triangleSide = CncDot(CncCross(chord, CncSubtract(third, edge[0])), axis);
                    // Only the exterior triangle adjoining an actual terminal
                    // chord represents the rim's inscribed-circle sliver. A roof
                    // extending to the centre side must remain an obstruction.
                    return centerSide * triangleSide < -CNC_EPSILON;
                });
                if (rim) { ignored.add(index); }
            });
            circles.push({ axis: axis, center: feature.centerMm, radius: feature.radiusMm,
                minimum: minimum, maximum: maximum, tolerance: tolerance, ignored: ignored });
        });
    });
    return function (sample, direction, tool) {
        if (!sample || !sample.contactPosition || !sample.normal || !direction || !tool
            || tool.family !== 'flat_end_mill' || !(tool.diameterMm > 0)
            || !(tool.usableCutLengthMm > 0) || !(tool.reachMm >= tool.usableCutLengthMm)
            || !(tool.shankDiameterMm > 0) || !(tool.holderDiameterMm > 0)) { return false; }
        var sourceIndex = bySourceIndex.get(sample.sourceTriangleIndex);
        var approach = CncNormalize(direction);
        if (sourceIndex === undefined || !approach) { return false; }
        var point = sample.contactPosition;
        var radius = tool.diameterMm * 0.5;
        return circles.some(function (circle) {
            if (Math.abs(CncDot(circle.axis, approach)) < 0.99999 || radius > circle.radius + circle.tolerance) { return false; }
            var depth = CncDot(point, circle.axis);
            if (depth < circle.minimum - circle.tolerance || depth > circle.maximum + circle.tolerance) { return false; }
            var radial = CncRadialVector(point, circle.center, circle.axis);
            var distance = CncLength(radial);
            if (distance > circle.radius + circle.tolerance) { return false; }
            var flute = circle.ignored.has(sourceIndex) && Math.abs(CncDot(sample.normal, approach)) <= 1e-4;
            var floor = CncDot(sample.normal, approach) >= 0.99999
                && Math.min(Math.abs(depth - circle.minimum), Math.abs(depth - circle.maximum)) <= circle.tolerance;
            if (!flute && !floor) { return false; }
            // The smallest displacement that covers the contact maximizes the
            // continuous clearance; the centre need not sit at the circle rim.
            var offset = Math.max(0, distance - radius);
            var centre = CncAdd(CncSubtract(point, radial), distance > CNC_EPSILON
                ? CncScale(radial, offset / distance) : CncVector(0, 0, 0));
            if (CncLength(CncSubtract(point, centre)) > radius + circle.tolerance) { return false; }
            var clearedRadius = 0;
            return [
                { radius: radius, start: 0 },
                { radius: tool.shankDiameterMm * 0.5, start: tool.usableCutLengthMm },
                { radius: tool.holderDiameterMm * 0.5, start: tool.reachMm }
            ].every(function (section) {
                // Sections start in increasing order. A previously clear wider
                // infinite sweep contains this entire later, narrower sweep.
                if (section.radius <= clearedRadius) { return true; }
                var ignored = offset + section.radius <= circle.radius + circle.tolerance ? circle.ignored : null;
                if (!sweepClear(centre, approach, section, ignored)) { return false; }
                clearedRadius = section.radius;
                return true;
            });
        });
    };
}

function CncFlatToolContactVerifier(records, surfaceClusters) {
    var circularContact = CncCircularToolContactVerifier(records, surfaceClusters);
    var sweepClear = CncToolSweepVerifier(records);
    var sourceIndexes = new Set(records.map(function (record) { return record.sourceTriangleIndex; }));
    return function (sample, direction, tool) {
        if (circularContact(sample, direction, tool)) { return true; }
        if (!sample || !sample.contactPosition || !sample.normal || !sourceIndexes.has(sample.sourceTriangleIndex)
            || !tool || tool.family !== 'flat_end_mill' || !(tool.diameterMm > 0)
            || !(tool.usableCutLengthMm > 0) || !(tool.reachMm >= tool.usableCutLengthMm)
            || !(tool.shankDiameterMm > 0) || !(tool.holderDiameterMm > 0)) { return false; }
        var approach = CncNormalize(direction), normal = CncNormalize(sample.normal);
        if (!approach || !normal) { return false; }
        var alignment = CncDot(normal, approach);
        // A parallel wall contacts the flute at its true radius; the coarse
        // field's padded centre can otherwise invent a smaller-tool cleanup.
        // Forward-facing slopes use the same rim contact. Keep back-facing
        // surfaces excluded and flat floors on their existing disk search.
        if (alignment < -1e-4 || alignment >= 0.99999) { return false; }
        var radial = CncNormalize(CncSubtract(normal, CncScale(approach, alignment)));
        if (!radial) { return false; }
        var centre = CncAdd(sample.contactPosition, CncScale(radial, tool.diameterMm * 0.5));
        var clearedRadius = 0;
        return [
            { radius: tool.diameterMm * 0.5, start: 0 },
            { radius: tool.shankDiameterMm * 0.5, start: tool.usableCutLengthMm },
            { radius: tool.holderDiameterMm * 0.5, start: tool.reachMm }
        ].every(function (section) {
            if (section.radius <= clearedRadius) { return true; }
            // Unlike the analytic-circle case, no triangle may be ignored.
            if (!sweepClear(centre, approach, section, null)) { return false; }
            clearedRadius = section.radius;
            return true;
        });
    };
}

function CncPrismaticContourAxis(cluster, records, axes) {
    if (!cluster.memberIndexes || cluster.memberIndexes.length < 2) { return null; }
    return (axes || []).find(function (axis) {
        return cluster.memberIndexes.every(function (index) {
            return Math.abs(CncDot(records[index].normal, axis)) <= 1e-3;
        });
    }) || null;
}

function CncMarkManufacturingFeature(cluster, type) {
    cluster.evidence.featureType = type;
    cluster.evidence.filletFeatures = [];
    cluster.evidence.curvedFinishingByDirection = {};
}

// STEP supports are untrusted hints: match every imported vertex before using
// their local axis. A part-origin cylinder fit is never a local bore certificate.
function CncMatchLocalSupport(cluster, records, hints, kind) {
    var points = cluster.memberIndexes.flatMap(function (index) { return records[index].vertices; });
    return (hints || []).find(function (hint) {
        if (hint.kind !== kind || !hint.axis || !hint.centerMm || !points.length) { return false; }
        var slope = kind === 'cone' ? Math.tan(hint.halfAngleRadians) : 0;
        return points.every(function (point) {
            var delta = CncSubtract(point, hint.centerMm), axial = CncDot(delta, hint.axis);
            var radial = CncLength(CncSubtract(delta, CncScale(hint.axis, axial)));
            var expected = hint.radiusMm + slope * axial;
            return expected > 0 && Math.abs(radial - expected) <= Math.max(0.015, expected * 0.003);
        }) && cluster.memberIndexes.every(function (index) {
            var record = records[index], delta = CncSubtract(record.centroid, hint.centerMm);
            var radial = CncNormalize(CncSubtract(delta, CncScale(hint.axis, CncDot(delta, hint.axis))));
            var expectedNormal = radial && CncNormalize(CncSubtract(radial, CncScale(hint.axis, slope)));
            return expectedNormal && Math.abs(CncDot(record.normal, expectedNormal)) >= 0.98;
        });
    }) || null;
}

function CncHelicalPhaseEvidence(clusters, records, center, axis) {
    if (!records || !records.length || !center || !axis) { return null; }
    axis = CncNormalize(axis);
    var basis = axis && CncNormalize(CncCross(axis,
        Math.abs(axis.x) < 0.8 ? CncVector(1, 0, 0) : CncVector(0, 1, 0)));
    if (!axis || !basis) { return null; }
    var other = CncCross(axis, basis), seen = new Set(), points = [];
    (clusters || []).forEach(function (cluster) {
        (cluster.memberIndexes || []).forEach(function (index) {
            (records[index].vertices || []).forEach(function (point) {
                var key = CncQuantizedVertexKey(point);
                if (seen.has(key)) { return; }
                seen.add(key);
                var delta = CncSubtract(point, center), z = CncDot(delta, axis);
                points.push({ z: z,
                    r: CncLength(CncSubtract(delta, CncScale(axis, z))),
                    angle: Math.atan2(CncDot(delta, other), CncDot(delta, basis)) });
            });
        });
    });
    if (points.length < 40) { return null; }
    var low = Math.min.apply(null, points.map(function (point) { return point.z; }));
    var high = Math.max.apply(null, points.map(function (point) { return point.z; }));
    var majorRadius = Math.max.apply(null, points.map(function (point) { return point.r; }));
    var crests = points.filter(function (point) {
        return point.r >= majorRadius - Math.max(0.015, majorRadius * 0.008);
    });
    if (crests.length < 24 || high - low < 0.6) { return null; }
    var best = { score: 0 }, maximumPitch = Math.min(4, (high - low) / 3);
    for (var pitch = 0.2; pitch <= maximumPitch; pitch += 0.005) {
        [-1, 1].forEach(function (hand) {
            var real = 0, imaginary = 0;
            crests.forEach(function (point) {
                var phase = 2 * Math.PI * point.z / pitch + hand * point.angle;
                real += Math.cos(phase); imaginary += Math.sin(phase);
            });
            var score = Math.hypot(real, imaginary) / crests.length;
            if (score > best.score) { best = { score: score, pitch: pitch, hand: hand }; }
        });
    }
    return best.score >= 0.85 ? {
        pitchMm: Math.round(best.pitch * 1000) / 1000,
        handedness: best.hand === -1 ? 'right' : 'left',
        helicalPhaseCoherence: best.score
    } : null;
}

function CncLocalModeledThreads(holes, groups, clusters, records) {
    var threads = [];
    holes.forEach(function (hole, holeIndex) {
        var group = groups[holeIndex], center = group.centroid, axis = group.axis;
        var basis = CncNormalize(CncCross(axis, Math.abs(axis.x) < 0.8 ? CncVector(1, 0, 0) : CncVector(0, 1, 0)));
        var other = CncCross(axis, basis), low = Infinity, high = -Infinity;
        group.candidates.forEach(function (candidate) { candidate.cluster.memberIndexes.forEach(function (index) {
            records[index].vertices.forEach(function (point) {
                var z = CncDot(CncSubtract(point, center), axis); low = Math.min(low, z); high = Math.max(high, z);
            });
        }); });
        var pilotRadius = hole.diameterMm / 2, seen = new Set(), points = [];
        var local = clusters.filter(function (cluster) {
            return cluster.memberIndexes.length && cluster.memberIndexes.every(function (index) {
                return records[index].vertices.every(function (point) {
                    var delta = CncSubtract(point, center), z = CncDot(delta, axis);
                    var r = CncLength(CncSubtract(delta, CncScale(axis, z)));
                    return z >= low - 0.02 && z <= high + 0.02 && r >= pilotRadius * 0.97 && r <= pilotRadius * 1.35;
                });
            });
        });
        local.forEach(function (cluster) { cluster.memberIndexes.forEach(function (index) {
            records[index].vertices.forEach(function (point) {
                var key = CncQuantizedVertexKey(point); if (seen.has(key)) { return; } seen.add(key);
                var delta = CncSubtract(point, center), z = CncDot(delta, axis);
                points.push({ z: z, r: CncLength(CncSubtract(delta, CncScale(axis, z))),
                    angle: Math.atan2(CncDot(delta, other), CncDot(delta, basis)) });
            });
        }); });
        if (points.length < 40 || local.length < 3) { return; }
        var majorRadius = Math.max.apply(null, points.map(function (point) { return point.r; }));
        if (majorRadius < pilotRadius * 1.04) { return; }
        var roots = points.filter(function (point) { return point.r >= majorRadius - Math.max(0.015, majorRadius * 0.008); });
        if (roots.length < 24) { return; }
        // Helical roots repeat in z while advancing one turn in angle. Ordinary
        // annular grooves may repeat axially, but fail this angular phase test.
        var best = { score: 0 }, maximumPitch = Math.min(4, (high - low) / 3);
        for (var pitch = 0.2; pitch <= maximumPitch; pitch += 0.005) {
            [-1, 1].forEach(function (hand) {
                var real = 0, imaginary = 0;
                roots.forEach(function (point) {
                    var phase = 2 * Math.PI * point.z / pitch + hand * point.angle;
                    real += Math.cos(phase); imaginary += Math.sin(phase);
                });
                var score = Math.hypot(real, imaginary) / roots.length;
                if (score > best.score) { best = { score: score, pitch: pitch, hand: hand }; }
            });
        }
        if (best.score < 0.85) { return; }
        var threadIndexes = new Set();
        local.forEach(function (cluster) {
            CncMarkManufacturingFeature(cluster, 'thread');
            cluster.memberIndexes.forEach(function (index) { threadIndexes.add(index); });
        });
        // The pilot's faceted inscribed circle, not the fitted mean cylinder,
        // bounds its aperture. Ignore only the identified thread and bore walls.
        var clearance = pilotRadius;
        threadIndexes.forEach(function (index) {
            var radial = records[index].vertices.map(function (point) {
                var delta = CncSubtract(point, center);
                return CncSubtract(delta, CncScale(axis, CncDot(delta, axis)));
            });
            radial.forEach(function (point, i) {
                var edge = CncSubtract(radial[(i + 1) % 3], point), square = CncDot(edge, edge);
                var t = square > CNC_EPSILON ? Math.max(0, Math.min(1, -CncDot(point, edge) / square)) : 0;
                clearance = Math.min(clearance, CncLength(CncAdd(point, CncScale(edge, t))));
            });
        });
        hole.entryDirections = CncHoleEntryDirections(records, center, axis, clearance, low, high, threadIndexes);
        var ids = local.map(function (cluster) { return cluster.evidence.id; });
        hole.surfaceClusterIds = ids.slice();
        threads.push({ id: 'thread-' + (threads.length + 1), holeId: hole.id, axis: CncCloneVector(axis),
            surfaceClusterIds: ids, majorDiameterMm: majorRadius * 2, minorDiameterMm: hole.diameterMm,
            pitchMm: Math.round(best.pitch * 1000) / 1000, depthMm: high - low, axialDepthMm: high - low,
            isInternal: true,
            handedness: best.hand === -1 ? 'right' : 'left',
            entryDirections: hole.entryDirections.slice(), confidence: 'Medium', evidence: 'local-helical-root-phase',
            helicalPhaseCoherence: best.score, camCertain: false });
    });
    return threads;
}

function CncPublishClusterAdjacency(clusterAnalysis, records) {
    if (!records || !records.length) { return; }
    var edgeOwners = new Map(), links = new Map();
    clusterAnalysis.forEach(function (cluster) {
        links.set(cluster.evidence.id, new Set());
        (cluster.memberIndexes || []).forEach(function (index) {
            // The existing neighbors list deliberately excludes sharp edges;
            // wall/floor feature connectivity must use the mesh edges themselves.
            CncTriangleEdgeKeys(records[index]).forEach(function (edge) {
                if (!edgeOwners.has(edge)) { edgeOwners.set(edge, new Set()); }
                edgeOwners.get(edge).add(cluster.evidence.id);
            });
        });
    });
    edgeOwners.forEach(function (owners) {
        if (owners.size !== 2) { return; }
        var ids = Array.from(owners);
        links.get(ids[0]).add(ids[1]); links.get(ids[1]).add(ids[0]);
    });
    clusterAnalysis.forEach(function (cluster) {
        cluster.evidence.adjacentClusterIds = Array.from(links.get(cluster.evidence.id)).sort();
    });
}

function CncFeatureProxies(clusterAnalysis, rotationalEvidence, partOrigin, records, axes, analyticSurfaces) {
    CncPublishClusterAdjacency(clusterAnalysis, records);
    var orientationVote = clusterAnalysis.reduce(function (total, cluster) {
        var evidence = cluster.evidence;
        if (evidence.type !== 'planar' || !evidence.normal || !evidence.centroid) { return total; }
        return total + CncDot(evidence.normal, CncSubtract(evidence.centroid, partOrigin)) * evidence.areaMm2;
    }, 0);
    var windingSign = orientationVote < 0 ? -1 : 1;
    clusterAnalysis.forEach(function (cluster) {
        var evidence = cluster.evidence;
        if (evidence.type === 'cylindrical' || evidence.type === 'conical') {
            evidence.isInternal = cluster.radialNormalSign * windingSign <= -0.35;
        }
        if (evidence.localCylinder) {
            evidence.localCylinder.isInternal = evidence.localCylinder.radialNormalSign * windingSign <= -0.35;
        }
        if (records && cluster.memberIndexes && records.length > 0) {
            evidence.filletFeatures = CncLocalFilletFeatures(cluster, records, windingSign);
            // A rounded slot can be coarsely classified as cylindrical even
            // though every wall facet is parallel to one machining axis.
            // Publish the same all-facet contour proof used for freeform walls.
            if (evidence.type === 'freeform' || evidence.type === 'cylindrical') {
                evidence.prismaticContourAxis = CncPrismaticContourAxis(cluster, records, axes);
            }
        }
    });
    // A hole is a local cylindrical feature; the containing body does not need to be a shaft.
    // Group concentric full internal cylinders so a counterbore and its through bore become one
    // stepped-hole proxy whose drill diameter is the smallest cylindrical section.
    var localHoleCylinders = clusterAnalysis.map(function (cluster) {
        var evidence = cluster.evidence;
        var cylinder = evidence.localCylinder || ((!records || !records.length) && evidence.type === 'cylindrical' && evidence.axis ? {
            axis: evidence.axis,
            radiusMm: evidence.radiusMm,
            axialDepthMm: evidence.axialDepthMm,
            radialNormalSign: cluster.radialNormalSign,
            isInternal: evidence.isInternal
        } : null);
        var support = cylinder && records && records.length
            ? CncMatchLocalSupport(cluster, records, analyticSurfaces, 'cylinder') : null;
        return { cluster: cluster, cylinder: cylinder, center: support ? support.centerMm : evidence.centroid };
    }).filter(function (candidate) {
        return candidate.cylinder
            && candidate.cylinder.isInternal === true
            && candidate.cylinder.radialNormalSign * windingSign <= -0.65
            && candidate.cluster.normalConsistency <= 0.35
            && candidate.cylinder.radiusMm > CNC_EPSILON
            && candidate.cylinder.axialDepthMm > CNC_EPSILON;
    });
    function holeCandidateAxialRange(candidate, axis) {
        var projections = [];
        if (records && records.length) {
            candidate.cluster.memberIndexes.forEach(function (recordIndex) {
                (records[recordIndex].vertices || []).forEach(function (point) {
                    projections.push(CncDot(point, axis));
                });
            });
        }
        if (projections.length > 0) {
            return { minimum: Math.min.apply(null, projections), maximum: Math.max.apply(null, projections) };
        }
        var center = CncDot(candidate.center, axis);
        var halfDepth = candidate.cylinder.axialDepthMm / 2;
        return { minimum: center - halfDepth, maximum: center + halfDepth };
    }
    var holeGroups = [];
    localHoleCylinders.forEach(function (candidate) {
        var cluster = candidate.cluster;
        var cylinder = candidate.cylinder;
        var axis = CncNormalize(cylinder.axis);
        var group = holeGroups.find(function (existing) {
            if (Math.abs(CncDot(existing.axis, axis)) < 0.98) { return false; }
            var delta = CncSubtract(candidate.center, existing.centroid);
            var perpendicular = CncSubtract(delta, CncScale(existing.axis, CncDot(delta, existing.axis)));
            if (CncLength(perpendicular) > Math.max(0.15, cylinder.radiusMm * 0.12)) { return false; }
            var range = holeCandidateAxialRange(candidate, existing.axis);
            var axialGap = Math.max(existing.axialMinimum - range.maximum,
                range.minimum - existing.axialMaximum, 0);
            return axialGap <= Math.max(0.05, cylinder.radiusMm * 0.03);
        });
        if (!group) {
            var initialRange = holeCandidateAxialRange(candidate, axis);
            group = { axis: axis, centroid: candidate.center, candidates: [],
                axialMinimum: initialRange.minimum, axialMaximum: initialRange.maximum };
            holeGroups.push(group);
        }
        group.candidates.push(candidate);
        var candidateRange = holeCandidateAxialRange(candidate, group.axis);
        group.axialMinimum = Math.min(group.axialMinimum, candidateRange.minimum);
        group.axialMaximum = Math.max(group.axialMaximum, candidateRange.maximum);
    });
    var holes = holeGroups.map(function (group, index) {
        var radii = group.candidates.map(function (candidate) { return candidate.cylinder.radiusMm; });
        var hole = {
            id: 'hole-' + (index + 1),
            surfaceClusterIds: group.candidates.map(function (candidate) { return candidate.cluster.evidence.id; }),
            axis: CncCloneVector(group.axis),
            diameterMm: Math.min.apply(null, radii) * 2,
            counterboreDiameterMm: Math.max.apply(null, radii) * 2,
            depthMm: Math.max.apply(null, group.candidates.map(function (candidate) {
                return candidate.cylinder.axialDepthMm;
            })),
            confidence: group.candidates.every(function (candidate) {
                return candidate.cluster.evidence.confidence === 'High';
            }) ? 'High' : 'Medium'
        };
        if (Array.isArray(records) && records.length > 0) {
            var ignored = new Set();
            var minimum = Infinity;
            var maximum = -Infinity;
            var corridorRadius = hole.diameterMm * 0.5;
            group.candidates.forEach(function (candidate) {
                candidate.cluster.memberIndexes.forEach(function (recordIndex) {
                    ignored.add(recordIndex);
                    // A circular CAD bore is tessellated as an inscribed polygon. Use its
                    // measured facet clearance for the entry test, while preserving the fitted
                    // nominal diameter for drill selection. Otherwise every aperture rim falsely
                    // intersects a nominal-radius circular sweep.
                    var record = records[recordIndex];
                    var radialNormal = CncSubtract(record.normal,
                        CncScale(group.axis, CncDot(record.normal, group.axis)));
                    var normalLength = CncLength(radialNormal);
                    if (normalLength > 0.99) {
                        corridorRadius = Math.min(corridorRadius, Math.abs(CncDot(
                            CncSubtract(record.centroid, group.centroid), radialNormal)) / normalLength);
                    }
                    records[recordIndex].vertices.forEach(function (point) {
                        var depth = CncDot(CncSubtract(point, group.centroid), group.axis);
                        minimum = Math.min(minimum, depth);
                        maximum = Math.max(maximum, depth);
                    });
                });
            });
            hole.depthMm = Math.max(hole.depthMm, maximum - minimum);
            hole.entryDirections = CncHoleEntryDirections(records, group.centroid, group.axis,
                corridorRadius, minimum, maximum, ignored);
            hole.spotEntryEvidence = CncSpotEntryEvidence(records, group.centroid, group.axis, minimum, maximum, ignored);
        }
        return hole;
    });
    var localThreads = records && records.length ? CncLocalModeledThreads(holes, holeGroups, clusterAnalysis, records) : [];
    var chamfers = [];
    if (records && records.length) {
        clusterAnalysis.forEach(function (cluster) {
            if (cluster.evidence.featureType === 'thread') { return; }
            var support = CncMatchLocalSupport(cluster, records, analyticSurfaces, 'cone');
            if (!support || support.halfAngleRadians < Math.PI / 12 || support.halfAngleRadians > Math.PI * 5 / 12) { return; }
            var radii = [], heights = [], inward = 0, area = 0;
            cluster.memberIndexes.forEach(function (index) {
                var record = records[index], delta = CncSubtract(record.centroid, support.centerMm);
                var radial = CncNormalize(CncSubtract(delta, CncScale(support.axis, CncDot(delta, support.axis))));
                inward += radial ? CncDot(record.normal, radial) * record.areaMm2 * windingSign : 0;
                area += record.areaMm2;
                record.vertices.forEach(function (point) {
                    var d = CncSubtract(point, support.centerMm), z = CncDot(d, support.axis);
                    heights.push(z); radii.push(CncLength(CncSubtract(d, CncScale(support.axis, z))));
                });
            });
            var radialSpan = Math.max.apply(null, radii) - Math.min.apply(null, radii);
            var axialSpan = Math.max.apply(null, heights) - Math.min.apply(null, heights);
            var majorRadius = Math.max.apply(null, radii);
            var inwardCone = inward < -0.2 * area;
            var shortConvexEdgeCone = inward > 0.2 * area
                && radialSpan <= Math.max(3, majorRadius * 0.25)
                && axialSpan <= Math.max(3, majorRadius * 0.25);
            if (!inwardCone && !shortConvexEdgeCone) { return; }
            CncMarkManufacturingFeature(cluster, 'chamfer');
            var maximumHeight = Math.max.apply(null, heights);
            var entries = CncHoleEntryDirections(records, support.centerMm, support.axis,
                Math.max.apply(null, radii), maximumHeight, maximumHeight,
                new Set(cluster.memberIndexes), [1]);
            var chamfer = { id: 'chamfer-' + (chamfers.length + 1), surfaceClusterIds: [cluster.evidence.id],
                axis: CncCloneVector(support.axis), includedAngleDegrees: support.halfAngleRadians * 360 / Math.PI,
                majorDiameterMm: Math.max.apply(null, radii) * 2, minorDiameterMm: Math.min.apply(null, radii) * 2,
                depthMm: Math.max.apply(null, heights) - Math.min.apply(null, heights), confidence: 'High',
                entryDirections: entries, evidence: 'vertex-verified-step-cone' };
            chamfer.spotEntryEvidence = CncSpotEntryEvidence(records, support.centerMm, support.axis,
                maximumHeight, maximumHeight, new Set(cluster.memberIndexes), [1]);
            var minorHeight = Math.min.apply(null, heights);
            holes.some(function (hole, holeIndex) {
                var group = holeGroups[holeIndex];
                if (!group || Math.abs(CncDot(group.axis, support.axis)) < 0.99999
                    || Math.abs(hole.diameterMm - chamfer.minorDiameterMm) > 0.05
                    || !(hole.entryDirections || []).some(function (direction) {
                        return CncDot(direction, support.axis) > 0.99999;
                    })) { return false; }
                var offset = CncSubtract(group.centroid, support.centerMm);
                var eccentricity = CncLength(CncSubtract(offset, CncScale(support.axis, CncDot(offset, support.axis))));
                if (eccentricity > Math.max(0.015, chamfer.minorDiameterMm * 0.0025)) { return false; }
                var pilotMinimum = Infinity, pilotMaximum = -Infinity;
                group.candidates.forEach(function (candidate) { candidate.cluster.memberIndexes.forEach(function (index) {
                    records[index].vertices.forEach(function (point) {
                        var projection = CncDot(CncSubtract(point, support.centerMm), support.axis);
                        pilotMinimum = Math.min(pilotMinimum, projection);
                        pilotMaximum = Math.max(pilotMaximum, projection);
                    });
                }); });
                // A nearby coaxial hole is insufficient. Its measured mouth must
                // meet this cone's minor rim, and its verified entry corridor must
                // continue below that rim far enough for a countersink's tip.
                if (Math.abs(pilotMaximum - minorHeight) > 0.03 || minorHeight <= pilotMinimum) { return false; }
                chamfer.pilotHoleId = hole.id;
                chamfer.pilotDiameterMm = hole.diameterMm;
                chamfer.verifiedPilotDepthBelowMm = minorHeight - pilotMinimum;
                return true;
            });
            chamfers.push(chamfer);
        });
    }
    if (!rotationalEvidence.eligible || !rotationalEvidence.axis) {
        return { holes: holes, threads: localThreads, chamfers: chamfers };
    }
    var outerRadius = rotationalEvidence.diameterMm * 0.5;
    var coaxialInterior = clusterAnalysis.filter(function (cluster) {
        var evidence = cluster.evidence;
        return (evidence.type === 'cylindrical' || evidence.type === 'conical')
            && evidence.featureType !== 'thread'
            && evidence.axis
            && Math.abs(CncDot(evidence.axis, rotationalEvidence.axis)) >= 0.98
            && evidence.isInternal === true
            && cluster.radialMaximumMm < outerRadius * 0.75;
    });
    var threads = localThreads.slice();
    if (coaxialInterior.length > 0) {
        var clusterIds = coaxialInterior.map(function (cluster) { return cluster.evidence.id; });
        var minimumRadius = Math.min.apply(null, coaxialInterior.map(function (cluster) { return cluster.radialMinimumMm; }));
        var maximumRadius = Math.max.apply(null, coaxialInterior.map(function (cluster) { return cluster.radialMaximumMm; }));
        var depth = Math.max.apply(null, coaxialInterior.map(function (cluster) { return cluster.evidence.axialDepthMm; }));
        var radialVariation = maximumRadius > CNC_EPSILON ? (maximumRadius - minimumRadius) / maximumRadius : 0;
        if (!holes.some(function (hole) {
            return hole.surfaceClusterIds.some(function (id) { return clusterIds.indexOf(id) >= 0; });
        })) {
            holes.push({
                id: 'hole-' + (holes.length + 1),
                surfaceClusterIds: clusterIds.slice(),
                axis: CncCloneVector(rotationalEvidence.axis),
                diameterMm: minimumRadius * 2,
                counterboreDiameterMm: maximumRadius * 2,
                depthMm: depth,
                confidence: 'Medium'
            });
        }
        if (radialVariation >= 0.04 || coaxialInterior.length >= 3) {
            var pilot = holes.find(function (hole) {
                return hole.surfaceClusterIds.some(function (id) { return clusterIds.indexOf(id) >= 0; });
            });
            // A modeled thread's fitted cylinder lies between the crests and roots.
            // Drill the minor diameter; the midpoint would intersect every thread ridge.
            pilot.diameterMm = minimumRadius * 2;
            if (Array.isArray(records) && records.length > 0) {
                var threadAxis = rotationalEvidence.axis;
                var threadCenter = coaxialInterior[0].evidence.centroid;
                var threadIndexes = new Set();
                var threadMinimum = Infinity;
                var threadMaximum = -Infinity;
                var pilotClearance = minimumRadius;
                coaxialInterior.forEach(function (cluster) {
                    cluster.memberIndexes.forEach(function (recordIndex) {
                        threadIndexes.add(recordIndex);
                        var radialVertices = records[recordIndex].vertices.map(function (point) {
                            var delta = CncSubtract(point, threadCenter);
                            var axial = CncDot(delta, threadAxis);
                            threadMinimum = Math.min(threadMinimum, axial);
                            threadMaximum = Math.max(threadMaximum, axial);
                            return CncSubtract(delta, CncScale(threadAxis, axial));
                        });
                        radialVertices.forEach(function (a, index) {
                            var edge = CncSubtract(radialVertices[(index + 1) % 3], a);
                            var lengthSquared = CncDot(edge, edge);
                            var fraction = lengthSquared > CNC_EPSILON
                                ? Math.max(0, Math.min(1, -CncDot(a, edge) / lengthSquared)) : 0;
                            pilotClearance = Math.min(pilotClearance, CncLength(CncAdd(a, CncScale(edge, fraction))));
                        });
                    });
                });
                pilot.depthMm = Math.max(pilot.depthMm, threadMaximum - threadMinimum);
                pilot.entryDirections = CncHoleEntryDirections(records, threadCenter, threadAxis,
                    pilotClearance, threadMinimum, threadMaximum, threadIndexes);
            }
            var internalHelix = CncHelicalPhaseEvidence(coaxialInterior, records,
                partOrigin, rotationalEvidence.axis);
            if (internalHelix) {
                coaxialInterior.forEach(function (cluster) { CncMarkManufacturingFeature(cluster, 'thread'); });
                threads.push({
                    id: 'thread-' + (threads.length + 1),
                    surfaceClusterIds: clusterIds.slice(),
                    axis: CncCloneVector(rotationalEvidence.axis),
                    majorDiameterMm: maximumRadius * 2,
                    minorDiameterMm: minimumRadius * 2,
                    axialDepthMm: depth,
                    isInternal: true,
                    pitchMm: internalHelix.pitchMm,
                    handedness: internalHelix.handedness,
                    helicalPhaseCoherence: internalHelix.helicalPhaseCoherence,
                    confidence: 'Medium',
                    evidence: 'coaxial_helical_ridges'
                });
            }
        }
    }

    var coaxialExterior = clusterAnalysis.filter(function (cluster) {
        var evidence = cluster.evidence;
        return (evidence.type === 'cylindrical' || evidence.type === 'conical')
            && evidence.featureType !== 'thread'
            && evidence.axis
            && Math.abs(CncDot(evidence.axis, rotationalEvidence.axis)) >= 0.98
            && evidence.isInternal !== true;
    });
    var shallowRidgeClusters = coaxialExterior.filter(function (cluster) {
        var radialRange = cluster.radialMaximumMm - cluster.radialMinimumMm;
        var radialVariation = cluster.radialMaximumMm > CNC_EPSILON
            ? radialRange / cluster.radialMaximumMm : 0;
        return radialVariation >= 0.04
            && cluster.evidence.axialDepthMm >= radialRange * 6;
    });
    var totalArea = clusterAnalysis.reduce(function (sum, cluster) {
        return sum + cluster.evidence.areaMm2;
    }, 0);
    var nonCoaxialCurvedArea = clusterAnalysis.reduce(function (sum, cluster) {
        var evidence = cluster.evidence;
        var curved = evidence.featureType !== 'thread'
            && (evidence.type === 'cylindrical' || evidence.type === 'conical');
        var coaxial = curved && evidence.axis
            && Math.abs(CncDot(evidence.axis, rotationalEvidence.axis)) >= 0.98;
        return sum + (curved && !coaxial ? evidence.areaMm2 : 0);
    }, 0);
    var fragmentedHelicalEvidence = rotationalEvidence.circularFaceCoverage < CNC_ROTATIONAL_COVERAGE_MINIMUM
        && rotationalEvidence.radialDeviationRatio <= 0.005
        && clusterAnalysis.length >= 12
        && totalArea > CNC_EPSILON
        && nonCoaxialCurvedArea / totalArea >= 0.10;
    if (shallowRidgeClusters.length > 0 || fragmentedHelicalEvidence) {
        var likelyThreadClusters = shallowRidgeClusters.slice();
        if (fragmentedHelicalEvidence) {
            coaxialExterior.forEach(function (cluster) {
                if (cluster.radialMaximumMm < outerRadius * 0.98
                    && likelyThreadClusters.indexOf(cluster) === -1) {
                    likelyThreadClusters.push(cluster);
                }
            });
        }
        if (likelyThreadClusters.length === 0) { likelyThreadClusters = coaxialExterior.slice(); }
        var ownedThreadClusterIds = new Set(likelyThreadClusters.map(function (cluster) {
            return String(cluster.evidence.id);
        }));
        var ownershipChanged = true;
        while (ownershipChanged) {
            ownershipChanged = false;
            clusterAnalysis.forEach(function (cluster) {
                var evidence = cluster.evidence, id = String(evidence.id);
                if (ownedThreadClusterIds.has(id) || evidence.type === 'planar'
                    || evidence.featureType === 'chamfer' || evidence.isInternal === true
                    || !(cluster.radialMaximumMm < outerRadius * 0.98)
                    || !(evidence.type === 'freeform' || evidence.type === 'cylindrical'
                        || evidence.type === 'conical')) { return; }
                if ((evidence.adjacentClusterIds || []).map(String).some(function (adjacentId) {
                    return ownedThreadClusterIds.has(adjacentId);
                })) {
                    likelyThreadClusters.push(cluster);
                    ownedThreadClusterIds.add(id);
                    ownershipChanged = true;
                }
            });
        }
        var externalMinimumRadius = likelyThreadClusters.length > 0
            ? Math.min.apply(null, likelyThreadClusters.map(function (cluster) { return cluster.radialMinimumMm; }))
            : outerRadius * 0.85;
        var externalMaximumRadius = likelyThreadClusters.length > 0
            ? Math.max.apply(null, likelyThreadClusters.map(function (cluster) { return cluster.radialMaximumMm; }))
            : outerRadius;
        var externalDepth = likelyThreadClusters.length > 0
            ? Math.max.apply(null, likelyThreadClusters.map(function (cluster) { return cluster.evidence.axialDepthMm; }))
            : rotationalEvidence.lengthMm;
        var externalHelix = CncHelicalPhaseEvidence(likelyThreadClusters, records,
            partOrigin, rotationalEvidence.axis);
        likelyThreadClusters.forEach(function (cluster) { CncMarkManufacturingFeature(cluster, 'thread'); });
        threads.push({
            id: 'external-thread-1',
            surfaceClusterIds: likelyThreadClusters.map(function (cluster) { return cluster.evidence.id; }),
            axis: CncCloneVector(rotationalEvidence.axis),
            majorDiameterMm: externalMaximumRadius * 2,
            minorDiameterMm: externalMinimumRadius * 2,
            axialDepthMm: externalDepth,
            isInternal: false,
            pitchMm: externalHelix ? externalHelix.pitchMm : undefined,
            handedness: externalHelix ? externalHelix.handedness : undefined,
            helicalPhaseCoherence: externalHelix ? externalHelix.helicalPhaseCoherence : undefined,
            confidence: 'Medium',
            evidence: fragmentedHelicalEvidence ? 'fragmented_helical_envelope' : 'coaxial_external_ridges'
        });
    }
    return { holes: holes, threads: threads, chamfers: chamfers };
}

function CncDirectionCandidates(axes, evidence, surfaceAnalysis, rotationalEvidence) {
    var candidates = [];
    var labels = ['positive-x', 'negative-x', 'positive-y', 'negative-y', 'positive-z', 'negative-z'];
    var directionAreas = evidence.directionAreas.slice();
    surfaceAnalysis.clusters.forEach(function (cluster) {
        var clusterAxis = cluster.evidence.axis;
        var rotationalSidewall = rotationalEvidence.eligible
            && clusterAxis
            && (cluster.evidence.type === 'cylindrical' || cluster.evidence.type === 'conical')
            && Math.abs(CncDot(clusterAxis, rotationalEvidence.axis)) >= 0.98;
        if (!rotationalSidewall) { return; }
        cluster.directionAreas.forEach(function (area, directionIndex) {
            directionAreas[directionIndex] = Math.max(0, directionAreas[directionIndex] - area);
        });
    });
    for (var axis = 0; axis < 3; axis++) {
        for (var sign = 0; sign < 2; sign++) {
            var index = axis * 2 + sign;
            var direction = sign === 0 ? axes[axis] : CncNegate(axes[axis]);
            var projectedArea = directionAreas[index];
            if (projectedArea <= CNC_EPSILON) { continue; }
            candidates.push({
                id: labels[index],
                toolDirection: direction,
                projectedFaceAreaMm2: projectedArea,
                projectedFaceCoverage: evidence.totalArea > 0 ? projectedArea / evidence.totalArea : 0,
                accessibility: 'heuristic'
            });
        }
    }
    return candidates;
}

function CncReviewReasons(modelInfo, sampled, topologyDegraded) {
    var reasons = [];
    if (sampled) { reasons.push('geometry_sampling_applied'); }
    if (topologyDegraded) { reasons.push('topology_boundary_overflow'); }
    if (!modelInfo || modelInfo.nonWatertight) { reasons.push('non_watertight_geometry'); }
    if (modelInfo && modelInfo.nonManifold) { reasons.push('non_manifold_geometry'); }
    if (modelInfo && modelInfo.bodyCount > 1) { reasons.push('multi_body_geometry'); }
    if (modelInfo && modelInfo.oddlySmall) { reasons.push('unusually_small_geometry'); }
    if (modelInfo && modelInfo.oddlyLarge) { reasons.push('unusually_large_geometry'); }
    return reasons;
}

function CncTriangleClosestPoint(point, record) {
    var vertices = record.vertices;
    var relative = CncSubtract(point, vertices[0]);
    var planeDistance = CncDot(relative, record.normal);
    var projected = CncSubtract(point, CncScale(record.normal, planeDistance));
    var inside = true;
    var best = Infinity;
    var closest = vertices[0];
    for (var index = 0; index < 3; index++) {
        var start = vertices[index];
        var edge = CncSubtract(vertices[(index + 1) % 3], start);
        var delta = CncSubtract(projected, start);
        if (CncDot(CncCross(edge, delta), record.normal) < -1e-8) { inside = false; }
        var lengthSquared = CncDot(edge, edge);
        relative = CncSubtract(point, start);
        var fraction = lengthSquared > 0 ? Math.max(0, Math.min(1, CncDot(relative, edge) / lengthSquared)) : 0;
        var offset = CncSubtract(relative, CncScale(edge, fraction));
        var distance = CncDot(offset, offset);
        if (distance < best) {
            best = distance;
            closest = CncSubtract(point, offset);
        }
    }
    return inside ? projected : closest;
}

function CncTriangleDistanceSquared(point, record) {
    var delta = CncSubtract(point, CncTriangleClosestPoint(point, record));
    return CncDot(delta, delta);
}

function CncSurfaceSearchTree(records, surfaceClusters) {
    var owners = new Map();
    surfaceClusters.forEach(function (cluster) {
        (cluster.triangleIndexes || []).forEach(function (index) { owners.set(index, cluster); });
    });
    var entries = (records || []).filter(function (record) { return owners.has(record.sourceTriangleIndex); });
    function build(items) {
        if (items.length === 0) { return null; }
        var minimum = { x: Infinity, y: Infinity, z: Infinity };
        var maximum = { x: -Infinity, y: -Infinity, z: -Infinity };
        items.forEach(function (record) {
            record.vertices.forEach(function (point) {
                ['x', 'y', 'z'].forEach(function (axis) {
                    minimum[axis] = Math.min(minimum[axis], point[axis]);
                    maximum[axis] = Math.max(maximum[axis], point[axis]);
                });
            });
        });
        var node = { minimum: minimum, maximum: maximum };
        if (items.length <= 8) { node.items = items; return node; }
        var axis = ['x', 'y', 'z'].sort(function (a, b) {
            return maximum[b] - minimum[b] - maximum[a] + minimum[a];
        })[0];
        items.sort(function (a, b) { return a.centroid[axis] - b.centroid[axis]; });
        var middle = Math.floor(items.length / 2);
        node.left = build(items.slice(0, middle));
        node.right = build(items.slice(middle));
        return node;
    }
    var tree = build(entries);
    function bound(node, point) {
        return ['x', 'y', 'z'].reduce(function (sum, axis) {
            var distance = Math.max(node.minimum[axis] - point[axis], 0, point[axis] - node.maximum[axis]);
            return sum + distance * distance;
        }, 0);
    }
    return function (sample) {
        var best = null;
        var distance = Infinity;
        function visit(node) {
            if (!node || bound(node, sample.position) > distance + 1e-8) { return; }
            if (node.items) {
                node.items.forEach(function (record) {
                    var candidate = CncTriangleDistanceSquared(sample.position, record);
                    if (candidate < distance - 1e-8 || (Math.abs(candidate - distance) <= 1e-8
                        && (!best || CncDot(sample.normal, record.normal) > CncDot(sample.normal, best.normal)))) {
                        best = record; distance = candidate;
                    }
                });
                return;
            }
            var first = bound(node.left, sample.position) <= bound(node.right, sample.position) ? node.left : node.right;
            visit(first); visit(first === node.left ? node.right : node.left);
        }
        visit(tree);
        return best ? { record: best, cluster: owners.get(best.sourceTriangleIndex) } : null;
    };
}

function CncAssignFieldSamplesToClusters(accessibilityField, surfaceClusters, records) {
    if (!accessibilityField || !Array.isArray(accessibilityField.surfaceSamples)
        || !Array.isArray(surfaceClusters) || surfaceClusters.length === 0) {
        return;
    }
    var nearest = records && records.length > 0 ? CncSurfaceSearchTree(records, surfaceClusters) : null;
    accessibilityField.surfaceSamples.forEach(function (sample) {
        var patch = nearest && nearest(sample);
        if (patch) {
            sample.clusterId = patch.cluster.id;
            sample.sourceTriangleIndex = patch.record.sourceTriangleIndex;
            sample.contactPosition = CncTriangleClosestPoint(sample.position, patch.record);
            // Stair-step voxel normals invent diagonal tool approaches on axial CAD walls.
            // Preserve the field cell/position but use the actual nearest surface orientation.
            sample.normal = CncDot(sample.normal, patch.record.normal) >= 0
                ? patch.record.normal : CncNegate(patch.record.normal);
            return;
        }
        var selected = null;
        var selectedScore = Infinity;
        surfaceClusters.forEach(function (cluster) {
            if (!cluster.centroid) { return; }
            var delta = CncSubtract(sample.position, cluster.centroid);
            var distance = CncLength(delta);
            var score = distance;
            if (cluster.normal) {
                var normal = CncNormalize(cluster.normal);
                var planeDistance = Math.abs(CncDot(delta, normal));
                var normalAgreement = Math.abs(CncDot(sample.normal, normal));
                score = (planeDistance * 8) + (distance * 0.05)
                    + ((1 - normalAgreement) * accessibilityField.cellSizeMm * 8);
            }
            if (score < selectedScore) {
                selected = cluster;
                selectedScore = score;
            }
        });
        sample.clusterId = selected ? selected.id : null;
    });
}

// Surface type is a coarse whole-cluster fit, not a finishing strategy. A swept
// handle can fit a cone poorly while its visible cross-axis skin still needs
// ball finishing. Preserve that fit and expose only the actual curved, sloped
// triangles; axial prism/counterbore walls remain side-flute profile work.
function CncApplyCurvedFinishingEvidence(surfaceAnalysis, axes) {
    surfaceAnalysis.clusters.forEach(function (cluster) {
        var result = {};
        cluster.evidence.curvedFinishingByDirection = result;
        if (cluster.evidence.featureType === 'thread' || cluster.evidence.featureType === 'chamfer') { return; }
        if (surfaceAnalysis.aggregated || !cluster.memberIndexes.length) { return; }
        var records = cluster.memberIndexes.map(function (index) { return surfaceAnalysis.records[index]; });
        var area = 0;
        var weightedNormal = CncVector(0, 0, 0);
        records.forEach(function (record) {
            area += record.areaMm2;
            weightedNormal = CncAdd(weightedNormal, CncScale(record.normal, record.areaMm2));
        });
        // A tilted plane has constant normals: slope alone never makes it a
        // curved surface. This small angular tolerance absorbs mesh roundoff.
        if (area <= CNC_EPSILON || CncLength(weightedNormal) / area >= 0.999) { return; }
        axes.forEach(function (axis, axisIndex) {
            [true, false].forEach(function (positive) {
                var directionId = CncVisibilityDirectionId(axisIndex, positive);
                var visible = (cluster.evidence.accessibleTriangleIndexesByDirection || {})[directionId];
                if (!Array.isArray(visible) || visible.length === 0) { return; }
                var visibleIndexes = new Set(visible);
                var curved = records.filter(function (record) {
                    var alignment = Math.abs(CncDot(record.normal, axis));
                    return visibleIndexes.has(record.sourceTriangleIndex)
                        && alignment > 0.05 && alignment < 0.995;
                });
                if (!curved.length) { return; }
                result[directionId] = {
                    triangleIndexes: curved.map(function (record) { return record.sourceTriangleIndex; }),
                    areaMm2: curved.reduce(function (sum, record) { return sum + record.areaMm2; }, 0),
                    method: 'triangle-normal-variation',
                    camCertain: false
                };
            });
        });
    });
}

// Ball clearance and stock removal are different evidence. A contact record may
// support a finishing suggestion, but only a matching handoff may suppress a
// preceding flat operation. Every exported ID was checked; bounded omissions
// remain unverified and must never be expanded to their whole CAD face.
// The local seam detector recognizes straight fillet strips, but their adjoining
// toroidal/spherical CAD blends belong to the same radius-matched finishing pass.
// Extend only an existing radius with independently matched concave CAD evidence;
// unrelated radii, convex skins and unsupported splines remain general curves.
function CncExtendVerifiedFilletRegions(records, clusters, verifier, axes, ballTools) {
    if (!verifier || typeof verifier.concaveRadiusMm !== 'function' || typeof verifier.contact !== 'function'
        || !Array.isArray(axes) || axes.length !== 3 || !Array.isArray(ballTools)) { return; }
    var directions = Object.create(null);
    axes.forEach(function (axis, axisIndex) {
        directions[CncVisibilityDirectionId(axisIndex, true)] = axis;
        directions[CncVisibilityDirectionId(axisIndex, false)] = CncNegate(axis);
    });
    var bySource = new Map(records.map(function (record) { return [record.sourceTriangleIndex, record]; }));
    clusters.forEach(function (cluster) {
        var features = cluster.filletFeatures || [];
        if (!features.length) { return; }
        var owned = new Set(features.flatMap(function (feature) { return feature.triangleIndexes || []; }));
        var visibility = cluster.accessibleTriangleIndexesByDirection || {};
        var visible = Object.create(null);
        Object.keys(visibility).forEach(function (direction) {
            visible[direction] = new Set(visibility[direction]);
        });
        (cluster.triangleIndexes || []).forEach(function (index) {
            if (owned.has(index)) { return; }
            var radius = verifier.concaveRadiusMm(index);
            if (!Number.isFinite(radius) || radius <= 0) { return; }
            var matches = features.filter(function (feature) {
                return Math.abs(feature.radiusMm - radius) <= Math.max(0.0001, radius * 0.001);
            }).map(function (feature) {
                return { feature: feature, directions: (feature.accessibleDirectionIds || []).filter(function (direction) {
                    return visible[direction] && visible[direction].has(index);
                }) };
            }).filter(function (entry) {
                return entry.directions.length > 0
                    && entry.directions.length === entry.feature.accessibleDirectionIds.length;
            })
                .sort(function (a, b) { return b.directions.length - a.directions.length; });
            if (!matches.length) { return; }
            var match = matches[0];
            var record = bySource.get(index);
            var tool = ballTools.find(function (entry) { return entry && Math.abs(entry.diameterMm - radius * 2) <= 0.002; });
            if (!record || !tool || !match.directions.every(function (directionId) {
                var direction = directions[directionId];
                return direction && [record.centroid].concat(record.vertices).every(function (point) {
                    return verifier.contact({ sourceTriangleIndex: index, contactPosition: point, normal: record.normal }, direction, tool);
                });
            })) { return; }
            match.feature.triangleIndexes.push(index);
            // Preserve every existing entry direction. A blend visible from only
            // a different setup must not steal the original strip's operation.
            owned.add(index);
        });
        features.forEach(function (feature) {
            feature.areaMm2 = feature.triangleIndexes.reduce(function (sum, index) {
                var record = bySource.get(index);
                return sum + (record ? record.areaMm2 : 0);
            }, 0);
        });
        Object.keys(cluster.curvedFinishingByDirection || {}).forEach(function (direction) {
            var patch = cluster.curvedFinishingByDirection[direction];
            var represented = new Set(features.filter(function (feature) {
                return (feature.accessibleDirectionIds || []).indexOf(direction) >= 0;
            }).flatMap(function (feature) { return feature.triangleIndexes; }));
            patch.triangleIndexes = patch.triangleIndexes.filter(function (index) { return !represented.has(index); });
            patch.areaMm2 = patch.triangleIndexes.reduce(function (sum, index) {
                var record = bySource.get(index);
                return sum + (record ? record.areaMm2 : 0);
            }, 0);
            if (!patch.triangleIndexes.length) { delete cluster.curvedFinishingByDirection[direction]; }
        });
    });
}

function CncGeneralBallEvidence(records, clusters, field, axes, verifier, balls, preparationTools) {
    var result = { handoffs: [], finishingAccess: [], stockFacingRequirements: [],
        limits: { handoffSamplesPerDirection: 512, contactSamplesPerDirection: 8192,
            omittedContactSamples: 0, omittedHandoffSamples: 0 } };
    if (!field || field.degraded || !Array.isArray(field.surfaceSamples) || !verifier
        || typeof verifier.generalHandoff !== 'function') { return result; }
    axes.forEach(function (axis, axisIndex) {
        [true, false].forEach(function (positive) {
            var directionId = CncVisibilityDirectionId(axisIndex, positive);
            var direction = positive ? axis : CncNegate(axis);
            var visible = new Map();
            var requiredBallDiameters = new Set();
            clusters.forEach(function (cluster) {
                (cluster.filletFeatures || []).forEach(function (feature) {
                    if ((feature.accessibleDirectionIds || []).indexOf(directionId) < 0) { return; }
                    balls.forEach(function (ball) {
                        if (Math.abs(ball.diameterMm - feature.radiusMm * 2) <= 0.002) {
                            requiredBallDiameters.add(ball.diameterMm);
                        }
                    });
                });
                var entry = (cluster.curvedFinishingByDirection || {})[directionId];
                if (entry && entry.method === 'triangle-normal-variation' && entry.camCertain === false
                    && Array.isArray(entry.triangleIndexes) && entry.triangleIndexes.length) {
                    visible.set(cluster.id, new Set(entry.triangleIndexes));
                }
            });
            var samples = field.surfaceSamples.filter(function (sample) {
                return visible.has(sample.clusterId) && visible.get(sample.clusterId).has(sample.sourceTriangleIndex);
            });
            if (!samples.length) { return; }
            var candidates = balls.filter(function (ball) {
                // A tiny ball is not a generic cleanup staircase. Consider it
                // only when this setup already requires the matched fillet tool.
                return ball.diameterMm !== 1 || requiredBallDiameters.has(1);
            }).slice().sort(function (a, b) {
                return Number(requiredBallDiameters.has(b.diameterMm)) - Number(requiredBallDiameters.has(a.diameterMm))
                    || b.diameterMm - a.diameterMm;
            });
            var alreadyPrepared = new Set();
            var access = (field.toolAccess || {})[directionId] || {};
            preparationTools.forEach(function (tool) {
                ((access[tool.analysisProfileId || tool.id] || {}).reachableSampleIds || []).forEach(function (id) { alreadyPrepared.add(id); });
            });
            // Expensive exact rest certificates prioritize samples that caused
            // smaller-tool passes. Contact checks still cover the wider skin.
            samples.sort(function (a, b) { return Number(alreadyPrepared.has(a.id)) - Number(alreadyPrepared.has(b.id)) || a.id - b.id; });
            var groups = new Map();
            var handoffAttempts = 0;
            result.limits.omittedContactSamples += Math.max(0, samples.length - result.limits.contactSamplesPerDirection);
            samples.slice(0, result.limits.contactSamplesPerDirection).forEach(function (sample) {
                var chosen = null, certificate = null;
                // Existing D6/D10 preparation has no smaller flat pass to
                // replace. Still verify ball contact, but reserve the expensive
                // stock search for actual small-cutter handoffs.
                var needsHandoff = !alreadyPrepared.has(sample.id);
                var tryHandoff = needsHandoff && handoffAttempts < result.limits.handoffSamplesPerDirection;
                if (tryHandoff) { handoffAttempts += 1; }
                else if (needsHandoff) { result.limits.omittedHandoffSamples += 1; }
                for (var ball of candidates) {
                    // Once an already-required cutter reaches the sample, keep
                    // that tool instead of adding a new diameter solely to seek
                    // a different stock certificate. Missing stock proof retains
                    // its flat preparation work; contact is never assumed.
                    if (chosen && requiredBallDiameters.has(chosen.diameterMm)
                        && !requiredBallDiameters.has(ball.diameterMm)) { break; }
                    if (!verifier.contact(sample, direction, ball)) { continue; }
                    if (!chosen) { chosen = ball; }
                    if (!tryHandoff) { break; }
                    for (var preparation of preparationTools) {
                        certificate = verifier.generalHandoff(sample, direction, ball, preparation);
                        if (certificate) { chosen = ball; break; }
                    }
                    if (certificate) { break; }
                }
                if (!chosen) { return; }
                if (!groups.has(chosen.id)) { groups.set(chosen.id, { directionId: directionId, toolId: chosen.id,
                    sampleIds: [], triangleIndexes: [], method: 'sampled-ball-contact', camCertain: false }); }
                var group = groups.get(chosen.id);
                group.sampleIds.push(sample.id);
                if (group.triangleIndexes.indexOf(sample.sourceTriangleIndex) < 0) { group.triangleIndexes.push(sample.sourceTriangleIndex); }
                if (certificate) { result.handoffs.push(Object.assign(certificate, { directionId: directionId, setupId: directionId })); }
            });
            groups.forEach(function (group) { result.finishingAccess.push(group); });
            if (result.handoffs.some(function (entry) { return entry.directionId === directionId; })) {
                result.stockFacingRequirements.push({ directionId: directionId,
                    planeProjectionMm: records.reduce(function (top, record) {
                        return Math.max(top, CncDot(record.vertices[0], direction), CncDot(record.vertices[1], direction),
                            CncDot(record.vertices[2], direction));
                    }, -Infinity), method: 'model-exterior-plane', requiresFacing: true, camCertain: false });
            }
        });
    });
    return result;
}

function AnalyzeCncGeometry(triangles, modelInfo) {
    var sample = CncSampleTriangles(triangles || []);
    var selected = CncChooseAxes(triangles || []);
    var evidence = CncTriangleEvidence(triangles || [], selected.axes);
    var surfaceAnalysis = CncSurfaceAnalysis(triangles || [], selected.axes);
    CncApplyDirectionalVisibility(surfaceAnalysis, selected.axes);
    CncApplyCurvedFinishingEvidence(surfaceAnalysis, selected.axes);
    var surfaceClusters = surfaceAnalysis.clusters.map(function (cluster) { return cluster.evidence; });
    var rotationalEvidence = CncRotationalEvidence(
        triangles || [],
        selected.axes,
        selected.extents.dimensions,
        surfaceAnalysis.origin,
        surfaceAnalysis.clusters);
    var featureProxies = CncFeatureProxies(surfaceAnalysis.clusters, rotationalEvidence, surfaceAnalysis.origin,
        surfaceAnalysis.records, selected.axes, modelInfo && modelInfo.analyticSurfaces);
    var reasons = CncReviewReasons(
        modelInfo,
        sample.sampled || surfaceAnalysis.aggregated,
        surfaceAnalysis.limits.topologyDegraded);
    var dimensions = selected.extents.dimensions;
    var orientedEnvelopeVolumeMm3 = selected.extents.volume;
    var partVolumeMm3 = modelInfo && Number.isFinite(modelInfo.volume) ? Math.abs(modelInfo.volume) : 0;
    var boxFillRatio = orientedEnvelopeVolumeMm3 > CNC_EPSILON
        ? partVolumeMm3 / orientedEnvelopeVolumeMm3 : 0;
    var sortedDimensions = dimensions.slice().sort(function (left, right) { return left - right; });
    var planarClusterArea = surfaceClusters.reduce(function (total, cluster) {
        return total + (cluster.type === 'planar' ? cluster.areaMm2 : 0);
    }, 0);
    var planarAreaRatio = evidence.totalArea > 0 ? planarClusterArea / evidence.totalArea : 0;
    var nonPlanarRatio = evidence.totalArea > 0 ? evidence.nonPlanarArea / evidence.totalArea : 0;
    var maxDimension = sortedDimensions[2] || 0;
    var minimumDimension = sortedDimensions[0] || 0;
    var topBottomCoverage = CncOpposedPlanarCoverage(surfaceClusters, selected.axes, minimumDimension);
    var thinPlateToleranceMm = CNC_THIN_PLATE_RULES
        && Number.isFinite(CNC_THIN_PLATE_RULES.eligibilityToleranceMm)
        ? CNC_THIN_PLATE_RULES.eligibilityToleranceMm : 0;
    var flatPlateEligible = CNC_THIN_PLATE_RULES
        && minimumDimension >= CNC_THIN_PLATE_RULES.minPartThickness - thinPlateToleranceMm
        && minimumDimension <= CNC_THIN_PLATE_RULES.maxPartThickness + thinPlateToleranceMm
        && topBottomCoverage >= 0.60;
    var weakGripRisk = flatPlateEligible
        && maxDimension / minimumDimension >= CNC_THIN_PLATE_RULES.bowingRiskSpanToThicknessRatio;
    var deepFeatureRisk = minimumDimension > 0 && maxDimension / minimumDimension > 4 && nonPlanarRatio > 0.08;
    var orientationCandidates = CncDirectionCandidates(selected.axes, evidence, surfaceAnalysis, rotationalEvidence);
    var accessibilityField = self.CncSpatialField
        ? self.CncSpatialField.build(triangles || [], { axes: selected.axes }, {
            geometryToleranceMm: 0.5,
            minimumCutterDiameterMm: CNC_MINIMUM_MILLING_TOOL_RADIUS_MM * 2
        })
        : null;
    CncAssignFieldSamplesToClusters(accessibilityField, surfaceClusters, surfaceAnalysis.records);
    if (accessibilityField && self.CncToolLibrary) {
        accessibilityField._refineToolContact = CncFlatToolContactVerifier(surfaceAnalysis.records, surfaceClusters);
        self.CncSpatialField.classifyToolAccess(accessibilityField, self.CncToolLibrary.analysisProfiles());
        // Stock surfels choose fitting cutters and allocate work. They are not
        // exact visibility samples: a cell near a seam must not turn an entire
        // visible CAD triangle red (or promote a hidden face to green).
    }
    var ballRestHandoffs = [];
    var ballRestFinishingAccess = [];
    var generalBallEvidence = { handoffs: [], finishingAccess: [], stockFacingRequirements: [], limits: {} };
    if (self.CncBallRest && self.CncToolLibrary && accessibilityField && !accessibilityField.degraded
        && modelInfo && Array.isArray(modelInfo.analyticSurfaces) && Array.isArray(modelInfo.cadFaceRanges)) {
        var ballRestVerifier = self.CncBallRest.createVerifier(surfaceAnalysis.records,
            modelInfo.analyticSurfaces, modelInfo.cadFaceRanges);
        CncExtendVerifiedFilletRegions(surfaceAnalysis.records, surfaceClusters, ballRestVerifier, selected.axes,
            [1, 4].map(function (diameter) { return self.CncToolLibrary.ballRestTool(diameter); }).filter(Boolean));
        generalBallEvidence = CncGeneralBallEvidence(surfaceAnalysis.records, surfaceClusters, accessibilityField,
            selected.axes, ballRestVerifier, [6, 4, 1].map(function (diameter) {
                return self.CncToolLibrary.ballRestTool(diameter);
            }).filter(Boolean), self.CncToolLibrary.compatible('roughing', '6061').filter(function (tool) {
                return ['flat-10-2d', 'flat-6x18'].indexOf(tool.id) >= 0;
            }).sort(function (a, b) { return b.diameterMm - a.diameterMm; }));
        var ballRestTool = self.CncToolLibrary.ballRestTool(1);
        var preparationTool = self.CncToolLibrary.ballRestPreparationTool();
        Object.keys(accessibilityField.toolAccess || {}).forEach(function (directionId) {
            var access = accessibilityField.toolAccess[directionId];
            var small = new Set((access['analysis-flat-1-2d'] || {}).reachableSampleIds || []);
            self.CncToolLibrary.analysisProfiles().forEach(function (profile) {
                if (profile.family === 'flat_end_mill' && profile.diameterMm > 1) {
                    ((access[profile.id] || {}).reachableSampleIds || []).forEach(function (id) { small.delete(id); });
                }
            });
            // Bound extra work on intricate imports. Missing evidence retains
            // the existing flat operation; it never silently grants clearance.
            if (small.size === 0 || small.size > 128) { return; }
            var axisIndex = ['x', 'y', 'z'].indexOf(directionId.split('-')[1]);
            var direction = CncScale(selected.axes[axisIndex], directionId.indexOf('positive-') === 0 ? 1 : -1);
            accessibilityField.surfaceSamples.forEach(function (entry) {
                if (!small.has(entry.id)) { return; }
                var handoff = ballRestVerifier.handoff(entry, direction, ballRestTool, preparationTool);
                if (handoff) { ballRestHandoffs.push(Object.assign(handoff, {
                    directionId: directionId, ballToolId: ballRestTool.id, preparationDiameterMm: preparationTool.diameterMm
                })); }
            });
            if (!ballRestHandoffs.some(function (entry) { return entry.directionId === directionId; })) { return; }
            var finishingTriangles = new Set();
            surfaceClusters.forEach(function (cluster) {
                (cluster.filletFeatures || []).forEach(function (feature) {
                    if (Math.abs(feature.radiusMm - .5) <= .001
                        && (feature.accessibleDirectionIds || []).indexOf(directionId) >= 0) {
                        (feature.triangleIndexes || []).forEach(function (index) { finishingTriangles.add(index); });
                    }
                });
            });
            // Matched toroidal/spherical continuations can contain substantially
            // more triangles than the original straight cylindrical strip. Keep
            // a bounded complete check; never truncate a finishing region.
            if (finishingTriangles.size === 0 || finishingTriangles.size > 2048) { return; }
            var cleared = surfaceAnalysis.records.filter(function (record) {
                return finishingTriangles.has(record.sourceTriangleIndex)
                    && [record.centroid].concat(record.vertices).every(function (point) {
                        return ballRestVerifier.contact({ sourceTriangleIndex: record.sourceTriangleIndex,
                            contactPosition: point, normal: record.normal }, direction, ballRestTool);
                    });
            }).map(function (record) { return record.sourceTriangleIndex; });
            ballRestFinishingAccess.push({ directionId: directionId, toolId: ballRestTool.id,
                triangleIndexes: cleared, method: 'sampled-ball-contact', camCertain: false });
        });
    }
    var undercutRisk = nonPlanarRatio > 0.08 && orientationCandidates.some(function (candidate) {
        return candidate.projectedFaceCoverage < 0.02;
    });
    if (undercutRisk) { reasons.push('undercut_risk'); }
    if (deepFeatureRisk) { reasons.push('deep_feature_risk'); }
    if (weakGripRisk) { reasons.push('bowing_risk'); }

    return {
        bodyCount: modelInfo && Number.isInteger(modelInfo.bodyCount) && modelInfo.bodyCount > 0
            ? modelInfo.bodyCount : 1,
        orientedSizeMm: { x: dimensions[0], y: dimensions[1], z: dimensions[2] },
        principalAxes: selected.axes,
        orientationEvidence: selected.evidence,
        accessibilityField: self.CncSpatialField
            ? self.CncSpatialField.serialize(accessibilityField)
            : null,
        orientationCandidates: orientationCandidates,
        surfaceClusters: surfaceClusters,
        ballRestHandoffs: ballRestHandoffs,
        ballRestFinishingAccess: ballRestFinishingAccess,
        generalBallRestHandoffs: generalBallEvidence.handoffs,
        generalBallFinishingAccess: generalBallEvidence.finishingAccess,
        stockFacingRequirements: generalBallEvidence.stockFacingRequirements,
        generalBallEvidenceLimits: generalBallEvidence.limits,
        analysisLimits: surfaceAnalysis.limits,
        rotationalEvidence: rotationalEvidence,
        planarAreaRatio: planarAreaRatio,
        boxFillRatio: boxFillRatio,
        topBottomPlanarCoverage: topBottomCoverage,
        // Compatibility-only evidence for the legacy planner and overlays. The
        // ManufacturingFeatureGraph published by the model worker never copies
        // triangle cluster IDs, sample IDs, or proposed operation codes.
        legacyFeatureEvidenceDiagnosticOnly: true,
        holeProxies: featureProxies.holes,
        threadProxies: featureProxies.threads,
        chamferProxies: featureProxies.chamfers,
        pocketProxies: {
            count: deepFeatureRisk ? 1 : 0,
            evidence: 'heuristic_depth_band'
        },
        undercutRisk: undercutRisk,
        deepFeatureRisk: deepFeatureRisk,
        flatPlateEligible: flatPlateEligible,
        weakGripRisk: weakGripRisk,
        geometryConfidence: reasons.length === 0 ? 'High' : 'Low',
        reviewReasons: reasons
    };
}
