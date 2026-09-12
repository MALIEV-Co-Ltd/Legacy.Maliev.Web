#include "kernel-repair-export.hpp"
#include <BRepPrimAPI_MakeBox.hxx>
#include <iostream>
#include <stdexcept>
using namespace MalievRepair;
static Boundary Snapshot(const TopoDS_Shape &shape) {
  Boundary boundary;
  TopTools_IndexedMapOfShape shapes;
  TopExp::MapShapes(shape, shapes);
  for (int i = 1; i <= shapes.Extent(); ++i) {
    Item item;
    item.original = item.mapped = shapes(i);
    boundary.items.push_back(item);
  }
  for (auto &item : boundary.items)
    item.before = Capture(boundary, item.original, false);
  AllocateSourceObligations(boundary);
  return boundary;
}
int main() {
  int checks = 0;
  auto check = [&](bool ok, const char *name) {
    ++checks;
    if (!ok)
      throw std::runtime_error(name);
  };
  try {
    const auto box = BRepPrimAPI_MakeBox(1, 2, 3).Shape();
    auto source = Snapshot(box);
    check(source.requiredMetrics.Counts().required == 24,
          "immutable box allocates every source coedge obligation");
    Configure(.01, false);
    Reset();
    EvidenceStore().boundaries.push_back(source);
    EvidenceStore().current = 0;
    End(BRepPrimAPI_MakeBox(1, 2, 3).Shape(), Handle(Standard_Transient)());
    const auto &failed = EvidenceStore().boundaries.front();
    check(
        !failed.finalCorrespondenceComplete &&
            failed.requiredMetrics.Counts().required == 24 &&
            failed.requiredMetrics.Counts().unassessed == 24,
        "failed real final association retains all unassessed source coedges");
    Configure(.01, false);
    Reset();
    EvidenceStore().boundaries.push_back(Snapshot(box));
    EvidenceStore().current = 0;
    End(box, Handle(Standard_Transient)());
    const auto &complete = EvidenceStore().boundaries.front();
    check(complete.requiredMetrics.Counts().required == 24 &&
              complete.requiredMetrics.Complete(),
          "actual unchanged box End finalizes all source obligations with "
          "complete paths");
    check(EvidenceStore().audit.identityCalls > 0 &&
              EvidenceStore().audit.identityCandidates >=
                  EvidenceStore().audit.identityCalls &&
              EvidenceStore().audit.captureCalls > 0 &&
              EvidenceStore().audit.phases.at("residual-evaluation").calls == 1 &&
              EvidenceStore().audit.phases.at("native-analyzer").calls == 1 &&
              EvidenceStore().audit.marks.count("metric-start") == 1 &&
              EvidenceStore().audit.marks.count("metric-end") == 1 &&
              EvidenceStore().audit.firstCancellationSite.empty(),
          "real box exposes executed phase counters and deadline checkpoints");
    check(complete.tolerances.required == 52 && complete.tolerances.Complete(),
          "all original and final box face edge vertex tolerances observed");
    bool nativePassed =
        complete.nativeAnalyzerExecuted && complete.nativeChecks.size() == 48;
    for (const auto &row : complete.nativeChecks)
      nativePassed =
          nativePassed &&
          row.state == MalievNativeInterpretation::NativeCheckState::Passed;
    check(nativePassed, "actual End reuses analyzer and executes complete "
                        "native face wire predicates");
    const Item *face = nullptr, *wire = nullptr;
    const FaceLoop *finalLoop = nullptr;
    for (const auto &candidate : complete.items)
      if (!face && candidate.original.ShapeType() == TopAbs_FACE &&
          !candidate.before.faceLoops.empty())
        face = &candidate;
    check(face != nullptr, "actual box exposes a captured source face loop");
    if (face) {
      const auto sourceWire = face->before.faceLoops.front().nativeWire;
      const int wireIndex = Identity(complete, sourceWire, false);
      if (wireIndex >= 0)
        wire = &complete.items[wireIndex];
      if (wire)
        for (const auto &candidate : face->after.faceLoops)
          if (!wire->mapped.IsNull() &&
              candidate.nativeWire.IsSame(wire->mapped))
            finalLoop = &candidate;
    }
    check(wire && finalLoop,
          "actual captured source wire has one mapped final loop");
    if (wire && finalLoop) {
      SourceBound declaration;
      declaration.face = face->original;
      declaration.wire = wire->original;
      declaration.faceEntity = 1;
      declaration.boundEntity = 2;
      declaration.loopEntity = 3;
      declaration.loopKind = "StepShape_EdgeLoop";
      declaration.effectiveFaceSense = true;
      declaration.boundSense =
          declaration.wire.Orientation() == TopAbs_FORWARD;
      declaration.insertionParentOrientation =
          declaration.wire.Orientation() ==
                  face->before.faceLoops.front().nativeWire.Orientation()
              ? TopAbs_FORWARD
              : TopAbs_REVERSED;
      BRep_Builder builder;
      TopoDS_Wire reversedWire;
      builder.MakeWire(reversedWire);
      reversedWire.Orientation(TopAbs_REVERSED);
      for (auto edge = face->before.faceLoops.front().edges.rbegin();
           edge != face->before.faceLoops.front().edges.rend(); ++edge)
        builder.Add(reversedWire, *edge);
      auto representedFace = TopoDS::Face(face->mapped.EmptyCopied());
      representedFace.Orientation(TopAbs_FORWARD);
      builder.Add(representedFace, reversedWire);
      const auto representedState = Capture(complete, representedFace, true);
      check(representedState.faceLoops.size() == 1,
            "actual reversed-parent B.Add produces one captured loop");
      auto representedWire = *wire;
      representedWire.mapped = reversedWire;
      const auto &representedReverse = representedState.faceLoops.front();
      const auto representedCheck = CheckSourceBoundCycle(
          complete, *face, representedWire, declaration, representedReverse);
      if (!representedCheck.Positive()) {
        std::cerr << "represented check context=" << representedCheck.supportedContext
                  << " preAdd=" << representedCheck.preAddOrientationMatches
                  << " beforeUnique=" << representedCheck.beforeOccurrenceUnique
                  << " beforeOrientation=" << representedCheck.beforeOrientationMatches
                  << " cycle=" << representedCheck.composedCyclePreserved << '\n';
        for (const auto &use : face->before.faceLoops.front().uses)
          std::cerr << "before " << use.first << ':' << use.second << '\n';
        for (const auto &use : representedReverse.uses)
          std::cerr << "represented " << use.first << ':' << use.second << '\n';
      }
      check(representedReverse.nativeWire.Orientation() == TopAbs_REVERSED &&
                representedCheck.Positive(),
            "actual reversed-parent B.Add preserves the exact composed source "
            "coedge cycle");
      auto invertedBound = declaration;
      invertedBound.boundSense = !invertedBound.boundSense;
      check(!CheckSourceBoundCycle(complete, *face, representedWire, invertedBound,
                                   representedReverse)
                 .Positive(),
            "independently inverted source bound sense remains nonpositive");
      auto invertedCoedge = representedReverse;
      invertedCoedge.uses.front().second =
          invertedCoedge.uses.front().second == TopAbs_FORWARD
              ? TopAbs_REVERSED
              : TopAbs_FORWARD;
      check(!CheckSourceBoundCycle(complete, *face, representedWire, declaration,
                                   invertedCoedge)
                 .Positive(),
            "independently inverted composed coedge remains nonpositive");
      auto incompleteLoop = representedReverse;
      incompleteLoop.complete = false;
      check(!CheckSourceBoundCycle(complete, *face, representedWire, declaration,
                                   incompleteLoop)
                 .Positive(),
            "incomplete native loop traversal remains nonpositive");
    }
    Configure(.01, false);
    Reset();
    EvidenceStore().boundaries.push_back(Snapshot(box));
    EvidenceStore().current = 0;
    EvidenceStore().deadline =
        std::chrono::steady_clock::now() - std::chrono::seconds(1);
    End(box, Handle(Standard_Transient)());
    const auto &cancelled = EvidenceStore().boundaries.front();
    check(cancelled.requiredMetrics.Counts().required == 24 &&
              cancelled.requiredMetrics.Counts().unassessed == 24 &&
              cancelled.requiredMetrics.Counts().evaluated == 0,
          "actual deadline before metric work retains "
          "required/evaluated/unassessed partition");
    bool nativeUnassessed = cancelled.nativeChecks.size() == 48;
    for (const auto &row : cancelled.nativeChecks)
      nativeUnassessed =
          nativeUnassessed &&
          row.state ==
              MalievNativeInterpretation::NativeCheckState::NotEvaluated;
    check(nativeUnassessed,
          "deadline retains all required contextual rows unexecuted");
    check(!EvidenceStore().audit.firstCancellationSite.empty() &&
              EvidenceStore().audit.marks.at("metric-start").remainingMs < 0 &&
              EvidenceStore().audit.phases.at("native-analyzer").calls == 1,
          "expired deadline records its first cancellation without suppressing native analyzer work");
    std::cout << "PASS: " << checks
              << " native interpretation producer checks\n";
    return 0;
  } catch (const Standard_Failure &error) {
    std::cerr << error.GetMessageString() << '\n';
  } catch (const std::exception &error) {
    std::cerr << error.what() << '\n';
  }
  return 1;
}
