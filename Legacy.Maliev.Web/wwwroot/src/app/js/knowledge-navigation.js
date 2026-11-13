(function () {
    'use strict';

    const drawer = document.getElementById('knowledge-navigation');
    const openButton = document.querySelector('[data-workspace-open]');
    const closeButton = drawer?.querySelector('[data-workspace-close]');
    const backdrop = document.querySelector('[data-workspace-backdrop]');
    if (!drawer || !openButton || !closeButton || !backdrop || drawer.dataset.navigationReady === 'true') return;
    drawer.dataset.navigationReady = 'true';

    // Keep the left contents list aligned with the current article headings.
    // Native topic groups and the in-article contents still work without JS.
    const sections = drawer.querySelector('[data-knowledge-current-toc]');
    if (sections) {
        document.querySelectorAll('.knowledge-document section[id] > h2').forEach(heading => {
            const item = document.createElement('li');
            const link = document.createElement('a');
            link.href = '#' + encodeURIComponent(heading.parentElement.id);
            link.textContent = heading.textContent;
            link.addEventListener('click', () => {
                if (mobile.matches) close(false);
                heading.parentElement.focus({ preventScroll: true });
            });
            heading.parentElement.tabIndex = -1;
            item.append(link);
            sections.append(item);
        });
    }

    // Match the shared workspace breakpoint; desktop navigation is never modal.
    const mobile = window.matchMedia('(max-width: 71.99rem)');
    let previousInert = [];
    let focusFrame = null;
    const isOpen = () => drawer.classList.contains('is-open');
    const close = (restoreFocus = true) => {
        const wasOpen = isOpen();
        if (focusFrame !== null) window.cancelAnimationFrame(focusFrame);
        focusFrame = null;
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
        focusFrame = window.requestAnimationFrame(() => {
            focusFrame = null;
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
            const controls = Array.from(drawer.querySelectorAll('a[href], button:not([disabled]), input:not([disabled]), summary'))
                .filter(element => element.checkVisibility());
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
    drawer.addEventListener('click', event => {
        const link = event.target.closest('[data-knowledge-search-results] a');
        if (!link) return;
        const target = new URL(link.href, window.location.href);
        if (target.pathname === window.location.pathname && target.hash) {
            if (mobile.matches) close(false);
            const heading = document.getElementById(decodeURIComponent(target.hash.slice(1)));
            if (heading) {
                heading.tabIndex = -1;
                heading.focus({ preventScroll: true });
            }
        }
    });
    mobile.addEventListener('change', () => close(false));
    window.addEventListener('pagehide', () => close(false));
    close(false);
})();
