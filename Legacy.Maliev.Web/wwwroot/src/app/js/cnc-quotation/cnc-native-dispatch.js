(function (root) {
    'use strict';
    // One pin for the verified CAD loader and the transported-evidence receiver.
    var runtimePin = Object.freeze({ path: '/lib/occt-cnc/0f4759e678ea-191da5c8b62d/',
        manifestSha256: 'f5ee4f75401ce4086e3e8ffb0048f94e3010ac508dc6ae2709f89b4439de8683' });
    var pinnedIdentity;
    function freeze(value) { if (value && typeof value === 'object') { Object.values(value).forEach(freeze); Object.freeze(value); } return value; }
    function clone(value) { return JSON.parse(JSON.stringify(value)); }
    function fail(code) { var error = new Error(code); error.code = code; throw error; }
    async function contentIdentity(value) {
        // Serialize once for both content identity and conservative cache accounting.
        var bytes = new TextEncoder().encode(JSON.stringify(root.CncPlanContracts.canonicalize(value)));
        var digest = await root.crypto.subtle.digest('SHA-256', bytes);
        return { hash: Array.from(new Uint8Array(digest), function (b) { return b.toString(16).padStart(2, '0'); }).join(''), bytes: bytes.byteLength };
    }
    function native(topology) { return !!(topology && (topology.nativeInterpretation
        || topology.cadDocument && (topology.cadDocument.contract === 'CadDocument.v2' || topology.cadDocument.nativeImport)
        || topology.validationMesh && topology.validationMesh.source === 'occt_native_same_import_v1')); }
    async function identity() {
        if (!pinnedIdentity) {
            pinnedIdentity = (async function () {
                var response = await root.fetch(runtimePin.path + 'manifest.js');
                if (!response.ok) { fail('native_runtime_identity_mismatch'); }
                var bytes = await response.arrayBuffer(), hash = await root.crypto.subtle.digest('SHA-256', bytes);
                if (Array.from(new Uint8Array(hash), function (b) { return b.toString(16).padStart(2, '0'); }).join('') !== runtimePin.manifestSha256) {
                    fail('native_runtime_identity_mismatch');
                }
                var source = new TextDecoder().decode(bytes), prefix = 'self.CncNativeBuild = Object.freeze(', suffix = ');\n';
                if (!source.startsWith(prefix) || !source.endsWith(suffix)) { fail('native_runtime_identity_mismatch'); }
                return freeze(JSON.parse(source.slice(prefix.length, -suffix.length)));
            }()).catch(function (error) { pinnedIdentity = null; throw error; });
        }
        return pinnedIdentity;
    }
    async function buildLocal(topology) {
        var diagnostic = await root.CncNativePrismaticRecognition.recognize(topology);
        var n = topology && topology.cadDocument && topology.cadDocument.nativeImport;
        var interpretation = n && n.kernelProvenance && n.kernelProvenance.nativeInterpretation;
        var projection = diagnostic.projection, faceOwners = {}, byFace = new Map(projection.regions.map(function (r) { return [r.faceId, r]; }));
        var features = diagnostic.features.map(function (owner) {
            var feature = Object.assign({}, owner, { id: owner.featureId, bodyId: byFace.get(owner.primaryFaceIds[0]).bodyId,
                secondaryFeatureIds: [], evidenceRefs: owner.primaryFaceIds.slice(), confidence: owner.kind === 'unresolved' ? 'Low' : 'High' });
            if (owner.kind === 'unresolved') { feature.unresolvedReason = owner.reason; }
            owner.primaryFaceIds.forEach(function (id) { if (faceOwners[id]) { fail('native_owner_partition_invalid'); } faceOwners[id] = feature.id; });
            return feature;
        });
        var unresolved = features.map(function (feature) {
            return { scope: 'feature', stage: 'recognition', featureId: feature.id, required: true,
                reason: feature.kind === 'unresolved' ? feature.reason : feature.kind === 'datum'
                    ? 'native_machining_role_unverified' : 'native_tool_access_unverified' };
        });
        var documentReasons = Array.from(new Set((topology && topology.unresolvedReasons || []).filter(function (r) {
            return r !== 'native_trim_consumer_unsupported';
        }).concat(diagnostic.structuralIssues.map(function (r) { return r.reason; }))));
        documentReasons.forEach(function (reason) { unresolved.push({ scope: 'document', stage: 'topology', reason: reason, required: true }); });
        var graph = { contract: 'ManufacturingFeatureGraph.v1', topologyRevision: topology.revision,
            automaticPlanningEligible: false, diagnosticOnly: true, nativeDiagnosticRevision: diagnostic.revision,
            provenance: diagnostic.provenance, recognitionReadiness: { contract: 'NativeRecognitionReadiness.v1',
                status: !projection.sourceVerified || diagnostic.structuralIssues.length ? 'unavailable' : diagnostic.unresolved.length ? 'incomplete' : 'ready',
                sourceVerified: projection.sourceVerified, policy: clone(interpretation && interpretation.policy || null),
                structuralIssues: diagnostic.structuralIssues },
            features: features, faceOwners: faceOwners, machinableFaceIds: projection.regions.map(function (r) { return r.faceId; }), unresolved: unresolved };
        root.CncPlanContracts.validateFeatureGraph(graph);
        graph.revision = await root.CncPlanContracts.hash(graph);
        return freeze({ diagnostic: diagnostic, graph: graph });
    }
    function association(topology, result, expectation) {
        if (!expectation || !topology.cadDocument.nativeImport) { return null; }
        return freeze({ contract: 'NativeSourceAssociation.v1', sourceExpectation: clone(expectation),
            nativeImportRevision: topology.cadDocument.nativeImport.importRevision, topologyRevision: topology.revision,
            diagnosticRevision: result.diagnostic.revision, featureGraphRevision: result.graph.revision });
    }
    async function validateTransport(topology, diagnostic, graph, binding) {
        if (!binding || binding.contract !== 'NativeSourceAssociation.v1' || typeof binding.sourceGeneration !== 'string'
            || !binding.sourceGeneration || !binding.sourceExpectation) { fail('native_source_association_required'); }
        var expected = binding.sourceExpectation, n = topology && topology.cadDocument && topology.cadDocument.nativeImport;
        var canonical = root.CncPlanContracts.canonicalize;
        function equal(a, b) { return JSON.stringify(canonical(a)) === JSON.stringify(canonical(b)); }
        if (!n || expected.manifestUrl !== runtimePin.path + 'manifest.js' || expected.manifestSha256 !== runtimePin.manifestSha256
            || !equal(expected.buildIdentity, await identity())) { fail('native_runtime_identity_mismatch'); }
        if (expected.sourceBytesHash !== n.sourceBytesHash || binding.nativeImportRevision !== n.importRevision
            || binding.topologyRevision !== topology.revision || !diagnostic || binding.diagnosticRevision !== diagnostic.revision
            || !graph || binding.featureGraphRevision !== graph.revision) { fail('native_source_association_mismatch'); }
        // This is JavaScript reconstruction, not another kernel import. The separately
        // captured callback expectation is the trusted channel input, never payload flags.
        var rebuilt = await root.CncNativeTopology.build(n, expected);
        if (!equal(rebuilt, topology)) { fail('native_transported_topology_mismatch'); }
        var derived = await buildLocal(rebuilt);
        if (!equal(derived.diagnostic, diagnostic) || !equal(derived.graph, graph)) { fail('native_transported_recognition_mismatch'); }
        return freeze({ topology: rebuilt, diagnostic: derived.diagnostic, graph: derived.graph });
    }
    root.CncNativeDispatch = Object.freeze({ runtimePin: runtimePin, isNative: native, buildLocal: buildLocal,
        association: association, validateTransport: validateTransport, contentIdentity: contentIdentity });
}(typeof self !== 'undefined' ? self : globalThis));
