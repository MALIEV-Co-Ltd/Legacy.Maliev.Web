#pragma once
#include <BRep_TEdge.hxx>
#include <BRep_ListIteratorOfListOfCurveRepresentation.hxx>
#include <Poly_PolygonOnTriangulation.hxx>
#include <Poly_Triangulation.hxx>
#include <map>
#include <set>

// Included after the scalar export helpers. Native correspondence only: no
// coordinate welding, derived mesh movement, watertightness or CAD error proof.
namespace MalievKernel { namespace TriangulationCorrespondence {
struct BoundaryUse { int count=0,first=0,last=0; };
inline bool OnDirectedBoundary(const Handle(Poly_PolygonOnTriangulation)& polygon,
                              const Handle(Poly_Triangulation)& mesh,bool reversed) {
    if(polygon.IsNull()||polygon->NbNodes()<2)return false;
    std::map<std::pair<int,int>,BoundaryUse> boundary;
    for(int i=1;i<=mesh->NbTriangles();++i){int nodes[3];mesh->Triangle(i).Get(nodes[0],nodes[1],nodes[2]);
        for(int k=0;k<3;++k)if(nodes[k]<1||nodes[k]>mesh->NbNodes()||nodes[k]==nodes[(k+1)%3])return false;
        for(int k=0;k<3;++k){int a=nodes[k],b=nodes[(k+1)%3];auto& use=boundary[{std::min(a,b),std::max(a,b)}];
            if(use.count==0){use.first=a;use.last=b;}
            else if(use.count!=1||use.first!=b||use.last!=a)return false;
            ++use.count;}}
    std::set<std::pair<int,int>> visited;
    for(int i=2;i<=polygon->NbNodes();++i){int a=polygon->Node(i-1),b=polygon->Node(i);if(reversed)std::swap(a,b);
        if(a<1||b<1||a>mesh->NbNodes()||b>mesh->NbNodes()||a==b)return false;
        auto found=boundary.find({std::min(a,b),std::max(a,b)});
        if(found==boundary.end()||found->second.count!=1||found->second.first!=a||found->second.last!=b||!visited.insert(found->first).second)return false;
    }
    return true;
}
inline std::string ParameterPremise(const Handle(Poly_PolygonOnTriangulation)& polygon,double first,double last) {
    if(polygon.IsNull())return "polygon_missing";
    if(polygon->NbNodes()<2||!std::isfinite(polygon->Deflection())||polygon->Deflection()<0)return "invalid_polygon_metadata";
    if(!polygon->HasParameters())return "polygon_parameters_missing";
    if(polygon->Parameters()->Length()!=polygon->NbNodes())return "polygon_parameter_count_mismatch";
    for(int i=1;i<=polygon->NbNodes();++i){double p=polygon->Parameter(i);
        if(!std::isfinite(p))return "nonfinite_polygon_parameter";
        if(i>1&&p<=polygon->Parameter(i-1))return "nonmonotone_polygon_parameters";}
    if(!std::isfinite(first)||!std::isfinite(last)||first>=last||polygon->Parameter(1)!=first||polygon->Parameter(polygon->NbNodes())!=last)
        return "polygon_edge_parameter_endpoints_mismatch";
    return {};
}
inline val Face(const TopoDS_Face& face,const Handle(Poly_Triangulation)& mesh,
                const TopLoc_Location& location,const std::string& faceId,
                int nodeOffset,int nodeCount,int triangleOffset,int triangleCount) {
    val o=val::object(); o.set("schema",std::string("MalievNativeTriangulation.v1"));
    o.set("status",std::string("unavailable"));o.set("faceId",faceId);
    o.set("triangulationId",faceId+"/display-triangulation");
    o.set("selection",std::string("exact-OcctFace-stored-handle-and-location"));
    o.set("identityScope",std::string("enclosing-native-import-face-occurrence"));
    o.set("correspondenceScope",std::string("raw-stored-face-triangulation; boundary-completeness-not-asserted"));
    o.set("nodeOffset",nodeOffset);o.set("nodeCount",nodeCount);
    o.set("triangleOffset",triangleOffset);o.set("triangleCount",triangleCount);
    o.set("indexBase",0);o.set("faceOrientation",static_cast<int>(face.Orientation()));
    o.set("winding",std::string("display-already-reversed-for-reversed-face"));
    o.set("coordinateSpace",std::string("import-world; stored-location-applied-once"));
    val transform=val::array();const auto t=location.Transformation();
    for(int r=1;r<=3;++r)for(int c=1;c<=4;++c)transform.set((r-1)*4+c-1,t.Value(r,c));
    o.set("triangulationToImportWorld3x4",transform);
    o.set("fullTriangleCadErrorBound",val::null());o.set("watertightCertified",false);
    if(mesh.IsNull()){o.set("reason",std::string("triangulation_missing"));return o;}
    o.set("storedDeflectionMm",mesh->Deflection());o.set("deflectionMeaning",std::string("native-stored-mesher-metadata-not-certified-error"));
    if(nodeOffset<0||triangleOffset<0||nodeCount!=mesh->NbNodes()||triangleCount!=mesh->NbTriangles()
       ||nodeCount<=0||triangleCount<=0){o.set("reason",std::string("display_traversal_capture_mismatch"));return o;}
    if(!std::isfinite(mesh->Deflection())||mesh->Deflection()<0){o.set("reason",std::string("nonfinite_deflection"));return o;}
    if(face.Orientation()!=TopAbs_FORWARD&&face.Orientation()!=TopAbs_REVERSED){o.set("reason",std::string("unsupported_face_orientation"));return o;}
    val nodes=val::array(),uvs=val::array(),triangles=val::array();
    for(int i=1;i<=nodeCount;++i){const auto p=mesh->Node(i);
        if(!std::isfinite(p.X())||!std::isfinite(p.Y())||!std::isfinite(p.Z())){o.set("reason",std::string("nonfinite_node"));return o;}
        nodes.set(i-1,Triple(p.Transformed(t)));
        if(mesh->HasUVNodes()){const auto uv=mesh->UVNode(i);if(!std::isfinite(uv.X())||!std::isfinite(uv.Y())){o.set("reason",std::string("nonfinite_uv"));return o;}
            val row=val::array();row.set(0,uv.X());row.set(1,uv.Y());uvs.set(i-1,row);}
    }
    for(int i=1;i<=triangleCount;++i){int a,b,c;mesh->Triangle(i).Get(a,b,c);
        if(a<1||b<1||c<1||a>nodeCount||b>nodeCount||c>nodeCount){o.set("reason",std::string("triangle_index_invalid"));return o;}
        if(face.Orientation()==TopAbs_REVERSED)std::swap(b,c);
        val row=val::array();row.set(0,a-1);row.set(1,b-1);row.set(2,c-1);triangles.set(i-1,row);
    }
    o.set("nodes",nodes);o.set("uvNodes",mesh->HasUVNodes()?uvs:val::null());o.set("triangles",triangles);
    o.set("status",std::string("available"));return o;
}
inline val Polygon(const TopoDS_Edge& edge,const Handle(Poly_Triangulation)& mesh,
                   const TopLoc_Location& location,const std::string& faceId,
                   const std::string& useId,const std::string& edgeId,double pcurveFirst,double pcurveLast) {
    val o=val::object();o.set("schema",std::string("MalievNativeTriangulationPolygon.v1"));
    o.set("status",std::string("unavailable"));o.set("triangulationId",faceId+"/display-triangulation");
    o.set("coedgeId",useId);o.set("edgeId",edgeId);o.set("indexBase",0);
    o.set("orientation",static_cast<int>(edge.Orientation()));
    o.set("nodeOrder",std::string("native-polygon-storage; reverse-for-reversed-coedge-traversal"));
    val pcurveRange=val::array();pcurveRange.set(0,pcurveFirst);pcurveRange.set(1,pcurveLast);o.set("sourcePcurveRange",pcurveRange);
    if(mesh.IsNull()){o.set("reason",std::string("triangulation_missing"));return o;}
    if(edge.Orientation()!=TopAbs_FORWARD&&edge.Orientation()!=TopAbs_REVERSED){o.set("reason",std::string("unsupported_edge_orientation"));return o;}
    const TopLoc_Location relative=location.Predivided(edge.Location());
    const auto* te=static_cast<const BRep_TEdge*>(edge.TShape().get());
    Handle(Poly_PolygonOnTriangulation) polygon,firstPolygon,secondPolygon;int matches=0,branch=0,apiBranch=0;
    for(BRep_ListIteratorOfListOfCurveRepresentation it(te->Curves());it.More();it.Next()){
        const auto& cr=it.Value();if(!cr->IsPolygonOnTriangulation(mesh,relative))continue;
        ++matches;apiBranch=cr->IsPolygonOnClosedTriangulation()&&edge.Orientation()==TopAbs_REVERSED?2:1;
        firstPolygon=cr->PolygonOnTriangulation();secondPolygon=cr->IsPolygonOnClosedTriangulation()?cr->PolygonOnTriangulation2():Handle(Poly_PolygonOnTriangulation)();
        polygon=apiBranch==2?secondPolygon:firstPolygon;
    }
    o.set("matchingRepresentationCount",matches);o.set("apiSelectedBranch",apiBranch);
    o.set("branchMethod",std::string("unique-native-edge-representation-and-directed-face-boundary-node-incidence"));
    if(matches!=1||polygon.IsNull()){o.set("reason",std::string(matches>1?"ambiguous_polygon_representation":"polygon_missing"));return o;}
    if(BRep_Tool::PolygonOnTriangulation(edge,mesh,location)!=polygon){o.set("reason",std::string("polygon_selection_mismatch"));return o;}
    double first,last;BRep_Tool::Range(edge,first,last);
    if(!std::isfinite(pcurveFirst)||!std::isfinite(pcurveLast)||first!=pcurveFirst||last!=pcurveLast){o.set("reason",std::string("polygon_pcurve_parameter_domain_mismatch"));return o;}
    // PolygonOnTriangulation's orientation-selected branch can differ from the
    // actual trim coedge's branch. Resolve only within this same native edge,
    // triangulation handle and location, by oriented native triangle node IDs.
    // Raw triangles are face-forward; exported display reversal is independent.
    const auto firstReason=ParameterPremise(firstPolygon,first,last),secondReason=ParameterPremise(secondPolygon,first,last);
    const bool firstMatches=firstReason.empty()&&OnDirectedBoundary(firstPolygon,mesh,edge.Orientation()==TopAbs_REVERSED);
    const bool secondMatches=secondReason.empty()&&OnDirectedBoundary(secondPolygon,mesh,edge.Orientation()==TopAbs_REVERSED);
    const int boundaryMatches=static_cast<int>(firstMatches)+static_cast<int>(secondMatches);
    o.set("matchingBoundaryBranchCount",boundaryMatches);
    if(boundaryMatches!=1){const auto reason=boundaryMatches==0&&!firstReason.empty()&&(secondPolygon.IsNull()||secondReason==firstReason)?firstReason:
            std::string(boundaryMatches==0?"polygon_boundary_incidence_missing":"polygon_boundary_incidence_ambiguous");o.set("reason",reason);return o;}
    branch=firstMatches?1:2;polygon=firstMatches?firstPolygon:secondPolygon;o.set("branch",branch);
    const int count=polygon->NbNodes();val nodes=val::array(),params=val::array();
    o.set("storedDeflectionMm",polygon->Deflection());o.set("hasParameters",polygon->HasParameters());
    if(count<2||!std::isfinite(polygon->Deflection())||polygon->Deflection()<0){o.set("reason",std::string("invalid_polygon_metadata"));return o;}
    if(polygon->HasParameters()&&polygon->Parameters()->Length()!=count){o.set("reason",std::string("polygon_parameter_count_mismatch"));return o;}
    for(int i=1;i<=count;++i){int node=polygon->Node(i);if(node<1||node>mesh->NbNodes()){o.set("reason",std::string("polygon_node_out_of_range"));return o;}
        nodes.set(i-1,node-1);if(polygon->HasParameters()){double p=polygon->Parameter(i);if(!std::isfinite(p)){o.set("reason",std::string("nonfinite_polygon_parameter"));return o;}
            if(i>1&&p<=polygon->Parameter(i-1)){o.set("reason",std::string("nonmonotone_polygon_parameters"));return o;}params.set(i-1,p);}}
    o.set("nodeIndices",nodes);o.set("parameters",polygon->HasParameters()?params:val::null());
    if(!polygon->HasParameters()){o.set("reason",std::string("polygon_parameters_missing"));return o;}
    if(!std::isfinite(first)||!std::isfinite(last)||polygon->Parameter(1)!=first||polygon->Parameter(count)!=last){o.set("reason",std::string("polygon_edge_parameter_endpoints_mismatch"));return o;}
    o.set("status",std::string("available"));return o;
}
}}
