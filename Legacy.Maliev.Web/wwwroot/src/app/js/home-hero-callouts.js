(() => {
    'use strict';
    document.querySelectorAll('.hero-assembly-annotated').forEach(root => {
        if (root.dataset.leadersReady) return;
        const paths = [...root.querySelectorAll('[data-tech-leader]')];
        if (!paths.length) return;
        let frame = null, destroyed = false;
        const layout = () => {
            frame = null;
            if (destroyed) return;
            const bounds = root.getBoundingClientRect();
            if (!bounds.width || !bounds.height) return;
            const sx = 1200 / bounds.width, sy = 1091 / bounds.height;
            paths.forEach(path => {
                const key = path.dataset.techLeader;
                const label = root.querySelector(`.hero-tech-${key}`);
                if (!label) return;
                const box = label.getBoundingClientRect();
                const anchorX = key === 'fdm' ? box.right + 4
                    : key === 'cnc' ? box.left + 4 : box.left + box.width / 2;
                const x = (anchorX - bounds.left) * sx;
                const top = key === 'resin' || key === 'fdm';
                const y = ((top ? box.bottom + 6 : box.top - 6) - bounds.top) * sy;
                const tx = Number(path.dataset.targetX), ty = Number(path.dataset.targetY);
                path.setAttribute('d', `M${x} ${y} L${tx} ${ty}`);
            });
        };
        const schedule = () => {
            if (!destroyed && frame === null) frame = window.requestAnimationFrame(layout);
        };
        const observer = typeof window.ResizeObserver === 'function' ? new window.ResizeObserver(schedule) : null;
        observer?.observe(root);
        paths.forEach(path => {
            const label = root.querySelector(`.hero-tech-${path.dataset.techLeader}`);
            if (label) observer?.observe(label);
        });
        window.addEventListener('resize', schedule);
        document.fonts?.ready.then(schedule);
        const dispose = event => {
            if (event.persisted) return;
            destroyed = true;
            if (frame !== null) window.cancelAnimationFrame(frame);
            observer?.disconnect();
            window.removeEventListener('resize', schedule);
            window.removeEventListener('pagehide', dispose);
        };
        window.addEventListener('pagehide', dispose);
        root.dataset.leadersReady = 'true';
        schedule();
    });
})();
