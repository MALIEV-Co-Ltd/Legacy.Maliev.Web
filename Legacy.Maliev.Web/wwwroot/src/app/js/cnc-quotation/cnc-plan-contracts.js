(function (root) {
    'use strict';

    var FEATURE_KINDS = Object.freeze({
        datum: true,
        outside_profile: true,
        boss: true,
        pocket: true,
        slot: true,
        hole: true,
        chamfer: true,
        fillet: true,
        freeform_patch: true,
        internal_thread: true,
        external_thread: true,
        unresolved: true
    });
    var OPERATION_KINDS = Object.freeze({
        spot_drilling: true, drilling: true, tapping: true, thread_milling: true,
        bore_preparation: true, major_diameter_preparation: true,
        chamfering: true, roughing: true, finishing: true, facing: true,
        ball_end_finishing: true, deburring: true
    });
    var OPERATION_PHASES = Object.freeze({
        spot: true, drill: true, thread: true, rough: true, finish: true, deburr: true
    });
    var TOOL_CLASSES = Object.freeze({
        spot_drill: true, hss_drill: true, tap: true, thread_mill: true,
        flat_end_mill: true, chamfer_mill: true, face_mill: true,
        ball_end_mill: true, deburring_tool: true
    });
    var OPERATION_TEMPLATES = Object.freeze({
        spot_drilling: Object.freeze({ phase: 'spot', toolClass: 'spot_drill' }),
        drilling: Object.freeze({ phase: 'drill', toolClass: 'hss_drill' }),
        tapping: Object.freeze({ phase: 'thread', toolClass: 'tap' }),
        thread_milling: Object.freeze({ phase: 'thread', toolClass: 'thread_mill' }),
        bore_preparation: Object.freeze({ phase: 'drill', toolClass: 'flat_end_mill' }),
        major_diameter_preparation: Object.freeze({ phase: 'rough', toolClass: 'flat_end_mill' }),
        chamfering: Object.freeze({ phase: 'finish', toolClass: 'chamfer_mill' }),
        roughing: Object.freeze({ phase: 'rough', toolClass: 'flat_end_mill' }),
        finishing: Object.freeze({ phase: 'finish', toolClass: 'flat_end_mill' }),
        facing: Object.freeze({ phase: 'rough', toolClass: 'face_mill' }),
        ball_end_finishing: Object.freeze({ phase: 'finish', toolClass: 'ball_end_mill' }),
        deburring: Object.freeze({ phase: 'deburr', toolClass: 'deburring_tool' })
    });
    var FEATURE_OPERATION_KINDS = Object.freeze({
        datum: Object.freeze({ facing: true }),
        outside_profile: Object.freeze({ roughing: true, finishing: true }),
        boss: Object.freeze({ roughing: true, finishing: true }),
        pocket: Object.freeze({ roughing: true, finishing: true }),
        slot: Object.freeze({ roughing: true, finishing: true }),
        hole: Object.freeze({ spot_drilling: true, drilling: true, bore_preparation: true }),
        chamfer: Object.freeze({ chamfering: true }),
        fillet: Object.freeze({ ball_end_finishing: true }),
        freeform_patch: Object.freeze({ ball_end_finishing: true }),
        internal_thread: Object.freeze({ spot_drilling: true, drilling: true,
            bore_preparation: true, tapping: true, thread_milling: true }),
        external_thread: Object.freeze({ major_diameter_preparation: true,
            chamfering: true, thread_milling: true }),
        unresolved: Object.freeze({})
    });
    var LEGACY_EVIDENCE_FIELDS = Object.freeze({
        cluster: true, clusterid: true, clusterids: true, featureclusterids: true,
        sample: true, sampleid: true, sampleids: true, featuresampleids: true,
        sourcetriangleindex: true, sourcetriangleindexes: true, triangleindexes: true,
        operationcodes: true, curvedfinishingbydirection: true
    });

    function canonicalize(value) {
        if (Array.isArray(value)) { return value.map(canonicalize); }
        if (value && typeof value === 'object') {
            return Object.keys(value).sort().reduce(function (result, key) {
                if (value[key] !== undefined) { result[key] = canonicalize(value[key]); }
                return result;
            }, {});
        }
        if (typeof value === 'number' && !Number.isFinite(value)) {
            throw new Error('CNC plan contracts require finite numbers.');
        }
        return value;
    }

    async function hash(value) {
        var bytes = new TextEncoder().encode(JSON.stringify(canonicalize(value)));
        var digest = await crypto.subtle.digest('SHA-256', bytes);
        return Array.from(new Uint8Array(digest), function (b) {
            return b.toString(16).padStart(2, '0');
        }).join('');
    }

    function contractError(code, message) {
        var error = new Error(code + ': ' + message);
        error.code = code;
        return error;
    }

    function values(value) {
        return Array.isArray(value) ? value : [];
    }

    function hasText(value) {
        return typeof value === 'string' && value.trim().length > 0;
    }

    function isPlainObject(value) {
        return value && typeof value === 'object' && !Array.isArray(value);
    }

    function positiveNumber(value) {
        value = Number(value);
        return Number.isFinite(value) && value > 0 ? value : 0;
    }

    function featureDimension(feature, name) {
        return positiveNumber(feature && (feature[name] !== undefined
            ? feature[name] : feature.dimensions && feature.dimensions[name]));
    }

    function hasThreadIdentity(feature) {
        return hasText(feature && feature.threadDesignation)
            && featureDimension(feature, 'nominalDiameterMm') > 0
            && featureDimension(feature, 'pitchMm') > 0;
    }

    function isAllowedCompilerUnresolvedReason(feature, reason) {
        if (!feature || !hasText(reason)) { return false; }
        var widthMm = featureDimension(feature, 'widthMm')
            || featureDimension(feature, 'corridorWidthMm');
        var depthMm = featureDimension(feature, 'depthMm');
        var cornerRadiusMm = featureDimension(feature, 'internalCornerRadiusMm');
        var nominalDiameterMm = featureDimension(feature, 'nominalDiameterMm');
        var threadIdentity = hasThreadIdentity(feature);

        if (feature.kind === 'datum') { return reason === 'unsupported_face_mill'
            || reason === 'datum_facing_evidence_required'; }
        if (feature.kind === 'chamfer') { return reason === 'unsupported_chamfer_mill'; }
        if (feature.kind === 'hole') {
            var holeDiameterMm = featureDimension(feature, 'diameterMm');
            return (!holeDiameterMm && reason === 'hole_diameter_required')
                || (holeDiameterMm && !depthMm && reason === 'hole_depth_required')
                || (holeDiameterMm && depthMm && !holeDepthConsistent(feature) && reason === 'hole_depth_mismatch')
                || (holeDiameterMm && (reason === 'unsupported_spot_drill'
                    || reason === 'unsupported_hss_drill'));
        }
        if (feature.kind === 'slot' || feature.kind === 'pocket'
            || feature.kind === 'outside_profile' || feature.kind === 'boss') {
            if (!widthMm || !depthMm) { return reason === 'prismatic_envelope_required'; }
            if (reason === 'prismatic_corner_envelope_required') { return true; }
            if (reason === 'unsupported_prismatic_tool') { return true; }
            if (feature.restMaterialRequired === true && !cornerRadiusMm) {
                return reason === 'prismatic_rest_envelope_required';
            }
            return (cornerRadiusMm > 0 || feature.restMaterialRequired === true)
                && reason === 'unsupported_prismatic_rest_tool';
        }
        if (feature.kind === 'fillet' || feature.kind === 'freeform_patch') {
            if (feature.certification !== 'non_prismatic_curvature') {
                return reason === 'uncertified_freeform_feature';
            }
            var radiusMm = featureDimension(feature, 'radiusMm');
            var requiredReachMm = featureDimension(feature, 'requiredReachMm') || depthMm;
            return (!radiusMm || !depthMm || !requiredReachMm)
                ? reason === 'certified_curvature_envelope_required'
                : reason === 'unsupported_ball_end_mill';
        }
        if (feature.kind === 'internal_thread') {
            if (!threadIdentity) { return reason === 'thread_designation_required'; }
            if (reason === 'unsupported_spot_drill') { return true; }
            return nominalDiameterMm <= 12
                ? reason === 'unsupported_hss_drill' || reason === 'unsupported_tap'
                : reason === 'unsupported_bore_preparation_tool'
                    || reason === 'unsupported_thread_mill';
        }
        if (feature.kind === 'external_thread') {
            if (!threadIdentity) { return reason === 'thread_designation_required'; }
            if (typeof feature.majorDiameterPreparationRequired !== 'boolean'
                || typeof feature.leadInChamferRequired !== 'boolean') {
                return reason === 'external_thread_process_state_required';
            }
            if (feature.majorDiameterPreparationRequired && !depthMm) {
                return reason === 'external_thread_preparation_envelope_required';
            }
            if (feature.majorDiameterPreparationRequired
                && reason === 'unsupported_major_diameter_preparation_tool') { return true; }
            var includedAngleDegrees = featureDimension(feature, 'includedAngleDegrees');
            if (feature.leadInChamferRequired && !includedAngleDegrees) {
                return reason === 'external_thread_lead_in_envelope_required';
            }
            if (feature.leadInChamferRequired && reason === 'unsupported_chamfer_mill') { return true; }
            return reason === 'unsupported_thread_mill';
        }
        return false;
    }

    function validateTopology(topology) {
        topology = topology || {};
        if (topology.contract !== 'CncCadTopology.v1' || !hasText(topology.revision)) {
            throw contractError('revision_mismatch', 'Topology must use CncCadTopology.v1 with a non-empty revision.');
        }
        return topology;
    }

    function validateFeatureGraph(graph) {
        graph = graph || {};
        if (graph.contract !== 'ManufacturingFeatureGraph.v1' || !hasText(graph.topologyRevision)) {
            throw contractError('revision_mismatch', 'Feature graph must use ManufacturingFeatureGraph.v1 with a non-empty topology revision.');
        }
        var features = values(graph.features);
        var featureIds = Object.create(null);
        var faceOwners = Object.create(null);
        features.forEach(function (feature) {
            feature = feature || {};
            if (!FEATURE_KINDS[feature.kind]) {
                throw contractError('unknown_feature_kind', 'Feature ' + String(feature.id) + ' has unknown kind ' + String(feature.kind) + '.');
            }
            if (!hasText(feature.id) || featureIds[feature.id]) {
                throw contractError('broken_feature_reference', 'Feature IDs must be unique and non-empty.');
            }
            featureIds[feature.id] = true;
            var primaryFaceIds = feature.primaryFaceIds;
            if (feature.kind !== 'unresolved'
                && (!Array.isArray(primaryFaceIds) || primaryFaceIds.length === 0)) {
                throw contractError('broken_feature_reference', 'Resolved machinable feature '
                    + String(feature.id) + ' must own at least one primary face.');
            }
            values(primaryFaceIds).forEach(function (faceId) {
                if (!hasText(faceId)) {
                    throw contractError('broken_feature_reference', 'Primary face IDs must be non-empty text.');
                }
                if (faceOwners[faceId]) {
                    throw contractError('duplicate_primary_face_owner', 'Face ' + String(faceId) + ' is already owned by ' + faceOwners[faceId] + '.');
                }
                faceOwners[faceId] = feature.id;
            });
        });
        features.forEach(function (feature) {
            values(feature.secondaryFeatureIds).forEach(function (featureId) {
                if (!hasText(featureId) || !featureIds[featureId]) {
                    throw contractError('broken_feature_reference', 'Feature ' + String(feature.id) + ' references missing feature ' + String(featureId) + '.');
                }
            });
        });
        if (!Array.isArray(graph.machinableFaceIds) || !isPlainObject(graph.faceOwners)) {
            throw contractError('broken_feature_reference', 'Feature graph must declare exact machinable face ownership.');
        }
        var machinableFaceIds = Object.create(null);
        graph.machinableFaceIds.forEach(function (faceId) {
            if (!hasText(faceId) || machinableFaceIds[faceId]) {
                throw contractError('broken_feature_reference', 'Machinable face IDs must be unique and non-empty.');
            }
            machinableFaceIds[faceId] = true;
        });
        var declaredFaceIds = Object.keys(graph.faceOwners);
        if (declaredFaceIds.length !== graph.machinableFaceIds.length
            || Object.keys(faceOwners).length !== graph.machinableFaceIds.length) {
            throw contractError('broken_feature_reference', 'Every machinable face must have exactly one feature owner.');
        }
        graph.machinableFaceIds.forEach(function (faceId) {
            var owner = graph.faceOwners[faceId];
            if (!hasText(owner) || !featureIds[owner] || faceOwners[faceId] !== owner) {
                throw contractError('broken_feature_reference', 'Face ' + String(faceId) + ' has inconsistent feature ownership.');
            }
        });
        declaredFaceIds.forEach(function (faceId) {
            if (!machinableFaceIds[faceId] || faceOwners[faceId] !== graph.faceOwners[faceId]) {
                throw contractError('broken_feature_reference', 'Face ownership contains an undeclared or inconsistent face.');
            }
        });
        return graph;
    }

    function operationIdentity(operation) {
        return JSON.stringify(canonicalize({
            featureId: operation.featureId,
            phase: operation.phase,
            toolConstraints: operation.toolConstraints || {}
        }));
    }

    function containsLegacyEvidence(value, visited) {
        if (!value || typeof value !== 'object') { return false; }
        visited = visited || new Set();
        if (visited.has(value)) { return false; }
        visited.add(value);
        return Object.keys(value).some(function (key) {
            return LEGACY_EVIDENCE_FIELDS[key.toLowerCase()] || containsLegacyEvidence(value[key], visited);
        });
    }

    function hasRecursivePredecessorKind(operation, operationsById, kind, featureId, visited) {
        visited = visited || new Set();
        if (!operation || visited.has(operation.id)) { return false; }
        visited.add(operation.id);
        return values(operation.predecessors).some(function (predecessorId) {
            var predecessor = operationsById[predecessorId];
            return predecessor && predecessor.featureId === featureId
                && (predecessor.kind === kind
                    || hasRecursivePredecessorKind(predecessor, operationsById, kind, featureId,
                        new Set(visited)));
        });
    }

    function validateOperationGraph(graph, featureGraph) {
        graph = graph || {};
        if (graph.contract !== 'MachiningOperationGraph.v1' || !hasText(graph.topologyRevision)) {
            throw contractError('revision_mismatch', 'Operation graph must use MachiningOperationGraph.v1 with a non-empty topology revision.');
        }
        var operations = values(graph.operations);
        var operationIds = Object.create(null);
        var operationIdentities = Object.create(null);
        var strictFeaturesById = null;
        if (featureGraph) {
            validateFeatureGraph(featureGraph);
            if (featureGraph.topologyRevision !== graph.topologyRevision) {
                throw contractError('revision_mismatch', 'Feature and operation graphs must share one topology revision.');
            }
            strictFeaturesById = Object.create(null);
            values(featureGraph.features).forEach(function (feature) { strictFeaturesById[feature.id] = feature; });
            if (!hasText(graph.toolLibraryVersion) || containsLegacyEvidence(graph)
                || containsLegacyEvidence(featureGraph)) {
                throw contractError('broken_feature_reference', 'Operation graph metadata must be feature-derived and cluster-free.');
            }
        }
        operations.forEach(function (operation) {
            operation = operation || {};
            if (strictFeaturesById) {
                var provenance = operation.provenance;
                var provenanceKeys = provenance && typeof provenance === 'object'
                    ? Object.keys(provenance).sort() : [];
                var owner = strictFeaturesById[operation.featureId];
                var operationTemplate = OPERATION_TEMPLATES[operation.kind];
                if (!hasText(operation.id) || !hasText(operation.featureId)
                    || !owner || !OPERATION_KINDS[operation.kind]
                    || !FEATURE_OPERATION_KINDS[owner.kind][operation.kind]
                    || !OPERATION_PHASES[operation.phase] || !TOOL_CLASSES[operation.toolClass]
                    || !operationTemplate || operation.phase !== operationTemplate.phase
                    || operation.toolClass !== operationTemplate.toolClass
                    || !operation.toolConstraints || typeof operation.toolConstraints !== 'object'
                    || Array.isArray(operation.toolConstraints) || !Array.isArray(operation.predecessors)
                    || !Object.prototype.hasOwnProperty.call(operation, 'accessAxis')
                    || provenanceKeys.join('|') !== 'featureId|sourceContract'
                    || provenance.sourceContract !== 'ManufacturingFeatureGraph.v1'
                    || provenance.featureId !== operation.featureId || containsLegacyEvidence(operation)) {
                    throw contractError('broken_feature_reference', 'Operation ' + String(operation.id)
                        + ' must contain only complete ManufacturingFeatureGraph.v1 provenance.');
                }
            }
            var identity = operationIdentity(operation);
            if (!hasText(operation.id) || !hasText(operation.featureId)) {
                throw contractError('broken_feature_reference',
                    'Operation identifiers must be non-empty text.');
            }
            if (operationIds[operation.id] || operationIdentities[identity]) {
                throw contractError('duplicate_operation', 'Operation ' + String(operation.id) + ' duplicates a feature-phase-tool operation.');
            }
            operationIds[operation.id] = operation;
            operationIdentities[identity] = operation.id;
        });
        operations.forEach(function (operation) {
            var predecessorIds = Object.create(null);
            values(operation.predecessors).forEach(function (predecessorId) {
                if (!operationIds[predecessorId] || predecessorId === operation.id) {
                    throw contractError('broken_predecessor', 'Operation ' + String(operation.id) + ' references missing predecessor ' + String(predecessorId) + '.');
                }
                var crossFeatureProfilePreparation = strictFeaturesById
                    && operation.kind === 'thread_milling'
                    && strictFeaturesById[operation.featureId].kind === 'external_thread'
                    && operationIds[predecessorId].kind === 'finishing'
                    && strictFeaturesById[operationIds[predecessorId].featureId].kind === 'outside_profile';
                if (predecessorIds[predecessorId]
                    || (strictFeaturesById && operationIds[predecessorId].featureId !== operation.featureId
                        && !crossFeatureProfilePreparation)) {
                    throw contractError('broken_predecessor', 'Operation ' + String(operation.id)
                        + ' predecessors must be unique and belong to the same feature.');
                }
                predecessorIds[predecessorId] = true;
            });
        });
        if (strictFeaturesById) {
            operations.forEach(function (operation) {
                if (operation.kind === 'finishing'
                    && !hasRecursivePredecessorKind(operation, operationIds, 'roughing', operation.featureId)) {
                    throw contractError('broken_predecessor', 'Finishing operation ' + String(operation.id)
                        + ' must follow roughing for the same feature.');
                }
            });
            validateFeatureOperations(featureGraph, graph);
        }
        return graph;
    }

    function withoutPlanHash(plan) {
        return Object.keys(plan).reduce(function (result, key) {
            if (key !== 'planHash') { result[key] = plan[key]; }
            return result;
        }, {});
    }

    function operationToolClass(operation) {
        return operation.toolClass || (operation.toolConstraints && operation.toolConstraints.toolClass) || '';
    }

    function isBallEndOperation(operation) {
        return operationToolClass(operation) === 'ball_end_mill' || operation.kind === 'ball_end_finishing';
    }

    function isCylindricalFeature(feature) {
        var surface = feature.surface || {};
        return feature.kind === 'hole' || feature.surfaceKind === 'cylinder'
            || feature.geometryKind === 'cylinder' || surface.kind === 'cylinder';
    }

    function hasPredecessorKind(operation, operationsById, kind) {
        return values(operation.predecessors).some(function (predecessorId) {
            var predecessor = operationsById[predecessorId];
            return predecessor.kind === kind && predecessor.featureId === operation.featureId;
        });
    }

    function operationOfKind(operations, kind) {
        return operations.filter(function (operation) { return operation.kind === kind; })[0] || null;
    }

    function exactPredecessors(operation, expected) {
        var actual = values(operation && operation.predecessors);
        if (actual.length !== expected.length || actual.some(function (id, index) { return id !== expected[index]; })) {
            throw contractError('broken_predecessor', 'Operation ' + String(operation && operation.id)
                + ' does not have its exact feature process predecessors.');
        }
    }

    function validatePrismaticChain(feature, operations) {
        var rough = operations.filter(function (operation) { return operation.kind === 'roughing'; });
        var finish = operations.filter(function (operation) { return operation.kind === 'finishing'; });
        var byBasis = function (items, basis) {
            return items.filter(function (operation) {
                return operation.toolConstraints && operation.toolConstraints.constraintBasis === basis;
            });
        };
        var roughBulk = byBasis(rough, 'feature_corridor');
        var roughRest = byBasis(rough, 'proven_internal_corner');
        var finishBulk = byBasis(finish, 'feature_corridor');
        var finishRest = byBasis(finish, 'proven_internal_corner');
        if (roughBulk.length !== 1 || finishBulk.length !== 1 || rough.length !== finish.length
            || rough.length < 1 || rough.length > 2 || roughRest.length !== finishRest.length
            || roughRest.length > 1 || finishRest.length > 1) {
            throw contractError('broken_feature_reference', 'Prismatic feature ' + String(feature.id)
                + ' must use one bulk band and at most one matching rest band per phase.');
        }
        exactPredecessors(roughBulk[0], []);
        if (roughRest.length) { exactPredecessors(roughRest[0], [roughBulk[0].id]); }
        exactPredecessors(finishBulk[0], [(roughRest[0] || roughBulk[0]).id]);
        if (finishRest.length) { exactPredecessors(finishRest[0], [finishBulk[0].id]); }
    }

    function countKind(operations, kind) {
        return operations.filter(function (operation) { return operation.kind === kind; }).length;
    }

    function sameNumber(left, right) {
        return positiveNumber(left) > 0 && Math.abs(Number(left) - Number(right)) <= 1e-6;
    }

    function validateThreadIdentity(feature, operation) {
        var constraints = operation && operation.toolConstraints || {};
        var nominalDiameterMm = featureDimension(feature, 'nominalDiameterMm');
        var pitchMm = featureDimension(feature, 'pitchMm');
        if (!hasText(feature.threadDesignation) || !nominalDiameterMm || !pitchMm
            || constraints.threadDesignation !== feature.threadDesignation
            || !sameNumber(constraints.nominalDiameterMm, nominalDiameterMm)
            || !sameNumber(constraints.pitchMm, pitchMm)) {
            throw contractError('broken_feature_reference', 'Thread ' + String(feature.id)
                + ' requires a matching designation, nominal diameter, and pitch.');
        }
    }

    function validateFeatureOperations(featureGraph, operationGraph) {
        var featuresById = Object.create(null);
        var operationsById = Object.create(null);
        var operationsByFeatureId = Object.create(null);
        values(featureGraph.features).forEach(function (feature) {
            featuresById[feature.id] = feature;
        });
        values(operationGraph.operations).forEach(function (operation) {
            if (!featuresById[operation.featureId]) {
                throw contractError('broken_feature_reference', 'Operation ' + String(operation.id) + ' references missing feature ' + String(operation.featureId) + '.');
            }
            operationsById[operation.id] = operation;
            (operationsByFeatureId[operation.featureId] || (operationsByFeatureId[operation.featureId] = [])).push(operation);
        });

        values(operationGraph.operations).forEach(function (operation) {
            var feature = featuresById[operation.featureId];
            if (isBallEndOperation(operation)) {
                var ballConstraints = operation.toolConstraints || {};
                var radiusMm = featureDimension(feature, 'radiusMm');
                var depthMm = featureDimension(feature, 'depthMm');
                var requiredReachMm = featureDimension(feature, 'requiredReachMm') || depthMm;
                if ((feature.kind !== 'fillet' && feature.kind !== 'freeform_patch')
                    || feature.certification !== 'non_prismatic_curvature'
                    || ballConstraints.certification !== 'non_prismatic_curvature'
                    || !radiusMm || !depthMm || !requiredReachMm
                    || !positiveNumber(ballConstraints.maximumDiameterMm)
                    || Number(ballConstraints.maximumDiameterMm) > radiusMm * 2 + 1e-6
                    || positiveNumber(ballConstraints.minimumReachMm) < requiredReachMm
                    || (depthMm && positiveNumber(ballConstraints.minimumCutLengthMm) < depthMm)) {
                    throw contractError('broken_feature_reference', 'Feature ' + String(feature.id)
                        + ' lacks certified non-prismatic curvature and matching ball-tool envelope evidence.');
                }
            }
            if (operation.kind === 'drilling' && operationToolClass(operation) === 'hss_drill'
                && !hasPredecessorKind(operation, operationsById, 'spot_drilling')) {
                throw contractError('broken_predecessor', 'HSS drilling operation ' + String(operation.id) + ' must follow spot drilling.');
            }
            if (operation.kind === 'bore_preparation' && feature.kind === 'internal_thread'
                && !hasPredecessorKind(operation, operationsById, 'spot_drilling')) {
                throw contractError('broken_predecessor', 'Internal bore preparation ' + String(operation.id) + ' must follow spot drilling.');
            }
            if (operation.kind === 'tapping' && feature.kind === 'internal_thread'
                && !hasPredecessorKind(operation, operationsById, 'drilling')) {
                throw contractError('broken_predecessor', 'Internal thread operation ' + String(operation.id) + ' must follow drilling.');
            }
            if (operation.kind === 'thread_milling' && feature.kind === 'internal_thread'
                && featureDimension(feature, 'nominalDiameterMm') > 12
                && !hasPredecessorKind(operation, operationsById, 'drilling')
                && !hasPredecessorKind(operation, operationsById, 'bore_preparation')) {
                throw contractError('broken_predecessor', 'Internal thread operation ' + String(operation.id) + ' must follow drilling or bore preparation.');
            }
        });

        var featureGraphUnresolved = values(featureGraph.unresolved);
        var unresolvedByFeatureId = Object.create(null);
        values(operationGraph.unresolved).forEach(function (item) {
            if (!isPlainObject(item) || !hasText(item.reason)) {
                throw contractError('broken_feature_reference',
                    'Operation unresolved reasons must be required canonical compiler outcomes.');
            }
            var feature = hasText(item.featureId) ? featuresById[item.featureId] : null;
            var inherited = featureGraphUnresolved.some(function (source) {
                return isPlainObject(source) && source.featureId === item.featureId
                    && source.reason === item.reason && source.required === item.required;
            });
            if (inherited) { return; }
            if (!feature) {
                throw contractError('broken_feature_reference',
                    'Operation unresolved reason references a missing feature.');
            }
            if (feature.kind === 'unresolved') {
                if (item.required !== true || item.reason !== (feature.unresolvedReason || 'unresolved_feature')
                    || unresolvedByFeatureId[feature.id]) {
                    throw contractError('broken_feature_reference', 'Unresolved feature '
                        + String(feature.id) + ' must retain its canonical recognition reason.');
                }
                unresolvedByFeatureId[feature.id] = item;
                return;
            }
            if (item.required !== true || !isAllowedCompilerUnresolvedReason(feature, item.reason)
                || unresolvedByFeatureId[feature.id]) {
                throw contractError('broken_feature_reference', 'Feature ' + String(feature.id)
                    + ' has an invalid or duplicate compiler unresolved reason ' + String(item.reason) + '.');
            }
            unresolvedByFeatureId[feature.id] = item;
        });

        if (values(operationGraph.operations).length === 0 && featureGraphUnresolved.some(function (item) {
            return item && item.required !== false;
        })) { return; }

        values(featureGraph.features).forEach(function (feature) {
            var operations = operationsByFeatureId[feature.id] || [];
            var hasRequiredUnresolvedReason = !!unresolvedByFeatureId[feature.id];
            if (operations.length && hasRequiredUnresolvedReason) {
                throw contractError('broken_feature_reference', 'Feature ' + String(feature.id)
                    + ' cannot contain both operations and an unresolved compiler outcome.');
            }
            if (!operations.length && (feature.kind === 'unresolved' || hasRequiredUnresolvedReason
                || feature.machiningRequired === false || feature.kind === 'datum' && feature.machiningRequired !== true)) {
                return;
            }
            if (!operations.length) {
                throw contractError('broken_feature_reference', 'Resolved operation-producing feature '
                    + String(feature.id) + ' must compile its complete operation template.');
            }
            if (feature.kind === 'hole') {
                var holeSpot = operations.filter(function (operation) { return operation.kind === 'spot_drilling'; });
                var holeDrill = operations.filter(function (operation) { return operation.kind === 'drilling'; });
                if (holeSpot.length !== 1 || holeDrill.length !== 1 || operations.length !== 2) {
                    throw contractError('broken_feature_reference', 'Hole ' + String(feature.id)
                        + ' requires exactly Spot and HSS Drill.');
                }
                exactPredecessors(holeSpot[0], []);
                exactPredecessors(holeDrill[0], [holeSpot[0].id]);
                var holeDepth = featureDimension(feature, 'depthMm'), holeConstraints = holeDrill[0].toolConstraints || {};
                if (!holeDepth || !holeDepthConsistent(feature) || positiveNumber(holeConstraints.minimumCutLengthMm) < holeDepth
                    || positiveNumber(holeConstraints.minimumReachMm) < holeDepth) {
                    throw contractError('broken_feature_reference', 'Hole ' + String(feature.id)
                        + ' requires cutting length and reach constraints covering its finite depth.');
                }
            }
            if (feature.kind === 'slot' || feature.kind === 'pocket'
                || feature.kind === 'outside_profile' || feature.kind === 'boss') {
                validatePrismaticChain(feature, operations);
            }
            if (feature.kind === 'datum') {
                var facing = operationOfKind(operations, 'facing');
                if (!facing || operations.length !== 1) {
                    throw contractError('broken_feature_reference', 'Datum ' + String(feature.id)
                        + ' must compile only one facing operation.');
                }
                exactPredecessors(facing, []);
            }
            if (feature.kind === 'chamfer') {
                var chamfer = operationOfKind(operations, 'chamfering');
                if (!chamfer || operations.length !== 1) {
                    throw contractError('broken_feature_reference', 'Chamfer ' + String(feature.id)
                        + ' must compile only one chamfering operation.');
                }
                exactPredecessors(chamfer, []);
            }
            if (feature.kind === 'fillet' || feature.kind === 'freeform_patch') {
                var ballFinish = operationOfKind(operations, 'ball_end_finishing');
                if (!ballFinish || operations.length !== 1) {
                    throw contractError('broken_feature_reference', 'Curvature feature ' + String(feature.id)
                        + ' must compile only one certified ball finishing operation.');
                }
                exactPredecessors(ballFinish, []);
            }
            if (feature.kind === 'internal_thread') {
                var diameterMm = featureDimension(feature, 'nominalDiameterMm');
                var tapCount = countKind(operations, 'tapping');
                var threadMillCount = countKind(operations, 'thread_milling');
                var spotCount = countKind(operations, 'spot_drilling');
                var drillingCount = countKind(operations, 'drilling');
                var boreCount = countKind(operations, 'bore_preparation');
                if (diameterMm <= 12) {
                    if (!diameterMm || tapCount !== 1 || threadMillCount !== 0
                        || spotCount !== 1 || drillingCount !== 1 || boreCount !== 0) {
                        throw contractError('broken_feature_reference', 'Internal thread ' + String(feature.id)
                            + ' through M12 requires exactly Spot, Drill, and Tap.');
                    }
                    validateThreadIdentity(feature, operations.filter(function (operation) {
                        return operation.kind === 'tapping';
                    })[0]);
                    var internalSpot = operationOfKind(operations, 'spot_drilling');
                    var internalDrill = operationOfKind(operations, 'drilling');
                    var internalTap = operationOfKind(operations, 'tapping');
                    exactPredecessors(internalSpot, []);
                    exactPredecessors(internalDrill, [internalSpot.id]);
                    exactPredecessors(internalTap, [internalDrill.id]);
                } else {
                    if (threadMillCount !== 1 || tapCount !== 0 || spotCount !== 1
                        || drillingCount !== 0 || boreCount !== 1) {
                        throw contractError('broken_feature_reference', 'Internal thread ' + String(feature.id)
                            + ' above M12 requires exactly Spot, Bore Preparation, and Thread Milling.');
                    }
                    validateThreadIdentity(feature, operations.filter(function (operation) {
                        return operation.kind === 'thread_milling';
                    })[0]);
                    var largeInternalSpot = operationOfKind(operations, 'spot_drilling');
                    var internalBore = operationOfKind(operations, 'bore_preparation');
                    var internalThreadMill = operationOfKind(operations, 'thread_milling');
                    exactPredecessors(largeInternalSpot, []);
                    exactPredecessors(internalBore, [largeInternalSpot.id]);
                    exactPredecessors(internalThreadMill, [internalBore.id]);
                }
            }
            if (feature.kind === 'external_thread') {
                var externalThreadMills = operations.filter(function (operation) {
                    return operation.kind === 'thread_milling';
                });
                if (typeof feature.majorDiameterPreparationRequired !== 'boolean'
                    || typeof feature.leadInChamferRequired !== 'boolean'
                    || externalThreadMills.length !== 1 || countKind(operations, 'tapping') !== 0) {
                    throw contractError('broken_feature_reference', 'External thread ' + String(feature.id)
                        + ' requires explicit preparation flags and one thread-milling operation.');
                }
                var externalThreadMill = externalThreadMills[0];
                validateThreadIdentity(feature, externalThreadMill);
                var externalConstraints = externalThreadMill.toolConstraints || {};
                if (externalConstraints.majorDiameterPreparationRequired !== feature.majorDiameterPreparationRequired
                    || externalConstraints.leadInChamferRequired !== feature.leadInChamferRequired) {
                    throw contractError('broken_feature_reference', 'External thread process flags must match feature evidence.');
                }
                var majorPreparationCount = countKind(operations, 'major_diameter_preparation');
                var chamferCount = countKind(operations, 'chamfering');
                if ((feature.majorDiameterPreparationRequired
                        && (majorPreparationCount !== 1 || !hasRecursivePredecessorKind(externalThreadMill,
                            operationsById, 'major_diameter_preparation', feature.id)))
                    || (!feature.majorDiameterPreparationRequired && majorPreparationCount !== 0)
                    || (feature.leadInChamferRequired
                        && (chamferCount !== 1 || !hasRecursivePredecessorKind(externalThreadMill,
                            operationsById, 'chamfering', feature.id)))
                    || (!feature.leadInChamferRequired && chamferCount !== 0)) {
                    throw contractError('broken_predecessor', 'External thread ' + String(feature.id)
                        + ' does not satisfy its required preparation and lead-in chain.');
                }
                var externalPreparation = operationOfKind(operations, 'major_diameter_preparation');
                var externalChamfer = operationOfKind(operations, 'chamfering');
                if (externalPreparation) { exactPredecessors(externalPreparation, []); }
                if (externalChamfer) {
                    exactPredecessors(externalChamfer, externalPreparation ? [externalPreparation.id] : []);
                }
                var requiredExternalPredecessors = externalChamfer ? [externalChamfer.id]
                    : externalPreparation ? [externalPreparation.id] : [];
                var actualExternalPredecessors = values(externalThreadMill.predecessors);
                if (requiredExternalPredecessors.some(function (id) { return actualExternalPredecessors.indexOf(id) < 0; })
                    || actualExternalPredecessors.some(function (id) {
                        if (requiredExternalPredecessors.indexOf(id) >= 0) { return false; }
                        var predecessor = operationsById[id];
                        return !predecessor || predecessor.kind !== 'finishing'
                            || !featuresById[predecessor.featureId]
                            || featuresById[predecessor.featureId].kind !== 'outside_profile';
                    })) {
                    throw contractError('broken_predecessor', 'External thread ' + String(feature.id)
                        + ' has an invalid profile preparation dependency.');
                }
            }
        });
    }

    function validatePlan(plan) {
        plan = plan || {};
        if (plan.contract !== 'ValidatedManufacturingPlan.v1' || !hasText(plan.geometryRevision)
            || !hasText(plan.requirementsRevision) || plan.plannerVersion !== 'cnc-feature-planner-v3'
            || !hasText(plan.toolLibraryVersion)) {
            throw contractError('revision_mismatch',
                'Plan must use current non-empty manufacturing revisions and versions.');
        }
        var topology = validateTopology(plan.topology);
        var featureGraph = plan.featureGraph || {};
        var operationGraph = plan.operationGraph || {};
        var setupPlan = plan.setupPlan || {};
        var geometryRevision = plan.geometryRevision;
        validateFeatureGraph(featureGraph);
        validateOperationGraph(operationGraph, featureGraph);

        if (setupPlan.contract !== 'SetupPlan.v1' || !hasText(setupPlan.geometryRevision)
            || topology.revision !== geometryRevision || featureGraph.topologyRevision !== geometryRevision
            || operationGraph.topologyRevision !== geometryRevision || setupPlan.geometryRevision !== geometryRevision
            || operationGraph.toolLibraryVersion !== plan.toolLibraryVersion
            || (root.CncToolLibrary && root.CncToolLibrary.version !== plan.toolLibraryVersion)) {
            throw contractError('revision_mismatch', 'Plan contracts must share geometry revision ' + String(geometryRevision) + '.');
        }

        var unresolved = values(featureGraph.unresolved)
            .concat(values(operationGraph.unresolved), values(plan.unresolvedReasons));
        var hasRequiredUnresolved = unresolved.some(function (item) {
            return !item || typeof item !== 'object' || item.required !== false;
        }) || values(featureGraph.features).some(function (feature) {
            return feature.kind === 'unresolved' && feature.required !== false;
        });
        if (hasRequiredUnresolved) {
            throw contractError('unresolved_required_feature', 'The plan contains unresolved required feature evidence.');
        }

        var assigned = Object.create(null);
        var operationIdSet = Object.create(null);
        var setupIdSet = Object.create(null);
        var hasEmptySetup = false;
        values(operationGraph.operations).forEach(function (operation) { operationIdSet[operation.id] = true; });
        values(setupPlan.setups).forEach(function (setup) {
            if (!setup || !hasText(setup.id) || setupIdSet[setup.id]) {
                throw contractError('broken_feature_reference', 'Setup IDs must be unique and non-empty text.');
            }
            setupIdSet[setup.id] = true;
            var setupOperationIds = values(setup && setup.operationIds);
            if (setupOperationIds.length === 0) { hasEmptySetup = true; }
            setupOperationIds.forEach(function (operationId) {
                if (!hasText(operationId) || !operationIdSet[operationId]) {
                    throw contractError('broken_feature_reference', 'Setup ' + String(setup && setup.id) + ' references missing operation ' + String(operationId) + '.');
                }
                assigned[operationId] = (assigned[operationId] || 0) + 1;
            });
        });
        values(operationGraph.operations).forEach(function (operation) {
            if (assigned[operation.id] !== 1) {
                throw contractError('unassigned_operation', 'Operation ' + String(operation.id) + ' must be assigned exactly once.');
            }
        });
        if (hasEmptySetup) {
            throw contractError('empty_setup', 'Every setup must contain at least one operation.');
        }

        return hash(withoutPlanHash(plan)).then(function (expectedPlanHash) {
            if (plan.planHash !== expectedPlanHash) {
                throw contractError('plan_hash_mismatch', 'Plan hash does not match its canonical content.');
            }
            return plan;
        });
    }

    // Reconstruct this bounded template from the authoritative face loops, never
    // from a caller-authored sweep box. Unsupported corner/access geometry returns null.
    function prismaticCorridor(feature, operation, faceIndex) {
        if (!feature || (feature.kind !== 'slot' && feature.kind !== 'pocket')
            || (operation.kind !== 'roughing' && operation.kind !== 'finishing')) { return null; }
        var corner = feature.cornerEnvelope, access = feature.accessEvidence, axis = operation.accessAxis;
        if (!corner || corner.kind !== 'open_ended_corridor' || corner.openEndCount !== 2
            || !access || access.source !== 'brep_opposing_slot_walls' || !axis) { return null; }
        var names = ['x', 'y', 'z'], axisName = names.find(function (name) { return Math.abs(axis[name]) === 1; });
        if (!axisName || names.some(function (name) { return name !== axisName && axis[name] !== 0; })) { return null; }
        var sign = axis[axisName], floor = faceIndex[corner.floorFaceId], walls = values(corner.wallFaceIds).map(function (id) { return faceIndex[id]; });
        function points(face) { return face && values(face.loops).length === 1 ? values(face.loops[0].vertices) : []; }
        function normal(face) { return face && face.surface && (face.surface.normal || face.surface.axis); }
        function planar(face) { return face && face.surface.kind === 'plane' && face.bodyId === feature.bodyId
            && points(face).length === 4 && points(face).every(function (p) { return names.every(function (n) { return Number.isFinite(p[n]); }); }); }
        function near(a, b) { return Number.isFinite(a) && Number.isFinite(b) && Math.abs(a - b) < 0.001; }
        function extent(face, name) { var coordinates = points(face).map(function (p) { return p[name]; });
            return { min: Math.min.apply(Math, coordinates), max: Math.max.apply(Math, coordinates) }; }
        if (!planar(floor) || !normal(floor) || !near(normal(floor)[axisName], sign)
            || walls.length !== 2 || walls.some(function (wall) { return !planar(wall) || !normal(wall)
                || !values(floor.adjacentFaceIds).includes(wall.id); })
            || JSON.stringify(values(feature.primaryFaceIds).slice().sort()) !== JSON.stringify([floor.id].concat(walls.map(function (w) { return w.id; })).sort())) { return null; }
        var lateral = names.filter(function (name) { return name !== axisName; });
        var transverse = lateral.find(function (name) { return near(Math.abs(normal(walls[0])[name]), 1); });
        if (!transverse || !near(normal(walls[0])[transverse], -normal(walls[1])[transverse])) { return null; }
        var longitudinal = lateral.find(function (name) { return name !== transverse; });
        var across = extent(floor, transverse), along = extent(floor, longitudinal), axial = extent(floor, axisName);
        if (!near(axial.min, axial.max) || !(across.max > across.min) || !(along.max > along.min)
            || new Set(points(floor).map(function (p) { return p[transverse] + ':' + p[longitudinal]; })).size !== 4
            || points(floor).some(function (p) { return !(near(p[transverse], across.min) || near(p[transverse], across.max))
                || !(near(p[longitudinal], along.min) || near(p[longitudinal], along.max)); })) { return null; }
        var wallLevels = walls.map(function (wall) { var e = extent(wall, axisName); return sign > 0 ? e.max : e.min; });
        var floorPlane = axial.min, entryPlane = wallLevels[0], depth = sign * (entryPlane - floorPlane);
        if (!(depth > 0) || !near(wallLevels[0], wallLevels[1])
            || !near(access.floorPlaneMm, sign * floorPlane) || !near(access.entryPlaneMm, sign * entryPlane)
            || !near(featureDimension(feature, 'depthMm'), depth)
            || !near(featureDimension(feature, 'widthMm'), across.max - across.min)
            || !near(featureDimension(feature, 'lengthMm'), along.max - along.min)
            || !near(corner.maximumDiameterMm, across.max - across.min)) { return null; }
        if (walls.some(function (wall) { var w = extent(wall, transverse), l = extent(wall, longitudinal), a = extent(wall, axisName);
            return !near(w.min, w.max) || !(near(w.min, across.min) || near(w.min, across.max))
                || !near(l.min, along.min) || !near(l.max, along.max)
                || !near(sign > 0 ? a.min : a.max, floorPlane)
                || normal(wall)[transverse] * ((across.min + across.max) / 2 - w.min) <= 0; })) { return null; }
        return { geometry: 'prismatic_corridor', axisName: axisName, axisSign: sign, lateralNames: lateral,
            transverseName: transverse, longitudinalName: longitudinal,
            minimumTransverseMm: across.min, maximumTransverseMm: across.max,
            minimumLongitudinalMm: along.min, maximumLongitudinalMm: along.max,
            floorPlaneMm: floorPlane, entryPlaneMm: entryPlane,
            targetClassifierContract: 'CncBrepValidationMesh.v1' };
    }

    function holeDepthConsistent(feature) {
        var closure = feature && feature.cylindricalClosure;
        if (!closure) { return true; } // Abstract compiler contracts have no CAD; physical validation requires it below.
        return Number.isFinite(closure.minimumAxialMm) && Number.isFinite(closure.maximumAxialMm)
            && closure.maximumAxialMm > closure.minimumAxialMm
            && Math.abs(featureDimension(feature, 'depthMm') - (closure.maximumAxialMm - closure.minimumAxialMm)) <= 0.001;
    }
    function finiteHoleInterval(feature, operation, faceIndex) {
        if (!feature || feature.kind !== 'hole' || !feature.cylindricalClosure || !holeDepthConsistent(feature)) { return null; }
        var cylinders = values(feature.primaryFaceIds).map(function (id) { return faceIndex[id]; })
            .filter(function (face) { return face && face.surface && face.surface.kind === 'cylinder'; });
        if (cylinders.length !== 1) { return null; }
        var face = cylinders[0], support = face.surface, center = support.centerMm, axis = support.axis;
        function finite(p) { return p && ['x', 'y', 'z'].every(function (name) { return Number.isFinite(p[name]); }); }
        function points(f) { return values(f && f.loops).flatMap(function (loop) { return values(loop.vertices); }); }
        if (face.orientation !== 'reversed' || face.bodyId !== feature.bodyId || !finite(center) || !finite(axis)
            || !(support.radiusMm > 0) || !Number.isFinite(support.angularSpanRadians)
            || Math.abs(support.angularSpanRadians - Math.PI * 2) > 0.0001) { return null; }
        var length = Math.hypot(axis.x, axis.y, axis.z);
        if (!(length > 0)) { return null; }
        axis = { x: axis.x / length, y: axis.y / length, z: axis.z / length };
        var axisName = ['x', 'y', 'z'].find(function (name) { return Math.abs(axis[name]) > 0.999999; });
        var access = operation && operation.accessAxis;
        if (!axisName || !finite(access) || Math.abs(access[axisName]) < 0.999999) { return null; }
        function project(p) { return (p.x-center.x)*axis.x + (p.y-center.y)*axis.y + (p.z-center.z)*axis.z; }
        var vertices = points(face);
        if (!vertices.length || !vertices.every(finite)) { return null; }
        var axial = vertices.map(project), minimum = Math.min.apply(Math, axial), maximum = Math.max.apply(Math, axial);
        if (!(maximum - minimum > 0.001)) { return null; }
        var rings = values(face.loops).filter(function (loop) { var ring = values(loop.vertices).map(project);
            return loop.closed === true && ring.length >= 3 && Math.max.apply(Math, ring) - Math.min.apply(Math, ring) <= 0.001; });
        if (support.closureEvidence !== 'brep_periodic_seam' && rings.length < 2) { return null; }
        var closure = feature.cylindricalClosure;
        if (Math.abs(closure.minimumAxialMm - minimum) > 0.001 || Math.abs(closure.maximumAxialMm - maximum) > 0.001
            || Math.abs(featureDimension(feature, 'depthMm') - (maximum - minimum)) > 0.001
            || Math.abs(featureDimension(feature, 'diameterMm') - support.radiusMm * 2) > 0.001) { return null; }
        var caps = values(face.adjacentFaceIds).map(function (id) { return faceIndex[id]; }).filter(function (cap) {
            return cap && cap.bodyId === face.bodyId && cap.surface && ['plane', 'cone'].indexOf(cap.surface.kind) >= 0;
        });
        if (![minimum, maximum].every(function (limit) { return caps.some(function (cap) {
            var normal = cap.surface.normal || cap.surface.axis;
            if (cap.surface.kind === 'plane' && (!finite(normal) || Math.abs(normal.x*axis.x + normal.y*axis.y + normal.z*axis.z) < 0.999)) { return false; }
            return points(cap).filter(finite).some(function (p) { var a = project(p);
                return Math.abs(a - limit) <= 0.001 && Math.abs(Math.hypot(p.x-center.x-a*axis.x,
                    p.y-center.y-a*axis.y, p.z-center.z-a*axis.z) - support.radiusMm) <= 0.001;
            });
        }); })) { return null; }
        var first = center[axisName] + minimum * axis[axisName], last = center[axisName] + maximum * axis[axisName];
        return { minimumAxialMm: Math.min(first, last), maximumAxialMm: Math.max(first, last),
            depthMm: maximum - minimum, axisName: axisName, radiusMm: support.radiusMm };
    }

    function topologyEvidence(topology) {
        var evidence = JSON.parse(JSON.stringify(topology));
        values(evidence.faces).forEach(function (face) { delete face.analysisSamples; delete face.triangleRange; });
        delete evidence.validationMesh;
        return canonicalize(evidence);
    }

    root.CncPlanContracts = Object.freeze({ canonicalize: canonicalize, hash: hash,
        topologyEvidence: topologyEvidence,
        holeDepthConsistent: holeDepthConsistent, finiteHoleInterval: finiteHoleInterval,
        prismaticCorridor: prismaticCorridor,
        validateTopology: validateTopology,
        validateFeatureGraph: validateFeatureGraph,
        validateOperationGraph: validateOperationGraph,
        validatePlan: validatePlan });
})(self);
