#include "kernel-repair-high-axis-composition.hpp"
#include <Geom2d_Line.hxx>
#include <GeomTools.hxx>
#include <Geom_Circle.hxx>
#include <Geom_Ellipse.hxx>
#include <TColStd_Array2OfReal.hxx>
#include <TColgp_Array2OfPnt.hxx>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <stdexcept>
using namespace MalievRepair;
static Handle(Geom_BSplineSurface) GeneratedSupport(double scale, bool circle) {
  TColgp_Array2OfPnt poles(1, 3, 1, 7);
  TColStd_Array2OfReal weights(1, 3, 1, 7);
  for (int u = 1; u <= 3; ++u)
    for (int v = 1; v <= 7; ++v) {
      poles(u, v) =
          circle ? gp_Pnt(u == 3 ? 0 : 1, u == 1 ? 0 : 1, scale * (v - 1) / 6.)
                 : gp_Pnt((u - 1) / 2., 0, scale * (v - 1) / 6.);
      weights(u, v) = circle && u == 2 ? std::sqrt(.5) : 1;
    }
  TColStd_Array1OfReal knots(1, 2);
  knots(1) = 0;
  knots(2) = 1;
  TColStd_Array1OfInteger uMults(1, 2), vMults(1, 2);
  uMults.Init(3);
  vMults.Init(7);
  return new Geom_BSplineSurface(poles, weights, knots, knots, uMults, vMults,
                                 2, 6);
}
int main(int argc, char **argv) {
  int checks = 0;
  auto check = [&](bool value, const char *name) {
    ++checks;
    if (!value)
      throw std::runtime_error(name);
  };
  try {
    for (int fixture = 1; fixture < argc; ++fixture) {
      std::ifstream stream(argv[fixture]);
      if (!stream)
        throw std::runtime_error("private high-axis fixture unavailable");
      auto identity = [&]() {
        for (int row = 0; row < 3; ++row)
          for (int column = 0; column < 4; ++column) {
            double value;
            if (!(stream >> value) || value != (row == column ? 1. : 0.))
              throw std::runtime_error(
                  "private high-axis placement not identity");
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
        throw std::runtime_error("private high-axis range unavailable");
      const auto start = std::chrono::steady_clock::now();
      const auto result = BoundHighAxisRationalComposition(
          CurveBound(source, TopLoc_Location()), CurveBound(pcurve),
          SurfaceBound(surface, TopLoc_Location()), first, last, .01);
      std::cout << std::setprecision(17) << "private " << fixture << " "
                << result.status << " " << result.reason
                << " upper=" << result.upper
                << " intervals=" << result.intervals
                << " subdivisions=" << result.subdivisions << " ms="
                << std::chrono::duration<double, std::milli>(
                       std::chrono::steady_clock::now() - start)
                       .count()
                << '\n';
      check(result.status == "bounded-within-budget",
            "private high-axis positive not bounded");
      check(result.intervals < 4096,
            "private high-axis path completes with bounded work");
      gp_Trsf move;
      move.SetTranslation(gp_Vec(0, 0, .02));
      check(BoundHighAxisRationalComposition(
                CurveBound(source, TopLoc_Location(move)), CurveBound(pcurve),
                SurfaceBound(surface, TopLoc_Location()), first, last, .01)
                    .status == "exceeds-budget",
            "private source displacement rejects");
      const auto again = BoundHighAxisRationalComposition(
          CurveBound(source, TopLoc_Location()), CurveBound(pcurve),
          SurfaceBound(surface, TopLoc_Location()), first, last, .01);
      check(again.status == result.status && again.upper == result.upper &&
                again.lower == result.lower && again.reason == result.reason &&
                again.intervals == result.intervals &&
                again.subdivisions == result.subdivisions,
            "private complete result and charged work are deterministic");
      check(BoundHighAxisRationalComposition(
                CurveBound(source, TopLoc_Location()), CurveBound(pcurve),
                SurfaceBound(surface, TopLoc_Location()), Down(first), Up(last),
                .01)
                    .status == "bounded-within-budget",
            "private native endpoint excursions remain represented");
      bool nativeContained = true;
      std::vector<double> samples{first, last, first + (last - first) * .5};
      const CurveBound nativeSource(source, TopLoc_Location());
      const CurveBound nativePC(pcurve);
      for (const auto *spline : {&nativeSource.spline, &nativePC.spline})
        for (const auto &knot : spline->knots)
          if (knot.lo > first && knot.lo < last)
            samples.push_back(knot.lo);
      for (const double parameter : samples) {
        const auto uv = pcurve->Value(parameter);
        nativeContained =
            nativeContained &&
            source->Value(parameter).Distance(surface->Value(uv.X(), uv.Y())) <=
                result.upper + 1e-8;
      }
      check(nativeContained,
            "native endpoint and knot D0 diagnostics lie within bound");
      gp_Trsf placed;
      placed.SetRotation(gp_Ax1(gp_Pnt(0, 0, 0), gp_Dir(0, 0, 1)), .4);
      placed.SetTranslationPart(gp_Vec(10, -4, 2));
      check(BoundHighAxisRationalComposition(
                CurveBound(source, TopLoc_Location(placed)), nativePC,
                SurfaceBound(surface, TopLoc_Location(placed)), first, last,
                .01)
                    .status == "bounded-within-budget",
            "placed source and corridor transform consistently");
    }
    const CurveBound circle(
        Handle(Geom_Curve)(new Geom_Circle(gp_Circ(gp_Ax2(), 1))),
        TopLoc_Location());
    TColgp_Array1OfPnt2d uvPoles(1, 4);
    for (int index = 1; index <= 4; ++index)
      uvPoles(index) = gp_Pnt2d(.001 * std::sqrt(.5) * (index - 1) / 3.,
                                index == 2 || index == 3 ? 1e-16 : 0);
    TColStd_Array1OfReal pcKnots(1, 2);
    pcKnots(1) = 0;
    pcKnots(2) = .001;
    TColStd_Array1OfInteger pcMults(1, 2);
    pcMults.Init(4);
    const CurveBound tinyV(Handle(Geom2d_Curve)(
        new Geom2d_BSplineCurve(uvPoles, pcKnots, pcMults, 3)));
    const SurfaceBound amplified(GeneratedSupport(1e15, true),
                                 TopLoc_Location());
    check(BoundHighAxisRationalComposition(circle, tinyV, amplified, 0, .001,
                                           .01, 32768, 0)
                  .status != "bounded-within-budget",
          "complete tiny-V corridor prevents endpoint-model false acceptance");
    check(
        BoundHighAxisRationalComposition(circle, tinyV, amplified, 0, .001, .01)
                .status == "exceeds-budget",
        "amplified tiny-V interior geometry is detected");
    for (int index = 1; index <= 4; ++index)
      uvPoles(index).SetY(0);
    const CurveBound snapped(Handle(Geom2d_Curve)(
        new Geom2d_BSplineCurve(uvPoles, pcKnots, pcMults, 3)));
    check(BoundHighAxisRationalComposition(circle, snapped, amplified, 0, .001,
                                           .01)
                  .status == "bounded-within-budget",
          "dropping the V remainder demonstrably changes acceptance");
    for (int allowance : {0, 1, 2}) {
      const auto limited = BoundHighAxisRationalComposition(
          circle, tinyV, amplified, 0, .001, .01, allowance);
      check(limited.status == "unavailable" && limited.intervals <= allowance,
            "corridor and nominal work cannot exceed remaining allowance");
    }
    int ticks = 0;
    const auto cancelled = BoundHighAxisRationalComposition(
        circle, tinyV, amplified, 0, .001, .01, 32768, 32,
        [&]() { return ++ticks < 100; });
    check(cancelled.status == "unavailable" &&
              cancelled.reason == "assessment-monotonic-time-limit",
          "high-axis work obeys common continuation callback");
    auto unsafeNative = GeneratedSupport(1, false);
    for (int v = 1; v <= 7; ++v)
      unsafeNative->SetWeight(3, v, .01);
    uvPoles.Init(gp_Pnt2d(1.1, 0));
    check(BoundHighAxisRationalComposition(
              circle,
              CurveBound(Handle(Geom2d_Curve)(
                  new Geom2d_BSplineCurve(uvPoles, pcKnots, pcMults, 3))),
              SurfaceBound(unsafeNative, TopLoc_Location()), 0, .001, .01,
              32768, 0)
                  .status == "unavailable",
          "positive native weights do not clamp exterior corridor denominator");
    const CurveBound ellipse(
        Handle(Geom_Curve)(new Geom_Ellipse(gp_Elips(gp_Ax2(), 1000, 2))),
        TopLoc_Location());
    const auto ellipseModel = HighAxisSourcePiece(
        ellipse, -.49, .49, {Interval(-.49), Interval(.98)}, CompositionWork{});
    const auto ellipseEnd = ellipse.c3->Value(.49);
    bool tailContained = true;
    double discardedError = 0;
    for (int coordinate = 0; coordinate < 3; ++coordinate) {
      Interval endpoint(0);
      for (const auto &coefficient : ellipseModel.polynomial[coordinate])
        endpoint = endpoint + coefficient;
      const auto enclosed = endpoint + ellipseModel.remainder[coordinate];
      tailContained = tailContained &&
                      enclosed.lo <= ellipseEnd.Coord(coordinate + 1) + 1e-9 &&
                      enclosed.hi >= ellipseEnd.Coord(coordinate + 1) - 1e-9;
      discardedError =
          std::max(discardedError,
                   std::abs(endpoint.lo - ellipseEnd.Coord(coordinate + 1)));
    }
    check(tailContained && discardedError > .01,
          "ellipse anisotropic radii retain omitted polynomial tail beyond "
          "budget");
    TColStd_Array1OfReal spikeKnots(1, 4);
    spikeKnots(1) = 0;
    spikeKnots(2) = .5001229;
    spikeKnots(3) = .5001231;
    spikeKnots(4) = 1;
    TColStd_Array1OfInteger spikeMults(1, 4);
    spikeMults.Init(6);
    spikeMults(1) = spikeMults(4) = 7;
    TColgp_Array1OfPnt spikePoles(1, 19);
    for (int span = 1; span <= 3; ++span)
      for (int pole = 0; pole <= 6; ++pole)
        spikePoles((span - 1) * 6 + pole + 1) =
            gp_Pnt(spikeKnots(span) +
                       (spikeKnots(span + 1) - spikeKnots(span)) * pole / 6.,
                   0, 0);
    Handle(Geom_BSplineCurve) spike =
        new Geom_BSplineCurve(spikePoles, spikeKnots, spikeMults, 6);
    const CurveBound linePC(
        Handle(Geom2d_Curve)(new Geom2d_Line(gp_Pnt2d(0, 0), gp_Dir2d(1, 0))));
    const SurfaceBound planeLike(GeneratedSupport(1, false), TopLoc_Location());
    TColgp_Array1OfPnt zeroPoles(1, 7);
    zeroPoles.Init(gp_Pnt(0, 0, 0));
    const CurveBound zeroSource(
        Handle(Geom_Curve)(new Geom_BezierCurve(zeroPoles)), TopLoc_Location());
    const CurveBound exteriorLine(Handle(Geom2d_Curve)(
        new Geom2d_Line(gp_Pnt2d(1.1, 0), gp_Dir2d(0, 1))));
    check(BoundHighAxisRationalComposition(
              zeroSource, exteriorLine,
              SurfaceBound(unsafeNative, TopLoc_Location()), 0, .001, .01,
              32768, 0)
                  .status == "unavailable",
          "exact source lane also rejects a negative raw nominal guard "
          "denominator");
    auto periodicSupport = GeneratedSupport(0, false);
    periodicSupport->SetVPeriodic();
    const auto unsupportedPeriodic = BoundHighAxisRationalComposition(
        circle, snapped, SurfaceBound(periodicSupport, TopLoc_Location()), 0,
        .001, .01);
    check(unsupportedPeriodic.status == "unavailable" &&
              unsupportedPeriodic.intervals == 0,
          "high-axis lane never widens the approved nonperiodic class");
    check(BoundHighAxisRationalComposition(CurveBound(spike, TopLoc_Location()),
                                           linePC, planeLike, 0, 1, .01)
                  .status == "bounded-within-budget",
          "degree6 source with exact affine PC retains every narrow knot span");
    auto changedPole = spike->Pole(8);
    changedPole.SetZ(.2);
    spike->SetPole(8, changedPole);
    bool samplesMiss = true;
    for (double parameter : {0., .25, .5, .75, 1.})
      samplesMiss = samplesMiss && spike->Value(parameter).Z() == 0;
    check(samplesMiss && BoundHighAxisRationalComposition(
                             CurveBound(spike, TopLoc_Location()), linePC,
                             planeLike, 0, 1, .01)
                                 .status == "exceeds-budget",
          "whole high-degree source catches a narrow spike missed by sample "
          "nodes");
    for (double invalidBudget :
         {0., -1., std::numeric_limits<double>::infinity(),
          std::numeric_limits<double>::quiet_NaN()})
      check(BoundHighAxisRationalComposition(circle, tinyV, amplified, 0, .001,
                                             invalidBudget)
                    .intervals == 0,
            "invalid high-axis physical policy performs zero work");
    std::cout << "PASS: " << checks << " high-axis composition checks\n";
    return 0;
  } catch (const Standard_Failure &error) {
    std::cerr << error.GetMessageString() << '\n';
  } catch (const std::exception &error) {
    std::cerr << error.what() << '\n';
  }
  return 1;
}
