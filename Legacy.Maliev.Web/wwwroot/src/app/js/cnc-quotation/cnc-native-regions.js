(function (root) {
    'use strict';
    var period = 2 * Math.PI, projections = new WeakSet();
    var list = function (x) { return Array.isArray(x) ? x : []; };
    var clone = function (x) { return x === undefined ? null : JSON.parse(JSON.stringify(x)); };
    var record = function (x) { return !!x && typeof x === 'object' && !Array.isArray(x); };
    var identity = function (x) { return typeof x === 'string' && x.trim().length > 0; };
    var equal = function (a, b) { return JSON.stringify(a) === JSON.stringify(b); };
    var same = function (a, b) { return Number.isFinite(a) && Number.isFinite(b) && Math.abs(a - b) <= 64 * Number.EPSILON * Math.max(1, Math.abs(a), Math.abs(b)); };
    var near = function (a, b) { return Array.isArray(a) && Array.isArray(b) && a.length === b.length && a.every(function (v, i) { return same(v, b[i]); }); };
    var interval = function (x) { return Array.isArray(x) && x.length === 2 && x.every(Number.isFinite) && x[0] < x[1]; };
    var unique = function (x) { return new Set(x).size === x.length; };
    var sorted = function (x) { return Array.from(new Set(x)).sort(); };
    function freeze(x) { if (x && typeof x === 'object') { Object.values(x).forEach(freeze); Object.freeze(x); } return x; }
    function requireThat(ok, reason) { if (!ok) { throw new Error('native_band_invalid:' + reason); } }
    function keys(x, names) { return !!x && typeof x === 'object' && !Array.isArray(x) && equal(Object.keys(x).sort(), names.split(' ').sort()); }
    function dot(a, b) { return a.reduce(function (sum, v, i) { return sum + v * b[i]; }, 0); }
    function world(frame, axial) { return frame.origin.map(function (v, i) { return v + frame.axis[i] * axial; }); }
    function validateBand(face, mesh) {
        var b = face.nativeRotationalBand, support = face.support, wires = face.trims.wires;
        requireThat(keys(b, 'schema methodVersion bodyId faceId status reason supportType units coordinateSpace sourceParameterSpace nativeOrientation helicalBoundaryStatus guarantee interpretationGate precision wireOccurrences frame parameters uCoverage profileInterval sides seamPairs boundaryComponents polarity'), 'shape');
        requireThat(b.schema === 'MalievNativeRotationalBand.v1' && b.methodVersion === 1 && b.bodyId === mesh.bodyId && b.faceId === face.faceId
            && b.units === 'millimeter' && b.coordinateSpace === 'import-world' && b.sourceParameterSpace === 'native-surface-uv'
            && b.nativeOrientation === face.orientation && [0, 1].includes(b.nativeOrientation), 'identity-policy');
        requireThat(b.guarantee === 'trusted-native-numerical-rectangular-trim-interpretation'
            && b.interpretationGate === 'separate-same-invocation-native-import-and-metric-ledger-required'
            && keys(b.precision, 'policy epsilonFactor geometricResolutionAllowance outwardEnclosure customerToleranceCertificate')
            && b.precision.policy === 'affine-axis-roundoff-v1' && b.precision.epsilonFactor === 64
            && b.precision.geometricResolutionAllowance === 0 && b.precision.outwardEnclosure === false && b.precision.customerToleranceCertificate === false, 'precision');
        requireThat(Array.isArray(b.wireOccurrences) && b.wireOccurrences.length === wires.length && b.wireOccurrences.every(function (w, i) {
            return keys(w, 'wireId orderedCoedgeIds') && w.wireId === wires[i].wireId && equal(w.orderedCoedgeIds, wires[i].coedges.map(function (u) { return u.coedgeId; }));
        }), 'wire-coverage');
        var unavailable = ['millimeter_units_unverified', 'native_face_unavailable', 'unsupported_support', 'invalid_frame_or_placement', 'incomplete_native_traversal',
            'additional_boundary_component', 'nonreciprocal_occurrence', 'singular_cone_band', 'unsupported_periodic_boundary_representation', 'nonfinite_range',
            'unsupported_pcurve_type', 'oblique_or_helical_boundary', 'native_check_failed', 'parameter_boundary_ambiguous', 'nonrectangular_cycle',
            'multiple_period_winding', 'seam_pair_incomplete', 'unsupported_torus_profile'];
        if (b.status === 'unavailable') {
            requireThat(unavailable.includes(b.reason) && ['unsupported', 'cylinder', 'cone', 'torus'].includes(b.supportType)
                && ['unknown', 'none-affine-axis-boundaries', 'oblique-or-helical'].includes(b.helicalBoundaryStatus)
                && ['frame', 'parameters', 'uCoverage', 'profileInterval', 'sides', 'seamPairs', 'boundaryComponents', 'polarity'].every(function (k) { return b[k] === null; }), 'unavailable-shape');
            requireThat(b.reason !== 'unsupported_support' || !['cylinder', 'cone', 'torus'].includes(support.type), 'unsupported-support');
            requireThat(b.supportType === 'unsupported' || b.supportType === support.type, 'unavailable-support');
            return b;
        }
        requireThat(b.status === 'available' && b.reason === null && ['cylinder', 'cone', 'torus'].includes(b.supportType)
            && b.supportType === support.type && b.helicalBoundaryStatus === 'none-affine-axis-boundaries', 'available-shape');
        requireThat(keys(b.frame, 'origin axis xDirection yDirection placementConvention') && b.frame.placementConvention === 'located-BRepAdaptor-frame-applied-once'
            && support.direct === true && ['origin', 'axis', 'xDirection', 'yDirection'].every(function (k) { return near(b.frame[k], support[k]); }), 'world-frame');
        var f = b.frame;
        var basis = face.trims.surfaceBasis, placement = face.trims.surfaceToImportWorld3x4;
        requireThat(basis.type === support.type && ['origin', 'axis', 'xDirection', 'yDirection'].every(function (key) {
            var v = basis[key];
            return near(f[key], [0, 1, 2].map(function (row) { var offset = row * 4;
                return placement[offset] * v[0] + placement[offset + 1] * v[1] + placement[offset + 2] * v[2] + (key === 'origin' ? placement[offset + 3] : 0);
            }));
        }), 'basis-world-placement');
        requireThat(same(dot(f.axis, f.axis), 1) && same(dot(f.xDirection, f.xDirection), 1) && same(dot(f.yDirection, f.yDirection), 1)
            && same(dot(f.axis, f.xDirection), 0) && same(dot(f.axis, f.yDirection), 0) && same(dot(f.xDirection, f.yDirection), 0), 'orthonormal-frame');
        var cross = [f.xDirection[1] * f.yDirection[2] - f.xDirection[2] * f.yDirection[1], f.xDirection[2] * f.yDirection[0] - f.xDirection[0] * f.yDirection[2], f.xDirection[0] * f.yDirection[1] - f.xDirection[1] * f.yDirection[0]];
        requireThat(near(cross, f.axis), 'frame-handedness');
        var p = b.parameters, radius = support.type === 'torus' ? support.majorRadius : support.type === 'cone' ? support.referenceRadius : support.radius;
        requireThat(keys(p, 'referenceRadius minorRadius semiAngleRadians') && p.referenceRadius === radius
            && p.minorRadius === (support.type === 'torus' ? support.minorRadius : null)
            && p.semiAngleRadians === (support.type === 'cone' ? support.semiAngleRadians : null), 'support-parameters');
        requireThat(p.referenceRadius === (basis.type === 'torus' ? basis.majorRadius : basis.type === 'cone' ? basis.referenceRadius : basis.radius)
            && p.minorRadius === (basis.type === 'torus' ? basis.minorRadius : null)
            && p.semiAngleRadians === (basis.type === 'cone' ? basis.semiAngleRadians : null), 'basis-parameters');
        requireThat(wires.length === 1 && wires[0].role === 'outer' && wires[0].complete && wires[0].connectedClosed && wires[0].closed, 'single-wire');
        var coedges = wires[0].coedges, byId = new Map(coedges.map(function (u) { return [u.coedgeId, u]; }));
        var edgeMap = new Map(mesh.kernelTopology.edges.map(function (e) { return [e.edgeId, e]; }));
        requireThat(unique(coedges.map(function (u) { return u.coedgeId; })) && Array.isArray(b.sides) && b.sides.length === 4, 'side-count');
        requireThat(b.sides.every(function (s) { return keys(s, 'wireId constantAxis startUv endUv orderedCoedgeIds edgeIds occurrences')
            && Array.isArray(s.startUv) && s.startUv.length === 2 && s.startUv.every(Number.isFinite)
            && Array.isArray(s.endUv) && s.endUv.length === 2 && s.endUv.every(Number.isFinite)
            && Array.isArray(s.occurrences) && s.occurrences.every(function (o) { return keys(o, 'coedgeId edgeId nativeRange orientation'); }); }), 'side-shape');
        var covered = [], segments = new Map();
        b.sides.forEach(function (s, sideIndex) {
            requireThat(keys(s, 'wireId constantAxis startUv endUv orderedCoedgeIds edgeIds occurrences') && s.wireId === wires[0].wireId
                && ['u', 'v'].includes(s.constantAxis) && Array.isArray(s.occurrences) && s.occurrences.length > 0, 'side-shape');
            requireThat(equal(s.orderedCoedgeIds, s.occurrences.map(function (u) { return u.coedgeId; }))
                && equal(s.edgeIds, s.occurrences.map(function (u) { return u.edgeId; })), 'side-coverage');
            var constant = s.constantAxis === 'u' ? 0 : 1, varying = 1 - constant, previous;
            s.occurrences.forEach(function (o) {
                var u = byId.get(o.coedgeId), e = edgeMap.get(o.edgeId);
                requireThat(keys(o, 'coedgeId edgeId nativeRange orientation') && u && e && u.edgeId === o.edgeId && u.orientation === o.orientation
                    && equal(u.range, o.nativeRange) && interval(o.nativeRange) && !e.degenerated && e.sameParameter && e.sameRange, 'occurrence-source');
                requireThat(e.uses.length === 2 && e.uses.filter(function (v) { return v.coedgeId === u.coedgeId && v.faceId === face.faceId
                    && v.wireId === s.wireId && v.orientation === u.orientation && v.seam === u.seam; }).length === 1, 'reciprocal-incidence');
                requireThat(u.status === 'complete' && u.pcurve.status === 'exact' && u.pcurve.type === 'line'
                    && u.pcurve.direction[constant] === 0 && u.pcurve.direction[varying] !== 0, 'affine-boundary');
                var ends = u.range.map(function (t) { return u.pcurve.origin.map(function (v, i) { return v + t * u.pcurve.direction[i]; }); });
                if (u.orientation === 1) { ends.reverse(); }
                requireThat([0, 1].includes(u.orientation) && ends.flat().every(Number.isFinite)
                    && !same(ends[0][varying], ends[1][varying]) && (!previous || near(previous, ends[0])), 'ordered-side');
                requireThat(same(ends[0][constant], s.startUv[constant])
                    && (ends[1][varying] - ends[0][varying]) * (s.endUv[varying] - s.startUv[varying]) > 0, 'side-direction');
                if (!previous) { requireThat(near(s.startUv, ends[0]), 'side-start'); }
                previous = ends[1]; covered.push(u.coedgeId); segments.set(u.coedgeId, ends);
            });
            requireThat(near(previous, s.endUv) && near(s.endUv, b.sides[(sideIndex + 1) % 4].startUv)
                && s.constantAxis !== b.sides[(sideIndex + 1) % 4].constantAxis, 'side-cycle');
        });
        requireThat(covered.length === coedges.length && unique(covered) && covered.every(function (v) { return byId.has(v); }), 'complete-coedge-partition');
        var offset = coedges.findIndex(function (u) { return u.coedgeId === covered[0]; });
        requireThat(covered.every(function (v, i) { var u = coedges[(offset + i) % coedges.length], next = coedges[(offset + i + 1) % coedges.length];
            return v === u.coedgeId && u.endVertexId === next.startVertexId; }), 'native-cycle-order');
        var corners = b.sides.map(function (s) { return s.startUv; });
        var ur = [Math.min.apply(null, corners.map(function (v) { return v[0]; })), Math.max.apply(null, corners.map(function (v) { return v[0]; }))];
        var vr = [Math.min.apply(null, corners.map(function (v) { return v[1]; })), Math.max.apply(null, corners.map(function (v) { return v[1]; }))];
        requireThat(interval(ur) && interval(vr) && b.sides.reduce(function (a, s) { return a + (s.startUv[0] - ur[0]) * (s.endUv[1] - vr[0]) - (s.endUv[0] - ur[0]) * (s.startUv[1] - vr[0]); }, 0) > 0, 'rectangle-orientation');
        var coverage = b.uCoverage, full = same(ur[1] - ur[0], period), start = ur[0] % period; if (start < 0) { start += period; }
        var end = start + ur[1] - ur[0], intervals = full ? [[0, period]] : end <= period ? [[start, end]] : [[start, period], [0, end - period]];
        requireThat(keys(coverage, 'kind liftedInterval chartShift winding coveredIntervals') && near(coverage.liftedInterval, ur)
            && coverage.chartShift === 0 && coverage.kind === (full ? 'complete-revolution' : 'partial') && coverage.winding === (full ? 1 : null)
            && ur[1] - ur[0] <= period + 64 * Number.EPSILON * period && list(coverage.coveredIntervals).length === intervals.length
            && coverage.coveredIntervals.every(function (v, i) { return near(v, intervals[i]); }), 'angular-coverage');
        var seamSides = b.sides.filter(function (s) { return s.constantAxis === 'u'; }), seamIds = [];
        requireThat(Array.isArray(b.seamPairs) && b.seamPairs.length === (full ? seamSides[0].occurrences.length : 0), 'seam-count');
        b.seamPairs.forEach(function (pair) {
            requireThat(keys(pair, 'firstCoedgeId secondCoedgeId edgeId'), 'seam-shape');
            var a = byId.get(pair.firstCoedgeId), c = byId.get(pair.secondCoedgeId), ea = segments.get(pair.firstCoedgeId), ec = segments.get(pair.secondCoedgeId);
            requireThat(keys(pair, 'firstCoedgeId secondCoedgeId edgeId') && a && c && a !== c && a.edgeId === pair.edgeId && c.edgeId === pair.edgeId
                && a.seam && c.seam && a.orientation !== c.orientation && equal(a.range, c.range)
                && seamSides[0].orderedCoedgeIds.includes(a.coedgeId) && seamSides[1].orderedCoedgeIds.includes(c.coedgeId)
                && same(ea[0][1], ec[1][1]) && same(ea[1][1], ec[0][1]) && same(Math.abs(ea[0][0] - ec[0][0]), period)
                && edgeMap.get(pair.edgeId).uses.every(function (u) { return u.faceId === face.faceId && u.seam; }), 'seam-identity');
            seamIds.push(a.coedgeId, c.coedgeId);
        });
        requireThat(unique(seamIds) && equal(sorted(seamIds), sorted(coedges.filter(function (u) { return u.seam; }).map(function (u) { return u.coedgeId; })))
            && (!full || equal(sorted(seamIds), sorted(seamSides.flatMap(function (s) { return s.orderedCoedgeIds; })))), 'seam-coverage');
        var expectedSides = b.sides.map(function (s, i) { return i; }).filter(function (i) { return !full || b.sides[i].constantAxis === 'v'; });
        requireThat(Array.isArray(b.boundaryComponents) && b.boundaryComponents.length === expectedSides.length, 'boundary-count');
        b.boundaryComponents.forEach(function (component, i) {
            var index = expectedSides[i], s = b.sides[index];
            requireThat(keys(component, 'sideIndex kind orderedCoedgeIds edgeIds wireId nativeV incidenceSource') && component.sideIndex === index
                && component.kind === (full ? 'ring' : s.constantAxis === 'v' ? 'arc' : 'profile-segment')
                && component.wireId === s.wireId && equal(component.edgeIds, s.edgeIds) && equal(component.orderedCoedgeIds, s.orderedCoedgeIds)
                && component.nativeV === (s.constantAxis === 'v' ? s.startUv[1] : null)
                && component.incidenceSource === 'same-body-finalized-kernelTopology.edges.uses', 'boundary-source');
        });
        var profile = b.profileInterval, axial = vr.slice(), radial = [radius, radius], normals = [1, 1];
        if (b.supportType === 'cone') { axial = vr.map(function (v) { return v * Math.cos(p.semiAngleRadians); }); radial = vr.map(function (v) { return radius + v * Math.sin(p.semiAngleRadians); }).sort(function (a, c) { return a - c; }); }
        if (b.supportType === 'torus') {
            requireThat(p.minorRadius > 0 && radius > p.minorRadius && vr[1] - vr[0] < period - 64 * Number.EPSILON * period && Math.max(Math.abs(vr[0]), Math.abs(vr[1])) <= 1e12, 'torus-profile');
            var points = vr.slice(); for (var k = Math.ceil(vr[0] / (period / 4)); k * period / 4 < vr[1]; k++) { points.push(k * period / 4); }
            var cosines = points.map(Math.cos), sines = points.map(Math.sin);
            normals = [Math.min.apply(null, cosines), Math.max.apply(null, cosines)];
            radial = normals.map(function (v) { return radius + p.minorRadius * v; });
            axial = [Math.min.apply(null, sines) * p.minorRadius, Math.max.apply(null, sines) * p.minorRadius];
        }
        requireThat(keys(profile, 'nativeV axialMm radialMm method') && near(profile.nativeV, vr) && near(profile.axialMm, axial) && near(profile.radialMm, radial)
            && radial[0] > 0 && profile.method === 'elementary-endpoints-and-internal-critical-extrema-numerical', 'profile-measures');
        if (face.orientation === 1) { normals = [-normals[1], -normals[0]]; }
        var polarity = normals[0] > 64 * Number.EPSILON * Math.max(1, Math.abs(normals[0])) ? 'outward'
            : normals[1] < -64 * Number.EPSILON * Math.max(1, Math.abs(normals[1])) ? 'inward' : 'mixed-or-tangent';
        requireThat(keys(b.polarity, 'radial profileOrientationSign') && b.polarity.radial === polarity
            && b.polarity.profileOrientationSign === (face.orientation === 1 ? -1 : 1), 'polarity');
        return b;
    }
    function projectFace(face, mesh, binding, trusted) {
        // Establish the known identity and nonpositive fallback before reading optional
        // evidence. Corrupt transport must not erase diagnostics for unaffected faces.
        var support = record(face.support) ? face.support : {}, instance = record(face.assemblyInstance) ? face.assemblyInstance : {};
        var nativeEdges = list(mesh.kernelTopology && mesh.kernelTopology.edges).filter(record);
        var incidence = nativeEdges.filter(function (e) { return list(e.uses).some(function (u) { return record(u) && u.faceId === face.faceId; }); });
        var r = { regionId: face.faceId + '/native-region', faceId: face.faceId, bodyId: identity(mesh.bodyId) ? mesh.bodyId : face.bodyId || null,
            sourceOccurrenceId: identity(instance.occurrenceId) ? instance.occurrenceId : null,
            nativeImportRevision: binding.nativeImportRevision, supportType: identity(support.type) ? support.type : 'unknown', status: 'unavailable', reason: null,
            nativeOrientation: face.orientation === undefined ? null : face.orientation, measures: clone(face.nativeRegionMeasures),
            wires: clone(list(face.trims && face.trims.wires)), support: clone(face.support), precision: clone(face.precision), band: null, boundaries: [],
            edgeIncidence: incidence.map(function (e) { return { edgeId: e.edgeId, uses: clone(list(e.uses).filter(record)) }; }),
            adjacentFaceIds: sorted(incidence.flatMap(function (e) { return list(e.uses).filter(record).map(function (u) { return u.faceId; }); })
                .filter(function (id) { return identity(id) && id !== face.faceId; })) };
        if (!trusted) { r.reason = 'native_region_source_unverified'; return r; }
        try { r.band = clone(validateBand(face, mesh)); }
        catch (error) { if (!error.message.startsWith('native_band_invalid:')) { throw error; } r.reason = error.message; return r; }
        if (face.support.type === 'plane') {
            r.status = r.measures.status === 'available' ? 'available' : 'unavailable'; r.reason = r.status === 'available' ? null : 'native_plane_measure_unavailable';
            r.contract = 'NativePlanarRegion.v1'; return r;
        }
        if (r.band.status !== 'available') { r.reason = 'native_band_unavailable:' + r.band.reason; return r; }
        r.contract = 'NativeRotationalRegion.v1'; r.status = 'available';
        var edges = new Map(mesh.kernelTopology.edges.map(function (e) { return [e.edgeId, e]; }));
        r.boundaries = r.band.boundaryComponents.map(function (b) {
            var neighbors = b.edgeIds.map(function (id) { return { edgeId: id, uses: clone(edges.get(id).uses.filter(function (u) { return u.faceId !== face.faceId; })) }; });
            return { boundaryId: r.regionId + '/side-' + b.sideIndex, kind: b.kind, edgeIds: sorted(b.edgeIds), orderedCoedgeIds: b.orderedCoedgeIds.slice(),
                nativeV: b.nativeV, worldCenterMm: r.supportType === 'cylinder' ? world(r.band.frame, b.nativeV) : null, neighbors: neighbors };
        });
        return r;
    }
    async function build(topology) {
        var n = topology && topology.cadDocument && topology.cadDocument.nativeImport;
        var trusted = await root.CncNativeTopology.diagnosticSourceVerified(topology);
        var binding = { nativeImportRevision: n && n.importRevision || null, topologyRevision: topology && topology.revision || null, sourceBytesHash: n && n.sourceBytesHash || null };
        var inventory = new Map(), structuralIssues = [], meshes = list(n && n.meshes).filter(record);
        function issue(code) { if (!structuralIssues.some(function (r) { return r.reason === code; })) { structuralIssues.push({ reason: code, required: true }); } }
        if (!meshes.length) { issue('native_region_inventory_missing'); }
        meshes.forEach(function (m) {
            if (!Array.isArray(m.brep_faces) || !m.brep_faces.length) { issue('native_region_face_inventory_missing'); }
            list(m.brep_faces).forEach(function (f) {
                if (!record(f) || !identity(f.faceId)) { issue('native_region_face_identity_missing'); return; }
                if (inventory.has(f.faceId)) { issue('native_region_face_identity_duplicate'); return; }
                inventory.set(f.faceId, { face: f, mesh: m });
            });
        });
        list(topology && topology.faces).filter(record).forEach(function (f) {
            if (!identity(f.id)) { issue('native_region_face_identity_missing'); return; }
            if (!inventory.has(f.id)) {
                issue('native_region_face_record_missing');
                inventory.set(f.id, { face: { faceId: f.id, bodyId: f.bodyId || null }, mesh: { bodyId: f.bodyId || null } });
            }
        });
        if (structuralIssues.length) { trusted = false; }
        var regions = Array.from(inventory.values()).map(function (entry) { return projectFace(entry.face, entry.mesh, binding, trusted); });
        regions.sort(function (a, b) { return a.faceId.localeCompare(b.faceId); });
        var result = { contract: 'NativeManufacturingRegions.v1', units: 'millimeter', coordinateSpace: 'import-world', provenance: binding,
            sourceVerified: trusted, diagnosticOnly: true, structuralIssues: structuralIssues, regions: regions, unresolved: regions.filter(function (r) { return r.status !== 'available'; }).map(function (r) {
                return { faceIds: [r.faceId], reason: r.reason, required: true };
            }) };
        result.revision = await root.CncPlanContracts.hash(result); freeze(result); projections.add(result); return result;
    }
    root.CncNativeRegions = Object.freeze({ build: build, validateBand: validateBand, isLocalProjection: function (x) { return projections.has(x); } });
}(typeof self !== 'undefined' ? self : globalThis));
