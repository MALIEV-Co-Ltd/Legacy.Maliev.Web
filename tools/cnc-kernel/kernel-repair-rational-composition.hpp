#pragma once
#include "kernel-repair-cylinder-composition.hpp"

namespace MalievRepair {
using RationalPoly24 = std::vector<Interval>;
inline RationalPoly24 RationalProduct(const RationalPoly24 &a,
                                      const RationalPoly24 &b,
                                      const CompositionWork &work) {
  work.Check();
  if (a.empty() || b.empty() || a.size() + b.size() > 26)
    throw Standard_Failure("rational polynomial exceeds degree24");
  RationalPoly24 result(a.size() + b.size() - 1);
  for (size_t i = 0; i < a.size(); ++i)
    for (size_t j = 0; j < b.size(); ++j)
      result[i + j] = result[i + j] + a[i] * b[j];
  return result;
}
inline RationalPoly24 RationalSum(const RationalPoly24 &a,
                                  const RationalPoly24 &b, int sign = 1) {
  if (a.empty() || b.empty() || a.size() > 25 || b.size() > 25)
    throw Standard_Failure("invalid rational polynomial sum");
  RationalPoly24 result(std::max(a.size(), b.size()));
  for (size_t index = 0; index < result.size(); ++index) {
    const Interval x = index < a.size() ? a[index] : Interval(0);
    const Interval y = index < b.size() ? b[index] : Interval(0);
    result[index] = sign == 1 ? x + y : x - y;
  }
  return result;
}
inline Interval RationalHull(const RationalPoly24 &power,
                             const CompositionWork &work) {
  work.Check();
  if (power.empty() || power.size() > 25)
    throw Standard_Failure("invalid rational polynomial degree");
  const int degree = static_cast<int>(power.size()) - 1;
  Interval result(std::numeric_limits<double>::infinity(),
                  -std::numeric_limits<double>::infinity());
  for (int index = 0; index <= degree; ++index) {
    work.Check();
    Interval coefficient;
    for (int term = 0; term <= index; ++term)
      coefficient =
          coefficient + power[term] * (Interval(Binomial(index, term)) /
                                       Interval(Binomial(degree, term)));
    if (!std::isfinite(coefficient.lo) || !std::isfinite(coefficient.hi))
      throw Standard_Failure("nonfinite rational polynomial coefficient");
    result.lo = std::min(result.lo, coefficient.lo);
    result.hi = std::max(result.hi, coefficient.hi);
  }
  return result;
}
inline Interval RationalCenter(const RationalPoly24 &polynomial) {
  Interval result;
  for (auto term = polynomial.rbegin(); term != polynomial.rend(); ++term)
    result = result * Interval(.5) + *term;
  return result;
}
inline RationalPoly24 RationalDeBoor(const Spline &axis, int span,
                                     const RationalPoly24 &parameter,
                                     std::vector<RationalPoly24> controls,
                                     const CompositionWork &work) {
  for (int level = 1; level <= axis.degree; ++level)
    for (int index = axis.degree; index >= level; --index) {
      work.Check();
      const int knot = span - axis.degree + index;
      const auto denominator =
          axis.knots.at(knot + axis.degree - level + 1) - axis.knots.at(knot);
      if (denominator.lo <= 0)
        throw Standard_Failure("rational native knot denominator unavailable");
      auto alpha = RationalSum(parameter, {axis.knots.at(knot)}, -1);
      for (auto &coefficient : alpha)
        coefficient = coefficient / denominator;
      controls[index] = RationalSum(
          controls[index - 1],
          RationalProduct(alpha,
                          RationalSum(controls[index], controls[index - 1], -1),
                          work));
    }
  return controls.at(axis.degree);
}
using HomogeneousPoly21 = std::array<RationalPoly24, 4>;
inline HomogeneousPoly21 RationalSurfacePiece(const SurfaceBound &surface,
                                              int uSpan, int vSpan,
                                              const RationalPoly24 &u,
                                              const RationalPoly24 &v,
                                              const CompositionWork &work) {
  HomogeneousPoly21 local;
  for (int coordinate = 0; coordinate < 4; ++coordinate) {
    std::vector<RationalPoly24> rows;
    for (int vIndex = 0; vIndex <= surface.v.degree; ++vIndex) {
      std::vector<RationalPoly24> controls;
      for (int uIndex = 0; uIndex <= surface.u.degree; ++uIndex)
        controls.push_back(
            {surface.poles.at(uSpan - surface.u.degree + uIndex)
                 .at(vSpan - surface.v.degree + vIndex)[coordinate]});
      rows.push_back(RationalDeBoor(surface.u, uSpan, u, controls, work));
    }
    local[coordinate] = RationalDeBoor(surface.v, vSpan, v, rows, work);
    if (local[coordinate].size() > 22)
      throw Standard_Failure("homogeneous lift exceeds degree21");
  }
  HomogeneousPoly21 placed;
  placed[3] = local[3];
  const auto matrix = surface.location.Transformation();
  for (int coordinate = 0; coordinate < 3; ++coordinate) {
    placed[coordinate] = RationalProduct(
        {Interval(matrix.Value(coordinate + 1, 4))}, local[3], work);
    for (int axis = 0; axis < 3; ++axis)
      placed[coordinate] = RationalSum(
          placed[coordinate],
          RationalProduct({Interval(matrix.Value(coordinate + 1, axis + 1))},
                          local[axis], work));
  }
  return placed;
}
struct RationalSourceModel {
  PolynomialPoint polynomial;
  Box remainder;
};
inline RationalSourceModel RationalSourcePiece(const CurveBound &curve,
                                               double first, double last,
                                               const UniPolynomial &parameter,
                                               const CompositionWork &work) {
  RationalSourceModel result;
  if (curve.isSpline) {
    result.polynomial = PolynomialPlaced(
        CurvePiecePolynomial(
            curve.spline,
            PieceSpan(curve.spline, static_cast<int>(curve.spline.poles.size()),
                      first, last),
            parameter, work),
        curve.location, work);
    return result;
  }
  const auto circle = GeomAdaptor_Curve(curve.c3).Circle();
  const auto trig = BoundAffineTrigPolynomial(parameter, work);
  auto cubicPart = [](const UniPolynomial &polynomial) {
    return UniPolynomial(polynomial.begin(),
                         polynomial.begin() +
                             std::min<size_t>(4, polynomial.size()));
  };
  auto tail = [&](const UniPolynomial &polynomial) {
    auto remainder = polynomial;
    for (size_t index = 0; index < std::min<size_t>(4, remainder.size());
         ++index)
      remainder[index] = Interval(0);
    return PolynomialHull(remainder, work) +
           Interval(-trig.remainder, trig.remainder);
  };
  const auto sine = cubicPart(trig.sine), cosine = cubicPart(trig.cosine);
  const auto sineTail = tail(trig.sine), cosineTail = tail(trig.cosine);
  PolynomialPoint local;
  Box localRemainder;
  for (int coordinate = 0; coordinate < 3; ++coordinate) {
    const Interval x =
        Interval(circle.Radius()) *
        Interval(circle.XAxis().Direction().Coord(coordinate + 1));
    const Interval y =
        Interval(circle.Radius()) *
        Interval(circle.YAxis().Direction().Coord(coordinate + 1));
    local[coordinate] =
        PolynomialSum({Interval(circle.Location().Coord(coordinate + 1))},
                      PolynomialSum(PolynomialProduct({x}, cosine, work),
                                    PolynomialProduct({y}, sine, work)));
    localRemainder[coordinate] = x * cosineTail + y * sineTail;
  }
  result.polynomial = PolynomialPlaced(local, curve.location, work);
  const auto matrix = curve.location.Transformation();
  for (int coordinate = 0; coordinate < 3; ++coordinate)
    for (int axis = 0; axis < 3; ++axis)
      result.remainder[coordinate] =
          result.remainder[coordinate] +
          Interval(matrix.Value(coordinate + 1, axis + 1)) *
              localRemainder[axis];
  return result;
}
struct RationalShiftSpan {
  int span, shift;
  RationalPoly24 parameter;
};
inline std::vector<RationalShiftSpan>
RationalPeriodicSpans(const Spline &axis, int count,
                      const RationalPoly24 &parameter,
                      const CompositionWork &work) {
  const auto range = RationalHull(parameter, work);
  const Interval period = Interval(axis.last) - Interval(axis.first);
  if (period.lo <= 0 || !std::isfinite(period.hi))
    throw Standard_Failure("invalid rational native period");
  if (range.lo < axis.first - 2 * period.lo ||
      range.hi > axis.last + 2 * period.lo)
    throw Standard_Failure("rational periodic local-domain resource limit");
  std::vector<RationalShiftSpan> result;
  for (int shift = -3; shift <= 3; ++shift) {
    work.Check();
    const auto shiftAmount = Interval(shift) * period;
    const auto shiftedHull = range - shiftAmount;
    const Interval clipped(std::max(axis.first, shiftedHull.lo),
                           std::min(axis.last, shiftedHull.hi));
    if (clipped.lo > clipped.hi)
      continue;
    auto shifted = parameter;
    shifted[0] = shifted[0] - shiftAmount;
    for (const auto &span : RepresentedSpans(axis, count, clipped, false))
      result.push_back({span.index, shift, shifted});
  }
  if (result.empty())
    throw Standard_Failure("uncovered rational periodic parameter");
  return result;
}
inline Residual
BoundRationalComposition(const CurveBound &curve, const CurveBound &pcurve,
                         const SurfaceBound &surface, double first, double last,
                         double budget, int limit = 32768, int depthLimit = 32,
                         const std::function<bool()> &continues = {}) {
  Residual result;
  CompositionWork work{continues};
  try {
    if (!std::isfinite(first) || !std::isfinite(last) || first >= last ||
        !std::isfinite(budget) || budget <= 0 || limit <= 0 || depthLimit < 0)
      throw Standard_Failure("invalid rational composition policy or domain");
    limit = std::min(limit, 32768);
    depthLimit = std::min(depthLimit, 32);
    const bool circle = !curve.c3.IsNull() &&
                        GeomAdaptor_Curve(curve.c3).GetType() == GeomAbs_Circle;
    if ((!curve.isSpline || curve.spline.degree != 3) && !circle)
      throw Standard_Failure("rational lift requires cubic or circle source");
    if (!pcurve.isSpline || pcurve.spline.degree != 3 || !surface.spline ||
        surface.u.degree != 5 || surface.v.degree != 2)
      throw Standard_Failure(
          "rational lift requires cubic PC and degree(5,2) surface");
    const auto native = GeomAdaptor_Surface(surface.surface).BSpline();
    if (native->IsUPeriodic() || !native->IsVPeriodic())
      throw Standard_Failure("rational lift requires only V periodicity");
    for (const auto *spline : {&curve.spline, &pcurve.spline})
      for (const auto &pole : spline->poles) {
        work.Check();
        if (pole[3].lo != 1 || pole[3].hi != 1)
          throw Standard_Failure("rational source or pcurve unsupported");
      }
    for (const auto &row : surface.poles)
      for (const auto &pole : row) {
        work.Check();
        if (!std::isfinite(pole[3].lo) || !std::isfinite(pole[3].hi) ||
            pole[3].lo <= 0)
          throw Standard_Failure("invalid native positive surface weight");
      }
    std::vector<double> cuts{first, last};
    for (const auto *spline : {&curve.spline, &pcurve.spline})
      for (const auto knot : spline->knots) {
        work.Check();
        if (knot.lo != knot.hi)
          throw Standard_Failure("uncertain rational partition knot");
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
        throw Standard_Failure("rational interval resource limit");
      ++result.intervals;
      const Piece piece = stack.back();
      stack.pop_back();
      const UniPolynomial parameter{
          Interval(piece.first), Interval(piece.last) - Interval(piece.first)};
      RationalSourceModel source;
      bool needsSplit = false;
      try {
        source = RationalSourcePiece(curve, piece.first, piece.last, parameter,
                                     work);
      } catch (const Standard_Failure &error) {
        if (std::string(error.GetMessageString()) !=
            "cylindrical angular piece needs subdivision")
          throw;
        needsSplit = true;
      }
      double upper = 0;
      Box centerHull = EmptyBox();
      if (!needsSplit) {
        const auto uv = CurvePiecePolynomial(
            pcurve.spline,
            PieceSpan(pcurve.spline,
                      static_cast<int>(pcurve.spline.poles.size()), piece.first,
                      piece.last),
            parameter, work);
        const auto uSpans = RepresentedSpans(surface.u, surface.nu,
                                             RationalHull(uv[0], work), false);
        const auto vSpans =
            RationalPeriodicSpans(surface.v, surface.nv, uv[1], work);
        needsSplit = uSpans.size() * vSpans.size() > 16;
        if (!needsSplit)
          for (const auto &u : uSpans) {
            for (const auto &v : vSpans) {
              work.Check();
              if (result.intervals >= limit)
                throw Standard_Failure("rational interval resource limit");
              ++result.intervals;
              const auto lifted = RationalSurfacePiece(
                  surface, u.index, v.span, uv[0], v.parameter, work);
              const auto weight = RationalHull(lifted[3], work);
              const auto centerWeight = RationalCenter(lifted[3]);
              // Every guard expression is evaluated over the FULL t piece.
              // Its raw denominator is never clamped to the native pole hull.
              if (weight.lo <= 0 || centerWeight.lo <= 0) {
                needsSplit = true;
                break;
              }
              Box residual, center;
              for (int coordinate = 0; coordinate < 3; ++coordinate) {
                const auto numerator =
                    RationalSum(RationalProduct(source.polynomial[coordinate],
                                                lifted[3], work),
                                lifted[coordinate], -1);
                residual[coordinate] = RationalHull(numerator, work) / weight +
                                       source.remainder[coordinate];
                center[coordinate] = RationalCenter(numerator) / centerWeight +
                                     source.remainder[coordinate];
              }
              const double candidateUpper = DistanceUpper(
                  residual, Box{Interval(0), Interval(0), Interval(0)});
              if (!std::isfinite(candidateUpper))
                throw Standard_Failure("nonfinite rational residual");
              upper = std::max(upper, candidateUpper);
              Union(centerHull, center);
            }
            if (needsSplit)
              break;
          }
      }
      if (!needsSplit) {
        const double lower = DistanceLower(
            centerHull, Box{Interval(0), Interval(0), Interval(0)});
        result.lower = std::max(result.lower, lower);
        if (lower > budget) {
          result.status = "exceeds-budget";
          result.upper = std::numeric_limits<double>::infinity();
          return result;
        }
        if (upper <= budget) {
          acceptedUpper = std::max(acceptedUpper, upper);
          continue;
        }
      }
      const double middle = piece.first + (piece.last - piece.first) * .5;
      if (piece.depth >= depthLimit || middle <= piece.first ||
          middle >= piece.last)
        throw Standard_Failure("rational subdivision resolution limit");
      ++result.subdivisions;
      stack.push_back({middle, piece.last, piece.depth + 1});
      stack.push_back({piece.first, middle, piece.depth + 1});
    }
    result.status = "bounded-within-budget";
    result.upper = acceptedUpper;
  } catch (const Standard_Failure &error) {
    result.reason = error.GetMessageString();
  }
  return result;
}
} // namespace MalievRepair
