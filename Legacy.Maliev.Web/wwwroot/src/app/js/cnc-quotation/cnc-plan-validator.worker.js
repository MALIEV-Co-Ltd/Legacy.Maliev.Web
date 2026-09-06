(function (root) {
    'use strict';

    var contracts = root.CncPlanContracts, tools = root.CncToolLibrary, spatial = root.CncSpatialField,
        fixtureCatalog = root.CncFixtureCatalog, topologyContract = root.CncTopology;
    if (!contracts || !tools || !spatial || !fixtureCatalog || !topologyContract) { throw new Error('CNC contracts, topology, tool, spatial, and fixture modules must load before plan validation.'); }
    var STOCK_CONTRACT = 'CncValidationStock.v2', OCCUPANCY_VERSION = 2;

    function values(value) { return Array.isArray(value) ? value : []; }
    function text(value) { return typeof value === 'string' && value.trim().length > 0; }
    function positive(value) { return Number.isFinite(Number(value)) && Number(value) > 0; }
    function clone(value) { return JSON.parse(JSON.stringify(value)); }
    function byId(items) { return values(items).reduce(function (map, item) { if (item && text(item.id)) { map[item.id] = item; } return map; }, Object.create(null)); }
    function fail(reason, diagnostics) { return Object.freeze({ valid: false, reviewReasons: Object.freeze([reason]),
        diagnostics: diagnostics ? Object.freeze(clone(diagnostics)) : undefined }); }
    function freeze(value) { if (!value || typeof value !== 'object' || Object.isFrozen(value)) { return value; } Object.keys(value).forEach(function (key) { freeze(value[key]); }); return Object.freeze(value); }
    function canonicalJson(value) {
        if (Array.isArray(value)) { return '[' + value.map(canonicalJson).join(',') + ']'; }
        if (value && typeof value === 'object') { return '{' + Object.keys(value).sort().map(function (key) {
            return JSON.stringify(key) + ':' + canonicalJson(value[key]);
        }).join(',') + '}'; }
        return JSON.stringify(value);
    }
    function point(value) { return value && Number.isFinite(value.x) && Number.isFinite(value.y) && Number.isFinite(value.z); }
    function bounds(value) { return value && point(value.minimum) && point(value.maximum) && ['x', 'y', 'z'].every(function (name) { return value.maximum[name] > value.minimum[name]; }); }
    function normalize(axis) { if (!point(axis)) { return null; } var length = Math.hypot(axis.x, axis.y, axis.z); return length > 0 ? { x: axis.x / length, y: axis.y / length, z: axis.z / length } : null; }
    function sameAxis(left, right) { left = normalize(left); right = normalize(right); return left && right && left.x * right.x + left.y * right.y + left.z * right.z > 0.999999; }
    function parallelAxis(left, right) { left = normalize(left); right = normalize(right); return left && right && Math.abs(left.x * right.x + left.y * right.y + left.z * right.z) > 0.999999; }
    function cardinal(axis) { axis = normalize(axis); if (!axis) { return null; } var name = ['x', 'y', 'z'].sort(function (a, b) { return Math.abs(axis[b]) - Math.abs(axis[a]); })[0]; return Math.abs(axis[name]) > 0.999999 ? { name: name, sign: axis[name] < 0 ? -1 : 1 } : null; }
    function inside(pointValue, box, tolerance) { return ['x', 'y', 'z'].every(function (name) { return pointValue[name] >= box.minimum[name] - tolerance && pointValue[name] <= box.maximum[name] + tolerance; }); }
    function cellPoint(state, index) { var plane = state.dimensions.x * state.dimensions.y, z = Math.floor(index / plane), remainder = index - z * plane, y = Math.floor(remainder / state.dimensions.x), x = remainder - y * state.dimensions.x; return { x: state.origin.x + (x + 0.5) * state.resolutionMm, y: state.origin.y + (y + 0.5) * state.resolutionMm, z: state.origin.z + (z + 0.5) * state.resolutionMm }; }
    function bit(bits, index) { return (bits[index >>> 5] & (1 << (index & 31))) !== 0; }
    function set(bits, index) { bits[index >>> 5] |= (1 << (index & 31)); }
    function checksum(state) { return spatial.occupancyChecksum(state); }
    function faceNormal(face) { var surface = face && face.surface || {}; return normalize(surface.normal || surface.axis || face.normal); }
    function planarFace(face) { return face && face.surface && face.surface.kind === 'plane' && faceNormal(face); }
    function boundsForFaces(ids, faceIndex) {
        var items = values(ids).map(function (id) { return faceIndex[id] && faceIndex[id].validationVolume; });
        if (!items.length || items.some(function (item) { return !bounds(item); })) { return null; }
        return items.reduce(function (result, item) { if (!result) { return clone(item); } ['x', 'y', 'z'].forEach(function (name) {
            result.minimum[name] = Math.min(result.minimum[name], item.minimum[name]); result.maximum[name] = Math.max(result.maximum[name], item.maximum[name]);
        }); return result; }, null);
    }
    function connectedFeatures(left, right, faceIndex) {
        var rightIds = new Set(values(right.primaryFaceIds)), visited = new Set(values(left.primaryFaceIds)), frontier = Array.from(visited);
        for (var depth = 0; depth < 4 && frontier.length; depth++) {
            var next = [];
            for (var index = 0; index < frontier.length; index++) {
                var face = faceIndex[frontier[index]];
                if (!face || face.bodyId !== left.bodyId) { continue; }
                var adjacentIds = values(face.adjacentFaceIds);
                if (adjacentIds.some(function (id) { return rightIds.has(id); })) { return true; }
                adjacentIds.forEach(function (id) {
                    var adjacent = faceIndex[id];
                    if (adjacent && adjacent.bodyId === left.bodyId && !visited.has(id)) { visited.add(id); next.push(id); }
                });
            }
            frontier = next;
        }
        return false;
    }
    function externalThreadShoulder(feature, featureIndex, faceIndex, threadSurface, axis, path, modelTolerance) {
        if (!threadSurface || !point(threadSurface.centerMm) || !point(threadSurface.axis)) { return null; }
        var candidates = values(Object.keys(featureIndex).map(function (id) { return featureIndex[id]; })).filter(function (candidate) {
            if (!candidate || candidate.kind !== 'outside_profile' || candidate.bodyId !== feature.bodyId
                || !connectedFeatures(feature, candidate, faceIndex)
                || !values(candidate.accessAxes).some(function (candidateAxis) { return parallelAxis(candidateAxis, path.accessAxis); })) { return false; }
            var candidateSurface = values(candidate.primaryFaceIds).map(function (id) { return faceIndex[id] && faceIndex[id].surface; })
                .find(function (surface) { return surface && (surface.kind === 'cylinder' || surface.kind === 'cone')
                    && parallelAxis(surface.axis, threadSurface.axis) && point(surface.centerMm); });
            if (!candidateSurface) { return false; }
            var centerOffset = Math.hypot(candidateSurface.centerMm[path.lateralNames[0]] - path.centerMm[path.lateralNames[0]],
                candidateSurface.centerMm[path.lateralNames[1]] - path.centerMm[path.lateralNames[1]]);
            var candidateRadius = Number(candidateSurface.radiusMm);
            if (!(centerOffset <= modelTolerance) || !(candidateRadius > path.majorRadiusMm + modelTolerance)) { return false; }
            return true;
        }).map(function (candidate) {
            var siblingBounds = boundsForFaces(candidate.primaryFaceIds, faceIndex);
            if (!siblingBounds || !(siblingBounds.maximum[axis.name] > path.minimumAxialMm
                && siblingBounds.minimum[axis.name] < path.maximumAxialMm)) { return null; }
            var candidateSurface = values(candidate.primaryFaceIds).map(function (id) { return faceIndex[id] && faceIndex[id].surface; })
                .find(function (surface) { return surface && (surface.kind === 'cylinder' || surface.kind === 'cone'); });
            return { featureId: candidate.id, bodyId: candidate.bodyId, radiusMm: Number(candidateSurface && candidateSurface.radiusMm),
                faceIds: values(candidate.primaryFaceIds).slice().sort(), bounds: siblingBounds,
                boundaryMm: axis.sign > 0 ? siblingBounds.maximum[axis.name] : siblingBounds.minimum[axis.name] };
        }).filter(Boolean).sort(function (left, right) { return left.featureId.localeCompare(right.featureId); });
        if (!candidates.length) { return null; }
        var nearestRadius = Math.min.apply(Math, candidates.map(function (candidate) { return candidate.radiusMm; }));
        candidates = candidates.filter(function (candidate) { return Math.abs(candidate.radiusMm - nearestRadius) <= modelTolerance; });
        if (candidates.some(function (candidate) { return Math.abs(candidate.boundaryMm - candidates[0].boundaryMm) > modelTolerance; })) {
            return { ambiguous: true };
        }
        return candidates[0];
    }
    function canonicalSemanticPath(feature, operation, faceIndex, blankBounds, featureIndex) {
        var holeInterval = feature.kind === 'hole' ? contracts.finiteHoleInterval(feature, operation, faceIndex) : null;
        if (feature.kind === 'hole' && !holeInterval) { return null; }
        var featureBounds = boundsForFaces(feature.primaryFaceIds, faceIndex), axis = cardinal(operation.accessAxis);
        if (!featureBounds || !axis) { return null; }
        var cylinder = values(feature.primaryFaceIds).map(function (id) { return faceIndex[id]; }).find(function (face) {
            return face && face.surface && face.surface.kind === 'cylinder';
        });
        var cone = values(feature.primaryFaceIds).map(function (id) { return faceIndex[id]; }).find(function (face) {
            return face && face.surface && face.surface.kind === 'cone';
        });
        var lateral = ['x', 'y', 'z'].filter(function (name) { return name !== axis.name; });
        var centre = cylinder && cylinder.surface.centerMm || cone && cone.surface.centerMm || {};
        var radius = Number(cylinder && cylinder.surface.radiusMm || cone && cone.surface.radiusMm);
        var diameter = Number(feature.nominalDiameterMm || feature.diameterMm || feature.dimensions && (feature.dimensions.nominalDiameterMm || feature.dimensions.diameterMm));
        if (!(radius > 0) && diameter > 0) { radius = diameter / 2; }
        var selectedTool = tools.get(operation.toolId);
        if (!selectedTool) { return null; }
        var path = { operationKind: operation.kind, phase: operation.phase, accessAxis: clone(normalize(operation.accessAxis)),
            topologyFaceIds: values(feature.primaryFaceIds).slice().sort(), featureKind: feature.kind,
            featureBounds: clone(featureBounds), removalPolicyVersion: 'cnc-semantic-removal-2026-09-05-v1',
            finishAllowanceMm: operation.kind === 'roughing' ? 1 : 0,
            toolPath: { contract: 'CncCanonicalToolPath.v1', toolId: selectedTool.id,
                cutterDiameterMm: selectedTool.diameterMm, usableCutLengthMm: selectedTool.usableCutLengthMm,
                reachMm: selectedTool.reachMm, shankDiameterMm: selectedTool.shankDiameterMm,
                holderDiameterMm: selectedTool.holderDiameterMm } };
        if (feature.kind === 'slot' || feature.kind === 'pocket') {
            var corridor = root.CncPlanContracts.prismaticCorridor(feature, operation, faceIndex);
            if (!corridor) { return null; }
            Object.assign(path, corridor);
            path.toolPath.referencePoint = 'cutter_tip'; path.toolPath.orientationAxis = clone(normalize(operation.accessAxis));
        } else if (operation.kind === 'thread_milling' && (feature.kind === 'external_thread' || feature.kind === 'internal_thread') && diameter > 0) {
            path.geometry = 'brep_thread_groove'; path.axisName = axis.name; path.axisSign = axis.sign;
            path.lateralNames = lateral; path.centerMm = {};
            path.centerMm[lateral[0]] = Number.isFinite(centre[lateral[0]]) ? centre[lateral[0]] : (featureBounds.minimum[lateral[0]] + featureBounds.maximum[lateral[0]]) / 2;
            path.centerMm[lateral[1]] = Number.isFinite(centre[lateral[1]]) ? centre[lateral[1]] : (featureBounds.minimum[lateral[1]] + featureBounds.maximum[lateral[1]]) / 2;
            path.radiusMm = diameter / 2; path.majorRadiusMm = diameter / 2;
            path.minimumAxialMm = featureBounds.minimum[axis.name]; path.maximumAxialMm = featureBounds.maximum[axis.name];
            path.pitchMm = Number(feature.pitchMm || feature.dimensions && feature.dimensions.pitchMm);
            path.minorRadiusMm = path.majorRadiusMm - Math.max(0.2, 0.6134 * path.pitchMm);
            path.cutterCenterRadiusMm = feature.kind === 'external_thread'
                ? path.minorRadiusMm + selectedTool.diameterMm / 2
                : path.majorRadiusMm - selectedTool.diameterMm / 2;
            path.handedness = feature.handedness === 'left' ? 'left' : 'right';
            path.toolPath.referencePoint = 'cutter_tip'; path.toolPath.orientationAxis = clone(normalize(operation.accessAxis));
            if (feature.kind === 'external_thread') {
                var modelTolerance = Math.max(0.001, Math.hypot(blankBounds.maximum.x - blankBounds.minimum.x,
                    blankBounds.maximum.y - blankBounds.minimum.y, blankBounds.maximum.z - blankBounds.minimum.z) * 0.0001);
                var shoulder = externalThreadShoulder(feature, featureIndex, faceIndex,
                    cylinder && cylinder.surface || cone && cone.surface, axis, path, modelTolerance);
                if (shoulder && shoulder.ambiguous) { return null; }
                if (shoulder) {
                    if (axis.sign > 0) { path.minimumAxialMm = Math.min(path.maximumAxialMm, shoulder.boundaryMm); }
                    else { path.maximumAxialMm = Math.max(path.minimumAxialMm, shoulder.boundaryMm); }
                    path.shoulderFeatureId = shoulder.featureId; path.shoulderBodyId = shoulder.bodyId;
                    path.shoulderFaceIds = shoulder.faceIds;
                }
            }
            path.targetClassifierContract = 'CncBrepValidationMesh.v1';
        } else if (operation.kind === 'chamfering' && cone && radius > 0) {
            var coneMidpoint = { x: (featureBounds.minimum.x + featureBounds.maximum.x) / 2,
                y: (featureBounds.minimum.y + featureBounds.maximum.y) / 2,
                z: (featureBounds.minimum.z + featureBounds.maximum.z) / 2 };
            var coneAxial = Math.abs(coneMidpoint[axis.name] - Number(centre[axis.name] || 0));
            var coneRadial = Math.hypot(coneMidpoint[lateral[0]] - Number(centre[lateral[0]] || 0),
                coneMidpoint[lateral[1]] - Number(centre[lateral[1]] || 0));
            var coneDelta = coneAxial * Math.tan(Number(cone.surface.halfAngleRadians));
            var slopeSign = Math.abs(coneRadial - (radius + coneDelta)) <= Math.abs(coneRadial - Math.max(0, radius - coneDelta)) ? 1 : -1;
            path.geometry = 'chamfer_cone'; path.axisName = axis.name; path.axisSign = axis.sign;
            path.lateralNames = lateral; path.centerMm = clone(centre); path.radiusMm = radius;
            path.halfAngleRadians = Number(cone.surface.halfAngleRadians);
            path.radiusSlopeSign = slopeSign; path.materialSide = coneRadial >= radius ? 'outside' : 'inside';
            path.minimumAxialMm = featureBounds.minimum[axis.name];
            path.maximumAxialMm = featureBounds.maximum[axis.name];
        } else if (radius > 0 && (feature.kind === 'hole' || feature.kind === 'internal_thread'
            || feature.kind === 'external_thread' || feature.kind === 'outside_profile')) {
            path.geometry = 'axial_cylinder'; path.axisName = axis.name; path.axisSign = axis.sign; path.lateralNames = lateral; path.centerMm = {};
            path.centerMm[lateral[0]] = Number.isFinite(centre[lateral[0]]) ? centre[lateral[0]] : (featureBounds.minimum[lateral[0]] + featureBounds.maximum[lateral[0]]) / 2;
            path.centerMm[lateral[1]] = Number.isFinite(centre[lateral[1]]) ? centre[lateral[1]] : (featureBounds.minimum[lateral[1]] + featureBounds.maximum[lateral[1]]) / 2;
            path.radiusMm = feature.kind === 'external_thread' && diameter > 0 ? diameter / 2 : radius;
            path.minimumAxialMm = featureBounds.minimum[axis.name]; path.maximumAxialMm = featureBounds.maximum[axis.name];
            if (holeInterval) { path.minimumAxialMm = holeInterval.minimumAxialMm; path.maximumAxialMm = holeInterval.maximumAxialMm; }
            path.pitchMm = Number(feature.pitchMm || feature.dimensions && feature.dimensions.pitchMm) || null;
            if (feature.kind === 'external_thread' && operation.kind === 'major_diameter_preparation') {
                path.majorRadiusMm = path.radiusMm;
                var preparationTolerance = Math.max(0.001, Math.hypot(blankBounds.maximum.x - blankBounds.minimum.x,
                    blankBounds.maximum.y - blankBounds.minimum.y, blankBounds.maximum.z - blankBounds.minimum.z) * 0.0001);
                var preparationShoulder = externalThreadShoulder(feature, featureIndex, faceIndex,
                    cylinder && cylinder.surface || cone && cone.surface, axis, path, preparationTolerance);
                if (preparationShoulder && preparationShoulder.ambiguous) { return null; }
                if (preparationShoulder) {
                    if (axis.sign > 0) { path.minimumAxialMm = Math.min(path.maximumAxialMm, preparationShoulder.boundaryMm); }
                    else { path.maximumAxialMm = Math.max(path.minimumAxialMm, preparationShoulder.boundaryMm); }
                    path.shoulderFeatureId = preparationShoulder.featureId; path.shoulderBodyId = preparationShoulder.bodyId;
                    path.shoulderFaceIds = preparationShoulder.faceIds;
                }
                path.featureBounds.minimum[axis.name] = path.minimumAxialMm;
                path.featureBounds.maximum[axis.name] = path.maximumAxialMm;
            }
            if (feature.kind === 'outside_profile' || (feature.kind === 'external_thread'
                && operation.kind === 'major_diameter_preparation')) { lateral.forEach(function (name) {
                path.featureBounds.minimum[name] = blankBounds.minimum[name];
                path.featureBounds.maximum[name] = blankBounds.maximum[name];
            }); }
        } else if (operation.kind === 'facing') {
            var datumFaces = values(feature.primaryFaceIds).map(function (id) { return faceIndex[id]; })
                .filter(function (face) { return planarFace(face) && sameAxis(faceNormal(face), operation.accessAxis); });
            var datumFace = datumFaces[0]; if (!datumFace || !bounds(blankBounds)) { return null; }
            path.geometry = 'planar_facing'; path.axisName = axis.name; path.axisSign = axis.sign;
            path.targetPlaneMm = (datumFace.validationVolume.minimum[axis.name] + datumFace.validationVolume.maximum[axis.name]) / 2;
            path.planarRegions = datumFaces.map(function (face) {
                var faceLoops = values(face.loops), loops = faceLoops.map(function (loop) { return values(loop.vertices).map(clone); });
                if (faceLoops.length && loops.every(function (loop) { return loop.length >= 3; })) { return { kind: 'polygon_loops', loops: loops }; }
                var adjacent = values(face.adjacentFaceIds).map(function (id) { return faceIndex[id]; }).filter(function (item) {
                    return item && item.surface && (item.surface.kind === 'cylinder' || item.surface.kind === 'cone')
                        && parallelAxis(item.surface.axis, operation.accessAxis) && Number(item.surface.radiusMm) > 0;
                });
                if (adjacent.length >= 2) { var radii = adjacent.map(function (item) { return Number(item.surface.radiusMm); }).sort(function (a, b) { return a - b; });
                    return { kind: 'radial_band', centerMm: clone(adjacent[0].surface.centerMm), minimumRadiusMm: radii[0], maximumRadiusMm: radii[radii.length - 1] }; }
                return null;
            }).filter(Boolean); if (!path.planarRegions.length) { return null; }
            var protectedRegions = datumFaces.reduce(function (regions, face) {
                values(face.adjacentFaceIds).map(function (id) { return faceIndex[id]; }).forEach(function (adjacent) {
                    var surface = adjacent && adjacent.surface, volume = adjacent && adjacent.validationVolume;
                    if (!surface || !bounds(volume) || (surface.kind !== 'cylinder' && surface.kind !== 'cone')
                        || !parallelAxis(surface.axis, operation.accessAxis) || !point(surface.centerMm)) { return; }
                    var supportBounds = surface.boundsMm, supportMinimum = supportBounds && supportBounds.min,
                        supportMaximum = supportBounds && supportBounds.max;
                    if (!point(supportMinimum) || !point(supportMaximum)) { return; }
                    var extendsTowardTool = axis.sign > 0 ? supportMaximum[axis.name] > path.targetPlaneMm + 0.001
                        : supportMinimum[axis.name] < path.targetPlaneMm - 0.001;
                    if (!extendsTowardTool) { return; }
                    var maximumRadius = Number(surface.radiusMm);
                    if (surface.kind === 'cone') { maximumRadius += Math.max(Math.abs(supportMinimum[axis.name] - surface.centerMm[axis.name]),
                        Math.abs(supportMaximum[axis.name] - surface.centerMm[axis.name])) * Math.tan(Number(surface.halfAngleRadians)); }
                    if (!(maximumRadius > 0)) { return; }
                    regions.push({ centerMm: clone(surface.centerMm), maximumRadiusMm: maximumRadius });
                }); return regions;
            }, []);
            if (protectedRegions.length) { path.protectedRegions = protectedRegions; }
            path.featureBounds = clone(blankBounds);
        } else { return null; }
        return path;
    }

    function initialStock(stock) {
        var blank = stock.supplierBlank, dimensions = blank && blank.dimensionsMm, origin = blank && blank.originMm;
        if (!dimensions || !point(dimensions) || !point(origin) || !positive(stock.resolutionMm)) { return null; }
        var grid = { x: Math.max(1, Math.ceil(dimensions.x / stock.resolutionMm)), y: Math.max(1, Math.ceil(dimensions.y / stock.resolutionMm)), z: Math.max(1, Math.ceil(dimensions.z / stock.resolutionMm)) };
        var total = grid.x * grid.y * grid.z;
        if (!(total > 0) || total > 4000000) { return null; }
        var state = { origin: clone(origin), dimensions: grid, resolutionMm: stock.resolutionMm,
            toleranceMm: stock.toleranceMm, totalCells: total, bits: new Uint32Array(Math.ceil(total / 32)) };
        var round = blank.shape === 'round' || blank.shape === 'bar';
        for (var index = 0; index < total; index++) {
            var p = cellPoint(state, index), occupied = true;
            if (round) { var cx = origin.x + dimensions.x / 2, cy = origin.y + dimensions.y / 2; occupied = Math.hypot(p.x - cx, p.y - cy) <= Math.min(dimensions.x, dimensions.y) / 2 + stock.toleranceMm; }
            if (occupied) { set(state.bits, index); }
        }
        return state;
    }
    function targetSolidOccupancy(topology, stock, stockState) {
        var mesh = topology && topology.validationMesh;
        if (!mesh || mesh.contract !== 'CncBrepValidationMesh.v1' || mesh.watertight !== true
            || Number(mesh.resolutionMm) !== Number(stock.resolutionMm) || !values(mesh.vertices).length
            || !values(mesh.triangles).length) { return null; }
        var vertices = mesh.vertices, triangles = values(mesh.triangles).map(function (indices) {
            if (!Array.isArray(indices) || indices.length !== 3) { return null; }
            var a = vertices[indices[0]], b = vertices[indices[1]], c = vertices[indices[2]];
            if (!point(a) || !point(b) || !point(c)) { return null; }
            return { a: a, b: b, c: c, minY: Math.min(a.y, b.y, c.y), maxY: Math.max(a.y, b.y, c.y),
                minZ: Math.min(a.z, b.z, c.z), maxZ: Math.max(a.z, b.z, c.z) };
        });
        if (triangles.some(function (triangle) { return !triangle; })) { return null; }
        var target = Object.assign({}, stockState, { bits: new Uint32Array(stockState.bits.length) }), count = 0;
        for (var z = 0; z < stockState.dimensions.z; z++) {
            var pz = stockState.origin.z + (z + 0.500013) * stockState.resolutionMm;
            for (var y = 0; y < stockState.dimensions.y; y++) {
                var py = stockState.origin.y + (y + 0.500007) * stockState.resolutionMm, hits = [];
                triangles.forEach(function (triangle) {
                    if (py < triangle.minY || py > triangle.maxY || pz < triangle.minZ || pz > triangle.maxZ) { return; }
                    var ay = triangle.a.y, az = triangle.a.z, by = triangle.b.y, bz = triangle.b.z,
                        cy = triangle.c.y, cz = triangle.c.z;
                    var denominator = (bz - cz) * (ay - cy) + (cy - by) * (az - cz);
                    if (Math.abs(denominator) < 0.0000000001) { return; }
                    var u = ((bz - cz) * (py - cy) + (cy - by) * (pz - cz)) / denominator;
                    var v = ((cz - az) * (py - cy) + (ay - cy) * (pz - cz)) / denominator;
                    var w = 1 - u - v;
                    if (u >= -0.0000001 && v >= -0.0000001 && w >= -0.0000001) {
                        hits.push(u * triangle.a.x + v * triangle.b.x + w * triangle.c.x);
                    }
                });
                hits.sort(function (a, b) { return a - b; });
                hits = hits.filter(function (value, index) { return index === 0 || Math.abs(value - hits[index - 1]) > 0.0001; });
                if (hits.length % 2 !== 0) { return null; }
                for (var pair = 0; pair < hits.length; pair += 2) {
                    var first = hits[pair], last = hits[pair + 1];
                    for (var x = 0; x < stockState.dimensions.x; x++) {
                        var px = stockState.origin.x + (x + 0.5) * stockState.resolutionMm;
                        if (px <= first + stock.toleranceMm || px >= last - stock.toleranceMm) { continue; }
                        var index = x + stockState.dimensions.x * (y + stockState.dimensions.y * z); set(target.bits, index); count++;
                    }
                }
            }
        }
        return count > 0 ? { state: target, triangles: triangles, occupiedCellCount: count, checksum: checksum(target) } : null;
    }
    function squaredDistance(left, right) { return Math.pow(left.x - right.x, 2) + Math.pow(left.y - right.y, 2) + Math.pow(left.z - right.z, 2); }
    function pointTriangleDistanceSquared(p, triangle) {
        var a = triangle.a, b = triangle.b, c = triangle.c,
            ab = { x: b.x - a.x, y: b.y - a.y, z: b.z - a.z },
            ac = { x: c.x - a.x, y: c.y - a.y, z: c.z - a.z },
            ap = { x: p.x - a.x, y: p.y - a.y, z: p.z - a.z },
            d1 = ab.x * ap.x + ab.y * ap.y + ab.z * ap.z,
            d2 = ac.x * ap.x + ac.y * ap.y + ac.z * ap.z;
        if (d1 <= 0 && d2 <= 0) { return squaredDistance(p, a); }
        var bp = { x: p.x - b.x, y: p.y - b.y, z: p.z - b.z },
            d3 = ab.x * bp.x + ab.y * bp.y + ab.z * bp.z,
            d4 = ac.x * bp.x + ac.y * bp.y + ac.z * bp.z;
        if (d3 >= 0 && d4 <= d3) { return squaredDistance(p, b); }
        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) {
            var v = d1 / (d1 - d3), projectionAb = { x: a.x + v * ab.x, y: a.y + v * ab.y, z: a.z + v * ab.z };
            return squaredDistance(p, projectionAb);
        }
        var cp = { x: p.x - c.x, y: p.y - c.y, z: p.z - c.z },
            d5 = ab.x * cp.x + ab.y * cp.y + ab.z * cp.z,
            d6 = ac.x * cp.x + ac.y * cp.y + ac.z * cp.z;
        if (d6 >= 0 && d5 <= d6) { return squaredDistance(p, c); }
        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) {
            var w = d2 / (d2 - d6), projectionAc = { x: a.x + w * ac.x, y: a.y + w * ac.y, z: a.z + w * ac.z };
            return squaredDistance(p, projectionAc);
        }
        var va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) {
            var edge = (d4 - d3) / ((d4 - d3) + (d5 - d6)), projectionBc = {
                x: b.x + edge * (c.x - b.x), y: b.y + edge * (c.y - b.y), z: b.z + edge * (c.z - b.z) };
            return squaredDistance(p, projectionBc);
        }
        var denominator = 1 / (va + vb + vc), baryV = vb * denominator, baryW = vc * denominator,
            projection = { x: a.x + ab.x * baryV + ac.x * baryW,
                y: a.y + ab.y * baryV + ac.y * baryW, z: a.z + ab.z * baryV + ac.z * baryW };
        return squaredDistance(p, projection);
    }
    function targetInteriorPoint(target, index, tolerance, resolutionMm) {
        if (!target || !target.state || !bit(target.state.bits, index)) { return false; }
        // The common solid-validation boundary allowance is one voxel diagonal plus
        // numeric tolerance.  A target cell is interior only when that complete
        // neighbourhood is also target occupancy.  This is independent of operation
        // kind and avoids an O(swept-cells * mesh-triangles) surface-distance pass.
        var state = target.state, plane = state.dimensions.x * state.dimensions.y,
            z = Math.floor(index / plane), remainder = index - z * plane,
            y = Math.floor(remainder / state.dimensions.x), x = remainder - y * state.dimensions.x,
            radius = 1 + Math.floor(Math.max(0, tolerance) / resolutionMm);
        for (var dz = -radius; dz <= radius; dz++) {
            for (var dy = -radius; dy <= radius; dy++) {
                for (var dx = -radius; dx <= radius; dx++) {
                    var nx = x + dx, ny = y + dy, nz = z + dz;
                    if (nx < 0 || ny < 0 || nz < 0 || nx >= state.dimensions.x
                        || ny >= state.dimensions.y || nz >= state.dimensions.z
                        || !bit(state.bits, nx + state.dimensions.x * (ny + state.dimensions.y * nz))) { return false; }
                }
            }
        }
        return true;
    }
    function completeTool(operation, version, feature, faceIndex) {
        if (!operation || !text(operation.toolId) || !operation.toolConstraints
            || operation.toolConstraints.selectedToolId !== operation.toolId || version !== tools.version) { return null; }
        var tool = tools.get(operation.toolId);
        if (operation.kind === 'drilling' && feature && feature.kind === 'hole') {
            var interval = contracts.finiteHoleInterval(feature, operation, faceIndex || {});
            if (!interval || !tool || tool.usableCutLengthMm < interval.depthMm || tool.reachMm < interval.depthMm) { return null; }
        }
        var constraints = operation.toolConstraints || {};
        return tool && tool.family === operation.toolClass && positive(tool.diameterMm)
            && positive(tool.usableCutLengthMm) && positive(tool.reachMm)
            && (constraints.minimumCutLengthMm == null || Number.isFinite(constraints.minimumCutLengthMm)
                && constraints.minimumCutLengthMm >= 0 && tool.usableCutLengthMm >= constraints.minimumCutLengthMm)
            && (constraints.minimumReachMm == null || Number.isFinite(constraints.minimumReachMm)
                && constraints.minimumReachMm >= 0 && tool.reachMm >= constraints.minimumReachMm)
            && Math.abs(Number(operation.toolDiameterMm) - Number(tool.diameterMm)) < 0.000001
            && Math.abs(Number(constraints.reachMm) - Number(tool.reachMm)) < 0.000001
            && Math.abs(Number(constraints.usableCutLengthMm) - Number(tool.usableCutLengthMm)) < 0.000001
            ? tool : null;
    }
    function semanticRadius(operation, feature, semantic) {
        var constraints = operation.toolConstraints || {};
        if (operation.kind === 'drilling') { return Number(constraints.nominalDiameterMm || constraints.maximumDiameterMm || operation.toolDiameterMm) / 2; }
        if (operation.kind === 'spot_drilling') { return Number(constraints.pilotDiameterMm || operation.toolDiameterMm) / 2; }
        if (operation.kind === 'bore_preparation' || operation.kind === 'major_diameter_preparation') { return Number(constraints.targetDiameterMm || constraints.nominalDiameterMm || operation.toolDiameterMm) / 2; }
        return Number(semantic.radiusMm || feature.nominalDiameterMm / 2 || feature.diameterMm / 2);
    }
    function radial(pointValue, semantic) { var names = semantic.lateralNames; return Math.hypot(pointValue[names[0]] - semantic.centerMm[names[0]], pointValue[names[1]] - semantic.centerMm[names[1]]); }
    function insidePolygonLoops(pointValue, loops, lateral, tolerance) {
        var crossings = 0;
        values(loops).forEach(function (loop) {
            var vertices = values(loop);
            for (var index = 0, prior = vertices.length - 1; index < vertices.length; prior = index++) {
                var first = vertices[prior], second = vertices[index];
                if (!point(first) || !point(second)) { continue; }
                var a = first[lateral[0]], b = first[lateral[1]], c = second[lateral[0]], d = second[lateral[1]];
                var denominator = d - b;
                if ((b > pointValue[lateral[1]]) !== (d > pointValue[lateral[1]])
                    && pointValue[lateral[0]] < (c - a) * (pointValue[lateral[1]] - b) / denominator + a + tolerance) { crossings++; }
            }
        });
        return crossings % 2 === 1;
    }
    function insidePlanarBoundary(pointValue, semantic, axisName, tolerance) {
        var lateral = ['x', 'y', 'z'].filter(function (name) { return name !== axisName; });
        return values(semantic.planarRegions).some(function (region) {
            if (region.kind === 'polygon_loops') { return insidePolygonLoops(pointValue, region.loops, lateral, tolerance); }
            if (region.kind !== 'radial_band' || !point(region.centerMm)) { return false; }
            var radius = Math.hypot(pointValue[lateral[0]] - region.centerMm[lateral[0]], pointValue[lateral[1]] - region.centerMm[lateral[1]]);
            return radius >= Number(region.minimumRadiusMm) - tolerance && radius <= Number(region.maximumRadiusMm) + tolerance;
        });
    }
    function operationRemoves(pointValue, operation, feature, semantic, tolerance, tool) {
        if (operation.kind === 'deburring') { return false; }
        var box = semantic.featureBounds, axis = cardinal(operation.accessAxis);
        if (!bounds(box) || !axis || !inside(pointValue, box, tolerance)) { return false; }
        if (semantic.geometry === 'planar_facing') {
            if (semantic.axisName !== axis.name || semantic.axisSign !== axis.sign
                || !Number.isFinite(Number(semantic.targetPlaneMm))) { return false; }
            var signedDistance = (pointValue[axis.name] - Number(semantic.targetPlaneMm)) * axis.sign;
            var protectedByAdjacent = values(semantic.protectedRegions).some(function (region) {
                var lateral = ['x', 'y', 'z'].filter(function (name) { return name !== axis.name; });
                return Math.hypot(pointValue[lateral[0]] - region.centerMm[lateral[0]],
                    pointValue[lateral[1]] - region.centerMm[lateral[1]]) <= Number(region.maximumRadiusMm) + tolerance;
            });
            return signedDistance > Math.max(tolerance, 0.01) && !protectedByAdjacent;
        }
        if (semantic.geometry === 'chamfer_cone') {
            var coneRadius = radial(pointValue, semantic), axialDistance = Math.abs(pointValue[semantic.axisName]
                - Number(semantic.centerMm[semantic.axisName] || 0));
            var expected = Number(semantic.radiusMm) + Number(semantic.radiusSlopeSign) * axialDistance
                * Math.tan(Number(semantic.halfAngleRadians));
            return semantic.materialSide === 'outside' ? coneRadius >= expected + tolerance
                : semantic.materialSide === 'inside' && coneRadius <= Math.max(0, expected - tolerance);
        }
        if (semantic.geometry === 'brep_thread_groove') { return false; }
        if (semantic.geometry !== 'axial_cylinder') {
            var allowance = operation.kind === 'roughing' ? Number(semantic.finishAllowanceMm) : 0;
            if (operation.kind === 'roughing' && !(allowance > 0)) { return false; }
            if (allowance > 0) {
                return ['x', 'y', 'z'].every(function (name) { return pointValue[name] >= box.minimum[name] + allowance && pointValue[name] <= box.maximum[name] - allowance; });
            }
            return true;
        }
        var r = radial(pointValue, semantic), target = semanticRadius(operation, feature, semantic);
        if (!(target > 0)) { return false; }
        if (operation.kind === 'spot_drilling') {
            var entry = semantic.axisSign > 0 ? semantic.maximumAxialMm - Math.min(target, 5) : semantic.minimumAxialMm + Math.min(target, 5);
            return r <= target + tolerance && (semantic.axisSign > 0 ? pointValue[semantic.axisName] >= entry : pointValue[semantic.axisName] <= entry);
        }
        if (operation.kind === 'tapping' || operation.kind === 'thread_milling') {
            var pitch = Number(semantic.pitchMm || feature.pitchMm || feature.dimensions && feature.dimensions.pitchMm);
            var depth = pitch > 0 ? Math.max(0.2, 0.6134 * pitch) : Math.max(0.2, target * 0.08);
            return feature.kind === 'external_thread' ? r >= target - depth - tolerance && r <= target + tolerance
                : r >= Math.max(0, target - depth) - tolerance && r <= target + tolerance;
        }
        if (operation.kind === 'chamfering') {
            var axialEdge = semantic.axisSign > 0 ? semantic.maximumAxialMm - Math.min(1, target / 4) : semantic.minimumAxialMm + Math.min(1, target / 4);
            return r >= Math.max(0, target - 1) && r <= target + tolerance
                && (semantic.axisSign > 0 ? pointValue[semantic.axisName] >= axialEdge : pointValue[semantic.axisName] <= axialEdge);
        }
        if (feature.kind === 'outside_profile') {
            var profileTarget = target + (operation.kind === 'roughing' ? Number(semantic.finishAllowanceMm) : 0);
            return r > profileTarget + tolerance;
        }
        if (feature.kind === 'external_thread' && operation.kind === 'major_diameter_preparation') {
            return r > target + tolerance;
        }
        return r <= target + tolerance;
    }
    function rasterCylinder(bits, state, axisName, lateral, center, axialMinimum, axialMaximum, radius) {
        if (!(radius > 0) || !Number.isFinite(axialMinimum) || !Number.isFinite(axialMaximum)) { return; }
        var minimum = Math.min(axialMinimum, axialMaximum), maximum = Math.max(axialMinimum, axialMaximum);
        function range(name, low, high) { return { minimum: Math.max(0, Math.floor((low - state.origin[name]) / state.resolutionMm)),
            maximum: Math.min(state.dimensions[name] - 1, Math.ceil((high - state.origin[name]) / state.resolutionMm) - 1) }; }
        var ranges = {};
        ranges[axisName] = range(axisName, minimum, maximum);
        ranges[lateral[0]] = range(lateral[0], center[lateral[0]] - radius, center[lateral[0]] + radius);
        ranges[lateral[1]] = range(lateral[1], center[lateral[1]] - radius, center[lateral[1]] + radius);
        for (var z = ranges.z.minimum; z <= ranges.z.maximum; z++) {
            for (var y = ranges.y.minimum; y <= ranges.y.maximum; y++) {
                for (var x = ranges.x.minimum; x <= ranges.x.maximum; x++) {
                    var index = x + state.dimensions.x * (y + state.dimensions.y * z), p = cellPoint(state, index);
                    if (p[axisName] < minimum || p[axisName] > maximum
                        || Math.hypot(p[lateral[0]] - center[lateral[0]], p[lateral[1]] - center[lateral[1]]) > radius) { continue; }
                    set(bits, index);
                }
            }
        }
    }
    function threadAssemblySamples(semantic, tool, resolutionMm, visit) {
        var pitch = Number(semantic.pitchMm), majorRadius = Number(semantic.majorRadiusMm),
            minorRadius = Number(semantic.minorRadiusMm), pathRadius = Number(semantic.cutterCenterRadiusMm),
            cutterRadius = Number(tool && tool.diameterMm) / 2, cutterLength = Number(tool && tool.usableCutLengthMm),
            shankRadius = Number(tool && tool.shankDiameterMm) / 2, reach = Number(tool && tool.reachMm),
            holderRadius = Number(tool && tool.holderDiameterMm) / 2,
            minimumAxial = Number(semantic.minimumAxialMm), maximumAxial = Number(semantic.maximumAxialMm),
            lateral = semantic.lateralNames, axisName = semantic.axisName, axisSign = Number(semantic.axisSign),
            handedness = semantic.handedness === 'left' ? -1 : 1,
            orientation = semantic.toolPath && normalize(semantic.toolPath.orientationAxis);
        if (!(pitch > 0) || !(majorRadius > minorRadius && minorRadius > 0) || !(pathRadius > 0)
            || !(cutterRadius > 0) || !(cutterLength > 0) || !(shankRadius > 0) || !(reach >= cutterLength)
            || !(holderRadius > 0) || !(maximumAxial > minimumAxial) || !Array.isArray(lateral)
            || lateral.length !== 2 || (axisSign !== 1 && axisSign !== -1)
            || !semantic.toolPath || semantic.toolPath.referencePoint !== 'cutter_tip'
            || !orientation || !sameAxis(orientation, semantic.accessAxis)
            || semantic.targetClassifierContract !== 'CncBrepValidationMesh.v1') { return null; }
        var extent = maximumAxial - minimumAxial, start = axisSign > 0 ? minimumAxial : maximumAxial,
            spatialSpeed = Math.hypot(1, 2 * Math.PI * pathRadius / pitch),
            travelStep = Math.max(0.0001, Math.min(pitch / 24, resolutionMm * 0.35 / spatialSpeed)),
            sampleCount = Math.ceil(extent / travelStep) + 1, holderLength = Math.max(10, holderRadius), center = {};
        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++) {
            var travel = Math.min(extent, sampleIndex * travelStep), angle = handedness * 2 * Math.PI * travel / pitch;
            center[lateral[0]] = Number(semantic.centerMm[lateral[0]]) + pathRadius * Math.cos(angle);
            center[lateral[1]] = Number(semantic.centerMm[lateral[1]]) + pathRadius * Math.sin(angle);
            center[axisName] = start + axisSign * travel;
            visit('cutter', center, center[axisName], center[axisName] + axisSign * cutterLength, cutterRadius);
            visit('shank', center, center[axisName] + axisSign * cutterLength, center[axisName] + axisSign * reach, shankRadius);
            visit('holder', center, center[axisName] + axisSign * reach,
                center[axisName] + axisSign * (reach + holderLength), holderRadius);
        }
        return { sampleCount: sampleCount, cutterLengthMm: cutterLength,
            shankLengthMm: reach - cutterLength, holderLengthMm: holderLength,
            referencePoint: semantic.toolPath.referencePoint, orientationAxis: orientation };
    }
    function prismaticAssemblySamples(semantic, tool, resolutionMm, visit) {
        var axis = cardinal(semantic.accessAxis), lateral = semantic.lateralNames,
            across = semantic.transverseName, along = semantic.longitudinalName,
            radius = Number(tool && tool.diameterMm) / 2, cutterLength = Number(tool && tool.usableCutLengthMm),
            reach = Number(tool && tool.reachMm), shankRadius = Number(tool && tool.shankDiameterMm) / 2,
            holderRadius = Number(tool && tool.holderDiameterMm) / 2, allowance = Number(semantic.finishAllowanceMm),
            floor = Number(semantic.floorPlaneMm), entry = Number(semantic.entryPlaneMm),
            minimum = Number(semantic.minimumTransverseMm), maximum = Number(semantic.maximumTransverseMm),
            start = Number(semantic.minimumLongitudinalMm), end = Number(semantic.maximumLongitudinalMm),
            orientation = semantic.toolPath && normalize(semantic.toolPath.orientationAxis);
        if (!axis || axis.name !== semantic.axisName || axis.sign !== semantic.axisSign
            || !Array.isArray(lateral) || lateral.length !== 2 || !lateral.includes(across) || !lateral.includes(along)
            || across === along || lateral.includes(axis.name) || ![floor, entry, minimum, maximum, start, end, allowance].every(Number.isFinite)
            || !(radius > 0) || !(cutterLength > 0) || !(reach >= cutterLength) || !(shankRadius > 0) || !(holderRadius > 0)
            || !(axis.sign * (entry - floor) > allowance) || axis.sign * (entry - floor) > cutterLength
            || allowance < 0 || !(maximum - minimum > 2 * (radius + allowance)) || !(end > start)
            || !semantic.toolPath || semantic.toolPath.referencePoint !== 'cutter_tip'
            || !orientation || !sameAxis(orientation, semantic.accessAxis)
            || semantic.targetClassifierContract !== 'CncBrepValidationMesh.v1') { return null; }
        var low = minimum + radius + allowance, high = maximum - radius - allowance,
            passCount = Math.max(1, Math.ceil((high - low) / Math.max(0.1, radius * 0.8))),
            travel = end - start + 2 * radius, steps = Math.ceil(travel / Math.max(0.02, resolutionMm * 0.35)),
            holderLength = Math.max(10, holderRadius), count = 0, center = {};
        // A bounded open-ended raster: enter outside the open end at the cutting
        // depth. The entire tip-referenced assembly follows every sampled pass.
        if ((passCount + 1) * (steps + 1) > 200000) { return null; }
        for (var pass = 0; pass <= passCount; pass++) {
            center[across] = low + (high - low) * pass / passCount;
            for (var step = 0; step <= steps; step++) {
                center[along] = start - radius + travel * step / steps;
                center[axis.name] = floor + axis.sign * allowance;
                visit('cutter', center, center[axis.name], center[axis.name] + axis.sign * cutterLength, radius);
                visit('shank', center, center[axis.name] + axis.sign * cutterLength, center[axis.name] + axis.sign * reach, shankRadius);
                visit('holder', center, center[axis.name] + axis.sign * reach,
                    center[axis.name] + axis.sign * (reach + holderLength), holderRadius);
                count++;
            }
        }
        return { sampleCount: count, cutterLengthMm: cutterLength, shankLengthMm: reach - cutterLength,
            holderLengthMm: holderLength, referencePoint: 'cutter_tip', orientationAxis: orientation };
    }
    function assemblySamples(semantic, tool, resolutionMm, visit) {
        return semantic.geometry === 'prismatic_corridor' ? prismaticAssemblySamples(semantic, tool, resolutionMm, visit)
            : threadAssemblySamples(semantic, tool, resolutionMm, visit);
    }
    function toolAssemblySweep(state, semantic, tool) {
        var empty = function () { return new Uint32Array(state.bits.length); }, cutterBits = empty(), shankBits = empty(), holderBits = empty();
        var sampleEvidence = assemblySamples(semantic, tool, state.resolutionMm,
            function (kind, center, minimum, maximum, radius) {
                rasterCylinder(kind === 'cutter' ? cutterBits : kind === 'shank' ? shankBits : holderBits,
                    state, semantic.axisName, semantic.lateralNames, center, minimum, maximum, radius);
            });
        if (!sampleEvidence) { return null; }
        var rawBits = empty(), counts = { cutter: 0, shank: 0, holder: 0 }, rawCount = 0;
        for (var wordIndex = 0; wordIndex < rawBits.length; wordIndex++) {
            rawBits[wordIndex] = cutterBits[wordIndex] | shankBits[wordIndex] | holderBits[wordIndex];
        }
        for (var index = 0; index < state.totalCells; index++) {
            if (bit(cutterBits, index)) { counts.cutter++; }
            if (bit(shankBits, index)) { counts.shank++; }
            if (bit(holderBits, index)) { counts.holder++; }
            if (bit(rawBits, index)) { rawCount++; }
        }
        return { cutterBits: cutterBits, shankBits: shankBits, holderBits: holderBits,
            rawBits: rawBits, rawSweepCellCount: rawCount,
            rawSweepChecksum: checksum(Object.assign({}, state, { bits: rawBits })),
            assemblySweepCellCounts: counts, trajectorySampleCount: sampleEvidence.sampleCount,
            assemblyAxialSpansMm: { cutter: sampleEvidence.cutterLengthMm,
                shank: sampleEvidence.shankLengthMm, holder: sampleEvidence.holderLengthMm },
            toolReference: sampleEvidence.referencePoint, toolOrientationAxis: sampleEvidence.orientationAxis };
    }
    function bitRuns(bits, totalCells) {
        var runs = [], start = -1;
        for (var index = 0; index <= totalCells; index++) {
            var occupied = index < totalCells && bit(bits, index);
            if (occupied && start < 0) { start = index; }
            if (!occupied && start >= 0) { runs.push(Object.freeze({ start: start, length: index - start })); start = -1; }
        }
        return Object.freeze(runs);
    }
    function classifyRemoval(state, collisionBits, removalSourceBits, target, tolerance, partBits, checkNonCuttingStock) {
        var boundaryBits = new Uint32Array(state.bits.length), removalBits = new Uint32Array(state.bits.length),
            targetViolations = 0, boundaryContacts = 0, stockViolations = 0,
            violationParts = { cutter: 0, shank: 0, holder: 0 }, violationSamples = [];
        for (var sweptIndex = 0; sweptIndex < state.totalCells; sweptIndex++) {
            if (checkNonCuttingStock && partBits && bit(state.bits, sweptIndex)
                && (bit(partBits.shank, sweptIndex) || bit(partBits.holder, sweptIndex))) { stockViolations++; }
            if (!bit(collisionBits, sweptIndex) || !target || !target.state || !bit(target.state.bits, sweptIndex)) { continue; }
            if (targetInteriorPoint(target, sweptIndex, tolerance, state.resolutionMm)) {
                targetViolations++;
                if (partBits && bit(partBits.cutter, sweptIndex)) { violationParts.cutter++; }
                if (partBits && bit(partBits.shank, sweptIndex)) { violationParts.shank++; }
                if (partBits && bit(partBits.holder, sweptIndex)) { violationParts.holder++; }
                if (violationSamples.length < 8) { violationSamples.push(cellPoint(state, sweptIndex)); }
            } else { set(boundaryBits, sweptIndex); boundaryContacts++; }
        }
        if (!targetViolations && !stockViolations) {
            for (var candidate = 0; candidate < state.totalCells; candidate++) {
                if (bit(removalSourceBits, candidate) && bit(state.bits, candidate) && !bit(boundaryBits, candidate)) {
                    set(removalBits, candidate);
                }
            }
        }
        var runs = bitRuns(removalBits, state.totalCells), bits = new Uint32Array(state.bits), removed = 0;
        runs.forEach(function (run) { for (var index = run.start; index < run.start + run.length; index++) {
            bits[index >>> 5] &= ~(1 << (index & 31)); removed++;
        } });
        return { state: Object.assign({}, state, { bits: bits }), removedCellCount: removed,
            targetViolationCount: targetViolations, targetViolationCellCounts: violationParts, stockViolationCount: stockViolations,
            targetViolationSamples: violationSamples, boundaryContactCellCount: boundaryContacts,
            boundaryContactMaskChecksum: checksum(Object.assign({}, state, { bits: boundaryBits })),
            validatedRemovalCellCount: removed,
            validatedRemovalMaskChecksum: checksum(Object.assign({}, state, { bits: removalBits })),
            validatedRemovalRuns: runs };
    }
    function deriveOperationSweep(state, operation, feature, semantic, tolerance, target, tool) {
        var emptyBits = new Uint32Array(state.bits.length);
        if (semantic.geometry === 'brep_thread_groove' || semantic.geometry === 'prismatic_corridor') {
            var assembly = toolAssemblySweep(state, semantic, tool);
            if (!assembly) { return { state: Object.assign({}, state, { bits: new Uint32Array(state.bits) }), removedCellCount: 0,
                targetViolationCount: 0, rawSweepCellCount: 0, rawSweepChecksum: null,
                assemblySweepCellCounts: { cutter: 0, shank: 0, holder: 0 }, trajectorySampleCount: 0,
                boundaryContactCellCount: 0 }; }
            return Object.assign(assembly, classifyRemoval(state, assembly.rawBits, assembly.cutterBits,
                target, tolerance, { cutter: assembly.cutterBits, shank: assembly.shankBits, holder: assembly.holderBits },
                semantic.geometry === 'prismatic_corridor'));
        }
        var box = semantic.featureBounds;
        if (!bounds(box)) { return { state: Object.assign({}, state, { bits: new Uint32Array(state.bits) }), removedCellCount: 0 }; }
        function range(name) {
            return { minimum: Math.max(0, Math.floor((box.minimum[name] - tolerance - state.origin[name]) / state.resolutionMm)),
                maximum: Math.min(state.dimensions[name] - 1, Math.ceil((box.maximum[name] + tolerance - state.origin[name]) / state.resolutionMm)) };
        }
        var xRange = range('x'), yRange = range('y'), zRange = range('z');
        for (var z = zRange.minimum; z <= zRange.maximum; z++) {
            for (var y = yRange.minimum; y <= yRange.maximum; y++) {
                for (var x = xRange.minimum; x <= xRange.maximum; x++) {
                    var index = x + state.dimensions.x * (y + state.dimensions.y * z);
                    var pointValue = cellPoint(state, index);
                    if (operationRemoves(pointValue, operation, feature, semantic,
                        Math.max(tolerance, state.resolutionMm / 2), tool)) { set(emptyBits, index); }
                }
            }
        }
        var classified = classifyRemoval(state, emptyBits, emptyBits, target, tolerance, null), rawCount = 0;
        for (var rawIndex = 0; rawIndex < state.totalCells; rawIndex++) { if (bit(emptyBits, rawIndex)) { rawCount++; } }
        return Object.assign({ rawSweepCellCount: rawCount,
            rawSweepChecksum: checksum(Object.assign({}, state, { bits: emptyBits })) }, classified);
    }
    function terminalResidual(state, target, stock) {
        var missing = 0, excess = 0, boundaryExcess = 0, residualCount = 0, excessBounds = null;
        var boundaryTolerance = stock.resolutionMm * Math.sqrt(3) + stock.toleranceMm;
        for (var index = 0; index < state.totalCells; index++) {
            var residualOccupied = bit(state.bits, index), targetOccupied = bit(target.state.bits, index);
            if (residualOccupied) { residualCount++; }
            if (targetOccupied && !residualOccupied) { missing++; }
            if (residualOccupied && !targetOccupied) {
                var p = cellPoint(state, index), nearBoundary = values(target.triangles).some(function (triangle) {
                    return pointTriangleDistanceSquared(p, triangle) <= boundaryTolerance * boundaryTolerance;
                });
                if (nearBoundary) { boundaryExcess++; } else {
                    excess++;
                    if (!excessBounds) { excessBounds = { minimum: clone(p), maximum: clone(p) }; }
                    else { ['x', 'y', 'z'].forEach(function (name) {
                        excessBounds.minimum[name] = Math.min(excessBounds.minimum[name], p[name]);
                        excessBounds.maximum[name] = Math.max(excessBounds.maximum[name], p[name]);
                    }); }
                }
            }
        }
        var reconciled = missing === 0 && excess === 0;
        return { contract: 'CncTerminalResidualValidation.v1', policyVersion: 'cnc-terminal-residual-2026-09-05-v1',
            resolutionMm: stock.resolutionMm, toleranceMm: stock.toleranceMm,
            boundaryToleranceMm: boundaryTolerance, physicalResidualChecksum: checksum(state),
            residualChecksum: reconciled ? target.checksum : checksum(state), targetChecksum: target.checksum,
            physicalResidualCellCount: residualCount,
            residualCellCount: reconciled ? target.occupiedCellCount : residualCount,
            targetCellCount: target.occupiedCellCount, toleranceBoundaryExcessCellCount: boundaryExcess,
            excessStockBounds: excessBounds,
            missingTargetCellCount: missing, excessStockCellCount: excess,
            allowedMissingTargetCellCount: 0, allowedExcessStockCellCount: 0 };
    }
    function validFixture(setup, topologyFaces, stock) {
        var clampFaces = values(setup.clampFaceIds).map(function (id) { return topologyFaces[id]; });
        var fixture = fixtureCatalog.resolve(setup.fixtureId), axis = cardinal(setup.orientation && setup.orientation.axis);
        var cap = fixtureCatalog.capability(setup.fixtureId, setup.orientation && setup.orientation.axis, clampFaces);
        var dimensions = stock.supplierBlank && stock.supplierBlank.dimensionsMm;
        if (!cap || cap.catalogVersion !== fixtureCatalog.version
            || canonicalJson(cap) !== canonicalJson(setup.fixtureCapability)
            || !fixture || !axis || !dimensions || Number(dimensions[axis.name]) > fixture.maximumOpeningMm
            || !positive(cap.maximumToolReachMm) || !values(cap.accessAxes).some(function (candidate) { return sameAxis(candidate, setup.orientation.axis); })) { return false; }
        return values(setup.datumFaceIds).length && values(setup.clampFaceIds).length
            && values(setup.datumFaceIds).concat(values(setup.clampFaceIds)).every(function (id) {
                var face = topologyFaces[id]; return face && face.surface && face.surface.kind === 'plane' && bounds(face.validationVolume)
                    && ['x', 'y', 'z'].filter(function (name) { return name !== axis.name; }).some(function (name) {
                        return face.validationVolume.maximum[name] - face.validationVolume.minimum[name] >= fixture.minimumGripMm;
                    })
                    && inside({ x: (face.validationVolume.minimum.x + face.validationVolume.maximum.x) / 2,
                        y: (face.validationVolume.minimum.y + face.validationVolume.maximum.y) / 2,
                        z: (face.validationVolume.minimum.z + face.validationVolume.maximum.z) / 2 },
                    stock.targetSolid.bounds, stock.toleranceMm);
            });
    }
    function boxesOverlap(left, right, tolerance) { return ['x', 'y', 'z'].every(function (name) {
        return left.maximum[name] > right.minimum[name] + tolerance && right.maximum[name] > left.minimum[name] + tolerance;
    }); }
    function cylinderIntersectsBox(center, axisName, lateral, axialMinimum, axialMaximum, radius, box, tolerance) {
        var minimum = Math.min(axialMinimum, axialMaximum), maximum = Math.max(axialMinimum, axialMaximum);
        if (maximum <= box.minimum[axisName] + tolerance || box.maximum[axisName] <= minimum + tolerance) { return false; }
        var distances = lateral.map(function (name) {
            return center[name] < box.minimum[name] ? box.minimum[name] - center[name]
                : center[name] > box.maximum[name] ? center[name] - box.maximum[name] : 0;
        });
        return Math.hypot(distances[0], distances[1]) < radius + tolerance;
    }
    function toolAssemblyClear(operation, tool, semantic, capability, tolerance) {
        var axis = cardinal(operation.accessAxis), box = semantic.featureBounds;
        if (!axis || !bounds(box) || !positive(tool.shankDiameterMm) || !positive(tool.holderDiameterMm)) { return false; }
        if (semantic.geometry === 'brep_thread_groove' || semantic.geometry === 'prismatic_corridor') {
            var collision = false, sampled = assemblySamples(semantic, tool, Math.max(0.05, tolerance),
                function (_, center, minimum, maximum, radius) {
                    if (!collision && values(capability.obstacles).some(function (obstacle) {
                        return bounds(obstacle) && cylinderIntersectsBox(center, semantic.axisName,
                            semantic.lateralNames, minimum, maximum, radius, obstacle, tolerance);
                    })) { collision = true; }
                });
            return !!sampled && !collision;
        }
        var lateral = ['x', 'y', 'z'].filter(function (name) { return name !== axis.name; });
        function envelope(axialMinimum, axialMaximum, diameter) {
            var result = { minimum: clone(box.minimum), maximum: clone(box.maximum) }, radius = diameter / 2;
            result.minimum[axis.name] = Math.min(axialMinimum, axialMaximum); result.maximum[axis.name] = Math.max(axialMinimum, axialMaximum);
            lateral.forEach(function (name) { var center = (box.minimum[name] + box.maximum[name]) / 2;
                result.minimum[name] = Math.min(box.minimum[name], center - radius); result.maximum[name] = Math.max(box.maximum[name], center + radius); });
            return result;
        }
        var targetPlane = Number(semantic.targetPlaneMm), entry = Number.isFinite(targetPlane) ? targetPlane
                : axis.sign > 0 ? box.maximum[axis.name] : box.minimum[axis.name], outward = axis.sign,
            cutterMinimum = Number.isFinite(targetPlane) ? (axis.sign < 0 ? box.minimum[axis.name] : entry) : box.minimum[axis.name],
            cutterMaximum = Number.isFinite(targetPlane) ? (axis.sign > 0 ? box.maximum[axis.name] : entry) : box.maximum[axis.name],
            cutter = envelope(cutterMinimum, cutterMaximum, tool.diameterMm),
            shank = envelope(entry, entry + outward * tool.reachMm, tool.shankDiameterMm),
            holderLength = Math.max(10, tool.holderDiameterMm / 2),
            holder = envelope(entry + outward * tool.reachMm, entry + outward * (tool.reachMm + holderLength), tool.holderDiameterMm);
        return !values(capability.obstacles).some(function (obstacle) { return bounds(obstacle)
            && [cutter, shank, holder].some(function (assemblyPart) { return boxesOverlap(assemblyPart, obstacle, tolerance); }); });
    }
    function contactSurvives(state, face, tolerance) {
        var box = face && face.validationVolume;
        if (!bounds(box)) { return false; }
        function range(name) {
            return { minimum: Math.max(0, Math.floor((box.minimum[name] - tolerance - state.origin[name]) / state.resolutionMm)),
                maximum: Math.min(state.dimensions[name] - 1, Math.ceil((box.maximum[name] + tolerance - state.origin[name]) / state.resolutionMm)) };
        }
        var xRange = range('x'), yRange = range('y'), zRange = range('z');
        for (var z = zRange.minimum; z <= zRange.maximum; z++) {
            for (var y = yRange.minimum; y <= yRange.maximum; y++) {
                for (var x = xRange.minimum; x <= xRange.maximum; x++) {
                    var index = x + state.dimensions.x * (y + state.dimensions.y * z);
                    if (bit(state.bits, index) && inside(cellPoint(state, index), box, tolerance)) { return true; }
                }
            }
        }
        return false;
    }
    function assignmentValid(operationGraph, setupPlan) {
        var counts = Object.create(null), operations = byId(operationGraph.operations);
        values(setupPlan.setups).forEach(function (setup) { values(setup.operationIds).forEach(function (id) { counts[id] = (counts[id] || 0) + 1; }); });
        return values(operationGraph.operations).every(function (operation) { return counts[operation.id] === 1 && setupPlan.operationAssignments[operation.id]; })
            && Object.keys(operations).length === Object.keys(setupPlan.operationAssignments || {}).length;
    }

    async function validateInternal(input) {
        input = input || {};
        try { contracts.validateTopology(input.topology); contracts.validateFeatureGraph(input.featureGraph); contracts.validateOperationGraph(input.operationGraph, input.featureGraph); }
        catch (error) { return fail(error && error.code || 'plan_contract_invalid'); }
        if (!input.topology || input.topology.sourceKind !== 'brep' || input.topology.automaticPlanningEligible !== true) { return fail('semantic_topology_required'); }
        var validationMesh = input.topology.validationMesh;
        if (!validationMesh || validationMesh.contract !== 'CncBrepValidationMesh.v1'
            || validationMesh.source !== 'occt_brep_validation_tessellation_v1'
            || Number(validationMesh.deflectionMm) !== 0.1 || !text(input.topology.validationMeshHash)) {
            return fail('validation_mesh_mismatch');
        }
        var recomputedValidationHash = await topologyContract.validationMeshHash(validationMesh);
        if (recomputedValidationHash !== input.topology.validationMeshHash
            || await topologyContract.revisionHash(input.topology) !== input.topology.revision) {
            return fail('validation_mesh_mismatch');
        }
        if (!input.setupPlan || input.setupPlan.contract !== 'SetupPlan.v1' || !assignmentValid(input.operationGraph, input.setupPlan)) { return fail('unassigned_operation'); }
        var stock = input.stock || {};
        if (stock.contract !== STOCK_CONTRACT || stock.occupancyVersion !== OCCUPANCY_VERSION
            || !stock.supplierBlank || stock.supplierBlank.source !== 'quoted_supplier_blank'
            || !stock.targetSolid || stock.targetSolid.geometryRevision !== input.topology.revision) { return fail('stock_evidence_invalid'); }
        var topologyFaceIds = values(input.topology.faces).map(function (face) { return face.id; }).sort();
        if (JSON.stringify(topologyFaceIds) !== JSON.stringify(values(stock.targetSolid.topologyFaceIds).slice().sort())) { return fail('stock_evidence_invalid'); }
        var state = initialStock(stock); if (!state) { return fail('stock_evidence_invalid'); }
        var targetSolid = targetSolidOccupancy(input.topology, stock, state);
        if (!targetSolid) { return fail('target_solid_classification_required'); }
        var blankBounds = { minimum: clone(stock.supplierBlank.originMm), maximum: {
            x: stock.supplierBlank.originMm.x + stock.supplierBlank.dimensionsMm.x,
            y: stock.supplierBlank.originMm.y + stock.supplierBlank.dimensionsMm.y,
            z: stock.supplierBlank.originMm.z + stock.supplierBlank.dimensionsMm.z } };
        var operations = byId(input.operationGraph.operations), features = byId(input.featureGraph.features), topologyFaces = byId(input.topology.faces);
        var paths = Object.create(null);
        values(stock.operationPaths).forEach(function (item) { if (item && text(item.operationId) && !paths[item.operationId]) { paths[item.operationId] = item; } else { paths.invalid = true; } });
        if (paths.invalid || values(stock.operationPaths).length !== values(input.operationGraph.operations).length) { return fail('operation_path_invalid'); }
        var completed = Object.create(null), results = [], priorOutput = input.setupPlan.inputStockState;
        for (var setupIndex = 0; setupIndex < input.setupPlan.setups.length; setupIndex++) {
            var setup = input.setupPlan.setups[setupIndex];
            if (setup.sequence !== setupIndex + 1 || setup.inputStockState !== priorOutput) { return fail('stock_transition_mismatch'); }
            if (!validFixture(setup, topologyFaces, stock)) { return fail('fixture_validation_evidence_required'); }
            if (!values(setup.datumFaceIds).concat(values(setup.clampFaceIds)).every(function (id) {
                return contactSurvives(state, topologyFaces[id], Math.max(stock.toleranceMm, stock.resolutionMm / 2));
            })) { return fail('fixture_contact_removed'); }
            for (var operationIndex = 0; operationIndex < setup.operationIds.length; operationIndex++) {
                var operation = operations[setup.operationIds[operationIndex]], feature = operation && features[operation.featureId], certificate = operation && paths[operation.id];
                if (!operation || !feature || !certificate || certificate.contract !== 'CncOperationSemanticPath.v1'
                    || certificate.geometryRevision !== input.topology.revision || certificate.featureId !== feature.id
                    || certificate.setupId !== setup.id || certificate.toolId !== operation.toolId
                    || JSON.stringify(values(feature.primaryFaceIds).slice().sort()) !== JSON.stringify(values(certificate.semanticPath && certificate.semanticPath.topologyFaceIds).slice().sort())) { return fail('operation_path_invalid'); }
                if (values(operation.predecessors).some(function (id) { return !completed[id]; })) { return fail('broken_predecessor'); }
                var tool = completeTool(operation, input.toolLibraryVersion, feature, topologyFaces); if (!tool) { return fail('tool_envelope_invalid'); }
                var canonicalCapability = fixtureCatalog.capability(setup.fixtureId, setup.orientation.axis,
                    values(setup.clampFaceIds).map(function (id) { return topologyFaces[id]; }));
                if (!canonicalCapability || tool.reachMm > canonicalCapability.maximumToolReachMm || !sameAxis(operation.accessAxis, setup.orientation.axis)) { return fail('tool_envelope_unreachable'); }
                var semantic = canonicalSemanticPath(feature, operation, topologyFaces, blankBounds, features);
                if (!semantic || canonicalJson(semantic) !== canonicalJson(certificate.semanticPath)) { return fail('operation_path_invalid', {
                    operationId: operation.id, expectedSemanticPath: semantic, suppliedSemanticPath: certificate.semanticPath }); }
                if (!toolAssemblyClear(operation, tool, semantic, canonicalCapability, stock.toleranceMm)) { return fail('tool_fixture_collision', {
                    operationId: operation.id, fixtureId: setup.fixtureId, toolId: tool.id }); }
                if (operation.kind === 'roughing' && (semantic.removalPolicyVersion !== 'cnc-semantic-removal-2026-09-05-v1'
                    || !(Number(semantic.finishAllowanceMm) >= stock.resolutionMm * 2))) { return fail('finish_allowance_required'); }
                var inputChecksum = checksum(state), derived = deriveOperationSweep(state, operation, feature, semantic,
                    stock.toleranceMm, targetSolid, tool);
                if (derived.targetViolationCount > 0) { return fail('tool_target_collision', {
                    operationId: operation.id, featureId: feature.id, targetViolationCount: derived.targetViolationCount,
                    targetViolationCellCounts: derived.targetViolationCellCounts,
                    targetViolationSamples: derived.targetViolationSamples,
                    threadPath: semantic.geometry === 'brep_thread_groove' ? semantic : undefined }); }
                if (derived.stockViolationCount > 0) { return fail('tool_stock_collision', {
                    operationId: operation.id, featureId: feature.id, stockViolationCount: derived.stockViolationCount }); }
                var nonRemoving = operation.kind === 'deburring';
                if (!derived.removedCellCount && !nonRemoving) { return fail(operation.kind === 'finishing'
                    ? 'positive_finish_removal_required' : 'empty_material_sweep', { operationId: operation.id, featureId: feature.id }); }
                state = derived.state; completed[operation.id] = true;
                results.push({ operationId: operation.id, setupId: setup.id, toolId: tool.id, envelopeValidated: true,
                    occupancyVersion: stock.occupancyVersion, resolutionMm: stock.resolutionMm, toleranceMm: stock.toleranceMm,
                    inputChecksum: inputChecksum, outputChecksum: checksum(state), removedCellCount: derived.removedCellCount,
                    rawSweepChecksum: derived.rawSweepChecksum || null,
                    rawSweepCellCount: Number(derived.rawSweepCellCount) || null,
                    boundaryContactMaskChecksum: derived.boundaryContactMaskChecksum || null,
                    validatedRemovalMaskChecksum: derived.validatedRemovalMaskChecksum || null,
                    validatedRemovalCellCount: Number(derived.validatedRemovalCellCount) || 0,
                    validatedRemovalRuns: derived.validatedRemovalRuns || Object.freeze([]),
                    assemblySweepCellCounts: derived.assemblySweepCellCounts || null,
                    assemblyAxialSpansMm: derived.assemblyAxialSpansMm || null,
                    trajectorySampleCount: Number(derived.trajectorySampleCount) || null,
                    toolReference: derived.toolReference || null,
                    toolOrientationAxis: derived.toolOrientationAxis || null,
                    boundaryContactCellCount: Number(derived.boundaryContactCellCount) || 0,
                    occupancyFrame: { origin: clone(state.origin), dimensions: clone(state.dimensions) },
                    accessAxis: clone(operation.accessAxis), topologyFaceIds: values(feature.primaryFaceIds).slice().sort() });
            }
            priorOutput = setup.outputStockState;
        }
        if (priorOutput !== input.setupPlan.outputStockState) { return fail('stock_transition_mismatch'); }
        var residual = terminalResidual(state, targetSolid, stock);
        if (residual.missingTargetCellCount > residual.allowedMissingTargetCellCount
            || residual.excessStockCellCount > residual.allowedExcessStockCellCount) {
            return fail(residual.missingTargetCellCount ? 'target_solid_violation' : 'terminal_residual_mismatch', residual);
        }
        var sealedTopology = contracts.topologyEvidence(input.topology);
        var plan = { contract: 'ValidatedManufacturingPlan.v1', geometryRevision: input.topology.revision,
            requirementsRevision: input.requirementsRevision, plannerVersion: 'cnc-feature-planner-v3',
            toolLibraryVersion: input.toolLibraryVersion, topology: sealedTopology, featureGraph: clone(input.featureGraph),
            operationGraph: clone(input.operationGraph), setupPlan: clone(input.setupPlan), validationResults: results,
            validationSolid: { contract: 'CncTargetSolidValidation.v1', resolutionMm: stock.resolutionMm,
                occupancyChecksum: targetSolid.checksum, occupiedCellCount: targetSolid.occupiedCellCount },
            terminalResidual: residual, unresolvedReasons: [] };
        plan.planHash = await contracts.hash(plan);
        try { await contracts.validatePlan(plan); } catch (error) { return fail(error && error.code || 'plan_contract_invalid'); }
        return freeze({ valid: true, plan: plan, reviewReasons: [] });
    }
    async function validate(input) { try { return await validateInternal(input); } catch (_) { return fail('plan_validation_failed'); } }
    root.CncPlanValidator = Object.freeze({ validate: validate, stockContract: STOCK_CONTRACT,
        occupancyVersion: OCCUPANCY_VERSION, deriveOperationSweep: deriveOperationSweep });
}(typeof self !== 'undefined' ? self : window));
