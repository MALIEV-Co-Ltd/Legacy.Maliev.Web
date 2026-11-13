(() => {
    'use strict';
    const start = () => {
        const root = document.querySelector('.service-showcase');
        if (!root || root.dataset.serviceIntroReady || !window.IntersectionObserver || !window.matchMedia) return;
        root.dataset.serviceIntroReady = 'true';
        const motion = window.matchMedia('(prefers-reduced-motion: reduce)');
        if (motion.matches) return;
        const cards = [...root.querySelectorAll('.service-feature--scanning, .service-feature--design')];
        const timers = new Map();
        const element = (parent, cls, count) => {
            for (let i = 0; i < count; i++) {
                const item = document.createElement('span');
                item.className = cls;
                item.style.setProperty('--i', i);
                item.style.setProperty('--col', i % 4);
                item.style.setProperty('--row', Math.floor(i / 4));
                item.style.setProperty('--x', (i * 17) % 90);
                item.style.setProperty('--y', (i * 23) % 80);
                item.style.setProperty('--sprite-x', `${(i % 3) * 50}%`);
                item.style.setProperty('--sprite-y', `${Math.floor(i / 3) % 2 * 100}%`);
                if (cls === 'intro-marker') {
                    item.style.setProperty('--marker-x', `${9 + (i % 4) * 23 + Math.random() * 13}%`);
                    item.style.setProperty('--marker-y', `${12 + Math.floor(i / 4) * 30 + Math.random() * 11}%`);
                }
                parent.appendChild(item);
            }
        };
        const finish = (card) => {
            clearTimeout(timers.get(card));
            timers.delete(card);
            card.classList.remove('service-intro-playing');
            card.querySelector('.service-intro-effect')?.remove();
        };
        const observer = new IntersectionObserver((entries) => {
            entries.forEach(({ target: card, isIntersecting }) => {
                if (!isIntersecting) {
                    if (timers.has(card)) { finish(card); observer.unobserve(card); }
                    return;
                }
                if (card.dataset.serviceIntroDone || motion.matches || document.hidden) return;
                card.dataset.serviceIntroDone = 'true';
                const effect = document.createElement('div');
                effect.className = 'service-intro-effect';
                effect.setAttribute('aria-hidden', 'true');
                if (card.classList.contains('service-feature--scanning')) {
                    element(effect, 'intro-scan-matte', 1);
                    element(effect, 'intro-scan-spray', 1);
                    element(effect, 'intro-marker', 12);
                    element(effect, 'intro-parallel', 1);
                    element(effect, 'intro-cross', 1);
                    element(effect, 'intro-point', 18);
                }
                card.appendChild(effect);
                card.classList.add('service-intro-playing');
                const duration = card.classList.contains('service-feature--design') ? 10000 : 9500;
                timers.set(card, setTimeout(() => { finish(card); observer.unobserve(card); }, duration));
            });
        }, { threshold: 0.45 });
        cards.forEach(card => observer.observe(card));
        const stop = () => {
            for (const card of [...timers.keys()]) { finish(card); observer.unobserve(card); }
        };
        motion.addEventListener?.('change', event => { if (event.matches) stop(); });
        document.addEventListener('visibilitychange', () => {
            if (document.hidden) stop();
            else cards.filter(card => !card.dataset.serviceIntroDone).forEach(card => { observer.unobserve(card); observer.observe(card); });
        });
        window.addEventListener('pagehide', () => { stop(); observer.disconnect(); }, { once: true });
    };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
    else start();
})();
