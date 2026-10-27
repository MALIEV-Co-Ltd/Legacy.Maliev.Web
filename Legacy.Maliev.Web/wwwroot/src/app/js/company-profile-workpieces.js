(() => {
    'use strict';
    const root = document.querySelector('[data-workpiece-gallery]');
    if (!root || root.dataset.workpieceReady) return;
    const photos = [...root.querySelectorAll('[data-workpiece-photo]')];
    const choices = [...root.querySelectorAll('[data-workpiece-index]')];
    const controls = root.querySelector('[data-workpiece-controls]');
    const toggle = root.querySelector('[data-workpiece-toggle]');
    if (photos.length < 2 || choices.length !== photos.length || !controls || !toggle) return;
    const motion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const pauses = new Set(motion.matches ? ['motion'] : []);
    if (document.hidden) pauses.add('hidden');
    let index = 0, timer = null, revision = 0, animation = null, destroyed = false, pointerState = null;
    const listeners = [];
    const listen = (node, event, handler) => { node.addEventListener(event, handler); listeners.push(() => node.removeEventListener(event, handler)); };
    const clear = () => { if (timer !== null) window.clearTimeout(timer); timer = null; };
    const update = () => {
        choices.forEach((choice, i) => choice.setAttribute('aria-pressed', String(i === index)));
        const stopped = pauses.has('user') || pauses.has('focus') || pauses.has('motion');
        toggle.setAttribute('aria-label', stopped ? root.dataset.playLabel : root.dataset.pauseLabel);
        toggle.dataset.paused = String(stopped);
        toggle.disabled = motion.matches;
    };
    const schedule = () => {
        clear(); update();
        if (!destroyed && !pauses.size) timer = window.setTimeout(() => { timer = null; go(index + 1); }, 6000);
    };
    const settle = () => {
        revision++;
        try { animation?.cancel(); } catch (_) { /* Leave a static photo. */ }
        animation = null;
        photos.forEach((photo, i) => { photo.hidden = i !== index; photo.setAttribute('aria-hidden', String(i !== index)); photo.style.zIndex = ''; });
    };
    async function go(target) {
        if (destroyed) return;
        const destination = (target + photos.length) % photos.length;
        if (destination === index) { schedule(); return; }
        clear(); settle();
        const from = photos[index];
        index = destination;
        const token = ++revision;
        const to = photos[index];
        to.hidden = false; to.setAttribute('aria-hidden', 'false');
        from.setAttribute('aria-hidden', 'true');
        from.style.zIndex = '1'; to.style.zIndex = '2';
        update();
        if (!motion.matches && typeof to.animate === 'function') {
            try {
                // Incoming opacity over the opaque outgoing photo creates a true crossfade without a white gap.
                animation = to.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 650, easing: 'ease-in-out', fill: 'both' });
                await animation.finished.catch(() => {});
            } catch (_) { /* Unsupported animation falls back to the next photo. */ }
        }
        if (destroyed || revision !== token) return;
        settle(); schedule();
    }
    const pause = (reason, enabled, finish = false) => { if (enabled) pauses.add(reason); else pauses.delete(reason); if (finish) settle(); schedule(); };
    choices.forEach((choice, i) => listen(choice, 'click', () => go(i)));
    listen(toggle, 'pointerdown', () => { pointerState = pauses.has('user') || pauses.has('focus') || pauses.has('motion'); });
    listen(toggle, 'click', () => {
        const stopped = pointerState ?? (pauses.has('user') || pauses.has('focus') || pauses.has('motion'));
        pointerState = null;
        if (motion.matches) return;
        pauses.delete('focus'); pause('user', !stopped);
    });
    listen(root, 'pointerenter', () => pause('hover', true));
    listen(root, 'pointerleave', () => pause('hover', false));
    listen(root, 'focusin', () => pause('focus', true, true));
    listen(document, 'visibilitychange', () => pause('hidden', document.hidden, document.hidden));
    const onMotion = () => pause('motion', motion.matches, true);
    motion.addEventListener?.('change', onMotion);
    let observer = null;
    if (typeof window.IntersectionObserver === 'function') {
        try {
            observer = new window.IntersectionObserver(entries => {
                if (destroyed) return;
                const entry = entries.find(item => item.target === root);
                if (!entry) return;
                if (entry.isIntersecting) photos.forEach(photo => { photo.loading = 'eager'; });
                pause('offscreen', !entry.isIntersecting, !entry.isIntersecting);
            }, { threshold: 0 });
            observer.observe(root);
        } catch (_) { observer?.disconnect(); observer = null; }
    }
    const destroy = () => {
        destroyed = true; clear(); settle(); observer?.disconnect(); motion.removeEventListener?.('change', onMotion);
        listeners.forEach(remove => remove()); controls.hidden = true; root.dataset.workpieceReady = 'stopped';
    };
    listen(window, 'pagehide', event => { if (event.persisted) pause('hidden', true, true); else destroy(); });
    listen(window, 'pageshow', () => pause('hidden', document.hidden, true));
    root.dataset.workpieceReady = 'true'; controls.hidden = false; settle(); schedule();
})();
