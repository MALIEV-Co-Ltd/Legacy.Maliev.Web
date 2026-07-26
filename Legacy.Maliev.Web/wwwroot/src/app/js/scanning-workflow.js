(function () {
    'use strict';

    document.documentElement.classList.add('js');

    var timelines = Array.prototype.slice.call(document.querySelectorAll('[data-scanning-workflow]'));
    if (!timelines.length) {
        return;
    }

    var reducedMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    function revealTimeline(timeline) {
        if (timeline.dataset.scanningWorkflowRevealed === 'true') {
            return;
        }

        timeline.dataset.scanningWorkflowRevealed = 'true';
        timeline.classList.add('is-active');

        var steps = Array.prototype.slice.call(timeline.querySelectorAll('[data-scanning-step]'));
        steps.forEach(function (step, index) {
            var reveal = function () {
                step.classList.add('is-visible');
            };

            if (reducedMotion) {
                reveal();
            } else {
                window.setTimeout(reveal, index * 140);
            }
        });
    }

    if (reducedMotion || !('IntersectionObserver' in window)) {
        timelines.forEach(revealTimeline);
        return;
    }

    var observer = new IntersectionObserver(function (entries, currentObserver) {
        entries.forEach(function (entry) {
            if (!entry.isIntersecting) {
                return;
            }

            revealTimeline(entry.target);
            currentObserver.unobserve(entry.target);
        });
    }, { rootMargin: '0px 0px -12% 0px', threshold: 0.18 });

    timelines.forEach(function (timeline) {
        observer.observe(timeline);
    });
}());
