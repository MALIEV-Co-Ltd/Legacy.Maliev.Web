#include "kernel-target-query-profile.hpp"
#include <stdexcept>
#include <iostream>
#if defined(PROFILE_NATIVE) && !defined(PROFILE_HELPER_TU)
#include "kernel-target-query.hpp"
#include <BRepPrimAPI_MakeBox.hxx>
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeVertex.hxx>
#include <BRepClass3d_BndBoxTree.hxx>
#include <Geom_BezierCurve.hxx>
#include <Geom_Line.hxx>
#include <GeomAdaptor_Curve.hxx>
#include <Geom2d_BezierCurve.hxx>
#include <Geom2d_Line.hxx>
#include <Geom2dAdaptor_Curve.hxx>
#include <TColgp_Array1OfPnt.hxx>
#include <TColgp_Array1OfPnt2d.hxx>
#include <Extrema_ExtCC.hxx>
#include <Extrema_ExtCC2d.hxx>
#include <Extrema_POnCurv.hxx>
#include <Extrema_POnCurv2d.hxx>
#include <vector>
#endif
#ifdef PROFILE_HELPER_TU
extern "C" const void* ProfileOtherStore() { return &MalievTargetQueryProfile::Store(); }
extern "C" void ProfileOtherPhase() { MalievTargetQueryProfile::PhaseScope scope(MalievTargetQueryProfile::Phase::Ancestry); }
#else
extern "C" const void* ProfileOtherStore();
extern "C" void ProfileOtherPhase();
using namespace MalievTargetQueryProfile;
static unsigned checks=0;
static void Check(bool v) { ++checks; if(!v) {std::cerr<<"FAIL profile assertion "<<checks<<"\n";throw std::runtime_error("profile assertion "+std::to_string(checks));} }
static void Deep(unsigned n) { PhaseScope s(Phase::Query); if(n) Deep(n-1); }
int main() {
 Check(ProfileOtherStore()==&Store());
 const auto clocks=Store().clockReads;
 { PhaseScope s(Phase::Query); ProfileOtherPhase(); Count(Counter::Points); }
 Check(Store().clockReads==clocks); Check(!Active());
 {
  BatchScope batch(std::string(64,'a'));
  Bind("source","hash","import","topology","session","batch","membership",2);
  { PhaseScope outer(Phase::Batch); { PhaseScope q(Phase::Query); ProfileOtherPhase(); } }
 }
 auto first=ReadLastCompletedSnapshot();
 Check(first.valid); Check(first.phases[size_t(Phase::Batch)].calls==1);
 Check(first.phases[size_t(Phase::Query)].calls==1); Check(first.phases[size_t(Phase::Ancestry)].calls==1);
 uint64_t sum=0;for(const auto& p:first.phases)sum+=p.exclusiveNs;
 Check(sum==first.phases[size_t(Phase::Batch)].inclusiveNs);
 Check(!Active());
 {
  BatchScope b(std::string(64,'b'));Bind("s","h","i","t","x","y","membership",1);
  try { PhaseScope q(Phase::Query); throw std::runtime_error("native-style"); } catch(const std::runtime_error&) {}
 }
 auto unwound=ReadLastCompletedSnapshot();Check(unwound.valid);Check(unwound.phases[size_t(Phase::Query)].unwinds==1);
 Check(unwound.phases[size_t(Phase::Ancestry)].calls==0);
 {
  BatchScope outer(std::string(64,'c'));Bind("s","h","i","t","x","outer","membership",1);
  auto* original=Active();{BatchScope inner(std::string(64,'d'));Check(Active()!=original);}
  Check(Active()==original);ProfileOtherPhase();
 }
 Check(ReadLastCompletedSnapshot().phases[size_t(Phase::Ancestry)].calls==1);
 {BatchScope b(std::string(64,'a'));Bind("s","h","i","t","x","y","membership",1);Deep(40);}
 Check(ReadLastCompletedSnapshot().overflow);Check(!ReadLastCompletedSnapshot().valid);Check(!Active());
 {BatchScope b("not-a-sha");Bind("s","h","i","t","x","y","membership",1);}
 Check(!ReadLastCompletedSnapshot().valid);
 {BatchScope b(std::string(64,'a'));Bind(std::string(600,'s'),"h","i","t","x","y","membership",1);}
 Check(ReadLastCompletedSnapshot().truncated);Check(!ReadLastCompletedSnapshot().valid);
 {BatchScope b(std::string(64,'a'));Bind("s","h","i","t","x","y","membership",1);Count(Counter::Points,UINT64_MAX);Count(Counter::Points);}
 Check(ReadLastCompletedSnapshot().overflow);Check(!ReadLastCompletedSnapshot().valid);
 Check(sizeof(Snapshot)<32768);
#ifdef PROFILE_NATIVE
 const auto beforeLoad=Store().clockReads;
 auto target=MalievKernel::TargetQuery::MakeTarget(BRepPrimAPI_MakeBox(20,20,20).Solid());
 Check(target.Available());Check(Store().clockReads==beforeLoad);
 {
  BatchScope batch(std::string(64,'a'));Bind("actual-box","h","i","t","x","native-cross-tu","membership",1);
  PhaseScope total(Phase::Batch);
  const auto result=target.Query(gp_Pnt(10,10,10),MalievKernel::TargetQuery::QueryMode::Membership);
  Check(result.membership.available);Check(result.membership.state==MalievKernel::TargetQuery::QueryState::In);
 }
 const auto native=ReadLastCompletedSnapshot();Check(native.valid);
 Check(native.phases[size_t(Phase::Query)].calls==1);
 Check(native.phases[size_t(Phase::Membership)].calls==1);
 Check(native.phases[size_t(Phase::Ancestry)].calls==1);
 Check(native.phases[size_t(Phase::SegmentExtrema)].calls>0);
 Check(native.phases[size_t(Phase::IntersectorCore)].calls>0);
 Check(native.phases[size_t(Phase::Distance)].calls==0);
 for(const char* name:{"lineBoxReject","lineAccept","lineEdgePrepare","lineEdgeExtrema","lineEdgeResults"}) {
  bool found=false;for(size_t i=0;i<PhaseCount;++i)if(std::strcmp(PhaseName(i),name)==0)found=true;
  if(!found)std::cerr<<"Missing selector phase "<<name<<"\n";Check(found);
 }
 const TopoDS_Edge edge=BRepBuilderAPI_MakeEdge(gp_Pnt(-1,0,0),gp_Pnt(1,0,0)).Edge();
 const TopoDS_Vertex vertex=BRepBuilderAPI_MakeVertex(gp_Pnt(0,0,0)).Vertex();TopTools_IndexedMapOfShape map;map.Add(edge);map.Add(vertex);
 BRepClass3d_BndBoxTreeSelectorLine selector(map);selector.SetCurrentLine(gp_Lin(gp_Pnt(0,-1,0),gp_Dir(0,1,0)),2);
 {
  BatchScope batch(std::string(64,'a'));Bind("literal-selector","h","i","t","x","selector","membership",1);PhaseScope total(Phase::Batch);
  Bnd_Box nearBox,farBox;nearBox.Add(gp_Pnt(-.1,-.1,-.1));nearBox.Add(gp_Pnt(.1,.1,.1));farBox.Add(gp_Pnt(10,10,10));farBox.Add(gp_Pnt(11,11,11));
  Check(!selector.Reject(nearBox));Check(selector.Reject(farBox));Check(selector.Accept(1));Check(selector.Accept(2));Check(selector.IsCorrect());
  Check(selector.GetNbEdgeParam()==1);Check(selector.GetNbVertParam()==1);TopoDS_Edge selectedEdge;TopoDS_Vertex selectedVertex;double ep,lp,vp;
  selector.GetEdgeParam(1,selectedEdge,ep,lp);selector.GetVertParam(1,selectedVertex,vp);Check(selectedEdge.IsSame(edge));Check(selectedVertex.IsSame(vertex));Check(ep==1);Check(lp==1);Check(vp==1);
  std::cout<<"SELECTOR crossing edgeParameter="<<ep<<" lineParameter="<<lp<<" vertexParameter="<<vp<<" correct="<<selector.IsCorrect()<<"\n";
  selector.ClearResults();selector.SetCurrentLine(gp_Lin(gp_Pnt(-1,1,0),gp_Dir(1,0,0)),2);Check(!selector.Accept(1));Check(!selector.IsCorrect());Check(selector.GetNbEdgeParam()==0);
  std::cout<<"SELECTOR parallel accepted=0 correct="<<selector.IsCorrect()<<" edgeParameters="<<selector.GetNbEdgeParam()<<"\n";
 }
 const auto selected=ReadLastCompletedSnapshot();Check(selected.valid);
 Check(selected.counters[size_t(Counter::LineBoxes)]==2);Check(selected.counters[size_t(Counter::LineBoxesRejected)]==1);
 Check(selected.counters[size_t(Counter::LineEdgeCandidates)]==2);Check(selected.counters[size_t(Counter::LineVertexCandidates)]==1);
 Check(selected.counters[size_t(Counter::LineExtremaDone)]==2);Check(selected.counters[size_t(Counter::LineExtremaNotDone)]==0);Check(selected.counters[size_t(Counter::LineExtremaParallel)]==1);
 Check(selected.counters[size_t(Counter::LineNbExt)]==1);Check(selected.counters[size_t(Counter::LineNearEdgeExtrema)]==1);Check(selected.counters[size_t(Counter::LineAcceptedEdges)]==1);Check(selected.counters[size_t(Counter::LineAcceptedVertices)]==1);
 Check(selected.phases[size_t(Phase::LineEdgeExtrema)].calls==2);Check(selected.phases[size_t(Phase::LineVertex)].calls==1);
 Check(selected.support[size_t(SupportGroup::LineEdgeExtrema)][0].calls==2);
 // Missing hooks, accidental 2D instrumentation, or changed original extrema
 // outputs must fail at the linked specialization, not a phase counter mock.
 Check(PhaseCount==28);
 std::vector<size_t> genericPhases;
 for(const char* name:{"genericEcc3d","genericEccPrepare","genericEccLength1","genericEccLength2","genericEccFinder"}) {
  size_t found=PhaseCount;for(size_t i=0;i<PhaseCount;++i)if(std::strcmp(PhaseName(i),name)==0)found=i;
  Check(found<PhaseCount);genericPhases.push_back(found);
 }
 TColgp_Array1OfPnt poles(1,3);poles(1)=gp_Pnt(-1,1,0);poles(2)=gp_Pnt(0,-1,0);poles(3)=gp_Pnt(1,1,0);
 Handle(Geom_BezierCurve) curve=new Geom_BezierCurve(poles);Handle(Geom_Line) line=new Geom_Line(gp_Pnt(0,-1,0),gp_Dir(1,0,0));
 GeomAdaptor_Curve c3(curve,0,1),l3(line,-2,2);
 auto solve3=[&](){Extrema_ExtCC e(c3,l3,0,1,-2,2);Check(e.IsDone());Check(!e.IsParallel());Check(e.NbExt()>0);std::vector<double> values;for(int i=1;i<=e.NbExt();++i){Extrema_POnCurv p,q;e.Points(i,p,q);values.push_back(e.SquareDistance(i));values.push_back(p.Parameter());values.push_back(q.Parameter());}Check(e.SquareDistance(1)==1);return values;};
 const auto beforeGeneric=Store().clockReads;const auto expected3=solve3();Check(Store().clockReads==beforeGeneric);
 {BatchScope b(std::string(64,'a'));Bind("generic3d","h","i","t","s","b","membership",1);PhaseScope total(Phase::Batch);Check(solve3()==expected3);}
 const auto generic=ReadLastCompletedSnapshot();Check(generic.valid);for(const auto i:genericPhases)Check(generic.phases[i].calls>0);
 uint64_t genericSum=0;for(const auto& p:generic.phases)genericSum+=p.exclusiveNs;Check(genericSum==generic.phases[size_t(Phase::Batch)].inclusiveNs);
 TColgp_Array1OfPnt2d poles2(1,3);poles2(1)=gp_Pnt2d(-1,1);poles2(2)=gp_Pnt2d(0,-1);poles2(3)=gp_Pnt2d(1,1);
 Handle(Geom2d_BezierCurve) curve2=new Geom2d_BezierCurve(poles2);Handle(Geom2d_Line) line2=new Geom2d_Line(gp_Pnt2d(0,-1),gp_Dir2d(1,0));Geom2dAdaptor_Curve c2(curve2,0,1),l2(line2,-2,2);
 auto solve2=[&](){Extrema_ExtCC2d e(c2,l2,0,1,-2,2);Check(e.IsDone());Check(!e.IsParallel());Check(e.NbExt()>0);std::vector<double> values;for(int i=1;i<=e.NbExt();++i){Extrema_POnCurv2d p,q;e.Points(i,p,q);values.push_back(e.SquareDistance(i));values.push_back(p.Parameter());values.push_back(q.Parameter());}Check(e.SquareDistance(1)==1);return values;};
 const auto before2=Store().clockReads;const auto expected2=solve2();Check(Store().clockReads==before2);
 {BatchScope b(std::string(64,'a'));Bind("generic2d","h","i","t","s","b","membership",1);PhaseScope total(Phase::Batch);Check(solve2()==expected2);}
 const auto generic2=ReadLastCompletedSnapshot();Check(generic2.valid);for(const auto i:genericPhases)Check(generic2.phases[i].calls==0);
 std::cout<<"GENERIC actual3D phases positive; actual2D phases zero; inactive clock unchanged; ordered extrema values identical\n";
#endif
 std::cout<<"PASS profile counter checks "<<checks<<" failures 0\n";
}
#endif
