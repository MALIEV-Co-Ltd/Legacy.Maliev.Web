#include "kernel-face.hpp"
#include <BRepBuilderAPI_Copy.hxx>
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_MakeVertex.hxx>
#include <BRepBuilderAPI_MakeWire.hxx>
#include <BRepLib.hxx>
#include <BRepMesh_IncrementalMesh.hxx>
#include <BRepTools_ReShape.hxx>
#include <BRep_Builder.hxx>
#include <Geom2d_Circle.hxx>
#include <Geom2d_Line.hxx>
#include <Geom_ConicalSurface.hxx>
#include <Geom_CylindricalSurface.hxx>
#include <Geom_ToroidalSurface.hxx>
#include <iostream>
#include <stdexcept>
using namespace MalievKernel;
namespace RB = MalievKernel::RotationalBand;
TopoDS_Face SplitAll(const TopoDS_Face &face) {
  Handle(BRepTools_ReShape) reshape = new BRepTools_ReShape;
  TopTools_IndexedMapOfShape edges;
  TopExp::MapShapes(face, TopAbs_EDGE, edges);
  BRep_Builder builder;
  for (int i = 1; i <= edges.Extent(); ++i) {
    auto edge = TopoDS::Edge(edges(i).Oriented(TopAbs_FORWARD));
    double first, last;
    auto curve = BRep_Tool::Curve(edge, first, last);
    double mid = (first + last) / 2;
    auto vertex = BRepBuilderAPI_MakeVertex(curve->Value(mid)).Vertex();
    TopoDS_Vertex a, b;
    TopExp::Vertices(edge, a, b);
    auto left = TopoDS::Edge(edge.EmptyCopied()),
         right = TopoDS::Edge(edge.EmptyCopied());
    builder.Add(left, a.Oriented(TopAbs_FORWARD));
    builder.Add(left, vertex.Oriented(TopAbs_REVERSED));
    builder.Range(left, first, mid);
    builder.Add(right, vertex.Oriented(TopAbs_FORWARD));
    builder.Add(right, b.Oriented(TopAbs_REVERSED));
    builder.Range(right, mid, last);
    auto wire = BRepBuilderAPI_MakeWire(left, right).Wire();
    reshape->Replace(edge, wire);
  }
  return TopoDS::Face(reshape->Apply(face));
}
TopoDS_Wire Polygon(const Handle(Geom_Surface) & surface,
                    const std::vector<gp_Pnt2d> &points) {
  BRepBuilderAPI_MakeWire wire;
  for (size_t i = 0; i < points.size(); ++i) {
    auto a = points[i], b = points[(i + 1) % points.size()];
    gp_Vec2d vector(a, b);
    Handle(Geom2d_Curve) line = new Geom2d_Line(a, gp_Dir2d(vector));
    wire.Add(
        BRepBuilderAPI_MakeEdge(line, surface, 0, vector.Magnitude()).Edge());
  }
  auto result = wire.Wire();
  BRepLib::BuildCurves3d(result);
  BRepLib::SameParameter(result, 1e-7);
  return result;
}
std::vector<RB::Wire> Gather(const TopoDS_Face &original) {
  TopoDS_Face face = TopoDS::Face(original.Oriented(TopAbs_FORWARD));
  std::vector<RB::Wire> result;
  TopTools_IndexedMapOfShape edges, vertices;
  for (TopoDS_Iterator it(face); it.More(); it.Next()) {
    auto wire = TopoDS::Wire(it.Value());
    RB::Wire w;
    w.id = "body-0/face-0/wire-" + std::to_string(result.size());
    w.outer = wire.IsSame(BRepTools::OuterWire(face));
    w.complete = true;
    for (BRepTools_WireExplorer ex(wire, face); ex.More(); ex.Next()) {
      RB::Use u;
      u.edge = ex.Current();
      u.id = w.id + "/use-" + std::to_string(w.uses.size());
      u.edgeId = "body-0/edge-" + std::to_string(edges.Add(u.edge));
      u.wireId = w.id;
      TopoDS_Vertex a, b;
      TopExp::Vertices(u.edge, a, b, Standard_True);
      u.startId = std::to_string(vertices.Add(a));
      u.endId = std::to_string(vertices.Add(b));
      u.curve = BRep_Tool::CurveOnSurface(u.edge, face, u.first, u.last);
      u.complete = true;
      u.seam = BRep_Tool::IsClosed(u.edge, face);
      w.uses.push_back(u);
    }
    result.push_back(w);
  }
  return result;
}
int main() {
  int count = 0;
  auto check = [&](bool ok, const char *text) {
    ++count;
    if (!ok)
      throw std::runtime_error(text);
  };
  auto near = [&](double a, double b, const char *text) {
    check(std::abs(a - b) < 1e-9, text);
  };
  try {
    const double pi = std::acos(-1.0);
    gp_Ax3 axes(gp::XOY());
    Handle(Geom_Surface) cylinder = new Geom_CylindricalSurface(axes, 5),
                         cone = new Geom_ConicalSurface(axes, .4, 5),
                         negativeCone = new Geom_ConicalSurface(axes, -.4, 5),
                         torus = new Geom_ToroidalSurface(axes, 10, 2);
    auto face = [&](const Handle(Geom_Surface) &surface, double u0, double u1,
                    double v0, double v1) {
      return BRepBuilderAPI_MakeFace(surface, u0, u1, v0, v1, 1e-7).Face();
    };
    auto run = [&](const TopoDS_Face &f) {
      return RB::Evaluate(f, Gather(f), true, true);
    };
    auto full = face(cylinder, 0, 2 * pi, -2, 4);
    auto r = run(full);
    std::cout << "initial " << r.reason << std::endl;
    check(r.available && r.full, "full cylinder available");
    check(r.seams.size() == 1 && r.sides.size() == 4,
          "physical seam pair four sides");
    near(r.axialMin, -2, "negative v origin");
    near(r.radialMax, 5, "radius");
    auto split = SplitAll(full);
    auto splitBand = run(split);
    std::cout << "split " << splitBand.reason << std::endl;
    check(splitBand.available && splitBand.full,
          "split all four sides native positive");
    check(splitBand.seams.size() == 2, "segmented seam bijection");
    for (const auto &side : splitBand.sides)
      check(side.segments.size() == 2, "every side keeps two coedges");
    for (auto surface : {cylinder, cone, negativeCone, torus})
      for (bool complete : {false, true}) {
        auto f = face(surface, 0, complete ? 2 * pi : 2.0, .2, 1.2);
        auto band = run(f);
        check(band.available && band.full == complete,
              "elementary full and partial fixtures");
        auto reverse = run(TopoDS::Face(f.Reversed()));
        check(reverse.available && reverse.full == complete,
              "reversed face retains band");
        auto shifted = Gather(f);
        std::rotate(shifted[0].uses.begin(), shifted[0].uses.begin() + 1,
                    shifted[0].uses.end());
        check(RB::Evaluate(f, shifted, true, true).available,
              "cyclic start invariant");
        auto splitResult = run(SplitAll(f));
        check(splitResult.available && splitResult.full == complete,
              "split elementary rectangle");
      }
    auto c = run(face(cone, 0, 2 * pi, 1, 3));
    check(c.available, "cone two rings");
    near(c.axialMin, std::cos(.4), "cone slant parameter axial regression");
    near(c.radialMax, 5 + 3 * std::sin(.4), "cone endpoint radius");
    auto t = run(face(torus, 0, 2 * pi, .2, 4.2));
    check(t.available, "torus critical intervals");
    near(t.radialMin, 8, "internal radial extremum");
    near(t.axialMax, 2, "internal axial extremum");
    check(t.radialPolarity == "mixed-or-tangent", "mixed torus polarity");
    check(run(face(torus, 0, 2 * pi, pi - .2, pi + .2)).radialPolarity ==
              "inward",
          "torus inward");
    check(run(face(torus, 0, 2 * pi, -.2, .2)).radialPolarity == "outward",
          "torus outward");
    check(!run(face(torus, 0, 2 * pi, 0, 2 * pi)).available,
          "full V torus rejected");
    Handle(Geom_Surface) horn = new Geom_ToroidalSurface(axes, 2, 2);
    check(!run(face(horn, 0, 2 * pi, .1, 1)).available, "horn rejected");
    check(!run(face(cone, 0, 2 * pi, -5 / std::sin(.4), 2)).available,
          "apex connector rejected");
    auto partial = face(cylinder, 5.8, 6.6, -2, 4);
    r = run(partial);
    check(r.available && !r.full, "partial crossing canonical cut");
    auto exported = RB::Export(r, "body-0", "body-0/face-0");
    check(exported["uCoverage"]["coveredIntervals"]["length"].as<int>() == 2,
          "partial cut intervals");
    check(exported["boundaryComponents"]["length"].as<int>() == 4,
          "partial four physical sides");
    gp_Trsf placement;
    placement.SetRotation(gp_Ax1(gp::Origin(), gp::DY()), pi / 2);
    placement.SetTranslationPart(gp_Vec(10, 20, 30));
    r = run(TopoDS::Face(full.Moved(TopLoc_Location(placement))));
    check(r.available, "located native frame");
    near(r.frame.Location().X(), 10, "translation once");
    near(r.frame.Direction().X(), 1, "rotation once");
    // Actual native zero-reference cones: the apex is outside these trims.
    Handle(Geom_Surface) zeroCone = new Geom_ConicalSurface(axes, .4, 0);
    for (bool complete : {false, true}) {
      auto f = face(zeroCone, 0, complete ? 2 * pi : 2, 1, 3);
      auto zero = run(f);
      check(zero.available && zero.full == complete,
            "zero-reference cone finite band available");
      near(zero.radialMin, std::sin(.4), "zero-reference positive trim radius");
      near(zero.axialMin, std::cos(.4), "zero-reference cone axial conversion");
      auto reversed = run(TopoDS::Face(f.Reversed()));
      check(reversed.available && reversed.radialPolarity == "inward",
            "zero-reference cone reversal");
      auto located = run(TopoDS::Face(f.Moved(TopLoc_Location(placement))));
      check(located.available, "zero-reference cone located band");
      near(located.frame.Location().X(), 10, "zero-reference translation once");
      near(located.frame.Direction().X(), 1, "zero-reference rotation once");
      check(!run(face(zeroCone, 0, complete ? 2 * pi : 2, 0, 3)).available,
            "zero-reference apex remains unsupported");
      check(!run(face(zeroCone, 0, complete ? 2 * pi : 2, -1, 3)).available,
            "zero-reference cross-apex remains unsupported");
    }
    auto wires = Gather(full);
    auto originalWires = wires;
    wires[0].uses.push_back(wires[0].uses[0]);
    check(!RB::Evaluate(full, wires, true, true).available,
          "duplicate occurrence rejected");
    wires = originalWires;
    wires[0].uses[0].wireId = "foreign";
    check(RB::Evaluate(full, wires, true, true).reason ==
              "nonreciprocal_occurrence",
          "foreign wire rejected");
    wires = originalWires;
    wires[0].uses[0].endId = "foreign";
    check(!RB::Evaluate(full, wires, true, true).available,
          "broken vertex identity rejected");
    wires = originalWires;
    wires.push_back(wires[0]);
    check(RB::Evaluate(full, wires, true, true).reason ==
              "additional_boundary_component",
          "all extra wires rejected");
    check(!RB::Evaluate(full, originalWires, false, true).available,
          "incomplete traversal rejected");
    check(!RB::Evaluate(full, originalWires, true, false).available,
          "unit gate");
    for (size_t i = 0; i < originalWires[0].uses.size(); ++i)
      if (originalWires[0].uses[i].seam) {
        wires = originalWires;
        wires[0].uses[i].seam = false;
        check(!RB::Evaluate(full, wires, true, true).available,
              "missing seam branch flag");
        wires = originalWires;
        wires[0].uses[i].last -= 1e-6;
        check(!RB::Evaluate(full, wires, true, true).available,
              "mismatched seam range");
      }
    wires = originalWires;
    auto &use = wires[0].uses[0];
    use.curve = new Geom2d_Line(gp_Pnt2d(0, 0), gp_Dir2d(1, 1e-18));
    check(RB::Evaluate(full, wires, true, true).reason ==
              "oblique_or_helical_boundary",
          "nonzero transverse slope never snapped");
    wires = originalWires;
    wires[0].uses[0].first = std::numeric_limits<double>::infinity();
    check(RB::Evaluate(full, wires, true, true).reason == "nonfinite_range",
          "nonfinite range");
    auto gap = run(face(cylinder, 0, 2 * pi - 1e-6, -2, 4));
    check(gap.available && !gap.full,
          "genuine near-full strip remains partial");
    // Captured-occurrence mutations exercise the complete classifier; these
    // do not claim OCCT produced the deliberately malformed representations.
    wires = originalWires;
    const double targetWidth = 2 * pi - RB::Guard(2 * pi, 2 * pi) / 2;
    const double uScale = targetWidth / (2 * pi);
    for (auto &row : wires[0].uses) {
      auto line = Geom2dAdaptor_Curve(row.curve).Line();
      row.curve = new Geom2d_Line(
          gp_Pnt2d(line.Location().X() * uScale, line.Location().Y()),
          line.Direction());
      if (line.Direction().Y() == 0) {
        row.first *= uScale;
        row.last *= uScale;
      }
      row.seam = false;
    }
    check(RB::Evaluate(full, wires, true, true).reason ==
              "parameter_boundary_ambiguous",
          "classifier captured within-guard near-period gap is ambiguous");
    wires = originalWires;
    for (auto &row : wires[0].uses) {
      auto line = Geom2dAdaptor_Curve(row.curve).Line();
      if (line.Direction().Y() == 0) {
        const double v = line.Location().Y();
        row.curve = new Geom2d_Line(
            gp_Pnt2d(line.Location().X(), v + RB::Guard(v, v) / 4),
            line.Direction());
        break;
      }
    }
    check(RB::Evaluate(full, wires, true, true).available,
          "classifier captured affine junction roundoff remains available");
    std::vector<size_t> seamIndices;
    for (size_t i = 0; i < originalWires[0].uses.size(); ++i)
      if (originalWires[0].uses[i].seam)
        seamIndices.push_back(i);
    check(seamIndices.size() == 2,
          "captured seam mutation fixture precondition");
    wires = originalWires;
    auto &distinct = wires[0].uses[seamIndices[0]];
    auto copiedEdge = TopoDS::Edge(distinct.edge.EmptyCopied());
    BRep_Builder edgeBuilder;
    for (TopoDS_Iterator it(distinct.edge); it.More(); it.Next())
      edgeBuilder.Add(copiedEdge, it.Value());
    distinct.edge = copiedEdge;
    distinct.edgeId += "-distinct";
    check(RB::Evaluate(full, wires, true, true).reason ==
              "seam_pair_incomplete",
          "captured coincident distinct seam edge does not pair");
    wires = originalWires;
    wires[0].uses[seamIndices[0]].edge.Orientation(
        wires[0].uses[seamIndices[1]].edge.Orientation());
    check(!RB::Evaluate(full, wires, true, true).available,
          "captured same-direction seam uses rejected");
    wires = originalWires;
    wires[0].uses.push_back(wires[0].uses[seamIndices[0]]);
    check(!RB::Evaluate(full, wires, true, true).available,
          "captured duplicate seam occurrence cannot pair twice");
    BRepBuilderAPI_MakeFace cutout(
        cylinder, Polygon(cylinder, {{0, 0}, {2, 0}, {2, 3}, {0, 3}}),
        Standard_True);
    cutout.Add(TopoDS::Wire(
        Polygon(cylinder, {{.5, 1}, {1, 1}, {1, 2}, {.5, 2}}).Reversed()));
    check(run(cutout.Face()).reason == "additional_boundary_component",
          "actual small inner wire rejected");
    auto notch = BRepBuilderAPI_MakeFace(cylinder,
                                         Polygon(cylinder, {{0, 0},
                                                            {2, 0},
                                                            {2, 1},
                                                            {1.5, 1},
                                                            {1.5, 2},
                                                            {2, 2},
                                                            {2, 3},
                                                            {0, 3}}),
                                         Standard_True)
                     .Face();
    check(!run(notch).available, "actual stepped same envelope rejected");
    auto diagonal =
        BRepBuilderAPI_MakeFace(
            cylinder, Polygon(cylinder, {{0, 0}, {2, 0}, {1.5, 3}, {0, 3}}),
            Standard_True)
            .Face();
    check(run(diagonal).reason == "oblique_or_helical_boundary",
          "actual diagonal native trim");
    wires = originalWires;
    wires[0].uses[0].curve =
        new Geom2d_Circle(gp_Ax2d(gp_Pnt2d(0, 0), gp::DX2d()), 1);
    auto curved = RB::Evaluate(full, wires, true, true);
    check(curved.reason == "unsupported_pcurve_type" &&
              curved.helical == "unknown",
          "curved pcurve unknown helical status");
    check(RB::Same(1, std::nextafter(1.0, 2.0)),
          "positive arithmetic roundoff policy");
    check(!RB::Same(0, 1e-10), "real gap outside roundoff policy");
    BRepMesh_IncrementalMesh coarse(full, .5);
    auto before = run(full);
    BRepMesh_IncrementalMesh fine(full, .01);
    auto after = run(full);
    check(before.available && after.available && before.u0 == after.u0 &&
              before.u1 == after.u1 && before.axialMin == after.axialMin,
          "native band independent of deflection");
    ExportContext context(0);
    val out = val::object();
    out.set("bodyId", std::string("body-0"));
    out.set("faceId", std::string("body-0/face-0"));
    context.WriteTrims(full, out, true);
    val mesh = val::object();
    context.Finish(mesh);
    auto band = out["nativeRotationalBand"];
    check(band["status"].as<std::string>() == "available",
          "live exporter hook positive");
    check(band["boundaryComponents"]["length"].as<int>() == 2,
          "seam not physical ring");
    check(band["wireOccurrences"][0]["orderedCoedgeIds"]["length"].as<int>() ==
              4,
          "complete occurrence consumption");
    auto unavailable = RB::Export(RB::Result(), "body-2", "body-2/face-3");
    check(unavailable["sides"].isNull() &&
              unavailable["faceId"].as<std::string>() == "body-2/face-3",
          "unavailable IDs and null geometry");
    std::cout << "PASS " << count << " native rotational band checks"
              << std::endl;
    return 0;
  } catch (const Standard_Failure &e) {
    std::cerr << "FAIL native " << e.GetMessageString() << std::endl;
    return 1;
  } catch (const std::exception &e) {
    std::cerr << "FAIL after " << count << ": " << e.what() << std::endl;
    return 1;
  }
}
