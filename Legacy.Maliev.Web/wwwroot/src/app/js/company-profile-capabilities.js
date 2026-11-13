(() => {
    'use strict';
    const root = document.querySelector('[data-capability-carousel]');
    if (!root || root.dataset.carouselState) return;
    const track = root.querySelector('[data-capability-track]');
    const controls = root.querySelector('[data-capability-controls]');
    const choices = root.querySelector('.profile-capability-choices');
    const slides = Array.from(root.querySelectorAll('.profile-capability-slide'));
    const buttons = Array.from(root.querySelectorAll('[data-capability-index]'));
    const previous = root.querySelector('[data-capability-prev]');
    const next = root.querySelector('[data-capability-next]');
    const status = root.querySelector('[data-capability-status]');
    if (!track || !controls || !choices || !previous || !next || !status
        || !slides.length || buttons.length !== slides.length || typeof track.scrollTo !== 'function') return;

    let index = 0, destination = null, settleTimer = null, disposed = false;
    let statusAnimation = null, statusRevision = 0, pendingLabel = '', statusReady = false;
    const reducedMotion = () => !window.matchMedia || window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const showStatus = async label => {
        pendingLabel = label;
        if (!statusReady || reducedMotion() || typeof status.animate !== 'function') {
            statusReady = true;
            statusRevision++;
            statusAnimation?.cancel(); statusAnimation = null;
            status.textContent = label;
            return;
        }
        if (statusAnimation || status.textContent === label) return;
        const revision = ++statusRevision;
        try {
            statusAnimation = status.animate([{ opacity: 1 }, { opacity: 0 }], { duration: 100, easing: 'ease-out', fill: 'both' });
            await statusAnimation.finished;
            if (disposed || revision !== statusRevision) return;
            status.textContent = pendingLabel;
            statusAnimation.cancel();
            statusAnimation = status.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 160, easing: 'ease-out', fill: 'both' });
            await statusAnimation.finished;
        } catch (_) {
            if (!disposed && revision === statusRevision) status.textContent = pendingLabel;
        }
        if (disposed || revision !== statusRevision) return;
        statusAnimation?.cancel(); statusAnimation = null;
        if (status.textContent !== pendingLabel) showStatus(pendingLabel);
    };
    const update = value => {
        index = Math.max(0, Math.min(slides.length - 1, value));
        buttons.forEach((button, i) => button.setAttribute('aria-pressed', String(i === index)));
        previous.disabled = index === 0;
        next.disabled = index === slides.length - 1;
        const label = `${index + 1} / ${slides.length} · ${buttons[index].textContent.trim()}`;
        showStatus(label);
    };
    const slideOffsets = () => {
        // offsetLeft rounds to whole CSS pixels and can leave a sliver at fractional widths.
        const origin = slides[0].getBoundingClientRect().left;
        return slides.map(slide => slide.getBoundingClientRect().left - origin);
    };
    const go = value => {
        update(value);
        const reduced = reducedMotion();
        destination = reduced ? null : index;
        track.scrollTo({ left: slideOffsets()[index], behavior: reduced ? 'auto' : 'smooth' });
        if (destination !== null) waitForSettlement();
    };
    const nearestSlide = () => {
        const offsets = slideOffsets();
        let nearest = 0;
        offsets.forEach((offset, i) => {
            if (Math.abs(offset - track.scrollLeft) < Math.abs(offsets[nearest] - track.scrollLeft)) nearest = i;
        });
        return nearest;
    };
    const settled = () => {
        window.clearTimeout(settleTimer); settleTimer = null;
        destination = null;
        update(nearestSlide());
    };
    const waitForSettlement = () => {
        window.clearTimeout(settleTimer);
        settleTimer = window.setTimeout(settled, 140);
    };
    const onScroll = () => {
        // Smooth scrolling crosses other services; keep the requested destination steady.
        if (destination !== null) waitForSettlement();
        else update(nearestSlide());
    };
    const onPrevious = () => go(index - 1);
    const onNext = () => go(index + 1);
    const onKey = event => {
        const destinations = { ArrowLeft: index - 1, ArrowRight: index + 1, Home: 0, End: slides.length - 1 };
        if (!(event.key in destinations)) return;
        event.preventDefault();
        go(destinations[event.key]);
        buttons[index].focus();
    };
    const select = buttons.map((_, i) => () => go(i));
    buttons.forEach((button, i) => button.addEventListener('click', select[i]));
    previous.addEventListener('click', onPrevious);
    next.addEventListener('click', onNext);
    choices.addEventListener('keydown', onKey);
    track.addEventListener('scroll', onScroll, { passive: true });
    track.addEventListener('scrollend', settled);
    const dispose = () => {
        disposed = true; statusRevision++;
        window.clearTimeout(settleTimer);
        statusAnimation?.cancel(); statusAnimation = null;
        buttons.forEach((button, i) => button.removeEventListener('click', select[i]));
        previous.removeEventListener('click', onPrevious);
        next.removeEventListener('click', onNext);
        choices.removeEventListener('keydown', onKey);
        track.removeEventListener('scroll', onScroll);
        track.removeEventListener('scrollend', settled);
        controls.hidden = true;
        root.dataset.carouselState = 'static';
    };
    window.addEventListener('pagehide', dispose, { once: true });
    onScroll();
    controls.hidden = false;
    root.dataset.carouselState = 'ready';
})();
