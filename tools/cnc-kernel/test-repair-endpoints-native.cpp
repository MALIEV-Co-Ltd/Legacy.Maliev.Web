#include "kernel-repair-endpoints.hpp"
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
  for (int axis = 0; axis < 3; ++axis)
    if (point.Coord(axis + 1) < box[axis].lo ||
        point.Coord(axis + 1) > box[axis].hi)
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
    TColgp_Array1OfPnt poles(1, 2);
    poles(1) = gp_Pnt(0, 0, 0);
    poles(2) = gp_Pnt(1, 2, 0);
    Handle(Geom_BSplineCurve) line =
        new Geom_BSplineCurve(poles, knots, mult, 1);
    CurveBound linear(line, TopLoc_Location());
    for (const auto range :
         {std::make_pair(-.01, 0.), std::make_pair(1., 1.01)}) {
      const auto box =
          EndpointExtendedCurveBox(linear, range.first, range.second);
      Check(Contains(box, gp_Pnt(range.first, 2 * range.first, 0)) &&
                Contains(box, gp_Pnt(range.second, 2 * range.second, 0)),
            "linear endpoint extension includes analytic endpoints");
      gp_Pnt native;
      line->D0(range.first, native);
      Check(Contains(box, native), "native D0 preserves exterior parameter");
    }
    Check(Contains(EndpointExtendedCurveBox(linear, -.01, 1.01),
                   gp_Pnt(1.01, 2.02, 0)),
          "interior and both slivers form whole interval enclosure");
    TColgp_Array1OfPnt quadraticPoles(1, 3);
    quadraticPoles(1) = gp_Pnt(0, 0, 0);
    quadraticPoles(2) = gp_Pnt(.5, 0, 0);
    quadraticPoles(3) = gp_Pnt(1, 1, 0);
    mult.Init(3);
    Handle(Geom_BSplineCurve) quadratic =
        new Geom_BSplineCurve(quadraticPoles, knots, mult, 2);
    for (const double parameter : {-.01, 1.01}) {
      const auto box = EndpointExtendedCurveBox(
          CurveBound(quadratic, TopLoc_Location()), parameter, parameter);
      Check(Contains(box, gp_Pnt(parameter, parameter * parameter, 0)),
            "quadratic exterior value analytic enclosure");
    }
    TColStd_Array1OfReal weights(1, 2);
    weights(1) = 1;
    weights(2) = 2;
    mult.Init(2);
    Handle(Geom_BSplineCurve) rational =
        new Geom_BSplineCurve(poles, weights, knots, mult, 1);
    const auto rationalBox = EndpointExtendedCurveBox(
        CurveBound(rational, TopLoc_Location()), -.01, 0);
    Check(Contains(rationalBox, gp_Pnt(-.02 / .99, -.04 / .99, 0)),
          "positive exterior rational denominator enclosure");
    bool rejected = false;
    try {
      EndpointExtendedCurveBox(CurveBound(rational, TopLoc_Location()), -1.1,
                               -.9);
    } catch (const Standard_Failure &) {
      rejected = true;
    }
    Check(rejected, "exterior rational zero denominator unavailable");
    rejected = false;
    try {
      linear.Bounds(-.01, 0);
    } catch (const Standard_Failure &) {
      rejected = true;
    }
    Check(rejected, "reviewed base domain contract unchanged");
    for (const auto &nativeCurve : {line, quadratic}) {
      const CurveBound bounded(nativeCurve, TopLoc_Location());
      const bool isLinear = nativeCurve == line;
      for (const double parameter : {-.01, .01, .99, 1.01}) {
        gp_Pnt point;
        gp_Vec derivative;
        nativeCurve->D1(parameter, point, derivative);
        const gp_Pnt expected(
            parameter, isLinear ? 2 * parameter : parameter * parameter, 0);
        const gp_Vec expectedDerivative(1, isLinear ? 2 : 2 * parameter, 0);
        gp_Pnt d0;
        nativeCurve->D0(parameter, d0);
        Check(point.Distance(expected) < 1e-13 &&
                  d0.Distance(expected) < 1e-13 &&
                  (derivative - expectedDerivative).Magnitude() < 1e-13,
              "native D0 D1 interior exterior analytic regression");
      }
      for (const double endpoint : {0., 1.}) {
        const auto left =
            EndpointExtendedCurveBox(bounded, endpoint - .01, endpoint);
        const auto right =
            EndpointExtendedCurveBox(bounded, endpoint, endpoint + .01);
        const gp_Pnt expected(endpoint,
                              isLinear ? 2 * endpoint : endpoint * endpoint, 0);
        Check(Contains(left, expected) && Contains(right, expected),
              "adjacent interior exterior enclosures share native endpoint");
      }
    }
    std::cout << "PASS: " << checks << " endpoint extension native checks\n";
    return 0;
  } catch (const Standard_Failure &failure) {
    std::cerr << failure.GetMessageString() << '\n';
  } catch (const std::exception &failure) {
    std::cerr << failure.what() << '\n';
  }
  return 1;
}
