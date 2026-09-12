#pragma once
#include "kernel-repair-domains.hpp"

namespace MalievRepair {
// Common-variable, nonrational cubic/bicubic composition. Never truncate a
// polynomial: cubic U and V in a bicubic support require all degree18 terms.
using UniPolynomial = std::vector<Interval>;
using PolynomialPoint = std::array<UniPolynomial, 3>;
struct CompositionWork {
  std::function<bool()> continues;
  void Check() const {
    if (continues && !continues())
      throw Standard_Failure("assessment-monotonic-time-limit");
  }
};
inline UniPolynomial PolynomialSum(const UniPolynomial &a,
                                   const UniPolynomial &b, int sign = 1) {
  UniPolynomial result(std::max(a.size(), b.size()));
  for (size_t i = 0; i < result.size(); ++i) {
    const Interval x = i < a.size() ? a[i] : Interval(0);
    const Interval y = i < b.size() ? b[i] : Interval(0);
    result[i] = sign == 1 ? x + y : x - y;
  }
  return result;
}
inline UniPolynomial PolynomialProduct(const UniPolynomial &a,
                                       const UniPolynomial &b,
                                       const CompositionWork &work) {
  work.Check();
  if (a.size() + b.size() > 20)
    throw Standard_Failure("composition degree exceeds18");
  UniPolynomial result(a.size() + b.size() - 1);
  for (size_t i = 0; i < a.size(); ++i)
    for (size_t j = 0; j < b.size(); ++j)
      result[i + j] = result[i + j] + a[i] * b[j];
  return result;
}
inline UniPolynomial PolynomialDeBoorScalar(const Spline &axis, int span,
                                            const UniPolynomial &parameter,
                                            std::vector<UniPolynomial> controls,
                                            const CompositionWork &work) {
  for (int level = 1; level <= axis.degree; ++level)
    for (int index = axis.degree; index >= level; --index) {
      work.Check();
      const int knot = span - axis.degree + index;
      const auto denominator =
          axis.knots.at(knot + axis.degree - level + 1) - axis.knots.at(knot);
      if (denominator.lo <= 0)
        throw Standard_Failure("composition knot denominator unavailable");
      auto alpha = PolynomialSum(parameter, {axis.knots.at(knot)}, -1);
      for (auto &coefficient : alpha)
        coefficient = coefficient / denominator;
      controls[index] = PolynomialSum(
          controls[index - 1],
          PolynomialProduct(
              alpha, PolynomialSum(controls[index], controls[index - 1], -1),
              work));
    }
  return controls.at(axis.degree);
}
inline PolynomialPoint CurvePiecePolynomial(const Spline &curve, int span,
                                            const UniPolynomial &parameter,
                                            const CompositionWork &work) {
  PolynomialPoint result;
  for (int coordinate = 0; coordinate < 3; ++coordinate) {
    std::vector<UniPolynomial> controls;
    for (int index = 0; index <= curve.degree; ++index)
      controls.push_back(
          {curve.poles.at(span - curve.degree + index)[coordinate]});
    result[coordinate] =
        PolynomialDeBoorScalar(curve, span, parameter, controls, work);
  }
  return result;
}
inline double Binomial(int n, int k) {
  double value = 1;
  for (int i = 1; i <= k; ++i)
    value = value * (n - i + 1) / i;
  return value;
}
inline Interval PolynomialHull(const UniPolynomial &power,
                               const CompositionWork &work) {
  work.Check();
  if (power.empty() || power.size() > 19)
    throw Standard_Failure("invalid composition polynomial degree");
  const int degree = static_cast<int>(power.size()) - 1;
  Interval hull(std::numeric_limits<double>::infinity(),
                -std::numeric_limits<double>::infinity());
  for (int index = 0; index <= degree; ++index) {
    Interval coefficient;
    for (int term = 0; term <= index; ++term)
      coefficient =
          coefficient + power[term] * (Interval(Binomial(index, term)) /
                                       Interval(Binomial(degree, term)));
    if (!std::isfinite(coefficient.lo) || !std::isfinite(coefficient.hi))
      throw Standard_Failure("nonfinite composition coefficient");
    hull.lo = std::min(hull.lo, coefficient.lo);
    hull.hi = std::max(hull.hi, coefficient.hi);
  }
  return hull;
}
inline PolynomialPoint PolynomialPlaced(const PolynomialPoint &point,
                                        const TopLoc_Location &location,
                                        const CompositionWork &work) {
  PolynomialPoint result;
  const auto matrix = location.Transformation();
  for (int row = 0; row < 3; ++row) {
    result[row] = {Interval(matrix.Value(row + 1, 4))};
    for (int column = 0; column < 3; ++column)
      result[row] = PolynomialSum(
          result[row],
          PolynomialProduct(point[column],
                            {Interval(matrix.Value(row + 1, column + 1))},
                            work));
  }
  return result;
}
inline PolynomialPoint SurfacePiecePolynomial(const SurfaceBound &surface,
                                              int uSpan, int vSpan,
                                              const PolynomialPoint &uv,
                                              const CompositionWork &work) {
  PolynomialPoint result;
  for (int coordinate = 0; coordinate < 3; ++coordinate) {
    std::vector<UniPolynomial> vControls;
    for (int vIndex = 0; vIndex <= surface.v.degree; ++vIndex) {
      std::vector<UniPolynomial> uControls;
      for (int uIndex = 0; uIndex <= surface.u.degree; ++uIndex)
        uControls.push_back(
            {surface.poles.at(uSpan - surface.u.degree + uIndex)
                 .at(vSpan - surface.v.degree + vIndex)[coordinate]});
      vControls.push_back(
          PolynomialDeBoorScalar(surface.u, uSpan, uv[0], uControls, work));
    }
    result[coordinate] =
        PolynomialDeBoorScalar(surface.v, vSpan, uv[1], vControls, work);
  }
  return PolynomialPlaced(result, surface.location, work);
}
inline int PieceSpan(const Spline &axis, int count, double first, double last) {
  const auto spans =
      RepresentedSpans(axis, count, Interval(first, last), false);
  int result = -1;
  for (const auto &span : spans) {
    // Closed neighboring spans can meet only at a partition endpoint. Select
    // the expression covering the full positive-length interval or exterior.
    if (span.parameter.lo <= first && span.parameter.hi >= last) {
      if (result >= 0 && result != span.index)
        throw Standard_Failure("ambiguous composition curve span");
      result = span.index;
    }
  }
  if (result < 0)
    throw Standard_Failure("composition partition crosses curve knot");
  return result;
}
inline Residual
BoundPolynomialComposition(const CurveBound &curve, const CurveBound &pcurve,
                           const SurfaceBound &surface, double first,
                           double last, double budget, int limit = 32768,
                           int depthLimit = 32,
                           const std::function<bool()> &continues = {}) {
  Residual result;
  CompositionWork work{continues};
  try {
    if (!std::isfinite(first) || !std::isfinite(last) || first >= last ||
        !std::isfinite(budget) || budget <= 0 || limit <= 0 || depthLimit < 0)
      throw Standard_Failure("invalid composition policy or domain");
    const bool affinePC =
        !pcurve.c2.IsNull() &&
        Geom2dAdaptor_Curve(pcurve.c2).GetType() == GeomAbs_Line;
    if (!curve.isSpline || (!pcurve.isSpline && !affinePC) || !surface.spline ||
        curve.spline.degree != 3 ||
        (pcurve.isSpline && pcurve.spline.degree != 3) ||
        surface.u.degree != 3 || surface.v.degree != 3)
      throw Standard_Failure("composition requires cubic 3d, cubic or line "
                             "pcurve, bicubic surface");
    const auto nativeSurface = GeomAdaptor_Surface(surface.surface).BSpline();
    if (nativeSurface->IsUPeriodic() || nativeSurface->IsVPeriodic())
      throw Standard_Failure("periodic composition unsupported");
    auto checkWeights = [&](const std::vector<HPoint> &poles) {
      for (const auto &point : poles) {
        work.Check();
        if (point[3].lo != 1 || point[3].hi != 1)
          throw Standard_Failure("rational composition unsupported");
      }
    };
    checkWeights(curve.spline.poles);
    if (!affinePC)
      checkWeights(pcurve.spline.poles);
    for (const auto &row : surface.poles)
      checkWeights(row);
    std::vector<double> cuts{first, last};
    for (const auto *axis : {&curve.spline, &pcurve.spline})
      for (const auto &knot : axis->knots) {
        work.Check();
        if (knot.lo != knot.hi)
          throw Standard_Failure("uncertain composition partition knot");
        if (knot.lo > first && knot.lo < last)
          cuts.push_back(knot.lo);
      }
    std::sort(cuts.begin(), cuts.end());
    cuts.erase(std::unique(cuts.begin(), cuts.end()), cuts.end());
    struct Piece {
      double first, last;
      int depth;
    };
    std::vector<Piece> stack;
    for (size_t index = cuts.size() - 1; index > 0; --index)
      stack.push_back({cuts[index - 1], cuts[index], 0});
    double acceptedUpper = 0;
    while (!stack.empty()) {
      work.Check();
      if (result.intervals >= limit)
        throw Standard_Failure("composition interval resource limit");
      ++result.intervals;
      const auto piece = stack.back();
      stack.pop_back();
      const UniPolynomial parameter{
          Interval(piece.first), Interval(piece.last) - Interval(piece.first)};
      const auto source = PolynomialPlaced(
          CurvePiecePolynomial(
              curve.spline,
              PieceSpan(curve.spline,
                        static_cast<int>(curve.spline.poles.size()),
                        piece.first, piece.last),
              parameter, work),
          curve.location, work);
      PolynomialPoint uv;
      if (affinePC) {
        const auto line = Geom2dAdaptor_Curve(pcurve.c2).Line();
        // Both stored direction components are retained, even when one is
        // near machine epsilon. This is exact affine composition, not an
        // isoparametric approximation or an angle/axis snapping operation.
        for (int coordinate = 0; coordinate < 2; ++coordinate) {
          const Interval direction(line.Direction().Coord(coordinate + 1));
          uv[coordinate] = {Interval(line.Location().Coord(coordinate + 1)) +
                                direction * parameter[0],
                            direction * parameter[1]};
        }
        uv[2] = {Interval(0)};
      } else {
        uv = CurvePiecePolynomial(
            pcurve.spline,
            PieceSpan(pcurve.spline,
                      static_cast<int>(pcurve.spline.poles.size()), piece.first,
                      piece.last),
            parameter, work);
      }
      const auto uSpans = RepresentedSpans(surface.u, surface.nu,
                                           PolynomialHull(uv[0], work), false);
      const auto vSpans = RepresentedSpans(surface.v, surface.nv,
                                           PolynomialHull(uv[1], work), false);
      double upper = 0;
      Box centerHull = EmptyBox();
      bool boundedCells = uSpans.size() * vSpans.size() <= 16;
      if (boundedCells)
        for (const auto &u : uSpans)
          for (const auto &v : vSpans) {
            if (result.intervals >= limit)
              throw Standard_Failure("composition interval resource limit");
            ++result.intervals;
            const auto lifted =
                SurfacePiecePolynomial(surface, u.index, v.index, uv, work);
            Box residual;
            Box centerResidual;
            for (int coordinate = 0; coordinate < 3; ++coordinate) {
              const auto polynomial =
                  PolynomialSum(source[coordinate], lifted[coordinate], -1);
              residual[coordinate] = PolynomialHull(polynomial, work);
              Interval value;
              for (auto term = polynomial.rbegin(); term != polynomial.rend();
                   ++term)
                value = value * Interval(.5) + *term;
              centerResidual[coordinate] = value;
            }
            Union(centerHull, centerResidual);
            const double candidateUpper = DistanceUpper(
                residual, Box{Interval(0), Interval(0), Interval(0)});
            if (!std::isfinite(candidateUpper))
              throw Standard_Failure("nonfinite composition residual");
            upper = std::max(upper, candidateUpper);
          }
      if (boundedCells) {
        result.lower =
            std::max(result.lower,
                     DistanceLower(centerHull,
                                   Box{Interval(0), Interval(0), Interval(0)}));
        if (result.lower > budget) {
          result.status = "exceeds-budget";
          result.reason = "composition interval point witness";
          result.upper = std::numeric_limits<double>::infinity();
          return result;
        }
      }
      if (boundedCells && std::isfinite(upper) && upper <= budget) {
        acceptedUpper = std::max(acceptedUpper, upper);
        continue;
      }
      const double center = piece.first + (piece.last - piece.first) * .5;
      if (piece.depth >= depthLimit ||
          !(center > piece.first && center < piece.last))
        throw Standard_Failure("composition subdivision resolution limit");
      ++result.subdivisions;
      stack.push_back({center, piece.last, piece.depth + 1});
      stack.push_back({piece.first, center, piece.depth + 1});
    }
    result.upper = acceptedUpper;
    result.status = "bounded-within-budget";
  } catch (const Standard_Failure &failure) {
    result.reason = failure.GetMessageString();
  }
  return result;
}
} // namespace MalievRepair
