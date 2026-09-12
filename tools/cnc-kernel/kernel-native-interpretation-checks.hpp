#pragma once
#include "kernel-native-interpretation-audit.hpp"
#include <BRepCheck_Analyzer.hxx>
#include <BRepCheck_Face.hxx>
#include <BRepCheck_Wire.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Edge.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Iterator.hxx>
#include <TopoDS_Wire.hxx>
#include <functional>
#include <memory>
#include <string>
#include <vector>

namespace MalievNativeInterpretation {
enum class NativeCheckState { NotEvaluated, Passed, Failed, Unavailable };
struct NativeCheckRow {
  std::string method;
  TopoDS_Face face;
  TopoDS_Shape subject;
  TopoDS_Edge offendingFirst, offendingSecond;
  NativeCheckState state = NativeCheckState::NotEvaluated;
  int nativeStatus = -1;
  bool reusedAnalyzerResult = false;
  std::string reason = "not evaluated";
};

// Rows are allocated before cancellation or native calls. They describe native
// interpretation checks, not independent source-region or manufacturing proof.
inline std::vector<NativeCheckRow>
AllocateFaceChecks(const TopoDS_Face &input) {
  const TopoDS_Face face = TopoDS::Face(input.Oriented(TopAbs_FORWARD));
  std::vector<NativeCheckRow> rows;
  auto append = [&](const char *method, const TopoDS_Shape &subject) {
    NativeCheckRow row;
    row.method = method;
    row.face = face;
    row.subject = subject;
    rows.push_back(row);
  };
  append("BRepCheck_Face.IntersectWires", face);
  append("BRepCheck_Face.ClassifyWires", face);
  append("BRepCheck_Face.OrientationOfWires", face);
  append("BRepCheck_Face.IsUnorientable", face);
  for (TopoDS_Iterator it(face); it.More(); it.Next()) {
    if (it.Value().ShapeType() != TopAbs_WIRE)
      continue;
    append("BRepCheck_Wire.Closed", it.Value());
    append("BRepCheck_Wire.Closed2d", it.Value());
    append("BRepCheck_Wire.Orientation", it.Value());
    append("BRepCheck_Wire.SelfIntersect", it.Value());
  }
  return rows;
}
inline std::vector<NativeCheckRow>
EvaluateFaceChecks(const TopoDS_Face &input, const BRepCheck_Analyzer &analyzer,
                   const std::function<bool()> &continues = {},
                   MalievNativeAudit::Audit *audit = nullptr) {
  const TopoDS_Face face = TopoDS::Face(input.Oriented(TopAbs_FORWARD));
  auto rows = AllocateFaceChecks(input);
  Handle(BRepCheck_Face) faceCheck;
  Handle(BRepCheck_Wire) wireCheck;
  bool reusedWire = false;
  bool cancelled = false;
  for (size_t index = 0; index < rows.size(); ++index) {
    NativeCheckRow &row = rows[index];
    cancelled = cancelled || (continues && !continues());
    if (cancelled) {
      row.reason = "assessment cancelled before native check";
      continue;
    }
    try {
      // Record only actually entered method work, never allocated/skipped rows.
      std::unique_ptr<MalievNativeAudit::Scope> auditScope;
      if (audit) auditScope.reset(new MalievNativeAudit::Scope(*audit, row.method));
      BRepCheck_Status status;
      if (index < 4) {
        if (faceCheck.IsNull())
          faceCheck = Handle(BRepCheck_Face)::DownCast(analyzer.Result(input));
        if (faceCheck.IsNull()) {
          row.state = NativeCheckState::Unavailable;
          row.reason = "native face result unavailable";
          continue;
        }
        faceCheck->GeometricControls(Standard_True);
        row.reusedAnalyzerResult = true;
        if (index == 0)
          status = faceCheck->IntersectWires();
        else if (index == 1)
          status = faceCheck->ClassifyWires();
        else if (index == 2)
          status = faceCheck->OrientationOfWires();
        else
          status = faceCheck->IsUnorientable() ? BRepCheck_UnorientableShape
                                               : BRepCheck_NoError;
      } else {
        const size_t method = (index - 4) % 4;
        // The pinned analyzer uses OrientedShapeMapHasher. Reuse only an exact
        // face-relative occurrence; a reversed parent may require another one.
        if (method == 0 || wireCheck.IsNull()) {
          wireCheck.Nullify();
          try {
            wireCheck =
                Handle(BRepCheck_Wire)::DownCast(analyzer.Result(row.subject));
          } catch (const Standard_Failure &) {
            // This oriented occurrence was not present in the analyzer input.
          }
          reusedWire = !wireCheck.IsNull();
          if (wireCheck.IsNull())
            wireCheck = new BRepCheck_Wire(TopoDS::Wire(row.subject));
          wireCheck->GeometricControls(Standard_True);
        }
        row.reusedAnalyzerResult = reusedWire;
        if (method == 0)
          status = wireCheck->Closed();
        else if (method == 1)
          status = wireCheck->Closed2d(face);
        else if (method == 2)
          status = wireCheck->Orientation(face);
        else
          status = wireCheck->SelfIntersect(face, row.offendingFirst,
                                            row.offendingSecond);
      }
      row.nativeStatus = static_cast<int>(status);
      row.state = status == BRepCheck_NoError ? NativeCheckState::Passed
                                              : NativeCheckState::Failed;
      row.reason =
          status == BRepCheck_NoError ? "" : "native check returned failure";
    } catch (const Standard_Failure &error) {
      row.state = NativeCheckState::Unavailable;
      row.reason = error.GetMessageString() ? error.GetMessageString()
                                            : "native check exception";
    }
  }
  return rows;
}
} // namespace MalievNativeInterpretation
