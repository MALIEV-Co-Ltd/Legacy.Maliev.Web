// Independent primitive/boolean counterparts; retain the rejected revolved sources.
#include <BRepBuilderAPI_Transform.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <BRepGProp.hxx>
#include <GProp_GProps.hxx>
#include <STEPControl_Writer.hxx>
#include <StepData_StepWriter.hxx>
#include <StepData_Protocol.hxx>
#include <XSControl_WorkSession.hxx>
#include <BRepPrimAPI_MakeBox.hxx>
#include <BRepAlgoAPI_Cut.hxx>
#include <fstream>
#include <iostream>
#include <string>
#include <stdexcept>
#include <emscripten.h>
#include <BRepPrimAPI_MakeCylinder.hxx>
#include <BRepPrimAPI_MakeCone.hxx>
#include <BRepPrimAPI_MakeTorus.hxx>
#include <BRepAlgoAPI_Fuse.hxx>
void save(TopoDS_Shape shape,const std::string& name,bool placed) {
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
TopoDS_Shape cylinder(double r,double z,double h,bool reverse,double x=0,double y=0) {
  return BRepPrimAPI_MakeCylinder(gp_Ax2(gp_Pnt(x,y,reverse?z+h:z),reverse?-gp::DZ():gp::DZ()),r,h).Shape();
}
TopoDS_Shape cone(double low,double high,double z,double h,bool reverse) {
  return BRepPrimAPI_MakeCone(gp_Ax2(gp_Pnt(0,0,reverse?z+h:z),reverse?-gp::DZ():gp::DZ()),reverse?high:low,reverse?low:high,h).Shape();
}
int main(int argc,char** argv) {
  const std::string selected=argc>1?argv[1]:"all";
  for(const std::string family:{"outside-chamfer","countersink","drill-point","tapered-through","rotational-groove","open-bore"}) {
    if(selected!="all"&&selected!=family)continue;
    for(int mode=0;mode<3;++mode) {
      bool reverse=mode==2;TopoDS_Shape s;
      if(family=="outside-chamfer")s=BRepAlgoAPI_Fuse(cylinder(10,0,8,reverse),cone(10,8,8,2,reverse)).Shape();
      if(family=="countersink")s=BRepAlgoAPI_Cut(BRepAlgoAPI_Cut(cylinder(10,0,10,reverse),cylinder(3,0,10,reverse)).Shape(),cone(3,5,8,2,reverse)).Shape();
      if(family=="drill-point")s=BRepAlgoAPI_Cut(BRepAlgoAPI_Cut(cylinder(10,0,10,reverse),cylinder(3,4,6,reverse)).Shape(),cone(0,3,2,2,reverse)).Shape();
      if(family=="tapered-through")s=BRepAlgoAPI_Cut(cylinder(10,0,10,reverse),cone(1,2,0,10,reverse)).Shape();
      if(family=="rotational-groove")s=BRepAlgoAPI_Cut(cylinder(12,0,10,reverse),BRepPrimAPI_MakeTorus(gp_Ax2(gp_Pnt(0,0,5),reverse?-gp::DZ():gp::DZ()),12,3).Shape()).Shape();
      if(family=="open-bore")s=BRepAlgoAPI_Cut(BRepPrimAPI_MakeBox(gp_Pnt(-10,-5,0),20,10,10).Shape(),cylinder(3,0,10,reverse,0,5)).Shape();
      save(s,"analytic-primitive-"+family+(mode==1?"-placed":mode==2?"-reversed":""),mode==1);
    }
  }
  return 0;
}
