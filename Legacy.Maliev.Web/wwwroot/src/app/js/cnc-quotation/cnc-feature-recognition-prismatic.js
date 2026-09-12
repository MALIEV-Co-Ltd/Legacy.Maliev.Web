(function (root) {
    'use strict';
    var sorted = function (x) { return Array.from(new Set(x)).sort(); };
    var equalSet = function (a, b) { return JSON.stringify(sorted(a)) === JSON.stringify(sorted(b)); };
    function freeze(x) { if (x && typeof x === 'object') { Object.values(x).forEach(freeze); Object.freeze(x); } return x; }
    function dot(a, b) { return a.reduce(function (s, v, i) { return s + v * b[i]; }, 0); }
    function mouth(region, boundary, index, regions) {
        if (boundary.kind !== 'ring' || !boundary.edgeIds.length || boundary.neighbors.some(function (n) { return n.uses.length !== 1 || n.uses[0].seam; })) { return null; }
        var ids = sorted(boundary.neighbors.flatMap(function (n) { return n.uses.map(function (u) { return u.faceId; }); }));
        if (ids.length !== 1) { return null; }
        var plane = regions.get(ids[0]);
        if (!plane || plane.supportType !== 'plane' || plane.status !== 'available') { return null; }
        var wires = plane.wires.filter(function (w) { return w.role === 'inner' && equalSet(w.coedges.map(function (u) { return u.edgeId; }), boundary.edgeIds); });
        if (wires.length !== 1 || boundary.neighbors.some(function (n) { return n.uses[0].wireId !== wires[0].wireId
            || !wires[0].coedges.some(function (u) { return u.coedgeId === n.uses[0].coedgeId && u.edgeId === n.edgeId; }); })) { return null; }
        var axis = region.band.frame.axis, sign = index === 0 ? -1 : 1;
        var direction = axis.map(function (v) { return sign * v; });
        if (dot(direction, plane.support.orientedNormal) < 1 - 1e-9) { return null; }
        var delta = boundary.worldCenterMm.map(function (v, i) { return v - plane.support.origin[i]; });
        if (Math.abs(dot(delta, plane.support.orientedNormal)) > Math.max(region.precision.maxTopologyTolerance, plane.precision.maxTopologyTolerance) + 1e-10) { return null; }
        // An annular shoulder leading to another inward band is not an exterior mouth.
        // Read all neighbors even when they will receive separate ownership below.
        var outer = plane.wires.filter(function (w) { return w.role === 'outer'; });
        if (outer.length !== 1) { return null; }
        var outerEdges = outer[0].coedges.map(function (u) { return u.edgeId; });
        var supportedOutside = outerEdges.every(function (edgeId) {
            var edges = plane.edgeIncidence.filter(function (e) { return e.edgeId === edgeId; });
            if (edges.length !== 1 || edges[0].uses.length !== 2) { return false; }
            var neighbors = edges[0].uses.filter(function (u) { return u.faceId !== plane.faceId; });
            if (neighbors.length !== 1 || neighbors[0].seam) { return false; }
            var other = regions.get(neighbors[0].faceId), support = other && other.support;
            if (!support) { return false; }
            // Native support polarity remains known when only the trim-band capability
            // is unavailable. Inward and unsupported neighbors cannot certify a mouth.
            if (support.type === 'cylinder') { return support.orientationSign === 1 && other.nativeOrientation === 0; }
            if (support.type === 'plane' && other.status === 'available') {
                var toCenter = boundary.worldCenterMm.map(function (v, i) { return v - support.origin[i]; });
                return dot(toCenter, support.orientedNormal) < -Math.max(plane.precision.maxTopologyTolerance, other.precision.maxTopologyTolerance);
            }
            return false;
        });
        if (!supportedOutside) { return null; }
        return { role: 'candidate-planar-opening', boundaryId: boundary.boundaryId, planeFaceId: plane.faceId, planeWireId: wires[0].wireId,
            edgeIds: boundary.edgeIds, centerMm: boundary.worldCenterMm, signedDirection: direction, toolAccessVerified: false };
    }
    async function recognize(topology) {
        var projection = await root.CncNativeRegions.build(topology);
        var regions = new Map(projection.regions.map(function (r) { return [r.faceId, r]; }));
        var features = projection.regions.map(function (r) {
            var feature = { featureId: r.faceId + '/native-owner', kind: 'unresolved', primaryFaceIds: [r.faceId], required: true,
                reason: r.reason || 'native_support_consumer_unsupported:' + r.supportType, regionId: r.regionId, regionRevision: projection.revision,
                accessAxes: [], candidateAccessEnds: [], machiningRequired: null, regionEvidence: { contract: 'NativeFeatureRegionEvidence.v1',
                    nativeImportRevision: projection.provenance.nativeImportRevision, regionId: r.regionId, diagnosticOnly: true } };
            if (!projection.sourceVerified || r.status !== 'available') { return feature; }
            if (r.supportType === 'plane') {
                feature.kind = 'datum'; feature.role = 'planar-candidate'; feature.reason = null;
                feature.dimensions = { areaMm2: r.measures.value }; feature.centroidMm = r.measures.centroidMm;
                feature.outerWireId = r.wires.find(function (w) { return w.role === 'outer'; }).wireId;
                feature.innerWireIds = r.wires.filter(function (w) { return w.role === 'inner'; }).map(function (w) { return w.wireId; });
                feature.candidateNormal = r.support.orientedNormal; return feature;
            }
            var b = r.band;
            if (r.supportType !== 'cylinder') { return feature; }
            if (b.uCoverage.kind !== 'complete-revolution' || b.helicalBoundaryStatus !== 'none-affine-axis-boundaries'
                || r.boundaries.length !== 2 || !(b.profileInterval.axialMm[1] > b.profileInterval.axialMm[0])) {
                feature.reason = 'native_simple_cylinder_coverage_unsupported'; return feature;
            }
            var boundaries = r.boundaries.slice().sort(function (a, c) { return a.nativeV - c.nativeV; });
            if (equalSet(boundaries[0].edgeIds, boundaries[1].edgeIds)) { feature.reason = 'native_cylinder_end_identity_ambiguous'; return feature; }
            if (b.polarity.radial === 'inward') {
                var ends = boundaries.map(function (boundary, i) { return mouth(r, boundary, i, regions); });
                if (ends.some(function (end) { return !end; }) || ends[0].planeFaceId === ends[1].planeFaceId) {
                    feature.reason = 'native_hole_opening_or_stepped_chain_unsupported'; return feature;
                }
                feature.kind = 'hole'; feature.role = 'smooth-through-cylinder'; feature.reason = null; feature.candidateAccessEnds = ends;
                feature.regionEvidence.contract = 'NativeHoleRegion.v1'; feature.through = true;
            } else if (b.polarity.radial === 'outward') {
                feature.kind = 'outside_profile'; feature.role = 'outside-cylinder-band'; feature.reason = null;
                feature.regionEvidence.contract = 'NativeOutsideBandRegion.v1';
            } else { return feature; }
            feature.dimensions = { diameterMm: 2 * b.parameters.referenceRadius, depthMm: b.profileInterval.axialMm[1] - b.profileInterval.axialMm[0] };
            feature.axisLine = { originMm: b.frame.origin, unitDirection: b.frame.axis };
            feature.axialIntervalMm = b.profileInterval.axialMm; feature.boundaryIds = boundaries.map(function (boundary) { return boundary.boundaryId; });
            return feature;
        });
        var threads = root.CncNativeThreadRecognition.recognize(topology, projection);
        var threadOwned = new Set(threads.features.flatMap(function (f) { return f.primaryFaceIds; }));
        features = features.filter(function (f) { return !threadOwned.has(f.primaryFaceIds[0]); });
        features.forEach(function (f) { if (threads.unresolvedByFace[f.primaryFaceIds[0]]) { f.reason = threads.unresolvedByFace[f.primaryFaceIds[0]]; } });
        features = features.concat(threads.features).sort(function (a, b) { return a.featureId.localeCompare(b.featureId); });
        var owners = features.flatMap(function (f) { return f.primaryFaceIds; });
        if (new Set(owners).size !== projection.regions.length || owners.length !== projection.regions.length) { throw new Error('native_owner_partition_invalid'); }
        var result = { contract: 'NativeFeatureDiagnostics.v1', diagnosticOnly: true, automaticPlanningEligible: false,
            provenance: projection.provenance, projection: projection, structuralIssues: projection.structuralIssues,
            features: features, unresolved: features.filter(function (f) { return f.kind === 'unresolved'; }) };
        result.revision = await root.CncPlanContracts.hash(result); return freeze(result);
    }
    root.CncNativePrismaticRecognition = Object.freeze({ recognize: recognize });
}(typeof self !== 'undefined' ? self : globalThis));
