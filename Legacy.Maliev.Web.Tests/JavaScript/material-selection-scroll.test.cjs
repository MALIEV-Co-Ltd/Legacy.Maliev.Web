const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname,
  '../../Legacy.Maliev.Web/wwwroot/src/app/js/model-viewer/model-viewer.js'), 'utf8');
const start = source.indexOf('    var lastRevealedMaterialKey = null;');
const end = source.indexOf('    this.ToggleMaterialList = function', start);
assert.ok(start >= 0 && end > start, 'Use the actual material rendering and reveal functions');

function fixture() {
  const frames = [];
  let cardTop = 60;
  let cardHeight = 40;
  const card = {
    dataset: { key: 'PLA-CF' },
    previousElementSibling: null,
    getBoundingClientRect() {
      return { top: cardTop - container.scrollTop, bottom: cardTop + cardHeight - container.scrollTop, height: cardHeight };
    }
  };
  const container = {
    scrollTop: 0, clientHeight: 100,
    getBoundingClientRect() { return { top: 0, bottom: this.clientHeight }; },
    querySelector() { return card; },
    replaceChildren(fragment) { card.dataset.key = fragment.key; cardHeight = fragment.height; }
  };
  const item = { material: 'PLA-CF', materialPriceState: 'loading', materialPrices: { height: 40 } };
  const context = {
    activeId: 'part', items: { part: item }, materialSearch: '', materialsExpanded: true,
    materialPreviewCount: 8, culture: 'en', currencyString: 'THB',
    MATERIALS: Array.from({ length: 20 }, (_, i) => ({ key: i === 1 ? 'PLA-CF' : 'material-' + i })),
    document: { documentElement: { clientWidth: 1600, clientHeight: 900 },
      getElementById(id) { return id === 'material-cards' ? container : null; } },
    window: { innerWidth: 1600, innerHeight: 900, addEventListener() {},
      requestAnimationFrame(callback) { frames.push(callback); } },
    BuildMaterialCardsFragment(key, search, culture, limit, state, prices) {
      return { key, height: prices.height };
    }
  };
  vm.createContext(context);
  vm.runInContext(source.slice(start, end), context);
  return {
    context, item, container, card,
    render() { context.RenderMaterialCards(); },
    frame() { while (frames.length) { frames.shift()(); } },
    visible() { const box = card.getBoundingClientRect(); return box.top >= -1 && box.bottom <= container.clientHeight + 1; },
    moveCard(top) { cardTop = top; }
  };
}

test('price rerender preserves the visible selected material', () => {
  const f = fixture();
  f.render(); f.frame();
  assert.equal(f.visible(), true);
  f.item.materialPriceState = 'ready';
  f.item.materialPrices.height = 56;
  f.render(); f.frame();
  assert.equal(f.visible(), true, 'A price row growing after selection must not hide the selected card');
});

test('selection reveal measures the completed frame layout', () => {
  const f = fixture();
  f.render();
  f.container.clientHeight = 84;
  f.frame();
  assert.equal(f.visible(), true, 'Layout changes in the selection event must be included');
});

test('price rerender preserves a customer scroll away from the selection', () => {
  const f = fixture();
  f.render(); f.frame();
  f.container.scrollTop = 120;
  f.item.materialPrices.height = 56;
  f.render(); f.frame();
  assert.equal(f.container.scrollTop, 120);
  assert.equal(f.visible(), false);
});
