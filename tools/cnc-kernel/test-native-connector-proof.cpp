#include "kernel-repair-export.hpp"
#include <BRepPrimAPI_MakeSphere.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <TColStd_Array2OfReal.hxx>
#include <TColgp_Array2OfPnt.hxx>
#include <iostream>
#include <stdexcept>
using namespace MalievRepair;
int main(int argc, char **argv) {
  int checks = 0;
  auto check = [&](bool ok, const char *name) {
    ++checks;
    if (!ok) throw std::runtime_error(name);
  };
  try {
    Configure(.01, false);
    Reset();
    TopoDS_Shape sphere = BRepPrimAPI_MakeSphere(1).Shape();
    const bool rationalFixture = argc > 1;
    if (rationalFixture) {
      TColgp_Array2OfPnt poles(1, 3, 1, 10);
      TColStd_Array2OfReal weights(1, 3, 1, 10);
      for (int u = 1; u <= 3; ++u)
        for (int v = 1; v <= 10; ++v) {
          const double t = (v - 1) / 9.;
          poles(u, v) = gp_Pnt((u - 1) * t * (1 - t), t, 0);
          weights(u, v) = u == 2 ? .866025459885 : 1;
        }
      TColStd_Array1OfReal uk(1, 2), vk(1, 2);
      uk(1) = 0; uk(2) = 3.388958684785;
      vk(1) = 81; vk(2) = 84.388958684785;
      TColStd_Array1OfInteger um(1, 2), vm(1, 2);
      um.Init(3); vm.Init(10);
      const Handle(Geom_Surface) patch = new Geom_BSplineSurface(
          poles, weights, uk, vk, um, vm, 2, 9);
      sphere = BRepBuilderAPI_MakeFace(patch, 1e-7).Face().Reversed();
    }
    Boundary source;
    TopTools_IndexedMapOfShape shapes;
    TopExp::MapShapes(sphere, shapes);
    // Synthetic captured source singular-bound fixture: the final sphere is
    // real OCCT topology, while original edge inventory excludes its inserted
    // degenerate connectors. No claim this fixture is a STEP transfer.
    for (int i = 1; i <= shapes.Extent(); ++i) {
      if (shapes(i).ShapeType() == TopAbs_EDGE &&
          BRep_Tool::Degenerated(TopoDS::Edge(shapes(i)))) continue;
      Item item;
      item.original = item.mapped = shapes(i);
      source.items.push_back(item);
    }
    for (auto &item : source.items) {
      item.before = Capture(source, item.original, false);
      if (item.original.ShapeType() != TopAbs_FACE) continue;
      auto &uses = item.before.uses;
      uses.erase(std::remove_if(uses.begin(), uses.end(),
                  [](const Pcurve &use) { return use.edge == -1; }), uses.end());
      for (auto &loop : item.before.faceLoops) {
        for (size_t j = loop.uses.size(); j-- > 0;)
          if (loop.uses[j].first == -1) {
            loop.uses.erase(loop.uses.begin() + j);
            loop.edges.erase(loop.edges.begin() + j);
          }
      }
    }
    AllocateSourceObligations(source);
    EvidenceStore().boundaries.push_back(source);
    EvidenceStore().current = 0;
    End(sphere, Handle(Standard_Transient)());
    const auto complete = EvidenceStore().boundaries.front();
    check(complete.connectorProofs.size() == 2,
          "actual End persists both exact degenerate occurrences");
    if (rationalFixture)
      for (const auto &connector : complete.connectorProofs) {
        const auto &metric = complete.residuals[connector.metric];
        check(metric.intervals == 4 && metric.subdivisions == 0,
              "reversed rational face connector charges complete exact row enclosure");
      }
    for (const auto &proof : complete.connectorProofs)
      check(ConnectorProofValid(complete, proof, .01, true),
            "retained proof joins actual bounded metric and native checks");
    const auto proof = complete.connectorProofs.front();
    const auto &face = complete.items[proof.face];
    const auto &loop = face.after.faceLoops[proof.loop];
    const int wireIndex = Identity(complete, loop.nativeWire, true);
    check(wireIndex >= 0, "connector retains unique original wire join");
    const auto &wire = complete.items[wireIndex];
    SourceBound declaration;
    declaration.face = face.original;
    declaration.wire = wire.original;
    declaration.loopKind = "StepShape_EdgeLoop";
    declaration.effectiveFaceSense = true;
    declaration.boundSense = declaration.wire.Orientation() == TopAbs_FORWARD;
    declaration.insertionParentOrientation =
        declaration.wire.Orientation() ==
                face.before.faceLoops[proof.loop].nativeWire.Orientation()
            ? TopAbs_FORWARD : TopAbs_REVERSED;
    check(CheckSourceBoundCycle(complete, face, wire, declaration, loop).Positive(),
          "final ordered source cycle omits only fully proved connectors");
    auto invalid = complete;
    invalid.connectorProofs.push_back(proof);
    check(!ConnectorProofValid(invalid, proof, .01, true),
          "duplicate proof cannot consume one occurrence twice");
    invalid = complete;
    invalid.items[proof.sourceVertex].after.placement += " moved";
    check(!ConnectorProofValid(invalid, proof, .01, true),
          "moved source vertex invalidates connector permission");
    invalid = complete;
    invalid.nativeChecks.clear();
    check(!ConnectorProofValid(invalid, proof, .01, true),
          "missing native predicates cannot produce final positive proof");
    invalid = complete;
    invalid.requiredMetrics.rows.clear();
    check(!ConnectorProofValid(invalid, proof, .01, true),
          "missing required obligation cannot produce positive proof");
    invalid = complete;
    invalid.residuals[proof.metric].upper = .02;
    check(!ConnectorProofValid(invalid, proof, .01, true),
          "excess metric bound cannot retain connector permission");
    invalid = complete;
    invalid.connectorProofs.clear();
    check(!CheckSourceBoundCycle(invalid, invalid.items[proof.face],
                                invalid.items[wireIndex], declaration,
                                invalid.items[proof.face].after.faceLoops[proof.loop])
               .Positive(),
          "degenerate flag without persisted proof cannot change source cycle");
    invalid = complete;
    invalid.items[proof.face].after.uses[proof.postUse].edge = -2;
    check(!ConnectorProofValid(invalid, proof, .01, true),
          "ambiguous source identity is never a new connector");
    invalid = complete;
    invalid.items[proof.face].after.faceLoops[proof.loop].complete = false;
    check(!ConnectorProofValid(invalid, proof, .01, true),
          "incomplete loop cannot omit a connector");
    auto wrongFace = proof;
    wrongFace.face = proof.sourceVertex;
    check(!ConnectorProofValid(complete, wrongFace, .01, true),
          "wrong face cannot reuse connector proof");
    invalid = complete;
    invalid.residuals[proof.metric].upper = std::numeric_limits<double>::quiet_NaN();
    check(!ConnectorProofValid(invalid, proof, .01, true),
          "unknown bound cannot retain connector permission");
    invalid = complete;
    invalid.items[proof.face].after.uses[proof.postUse].degenerate = false;
    check(!ConnectorProofValid(invalid, proof, .01, true),
          "new nondegenerate edge cannot use connector route");
    Evidence emitted;
    emitted.budget = .01;
    EmittedUse use;
    use.face = TopoDS::Face(face.mapped.Oriented(TopAbs_FORWARD));
    use.wire = proof.nativeWire;
    use.edge = proof.normalizedEdge;
    use.faceId = "face";
    use.wireId = "wire";
    use.coedgeId = "coedge";
    use.edgeId = "edge";
    use.startVertexId = use.endVertexId = "vertex";
    use.complete = true;
    emitted.emittedUses.push_back(use);
    check(InterpretationConnectorUse(emitted, complete, proof) != nullptr,
          "export joins exact current emitted connector occurrence");
    invalid = complete;
    invalid.nativeChecks.clear();
    check(InterpretationConnectorUse(emitted, invalid, proof) == nullptr,
          "export independently rejects absent same-face native checks");
    emitted.emittedUses.push_back(use);
    check(InterpretationConnectorUse(emitted, complete, proof) == nullptr,
          "duplicate emitted occurrence cannot be joined uniquely");
    Evidence compact;
    compact.active = true;
    compact.budget = .01;
    compact.boundaries.push_back(complete);
    auto bound = declaration;
    bound.sourceFaceSense = face.before.orientation == TopAbs_FORWARD;
    compact.sourceBounds.push_back(bound);
    EmittedFace emittedFace;
    emittedFace.face = TopoDS::Face(face.mapped);
    emittedFace.bodyId = "body";
    emittedFace.faceId = "face";
    emittedFace.sourceOccurrenceId = "synthetic-source-face";
    emittedFace.uniqueSource = emittedFace.trimsComplete = emittedFace.adjacencyComplete = true;
    compact.emittedFaces.push_back(emittedFace);
    for (size_t li = 0; li < face.after.faceLoops.size(); ++li) {
      const auto &nativeLoop = face.after.faceLoops[li];
      for (size_t ui = 0; ui < nativeLoop.edges.size(); ++ui) {
        EmittedUse occurrence;
        occurrence.face = TopoDS::Face(face.mapped.Oriented(TopAbs_FORWARD));
        occurrence.wire = nativeLoop.nativeWire;
        occurrence.edge = nativeLoop.edges[ui];
        occurrence.faceId = "face";
        occurrence.wireId = "wire-" + std::to_string(li);
        occurrence.coedgeId = occurrence.wireId + "/coedge-" + std::to_string(ui);
        const int edge = Identity(complete, occurrence.edge, true);
        occurrence.edgeId = edge >= 0 ? "edge-" + std::to_string(edge) : occurrence.coedgeId + "/edge";
        occurrence.startVertexId = "vertex-" + std::to_string(Identity(
            complete, TopExp::FirstVertex(occurrence.edge, true), true));
        occurrence.endVertexId = "vertex-" + std::to_string(Identity(
            complete, TopExp::LastVertex(occurrence.edge, true), true));
        occurrence.complete = true;
        compact.emittedUses.push_back(occurrence);
      }
    }
    auto exportCompact = [&](const Evidence &snapshot) {
      EvidenceStore() = snapshot;
      auto provenance = emscripten::val::object();
      auto coverage = emscripten::val::object();
      coverage.set("status", std::string("complete_supported_single_solid"));
      provenance.set("sourceCoverage", coverage);
      auto document = emscripten::val::object(), transfer = emscripten::val::object();
      transfer.set("format", std::string("step"));
      transfer.set("diagnostics", emscripten::val::array());
      transfer.set("failureCount", 0);
      document.set("sourceTransfer", transfer);
      provenance.set("documentCoverage", document);
      Export(provenance);
      check(!provenance["repairAssessment"]["boundaries"][0].hasOwnProperty("residuals"),
            "actual compact exporter retains compact legacy representation");
      return provenance["nativeInterpretation"];
    };
    const auto positiveExport = exportCompact(compact);
    check(positiveExport["connectorProofs"]["length"].as<int>() == 2 &&
              positiveExport["connectorProofs"][0]["status"].as<std::string>() == "complete" &&
              positiveExport["connectorProofs"][0]["nativeCheckIds"]["length"].as<int>() == 8,
          "compact exporter persists complete connector and native check joins");
    int connectorMetrics = 0, connectorEffects = 0;
    const auto metricRows = positiveExport["metrics"]["rows"];
    for (int i = 0; i < metricRows["length"].as<int>(); ++i)
      if (metricRows[i]["kind"].as<std::string>() == "new-degenerate-connector") {
        check(metricRows[i]["associationStatus"].as<std::string>() == "complete-unique-native-occurrence" &&
                  metricRows[i]["sourceEdgeItemId"].isNull() &&
                  !metricRows[i]["sourceVertexItemId"].isNull() &&
                  !metricRows[i]["connectorProofId"].isNull(),
              "compact connector metric joins vertex proof and emitted coedge");
        ++connectorMetrics;
      }
    const auto effects = positiveExport["effects"];
    for (int i = 0; i < effects["length"].as<int>(); ++i)
      if (effects[i]["kind"].as<std::string>() == "bounded-singular-connector") {
        check(effects[i]["status"].as<std::string>() == "checked" &&
                  effects[i]["coedgeIds"]["length"].as<int>() == 1 &&
                  effects[i]["obligationIds"]["length"].as<int>() == 1 &&
                  effects[i]["nativeCheckIds"]["length"].as<int>() == 8,
              "compact connector effect joins one occurrence and required checks");
        ++connectorEffects;
      }
    check(connectorMetrics == 2 && connectorEffects == 2,
          "every inserted connector retains metric and effect rows");
    for (int mode = 0; mode < 3; ++mode) {
      auto broken = compact;
      if (mode == 0) broken.boundaries[0].connectorProofs.clear();
      if (mode == 1) broken.boundaries[0].connectorProofs.push_back(proof);
      if (mode == 2) broken.boundaries[0].connectorProofs[0].metric = complete.residuals.size();
      const auto rejected = exportCompact(broken);
      check(rejected["status"].as<std::string>() == "review-required" &&
                rejected["metrics"]["required"].as<int>() ==
                    positiveExport["metrics"]["required"].as<int>(),
            "compact exporter preserves inventory for missing duplicate malformed proof");
      bool missingAssociation = false;
      const auto rows = rejected["metrics"]["rows"];
      for (int i = 0; i < rows["length"].as<int>(); ++i)
        if (rows[i]["kind"].as<std::string>() == "new-degenerate-connector" &&
            rows[i]["associationStatus"].as<std::string>() != "complete-unique-native-occurrence")
          missingAssociation = true;
      check(missingAssociation, "malformed connector cannot retain positive exported metric association");
    }
    std::cout << "PASS: " << checks << " native connector proof checks\n";
    return 0;
  } catch (const Standard_Failure &error) {
    std::cerr << error.GetMessageString() << '\n';
  } catch (const std::exception &error) {
    std::cerr << error.what() << '\n';
  }
  return 1;
}
