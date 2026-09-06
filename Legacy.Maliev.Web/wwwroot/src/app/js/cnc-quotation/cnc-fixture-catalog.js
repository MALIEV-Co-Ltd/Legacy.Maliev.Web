(function (root) {
    'use strict';

    var VERSION = 'cnc-local-fixtures-2026-09-05-v2';
    var FIXTURES = Object.freeze({
        'vise-100-standard': Object.freeze({ id: 'vise-100-standard', kind: 'machine_vise',
            jawWidthMm: 100, maximumOpeningMm: 100, jawHeightMm: 25,
            minimumGripMm: 3, maximumToolReachMm: 120 })
    });

    function values(value) { return Array.isArray(value) ? value : []; }
    function point(value) { return value && Number.isFinite(value.x) && Number.isFinite(value.y) && Number.isFinite(value.z); }
    function clone(value) { return value == null ? value : JSON.parse(JSON.stringify(value)); }
    function normalize(axis) { if (!point(axis)) { return null; } var length = Math.hypot(axis.x, axis.y, axis.z); return length > 0 ? { x: axis.x / length, y: axis.y / length, z: axis.z / length } : null; }
    function cardinal(axis) { axis = normalize(axis); if (!axis) { return null; } var name = ['x', 'y', 'z'].sort(function (a, b) { return Math.abs(axis[b]) - Math.abs(axis[a]); })[0]; return Math.abs(axis[name]) > 0.999999 ? { name: name, sign: axis[name] < 0 ? -1 : 1 } : null; }
    function bounds(value) { return value && point(value.minimum) && point(value.maximum); }

    function resolve(fixtureId) { return FIXTURES[fixtureId] ? clone(FIXTURES[fixtureId]) : null; }

    function capability(fixtureId, orientation, clampFaces) {
        var fixture = FIXTURES[fixtureId], axis = normalize(orientation), card = cardinal(axis);
        var face = values(clampFaces)[0], volume = face && face.validationVolume;
        if (!fixture || !axis || !card || !bounds(volume)) { return null; }
        var obstacle = clone(volume);
        ['x', 'y', 'z'].filter(function (name) { return name !== card.name; }).forEach(function (name) {
            obstacle.minimum[name] -= 2; obstacle.maximum[name] += 2;
        });
        if (card.sign > 0) {
            obstacle.maximum[card.name] = volume.minimum[card.name] - 0.1;
            obstacle.minimum[card.name] = obstacle.maximum[card.name] - fixture.jawHeightMm;
        } else {
            obstacle.minimum[card.name] = volume.maximum[card.name] + 0.1;
            obstacle.maximum[card.name] = obstacle.minimum[card.name] + fixture.jawHeightMm;
        }
        return { catalogVersion: VERSION, fixtureId: fixture.id, maximumToolReachMm: fixture.maximumToolReachMm,
            accessAxes: [axis], obstacles: [obstacle] };
    }

    root.CncFixtureCatalog = Object.freeze({ contract: 'CncFixtureCatalog.v2', version: VERSION,
        resolve: resolve, capability: capability });
}(typeof self !== 'undefined' ? self : window));
