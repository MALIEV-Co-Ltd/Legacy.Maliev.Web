#include "kernel-repair-export.hpp"
#include <BRepPrimAPI_MakeBox.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_MakePolygon.hxx>
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeWire.hxx>
#include <BRep_Builder.hxx>
#include <iostream>
#include <stdexcept>
using namespace MalievRepair;
static Boundary Snapshot(const TopoDS_Shape &shape) {
  Boundary boundary;
  TopTools_IndexedMapOfShape shapes;
  TopExp::MapShapes(shape, shapes);
  for (int index = 1; index <= shapes.Extent(); ++index) {
    Item item;
    item.original = item.mapped = shapes(index);
    boundary.items.push_back(item);
  }
  for (auto &item : boundary.items)
    item.before = Capture(boundary, item.original, false);
  return boundary;
}
int main() {
  int checks = 0;
  auto check = [&](bool value, const char *name) {
    ++checks;
    if (!value)
      throw std::runtime_error(name);
  };
  try {
    const TopoDS_Shape source = BRepPrimAPI_MakeBox(1, 2, 3).Shape();
    auto unchanged = Snapshot(source);
    ResolveFinalCorrespondence(unchanged, source);
    check(unchanged.finalCorrespondenceComplete,
          "unchanged final shape has bidirectional evidence coverage");
    auto stale = Snapshot(source);
    const TopoDS_Shape replacement = BRepPrimAPI_MakeBox(1, 2, 3).Shape();
    ResolveFinalCorrespondence(stale, replacement);
    check(!stale.finalCorrespondenceComplete,
          "valid replacement without a context map cannot assess stale handles");
    auto additional = Snapshot(source);
    BRep_Builder builder;
    TopoDS_Compound compound;
    builder.MakeCompound(compound);
    builder.Add(compound, source);
    builder.Add(compound, replacement);
    ResolveFinalCorrespondence(additional, compound);
    check(!additional.finalCorrespondenceComplete,
          "additional final faces cannot disappear from the evidence universe");
    auto omitted = Snapshot(source);
    TopoDS_Compound missingFace;
    builder.MakeCompound(missingFace);
    int faceIndex = 0;
    for (TopExp_Explorer face(source, TopAbs_FACE); face.More(); face.Next())
      if (++faceIndex != 1)
        builder.Add(missingFace, face.Current());
    ResolveFinalCorrespondence(omitted, missingFace);
    check(!omitted.finalCorrespondenceComplete,
          "omitted original face invalidates the full final association");
    auto ambiguous = Snapshot(source);
    for (const auto &item : ambiguous.items)
      if (item.original.ShapeType() == TopAbs_FACE) {
        ambiguous.items.push_back(item);
        break;
      }
    ResolveFinalCorrespondence(ambiguous, source);
    check(!ambiguous.finalCorrespondenceComplete,
          "two evidence faces cannot own one final face");
    Evidence evidence;
    evidence.boundaries.push_back(unchanged);
    int entity = 1;
    for (auto &item : evidence.boundaries[0].items) {
      item.after = Capture(evidence.boundaries[0], item.mapped, true);
      if (item.original.ShapeType() != TopAbs_FACE)
        continue;
      for (const auto &loop : item.before.faceLoops) {
        SourceBound declaration;
        declaration.face = item.original;
        declaration.wire = loop.nativeWire;
        declaration.faceEntity = entity++;
        declaration.boundEntity = entity++;
        declaration.loopEntity = entity++;
        declaration.effectiveFaceSense = true;
        declaration.sourceFaceSense = true;
        declaration.boundSense = loop.nativeWire.Orientation() == TopAbs_FORWARD;
        declaration.outer = true;
        evidence.sourceBounds.push_back(declaration);
      }
    }
    check(CompleteSourceBoundCoverage(evidence),
          "every declared source face-loop maps uniquely to an actual final loop");
    auto dropped = evidence;
    dropped.sourceBounds.pop_back();
    check(!CompleteSourceBoundCoverage(dropped),
          "one dropped declaration cannot disappear from the denominator");
    auto substituted = evidence;
    substituted.sourceBounds.back().wire = substituted.sourceBounds.front().wire;
    check(!CompleteSourceBoundCoverage(substituted),
          "substituting another existing boundary wire is not source coverage");
    auto duplicate = evidence;
    duplicate.sourceBounds.push_back(duplicate.sourceBounds.front());
    check(!CompleteSourceBoundCoverage(duplicate),
          "duplicated captured occurrence cannot cover one actual loop twice");
    auto contradictory = evidence;
    contradictory.sourceBounds.front().boundSense =
        !contradictory.sourceBounds.front().boundSense;
    check(!CompleteSourceBoundCoverage(contradictory),
          "captured bound sense must agree with the declared wire occurrence");
    auto rectangle = [](double low, double high) {
      BRepBuilderAPI_MakePolygon polygon;
      polygon.Add(gp_Pnt(low, low, 0));
      polygon.Add(gp_Pnt(high, low, 0));
      polygon.Add(gp_Pnt(high, high, 0));
      polygon.Add(gp_Pnt(low, high, 0));
      polygon.Close();
      return polygon.Wire();
    };
    const auto outerWire = rectangle(0, 4);
    const auto innerWire = TopoDS::Wire(rectangle(1, 2).Reversed());
    BRepBuilderAPI_MakeFace annulusBuilder(outerWire);
    annulusBuilder.Add(innerWire);
    const auto annulus = annulusBuilder.Face();
    Evidence multiplyMapped;
    multiplyMapped.boundaries.push_back(Snapshot(annulus));
    auto &annularBoundary = multiplyMapped.boundaries[0];
    ResolveFinalCorrespondence(annularBoundary, annulus);
    for (auto &item : annularBoundary.items) {
      item.after = Capture(annularBoundary, item.mapped, true);
      if (item.original.ShapeType() != TopAbs_FACE)
        continue;
      for (const auto &loop : item.before.faceLoops) {
        SourceBound declaration;
        declaration.face = item.original;
        declaration.wire = loop.nativeWire;
        declaration.faceEntity = 1;
        declaration.boundEntity = entity++;
        declaration.loopEntity = entity++;
        declaration.effectiveFaceSense = true;
        declaration.sourceFaceSense = true;
        declaration.boundSense = loop.nativeWire.Orientation() == TopAbs_FORWARD;
        declaration.outer = loop.nativeWire.IsSame(outerWire);
        multiplyMapped.sourceBounds.push_back(declaration);
      }
    }
    check(BRepCheck_Analyzer(annulus).IsValid() &&
              CompleteSourceBoundCoverage(multiplyMapped),
          "valid planar face with outer and inner loops has complete coverage");
    const int outerId = Identity(annularBoundary, outerWire, false);
    const int innerId = Identity(annularBoundary, innerWire, false);
    annularBoundary.items[innerId].mapped = annularBoundary.items[outerId].mapped;
    check(!CompleteSourceBoundCoverage(multiplyMapped),
          "two source wires cannot consume the same post loop and omit another");
    const TopoDS_Edge sharedCircle = BRepBuilderAPI_MakeEdge(
        gp_Circ(gp_Ax2(), 2)).Edge();
    const TopoDS_Wire firstCircleWire = BRepBuilderAPI_MakeWire(sharedCircle).Wire();
    const TopoDS_Wire secondCircleWire = BRepBuilderAPI_MakeWire(
        TopoDS::Edge(sharedCircle.Reversed())).Wire();
    const TopoDS_Face firstCircleFace = BRepBuilderAPI_MakeFace(firstCircleWire).Face();
    const TopoDS_Face secondCircleFace = BRepBuilderAPI_MakeFace(secondCircleWire).Face();
    TopoDS_Compound twoFaces;
    builder.MakeCompound(twoFaces);
    builder.Add(twoFaces, firstCircleFace);
    builder.Add(twoFaces, secondCircleFace);
    auto parentScoped = Snapshot(twoFaces);
    ResolveFinalCorrespondence(parentScoped, twoFaces);
    check(parentScoped.finalCorrespondenceComplete,
          "distinct parent faces disambiguate opposite uses of the same edge");
    auto wrongParent = Snapshot(twoFaces);
    wrongParent.items[Identity(wrongParent, firstCircleWire, false)].mapped =
        secondCircleWire;
    ResolveFinalCorrespondence(wrongParent, twoFaces);
    check(!wrongParent.finalCorrespondenceComplete,
          "an existing mapped wire on the wrong parent face is contradictory");
    const auto rebuiltWire = BRepBuilderAPI_MakeWire(
        TopoDS::Edge(sharedCircle.Reversed())).Wire();
    const auto rebuiltFace = BRepBuilderAPI_MakeFace(rebuiltWire).Face();
    auto reconstructed = Snapshot(firstCircleFace);
    reconstructed.items[Identity(reconstructed, firstCircleFace, false)].mapped =
        rebuiltFace;
    ResolveFinalCorrespondence(reconstructed, rebuiltFace);
    check(reconstructed.finalCorrespondenceComplete &&
              reconstructed.items[Identity(reconstructed, firstCircleWire, false)]
                  .mapped.IsSame(rebuiltWire),
          "reconstructed wire resolves within its mapped parent despite storage reversal");
    TopoDS_Compound repeatedRebuilt;
    builder.MakeCompound(repeatedRebuilt);
    builder.Add(repeatedRebuilt, rebuiltFace);
    builder.Add(repeatedRebuilt, rebuiltWire);
    auto repeatedReconstruction = Snapshot(firstCircleFace);
    repeatedReconstruction.items[Identity(repeatedReconstruction, firstCircleFace, false)]
        .mapped = rebuiltFace;
    ResolveFinalCorrespondence(repeatedReconstruction, repeatedRebuilt);
    check(!repeatedReconstruction.finalCorrespondenceComplete,
          "reconstructed candidate repeated outside its parent remains ambiguous");
    const auto secondRebuiltFace = BRepBuilderAPI_MakeFace(rebuiltWire).Face();
    TopoDS_Compound sharedRebuilt;
    builder.MakeCompound(sharedRebuilt);
    builder.Add(sharedRebuilt, rebuiltFace);
    builder.Add(sharedRebuilt, secondRebuiltFace);
    auto sharedReconstruction = Snapshot(twoFaces);
    sharedReconstruction.items[Identity(sharedReconstruction, firstCircleFace, false)]
        .mapped = rebuiltFace;
    sharedReconstruction.items[Identity(sharedReconstruction, secondCircleFace, false)]
        .mapped = secondRebuiltFace;
    ResolveFinalCorrespondence(sharedReconstruction, sharedRebuilt);
    check(!sharedReconstruction.finalCorrespondenceComplete,
          "rebuilt identity shared by two mapped faces remains unsupported");
    BRepBuilderAPI_MakeFace ambiguousFaceBuilder(rebuiltWire);
    ambiguousFaceBuilder.Add(BRepBuilderAPI_MakeWire(sharedCircle).Wire());
    const auto ambiguousFace = ambiguousFaceBuilder.Face();
    auto sameParentAmbiguous = Snapshot(firstCircleFace);
    sameParentAmbiguous.items[Identity(sameParentAmbiguous, firstCircleFace, false)]
        .mapped = ambiguousFace;
    ResolveFinalCorrespondence(sameParentAmbiguous, ambiguousFace);
    check(!sameParentAmbiguous.finalCorrespondenceComplete,
          "multiple reconstructed wire candidates within one parent remain ambiguous");
    auto wireAmbiguous = Snapshot(source);
    TopoDS_Compound repeatedWire;
    builder.MakeCompound(repeatedWire);
    builder.Add(repeatedWire, source);
    for (TopExp_Explorer wire(source, TopAbs_WIRE); wire.More(); wire.Next()) {
      builder.Add(repeatedWire, wire.Current());
      break;
    }
    ResolveFinalCorrespondence(wireAmbiguous, repeatedWire);
    check(!wireAmbiguous.finalCorrespondenceComplete,
          "ambiguous repeated final wire cannot retain its stale association");
    Configure(.01, false);
    Reset();
    EvidenceStore().boundaries.push_back(Snapshot(source));
    EvidenceStore().current = 0;
    End(replacement, Handle(Standard_Transient)());
    check(EvidenceStore().boundaries[0].finalValid &&
              !EvidenceStore().boundaries[0].finalCorrespondenceComplete &&
              EvidenceStore().boundaries[0].residuals.empty() &&
              EvidenceStore().current == -1,
          "actual End rejects stale evidence without assessing detached handles");
    auto &exportBoundary = EvidenceStore().boundaries[0];
    for (const char *status : {"bounded-within-budget", "exceeds-budget",
                               "unavailable", "unresolved-upper-bound-exceeds-budget"}) {
      Residual residual;
      residual.status = status;
      exportBoundary.residuals.push_back(residual);
    }
    auto provenance = emscripten::val::object();
    auto document = emscripten::val::object();
    document.set("sourceTransfer", emscripten::val::object());
    provenance.set("documentCoverage", document);
    const auto retainedEvidence = EvidenceStore();
    const size_t requiredMetrics = exportBoundary.requiredMetrics.rows.size();
    const size_t requiredChecks = exportBoundary.nativeChecks.size();
    Export(provenance);
    const auto assessment = provenance["repairAssessment"];
    const auto exported = assessment["boundaries"][0];
    check(exported["totalResidualCount"].as<int>() == 4 &&
              exported["boundedResidualCount"].as<int>() == 1 &&
              exported["exceededResidualCount"].as<int>() == 1 &&
              exported["incompleteResidualCount"].as<int>() == 2 &&
              !exported.hasOwnProperty("residuals") &&
              !assessment["eligible"].as<bool>(),
          "compact total reconciles incomplete upper bounds and preserves rejection");
    auto checkMissingDiagnostics = [&]() {
      const auto interpretation = provenance["nativeInterpretation"];
      check(interpretation["status"].as<std::string>() == "review-required" &&
                interpretation["metrics"]["required"].as<size_t>() ==
                    requiredMetrics &&
                interpretation["metrics"]["unassessed"].as<size_t>() ==
                    requiredMetrics &&
                interpretation["nativeChecks"]["required"].as<size_t>() ==
                    requiredChecks &&
                interpretation["diagnostics"]["required"].isNull() &&
                interpretation["diagnostics"]["classified"].isNull() &&
                interpretation["manufacturingEligibility"].as<std::string>() ==
                    "not-assessed" &&
                provenance["repairAssessment"]["boundaries"][0]
                    ["totalResidualCount"].as<int>() == 4,
            "malformed transfer diagnostics retain native obligations and legacy compact repair evidence without acceptance");
    };
    checkMissingDiagnostics();
    for (const char *json : {"null", "{}", "42", "\"invalid\"", "[null]", "[{}]"}) {
      auto transfer = emscripten::val::object();
      transfer.set("diagnostics", emscripten::val::global("JSON").call<emscripten::val>("parse", std::string(json)));
      document.set("sourceTransfer", transfer);
      EvidenceStore() = retainedEvidence;
      Export(provenance);
      checkMissingDiagnostics();
    }
    auto unmatchedTransfer = emscripten::val::global("JSON").call<emscripten::val>(
        "parse", std::string("{\"format\":\"step\",\"failureCount\":0,\"diagnostics\":[{\"diagnosticOccurrenceId\":\"native-diagnostic-0\",\"nativeDeliveryId\":null,\"severity\":\"warning\",\"entityNumber\":1}]}"));
    document.set("sourceTransfer", unmatchedTransfer);
    EvidenceStore() = retainedEvidence;
    Export(provenance);
    const auto unmatched = provenance["nativeInterpretation"];
    check(unmatched["status"].as<std::string>() == "review-required" &&
              unmatched["diagnostics"]["required"].as<int>() == 1 &&
              unmatched["diagnostics"]["classified"].as<int>() == 0 &&
              unmatched["diagnostics"]["unclassified"].as<int>() == 1 &&
              unmatched["diagnostics"]["rows"][0]["nativeDeliveryId"].isNull() &&
              unmatched["metrics"]["required"].as<size_t>() == requiredMetrics,
          "well formed unmatched diagnostic keeps its exact inventory and nonpositive disposition");
    std::cout << "PASS: " << checks << " repair coverage checks\n";
    return 0;
  } catch (const std::exception &error) {
    std::cerr << error.what() << '\n';
  }
  return 1;
}
