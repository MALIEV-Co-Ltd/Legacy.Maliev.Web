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
    let disposed = false;
    let previewCount = 0;

    function observeVisibility() {
        const card = selected();
        if (!card) return;
        const container = scroller.getBoundingClientRect();
        const bounds = card.getBoundingClientRect();
        selectedVisible = bounds.top >= container.top && bounds.bottom <= container.bottom;
    }
    function reveal(card) {
        if (!card) return;
        const heading = card.previousElementSibling?.matches('[data-material-category]')
            ? card.previousElementSibling : card;
        const container = scroller.getBoundingClientRect();
        const top = heading.getBoundingClientRect().top - container.top;
        const bottom = card.getBoundingClientRect().bottom - container.bottom;
        if (top < 0) scroller.scrollTop += top;
        else if (bottom > 0) scroller.scrollTop += bottom;
        observeVisibility();
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
        const key = card?.dataset.materialKey;
        const searching = !!root.querySelector('[data-material-search]').value.trim();
        if (!searching && (key !== previousKey || selectedVisible)) reveal(card);
        previousKey = key;
        const focused = scroller.querySelector('[role="radio"]:focus');
        for (const item of cards()) item.tabIndex = item === (focused || card || cards()[0]) ? 0 : -1;
    }
    function schedule() {
        if (!disposed && !frame) frame = root.ownerDocument.defaultView.requestAnimationFrame(layout);
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
            reveal(next);
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
        throw error;
    }
    return {
        dispose() {
            disposed = true;
            observer.disconnect();
            resize.disconnect();
            scroller.removeEventListener('keydown', keydown);
            scroller.removeEventListener('scroll', observeVisibility);
            root.ownerDocument.defaultView.removeEventListener('resize', schedule);
            if (frame) root.ownerDocument.defaultView.cancelAnimationFrame(frame);
        }
    };
}
