#include "kernel-repair.hpp"
#include <STEPControl_Reader.hxx>
#include <iostream>
#include <stdexcept>
using namespace MalievRepair;
int main(int argc, char **argv) {
  int checks = 0;
  auto check = [&](bool ok, const char *message) {
    ++checks;
    if (!ok) throw std::runtime_error(message);
  };
  try {
    check(argc == 2, "public STEP fixture path required");
    Configure(.01, true);
    Reset();
    STEPControl_Reader reader;
    check(reader.ReadFile(argv[1]) == IFSelect_RetDone, "read exact public source");
    check(reader.TransferRoots() == 1, "one actual source transfer");
    auto &e = EvidenceStore();
    check(e.boundaries.size() == 1, "same transfer exposes one repair boundary");
    const auto &boundary = e.boundaries.front();
    int incomplete = 0;
    for (auto &source : e.sourceBounds) {
      const int fi = Identity(boundary, source.face, false), wi = Identity(boundary, source.wire, false);
      check(fi >= 0 && wi >= 0, "source bound retains exact native face/wire mapping");
      const auto &face = boundary.items[fi], &wire = boundary.items[wi];
      for (const auto &before : face.before.faceLoops)
        if (before.nativeWire.IsSame(wire.original) && !before.complete) {
          ++incomplete;
          check(before.uses.size() == 2, "unmodified face-aware source prefix retains two of four occurrences");
          for (const auto &after : face.after.faceLoops)
            if (after.nativeWire.IsSame(wire.mapped)) {
              check(after.complete && after.uses.size() == 4, "actual repaired final loop is complete");
              check(CheckSourceBoundCycle(boundary, face, wire, source, after).Positive(),
                    "complete source-declared oriented seam cycle must survive pre-repair UV traversal prefix");
              check(source.declarationAttempted && source.declarationComplete && source.declaredCoedges.size() == 4,
                    "actual STEP occurrences, not an audit boolean, back the cycle proof");
              check(!before.complete && SourceFaceCyclesPreserved(boundary, face, .01),
                    "membership gate independently uses declaration without changing before completeness");
              const auto saved = source;
              auto reject = [&](const char *message) {
                check(!CheckSourceBoundCycle(boundary, face, wire, source, after).Positive(), message);
                check(!SourceFaceCyclesPreserved(boundary, face, .01), message);
                source = saved;
              };
              source.declaredCoedges.clear(); reject("missing declaration must fail both actual gates");
              source.declarationComplete = false; reject("incomplete declaration must fail both actual gates");
              source.declaredCoedges[0].forward = !source.declaredCoedges[0].forward; reject("changed declared coedge sense must fail both gates");
              source.declaredCoedges[0].edgeSameSense = !source.declaredCoedges[0].edgeSameSense; reject("changed source edge sense must fail both gates");
              std::swap(source.declaredCoedges[0], source.declaredCoedges[1]); reject("noncyclic source order must fail both gates");
              source.declaredCoedges.erase(source.declaredCoedges.begin() + 1); reject("omitted seam occurrence must fail both gates");
              source.declaredCoedges.push_back(source.declaredCoedges[1]); reject("extra seam occurrence must fail both gates");
              source.declaredCoedges[3] = source.declaredCoedges[1]; reject("same-sense repeated seam must fail both gates");
              source.boundSense = !source.boundSense; reject("changed bound sense must fail both gates");
              source.sourceFaceSense = !source.sourceFaceSense; reject("changed face sense must fail both gates");
              source.insertionParentOrientation = TopAbs_INTERNAL; reject("unknown insertion composition must fail both gates");
              source.declaredCoedges[0].nativeEdgeItem = source.declaredCoedges[1].nativeEdgeItem; reject("wrong native edge association must fail both gates");
              source.declaredCoedges[0].edgeEntity = source.declaredCoedges[1].edgeEntity; reject("wrong source edge association must fail both gates");
              source.declaredCoedges[1].nativeEndItem = source.declaredCoedges[1].nativeStartItem; reject("wrong endpoint binding must fail both gates");
              auto ambiguous = boundary;
              ambiguous.items.push_back(ambiguous.items[source.declaredCoedges[0].nativeEdgeItem]);
              check(!CheckSourceBoundCycle(ambiguous, face, wire, source, after).Positive() &&
                        !SourceFaceCyclesPreserved(ambiguous, face, .01), "ambiguous transfer association fails both gates");
              std::rotate(source.declaredCoedges.begin(), source.declaredCoedges.begin() + 1, source.declaredCoedges.end());
              check(CheckSourceBoundCycle(boundary, face, wire, source, after).Positive() &&
                        SourceFaceCyclesPreserved(boundary, face, .01), "equivalent cyclic declaration start remains supported");
              source = saved;
              auto partial = after; partial.complete = false;
              check(!CheckSourceBoundCycle(boundary, face, wire, source, partial).Positive(), "certified source does not replace complete final traversal");
              auto changed = after; changed.uses[0].second = 1 - changed.uses[0].second;
              check(!CheckSourceBoundCycle(boundary, face, wire, source, changed).Positive(), "certified source does not permit final boundary reversal");
              changed = after; changed.uses.erase(changed.uses.begin() + 1);
              check(!CheckSourceBoundCycle(boundary, face, wire, source, changed).Positive(), "certified source does not permit missing final seam use");
              auto badFace = face; badFace.after.faceLoops = {partial};
              check(!SourceFaceCyclesPreserved(boundary, badFace, .01), "membership also requires complete final traversal");
              const auto audit = e.diagnostics; e.diagnostics = false;
              check(CheckSourceBoundCycle(boundary, face, wire, source, after).Positive() && SourceFaceCyclesPreserved(boundary, face, .01),
                    "optional audit output is not a premise of certification");
              e.diagnostics = audit;
            }
        }
    }
    check(incomplete == 2, "independent public chamfer exposes two affected source loops");
    check(boundary.changedMembership == 0, "actual End source membership gate uses certified declaration cycle");
    check(boundary.requiredMetrics.Complete() && boundary.finalValid,
          "cycle proof never replaces actual complete metric obligations or final validity");
    const auto savedBoundary = boundary;
    const auto savedDeclarations = e.sourceBounds;
    // Isolated immutable-capture fault injection, not another source import or repair.
    // Re-execute the real End assessment against the unchanged final native shape.
    for (int fault = 0; fault < 3; ++fault) {
      Configure(.01, false); Reset();
      auto &run = EvidenceStore(); run.sourceBounds = savedDeclarations;
      Boundary input; input.sourceEntity = savedBoundary.sourceEntity; input.items = savedBoundary.items;
      int edgeId = -1, faceId = -1;
      for (const auto &source : run.sourceBounds)
        if (source.declaredCoedges.size() == 4) {
          faceId = Identity(input, source.face, false);
          edgeId = source.declaredCoedges[0].nativeEdgeItem;
          break;
        }
      check(faceId >= 0 && edgeId >= 0, "fault injection binds a real captured declared boundary");
      if (fault == 0) input.items[edgeId].before.geometry += " changed source curve";
      if (fault == 1) input.items[edgeId].before.first += .1;
      if (fault == 2) {
        auto &prior = input.items[faceId].before.uses[0];
        prior.curve = Handle(Geom2d_Curve)::DownCast(prior.curve->Copy());
        prior.curve->Translate(gp_Vec2d(0, 100));
      }
      run.boundaries.push_back(input); run.current = 0;
      End(savedBoundary.finalShape, Handle(Standard_Transient)());
      const auto &assessed = run.boundaries.front();
      check(assessed.changedMembership == 0 && assessed.finalValid,
            "source cycle and final body remain independently proved under metric fault");
      check(!assessed.requiredMetrics.Complete(), "certified source cycle cannot discharge changed boundary/range/unbounded PC obligation");
      if (fault == 0) check(assessed.changedSourceGeometry > 0, "actual End rejects altered captured source geometry");
      if (fault == 1) check(assessed.changedRanges > 0, "actual End retains source range-change obligation");
    }
    std::cout << "PASS: " << checks << " source-declared cycle native checks\n";
    return 0;
  } catch (const Standard_Failure &error) { std::cerr << error.GetMessageString() << '\n'; }
    catch (const std::exception &error) { std::cerr << error.what() << '\n'; }
  std::cerr << "FAILED after " << checks << " checks\n";
  return 1;
}
