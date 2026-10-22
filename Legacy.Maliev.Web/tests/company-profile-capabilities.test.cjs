const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname,
    '../wwwroot/src/app/js/company-profile-capabilities.js'), 'utf8');

function fixture(options = {}) {
    class Node {
        constructor(text = '') { this.textContent = text; this.dataset = {}; this.events = new Map(); this.attrs = {}; }
        setAttribute(key, value) { this.attrs[key] = value; }
        addEventListener(key, handler) { this.events.set(key, handler); }
        removeEventListener(key) { this.events.delete(key); }
        focus() { this.focused = true; }
    }
    const root = new Node();
    const track = new Node();
    track.scrollLeft = 0;
    track.calls = [];
    track.scrollTo = value => track.calls.push(value);
    if (options.unsupported) delete track.scrollTo;
    const controls = new Node(); controls.hidden = true;
    const choices = new Node();
    const previous = new Node(); const next = new Node(); const status = new Node();
    const animations = [];
    if (options.animated) status.animate = (frames, timing) => {
        let finish;
        const animation = { frames, timing, finished: new Promise(resolve => { finish = resolve; }), finish: () => finish(), cancel() { this.cancelled = true; } };
        animations.push(animation);
        return animation;
    };
    const timers = new Map(); let timerId = 0;
    const buttons = ['CNC', 'Printing', 'Scanning', 'Design', 'Silicone', 'Injection'].map(text => new Node(text));
    const slides = buttons.map((_, i) => ({
        offsetLeft: Math.round(20 + i * ((options.width ?? 1000) + (options.gap ?? 0))),
        getBoundingClientRect: () => ({ left: 20 + i * ((options.width ?? 1000) + (options.gap ?? 0)) - track.scrollLeft })
    }));
    const selectors = new Map([
        ['[data-capability-track]', track], ['[data-capability-controls]', controls],
        ['.profile-capability-choices', choices], ['[data-capability-prev]', previous],
        ['[data-capability-next]', next], ['[data-capability-status]', status]
    ]);
    root.querySelector = selector => selectors.get(selector);
    root.querySelectorAll = selector => selector === '.profile-capability-slide' ? slides : buttons;
    const events = new Map();
    const context = vm.createContext({ document: { querySelector: () => options.otherPage ? null : root }, window: {
        matchMedia: () => ({ matches: Boolean(options.reduced) }),
        setTimeout: handler => { timers.set(++timerId, handler); return timerId; },
        clearTimeout: id => timers.delete(id),
        addEventListener: (key, handler) => events.set(key, handler)
    } });
    const run = () => vm.runInContext(source, context);
    run();
    return { root, track, controls, choices, previous, next, status, buttons, slides, events, run, animations, timers };
}

test('capability controls initialize without autoplay and expose the first service', () => {
    const f = fixture();
    assert.equal(f.controls.hidden, false);
    assert.equal(f.root.dataset.carouselState, 'ready');
    assert.equal(f.previous.disabled, true);
    assert.equal(f.next.disabled, false);
    assert.equal(f.status.textContent, '1 / 6 · CNC');
    assert.equal(f.track.calls.length, 0);
});

test('service choice scrolls to its own slide and updates the pressed state', () => {
    const f = fixture();
    f.buttons[3].events.get('click')();
    assert.equal(f.track.calls[0].left, 3000);
    assert.equal(f.track.calls[0].behavior, 'smooth');
    assert.equal(f.buttons[3].attrs['aria-pressed'], 'true');
    assert.equal(f.buttons[0].attrs['aria-pressed'], 'false');
    assert.equal(f.status.textContent, '4 / 6 · Design');
});

test('previous and next stay within bounds rather than looping', () => {
    const f = fixture();
    f.previous.events.get('click')();
    assert.equal(f.track.calls.at(-1).left, 0);
    for (let i = 0; i < 8; i++) f.next.events.get('click')();
    assert.equal(f.next.disabled, true);
    assert.equal(f.track.calls.at(-1).left, 5000);
    f.previous.events.get('click')();
    assert.equal(f.track.calls.at(-1).left, 4000);
});

test('keyboard selection moves focus and reduced motion never requests smooth scrolling', () => {
    const f = fixture({ reduced: true });
    let prevented = false;
    f.choices.events.get('keydown')({ key: 'End', preventDefault() { prevented = true; } });
    assert.equal(prevented, true);
    assert.equal(f.buttons[5].focused, true);
    assert.equal(f.track.calls.at(-1).behavior, 'auto');
    f.choices.events.get('keydown')({ key: 'Home', preventDefault() {} });
    assert.equal(f.buttons[0].focused, true);
    assert.equal(f.track.calls.at(-1).left, 0);
});

