#pragma once
#include "kernel-native-warning-lineage.hpp"
// Conservative source-format audit, before XCAF/mesh omissions can hide roots.
#include <XSControl_Reader.hxx>
#include <XSControl_WorkSession.hxx>
#include <XSControl_TransferReader.hxx>
#include <Transfer_TransientProcess.hxx>
#include <Transfer_Binder.hxx>
#include <Interface_CheckIterator.hxx>
#include <Interface_Check.hxx>
#include <Interface_Graph.hxx>
#include <Interface_EntityIterator.hxx>
#include <StepShape_ShapeRepresentation.hxx>
#include <StepRepr_DefinitionalRepresentation.hxx>
#include <StepShape_ManifoldSolidBrep.hxx>
#include <StepShape_Face.hxx>
#include <StepShape_TopologicalRepresentationItem.hxx>
#include <TransferBRep.hxx>
#include <StepGeom_Axis2Placement3d.hxx>
#include <StepGeom_GeometricRepresentationItem.hxx>
#include <StepData_UndefinedEntity.hxx>
#include <STEPControl_Reader.hxx>
#include <TColStd_SequenceOfAsciiString.hxx>
#include <vector>
#include <string>

struct KernelTransferDiagnostic {
    int entityNumber=0;
    int nativeDelivery=-1;
    std::string severity,message,originalMessage,sourceLabel;
    KernelTransferDiagnostic(int number,const char* level,const char* finalText,const char* originalText,const std::string& label)
        : entityNumber(number),severity(level),message(finalText?finalText:""),originalMessage(originalText?originalText:""),sourceLabel(label) {}
};
struct KernelTransferAudit {
    bool available=false, supported=false, structurallySupported=false;
    int roots=0, missingRoots=0, failures=0, warnings=0, entities=0;
    int sourceFaces=0, missingFaces=0;
    std::vector<std::string> declaredLengthUnits;
    std::vector<KernelTransferDiagnostic> diagnostics;
    std::vector<TopoDS_Shape> transferredFaces;
    std::vector<int> unsupportedEntities;
    std::vector<int> missingRootEntities;
    std::string format="unknown";
};

inline KernelTransferAudit AuditKernelTransfer(XSControl_Reader& reader, bool step) {
    KernelTransferAudit audit; audit.format=step?"step":"iges";
    const auto model=reader.WS()->Model();
    const auto process=reader.WS()->TransferReader()->TransientProcess();
    if(model.IsNull()||process.IsNull()) return audit;
    audit.available=true; audit.entities=model->NbEntities();
    audit.roots=reader.NbRootsForTransfer();
    for(int i=1;i<=audit.roots;++i) {
        const auto entity=reader.RootForTransfer(i);
        const auto binder=process->Find(entity);
        if(binder.IsNull()||!binder->HasResult()) {
            ++audit.missingRoots; audit.missingRootEntities.push_back(model->Number(entity));
        }
    }
    auto checks=process->CheckList(Standard_False);
    for(checks.Start();checks.More();checks.Next()) {
        const auto check=checks.Value();
        audit.failures+=check->NbFails(); audit.warnings+=check->NbWarnings();
        const int number=check->HasEntity()?model->Number(check->Entity()):checks.Number();
        std::string label;
        if(number>0&&number<=model->NbEntities()) {
            const auto nativeLabel=model->StringLabel(model->Value(number));
            if(!nativeLabel.IsNull()) label=nativeLabel->ToCString();
        }
        for(int i=1;i<=check->NbFails();++i)
            audit.diagnostics.push_back(KernelTransferDiagnostic(number,"failure",check->CFail(i,Standard_True),check->CFail(i,Standard_False),label));
        for(int i=1;i<=check->NbWarnings();++i) {
            audit.diagnostics.push_back(KernelTransferDiagnostic(number,"warning",check->CWarning(i,Standard_True),check->CWarning(i,Standard_False),label));
            audit.diagnostics.back().nativeDelivery = MalievNativeWarning::FindDelivery(check, check->Warning(i,Standard_True), number, model);
        }
    }
    // IGES root transfer evidence is retained, but its entity ownership audit
    // needs format-specific certification (blanked/dependent entities, groups).
    if(!step) return audit;
    auto stepReader=dynamic_cast<STEPControl_Reader*>(&reader);
    if(stepReader) {
        TColStd_SequenceOfAsciiString length,angle,solidAngle;
        stepReader->FileUnits(length,angle,solidAngle);
        for(int i=1;i<=length.Length();++i) audit.declaredLengthUnits.push_back(length.Value(i).ToCString());
    }
    Interface_Graph graph(model);
    int solidItems=0;
    for(int i=1;i<=model->NbEntities();++i) {
        const auto entity=model->Value(i);
        if(entity->IsKind(STANDARD_TYPE(StepShape_Face))) {
            ++audit.sourceFaces;
            const auto shape=TransferBRep::ShapeResult(process,entity);
            if(shape.IsNull()||shape.ShapeType()!=TopAbs_FACE) ++audit.missingFaces;
            else audit.transferredFaces.push_back(shape);
        }
        bool unsupported=entity->IsKind(STANDARD_TYPE(StepData_UndefinedEntity));
        const auto rep=Handle(StepRepr_Representation)::DownCast(entity);
        // Referenced definitional representations carry native pcurve basis
        // geometry; they are dependencies of a face, not extra document roots.
        const bool definition=!rep.IsNull()&&rep->IsKind(STANDARD_TYPE(StepRepr_DefinitionalRepresentation))
            &&graph.Sharings(entity).NbEntities()>0;
        if(!rep.IsNull()&&!definition) for(int item=1;item<=rep->NbItems();++item) {
            const auto value=rep->ItemsValue(item);
            if(value->IsKind(STANDARD_TYPE(StepShape_ManifoldSolidBrep))) ++solidItems;
            else if(!value->IsKind(STANDARD_TYPE(StepGeom_Axis2Placement3d))&&
                    (rep->IsKind(STANDARD_TYPE(StepShape_ShapeRepresentation))||
                     value->IsKind(STANDARD_TYPE(StepGeom_GeometricRepresentationItem))||
                     value->IsKind(STANDARD_TYPE(StepShape_TopologicalRepresentationItem)))) unsupported=true;
        }
        // An unreferenced geometric entity is source content, even if the STEP
        // product-root selector would never transfer it.
        if((entity->IsKind(STANDARD_TYPE(StepGeom_GeometricRepresentationItem))||
            entity->IsKind(STANDARD_TYPE(StepShape_TopologicalRepresentationItem)))&&graph.Sharings(entity).NbEntities()==0)
            unsupported=true;
        if(unsupported) audit.unsupportedEntities.push_back(i);
    }
    audit.structurallySupported=audit.roots==1&&audit.missingRoots==0&&audit.failures==0
        &&audit.unsupportedEntities.empty()&&solidItems==1&&audit.sourceFaces>0&&audit.missingFaces==0
        &&!audit.declaredLengthUnits.empty();
    audit.supported=audit.structurallySupported&&audit.warnings==0;
    return audit;
}
