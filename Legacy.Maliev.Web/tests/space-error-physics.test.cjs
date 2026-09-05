const test = require('node:test');
const assert = require('node:assert/strict');
const physics = require('../wwwroot/src/app/js/space-error-physics.js');

const desktop = { width: 1440, height: 900, spriteWidth: 240, spriteHeight: 300 };
const mobile = { width: 390, height: 844, spriteWidth: 120, spriteHeight: 150 };

function createState(overrides = {}) {
    return { x: 720, y: 450, vx: 0, vy: 0, angle: 0, spin: 0, ...overrides };
}

function limits(viewport, angle = 0) {
    return physics.bounds(viewport.width, viewport.height, viewport.spriteWidth, viewport.spriteHeight, angle);
}

function assertContained(state, viewport) {
    for (const property of ['x', 'y', 'vx', 'vy', 'angle', 'spin']) {
        assert.ok(Number.isFinite(state[property]), `${property} must remain finite`);
    }
    assert.ok(state.angle >= 0 && state.angle < 360, 'rotation stays normalized');
    const boundary = limits(viewport, state.angle);
    assert.ok(state.x >= boundary.minX - 0.001 && state.x <= boundary.maxX + 0.001, 'horizontal position stays in bounds');
    assert.ok(state.y >= boundary.minY - 0.001 && state.y <= boundary.maxY + 0.001, 'vertical position stays in bounds');
}

test('bounds keep all four rotated sprite corners inside the viewport margin', () => {
    for (const angle of [0, 30, 45, 90, 179, 270, 359]) {
        const boundary = limits(desktop, angle);
        const radians = angle * Math.PI / 180;
        for (const x of [boundary.minX, boundary.maxX]) {
            for (const y of [boundary.minY, boundary.maxY]) {
                for (const dx of [-desktop.spriteWidth / 2, desktop.spriteWidth / 2]) {
                    for (const dy of [-desktop.spriteHeight / 2, desktop.spriteHeight / 2]) {
                        const cornerX = x + dx * Math.cos(radians) - dy * Math.sin(radians);
                        const cornerY = y + dx * Math.sin(radians) + dy * Math.cos(radians);
                        assert.ok(cornerX >= 8 - 0.001 && cornerX <= desktop.width - 8 + 0.001);
                        assert.ok(cornerY >= 8 - 0.001 && cornerY <= desktop.height - 8 + 0.001);
                    }
                }
            }
        }
    }
});

test('viewports too small for the sprite produce finite bounds at their center', () => {
    for (const [width, height] of [[0, 0], [1, 1], [50, 60]]) {
        const boundary = physics.bounds(width, height, 240, 300, 45);
        assert.deepEqual(boundary, { minX: width / 2, maxX: width / 2, minY: height / 2, maxY: height / 2 });
    }
});

test('constrain brings an existing desktop position into a resized mobile viewport', () => {
    const state = createState({ x: 1300, y: -40, angle: 37 });
    physics.constrain(state, limits(mobile, state.angle));
    assertContained(state, mobile);
    assert.equal(state.x, limits(mobile, state.angle).maxX);
    assert.equal(state.y, limits(mobile, state.angle).minY);
});

test('release velocity follows the drag direction and a faster gesture throws harder', () => {
    const slow = physics.sampleVelocity([{ x: 0, y: 0, time: 0 }, { x: 60, y: -30, time: 100 }], 100);
    const fast = physics.sampleVelocity([{ x: 0, y: 0, time: 0 }, { x: 60, y: -30, time: 50 }], 50);
    assert.ok(slow.vx > 0 && slow.vy < 0);
    assert.ok(fast.vx > slow.vx * 1.5);
    assert.ok(Math.abs(fast.vy) > Math.abs(slow.vy) * 1.5);
});

test('release velocity uses the recent gesture rather than an old movement', () => {
    const velocity = physics.sampleVelocity([
        { x: 10000, y: 10000, time: 0 },
        { x: 200, y: 200, time: 200 },
        { x: 240, y: 220, time: 250 },
        { x: 280, y: 240, time: 300 }
    ], 300);
    assert.ok(velocity.vx > 0 && velocity.vy > 0);
    assert.ok(Math.abs(velocity.vx / velocity.vy - 2) < 0.01);
});

