(function () {
    'use strict';

    var astronaut = document.querySelector('.space-error__astronaut');
    var scene = document.querySelector('.space-error__scene');
    var physics = window.SpaceErrorPhysics;
    if (!astronaut || !scene || !physics || !window.PointerEvent) return;

    var reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    var state = { x: 0, y: 0, vx: 10, vy: -5, angle: -24, spin: 4 };
    var viewport;
    var frame = null;
    var lastTime = 0;
    var pointer = null;
    var samples = [];
    var grab = { x: 0, y: 0 };
    var thrown = false;
    var steeringTime = 0;
    var target = { vx: 10, vy: -5, spin: 4 };
    var started = false;

    function edges() {
        return physics.bounds(viewport.width, viewport.height, viewport.spriteWidth, viewport.spriteHeight, state.angle);
    }

    function paint() {
        astronaut.style.transform = 'translate(' + state.x + 'px,' + state.y + 'px) translate(-50%,-50%) rotate(' + state.angle + 'deg)';
        astronaut.dataset.motion = pointer !== null ? 'dragging' : reducedMotion.matches ? 'still' : thrown ? 'thrown' : 'floating';
    }

    function stopFrame() {
        if (frame !== null) window.cancelAnimationFrame(frame);
        frame = null;
        lastTime = 0;
    }

    function startFrame() {
        if (!started || frame !== null || reducedMotion.matches || document.hidden || pointer !== null) return;
        frame = window.requestAnimationFrame(tick);
    }

    function tick(time) {
        frame = null;
        var dt = lastTime ? Math.min((time - lastTime) / 1000, .05) : 0;
        lastTime = time;
        if (!thrown) {
            steeringTime -= dt;
            if (steeringTime <= 0) {
                var direction = Math.random() * Math.PI * 2;
                var speed = 10 + Math.random() * 10;
                target = { vx: Math.cos(direction) * speed, vy: Math.sin(direction) * speed, spin: (Math.random() > .5 ? 1 : -1) * (2 + Math.random() * 3) };
                steeringTime = 8 + Math.random() * 10;
            }
            var blend = 1 - Math.exp(-dt * .35);
            state.vx += (target.vx - state.vx) * blend;
            state.vy += (target.vy - state.vy) * blend;
            state.spin += (target.spin - state.spin) * blend;
        }
        physics.advance(state, viewport, dt);
        if (thrown && Math.hypot(state.vx, state.vy) < 14) {
            thrown = false;
            steeringTime = 0;
        }
        paint();
        startFrame();
    }

    function sample(event) {
        samples.push({ x: event.clientX, y: event.clientY, time: event.timeStamp });
        while (samples.length > 2 && event.timeStamp - samples[0].time > 150) samples.shift();
    }

    function release(event, cancelled) {
        if (pointer === null || (event && event.pointerId !== pointer)) return;
        var captured = pointer;
        pointer = null;
        astronaut.classList.remove('is-dragging');
        var velocity = !cancelled && !reducedMotion.matches ? physics.sampleVelocity(samples, event.timeStamp) : { vx: 0, vy: 0 };
        state.vx = velocity.vx;
        state.vy = velocity.vy;
        // Off-center grabs impart angular momentum; centered throws still tumble gently.
        var torque = (grab.x * state.vy - grab.y * state.vx) / 700;
        state.spin = reducedMotion.matches || cancelled ? 0 : Math.max(-180, Math.min(180, torque + state.vx * .025));
        thrown = Math.hypot(state.vx, state.vy) >= 14;
        samples = [];
        if (astronaut.hasPointerCapture(captured)) astronaut.releasePointerCapture(captured);
        paint();
        startFrame();
    }

    function resize() {
        viewport = { width: window.innerWidth, height: window.innerHeight, spriteWidth: astronaut.offsetWidth, spriteHeight: astronaut.offsetHeight };
        if (started) {
            release(null, true);
            physics.constrain(state, edges());
            paint();
        }
    }

    function initialize() {
        if (started) return;
        var initial = astronaut.getBoundingClientRect();
        state.x = initial.left + initial.width / 2;
        state.y = initial.top + initial.height / 2;
        astronaut.classList.add('is-interactive');
        astronaut.setAttribute('tabindex', '0');
        astronaut.setAttribute('role', 'img');
        astronaut.setAttribute('aria-label', astronaut.dataset.instructions);
        scene.removeAttribute('aria-hidden');
        resize();
        started = true;
        physics.constrain(state, edges());
        paint();
        startFrame();
    }

    astronaut.addEventListener('pointerdown', function (event) {
        if (!started || pointer !== null || event.isPrimary === false || (event.pointerType === 'mouse' && event.button !== 0)) return;
        event.preventDefault();
        stopFrame();
        pointer = event.pointerId;
        grab = { x: event.clientX - state.x, y: event.clientY - state.y };
        state.vx = state.vy = state.spin = 0;
        samples = [];
        sample(event);
        astronaut.setPointerCapture(pointer);
        astronaut.classList.add('is-dragging');
        paint();
    });

    astronaut.addEventListener('pointermove', function (event) {
        if (event.pointerId !== pointer) return;
        event.preventDefault();
        state.x = event.clientX - grab.x;
        state.y = event.clientY - grab.y;
        physics.constrain(state, edges());
        sample(event);
        paint();
    });
    astronaut.addEventListener('pointerup', function (event) { release(event, false); });
    astronaut.addEventListener('pointercancel', function (event) { release(event, true); });
    astronaut.addEventListener('lostpointercapture', function (event) { release(event, true); });
    astronaut.addEventListener('dragstart', function (event) { event.preventDefault(); });

    astronaut.addEventListener('keydown', function (event) {
        var directions = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
        var direction = directions[event.key];
        if (!direction || pointer !== null) return;
        event.preventDefault();
        var distance = event.shiftKey ? 48 : 24;
        state.x += direction[0] * distance;
        state.y += direction[1] * distance;
        state.vx = reducedMotion.matches ? 0 : direction[0] * (event.shiftKey ? 280 : 140);
        state.vy = reducedMotion.matches ? 0 : direction[1] * (event.shiftKey ? 280 : 140);
        thrown = !reducedMotion.matches;
        physics.constrain(state, edges());
        paint();
        startFrame();
    });

    reducedMotion.addEventListener('change', function () {
        if (!started) return;
        stopFrame();
        release(null, true);
        state.vx = state.vy = state.spin = 0;
        thrown = false;
        paint();
        startFrame();
    });
    document.addEventListener('visibilitychange', function () {
        if (document.hidden) { release(null, true); stopFrame(); }
        else startFrame();
    });
    window.addEventListener('pagehide', function () { release(null, true); stopFrame(); });
    window.addEventListener('pageshow', startFrame);
    window.addEventListener('resize', resize);
    if (astronaut.complete && astronaut.naturalWidth) initialize();
    else astronaut.addEventListener('load', initialize, { once: true });
}());
