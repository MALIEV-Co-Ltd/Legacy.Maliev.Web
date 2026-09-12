#pragma once
#include "kernel-repair-bounds.hpp"
#include <Geom2d_Line.hxx>
#include <Geom_Circle.hxx>
#include <Geom_Line.hxx>

namespace MalievRepair {
// These are same-transfer source occurrences, not reconstructed geometric IDs.
struct ConeRegionUse {
  int edge = -1, startVertex = -1, endVertex = -1, orientation = 0;
  Handle(Geom_Curve) source;
  Handle(Geom2d_Curve) pcurve;
  TopLoc_Location sourceLocation;
  double first = 0, last = 0;
  bool degenerate = false;
  bool sourceVerticesCaptured = false;
  gp_Pnt startPoint, endPoint; // Immutable, already placed source coordinates.
};
struct ConeRegionInput {
  gp_Cone cone;
  TopLoc_Location location;
  std::vector<ConeRegionUse> uses;
  bool mappedSourceOuter = false, sourceCyclePreserved = false;
  bool unchangedSupportAndOrientation = false;
};
struct ConeRegionResult {
  std::string status = "unavailable", reason;
  double collarUpperMm = 0;
  int circleEdge = -1, seamEdge = -1, apexVertex = -1;
};
inline double AbsoluteUpper(Interval value) {
  return std::max(std::abs(value.lo), std::abs(value.hi));
}
inline double NormUpper(const Box &value) {
  return DistanceUpper(value, Box{Interval(0), Interval(0), Interval(0)});
}
inline Box PointBox(const gp_Pnt &point) {
  return {Interval(point.X()), Interval(point.Y()), Interval(point.Z())};
}
inline Box DifferenceBox(const Box &a, const Box &b) {
  return {a[0] - b[0], a[1] - b[1], a[2] - b[2]};
}
inline double MinimumStretchLower(const std::array<Box, 3> &columns) {
  double eigenvalueLower = std::numeric_limits<double>::infinity();
  for (int row = 0; row < 3; ++row) {
    Interval diagonal;
    double offDiagonal = 0;
    for (int column = 0; column < 3; ++column) {
      Interval gram;
      for (int axis = 0; axis < 3; ++axis)
        gram = gram + columns[row][axis] * columns[column][axis];
      if (row == column)
        diagonal = gram;
      else
        offDiagonal = Up(offDiagonal + AbsoluteUpper(gram));
    }
    eigenvalueLower =
        std::min(eigenvalueLower, Down(diagonal.lo - offDiagonal));
  }
  if (!std::isfinite(eigenvalueLower) || eigenvalueLower <= 0)
    throw Standard_Failure("cone placement/frame minimum stretch unavailable");
  return std::max(0., Down(std::sqrt(eigenvalueLower)));
}
inline ConeRegionResult BoundSourceConeCap(const ConeRegionInput &input,
                                           double budget) {
  ConeRegionResult result;
  try {
    if (!std::isfinite(budget) || budget <= 0 || !input.mappedSourceOuter ||
        !input.sourceCyclePreserved || !input.unchangedSupportAndOrientation)
      throw Standard_Failure("missing source outer-cycle authority");
    if (input.uses.size() != 4)
      throw Standard_Failure("cone cap requires four chart occurrences");
    const ConeRegionUse *circleUse = nullptr, *collapsed = nullptr;
    std::vector<const ConeRegionUse *> seamUses;
    for (const auto &use : input.uses) {
      if (!std::isfinite(use.first) || !std::isfinite(use.last) ||
          use.first >= use.last || use.orientation < 0 || use.orientation > 1)
        throw Standard_Failure("invalid cone occurrence range or sense");
      if (!use.sourceVerticesCaptured)
        throw Standard_Failure("source cone vertex positions unavailable");
      if (use.degenerate) {
        if (collapsed)
          throw Standard_Failure("multiple collapsed cone uses");
        collapsed = &use;
      } else if (!Handle(Geom_Circle)::DownCast(use.source).IsNull()) {
        if (circleUse)
          throw Standard_Failure("multiple source latitude circles");
        circleUse = &use;
      } else if (!Handle(Geom_Line)::DownCast(use.source).IsNull()) {
        seamUses.push_back(&use);
      } else {
        throw Standard_Failure("unsupported source cone curve");
      }
      if (Handle(Geom2d_Line)::DownCast(use.pcurve).IsNull())
        throw Standard_Failure("cone chart is not native affine lines");
    }
    if (!circleUse || !collapsed || seamUses.size() != 2 ||
        seamUses[0]->edge < 0 || seamUses[0]->edge != seamUses[1]->edge ||
        seamUses[0]->orientation == seamUses[1]->orientation ||
        circleUse->startVertex < 0 ||
        circleUse->startVertex != circleUse->endVertex ||
        collapsed->startVertex < 0 ||
        collapsed->startVertex != collapsed->endVertex ||
        collapsed->startVertex == circleUse->startVertex)
      throw Standard_Failure("source cone seam/circle/apex ownership mismatch");
    for (const auto *seam : seamUses)
      if (!((seam->startVertex == circleUse->startVertex &&
             seam->endVertex == collapsed->startVertex) ||
            (seam->endVertex == circleUse->startVertex &&
             seam->startVertex == collapsed->startVertex)))
        throw Standard_Failure("source seam does not join circle to apex");
    const auto sourceCircle = Handle(Geom_Circle)::DownCast(circleUse->source);
    const auto circle = sourceCircle->Circ();
    const auto axes = input.cone.Position();
    // Equal placements avoid unproved frame conversion; transformed frames
    // themselves remain supported when both native representations share them.
    if (!circleUse->sourceLocation.IsEqual(input.location) ||
        !seamUses[0]->sourceLocation.IsEqual(input.location) ||
        !seamUses[1]->sourceLocation.IsEqual(input.location))
      throw Standard_Failure(
          "cone source placement correspondence unavailable");
    int axisSense = 0;
    for (int candidate : {-1, 1}) {
      bool equal = true;
      for (int coordinate = 1; coordinate <= 3; ++coordinate)
        equal = equal && circle.Axis().Direction().Coord(coordinate) ==
                             candidate * axes.Direction().Coord(coordinate);
      if (equal)
        axisSense = candidate;
    }
    if (!axisSense || circle.Radius() <= 0)
      throw Standard_Failure("source circle plane is not a cone latitude");
    for (int coordinate = 1; coordinate <= 3; ++coordinate)
      if (circle.XAxis().Direction().Coord(coordinate) !=
              axes.XDirection().Coord(coordinate) ||
          circle.YAxis().Direction().Coord(coordinate) !=
              axisSense * axes.YDirection().Coord(coordinate))
        throw Standard_Failure(
            "source latitude frame phase correspondence unavailable");
    const Interval pi(3.141592653589793, 3.1415926535897936);
    const Interval period = Interval(2) * pi;
    const Interval sourceWidth =
        Interval(circleUse->last) - Interval(circleUse->first);
    if (sourceWidth.lo <= pi.hi || sourceWidth.hi >= 3 * pi.lo)
      throw Standard_Failure("source circle is not a single angular turn");
    const auto latitude =
        Handle(Geom2d_Line)::DownCast(circleUse->pcurve)->Lin2d();
    const auto apexLine =
        Handle(Geom2d_Line)::DownCast(collapsed->pcurve)->Lin2d();
    if (latitude.Direction().Y() != 0 || apexLine.Direction().Y() != 0 ||
        latitude.Direction().X() != axisSense)
      throw Standard_Failure(
          "latitude angular sense disagrees with source circle");
    const Interval sine = Trig(Interval(input.cone.SemiAngle()), false);
    const Interval cosine = Trig(Interval(input.cone.SemiAngle()), true);
    const Interval apexV =
        (Interval(0) - Interval(input.cone.RefRadius())) / sine;
    const Interval latitudeV(latitude.Location().Y());
    const auto signedHeight = latitudeV - apexV;
    const double orientedLatitudeDirection =
        latitude.Direction().X() * (circleUse->orientation == 0 ? 1 : -1);
    if ((signedHeight.lo > 0 && orientedLatitudeDirection >= 0) ||
        (signedHeight.hi < 0 && orientedLatitudeDirection <= 0) ||
        (signedHeight.lo <= 0 && signedHeight.hi >= 0))
      throw Standard_Failure(
          "source outer orientation selects cone complement");
    const Interval radial = Interval(input.cone.RefRadius()) + latitudeV * sine;
    if (radial.lo <= 0 || AbsoluteUpper(latitudeV - apexV) == 0)
      throw Standard_Failure(
          "cone latitude crosses or selects unsupported nappe");
    const auto canonicalCenter = FrameBox(
        axes.Location(), axes.XDirection(), axes.YDirection(), axes.Direction(),
        Interval(0), Interval(0), latitudeV * cosine);
    const double centerError =
        NormUpper(DifferenceBox(PointBox(circle.Location()), canonicalCenter));
    const double radiusError =
        AbsoluteUpper(Interval(circle.Radius()) - radial);
    const double angularError = AbsoluteUpper(sourceWidth - period);
    double sourceCollar = (Interval(centerError) + Interval(radiusError) +
                           Interval(circle.Radius()) * Interval(angularError))
                              .hi;
    const auto firstSeam =
        Handle(Geom2d_Line)::DownCast(seamUses[0]->pcurve)->Lin2d();
    const auto secondSeam =
        Handle(Geom2d_Line)::DownCast(seamUses[1]->pcurve)->Lin2d();
    if (firstSeam.Direction().X() != 0 || secondSeam.Direction().X() != 0)
      throw Standard_Failure("cone seam is not an affine meridian");
    const Interval seamWidth =
        Interval(
            std::max(firstSeam.Location().X(), secondSeam.Location().X())) -
        Interval(std::min(firstSeam.Location().X(), secondSeam.Location().X()));
    if (seamWidth.lo <= pi.hi || seamWidth.hi >= 3 * pi.lo)
      throw Standard_Failure("cone seam pair has zero or multiple wrap");
    double cornerU = 0, cornerV = 0, generatorError = 0;
    const double left =
        std::min(firstSeam.Location().X(), secondSeam.Location().X());
    const Interval right = Interval(left) + period;
    auto canonicalU = [&](const ConeRegionUse &use) {
      const auto line = Handle(Geom2d_Line)::DownCast(use.pcurve)->Lin2d();
      return line.Location().X() == left ? Interval(left) : right;
    };
    for (size_t index = 0; index < input.uses.size(); ++index) {
      const auto &use = input.uses[index];
      const auto &previous = input.uses[(index + 3) % 4];
      const auto &next = input.uses[(index + 1) % 4];
      if (use.endVertex != next.startVertex)
        throw Standard_Failure("oriented cone chart vertex cycle is open");
      const auto line = Handle(Geom2d_Line)::DownCast(use.pcurve)->Lin2d();
      const bool vertical = line.Direction().X() == 0;
      const auto previousLine =
          Handle(Geom2d_Line)::DownCast(previous.pcurve)->Lin2d();
      const auto nextLine = Handle(Geom2d_Line)::DownCast(next.pcurve)->Lin2d();
      if (vertical == (previousLine.Direction().X() == 0) ||
          vertical == (nextLine.Direction().X() == 0))
        throw Standard_Failure(
            "cone chart does not alternate meridians and latitudes");
      for (int endpoint = 0; endpoint < 2; ++endpoint) {
        const double parameter =
            (endpoint == 0) == (use.orientation == 0) ? use.first : use.last;
        const int vertex = endpoint == 0 ? use.startVertex : use.endVertex;
        const Interval targetU =
            vertical ? canonicalU(use)
                     : canonicalU(endpoint == 0 ? previous : next);
        const Interval targetV =
            vertex == collapsed->startVertex ? apexV : latitudeV;
        const Interval actualU =
            Interval(line.Location().X()) +
            Interval(parameter) * Interval(line.Direction().X());
        const Interval actualV =
            Interval(line.Location().Y()) +
            Interval(parameter) * Interval(line.Direction().Y());
        cornerU = std::max(cornerU, AbsoluteUpper(actualU - targetU));
        cornerV = std::max(cornerV, AbsoluteUpper(actualV - targetV));
        const auto referenceRadius =
            Interval(input.cone.RefRadius()) + targetV * sine;
        const auto reference = Placed(
            FrameBox(axes.Location(), axes.XDirection(), axes.YDirection(),
                     axes.Direction(), referenceRadius * Trig(targetU, true),
                     referenceRadius * Trig(targetU, false), targetV * cosine),
            input.location);
        const auto sourceVertex = endpoint == 0 ? use.startPoint : use.endPoint;
        for (int coordinate = 1; coordinate <= 3; ++coordinate)
          if (!std::isfinite(sourceVertex.Coord(coordinate)))
            throw Standard_Failure("nonfinite source cone vertex");
        generatorError = std::max(
            generatorError, DistanceUpper(PointBox(sourceVertex), reference));
        if (!use.degenerate) {
          CurveBound source(use.source, use.sourceLocation);
          generatorError = std::max(
              generatorError,
              DistanceUpper(source.Bounds(parameter, parameter), reference));
        }
      }
    }
    // Native straight meridians and affine chart sides attain their deviation
    // maximum at an endpoint. The chart is deformed to one canonical rectangle,
    // not silently clamped. Its paired sides form a disk after the apex
    // collapse. A Frobenius operator bound accounts for every stored
    // frame/placement coefficient; no assumption that rounded directions have
    // exact norm one.
    Interval frameSquared, placementSquared;
    std::array<Box, 3> frameColumns, placementColumns;
    const auto transform = input.location.Transformation();
    for (int coordinate = 1; coordinate <= 3; ++coordinate) {
      for (const auto &direction :
           {axes.XDirection(), axes.YDirection(), axes.Direction()}) {
        const Interval coefficient(direction.Coord(coordinate));
        frameSquared = frameSquared + coefficient * coefficient;
      }
      for (int column = 1; column <= 3; ++column) {
        const Interval coefficient(transform.Value(coordinate, column));
        placementSquared = placementSquared + coefficient * coefficient;
        placementColumns[column - 1][coordinate - 1] = coefficient;
      }
      frameColumns[0][coordinate - 1] =
          Interval(axes.XDirection().Coord(coordinate));
      frameColumns[1][coordinate - 1] =
          Interval(axes.YDirection().Coord(coordinate));
      frameColumns[2][coordinate - 1] =
          Interval(axes.Direction().Coord(coordinate));
    }
    const Interval frameNorm(Up(std::sqrt(frameSquared.hi)));
    const Interval placementNorm(Up(std::sqrt(placementSquared.hi)));
    const Interval chartError =
        placementNorm * frameNorm *
        (radial * Interval(cornerU) + Interval(cornerV));
    sourceCollar = (placementNorm * frameNorm * Interval(sourceCollar)).hi;
    result.collarUpperMm =
        std::max({sourceCollar, chartError.hi, generatorError});
    // Keep the apex collar strictly separated from the noncollapsed latitude;
    // this excludes a topology-changing collar consuming the whole cap.
    const auto heightInterval = latitudeV - apexV;
    const double heightLower = heightInterval.lo <= 0 && heightInterval.hi >= 0
                                   ? 0
                                   : std::min(std::abs(heightInterval.lo),
                                              std::abs(heightInterval.hi));
    const double clearance = (Interval(std::min(radial.lo, heightLower)) *
                              Interval(MinimumStretchLower(frameColumns)) *
                              Interval(MinimumStretchLower(placementColumns)))
                                 .lo;
    if (!std::isfinite(result.collarUpperMm) || result.collarUpperMm > budget ||
        4 * result.collarUpperMm >= clearance)
      throw Standard_Failure(
          "cone collar exceeds budget or noncollapsed clearance");
    result.circleEdge = circleUse->edge;
    result.seamEdge = seamUses[0]->edge;
    result.apexVertex = collapsed->startVertex;
    result.status = "bounded-source-cone-cap";
  } catch (const Standard_Failure &failure) {
    result.reason = failure.GetMessageString();
  }
  return result;
}
} // namespace MalievRepair
