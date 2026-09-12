// Browser-local CNC manufacturing-plan orchestration and finalized pricing.
'use strict';

self.window = self;
var query = self.location.search || '';
importScripts(
    '/src/app/js/cnc-quotation/cnc-plan-contracts.js' + query,
    '/src/app/js/cnc-quotation/cnc-quotation-config.js' + query,
    '/src/app/js/cnc-quotation/cnc-material-catalog.js' + query,
    '/src/app/js/cnc-quotation/cnc-tool-library.js' + query,
    '/src/app/js/cnc-quotation/cnc-spatial-field.worker.js' + query,
    '/src/app/js/cnc-quotation/cnc-cad-document.js' + query,
    '/src/app/js/cnc-quotation/cnc-native-topology.worker.js' + query,
    '/src/app/js/cnc-quotation/cnc-topology.worker.js' + query,
    '/src/app/js/cnc-quotation/cnc-native-regions.js' + query,
    '/src/app/js/cnc-quotation/cnc-native-thread-evidence.js' + query,
    '/src/app/js/cnc-quotation/cnc-feature-recognition-thread.js' + query,
    '/src/app/js/cnc-quotation/cnc-feature-recognition-prismatic.js' + query,
    '/src/app/js/cnc-quotation/cnc-native-dispatch.js' + query,
    '/src/app/js/cnc-quotation/cnc-feature-graph.worker.js' + query,
    '/src/app/js/cnc-quotation/cnc-process-compiler.js' + query,
    '/src/app/js/cnc-quotation/cnc-reach.js' + query,
    '/src/app/js/cnc-quotation/cnc-fixture-clearance.js' + query,
    '/src/app/js/cnc-quotation/cnc-machine-capability.js' + query,
    '/src/app/js/cnc-quotation/cnc-fixture-catalog.js' + query,
    '/src/app/js/cnc-quotation/cnc-manufacturing-evidence.worker.js' + query,
    '/src/app/js/cnc-quotation/cnc-setup-planner.js' + query,
    '/src/app/js/cnc-quotation/cnc-plan-validator.worker.js' + query,
    '/src/app/js/cnc-quotation/cnc-stock.js' + query,
    '/src/app/js/cnc-quotation/cnc-planning.js' + query,
    '/src/app/js/cnc-quotation/cnc-engine.js' + query);

var PLANNER_VERSION = 'cnc-feature-planner-v3';
var MAX_CACHE_ENTRIES = 96;
var planCache = new Map();
var quoteCache = new Map();
var nativeGeometryCache = new Map();
// Full native CAD is much larger than a material plan. This is a serialized-content
// budget, not a JavaScript heap ceiling. Eviction never requires another CAD import.
var MAX_NATIVE_CACHE_ENTRIES = 4;
var MAX_NATIVE_CACHE_BYTES = 64 * 1024 * 1024;
var nativeGeometryCacheBytes = 0;
var counters = { planValidations: 0, quotes: 0, nativeGeometryValidations: 0 };

function hasText(value) { return typeof value === 'string' && value.trim().length > 0; }
function codedError(code) { var error = new Error(code); error.code = code; return error; }
function cacheSet(cache, key, value) {
    if (cache.size >= MAX_CACHE_ENTRIES) { cache.delete(cache.keys().next().value); }
    cache.set(key, value);
}
function unresolvedEntries(graph) {
    return graph && Array.isArray(graph.unresolved) ? graph.unresolved : [];
}
function unresolvedIdentity(entry) {
    var featureId = entry && hasText(entry.featureId) ? entry.featureId : null;
    var scope = entry && hasText(entry.scope) ? entry.scope : featureId ? 'feature' : 'document';
    var stage = entry && hasText(entry.stage) ? entry.stage
        : scope === 'feature' ? 'recognition' : 'topology';
    return JSON.stringify([scope, stage,
        featureId,
        entry && entry.reason, entry && entry.required !== false]);
}
function cacheNativeGeometry(key, evidence, bytes) {
    function evict() {
        var oldest = nativeGeometryCache.keys().next().value;
        nativeGeometryCacheBytes -= nativeGeometryCache.get(oldest).bytes;
        nativeGeometryCache.delete(oldest);
    }
    while (nativeGeometryCache.size && nativeGeometryCacheBytes > MAX_NATIVE_CACHE_BYTES) { evict(); }
    if (bytes > MAX_NATIVE_CACHE_BYTES) { return; }
    if (nativeGeometryCache.has(key)) { return; }
    while (nativeGeometryCache.size && (nativeGeometryCache.size >= MAX_NATIVE_CACHE_ENTRIES
        || nativeGeometryCacheBytes + bytes > MAX_NATIVE_CACHE_BYTES)) { evict(); }
    nativeGeometryCache.set(key, { evidence: evidence, bytes: bytes });
    nativeGeometryCacheBytes += bytes;
}

