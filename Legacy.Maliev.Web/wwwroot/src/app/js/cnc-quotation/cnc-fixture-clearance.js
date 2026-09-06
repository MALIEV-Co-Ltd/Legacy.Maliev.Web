(function (root) {
    'use strict';

    function finite(value) {
        return typeof value === 'number' && Number.isFinite(value);
    }

    function directionAxis(direction) {
        direction = direction || {};
        return ['x', 'y', 'z'].map(function (axis) {
            return { axis: axis, value: finite(direction[axis]) ? direction[axis] : 0 };
        }).sort(function (left, right) {
            return Math.abs(right.value) - Math.abs(left.value);
        })[0];
    }

    function dot(left, right) {
        return ((left && left.x) || 0) * ((right && right.x) || 0)
            + ((left && left.y) || 0) * ((right && right.y) || 0)
            + ((left && left.z) || 0) * ((right && right.z) || 0);
    }

    function subtract(left, right) {
        return {
            x: ((left && left.x) || 0) - ((right && right.x) || 0),
            y: ((left && left.y) || 0) - ((right && right.y) || 0),
            z: ((left && left.z) || 0) - ((right && right.z) || 0)
        };
    }

    function principalAxisIndex(candidate, axes) {
        var match = candidate && typeof candidate.id === 'string'
            ? candidate.id.match(/(?:^|-)(x|y|z)$/) : null;
        if (match) { return { x: 0, y: 1, z: 2 }[match[1]]; }
        if (!Array.isArray(axes) || axes.length !== 3) {
            return { x: 0, y: 1, z: 2 }[directionAxis(candidate && candidate.direction).axis];
        }
        var selectedIndex = 0;
        var selectedMagnitude = -1;
        axes.forEach(function (axis, index) {
            var magnitude = Math.abs(dot(candidate && candidate.direction, axis));
            if (magnitude > selectedMagnitude) {
                selectedIndex = index;
                selectedMagnitude = magnitude;
            }
        });
        return selectedIndex;
    }

    function rounded(value, increment) {
        increment = finite(increment) && increment > 0 ? increment : 5;
        return Math.ceil(Math.max(0, value) / increment) * increment;
    }

    function blankSize(size, strategy, rule, axes) {
        var candidate = strategy && strategy.candidates && strategy.candidates[0];
        var normalIndex = principalAxisIndex(candidate, axes);
        var sizeKeys = ['x', 'y', 'z'];
        var footprint = sizeKeys.filter(function (_, index) { return index !== normalIndex; })
            .map(function (axis) { return finite(size[axis]) ? size[axis] : 0; })
            .sort(function (left, right) { return right - left; });
        return Object.freeze({
            x: rounded(footprint[0] + (rule.planarMarginMm * 2), rule.stockIncrementMm),
            y: rounded(footprint[1] + (rule.planarMarginMm * 2), rule.stockIncrementMm),
            z: rounded(rule.blankThicknessMm, rule.stockIncrementMm)
        });
    }

    function findById(items, id) {
        return (items || []).find(function (item) { return item && item.id === id; }) || null;
    }

    function recordFor(records, work) {
        return (records || []).find(function (record) {
            return record && record.reachable === true && record.clusterId === work.clusterId
                && record.operationCode === work.operationCode
                && Array.isArray(record.reachableSampleIds)
                && record.reachableSampleIds.indexOf(work.sampleId) >= 0;
        }) || null;
    }

    function principalFrame(field, size, candidate, rule) {
        var axes = field && field.axes;
        var dimensions = field && field.dimensions;
        var origin = field && field.origin;
        var cellSizeMm = field && field.cellSizeMm;
        if (!Array.isArray(axes) || axes.length !== 3 || !origin || !dimensions
            || !finite(cellSizeMm) || cellSizeMm <= 0) {
            return null;
        }
        var normalIndex = principalAxisIndex(candidate, axes);
        var lateralIndexes = [0, 1, 2].filter(function (index) { return index !== normalIndex; });
        var sizeKeys = ['x', 'y', 'z'];
        return {
            axes: axes,
            origin: origin,
            center: [dimensions.x, dimensions.y, dimensions.z].map(function (dimension) {
                return dimension * cellSizeMm * 0.5;
            }),
            normalIndex: normalIndex,
            lateralIndexes: lateralIndexes,
            halfSpans: lateralIndexes.map(function (index) {
                return ((size[sizeKeys[index]] || 0) * 0.5) + rule.planarMarginMm;
            })
        };
    }

    function sampleClearance(sample, size, normalAxis, rule, frame) {
        if (frame) {
            var delta = subtract(sample.position, frame.origin);
            return Math.min.apply(null, frame.lateralIndexes.map(function (index, lateralIndex) {
                var coordinate = dot(delta, frame.axes[index]) - frame.center[index];
                return frame.halfSpans[lateralIndex] - Math.abs(coordinate);
            }));
        }
        var axes = ['x', 'y', 'z'].filter(function (axis) { return axis !== normalAxis; });
        return Math.min(
            ((size[axes[0]] || 0) * 0.5) - Math.abs(sample.position[axes[0]] || 0),
            ((size[axes[1]] || 0) * 0.5) - Math.abs(sample.position[axes[1]] || 0));
    }

    function collisionReason(sample, tool, size, normalAxis, rule, frame) {
        if (!sample || !sample.position || !tool) { return 'fixture_clearance_evidence_missing'; }
        var edgeClearance = sampleClearance(sample, size, normalAxis, rule, frame);
        var cutterRadius = Math.max(0, (tool.diameterMm || 0) * 0.5) + rule.cutterClearanceMm;
        var shankRadius = Math.max(cutterRadius, (tool.shankDiameterMm || tool.diameterMm || 0) * 0.5);
        var holderRadius = Math.max(shankRadius, (tool.holderDiameterMm || 0) * 0.5);
        if (edgeClearance >= rule.jawEndClearanceMm) { return null; }
        if (edgeClearance < cutterRadius) { return 'fixture_cutter_collision'; }
        if (edgeClearance < shankRadius) { return 'fixture_shank_collision'; }
        if (edgeClearance < holderRadius) { return 'fixture_holder_collision'; }
        return null;
    }

    function validateToolEnvelope(input) {
        input = input || {};
        var tool = input.tool || {};
        var clearance = input.fixtureClearance;
        var toleranceMm = finite(input.toleranceMm) && input.toleranceMm >= 0 ? input.toleranceMm : 0;
        var valid = clearance && finite(clearance.cutterRadiusMm)
            && finite(clearance.shankRadiusMm) && finite(clearance.holderRadiusMm)
            && finite(tool.diameterMm) && tool.diameterMm > 0
            && finite(tool.shankDiameterMm) && tool.shankDiameterMm > 0
            && finite(tool.holderDiameterMm) && tool.holderDiameterMm > 0
            && clearance.cutterRadiusMm + toleranceMm >= tool.diameterMm * 0.5
            && clearance.shankRadiusMm + toleranceMm >= tool.shankDiameterMm * 0.5
            && clearance.holderRadiusMm + toleranceMm >= tool.holderDiameterMm * 0.5;
        return Object.freeze({ valid: !!valid,
            reason: valid ? null : 'tool_envelope_unreachable' });
    }

    function evaluate(input) {
        input = input || {};
        var geometry = input.geometry || {};
        var size = geometry.orientedSizeMm || {};
        var strategy = input.strategy || {};
        var rule = input.rule || {};
        var field = geometry.accessibilityField || {};
        var datum = (strategy.candidates || []).find(function (candidate) {
            return candidate.id === strategy.datumSetupId;
        }) || (strategy.candidates || [])[0] || {};
        var normal = directionAxis(datum.direction);
        var frame = principalFrame(field, size, datum, rule);
        var blocked = [];
        var reasons = [];
        var minimumClearance = Number.POSITIVE_INFINITY;
        var required = Array.isArray(input.requiredWork) ? input.requiredWork : [];
        var sampleWork = required.filter(function (work) { return work && work.sampleId !== null; });
        var clusterProxyCovered = sampleWork.length === 0 && required.length > 0 && required.every(function (work) {
            return (input.reachRecords || []).some(function (record) {
                return record && record.reachable === true && record.clusterId === work.clusterId
                    && record.operationCode === work.operationCode;
            });
        });

        sampleWork.forEach(function (work) {
            var record = recordFor(input.reachRecords, work);
            var sample = findById(field.surfaceSamples, work.sampleId);
            var tool = record && findById(input.tools, record.toolId);
            var reason = record && sample && tool ? collisionReason(sample, tool, size, normal.axis, rule, frame)
                : 'fixture_clearance_evidence_missing';
            if (reason) {
                blocked.push(work.key);
                if (reasons.indexOf(reason) < 0) { reasons.push(reason); }
                return;
            }
            minimumClearance = Math.min(minimumClearance,
                Math.max(0, sampleClearance(sample, size, normal.axis, rule, frame)));
        });

        var supportedAreaRatio = sampleWork.length > 0
            ? (sampleWork.length - blocked.length) / sampleWork.length : clusterProxyCovered ? 1 : 0;
        var feasible = (sampleWork.length > 0 || clusterProxyCovered) && blocked.length === 0
            && supportedAreaRatio >= rule.minimumSupportedAreaRatio;
        if (clusterProxyCovered) { reasons.push('fixture_clearance_cluster_proxy'); }
        if (!feasible && reasons.length === 0) { reasons.push('fixture_support_insufficient'); }
        return Object.freeze({
            feasible: feasible,
            supportDirection: Object.freeze({ axis: normal.axis, sign: normal.value >= 0 ? 1 : -1 }),
            supportedAreaRatio: supportedAreaRatio,
            minimumClearanceMm: Number.isFinite(minimumClearance) ? minimumClearance : 0,
            blockedRequiredWorkKeys: Object.freeze(blocked),
            acceptedSetupIds: Object.freeze(feasible ? (strategy.candidates || []).map(function (candidate) {
                return candidate.id;
            }) : []),
            confidence: feasible && !clusterProxyCovered ? 'High' : 'Low',
            reasons: Object.freeze(reasons),
            stock: Object.freeze({
                materialCode: rule.materialCode,
                form: rule.stockForm,
                sizeMm: blankSize(size, strategy, rule, field.axes)
            })
        });
    }

    root.CncFixtureClearance = Object.freeze({ evaluate: evaluate,
        validateToolEnvelope: validateToolEnvelope });
})(typeof window !== 'undefined' ? window : self);
