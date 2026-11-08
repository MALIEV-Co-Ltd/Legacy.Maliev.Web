(() => {
    document.addEventListener('click', (event) => {
        document.querySelectorAll('.landing-contact-details[open]').forEach((menu) => {
            if (!menu.contains(event.target)) menu.open = false;
        });
    });

    document.addEventListener('keydown', (event) => {
        if (event.key !== 'Escape') return;
        document.querySelectorAll('.landing-contact-details[open]').forEach((menu) => {
            menu.open = false;
            menu.querySelector('summary')?.focus();
        });
    });
})();
