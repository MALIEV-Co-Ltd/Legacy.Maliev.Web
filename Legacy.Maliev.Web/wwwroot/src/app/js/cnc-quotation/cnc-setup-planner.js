(function (root) {
    'use strict';

    var contracts = root.CncPlanContracts;
    if (!contracts) {
        throw new Error('CNC plan contracts must load before the setup planner.');
    }

    var AXIS_TOLERANCE = 0.000001;

    function values(value) { return Array.isArray(value) ? value : []; }
    function hasText(value) { return typeof value === 'string' && value.trim().length > 0; }

    function plannerError(code, message) {
        var error = new Error(code + ': ' + message);
        error.code = code;
        return error;
    }

    function finite(value) { return typeof value === 'number' && Number.isFinite(value); }

    function normalizeAxis(axis) {
        if (!axis || !finite(axis.x) || !finite(axis.y) || !finite(axis.z)) { return null; }
        var length = Math.sqrt(axis.x * axis.x + axis.y * axis.y + axis.z * axis.z);
        if (!(length > AXIS_TOLERANCE)) { return null; }
        function component(value) {
            var normalized = value / length;
            return Math.abs(normalized) < AXIS_TOLERANCE ? 0 : normalized;
        }
        return { x: component(axis.x), y: component(axis.y), z: component(axis.z) };
    }

    function axisComponent(value) {
        var rounded = Math.round(value * 1000000) / 1000000;
        if (Object.is(rounded, -0)) { rounded = 0; }
        return (rounded >= 0 ? '+' : '') + rounded.toFixed(6);
    }

    function axisKey(axis) {
        return 'axis:' + axisComponent(axis.x) + ',' + axisComponent(axis.y) + ',' + axisComponent(axis.z);
    }

    function sameAxis(left, right) {
        return Math.abs(left.x - right.x) <= AXIS_TOLERANCE
            && Math.abs(left.y - right.y) <= AXIS_TOLERANCE
            && Math.abs(left.z - right.z) <= AXIS_TOLERANCE;
    }

    function axisPriority(axis) {
        var canonical = [
            { x: 0, y: 0, z: 1 }, { x: 0, y: 0, z: -1 },
            { x: 0, y: 1, z: 0 }, { x: 0, y: -1, z: 0 },
            { x: 1, y: 0, z: 0 }, { x: -1, y: 0, z: 0 }
        ];
        var index = canonical.findIndex(function (candidate) { return sameAxis(axis, candidate); });
        return index < 0 ? canonical.length : index;
    }

    function compareAxes(left, right) {
        var priority = axisPriority(left.axis) - axisPriority(right.axis);
        return priority || left.key.localeCompare(right.key);
    }

    function requiredUnresolved(input) {
        return values(input.featureGraph && input.featureGraph.unresolved)
            .concat(values(input.operationGraph && input.operationGraph.unresolved))
            .filter(function (item) { return !item || item.required !== false; });
    }

    function validateInput(input) {
        input = input || {};
        if (!input.featureGraph || !input.operationGraph) {
            throw plannerError('manufacturing_operation_graph_required',
                'Setup planning requires feature and operation graphs.');
        }
        if (!input.topology) {
            throw plannerError('semantic_topology_required',
                'Setup planning requires eligible B-Rep topology.');
        }
        contracts.validateTopology(input.topology);
        if (input.topology.sourceKind !== 'brep'
            || input.topology.automaticPlanningEligible !== true
            || values(input.topology.unresolvedReasons).length > 0) {
            throw plannerError('semantic_topology_required',
                'Only complete eligible B-Rep topology can be planned automatically.');
        }
        var topologyBodies = values(input.topology.bodies);
        var topologyFaces = values(input.topology.faces);
        if (topologyBodies.length === 0 || topologyFaces.length === 0) {
            throw plannerError('semantic_topology_required',
                'Automatic setup planning requires non-empty B-Rep bodies and faces.');
        }
        var bodyIds = new Set();
        topologyBodies.forEach(function (body) {
            if (!body || !hasText(body.id) || bodyIds.has(body.id)) {
                throw plannerError('semantic_topology_required',
                    'B-Rep body IDs must be unique and non-empty.');
            }
            bodyIds.add(body.id);
        });
        var facesById = Object.create(null);
        topologyFaces.forEach(function (face) {
            if (!face || !hasText(face.id) || facesById[face.id]
                || !hasText(face.bodyId) || !bodyIds.has(face.bodyId)) {
                throw plannerError('semantic_topology_required',
                    'B-Rep face IDs and body associations must be unique and valid.');
            }
            facesById[face.id] = face;
        });
        contracts.validateFeatureGraph(input.featureGraph);
        contracts.validateOperationGraph(input.operationGraph, input.featureGraph);
        var revision = input.operationGraph.topologyRevision;
        if (!hasText(revision) || input.featureGraph.topologyRevision !== revision
            || input.topology.revision !== revision) {
            throw plannerError('revision_mismatch', 'Topology, feature, and operation revisions must match.');
        }
        if (requiredUnresolved(input).length > 0) {
            throw plannerError('unresolved_required_feature',
                'Required unresolved manufacturing evidence cannot be assigned to a setup.');
        }
        values(input.featureGraph.features).forEach(function (feature) {
            values(feature && feature.primaryFaceIds).forEach(function (faceId) {
                var topologyFace = facesById[faceId];
                if (!topologyFace || hasText(feature.bodyId) && topologyFace.bodyId !== feature.bodyId) {
                    throw plannerError('semantic_topology_required',
                        'Every feature primary face must resolve to its owning B-Rep body.');
                }
            });
        });
        if (!input.stock || !hasText(input.stock.stateId) || values(input.stock.states).length === 0) {
            throw plannerError('invalid_stock_transition',
                'Setup planning requires explicit stock states and an initial state.');
        }
        var stateIds = new Set();
        values(input.stock.states).forEach(function (state) {
            if (!state || !hasText(state.id) || stateIds.has(state.id)
                || !Array.isArray(state.compatibleFixtureIds)
                || !Array.isArray(state.availableDatumFaceIds)
                || !Array.isArray(state.availableClampFaceIds)) {
                throw plannerError('invalid_stock_transition', 'Stock states must be unique and complete.');
            }
            stateIds.add(state.id);
        });
        if (!stateIds.has(input.stock.stateId)) {
            throw plannerError('invalid_stock_transition', 'The initial stock state is not declared.');
        }
        if (!input.fixtureCatalog || input.fixtureCatalog.contract !== 'CncFixtureCatalog.v2'
            || values(input.fixtureCatalog.candidates).length === 0) {
            throw plannerError('fixture_catalog_required',
                'Setup planning requires an explicit non-empty fixture catalog.');
        }
        return input;
    }

    function featureIndex(featureGraph) {
        return values(featureGraph.features).reduce(function (result, feature) {
            result[feature.id] = feature;
            return result;
        }, Object.create(null));
    }

    function featureAxisKeys(feature) {
        return values(feature.accessAxes).map(normalizeAxis).filter(Boolean).map(axisKey);
    }

    function normalizedOperations(input) {
        var features = featureIndex(input.featureGraph);
        return values(input.operationGraph.operations).map(function (operation) {
            var axis = normalizeAxis(operation.accessAxis);
            if (!axis) {
                throw plannerError('operation_access_axis_required',
                    'Operation ' + String(operation.id) + ' has no usable access axis.');
            }
            var feature = features[operation.featureId];
            if (!feature || featureAxisKeys(feature).indexOf(axisKey(axis)) === -1) {
                throw plannerError('operation_access_axis_mismatch',
                    'Operation ' + String(operation.id) + ' does not use an owning feature access axis.');
            }
            return { operation: operation, axis: axis, axisKey: axisKey(axis) };
        }).sort(function (left, right) { return left.operation.id.localeCompare(right.operation.id); });
    }

    function topologyFaceIndex(topology) {
        return values(topology.faces).reduce(function (result, face) {
            if (face && hasText(face.id)) { result[face.id] = face; }
            return result;
        }, Object.create(null));
    }

    function stockStateIndex(stock) {
        return values(stock.states).reduce(function (result, state) {
            result[state.id] = state;
            return result;
        }, Object.create(null));
    }

    function validateCandidate(source, input, features, topologyFaces, stockStates) {
        if (!source || !hasText(source.id) || !hasText(source.fixtureId)
            || !hasText(source.fixtureState) || !hasText(source.inputStockState)
            || !hasText(source.outputStockState) || source.inputStockState === source.outputStockState
            || values(source.datumFeatureIds).length === 0 || values(source.datumFaceIds).length === 0
            || values(source.clampFaceIds).length === 0 || !source.catalogCapability
            || source.catalogCapability.catalogVersion !== input.fixtureCatalog.version
            || source.catalogCapability.fixtureId !== source.fixtureId
            || !source.datumContact || !source.clampContact || !finite(source.handlingMinutes)
            || source.handlingMinutes < 0) {
            throw plannerError('invalid_fixture_evidence',
                'Every setup candidate requires explicit fixture, datum, workholding, clamp, and stock evidence.');
        }
        var inputState = stockStates[source.inputStockState];
        if (!inputState || !stockStates[source.outputStockState]) {
            throw plannerError('invalid_stock_transition',
                'Candidate ' + source.id + ' references an undeclared stock state.');
        }
        if (values(inputState.compatibleFixtureIds).indexOf(source.fixtureId) === -1) {
            throw plannerError('invalid_fixture_evidence',
                'Candidate ' + source.id + ' fixture is incompatible with its input stock state.');
        }
        var datumFeatures = values(source.datumFeatureIds).map(function (featureId) {
            var feature = features[featureId];
            if (!feature || feature.kind !== 'datum' || values(feature.primaryFaceIds).length === 0) {
                throw plannerError('invalid_fixture_evidence',
                    'Candidate ' + source.id + ' references a non-datum feature.');
            }
            return feature;
        });
        var ownedDatumFaces = new Set(datumFeatures.reduce(function (faceIds, feature) {
            return faceIds.concat(values(feature.primaryFaceIds));
        }, []));
        values(source.datumFaceIds).forEach(function (faceId) {
            if (!hasText(faceId) || !topologyFaces[faceId] || !ownedDatumFaces.has(faceId)
                || values(inputState.availableDatumFaceIds).indexOf(faceId) === -1) {
                throw plannerError('invalid_fixture_evidence',
                    'Candidate ' + source.id + ' datum face is not available B-Rep datum evidence.');
            }
        });
        values(source.clampFaceIds).forEach(function (faceId) {
            if (!hasText(faceId) || !topologyFaces[faceId]
                || values(inputState.availableClampFaceIds).indexOf(faceId) === -1) {
                throw plannerError('invalid_fixture_evidence',
                    'Candidate ' + source.id + ' clamp face is unavailable.');
            }
        });
        if (source.datumContact.topologyFaceId !== source.datumFaceIds[0]
            || source.clampContact.topologyFaceId !== source.clampFaceIds[0]
            || source.datumContact.surfaceKind !== 'plane' || source.clampContact.surfaceKind !== 'plane'
            || !source.fixtureCapability || source.fixtureCapability.fixtureId !== source.fixtureId
            || source.fixtureCapability.catalogVersion !== input.fixtureCatalog.version
            || !finite(source.catalogCapability.maximumOpeningMm)
            || !finite(source.catalogCapability.minimumGripMm)) {
            throw plannerError('invalid_fixture_evidence',
                'Candidate ' + source.id + ' does not match the versioned local fixture capability.');
        }
    }

    function sourceCoverage(source, matchingOperations) {
        var allowed = values(source.operationIds).concat(values(source.supportedOperationIds));
        var blocked = new Set(values(source.blockedOperationIds));
        var supportedTools = values(source.supportedToolClasses);
        return matchingOperations.filter(function (entry) {
            return (allowed.length === 0 || allowed.indexOf(entry.operation.id) !== -1)
                && !blocked.has(entry.operation.id)
                && (supportedTools.length === 0 || supportedTools.indexOf(entry.operation.toolClass) !== -1);
        });
    }

    function stableChecksum(text) {
        var hash = 2166136261;
        for (var index = 0; index < text.length; index += 1) {
            hash ^= text.charCodeAt(index);
            hash = Math.imul(hash, 16777619);
        }
        return (hash >>> 0).toString(16).padStart(8, '0');
    }

    function isEvidenceSetField(field) {
        return /(?:Ids|Kinds|Classes|Axes|Capabilities)$/.test(String(field || ''));
    }

    function canonicalCandidateEvidence(value, field) {
        if (Array.isArray(value)) {
            var items = value.map(function (item) { return canonicalCandidateEvidence(item, null); });
            if (isEvidenceSetField(field)) {
                items.sort(function (left, right) {
                    var leftKey = JSON.stringify(left);
                    var rightKey = JSON.stringify(right);
                    return leftKey < rightKey ? -1 : leftKey > rightKey ? 1 : 0;
                });
            }
            return items;
        }
        if (value && typeof value === 'object') {
            return Object.keys(value).sort().reduce(function (result, key) {
                result[key] = canonicalCandidateEvidence(value[key], key);
                return result;
            }, {});
        }
        return value;
    }

    function candidateIdentity(source, key, coverage) {
        var payload = contracts.canonicalize({ sourceCandidateId: source.id,
            feasibility: canonicalCandidateEvidence(source, null), normalizedOrientation: key,
            coveredOperationIds: coverage.map(function (entry) { return entry.operation.id; }).sort() });
        var serialized = JSON.stringify(payload);
        return { key: 'candidate:' + source.id + ':' + stableChecksum(serialized), payload: serialized };
    }

    function buildCandidates(input, operations) {
        var features = featureIndex(input.featureGraph);
        var topologyFaces = topologyFaceIndex(input.topology);
        var stockStates = stockStateIndex(input.stock);
        var derivedAxes = operations.reduce(function (map, operation) {
            map[operation.axisKey] = operation.axis;
            return map;
        }, Object.create(null));
        var supplied = values(input.fixtureCatalog.candidates);
        var candidates = [];
        var candidateIds = new Set();
        var identities = Object.create(null);
        supplied.forEach(function (candidate) {
            validateCandidate(candidate, input, features, topologyFaces, stockStates);
            if (candidateIds.has(candidate.id)) {
                throw plannerError('ambiguous_fixture_candidate',
                    'Fixture candidate IDs must be unique.');
            }
            candidateIds.add(candidate.id);
            if (candidate.enabled === false) { return; }
            var axis = normalizeAxis(candidate.orientation || candidate.axis || candidate.toolDirection);
            if (!axis) {
                throw plannerError('invalid_fixture_evidence',
                    'Candidate ' + candidate.id + ' requires an explicit orientation.');
            }
            var key = axisKey(axis);
            if (!derivedAxes[key]) { return; }
            var coverage = sourceCoverage(candidate, operations.filter(function (entry) {
                return entry.axisKey === key;
            }));
            if (coverage.length === 0) { return; }
            var identity = candidateIdentity(candidate, key, coverage);
            if (identities[identity.key]) {
                throw plannerError('ambiguous_fixture_candidate',
                    'Fixture candidates have an ambiguous canonical identity.');
            }
            identities[identity.key] = identity.payload;
            candidates.push({ source: candidate, axis: axis, axisKey: key, operations: coverage,
                key: identity.key, identityPayload: identity.payload });
        });
        candidates.sort(function (left, right) {
            var axisOrder = compareAxes({ axis: left.axis, key: left.axisKey },
                { axis: right.axis, key: right.axisKey });
            var leftMinutes = Number(left.source.handlingMinutes) || 0;
            var rightMinutes = Number(right.source.handlingMinutes) || 0;
            return axisOrder || leftMinutes - rightMinutes || left.key.localeCompare(right.key);
        });
        Object.keys(derivedAxes).forEach(function (key) {
            var covered = new Set();
            candidates.forEach(function (candidate) {
                if (candidate.axisKey === key) {
                    candidate.operations.forEach(function (entry) { covered.add(entry.operation.id); });
                }
            });
            if (operations.some(function (entry) { return entry.axisKey === key && !covered.has(entry.operation.id); })) {
                throw plannerError('no_feasible_setup', 'No datum and clamp-feasible setup can reach ' + key + '.');
            }
        });
        return candidates.map(function (candidate) {
            candidate.datumFeatureIds = values(candidate.source.datumFeatureIds).slice().sort();
            candidate.handlingMinutes = candidate.source.handlingMinutes;
            candidate.machiningMinutes = Number.isFinite(Number(candidate.source.machiningMinutes))
                ? Math.max(0, Number(candidate.source.machiningMinutes)) : 0;
            return candidate;
        });
    }

    function orderCandidates(candidates, operations, initialStockState) {
        var candidateByOperation = Object.create(null);
        candidates.forEach(function (candidate) {
            candidate.operations.forEach(function (entry) { candidateByOperation[entry.operation.id] = candidate.key; });
        });
        var dependencies = Object.create(null);
        candidates.forEach(function (candidate) { dependencies[candidate.key] = new Set(); });
        operations.forEach(function (entry) {
            values(entry.operation.predecessors).forEach(function (predecessorId) {
                var predecessorCandidate = candidateByOperation[predecessorId];
                var operationCandidate = candidateByOperation[entry.operation.id];
                if (predecessorCandidate && operationCandidate
                    && predecessorCandidate !== operationCandidate) {
                    dependencies[operationCandidate].add(predecessorCandidate);
                }
            });
        });
        function visit(remaining, emitted, currentStockState, ordered) {
            if (remaining.length === 0) { return ordered; }
            var ready = remaining.filter(function (candidate) {
                return candidate.source.inputStockState === currentStockState
                    && Array.from(dependencies[candidate.key]).every(function (key) { return emitted.has(key); });
            }).sort(compareAxes);
            for (var index = 0; index < ready.length; index += 1) {
                var next = ready[index];
                var nextEmitted = new Set(emitted);
                nextEmitted.add(next.key);
                var result = visit(remaining.filter(function (candidate) {
                    return candidate.key !== next.key;
                }), nextEmitted, next.source.outputStockState, ordered.concat([next]));
                if (result) { return result; }
            }
            return null;
        }
        var result = visit(candidates.slice(), new Set(), initialStockState, []);
        if (!result) {
            throw plannerError('invalid_stock_transition',
                'No predecessor-compatible fixture sequence connects the declared stock states.');
        }
        return result;
    }

    function selectMinimumCandidates(candidates, operations, initialStockState) {
        var visits = 0;
        var maximumVisits = 200000;
        var candidatesByOperation = operations.reduce(function (result, entry) {
            result[entry.operation.id] = candidates.filter(function (candidate) {
                return candidate.operations.some(function (covered) {
                    return covered.operation.id === entry.operation.id;
                });
            }).sort(function (left, right) {
                return left.handlingMinutes + left.machiningMinutes
                    - right.handlingMinutes - right.machiningMinutes || left.key.localeCompare(right.key);
            });
            return result;
        }, Object.create(null));
        var orderedOperations = operations.slice().sort(function (left, right) {
            var optionCount = candidatesByOperation[left.operation.id].length
                - candidatesByOperation[right.operation.id].length;
            return optionCount || left.operation.id.localeCompare(right.operation.id);
        });
        var best = null;
        var stockTransitionRejected = false;
        function visit(index, assignments, selected) {
            visits += 1;
            if (visits > maximumVisits) {
                throw plannerError('setup_search_budget_exceeded',
                    'The deterministic setup search exceeded its bounded browser budget.');
            }
            if (best && selected.size > best.setupCount) { return; }
            if (index === orderedOperations.length) {
                var selectedCandidates = candidates.filter(function (candidate) {
                    return selected.has(candidate.key);
                }).map(function (candidate) {
                    var selectedCandidate = Object.assign({}, candidate);
                    selectedCandidate.operations = operations.filter(function (entry) {
                        return assignments[entry.operation.id] === candidate.key;
                    });
                    selectedCandidate.datumFeatureIds = candidate.datumFeatureIds.slice();
                    return selectedCandidate;
                });
                var orderedCandidates;
                try {
                    orderedCandidates = orderCandidates(selectedCandidates, operations, initialStockState);
                } catch (error) {
                    if (error && error.code === 'invalid_stock_transition') {
                        stockTransitionRejected = true;
                        return;
                    }
                    throw error;
                }
                var handlingAndMachiningMinutes = Array.from(selected).reduce(function (sum, key) {
                    var candidate = candidates.find(function (item) { return item.key === key; });
                    return sum + candidate.handlingMinutes + candidate.machiningMinutes;
                }, 0);
                var canonicalKey = Object.keys(assignments).sort().map(function (operationId) {
                    return operationId + '=' + assignments[operationId];
                }).join('|');
                var score = [selected.size, handlingAndMachiningMinutes, canonicalKey];
                if (!best || score[0] < best.score[0]
                    || score[0] === best.score[0] && score[1] < best.score[1]
                    || score[0] === best.score[0] && score[1] === best.score[1]
                        && score[2].localeCompare(best.score[2]) < 0) {
                    best = { score: score, setupCount: selected.size,
                        assignments: Object.assign({}, assignments), selected: new Set(selected),
                        orderedCandidates: orderedCandidates };
                }
                return;
            }
            var operationId = orderedOperations[index].operation.id;
            candidatesByOperation[operationId].forEach(function (candidate) {
                assignments[operationId] = candidate.key;
                var nextSelected = new Set(selected);
                nextSelected.add(candidate.key);
                visit(index + 1, assignments, nextSelected);
                delete assignments[operationId];
            });
        }
        visit(0, Object.create(null), new Set());
        if (!best) {
            if (stockTransitionRejected) {
                throw plannerError('invalid_stock_transition',
                    'No complete setup assignment has a compatible stock-state sequence.');
            }
            throw plannerError('no_feasible_setup', 'No complete setup assignment exists.');
        }
        return best.orderedCandidates;
    }

    function topologicalOperations(entries, operationsById) {
        var remaining = entries.map(function (entry) { return entry.operation; });
        var emitted = new Set();
        var result = [];
        while (remaining.length > 0) {
            var ready = remaining.filter(function (operation) {
                return values(operation.predecessors).every(function (id) {
                    var predecessor = operationsById[id];
                    return predecessor && (emitted.has(id) || !remaining.some(function (item) { return item.id === id; }));
                });
            }).sort(function (left, right) { return left.id.localeCompare(right.id); });
            if (ready.length === 0) {
                throw plannerError('broken_predecessor', 'Operation predecessors cannot be scheduled.');
            }
            var next = ready[0];
            result.push(next);
            emitted.add(next.id);
            remaining = remaining.filter(function (operation) { return operation.id !== next.id; });
        }
        return result;
    }

    function plan(rawInput) {
        var input = validateInput(rawInput);
        var operations = normalizedOperations(input);
        if (operations.length === 0) {
            throw plannerError('unassigned_operation', 'A setup plan requires at least one explicit operation.');
        }
        var operationsById = operations.reduce(function (result, entry) {
            result[entry.operation.id] = entry.operation;
            return result;
        }, Object.create(null));
        var candidates = selectMinimumCandidates(buildCandidates(input, operations), operations,
            input.stock.stateId);
        var assignments = Object.create(null);
        var setups = candidates.map(function (candidate, index) {
            var setupId = 'setup:' + (index + 1) + ':' + candidate.key;
            var orderedOperations = topologicalOperations(candidate.operations, operationsById);
            orderedOperations.forEach(function (operation) { assignments[operation.id] = setupId; });
            var setup = {
                id: setupId,
                sequence: index + 1,
                number: index + 1,
                orientation: { id: candidate.axisKey, axis: candidate.axis },
                direction: candidate.axis,
                toolDirection: candidate.axis,
                datumFeatureIds: candidate.datumFeatureIds,
                datumFaceIds: values(candidate.source.datumFaceIds).slice().sort(),
                clampFaceIds: values(candidate.source.clampFaceIds).slice().sort(),
                fixtureId: candidate.source.fixtureId,
                fixtureState: candidate.source.fixtureState,
                workholding: candidate.source.fixtureState,
                catalogCapability: candidate.source.catalogCapability,
                datumContact: candidate.source.datumContact,
                clampContact: candidate.source.clampContact,
                fixtureCapability: candidate.source.fixtureCapability,
                operationIds: orderedOperations.map(function (operation) { return operation.id; }),
                inputStockState: candidate.source.inputStockState,
                outputStockState: candidate.source.outputStockState,
                handlingMinutes: candidate.handlingMinutes,
                machiningMinutes: candidate.machiningMinutes
            };
            return setup;
        });
        return {
            contract: 'SetupPlan.v1',
            geometryRevision: input.operationGraph.topologyRevision,
            setups: setups,
            operationAssignments: assignments,
            inputStockState: input.stock.stateId,
            outputStockState: setups[setups.length - 1].outputStockState
        };
    }

    root.CncSetupPlanner = Object.freeze({ plan: plan });
})(typeof self !== 'undefined' ? self : window);
