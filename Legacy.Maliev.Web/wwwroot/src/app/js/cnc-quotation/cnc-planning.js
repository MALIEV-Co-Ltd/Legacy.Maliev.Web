(function (window) {
    'use strict';

    var config = window.CncQuotationConfig;
    var materialCatalog = window.CncMaterialCatalog;
    if (!config || !config.planning || !materialCatalog) {
        throw new Error('CNC quotation configuration and material catalog must load before commercial planning.');
    }

    var planning = config.planning;

    function isNonNegativeNumber(value) {
        return typeof value === 'number' && Number.isFinite(value) && value >= 0;
    }

    function planningContractError(code, message) {
        var error = new Error(message);
        error.code = code;
        return error;
    }

    function stockVolumeMm3(stock) {
        if (stock && isNonNegativeNumber(stock.stockVolumeMm3)) {
            return stock.stockVolumeMm3;
        }

        var size = stock && stock.stockSizeMm;
        return size && isNonNegativeNumber(size.x) && isNonNegativeNumber(size.y)
            && isNonNegativeNumber(size.z) ? size.x * size.y * size.z : 0;
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
        if (!(materialMrr > 0) || !(finishRate > 0)) {
            throw planningContractError('unsupported_alloy',
                'Commercial timing requires a supported material.');
        }

        var operations = (validated.operationGraph.operations || []).slice();
        var setups = (validated.setupPlan.setups || []).map(function (setup) {
            return Object.assign({}, setup);
        });
        var partVolumeMm3 = isNonNegativeNumber(geometry.partVolumeMm3)
            ? geometry.partVolumeMm3 : 0;
        var removalVolumeMm3 = Math.max(0, stockVolumeMm3(stock) - partVolumeMm3);
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
            return Promise.resolve(Object.freeze({
                valid: false,
                reviewReasons: Object.freeze(['plan_validator_required'])
            }));
        }

        return window.CncPlanValidator.validate(input);
    }

    window.CncPlanning = Object.freeze({
        commercializeValidatedPlan: commercializeValidatedPlan,
        validateManufacturingPlan: validateManufacturingPlan
    });
})(window);
