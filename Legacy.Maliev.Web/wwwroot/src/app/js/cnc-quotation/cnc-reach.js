(function (root) {
    'use strict';

    var materialCatalog = root.CncMaterialCatalog;
    var toolLibrary = root.CncToolLibrary;
    if (!materialCatalog || !toolLibrary) {
        throw new Error('CNC material and tool catalogs must load before tool reach analysis.');
    }

    var confidenceRank = Object.freeze({ High: 0, Medium: 1, Low: 2 });
    var operationNames = Object.freeze({
        facing: 'Facing',
        roughing: 'Pocket roughing',
        finishing: 'Flat finishing',
        profiling: 'Profile milling',
        freeform_finishing: 'Ball finishing',
        slotting: 'Slot milling',
        drilling: 'Drilling',
        spot_drilling: 'Spot drilling',
        reaming: 'Reaming',
        tapping: 'Tapping',
        thread_milling: 'Thread milling',
        chamfering: 'Chamfering',
        engraving: 'Engraving',
        tab_removal: 'Tab removal'
    });
    var fieldIndexCache = typeof WeakMap === 'function' ? new WeakMap() : null;

    function fieldIndexFor(field) {
        if (!field || field.degraded === true || !Array.isArray(field.surfaceSamples)) {
            return {
                samplesByCluster: Object.create(null),
                accessByDirectionTool: Object.create(null),
                coverageByClusterDirectionTool: Object.create(null)
            };
        }
        if (fieldIndexCache && fieldIndexCache.has(field)) { return fieldIndexCache.get(field); }
        var index = {
            samplesByCluster: Object.create(null),
            accessByDirectionTool: Object.create(null),
            coverageByClusterDirectionTool: Object.create(null)
        };
        field.surfaceSamples.forEach(function (sample) {
            if (!index.samplesByCluster[sample.clusterId]) { index.samplesByCluster[sample.clusterId] = []; }
            index.samplesByCluster[sample.clusterId].push(sample);
        });
        Object.keys(field.toolAccess || {}).forEach(function (directionId) {
            Object.keys(field.toolAccess[directionId] || {}).forEach(function (toolId) {
                var access = field.toolAccess[directionId][toolId];
                index.accessByDirectionTool[directionId + '\u0000' + toolId] = {
                    reachable: new Set(access.reachableSampleIds || []),
                    tip: new Set(access.tipSampleIds || []),
                    flute: new Set(access.fluteSampleIds || [])
                };
            });
        });
        if (fieldIndexCache) { fieldIndexCache.set(field, index); }
        return index;
    }

    function finitePositive(value) {
        return typeof value === 'number' && Number.isFinite(value) && value > 0;
    }

    function addReason(reasons, code) {
        if (reasons.indexOf(code) === -1) {
            reasons.push(code);
        }
    }

    function normalizedConfidence(value) {
        return Object.prototype.hasOwnProperty.call(confidenceRank, value) ? value : 'Low';
    }

    function lowerConfidence(current, candidate) {
        candidate = normalizedConfidence(candidate);
        return confidenceRank[candidate] > confidenceRank[current] ? candidate : current;
    }

    function normalizedVector(value) {
        if (!value || !Number.isFinite(value.x) || !Number.isFinite(value.y) || !Number.isFinite(value.z)) {
            return null;
        }

        var length = Math.sqrt((value.x * value.x) + (value.y * value.y) + (value.z * value.z));
        return length > 0 ? { x: value.x / length, y: value.y / length, z: value.z / length } : null;
    }

    function dot(left, right) {
        return left && right ? (left.x * right.x) + (left.y * right.y) + (left.z * right.z) : 0;
    }

    function materialClass(material) {
        var record = materialCatalog.get(material);
        if (!record) {
            return null;
        }

        return record.family === 'engineering_plastic' ? 'plastic' : 'metal';
    }

    function clusterOperations(cluster) {
        if (Array.isArray(cluster.operationCodes) && cluster.operationCodes.length > 0) {
            return cluster.operationCodes.filter(function (code) { return typeof code === 'string'; });
        }
        if (typeof cluster.operation === 'string' && cluster.operation.length > 0) {
            return [cluster.operation];
        }

        if (cluster.type === 'conical') { return ['chamfering']; }
        if (cluster.type === 'spherical' || cluster.type === 'freeform') { return ['freeform_finishing']; }
        return ['finishing'];
    }

    function orientationCosine(cluster, setup) {
        var direction = normalizedVector(setup.direction || setup.toolDirection);
        var normal = normalizedVector(cluster.normal);
        var axis = normalizedVector(cluster.featureAxis || cluster.axis);
        if (!direction) {
            return 0;
        }
        if (cluster.type === 'planar' && normal) {
            return Math.max(0, dot(normal, direction));
        }
        if (axis) {
            return Math.abs(dot(axis, direction));
        }
        if (normal) {
            return Math.max(0, dot(normal, direction));
        }
        return 0;
    }

    function orientationCompatible(cluster, setup, operationCode) {
        var setupId = String(setup.id || ('setup-' + setup.number));
        var projectedSurfaceOperations = ['roughing', 'finishing', 'profiling', 'freeform_finishing', 'chamfering'];
        if (Array.isArray(cluster.accessibleDirectionIds)
            && cluster.accessibleDirectionIds.indexOf(setupId) >= 0
            && projectedSurfaceOperations.indexOf(operationCode) >= 0) {
            // Freeform and curved clusters do not have one meaningful normal. The worker's
            // per-triangle ray field is the stronger evidence: the compatible cutter follows
            // only the visible patch, and the viewer keeps inaccessible triangles excluded.
            return true;
        }
        if (cluster.type === 'planar' && operationCode === 'profiling') {
            var direction = normalizedVector(setup.direction || setup.toolDirection);
            var normal = normalizedVector(cluster.normal);
            return direction && normal && Math.abs(dot(normal, direction)) <= 0.20;
        }

        return orientationCosine(cluster, setup) >= 0.80;
    }

    function holderClearanceDepth(tool) {
        var holderRadius = finitePositive(tool.holderDiameterMm) ? tool.holderDiameterMm * 0.5 : 0;
        var shankRadius = finitePositive(tool.shankDiameterMm) ? tool.shankDiameterMm * 0.5 : 0;
        return Math.max(0, tool.reachMm - Math.max(0, holderRadius - shankRadius));
    }

    function physicalClearanceFits(clearance, tool, toleranceMm) {
        toleranceMm = Number.isFinite(toleranceMm) && toleranceMm >= 0 ? toleranceMm : 0;
        return clearance && finitePositive(clearance.cutterRadiusMm)
            && finitePositive(clearance.shankRadiusMm) && finitePositive(clearance.holderRadiusMm)
            && clearance.cutterRadiusMm + toleranceMm >= tool.diameterMm * 0.5
            && clearance.shankRadiusMm + toleranceMm >= tool.shankDiameterMm * 0.5
            && clearance.holderRadiusMm + toleranceMm >= tool.holderDiameterMm * 0.5;
    }

    function validatePhysicalEnvelope(input) {
        input = input || {};
        var tool = input.tool || {};
        var depthMm = Number(input.requiredDepthMm);
        var valid = finitePositive(tool.diameterMm) && finitePositive(tool.usableCutLengthMm)
            && finitePositive(tool.reachMm) && finitePositive(tool.shankDiameterMm)
            && finitePositive(tool.holderDiameterMm) && finitePositive(depthMm)
            && depthMm <= tool.usableCutLengthMm + (input.toleranceMm || 0)
            && depthMm <= tool.reachMm + (input.toleranceMm || 0)
            && physicalClearanceFits(input.stockClearance, tool, input.toleranceMm)
            && physicalClearanceFits(input.topologyClearance, tool, input.toleranceMm);
        return Object.freeze({ valid: valid,
            reason: valid ? null : 'tool_envelope_unreachable' });
    }

    function fieldDirectionId(field, setup) {
        var setupId = String(setup.id || '');
        if (field.toolAccess && field.toolAccess[setupId]) { return setupId; }
        var direction = normalizedVector(setup.direction || setup.toolDirection);
        if (!direction || !Array.isArray(field.axes) || field.axes.length !== 3) { return null; }
        var best = null;
        field.axes.forEach(function (axis, axisIndex) {
            var alignment = dot(direction, normalizedVector(axis));
            if (!best || Math.abs(alignment) > best.absoluteAlignment) {
                best = {
                    absoluteAlignment: Math.abs(alignment),
                    id: (alignment >= 0 ? 'positive-' : 'negative-') + ['x', 'y', 'z'][axisIndex]
                };
            }
        });
        return best && best.absoluteAlignment >= 0.98 ? best.id : null;
    }

    function fieldCoverage(geometry, cluster, setup, tool, fieldIndex) {
        var field = geometry && geometry.accessibilityField;
        if (!field || field.degraded === true || !tool || !tool.id
            || !field.toolAccess || !Array.isArray(field.surfaceSamples)) {
            return null;
        }
        var directionId = fieldDirectionId(field, setup);
        var analysisProfileId = tool.analysisProfileId || tool.id;
        var directionAccess = directionId && field.toolAccess[directionId]
            ? field.toolAccess[directionId] : null;
        var accessToolId = directionAccess && directionAccess[analysisProfileId]
            ? analysisProfileId : tool.id;
        var access = directionAccess ? directionAccess[accessToolId] : null;
        if (!access) { return null; }
        var samples = fieldIndex && fieldIndex.samplesByCluster[cluster.id]
            ? fieldIndex.samplesByCluster[cluster.id] : [];
        if (samples.length === 0) { return null; }
        var accessKey = directionId + '\u0000' + accessToolId;
        var coverageKey = cluster.id + '\u0000' + accessKey;
        if (fieldIndex && fieldIndex.coverageByClusterDirectionTool[coverageKey]) {
            return fieldIndex.coverageByClusterDirectionTool[coverageKey];
        }
        var indexedAccess = fieldIndex && fieldIndex.accessByDirectionTool[accessKey];
        var reachable = indexedAccess ? indexedAccess.reachable : new Set(access.reachableSampleIds || []);
        var tip = indexedAccess ? indexedAccess.tip : new Set(access.tipSampleIds || []);
        var flute = indexedAccess ? indexedAccess.flute : new Set(access.fluteSampleIds || []);
        // A sampled flute envelope cannot override a confirmed CAD obstruction.
        // Missing directional evidence permits the field fallback; an empty list
        // explicitly means that none of this face's triangles is visible.
        var directionalTriangles = cluster.accessibleTriangleIndexesByDirection;
        var visibleTriangles = directionalTriangles && directionalTriangles[directionId];
        var visible = Array.isArray(visibleTriangles) ? new Set(visibleTriangles) : null;
        var coverage = samples.reduce(function (result, sample) {
            if (!reachable.has(sample.id)) { return result; }
            if (visible && (!visible.size || (Number.isInteger(sample.sourceTriangleIndex)
                && !visible.has(sample.sourceTriangleIndex)))) { return result; }
            result.reachableSampleIds.push(sample.id);
            result.reachableSampleCount += 1;
            if (tip.has(sample.id)) { result.tipSampleCount += 1; }
            if (flute.has(sample.id)) { result.fluteSampleCount += 1; }
            result.reachableAreaMm2 += finitePositive(sample.areaMm2) ? sample.areaMm2 : 0;
            return result;
        }, {
            directionId: directionId,
            accessToolId: accessToolId,
            sampleCount: samples.length,
            reachableSampleCount: 0,
            tipSampleCount: 0,
            fluteSampleCount: 0,
            reachableAreaMm2: 0,
            reachableSampleIds: []
        });
        if (fieldIndex) { fieldIndex.coverageByClusterDirectionTool[coverageKey] = coverage; }
        return coverage;
    }

    function limitingFactor(cluster, setup, tool, operationCode, category, coverage) {
        var setupId = String(setup.id || ('setup-' + setup.number));
        var directional = ['drilling', 'spot_drilling', 'reaming', 'tapping', 'thread_milling']
            .indexOf(operationCode) >= 0 || operationCode === 'chamfering' && !!cluster.featureChamfer;
        var featureAxis = normalizedVector(cluster.featureAxis || cluster.axis);
        var direction = normalizedVector(setup.direction || setup.toolDirection);
        if (directional && featureAxis && (!direction || Math.abs(dot(featureAxis, direction)) < 0.995)) {
            return 'orientation';
        }
        var spotFeature = tool && tool.directSpotting && (operationCode === 'spot_drilling' || operationCode === 'chamfering')
            ? cluster.featureChamfer || cluster.featureHole : null;
        var spotEntry = spotFeature && spotFeature.spotEntryEvidence;
        var entryDirections = spotFeature
            ? spotEntry && spotEntry.byDiameterMm && spotEntry.byDiameterMm[String(tool.diameterMm)] || []
            : directional && cluster.featureEntryDirections;
        var verifiedEntry = Array.isArray(entryDirections) && entryDirections.some(function (entry) {
            return direction && dot(normalizedVector(entry), direction) >= 0.995;
        });
        if (Array.isArray(entryDirections) && !verifiedEntry) { return 'body_occlusion'; }
        if (!verifiedEntry && coverage && coverage.reachableSampleCount === 0) {
            return 'body_occlusion';
        }
        if (!verifiedEntry && Array.isArray(cluster.accessibleDirectionIds)
            && !coverage
            && cluster.accessibleDirectionIds.indexOf(setupId) === -1) {
            return 'body_occlusion';
        }
        if (!tool || tool.enabled === false || !Array.isArray(tool.operations)
            || tool.operations.indexOf(operationCode) === -1) {
            return 'operation_compatibility';
        }
        if (!category || !Array.isArray(tool.materials) || tool.materials.indexOf(category) === -1) {
            return 'material_compatibility';
        }
        if (!verifiedEntry && !coverage && !orientationCompatible(cluster, setup, operationCode)) {
            return 'orientation';
        }

        var openingWidth = finitePositive(cluster.openingWidthMm) ? cluster.openingWidthMm
            : finitePositive(cluster.diameterMm) ? cluster.diameterMm : null;
        if (cluster.operationOpeningWidthsMm && finitePositive(cluster.operationOpeningWidthsMm[operationCode])) {
            openingWidth = cluster.operationOpeningWidthsMm[operationCode];
        }
        var diameterTolerance = operationCode === 'drilling' ? 0.05 : directional ? 1e-5 : 0;
        var spotDepth = null;
        if (spotFeature) {
            var spotDiameter = cluster.featureChamfer ? spotFeature.majorDiameterMm : spotFeature.diameterMm * 0.5;
            var pilotDiameter = cluster.featureChamfer ? spotFeature.minorDiameterMm : spotFeature.diameterMm;
            if (tool.diameterMm < spotDiameter || tool.minimumHoleDiameterMm > pilotDiameter
                || Math.abs(tool.includedAngleDegrees - (spotFeature.includedAngleDegrees || 90)) > 0.1) { return 'tool_diameter'; }
            spotDepth = spotDiameter / (2 * Math.tan(tool.includedAngleDegrees * Math.PI / 360));
            if (!spotEntry || tool.holderDiameterMm > spotEntry.holderDiameterMm
                || tool.reachMm - spotDepth < spotEntry.holderStartAboveEntryMm) { return 'holder_clearance'; }
            if (!cluster.featureChamfer && spotDepth > spotFeature.depthMm) { return 'body_occlusion'; }
            // The certificate proves the complete cutter envelope above the entry;
            // only its cone enters the pilot, not the full shank diameter.
            openingWidth = null;
        }
        if (cluster.featureChamfer && spotFeature) {
            var chamfer = cluster.featureChamfer;
            if (Math.abs(tool.includedAngleDegrees - chamfer.includedAngleDegrees) > 0.1
                || tool.diameterMm < chamfer.majorDiameterMm
                || tool.minimumHoleDiameterMm > chamfer.minorDiameterMm) { return 'tool_diameter'; }
            var tipDepthBelow = (chamfer.minorDiameterMm - tool.pointDiameterMm)
                / (2 * Math.tan(tool.includedAngleDegrees * Math.PI / 360));
            if (!Number.isFinite(chamfer.verifiedPilotDepthBelowMm)
                || chamfer.verifiedPilotDepthBelowMm < tipDepthBelow
                || !finitePositive(chamfer.pilotDiameterMm)
                || chamfer.pilotDiameterMm < tool.pointDiameterMm) { return 'body_occlusion'; }
        }
        if (openingWidth !== null && finitePositive(tool.diameterMm) && tool.diameterMm > openingWidth + diameterTolerance) {
            return 'tool_diameter';
        }

        // For a worker-generated surface cluster, axialDepthMm is its span along the fitted
        // analysis axis, not cutter engagement depth. Directional ray evidence identifies these
        // sampled surfaces; feature proxies supply requiredDepthMm for pockets, holes, and threads
        // when flute length really applies.
        var hasProjectedSurfaceEvidence = Array.isArray(cluster.accessibleDirectionIds);
        var operationDepth = cluster.operationDepthsMm && cluster.operationDepthsMm[operationCode];
        var depth = spotDepth !== null ? spotDepth : finitePositive(operationDepth) ? operationDepth
            : finitePositive(cluster.requiredDepthMm) ? cluster.requiredDepthMm
            : !hasProjectedSurfaceEvidence && cluster.type !== 'planar' && finitePositive(cluster.axialDepthMm)
            ? cluster.axialDepthMm : 0;
        if (depth > tool.usableCutLengthMm + (spotDepth !== null ? 1e-9 : 0)) {
            return 'cutting_length';
        }
        if (depth > tool.reachMm) {
            return 'tool_reach';
        }
        if (openingWidth !== null && openingWidth < tool.holderDiameterMm
            && depth > holderClearanceDepth(tool)) {
            return 'holder_clearance';
        }
        return null;
    }

    function recordFor(geometry, cluster, setup, tool, operationCode, category, fieldIndex) {
        var toolCompatible = tool && tool.enabled !== false && Array.isArray(tool.operations)
            && tool.operations.indexOf(operationCode) !== -1 && category
            && Array.isArray(tool.materials) && tool.materials.indexOf(category) !== -1;
        var coverage = toolCompatible ? fieldCoverage(geometry, cluster, setup, tool, fieldIndex) : null;
        var limit = limitingFactor(cluster, setup, tool, operationCode, category, coverage);
        return {
            clusterId: cluster.id,
            setupId: setup.id || ('setup-' + setup.number),
            setupNumber: setup.number,
            operationCode: operationCode,
            operationName: operationNames[operationCode] || 'CNC machining',
            toolId: tool && tool.id || null,
            analysisProfileId: coverage && coverage.accessToolId
                || tool && (tool.analysisProfileId || tool.id) || null,
            toolType: tool && tool.family || null,
            toolDiameterMm: tool && tool.diameterMm || 0,
            usableCutLengthMm: tool && tool.usableCutLengthMm || 0,
            reachMm: tool && tool.reachMm || 0,
            holderDiameterMm: tool && tool.holderDiameterMm || 0,
            orientationCosine: orientationCosine(cluster, setup),
            accessEvidence: coverage ? 'field' : 'cluster',
            fieldDirectionId: coverage && coverage.directionId || null,
            fieldSampleCount: coverage && coverage.sampleCount || 0,
            reachableSampleCount: coverage && coverage.reachableSampleCount || 0,
            tipSampleCount: coverage && coverage.tipSampleCount || 0,
            fluteSampleCount: coverage && coverage.fluteSampleCount || 0,
            reachableAreaMm2: coverage && coverage.reachableAreaMm2 || 0,
            reachableSampleIds: coverage && coverage.reachableSampleIds
                ? coverage.reachableSampleIds.slice() : [],
            reachable: limit === null,
            limitingFactor: limit,
            confidence: lowerConfidence(normalizedConfidence(cluster.confidence), tool && tool.confidence)
        };
    }

    function residualClustersFor(clusters, records) {
        return clusters.map(function (cluster) {
            var unreachableOperations = clusterOperations(cluster).filter(function (operationCode) {
                return !records.some(function (record) {
                    return record.clusterId === cluster.id && record.operationCode === operationCode && record.reachable;
                });
            });
            if (unreachableOperations.length === 0) { return null; }
            var failed = records.filter(function (record) {
                return record.clusterId === cluster.id && record.operationCode === unreachableOperations[0];
            });
            return {
                id: cluster.id,
                areaMm2: finitePositive(cluster.areaMm2) ? cluster.areaMm2 : 0,
                operationCodes: unreachableOperations,
                limitingFactor: failed.length > 0 ? failed[0].limitingFactor : 'no_reach_evidence',
                confidence: 'Low'
            };
        }).filter(function (cluster) { return cluster; });
    }

    function resultForRecords(geometry, clusters, records) {
        var confidence = records.reduce(function (current, record) {
            return lowerConfidence(current, record.confidence);
        }, 'High');
        var reviewReasons = [];
        var residualClusters = residualClustersFor(clusters, records);
        if (residualClusters.length > 0) {
            confidence = 'Low';
            addReason(reviewReasons, 'unreachable_tool_access');
        }
        if (records.length === 0) {
            confidence = 'Low';
            addReason(reviewReasons, 'missing_tool_reach_evidence');
        }
        if (geometry.accessibilityField && geometry.accessibilityField.degraded === true) {
            confidence = 'Low';
            addReason(reviewReasons, 'tool_access_field_degraded');
        }
        addReason(reviewReasons, 'tool_reach_advisory');
        return {
            records: records,
            residualClusters: residualClusters,
            confidence: confidence,
            reviewReasons: reviewReasons
        };
    }

    function selectSetups(evaluation, geometry, setups) {
        var setupNumbers = Object.create(null);
        (setups || []).forEach(function (setup, index) {
            setupNumbers[setup.id || ('setup-' + (index + 1))] = Number.isSafeInteger(setup.number) && setup.number > 0
                ? setup.number : index + 1;
        });
        var records = (evaluation && Array.isArray(evaluation.records) ? evaluation.records : [])
            .filter(function (record) { return Object.prototype.hasOwnProperty.call(setupNumbers, record.setupId); })
            .map(function (record) { return Object.assign({}, record, { setupNumber: setupNumbers[record.setupId] }); });
        var normalizedGeometry = geometry || {};
        var clusters = Array.isArray(normalizedGeometry.surfaceClusters) ? normalizedGeometry.surfaceClusters : [];
        return resultForRecords(normalizedGeometry, clusters, records);
    }

    function evaluate(input) {
        input = input || {};
        var geometry = input.geometry || {};
        var clusters = Array.isArray(geometry.surfaceClusters) ? geometry.surfaceClusters : [];
        var setups = Array.isArray(input.setups) ? input.setups : [];
        var suppliedTools = Array.isArray(input.tools);
        var tools = suppliedTools ? input.tools.filter(function (tool) { return tool; }) : toolLibrary.planningTools();
        var category = materialClass(input.material);
        var records = [];
        var confidence = 'High';
        var reviewReasons = [];
        var field = geometry.accessibilityField;
        var fieldIndex = fieldIndexFor(field);

        clusters.forEach(function (cluster) {
            clusterOperations(cluster).forEach(function (operationCode) {
                var operationTools = suppliedTools ? tools : tools.filter(function (tool) {
                    return tool.enabled !== false && Array.isArray(tool.operations)
                        && (!tool.materialCodes || tool.materialCodes.indexOf(input.material) >= 0)
                        && tool.operations.indexOf(operationCode) !== -1 && category
                        && Array.isArray(tool.materials) && tool.materials.indexOf(category) !== -1;
                });
                if (!suppliedTools && operationCode === 'drilling'
                    && Array.isArray(cluster.featureEntryDirections) && finitePositive(cluster.openingWidthMm)) {
                    // Feature entry has a verified axial corridor: evaluate the actual drill,
                    // not a 1/3/10 mm representative SKU sharing its coarse analysis envelope.
                    operationTools = toolLibrary.compatible(operationCode, input.material).filter(function (tool) {
                        return Math.abs(tool.diameterMm - cluster.openingWidthMm) <= 0.05;
                    });
                }
                if (!suppliedTools && operationCode === 'tapping' && cluster.featureThread
                    && finitePositive(cluster.featureThread.pitchMm)) {
                    var thread = cluster.featureThread;
                    operationTools = toolLibrary.compatible(operationCode, input.material).filter(function (tool) {
                        return tool.family === 'tap' && Math.abs(tool.pitchMm - thread.pitchMm) <= 0.01
                            && (!thread.handedness || thread.handedness === (tool.handedness || 'right'))
                            && Math.abs(tool.diameterMm - thread.majorDiameterMm) <= thread.pitchMm * 0.25
                            && (!finitePositive(thread.minorDiameterMm)
                                || Math.abs(tool.diameterMm - thread.pitchMm - thread.minorDiameterMm) <= thread.pitchMm * 0.2);
                    });
                }
                if (!suppliedTools && (operationCode === 'chamfering' && cluster.featureChamfer || operationCode === 'spot_drilling')) {
                    operationTools = toolLibrary.compatible(operationCode, input.material).filter(function (tool) {
                        return tool.directSpotting === true;
                    });
                }
                setups.forEach(function (setup, setupIndex) {
                    var normalizedSetup = Object.assign({}, setup, {
                        number: Number.isSafeInteger(setup.number) && setup.number > 0 ? setup.number : setupIndex + 1
                    });
                    operationTools.forEach(function (tool) {
                        var record = recordFor(geometry, cluster, normalizedSetup, tool, operationCode, category, fieldIndex);
                        records.push(record);
                        confidence = lowerConfidence(confidence, record.confidence);
                    });
                });
            });
        });

        var residualClusters = residualClustersFor(clusters, records);

        if (residualClusters.length > 0) {
            confidence = 'Low';
            addReason(reviewReasons, 'unreachable_tool_access');
        }
        if (records.length === 0) {
            confidence = 'Low';
            addReason(reviewReasons, 'missing_tool_reach_evidence');
        }
        if (geometry.accessibilityField && geometry.accessibilityField.degraded === true) {
            confidence = 'Low';
            addReason(reviewReasons, 'tool_access_field_degraded');
        }
        addReason(reviewReasons, 'tool_reach_advisory');

        return {
            records: records,
            residualClusters: residualClusters,
            confidence: confidence,
            reviewReasons: reviewReasons
        };
    }

    root.CncReach = Object.freeze({
        evaluate: evaluate,
        selectSetups: selectSetups,
        validatePhysicalEnvelope: validatePhysicalEnvelope,
        operationName: function (code) { return operationNames[code] || 'CNC machining'; }
    });
}(typeof window !== 'undefined' ? window : self));
