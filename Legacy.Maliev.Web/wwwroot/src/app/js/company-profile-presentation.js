(() => {
    'use strict';

    const page = document.querySelector('.profile-about');
    if (!page || page.dataset.presentationState) return;

    // Progressive enhancement: only successfully observed offscreen content waits for entry.
    page.dataset.presentationState = 'static';
    if (typeof IntersectionObserver !== 'function'
        || typeof Element.prototype.animate !== 'function'
        || typeof window.matchMedia !== 'function') return;

    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
    const compact = window.matchMedia('(max-width: 767px)');
    const active = new Set();
    const pending = new Set();
    const played = new WeakSet();
    const chapters = new Map();
    let observer;
    let disposed = false;
    let focused = false;
    const ease = 'cubic-bezier(0.16, 1, 0.3, 1)';
    const all = (selector, scope = page) => Array.from(scope.querySelectorAll(selector));

    const cancelActive = () => {
        active.forEach(animation => animation.cancel());
        active.clear();
    };

    const release = element => {
        element.removeAttribute('data-presentation-pending');
        pending.delete(element);
    };
    const clearPending = () => { pending.forEach(release); };
    const targets = (chapter, kind) => {
        const selectors = {
            hero: '.landing-hero-copy, .profile-partnership-photo',
            workflow: ':scope > article',
            story: '.profile-story-media, .profile-story-grid > div',
            collaboration: '.profile-collaboration-grid > article',
            customers: '.landing-section-heading, .profile-customer-grid',
            close: '.landing-cta > div, .landing-cta > img'
        };
        return selectors[kind] ? all(selectors[kind], chapter) : [chapter];
    };

    const stop = (state) => {
        if (observer) observer.disconnect();
        cancelActive();
        clearPending();
        page.dataset.presentationState = state;
    };

    const animate = (element, transform, delay = 0, duration = 500, workflow = false, photo = false) => {
        if (!element || disposed || reduced.matches || page.dataset.presentationState !== 'ready') return;
        try {
            release(element);
            const start = { opacity: 0, transform };
            const end = { opacity: 1, transform: 'none' };
            if (photo) {
                start.clipPath = 'inset(0 0 14% 0)';
                end.clipPath = 'inset(0)';
            }
            // The blue registration edge passes from sketch to CAD to prototype to production.
            if (workflow) {
                start.borderColor = '#075de8';
                end.borderColor = '#dce4ee';
            }
            const animation = element.animate([start, end], { duration, delay, easing: ease, fill: 'backwards' });
            active.add(animation);
            animation.finished.then(() => active.delete(animation), () => active.delete(animation));
        } catch (_) {
            stop('static');
        }
    };

    const play = (chapter, kind) => {
        if (played.has(chapter) || reduced.matches || disposed) return;
        played.add(chapter);
        chapter.dataset.presentationStatus = 'played';
        if (kind === 'hero') {
            animate(chapter.querySelector('.landing-hero-copy'), 'translate3d(0, 28px, 0)', 0, 560);
            animate(chapter.querySelector('.profile-partnership-photo'), 'translate3d(28px, 0, 0)', 60, 640, false, true);
        } else if (kind === 'workflow') {
            all(':scope > article', chapter).forEach((card, index) => {
                if (played.has(card)) return;
                played.add(card);
                card.dataset.presentationStatus = 'played';
                animate(card, 'translate3d(0, 32px, 0)', index * 100, 500, true);
            });
        } else if (kind === 'step') {
            animate(chapter, 'translate3d(0, 28px, 0)', 0, 500, true);
            const workflow = page.querySelector('.profile-rapid-workflow-grid');
            if (workflow && all(':scope > article', workflow).every(card => played.has(card))) {
                played.add(workflow);
                workflow.dataset.presentationStatus = 'played';
            }
        } else if (kind === 'story') {
            const reverse = chapter.classList.contains('profile-manufacturing');
            animate(chapter.querySelector('.profile-story-media'), `translate3d(${reverse ? 36 : -36}px, 0, 0)`, 0, 600, false, true);
            animate(chapter.querySelector('.profile-story-grid > div'), `translate3d(${reverse ? -24 : 24}px, 0, 0)`, 80, 500);
        } else if (kind === 'collaboration') {
            all('.profile-collaboration-grid > article', chapter).forEach((article, index) => {
                animate(article, 'translate3d(0, 28px, 0)', index * 70, 460);
            });
        } else if (kind === 'customers') {
            animate(chapter.querySelector('.landing-section-heading'), 'translate3d(0, 24px, 0)', 0, 460);
            animate(chapter.querySelector('.profile-customer-grid'), 'translate3d(0, 28px, 0)', 80, 500);
        } else if (kind === 'heading') {
            animate(chapter, 'translate3d(0, 24px, 0)', 0, 460);
        } else if (kind === 'close') {
            animate(chapter.querySelector('.landing-cta > div'), 'translate3d(28px, 0, 0)', 0, 500);
            animate(chapter.querySelector('.landing-cta > img'), 'translate3d(-28px, 0, 0)', 60, 560);
        }
    };

    const start = () => {
        if (disposed || focused) return;
        if (reduced.matches) {
            stop('reduced');
            return;
        }
        cancelActive();
        clearPending();
        page.dataset.presentationState = 'ready';
        if (observer) observer.disconnect();
        chapters.clear();
        const add = (selector, kind) => all(selector).forEach(chapter => chapters.set(chapter, kind));
        add('.profile-partnership-hero', 'hero');
        add(compact.matches ? '.profile-rapid-workflow-grid > article' : '.profile-rapid-workflow-grid', compact.matches ? 'step' : 'workflow');
        add('.profile-development, .profile-deployment', 'story');
        add('.profile-collaboration', 'collaboration');
        add('.profile-outcomes .landing-section-heading, .profile-collaboration .landing-section-heading, .profile-manufacturing .landing-section-heading', 'heading');
        add('.profile-customers', 'customers');
        add('.landing-cta-section', 'close');
        try {
            observer = new IntersectionObserver((entries, currentObserver) => {
                if (currentObserver !== observer || page.dataset.presentationState !== 'ready') return;
                entries.forEach(entry => {
                    if (!entry.isIntersecting) return;
                    play(entry.target, chapters.get(entry.target));
                    currentObserver.unobserve(entry.target);
                });
            }, { threshold: 0.08, rootMargin: '0px 0px -8% 0px' });
            chapters.forEach((kind, chapter) => {
                if (played.has(chapter)) return;
                // Never hide something the visitor is already reading, including restored scroll positions.
                targets(chapter, kind).forEach(element => {
                    if (!played.has(element) && element.getBoundingClientRect().top >= window.innerHeight * 0.85) {
                        element.setAttribute('data-presentation-pending', '');
                        pending.add(element);
                    }
                });
                observer.observe(chapter);
            });
        } catch (_) {
            stop('static');
        }
    };

    const onPreference = () => reduced.matches ? stop('reduced') : start();
    const onVisibility = () => { if (document.hidden) stop('static'); else start(); };
    const onFocus = () => { focused = true; stop('static'); };
    const dispose = () => {
        disposed = true;
        stop('disposed');
        if (reduced.removeEventListener) reduced.removeEventListener('change', onPreference);
        if (compact.removeEventListener) compact.removeEventListener('change', start);
        document.removeEventListener('visibilitychange', onVisibility);
        page.removeEventListener('focusin', onFocus);
    };
    if (reduced.addEventListener) reduced.addEventListener('change', onPreference);
    if (compact.addEventListener) compact.addEventListener('change', start);
    document.addEventListener('visibilitychange', onVisibility);
    page.addEventListener('focusin', onFocus);
    window.addEventListener('pagehide', dispose, { once: true });
    start();
})();