function normalizedIssue(issue, fallbackStage) {
    if (hasText(issue)) {
        return { scope: 'document', stage: fallbackStage || 'planning', reason: issue, required: true };
    }
    issue = issue || {};
    var featureId = hasText(issue.featureId) ? issue.featureId : null;
    var result = { scope: hasText(issue.scope) ? issue.scope : featureId ? 'feature' : 'document',
        stage: hasText(issue.stage) ? issue.stage : fallbackStage || 'planning',
        reason: hasText(issue.reason) ? issue.reason : 'cnc_plan_review_required',
        required: issue.required !== false };
    if (featureId) { result.featureId = featureId; }
    return result;
}

function assertInheritedRecognitionReasons(featureGraph, operationGraph) {
    var source = unresolvedEntries(featureGraph);
    var compiled = unresolvedEntries(operationGraph);
    var sourceCounts = Object.create(null);
    var compiledCounts = Object.create(null);
    var sourceRequirements = Object.create(null);
    var sourceSubjects = Object.create(null);
    var operationFeatureIds = new Set((operationGraph && Array.isArray(operationGraph.operations)
        ? operationGraph.operations : []).map(function (operation) { return operation && operation.featureId; }));
    source.forEach(function (entry) {
        if (!entry || !hasText(entry.reason)) { throw codedError('cnc_inherited_recognition_mismatch'); }
        var key = unresolvedIdentity(entry);
        sourceCounts[key] = (sourceCounts[key] || 0) + 1;
        var subject = hasText(entry.featureId) ? entry.featureId : null;
        sourceSubjects[JSON.stringify(subject)] = true;
        var conflictKey = JSON.stringify([subject, entry.reason]);
        var required = entry.required !== false;
        if (Object.prototype.hasOwnProperty.call(sourceRequirements, conflictKey)
            && sourceRequirements[conflictKey] !== required) {
            throw codedError('cnc_inherited_recognition_mismatch');
        }
        sourceRequirements[conflictKey] = required;
        if (hasText(entry.featureId) && operationFeatureIds.has(entry.featureId)) {
            throw codedError('cnc_inherited_recognition_mismatch');
        }
    });
    compiled.forEach(function (entry) {
        if (!entry || !hasText(entry.reason)) { throw codedError('cnc_inherited_recognition_mismatch'); }
        var key = unresolvedIdentity(entry);
        compiledCounts[key] = (compiledCounts[key] || 0) + 1;
        var subject = JSON.stringify(hasText(entry.featureId) ? entry.featureId : null);
        if (sourceSubjects[subject] && !sourceCounts[key]) {
            throw codedError('cnc_inherited_recognition_mismatch');
        }
    });
    Object.keys(sourceCounts).forEach(function (key) {
        if (sourceCounts[key] !== 1 || compiledCounts[key] !== 1) {
            throw codedError('cnc_inherited_recognition_mismatch');
        }
    });
    Object.keys(compiledCounts).forEach(function (key) {
        if (sourceCounts[key] && compiledCounts[key] !== 1) {
            throw codedError('cnc_inherited_recognition_mismatch');
        }
    });
    return true;
}

