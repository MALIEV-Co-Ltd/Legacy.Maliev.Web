// Independent rectangular target and an actual additional through cavity.
#include <BRepPrimAPI_MakeBox.hxx>
#include <BRepPrimAPI_MakeCylinder.hxx>
#include <BRepAlgoAPI_Cut.hxx>
#include <BRepBuilderAPI_Transform.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <STEPControl_Writer.hxx>
#include <StepData_StepWriter.hxx>
#include <StepData_Protocol.hxx>
#include <XSControl_WorkSession.hxx>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <emscripten.h>
int main(int argc,char** argv){try{
    if(argc!=3)throw std::runtime_error("family variant required");
    const std::string family=argv[1],variant=argv[2];const bool reversed=variant=="reversed";
    TopoDS_Shape shape=BRepPrimAPI_MakeBox(gp_Ax2(gp_Pnt(2,reversed?23:3,reversed?17:5),reversed?-gp::DZ():gp::DZ(),gp::DX()),30,20,12).Shape();
    if(family=="cavity")shape=BRepAlgoAPI_Cut(shape,BRepPrimAPI_MakeCylinder(gp_Ax2(gp_Pnt(17,13,4),gp::DZ()),3,14).Shape()).Shape();
    else if(family!="cuboid")throw std::runtime_error("unknown family");
    if(variant=="placed"){gp_Trsf t;t.SetRotation(gp_Ax1(gp::Origin(),gp_Dir(1,2,3)),.713);t.SetTranslationPart(gp_Vec(17,-23,41));shape=BRepBuilderAPI_Transform(shape,t,true).Shape();}
    if(!BRepCheck_Analyzer(shape).IsValid())throw std::runtime_error("native fixture invalid");
    STEPControl_Writer writer;if(writer.Transfer(shape,STEPControl_AsIs)!=IFSelect_RetDone)throw std::runtime_error("STEP transfer failed");
    StepData_StepWriter precise(writer.Model());precise.FloatWriter().SetFormat("%.17E");precise.SendModel(Handle(StepData_Protocol)::DownCast(writer.WS()->Protocol()));
    const std::string name="native-common-route-"+family+(variant=="normal"?"":"-"+variant);
    std::ofstream out("/"+name+".step");if(!precise.Print(out))throw std::runtime_error("STEP write failed");out.close();
    EM_ASM({const n=UTF8ToString($0);require('fs').writeFileSync('/tmp/'+n+'.step',FS.readFile('/'+n+'.step'));},name.c_str());
    std::cout<<name<<" native-valid=true target=30x20x12 cavity-radius="<<(family=="cavity"?3:0)<<"\n";return 0;
}catch(const std::exception& e){std::cerr<<e.what()<<"\n";return 1;}}
