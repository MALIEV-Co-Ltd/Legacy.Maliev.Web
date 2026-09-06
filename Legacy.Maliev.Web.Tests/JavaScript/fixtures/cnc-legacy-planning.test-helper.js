(function (window) {
    'use strict';

    var config = window.CncQuotationConfig;
    if (!config || !config.planning) {
        throw new Error('CNC quotation configuration must load before the manufacturing planner.');
    }

    var planning = config.planning;
    var reachEvaluator = window.CncReach;
    var toolLibrary = window.CncToolLibrary;
    var materialCatalog = window.CncMaterialCatalog;
    var fixtureClearance = window.CncFixtureClearance;
    var machineCapability = window.CncMachineCapability;
    if (!reachEvaluator || !toolLibrary || !materialCatalog || !fixtureClearance || !machineCapability) {
        throw new Error('CNC reach, fixture, capability, tool, and material modules must load before the manufacturing planner.');
    }
    var confidenceRank = Object.freeze({ High: 0, Medium: 1, Low: 2 });
    // One hundred-millionth coverage buckets make the setup sort a transitive total order.
    var coverageOrderingScale = 100000000;
    var canonicalDirectionPriority = Object.freeze({
        'positive-z': 0, 'negative-z': 1,
        'positive-y': 2, 'negative-y': 3,
        'positive-x': 4, 'negative-x': 5
    });

    function isPositiveNumber(value) {
        return typeof value === 'number' && Number.isFinite(value) && value > 0;
    }

    function isNonNegativeNumber(value) {
        return typeof value === 'number' && Number.isFinite(value) && value >= 0;
    }

    function addReason(reasons, code) {
        if (reasons.indexOf(code) === -1) {
            reasons.push(code);
        }
    }

    function lowerConfidence(current, next) {
        return confidenceRank[next] > confidenceRank[current] ? next : current;
    }

    function coverageOrderingBucket(coverage) {
        return Math.min(Number.MAX_SAFE_INTEGER, Math.round(coverage * coverageOrderingScale));
    }

    function stockVolumeMm3(stock) {
        if (stock && isPositiveNumber(stock.stockVolumeMm3)) {
            return stock.stockVolumeMm3;
        }

        var size = stock && stock.stockSizeMm;
        return size && isPositiveNumber(size.x) && isPositiveNumber(size.y) && isPositiveNumber(size.z)
            ? size.x * size.y * size.z
            : 0;
    }

    function incrementalDirectionEvidence(geometry, directionId, selectedDirectionIds) {
        var score = { featureTriangles: 0, totalTriangles: 0 };
        if (!geometry || !Array.isArray(geometry.surfaceClusters) || typeof directionId !== 'string') {
            return score;
        }
        geometry.surfaceClusters.forEach(function (cluster) {
            var byDirection = cluster && cluster.accessibleTriangleIndexesByDirection;
            var candidateIndexes = byDirection && Array.isArray(byDirection[directionId])
                ? byDirection[directionId]
                : [];
            if (candidateIndexes.length === 0) { return; }
            var alreadyReachable = new Set();
            selectedDirectionIds.forEach(function (selectedId) {
                (Array.isArray(byDirection[selectedId]) ? byDirection[selectedId] : []).forEach(function (index) {
                    alreadyReachable.add(index);
                });
            });
            var incrementalCount = candidateIndexes.filter(function (index) {
                return !alreadyReachable.has(index);
            }).length;
            score.totalTriangles += incrementalCount;
            if (cluster.type === 'cylindrical') { score.featureTriangles += incrementalCount; }
        });
        return score;
    }

    function completeSurfaceAreaForDirection(geometry, directionId) {
        if (!geometry || !Array.isArray(geometry.surfaceClusters) || typeof directionId !== 'string') {
            return 0;
        }

        return geometry.surfaceClusters.reduce(function (total, cluster) {
            var triangleIndexes = cluster && cluster.triangleIndexes;
            var byDirection = cluster && cluster.accessibleTriangleIndexesByDirection;
            var accessible = byDirection && byDirection[directionId];
            return Array.isArray(triangleIndexes) && triangleIndexes.length > 0
                && Array.isArray(accessible) && accessible.length === triangleIndexes.length
                && isPositiveNumber(cluster.areaMm2)
                ? total + cluster.areaMm2 : total;
        }, 0);
    }

    function planSetups(geometry, reviewReasons) {
        var candidates = geometry && Array.isArray(geometry.orientationCandidates)
            ? geometry.orientationCandidates.filter(function (candidate) {
                return candidate && isPositiveNumber(candidate.projectedFaceCoverage);
            })
            : [];
        var requiredDirections = [];
        // Six visible faces do not mean six machining setups. A fully planar prismatic
        // part with no detected holes, pockets, undercuts, or deep features can be faced
        // and contoured from one side, then flipped to finish the held face. Side-face
        // projected area alone is not evidence that a cutter must approach from that side.
        var simpleTopFlipPart = geometry
            && isNonNegativeNumber(geometry.planarAreaRatio)
            && geometry.planarAreaRatio >= 0.98
            && isPositiveNumber(geometry.boxFillRatio)
            && geometry.boxFillRatio >= 0.95
            && geometry.boxFillRatio <= 1.01
            && proxyCount(geometry.holeProxies) === 0
            && proxyCount(geometry.pocketProxies) === 0
            && geometry.undercutRisk !== true
            && geometry.deepFeatureRisk !== true;
        var multiBodyPart = geometry && Number.isInteger(geometry.bodyCount) && geometry.bodyCount > 1;

        if (candidates.length > 0) {
            var pairDirections = {};
            candidates.forEach(function (candidate) {
                var direction = candidate.toolDirection;
                var idMatch = typeof candidate.id === 'string' && /^(positive|negative)-(x|y|z)$/.exec(candidate.id);
                var axis;
                var side;
                if (idMatch) {
                    side = idMatch[1];
                    axis = idMatch[2];
                } else if (direction && Number.isFinite(direction.x) && Number.isFinite(direction.y) && Number.isFinite(direction.z)) {
                    var components = [direction.x, direction.y, direction.z];
                    var axisIndex = components.reduce(function (maximumIndex, component, index) {
                        return Math.abs(component) > Math.abs(components[maximumIndex]) ? index : maximumIndex;
                    }, 0);
                    axis = ['x', 'y', 'z'][axisIndex];
                    side = components[axisIndex] >= 0 ? 'positive' : 'negative';
                } else {
                    return;
                }
                var key = axis + '-' + side;
                if (!pairDirections[key] || candidate.projectedFaceCoverage > pairDirections[key].projectedFaceCoverage) {
                    pairDirections[key] = candidate;
                }
            });

            var directionPriority = {
                'z-positive': 0,
                'z-negative': 1,
                'y-positive': 2,
                'y-negative': 3,
                'x-positive': 4,
                'x-negative': 5
            };
            var rankedDirections = Object.keys(pairDirections).map(function (key) {
                var keyParts = key.split('-');
                return {
                    axis: keyParts[0],
                    side: keyParts[1],
                    key: key,
                    coverageBucket: coverageOrderingBucket(pairDirections[key].projectedFaceCoverage),
                    completeSurfaceAreaMm2: completeSurfaceAreaForDirection(geometry, pairDirections[key].id),
                    candidate: pairDirections[key]
                };
            }).sort(function (left, right) {
                if (left.coverageBucket !== right.coverageBucket) {
                    return left.coverageBucket > right.coverageBucket ? -1 : 1;
                }

                var priorityDifference = directionPriority[left.axis + '-' + left.side] - directionPriority[right.axis + '-' + right.side];
                return priorityDifference !== 0 ? priorityDifference : left.key.localeCompare(right.key);
            });

            var dominant = rankedDirections[0];
            var materialCoverage = Math.max(0.02, dominant.candidate.projectedFaceCoverage * 0.50);
            requiredDirections.push(dominant.candidate);

            var opposite = rankedDirections.find(function (direction) {
                return direction.axis === dominant.axis && direction.side !== dominant.side;
            });
            if (opposite && opposite.candidate.projectedFaceCoverage >= materialCoverage) {
                requiredDirections.push(opposite.candidate);
            }

            if (!simpleTopFlipPart) {
                if (multiBodyPart) {
                    // Disconnected bodies can hide slots or walls that have a small projected
                    // share compared with their broad faces. Rank orthogonal reindexes by the
                    // cylindrical feature triangles they add after the dominant top/flip pair;
                    // projected envelope area alone can otherwise choose an axial view that does
                    // not approach the slot from its side.
                    var selectedDirectionIds = requiredDirections.map(function (direction) { return direction.id; });
                    var bestSide = rankedDirections.filter(function (direction) {
                        return direction.axis !== dominant.axis;
                    }).map(function (direction, index) {
                        return {
                            direction: direction,
                            originalIndex: index,
                            evidence: incrementalDirectionEvidence(geometry, direction.candidate.id, selectedDirectionIds)
                        };
                    }).sort(function (left, right) {
                        if (left.evidence.featureTriangles !== right.evidence.featureTriangles) {
                            return right.evidence.featureTriangles - left.evidence.featureTriangles;
                        }
                        // A rotational slot can expose identical feature evidence around its
                        // radial plane. Prefer the lateral X reindex over an arbitrary top-Z
                        // orientation so the setup represents side milling in the viewer.
                        if (left.evidence.featureTriangles > 0 && right.evidence.featureTriangles > 0) {
                            var leftAxisPriority = left.direction.axis === 'x' ? 0 : 1;
                            var rightAxisPriority = right.direction.axis === 'x' ? 0 : 1;
                            if (leftAxisPriority !== rightAxisPriority) {
                                return leftAxisPriority - rightAxisPriority;
                            }
                        }
                        if (left.evidence.totalTriangles !== right.evidence.totalTriangles) {
                            return right.evidence.totalTriangles - left.evidence.totalTriangles;
                        }
                        return left.originalIndex - right.originalIndex;
                    }).map(function (ranked) { return ranked.direction; })[0];
                    if (bestSide && requiredDirections.length < planning.maxAutomaticSetups) {
                        requiredDirections.push(bestSide.candidate);
                    }
                } else {
                    var orientedSize = geometry && geometry.orientedSizeMm;
                    var envelopeDimensions = orientedSize ? [
                        { axis: 'x', value: orientedSize.x || 0 },
                        { axis: 'y', value: orientedSize.y || 0 },
                        { axis: 'z', value: orientedSize.z || 0 }
                    ].sort(function (left, right) { return right.value - left.value; }) : [];
                    var elongatedAxis = envelopeDimensions.length === 3 && envelopeDimensions[1].value > 0
                        && envelopeDimensions[0].value >= envelopeDimensions[1].value * 1.75
                        ? envelopeDimensions[0].axis : null;
                    rankedDirections.filter(function (direction) {
                        return direction.axis !== dominant.axis && direction.candidate.projectedFaceCoverage >= materialCoverage;
                    }).sort(function (left, right) {
                        if (left.coverageBucket !== right.coverageBucket) {
                            return right.coverageBucket - left.coverageBucket;
                        }
                        if (elongatedAxis && left.axis !== elongatedAxis && right.axis !== elongatedAxis
                            && left.completeSurfaceAreaMm2 !== right.completeSurfaceAreaMm2) {
                            return right.completeSurfaceAreaMm2 - left.completeSurfaceAreaMm2;
                        }
                        return directionPriority[left.axis + '-' + left.side]
                            - directionPriority[right.axis + '-' + right.side];
                    }).some(function (direction) {
                        if (requiredDirections.length >= planning.maxAutomaticSetups + 1) {
                            return true;
                        }

                        requiredDirections.push(direction.candidate);
                        return false;
                    });
                }
            }

            var meaningfulDirectionCount = rankedDirections.filter(function (direction) {
                return direction.candidate.projectedFaceCoverage >= materialCoverage;
            }).length;
            if (!simpleTopFlipPart && meaningfulDirectionCount > planning.maxAutomaticSetups + 1) {
                addReason(reviewReasons, 'uncertain_geometry_directions');
            }
        }

        var fixtureDirections = geometry && Array.isArray(geometry.requiredToolDirections)
            ? geometry.requiredToolDirections.filter(function (direction) { return direction; })
            : [];
        if (requiredDirections.length === 0
            || (fixtureDirections.length > requiredDirections.length && candidates.length < fixtureDirections.length)) {
            requiredDirections = fixtureDirections.length > 0 ? fixtureDirections : requiredDirections;
        }

        if (requiredDirections.length === 0) {
            addReason(reviewReasons, 'uncertain_geometry_directions');
            requiredDirections = [{ id: 'conservative-top-side' }];
        }

        return requiredDirections.map(function (direction, index) {
            return {
                id: direction.id || ('setup-' + (index + 1)),
                sequence: index + 1,
                toolDirection: direction.toolDirection || null
            };
        });
    }

    function cloneDirection(direction) {
        return direction && Number.isFinite(direction.x) && Number.isFinite(direction.y) && Number.isFinite(direction.z)
            ? { x: direction.x, y: direction.y, z: direction.z }
            : null;
    }

    function negateDirection(direction) {
        return direction ? { x: -direction.x, y: -direction.y, z: -direction.z } : null;
    }

    function vectorDot(left, right) {
        return left && right ? (left.x * right.x) + (left.y * right.y) + (left.z * right.z) : 0;
    }

    function normalizedDirection(direction) {
        direction = cloneDirection(direction);
        if (!direction) { return null; }
        var length = Math.sqrt((direction.x * direction.x) + (direction.y * direction.y) + (direction.z * direction.z));
        return length > 0 ? { x: direction.x / length, y: direction.y / length, z: direction.z / length } : null;
    }

    function proxyArray(proxy) {
        return Array.isArray(proxy) ? proxy : [];
    }

    function proxyClusterIds(proxies) {
        return proxyArray(proxies).reduce(function (ids, proxy) {
            (Array.isArray(proxy.surfaceClusterIds) ? proxy.surfaceClusterIds : []).forEach(function (id) {
                if (ids.indexOf(id) === -1) { ids.push(id); }
            });
            return ids;
        }, []);
    }

    function pilotSpotChamfer(geometry, chamfer) {
        return chamfer && Math.abs(chamfer.includedAngleDegrees - 90) <= 0.1
            && chamfer.pilotHoleId && proxyArray(geometry.holeProxies).some(function (hole) {
                return hole.id === chamfer.pilotHoleId;
            });
    }

    function planningContractError(code, message) {
        var error = new Error(code + ': ' + message);
        error.code = code;
        return error;
    }

    function planFromOperationGraph(input) {
        var featureGraph = input.featureGraph;
        var operationGraph = input.operationGraph;
        if (!featureGraph || !operationGraph) {
            throw planningContractError('manufacturing_feature_graph_required',
                'Production CNC planning requires a feature graph and operation graph.');
        }
        var contracts = window.CncPlanContracts;
        if (!contracts) {
            throw planningContractError('manufacturing_feature_graph_required',
                'CNC plan contracts must load before production planning.');
        }
        contracts.validateFeatureGraph(featureGraph);
        contracts.validateOperationGraph(operationGraph, featureGraph);
        if (featureGraph.topologyRevision !== operationGraph.topologyRevision) {
            throw planningContractError('revision_mismatch',
                'Feature and operation graphs must share a topology revision.');
        }
        var requiredUnresolved = (featureGraph.unresolved || []).concat(operationGraph.unresolved || [])
            .filter(function (item) { return !item || item.required !== false; });
        if (requiredUnresolved.length > 0) {
            throw planningContractError('unresolved_required_feature',
                'Production CNC planning cannot assign unresolved required manufacturing evidence.');
        }
        if (!window.CncSetupPlanner) {
            throw planningContractError('setup_planner_required',
                'The deterministic setup planner must load before production planning.');
        }
        var setupPlan = window.CncSetupPlanner.plan({
            topology: input.topology,
            featureGraph: featureGraph,
            operationGraph: operationGraph,
            stock: input.stock,
            fixtureCatalog: input.fixtureCatalog
        });
        return {
            featureGraph: featureGraph,
            operationGraph: operationGraph,
            setupPlan: setupPlan,
            operations: operationGraph.operations.slice(),
            setups: setupPlan.setups.slice(),
            setupCount: setupPlan.setups.length,
            confidence: 'High',
            reviewReasons: [],
            setupPlanningPending: false
        };
    }

    function threadTransitionCandidate(thread, cluster) {
        if (!cluster || cluster.type === 'planar') { return false; }
        if (thread.isInternal === false || /^external-thread/.test(String(thread.id || ''))) {
            return cluster.isInternal !== true && cluster.type === 'freeform'
                && (isNonNegativeNumber(cluster.internalCornerRadiusMm)
                    || Object.keys(cluster.curvedFinishingByDirection || {}).length > 0);
        }
        return cluster.isInternal === true;
    }

    function threadOwnedClusterIds(thread, clusters) {
        var ids = Array.isArray(thread && thread.surfaceClusterIds)
            ? new Set(thread.surfaceClusterIds.map(String)) : new Set();
        var changed = true;
        while (changed) {
            changed = false;
            (clusters || []).forEach(function (cluster) {
                var id = String(cluster && cluster.id);
                if (ids.has(id) || !threadTransitionCandidate(thread, cluster)) { return; }
                if ((cluster.adjacentClusterIds || []).map(String).some(function (adjacentId) {
                    return ids.has(adjacentId);
                })) {
                    ids.add(id);
                    changed = true;
                }
            });
        }
        return ids;
    }

    function threadOwnsCluster(thread, cluster, clusters) {
        return threadOwnedClusterIds(thread, clusters).has(String(cluster && cluster.id));
    }

    function threadOperationCode(thread) {
        var external = thread && (thread.isInternal === false
            || /^external-thread/.test(String(thread.id || '')));
        var nominalDiameterMm = isPositiveNumber(thread && thread.majorDiameterMm)
            ? thread.majorDiameterMm : null;
        if (!external && isPositiveNumber(thread && thread.minorDiameterMm)
            && isPositiveNumber(thread && thread.pitchMm)) {
            // Modeled internal root relief can exceed the nominal major diameter.
            // Minor diameter plus pitch is a stable nominal-size estimate for deciding
            // whether the feature is still within the M12 tapping policy.
            nominalDiameterMm = Math.min(nominalDiameterMm,
                thread.minorDiameterMm + thread.pitchMm);
        }
        var largeInternal = !external && isPositiveNumber(nominalDiameterMm)
            && nominalDiameterMm > 12.05;
        return external || largeInternal || !isPositiveNumber(thread && thread.pitchMm)
            ? 'thread_milling' : 'tapping';
    }

    function nominalMetricThreadDiameter(diameterMm, pitchMm) {
        if (!isPositiveNumber(diameterMm)) { return null; }
        var preferred = [1, 1.2, 1.4, 1.6, 1.8, 2, 2.5, 3, 3.5, 4, 5, 6, 8, 10, 12,
            14, 16, 18, 20, 22, 24, 27, 30, 33, 36, 39, 42, 45, 48, 52, 56, 60, 64];
        var nearest = preferred.reduce(function (best, candidate) {
            return Math.abs(candidate - diameterMm) < Math.abs(best - diameterMm) ? candidate : best;
        }, preferred[0]);
        return Math.abs(nearest - diameterMm) <= Math.max(0.2,
            isPositiveNumber(pitchMm) ? pitchMm * 0.25 : 0.2) ? nearest : diameterMm;
    }

    function threadManufacturingClusterIds(geometry) {
        var threads = proxyArray(geometry && geometry.threadProxies);
        var clusters = Array.isArray(geometry && geometry.surfaceClusters) ? geometry.surfaceClusters : [];
        return new Set(clusters
            .filter(function (cluster) {
                return threads.some(function (thread) { return threadOwnsCluster(thread, cluster, clusters); });
            }).map(function (cluster) { return String(cluster.id); }));
    }

    function clusterFeatureEvidence(geometry, cluster, includeTapping, primaryDirection) {
        var copy = Object.assign({}, cluster);
        var operationCodes = [];
        var clusters = Array.isArray(geometry.surfaceClusters) ? geometry.surfaceClusters : [];
        var matchingThreads = proxyArray(geometry.threadProxies).filter(function (thread) {
            return threadOwnsCluster(thread, cluster, clusters);
        });
        var matchingHoles = proxyArray(geometry.holeProxies).filter(function (hole) {
            return Array.isArray(hole.surfaceClusterIds) && hole.surfaceClusterIds.indexOf(cluster.id) !== -1;
        });
        var matchingChamfers = proxyArray(geometry.chamferProxies).filter(function (feature) {
            return Array.isArray(feature.surfaceClusterIds) && feature.surfaceClusterIds.indexOf(cluster.id) !== -1;
        });
        function addOperation(code) {
            if (operationCodes.indexOf(code) === -1) { operationCodes.push(code); }
        }

        if (matchingChamfers.length > 0) {
            addOperation(pilotSpotChamfer(geometry, matchingChamfers[0]) ? 'spot_drilling' : 'chamfering');
            copy.featureAxis = matchingChamfers[0].axis;
            copy.requiredDepthMm = matchingChamfers[0].depthMm;
            copy.featureChamfer = matchingChamfers[0];
            copy.featureEntryDirections = matchingChamfers[0].entryDirections || [];
        } else if (matchingThreads.length === 0 && matchingHoles.length === 0
            && Array.isArray(cluster.operationCodes)) {
            cluster.operationCodes.forEach(addOperation);
        } else if (matchingThreads.length === 0 && matchingHoles.length === 0
            && typeof cluster.operation === 'string') {
            addOperation(cluster.operation);
        } else if (matchingThreads.length === 0 && matchingHoles.length === 0) {
            if (cluster.type === 'planar') {
                var normal = normalizedDirection(cluster.normal);
                var primaryAlignment = normal && primaryDirection
                    ? Math.abs(vectorDot(normal, primaryDirection)) : 1;
                if (primaryAlignment >= 0.80) {
                    addOperation('facing');
                    addOperation('roughing');
                    addOperation('finishing');
                } else {
                    addOperation('profiling');
                }
            } else {
                addOperation('roughing');
                // An empty fillet scan is not evidence of an extruded contour: a
                // torus or sculpted surface also has no cylindrical concave strips.
                addOperation(cluster.type === 'spherical' || (cluster.type === 'freeform'
                    && !normalizedDirection(cluster.prismaticContourAxis))
                    ? 'freeform_finishing' : 'finishing');
                if (cluster.type === 'conical') { addOperation('chamfering'); }
            }
        }

        if (matchingHoles.length === 0 && matchingThreads.length === 0 && matchingChamfers.length === 0
            && Object.keys(cluster.curvedFinishingByDirection || {}).length > 0) {
            addOperation('freeform_finishing');
        }
        matchingHoles.forEach(function (hole) {
            addOperation('drilling');
            if (!proxyArray(geometry.chamferProxies).some(function (chamfer) {
                return pilotSpotChamfer(geometry, chamfer) && chamfer.pilotHoleId === hole.id;
            })) { addOperation('spot_drilling'); }
            copy.featureHole = hole;
            copy.featureAxis = copy.featureAxis || hole.axis;
            if (Array.isArray(hole.entryDirections)) { copy.featureEntryDirections = hole.entryDirections; }
            copy.openingWidthMm = copy.openingWidthMm || hole.diameterMm;
            copy.requiredDepthMm = copy.requiredDepthMm || hole.depthMm;
        });
        matchingThreads.forEach(function (thread) {
            addOperation(threadOperationCode(thread));
            if (includeTapping) { addOperation('tapping'); }
            copy.featureAxis = copy.featureAxis || thread.axis;
            copy.openingWidthMm = copy.openingWidthMm || thread.majorDiameterMm;
            if (Array.isArray(thread.entryDirections)) { copy.featureEntryDirections = thread.entryDirections; }
            copy.featureThread = thread;
            copy.operationOpeningWidthsMm = Object.assign({}, copy.operationOpeningWidthsMm, {
                tapping: thread.majorDiameterMm, thread_milling: thread.majorDiameterMm
            });
            copy.requiredDepthMm = copy.requiredDepthMm || thread.depthMm || thread.axialDepthMm;
            // Thread engagement can be shorter than the through pilot on the same surface.
            copy.operationDepthsMm = Object.assign({}, copy.operationDepthsMm, {
                thread_milling: thread.depthMm || thread.axialDepthMm, tapping: thread.depthMm || thread.axialDepthMm
            });
        });
        copy.operationCodes = operationCodes;
        return copy;
    }

    function reachGeometry(geometry, includeTapping) {
        var candidates = candidateSetups(geometry);
        var primaryDirection = candidates.length > 0 ? candidates[0].direction : null;
        var clusters = Array.isArray(geometry.surfaceClusters) ? geometry.surfaceClusters : [];
        var expandedThreads = proxyArray(geometry.threadProxies).map(function (thread) {
            return Object.assign({}, thread, {
                surfaceClusterIds: Array.from(threadOwnedClusterIds(thread, clusters))
            });
        });
        var prepared = Object.assign({}, geometry, { threadProxies: expandedThreads });
        return Object.assign({}, prepared, {
            surfaceClusters: geometry.surfaceClusters.map(function (cluster) {
                return clusterFeatureEvidence(prepared, cluster, includeTapping, primaryDirection);
            })
        });
    }

    function candidateSetups(geometry) {
        var candidates = (Array.isArray(geometry.orientationCandidates) ? geometry.orientationCandidates : [])
            .map(function (candidate, index) {
                return {
                    id: candidate.id || ('setup-' + (index + 1)),
                    number: index + 1,
                    direction: normalizedDirection(candidate.toolDirection || candidate.direction),
                    coverage: isPositiveNumber(candidate.projectedFaceCoverage) ? candidate.projectedFaceCoverage : 0,
                    accessibility: candidate.accessibility || 'heuristic'
                };
            }).filter(function (candidate) { return candidate.direction; });

        if (candidates.length === 0) {
            candidates = (Array.isArray(geometry.requiredToolDirections) ? geometry.requiredToolDirections : [])
                .map(function (candidate, index) {
                    return {
                        id: candidate.id || ('setup-' + (index + 1)),
                        number: index + 1,
                        direction: normalizedDirection(candidate.toolDirection || candidate.direction),
                        coverage: 0,
                        accessibility: 'required'
                    };
                }).filter(function (candidate) { return candidate.direction; });
        }

        var directionalOperations = ['drilling', 'spot_drilling', 'reaming', 'tapping', 'thread_milling'];
        var featureAxes = [];
        function addFeatureAxis(axis) {
            axis = normalizedDirection(axis);
            if (!axis || featureAxes.some(function (entry) {
                return Math.abs(vectorDot(entry, axis)) >= 0.995;
            })) { return; }
            featureAxes.push(axis);
        }
        proxyArray(geometry.holeProxies).forEach(function (proxy) { addFeatureAxis(proxy.axis); });
        proxyArray(geometry.threadProxies).forEach(function (proxy) { addFeatureAxis(proxy.axis); });
        (Array.isArray(geometry.surfaceClusters) ? geometry.surfaceClusters : []).forEach(function (cluster) {
            var operations = Array.isArray(cluster.operationCodes) ? cluster.operationCodes : [];
            if (cluster.featureAxis && operations.some(function (operation) {
                return directionalOperations.indexOf(operation) >= 0;
            })) {
                addFeatureAxis(cluster.featureAxis);
            }
        });
        featureAxes.forEach(function (axis, axisIndex) {
            [axis, negateDirection(axis)].forEach(function (direction, signIndex) {
                if (candidates.some(function (candidate) {
                    return vectorDot(candidate.direction, direction) >= 0.995;
                })) { return; }
                candidates.push({
                    id: 'feature-axis-' + (axisIndex + 1) + (signIndex === 0 ? '-positive' : '-negative'),
                    number: candidates.length + 1,
                    direction: direction,
                    coverage: 0,
                    accessibility: 'feature_axis'
                });
            });
        });

        return candidates.sort(function (left, right) {
            var bucketDifference = coverageOrderingBucket(right.coverage) - coverageOrderingBucket(left.coverage);
            if (bucketDifference !== 0) { return bucketDifference; }
            var leftPriority = Object.prototype.hasOwnProperty.call(canonicalDirectionPriority, left.id)
                ? canonicalDirectionPriority[left.id] : Number.MAX_SAFE_INTEGER;
            var rightPriority = Object.prototype.hasOwnProperty.call(canonicalDirectionPriority, right.id)
                ? canonicalDirectionPriority[right.id] : Number.MAX_SAFE_INTEGER;
            if (leftPriority !== rightPriority) { return leftPriority - rightPriority; }
            return left.id.localeCompare(right.id);
        });
    }

    function primaryTopFlipCandidates(candidates) {
        var bestPair = null;
        candidates.forEach(function (primary, index) {
            candidates.slice(index + 1).forEach(function (flip) {
                if (vectorDot(primary.direction, flip.direction) > -0.98) { return; }
                var combinedCoverage = primary.coverage + flip.coverage;
                var signature = primary.id + '\u0000' + flip.id;
                if (!bestPair || combinedCoverage > bestPair.combinedCoverage
                    || combinedCoverage === bestPair.combinedCoverage && signature < bestPair.signature) {
                    bestPair = {
                        candidates: [primary, flip],
                        combinedCoverage: combinedCoverage,
                        signature: signature
                    };
                }
            });
        });
        return bestPair ? bestPair.candidates : null;
    }

    function matingFixtureStrategy(geometry, stock, candidates) {
        var rule = planning.matingFixture;
        if (!rule || !Array.isArray(candidates) || candidates.length < 2) { return null; }
        var size = geometry && geometry.orientedSizeMm;
        var maximumSpan = size && Math.max(size.x || 0, size.y || 0, size.z || 0);
        var heuristicEligible = geometry && geometry.bodyCount === 1
            && geometry.flatPlateEligible !== true
            && isPositiveNumber(geometry.boxFillRatio)
            && geometry.boxFillRatio <= rule.maximumBoxFillRatio
            && isNonNegativeNumber(geometry.planarAreaRatio)
            && geometry.planarAreaRatio >= rule.minimumPlanarAreaRatio
            && maximumSpan >= rule.minimumPartSpanMm;
        if ((stock && (stock.stockShape === 'round' || String(stock.strategy || '').indexOf('round') !== -1))
            || (geometry.customFixtureRisk !== true && !heuristicEligible)) {
            return null;
        }

        var bestPair = null;
        candidates.forEach(function (primary, index) {
            candidates.slice(index + 1).forEach(function (flip) {
                if (vectorDot(primary.direction, flip.direction) > -0.98) { return; }
                var combinedCoverage = primary.coverage + flip.coverage;
                if (!bestPair || combinedCoverage > bestPair.combinedCoverage
                    || combinedCoverage === bestPair.combinedCoverage
                    && (primary.id + flip.id).localeCompare(bestPair.primary.id + bestPair.flip.id) < 0) {
                    bestPair = { primary: primary, flip: flip, combinedCoverage: combinedCoverage };
                }
            });
        });
        if (!bestPair || bestPair.combinedCoverage < rule.minimumOpposedCoverage) { return null; }

        return {
            required: true,
            type: 'machined_mating_fixture',
            candidates: [bestPair.primary, bestPair.flip],
            datumSetupId: bestPair.primary.id,
            flipSetupId: bestPair.flip.id,
            combinedProjectedCoverage: bestPair.combinedCoverage,
            originalDirectionCount: candidates.length
        };
    }

    function fixtureNormalAxis(strategy) {
        var direction = strategy && strategy.candidates && strategy.candidates[0]
            && strategy.candidates[0].direction || {};
        var components = [
            { axis: 'x', magnitude: Math.abs(direction.x || 0) },
            { axis: 'y', magnitude: Math.abs(direction.y || 0) },
            { axis: 'z', magnitude: Math.abs(direction.z || 0) }
        ];
        components.sort(function (left, right) { return right.magnitude - left.magnitude; });
        return components[0].axis;
    }

    function roundFixtureValue(value, increment) {
        return Math.ceil((value - Number.EPSILON) / increment) * increment;
    }

    function estimateMatingFixture(geometry, strategy) {
        if (!strategy || strategy.required !== true) { return null; }
        var rule = planning.matingFixture;
        var size = geometry && geometry.orientedSizeMm || {};
        var normalAxis = fixtureNormalAxis(strategy);
        var footprint = ['x', 'y', 'z'].filter(function (axis) { return axis !== normalAxis; })
            .map(function (axis) { return size[axis] || 0; })
            .sort(function (left, right) { return right - left; });
        var estimatedBlankSizeMm = {
            x: roundFixtureValue(footprint[0] + (rule.planarMarginMm * 2), rule.stockIncrementMm),
            y: roundFixtureValue(footprint[1] + (rule.planarMarginMm * 2), rule.stockIncrementMm),
            z: rule.blankThicknessMm
        };
        var clearanceBlank = strategy.clearance && strategy.clearance.stock
            && strategy.clearance.stock.sizeMm;
        var blankSizeMm = clearanceBlank ? {
            x: clearanceBlank.x,
            y: clearanceBlank.y,
            z: clearanceBlank.z
        } : estimatedBlankSizeMm;
        var stockVolumeMm3 = blankSizeMm.x * blankSizeMm.y * blankSizeMm.z;
        var fixtureMaterial = materialCatalog.get(rule.materialCode);
        var stockMassKg = stockVolumeMm3 * fixtureMaterial.densityKgPerMm3;
        var stockPrice = materialCatalog.estimateStockBeforeVat(rule.materialCode, stockMassKg, rule.stockForm);
        var bufferedStockBeforeVat = stockPrice.priceBeforeVat * config.procurementBuffer;
        var landedStockBeforeVat = roundFixtureValue(
            bufferedStockBeforeVat + config.supplierShippingBeforeVat,
            rule.roundToThb);

        var normalSpanMm = size[normalAxis] || 0;
        var matingDepthMm = Math.min(rule.maximumMatingDepthMm,
            Math.max(rule.minimumMatingDepthMm, normalSpanMm * rule.matingDepthRatio));
        var footprintFactor = Math.min(rule.maximumMatingFootprintFactor,
            Math.max(rule.minimumMatingFootprintFactor, Math.sqrt(Math.max(0, geometry.boxFillRatio || 0))));
        var projectedPartAreaMm2 = footprint[0] * footprint[1];
        var matingAreaMm2 = projectedPartAreaMm2 * footprintFactor;
        var facingRemovalVolumeMm3 = blankSizeMm.x * blankSizeMm.y * rule.facingStockMm;
        var matingRemovalVolumeMm3 = matingAreaMm2 * matingDepthMm;
        var removalVolumeMm3 = Math.min(stockVolumeMm3 * 0.60,
            facingRemovalVolumeMm3 + matingRemovalVolumeMm3);
        var matingWallAreaMm2 = 2 * (footprint[0] + footprint[1]) * matingDepthMm * footprintFactor;
        var finishingAreaMm2 = (blankSizeMm.x * blankSizeMm.y) + matingAreaMm2 + matingWallAreaMm2;
        var materialMrr = materialCatalog.adjustRate(
            rule.materialCode, planning.mrrMm3PerMinute['6061'], 'mrr');
        var materialFinishRate = materialCatalog.adjustRate(
            rule.materialCode, planning.finishMm2PerMinute['6061'], 'finish');
        var roughingMinutes = removalVolumeMm3 / materialMrr;
        var finishingMinutes = finishingAreaMm2 / materialFinishRate;
        var cuttingMinutes = (roughingMinutes + finishingMinutes) / planning.utilizationFactor;
        var machiningMinutes = cuttingMinutes
            + (rule.mountingHoleCount * rule.mountingHoleMinutes)
            + (rule.toolFamilyCount * planning.toolChangeMinutes)
            + planning.handlingMinutesPerPart;
        var threeAxisPolicy = config.commercial.machineClasses.threeAxis;
        var customerMinuteRate = threeAxisPolicy.machineMinuteCustomerRateBeforeVat;
        var setupBeforeVat = rule.setupCount * threeAxisPolicy.setupAllowanceBeforeVat;
        var designAndCamBeforeVat = rule.designAndCamMinutes * customerMinuteRate;
        var machiningBeforeVat = machiningMinutes * customerMinuteRate;
        var proveOutBeforeVat = rule.proveOutMinutes * customerMinuteRate;
        var toolingBeforeVat = materialCatalog.adjustRate(
            rule.materialCode, planning.standardToolingAllowanceBeforeVat, 'toolingWear');
        var subtotalBeforeVat = landedStockBeforeVat + setupBeforeVat + designAndCamBeforeVat
            + machiningBeforeVat + proveOutBeforeVat + toolingBeforeVat + rule.hardwareBeforeVat;
        var contingencyBeforeVat = subtotalBeforeVat * rule.contingencyRate;
        var modeledBeforeVat = subtotalBeforeVat * (1 + rule.contingencyRate);
        var amountBeforeVat = Math.max(planning.customFixtureAllowanceBeforeVat,
            Math.ceil(modeledBeforeVat / rule.roundToThb) * rule.roundToThb);
        return {
            calculationMode: 'bounded_fixture_workpiece',
            recursivePlanning: false,
            chargedPer: 'batch',
            stock: {
                materialCode: rule.materialCode,
                form: rule.stockForm,
                sizeMm: blankSizeMm,
                volumeMm3: stockVolumeMm3,
                massKg: stockMassKg,
                expectedBeforeVat: stockPrice.priceBeforeVat,
                procurementBufferBeforeVat: bufferedStockBeforeVat - stockPrice.priceBeforeVat,
                supplierShippingBeforeVat: config.supplierShippingBeforeVat,
                landedBeforeVat: landedStockBeforeVat,
                confidence: stockPrice.confidence,
                supplierReviewRequired: stockPrice.supplierReviewRequired
            },
            setupCount: rule.setupCount,
            setupBeforeVat: setupBeforeVat,
            designAndCamMinutes: rule.designAndCamMinutes,
            designAndCamBeforeVat: designAndCamBeforeVat,
            removalVolumeMm3: removalVolumeMm3,
            finishingAreaMm2: finishingAreaMm2,
            roughingMinutes: roughingMinutes,
            finishingMinutes: finishingMinutes,
            machiningMinutes: machiningMinutes,
            machiningBeforeVat: machiningBeforeVat,
            proveOutMinutes: rule.proveOutMinutes,
            proveOutBeforeVat: proveOutBeforeVat,
            toolingBeforeVat: toolingBeforeVat,
            hardwareBeforeVat: rule.hardwareBeforeVat,
            subtotalBeforeVat: subtotalBeforeVat,
            contingencyRate: rule.contingencyRate,
            contingencyBeforeVat: contingencyBeforeVat,
            amountBeforeVat: amountBeforeVat
        };
    }

    function oppositeCandidate(primary, candidates) {
        var opposite = candidates.find(function (candidate) {
            return vectorDot(candidate.direction, primary.direction) <= -0.98;
        });
        return opposite || {
            id: primary.id + '-flip',
            number: candidates.length + 1,
            direction: negateDirection(primary.direction),
            coverage: primary.coverage,
            accessibility: 'opposite_axial_seed'
        };
    }

    function axialCandidates(geometry, candidates) {
        var rotational = geometry.rotationalEvidence;
        if (geometry.bodyCount > 1
            || !rotational || rotational.eligible !== true || !rotational.axis || candidates.length === 0) {
            return null;
        }
        var axis = normalizedDirection(rotational.axis);
        var aligned = candidates.filter(function (candidate) {
            return Math.abs(vectorDot(candidate.direction, axis)) >= 0.80;
        });
        if (aligned.length === 0) {
            aligned.push({ id: 'axial-top', number: 1, direction: axis, coverage: 1, accessibility: 'rotational_axis' });
        }
        var primary = aligned[0];
        return [primary, oppositeCandidate(primary, aligned)];
    }

    function simpleTopFlipCandidates(geometry, candidates) {
        var simple = isNonNegativeNumber(geometry.planarAreaRatio)
            && geometry.planarAreaRatio >= 0.98
            && isPositiveNumber(geometry.boxFillRatio)
            && geometry.boxFillRatio >= 0.95
            && geometry.boxFillRatio <= 1.01
            && proxyCount(geometry.holeProxies) === 0
            && proxyCount(geometry.pocketProxies) === 0
            && geometry.undercutRisk !== true
            && geometry.deepFeatureRisk !== true;
        if (!simple || candidates.length === 0) { return null; }
        return [candidates[0], oppositeCandidate(candidates[0], candidates)];
    }

    function ignoredRotationalClusterIds(geometry) {
        var rotational = geometry.rotationalEvidence;
        if (!rotational || rotational.eligible !== true || !rotational.axis) { return []; }
        var featureIds = proxyClusterIds(geometry.holeProxies).concat(proxyClusterIds(geometry.threadProxies));
        var axis = normalizedDirection(rotational.axis);
        return geometry.surfaceClusters.filter(function (cluster) {
            var clusterAxis = normalizedDirection(cluster.axis);
            return (cluster.type === 'cylindrical' || cluster.type === 'conical')
                && clusterAxis && Math.abs(vectorDot(clusterAxis, axis)) >= 0.98
                && featureIds.indexOf(cluster.id) === -1;
        }).map(function (cluster) { return cluster.id; });
    }

    // Legacy mesh-estimation path only. Versioned production graphs bypass this function and
    // delegate setup ownership exclusively to CncSetupPlanner.
    function weightedReachSetups(geometry, stock, material, includeTapping, reviewReasons) {
        var preparedGeometry = reachGeometry(geometry, includeTapping);
        var candidates = candidateSetups(geometry);
        var fixtureCandidate = matingFixtureStrategy(geometry, stock, candidates);
        var fixtureStrategy = null;
        var axial = fixtureCandidate ? null : axialCandidates(geometry, candidates);
        var simpleTopFlip = fixtureCandidate ? null : simpleTopFlipCandidates(geometry, candidates);
        var primaryTopFlip = fixtureCandidate ? null : (axial || simpleTopFlip || primaryTopFlipCandidates(candidates));
        var legacySeedIds = [];
        if (axial || simpleTopFlip) {
            var seededPair = axial || simpleTopFlip;
            candidates = seededPair.concat(candidates.filter(function (candidate) {
                return seededPair.every(function (seed) { return seed.id !== candidate.id; });
            }));
        }
        else {
            // A conventional three-axis plan starts with the broad datum face and its opposed
            // flip. Orthogonal or angled candidates are considered only for work that this pair
            // cannot perform; projected side area is never itself a setup requirement.
            legacySeedIds = primaryTopFlip
                ? primaryTopFlip.map(function (candidate) { return candidate.id; })
                : planSetups(geometry, reviewReasons).map(function (setup) { return setup.id; });
        }

        if (candidates.length === 0) {
            addReason(reviewReasons, 'uncertain_geometry_directions');
            candidates = [{ id: 'conservative-top', number: 1, direction: { x: 0, y: 0, z: 1 }, coverage: 0, accessibility: 'fallback' }];
        }

        var allReach = reachEvaluator.evaluate({
            geometry: preparedGeometry,
            stock: stock,
            material: material,
            setups: candidates
        });
        var ignoredIds = ignoredRotationalClusterIds(geometry);
        var requiredClusters = preparedGeometry.surfaceClusters.filter(function (cluster) {
            return ignoredIds.indexOf(cluster.id) === -1;
        });
        var requiredClusterById = Object.create(null);
        requiredClusters.forEach(function (cluster) { requiredClusterById[String(cluster.id)] = cluster; });
        var requiredClusterAreaMm2 = preparedGeometry.surfaceClusters.reduce(function (total, cluster) {
            return total + (isPositiveNumber(cluster.areaMm2) ? cluster.areaMm2 : 0);
        }, 0);
        var accessibilityField = preparedGeometry.accessibilityField;
        var authoritativeSamples = accessibilityField && accessibilityField.degraded !== true
            && Array.isArray(accessibilityField.surfaceSamples)
            ? accessibilityField.surfaceSamples : [];
        var samplesByCluster = Object.create(null);
        authoritativeSamples.forEach(function (sample) {
            if (sample.clusterId === null || sample.clusterId === undefined) { return; }
            var clusterId = String(sample.clusterId);
            if (!samplesByCluster[clusterId]) { samplesByCluster[clusterId] = []; }
            samplesByCluster[clusterId].push(sample);
        });

        var primaryProfileSetupId = primaryTopFlip && primaryTopFlip.length > 0
            ? primaryTopFlip[0].id : null;
        var primaryProfileDirection = primaryTopFlip && primaryTopFlip.length > 0
            ? primaryTopFlip[0].direction : null;
        var primaryProfileEligible = geometry.bodyCount === 1
            && geometry.undercutRisk !== true
            && isNonNegativeNumber(geometry.topBottomPlanarCoverage)
            && geometry.topBottomPlanarCoverage >= 0.60;
        var directionalOperations = ['drilling', 'spot_drilling', 'reaming', 'tapping', 'thread_milling'];
        allReach.records.forEach(function (record) {
            var cluster = requiredClusterById[String(record.clusterId)];
            if (!cluster) { return; }
            var clusterSamples = samplesByCluster[String(cluster.id)] || [];
            var clusterNormal = normalizedDirection(cluster.normal);
            var featureAxis = normalizedDirection(cluster.featureAxis || cluster.axis);
            var primaryExteriorProfile = primaryProfileEligible
                && record.setupId === primaryProfileSetupId
                && record.operationCode === 'profiling'
                && cluster.type === 'planar'
                && !cluster.featureAxis
                && clusterNormal && primaryProfileDirection
                && Math.abs(vectorDot(clusterNormal, primaryProfileDirection)) <= 0.20
                && (record.reachable || record.limitingFactor === 'body_occlusion');
            var exactFeatureAxis = featureAxis
                && directionalOperations.indexOf(record.operationCode) >= 0
                && candidates.some(function (candidate) {
                    return candidate.id === record.setupId
                        && Math.abs(vectorDot(candidate.direction, featureAxis)) >= 0.995;
                })
                && record.reachable;
            if (!primaryExteriorProfile && !exactFeatureAxis) { return; }
            record.reachable = true;
            record.limitingFactor = null;
            record.accessEvidence = primaryExteriorProfile ? 'primary_profile' : 'feature_axis';
            if (clusterSamples.length > 0) {
                record.fieldSampleCount = clusterSamples.length;
                record.reachableSampleCount = clusterSamples.length;
                record.reachableSampleIds = clusterSamples.map(function (sample) { return sample.id; });
                record.reachableAreaMm2 = clusterSamples.reduce(function (total, sample) {
                    return total + (isPositiveNumber(sample.areaMm2) ? sample.areaMm2 : 0);
                }, 0);
                if (primaryExteriorProfile) { record.fluteSampleCount = clusterSamples.length; }
                else { record.tipSampleCount = clusterSamples.length; }
            }
        });
        var requiredWork = requiredClusters.reduce(function (items, cluster) {
            var operationCodes = Array.isArray(cluster.operationCodes) && cluster.operationCodes.length > 0
                ? cluster.operationCodes : ['finishing'];
            var clusterSamples = samplesByCluster[String(cluster.id)] || [];
            var areaShare = isPositiveNumber(cluster.areaMm2) ? cluster.areaMm2 / operationCodes.length : 0;
            operationCodes.forEach(function (operationCode) {
                if (clusterSamples.length > 0) {
                    clusterSamples.forEach(function (sample) {
                        items.push({
                            key: 'sample\u0000' + sample.id + '\u0000' + operationCode,
                            clusterId: cluster.id,
                            sampleId: sample.id,
                            operationCode: operationCode,
                            areaMm2: isPositiveNumber(sample.areaMm2) ? sample.areaMm2 / operationCodes.length : 0
                        });
                    });
                } else {
                    items.push({
                        key: 'cluster\u0000' + cluster.id + '\u0000' + operationCode,
                        clusterId: cluster.id,
                        sampleId: null,
                        operationCode: operationCode,
                        areaMm2: areaShare
                    });
                }
            });
            return items;
        }, []);
        var uncovered = new Set(requiredWork.map(function (work) { return work.key; }));
        var selected = [];
        var hasAnyReachEvidence = allReach.records.some(function (record) { return record.reachable; });
        var workByClusterOperation = Object.create(null);
        var reachableWorkCache = Object.create(null);
        var fieldReachableSetCache = Object.create(null);
        requiredWork.forEach(function (work) {
            var indexKey = work.clusterId + '\u0000' + work.operationCode;
            if (!workByClusterOperation[indexKey]) { workByClusterOperation[indexKey] = []; }
            workByClusterOperation[indexKey].push(work);
        });

        function fieldAccess(record) {
            if (!accessibilityField || record.accessEvidence !== 'field' || !record.fieldDirectionId
                || !accessibilityField.toolAccess || !accessibilityField.toolAccess[record.fieldDirectionId]) {
                return null;
            }
            return accessibilityField.toolAccess[record.fieldDirectionId]
                [record.analysisProfileId || record.toolId] || null;
        }

        function fieldReachableSamples(record) {
            if (Array.isArray(record.reachableSampleIds) && record.reachableSampleIds.length > 0) {
                return new Set(record.reachableSampleIds);
            }
            var access = fieldAccess(record);
            if (!access) { return null; }
            var key = record.fieldDirectionId + '\u0000' + (record.analysisProfileId || record.toolId);
            if (!fieldReachableSetCache[key]) {
                fieldReachableSetCache[key] = new Set(access.reachableSampleIds || []);
            }
            return fieldReachableSetCache[key];
        }

        function allReachableWorkKeys(candidate) {
            if (reachableWorkCache[candidate.id]) { return reachableWorkCache[candidate.id]; }
            var keys = new Set();
            allReach.records.filter(function (record) {
                return record.setupId === candidate.id && record.reachable && ignoredIds.indexOf(record.clusterId) === -1;
            }).forEach(function (record) {
                var reachableSamples = fieldReachableSamples(record);
                var indexedWork = workByClusterOperation[record.clusterId + '\u0000' + record.operationCode] || [];
                indexedWork.forEach(function (work) {
                    if (work.sampleId === null || (reachableSamples && reachableSamples.has(work.sampleId))) {
                        keys.add(work.key);
                    }
                });
            });
            reachableWorkCache[candidate.id] = Array.from(keys);
            return reachableWorkCache[candidate.id];
        }

        function reachableWorkKeys(candidate) {
            return allReachableWorkKeys(candidate).filter(function (key) { return uncovered.has(key); });
        }

        function select(candidate, retainProjectionEvidence) {
            var covered = reachableWorkKeys(candidate);
            if (selected.some(function (entry) { return entry.id === candidate.id; })
                || (hasAnyReachEvidence && covered.length === 0 && retainProjectionEvidence !== true)) { return; }
            selected.push(candidate);
            covered.forEach(function (key) { uncovered.delete(key); });
        }

        if (axial || simpleTopFlip) {
            candidates.slice(0, 2).forEach(select);
        } else if (legacySeedIds.length > 0) {
            legacySeedIds.forEach(function (id) {
                var seed = candidates.find(function (candidate) { return candidate.id === id; });
                if (seed) {
                    // A disconnected multi-body upload is still one workpiece. Preserve the
                    // dominant face, its flip, and the selected side reindex even when coarse
                    // cluster evidence makes an earlier direction appear to cover the same work.
                    // Per-triangle accessibility limits the highlighted/reachable faces later.
                    select(seed, geometry.bodyCount > 1 || legacySeedIds.length >= planning.maxAutomaticSetups + 1);
                }
            });
        }

        // A fixture-qualified top/flip strategy is deliberately bounded by its verified
        // clearance model. Every ordinary workpiece remains feature-driven until all modeled
        // work is covered, including multi-body and non-principal-axis features.
        var boundedWorkholdingStrategyComplete = Boolean(fixtureStrategy && selected.length >= 2);
        while (!boundedWorkholdingStrategyComplete && uncovered.size > 0) {
            var ranked = candidates.filter(function (candidate) {
                return !selected.some(function (entry) { return entry.id === candidate.id; });
            }).map(function (candidate) {
                var keys = reachableWorkKeys(candidate);
                var keySet = new Set(keys);
                var area = requiredWork.reduce(function (total, work) {
                    return total + (keySet.has(work.key) ? work.areaMm2 : 0);
                }, 0);
                var workholdingPenalty = stock && stock.strategy === 'round_bar' ? 8 : 0;
                var lowConfidencePenalty = candidate.accessibility === 'heuristic' ? 5 : 0;
                var triangleEvidence = incrementalDirectionEvidence(
                    preparedGeometry,
                    candidate.id,
                    selected.map(function (entry) { return entry.id; }));
                return {
                    candidate: candidate,
                    keys: keys,
                    uniqueAreaMm2: area,
                    featureTriangles: triangleEvidence.featureTriangles,
                    totalTriangles: triangleEvidence.totalTriangles,
                    score: area - planning.setupMinutes - workholdingPenalty - lowConfidencePenalty
                };
            }).filter(function (entry) { return entry.keys.length > 0; }).sort(function (left, right) {
                if (left.score !== right.score) { return right.score - left.score; }
                if (left.featureTriangles !== right.featureTriangles) {
                    return right.featureTriangles - left.featureTriangles;
                }
                if (left.totalTriangles !== right.totalTriangles) {
                    return right.totalTriangles - left.totalTriangles;
                }
                var leftPriority = Object.prototype.hasOwnProperty.call(canonicalDirectionPriority, left.candidate.id)
                    ? canonicalDirectionPriority[left.candidate.id] : Number.MAX_SAFE_INTEGER;
                var rightPriority = Object.prototype.hasOwnProperty.call(canonicalDirectionPriority, right.candidate.id)
                    ? canonicalDirectionPriority[right.candidate.id] : Number.MAX_SAFE_INTEGER;
                if (leftPriority !== rightPriority) { return leftPriority - rightPriority; }
                return left.candidate.id.localeCompare(right.candidate.id);
            });
            if (ranked.length === 0) { break; }
            // Apply the existing field-residue tolerance before buying another setup,
            // not only after exhausting all six directions. A whole small feature or
            // an axial hole is never residue: the margin must be small within EACH
            // affected cluster as well as the complete workpiece.
            if (selected.length >= 2 && authoritativeSamples.length > 0) {
                var fitsResidueBudget = function (marginalKeys) {
                    var marginalByCluster = Object.create(null);
                    var onlySurfaceSamples = true;
                    requiredWork.forEach(function (work) {
                        if (!marginalKeys.has(work.key)) { return; }
                        if (work.sampleId === null || directionalOperations.indexOf(work.operationCode) >= 0) {
                            onlySurfaceSamples = false;
                        }
                        if (!marginalByCluster[work.clusterId]) { marginalByCluster[work.clusterId] = new Set(); }
                        marginalByCluster[work.clusterId].add(work.sampleId);
                    });
                    var marginalArea = 0;
                    var totalSampleArea = authoritativeSamples.reduce(function (sum, sample) { return sum + (sample.areaMm2 || 0); }, 0);
                    var smallWithinEveryCluster = Object.keys(marginalByCluster).every(function (id) {
                        var samples = samplesByCluster[id] || [];
                        var total = 0, unique = 0;
                        samples.forEach(function (sample) {
                            total += sample.areaMm2 || 0;
                            if (marginalByCluster[id].has(sample.id)) { unique += sample.areaMm2 || 0; }
                        });
                        marginalArea += unique;
                        return total > 0 && unique / total <= 0.015;
                    });
                    return onlySurfaceSamples && smallWithinEveryCluster && totalSampleArea > 0
                        && marginalArea / totalSampleArea <= 0.015;
                };
                var discardable = ranked.filter(function (entry) {
                    return fitsResidueBudget(new Set(entry.keys));
                });
                var discardedKeys = new Set();
                discardable.forEach(function (entry) {
                    entry.keys.forEach(function (key) { discardedKeys.add(key); });
                });
                // Several individually small directions may own different strips.
                // Spend the residue allowance once on their union, counting a shared
                // sample only once. Otherwise keep selecting useful directions until
                // the remaining union fits both the cluster and workpiece budgets.
                if (fitsResidueBudget(discardedKeys)) {
                    var discardedIds = new Set(discardable.map(function (entry) { return entry.candidate.id; }));
                    ranked = ranked.filter(function (entry) { return !discardedIds.has(entry.candidate.id); });
                }
                if (ranked.length === 0) { break; }
            }
            // After the dominant face, flip, and two lateral orientations, an extra setup must
            // own meaningful unique surface work. Sub-one-percent field fragments are normally
            // projection/tessellation residue at a curved profile seam; retain them for DFM
            // review instead of inventing axial workholding setups with no machining operation.
            var rankedKeySet = new Set(ranked[0].keys);
            var uniqueClusterIds = requiredWork.filter(function (work) {
                return rankedKeySet.has(work.key);
            }).map(function (work) {
                return String(work.clusterId);
            }).filter(function (clusterId, index, ids) {
                return ids.indexOf(clusterId) === index;
            });
            var uniqueClusterAreaMm2 = uniqueClusterIds.reduce(function (total, clusterId) {
                var cluster = requiredClusterById[clusterId];
                return total + (cluster && isPositiveNumber(cluster.areaMm2) ? cluster.areaMm2 : 0);
            }, 0);
            var orientedSize = geometry && geometry.orientedSizeMm;
            var rankedDimensions = orientedSize ? [
                { axis: 'x', value: orientedSize.x || 0 },
                { axis: 'y', value: orientedSize.y || 0 },
                { axis: 'z', value: orientedSize.z || 0 }
            ].sort(function (left, right) { return right.value - left.value; }) : [];
            var candidateAxisMatch = /^(?:positive|negative)-(x|y|z)$/.exec(ranked[0].candidate.id || '');
            var elongatedEnvelopeAxis = rankedDimensions.length === 3 && rankedDimensions[1].value > 0
                && rankedDimensions[0].value >= rankedDimensions[1].value * 1.75
                ? rankedDimensions[0].axis : null;
            var candidateTargetsElongatedEnvelope = candidateAxisMatch && elongatedEnvelopeAxis
                && candidateAxisMatch[1] === elongatedEnvelopeAxis;
            var candidateHasAxialFeature = preparedGeometry.surfaceClusters.some(function (cluster) {
                return cluster.featureAxis
                    && Math.abs(vectorDot(normalizedDirection(cluster.featureAxis), ranked[0].candidate.direction)) >= 0.90;
            });
            // On an elongated bent handle, the two end-on projections often report residual
            // samples from the continuous swept skin after the primary/flip and transverse side
            // setups are already selected. Without a detected hole/thread/feature on that axis,
            // those samples do not justify two additional end-held workholding orientations.
            if (selected.length >= planning.maxAutomaticSetups + 1 && geometry.bodyCount === 1
                && candidateTargetsElongatedEnvelope && !candidateHasAxialFeature) {
                break;
            }
            if (selected.length >= planning.maxAutomaticSetups + 1
                && requiredClusterAreaMm2 > 0
                && !candidateHasAxialFeature
                && uniqueClusterAreaMm2 < requiredClusterAreaMm2 * 0.01) {
                break;
            }
            select(ranked[0].candidate);
        }

        if (fixtureCandidate) {
            var fixtureCandidateIds = fixtureCandidate.candidates.map(function (candidate) { return candidate.id; });
            var fixtureCoveredKeys = new Set();
            fixtureCandidate.candidates.forEach(function (candidate) {
                allReachableWorkKeys(candidate).forEach(function (key) { fixtureCoveredKeys.add(key); });
            });
            var fixtureRecords = allReach.records.filter(function (record) {
                return fixtureCandidateIds.indexOf(record.setupId) >= 0;
            });
            // CAD field sampling can leave a narrow seam between otherwise continuous planar
            // facing regions. Preserve exact sample evidence first, then cluster-operation
            // continuity. Only a bounded facing-only seam may use the fixture pair's aggregate
            // facing evidence; every other operation still requires exact or same-cluster reach.
            function fixtureClusterCovers(work) {
                return fixtureCoveredKeys.has(work.key) || fixtureRecords.some(function (record) {
                    return record.reachable === true && record.clusterId === work.clusterId
                        && record.operationCode === work.operationCode;
                });
            }
            var facingWork = requiredWork.filter(function (work) { return work.operationCode === 'facing'; });
            var facingClusterMisses = facingWork.filter(function (work) { return !fixtureClusterCovers(work); });
            var facingFieldGapRatio = facingWork.length > 0
                ? facingClusterMisses.length / facingWork.length : 0;
            var boundedFacingGap = facingClusterMisses.length > 0
                && facingFieldGapRatio <= planning.matingFixture.maximumFacingFieldGapRatio
                && fixtureRecords.some(function (record) {
                    return record.reachable === true && record.operationCode === 'facing';
                });
            function fixtureSemanticallyCovers(work) {
                return fixtureClusterCovers(work)
                    || (work.operationCode === 'facing' && boundedFacingGap);
            }
            var fixtureFullyCoversRequiredWork = requiredWork.length > 0 && requiredWork.every(function (work) {
                return fixtureSemanticallyCovers(work);
            });
            var fixtureClearanceWork = requiredWork.filter(function (work) {
                return fixtureCoveredKeys.has(work.key);
            });
            var clearance = fixtureFullyCoversRequiredWork ? fixtureClearance.evaluate({
                geometry: preparedGeometry,
                stock: stock,
                strategy: fixtureCandidate,
                reachRecords: fixtureRecords,
                requiredWork: fixtureClearanceWork,
                tools: toolLibrary.planningTools(),
                rule: planning.matingFixture
            }) : null;
            var fixtureImprovesPlan = clearance && clearance.feasible === true
                && (fixtureCandidate.candidates.length <= selected.length || uncovered.size > 0);
            if (fixtureImprovesPlan) {
                fixtureStrategy = Object.assign({}, fixtureCandidate, { clearance: clearance });
                selected = fixtureCandidate.candidates.slice();
                uncovered = new Set(requiredWork.map(function (work) { return work.key; }));
                requiredWork.forEach(function (work) {
                    if (fixtureSemanticallyCovers(work)) { uncovered.delete(work.key); }
                });
            }
        }

        var rawUnmachinableSampleIds = requiredWork.filter(function (work) {
            return work.sampleId !== null && uncovered.has(work.key);
        }).map(function (work) { return work.sampleId; }).filter(function (id, index, ids) {
            return ids.indexOf(id) === index;
        });
        var unmachinableSampleSet = new Set(rawUnmachinableSampleIds);
        var totalFieldAreaMm2 = authoritativeSamples.reduce(function (total, sample) {
            return total + (isPositiveNumber(sample.areaMm2) ? sample.areaMm2 : 0);
        }, 0);
        var unmachinableFieldAreaMm2 = authoritativeSamples.reduce(function (total, sample) {
            return total + (unmachinableSampleSet.has(sample.id) && isPositiveNumber(sample.areaMm2)
                ? sample.areaMm2 : 0);
        }, 0);
        var hasUncoveredClusterWork = requiredWork.some(function (work) {
            return work.sampleId === null && uncovered.has(work.key);
        });
        // Voxel boundary normals at sharp edges can leave a very small number of diagonal
        // surfels without a valid tip/flute contact. Treat that discretization residue as
        // covered; meaningful pockets, slots, or whole cluster gaps remain reviewable.
        var insignificantFieldResidue = rawUnmachinableSampleIds.length > 0
            && !hasUncoveredClusterWork && totalFieldAreaMm2 > 0
            && unmachinableFieldAreaMm2 / totalFieldAreaMm2 <= 0.015;
        var unmachinableSampleIds = insignificantFieldResidue ? [] : rawUnmachinableSampleIds;
        var hasSignificantUnmachinableSurface = selected.length === 0
            || hasUncoveredClusterWork || (uncovered.size > 0 && !insignificantFieldResidue);
        if (hasSignificantUnmachinableSurface) { addReason(reviewReasons, 'unreachable_tool_access'); }
        var selectionOrder = {};
        selected.forEach(function (candidate, index) { selectionOrder[candidate.id] = index; });
        var setupEnvelopeSize = geometry && geometry.orientedSizeMm;
        var setupEnvelopeDimensions = setupEnvelopeSize ? [
            { axis: 'x', value: setupEnvelopeSize.x || 0 },
            { axis: 'y', value: setupEnvelopeSize.y || 0 },
            { axis: 'z', value: setupEnvelopeSize.z || 0 }
        ].sort(function (left, right) { return right.value - left.value; }) : [];
        var coherentSideAxis = setupEnvelopeDimensions.length === 3 && setupEnvelopeDimensions[1].value > 0
            && setupEnvelopeDimensions[0].value >= setupEnvelopeDimensions[1].value * 1.75
            ? setupEnvelopeDimensions[1].axis : null;
        selected.sort(function (left, right) {
            if (geometry.bodyCount > 1) {
                // Multi-body setup numbering follows the manufacturing decision: dominant
                // access first, its flip second, then the side reindex.
                return selectionOrder[left.id] - selectionOrder[right.id] || left.id.localeCompare(right.id);
            }
            var leftMatch = /^(?:positive|negative)-(x|y|z)$/.exec(left.id || '');
            var rightMatch = /^(?:positive|negative)-(x|y|z)$/.exec(right.id || '');
            if (coherentSideAxis && leftMatch && rightMatch
                && leftMatch[1] === coherentSideAxis && rightMatch[1] === coherentSideAxis) {
                return selectionOrder[left.id] - selectionOrder[right.id] || left.id.localeCompare(right.id);
            }
            var leftPriority = Object.prototype.hasOwnProperty.call(canonicalDirectionPriority, left.id)
                ? canonicalDirectionPriority[left.id] : Number.MAX_SAFE_INTEGER;
            var rightPriority = Object.prototype.hasOwnProperty.call(canonicalDirectionPriority, right.id)
                ? canonicalDirectionPriority[right.id] : Number.MAX_SAFE_INTEGER;
            return leftPriority - rightPriority || selectionOrder[left.id] - selectionOrder[right.id]
                || left.id.localeCompare(right.id);
        });
        var setups = selected.map(function (candidate, index) {
            var reachableKeys = allReachableWorkKeys(candidate);
            return {
                id: candidate.id,
                sequence: index + 1,
                number: index + 1,
                direction: cloneDirection(candidate.direction),
                toolDirection: cloneDirection(candidate.direction),
                workholding: fixtureStrategy
                    ? index === 0 ? 'custom_fixture_datum' : 'custom_fixture_flip'
                    : stock && stock.stockShape === 'round'
                    ? 'collet_or_soft_jaws'
                    : index === 0 ? 'vise_primary' : index === 1 ? 'vise_flip' : 'vise_reindex',
                reachableClusterIds: [],
                coveredSampleIds: requiredWork.filter(function (work) {
                    return work.sampleId !== null && reachableKeys.indexOf(work.key) !== -1;
                }).map(function (work) { return work.sampleId; }).filter(function (id, sampleIndex, ids) {
                    return ids.indexOf(id) === sampleIndex;
                }),
                operationIds: [],
                toolIds: [],
                minutes: 0
            };
        });
        var selectedReach = reachEvaluator.selectSetups(allReach, preparedGeometry, setups);
        var primarySetupIds = (fixtureCandidate && fixtureCandidate.candidates
            ? fixtureCandidate.candidates : primaryTopFlip || []).map(function (candidate) {
            return candidate.id;
        });
        var primaryDirection = primaryTopFlip && primaryTopFlip.length > 0
            ? normalizedDirection(primaryTopFlip[0].direction) : null;
        var envelope = geometry && geometry.orientedSizeMm;
        var envelopeMaximum = envelope
            ? Math.max(envelope.x || 0, envelope.y || 0, envelope.z || 0) : 0;
        var primarySpanMm = primaryDirection && envelope
            ? Math.abs(primaryDirection.x) * (envelope.x || 0)
                + Math.abs(primaryDirection.y) * (envelope.y || 0)
                + Math.abs(primaryDirection.z) * (envelope.z || 0)
            : 0;
        var primaryCoverageMaximum = primaryTopFlip && primaryTopFlip.length === 2
            ? Math.max(primaryTopFlip[0].coverage || 0, primaryTopFlip[1].coverage || 0) : 0;
        var primaryCoverageMinimum = primaryTopFlip && primaryTopFlip.length === 2
            ? Math.min(primaryTopFlip[0].coverage || 0, primaryTopFlip[1].coverage || 0) : 0;
        var primaryPairOnlyEligible = !fixtureCandidate && geometry.bodyCount === 1
            && geometry.undercutRisk !== true
            && isNonNegativeNumber(geometry.boxFillRatio) && geometry.boxFillRatio >= 0.50
            && primarySetupIds.length === 2 && envelopeMaximum > 0
            && primaryCoverageMaximum > 0 && primaryCoverageMinimum / primaryCoverageMaximum >= 0.50
            && primarySpanMm / envelopeMaximum <= 0.12;
        var directionalFeatureSetupIds = selectedReach.records.filter(function (record) {
            var cluster = requiredClusterById[String(record.clusterId)];
            return record.reachable && cluster && normalizedDirection(cluster.featureAxis || cluster.axis)
                && directionalOperations.indexOf(record.operationCode) >= 0;
        }).map(function (record) { return record.setupId; }).filter(function (id, index, ids) {
            return ids.indexOf(id) === index;
        });
        function setupAxisSpanMm(setup) {
            var match = /^(?:positive|negative)-(x|y|z)$/.exec(String(setup.id || ''));
            var size = geometry && geometry.orientedSizeMm;
            return match && size && isPositiveNumber(size[match[1]]) ? size[match[1]] : 0;
        }
        function recommendedSetupToolIds(setup, setupIndex) {
            var scores = Object.create(null);
            selectedReach.records.filter(function (record) {
                return record.setupNumber === setup.number && record.reachable && record.toolId
                    && ['roughing', 'finishing', 'profiling', 'freeform_finishing'].indexOf(record.operationCode) >= 0;
            }).forEach(function (record) {
                var tool = toolLibrary.get(record.toolId);
                if (!tool) { return; }
                if (!scores[tool.id]) {
                    scores[tool.id] = { id: tool.id, areaMm2: 0, timeMultiplier: tool.timeMultiplier || 1 };
                }
                scores[tool.id].areaMm2 += isPositiveNumber(record.reachableAreaMm2)
                    ? record.reachableAreaMm2 : record.reachableSampleCount;
            });
            var ranked = Object.keys(scores).map(function (id) { return scores[id]; });
            var preferCompactDeepReach = false;
            if (geometry.bodyCount > 1 && setupIndex >= 2) {
                var requiredUnderNeckMm = setupAxisSpanMm(setup);
                var deepReach = ranked.filter(function (score) {
                    var cutter = toolLibrary.get(score.id);
                    var underNeck = cutter && (cutter.underNeckLengthMm || cutter.usableCutLengthMm) || 0;
                    return underNeck + 1e-6 >= requiredUnderNeckMm;
                });
                if (deepReach.length > 0) {
                    ranked = deepReach;
                    preferCompactDeepReach = true;
                }
            }
            ranked.sort(function (left, right) {
                if (preferCompactDeepReach) {
                    var leftTool = toolLibrary.get(left.id);
                    var rightTool = toolLibrary.get(right.id);
                    var diameterDifference = (leftTool && leftTool.diameterMm || Number.MAX_VALUE)
                        - (rightTool && rightTool.diameterMm || Number.MAX_VALUE);
                    if (Math.abs(diameterDifference) > 1e-6) { return diameterDifference; }
                }
                var leftScore = left.areaMm2 / left.timeMultiplier;
                var rightScore = right.areaMm2 / right.timeMultiplier;
                return rightScore - leftScore || left.timeMultiplier - right.timeMultiplier
                    || left.id.localeCompare(right.id);
            });
            return ranked.length > 0 ? [ranked[0].id] : [];
        }
        setups.forEach(function (setup, setupIndex) {
            setup.reachableClusterIds = selectedReach.records.filter(function (record) {
                return record.setupNumber === setup.number && record.reachable;
            }).map(function (record) { return record.clusterId; }).filter(function (id, index, ids) {
                return ignoredIds.indexOf(id) === -1 && ids.indexOf(id) === index;
            });
            setup.toolIds = recommendedSetupToolIds(setup, setupIndex);
        });
        selectedReach.reviewReasons.forEach(function (reason) { addReason(reviewReasons, reason); });

        return {
            setups: setups,
            reach: selectedReach,
            ignoredClusterIds: ignoredIds,
            unmachinableSampleIds: unmachinableSampleIds,
            unmachinableFieldAreaMm2: insignificantFieldResidue ? 0 : unmachinableFieldAreaMm2,
            unmachinableFieldAreaRatio: insignificantFieldResidue || totalFieldAreaMm2 <= 0
                ? 0 : unmachinableFieldAreaMm2 / totalFieldAreaMm2,
            hasSignificantUnmachinableSurface: hasSignificantUnmachinableSurface,
            fixtureStrategy: fixtureStrategy,
            primarySetupIds: primarySetupIds,
            primaryPairOnlyEligible: primaryPairOnlyEligible,
            directionalFeatureSetupIds: directionalFeatureSetupIds,
            geometry: preparedGeometry
        };
    }

    function proxyCount(proxy) {
        if (Array.isArray(proxy)) { return proxy.length; }
        return proxy && Number.isInteger(proxy.count) && proxy.count > 0 ? proxy.count : 0;
    }

    function threadSummary(threads) {
        var result = { count: 0, specifications: [], incompleteSpecification: false, malformedCount: false };
        if (!Array.isArray(threads)) {
            return result;
        }

        threads.forEach(function (thread) {
            if (!thread || typeof thread.designation !== 'string' || thread.designation.trim().length === 0) {
                result.incompleteSpecification = true;
                return;
            }
            if (!Number.isSafeInteger(thread.count) || thread.count <= 0 || !Number.isSafeInteger(result.count + thread.count)) {
                result.malformedCount = true;
                return;
            }

            result.count += thread.count;
            var metric = /^\s*M\s*(\d+(?:[.,]\d+)?)\s*(?:[xX×]\s*(\d+(?:[.,]\d+)?))?/i.exec(thread.designation);
            result.specifications.push({
                count: thread.count,
                majorDiameterMm: metric ? Number(metric[1].replace(',', '.')) : null,
                pitchMm: metric && metric[2] ? Number(metric[2].replace(',', '.')) : null
            });
        });
        return result;
    }

    function unmatchedRequestedThreadCount(threads, detectedThreads) {
        if (!threads || !Array.isArray(threads.specifications)) {
            return threads && Number.isSafeInteger(threads.count) && threads.count > 0 ? threads.count : 0;
        }
        var available = proxyArray(detectedThreads).slice();
        return threads.specifications.reduce(function (total, specification) {
            for (var index = 0; index < specification.count; index += 1) {
                var matchIndex = isPositiveNumber(specification.majorDiameterMm) ? available.findIndex(function (detected) {
                    var diameterTolerance = isPositiveNumber(detected.pitchMm) ? detected.pitchMm * 0.25 : 0.05;
                    return isPositiveNumber(detected.majorDiameterMm)
                        && Math.abs(detected.majorDiameterMm - specification.majorDiameterMm) <= diameterTolerance
                        && (!isPositiveNumber(specification.pitchMm)
                            || isPositiveNumber(detected.pitchMm) && Math.abs(detected.pitchMm - specification.pitchMm) <= 0.05);
                }) : -1;
                if (matchIndex >= 0) { available.splice(matchIndex, 1); }
                else { total += 1; }
            }
            return total;
        }, 0);
    }

    function createOperation(code, toolFamily, minutes, options) {
        options = options || {};
        return Object.assign({
            id: 'operation-' + code + (options.idSuffix ? '-' + options.idSuffix : ''),
            code: code,
            name: reachEvaluator.operationName(code),
            toolFamily: toolFamily,
            toolType: toolFamily,
            toolId: null,
            setupNumber: 1,
            clusterIds: [],
            estimatedMinutes: minutes || 0,
            minutes: minutes || 0,
            generatesSetup: false
        }, options);
    }

    function planOperations(geometry, stock, requirements, threads, removalVolume, partSurfaceAreaMm2, setups, primarySetupIds) {
        var operations = [];
        var hasGeometry = geometry && geometry.orientedSizeMm;
        var hasMaterialRemoval = removalVolume > 0;
        var holeCount = proxyCount(geometry && geometry.holeProxies);
        var pocketCount = proxyCount(geometry && geometry.pocketProxies);
        var detectedThreadCount = proxyCount(geometry && geometry.threadProxies);
        var unmatchedThreadCount = unmatchedRequestedThreadCount(threads, geometry && geometry.threadProxies);
        var chamferCount = proxyCount(geometry && geometry.chamferProxies) + proxyCount(requirements.chamfers);
        var engravingCount = proxyCount(geometry && geometry.engravingProxies) + proxyCount(requirements.engravings);

        var dimensions = hasGeometry ? [geometry.orientedSizeMm.x, geometry.orientedSizeMm.y, geometry.orientedSizeMm.z]
            .filter(isPositiveNumber).sort(function (left, right) { return left - right; }) : [];
        var bulkCutterLimitMm = dimensions.length > 0 ? dimensions[0] : null;
        var holeProxies = proxyArray(geometry && geometry.holeProxies);
        var threadProxies = proxyArray(geometry && geometry.threadProxies);
        var holeClusterIds = new Set(proxyClusterIds(holeProxies).map(String));
        var specializedClusterIds = new Set(proxyClusterIds(threadProxies
            .concat(proxyArray(geometry && geometry.chamferProxies))).map(String));
        var surfaceClusters = Array.isArray(geometry && geometry.surfaceClusters) ? geometry.surfaceClusters : [];
        // Modeled threads are commonly tessellated into several helical strips plus a
        // small root/entry transition. The helical detector owns the strips explicitly;
        // keep an immediately adjoining internal curved transition with the same
        // manufacturing feature. Otherwise that sliver becomes an invented R0.5 pocket,
        // which selects both a 1 mm flat cutter and an unverifiable ball-end pass.
        threadManufacturingClusterIds(geometry).forEach(function (id) {
            specializedClusterIds.add(id);
        });
        var cornerRadii = surfaceClusters.filter(function (cluster) {
            return cluster && !specializedClusterIds.has(String(cluster.id))
                && isNonNegativeNumber(cluster.internalCornerRadiusMm);
        }).map(function (cluster) { return cluster.internalCornerRadiusMm; });
        var cleanupToolDiameterMm = cornerRadii.length > 0
            ? Math.max(1, Math.min.apply(null, cornerRadii) * 2) : null;
        var primarySetups = (setups || []).filter(function (setup) {
            return (primarySetupIds || []).indexOf(setup.id) >= 0;
        });
        if (primarySetups.length === 0 && setups && setups.length > 0) { primarySetups = [setups[0]]; }
        var internalFillets = surfaceClusters.filter(function (cluster) {
            return !specializedClusterIds.has(String(cluster.id))
                && cluster.featureType !== 'thread' && cluster.featureType !== 'chamfer';
        })
            .flatMap(function (cluster) {
                // An explicit empty local scan must not fall through to the
                // coarse part-origin radius (e.g. the side of a long channel).
                if (Array.isArray(cluster && cluster.filletFeatures)) {
                    return cluster.filletFeatures.map(function (feature) {
                        return { cluster: cluster, radiusMm: feature.radiusMm, feature: feature };
                    });
                }
                var cylinder = cluster && cluster.localCylinder;
                var radiusMm = cylinder && cylinder.isInternal === true && isPositiveNumber(cylinder.radiusMm)
                    ? cylinder.radiusMm : (cluster && cluster.type === 'cylindrical'
                        && cluster.isInternal === true && isPositiveNumber(cluster.radiusMm)
                        ? cluster.radiusMm : null);
                return [{ cluster: cluster, radiusMm: radiusMm }];
            })
            .filter(function (cluster) {
                return cluster.cluster && isPositiveNumber(cluster.radiusMm)
                    && (!cluster.feature || !cluster.feature.axis
                        // An axial concave wall corner is flat-end-mill work, not a
                        // bottom fillet merely because an unrelated side setup exists.
                        // Preserve genuinely transverse but occluded fillets for review;
                        // explicit side-entry evidence also admits real side-pocket fillets.
                        || primarySetups.some(function (setup) {
                            return setup.direction && Math.abs(vectorDot(cluster.feature.axis, setup.direction)) < 0.5;
                        }) || (setups || []).some(function (setup) {
                            return Array.isArray(cluster.feature.accessibleDirectionIds)
                                && cluster.feature.accessibleDirectionIds.indexOf(setup.id) >= 0
                                && setup.direction && Math.abs(vectorDot(cluster.feature.axis, setup.direction)) < 0.5;
                        }))
                    && !holeClusterIds.has(String(cluster.cluster.id));
            });

        if (hasGeometry && hasMaterialRemoval) { operations.push(createOperation('facing', 'face_mill', 1)); }
        if (hasMaterialRemoval || pocketCount > 0) {
            operations.push(createOperation('roughing', 'end_mill', pocketCount, {
                maximumToolDiameterMm: bulkCutterLimitMm
            }));
        }
        if (isPositiveNumber(partSurfaceAreaMm2) || hasMaterialRemoval) {
            operations.push(createOperation('finishing', 'end_mill', 1, {
                targetToolDiameterMm: cleanupToolDiameterMm,
                maximumToolDiameterMm: cleanupToolDiameterMm || bulkCutterLimitMm
            }));
        }
        internalFillets.forEach(function (candidate, index) {
            var cluster = candidate.cluster;
            operations.push(createOperation('freeform_finishing', 'ball_end_mill', 0, {
                idSuffix: 'r' + Number(candidate.radiusMm).toFixed(3).replace('.', 'p') + '-' + (index + 1),
                targetToolDiameterMm: candidate.radiusMm * 2,
                featureClusterIds: [cluster.id],
                featureTriangleIndexes: candidate.feature && candidate.feature.triangleIndexes,
                featureAreaMm2: candidate.feature ? candidate.feature.areaMm2 : cluster.areaMm2,
                finishingRegions: [{ clusterIds: [cluster.id],
                    triangleIndexes: candidate.feature && candidate.feature.triangleIndexes,
                    areaMm2: candidate.feature ? candidate.feature.areaMm2 : cluster.areaMm2 }],
                filletAxis: candidate.feature && candidate.feature.axis,
                allowedDirectionIds: candidate.feature && candidate.feature.accessibleDirectionIds
            }));
        });
        surfaceClusters.forEach(function (cluster) {
            if (holeClusterIds.has(String(cluster.id)) || specializedClusterIds.has(String(cluster.id))
                || cluster.featureType === 'thread' || cluster.featureType === 'chamfer') { return; }
            var patches = cluster.curvedFinishingByDirection || {};
            var directions = Object.keys(patches).filter(function (id) {
                return setups.some(function (setup) { return setup.id === id; });
            });
            var filletTriangles = new Set(internalFillets.filter(function (candidate) {
                return candidate.cluster.id === cluster.id;
            }).flatMap(function (candidate) { return candidate.feature && candidate.feature.triangleIndexes || []; }));
            if (!Object.prototype.hasOwnProperty.call(cluster, 'curvedFinishingByDirection') && (cluster.type === 'spherical'
                || cluster.type === 'freeform' && !normalizedDirection(cluster.prismaticContourAxis))) {
                directions = [null];
            }
            directions.forEach(function (directionId) {
                var patch = directionId && patches[directionId];
                var triangles = patch && patch.triangleIndexes.filter(function (id) { return !filletTriangles.has(id); });
                if (patch && triangles.length === 0) { return; }
                operations.push(createOperation('freeform_finishing', 'ball_end_mill', 0, {
                    idSuffix: 'surface-' + cluster.id + '-' + (directionId || 'all'),
                    sampledSurfaceFinishing: true,
                    featureClusterIds: [cluster.id], featureTriangleIndexes: triangles || undefined,
                    featureAreaMm2: patch ? patch.areaMm2 : cluster.areaMm2,
                    allowedDirectionIds: directionId ? [directionId] : undefined
                }));
            });
        });
        if (holeProxies.length > 0) {
            holeProxies.forEach(function (hole, index) {
                if (!proxyArray(geometry.chamferProxies).some(function (chamfer) {
                    return pilotSpotChamfer(geometry, chamfer) && chamfer.pilotHoleId === hole.id;
                })) {
                    operations.push(createOperation('spot_drilling', 'spot_drill', 1, {
                        idSuffix: 'pilot-' + index, drillHoleId: hole.id,
                        majorDiameterMm: hole.diameterMm * 0.5, minorDiameterMm: hole.diameterMm,
                        includedAngleDegrees: 90, requiredDepthMm: hole.diameterMm * 0.25,
                        featureAxis: hole.axis, featureClusterIds: hole.surfaceClusterIds || []
                    }));
                }
                operations.push(createOperation('drilling', 'drill', 1, {
                    drillHoleId: hole.id,
                    idSuffix: 'd' + (isPositiveNumber(hole.diameterMm)
                        ? Number(hole.diameterMm).toFixed(3).replace('.', 'p') : 'unknown') + '-' + (index + 1),
                    targetToolDiameterMm: isPositiveNumber(hole.diameterMm) ? hole.diameterMm : null,
                    requiredDepthMm: isPositiveNumber(hole.depthMm) ? hole.depthMm : null,
                    featureAxis: hole.axis || null,
                    featureClusterIds: Array.isArray(hole.surfaceClusterIds) ? hole.surfaceClusterIds.slice() : []
                }));
            });
        } else if (holeCount > 0) {
            operations.push(createOperation('spot_drilling', 'spot_drill', holeCount));
            operations.push(createOperation('drilling', 'drill', holeCount));
        }
        if (unmatchedThreadCount > 0) { operations.push(createOperation('tapping', 'tap', unmatchedThreadCount)); }
        if (Array.isArray(geometry && geometry.threadProxies)) {
            geometry.threadProxies.forEach(function (thread, index) {
                var metric = isPositiveNumber(thread.pitchMm) && isPositiveNumber(thread.majorDiameterMm);
                var threadCode = threadOperationCode(thread);
                var nominalMajorDiameterMm = nominalMetricThreadDiameter(thread.majorDiameterMm, thread.pitchMm);
                operations.push(createOperation(threadCode, threadCode === 'tapping' ? 'tap' : 'thread_mill', 1, {
                    idSuffix: 'thread-' + index, featureClusterIds: thread.surfaceClusterIds || [],
                    featureAxis: thread.axis, targetToolDiameterMm: threadCode === 'tapping' && metric ? nominalMajorDiameterMm : null,
                    threadMajorDiameterMm: nominalMajorDiameterMm,
                    pitchMm: metric ? thread.pitchMm : null, minorDiameterMm: thread.minorDiameterMm,
                    measuredThread: metric, threadHand: thread.handedness,
                    drillHoleId: thread.holeId,
                    requiredDepthMm: thread.depthMm || thread.axialDepthMm
                }));
            });
        } else if (detectedThreadCount > 0) { operations.push(createOperation('thread_milling', 'thread_mill', detectedThreadCount)); }
        if (Array.isArray(geometry && geometry.chamferProxies)) {
            geometry.chamferProxies.forEach(function (chamfer, index) {
                var spotting = pilotSpotChamfer(geometry, chamfer);
                operations.push(createOperation(spotting ? 'spot_drilling' : 'chamfering', spotting ? 'spot_drill' : 'chamfer_mill', 1, {
                    drillHoleId: spotting ? chamfer.pilotHoleId : null,
                    idSuffix: 'chamfer-' + index, featureClusterIds: chamfer.surfaceClusterIds || [],
                    featureAxis: chamfer.axis, includedAngleDegrees: chamfer.includedAngleDegrees,
                    majorDiameterMm: chamfer.majorDiameterMm, minorDiameterMm: chamfer.minorDiameterMm,
                    requiredDepthMm: spotting ? chamfer.majorDiameterMm * 0.5 : chamfer.depthMm
                }));
            });
            if (proxyCount(requirements.chamfers) > 0) { operations.push(createOperation('chamfering', 'chamfer_mill', proxyCount(requirements.chamfers))); }
        } else if (chamferCount > 0) { operations.push(createOperation('chamfering', 'chamfer_mill', chamferCount)); }
        if (engravingCount > 0) { operations.push(createOperation('engraving', 'engraving_tool', engravingCount)); }
        if (stock && stock.strategy === 'thin_plate_nesting' && geometry && geometry.flatPlateEligible === true) {
            operations.push(createOperation('tab_removal', 'end_mill', 1));
        }
        if (hasGeometry) { operations.push(createOperation('deburring', 'deburr', 0)); }

        // Default prismatic sequence: establish the face, make holes while the stock still
        // supports them, remove bulk material, then finish and deburr. Sorting here preserves
        // the same sequence in every setup and in the order-review operation IDs.
        var sequence = { facing: 0, spot_drilling: 1, drilling: 2, roughing: 3,
            finishing: 4, freeform_finishing: 5, reaming: 6, tapping: 7,
            thread_milling: 7, chamfering: 6.5, engraving: 9, tab_removal: 10, deburring: 11 };
        operations.sort(function (left, right) {
            return (sequence[left.code] === undefined ? 9 : sequence[left.code])
                - (sequence[right.code] === undefined ? 9 : sequence[right.code])
                || (left.sampledSurfaceFinishing && right.sampledSurfaceFinishing
                    ? Math.min.apply(null, setups.filter(function (setup) { return !left.allowedDirectionIds || left.allowedDirectionIds.indexOf(setup.id) >= 0; }).map(function (setup) { return setup.number; }))
                        - Math.min.apply(null, setups.filter(function (setup) { return !right.allowedDirectionIds || right.allowedDirectionIds.indexOf(setup.id) >= 0; }).map(function (setup) { return setup.number; }))
                    : 0);
        });
        return operations;
    }

    function operationTool(operation, material, reachableRecords) {
        var tools = operation.sampledSurfaceFinishing
            ? [6, 4, 1].map(function (diameter) { return toolLibrary.ballRestTool(diameter); }).filter(function (tool) {
                return tool && ['6061', '5083', '6063', '7075'].indexOf(material) >= 0;
            }) : toolLibrary.compatible(operation.code, material);
        if (operation.assignedToolId) {
            tools = tools.filter(function (tool) { return tool.id === operation.assignedToolId; });
        }
        if (operation.code !== 'drilling' && Array.isArray(reachableRecords) && reachableRecords.length > 0) {
            var physicalSpot = operation.code === 'spot_drilling' || operation.code === 'chamfering';
            var reachableProfiles = new Set(reachableRecords.map(function (record) {
                return physicalSpot ? record.toolId : record.analysisProfileId || record.toolId;
            }));
            tools = tools.filter(function (tool) {
                return reachableProfiles.has(physicalSpot ? tool.id : tool.analysisProfileId || tool.id);
            });
        }
        if (tools.length === 0) { return null; }
        var targetDiameter = isPositiveNumber(operation.targetToolDiameterMm)
            ? operation.targetToolDiameterMm : null;
        var maximumDiameter = isPositiveNumber(operation.maximumToolDiameterMm)
            ? operation.maximumToolDiameterMm : null;
        var candidates = maximumDiameter
            ? tools.filter(function (tool) { return isPositiveNumber(tool.diameterMm) && tool.diameterMm <= maximumDiameter + 1e-6; })
            : tools.slice();
        // A catalogue neighbour is not a substitute for a dimensioned hole or thread.
        // Allow only the small tessellation deviation in imported nominal bores.
        candidates = candidates.filter(function (tool) {
            if (targetDiameter && operation.code === 'drilling'
                && Math.abs(tool.diameterMm - targetDiameter) > 0.05) { return false; }
            if (operation.code === 'tapping' && isPositiveNumber(operation.pitchMm)) {
                // Modeled root relief can exceed the nominal major diameter. Require
                // both measured diameters and pitch before allowing that difference.
                var diameterTolerance = operation.measuredThread && isPositiveNumber(operation.minorDiameterMm)
                    ? operation.pitchMm * 0.25 : 0.05;
                return tool.family === 'tap' && Math.abs(tool.diameterMm - targetDiameter) <= diameterTolerance
                    && (!operation.threadHand || operation.threadHand === (tool.handedness || 'right'))
                    && (!operation.measuredThread || !isPositiveNumber(operation.minorDiameterMm)
                        || Math.abs(tool.diameterMm - operation.pitchMm - operation.minorDiameterMm) <= operation.pitchMm * 0.2)
                    && Math.abs(tool.pitchMm - operation.pitchMm) <= 0.01;
            }
            if ((operation.code === 'chamfering' || operation.code === 'spot_drilling') && isPositiveNumber(operation.includedAngleDegrees)) {
                return Math.abs(tool.includedAngleDegrees - operation.includedAngleDegrees) <= 0.1
                    && (!tool.directSpotting || tool.diameterMm >= operation.majorDiameterMm
                        && tool.minimumHoleDiameterMm <= operation.minorDiameterMm);
            }
            return true;
        });
        if (candidates.length === 0) { return null; }
        candidates.sort(function (left, right) {
            if (targetDiameter) {
                var leftDifference = Math.abs(left.diameterMm - targetDiameter);
                var rightDifference = Math.abs(right.diameterMm - targetDiameter);
                if (leftDifference !== rightDifference) { return leftDifference - rightDifference; }
            } else if (operation.code === 'roughing' || operation.code === 'finishing' || operation.sampledSurfaceFinishing) {
                if (left.diameterMm !== right.diameterMm) { return right.diameterMm - left.diameterMm; }
            } else if (operation.code === 'spot_drilling' || operation.code === 'chamfering') {
                if (left.diameterMm !== right.diameterMm) { return left.diameterMm - right.diameterMm; }
            }
            if (left.timeMultiplier !== right.timeMultiplier) { return left.timeMultiplier - right.timeMultiplier; }
            if (left.wearMultiplier !== right.wearMultiplier) { return left.wearMultiplier - right.wearMultiplier; }
            return left.id.localeCompare(right.id);
        });
        return candidates[0];
    }

    function operationFeatureAxis(operation, geometry) {
        if (operation.featureAxis) { return normalizedDirection(operation.featureAxis); }
        var proxies = operation.code === 'thread_milling' ? proxyArray(geometry.threadProxies)
            : operation.code === 'drilling' || operation.code === 'tapping' ? proxyArray(geometry.holeProxies) : [];
        return proxies.length > 0 ? normalizedDirection(proxies[0].axis) : null;
    }

    function bestAlignedSetup(setups, axis) {
        if (!axis || setups.length === 0) { return setups[0] || null; }
        return setups.slice().sort(function (left, right) {
            return Math.abs(vectorDot(right.direction, axis)) - Math.abs(vectorDot(left.direction, axis));
        })[0];
    }

    function assignOperations(operations, setups, reachResult, geometry, material, requireReachEvidence, cuttingBudgets) {
        // Allocate sampled surface work, not whole clusters: one smooth cluster can
        // include an open face and a narrow recess reached by different cutter sizes.
        // Each sample has one owner per phase, so cleanup does not recount bulk work.
        var samplesByCluster = Object.create(null);
        var samplesById = new Map();
        var deferredSurfaceSamples = new Set();
        var field = geometry.accessibilityField;
        if (field && field.degraded !== true && Array.isArray(field.surfaceSamples)) {
            field.surfaceSamples.forEach(function (sample) {
                if (!samplesByCluster[sample.clusterId]) { samplesByCluster[sample.clusterId] = []; }
                samplesByCluster[sample.clusterId].push(sample);
                samplesById.set(sample.id, sample);
            });
        }
        var recordSamples = new Map();
        var recordsByClusterOperation = Object.create(null);
        reachResult.records.forEach(function (record) {
            if (!record.reachable) { return; }
            var key = record.clusterId + '\u0000' + record.operationCode;
            if (!recordsByClusterOperation[key]) { recordsByClusterOperation[key] = []; }
            recordsByClusterOperation[key].push(record);
            if (record.accessEvidence === 'field' || record.fieldSampleCount > 0) {
                recordSamples.set(record, new Set(record.reachableSampleIds || []));
            }
        });
        var expanded = [];
        var ballOwnedSamples = new Set();
        var facedSamplesBySetup = new Map();
        operations.forEach(function (operation) {
            if (['facing', 'roughing', 'finishing'].indexOf(operation.code) < 0 && !operation.sampledSurfaceFinishing
                || !requireReachEvidence) { expanded.push(operation); return; }
            var allocations = [];
            var missingSamples = [];
            var eligibleWork = 0;
            var alreadyFacedWork = 0;
            var toolsByEvidence = Object.create(null);
            (geometry.surfaceClusters || []).forEach(function (cluster) {
                if (operation.featureClusterIds && operation.featureClusterIds.indexOf(cluster.id) < 0) { return; }
                var clusterRecords = recordsByClusterOperation[cluster.id + '\u0000' + operation.code] || [];
                if (operation.sampledSurfaceFinishing) {
                    clusterRecords = clusterRecords.filter(function (record) { return record.accessEvidence === 'sampled-ball-contact'; });
                }
                var candidates = setups.map(function (setup) {
                    if (operation.allowedDirectionIds && operation.allowedDirectionIds.indexOf(setup.id) < 0) { return null; }
                    var records = clusterRecords.filter(function (record) {
                        return record.setupNumber === setup.number;
                    });
                    var normal = normalizedDirection(cluster.normal);
                    var alignment = normal ? vectorDot(normal, setup.direction) : 0;
                    if (operation.code === 'facing' && alignment < 0.8) { return null; }
                    var coverage = records.reduce(function (max, record) { return Math.max(max, record.reachableAreaMm2 || 0); }, 0);
                    return records.length > 0 ? { setup: setup, records: records, score: normal ? alignment : coverage } : null;
                }).filter(Boolean).sort(function (a, b) {
                    // Setup selection has already established the primary machining sequence.
                    // Assign each sampled cut to the first setup that can actually reach it;
                    // a later slot/recess setup must not win exterior work merely because its
                    // projection sees more of a coarse, curved surface cluster.
                    return operation.code === 'facing'
                        ? b.score - a.score || a.setup.number - b.setup.number
                        : a.setup.number - b.setup.number;
                });
                var samples = samplesByCluster[cluster.id];
                var work = samples && samples.length > 0 ? samples : [{ id: null, areaMm2: cluster.areaMm2 }];
                work.forEach(function (sample) {
                    if (operation.featureTriangleIndexes && operation.featureTriangleIndexes.indexOf(sample.sourceTriangleIndex) < 0) { return; }
                    if (operation.code === 'finishing' && operations.some(function (ball) {
                        return ball.sampledSurfaceFinishing && ball.featureClusterIds.indexOf(cluster.id) >= 0
                            && (!ball.featureTriangleIndexes || ball.featureTriangleIndexes.indexOf(sample.sourceTriangleIndex) >= 0);
                    })) { return; }
                    if (operation.sampledSurfaceFinishing && (ballOwnedSamples.has(sample.id) || deferredSurfaceSamples.has(sample.id))) { return; }
                    eligibleWork += 1;
                    var owner = null, selectedTool = null;
                    for (var index = 0; index < candidates.length; index += 1) {
                        var candidate = candidates[index];
                        var records = candidate.records.filter(function (record) {
                            var ids = recordSamples.get(record);
                            return sample.id === null || !ids || ids.has(sample.id);
                        });
                        if (records.length === 0) { continue; }
                        var evidenceKey = records.map(function (record) {
                            return record.analysisProfileId || record.toolId;
                        }).sort().join('\u0000');
                        if (!Object.prototype.hasOwnProperty.call(toolsByEvidence, evidenceKey)) {
                            toolsByEvidence[evidenceKey] = operationTool(operation, material, records);
                        }
                        selectedTool = toolsByEvidence[evidenceKey];
                        if (selectedTool) { owner = candidate.setup; break; }
                    }
                    if (!owner) {
                        if (operation.sampledSurfaceFinishing && sample.id !== null) { missingSamples.push(sample); }
                        return;
                    }
                    // A verified facing pass completes this planar surface. Do not
                    // create two more passes over it merely because a flat end mill
                    // can also reach it. Keep ownership local to the same setup.
                    if (sample.id !== null && ['roughing', 'finishing'].indexOf(operation.code) >= 0
                        && facedSamplesBySetup.has(owner.number)
                        && facedSamplesBySetup.get(owner.number).has(sample.id)) {
                        alreadyFacedWork += 1;
                        return;
                    }
                    if (operation.code === 'facing' && sample.id !== null && cluster.type === 'planar'
                        && normalizedDirection(cluster.normal)
                        && vectorDot(normalizedDirection(cluster.normal), owner.direction) >= 0.99999) {
                        if (!facedSamplesBySetup.has(owner.number)) { facedSamplesBySetup.set(owner.number, new Set()); }
                        facedSamplesBySetup.get(owner.number).add(sample.id);
                    }
                    if (operation.sampledSurfaceFinishing && sample.id !== null) { ballOwnedSamples.add(sample.id); }
                    var allocation = allocations.find(function (entry) {
                        return entry.setup === owner && entry.tool.id === selectedTool.id;
                    });
                    if (!allocation) {
                        allocation = { setup: owner, tool: selectedTool, ids: [], sampleIds: [], area: 0 };
                        allocations.push(allocation);
                    }
                    if (allocation.ids.indexOf(cluster.id) < 0) { allocation.ids.push(cluster.id); }
                    if (sample.id !== null) { allocation.sampleIds.push(sample.id); }
                    allocation.area += sample.areaMm2 || 0;
                });
            });
            if (missingSamples.length > 0) {
                expanded.push(Object.assign({}, operation, { id: operation.id + '-unreachable',
                    missingBallSamples: true, featureSampleIds: missingSamples.map(function (sample) { return sample.id; }),
                    featureAreaMm2: missingSamples.reduce(function (sum, sample) { return sum + (sample.areaMm2 || 0); }, 0) }));
            }
            if (allocations.length === 0) {
                if (alreadyFacedWork > 0 && alreadyFacedWork === eligibleWork) { return; }
                if (!operation.sampledSurfaceFinishing && (eligibleWork > 0 || operation.code !== 'finishing')) { expanded.push(operation); }
                return;
            }
            if (operation.code !== 'facing') {
                // Apply the same 1.5% per-cluster AND global discretization budget
                // used during setup selection to incidental work on a retained side
                // setup. A complete recess is never residue. Keep these sample IDs
                // explicit for review; do not claim another setup machined them.
                var primaryClusters = new Set();
                var marginalByCluster = new Map();
                allocations.forEach(function (entry) {
                    if (entry.setup.number <= 2) {
                        entry.ids.forEach(function (id) { primaryClusters.add(id); });
                    } else {
                        entry.sampleIds.forEach(function (id) {
                            var sample = samplesById.get(id);
                            if (!sample) { return; }
                            if (!marginalByCluster.has(sample.clusterId)) { marginalByCluster.set(sample.clusterId, []); }
                            marginalByCluster.get(sample.clusterId).push(sample);
                        });
                    }
                });
                var deferred = [];
                marginalByCluster.forEach(function (samples, id) {
                    var area = samples.reduce(function (sum, sample) { return sum + (sample.areaMm2 || 0); }, 0);
                    var total = (samplesByCluster[id] || []).reduce(function (sum, sample) { return sum + (sample.areaMm2 || 0); }, 0);
                    if (primaryClusters.has(id) && total > 0 && area / total <= 0.015) {
                        deferred.push.apply(deferred, samples);
                    }
                });
                var totalFieldArea = Array.from(samplesById.values()).reduce(function (sum, sample) { return sum + (sample.areaMm2 || 0); }, 0);
                var deferredArea = deferred.reduce(function (sum, sample) { return sum + (sample.areaMm2 || 0); }, 0);
                if (totalFieldArea > 0 && deferredArea / totalFieldArea <= 0.015) {
                    var deferredIds = new Set(deferred.map(function (sample) { return sample.id; }));
                    deferredIds.forEach(function (id) { deferredSurfaceSamples.add(id); });
                    allocations = allocations.filter(function (entry) {
                        if (!entry.sampleIds.some(function (id) { return deferredIds.has(id); })) { return true; }
                        entry.sampleIds = entry.sampleIds.filter(function (id) { return !deferredIds.has(id); });
                        var retained = entry.sampleIds.map(function (id) { return samplesById.get(id); });
                        entry.ids = Array.from(new Set(retained.map(function (sample) { return sample.clusterId; })));
                        entry.area = retained.reduce(function (sum, sample) { return sum + (sample.areaMm2 || 0); }, 0);
                        return entry.sampleIds.length > 0;
                    });
                }
                var allocatedArea = allocations.reduce(function (sum, entry) { return sum + entry.area; }, 0);
                var coverageBySetupTool = new Map();
                var bulkBySetup = new Map();
                allocations.forEach(function (entry) {
                    var bulk = bulkBySetup.get(entry.setup);
                    if (!bulk || entry.area > bulk.area) { bulkBySetup.set(entry.setup, entry); }
                });
                function sharedPrismaticDetail(source, target) {
                    var eligible = source.tool.family === 'flat_end_mill' && target.tool.family === 'flat_end_mill'
                        && source !== bulkBySetup.get(source.setup)
                        && source.ids.some(function (id) { return target.ids.indexOf(id) >= 0; })
                        && source.ids.every(function (id) {
                            var cluster = (geometry.surfaceClusters || []).find(function (entry) { return entry.id === id; });
                            var axis = cluster && normalizedDirection(cluster.prismaticContourAxis);
                            // Curved stock preparation and ball cutters retain the
                            // original productivity and certified-handoff rules.
                            return cluster && (cluster.type === 'planar'
                                || axis && Math.abs(vectorDot(axis, source.setup.direction)) >= 0.99999);
                        });
                    if (!eligible) { return false; }
                    // An allocation can contain a slot AND a separate pocket.
                    // Traverse only this source's unfinished work, from shared
                    // target-owned seeds; never borrow a faced/bulk transit face.
                    var connected = new Set(source.ids.filter(function (id) { return target.ids.indexOf(id) >= 0; }));
                    var pending = Array.from(connected);
                    while (pending.length) {
                        var id = pending.pop();
                        var cluster = (geometry.surfaceClusters || []).find(function (entry) { return entry.id === id; });
                        (cluster.adjacentClusterIds || []).forEach(function (neighbor) {
                            if (source.ids.indexOf(neighbor) >= 0 && !connected.has(neighbor)) {
                                connected.add(neighbor); pending.push(neighbor);
                            }
                        });
                    }
                    return source.ids.every(function (id) { return connected.has(id); });
                }
                function toolCoverage(entry) {
                    var key = entry.setup.number + '\u0000' + entry.tool.id;
                    if (!coverageBySetupTool.has(key)) {
                        var ids = new Set();
                        reachResult.records.forEach(function (record) {
                            if (record.reachable && record.setupNumber === entry.setup.number
                                && record.operationCode === operation.code
                                && (record.analysisProfileId || record.toolId) === (entry.tool.analysisProfileId || entry.tool.id)) {
                                (record.reachableSampleIds || []).forEach(function (id) { ids.add(id); });
                            }
                        });
                        coverageBySetupTool.set(key, ids);
                    }
                    return coverageBySetupTool.get(key);
                }
                // Consolidate only onto a cutter already needed in this setup, with
                // affirmative reach for EVERY transferred sample. Keep the bulk cutter
                // when a smaller one serves only a minority recess; never substitute
                // whole-face visibility for actual cutter/holder clearance.
                allocations.slice().sort(function (a, b) { return a.area - b.area; }).forEach(function (source) {
                    if (allocations.indexOf(source) < 0 || source.sampleIds.length === 0) { return; }
                    var options = allocations.filter(function (target) {
                        return target !== source && target.setup === source.setup
                            && (target.tool.diameterMm >= source.tool.diameterMm || target.area > source.area
                                || sharedPrismaticDetail(source, target))
                            && source.sampleIds.every(function (id) { return toolCoverage(target).has(id); });
                    }).map(function (target) {
                        // Conservative diameter-squared cutting penalty versus the existing
                        // configured tool-change allowance; this is a quoting heuristic,
                        // not a manufacturer feed/speed recommendation.
                        // Preserve the bulk cutter's productivity. For detail work,
                        // prefer a complete-feature cutter over several intermediate
                        // diameters. Initial sample ownership is not feature size;
                        // it must not prevent a necessary smaller cutter absorbing
                        // the rest of the same reachable work.
                        var sharedDetail = sharedPrismaticDetail(source, target);
                        var penalty = sharedDetail ? 1
                            : Math.max(1, Math.pow(source.tool.diameterMm / target.tool.diameterMm, 2));
                        var cuttingCode = operation.sampledSurfaceFinishing ? 'finishing' : operation.code;
                        var addedMinutes = (cuttingBudgets && cuttingBudgets[cuttingCode] || 0)
                            * source.area / (cuttingCode === 'finishing' ? Math.max(allocatedArea, totalFieldArea) : allocatedArea)
                            * Math.max(0, penalty * target.tool.timeMultiplier - source.tool.timeMultiplier);
                        return { target: target, addedMinutes: addedMinutes };
                    }).filter(function (option) { return option.addedMinutes < planning.toolChangeMinutes; })
                        .sort(function (a, b) { return a.addedMinutes - b.addedMinutes
                            || b.target.tool.diameterMm - a.target.tool.diameterMm
                            || a.target.tool.id.localeCompare(b.target.tool.id); });
                    if (options.length === 0) { return; }
                    var target = options[0].target;
                    target.sampleIds.push.apply(target.sampleIds, source.sampleIds);
                    source.ids.forEach(function (id) { if (target.ids.indexOf(id) < 0) { target.ids.push(id); } });
                    target.area += source.area;
                    allocations.splice(allocations.indexOf(source), 1);
                });
            }
            allocations.sort(function (left, right) {
                return left.setup.number - right.setup.number || right.tool.diameterMm - left.tool.diameterMm
                    || left.tool.id.localeCompare(right.tool.id);
            });
            var totalArea = allocations.reduce(function (sum, entry) { return sum + entry.area; }, 0);
            allocations.forEach(function (entry) {
                expanded.push(Object.assign({}, operation, {
                    id: operation.id + '-setup-' + entry.setup.number + '-tool-' + entry.tool.id,
                    assignedSetupNumber: entry.setup.number,
                    assignedToolId: entry.tool.id,
                    featureClusterIds: entry.ids,
                    featureSampleIds: entry.sampleIds,
                    facingFinishedSampleIds: operation.code === 'facing' ? entry.sampleIds.filter(function (id) {
                        return facedSamplesBySetup.has(entry.setup.number) && facedSamplesBySetup.get(entry.setup.number).has(id);
                    }) : undefined,
                    featureAreaMm2: entry.area,
                    estimatedMinutes: operation.estimatedMinutes * (totalArea > 0 ? entry.area / totalArea : 1 / allocations.length)
                }));
            });
        });
        var missingOwnedSamples = new Set();
        expanded = expanded.filter(function (operation) {
            if (!operation.missingBallSamples) { return true; }
            operation.featureSampleIds = operation.featureSampleIds.filter(function (id) {
                if (ballOwnedSamples.has(id) || missingOwnedSamples.has(id)) { return false; }
                missingOwnedSamples.add(id); return true;
            });
            operation.featureAreaMm2 = operation.featureSampleIds.reduce(function (sum, id) { return sum + (samplesById.get(id).areaMm2 || 0); }, 0);
            return operation.featureSampleIds.length > 0;
        });
        // Sampled cutter contact can leave a few boundary surfels unowned where a
        // tessellated sharp edge meets an otherwise verified curved pass. Do not turn
        // that numerical residue into a fictitious machining operation with no cutter.
        // Use the same conservative global 1.5% budget as setup selection, and only
        // defer it when every affected cluster already has affirmative ball contact.
        // A wholly inaccessible curved feature therefore remains an explicit review.
        var missingBallOperations = expanded.filter(function (operation) { return operation.missingBallSamples; });
        var missingBallSampleIds = new Set(missingBallOperations.flatMap(function (operation) {
            return operation.featureSampleIds || [];
        }));
        var totalFieldArea = Array.from(samplesById.values()).reduce(function (sum, sample) {
            return sum + (sample.areaMm2 || 0);
        }, 0);
        var missingBallArea = Array.from(missingBallSampleIds).reduce(function (sum, id) {
            var sample = samplesById.get(id);
            return sum + (sample && sample.areaMm2 || 0);
        }, 0);
        var missingBallClusterIds = new Set(Array.from(missingBallSampleIds).map(function (id) {
            var sample = samplesById.get(id);
            return sample && sample.clusterId;
        }).filter(Boolean));
        var partiallyCoveredClusters = Array.from(missingBallClusterIds).every(function (clusterId) {
            return (samplesByCluster[clusterId] || []).some(function (sample) { return ballOwnedSamples.has(sample.id); });
        });
        var insignificantBallBoundaryResidue = missingBallSampleIds.size > 0 && totalFieldArea > 0
            && missingBallArea / totalFieldArea <= 0.015 && partiallyCoveredClusters;
        if (insignificantBallBoundaryResidue) {
            missingBallSampleIds.forEach(function (id) { deferredSurfaceSamples.add(id); });
            expanded = expanded.filter(function (operation) { return !operation.missingBallSamples; });
        }
        operations.splice.apply(operations, [0, operations.length].concat(expanded));
        var advisoryAllocation = requireReachEvidence && setups.length > 0
            && !reachResult.records.some(function (record) { return record.reachable; });
        var drillingGroups = [];
        var preferredDrillSetup = Object.create(null);
        function operationReachableInSetup(operation, setup) {
            return reachResult.records.some(function (record) {
                return record.reachable && record.operationCode === operation.code
                    && record.setupNumber === setup.number
                    && (!operation.featureClusterIds || operation.featureClusterIds.length === 0
                        || operation.featureClusterIds.indexOf(record.clusterId) >= 0);
            });
        }
        operations.filter(function (operation) { return operation.code === 'drilling'; }).forEach(function (operation) {
            var axis = operationFeatureAxis(operation, geometry);
            if (!axis) { return; }
            var group = drillingGroups.find(function (entry) { return Math.abs(vectorDot(entry.axis, axis)) >= 0.995; });
            if (!group) { group = { axis: axis, operations: [] }; drillingGroups.push(group); }
            group.operations.push(operation);
        });
        drillingGroups.forEach(function (group) {
            var ranked = setups.map(function (setup) {
                var covered = group.operations.filter(function (operation) {
                    return operationReachableInSetup(operation, setup);
                });
                var completeSequences = covered.filter(function (drill) {
                    if (!drill.drillHoleId) { return true; }
                    var linked = operations.filter(function (operation) {
                        return operation.drillHoleId === drill.drillHoleId;
                    });
                    var spots = linked.filter(function (operation) { return operation.code === 'spot_drilling'; });
                    var taps = linked.filter(function (operation) { return operation.code === 'tapping'; });
                    // Either mouth may start a through-hole. The opposite mouth's
                    // finishing spot still belongs to its own accessible setup.
                    return (spots.length === 0 || spots.some(function (operation) {
                        return operationReachableInSetup(operation, setup);
                    })) && taps.every(function (operation) { return operationReachableInSetup(operation, setup); });
                }).length;
                return { setup: setup, covered: covered, completeSequences: completeSequences };
            }).sort(function (left, right) {
                return right.completeSequences - left.completeSequences
                    || right.covered.length - left.covered.length || left.setup.number - right.setup.number;
            });
            if (ranked.length > 0) {
                ranked[0].covered.forEach(function (operation) { preferredDrillSetup[operation.id] = ranked[0].setup; });
            }
        });
        operations.forEach(function (operation) {
            if (!operation.drillHoleId || ['spot_drilling', 'tapping'].indexOf(operation.code) < 0) { return; }
            var drill = operations.find(function (candidate) {
                return candidate.code === 'drilling' && candidate.drillHoleId === operation.drillHoleId;
            });
            if (drill && preferredDrillSetup[drill.id]
                && operationReachableInSetup(operation, preferredDrillSetup[drill.id])) {
                preferredDrillSetup[operation.id] = preferredDrillSetup[drill.id];
            }
        });
        operations.forEach(function (operation, operationIndex) {
            var reachable = reachResult.records.filter(function (record) {
                return record.reachable && !operation.missingBallSamples && record.operationCode === operation.code
                    && (!operation.sampledSurfaceFinishing || record.accessEvidence === 'sampled-ball-contact')
                    && (!operation.sampledSurfaceFinishing || !operation.featureTriangleIndexes
                        || (record.reachableSampleIds || []).some(function (id) {
                            var sample = samplesById.get(id);
                            return sample && operation.featureTriangleIndexes.indexOf(sample.sourceTriangleIndex) >= 0;
                        }))
                    && (!operation.assignedSetupNumber || record.setupNumber === operation.assignedSetupNumber)
                    && (!Array.isArray(operation.allowedDirectionIds) || operation.allowedDirectionIds.indexOf(record.setupId) >= 0)
                    && (!operation.filletAxis || setups.some(function (setup) {
                        return setup.number === record.setupNumber && Math.abs(vectorDot(operation.filletAxis, setup.direction)) < 0.5;
                    }))
                    && (!Array.isArray(operation.featureClusterIds) || operation.featureClusterIds.length === 0
                        || operation.featureClusterIds.map(String).indexOf(String(record.clusterId)) >= 0)
                    && setups.some(function (setup) { return setup.number === record.setupNumber; });
            });
            // A modeled thread proves the pilot-hole axis is open even when the imported CAD
            // exposes thread faces but no separate cylindrical hole cluster. Reuse that setup
            // evidence for drilling while retaining a drill as the selected cutting tool.
            // Never replace a detected pilot's failed drill-length/entry checks with the
            // shorter thread engagement. This fallback is only for legacy thread-only facts.
            if (operation.code === 'drilling' && reachable.length === 0
                && !isPositiveNumber(operation.requiredDepthMm)
                && !isPositiveNumber(operation.targetToolDiameterMm)
                && (!Array.isArray(operation.featureClusterIds) || operation.featureClusterIds.length === 0)) {
                reachable = reachResult.records.filter(function (record) {
                    return record.reachable
                        && (record.operationCode === 'tapping' || record.operationCode === 'thread_milling')
                        && setups.some(function (setup) { return setup.number === record.setupNumber; });
                });
            }
            if (preferredDrillSetup[operation.id]) {
                reachable = reachable.filter(function (record) {
                    return record.setupNumber === preferredDrillSetup[operation.id].number;
                });
            }
            var selectedTool = operationTool(operation, material, reachable);
            var selectedToolRecords = selectedTool ? reachable.filter(function (record) {
                if (operation.code === 'spot_drilling' || operation.code === 'chamfering') { return record.toolId === selectedTool.id; }
                return (record.analysisProfileId || record.toolId)
                    === (selectedTool.analysisProfileId || selectedTool.id);
            }) : [];
            var setupEvidence = selectedToolRecords.length > 0 ? selectedToolRecords : reachable;
            var assignedSetup = advisoryAllocation
                ? setups[operationIndex % setups.length]
                : preferredDrillSetup[operation.id] || (setupEvidence.length > 0
                ? setups.find(function (setup) { return setup.number === setupEvidence[0].setupNumber; })
                : setups.find(function (setup) { return operation.allowedDirectionIds && operation.allowedDirectionIds.indexOf(setup.id) >= 0; })
                    || bestAlignedSetup(setups, operationFeatureAxis(operation, geometry)));
            if (!selectedTool) { selectedTool = operationTool(operation, material); }
            // A missing curved-contact region is work for review, not evidence
            // that the largest library ball is a usable planned cutter.
            var requiresPhysicalSpotEvidence = requireReachEvidence
                && (operation.code === 'spot_drilling' || operation.code === 'chamfering');
            if ((operation.sampledSurfaceFinishing || requiresPhysicalSpotEvidence)
                && selectedToolRecords.length === 0) { selectedTool = null; }

            operation.setupNumber = assignedSetup ? assignedSetup.number : 1;
            operation.toolId = selectedTool ? selectedTool.id : null;
            operation.toolDiameterMm = selectedTool && isPositiveNumber(selectedTool.diameterMm)
                ? selectedTool.diameterMm : null;
            operation.toolType = selectedTool ? selectedTool.family : operation.toolFamily;
            operation.toolFamily = operation.toolType;
            operation.reachable = (!!selectedTool || operation.code === 'deburring')
                && (!requireReachEvidence || operation.code === 'deburring' || reachable.length > 0)
                && (!operation.sampledSurfaceFinishing || !!selectedTool);
            if (operation.reachable && selectedTool && selectedTool.family === 'hss_drill') {
                var requiredSpots = operations.filter(function (candidate) {
                    return candidate.code === 'spot_drilling'
                        && (operation.drillHoleId
                            ? candidate.drillHoleId === operation.drillHoleId
                            : !candidate.drillHoleId);
                });
                operation.reachable = requiredSpots.some(function (spot) {
                    return spot.reachable && spot.setupNumber === operation.setupNumber;
                });
            }
            operation.confidence = operation.reachable ? (reachable.length > 0 ? reachable[0].confidence : 'Medium') : 'Low';
            operation.clusterIds = (selectedToolRecords.length > 0 ? selectedToolRecords : reachable).filter(function (record) {
                return record.setupNumber === operation.setupNumber;
            }).map(function (record) { return record.clusterId; }).filter(function (id, index, ids) {
                return ids.indexOf(id) === index;
            });
            operation.minutes = operation.estimatedMinutes;

            if (assignedSetup) {
                assignedSetup.operationIds.push(operation.id);
                if (operation.toolId && assignedSetup.toolIds.indexOf(operation.toolId) === -1) {
                    assignedSetup.toolIds.push(operation.toolId);
                }
            }
        });
        // Several fillet strips with the same cutter and entry are one finishing pass.
        var consolidated = [];
        operations.forEach(function (operation) {
            var existing = operation.code === 'freeform_finishing' && isPositiveNumber(operation.featureAreaMm2)
                ? consolidated.find(function (entry) {
                    return entry.code === operation.code && entry.toolId === operation.toolId
                        && entry.setupNumber === operation.setupNumber && entry.reachable === operation.reachable
                        && !!entry.sampledSurfaceFinishing === !!operation.sampledSurfaceFinishing;
                }) : null;
            if (!existing) { consolidated.push(operation); return; }
            existing.featureAreaMm2 += operation.featureAreaMm2 || 0;
            existing.estimatedMinutes += operation.estimatedMinutes;
            existing.clusterIds = Array.from(new Set(existing.clusterIds.concat(operation.clusterIds)));
            existing.featureClusterIds = Array.from(new Set(existing.featureClusterIds.concat(operation.featureClusterIds)));
            existing.featureTriangleIndexes = Array.from(new Set((existing.featureTriangleIndexes || [])
                .concat(operation.featureTriangleIndexes || [])));
            existing.featureSampleIds = Array.from(new Set((existing.featureSampleIds || []).concat(operation.featureSampleIds || [])));
            existing.finishingRegions = (existing.finishingRegions || []).concat(operation.finishingRegions || []);
            var owner = setups.find(function (setup) { return setup.number === operation.setupNumber; });
            if (owner) { owner.operationIds = owner.operationIds.filter(function (id) { return id !== operation.id; }); }
        });
        operations.splice.apply(operations, [0, operations.length].concat(consolidated));
        return Array.from(deferredSurfaceSamples);
    }

    function applyBallStockHandoffs(operations, setups, geometry, material, spindleLimitRpm) {
        var evidence = geometry.ballRestHandoffs;
        if (!Array.isArray(evidence) || evidence.length === 0 || !toolLibrary.estimateBallRestPasses) { return; }
        var samples = new Map(((geometry.accessibilityField || {}).surfaceSamples || []).map(function (sample) { return [sample.id, sample]; }));
        setups.forEach(function (setup) {
            var own = operations.filter(function (operation) { return operation.setupNumber === setup.number; });
            var small = own.filter(function (operation) {
                return operation.toolFamily === 'flat_end_mill' && operation.toolDiameterMm === 1
                    && (operation.code === 'roughing' || operation.code === 'finishing');
            });
            var preparation = own.find(function (operation) {
                return operation.code === 'roughing' && operation.toolDiameterMm === 2 && operation.reachable;
            });
            var ball = own.find(function (operation) {
                return operation.code === 'freeform_finishing' && operation.toolDiameterMm === 1 && operation.reachable;
            });
            if (small.length === 0 || !preparation || !ball || !own.some(function (operation) {
                return operation.code === 'facing' && operation.reachable;
            }) || small.some(function (operation) { return !operation.reachable || !(operation.featureSampleIds || []).length; })) { return; }
            var finishingAccess = (geometry.ballRestFinishingAccess || []).find(function (entry) {
                return entry.directionId === setup.id && entry.toolId === 'ns-alb225-1-lu5'
                    && entry.method === 'sampled-ball-contact' && entry.camCertain === false;
            });
            var verifiedTriangles = new Set(finishingAccess && finishingAccess.triangleIndexes || []);
            if (!(ball.featureTriangleIndexes || []).length || !ball.featureTriangleIndexes.every(function (index) {
                return verifiedTriangles.has(index);
            })) { return; }
            var ids = Array.from(new Set(small.flatMap(function (operation) { return operation.featureSampleIds; })));
            var handoffs = ids.map(function (id) {
                var sample = samples.get(id);
                return sample && evidence.find(function (entry) {
                    return entry.sampleId === id && entry.sourceTriangleIndex === sample.sourceTriangleIndex
                        && entry.directionId === setup.id && entry.ballToolId === 'ns-alb225-1-lu5'
                        && entry.preparationDiameterMm === 2 && entry.requiresFacing === true && entry.requiresPreparation === true
                        && entry.method === 'sampled-ball-stock-handoff' && entry.camCertain === false
                        && Number.isFinite(entry.residualAxialCapMm) && entry.residualAxialCapMm > 0 && entry.residualAxialCapMm <= .75
                        && [entry.ballCenterMm, entry.preparationTipMm].every(function (point) {
                            return point && [point.x, point.y, point.z].every(Number.isFinite);
                        });
                });
            });
            // All-or-nothing for an operation. Partial contact evidence must not
            // erase another feature's flat work, even on the same BREP face.
            if (handoffs.some(function (entry) { return !entry; })) { return; }
            var triangles = new Set(ball.featureTriangleIndexes || []);
            var extraArea = ids.reduce(function (sum, id) {
                var sample = samples.get(id);
                return sum + (triangles.has(sample.sourceTriangleIndex) ? 0 : sample.areaMm2 || 0);
            }, 0);
            var area = (ball.featureAreaMm2 || 0) + extraArea;
            var handoffArea = ids.reduce(function (sum, id) { return sum + (samples.get(id).areaMm2 || 0); }, 0);
            var cap = Math.max.apply(null, handoffs.map(function (entry) { return entry.residualAxialCapMm; }));
            var policy = toolLibrary.estimateBallRestPasses({ material: material, diameterMm: 1,
                residualAxialCapMm: cap, areaMm2: handoffArea, spindleLimitRpm: spindleLimitRpm });
            var finishPolicy = toolLibrary.estimateBallRestPasses({ material: material, diameterMm: 1,
                residualAxialCapMm: 0, areaMm2: area, spindleLimitRpm: spindleLimitRpm });
            if (!policy || !finishPolicy) { return; }
            var preparationIds = small.filter(function (operation) { return operation.code === 'roughing'; })
                .flatMap(function (operation) { return operation.featureSampleIds; });
            var existingPreparation = new Set(preparation.featureSampleIds || []);
            preparation.featureAreaMm2 += preparationIds.reduce(function (sum, id) {
                return sum + (existingPreparation.has(id) ? 0 : samples.get(id).areaMm2 || 0);
            }, 0);
            preparation.featureSampleIds = Array.from(new Set((preparation.featureSampleIds || []).concat(preparationIds)));
            preparation.featureClusterIds = Array.from(new Set((preparation.featureClusterIds || []).concat(ids.map(function (id) { return samples.get(id).clusterId; }))));
            preparation.restPreparation = { tool: toolLibrary.ballRestPreparationTool(), handoffs: handoffs,
                method: 'sampled-ball-stock-handoff', camCertain: false };
            ball.toolId = 'ns-alb225-1-lu5';
            ball.featureAreaMm2 = area;
            ball.featureSampleIds = Array.from(new Set((ball.featureSampleIds || []).concat(ids)));
            ball.stockHandoff = Object.assign({}, policy, { preparationSampleIds: ids,
                passes: policy.passes.filter(function (pass) { return pass.phase === 'rest_machining'; })
                    .concat(finishPolicy.passes),
                handoffAreaMm2: handoffArea, finalFinishingAreaMm2: area,
                restCuttingMinutes: policy.passes.filter(function (pass) { return pass.phase === 'rest_machining'; })
                    .reduce(function (sum, pass) { return sum + pass.cuttingMinutes; }, 0),
                finalFinishingMinutes: finishPolicy.cuttingMinutes,
                preparationOperationId: preparation.id, method: 'sampled-ball-stock-handoff', camCertain: false,
                spindleBasis: 'configured-quotation-limit', stockClearanceBasis: 'sampled-advisory', requiresCamVerification: true });
            ball.stockHandoff.cuttingMinutes = ball.stockHandoff.restCuttingMinutes + ball.stockHandoff.finalFinishingMinutes;
            // Keep bulk roughing volume in the preceding flat allocation. The
            // ball's ordered rest layers are charged separately below.
            small.forEach(function (operation) {
                if (operation.code === 'roughing') { preparation.estimatedMinutes += operation.estimatedMinutes || 0; }
                operations.splice(operations.indexOf(operation), 1);
            });
            setup.operationIds = operations.filter(function (operation) { return operation.setupNumber === setup.number; }).map(function (operation) { return operation.id; });
            setup.toolIds = Array.from(new Set(operations.filter(function (operation) { return operation.setupNumber === setup.number; }).map(function (operation) { return operation.toolId; }).filter(Boolean)));
        });
    }

    function compareAssignedOperations(a, b) {
        var sequence = { facing: 0, spot_drilling: 1, drilling: 2, roughing: 3, finishing: 4,
            freeform_finishing: 5, reaming: 6, tapping: 7, thread_milling: 7,
            chamfering: 6.5, engraving: 9, tab_removal: 10, deburring: 11 };
        var leftPhase = sequence[a.code] === undefined ? 9 : sequence[a.code];
        var rightPhase = sequence[b.code] === undefined ? 9 : sequence[b.code];
        if (leftPhase !== rightPhase) { return leftPhase - rightPhase; }
        if (a.code === b.code && (a.code === 'roughing' || a.code === 'finishing'
            || a.code === 'freeform_finishing')) {
            return (Number(b.toolDiameterMm) || 0) - (Number(a.toolDiameterMm) || 0);
        }
        return 0;
    }

    function applyCurvedStockHandoffs(operations, setups, geometry, material, spindleLimitRpm) {
        var samples = new Map(((geometry.accessibilityField || {}).surfaceSamples || []).map(function (sample) { return [sample.id, sample]; }));
        var certificates = geometry.generalBallRestHandoffs || [];
        if (certificates.length === 0) { return; }
        var transferredSources = new Set();
        setups.forEach(function (setup) {
            var own = operations.filter(function (operation) { return operation.setupNumber === setup.number; });
            var balls = own.filter(function (operation) { return operation.sampledSurfaceFinishing && operation.reachable; });
            balls.forEach(function (ball) {
                var transfers = [];
                own.filter(function (operation) {
                    return operation.code === 'roughing' && operation.toolFamily === 'flat_end_mill' && operation.reachable;
                }).forEach(function (source) {
                    (source.featureSampleIds || []).forEach(function (id) {
                        if ((ball.featureSampleIds || []).indexOf(id) < 0) { return; }
                        var sample = samples.get(id);
                        var certificate = certificates.find(function (entry) {
                            var prepTool = toolLibrary.get(entry.preparationToolId);
                            return sample && prepTool && prepTool.family === 'flat_end_mill'
                                && [6, 10].indexOf(prepTool.diameterMm) >= 0 && prepTool.diameterMm > source.toolDiameterMm
                                && entry.preparationDiameterMm === prepTool.diameterMm
                                && entry.sampleId === id && entry.sourceTriangleIndex === sample.sourceTriangleIndex
                                && entry.directionId === setup.id && entry.ballToolId === ball.toolId
                                && entry.requiresPreparation === true && entry.method === 'sampled-ball-stock-handoff'
                                && entry.camCertain === false && entry.stockClearanceBasis === 'prepared-cylinder-and-stock-top'
                                && Number.isFinite(entry.residualAxialCapMm) && entry.residualAxialCapMm > 0
                                && entry.residualAxialCapMm <= ball.toolDiameterMm
                                && [entry.ballCenterMm, entry.preparationTipMm].every(function (point) {
                                    return point && [point.x, point.y, point.z].every(Number.isFinite);
                                });
                        });
                        if (certificate) { transfers.push({ source: source, sample: sample, certificate: certificate }); }
                    });
                });
                if (transfers.length === 0) { return; }
                var cap = Math.max.apply(null, transfers.map(function (entry) { return entry.certificate.residualAxialCapMm; }));
                var area = transfers.reduce(function (sum, entry) { return sum + (entry.sample.areaMm2 || 0); }, 0);
                var policy = toolLibrary.estimateBallRestPasses({ material: material, diameterMm: ball.toolDiameterMm,
                    residualAxialCapMm: cap, areaMm2: area, spindleLimitRpm: spindleLimitRpm });
                var finish = toolLibrary.estimateBallRestPasses({ material: material, diameterMm: ball.toolDiameterMm,
                    residualAxialCapMm: 0, areaMm2: ball.featureAreaMm2, spindleLimitRpm: spindleLimitRpm });
                if (!policy || !finish) { return; }
                var hasFacing = own.some(function (operation) { return operation.code === 'facing' && operation.reachable; });
                // Worker certificates based on the finished bounding plane require
                // an actual facing pass. Never silently assume previous stock removal.
                if (!hasFacing && transfers.some(function (entry) { return entry.certificate.requiresFacing; })) {
                    var facingProof = (geometry.stockFacingRequirements || []).find(function (entry) {
                        return entry.directionId === setup.id && entry.method === 'model-exterior-plane'
                            && entry.requiresFacing === true && entry.camCertain === false && Number.isFinite(entry.planeProjectionMm)
                            && transfers.every(function (transfer) {
                                return !transfer.certificate.requiresFacing
                                    || Math.abs(transfer.certificate.facedStockTopMm - entry.planeProjectionMm) <= 1e-6;
                            });
                    });
                    var facingTool = operationTool({ code: 'facing' }, material);
                    if (!facingProof || !facingTool) { return; }
                    var fallbackFacing = own.find(function (operation) {
                        return operation.code === 'facing' && operation.reachable === false
                            && !(operation.featureSampleIds || []).length && !(operation.featureClusterIds || []).length;
                    });
                    var facing = Object.assign(fallbackFacing
                        || createOperation('facing', 'face_mill', 1, { idSuffix: 'curved-stock-' + setup.id }), {
                        setupNumber: setup.number, toolId: facingTool.id, toolDiameterMm: facingTool.diameterMm,
                        toolType: facingTool.family, reachable: true, confidence: 'Medium', clusterIds: [],
                        stockPreparation: facingProof
                    });
                    if (!fallbackFacing) { own.push(facing); operations.push(facing); }
                }
                var preparationIds = [];
                transfers.forEach(function (transfer) {
                    var tool = toolLibrary.get(transfer.certificate.preparationToolId);
                    var prep = own.find(function (operation) { return operation.code === 'roughing' && operation.toolId === tool.id && operation.reachable; });
                    if (!prep) {
                        prep = Object.assign(createOperation('roughing', 'flat_end_mill', 0, { idSuffix: 'curved-preparation-' + setup.id + '-' + tool.id }), {
                            setupNumber: setup.number, toolId: tool.id, toolDiameterMm: tool.diameterMm, toolType: tool.family,
                            reachable: true, confidence: 'Medium', featureSampleIds: [], featureClusterIds: [], clusterIds: [], featureAreaMm2: 0
                        });
                        own.push(prep); operations.push(prep);
                    }
                    var source = transfer.source, sample = transfer.sample;
                    transferredSources.add(source);
                    var share = source.featureAreaMm2 > 0 ? sample.areaMm2 / source.featureAreaMm2 : 0;
                    var minutes = source.estimatedMinutes * share;
                    source.estimatedMinutes -= minutes; prep.estimatedMinutes += minutes;
                    source.featureSampleIds = source.featureSampleIds.filter(function (id) { return id !== sample.id; });
                    source.featureClusterIds = Array.from(new Set(source.featureSampleIds.map(function (id) { return samples.get(id).clusterId; })));
                    source.clusterIds = source.featureClusterIds.slice();
                    source.featureAreaMm2 = Math.max(0, source.featureAreaMm2 - sample.areaMm2);
                    prep.featureSampleIds = Array.from(new Set((prep.featureSampleIds || []).concat(sample.id)));
                    prep.featureAreaMm2 += sample.areaMm2;
                    prep.featureClusterIds = Array.from(new Set((prep.featureClusterIds || []).concat(sample.clusterId)));
                    prep.clusterIds = prep.featureClusterIds.slice();
                    if (!prep.restPreparation) { prep.restPreparation = { tool: tool, handoffs: [], method: 'sampled-ball-stock-handoff', camCertain: false }; }
                    prep.restPreparation.handoffs.push(transfer.certificate);
                    preparationIds.push(prep.id);
                });
                var rest = policy.passes.filter(function (pass) { return pass.phase === 'rest_machining'; });
                ball.stockHandoff = Object.assign({}, policy, { preparationSampleIds: transfers.map(function (entry) { return entry.sample.id; }),
                    preparationOperationIds: Array.from(new Set(preparationIds)), passes: rest.concat(finish.passes),
                    handoffAreaMm2: area, finalFinishingAreaMm2: ball.featureAreaMm2,
                    restCuttingMinutes: rest.reduce(function (sum, pass) { return sum + pass.cuttingMinutes; }, 0),
                    finalFinishingMinutes: finish.cuttingMinutes, method: 'sampled-ball-stock-handoff', camCertain: false,
                    requiresCamVerification: true, stockClearanceBasis: 'prepared-cylinder-and-stock-top' });
                ball.stockHandoff.cuttingMinutes = ball.stockHandoff.restCuttingMinutes + finish.cuttingMinutes;
            });
        });
        var retained = operations.filter(function (operation) {
            return !(transferredSources.has(operation) && operation.featureSampleIds.length === 0);
        });
        retained.sort(compareAssignedOperations);
        operations.splice.apply(operations, [0, operations.length].concat(retained));
        setups.forEach(function (setup) {
            var own = operations.filter(function (operation) { return operation.setupNumber === setup.number; });
            setup.operationIds = own.map(function (operation) { return operation.id; });
            setup.toolIds = Array.from(new Set(own.map(function (operation) { return operation.toolId; }).filter(Boolean)));
        });
    }

    function retainEvidenceSetups(setups, operations, reachResult, geometry, setupEvidence) {
        var primarySetupIds = setupEvidence && Array.isArray(setupEvidence.primarySetupIds)
            ? setupEvidence.primarySetupIds : [];
        var directionalFeatureSetupIds = setupEvidence && Array.isArray(setupEvidence.directionalFeatureSetupIds)
            ? setupEvidence.directionalFeatureSetupIds : [];
        var retained = setups.filter(function (setup) {
            if (setupEvidence && setupEvidence.primaryPairOnlyEligible) {
                return primarySetupIds.indexOf(setup.id) !== -1
                    || directionalFeatureSetupIds.indexOf(setup.id) !== -1;
            }
            return (geometry && geometry.bodyCount > 1)
                || setup.reachableClusterIds.length > 0
                || setup.operationIds.length > 0
                || setup.toolIds.length > 0;
        });

        if (retained.length === 0) {
            retained = setups.length > 0 ? [setups[0]] : [{
                id: 'conservative-top-side',
                direction: null,
                toolDirection: null,
                workholding: 'engineering_review',
                reachableClusterIds: [],
                operationIds: [],
                toolIds: [],
                minutes: 0
            }];
            retained[0].workholding = 'engineering_review';
        }

        var numberById = {};
        retained.forEach(function (setup, index) {
            numberById[setup.id] = index + 1;
            setup.sequence = index + 1;
            setup.number = index + 1;
        });

        operations.forEach(function (operation) {
            var owner = retained.find(function (setup) {
                return setup.operationIds.indexOf(operation.id) !== -1;
            });
            if (owner) { operation.setupNumber = owner.number; }
        });

        reachResult.records = reachResult.records.filter(function (record) {
            return Object.prototype.hasOwnProperty.call(numberById, record.setupId);
        }).map(function (record) {
            return Object.assign({}, record, { setupNumber: numberById[record.setupId] });
        });
        return retained;
    }

    function decorateLegacySetups(setups) {
        return setups.map(function (setup, index) {
            var direction = cloneDirection(setup.toolDirection || setup.direction);
            return Object.assign({}, setup, {
                sequence: index + 1,
                number: index + 1,
                direction: direction,
                toolDirection: direction,
                workholding: index === 0 ? 'vise_primary' : index === 1 ? 'vise_flip' : 'vise_reindex',
                reachableClusterIds: [],
                operationIds: [],
                toolIds: [],
                minutes: 0
            });
        });
    }

    function selectedFlatToolRadius(operations) {
        var diameters = operations.map(function (operation) { return toolLibrary.get(operation.toolId); })
            .filter(function (tool) { return tool && tool.family === 'flat_end_mill' && isPositiveNumber(tool.diameterMm); })
            .map(function (tool) { return tool.diameterMm; });
        if (diameters.length === 0) {
            diameters = toolLibrary.list().filter(function (tool) {
                return tool.family === 'flat_end_mill' && isPositiveNumber(tool.diameterMm);
            }).map(function (tool) { return tool.diameterMm; });
        }
        return diameters.length > 0 ? Math.min.apply(null, diameters) * 0.5 : 0;
    }

    function boundedVoxelResolution(geometry) {
        var size = geometry.orientedSizeMm || {};
        var maximum = Math.max(Number(size.x) || 0, Number(size.y) || 0, Number(size.z) || 0, 1);
        return {
            x: Math.max(1, Math.min(48, Math.ceil(((Number(size.x) || 0) / maximum) * 48))),
            y: Math.max(1, Math.min(48, Math.ceil(((Number(size.y) || 0) / maximum) * 48))),
            z: Math.max(1, Math.min(48, Math.ceil(((Number(size.z) || 0) / maximum) * 48)))
        };
    }

    function spatialFieldAccounting(geometry, selectedStockVolume, reachPlan) {
        var field = geometry && geometry.accessibilityField;
        if (!field || !isPositiveNumber(field.cellSizeMm) || !Array.isArray(field.occupancyRuns)
            || !Array.isArray(field.surfaceSamples)) {
            return null;
        }
        var occupiedCellCount = field.occupancyRuns.reduce(function (total, run) {
            return total + (Array.isArray(run) && Number.isSafeInteger(run[1]) && run[1] > 0 ? run[1] : 0);
        }, 0);
        var cellVolumeMm3 = Math.pow(field.cellSizeMm, 3);
        var coveredSampleIds = new Set();
        (reachPlan && Array.isArray(reachPlan.setups) ? reachPlan.setups : []).forEach(function (setup) {
            (Array.isArray(setup.coveredSampleIds) ? setup.coveredSampleIds : []).forEach(function (id) {
                coveredSampleIds.add(id);
            });
        });
        var unmachinableSampleIds = new Set(reachPlan && Array.isArray(reachPlan.unmachinableSampleIds)
            ? reachPlan.unmachinableSampleIds : []);
        var axialFluteClusterIds = new Set(reachPlan && Array.isArray(reachPlan.ignoredClusterIds)
            ? reachPlan.ignoredClusterIds.map(String) : []);
        var machinableSurfaceAreaMm2 = 0;
        var residualVolumeMm3 = 0;
        field.surfaceSamples.forEach(function (sample) {
            var area = isPositiveNumber(sample.areaMm2) ? sample.areaMm2 : 0;
            if (coveredSampleIds.has(sample.id) || axialFluteClusterIds.has(String(sample.clusterId))) {
                machinableSurfaceAreaMm2 += area;
            }
            if (unmachinableSampleIds.has(sample.id)) { residualVolumeMm3 += area * field.cellSizeMm; }
        });
        var partVolumeMm3 = occupiedCellCount * cellVolumeMm3;
        return {
            method: field.degraded === true ? 'degraded_spatial_field' : 'adaptive_spatial_field',
            checksum: field.checksum || null,
            cellSizeMm: field.cellSizeMm,
            occupiedCellCount: occupiedCellCount,
            cellVolumeMm3: cellVolumeMm3,
            partVolumeMm3: partVolumeMm3,
            removalVolumeMm3: Math.max(0, selectedStockVolume - partVolumeMm3),
            machinableSurfaceAreaMm2: machinableSurfaceAreaMm2,
            residualVolumeMm3: residualVolumeMm3,
            degraded: field.degraded === true,
            resolutionLimited: field.resolutionLimited === true
        };
    }

    function estimateResidual(geometry, reachResult, ignoredClusterIds, operations, removalVolumeMm3,
        fieldAccounting, unmachinableSampleIds) {
        var residualIds = reachResult.residualClusters.map(function (cluster) { return cluster.id; })
            .filter(function (id) { return ignoredClusterIds.indexOf(id) === -1; });
        var cutterLimitedSampleIds = Array.isArray(unmachinableSampleIds)
            ? unmachinableSampleIds.slice() : [];
        var cutterLimitedRegions = [];
        var estimatedVolume = geometry.surfaceClusters.reduce(function (total, cluster) {
            if (residualIds.indexOf(cluster.id) === -1) { return total; }
            var depth = isPositiveNumber(cluster.requiredDepthMm) ? cluster.requiredDepthMm
                : isPositiveNumber(cluster.axialDepthMm) ? cluster.axialDepthMm : 0.1;
            return total + ((isPositiveNumber(cluster.areaMm2) ? cluster.areaMm2 : 0) * depth * 0.25);
        }, 0);
        var toolRadius = selectedFlatToolRadius(operations);
        geometry.surfaceClusters.forEach(function (cluster) {
            if (!isNonNegativeNumber(cluster.internalCornerRadiusMm) || cluster.internalCornerRadiusMm >= toolRadius || toolRadius <= 0) {
                return;
            }
            var cornerCount = Number.isSafeInteger(cluster.internalCornerCount) && cluster.internalCornerCount > 0
                ? cluster.internalCornerCount : 1;
            var depth = isPositiveNumber(cluster.requiredDepthMm) ? cluster.requiredDepthMm
                : isPositiveNumber(cluster.axialDepthMm) ? cluster.axialDepthMm : 1;
            var excessRadius = toolRadius - cluster.internalCornerRadiusMm;
            estimatedVolume += excessRadius * excessRadius * (1 - (Math.PI / 4)) * depth * cornerCount;
            if (residualIds.indexOf(cluster.id) === -1) { residualIds.push(cluster.id); }
            cutterLimitedRegions.push({
                type: 'internal_corner',
                clusterId: cluster.id,
                count: cornerCount,
                toolRadiusMm: toolRadius,
                requestedRadiusMm: cluster.internalCornerRadiusMm,
                retainedRadiusMm: toolRadius,
                depthMm: depth,
                centroid: cluster.centroid || null,
                normal: cluster.normal || null
            });
        });
        if (fieldAccounting && fieldAccounting.degraded !== true) {
            estimatedVolume = Math.max(estimatedVolume, fieldAccounting.residualVolumeMm3);
        }
        estimatedVolume = Math.min(Math.max(0, estimatedVolume), Math.max(0, removalVolumeMm3));

        return {
            label: 'Estimated remaining material',
            camCertain: false,
            method: 'bounded_analytic_and_surface_evidence',
            voxelResolutionCap: 48,
            voxelResolution: boundedVoxelResolution(geometry),
            surfaceOnly: cutterLimitedSampleIds.length === 0 && cutterLimitedRegions.length === 0,
            estimatedVolumeMm3: estimatedVolume,
            cutterRadiusMm: toolRadius,
            cutterLimitedSampleIds: cutterLimitedSampleIds,
            cutterLimitedRegions: cutterLimitedRegions,
            clusterIds: residualIds,
            confidence: residualIds.length > 0 ? 'Low' : 'Medium'
        };
    }

    function operationMinutes(operations) {
        return operations.reduce(function (total, operation) {
            if (operation.code === 'finishing' || operation.code === 'deburring') {
                return total;
            }

            return total + operation.estimatedMinutes;
        }, 0);
    }

    function toolFamilies(operations) {
        return operations.reduce(function (families, operation) {
            if (families.indexOf(operation.toolFamily) === -1) {
                families.push(operation.toolFamily);
            }

            return families;
        }, []);
    }

    function fixtureDirections(count) {
        var directions = [
            { id: 'positive-z' },
            { id: 'negative-z' },
            { id: 'positive-x' },
            { id: 'negative-x' }
        ];
        return directions.slice(0, Math.max(0, count));
    }

    function planFixture(directions, undercut, requirements) {
        return plan({
            alloy: '6061',
            geometry: {
                orientedSizeMm: { x: 80, y: 60, z: 20 },
                requiredToolDirections: fixtureDirections(directions),
                orientationCandidates: [{ id: 'positive-z', projectedFaceCoverage: 0.70 }],
                flatPlateEligible: false,
                undercutRisk: undercut === true,
                geometryConfidence: 'High',
                reviewReasons: []
            },
            stock: {
                strategy: 'rectangular_block',
                stockSizeMm: { x: 88, y: 68, z: 30 },
                confidence: 'High',
                reviewReasons: []
            },
            partVolumeMm3: 70000,
            partSurfaceAreaMm2: 15000,
            requirements: requirements || { quantity: 1 }
        });
    }

    function finitePlanValue(value) {
        return typeof value === 'number' && Number.isFinite(value) && value >= 0;
    }

    function malformedFallback(reviewReasons) {
        addReason(reviewReasons, 'malformed_planning_input');
        var batchMinutes = planning.setupMinutes + planning.probingMinutesPerSetup + planning.toolChangeMinutes;
        var cycleMinutesPerPart = planning.handlingMinutesPerPart + planning.deburrMinutesPerPart;
        var fixedMinutes = batchMinutes + cycleMinutesPerPart;
        var fallbackSetup = {
            id: 'conservative-top-side', sequence: 1, number: 1, direction: null, toolDirection: null,
            workholding: 'engineering_review', reachableClusterIds: [], operationIds: [], toolIds: [], minutes: fixedMinutes
        };
        var residual = {
            label: 'Estimated remaining material', camCertain: false, method: 'surface_only',
            voxelResolutionCap: 48, voxelResolution: { x: 1, y: 1, z: 1 }, surfaceOnly: true,
            estimatedVolumeMm3: 0, cutterRadiusMm: 0, cutterLimitedSampleIds: [],
            cutterLimitedRegions: [], clusterIds: [], confidence: 'Low'
        };
        return {
            setups: [fallbackSetup],
            setupCount: 1,
            operations: [],
            reachMatrix: [],
            residual: residual,
            roughingMinutes: 0,
            finishingMinutes: 0,
            batchMinutes: batchMinutes,
            cycleMinutesPerPart: cycleMinutesPerPart,
            fixedMinutes: fixedMinutes,
            totalMinutesPerPart: fixedMinutes,
            estimatedMinutesPerPart: fixedMinutes,
            toolingAllowanceBeforeVat: planning.standardToolingAllowanceBeforeVat,
            toolingBeforeVat: planning.standardToolingAllowanceBeforeVat,
            workholdingAllowanceBeforeVat: planning.standardWorkholdingAllowanceBeforeVat,
            workholdingBeforeVat: planning.standardWorkholdingAllowanceBeforeVat,
            inspectionAllowanceBeforeVat: 0,
            confidence: 'Low',
            reviewReasons: reviewReasons
        };
    }

    function plan(input) {
        input = input || {};
        if (input.production === true || input.featureGraph || input.operationGraph) {
            return planFromOperationGraph(input);
        }
        var geometry = input.geometry || input.geometryFacts || input.cncGeometry || {};
        var stock = input.stock || input.stockPlan || {};
        var requirements = input.requirements || input.requirementInput || {};
        var threads = threadSummary(requirements.threads);
        var reviewReasons = [];
        var confidence = 'High';
        var alloy = input.material || input.alloy;
        var materialRecord = materialCatalog.get(alloy);
        var materialMrr;
        var finishRate;
        if (materialRecord) {
            materialMrr = materialCatalog.adjustRate(alloy, planning.mrrMm3PerMinute['6061'], 'mrr');
            finishRate = materialCatalog.adjustRate(alloy, planning.finishMm2PerMinute['6061'], 'finish');
        }
        var validGeometry = geometry && geometry.orientedSizeMm;
        var hasPartVolume = isNonNegativeNumber(input.partVolumeMm3)
            || isNonNegativeNumber(input.partVolume)
            || isNonNegativeNumber(geometry.partVolumeMm3);
        var hasPartSurfaceArea = isNonNegativeNumber(input.partSurfaceAreaMm2)
            || isNonNegativeNumber(input.partSurfaceArea)
            || isNonNegativeNumber(geometry.partSurfaceAreaMm2);
        var partVolumeMm3 = isNonNegativeNumber(input.partVolumeMm3)
            ? input.partVolumeMm3
            : isNonNegativeNumber(input.partVolume) ? input.partVolume
                : isNonNegativeNumber(geometry.partVolumeMm3) ? geometry.partVolumeMm3 : 0;
        var partSurfaceAreaMm2 = isNonNegativeNumber(input.partSurfaceAreaMm2)
            ? input.partSurfaceAreaMm2
            : isNonNegativeNumber(input.partSurfaceArea) ? input.partSurfaceArea
                : isNonNegativeNumber(geometry.partSurfaceAreaMm2) ? geometry.partSurfaceAreaMm2 : 0;
        var selectedStockVolume = stockVolumeMm3(stock);

        if (!materialMrr || !finishRate) {
            addReason(reviewReasons, 'unsupported_alloy');
            confidence = 'Low';
            materialMrr = planning.mrrMm3PerMinute['6061'];
            finishRate = planning.finishMm2PerMinute['6061'];
        }
        if (materialRecord && materialRecord.procurement) {
            confidence = lowerConfidence(confidence, materialRecord.procurement.confidence);
            if (materialRecord.procurement.supplierReviewRequired === true) {
                addReason(reviewReasons, 'outside_material_model_range');
            }
        }

        if (!validGeometry || selectedStockVolume === 0 || !hasPartVolume || !hasPartSurfaceArea) {
            addReason(reviewReasons, 'malformed_planning_input');
            confidence = 'Low';
        }
        if (!validGeometry) { addReason(reviewReasons, 'low_geometry_confidence'); }
        if (selectedStockVolume === 0) { addReason(reviewReasons, 'low_stock_confidence'); }

        (Array.isArray(geometry.reviewReasons) ? geometry.reviewReasons : []).forEach(function (reason) { addReason(reviewReasons, reason); });
        (Array.isArray(stock.reviewReasons) ? stock.reviewReasons : []).forEach(function (reason) { addReason(reviewReasons, reason); });

        var hasReachGeometry = Array.isArray(geometry.surfaceClusters) && geometry.surfaceClusters.length > 0;
        var reachPlan = hasReachGeometry
            ? weightedReachSetups(geometry, stock, alloy, threads.count > 0, reviewReasons)
            : null;
        var setups = reachPlan ? reachPlan.setups : decorateLegacySetups(planSetups(geometry, reviewReasons));
        if (geometry.undercutRisk === true) {
            addReason(reviewReasons, 'undercut_risk');
            confidence = 'Low';
        }
        if (geometry.deepFeatureRisk === true) {
            addReason(reviewReasons, 'deep_reach');
            confidence = 'Low';
        }
        if (geometry.weakGripRisk === true) {
            addReason(reviewReasons, 'weak_grip');
            confidence = 'Low';
        }
        if (geometry.specialToolRisk === true) {
            addReason(reviewReasons, 'special_tool');
            confidence = 'Low';
        }
        if (geometry.customFixtureRisk === true || reachPlan && reachPlan.fixtureStrategy) {
            addReason(reviewReasons, 'custom_fixture');
            confidence = 'Low';
        }
        if (geometry.geometryConfidence && geometry.geometryConfidence !== 'High') {
            addReason(reviewReasons, 'low_geometry_confidence');
            confidence = lowerConfidence(confidence, geometry.geometryConfidence === 'Medium' ? 'Medium' : 'Low');
        }
        if (stock.confidence && stock.confidence !== 'High') {
            addReason(reviewReasons, 'low_stock_confidence');
            confidence = lowerConfidence(confidence, stock.confidence === 'Medium' ? 'Medium' : 'Low');
        }

        var toleranceClass = requirements.toleranceClass || 'ISO 2768-m';
        if (requirements.hasTightTolerance === true) {
            addReason(reviewReasons, 'tight_tolerance');
            confidence = lowerConfidence(confidence, 'Medium');
        }
        if (toleranceClass !== 'ISO 2768-m') {
            addReason(reviewReasons, 'non_default_tolerance');
            confidence = lowerConfidence(confidence, 'Medium');
        }
        if (threads.incompleteSpecification) {
            addReason(reviewReasons, 'incomplete_thread_specification');
            confidence = lowerConfidence(confidence, 'Medium');
        }
        if (threads.malformedCount) {
            addReason(reviewReasons, 'malformed_thread_count');
            confidence = lowerConfidence(confidence, 'Medium');
        }
        if (isPositiveNumber(requirements.surfaceRoughnessRa) && requirements.surfaceRoughnessRa < 1.6) {
            addReason(reviewReasons, 'special_surface_roughness');
            confidence = lowerConfidence(confidence, 'Medium');
        }
        if (requirements.certificateRequired === true) {
            addReason(reviewReasons, 'certificate_required');
            confidence = lowerConfidence(confidence, 'Medium');
        }
        if (requirements.inspectionLevel && requirements.inspectionLevel !== 'standard') {
            addReason(reviewReasons, 'enhanced_inspection');
            confidence = lowerConfidence(confidence, 'Medium');
        }
        if (reviewReasons.indexOf('uncertain_geometry_directions') !== -1) {
            confidence = 'Low';
        }

        var fieldAccounting = spatialFieldAccounting(geometry, selectedStockVolume, reachPlan);
        var authoritativeFieldAccounting = fieldAccounting && fieldAccounting.degraded !== true;
        var removalVolumeMm3 = authoritativeFieldAccounting
            ? fieldAccounting.removalVolumeMm3 : Math.max(0, selectedStockVolume - partVolumeMm3);
        var featureEvidenceComplete = proxyCount(geometry.holeProxies) > 0 || proxyCount(geometry.pocketProxies) > 0;
        var roughingVolumeMm3 = authoritativeFieldAccounting
            ? removalVolumeMm3 : featureEvidenceComplete ? removalVolumeMm3 : removalVolumeMm3 * 0.85;
        var finishingAreaMm2 = authoritativeFieldAccounting && fieldAccounting.machinableSurfaceAreaMm2 > 0
            ? fieldAccounting.machinableSurfaceAreaMm2
            : partSurfaceAreaMm2 > 0 ? partSurfaceAreaMm2 : removalVolumeMm3 * 0.15;
        var reachResult = reachPlan ? reachPlan.reach : { records: [], residualClusters: [], confidence: confidence, reviewReasons: [] };
        // Prune heuristic side candidates before numbering/assigning work. Otherwise an
        // orphan from discarded setup 1 can masquerade as retained setup 4 renumbered to 1.
        if (reachPlan && reachPlan.primaryPairOnlyEligible) {
            setups = retainEvidenceSetups(setups, [], reachResult, geometry, reachPlan);
        }
        var operations = planOperations(geometry, stock, requirements, threads, removalVolumeMm3, partSurfaceAreaMm2,
            setups, reachPlan && reachPlan.primarySetupIds);
        var ballClusterIds = new Set(operations.filter(function (operation) {
            return operation.code === 'freeform_finishing';
        }).flatMap(function (operation) { return operation.featureClusterIds || []; }));
        if (hasReachGeometry && ballClusterIds.size > 0) {
            // Evaluate local finishing against the selected setups without making a
            // cylindrical counterbore demand a new side setup just to use a ball cutter.
            var ballGeometry = Object.assign({}, reachPlan.geometry, {
                surfaceClusters: reachPlan.geometry.surfaceClusters.filter(function (cluster) {
                    return ballClusterIds.has(cluster.id);
                }).map(function (cluster) { return Object.assign({}, cluster, { operationCodes: ['freeform_finishing'] }); })
            });
            var ballReach = reachEvaluator.evaluate({ geometry: ballGeometry, material: alloy, setups: setups });
            reachResult.records = reachResult.records.filter(function (record) {
                return record.operationCode !== 'freeform_finishing' || !ballClusterIds.has(record.clusterId);
            }).concat(ballReach.records);
        }
        var ballSamples = new Map(((geometry.accessibilityField || {}).surfaceSamples || []).map(function (sample) { return [sample.id, sample]; }));
        (geometry.generalBallFinishingAccess || []).forEach(function (access) {
            var setup = setups.find(function (entry) { return entry.id === access.directionId; });
            var tool = toolLibrary.get(access.toolId);
            if (!setup || !tool || access.method !== 'sampled-ball-contact' || access.camCertain !== false) { return; }
            var byCluster = new Map();
            (access.sampleIds || []).forEach(function (id) {
                var sample = ballSamples.get(id);
                if (!sample || (access.triangleIndexes || []).indexOf(sample.sourceTriangleIndex) < 0) { return; }
                if (!byCluster.has(sample.clusterId)) { byCluster.set(sample.clusterId, []); }
                byCluster.get(sample.clusterId).push(sample);
            });
            byCluster.forEach(function (samples, clusterId) {
                reachResult.records.push({ clusterId: clusterId, setupId: setup.id, setupNumber: setup.number,
                    toolId: tool.id, operationCode: 'freeform_finishing', reachable: true,
                    reachableSampleIds: samples.map(function (sample) { return sample.id; }),
                    fieldSampleCount: samples.length, reachableAreaMm2: samples.reduce(function (sum, sample) { return sum + (sample.areaMm2 || 0); }, 0),
                    accessEvidence: 'sampled-ball-contact', confidence: 'Medium', camCertain: false });
            });
        });
        reachResult.reviewReasons.forEach(function (reason) { addReason(reviewReasons, reason); });
        confidence = lowerConfidence(confidence, reachResult.confidence);
        var deferredSurfaceSampleIds = assignOperations(operations, setups, reachResult, reachPlan ? reachPlan.geometry : geometry, alloy, hasReachGeometry, {
            roughing: roughingVolumeMm3 / materialMrr,
            finishing: finishingAreaMm2 / finishRate
        });
        applyBallStockHandoffs(operations, setups, geometry, alloy, planning.ballRestSpindleLimitRpm);
        applyCurvedStockHandoffs(operations, setups, geometry, alloy, planning.ballRestSpindleLimitRpm);
        setups = retainEvidenceSetups(setups, operations, reachResult, geometry, reachPlan);
        // A facing-only reverse setup still needs its exposed edges deburred.
        // The per-part deburring budget remains shared.
        var deburr = operations.find(function (operation) { return operation.code === 'deburring'; });
        if (deburr) {
            setups.forEach(function (setup) {
                var own = operations.filter(function (operation) { return operation.setupNumber === setup.number; });
                if (own.length === 0 || !own.every(function (operation) {
                    return operation.code === 'facing' && operation.reachable;
                })) { return; }
                var copy = Object.assign({}, deburr, { id: deburr.id + '-setup-' + setup.number,
                    setupNumber: setup.number, clusterIds: [], estimatedMinutes: 0, minutes: 0 });
                operations.push(copy);
                setup.operationIds.push(copy.id);
                if (copy.toolId && setup.toolIds.indexOf(copy.toolId) < 0) { setup.toolIds.push(copy.toolId); }
            });
        }
        if (setups.length > planning.maxAutomaticSetups) {
            addReason(reviewReasons, 'too_many_setups');
            confidence = 'Low';
        }
        if (operations.some(function (operation) { return operation.reachable === false; })
            && (!reachPlan || reachPlan.hasSignificantUnmachinableSurface !== false
                || operations.some(function (operation) { return operation.reachable === false && (operation.filletAxis || operation.sampledSurfaceFinishing); }))) {
            addReason(reviewReasons, 'unreachable_tool_access');
            confidence = 'Low';
        }
        var requiredToolFamilies = toolFamilies(operations);
        if (requiredToolFamilies.length === 0) {
            requiredToolFamilies.push('end_mill');
        }

        var fieldConfidenceFactor = fieldAccounting && fieldAccounting.degraded === true
            ? 1.15 : fieldAccounting && fieldAccounting.resolutionLimited === true ? 1.08 : 1;
        var facedSampleIds = new Set();
        operations.filter(function (operation) { return operation.code === 'facing' && operation.reachable; })
            .forEach(function (operation) {
                (operation.facingFinishedSampleIds || []).forEach(function (id) { facedSampleIds.add(id); });
            });
        var facedAreaMm2 = ((geometry.accessibilityField || {}).surfaceSamples || []).reduce(function (area, sample) {
            return area + (facedSampleIds.has(sample.id) ? sample.areaMm2 || 0 : 0);
        }, 0);
        // Facing already leaves the specified plane finished. Remove its area
        // from the end-mill finishing budget, rather than redistributing that
        // charge to the remaining slot and contour operations.
        finishingAreaMm2 = Math.max(0, finishingAreaMm2 - facedAreaMm2);
        var roughingMinutes = (roughingVolumeMm3 / materialMrr) * fieldConfidenceFactor;
        var finishingMinutes = (finishingAreaMm2 / finishRate) * fieldConfidenceFactor;
        operations.forEach(function (operation) { operation.cuttingMinutes = 0; });
        function allocateCuttingTime(codes, minutes) {
            var candidates = operations.filter(function (operation) {
                return codes.indexOf(operation.code) >= 0 && (operation.code !== 'facing'
                    || operation.reachable && isPositiveNumber(operation.featureAreaMm2));
            });
            var weights = candidates.map(function (operation) {
                return isPositiveNumber(operation.featureAreaMm2) ? operation.featureAreaMm2 : 1;
            });
            var samplesById = new Map(((geometry.accessibilityField || {}).surfaceSamples || []).map(function (sample) {
                return [sample.id, sample];
            }));
            var ballRegions = candidates.filter(function (operation) { return operation.code === 'freeform_finishing'; })
                .flatMap(function (ball) {
                    return (ball.finishingRegions || []).map(function (region) {
                        return Object.assign({ setupNumber: ball.setupNumber }, region);
                    });
                });
            ballRegions.forEach(function (region) {
                var triangles = new Set(region.triangleIndexes || []);
                var clusters = new Set(region.clusterIds || []);
                var overlaps = candidates.map(function (flat, index) {
                    if (flat.code !== 'finishing' || flat.setupNumber !== region.setupNumber) { return 0; }
                    var samples = (flat.featureSampleIds || []).map(function (id) { return samplesById.get(id); }).filter(Boolean);
                    var area;
                    if (samples.length > 0) {
                        area = samples.reduce(function (sum, sample) {
                            var matches = triangles.size > 0 ? triangles.has(sample.sourceTriangleIndex) : clusters.has(sample.clusterId);
                            return sum + (matches ? sample.areaMm2 || 0 : 0);
                        }, 0);
                    } else {
                        // Legacy cluster-only evidence can distribute the replacement
                        // within the owning clusters, never across unrelated setup faces.
                        var ids = new Set(flat.featureClusterIds || []);
                        var totalArea = 0;
                        var matchingArea = 0;
                        (geometry.surfaceClusters || []).forEach(function (cluster) {
                            if (!ids.has(cluster.id)) { return; }
                            totalArea += cluster.areaMm2 || 0;
                            if (clusters.has(cluster.id)) { matchingArea += cluster.areaMm2 || 0; }
                        });
                        area = totalArea > 0 ? weights[index] * matchingArea / totalArea : 0;
                    }
                    return Math.min(weights[index], area);
                });
                var overlapArea = overlaps.reduce(function (sum, area) { return sum + area; }, 0);
                var replacementArea = Math.min(region.areaMm2 || 0, overlapArea);
                if (overlapArea > 0) {
                    overlaps.forEach(function (area, index) { weights[index] -= replacementArea * area / overlapArea; });
                }
            });
            var total = weights.reduce(function (sum, weight) { return sum + weight; }, 0);
            return candidates.reduce(function (sum, operation, index) {
                var tool = toolLibrary.get(operation.toolId);
                var share = total > 0 ? weights[index] / total : 1 / candidates.length;
                var baseMinutes = minutes * share * (tool && tool.timeMultiplier || 1);
                if (operation.sampledSurfaceFinishing && operation.reachable) {
                    var finishPolicy = toolLibrary.estimateBallRestPasses({ material: alloy,
                        diameterMm: operation.toolDiameterMm, residualAxialCapMm: 0,
                        areaMm2: operation.featureAreaMm2, spindleLimitRpm: planning.ballRestSpindleLimitRpm });
                    if (finishPolicy) {
                        operation.finishingPolicy = finishPolicy;
                        baseMinutes = Math.max(baseMinutes, finishPolicy.cuttingMinutes);
                    }
                }
                operation.cuttingMinutes += operation.stockHandoff
                    ? Math.max(baseMinutes, operation.stockHandoff.finalFinishingMinutes)
                        + operation.stockHandoff.restCuttingMinutes
                    : baseMinutes;
                return sum + operation.cuttingMinutes;
            }, 0);
        }
        // Facing is stock removal too: preserve the removal-volume budget when
        // replacing redundant flat-end-mill passes with the face mill.
        var cuttingMinutesPerPart = allocateCuttingTime(['roughing', 'facing'], roughingMinutes)
            + allocateCuttingTime(['finishing', 'freeform_finishing'], finishingMinutes);
        operations.forEach(function (operation) { operation.minutes = operation.estimatedMinutes + operation.cuttingMinutes; });
        var batchMinutes = (setups.length * (planning.setupMinutes + planning.probingMinutesPerSetup))
            + (requiredToolFamilies.length * planning.toolChangeMinutes);
        var cycleMinutesPerPart = ((cuttingMinutesPerPart + operationMinutes(operations)) / planning.utilizationFactor)
            + planning.handlingMinutesPerPart
            + planning.deburrMinutesPerPart;
        var fixedMinutes = batchMinutes + planning.handlingMinutesPerPart + planning.deburrMinutesPerPart;
        var totalMinutesPerPart = batchMinutes + cycleMinutesPerPart;
        setups.forEach(function (setup) {
            var operationTime = operations.filter(function (operation) { return operation.setupNumber === setup.number; })
                .reduce(function (total, operation) { return total + operation.minutes; }, 0);
            setup.minutes = planning.setupMinutes + planning.probingMinutesPerSetup + operationTime;
        });

        var usesLongReachTool = setups.some(function (setup) {
            return setup.toolIds.some(function (toolId) {
                var selectedTool = toolLibrary.get(toolId);
                return selectedTool && selectedTool.longReach === true;
            });
        });
        var toolingAllowanceBeforeVat = geometry.specialToolRisk === true
            ? planning.specialToolAllowanceBeforeVat
            : geometry.deepFeatureRisk === true || usesLongReachTool
                ? planning.longReachToolingAllowanceBeforeVat
                : planning.standardToolingAllowanceBeforeVat;
        if (materialRecord) {
            toolingAllowanceBeforeVat = materialCatalog.adjustRate(
                alloy,
                toolingAllowanceBeforeVat,
                'toolingWear');
        }
        var fixtureEstimate = estimateMatingFixture(geometry, reachPlan && reachPlan.fixtureStrategy);
        var workholdingAllowanceBeforeVat = fixtureEstimate
            ? fixtureEstimate.amountBeforeVat
            : geometry.customFixtureRisk === true
                ? planning.customFixtureAllowanceBeforeVat
            : geometry.weakGripRisk === true
                ? planning.softJawAllowanceBeforeVat
                : planning.standardWorkholdingAllowanceBeforeVat;
        var inspectionAllowanceBeforeVat = reviewReasons.indexOf('tight_tolerance') !== -1
            || reviewReasons.indexOf('non_default_tolerance') !== -1
            || reviewReasons.indexOf('special_surface_roughness') !== -1
            || reviewReasons.indexOf('enhanced_inspection') !== -1
            ? planning.standardToolingAllowanceBeforeVat
            : 0;

        var residual = reachPlan
            ? estimateResidual(reachPlan.geometry, reachResult, reachPlan.ignoredClusterIds, operations,
                removalVolumeMm3, fieldAccounting, reachPlan.unmachinableSampleIds)
            : {
                label: 'Estimated remaining material', camCertain: false, method: 'surface_only',
                voxelResolutionCap: 48, voxelResolution: boundedVoxelResolution(geometry), surfaceOnly: true,
                estimatedVolumeMm3: 0, cutterRadiusMm: 0, cutterLimitedSampleIds: [],
                cutterLimitedRegions: [], clusterIds: [], confidence: 'Low'
            };
        if (reachPlan) { confidence = lowerConfidence(confidence, residual.confidence); }
        if (residual.clusterIds.length > 0) {
            addReason(reviewReasons, 'estimated_residual_material');
            confidence = 'Low';
        }

        if (![roughingMinutes, finishingMinutes, fixedMinutes, totalMinutesPerPart,
            toolingAllowanceBeforeVat, workholdingAllowanceBeforeVat, inspectionAllowanceBeforeVat].every(finitePlanValue)
            || operations.some(function (operation) { return !finitePlanValue(operation.estimatedMinutes); })) {
            return malformedFallback(reviewReasons);
        }

        var result = {
            setups: setups,
            setupCount: setups.length,
            axialFluteClusterIds: reachPlan && Array.isArray(reachPlan.ignoredClusterIds)
                ? reachPlan.ignoredClusterIds.slice() : [],
            unmachinableSampleIds: reachPlan ? reachPlan.unmachinableSampleIds : [],
            unmachinableFieldAreaMm2: reachPlan ? reachPlan.unmachinableFieldAreaMm2 : 0,
            unmachinableFieldAreaRatio: reachPlan ? reachPlan.unmachinableFieldAreaRatio : 0,
            hasSignificantUnmachinableSurface: reachPlan ? reachPlan.hasSignificantUnmachinableSurface : true,
            operations: operations,
            deferredSurfaceSampleIds: deferredSurfaceSampleIds,
            reachMatrix: reachResult.records,
            fieldAccounting: fieldAccounting,
            removalVolumeMm3: removalVolumeMm3,
            finishingAreaMm2: finishingAreaMm2,
            residual: residual,
            fixtureStrategy: reachPlan && reachPlan.fixtureStrategy
                ? Object.assign({}, reachPlan.fixtureStrategy, { estimate: fixtureEstimate }) : null,
            roughingMinutes: roughingMinutes,
            finishingMinutes: finishingMinutes,
            batchMinutes: batchMinutes,
            cycleMinutesPerPart: cycleMinutesPerPart,
            fixedMinutes: fixedMinutes,
            totalMinutesPerPart: totalMinutesPerPart,
            estimatedMinutesPerPart: totalMinutesPerPart,
            toolingAllowanceBeforeVat: toolingAllowanceBeforeVat,
            toolingBeforeVat: toolingAllowanceBeforeVat,
            workholdingAllowanceBeforeVat: workholdingAllowanceBeforeVat,
            workholdingBeforeVat: workholdingAllowanceBeforeVat,
            inspectionAllowanceBeforeVat: inspectionAllowanceBeforeVat,
            confidence: confidence,
            reviewReasons: reviewReasons
        };
        result.notFullyMillable = geometry.notFullyMillable === true;
        result.machineCapability = machineCapability.classify(result);
        return result;
    }

    function commercializeValidatedPlan(input) {
        input = input || {};
        var validated = input.plan || {};
        if (validated.contract !== 'ValidatedManufacturingPlan.v1'
            || typeof validated.planHash !== 'string' || !validated.planHash.trim()
            || !validated.operationGraph || !validated.setupPlan) {
            throw planningContractError('cnc_validated_plan_required',
                'Commercial timing requires a sealed manufacturing plan.');
        }
        var geometry = input.geometry || {};
        var stock = input.stock || {};
        var alloy = input.material;
        var materialMrr = materialCatalog.adjustRate(alloy,
            planning.mrrMm3PerMinute['6061'], 'mrr');
        var finishRate = materialCatalog.adjustRate(alloy,
            planning.finishMm2PerMinute['6061'], 'finish');
        if (!isPositiveNumber(materialMrr) || !isPositiveNumber(finishRate)) {
            throw planningContractError('unsupported_alloy',
                'Commercial timing requires a supported material.');
        }
        var operations = (validated.operationGraph.operations || []).slice();
        var setups = (validated.setupPlan.setups || []).map(function (setup) {
            return Object.assign({}, setup);
        });
        var removalVolumeMm3 = Math.max(0, stockVolumeMm3(stock)
            - (isNonNegativeNumber(geometry.partVolumeMm3) ? geometry.partVolumeMm3 : 0));
        var roughOperationCount = operations.filter(function (operation) {
            return operation.phase === 'rough' || operation.kind === 'roughing'
                || operation.kind === 'facing' || operation.kind === 'bore_preparation';
        }).length;
        var finishOperationCount = operations.filter(function (operation) {
            return operation.phase === 'finish' || operation.kind === 'finishing'
                || operation.kind === 'ball_finishing' || operation.kind === 'chamfering'
                || operation.kind === 'thread_milling' || operation.kind === 'tapping';
        }).length;
        var roughingMinutes = roughOperationCount > 0 ? removalVolumeMm3 / materialMrr : 0;
        var finishingAreaMm2 = isNonNegativeNumber(geometry.partSurfaceAreaMm2)
            ? geometry.partSurfaceAreaMm2 : 0;
        var finishingMinutes = finishOperationCount > 0 ? finishingAreaMm2 / finishRate : 0;
        var explicitMachiningMinutes = setups.reduce(function (total, setup) {
            return total + (isNonNegativeNumber(setup.machiningMinutes) ? setup.machiningMinutes : 0);
        }, 0);
        var handlingMinutes = setups.reduce(function (total, setup) {
            return total + (isNonNegativeNumber(setup.handlingMinutes) ? setup.handlingMinutes : 0);
        }, 0);
        var batchMinutes = setups.length * (planning.setupMinutes + planning.probingMinutesPerSetup)
            + operations.length * planning.toolChangeMinutes;
        var cycleMinutesPerPart = ((roughingMinutes + finishingMinutes
            + explicitMachiningMinutes) / planning.utilizationFactor)
            + handlingMinutes + planning.handlingMinutesPerPart + planning.deburrMinutesPerPart;
        var tooling = materialCatalog.adjustRate(alloy,
            planning.standardToolingAllowanceBeforeVat, 'toolingWear');
        return {
            contract: validated.contract,
            planHash: validated.planHash,
            geometryRevision: validated.geometryRevision,
            requirementsRevision: validated.requirementsRevision,
            plannerVersion: validated.plannerVersion,
            toolLibraryVersion: validated.toolLibraryVersion,
            operations: operations,
            setups: setups,
            batchMinutes: batchMinutes,
            cycleMinutesPerPart: cycleMinutesPerPart,
            totalMinutesPerPart: batchMinutes + cycleMinutesPerPart,
            roughingMinutes: roughingMinutes,
            finishingMinutes: finishingMinutes,
            toolingAllowanceBeforeVat: isNonNegativeNumber(tooling) ? tooling
                : planning.standardToolingAllowanceBeforeVat,
            workholdingAllowanceBeforeVat: planning.standardWorkholdingAllowanceBeforeVat,
            inspectionAllowanceBeforeVat: 0,
            confidence: 'High',
            reviewReasons: []
        };
    }

    function validateManufacturingPlan(input) {
        if (!window.CncPlanValidator) {
            return Promise.resolve(Object.freeze({ valid: false,
                reviewReasons: Object.freeze(['plan_validator_required']) }));
        }
        return window.CncPlanValidator.validate(input);
    }

    /* CNC_TEST_INSTRUMENTATION_POINT */
    window.CncPlanning = Object.freeze({
        commercializeValidatedPlan: commercializeValidatedPlan,
        validateManufacturingPlan: validateManufacturingPlan
    });
})(window);
