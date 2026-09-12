#pragma once
#include "kernel-repair.hpp"
#include "kernel-native-interpretation-export.hpp"
#include <emscripten/val.h>
namespace MalievRepair {
inline void ConfigureOptions(const emscripten::val &options) {
  double budget = 0;
  bool diagnostics = false;
  int assessmentMilliseconds = 60000;
  if (!options.isNull() && !options.isUndefined()) {
    if (options.hasOwnProperty("repairErrorBudgetMm") &&
        options["repairErrorBudgetMm"].typeOf().as<std::string>() == "number")
      budget = options["repairErrorBudgetMm"].as<double>();
    if (options.hasOwnProperty("repairDiagnostics") &&
        options["repairDiagnostics"].typeOf().as<std::string>() == "boolean")
      diagnostics = options["repairDiagnostics"].as<bool>();
    if (options.hasOwnProperty("repairAssessmentTimeLimitMs") &&
        options["repairAssessmentTimeLimitMs"].typeOf().as<std::string>() ==
            "number") {
      const double requested = options["repairAssessmentTimeLimitMs"].as<double>();
      if (std::isfinite(requested))
        assessmentMilliseconds = static_cast<int>(
            std::max(0.0, std::min(60000.0, requested)));
    }
    if (options.hasOwnProperty("linearUnit") &&
        options["linearUnit"].as<std::string>() != "millimeter")
      budget = 0;
  }
  if (!std::isfinite(budget) || budget <= 0)
    budget = 0;
  Configure(budget, diagnostics, assessmentMilliseconds);
}
inline emscripten::val ExportResidual(const Residual &r) {
  using emscripten::val;
  auto v = val::object();
  v.set("status", r.status);
  v.set("reason", r.reason);
  v.set("lowerBoundMm", std::isfinite(r.lower) ? val(r.lower) : val::null());
  v.set("upperBoundMm",
        (r.status == "bounded-within-budget" || r.status == "exceeds-budget" ||
         r.status == "unresolved-upper-bound-exceeds-budget") &&
                std::isfinite(r.upper)
            ? val(r.upper)
            : val::null());
  v.set("intervalCount", r.intervals);
  v.set("subdivisionCount", r.subdivisions);
  return v;
}
inline emscripten::val ExportState(const State &s) {
  auto v = emscripten::val::object();
  v.set("geometry", s.geometry);
  v.set("placement", s.placement);
  v.set("membership", s.membership);
  v.set("pcurves", s.pcurves);
  v.set("tolerance", s.tolerance);
  v.set("rangeFirst", s.first);
  v.set("rangeLast", s.last);
  v.set("flags", s.flags);
  return v;
}
inline bool CompleteSourceBoundCoverage(const Evidence &e) {
  if (e.sourceBounds.empty() || e.boundaries.empty())
    return false;
  std::vector<int> declarationMatches(e.sourceBounds.size(), 0);
  bool complete = true;
  for (const auto &boundary : e.boundaries) {
    complete = complete && boundary.finalCorrespondenceComplete;
    for (const auto &face : boundary.items) {
      if (face.original.ShapeType() != TopAbs_FACE)
        continue;
      complete = complete && !face.before.faceLoops.empty();
      std::vector<int> postConsumption(face.after.faceLoops.size(), 0);
      for (const auto &loop : face.before.faceLoops) {
        int matches = 0;
        for (size_t index = 0; index < e.sourceBounds.size(); ++index) {
          const auto &declaration = e.sourceBounds[index];
          if (!declaration.face.IsNull() && !declaration.wire.IsNull() &&
              declaration.face.IsSame(face.original) &&
              declaration.wire.IsEqual(loop.nativeWire)) {
            ++matches;
            ++declarationMatches[index];
            const auto expectedOrientation =
                declaration.boundSense == declaration.effectiveFaceSense
                    ? TopAbs_FORWARD : TopAbs_REVERSED;
            complete = complete && declaration.faceEntity > 0 &&
                declaration.boundEntity > 0 && declaration.loopEntity > 0 &&
                declaration.wire.Orientation() == expectedOrientation;
            const int wire = Identity(boundary, declaration.wire, false);
            int postMatches = 0;
            if (wire >= 0 && !boundary.items[wire].mapped.IsNull())
              for (size_t postIndex = 0; postIndex < face.after.faceLoops.size(); ++postIndex)
                if (face.after.faceLoops[postIndex].nativeWire.IsSame(boundary.items[wire].mapped)) {
                  ++postMatches;
                  ++postConsumption[postIndex];
                }
            complete = complete && postMatches == 1;
          }
        }
        complete = complete && matches == 1;
      }
      complete = complete && face.before.faceLoops.size() == face.after.faceLoops.size();
      for (const int consumed : postConsumption)
        complete = complete && consumed == 1;
    }
  }
  for (const int matches : declarationMatches)
    complete = complete && matches == 1;
  return complete;
}
inline void Export(emscripten::val &provenance) {
  using emscripten::val;
  auto &e = EvidenceStore();
  auto out = val::object(), boundaries = val::array(), periodic = val::array();
  out.set("schema", std::string("MalievKernelRepair.v1"));
  out.set("status",
          std::string(e.active ? "assessment-incomplete" : "not-requested"));
  out.set(
      "evidenceScope",
      std::string("same-import-before-process-shape-and-before-binder-merge"));
  out.set("boundMethod", std::string("outward-interval-native-rational-de-Boor-"
                                     "analytic-Taylor-envelopes"));
  out.set("errorBudgetMm", e.budget > 0 ? val(e.budget) : val::null());
  out.set(
      "budgetSource",
      std::string(
          "explicit-caller-policy-not-native-tolerance-or-customer-accuracy"));
  out.set("remainingProof", std::string("oriented-trim-material-side-and-new-"
                                        "degenerate-coedge-certification"));
  out.set("eligible", false);
  out.set(
      "resourcePolicy",
      std::string("max(65536,512*coedgeUses),ceiling1000000;comparison32768/"
                  "depth32;monotonic60s-from-import-options"));
  auto sourceBounds = val::array();
  bool sourceBoundCoverage = CompleteSourceBoundCoverage(e);
  for (size_t i = 0; i < e.sourceBounds.size(); ++i) {
    const auto &s = e.sourceBounds[i];
    auto value = val::object();
    value.set("sourceFaceEntityNumber", s.faceEntity);
    value.set("sourceBoundEntityNumber", s.boundEntity);
    value.set("sourceLoopEntityNumber", s.loopEntity);
    value.set("isOuterBound", s.outer);
    value.set("boundSense", s.boundSense);
    value.set("sourceFaceSameSense", s.sourceFaceSense);
    value.set("effectiveFaceSameSense", s.effectiveFaceSense);
    value.set("mapped", !s.wire.IsNull());
    int face = -1, wire = -1, boundary = -1;
    for (size_t j = 0; j < e.boundaries.size(); ++j) {
      const int candidate = Identity(e.boundaries[j], s.face, false);
      if (candidate >= 0) {
        if (boundary >= 0) {
          boundary = -2;
          face = wire = -1;
          break;
        }
        boundary = static_cast<int>(j);
        face = candidate;
        wire = Identity(e.boundaries[j], s.wire, false);
      }
    }
    value.set("boundary", boundary);
    value.set("nativeFaceId", face);
    value.set("nativeWireId", wire);
    sourceBoundCoverage =
        sourceBoundCoverage && boundary >= 0 && face >= 0 && wire >= 0;
    sourceBounds.set(i, value);
  }
  out.set("sourceBoundOccurrenceCount", e.sourceBounds.size());
  if (e.diagnostics)
    out.set("sourceBoundOccurrences", sourceBounds);
  for (size_t i = 0; i < e.boundaries.size(); ++i) {
    const auto &b = e.boundaries[i];
    auto boundary = val::object(), items = val::array();
    boundary.set("sourceEntityNumber", b.sourceEntity);
    boundary.set("finalNativeValid", b.finalValid);
    boundary.set("finalCorrespondenceComplete", b.finalCorrespondenceComplete);
    boundary.set("nativeItemCount", b.items.size());
    boundary.set("changedSourceGeometryCount", b.changedSourceGeometry);
    boundary.set("changedRangeCount", b.changedRanges);
    boundary.set("generatedPcurveCount", b.generatedPcurves);
    boundary.set("changedExistingPcurveCount", b.changedPcurves);
    boundary.set("unmappedPostCoedgeCount", b.unmappedUses);
    boundary.set("changedOrientedMembershipCount", b.changedMembership);
    boundary.set("verifiedNewDegenerateUseCount", b.verifiedDegenerateUses);
    int boundedConeRegions = 0;
    auto coneRegions = val::array();
    for (size_t regionIndex = 0; regionIndex < b.coneRegions.size(); ++regionIndex) {
      const auto &entry = b.coneRegions[regionIndex];
      const auto &region = entry.second;
      boundedConeRegions += region.status == "bounded-source-cone-cap";
      if (e.diagnostics) {
        auto value = val::object();
        value.set("nativeFaceId", entry.first);
        value.set("status", region.status);
        value.set("reason", region.reason);
        value.set("collarUpperBoundMm", std::isfinite(region.collarUpperMm) ? val(region.collarUpperMm) : val::null());
        value.set("sourceCircleEdgeId", region.circleEdge);
        value.set("sourceSeamEdgeId", region.seamEdge);
        value.set("sourceApexVertexId", region.apexVertex);
        coneRegions.set(regionIndex, value);
      }
    }
    boundary.set("boundedSourceConeRegionCount", boundedConeRegions);
    if (e.diagnostics)
      boundary.set("sourceConeRegions", coneRegions);
    boundary.set("totalIntervalCount", b.totalIntervals);
    boundary.set("aggregateIntervalLimit", b.maximumIntervals);
    boundary.set("maximumNativeToleranceMm", std::isfinite(b.maximumTolerance)
                                                 ? val(b.maximumTolerance)
                                                 : val::null());
    boundary.set("rangeSliverUpperBoundMm", std::isfinite(b.rangeDeviationUpper)
                                                ? val(b.rangeDeviationUpper)
                                                : val::null());
    auto correlated = val::object(), fallbacks = val::object();
    for (const auto &entry : b.correlatedDiagnostics.fallbackReasons)
      fallbacks.set(entry.first, entry.second);
    correlated.set("attemptCount", b.correlatedDiagnostics.centeredAttempts);
    correlated.set("boundedCount", b.correlatedDiagnostics.centeredSuccesses);
    correlated.set("fallbackReasons", fallbacks);
    correlated.set("wallMilliseconds",
                   b.correlatedDiagnostics.elapsedWallMilliseconds);
    correlated.set("processCpuMilliseconds",
                   b.correlatedDiagnostics.processCpuAvailable
                       ? val(b.correlatedDiagnostics.elapsedCpuMilliseconds)
                       : val::null());
    boundary.set("correlatedDiagnostics", correlated);
    auto metricDispatch = val::object(), dispatchClasses = val::object(), dispatchReasons = val::object();
    metricDispatch.set("compositionAttempts", b.residualDispatch.compositionAttempts);
    metricDispatch.set("compositionBounded", b.residualDispatch.compositionBounded);
    metricDispatch.set("compositionRejected", b.residualDispatch.compositionRejected);
    metricDispatch.set("compositionIntervals", b.residualDispatch.compositionIntervals);
    metricDispatch.set("compositionWallMilliseconds", b.residualDispatch.compositionWallMilliseconds);
    metricDispatch.set("cylinderAttempts", b.residualDispatch.cylinderAttempts);
    metricDispatch.set("cylinderBounded", b.residualDispatch.cylinderBounded);
    metricDispatch.set("cylinderRejected", b.residualDispatch.cylinderRejected);
    metricDispatch.set("cylinderIntervals", b.residualDispatch.cylinderIntervals);
    metricDispatch.set("cylinderWallMilliseconds", b.residualDispatch.cylinderWallMilliseconds);
    metricDispatch.set("rationalAttempts", b.residualDispatch.rationalAttempts);
    metricDispatch.set("rationalBounded", b.residualDispatch.rationalBounded);
    metricDispatch.set("rationalRejected", b.residualDispatch.rationalRejected);
    metricDispatch.set("rationalIntervals", b.residualDispatch.rationalIntervals);
    metricDispatch.set("rationalWallMilliseconds", b.residualDispatch.rationalWallMilliseconds);
    metricDispatch.set("highAxisAttempts", b.residualDispatch.highAxisAttempts);
    metricDispatch.set("highAxisBounded", b.residualDispatch.highAxisBounded);
    metricDispatch.set("highAxisRejected", b.residualDispatch.highAxisRejected);
    metricDispatch.set("highAxisIntervals", b.residualDispatch.highAxisIntervals);
    metricDispatch.set("highAxisWallMilliseconds", b.residualDispatch.highAxisWallMilliseconds);
    metricDispatch.set("correlatedFallbacks", b.residualDispatch.correlatedFallbacks);
    if (e.diagnostics) {
      for (const auto &entry : b.residualDispatch.classes) dispatchClasses.set(entry.first, entry.second);
      for (const auto &entry : b.residualDispatch.fallbackReasons) dispatchReasons.set(entry.first, entry.second);
      metricDispatch.set("classes", dispatchClasses);
      metricDispatch.set("fallbackReasons", dispatchReasons);
    }
    boundary.set("metricDispatch", metricDispatch);
    auto residuals = val::array();
    int bounded = 0, exceeded = 0, unavailable = 0;
    for (size_t j = 0; j < b.residuals.size(); ++j) {
      const auto &r = b.residuals[j];
      bounded += r.status == "bounded-within-budget";
      exceeded += r.status == "exceeds-budget";
      unavailable += r.status == "unavailable";
      if (e.diagnostics) {
        auto value = ExportResidual(r);
        const auto &ref = b.residualReferences.at(j);
        value.set("nativeFaceId", ref.face);
        value.set("nativeEdgeId", ref.edge);
        value.set("coedgeOrientation", ref.orientation);
        value.set("first", ref.first);
        value.set("last", ref.last);
        value.set("referencePath", ref.path);
        value.set("metricMethod", ref.metricMethod);
        value.set("sliverUpperBoundMm", std::isfinite(ref.sliverUpper)
                                            ? val(ref.sliverUpper)
                                            : val::null());
        auto entities = val::array();
        if (ref.edge >= 0)
          for (size_t k = 0; k < b.items[ref.edge].entities.size(); ++k)
            entities.set(k, b.items[ref.edge].entities[k]);
        value.set("sourceEdgeEntityNumbers", entities);
        residuals.set(j, value);
      }
    }
    boundary.set("boundedResidualCount", bounded);
    boundary.set("exceededResidualCount", exceeded);
    boundary.set("unavailableResidualCount", unavailable);
    boundary.set("totalResidualCount", b.residuals.size());
    boundary.set("incompleteResidualCount", b.residuals.size() - bounded - exceeded);
    if (e.diagnostics)
      boundary.set("residuals", residuals);
    if (e.diagnostics)
      for (size_t j = 0; j < b.items.size(); ++j) {
        const auto &item = b.items[j];
        auto v = val::object(), entities = val::array(), changes = val::array();
        int n = 0;
        v.set("nativeId", static_cast<int>(j));
        v.set("kind", static_cast<int>(item.original.ShapeType()));
        v.set("association", item.association);
        for (size_t k = 0; k < item.entities.size(); ++k)
          entities.set(k, item.entities[k]);
        v.set("sourceEntityNumbers", entities);
        if (item.before.geometry != item.after.geometry)
          changes.set(n++, std::string("geometry"));
        if (item.before.placement != item.after.placement)
          changes.set(n++, std::string("placement"));
        if (item.before.membership != item.after.membership)
          changes.set(n++, std::string("oriented-membership"));
        if (item.before.pcurves != item.after.pcurves)
          changes.set(n++, std::string("pcurves-or-ranges"));
        if (item.before.tolerance != item.after.tolerance)
          changes.set(n++, std::string("tolerance"));
        if (item.before.first != item.after.first ||
            item.before.last != item.after.last)
          changes.set(n++, std::string("native-range"));
        v.set("before", ExportState(item.before));
        v.set("after", ExportState(item.after));
        v.set("changes", changes);
        items.set(j, v);
      }
    if (e.diagnostics)
      boundary.set("items", items);
    boundaries.set(i, boundary);
  }
  for (size_t i = 0; i < e.periodic.size(); ++i) {
    const auto &p = e.periodic[i];
    auto v = val::object();
    v.set("sourceSurfaceEntityNumber", p.surfaceEntity);
    v.set("sourceFaceEntityNumber", p.faceEntity);
    if (e.diagnostics) {
      v.set("before", p.before);
      v.set("after", p.after);
    }
    v.set("equivalence", ExportResidual(p.comparison));
    periodic.set(i, v);
  }
  auto operations = val::array();
  for (size_t i = 0; e.diagnostics && i < e.operations.size(); ++i) {
    const auto &o = e.operations[i];
    auto v = val::object(), entities = val::array();
    v.set("emitter", std::string("ShapeFix_Wire.FixIntersectingEdges"));
    v.set("branch", o.branch);
    v.set("boundary", o.boundary);
    v.set("nativeWireId", o.wire);
    v.set("nativeStatus", o.status);
    v.set("message", o.message);
    v.set("originalMessage", o.originalMessage);
    for (size_t j = 0; j < o.entities.size(); ++j)
      entities.set(j, o.entities[j]);
    v.set("sourceEntityNumbers", entities);
    operations.set(i, v);
  }
  out.set("nativeRepairOperationCount", e.operations.size());
  if (e.diagnostics)
    out.set("nativeRepairOperations", operations);
  bool exactEligible = e.active && e.budget > 0 && sourceBoundCoverage &&
                       !e.boundaries.empty() && e.periodic.empty();
  for (const auto &boundary : e.boundaries) {
    exactEligible =
        exactEligible && boundary.finalValid && boundary.finalCorrespondenceComplete &&
        boundary.changedSourceGeometry == 0 && boundary.changedRanges == 0 &&
        boundary.generatedPcurves == 0 && boundary.changedPcurves == 0 &&
        boundary.changedMembership == 0 && boundary.unmappedUses == 0 &&
        boundary.maximumTolerance <= e.budget;
    for (const auto &residual : boundary.residuals)
      exactEligible =
          exactEligible && residual.status == "bounded-within-budget";
  }
  const auto transfer = provenance["documentCoverage"]["sourceTransfer"];
  exactEligible = exactEligible && transfer["warningCount"].as<int>() == 0 &&
                  transfer["failureCount"].as<int>() == 0 &&
                  provenance["sourceCoverage"]["status"].as<std::string>() ==
                      "complete_supported_single_solid";
  out.set("eligible", exactEligible);
  if (exactEligible) {
    out.set("status", std::string("verified-unchanged-or-tolerance-only"));
    out.set("remainingProof", val::null());
  }
  out.set(
      "eligibilityScope",
      std::string("native-repair-only-not-manufacturing-or-customer-accuracy"));
  out.set("boundaries", boundaries);
  out.set("periodicConversions", periodic);
  provenance.set("repairAssessment", out);
  ExportNativeInterpretation(provenance, sourceBoundCoverage);
  // Never leak an earlier STEP transfer's evidence into a later IGES/BREP
  // import.
  e = Evidence();
  MalievNativeWarning::Reset(false);
}
} // namespace MalievRepair
