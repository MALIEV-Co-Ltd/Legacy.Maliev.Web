(function (root) {
    'use strict';

    var config = root.CncQuotationConfig;
    if (!config) {
        throw new Error('CNC quotation configuration must load before the material catalog.');
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

    var presentationByCode = Object.freeze({
        '6061': { catalogGroup: 'aluminum', commonRank: 1, aliases: ['aluminium', 'aluminum'] },
        '5083': { catalogGroup: 'aluminum', aliases: ['aluminium', 'aluminum', 'marine plate'] },
        '6063': { catalogGroup: 'aluminum', aliases: ['aluminium', 'aluminum', 'extrusion'] },
        '7075': { catalogGroup: 'aluminum', commonRank: 2, aliases: ['aluminium', 'aluminum'] },
        'C3604': { catalogGroup: 'copper_alloy', commonRank: 3, aliases: ['brass', 'free cutting brass', 'ทองเหลือง'] },
        'BC6': { catalogGroup: 'copper_alloy', aliases: ['LG2', 'bronze', 'bronze bushing', 'บรอนซ์', 'ทองเหลืองลายเสือ'] },
        'ALBC': { catalogGroup: 'copper_alloy', aliases: ['aluminium bronze', 'aluminum bronze', 'อลูมิเนียมบรอนซ์'] },
        'SS400': { catalogGroup: 'carbon_steel', aliases: ['mild steel'] },
        'S45C': { catalogGroup: 'carbon_steel', aliases: ['JIS S45C'] },
        'S50C': { catalogGroup: 'carbon_steel', aliases: ['JIS S50C'] },
        'SCM440': { catalogGroup: 'alloy_steel', aliases: ['chromoly', 'JIS SCM440'] },
        'SUS303': { catalogGroup: 'stainless_steel', aliases: ['stainless 303'] },
        'SUS304': { catalogGroup: 'stainless_steel', commonRank: 4, aliases: ['stainless 304'] },
        'SUS316L': { catalogGroup: 'stainless_steel', aliases: ['stainless 316L'] },
        'PA6': { catalogGroup: 'nylon', commonRank: 6, aliases: ['nylon', 'superlene', 'superlene nylon 6', 'ไนลอน', 'ซุปเปอร์ลีน'] },
        'MC901': { catalogGroup: 'nylon', commonRank: 7, aliases: ['nylon', 'MC nylon', 'cast nylon', 'PA6C', 'ไนลอน'] },
        'POM': { catalogGroup: 'acetal_fluoropolymer', commonRank: 5, aliases: ['acetal', 'delrin', 'ปอม'] },
        'PTFE': { catalogGroup: 'acetal_fluoropolymer', aliases: ['teflon', 'เทปล่อน'] },
        'PVDF': { catalogGroup: 'acetal_fluoropolymer', aliases: ['polyvinylidene fluoride'] },
        'PE1000': { catalogGroup: 'polyolefin', commonRank: 8, aliases: ['UHMW-PE', 'UHMWPE', 'polyethylene', 'พีอี 1000'] },
        'PE500': { catalogGroup: 'polyolefin', aliases: ['HMW-PE', 'HDPE', 'polyethylene', 'พีอี 500'] },
        'PP': { catalogGroup: 'polyolefin', aliases: ['polypropylene', 'โพลีโพรพิลีน'] },
        'Acrylic': { catalogGroup: 'general_plastic', aliases: ['PMMA', 'อะคริลิก'] },
        'PVC': { catalogGroup: 'general_plastic', aliases: ['rigid PVC', 'พีวีซี'] },
        'ABS': { catalogGroup: 'general_plastic', aliases: ['acrylonitrile butadiene styrene'] },
        'PC': { catalogGroup: 'general_plastic', aliases: ['polycarbonate', 'โพลีคาร์บอเนต'] },
        'PEEK': { catalogGroup: 'high_performance_plastic', aliases: ['polyether ether ketone'] },
        'PEI': { catalogGroup: 'high_performance_plastic', aliases: ['Ultem', 'polyetherimide'] }
    });

    function material(record) {
        var presentation = presentationByCode[record.code];
        if (!presentation) {
            throw new Error('CNC material presentation metadata is required for ' + record.code + '.');
        }
        record.catalogGroup = presentation.catalogGroup;
        record.commonRank = presentation.commonRank || 0;
        record.searchAliases = presentation.aliases || [];
        return freezeRecord(record);
    }

    var materials = Object.freeze([
        material({
            code: '6061', label: '6061-T6', family: 'aluminum', densityKgPerMm3: 2.70 / 1000000,
            stockForms: ['rectangular', 'round', 'plate'],
            procurement: { mode: 'calibrated_curve', confidence: 'High', supplierReviewRequired: false, supplier: 'metha_metal', evidence: 'metha_metal_aluminum_catalog_2026-08-29' },
            machining: { mrrMultiplier: 1, finishMultiplier: 1, toolingWearMultiplier: 1 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'tap', 'thread_mill', 'chamfer_mill'],
            risks: [], enabled: true
        }),
        material({
            code: '5083', label: '5083-H112', family: 'aluminum', densityKgPerMm3: 2.66 / 1000000,
            stockForms: ['rectangular', 'plate'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 230, minimumCutThb: 300, confidence: 'Low', supplierReviewRequired: true, supplier: 'metha_metal', evidence: 'metha_metal_5083_plate_catalog_2026-08-29' },
            machining: { mrrMultiplier: 0.82, finishMultiplier: 0.85, toolingWearMultiplier: 1.15 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'tap', 'thread_mill', 'chamfer_mill'],
            risks: ['temper_and_plate_thickness_variation'], enabled: true
        }),
        material({
            code: '6063', label: '6063-T5/T6', family: 'aluminum', densityKgPerMm3: 2.70 / 1000000,
            stockForms: ['rectangular', 'round'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 180, minimumCutThb: 200, confidence: 'Low', supplierReviewRequired: true, supplier: 'metha_metal', evidence: 'metha_metal_6063_extrusion_catalog_2026-08-29' },
            machining: { mrrMultiplier: 1.05, finishMultiplier: 0.95, toolingWearMultiplier: 0.95 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'tap', 'thread_mill', 'chamfer_mill'],
            risks: ['extrusion_form_availability'], enabled: true
        }),
        material({
            code: '7075', label: '7075-T6', family: 'aluminum', densityKgPerMm3: 2.81 / 1000000,
            stockForms: ['rectangular', 'round', 'plate'],
            procurement: { mode: 'calibrated_curve', confidence: 'Medium', supplierReviewRequired: false, supplier: 'metha_metal', evidence: 'metha_metal_aluminum_catalog_2026-08-29' },
            machining: { mrrMultiplier: 0.72, finishMultiplier: 0.75, toolingWearMultiplier: 1.35 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'tap', 'thread_mill', 'chamfer_mill'],
            risks: ['tool_wear'], enabled: true
        }),
        material({
            code: 'C3604', label: 'C3604 Free-cutting Brass', family: 'copper_alloy', densityKgPerMm3: 8.50 / 1000000,
            stockForms: ['rectangular', 'round'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 380, minimumCutThb: 350, confidence: 'Low', supplierReviewRequired: true, supplier: 'metha_metal', evidence: 'metha_metal_c3604_brass_bar_catalog_2026-08-29' },
            machining: { mrrMultiplier: 0.9, finishMultiplier: 0.95, toolingWearMultiplier: 0.8 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'tap', 'thread_mill', 'chamfer_mill'],
            risks: ['lead_content_requires_chip_control'], enabled: true
        }),
        material({
            code: 'BC6', label: 'BC6 / LG2 Bronze', family: 'copper_alloy', densityKgPerMm3: 8.80 / 1000000,
            stockForms: ['rectangular', 'round'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 450, minimumCutThb: 450, confidence: 'Low', supplierReviewRequired: true, supplier: 'metha_metal', evidence: 'metha_metal_bc6_lg2_bronze_catalog_2026-08-29' },
            machining: { mrrMultiplier: 0.58, finishMultiplier: 0.7, toolingWearMultiplier: 1.3 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'thread_mill', 'chamfer_mill'],
            risks: ['continuous_cast_stock_variation'], enabled: true
        }),
        material({
            code: 'ALBC', label: 'Aluminium Bronze', family: 'copper_alloy', densityKgPerMm3: 7.60 / 1000000,
            stockForms: ['rectangular', 'round'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 520, minimumCutThb: 500, confidence: 'Low', supplierReviewRequired: true, supplier: 'metha_metal', evidence: 'metha_metal_aluminum_bronze_catalog_2026-08-29' },
            machining: { mrrMultiplier: 0.34, finishMultiplier: 0.5, toolingWearMultiplier: 1.9 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'thread_mill', 'chamfer_mill'],
            risks: ['high_tool_load'], enabled: true
        }),
        material({
            code: 'SS400', label: 'SS400', family: 'carbon_steel', densityKgPerMm3: 7.85 / 1000000,
            stockForms: ['rectangular', 'round', 'plate'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 45, minimumCutThb: 180, confidence: 'Low', supplierReviewRequired: true },
            machining: { mrrMultiplier: 0.55, finishMultiplier: 0.65, toolingWearMultiplier: 1.4 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'tap', 'thread_mill', 'chamfer_mill'],
            risks: ['scale_variation'], enabled: true
        }),
        material({
            code: 'S45C', label: 'S45C', family: 'carbon_steel', densityKgPerMm3: 7.85 / 1000000,
            stockForms: ['rectangular', 'round', 'plate'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 60, minimumCutThb: 200, confidence: 'Low', supplierReviewRequired: true },
            machining: { mrrMultiplier: 0.45, finishMultiplier: 0.58, toolingWearMultiplier: 1.6 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'tap', 'thread_mill', 'chamfer_mill'],
            risks: ['hardness_variation'], enabled: true
        }),
        material({
            code: 'S50C', label: 'S50C', family: 'carbon_steel', densityKgPerMm3: 7.85 / 1000000,
            stockForms: ['rectangular', 'round', 'plate'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 65, minimumCutThb: 200, confidence: 'Low', supplierReviewRequired: true },
            machining: { mrrMultiplier: 0.42, finishMultiplier: 0.55, toolingWearMultiplier: 1.7 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'tap', 'thread_mill', 'chamfer_mill'],
            risks: ['hardness_variation'], enabled: true
        }),
        material({
            code: 'SCM440', label: 'SCM440', family: 'alloy_steel', densityKgPerMm3: 7.85 / 1000000,
            stockForms: ['rectangular', 'round', 'plate'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 95, minimumCutThb: 220, confidence: 'Low', supplierReviewRequired: true },
            machining: { mrrMultiplier: 0.35, finishMultiplier: 0.48, toolingWearMultiplier: 1.9 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'tap', 'thread_mill', 'chamfer_mill'],
            risks: ['heat_treatment_variation'], enabled: true
        }),
        material({
            code: 'SUS303', label: 'SUS303', family: 'stainless_steel', densityKgPerMm3: 7.93 / 1000000,
            stockForms: ['rectangular', 'round', 'plate'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 175, minimumCutThb: 250, confidence: 'Low', supplierReviewRequired: true },
            machining: { mrrMultiplier: 0.36, finishMultiplier: 0.5, toolingWearMultiplier: 2 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'tap', 'thread_mill', 'chamfer_mill'],
            risks: ['work_hardening'], enabled: true
        }),
        material({
            code: 'SUS304', label: 'SUS304', family: 'stainless_steel', densityKgPerMm3: 7.93 / 1000000,
            stockForms: ['rectangular', 'round', 'plate'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 155, minimumCutThb: 250, confidence: 'Low', supplierReviewRequired: true },
            machining: { mrrMultiplier: 0.28, finishMultiplier: 0.42, toolingWearMultiplier: 2.2 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'tap', 'thread_mill', 'chamfer_mill'],
            risks: ['work_hardening'], enabled: true
        }),
        material({
            code: 'SUS316L', label: 'SUS316L', family: 'stainless_steel', densityKgPerMm3: 7.99 / 1000000,
            stockForms: ['rectangular', 'round', 'plate'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 220, minimumCutThb: 300, confidence: 'Low', supplierReviewRequired: true, supplier: 'metha_metal', evidence: 'metha_metal_stainless_316l_catalog_2026-08-29' },
            machining: { mrrMultiplier: 0.24, finishMultiplier: 0.38, toolingWearMultiplier: 2.4 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'tap', 'thread_mill', 'chamfer_mill'],
            risks: ['work_hardening', 'heat_build_up'], enabled: true
        }),
        material({
            code: 'PA6', label: 'PA6 Superlene Nylon', family: 'engineering_plastic', densityKgPerMm3: 1.14 / 1000000,
            stockForms: ['rectangular', 'round', 'plate'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 260, minimumCutThb: 180, confidence: 'Low', supplierReviewRequired: true, supplier: 'myps', evidence: 'myps_superlene_pa6_catalog_2026-08-29' },
            machining: { mrrMultiplier: 1.65, finishMultiplier: 1.1, toolingWearMultiplier: 0.5 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'tap', 'thread_mill', 'chamfer_mill'],
            risks: ['moisture_absorption', 'thermal_movement'], enabled: true
        }),
        material({
            code: 'MC901', label: 'MC Nylon MC901 / PA6C', family: 'engineering_plastic', densityKgPerMm3: 1.16 / 1000000,
            stockForms: ['rectangular', 'round', 'plate'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 520, minimumCutThb: 250, confidence: 'Low', supplierReviewRequired: true, supplier: 'myps', evidence: 'myps_mc901_pa6c_catalog_2026-08-29' },
            machining: { mrrMultiplier: 1.55, finishMultiplier: 1.15, toolingWearMultiplier: 0.48 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'tap', 'thread_mill', 'chamfer_mill'],
            risks: ['moisture_absorption', 'workholding_deformation'], enabled: true
        }),
        material({
            code: 'POM', label: 'POM', family: 'engineering_plastic', densityKgPerMm3: 1.41 / 1000000,
            stockForms: ['rectangular', 'round', 'plate'],
            procurement: {
                mode: 'benchmark_mass', thbPerKg: 400, minimumCutThb: 180,
                confidence: 'Low', supplierReviewRequired: true, supplier: 'myps',
                evidence: 'myps_pom_sheet_rod_catalog_2026-08-29'
            },
            machining: { mrrMultiplier: 1.8, finishMultiplier: 1.35, toolingWearMultiplier: 0.55 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'tap', 'thread_mill', 'chamfer_mill'],
            risks: ['thermal_movement'], enabled: true
        }),
        material({
            code: 'PTFE', label: 'PTFE (Teflon)', family: 'engineering_plastic', densityKgPerMm3: 2.20 / 1000000,
            stockForms: ['rectangular', 'round', 'plate'],
            procurement: {
                mode: 'benchmark_mass', thbPerKg: 830, minimumCutThb: 250,
                confidence: 'Low', supplierReviewRequired: true, supplier: 'myps',
                evidence: 'myps_ptfe_sheet_rod_catalog_2026-08-29'
            },
            machining: { mrrMultiplier: 1.3, finishMultiplier: 0.9, toolingWearMultiplier: 0.5 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'thread_mill', 'chamfer_mill'],
            risks: ['creep', 'workholding_deformation'], enabled: true
        }),
        material({
            code: 'PVDF', label: 'PVDF', family: 'engineering_plastic', densityKgPerMm3: 1.78 / 1000000,
            stockForms: ['rectangular', 'round', 'plate'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 900, minimumCutThb: 500, confidence: 'Low', supplierReviewRequired: true, supplier: 'myps', evidence: 'myps_pvdf_catalog_2026-08-29' },
            machining: { mrrMultiplier: 1.05, finishMultiplier: 0.85, toolingWearMultiplier: 0.55 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'thread_mill', 'chamfer_mill'],
            risks: ['thermal_expansion', 'workholding_deformation'], enabled: true
        }),
        material({
            code: 'PE1000', label: 'PE1000 / UHMW-PE', family: 'engineering_plastic', densityKgPerMm3: 0.94 / 1000000,
            stockForms: ['rectangular', 'round', 'plate'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 360, minimumCutThb: 250, confidence: 'Low', supplierReviewRequired: true, supplier: 'myps', evidence: 'myps_pe1000_uhmwpe_catalog_2026-08-29' },
            machining: { mrrMultiplier: 1.5, finishMultiplier: 0.85, toolingWearMultiplier: 0.45 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'thread_mill', 'chamfer_mill'],
            risks: ['thermal_expansion', 'chip_control', 'workholding_deformation'], enabled: true
        }),
        material({
            code: 'PE500', label: 'PE500 / HMW-PE', family: 'engineering_plastic', densityKgPerMm3: 0.95 / 1000000,
            stockForms: ['rectangular', 'round', 'plate'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 180, minimumCutThb: 180, confidence: 'Low', supplierReviewRequired: true, supplier: 'myps', evidence: 'myps_pe500_catalog_2026-08-29' },
            machining: { mrrMultiplier: 1.55, finishMultiplier: 0.88, toolingWearMultiplier: 0.45 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'thread_mill', 'chamfer_mill'],
            risks: ['thermal_expansion', 'chip_control'], enabled: true
        }),
        material({
            code: 'PP', label: 'PP Polypropylene', family: 'engineering_plastic', densityKgPerMm3: 0.91 / 1000000,
            stockForms: ['rectangular', 'round', 'plate'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 120, minimumCutThb: 150, confidence: 'Low', supplierReviewRequired: true, supplier: 'myps', evidence: 'myps_pp_catalog_2026-08-29' },
            machining: { mrrMultiplier: 1.45, finishMultiplier: 0.8, toolingWearMultiplier: 0.45 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'thread_mill', 'chamfer_mill'],
            risks: ['thermal_expansion', 'workholding_deformation'], enabled: true
        }),
        material({
            code: 'Acrylic', label: 'Acrylic', family: 'engineering_plastic', densityKgPerMm3: 1.19 / 1000000,
            stockForms: ['rectangular', 'plate'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 180, minimumCutThb: 150, confidence: 'Low', supplierReviewRequired: true, supplier: 'myps', evidence: 'myps_acrylic_catalog_2026-08-29' },
            machining: { mrrMultiplier: 1.4, finishMultiplier: 0.75, toolingWearMultiplier: 0.65 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'chamfer_mill'],
            risks: ['brittleness', 'thermal_softening'], enabled: true
        }),
        material({
            code: 'PVC', label: 'Rigid PVC', family: 'engineering_plastic', densityKgPerMm3: 1.40 / 1000000,
            stockForms: ['rectangular', 'round', 'plate'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 120, minimumCutThb: 150, confidence: 'Low', supplierReviewRequired: true, supplier: 'myps', evidence: 'myps_rigid_pvc_catalog_2026-08-29' },
            machining: { mrrMultiplier: 1.2, finishMultiplier: 0.8, toolingWearMultiplier: 0.65 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'thread_mill', 'chamfer_mill'],
            risks: ['thermal_softening', 'chip_control'], enabled: true
        }),
        material({
            code: 'ABS', label: 'ABS', family: 'engineering_plastic', densityKgPerMm3: 1.05 / 1000000,
            stockForms: ['rectangular', 'round', 'plate'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 180, minimumCutThb: 150, confidence: 'Low', supplierReviewRequired: true, supplier: 'myps', evidence: 'myps_abs_catalog_2026-08-29' },
            machining: { mrrMultiplier: 1.45, finishMultiplier: 0.9, toolingWearMultiplier: 0.55 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'tap', 'thread_mill', 'chamfer_mill'],
            risks: ['thermal_softening'], enabled: true
        }),
        material({
            code: 'PC', label: 'PC Polycarbonate', family: 'engineering_plastic', densityKgPerMm3: 1.20 / 1000000,
            stockForms: ['rectangular', 'round', 'plate'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 300, minimumCutThb: 200, confidence: 'Low', supplierReviewRequired: true, supplier: 'myps', evidence: 'myps_polycarbonate_catalog_2026-08-29' },
            machining: { mrrMultiplier: 1.25, finishMultiplier: 0.8, toolingWearMultiplier: 0.6 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'tap', 'thread_mill', 'chamfer_mill'],
            risks: ['thermal_softening', 'stress_cracking'], enabled: true
        }),
        material({
            code: 'PEEK', label: 'PEEK', family: 'engineering_plastic', densityKgPerMm3: 1.32 / 1000000,
            stockForms: ['rectangular', 'round', 'plate'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 4500, minimumCutThb: 1000, confidence: 'Low', supplierReviewRequired: true, supplier: 'myps', evidence: 'myps_peek_catalog_2026-08-29' },
            machining: { mrrMultiplier: 0.72, finishMultiplier: 0.72, toolingWearMultiplier: 0.85 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'tap', 'thread_mill', 'chamfer_mill'],
            risks: ['high_stock_cost', 'heat_build_up'], enabled: true
        }),
        material({
            code: 'PEI', label: 'PEI / Ultem', family: 'engineering_plastic', densityKgPerMm3: 1.27 / 1000000,
            stockForms: ['rectangular', 'round', 'plate'],
            procurement: { mode: 'benchmark_mass', thbPerKg: 2200, minimumCutThb: 700, confidence: 'Low', supplierReviewRequired: true, supplier: 'myps', evidence: 'myps_ultem_pei_catalog_2026-08-29' },
            machining: { mrrMultiplier: 0.85, finishMultiplier: 0.75, toolingWearMultiplier: 0.75 },
            recommendedToolFamilies: ['face_mill', 'flat_end_mill', 'ball_end_mill', 'drill', 'tap', 'thread_mill', 'chamfer_mill'],
            risks: ['high_stock_cost', 'stress_cracking'], enabled: true
        })
    ]);

    var materialsByCode = Object.create(null);
    materials.forEach(function (entry) {
        materialsByCode[entry.code] = entry;
    });
    Object.freeze(materialsByCode);

    function get(code) {
        return typeof code === 'string' && materialsByCode[code] ? materialsByCode[code] : null;
    }

    function listEnabled() {
        return Object.freeze(materials.filter(function (entry) { return entry.enabled; }));
    }

    function adjustRate(code, baseRate, kind) {
        var entry = get(code);
        if (!entry || typeof baseRate !== 'number' || !Number.isFinite(baseRate) || baseRate < 0) {
            throw new RangeError('A supported CNC material and non-negative base rate are required.');
        }

        var factors = {
            mrr: 'mrrMultiplier',
            finish: 'finishMultiplier',
            toolingWear: 'toolingWearMultiplier'
        };
        var factorName = factors[kind];
        if (!factorName) {
            throw new RangeError('Unsupported CNC machining rate kind: ' + kind);
        }

        return baseRate * entry.machining[factorName];
    }

    function estimateStockBeforeVat(code, massKg, form) {
        var entry = get(code);
        if (!entry || typeof massKg !== 'number' || !Number.isFinite(massKg) || massKg <= 0) {
            throw new RangeError('A supported CNC material and positive stock mass are required.');
        }
        if (entry.stockForms.indexOf(form) < 0) {
            throw new RangeError('Unsupported stock form for CNC material ' + code + ': ' + form);
        }

        var procurement = entry.procurement;
        var priceBeforeVat;
        if (procurement.mode === 'calibrated_curve') {
            if (!root.CncStock || typeof root.CncStock.expectedPiecePriceBeforeVat !== 'function') {
                throw new Error('The calibrated CNC stock model must load before estimating aluminum stock.');
            }
            priceBeforeVat = root.CncStock.expectedPiecePriceBeforeVat(code, massKg);
        } else {
            priceBeforeVat = Math.ceil(Math.max(procurement.minimumCutThb, massKg * procurement.thbPerKg) / 10) * 10;
        }

        return freezeRecord({
            priceBeforeVat: priceBeforeVat,
            confidence: procurement.confidence,
            supplierReviewRequired: procurement.supplierReviewRequired
        });
    }

    root.CncMaterialCatalog = Object.freeze({
        version: config.materialCatalogVersion,
        get: get,
        listEnabled: listEnabled,
        adjustRate: adjustRate,
        estimateStockBeforeVat: estimateStockBeforeVat
    });
}(typeof window !== 'undefined' ? window : self));
