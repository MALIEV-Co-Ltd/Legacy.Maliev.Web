#include "kernel-repair-residual.hpp"
#include <Geom2d_Line.hxx>
#include <GeomTools.hxx>
#include <Geom_Line.hxx>
#include <Geom_Plane.hxx>
#include <TColStd_Array1OfInteger.hxx>
#include <TColStd_Array1OfReal.hxx>
#include <TColgp_Array1OfPnt.hxx>
#include <TColgp_Array1OfPnt2d.hxx>
#include <TColgp_Array2OfPnt.hxx>
#include <fstream>
#include <iostream>
#include <stdexcept>
using namespace MalievRepair;
int main(int argc, char **argv) {
  int checks = 0;
  auto check = [&](bool condition,
                   const char *name = "composition regression failed") {
    ++checks;
    if (!condition)
      throw std::runtime_error(name);
  };
  try {
    for (int fixture = 1; fixture < argc; ++fixture) {
      std::ifstream stream(argv[fixture]);
      if (!stream)
        throw std::runtime_error("private fixture unavailable");
      auto identity = [&]() {
        for (int row = 0; row < 3; ++row)
          for (int column = 0; column < 4; ++column) {
            double value;
            if (!(stream >> value) || value != (column == row ? 1. : 0.))
              throw std::runtime_error(
                  "private fixture requires exact identity placement");
          }
      };
      Handle(Geom_Surface) privateSurface;
      Handle(Geom_Curve) privateCurve;
      Handle(Geom2d_Curve) privatePC;
      GeomTools::Read(privateSurface, stream);
      identity();
      GeomTools::Read(privateCurve, stream);
      identity();
      GeomTools::Read(privatePC, stream);
      double first, last;
      if (!(stream >> first >> last))
        throw std::runtime_error("invalid private range");
      const CurveBound c(privateCurve, TopLoc_Location()), pc(privatePC);
      const SurfaceBound support(privateSurface, TopLoc_Location());
      const auto start = std::chrono::steady_clock::now();
      const auto result =
          BoundPolynomialComposition(c, pc, support, first, last, .01);
      std::cout << "private " << fixture << " " << result.status << " "
                << result.reason << " upper=" << result.upper
                << " intervals=" << result.intervals
                << " subdivisions=" << result.subdivisions << " ms="
                << std::chrono::duration<double, std::milli>(
                       std::chrono::steady_clock::now() - start)
                       .count()
                << '\n';
      check(result.status == "bounded-within-budget" &&
            result.intervals < 1024);
      gp_Trsf translation;
      translation.SetTranslation(gp_Vec(.02, 0, 0));
      check(BoundPolynomialComposition(
                CurveBound(privateCurve, TopLoc_Location(translation)), pc,
                support, first, last, .01)
                .status == "exceeds-budget");
      check(
          BoundPolynomialComposition(c, pc, support, Down(first), Up(last), .01)
              .status == "bounded-within-budget");
      const auto repeated =
          BoundPolynomialComposition(c, pc, support, first, last, .01);
      check(repeated.status == result.status &&
                repeated.reason == result.reason &&
                repeated.lower == result.lower &&
                repeated.upper == result.upper &&
                repeated.intervals == result.intervals &&
                repeated.subdivisions == result.subdivisions,
            "private cold/warm complete fields");
      std::vector<double> samples{first, last};
      for (const auto &knot : pc.spline.knots)
        if (knot.lo >= first && knot.lo <= last)
          samples.push_back(knot.lo);
      for (const auto &knot : c.spline.knots)
        if (knot.lo >= first && knot.lo <= last)
          samples.push_back(knot.lo);
      bool nativeAgrees = true;
      for (double parameter : samples) {
        const auto uv = privatePC->Value(parameter);
        nativeAgrees =
            nativeAgrees &&
            privateCurve->Value(parameter).Distance(
                privateSurface->Value(uv.X(), uv.Y())) <= result.upper + 1e-9;
      }
      check(nativeAgrees, "private native D0 knots/endpoints diagnostic");
      auto modified = Handle(Geom_BSplineCurve)::DownCast(privateCurve->Copy());
      auto point = modified->Pole(modified->NbPoles() - 2);
      point.SetX(point.X() + .1);
      modified->SetPole(modified->NbPoles() - 2, point);
      check(BoundPolynomialComposition(CurveBound(modified, TopLoc_Location()),
                                       pc, support, first, last, .01)
                    .status == "exceeds-budget",
            "private interior control mutation");
    }
    TColgp_Array2OfPnt poles(1, 4, 1, 4);
    for (int u = 1; u <= 4; ++u)
      for (int v = 1; v <= 4; ++v) {
        const double x = (u - 1) / 3.;
        const double y = (v - 1);
        poles(u, v) = gp_Pnt(x, y - 2 * x, x);
      }
    TColStd_Array1OfReal knots(1, 2);
    knots(1) = 0;
    knots(2) = 1;
    TColStd_Array1OfInteger multiplicities(1, 2);
    multiplicities(1) = multiplicities(2) = 4;
    Handle(Geom_Surface) surface = new Geom_BSplineSurface(
        poles, knots, knots, multiplicities, multiplicities, 3, 3);
    TColgp_Array1OfPnt curvePoles(1, 4);
    TColgp_Array1OfPnt2d uvPoles(1, 4);
    for (int i = 1; i <= 4; ++i) {
      const double x = i == 4 ? 1 : 0;
      curvePoles(i) = gp_Pnt(x, 1, x);
      uvPoles(i) = gp_Pnt2d(x, (1 + 2 * x) / 3.);
    }
    Handle(Geom_Curve) curve = new Geom_BezierCurve(curvePoles);
    Handle(Geom2d_Curve) pcurve = new Geom2d_BezierCurve(uvPoles);
    const CurveBound c(curve, TopLoc_Location()), pc(pcurve);
    const SurfaceBound support(surface, TopLoc_Location());
    const auto positive = BoundPolynomialComposition(c, pc, support, 0, 1, .01);
    std::cout << positive.status << " " << positive.reason << " "
              << positive.upper << " " << positive.intervals << '\n';
    check(positive.status == "bounded-within-budget" &&
          positive.intervals <= 8);
    Handle(Geom2d_Curve) affinePC =
        new Geom2d_Line(gp_Pnt2d(.1, .2), gp_Dir2d(.6, .8));
    TColgp_Array1OfPnt affinePoles(1, 4);
    for (int index = 1; index <= 4; ++index) {
      const double t = (index - 1) / 3.;
      const double u = .1 + .6 * t, v = .2 + .8 * t;
      affinePoles(index) = gp_Pnt(u, 3 * v - 2 * u, u);
    }
    Handle(Geom_Curve) affineCurve = new Geom_BezierCurve(affinePoles);
    const auto affinePositive =
        BoundPolynomialComposition(CurveBound(affineCurve, TopLoc_Location()),
                                   CurveBound(affinePC), support, 0, 1, .01);
    check(affinePositive.status == "bounded-within-budget" &&
              affinePositive.intervals <= 16,
          "analytic rotated line PC uses exact common polynomial composition");
    gp_Trsf affineTranslation;
    affineTranslation.SetTranslation(gp_Vec(.02, 0, 0));
    check(BoundPolynomialComposition(
              CurveBound(affineCurve, TopLoc_Location(affineTranslation)),
              CurveBound(affinePC), support, 0, 1, .01)
                  .status == "exceeds-budget",
          "line-PC source displacement is not hidden by affine dispatch");
    bool affineNativeAgrees = true;
    for (double parameter : {0., .25, .5, .75, 1.}) {
      const auto uv = affinePC->Value(parameter);
      affineNativeAgrees =
          affineNativeAgrees &&
          affineCurve->Value(parameter).Distance(
              surface->Value(uv.X(), uv.Y())) <= affinePositive.upper;
    }
    check(affineNativeAgrees, "line-PC native D0 diagnostic is inside bound");
    check(BoundPolynomialComposition(CurveBound(affineCurve, TopLoc_Location()),
                                     CurveBound(affinePC), support, Down(0.),
                                     Up(1.), .01)
                  .status == "bounded-within-budget",
          "line-PC retains full endpoint ULP extensions");
    auto amplifiedSurface =
        Handle(Geom_BSplineSurface)::DownCast(surface->Copy());
    for (int u = 1; u <= 4; ++u)
      for (int v = 1; v <= 4; ++v) {
        auto point = amplifiedSurface->Pole(u, v);
        point.SetX(point.X() * 1e15);
        amplifiedSurface->SetPole(u, v, point);
      }
    Handle(Geom2d_Curve) tinySlopePC =
        new Geom2d_Line(gp_Pnt2d(0, 0), gp_Dir2d(1e-16, 1));
    TColgp_Array1OfPnt tinyPoles(1, 4);
    for (int index = 1; index <= 4; ++index) {
      const double t = (index - 1) / 3.;
      tinyPoles(index) = gp_Pnt(.1 * t, 3 * t - 2e-16 * t, 1e-16 * t);
    }
    Handle(Geom_Curve) tinyCurve = new Geom_BezierCurve(tinyPoles);
    const auto tinyResult = BoundPolynomialComposition(
        CurveBound(tinyCurve, TopLoc_Location()), CurveBound(tinySlopePC),
        SurfaceBound(amplifiedSurface, TopLoc_Location()), 0, 1, .01);
    check(tinyResult.status == "bounded-within-budget" &&
              tinyResult.upper < 1e-8,
          "tiny transverse slope remains physical under amplified support");
    Handle(Geom2d_Curve) snappedPC =
        new Geom2d_Line(gp_Pnt2d(0, 0), gp_Dir2d(0, 1));
    check(BoundPolynomialComposition(
              CurveBound(tinyCurve, TopLoc_Location()), CurveBound(snappedPC),
              SurfaceBound(amplifiedSurface, TopLoc_Location()), 0, 1, .01)
                  .status == "exceeds-budget",
          "snapped tiny slope is geometrically different and rejected");
    auto rationalSupport =
        Handle(Geom_BSplineSurface)::DownCast(surface->Copy());
    rationalSupport->SetWeight(2, 2, .5);
    const auto rationalRejected = BoundPolynomialComposition(
        c, pc, SurfaceBound(rationalSupport, TopLoc_Location()), 0, 1, .01);
    check(rationalRejected.status == "unavailable" &&
              rationalRejected.reason == "rational composition unsupported" &&
              rationalRejected.intervals == 0,
          "line extension does not relax rational support rejection");
    TColStd_Array1OfReal periodicKnots(1, 3);
    periodicKnots(1) = 0;
    periodicKnots(2) = 1;
    periodicKnots(3) = 2;
    TColStd_Array1OfInteger periodicMults(1, 3);
    periodicMults.Init(2);
    Handle(Geom_Surface) periodicSurface =
        new Geom_BSplineSurface(poles, knots, periodicKnots, multiplicities,
                                periodicMults, 3, 3, false, true);
    const auto periodicRejected = BoundPolynomialComposition(
        c, pc, SurfaceBound(periodicSurface, TopLoc_Location()), 0, 1, .01);
    check(periodicRejected.status == "unavailable" &&
              periodicRejected.reason == "periodic composition unsupported" &&
              periodicRejected.intervals == 0,
          "line extension does not relax periodic support rejection");
    CorrelatedDiagnostics correlated;
    ResidualDispatchDiagnostics dispatch;
    const RepresentedCurveBound representedCurve(curve, TopLoc_Location()),
        representedPC(pcurve);
    const RepresentedSurfaceBound representedSurface(surface,
                                                     TopLoc_Location());
    const auto combinedCap =
        BoundRepairMetric(representedCurve, representedPC, representedSurface,
                          0, 1, .01, correlated, dispatch, nullptr, 3, 2);
    check(combinedCap.status == "unavailable" && combinedCap.intervals == 3 &&
              dispatch.compositionIntervals == 2 &&
              dispatch.correlatedFallbacks == 1,
          "worked composition fallback shares remaining interval cap");
    std::string zeroMethod;
    const auto zeroCap =
        BoundRepairMetric(representedCurve, representedPC, representedSurface,
                          0, 1, .01, correlated, dispatch, &zeroMethod, 0);
    check(zeroCap.status == "unavailable" && zeroCap.intervals == 0 &&
              zeroMethod == "not-assessed-interval-limit",
          "zero metric interval budget");
    check(BoundCorrelatedResidual(representedCurve, representedPC,
                                  representedSurface, 0, 1, .01, nullptr,
                                  &correlated, 0)
                  .intervals == 0,
          "zero direct correlated remaining budget");
    correlated.continueAssessment = []() { return false; };
    ResidualDispatchDiagnostics cancelledDispatch;
    const auto cancellation =
        BoundRepairMetric(representedCurve, representedPC, representedSurface,
                          0, 1, .01, correlated, cancelledDispatch);
    check(cancellation.status == "unavailable" &&
              cancellation.reason == "assessment-monotonic-time-limit" &&
              cancelledDispatch.correlatedFallbacks == 0,
          "cancelled polynomial does not restart fallback");
    correlated.continueAssessment = {};
    ResidualDispatchDiagnostics unsupportedDispatch;
    const RepresentedCurveBound line(
        Handle(Geom_Curve)(new Geom_Line(gp_Pnt(0, 0, 0), gp_Dir(1, 0, 0))),
        TopLoc_Location());
    const RepresentedCurveBound linePC(
        Handle(Geom2d_Curve)(new Geom2d_Line(gp_Pnt2d(0, 0), gp_Dir2d(1, 0))));
    const RepresentedSurfaceBound plane(
        Handle(Geom_Surface)(
            new Geom_Plane(gp_Pln(gp_Pnt(0, 0, 0), gp_Dir(0, 0, 1)))),
        TopLoc_Location());
    const auto fallbackPositive = BoundRepairMetric(
        line, linePC, plane, 0, 1, .01, correlated, unsupportedDispatch);
    check(fallbackPositive.status == "bounded-within-budget" &&
              unsupportedDispatch.compositionIntervals == 0 &&
              unsupportedDispatch.correlatedFallbacks == 1,
          "unsupported zero-work path retains correlated positive");
    const auto repeat = BoundPolynomialComposition(c, pc, support, 0, 1, .01);
    check(repeat.status == positive.status && repeat.upper == positive.upper &&
          repeat.lower == positive.lower &&
          repeat.intervals == positive.intervals &&
          repeat.subdivisions == positive.subdivisions &&
          repeat.reason == positive.reason);
    gp_Trsf move;
    move.SetTranslation(gp_Vec(.02, 0, 0));
    const auto changed = BoundPolynomialComposition(
        CurveBound(curve, TopLoc_Location(move)), pc, support, 0, 1, .01);
    check(changed.status == "exceeds-budget" && changed.lower > .01);
    check(
        BoundPolynomialComposition(c, pc, support, 0, 1, .01, 32768, 32, []() {
          return false;
        }).status == "unavailable");
    check(BoundPolynomialComposition(c, pc, support, 0, 1, .01, 1).status ==
          "unavailable");
    const auto extended =
        BoundPolynomialComposition(c, pc, support, Down(0.), Up(1.), .01);
    check(extended.status == "bounded-within-budget");
    check(BoundPolynomialComposition(c, pc, support, 0, 1,
                                     std::numeric_limits<double>::infinity())
              .status == "unavailable");
    curvePoles(2).SetY(1.2);
    Handle(Geom_Curve) spike = new Geom_BezierCurve(curvePoles);
    check(BoundPolynomialComposition(CurveBound(spike, TopLoc_Location()), pc,
                                     support, 0, 1, .01)
              .status == "exceeds-budget");
    int deadlineTicks = 0;
    const auto interrupted =
        BoundPolynomialComposition(c, pc, support, 0, 1, .01, 32768, 32,
                                   [&]() { return ++deadlineTicks < 100; });
    check(interrupted.status == "unavailable" &&
              interrupted.reason == "assessment-monotonic-time-limit" &&
              interrupted.intervals > 0,
          "deadline inside polynomial work");
    TColStd_Array1OfReal narrowKnots(1, 4);
    narrowKnots(1) = 0;
    narrowKnots(2) = .5001229;
    narrowKnots(3) = .5001231;
    narrowKnots(4) = 1;
    TColStd_Array1OfInteger narrowMults(1, 4);
    narrowMults(1) = narrowMults(4) = 4;
    narrowMults(2) = narrowMults(3) = 3;
    TColgp_Array1OfPnt narrowPoles(1, 10);
    for (int span = 1; span <= 3; ++span) {
      const double a = narrowKnots(span), b = narrowKnots(span + 1);
      const double coefficients[] = {a * a * a, a * a * b, a * b * b,
                                     b * b * b};
      for (int index = 0; index < 4; ++index)
        narrowPoles((span - 1) * 3 + index + 1) =
            gp_Pnt(coefficients[index], 1, coefficients[index]);
    }
    Handle(Geom_Curve) narrowBase =
        new Geom_BSplineCurve(narrowPoles, narrowKnots, narrowMults, 3);
    check(BoundPolynomialComposition(CurveBound(narrowBase, TopLoc_Location()),
                                     pc, support, 0, 1, .01)
                  .status == "bounded-within-budget",
          "narrow native knot partition baseline");
    narrowPoles(5).SetY(1.2);
    Handle(Geom_Curve) narrowSpike =
        new Geom_BSplineCurve(narrowPoles, narrowKnots, narrowMults, 3);
    bool sampleMisses = true;
    for (double t : {0., .25, .5, .75, 1.})
      sampleMisses = sampleMisses &&
                     narrowSpike->Value(t).Distance(curve->Value(t)) < 1e-10;
    check(sampleMisses, "narrow spike lies between ordinary sample nodes");
    check(BoundPolynomialComposition(CurveBound(narrowSpike, TopLoc_Location()),
                                     pc, support, 0, 1, .01)
                  .status == "exceeds-budget",
          "whole native span rejects narrow spike");
    for (int span = 1; span <= 3; ++span)
      for (int index = 0; index < 4; ++index) {
        const double t =
            narrowKnots(span) +
            (narrowKnots(span + 1) - narrowKnots(span)) * index / 3.;
        narrowPoles((span - 1) * 3 + index + 1) = affineCurve->Value(t);
      }
    Handle(Geom_Curve) affineNarrowBase =
        new Geom_BSplineCurve(narrowPoles, narrowKnots, narrowMults, 3);
    check(BoundPolynomialComposition(
              CurveBound(affineNarrowBase, TopLoc_Location()),
              CurveBound(affinePC), support, 0, 1, .01)
                  .status == "bounded-within-budget",
          "line-PC retains narrow source knot partition baseline");
    narrowPoles(5).SetZ(narrowPoles(5).Z() + .2);
    Handle(Geom_Curve) affineNarrowSpike =
        new Geom_BSplineCurve(narrowPoles, narrowKnots, narrowMults, 3);
    bool affineSampleMisses = true;
    for (double t : {0., .25, .5, .75, 1.})
      affineSampleMisses =
          affineSampleMisses &&
          affineNarrowSpike->Value(t).Distance(affineCurve->Value(t)) < 1e-10;
    check(affineSampleMisses &&
              BoundPolynomialComposition(
                  CurveBound(affineNarrowSpike, TopLoc_Location()),
                  CurveBound(affinePC), support, 0, 1, .01)
                      .status == "exceeds-budget",
          "line-PC rejects a spike between all five ordinary sample nodes");
    check(BoundPolynomialComposition(CurveBound(affineCurve, TopLoc_Location()),
                                     CurveBound(affinePC), support, 0, 1, .01,
                                     1)
                  .status == "unavailable",
          "line-PC candidate cell work respects remaining allowance");
    for (int u = 1; u <= 4; ++u)
      for (int v = 1; v <= 4; ++v)
        poles(u, v) = gp_Pnt(0, 0, (u == 4 && v == 4) ? 1 : 0);
    Handle(Geom_Surface) degree18Surface = new Geom_BSplineSurface(
        poles, knots, knots, multiplicities, multiplicities, 3, 3);
    for (int i = 1; i <= 4; ++i) {
      curvePoles(i) = gp_Pnt(0, 0, 0);
      uvPoles(i) = gp_Pnt2d(i == 4 ? 1 : 0, i == 4 ? 1 : 0);
    }
    const CurveBound zero(Handle(Geom_Curve)(new Geom_BezierCurve(curvePoles)),
                          TopLoc_Location());
    const CurveBound degree3UV(
        Handle(Geom2d_Curve)(new Geom2d_BezierCurve(uvPoles)));
    check(BoundPolynomialComposition(
              zero, degree3UV, SurfaceBound(degree18Surface, TopLoc_Location()),
              0, 1, .01)
                  .status == "exceeds-budget",
          "all degree18 composition terms retained");
    std::cout << "PASS: " << checks << " polynomial composition checks\n";
    return 0;
  } catch (const Standard_Failure &failure) {
    std::cerr << failure.GetMessageString() << '\n';
  } catch (const std::exception &failure) {
    std::cerr << failure.what() << '\n';
  }
  return 1;
}
