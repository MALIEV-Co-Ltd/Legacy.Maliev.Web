#include "kernel-repair.hpp"
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <Geom_Line.hxx>
#include <iostream>
#include <stdexcept>
using namespace MalievRepair;
int main() {
  int checks = 0;
  auto check = [&](bool condition) {
    ++checks;
    if (!condition)
      throw std::runtime_error("reference metadata regression");
  };
  try {
    Item edge;
    edge.original = BRepBuilderAPI_MakeEdge(gp_Pnt(0, 0, 0), gp_Pnt(1, 0, 0));
    edge.mapped = edge.original;
    edge.before.curve = new Geom_Line(gp_Pnt(0, 0, 0), gp_Dir(1, 0, 0));
    edge.before.geometry = edge.after.geometry = "unchanged native line";
    edge.before.last = 1;
    edge.after.last = 1.004;
    Item face;
    face.original =
        BRepBuilderAPI_MakeFace(gp_Pln(gp_Pnt(0, 0, 0), gp_Dir(0, 0, 1)));
    face.mapped = face.original;
    face.before.geometry = face.after.geometry = "unchanged native plane";
    Boundary first, second;
    first.items = {face, edge};
    second.items = {edge, face};
    PrepareReferenceMetadata(first);
    PrepareReferenceMetadata(second);
    const int firstEdge = Identity(first, edge.original, false);
    const int secondEdge = Identity(second, edge.original, false);
    check(firstEdge == 1 && secondEdge == 0);
    const double a = first.items[firstEdge].rangeDeviationUpper;
    const double b = second.items[secondEdge].rangeDeviationUpper;
    check(a == b && a >= .004 && a < .00400000001);
    check(first.changedRanges == 1 && second.changedRanges == 1);
    check(first.items[0].sourceGeometryUnchanged &&
          second.items[1].sourceGeometryUnchanged);
    const double upperA = ComposePathUpper(.008, a);
    const double upperB = ComposePathUpper(.008, b);
    check(upperA == upperB && upperA > .01);
    check(.008 > RemainingPathBudget(.01, a));
    check(ComposePathUpper(.002, a) == ComposePathUpper(.002, b) &&
          ComposePathUpper(.002, a) < .01);
    PrepareReferenceMetadata(first);
    check(first.changedRanges == 1 &&
          first.items[firstEdge].rangeDeviationUpper == a);
    first.items[firstEdge].after.geometry = "changed native curve";
    PrepareReferenceMetadata(first);
    check(first.changedSourceGeometry == 1 &&
          !first.items[firstEdge].sourceGeometryUnchanged);
    std::cout << "PASS: " << checks << " source metadata ordering checks\n";
    return 0;
  } catch (const Standard_Failure &e) {
    std::cerr << e.GetMessageString() << '\n';
  } catch (const std::exception &e) {
    std::cerr << e.what() << '\n';
  }
  return 1;
}
