#include "kernel-face.hpp"
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeWire.hxx>
#include <BRepLib.hxx>
#include <BRep_Builder.hxx>
#include <Geom2d_BSplineCurve.hxx>
#include <Geom2d_Line.hxx>
#include <Geom_BSplineCurve.hxx>
#include <Geom_CylindricalSurface.hxx>
#include <TColgp_Array1OfPnt2d.hxx>
#include <TColgp_Array1OfPnt.hxx>
#include <TColStd_Array1OfInteger.hxx>
#include <TColStd_Array1OfReal.hxx>
#include <iostream>
#include <stdexcept>
namespace RB=MalievKernel::RotationalBand;
static std::vector<RB::Wire> Gather(const TopoDS_Face& original) {
  auto face=TopoDS::Face(original.Oriented(TopAbs_FORWARD));
  std::vector<RB::Wire> result; TopTools_IndexedMapOfShape edges,vertices;
  for(TopoDS_Iterator it(face);it.More();it.Next()) {
    auto wire=TopoDS::Wire(it.Value()); RB::Wire w;
    w.id="wire-"+std::to_string(result.size());w.outer=wire.IsSame(BRepTools::OuterWire(face));w.complete=true;
    for(BRepTools_WireExplorer ex(wire,face);ex.More();ex.Next()) {
      RB::Use u;u.edge=ex.Current();u.id=w.id+"/use-"+std::to_string(w.uses.size());u.edgeId=std::to_string(edges.Add(u.edge));u.wireId=w.id;
      TopoDS_Vertex a,b;TopExp::Vertices(u.edge,a,b,Standard_True);u.startId=std::to_string(vertices.Add(a));u.endId=std::to_string(vertices.Add(b));
      u.curve=BRep_Tool::CurveOnSurface(u.edge,face,u.first,u.last);u.seam=BRep_Tool::IsClosed(u.edge,face);u.complete=true;w.uses.push_back(u);
    }
    result.push_back(w);
  }
  return result;
}
static Handle(Geom2d_BSplineCurve) Spline(gp_Pnt2d a,gp_Pnt2d b,double first=7,double last=11,double wa=3,double wb=3) {
  TColgp_Array1OfPnt2d poles(1,2);poles(1)=a;poles(2)=b;
  TColStd_Array1OfReal knots(1,2),weights(1,2);knots(1)=first;knots(2)=last;weights(1)=wa;weights(2)=wb;
  TColStd_Array1OfInteger mult(1,2);mult(1)=mult(2)=2;
  return new Geom2d_BSplineCurve(poles,weights,knots,mult,1,Standard_False);
}
// Genuine native construction, not captured metadata substitution: both 3D
// seam and its two PC branches have the same non-unit parameterization.
static TopoDS_Face FullFace(bool negative) {
  Handle(Geom_Surface) s=new Geom_CylindricalSurface(gp_Ax3(gp::XOY()),5);
  auto f=BRepBuilderAPI_MakeFace(s,0,2*std::acos(-1.0),0,8,1e-7).Face();
  auto wires=Gather(f);TopoDS_Edge edge;
  for(const auto& u:wires[0].uses)if(u.seam){edge=TopoDS::Edge(u.edge.Oriented(TopAbs_FORWARD));break;}
  double first,last;auto pc=BRep_Tool::CurveOnSurface(edge,f,first,last);
  const double u=pc->Value(first).X(),other=u==0?2*std::acos(-1.0):0;
  // Negative source derivative is independent of native traversal reversal.
  const double a=negative?12:-4,b=negative?-4:12;
  TColgp_Array1OfPnt poles(1,2);poles(1)=s->Value(u,a);poles(2)=s->Value(u,b);
  TColStd_Array1OfReal knots(1,2);knots(1)=7;knots(2)=11;
  TColStd_Array1OfInteger mult(1,2);mult(1)=mult(2)=2;
  BRep_Builder builder;
  builder.UpdateEdge(edge,new Geom_BSplineCurve(poles,knots,mult,1),1e-7);
  builder.UpdateEdge(edge,Spline({negative?other:u,a},{negative?other:u,b}),Spline({negative?u:other,a},{negative?u:other,b}),f,1e-7);
  builder.Range(edge,8,10);
  TopoDS_Vertex va,vb;TopExp::Vertices(edge,va,vb);
  if(negative){
    // Edge source direction changed, so reverse its vertex orientations and
    // every wire occurrence rather than changing the physical boundary.
    edge.Free(Standard_True);builder.Remove(edge,va);builder.Remove(edge,vb);
    builder.Add(edge,vb.Oriented(TopAbs_FORWARD));builder.Add(edge,va.Oriented(TopAbs_REVERSED));
    auto wire=BRepTools::OuterWire(f);wire.Free(Standard_True);
    std::vector<TopoDS_Edge> uses;for(TopoDS_Iterator it(wire);it.More();it.Next())uses.push_back(TopoDS::Edge(it.Value()));
    for(const auto& use:uses)if(use.IsSame(edge)){builder.Remove(wire,use);builder.Add(wire,use.Reversed());}
  }
  builder.UpdateVertex(va,negative?10:8,edge,1e-7);builder.UpdateVertex(vb,negative?8:10,edge,1e-7);
  builder.SameRange(edge,Standard_True);builder.SameParameter(edge,Standard_True);
  return f;
}
int main(){int checks=0;auto check=[&](bool ok,const char* why){++checks;if(!ok)throw std::runtime_error(why);};
 auto near=[&](double a,double b,const char* why){check(std::abs(a-b)<1e-10,why);};
 try {
  for(bool negative:{false,true}) {
   auto f=FullFace(negative);check(BRepCheck_Analyzer(f,Standard_True,Standard_False).IsValid(),"genuine affine seam face valid");
   for(bool reversed:{false,true})for(bool placed:{false,true}) {
    auto actual=reversed?TopoDS::Face(f.Reversed()):f;
    if(placed){gp_Trsf t;t.SetRotation(gp_Ax1(gp_Pnt(),gp_Dir(1,2,3)),.713);t.SetTranslationPart(gp_Vec(17,-23,41));actual=TopoDS::Face(actual.Moved(TopLoc_Location(t)));}
    auto uses=Gather(actual);auto band=RB::Evaluate(actual,uses,true,true);
    std::cout<<"FULL negative="<<negative<<" reversed="<<reversed<<" placed="<<placed<<" reason="<<band.reason<<'\n';
    check(band.available&&band.full,"non-unit spline seam full band");check(band.seams.size()==1,"native shared seam retained");near(band.axialMin,0,"strict subrange starts at physical zero");near(band.axialMax,8,"non-unit derivative physical height");
    int splines=0;for(const auto& use:uses[0].uses)if(use.seam){++splines;Geom2dAdaptor_Curve c(use.curve);check(c.GetType()==GeomAbs_BSplineCurve,"source representation not replaced");near(use.first,8,"native range not normalized");near(use.last,10,"native range not normalized");near(c.BSpline()->Knot(1),7,"full source domain retained");near(c.BSpline()->Knot(2),11,"full source domain retained");
      double a,b;TopLoc_Location loc;auto curve=BRep_Tool::Curve(use.edge,loc,a,b);
      for(double t:{8.0,8.5,9.0,9.5,10.0}){auto uv=use.curve->Value(t);auto p=BRepAdaptor_Surface(actual).Value(uv.X(),uv.Y());near(p.Distance(curve->Value(t).Transformed(loc.Transformation())),0,"actual SameParameter geometry agrees");}
    }check(splines==2,"both actual distinct seam PC branches");
   }
  }
  Handle(Geom_Surface) cylinder=new Geom_CylindricalSurface(gp_Ax3(gp::XOY()),5);
  // Independent real partial-U trim made directly from affine PC edges. The
  // 3D representation is constructed consistently by OCCT for this fixture.
  BRepBuilderAPI_MakeWire wire;std::vector<gp_Pnt2d> corners={{.2,0},{1.7,0},{1.7,8},{.2,8}};
  for(size_t i=0;i<4;++i){auto a=corners[i],b=corners[(i+1)%4];gp_Vec2d d(a,b);
    auto pc=Spline(a.Translated(-.5*d),b.Translated(.5*d));
    wire.Add(BRepBuilderAPI_MakeEdge(pc,cylinder,8,10).Edge());
  }
  auto w=wire.Wire();BRepLib::BuildCurves3d(w);BRepLib::SameParameter(w,1e-7);
  auto partial=BRepBuilderAPI_MakeFace(cylinder,w,Standard_True).Face();
  check(BRepCheck_Analyzer(partial,Standard_True,Standard_False).IsValid(),"actual affine partial face valid");
  auto partialUses=Gather(partial);auto partialBand=RB::Evaluate(partial,partialUses,true,true);
  check(partialBand.available&&!partialBand.full,"nonzero U partial affine band");near(partialBand.u0,.2,"partial lower U");near(partialBand.u1,1.7,"partial upper U");
  int pcs=0;for(const auto& use:partialUses[0].uses)if(Geom2dAdaptor_Curve(use.curve).GetType()==GeomAbs_BSplineCurve)++pcs;
  check(pcs==4,"native construction retains all four original affine PCs");
  auto f=FullFace(false);auto original=Gather(f);
  size_t index=0;while(!original[0].uses[index].seam)++index;
  auto rejection=[&](const Handle(Geom2d_Curve)& curve,const char* expected){auto uses=original;uses[0].uses[index].curve=curve;
    auto r=RB::Evaluate(f,uses,true,true);check(!r.available&&r.reason==expected,"captured unsupported PC reaches complete band rejection");};
  auto pc=Handle(Geom2d_BSplineCurve)::DownCast(original[0].uses[index].curve);
  auto a=pc->Pole(1),b=pc->Pole(2);
  rejection(Spline(a,b,7,11,1,2),"unsupported_pcurve_type");
  auto unequal=Spline({0,0},{2,0},0,1,1,2);near(unequal->Value(.5).X(),4.0/3,"straight unequal-weight curve has non-affine speed");
  rejection(Spline(a,a),"parameter_boundary_ambiguous");
  rejection(Spline(a,{b.X()+1e-18,b.Y()}),"oblique_or_helical_boundary");
  // Kink and degree elevation remain unsupported even with matching endpoints.
  TColgp_Array1OfPnt2d poles(1,3);poles(1)=a;poles(2)={a.X()+1,4};poles(3)=b;
  TColStd_Array1OfReal knots(1,3);knots(1)=7;knots(2)=9;knots(3)=11;
  TColStd_Array1OfInteger mult(1,3);mult(1)=2;mult(2)=1;mult(3)=2;
  rejection(new Geom2d_BSplineCurve(poles,knots,mult,1),"unsupported_pcurve_type");
  auto elevated=Handle(Geom2d_BSplineCurve)::DownCast(pc->Copy());elevated->IncreaseDegree(2);rejection(elevated,"unsupported_pcurve_type");
  TColStd_Array1OfInteger periodicMult(1,3);periodicMult.Init(1);
  TColgp_Array1OfPnt2d periodicPoles(1,2);periodicPoles(1)=a;periodicPoles(2)=b;
  rejection(new Geom2d_BSplineCurve(periodicPoles,knots,periodicMult,1,Standard_True),"unsupported_pcurve_type");
  for(auto range:std::vector<std::pair<double,double>>{{6,10},{8,12},{8,8},{8,std::numeric_limits<double>::infinity()}}){
    auto uses=original;uses[0].uses[index].first=range.first;uses[0].uses[index].last=range.second;
    check(!RB::Evaluate(f,uses,true,true).available,"actual use range cannot extend outside finite knot domain");
  }
  auto bad=original;bad[0].uses[index].complete=false;check(!RB::Evaluate(f,bad,true,true).available,"incomplete occurrence rejected");
  bad=original;bad[0].uses[index].seam=false;check(!RB::Evaluate(f,bad,true,true).available,"missing seam cannot be inferred geometrically");
  bad=original;bad[0].uses[index].edgeId="foreign";check(!RB::Evaluate(f,bad,true,true).available,"wrong seam identity rejected");
  check(!RB::Evaluate(f,original,false,true).available,"complete traversal gate independent");
  check(!RB::Evaluate(f,original,true,false).available,"millimeter gate independent");
  BRep_Builder builder;builder.SameParameter(original[0].uses[index].edge,Standard_False);
  check(RB::Evaluate(f,original,true,true).reason=="native_check_failed","SameParameter gate not bypassed by affine proof");
  builder.SameParameter(original[0].uses[index].edge,Standard_True);builder.SameRange(original[0].uses[index].edge,Standard_False);
  check(RB::Evaluate(f,original,true,true).reason=="native_check_failed","SameRange gate remains independent");
  std::cout<<"PASS "<<checks<<" native affine pcurve checks\n";return 0;
 }catch(const Standard_Failure& e){std::cerr<<"FAIL native after "<<checks<<": "<<e.GetMessageString()<<'\n';return 1;}
 catch(const std::exception& e){std::cerr<<"FAIL after "<<checks<<": "<<e.what()<<'\n';return 1;}
}
