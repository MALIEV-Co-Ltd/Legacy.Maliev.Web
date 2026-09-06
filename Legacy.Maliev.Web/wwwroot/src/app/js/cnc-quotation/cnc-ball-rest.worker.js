(function (root) {
    'use strict';
    var add = CncAdd, sub = CncSubtract, scale = CncScale, dot = CncDot;
    var length = CncLength, normalize = CncNormalize;
    var tolerance = 1e-5;
    // Surface reconstruction uncertainty, not a CAM clearance guarantee. STEP
    // trim tessellation can drift by fractions of a micrometre from its surface.
    var surfaceFitMm = .001;
    function finiteVector(v) {
        return v && [v.x, v.y, v.z].every(Number.isFinite);
    }
    function clamp(v) { return Math.max(-1, Math.min(1, v)); }
    function radial(point, hint) {
        var delta = sub(point, hint.centerMm);
        return sub(delta, scale(hint.axis, dot(delta, hint.axis)));
    }
    function medial(point, hint) {
        if (hint.kind === 'sphere') { return hint.centerMm; }
        if (hint.kind === 'cylinder') {
            return add(hint.centerMm, scale(hint.axis, dot(sub(point, hint.centerMm), hint.axis)));
        }
        var unit = normalize(radial(point, hint));
        return unit ? add(hint.centerMm, scale(unit, hint.radiusMm)) : null;
    }
    function radius(hint) { return hint.kind === 'torus' ? hint.minorRadiusMm : hint.radiusMm; }
    function validHint(hint) {
        return hint && ['cylinder', 'sphere', 'torus'].indexOf(hint.kind) >= 0
            && finiteVector(hint.centerMm) && Number.isFinite(hint.radiusMm) && hint.radiusMm > 0
            && (hint.kind === 'sphere' || (finiteVector(hint.axis) && length(hint.axis) > 1e-8))
            && (hint.kind !== 'torus' || (Number.isFinite(hint.minorRadiusMm)
                && hint.minorRadiusMm > 0 && hint.radiusMm > hint.minorRadiusMm));
    }
    function matches(record, hint, fitTolerance, normalAlignment, outward) {
        var r = radius(hint);
        if (!record.vertices.every(function (p) {
            var m = medial(p, hint);
            return m && Math.abs(length(sub(p, m)) - r) <= (fitTolerance || surfaceFitMm);
        })) { return false; }
        var m = medial(record.centroid, hint);
        var inward = m && normalize(sub(m, record.centroid));
        return inward && dot(inward, record.normal) * (outward ? -1 : 1) >= (normalAlignment || 0.95);
    }
    // A spherical cone bounds the entire short arc between tessellation vertices,
    // not just its chord. Broad/ambiguous patches fail closed.
    function coneUpper(units, direction) {
        var center = normalize(units.reduce(function (sum, u) { return add(sum, u); }, { x: 0, y: 0, z: 0 }));
        if (!center) { return Infinity; }
        var half = Math.max.apply(null, units.map(function (u) { return Math.acos(clamp(dot(center, u))); }));
        if (half >= Math.PI / 2) { return Infinity; }
        var magnitude = length(direction);
        if (magnitude < 1e-12) { return 0; }
        var angle = Math.acos(clamp(dot(center, direction) / magnitude));
        return magnitude * Math.cos(Math.max(0, angle - half));
    }
    function trigUpper(angles, cosine, sine) {
        var base = angles[0];
        angles = angles.map(function (a) { return base + Math.atan2(Math.sin(a - base), Math.cos(a - base)); });
        var low = Math.min.apply(null, angles), high = Math.max.apply(null, angles);
        if (high - low >= Math.PI) { return Infinity; }
        var peak = Math.atan2(sine, cosine);
        var values = [cosine * Math.cos(low) + sine * Math.sin(low), cosine * Math.cos(high) + sine * Math.sin(high)];
        for (var k = -2; k <= 2; k += 1) {
            var a = peak + k * Math.PI * 2;
            if (a >= low && a <= high) { values.push(Math.hypot(cosine, sine)); }
        }
        return Math.max.apply(null, values);
    }
    function patchUpper(record, hint, direction) {
        var base = dot(hint.centerMm, direction), r = radius(hint);
        if (hint.kind === 'sphere') {
            return base + r * coneUpper(record.vertices.map(function (p) { return normalize(sub(p, hint.centerMm)); }), direction);
        }
        var axial = dot(hint.axis, direction);
        var projected = sub(direction, scale(hint.axis, axial));
        var units = record.vertices.map(function (p) { return normalize(radial(p, hint)); });
        var first = units[0], second = CncCross(hint.axis, first);
        var upper = trigUpper(units.map(function (u) { return Math.atan2(dot(u, second), dot(u, first)); }),
            dot(first, projected), dot(second, projected));
        if (hint.kind === 'cylinder') {
            return base + Math.max.apply(null, record.vertices.map(function (p) {
                return dot(sub(p, hint.centerMm), hint.axis) * axial;
            })) + r * upper;
        }
        var angles = record.vertices.map(function (p) {
            var delta = sub(p, hint.centerMm);
            return Math.atan2(dot(delta, hint.axis), length(radial(p, hint)) - hint.radiusMm);
        });
        // A regular ring torus has positive major + minor*cos(phi), so replacing
        // u.direction by its upper bound cannot underestimate the surface bound.
        return base + hint.radiusMm * upper + r * trigUpper(angles, upper, axial);
    }
    function raySegmentDistanceSquared(p, d, a, b) {
        var e = sub(b, a), w = sub(p, a), cc = dot(e, e), bb = dot(d, e), dd = dot(d, w), ee = dot(e, w);
        var best = Infinity;
        function visit(t, u) {
            var v = sub(add(w, scale(d, t)), scale(e, u));
            best = Math.min(best, dot(v, v));
        }
        visit(Math.max(0, -dd), 0);
        visit(Math.max(0, bb - dd), 1);
        visit(0, cc > 0 ? Math.max(0, Math.min(1, ee / cc)) : 0);
        var denominator = cc - bb * bb;
        if (denominator > 1e-12) {
            var t = (bb * ee - cc * dd) / denominator, u = (ee + bb * t) / cc;
            if (t >= 0 && u >= 0 && u <= 1) { visit(t, u); }
        }
        return best;
    }
    function curvedPatchClear(record, hint, center, direction, r, forwardSquaredAllowance) {
        var first = normalize(radial(record.vertices[0], hint)), second = CncCross(hint.axis, first);
        var theta = record.vertices.map(function (p) {
            var u = normalize(radial(p, hint));
            return Math.atan2(dot(u, second), dot(u, first));
        });
        var phi = record.vertices.map(function (p) {
            if (hint.kind === 'cylinder') { return dot(sub(p, hint.centerMm), hint.axis); }
            return Math.atan2(dot(sub(p, hint.centerMm), hint.axis), length(radial(p, hint)) - hint.radiusMm);
        });
        if (hint.kind === 'torus') {
            phi = phi.map(function (a) { return phi[0] + Math.atan2(Math.sin(a - phi[0]), Math.cos(a - phi[0])); });
        }
        var box = [Math.min.apply(null, theta), Math.max.apply(null, theta), Math.min.apply(null, phi), Math.max.apply(null, phi)];
        if (box[1] - box[0] >= Math.PI || (hint.kind === 'torus' && box[3] - box[2] >= Math.PI)) { return false; }
        var transverse = normalize(CncCross(direction, Math.abs(direction.z) < .9 ? { x: 0, y: 0, z: 1 } : { x: 0, y: 1, z: 0 }));
        var other = CncCross(direction, transverse), visited = 0;
        function upper(bounds, d) {
            var u = trigUpper([bounds[0], bounds[1]], dot(first, d), dot(second, d));
            if (hint.kind === 'cylinder') {
                var axial = dot(hint.axis, d);
                return dot(sub(hint.centerMm, center), d) + hint.radiusMm * u
                    + Math.max(bounds[2] * axial, bounds[3] * axial);
            }
            return dot(sub(hint.centerMm, center), d) + hint.radiusMm * u
                + hint.minorRadiusMm * trigUpper([bounds[2], bounds[3]], u, dot(hint.axis, d));
        }
        function intervalDistance(bounds, d) {
            var high = upper(bounds, d), low = -upper(bounds, scale(d, -1));
            return Math.max(low, 0, -high);
        }
        function check(bounds, depth) {
            visited += 1;
            // All points start outside this ball (proved using the analytic tube).
            // Removing at most upper^2 from squared distance stays within tolerance.
            var forward = Math.max(0, upper(bounds, direction));
            if (forward * forward <= forwardSquaredAllowance) { return true; }
            var a = intervalDistance(bounds, transverse), b = intervalDistance(bounds, other);
            if (a * a + b * b >= Math.pow(r - tolerance, 2)) { return true; }
            if (depth >= 18 || visited >= 4096) { return false; }
            var left = bounds.slice(), right = bounds.slice();
            var index = (bounds[1] - bounds[0]) * hint.radiusMm > (bounds[3] - bounds[2])
                * (hint.kind === 'torus' ? hint.minorRadiusMm : 1) ? 0 : 2;
            var middle = (bounds[index] + bounds[index + 1]) / 2;
            left[index + 1] = middle; right[index] = middle;
            return check(left, depth + 1) && check(right, depth + 1);
        }
        return check(box, 0);
    }
    function rayTriangleDistanceSquared(center, direction, record) {
        var delta = sub(center, CncTriangleClosestPoint(center, record)), distance = dot(delta, delta);
        var denominator = dot(record.normal, direction);
        if (Math.abs(denominator) > 1e-10) {
            var t = dot(record.normal, sub(record.vertices[0], center)) / denominator;
            if (t >= 0) {
                var hit = add(center, scale(direction, t));
                if (length(sub(hit, CncTriangleClosestPoint(hit, record))) < 1e-7) { return 0; }
            }
        }
        record.vertices.forEach(function (a, index, vertices) {
            distance = Math.min(distance, raySegmentDistanceSquared(center, direction, a, vertices[(index + 1) % 3]));
        });
        return distance;
    }
    function createVerifier(records, hints, cadFaceRanges) {
        var bySource = new Map(), byRecord = new Map(), sweep = CncToolSweepVerifier(records);
        var outwardByRecord = new Map();
        var stockTopByDirection = new Map();
        var sphereBoundsByDirection = new Map();
        var contactCache = new Map();
        records.forEach(function (record) { bySource.set(record.sourceTriangleIndex, record); });
        var claimed = new Set(), ambiguous = false;
        var faces = (cadFaceRanges || []).map(function (range) {
            if (!Number.isInteger(range.first) || !Number.isInteger(range.last)
                || range.first < 0 || range.last < range.first || range.last - range.first > records.length) { return []; }
            var face = [];
            for (var index = range.first; index <= range.last; index += 1) {
                if (claimed.has(index)) { ambiguous = true; }
                claimed.add(index);
                var record = bySource.get(index);
                if (!record) { return []; }
                face.push(record);
            }
            return face;
        }).filter(function (face) { return face.length >= 3; });
        // Vertex coincidence alone is not CAD provenance: a real planar floor
        // may share a cylinder's vertices. Match a complete curved BREP face.
        if (ambiguous || faces.reduce(function (sum, face) { return sum + face.length; }, 0)
            * (hints || []).length > 3000000) { faces = []; }
        var candidates = (hints || []).filter(validHint).map(function (input) {
            return Object.assign({}, input, { axis: input.kind === 'sphere' ? null : normalize(input.axis) });
        });
        function equivalent(a, b) {
            if (a.kind !== b.kind || Math.abs(radius(a) - radius(b)) > 1e-7) { return false; }
            if (a.kind === 'sphere') { return length(sub(a.centerMm, b.centerMm)) <= 1e-7; }
            if (Math.abs(dot(a.axis, b.axis)) < 1 - 1e-10) { return false; }
            return a.kind === 'cylinder' ? length(radial(b.centerMm, a)) <= 1e-7
                : Math.abs(a.radiusMm - b.radiusMm) <= 1e-7 && length(sub(a.centerMm, b.centerMm)) <= 1e-7;
        }
        function registerFaces(outward, destination) {
        faces.forEach(function (face) {
            var supported = candidates.filter(function (hint) {
                return face.every(function (record) { return matches(record, hint, surfaceFitMm, .8, outward); });
            }).map(function (hint) {
                var error = Math.max.apply(null, face.map(function (record) {
                    return Math.max.apply(null, record.vertices.map(function (point) {
                        return Math.abs(length(sub(point, medial(point, hint))) - radius(hint));
                    }));
                }));
                return { hint: hint, error: error };
            }).sort(function (a, b) { return a.error - b.error; });
            if (!supported.length || supported.some(function (entry, index) {
                return index > 0 && entry.error <= supported[0].error + 1e-8 && !equivalent(entry.hint, supported[0].hint);
            })) { return; }
            var hint = supported[0].hint;
                // Imported trim vertices can depart slightly from the analytic
                // surface. They may establish face identity but are never exempt
                // from collision checks unless they pass the stricter local normal.
                var matched = face.filter(function (record) { return matches(record, hint, surfaceFitMm, .95, outward); });
                if (matched.length < 3 || !matched.some(function (record) { return dot(record.normal, matched[0].normal) < .9999; })) { return; }
                matched.forEach(function (record) {
                    if (!destination.has(record)) { destination.set(record, []); }
                    destination.get(record).push(hint);
                });
        });
        }
        registerFaces(false, byRecord);
        registerFaces(true, outwardByRecord);
        function sphereBounds(direction) {
            var key = [direction.x, direction.y, direction.z].join(',');
            if (sphereBoundsByDirection.has(key)) { return sphereBoundsByDirection.get(key); }
            var first = normalize(CncCross(direction, Math.abs(direction.z) < .9
                ? { x: 0, y: 0, z: 1 } : { x: 0, y: 1, z: 0 }));
            var second = CncCross(direction, first);
            var projected = { first: first, second: second, records: records.map(function (record) {
                var bounds = { minimumX: Infinity, maximumX: -Infinity, minimumY: Infinity,
                    maximumY: -Infinity, maximumDepth: -Infinity };
                record.vertices.forEach(function (point) {
                    var x = dot(point, first), y = dot(point, second), z = dot(point, direction);
                    bounds.minimumX = Math.min(bounds.minimumX, x - tolerance);
                    bounds.maximumX = Math.max(bounds.maximumX, x + tolerance);
                    bounds.minimumY = Math.min(bounds.minimumY, y - tolerance);
                    bounds.maximumY = Math.max(bounds.maximumY, y + tolerance);
                    bounds.maximumDepth = Math.max(bounds.maximumDepth, z + tolerance);
                });
                return bounds;
            }) };
            sphereBoundsByDirection.set(key, projected);
            return projected;
        }
        function clear(center, direction, tool) {
            var r = tool.diameterMm / 2;
            if (!sweep(center, direction, { radius: tool.shankDiameterMm / 2, start: tool.underNeckLengthMm - r }, null)
                || !sweep(center, direction, { radius: tool.holderDiameterMm / 2, start: tool.reachMm - r }, null)) { return false; }
            var projected = sphereBounds(direction), cx = dot(center, projected.first);
            var cy = dot(center, projected.second), depth = dot(center, direction);
            return records.every(function (record, index) {
                var bounds = projected.records[index];
                var dx = Math.max(bounds.minimumX - cx, 0, cx - bounds.maximumX);
                var dy = Math.max(bounds.minimumY - cy, 0, cy - bounds.maximumY);
                var dz = Math.max(0, depth - bounds.maximumDepth);
                // This lower bound only shortcuts the existing triangle-ray
                // gate. Its box contains the complete triangle, so a rejection
                // would also pass that precise gate. Analytic arcs are not newly
                // excluded: all contacts reaching the analytic test still do so.
                // Expanded boxes and the full radius leave boundary decisions
                // to the unchanged precise test below.
                if (dx * dx + dy * dy + dz * dz >= r * r) { return true; }
                if (rayTriangleDistanceSquared(center, direction, record) >= Math.pow(r - tolerance, 2)) { return true; }
                return (byRecord.get(record) || []).concat(outwardByRecord.get(record) || []).some(function (hint) {
                    var m = medial(center, hint);
                    if (!m) { return false; }
                    var distance = length(sub(center, m));
                    // The full sphere must initially be separated from the
                    // verified tube, either inside its cavity or outside its
                    // solid envelope. A nearby normal alone proves neither.
                    var inside = distance + r <= radius(hint) + tolerance;
                    var outside = distance - r >= radius(hint) - tolerance;
                    if (!inside && !outside) { return false; }
                    var penetration = Math.max(0, inside ? distance + r - radius(hint) : radius(hint) + r - distance);
                    var forwardSquaredAllowance = Math.max(0, Math.pow(r - penetration, 2) - Math.pow(r - tolerance, 2));
                    if (hint.kind === 'cylinder' && Math.abs(dot(hint.axis, direction)) >= 1 - 1e-10) { return true; }
                    // The smooth patch starts outside the ball and lies behind its
                    // center; withdrawal increases every point's squared distance.
                    return patchUpper(record, hint, direction) <= dot(center, direction) + Math.sqrt(forwardSquaredAllowance)
                        || (['torus', 'cylinder'].indexOf(hint.kind) >= 0
                            && curvedPatchClear(record, hint, center, direction, r, forwardSquaredAllowance));
                });
            });
        }
        function computeContact(sample, direction, tool) {
            if (!sample || !finiteVector(sample.contactPosition) || !finiteVector(direction)
                || !tool || tool.family !== 'ball_end_mill'
                || !['diameterMm', 'usableCutLengthMm', 'underNeckLengthMm', 'neckDiameterMm', 'shankDiameterMm', 'reachMm', 'holderDiameterMm']
                    .every(function (key) { return Number.isFinite(tool[key]) && tool[key] > 0; })
                || tool.usableCutLengthMm < tool.diameterMm / 2 || tool.underNeckLengthMm < tool.usableCutLengthMm
                || tool.reachMm < tool.underNeckLengthMm || tool.neckDiameterMm > tool.diameterMm
                || tool.shankDiameterMm < tool.neckDiameterMm || tool.holderDiameterMm < tool.shankDiameterMm) { return null; }
            direction = normalize(direction);
            var record = bySource.get(sample.sourceTriangleIndex);
            if (!direction || !record || dot(record.normal, direction) < -tolerance
                || length(sub(sample.contactPosition, CncTriangleClosestPoint(sample.contactPosition, record))) > tolerance) { return null; }
            var candidates = (byRecord.get(record) || []).filter(function (hint) { return radius(hint) >= tool.diameterMm / 2 - tolerance; });
            for (var i = 0; i < candidates.length; i += 1) {
                var hint = candidates[i], m = medial(sample.contactPosition, hint);
                var outward = m && normalize(sub(sample.contactPosition, m));
                if (!outward) { continue; }
                var center = add(m, scale(outward, Math.max(0, radius(hint) - tool.diameterMm / 2)));
                if (clear(center, direction, tool)) { return { center: center, surfaceKind: hint.kind, sourceId: hint.sourceId,
                    reconstructionUncertaintyMm: surfaceFitMm }; }
            }
            // A chord normal is not the CAD normal. Reconstruct only complete,
            // unambiguously matched outward faces, then retain every existing
            // cutter, shank and holder obstruction check. Intersecting chords
            // require the same verified analytic patch withdrawal proof above.
            var outwardCandidates = outwardByRecord.get(record) || [];
            for (var i = 0; i < outwardCandidates.length; i += 1) {
                var hint = outwardCandidates[i], m = medial(sample.contactPosition, hint);
                var normal = m && normalize(sub(sample.contactPosition, m));
                if (!normal || dot(normal, direction) < -tolerance) { continue; }
                var point = add(m, scale(normal, radius(hint)));
                var center = add(point, scale(normal, tool.diameterMm / 2));
                if (clear(center, direction, tool)) { return { center: center, surfaceKind: hint.kind, sourceId: hint.sourceId,
                    contactPointMm: point, contactNormal: normal,
                    meshProjectionDistanceMm: length(sub(point, sample.contactPosition)),
                    reconstructionUncertaintyMm: surfaceFitMm }; }
            }
            var center = add(sample.contactPosition, scale(record.normal, tool.diameterMm / 2));
            return clear(center, direction, tool) ? { center: center, surfaceKind: 'mesh' } : null;
        }
        function contact(sample, direction, tool) {
            if (!sample || !finiteVector(sample.contactPosition) || !finiteVector(direction) || !tool) {
                return computeContact(sample, direction, tool);
            }
            var key = [sample.sourceTriangleIndex, sample.contactPosition.x, sample.contactPosition.y,
                sample.contactPosition.z, direction.x, direction.y, direction.z, tool.family,
                tool.diameterMm, tool.usableCutLengthMm, tool.underNeckLengthMm, tool.neckDiameterMm,
                tool.shankDiameterMm, tool.reachMm, tool.holderDiameterMm].join('|');
            if (contactCache.has(key)) { return contactCache.get(key); }
            var value = computeContact(sample, direction, tool);
            if (contactCache.size >= 32768) { contactCache.clear(); }
            contactCache.set(key, value);
            return value;
        }
        function handoff(sample, direction, ball, flat, options) {
            var general = options && options.general === true;
            var pose = contact(sample, direction, ball);
            if (!pose || !flat || !['diameterMm', 'usableCutLengthMm', 'shankDiameterMm', 'reachMm', 'holderDiameterMm']
                .every(function (key) { return Number.isFinite(flat[key]) && flat[key] > 0; })
                || (general ? flat.diameterMm < ball.diameterMm : flat.diameterMm <= ball.diameterMm)
                || flat.reachMm < flat.usableCutLengthMm
                || flat.shankDiameterMm < flat.diameterMm || flat.holderDiameterMm < flat.shankDiameterMm) { return null; }
            direction = normalize(direction);
            var directionKey = [direction.x, direction.y, direction.z].join('|');
            if (!stockTopByDirection.has(directionKey)) {
                stockTopByDirection.set(directionKey, records.reduce(function (top, record) {
                    return Math.max(top, dot(record.vertices[0], direction), dot(record.vertices[1], direction),
                        dot(record.vertices[2], direction));
                }, -Infinity));
            }
            var modelTop = stockTopByDirection.get(directionKey);
            var explicitStock = general && Object.prototype.hasOwnProperty.call(options, 'stockTopMm');
            if (explicitStock && (!Number.isFinite(options.stockTopMm) || options.stockTopMm < modelTop - tolerance)) { return null; }
            var top = explicitStock ? options.stockTopMm : modelTop;
            var ballRadius = ball.diameterMm / 2, flatRadius = flat.diameterMm / 2;
            if (!general && (dot(pose.center, direction) + ball.underNeckLengthMm - ballRadius <= top + tolerance
                || dot(pose.center, direction) + ball.reachMm - ballRadius <= top + tolerance)) { return null; }
            var first = normalize(CncCross(direction, Math.abs(direction.z) < .9 ? { x: 0, y: 0, z: 1 } : { x: 0, y: 1, z: 0 }));
            var second = CncCross(direction, first), offset = Math.max(0, flatRadius - ballRadius - tolerance);
            // Bounded quote-estimator search. This certifies sampled poses only;
            // it does not establish a collision-free linking/toolpath trajectory.
            // These are search bounds, not cutting depths. The consuming planner
            // must split the certified cap into the selected tool's staged ap.
            // One ball diameter bounds work and avoids inventing unbounded rest
            // roughing. Unsupported points retain their previous operations.
            var caps = general ? [.0625, .125, .25, .375, .5, .75, 1, 1.5, 2]
                .map(function (fraction) { return ballRadius * fraction; }) : [.125, .25, .5, .75];
            for (var cap of caps) {
                for (var fraction of offset > 0 ? [1, .75, .5, .25, 0] : [0]) {
                    for (var angle = 0; angle < (fraction > 0 ? 16 : 1); angle += 1) {
                        var shift = add(scale(first, offset * fraction * Math.cos(angle * Math.PI / 8)),
                            scale(second, offset * fraction * Math.sin(angle * Math.PI / 8)));
                        var tip = add(add(pose.center, shift), scale(direction, -ballRadius + cap - tolerance));
                        // Below the known stock plane, a non-cutting envelope
                        // must fit wholly in the preceding flat's cleared column.
                        // Merely proving final-CAD ball contact cannot prove this.
                        function stockClear(radiusMm, startMm) {
                            var bottom = dot(pose.center, direction) + startMm - ballRadius;
                            return bottom > top + tolerance || (length(shift) + radiusMm <= flatRadius + tolerance
                                && bottom >= dot(tip, direction) - tolerance);
                        }
                        if (general && (!stockClear(ball.neckDiameterMm / 2, ball.usableCutLengthMm)
                            || !stockClear(ball.shankDiameterMm / 2, ball.underNeckLengthMm)
                            || !stockClear(ball.holderDiameterMm / 2, ball.reachMm))) { continue; }
                        if (dot(tip, direction) + flat.usableCutLengthMm <= top + tolerance
                            || dot(tip, direction) + flat.reachMm <= top + tolerance) { continue; }
                        if (!sweep(tip, direction, { radius: flatRadius, start: 0 }, null)
                            || !sweep(tip, direction, { radius: flat.shankDiameterMm / 2, start: flat.usableCutLengthMm }, null)
                            || !sweep(tip, direction, { radius: flat.holderDiameterMm / 2, start: flat.reachMm }, null)) { continue; }
                        return { sampleId: sample.id, sourceTriangleIndex: sample.sourceTriangleIndex,
                            ballCenterMm: pose.center, preparationTipMm: tip, residualAxialCapMm: cap,
                            facedStockTopMm: modelTop, stockTopMm: top, requiresFacing: !explicitStock, requiresPreparation: true,
                            ballToolId: ball.id, preparationToolId: flat.id, preparationDiameterMm: flat.diameterMm,
                            stockClearanceBasis: general ? 'prepared-cylinder-and-stock-top' : 'faced-stock-top',
                            reconstructionUncertaintyMm: surfaceFitMm,
                            method: 'sampled-ball-stock-handoff', camCertain: false };
                    }
                }
            }
            return null;
        }
        function generalHandoff(sample, direction, ball, flat, options) {
            return handoff(sample, direction, ball, flat, Object.assign({}, options || {}, { general: true }));
        }
        function concaveRadiusMm(sourceTriangleIndex) {
            var candidates = byRecord.get(bySource.get(sourceTriangleIndex)) || [];
            if (!candidates.length) { return null; }
            var value = radius(candidates[0]);
            return candidates.every(function (hint) { return Math.abs(radius(hint) - value) <= 1e-7; }) ? value : null;
        }
        return { contact: contact, handoff: handoff, generalHandoff: generalHandoff, concaveRadiusMm: concaveRadiusMm };
    }
    root.CncBallRest = { createVerifier: createVerifier };
}(self));
