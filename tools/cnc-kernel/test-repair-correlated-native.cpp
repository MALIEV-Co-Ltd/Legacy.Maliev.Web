#include "kernel-repair-correlated.hpp"
#include <Geom2d_Line.hxx>
#include <Geom_Circle.hxx>
#include <Geom_CylindricalSurface.hxx>
#include <Geom_Line.hxx>
#include <Geom_Plane.hxx>
#include <TColStd_Array2OfReal.hxx>
#include <TColgp_Array1OfPnt.hxx>
#include <TColgp_Array1OfPnt2d.hxx>
#include <TColgp_Array2OfPnt.hxx>
#include <iostream>
#include <stdexcept>
using namespace MalievRepair;
static int checks = 0;
static void Check(bool ok, const char *message) {
  ++checks;
  if (!ok)
    throw std::runtime_error(message);
}
static void Positive(const Residual &r, int intervals, const char *message) {
  if (r.status != "bounded-within-budget")
    std::cerr << message << ": " << r.status << ' ' << r.reason
              << " visited=" << r.intervals << '\n';
  Check(r.status == "bounded-within-budget" && r.intervals <= intervals,
        message);
}
int main() {
  try {
    Handle(Geom_Curve) line = new Geom_Line(gp_Pnt(0, 0, 0), gp_Dir(1, 0, 0));
    Handle(Geom2d_Curve) pc = new Geom2d_Line(gp_Pnt2d(0, 0), gp_Dir2d(1, 0));
    Handle(Geom_Surface) plane =
        new Geom_Plane(gp_Pln(gp_Pnt(), gp_Dir(0, 0, 1)));
    CurveBound c(line, TopLoc_Location()), p(pc);
    SurfaceBound s(plane, TopLoc_Location());
    int centered = 0;
    Positive(BoundCorrelatedResidual(c, p, s, 0, 10000, .01, &centered), 1,
             "high-speed equal line centered positive");
    Check(centered == 1, "centered path used");
    gp_Trsf transform;
    transform.SetRotation(gp_Ax1(gp_Pnt(), gp_Dir(0, 0, 1)), .7);
    transform.SetTranslationPart(gp_Vec(100, -200, 300));
    Positive(
        BoundCorrelatedResidual(CurveBound(line, TopLoc_Location(transform)), p,
                                SurfaceBound(plane, TopLoc_Location(transform)),
                                0, 10000, .01),
        1,
        "matching nonidentity placements and derivative translation exclusion");
    gp_Trsf shift;
    shift.SetTranslation(gp_Vec(0, 0, .002));
    Positive(BoundCorrelatedResidual(CurveBound(line, TopLoc_Location(shift)),
                                     p, s, 0, 10000, .01),
             1, "nonzero center residual retained below budget");
    shift.SetTranslation(gp_Vec(0, 0, .02));
    const auto rejected = BoundCorrelatedResidual(
        CurveBound(line, TopLoc_Location(shift)), p, s, 0, 10000, .01);
    Check(rejected.upper >= rejected.lower && rejected.lower > .01,
          "negative residual retains consistent upper and lower bounds");
    Check(BoundCorrelatedResidual(CurveBound(line, TopLoc_Location(shift)), p,
                                  s, 0, 10000, .01)
                  .status == "exceeds-budget",
          "nonzero center residual rejects above budget");
    Handle(Geom_Curve) circle =
        new Geom_Circle(gp_Circ(gp_Ax2(gp_Pnt(), gp_Dir(0, 0, 1)), 1));
    Handle(Geom_Surface) cylinder =
        new Geom_CylindricalSurface(gp_Ax3(gp_Pnt(), gp_Dir(0, 0, 1)), 1);
    auto circular = BoundCorrelatedResidual(
        CurveBound(circle, TopLoc_Location()), p,
        SurfaceBound(cylinder, TopLoc_Location()), 0, 6.283185307179586, .01);
    Positive(circular, 4096, "circle-cylinder centered positive");
    TColgp_Array1OfPnt quarter(1, 4);
    const double q = .5522847498307936;
    quarter(1) = gp_Pnt(1, 0, 0);
    quarter(2) = gp_Pnt(1, q, 0);
    quarter(3) = gp_Pnt(q, 1, 0);
    quarter(4) = gp_Pnt(0, 1, 0);
    Handle(Geom_Curve) approx = new Geom_BezierCurve(quarter);
    TColgp_Array1OfPnt2d angular(1, 2);
    angular(1) = gp_Pnt2d(0, 0);
    angular(2) = gp_Pnt2d(1.5707963267948966, 0);
    Handle(Geom2d_Curve) angle = new Geom2d_BezierCurve(angular);
    auto curved = BoundCorrelatedResidual(
        CurveBound(approx, TopLoc_Location()), CurveBound(angle),
        SurfaceBound(cylinder, TopLoc_Location()), 0, 1, .01);
    Positive(curved, 4096, "curved polynomial on cylinder bounded positive");
    TColgp_Array1OfPnt rp(1, 3);
    TColgp_Array1OfPnt2d pp(1, 3);
    TColStd_Array1OfReal weights(1, 3);
    weights(1) = 1;
    weights(2) = .4;
    weights(3) = 1.2;
    rp(1) = gp_Pnt(0, 0, 0);
    rp(2) = gp_Pnt(.4, .7, 0);
    rp(3) = gp_Pnt(1, 0, 0);
    for (int i = 1; i <= 3; ++i)
      pp(i) = gp_Pnt2d(rp(i).X(), rp(i).Y());
    Handle(Geom_Curve) rational = new Geom_BezierCurve(rp, weights);
    Handle(Geom2d_Curve) rationalPc = new Geom2d_BezierCurve(pp, weights);
    Positive(BoundCorrelatedResidual(CurveBound(rational, TopLoc_Location()),
                                     CurveBound(rationalPc), s, 0, 1, .01),
             4096, "unequal-weight rational curve derivative positive");
    TColgp_Array2OfPnt net(1, 2, 1, 2);
    TColStd_Array2OfReal sw(1, 2, 1, 2);
    for (int i = 1; i <= 2; ++i)
      for (int j = 1; j <= 2; ++j) {
        net(i, j) = gp_Pnt(i - 1, j - 1, 0);
        sw(i, j) = i == 1 ? 1 : .5;
      }
    TColStd_Array1OfReal knots(1, 2);
    knots(1) = 0;
    knots(2) = 1;
    TColStd_Array1OfInteger mult(1, 2);
    mult.Init(2);
    Handle(Geom_Surface) rationalSurface =
        new Geom_BSplineSurface(net, sw, knots, knots, mult, mult, 1, 1);
    TColgp_Array1OfPnt planePoles(1, 2);
    planePoles(1) = gp_Pnt(0, .5, 0);
    planePoles(2) = gp_Pnt(1, .5, 0);
    TColStd_Array1OfReal planeWeights(1, 2);
    planeWeights(1) = 1;
    planeWeights(2) = .5;
    Handle(Geom_Curve) surfaceCurve =
        new Geom_BSplineCurve(planePoles, planeWeights, knots, mult, 1);
    Handle(Geom2d_Curve) surfacePc =
        new Geom2d_Line(gp_Pnt2d(0, .5), gp_Dir2d(1, 0));
    Positive(BoundCorrelatedResidual(
                 CurveBound(surfaceCurve, TopLoc_Location()),
                 CurveBound(surfacePc),
                 SurfaceBound(rationalSurface, TopLoc_Location()), .1, .9, .01),
             4096, "rational spline-support composed jet positive");
    // Bilinear S(u,v)=(u,v,u*v), with u=v=t: both tensor stages vary.
    for (int i = 1; i <= 2; ++i)
      for (int j = 1; j <= 2; ++j)
        net(i, j) = gp_Pnt(i - 1, j - 1, (i - 1) * (j - 1));
    Handle(Geom_Surface) tensorSurface =
        new Geom_BSplineSurface(net, knots, knots, mult, mult, 1, 1);
    TColgp_Array1OfPnt tensorPoles(1, 3);
    tensorPoles(1) = gp_Pnt(0, 0, 0);
    tensorPoles(2) = gp_Pnt(.5, .5, 0);
    tensorPoles(3) = gp_Pnt(1, 1, 1);
    Handle(Geom_Curve) tensorCurve = new Geom_BezierCurve(tensorPoles);
    TColgp_Array1OfPnt2d tensorUV(1, 2);
    tensorUV(1) = gp_Pnt2d(0, 0);
    tensorUV(2) = gp_Pnt2d(1, 1);
    Handle(Geom2d_Curve) tensorPC = new Geom2d_BezierCurve(tensorUV);
    Positive(BoundCorrelatedResidual(
                 CurveBound(tensorCurve, TopLoc_Location()),
                 CurveBound(tensorPC),
                 SurfaceBound(tensorSurface, TopLoc_Location()), .1, .9, .01),
             4096, "both U and V tensor derivative contributions");
    bool rejectedCenter = false;
    try {
      CenteredResidual(c, p, s, 1, std::nextafter(1.0, 2.0));
    } catch (const Standard_Failure &) {
      rejectedCenter = true;
    }
    Check(rejectedCenter, "adjacent endpoint center excludes centered path");
    CorrelatedDiagnostics diagnostics;
    BoundCorrelatedResidual(c, p, s, 1, std::nextafter(1.0, 2.0), 1e-30,
                            nullptr, &diagnostics);
    Check(diagnostics.centeredAttempts == 1 &&
              diagnostics.fallbackReasons
                      ["no strictly interior representable center"] == 1 &&
              diagnostics.elapsedWallMilliseconds >= 0 &&
              diagnostics.elapsedCpuMilliseconds >= 0,
          "fallback reason and elapsed diagnostics retained");
    TColgp_Array1OfPnt spikePoles(1, 5);
    spikePoles(1) = gp_Pnt(0, 0, 0);
    spikePoles(2) = gp_Pnt(.5012, 0, 0);
    spikePoles(3) = gp_Pnt(.5013, 0, .1);
    spikePoles(4) = gp_Pnt(.5014, 0, 0);
    spikePoles(5) = gp_Pnt(1, 0, 0);
    TColStd_Array1OfReal sk(1, 5);
    sk(1) = 0;
    sk(2) = .5012;
    sk(3) = .5013;
    sk(4) = .5014;
    sk(5) = 1;
    TColStd_Array1OfInteger sm(1, 5);
    sm.Init(1);
    sm(1) = sm(5) = 2;
    Handle(Geom_Curve) spike = new Geom_BSplineCurve(spikePoles, sk, sm, 1);
    Check(BoundCorrelatedResidual(CurveBound(spike, TopLoc_Location()), p, s, 0,
                                  1, .01)
                  .status == "exceeds-budget",
          "centered method preserves narrow spike rejection");
    for (int i = 1; i <= 5; ++i) {
      spikePoles(i).SetZ(0);
      spikePoles(i).SetY(i == 3 ? .1 : 0);
    }
    Handle(Geom_Curve) c0 = new Geom_BSplineCurve(spikePoles, sk, sm, 1);
    TColgp_Array1OfPnt2d c0p(1, 5);
    for (int i = 1; i <= 5; ++i)
      c0p(i) = gp_Pnt2d(spikePoles(i).X(), spikePoles(i).Y());
    Handle(Geom2d_Curve) c0pc = new Geom2d_BSplineCurve(c0p, sk, sm, 1);
    Positive(BoundCorrelatedResidual(CurveBound(c0, TopLoc_Location()),
                                     CurveBound(c0pc), s, 0, 1, .01),
             4096, "C0 one-sided derivative knot crossing");
    Check(BoundCorrelatedResidual(c, p, s, 0, 1,
                                  std::numeric_limits<double>::infinity())
                  .status == "unavailable",
          "nonfinite policy rejected");
    Check(BoundCorrelatedResidual(CurveBound(spike, TopLoc_Location()), p, s,
                                  -.1, 1, .01)
                  .status == "unavailable",
          "outside basis rejected");
    const auto exhausted = BoundCorrelatedResidual(c, p, s, 0, 1, 1e-30);
    Check(exhausted.status == "unavailable" && exhausted.intervals <= 32769,
          "resource exhaustion remains unavailable");
    Spline discontinuous;
    discontinuous.degree = 1;
    discontinuous.first = 0;
    discontinuous.last = 1;
    discontinuous.knots = {Interval(0),  Interval(0), Interval(.5),
                           Interval(.5), Interval(1), Interval(1)};
    bool rejectedJump = false;
    try {
      RequireContinuous(discontinuous, Interval(0, 1));
    } catch (const Standard_Failure &) {
      rejectedJump = true;
    }
    Check(rejectedJump, "discontinuous knot excludes centered theorem");
    TColgp_Array1OfPnt translatedPoles(1, 3);
    translatedPoles(1) = gp_Pnt(10000, 0, 0);
    translatedPoles(2) = gp_Pnt(10000, 0, 0);
    translatedPoles(3) = gp_Pnt(10001, 0, 0);
    Handle(Geom_Curve) translatedQuadratic =
        new Geom_BezierCurve(translatedPoles);
    const auto tightJet = CurveJet(
        CurveBound(translatedQuadratic, TopLoc_Location()), Interval(.4, .6));
    Check(tightJet[0].derivative.lo <= .8 && tightJet[0].derivative.hi >= 1.2 &&
              tightJet[0].derivative.hi - tightJet[0].derivative.lo < .401,
          "native derivative coefficient differences cancel large translation");
    TColgp_Array1OfPnt2d translatedUV(1, 3);
    for (int i = 1; i <= 3; ++i)
      translatedUV(i) =
          gp_Pnt2d(translatedPoles(i).X(), translatedPoles(i).Y());
    Handle(Geom2d_Curve) translatedPC = new Geom2d_BezierCurve(translatedUV);
    const auto translatedCost = BoundCorrelatedResidual(
        CurveBound(translatedQuadratic, TopLoc_Location()),
        CurveBound(translatedPC), s, .1, .9, .01);
    Positive(translatedCost, 63,
             "translated spline residual deterministic cost bound");
    const CurveBound rationalBound(rational, TopLoc_Location());
    const auto rationalJet = CurveJet(rationalBound, Interval(.4, .6));
    for (const double parameter : {.4, .5, .6}) {
      gp_Pnt nativePoint;
      gp_Vec nativeDerivative;
      rational->D1(parameter, nativePoint, nativeDerivative);
      bool enclosed = true;
      for (int axis = 0; axis < 3; ++axis)
        enclosed =
            enclosed &&
            rationalJet[axis].derivative.lo <=
                nativeDerivative.Coord(axis + 1) &&
            rationalJet[axis].derivative.hi >= nativeDerivative.Coord(axis + 1);
      Check(enclosed, "rational homogeneous derivative native D1 regression");
    }
    std::cout << "PASS: " << checks
              << " correlated native checks; circle intervals="
              << circular.intervals
              << ", curved cylinder intervals=" << curved.intervals << '\n';
    return 0;
  } catch (const std::exception &e) {
    std::cerr << e.what() << '\n';
    return 1;
  } catch (const Standard_Failure &e) {
    std::cerr << e.GetMessageString() << '\n';
    return 1;
  }
}
