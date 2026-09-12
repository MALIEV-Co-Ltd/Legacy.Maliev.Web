#pragma once
#include "importer-xcaf.hpp"
#include <TDF_Tool.hxx>
#include <TopoDS_Iterator.hxx>
#include <algorithm>

namespace MalievKernel {
inline val DocumentPlacement(const TopLoc_Location& location) {
    val a=val::array(); const auto t=location.Transformation();
    for(int r=1;r<=3;++r) for(int c=1;c<=4;++c) a.set((r-1)*4+c-1,t.Value(r,c));
    return a;
}
inline std::string DocumentLabel(const TDF_Label& label) {
    TCollection_AsciiString entry; TDF_Tool::Entry(label,entry); return entry.ToCString();
}
inline std::string DocumentKind(const TopoDS_Shape& s) {
    if(s.IsNull()) return "null";
    static const char* kinds[]={"compound","compsolid","solid","shell","face","wire","edge","vertex","shape"};
    return kinds[static_cast<int>(s.ShapeType())];
}
struct DocumentOccurrence {
    TopoDS_Shape shape;
    std::vector<TopoDS_Shape> faces;
    std::vector<int> uses;
    val record=val::object(), bodies=val::array();
    std::string id;
    int bodyCount=0;
    bool leaf=false;
};
class DocumentCoverage {
    std::vector<DocumentOccurrence> occurrences;
    val document=val::object(), roots=val::array(), records=val::array();
    std::vector<std::string> reasons;
    int rootCount=0, leafCount=0, emittedBodies=0, emittedFaces=0, unmappedFaces=0;
    bool enumerated=false, assemblies=false, units=false;
    KernelTransferAudit audit;
    void Reason(const std::string& reason) {
        if(std::find(reasons.begin(),reasons.end(),reason)==reasons.end()) reasons.push_back(reason);
    }
    void Visit(const TDF_Label& label,const Handle(XCAFDoc_ShapeTool)& tool,
               const TopLoc_Location& parent,const std::string& parentId,std::vector<std::string> ancestors) {
        const std::string labelId=DocumentLabel(label);
        if(std::find(ancestors.begin(),ancestors.end(),labelId)!=ancestors.end()) {
            Reason("source_occurrence_cycle"); enumerated=false; return;
        }
        ancestors.push_back(labelId);
        TDF_Label definition=label;
        const bool reference=tool->IsReference(label);
        if(reference&&!tool->GetReferredShape(label,definition)) {
            Reason("source_reference_unresolved"); enumerated=false; return;
        }
        const auto local=tool->GetLocation(label);
        const auto world=parent.Multiplied(local);
        const bool assembly=tool->IsAssembly(definition);
        assemblies=assemblies||assembly||reference;
        const size_t index=occurrences.size(); occurrences.emplace_back();
        auto& o=occurrences[index]; o.id="occurrence-"+std::to_string(index);
        o.shape=tool->GetShape(definition);
        // GetShape(reference) already includes its local placement. We instead
        // resolve the definition and apply the occurrence chain exactly once.
        if(!o.shape.IsNull()) o.shape.Location(reference?world:parent.Multiplied(o.shape.Location()));
        o.leaf=!assembly; if(o.leaf) ++leafCount;
        o.record.set("occurrenceId",o.id); o.record.set("parentOccurrenceId",parentId.empty()?val::null():val(parentId));
        o.record.set("sourceLabel",labelId); o.record.set("definitionLabel",DocumentLabel(definition));
        o.record.set("occurrenceKind",std::string(assembly?"assembly":reference?"reference":"free-or-component-shape"));
        o.record.set("sourceKind",DocumentKind(o.shape)); o.record.set("leaf",o.leaf);
        o.record.set("localPlacement3x4",DocumentPlacement(local));
        o.record.set("worldPlacement3x4",DocumentPlacement(o.shape.IsNull()?world:o.shape.Location()));
        o.record.set("bodyIds",o.bodies);
        val faceRecords=val::array(); o.record.set("sourceFaces",faceRecords);
        if(o.leaf&&!o.shape.IsNull()) {
            for(TopExp_Explorer ex(o.shape,TopAbs_FACE);ex.More();ex.Next()) {
                o.faces.push_back(ex.Current()); o.uses.push_back(0);
                val f=val::object(); f.set("sourceFaceOccurrenceId",o.id+"/face-"+std::to_string(o.faces.size()-1));
                f.set("emittedFaceIds",val::array()); faceRecords.set(o.faces.size()-1,f);
            }
            if(o.shape.ShapeType()!=TopAbs_SOLID) Reason("source_leaf_not_single_solid");
            if(o.faces.empty()) Reason("source_geometry_without_faces");
            int edges=0,vertices=0;
            for(TopExp_Explorer ex(o.shape,TopAbs_EDGE,TopAbs_FACE);ex.More();ex.Next()) ++edges;
            for(TopExp_Explorer ex(o.shape,TopAbs_VERTEX,TopAbs_EDGE);ex.More();ex.Next()) ++vertices;
            o.record.set("edgesOutsideFaces",edges); o.record.set("verticesOutsideEdges",vertices);
            if(edges||vertices) Reason("source_geometry_outside_faces");
        }
        o.record.set("sourceFaceCount",o.faces.size()); records.set(index,o.record);
        const std::string id=o.id;
        if(assembly) {
            TDF_LabelSequence children; tool->GetComponents(definition,children,Standard_False);
            if(children.IsEmpty()) { enumerated=false; Reason("empty_assembly"); }
            for(int i=1;i<=children.Length();++i) Visit(children.Value(i),tool,world,id,ancestors);
        }
    }
public:
    DocumentCoverage(Importer* importer,const ImportParams& params) {
        document.set("schema",std::string("MalievKernelDocument.v1"));
        document.set("scope",std::string("source-transfer-and-xcaf-single-solid-coverage"));
        document.set("identityMethod",std::string("TopoDS.IsEqual-TShape-location-orientation"));
        document.set("coordinateSpace",std::string("import-world"));
        document.set("repair",std::string("no-additional-healing-or-sewing; importer-default-transfer"));
        document.set("geometricValidityIsSeparate",true);
        document.set("roots",roots); document.set("occurrences",records);
        auto xcaf=dynamic_cast<ImporterXcaf*>(importer);
        if(!xcaf) { Reason("source_units_unverified"); Reason("source_document_enumeration_unavailable"); return; }
        units=params.linearUnit==ImportParams::LinearUnit::Millimeter;
        if(!units) Reason("output_not_millimeter");
        audit=xcaf->KernelAudit();
        if(!audit.available) Reason("source_transfer_audit_unavailable");
        else if(!audit.supported) Reason(audit.format=="iges"?"iges_source_entity_coverage_unverified":"step_source_representation_not_supported");
        if(audit.missingRoots) Reason("source_transfer_roots_missing");
        if(audit.failures) Reason("source_transfer_failures");
        if(audit.warnings) Reason("source_transfer_warnings");
        if(audit.missingFaces) Reason("source_transfer_faces_missing");
        if(audit.format=="step"&&audit.declaredLengthUnits.empty()) Reason("source_units_unverified");
        if(!audit.unsupportedEntities.empty()) Reason("unsupported_source_entities");
        try {
            const auto tool=xcaf->KernelShapeTool(); TDF_LabelSequence labels; tool->GetFreeShapes(labels);
            rootCount=labels.Length(); enumerated=true;
            for(int i=1;i<=rootCount;++i) {
                const size_t next=occurrences.size(); Visit(labels.Value(i),tool,TopLoc_Location(),"",{});
                if(next<occurrences.size()) roots.set(i-1,occurrences[next].id);
            }
        } catch(const Standard_Failure&) { enumerated=false; Reason("source_document_enumeration_failed"); }
        if(rootCount!=1||leafCount!=1) Reason("source_body_selection_required");
        if(assemblies&&leafCount!=1) Reason("assembly_requires_explicit_selection");
    }
    void Associate(const Mesh& mesh,val& output,ExportContext& context) {
        ++emittedBodies; emittedFaces+=context.sourceFaces.size();
        const auto source=dynamic_cast<const KernelMeshSource*>(&mesh);
        val candidates=val::array(); std::vector<size_t> bodyMatches;
        if(source) for(size_t i=0;i<occurrences.size();++i) {
            auto& o=occurrences[i];
            if(o.leaf&&!o.shape.IsNull()&&!source->IsStandaloneFaceGroup()&&o.shape.IsEqual(source->KernelShape())) bodyMatches.push_back(i);
        }
        for(size_t i=0;i<bodyMatches.size();++i) {
            auto& o=occurrences[bodyMatches[i]]; candidates.set(i,o.id); o.bodies.set(o.bodyCount++,context.prefix);
        }
        output.set("sourceOccurrenceCandidates",candidates);
        output.set("sourceAssociationStatus",std::string(bodyMatches.size()==1?"unique":"ambiguous_or_missing"));
        for(size_t f=0;f<context.sourceFaces.size();++f) {
            std::vector<std::pair<size_t,size_t>> matches;
            for(size_t i=0;i<occurrences.size();++i) if(occurrences[i].leaf)
                for(size_t j=0;j<occurrences[i].faces.size();++j)
                    if(occurrences[i].faces[j].IsEqual(context.sourceFaces[f])) matches.emplace_back(i,j);
            val instance=val::object(),faceCandidates=val::array();
            for(size_t m=0;m<matches.size();++m) {
                auto& o=occurrences[matches[m].first]; const size_t j=matches[m].second;
                faceCandidates.set(m,o.id+"/face-"+std::to_string(j));
                o.record["sourceFaces"][j]["emittedFaceIds"].set(o.uses[j]++,context.sourceFaceRecords[f]["faceId"]);
                o.record["sourceFaces"][j].set("emittedTriangleCount",context.sourceFaceRecords[f]["last"].as<int>()-context.sourceFaceRecords[f]["first"].as<int>()+1);
            }
            instance.set("status",std::string(matches.size()==1?"resolved":"ambiguous_or_missing"));
            instance.set("sourceFaceOccurrenceCandidates",faceCandidates);
            if(matches.size()==1) instance.set("occurrenceId",occurrences[matches[0].first].id);
            else ++unmappedFaces;
            context.sourceFaceRecords[f].set("assemblyInstance",instance);
        }
    }
    void Finish(val& provenance) {
        bool faceCoverage=enumerated&&unmappedFaces==0; int sourceFaces=0;
        for(auto& o:occurrences) {
            for(size_t f=0;f<o.uses.size();++f) { ++sourceFaces; faceCoverage=faceCoverage&&o.uses[f]==1; }
            o.record.set("bodyAssociationStatus",std::string(!o.leaf?"not_applicable":o.bodyCount==1?"unique":"ambiguous_or_missing"));
        }
        if(!faceCoverage||sourceFaces!=emittedFaces) Reason("source_face_coverage_incomplete");
        bool bodyCoverage=leafCount==1&&emittedBodies==1;
        for(const auto& o:occurrences) if(o.leaf) bodyCoverage=bodyCoverage&&o.bodyCount==1;
        if(!bodyCoverage) Reason("source_body_association_not_single_unique");
        // For this single-occurrence STEP lane, every source-format face must
        // survive as the same native TShape, with placement handled separately.
        bool transferFaces=audit.available&&audit.format=="step"&&audit.sourceFaces==sourceFaces;
        for(const auto& face:audit.transferredFaces) {
            int matches=0;
            for(const auto& o:occurrences) if(o.leaf) for(const auto& imported:o.faces)
                if(face.IsPartner(imported)) ++matches;
            transferFaces=transferFaces&&matches==1;
        }
        if(audit.format=="step"&&!transferFaces) Reason("source_transfer_face_identity_incomplete");
        val transfer=val::object(); transfer.set("status",std::string(audit.available?"available":"unavailable"));
        transfer.set("format",audit.format); transfer.set("entityCount",audit.entities); transfer.set("rootCount",audit.roots);
        transfer.set("missingRootCount",audit.missingRoots); transfer.set("failureCount",audit.failures); transfer.set("warningCount",audit.warnings);
        transfer.set("sourceFaceEntityCount",audit.sourceFaces); transfer.set("missingFaceEntityCount",audit.missingFaces);
        val lengthUnits=val::array(); for(size_t i=0;i<audit.declaredLengthUnits.size();++i) lengthUnits.set(i,audit.declaredLengthUnits[i]);
        transfer.set("declaredLengthUnits",lengthUnits);
        val diagnostics=val::array();
        for(size_t i=0;i<audit.diagnostics.size();++i) {
            const auto& native=audit.diagnostics[i]; val diagnostic=val::object();
            diagnostic.set("entityNumber",native.entityNumber); diagnostic.set("severity",native.severity);
            diagnostic.set("sourceEntityLabel",native.sourceLabel);
            diagnostic.set("message",native.message); diagnostic.set("originalMessage",native.originalMessage);
            diagnostic.set("category",std::string("unclassified-native-transfer")); diagnostics.set(i,diagnostic);
        }
        transfer.set("diagnostics",diagnostics);
        transfer.set("faceIdentityStatus",std::string(transferFaces?"complete":"unverified_or_incomplete"));
        val unsupported=val::array(),missing=val::array();
        for(size_t i=0;i<audit.unsupportedEntities.size();++i) unsupported.set(i,audit.unsupportedEntities[i]);
        for(size_t i=0;i<audit.missingRootEntities.size();++i) missing.set(i,audit.missingRootEntities[i]);
        transfer.set("unsupportedEntityNumbers",unsupported); transfer.set("missingRootEntityNumbers",missing);
        transfer.set("supportedSingleSolidSource",audit.supported); document.set("sourceTransfer",transfer);
        document.set("freeRootCount",rootCount); document.set("occurrenceCount",occurrences.size()); document.set("leafOccurrenceCount",leafCount);
        document.set("sourceFaceCount",sourceFaces); document.set("emittedFaceCount",emittedFaces); document.set("emittedBodyCount",emittedBodies);
        document.set("enumerationStatus",std::string(enumerated?"complete":"unavailable_or_partial"));
        document.set("faceCoverageStatus",std::string(faceCoverage&&sourceFaces==emittedFaces?"complete":"incomplete"));
        val why=val::array(); for(size_t i=0;i<reasons.size();++i) why.set(i,reasons[i]); document.set("reasons",why);
        const bool complete=reasons.empty()&&units&&audit.supported&&bodyCoverage&&faceCoverage;
        document.set("status",std::string(complete?"complete_supported_single_solid":"review_required"));
        provenance.set("completeCadDocument",complete); provenance.set("documentCoverage",document);
    }
};
}
