const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname, '../wwwroot/src/app/js/home-hero-callouts.js'), 'utf8');

function fixture(options = {}) {
    let bounds = { left: 100, top: 50, width: 600, height: 545.5 };
    let labelHeight = 35;
    const keys = ['resin', 'fdm', 'nylon', 'cnc'];
    const targets = [[790,289],[580,335],[300,615],[850,780]];
    const paths = keys.map((key, i) => ({ dataset: { techLeader: key, targetX: targets[i][0], targetY: targets[i][1] }, attrs: {}, setAttribute(k,v) { this.attrs[k] = v; } }));
    const labels = keys.map((_, i) => ({ getBoundingClientRect() {
        const width = Math.min(120, bounds.width * .39);
        const left = i === 3 ? bounds.left + bounds.width * .98 - width : bounds.left + bounds.width * (i === 0 ? .59 : .02);
        const top = bounds.top + bounds.height * (i < 2 ? .04 : .88);
        return { left, right: left + width, width, top, bottom: top + labelHeight };
    } }));
    const root = { dataset: {}, getBoundingClientRect: () => bounds, querySelectorAll: () => paths, querySelector: selector => labels[keys.findIndex(key => selector.endsWith(key))] };
    const frames = new Map(), events = new Map(); let clock = 0, observer;
    const window = {
        requestAnimationFrame: fn => { frames.set(++clock, fn); return clock; },
        cancelAnimationFrame: id => frames.delete(id),
        addEventListener: (key,fn) => events.set(key,fn), removeEventListener: key => events.delete(key),
        ResizeObserver: class { constructor(fn) { this.callback=fn; observer=this; this.nodes=[]; } observe(node) { this.nodes.push(node); } disconnect() { this.disconnected=true; } }
    };
    let finishFonts;
    const fonts = options.fonts ? { ready: new Promise(resolve => { finishFonts = resolve; }) } : undefined;
    if (options.noObserver) delete window.ResizeObserver;
    const context = vm.createContext({ document: { querySelectorAll: () => [root], fonts }, window });
    const run = () => vm.runInContext(source, context);
    const draw = () => { const pending=[...frames.values()]; frames.clear(); pending.forEach(fn=>fn()); };
    run(); draw();
    return { paths, labels, events, frames, observer, root, run, draw, finishFonts,
        resize() { bounds={...bounds,width:300,height:272.75}; observer.callback(); },
        wrap() { labelHeight=55; observer.callback(); }
    };
}

test('leaders start beside rendered text and preserve their component targets', () => {
    const f=fixture();
    const values = f.paths[0].attrs.d.match(/[\d.]+/g).map(Number);
    assert.equal(values[0],828);
    assert.ok(Math.abs(values[1]-125.64)<.001);
    assert.deepEqual(values.slice(2),[790,289]);
    assert.equal(Number(f.paths[1].attrs.d.match(/[\d.]+/g)[0]),272, 'FDM starts beside the right edge of its text');
    assert.equal(Number(f.paths[3].attrs.d.match(/[\d.]+/g)[0]),944, 'CNC starts beside the left edge of its right-aligned text');
    assert.ok(f.paths.every(p => !/[HV]/.test(p.attrs.d)), 'leaders must not detour along the old bubble edges');
    assert.ok(f.paths[2].attrs.d.endsWith('L300 615'));
    assert.equal(f.observer.nodes.length,5);
});

test('leader anchors follow label wrapping and responsive dimensions', () => {
    const f=fixture(); const before=f.paths[0].attrs.d;
    f.wrap(); f.draw(); assert.notEqual(f.paths[0].attrs.d,before);
    const wrapped=f.paths[0].attrs.d;
    f.resize(); f.draw(); assert.notEqual(f.paths[0].attrs.d,wrapped);
    assert.ok(f.paths[0].attrs.d.endsWith('L790 289'));
});

test('initialization is idempotent and teardown releases observation and queued layout', () => {
    const f=fixture(); f.run(); assert.equal(f.frames.size,0);
    f.events.get('resize')(); assert.equal(f.frames.size,1);
    f.events.get('pagehide')({persisted:true}); assert.equal(f.observer.disconnected,undefined);
    f.events.get('pagehide')({persisted:false}); assert.equal(f.frames.size,0);
    assert.equal(f.observer.disconnected,true); assert.equal(f.events.size,0);
});

test('partnership service list is absent from the shipped hero partial', () => {
    const markup=fs.readFileSync(path.resolve(__dirname,'../Components/Pages/Home/HomeHeroScene.razor'),'utf8');
    assert.doesNotMatch(markup,/hero-process-line/);
    assert.equal((markup.match(/data-tech-leader=/g)||[]).length,4);
    for (const [key,x,y] of [['resin',790,289],['fdm',580,335],['nylon',300,615],['cnc',850,780]]) {
        assert.ok(markup.includes(`data-tech-leader="${key}" data-target-x="${x}" data-target-y="${y}"`));
        assert.ok(markup.includes(`<circle cx="${x}" cy="${y}"`));
    }
    const css=fs.readFileSync(path.resolve(__dirname,'../wwwroot/src/app/css/home-hero-variants.css'),'utf8');
    assert.match(css,/\.hero-tech-resin \{ top: 4%; left: 59%; \}/);
    assert.match(css,/\.hero-tech-fdm \{ top: 4%; left: 2%; \}/);
    assert.match(css,/\.hero-tech-cnc \{ bottom: 4%; right: 2%; \}/);
});

test('font readiness coalesces layout and cannot schedule work after teardown', async () => {
    const f = fixture({ fonts: true });
    f.finishFonts(); await Promise.resolve(); assert.equal(f.frames.size, 1); f.draw();
    f.events.get('pagehide')({ persisted: false });
    const stopped = fixture({ fonts: true });
    stopped.events.get('pagehide')({ persisted: false });
    stopped.finishFonts(); await Promise.resolve(); assert.equal(stopped.frames.size, 0);
});

test('window resize remains a fallback when ResizeObserver is unavailable', () => {
    const f = fixture({ noObserver: true });
    f.events.get('resize')(); assert.equal(f.frames.size, 1); f.draw();
    assert.equal(f.paths.length, 4);
    f.events.get('pagehide')({ persisted: false }); assert.equal(f.events.size, 0);
});
