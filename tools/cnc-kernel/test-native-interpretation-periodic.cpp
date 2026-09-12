#include "kernel-repair.hpp"
#include <StepData_StepModel.hxx>
#include <StepShape_Face.hxx>
#include <TransferBRep_ShapeBinder.hxx>
#include <iostream>
#include <stdexcept>
using namespace MalievRepair;
int main() {
  int checks = 0;
  auto check = [&](bool ok, const char *name) {
    ++checks;
    if (!ok)
      throw std::runtime_error(name);
  };
  try {
    TColgp_Array2OfPnt poles(1, 2, 1, 3);
    for (int i = 1; i <= 2; ++i)
      for (int j = 1; j <= 3; ++j)
        poles(i, j) = gp_Pnt(i - 1, j - 1, 0);
    TColStd_Array1OfReal u(1, 2), v(1, 3);
    u(1) = 0;
    u(2) = 1;
    v(1) = 0;
    v(2) = 1;
    v(3) = 2;
    TColStd_Array1OfInteger um(1, 2), vm(1, 3);
    um.Init(2);
    vm.Init(2);
    vm(2) = 1;
    Handle(Geom_Surface) surface =
        new Geom_BSplineSurface(poles, u, v, um, vm, 1, 1);
    auto cancel =
        BoundSurfaceDifference(surface, surface, .01, [] { return false; });
    check(cancel.status == "unavailable" && cancel.intervals == 0,
          "cancelled periodic comparison performs no cells");
    auto zero = BoundSurfaceDifference(surface, surface, .01, {}, 0);
    check(zero.status == "unavailable" && zero.intervals == 0,
          "zero remaining allowance cannot regain default cells");
    auto one = BoundSurfaceDifference(surface, surface, .01, {}, 1);
    check(one.status == "unavailable" && one.intervals == 1,
          "partial cell coverage remains unavailable and charged");
    int calls = 0;
    auto midway = BoundSurfaceDifference(surface, surface, .01,
                                         [&] { return ++calls <= 2; });
    check(midway.status == "unavailable" && midway.intervals == 1,
          "deadline checked within periodic cell loop");
    MalievNativeInterpretation::Obligation placedPath;
    placedPath.state = MalievNativeInterpretation::ObligationState::Bounded;
    placedPath.upper = .001;
    placedPath.pathTerms = {.001};
    check(!MalievNativeInterpretation::AppendBoundedPathTerm(
              placedPath, std::numeric_limits<double>::infinity(), .01,
              "periodic world-placement upper bound unavailable") &&
              placedPath.state ==
                  MalievNativeInterpretation::ObligationState::Unavailable &&
              placedPath.pathTerms.size() == 1 &&
              placedPath.pathTerms.front() == .001 &&
              placedPath.reason ==
                  "periodic world-placement upper bound unavailable",
          "nonfinite periodic placement remains unavailable without composing");
    MalievNativeInterpretation::Obligation deadlineSkipped;
    deadlineSkipped.reason = "deadline expired before required evaluator";
    check(!MalievNativeInterpretation::AppendBoundedPathTerm(
              deadlineSkipped, .001, .01, "placement unavailable") &&
              deadlineSkipped.state ==
                  MalievNativeInterpretation::ObligationState::Unassessed &&
              deadlineSkipped.reason ==
                  "deadline expired before required evaluator" &&
              deadlineSkipped.pathTerms.empty(),
          "periodic composition retains deadline-skipped residual state");
    Configure(.01, false);
    Reset();
    const auto token = PeriodicChange(Geometry(surface), surface, 1, 2);
    check(token != 0 && EvidenceStore().periodicMetrics.Complete() &&
              EvidenceStore().periodicIntervals == 2 &&
              EvidenceStore().periodic.front().domainsCaptured,
          "actual periodic hook finalizes finite common-domain bound and work");
    Handle(StepData_StepModel) model = new StepData_StepModel;
    Handle(Standard_Transient) entity = new StepShape_Face;
    model->AddEntity(entity);
    Handle(Transfer_TransientProcess) process = new Transfer_TransientProcess;
    process->SetModel(model);
    process->Bind(entity, new TransferBRep_ShapeBinder);
    const int before = WarningCount(process, entity);
    process->AddWarning(entity, "Surface forced to be periodic");
    PeriodicWarningDelivered(process, entity, before, token);
    const auto binder = process->Find(entity);
    const int delivery = MalievNativeWarning::FindDelivery(
        binder->Check(), binder->Check()->Warning(1, true),
        model->Number(entity), model);
    check(delivery >= 0 &&
              MalievNativeWarning::Store().deliveries[delivery].emission ==
                  token,
          "actual direct warning handle joins its exact periodic operation");
    Configure(.01, false);
    Reset();
    EvidenceStore().deadline =
        std::chrono::steady_clock::now() - std::chrono::seconds(1);
    PeriodicChange(Geometry(surface), surface, 1, 2);
    check(EvidenceStore().periodicMetrics.Counts().required == 1 &&
              EvidenceStore().periodicMetrics.Counts().unassessed == 1 &&
              EvidenceStore().periodicIntervals == 0,
          "expired periodic hook retains immutable unassessed requirement");
    Configure(.01, false);
    Reset();
    EvidenceStore().periodicIntervals = 65535;
    PeriodicChange(Geometry(surface), surface, 1, 2);
    check(EvidenceStore().periodicIntervals == 65536 &&
              EvidenceStore().periodicMetrics.Counts().unavailable == 1,
          "partial periodic work consumes only remaining aggregate allowance");
    Configure(.01, false);
    Reset();
    PeriodicChange("not a serialized surface", surface, 1, 2);
    check(EvidenceStore().periodicMetrics.Counts().unavailable == 1 &&
              !EvidenceStore().periodic.front().domainsCaptured,
          "malformed original periodic representation is recorded unavailable");
    Configure(.01, false);
    Reset();
    PeriodicWarningDelivered(Handle(Transfer_TransientProcess)(), entity, 0, 0);
    check(MalievNativeWarning::Store().appendFailures == 1,
          "missing direct transfer context remains explicit unbound failure");
    std::cout << "PASS: " << checks << " periodic producer checks\n";
    return 0;
  } catch (const Standard_Failure &error) {
    std::cerr << error.GetMessageString() << '\n';
  } catch (const std::exception &error) {
    std::cerr << error.what() << '\n';
  }
  return 1;
}
