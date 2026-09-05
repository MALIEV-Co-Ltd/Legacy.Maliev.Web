(function (root, factory) {
    'use strict';
    if (typeof module === 'object' && module.exports) module.exports = factory();
    else root.SpaceErrorPhysics = factory();
}(typeof globalThis !== 'undefined' ? globalThis : this, function () {
    'use strict';

    function bounds(width, height, spriteWidth, spriteHeight, angle) {
        var radians = angle * Math.PI / 180;
        var cosine = Math.abs(Math.cos(radians));
        var sine = Math.abs(Math.sin(radians));
        var halfWidth = Math.min(width / 2, (spriteWidth * cosine + spriteHeight * sine) / 2 + 8);
        var halfHeight = Math.min(height / 2, (spriteWidth * sine + spriteHeight * cosine) / 2 + 8);
        return { minX: halfWidth, maxX: width - halfWidth, minY: halfHeight, maxY: height - halfHeight };
    }

    function constrain(state, edges) {
        state.x = Math.max(edges.minX, Math.min(edges.maxX, state.x));
        state.y = Math.max(edges.minY, Math.min(edges.maxY, state.y));
    }

    function reflect(position, velocity, min, max) {
        if (max <= min) return { position: min, velocity: 0 };
        // Substeps normally need only one reflection; preserve overshoot at fast throws.
        for (var bounce = 0; bounce < 8 && (position < min || position > max); bounce++) {
            if (position < min) {
                position = min + (min - position);
                if (velocity < 0) velocity = -velocity * .82;
            } else {
                position = max - (position - max);
                if (velocity > 0) velocity = -velocity * .82;
            }
        }
        return { position: Math.max(min, Math.min(max, position)), velocity: velocity };
    }

    function advance(state, viewport, seconds) {
        var remaining = Math.max(0, Math.min(seconds, .1));
        while (remaining > .000001) {
            var dt = Math.min(remaining, 1 / 120);
            var decay = Math.exp(-.09 * dt);
            var travel = (1 - decay) / .09;
            state.x += state.vx * travel;
            state.y += state.vy * travel;
            state.vx *= decay;
            state.vy *= decay;
            state.angle = ((state.angle + state.spin * dt) % 360 + 360) % 360;
            state.spin *= Math.exp(-.16 * dt);
            var edges = bounds(viewport.width, viewport.height, viewport.spriteWidth, viewport.spriteHeight, state.angle);
            var horizontal = reflect(state.x, state.vx, edges.minX, edges.maxX);
            var vertical = reflect(state.y, state.vy, edges.minY, edges.maxY);
            state.x = horizontal.position;
            state.vx = horizontal.velocity;
            state.y = vertical.position;
            state.vy = vertical.velocity;
            remaining -= dt;
        }
        return state;
    }

    function sampleVelocity(samples, now, maxSpeed) {
        maxSpeed = maxSpeed || 1800;
        if (samples.length < 2) return { vx: 0, vy: 0 };
        var last = samples[samples.length - 1];
        if (now - last.time > 120) return { vx: 0, vy: 0 };
        var first = last;
        for (var i = samples.length - 2; i >= 0; i--) {
            if (last.time - samples[i].time > 120) break;
            first = samples[i];
        }
        var elapsed = (last.time - first.time) / 1000;
        if (elapsed <= 0) return { vx: 0, vy: 0 };
        var vx = (last.x - first.x) / elapsed;
        var vy = (last.y - first.y) / elapsed;
        var speed = Math.hypot(vx, vy);
        var scale = speed > maxSpeed ? maxSpeed / speed : 1;
        return { vx: vx * scale, vy: vy * scale };
    }

    return { bounds: bounds, constrain: constrain, advance: advance, sampleVelocity: sampleVelocity };
}));
