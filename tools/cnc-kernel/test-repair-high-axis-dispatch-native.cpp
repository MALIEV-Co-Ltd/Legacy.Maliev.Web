#include "kernel-repair-export.hpp"
#include <Geom2d_Line.hxx>
#include <Geom_Ellipse.hxx>
#include <TColStd_Array2OfReal.hxx>
#include <TColgp_Array2OfPnt.hxx>
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
    TColgp_Array2OfPnt poles(1, 3, 1, 7);
    TColStd_Array2OfReal weights(1, 3, 1, 7);
    for (int u = 1; u <= 3; ++u)
      for (int v = 1; v <= 7; ++v) {
        poles(u, v) = gp_Pnt((u - 1) / 2., 0, 0);
        weights(u, v) = v % 2 ? 1 : .75;
      }
    TColStd_Array1OfReal knots(1, 2);
    knots(1) = 0;
    knots(2) = 1;
    TColStd_Array1OfInteger uMults(1, 2), vMults(1, 2);
    uMults.Init(3);
    vMults.Init(7);
    Handle(Geom_BSplineSurface) native = new Geom_BSplineSurface(
        poles, weights, knots, knots, uMults, vMults, 2, 6);
    const RepresentedSurfaceBound surface(native, TopLoc_Location());
    TColgp_Array1OfPnt sourcePoles(1, 7);
    for (int index = 1; index <= 7; ++index)
      sourcePoles(index) = gp_Pnt((index - 1) / 6., 0, 0);
    const RepresentedCurveBound source(
        Handle(Geom_Curve)(new Geom_BezierCurve(sourcePoles)),
        TopLoc_Location());
    const RepresentedCurveBound pcurve(
        Handle(Geom2d_Curve)(new Geom2d_Line(gp_Pnt2d(0, 0), gp_Dir2d(1, 0))));
    CorrelatedDiagnostics correlated;
    ResidualDispatchDiagnostics dispatch;
    std::string method;
    const auto result = BoundRepairMetric(source, pcurve, surface, 0, 1, .01,
                                          correlated, dispatch, &method);
    check(result.status == "bounded-within-budget" &&
              dispatch.highAxisAttempts == 1 && dispatch.highAxisBounded == 1 &&
              dispatch.correlatedFallbacks == 0 &&
              method == "shared-high-axis-rational",
          "exact high-degree source selects reviewed high-axis class");
    TColgp_Array1OfPnt cubicPoles(1, 4);
    TColgp_Array1OfPnt2d uvPoles(1, 4);
    for (int index = 1; index <= 4; ++index) {
      const double t = (index - 1) / 3.;
      cubicPoles(index) = gp_Pnt(t, 0, 0);
      uvPoles(index) = gp_Pnt2d(t, .2 * t * t);
    }
    const RepresentedCurveBound cubic(
        Handle(Geom_Curve)(new Geom_BezierCurve(cubicPoles)),
        TopLoc_Location());
    const RepresentedCurveBound cubicPC(
        Handle(Geom2d_Curve)(new Geom2d_BezierCurve(uvPoles)));
    ResidualDispatchDiagnostics modelDispatch;
    check(BoundRepairMetric(cubic, cubicPC, surface, 0, 1, .01, correlated,
                            modelDispatch, &method)
                      .status == "bounded-within-budget" &&
              modelDispatch.highAxisBounded == 1,
          "cubic model class uses complete V corridor");
    ResidualDispatchDiagnostics limitedDispatch;
    const auto limited =
        BoundRepairMetric(cubic, cubicPC, surface, 0, 1, .01, correlated,
                          limitedDispatch, &method, 2, 32768, 32768, 32768, 1);
    check(limited.status == "unavailable" && limited.intervals == 2 &&
              limitedDispatch.highAxisIntervals == 1 &&
              limitedDispatch.correlatedFallbacks == 1,
          "failed high-axis prefix leaves only one true fallback interval");
    ResidualDispatchDiagnostics exhaustedDispatch;
    const auto exhausted =
        BoundRepairMetric(cubic, cubicPC, surface, 0, 1, .01, correlated,
                          exhaustedDispatch, &method, 1);
    check(exhausted.status == "unavailable" && exhausted.intervals == 1 &&
              exhaustedDispatch.correlatedFallbacks == 0,
          "exhausted high-axis prefix cannot restart work");
    ResidualDispatchDiagnostics zeroDispatch;
    check(BoundRepairMetric(cubic, cubicPC, surface, 0, 1, .01, correlated,
                            zeroDispatch, &method, 0)
                      .intervals == 0 &&
              zeroDispatch.highAxisAttempts == 0,
          "zero high-axis allowance performs no work");
    int ticks = 0;
    correlated.continueAssessment = [&]() { return ++ticks < 100; };
    ResidualDispatchDiagnostics cancelledDispatch;
    const auto cancelled =
        BoundRepairMetric(cubic, cubicPC, surface, 0, 1, .01, correlated,
                          cancelledDispatch, &method);
    check(cancelled.status == "unavailable" &&
              cancelled.reason == "assessment-monotonic-time-limit" &&
              cancelledDispatch.correlatedFallbacks == 0,
          "cancelled corridor does not enter fallback");
    correlated.continueAssessment = {};
    gp_Trsf moved;
    moved.SetTranslation(gp_Vec(0, 0, .02));
    auto degreeSurface = [&](int degree) {
      TColgp_Array2OfPnt highPoles(1, 3, 1, degree + 1);
      TColStd_Array2OfReal highWeights(1, 3, 1, degree + 1);
      for (int u = 1; u <= 3; ++u)
        for (int v = 1; v <= degree + 1; ++v) {
          highPoles(u, v) = gp_Pnt((u - 1) / 2., 0, 0);
          highWeights(u, v) = v % 2 ? 1 : .75;
        }
      TColStd_Array1OfInteger highMults(1, 2);
      highMults.Init(degree + 1);
      return RepresentedSurfaceBound(
          Handle(Geom_Surface)(new Geom_BSplineSurface(
              highPoles, highWeights, knots, knots, uMults, highMults, 2,
              degree)), TopLoc_Location());
    };
    for (int degree : {9, 10, 15}) {
      ResidualDispatchDiagnostics degreeDispatch;
      const auto degreeResult = BoundRepairMetric(
          cubic, cubicPC, degreeSurface(degree), 0, .001, .01,
          correlated, degreeDispatch, &method);
      check(degreeResult.status == "bounded-within-budget" &&
                degreeDispatch.highAxisBounded == 1 &&
                method == "shared-high-axis-rational",
            "positive rational surface degree fits exact model capacities");
    }
    check(BoundHighAxisRationalComposition(cubic, cubicPC, degreeSurface(16),
                                           0, .001, .01).status == "unavailable",
          "model numerator degree beyond capacity remains unsupported");
    for (double origin : {0., 81.}) {
      TColgp_Array1OfPnt ninthPoles(1, 10);
      for (int i = 1; i <= 10; ++i)
        ninthPoles(i) = gp_Pnt((i - 1) / 9., 0, i == 10 ? .002 : 0);
      const RepresentedCurveBound ninth(Handle(Geom_Curve)(
          new Geom_BezierCurve(ninthPoles)), TopLoc_Location());
      auto shifted = Handle(Geom_BSplineSurface)::DownCast(degreeSurface(9).surface->Copy());
      shifted->SetVKnot(2, origin + 1);
      shifted->SetVKnot(1, origin);
      TColgp_Array1OfPnt2d shiftedUV(1, 4);
      for (int i = 1; i <= 4; ++i) {
        const double t = (i - 1) / 3.;
        shiftedUV(i) = gp_Pnt2d(t + (i == 2 ? 1e-10 : 0), origin + .2 * t * t);
      }
      const RepresentedCurveBound nonlinearPC(Handle(Geom2d_Curve)(
          new Geom2d_BezierCurve(shiftedUV)));
      const RepresentedSurfaceBound shiftedSurface(shifted, TopLoc_Location());
      ResidualDispatchDiagnostics ninthDispatch;
      const auto ninthResult = BoundRepairMetric(ninth, nonlinearPC, shiftedSurface,
          0, .001, .01, correlated, ninthDispatch, &method);
      check(ninthResult.status == "bounded-within-budget" &&
                method == "shared-high-axis-rational" && ninthDispatch.highAxisBounded == 1,
            "exact degree9 source and actual nonlinear cubic PC fit numerator24");
      const auto model = HighAxisSourcePiece(ninth, 0, .001,
          UniPolynomial{Interval(0), Interval(.001)}, CompositionWork{});
      check(model.polynomial[2].size() == 10 && model.polynomial[2][9].lo > 0,
            "degree9 source retains actual nonzero ninth-order term");
      check(BoundHighAxisRationalComposition(
          RepresentedCurveBound(ninth.c3, TopLoc_Location(moved)), nonlinearPC,
          shiftedSurface, 0, .001, .01).status == "exceeds-budget",
          "degree9 displaced source remains rejected");
    }
    check(HighAxisDegreeCapacity(degreeSurface(9), true, false, 9) &&
              !HighAxisDegreeCapacity(degreeSurface(9), true, false, 10) &&
              !HighAxisDegreeCapacity(degreeSurface(16), true, false, 1),
          "actual source degree and independent lifted cap govern capacity");
    TColgp_Array1OfPnt tenthPoles(1, 11);
    for (int i = 1; i <= 11; ++i) tenthPoles(i) = gp_Pnt((i - 1) / 10., 0, 0);
    check(BoundHighAxisRationalComposition(
        RepresentedCurveBound(Handle(Geom_Curve)(new Geom_BezierCurve(tenthPoles)),
                              TopLoc_Location()), cubicPC, degreeSurface(9),
        0, .001, .01).reason == "unsupported high-axis native class",
        "degree10 cubic-PC implementation rejects numerator25");
    auto invalidWeights = degreeSurface(9);
    invalidWeights.poles[0][0][3] = Interval(0);
    check(BoundHighAxisRationalComposition(cubic, cubicPC, invalidWeights,
        0, .001, .01).reason == "invalid high-axis native weight",
        "nonpositive native weight cannot enter high-axis arithmetic");
    invalidWeights.poles[0][0][3] = Interval(std::numeric_limits<double>::infinity());
    check(BoundHighAxisRationalComposition(cubic, cubicPC, invalidWeights,
        0, .001, .01).reason == "invalid high-axis native weight",
        "nonfinite native weight cannot enter high-axis arithmetic");
    check(BoundHighAxisRationalComposition(
              RepresentedCurveBound(cubic.c3, TopLoc_Location(moved)), cubicPC,
              degreeSurface(15), 0, .001, .01).status == "exceeds-budget",
          "maximum admitted model degree still rejects target displacement");
    check(BoundHighAxisRationalComposition(source, pcurve, degreeSurface(16),
                                           0, .001, .01).status ==
              "bounded-within-budget" &&
              BoundHighAxisRationalComposition(source, pcurve, degreeSurface(17),
                                                0, .001, .01).status ==
                  "unavailable",
          "exact degree-six source uses its own numerator capacity");
    TColgp_Array1OfPnt eighthPoles(1, 9);
    for (int index = 1; index <= 9; ++index)
      eighthPoles(index) = gp_Pnt((index - 1) / 8., 0, 0);
    const RepresentedCurveBound eighth(
        Handle(Geom_Curve)(new Geom_BezierCurve(eighthPoles)), TopLoc_Location());
    check(BoundHighAxisRationalComposition(eighth, pcurve, degreeSurface(14),
                                           0, .001, .01).status ==
              "bounded-within-budget" &&
              BoundHighAxisRationalComposition(eighth, pcurve, degreeSurface(15),
                                                0, .001, .01).status ==
                  "unavailable",
          "exact degree-eight source uses its own numerator capacity");
    TColStd_Array1OfReal rationalWeights(1, 4);
    rationalWeights.Init(1);
    rationalWeights(2) = .75;
    const auto rationalSource = BoundHighAxisRationalComposition(
        RepresentedCurveBound(Handle(Geom_Curve)(
            new Geom_BezierCurve(cubicPoles, rationalWeights)), TopLoc_Location()),
        cubicPC, degreeSurface(9), 0, .001, .01);
    const auto rationalPC = BoundHighAxisRationalComposition(
        cubic, RepresentedCurveBound(Handle(Geom2d_Curve)(
            new Geom2d_BezierCurve(uvPoles, rationalWeights))),
        degreeSurface(9), 0, .001, .01);
    check(rationalSource.status == "unavailable" &&
              rationalSource.reason == "high-axis source or PC rational unsupported" &&
              rationalPC.status == "unavailable" &&
              rationalPC.reason == "high-axis source or PC rational unsupported",
          "higher degree does not admit rational source or pcurve");
    ResidualDispatchDiagnostics rejectedDispatch;
    check(BoundRepairMetric(
              RepresentedCurveBound(cubic.c3, TopLoc_Location(moved)), cubicPC,
              surface, 0, 1, .01, correlated, rejectedDispatch, &method)
                      .status == "exceeds-budget" &&
              rejectedDispatch.highAxisRejected == 1 &&
              rejectedDispatch.correlatedFallbacks == 0,
          "proven source displacement cannot use a fallback to accept");
    auto periodicNative = Handle(Geom_BSplineSurface)::DownCast(native->Copy());
    periodicNative->SetVPeriodic();
    ResidualDispatchDiagnostics periodicDispatch;
    BoundRepairMetric(
        cubic, cubicPC,
        RepresentedSurfaceBound(periodicNative, TopLoc_Location()), 0, .001,
        .01, correlated, periodicDispatch, &method);
    check(periodicDispatch.highAxisAttempts == 0 &&
              periodicDispatch.compositionAttempts == 1,
          "periodic native surface stays outside high-axis class");
    TColgp_Array1OfPnt2d affinePoles(1, 2);
    affinePoles(1) = gp_Pnt2d(0, 0);
    affinePoles(2) = gp_Pnt2d(1, 0);
    ResidualDispatchDiagnostics affineDispatch;
    BoundRepairMetric(source,
                      RepresentedCurveBound(Handle(Geom2d_Curve)(
                          new Geom2d_BezierCurve(affinePoles))),
                      surface, 0, .001, .01, correlated, affineDispatch,
                      &method);
    check(affineDispatch.highAxisAttempts == 0,
          "degree1 spline PC is not silently promoted to native analytic-line "
          "capability");
    auto ellipseNative = Handle(Geom_BSplineSurface)::DownCast(native->Copy());
    for (int u = 1; u <= 3; ++u)
      for (int v = 1; v <= 7; ++v)
        ellipseNative->SetPole(u, v, gp_Pnt(1, 0, 0));
    const RepresentedCurveBound ellipse(
        Handle(Geom_Curve)(new Geom_Ellipse(gp_Elips(gp_Ax2(), 1, .5))),
        TopLoc_Location());
    ResidualDispatchDiagnostics ellipseDispatch;
    check(BoundRepairMetric(
              ellipse, cubicPC,
              RepresentedSurfaceBound(ellipseNative, TopLoc_Location()), 0,
              .001, .01, correlated, ellipseDispatch, &method)
                      .status == "bounded-within-budget" &&
              ellipseDispatch.highAxisBounded == 1,
          "native ellipse source selects explicit retained-tail method");
    Configure(.01, false);
    Reset();
    Boundary boundary;
    boundary.residualDispatch = dispatch;
    EvidenceStore().boundaries.push_back(boundary);
    auto provenance = emscripten::val::object();
    auto document = emscripten::val::object();
    document.set("sourceTransfer", emscripten::val::object());
    provenance.set("documentCoverage", document);
    Export(provenance);
    const auto exported = provenance["repairAssessment"]["boundaries"][0];
    const auto counters = exported["metricDispatch"];
    check(counters["highAxisAttempts"].as<int>() == 1 &&
              counters["highAxisBounded"].as<int>() == 1 &&
              counters["highAxisIntervals"].as<int>() == result.intervals &&
              counters["highAxisWallMilliseconds"].as<double>() >= 0 &&
              !counters.hasOwnProperty("classes") &&
              !exported.hasOwnProperty("residuals"),
          "compact export reports high-axis work without geometry snapshots");
    std::cout << "PASS: " << checks << " high-axis dispatch checks\n";
    return 0;
  } catch (const Standard_Failure &error) {
    std::cerr << error.GetMessageString() << '\n';
  } catch (const std::exception &error) {
    std::cerr << error.what() << '\n';
  }
  return 1;
}
