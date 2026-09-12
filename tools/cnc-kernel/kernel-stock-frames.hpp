#pragma once
#include <BRepBndLib.hxx>
#include <BRepAdaptor_Surface.hxx>
#include <BRepAdaptor_Curve.hxx>
#include <BRepAdaptor_Curve2d.hxx>
#include <Bnd_Box.hxx>
#include <Precision.hxx>
#include <array>
#include <limits>

namespace MalievKernel { namespace StockFrames {
struct Candidate { gp_Ax3 frame;std::string faceId,kind; };
inline bool SameAxes(const gp_Ax3& a,const gp_Ax3& b){return a.XDirection().XYZ().IsEqual(b.XDirection().XYZ(),0)
    &&a.YDirection().XYZ().IsEqual(b.YDirection().XYZ(),0)&&a.Direction().XYZ().IsEqual(b.Direction().XYZ(),0);}
inline gp_Trsf ToFrame(const gp_Ax3& frame){const auto x=frame.XDirection(),y=frame.YDirection(),z=frame.Direction();const auto o=frame.Location().XYZ();gp_Trsf t;
    t.SetValues(x.X(),x.Y(),x.Z(),-x.XYZ().Dot(o),y.X(),y.Y(),y.Z(),-y.XYZ().Dot(o),z.X(),z.Y(),z.Z(),-z.XYZ().Dot(o));return t;}
inline val Bounds(const Bnd_Box& box){val o=val::object();if(box.IsVoid()||box.IsOpen()){o.set("status",std::string("unavailable"));return o;}
    double a,b,c,d,e,f;box.Get(a,b,c,d,e,f);if(!std::isfinite(a)||!std::isfinite(b)||!std::isfinite(c)||!std::isfinite(d)||!std::isfinite(e)||!std::isfinite(f)){o.set("status",std::string("unavailable"));return o;}
    o.set("status",std::string("available"));o.set("min",Triple(gp_Pnt(a,b,c)));o.set("max",Triple(gp_Pnt(d,e,f)));return o;}
struct Box { std::array<double,3> min,max; };
inline bool ReadBox(const val& input,Box& box){
    if(input.isNull()||input.isUndefined()||!input.hasOwnProperty("min")||!input.hasOwnProperty("max"))return false;
    const val lo=input["min"],hi=input["max"];
    if(lo.isNull()||hi.isNull()||lo.isUndefined()||hi.isUndefined()||lo["length"].as<int>()!=3||hi["length"].as<int>()!=3)return false;
    for(int i=0;i<3;++i){box.min[i]=lo[i].as<double>();box.max[i]=hi[i].as<double>();if(!std::isfinite(box.min[i])||!std::isfinite(box.max[i])||box.min[i]>box.max[i])return false;}return true;
}
inline val BoxValue(const Box& box){val v=val::object();v.set("status",std::string("available"));v.set("min",Triple(gp_Pnt(box.min[0],box.min[1],box.min[2])));v.set("max",Triple(gp_Pnt(box.max[0],box.max[1],box.max[2])));return v;}
inline bool Outward(double value,bool upper,double& result){if(!std::isfinite(value))return false;
    result=std::nextafter(value,upper?std::numeric_limits<double>::infinity():-std::numeric_limits<double>::infinity());return std::isfinite(result);}
inline bool TransformBox(const Box& source,const gp_Trsf& t,Box& result){
    for(int row=0;row<3;++row){double low=t.Value(row+1,4),high=low;if(!std::isfinite(low))return false;
        for(int col=0;col<3;++col){const double a=t.Value(row+1,col+1);if(!std::isfinite(a))return false;
            double p=a*source.min[col],q=a*source.max[col],lo,hi;
            if(!Outward(std::min(p,q),false,lo)||!Outward(std::max(p,q),true,hi)
                ||!Outward(low+lo,false,low)||!Outward(high+hi,true,high))return false;}
        result.min[row]=low;result.max[row]=high;}return true;
}
inline void Union(Box& target,const Box& box){for(int i=0;i<3;++i){target.min[i]=std::min(target.min[i],box.min[i]);target.max[i]=std::max(target.max[i],box.max[i]);}}
inline bool AnalyticCurve(GeomAbs_CurveType type){return type==GeomAbs_Line||type==GeomAbs_Circle||type==GeomAbs_Ellipse||type==GeomAbs_Hyperbola||type==GeomAbs_Parabola;}
inline bool CheapAnalytic(const TopoDS_Face& face){try{
    BRepAdaptor_Surface surface(face,Standard_True);const auto type=surface.GetType();
    if(type!=GeomAbs_Plane&&type!=GeomAbs_Cylinder&&type!=GeomAbs_Cone&&type!=GeomAbs_Sphere)return false;
    bool found=false;for(TopExp_Explorer e(face,TopAbs_EDGE);e.More();e.Next()){const auto edge=TopoDS::Edge(e.Current());found=true;
        if(!BRep_Tool::Degenerated(edge)&&!AnalyticCurve(BRepAdaptor_Curve(edge).GetType()))return false;
        if(!AnalyticCurve(BRepAdaptor_Curve2d(edge,face).GetType()))return false;}
    return found;
}catch(const Standard_Failure&){return false;}}
inline val Export(const ExportContext& context,bool millimeters){
    val o=val::object(),ids=val::array(),out=val::array();o.set("schema",std::string("MalievNativeStockFrames.v1"));o.set("bodyId",context.prefix);
    o.set("identityScope",std::string("enclosing-native-import-body-and-source-face-occurrences"));o.set("units",std::string(millimeters?"millimeter":"unverified"));
    o.set("status",std::string("unavailable"));o.set("faceIds",ids);o.set("candidates",out);
    o.set("candidatePolicy",std::string("native-support-frames-in-face-order-plus-world; exact-axis-deduplication; max32"));
    o.set("globalMinimumVolumeCertified",false);o.set("stockSelectionAuthorized",false);
    for(size_t i=0;i<context.sourceFaces.size();++i)ids.set(i,context.sourceFaceRecords[i]["faceId"]);
    if(!millimeters||context.sourceFaces.empty()){o.set("reason",std::string(!millimeters?"millimeter_units_unverified":"no_native_faces"));return o;}
    std::vector<Candidate> candidates;bool truncated=false;
    for(size_t i=0;i<context.sourceFaces.size();++i){try{
        BRepAdaptor_Surface s(context.sourceFaces[i],Standard_True);gp_Ax3 frame;std::string kind;
        switch(s.GetType()){
        case GeomAbs_Plane:frame=s.Plane().Position();kind="plane";break;
        case GeomAbs_Cylinder:frame=s.Cylinder().Position();kind="cylinder";break;
        case GeomAbs_Cone:frame=s.Cone().Position();kind="cone";break;
        case GeomAbs_Sphere:frame=s.Sphere().Position();kind="sphere";break;
        case GeomAbs_Torus:frame=s.Torus().Position();kind="torus";break;
        default:continue;}
        // A reflected source chart is not a reflected stock frame. Construct all
        // three coupled proper axes explicitly; source geometry stays untouched.
        frame=gp_Ax3(frame.Location(),frame.Direction(),frame.XDirection());
        bool seen=false;for(const auto& c:candidates)seen=seen||SameAxes(c.frame,frame);if(seen)continue;
        if(candidates.size()==31){truncated=true;continue;}
        candidates.push_back({frame,context.sourceFaceRecords[i]["faceId"].as<std::string>(),kind});
    }catch(const Standard_Failure&){/* Unsupported candidate does not omit its face from bounds. */}}
    candidates.push_back({gp_Ax3(),"","import-world-fallback"});o.set("candidateLimitReached",truncated);
    bool complete=true;
    for(size_t i=0;i<candidates.size();++i){const auto& c=candidates[i];val r=val::object(),faces=val::array();out.set(i,r);
        r.set("candidateId",context.prefix+"/stock-frame-"+std::to_string(i));r.set("sourceFaceId",c.faceId.empty()?val::null():val(c.faceId));r.set("sourceKind",c.kind);
        r.set("origin",Triple(c.frame.Location()));val axes=val::array();axes.set(0,Triple(c.frame.XDirection()));axes.set(1,Triple(c.frame.YDirection()));axes.set(2,Triple(c.frame.Direction()));r.set("axes",axes);
        r.set("transformDirection",std::string("import-world-to-candidate"));r.set("importWorldToCandidate3x4",Placement(ToFrame(c.frame)));
        r.set("method",std::string("union-of-explicit-per-face-native-enclosures-v2"));
        r.set("numericalPolicy",std::string("native-numerical-source-enclosures; analytic-or-world-box; outward-transform-products-and-sums-v1; separate-topology-expansion"));
        r.set("numericalToleranceMm",Precision::Confusion());r.set("formalIntervalCertificate",false);r.set("faceBounds",faces);
        bool available=true,first=true;Box total{},expanded{};
        for(size_t f=0;f<context.sourceFaces.size();++f){val b=val::object();faces.set(f,b);const auto& source=context.sourceFaces[f];const val record=context.sourceFaceRecords[f];
            b.set("faceId",record["faceId"]);b.set("sourceFaceOccurrenceCandidates",record["assemblyInstance"]["sourceFaceOccurrenceCandidates"]);
            b.set("occurrenceId",record["assemblyInstance"].hasOwnProperty("occurrenceId")?record["assemblyInstance"]["occurrenceId"]:val::null());b.set("status",std::string("unavailable"));
            if(record["assemblyInstance"]["status"].as<std::string>()!="resolved"){available=false;b.set("reason",std::string("source_face_occurrence_unresolved"));continue;}
            try{Box world,worldTolerance,box,tb;
                if(!ReadBox(record["bounds"],world)||!ReadBox(record["toleranceBounds"],worldTolerance)){available=false;b.set("reason",std::string("native_bounds_unavailable"));continue;}
                double tolerance=record["precision"]["maxTopologyTolerance"].as<double>();
                if(!std::isfinite(tolerance)||tolerance<0){available=false;b.set("reason",std::string("native_tolerance_unavailable"));continue;}
                if(c.kind=="import-world-fallback"){
                    box=world;tb=worldTolerance;b.set("method",std::string("same-import-world-face-box"));b.set("arithmeticPolicy",std::string("identity-reuse-no-arithmetic"));
                }else if(CheapAnalytic(source)){
                    Bnd_Box analytic;BRepBndLib::AddOptimal(source.Moved(TopLoc_Location(ToFrame(c.frame))),analytic,Standard_False,Standard_False);
                    Bnd_Box enlarged=analytic;enlarged.Enlarge(tolerance);
                    if(!ReadBox(Bounds(analytic),box)||!ReadBox(Bounds(enlarged),tb)){available=false;b.set("reason",std::string("native_bounds_unavailable"));continue;}
                    b.set("method",std::string("BRepBndLib.AddOptimal-analytic-support-and-boundaries-in-frame"));b.set("arithmeticPolicy",std::string("native-analytic-numerical"));
                }else{
                    if(!TransformBox(world,ToFrame(c.frame),box)||!TransformBox(worldTolerance,ToFrame(c.frame),tb)){available=false;b.set("reason",std::string("native_frame_bounds_nonfinite"));continue;}
                    b.set("method",std::string("outward-transform-of-same-import-world-face-box"));b.set("arithmeticPolicy",std::string("ieee754-nextafter-each-product-and-sum-v1"));
                }
                b.set("bounds",BoxValue(box));b.set("toleranceBounds",BoxValue(tb));b.set("maxTopologyToleranceMm",tolerance);
                b.set("status",std::string("available"));if(first){total=box;expanded=tb;first=false;}else{Union(total,box);Union(expanded,tb);}
            }catch(const Standard_Failure&){available=false;b.set("reason",std::string("native_frame_bounds_failed"));}}
        r.set("bounds",first?Unavailable("native_bounds_unavailable"):BoxValue(total));r.set("toleranceBounds",first?Unavailable("native_bounds_unavailable"):BoxValue(expanded));r.set("status",std::string(available?"available":"unavailable"));
        if(!available)r.set("reason",std::string("incomplete_all_face_bounds"));complete=complete&&available;
    }
    o.set("status",std::string(complete?"complete":"partial"));return o;
}
}}