test('manual swipe or scrollbar navigation synchronizes the service selector', () => {
    const f = fixture();
    f.track.scrollLeft = 1980;
    f.track.events.get('scroll')();
    assert.equal(f.status.textContent, '3 / 6 · Scanning');
    assert.equal(f.buttons[2].attrs['aria-pressed'], 'true');
});

test('fractional slide widths and spacing preserve exact alignment for every service', () => {
    const f = fixture({ width: 1000.25, gap: 32 });
    f.buttons.forEach((button, i) => {
        button.events.get('click')();
        const expected = i * 1032.25;
        assert.equal(f.track.calls.at(-1).left, expected);
        f.track.scrollLeft = expected;
        f.track.events.get('scrollend')();
        assert.equal(button.attrs['aria-pressed'], 'true');
        assert.equal(f.status.textContent.startsWith(`${i + 1} / 6`), true);
    });
});

test('missing scroll API leaves native reading intact with enhancement controls hidden', () => {
    const f = fixture({ unsupported: true });
    assert.equal(f.controls.hidden, true);
    assert.equal(f.track.events.size, 0);
    assert.equal(f.slides.length, 6);
    assert.doesNotThrow(() => fixture({ otherPage: true }));
});

test('duplicate initialization and page teardown do not retain duplicate listeners', () => {
    const f = fixture();
    const handler = f.next.events.get('click');
    f.run();
    assert.equal(f.next.events.get('click'), handler);
    f.events.get('pagehide')();
    assert.equal(f.controls.hidden, true);
    assert.equal(f.next.events.size, 0);
    assert.equal(f.track.events.size, 0);
    assert.ok(f.buttons.every(button => button.events.size === 0));
});

test('smooth navigation keeps the requested status while passing intermediate slides', () => {
    const f = fixture();
    f.buttons[5].events.get('click')();
    for (const position of [300, 1400, 2700, 3900, 5000]) {
        f.track.scrollLeft = position;
        f.track.events.get('scroll')();
        assert.equal(f.status.textContent, '6 / 6 · Injection');
        assert.equal(f.buttons[5].attrs['aria-pressed'], 'true');
    }
    f.track.events.get('scrollend')();
    assert.equal(f.timers.size, 0);
    assert.equal(f.status.textContent, '6 / 6 · Injection');
});

test('status fades out before swapping to the latest label, then fades in', async () => {
    const f = fixture({ animated: true });
    f.buttons[2].events.get('click')();
    assert.equal(f.status.textContent, '1 / 6 · CNC');
    assert.equal(f.animations[0].timing.duration, 100);
    f.buttons[5].events.get('click')();
    assert.equal(f.animations.length, 1);
    f.animations[0].finish(); await new Promise(resolve => setImmediate(resolve));
    assert.equal(f.status.textContent, '6 / 6 · Injection');
    assert.equal(f.animations[1].frames[0].opacity, 0);
    assert.equal(f.animations[1].timing.duration, 160);
    f.animations[1].finish(); await new Promise(resolve => setImmediate(resolve));
    assert.ok(f.animations.every(animation => animation.cancelled));
});

test('interrupted scrolling settles to the actual service without scrollend support', () => {
    const f = fixture();
    f.buttons[5].events.get('click')();
    f.track.scrollLeft = 2000;
    f.track.events.get('scroll')();
    [...f.timers.values()][0]();
    assert.equal(f.status.textContent, '3 / 6 · Scanning');
});

test('reduced motion skips status fades and teardown cancels pending work', () => {
    const reduced = fixture({ animated: true, reduced: true });
    reduced.buttons[3].events.get('click')();
    assert.equal(reduced.animations.length, 0);
    assert.equal(reduced.status.textContent, '4 / 6 · Design');
    const f = fixture({ animated: true });
    f.buttons[5].events.get('click')();
    f.events.get('pagehide')();
    assert.equal(f.timers.size, 0);
    assert.ok(f.animations[0].cancelled);
});

test('unrecognized keys do not hijack native button keyboard behavior', () => {
    const f = fixture();
    let prevented = false;
    f.choices.events.get('keydown')({ key: 'Tab', preventDefault() { prevented = true; } });
    assert.equal(prevented, false);
    assert.equal(f.track.calls.length, 0);
    assert.equal(f.buttons.some(button => button.focused), false);
    assert.equal(f.status.textContent, '1 / 6 · CNC');
});

test('page teardown prevents a pending status fade from announcing stale selection', async () => {
    const f = fixture({ animated: true });
    f.buttons[5].events.get('click')();
    assert.equal(f.animations.length, 1);
    f.events.get('pagehide')();
    f.animations[0].finish();
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(f.root.dataset.carouselState, 'static');
    assert.equal(f.controls.hidden, true);
    assert.equal(f.status.textContent, '1 / 6 · CNC');
    assert.equal(f.timers.size, 0);
});
