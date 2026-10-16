(function () {
    'use strict';

    const drawer = document.getElementById('knowledge-navigation');
    const openButton = document.querySelector('[data-workspace-open]');
    const closeButton = drawer?.querySelector('[data-workspace-close]');
    const backdrop = document.querySelector('[data-workspace-backdrop]');
    if (!drawer || !openButton || !closeButton || !backdrop || drawer.dataset.navigationReady === 'true') return;
    drawer.dataset.navigationReady = 'true';

    // Match the shared workspace breakpoint; desktop navigation is never modal.
    const mobile = window.matchMedia('(max-width: 71.99rem)');
    let previousInert = [];
    const isOpen = () => drawer.classList.contains('is-open');
    const close = (restoreFocus = true) => {
        const wasOpen = isOpen();
        drawer.classList.remove('is-open');
        document.body.classList.remove('workspace-drawer-open');
        drawer.removeAttribute('role');
        drawer.removeAttribute('aria-modal');
        drawer.inert = mobile.matches;
        openButton.setAttribute('aria-expanded', 'false');
        backdrop.hidden = true;
        previousInert.forEach(([element, value]) => { element.inert = value; });
        previousInert = [];
        if (wasOpen && restoreFocus && mobile.matches) openButton.focus();
        if (wasOpen && !mobile.matches) drawer.querySelector('[aria-current="page"]')?.focus();
    };
    const open = () => {
        if (!mobile.matches || isOpen()) return;
        drawer.inert = false;
        drawer.classList.add('is-open');
        drawer.setAttribute('role', 'dialog');
        drawer.setAttribute('aria-modal', 'true');
        document.body.classList.add('workspace-drawer-open');
        openButton.setAttribute('aria-expanded', 'true');
        backdrop.hidden = false;
        const background = Array.from(document.body.children)
            .filter(element => !element.contains(drawer) && element.tagName !== 'SCRIPT');
        const main = document.getElementById('knowledge-content');
        if (main) background.push(main);
        previousInert = background.map(element => [element, element.inert]);
        background.forEach(element => { element.inert = true; });
        // Let the clicked main content become inert before moving focus into
        // the newly visible drawer; the browser may otherwise reset it to body.
        window.requestAnimationFrame(() => {
            if (isOpen()) closeButton.focus({ preventScroll: true });
        });
    };

    openButton.addEventListener('click', open);
    closeButton.addEventListener('click', () => close());
    backdrop.addEventListener('click', () => close());
    document.addEventListener('keydown', event => {
        if (!isOpen()) return;
        if (event.key === 'Escape') {
            event.preventDefault();
            close();
        } else if (event.key === 'Tab') {
            const controls = Array.from(drawer.querySelectorAll('a[href], button:not([disabled])'))
                .filter(element => element.getClientRects().length > 0);
            const first = controls[0];
            const last = controls[controls.length - 1];
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last?.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first?.focus();
            }
        }
    });
    mobile.addEventListener('change', () => close(false));
    close(false);
})();
