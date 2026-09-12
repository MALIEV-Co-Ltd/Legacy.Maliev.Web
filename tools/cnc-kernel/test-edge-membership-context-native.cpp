#include "kernel-repair.hpp"
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <Geom_ConicalSurface.hxx>
#include <iostream>
#include <stdexcept>
using namespace MalievRepair;
static Boundary Snapshot(const TopoDS_Shape &shape) {
  Boundary b; TopTools_IndexedMapOfShape map; TopExp::MapShapes(shape,map);
  for(int i=1;i<=map.Extent();++i){Item item;item.original=item.mapped=map(i);b.items.push_back(item);}
  for(auto &item:b.items)item.before=Capture(b,item.original,false);
  return b;
}
int main(){int checks=0,failures=0;auto check=[&](bool pass,const char *name){++checks;if(!pass){++failures;std::cerr<<"FAIL "<<name<<'\n';}};
  try{
    Configure(.01,true,60000);Reset();
    const auto edge=BRepBuilderAPI_MakeEdge(gp_Pnt(0,0,0),gp_Pnt(2,0,0)).Edge();
    auto b=Snapshot(edge);const int id=Identity(b,edge,false);
    b.items[id].mapped=edge.Reversed();
    const auto after=Capture(b,b.items[id].mapped,true);
    check(b.items[id].before.orientation==0&&after.orientation==1,"actual opposite parent occurrence contexts");
    check(MemberSet(b.items[id].before.membership)!=MemberSet(after.membership),"raw composed vertex orientations differ and remain diagnostic");
    auto &e=EvidenceStore();e.boundaries.push_back(b);e.current=0;
    End(edge.Reversed(),Handle(Standard_Transient)());
    check(e.boundaries[0].finalCorrespondenceComplete,"same native edge has complete final correspondence");
    check(e.boundaries[0].changedSourceGeometry==0,"opposite occurrence leaves source geometry unchanged");
    check(e.boundaries[0].changedMembership==0,"equivalent edge-local oriented vertex membership must not reject");
    auto captured=e.boundaries[0];auto item=captured.items[id];
    check(SourceItemMembershipPreserved(captured,item),"both consumers share the oriented edge-local predicate");
    auto bad=item;bad.after.orientation=bad.before.orientation;
    check(!SourceItemMembershipPreserved(captured,bad),"endpoint senses flip with unchanged parent is a genuine change");
    for(int orientation:{2,3,4,-1}){bad=item;bad.after.orientation=orientation;check(!SourceItemMembershipPreserved(captured,bad),"unsupported parent orientation cannot be inverted");}
    for(const std::string &members:{"", "1:0", "1:0 2:1 2:1", "1:0 1:0", "-1:0 2:1", "-2:0 2:1", "9999999999999999999999999:0 2:1", "0:0 2:1", "1:2 2:1", "1:3 2:1", "1:0junk 2:1", "x:0 2:1"}){
      bad=item;bad.after.membership=members;check(!SourceItemMembershipPreserved(captured,bad),"missing duplicate malformed unmapped or unsupported vertex occurrence rejects");
    }
    const auto circle=BRepBuilderAPI_MakeEdge(gp_Circ(gp_Ax2(),2)).Edge();auto closed=Snapshot(circle);const int ci=Identity(closed,circle,false);closed.items[ci].mapped=circle.Reversed();closed.items[ci].after=Capture(closed,closed.items[ci].mapped,true);
    check(SourceItemMembershipPreserved(closed,closed.items[ci]),"closed edge retains same vertex twice with opposite local senses");
    auto ambiguous=closed;ambiguous.items.push_back(closed.items[1]);ambiguous.items[ci].after=Capture(ambiguous,ambiguous.items[ci].mapped,true);
    check(!SourceItemMembershipPreserved(ambiguous,ambiguous.items[ci]),"actual ambiguous native vertex mapping cannot become local membership");
    const double pi=std::acos(-1.0);Handle(Geom_Surface) cone=new Geom_ConicalSurface(gp_Ax3(),pi/4,1);
    const auto face=BRepBuilderAPI_MakeFace(cone,0,2*pi,-std::sqrt(2.),0,Precision::Confusion()).Face();auto cap=Snapshot(face);
    const int fi=Identity(cap,face,false);for(auto &entry:cap.items){if(entry.original.ShapeType()==TopAbs_EDGE)entry.mapped=entry.original.Reversed();entry.after=Capture(cap,entry.mapped,true);entry.sourceGeometryUnchanged=entry.before.geometry==entry.after.geometry;}
    SourceBound declaration;declaration.face=face;declaration.wire=cap.items[fi].before.faceLoops[0].nativeWire;declaration.outer=true;declaration.boundSense=declaration.wire.Orientation()==TopAbs_FORWARD;declaration.sourceFaceSense=declaration.effectiveFaceSense=true;
    e.sourceBounds.clear();e.sourceBounds.push_back(declaration);
    const auto result=AssessSourceConeRegion(cap,cap.items[fi]);if(result.status!="bounded-source-cone-cap")std::cerr<<"CONE "<<result.reason<<'\n';
    check(result.status=="bounded-source-cone-cap","actual cone adapter accepts equivalent edge-local endpoint contexts");
    auto changed=cap;const int use=changed.items[fi].after.faceLoops[0].uses[0].first;changed.items[use].before.first+=.25;
    check(AssessSourceConeRegion(changed,changed.items[fi]).status=="unavailable","cone native parameter range gate remains independent");
    changed=cap;changed.items[use].sourceGeometryUnchanged=false;
    check(AssessSourceConeRegion(changed,changed.items[fi]).status=="unavailable","cone source geometry gate remains independent");
    changed=cap;changed.items[use].after.orientation=changed.items[use].before.orientation;
    check(AssessSourceConeRegion(changed,changed.items[fi]).status=="unavailable","cone rejects genuine local endpoint reversal");
    changed=cap;changed.items[fi].after.faceLoops[0].uses[0].second=1-changed.items[fi].after.faceLoops[0].uses[0].second;
    check(AssessSourceConeRegion(changed,changed.items[fi]).status=="unavailable","face-relative cone coedge cycle is not normalized away");
  }catch(const Standard_Failure &f){++failures;std::cerr<<"NATIVE "<<f.GetMessageString()<<'\n';}catch(const std::exception &f){++failures;std::cerr<<f.what()<<'\n';}
  std::cout<<"edge membership context "<<checks<<" checks "<<failures<<" failures\n";return failures?1:0;
}
