const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.resolve(__dirname,
    '../wwwroot/src/app/js/company-profile-presentation.js'), 'utf8');

function fixture(options = {}) {
    const animations = [];
    const observers = [];
    const documentEvents = new Map();
    const windowEvents = new Map();
    class Element {
        constructor(name) {
            this.name = name;
            this.dataset = {};
            this.children = new Map();
            this.events = new Map();
            this.classList = { contains: () => false };
        }
        setAttribute(name) { if (name === 'data-presentation-pending') this.dataset.presentationPending = ''; }
        removeAttribute(name) { if (name === 'data-presentation-pending') delete this.dataset.presentationPending; }
        getBoundingClientRect() { return { top: options.inView ? 100 : 1200 }; }
        querySelector(selector) { return (this.children.get(selector) || [])[0] || null; }
        querySelectorAll(selector) { return this.children.get(selector) || []; }
        addEventListener(name, handler) { this.events.set(name, handler); }
        removeEventListener(name) { this.events.delete(name); }
        animate(frames, timing) {
            if (options.throwAnimation) throw new Error('Animation unavailable');
            const animation = {
                target: this, frames, timing, cancelled: false,
                finished: new Promise(() => {}),
                cancel() { this.cancelled = true; }
            };
            animations.push(animation);
            return animation;
        }
    }
    if (options.unsupported) delete Element.prototype.animate;
    const page = new Element('page');
    const hero = new Element('hero');
    hero.children.set('.landing-hero-copy', [new Element('copy')]);
    hero.children.set('.profile-partnership-photo', [new Element('photo')]);
    hero.children.set('.landing-hero-copy, .profile-partnership-photo', [...hero.children.values()].flat());
    const deployment = new Element('deployment');
    deployment.children.set('.profile-story-media', [new Element('installed-camera')]);
    deployment.children.set('.profile-story-grid > div', [new Element('installation-copy')]);
    deployment.children.set('.profile-story-media, .profile-story-grid > div', [...deployment.children.values()].flat());
    page.children.set('.profile-development, .profile-deployment', [deployment]);
    const workflow = new Element('workflow');
    const cards = [0, 1, 2, 3].map(index => new Element('stage-' + index));
    workflow.children.set(':scope > article', cards);
    page.children.set('.profile-partnership-hero', [hero]);
    page.children.set('.profile-rapid-workflow-grid', [workflow]);
    page.children.set('.profile-rapid-workflow-grid > article', cards);
    function media(matches) {
        const listeners = new Set();
        return {
            matches,
            addEventListener: (_, handler) => listeners.add(handler),
            removeEventListener: (_, handler) => listeners.delete(handler),
            change(value) { this.matches = value; listeners.forEach(handler => handler()); },
            listeners
        };
    }
    const reduced = media(Boolean(options.reduced));
    const compact = media(Boolean(options.compact));
    class IntersectionObserver {
        constructor(callback) {
            if (options.throwObserver) throw new Error('Observer unavailable');
            this.callback = callback;
            this.observed = new Set();
            observers.push(this);
        }
        observe(element) { this.observed.add(element); }
        unobserve(element) { this.observed.delete(element); }
        disconnect() { this.disconnected = true; this.observed.clear(); }
        enter(element, isIntersecting = true) { this.callback([{ target: element, isIntersecting }], this); }
    }
    const document = {
        hidden: false,
        querySelector: () => options.otherPage ? null : page,
        addEventListener: (name, handler) => documentEvents.set(name, handler),
        removeEventListener: name => documentEvents.delete(name)
    };
    const context = vm.createContext({
        document, Element, IntersectionObserver,
        window: {
            innerHeight: 900,
            matchMedia: query => query.includes('reduced-motion') ? reduced : compact,
            addEventListener: (name, handler) => windowEvents.set(name, handler)
        }
    });
    const run = () => vm.runInContext(source, context);
    run();
    return { page, hero, deployment, workflow, cards, animations, observers, reduced, compact,
        document, documentEvents, windowEvents, run };
}

