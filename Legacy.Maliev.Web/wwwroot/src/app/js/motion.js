(() => {
    const root = document.documentElement;

    // The hidden state lives entirely behind this gate (see motion.css). If the inline
    // head script never ran, nothing was ever hidden and there is nothing to reveal.
    if (root.dataset.motion !== 'ready') {
        return;
    }

    // Production concatenates every file under src/app/js into one bundle while some of
    // them are also linked per page, so a module here has to survive being run twice.
    if (root.dataset.motionInit === 'true') {
        return;
    }
    root.dataset.motionInit = 'true';

    const releaseGate = () => {
        root.removeAttribute('data-motion');
        window.clearTimeout(window.__malievMotionFailsafe);
    };

    // Selection is structural so nine service pages and two inquiry pages cost no markup
    // edits. Each entry is one visual group: siblings within a group stagger together.
    // Keep in step with the hidden-state selector list in motion.css.
    const REVEAL_TARGETS = [
        '.landing-value-strip article',
        '.landing-section-heading',
        '.landing-service-grid > article',
        '.landing-process-grid > li',
        '.landing-reason-grid > article',
        '.landing-cta',
        '.service-quick article',
        '.service-heading',
        '.service-card-grid > *',
        '.service-part-bento > figure',
        '.service-process > li',
        '.service-pricing-grid > *',
        '.service-split > *',
        '.service-faq > details',
        '.service-table-wrap',
        '.service-material-comparison',
        '.service-note',
        '.service-cta',
        '.timeline > li',
        '.contact-method-grid > article',
        '.inquiry-panel',
        '.inquiry-form-card',
        '.inquiry-checklist',
        '.inquiry-map'
    ];

    // The one on-load moment per page. Hero copy rises; on the home page the three
    // process plates close on the diagonal seam behind it.
    const ENTER_TARGETS = [
        { selector: '.landing-hero-copy > *', variant: 'enter', step: 70 },
        { selector: '.service-hero-copy > *', variant: 'enter', step: 70 },
        { selector: '.inquiry-hero__content > *', variant: 'enter', step: 70 },
        { selector: '.landing-collage img', variant: 'plate', step: 90 }
    ];

    // Four steps of stagger is a group reading as a group; beyond that it reads as
    // a queue and the last card arrives after the reader has already looked at it.
    const STAGGER_STEP_MS = 60;
    const STAGGER_MAX_STEPS = 4;

    const prefersReducedMotion = () => window.matchMedia
        && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    const mark = (element, variant, delayMs) => {
        element.setAttribute('data-reveal', variant);
        if (delayMs > 0) {
            element.style.transitionDelay = delayMs + 'ms';
        }
    };

    // Longest transition in motion.css, plus room for the stagger to run out.
    const LONGEST_TRANSITION_MS = 800;

    const reveal = (element) => {
        const delayMs = parseFloat(element.style.transitionDelay) || 0;
        element.classList.add('is-revealed');
        // The delay has done its job once the element has arrived; leaving it in place
        // would slow every later transition on the same element — a card hover, say — by
        // the same amount. On a timer rather than transitionend, which never fires if the
        // transition is interrupted or the tab is hidden while it runs.
        window.setTimeout(() => {
            element.style.transitionDelay = '';
        }, delayMs + LONGEST_TRANSITION_MS);
    };

    const collect = (selector) => Array.prototype.slice.call(document.querySelectorAll(selector));

    const initialise = () => {
        const entering = [];
        ENTER_TARGETS.forEach((target) => {
            collect(target.selector).forEach((element, index) => {
                mark(element, target.variant, index * target.step);
                entering.push(element);
            });
        });

        const observed = [];
        REVEAL_TARGETS.forEach((selector) => {
            const groups = new Map();
            collect(selector).forEach((element) => {
                // Position within the sibling group, not within the document: two
                // separate card grids on one page each start their stagger at zero.
                const parent = element.parentElement;
                const position = groups.get(parent) || 0;
                groups.set(parent, position + 1);
                mark(element, 'rise', Math.min(position, STAGGER_MAX_STEPS) * STAGGER_STEP_MS);
                observed.push(element);
            });
        });

        if (typeof IntersectionObserver === 'undefined') {
            entering.concat(observed).forEach(reveal);
            return;
        }

        // Fires as a block's leading edge crosses the last tenth of the viewport, so the
        // reveal completes just as the reader arrives at it rather than under their eyes.
        const observer = new IntersectionObserver((entries, activeObserver) => {
            entries.forEach((entry) => {
                if (!entry.isIntersecting) {
                    return;
                }

                reveal(entry.target);
                activeObserver.unobserve(entry.target);
            });
        }, { threshold: 0, rootMargin: '0px 0px -10% 0px' });

        observed.forEach((element) => observer.observe(element));

        // The hero is the page's arrival, not a scroll event: it plays on load. One frame
        // of separation so the browser has the hidden state before the revealed one.
        let entranceStarted = false;
        const playEntrance = () => {
            if (entranceStarted) {
                return;
            }

            entranceStarted = true;
            entering.forEach(reveal);
        };

        window.requestAnimationFrame(() => window.requestAnimationFrame(playEntrance));
        // requestAnimationFrame never fires in a background tab, and a hero that waits for
        // it would still be hidden when the visitor switches over. Timers do fire there, so
        // this is the guarantee; in a visible tab the two frames above always win the race
        // and the entrance plays properly.
        window.setTimeout(playEntrance, 150);
    };

    if (prefersReducedMotion()) {
        releaseGate();
        return;
    }

    try {
        initialise();
        // Init succeeded, so this module owns the gate from here. Cancel the head
        // script's failsafe or it would unhide every unscrolled block at 2.5s.
        window.clearTimeout(window.__malievMotionFailsafe);
    } catch (error) {
        releaseGate();
    }
})();
