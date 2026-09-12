#include "kernel-repair.hpp"
#include <BRep_Builder.hxx>
#include <iostream>
#include <stdexcept>
using namespace MalievRepair;
int main() {
  int checks = 0;
  auto check = [&](bool condition, const char *name) {
    ++checks;
    if (!condition)
      throw std::runtime_error(name);
  };
  try {
    const double pi = 3.141592653589793;
    const double height = std::sqrt(2.);
    ConeRegionInput input;
    input.cone = gp_Cone(gp_Ax3(), pi / 4, 1);
    input.mappedSourceOuter = input.sourceCyclePreserved =
        input.unchangedSupportAndOrientation = true;
    ConeRegionUse left;
    left.edge = 1;
    left.startVertex = 10;
    left.endVertex = 11;
    left.last = height;
    left.source = new Geom_Line(gp_Pnt(0, 0, -1), gp_Dir(1, 0, 1));
    left.pcurve = new Geom2d_Line(gp_Pnt2d(0, -height), gp_Dir2d(0, 1));
    ConeRegionUse top;
    top.edge = 2;
    top.startVertex = top.endVertex = 11;
    top.last = 2 * pi;
    top.source = new Geom_Circle(gp_Circ(gp_Ax2(), 1));
    top.pcurve = new Geom2d_Line(gp_Pnt2d(0, 0), gp_Dir2d(1, 0));
    ConeRegionUse right = left;
    right.orientation = 1;
    right.startVertex = 11;
    right.endVertex = 10;
    right.pcurve = new Geom2d_Line(gp_Pnt2d(2 * pi, -height), gp_Dir2d(0, 1));
    ConeRegionUse bottom;
    bottom.degenerate = true;
    bottom.startVertex = bottom.endVertex = 10;
    bottom.orientation = 1;
    bottom.last = 2 * pi;
    bottom.pcurve = new Geom2d_Line(gp_Pnt2d(0, -height), gp_Dir2d(1, 0));
    input.uses = {left, top, right, bottom};
    for (auto &use : input.uses) {
      use.orientation = 1 - use.orientation;
      std::swap(use.startVertex, use.endVertex);
    }
    std::reverse(input.uses.begin(), input.uses.end());
    auto capturePoints = [](ConeRegionInput &cap) {
      for (auto &use : cap.uses) {
        use.sourceVerticesCaptured = true;
        use.startPoint =
            use.startVertex == 10 ? gp_Pnt(0, 0, -1) : gp_Pnt(1, 0, 0);
        use.endPoint = use.endVertex == 10 ? gp_Pnt(0, 0, -1) : gp_Pnt(1, 0, 0);
        use.startPoint.Transform(cap.location.Transformation());
        use.endPoint.Transform(cap.location.Transformation());
      }
    };
    capturePoints(input);
    const auto positive = BoundSourceConeCap(input, .01);
    if (positive.status != "bounded-source-cone-cap")
      std::cerr << positive.reason << '\n';
    check(positive.status == "bounded-source-cone-cap" &&
              positive.collarUpperMm < 1e-10,
          "single source cap with actual vertices");
    auto changed = input;
    std::rotate(changed.uses.begin(), changed.uses.begin() + 1,
                changed.uses.end());
    check(BoundSourceConeCap(changed, .01).status == positive.status,
          "cyclic rotation");
    changed = input;
    gp_Trsf scalePlacement;
    scalePlacement.SetScale(gp_Pnt(0, 0, 0), .001);
    changed.location = TopLoc_Location(scalePlacement);
    for (auto &use : changed.uses)
      use.sourceLocation = changed.location;
    capturePoints(changed);
    check(BoundSourceConeCap(changed, .01).status == positive.status,
          "scaled cap");
    changed.uses[3].source = new Geom_Line(gp_Pnt(.3, 0, -1), gp_Dir(1, 0, 1));
    check(BoundSourceConeCap(changed, .01).status == "unavailable",
          "scaled generator violates clearance");
    changed = input;
    changed.mappedSourceOuter = false;
    check(BoundSourceConeCap(changed, .01).status == "unavailable",
          "missing outer authority");
    changed = input;
    changed.uses[0].startVertex = 99;
    check(BoundSourceConeCap(changed, .01).status == "unavailable",
          "open apex vertex use");
    changed = input;
    changed.uses[0].startVertex = changed.uses[0].endVertex = 99;
    check(BoundSourceConeCap(changed, .01).status == "unavailable",
          "unmatched apex ownership");
    changed = input;
    changed.uses[2].last = 4 * pi;
    check(BoundSourceConeCap(changed, .01).status == "unavailable",
          "double circle turn");
    changed = input;
    changed.uses[1].pcurve =
        new Geom2d_Line(gp_Pnt2d(4 * pi, -height), gp_Dir2d(0, 1));
    check(BoundSourceConeCap(changed, .01).status == "unavailable",
          "double seam wrap");
    changed = input;
    changed.uses[3].source = new Geom_Line(gp_Pnt(.1, 0, -1), gp_Dir(1, 0, 1));
    check(BoundSourceConeCap(changed, .01).status == "unavailable",
          "changed source generator");
    changed = input;
    changed.uses[2].pcurve =
        new Geom2d_Line(gp_Pnt2d(2 * pi, 0), gp_Dir2d(-1, 0));
    check(BoundSourceConeCap(changed, .01).status == "unavailable",
          "wrong latitude parameter sense");
    changed = input;
    for (auto &use : changed.uses) {
      use.orientation = 1 - use.orientation;
      std::swap(use.startVertex, use.endVertex);
      std::swap(use.startPoint, use.endPoint);
    }
    std::reverse(changed.uses.begin(), changed.uses.end());
    check(BoundSourceConeCap(changed, .01).reason ==
              "source outer orientation selects cone complement",
          "complementary source cycle");
    changed = input;
    gp_Trsf placement;
    placement.SetTranslation(gp_Vec(25, -13, 7));
    changed.location = TopLoc_Location(placement);
    for (auto &use : changed.uses)
      use.sourceLocation = changed.location;
    capturePoints(changed);
    check(BoundSourceConeCap(changed, .01).status == positive.status,
          "translated cap");
    check(BoundSourceConeCap(input, std::numeric_limits<double>::quiet_NaN())
                  .status == "unavailable",
          "invalid physical budget");
    changed = input;
    for (auto &use : changed.uses) {
      if (use.startVertex == 10)
        use.startPoint.SetX(.1);
      if (use.endVertex == 10)
        use.endPoint.SetX(.1);
    }
    check(BoundSourceConeCap(changed, .01).status == "unavailable",
          "unchanged IDs displaced apex");
    changed = input;
    changed.location = TopLoc_Location(scalePlacement);
    for (auto &use : changed.uses)
      use.sourceLocation = changed.location;
    capturePoints(changed);
    for (auto &use : changed.uses) {
      if (use.startVertex == 10)
        use.startPoint.SetX(.0003);
      if (use.endVertex == 10)
        use.endPoint.SetX(.0003);
    }
    check(BoundSourceConeCap(changed, .01).status == "unavailable",
          "scaled actual apex violates clearance");
    changed = input;
    changed.uses[2].startPoint.SetY(.1);
    check(BoundSourceConeCap(changed, .01).status == "unavailable",
          "displaced retained circle vertex");

    BRep_Builder builder;
    TopoDS_Wire declaredWire, unrelatedWire;
    builder.MakeWire(declaredWire);
    builder.MakeWire(unrelatedWire);
    declaredWire.Orientation(TopAbs_REVERSED);
    Item face;
    TopoDS_Face nativeFace;
    builder.MakeFace(nativeFace);
    face.original = face.mapped = nativeFace;
    FaceLoop loop;
    loop.nativeWire = declaredWire;
    face.before.faceLoops = face.after.faceLoops = {loop};
    Item wire;
    wire.original = wire.mapped = declaredWire;
    Item otherWire;
    otherWire.original = otherWire.mapped = unrelatedWire;
    Boundary boundary;
    boundary.items = {face, wire, otherWire};
    SourceBound declaration;
    declaration.face = nativeFace;
    declaration.wire = declaredWire;
    declaration.outer = declaration.boundSense = true;
    declaration.sourceFaceSense = declaration.effectiveFaceSense = false;
    auto assessDeclaration = [&](const SourceBound &candidate) {
      auto cap = input;
      cap.mappedSourceOuter = ConeDeclarationMatches(boundary, face, candidate);
      return BoundSourceConeCap(cap, .01).status;
    };
    check(assessDeclaration(declaration) == positive.status,
          "adapter exact declared wire and senses");
    auto badDeclaration = declaration;
    badDeclaration.boundSense = false;
    check(assessDeclaration(badDeclaration) == "unavailable",
          "adapter contradictory bound sense");
    badDeclaration = declaration;
    badDeclaration.sourceFaceSense = true;
    check(assessDeclaration(badDeclaration) == "unavailable",
          "adapter contradictory source face sense");
    badDeclaration = declaration;
    badDeclaration.effectiveFaceSense = true;
    check(assessDeclaration(badDeclaration) == "unavailable",
          "adapter contradictory effective face sense");
    badDeclaration = declaration;
    badDeclaration.wire = unrelatedWire.Oriented(TopAbs_REVERSED);
    check(assessDeclaration(badDeclaration) == "unavailable",
          "adapter wrong wire present in same boundary");
    std::cout << "PASS: " << checks << " source cone region checks\n";
    return 0;
  } catch (const Standard_Failure &failure) {
    std::cerr << failure.GetMessageString() << '\n';
  } catch (const std::exception &failure) {
    std::cerr << failure.what() << '\n';
  }
  return 1;
}
