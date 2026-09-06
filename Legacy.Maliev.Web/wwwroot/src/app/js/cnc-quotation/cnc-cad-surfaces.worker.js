(function (root) {
    'use strict';

    // These are untrusted analytic hints in the STEP entity's LOCAL coordinates,
    // not B-rep faces or verified machining geometry. No assembly/mapped-item
    // transform, trimming, face orientation or topology is resolved here. Callers
    // must match each hint strictly against the imported CAD triangles before use.
    // Only documents whose assigned length-unit contexts are explicitly millimetres
    // are accepted. Unused unit definitions do not select the model's units.
    var MAX_TEXT = 16 * 1024 * 1024;
    var MAX_STATEMENT = 64 * 1024;
    var MAX_ENTITIES = 200000;
    var MAX_CANDIDATES = 4096;
    var MAX_DEPTH = 16;
    var relevant = /^(?:CYLINDRICAL_SURFACE|SPHERICAL_SURFACE|TOROIDAL_SURFACE|CONICAL_SURFACE|PLANE|B_SPLINE_SURFACE_WITH_KNOTS|FACE_SURFACE|ADVANCED_FACE|FACE_OUTER_BOUND|FACE_BOUND|POLY_LOOP|EDGE_LOOP|ORIENTED_EDGE|EDGE_CURVE|VERTEX_POINT|AXIS2_PLACEMENT_3D|CARTESIAN_POINT|DIRECTION|GLOBAL_UNIT_ASSIGNED_CONTEXT)\s*\(/;

    function statements(text) {
        var result = [], current = '', quoted = false, comment = false;
        for (var i = 0; i < text.length; i += 1) {
            var ch = text[i], next = text[i + 1];
            if (comment) {
                if (ch === '*' && next === '/') { comment = false; i += 1; }
                continue;
            }
            if (!quoted && ch === '/' && next === '*') { comment = true; current += ' '; i += 1; continue; }
            if (ch === "'") {
                current += ch;
                if (quoted && next === "'") { current += next; i += 1; }
                else { quoted = !quoted; }
            } else if (ch === ';' && !quoted) {
                result.push(current.trim()); current = '';
                if (result.length > MAX_ENTITIES + 1000) { return null; }
            } else { current += ch; }
            if (current.length > MAX_STATEMENT) { return null; }
        }
        return quoted || comment || current.trim() ? null : result;
    }

    function calls(body) {
        var tokens = [], offset = 0;
        while (offset < body.length) {
            var tail = body.slice(offset), match;
            if ((match = /^\s+/.exec(tail))) { offset += match[0].length; continue; }
            if (tail[0] === "'") {
                var end = offset + 1, closed = false;
                for (; end < body.length; end += 1) {
                    if (body[end] !== "'") { continue; }
                    if (body[end + 1] === "'") { end += 1; continue; }
                    closed = true; break;
                }
                if (!closed) { return null; }
                // Names are deliberately not interpreted or evaluated.
                tokens.push({ name: true }); offset = end + 1;
            } else if ((match = /^#([0-9]+)/.exec(tail))) {
                var id = Number(match[1]);
                if (!Number.isSafeInteger(id) || id <= 0) { return null; }
                tokens.push({ ref: id }); offset += match[0].length;
            } else if ((match = /^[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[Ee][+-]?[0-9]+)?/.exec(tail))) {
                var value = Number(match[0]);
                if (!Number.isFinite(value)) { return null; }
                tokens.push(value); offset += match[0].length;
            } else if ((match = /^(?:\.[A-Z_][A-Z_0-9]*\.|[A-Z_][A-Z_0-9]*|[(),$*])/.exec(tail))) {
                tokens.push(match[0]); offset += match[0].length;
            } else { return null; }
        }
        var position = 0, failed = false;
        function list(depth) {
            if (depth > MAX_DEPTH || tokens[position++] !== '(') { failed = true; return []; }
            var values = [];
            if (tokens[position] === ')') { position += 1; return values; }
            while (position < tokens.length) {
                var token = tokens[position];
                if (token === '(') { values.push(list(depth + 1)); }
                else if (token === ')' || token === ',') { failed = true; return []; }
                else { values.push(token); position += 1; }
                if (failed) { return []; }
                if (tokens[position] === ')') { position += 1; return values; }
                if (tokens[position++] !== ',') { failed = true; return []; }
            }
            failed = true; return [];
        }
        var complex = tokens[0] === '(';
        if (complex) { position += 1; }
        var output = Object.create(null);
        while (position < tokens.length && tokens[position] !== ')') {
            var type = tokens[position++];
            if (typeof type !== 'string' || !/^[A-Z_][A-Z_0-9]*$/.test(type)
                || Object.prototype.hasOwnProperty.call(output, type)) { return null; }
            output[type] = list(0);
            if (failed || (!complex && position < tokens.length)) { return null; }
        }
        if (complex && tokens[position++] !== ')') { return null; }
        return position === tokens.length ? output : null;
    }

    function isMillimetres(entities) {
        var assigned = 0, valid = true;
        entities.forEach(function (entity) {
            var context = entity.GLOBAL_UNIT_ASSIGNED_CONTEXT;
            if (!context) { return; }
            assigned += 1;
            if (context.length !== 1 || !Array.isArray(context[0]) || context[0].length === 0) { valid = false; return; }
            var lengths = 0;
            context[0].forEach(function (reference) {
                var unit = reference && entities.get(reference.ref);
                if (!unit) { valid = false; return; }
                if (unit.LENGTH_UNIT) {
                    lengths += 1;
                    var si = unit.SI_UNIT;
                    if (unit.LENGTH_UNIT.length !== 0 || unit.CONVERSION_BASED_UNIT
                        || !si || si.length !== 2 || si[0] !== '.MILLI.' || si[1] !== '.METRE.') { valid = false; }
                } else if (!unit.PLANE_ANGLE_UNIT && !unit.SOLID_ANGLE_UNIT) { valid = false; }
            });
            if (lengths !== 1) { valid = false; }
        });
        return valid && assigned > 0;
    }

    function vector(entity, type) {
        var args = entity && entity[type], values = args && args[1];
        if (!args || args.length !== 2 || !args[0] || args[0].name !== true
            || !Array.isArray(values) || values.length !== 3
            || !values.every(function (value) { return typeof value === 'number' && Number.isFinite(value); })) { return null; }
        return { x: values[0], y: values[1], z: values[2] };
    }

    function normalize(value) {
        if (!value) { return null; }
        var length = Math.hypot(value.x, value.y, value.z);
        return Number.isFinite(length) && length > 0
            ? { x: value.x / length, y: value.y / length, z: value.z / length } : null;
    }

    function placement(entity, entities) {
        var args = entity && entity.AXIS2_PLACEMENT_3D;
        if (!args || args.length !== 4 || !args[0] || args[0].name !== true) { return null; }
        var center = vector(entities.get(args[1] && args[1].ref), 'CARTESIAN_POINT');
        var axis = args[2] === '$' ? { x: 0, y: 0, z: 1 }
            : normalize(vector(entities.get(args[2] && args[2].ref), 'DIRECTION'));
        if (!center || !axis) { return null; }
        // The reference direction determines the parameter seam, not the support
        // of these rotational surfaces. Validate explicit directions anyway.
        if (args[3] !== '$') {
            var reference = normalize(vector(entities.get(args[3] && args[3].ref), 'DIRECTION'));
            if (!reference || Math.hypot(axis.y * reference.z - axis.z * reference.y,
                axis.z * reference.x - axis.x * reference.z,
                axis.x * reference.y - axis.y * reference.x) < 1e-12) { return null; }
        }
        return { centerMm: center, axis: axis };
    }

    function parseStep(text) {
        if (typeof text !== 'string' || text.length > MAX_TEXT) { return []; }
        var chunks = statements(text);
        if (!chunks || chunks[0] !== 'ISO-10303-21' || chunks[chunks.length - 1] !== 'END-ISO-10303-21') { return []; }
        var entities = new Map(), ids = new Set(), inData = false;
        for (var i = 1; i < chunks.length - 1; i += 1) {
            var chunk = chunks[i];
            if (chunk === 'DATA') { if (inData) { return []; } inData = true; continue; }
            if (chunk === 'ENDSEC') { inData = false; continue; }
            if (chunk[0] !== '#') { continue; }
            if (!inData) { return []; }
            var match = /^#([0-9]+)\s*=\s*([\s\S]+)$/.exec(chunk);
            if (!match) { return []; }
            var id = Number(match[1]), body = match[2];
            if (!Number.isSafeInteger(id) || id <= 0 || ids.has(id) || ids.size >= MAX_ENTITIES) { return []; }
            ids.add(id);
            if (!relevant.test(body) && !(body[0] === '(' && /(?:UNIT|GLOBAL_UNIT_ASSIGNED_CONTEXT)\s*\(/.test(body))) { continue; }
            var parsed = calls(body);
            if (!parsed) { return []; }
            entities.set(id, parsed);
        }
        if (inData || !isMillimetres(entities)) { return []; }
        var result = [], exceeded = false;
        entities.forEach(function (entity, id) {
            ['CYLINDRICAL_SURFACE', 'SPHERICAL_SURFACE', 'TOROIDAL_SURFACE', 'CONICAL_SURFACE'].forEach(function (type, index) {
                var args = entity[type];
                if (!args || args.length !== (index >= 2 ? 4 : 3) || !args[0] || args[0].name !== true
                    || typeof args[2] !== 'number' || !(args[2] > 0)
                    || (index >= 2 && (typeof args[3] !== 'number' || !(args[3] > 0)))
                    || (index === 3 && args[3] >= Math.PI / 2)) { return; }
                var frame = placement(entities.get(args[1] && args[1].ref), entities);
                if (!frame) { return; }
                if (result.length >= MAX_CANDIDATES) { exceeded = true; return; }
                var candidate = { kind: ['cylinder', 'sphere', 'torus', 'cone'][index], sourceId: id,
                    centerMm: frame.centerMm, axis: frame.axis, radiusMm: args[2] };
                if (index === 2) { candidate.minorRadiusMm = args[3]; }
                if (index === 3) { candidate.halfAngleRadians = args[3]; }
                result.push(candidate);
            });
        });
        if (exceeded) { return []; }

        var supportBySourceId = new Map();
        result.forEach(function (candidate) { supportBySourceId.set(candidate.sourceId, candidate); });
        entities.forEach(function (entity, id) {
            var args = entity.PLANE;
            if (!args || args.length !== 2 || !args[0] || args[0].name !== true) { return; }
            var frame = placement(entities.get(args[1] && args[1].ref), entities);
            if (frame) { supportBySourceId.set(id, { kind: 'plane', sourceId: id,
                centerMm: frame.centerMm, axis: frame.axis }); }
        });

        function loop(bound) {
            var boundArgs = bound && (bound.FACE_OUTER_BOUND || bound.FACE_BOUND);
            var poly = entities.get(boundArgs && boundArgs[1] && boundArgs[1].ref);
            var refs = poly && poly.POLY_LOOP && poly.POLY_LOOP[1];
            if (Array.isArray(refs)) {
                var polyVertices = refs.map(function (reference) {
                    return vector(entities.get(reference && reference.ref), 'CARTESIAN_POINT');
                });
                return polyVertices.length >= 3 && polyVertices.every(Boolean)
                    ? { vertices: polyVertices } : null;
            }
            var edgeRefs = poly && poly.EDGE_LOOP && poly.EDGE_LOOP[1];
            if (!Array.isArray(edgeRefs) || edgeRefs.length === 0) { return null; }
            var vertices = [], edgeIds = [];
            for (var edgeIndex = 0; edgeIndex < edgeRefs.length; edgeIndex += 1) {
                var oriented = entities.get(edgeRefs[edgeIndex] && edgeRefs[edgeIndex].ref);
                var orientedArgs = oriented && oriented.ORIENTED_EDGE;
                var edge = entities.get(orientedArgs && orientedArgs[3] && orientedArgs[3].ref);
                var edgeArgs = edge && edge.EDGE_CURVE;
                if (!orientedArgs || orientedArgs.length !== 5 || !edgeArgs || edgeArgs.length !== 5) { return null; }
                var forward = orientedArgs[4] !== '.F.';
                var vertexRef = forward ? edgeArgs[1] : edgeArgs[2];
                var vertex = entities.get(vertexRef && vertexRef.ref);
                var vertexArgs = vertex && vertex.VERTEX_POINT;
                var pointEntity = entities.get(vertexArgs && vertexArgs[1] && vertexArgs[1].ref);
                var point = vector(pointEntity, 'CARTESIAN_POINT');
                if (!point) { return null; }
                vertices.push(point); edgeIds.push(orientedArgs[3].ref);
            }
            return { vertices: vertices, edgeIds: edgeIds };
        }

        var faces = [];
        function referencedPoints(value, output, visited) {
            if (Array.isArray(value)) {
                value.forEach(function (item) { referencedPoints(item, output, visited); });
                return;
            }
            if (!value || !Number.isSafeInteger(value.ref) || visited.has(value.ref)) { return; }
            visited.add(value.ref);
            var referenced = entities.get(value.ref);
            var coordinate = vector(referenced, 'CARTESIAN_POINT');
            if (coordinate) { output.push(coordinate); return; }
            if (referenced) {
                Object.keys(referenced).forEach(function (key) {
                    referencedPoints(referenced[key], output, visited);
                });
            }
        }
        entities.forEach(function (entity, id) {
            var args = entity.FACE_SURFACE || entity.ADVANCED_FACE;
            if (!args || args.length !== 4 || !args[0] || args[0].name !== true || !Array.isArray(args[1])) { return; }
            var loops = args[1].map(function (reference) { return loop(entities.get(reference && reference.ref)); }).filter(Boolean);
            var supportEntity = entities.get(args[2] && args[2].ref);
            var support = supportBySourceId.get(args[2] && args[2].ref)
                || (supportEntity && supportEntity.B_SPLINE_SURFACE_WITH_KNOTS
                    ? { kind: 'swept', sourceId: args[2].ref }
                    : { kind: 'unknown' });
            if (support.kind === 'swept') {
                var matchVertices = [];
                referencedPoints(supportEntity.B_SPLINE_SURFACE_WITH_KNOTS,
                    matchVertices, new Set());
                support = Object.assign({}, support, { matchVertices: matchVertices });
            }
            faces.push(Object.assign({}, support, { sourceId: id, faceIndex: faces.length, loops: loops,
                orientation: args[3] === '.F.' ? 'reversed' : 'forward' }));
        });
        return faces.length ? faces : result;
    }

    root.CncCadSurfaces = Object.freeze({ parseStep: parseStep });
}(typeof self !== 'undefined' ? self : globalThis));
