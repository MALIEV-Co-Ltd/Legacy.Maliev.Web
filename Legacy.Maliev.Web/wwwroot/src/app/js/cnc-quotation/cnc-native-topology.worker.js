(function (root) {
    'use strict';
    // Local source expectations do not survive structured cloning. Derived diagnostics
    // must start at this verified import boundary, never a caller-owned status flag.
    var verifiedDiagnostics = new WeakMap();
    var list = function (x) { return Array.isArray(x) ? x : []; };
    var vector = function (x) { return Array.isArray(x) && x.length === 3 && x.every(Number.isFinite); };
    var point = function (x) { return vector(x) ? { x: x[0], y: x[1], z: x[2] } : null; };
    var matrix = function (x) { return Array.isArray(x) && x.length === 12 && x.every(Number.isFinite); };
    function id(value) { return typeof value === 'string' && value.trim().length > 0; }
    function unique(items, key) { return items.every(function (x) { return x && id(x[key]); }) && new Set(items.map(function (x) { return x[key]; })).size === items.length; }
    function coordinates(value, dimension) { return Array.isArray(value) && value.length === dimension && value.every(Number.isFinite); }
    function unit(value, dimension) { return coordinates(value, dimension) && Math.abs(Math.hypot.apply(Math, value) - 1) < 1e-9; }
    function perpendicular(a, b) { return Math.abs(a.reduce(function (sum, x, i) { return sum + x * b[i]; }, 0)) < 1e-9; }
    function positive(value) { return Number.isFinite(value) && value > 0; }
    function frame(value, dimension, surface) {
        return coordinates(value.origin, dimension) && unit(value.xDirection, dimension) && unit(value.yDirection, dimension)
            && perpendicular(value.xDirection, value.yDirection)
            && (!surface || (unit(value.axis, 3) && perpendicular(value.axis, value.xDirection) && perpendicular(value.axis, value.yDirection) && typeof value.direct === 'boolean'));
    }
    function knots(values, multiplicities, degree, poles, periodic) {
        return Array.isArray(values) && values.length >= 2 && Array.isArray(multiplicities) && multiplicities.length === values.length
            && values.every(function (v, i) { return Number.isFinite(v) && (!i || v > values[i - 1]); })
            && multiplicities.every(function (v) { return Number.isSafeInteger(v) && v > 0 && v <= degree + 1; })
            && (periodic || multiplicities.reduce(function (sum, v) { return sum + v; }, 0) === poles + degree + 1);
    }
    function curve(value, dimension) {
        if (!value || value.status !== 'exact') { return false; }
        if (value.type === 'line') { return coordinates(value.origin, dimension) && unit(value.direction, dimension); }
        if (['circle', 'ellipse', 'hyperbola', 'parabola'].includes(value.type)) {
            return frame(value, dimension, false) && (value.type === 'circle' ? positive(value.radius)
                : value.type === 'parabola' ? positive(value.focal) : positive(value.majorRadius) && positive(value.minorRadius)
                    && (value.type !== 'ellipse' || value.majorRadius >= value.minorRadius));
        }
        if (!['bspline', 'bezier'].includes(value.type) || !Number.isSafeInteger(value.degree) || value.degree < 1
            || typeof value.rational !== 'boolean' || typeof value.periodic !== 'boolean' || !Array.isArray(value.poles)
            || value.poles.length <= value.degree || !value.poles.every(function (v) { return coordinates(v, dimension); })
            || !Array.isArray(value.weights) || value.weights.length !== value.poles.length || !value.weights.every(positive)) { return false; }
        return value.type === 'bezier' ? !value.periodic && value.poles.length === value.degree + 1
            : knots(value.knots, value.multiplicities, value.degree, value.poles.length, value.periodic);
    }
    function surfaceGeometry(value, basis) {
        if (!value || (basis && value.status !== 'exact')) { return false; }
        if (!basis && value.type === 'unsupported') { return id(value.reason); }
        if (['plane', 'cylinder', 'cone', 'sphere', 'torus'].includes(value.type)) {
            if (!frame(value, 3, true)) { return false; }
            if (value.type === 'plane') { return basis || unit(value.orientedNormal, 3); }
            if (value.type === 'cylinder' || value.type === 'sphere') { return positive(value.radius); }
            if (value.type === 'cone') { return Number.isFinite(value.referenceRadius) && value.referenceRadius >= 0
                && Number.isFinite(value.semiAngleRadians) && Math.abs(value.semiAngleRadians) > 0 && Math.abs(value.semiAngleRadians) < Math.PI / 2; }
            return positive(value.majorRadius) && positive(value.minorRadius);
        }
        if (!basis || !['bspline', 'bezier'].includes(value.type) || !Array.isArray(value.poles) || !Array.isArray(value.weights)
            || value.weights.length !== value.poles.length || !Number.isSafeInteger(value.uDegree) || !Number.isSafeInteger(value.vDegree)
            || value.uDegree < 1 || value.vDegree < 1 || value.poles.length <= value.uDegree
            || !Array.isArray(value.poles[0]) || value.poles[0].length <= value.vDegree
            || !['uRational', 'vRational', 'uPeriodic', 'vPeriodic'].every(function (key) { return typeof value[key] === 'boolean'; })) { return false; }
        var count = value.poles[0].length;
        if (!value.poles.every(function (row, i) { return Array.isArray(row) && row.length === count && row.every(vector)
            && Array.isArray(value.weights[i]) && value.weights[i].length === count && value.weights[i].every(positive); })) { return false; }
        return value.type === 'bezier' ? !value.uPeriodic && !value.vPeriodic && value.poles.length === value.uDegree + 1 && count === value.vDegree + 1
            : knots(value.uKnots, value.uMultiplicities, value.uDegree, value.poles.length, value.uPeriodic)
                && knots(value.vKnots, value.vMultiplicities, value.vDegree, count, value.vPeriodic);
    }
    function record(x) { return !!x && typeof x === 'object' && !Array.isArray(x); }
    function records(x, predicate) { return Array.isArray(x) && x.every(function (v) { return record(v) && (!predicate || predicate(v)); }); }
    function validStructure(n) {
        // Reject non-JSON and nonfinite source values before hashing (JSON would silently turn
        // NaN into null). This is wire validation, not a catch-all for implementation errors.
        var seen = new Set();
        function json(value) {
            if (value === null || typeof value === 'string' || typeof value === 'boolean') { return true; }
            if (typeof value === 'number') { return Number.isFinite(value); }
            if (typeof value !== 'object' || seen.has(value)) { return false; }
            seen.add(value);
            var valid = Object.keys(value).every(function (key) { return json(value[key]); });
            seen.delete(value); return valid;
        }
        if (!record(n) || !json(n) || !record(n.kernelProvenance) || !record(n.kernelProvenance.documentCoverage)) { return false; }
        var d = n.kernelProvenance.documentCoverage;
        return records(d.occurrences, function (o) { return records(o.sourceFaces); })
            && records(n.meshes, function (m) {
                return record(m.attributes) && record(m.attributes.position) && Array.isArray(m.attributes.position.array)
                    && record(m.index) && Array.isArray(m.index.array) && record(m.kernelBody) && records(m.kernelBody.shells)
                    && record(m.kernelTopology) && records(m.kernelTopology.vertices)
                    && records(m.kernelTopology.edges, function (e) { return records(e.uses); })
                    && records(m.brep_faces, function (f) {
                        return record(f.trims) && records(f.trims.wires, function (w) { return records(w.coedges); });
                    });
            });
    }
    async function rejected(reason) {
        var result = { contract: 'CncCadTopology.v1', sourceKind: 'brep', automaticPlanningEligible: false,
            unresolvedReasons: [reason], bodies: [], faces: [], edges: [], validationMesh: null, validationMeshHash: null,
            cadDocument: { contract: 'CadDocument.v2', nativeImport: null, rejectedNativeStructure: true } };
        result.revision = await root.CncTopology.revisionHash(result); return result;
    }
    async function build(envelope, expectation) {
        if (!envelope) { return rejected('native_provenance_missing'); }
        if (!validStructure(envelope)) { return rejected('native_payload_structure_invalid'); }
        var reasons = [], faces = [], edges = [], bodies = [], vertices = [], triangles = [], associations = [];
        function fail(reason) { if (reasons.indexOf(reason) < 0) { reasons.push(reason); } }
        var n = envelope || {}, p = n.kernelProvenance || {}, d = p.documentCoverage || {}, meshes = list(n.meshes), params = n.tessellation || {}, identity = n.buildIdentity || {};
        var interpretation = root.CncNativeInterpretation.assess(n, expectation);
        if (!interpretation.validContract) { return rejected(interpretation.reasons[0]); }
        if (n.contract !== 'CncNativeImport.v1' || p.schema !== 'MalievKernelFaces.v1' || p.identityScope !== 'single-import-result'
            || d.schema !== 'MalievKernelDocument.v1' || !meshes.length || n.analysisProfile !== 'cnc'
            || !/^[a-f0-9]{64}$/.test(n.sourceBytesHash || '') || !/^[a-f0-9]{64}$/.test(identity.jsSha256 || '')
            || !/^[a-f0-9]{64}$/.test(identity.wasmSha256 || '') || identity.exporterSchema !== 'MalievKernelDocument.v1') { fail('native_provenance_missing'); }
        if (n.importRevision !== await root.CncCadDocument.hash(n)) { fail('native_import_revision_mismatch'); }
        if (!/^(step|stp|iges|igs)$/.test(n.sourceFormat || '') || p.millimeterOutput !== true || p.unitsStatus !== 'normalized-import'
            || params.linearUnit !== 'millimeter' || params.linearDeflectionType !== 'absolute_value' || params.linearDeflection !== 0.1) { fail('native_units_unverified'); }
        var transfer = d.sourceTransfer || {}, occurrences = list(d.occurrences), sourceFaces = occurrences.flatMap(function (o) { return list(o.sourceFaces); });
        var format = /^(step|stp)$/.test(n.sourceFormat) ? 'step' : /^(iges|igs)$/.test(n.sourceFormat) ? 'iges' : n.sourceFormat;
        if (transfer.format !== format || transfer.status !== 'available' || !list(transfer.declaredLengthUnits).length) { fail('native_units_unverified'); }
        if (list(transfer.diagnostics).length !== transfer.warningCount + transfer.failureCount || transfer.missingRootCount !== 0
            || transfer.missingFaceEntityCount !== 0 || list(transfer.unsupportedEntityNumbers).length) { fail('native_document_coverage_incomplete'); }
        if (d.enumerationStatus !== 'complete' || d.faceCoverageStatus !== 'complete' || transfer.faceIdentityStatus !== 'complete'
            || d.emittedBodyCount !== meshes.length || d.occurrenceCount !== occurrences.length || !unique(occurrences, 'occurrenceId')
            || d.freeRootCount !== list(d.roots).length || d.sourceFaceCount !== sourceFaces.length
            || !unique(sourceFaces, 'sourceFaceOccurrenceId')) { fail('native_document_coverage_incomplete'); }
        if (transfer.failureCount > 0 || (transfer.warningCount > 0 && !interpretation.interpretationAccepted)) { fail('native_repair_review_required'); }
        if (!interpretation.interpretationAccepted && (p.completeCadDocument !== true || d.status !== 'complete_supported_single_solid')) { fail('native_document_uncertified'); }
        if (p.nativeInterpretation && !interpretation.interpretationAccepted) { interpretation.reasons.forEach(fail); }
        if (meshes.length !== 1 || d.leafOccurrenceCount !== 1) { fail('native_body_selection_required'); }
        var allNativeFaces = meshes.flatMap(function (m) { return list(m.brep_faces); });
        if (!unique(meshes, 'bodyId') || !unique(allNativeFaces, 'faceId') || d.emittedFaceCount !== allNativeFaces.length) { fail('native_membership_incomplete'); }
        var faceById = new Map(allNativeFaces.map(function (f) { return [f.faceId, f]; }));
        var occurrencesById = new Map(occurrences.map(function (o) { return [o.occurrenceId, o]; }));
        occurrences.forEach(function (o) {
            if (!matrix(o.localPlacement3x4) || !matrix(o.worldPlacement3x4)
                || (o.parentOccurrenceId !== null && !occurrencesById.has(o.parentOccurrenceId))) { fail('native_assembly_provenance_unavailable'); }
            var visited = new Set(), ancestor = o;
            while (ancestor) {
                if (visited.has(ancestor.occurrenceId)) { fail('native_assembly_provenance_unavailable'); break; }
                visited.add(ancestor.occurrenceId); ancestor = occurrencesById.get(ancestor.parentOccurrenceId);
            }
            if (o.edgesOutsideFaces !== 0 || o.verticesOutsideEdges !== 0 || (o.leaf && o.sourceFaceCount !== list(o.sourceFaces).length)) { fail('native_document_coverage_incomplete'); }
        });
        var roots = list(d.roots), parentless = occurrences.filter(function (o) { return o.parentOccurrenceId === null; });
        if (!roots.every(id) || new Set(roots).size !== roots.length || roots.length !== parentless.length
            || parentless.some(function (o) { return !roots.includes(o.occurrenceId); })) { fail('native_assembly_provenance_unavailable'); }
        var sourceEmitted = sourceFaces.flatMap(function (f) { return list(f.emittedFaceIds); });
        if (sourceEmitted.length !== allNativeFaces.length || new Set(sourceEmitted).size !== allNativeFaces.length
            || sourceEmitted.some(function (id) { return !faceById.has(id); })) { fail('native_document_coverage_incomplete'); }
        meshes.forEach(function (m, meshIndex) {
            var body = m.kernelBody || {}, topology = m.kernelTopology || {}, nativeFaces = list(m.brep_faces), nativeEdges = list(topology.edges), nativeVertices = list(topology.vertices);
            var edgeMap = new Map(nativeEdges.map(function (e) { return [e.edgeId, e]; })), vertexMap = new Map(nativeVertices.map(function (v) { return [v.vertexId, v]; }));
            var positions = list(m.attributes && m.attributes.position && m.attributes.position.array), indices = list(m.index && m.index.array);
            var count = indices.length / 3, covered = new Set(), coedges = new Map(), wireIds = new Set();
            if (!positions.length || positions.length % 3 || positions.some(function (v) { return !Number.isFinite(v); })
                || indices.length % 3 || indices.some(function (v) { return !Number.isSafeInteger(v) || v < 0 || v >= positions.length / 3; })) { fail('native_validation_range_invalid'); }
            if (body.schema !== 'MalievKernelBody.v1' || body.bodyId !== m.bodyId || body.membershipStatus !== 'complete'
                || m.sourceAssociationStatus !== 'unique' || list(m.sourceOccurrenceCandidates).length !== 1) { fail('native_membership_incomplete'); }
            var occurrence = occurrencesById.get(list(m.sourceOccurrenceCandidates)[0]);
            if (!occurrence || !list(occurrence.bodyIds).includes(m.bodyId) || occurrence.bodyAssociationStatus !== 'unique') { fail('native_membership_incomplete'); }
            if (body.sourceKind !== 'solid' || body.boundedSolidEvidence !== true || !body.validity || body.validity.isValid !== true
                || body.infinitePointState !== 'OUT' || body.sourceOrientation !== 0 || list(body.shells).length !== 1
                || list(body.shells).some(function (s) { return s.closureCheck !== 0 || s.orientationCheck !== 0; })) { fail('native_solid_validity_unverified'); }
            if (topology.schema !== 'MalievKernelTopology.v1' || topology.status !== 'complete'
                || !unique(nativeEdges, 'edgeId') || !unique(nativeVertices, 'vertexId')) { fail('native_topology_partial'); }
            nativeVertices.forEach(function (v) { if (v.status !== 'complete' || !vector(v.worldPoint)) { fail('native_topology_partial'); } });
            var shellFaces = list(body.shells).flatMap(function (s) { return list(s.faceIds); });
            if (shellFaces.length !== nativeFaces.length || new Set(shellFaces).size !== nativeFaces.length || shellFaces.some(function (id) { return !nativeFaces.some(function (f) { return f.faceId === id; }); })) { fail('native_membership_incomplete'); }
            nativeFaces.forEach(function (f) {
                var trims = f.trims || {}, support = f.support || {}, instance = f.assemblyInstance || {}, source = sourceFaces.filter(function (s) { return list(s.emittedFaceIds).includes(f.faceId); });
                if (f.bodyId !== m.bodyId || f.solidId !== m.bodyId || f.membershipStatus !== 'complete'
                    || list(f.shellMemberships).length !== 1 || !list(body.shells).some(function (s) { return s.shellId === f.shellMemberships[0] && list(s.faceIds).includes(f.faceId); })
                    || instance.status !== 'resolved' || !occurrencesById.has(instance.occurrenceId)
                    || instance.occurrenceId !== list(m.sourceOccurrenceCandidates)[0]
                    || source.length !== 1 || list(instance.sourceFaceOccurrenceCandidates).length !== 1
                    || (source[0] && source[0].sourceFaceOccurrenceId !== instance.sourceFaceOccurrenceCandidates[0])) { fail('native_membership_incomplete'); }
                var empty = f.last === f.first - 1;
                if (!Number.isSafeInteger(f.first) || !Number.isSafeInteger(f.last) || f.first < 0 || f.first > count || (!empty && (f.last < f.first || f.last >= count))) { fail('native_validation_range_invalid'); }
                else {
                    for (var i = f.first; i <= f.last; i++) { if (covered.has(i)) { fail('native_validation_range_invalid'); } covered.add(i); }
                    if (empty) { fail('native_face_numerical_coverage_missing'); }
                    if (source[0] && source[0].emittedTriangleCount !== f.last - f.first + 1) { fail('native_document_coverage_incomplete'); }
                }
                if (f.orientationStatus !== 'resolved' || ![0, 1].includes(f.orientation) || !matrix(f.placement3x4)
                    || support.coordinateSpace !== 'import-world' || !f.bounds || !vector(f.bounds.min) || !vector(f.bounds.max)
                    || !f.precision || f.precision.status !== 'available') { fail('native_face_geometry_unverified'); }
                if (f.bounds && vector(f.bounds.min) && vector(f.bounds.max) && f.bounds.min.some(function (v, i) { return v > f.bounds.max[i]; })) { fail('native_face_geometry_unverified'); }
                var precision = f.precision || {};
                if (['faceTolerance', 'maxEdgeTolerance', 'maxVertexTolerance', 'maxTopologyTolerance'].some(function (key) { return !Number.isFinite(precision[key]) || precision[key] < 0; })
                    || precision.maxTopologyTolerance !== Math.max(precision.faceTolerance, precision.maxEdgeTolerance, precision.maxVertexTolerance)) { fail('native_face_geometry_unverified'); }
                if (support.type === 'plane' && (!vector(support.orientedNormal) || !vector(support.origin))) { fail('native_face_geometry_unverified'); }
                if (['cylinder', 'cone', 'sphere', 'torus'].includes(support.type) && (!vector(support.origin) || !vector(support.axis))) { fail('native_face_geometry_unverified'); }
                if (!surfaceGeometry(support, false) || !surfaceGeometry(trims.surfaceBasis, true)) { fail('native_face_geometry_unverified'); }
                if (trims.status !== 'complete' || !matrix(trims.surfaceToImportWorld3x4) || !f.adjacency || f.adjacency.status !== 'complete' || !list(trims.wires).length) { fail('native_topology_partial'); }
                var loops = list(trims.wires).map(function (w) {
                    var uses = list(w.coedges);
                    if (!id(w.wireId) || wireIds.has(w.wireId) || ![0, 1].includes(w.orientation)
                        || !w.complete || !w.connectedClosed || !w.closed || w.directEdgeUseCount !== uses.length || w.visitedEdgeUseCount !== uses.length || !uses.length) { fail('native_topology_partial'); }
                    wireIds.add(w.wireId);
                    uses.forEach(function (u, i) {
                        var e = edgeMap.get(u.edgeId);
                        if (!id(u.coedgeId) || coedges.has(u.coedgeId) || u.status !== 'complete' || !e || !vertexMap.has(u.startVertexId) || !vertexMap.has(u.endVertexId)
                            || u.endVertexId !== uses[(i + 1) % uses.length].startVertexId || ![0, 1].includes(u.orientation)
                            || !curve(u.pcurve, 2) || !Array.isArray(u.range) || u.range.length !== 2 || !u.range.every(Number.isFinite) || u.range[1] < u.range[0]) { fail('native_topology_partial'); }
                        coedges.set(u.coedgeId, { faceId: f.faceId, wireId: w.wireId, use: u });
                        if (e && (u.startVertexId !== (u.orientation === 1 ? e.endVertexId : e.startVertexId)
                            || u.endVertexId !== (u.orientation === 1 ? e.startVertexId : e.endVertexId))) { fail('native_topology_partial'); }
                    });
                    var polygon = support.type === 'plane' && uses.length >= 3 && uses.every(function (u) { var e = edgeMap.get(u.edgeId); return e && e.curve3d && e.curve3d.type === 'line' && vertexMap.has(u.startVertexId); });
                    return { id: w.wireId, role: w.role, edgeIds: uses.map(function (u) { return u.edgeId; }),
                        representation: polygon ? 'native-ordered-line-polygon' : 'native-ordered-coedges',
                        vertices: polygon ? uses.map(function (u) { return point(vertexMap.get(u.startVertexId).worldPoint); }) : [], nativeWire: w };
                });
                var surface = { kind: support.type || 'unsupported', centerMm: point(support.origin), axis: point(support.axis), normal: point(support.orientedNormal),
                    radiusMm: support.radius === undefined ? (support.referenceRadius === undefined ? null : support.referenceRadius) : support.radius,
                    halfAngleRadians: support.semiAngleRadians === undefined ? null : support.semiAngleRadians,
                    minorRadiusMm: support.minorRadius === undefined ? null : support.minorRadius, majorRadiusMm: support.majorRadius === undefined ? null : support.majorRadius,
                    angularSpanRadians: null, axialIntervalMm: null, boundsMm: f.bounds ? { min: point(f.bounds.min), max: point(f.bounds.max) } : null, nativeSupport: support };
                faces.push({ id: f.faceId, bodyId: f.bodyId, surface: surface, orientation: f.orientation === 1 ? 'reversed' : 'forward',
                    loops: loops, triangleRange: { first: f.first, last: f.last }, nativeFace: f, adjacentFaceIds: Array.from(new Set(nativeEdges.filter(function (e) { return list(e.uses).some(function (u) { return u.faceId === f.faceId; }); }).flatMap(function (e) { return list(e.uses).map(function (u) { return u.faceId; }); }))).filter(function (id) { return id !== f.faceId; }) });
            });
            var seenUses = new Set();
            nativeEdges.forEach(function (e) {
                if (e.status !== 'complete' || e.adjacencyStatus !== 'complete' || !vertexMap.has(e.startVertexId) || !vertexMap.has(e.endVertexId)
                    || (!e.degenerated && list(e.uses).length !== 2)) { fail('native_topology_partial'); }
                var uses = list(e.uses), ownerCount = new Set(uses.map(function (u) { return u.faceId; })).size;
                var adjacency = uses.length === 1 ? 'boundary' : uses.length === 2 && ownerCount === 2 ? 'two-face'
                    : uses.length === 2 && ownerCount === 1 ? (uses[0].orientation !== uses[1].orientation && uses.every(function (u) { return u.seam === true; }) ? 'same-face-seam' : 'same-face-repeated') : 'nonmanifold';
                if (e.adjacency !== adjacency || (!e.degenerated && (!curve(e.curve3d, 3) || !matrix(e.curveToImportWorld3x4)
                    || !Array.isArray(e.range) || e.range.length !== 2 || !e.range.every(Number.isFinite) || e.range[1] < e.range[0]))) { fail('native_topology_partial'); }
                list(e.uses).forEach(function (u) {
                    var record = coedges.get(u.coedgeId);
                    if (seenUses.has(u.coedgeId) || !record || record.faceId !== u.faceId || record.wireId !== u.wireId
                        || record.use.edgeId !== e.edgeId || record.use.orientation !== u.orientation || record.use.seam !== u.seam) { fail('native_topology_partial'); }
                    seenUses.add(u.coedgeId);
                });
                edges.push({ id: e.edgeId, bodyId: m.bodyId, useCount: list(e.uses).length, adjacentFaceIds: Array.from(new Set(list(e.uses).map(function (u) { return u.faceId; }))), nativeEdge: e });
            });
            if (seenUses.size !== coedges.size) { fail('native_topology_partial'); }
            if (covered.size !== count) { fail('native_validation_range_invalid'); }
            var offset = vertices.length;
            for (var j = 0; j + 2 < positions.length; j += 3) { vertices.push({ x: positions[j], y: positions[j + 1], z: positions[j + 2] }); }
            for (var k = 0; k + 2 < indices.length; k += 3) { triangles.push([indices[k] + offset, indices[k + 1] + offset, indices[k + 2] + offset]); }
            bodies.push({ id: m.bodyId, faceIds: nativeFaces.map(function (f) { return f.faceId; }), nativeBody: body });
            associations.push({ bodyId: m.bodyId, validationMeshIndex: meshIndex });
        });
        // Structural native obligations remain independent of the warning-sensitive legacy flags.
        // Numerical display coverage is downstream evidence, not import interpretation authority.
        var structuralFailures = reasons.filter(function (r) { return !['native_face_numerical_coverage_missing'].includes(r); });
        if (structuralFailures.length) { interpretation = Object.assign({}, interpretation, { interpretationAccepted: false, reasons: interpretation.reasons.concat(structuralFailures) }); }
        // Enumeration is not feature-consumer certification. This transport slice cannot authorize machining.
        fail('native_trim_consumer_unsupported');
        // Exact coordinate welding is a numerical diagnostic only; never snap, close or repair CAD.
        var vertexKeys = vertices.map(function (v) { return [v.x, v.y, v.z].join(','); }), incidence = new Map();
        triangles.forEach(function (t) { [[0, 1], [1, 2], [2, 0]].forEach(function (pair) {
            var key = [vertexKeys[t[pair[0]]], vertexKeys[t[pair[1]]]].sort().join('|'); incidence.set(key, (incidence.get(key) || 0) + 1);
        }); });
        var boundary = Array.from(incidence.values()).filter(function (v) { return v === 1; }).length;
        var nonmanifold = Array.from(incidence.values()).filter(function (v) { return v > 2; }).length;
        var watertight = triangles.length > 3 && !boundary && !nonmanifold && !reasons.includes('native_validation_range_invalid') && !reasons.includes('native_solid_validity_unverified');
        if (!watertight) { fail('nonwatertight_validation_mesh'); }
        var mesh = { contract: 'CncBrepValidationMesh.v1', source: 'occt_native_same_import_v1', resolutionMm: 0.5,
            deflectionMm: params.linearDeflection, bodyAssociations: associations, vertices: vertices, triangles: triangles,
            watertight: watertight, boundaryEdgeCount: boundary, nonmanifoldEdgeCount: nonmanifold,
            watertightStatus: 'exact-coordinate-incidence-diagnostic', repair: 'none', nativeImportRevision: n.importRevision };
        var result = { contract: 'CncCadTopology.v1', sourceKind: 'brep', automaticPlanningEligible: false, unresolvedReasons: reasons,
            bodies: bodies, faces: faces, edges: edges, validationMesh: mesh, nativeInterpretation: interpretation,
            cadDocument: { contract: 'CadDocument.v2', nativeImport: envelope || null, coverage: d, repairEvidence: transfer.diagnostics || [], coordinateSpace: 'import-world' } };
        result.validationMeshHash = await root.CncTopology.validationMeshHash(mesh);
        result.revision = await root.CncTopology.revisionHash(result);
        if (interpretation.interpretationAccepted) { verifiedDiagnostics.set(result, { importRevision: n.importRevision, topologyRevision: result.revision }); }
        return result;
    }
    async function diagnosticSourceVerified(topology) {
        var binding = verifiedDiagnostics.get(topology), n = topology && topology.cadDocument && topology.cadDocument.nativeImport;
        if (!binding || !n || n.importRevision !== binding.importRevision || topology.revision !== binding.topologyRevision) { return false; }
        // Hash projection may itself reject a malformed native contract. That is lost
        // source authority, not a reason to discard the identifiable face inventory.
        if (!validStructure(n)) { return false; }
        try {
            return await root.CncCadDocument.hash(n) === binding.importRevision
                && await root.CncTopology.revisionHash(topology) === binding.topologyRevision;
        } catch (error) {
            if (String(error && error.message).startsWith('native_contract_invalid:')) { return false; }
            throw error;
        }
    }
    root.CncNativeTopology = Object.freeze({ build: build, diagnosticSourceVerified: diagnosticSourceVerified });
}(typeof self !== 'undefined' ? self : globalThis));
