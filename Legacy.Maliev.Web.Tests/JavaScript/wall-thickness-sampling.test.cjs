const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const workerPath = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation/wall-thickness.worker.js');
const thickness = require(workerPath);

function reference(triangles, maximum) {
    if (triangles.length <= maximum) { return triangles.map((_, index) => index); }
    const total = triangles.reduce((sum, triangle) => sum + triangle.area, 0);
    const selected = new Set();
    let cursor = 0;
    let cumulative = triangles[0].area;
    for (let sample = 0; sample < maximum; sample++) {
        const target = total * ((sample + 0.5) / maximum);
        while (cursor < triangles.length - 1 && cumulative < target) { cumulative += triangles[++cursor].area; }
        let chosen = cursor;
        while (chosen < triangles.length && selected.has(chosen)) { chosen++; }
        if (chosen >= triangles.length) {
            chosen = cursor - 1;
            while (chosen >= 0 && selected.has(chosen)) { chosen--; }
        }
        if (chosen >= 0) { selected.add(chosen); }
    }
    for (let index = 0; index < triangles.length && selected.size < maximum; index++) { selected.add(index); }
    return [...selected].sort((a, b) => a - b);
}

test('sampling preserves exact area-quantile choices, including backward fallback', () => {
    for (let count = 2; count <= 50; count++) {
        for (let maximum = 1; maximum <= count; maximum++) {
            for (const dominant of [0, Math.floor(count / 2), count - 1]) {
                const triangles = Array.from({ length: count }, (_, index) => ({
                    area: index === dominant ? 10000 : ((index * 17) % 11 + 1) / 100,
                }));
                assert.deepEqual(thickness._test.selectAreaQuantiles(triangles, maximum), reference(triangles, maximum));
            }
        }
    }
});

test('a dense model with uneven triangle areas selects 200000 unique samples within its budget', () => {
    const triangles = Array.from({ length: 225000 }, (_, index) => ({ area: index === 0 ? 1e9 : 1e-6 }));
    const context = vm.createContext({ self: {}, triangles });
    vm.runInContext(fs.readFileSync(workerPath, 'utf8'), context);
    const result = vm.runInContext('self.MalievWallThickness._test.selectAreaQuantiles(triangles, 200000)', context,
        { timeout: 1500 });
    assert.equal(result.length, 200000);
    assert.equal(new Set(result).size, 200000);
    assert.equal(result[0], 0);
    assert.equal(result[result.length - 1], 199999);
});
