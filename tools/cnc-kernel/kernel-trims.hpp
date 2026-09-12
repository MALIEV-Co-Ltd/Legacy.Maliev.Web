#pragma once
// Native OCCT topology only. Included after kernel-face.hpp's scalar helpers.
#include <BRep_Tool.hxx>
#include <BRepTools.hxx>
#include <BRepTools_WireExplorer.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Wire.hxx>
#include <TopoDS_Iterator.hxx>
#include <TopExp.hxx>
#include <TopTools_IndexedMapOfShape.hxx>
#include <GeomAdaptor_Curve.hxx>
#include <Geom2dAdaptor_Curve.hxx>
#include <GeomAdaptor_Surface.hxx>
#include <Geom_BSplineCurve.hxx>
#include <Geom_BezierCurve.hxx>
#include <Geom2d_BSplineCurve.hxx>
#include <Geom2d_BezierCurve.hxx>
#include <Geom_BSplineSurface.hxx>
#include <Geom_BezierSurface.hxx>
#include <gp_Elips.hxx>
#include <gp_Hypr.hxx>
#include <gp_Parab.hxx>
#include <gp_Lin2d.hxx>
#include <gp_Circ2d.hxx>
#include <gp_Elips2d.hxx>
#include <gp_Hypr2d.hxx>
#include <gp_Parab2d.hxx>
#include <set>
#include <vector>
#include <cmath>

