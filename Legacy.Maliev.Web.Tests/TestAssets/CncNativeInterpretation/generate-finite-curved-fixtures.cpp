// Independent finite native patches. Sphere corners use R2 and three planes
// x/y/z=.5, not an endpoint polygon approximation. The rational roof is an
// explicit positive-weight degree(2,2) tensor with an internal V knot at .5.
#include <BRepPrimAPI_MakeSphere.hxx>
#include <BRepPrimAPI_MakeBox.hxx>
#include <BRepAlgoAPI_Common.hxx>
#include <BRepAlgoAPI_Cut.hxx>
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeWire.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_Sewing.hxx>
#include <BRepBuilderAPI_MakeSolid.hxx>
#include <BRepBuilderAPI_Transform.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <BRepLib.hxx>
#include <BRepGProp.hxx>
#include <GProp_GProps.hxx>
#include <Geom_BSplineSurface.hxx>
#include <TColgp_Array2OfPnt.hxx>
#include <TColStd_Array2OfReal.hxx>
#include <TColStd_Array1OfReal.hxx>
#include <TColStd_Array1OfInteger.hxx>
#include <TopExp_Explorer.hxx>
#include <TopExp.hxx>
#include <TopoDS.hxx>
#include <BRep_Tool.hxx>
#include <STEPControl_Writer.hxx>
#include <STEPControl_Reader.hxx>
#include <StepData_StepWriter.hxx>
#include <StepData_Protocol.hxx>
#include <XSControl_WorkSession.hxx>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <string>
#include <emscripten.h>
TopoDS_Shape roof(bool reversed,bool singular=false) {
 TColgp_Array2OfPnt poles(1,3,1,4);TColStd_Array2OfReal weights(1,3,1,4);
 for(int u=1;u<=3;++u)for(int v=1;v<=4;++v){
  poles(u,v)=gp_Pnt((u-1)*(singular?(v-1)/3.:1.),2.*(v-1)/3.,2.+(u==2&&(!singular||v>1)?.3:0)+(v==2||v==3?.2:0));
  weights(u,v)=(u==2?.8:1.)*(v==2||v==3?.9:1.);
 }
 TColStd_Array1OfReal uk(1,2),vk(1,3);uk(1)=0;uk(2)=1;vk(1)=0;vk(2)=.5;vk(3)=1;
 TColStd_Array1OfInteger um(1,2),vm(1,3);um(1)=um(2)=3;vm(1)=vm(3)=3;vm(2)=1;
 Handle(Geom_BSplineSurface) surface=new Geom_BSplineSurface(poles,weights,uk,vk,um,vm,2,2);
 if(reversed)surface->UReverse();
 TopoDS_Face top=BRepBuilderAPI_MakeFace(surface,0,1,0,1,1e-7).Face();BRepLib::BuildCurves3d(top);
 BRepBuilderAPI_Sewing sew(1e-7);sew.Add(top);
 for(TopExp_Explorer ex(top,TopAbs_EDGE);ex.More();ex.Next()){
  TopoDS_Edge e=TopoDS::Edge(ex.Current());if(BRep_Tool::Degenerated(e))continue;TopoDS_Vertex a,b;TopExp::Vertices(e,a,b,true);
  gp_Pnt p=BRep_Tool::Pnt(a),q=BRep_Tool::Pnt(b),pb(p.X(),p.Y(),0),qb(q.X(),q.Y(),0);
  BRepBuilderAPI_MakeWire w;w.Add(e);w.Add(BRepBuilderAPI_MakeEdge(q,qb));w.Add(BRepBuilderAPI_MakeEdge(qb,pb));w.Add(BRepBuilderAPI_MakeEdge(pb,p));
  sew.Add(BRepBuilderAPI_MakeFace(w.Wire(),true).Face());
 }
 BRepBuilderAPI_MakeWire bottom;gp_Pnt p[4]={gp_Pnt(0,0,0),gp_Pnt(2,0,0),gp_Pnt(2,2,0),gp_Pnt(0,2,0)};
 if(singular){p[1]=p[2];p[2]=p[3];}int count=singular?3:4;
 for(int i=0;i<count;++i)bottom.Add(BRepBuilderAPI_MakeEdge(p[i],p[(i+1)%count]));
 sew.Add(BRepBuilderAPI_MakeFace(bottom.Wire()).Face());sew.Perform();
 TopExp_Explorer shells(sew.SewedShape(),TopAbs_SHELL);if(!shells.More())throw std::runtime_error("roof shell missing");
 TopoDS_Solid solid=BRepBuilderAPI_MakeSolid(TopoDS::Shell(shells.Current())).Solid();BRepLib::OrientClosedSolid(solid);return solid;
}
int main(int argc,char** argv){
 try {
 const std::string family=argc>1?argv[1]:"sphere-outward",variant=argc>2?argv[2]:"normal";const bool reverse=variant=="reversed";
 TopoDS_Shape shape;
 if(family=="rational-roof")shape=roof(reverse);
 else if(family=="singular-roof")shape=roof(reverse,true);
 else if(family=="thread-small-sphere"||family=="thread-quarter-sphere"){
  if(argc<4)throw std::runtime_error("public source required");
  EM_ASM({FS.writeFile('/source.step',require('fs').readFileSync(UTF8ToString($0)));},argv[3]);
  STEPControl_Reader reader;if(reader.ReadFile("/source.step")!=IFSelect_RetDone)throw std::runtime_error("source read failed");reader.TransferRoots();
  // Off-axis R.5 cap dimple. The sphere poles and its seam are outside the
  // retained cap patch; no thread flank or cylindrical thread band intersects it.
  const bool quarter=family=="thread-quarter-sphere";
  TopoDS_Shape cutter=BRepPrimAPI_MakeSphere(gp_Ax2(quarter?gp_Pnt(0,3,4.25):gp_Pnt(3,0,4.25),reverse?-gp::DX():gp::DX(),gp::DY()),.5).Shape();
  if(quarter)cutter=BRepAlgoAPI_Common(cutter,BRepPrimAPI_MakeBox(gp_Pnt(0,3,3),2,2,2).Shape()).Shape();
  shape=BRepAlgoAPI_Cut(reader.OneShape(),cutter).Shape();
 }
 else {
  auto sphere=BRepPrimAPI_MakeSphere(gp_Ax2(gp::Origin(),reverse?-gp::DZ():gp::DZ(),gp::DX()),2).Shape();
  auto box=BRepPrimAPI_MakeBox(gp_Pnt(.5,.5,.5),3,3,3).Shape();
  if(family=="sphere-outward")shape=BRepAlgoAPI_Common(box,sphere).Shape();
  else if(family=="sphere-inward")shape=BRepAlgoAPI_Cut(box,sphere).Shape();
  else throw std::runtime_error("unknown family");
 }
 if(variant=="placed"){gp_Trsf tr;tr.SetRotation(gp_Ax1(gp::Origin(),gp_Dir(1,2,3)),.713);tr.SetTranslationPart(gp_Vec(17,-23,41));shape=BRepBuilderAPI_Transform(shape,tr,true).Shape();}
 if(!BRepCheck_Analyzer(shape).IsValid())throw std::runtime_error("invalid fixture");
 GProp_GProps props;BRepGProp::VolumeProperties(shape,props);if(!(props.Mass()>0))throw std::runtime_error("nonpositive volume");
 STEPControl_Writer writer;if(writer.Transfer(shape,STEPControl_AsIs)!=IFSelect_RetDone)throw std::runtime_error("transfer failed");
 StepData_StepWriter precise(writer.Model());precise.FloatWriter().SetFormat("%.17E");precise.SendModel(Handle(StepData_Protocol)::DownCast(writer.WS()->Protocol()));
 std::string name="finite-curved-"+family+(variant=="normal"?"":"-"+variant);std::ofstream out("/"+name+".step");if(!precise.Print(out))throw std::runtime_error("write failed");out.close();
 EM_ASM({const name=UTF8ToString($0);require('fs').writeFileSync('/tmp/'+name+'.step',FS.readFile('/'+name+'.step'));},name.c_str());
 std::cout<<name<<" valid=true volume="<<props.Mass()<<std::endl;return 0;
 }catch(const std::exception& e){std::cerr<<e.what()<<std::endl;return 1;}
}
