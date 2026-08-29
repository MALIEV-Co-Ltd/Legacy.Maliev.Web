(() => {
    'use strict';

    const toggle = document.querySelector('[data-part-gallery-toggle]');
    const gallery = document.getElementById(toggle?.getAttribute('aria-controls') ?? '');
    const controls = document.querySelector('[data-part-gallery-controls]');

    if (!(toggle instanceof HTMLButtonElement) || !(gallery instanceof HTMLElement) || !(controls instanceof HTMLElement)) {
        return;
    }

    const tiles = [...gallery.querySelectorAll('[data-part-gallery-extra]')];
    const loadDeferredImages = () => {
        for (const image of gallery.querySelectorAll('img[data-src]')) {
            image.src = image.dataset.src;
            image.removeAttribute('data-src');

            if (image.dataset.srcset) {
                image.srcset = image.dataset.srcset;
                image.removeAttribute('data-srcset');
            }
        }
    };

    controls.hidden = false;
    for (const tile of tiles) {
        tile.setAttribute('aria-hidden', 'true');
    }

    toggle.addEventListener('click', () => {
        loadDeferredImages();
        gallery.hidden = false;
        toggle.setAttribute('aria-expanded', 'true');
        toggle.hidden = true;

        for (const tile of tiles) {
            tile.setAttribute('aria-hidden', 'false');
        }
    });
})();