function reviewEnvelope(reasons, analysisRevision, fallbackStage) {
    var unique = [], issues = [], identities = Object.create(null);
    (Array.isArray(reasons) ? reasons : [reasons]).forEach(function (reason) {
        var issue = normalizedIssue(reason, fallbackStage);
        var identity = unresolvedIdentity(issue);
        if (!identities[identity]) { identities[identity] = true; issues.push(issue); }
        if (hasText(issue.reason) && unique.indexOf(issue.reason) === -1) { unique.push(issue.reason); }
    });
    if (!unique.length) {
        issues.push(normalizedIssue('cnc_plan_review_required', fallbackStage));
        unique.push('cnc_plan_review_required');
    }
    return { status: 'review_required', planStatus: 'review_required', pricingStatus: 'blocked', analysisRevision: hasText(analysisRevision)
        ? analysisRevision : null, reviewReasons: unique, reviewIssues: issues, quote: null };
}
function materialCode(request) { return request.selectedMaterial || request.alloy || request.material; }
function planKey(request, geometryRevision, quotedStock) {
    if (!hasText(geometryRevision) || !hasText(request.requirementsRevision)) {
        throw codedError('revision_mismatch');
    }
    return JSON.stringify([geometryRevision, request.requirementsRevision, PLANNER_VERSION,
        self.CncToolLibrary.version, quotedStock && quotedStock.stockShape,
        quotedStock && quotedStock.stockSizeMm]);
}
function quoteKey(request, plan) {
    return JSON.stringify([plan.planHash, plan.requirementsRevision, materialCode(request), request.quantity,
        request.finish || null, request.certificateRequired === true,
        request.includeShipping !== false]);
}
function attachFixtureCapabilities(setupPlan, fixtureCatalog) {
    var fixtures = Object.create(null);
    (fixtureCatalog && Array.isArray(fixtureCatalog.candidates)
        ? fixtureCatalog.candidates : []).forEach(function (candidate) {
        if (candidate && hasText(candidate.fixtureId)) { fixtures[candidate.fixtureId] = candidate; }
    });
    var result = Object.assign({}, setupPlan);
    result.operationAssignments = Object.assign({}, setupPlan.operationAssignments);
    result.setups = (setupPlan.setups || []).map(function (setup) {
        var fixture = (fixtureCatalog.candidates || []).find(function (candidate) {
            return candidate.fixtureId === setup.fixtureId
                && candidate.fixtureCapability.accessAxes.some(function (axis) {
                    var target = setup.orientation.axis;
                    return Math.abs(axis.x - target.x) < 0.000001 && Math.abs(axis.y - target.y) < 0.000001
                        && Math.abs(axis.z - target.z) < 0.000001;
                });
        });
        if (!fixture || !fixture.fixtureCapability
            || !Array.isArray(fixture.fixtureCapability.accessAxes)
            || !Array.isArray(fixture.fixtureCapability.obstacles)) {
            throw codedError('fixture_validation_evidence_required');
        }
        return Object.assign({}, setup, {
            fixtureCapability: {
                catalogVersion: fixture.fixtureCapability.catalogVersion,
                fixtureId: fixture.fixtureCapability.fixtureId,
                maximumToolReachMm: fixture.fixtureCapability.maximumToolReachMm,
                accessAxes: fixture.fixtureCapability.accessAxes.map(function (axis) {
                    return { x: axis.x, y: axis.y, z: axis.z };
                }),
                obstacles: fixture.fixtureCapability.obstacles.map(function (obstacle) {
                    return { minimum: Object.assign({}, obstacle.minimum),
                        maximum: Object.assign({}, obstacle.maximum) };
                })
            }
        });
    });
    return result;
}

async function buildValidatedPlan(request, topology, featureGraph, quotedStock, emitProgress) {
    emitProgress('process_plan');
    var operationGraph = self.CncProcessCompiler.compile(featureGraph, {
        toolLibraryVersion: self.CncToolLibrary.version
    });
    assertInheritedRecognitionReasons(featureGraph, operationGraph);
    var requiredRecognition = unresolvedEntries(featureGraph).filter(function (entry) {
        return !entry || entry.required !== false;
    });
    if (requiredRecognition.length) {
        return { valid: false, reviewReasons: requiredRecognition.map(function (entry) {
            return entry && entry.reason || 'unresolved_required_feature';
        }) };
    }
    var evidence = {};
    var prepared = self.CncManufacturingEvidence && self.CncManufacturingEvidence.prepare(
        topology, featureGraph, operationGraph, quotedStock);
    evidence.setupStock = prepared && prepared.setupStock;
    evidence.fixtureCatalog = prepared && prepared.fixtureCatalog;
    if (!evidence.setupStock || !evidence.fixtureCatalog) {
        return { valid: false, reviewReasons: ['manufacturing_validation_evidence_required'] };
    }
    var setupPlan = self.CncSetupPlanner.plan({ topology: topology, featureGraph: featureGraph,
        operationGraph: operationGraph, stock: evidence.setupStock,
        fixtureCatalog: evidence.fixtureCatalog });
    setupPlan = attachFixtureCapabilities(setupPlan, evidence.fixtureCatalog);
    if (self.CncManufacturingEvidence) { evidence.validationStock = self.CncManufacturingEvidence.complete(
        topology, featureGraph, operationGraph, setupPlan, quotedStock); }
    if (!evidence.validationStock) { return { valid: false, reviewReasons: ['manufacturing_validation_evidence_required'] }; }
    emitProgress('stock_access_validation');
    counters.planValidations++;
    var validated = await self.CncPlanning.validateManufacturingPlan({
        topology: topology, featureGraph: featureGraph, operationGraph: operationGraph,
        setupPlan: setupPlan, stock: evidence.validationStock,
        requirementsRevision: request.requirementsRevision,
        toolLibraryVersion: self.CncToolLibrary.version
    });
    if (!validated || validated.valid !== true || !validated.plan) { return validated; }
    assertInheritedRecognitionReasons(featureGraph, validated.plan.operationGraph);
    if (JSON.stringify(unresolvedEntries(featureGraph))
        !== JSON.stringify(unresolvedEntries(validated.plan.featureGraph))) {
        throw codedError('cnc_inherited_recognition_mismatch');
    }
    await self.CncPlanContracts.validatePlan(validated.plan);
    return validated;
}

