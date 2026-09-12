#pragma once
#include "kernel-repair-rational-composition.hpp"
namespace MalievRepair {
inline bool HighAxisDegreeCapacity(const SurfaceBound &surface, bool modelClass,
                                   bool exactClass, int sourceDegree) {
  // V >= 6 is a dispatch preference: preserve the existing lower-degree
  // routes, not a mathematical restriction. The admitted source classes stay
  // retained-tail analytic models or exact spline polynomials with cubic PC,
  // and exact degree-6/8 splines with native line PC.
  if ((!modelClass && !exactClass) || !surface.spline ||
      surface.u.degree != 2 || surface.v.degree < 6)
    return false;
  const int liftedDegree = surface.u.degree * (modelClass ? 3 : 1) +
                           surface.v.degree;
  // RationalSurfacePiece allows degree 21; RationalProduct allows degree 24.
  return sourceDegree >= 1 && liftedDegree <= 21 &&
         liftedDegree + sourceDegree <= 24;
}
inline RationalSourceModel HighAxisSourcePiece(const CurveBound &curve,
                                               double first, double last,
                                               const UniPolynomial &parameter,
                                               const CompositionWork &work) {
  if (curve.isSpline || GeomAdaptor_Curve(curve.c3).GetType() == GeomAbs_Circle)
    return RationalSourcePiece(curve, first, last, parameter, work);
  const auto ellipse = GeomAdaptor_Curve(curve.c3).Ellipse();
  const auto trig = BoundAffineTrigPolynomial(parameter, work);
  auto cubic = [](const UniPolynomial &polynomial) {
    return UniPolynomial(polynomial.begin(),
                         polynomial.begin() +
                             std::min<size_t>(4, polynomial.size()));
  };
  auto tail = [&](UniPolynomial polynomial) {
    for (size_t index = 0; index < std::min<size_t>(4, polynomial.size());
         ++index)
      polynomial[index] = Interval(0);
    return PolynomialHull(polynomial, work) +
           Interval(-trig.remainder, trig.remainder);
  };
  const auto sine = cubic(trig.sine), cosine = cubic(trig.cosine);
  const auto sineTail = tail(trig.sine), cosineTail = tail(trig.cosine);
  PolynomialPoint local;
  Box error;
  for (int coordinate = 0; coordinate < 3; ++coordinate) {
    const auto x = Interval(ellipse.MajorRadius()) *
                   Interval(ellipse.XAxis().Direction().Coord(coordinate + 1));
    const auto y = Interval(ellipse.MinorRadius()) *
                   Interval(ellipse.YAxis().Direction().Coord(coordinate + 1));
    local[coordinate] =
        PolynomialSum({Interval(ellipse.Location().Coord(coordinate + 1))},
                      PolynomialSum(PolynomialProduct({x}, cosine, work),
                                    PolynomialProduct({y}, sine, work)));
    error[coordinate] = x * cosineTail + y * sineTail;
  }
  RationalSourceModel model;
  model.polynomial = PolynomialPlaced(local, curve.location, work);
  const auto matrix = curve.location.Transformation();
  for (int coordinate = 0; coordinate < 3; ++coordinate)
    for (int axis = 0; axis < 3; ++axis)
      model.remainder[coordinate] =
          model.remainder[coordinate] +
          Interval(matrix.Value(coordinate + 1, axis + 1)) * error[axis];
  return model;
}
inline Residual BoundHighAxisRationalComposition(
    const CurveBound &curve, const CurveBound &pcurve,
    const SurfaceBound &surface, double first, double last, double budget,
    int limit = 32768, int depthLimit = 32,
    const std::function<bool()> &continues = {}) {
  Residual result;
  CompositionWork work{continues};
  try {
    if (!std::isfinite(first) || !std::isfinite(last) || first >= last ||
        !std::isfinite(budget) || budget <= 0 || limit <= 0 || depthLimit < 0)
      throw Standard_Failure("invalid high-axis policy or domain");
    limit = std::min(limit, 32768);
    depthLimit = std::min(depthLimit, 32);
    const bool analyticSource =
        !curve.c3.IsNull() && !curve.isSpline &&
        (GeomAdaptor_Curve(curve.c3).GetType() == GeomAbs_Circle ||
         GeomAdaptor_Curve(curve.c3).GetType() == GeomAbs_Ellipse);
    const bool affinePC =
        !pcurve.c2.IsNull() && !pcurve.isSpline &&
        Geom2dAdaptor_Curve(pcurve.c2).GetType() == GeomAbs_Line;
    const bool exactClass =
        curve.isSpline &&
        (curve.spline.degree == 6 || curve.spline.degree == 8) && affinePC;
    const bool modelClass =
        (analyticSource || curve.isSpline) &&
        pcurve.isSpline && pcurve.spline.degree == 3;
    if (!HighAxisDegreeCapacity(surface, modelClass, exactClass,
                                analyticSource ? 3 : curve.spline.degree))
      throw Standard_Failure("unsupported high-axis native class");
    const auto native = GeomAdaptor_Surface(surface.surface).BSpline();
    if (native->IsUPeriodic() || native->IsVPeriodic())
      throw Standard_Failure("high-axis surface must be nonperiodic");
    for (const auto *spline : {&curve.spline, &pcurve.spline})
      for (const auto &pole : spline->poles) {
        work.Check();
        if (pole[3].lo != 1 || pole[3].hi != 1)
          throw Standard_Failure("high-axis source or PC rational unsupported");
      }
    for (const auto &row : surface.poles)
      for (const auto &pole : row) {
        work.Check();
        if (!std::isfinite(pole[3].lo) || !std::isfinite(pole[3].hi) ||
            pole[3].lo <= 0)
          throw Standard_Failure("invalid high-axis native weight");
      }
    auto charge = [&]() {
      work.Check();
      if (result.intervals >= limit)
        throw Standard_Failure("high-axis interval resource limit");
      ++result.intervals;
    };
    RepresentedSurfaceBound represented(surface.surface, surface.location);
    // SurfaceJet invokes this exactly before each native tensor cell. Charge
    // those nested evaluations as well as pieces and nominal candidates.
    represented.continueAssessment = [&]() {
      charge();
      return true;
    };
    std::vector<double> cuts{first, last};
    for (const auto *spline : {&curve.spline, &pcurve.spline})
      for (const auto &knot : spline->knots) {
        work.Check();
        if (knot.lo != knot.hi)
          throw Standard_Failure("uncertain high-axis partition knot");
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
      charge();
      const Piece piece = stack.back();
      stack.pop_back();
      const UniPolynomial parameter{
          Interval(piece.first), Interval(piece.last) - Interval(piece.first)};
      bool split = false;
      double upper = 0;
      Box centerHull = EmptyBox();
      try {
        const auto source = HighAxisSourcePiece(curve, piece.first, piece.last,
                                                parameter, work);
        PolynomialPoint uv;
        if (affinePC) {
          const auto line = Geom2dAdaptor_Curve(pcurve.c2).Line();
          for (int coordinate = 0; coordinate < 2; ++coordinate) {
            const Interval direction(line.Direction().Coord(coordinate + 1));
            uv[coordinate] = {Interval(line.Location().Coord(coordinate + 1)) +
                                  direction * parameter[0],
                              direction * parameter[1]};
          }
        } else {
          uv = CurvePiecePolynomial(
              pcurve.spline,
              PieceSpan(pcurve.spline,
                        static_cast<int>(pcurve.spline.poles.size()),
                        piece.first, piece.last),
              parameter, work);
        }
        auto nominalV = uv[1];
        Box corridorError;
        if (modelClass) {
          Interval endpoint(0);
          for (const auto &coefficient : uv[1])
            endpoint = endpoint + coefficient;
          nominalV = {uv[1][0], endpoint - uv[1][0]};
          const auto delta =
              RationalHull(RationalSum(uv[1], nominalV, -1), work);
          const auto actualV = RationalHull(uv[1], work);
          const auto modelV = RationalHull(nominalV, work);
          const Interval corridor(std::min(actualV.lo, modelV.lo),
                                  std::max(actualV.hi, modelV.hi));
          const auto derivative = SurfaceJet(
              represented, Jet(RationalHull(uv[0], work), Interval(0)),
              Jet(corridor, Interval(1)));
          for (int coordinate = 0; coordinate < 3; ++coordinate)
            corridorError[coordinate] =
                Interval(0) - delta * derivative[coordinate].derivative;
        }
        const auto uSpans = RepresentedSpans(surface.u, surface.nu,
                                             RationalHull(uv[0], work), false);
        const auto vSpans = RepresentedSpans(
            surface.v, surface.nv, RationalHull(nominalV, work), false);
        if (uSpans.size() * vSpans.size() > 16)
          split = true;
        else
          for (const auto &u : uSpans) {
            for (const auto &v : vSpans) {
              charge();
              const auto lifted = RationalSurfacePiece(
                  surface, u.index, v.index, uv[0], nominalV, work);
              const auto weight = RationalHull(lifted[3], work);
              const auto centerWeight = RationalCenter(lifted[3]);
              if (weight.lo <= 0 || centerWeight.lo <= 0) {
                split = true;
                break;
              }
              Box residual, center;
              for (int coordinate = 0; coordinate < 3; ++coordinate) {
                const auto numerator =
                    RationalSum(RationalProduct(source.polynomial[coordinate],
                                                lifted[3], work),
                                lifted[coordinate], -1);
                const auto error =
                    source.remainder[coordinate] + corridorError[coordinate];
                residual[coordinate] =
                    RationalHull(numerator, work) / weight + error;
                center[coordinate] =
                    RationalCenter(numerator) / centerWeight + error;
              }
              const double candidateUpper = DistanceUpper(
                  residual, Box{Interval(0), Interval(0), Interval(0)});
              if (!std::isfinite(candidateUpper))
                throw Standard_Failure("nonfinite high-axis residual");
              upper = std::max(upper, candidateUpper);
              Union(centerHull, center);
            }
            if (split)
              break;
          }
      } catch (const Standard_Failure &error) {
        const std::string reason = error.GetMessageString();
        if (reason == "assessment-monotonic-time-limit" ||
            reason == "high-axis interval resource limit")
          throw;
        split = true;
      }
      if (!split) {
        result.lower =
            std::max(result.lower,
                     DistanceLower(centerHull,
                                   Box{Interval(0), Interval(0), Interval(0)}));
        if (result.lower > budget) {
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
        throw Standard_Failure("high-axis subdivision resolution limit");
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
