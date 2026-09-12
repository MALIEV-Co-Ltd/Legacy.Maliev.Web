#include "kernel-repair-export.hpp"
#include <Geom_CylindricalSurface.hxx>
#include <TColStd_Array1OfInteger.hxx>
#include <TColStd_Array1OfReal.hxx>
#include <TColgp_Array1OfPnt.hxx>
#include <TColgp_Array1OfPnt2d.hxx>
#include <iostream>
#include <stdexcept>
using namespace MalievRepair;
int main() {
  int checks = 0;
  auto check = [&](bool condition, const char *name) {
    ++checks;
    if (!condition)
      throw std::runtime_error(name);
  };
  try {
    TColgp_Array1OfPnt poles(1, 4);
    for (int i = 1; i <= 4; ++i)
      poles(i) = gp_Pnt(1, .01 * ((i - 1) / 3. - .5), (i - 1) / 3.);
    Handle(Geom_Curve) source = new Geom_BezierCurve(poles);
    TColgp_Array1OfPnt2d pcPoles(1, 2);
    pcPoles(1) = gp_Pnt2d(-.005, 0);
    pcPoles(2) = gp_Pnt2d(.005, 1);
    TColStd_Array1OfReal knots(1, 2);
    knots(1) = 0;
    knots(2) = 1;
    TColStd_Array1OfInteger multiplicities(1, 2);
    multiplicities.Init(2);
    Handle(Geom2d_Curve) pcurve =
        new Geom2d_BSplineCurve(pcPoles, knots, multiplicities, 1);
    Handle(Geom_Surface) surface = new Geom_CylindricalSurface(gp_Ax3(), 1);
    const RepresentedCurveBound c(source, TopLoc_Location()), pc(pcurve);
    const RepresentedSurfaceBound s(surface, TopLoc_Location());
    CorrelatedDiagnostics correlated;
    ResidualDispatchDiagnostics dispatch;
    std::string method;
    const auto result =
        BoundRepairMetric(c, pc, s, 0, 1, .01, correlated, dispatch, &method);
    check(result.status == "bounded-within-budget" &&
              dispatch.cylinderAttempts == 1 && dispatch.cylinderBounded == 1 &&
              dispatch.compositionAttempts == 0 &&
              dispatch.correlatedFallbacks == 0 &&
              method == "shared-cylinder-trig",
          "supported cylinder class selects reviewed shared-trig metric only");
    check(result.intervals == dispatch.cylinderIntervals &&
              dispatch.cylinderWallMilliseconds >= 0 &&
              dispatch.classes.size() == 1,
          "completed selected metric exports its own work and elapsed "
          "diagnostics");
    gp_Trsf translation;
    translation.SetTranslation(gp_Vec(.02, 0, 0));
    ResidualDispatchDiagnostics rejectedDispatch;
    const auto rejected = BoundRepairMetric(
        RepresentedCurveBound(source, TopLoc_Location(translation)), pc, s, 0,
        1, .01, correlated, rejectedDispatch, &method);
    check(rejected.status == "exceeds-budget" &&
              rejectedDispatch.cylinderRejected == 1 &&
              rejectedDispatch.correlatedFallbacks == 0 &&
              method == "shared-cylinder-trig",
          "proven changed-cylinder result does not restart a fallback");
    ResidualDispatchDiagnostics zeroDispatch;
    const auto zero = BoundRepairMetric(c, pc, s, 0, 1, .01, correlated,
                                        zeroDispatch, &method, 0);
    check(zero.status == "unavailable" && zero.intervals == 0 &&
              zeroDispatch.cylinderAttempts == 0 &&
              method == "not-assessed-interval-limit",
          "zero global allowance does not dispatch cylinder or fallback");
    const double kappa = 4 * (std::sqrt(2.) - 1) / 3;
    poles(1) = gp_Pnt(1, 0, 0);
    poles(2) = gp_Pnt(1, kappa, 0);
    poles(3) = gp_Pnt(kappa, 1, 0);
    poles(4) = gp_Pnt(0, 1, 0);
    Handle(Geom_Curve) quarter = new Geom_BezierCurve(poles);
    pcPoles(1) = gp_Pnt2d(0, 0);
    pcPoles(2) = gp_Pnt2d(1.5707963267948966, 0);
    Handle(Geom2d_Curve) quarterPC =
        new Geom2d_BSplineCurve(pcPoles, knots, multiplicities, 1);
    ResidualDispatchDiagnostics limitedDispatch;
    const auto limited =
        BoundRepairMetric(RepresentedCurveBound(quarter, TopLoc_Location()),
                          RepresentedCurveBound(quarterPC), s, 0, 1, .01,
                          correlated, limitedDispatch, &method, 2, 32768, 1);
    check(limited.status == "unavailable" && limited.intervals == 2 &&
              limitedDispatch.cylinderIntervals == 1 &&
              limitedDispatch.correlatedFallbacks == 1 &&
              limitedDispatch.compositionAttempts == 0 &&
              method == "shared-cylinder-trig-then-correlated",
          "worked cylindrical prefix leaves only one correlated interval");
    ResidualDispatchDiagnostics exhaustedDispatch;
    const auto exhausted =
        BoundRepairMetric(RepresentedCurveBound(quarter, TopLoc_Location()),
                          RepresentedCurveBound(quarterPC), s, 0, 1, .01,
                          correlated, exhaustedDispatch, &method, 1);
    check(exhausted.status == "unavailable" && exhausted.intervals == 1 &&
              exhaustedDispatch.correlatedFallbacks == 0,
          "exhausted cylinder prefix never regains global allowance");
    int ticks = 0;
    correlated.continueAssessment = [&]() { return ++ticks < 30; };
    ResidualDispatchDiagnostics cancelledDispatch;
    const auto cancelled = BoundRepairMetric(c, pc, s, 0, 1, .01, correlated,
                                             cancelledDispatch, &method);
    check(cancelled.status == "unavailable" && cancelled.intervals > 0 &&
              cancelled.reason == "assessment-monotonic-time-limit" &&
              cancelledDispatch.correlatedFallbacks == 0,
          "cancelled shared trig prefix does not start correlated work");
    correlated.continueAssessment = {};
    TColgp_Array1OfPnt2d cubicPCPoles(1, 4);
    for (int index = 1; index <= 4; ++index) {
      const double t = (index - 1) / 3.;
      cubicPCPoles(index) = gp_Pnt2d(.01 * (t - .5), t);
    }
    Handle(Geom2d_Curve) cubicPC = new Geom2d_BezierCurve(cubicPCPoles);
    ResidualDispatchDiagnostics otherClass;
    const auto other =
        BoundRepairMetric(c, RepresentedCurveBound(cubicPC), s, 0, 1, .01,
                          correlated, otherClass, &method);
    check(other.status == "bounded-within-budget" &&
              otherClass.cylinderAttempts == 0 &&
              otherClass.compositionAttempts == 1 &&
              otherClass.correlatedFallbacks == 1 && method == "correlated",
          "cubic pcurve is not silently widened into affine cylinder class");
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
    check(counters["cylinderAttempts"].as<int>() == 1 &&
              counters["cylinderBounded"].as<int>() == 1 &&
              counters["cylinderIntervals"].as<int>() == result.intervals &&
              counters["cylinderWallMilliseconds"].as<double>() >= 0 &&
              !counters.hasOwnProperty("classes") &&
              !exported.hasOwnProperty("items") &&
              !exported.hasOwnProperty("residuals"),
          "compact export carries cylinder counters without source snapshots");
    std::cout << "PASS: " << checks << " cylinder dispatch checks\n";
    return 0;
  } catch (const Standard_Failure &error) {
    std::cerr << error.GetMessageString() << '\n';
  } catch (const std::exception &error) {
    std::cerr << error.what() << '\n';
  }
  return 1;
}
