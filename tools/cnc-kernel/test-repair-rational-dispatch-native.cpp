#include "kernel-repair-export.hpp"
#include <TColStd_Array2OfReal.hxx>
#include <TColgp_Array2OfPnt.hxx>
#include <iostream>
#include <stdexcept>
using namespace MalievRepair;
int main() {
  int checks = 0;
  auto check = [&](bool value, const char *name) {
    ++checks;
    if (!value)
      throw std::runtime_error(name);
  };
  try {
    TColgp_Array2OfPnt poles(1, 6, 1, 8);
    TColStd_Array2OfReal weights(1, 6, 1, 8);
    for (int u = 1; u <= 6; ++u)
      for (int v = 1; v <= 8; ++v) {
        poles(u, v) = gp_Pnt((u - 1) / 5., 1, 0);
        weights(u, v) = v % 2 ? 1 : .75;
      }
    TColStd_Array1OfReal uKnots(1, 2), vKnots(1, 5);
    uKnots(1) = 0;
    uKnots(2) = 1;
    for (int index = 1; index <= 5; ++index)
      vKnots(index) = index - 1;
    TColStd_Array1OfInteger uMults(1, 2), vMults(1, 5);
    uMults.Init(6);
    vMults.Init(2);
    Handle(Geom_Surface) surface = new Geom_BSplineSurface(
        poles, weights, uKnots, vKnots, uMults, vMults, 5, 2, false, true);
    TColgp_Array1OfPnt curvePoles(1, 4);
    TColgp_Array1OfPnt2d pcPoles(1, 4);
    for (int index = 1; index <= 4; ++index) {
      const double t = (index - 1) / 3.;
      curvePoles(index) = gp_Pnt(t, 1, 0);
      pcPoles(index) = gp_Pnt2d(t, -.1 + .2 * t);
    }
    const RepresentedCurveBound curve(
        Handle(Geom_Curve)(new Geom_BezierCurve(curvePoles)),
        TopLoc_Location());
    const RepresentedCurveBound pcurve(
        Handle(Geom2d_Curve)(new Geom2d_BezierCurve(pcPoles)));
    const RepresentedSurfaceBound support(surface, TopLoc_Location());
    CorrelatedDiagnostics correlated;
    ResidualDispatchDiagnostics dispatch;
    std::string method;
    const auto result = BoundRepairMetric(curve, pcurve, support, 0, 1, .01,
                                          correlated, dispatch, &method);
    check(result.status == "bounded-within-budget" &&
              dispatch.rationalAttempts == 1 && dispatch.rationalBounded == 1 &&
              dispatch.correlatedFallbacks == 0 &&
              method == "shared-rational-polynomial",
          "reviewed periodic rational class selects its shared numerator lane");
    ResidualDispatchDiagnostics limitedDispatch;
    const auto limited =
        BoundRepairMetric(curve, pcurve, support, 0, 1, .01, correlated,
                          limitedDispatch, &method, 2, 32768, 32768, 1);
    check(limited.status == "unavailable" && limited.intervals == 2 &&
              limitedDispatch.rationalIntervals == 1 &&
              limitedDispatch.correlatedFallbacks == 1,
          "failed rational prefix leaves only the true remaining interval");
    ResidualDispatchDiagnostics exhaustedDispatch;
    const auto exhausted =
        BoundRepairMetric(curve, pcurve, support, 0, 1, .01, correlated,
                          exhaustedDispatch, &method, 1);
    check(exhausted.status == "unavailable" && exhausted.intervals == 1 &&
              exhaustedDispatch.correlatedFallbacks == 0,
          "exhausted rational prefix cannot restart correlated work");
    ResidualDispatchDiagnostics zeroDispatch;
    check(BoundRepairMetric(curve, pcurve, support, 0, 1, .01, correlated,
                            zeroDispatch, &method, 0)
                      .intervals == 0 &&
              zeroDispatch.rationalAttempts == 0,
          "zero shared allowance performs no rational dispatch work");
    int ticks = 0;
    correlated.continueAssessment = [&]() { return ++ticks < 100; };
    ResidualDispatchDiagnostics cancelledDispatch;
    const auto cancelled =
        BoundRepairMetric(curve, pcurve, support, 0, 1, .01, correlated,
                          cancelledDispatch, &method);
    check(cancelled.status == "unavailable" && cancelled.intervals > 0 &&
              cancelled.reason == "assessment-monotonic-time-limit" &&
              cancelledDispatch.correlatedFallbacks == 0,
          "rational coefficient cancellation cannot start a fallback");
    correlated.continueAssessment = {};
    gp_Trsf displaced;
    displaced.SetTranslation(gp_Vec(0, 0, .02));
    const RepresentedCurveBound changed(curve.c3, TopLoc_Location(displaced));
    ResidualDispatchDiagnostics rejectedDispatch;
    const auto rejected =
        BoundRepairMetric(changed, pcurve, support, 0, 1, .01, correlated,
                          rejectedDispatch, &method);
    check(rejected.status == "exceeds-budget" &&
              rejectedDispatch.rationalRejected == 1 &&
              rejectedDispatch.correlatedFallbacks == 0,
          "proven changed source rejects without an alternate acceptance path");
    const RepresentedCurveBound linePC(Handle(Geom2d_Curve)(
        new Geom2d_Line(gp_Pnt2d(0, .25), gp_Dir2d(1, 0))));
    ResidualDispatchDiagnostics outsideClass;
    const auto outside = BoundRepairMetric(curve, linePC, support, 0, .001, .01,
                                           correlated, outsideClass, &method);
    check(outside.status == "bounded-within-budget" &&
              outsideClass.rationalAttempts == 0 &&
              outsideClass.compositionAttempts == 1,
          "analytic pcurve remains outside the initial rational cubic class");
    auto nonperiodicNative =
        Handle(Geom_BSplineSurface)::DownCast(surface->Copy());
    nonperiodicNative->SetVNotPeriodic();
    ResidualDispatchDiagnostics nonperiodicDispatch;
    const auto nonperiodic = BoundRepairMetric(
        curve, pcurve,
        RepresentedSurfaceBound(nonperiodicNative, TopLoc_Location()), 0, .001,
        .01, correlated, nonperiodicDispatch, &method);
    check(nonperiodic.status == "bounded-within-budget" &&
              nonperiodicDispatch.rationalAttempts == 0,
          "nonperiodic support does not widen the initial periodic rational "
          "class");
    auto circleSupport = Handle(Geom_BSplineSurface)::DownCast(surface->Copy());
    for (int u = 1; u <= 6; ++u)
      for (int v = 1; v <= 8; ++v)
        circleSupport->SetPole(u, v, gp_Pnt(1, 0, 0));
    const RepresentedCurveBound circle(
        Handle(Geom_Curve)(new Geom_Circle(gp_Circ(gp_Ax2(), 1))),
        TopLoc_Location());
    ResidualDispatchDiagnostics circleDispatch;
    const auto circleResult = BoundRepairMetric(
        circle, pcurve,
        RepresentedSurfaceBound(circleSupport, TopLoc_Location()), 0, .001, .01,
        correlated, circleDispatch, &method);
    check(
        circleResult.status == "bounded-within-budget" &&
            circleDispatch.rationalBounded == 1 &&
            circleDispatch.correlatedFallbacks == 0,
        "native circle source selects the reviewed rational circle-tail lane");
    Configure(.01, false);
    Reset();
    Boundary exportedBoundary;
    exportedBoundary.residualDispatch = dispatch;
    EvidenceStore().boundaries.push_back(exportedBoundary);
    auto provenance = emscripten::val::object();
    auto document = emscripten::val::object();
    document.set("sourceTransfer", emscripten::val::object());
    provenance.set("documentCoverage", document);
    Export(provenance);
    const auto exported = provenance["repairAssessment"]["boundaries"][0];
    const auto counters = exported["metricDispatch"];
    check(counters["rationalAttempts"].as<int>() == 1 &&
              counters["rationalBounded"].as<int>() == 1 &&
              counters["rationalIntervals"].as<int>() == result.intervals &&
              counters["rationalWallMilliseconds"].as<double>() >= 0 &&
              !exported.hasOwnProperty("residuals") &&
              !counters.hasOwnProperty("classes"),
          "compact native export reports rational work without geometry dumps");
    std::cout << "PASS: " << checks << " rational dispatch checks\n";
    return 0;
  } catch (const Standard_Failure &error) {
    std::cerr << error.GetMessageString() << '\n';
  } catch (const std::exception &error) {
    std::cerr << error.what() << '\n';
  }
  return 1;
}
