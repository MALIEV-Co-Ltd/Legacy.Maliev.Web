#include "kernel-repair-rational-composition.hpp"
#include <GeomTools.hxx>
#include <Geom_Circle.hxx>
#include <TColStd_Array1OfInteger.hxx>
#include <TColStd_Array1OfReal.hxx>
#include <TColStd_Array2OfReal.hxx>
#include <TColgp_Array1OfPnt.hxx>
#include <TColgp_Array1OfPnt2d.hxx>
#include <TColgp_Array2OfPnt.hxx>
#include <fstream>
#include <iostream>
#include <stdexcept>
using namespace MalievRepair;
// Generated tensor fixture: degree5 clamped U and degree2 periodic V. Native
// weights vary across the periodic direction even for simple Cartesian images.
static Handle(Geom_BSplineSurface) FixtureSurface(double origin = 0) {
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
  for (int i = 1; i <= 5; ++i)
    vKnots(i) = origin + i - 1;
  TColStd_Array1OfInteger uMults(1, 2), vMults(1, 5);
  uMults.Init(6);
  vMults.Init(2);
  return new Geom_BSplineSurface(poles, weights, uKnots, vKnots, uMults, vMults,
                                 5, 2, false, true);
}
static Handle(Geom_Curve) FixtureSource(bool cubic = false) {
  TColgp_Array1OfPnt poles(1, 4);
  for (int index = 1; index <= 4; ++index)
    poles(index) =
        gp_Pnt(cubic ? (index == 4 ? 1. : 0.) : (index - 1) / 3., 1, 0);
  return new Geom_BezierCurve(poles);
}
static Handle(Geom2d_Curve)
    FixturePC(double firstV, double lastV, bool cubicU = false,
              bool cubicV = false) {
  TColgp_Array1OfPnt2d poles(1, 4);
  for (int index = 1; index <= 4; ++index) {
    const double u = cubicU ? (index == 4 ? 1. : 0.) : (index - 1) / 3.;
    const double v = cubicV ? (index == 4 ? 1. : 0.) : (index - 1) / 3.;
    poles(index) = gp_Pnt2d(u, firstV + (lastV - firstV) * v);
  }
  return new Geom2d_BezierCurve(poles);
}
int main(int argc, char **argv) {
  int checks = 0;
  auto check = [&](bool condition, const char *name) {
    ++checks;
    if (!condition)
      throw std::runtime_error(name);
  };
  try {
    RationalPoly24 degree12(13, Interval(0));
    degree12[12] = Interval(1);
    const auto degree24 =
        RationalProduct(degree12, degree12, CompositionWork{});
    check(degree24.size() == 25 && degree24[24].lo <= 1 && degree24[24].hi >= 1,
          "homogeneous residual product retains its degree24 coefficient");
    for (int fixture = 1; fixture < argc; ++fixture) {
      std::ifstream stream(argv[fixture]);
      if (!stream)
        throw std::runtime_error("private rational fixture unavailable");
      auto identity = [&]() {
        for (int row = 0; row < 3; ++row)
          for (int column = 0; column < 4; ++column) {
            double value;
            if (!(stream >> value) || value != (row == column ? 1. : 0.))
              throw std::runtime_error(
                  "private rational placement not identity");
          }
      };
      Handle(Geom_Surface) surface;
      Handle(Geom_Curve) source;
      Handle(Geom2d_Curve) pcurve;
      GeomTools::Read(surface, stream);
      identity();
      GeomTools::Read(source, stream);
      identity();
      GeomTools::Read(pcurve, stream);
      double first, last;
      if (!(stream >> first >> last))
        throw std::runtime_error("private rational range unavailable");
      const auto start = std::chrono::steady_clock::now();
      const auto result = BoundRationalComposition(
          CurveBound(source, TopLoc_Location()), CurveBound(pcurve),
          SurfaceBound(surface, TopLoc_Location()), first, last, .01);
      std::cout << "private " << fixture << " " << result.status << " "
                << result.reason << " upper=" << result.upper
                << " intervals=" << result.intervals
                << " subdivisions=" << result.subdivisions << " ms="
                << std::chrono::duration<double, std::milli>(
                       std::chrono::steady_clock::now() - start)
                       .count()
                << '\n';
      check(result.status == "bounded-within-budget",
            "exact represented weighted periodic support residual is bounded");
      check(result.intervals < 512,
            "private rational comparison has bounded cost");
      gp_Trsf translation;
      translation.SetTranslation(gp_Vec(.02, 0, 0));
      check(BoundRationalComposition(
                CurveBound(source, TopLoc_Location(translation)),
                CurveBound(pcurve), SurfaceBound(surface, TopLoc_Location()),
                first, last, .01)
                    .status == "exceeds-budget",
            "private rational source displacement rejects");
      const auto repeated = BoundRationalComposition(
          CurveBound(source, TopLoc_Location()), CurveBound(pcurve),
          SurfaceBound(surface, TopLoc_Location()), first, last, .01);
      check(repeated.status == result.status &&
                repeated.reason == result.reason &&
                repeated.lower == result.lower &&
                repeated.upper == result.upper &&
                repeated.intervals == result.intervals &&
                repeated.subdivisions == result.subdivisions,
            "private rational complete certificate and work repeat exactly");
      check(BoundRationalComposition(CurveBound(source, TopLoc_Location()),
                                     CurveBound(pcurve),
                                     SurfaceBound(surface, TopLoc_Location()),
                                     Down(first), Up(last), .01)
                    .status == "bounded-within-budget",
            "private rational full endpoint ULP extensions");
      std::vector<double> samples{first, last};
      const CurveBound sourceBound(source, TopLoc_Location()), pcBound(pcurve);
      for (const auto *axis : {&sourceBound.spline, &pcBound.spline})
        for (const auto knot : axis->knots)
          if (knot.lo >= first && knot.lo <= last)
            samples.push_back(knot.lo);
      bool nativeAgrees = true;
      for (double parameter : samples) {
        const auto uv = pcurve->Value(parameter);
        nativeAgrees = nativeAgrees && source->Value(parameter).Distance(
                                           surface->Value(uv.X(), uv.Y())) <=
                                           result.upper + 1e-9;
      }
      check(nativeAgrees,
            "private rational native D0 knot diagnostics enclosed");
      Handle(Geom_Curve) modified =
          Handle(Geom_Curve)::DownCast(source->Copy());
      if (auto spline = Handle(Geom_BSplineCurve)::DownCast(modified)) {
        auto point = spline->Pole(spline->NbPoles() / 2);
        point.SetZ(point.Z() + .2);
        spline->SetPole(spline->NbPoles() / 2, point);
      } else {
        const auto circle = Handle(Geom_Circle)::DownCast(modified);
        circle->SetRadius(circle->Radius() + .2);
      }
      check(BoundRationalComposition(
                CurveBound(modified, TopLoc_Location()), CurveBound(pcurve),
                SurfaceBound(surface, TopLoc_Location()), first, last, .01)
                    .status == "exceeds-budget",
            "private rational interior or radius mutation rejects");
    }
    const auto surface = FixtureSurface();
    const auto source = FixtureSource();
    const auto pc = FixturePC(-.1, .1);
    const CurveBound c(source, TopLoc_Location()), p(pc);
    const SurfaceBound s(surface, TopLoc_Location());
    const auto seam = BoundRationalComposition(c, p, s, 0, 1, .01);
    check(seam.status == "bounded-within-budget" && seam.upper < 1e-8,
          "generated nonconstant weights across periodic seam preserve "
          "correspondence");
    const auto originShift = BoundRationalComposition(
        c, CurveBound(FixturePC(9.9, 10.1)),
        SurfaceBound(FixtureSurface(10), TopLoc_Location()), 0, 1, .01);
    check(originShift.status == "bounded-within-budget" &&
              originShift.upper < 1e-8,
          "periodic shift identity includes nonzero native origin");
    const auto nonlinear = BoundRationalComposition(
        CurveBound(FixtureSource(true), TopLoc_Location()),
        CurveBound(FixturePC(.5, 2.5, true, true)), s, 0, 1, .01);
    check(
        nonlinear.status == "bounded-within-budget",
        "both U and V vary cubically without dropping transverse coefficients");
    auto highSurface = FixtureSurface();
    for (int u = 1; u <= 6; ++u)
      for (int v = 1; v <= 8; ++v) {
        highSurface->SetPole(u, v, gp_Pnt(0, 0, 0));
        highSurface->SetWeight(u, v, 1);
      }
    highSurface->SetWeight(6, 3, 2);
    const auto homogeneous = RationalSurfacePiece(
        SurfaceBound(highSurface, TopLoc_Location()), 5, 2,
        {Interval(0), Interval(0), Interval(0), Interval(1)},
        {Interval(0), Interval(0), Interval(0), Interval(1)},
        CompositionWork{});
    const auto highNumerator =
        RationalProduct({Interval(0), Interval(0), Interval(0), Interval(1)},
                        homogeneous[3], CompositionWork{});
    check(highNumerator.size() == 25 && highNumerator[24].lo <= 1 &&
              highNumerator[24].hi >= 1 && highNumerator[3].lo <= 1 &&
              highNumerator[3].hi >= 1,
          "native tensor W=1+s21 yields the full s3+s24 numerator");
    bool overflowRejected = false;
    try {
      RationalProduct(degree24, {Interval(0), Interval(1)}, CompositionWork{});
    } catch (const Standard_Failure &) {
      overflowRejected = true;
    }
    check(overflowRejected,
          "degree25 cannot silently exceed class-specific storage");
    gp_Trsf placement;
    placement.SetRotation(gp_Ax1(gp_Pnt(0, 0, 0), gp_Dir(1, 1, 1)), .3);
    placement.SetTranslationPart(gp_Vec(10, -20, 30));
    check(BoundRationalComposition(
              CurveBound(source, TopLoc_Location(placement)), p,
              SurfaceBound(surface, TopLoc_Location(placement)), 0, 1, .01)
                  .status == "bounded-within-budget",
          "homogeneous placement includes translation times W exactly");
    auto denominatorSurface = FixtureSurface();
    for (int u = 1; u <= 6; ++u)
      for (int v = 1; v <= 8; ++v) {
        denominatorSurface->SetPole(u, v, gp_Pnt(0, 0, 0));
        denominatorSurface->SetWeight(u, v, u == 6 ? .01 : 1);
      }
    TColgp_Array1OfPnt zeroPoles(1, 4);
    zeroPoles.Init(gp_Pnt(0, 0, 0));
    const CurveBound zero(Handle(Geom_Curve)(new Geom_BezierCurve(zeroPoles)),
                          TopLoc_Location());
    TColgp_Array1OfPnt2d exteriorPoles(1, 4);
    exteriorPoles.Init(gp_Pnt2d(1.1, .25));
    const CurveBound exteriorPC(
        Handle(Geom2d_Curve)(new Geom2d_BezierCurve(exteriorPoles)));
    const auto denominator = BoundRationalComposition(
        zero, exteriorPC, SurfaceBound(denominatorSurface, TopLoc_Location()),
        0, 1, .01, 32768, 0);
    check(denominator.status == "unavailable" && denominator.intervals > 0,
          "positive native weights cannot clamp a negative exterior guard W");
    for (int v = 1; v <= 8; ++v)
      denominatorSurface->SetWeight(6, v, 1e-30);
    exteriorPoles.Init(gp_Pnt2d(1, .25));
    check(BoundRationalComposition(
              zero,
              CurveBound(
                  Handle(Geom2d_Curve)(new Geom2d_BezierCurve(exteriorPoles))),
              SurfaceBound(denominatorSurface, TopLoc_Location()), 0, 1, .01,
              32768, 0)
                  .status == "unavailable",
          "unresolved nearzero denominator remains unavailable");
    auto guardSurface = FixtureSurface();
    for (int u = 1; u <= 6; ++u)
      for (int v = 1; v <= 8; ++v) {
        guardSurface->SetPole(u, v, gp_Pnt(v == 5 ? 100 : 0, 0, 0));
        guardSurface->SetWeight(u, v, 1);
      }
    TColgp_Array1OfPnt guardPoles(1, 7);
    guardPoles.Init(gp_Pnt(0, 0, 0));
    guardPoles(6) = gp_Pnt(25. / 3., 0, 0);
    guardPoles(7) = gp_Pnt(25, 0, 0);
    TColStd_Array1OfReal guardKnots(1, 3);
    guardKnots(1) = 0;
    guardKnots(2) = .5;
    guardKnots(3) = 1;
    TColStd_Array1OfInteger guardMults(1, 3);
    guardMults(1) = guardMults(3) = 4;
    guardMults(2) = 3;
    const CurveBound guardSource(Handle(Geom_Curve)(new Geom_BSplineCurve(
                                     guardPoles, guardKnots, guardMults, 3)),
                                 TopLoc_Location());
    const auto guarded = BoundRationalComposition(
        guardSource, CurveBound(FixturePC(.5, 1.5)),
        SurfaceBound(guardSurface, TopLoc_Location()), 0, 1, .01);
    check(guarded.status == "bounded-within-budget" && guarded.subdivisions > 0,
          "nonactual candidate's rejecting center cannot reject the actual "
          "branch");
    const auto distant = BoundRationalComposition(
        c, CurveBound(FixturePC(20, 21)), s, 0, 1, .01);
    check(distant.status == "unavailable" &&
              distant.reason == "rational periodic local-domain resource limit",
          "distant periodic normalization is not silently admitted");
    check(BoundRationalComposition(c, CurveBound(FixturePC(-7, 11)), s, 0, 1,
                                   .01, 32768, 0)
                  .status == "unavailable",
          "more than16 periodic candidates cannot be silently trimmed");
    check(BoundRationalComposition(c, p, s, 0, 1, .01, 0).intervals == 0,
          "zero rational allowance cannot start work");
    int ticks = 0;
    const auto cancelled = BoundRationalComposition(
        c, p, s, 0, 1, .01, 32768, 32, [&]() { return ++ticks < 100; });
    check(cancelled.status == "unavailable" && cancelled.intervals > 0 &&
              cancelled.reason == "assessment-monotonic-time-limit",
          "rational callback interrupts homogeneous coefficient work");
    const CurveBound circle(
        Handle(Geom_Curve)(new Geom_Circle(gp_Circ(gp_Ax2(), 1))),
        TopLoc_Location());
    const auto sourceModel = RationalSourcePiece(
        circle, -.49, .49, {Interval(-.49), Interval(.98)}, CompositionWork{});
    Box polynomialEndpoint;
    for (int coordinate = 0; coordinate < 3; ++coordinate)
      for (const auto coefficient : sourceModel.polynomial[coordinate])
        polynomialEndpoint[coordinate] =
            polynomialEndpoint[coordinate] + coefficient;
    const auto actualEndpoint = circle.c3->Value(.49);
    bool tailContained = true;
    double omittedDifference = 0;
    for (int coordinate = 0; coordinate < 3; ++coordinate) {
      const auto enclosure =
          polynomialEndpoint[coordinate] + sourceModel.remainder[coordinate];
      tailContained =
          tailContained &&
          enclosure.lo <= actualEndpoint.Coord(coordinate + 1) + 1e-12 &&
          enclosure.hi >= actualEndpoint.Coord(coordinate + 1) - 1e-12;
      omittedDifference = std::max(
          omittedDifference, std::abs(actualEndpoint.Coord(coordinate + 1) -
                                      polynomialEndpoint[coordinate].lo));
    }
    check(tailContained && omittedDifference > 1e-5,
          "circle cubic model transfers all degree4..8 terms into explicit "
          "remainder");
    for (double invalidBudget :
         {0., -1., std::numeric_limits<double>::infinity(),
          std::numeric_limits<double>::quiet_NaN()})
      check(BoundRationalComposition(c, p, s, 0, 1, invalidBudget).intervals ==
                0,
            "invalid physical budget cannot start rational work");
    check(BoundRationalComposition(c, p, s, 1, 0, .01).intervals == 0,
          "reversed native domain cannot start rational work");
    const auto oneInterval = BoundRationalComposition(c, p, s, 0, 1, .01, 1);
    check(oneInterval.status == "unavailable" && oneInterval.intervals == 1,
          "last remaining interval is charged without exceeding allowance");
    auto amplifiedSurface = FixtureSurface();
    for (int u = 1; u <= 6; ++u)
      for (int v = 1; v <= 8; ++v)
        amplifiedSurface->SetPole(u, v, gp_Pnt((u - 1) * 2e14, 1, 0));
    TColgp_Array1OfPnt tinySourcePoles(1, 4);
    tinySourcePoles.Init(gp_Pnt(0, 1, 0));
    tinySourcePoles(4) = gp_Pnt(.1, 1, 0);
    const CurveBound tinySource(
        Handle(Geom_Curve)(new Geom_BezierCurve(tinySourcePoles)),
        TopLoc_Location());
    TColgp_Array1OfPnt2d tinyPCPoles(1, 4);
    tinyPCPoles.Init(gp_Pnt2d(0, .25));
    tinyPCPoles(4) = gp_Pnt2d(1e-16, .25);
    const SurfaceBound amplified(amplifiedSurface, TopLoc_Location());
    check(BoundRationalComposition(tinySource,
                                   CurveBound(Handle(Geom2d_Curve)(
                                       new Geom2d_BezierCurve(tinyPCPoles))),
                                   amplified, 0, 1, .01)
                  .status == "bounded-within-budget",
          "tiny native cubic U terms survive physical amplification");
    tinyPCPoles.Init(gp_Pnt2d(0, .25));
    check(BoundRationalComposition(tinySource,
                                   CurveBound(Handle(Geom2d_Curve)(
                                       new Geom2d_BezierCurve(tinyPCPoles))),
                                   amplified, 0, 1, .01)
                  .status == "exceeds-budget",
          "snapping the tiny native U term is a physical geometry change");
    TColStd_Array1OfReal spikeKnots(1, 4);
    spikeKnots(1) = 0;
    spikeKnots(2) = .5001229;
    spikeKnots(3) = .5001231;
    spikeKnots(4) = 1;
    TColStd_Array1OfInteger spikeMults(1, 4);
    spikeMults.Init(3);
    spikeMults(1) = spikeMults(4) = 4;
    TColgp_Array1OfPnt spikePoles(1, 10);
    for (int span = 1; span <= 3; ++span)
      for (int pole = 0; pole <= 3; ++pole)
        spikePoles((span - 1) * 3 + pole + 1) =
            gp_Pnt(spikeKnots(span) +
                       (spikeKnots(span + 1) - spikeKnots(span)) * pole / 3.,
                   1, 0);
    Handle(Geom_BSplineCurve) spike =
        new Geom_BSplineCurve(spikePoles, spikeKnots, spikeMults, 3);
    const CurveBound spikePC(FixturePC(.25, .25));
    check(BoundRationalComposition(CurveBound(spike, TopLoc_Location()),
                                   spikePC, s, 0, 1, .01)
                  .status == "bounded-within-budget",
          "generated narrow native knot span preserves unchanged geometry");
    auto spikePole = spike->Pole(5);
    spikePole.SetZ(.2);
    spike->SetPole(5, spikePole);
    bool ordinarySamplesMiss = true;
    for (double parameter : {0., .25, .5, .75, 1.})
      ordinarySamplesMiss =
          ordinarySamplesMiss && spike->Value(parameter).Z() == 0;
    check(ordinarySamplesMiss &&
              BoundRationalComposition(CurveBound(spike, TopLoc_Location()),
                                       spikePC, s, 0, 1, .01)
                      .status == "exceeds-budget",
          "whole native spans reject an interior spike missed by sample nodes");
    std::cout << "PASS: " << checks << " rational composition checks\n";
    return 0;
  } catch (const Standard_Failure &error) {
    std::cerr << error.GetMessageString() << '\n';
  } catch (const std::exception &error) {
    std::cerr << error.what() << '\n';
  }
  return 1;
}
