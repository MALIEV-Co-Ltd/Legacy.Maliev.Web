(function (root) {
    'use strict';

    var codes = Object.freeze({
        threeAxis: 'three_axis',
        threeAxisCustomFixture: 'three_axis_custom_fixture',
        fiveAxisReviewRequired: 'five_axis_review_required',
        notFullyMillable: 'not_fully_millable'
    });

    function finiteNonNegative(value) {
        return typeof value === 'number' && Number.isFinite(value) && value >= 0;
    }

    function unpriceable(errorCode) {
        return Object.freeze({
            priceable: false,
            errorCode: errorCode,
            machineMinuteCustomerRateBeforeVat: null,
            camAllowanceBeforeVat: null,
            setupAllowanceBeforeVat: null,
            probingMinutesPerSetup: null,
            riskAllowanceBeforeVat: null
        });
    }

    function classify(plan) {
        plan = plan || {};
        var fixtureFeasible = plan.fixtureStrategy && plan.fixtureStrategy.required === true
            && plan.fixtureStrategy.clearance && plan.fixtureStrategy.clearance.feasible === true;
        var operations = Array.isArray(plan.operations) ? plan.operations : null;
        var reachMatrix = Array.isArray(plan.reachMatrix) ? plan.reachMatrix : null;
        var hasUnreachableOperation = operations === null || operations.some(function (operation) {
            if (!operation || operation.code === 'deburring' || operation.reachable !== false) { return false; }
            var operationReach = reachMatrix ? reachMatrix.filter(function (record) {
                return record && record.operationCode === operation.code;
            }) : null;
            if (operationReach) {
                // Roughing and other stock-removal passes can exist without a one-to-one CAD
                // surface cluster. Their absence from the reach matrix is not evidence that a
                // reindex or five-axis machine is required. A mapped operation escalates only
                // when every evaluated setup/tool combination is unreachable.
                return operationReach.length > 0
                    && !operationReach.some(function (record) { return record.reachable === true; });
            }
            // Facing is an optional stock-preparation pass. A near-net or already-datumed part
            // can legitimately have no face-mill cluster while its required roughing and
            // finishing operations remain fully reachable on a 3-axis machine.
            var emptyFacingPass = operation.code === 'facing'
                && ((Array.isArray(operation.clusterIds) && operation.clusterIds.length === 0)
                    || Number(operation.clusterCount) === 0);
            return !emptyFacingPass;
        });
        // Surface-field residue is advisory until it maps to required machining work. This
        // prevents tessellation or axial flute-contact gaps from escalating an otherwise
        // complete 3-axis operation plan to a 5-axis quote.
        var unresolved = plan.hasSignificantUnmachinableSurface === true && hasUnreachableOperation;
        // Re-indexed side work can remain unresolved in one sampled orientation without
        // requiring simultaneous five-axis motion. Escalate the machine class only when the
        // planner supplies explicit five-axis evidence; keep generic access gaps as an
        // engineering-review flag on the ordinary three-axis plan.
        var requiresFiveAxis = plan.requiresFiveAxis === true || (operations !== null && operations.some(function (operation) {
            return operation && operation.reachable === false
                && (operation.requiresFiveAxis === true || operation.simultaneousAxisRequired === true);
        }));
        var impossible = plan.notFullyMillable === true;
        var code = impossible ? codes.notFullyMillable
            : unresolved && requiresFiveAxis ? codes.fiveAxisReviewRequired
                : fixtureFeasible ? codes.threeAxisCustomFixture : codes.threeAxis;

        return Object.freeze({
            code: code,
            requiresEngineeringReview: unresolved || impossible,
            pricingClass: code === codes.fiveAxisReviewRequired ? 'five_axis' : 'three_axis',
            confidence: plan.confidence || 'Low',
            reasons: Object.freeze(Array.isArray(plan.reviewReasons) ? plan.reviewReasons.slice() : [])
        });
    }

    function normalizedThreeAxis(commercial) {
        var classes = commercial && commercial.machineClasses || {};
        var configured = classes.threeAxis || {};
        return {
            machineMinuteCustomerRateBeforeVat: configured.machineMinuteCustomerRateBeforeVat,
            camAllowanceBeforeVat: configured.camAllowanceBeforeVat,
            setupAllowanceBeforeVat: configured.setupAllowanceBeforeVat,
            probingMinutesPerSetup: finiteNonNegative(configured.probingMinutesPerSetup)
                ? configured.probingMinutesPerSetup : 0,
            riskAllowanceBeforeVat: finiteNonNegative(configured.riskAllowanceBeforeVat)
                ? configured.riskAllowanceBeforeVat : 0
        };
    }

    function validPolicy(policy) {
        return finiteNonNegative(policy.machineMinuteCustomerRateBeforeVat)
            && policy.machineMinuteCustomerRateBeforeVat > 0
            && finiteNonNegative(policy.camAllowanceBeforeVat)
            && finiteNonNegative(policy.setupAllowanceBeforeVat)
            && finiteNonNegative(policy.probingMinutesPerSetup)
            && finiteNonNegative(policy.riskAllowanceBeforeVat);
    }

    function priced(policy) {
        return Object.freeze({
            priceable: true,
            errorCode: null,
            machineMinuteCustomerRateBeforeVat: policy.machineMinuteCustomerRateBeforeVat,
            camAllowanceBeforeVat: policy.camAllowanceBeforeVat,
            setupAllowanceBeforeVat: policy.setupAllowanceBeforeVat,
            probingMinutesPerSetup: policy.probingMinutesPerSetup,
            riskAllowanceBeforeVat: policy.riskAllowanceBeforeVat
        });
    }

    function resolvePricingPolicy(capability, commercial) {
        capability = capability || {};
        commercial = commercial || {};
        if (capability.code === codes.notFullyMillable) {
            return unpriceable('cnc_not_fully_millable');
        }

        var threeAxis = normalizedThreeAxis(commercial);
        if (!validPolicy(threeAxis)) {
            return unpriceable('cnc_quote_unpriceable');
        }
        if (capability.pricingClass !== 'five_axis') {
            return priced(threeAxis);
        }

        var fiveAxis = commercial.machineClasses && commercial.machineClasses.fiveAxis;
        if (!fiveAxis || fiveAxis.configured !== true || !validPolicy(fiveAxis)
            || fiveAxis.machineMinuteCustomerRateBeforeVat <= threeAxis.machineMinuteCustomerRateBeforeVat) {
            return unpriceable('cnc_five_axis_pricing_review_required');
        }
        return priced(fiveAxis);
    }

    root.CncMachineCapability = Object.freeze({
        codes: codes,
        classify: classify,
        resolvePricingPolicy: resolvePricingPolicy
    });
})(window);
