// Compound recipe reuses the accepted public ruled-thread construction below.
// Lineage: generate-thread-fixtures.cpp SHA256 25980783122324142b21740f8748030cb5561b591c64d6ed687eacb552cfdbb3.
// Independent public analytical external threads: sew exact cylinder bands and
// finite cubic ruled flanks, including analytic annular end sectors. No boolean,
// solid sweep or runtime repair/consumer relaxation is used.
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeWire.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_Sewing.hxx>
#include <BRepBuilderAPI_MakeSolid.hxx>
#include <BRepBuilderAPI_Transform.hxx>
#include <BRepLib.hxx>
#include <BRep_Builder.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <BRepGProp.hxx>
#include <GProp_GProps.hxx>
#include <Geom_CylindricalSurface.hxx>
#include <Geom_BSplineSurface.hxx>
#include <Geom2d_BSplineCurve.hxx>
#include <TColgp_Array1OfPnt2d.hxx>
#include <TColgp_Array2OfPnt.hxx>
#include <TColStd_Array1OfReal.hxx>
#include <TColStd_Array1OfInteger.hxx>
#include <TopExp_Explorer.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Solid.hxx>
#include <TopTools_ListOfShape.hxx>
#include <STEPControl_Writer.hxx>
#include <StepData_StepWriter.hxx>
#include <StepData_Protocol.hxx>
#include <XSControl_WorkSession.hxx>
#include <Standard_Failure.hxx>
#include <gp_Pln.hxx>
#include <gp_Circ.hxx>
#include <fstream>
#include <iostream>
#include <cmath>
#include <stdexcept>
#include <vector>
#include <string>
#include <emscripten.h>
struct UV { double u,v; };
const double pi=std::acos(-1.0), pitch=1, lowR=6.3705, highR=6.942, halfCrest=.0625;
double clipLow=0,clipHigh=4,hand=1;
bool reverseAxis=false,reverseEdges=false,reverseU=false;
bool connectorMode=false;
std::vector<size_t> extendedBands;
std::vector<TopoDS_Face> sides; TopTools_ListOfShape caps[2];
struct CapPart {TopoDS_Edge edge;double radius;};std::vector<CapPart> capParts[2];
std::vector<UV> clip(std::vector<UV> p,double a,double b,double c) {
  std::vector<UV> out;
  for(size_t i=0;i<p.size();i++) { UV s=p[i],e=p[(i+1)%p.size()]; double ds=a*s.u+b*s.v+c,de=a*e.u+b*e.v+c;
    if(ds>=0) out.push_back(s);
    if((ds<0)!=(de<0)) { double t=ds/(ds-de); out.push_back({s.u+t*(e.u-s.u),s.v+t*(e.v-s.v)}); }
  } return out;
}
void face(const Handle(Geom_Surface)& surface,std::vector<UV> polygon,double zU,double zV,double z0) {
  polygon=clip(polygon,zU,zV,z0-clipLow); polygon=clip(polygon,-zU,-zV,clipHigh-z0);
  if(polygon.size()<3) return;
  BRepBuilderAPI_MakeWire wire;
  for(size_t i=0;i<polygon.size();i++) {
    UV a=polygon[i],b=polygon[(i+1)%polygon.size()];
    if(std::hypot(a.u-b.u,a.v-b.v)<1e-12) continue;
    TColgp_Array1OfPnt2d poles(1,2); poles(1)=gp_Pnt2d(reverseEdges?b.u:a.u,reverseEdges?b.v:a.v); poles(2)=gp_Pnt2d(reverseEdges?a.u:b.u,reverseEdges?a.v:b.v);
    TColStd_Array1OfReal knots(1,2); knots(1)=0; knots(2)=1;
    TColStd_Array1OfInteger mult(1,2); mult(1)=2; mult(2)=2;
    Handle(Geom2d_BSplineCurve) curve=new Geom2d_BSplineCurve(poles,knots,mult,1);
    auto edge=BRepBuilderAPI_MakeEdge(curve,surface,0,1).Edge(); BRepLib::BuildCurves3d(edge,1e-7);
    if(reverseEdges) edge.Reverse();
    // Cubic circle approximation is an explicit fixture construction tolerance,
    // not a runtime repair or a consumer acceptance relaxation.
    BRep_Builder builder; for(TopExp_Explorer v(edge,TopAbs_VERTEX);v.More();v.Next()) builder.UpdateVertex(TopoDS::Vertex(v.Current()),1e-5);
    wire.Add(edge);
    double za=zU*a.u+zV*a.v+z0,zb=zU*b.u+zV*b.v+z0;
    for(int k=0;k<2;k++) {double level=k?clipHigh:clipLow;if(std::abs(za-level)<1e-9 && std::abs(zb-level)<1e-9){
      caps[k].Append(edge);auto cyl=Handle(Geom_CylindricalSurface)::DownCast(surface);capParts[k].push_back({edge,cyl.IsNull()?-1:cyl->Radius()});
    }}
  }
  if(!wire.IsDone()) throw std::runtime_error("side wire disconnected");
  sides.push_back(BRepBuilderAPI_MakeFace(surface,wire.Wire(),true).Face());
}
void band(double radius,double low,double high) {
  Handle(Geom_Surface) cylinder=new Geom_CylindricalSurface(gp_Ax3(gp::Origin(),reverseAxis?-gp::DZ():gp::DZ(),gp::DX()),radius);
  std::vector<UV> polygon={{0,low},{2*pi,low+hand*pitch},{2*pi,high+hand*pitch},{0,high}};
  if(reverseAxis)for(auto& p:polygon){p.u=-p.u;p.v=-p.v;}
  face(cylinder,polygon,0,reverseAxis?-1:1,0);
  if(connectorMode && ((clipLow==0 && radius==lowR && low>1 && low<2) ||
      (clipLow==3 && radius==highR && low>1 && low<3))) extendedBands.push_back(sides.size()-1);
}
void flank(double lowZ,double highZ,double advance) {
  const int spans=64; TColgp_Array2OfPnt poles(1,4,1,3*spans+1);
  for(int j=0;j<spans;j++) {
    double a=2*pi*j/spans,b=2*pi*(j+1)/spans,h=b-a;
    for(int k=0;k<4;k++) { double angle=k<2?a:b, d=k==1?h/3:k==2?-h/3:0;
      for(int i=0;i<4;i++) { double s=i/3.0,r=lowR+s*(highR-lowR),z=lowZ+s*(highZ-lowZ);
        poles(reverseU?4-i:i+1,3*j+k+1)=gp_Pnt(r*(std::cos(angle)-d*std::sin(angle)),r*(std::sin(angle)+d*std::cos(angle)),z+advance*(angle+d)/(2*pi));
      }
    }
  }
  TColStd_Array1OfReal uk(1,2),vk(1,spans+1); TColStd_Array1OfInteger um(1,2),vm(1,spans+1);
  uk(1)=0;uk(2)=1;um(1)=um(2)=4;
  for(int j=0;j<=spans;j++){vk(j+1)=2*pi*j/spans;vm(j+1)=j==0||j==spans?4:3;}
  Handle(Geom_Surface) surface=new Geom_BSplineSurface(poles,uk,vk,um,vm,3,3);
  std::vector<UV> polygon={{0,0},{1,0},{1,2*pi},{0,2*pi}};
  if(advance==0){face(surface,clip(polygon,0,-1,pi),highZ-lowZ,0,lowZ);face(surface,clip(polygon,0,1,-pi),highZ-lowZ,0,lowZ);}
  else face(surface,polygon,reverseU?lowZ-highZ:highZ-lowZ,advance/(2*pi),reverseU?highZ:lowZ);
}
void thread(double start,double length) {
  clipLow=start;clipHigh=start+length;caps[0].Clear();caps[1].Clear();capParts[0].clear();capParts[1].clear();
  double rise=(highR-lowR)/std::sqrt(3.0);
  for(int k=-1;k<=int(std::ceil(length/pitch))+1;k++) {
    double base=start+k*pitch;
    band(highR,base-halfCrest,base+halfCrest);
    band(lowR,base+halfCrest+rise,base+pitch-halfCrest-rise);
    flank(base-halfCrest-rise,base-halfCrest,hand*pitch);
    flank(base+halfCrest+rise,base+halfCrest,hand*pitch);
  }
}
TopoDS_Face cap(int k) {BRepBuilderAPI_MakeWire wire;wire.Add(caps[k]);
  if(!wire.IsDone())throw std::runtime_error("plane cap wire disconnected");
  return BRepBuilderAPI_MakeFace(gp_Pln(gp_Pnt(0,0,k?clipHigh:clipLow),gp::DZ()),wire.Wire()).Face();
}
TopoDS_Face annularCap(int k,double radius){
  TopTools_ListOfShape edges;for(const auto& p:capParts[k])if(p.radius!=radius)edges.Append(p.edge);
  double rise=(highR-lowR)/std::sqrt(3.0),a=radius==lowR?2*pi*(1-halfCrest-rise):2*pi*halfCrest;
  double b=radius==lowR?2*pi*(1+halfCrest+rise):2*pi*(1-halfCrest),z=k?clipHigh:clipLow;
  auto edge=BRepBuilderAPI_MakeEdge(gp_Circ(gp_Ax2(gp_Pnt(0,0,z),gp::DZ()),radius),a,b).Edge();
  BRep_Builder builder;for(TopExp_Explorer v(edge,TopAbs_VERTEX);v.More();v.Next())builder.UpdateVertex(TopoDS::Vertex(v.Current()),1e-5);
  edges.Append(edge);BRepBuilderAPI_MakeWire wire;wire.Add(edges);
  if(!wire.IsDone())throw std::runtime_error("analytic annular cap disconnected");
  return BRepBuilderAPI_MakeFace(gp_Pln(gp_Pnt(0,0,z),gp::DZ()),wire.Wire()).Face();
}
void cylinder(double radius,double a,double b){
  Handle(Geom_Surface) s=new Geom_CylindricalSurface(gp_Ax3(gp::Origin(),gp::DZ()),radius);face(s,{{0,a},{2*pi,a},{2*pi,b},{0,b}},0,1,0);
}

