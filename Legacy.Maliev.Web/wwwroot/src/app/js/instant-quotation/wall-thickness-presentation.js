(function (root, factory) {
    var api = factory();
    if (typeof module === 'object' && module.exports) { module.exports = api; }
    root.MalievWallThicknessPresentation = api;
}(typeof self !== 'undefined' ? self : globalThis, function () {
    'use strict';

    var MIN_RELIABLE_COVERAGE = 0.75;
    var RESIN_MATERIALS = new Set(['M68', 'K', 'G217', 'F80', 'CASTWAX']);

    function policyForMaterial(materialKey) {
        return RESIN_MATERIALS.has(String(materialKey || '').toUpperCase())
            ? { process: 'resin', thinMm: 0.6, thickMm: 3.0 }
            : { process: 'fdm', thinMm: 0.8, thickMm: 4.0 };
    }

    function assess(summary, thicknesses, areas, policy) {
        summary = summary || {};
        thicknesses = thicknesses || [];
        areas = areas || [];
        var belowArea = 0;
        var belowCount = 0;
        for (var i = 0; i < thicknesses.length; i += 1) {
            if (Number.isFinite(thicknesses[i]) && thicknesses[i] < policy.thinMm) {
                belowCount += 1;
                belowArea += Number(areas[i]) || 0;
            }
        }
        var reliable = summary.state !== 'unavailable'
            && Number(summary.measuredAreaRatio) >= MIN_RELIABLE_COVERAGE;
        return {
            process: policy.process,
            thinMm: policy.thinMm,
            thickMm: policy.thickMm,
            state: reliable ? summary.state : 'partial',
            reliable: reliable,
            hasThinRegion: belowCount > 0,
            belowThinCount: belowCount,
            belowThinAreaMm2: belowArea
        };
    }

    function alertsFor(assessment, culture) {
        if (!assessment) { return []; }
        var thai = String(culture || '').toLowerCase().indexOf('th') === 0;
        var processLabel = assessment.process === 'resin' ? 'SLA/DLP' : 'FDM';
        var limit = Number(assessment.thinMm).toFixed(2);
        var alerts = [];
        if (assessment.hasThinRegion) {
            alerts.push({
                level: 'warn',
                iconChar: '⚠',
                title: thai ? 'ผนังบางอาจเกิดข้อบกพร่อง' : 'Thin walls may print with defects',
                subtitle: thai
                    ? 'ผนังบางบางส่วนต่ำกว่า ' + limit + ' มม. สำหรับ ' + processLabel + ' และอาจพิมพ์เป็นรู ผนังไม่แข็งแรง หรือรายละเอียดหายไป'
                    : 'Some walls are below ' + limit + ' mm for ' + processLabel + ' and may print with holes, weak walls, or missing details.'
            });
        }
        if (assessment.reliable === false) {
            alerts.push({
                level: 'warn',
                iconChar: '⚠',
                title: thai ? 'ตรวจวัดความหนาได้ไม่ครบทุกบริเวณ' : 'Thickness analysis is incomplete',
                subtitle: thai
                    ? 'บางบริเวณไม่สามารถตรวจวัดได้อย่างน่าเชื่อถือ เนื่องจากคุณภาพเมชหรือข้อจำกัดของการวิเคราะห์'
                    : 'Some regions could not be measured reliably because of mesh quality or analysis limits.'
            });
        }
        return alerts;
    }

    return {
        policyForMaterial: policyForMaterial,
        assess: assess,
        alertsFor: alertsFor,
        MIN_RELIABLE_COVERAGE: MIN_RELIABLE_COVERAGE
    };
}));
