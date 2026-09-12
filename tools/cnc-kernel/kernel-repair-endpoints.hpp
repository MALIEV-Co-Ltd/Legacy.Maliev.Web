#pragma once
#include "kernel-repair-bounds.hpp"

namespace MalievRepair {
// Pinned BSplCLib retains an exterior nonperiodic parameter and clamps only
// its span index. This separate helper does not relax the base-domain API.
inline Box EndpointExtendedCurveBox(const CurveBound &curve, double first,
                                    double last) {
  if (!std::isfinite(first) || !std::isfinite(last) || first > last)
    throw Standard_Failure("invalid endpoint extension interval");
  if (!curve.isSpline)
    return curve.Bounds(first, last);
  const auto &spline = curve.spline;
  Box result = EmptyBox();
  const double interiorFirst = std::max(first, spline.first);
  const double interiorLast = std::min(last, spline.last);
  if (interiorFirst <= interiorLast)
    Union(result, SplineBox(spline, interiorFirst, interiorLast));
  for (int side = 0; side < 2; ++side) {
    const bool left = side == 0;
    const double a = left ? first : std::max(first, spline.last);
    const double b = left ? std::min(last, spline.first) : last;
    if ((left && first >= spline.first) || (!left && last <= spline.last))
      continue;
    int selected = -1;
    for (int span = spline.degree; span < int(spline.poles.size()); ++span) {
      const double lo = spline.knots.at(span).lo;
      const double hi = spline.knots.at(span + 1).hi;
      if (lo < hi && ((left && lo <= spline.first && spline.first < hi) ||
                      (!left && lo < spline.last && spline.last <= hi))) {
        if (selected != -1)
          throw Standard_Failure("ambiguous endpoint extension span");
        selected = span;
      }
    }
    if (selected == -1)
      throw Standard_Failure("missing endpoint extension span");
    const auto homogeneous = DeBoor(spline, selected, Interval(a, b));
    // Extrapolated basis functions need not be positive. Never intersect this
    // denominator with the original positive control-weight hull.
    if (!std::isfinite(homogeneous[3].lo) ||
        !std::isfinite(homogeneous[3].hi) || homogeneous[3].lo <= 0)
      throw Standard_Failure("endpoint extension denominator unavailable");
    Union(result, Cartesian(homogeneous));
  }
  result = curve.is2d ? result : Placed(result, curve.location);
  for (const auto &coordinate : result)
    if (!std::isfinite(coordinate.lo) || !std::isfinite(coordinate.hi) ||
        coordinate.lo > coordinate.hi)
      throw Standard_Failure("nonfinite endpoint extension enclosure");
  return result;
}
} // namespace MalievRepair