#include <BRepPrimAPI_MakeCylinder.hxx>
#include <BRepPrimAPI_MakeCone.hxx>
#include <BRepPrimAPI_MakeTorus.hxx>
#include <BRepAlgoAPI_Cut.hxx>
#include <BRepAlgoAPI_Fuse.hxx>

// Finite added bore touches caps/core only: 3 + .4 < lowR.
TopoDS_Shape threadWithSmallBore() {
  thread(0,4);
  for(int k=0;k<2;k++) {
    // Reverse the source circle together with the bore chart, preserving the
    // physical clockwise inner wire in the cap's +Z plane.
    auto edge=BRepBuilderAPI_MakeEdge(gp_Circ(gp_Ax2(gp_Pnt(3,0,k?4:0),reverseAxis?-gp::DZ():gp::DZ(),gp::DX()),.4)).Edge();
    auto inner=BRepBuilderAPI_MakeWire(edge).Wire();if(!reverseAxis)inner.Reverse();
    BRepBuilderAPI_MakeFace end(cap(k));end.Add(inner);sides.push_back(end.Face());
  }
  Handle(Geom_Surface) bore=new Geom_CylindricalSurface(gp_Ax3(gp_Pnt(3,0,reverseAxis?4:0),reverseAxis?-gp::DZ():gp::DZ(),gp::DX()),.4);
  sides.push_back(BRepBuilderAPI_MakeFace(bore,0,2*pi,0,4,1e-7).Face());
  BRepBuilderAPI_Sewing sew(1e-5);for(const auto& f:sides)sew.Add(f);
  std::cerr<<"stage thread sewing"<<std::endl;sew.Perform();
  TopExp_Explorer shells(sew.SewedShape(),TopAbs_SHELL);
  if(!shells.More())throw std::runtime_error("no compound thread shell");
  auto solid=BRepBuilderAPI_MakeSolid(TopoDS::Shell(shells.Current())).Solid();shells.Next();
  if(shells.More()||!BRepLib::OrientClosedSolid(solid)||!BRepCheck_Analyzer(solid).IsValid())
    throw std::runtime_error("invalid compound thread solid");
  return solid;
}
TopoDS_Shape primitiveCylinder(double r,double z,double h,bool reversed) {
  return BRepPrimAPI_MakeCylinder(gp_Ax2(gp_Pnt(0,0,reversed?z+h:z),reversed?-gp::DZ():gp::DZ()),r,h).Shape();
}
TopoDS_Shape primitiveCone(double a,double b,double z,double h,bool reversed) {
  return BRepPrimAPI_MakeCone(gp_Ax2(gp_Pnt(0,0,reversed?z+h:z),reversed?-gp::DZ():gp::DZ()),reversed?b:a,reversed?a:b,h).Shape();
}
TopoDS_Shape grooveWithBead(bool reversed) {
  auto valley=BRepPrimAPI_MakeTorus(gp_Ax2(gp_Pnt(0,0,5),reversed?-gp::DZ():gp::DZ()),12,3).Shape();
  auto bead=BRepPrimAPI_MakeTorus(gp_Ax2(gp_Pnt(0,0,12),reversed?-gp::DZ():gp::DZ()),12,2).Shape();
  auto grooved=BRepAlgoAPI_Cut(primitiveCylinder(12,0,16,reversed),valley).Shape();
  return BRepAlgoAPI_Fuse(grooved,bead).Shape();
}
TopoDS_Shape opposedBores(bool reversed) {
  auto upper=BRepAlgoAPI_Cut(primitiveCylinder(10,0,10,reversed),primitiveCylinder(3,4,6,reversed)).Shape();
  auto tip=BRepAlgoAPI_Cut(upper,primitiveCone(0,3,2,2,reversed)).Shape();
  return BRepAlgoAPI_Cut(tip,primitiveCylinder(3,0,1,reversed)).Shape();
}
int main(int argc,char** argv) {try{
  if(argc!=3)throw std::runtime_error("expected family and normal/placed/reversed");
  std::string family=argv[1],variant=argv[2];
  if(variant!="normal"&&variant!="placed"&&variant!="reversed")throw std::runtime_error("unknown variant");
  reverseAxis=reverseEdges=reverseU=variant=="reversed";
  TopoDS_Shape shape;
  if(family=="thread-small-bore")shape=threadWithSmallBore();
  else if(family=="groove-bead")shape=grooveWithBead(reverseAxis);
  else if(family=="opposed-bores")shape=opposedBores(reverseAxis);
  else throw std::runtime_error("unknown family");
  if(variant=="placed"){gp_Trsf tr;tr.SetRotation(gp_Ax1(gp::Origin(),gp_Dir(1,2,3)),.713);tr.SetTranslationPart(gp_Vec(17,-23,41));shape=BRepBuilderAPI_Transform(shape,tr,true).Shape();}
  int solids=0,shells=0,faces=0;for(TopExp_Explorer i(shape,TopAbs_SOLID);i.More();i.Next())++solids;
  for(TopExp_Explorer i(shape,TopAbs_SHELL);i.More();i.Next())++shells;
  for(TopExp_Explorer i(shape,TopAbs_FACE);i.More();i.Next())++faces;
  if(solids!=1||shells!=1||!BRepCheck_Analyzer(shape).IsValid())throw std::runtime_error("fixture must be one valid connected solid/shell");
  GProp_GProps mass;BRepGProp::VolumeProperties(shape,mass);
  if(!std::isfinite(mass.Mass())||mass.Mass()<=0)throw std::runtime_error("nonpositive volume");
  const std::string name="analytic-compound-"+family+(variant=="normal"?"":"-"+variant);
  std::cout<<"COMPOUND "<<name<<" solids="<<solids<<" shells="<<shells<<" faces="<<faces<<" volume="<<mass.Mass()<<std::endl;
  STEPControl_Writer writer;if(writer.Transfer(shape,STEPControl_AsIs)!=IFSelect_RetDone)throw std::runtime_error("STEP transfer failed");
  StepData_StepWriter precise(writer.Model());precise.FloatWriter().SetFormat("%.17E");
  precise.SendModel(Handle(StepData_Protocol)::DownCast(writer.WS()->Protocol()));
  const std::string file="/"+name+".step";std::ofstream out(file);
  if(!precise.Print(out))throw std::runtime_error("STEP write failed");out.close();
  EM_ASM({const n=UTF8ToString($0);require('fs').writeFileSync('/tmp/'+n+'.step',FS.readFile('/'+n+'.step'));},name.c_str());
  return 0;
}catch(const Standard_Failure& e){std::cerr<<e.GetMessageString()<<std::endl;return 1;}
 catch(const std::exception& e){std::cerr<<e.what()<<std::endl;return 1;}}
