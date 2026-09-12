#include "kernel-face.hpp"
#include "kernel-stock-frames.hpp"
#include <BRepPrimAPI_MakeCylinder.hxx>
#include <BRepPrimAPI_MakeBox.hxx>
#include <BRepPrimAPI_MakeTorus.hxx>
#include <BRepMesh_IncrementalMesh.hxx>
#include <BRep_Builder.hxx>
#include <BRep_PolygonOnTriangulation.hxx>
#include <iostream>
#include <stdexcept>
using namespace MalievKernel;
static int checks=0;
static void Check(bool value,const char* reason){++checks;if(!value)throw std::runtime_error(reason);}
static val PolygonTest(const TopoDS_Edge& e,const Handle(Poly_Triangulation)& m,const TopLoc_Location& l,const std::string& f,const std::string& u,const std::string& id){
    double first,last;BRep_Tool::Range(e,first,last);return TriangulationCorrespondence::Polygon(e,m,l,f,u,id,first,last);
}
static val Stocks(const TopoDS_Shape& shape,bool omitBounds=false){ExportContext c(0);int i=0;
    for(TopExp_Explorer e(shape,TopAbs_FACE);e.More();e.Next()) {c.sourceFaces.push_back(TopoDS::Face(e.Current()));val f=val::object(),a=val::object(),ids=val::array();
        f.set("faceId",std::string("body-0/face-")+std::to_string(i));a.set("status",std::string("resolved"));a.set("occurrenceId",std::string("occurrence-0"));
        ids.set(0,std::string("occurrence-0/face-")+std::to_string(i++));a.set("sourceFaceOccurrenceCandidates",ids);f.set("assemblyInstance",a);
        const auto face=TopoDS::Face(e.Current());Bnd_Box box;BRepBndLib::AddOptimal(face,box,Standard_False,Standard_False);
        double tolerance=BRep_Tool::Tolerance(face);
        for(TopExp_Explorer edge(face,TopAbs_EDGE);edge.More();edge.Next())tolerance=std::max(tolerance,BRep_Tool::Tolerance(TopoDS::Edge(edge.Current())));
        for(TopExp_Explorer vertex(face,TopAbs_VERTEX);vertex.More();vertex.Next())tolerance=std::max(tolerance,BRep_Tool::Tolerance(TopoDS::Vertex(vertex.Current())));
        val precision=val::object();precision.set("maxTopologyTolerance",tolerance);f.set("precision",precision);
        if(!omitBounds){f.set("bounds",StockFrames::Bounds(box));box.Enlarge(tolerance);f.set("toleranceBounds",StockFrames::Bounds(box));}
        c.sourceFaceRecords.push_back(f);}
    return StockFrames::Export(c,true);
}
int main(){
    // A torus goes through AddGenSurf in the pinned AddOptimal implementation.
    // Its already-generated world face enclosure must be transformed, not
    // re-optimized for every candidate or mislabeled as a tight analytic bound.
    const auto torus=BRepPrimAPI_MakeTorus(12,2).Shape();
    Check(!StockFrames::CheapAnalytic(TopoDS::Face(TopExp_Explorer(torus,TopAbs_FACE).Current())),"torus optimizer excluded");
    const val torusStocks=Stocks(torus);Check(torusStocks["status"].as<std::string>()=="complete","generic all-face coverage");
    Check(torusStocks["candidates"][0]["faceBounds"][0]["method"].as<std::string>()=="outward-transform-of-same-import-world-face-box","generic face uses source box");
    Check(Stocks(torus,true)["status"].as<std::string>()=="partial","missing generic bounds never omitted");
    StockFrames::Box source{{{-2,-3,-4}},{{5,6,7}}},transformed;gp_Trsf rotation;rotation.SetRotation(gp_Ax1({0,0,0},{1,2,3}),.713);rotation.SetTranslationPart({17,-23,41});
    Check(StockFrames::TransformBox(source,rotation,transformed),"finite interval transform");
    for(double x:{-2.0,5.0})for(double y:{-3.0,6.0})for(double z:{-4.0,7.0}){const auto p=gp_Pnt(x,y,z).Transformed(rotation);
        for(int k=0;k<3;++k)Check(transformed.min[k]<=p.Coord(k+1)&&transformed.max[k]>=p.Coord(k+1),"complete transformed endpoint containment");}
    source.max[0]=std::numeric_limits<double>::max();source.min[0]=source.max[0];gp_Trsf identity;
    Check(!StockFrames::TransformBox(source,identity,transformed),"overflowing outward result unavailable");
    source.max[0]=std::numeric_limits<double>::infinity();Check(!StockFrames::TransformBox(source,identity,transformed),"nonfinite transform unavailable");
    for(bool placed:{false,true})for(bool reverse:{false,true}){
        auto shape=BRepPrimAPI_MakeCylinder(gp_Ax2({0,0,0},{0,0,reverse?-1.0:1.0},{1,0,0}),5,8).Shape();
        gp_Trsf placement;if(placed){placement.SetRotation(gp_Ax1({0,0,0},{1,2,3}),.713);placement.SetTranslationPart({17,-23,41});shape.Move(TopLoc_Location(placement));}
        val s=Stocks(shape);Check(s["status"].as<std::string>()=="complete","stock all-face complete");Check(s["faceIds"]["length"].as<int>()==3,"stock native cylinder faces");
        val candidate=s["candidates"][0],bounds=candidate["bounds"];for(int k=0;k<3;k++)Check(std::abs(bounds["max"][k].as<double>()-bounds["min"][k].as<double>()-(k==2?8:10))<1e-5,"support frame physical dimensions");
        Check(candidate["formalIntervalCertificate"].as<bool>()==false,"numerical not interval");
        BRepMesh_IncrementalMesh mesh(shape,.1,false,.5,false);int nodeOffset=0,triangleOffset=0,fi=0;bool foundSeam=false;
        for(TopExp_Explorer e(shape,TopAbs_FACE);e.More();e.Next()){
            const auto face=TopoDS::Face(e.Current());TopLoc_Location loc;const auto tri=BRep_Tool::Triangulation(face,loc);const auto id=std::string("face-")+std::to_string(fi++);
            val t=TriangulationCorrespondence::Face(face,tri,loc,id,nodeOffset,tri->NbNodes(),triangleOffset,tri->NbTriangles());
            Check(t["status"].as<std::string>()=="available","actual stored triangulation");
            for(int j=1;j<=tri->NbNodes();++j){gp_Pnt p=tri->Node(j).Transformed(loc.Transformation());for(int k=0;k<3;++k)Check(t["nodes"][j-1][k].as<double>()==p.Coord(k+1),"node location exactly once");}
            Check(TriangulationCorrespondence::Face(face,tri,loc,id,nodeOffset,tri->NbNodes()+1,triangleOffset,tri->NbTriangles())["reason"].as<std::string>()=="display_traversal_capture_mismatch","capture count mismatch");
            auto forward=TopoDS::Face(face.Oriented(TopAbs_FORWARD));
            for(TopExp_Explorer w(forward,TopAbs_WIRE);w.More();w.Next())for(BRepTools_WireExplorer use(TopoDS::Wire(w.Current()),forward);use.More();use.Next()){
                auto edge=use.Current();double first,last;BRep_Tool::CurveOnSurface(edge,forward,first,last);
                val p=TriangulationCorrespondence::Polygon(edge,tri,loc,id,"use","edge",first,last);
                Check(p["status"].as<std::string>()=="available","native polygon available");
                if(BRep_Tool::IsClosed(edge,tri,loc)){foundSeam=true;Check(p["apiSelectedBranch"].as<int>()==(edge.Orientation()==TopAbs_REVERSED?2:1),"retained API branch");}
                Check(p["matchingBoundaryBranchCount"].as<int>()==1,"unique actual directed branch");
                Check(p["nodeIndices"]["length"].as<int>()==p["parameters"]["length"].as<int>(),"native param association");
            }
            nodeOffset+=tri->NbNodes();triangleOffset+=tri->NbTriangles();
        }
        Check(foundSeam,"seam test nonvacuous");
    }
    auto box=BRepPrimAPI_MakeBox(3,5,7).Shape();BRepMesh_IncrementalMesh mesh(box,.1,false,.5,false);
    auto face=TopoDS::Face(TopExp_Explorer(box,TopAbs_FACE).Current());TopLoc_Location loc;auto tri=BRep_Tool::Triangulation(face,loc);
    auto edge=TopoDS::Edge(TopExp_Explorer(face.Oriented(TopAbs_FORWARD),TopAbs_EDGE).Current());BRep_Builder builder;
    auto old=BRep_Tool::PolygonOnTriangulation(edge,tri,loc);Handle(Poly_PolygonOnTriangulation) noParams=new Poly_PolygonOnTriangulation(old->Nodes());
    builder.UpdateEdge(edge,noParams,tri,loc);
    Check(PolygonTest(edge,tri,loc,"f","u","e")["reason"].as<std::string>()=="polygon_parameters_missing","missing parameters");
    Handle(Poly_Triangulation) absent=new Poly_Triangulation(3,1,false);
    Check(PolygonTest(edge,absent,loc,"f","u","e")["reason"].as<std::string>()=="polygon_missing","different handle cannot match");
    // Stored native triangulation coordinates can differ without changing the
    // underlying CAD edge: export the actual value, never silently reconcile it.
    const auto firstNode=tri->Node(1);tri->SetNode(1,firstNode.Translated({.001,0,0}));
    val moved=TriangulationCorrespondence::Face(face,tri,loc,"f",0,tri->NbNodes(),0,tri->NbTriangles());
    Check(moved["nodes"][0][0].as<double>()==firstNode.X()+.001,"different native face-edge coordinate retained");
    tri->SetNode(1,firstNode);
    builder.UpdateEdge(edge,old,tri,loc);
    auto wrong=old->Copy();wrong->SetNode(2,wrong->Node(1));builder.UpdateEdge(edge,wrong,tri,loc);
    Check(PolygonTest(edge,tri,loc,"f","u","e")["reason"].as<std::string>()=="polygon_boundary_incidence_missing","collapsed boundary incidence");
    builder.UpdateEdge(edge,old,tri,loc);
    double first,last;BRep_Tool::Range(edge,first,last);
    Check(TriangulationCorrespondence::Polygon(edge,tri,loc,"f","u","e",first,last+.1)["reason"].as<std::string>()=="polygon_pcurve_parameter_domain_mismatch","different actual pcurve domain");
    auto paramWrong=old->Copy();paramWrong->SetParameter(2,paramWrong->Parameter(1));builder.UpdateEdge(edge,paramWrong,tri,loc);
    Check(PolygonTest(edge,tri,loc,"f","u","e")["reason"].as<std::string>()=="nonmonotone_polygon_parameters","nonmonotone stored parameters");
    paramWrong=old->Copy();paramWrong->SetParameter(1,paramWrong->Parameter(1)-.1);builder.UpdateEdge(edge,paramWrong,tri,loc);
    Check(PolygonTest(edge,tri,loc,"f","u","e")["reason"].as<std::string>()=="polygon_edge_parameter_endpoints_mismatch","different native polygon domain");
    builder.UpdateEdge(edge,old,tri,loc);
    const auto savedTriangle=tri->Triangle(2);tri->SetTriangle(2,tri->Triangle(1));
    Check(PolygonTest(edge,tri,loc,"f","u","e")["reason"].as<std::string>()=="polygon_boundary_incidence_missing","same-direction duplicate triangles rejected globally");
    int a,b,c;tri->Triangle(1).Get(a,b,c);tri->SetTriangle(2,Poly_Triangle(a,c,b));
    Check(PolygonTest(edge,tri,loc,"f","u","e")["reason"].as<std::string>()=="polygon_boundary_incidence_missing","opposite duplicate has no available boundary");
    tri->SetTriangle(2,Poly_Triangle(a,a,b));
    Check(PolygonTest(edge,tri,loc,"f","u","e")["reason"].as<std::string>()=="polygon_boundary_incidence_missing","repeated triangle node rejected");
    tri->SetTriangle(2,savedTriangle);
    builder.UpdateEdge(edge,old,old,tri,loc);
    Check(PolygonTest(edge,tri,loc,"f","u","e")["reason"].as<std::string>()=="polygon_boundary_incidence_ambiguous","duplicate matching branches");
    builder.UpdateEdge(edge,old,tri,loc);
    auto* te=static_cast<BRep_TEdge*>(edge.TShape().get());Handle(BRep_CurveRepresentation) duplicate;
    for(BRep_ListIteratorOfListOfCurveRepresentation it(te->Curves());it.More();it.Next())if(it.Value()->IsPolygonOnTriangulation(tri,loc.Predivided(edge.Location())))duplicate=it.Value();
    Check(!duplicate.IsNull(),"duplicate premise");te->ChangeCurves().Append(duplicate);
    Check(PolygonTest(edge,tri,loc,"f","u","e")["reason"].as<std::string>()=="ambiguous_polygon_representation","ambiguous representation");
    std::cout<<"PASS "<<checks<<" native stock/triangulation checks\n";
}
