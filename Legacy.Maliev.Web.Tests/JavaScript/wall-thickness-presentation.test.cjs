const test = require('node:test');
const assert = require('node:assert/strict');
const { alertsFor } = require('../../Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation/wall-thickness-presentation.js');

test('incomplete coverage retains the source Thai and English explanation', () => {
    const assessment = { reliable: false, hasThinRegion: false, process: 'fdm', thinMm: 0.8 };
    assert.deepEqual(alertsFor(assessment, 'th').map(({ title, subtitle }) => [title, subtitle]), [[
        'ตรวจวัดความหนาได้ไม่ครบทุกบริเวณ',
        'บางบริเวณไม่สามารถตรวจวัดได้อย่างน่าเชื่อถือ เนื่องจากคุณภาพ mesh หรือข้อจำกัดของการวิเคราะห์'
    ]]);
    assert.deepEqual(alertsFor(assessment, 'en').map(({ title, subtitle }) => [title, subtitle]), [[
        'Thickness analysis is incomplete',
        'Some regions could not be measured reliably because of mesh quality or analysis limits.'
    ]]);
});
