#pragma once
#include "kernel-repair-composition.hpp"

namespace MalievRepair {
struct CylindricalTrigPolynomial {
  UniPolynomial sine, cosine;
  double remainder = 0;
};
inline CylindricalTrigPolynomial
BoundAffineTrigPolynomial(const UniPolynomial &angle,
                          const CompositionWork &work) {
  if (angle.empty() || angle.size() > 2)
    throw Standard_Failure("cylindrical angle is not affine");
  const Interval domain = PolynomialHull(angle, work);
  const double center = domain.lo + (domain.hi - domain.lo) * .5;
  if (!std::isfinite(center))
    throw Standard_Failure("nonfinite cylindrical phase center");
  const auto delta = PolynomialSum(angle, {Interval(center)}, -1);
  const auto deltaHull = PolynomialHull(delta, work);
  const double radius =
      std::max(std::abs(deltaHull.lo), std::abs(deltaHull.hi));
  if (radius > .5)
    throw Standard_Failure("cylindrical angular piece needs subdivision");
  const Interval sine = Trig(Interval(center), false);
  const Interval cosine = Trig(Interval(center), true);
  const std::array<Interval, 4> sineDerivatives{
      sine, cosine, Interval(0) - sine, Interval(0) - cosine};
  const std::array<Interval, 4> cosineDerivatives{cosine, Interval(0) - sine,
                                                  Interval(0) - cosine, sine};
  CylindricalTrigPolynomial result;
  UniPolynomial power{Interval(1)};
  double factorial = 1;
  for (int degree = 0; degree <= 8; ++degree) {
    work.Check();
    if (degree > 0)
      factorial *= degree; // Integers through8! are exactly representable.
    const Interval sineCoefficient =
        sineDerivatives[degree % 4] / Interval(factorial);
    const Interval cosineCoefficient =
        cosineDerivatives[degree % 4] / Interval(factorial);
    UniPolynomial sineTerm = power, cosineTerm = power;
    for (size_t index = 0; index < power.size(); ++index) {
      sineTerm[index] = sineTerm[index] * sineCoefficient;
      cosineTerm[index] = cosineTerm[index] * cosineCoefficient;
    }
    result.sine = PolynomialSum(result.sine, sineTerm);
    result.cosine = PolynomialSum(result.cosine, cosineTerm);
    if (degree < 8)
      power = PolynomialProduct(power, delta, work);
  }
  // Taylor's theorem on the REAL entire angular segment: every ninth
  // derivative of sin/cos has absolute value<=1. This is not sampled fitting.
  Interval remainder(1);
  for (int exponent = 0; exponent < 9; ++exponent)
    remainder = remainder * Interval(radius);
  result.remainder = (remainder / Interval(362880)).hi;
  return result;
}
inline Residual
BoundCylindricalComposition(const CurveBound &curve, const CurveBound &pcurve,
                            const SurfaceBound &surface, double first,
                            double last, double budget, int limit = 32768,
                            int depthLimit = 32,
                            const std::function<bool()> &continues = {}) {
  Residual result;
  CompositionWork work{continues};
  try {
    if (!std::isfinite(first) || !std::isfinite(last) || first >= last ||
        !std::isfinite(budget) || budget <= 0 || limit <= 0 || depthLimit < 0)
      throw Standard_Failure("invalid cylindrical policy or domain");
    limit = std::min(limit, 32768);
    depthLimit = std::min(depthLimit, 32);
    if (!curve.isSpline || curve.spline.degree != 3 || !pcurve.isSpline ||
        pcurve.spline.degree != 1 || surface.surface.IsNull() ||
        GeomAdaptor_Surface(surface.surface).GetType() != GeomAbs_Cylinder)
      throw Standard_Failure(
          "cylindrical composition requires cubic 3d and linear spline pcurve");
    for (const auto *spline : {&curve.spline, &pcurve.spline})
      for (const auto &pole : spline->poles) {
        work.Check();
        if (pole[3].lo != 1 || pole[3].hi != 1)
          throw Standard_Failure(
              "rational cylindrical composition unsupported");
      }
    const auto cylinder = GeomAdaptor_Surface(surface.surface).Cylinder();
    const auto axes = cylinder.Position();
    const auto matrix = surface.location.Transformation();
    std::vector<double> cuts{first, last};
    for (const auto *spline : {&curve.spline, &pcurve.spline})
      for (const auto knot : spline->knots) {
        work.Check();
        if (knot.lo != knot.hi)
          throw Standard_Failure("uncertain cylindrical partition knot");
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
        throw Standard_Failure("cylindrical interval resource limit");
      ++result.intervals;
      const Piece piece = stack.back();
      stack.pop_back();
      const UniPolynomial parameter{
          Interval(piece.first), Interval(piece.last) - Interval(piece.first)};
      const auto uv = CurvePiecePolynomial(
          pcurve.spline,
          PieceSpan(pcurve.spline, static_cast<int>(pcurve.spline.poles.size()),
                    piece.first, piece.last),
          parameter, work);
      bool needsSplit = false;
      CylindricalTrigPolynomial trigonometry;
      try {
        trigonometry = BoundAffineTrigPolynomial(uv[0], work);
      } catch (const Standard_Failure &error) {
        if (std::string(error.GetMessageString()) !=
            "cylindrical angular piece needs subdivision")
          throw;
        needsSplit = true;
      }
      if (!needsSplit) {
        if (result.intervals >= limit)
          throw Standard_Failure("cylindrical interval resource limit");
        ++result.intervals;
        const auto source = PolynomialPlaced(
            CurvePiecePolynomial(
                curve.spline,
                PieceSpan(curve.spline,
                          static_cast<int>(curve.spline.poles.size()),
                          piece.first, piece.last),
                parameter, work),
            curve.location, work);
        PolynomialPoint localLift;
        Box localRemainder;
        for (int coordinate = 0; coordinate < 3; ++coordinate) {
          const Interval x = Interval(cylinder.Radius()) *
                             Interval(axes.XDirection().Coord(coordinate + 1));
          const Interval y = Interval(cylinder.Radius()) *
                             Interval(axes.YDirection().Coord(coordinate + 1));
          const Interval z(axes.Direction().Coord(coordinate + 1));
          localLift[coordinate] = {
              Interval(axes.Location().Coord(coordinate + 1))};
          localLift[coordinate] =
              PolynomialSum(localLift[coordinate],
                            PolynomialProduct({x}, trigonometry.cosine, work));
          localLift[coordinate] =
              PolynomialSum(localLift[coordinate],
                            PolynomialProduct({y}, trigonometry.sine, work));
          localLift[coordinate] = PolynomialSum(
              localLift[coordinate], PolynomialProduct({z}, uv[1], work));
          const Interval error =
              (Interval(std::max(std::abs(x.lo), std::abs(x.hi))) +
               Interval(std::max(std::abs(y.lo), std::abs(y.hi)))) *
              Interval(trigonometry.remainder);
          localRemainder[coordinate] = Interval(-error.hi, error.hi);
        }
        const auto lifted = PolynomialPlaced(localLift, surface.location, work);
        Box residual, centerResidual;
        for (int coordinate = 0; coordinate < 3; ++coordinate) {
          Interval remainder;
          for (int axis = 0; axis < 3; ++axis)
            remainder =
                remainder + Interval(matrix.Value(coordinate + 1, axis + 1)) *
                                localRemainder[axis];
          const auto polynomial =
              PolynomialSum(source[coordinate], lifted[coordinate], -1);
          residual[coordinate] = PolynomialHull(polynomial, work) + remainder;
          Interval center;
          for (auto term = polynomial.rbegin(); term != polynomial.rend();
               ++term)
            center = center * Interval(.5) + *term;
          centerResidual[coordinate] = center + remainder;
        }
        const Box zero{Interval(0), Interval(0), Interval(0)};
        const double upper = DistanceUpper(residual, zero);
        const double lower = DistanceLower(centerResidual, zero);
        if (!std::isfinite(upper) || !std::isfinite(lower))
          throw Standard_Failure("nonfinite cylindrical residual");
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
        throw Standard_Failure("cylindrical subdivision resolution limit");
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
