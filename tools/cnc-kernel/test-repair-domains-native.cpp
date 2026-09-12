#include "kernel-repair-domains.hpp"
#include <TColStd_Array2OfReal.hxx>
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
static bool Contains(const Box &box, const gp_Pnt &point) {
  for (int i = 0; i < 3; ++i)
    if (point.Coord(i + 1) < box[i].lo || point.Coord(i + 1) > box[i].hi)
      return false;
  return true;
}
int main() {
  try {
    TColStd_Array1OfReal knots(1, 2);
    knots(1) = 0;
    knots(2) = 1;
    TColStd_Array1OfInteger mult(1, 2);
    mult.Init(2);
    TColgp_Array2OfPnt net(1, 2, 1, 2);
    for (int i = 1; i <= 2; ++i)
      for (int j = 1; j <= 2; ++j)
        net(i, j) = gp_Pnt(i - 1, j - 1, (i - 1) * (j - 1));
    Handle(Geom_BSplineSurface) surface =
        new Geom_BSplineSurface(net, knots, knots, mult, mult, 1, 1);
    RepresentedSurfaceBound bound(surface, TopLoc_Location());
    for (double u : {-.01, 1.01})
      for (double v : {-.01, 1.01}) {
        const auto box = bound.Bounds(Interval(u), Interval(v));
        Check(Contains(box, gp_Pnt(u, v, u * v)),
              "tensor exterior analytic corner");
        gp_Pnt native;
        surface->D0(u, v, native);
        Check(Contains(box, native), "tensor native exterior D0");
      }
    TColStd_Array2OfReal weights(1, 2, 1, 2);
    for (int i = 1; i <= 2; ++i)
      for (int j = 1; j <= 2; ++j)
        weights(i, j) = i;
    Handle(Geom_Surface) rational =
        new Geom_BSplineSurface(net, weights, knots, knots, mult, mult, 1, 1);
    RepresentedSurfaceBound rb(rational, TopLoc_Location());
    Check(Contains(rb.Bounds(Interval(-.01), Interval(.5)),
                   gp_Pnt(-.02 / .99, .5, -.01 / .99)),
          "rational exterior positive denominator");
    bool rejected = false;
    try {
      rb.Bounds(Interval(-1.1, -.9), Interval(.5));
    } catch (const Standard_Failure &) {
      rejected = true;
    }
    Check(rejected, "tensor exterior zero denominator unavailable");
    TColgp_Array2OfPnt periodicNet(1, 2, 1, 4);
    TColStd_Array2OfReal periodicWeights(1, 2, 1, 4);
    for (int i = 1; i <= 2; ++i) {
      periodicNet(i, 1) = gp_Pnt(1, 0, i);
      periodicNet(i, 2) = gp_Pnt(0, 1, i);
      periodicNet(i, 3) = gp_Pnt(-1, 0, i);
      periodicNet(i, 4) = gp_Pnt(0, -1, i);
      for (int j = 1; j <= 4; ++j)
        periodicWeights(i, j) = j % 2 ? 1 : .5;
    }
    TColStd_Array1OfReal vk(1, 5);
    TColStd_Array1OfInteger vm(1, 5);
    vm.Init(1);
    for (int i = 1; i <= 5; ++i)
      vk(i) = i - 1;
    Handle(Geom_BSplineSurface) periodic = new Geom_BSplineSurface(
        periodicNet, periodicWeights, knots, vk, mult, vm, 1, 1, false, true);
    RepresentedSurfaceBound pb(periodic, TopLoc_Location());
    for (double parameter : {-.01, .01, 3.99, 4.01}) {
      gp_Pnt native;
      periodic->D0(.5, parameter, native);
      Check(Contains(pb.Bounds(Interval(.5), Interval(parameter)), native),
            "native rational periodic seam wrapping");
    }
    const auto seam = pb.Bounds(Interval(.5), Interval(3.99, 4.01));
    gp_Pnt native;
    periodic->D0(.5, 4, native);
    Check(Contains(seam, native), "periodic split seam includes endpoint");
    pb.continueAssessment = []() { return false; };
    rejected = false;
    try {
      pb.Bounds(Interval(.5), Interval(.5));
    } catch (const Standard_Failure &failure) {
      rejected = std::string(failure.GetMessageString()) ==
                 "assessment-monotonic-time-limit";
    }
    Check(rejected, "surface tensor watchdog checked within cell loop");
    CorrelatedDiagnostics diagnostics;
    diagnostics.continueAssessment = []() { return false; };
    TColgp_Array1OfPnt poles(1, 2);
    poles(1) = gp_Pnt();
    poles(2) = gp_Pnt(1, 0, 0);
    TColgp_Array1OfPnt2d uv(1, 2);
    uv(1) = gp_Pnt2d();
    uv(2) = gp_Pnt2d(1, 0);
    Handle(Geom_Curve) curve = new Geom_BSplineCurve(poles, knots, mult, 1);
    Handle(Geom2d_Curve) pc = new Geom2d_BSplineCurve(uv, knots, mult, 1);
    const auto timed = BoundCorrelatedResidual(
        RepresentedCurveBound(curve, TopLoc_Location()),
        RepresentedCurveBound(pc), bound, 0, 1, .01, nullptr, &diagnostics);
    Check(timed.status == "unavailable" &&
              timed.reason == "assessment-monotonic-time-limit",
          "subdivision watchdog unavailable status");
    const auto extended = BoundCorrelatedResidual(
        RepresentedCurveBound(curve, TopLoc_Location()),
        RepresentedCurveBound(pc), bound, -1e-12, 1 + 1e-12, .01);
    if (extended.status != "bounded-within-budget")
      std::cerr << extended.status << ":" << extended.reason
                << " intervals=" << extended.intervals << '\n';
    Check(extended.status == "bounded-within-budget" && extended.intervals <= 3,
          "full represented residual includes exterior curve surface slivers");
    pb.continueAssessment = {};
    Handle(Geom_Curve) iso = periodic->VIso(.1);
    uv(1) = gp_Pnt2d(0, 4.1);
    uv(2) = gp_Pnt2d(1, 4.1);
    Handle(Geom2d_Curve) wrappedPC =
        new Geom2d_BSplineCurve(uv, knots, mult, 1);
    const auto wrappedResidual = BoundCorrelatedResidual(
        RepresentedCurveBound(iso, TopLoc_Location()),
        RepresentedCurveBound(wrappedPC), pb, .1, .9, .01);
    Check(wrappedResidual.status == "bounded-within-budget" &&
              wrappedResidual.intervals <= 127,
          "full periodic wrapped lifted residual positive");
    gp_Trsf shift;
    shift.SetTranslation(gp_Vec(0, 0, 1));
    const auto changedResidual = BoundCorrelatedResidual(
        RepresentedCurveBound(iso, TopLoc_Location(shift)),
        RepresentedCurveBound(wrappedPC), pb, .1, .9, .01);
    Check(changedResidual.status == "exceeds-budget",
          "geometrically changed periodic lift rejected");
    rejected = false;
    try {
      pb.Bounds(Interval(.5), Interval(100));
    } catch (const Standard_Failure &) {
      rejected = true;
    }
    Check(rejected, "nonlocal periodic interval unavailable");
    TColgp_Array2OfPnt uNet(1, 4, 1, 2);
    TColStd_Array2OfReal uWeights(1, 4, 1, 2);
    for (int i = 1; i <= 4; ++i)
      for (int j = 1; j <= 2; ++j) {
        uNet(i, j) = periodicNet(j, i);
        uWeights(i, j) = periodicWeights(j, i);
      }
    TColStd_Array1OfReal uk(1, 5);
    for (int i = 1; i <= 5; ++i)
      uk(i) = i + 9;
    Handle(Geom_BSplineSurface) uPeriodic = new Geom_BSplineSurface(
        uNet, uWeights, uk, knots, vm, mult, 2, 1, true, false);
    RepresentedSurfaceBound upb(uPeriodic, TopLoc_Location());
    for (double parameter : {9.99, 10.01, 13.99, 14.01}) {
      gp_Pnt point;
      uPeriodic->D0(parameter, .5, point);
      Check(Contains(upb.Bounds(Interval(parameter), Interval(.5)), point),
            "quadratic U periodic nonzero-origin native seam regression");
      gp_Vec du, dv;
      uPeriodic->D1(parameter, .5, point, du, dv);
      const auto derivative = SurfaceJet(
          upb, Jet(Interval(parameter), Interval(1)), Jet(Interval(.5)));
      bool enclosed = true;
      for (int axis = 0; axis < 3; ++axis)
        enclosed = enclosed &&
                   derivative[axis].derivative.lo <= du.Coord(axis + 1) &&
                   derivative[axis].derivative.hi >= du.Coord(axis + 1);
      Check(enclosed, "native periodic D1 on both seam sides");
    }
#ifdef __EMSCRIPTEN__
    Check(!diagnostics.processCpuAvailable,
          "Emscripten process CPU measurement unavailable");
#endif
    const auto rationalJet = SurfaceJet(rb, Jet(Interval(-.01), Interval(1)),
                                        Jet(Interval(.5), Interval(1)));
    gp_Pnt rationalPoint;
    gp_Vec du, dv;
    rational->D1(-.01, .5, rationalPoint, du, dv);
    bool derivativeEnclosed = true;
    for (int axis = 0; axis < 3; ++axis)
      derivativeEnclosed =
          derivativeEnclosed &&
          rationalJet[axis].derivative.lo <= (du + dv).Coord(axis + 1) &&
          rationalJet[axis].derivative.hi >= (du + dv).Coord(axis + 1);
    Check(derivativeEnclosed,
          "represented rational exterior both-axis derivative seeds");
    rejected = false;
    try {
      SurfaceJet(rb, Jet(Interval(-1.1, -.9), Interval(1)), Jet(Interval(.5)));
    } catch (const Standard_Failure &) {
      rejected = true;
    }
    Check(rejected,
          "represented rational jet uncertain denominator unavailable");
    const double epsilon = 1e-12;
    TColgp_Array1OfPnt tensorPoles(1, 3);
    tensorPoles(1) = gp_Pnt(0, epsilon, 0);
    tensorPoles(2) = gp_Pnt(.5, .5 + epsilon, epsilon * .5);
    tensorPoles(3) = gp_Pnt(1, 1 + epsilon, 1 + epsilon);
    Handle(Geom_Curve) tensorCurve = new Geom_BezierCurve(tensorPoles);
    uv(1) = gp_Pnt2d(0, epsilon);
    uv(2) = gp_Pnt2d(1, 1 + epsilon);
    Handle(Geom2d_Curve) tensorPC = new Geom2d_BezierCurve(uv);
    const auto bothAxes = BoundCorrelatedResidual(
        RepresentedCurveBound(tensorCurve, TopLoc_Location()),
        RepresentedCurveBound(tensorPC), bound, 0, 1, .01);
    Check(bothAxes.status == "bounded-within-budget" &&
              bothAxes.intervals <= 127,
          "both varying tensor axes including exterior sliver cost");
    bound.continueAssessment = []() { return false; };
    rejected = false;
    try {
      SurfaceJet(bound, Jet(Interval(.5), Interval(1)),
                 Jet(Interval(.5), Interval(1)));
    } catch (const Standard_Failure &failure) {
      rejected = std::string(failure.GetMessageString()) ==
                 "assessment-monotonic-time-limit";
    }
    Check(rejected, "represented derivative tensor watchdog");
    RepresentedSurfaceBound cacheProbe(periodic, TopLoc_Location());
    const auto cold = cacheProbe.Bounds(Interval(.2, .3), Interval(.2, .3));
    const auto warm = cacheProbe.Bounds(Interval(.2, .3), Interval(.2, .3));
    bool identical = true;
    for (int axis = 0; axis < 3; ++axis)
      identical = identical && cold[axis].lo == warm[axis].lo &&
                  cold[axis].hi == warm[axis].hi;
    Check(identical && cacheProbe.coefficientsCached,
          "cold and warm coefficient cache exact bounds");
    const auto repeated = BoundCorrelatedResidual(
        RepresentedCurveBound(iso, TopLoc_Location()),
        RepresentedCurveBound(wrappedPC), cacheProbe, .1, .9, .01);
    Check(repeated.status == wrappedResidual.status &&
              repeated.upper == wrappedResidual.upper &&
              repeated.lower == wrappedResidual.lower &&
              repeated.intervals == wrappedResidual.intervals,
          "cached completed metric retains exact fields and work count");
    std::cout << "PASS: " << checks << " represented domain checks\n";
    return 0;
  } catch (const Standard_Failure &e) {
    std::cerr << e.GetMessageString() << '\n';
  } catch (const std::exception &e) {
    std::cerr << e.what() << '\n';
  }
  return 1;
}