test('workflow waits for intersection, then advances once in design-to-production order within 800 ms', () => {
    const f = fixture();
    const observer = f.observers[0];
    assert.equal(f.animations.length, 0);
    observer.enter(f.workflow, false);
    assert.equal(f.animations.length, 0);
    observer.enter(f.workflow);
    assert.deepEqual(f.animations.map(a => a.target.name), ['stage-0', 'stage-1', 'stage-2', 'stage-3']);
    assert.deepEqual(f.animations.map(a => a.timing.delay), [0, 100, 200, 300]);
    assert.ok(f.animations.every(a => a.timing.delay + a.timing.duration <= 800));
    assert.ok(f.animations.every(a => a.frames[0].opacity === 0 && a.frames[1].opacity === 1 && a.timing.fill === 'backwards'));
    observer.enter(f.workflow);
    assert.equal(f.animations.length, 4);
    assert.equal(observer.observed.has(f.workflow), false);
});

test('mobile observes individual stages so below-fold cards do not run ahead', () => {
    const f = fixture({ compact: true });
    assert.equal(f.observers[0].observed.has(f.workflow), false);
    assert.ok(f.cards.every(card => f.observers[0].observed.has(card)));
    f.observers[0].enter(f.cards[0]);
    assert.equal(f.animations.length, 1);
    assert.equal(f.animations[0].target, f.cards[0]);
    assert.equal(f.cards[1].dataset.presentationStatus, undefined);
});

test('reduced motion starts static and creates no observer or animation', () => {
    const f = fixture({ reduced: true });
    assert.equal(f.page.dataset.presentationState, 'reduced');
    assert.equal(f.animations.length, 0);
    assert.equal(f.observers.length, 0);
    f.reduced.change(false);
    assert.equal(f.page.dataset.presentationState, 'ready');
    assert.equal(f.observers.length, 1);
});

test('changing motion preference cancels active movement without replaying a completed chapter', () => {
    const f = fixture();
    f.observers[0].enter(f.hero);
    f.reduced.change(true);
    assert.ok(f.animations.every(a => a.cancelled));
    assert.equal(f.page.dataset.presentationState, 'reduced');
    assert.equal(f.observers[0].disconnected, true);
    f.reduced.change(false);
    assert.equal(f.observers[1].observed.has(f.hero), false);
});

test('keyboard focus and a hidden tab immediately cancel movement', () => {
    const f = fixture();
    f.observers[0].enter(f.hero);
    f.page.events.get('focusin')();
    assert.ok(f.animations.every(a => a.cancelled));
    f.observers[0].enter(f.workflow);
    f.document.hidden = true;
    f.documentEvents.get('visibilitychange')();
    assert.ok(f.animations.every(a => a.cancelled));
});

test('page teardown disconnects observation and removes persistent listeners', () => {
    const f = fixture();
    f.observers[0].enter(f.hero);
    f.windowEvents.get('pagehide')();
    assert.equal(f.page.dataset.presentationState, 'disposed');
    assert.equal(f.observers[0].disconnected, true);
    assert.ok(f.animations.every(a => a.cancelled));
    assert.equal(f.documentEvents.size, 0);
    assert.equal(f.page.events.size, 0);
    assert.equal(f.reduced.listeners.size, 0);
});

test('unsupported or failed browser animation APIs leave a static page', () => {
    const unsupported = fixture({ unsupported: true });
    assert.equal(unsupported.page.dataset.presentationState, 'static');
    assert.equal(unsupported.observers.length, 0);
    const failedObserver = fixture({ throwObserver: true });
    assert.equal(failedObserver.page.dataset.presentationState, 'static');
    const failedAnimation = fixture({ throwAnimation: true });
    failedAnimation.observers[0].enter(failedAnimation.workflow);
    assert.equal(failedAnimation.page.dataset.presentationState, 'static');
    assert.equal(failedAnimation.animations.length, 0);
});

test('initialization is idempotent and unrelated pages are untouched', () => {
    const f = fixture();
    f.run();
    assert.equal(f.observers.length, 1);
    const other = fixture({ otherPage: true });
    assert.deepEqual(other.page.dataset, {});
    assert.equal(other.observers.length, 0);
});