async function estimate(request, emitProgress) {
    request = request || {};
    emitProgress = typeof emitProgress === 'function' ? emitProgress : function () {};
    try {
        var analysisRevision = request.analysisRevision;
        if (!hasText(analysisRevision)) {
            return reviewEnvelope(['analysis_revision_required'], null);
        }
        var geometry = request.geometry || {};
        var topology = request.topology || geometry.cadTopology;
        var expectedRevision = request.geometryRevision || geometry.geometryRevision;
        if (!topology || topology.contract !== 'CncCadTopology.v1'
            || topology.revision !== expectedRevision
            || geometry.geometryRevision && geometry.geometryRevision !== expectedRevision) {
            return reviewEnvelope(['revision_mismatch'], analysisRevision);
        }
        emitProgress('feature_graph');
        var nativeRequest = request.nativeSourceAssociation || geometry.nativeFeatureDiagnostics || self.CncNativeDispatch.isNative(topology);
        var featureGraph = request.featureGraph || geometry.manufacturingFeatureGraph
            || (nativeRequest ? null : self.CncFeatureGraph.build(topology, request.featureRecognition || {}));
        if (nativeRequest) {
            if (!request.nativeSourceAssociation) { return reviewEnvelope(['native_source_association_required'], analysisRevision); }
            var canonicalNative = function (value) { return JSON.stringify(self.CncPlanContracts.canonicalize(value)); };
            if (geometry.cadTopology && canonicalNative(geometry.cadTopology) !== canonicalNative(topology)
                || geometry.manufacturingFeatureGraph && canonicalNative(geometry.manufacturingFeatureGraph) !== canonicalNative(featureGraph)) {
                return reviewEnvelope(['native_payload_alias_mismatch'], analysisRevision);
            }
            // Rehash all incoming content even on a hit: retaining revision strings
            // cannot smuggle a changed face, ownership record, or source envelope.
            var nativeContent = await self.CncNativeDispatch.contentIdentity({ topology: topology, diagnostic: geometry.nativeFeatureDiagnostics,
                graph: featureGraph, association: request.nativeSourceAssociation });
            var cachedNative = nativeGeometryCache.get(nativeContent.hash);
            var nativeGeometry = cachedNative && cachedNative.evidence;
            if (!nativeGeometry) {
                nativeGeometry = await self.CncNativeDispatch.validateTransport(topology, geometry.nativeFeatureDiagnostics,
                    featureGraph, request.nativeSourceAssociation);
                counters.nativeGeometryValidations++;
                cacheNativeGeometry(nativeContent.hash, nativeGeometry, nativeContent.bytes);
            }
            topology = nativeGeometry.topology;
            featureGraph = nativeGeometry.graph;
            // The next compiler receives real owners and inherited required reasons.
            // This bridge cannot certify stock, setup, tool access or target occupancy.
            var nativeOperations = self.CncProcessCompiler.compile(featureGraph, { toolLibraryVersion: self.CncToolLibrary.version });
            self.CncPlanContracts.validateOperationGraph(nativeOperations, featureGraph);
            assertInheritedRecognitionReasons(featureGraph, nativeOperations);
            var nativeReview = reviewEnvelope(featureGraph.unresolved, analysisRevision, 'recognition');
            nativeReview.featureGraph = featureGraph;
            nativeReview.operationGraph = nativeOperations;
            nativeReview.geometryRevision = expectedRevision;
            return nativeReview;
        }
        if (!featureGraph || featureGraph.topologyRevision !== expectedRevision) {
            return reviewEnvelope(['revision_mismatch'], analysisRevision);
        }
        var selected = materialCode(request);
        var stock = self.CncStock.selectStock({ alloy: selected, quantity: request.quantity,
            partSizeMm: geometry.orientedSizeMm, partVolumeMm3: geometry.partVolumeMm3,
            flatPlateEligible: geometry.flatPlateEligible, principalAxes: geometry.principalAxes,
            rotationalEvidence: geometry.rotationalEvidence,
            analysisLimits: geometry.analysisLimits, clampBorderMm: 25,
            includeShipping: request.includeShipping !== false });
        var key = planKey(request, expectedRevision, stock);
        var validatedPlan = planCache.get(key);
        // Cached plans retain the validation hash, not its large mesh. Rehash
        // incoming proof before comparing the same sealed evidence projection.
        if (validatedPlan && (validatedPlan.topology.validationMeshHash || topology.validationMeshHash || topology.validationMesh)) {
            if (!topology.validationMesh || !topology.validationMeshHash
                || await self.CncTopology.validationMeshHash(topology.validationMesh) !== topology.validationMeshHash
                || await self.CncTopology.revisionHash(topology) !== topology.revision) {
                return reviewEnvelope(['validation_mesh_mismatch'], analysisRevision);
            }
        }
        if (validatedPlan && (JSON.stringify(self.CncPlanContracts.topologyEvidence(topology))
                !== JSON.stringify(self.CncPlanContracts.topologyEvidence(validatedPlan.topology))
            || JSON.stringify(self.CncPlanContracts.canonicalize(featureGraph))
                !== JSON.stringify(self.CncPlanContracts.canonicalize(validatedPlan.featureGraph)))) {
            return reviewEnvelope(['cnc_cached_plan_evidence_mismatch'], analysisRevision);
        }
        if (!validatedPlan) {
            var validation = await buildValidatedPlan(request, topology, featureGraph, stock, emitProgress);
            if (!validation || validation.valid !== true || !validation.plan) {
                return reviewEnvelope(validation && validation.reviewReasons, analysisRevision);
            }
            validatedPlan = validation.plan;
            cacheSet(planCache, key, validatedPlan);
        }
        await self.CncPlanContracts.validatePlan(validatedPlan);
        if (validatedPlan.geometryRevision !== expectedRevision
            || validatedPlan.requirementsRevision !== request.requirementsRevision) {
            return reviewEnvelope(['revision_mismatch'], analysisRevision);
        }
        emitProgress('material_time_pricing');
        var qKey = quoteKey(request, validatedPlan);
        var cachedQuote = quoteCache.get(qKey);
        if (!cachedQuote) {
            var commercialPlan = self.CncPlanning.commercializeValidatedPlan({
                plan: validatedPlan, material: selected, geometry: geometry, stock: stock
            });
            cachedQuote = await self.CncQuoteEngine.quote({ quantity: request.quantity,
                material: selected, geometry: geometry, stock: stock, plan: validatedPlan,
                commercialPlan: commercialPlan,
                requirementsRevision: request.requirementsRevision,
                certificateRequired: request.certificateRequired === true,
                finish: request.finish || null });
            counters.quotes++;
            cacheSet(quoteCache, qKey, cachedQuote);
        }
        return { status: 'finalized', analysisRevision: analysisRevision,
            validatedPlan: validatedPlan, quote: cachedQuote };
    } catch (error) {
        return reviewEnvelope([error && error.code || 'cnc_plan_review_required'],
            request.analysisRevision);
    }
}

self.CncQuotationWorker = Object.freeze({ estimate: estimate,
    assertInheritedRecognitionReasons: assertInheritedRecognitionReasons,
    counters: counters });

self.onmessage = async function (event) {
    var message = event.data || {};
    var request = message.request || {};
    var analysisRevision = hasText(request.analysisRevision) ? request.analysisRevision : null;
    try {
        if (!analysisRevision) { throw codedError('analysis_revision_required'); }
        var emitProgress = function (stage) {
            if (message.progressive !== true) { return; }
            self.postMessage({ requestId: message.requestId, success: true,
                analysisRevision: analysisRevision, progress: true, stage: stage });
        };
        var result = await estimate(request, emitProgress);
        self.postMessage({ requestId: message.requestId, success: true,
            analysisRevision: analysisRevision, estimate: result });
    } catch (error) {
        self.postMessage({ requestId: message.requestId, success: false,
            analysisRevision: analysisRevision,
            error: (error && error.message) || 'Unable to calculate the CNC estimate.',
            errorCode: error && error.code || null });
    }
};
