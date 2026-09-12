#include "kernel-repair-bounds.hpp"
#include <Geom_Line.hxx>
#include <Geom2d_Line.hxx>
#include <Geom_Plane.hxx>
#include <TColgp_Array1OfPnt.hxx>
#include <TColgp_Array2OfPnt.hxx>
#include <TColStd_Array1OfReal.hxx>
#include <TColStd_Array1OfInteger.hxx>
#include <TColStd_Array2OfReal.hxx>
#include <iostream>
#include <stdexcept>
using namespace MalievRepair;
static int checks=0;
static void Check(bool success,const char* message){++checks;if(!success)throw std::runtime_error(message);}
int main(){try{
    Handle(Geom_Curve)line=new Geom_Line(gp_Pnt(0,0,0),gp_Dir(1,0,0));
    Handle(Geom2d_Curve)pc=new Geom2d_Line(gp_Pnt2d(0,0),gp_Dir2d(1,0));
    Handle(Geom_Surface)plane=new Geom_Plane(gp_Pln(gp_Pnt(0,0,0),gp_Dir(0,0,1)));
    CurveBound c(line,TopLoc_Location()),p(pc);SurfaceBound s(plane,TopLoc_Location());
    auto positive=BoundResidual(c,p,s,0,1,.01);Check(positive.status=="bounded-within-budget","line-plane whole interval positive");Check(positive.intervals>1,"positive is subdivided");
    gp_Trsf move;move.SetTranslation(gp_Vec(0,0,1));CurveBound moved(line,TopLoc_Location(move));
    Check(BoundResidual(moved,p,s,0,1,.01).status=="exceeds-budget","placement change negative");
    SurfaceBound movedSurface(plane,TopLoc_Location(move));
    Check(BoundResidual(moved,p,movedSurface,0,1,.01).status=="bounded-within-budget","matching curve and support placements applied once");
    Check(BoundResidual(c,p,movedSurface,0,1,.01).status=="exceeds-budget","support-only placement negative");
    const auto tiny=c.Bounds(1,std::nextafter(1,2));Check(DistanceUpper(tiny,tiny)>0&&DistanceUpper(tiny,tiny)<1e-12,"tiny range sliver is enclosed");
    TColgp_Array1OfPnt poles(1,5);poles(1)=gp_Pnt(0,0,0);poles(2)=gp_Pnt(.5012,0,0);poles(3)=gp_Pnt(.5013,0,.1);poles(4)=gp_Pnt(.5014,0,0);poles(5)=gp_Pnt(1,0,0);
    TColStd_Array1OfReal knots(1,5);knots(1)=0;knots(2)=.5012;knots(3)=.5013;knots(4)=.5014;knots(5)=1;
    TColStd_Array1OfInteger multiplicities(1,5);multiplicities.Init(1);multiplicities(1)=multiplicities(5)=2;
    Handle(Geom_Curve)spike=new Geom_BSplineCurve(poles,knots,multiplicities,1);
    for(double t:{0.,.25,.5,.75,1.})Check(spike->Value(t).Z()==0,"spike evades sparse nodes");
    auto negative=BoundResidual(CurveBound(spike,TopLoc_Location()),p,s,0,1,.01);
    if(negative.status!="exceeds-budget")std::cerr<<"spike status="<<negative.status<<" reason="<<negative.reason<<" intervals="<<negative.intervals<<" lower="<<negative.lower<<" upper="<<negative.upper<<'\n';
    Check(negative.status=="exceeds-budget"&&negative.lower>.01,"narrow spike rejected by whole interval bound");
    Check(BoundResidual(c,p,s,0,1,0).status=="unavailable","missing budget negative");
    Check(BoundResidual(c,p,s,1,0,.01).status=="unavailable","malformed interval negative");
    const double infinity=std::numeric_limits<double>::infinity(),nan=std::numeric_limits<double>::quiet_NaN();
    for(double invalid:{infinity,nan,0.,-1.})Check(BoundResidual(c,p,s,0,1,invalid).status=="unavailable","invalid curve budget negative");
    Check(BoundResidual(c,p,s,nan,1,.01).status=="unavailable","nonfinite domain negative");
    Check(BoundResidual(CurveBound(spike,TopLoc_Location()),p,s,-.1,1,.01).status=="unavailable","domain outside native spline basis negative");
    gp_Trsf invalidMove;invalidMove.SetTranslation(gp_Vec(nan,0,0));
    Check(BoundResidual(CurveBound(line,TopLoc_Location(invalidMove)),p,s,0,1,.01).status=="unavailable","nonfinite placement geometry negative");
    bool rejected=false;try{Weighted(1,2,3,-1);}catch(const Standard_Failure&){rejected=true;}Check(rejected,"negative rational weight rejected");
    TColgp_Array2OfPnt net(1,2,1,2);net(1,1)=gp_Pnt(0,0,0);net(2,1)=gp_Pnt(1,0,0);net(1,2)=gp_Pnt(0,1,0);net(2,2)=gp_Pnt(1,1,0);
    TColStd_Array1OfReal k(1,2);k(1)=0;k(2)=1;TColStd_Array1OfInteger m(1,2);m.Init(2);
    Handle(Geom_Surface)a=new Geom_BSplineSurface(net,k,k,m,m,1,1);
    auto identical=BoundSurfaceDifference(a,a,.01);Check(identical.status=="bounded-within-budget"&&identical.upper<1e-10,"common surface polynomial positive");
    net(2,2)=gp_Pnt(1,1,.1);Handle(Geom_Surface)b=new Geom_BSplineSurface(net,k,k,m,m,1,1);
    Check(BoundSurfaceDifference(a,b,.01).status!="bounded-within-budget","changed surface polynomial negative");
    for(double invalid:{infinity,nan,0.,-1.})Check(BoundSurfaceDifference(a,b,invalid).status=="unavailable","invalid surface budget negative");
    TColStd_Array2OfReal weights(1,2,1,2);weights(1,1)=1;weights(1,2)=.5;weights(2,1)=.7;weights(2,2)=1.2;
    net(2,2)=gp_Pnt(1,1,0);Handle(Geom_Surface)rational=new Geom_BSplineSurface(net,weights,k,k,m,m,1,1);
    Check(BoundSurfaceDifference(rational,rational,.01).status=="bounded-within-budget","genuinely rational identical surface positive");
    weights(2,2)=1.20000001;Handle(Geom_Surface)closeWeights=new Geom_BSplineSurface(net,weights,k,k,m,m,1,1);
    Check(BoundSurfaceDifference(rational,closeWeights,.01).status=="bounded-within-budget","unequal positive weights small bounded change");
    weights(2,2)=10;Handle(Geom_Surface)differentWeights=new Geom_BSplineSurface(net,weights,k,k,m,m,1,1);
    Check(BoundSurfaceDifference(rational,differentWeights,.01).status!="bounded-within-budget","unequal rational weights large change negative");
    TColgp_Array2OfPnt periodicNet(1,2,1,4);TColStd_Array2OfReal periodicWeights(1,2,1,4);
    for(int i=1;i<=2;++i){periodicNet(i,1)=gp_Pnt(1,0,i);periodicNet(i,2)=gp_Pnt(0,1,i);periodicNet(i,3)=gp_Pnt(-1,0,i);periodicNet(i,4)=gp_Pnt(0,-1,i);for(int j=1;j<=4;++j)periodicWeights(i,j)=j%2?1:.5;}
    TColStd_Array1OfReal vk(1,5);TColStd_Array1OfInteger vm(1,5);vm.Init(1);for(int i=1;i<=5;++i)vk(i)=i-1;
    Handle(Geom_BSplineSurface)periodicSurface=new Geom_BSplineSurface(periodicNet,periodicWeights,k,vk,m,vm,1,1,false,true);
    auto unperiodized=Handle(Geom_BSplineSurface)::DownCast(periodicSurface->Copy());unperiodized->SetVNotPeriodic();
    Check(BoundSurfaceDifference(periodicSurface,unperiodized,.01).status=="bounded-within-budget","periodic rational versus native unperiodized positive");
    periodicNet(2,2)=gp_Pnt(0,1,2.5);Handle(Geom_Surface)changedPeriodic=new Geom_BSplineSurface(periodicNet,periodicWeights,k,vk,m,vm,1,1,false,true);
    Check(BoundSurfaceDifference(changedPeriodic,unperiodized,.01).status!="bounded-within-budget","changed periodic rational negative");
    for(double t:{-3.,-1.,0.,1.,3.}){const auto q=Trig(Interval(t),false);Check(q.lo<=std::sin(t)&&q.hi>=std::sin(t),"trig point sanity");}
    std::cout<<"PASS: "<<checks<<" native interval checks, including unsampled narrow spike, placement, tiny range, malformed/weight negatives and surface polynomial comparison\n";return 0;
}catch(const std::exception&e){std::cerr<<e.what()<<'\n';return 1;}catch(const Standard_Failure&e){std::cerr<<e.GetMessageString()<<'\n';return 1;}}
