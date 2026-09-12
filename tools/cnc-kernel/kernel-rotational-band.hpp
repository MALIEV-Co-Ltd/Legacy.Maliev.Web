#pragma once
#include "kernel-native-interpretation-checks.hpp"
#include <BRepAdaptor_Surface.hxx>
#include <BRep_Tool.hxx>
#include <Geom2dAdaptor_Curve.hxx>
#include <TopExp.hxx>
#include <algorithm>
#include <cmath>
#include <emscripten/val.h>
#include <gp_Cone.hxx>
#include <gp_Cylinder.hxx>
#include <gp_Lin2d.hxx>
#include <gp_Torus.hxx>
#include <limits>
#include <set>
#include <string>
#include <vector>

namespace MalievKernel {
namespace RotationalBand {
constexpr double RoundoffFactor = 64;
inline double Guard(double a, double b) {
  return RoundoffFactor * std::numeric_limits<double>::epsilon() *
         std::max(1.0, std::max(std::abs(a), std::abs(b)));
}
inline bool Same(double a, double b) {
  return std::isfinite(a) && std::isfinite(b) && std::abs(a - b) <= Guard(a, b);
}
struct Use {
  TopoDS_Edge edge;
  std::string id, edgeId, wireId, startId, endId;
  Handle(Geom2d_Curve) curve;
  double first = 0, last = 0;
  bool complete = false, seam = false;
};
struct Wire {
  std::string id;
  bool outer = false, complete = false;
  std::vector<Use> uses;
};
struct Segment {
  Use use;
  gp_Pnt2d start, end;
  int constantAxis = 0;
};
struct Side {
  int constantAxis = 0;
  gp_Pnt2d start, end;
  std::vector<Segment> segments;
};
struct Result {
  bool available = false, full = false;
  std::string reason = "not_evaluated", support = "unsupported",
              helical = "unknown", radialPolarity = "unknown";
  int orientation = 0;
  gp_Ax3 frame;
  double radius = 0, minor = 0, angle = 0, u0 = 0, u1 = 0, v0 = 0, v1 = 0,
         radialMin = 0, radialMax = 0, axialMin = 0, axialMax = 0;
  std::vector<Wire> wires;
  std::vector<Side> sides;
  std::vector<std::pair<Use, Use>> seams;
};
inline double Coord(const gp_Pnt2d &p, int axis) {
  return axis == 0 ? p.X() : p.Y();
}
inline bool Junction(const gp_Pnt2d &a, const gp_Pnt2d &b) {
  return Same(a.X(), b.X()) && Same(a.Y(), b.Y());
}
inline Result Evaluate(const TopoDS_Face &original,
                       const std::vector<Wire> &wires, bool complete,
                       bool millimeters) {
  Result r;
  r.wires = wires;
  r.orientation = static_cast<int>(original.Orientation());
  auto fail = [&](const char *why) {
    r.reason = why;
    return r;
  };
  try {
    if (!millimeters)
      return fail("millimeter_units_unverified");
    if (original.IsNull())
      return fail("native_face_unavailable");
    BRepAdaptor_Surface surface(original, Standard_False);
    if (surface.GetType() == GeomAbs_Cylinder) {
      r.support = "cylinder";
      r.frame = surface.Cylinder().Position();
      r.radius = surface.Cylinder().Radius();
    } else if (surface.GetType() == GeomAbs_Cone) {
      r.support = "cone";
      r.frame = surface.Cone().Position();
      r.radius = surface.Cone().RefRadius();
      r.angle = surface.Cone().SemiAngle();
    } else if (surface.GetType() == GeomAbs_Torus) {
      r.support = "torus";
      r.frame = surface.Torus().Position();
      r.radius = surface.Torus().MajorRadius();
      r.minor = surface.Torus().MinorRadius();
    } else
      return fail("unsupported_support");
    TopLoc_Location placement;
    BRep_Tool::Surface(original, placement);
    const auto trsf = placement.Transformation();
    if (trsf.IsNegative() || !Same(trsf.ScaleFactor(), 1) ||
        !r.frame.Direct() ||
        (original.Orientation() != TopAbs_FORWARD &&
         original.Orientation() != TopAbs_REVERSED))
      return fail("invalid_frame_or_placement");
    for (int row = 1; row <= 3; ++row)
      for (int col = 1; col <= 4; ++col)
        if (!std::isfinite(trsf.Value(row, col)))
          return fail("invalid_frame_or_placement");
    for (const auto &d :
         {r.frame.Direction(), r.frame.XDirection(), r.frame.YDirection()})
      if (!std::isfinite(d.X()) || !std::isfinite(d.Y()) ||
          !std::isfinite(d.Z()))
        return fail("invalid_frame_or_placement");
    if (!Same(r.frame.XDirection().Dot(r.frame.YDirection()), 0) ||
        !Same(r.frame.XDirection().Dot(r.frame.Direction()), 0) ||
        !Same(r.frame.YDirection().Dot(r.frame.Direction()), 0))
      return fail("invalid_frame_or_placement");
    if (!std::isfinite(r.radius) ||
        (r.support == "cone" ? r.radius < 0 : r.radius <= 0) ||
        !std::isfinite(r.angle) || !std::isfinite(r.minor))
      return fail("invalid_frame_or_placement");
    if (!complete)
      return fail("incomplete_native_traversal");
    if (wires.size() != 1)
      return fail("additional_boundary_component");
    const auto &wire = wires[0];
    if (!wire.outer || !wire.complete || wire.uses.empty())
      return fail("incomplete_native_traversal");
    std::set<std::string> ids;
    std::vector<Segment> segments;
    TopoDS_Face forward = TopoDS::Face(original.Oriented(TopAbs_FORWARD));
    for (size_t i = 0; i < wire.uses.size(); ++i) {
      const auto &use = wire.uses[i];
      const auto &next = wire.uses[(i + 1) % wire.uses.size()];
      if (!use.complete || use.wireId != wire.id || use.id.empty() ||
          use.edgeId.empty() || !ids.insert(use.id).second ||
          use.endId.empty() || use.endId != next.startId)
        return fail("nonreciprocal_occurrence");
      TopoDS_Vertex end, start;
      TopExp::Vertices(use.edge, start, end, Standard_True);
      TopoDS_Vertex nextStart, nextEnd;
      TopExp::Vertices(next.edge, nextStart, nextEnd, Standard_True);
      if (end.IsNull() || nextStart.IsNull() || !end.IsSame(nextStart))
        return fail("nonreciprocal_occurrence");
      if (BRep_Tool::Degenerated(use.edge))
        return fail(r.support == "cone"
                        ? "singular_cone_band"
                        : "unsupported_periodic_boundary_representation");
      if (!std::isfinite(use.first) || !std::isfinite(use.last) ||
          use.first >= use.last)
        return fail("nonfinite_range");
      if (use.curve.IsNull())
        return fail("unsupported_pcurve_type");
      Geom2dAdaptor_Curve curve(use.curve, use.first, use.last);
      if (curve.GetType() != GeomAbs_Line)
        return fail("unsupported_pcurve_type");
      auto line = curve.Line();
      auto direction = line.Direction();
      if (direction.X() != 0 && direction.Y() != 0) {
        r.helical = "oblique-or-helical";
        return fail("oblique_or_helical_boundary");
      }
      if (!BRep_Tool::SameParameter(use.edge) ||
          !BRep_Tool::SameRange(use.edge))
        return fail("native_check_failed");
      Segment segment;
      segment.use = use;
      segment.constantAxis = direction.X() == 0 ? 0 : 1;
      auto point = [&](double t) {
        return gp_Pnt2d(line.Location().X() + t * direction.X(),
                        line.Location().Y() + t * direction.Y());
      };
      segment.start = point(use.first);
      segment.end = point(use.last);
      if (use.edge.Orientation() == TopAbs_REVERSED)
        std::swap(segment.start, segment.end);
      else if (use.edge.Orientation() != TopAbs_FORWARD)
        return fail("nonreciprocal_occurrence");
      if (!std::isfinite(segment.start.X()) ||
          !std::isfinite(segment.start.Y()) ||
          !std::isfinite(segment.end.X()) || !std::isfinite(segment.end.Y()))
        return fail("nonfinite_range");
      if (Same(Coord(segment.start, 1 - segment.constantAxis),
               Coord(segment.end, 1 - segment.constantAxis)))
        return fail("parameter_boundary_ambiguous");
      segments.push_back(segment);
    }
    r.helical = "none-affine-axis-boundaries";
    for (size_t i = 0; i < segments.size(); ++i)
      if (!Junction(segments[i].end, segments[(i + 1) % segments.size()].start))
        return fail("parameter_boundary_ambiguous");
    // Rotate to an actual corner, preserving native cyclic order and all IDs.
    size_t corner = segments.size();
    for (size_t i = 0; i < segments.size(); ++i)
      if (segments[i].constantAxis !=
          segments[(i + segments.size() - 1) % segments.size()].constantAxis) {
        corner = i;
        break;
      }
    if (corner == segments.size())
      return fail("nonrectangular_cycle");
    std::rotate(segments.begin(), segments.begin() + corner, segments.end());
    for (const auto &segment : segments) {
      if (r.sides.empty() ||
          r.sides.back().constantAxis != segment.constantAxis) {
        Side side;
        side.constantAxis = segment.constantAxis;
        side.start = segment.start;
        side.end = segment.end;
        r.sides.push_back(side);
      } else {
        auto &side = r.sides.back();
        const int a = 1 - side.constantAxis;
        if (!Same(Coord(side.start, side.constantAxis),
                  Coord(segment.start, side.constantAxis)))
          return fail("nonrectangular_cycle");
        if ((Coord(side.end, a) - Coord(side.start, a)) *
                (Coord(segment.end, a) - Coord(segment.start, a)) <=
            0)
          return fail("nonrectangular_cycle");
        side.end = segment.end;
      }
      r.sides.back().segments.push_back(segment);
    }
    if (r.sides.size() != 4)
      return fail("nonrectangular_cycle");
    r.u0 = r.u1 = r.sides[0].start.X();
    r.v0 = r.v1 = r.sides[0].start.Y();
    double twiceArea = 0;
    for (const auto &side : r.sides) {
      r.u0 = std::min(r.u0, side.start.X());
      r.u1 = std::max(r.u1, side.start.X());
      r.v0 = std::min(r.v0, side.start.Y());
      r.v1 = std::max(r.v1, side.start.Y());
      twiceArea += (side.start.X() - r.sides[0].start.X()) *
                       (side.end.Y() - r.sides[0].start.Y()) -
                   (side.end.X() - r.sides[0].start.X()) *
                       (side.start.Y() - r.sides[0].start.Y());
    }
    if (Same(r.u0, r.u1) || Same(r.v0, r.v1))
      return fail("parameter_boundary_ambiguous");
    if (!(twiceArea > 0))
      return fail("nonrectangular_cycle");
    for (size_t i = 0; i < 4; ++i) {
      const auto &a = r.sides[i];
      const auto &b = r.sides[(i + 2) % 4];
      int varying = 1 - a.constantAxis;
      if (a.constantAxis != b.constantAxis ||
          !Same(Coord(a.start, varying), Coord(b.end, varying)) ||
          !Same(Coord(a.end, varying), Coord(b.start, varying)))
        return fail("nonrectangular_cycle");
    }
    const double period = 2 * std::acos(-1.0), width = r.u1 - r.u0;
    if (width > period + Guard(width, period))
      return fail("multiple_period_winding");
    r.full = Same(width, period);
    if (r.full && width != period &&
        std::none_of(segments.begin(), segments.end(),
                     [](const Segment &s) { return s.use.seam; }))
      return fail("parameter_boundary_ambiguous");
    if (r.full) {
      const Side *left = nullptr, *right = nullptr;
      for (const auto &side : r.sides)
        if (side.constantAxis == 0) {
          if (!left)
            left = &side;
          else
            right = &side;
        }
      std::set<size_t> matched;
      for (const auto &a : left->segments) {
        size_t match = right->segments.size();
        int count = 0;
        for (size_t i = 0; i < right->segments.size(); ++i) {
          const auto &b = right->segments[i];
          if (a.use.edge.IsSame(b.use.edge)) {
            ++count;
            match = i;
          }
        }
        if (count != 1 || !matched.insert(match).second)
          return fail("seam_pair_incomplete");
        const auto &b = right->segments[match];
        if (a.use.id == b.use.id || a.use.edgeId != b.use.edgeId ||
            a.use.edge.Orientation() == b.use.edge.Orientation() ||
            !a.use.seam || !b.use.seam ||
            !BRep_Tool::IsClosed(a.use.edge, forward) ||
            a.use.curve == b.use.curve || a.use.first != b.use.first ||
            a.use.last != b.use.last || !Same(a.start.Y(), b.end.Y()) ||
            !Same(a.end.Y(), b.start.Y()) ||
            !Same(std::abs(a.start.X() - b.start.X()), period))
          return fail("seam_pair_incomplete");
        r.seams.push_back({a.use, b.use});
      }
      if (matched.size() != right->segments.size())
        return fail("seam_pair_incomplete");
    } else {
      for (const auto &segment : segments)
        if (segment.use.seam)
          return fail("seam_pair_incomplete");
    }
    if (r.support == "torus" &&
        (r.minor <= 0 || r.radius <= r.minor ||
         r.v1 - r.v0 >= period - Guard(r.v1 - r.v0, period)))
      return fail("unsupported_torus_profile");
    if (r.support == "cylinder") {
      r.radialMin = r.radialMax = r.radius;
      r.axialMin = r.v0;
      r.axialMax = r.v1;
    }
    if (r.support == "cone") {
      double a = r.radius + r.v0 * std::sin(r.angle),
             b = r.radius + r.v1 * std::sin(r.angle);
      r.radialMin = std::min(a, b);
      r.radialMax = std::max(a, b);
      if (r.radialMin <= Guard(r.radius, r.radialMax))
        return fail("singular_cone_band");
      r.axialMin = r.v0 * std::cos(r.angle);
      r.axialMax = r.v1 * std::cos(r.angle);
    }
    double normalMin = 1, normalMax = 1;
    if (r.support == "torus") {
      std::vector<double> points = {r.v0, r.v1};
      const double halfPi = period / 4;
      if (std::abs(r.v0) > 1e12 || std::abs(r.v1) > 1e12)
        return fail("parameter_boundary_ambiguous");
      for (double k = std::ceil(r.v0 / halfPi); k * halfPi < r.v1; ++k)
        points.push_back(k * halfPi);
      r.radialMin = r.axialMin = std::numeric_limits<double>::infinity();
      r.radialMax = r.axialMax = -r.radialMin;
      normalMin = 1;
      normalMax = -1;
      for (double v : points) {
        double c = std::cos(v), radial = r.radius + r.minor * c,
               axial = r.minor * std::sin(v);
        r.radialMin = std::min(r.radialMin, radial);
        r.radialMax = std::max(r.radialMax, radial);
        r.axialMin = std::min(r.axialMin, axial);
        r.axialMax = std::max(r.axialMax, axial);
        normalMin = std::min(normalMin, c);
        normalMax = std::max(normalMax, c);
      }
    }
    if (original.Orientation() == TopAbs_REVERSED) {
      double previous = normalMin;
      normalMin = -normalMax;
      normalMax = -previous;
    }
    r.radialPolarity = normalMin > Guard(normalMin, 0)    ? "outward"
                       : normalMax < -Guard(normalMax, 0) ? "inward"
                                                          : "mixed-or-tangent";
    for (double value : {r.u0, r.u1, r.v0, r.v1, r.radialMin, r.radialMax,
                         r.axialMin, r.axialMax, r.frame.Location().X(),
                         r.frame.Location().Y(), r.frame.Location().Z()})
      if (!std::isfinite(value))
        return fail("nonfinite_range");
    BRepCheck_Analyzer analyzer(forward, Standard_True, Standard_False);
    if (!analyzer.IsValid())
      return fail("native_check_failed");
    const auto checks =
        MalievNativeInterpretation::EvaluateFaceChecks(forward, analyzer);
    if (checks.empty())
      return fail("native_check_failed");
    for (const auto &check : checks)
      if (check.state != MalievNativeInterpretation::NativeCheckState::Passed)
        return fail("native_check_failed");
    r.available = true;
    r.reason.clear();
    return r;
  } catch (const Standard_Failure &) {
    return fail("native_check_failed");
  }
}
inline emscripten::val Export(const Result &r, const std::string &body,
                              const std::string &face) {
  using emscripten::val;
  val o = val::object();
  o.set("schema", std::string("MalievNativeRotationalBand.v1"));
  o.set("methodVersion", 1);
  o.set("bodyId", body);
  o.set("faceId", face);
  o.set("status", std::string(r.available ? "available" : "unavailable"));
  o.set("reason", r.available ? val::null() : val(r.reason));
  o.set("supportType", r.support);
  o.set("units", std::string("millimeter"));
  o.set("coordinateSpace", std::string("import-world"));
  o.set("sourceParameterSpace", std::string("native-surface-uv"));
  o.set("nativeOrientation", r.orientation);
  o.set("helicalBoundaryStatus", r.helical);
  o.set(
      "guarantee",
      std::string("trusted-native-numerical-rectangular-trim-interpretation"));
  o.set(
      "interpretationGate",
      std::string(
          "separate-same-invocation-native-import-and-metric-ledger-required"));
  val precision = val::object();
  precision.set("policy", std::string("affine-axis-roundoff-v1"));
  precision.set("epsilonFactor", RoundoffFactor);
  precision.set("geometricResolutionAllowance", 0);
  precision.set("outwardEnclosure", false);
  precision.set("customerToleranceCertificate", false);
  o.set("precision", precision);
  auto range = [](double a, double b) {
    val v = val::array();
    v.set(0, a);
    v.set(1, b);
    return v;
  };
  auto point = [](const gp_Pnt &p) {
    val v = val::array();
    v.set(0, p.X());
    v.set(1, p.Y());
    v.set(2, p.Z());
    return v;
  };
  auto direction = [](const gp_Dir &p) {
    val v = val::array();
    v.set(0, p.X());
    v.set(1, p.Y());
    v.set(2, p.Z());
    return v;
  };
  val memberships = val::array();
  for (size_t i = 0; i < r.wires.size(); ++i) {
    val w = val::object(), uses = val::array();
    w.set("wireId", r.wires[i].id);
    for (size_t j = 0; j < r.wires[i].uses.size(); ++j)
      uses.set(j, r.wires[i].uses[j].id);
    w.set("orderedCoedgeIds", uses);
    memberships.set(i, w);
  }
  o.set("wireOccurrences", memberships);
  for (const char *field :
       {"frame", "parameters", "uCoverage", "profileInterval", "sides",
        "seamPairs", "boundaryComponents", "polarity"})
    o.set(field, val::null());
  if (!r.available)
    return o;
  val frame = val::object();
  frame.set("origin", point(r.frame.Location()));
  frame.set("axis", direction(r.frame.Direction()));
  frame.set("xDirection", direction(r.frame.XDirection()));
  frame.set("yDirection", direction(r.frame.YDirection()));
  frame.set("placementConvention",
            std::string("located-BRepAdaptor-frame-applied-once"));
  o.set("frame", frame);
  val parameters = val::object();
  parameters.set("referenceRadius", r.radius);
  parameters.set("minorRadius",
                 r.support == "torus" ? val(r.minor) : val::null());
  parameters.set("semiAngleRadians",
                 r.support == "cone" ? val(r.angle) : val::null());
  o.set("parameters", parameters);
  val coverage = val::object();
  coverage.set("kind", std::string(r.full ? "complete-revolution" : "partial"));
  coverage.set("liftedInterval", range(r.u0, r.u1));
  coverage.set("chartShift", 0);
  coverage.set("winding", r.full ? val(1) : val::null());
  const double period = 2 * std::acos(-1.0);
  double start = std::fmod(r.u0, period);
  if (start < 0)
    start += period;
  val intervals = val::array();
  if (r.full)
    intervals.set(0, range(0, period));
  else if (start + r.u1 - r.u0 <= period)
    intervals.set(0, range(start, start + r.u1 - r.u0));
  else {
    intervals.set(0, range(start, period));
    intervals.set(1, range(0, start + r.u1 - r.u0 - period));
  }
  coverage.set("coveredIntervals", intervals);
  o.set("uCoverage", coverage);
  val profile = val::object();
  profile.set("nativeV", range(r.v0, r.v1));
  profile.set("axialMm", range(r.axialMin, r.axialMax));
  profile.set("radialMm", range(r.radialMin, r.radialMax));
  profile.set(
      "method",
      std::string(
          "elementary-endpoints-and-internal-critical-extrema-numerical"));
  o.set("profileInterval", profile);
  val sides = val::array(), boundaries = val::array(), pairs = val::array();
  int bi = 0;
  for (size_t i = 0; i < r.sides.size(); ++i) {
    const auto &side = r.sides[i];
    val s = val::object(), uses = val::array(), edges = val::array(),
        occurrences = val::array();
    s.set("wireId", r.wires[0].id);
    s.set("constantAxis", std::string(side.constantAxis == 0 ? "u" : "v"));
    s.set("startUv", range(side.start.X(), side.start.Y()));
    s.set("endUv", range(side.end.X(), side.end.Y()));
    for (size_t j = 0; j < side.segments.size(); ++j) {
      const auto &use = side.segments[j].use;
      uses.set(j, use.id);
      edges.set(j, use.edgeId);
      val occurrence = val::object();
      occurrence.set("coedgeId", use.id);
      occurrence.set("edgeId", use.edgeId);
      occurrence.set("nativeRange", range(use.first, use.last));
      occurrence.set("orientation", static_cast<int>(use.edge.Orientation()));
      occurrences.set(j, occurrence);
    }
    s.set("orderedCoedgeIds", uses);
    s.set("edgeIds", edges);
    s.set("occurrences", occurrences);
    sides.set(i, s);
    if (!r.full || side.constantAxis == 1) {
      val b = val::object();
      b.set("sideIndex", static_cast<int>(i));
      b.set("kind", std::string(r.full                   ? "ring"
                                : side.constantAxis == 1 ? "arc"
                                                         : "profile-segment"));
      b.set("orderedCoedgeIds", uses);
      b.set("edgeIds", edges);
      b.set("wireId", r.wires[0].id);
      b.set("nativeV",
            side.constantAxis == 1 ? val(side.start.Y()) : val::null());
      b.set("incidenceSource",
            std::string("same-body-finalized-kernelTopology.edges.uses"));
      boundaries.set(bi++, b);
    }
  }
  for (size_t i = 0; i < r.seams.size(); ++i) {
    val p = val::object();
    p.set("firstCoedgeId", r.seams[i].first.id);
    p.set("secondCoedgeId", r.seams[i].second.id);
    p.set("edgeId", r.seams[i].first.edgeId);
    pairs.set(i, p);
  }
  o.set("sides", sides);
  o.set("boundaryComponents", boundaries);
  o.set("seamPairs", pairs);
  val polarity = val::object();
  polarity.set("radial", r.radialPolarity);
  polarity.set("profileOrientationSign",
               r.orientation == TopAbs_REVERSED ? -1 : 1);
  o.set("polarity", polarity);
  return o;
}
} // namespace RotationalBand
} // namespace MalievKernel
