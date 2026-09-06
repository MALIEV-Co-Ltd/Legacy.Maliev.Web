(function (root) {
root.CncQuotationConfig = Object.freeze({
    estimatorVersion: 'cnc-local-v2',
    stockRulesVersion: 'cnc-stock-th-2026-08-27-r2',
    materialPriceModelVersion: 'cnc-material-procurement-risk-2026-08-27',
    materialCatalogVersion: 'cnc-materials-th-supplier-backed-2026-08-29-r3',
    toolLibraryVersion: 'cnc-tools-hss-spotting-2026-09-03-r3',
    reachRulesVersion: 'cnc-reach-connected-flute-contact-2026-09-03-r5',
    finishingRulesVersion: 'cnc-finishing-connected-feature-facing-2026-09-03-r4',
    commercialRulesVersion: 'cnc-commercial-v6',
    densitiesKgPerMm3: Object.freeze({ '6061': 2.70 / 1000000, '7075': 2.81 / 1000000 }),
    standardThicknessMm: Object.freeze([2, 3, 4, 5, 6.35, 10, 12, 12.7, 15, 16, 20, 25, 25.4, 31.75, 38.1, 40, 50.8, 60, 75, 80, 90, 100, 105, 110, 120]),
    allowancesMm: Object.freeze({ top: 2, side: 4, bottom: 8, clampBorder: 25 }),
    // Purchase rectangular blanks in practical 5 mm planar increments.
    stockIncrementsMm: Object.freeze({ planar: 5 }),
    thinPlate: Object.freeze({
        minPartThickness: 2,
        maxPartThickness: 5,
        eligibilityToleranceMm: 0.01,
        interPartClearanceMm: 2,
        bowingRiskSpanToThicknessRatio: 20,
        blanks: Object.freeze([Object.freeze([150, 150]), Object.freeze([300, 300])]),
        observedCutFloorThb: 120
    }),
    roundStock: Object.freeze({
        diametersMm: Object.freeze([6, 8, 10, 12, 12.7, 16, 20, 25, 25.4, 31.75, 38.1, 40, 50.8, 60, 75, 80, 90, 100, 105, 110, 114.3, 120]),
        additionalDiametersByMaterial: Object.freeze({
            '6061': Object.freeze([127, 139.7, 152.4, 165.1, 177.8, 190.5, 203.2, 215.9, 228.6, 254, 279.4]),
            '7075': Object.freeze([125, 139.7, 150, 152.4])
        }),
        radialAllowanceMm: 2,
        facingAllowanceMm: 2,
        gripAllowanceMm: 6,
        cutLengthIncrementMm: 5,
        // Public round-bar catalogs validate the kg-per-m mass basis but do not provide a
        // dependable Thailand cut quote. These rates therefore exceed the rectangular supplier
        // slopes, retain a cut floor, and carry a separate mandatory-review risk allowance.
        procurementByMaterial: Object.freeze({
            '6061': Object.freeze({
                thbPerKg: 350,
                minimumCutBeforeVat: 120,
                riskRate: 0.30,
                minimumRiskBeforeVat: 40,
                evidence: Object.freeze({
                    mass: 'hakudo_thailand_6061_t651_round_catalog_2025-10-06',
                    procurement: 'maliev_rectangular_rate_uplift_and_observed_cut_floor'
                })
            }),
            '7075': Object.freeze({
                thbPerKg: 500,
                minimumCutBeforeVat: 160,
                riskRate: 0.35,
                minimumRiskBeforeVat: 60,
                evidence: Object.freeze({
                    mass: 'hakudo_thailand_7075_t651_round_catalog_2025-10-06',
                    procurement: 'maliev_7075_rate_uplift_and_conservative_cut_floor'
                })
            })
        })
    }),
    planning: Object.freeze({
        maxAutomaticSetups: 3,
        mrrMm3PerMinute: Object.freeze({ '6061': 18000, '7075': 12000 }),
        finishMm2PerMinute: Object.freeze({ '6061': 1400, '7075': 950 }),
        setupMinutes: 35,
        probingMinutesPerSetup: 8,
        toolChangeMinutes: 1.5,
        handlingMinutesPerPart: 6,
        deburrMinutesPerPart: 8,
        utilizationFactor: 0.72,
        // Conservative quote benchmark, not a claim about the assigned machine.
        // Published ball-tool feeds are scaled with rpm to retain chip load.
        ballRestSpindleLimitRpm: 12000,
        standardToolingAllowanceBeforeVat: 150,
        longReachToolingAllowanceBeforeVat: 450,
        specialToolAllowanceBeforeVat: 1500,
        standardWorkholdingAllowanceBeforeVat: 100,
        softJawAllowanceBeforeVat: 900,
        customFixtureAllowanceBeforeVat: 2500,
        matingFixture: Object.freeze({
            maximumBoxFillRatio: 0.25,
            minimumPlanarAreaRatio: 0.45,
            minimumPartSpanMm: 120,
            minimumOpposedCoverage: 0.55,
            materialCode: '6061',
            stockForm: 'plate',
            planarMarginMm: 15,
            supportDepthMm: 18,
            cutterClearanceMm: 2,
            jawDepthMm: 12,
            jawEndClearanceMm: 8,
            minimumSupportedAreaRatio: 0.45,
            maximumFacingFieldGapRatio: 0.06,
            blankThicknessMm: 25,
            stockIncrementMm: 5,
            facingStockMm: 1,
            matingDepthRatio: 0.10,
            minimumMatingDepthMm: 4,
            maximumMatingDepthMm: 12,
            minimumMatingFootprintFactor: 0.25,
            maximumMatingFootprintFactor: 0.65,
            setupCount: 1,
            mountingHoleCount: 4,
            mountingHoleMinutes: 2,
            toolFamilyCount: 3,
            designAndCamMinutes: 45,
            proveOutMinutes: 20,
            hardwareBeforeVat: 350,
            contingencyRate: 0.15,
            roundToThb: 10
        })
    }),
    commercial: Object.freeze({
        minimumOrderBeforeVat: 2500,
        machineClasses: Object.freeze({
            threeAxis: Object.freeze({
                machineMinuteCustomerRateBeforeVat: 18,
                camAllowanceBeforeVat: 750,
                setupAllowanceBeforeVat: 600,
                probingMinutesPerSetup: 0,
                riskAllowanceBeforeVat: 0
            }),
            fiveAxis: Object.freeze({
                configured: false,
                machineMinuteCustomerRateBeforeVat: null,
                camAllowanceBeforeVat: null,
                setupAllowanceBeforeVat: null,
                probingMinutesPerSetup: null,
                riskAllowanceBeforeVat: null
            })
        }),
        // Public commercial safety floor after direct external job costs. This is deliberately
        // not an internal cost, utilization, or margin disclosure. Production approval still
        // requires MALIEV's actual fully burdened machine cost to remain below this recovery.
        minimumContributionRecoveryBeforeVatPerMachineHour: 2400,
        inspectionBaseBeforeVat: 200,
        // Conservative standard supplier document allowance per quoted part/material batch.
        // Special testing, EN 10204 3.2, and third-party witnessing remain engineering review.
        materialCertificateSupplierBeforeVat: 750,
        deburrBatchBeforeVat: 100,
        deburrPerPartBeforeVat: 30,
        scrapReserveRate: 0.12,
        calibratedCommercialAdjustment: 1.25,
        paymentFeeRate: 0,
        roundToThb: 10
    }),
    finishing: Object.freeze({
        rules: Object.freeze([
            Object.freeze({
                code: 'anodize-clear',
                minimumBatchBeforeVat: 900,
                thbPerDm2: 18,
                handlingPerPartBeforeVat: 25,
                compatibleMaterialFamilies: Object.freeze(['aluminum']),
                confidence: 'Medium'
            }),
            Object.freeze({
                code: 'sand-blast',
                minimumBatchBeforeVat: 1200,
                thbPerDm2: 40,
                handlingPerPartBeforeVat: 35,
                compatibleMaterialFamilies: Object.freeze([
                    'aluminum',
                    'carbon_steel',
                    'alloy_steel',
                    'stainless_steel'
                ]),
                media: 'fine_glass_bead',
                confidence: 'Medium'
            })
        ])
    }),
    supplierShippingBeforeVat: 200,
    procurementBuffer: 1.05,
    benchmarkProcurementRisk: Object.freeze({
        rate: 0.25,
        minimumBeforeVat: 50
    }),
    vatRate: 0.07
});
}(typeof window !== 'undefined' ? window : self));
