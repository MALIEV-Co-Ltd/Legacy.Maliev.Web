const test = require('node:test');
const assert = require('node:assert/strict');
const { normalizeQuery, searchSections } = require('../../Legacy.Maliev.Web/wwwroot/src/app/js/knowledge-search.js');

function section(overrides = {}) {
    return { href: '/knowledges/cnc/feeds-and-speeds?culture=en#milling', title: 'Milling calculations',
        articleTitle: 'Feeds and speeds', aliases: 'chip load spindle', text: 'Calculate 3819.7 rpm for a 10 mm cutter.', ...overrides };
}

test('normalization handles compatibility characters, case and whitespace', () => {
    assert.equal(normalizeQuery('  ＣＮＣ\tM6\n  ３８１９.７  '), 'cnc m6 3819.7');
    assert.equal(normalizeQuery('  พิมพ์\tสามมิติ  '), 'พิมพ์ สามมิติ');
});

test('an empty or whitespace-only query returns no matches', () => {
    for (const query of ['', ' \t\n']) assert.deepEqual(searchSections([section()], query), { total: 0, items: [] });
});

test('all query words must match, even when distributed across fields', () => {
    assert.equal(searchSections([section()], 'spindle 3819.7').total, 1);
    assert.equal(searchSections([section()], 'spindle titanium').total, 0);
});

test('numeric technical data in body text is discoverable', () => {
    const result = searchSections([section()], '3819.7');
    assert.equal(result.total, 1);
    assert.equal(result.items[0].href, section().href);
    assert.match(result.items[0].snippet, /3819\.7/);
});

test('heading matches outrank body-only matches', () => {
    const body = section({ href: '/body', title: 'Tool selection', articleTitle: 'Tooling', aliases: '', text: 'A milling operation.' });
    const heading = section({ href: '/heading', title: 'Milling', aliases: '', text: 'Tool geometry.' });
    assert.equal(searchSections([body, heading], 'milling').items[0].href, '/heading');
});

test('article-title matches outrank body-only matches', () => {
    const body = section({ href: '/body', title: 'Overview', articleTitle: 'Geometry', aliases: '', text: 'Consider anodizing.' });
    const article = section({ href: '/article', title: 'Dimensions', articleTitle: 'Anodizing', aliases: '', text: 'Growth allowance.' });
    assert.equal(searchSections([body, article], 'anodizing').items[0].href, '/article');
});

test('Thai body text and English aliases identify the same localized article', () => {
    const thai = section({ href: '/knowledges/cnc/feeds-and-speeds?culture=th#milling', title: 'คำนวณอัตราป้อน',
        articleTitle: 'ความเร็วตัดและอัตราป้อน', aliases: 'feeds speeds chip load', text: 'ตรวจสอบรอบและอัตราป้อน 3819.7 rpm' });
    assert.equal(searchSections([thai], 'อัตราป้อน').total, 1);
    assert.equal(searchSections([thai], 'chip load').items[0].href, thai.href);
});

test('limited results retain the complete matching total', () => {
    const sections = Array.from({ length: 12 }, (_, index) => section({ href: `/part-${index}` }));
    assert.equal(searchSections(sections, 'milling').items.length, 8);
    const result = searchSections(sections, 'milling', 3);
    assert.equal(result.total, 12);
    assert.equal(result.items.length, 3);
});

test('snippets include a late body match without returning the entire article', () => {
    const result = searchSections([section({ text: 'Background detail. '.repeat(100) + '3819.7 rpm is the worked example.' })], '3819.7');
    assert.match(result.items[0].snippet, /3819\.7/);
    assert.ok(result.items[0].snippet.length <= 220);
});

test('search preserves its input and returns plain text without interpreting markup', () => {
    const input = [Object.freeze(section({ title: '<img src=x onerror=alert(1)>', text: 'Literal <script> text, 3819.7 rpm.' }))];
    const before = JSON.stringify(input);
    Object.freeze(input);
    const result = searchSections(input, '3819.7');
    assert.equal(JSON.stringify(input), before);
    assert.equal(result.items[0].title, input[0].title);
    assert.equal(typeof result.items[0].snippet, 'string');
    assert.equal(result.items[0].text, input[0].text);
});

test('literal field weights are heading150 alias80 article40 body1 with stable ties', () => {
    const items = [
        section({ href: '/body', title: '', articleTitle: '', aliases: '', text: 'milling' }),
        section({ href: '/article', title: '', articleTitle: 'milling', aliases: '', text: '' }),
        section({ href: '/alias', title: '', articleTitle: '', aliases: 'milling', text: '' }),
        section({ href: '/heading-a', title: 'milling', articleTitle: '', aliases: '', text: '' }),
        section({ href: '/heading-b', title: 'milling', articleTitle: '', aliases: '', text: '' })
    ];
    assert.deepEqual(searchSections(items, 'milling').items.map(x => [x.href, x.score]),
        [['/heading-a', 150], ['/heading-b', 150], ['/alias', 80], ['/article', 40], ['/body', 1]]);
});

test('duplicate query terms do not inflate relevance or change all-term matching', () => {
    const result = searchSections([section({ title: 'milling', articleTitle: '', aliases: '', text: '3819.7' })], 'milling milling 3819.7');
    assert.equal(result.total, 1);
    assert.equal(result.items[0].score, 151);
});
