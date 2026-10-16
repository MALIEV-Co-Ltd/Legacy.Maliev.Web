import assert from 'node:assert/strict';
import test from 'node:test';
import { materialPreviewCount } from '../wwwroot/src/app/js/instant-quotation/material-card-selector.mjs';

for (const [width, height, count] of [
    [375, 799, 4], [375, 800, 6], [767, 900, 6], [1199, 1100, 6],
    [1280, 820, 6], [1600, 900, 8], [1600, 1000, 10],
]) {
    test(`source responsive preview ${width} × ${height} preserves ${count} cards`, () => {
        assert.equal(materialPreviewCount(width, height), count);
    });
}
