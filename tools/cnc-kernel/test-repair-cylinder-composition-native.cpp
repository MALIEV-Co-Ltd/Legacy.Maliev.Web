#include "kernel-repair-cylinder-composition.hpp"
#include <GeomTools.hxx>
#include <Geom_CylindricalSurface.hxx>
#include <TColStd_Array1OfInteger.hxx>
#include <TColStd_Array1OfReal.hxx>
#include <TColgp_Array1OfPnt.hxx>
#include <TColgp_Array1OfPnt2d.hxx>
#include <fstream>
#include <iostream>
#include <stdexcept>
using namespace MalievRepair;
int main(int argc, char **argv) {
  int checks = 0;
  auto check = [&](bool condition, const char *name) {
    ++checks;
    if (!condition)
      throw std::runtime_error(name);
  };
  try {
    for (int fixture = 1; fixture < argc; ++fixture) {
      std::ifstream stream(argv[fixture]);
      if (!stream)
        throw std::runtime_error("private cylinder fixture unavailable");
      auto identity = [&]() {
        for (int row = 0; row < 3; ++row)
          for (int column = 0; column < 4; ++column) {
            double value;
            if (!(stream >> value) || value != (row == column ? 1. : 0.))
              throw std::runtime_error(
                  "private cylinder placement not identity");
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
        throw std::runtime_error("private cylinder range unavailable");
      const CurveBound c(privateCurve, TopLoc_Location()), pc(privatePC);
      const SurfaceBound s(privateSurface, TopLoc_Location());
      const auto start = std::chrono::steady_clock::now();
      const auto bound =
          BoundCylindricalComposition(c, pc, s, first, last, .01);
      std::cout << "private " << fixture << " " << bound.status << " "
                << bound.reason << " upper=" << bound.upper
                << " intervals=" << bound.intervals
                << " subdivisions=" << bound.subdivisions << " ms="
                << std::chrono::duration<double, std::milli>(
                       std::chrono::steady_clock::now() - start)
                       .count()
                << '\n';
      check(bound.status == "bounded-within-budget" && bound.intervals < 128,
            "private cylinder completes with deterministic bounded work");
      gp_Trsf translation;
      translation.SetTranslation(gp_Vec(.02, 0, 0));
      check(BoundCylindricalComposition(
                CurveBound(privateCurve, TopLoc_Location(translation)), pc, s,
                first, last, .01)
                    .status == "exceeds-budget",
            "private source translation rejects");
      const auto repeated =
          BoundCylindricalComposition(c, pc, s, first, last, .01);
      check(
          repeated.status == bound.status && repeated.reason == bound.reason &&
              repeated.lower == bound.lower && repeated.upper == bound.upper &&
              repeated.intervals == bound.intervals &&
              repeated.subdivisions == bound.subdivisions,
          "private repeat retains every certificate and work field");
      check(BoundCylindricalComposition(c, pc, s, Down(first), Up(last), .01)
                    .status == "bounded-within-budget",
            "private cylinder preserves native exterior endpoint slivers");
      bool nativeAgrees = true;
      std::vector<double> samples{first, last};
      for (const auto knot : c.spline.knots)
        if (knot.lo >= first && knot.lo <= last)
          samples.push_back(knot.lo);
      for (const double parameter : samples) {
        const auto uv = privatePC->Value(parameter);
        nativeAgrees =
            nativeAgrees &&
            privateCurve->Value(parameter).Distance(
                privateSurface->Value(uv.X(), uv.Y())) <= bound.upper + 1e-9;
      }
      check(nativeAgrees,
            "private cylinder native D0 knot diagnostics enclosed");
    }
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
    const auto result = BoundCylindricalComposition(
        CurveBound(source, TopLoc_Location()), CurveBound(pcurve),
        SurfaceBound(surface, TopLoc_Location()), 0, 1, .01);
    check(result.status == "bounded-within-budget" && result.intervals < 16,
          "cubic source and affine cylindrical lift share one parameter bound");
    const CurveBound c(source, TopLoc_Location()), pc(pcurve);
    const SurfaceBound support(surface, TopLoc_Location());
    for (double center : {0., 1., 100.}) {
      const auto trig = BoundAffineTrigPolynomial(
          {Interval(center - .49), Interval(.98)}, CompositionWork{});
      bool contains = true;
      for (double parameter : {0., .125, .5, .875, 1.}) {
        auto evaluate = [&](const UniPolynomial &polynomial) {
          Interval value;
          for (auto term = polynomial.rbegin(); term != polynomial.rend();
               ++term)
            value = value * Interval(parameter) + *term;
          return value + Interval(-trig.remainder, trig.remainder);
        };
        const double angle = center - .49 + .98 * parameter;
        const auto sine = evaluate(trig.sine), cosine = evaluate(trig.cosine);
        contains = contains && sine.lo <= std::sin(angle) &&
                   sine.hi >= std::sin(angle) && cosine.lo <= std::cos(angle) &&
                   cosine.hi >= std::cos(angle);
      }
      check(contains && trig.remainder > 0 && trig.remainder < 6e-9,
            "whole Taylor remainder includes phase-center diagnostics");
    }
    gp_Trsf move;
    move.SetTranslation(gp_Vec(.02, 0, 0));
    check(BoundCylindricalComposition(CurveBound(source, TopLoc_Location(move)),
                                      pc, support, 0, 1, .01)
                  .status == "exceeds-budget",
          "translated source cannot use matched cylindrical certificate");
    auto shiftedPC = Handle(Geom2d_BSplineCurve)::DownCast(pcurve->Copy());
    for (int pole = 1; pole <= shiftedPC->NbPoles(); ++pole)
      shiftedPC->SetPole(pole,
                         shiftedPC->Pole(pole).Translated(gp_Vec2d(.1, 0)));
    check(BoundCylindricalComposition(c, CurveBound(shiftedPC), support, 0, 1,
                                      .01)
                  .status == "exceeds-budget",
          "fitted angular phase is retained instead of aligned or snapped");
    gp_Trsf placement;
    placement.SetRotation(gp_Ax1(gp_Pnt(0, 0, 0), gp_Dir(1, 1, 1)), .3);
    placement.SetTranslationPart(gp_Vec(10, -20, 30));
    check(BoundCylindricalComposition(
              CurveBound(source, TopLoc_Location(placement)), pc,
              SurfaceBound(surface, TopLoc_Location(placement)), 0, 1, .01)
                  .status == "bounded-within-budget",
          "matching nontrivial source and support placement remains bounded");
    check(BoundCylindricalComposition(
              c, pc, SurfaceBound(surface, TopLoc_Location(move)), 0, 1, .01)
                  .status == "exceeds-budget",
          "support-only placement change rejects");
    check(
        BoundCylindricalComposition(c, pc, support, 0, 1, .01, 1).intervals ==
                1 &&
            BoundCylindricalComposition(c, pc, support, 0, 1, .01, 1).status ==
                "unavailable",
        "cylinder interval cap charges prefix without off-by-one");
    check(BoundCylindricalComposition(c, pc, support, 0, 1, .01, 0).intervals ==
              0,
          "zero cylindrical allowance cannot start work");
    int ticks = 0;
    const auto cancelled = BoundCylindricalComposition(
        c, pc, support, 0, 1, .01, 32768, 32, [&]() { return ++ticks < 30; });
    check(cancelled.status == "unavailable" &&
              cancelled.reason == "assessment-monotonic-time-limit" &&
              cancelled.intervals > 0,
          "deadline interrupts inside shared trig work");
    auto rationalPC = Handle(Geom2d_BSplineCurve)::DownCast(pcurve->Copy());
    rationalPC->SetWeight(2, .5);
    const auto rational = BoundCylindricalComposition(c, CurveBound(rationalPC),
                                                      support, 0, 1, .01);
    check(rational.status == "unavailable" && rational.intervals == 0 &&
              rational.reason == "rational cylindrical composition unsupported",
          "rational angle is unsupported rather than treated as affine");
    TColStd_Array1OfReal spikeKnots(1, 4);
    spikeKnots(1) = 0;
    spikeKnots(2) = .5001229;
    spikeKnots(3) = .5001231;
    spikeKnots(4) = 1;
    TColStd_Array1OfInteger spikeMults(1, 4);
    spikeMults(1) = spikeMults(4) = 4;
    spikeMults(2) = spikeMults(3) = 3;
    TColgp_Array1OfPnt spikePoles(1, 10);
    for (int span = 1; span <= 3; ++span)
      for (int index = 0; index < 4; ++index) {
        const double t = spikeKnots(span) +
                         (spikeKnots(span + 1) - spikeKnots(span)) * index / 3.;
        spikePoles((span - 1) * 3 + index + 1) = source->Value(t);
      }
    Handle(Geom_Curve) narrowBase =
        new Geom_BSplineCurve(spikePoles, spikeKnots, spikeMults, 3);
    check(BoundCylindricalComposition(CurveBound(narrowBase, TopLoc_Location()),
                                      pc, support, 0, 1, .01)
                  .status == "bounded-within-budget",
          "cylindrical common partition retains tiny source knot spans");
    spikePoles(5).SetZ(spikePoles(5).Z() + .2);
    Handle(Geom_Curve) narrowSpike =
        new Geom_BSplineCurve(spikePoles, spikeKnots, spikeMults, 3);
    bool samplesMiss = true;
    for (double t : {0., .25, .5, .75, 1.})
      samplesMiss = samplesMiss &&
                    narrowSpike->Value(t).Distance(source->Value(t)) < 1e-10;
    check(samplesMiss && BoundCylindricalComposition(
                             CurveBound(narrowSpike, TopLoc_Location()), pc,
                             support, 0, 1, .01)
                                 .status == "exceeds-budget",
          "cylindrical whole-domain bound detects between-sample spike");
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
    const auto wideAngle =
        BoundCylindricalComposition(CurveBound(quarter, TopLoc_Location()),
                                    CurveBound(quarterPC), support, 0, 1, .01);
    check(wideAngle.status == "bounded-within-budget" &&
              wideAngle.subdivisions > 0 && wideAngle.intervals < 128,
          "wide angular piece subdivides before applying bounded Taylor model");
    check(BoundCylindricalComposition(c, pc, support, 0, 1,
                                      std::numeric_limits<double>::infinity())
                  .status == "unavailable",
          "nonfinite budget is unavailable rather than automatic acceptance");
    check(BoundCylindricalComposition(c, pc, support, 1, 0, .01).status ==
              "unavailable",
          "reversed source interval cannot select another traversal");
    std::cout << "PASS: " << checks << " cylindrical composition checks\n";
    return 0;
  } catch (const Standard_Failure &error) {
    std::cerr << error.GetMessageString() << '\n';
  } catch (const std::exception &error) {
    std::cerr << error.what() << '\n';
  }
  return 1;
}
