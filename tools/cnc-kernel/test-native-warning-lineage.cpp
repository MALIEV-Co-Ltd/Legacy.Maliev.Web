#include "kernel-native-warning-lineage.hpp"
#include <BRepBuilderAPI_MakePolygon.hxx>
#include <Message_ListOfMsg.hxx>
#include <Message_Msg.hxx>
#include <ShapeExtend_MsgRegistrator.hxx>
#include <ShapeProcess_ShapeContext.hxx>
#include <StepData_StepModel.hxx>
#include <StepShape_Face.hxx>
#include <TopoDS_Wire.hxx>
#include <TransferBRep_ShapeBinder.hxx>
#include <Transfer_TransientProcess.hxx>
#include <XSAlgo.hxx>
#include <XSAlgo_AlgoContainer.hxx>
#include <iostream>
#include <new>
#include <stdexcept>
using namespace MalievNativeWarning;
int main() {
  int checks = 0;
  auto check = [&](bool ok, const char *name) {
    ++checks;
    if (!ok)
      throw std::runtime_error(name);
  };
  try {
    Reset(true);
    Message_Msg message("same diagnostic");
    const auto token = Tag(&message, "intersection", 0);
    Message_Msg copied(message);
    check(token != 0 && Token(&copied) == token,
          "native copy constructor preserves emission lineage");
    Message_ListOfMsg list;
    list.Append(copied);
    Message_ListOfMsg assigned;
    assigned = list;
    check(Token(&assigned.First()) == token,
          "native list assignment preserves copy-constructor lineage");
    copied.Set("same diagnostic");
    check(Token(&copied) == 0, "Set invalidates reused message lineage");
    Message_Msg untraced;
    untraced = message;
    check(Token(&untraced) == 0,
          "untraced implicit assignment does not acquire a token");
    alignas(Message_Msg) unsigned char storage[sizeof(Message_Msg)];
    auto *reused = new (storage) Message_Msg("same diagnostic");
    Tag(reused, "intersection", 8);
    reused->~Message_Msg();
    reused = new (storage)
        Message_Msg(TCollection_ExtendedString("same diagnostic"));
    check(Token(reused) == 0,
          "extended-key constructor resets a reused address");
    reused->~Message_Msg();
    reused = new (storage) Message_Msg();
    check(Token(reused) == 0,
          "same address reconstruction cannot retain old emission");
    reused->~Message_Msg();

    BRepBuilderAPI_MakePolygon polygon;
    polygon.Add(gp_Pnt(0, 0, 0));
    polygon.Add(gp_Pnt(1, 0, 0));
    polygon.Add(gp_Pnt(0, 1, 0));
    polygon.Close();
    const TopoDS_Shape wire = polygon.Wire();
    Handle(StepData_StepModel) model = new StepData_StepModel;
    Handle(Standard_Transient) firstEntity = new StepShape_Face;
    Handle(Standard_Transient) secondEntity = new StepShape_Face;
    model->AddEntity(firstEntity);
    model->AddEntity(secondEntity);
    Handle(Transfer_TransientProcess) process = new Transfer_TransientProcess;
    process->SetModel(model);
    Handle(TransferBRep_ShapeBinder) firstBinder =
        new TransferBRep_ShapeBinder(wire);
    Handle(TransferBRep_ShapeBinder) secondBinder =
        new TransferBRep_ShapeBinder(wire);
    process->Bind(firstEntity, firstBinder);
    process->Bind(secondEntity, secondBinder);
    Handle(ShapeExtend_MsgRegistrator) registrator =
        new ShapeExtend_MsgRegistrator;
    Message_Msg other("same diagnostic");
    const auto otherToken = Tag(&other, "intersection", 1);
    registrator->Send(wire, other, Message_Warning);
    registrator->Send(wire, message, Message_Warning);
    Handle(ShapeProcess_ShapeContext) context =
        new ShapeProcess_ShapeContext(wire, "");
    context->Messages() = registrator;
    XSAlgo::Init();
    XSAlgo::AlgoContainer()->MergeTransferInfo(process, context, 1);
    check(Store().deliveries.size() == 4 &&
              firstBinder->Check()->NbWarnings() == 2 &&
              secondBinder->Check()->NbWarnings() == 2,
          "real merge retains distinct duplicate warnings and multiple binder "
          "deliveries");
    const auto firstDelivery = FindDelivery(
        firstBinder->Check(), firstBinder->Check()->Warning(1, true),
        model->Number(firstEntity), model);
    const auto secondDelivery = FindDelivery(
        firstBinder->Check(), firstBinder->Check()->Warning(2, true),
        model->Number(firstEntity), model);
    check(firstDelivery >= 0 && secondDelivery >= 0 &&
              firstDelivery != secondDelivery &&
              Store().deliveries[firstDelivery].emission == otherToken &&
              Store().deliveries[secondDelivery].emission == token,
          "native handle joins preserve differing operation provenance despite "
          "reversed propagation");
    check(FindDelivery(firstBinder->Check(),
                       firstBinder->Check()->Warning(1, true),
                       model->Number(secondEntity), model) == -1,
          "wrong source entity cannot consume another binder delivery");
    Handle(StepData_StepModel) wrongModel = new StepData_StepModel;
    check(FindDelivery(firstBinder->Check(),
                       firstBinder->Check()->Warning(1, true),
                       model->Number(firstEntity), wrongModel) == -1,
          "same entity number in a different model cannot consume delivery");
    firstBinder->AddWarning("same diagnostic", "same diagnostic");
    check(FindDelivery(firstBinder->Check(),
                       firstBinder->Check()->Warning(3, true),
                       model->Number(firstEntity), model) == -1,
          "direct uninstrumented identical warning remains unclassified");
    Appended(firstBinder->Check(), firstBinder->Check()->NbWarnings(), token,
             model->Number(firstEntity), model);
    check(Store().appendFailures == 1 && Store().deliveries.size() == 4,
          "absent native append creates no fabricated delivery");
    const auto retainedWarning = Store().deliveries[firstDelivery].warning;
    Reset(true);
    const auto newToken = Tag(&other, "intersection", 2);
    check(newToken != token && Token(&message) == 0 &&
              EmissionIndex(token) < 0 && Store().deliveries.empty(),
          "import reset invalidates message, delivery and generation tokens");
    check(FindDelivery(firstBinder->Check(), retainedWarning,
                       model->Number(firstEntity), model) == -1,
          "retained old warning handle cannot cross import generation");
    const int beforeDirect = firstBinder->Check()->NbWarnings();
    firstBinder->AddWarning("same diagnostic", "same diagnostic");
    Appended(firstBinder->Check(), beforeDirect, token,
             model->Number(firstEntity), model);
    check(Store().deliveries.size() == 1 &&
              Store().deliveries.front().emission == 0,
          "stale explicit token remains unknown even when append is observed");
    Reset(true);
    for (size_t i = 0; i < 65536; ++i)
      Tag(&message, "intersection", i);
    check(Tag(&message, "intersection", 65536) == 0 &&
              Store().resourceFailures == 1 && Token(&message) == 0,
          "bounded emission accounting fails closed without retaining old tag");
    Reset(true);
    Tag(&message, "intersection", 0);
    std::vector<Message_Msg> copies;
    copies.reserve(65536);
    for (size_t i = 0; i < 65536; ++i)
      copies.emplace_back(message);
    check(
        Store().messages.size() == 65536 && Store().resourceFailures == 1 &&
            Token(&copies.back()) == 0,
        "bounded live and stale copy accounting leaves overflow copy untraced");
    Reset(false);
    check(Tag(&message, "intersection", 0) == 0 && Store().messages.empty(),
          "unrequested import does not capture emission lineage");
    auto missingIdentity = [&](bool enabled, bool absentEntity) {
      Reset(enabled);
      Handle(Transfer_TransientProcess) unboundProcess =
          new Transfer_TransientProcess;
      Handle(StepData_StepModel) emptyModel;
      if (absentEntity) {
        emptyModel = new StepData_StepModel;
        unboundProcess->SetModel(emptyModel);
      }
      Handle(TransferBRep_ShapeBinder) binder =
          new TransferBRep_ShapeBinder(wire);
      unboundProcess->Bind(firstEntity, binder);
      Handle(ShapeExtend_MsgRegistrator) localMessages =
          new ShapeExtend_MsgRegistrator;
      Message_Msg local("same diagnostic");
      Tag(&local, "intersection", 0);
      localMessages->Send(wire, local, Message_Warning);
      Handle(ShapeProcess_ShapeContext) localContext =
          new ShapeProcess_ShapeContext(wire, "");
      localContext->Messages() = localMessages;
      XSAlgo::AlgoContainer()->MergeTransferInfo(unboundProcess, localContext,
                                                 1);
      check(binder->Check()->NbWarnings() == 1,
            "native merge warning survives missing model or missing model "
            "entity");
      if (!enabled)
        check(Store().deliveries.empty(),
              "disabled missing-model merge adds no observation precondition");
      else
        check(Store().deliveries.size() == 1 &&
                  Store().deliveries.front().emission == 0 &&
                  Store().deliveries.front().sourceEntity == 0 &&
                  Store().sourceIdentityFailures == 1 &&
                  Store().deliveries.front().sourceIdentityReason ==
                      (absentEntity ? "unresolved-source-entity"
                                    : "missing-source-model") &&
                  FindDelivery(binder->Check(),
                               binder->Check()->Warning(1, true), 0,
                               emptyModel) == -1,
              "enabled unresolved source identity cannot produce a recognized "
              "delivery join");
    };
    missingIdentity(false, false);
    missingIdentity(true, false);
    missingIdentity(true, true);
    std::cout << "PASS: " << checks << " native warning lineage checks\n";
    return 0;
  } catch (const std::exception &error) {
    std::cerr << error.what() << '\n';
  }
  return 1;
}
