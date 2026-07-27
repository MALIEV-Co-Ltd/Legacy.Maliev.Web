(() => {
    const initialiseServiceToc = () => {
        document.querySelectorAll('[data-service-toc]').forEach((toc) => {
            const page = toc.closest('main.service-page');
            const list = toc.querySelector('[data-service-toc-list]');

            if (!page || !list || list.children.length > 0) {
                return;
            }

            const sections = Array.from(page.querySelectorAll(':scope > section'))
                .filter((section) => !section.classList.contains('service-quick') && !section.classList.contains('service-cta-wrap'))
                .filter((section) => section.querySelector('h2'));

            sections.forEach((section, index) => {
                const heading = section.querySelector('h2');
                const text = heading?.textContent?.trim();
                const targetId = section.id || heading?.id || `service-section-${index + 1}`;

                if (!text) {
                    return;
                }

                if (!section.id && !heading?.id) {
                    section.id = targetId;
                }

                const item = document.createElement('li');
                const link = document.createElement('a');
                link.href = `#${targetId}`;
                link.textContent = text;
                item.append(link);
                list.append(item);
            });

            if (list.children.length > 0) {
                toc.hidden = false;
            }
        });
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initialiseServiceToc, { once: true });
    } else {
        initialiseServiceToc();
    }
})();
