#pragma once
#include <BRepClass3d_SolidClassifier.hxx>
#include <BRepExtrema_DistShapeShape.hxx>
#include <BRepBuilderAPI_MakeVertex.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <BRepCheck_Shell.hxx>
#include <BRep_Builder.hxx>
#include <BRep_Tool.hxx>
#include <Precision.hxx>
#include <TopExp_Explorer.hxx>
#include <TopExp.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Iterator.hxx>
#include <TopoDS_Compound.hxx>
#include <TopoDS_Shell.hxx>
#include <TopTools_IndexedMapOfShape.hxx>
#include <cmath>
#include <memory>
#include <set>
#include <string>
#include <vector>
namespace MalievKernel { namespace TargetQuery {
enum class QueryState { Unknown, In, Out, On };
enum class QueryMode { Membership, Distance, Both };
inline const char* StateName(QueryState s) { return s==QueryState::In?"IN":s==QueryState::Out?"OUT":s==QueryState::On?"ON":"UNKNOWN"; }
inline const char* NativeStateName(TopAbs_State s) { return s==TopAbs_IN?"IN":s==TopAbs_OUT?"OUT":s==TopAbs_ON?"ON":"UNKNOWN"; }
struct Membership {
 QueryState state=QueryState::Unknown;
 bool available=false,invoked=false,rejected=false;
 TopAbs_State raw=TopAbs_UNKNOWN;
 std::string reason="target_unavailable",completion="not-invoked-or-exception",sourceFaceId;
};
struct Support { std::string kind; std::vector<std::string> sourceIds; };
struct Distance {
 bool available=false;
 double valueMm=0; // Serialized as null unless available; never a failure distance.
 std::string reason="target_unavailable",supportStatus="unavailable";
 std::vector<Support> supports;
};
struct PointResult { Membership membership; Distance distance; };
// Only observable final native states. No claim about hidden historical subqueries.
inline Membership DecodeMembership(TopAbs_State raw,bool rejected,bool returned,bool witness) {
 Membership m;m.raw=raw;m.rejected=rejected;m.invoked=returned;
 if(!returned) { m.reason="classifier_not_completed";return m; }
 if(rejected) { m.reason="classifier_rejected";return m; }
 if(raw==TopAbs_OUT&&!witness) { m.reason="classifier_out_completion_unobservable";m.completion="unresolved-public-OUT";return m; }
 if(raw==TopAbs_IN||raw==TopAbs_ON||raw==TopAbs_OUT) {
  m.available=true;m.reason.clear();m.state=raw==TopAbs_IN?QueryState::In:raw==TopAbs_ON?QueryState::On:QueryState::Out;
  m.completion=raw==TopAbs_IN?"returned-numeric-IN":raw==TopAbs_ON?"returned-numeric-ON":"source-face-witnessed-OUT";
 } else m.reason="classifier_state_unavailable";
 return m;
}
inline bool InfinitePointAccepted(TopAbs_State raw,bool rejected,bool returned) { return returned&&!rejected&&raw==TopAbs_OUT; }
inline Distance DecodeDistance(bool returned,bool done,int count,double value,bool inner) {
 Distance d;
 if(!returned||!done) d.reason="distance_not_completed";
 else if(count<=0) d.reason="distance_no_solutions";
 else if(inner) d.reason="unexpected_inner_solid_solution";
 else if(!std::isfinite(value)||value<0) d.reason="distance_nonfinite_or_negative";
 else { d.available=true;d.valueMm=value;d.reason.clear(); }
 return d;
}
struct SourceShape { TopoDS_Shape shape; std::string id; SourceShape(const TopoDS_Shape& s,const std::string& i):shape(s),id(i){} };
struct Inventory { std::string bodyId="native-body"; std::vector<SourceShape> faces,edges,vertices; };
inline std::string JoinShape(const TopoDS_Shape& shape,const std::vector<SourceShape>& list,bool oriented=false) {
 if(shape.IsNull())return "";std::string id;size_t count=0;
 for(const auto& row:list)if(oriented?row.shape.IsEqual(shape):row.shape.IsSame(shape)){id=row.id;++count;}
 return count==1?id:"";
}
inline Inventory NativeInventory(const TopoDS_Shape& shape) {
 Inventory inv;
 for(TopExp_Explorer e(shape,TopAbs_FACE);e.More();e.Next())inv.faces.emplace_back(e.Current(),inv.bodyId+"/face-"+std::to_string(inv.faces.size()));
 for(auto type:{TopAbs_EDGE,TopAbs_VERTEX}) {
  TopTools_IndexedMapOfShape map;TopExp::MapShapes(shape,type,map);
  auto& rows=type==TopAbs_EDGE?inv.edges:inv.vertices;
  for(int i=1;i<=map.Extent();++i)rows.emplace_back(map(i),inv.bodyId+(type==TopAbs_EDGE?"/edge-":"/vertex-")+std::to_string(i));
 }
 return inv;
}
class Target {
 struct State {
  TopoDS_Shape solid;TopoDS_Compound boundary;Inventory inventory;
  BRepClass3d_SolidClassifier classifier;BRepExtrema_DistShapeShape distance;
 };
 std::unique_ptr<State> data;
public:
 std::string reason="target_unavailable";double maxTopologyToleranceMm=0;
 Target()=default;Target(Target&&)=default;Target& operator=(Target&&)=default;
 bool Available() const { return bool(data); }
 const Inventory& SourceInventory() const { return data->inventory; }
 Target(const TopoDS_Shape& shape,const Inventory& inventory,bool millimeters=true) {
  try {
   if(!millimeters){reason="units_unverified";return;}
   if(shape.IsNull()||shape.ShapeType()!=TopAbs_SOLID){reason="single_solid_required";return;}
   size_t shells=0;TopoDS_Shell shell;
   for(TopoDS_Iterator it(shape);it.More();it.Next()) { if(it.Value().ShapeType()!=TopAbs_SHELL){reason="solid_child_not_shell";return;}shell=TopoDS::Shell(it.Value());++shells; }
   if(shells!=1){reason="single_shell_required";return;}
   BRepCheck_Shell check(shell);
   if(check.Closed()!=BRepCheck_NoError||check.Orientation()!=BRepCheck_NoError||!BRepCheck_Analyzer(shape,Standard_True,Standard_False).IsValid()){reason="native_solid_invalid";return;}
   std::set<std::string> ids;size_t count=0;
   if(inventory.bodyId.empty()||inventory.faces.empty()){reason="face_inventory_incomplete";return;}
   for(const auto& row:inventory.faces)if(row.id.empty()||row.shape.IsNull()||row.shape.ShapeType()!=TopAbs_FACE||!ids.insert(row.id).second){reason="face_inventory_ambiguous";return;}
   for(TopExp_Explorer e(shell,TopAbs_FACE);e.More();e.Next()) { ++count;if(JoinShape(e.Current(),inventory.faces,true).empty()){reason="face_inventory_incomplete";return;} }
   if(count!=inventory.faces.size()){reason="face_inventory_incomplete";return;}
   std::unique_ptr<State> next(new State);next->solid=shape;next->inventory=inventory;
   next->classifier.Load(shape);next->classifier.PerformInfinitePoint(Precision::Confusion());
   if(!InfinitePointAccepted(next->classifier.State(),next->classifier.Rejected(),true)){reason="infinite_point_not_outside";return;}
   BRep_Builder builder;builder.MakeCompound(next->boundary);
   for(const auto& row:inventory.faces)builder.Add(next->boundary,row.shape);
   for(auto type:{TopAbs_FACE,TopAbs_EDGE,TopAbs_VERTEX})for(TopExp_Explorer e(shape,type);e.More();e.Next()) {
    double tolerance=type==TopAbs_FACE?BRep_Tool::Tolerance(TopoDS::Face(e.Current())):type==TopAbs_EDGE?BRep_Tool::Tolerance(TopoDS::Edge(e.Current())):BRep_Tool::Tolerance(TopoDS::Vertex(e.Current()));
    if(!std::isfinite(tolerance)||tolerance<0){reason="topology_tolerance_unavailable";return;}maxTopologyToleranceMm=std::max(maxTopologyToleranceMm,tolerance);
   }
   next->distance.SetDeflection(Precision::Confusion());next->distance.LoadS1(next->boundary);
   data=std::move(next);reason.clear();
  }catch(const Standard_Failure&){reason="native_admission_exception";}
 }
 PointResult Query(const gp_Pnt& p,QueryMode mode) {
  PointResult result;
  if(!data)return result;
  if(!std::isfinite(p.X())||!std::isfinite(p.Y())||!std::isfinite(p.Z())) { result.membership.reason=result.distance.reason="point_nonfinite";return result; }
  if(mode!=QueryMode::Distance)try {
   data->classifier.Perform(p,Precision::Confusion());
   const std::string faceId=JoinShape(data->classifier.Face(),data->inventory.faces);
   result.membership=DecodeMembership(data->classifier.State(),data->classifier.Rejected(),true,!faceId.empty());
   result.membership.sourceFaceId=faceId;
  }catch(const Standard_Failure&){result.membership=DecodeMembership(TopAbs_UNKNOWN,false,false,false);result.membership.reason="classifier_exception";}
  if(mode!=QueryMode::Membership)try {
   data->distance.LoadS2(BRepBuilderAPI_MakeVertex(p).Vertex());
   const bool returned=data->distance.Perform(),done=data->distance.IsDone();
   const int count=done?data->distance.NbSolution():0;
   result.distance=DecodeDistance(returned,done,count,done&&count>0?data->distance.Value():0,data->distance.InnerSolution());
   if(result.distance.available) {
    result.distance.supportStatus="complete";std::set<std::string> seen;
    for(int i=1;i<=count;++i) {
     const TopoDS_Shape support=data->distance.SupportOnShape1(i);
     const auto kind=data->distance.SupportTypeShape1(i);
     const std::string id=JoinShape(support,kind==BRepExtrema_IsInFace?data->inventory.faces:kind==BRepExtrema_IsOnEdge?data->inventory.edges:data->inventory.vertices);
     if(id.empty()){result.distance.supportStatus="ambiguous_or_unavailable";continue;}
     if(seen.insert(id).second){Support s;s.kind=kind==BRepExtrema_IsInFace?"face":kind==BRepExtrema_IsOnEdge?"edge":"vertex";s.sourceIds.push_back(id);result.distance.supports.push_back(s);}
    }
   }
  }catch(const Standard_Failure&){result.distance=Distance();result.distance.reason="distance_exception";}
  return result;
 }
};
inline Target MakeTarget(const TopoDS_Shape& shape) { return Target(shape,NativeInventory(shape)); }
} }
