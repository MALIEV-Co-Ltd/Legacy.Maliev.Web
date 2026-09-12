#pragma once
#include "kernel-repair.hpp"
#include <emscripten/val.h>
#include <map>
#include <set>

namespace MalievRepair {
using InterpretationValue = emscripten::val;
inline InterpretationValue InterpretationNumber(double number) {
  return std::isfinite(number) ? InterpretationValue(number)
                               : InterpretationValue::null();
}
inline std::string InterpretationString(const InterpretationValue &object,
                                        const char *field) {
  if (object.isNull() || object.isUndefined() ||
      !object.hasOwnProperty(field) ||
      object[field].typeOf().as<std::string>() != "string")
    return "";
  return object[field].as<std::string>();
}
inline bool InterpretationTrue(const InterpretationValue &object,
                               const char *field) {
  return !object.isNull() && !object.isUndefined() &&
         object.hasOwnProperty(field) &&
         object[field].typeOf().as<std::string>() == "boolean" &&
         object[field].as<bool>();
}
inline std::string InterpretationFaceId(const Evidence &e,
                                        const TopoDS_Shape &face) {
  if (face.IsNull())
    return "";
  std::string id;
  for (const auto &emitted : e.emittedFaces)
    if (emitted.face.IsSame(face)) {
      if (!id.empty())
        return "";
      id = emitted.faceId;
    }
  return id;
}
inline const EmittedUse *InterpretationConnectorUse(const Evidence &e,
    const Boundary &boundary, const ConnectorProof &proof) {
  if (!ConnectorProofValid(boundary, proof, e.budget, true)) return nullptr;
  const auto &face = boundary.items[proof.face];
  const auto &post = face.after.uses[proof.postUse];
  const EmittedUse *found = nullptr;
  for (const auto &use : e.emittedUses) {
    if (!use.face.IsSame(face.mapped) || !use.wire.IsEqual(proof.nativeWire) ||
        !use.edge.IsEqual(proof.normalizedEdge)) continue;
    Standard_Real first = 0, last = 0;
    BRep_Tool::Range(use.edge, use.face, first, last);
    if (first != post.first || last != post.last || !use.complete ||
        use.startVertexId.empty() || use.startVertexId != use.endVertexId) continue;
    if (found) return nullptr;
    found = &use;
  }
  return found;
}
inline InterpretationValue
InterpretationLedger(const MalievNativeInterpretation::RequiredLedger &ledger) {
  using MalievNativeInterpretation::ObligationState;
  auto out = InterpretationValue::object(), rows = InterpretationValue::array();
  const auto counts = ledger.Counts();
  out.set("required", counts.required);
  out.set("evaluated", counts.evaluated);
  out.set("unassessed", counts.unassessed);
  out.set("bounded", counts.bounded);
  out.set("exceeds", counts.exceeds);
  out.set("unavailable", counts.unavailable);
  for (size_t i = 0; i < ledger.rows.size(); ++i) {
    const auto &row = ledger.rows[i];
    auto value = InterpretationValue::object(),
         terms = InterpretationValue::array();
    value.set("obligationId", row.occurrence);
    value.set("kind", row.kind);
    value.set("status",
              std::string(row.state == ObligationState::Bounded    ? "bounded"
                          : row.state == ObligationState::Exceeded ? "exceeds"
                          : row.state == ObligationState::Unavailable
                              ? "unavailable"
                              : "unassessed"));
    value.set("reason", row.reason);
    value.set("referencePath", row.referencePath);
    value.set("metricMethod", row.metricMethod);
    value.set("sourceFaceItemId", row.sourceFaceItemId.empty()
                                      ? InterpretationValue::null()
                                      : InterpretationValue(row.sourceFaceItemId));
    value.set("sourceEdgeItemId", row.sourceEdgeItemId.empty()
                                      ? InterpretationValue::null()
                                      : InterpretationValue(row.sourceEdgeItemId));
    value.set("sourceVertexItemId", row.sourceVertexItemId.empty()
                                      ? InterpretationValue::null()
                                      : InterpretationValue(row.sourceVertexItemId));
    value.set("connectorProofId", row.connectorProofId.empty()
                                      ? InterpretationValue::null()
                                      : InterpretationValue(row.connectorProofId));
    value.set("faceId", row.faceId.empty() ? InterpretationValue::null()
                                             : InterpretationValue(row.faceId));
    value.set("wireId", row.wireId.empty() ? InterpretationValue::null()
                                             : InterpretationValue(row.wireId));
    value.set("coedgeId", row.coedgeId.empty() ? InterpretationValue::null()
                                                 : InterpretationValue(row.coedgeId));
    value.set("edgeId", row.edgeId.empty() ? InterpretationValue::null()
                                             : InterpretationValue(row.edgeId));
    value.set("operationId", row.operationId.empty()
                                 ? InterpretationValue::null()
                                 : InterpretationValue(row.operationId));
    value.set("associationStatus", row.associationStatus);
    value.set("sourceOrientation", row.sourceOrientation);
    value.set("postRangeFirst", InterpretationNumber(row.sourceFirst));
    value.set("postRangeLast", InterpretationNumber(row.sourceLast));
    auto sourceRange = InterpretationValue::object();
    if (row.priorRangeCaptured) {
      sourceRange.set("first", InterpretationNumber(row.priorFirst));
      sourceRange.set("last", InterpretationNumber(row.priorLast));
      value.set("sourceRange", sourceRange);
    } else value.set("sourceRange", InterpretationValue::null());
    value.set("rangeIdentityStatus", std::string(
        row.priorRangeCaptured ? "source-and-post-ranges-captured"
                               : "post-range-captured;source-range-unavailable"));
    value.set("composition", std::string("ordered-outward-add-v1"));
    for (size_t j = 0; j < row.pathTerms.size(); ++j) {
      auto term = InterpretationValue::object();
      term.set("termId", row.occurrence + "/term-" + std::to_string(j));
      term.set("kind", j < row.pathTermKinds.size()
                           ? row.pathTermKinds[j]
                           : std::string("explicit-kind-unavailable"));
      term.set("upperBoundMm", InterpretationNumber(row.pathTerms[j]));
      terms.set(j, term);
    }
    value.set("pathTermsMm", terms);
    value.set("upperBoundMm", InterpretationNumber(row.upper));
    rows.set(i, value);
  }
  out.set("rows", rows);
  return out;
}
inline void ExportNativeInterpretationUnchecked(InterpretationValue &provenance,
                                                bool completeBounds) {
  using V = InterpretationValue;
  using MalievNativeInterpretation::NativeCheckState;
  const auto &e = EvidenceStore();
  const auto &lineage = MalievNativeWarning::Store();
  auto out = V::object(), policy = V::object(), binding = V::object(),
       reasons = V::array();
  auto faces = V::array(), bounds = V::array(), checks = V::object(),
       checkRows = V::array();
  auto effects = V::array(), periodic = V::array(), tolerances = V::object(),
       toleranceRows = V::array();
  auto connectorProofs = V::array();
  size_t connectorCount = 0;
  auto diagnostics = V::object(), dispositions = V::array();
  out.set("schema", std::string("MalievNativeInterpretation.v1"));
  out.set("guarantee", std::string("trusted-native-interpretation"));
  out.set("manufacturingEligibility", std::string("not-assessed"));
  policy.set("id", std::string("maliev-native-step-interpretation"));
  policy.set("version", 1);
  policy.set("errorBudgetMm", e.budget > 0 ? V(e.budget) : V::null());
  policy.set("budgetSource", std::string("explicit-caller-policy"));
  policy.set("units", std::string("millimeter"));
  policy.set("assessmentTimeLimitMs", e.assessmentMilliseconds);
  policy.set("occtRevision",
             std::string("d2abb6d844231cb8f29be6894440874a4700e4a5"));
  policy.set("importerRevision",
             std::string("c2148e54b456b571238d35cac037d304053d64b2"));
  binding.set(
      "scope",
      std::string(
          "enclosing-CncNativeImport.v1-source-and-recomputed-revision"));
  binding.set("nativeInvocationId", std::to_string(lineage.generation));
  binding.set("identities", std::string("invocation-local-native-occurrences"));
  out.set("policy", policy);
  out.set("binding", binding);
  provenance.set("nativeInterpretation", out);
  if (!(e.budget > 0)) {
    out.set("status", std::string("not-requested"));
    out.set("reasons", reasons);
    return;
  }
  size_t reasonCount = 0;
  std::set<std::string> distinctReasons;
  auto require = [&](bool condition, const std::string &reason) {
    if (!condition && distinctReasons.insert(reason).second)
      reasons.set(reasonCount++, reason);
    return condition;
  };
  // JavaScript property access on null/undefined throws outside the native
  // exception boundary. Reject missing transfer evidence before dereferencing
  // it; an absent diagnostic inventory is not an empty successful inventory.
  auto record = [](const V &value) {
    return !value.isNull() && !value.isUndefined() &&
           value.typeOf().as<std::string>() == "object" &&
           !V::global("Array").call<bool>("isArray", value);
  };
  const auto document = provenance["documentCoverage"];
  if (!record(document))
    throw Standard_Failure("native-interpretation-document-coverage-missing-or-malformed");
  const auto transfer = document["sourceTransfer"];
  if (!record(transfer) ||
      !V::global("Array").call<bool>("isArray", transfer["diagnostics"]))
    throw Standard_Failure("native-interpretation-diagnostics-missing-or-malformed");
  const auto diagnosticInventory = transfer["diagnostics"];
  for (size_t i = 0; i < diagnosticInventory["length"].as<size_t>(); ++i) {
    const auto diagnostic = diagnosticInventory[i];
    if (!record(diagnostic) ||
        InterpretationString(diagnostic, "diagnosticOccurrenceId").empty() ||
        (!diagnostic["nativeDeliveryId"].isNull() &&
         diagnostic["nativeDeliveryId"].typeOf().as<std::string>() != "string") ||
        InterpretationString(diagnostic, "severity").empty() ||
        !V::global("Number").call<bool>("isSafeInteger", diagnostic["entityNumber"]))
      throw Standard_Failure("native-interpretation-diagnostics-missing-or-malformed");
  }
  const bool step = InterpretationString(transfer, "format") == "step";
  require(step, "unsupported-source-format");
  require(InterpretationString(provenance["sourceCoverage"], "status") ==
              "complete_supported_single_solid",
          "source-root-face-coverage-incomplete");
  require(e.boundaries.size() == 1, "requires-single-transfer-boundary");
  require(completeBounds, "source-bound-coverage-incomplete");
  require(e.emittedBodies.size() == 1, "requires-single-emitted-body");
  require(!e.emittedFaces.empty(), "missing-emitted-faces");
  bool invalid = false, changed = !e.periodic.empty();
  size_t nativeRequired = 0, nativePassed = 0, nativeFailed = 0,
         nativeUnavailable = 0, nativeUnassessed = 0;
  size_t faceCount = 0, effectCount = 0, toleranceCount = 0;
  MalievNativeInterpretation::RequiredLedger metrics;
  MalievNativeInterpretation::ToleranceLedger observed;
  std::vector<int> emittedConsumption(e.emittedFaces.size(), 0);
  std::map<std::string, int> emittedCoedgeMetricConsumption;
  for (const auto &use : e.emittedUses)
    emittedCoedgeMetricConsumption[use.coedgeId] = 0;
  std::set<std::string> goodFaces;
  for (size_t bi = 0; bi < e.boundaries.size(); ++bi) {
    const auto &boundary = e.boundaries[bi];
    const std::string prefix = "boundary-" + std::to_string(bi) + "/";
    require(boundary.finalCorrespondenceComplete,
            "final-native-correspondence-incomplete");
    require(boundary.nativeAnalyzerExecuted, "native-analyzer-unexecuted");
    require(boundary.finalValid, "final-native-shape-invalid");
    invalid =
        invalid || (boundary.nativeAnalyzerExecuted && !boundary.finalValid);
    require(boundary.changedSourceGeometry == 0, "source-geometry-moved");
    require(boundary.changedMembership == 0 && boundary.unmappedUses == 0,
            "source-oriented-cycle-changed");
    require(boundary.requiredMetrics.Complete(),
            "metric-obligations-incomplete");
    require(boundary.tolerances.Complete(),
            "observed-tolerances-incomplete-or-excessive");
    for (size_t i = 0; i < boundary.requiredMetrics.rows.size(); ++i) {
      auto row = boundary.requiredMetrics.rows[i];
      row.occurrence = prefix + row.occurrence;
      const ResidualReference *reference = nullptr;
      for (const auto &candidate : boundary.residualReferences)
        if (candidate.obligation == i) {
          if (reference) { reference = nullptr; break; }
          reference = &candidate;
        }
      if (reference) {
        row.referencePath = reference->path;
        row.metricMethod = reference->metricMethod;
        row.sourceOrientation = reference->orientation;
        row.sourceFirst = reference->first;
        row.sourceLast = reference->last;
        row.priorFirst = reference->sourceFirst;
        row.priorLast = reference->sourceLast;
        row.priorRangeCaptured = reference->sourceRangeCaptured;
        if (reference->face >= 0 && reference->face < (int)boundary.items.size()) {
          row.sourceFaceItemId = prefix + "item-" + std::to_string(reference->face);
          const auto &faceItem = boundary.items[reference->face];
          row.faceId = InterpretationFaceId(e, faceItem.mapped);
          if (reference->edge >= 0 && reference->edge < (int)boundary.items.size()) {
            row.sourceEdgeItemId = prefix + "item-" + std::to_string(reference->edge);
            const auto &edgeItem = boundary.items[reference->edge];
            std::vector<const EmittedUse *> candidates;
            TopoDS_Edge expected = TopoDS::Edge(edgeItem.mapped.Oriented(
                static_cast<TopAbs_Orientation>(reference->orientation)));
            // BRep_Tool::CurveOnSurface(E,F) reverses E when F is reversed.
            // Emission traverses the forward-normalized face, so use that same
            // face-relative convention to select the seam branch/occurrence.
            if (faceItem.after.orientation == TopAbs_REVERSED)
              expected.Reverse();
            for (const auto &use : e.emittedUses) {
              Standard_Real emittedFirst = 0, emittedLast = 0;
              BRep_Tool::Range(use.edge, use.face, emittedFirst, emittedLast);
              if (!faceItem.mapped.IsNull() && use.face.IsSame(faceItem.mapped) &&
                  !edgeItem.mapped.IsNull() && use.edge.IsSame(edgeItem.mapped) &&
                  use.edge.Orientation() == expected.Orientation() &&
                  emittedFirst == reference->first && emittedLast == reference->last)
                candidates.push_back(&use);
            }
            if (candidates.size() == 1) {
              row.wireId = candidates[0]->wireId;
              row.coedgeId = candidates[0]->coedgeId;
              row.edgeId = candidates[0]->edgeId;
              row.associationStatus = candidates[0]->complete
                                          ? "complete-unique-native-occurrence"
                                          : "incomplete-emitted-occurrence";
              ++emittedCoedgeMetricConsumption[candidates[0]->coedgeId];
            } else
              row.associationStatus = candidates.empty()
                                          ? "absent-scope-specific-native-occurrence"
                                          : "ambiguous-native-edge-occurrence";
          } else if (reference->edge == -1 && row.kind == "new-degenerate-connector") {
            const ConnectorProof *proof = nullptr;
            size_t proofIndex = 0;
            for (size_t pi = 0; pi < boundary.connectorProofs.size(); ++pi)
              if (boundary.connectorProofs[pi].obligation == i) {
                if (proof) { proof = nullptr; break; }
                proof = &boundary.connectorProofs[pi];
                proofIndex = pi;
              }
            const auto *use = proof ? InterpretationConnectorUse(e, boundary, *proof) : nullptr;
            if (use) {
              row.wireId = use->wireId;
              row.coedgeId = use->coedgeId;
              row.edgeId = use->edgeId;
              row.sourceVertexItemId = prefix + "item-" + std::to_string(proof->sourceVertex);
              row.connectorProofId = prefix + "connector-" + std::to_string(proofIndex);
              row.associationStatus = "complete-unique-native-occurrence";
              ++emittedCoedgeMetricConsumption[use->coedgeId];
            } else row.associationStatus = "incomplete-native-connector-proof";
          }
        }
      } else row.associationStatus = "absent-residual-reference";
      require(row.state != MalievNativeInterpretation::ObligationState::Bounded ||
                  row.associationStatus == "complete-unique-native-occurrence",
              "metric-emitted-occurrence-association-incomplete");
      require(row.pathTerms.size() == row.pathTermKinds.size(),
              "metric-path-term-kind-incomplete");
      metrics.rows.push_back(row);
    }
    for (size_t pi = 0; pi < boundary.connectorProofs.size(); ++pi) {
      const auto &proof = boundary.connectorProofs[pi];
      const auto *use = InterpretationConnectorUse(e, boundary, proof);
      auto value = V::object(), nativeCheckIds = V::array();
      value.set("connectorProofId", prefix + "connector-" + std::to_string(pi));
      value.set("status", std::string(use ? "complete" : "incomplete"));
      value.set("sourceIdentity", std::string("new-native-identity-minus-one"));
      value.set("sourceFaceItemId", prefix + "item-" + std::to_string(proof.face));
      value.set("sourceVertexItemId", prefix + "item-" + std::to_string(proof.sourceVertex));
      value.set("sourceEdgeItemId", V::null());
      value.set("faceId", use ? V(use->faceId) : V::null());
      value.set("wireId", use ? V(use->wireId) : V::null());
      value.set("coedgeId", use ? V(use->coedgeId) : V::null());
      value.set("edgeId", use ? V(use->edgeId) : V::null());
      value.set("startVertexId", use ? V(use->startVertexId) : V::null());
      value.set("endVertexId", use ? V(use->endVertexId) : V::null());
      value.set("postCoedgeOccurrence", proof.postUse);
      value.set("normalizedLoopOccurrence", proof.loop);
      value.set("normalizedLoopCoedgeOccurrence", proof.loopUse);
      value.set("obligationId", proof.obligation < boundary.requiredMetrics.rows.size()
          ? V(prefix + boundary.requiredMetrics.rows[proof.obligation].occurrence) : V::null());
      value.set("metricMethod", std::string("outward-degenerate-point-image"));
      value.set("referencePath", std::string("whole-native-degenerate-lift-to-preserved-source-vertex"));
      size_t ni = 0;
      if (use)
        for (size_t ci = 0; ci < boundary.nativeChecks.size(); ++ci)
          if (boundary.nativeChecks[ci].face.IsSame(use->face))
            nativeCheckIds.set(ni++, prefix + "check-" + std::to_string(ci));
      value.set("nativeCheckIds", nativeCheckIds);
      connectorProofs.set(connectorCount++, value);
      require(use != nullptr, "connector-proof-evidence-join-incomplete");
    }
    auto observe = [&](const State &state, const std::string &id,
                       const char *phase) {
      observed.Observe(state.toleranceCaptured, state.tolerance, e.budget);
      auto value = V::object();
      value.set("nativeItemId", id);
      value.set("phase", std::string(phase));
      value.set("captured", state.toleranceCaptured);
      value.set("valueMm", state.toleranceCaptured
                               ? InterpretationNumber(state.tolerance)
                               : V::null());
      toleranceRows.set(toleranceCount++, value);
    };
    for (size_t i = 0; i < boundary.items.size(); ++i) {
      const auto &item = boundary.items[i];
      const auto type = item.original.ShapeType();
      const std::string itemId = prefix + "item-" + std::to_string(i);
      if (type == TopAbs_FACE || type == TopAbs_EDGE || type == TopAbs_VERTEX) {
        observe(item.before, itemId, "before");
        observe(item.after, itemId, "after");
      }
      if (type != TopAbs_FACE)
        continue;
      auto face = V::object(), faceEffects = V::array();
      const std::string faceId = InterpretationFaceId(e, item.mapped);
      bool faceComplete = require(!faceId.empty(),
                                  "source-face-emitted-association-incomplete");
      int matches = 0;
      for (size_t fi = 0; fi < e.emittedFaces.size(); ++fi) {
        const auto &emitted = e.emittedFaces[fi];
        if (!item.mapped.IsNull() && emitted.face.IsEqual(item.mapped)) {
          ++matches;
          ++emittedConsumption[fi];
          faceComplete =
              require(emitted.uniqueSource && emitted.trimsComplete &&
                          emitted.adjacencyComplete,
                      "emitted-face-source-trim-adjacency-incomplete") &&
              faceComplete;
          face.set("sourceOccurrenceId", emitted.sourceOccurrenceId);
          face.set("bodyId", emitted.bodyId);
        }
      }
      faceComplete =
          require(matches == 1, "source-face-emitted-occurrence-not-unique") &&
          faceComplete;
      faceComplete = require(item.before.orientation == item.after.orientation,
                             "source-face-orientation-changed") &&
                     faceComplete;
      faceComplete = faceComplete && item.sourceGeometryUnchanged &&
                     boundary.finalCorrespondenceComplete;
      for (const auto &row : boundary.nativeChecks)
        if (!item.mapped.IsNull() && row.face.IsSame(item.mapped))
          faceComplete = faceComplete && row.state == NativeCheckState::Passed;
      for (size_t use = 0; use < boundary.postObligationIds[i].size(); ++use)
        faceComplete =
            faceComplete &&
            boundary.requiredMetrics.rows.at(boundary.postObligationIds[i][use])
                    .state ==
                MalievNativeInterpretation::ObligationState::Bounded;
      size_t effectIndex = 0;
      auto effect = [&](const char *kind, int postUse = -1) {
        const std::string id =
            itemId + "/effect-" + std::to_string(effectIndex);
        auto value = V::object();
        value.set("effectId", id);
        value.set("faceId", faceId);
        value.set("kind", std::string(kind));
        auto obligationIds = V::array(), coedgeIds = V::array(),
             nativeCheckIds = V::array();
        size_t oi = 0, ci = 0, ni = 0;
        for (size_t use = 0; use < boundary.postObligationIds[i].size(); ++use) {
          if (postUse >= 0 && use != static_cast<size_t>(postUse)) continue;
          const auto obligation = boundary.postObligationIds[i][use];
          obligationIds.set(oi++, prefix + boundary.requiredMetrics.rows[obligation].occurrence);
        }
        std::set<std::string> distinctCoedges;
        for (const auto &use : e.emittedUses) {
          bool occurrenceMatches = postUse < 0;
          if (postUse >= 0 && postUse < static_cast<int>(item.after.uses.size())) {
            const auto &post = item.after.uses[postUse];
            if (post.edge >= 0) {
              TopoDS_Edge expected = TopoDS::Edge(boundary.items[post.edge].mapped.Oriented(
                  static_cast<TopAbs_Orientation>(post.orientation)));
              if (item.after.orientation == TopAbs_REVERSED) expected.Reverse();
              Standard_Real emittedFirst = 0, emittedLast = 0;
              BRep_Tool::Range(use.edge, use.face, emittedFirst, emittedLast);
              occurrenceMatches = use.edge.IsSame(expected) &&
                  use.edge.Orientation() == expected.Orientation() &&
                  emittedFirst == post.first && emittedLast == post.last;
            } else if (post.edge == -1 && post.degenerate) {
              for (const auto &proof : boundary.connectorProofs)
                if (proof.face == static_cast<int>(i) && proof.postUse == static_cast<size_t>(postUse)) {
                  const auto *connector = InterpretationConnectorUse(e, boundary, proof);
                  occurrenceMatches = connector && connector == &use;
                  break;
                }
            }
          }
          if (!item.mapped.IsNull() && use.face.IsSame(item.mapped) && occurrenceMatches)
            distinctCoedges.insert(use.coedgeId);
        }
        for (const auto &id : distinctCoedges) coedgeIds.set(ci++, id);
        for (size_t check = 0; check < boundary.nativeChecks.size(); ++check)
          if (!item.mapped.IsNull() && boundary.nativeChecks[check].face.IsSame(item.mapped))
            nativeCheckIds.set(ni++, prefix + "check-" + std::to_string(check));
        value.set("obligationIds", obligationIds);
        value.set("coedgeIds", coedgeIds);
        value.set("nativeCheckIds", nativeCheckIds);
        value.set("joinStatus", std::string(oi > 0 && ci > 0 && ni > 0
                                                ? "complete"
                                                : "absent-scope-specific-identity"));
        faceComplete = require(oi > 0 && ci > 0 && ni > 0,
                               "effect-evidence-join-incomplete") &&
                       faceComplete;
        value.set("status",
                  std::string(faceComplete ? "checked" : "incomplete"));
        effects.set(effectCount++, value);
        faceEffects.set(effectIndex++, id);
      };
      bool representationChanged = false;
      int postUseIndex = 0;
      for (const auto &post : item.after.uses) {
        const Pcurve *prior = nullptr;
        int priorMatches = 0;
        for (const auto &candidate : item.before.uses)
          if (post.edge >= 0 && candidate.edge == post.edge &&
              candidate.orientation == post.orientation) {
            prior = &candidate;
            ++priorMatches;
          }
        if (priorMatches == 0 && post.edge == -1 && post.degenerate) {
          effect("bounded-singular-connector", postUseIndex);
          representationChanged = true;
        } else if (priorMatches == 1) {
          if (prior->curve.IsNull() && !post.curve.IsNull()) {
            effect("source-null-pcurve-construction", postUseIndex);
            representationChanged = true;
          } else if (Geometry(prior->curve) != Geometry(post.curve)) {
            effect("bounded-existing-pcurve-replacement", postUseIndex);
            representationChanged = true;
          }
          if (prior->first != post.first || prior->last != post.last) {
            effect("bounded-endpoint-range-adjustment", postUseIndex);
            representationChanged = true;
          }
        } else
          faceComplete = false;
        ++postUseIndex;
      }
      if (!representationChanged)
        effect(item.before.tolerance == item.after.tolerance
                   ? "unchanged"
                   : "tolerance-only");
      changed = changed || representationChanged;
      face.set("nativeItemId", itemId);
      face.set("faceId", faceId);
      face.set("sourceOrientation", item.before.orientation);
      face.set("finalOrientation", item.after.orientation);
      face.set("effects", faceEffects);
      face.set("status", std::string(faceComplete ? "checked" : "incomplete"));
      if (faceComplete)
        goodFaces.insert(faceId);
      require(faceComplete, "affected-face-checks-incomplete");
      faces.set(faceCount++, face);
    }
    TopTools_IndexedMapOfShape finalItems;
    if (!boundary.finalShape.IsNull())
      TopExp::MapShapes(boundary.finalShape, finalItems);
    for (int i = 1; i <= finalItems.Extent(); ++i) {
      const auto type = finalItems(i).ShapeType();
      if (Identity(boundary, finalItems(i), true) >= 0 ||
          (type != TopAbs_VERTEX && type != TopAbs_EDGE && type != TopAbs_FACE))
        continue;
      observe(Capture(const_cast<Boundary &>(boundary), finalItems(i), true),
              prefix + "new-item-" + std::to_string(i), "final-added");
    }
    size_t boundaryCheckIndex = 0;
    for (const auto &row : boundary.nativeChecks) {
      auto value = V::object();
      value.set("checkId", prefix + "check-" + std::to_string(boundaryCheckIndex));
      const auto checkFaceId = InterpretationFaceId(e, row.face);
      value.set("faceId", checkFaceId);
      value.set("method", row.method);
      auto identityFor = [&](const TopoDS_Shape &shape, std::string &kind) {
        std::set<std::string> ids;
        if (shape.IsNull()) { kind = "not-applicable"; return ids; }
        if (shape.ShapeType() == TopAbs_FACE) {
          kind = "face";
          const auto id = InterpretationFaceId(e, shape);
          if (!id.empty()) ids.insert(id);
        } else for (const auto &use : e.emittedUses) {
          if (!row.face.IsNull() && !use.face.IsSame(row.face)) continue;
          if (shape.ShapeType() == TopAbs_WIRE && use.wire.IsSame(shape)) {
            kind = "wire"; ids.insert(use.wireId);
          } else if (shape.ShapeType() == TopAbs_EDGE && use.edge.IsSame(shape)) {
            kind = "edge"; ids.insert(use.edgeId);
          }
        }
        return ids;
      };
      std::string subjectKind, firstKind, secondKind;
      const auto subjectIds = identityFor(row.subject, subjectKind);
      const auto firstIds = identityFor(row.offendingFirst, firstKind);
      const auto secondIds = identityFor(row.offendingSecond, secondKind);
      auto idsValue = [&](const std::set<std::string> &ids) {
        auto array = V::array(); size_t n = 0;
        for (const auto &id : ids) array.set(n++, id);
        return array;
      };
      value.set("subjectKind", subjectKind);
      value.set("subjectIds", idsValue(subjectIds));
      value.set("subjectIdentityStatus", std::string(
          row.subject.IsNull() ? "not-applicable" :
          subjectIds.size() == 1 ? "complete" :
          subjectIds.empty() ? "absent-scope-specific-native-identity" : "ambiguous"));
      std::set<std::string> offendingIds = firstIds;
      offendingIds.insert(secondIds.begin(), secondIds.end());
      value.set("offendingEdgeIds", idsValue(offendingIds));
      value.set("offendingEdgeIdentityStatus", std::string(
          row.offendingFirst.IsNull() && row.offendingSecond.IsNull()
              ? "not-applicable"
              : offendingIds.empty() ? "absent-scope-specific-native-identity"
                                     : "complete"));
      const bool passed = row.state == NativeCheckState::Passed;
      require(!passed || row.subject.IsNull() || subjectIds.size() == 1,
              "native-check-subject-identity-incomplete");
      require(!passed || (row.offendingFirst.IsNull() && row.offendingSecond.IsNull()) ||
                  !offendingIds.empty(),
              "native-check-offending-edge-identity-incomplete");
      value.set("status",
                std::string(passed                                  ? "passed"
                            : row.state == NativeCheckState::Failed ? "failed"
                            : row.state == NativeCheckState::Unavailable
                                ? "unavailable"
                                : "not-evaluated"));
      value.set("nativeStatus",
                row.nativeStatus >= 0 ? V(row.nativeStatus) : V::null());
      value.set("reason", row.reason);
      value.set("reusedAnalyzerResult", row.reusedAnalyzerResult);
      checkRows.set(nativeRequired++, value);
      nativePassed += passed;
      nativeFailed += row.state == NativeCheckState::Failed;
      nativeUnavailable += row.state == NativeCheckState::Unavailable;
      nativeUnassessed += row.state == NativeCheckState::NotEvaluated;
      ++boundaryCheckIndex;
    }
  }
  for (int count : emittedConsumption)
    require(count == 1, "emitted-face-inverse-coverage-incomplete");
  for (const auto &use : e.emittedUses)
    require(use.complete, "emitted-coedge-incomplete");
  for (const auto &entry : emittedCoedgeMetricConsumption)
    require(entry.second == 1, "emitted-coedge-metric-inverse-coverage-incomplete");
  require(!e.emittedUses.empty(), "missing-emitted-coedges");
  for (size_t si = 0; si < e.sourceBounds.size(); ++si) {
    const auto &source = e.sourceBounds[si];
    auto value = V::object();
    const std::string id = "source-bound-" + std::to_string(si);
    value.set("sourceBoundOccurrenceId", id);
    value.set("sourceFaceEntityNumber", source.faceEntity);
    value.set("sourceBoundEntityNumber", source.boundEntity);
    value.set("sourceLoopEntityNumber", source.loopEntity);
    value.set("isOuterBound", source.outer);
    value.set("boundSense", source.boundSense);
    value.set("sourceFaceSameSense", source.sourceFaceSense);
    value.set("effectiveFaceSameSense", source.effectiveFaceSense);
    value.set("loopKind", source.loopKind);
    value.set("insertionParentOrientation", source.insertionParentOrientation);
    value.set("preAddWireOrientation",
              source.wire.IsNull()
                  ? V::null()
                  : V(static_cast<int>(source.wire.Orientation())));
    int matches = 0;
    std::string faceId, wireId;
    std::set<std::string> boundCoedgeIds, boundEdgeIds;
    for (const auto &boundary : e.boundaries) {
      const int fi = Identity(boundary, source.face, false),
                wi = Identity(boundary, source.wire, false);
      if (fi < 0 || wi < 0)
        continue;
      const auto &face = boundary.items[fi];
      const auto &wire = boundary.items[wi];
      require(face.before.orientation ==
                  (source.sourceFaceSense ? TopAbs_FORWARD : TopAbs_REVERSED),
              "source-declared-face-sense-mismatch");
      for (const auto &loop : face.after.faceLoops)
        if (!wire.mapped.IsNull() && loop.nativeWire.IsSame(wire.mapped)) {
          ++matches;
          faceId = InterpretationFaceId(e, face.mapped);
          const auto cycle =
              CheckSourceBoundCycle(boundary, face, wire, source, loop);
          value.set("expectedPreAddWireOrientation",
                    cycle.supportedContext
                        ? V(static_cast<int>(cycle.expectedPreAdd))
                        : V::null());
          value.set("expectedNormalizedWireOrientation",
                    cycle.supportedContext
                        ? V(static_cast<int>(cycle.expectedNormalized))
                        : V::null());
          value.set("beforeNormalizedWireOrientation",
                    cycle.beforeOccurrenceUnique
                        ? V(static_cast<int>(cycle.beforeNormalized))
                        : V::null());
          value.set("finalNormalizedWireOrientation",
                    static_cast<int>(loop.nativeWire.Orientation()));
          value.set("representationOrientationChanged",
                    cycle.beforeOccurrenceUnique &&
                        cycle.beforeNormalized != loop.nativeWire.Orientation());
          value.set("composedCoedgeCyclePreserved",
                    cycle.composedCyclePreserved);
          auto sourceCycle = V::object(), declaredUses = V::array();
          sourceCycle.set("method", cycle.sourceProof.reason);
          sourceCycle.set("complete", cycle.sourceProof.complete);
          sourceCycle.set("sourceDeclarationAttempted", source.declarationAttempted);
          sourceCycle.set("sourceDeclarationComplete", source.declarationComplete);
          sourceCycle.set("semantics", std::string("source-oriented-topology-only-separate-final-native-and-metric-obligations-required"));
          for (size_t occurrence = 0; occurrence < source.declaredCoedges.size(); ++occurrence) {
            const auto &declared = source.declaredCoedges[occurrence];
            auto row = V::object();
            row.set("occurrenceIndex", occurrence);
            row.set("sourceOrientedEdgeEntityNumber", declared.orientedEntity);
            row.set("sourceEdgeEntityNumber", declared.edgeEntity);
            row.set("sourceStartVertexEntityNumber", declared.startEntity);
            row.set("sourceEndVertexEntityNumber", declared.endEntity);
            row.set("sourceOrientation", declared.forward);
            row.set("sourceEdgeSameSense", declared.edgeSameSense);
            row.set("nativeSourceEdgeItem", declared.nativeEdgeItem);
            row.set("nativeSourceStartVertexItem", declared.nativeStartItem);
            row.set("nativeSourceEndVertexItem", declared.nativeEndItem);
            declaredUses.set(occurrence, row);
          }
          sourceCycle.set("declaredOccurrences", declaredUses);
          value.set("sourceCycleProof", sourceCycle);
          if (e.diagnostics) {
            auto predicates = V::object();
            predicates.set("supportedContext", cycle.supportedContext);
            predicates.set("preAddOrientationMatches", cycle.preAddOrientationMatches);
            predicates.set("beforeOccurrenceUnique", cycle.beforeOccurrenceUnique);
            predicates.set("beforeOrientationMatches", cycle.beforeOrientationMatches);
            predicates.set("beforeComplete", cycle.beforeCompleteDiagnostic);
            predicates.set("finalComplete", cycle.finalCompleteDiagnostic);
            predicates.set("finalAssociationsComplete", cycle.finalAssociationsDiagnostic);
            predicates.set("sameCycle", cycle.sameCycleDiagnostic);
            value.set("cycleDiagnostics", predicates);
          }
          require(cycle.Positive(),
                  "source-bound-composed-orientation-changed");
          std::set<std::string> wireIds;
          for (const auto &use : e.emittedUses)
            if (use.face.IsSame(face.mapped) &&
                use.wire.IsEqual(loop.nativeWire)) {
              wireIds.insert(use.wireId);
              if (use.complete) {
                boundCoedgeIds.insert(use.coedgeId);
                boundEdgeIds.insert(use.edgeId);
              }
            }
          if (wireIds.size() == 1)
            wireId = *wireIds.begin();
        }
    }
    require(matches == 1 && !faceId.empty() && !wireId.empty(),
            "source-bound-emitted-wire-association-incomplete");
    require(!boundCoedgeIds.empty(), "source-bound-coedge-association-incomplete");
    value.set("faceId", faceId);
    value.set("wireId", wireId);
    auto coedgeIds = V::array(), edgeIds = V::array();
    size_t coedgeIndex = 0, edgeIndex = 0;
    for (const auto &coedgeId : boundCoedgeIds)
      coedgeIds.set(coedgeIndex++, coedgeId);
    for (const auto &edgeId : boundEdgeIds)
      edgeIds.set(edgeIndex++, edgeId);
    value.set("coedgeIds", coedgeIds);
    value.set("edgeIds", edgeIds);
    value.set("coedgeAssociationStatus", std::string(
        matches == 1 && !wireId.empty() && !boundCoedgeIds.empty()
            ? "complete-bidirectional-occurrence-join"
            : "absent-scope-specific-native-identity"));
    bounds.set(si, value);
  }
  MalievNativeInterpretation::RequiredLedger periodicWorld;
  std::vector<std::string> periodicFaces(e.periodic.size());
  for (size_t i = 0; i < e.periodic.size(); ++i) {
    const auto &conversion = e.periodic[i];
    auto value = V::object(), before = V::array(), after = V::array();
    value.set("operationId", std::string("periodic-") + std::to_string(i));
    value.set("method",
              std::string("native-common-parameter-rational-polynomial-v1"));
    value.set("disposition", std::string("kernel-trusted-reparameterization"));
    value.set("sourceFaceEntityNumber", conversion.faceEntity);
    value.set("sourceSurfaceEntityNumber", conversion.surfaceEntity);
    bool domain = conversion.domainsCaptured;
    for (size_t j = 0; j < 4; ++j) {
      if (conversion.domainsCaptured) {
        before.set(j, InterpretationNumber(conversion.beforeDomain[j]));
        after.set(j, InterpretationNumber(conversion.afterDomain[j]));
      }
      domain =
          domain && conversion.beforeDomain[j] == conversion.afterDomain[j];
    }
    value.set("beforeDomain", before);
    value.set("afterDomain", after);
    value.set("localUpperBound",
              InterpretationNumber(conversion.comparison.upper));
    value.set("intervalCount", conversion.comparison.intervals);
    require(domain && conversion.comparison.status == "bounded-within-budget",
            "periodic-domain-or-metric-incomplete");
    int matched = 0;
    double worldUpper = std::numeric_limits<double>::infinity();
    size_t offset = 0;
    std::string matchedFaceItemId;
    size_t periodicBoundaryIndex = 0;
    for (const auto &boundary : e.boundaries) {
      for (size_t fi = 0; fi < boundary.items.size(); ++fi) {
        const auto &face = boundary.items[fi];
        if (face.original.ShapeType() != TopAbs_FACE ||
            std::find(face.entities.begin(),face.entities.end(),conversion.faceEntity) == face.entities.end()) continue;
        ++matched;
        const auto faceId = InterpretationFaceId(e,face.mapped);
        matchedFaceItemId = "boundary-" + std::to_string(periodicBoundaryIndex) +
                            "/item-" + std::to_string(fi);
        periodicFaces[i] = faceId;
        const double scale = std::abs(face.before.geometryLocation.Transformation().ScaleFactor());
        const bool placed = std::isfinite(scale) && scale > 0 &&
            face.sourceGeometryUnchanged &&
            SameGeometry(Geometry(face.before.surface), conversion.after) &&
            face.before.geometryLocation.IsEqual(face.after.geometryLocation);
        if (placed && std::isfinite(conversion.comparison.upper))
          worldUpper = (Interval(scale)*Interval(conversion.comparison.upper)).hi;
        require(placed && goodFaces.count(faceId) == 1,"periodic-native-face-placement-or-trims-incomplete");
        value.set("faceId",faceId); value.set("placementScale",InterpretationNumber(scale));
        for (const auto obligation : boundary.postObligationIds[fi]) {
          auto &row = metrics.rows.at(offset+obligation);
          MalievNativeInterpretation::AppendBoundedPathTerm(
              row, worldUpper, e.budget,
              "periodic world-placement upper bound unavailable");
        }
      }
      offset += boundary.requiredMetrics.rows.size();
      ++periodicBoundaryIndex;
    }
    require(matched == 1,"periodic-source-face-occurrence-not-unique");
    value.set("worldUpperBoundMm",InterpretationNumber(worldUpper));
    const size_t obligation = periodicWorld.Require("periodic-reparameterization","periodic-"+std::to_string(i));
    periodicWorld.Finish(obligation,conversion.attempted,conversion.comparison.status,{worldUpper},e.budget,
                         conversion.comparison.reason);
    auto &periodicRow = periodicWorld.rows.at(obligation);
    periodicRow.pathTermKinds = {"periodic-world-displacement"};
    periodicRow.referencePath =
        "periodic-local-surface-through-native-face-placement";
    periodicRow.metricMethod =
        "native-common-parameter-rational-polynomial-v1+native-placement-scale";
    periodicRow.operationId = "periodic-" + std::to_string(i);
    periodicRow.faceId = periodicFaces[i];
    periodicRow.sourceFaceItemId = matchedFaceItemId;
    periodicRow.associationStatus =
        matched == 1 && !periodicRow.faceId.empty() &&
                !periodicRow.sourceFaceItemId.empty()
            ? "complete-periodic-operation-face-association"
            : "absent-scope-specific-native-identity";
    require(periodicRow.state != MalievNativeInterpretation::ObligationState::Bounded ||
                periodicRow.associationStatus ==
                    "complete-periodic-operation-face-association",
            "periodic-metric-operation-face-association-incomplete");
    periodic.set(i, value);
  }
  auto nativeDiagnostics = transfer["diagnostics"];
  const size_t diagnosticCount = nativeDiagnostics["length"].as<size_t>();
  size_t classified = 0;
  for (size_t i = 0; i < diagnosticCount; ++i) {
    const auto diagnostic = nativeDiagnostics[i];
    auto value = V::object(), affected = V::array();
    value.set("diagnosticOccurrenceId", diagnostic["diagnosticOccurrenceId"]);
    value.set("nativeDeliveryId", diagnostic["nativeDeliveryId"]);
    bool explained = InterpretationString(diagnostic, "severity") == "warning";
    const std::string deliveryId =
        InterpretationString(diagnostic, "nativeDeliveryId");
    int deliveryIndex = -1;
    for (size_t d = 0; d < lineage.deliveries.size(); ++d)
      if (deliveryId == "native-delivery-" + std::to_string(d))
        deliveryIndex = static_cast<int>(d);
    explained = explained && deliveryIndex >= 0;
    if (deliveryIndex >= 0) {
      const auto &delivery = lineage.deliveries[deliveryIndex];
      const int emission =
          MalievNativeWarning::EmissionIndex(delivery.emission);
      explained = explained && emission >= 0 &&
                  delivery.sourceIdentityReason.empty() &&
                  delivery.sourceEntity == diagnostic["entityNumber"].as<int>();
      if (emission >= 0) {
        const auto &origin = lineage.emissions[emission];
        value.set("operationId",
                  origin.kind + "-" + std::to_string(origin.operation));
        if (origin.kind == "intersection" &&
            origin.operation < e.operations.size()) {
          int targetMatches = 0;
          size_t affectedCount = 0;
          for (const auto &boundary : e.boundaries) {
            const int target =
                Identity(boundary, delivery.originalTarget, false);
            if (target < 0)
              continue;
            const auto &item = boundary.items[target];
            const bool sourceEntity =
                std::find(item.entities.begin(), item.entities.end(),
                          delivery.sourceEntity) != item.entities.end();
            if (!sourceEntity || item.original.ShapeType() != TopAbs_WIRE)
              continue;
            ++targetMatches;
            for (const auto &face : boundary.items)
              if (face.original.ShapeType() == TopAbs_FACE)
                for (const auto &loop : face.before.faceLoops)
                  if (loop.nativeWire.IsSame(item.original)) {
                    const auto faceId = InterpretationFaceId(e, face.mapped);
                    affected.set(affectedCount++, faceId);
                    explained = explained && goodFaces.count(faceId) == 1;
                  }
          }
          explained = explained && targetMatches == 1 && affectedCount > 0;
        } else if (origin.kind == "periodic" && origin.operation < e.periodic.size()) {
          const auto &conversion = e.periodic[origin.operation];
          const auto &faceId = periodicFaces[origin.operation];
          explained = explained && delivery.sourceEntity == conversion.surfaceEntity &&
              goodFaces.count(faceId) == 1 && periodicWorld.rows[origin.operation].state ==
                  MalievNativeInterpretation::ObligationState::Bounded;
          affected.set(0,faceId);
        } else
          explained = false;
      }
    }
    value.set("affectedFaceIds", affected);
    value.set("disposition",
              std::string(explained ? "checked-native-operation-effects"
                                    : "unclassified"));
    classified += explained;
    dispositions.set(i, value);
  }
  diagnostics.set("required", diagnosticCount);
  diagnostics.set("classified", classified);
  diagnostics.set("unclassified", diagnosticCount - classified);
  diagnostics.set("rows", dispositions);
  diagnostics.set("sourceIdentityFailures", lineage.sourceIdentityFailures);
  diagnostics.set("captureFailures",
                  lineage.appendFailures + lineage.resourceFailures);
  require(classified == diagnosticCount, "unclassified-transfer-diagnostics");
  require(lineage.sourceIdentityFailures == 0 && lineage.appendFailures == 0 &&
              lineage.resourceFailures == 0,
          "native-warning-lineage-incomplete");
  require(transfer["failureCount"].as<int>() == 0, "native-transfer-failures");
  if (e.emittedBodies.size() == 1) {
    const auto body = V::global("JSON").call<V>(
        "parse", e.emittedBodies.front().nativeEvidenceJson);
    out.set("body", body);
    require(InterpretationTrue(body, "boundedSolidEvidence") &&
                InterpretationString(body, "sourceKind") == "solid" &&
                InterpretationString(body, "membershipStatus") == "complete",
            "native-body-checks-incomplete");
    require(e.boundaries.size() == 1 && e.boundaries.front().finalShape.IsEqual(
                                            e.emittedBodies.front().shape),
            "emitted-body-transfer-shape-mismatch");
  }
  checks.set("required", nativeRequired);
  checks.set("passed", nativePassed);
  checks.set("failed", nativeFailed);
  checks.set("unavailable", nativeUnavailable);
  checks.set("notEvaluated", nativeUnassessed);
  checks.set("rows", checkRows);
  require(nativeRequired > 0 && nativeRequired == nativePassed,
          "native-context-checks-incomplete");
  tolerances.set("required", observed.required);
  tolerances.set("evaluated", observed.evaluated);
  tolerances.set("unassessed", observed.unassessed);
  tolerances.set("within", observed.within);
  tolerances.set("exceeds", observed.exceeds);
  tolerances.set("invalid", observed.invalid);
  tolerances.set("maximumMm", observed.evaluated > 0
                                  ? InterpretationNumber(observed.maximum)
                                  : V::null());
  tolerances.set("rows", toleranceRows);
  require(observed.Complete(), "tolerance-observations-incomplete");
  out.set("faces", faces);
  out.set("sourceBounds", bounds);
  out.set("effects", effects);
  out.set("connectorProofs", connectorProofs);
  require(metrics.Complete(),"composed-metric-paths-incomplete");
  require(e.periodic.empty() || periodicWorld.Complete(),"placed-periodic-obligations-incomplete");
  out.set("metrics", InterpretationLedger(metrics));
  out.set("periodicMetrics", InterpretationLedger(periodicWorld));
  out.set("periodicConversions", periodic);
  out.set("nativeChecks", checks);
  out.set("tolerances", tolerances);
  out.set("diagnostics", diagnostics);
  out.set("reasons", reasons);
  out.set("status",
          std::string(!step             ? "unsupported"
                      : invalid         ? "invalid"
                      : reasonCount > 0 ? "review-required"
                      : changed ? "native-interpreted-with-bounded-repair"
                                : "native-interpreted"));
}
inline void ExportNativeInterpretationFailure(InterpretationValue &provenance,
                                              const std::string &message) {
  auto out = provenance["nativeInterpretation"], reasons = InterpretationValue::array();
  auto metrics = InterpretationValue::object(), periodic = InterpretationValue::object();
  auto checks = InterpretationValue::object(), tolerances = InterpretationValue::object();
  auto diagnostics = InterpretationValue::object();
  size_t metricRequired = 0, checkRequired = 0, toleranceRequired = 0;
  for (const auto &boundary : EvidenceStore().boundaries) {
    metricRequired += boundary.requiredMetrics.rows.size();
    checkRequired += boundary.nativeChecks.size();
    toleranceRequired += boundary.tolerances.required;
  }
  const auto periodicRequired = EvidenceStore().periodicMetrics.rows.size();
  auto unavailableCounts = [](InterpretationValue &value, size_t required) {
    value.set("required", required);
    value.set("evaluated", 0);
    value.set("unassessed", required);
    value.set("bounded", 0);
    value.set("exceeds", 0);
    value.set("unavailable", 0);
    value.set("rows", InterpretationValue::array());
  };
  unavailableCounts(metrics, metricRequired);
  unavailableCounts(periodic, periodicRequired);
  checks.set("required", checkRequired);
  checks.set("passed", 0);
  checks.set("failed", 0);
  checks.set("unavailable", 0);
  checks.set("notEvaluated", checkRequired);
  checks.set("rows", InterpretationValue::array());
  tolerances.set("required", toleranceRequired);
  tolerances.set("evaluated", 0);
  tolerances.set("unassessed", toleranceRequired);
  tolerances.set("within", 0);
  tolerances.set("exceeds", 0);
  tolerances.set("invalid", 0);
  tolerances.set("maximumMm", InterpretationValue::null());
  tolerances.set("rows", InterpretationValue::array());
  diagnostics.set("status", std::string("unavailable-after-export-failure"));
  diagnostics.set("required", InterpretationValue::null());
  diagnostics.set("classified", InterpretationValue::null());
  diagnostics.set("unclassified", InterpretationValue::null());
  diagnostics.set("sourceIdentityFailures",
                  MalievNativeWarning::Store().sourceIdentityFailures);
  diagnostics.set("captureFailures",
                  MalievNativeWarning::Store().appendFailures +
                      MalievNativeWarning::Store().resourceFailures);
  reasons.set(0, std::string("native-interpretation-export-failed"));
  out.set("status", std::string("review-required"));
  out.set("reasons", reasons);
  out.set("exportFailure", message);
  out.set("faces", InterpretationValue::array());
  out.set("sourceBounds", InterpretationValue::array());
  out.set("effects", InterpretationValue::array());
  out.set("metrics", metrics);
  out.set("periodicMetrics", periodic);
  out.set("periodicConversions", InterpretationValue::array());
  out.set("nativeChecks", checks);
  out.set("tolerances", tolerances);
  out.set("diagnostics", diagnostics);
  out.set("manufacturingEligibility", std::string("not-assessed"));
  provenance.set("nativeInterpretation", out);
}
inline void ExportNativeInterpretation(InterpretationValue &provenance,
                                       bool completeBounds) {
  {
  MalievNativeAudit::Scope auditScope(EvidenceStore().audit, "interpretation-export");
  try {
    ExportNativeInterpretationUnchecked(provenance, completeBounds);
  } catch (const Standard_Failure &failure) {
    ExportNativeInterpretationFailure(
        provenance, failure.GetMessageString()
                        ? failure.GetMessageString()
                        : "native exception without a message");
  } catch (const std::exception &failure) {
    ExportNativeInterpretationFailure(provenance, failure.what());
  } catch (...) {
    ExportNativeInterpretationFailure(provenance, "unknown native exception");
  }
  }
  const auto &audit = EvidenceStore().audit;
  using V = InterpretationValue;
  auto out = V::object(), phases = V::object(), counters = V::object(), marks = V::array();
  out.set("schema", std::string("MalievNativeInterpretationAudit.v1"));
  out.set("semanticRole", std::string("excluded-by-MalievNativeInterpretationSemanticProjection.v1"));
  out.set("clock", std::string("steady-monotonic"));
  out.set("unit", std::string("millisecond"));
  out.set("origin", std::string("Configure"));
  out.set("cpuTimeMs", V::null());
  out.set("cpuTimeStatus", std::string("unavailable-in-current-wasm-runtime"));
  for (const auto &entry : audit.phases) {
    auto phase = V::object();
    phase.set("calls", entry.second.calls); phase.set("exits", entry.second.exits);
    phase.set("inclusiveMs", entry.second.inclusiveMs); phase.set("selfMs", entry.second.selfMs);
    phases.set(entry.first, phase);
  }
  for (const auto &entry : audit.counters) counters.set(entry.first, entry.second);
  counters.set("identityCalls", audit.identityCalls);
  counters.set("identityCandidates", audit.identityCandidates);
  counters.set("identityMs", audit.identityMs);
  counters.set("captureCalls", audit.captureCalls);
  auto exportMark = [](const MalievNativeAudit::Mark &mark) {
    auto value = V::object();
    value.set("name", mark.name); value.set("boundaryIndex", mark.boundaryIndex);
    value.set("elapsedMs", mark.elapsedMs); value.set("remainingMs", mark.remainingMs);
    return value;
  };
  for (size_t i = 0; i < audit.markOccurrences.size(); ++i) {
    auto mark = exportMark(audit.markOccurrences[i]); mark.set("sequence", i); marks.set(i, mark);
  }
  out.set("phases", phases); out.set("counters", counters); out.set("marks", marks);
  auto slowMetrics = V::array();
  for (size_t i = 0; i < audit.slowMetrics.size(); ++i) {
    const auto &metric = audit.slowMetrics[i];
    auto row = V::object();
    row.set("boundaryIndex", metric.boundaryIndex);
    row.set("obligationId", metric.obligationId);
    row.set("method", metric.method); row.set("status", metric.status);
    row.set("intervals", metric.intervals); row.set("elapsedMs", metric.elapsedMs);
    slowMetrics.set(i, row);
  }
  out.set("slowMetrics", slowMetrics);
  out.set("firstCancellation", audit.firstCancellationSite.empty() ? V::null() : exportMark(audit.firstCancellation));
  provenance["nativeInterpretation"].set("audit", out);
}
} // namespace MalievRepair
