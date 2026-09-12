#include <kernel-target-query.hpp>
#include <kernel-target-query-session.hpp>
#include "js-interface.cpp" // Actual composed import/capture/bind/query/release implementation, no second binding object.
#include <BRepPrimAPI_MakeBox.hxx>
#include <BRepPrimAPI_MakeCylinder.hxx>
#include <BRepPrimAPI_MakeSphere.hxx>
#include <BRepPrimAPI_MakeTorus.hxx>
#include <BRepAlgoAPI_Cut.hxx>
#include <BRepBuilderAPI_Transform.hxx>
#include <gp_Ax1.hxx>
#include <limits>
#include <fstream>
#include <sstream>
#include <BRepTools.hxx>
#include <malloc.h>
#include <emscripten/heap.h>
#include <cmath>
#include <iostream>
static int checks=0,failures=0;
static void Check(bool ok,const char* name) { ++checks; if(!ok) { ++failures; std::cerr<<"FAIL "<<name<<"\n"; } }
static TopoDS_Shape SoleBooleanSolid(const TopoDS_Shape& shape) { TopoDS_Shape solid;int n=0;for(TopExp_Explorer e(shape,TopAbs_SOLID);e.More();e.Next()){solid=e.Current();++n;}Check(n==1,"constructor has exactly one actual solid");return n==1?solid:TopoDS_Shape(); }
static int importLoads=0;
static void Memory(const char* phase) { const auto m=mallinfo();std::cout<<"MEMORY "<<phase<<" nativeAllocatedBytes="<<m.uordblks<<" wasmLinearMemoryBytes="<<emscripten_get_heap_size()<<"\n"; }
class CountingStep : public ImporterStep { public: Result LoadFile(const std::vector<uint8_t>& bytes,const ImportParams& params) override { ++importLoads;return ImporterStep::LoadFile(bytes,params); } };
int main(int argc,char** argv) {
 using namespace MalievKernel::TargetQuery;
 Check(DecodeMembership(TopAbs_OUT,true,true,true).state==QueryState::Unknown,"rejected OUT unavailable");
 Check(DecodeMembership(TopAbs_OUT,false,true,false).state==QueryState::Unknown,"null-face OUT unavailable");
 Check(DecodeMembership(TopAbs_OUT,false,true,true).state==QueryState::Out,"witnessed OUT available");
 Check(DecodeMembership(TopAbs_ON,false,true,false).state==QueryState::On,"ON preserved");
 auto target=MakeTarget(BRepPrimAPI_MakeBox(10,12,14).Shape());
 auto center=target.Query(gp_Pnt(5,6,7),QueryMode::Both);
 Check(center.membership.state==QueryState::In,"actual native box interior");
 Check(center.distance.available&&std::abs(center.distance.valueMm-5)<1e-6,"finite boundary not zero solid-interior distance");
 Check(DecodeMembership(TopAbs_IN,false,false,true).state==QueryState::Unknown,"uncompleted call unavailable");
 Check(DecodeMembership(TopAbs_UNKNOWN,false,true,true).state==QueryState::Unknown,"unexpected native state unavailable");
 Check(InfinitePointAccepted(TopAbs_OUT,false,true),"infinite OUT does not need finite face witness");
 Check(!InfinitePointAccepted(TopAbs_OUT,true,true),"infinite rejected OUT blocks admission");
 BRepClass3d_SolidClassifier fresh;
 Check(fresh.State()==TopAbs_OUT&&!fresh.Rejected()&&fresh.Face().IsNull(),"characterization only: never-performed public OUT is not completion");
 Check(!DecodeDistance(false,true,1,0,false).available,"raw fault uncompleted distance");
 Check(!DecodeDistance(true,false,1,0,false).available,"raw fault IsDone false");
 Check(!DecodeDistance(true,true,0,0,false).available,"raw fault empty distance");
 Check(!DecodeDistance(true,true,1,std::numeric_limits<double>::infinity(),false).available,"raw fault nonfinite distance");
 Check(!DecodeDistance(true,true,1,0,true).available,"inner solid distance forbidden");
 auto box=BRepPrimAPI_MakeBox(10,12,14).Shape();
 auto inv=NativeInventory(box);auto absent=inv;absent.faces.pop_back();
 Check(!Target(box,absent).Available(),"omitted finite face rejected");
 auto duplicate=inv;duplicate.faces.push_back(duplicate.faces.front());
 Check(!Target(box,duplicate).Available(),"ambiguous finite face rejected");
 Check(JoinShape(inv.faces[0].shape,duplicate.faces).empty(),"ambiguous classifier face join unavailable");
 Check(!Target(box,inv,false).Available(),"unverified units unavailable");
 Check(!MakeTarget(TopoDS_Shape()).Available(),"empty shape unavailable");
 Check(!MakeTarget(box.Reversed()).Available(),"physically inside-out solid unavailable");
 TopoDS_Compound compound;BRep_Builder builder;builder.MakeCompound(compound);builder.Add(compound,box);builder.Add(compound,BRepPrimAPI_MakeBox(gp_Pnt(20,0,0),10,12,14).Shape());
 Check(!MakeTarget(compound).Available(),"multiple-solid compound unavailable");
 TopExp_Explorer shell(box,TopAbs_SHELL);Check(!MakeTarget(shell.Current()).Available(),"standalone shell unavailable");
 TopoDS_Shell openShell;builder.MakeShell(openShell);int openFace=0;for(TopExp_Explorer e(box,TopAbs_FACE);e.More();e.Next())if(++openFace>1)builder.Add(openShell,e.Current());
 Check(!MakeTarget(openShell).Available(),"actual open shell unavailable");
 TopoDS_Solid invalidSolid;builder.MakeSolid(invalidSolid);builder.Add(invalidSolid,openShell);Check(!MakeTarget(invalidSolid).Available(),"open solid is not repaired for admission");
 auto cavity=SoleBooleanSolid(BRepAlgoAPI_Cut(box,BRepPrimAPI_MakeBox(gp_Pnt(2,2,2),2,2,2).Shape()).Shape());
 Check(!MakeTarget(cavity).Available(),"nested cavity shells unavailable");
 const gp_Pnt points[]={gp_Pnt(5,6,7),gp_Pnt(20,6,7),gp_Pnt(13,16,7),gp_Pnt(13,16,26),gp_Pnt(0,6,7),gp_Pnt(0,0,7),gp_Pnt(0,0,0)};
 const double distances[]={5,10,5,13,0,0,0};
 gp_Trsf tr;tr.SetRotation(gp_Ax1(gp_Pnt(),gp_Dir(1,2,3)),.713);tr.SetTranslationPart(gp_Vec(17,-23,41));
 int witnessed=0;
 for(int placement=0;placement<3;++placement){
  const auto shape=placement==0?box:placement==1?box.Moved(TopLoc_Location(tr)):BRepBuilderAPI_Transform(box,tr,true).Shape();
  auto t=MakeTarget(shape);Check(t.Available(),"box exact placement admitted");
  for(size_t i=0;i<7;++i){const gp_Pnt p=placement?points[i].Transformed(tr):points[i];auto q=t.Query(p,QueryMode::Both);
   Check(q.distance.available&&std::abs(q.distance.valueMm-distances[i])<1e-6,"placed finite-face/edge/corner numerical distance");
   if(i==0)Check(q.membership.state==QueryState::In,"placed interior IN");
   else if(i>=4)Check(q.membership.state==QueryState::On,"placed exact boundary ON");
   else if(q.membership.available){++witnessed;Check(q.membership.state==QueryState::Out&&!q.membership.sourceFaceId.empty(),"available OUT has actual source witness");}
   else Check(q.membership.raw==TopAbs_OUT&&q.membership.reason=="classifier_out_completion_unobservable","honest null-face OUT unavailable");
  }
 }
 Check(witnessed>0,"nonvacuous actual witnessed OUT");
 const auto cylinder=BRepPrimAPI_MakeCylinder(5,10).Shape();
 const auto ring=SoleBooleanSolid(BRepAlgoAPI_Cut(cylinder,BRepPrimAPI_MakeCylinder(2,10).Shape()).Shape());
 const auto bore=SoleBooleanSolid(BRepAlgoAPI_Cut(box,BRepPrimAPI_MakeCylinder(gp_Ax2(gp_Pnt(5,6,-1),gp_Dir(0,0,1)),2,16).Shape()).Shape());
 struct Case { TopoDS_Shape shape;gp_Pnt point;double distance;QueryState state; };
 const std::vector<Case> cases={
  {cylinder,gp_Pnt(0,0,5),5,QueryState::In},{cylinder,gp_Pnt(0,0,13),3,QueryState::Out},
  {ring,gp_Pnt(3.5,0,5),1.5,QueryState::In},{ring,gp_Pnt(0,0,5),2,QueryState::Out},
  {bore,gp_Pnt(5,6,7),2,QueryState::Out},{bore,gp_Pnt(1,6,7),1,QueryState::In},
  {BRepPrimAPI_MakeSphere(7).Shape(),gp_Pnt(),7,QueryState::In},
  {BRepPrimAPI_MakeTorus(10,2).Shape(),gp_Pnt(),8,QueryState::Out},
  {BRepPrimAPI_MakeCylinder(gp_Ax2(gp_Pnt(0,0,10),gp_Dir(0,0,-1)),5,10).Shape(),gp_Pnt(0,0,5),5,QueryState::In}};
 for(const auto& c:cases)for(bool placed:{false,true}){auto t=MakeTarget(placed?c.shape.Moved(TopLoc_Location(tr)):c.shape);Check(t.Available(),"curved finite native body admitted");auto q=t.Query(placed?c.point.Transformed(tr):c.point,QueryMode::Both);Check(q.distance.available&&std::abs(q.distance.valueMm-c.distance)<1e-6,"curved actual BRep finite distance");Check(q.membership.state==c.state||(c.state==QueryState::Out&&!q.membership.available&&q.membership.reason=="classifier_out_completion_unobservable"),"curved native classification or explicit unsupported OUT");}
 auto later=MakeTarget(BRepPrimAPI_MakeSphere(2).Shape());
 Check(std::abs(target.Query(gp_Pnt(5,6,7),QueryMode::Both).distance.valueMm-5)<1e-6,"owning target survives creation locals and later shape");
 Check(!target.Query(gp_Pnt(std::numeric_limits<double>::infinity(),0,0),QueryMode::Both).distance.available,"nonfinite point rejected");
 Registry registry;Binding binding;binding.sourceGeneration="source-a";binding.sourceBytesHash=std::string(64,'a');binding.nativeImportRevision="import-a";binding.topologyRevision="topology-a";
 std::vector<std::string> sessions;
 for(int i=0;i<4;++i){auto admitted=registry.Add(MakeTarget(box),std::vector<uint8_t>{1,2,3});Check(admitted.available,"four owning independent sessions");sessions.push_back(admitted.id);}
 Check(!registry.Add(MakeTarget(box),std::vector<uint8_t>{4}).available,"fifth session rejects without eviction");
 Check(registry.SourceBytes()==12&&registry.Size()==4,"exact source bytes accounting");
 Check(registry.Bind(sessions[0],{1,2,4},binding)=="source_bytes_mismatch","actual bytes bind not caller hash");
 Check(registry.Bind(sessions[0],{1,2,3},binding).empty(),"exact source bytes bind");
 Check(registry.Bind(sessions[0],{1,2,3},binding).empty(),"identical bind idempotent");
 auto changed=binding;changed.sourceGeneration="changed";Check(registry.Bind(sessions[0],{1,2,3},changed)=="binding_mismatch","different labels cannot rebind");
 Check(registry.Release(sessions[0])&&!registry.Release(sessions[0]),"release idempotent accounting");
 auto replacement=registry.Add(MakeTarget(box),std::vector<uint8_t>{8});Check(replacement.available&&replacement.id!=sessions[0],"session ID never reused");
 Check(registry.Find(sessions[1])!=nullptr&&registry.SourceBytes()==10,"release isolated to owned session");
 Registry budget;auto large=budget.Add(MakeTarget(box),std::vector<uint8_t>(Registry::MaxSourceBytes,7));Check(large.available,"exact source-byte budget admitted");
 Check(!budget.Add(MakeTarget(box),{8}).available,"aggregate byte budget rejects");
 Check(budget.Release(large.id)&&budget.SourceBytes()==0,"source-byte budget recovered after release");
 // Synthetic result faults exercise the same ordered production batch reducer.
 int evaluatedCalls=0;const std::vector<gp_Pnt> ordered={gp_Pnt(1,0,0),gp_Pnt(2,0,0),gp_Pnt(3,0,0)};
 const auto partial=EvaluateOrdered(ordered,QueryMode::Both,[&](const gp_Pnt& p,QueryMode){++evaluatedCalls;if(p.X()==2)throw std::runtime_error("synthetic interruption");PointResult r;r.membership=DecodeMembership(TopAbs_IN,false,true,false);r.distance=DecodeDistance(true,true,1,2,false);return r;});
 Check(partial.status=="partial"&&partial.points.size()==3&&partial.evaluated==1&&evaluatedCalls==2,"interrupted batch retains full suffix and stops evaluation");
 Check(partial.points[2].membership.reason=="batch_evaluation_interrupted"&&!partial.points[2].distance.available,"interrupted suffix explicit unavailable, never zero distance authority");
 const auto mixed=EvaluateOrdered(ordered,QueryMode::Both,[](const gp_Pnt&,QueryMode){PointResult r;r.membership=DecodeMembership(TopAbs_OUT,false,true,false);r.distance=DecodeDistance(true,true,1,3,false);return r;});
 Check(mixed.status=="partial"&&mixed.available==3&&mixed.requested==6,"failed membership preserves available distance in mixed batch");
 const auto none=EvaluateOrdered(ordered,QueryMode::Membership,[](const gp_Pnt&,QueryMode){return PointResult();});Check(none.status=="unavailable"&&none.points.size()==3,"entirely unavailable batch retains points");
 std::ostringstream beforeGeometry,afterGeometry;BRepTools::Write(box,beforeGeometry);target.Query(gp_Pnt(12,3,4),QueryMode::Both);BRepTools::Write(box,afterGeometry);Check(beforeGeometry.str()==afterGeometry.str(),"query preserves original native BRep geometry and placement");
 Check(argc==2,"native harness supplied exact public fixture");
 if(argc==2){
  std::ifstream stream(argv[1],std::ios::binary);std::vector<uint8_t> bytes((std::istreambuf_iterator<char>(stream)),std::istreambuf_iterator<char>());Check(!bytes.empty(),"public source loaded by test");
  val buffer=val::array();for(size_t i=0;i<bytes.size();++i)buffer.set(i,bytes[i]);val options=val::object();options.set("repairErrorBudgetMm",.01);options.set("repairDiagnostics",true);
  std::string retainedId;val imported=val::undefined();
  Memory("before-counted-import");
  {
   Capture capture;auto importer=std::make_shared<CountingStep>();MalievRepair::ConfigureOptions(options);
   imported=ImportFile(importer,buffer,ImportParams(),&capture);capture.Commit();
   Check(importLoads==1,"actual composed opt-in ImportFile performs exactly one native load");
   Check(imported["nativeTargetSession"]["status"].as<std::string>()=="available","actual native capture available");retainedId=imported["nativeTargetSession"]["sessionId"].as<std::string>();
  }
  Memory("after-import-locals-destroyed");
  Binding labels;labels.sourceGeneration="native-counted";labels.sourceBytesHash=std::string(64,'a');labels.nativeImportRevision="counted-import";labels.topologyRevision="counted-topology";
  Check(BindNativeTargetSource(val(retainedId),buffer,BindingValue(labels))["byteEqualityVerified"].as<bool>(),"native bind after import locals destroyed");
  val request=val::object(),points=val::array(),row=val::object(),xyz=val::array();xyz.set(0,17);xyz.set(1,13);xyz.set(2,11);row.set("pointId",std::string("center"));row.set("point",xyz);points.set(0,row);
  request.set("contract",std::string("NativeTargetQueryBatch.v1"));request.set("sessionId",retainedId);request.set("binding",BindingValue(labels));request.set("batchId",std::string("native-count"));request.set("policyVersion",std::string("native-target-numerical-v1"));request.set("mode",std::string("membership-and-distance"));request.set("coordinateSpace",std::string("import-world-mm"));request.set("points",points);
  const val queried=QueryNativeTargetBatch(request);Check(queried["points"][0]["membership"]["state"].as<std::string>()=="IN","actual retained source numerical IN after importer destruction");Check(std::abs(queried["points"][0]["boundaryDistance"]["valueMm"].as<double>()-6)<1e-6,"actual retained source finite boundary distance");
  Memory("after-bound-query");
  Check(ReleaseNativeTarget(val(retainedId))["released"].as<bool>()&&importLoads==1,"bind/query/release add zero imports");
  Memory("after-release");
  // Scope destruction releases an uncommitted provisional session even after successful export.
  {Capture capture;auto importer=std::make_shared<CountingStep>();MalievRepair::ConfigureOptions(options);ImportFile(importer,buffer,ImportParams(),&capture);Check(Sessions().Size()==1,"provisional session exists inside owning capture");}
  Check(Sessions().Size()==0&&Sessions().SourceBytes()==0,"uncommitted provisional capture releases all resources");
 }
 std::cout<<checks<<" checks, "<<failures<<" failures\n"; return failures?1:0;
}
