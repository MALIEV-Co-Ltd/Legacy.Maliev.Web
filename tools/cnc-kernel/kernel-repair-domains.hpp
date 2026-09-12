#pragma once
#include "kernel-repair-correlated.hpp"
#include "kernel-repair-endpoints.hpp"

namespace MalievRepair {
struct RepresentedCurveBound : CurveBound {
  using CurveBound::CurveBound;
  mutable bool coefficientsCached = false;
  mutable std::vector<HJet> cachedJetPoles;
  mutable double minWeight = std::numeric_limits<double>::infinity(),
                 maxWeight = 0;
  void CacheCoefficients() const {
    if (coefficientsCached)
      return;
    for (const auto &point : spline.poles) {
      cachedJetPoles.push_back(ConstantJet(point));
      minWeight = std::min(minWeight, point[3].lo);
      maxWeight = std::max(maxWeight, point[3].hi);
    }
    coefficientsCached = true;
  }
  Box Bounds(double first, double last) const {
    return EndpointExtendedCurveBox(*this, first, last);
  }
};
struct AxisSpan {
  int index;
  Interval parameter;
  bool exterior;
};
inline std::vector<Interval> PeriodPieces(const Spline &axis, Interval range,
                                          bool periodic) {
  if (!std::isfinite(range.lo) || !std::isfinite(range.hi) ||
      range.lo > range.hi)
    throw Standard_Failure("invalid represented parameter interval");
  if (!periodic || (range.lo >= axis.first && range.hi <= axis.last))
    return {range};
  const Interval period = Interval(axis.last) - Interval(axis.first);
  if (period.lo <= 0 || !std::isfinite(period.hi))
    throw Standard_Failure("invalid native period");
  // This adapter supports finite local seam neighborhoods, not arbitrary
  // large-parameter native normalization. Every admissible shift is enumerated.
  if (range.lo < axis.first - 2 * period.lo ||
      range.hi > axis.last + 2 * period.lo)
    throw Standard_Failure("periodic local-domain resource limit");
  std::vector<Interval> pieces;
  for (int shift = -3; shift <= 3; ++shift) {
    const auto translated = range - Interval(shift) * period;
    const double lo = std::max(axis.first, translated.lo);
    const double hi = std::min(axis.last, translated.hi);
    if (lo <= hi)
      pieces.push_back({lo, hi});
  }
  if (pieces.empty())
    throw Standard_Failure("uncovered periodic parameter");
  return pieces;
}
inline std::vector<AxisSpan> RepresentedSpans(const Spline &axis, int count,
                                              Interval range, bool periodic) {
  std::vector<AxisSpan> spans;
  for (const auto piece : PeriodPieces(axis, range, periodic)) {
    for (int index = axis.degree; index < count; ++index) {
      const double lo = axis.knots.at(index).lo;
      const double hi = axis.knots.at(index + 1).hi;
      if (lo >= hi)
        continue;
      const double first = std::max(piece.lo, lo);
      const double last = std::min(piece.hi, hi);
      if (first <= last)
        spans.push_back({index, {first, last}, false});
      if (!periodic && lo <= axis.first && axis.first < hi &&
          piece.lo < axis.first)
        spans.push_back(
            {index, {piece.lo, std::min(piece.hi, axis.first)}, true});
      if (!periodic && lo < axis.last && axis.last <= hi &&
          piece.hi > axis.last)
        spans.push_back(
            {index, {std::max(piece.lo, axis.last), piece.hi}, true});
    }
  }
  if (spans.empty())
    throw Standard_Failure("uncovered represented surface axis");
  return spans;
}
struct RepresentedSurfaceBound : SurfaceBound {
  using SurfaceBound::SurfaceBound;
  std::function<bool()> continueAssessment;
  mutable bool coefficientsCached = false;
  mutable std::vector<std::vector<HJet>> cachedJetPoles;
  mutable double minWeight = std::numeric_limits<double>::infinity(),
                 maxWeight = 0;
  void CacheCoefficients() const {
    if (coefficientsCached)
      return;
    for (const auto &row : poles) {
      std::vector<HJet> converted;
      for (const auto &point : row) {
        converted.push_back(ConstantJet(point));
        minWeight = std::min(minWeight, point[3].lo);
        maxWeight = std::max(maxWeight, point[3].hi);
      }
      cachedJetPoles.push_back(converted);
    }
    coefficientsCached = true;
  }
  Box Bounds(Interval U, Interval V) const {
    if (!spline)
      return SurfaceBound::Bounds(U, V);
    const auto native = GeomAdaptor_Surface(surface).BSpline();
    const auto us = RepresentedSpans(u, nu, U, native->IsUPeriodic());
    const auto vs = RepresentedSpans(v, nv, V, native->IsVPeriodic());
    if (us.size() * vs.size() > 1024)
      throw Standard_Failure("represented surface cell resource limit");
    Box result = EmptyBox();
    CacheCoefficients();
    for (const auto &up : us)
      for (const auto &vp : vs) {
        if (continueAssessment && !continueAssessment())
          throw Standard_Failure("assessment-monotonic-time-limit");
        Spline row = v;
        row.poles.resize(nv);
        for (int y = vp.index - v.degree; y <= vp.index; ++y) {
          Spline column = u;
          column.poles.resize(nu);
          for (int x = up.index - u.degree; x <= up.index; ++x)
            column.poles[x] = poles[x][y];
          row.poles[y] = DeBoor(column, up.index, up.parameter);
        }
        auto homogeneous = DeBoor(row, vp.index, vp.parameter);
        if (minWeight == maxWeight) {
          // Identical native weights define an exactly constant homogeneous
          // denominator, including endpoint polynomial extensions.
          homogeneous[3] = Interval(minWeight);
        } else if (!up.exterior && !vp.exterior) {
          homogeneous[3].lo = std::max(homogeneous[3].lo, minWeight);
          homogeneous[3].hi = std::min(homogeneous[3].hi, maxWeight);
        }
        if (homogeneous[3].lo <= 0 || !std::isfinite(homogeneous[3].hi))
          throw Standard_Failure("represented surface denominator unavailable");
        Union(result, Cartesian(homogeneous));
      }
    result = Placed(result, location);
    for (const auto &coordinate : result)
      if (!std::isfinite(coordinate.lo) || !std::isfinite(coordinate.hi) ||
          coordinate.lo > coordinate.hi)
        throw Standard_Failure("nonfinite represented surface enclosure");
    return result;
  }
};
inline PointJet RepresentedCartesianJet(HJet homogeneous, double minWeight,
                                        double maxWeight, bool exterior) {
  if (minWeight == maxWeight) {
    homogeneous[3] = Jet(Interval(minWeight));
  } else if (!exterior) {
    homogeneous[3].value.lo = std::max(homogeneous[3].value.lo, minWeight);
    homogeneous[3].value.hi = std::min(homogeneous[3].value.hi, maxWeight);
  }
  if (homogeneous[3].value.lo <= 0 || !std::isfinite(homogeneous[3].value.hi))
    throw Standard_Failure("represented jet denominator unavailable");
  PointJet result;
  for (int axis = 0; axis < 3; ++axis)
    result[axis] = homogeneous[axis] / homogeneous[3];
  return result;
}
inline PointJet CurveJet(const RepresentedCurveBound &curve, Interval range) {
  if (!curve.isSpline)
    return CurveJet(static_cast<const CurveBound &>(curve), range);
  const auto &spline = curve.spline;
  RequireContinuous(spline, range);
  const auto spans = RepresentedSpans(
      spline, static_cast<int>(spline.poles.size()), range, false);
  curve.CacheCoefficients();
  HJet total = EmptyJet();
  bool exterior = false;
  for (const auto &span : spans) {
    UnionJet(total,
             DeBoorJet(spline, span.index, Jet(span.parameter, Interval(1)),
                       curve.cachedJetPoles));
    exterior = exterior || span.exterior;
  }
  const auto result = RepresentedCartesianJet(total, curve.minWeight,
                                              curve.maxWeight, exterior);
  return curve.is2d ? result : PlacedJet(result, curve.location);
}
inline PointJet SurfaceJet(const RepresentedSurfaceBound &surface, Jet u,
                           Jet v) {
  if (!surface.spline)
    return SurfaceJet(static_cast<const SurfaceBound &>(surface), u, v);
  const auto native = GeomAdaptor_Surface(surface.surface).BSpline();
  const auto us =
      RepresentedSpans(surface.u, surface.nu, u.value, native->IsUPeriodic());
  const auto vs =
      RepresentedSpans(surface.v, surface.nv, v.value, native->IsVPeriodic());
  if (us.size() * vs.size() > 1024)
    throw Standard_Failure("represented jet cell resource limit");
  HJet total = EmptyJet();
  surface.CacheCoefficients();
  bool exterior = false;
  for (const auto &up : us)
    for (const auto &vp : vs) {
      if (surface.continueAssessment && !surface.continueAssessment())
        throw Standard_Failure("assessment-monotonic-time-limit");
      RequireContinuous(surface.u, up.parameter);
      RequireContinuous(surface.v, vp.parameter);
      std::vector<HJet> row(surface.nv);
      for (int y = vp.index - surface.v.degree; y <= vp.index; ++y) {
        std::vector<HJet> column(surface.nu);
        for (int x = up.index - surface.u.degree; x <= up.index; ++x)
          column[x] = surface.cachedJetPoles[x][y];
        row[y] = DeBoorJet(surface.u, up.index, Jet(up.parameter, u.derivative),
                           column);
      }
      UnionJet(total, DeBoorJet(surface.v, vp.index,
                                Jet(vp.parameter, v.derivative), row));
      exterior = exterior || up.exterior || vp.exterior;
    }
  return PlacedJet(RepresentedCartesianJet(total, surface.minWeight,
                                           surface.maxWeight, exterior),
                   surface.location);
}
} // namespace MalievRepair
