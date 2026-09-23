(function () {
    'use strict';

    var sample = document.getElementById('scanning-sample');
    if (!sample) { return; }
    var tablist = sample.querySelector('[data-proof-tablist]');
    if (!tablist) { return; }
    var tabs = Array.from(tablist.querySelectorAll('[data-proof-tab]'));
    var panels = tabs.map(function (tab) { return document.getElementById(tab.getAttribute('aria-controls')); });
    if (tabs.length !== 3 || panels.some(function (panel) { return !panel; })) { return; }

    function activate(index, focus) {
        tabs.forEach(function (tab, position) {
            var selected = position === index;
            tab.setAttribute('aria-selected', String(selected));
            tab.tabIndex = selected ? 0 : -1;
            panels[position].hidden = !selected;
        });
        if (focus) { tabs[index].focus(); }
    }

    function indexForHash() {
        var id = decodeURIComponent(window.location.hash.slice(1));
        var target = id && document.getElementById(id);
        return target ? panels.findIndex(function (panel) { return panel === target || panel.contains(target); }) : -1;
    }

    panels.forEach(function (panel, index) {
        panel.setAttribute('role', 'tabpanel');
        panel.setAttribute('aria-labelledby', tabs[index].id);
        panel.tabIndex = 0;
    });
    var initial = indexForHash();
    activate(initial < 0 ? 0 : initial, false);
    sample.setAttribute('data-proof-tabs-ready', '');
    tablist.hidden = false;

    tabs.forEach(function (tab, index) {
        tab.addEventListener('click', function () { activate(index, false); });
    });
    tablist.addEventListener('keydown', function (event) {
        var index = tabs.indexOf(event.target);
        if (index < 0) { return; }
        var next;
        switch (event.key) {
            case 'ArrowRight': next = (index + 1) % tabs.length; break;
            case 'ArrowLeft': next = (index + tabs.length - 1) % tabs.length; break;
            case 'Home': next = 0; break;
            case 'End': next = tabs.length - 1; break;
            default: return;
        }
        event.preventDefault();
        activate(next, true);
    });
    window.addEventListener('hashchange', function () {
        var index = indexForHash();
        if (index >= 0) { activate(index, false); }
    });
}());
