// Independent public finite profiles. No private CAD or production kernel edits.
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeWire.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_Transform.hxx>
#include <BRepPrimAPI_MakeRevol.hxx>
#include <BRepPrimAPI_MakePrism.hxx>
#include <BRepPrimAPI_MakeBox.hxx>
#include <BRepAlgoAPI_Cut.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <BRepLib.hxx>
#include <BRepGProp.hxx>
#include <GProp_GProps.hxx>
#include <GC_MakeArcOfCircle.hxx>
#include <Geom_TrimmedCurve.hxx>
#include <STEPControl_Writer.hxx>
#include <StepData_StepWriter.hxx>
#include <StepData_Protocol.hxx>
#include <XSControl_WorkSession.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Solid.hxx>
#include <TopoDS_Edge.hxx>
#include <gp_Pln.hxx>
#include <fstream>
#include <iostream>
#include <vector>
#include <string>
#include <stdexcept>
#include <emscripten.h>
using Points=std::vector<gp_Pnt>;
TopoDS_Edge line(gp_Pnt a,gp_Pnt b,bool reversed) {
  auto e=BRepBuilderAPI_MakeEdge(reversed?b:a,reversed?a:b).Edge();
  if(reversed)e.Reverse();return e;
}
TopoDS_Edge arc(gp_Pnt a,gp_Pnt middle,gp_Pnt b,bool reversed) {
  Handle(Geom_TrimmedCurve) c=GC_MakeArcOfCircle(reversed?b:a,middle,reversed?a:b);
  auto e=BRepBuilderAPI_MakeEdge(c).Edge();if(reversed)e.Reverse();return e;
}
TopoDS_Shape revol(Points points,bool reversed,bool groove=false) {
  BRepBuilderAPI_MakeWire wire;
  for(size_t i=0;i<points.size();++i) {
    auto a=points[i],b=points[(i+1)%points.size()];
    wire.Add(groove&&i==2?arc(a,gp_Pnt(9,0,5),b,reversed):line(a,b,reversed));
  }
  return BRepPrimAPI_MakeRevol(BRepBuilderAPI_MakeFace(wire.Wire()).Face(),gp_Ax1(gp::Origin(),gp::DZ())).Shape();
}
TopoDS_Wire race(double radius,double z,bool reversed) {
  gp_Pnt a(0,-radius,z),b(20,-radius,z),c(20,radius,z),d(0,radius,z);
  BRepBuilderAPI_MakeWire w;
  w.Add(line(a,b,reversed));w.Add(arc(b,gp_Pnt(20+radius,0,z),c,reversed));
  w.Add(line(c,d,reversed));w.Add(arc(d,gp_Pnt(-radius,0,z),a,reversed));return w.Wire();
}
TopoDS_Shape channel(bool reversed) {
  auto outer=race(8,2,reversed),inner=race(5,2,reversed);inner.Reverse();
  BRepBuilderAPI_MakeFace face(gp_Pln(gp_Pnt(0,0,2),gp::DZ()),outer);face.Add(inner);
  auto cutter=BRepPrimAPI_MakePrism(face.Face(),gp_Vec(0,0,3)).Shape();
  return BRepAlgoAPI_Cut(BRepPrimAPI_MakeBox(gp_Pnt(-10,-10,0),40,20,5).Shape(),cutter).Shape();
}
void save(TopoDS_Shape shape,const std::string& name,bool placed) {
  if(shape.ShapeType()==TopAbs_SOLID){auto solid=TopoDS::Solid(shape);if(!BRepLib::OrientClosedSolid(solid))throw std::runtime_error("fixture orientation");shape=solid;}
  if(placed){gp_Trsf tr;tr.SetRotation(gp_Ax1(gp::Origin(),gp_Dir(1,2,3)),.713);tr.SetTranslationPart(gp_Vec(17,-23,41));shape=BRepBuilderAPI_Transform(shape,tr,true).Shape();}
  if(!BRepCheck_Analyzer(shape).IsValid())throw std::runtime_error("invalid independent fixture "+name);
  GProp_GProps props;BRepGProp::VolumeProperties(shape,props);if(!(props.Mass()>0))throw std::runtime_error("nonpositive fixture volume "+name);
  STEPControl_Writer writer;if(writer.Transfer(shape,STEPControl_AsIs)!=IFSelect_RetDone)throw std::runtime_error("transfer failed");
  StepData_StepWriter precise(writer.Model());precise.FloatWriter().SetFormat("%.17E");
  precise.SendModel(Handle(StepData_Protocol)::DownCast(writer.WS()->Protocol()));
  std::string file="/"+name+".step";std::ofstream out(file);if(!precise.Print(out))throw std::runtime_error("write failed");out.close();
  EM_ASM({const name=UTF8ToString($0);require('fs').writeFileSync('/tmp/'+name+'.step',FS.readFile('/'+name+'.step'));},name.c_str());
  std::cout<<name<<" volume="<<props.Mass()<<std::endl;
}
int main(int argc,char** argv) {
  const std::string variant=argc>1?argv[1]:"all";
  for(const std::string base:{"outside-chamfer","countersink","drill-point","tapered-through","rotational-groove","closed-channel"}) {
    if(variant!="all"&&variant!=base)continue;
    for(int mode=0;mode<3;++mode){bool reverse=mode==2;TopoDS_Shape s;
      if(base=="outside-chamfer")s=revol({{0,0,0},{10,0,0},{10,0,8},{8,0,10},{0,0,10}},reverse);
      if(base=="countersink")s=revol({{3,0,0},{10,0,0},{10,0,10},{5,0,10},{3,0,8}},reverse);
      if(base=="drill-point")s=revol({{0,0,0},{10,0,0},{10,0,10},{3,0,10},{3,0,4},{0,0,2}},reverse);
      if(base=="tapered-through")s=revol({{1,0,0},{10,0,0},{10,0,10},{2,0,10}},reverse);
      if(base=="rotational-groove")s=revol({{0,0,0},{12,0,0},{12,0,2},{12,0,8},{12,0,10},{0,0,10}},reverse,true);
      if(base=="closed-channel")s=channel(reverse);
      save(s,"analytic-"+base+(mode==1?"-placed":mode==2?"-reversed":""),mode==1);
    }
  }
}
