// Independent public solids for same-import stock/triangulation export only.
#include <BRepPrimAPI_MakeBox.hxx>
#include <BRepPrimAPI_MakeCylinder.hxx>
#include <BRepBuilderAPI_Transform.hxx>
#include <BRepBuilderAPI_Copy.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <BRep_Builder.hxx>
#include <TopoDS_Compound.hxx>
#include <STEPControl_Writer.hxx>
#include <StepData_StepWriter.hxx>
#include <StepData_Protocol.hxx>
#include <XSControl_WorkSession.hxx>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <emscripten.h>
int main(int argc,char** argv){try{
    if(argc!=3)throw std::runtime_error("family variant required");const std::string family=argv[1],variant=argv[2];
    const bool reversed=variant=="reversed";TopoDS_Shape shape;
    if(family=="box")shape=BRepPrimAPI_MakeBox(gp_Ax2(gp_Pnt(2,reversed?8:3,reversed?12:5),reversed?-gp::DZ():gp::DZ(),gp::DX()),3,5,7).Shape();
    else if(family=="cylinder")shape=BRepPrimAPI_MakeCylinder(gp_Ax2(gp_Pnt(2,3,reversed?13:5),reversed?-gp::DZ():gp::DZ(),gp::DX()),5,8).Shape();
    else if(family=="coincident-instances"){
        // Different native TShapes at the same coordinates: never a shared edge.
        auto first=BRepPrimAPI_MakeBox(gp_Pnt(2,3,5),3,5,7).Shape();auto second=BRepBuilderAPI_Copy(first,true,false).Shape();
        TopoDS_Compound compound;BRep_Builder b;b.MakeCompound(compound);b.Add(compound,first);b.Add(compound,second);shape=compound;
    }else throw std::runtime_error("unknown family");
    if(variant=="placed"){gp_Trsf t;t.SetRotation(gp_Ax1(gp::Origin(),gp_Dir(1,2,3)),.713);t.SetTranslationPart(gp_Vec(17,-23,41));shape=BRepBuilderAPI_Transform(shape,t,true).Shape();}
    if(!BRepCheck_Analyzer(shape).IsValid())throw std::runtime_error("native fixture invalid");
    STEPControl_Writer writer;if(writer.Transfer(shape,STEPControl_AsIs)!=IFSelect_RetDone)throw std::runtime_error("STEP transfer failed");
    StepData_StepWriter precise(writer.Model());precise.FloatWriter().SetFormat("%.17E");precise.SendModel(Handle(StepData_Protocol)::DownCast(writer.WS()->Protocol()));
    const std::string name="stock-validation-"+family+(variant=="normal"?"":"-"+variant);
    std::ofstream out("/"+name+".step");if(!precise.Print(out))throw std::runtime_error("STEP write failed");out.close();
    EM_ASM({const n=UTF8ToString($0);require('fs').writeFileSync('/tmp/'+n+'.step',FS.readFile('/'+n+'.step'));},name.c_str());
    std::cout<<name<<" native-valid=true\n";return 0;
}catch(const std::exception& e){std::cerr<<e.what()<<"\n";return 1;}}