test('changing breakpoints does not replay completed desktop workflow stages', () => {
    const f = fixture();
    f.observers[0].enter(f.workflow);
    f.compact.change(true);
    assert.ok(f.cards.every(card => !f.observers[1].observed.has(card)));
    assert.equal(f.animations.length, 4);
    assert.ok(f.animations.every(a => a.cancelled));
});

test('late callbacks from a disconnected observer cannot start an old sequence', () => {
    const f = fixture();
    const old = f.observers[0];
    f.compact.change(true);
    old.enter(f.hero);
    assert.equal(f.animations.length, 0);
    f.observers[1].enter(f.hero);
    assert.equal(f.animations.length, 2);
});


test('only offscreen targets wait for entry and all pending states release on focus', () => {
    const f = fixture();
    assert.ok(f.cards.every(card => card.dataset.presentationPending === ''));
    f.observers[0].enter(f.workflow);
    assert.ok(f.cards.every(card => card.dataset.presentationPending === undefined));
    f.page.events.get('focusin')();
    assert.equal(f.page.dataset.presentationState, 'static');
    assert.ok([...f.hero.children.values()].flat().every(element => element.dataset.presentationPending === undefined));
    const visible = fixture({ inView: true });
    assert.ok(visible.cards.every(card => card.dataset.presentationPending === undefined));
});

test('photo has a bounded rectangular wipe and reduced motion releases waiting cards', () => {
    const f = fixture();
    f.observers[0].enter(f.hero);
    const photo = f.animations.find(animation => animation.target.name === 'photo');
    assert.equal(photo.frames[0].clipPath, 'inset(0 0 14% 0)');
    assert.equal(photo.frames[1].clipPath, 'inset(0)');
    f.reduced.change(true);
    assert.ok(f.cards.every(card => card.dataset.presentationPending === undefined));
});

test('observer or animation failure releases every pending target', () => {
    for (const options of [{ throwObserver: true }, { throwAnimation: true }]) {
        const f = fixture(options);
        if (f.observers.length) f.observers[0].enter(f.workflow);
        assert.equal(f.page.dataset.presentationState, 'static');
        assert.ok(f.cards.every(card => card.dataset.presentationPending === undefined));
    }
});


test('installed-product chapter waits for entry and reveals its photo and explanation once', () => {
    const f = fixture();
    assert.ok(f.observers[0].observed.has(f.deployment));
    assert.equal(f.animations.length, 0);
    f.observers[0].enter(f.deployment);
    assert.deepEqual(f.animations.map(a => a.target.name), ['installed-camera', 'installation-copy']);
    assert.equal(f.animations[0].frames[0].opacity, 0);
    assert.equal(f.animations[1].timing.delay, 80);
    assert.ok([...f.deployment.children.values()].flat().every(e => e.dataset.presentationPending === undefined));
    f.observers[0].enter(f.deployment);
    assert.equal(f.animations.length, 2);
});

test('pagehide releases pending content and blocks late observer callbacks', () => {
    const f = fixture();
    const old = f.observers[0];
    assert.ok(f.cards.every(card => card.dataset.presentationPending === ''));
    f.windowEvents.get('pagehide')();
    old.enter(f.workflow);
    assert.equal(f.page.dataset.presentationState, 'disposed');
    assert.equal(f.animations.length, 0);
    assert.ok(f.cards.every(card => card.dataset.presentationPending === undefined));
    assert.equal(old.disconnected, true);
});

test('keyboard focus suppresses a later breakpoint restart and keeps cards readable', () => {
    const f = fixture({ compact: true });
    const old = f.observers[0];
    f.page.events.get('focusin')();
    f.compact.change(false);
    old.enter(f.cards[0]);
    assert.equal(f.page.dataset.presentationState, 'static');
    assert.equal(f.observers.length, 1);
    assert.equal(f.animations.length, 0);
    assert.ok(f.cards.every(card => card.dataset.presentationPending === undefined));
});
