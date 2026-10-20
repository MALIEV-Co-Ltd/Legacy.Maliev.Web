(() => {
    'use strict';
    const root = document.querySelector('[data-home-hero-carousel]');
    if (!root || root.dataset.carouselReady) return;
    const panels = [...root.querySelectorAll('[data-hero-scene]')];
    if (panels.length !== 3) return;
    const controls = root.querySelector('[data-hero-controls]');
    const toggle = root.querySelector('[data-hero-toggle]');
    // Never start automatic motion without its persistent user stop mechanism.
    if (!controls || !toggle) return;
    const motion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const pauses = new Set(motion.matches ? ['motion'] : []);
    if (document.hidden) pauses.add('hidden');
    let index = 0, timer = null, revision = 0, destroyed = false, transitionPending = false;
    let releaseImageWait = null;
    let animations = [];
    const listeners = [];
    const listen = (node, event, handler) => {
        node.addEventListener(event, handler);
        listeners.push(() => node.removeEventListener(event, handler));
    };
    const clearTimer = () => { if (timer !== null) window.clearTimeout(timer); timer = null; };
    const schedule = () => {
        clearTimer();
        if (!destroyed && !transitionPending && pauses.size === 0) timer = window.setTimeout(() => { timer = null; go(index + 1); }, 8000);
    };
    const settle = () => {
        revision++;
        transitionPending = false;
        const release = releaseImageWait;
        releaseImageWait = null;
        release?.();
        animations.forEach(animation => { try { animation.cancel(); } catch (_) { /* Static fallback. */ } });
        animations = [];
        panels.forEach((panel, i) => {
            panel.hidden = i !== index;
            panel.inert = i !== index;
            panel.setAttribute('aria-hidden', String(i !== index));
        });
    };
    const animate = (panel, entering) => {
        const jobs = [];
        const created = [];
        const token = revision;
        if (motion.matches) return Promise.resolve();
        const run = (selector, frames, duration, delay = 0) => {
            panel.querySelectorAll(selector).forEach((element, i) => {
                if (typeof element.animate !== 'function') return;
                try {
                    const animation = element.animate(frames, { duration, delay: delay + i * (entering ? 65 : 25), easing: 'cubic-bezier(0.16, 1, 0.3, 1)', fill: 'both' });
                    animations.push(animation);
                    created.push(animation);
                    jobs.push(animation.finished.catch(() => {}));
                } catch (_) { /* Unsupported effects leave content visible. */ }
            });
        };
        const type = panel.dataset.heroScene;
        const text = 'h1, h2, .hero-option-copy > p:not(.hero-material-summary), .landing-actions';
        run(text, entering ? [{ opacity: 0, clipPath: 'inset(0 0 100% 0)', transform: 'translateY(16px)' }, { opacity: 1, clipPath: 'inset(0)', transform: 'none' }] : [{ opacity: 1, transform: 'none' }, { opacity: 0, transform: 'translateY(-10px)' }], entering ? 480 : 180);
        if (type === 'assembly') {
            run('.hero-assembly-annotated > img', entering ? [{ opacity: 0, transform: 'scale(.93) translateY(12px)' }, { opacity: 1, transform: 'none' }] : [{ opacity: 1, transform: 'none' }, { opacity: 0, transform: 'scale(.97)' }], entering ? 600 : 240);
            if (entering) {
                ['fdm', 'resin', 'nylon', 'cnc'].forEach((key, i) => {
                    const delay = 650 + i * 360;
                    run(`[data-tech-leader="${key}"]`, [{ opacity: 0, strokeDasharray: '1', strokeDashoffset: 1 }, { opacity: 1, strokeDasharray: '1', strokeDashoffset: 0 }], 280, delay);
                    run(`.hero-tech-${key}`, [{ opacity: 0 }, { opacity: 1 }], 200, delay + 120);
                    run(`[data-tech-point="${key}"]`, [{ opacity: 0 }, { opacity: 1 }], 100, delay + 220);
                });
                run('.hero-material-summary', [{ opacity: 0 }, { opacity: 1 }], 240, 2100);
                const image = panel.querySelector('.hero-assembly-annotated > img');
                if (image && !image.complete && typeof image.decode === 'function') {
                    created.forEach(animation => { try { animation.pause(); } catch (_) { /* Static fallback. */ } });
                    let release;
                    const imageReady = new Promise(resolve => {
                        release = () => {
                            if (releaseImageWait === release) releaseImageWait = null;
                            resolve();
                        };
                    });
                    releaseImageWait = release;
                    jobs.push(imageReady);
                    const resume = () => {
                        if (destroyed || token !== revision) return;
                        created.forEach(animation => { try { animation.play(); } catch (_) { /* Cancelled effects stay settled. */ } });
                        release();
                    };
                    try { Promise.resolve(image.decode()).then(resume, resume); }
                    catch (_) { resume(); }
                }
            } else {
                run('.hero-tech-leaders, .hero-tech-callouts li, .hero-material-summary', [{ opacity: 1 }, { opacity: 0 }], 120);
            }
        } else if (type === 'partnership') {
            run('.hero-partnership-media img', entering ? [{ clipPath: 'inset(0 0 0 100%)' }, { clipPath: 'inset(0)' }] : [{ clipPath: 'inset(0)' }, { clipPath: 'inset(0 100% 0 0)' }], entering ? 700 : 260);
        } else {
            run('.hero-journey-images img', entering ? [{ opacity: 0, clipPath: 'inset(100% 0 0 0)', transform: 'rotate(-1.5deg)' }, { opacity: 1, clipPath: 'inset(0)', transform: 'none' }] : [{ opacity: 1, clipPath: 'inset(0)' }, { opacity: 0, clipPath: 'inset(0 0 100% 0)' }], entering ? 500 : 160, entering ? 80 : 0);
        }
        return Promise.all(jobs);
    };
    async function go(target) {
        if (destroyed) return;
        const destination = (target + panels.length) % panels.length;
        if (destination === index) { schedule(); return; }
        clearTimer();
        settle();
        transitionPending = true;
        const from = panels[index];
        index = destination;
        const token = ++revision;
        // Remove outgoing links from keyboard/AT navigation before its short exit.
        from.inert = true;
        from.setAttribute('aria-hidden', 'true');
        await animate(from, false);
        if (destroyed || token !== revision) return;
        from.hidden = true;
        const to = panels[index];
        to.hidden = false;
        to.inert = false;
        to.setAttribute('aria-hidden', 'false');
        await animate(to, true);
        if (destroyed || token !== revision) return;
        settle();
        schedule();
    }
    const renderControl = () => {
        const userPaused = pauses.has('user');
        toggle.textContent = motion.matches ? root.dataset.motionPausedLabel
            : userPaused ? root.dataset.playLabel : root.dataset.pauseLabel;
        toggle.disabled = motion.matches;
        controls.hidden = false;
        root.dataset.carouselUserPaused = String(userPaused);
    };
    const pause = (reason, enabled, finish = false) => {
        if (enabled) pauses.add(reason); else pauses.delete(reason);
        if (finish) settle();
        renderControl();
        schedule();
    };
    listen(toggle, 'click', () => { if (!motion.matches) pause('user', !pauses.has('user'), true); });
    listen(root, 'pointerenter', () => pause('hover', true));
    listen(root, 'pointerleave', () => pause('hover', false));
    listen(root, 'focusin', () => pause('focus', true, true));
    listen(root, 'focusout', event => { if (!root.contains(event.relatedTarget)) pause('focus', false); });
    listen(document, 'visibilitychange', () => pause('hidden', document.hidden, document.hidden));
    const onMotion = () => pause('motion', motion.matches, true);
    motion.addEventListener?.('change', onMotion);
    let observer = null;
    if (typeof window.IntersectionObserver === 'function') {
        try {
            observer = new window.IntersectionObserver(entries => {
                if (destroyed) return;
                const entry = entries.find(item => item.target === root);
                if (entry) pause('offscreen', !entry.isIntersecting, !entry.isIntersecting);
            }, { threshold: 0 });
            observer.observe(root);
        } catch (_) { observer?.disconnect(); observer = null; }
    }
    const destroy = () => {
        destroyed = true; clearTimer(); settle(); observer?.disconnect();
        motion.removeEventListener?.('change', onMotion);
        listeners.forEach(remove => remove());
        toggle.disabled = true;
        root.dataset.carouselReady = 'stopped';
    };
    listen(window, 'pagehide', event => {
        if (event.persisted) pause('hidden', true, true); else destroy();
    });
    listen(window, 'pageshow', event => pause('hidden', document.hidden, !!event.persisted));
    root.dataset.carouselReady = 'true';
    renderControl();
    settle();
    if (!document.hidden) {
        transitionPending = true;
        const token = revision;
        animate(panels[index], true).then(() => {
            if (destroyed || token !== revision) return;
            settle(); schedule();
        });
    }
    schedule();
})();
