(() => {
    const prefersReducedMotion = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;

    const LEADING_EDGE_PADDING = 8;
    // The rail's trailing edge is masked by a 2.5rem fade (see service-pages.css). Parking the
    // current chip 8px from that edge would leave a third of it dissolved into the background,
    // so forward scrolling has to clear the fade as well as the edge.
    const TRAILING_EDGE_PADDING = 44;

    const scrollLinkIntoView = (list, link) => {
        const listRect = list.getBoundingClientRect();
        const linkRect = link.getBoundingClientRect();
        let targetLeft = list.scrollLeft;

        if (linkRect.left < listRect.left + LEADING_EDGE_PADDING) {
            targetLeft += linkRect.left - listRect.left - LEADING_EDGE_PADDING;
        } else if (linkRect.right > listRect.right - TRAILING_EDGE_PADDING) {
            targetLeft += linkRect.right - listRect.right + TRAILING_EDGE_PADDING;
        }

        const maximumLeft = Math.max(0, list.scrollWidth - list.clientWidth);
        targetLeft = Math.max(0, Math.min(maximumLeft, targetLeft));
        if (Math.abs(targetLeft - list.scrollLeft) < 1) {
            return;
        }

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
            const toggle = toc.querySelector('[data-service-toc-toggle]');
            const currentLabel = toc.querySelector('[data-service-toc-current]');
            const preview = toc.querySelector('[data-service-toc-preview]');
            const previewTitle = toc.querySelector('[data-service-toc-preview-title]');
            const previewSummary = toc.querySelector('[data-service-toc-preview-summary]');

            if (!page || !list || list.children.length > 0) {
                return;
            }

            const isPanelOpen = () => toc.dataset.tocOpen === 'true';

            const sections = Array.from(page.querySelectorAll(':scope > section'))
                .filter((section) => !section.classList.contains('service-quick') && !section.classList.contains('service-cta-wrap'))
                .filter((section) => section.querySelector('h2')?.textContent?.trim());
            const sectionTargets = new Map();
            const sectionPreviews = new Map();
            const links = new Map();
            let activeTargetId = null;
            let activeLink = null;
            let previewTargetId = null;
            let previewLink = null;
            let activeUpdateScheduled = false;

            const setPreviewSection = (targetId) => {
                const link = links.get(targetId);
                const sectionPreview = sectionPreviews.get(targetId);
                if (!link || !sectionPreview) {
                    return;
                }

                if (previewTargetId !== targetId) {
                    previewLink?.classList.remove('is-preview');
                    previewTargetId = targetId;
                    previewLink = link;
                    previewLink.classList.add('is-preview');
                }

                if (previewTitle) {
                    previewTitle.textContent = sectionPreview.title;
                }
                if (previewSummary) {
                    previewSummary.textContent = sectionPreview.summary;
                }
            };

            // The rail can only ever show a few destinations, and none at all on a phone.
            // The panel is the reliable way to reach every section, so it owns the full list.
            const setPanelOpen = (open) => {
                toc.dataset.tocOpen = open ? 'true' : 'false';
                toggle?.setAttribute('aria-expanded', open ? 'true' : 'false');
                preview?.setAttribute('aria-hidden', open ? 'false' : 'true');

                if (open) {
                    const targetId = activeTargetId || links.keys().next().value;
                    if (targetId) {
                        setPreviewSection(targetId);
                        window.requestAnimationFrame(() => links.get(targetId)?.focus({ preventScroll: true }));
                    }
                } else {
                    previewLink?.classList.remove('is-preview');
                    previewLink = null;
                    previewTargetId = null;
                }
            };

            const setActiveSection = (targetId, keepVisible = true) => {
                const link = links.get(targetId);
                if (!link) {
                    return;
                }

                if (activeTargetId !== targetId) {
                    activeLink?.classList.remove('is-active');
                    activeLink?.removeAttribute('aria-current');
                    activeTargetId = targetId;
                    activeLink = link;
                    activeLink.classList.add('is-active');
                    activeLink.setAttribute('aria-current', 'true');

                    // On a phone the rail is hidden, so the toggle is the only thing telling
                    // the reader where they are. Keep it naming the current section.
                    if (currentLabel) {
                        currentLabel.textContent = link.textContent;
                    }
                }

                if (keepVisible && !isPanelOpen()) {
                    window.requestAnimationFrame(() => scrollLinkIntoView(list, link));
                }
            };

            sections.forEach((section, index) => {
                const heading = section.querySelector('h2');
                const text = heading?.textContent?.trim();
                const targetId = section.id || heading?.id || `service-section-${index + 1}`;
                const summaryElement = section.querySelector(
                    '[data-service-toc-summary], .service-heading > p, .service-copy > p, p');
                const summary = summaryElement?.textContent?.trim() || '';

                if (!text) {
                    return;
                }

                if (!section.id && !heading?.id) {
                    section.id = targetId;
                }
                sectionTargets.set(section, targetId);
                sectionPreviews.set(targetId, { title: text, summary });

                const item = document.createElement('li');
                const link = document.createElement('a');
                link.href = `#${targetId}`;
                link.textContent = text;
                link.addEventListener('pointerenter', () => {
                    if (isPanelOpen()) {
                        setPreviewSection(targetId);
                    }
                });
                link.addEventListener('focus', () => {
                    if (isPanelOpen()) {
                        setPreviewSection(targetId);
                    }
                });
                link.addEventListener('click', () => {
                    setPanelOpen(false);
                    setActiveSection(targetId);
                });
                item.append(link);
                list.append(item);
                links.set(targetId, link);
            });

            if (list.children.length > 0) {
                toc.hidden = false;
                setPanelOpen(false);

                if (toggle) {
                    toggle.addEventListener('click', () => setPanelOpen(!isPanelOpen()));

                    document.addEventListener('keydown', (event) => {
                        if (event.key === 'Escape' && isPanelOpen()) {
                            setPanelOpen(false);
                            toggle.focus();
                        }
                    });

                    document.addEventListener('click', (event) => {
                        if (isPanelOpen() && !toc.contains(event.target)) {
                            setPanelOpen(false);
                        }
                    });
                }

                const updateActiveSection = () => {
                    const tocRect = toc.getBoundingClientRect();
                    const activationLine = Math.min(
                        window.innerHeight * 0.45,
                        tocRect.bottom + Math.max(24, tocRect.height * 0.65)
                    );
                    let currentSection = null;

                    sections.forEach((section) => {
                        if (section.getBoundingClientRect().top <= activationLine) {
                            currentSection = section;
                        }
                    });

                    const currentTargetId = currentSection ? sectionTargets.get(currentSection) : null;
                    if (currentTargetId) {
                        setActiveSection(currentTargetId);
                    }
                };

                const scheduleActiveSectionUpdate = () => {
                    if (activeUpdateScheduled) {
                        return;
                    }

                    activeUpdateScheduled = true;
                    window.requestAnimationFrame(() => {
                        activeUpdateScheduled = false;
                        updateActiveSection();
                    });
                };

                window.addEventListener('scroll', scheduleActiveSectionUpdate, { passive: true });
                window.addEventListener('resize', scheduleActiveSectionUpdate);

                const hashTargetId = window.location.hash.slice(1);
                if (hashTargetId && links.has(hashTargetId)) {
                    setActiveSection(hashTargetId);
                }

                scheduleActiveSectionUpdate();
            }
        });
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initialiseServiceToc, { once: true });
    } else {
        initialiseServiceToc();
    }
})();
