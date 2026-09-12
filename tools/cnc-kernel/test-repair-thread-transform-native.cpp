#include "kernel-repair-residual.hpp"
#include <Geom_CylindricalSurface.hxx>
#include <TColStd_Array1OfInteger.hxx>
#include <TColStd_Array1OfReal.hxx>
#include <TColgp_Array1OfPnt.hxx>
#include <TColgp_Array1OfPnt2d.hxx>
#include <TColgp_Array2OfPnt.hxx>
#include <iostream>
#include <stdexcept>
using namespace MalievRepair;

// Independent degree-n Bernstein representation of (t, .01*(t-.5),
// sign*t + displacement*t^n), on every specified native knot span.
// The nonzero highest-order coefficient must never be truncated to cubic.
Handle(Geom_BSplineCurve) Source(int degree, bool cylinder, int sign,
                                 double displacement, bool multiple) {
  const int spans = multiple ? 3 : 1;
  TColStd_Array1OfReal knots(1, spans + 1);
  TColStd_Array1OfInteger mult(1, spans + 1);
  knots(1)=0;knots(spans+1)=1;
  if(multiple){knots(2)=.5001229;knots(3)=.5001231;}
  mult.Init(degree);mult(1)=mult(spans+1)=degree+1;
  TColgp_Array1OfPnt poles(1,spans*degree+1);
  for(int span=1;span<=spans;++span)for(int i=0;i<=degree;++i){
    const double a=knots(span),b=knots(span+1),t=a+(b-a)*i/degree;
    const double high=displacement*std::pow(a,degree-i)*std::pow(b,i);
    poles((span-1)*degree+i+1)=gp_Pnt(cylinder?1:t,cylinder?.01*sign*(t-.5):.25,sign*t+high);
  }
  return new Geom_BSplineCurve(poles,knots,mult,degree);
}
Handle(Geom2d_BSplineCurve) PC(bool cylinder,int sign,bool multiple){
  const int spans=multiple?2:1;TColgp_Array1OfPnt2d poles(1,spans+1);
  TColStd_Array1OfReal knots(1,spans+1);TColStd_Array1OfInteger mult(1,spans+1);
  mult.Init(1);mult(1)=mult(spans+1)=2;
  for(int i=0;i<=spans;++i){double t=double(i)/spans;knots(i+1)=t;
    poles(i+1)=cylinder?gp_Pnt2d(.01*sign*(t-.5),sign*t):gp_Pnt2d(t,.25);}
  return new Geom2d_BSplineCurve(poles,knots,mult,1);
}
Handle(Geom_Surface) Support(bool cylinder,int sign){
  if(cylinder)return new Geom_CylindricalSurface(gp_Ax3(),1);
  TColgp_Array2OfPnt poles(1,4,1,4);
  for(int i=0;i<4;++i)for(int j=0;j<4;++j)poles(i+1,j+1)=gp_Pnt(i/3.,j/3.,sign*i/3.);
  TColStd_Array1OfReal knots(1,2);knots(1)=0;knots(2)=1;
  TColStd_Array1OfInteger mult(1,2);mult.Init(4);
  return new Geom_BSplineSurface(poles,knots,knots,mult,mult,3,3);
}
int main(){int checks=0;auto check=[&](bool value,const char* name){++checks;if(!value)throw std::runtime_error(name);};
 try{
  gp_Trsf transform;transform.SetRotation(gp_Ax1(gp::Origin(),gp_Dir(1,2,3)),.713);transform.SetTranslationPart(gp_Vec(17,-23,41));
  for(bool cylinder:{false,true})for(int degree:{11,14})for(int sign:{-1,1})for(bool multiple:{false,true})for(int placement:{0,1,2}){
    auto source=Source(degree,cylinder,sign,.002,multiple);auto pc=PC(cylinder,sign,multiple);auto surface=Support(cylinder,sign);
    TopLoc_Location location;if(placement==1)location=TopLoc_Location(transform);
    if(placement==2){source->Transform(transform);surface->Transform(transform);}
    const RepresentedCurveBound c(source,location),p(pc);const RepresentedSurfaceBound s(surface,location);
    CorrelatedDiagnostics correlated;ResidualDispatchDiagnostics dispatch;std::string method;
    const auto result=BoundRepairMetric(c,p,s,0,1,.01,correlated,dispatch,&method);
    std::cout<<"class="<<(cylinder?"cylinder":"bicubic")<<" degree="<<degree<<" sign="<<sign<<" spans="<<multiple<<" placement="<<placement<<" "<<result.status<<" "<<result.reason<<" upper="<<result.upper<<" work="<<result.intervals<<std::endl;
    check(result.status=="bounded-within-budget" && result.upper>=.002 && result.upper<.01 && dispatch.correlatedFallbacks==0,
      "degree11/14 affine-PC residual must use complete shared-variable coefficients");
    check(method==(cylinder?"shared-cylinder-trig":"shared-polynomial"),"dispatcher and evaluator supported classes agree");
    const auto direct=cylinder?BoundCylindricalComposition(c,p,s,Down(0.),Up(1.),.01):BoundPolynomialComposition(c,p,s,Down(0.),Up(1.),.01);
    check(direct.status=="bounded-within-budget","finite exterior endpoint slivers remain assessed");
    auto bad=Source(degree,cylinder,sign,.08,multiple);if(placement==2)bad->Transform(transform);
    const auto rejected=cylinder?BoundCylindricalComposition(CurveBound(bad,location),p,s,0,1,.01):BoundPolynomialComposition(CurveBound(bad,location),p,s,0,1,.01);
    check(rejected.status=="exceeds-budget","highest-degree over-budget residual must not be truncated");
  }
  for(bool cylinder:{false,true}){
    auto source=Source(14,cylinder,1,0,false);auto pc=PC(cylinder,1,false);auto surface=Support(cylinder,1);
    auto run=[&](const CurveBound& c,const CurveBound& p){return cylinder?BoundCylindricalComposition(c,p,SurfaceBound(surface,{}),0,1,.01):BoundPolynomialComposition(c,p,SurfaceBound(surface,{}),0,1,.01);};
    source->SetWeight(2,.5);auto rational=run(CurveBound(source,{}),CurveBound(pc));
    check(rational.status=="unavailable" && rational.intervals==0,"rational source is outside new finite class");
    source=Source(14,cylinder,1,0,false);pc->SetWeight(2,.5);rational=run(CurveBound(source,{}),CurveBound(pc));
    check(rational.status=="unavailable" && rational.intervals==0,"rational affine PC cannot become polynomial");
    pc=PC(cylinder,1,false);auto capacity=run(CurveBound(Source(19,cylinder,1,0,false),{}),CurveBound(pc));
    check(capacity.status=="unavailable" && capacity.intervals==0,"degree above18 rejects before coefficient construction");
  }
  std::cout<<"PASS: "<<checks<<" transformed thread residual checks\n";return 0;
 }catch(const Standard_Failure& e){std::cerr<<e.GetMessageString()<<'\n';}catch(const std::exception& e){std::cerr<<e.what()<<'\n';}return 1;
}
