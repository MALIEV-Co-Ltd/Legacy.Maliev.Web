(function (root) {
    'use strict';

    var fixtureCatalog = root.CncFixtureCatalog, toolLibrary = root.CncToolLibrary;
    if (!fixtureCatalog || !toolLibrary) { throw new Error('The authoritative CNC fixture and tool catalogs must load before manufacturing evidence.'); }
    var FIXTURE_CATALOG_VERSION = fixtureCatalog.version;
    var REMOVAL_POLICY_VERSION = 'cnc-semantic-removal-2026-09-05-v1';
    var STANDARD_VISE = Object.freeze(fixtureCatalog.resolve('vise-100-standard'));

    function values(value) { return Array.isArray(value) ? value : []; }
    function finitePoint(point) { return point && Number.isFinite(point.x) && Number.isFinite(point.y) && Number.isFinite(point.z); }
    function clone(value) { return JSON.parse(JSON.stringify(value)); }
    function normalized(axis) {
        if (!finitePoint(axis)) { return null; }
        var length = Math.hypot(axis.x, axis.y, axis.z);
        return length > 0 ? { x: axis.x / length, y: axis.y / length, z: axis.z / length } : null;
    }
    function axisKey(axis) {
        axis = normalized(axis);
        return axis && ['x', 'y', 'z'].map(function (name) { return Math.round(axis[name] * 1000000); }).join(':');
    }
    function sameAxis(left, right) { return axisKey(left) === axisKey(right); }
    function parallelAxis(left, right) {
        left = normalized(left); right = normalized(right);
        return left && right && Math.abs(left.x * right.x + left.y * right.y + left.z * right.z) > 0.999999;
    }
    function oppositeAxis(left, right) {
        left = normalized(left); right = normalized(right);
        return left && right && left.x * right.x + left.y * right.y + left.z * right.z < -0.999999;
    }
    function cardinalAxis(axis) {
        axis = normalized(axis);
        if (!axis) { return null; }
        var name = ['x', 'y', 'z'].sort(function (a, b) { return Math.abs(axis[b]) - Math.abs(axis[a]); })[0];
        return Math.abs(axis[name]) > 0.999999 ? { name: name, sign: axis[name] < 0 ? -1 : 1 } : null;
    }
    function validBounds(bounds) {
        return bounds && finitePoint(bounds.minimum) && finitePoint(bounds.maximum)
            && bounds.maximum.x > bounds.minimum.x && bounds.maximum.y > bounds.minimum.y
            && bounds.maximum.z > bounds.minimum.z;
    }
    function faceNormal(face) {
        var surface = face && face.surface || {};
        return normalized(surface.normal || surface.axis || face.normal);
    }
    function planarFace(face) { return face && face.surface && face.surface.kind === 'plane' && faceNormal(face); }
    function boundsForFaces(ids, faceIndex) {
        var bounds = values(ids).map(function (id) { return faceIndex[id] && faceIndex[id].validationVolume; });
        if (!bounds.length || bounds.some(function (item) { return !validBounds(item); })) { return null; }
        return bounds.reduce(function (result, item) {
            if (!result) { return clone(item); }
            ['x', 'y', 'z'].forEach(function (name) {
                result.minimum[name] = Math.min(result.minimum[name], item.minimum[name]);
                result.maximum[name] = Math.max(result.maximum[name], item.maximum[name]);
            });
            return result;
        }, null);
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
        if (!threadSurface || !finitePoint(threadSurface.centerMm) || !finitePoint(threadSurface.axis)) { return null; }
        var candidates = values(Object.keys(featureIndex).map(function (id) { return featureIndex[id]; })).filter(function (candidate) {
            if (!candidate || candidate.kind !== 'outside_profile' || candidate.bodyId !== feature.bodyId
                || !connectedFeatures(feature, candidate, faceIndex)
                || !values(candidate.accessAxes).some(function (candidateAxis) { return parallelAxis(candidateAxis, path.accessAxis); })) { return false; }
            var candidateSurface = values(candidate.primaryFaceIds).map(function (id) { return faceIndex[id] && faceIndex[id].surface; })
                .find(function (surface) { return surface && (surface.kind === 'cylinder' || surface.kind === 'cone')
                    && parallelAxis(surface.axis, threadSurface.axis) && finitePoint(surface.centerMm); });
            if (!candidateSurface) { return false; }
            var centerOffset = Math.hypot(candidateSurface.centerMm[path.lateralNames[0]] - path.centerMm[path.lateralNames[0]],
                candidateSurface.centerMm[path.lateralNames[1]] - path.centerMm[path.lateralNames[1]]);
            var candidateRadius = Number(candidateSurface.radiusMm);
            return centerOffset <= modelTolerance && candidateRadius > path.majorRadiusMm + modelTolerance;
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
    function topologyBounds(topology) {
        return boundsForFaces(values(topology.faces).map(function (face) { return face.id; }),
            values(topology.faces).reduce(function (map, face) { map[face.id] = face; return map; }, {}));
    }
    function stockDimensions(quotedStock) {
        var size = quotedStock && quotedStock.stockSizeMm;
        return size && Number(size.x) > 0 && Number(size.y) > 0 && Number(size.z) > 0
            ? { x: Number(size.x), y: Number(size.y), z: Number(size.z) } : null;
    }
    function orientedStockDimensions(dimensions, targetBounds) {
        var names = ['x', 'y', 'z'], source = [dimensions.x, dimensions.y, dimensions.z], best = null;
        [[0,1,2],[0,2,1],[1,0,2],[1,2,0],[2,0,1],[2,1,0]].forEach(function (order) {
            var candidate = { x: source[order[0]], y: source[order[1]], z: source[order[2]] }, score = 0, fits = true;
            names.forEach(function (name) { var span = targetBounds.maximum[name] - targetBounds.minimum[name]; fits = fits && candidate[name] + 0.001 >= span; score += candidate[name] - span; });
            if (fits && (!best || score < best.score)) { best = { dimensions: candidate, score: score }; }
        });
        return best && best.dimensions;
    }
    function contactEnvelope(face) {
        return { topologyFaceId: face.id, surfaceKind: 'plane', normal: clone(faceNormal(face)),
            bounds: clone(face.validationVolume) };
    }
    function prepare(topology, featureGraph, operationGraph, quotedStock) {
        var dimensions = stockDimensions(quotedStock);
        if (!dimensions || !topology || topology.sourceKind !== 'brep') { return null; }
        var faces = values(topology.faces), faceIndex = faces.reduce(function (map, face) { map[face.id] = face; return map; }, {});
        var datums = values(featureGraph.features).filter(function (feature) {
            return feature.kind === 'datum'
                && values(feature.primaryFaceIds).some(function (id) { return planarFace(faceIndex[id]); });
        });
        var axes = [], seen = Object.create(null);
        values(operationGraph.operations).forEach(function (operation) {
            var key = axisKey(operation.accessAxis); if (key && !seen[key]) { seen[key] = true; axes.push(operation.accessAxis); }
        });
        axes.sort(function (a, b) { return axisKey(a).localeCompare(axisKey(b)); });
        var candidates = [];
        axes.forEach(function (axis, index) {
            var datum = datums.find(function (feature) { return values(feature.accessAxes).some(function (item) { return sameAxis(item, axis); }); });
            var clampDatum = datums.find(function (feature) { return values(feature.accessAxes).some(function (item) { return oppositeAxis(item, axis); }); });
            var datumFace = datum && values(datum.primaryFaceIds).map(function (id) { return faceIndex[id]; })
                .find(function (face) { return planarFace(face) && sameAxis(faceNormal(face), axis); });
            var clampFace = clampDatum && values(clampDatum.primaryFaceIds).map(function (id) { return faceIndex[id]; })
                .find(function (face) { return planarFace(face) && oppositeAxis(faceNormal(face), axis); });
            var cardinal = cardinalAxis(axis), canonicalCapability = fixtureCatalog.capability(STANDARD_VISE.id, axis, [clampFace]);
            if (!datum || !clampDatum || !datumFace || !clampFace || !cardinal || !canonicalCapability
                || dimensions[cardinal.name] > STANDARD_VISE.maximumOpeningMm) { return; }
            candidates.push({ id: 'fixture-candidate-' + index, orientation: clone(normalized(axis)),
                fixtureId: STANDARD_VISE.id, fixtureState: 'vise', datumFeatureIds: [datum.id],
                datumFaceIds: [datumFace.id], clampFaceIds: [clampFace.id],
                supportedOperationIds: values(operationGraph.operations).filter(function (operation) { return sameAxis(operation.accessAxis, axis); }).map(function (operation) { return operation.id; }),
                inputStockState: 'manufacturing-stock-' + index,
                outputStockState: 'manufacturing-stock-' + (index + 1), handlingMinutes: 3,
                catalogCapability: { catalogVersion: FIXTURE_CATALOG_VERSION, fixtureId: STANDARD_VISE.id,
                    kind: STANDARD_VISE.kind, jawWidthMm: STANDARD_VISE.jawWidthMm,
                    maximumOpeningMm: STANDARD_VISE.maximumOpeningMm, jawHeightMm: STANDARD_VISE.jawHeightMm,
                    minimumGripMm: STANDARD_VISE.minimumGripMm },
                datumContact: contactEnvelope(datumFace), clampContact: contactEnvelope(clampFace),
                fixtureCapability: canonicalCapability });
        });
        if (!axes.length || candidates.length !== axes.length) { return null; }
        var planarIds = faces.filter(planarFace).map(function (face) { return face.id; }).sort();
        var states = [];
        for (var stateIndex = 0; stateIndex <= candidates.length; stateIndex++) {
            states.push({ id: 'manufacturing-stock-' + stateIndex,
                compatibleFixtureIds: [STANDARD_VISE.id], availableDatumFaceIds: planarIds.slice(),
                availableClampFaceIds: planarIds.slice(), supplierBlank: { dimensionsMm: clone(dimensions) } });
        }
        return { setupStock: { stateId: states[0].id, states: states },
            fixtureCatalog: { contract: 'CncFixtureCatalog.v2', version: FIXTURE_CATALOG_VERSION,
                fixtures: [clone(STANDARD_VISE)], candidates: candidates } };
    }

    function semanticPath(feature, operation, faceIndex, blankBounds, featureIndex) {
        var holeInterval = feature.kind === 'hole' ? root.CncPlanContracts.finiteHoleInterval(feature, operation, faceIndex) : null;
        if (feature.kind === 'hole' && !holeInterval) { return null; }
        var bounds = boundsForFaces(feature.primaryFaceIds, faceIndex), axis = cardinalAxis(operation.accessAxis);
        if (!bounds || !axis) { return null; }
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
        var selectedTool = toolLibrary.get(operation.toolId);
        if (!selectedTool) { return null; }
        var path = { operationKind: operation.kind, phase: operation.phase, accessAxis: clone(normalized(operation.accessAxis)),
            topologyFaceIds: values(feature.primaryFaceIds).slice().sort(), featureKind: feature.kind,
            featureBounds: clone(bounds), removalPolicyVersion: REMOVAL_POLICY_VERSION,
            finishAllowanceMm: operation.kind === 'roughing' ? 1 : 0,
            toolPath: { contract: 'CncCanonicalToolPath.v1', toolId: selectedTool.id,
                cutterDiameterMm: selectedTool.diameterMm, usableCutLengthMm: selectedTool.usableCutLengthMm,
                reachMm: selectedTool.reachMm, shankDiameterMm: selectedTool.shankDiameterMm,
                holderDiameterMm: selectedTool.holderDiameterMm } };
        if (feature.kind === 'slot' || feature.kind === 'pocket') {
            var corridor = root.CncPlanContracts.prismaticCorridor(feature, operation, faceIndex);
            if (!corridor) { return null; }
            Object.assign(path, corridor);
            path.toolPath.referencePoint = 'cutter_tip'; path.toolPath.orientationAxis = clone(normalized(operation.accessAxis));
        } else if (operation.kind === 'thread_milling' && (feature.kind === 'external_thread' || feature.kind === 'internal_thread') && diameter > 0) {
            path.geometry = 'brep_thread_groove'; path.axisName = axis.name; path.axisSign = axis.sign;
            path.lateralNames = lateral; path.centerMm = {};
            path.centerMm[lateral[0]] = Number.isFinite(centre[lateral[0]]) ? centre[lateral[0]] : (bounds.minimum[lateral[0]] + bounds.maximum[lateral[0]]) / 2;
            path.centerMm[lateral[1]] = Number.isFinite(centre[lateral[1]]) ? centre[lateral[1]] : (bounds.minimum[lateral[1]] + bounds.maximum[lateral[1]]) / 2;
            path.radiusMm = diameter / 2; path.majorRadiusMm = diameter / 2;
            path.minimumAxialMm = bounds.minimum[axis.name]; path.maximumAxialMm = bounds.maximum[axis.name];
            path.pitchMm = Number(feature.pitchMm || feature.dimensions && feature.dimensions.pitchMm);
            path.minorRadiusMm = path.majorRadiusMm - Math.max(0.2, 0.6134 * path.pitchMm);
            path.cutterCenterRadiusMm = feature.kind === 'external_thread'
                ? path.minorRadiusMm + selectedTool.diameterMm / 2
                : path.majorRadiusMm - selectedTool.diameterMm / 2;
            path.handedness = feature.handedness === 'left' ? 'left' : 'right';
            path.toolPath.referencePoint = 'cutter_tip'; path.toolPath.orientationAxis = clone(normalized(operation.accessAxis));
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
            var coneMidpoint = { x: (bounds.minimum.x + bounds.maximum.x) / 2,
                y: (bounds.minimum.y + bounds.maximum.y) / 2, z: (bounds.minimum.z + bounds.maximum.z) / 2 };
            var coneAxial = Math.abs(coneMidpoint[axis.name] - Number(centre[axis.name] || 0));
            var coneRadial = Math.hypot(coneMidpoint[lateral[0]] - Number(centre[lateral[0]] || 0),
                coneMidpoint[lateral[1]] - Number(centre[lateral[1]] || 0));
            var coneDelta = coneAxial * Math.tan(Number(cone.surface.halfAngleRadians));
            var slopeSign = Math.abs(coneRadial - (radius + coneDelta)) <= Math.abs(coneRadial - Math.max(0, radius - coneDelta)) ? 1 : -1;
            path.geometry = 'chamfer_cone'; path.axisName = axis.name; path.axisSign = axis.sign;
            path.lateralNames = lateral; path.centerMm = clone(centre); path.radiusMm = radius;
            path.halfAngleRadians = Number(cone.surface.halfAngleRadians);
            path.radiusSlopeSign = slopeSign; path.materialSide = coneRadial >= radius ? 'outside' : 'inside';
            path.minimumAxialMm = bounds.minimum[axis.name]; path.maximumAxialMm = bounds.maximum[axis.name];
        } else if (radius > 0 && (feature.kind === 'hole' || feature.kind === 'internal_thread'
            || feature.kind === 'external_thread' || feature.kind === 'outside_profile')) {
            path.geometry = 'axial_cylinder'; path.axisName = axis.name; path.axisSign = axis.sign;
            path.lateralNames = lateral; path.centerMm = {};
            path.centerMm[lateral[0]] = Number.isFinite(centre[lateral[0]]) ? centre[lateral[0]] : (bounds.minimum[lateral[0]] + bounds.maximum[lateral[0]]) / 2;
            path.centerMm[lateral[1]] = Number.isFinite(centre[lateral[1]]) ? centre[lateral[1]] : (bounds.minimum[lateral[1]] + bounds.maximum[lateral[1]]) / 2;
            path.radiusMm = feature.kind === 'external_thread' && diameter > 0 ? diameter / 2 : radius;
            path.minimumAxialMm = bounds.minimum[axis.name]; path.maximumAxialMm = bounds.maximum[axis.name];
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
            var datumFace = datumFaces[0];
            if (!datumFace || !validBounds(datumFace.validationVolume) || !validBounds(blankBounds)) { return null; }
            path.geometry = 'planar_facing';
            path.axisName = axis.name;
            path.axisSign = axis.sign;
            path.targetPlaneMm = (datumFace.validationVolume.minimum[axis.name]
                + datumFace.validationVolume.maximum[axis.name]) / 2;
            path.planarRegions = datumFaces.map(function (face) {
                var faceLoops = values(face.loops), loops = faceLoops.map(function (loop) { return values(loop.vertices).map(clone); });
                if (faceLoops.length && loops.every(function (loop) { return loop.length >= 3; })) { return { kind: 'polygon_loops', loops: loops }; }
                var cylinders = values(face.adjacentFaceIds).map(function (id) { return faceIndex[id]; }).filter(function (adjacent) {
                    return adjacent && adjacent.surface && (adjacent.surface.kind === 'cylinder' || adjacent.surface.kind === 'cone')
                        && parallelAxis(adjacent.surface.axis, operation.accessAxis) && Number(adjacent.surface.radiusMm) > 0;
                });
                if (cylinders.length >= 2) {
                    var radii = cylinders.map(function (adjacent) { return Number(adjacent.surface.radiusMm); }).sort(function (a, b) { return a - b; });
                    var center = cylinders[0].surface.centerMm || {};
                    return { kind: 'radial_band', centerMm: clone(center), minimumRadiusMm: radii[0], maximumRadiusMm: radii[radii.length - 1] };
                }
                return null;
            }).filter(Boolean);
            if (!path.planarRegions.length) { return null; }
            var protectedRegions = datumFaces.reduce(function (regions, face) {
                values(face.adjacentFaceIds).map(function (id) { return faceIndex[id]; }).forEach(function (adjacent) {
                    var surface = adjacent && adjacent.surface, volume = adjacent && adjacent.validationVolume;
                    if (!surface || !validBounds(volume) || (surface.kind !== 'cylinder' && surface.kind !== 'cone')
                        || !parallelAxis(surface.axis, operation.accessAxis) || !finitePoint(surface.centerMm)) { return; }
                    var supportBounds = surface.boundsMm, supportMinimum = supportBounds && supportBounds.min,
                        supportMaximum = supportBounds && supportBounds.max;
                    if (!finitePoint(supportMinimum) || !finitePoint(supportMaximum)) { return; }
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

    function complete(topology, featureGraph, operationGraph, setupPlan, quotedStock) {
        var quotedDimensions = stockDimensions(quotedStock), targetBounds = topologyBounds(topology);
        var dimensions = quotedDimensions && targetBounds && orientedStockDimensions(quotedDimensions, targetBounds);
        if (!dimensions || !targetBounds) { return null; }
        var centre = { x: (targetBounds.minimum.x + targetBounds.maximum.x) / 2,
            y: (targetBounds.minimum.y + targetBounds.maximum.y) / 2,
            z: (targetBounds.minimum.z + targetBounds.maximum.z) / 2 };
        var origin = { x: centre.x - dimensions.x / 2, y: centre.y - dimensions.y / 2, z: centre.z - dimensions.z / 2 };
        var blankBounds = { minimum: clone(origin), maximum: {
            x: origin.x + dimensions.x, y: origin.y + dimensions.y, z: origin.z + dimensions.z } };
        var features = values(featureGraph.features).reduce(function (map, feature) { map[feature.id] = feature; return map; }, {});
        var operations = values(operationGraph.operations).reduce(function (map, operation) { map[operation.id] = operation; return map; }, {});
        var faceIndex = values(topology.faces).reduce(function (map, face) { map[face.id] = face; return map; }, {});
        var paths = [];
        values(setupPlan.setups).forEach(function (setup) {
            values(setup.operationIds).forEach(function (operationId) {
                var operation = operations[operationId], feature = operation && features[operation.featureId];
                var path = feature && semanticPath(feature, operation, faceIndex, blankBounds, features);
                if (path) { paths.push({ contract: 'CncOperationSemanticPath.v1', geometryRevision: topology.revision,
                    operationId: operation.id, featureId: feature.id, setupId: setup.id,
                    inputStockState: setup.inputStockState, outputStockState: setup.outputStockState,
                    toolId: operation.toolId, toolClass: operation.toolClass, semanticPath: path }); }
            });
        });
        if (paths.length !== values(operationGraph.operations).length) { return null; }
        return { contract: 'CncValidationStock.v2', occupancyVersion: 2, resolutionMm: 0.5, toleranceMm: 0.05,
            supplierBlank: { source: 'quoted_supplier_blank', shape: quotedStock.stockShape || 'block',
                quotedDimensionsMm: quotedDimensions, dimensionsMm: dimensions, originMm: origin },
            targetSolid: { geometryRevision: topology.revision, sourceKind: topology.sourceKind,
                bodyIds: values(topology.bodies).map(function (body) { return body.id; }).sort(),
                topologyFaceIds: values(topology.faces).map(function (face) { return face.id; }).sort(), bounds: targetBounds },
            states: [{ id: setupPlan.inputStockState }].concat(values(setupPlan.setups).map(function (setup) { return { id: setup.outputStockState }; })),
            operationPaths: paths };
    }

    root.CncManufacturingEvidence = Object.freeze({ prepare: prepare, complete: complete,
        fixtureCatalogVersion: FIXTURE_CATALOG_VERSION, removalPolicyVersion: REMOVAL_POLICY_VERSION });
}(typeof self !== 'undefined' ? self : window));
