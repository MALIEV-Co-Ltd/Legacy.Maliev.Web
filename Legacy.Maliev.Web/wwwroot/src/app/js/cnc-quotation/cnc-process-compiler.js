(function (root) {
    'use strict';

    var contracts = root.CncPlanContracts;
    var toolLibrary = root.CncToolLibrary;
    if (!contracts || !toolLibrary) {
        throw new Error('CNC plan contracts and tool library must load before the process compiler.');
    }

    function values(value) { return Array.isArray(value) ? value : []; }
    function number(value) { return Number.isFinite(Number(value)) ? Number(value) : null; }
    function dimensions(feature) { return feature && feature.dimensions && typeof feature.dimensions === 'object'
        ? feature.dimensions : {}; }
    function dimension(feature, name) {
        var nested = number(dimensions(feature)[name]);
        return nested !== null ? nested : number(feature && feature[name]);
    }
    function accessAxis(feature) { return values(feature && feature.accessAxes)[0] || null; }
    function sameAxis(left, right) {
        return left && right && Math.abs(left.x - right.x) < 0.000001
            && Math.abs(left.y - right.y) < 0.000001 && Math.abs(left.z - right.z) < 0.000001;
    }

    function unresolved(feature, reason) {
        return { featureId: feature && feature.id || null, reason: reason, required: true };
    }

    function operationId(feature, kind, phase, band) {
        return ['operation', feature.id, kind, phase, band || 'primary'].join('-');
    }

    function addOperation(state, feature, kind, phase, toolClass, tool, constraints, predecessors, band) {
        var operation = {
            id: operationId(feature, kind, phase, band),
            featureId: feature.id,
            kind: kind,
            phase: phase,
            toolClass: toolClass,
            toolConstraints: Object.assign({ toolClass: toolClass }, constraints || {}),
            accessAxis: accessAxis(feature),
            predecessors: values(predecessors).slice(),
            provenance: { sourceContract: 'ManufacturingFeatureGraph.v1', featureId: feature.id }
        };
        if (tool) {
            operation.toolId = tool.id;
            operation.toolDiameterMm = tool.diameterMm;
            operation.toolConstraints.selectedToolId = tool.id;
            operation.toolConstraints.maximumDiameterMm = Number.isFinite(operation.toolConstraints.maximumDiameterMm)
                ? operation.toolConstraints.maximumDiameterMm : tool.diameterMm;
            operation.toolConstraints.usableCutLengthMm = tool.usableCutLengthMm;
            operation.toolConstraints.reachMm = tool.reachMm;
        }
        if (constraints && typeof constraints.threadDesignation === 'string') {
            operation.threadDesignation = constraints.threadDesignation;
        }
        state.operations.push(operation);
        return operation;
    }

    function select(state, feature, constraints, reason) {
        var tool = toolLibrary.selectLargestFeasible(constraints);
        if (!tool) { state.unresolved.push(unresolved(feature, reason)); }
        return tool;
    }

    function spot(state, feature, pilotDiameterMm) {
        var tool = select(state, feature, { family: 'spot_drill', operation: 'spot_drilling',
            minimumDiameterMm: 10, maximumDiameterMm: 10,
            minimumCutLengthMm: Math.min(5, Math.max(0, pilotDiameterMm / 2)) }, 'unsupported_spot_drill');
        return tool && addOperation(state, feature, 'spot_drilling', 'spot', 'spot_drill', tool, {
            pilotDiameterMm: pilotDiameterMm,
            includedAngleDegrees: tool.includedAngleDegrees
        }, []);
    }

    function drill(state, feature, diameterMm, predecessor) {
        var tolerance = 0.0001;
        var depthMm = dimension(feature, 'depthMm') || 0;
        var tool = select(state, feature, { family: 'hss_drill', operation: 'drilling',
            minimumDiameterMm: diameterMm - tolerance, maximumDiameterMm: diameterMm + tolerance,
            minimumCutLengthMm: depthMm, minimumReachMm: depthMm }, 'unsupported_hss_drill');
        return tool && addOperation(state, feature, 'drilling', 'drill', 'hss_drill', tool, {
            nominalDiameterMm: diameterMm, minimumDiameterMm: diameterMm - tolerance,
            maximumDiameterMm: diameterMm + tolerance, minimumCutLengthMm: depthMm,
            minimumReachMm: depthMm
        }, predecessor ? [predecessor.id] : []);
    }

    function borePreparation(state, feature, diameterMm, predecessor) {
        var depthMm = dimension(feature, 'depthMm') || 0;
        var clearance = Math.max(0.2, diameterMm * 0.03);
        var tool = select(state, feature, { family: 'flat_end_mill', operation: 'roughing',
            maximumDiameterMm: diameterMm - clearance, minimumCutLengthMm: depthMm,
            minimumReachMm: depthMm }, 'unsupported_bore_preparation_tool');
        return tool && addOperation(state, feature, 'bore_preparation', 'drill', 'flat_end_mill', tool, {
            targetDiameterMm: diameterMm, maximumDiameterMm: tool.diameterMm,
            minimumCutLengthMm: depthMm, minimumReachMm: depthMm
        }, predecessor ? [predecessor.id] : []);
    }

    function compileHole(feature, state) {
        var diameterMm = dimension(feature, 'diameterMm');
        if (!(diameterMm > 0)) { state.unresolved.push(unresolved(feature, 'hole_diameter_required')); return; }
        if (!(dimension(feature, 'depthMm') > 0)) { state.unresolved.push(unresolved(feature, 'hole_depth_required')); return; }
        if (!contracts.holeDepthConsistent(feature)) { state.unresolved.push(unresolved(feature, 'hole_depth_mismatch')); return; }
        var spotOperation = spot(state, feature, diameterMm);
        if (!spotOperation) { return; }
        drill(state, feature, diameterMm, spotOperation);
    }

    function compileInternalThread(feature, state) {
        var diameterMm = dimension(feature, 'nominalDiameterMm');
        var pitchMm = dimension(feature, 'pitchMm');
        var designation = feature.threadDesignation;
        var pilotDiameterMm = dimension(feature, 'pilotDiameterMm');
        if (!(diameterMm > 0) || !(pitchMm > 0) || typeof designation !== 'string' || !designation) {
            state.unresolved.push(unresolved(feature, 'thread_designation_required')); return;
        }
        if (!(pilotDiameterMm > 0)) { pilotDiameterMm = diameterMm - pitchMm; }
        var spotOperation = spot(state, feature, pilotDiameterMm);
        if (!spotOperation) { return; }
        var preparation = diameterMm <= 12 ? drill(state, feature, pilotDiameterMm, spotOperation)
            : borePreparation(state, feature, pilotDiameterMm, spotOperation);
        if (!preparation) { return; }
        var family = diameterMm <= 12 ? 'tap' : 'thread_mill';
        var kind = diameterMm <= 12 ? 'tapping' : 'thread_milling';
        var tool = select(state, feature, { family: family, operation: kind,
            designation: diameterMm <= 12 ? designation : undefined,
            pitchMm: diameterMm <= 12 ? pitchMm : undefined },
        diameterMm <= 12 ? 'unsupported_tap' : 'unsupported_thread_mill');
        if (!tool) { return; }
        addOperation(state, feature, kind, 'thread', family, tool, {
            threadDesignation: designation, nominalDiameterMm: diameterMm,
            pitchMm: pitchMm, handedness: feature.handedness || 'right'
        }, [preparation.id]);
    }

    function compileExternalThread(feature, state) {
        var diameterMm = dimension(feature, 'nominalDiameterMm');
        var pitchMm = dimension(feature, 'pitchMm');
        var designation = feature.threadDesignation;
        if (!(diameterMm > 0) || !(pitchMm > 0) || typeof designation !== 'string' || !designation) {
            state.unresolved.push(unresolved(feature, 'thread_designation_required')); return;
        }
        if (typeof feature.majorDiameterPreparationRequired !== 'boolean'
            || typeof feature.leadInChamferRequired !== 'boolean') {
            state.unresolved.push(unresolved(feature, 'external_thread_process_state_required')); return;
        }
        var predecessor = null;
        if (feature.majorDiameterPreparationRequired) {
            var preparationDepthMm = dimension(feature, 'depthMm');
            if (!(preparationDepthMm > 0)) {
                state.unresolved.push(unresolved(feature, 'external_thread_preparation_envelope_required')); return;
            }
            var preparationTool = select(state, feature, { family: 'flat_end_mill', operation: 'roughing',
                maximumDiameterMm: 16, minimumCutLengthMm: preparationDepthMm,
                minimumReachMm: preparationDepthMm }, 'unsupported_major_diameter_preparation_tool');
            if (!preparationTool) { return; }
            predecessor = addOperation(state, feature, 'major_diameter_preparation', 'rough',
                'flat_end_mill', preparationTool, { nominalDiameterMm: diameterMm,
                    maximumDiameterMm: preparationTool.diameterMm,
                    minimumCutLengthMm: preparationDepthMm,
                    minimumReachMm: preparationDepthMm }, []);
        }
        if (feature.leadInChamferRequired) {
            var angle = dimension(feature, 'includedAngleDegrees');
            if (!(angle > 0)) {
                state.unresolved.push(unresolved(feature, 'external_thread_lead_in_envelope_required')); return;
            }
            var chamferTool = select(state, feature, { family: 'chamfer_mill', operation: 'chamfering' },
                'unsupported_chamfer_mill');
            if (!chamferTool) { return; }
            predecessor = addOperation(state, feature, 'chamfering', 'finish', 'chamfer_mill',
                chamferTool, { includedAngleDegrees: angle,
                    minimumReachMm: Math.min(24, chamferTool.reachMm) }, predecessor ? [predecessor.id] : []);
        }
        var tool = select(state, feature, { family: 'thread_mill', operation: 'thread_milling' },
            'unsupported_thread_mill');
        if (!tool) { return; }
        addOperation(state, feature, 'thread_milling', 'thread', 'thread_mill', tool, {
            threadDesignation: designation, nominalDiameterMm: diameterMm,
            pitchMm: pitchMm, handedness: feature.handedness || 'right',
            majorDiameterPreparationRequired: feature.majorDiameterPreparationRequired,
            leadInChamferRequired: feature.leadInChamferRequired
        }, predecessor ? [predecessor.id] : []);
    }

    function compileChamfer(feature, state) {
        var angle = dimension(feature, 'includedAngleDegrees') || 90;
        var tool = select(state, feature, { family: 'chamfer_mill', operation: 'chamfering' },
            'unsupported_chamfer_mill');
        if (!tool) { return; }
        addOperation(state, feature, 'chamfering', 'finish', 'chamfer_mill', tool,
            { includedAngleDegrees: angle, minimumReachMm: Math.min(24, tool.reachMm) }, []);
    }

    function cutterFor(state, feature, maximumDiameterMm, depthMm, reason) {
        return select(state, feature, { family: 'flat_end_mill', operation: 'roughing',
            maximumDiameterMm: maximumDiameterMm, minimumCutLengthMm: depthMm,
            minimumReachMm: depthMm }, reason);
    }

    function compilePrismatic(feature, state) {
        var widthMm = dimension(feature, 'widthMm') || dimension(feature, 'corridorWidthMm');
        var depthMm = dimension(feature, 'depthMm');
        var cornerRadiusMm = dimension(feature, 'internalCornerRadiusMm');
        if (!(widthMm > 0) || !(depthMm > 0)) {
            state.unresolved.push(unresolved(feature, 'prismatic_envelope_required')); return;
        }
        if ((feature.kind === 'slot' || feature.kind === 'pocket') && !(cornerRadiusMm > 0)
            && !(feature.cornerEnvelope && feature.cornerEnvelope.kind === 'open_ended_corridor'
                && feature.cornerEnvelope.openEndCount === 2 && feature.cornerEnvelope.maximumDiameterMm >= widthMm)) {
            state.unresolved.push(unresolved(feature, 'prismatic_corner_envelope_required')); return;
        }
        var openingMaximum = widthMm - Math.max(0.2, widthMm * 0.03);
        var bulkTool = cutterFor(state, feature, openingMaximum, depthMm, 'unsupported_prismatic_tool');
        if (!bulkTool) { return; }
        var restRequired = cornerRadiusMm > 0 || feature.restMaterialRequired === true;
        if (feature.restMaterialRequired === true && !(cornerRadiusMm > 0)) {
            state.unresolved.push(unresolved(feature, 'prismatic_rest_envelope_required')); return;
        }
        var restTool = restRequired ? cutterFor(state, feature,
            Math.min(openingMaximum, cornerRadiusMm * 2), depthMm,
            'unsupported_prismatic_rest_tool') : null;
        if (restRequired && !restTool) { return; }
        var tools = [bulkTool];
        if (restTool && restTool.diameterMm < bulkTool.diameterMm) { tools.push(restTool); }
        var lastRough = null;
        ['rough', 'finish'].forEach(function (phase) {
            var previous = phase === 'finish' ? lastRough : null;
            tools.forEach(function (tool, index) {
                previous = addOperation(state, feature,
                    phase === 'rough' ? 'roughing' : 'finishing', phase, 'flat_end_mill', tool, {
                        maximumDiameterMm: tool.diameterMm, minimumCutLengthMm: depthMm,
                        minimumReachMm: depthMm,
                        constraintBasis: index === 0 ? 'feature_corridor' : 'proven_internal_corner'
                    }, previous ? [previous.id] : [], index === 0 ? 'bulk' : 'rest');
            });
            if (phase === 'rough') { lastRough = previous; }
        });
    }

    function compileDatum(feature, state) {
        if (feature.machiningRequired !== true) { return; }
        var evidence = feature.facingEvidence;
        if (!evidence || evidence.source !== 'brep_primary_datum_selection'
            || JSON.stringify(values(evidence.faceIds).slice().sort()) !== JSON.stringify(values(feature.primaryFaceIds).slice().sort())
            || !sameAxis(evidence.accessAxis, accessAxis(feature))) {
            state.unresolved.push(unresolved(feature, 'datum_facing_evidence_required')); return;
        }
        var tool = select(state, feature, { family: 'face_mill', operation: 'facing' },
            'unsupported_face_mill');
        if (tool) { addOperation(state, feature, 'facing', 'rough', 'face_mill', tool, {}, []); }
    }

    function compileBallFinish(feature, state) {
        if (feature.certification !== 'non_prismatic_curvature') {
            state.unresolved.push(unresolved(feature, 'uncertified_freeform_feature')); return;
        }
        var radiusMm = dimension(feature, 'radiusMm');
        var depthMm = dimension(feature, 'depthMm');
        var requiredReachMm = dimension(feature, 'requiredReachMm') || depthMm;
        if (!(radiusMm > 0) || !(depthMm > 0) || !(requiredReachMm > 0)) {
            state.unresolved.push(unresolved(feature, 'certified_curvature_envelope_required')); return;
        }
        var maximumDiameterMm = radiusMm * 2;
        var tool = select(state, feature, { family: 'ball_end_mill', operation: 'freeform_finishing',
            maximumDiameterMm: maximumDiameterMm, minimumCutLengthMm: depthMm || 0,
            minimumReachMm: requiredReachMm }, 'unsupported_ball_end_mill');
        if (tool) {
            addOperation(state, feature, 'ball_end_finishing', 'finish', 'ball_end_mill', tool,
                { maximumDiameterMm: tool.diameterMm,
                    minimumCutLengthMm: depthMm || 0, minimumReachMm: requiredReachMm,
                    certification: 'non_prismatic_curvature' }, [], 'certified');
        }
    }

    var templates = Object.freeze({
        hole: compileHole,
        internal_thread: compileInternalThread,
        external_thread: compileExternalThread,
        chamfer: compileChamfer,
        slot: compilePrismatic,
        pocket: compilePrismatic,
        outside_profile: compilePrismatic,
        boss: compilePrismatic,
        datum: compileDatum,
        fillet: compileBallFinish,
        freeform_patch: compileBallFinish
    });

    function compile(featureGraph, options) {
        contracts.validateFeatureGraph(featureGraph);
        var state = { operations: [], unresolved: values(featureGraph.unresolved).map(function (item) {
            return { featureId: item.featureId || null, reason: item.reason,
                required: item.required !== false };
        }) };
        if (!state.unresolved.some(function (item) { return item.required; })) {
            values(featureGraph.features).forEach(function (feature) {
                if (feature.kind === 'unresolved') {
                    state.unresolved.push(unresolved(feature,
                        feature.unresolvedReason || 'unresolved_feature'));
                    return;
                }
                var template = templates[feature.kind];
                if (!template) { state.unresolved.push(unresolved(feature, 'unsupported_feature_kind')); return; }
                if (feature.machiningRequired === false) { return; }
                var featureState = { operations: [], unresolved: [] };
                template(feature, featureState, options || {});
                if (featureState.unresolved.length) {
                    state.unresolved.push.apply(state.unresolved, featureState.unresolved);
                } else {
                    state.operations.push.apply(state.operations, featureState.operations);
                }
            });
            var featuresById = values(featureGraph.features).reduce(function (map, feature) {
                map[feature.id] = feature; return map;
            }, Object.create(null));
            var profileFinishes = state.operations.filter(function (operation) {
                return operation.kind === 'finishing' && featuresById[operation.featureId]
                    && featuresById[operation.featureId].kind === 'outside_profile';
            });
            state.operations.filter(function (operation) { return operation.kind === 'thread_milling'
                && featuresById[operation.featureId] && featuresById[operation.featureId].kind === 'external_thread';
            }).forEach(function (threadOperation) {
                profileFinishes.filter(function (operation) { return sameAxis(operation.accessAxis, threadOperation.accessAxis); })
                    .forEach(function (operation) { if (threadOperation.predecessors.indexOf(operation.id) < 0) { threadOperation.predecessors.push(operation.id); } });
            });
        }
        var graph = {
            contract: 'MachiningOperationGraph.v1',
            topologyRevision: featureGraph.topologyRevision,
            toolLibraryVersion: options && options.toolLibraryVersion || toolLibrary.version,
            operations: state.operations,
            unresolved: state.unresolved
        };
        contracts.validateOperationGraph(graph, featureGraph);
        return graph;
    }

    root.CncProcessCompiler = Object.freeze({ compile: compile });
}(typeof self !== 'undefined' ? self : globalThis));
