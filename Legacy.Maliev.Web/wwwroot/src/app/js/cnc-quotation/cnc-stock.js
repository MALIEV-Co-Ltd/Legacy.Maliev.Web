(function (window) {
    'use strict';

    var config = window.CncQuotationConfig;
    if (!config) {
        throw new Error('CNC quotation configuration must load before the stock model.');
    }

    var materialCatalog = window.CncMaterialCatalog;
    if (!materialCatalog) {
        throw new Error('The CNC material catalog must load before the stock model.');
    }

    var observedMassRangesKg = Object.freeze({
        '6061': Object.freeze({ min: 0.061, max: 34.733 }),
        '7075': Object.freeze({ min: 0.095, max: 4.957 })
    });
    var stockShapeTieBreakOrder = Object.freeze({ block: 0, plate: 1, round: 2 });

    function ceil10(value) {
        return Math.ceil(value / 10) * 10;
    }

    function expectedPiecePriceBeforeVat(alloy, massKg) {
        if (alloy === '6061') {
            return ceil10(Math.max(90, 20.875 + 269.519 * massKg - 1.29685 * massKg * massKg));
        }
        if (alloy === '7075') {
            return ceil10(Math.max(120, 5 + 360 * massKg));
        }
        throw new Error('Unsupported CNC stock alloy: ' + alloy);
    }

    function nextThickness(minimumThicknessMm) {
        for (var index = 0; index < config.standardThicknessMm.length; index++) {
            var thickness = config.standardThicknessMm[index];
            if (thickness >= minimumThicknessMm) {
                return thickness;
            }
        }

        return null;
    }

    function nextRoundDiameter(materialCode, minimumDiameterMm) {
        var additional = config.roundStock.additionalDiametersByMaterial
            && config.roundStock.additionalDiametersByMaterial[materialCode];
        var diameters = config.roundStock.diametersMm.concat(Array.isArray(additional) ? additional : [])
            .filter(function (diameter, index, values) { return values.indexOf(diameter) === index; })
            .sort(function (left, right) { return left - right; });
        for (var index = 0; index < diameters.length; index++) {
            var diameter = diameters[index];
            if (diameter >= minimumDiameterMm) {
                return diameter;
            }
        }

        return null;
    }

    function roundUp(value, increment) {
        return Math.ceil((value - Number.EPSILON) / increment) * increment;
    }

    function validPositiveNumber(value) {
        return typeof value === 'number' && Number.isFinite(value) && value > 0;
    }

    function partHasSupportedBoundingBox(partSizeMm) {
        return partSizeMm
            && validPositiveNumber(partSizeMm.x)
            && validPositiveNumber(partSizeMm.y)
            && validPositiveNumber(partSizeMm.z);
    }

    function createUnsupportedPlan(quantity) {
        return {
            strategy: 'unsupported_shape',
            stockShape: 'unsupported',
            stockSizeMm: { x: 0, y: 0, z: 0 },
            diameterMm: null,
            lengthMm: null,
            orientationFrame: null,
            pieces: quantity,
            partsPerBlank: 0,
            interPartClearanceMm: 0,
            usableBlankSizeMm: null,
            stockVolumeMm3: 0,
            massKgPerPiece: 0,
            expectedPiecePriceBeforeVat: 0,
            bufferedPiecePriceBeforeVat: 0,
            supplierShippingBeforeVat: 0,
            landedStockBeforeVat: 0,
            removalVolumeMm3: 0,
            workholdingAllowanceBeforeVat: 0,
            riskAllowanceBeforeVat: 0,
            riskAllowanceReason: null,
            priceModel: null,
            priceEvidence: null,
            priceRateBeforeVatPerKg: null,
            minimumCutBeforeVat: null,
            scoreBeforeVat: 0,
            feasible: false,
            rank: 0,
            selected: false,
            candidates: [],
            selectionReason: 'no_feasible_stock_candidate',
            confidence: 'Low',
            reviewReasons: ['unsupported_shape']
        };
    }

    function selectThinPlateBlank(partSizeMm, clampBorderMm, interPartClearanceMm) {
        var usableBorder = clampBorderMm * 2;
        for (var index = 0; index < config.thinPlate.blanks.length; index++) {
            var blank = config.thinPlate.blanks[index];
            var usableX = blank[0] - usableBorder;
            var usableY = blank[1] - usableBorder;
            var partsAcrossX = Math.floor((usableX + interPartClearanceMm) / (partSizeMm.x + interPartClearanceMm));
            var partsAcrossY = Math.floor((usableY + interPartClearanceMm) / (partSizeMm.y + interPartClearanceMm));
            var partsPerBlank = partsAcrossX * partsAcrossY;
            if (partsPerBlank > 0) {
                return {
                    x: blank[0],
                    y: blank[1],
                    partsPerBlank: partsPerBlank,
                    usableBlankSizeMm: { x: usableX, y: usableY }
                };
            }
        }

        return null;
    }

    function addReason(reasons, reason) {
        if (reasons.indexOf(reason) < 0) {
            reasons.push(reason);
        }
    }

    function materialCode(input) {
        return typeof input.material === 'string' && input.material.length > 0
            ? input.material
            : input.alloy;
    }

    function candidateConfidence(material, stockShape, massKg, priceEstimate, reviewReasons) {
        var confidence = priceEstimate.confidence;
        var observedRange = priceEstimate.model === 'rectangular_calibrated_curve'
            ? observedMassRangesKg[material.code] : null;
        if (observedRange && (massKg < observedRange.min || massKg > observedRange.max)) {
            addReason(reviewReasons, 'outside_material_model_range');
            confidence = 'Low';
        }

        if (priceEstimate.supplierReviewRequired || stockShape === 'round') {
            addReason(reviewReasons, 'low_stock_confidence');
        }
        if (stockShape === 'round') {
            confidence = 'Low';
        }

        return confidence;
    }

    function materialRoughingRate(material) {
        var configuredRate = config.planning.mrrMm3PerMinute[material.code];
        if (validPositiveNumber(configuredRate)) {
            return configuredRate;
        }

        return materialCatalog.adjustRate(
            material.code,
            config.planning.mrrMm3PerMinute['6061'],
            'mrr');
    }

    function identityAxes() {
        return [
            { x: 1, y: 0, z: 0 },
            { x: 0, y: 1, z: 0 },
            { x: 0, y: 0, z: 1 }
        ];
    }

    function validAxis(axis) {
        return axis
            && Number.isFinite(axis.x)
            && Number.isFinite(axis.y)
            && Number.isFinite(axis.z)
            && ((axis.x * axis.x) + (axis.y * axis.y) + (axis.z * axis.z)) > 0;
    }

    function stockOrientation(partSizeMm, principalAxes, canonicalizeThickness) {
        var suppliedAxes = Array.isArray(principalAxes) && principalAxes.length === 3
            && principalAxes.every(validAxis) ? principalAxes : identityAxes();
        var dimensions = [partSizeMm.x, partSizeMm.y, partSizeMm.z];
        var entries = dimensions.map(function (dimension, index) {
            return { dimension: dimension, axis: suppliedAxes[index], sourceIndex: index };
        });
        if (canonicalizeThickness) {
            entries.sort(function (left, right) {
                return right.dimension - left.dimension || left.sourceIndex - right.sourceIndex;
            });
        }

        return {
            partSizeMm: {
                x: entries[0].dimension,
                y: entries[1].dimension,
                z: entries[2].dimension
            },
            frame: {
                xAxis: { x: entries[0].axis.x, y: entries[0].axis.y, z: entries[0].axis.z },
                yAxis: { x: entries[1].axis.x, y: entries[1].axis.y, z: entries[1].axis.z },
                zAxis: { x: entries[2].axis.x, y: entries[2].axis.y, z: entries[2].axis.z }
            }
        };
    }

    function estimateCandidatePrice(material, massKgPerPiece, stockShape, catalogForm) {
        var roundPolicy = stockShape === 'round' && config.roundStock.procurementByMaterial
            ? config.roundStock.procurementByMaterial[material.code] : null;
        if (roundPolicy) {
            return {
                priceBeforeVat: ceil10(Math.max(roundPolicy.minimumCutBeforeVat, massKgPerPiece * roundPolicy.thbPerKg)),
                confidence: 'Low',
                supplierReviewRequired: true,
                model: 'round_benchmark_mass',
                evidence: roundPolicy.evidence,
                thbPerKg: roundPolicy.thbPerKg,
                minimumCutBeforeVat: roundPolicy.minimumCutBeforeVat,
                riskRate: roundPolicy.riskRate,
                minimumRiskBeforeVat: roundPolicy.minimumRiskBeforeVat
            };
        }

        var catalogEstimate = materialCatalog.estimateStockBeforeVat(material.code, massKgPerPiece, catalogForm);
        return {
            priceBeforeVat: catalogEstimate.priceBeforeVat,
            confidence: catalogEstimate.confidence,
            supplierReviewRequired: catalogEstimate.supplierReviewRequired,
            model: material.procurement.mode === 'calibrated_curve'
                ? 'rectangular_calibrated_curve' : 'catalog_benchmark_mass',
            evidence: material.procurement.mode
        };
    }

    function createCandidate(input) {
        var material = input.material;
        var pieces = Math.ceil(input.quantity / input.partsPerBlank);
        var stockVolumeMm3 = input.stockVolumeMm3;
        var massKgPerPiece = stockVolumeMm3 * material.densityKgPerMm3;
        var priceEstimate = estimateCandidatePrice(material, massKgPerPiece, input.stockShape, input.catalogForm);
        var expectedPrice = priceEstimate.priceBeforeVat;
        if (input.stockShape === 'plate'
            && (input.stockSizeMm.z === 4 || input.stockSizeMm.z === 5)
            && input.stockSizeMm.x === 150
            && input.stockSizeMm.y === 150) {
            expectedPrice = Math.max(expectedPrice, config.thinPlate.observedCutFloorThb);
        }

        var bufferedPrice = expectedPrice * config.procurementBuffer;
        var supplierShippingBeforeVat = input.includeShipping === false ? 0 : config.supplierShippingBeforeVat;
        var removalVolumeMm3 = Math.max(
            0,
            (stockVolumeMm3 * pieces) - (input.partVolumeMm3 * input.quantity));
        var workholdingAllowanceBeforeVat = validPositiveNumber(input.workholdingAllowanceBeforeVat)
            ? input.workholdingAllowanceBeforeVat
            : config.planning.standardWorkholdingAllowanceBeforeVat;
        var benchmarkRisk = priceEstimate.supplierReviewRequired === true
            ? config.benchmarkProcurementRisk : null;
        var riskRate = validPositiveNumber(priceEstimate.riskRate)
            ? priceEstimate.riskRate : benchmarkRisk && benchmarkRisk.rate;
        var minimumRiskBeforeVat = validPositiveNumber(priceEstimate.minimumRiskBeforeVat)
            ? priceEstimate.minimumRiskBeforeVat : benchmarkRisk && benchmarkRisk.minimumBeforeVat;
        var riskAllowanceBeforeVat = validPositiveNumber(riskRate)
            ? ceil10(Math.max(
                minimumRiskBeforeVat,
                expectedPrice * pieces * riskRate))
            : 0;
        var riskAllowanceReason = riskAllowanceBeforeVat > 0
            ? (priceEstimate.model === 'round_benchmark_mass'
                ? 'round_stock_supplier_confirmation'
                : 'benchmark_stock_supplier_confirmation')
            : null;
        var landedStockBeforeVat = (bufferedPrice * pieces)
            + riskAllowanceBeforeVat
            + supplierShippingBeforeVat;
        var reviewReasons = [];
        var confidence = candidateConfidence(material, input.stockShape, massKgPerPiece, priceEstimate, reviewReasons);
        var scoreBeforeVat = landedStockBeforeVat - riskAllowanceBeforeVat
            + (removalVolumeMm3 / materialRoughingRate(material))
                * config.commercial.machineClasses.threeAxis.machineMinuteCustomerRateBeforeVat
            + workholdingAllowanceBeforeVat;

        return {
            strategy: input.strategy,
            stockShape: input.stockShape,
            stockSizeMm: input.stockSizeMm,
            diameterMm: input.diameterMm,
            lengthMm: input.lengthMm,
            symmetryAxis: input.symmetryAxis || null,
            orientationFrame: input.orientationFrame || null,
            pieces: pieces,
            partsPerBlank: input.partsPerBlank,
            interPartClearanceMm: input.interPartClearanceMm || 0,
            usableBlankSizeMm: input.usableBlankSizeMm || null,
            stockVolumeMm3: stockVolumeMm3,
            massKgPerPiece: massKgPerPiece,
            expectedPiecePriceBeforeVat: expectedPrice,
            bufferedPiecePriceBeforeVat: bufferedPrice,
            supplierShippingBeforeVat: supplierShippingBeforeVat,
            landedStockBeforeVat: landedStockBeforeVat,
            removalVolumeMm3: removalVolumeMm3,
            workholdingAllowanceBeforeVat: workholdingAllowanceBeforeVat,
            riskAllowanceBeforeVat: riskAllowanceBeforeVat,
            riskAllowanceReason: riskAllowanceReason,
            priceModel: priceEstimate.model,
            priceEvidence: priceEstimate.evidence,
            priceRateBeforeVatPerKg: priceEstimate.thbPerKg || null,
            minimumCutBeforeVat: priceEstimate.minimumCutBeforeVat || null,
            scoreBeforeVat: scoreBeforeVat,
            feasible: true,
            rank: 0,
            selected: false,
            confidence: confidence,
            reviewReasons: reviewReasons
        };
    }

    function topologyDegraded(input) {
        return input.topologyDegraded === true
            || (input.analysisLimits && input.analysisLimits.topologyDegraded === true)
            || (input.geometry && input.geometry.analysisLimits && input.geometry.analysisLimits.topologyDegraded === true);
    }

    function rotationalEvidence(input) {
        return input.rotationalEvidence || (input.geometry && input.geometry.rotationalEvidence) || null;
    }

    function buildCandidates(input) {
        input = input || {};
        var quantity = Number.isInteger(input.quantity) && input.quantity > 0 ? input.quantity : 1;
        var geometry = input.geometry || {};
        var partSizeMm = input.partSizeMm || geometry.orientedSizeMm;
        if (!partHasSupportedBoundingBox(partSizeMm)) {
            return [];
        }

        var code = materialCode(input);
        var material = materialCatalog.get(code);
        if (!material || material.enabled !== true) {
            throw new Error('Unsupported CNC stock alloy: ' + code);
        }

        var maximumPartVolumeMm3 = partSizeMm.x * partSizeMm.y * partSizeMm.z;
        var suppliedPartVolumeMm3 = validPositiveNumber(input.partVolumeMm3)
            ? input.partVolumeMm3
            : geometry.partVolumeMm3;
        var partVolumeMm3 = validPositiveNumber(suppliedPartVolumeMm3)
            ? Math.min(suppliedPartVolumeMm3, maximumPartVolumeMm3)
            : maximumPartVolumeMm3;
        var candidates = [];
        var clampBorderMm = validPositiveNumber(input.clampBorderMm)
            ? input.clampBorderMm
            : config.allowancesMm.clampBorder;
        var stockForms = material.stockForms;
        var includeShipping = input.includeShipping !== false;
        var flatPlateEligible = typeof input.flatPlateEligible === 'boolean'
            ? input.flatPlateEligible
            : geometry.flatPlateEligible === true;
        var physicalThicknessMm = Math.min(partSizeMm.x, partSizeMm.y, partSizeMm.z);
        var thinPlateToleranceMm = validPositiveNumber(config.thinPlate.eligibilityToleranceMm)
            ? config.thinPlate.eligibilityToleranceMm : 0;
        var thinPlateEligible = flatPlateEligible
            && physicalThicknessMm >= config.thinPlate.minPartThickness - thinPlateToleranceMm
            && physicalThicknessMm <= config.thinPlate.maxPartThickness + thinPlateToleranceMm;
        var orientation = stockOrientation(
            partSizeMm,
            input.principalAxes || geometry.principalAxes,
            true);
        var stockPartSizeMm = orientation.partSizeMm;

        if (stockForms.indexOf('rectangular') >= 0) {
            var blockThicknessMm = nextThickness(Math.max(
                stockPartSizeMm.z + config.allowancesMm.top,
                config.allowancesMm.bottom));
            if (blockThicknessMm !== null) {
                var planarIncrementMm = config.stockIncrementsMm.planar;
                var blockSizeMm = {
                    x: roundUp(stockPartSizeMm.x + (config.allowancesMm.side * 2), planarIncrementMm),
                    y: roundUp(stockPartSizeMm.y + (config.allowancesMm.side * 2), planarIncrementMm),
                    z: blockThicknessMm
                };
                candidates.push(createCandidate({
                    material: material,
                    catalogForm: 'rectangular',
                    strategy: 'rectangular_block',
                    stockShape: 'block',
                    stockSizeMm: blockSizeMm,
                    diameterMm: null,
                    lengthMm: null,
                    orientationFrame: orientation.frame,
                    workholdingAllowanceBeforeVat: thinPlateEligible
                        ? config.planning.softJawAllowanceBeforeVat
                        : config.planning.standardWorkholdingAllowanceBeforeVat,
                    partsPerBlank: 1,
                    quantity: quantity,
                    includeShipping: includeShipping,
                    partVolumeMm3: partVolumeMm3,
                    stockVolumeMm3: blockSizeMm.x * blockSizeMm.y * blockSizeMm.z
                }));
            }
        }

        if (thinPlateEligible && stockForms.indexOf('plate') >= 0) {
            var interPartClearanceMm = config.thinPlate.interPartClearanceMm;
            var plateThicknessMm = nextThickness(stockPartSizeMm.z + config.allowancesMm.top);
            var thinPlateBlank = selectThinPlateBlank(stockPartSizeMm, clampBorderMm, interPartClearanceMm);
            if (plateThicknessMm !== null && thinPlateBlank) {
                var plateSizeMm = { x: thinPlateBlank.x, y: thinPlateBlank.y, z: plateThicknessMm };
                candidates.push(createCandidate({
                    material: material,
                    catalogForm: 'plate',
                    strategy: 'thin_plate_nesting',
                    stockShape: 'plate',
                    stockSizeMm: plateSizeMm,
                    diameterMm: null,
                    lengthMm: null,
                    orientationFrame: orientation.frame,
                    partsPerBlank: thinPlateBlank.partsPerBlank,
                    interPartClearanceMm: interPartClearanceMm,
                    usableBlankSizeMm: thinPlateBlank.usableBlankSizeMm,
                    quantity: quantity,
                    includeShipping: includeShipping,
                    partVolumeMm3: partVolumeMm3,
                    stockVolumeMm3: plateSizeMm.x * plateSizeMm.y * plateSizeMm.z
                }));
            }
        }

        var rotation = rotationalEvidence(input);
        var reliableRotation = rotation
            && rotation.eligible === true
            && rotation.confidence !== 'Low'
            && validPositiveNumber(rotation.diameterMm)
            && validPositiveNumber(rotation.lengthMm)
            && !topologyDegraded(input);
        if (reliableRotation && stockForms.indexOf('round') >= 0) {
            var roundDiameterMm = nextRoundDiameter(material.code, rotation.diameterMm + config.roundStock.radialAllowanceMm);
            var roundLengthMm = roundUp(
                rotation.lengthMm + config.roundStock.facingAllowanceMm + config.roundStock.gripAllowanceMm,
                config.roundStock.cutLengthIncrementMm);
            if (roundDiameterMm !== null && validPositiveNumber(roundLengthMm)) {
                var roundSizeMm = { x: roundDiameterMm, y: roundDiameterMm, z: roundLengthMm };
                candidates.push(createCandidate({
                    material: material,
                    catalogForm: 'round',
                    strategy: 'round_bar',
                    stockShape: 'round',
                    stockSizeMm: roundSizeMm,
                    diameterMm: roundDiameterMm,
                    lengthMm: roundLengthMm,
                    symmetryAxis: rotation.axis || null,
                    partsPerBlank: 1,
                    quantity: quantity,
                    includeShipping: includeShipping,
                    partVolumeMm3: partVolumeMm3,
                    stockVolumeMm3: Math.PI * Math.pow(roundDiameterMm / 2, 2) * roundLengthMm
                }));
            }
        }

        return candidates;
    }

    function selectStock(input) {
        input = input || {};
        var quantity = Number.isInteger(input.quantity) && input.quantity > 0 ? input.quantity : 1;
        var candidates = buildCandidates(input).slice().sort(function (left, right) {
            if (left.scoreBeforeVat !== right.scoreBeforeVat) {
                return left.scoreBeforeVat - right.scoreBeforeVat;
            }
            if (left.removalVolumeMm3 !== right.removalVolumeMm3) {
                return left.removalVolumeMm3 - right.removalVolumeMm3;
            }
            return stockShapeTieBreakOrder[left.stockShape] - stockShapeTieBreakOrder[right.stockShape];
        }).map(function (candidate, index) {
            return Object.assign({}, candidate, {
                rank: index + 1,
                selected: index === 0
            });
        });
        if (candidates.length === 0) {
            return createUnsupportedPlan(quantity);
        }

        return Object.assign({}, candidates[0], {
            candidates: candidates,
            selectionReason: 'lowest_conservative_score'
        });
    }

    window.CncStock = Object.freeze({
        ceil10: ceil10,
        expectedPiecePriceBeforeVat: expectedPiecePriceBeforeVat,
        nextThickness: nextThickness,
        buildCandidates: buildCandidates,
        selectStock: selectStock
    });
})(window);
