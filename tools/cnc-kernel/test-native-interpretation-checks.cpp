#include "kernel-native-interpretation-checks.hpp"
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_MakePolygon.hxx>
#include <BRep_Builder.hxx>
#include <Geom_Plane.hxx>
#include <TopoDS_Compound.hxx>
#include <gp_Pln.hxx>
#include <iostream>
#include <stdexcept>
using namespace MalievNativeInterpretation;
int main() {
  int checks = 0;
  auto check = [&](bool condition, const char *name) {
    ++checks;
    if (!condition)
      throw std::runtime_error(name);
  };
  try {
    const TopoDS_Face face =
        BRepBuilderAPI_MakeFace(gp_Pln(gp::XOY()), 0, 10, 0, 10).Face();
    BRepCheck_Analyzer analyzer(face, Standard_True);
    const auto passed = EvaluateFaceChecks(face, analyzer);
    check(passed.size() == 8,
          "one face and one wire allocate all eight native checks");
    bool allPassed = true;
    for (const auto &row : passed)
      allPassed = allPassed && row.state == NativeCheckState::Passed &&
                  row.nativeStatus == 0;
    check(allPassed,
          "valid planar face executes contextual native checks successfully");
    bool reused = true;
    for (const auto &row : passed)
      reused = reused && row.reusedAnalyzerResult;
    check(reused, "exact oriented analyzer face and wire results are reused");
    const auto skipped =
        EvaluateFaceChecks(face, analyzer, []() { return false; });
    bool allSkipped = skipped.size() == passed.size();
    for (const auto &row : skipped)
      allSkipped = allSkipped && row.state == NativeCheckState::NotEvaluated &&
                   row.nativeStatus == -1;
    check(
        allSkipped,
        "deadline preserves required rows without initialized native success");
    int visits = 0;
    const auto cancelled =
        EvaluateFaceChecks(face, analyzer, [&]() { return ++visits != 3; });
    bool remainsCancelled = cancelled.size() == 8;
    for (size_t i = 0; i < cancelled.size(); ++i)
      remainsCancelled =
          remainsCancelled &&
          cancelled[i].state == (i < 2 ? NativeCheckState::Passed
                                       : NativeCheckState::NotEvaluated);
    check(remainsCancelled,
          "cancellation is latched and cannot resume partial checks");

    auto allSuccess = [](const std::vector<NativeCheckRow> &rows) {
      for (const auto &row : rows)
        if (row.state != NativeCheckState::Passed)
          return false;
      return !rows.empty();
    };
    auto polygon = [](bool reversed) {
      BRepBuilderAPI_MakePolygon loop;
      loop.Add(gp_Pnt(2, 2, 0));
      loop.Add(gp_Pnt(4, 2, 0));
      loop.Add(gp_Pnt(4, 4, 0));
      loop.Add(gp_Pnt(2, 4, 0));
      loop.Close();
      TopoDS_Wire wire = loop.Wire();
      if (reversed)
        wire.Reverse();
      return wire;
    };
    BRepBuilderAPI_MakeFace withHole(gp_Pln(gp::XOY()), 0, 10, 0, 10);
    withHole.Add(polygon(true));
    const TopoDS_Face holed = withHole.Face();
    BRepCheck_Analyzer holeAnalyzer(holed, Standard_True);
    const auto holeRows = EvaluateFaceChecks(holed, holeAnalyzer);
    check(holeRows.size() == 12 && allSuccess(holeRows),
          "actual retained inner hole executes all twelve native checks");
    BRepBuilderAPI_MakeFace wrongHole(gp_Pln(gp::XOY()), 0, 10, 0, 10);
    wrongHole.Add(polygon(false));
    BRepCheck_Analyzer wrongAnalyzer(wrongHole.Face(), Standard_True);
    const auto wrongRows = EvaluateFaceChecks(wrongHole.Face(), wrongAnalyzer);
    check(wrongRows[2].state == NativeCheckState::Failed,
          "actual wrong inner-wire orientation returns native failure");
    const TopoDS_Face reversed = TopoDS::Face(holed.Reversed());
    BRepCheck_Analyzer reversedAnalyzer(reversed, Standard_True);
    const auto reversedRows = EvaluateFaceChecks(reversed, reversedAnalyzer);
    for (const auto &row : reversedRows)
      if (row.state != NativeCheckState::Passed)
        std::cerr << row.method << " status=" << row.nativeStatus
                  << " reason=" << row.reason << '\n';
    check(allSuccess(reversedRows),
          "reversed owning face checks use consistent forward-parent wires");
    check(reversedRows[0].reusedAnalyzerResult &&
              !reversedRows[4].reusedAnalyzerResult,
          "reversed parent reuses face but constructs missing forward wire "
          "occurrence");
    TopoDS_Compound both;
    BRep_Builder bothBuilder;
    bothBuilder.MakeCompound(both);
    bothBuilder.Add(both, holed);
    bothBuilder.Add(both, reversed);
    BRepCheck_Analyzer bothAnalyzer(both, Standard_True);
    const auto bothForward = EvaluateFaceChecks(holed, bothAnalyzer);
    const auto bothReverse = EvaluateFaceChecks(reversed, bothAnalyzer);
    check(allSuccess(bothForward) && allSuccess(bothReverse) &&
              bothForward[4].reusedAnalyzerResult &&
              bothReverse[4].reusedAnalyzerResult,
          "same identity opposite occurrences resolve exact oriented results "
          "in shared analyzer");
    const auto unrelated = EvaluateFaceChecks(holed, analyzer);
    check(unrelated[0].state == NativeCheckState::Unavailable &&
              unrelated[0].nativeStatus == -1,
          "missing analyzer face result is unavailable rather than success");
    BRepBuilderAPI_MakePolygon open;
    open.Add(gp_Pnt(0, 0, 0));
    open.Add(gp_Pnt(10, 0, 0));
    open.Add(gp_Pnt(10, 10, 0));
    BRep_Builder builder;
    TopoDS_Face openFace;
    builder.MakeFace(openFace, new Geom_Plane(gp_Pln(gp::XOY())), 1e-7);
    builder.Add(openFace, open.Wire());
    BRepCheck_Analyzer openAnalyzer(openFace, Standard_True);
    const auto openRows = EvaluateFaceChecks(openFace, openAnalyzer);
    check(openRows[4].state == NativeCheckState::Failed &&
              openRows[4].nativeStatus != 0,
          "actual open native wire has explicit failed closure");
    BRepBuilderAPI_MakePolygon crossing;
    crossing.Add(gp_Pnt(0, 0, 0));
    crossing.Add(gp_Pnt(10, 10, 0));
    crossing.Add(gp_Pnt(0, 10, 0));
    crossing.Add(gp_Pnt(10, 0, 0));
    crossing.Close();
    BRepBuilderAPI_MakeFace crossed(gp_Pln(gp::XOY()), crossing.Wire());
    BRepCheck_Analyzer crossedAnalyzer(crossed.Face(), Standard_True);
    const auto crossedRows =
        EvaluateFaceChecks(crossed.Face(), crossedAnalyzer);
    check(crossedRows[7].state == NativeCheckState::Failed &&
              !crossedRows[7].offendingFirst.IsNull(),
          "actual self-crossing wire returns native offending edge evidence");
    std::cout << "PASS: " << checks << " native interpretation check tests\n";
    return 0;
  } catch (const Standard_Failure &error) {
    std::cerr << error.GetMessageString() << '\n';
  } catch (const std::exception &error) {
    std::cerr << error.what() << '\n';
  }
  return 1;
}
