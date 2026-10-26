/** Owns keyboard focus and scrolling only; selection remains with the Blazor workflow. */
export function materialPreviewCount(width, height) {
    if (width <= 767) return height >= 800 ? 6 : 4;
    if (width <= 1199 || height <= 820) return 6;
    return height >= 1000 ? 10 : 8;
}

export function createMaterialCardSelector(root, callback) {
    const scroller = root.querySelector('[data-material-card-scroll]');
    const cards = () => Array.from(scroller.querySelectorAll('[role="radio"]'));
    const selected = () => scroller.querySelector('[aria-checked="true"]');
    let previousKey = selected()?.dataset.materialKey;
    let selectedVisible = true;
    let frame = 0;
    let revealFrame = 0;
    let disposed = false;
    let previewCount = 0;

    function observeVisibility() {
        const card = selected();
        if (!card) { selectedVisible = false; return; }
        const container = scroller.getBoundingClientRect();
        const bounds = card.getBoundingClientRect();
        selectedVisible = bounds.top >= container.top - 1 && bounds.bottom <= container.bottom + 1;
    }
    function reveal(card, includeHeading) {
        if (!card) return;
        const heading = includeHeading && card.previousElementSibling?.matches('[data-material-category]')
            ? card.previousElementSibling : card;
        const container = scroller.getBoundingClientRect();
        const top = heading.getBoundingClientRect().top - container.top;
        const bottom = card.getBoundingClientRect().bottom - container.bottom;
        if (top < -1) scroller.scrollTop += top;
        else if (bottom > 1) scroller.scrollTop += bottom;
        observeVisibility();
    }
    function searchActive() {
        return !!root.querySelector('[data-material-search]').value.trim();
    }
    function revealAfterSelection(key) {
        if (revealFrame) root.ownerDocument.defaultView.cancelAnimationFrame(revealFrame);
        revealFrame = root.ownerDocument.defaultView.requestAnimationFrame(() => {
            revealFrame = 0;
            if (disposed) return;
            const card = selected();
            if (!card || card.dataset.materialKey !== key || searchActive()) {
                selectedVisible = false;
                return;
            }
            reveal(card, true);
        });
    }
    function layout() {
        frame = 0;
        if (disposed) return;
        const width = root.ownerDocument.defaultView.innerWidth;
        const count = materialPreviewCount(width, root.ownerDocument.defaultView.innerHeight);
        if (count !== previewCount) {
            previewCount = count;
            callback.invokeMethodAsync('SetPreviewCount', count).catch(() => {});
        }
        const card = selected();
        if (!card) selectedVisible = false;
        const key = card?.dataset.materialKey;
        const searching = searchActive();
        if (searching) {
            selectedVisible = false;
            // A filtered refresh invalidates an earlier selection reveal even if
            // search clears again before that animation frame would run.
            if (revealFrame) root.ownerDocument.defaultView.cancelAnimationFrame(revealFrame);
            revealFrame = 0;
        }
        if (card && key !== previousKey) {
            previousKey = key;
            if (!searching) revealAfterSelection(key);
        } else if (card && !searching && selectedVisible && !revealFrame) {
            // A price row can grow during the Blazor render; repair visibility now,
            // without pulling a deliberately scrolled customer to the heading.
            reveal(card, false);
        }
        const focused = scroller.querySelector('[role="radio"]:focus');
        for (const item of cards()) item.tabIndex = item === (focused || card || cards()[0]) ? 0 : -1;
    }
    function schedule() {
        if (!disposed && !frame) frame = root.ownerDocument.defaultView.requestAnimationFrame(layout);
    }
    function refresh() {
        if (disposed) return;
        if (frame) root.ownerDocument.defaultView.cancelAnimationFrame(frame);
        frame = 0;
        layout();
    }
    function keydown(event) {
        if (event.target.closest('button,input')) return;
        const card = event.target.closest('[role="radio"]');
        if (!card) return;
        const items = cards();
        const index = items.indexOf(card);
        const direction = ['ArrowRight', 'ArrowDown'].includes(event.key) ? 1
            : ['ArrowLeft', 'ArrowUp'].includes(event.key) ? -1 : 0;
        if (direction) {
            event.preventDefault();
            const next = items[(index + direction + items.length) % items.length];
            for (const item of items) item.tabIndex = item === next ? 0 : -1;
            next.focus({ preventScroll: true });
            reveal(next, true);
        } else if (event.key === ' ' || event.key === 'Enter') {
            event.preventDefault();
            card.click();
        }
    }
    const observer = new MutationObserver(schedule);
    const resize = new ResizeObserver(schedule);
    try {
        observer.observe(scroller, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ['aria-checked'] });
        resize.observe(scroller);
        scroller.addEventListener('keydown', keydown);
        scroller.addEventListener('scroll', observeVisibility, { passive: true });
        root.ownerDocument.defaultView.addEventListener('resize', schedule);
        schedule();
    } catch (error) {
        observer.disconnect();
        resize.disconnect();
        scroller.removeEventListener('keydown', keydown);
        scroller.removeEventListener('scroll', observeVisibility);
        root.ownerDocument.defaultView.removeEventListener('resize', schedule);
        if (frame) root.ownerDocument.defaultView.cancelAnimationFrame(frame);
        if (revealFrame) root.ownerDocument.defaultView.cancelAnimationFrame(revealFrame);
        throw error;
    }
    return {
        refresh,
        dispose() {
            disposed = true;
            observer.disconnect();
            resize.disconnect();
            scroller.removeEventListener('keydown', keydown);
            scroller.removeEventListener('scroll', observeVisibility);
            root.ownerDocument.defaultView.removeEventListener('resize', schedule);
            if (frame) root.ownerDocument.defaultView.cancelAnimationFrame(frame);
            if (revealFrame) root.ownerDocument.defaultView.cancelAnimationFrame(revealFrame);
        }
    };
}
