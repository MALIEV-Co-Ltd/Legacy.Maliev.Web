#pragma once
#include <BRepCheck_Analyzer.hxx>
#include <BRepCheck_Shell.hxx>
#include <BRepClass3d_SolidClassifier.hxx>
#include <TopExp_Explorer.hxx>
#include <TopoDS_Shell.hxx>
#include <Precision.hxx>
#include "kernel-region-measures.hpp"
namespace MalievKernel {
// Native mesh occurrence evidence, not an assembly definition or cavity proof.
inline void WriteBody(const Mesh& mesh,val& output,ExportContext& context,bool millimeters=false) {
    val body=val::object(),shells=val::array(); output.set("kernelBody",body);
    body.set("schema",std::string("MalievKernelBody.v1")); body.set("bodyId",context.prefix);
    body.set("membershipStatus",std::string("unavailable")); body.set("shells",shells);
    body.set("boundedSolidEvidence",false); body.set("cavityValidation",std::string("unavailable"));
    body.set("nativeRegionMeasures",RegionMeasures::Export(RegionMeasures::Unavailable("supported_solid_unavailable"),context.prefix));
    const KernelMeshSource* source=dynamic_cast<const KernelMeshSource*>(&mesh);
    if(!source) { body.set("reason",std::string("native_mesh_source_unavailable")); return; }
    try {
        const TopoDS_Shape& shape=source->KernelShape();
        const bool standalone=source->IsStandaloneFaceGroup();
        const bool solid=!standalone&&shape.ShapeType()==TopAbs_SOLID;
        const bool shell=!standalone&&shape.ShapeType()==TopAbs_SHELL;
        body.set("sourceKind",std::string(standalone?"standalone-face-group":solid?"solid":shell?"standalone-shell":"unsupported"));
        body.set("sourceShapeType",static_cast<int>(shape.ShapeType()));
        body.set("sourceOrientation",static_cast<int>(shape.Orientation()));
        std::vector<TopoDS_Shell> nativeShells;
        bool membership=solid||shell||standalone;
        if(solid) for(TopoDS_Iterator it(shape);it.More();it.Next()) {
            if(it.Value().ShapeType()!=TopAbs_SHELL) membership=false;
            else nativeShells.push_back(TopoDS::Shell(it.Value()));
        }
        if(shell) nativeShells.push_back(TopoDS::Shell(shape));
        std::vector<std::vector<std::string>> faceMemberships(context.sourceFaces.size());
        TopTools_IndexedMapOfShape faceIndex;
        std::vector<std::vector<size_t>> faceCandidates;
        for(size_t f=0;f<context.sourceFaces.size();++f) {
            int i=faceIndex.FindIndex(context.sourceFaces[f]);
            if(i==0) { i=faceIndex.Add(context.sourceFaces[f]); faceCandidates.emplace_back(); }
            faceCandidates[i-1].push_back(f);
        }
        bool shellChecks=true;
        for(size_t s=0;s<nativeShells.size();++s) {
            const std::string id=context.prefix+"/shell-"+std::to_string(s);
            val record=val::object(),ids=val::array(); shells.set(s,record);
            record.set("shellId",id); record.set("sourceOrientation",static_cast<int>(nativeShells[s].Orientation()));
            record.set("faceIds",ids); int count=0;
            for(TopExp_Explorer ex(nativeShells[s],TopAbs_FACE);ex.More();ex.Next()) {
                std::vector<size_t> matches;
                const int i=faceIndex.FindIndex(ex.Current());
                if(i!=0) for(size_t f:faceCandidates[i-1]) if(context.sourceFaces[f].IsEqual(ex.Current())) matches.push_back(f);
                if(matches.size()!=1) membership=false;
                for(size_t f:matches) {
                    ids.set(count++,context.sourceFaceRecords[f]["faceId"]);
                    faceMemberships[f].push_back(id);
                }
            }
            BRepCheck_Shell check(nativeShells[s]);
            const auto closed=check.Closed(),oriented=check.Orientation();
            record.set("closureCheck",static_cast<int>(closed)); record.set("orientationCheck",static_cast<int>(oriented));
            shellChecks=shellChecks&&closed==BRepCheck_NoError&&oriented==BRepCheck_NoError;
        }
        for(size_t f=0;f<context.sourceFaces.size();++f) {
            val record=context.sourceFaceRecords[f],ids=val::array();
            for(size_t s=0;s<faceMemberships[f].size();++s) ids.set(s,faceMemberships[f][s]);
            const bool unique=standalone?faceMemberships[f].empty():faceMemberships[f].size()==1;
            membership=membership&&unique;
            record.set("shellMemberships",ids); record.set("solidId",solid?val(context.prefix):val::null());
            record.set("membershipStatus",std::string(unique?"complete":"ambiguous_or_missing"));
        }
        membership=membership&&!context.sourceFaces.empty()&&(standalone||!nativeShells.empty());
        body.set("membershipStatus",std::string(membership?"complete":"partial"));
        if(solid) body.set("nativeRegionMeasures",RegionMeasures::Export(RegionMeasures::Volume(shape,millimeters,membership),context.prefix));
        // A standalone-face group's source compound contains excluded objects;
        // never run a validity claim for that enclosing compound on its behalf.
        if(standalone||(!solid&&!shell)) return;
        BRepCheck_Analyzer analyzer(shape,Standard_True,Standard_False);
        const bool valid=analyzer.IsValid(); val validity=val::object();
        validity.set("method",std::string("BRepCheck_Analyzer")); validity.set("geometricControls",true);
        validity.set("isValid",valid); body.set("validity",validity);
        if(solid) {
            BRepClass3d_SolidClassifier classifier(shape); classifier.PerformInfinitePoint(Precision::Confusion());
            const TopAbs_State state=classifier.State();
            body.set("infinitePointState",std::string(state==TopAbs_OUT?"OUT":state==TopAbs_IN?"IN":state==TopAbs_ON?"ON":"UNKNOWN"));
            body.set("classificationTolerance",Precision::Confusion());
            // Multi-shell cavity nesting and assembly occurrence coverage are
            // separate gates; this bounded claim covers a single native shell.
            body.set("boundedSolidEvidence",valid&&membership&&shellChecks&&nativeShells.size()==1&&state==TopAbs_OUT);
        }
    } catch(const Standard_Failure&) {
        body.set("nativeRegionMeasures",RegionMeasures::Export(RegionMeasures::Unavailable("kernel_body_export_failed"),context.prefix));
        body.set("membershipStatus",std::string("partial"));
        body.set("boundedSolidEvidence",false); body.set("reason",std::string("kernel_body_export_failed"));
    }
}
}
