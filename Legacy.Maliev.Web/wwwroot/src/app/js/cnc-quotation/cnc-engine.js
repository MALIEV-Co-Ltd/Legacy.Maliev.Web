(function (window) {
    'use strict';

    var config = window.CncQuotationConfig;
    if (!config || !config.commercial || !config.planning || !config.finishing) {
        throw new Error('CNC quotation configuration must load before the commercial engine.');
    }

    var commercial = config.commercial;
    var machineCapability = window.CncMachineCapability;
    if (!machineCapability) {
        throw new Error('CNC machine capability policy must load before the commercial engine.');
    }
    var planContracts = window.CncPlanContracts;
    if (!planContracts) {
        throw new Error('CNC plan contracts must load before the commercial engine.');
    }
    var confidenceRank = Object.freeze({ High: 0, Medium: 1, Low: 2 });
    var maximumMoney = Number.MAX_SAFE_INTEGER / 100;

    function isFiniteNonNegative(value) {
        return typeof value === 'number' && Number.isFinite(value) && value >= 0;
    }

    function positiveInteger(value) {
        return typeof value === 'number' && Number.isSafeInteger(value) && value > 0 ? value : null;
    }

    function quoteError(code) {
        return Object.freeze({ code: code, message: 'CNC preliminary price needs additional engineering input.' });
    }

    function upwardRound(value, increment) {
        if (!isFiniteNonNegative(value) || !isFiniteNonNegative(increment) || increment <= 0) {
            return null;
        }

        var rounded = Math.ceil((value - Number.EPSILON) / increment) * increment;
        return isFiniteNonNegative(rounded) && rounded <= maximumMoney ? rounded : null;
    }

    function lowerConfidence(current, candidate) {
        return confidenceRank[candidate] > confidenceRank[current] ? candidate : current;
    }

    function normalizedConfidence(value) {
        return Object.prototype.hasOwnProperty.call(confidenceRank, value) ? value : 'Low';
    }

    function addReason(reasons, code) {
        if (typeof code === 'string' && code.length > 0 && reasons.indexOf(code) === -1) {
            reasons.push(code);
        }
    }

    function mergeEvidence(reasons, confidence, evidence) {
        var state = normalizedConfidence(evidence && evidence.confidence);
        (evidence && Array.isArray(evidence.reviewReasons) ? evidence.reviewReasons : []).forEach(function (reason) {
            addReason(reasons, reason);
        });
        return lowerConfidence(confidence, state);
    }

    function copyVersions() {
        return Object.freeze({
            estimatorVersion: config.estimatorVersion,
            stockRulesVersion: config.stockRulesVersion,
            materialPriceModelVersion: config.materialPriceModelVersion,
            commercialRulesVersion: config.commercialRulesVersion,
            materialCatalogVersion: config.materialCatalogVersion,
            toolLibraryVersion: config.toolLibraryVersion,
            reachRulesVersion: config.reachRulesVersion,
            finishingRulesVersion: config.finishingRulesVersion
        });
    }

    function finishRule(code) {
        return config.finishing.rules.find(function (rule) { return rule.code === code; }) || null;
    }

    function finishError(code) {
        return Object.freeze({ code: code, message: 'Selected CNC finish needs engineering review.' });
    }

    function selectedMaterial(code) {
        var catalog = window.CncMaterialCatalog;
        return catalog && typeof catalog.get === 'function' ? catalog.get(code) : null;
    }

    function estimateFinishBeforeVat(finish, quantity) {
        finish = finish || {};
        quantity = positiveInteger(quantity);
        if (!quantity) {
            throw finishError('cnc_finish_unpriceable');
        }

        if (!finish.code || finish.code === 'none' || finish.code === 'default' || finish.code === 'machined') {
            return 0;
        }

        var rule = finishRule(finish.code);
        if (!rule) {
            throw finishError('cnc_finish_unpriceable');
        }

        var materialCode = finish.material || finish.alloy;
        if (!materialCode || (finish.material && finish.alloy && finish.material !== finish.alloy)) {
            throw finishError('cnc_finish_material_invalid');
        }
        var material = selectedMaterial(materialCode);
        if (!material) {
            throw finishError('cnc_finish_material_invalid');
        }
        if (rule.compatibleMaterialFamilies.indexOf(material.family) === -1) {
            throw finishError('cnc_finish_incompatible');
        }

        if (!isFiniteNonNegative(finish.surfaceAreaMm2)) {
            throw finishError('cnc_finish_unpriceable');
        }

        var totalSurfaceAreaDm2 = (finish.surfaceAreaMm2 / 10000) * quantity;
        var calculated = (totalSurfaceAreaDm2 * rule.thbPerDm2)
            + (quantity * rule.handlingPerPartBeforeVat);
        var total = Math.max(rule.minimumBatchBeforeVat, calculated);
        if (!isFiniteNonNegative(total) || total > maximumMoney) {
            throw finishError('cnc_finish_unpriceable');
        }

        return total;
    }

    function stockShape(stock) {
        if (typeof stock.stockShape === 'string'
            && stock.stockShape.length > 0
            && stock.stockShape.length <= 80
            && /^[A-Za-z0-9_.-]+$/.test(stock.stockShape)) {
            return stock.stockShape;
        }
        if (typeof stock.strategy === 'string' && stock.strategy.indexOf('round') !== -1) {
            return 'round';
        }
        if (typeof stock.strategy === 'string' && stock.strategy.indexOf('plate') !== -1) {
            return 'plate';
        }
        return 'rectangular';
    }

    function safeManufacturingEvidence(stock, plan) {
        var families = [];
        (Array.isArray(plan.operations) ? plan.operations : []).forEach(function (operation) {
            var family = operation && (operation.toolFamily || operation.toolType);
            if (typeof family === 'string'
                && family.length > 0
                && family.length <= 80
                && /^[A-Za-z0-9_.-]+$/.test(family)
                && families.indexOf(family) === -1
                && families.length < 16) {
                families.push(family);
            }
        });
        var residual = plan.residual && plan.residual.estimatedVolumeMm3;
        var field = plan.fieldAccounting || {};
        var fieldMethod = typeof field.method === 'string' && field.method.length <= 80
            && /^[A-Za-z0-9_.-]+$/.test(field.method) ? field.method : null;
        var fieldChecksum = typeof field.checksum === 'string' && /^[A-Za-z0-9_.-]{1,80}$/.test(field.checksum)
            ? field.checksum : null;
        return Object.freeze({
            stockShape: stockShape(stock),
            setupCount: Math.min(16, Array.isArray(plan.setups) && plan.setups.length > 0 ? plan.setups.length : 1),
            toolFamilies: Object.freeze(families),
            residualVolumeMm3: isFiniteNonNegative(residual) ? residual : 0,
            fieldMethod: fieldMethod,
            fieldChecksum: fieldChecksum,
            fieldCellSizeMm: isFiniteNonNegative(field.cellSizeMm) ? field.cellSizeMm : 0,
            fieldPartVolumeMm3: isFiniteNonNegative(field.partVolumeMm3) ? field.partVolumeMm3 : 0,
            fieldRemovalVolumeMm3: isFiniteNonNegative(field.removalVolumeMm3) ? field.removalVolumeMm3 : 0,
            fieldMachinableSurfaceAreaMm2: isFiniteNonNegative(field.machinableSurfaceAreaMm2)
                ? field.machinableSurfaceAreaMm2 : 0,
            fieldDegraded: field.degraded === true
        });
    }

    function lineItem(code, category, amountBeforeVat, finishCode) {
        var item = { code: code, category: category, amountBeforeVat: amountBeforeVat };
        if (finishCode) { item.finishCode = finishCode; }
        return Object.freeze(item);
    }

    function safeGeometrySummary(geometry, confidence, reasons) {
        return Object.freeze({
            category: geometry && geometry.source === 'customer_dimensions' ? 'Customer dimensions' : 'Model analysis',
            confidence: confidence,
            reviewReasons: Object.freeze(reasons.slice())
        });
    }

    function safeStockPlan(stock, confidence, reasons) {
        return Object.freeze({
            strategy: typeof stock.strategy === 'string' ? stock.strategy : 'conservative_block',
            pieces: positiveInteger(stock.pieces) || 1,
            supplierShippingBeforeVat: isFiniteNonNegative(stock.supplierShippingBeforeVat)
                ? stock.supplierShippingBeforeVat : config.supplierShippingBeforeVat,
            bufferedPiecePriceBeforeVat: isFiniteNonNegative(stock.bufferedPiecePriceBeforeVat)
                ? stock.bufferedPiecePriceBeforeVat : 0,
            confidence: confidence,
            reviewReasons: Object.freeze(reasons.slice())
        });
    }

    function safeSetupPlan(plan) {
        var setups = Array.isArray(plan.setups) ? plan.setups : [];
        var fixture = plan.fixtureStrategy && plan.fixtureStrategy.required === true
            ? plan.fixtureStrategy : null;
        return Object.freeze({
            setupCount: setups.length > 0 ? setups.length : 1,
            category: fixture ? 'Two-setup mating fixture machining'
                : setups.length > 1 ? 'Multi-setup machining' : 'Standard machining',
            fixtureRequired: fixture !== null,
            fixtureType: fixture && fixture.type === 'machined_mating_fixture'
                ? fixture.type : null
        });
    }

    function safeOperationSummary(plan) {
        return Object.freeze({
            category: 'Machining, tooling, inspection and finishing',
            operationCount: Array.isArray(plan.operations) ? plan.operations.length : 0
        });
    }

    function failUnpriceable() {
        throw quoteError('cnc_quote_unpriceable');
    }

    async function requireCurrentValidatedPlan(input) {
        var plan = input && input.plan;
        var geometry = input && input.geometry || {};
        var inheritedUnresolved = plan && plan.featureGraph
            && Array.isArray(plan.featureGraph.unresolved) ? plan.featureGraph.unresolved : [];
        var compiledUnresolved = plan && plan.operationGraph
            && Array.isArray(plan.operationGraph.unresolved) ? plan.operationGraph.unresolved : [];
        if (!plan || plan.contract !== 'ValidatedManufacturingPlan.v1'
            || typeof plan.planHash !== 'string' || !plan.planHash.trim()
            || typeof plan.geometryRevision !== 'string' || !plan.geometryRevision.trim()
            || plan.geometryRevision !== geometry.geometryRevision
            || typeof plan.requirementsRevision !== 'string' || !plan.requirementsRevision.trim()
            || plan.requirementsRevision !== input.requirementsRevision
            || plan.plannerVersion !== 'cnc-feature-planner-v3'
            || plan.toolLibraryVersion !== window.CncToolLibrary.version
            || !plan.featureGraph || !plan.operationGraph || !plan.setupPlan
            || inheritedUnresolved.some(function (item) { return !item || item.required !== false; })
            || compiledUnresolved.some(function (item) { return !item || item.required !== false; })
            || (Array.isArray(plan.unresolvedReasons) && plan.unresolvedReasons.length > 0)) {
            throw quoteError('cnc_validated_plan_required');
        }
        try {
            await planContracts.validatePlan(plan);
        } catch (error) {
            throw quoteError('cnc_validated_plan_required');
        }
        return plan;
    }

    function hasPriceableStock(stock, landedStock) {
        var reviewReasons = Array.isArray(stock.reviewReasons) ? stock.reviewReasons : [];
        return isFiniteNonNegative(landedStock)
            && landedStock > 0
            && landedStock <= maximumMoney
            && positiveInteger(stock.pieces) !== null
            && isFiniteNonNegative(stock.bufferedPiecePriceBeforeVat)
            && stock.bufferedPiecePriceBeforeVat > 0
            && stock.strategy !== 'unsupported_shape'
            && reviewReasons.indexOf('no_priceable_facts') === -1;
    }

    async function quote(input) {
        input = input || {};
        var validatedPlan = await requireCurrentValidatedPlan(input);
        var stock = input.stockPlan || input.stock || {};
        var plan = input.commercialPlan || {};
        var geometry = input.geometrySummary || input.geometry || {};
        var quantity = positiveInteger(input.quantity)
            || positiveInteger(input.quantityPlan && input.quantityPlan.quantity);
        var landedStock = stock.landedStockBeforeVat;
        var reviewReasons = [];
        var confidence = 'High';

        if (!quantity || !hasPriceableStock(stock, landedStock)
            || plan.contract !== validatedPlan.contract
            || plan.planHash !== validatedPlan.planHash
            || plan.geometryRevision !== validatedPlan.geometryRevision
            || plan.requirementsRevision !== validatedPlan.requirementsRevision
            || plan.plannerVersion !== validatedPlan.plannerVersion
            || plan.toolLibraryVersion !== validatedPlan.toolLibraryVersion) {
            failUnpriceable();
        }

        confidence = mergeEvidence(reviewReasons, confidence, geometry);
        confidence = mergeEvidence(reviewReasons, confidence, stock);
        confidence = mergeEvidence(reviewReasons, confidence, plan);
        var capability = plan.machineCapability || machineCapability.classify(plan);
        var pricingPolicy = machineCapability.resolvePricingPolicy(capability, commercial);
        if (!pricingPolicy.priceable) {
            throw quoteError(pricingPolicy.errorCode);
        }

        var minutesPerPart = plan.totalMinutesPerPart;
        if (!isFiniteNonNegative(minutesPerPart) || minutesPerPart > maximumMoney) {
            minutesPerPart = config.planning.setupMinutes + config.planning.probingMinutesPerSetup
                + config.planning.handlingMinutesPerPart + config.planning.deburrMinutesPerPart;
            confidence = 'Low';
            addReason(reviewReasons, 'invalid_commercial_input');
        }
        var hasScopedMinutes = isFiniteNonNegative(plan.batchMinutes)
            && isFiniteNonNegative(plan.cycleMinutesPerPart)
            && plan.batchMinutes + plan.cycleMinutesPerPart <= maximumMoney;
        var batchMinutes = hasScopedMinutes ? plan.batchMinutes : 0;
        var cycleMinutesPerPart = hasScopedMinutes ? plan.cycleMinutesPerPart : minutesPerPart;
        var totalOrderMinutes = batchMinutes + (cycleMinutesPerPart * quantity);
        if (!isFiniteNonNegative(totalOrderMinutes) || totalOrderMinutes > maximumMoney) {
            failUnpriceable();
        }
        var setupCount = Array.isArray(plan.setups) && plan.setups.length > 0 ? plan.setups.length : 1;
        var pricedMinutes = totalOrderMinutes + (pricingPolicy.probingMinutesPerSetup * setupCount);
        var effectiveMinutesPerPart = pricedMinutes / quantity;
        var allowanceInputs = [plan.toolingAllowanceBeforeVat, plan.workholdingAllowanceBeforeVat, plan.inspectionAllowanceBeforeVat];
        var hasInvalidAllowance = !allowanceInputs.every(function (allowance) {
            return isFiniteNonNegative(allowance);
        });
        var tooling = isFiniteNonNegative(plan.toolingAllowanceBeforeVat) ? plan.toolingAllowanceBeforeVat : 0;
        var workholding = isFiniteNonNegative(plan.workholdingAllowanceBeforeVat) ? plan.workholdingAllowanceBeforeVat : 0;
        var inspection = isFiniteNonNegative(plan.inspectionAllowanceBeforeVat) ? plan.inspectionAllowanceBeforeVat : 0;
        if (!isFiniteNonNegative(setupCount)) {
            failUnpriceable();
        }
        if (hasInvalidAllowance) {
            confidence = 'Low';
            addReason(reviewReasons, 'invalid_commercial_input');
        }

        var selectedMaterialCode = input.material || input.alloy;
        if (input.material && input.alloy && input.material !== input.alloy) {
            throw finishError('cnc_finish_material_invalid');
        }
        if (input.finish && (!selectedMaterialCode || !selectedMaterial(selectedMaterialCode))) {
            throw finishError('cnc_finish_material_invalid');
        }
        if (input.finish && ((input.finish.material && input.finish.material !== selectedMaterialCode)
            || (input.finish.alloy && input.finish.alloy !== selectedMaterialCode))) {
            throw finishError('cnc_finish_material_invalid');
        }
        var finishInput = input.finish
            ? Object.assign({}, input.finish, { material: selectedMaterialCode, alloy: selectedMaterialCode })
            : null;
        var finishBeforeVat = estimateFinishBeforeVat(finishInput, quantity);
        var certificateBeforeVat = input.certificateRequired === true
            ? commercial.materialCertificateSupplierBeforeVat
            : 0;
        if (!isFiniteNonNegative(certificateBeforeVat)) {
            failUnpriceable();
        }
        var selectedFinishRule = input.finish && finishRule(input.finish.code);
        if (selectedFinishRule) {
            confidence = lowerConfidence(confidence, normalizedConfidence(selectedFinishRule.confidence));
            if (input.finish.requiresReview === true
                || input.finish.customColor === true
                || input.finish.maskingRequired === true
                || input.finish.cosmeticRequirement === true
                || input.finish.certificationRequired === true) {
                confidence = lowerConfidence(confidence, 'Low');
            }
        }

        var machiningBeforeVat = pricedMinutes * pricingPolicy.machineMinuteCustomerRateBeforeVat;
        var batchBase = landedStock
            + pricingPolicy.camAllowanceBeforeVat
            + (pricingPolicy.setupAllowanceBeforeVat * setupCount)
            + pricingPolicy.riskAllowanceBeforeVat
            + tooling
            + workholding
            + commercial.inspectionBaseBeforeVat
            + inspection
            + commercial.deburrBatchBeforeVat
            + certificateBeforeVat
            + finishBeforeVat;
        var perPartPostProcessingBeforeVat = commercial.deburrPerPartBeforeVat * quantity;
        var subtotalBeforeReserve = batchBase + machiningBeforeVat + perPartPostProcessingBeforeVat;
        var withReserve = subtotalBeforeReserve * (1 + commercial.scrapReserveRate);
        var adjustedBeforeMinimum = withReserve * commercial.calibratedCommercialAdjustment;
        var minimumContributionRate = commercial.minimumContributionRecoveryBeforeVatPerMachineHour;
        if (!isFiniteNonNegative(minimumContributionRate) || minimumContributionRate <= 0) {
            failUnpriceable();
        }
        var directExternalBeforeVat = landedStock + tooling + workholding + certificateBeforeVat + finishBeforeVat;
        var requiredContributionRecovery = (pricedMinutes / 60) * minimumContributionRate;
        var contributionFloorBeforeVat = directExternalBeforeVat + requiredContributionRecovery;
        if (!isFiniteNonNegative(contributionFloorBeforeVat) || contributionFloorBeforeVat > maximumMoney) {
            failUnpriceable();
        }
        var afterContributionFloor = Math.max(adjustedBeforeMinimum, contributionFloorBeforeVat);
        var afterMinimum = Math.max(commercial.minimumOrderBeforeVat, afterContributionFloor);
        var beforeVat = upwardRound(afterMinimum, commercial.roundToThb);
        var afterVat = beforeVat === null ? null : upwardRound(
            (beforeVat * (1 + config.vatRate)) * (1 + commercial.paymentFeeRate),
            commercial.roundToThb);
        if (beforeVat === null || afterVat === null) {
            failUnpriceable();
        }
        var shippingBeforeVat = isFiniteNonNegative(stock.supplierShippingBeforeVat)
            ? stock.supplierShippingBeforeVat : config.supplierShippingBeforeVat;
        var materialBeforeVat = landedStock - shippingBeforeVat;
        if (!isFiniteNonNegative(materialBeforeVat)) { failUnpriceable(); }
        var lineItems = Object.freeze([
            lineItem('material', 'Stock material', materialBeforeVat),
            lineItem('shipping', 'Supplier shipping', shippingBeforeVat),
            lineItem('cam', 'CAM programming', pricingPolicy.camAllowanceBeforeVat),
            lineItem('setup', 'Machine setup', pricingPolicy.setupAllowanceBeforeVat * setupCount),
            lineItem('machine_risk', 'Machine-class risk allowance', pricingPolicy.riskAllowanceBeforeVat),
            lineItem('machining', 'Machining time', machiningBeforeVat + perPartPostProcessingBeforeVat),
            lineItem('tooling', 'Tooling', tooling),
            lineItem('workholding', plan.fixtureStrategy && plan.fixtureStrategy.required === true
                ? 'Custom mating fixture (batch)' : 'Workholding', workholding),
            lineItem('inspection', 'Inspection', commercial.inspectionBaseBeforeVat + inspection),
            lineItem('material_certificate', 'Supplier material certificate', certificateBeforeVat),
            lineItem('deburr', 'Deburr and post-processing', commercial.deburrBatchBeforeVat),
            lineItem('finish', 'Selected finish', finishBeforeVat, finishInput && finishInput.code ? finishInput.code : 'none'),
            lineItem('commercial_adjustment', 'Published commercial adjustment', adjustedBeforeMinimum - subtotalBeforeReserve),
            lineItem('contribution_floor_adjustment', 'Commercial price protection', afterContributionFloor - adjustedBeforeMinimum),
            lineItem('minimum_order_adjustment', 'Minimum order adjustment', afterMinimum - afterContributionFloor),
            lineItem('rounding_adjustment', 'Commercial rounding', beforeVat - afterMinimum)
        ]);

        return Object.freeze({
            estimateStatus: 'Preliminary',
            clientGenerated: true,
            calculatedAtUtc: new Date().toISOString(),
            versions: copyVersions(),
            geometrySummary: safeGeometrySummary(geometry, normalizedConfidence(geometry.confidence), geometry.reviewReasons || []),
            stockPlan: safeStockPlan(stock, normalizedConfidence(stock.confidence), stock.reviewReasons || []),
            setupPlan: safeSetupPlan(plan),
            operationSummary: safeOperationSummary(plan),
            quantityPlan: Object.freeze({ quantity: quantity, category: quantity > 1 ? 'Batch quantity' : 'Single part' }),
            manufacturingEvidence: safeManufacturingEvidence(stock, plan),
            lineItems: lineItems,
            confidence: confidence,
            reviewReasons: Object.freeze(reviewReasons),
            estimatedMinutesPerPart: effectiveMinutesPerPart,
            estimatedPriceBeforeVat: beforeVat,
            vatRate: config.vatRate,
            estimatedPriceAfterVat: afterVat,
            currency: 'THB'
        });
    }

    window.CncQuoteEngine = Object.freeze({
        quote: quote,
        estimateFinishBeforeVat: estimateFinishBeforeVat
    });
})(window);
