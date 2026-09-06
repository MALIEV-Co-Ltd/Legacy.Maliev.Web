(function (root) {
    'use strict';

    var config = root.CncQuotationConfig;
    var materialCatalog = root.CncMaterialCatalog;
    if (!config || !materialCatalog) {
        throw new Error('CNC quotation configuration and material catalog must load before the tool library.');
    }

    function freezeRecord(value) {
        Object.keys(value).forEach(function (key) {
            var child = value[key];
            if (child && typeof child === 'object' && !Object.isFrozen(child)) {
                freezeRecord(child);
            }
        });
        return Object.freeze(value);
    }

    function tool(record) {
        record.enabled = true;
        record.timeMultiplier = Number.isFinite(record.timeMultiplier) ? record.timeMultiplier : 1;
        record.wearMultiplier = Number.isFinite(record.wearMultiplier) ? record.wearMultiplier : 1;
        record.confidence = record.confidence || 'Medium';
        return freezeRecord(record);
    }

    var allMaterials = Object.freeze(['metal', 'plastic']);
    var millingDiameters = Object.freeze([1, 2, 3, 4, 5, 6, 8, 10, 12, 16]);
    // The common 10 mm cutter needs its own clearance envelope; rounding it to
    // 16 mm rejects openings that admit the physical cutter.
    var analysisDiameters = Object.freeze([1, 2, 4, 8, 10, 16]);
    var analysisReachRatios = Object.freeze([2, 10]);

    function decimalId(value) {
        return Number(value).toFixed(1).replace('.', 'p');
    }

    function ceilingFrom(values, value) {
        for (var index = 0; index < values.length; index++) {
            if (values[index] >= value) { return values[index]; }
        }
        return values[values.length - 1];
    }

    function conservativeReachRatio(value) {
        var selected = analysisReachRatios[0];
        analysisReachRatios.forEach(function (candidate) {
            if (candidate <= value) { selected = candidate; }
        });
        return selected;
    }

    function millingProfileId(diameterMm, reachRatio) {
        return 'analysis-flat-' + ceilingFrom(analysisDiameters, diameterMm)
            + '-' + conservativeReachRatio(reachRatio) + 'd';
    }

    function drillProfileId(diameterMm) {
        return 'analysis-drill-' + ceilingFrom([1, 3, 6, 12], diameterMm);
    }

    var analysisProfileRecords = [];
    analysisDiameters.forEach(function (diameterMm) {
        analysisReachRatios.forEach(function (reachRatio) {
            var underNeckLengthMm = diameterMm * reachRatio;
            analysisProfileRecords.push(tool({
                id: millingProfileId(diameterMm, reachRatio), family: 'flat_end_mill',
                diameterMm: diameterMm, usableCutLengthMm: underNeckLengthMm,
                underNeckLengthMm: underNeckLengthMm, reachMm: underNeckLengthMm + 12,
                shankDiameterMm: diameterMm, holderDiameterMm: Math.max(20, diameterMm * 2),
                operations: ['roughing', 'finishing', 'profiling', 'freeform_finishing', 'slotting', 'tab_removal'],
                materials: allMaterials
            }));
        });
    });
    [1, 3, 6, 12].forEach(function (diameterMm) {
        analysisProfileRecords.push(tool({
            id: drillProfileId(diameterMm), family: 'drill', diameterMm: diameterMm,
            usableCutLengthMm: Math.max(12, diameterMm * 8), reachMm: Math.max(24, diameterMm * 10),
            shankDiameterMm: diameterMm, holderDiameterMm: 25,
            operations: ['drilling', 'tapping', 'reaming'], materials: allMaterials
        }));
    });
    analysisProfileRecords.push(tool({
        id: 'analysis-face-50', family: 'face_mill', diameterMm: 50, usableCutLengthMm: 4,
        reachMm: 35, shankDiameterMm: 20, holderDiameterMm: 50,
        operations: ['facing'], materials: allMaterials
    }));
    analysisProfileRecords.push(tool({
        id: 'flat-6x18', family: 'flat_end_mill', diameterMm: 6, usableCutLengthMm: 18,
        underNeckLengthMm: 18, reachMm: 30, shankDiameterMm: 6, holderDiameterMm: 32,
        operations: ['roughing', 'finishing', 'profiling', 'tab_removal'], materials: allMaterials
    }));
    analysisProfileRecords.push(tool({
        id: 'flat-4x70', family: 'flat_end_mill', diameterMm: 4, usableCutLengthMm: 70,
        underNeckLengthMm: 70, reachMm: 80, shankDiameterMm: 4, holderDiameterMm: 20,
        operations: ['roughing', 'finishing', 'profiling', 'slotting'], materials: allMaterials
    }));
    // Current Gühring catalogue: EDP 9067340100000, RF100A 6734, D10,
    // flute 50 / shank D10 / overall 100 mm, 0.10 x 45 degree corner chamfer.
    // https://guhring.com/ProductsServices/SeriesDetails?Series=6734
    // https://guhring.com/Request/GetAvailableSizes?id=6734
    // Holder D32 at 60 mm projection is an explicit quoting assembly assumption,
    // leaving 40 mm for gripping; do not infer an unverified reduced neck.
    var longAluminiumCutter = tool({
        id: 'guhring-6734-10', analysisProfileId: 'guhring-6734-10',
        manufacturer: 'Gühring', catalogueCode: '9067340100000',
        family: 'flat_end_mill', diameterMm: 10, usableCutLengthMm: 50,
        underNeckLengthMm: 50, shankDiameterMm: 10, overallLengthMm: 100,
        reachMm: 60, holderDiameterMm: 32, cornerChamferMm: 0.1,
        longReach: true, timeMultiplier: 1.24, wearMultiplier: 1.3,
        materials: ['metal'], materialCodes: ['6061', '5083', '6063', '7075'],
        operations: ['roughing', 'finishing', 'profiling', 'slotting'],
        requiresHolderVerification: true,
        sourceUrl: 'https://guhring.com/ProductsServices/SeriesDetails?Series=6734'
    });
    analysisProfileRecords.push(longAluminiumCutter);
    var analysisProfiles = Object.freeze(analysisProfileRecords);
    var analysisProfilesById = Object.create(null);
    analysisProfiles.forEach(function (profile) { analysisProfilesById[profile.id] = profile; });
    Object.freeze(analysisProfilesById);

    var toolRecords = [];
    toolRecords.push(longAluminiumCutter);
    function add(record) {
        if (!record.analysisProfileId) {
            record.analysisProfileId = millingProfileId(record.diameterMm, 2);
        }
        toolRecords.push(tool(record));
    }

    add({ id: 'face-40', family: 'face_mill', diameterMm: 40, usableCutLengthMm: 3,
        reachMm: 35, shankDiameterMm: 20, holderDiameterMm: 50,
        analysisProfileId: 'analysis-face-50', operations: ['facing'], materials: allMaterials });
    add({ id: 'face-50', family: 'face_mill', diameterMm: 50, usableCutLengthMm: 4,
        reachMm: 35, shankDiameterMm: 20, holderDiameterMm: 50,
        analysisProfileId: 'analysis-face-50', operations: ['facing'], materials: allMaterials });

    function addMillingFamily(family, prefix, operations) {
        millingDiameters.forEach(function (diameterMm) {
            var standardId = diameterMm === 6 ? prefix + '-6x18' : prefix + '-' + diameterMm + '-standard';
            add({ id: standardId, family: family, diameterMm: diameterMm,
                usableCutLengthMm: diameterMm * 3, underNeckLengthMm: diameterMm * 3,
                reachMm: (diameterMm * 3) + 12, shankDiameterMm: diameterMm,
                holderDiameterMm: Math.max(32, diameterMm * 2),
                analysisProfileId: family === 'flat_end_mill' && diameterMm === 6
                    ? 'flat-6x18' : millingProfileId(diameterMm, 2),
                lengthClass: 'standard', operations: operations, materials: allMaterials });
            for (var reachRatio = 2; reachRatio <= 10; reachRatio++) {
                var underNeckLengthMm = diameterMm * reachRatio;
                add({ id: prefix + '-' + diameterMm + '-' + reachRatio + 'd', family: family,
                    diameterMm: diameterMm, usableCutLengthMm: underNeckLengthMm,
                    underNeckLengthMm: underNeckLengthMm, reachMm: underNeckLengthMm + 12,
                    shankDiameterMm: diameterMm, holderDiameterMm: Math.max(20, diameterMm * 2),
                    analysisProfileId: millingProfileId(diameterMm, reachRatio),
                    lengthClass: 'long_neck', reachRatio: reachRatio, longReach: reachRatio >= 4,
                    timeMultiplier: 1 + Math.max(0, reachRatio - 2) * 0.08,
                    wearMultiplier: 1 + Math.max(0, reachRatio - 2) * 0.10,
                    operations: operations, materials: allMaterials });
            }
        });
    }

    addMillingFamily('flat_end_mill', 'flat', ['roughing', 'finishing', 'profiling', 'slotting', 'tab_removal']);
    addMillingFamily('ball_end_mill', 'ball', ['freeform_finishing']);

    add({ id: 'flat-4x35', family: 'flat_end_mill', diameterMm: 4, usableCutLengthMm: 35,
        underNeckLengthMm: 35, reachMm: 45, shankDiameterMm: 4, holderDiameterMm: 20,
        analysisProfileId: millingProfileId(4, 5), timeMultiplier: 1.35, wearMultiplier: 1.30,
        longReach: true, operations: ['roughing', 'finishing', 'profiling', 'slotting'], materials: allMaterials });
    add({ id: 'flat-4x70', family: 'flat_end_mill', diameterMm: 4, usableCutLengthMm: 70,
        underNeckLengthMm: 70, reachMm: 80, shankDiameterMm: 4, holderDiameterMm: 20,
        analysisProfileId: 'flat-4x70', timeMultiplier: 1.85, wearMultiplier: 1.75,
        longReach: true, operations: ['roughing', 'finishing', 'profiling', 'slotting'], materials: allMaterials });

    [10, 12, 16, 25].forEach(function (diameterMm) {
        add({ id: 'indexable-' + diameterMm + '-standard', family: 'indexable_end_mill', diameterMm: diameterMm,
            usableCutLengthMm: diameterMm * 2, underNeckLengthMm: diameterMm * 2,
            reachMm: diameterMm * 2 + 15, shankDiameterMm: diameterMm,
            holderDiameterMm: Math.max(25, diameterMm), analysisProfileId: millingProfileId(diameterMm, 2),
            lengthClass: 'standard', operations: ['roughing', 'finishing', 'profiling'], materials: allMaterials });
        for (var reachRatio = 2; reachRatio <= 10; reachRatio++) {
            add({ id: 'indexable-' + diameterMm + '-' + reachRatio + 'd', family: 'indexable_end_mill',
                diameterMm: diameterMm, usableCutLengthMm: diameterMm * reachRatio,
                underNeckLengthMm: diameterMm * reachRatio, reachMm: diameterMm * reachRatio + 15,
                shankDiameterMm: diameterMm, holderDiameterMm: Math.max(25, diameterMm),
                analysisProfileId: millingProfileId(diameterMm, reachRatio), lengthClass: 'long_neck',
                reachRatio: reachRatio, longReach: reachRatio >= 4,
                timeMultiplier: 1 + Math.max(0, reachRatio - 2) * 0.07,
                wearMultiplier: 1 + Math.max(0, reachRatio - 2) * 0.08,
                operations: ['roughing', 'finishing', 'profiling'], materials: allMaterials });
        }
    });

    // OSG List 1200, 90-degree HSS BRIGHT spot drills: EDP / D / FL / OAL /
    // minimum final drill-hole diameter. Manufacturer flute length is not the
    // usable axial spotting cone: its ideal zero-diameter tip gives D/2 depth.
    // The zero point diameter models spotting, not a manufacturer's chisel-width
    // claim; the listed minimum hole diameter applies to the subsequent drill.
    // Reach assumes 30 mm holder engagement and a D25 holder. These are advisory
    // quoting assembly allowances, not manufacturer reach/holder specifications.
    [
        ['62910', 10, 30, 93, 2.1],
        ['62912', 12, 36, 108, 2.1],
        ['62916', 16, 41, 118, 3]
    ].forEach(function (specification) {
        var diameterMm = specification[1];
        add({ id: 'spot-drill-' + diameterMm, family: 'spot_drill', diameterMm: diameterMm,
            manufacturer: 'OSG', series: 'List 1200', catalogueCode: specification[0],
            toolMaterial: 'HSS', surfaceTreatment: 'BRIGHT',
            includedAngleDegrees: 90, pointDiameterMm: 0, directSpotting: true,
            minimumHoleDiameterMm: specification[4],
            usableCutLengthMm: diameterMm * 0.5, fluteLengthMm: specification[2],
            overallLengthMm: specification[3], shankDiameterMm: diameterMm,
            holderEngagementMm: 30, reachMm: specification[3] - 30, holderDiameterMm: 25,
            requiresHolderVerification: true,
            // This shared diameter proxy is not a physical spot-tool certificate;
            // direct spotting uses the complete catalogued tool/holder envelope.
            analysisProfileId: millingProfileId(diameterMm, 2),
            sourceUrl: 'https://osgtool.com/content/literature/8002024CA/List%201200%20-%20EX-GOLD%20TIN-NC-LDS.pdf',
            operations: ['spot_drilling', 'chamfering'], materials: allMaterials });
    });

    for (var drillTenth = 10; drillTenth <= 120; drillTenth++) {
        var drillDiameterMm = drillTenth / 10;
        add({ id: 'hss-drill-' + decimalId(drillDiameterMm), family: 'hss_drill', diameterMm: drillDiameterMm,
            usableCutLengthMm: Math.max(12, drillDiameterMm * 8), reachMm: Math.max(24, drillDiameterMm * 10),
            shankDiameterMm: drillDiameterMm, holderDiameterMm: 25,
            analysisProfileId: drillProfileId(drillDiameterMm), operations: ['drilling'], materials: allMaterials });
    }
    add({ id: 'drill-3', family: 'drill', diameterMm: 3, usableCutLengthMm: 20,
        reachMm: 35, shankDiameterMm: 3, holderDiameterMm: 25,
        analysisProfileId: drillProfileId(3), operations: ['drilling'], materials: allMaterials });

    var metricTaps = [
        [2, 0.4], [2.5, 0.45], [3, 0.5], [3, 0.35], [3.5, 0.6], [4, 0.7], [4, 0.5],
        [5, 0.8], [5, 0.5], [6, 1], [6, 0.75], [7, 1], [8, 1.25], [8, 1], [8, 0.75],
        [9, 1.25], [10, 1.5], [10, 1.25], [10, 1], [11, 1.5], [12, 1.75], [12, 1.5], [12, 1.25]
    ];
    metricTaps.forEach(function (specification) {
        var diameterMm = specification[0];
        var pitchMm = specification[1];
        var id = diameterMm === 3 && pitchMm === 0.5 ? 'tap-m3'
            : 'tap-m' + decimalId(diameterMm) + 'x' + decimalId(pitchMm);
        add({ id: id, family: 'tap', threadSystem: 'metric', designation: 'M' + diameterMm + ' x ' + pitchMm,
            diameterMm: diameterMm, pitchMm: pitchMm, usableCutLengthMm: Math.max(12, diameterMm * 3),
            reachMm: Math.max(28, diameterMm * 5), shankDiameterMm: Math.max(3.5, diameterMm),
            holderDiameterMm: 25, analysisProfileId: drillProfileId(diameterMm),
            operations: ['tapping'], materials: allMaterials });
    });

    var imperialTapDiameters = [
        ['2', 2.184, 56, 64], ['4', 2.845, 40, 48], ['6', 3.505, 32, 40], ['8', 4.166, 32, 36],
        ['10', 4.826, 24, 32], ['1-4', 6.35, 20, 28], ['5-16', 7.938, 18, 24],
        ['3-8', 9.525, 16, 24], ['7-16', 11.113, 14, 20], ['1-2', 12.7, 13, 20]
    ];
    imperialTapDiameters.forEach(function (specification) {
        ['unc', 'unf'].forEach(function (series, seriesIndex) {
            var diameterMm = specification[1];
            var threadsPerInch = specification[2 + seriesIndex];
            add({ id: 'tap-' + series + '-' + specification[0], family: 'tap', threadSystem: 'imperial',
                designation: specification[0].replace('-', '/') + '-' + threadsPerInch + ' ' + series.toUpperCase(),
                diameterMm: diameterMm, threadsPerInch: threadsPerInch,
                usableCutLengthMm: Math.max(12, diameterMm * 3), reachMm: Math.max(28, diameterMm * 5),
                shankDiameterMm: Math.max(3.5, diameterMm), holderDiameterMm: 25,
                analysisProfileId: drillProfileId(diameterMm), operations: ['tapping'], materials: allMaterials });
        });
    });

    for (var reamerHalf = 2; reamerHalf <= 24; reamerHalf++) {
        var reamerDiameterMm = reamerHalf / 2;
        add({ id: 'reamer-' + decimalId(reamerDiameterMm), family: 'reamer', diameterMm: reamerDiameterMm,
            toleranceClass: 'H7', usableCutLengthMm: Math.max(12, reamerDiameterMm * 5),
            reachMm: Math.max(30, reamerDiameterMm * 7), shankDiameterMm: reamerDiameterMm,
            holderDiameterMm: 25, analysisProfileId: drillProfileId(reamerDiameterMm),
            operations: ['reaming'], materials: allMaterials });
    }

    [1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10, 12].forEach(function (widthMm) {
        add({ id: 'slot-cutter-50x' + decimalId(widthMm), family: 'slot_cutter', diameterMm: 50,
            cutterWidthMm: widthMm, usableCutLengthMm: widthMm, reachMm: 30,
            shankDiameterMm: 16, holderDiameterMm: 50,
            analysisProfileId: millingProfileId(widthMm, 2), operations: ['slotting'], materials: allMaterials });
    });

    add({ id: 'chamfer-6', family: 'chamfer_mill', diameterMm: 6, usableCutLengthMm: 6,
        reachMm: 24, shankDiameterMm: 6, holderDiameterMm: 32,
        analysisProfileId: millingProfileId(6, 2), operations: ['chamfering'], materials: allMaterials });
    add({ id: 'thread-mill-4', family: 'thread_mill', diameterMm: 4, usableCutLengthMm: 12,
        reachMm: 28, shankDiameterMm: 4, holderDiameterMm: 25,
        analysisProfileId: millingProfileId(4, 2), operations: ['thread_milling'], materials: allMaterials });

    var tools = Object.freeze(toolRecords);

    function hasSupportedAnalysisDiameter(entry) {
        var profile = analysisProfilesById[entry.analysisProfileId];
        // A shared flat profile proves clearance only up to its analysed diameter.
        // Keep unsupported SKUs in the catalogue, but never use their smaller proxy
        // as collision evidence (including operation-selection fallback candidates).
        return !!profile && (profile.family !== 'flat_end_mill'
            || entry.diameterMm <= profile.diameterMm);
    }

    // Commercial SKUs may share the same cutter envelope. Planning only needs one real tool
    // per envelope/family/operation set; retaining every diameter-length SKU here creates a
    // cartesian product across tools, surfaces, and setups without adding reach information.
    var planningToolsByKey = Object.create(null);
    tools.forEach(function (entry) {
        if (!hasSupportedAnalysisDiameter(entry)) {
            return;
        }
        var key = entry.analysisProfileId + '\u0000' + entry.family + '\u0000'
            + entry.operations.slice().sort().join(',');
        var current = planningToolsByKey[key];
        if (!current || entry.timeMultiplier < current.timeMultiplier
            || (entry.timeMultiplier === current.timeMultiplier && entry.wearMultiplier < current.wearMultiplier)
            || (entry.timeMultiplier === current.timeMultiplier && entry.wearMultiplier === current.wearMultiplier
                && entry.id.localeCompare(current.id) < 0)) {
            planningToolsByKey[key] = entry;
        }
    });
    var planningToolRecords = Object.freeze(Object.keys(planningToolsByKey).sort().map(function (key) {
        return planningToolsByKey[key];
    }));

    var toolsById = Object.create(null);
    tools.forEach(function (entry) {
        toolsById[entry.id] = entry;
    });
    Object.freeze(toolsById);

    function list() {
        return tools;
    }

    function get(id) {
        if (id === 'ns-alb225-1-lu5') { return ballRestTool(1); }
        if (id === 'ns-alb225-4') { return ballRestTool(4); }
        if (id === 'ns-alb225-6') { return ballRestTool(6); }
        return typeof id === 'string' && toolsById[id] ? toolsById[id] : null;
    }

    function listAnalysisProfiles() {
        return analysisProfiles;
    }

    function listPlanningTools() {
        return planningToolRecords;
    }

    function analysisProfile(id) {
        return typeof id === 'string' && analysisProfilesById[id] ? analysisProfilesById[id] : null;
    }

    function materialClass(materialCode) {
        var entry = materialCatalog.get(materialCode);
        if (!entry) {
            return null;
        }
        return entry.family === 'engineering_plastic' ? 'plastic' : 'metal';
    }

    function compatible(operationCode, materialCode) {
        var category = materialClass(materialCode);
        if (!category || typeof operationCode !== 'string') {
            return Object.freeze([]);
        }

        return Object.freeze(tools.filter(function (entry) {
            return entry.enabled
                && hasSupportedAnalysisDiameter(entry)
                && entry.operations.indexOf(operationCode) >= 0
                && (!entry.materialCodes || entry.materialCodes.indexOf(materialCode) >= 0)
                && entry.materials.indexOf(category) >= 0;
        }));
    }

    function selectLargestFeasible(constraints) {
        constraints = constraints || {};
        var families = Array.isArray(constraints.families) ? constraints.families
            : constraints.family ? [constraints.family] : [];
        var maximumDiameterMm = Number.isFinite(constraints.maximumDiameterMm)
            ? constraints.maximumDiameterMm : Infinity;
        var minimumDiameterMm = Number.isFinite(constraints.minimumDiameterMm)
            ? constraints.minimumDiameterMm : 0;
        var minimumCutLengthMm = Number.isFinite(constraints.minimumCutLengthMm)
            ? constraints.minimumCutLengthMm : 0;
        var minimumReachMm = Number.isFinite(constraints.minimumReachMm)
            ? constraints.minimumReachMm : minimumCutLengthMm;
        var candidates = tools.filter(function (entry) {
            return entry.enabled && hasSupportedAnalysisDiameter(entry)
                && (!families.length || families.indexOf(entry.family) >= 0)
                && (!constraints.operation || entry.operations.indexOf(constraints.operation) >= 0)
                && entry.diameterMm >= minimumDiameterMm && entry.diameterMm <= maximumDiameterMm
                && entry.usableCutLengthMm >= minimumCutLengthMm && entry.reachMm >= minimumReachMm
                && (!Number.isFinite(constraints.pitchMm) || entry.pitchMm === constraints.pitchMm)
                && (!constraints.designation || entry.designation === constraints.designation)
                && (!Number.isFinite(constraints.includedAngleDegrees)
                    || entry.includedAngleDegrees === constraints.includedAngleDegrees);
        }).sort(function (left, right) {
            return right.diameterMm - left.diameterMm
                || left.usableCutLengthMm - right.usableCutLengthMm
                || left.reachMm - right.reachMm
                || left.id.localeCompare(right.id);
        });
        return candidates.length ? candidates[0] : null;
    }

    // Market references for the stock-handoff calculation, not substitutes for
    // the clearance envelopes above. Catalogue overall length is deliberately
    // not usable reach: actual stickout/holder and remaining-stock checks are
    // required before these references can replace any planned flat-tool work.
    // Geometry: https://www.ns-tool.com/en/download/ebook/CATALOG_Vol21_2411_cn/434-435/
    // Conditions: https://www.ns-tool.com/ja/download/ebook/aluminium_vol2/35/
    var ballRestReferences = freezeRecord({
        1: {
            manufacturer: 'NS TOOL', series: 'ALB225', catalogueCode: '01-00638-05011',
            family: 'ball_end_mill', diameterMm: 1, radiusMm: 0.5,
            usableCutLengthMm: 0.75, underNeckLengthMm: 5, neckDiameterMm: 0.95,
            shankDiameterMm: 4, overallLengthMm: 60,
            referenceSpindleRpm: 20000, referenceFeedMmPerMinute: 1500,
            referenceAxialStepMm: 0.3, referenceStepoverMm: 0.3,
            finishingStepoverMm: 0.05
        },
        6: {
            manufacturer: 'NS TOOL', series: 'ALB225', catalogueCode: '01-00638-30001',
            family: 'ball_end_mill', diameterMm: 6, radiusMm: 3,
            usableCutLengthMm: 12, underNeckLengthMm: 12, neckDiameterMm: 6,
            shankDiameterMm: 6, overallLengthMm: 90,
            referenceSpindleRpm: 12000, referenceFeedMmPerMinute: 3000,
            referenceAxialStepMm: 1, referenceStepoverMm: 2,
            finishingStepoverMm: 0.2
        },
        4: {
            manufacturer: 'NS TOOL', series: 'ALB225', catalogueCode: '01-00638-20001',
            family: 'ball_end_mill', diameterMm: 4, radiusMm: 2,
            usableCutLengthMm: 8, underNeckLengthMm: 8, neckDiameterMm: 4,
            shankDiameterMm: 6, overallLengthMm: 70,
            referenceSpindleRpm: 14000, referenceFeedMmPerMinute: 2000,
            referenceAxialStepMm: 0.5, referenceStepoverMm: 1.5,
            finishingStepoverMm: 0.15
        }
    });

    function ballRestTool(diameterMm) {
        var reference = ballRestReferences[diameterMm];
        if (!reference) { return null; }
        return freezeRecord(Object.assign({}, reference, {
            id: diameterMm === 1 ? 'ns-alb225-1-lu5' : 'ns-alb225-' + diameterMm,
            // Quotation holder/stickout assumptions, independently checked by
            // the worker; these are not catalogue overall-length dimensions.
            reachMm: diameterMm === 1 ? 14 : diameterMm === 6 ? 24 : 20, holderDiameterMm: 20,
            timeMultiplier: 1, wearMultiplier: 1, enabled: true,
            operations: ['freeform_finishing'], materials: ['metal'], confidence: 'Medium'
        }));
    }

    function ballRestPreparationTool() {
        // NS TOOL AL2D-2 D2, code01-00631-00200. Reach/holder are setup
        // assumptions, not inferred from the catalogue 45mm overall length.
        // https://www.ns-tool.com/ja/download/ebook/aluminium_vol2/6/
        return freezeRecord({ diameterMm: 2, family: 'flat_end_mill', usableCutLengthMm: 4,
            shankDiameterMm: 4, reachMm: 16, holderDiameterMm: 20,
            manufacturer: 'NS TOOL', series: 'AL2D-2', catalogueCode: '01-00631-00200' });
    }

    function estimateBallRestPasses(input) {
        if (!input || ['6061', '5083', '6063', '7075'].indexOf(input.material) < 0) { return null; }
        var reference = ballRestReferences[input.diameterMm];
        if (!reference || !Number.isFinite(input.diameterMm)
            || !Number.isFinite(input.residualAxialCapMm) || input.residualAxialCapMm < 0
            || !Number.isFinite(input.areaMm2) || input.areaMm2 <= 0
            || !Number.isFinite(input.spindleLimitRpm) || input.spindleLimitRpm <= 0) { return null; }
        // A neck no wider than the ball can enter the previous layer's cleared
        // withdrawal cylinder only when its start remains above that layer's
        // sphere centre. This is stricter than the catalogue ap for R0.5 LU5.
        var axialStep = Math.min(reference.referenceAxialStepMm,
            reference.usableCutLengthMm - reference.radiusMm);
        if (!(axialStep > 0) || reference.neckDiameterMm > reference.diameterMm) { return null; }
        var layerCount = Math.max(1, Math.ceil(input.residualAxialCapMm / axialStep));
        // Malformed or unbounded stock evidence must not allocate huge arrays.
        if (!Number.isSafeInteger(layerCount) || layerCount > 256) { return null; }
        var spindleRpm = Math.min(input.spindleLimitRpm, reference.referenceSpindleRpm);
        var feed = reference.referenceFeedMmPerMinute * spindleRpm / reference.referenceSpindleRpm;
        if (!(feed > 0) || !Number.isFinite(feed)) { return null; }
        var passes = [];
        var cuttingMinutes = 0;
        for (var index = 0; index < layerCount; index++) {
            var last = index === layerCount - 1;
            // ap is an axial increment; ae is path spacing, never a residual-
            // stock thickness limit. The finer last-pass spacing is our quoting
            // finish policy, not a manufacturer-specified surface guarantee.
            var stepover = last ? reference.finishingStepoverMm : reference.referenceStepoverMm;
            var minutes = input.areaMm2 / (stepover * feed);
            if (!Number.isFinite(minutes)) { return null; }
            passes.push({ axialOffsetMm: input.residualAxialCapMm * (layerCount - index - 1) / layerCount,
                stepoverMm: stepover, cuttingMinutes: minutes, phase: last ? 'finishing' : 'rest_machining' });
            cuttingMinutes += minutes;
        }
        if (!Number.isFinite(cuttingMinutes)) { return null; }
        return freezeRecord({
            tool: reference, material: input.material,
            materialBasis: input.material === '7075' ? 'manufacturer-listed-alloy' : 'aluminium-alloy-estimate',
            sourceUrl: 'https://www.ns-tool.com/ja/download/ebook/aluminium_vol2/35/',
            residualAxialCapMm: input.residualAxialCapMm, axialStepMm: axialStep,
            spindleRpm: spindleRpm, feedMmPerMinute: feed,
            passes: passes, cuttingMinutes: cuttingMinutes,
            requiresStockAndClearanceCertificate: true
        });
    }

    root.CncToolLibrary = Object.freeze({
        version: config.toolLibraryVersion,
        list: list,
        planningTools: listPlanningTools,
        analysisProfiles: listAnalysisProfiles,
        analysisProfile: analysisProfile,
        compatible: compatible,
        selectLargestFeasible: selectLargestFeasible,
        ballRestTool: ballRestTool,
        ballRestPreparationTool: ballRestPreparationTool,
        estimateBallRestPasses: estimateBallRestPasses,
        get: get
    });
}(typeof window !== 'undefined' ? window : self));
