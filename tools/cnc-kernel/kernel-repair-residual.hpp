#pragma once
#include "kernel-repair-high-axis-composition.hpp"
#include <map>

namespace MalievRepair {
struct ResidualDispatchDiagnostics {
  int compositionAttempts = 0, compositionBounded = 0, compositionRejected = 0;
  int compositionIntervals = 0, correlatedFallbacks = 0;
  double compositionWallMilliseconds = 0;
  int cylinderAttempts = 0, cylinderBounded = 0, cylinderRejected = 0;
  int cylinderIntervals = 0;
  double cylinderWallMilliseconds = 0;
  int rationalAttempts = 0, rationalBounded = 0, rationalRejected = 0;
  int rationalIntervals = 0;
  double rationalWallMilliseconds = 0;
  int highAxisAttempts = 0, highAxisBounded = 0, highAxisRejected = 0;
  int highAxisIntervals = 0;
  double highAxisWallMilliseconds = 0;
  std::map<std::string, int> classes, fallbackReasons;
};
inline Residual
BoundRepairMetric(const RepresentedCurveBound &curve,
                  const RepresentedCurveBound &pcurve,
                  const RepresentedSurfaceBound &surface, double first,
                  double last, double budget, CorrelatedDiagnostics &correlated,
                  ResidualDispatchDiagnostics &dispatch,
                  std::string *method = nullptr, int intervalLimit = 32768,
                  int compositionLimit = 32768, int cylinderLimit = 32768,
                  int rationalLimit = 32768, int highAxisLimit = 32768) {
  Residual unavailable;
  const int limit = std::min(32768, intervalLimit);
  if (limit <= 0) {
    if (method)
      *method = "not-assessed-interval-limit";
    unavailable.reason = "metric-interval-resource-limit";
    return unavailable;
  }
  const auto className =
      std::string(curve.isSpline ? "spline" : "analytic") + ":" +
      std::to_string(
          curve.isSpline
              ? curve.spline.degree
              : static_cast<int>(GeomAdaptor_Curve(curve.c3).GetType())) +
      "/" + (pcurve.isSpline ? "spline" : "analytic") + ":" +
      std::to_string(
          pcurve.isSpline
              ? pcurve.spline.degree
              : static_cast<int>(Geom2dAdaptor_Curve(pcurve.c2).GetType())) +
      "/" + (surface.spline ? "spline" : "analytic") + ":" +
      std::to_string(
          surface.spline
              ? surface.u.degree
              : static_cast<int>(
                    GeomAdaptor_Surface(surface.surface).GetType())) +
      "," + std::to_string(surface.spline ? surface.v.degree : 0);
  ++dispatch.classes[className];
  const bool cylinderClass =
      curve.isSpline && curve.spline.degree == 3 && pcurve.isSpline &&
      pcurve.spline.degree == 1 && !surface.spline &&
      GeomAdaptor_Surface(surface.surface).GetType() == GeomAbs_Cylinder;
  const bool rationalClass =
      surface.spline && surface.u.degree == 5 && surface.v.degree == 2 &&
      pcurve.isSpline && pcurve.spline.degree == 3 &&
      ((curve.isSpline && curve.spline.degree == 3) ||
       (!curve.isSpline &&
        GeomAdaptor_Curve(curve.c3).GetType() == GeomAbs_Circle)) &&
      !GeomAdaptor_Surface(surface.surface).BSpline()->IsUPeriodic() &&
      GeomAdaptor_Surface(surface.surface).BSpline()->IsVPeriodic();
  const bool highAxisAnalyticSource =
      !curve.isSpline &&
      (GeomAdaptor_Curve(curve.c3).GetType() == GeomAbs_Circle ||
       GeomAdaptor_Curve(curve.c3).GetType() == GeomAbs_Ellipse);
  const bool highAxisModel =
      pcurve.isSpline && pcurve.spline.degree == 3 &&
      (highAxisAnalyticSource || (curve.isSpline && curve.spline.degree == 3));
  const bool highAxisExact =
      curve.isSpline &&
      (curve.spline.degree == 6 || curve.spline.degree == 8) &&
      !pcurve.isSpline &&
      Geom2dAdaptor_Curve(pcurve.c2).GetType() == GeomAbs_Line;
  const bool highAxisClass =
      surface.spline && surface.u.degree == 2 &&
      (surface.v.degree == 6 || surface.v.degree == 8) &&
      !GeomAdaptor_Surface(surface.surface).BSpline()->IsUPeriodic() &&
      !GeomAdaptor_Surface(surface.surface).BSpline()->IsVPeriodic() &&
      (highAxisModel || highAxisExact);
  if (highAxisClass)
    ++dispatch.highAxisAttempts;
  else if (rationalClass)
    ++dispatch.rationalAttempts;
  else if (cylinderClass)
    ++dispatch.cylinderAttempts;
  else
    ++dispatch.compositionAttempts;
  const auto start = std::chrono::steady_clock::now();
  auto result =
      highAxisClass
          ? BoundHighAxisRationalComposition(curve, pcurve, surface, first,
                                             last, budget,
                                             std::min(limit, highAxisLimit), 32,
                                             correlated.continueAssessment)
      : rationalClass
          ? BoundRationalComposition(curve, pcurve, surface, first, last,
                                     budget, std::min(limit, rationalLimit), 32,
                                     correlated.continueAssessment)
      : cylinderClass
          ? BoundCylindricalComposition(curve, pcurve, surface, first, last,
                                        budget, std::min(limit, cylinderLimit),
                                        32, correlated.continueAssessment)
          : BoundPolynomialComposition(curve, pcurve, surface, first, last,
                                       budget,
                                       std::min(limit, compositionLimit), 32,
                                       correlated.continueAssessment);
  const double elapsed = std::chrono::duration<double, std::milli>(
                             std::chrono::steady_clock::now() - start)
                             .count();
  if (highAxisClass) {
    dispatch.highAxisWallMilliseconds += elapsed;
    dispatch.highAxisIntervals += result.intervals;
  } else if (rationalClass) {
    dispatch.rationalWallMilliseconds += elapsed;
    dispatch.rationalIntervals += result.intervals;
  } else if (cylinderClass) {
    dispatch.cylinderWallMilliseconds += elapsed;
    dispatch.cylinderIntervals += result.intervals;
  } else {
    dispatch.compositionWallMilliseconds += elapsed;
    dispatch.compositionIntervals += result.intervals;
  }
  const std::string fastMethod = highAxisClass   ? "shared-high-axis-rational"
                                 : rationalClass ? "shared-rational-polynomial"
                                 : cylinderClass ? "shared-cylinder-trig"
                                                 : "shared-polynomial";
  if (method)
    *method = fastMethod;
  if (result.status == "bounded-within-budget") {
    if (highAxisClass)
      ++dispatch.highAxisBounded;
    else if (rationalClass)
      ++dispatch.rationalBounded;
    else if (cylinderClass)
      ++dispatch.cylinderBounded;
    else
      ++dispatch.compositionBounded;
    return result;
  }
  if (result.status == "exceeds-budget") {
    if (highAxisClass)
      ++dispatch.highAxisRejected;
    else if (rationalClass)
      ++dispatch.rationalRejected;
    else if (cylinderClass)
      ++dispatch.cylinderRejected;
    else
      ++dispatch.compositionRejected;
    return result;
  }
  ++dispatch.fallbackReasons[result.reason];
  const int remaining = limit - result.intervals;
  if (remaining <= 0 || result.reason == "assessment-monotonic-time-limit")
    return result;
  ++dispatch.correlatedFallbacks;
  if (method)
    *method = result.intervals ? fastMethod + "-then-correlated" : "correlated";
  auto fallback =
      BoundCorrelatedResidual(curve, pcurve, surface, first, last, budget,
                              nullptr, &correlated, remaining);
  fallback.intervals += result.intervals;
  fallback.subdivisions += result.subdivisions;
  return fallback;
}
} // namespace MalievRepair
