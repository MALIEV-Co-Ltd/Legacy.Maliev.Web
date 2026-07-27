(() => {
    const prefersReducedMotion = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;

    const scrollLinkIntoView = (list, link) => {
        const listRect = list.getBoundingClientRect();
        const linkRect = link.getBoundingClientRect();
        const edgePadding = 8;
        let targetLeft = list.scrollLeft;

        if (linkRect.left < listRect.left + edgePadding) {
            targetLeft += linkRect.left - listRect.left - edgePadding;
        } else if (linkRect.right > listRect.right - edgePadding) {
            targetLeft += linkRect.right - listRect.right + edgePadding;
        }

        const maximumLeft = Math.max(0, list.scrollWidth - list.clientWidth);
        targetLeft = Math.max(0, Math.min(maximumLeft, targetLeft));
        if (Math.abs(targetLeft - list.scrollLeft) < 1) return;

        list.scrollTo({
            left: targetLeft,
            top: 0,
            behavior: prefersReducedMotion() ? 'auto' : 'smooth'
        });
    };

    const initialiseServiceToc = () => {
        document.querySelectorAll('[data-service-toc]').forEach((toc) => {
            const page = toc.closest('main.service-page');
            const list = toc.querySelector('[data-service-toc-list]');
            if (!page || !list || list.children.length > 0) return;

            const sections = Array.from(page.querySelectorAll(':scope > section'))
                .filter((section) => !section.classList.contains('service-quick')
                    && !section.classList.contains('service-cta-wrap'))
                .filter((section) => section.querySelector('h2')?.textContent?.trim());
            const sectionTargets = new Map();
            const links = new Map();
            let activeTargetId = null;
            let activeLink = null;
            let activeUpdateScheduled = false;

            const setActiveSection = (targetId, keepVisible = true) => {
                const link = links.get(targetId);
                if (!link) return;
                if (activeTargetId !== targetId) {
                    activeLink?.classList.remove('is-active');
                    activeLink?.removeAttribute('aria-current');
                    activeTargetId = targetId;
                    activeLink = link;
                    activeLink.classList.add('is-active');
                    activeLink.setAttribute('aria-current', 'true');
                }
                if (keepVisible) window.requestAnimationFrame(() => scrollLinkIntoView(list, link));
            };

            sections.forEach((section, index) => {
                const heading = section.querySelector('h2');
                const targetId = section.id || `service-section-${index + 1}`;
                if (!section.id) section.id = targetId;
                sectionTargets.set(section, targetId);

                const item = document.createElement('li');
                const link = document.createElement('a');
                link.href = `#${targetId}`;
                link.textContent = heading.textContent.trim();
                link.addEventListener('click', () => setActiveSection(targetId));
                item.append(link);
                list.append(item);
                links.set(targetId, link);
            });

            if (list.children.length === 0) return;
            toc.hidden = false;

            const updateActiveSection = () => {
                const tocRect = toc.getBoundingClientRect();
                const activationLine = Math.min(
                    window.innerHeight * 0.45,
                    tocRect.bottom + Math.max(24, tocRect.height * 0.65)
                );
                let currentSection = null;
                sections.forEach((section) => {
                    if (section.getBoundingClientRect().top <= activationLine) currentSection = section;
                });
                const currentTargetId = currentSection ? sectionTargets.get(currentSection) : null;
                if (currentTargetId) setActiveSection(currentTargetId);
            };

            const scheduleActiveSectionUpdate = () => {
                if (activeUpdateScheduled) return;
                activeUpdateScheduled = true;
                window.requestAnimationFrame(() => {
                    activeUpdateScheduled = false;
                    updateActiveSection();
                });
            };

            window.addEventListener('scroll', scheduleActiveSectionUpdate, { passive: true });
            window.addEventListener('resize', scheduleActiveSectionUpdate);
            const hashTargetId = window.location.hash.slice(1);
            if (hashTargetId && links.has(hashTargetId)) setActiveSection(hashTargetId);
            scheduleActiveSectionUpdate();
        });
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initialiseServiceToc, { once: true });
    } else {
        initialiseServiceToc();
    }
})();
