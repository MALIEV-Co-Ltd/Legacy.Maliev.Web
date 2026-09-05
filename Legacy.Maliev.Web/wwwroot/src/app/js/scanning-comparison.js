(function () {
    'use strict';

    document.querySelectorAll('[data-scanning-comparison]').forEach(function (comparison) {
        var stage = comparison.querySelector('[data-comparison-stage]');
        var handle = comparison.querySelector('[data-comparison-handle]');
        var buttons = comparison.querySelectorAll('[data-comparison-mode]');
        var activePointer = null;
        var pointerOffset = 0;
        var position = 50;

        function updatePosition(value) {
            position = Math.max(0, Math.min(100, Math.round(value)));
            comparison.style.setProperty('--comparison-position', position + '%');
            handle.setAttribute('aria-valuenow', String(position));
            handle.setAttribute('aria-valuetext', position + '% ' + comparison.dataset.scanLabel + ', ' + (100 - position) + '% ' + comparison.dataset.cadLabel);
        }

        function endDrag() {
            if (activePointer !== null && stage.hasPointerCapture(activePointer)) {
                stage.releasePointerCapture(activePointer);
            }
            activePointer = null;
        }

        function setMode(mode) {
            endDrag();
            comparison.dataset.mode = mode;
            handle.hidden = mode !== 'compare';
            buttons.forEach(function (button) {
                button.setAttribute('aria-pressed', String(button.dataset.comparisonMode === mode));
            });
        }

        function moveDivider(event) {
            var bounds = stage.getBoundingClientRect();
            if (bounds.width > 0) {
                updatePosition((event.clientX - bounds.left - pointerOffset) / bounds.width * 100);
            }
        }

        handle.addEventListener('keydown', function (event) {
            handle.removeAttribute('data-pointer-focus');
            var next = position;
            switch (event.key) {
                case 'ArrowLeft': case 'ArrowDown': next -= 1; break;
                case 'ArrowRight': case 'ArrowUp': next += 1; break;
                case 'PageDown': next -= 10; break;
                case 'PageUp': next += 10; break;
                case 'Home': next = 0; break;
                case 'End': next = 100; break;
                default: return;
            }
            event.preventDefault();
            updatePosition(next);
        });
        buttons.forEach(function (button) {
            button.addEventListener('click', function () { setMode(button.dataset.comparisonMode); });
        });
        handle.addEventListener('blur', function () { handle.removeAttribute('data-pointer-focus'); });
        stage.addEventListener('dragstart', function (event) { event.preventDefault(); });
        stage.addEventListener('pointerdown', function (event) {
            if (comparison.dataset.mode !== 'compare' || !event.isPrimary || event.button !== 0) { return; }
            event.preventDefault();
            handle.setAttribute('data-pointer-focus', '');
            activePointer = event.pointerId;
            var bounds = stage.getBoundingClientRect();
            pointerOffset = handle.contains(event.target) ? event.clientX - bounds.left - position / 100 * bounds.width : 0;
            handle.focus({ preventScroll: true });
            stage.setPointerCapture(event.pointerId);
            moveDivider(event);
        });
        stage.addEventListener('pointermove', function (event) {
            if (event.pointerId === activePointer) {
                event.preventDefault();
                moveDivider(event);
            }
        });
        stage.addEventListener('pointerup', function (event) {
            if (event.pointerId === activePointer) { moveDivider(event); endDrag(); }
        });
        stage.addEventListener('pointercancel', function (event) {
            if (event.pointerId === activePointer) { endDrag(); }
        });
        stage.addEventListener('lostpointercapture', function (event) {
            if (event.pointerId === activePointer) { activePointer = null; }
        });
        updatePosition(position);
        setMode('compare');
        comparison.querySelector('[data-comparison-controls]').hidden = false;
    });
}());