namespace MalievKernel {
inline val Coordinates(const gp_Pnt& p) { return Triple(p); }
inline val Coordinates(const gp_Dir& p) { return Triple(p); }
template <typename T> val Pair(const T& p) {
    val a = val::array(); a.set(0,p.X()); a.set(1,p.Y()); return a;
}
inline val Coordinates(const gp_Pnt2d& p) { return Pair(p); }
inline val Coordinates(const gp_Dir2d& p) { return Pair(p); }
inline val Placement(const gp_Trsf& t) {
    val a = val::array();
    for (int r=1;r<=3;++r) for (int c=1;c<=4;++c) a.set((r-1)*4+c-1,t.Value(r,c));
    return a;
}
inline val Range(double first,double last) {
    val a = val::array(); a.set(0,first); a.set(1,last); return a;
}
template <typename T> void CurveFrame(val& o,const T& conic) {
    o.set("origin",Coordinates(conic.Location()));
    o.set("xDirection",Coordinates(conic.XAxis().Direction()));
    o.set("yDirection",Coordinates(conic.YAxis().Direction()));
}
inline void CurveFrame(val& o,const gp_Parab2d& conic) {
    o.set("origin",Pair(conic.Location()));
    o.set("xDirection",Pair(conic.Axis().XDirection()));
    o.set("yDirection",Pair(conic.Axis().YDirection()));
}
// Adaptors retain native parameters; placement is separate, never baked into poles.
template <typename T> val Curve(const T& c) {
    val o=val::object(); o.set("status",std::string("exact"));
    o.set("occtType",static_cast<int>(c.GetType()));
    switch(c.GetType()) {
    case GeomAbs_Line: {
        const auto p=c.Line(); o.set("type",std::string("line"));
        o.set("origin",Coordinates(p.Location())); o.set("direction",Coordinates(p.Direction())); break;
    }
    case GeomAbs_Circle: {
        const auto p=c.Circle(); o.set("type",std::string("circle")); CurveFrame(o,p); o.set("radius",p.Radius()); break;
    }
    case GeomAbs_Ellipse: {
        const auto p=c.Ellipse(); o.set("type",std::string("ellipse")); CurveFrame(o,p);
        o.set("majorRadius",p.MajorRadius()); o.set("minorRadius",p.MinorRadius()); break;
    }
    case GeomAbs_Hyperbola: {
        const auto p=c.Hyperbola(); o.set("type",std::string("hyperbola")); CurveFrame(o,p);
        o.set("majorRadius",p.MajorRadius()); o.set("minorRadius",p.MinorRadius()); break;
    }
    case GeomAbs_Parabola: {
        const auto p=c.Parabola(); o.set("type",std::string("parabola")); CurveFrame(o,p); o.set("focal",p.Focal()); break;
    }
    case GeomAbs_BezierCurve: {
        const auto p=c.Bezier(); o.set("type",std::string("bezier")); o.set("degree",p->Degree());
        o.set("rational",p->IsRational()); o.set("periodic",false);
        val poles=val::array(),weights=val::array();
        for(int i=1;i<=p->NbPoles();++i) { poles.set(i-1,Coordinates(p->Pole(i))); weights.set(i-1,p->Weight(i)); }
        o.set("poles",poles); o.set("weights",weights); break;
    }
    case GeomAbs_BSplineCurve: {
        const auto p=c.BSpline(); o.set("type",std::string("bspline")); o.set("degree",p->Degree());
        o.set("rational",p->IsRational()); o.set("periodic",p->IsPeriodic());
        val poles=val::array(),weights=val::array(),knots=val::array(),multiplicities=val::array();
        for(int i=1;i<=p->NbPoles();++i) { poles.set(i-1,Coordinates(p->Pole(i))); weights.set(i-1,p->Weight(i)); }
        for(int i=1;i<=p->NbKnots();++i) { knots.set(i-1,p->Knot(i)); multiplicities.set(i-1,p->Multiplicity(i)); }
        o.set("poles",poles); o.set("weights",weights); o.set("knots",knots); o.set("multiplicities",multiplicities); break;
    }
    default: return Unavailable("unsupported_native_curve_type");
    }
    return o;
}
inline bool Exact(const val& o) { return o["status"].as<std::string>()=="exact"; }
template<typename T> void SurfacePoles(val& o,const T& p) {
    o.set("uDegree",p->UDegree()); o.set("vDegree",p->VDegree());
    o.set("uRational",p->IsURational()); o.set("vRational",p->IsVRational());
    val poles=val::array(),weights=val::array();
    for(int u=1;u<=p->NbUPoles();++u) {
        val row=val::array(),w=val::array();
        for(int v=1;v<=p->NbVPoles();++v) { row.set(v-1,Triple(p->Pole(u,v))); w.set(v-1,p->Weight(u,v)); }
        poles.set(u-1,row); weights.set(u-1,w);
    }
    o.set("poles",poles); o.set("weights",weights);
}
inline val SurfaceBasis(const Handle(Geom_Surface)& native) {
    if(native.IsNull()) return Unavailable("missing_native_surface");
    GeomAdaptor_Surface s(native);
    val o=val::object(); o.set("status",std::string("exact")); o.set("occtType",static_cast<int>(s.GetType()));
    switch(s.GetType()) {
    case GeomAbs_Plane: { auto p=s.Plane(); o.set("type",std::string("plane")); Frame(o,p.Position()); break; }
    case GeomAbs_Cylinder: { auto p=s.Cylinder(); o.set("type",std::string("cylinder")); Frame(o,p.Position()); o.set("radius",p.Radius()); break; }
    case GeomAbs_Cone: { auto p=s.Cone(); o.set("type",std::string("cone")); Frame(o,p.Position()); o.set("referenceRadius",p.RefRadius()); o.set("semiAngleRadians",p.SemiAngle()); break; }
    case GeomAbs_Sphere: { auto p=s.Sphere(); o.set("type",std::string("sphere")); Frame(o,p.Position()); o.set("radius",p.Radius()); break; }
    case GeomAbs_Torus: { auto p=s.Torus(); o.set("type",std::string("torus")); Frame(o,p.Position()); o.set("majorRadius",p.MajorRadius()); o.set("minorRadius",p.MinorRadius()); break; }
    case GeomAbs_BezierSurface: {
        auto p=s.Bezier(); o.set("type",std::string("bezier")); SurfacePoles(o,p);
        o.set("uPeriodic",false); o.set("vPeriodic",false); break;
    }
    case GeomAbs_BSplineSurface: {
        auto p=s.BSpline(); o.set("type",std::string("bspline")); SurfacePoles(o,p);
        o.set("uPeriodic",p->IsUPeriodic()); o.set("vPeriodic",p->IsVPeriodic());
        val uk=val::array(),vk=val::array(),um=val::array(),vm=val::array();
        for(int i=1;i<=p->NbUKnots();++i) { uk.set(i-1,p->UKnot(i)); um.set(i-1,p->UMultiplicity(i)); }
        for(int i=1;i<=p->NbVKnots();++i) { vk.set(i-1,p->VKnot(i)); vm.set(i-1,p->VMultiplicity(i)); }
        o.set("uKnots",uk); o.set("vKnots",vk); o.set("uMultiplicities",um); o.set("vMultiplicities",vm); break;
    }
    default: return Unavailable("unsupported_native_surface_basis");
    }
    return o;
}
// One context per mesh-group occurrence. Maps use IsSame (TShape+location,
// orientation ignored), not coordinate comparisons. Context dies after callback
// traversal. IDs cannot establish cross-mesh adjacency or persistent naming.
struct ExportContext {
    std::string prefix;
    TopTools_IndexedMapOfShape edges,vertices;
    std::vector<val> edgeRecords,vertexRecords,faceRecords;
    std::vector<TopoDS_Face> sourceFaces;
    std::vector<val> sourceFaceRecords;
    std::vector<std::set<std::string>> edgeFaces;
    std::vector<int> useCounts;
    bool complete=true;
    explicit ExportContext(int body):prefix("body-"+std::to_string(body)) {}
    std::string Vertex(const TopoDS_Vertex& vertex) {
        if(vertex.IsNull()) return "";
        int i=vertices.FindIndex(vertex);
        if(i==0) {
            i=vertices.Add(vertex); val o=val::object();
            o.set("vertexId",prefix+"/vertex-"+std::to_string(i));
            o.set("status",std::string("partial"));
            // Keep an addressable record even if kernel extraction throws.
            vertexRecords.push_back(o);
            o.set("worldPoint",Triple(BRep_Tool::Pnt(vertex))); o.set("tolerance",BRep_Tool::Tolerance(vertex));
            o.set("status",std::string("complete"));
        }
        return prefix+"/vertex-"+std::to_string(i);
    }
    int Edge(const TopoDS_Edge& use) {
        int i=edges.FindIndex(use); if(i!=0) return i;
        TopoDS_Edge edge=TopoDS::Edge(use.Oriented(TopAbs_FORWARD));
        i=edges.Add(edge);
        val o=val::object(); o.set("edgeId",prefix+"/edge-"+std::to_string(i));
        // Reserve before export so an OCCT exception cannot misalign indexed maps.
        edgeRecords.push_back(o); edgeFaces.emplace_back(); useCounts.push_back(0);
        o.set("status",std::string("partial")); o.set("uses",val::array());
        o.set("degenerated",BRep_Tool::Degenerated(edge)); o.set("tolerance",BRep_Tool::Tolerance(edge));
        o.set("sameParameter",BRep_Tool::SameParameter(edge)); o.set("sameRange",BRep_Tool::SameRange(edge));
        TopoDS_Vertex start,end; TopExp::Vertices(edge,start,end,Standard_True);
        o.set("startVertexId",Vertex(start)); o.set("endVertexId",Vertex(end));
        TopLoc_Location location; double first=0,last=0;
        Handle(Geom_Curve) native=BRep_Tool::Curve(edge,location,first,last);
        o.set("range",Range(first,last)); o.set("curveToImportWorld3x4",Placement(location.Transformation()));
        val curve=native.IsNull()?Unavailable(BRep_Tool::Degenerated(edge)?"degenerated_no_3d_curve":"missing_3d_curve") : Curve(GeomAdaptor_Curve(native));
        o.set("curve3d",curve);
        bool valid=(Exact(curve)||(native.IsNull()&&BRep_Tool::Degenerated(edge))) && std::isfinite(first)&&std::isfinite(last);
        o.set("status",std::string(valid?"complete":"partial")); complete=complete&&valid;
        return i;
    }
    void WriteTrims(const TopoDS_Face& original,val& output) {
        sourceFaces.push_back(original); sourceFaceRecords.push_back(output);
        val trims=val::object(),wires=val::array(); output.set("trims",trims);
        trims.set("status",std::string("partial")); trims.set("wires",wires);
        trims.set("parameterSpace",std::string("native-surface-uv"));
        trims.set("orientationConvention",std::string("face-forward; reversed-coedge-traverses-last-to-first"));
        val adjacency=val::object(); adjacency.set("status",std::string("pending")); output.set("adjacency",adjacency); faceRecords.push_back(adjacency);
        try {
            TopoDS_Face face=TopoDS::Face(original.Oriented(TopAbs_FORWARD));
            TopLoc_Location location; auto native=BRep_Tool::Surface(face,location);
            val basis=SurfaceBasis(native); trims.set("surfaceBasis",basis);
            trims.set("surfaceToImportWorld3x4",Placement(location.Transformation()));
            bool faceComplete=Exact(basis) && (original.Orientation()==TopAbs_FORWARD||original.Orientation()==TopAbs_REVERSED);
            TopoDS_Wire outer=BRepTools::OuterWire(face);
            const std::string faceId=output["faceId"].as<std::string>();
            int wireIndex=0;
            for(TopoDS_Iterator it(face);it.More();it.Next()) {
                if(it.Value().ShapeType()!=TopAbs_WIRE) { faceComplete=false; continue; }
                TopoDS_Wire wire=TopoDS::Wire(it.Value());
                val w=val::object(),coedges=val::array(); const std::string wireId=faceId+"/wire-"+std::to_string(wireIndex);
                wires.set(wireIndex++,w); w.set("wireId",wireId); w.set("coedges",coedges);
                w.set("role",std::string(outer.IsNull()?"unknown":(wire.IsSame(outer)?"outer":"inner")));
                w.set("orientation",static_cast<int>(wire.Orientation())); w.set("closed",wire.Closed());
                w.set("complete",false);
                std::vector<TopoDS_Edge> children;
                for(TopoDS_Iterator child(wire);child.More();child.Next()) {
                    if(child.Value().ShapeType()==TopAbs_EDGE) children.push_back(TopoDS::Edge(child.Value()));
                    else faceComplete=false;
                }
                std::vector<bool> visited(children.size(),false);
                bool wireComplete=!outer.IsNull()&&!children.empty(); int useIndex=0;
                std::string firstVertex,previousEnd;
                for(BRepTools_WireExplorer explorer(wire,face);explorer.More();explorer.Next()) {
                    TopoDS_Edge edgeUse=explorer.Current(); bool found=false;
                    for(size_t c=0;c<children.size();++c) if(!visited[c]&&children[c].IsEqual(edgeUse)) { visited[c]=true; found=true; break; }
                    wireComplete=wireComplete&&found;
                    const int index=Edge(edgeUse); val use=val::object();
                    const std::string useId=wireId+"/use-"+std::to_string(useIndex);
                    coedges.set(useIndex++,use); use.set("coedgeId",useId); use.set("edgeId",prefix+"/edge-"+std::to_string(index));
                    use.set("orientation",static_cast<int>(edgeUse.Orientation()));
                    TopoDS_Vertex start,end; TopExp::Vertices(edgeUse,start,end,Standard_True);
                    const std::string startId=Vertex(start),endId=Vertex(end);
                    use.set("startVertexId",startId); use.set("endVertexId",endId);
                    if(useIndex==1) firstVertex=startId; else if(startId!=previousEnd) wireComplete=false;
                    previousEnd=endId;
                    use.set("seam",BRep_Tool::IsClosed(edgeUse,face));
                    double first=0,last=0; Standard_Boolean stored=Standard_False;
                    auto nativeCurve=BRep_Tool::CurveOnSurface(edgeUse,face,first,last,&stored);
                    val pcurve=nativeCurve.IsNull()?Unavailable("missing_pcurve"):Curve(Geom2dAdaptor_Curve(nativeCurve));
                    use.set("pcurve",pcurve); use.set("range",Range(first,last)); use.set("isStored",stored);
                    const bool oriented=edgeUse.Orientation()==TopAbs_FORWARD||edgeUse.Orientation()==TopAbs_REVERSED;
                    const bool useComplete=oriented&&Exact(pcurve)&&std::isfinite(first)&&std::isfinite(last)&&!startId.empty()&&!endId.empty()&&edgeRecords[index-1]["status"].as<std::string>()=="complete";
                    use.set("status",std::string(useComplete?"complete":"partial")); wireComplete=wireComplete&&useComplete;
                    val ownership=val::object(); ownership.set("faceId",faceId); ownership.set("wireId",wireId); ownership.set("coedgeId",useId);
                    ownership.set("seam",BRep_Tool::IsClosed(edgeUse,face));
                    ownership.set("orientation",static_cast<int>(edgeUse.Orientation()));
                    val uses=edgeRecords[index-1]["uses"]; uses.set(useCounts[index-1]++,ownership); edgeFaces[index-1].insert(faceId);
                }
                for(bool seen:visited) if(!seen) wireComplete=false;
                const bool connectedClosed=!firstVertex.empty()&&previousEnd==firstVertex;
                w.set("connectedClosed",connectedClosed); wireComplete=wireComplete&&connectedClosed;
                w.set("directEdgeUseCount",static_cast<int>(children.size())); w.set("visitedEdgeUseCount",useIndex);
                w.set("complete",wireComplete);
                if(!wireComplete) w.set("reason",std::string("incomplete_traversal_connectivity_or_curve"));
                faceComplete=faceComplete&&wireComplete;
            }
            faceComplete=faceComplete&&wireIndex>0;
            trims.set("status",std::string(faceComplete?"complete":"partial")); complete=complete&&faceComplete;
        } catch(const Standard_Failure&) {
            trims.set("reason",std::string("kernel_trim_export_failed")); complete=false;
        }
    }
    void Finish(val& mesh) {
        val topology=val::object(),outEdges=val::array(),outVertices=val::array();
        topology.set("schema",std::string("MalievKernelTopology.v1"));
        topology.set("identityScope",std::string("single-import-mesh-group-occurrence"));
        topology.set("status",std::string(complete?"complete":"partial"));
        topology.set("coverage",std::string("enumerated-face-trims-only; not-solid-shell-or-instance-membership"));
        for(size_t i=0;i<edgeRecords.size();++i) {
            std::string kind="nonmanifold";
            if(useCounts[i]==1) kind="boundary";
            else if(useCounts[i]==2&&edgeFaces[i].size()==2) kind="two-face";
            else if(useCounts[i]==2&&edgeFaces[i].size()==1) {
                val uses=edgeRecords[i]["uses"];
                const int a=uses[0]["orientation"].as<int>(),b=uses[1]["orientation"].as<int>();
                const bool opposite=(a==TopAbs_FORWARD&&b==TopAbs_REVERSED)||(a==TopAbs_REVERSED&&b==TopAbs_FORWARD);
                kind=opposite&&uses[0]["seam"].as<bool>()&&uses[1]["seam"].as<bool>()?"same-face-seam":"same-face-repeated";
            }
            edgeRecords[i].set("adjacency",kind);
            edgeRecords[i].set("adjacencyStatus",std::string(complete?"complete":"partial"));
            outEdges.set(i,edgeRecords[i]);
        }
        for(size_t i=0;i<vertexRecords.size();++i) outVertices.set(i,vertexRecords[i]);
        for(val& adjacency:faceRecords) { adjacency.set("status",std::string(complete?"complete":"partial")); adjacency.set("source",std::string("mesh.kernelTopology.edges.uses")); }
        topology.set("edges",outEdges); topology.set("vertices",outVertices); mesh.set("kernelTopology",topology);
    }
};
}