test('holding still before release and insufficient samples cannot create a throw', () => {
    for (const samples of [
        [],
        [{ x: 40, y: 50, time: 300 }],
        [{ x: 0, y: 0, time: 0 }, { x: 80, y: 80, time: 100 }],
        [{ x: 0, y: 0, time: 0 }, { x: 80, y: 80, time: 300 }],
        [{ x: 0, y: 0, time: 300 }, { x: 80, y: 80, time: 300 }]
    ]) {
        assert.deepEqual(physics.sampleVelocity(samples, 300), { vx: 0, vy: 0 });
    }
});

test('a very fast diagonal throw is capped by total speed while preserving direction', () => {
    const samples = [{ x: 0, y: 0, time: 0 }, { x: -1000, y: 1000, time: 1 }];
    for (const cap of [500, 1800]) {
        const velocity = physics.sampleVelocity(samples, 1, cap);
        assert.ok(velocity.vx < 0 && velocity.vy > 0);
        assert.ok(Math.hypot(velocity.vx, velocity.vy) <= cap + 0.001);
        assert.ok(Math.hypot(velocity.vx, velocity.vy) >= cap - 0.001);
        assert.ok(Math.abs(velocity.vx + velocity.vy) < 0.001);
    }
});

for (const wall of ['left', 'right', 'top', 'bottom']) {
    test(`a throw rebounds inward from the ${wall} wall and retains momentum`, () => {
        const boundary = limits(desktop);
        const horizontal = wall === 'left' || wall === 'right';
        const negative = wall === 'left' || wall === 'top';
        const axis = horizontal ? 'x' : 'y';
        const velocity = horizontal ? 'vx' : 'vy';
        const edge = boundary[`${negative ? 'min' : 'max'}${horizontal ? 'X' : 'Y'}`];
        const state = createState({ [axis]: edge + (negative ? 1 : -1), [velocity]: negative ? -500 : 500 });
        physics.advance(state, desktop, 0.1);
        assertContained(state, desktop);
        assert.ok(negative ? state[velocity] > 0 : state[velocity] < 0, 'velocity points inward after the bounce');
        assert.ok(Math.abs(state[velocity]) > 250 && Math.abs(state[velocity]) < 450, 'bounce loses energy without stopping');
        assert.ok(Math.abs(state[axis] - edge) > 10, 'overshoot carries the sprite back into the screen');
    });
}

test('free flight continues moving and spinning while gradually losing energy', () => {
    const state = createState({ vx: 50, vy: -25, spin: 20, angle: 10 });
    physics.advance(state, desktop, 0.5);
    assert.ok(state.x > 720 && state.y < 450);
    assert.ok(state.vx > 0 && state.vx < 50);
    assert.ok(state.vy < 0 && state.vy > -25);
    assert.ok(state.spin > 0 && state.spin < 20);
    assert.ok(state.angle > 10);
    assertContained(state, desktop);
});

test('repeated fast rotating throws remain on screen on desktop and mobile', () => {
    for (const viewport of [desktop, mobile]) {
        const state = createState({ x: viewport.width / 2, y: viewport.height / 2 });
        for (let throwIndex = 0; throwIndex < 8; throwIndex++) {
            state.vx = throwIndex % 2 ? -1700 : 1700;
            state.vy = throwIndex % 3 ? 1300 : -1300;
            state.spin = throwIndex % 2 ? -180 : 180;
            for (let frame = 0; frame < 120; frame++) {
                physics.advance(state, viewport, frame === 60 ? 1.5 : 1 / 30);
                assertContained(state, viewport);
            }
        }
    }
});

test('the same throw has approximately the same outcome across frame rates', () => {
    const initial = { x: 600, y: 400, vx: 900, vy: -500, angle: 350, spin: 70 };
    const outcomes = [30, 60, 120].map(frameRate => {
        const state = createState(initial);
        for (let frame = 0; frame < frameRate * 3; frame++) {
            physics.advance(state, desktop, 1 / frameRate);
        }
        assertContained(state, desktop);
        return state;
    });
    for (const state of outcomes.slice(1)) {
        for (const property of ['x', 'y', 'vx', 'vy', 'angle', 'spin']) {
            assert.ok(Math.abs(state[property] - outcomes[0][property]) < 2, `${property} should not depend materially on frame rate`);
        }
    }
});
