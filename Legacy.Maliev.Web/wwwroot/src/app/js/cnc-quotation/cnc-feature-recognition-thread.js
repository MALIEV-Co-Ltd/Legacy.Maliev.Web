(function (root) {
    'use strict';
    var sorted = function (a) { return Array.from(new Set(a)).sort(); };
    var dot = function (a, b) { return a.reduce(function (s, x, i) { return s + x * b[i]; }, 0); };
    var sub = function (a, b) { return a.map(function (x, i) { return x - b[i]; }); };
    var mid = function (a) { return a[0] + (a[1] - a[0]) / 2; };
    function coedges(face) { return face.trims.wires.flatMap(function (w) { return w.coedges.map(function (u) { return { wire: w, use: u }; }); }); }
    function compatibleAxis(a, b) {
        var sign = dot(a.axis, b.axis) < 0 ? -1 : 1, difference = sub(a.axis, b.axis.map(function (x) { return sign * x; }));
        var d = sub(a.origin, b.origin), along = dot(d, a.axis);
        return Math.hypot.apply(Math, difference) < 1e-12 && Math.hypot.apply(Math, sub(d, a.axis.map(function (x) { return x * along; }))) < 1e-8;
    }
    function intersection(a) { return [Math.max.apply(Math, a.map(function (x) { return x[0]; })), Math.min.apply(Math, a.map(function (x) { return x[1]; }))]; }
    function union(intervals, error) {
        var result = [];
        intervals.slice().sort(function (a, b) { return a[0] - b[0]; }).forEach(function (range) {
            var last = result[result.length - 1];
            if (last && range[0] <= last[1] + error) { last[1] = Math.max(last[1], range[1]); }
            else { result.push(range.slice()); }
        }); return result;
    }
    function axialRange(range, support, reference) {
        var offset = dot(sub(support.origin, reference.origin), reference.axis), sign = dot(support.axis, reference.axis);
        return [offset + sign * range[0], offset + sign * range[1]].sort(function (a, b) { return a - b; });
    }
    function measureSeed(face, metricRows) {
        var math = root.CncNativeThreadEvidence, I = math.interval, s = face.support;
        if (s.type !== 'cylinder' || ![1, -1].includes(s.orientationSign) || !(s.radius > 0)) { return null; }
        math.frame(s);
        var observations = [], bounds = [];
        coedges(face).forEach(function (entry) {
            var u = entry.use, p = u.pcurve, q;
            try { q = math.affine(p, u.range); } catch (error) { return; }
            bounds.push(q.uvBounds[1]);
            if (I.magnitude(q.delta[0]) < 1e-10 || I.magnitude(q.delta[1]) < 1e-10 || u.seam) { return; }
            if (q.delta[0][0] <= 0 && q.delta[0][1] >= 0) { return; }
            var rows = metricRows.get(u.coedgeId) || [];
            if (!rows.length || rows.some(function (r) { return r.status !== 'bounded' || r.faceId !== face.faceId || r.edgeId !== u.edgeId || r.wireId !== entry.wire.wireId; })) { return; }
            var e = Math.max.apply(Math, rows.map(function (r) { return r.upperBoundMm; })), du = I.magnitude(q.delta[0]);
            // A Euclidean source tube bounds endpoint axial error by e and angular
            // error by 2e/r for e/r<0.1. Propagate both through the finite secant.
            var angleError = I.up(2 * e / s.radius);
            if (!(e >= 0 && e < s.radius / 10 && du > 4 * angleError && du > .05)) { return; }
            var slope = I.div(q.delta[1], q.delta[0]), lead = I.mul(slope, [I.down(2 * Math.PI), I.up(2 * Math.PI)]);
            var errorMm = I.up(2 * Math.PI * (2 * e + 2 * angleError * I.magnitude(slope)) / (du - 2 * angleError));
            var compatibleLead = [I.down(lead[0] - errorMm), I.up(lead[1] + errorMm)];
            if (compatibleLead[0] <= 0 && compatibleLead[1] >= 0) { return; }
            observations.push({ faceId: face.faceId, edgeId: u.edgeId, coedgeId: u.coedgeId, wireId: entry.wire.wireId,
                startVertexId: u.orientation === 1 ? u.endVertexId : u.startVertexId,
                endVertexId: u.orientation === 1 ? u.startVertexId : u.endVertexId,
                sourceMetricRefs: rows.map(function (r) { return r.obligationId; }).sort(),
                range: u.range.slice(), endpointsUV: q.endpoints, signedLeadMm: lead, sourceCompatibleLeadMm: compatibleLead,
                sourceErrorMm: e, angularErrorRad: angleError, numericalMethod: 'complete-affine-cylinder-uv-outward-v1' });
        });
        if (!observations.length) { return null; }
        // All finite boundary coordinates are required for a seed coverage interval.
        if (bounds.length !== coedges(face).length) { throw new Error('native_thread_seed_trim_unsupported'); }
        return { face: face, support: s, observations: observations, axial: I.hull(bounds), sourceErrorMm: Math.max.apply(Math, observations.map(function (o) { return o.sourceErrorMm; })) };
    }
    function starts(seeds, flanks, reference, leadInterval, lowerRadius) {
        var sides = new Map([[-1, []], [1, []]]), evidence = [], ambiguity = null;
        var flankByEdge = new Map();
        flanks.forEach(function (f) { f.edgeIds.forEach(function (id) { flankByEdge.set(id, f); }); });
        seeds.filter(function (s) { return Math.abs(s.support.radius - lowerRadius) < 1e-8; }).forEach(function (seed) {
            var s = seed.support, sign = dot(s.axis, reference.axis) < 0 ? -1 : 1;
            var phase = Math.atan2(dot(s.xDirection, reference.yDirection), dot(s.xDirection, reference.xDirection));
            var offset = dot(sub(s.origin, reference.origin), reference.axis);
            seed.observations.forEach(function (o) {
                var flank = flankByEdge.get(o.edgeId); if (!flank) { return; }
                var a = o.endpointsUV[0].map(mid), b = o.endpointsUV[1].map(mid);
                var ua = phase + sign * a[0], ub = phase + sign * b[0], va = offset + sign * a[1], vb = offset + sign * b[1];
                var lo = Math.min(ua, ub), hi = Math.max(ua, ub), error = o.sourceErrorMm * (1 + 2 * Math.abs((vb - va) / (ub - ua)) / s.radius) + 1e-10;
                var first = Math.ceil((lo - 1e-12) / (2 * Math.PI)), last = Math.floor((hi + 1e-12) / (2 * Math.PI));
                if (last - first > 1024) { ambiguity = 'thread_section_resource_limit'; return; }
                var side = Math.sign(mid(flank.profile.axialPerRadialSlope) * dot(flank.reference.axis, reference.axis));
                for (var k = first; k <= last; k++) {
                    var angle = 2 * Math.PI * k, t = (angle - ua) / (ub - ua), v = va + t * (vb - va);
                    var vertex = Math.abs(angle - ua) < 1e-10 ? o.startVertexId : Math.abs(angle - ub) < 1e-10 ? o.endVertexId : null;
                    sides.get(side).push({ axialMm: v, uncertaintyMm: error, edgeId: o.edgeId, vertexId: vertex, flankId: flank.face.faceId, coedgeId: o.coedgeId });
                }
            });
        });
        var absoluteLead = leadInterval[1] < 0 ? [-leadInterval[1], -leadInterval[0]] : leadInterval;
        var candidates = [];
        sides.forEach(function (events, side) {
            events.sort(function (a, b) { return a.axialMm - b.axialMm || a.edgeId.localeCompare(b.edgeId); });
            var unique = [];
            events.forEach(function (event) {
                var last = unique[unique.length - 1];
                if (last && Math.abs(event.axialMm - last.axialMm) <= event.uncertaintyMm + last.uncertaintyMm) {
                    if (last.edgeId === event.edgeId || last.vertexId && last.vertexId === event.vertexId) { return; }
                    ambiguity = 'thread_section_overlap_ambiguous';
                }
                unique.push(event);
            });
            evidence.push({ profileSide: side, crossings: unique });
            if (unique.length < 3 || unique[unique.length - 1].axialMm - unique[0].axialMm < 1.5 * absoluteLead[1]) { candidates.push([]); return; }
            var fits = [];
            for (var n = 1; n <= 16; n++) {
                if (unique.slice(1).every(function (event, i) {
                    var previous = unique[i], d = event.axialMm - previous.axialMm, e = event.uncertaintyMm + previous.uncertaintyMm;
                    return d - e > 0 && d + e >= absoluteLead[0] / n && d - e <= absoluteLead[1] / n;
                })) { fits.push(n); }
            }
            candidates.push(fits);
        });
        var possible = candidates.length === 2 ? candidates[0].filter(function (n) { return candidates[1].includes(n); }) : [];
        return { method: 'complete-affine-angular-section-recurrence-two-profile-sides-v1', startCount: !ambiguity && possible.length === 1 ? possible[0] : null,
            candidates: possible, reason: ambiguity || (possible.length === 1 ? null : 'thread_pitch_lead_ambiguous'), sections: evidence };
    }
    function entryEnds(group, allFaces, edgeMap, reference) {
        var owned = new Set(group), neighbors = new Set();
        group.forEach(function (id) { coedges(allFaces.get(id)).forEach(function (x) { edgeMap.get(x.use.edgeId).uses.forEach(function (u) { if (!owned.has(u.faceId)) { neighbors.add(u.faceId); } }); }); });
        var ends = [];
        Array.from(neighbors).sort().forEach(function (id) {
            var f = allFaces.get(id), s = f.support;
            if (s.type !== 'plane' || Math.abs(dot(s.orientedNormal, reference.axis)) < 1 - 1e-10) { return; }
            f.trims.wires.forEach(function (w) {
                if (!w.coedges.length || !w.coedges.every(function (u) { return edgeMap.get(u.edgeId).uses.some(function (x) { return owned.has(x.faceId); }); })) { return; }
                var kind = reference.orientationSign < 0 ? (w.role === 'inner' ? 'mouth' : 'terminal-plane') : (w.role === 'inner' ? 'shoulder' : 'free-end-plane');
                ends.push({ kind: kind, axialMm: dot(sub(s.origin, reference.origin), reference.axis),
                    accessAxis: kind === 'mouth' ? s.orientedNormal.slice() : null, planeFaceId: id, wireId: w.wireId,
                    evidenceRefs: w.coedges.map(function (u) { return u.edgeId; }).sort(), toolAccessVerified: false });
            });
        }); return ends.sort(function (a, b) { return a.axialMm - b.axialMm || a.wireId.localeCompare(b.wireId); });
    }
    function finiteDomain(records, flanks, faces, edges, reference, error) {
        var seedIds = new Set(records.map(function (s) { return s.face.faceId; })), flankIds = new Set(flanks.map(function (f) { return f.face.faceId; })), proofs = [], allCuts = [];
        for (var flank of flanks) {
            var cuts = [], unsupported = false;
            coedges(flank.face).forEach(function (entry) {
                var neighbors = edges.get(entry.use.edgeId).uses.filter(function (u) { return u.faceId !== flank.face.faceId; });
                if (neighbors.some(function (u) { return seedIds.has(u.faceId); })) { return; }
                if (neighbors.length === 1 && flankIds.has(neighbors[0].faceId)) { return; }
                if (!neighbors.length && entry.use.seam) { return; }
                if (neighbors.length !== 1) { unsupported = true; return; }
                var face = faces.get(neighbors[0].faceId), s = face.support;
                if (s.type !== 'plane' || Math.abs(dot(s.orientedNormal, reference.axis)) < 1 - 1e-10) { unsupported = true; return; }
                cuts.push({ axialMm: dot(sub(s.origin, reference.origin), reference.axis), planeFaceId: face.faceId,
                    edgeId: entry.use.edgeId, coedgeId: entry.use.coedgeId });
            });
            if (unsupported) { return null; }
            var envelope = axialRange(flank.profile.trimAxialEnvelopeMm, flank.reference, reference);
            allCuts.push.apply(allCuts, cuts);
            proofs.push({ flankFaceId: flank.face.faceId, completeTrimEnvelopeMm: envelope, terminalCuts: cuts });
        }
        if (allCuts.length < 2) { return null; }
        var interval = [Math.min.apply(Math, allCuts.map(function (c) { return c.axialMm; })), Math.max.apply(Math, allCuts.map(function (c) { return c.axialMm; }))];
        var envelope = root.CncNativeThreadEvidence.interval.hull(proofs.map(function (p) { return p.completeTrimEnvelopeMm; }));
        if (!(interval[1] - interval[0] > 2 * error) || allCuts.some(function (c) { return Math.min(Math.abs(c.axialMm - interval[0]), Math.abs(c.axialMm - interval[1])) > 1e-8; })
            || Math.abs(envelope[0] - interval[0]) > error + 1e-8 || Math.abs(envelope[1] - interval[1]) > error + 1e-8) { return null; }
        return { intervalMm: interval, method: 'complete-flank-trims-between-two-native-perpendicular-cuts-v1', proofs: proofs, boundaryUncertaintyMm: error };
    }
    // Deliberately bounded compatible-nominal references, not a fit-class catalog
    // or permission to tap/thread-mill. Measurements are never rounded to these.
    var nominalReferences = [
        { designation: 'M6x1', internal: true, diameterMm: 6, pitchMm: 1, measuredDiameter: 'minor', limitsMm: [4.917, 5.153],
            source: 'https://www.yamawa.com/Portals/0/resource/en/tips/pdf/tips-073.pdf' },
        { designation: 'M14x1', internal: false, diameterMm: 14, pitchMm: 1, measuredDiameter: 'major', limitsMm: [13.794, 13.974],
            source: 'https://www.yamawa.com/Portals/0/images/download/holesize/en/technical-contents-17.pdf' },
        { designation: 'M24x1.5', internal: true, diameterMm: 24, pitchMm: 1.5, measuredDiameter: 'minor', limitsMm: [22.376, 22.676],
            source: 'https://www.yamawa.com/en/product/taps/detail.html?pdid=113574' }
    ];
    function nominalMatch(internal, radii, lead, start, flanks) {
        if (!start.startCount) { return null; }
        var expectedSlope = 1 / Math.sqrt(3), signs = new Set();
        if (!flanks.every(function (f) {
            var p = f.profile, slope = p.axialPerRadialSlope;
            signs.add(Math.sign(mid(slope)));
            return Math.abs(Math.abs(mid(slope)) - expectedSlope) <= (p.coefficientResidualMm + 3 * p.ruledResidualMm) / (radii[1] - radii[0]) + 1e-10;
        }) || signs.size !== 2) { return null; }
        var absolute = lead[1] < 0 ? [-lead[1], -lead[0]] : lead;
        var matches = nominalReferences.filter(function (r) {
            var diameter = 2 * (r.measuredDiameter === 'minor' ? radii[0] : radii[1]);
            return r.internal === internal && diameter >= r.limitsMm[0] && diameter <= r.limitsMm[1]
                && r.pitchMm * start.startCount >= absolute[0] && r.pitchMm * start.startCount <= absolute[1];
        });
        return matches.length === 1 ? { status: 'compatible-reference-not-fit-certification', designation: matches[0].designation,
            nominalDiameterMm: matches[0].diameterMm, nominalPitchMm: matches[0].pitchMm, fitClass: null,
            reference: matches[0], profileIncludedAngleDegrees: 60 } : null;
    }
    function recognize(topology, projection) {
        var result = { features: [], unresolvedByFace: {}, observations: [] };
        if (!projection.sourceVerified || !root.CncNativeRegions.isLocalProjection(projection)
            || projection.provenance.topologyRevision !== topology.revision) { return result; }
        var n = topology.cadDocument.nativeImport, metrics = new Map(), faces = new Map(), edges = new Map(), vertices = new Map(), seeds = new Map(), flanks = [];
        n.kernelProvenance.nativeInterpretation.metrics.rows.forEach(function (r) { if (r.coedgeId) { if (!metrics.has(r.coedgeId)) { metrics.set(r.coedgeId, []); } metrics.get(r.coedgeId).push(r); } });
        n.meshes.forEach(function (mesh) { mesh.brep_faces.forEach(function (f) { faces.set(f.faceId, f); }); mesh.kernelTopology.edges.forEach(function (e) { edges.set(e.edgeId, e); }); mesh.kernelTopology.vertices.forEach(function (v) { vertices.set(v.vertexId, v); }); });
        Array.from(faces.values()).sort(function (a, b) { return a.faceId.localeCompare(b.faceId); }).forEach(function (face) {
            try { var seed = measureSeed(face, metrics); if (seed) { seeds.set(face.faceId, seed); result.observations.push.apply(result.observations, seed.observations); } }
            catch (error) { result.unresolvedByFace[face.faceId] = error.message; }
        });
        faces.forEach(function (face) {
            if (face.trims.surfaceBasis.type !== 'bspline' || seeds.has(face.faceId)) { return; }
            var edgeIds = sorted(coedges(face).map(function (x) { return x.use.edgeId; }));
            var neighbors = sorted(edgeIds.flatMap(function (id) { return edges.get(id).uses.map(function (u) { return u.faceId; }); })).filter(function (id) { return seeds.has(id); });
            if (!neighbors.length) { return; }
            var records = neighbors.map(function (id) { return seeds.get(id); }), reference = records[0].support;
            var radii = Array.from(new Set(records.map(function (s) { return s.support.radius; }))).sort(function (a, b) { return a - b; });
            if (radii.length !== 2 || records.some(function (s) { return s.face.bodyId !== face.bodyId || s.support.orientationSign !== reference.orientationSign || !compatibleAxis(reference, s.support); })) { return; }
            try {
                var profile = root.CncNativeThreadEvidence.profile(face, reference, radii);
                var sideSeen = new Set(), boundaryProofs = [], connections = [];
                coedges(face).forEach(function (entry) {
                    var other = edges.get(entry.use.edgeId).uses.filter(function (u) { return seeds.has(u.faceId); });
                    if (!other.length) { return; }
                    other.forEach(function (neighbor) {
                        connections.push({ entry: entry, neighbor: neighbor, seed: seeds.get(neighbor.faceId) });
                        boundaryProofs.push(root.CncNativeThreadEvidence.sharedBoundary(face, entry, neighbor, seeds.get(neighbor.faceId),
                            profile, reference, metrics.get(entry.use.coedgeId) || []));
                    });
                    var uv = root.CncNativeThreadEvidence.trimBounds(entry.use.pcurve, entry.use.range), b = face.trims.surfaceBasis;
                    var side = Math.abs(mid(uv[0]) - b.uKnots[0]) < Math.abs(mid(uv[0]) - b.uKnots[1]) ? 0 : 1;
                    var expectedRadius = side === 0 ? profile.radiusA : profile.radiusB;
                    if (other.some(function (u) { return Math.abs(seeds.get(u.faceId).support.radius - expectedRadius) > 1e-8; })
                        || Math.max(Math.abs(uv[0][0] - b.uKnots[side]), Math.abs(uv[0][1] - b.uKnots[side])) > (b.uKnots[1] - b.uKnots[0]) * 1e-6) { throw new Error('native_thread_flank_boundary_correspondence'); }
                    sideSeen.add(side);
                });
                if (sideSeen.size !== 2) { throw new Error('native_thread_flank_boundary_correspondence'); }
                var boundaryLead = root.CncNativeThreadEvidence.compatibleBoundaryLead(boundaryProofs.map(function (p) { return p.sourceCompatibleSignedLeadMm; }));
                profile.radialCompatibility = root.CncNativeThreadEvidence.pairedRadial(face, profile, connections, metrics, vertices);
                flanks.push({ face: face, neighbors: neighbors, edgeIds: edgeIds, reference: reference, profile: profile,
                    boundaryProofs: boundaryProofs, boundaryLead: boundaryLead });
            } catch (error) { result.unresolvedByFace[face.faceId] = error.message; }
        });
        var adjacency = new Map(Array.from(seeds.keys(), function (id) { return [id, new Set()]; }));
        flanks.forEach(function (f) { f.neighbors.forEach(function (id) { f.neighbors.forEach(function (other) { adjacency.get(id).add(other); }); }); });
        var seen = new Set();
        Array.from(seeds.keys()).sort().forEach(function (id) {
            if (seen.has(id)) { return; }
            var queue = [id], ids = [];
            while (queue.length) { var current = queue.pop(); if (seen.has(current)) { continue; } seen.add(current); ids.push(current); adjacency.get(current).forEach(function (other) { queue.push(other); }); }
            ids.sort(); var ownedFlanks = flanks.filter(function (f) { return f.neighbors.every(function (id) { return ids.includes(id); }); });
            if (!ownedFlanks.length) { return; }
            var records = ids.map(function (id) { return seeds.get(id); }), reference = records[0].support;
            var radii = Array.from(new Set(records.map(function (s) { return s.support.radius; }))).sort(function (a, b) { return a - b; });
            var observations = records.flatMap(function (s) { return s.observations; });
            var lead = intersection(observations.map(function (o) { return o.sourceCompatibleLeadMm; }));
            if (radii.length !== 2 || lead[0] > lead[1] || lead[0] * lead[1] <= 0) { ids.forEach(function (id) { result.unresolvedByFace[id] = 'native_thread_lead_incompatible'; }); return; }
            var start = starts(records, ownedFlanks, reference, lead, radii[0]), issues = [];
            if (!start.startCount) { issues.push('thread_pitch_lead_ambiguous'); }
            var allIds = sorted(ids.concat(ownedFlanks.map(function (f) { return f.face.faceId; }))), roles = {};
            records.forEach(function (s) { roles[s.face.faceId] = (s.support.radius === radii[0]) === (reference.orientationSign < 0) ? 'crest' : 'root'; });
            ownedFlanks.forEach(function (f) { roles[f.face.faceId] = 'flank'; });
            var ends = entryEnds(allIds, faces, edges, reference);
            if (!ends.length || reference.orientationSign < 0 && !ends.some(function (e) { return e.kind === 'mouth'; })) { issues.push('thread_entry_end_unresolved'); }
            var error = Math.max.apply(Math, records.map(function (s) { return s.sourceErrorMm; }));
            var intervals = union(records.map(function (s) { return axialRange(s.axial, s.support, reference); }), error);
            var domain = finiteDomain(records, ownedFlanks, faces, edges, reference, error), subregions = {}, faceCoverage = {};
            var fullDepth = domain ? domain.intervalMm[1] - domain.intervalMm[0] : null;
            if (!domain) { issues.push('native_thread_finite_terminal_domain_unresolved'); }
            records.forEach(function (s) {
                var faceId = s.face.faceId, range = axialRange(s.axial, s.support, reference), role = roles[faceId];
                faceCoverage[faceId] = range;
                if (!domain) { subregions[faceId] = [{ regionId: faceId + '/unresolved-domain', role: 'unresolved-thread-or-smooth',
                    required: true, status: 'unresolved', nativeFaceId: faceId, trimPredicate: { nativeTrim: true }, axialIntervalMm: range }]; return; }
                var cut = domain.intervalMm, lo = Math.max(range[0], cut[0]), hi = Math.min(range[1], cut[1]), regions = [];
                function region(name, a, b, status) { return { regionId: faceId + '/' + name + '/' + regions.length, nativeFaceId: faceId,
                    role: name, required: true, status: status, axialIntervalMm: [a, b], boundaryUncertaintyMm: error,
                    trimPredicate: { nativeTrim: true, axisLine: { originMm: reference.origin.slice(), unitDirection: reference.axis.slice() },
                        axialHalfSpacesMm: [a, b], terminalProofRefs: domain.proofs.map(function (p) { return p.flankFaceId; }) } }; }
                if (hi > lo) { regions.push(region(role, lo, hi, 'recognized-required-machining')); }
                function extension(a, b) {
                    // Even a one-ULP enclosure remainder stays accounted for.
                    // The source tube cannot distinguish a tiny smooth strip
                    // from terminal correspondence uncertainty, so do not label it.
                    var name = b - a > error ? 'smooth-cylinder-extension' : 'terminal-boundary-uncertainty';
                    regions.push(region(name, a, b, 'unresolved-required-machining-role'));
                    if (name === 'terminal-boundary-uncertainty') { issues.push('native_thread_terminal_boundary_uncertainty'); }
                }
                if (range[0] < cut[0]) { extension(range[0], Math.min(range[1], cut[0])); }
                if (range[1] > cut[1]) { extension(Math.max(range[0], cut[1]), range[1]); }
                if (regions.some(function (r) { return r.role === 'smooth-cylinder-extension'; })) {
                    roles[faceId] = 'mixed-thread-and-smooth-cylinder'; issues.push('native_thread_smooth_extension_requires_machining_role');
                }
                subregions[faceId] = regions;
            });
            ownedFlanks.forEach(function (f) { subregions[f.face.faceId] = [{ regionId: f.face.faceId + '/flank', nativeFaceId: f.face.faceId,
                role: 'flank', required: true, status: 'recognized-required-machining', trimPredicate: { nativeTrim: true },
                axialIntervalMm: axialRange(f.profile.trimAxialEnvelopeMm, f.reference, reference) }]; });
            var measured = observations.map(function (o) { return mid(o.signedLeadMm); }).sort(function (a, b) { return a - b; });
            var measuredLead = Math.abs(measured[Math.floor(measured.length / 2)]), pitch = start.startCount ? measuredLead / start.startCount : null;
            var nominal = nominalMatch(reference.orientationSign < 0, radii, lead, start, ownedFlanks);
            if (!nominal) { issues.push('unresolved_thread_designation'); }
            result.features.push({ featureId: allIds[0] + '/native-thread', kind: reference.orientationSign < 0 ? 'internal_thread' : 'external_thread',
                primaryFaceIds: allIds, faceRoles: roles, faceSubregions: subregions, faceAxialCoverageMm: faceCoverage,
                bodyId: records[0].face.bodyId, topologyRevision: topology.revision,
                required: true, reason: null, recognitionIssues: sorted(issues), diagnosticOnly: true, machiningRequired: null,
                axisLine: { originMm: reference.origin.slice(), unitDirection: reference.axis.slice() }, isInternal: reference.orientationSign < 0,
                handedness: lead[0] > 0 ? 'right' : 'left', measuredMajorDiameterMm: 2 * radii[1], measuredMinorDiameterMm: 2 * radii[0],
                measuredLeadMm: measuredLead, measuredPitchMm: pitch, startCount: start.startCount, entryEnds: ends, accessAxes: [], candidateAccessEnds: ends,
                nominalThread: nominal,
                fullDepthIntervalsMm: domain ? [domain.intervalMm] : [], seedCoverageIntervalsMm: intervals,
                boreOrProfileExtentIntervalsMm: intervals, boreOrProfileExtentMm: intervals.reduce(function (sum, v) { return sum + v[1] - v[0]; }, 0),
                dimensions: { fullDepthMm: fullDepth, depthMm: fullDepth, runoutLengthMm: null },
                runoutExtentStatus: 'not-derived-no-additional-patch-claim',
                uncertainty: { sourceCompatibleSignedLeadMm: lead, sourceBoundaryMm: error },
                evidenceRefs: sorted(observations.map(function (o) { return o.coedgeId; }).concat(allIds)),
                threadEvidence: { contract: 'NativeConnectedThreadEvidence.v1', nativeImportRevision: projection.provenance.nativeImportRevision,
                    observations: observations, startRecurrence: start, finiteDomain: domain,
                    flanks: ownedFlanks.map(function (f) { return { faceId: f.face.faceId, profile: f.profile,
                        sharedBoundaryProofs: f.boundaryProofs, sourceCompatibleBoundaryLeadMm: f.boundaryLead }; }) } });
        });
        result.features.sort(function (a, b) { return a.featureId.localeCompare(b.featureId); }); return result;
    }
    // Arithmetic entry points return measurements only, never trusted owners.
    // The sole ownership entry remains recognize with its local source gate.
    root.CncNativeThreadRecognition = Object.freeze({ recognize: recognize, measureSectionRecurrence: starts,
        compatibleNominal: nominalMatch, measureFiniteTerminalDomain: finiteDomain });
}(typeof self !== 'undefined' ? self : globalThis));
