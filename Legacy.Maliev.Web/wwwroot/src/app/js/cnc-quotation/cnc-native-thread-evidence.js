(function (root) {
    'use strict';
    // Finite polynomial calculations only. These records carry no source authority;
    // the connected recognizer separately requires the locally verified projection.
    var bits = new DataView(new ArrayBuffer(8));
    function up(x) { if (x === Infinity) { return x; } if (x === 0) { return Number.MIN_VALUE; }
        bits.setFloat64(0, x); var b = bits.getBigUint64(0); bits.setBigUint64(0, x > 0 ? b + 1n : b - 1n); return bits.getFloat64(0); }
    function down(x) { return -up(-x); }
    function point(x) { if (!Number.isFinite(x)) { throw new Error('native_thread_nonfinite'); } return [x, x]; }
    function add(a, b) { return [down(a[0] + b[0]), up(a[1] + b[1])]; }
    function neg(a) { return [-a[1], -a[0]]; }
    function sub(a, b) { return add(a, neg(b)); }
    function mul(a, b) { var v = [a[0] * b[0], a[0] * b[1], a[1] * b[0], a[1] * b[1]]; return [down(Math.min.apply(Math, v)), up(Math.max.apply(Math, v))]; }
    function div(a, b) { if (b[0] <= 0 && b[1] >= 0) { throw new Error('native_thread_singular'); } return mul(a, [down(1 / b[1]), up(1 / b[0])]); }
    function hull(a) { return [Math.min.apply(Math, a.map(function (x) { return x[0]; })), Math.max.apply(Math, a.map(function (x) { return x[1]; }))]; }
    function magnitude(a) { return Math.max(Math.abs(a[0]), Math.abs(a[1])); }
    function vector(a) { return a.map(point); }
    function vadd(a, b) { return a.map(function (x, i) { return add(x, b[i]); }); }
    function vsub(a, b) { return a.map(function (x, i) { return sub(x, b[i]); }); }
    function scale(a, b) { return a.map(function (x) { return mul(x, b); }); }
    function dot(a, b) { return a.reduce(function (s, x, i) { return add(s, mul(x, b[i])); }, point(0)); }
    function cross(a, b) { return [sub(mul(a[1], b[2]), mul(a[2], b[1])), sub(mul(a[2], b[0]), mul(a[0], b[2])), sub(mul(a[0], b[1]), mul(a[1], b[0]))]; }
    function box(rows) { return rows[0].map(function (_, k) { return hull(rows.map(function (p) { return p[k]; })); }); }
    function requireValue(x, code) { if (!x) { throw new Error(code); } }
    function pair(x) { return Array.isArray(x) && x.length === 2 && x.every(Number.isFinite) && x[0] < x[1]; }
    function frame(s) {
        var vectors = [s.axis, s.xDirection, s.yDirection];
        requireValue(s.direct === true && vectors.every(function (v) { return Array.isArray(v) && v.length === 3 && v.every(Number.isFinite); }), 'native_thread_frame_unsupported');
        var a = vector(s.axis), x = vector(s.xDirection), y = vector(s.yDirection);
        requireValue(magnitude(sub(dot(a, a), point(1))) < 1e-12 && magnitude(sub(dot(x, x), point(1))) < 1e-12
            && magnitude(sub(dot(y, y), point(1))) < 1e-12 && magnitude(dot(a, x)) < 1e-12 && magnitude(dot(a, y)) < 1e-12
            && magnitude(dot(x, y)) < 1e-12 && dot(cross(x, y), a)[0] > 1 - 1e-12, 'native_thread_frame_unsupported');
        return { origin: vector(s.origin), axis: a, x: x, y: y };
    }
    function affine(p, range) {
        requireValue(pair(range), 'native_thread_curve_range_unsupported');
        var start, delta, first, last;
        if (p.type === 'line') { start = vector(p.origin); delta = vector(p.direction); first = point(range[0]); last = point(range[1]); }
        else {
            requireValue(p.type === 'bspline' && p.degree === 1 && p.periodic === false && p.rational === false
                && p.poles.length === 2 && p.weights.length === 2 && p.weights.every(function (w) { return w === 1; })
                && p.knots.length === 2 && pair(p.knots) && JSON.stringify(p.multiplicities) === '[2,2]', 'native_thread_curve_unsupported');
            requireValue(range[0] >= p.knots[0] && range[1] <= p.knots[1], 'native_thread_curve_range_unsupported');
            start = vector(p.poles[0]); delta = vsub(vector(p.poles[1]), start);
            first = div(sub(point(range[0]), point(p.knots[0])), sub(point(p.knots[1]), point(p.knots[0])));
            last = div(sub(point(range[1]), point(p.knots[0])), sub(point(p.knots[1]), point(p.knots[0])));
        }
        requireValue(start.length === 2 && delta.length === 2, 'native_thread_curve_dimension');
        var a = vadd(start, scale(delta, first)), b = vadd(start, scale(delta, last)), d = vsub(b, a);
        return { endpoints: [a, b], delta: d, uvBounds: [hull([a[0], b[0]]), hull([a[1], b[1]])] };
    }
    function compatibleBoundaryLead(intervals) {
        requireValue(intervals.length > 0, 'native_thread_lead_incompatible');
        var range = [Math.max.apply(Math, intervals.map(function (v) { return v[0]; })), Math.min.apply(Math, intervals.map(function (v) { return v[1]; }))];
        requireValue(range.every(Number.isFinite) && range[0] <= range[1] && range[0] * range[1] > 0, 'native_thread_lead_incompatible');
        return range;
    }
    function sharedBoundary(face, entry, neighbor, seed, profile, reference, rows) {
        var use = entry.use;
        // Identity is the actual native occurrence on this shared edge. Another
        // helix elsewhere on the same cylinder cannot qualify a circular ramp.
        var matches = seed.observations.filter(function (o) { return o.edgeId === use.edgeId && o.coedgeId === neighbor.coedgeId && o.wireId === neighbor.wireId; });
        requireValue(matches.length === 1, 'native_thread_shared_boundary_not_helical');
        var o = matches[0], lead = o.sourceCompatibleLeadMm;
        requireValue(lead.every(Number.isFinite) && lead[0] <= lead[1] && lead[0] * lead[1] > 0, 'native_thread_shared_boundary_not_helical');
        requireValue(rows.length && rows.every(function (r) { return r.status === 'bounded' && r.faceId === face.faceId && r.edgeId === use.edgeId
            && r.coedgeId === use.coedgeId && r.wireId === entry.wire.wireId && Number.isFinite(r.upperBoundMm) && r.upperBoundMm >= 0; }), 'native_thread_shared_boundary_metric_unavailable');
        var q = affine(use.pcurve, use.range), model = profile.axialAffineModel;
        var start = use.orientation === 1 ? use.endVertexId : use.startVertexId, end = use.orientation === 1 ? use.startVertexId : use.endVertexId;
        requireValue(start && end && start !== end && (start === o.startVertexId && end === o.endVertexId || start === o.endVertexId && end === o.startVertexId), 'native_thread_shared_boundary_continuation');
        var seedEndpoints = start === o.startVertexId ? o.endpointsUV : o.endpointsUV.slice().reverse();
        var offset = dot(vsub(vector(seed.support.origin), vector(reference.origin)), vector(reference.axis));
        var sign = dot(vector(seed.support.axis), vector(reference.axis));
        var maximum = 0, actualAxial = [], expectedAxial = [];
        q.endpoints.forEach(function (uv, i) {
            var z = add(model.valueAtOriginMm, add(mul(model.uSlopeMm, sub(uv[0], point(model.originUV[0]))), mul(model.vSlopeMm, sub(uv[1], point(model.originUV[1])))));
            var expected = add(offset, mul(sign, seedEndpoints[i][1]));
            actualAxial.push(add(z, [-model.representedResidualMm, model.representedResidualMm])); expectedAxial.push(expected);
            maximum = Math.max(maximum, magnitude(sub(z, expected)));
        });
        var actualAdvance = sub(actualAxial[1], actualAxial[0]), expectedAdvance = sub(expectedAxial[1], expectedAxial[0]);
        requireValue(actualAdvance[0] * actualAdvance[1] > 0 && expectedAdvance[0] * expectedAdvance[1] > 0
            && actualAdvance[0] * expectedAdvance[0] > 0, 'native_thread_shared_boundary_continuation');
        // Both complete finite pcurves are affine. Endpoint coefficient bounds
        // therefore enclose their difference everywhere, not sampled agreement.
        // The all-coefficient profile residual includes represented extensions;
        // the native source tubes bind both lifts to this very same 3-D edge.
        var allowance = up(o.sourceErrorMm + Math.max.apply(Math, rows.map(function (r) { return r.upperBoundMm; })) + model.representedResidualMm);
        requireValue(maximum <= allowance, 'native_thread_shared_boundary_continuation');
        return { method: 'actual-native-helix-and-whole-affine-boundary-continuation-v1', edgeId: use.edgeId,
            flankCoedgeId: use.coedgeId, seedCoedgeId: o.coedgeId, seedFaceId: seed.face.faceId,
            sourceCompatibleSignedLeadMm: lead.slice(), maximumAxialMismatchMm: maximum, sourceAllowanceMm: allowance,
            sourceMetricRefs: rows.map(function (r) { return r.obligationId; }).concat(o.sourceMetricRefs || []).sort() };
    }
    function curveSegments(poles, knots, multiplicities, degree) {
        requireValue([1, 3].includes(degree) && knots.length === multiplicities.length && knots.length >= 2
            && knots.every(Number.isFinite) && knots.every(function (k, i) { return !i || k > knots[i - 1]; })
            && multiplicities[0] === degree + 1 && multiplicities[multiplicities.length - 1] === degree + 1
            && multiplicities.every(function (m) { return Number.isInteger(m) && m > 0 && m <= degree + 1; }), 'native_thread_spline_unsupported');
        var u = knots.flatMap(function (k, i) { return Array(multiplicities[i]).fill(k); }), p = poles;
        requireValue(u.length === p.length + degree + 1 && p.length <= 4096, 'native_thread_spline_unsupported');
        for (var j = 1; j < knots.length - 1; j++) {
            var t = knots[j], s = multiplicities[j];
            while (s < degree) {
                var k = u.lastIndexOf(t), q = new Array(p.length + 1);
                for (var i = 0; i <= k - degree; i++) { q[i] = p[i]; }
                for (i = k - s + 1; i < q.length; i++) { q[i] = p[i - 1]; }
                for (i = k - degree + 1; i <= k - s; i++) {
                    var alpha = div(sub(point(t), point(u[i])), sub(point(u[i + degree]), point(u[i])));
                    q[i] = vadd(scale(p[i], alpha), scale(p[i - 1], sub(point(1), alpha)));
                }
                u.splice(k + 1, 0, t); p = q; s++;
            }
        }
        var result = [];
        for (j = degree; j < p.length; j++) { if (u[j + 1] > u[j]) { result.push({ range: [u[j], u[j + 1]], poles: p.slice(j - degree, j + 1) }); } }
        return result;
    }
    function evaluate(p, t) { p = p.slice(); while (p.length > 1) { p = p.slice(1).map(function (x, i) { return vadd(scale(p[i], sub(point(1), t)), scale(x, t)); }); } return p[0]; }
    function derivative(p) { return p.slice(1).map(function (x, i) { return scale(vsub(x, p[i]), point(p.length - 1)); }); }
    function restrict(p, a, b) {
        var lo = evaluate(p, a), hi = evaluate(p, b);
        if (p.length === 2) { return [lo, hi]; }
        var d = derivative(p), step = div(sub(b, a), point(3));
        return [lo, vadd(lo, scale(evaluate(d, a), step)), vsub(hi, scale(evaluate(d, b), step)), hi];
    }
    function radialSquared(p) {
        // Exact Bernstein product identity retains X/Y correlation throughout the
        // entire cubic span. Bounding coordinates independently destroys circles.
        var choose3 = [1, 3, 3, 1], choose6 = [1, 6, 15, 20, 15, 6, 1], coefficients = Array.from({ length: 7 }, function () { return point(0); });
        for (var i = 0; i < 4; i++) { for (var j = 0; j < 4; j++) {
            var weight = div(point(choose3[i] * choose3[j]), point(choose6[i + j]));
            coefficients[i + j] = add(coefficients[i + j], mul(weight, add(mul(p[i][0], p[j][0]), mul(p[i][1], p[j][1]))));
        } }
        return hull(coefficients);
    }
    function trimControls(p, range) {
        if (p.type === 'line' || p.type === 'bspline' && p.degree === 1) { return affine(p, range).endpoints; }
        requireValue(p.type === 'bspline' && p.degree === 3 && p.periodic === false && p.rational === false
            && p.weights.length === p.poles.length && p.weights.every(function (w) { return w === 1; }) && pair(range), 'native_thread_trim_unsupported');
        var segments = curveSegments(p.poles.map(vector), p.knots, p.multiplicities, p.degree), points = [];
        segments.forEach(function (s, i) {
            var lo = i === 0 ? range[0] : Math.max(range[0], s.range[0]), hi = i === segments.length - 1 ? range[1] : Math.min(range[1], s.range[1]);
            if (!(hi > lo)) { return; }
            var span = sub(point(s.range[1]), point(s.range[0]));
            points.push.apply(points, restrict(s.poles, div(sub(point(lo), point(s.range[0])), span), div(sub(point(hi), point(s.range[0])), span)));
        });
        requireValue(points.length, 'native_thread_trim_range_unsupported'); return points;
    }
    function trimBounds(p, range) { return box(trimControls(p, range)); }
    var profileGeometry = new WeakMap();
    function profile(face, support, radii) {
        var b = face.trims.surfaceBasis, f = frame(support), matrix = face.trims.surfaceToImportWorld3x4;
        requireValue(b.type === 'bspline' && b.uDegree === 3 && b.vDegree === 3 && !b.uPeriodic && !b.vPeriodic
            && !b.uRational && !b.vRational && b.poles.length === 4 && b.weights.length === 4
            && b.weights.every(function (row, i) { return row.length === b.poles[i].length && row.every(function (w) { return w === 1; }); })
            && b.uKnots.length === 2 && pair(b.uKnots) && JSON.stringify(b.uMultiplicities) === '[4,4]'
            && Array.isArray(matrix) && matrix.length === 12 && matrix.every(Number.isFinite), 'native_thread_flank_form_unsupported');
        function local(p) {
            var v = vector(p), world = [0, 1, 2].map(function (k) { return add(dot(vector(matrix.slice(4 * k, 4 * k + 3)), v), point(matrix[4 * k + 3])); });
            var d = vsub(world, f.origin); return [dot(d, f.x), dot(d, f.y), dot(d, f.axis)];
        }
        var rows = b.poles.map(function (r) { return r.map(local); }), a = rows[0], z = rows[3];
        requireValue(rows.every(function (r) { return r.length === a.length; }) && radii.length === 2 && radii[0] > 0 && radii[1] > radii[0], 'native_thread_flank_form_unsupported');
        var rawRatio = (z[0][0][0] * a[0][0][0] + z[0][1][0] * a[0][1][0]) / (a[0][0][0] ** 2 + a[0][1][0] ** 2);
        var radiusA = rawRatio > 1 ? radii[0] : radii[1], radiusB = rawRatio > 1 ? radii[1] : radii[0];
        var ratio = div(point(radiusB), point(radiusA)), dz = sub(z[0][2], a[0][2]), residual = 0, ruledResidual = 0;
        a.forEach(function (p, j) {
            residual = Math.max(residual, magnitude(sub(z[j][0], mul(p[0], ratio))), magnitude(sub(z[j][1], mul(p[1], ratio))), magnitude(sub(sub(z[j][2], p[2]), dz)));
            for (var i = 1; i < 3; i++) {
                var expected = scale(vadd(scale(p, point(3 - i)), scale(z[j], point(i))), div(point(1), point(3)));
                ruledResidual = Math.max(ruledResidual, ...vsub(rows[i][j], expected).map(magnitude));
            }
        });
        // Numerical enclosure is not discarded and not merged with source repair.
        // This specialized family excludes materially non-ruled or twisted patches.
        requireValue(residual <= 1e-8 && ruledResidual <= 1e-8, 'native_thread_flank_profile_unsupported');
        var slope = div(dz, sub(point(radiusB), point(radiusA)));
        var bounds = face.trims.wires.flatMap(function (w) { return w.coedges.map(function (u) { return trimBounds(u.pcurve, u.range); }); });
        var uv = [hull(bounds.map(function (q) { return q[0]; })), hull(bounds.map(function (q) { return q[1]; }))];
        var sRange = div(sub(uv[0], point(b.uKnots[0])), sub(point(b.uKnots[1]), point(b.uKnots[0])));
        requireValue(sRange[0] > -1e-6 && sRange[1] < 1 + 1e-6, 'native_thread_flank_trim_unsupported');
        var curves = rows.map(function (r) { return curveSegments(r, b.vKnots, b.vMultiplicities, 3); });
        // Z is affine in represented U,V for this supported ruled family. Prove
        // that relation against every coefficient, then bound the linear
        // functional on every complete boundary polynomial, not its UV box.
        var expandedV = b.vKnots.flatMap(function (k, i) { return Array(b.vMultiplicities[i]).fill(k); });
        var vSlope = div(sub(a[a.length - 1][2], a[0][2]), sub(point(b.vKnots[b.vKnots.length - 1]), point(b.vKnots[0])));
        var uSlope = div(dz, sub(point(b.uKnots[1]), point(b.uKnots[0]))), axialResidual = 0;
        function axialAt(uvPoint) { return add(a[0][2], add(mul(uSlope, sub(uvPoint[0], point(b.uKnots[0]))), mul(vSlope, sub(uvPoint[1], point(b.vKnots[0]))))); }
        rows.forEach(function (row, i) { row.forEach(function (p, j) {
            var greville = div(add(add(point(expandedV[j + 1]), point(expandedV[j + 2])), point(expandedV[j + 3])), point(3));
            var u = add(point(b.uKnots[0]), mul(div(point(i), point(3)), sub(point(b.uKnots[1]), point(b.uKnots[0]))));
            axialResidual = Math.max(axialResidual, magnitude(sub(p[2], axialAt([u, greville]))));
        }); });
        requireValue(axialResidual < 1e-8, 'native_thread_flank_axial_profile_unsupported');
        // Bernstein extrapolation can amplify residuals outside the base U/V
        // knot rectangle. Retain that amplification, including tiny extensions.
        var minSpan = Math.min.apply(Math, b.vKnots.slice(1).map(function (v, i) { return down(v - b.vKnots[i]); }));
        var vExtension = div(point(Math.max(0, up(b.vKnots[0] - uv[1][0]), up(uv[1][1] - b.vKnots[b.vKnots.length - 1]))), point(minSpan));
        var uExtension = point(Math.max(0, -sRange[0], up(sRange[1] - 1)));
        var factor = mul(add(point(1), mul(point(2), uExtension)), add(point(1), mul(point(2), vExtension)));
        var amplification = mul(mul(factor, factor), factor)[1];
        var trimAxial = hull(face.trims.wires.flatMap(function (w) { return w.coedges.flatMap(function (u) { return trimControls(u.pcurve, u.range).map(axialAt); }); }));
        trimAxial = add(trimAxial, [-up(axialResidual * amplification), up(axialResidual * amplification)]);
        var radial = [Infinity, -Infinity], axial = [Infinity, -Infinity], normal = [Infinity, -Infinity], cells = 0;
        var orientation = face.orientation === 0 ? 1 : face.orientation === 1 ? -1 : 0;
        requireValue(orientation, 'native_thread_polarity_ambiguous');
        function cell(net, width, depth) {
            requireValue(++cells <= 65536, 'native_thread_flank_bound_resource_limit');
            var vbox = box(net.flat()), du = box(net.slice(1).flatMap(function (r, i) { return r.map(function (p, j) { return scale(vsub(p, net[i][j]), point(3)); }); }));
            var dv = box(net.flatMap(function (r) { return derivative(r).map(function (p) { return scale(p, div(point(1), point(width))); }); }));
            var endRadiusSquared = hull([radialSquared(net[0]), radialSquared(net[3])]);
            // The full coefficient scaling relation rules out angular cancellation
            // between the rows. Preserve its error and the ruled residual instead
            // of replacing the actual surface with an exact interpolation.
            var radialError = up(up(4 * residual + 6 * ruledResidual) * amplification);
            var rlo = down(down(Math.sqrt(Math.max(0, endRadiusSquared[0]))) - radialError);
            var rhi = up(up(Math.sqrt(Math.max(0, endRadiusSquared[1]))) + radialError);
            var n = mul(dot(cross(du, dv), [vbox[0], vbox[1], point(0)]), point(orientation));
            var rightNormal = support.orientationSign === 1 ? n[0] > 0 : n[1] < 0;
            // A whole Bernstein hull is already a conservative envelope, not
            // an exact-extrema request. Refine only to prove nondegeneracy and
            // polarity here; paired compatibility has its own finite target.
            if ((rlo <= 0 || !rightNormal) && depth < 12) {
                cell(net.map(function (r) { return restrict(r, point(0), point(.5)); }), width / 2, depth + 1);
                cell(net.map(function (r) { return restrict(r, point(.5), point(1)); }), width / 2, depth + 1); return;
            }
            requireValue(rlo > 0, 'native_thread_flank_radial_bound_unavailable');
            requireValue(rightNormal, 'native_thread_flank_normal_unavailable');
            radial = [Math.min(radial[0], rlo), Math.max(radial[1], rhi)]; axial = [Math.min(axial[0], vbox[2][0]), Math.max(axial[1], vbox[2][1])]; normal = [Math.min(normal[0], n[0]), Math.max(normal[1], n[1])];
        }
        curves[0].forEach(function (segment, j) {
            var lo = j === 0 ? uv[1][0] : Math.max(uv[1][0], segment.range[0]);
            var hi = j === curves[0].length - 1 ? uv[1][1] : Math.min(uv[1][1], segment.range[1]);
            if (!(hi > lo)) { return; }
            // A rounding-width sliver at a knot remains represented. Enclose it
            // using the whole containing polynomial span; never discard it or
            // differentiate two almost identical rounded endpoint values.
            if (hi - lo < (segment.range[1] - segment.range[0]) * 1e-8) {
                lo = Math.min(lo, segment.range[0]); hi = Math.max(hi, segment.range[1]);
            }
            var span = sub(point(segment.range[1]), point(segment.range[0]));
            var net = curves.map(function (curve) { return restrict(curve[j].poles, div(sub(point(lo), point(segment.range[0])), span), div(sub(point(hi), point(segment.range[0])), span)); });
            // Reparameterize even the tiny represented U extensions: never clamp.
            net = net[0].map(function (_, k) { return restrict(net.map(function (row) { return row[k]; }), point(sRange[0]), point(sRange[1])); });
            net = net[0].map(function (_, k) { return net.map(function (column) { return column[k]; }); });
            cell(net, hi - lo, 0);
        });
        requireValue(cells && radial.every(Number.isFinite), 'native_thread_flank_trim_unsupported');
        var result = { method: 'complete-ruled-cubic-coefficients-and-represented-bernstein-bounds-v1', radiusA: radiusA, radiusB: radiusB,
            radialScale: ratio, axialOffsetMm: dz, axialPerRadialSlope: slope, coefficientResidualMm: residual, ruledResidualMm: ruledResidual,
            radialEnvelopeMm: radial, axialEnvelopeMm: axial, trimAxialEnvelopeMm: trimAxial,
            axialAffineResidualMm: axialResidual,
            axialAffineModel: { originUV: [b.uKnots[0], b.vKnots[0]], valueAtOriginMm: a[0][2], uSlopeMm: uSlope, vSlopeMm: vSlope,
                representedResidualMm: up(axialResidual * amplification) }, orientedRadialNormalDot: normal,
            trimUVBounds: uv, representedURange: sRange, boundedCells: cells };
        profileGeometry.set(result, { face: face, curves: curves, amplification: amplification });
        return result;
    }
    function pairedRadial(face, profile, connections, metricRows, nativeVertices) {
        var geometry = profileGeometry.get(profile), b = face.trims.surfaceBasis;
        requireValue(geometry && geometry.face === face, 'native_thread_radial_geometry_unverified');
        var vertexV = new Map();
        face.trims.wires.forEach(function (wire) { wire.coedges.forEach(function (use) {
            var controls = trimControls(use.pcurve, use.range), endpoints = [controls[0], controls[controls.length - 1]];
            var vertices = use.orientation === 1 ? [use.endVertexId, use.startVertexId] : [use.startVertexId, use.endVertexId];
            vertices.forEach(function (id, i) { if (id) { vertexV.set(id, vertexV.has(id) ? hull([vertexV.get(id), endpoints[i][1]]) : endpoints[i][1]); } });
        }); });
        function metric(use, wireId, faceId) {
            var rows = metricRows.get(use.coedgeId) || [];
            requireValue(rows.length === 1, 'native_thread_paired_radial_metric_unavailable');
            var r = rows[0];
            requireValue(r.status === 'bounded' && r.faceId === faceId && r.wireId === wireId && r.coedgeId === use.coedgeId
                && r.edgeId === use.edgeId && r.sourceEdgeItemId && r.obligationId
                && r.associationStatus === 'complete-unique-native-occurrence' && r.rangeIdentityStatus === 'source-and-post-ranges-captured'
                && r.postRangeFirst === use.range[0] && r.postRangeLast === use.range[1]
                && r.sourceRange && [r.sourceRange.first, r.sourceRange.last].every(Number.isFinite)
                && Number.isFinite(r.upperBoundMm) && r.upperBoundMm >= 0, 'native_thread_paired_radial_metric_unavailable');
            // These producer paths assess the post lift on the unchanged source
            // 3-D edge over postRange. A generated prior pcurve has [0,0]; that
            // metadata is not the assessed source-edge parameter domain.
            requireValue(['post-lift-to-unchanged-source-3d-plus-endpoint-sliver',
                'exact-unchanged-trim-with-independent-edge-residual', 'old-lift-via-source-3d-to-new-lift'].includes(r.referencePath)
                && Array.isArray(r.pathTermsMm) && r.pathTermsMm.some(function (t) { return t.kind === 'post-trim-lift-residual'
                    && t.termId && Number.isFinite(t.upperBoundMm) && t.upperBoundMm >= 0; })
                && pair([r.postRangeFirst, r.postRangeLast]), 'native_thread_paired_radial_metric_domain_unavailable');
            return r;
        }
        var proofs = connections.map(function (connection) {
            var entry = connection.entry, use = entry.use, neighbor = connection.neighbor, seed = connection.seed;
            var observations = seed.observations.filter(function (o) { return o.edgeId === use.edgeId && o.coedgeId === neighbor.coedgeId && o.wireId === neighbor.wireId; });
            requireValue(observations.length === 1, 'native_thread_shared_boundary_not_helical');
            var seedWires = seed.face.trims.wires.filter(function (w) { return w.wireId === neighbor.wireId; });
            var seedUses = seedWires.flatMap(function (w) { return w.coedges.filter(function (u) { return u.coedgeId === neighbor.coedgeId && u.edgeId === use.edgeId; }); });
            requireValue(seedUses.length === 1, 'native_thread_paired_radial_correspondence');
            var flankMetric = metric(use, entry.wire.wireId, face.faceId), seedMetric = metric(seedUses[0], neighbor.wireId, seed.face.faceId);
            requireValue(flankMetric.sourceEdgeItemId === seedMetric.sourceEdgeItemId
                && use.range[0] === seedUses[0].range[0] && use.range[1] === seedUses[0].range[1],
            'native_thread_paired_radial_correspondence');
            var q = affine(use.pcurve, use.range), uWidth = sub(point(b.uKnots[1]), point(b.uKnots[0]));
            var normalizedU = div(sub(q.uvBounds[0], point(b.uKnots[0])), uWidth);
            var side = magnitude(normalizedU) < magnitude(sub(normalizedU, point(1))) ? 0 : 1;
            requireValue(magnitude(sub(normalizedU, point(side))) <= 1e-6, 'native_thread_paired_radial_row_domain');
            var uPerV = div(q.delta[0], q.delta[1]);
            var radius = side === 0 ? profile.radiusA : profile.radiusB;
            requireValue(seed.support.radius === radius, 'native_thread_paired_radial_correspondence');
            var vertices = use.orientation === 1 ? [use.endVertexId, use.startVertexId] : [use.startVertexId, use.endVertexId];
            requireValue(vertices.every(function (id) { return id && vertexV.has(id); }), 'native_thread_paired_radial_row_domain');
            var vertexLimits = vertices.map(function (id) {
                var v = nativeVertices && nativeVertices.get(id);
                requireValue(v && v.status === 'complete' && Number.isFinite(v.tolerance) && v.tolerance >= 0
                    && v.worldPoint.length === 3 && v.worldPoint.every(Number.isFinite), 'native_thread_paired_radial_vertex_unavailable');
                return up(2 * v.tolerance);
            });
            // Only actual joined trim endpoints extend a row's certified domain.
            // Their finite continuation is separately bounded from coefficients;
            // it is never claimed to be part of the native same-edge source tube.
            var endpoints = q.endpoints.map(function (v) { return v[1]; }).sort(function (a, z) { return a[0] - z[0]; });
            // Outer endpoint rounding hulls are not certified positive-length
            // coverage. Use the guaranteed affine image interior; enclose every
            // remaining endpoint interval by the explicit continuation proof.
            var covered = [endpoints[0][1], endpoints[1][0]], endpointDomains = vertices.map(function (id) { return vertexV.get(id); });
            requireValue(pair(covered), 'native_thread_paired_radial_row_domain');
            return { method: 'paired-same-source-edge-finite-radial-tubes-v1', side: side, nativeRadiusMm: radius,
                edgeId: use.edgeId, sourceEdgeItemId: flankMetric.sourceEdgeItemId, flankCoedgeId: use.coedgeId, seedCoedgeId: seedUses[0].coedgeId,
                sourceParameterDomain: use.range.slice(), flankParameterDomain: use.range.slice(), seedParameterDomain: seedUses[0].range.slice(),
                priorFlankPcurveDomain: [flankMetric.sourceRange.first, flankMetric.sourceRange.last], priorSeedPcurveDomain: [seedMetric.sourceRange.first, seedMetric.sourceRange.last],
                flankReferencePath: flankMetric.referencePath, seedReferencePath: seedMetric.referencePath,
                flankPostLiftMetricRefs: flankMetric.pathTermsMm.filter(function (t) { return t.kind === 'post-trim-lift-residual'; }).map(function (t) { return t.termId; }),
                seedPostLiftMetricRefs: seedMetric.pathTermsMm.filter(function (t) { return t.kind === 'post-trim-lift-residual'; }).map(function (t) { return t.termId; }),
                affineBoundaryOriginUV: q.endpoints[0], affineBoundaryUPerV: uPerV,
                flankMetricRef: flankMetric.obligationId, seedMetricRef: seedMetric.obligationId,
                flankTubeMm: flankMetric.upperBoundMm, seedTubeMm: seedMetric.upperBoundMm, pairedTubeMm: up(flankMetric.upperBoundMm + seedMetric.upperBoundMm),
                coveredFlankVDomain: covered, endpointVertexIds: vertices, endpointVDomains: endpointDomains, endpointContinuationLimitsMm: vertexLimits,
                coefficientContinuationVDomain: hull([q.uvBounds[1]].concat(endpointDomains)), maximumContinuationMm: 0 };
        });
        requireValue(new Set(proofs.map(function (p) { return p.side; })).size === 2, 'native_thread_paired_radial_row_domain');
        var domain = profile.trimUVBounds[1], cuts = [domain[0], domain[1]];
        proofs.forEach(function (p) { cuts.push.apply(cuts, p.coefficientContinuationVDomain); cuts.push.apply(cuts, p.coveredFlankVDomain); });
        b.vKnots.forEach(function (v) { cuts.push(v); });
        cuts = Array.from(new Set(cuts.filter(function (v) { return v >= domain[0] && v <= domain[1]; }))).sort(function (a, z) { return a - z; });
        var cells = 0, coverage = [];
        function boundaryS(proof, lo, hi) {
            var u = add(proof.affineBoundaryOriginUV[0], mul(proof.affineBoundaryUPerV, sub([lo, hi], proof.affineBoundaryOriginUV[1])));
            return div(sub(u, point(b.uKnots[0])), sub(point(b.uKnots[1]), point(b.uKnots[0])));
        }
        function segmentAt(lo, hi) {
            var index = geometry.curves[0].findIndex(function (s, j) { return (j === 0 || s.range[0] <= lo)
                && (j === geometry.curves[0].length - 1 || s.range[1] >= hi); });
            requireValue(index >= 0 && geometry.curves.every(function (curve) { return curve[index] && pair(curve[index].range)
                && curve[index].range[0] === geometry.curves[0][index].range[0] && curve[index].range[1] === geometry.curves[0][index].range[1]; }),
            'native_thread_paired_radial_domain_uncovered');
            return index;
        }
        function derivatives(index, lo, hi, sDomain) {
            var span = geometry.curves[0][index].range, width = sub(point(span[1]), point(span[0]));
            // Use the whole original polynomial span for V differentiation, even
            // when the requested continuation is only a rounding-width sliver.
            var first = Math.min(lo, span[0]), last = Math.max(hi, span[1]);
            var net = geometry.curves.map(function (curve) { return restrict(curve[index].poles,
                div(sub(point(first), point(span[0])), width), div(sub(point(last), point(span[0])), width)); });
            var du = 0, dv = 0;
            net[0].forEach(function (_, j) { var d = evaluate(derivative(net.map(function (row) { return row[j]; })), sDomain);
                du = Math.max(du, up(Math.sqrt(dot(d, d)[1]))); });
            var dvRows = net.map(function (row) { return derivative(row).map(function (p) { return scale(p, div(point(1), sub(point(last), point(first)))); }); });
            dvRows[0].forEach(function (_, j) { var d = evaluate(dvRows.map(function (row) { return row[j]; }), sDomain);
                dv = Math.max(dv, up(Math.sqrt(dot(d, d)[1]))); });
            return { du: du, dv: dv };
        }
        function continuationPath(proof, lo, hi) {
            var range = lo < proof.coveredFlankVDomain[0] ? [lo, proof.coveredFlankVDomain[0]]
                : hi > proof.coveredFlankVDomain[1] ? [proof.coveredFlankVDomain[1], hi] : null;
            if (!range) { return { bound: 0, distance: 0, derivative: 0, segments: [], normalizedU: null }; }
            var pathCuts = [range[0], range[1]];
            b.vKnots.forEach(function (v, j) {
                if (!j || j === b.vKnots.length - 1 || v < range[0] || v > range[1]) { return; }
                // A complete spline with interior multiplicity <= degree is C0.
                // Full multiplicity does not prove continuity; a shared native
                // vertex or overlapping interval values cannot replace that fact.
                requireValue(b.vMultiplicities[j] <= b.vDegree, 'native_thread_paired_radial_continuation_discontinuous');
                pathCuts.push(v);
            });
            pathCuts = Array.from(new Set(pathCuts)).sort(function (a, z) { return a - z; });
            var bound = 0, maximumDerivative = 0, segments = [];
            var sPerV = div(proof.affineBoundaryUPerV, sub(point(b.uKnots[1]), point(b.uKnots[0])));
            for (var j = 1; j < pathCuts.length; j++) {
                var first = pathCuts[j - 1], last = pathCuts[j], index = segmentAt(first, last);
                var sDomain = boundaryS(proof, first, last), d = derivatives(index, first, last, sDomain);
                var derivativeBound = up(d.dv + up(d.du * magnitude(sPerV)));
                var distance = sub(point(last), point(first))[1], displacement = up(distance * derivativeBound);
                bound = up(bound + displacement); maximumDerivative = Math.max(maximumDerivative, derivativeBound);
                segments.push({ vDomain: [first, last], normalizedUDomain: sDomain, knotSpan: geometry.curves[0][index].range.slice(),
                    vDistance: distance, derivativeBoundMmPerV: derivativeBound, displacementBoundMm: displacement });
            }
            requireValue(segments.length && Number.isFinite(bound), 'native_thread_paired_radial_domain_uncovered');
            return { bound: bound, distance: sub(point(range[1]), point(range[0]))[1], derivative: maximumDerivative,
                segments: segments, normalizedU: boundaryS(proof, range[0], range[1]) };
        }
        for (var k = 1; k < cuts.length; k++) {
            var lo = cuts[k - 1], hi = cuts[k]; if (!(hi > lo)) { continue; }
            var available = proofs.filter(function (p) { return p.coefficientContinuationVDomain[0] <= lo && p.coefficientContinuationVDomain[1] >= hi; });
            requireValue(available.length, 'native_thread_paired_radial_domain_uncovered');
            var proof = available.sort(function (a, z) { return a.pairedTubeMm - z.pairedTubeMm || a.flankCoedgeId.localeCompare(z.flankCoedgeId); })[0];
            var segment = segmentAt(lo, hi);
            var span = geometry.curves[0][segment].range, width = sub(point(span[1]), point(span[0]));
            var net = geometry.curves.map(function (curve) { return restrict(curve[segment].poles, div(sub(point(lo), point(span[0])), width), div(sub(point(hi), point(span[0])), width)); });
            var continuationProof = continuationPath(proof, lo, hi);
            var actualS = boundaryS(proof, lo, hi), transferS = hull([actualS, point(proof.side)]);
            // Whole U-derivative coefficients cover the path from the actual
            // affine boundary to its reference row, including represented drift.
            // No U snapping, endpoint sampling, or omitted positive-length strip.
            var duBound = derivatives(segment, lo, hi, transferS).du, continuation = continuationProof.bound;
            // A shared vertex label cannot license arbitrary positive-length
            // extrapolation. Prove the entire continuation displacement, and
            // require it to stay within the actual native endpoint envelopes.
            requireValue(continuation <= Math.min.apply(Math, proof.endpointContinuationLimitsMm), 'native_thread_paired_radial_endpoint_continuation_unavailable');
            proof.maximumContinuationMm = Math.max(proof.maximumContinuationMm, continuation);
            var crossRowTransfer = up(duBound * magnitude(sub(actualS, point(proof.side))));
            var tube = up(up(proof.pairedTubeMm + continuation) + crossRowTransfer), rowErrors = [profile.radiusA, profile.radiusB].map(function (r) {
                var scaleFactor = div(point(r), point(proof.nativeRadiusMm));
                return add(mul(point(tube), scaleFactor),
                    mul(mul(add(mul(point(4), point(profile.coefficientResidualMm)), mul(point(6), point(profile.ruledResidualMm))), point(geometry.amplification)),
                        point(Math.max(1, scaleFactor[1]))))[1];
            });
            function check(piece, depth) {
                requireValue(++cells <= 65536, 'native_thread_paired_radial_resource_limit');
                var good = [0, 3].every(function (row, side) { var square = radialSquared(piece[row]), r = side === 0 ? profile.radiusA : profile.radiusB;
                    return down(Math.sqrt(Math.max(0, square[0]))) >= down(r - rowErrors[side]) && up(Math.sqrt(Math.max(0, square[1]))) <= up(r + rowErrors[side]); });
                if (!good && depth < 12) { check(piece.map(function (r) { return restrict(r, point(0), point(.5)); }), depth + 1); check(piece.map(function (r) { return restrict(r, point(.5), point(1)); }), depth + 1); return; }
                requireValue(good, 'native_thread_flank_radial_bound_unavailable');
            }
            check(net, 0);
            coverage.push({ vDomain: [lo, hi], pairedProofCoedgeId: proof.flankCoedgeId, continuationMm: continuation,
                continuationDerivativeBoundMmPerV: continuationProof.derivative, continuationVDistance: continuationProof.distance,
                continuationSegments: continuationProof.segments, continuationPathNormalizedUDomain: continuationProof.normalizedU, propagatedRowTubeMm: rowErrors,
                affineBoundaryNormalizedUDomain: actualS, crossRowDerivativeBoundMm: duBound, crossRowTransferMm: crossRowTransfer });
        }
        return { method: 'paired-domain-tubes-with-whole-coefficient-row-propagation-v1', proofs: proofs, coverage: coverage,
            representedVDomain: domain.slice(), actualRadialEnvelopeMm: profile.radialEnvelopeMm.slice(), nativeReferenceRadiiMm: [profile.radiusA, profile.radiusB], boundedCells: cells };
    }
    root.CncNativeThreadEvidence = Object.freeze({ affine: affine, trimBounds: trimBounds, profile: profile, frame: frame,
        sharedBoundary: sharedBoundary, compatibleBoundaryLead: compatibleBoundaryLead, pairedRadial: pairedRadial,
        interval: Object.freeze({ point: point, add: add, sub: sub, mul: mul, div: div, hull: hull, magnitude: magnitude, up: up, down: down }) });
}(typeof self !== 'undefined' ? self : globalThis));
