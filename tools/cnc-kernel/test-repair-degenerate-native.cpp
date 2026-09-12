#include "kernel-repair.hpp"
#include <Geom_Plane.hxx>
#include <TColStd_Array2OfReal.hxx>
#include <TColgp_Array2OfPnt.hxx>
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
    Configure(.01, true);
    State face;
    face.surface = new Geom_Plane(gp_Pln(gp::XOY()));
    Pcurve use;
    use.curve = new Geom2d_Line(gp_Pnt2d(0, 0), gp_Dir2d(1, 0));
    use.first = 0;
    use.last = .0001;
    const auto zero = BoundDegenerate(use, face, gp_Pnt(0, 0, 0), .01, 0);
    check(zero.status == "unavailable" && zero.intervals == 0,
          "zero remaining degenerate allowance performs no interval work");
    const auto defaultResult = BoundDegenerate(use, face, gp_Pnt(0, 0, 0), .01);
    const auto lastInterval =
        BoundDegenerate(use, face, gp_Pnt(0, 0, 0), .01, 1);
    check(lastInterval.status == "bounded-within-budget" &&
              lastInterval.intervals == 1 &&
              lastInterval.lower == defaultResult.lower &&
              lastInterval.upper == defaultResult.upper &&
              lastInterval.subdivisions == defaultResult.subdivisions,
          "last available degenerate interval can complete unchanged bound");
    Boundary boundary;
    boundary.maximumIntervals = 65536;
    boundary.totalIntervals = 65535;
    const auto firstNewUse =
        BoundDegenerate(use, face, gp_Pnt(0, 0, 0), .01,
                        boundary.maximumIntervals - boundary.totalIntervals);
    boundary.totalIntervals += firstNewUse.intervals;
    const auto nextNewUse =
        BoundDegenerate(use, face, gp_Pnt(0, 0, 0), .01,
                        boundary.maximumIntervals - boundary.totalIntervals);
    boundary.totalIntervals += nextNewUse.intervals;
    check(firstNewUse.status == "bounded-within-budget" &&
              nextNewUse.status == "unavailable" && nextNewUse.intervals == 0 &&
              boundary.totalIntervals == boundary.maximumIntervals,
          "sequential new degenerate uses cannot regain exhausted aggregate "
          "work");
    Pcurve wide = use;
    wide.last = 1;
    const auto limited = BoundDegenerate(wide, face, gp_Pnt(0, 0, 0), .01, 1);
    check(limited.status == "unavailable" && limited.intervals == 1 &&
              limited.subdivisions == 1,
          "subdivision requiring a second interval stops without off-by-one "
          "work");
    check(BoundDegenerate(use, face, gp_Pnt(0, 0, 0), .01, -1).intervals == 0,
          "negative remaining allowance cannot authorize work");
    Pcurve changed = use;
    changed.curve = new Geom2d_Line(gp_Pnt2d(.02, 0), gp_Dir2d(1, 0));
    const auto rejected =
        BoundDegenerate(changed, face, gp_Pnt(0, 0, 0), .01, 1);
    check(rejected.status == "exceeds-budget" && rejected.intervals == 1 &&
              rejected.lower > .01 && rejected.upper >= rejected.lower,
          "remaining last interval can still prove changed point image");
    for (double origin : {0., 81.}) {
      TColgp_Array2OfPnt poles(1, 3, 1, 10);
      TColStd_Array2OfReal weights(1, 3, 1, 10);
      for (int u = 1; u <= 3; ++u)
        for (int v = 1; v <= 10; ++v) {
          const int distance = origin == 0 ? v - 1 : 10 - v;
          poles(u, v) = gp_Pnt((u - 1) * distance / 18., distance / 9., 0);
          weights(u, v) = u == 2 ? .866025459885 : 1;
        }
      TColStd_Array1OfReal uk(1, 2), vk(1, 2);
      uk(1) = 0; uk(2) = 3.388958684785;
      vk(1) = origin; vk(2) = origin + 3.388958684785;
      TColStd_Array1OfInteger um(1, 2), vm(1, 2);
      um.Init(3); vm.Init(10);
      State collapsed;
      collapsed.surface = new Geom_BSplineSurface(poles, weights, uk, vk, um, vm, 2, 9);
      Pcurve connector;
      connector.curve = new Geom2d_Line(
          gp_Pnt2d(origin == 0 ? 0 : uk(2), origin == 0 ? vk(1) : vk(2)),
          gp_Dir2d(origin == 0 ? 1 : -1, 0));
      connector.first = 0; connector.last = uk(2);
      bool initialUnavailable = false;
      try {
        const auto uv = RepresentedCurveBound(connector.curve).Bounds(0, uk(2));
        RepresentedSurfaceBound(collapsed.surface, TopLoc_Location()).Bounds(uv[0], uv[1]);
      } catch (const Standard_Failure &failure) {
        initialUnavailable = std::string(failure.GetMessageString()) ==
            "represented surface denominator unavailable";
      }
      check(initialUnavailable, "collapsed rational full connector starts with denominator enclosure failure");
      const auto bounded = BoundDegenerate(connector, collapsed, gp_Pnt(), .01);
      check(bounded.status == "bounded-within-budget" && bounded.intervals == 4 &&
                bounded.subdivisions == 0 && bounded.upper <= .01,
            "exact native boundary charges three row poles and proves full image");
      std::cout << "connector origin=" << origin << " range=[" << connector.first
                << ',' << connector.last << "] intervals=" << bounded.intervals
                << " subdivisions=" << bounded.subdivisions << " upper=" << bounded.upper << '\n';
      TColgp_Array1OfPnt2d connectorPoles(1, 2);
      connectorPoles(1) = connector.curve->Value(connector.first);
      connectorPoles(2) = connector.curve->Value(connector.last);
      Pcurve splineConnector = connector;
      splineConnector.curve = new Geom2d_BezierCurve(connectorPoles);
      splineConnector.first = 0; splineConnector.last = 1;
      const auto subdivided = BoundDegenerate(splineConnector, collapsed, gp_Pnt(), .01);
      check(subdivided.status == "bounded-within-budget" && subdivided.subdivisions > 0,
            "non-native-line PC retains bounded denominator recovery subdivision");
      Box ignored;
      const auto noCharge = []() {};
      Pcurve near = connector;
      const auto nativeLine = Handle(Geom2d_Line)::DownCast(connector.curve)->Lin2d();
      near.curve = new Geom2d_Line(gp_Pnt2d(nativeLine.Location().X(),
          std::nextafter(nativeLine.Location().Y(), std::numeric_limits<double>::infinity())),
          nativeLine.Direction());
      check(!ClampedBoundaryImage(near, collapsed, near.first, near.last, ignored, noCharge),
            "near endpoint is never snapped into boundary restriction");
      check(!ClampedBoundaryImage(connector, collapsed, connector.first,
          std::nextafter(connector.last, std::numeric_limits<double>::infinity()), ignored, noCharge),
            "true one-ULP varying extension is not clipped into native hull");
      gp_Trsf placed;
      placed.SetRotation(gp_Ax1(gp_Pnt(), gp_Dir(0, 0, 1)), .7);
      placed.SetTranslationPart(gp_Vec(40, 5, 50));
      State placedFace = collapsed;
      placedFace.geometryLocation = TopLoc_Location(placed);
      check(BoundDegenerate(connector, placedFace, gp_Pnt().Transformed(placed), .01).status ==
                "bounded-within-budget", "boundary hull uses full rotated translated placement");
      auto swapped = Handle(Geom_BSplineSurface)::DownCast(collapsed.surface->Copy());
      swapped->ExchangeUV();
      State swappedFace = collapsed;
      swappedFace.surface = swapped;
      Pcurve swappedPC = connector;
      swappedPC.curve = new Geom2d_Line(
          gp_Pnt2d(nativeLine.Location().Y(), nativeLine.Location().X()),
          gp_Dir2d(nativeLine.Direction().Y(), nativeLine.Direction().X()));
      check(ClampedBoundaryImage(swappedPC, swappedFace, swappedPC.first,
          swappedPC.last, ignored, noCharge), "exact U boundary uses the matching complete V pole row");
      TColStd_Array1OfReal looseKnots(1, 6);
      TColStd_Array1OfInteger looseMults(1, 6);
      for (int i = 1; i <= 6; ++i) looseKnots(i) = i - 1;
      looseMults.Init(1);
      State looseFace = collapsed;
      looseFace.surface = new Geom_BSplineSurface(poles, weights, looseKnots, vk,
          looseMults, vm, 2, 9);
      Pcurve loosePC = connector;
      loosePC.curve = new Geom2d_Line(gp_Pnt2d(0, nativeLine.Location().Y()), gp_Dir2d(1, 0));
      check(!ClampedBoundaryImage(loosePC, looseFace, 1.5, 2.5, ignored, noCharge) &&
                ClampedBoundaryImage(loosePC, looseFace, 2, 3, ignored, noCharge),
            "nonclamped varying axis uses active native domain not exterior stored knots");
      bool invalidWeightRejected = false;
      try {
        auto invalidNative = Handle(Geom_BSplineSurface)::DownCast(collapsed.surface->Copy());
        invalidNative->SetWeight(2, origin == 0 ? 1 : 10, 0);
        State invalidFace = collapsed;
        invalidFace.surface = invalidNative;
        invalidWeightRejected = !ClampedBoundaryImage(connector, invalidFace,
            connector.first, connector.last, ignored, noCharge);
      } catch (const Standard_Failure &) { invalidWeightRejected = true; }
      check(invalidWeightRejected, "nonpositive row weight cannot produce a native hull positive");
      bool invalidLineRejected = false;
      try {
        Pcurve invalidLine = connector;
        invalidLine.curve = new Geom2d_Line(
            gp_Pnt2d(std::numeric_limits<double>::quiet_NaN(), nativeLine.Location().Y()),
            gp_Dir2d(1, 0));
        invalidLineRejected = !ClampedBoundaryImage(invalidLine, collapsed,
            invalidLine.first, invalidLine.last, ignored, noCharge);
      } catch (const Standard_Failure &) { invalidLineRejected = true; }
      check(invalidLineRejected, "nonfinite affine offset cannot admit a boundary hull");
      check(BoundDegenerate(connector, collapsed, gp_Pnt(0, 0, .02), .01).status !=
                "bounded-within-budget", "displaced singular vertex remains nonpositive");
      check(BoundDegenerate(connector, collapsed, gp_Pnt(), .01, 1).status ==
                "unavailable", "failed full enclosure cannot accept exhausted budget");
      auto bulged = Handle(Geom_BSplineSurface)::DownCast(collapsed.surface->Copy());
      bulged->SetPole(2, origin == 0 ? 1 : 10, gp_Pnt(0, 0, .1));
      collapsed.surface = bulged;
      check(BoundDegenerate(connector, collapsed, gp_Pnt(), .01).status !=
                "bounded-within-budget", "noncollapsed middle pole cannot be replaced by vertex");
      // Positive native weights do not license clamping exterior denominator
      // 1 + 2*u - 2*u^2, which has a true zero beyond the native domain.
      for (int u = 1; u <= 3; ++u)
        for (int v = 1; v <= 10; ++v) {
          poles(u, v) = gp_Pnt();
          weights(u, v) = u == 2 ? 2 : 1;
        }
      collapsed.surface = new Geom_BSplineSurface(poles, weights, uk, vk, um, vm, 2, 9);
      connector.curve = new Geom2d_Line(gp_Pnt2d(0, origin), gp_Dir2d(1, 0));
      connector.first = uk(2) * 1.3;
      connector.last = uk(2) * 1.5;
      const auto exterior = BoundDegenerate(connector, collapsed, gp_Pnt(), .01);
      check(exterior.status == "unavailable" && exterior.subdivisions > 0 &&
                exterior.reason == "degenerate image resolution limit",
            "true exterior denominator zero remains unavailable at depth exhaustion");
    }
    EvidenceStore().deadline = std::chrono::steady_clock::now();
    const auto cancelled = BoundDegenerate(use, face, gp_Pnt(0, 0, 0), .01, 1);
    check(cancelled.status == "unavailable" && cancelled.intervals == 0 &&
              cancelled.reason == "assessment-monotonic-time-limit",
          "degenerate allowance does not bypass assessment deadline");
    std::cout << "PASS: " << checks << " degenerate resource checks\n";
    return 0;
  } catch (const Standard_Failure &error) {
    std::cerr << error.GetMessageString() << '\n';
  } catch (const std::exception &error) {
    std::cerr << error.what() << '\n';
  }
  return 1;
}
