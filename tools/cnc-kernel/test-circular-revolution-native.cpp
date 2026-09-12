#include "kernel-repair-correlated.hpp"
#include <Geom_SurfaceOfRevolution.hxx>
#include <Geom_Circle.hxx>
#include <Geom_Line.hxx>
#include <iostream>
using namespace MalievRepair;
static int checks = 0;
static void Check(bool ok, const char *why) { ++checks; if (!ok) throw std::runtime_error(why); }
static bool Contains(Interval a, double b) { return a.lo <= b && b <= a.hi; }
int main() {
  try {
    Handle(Geom_Circle) circle = new Geom_Circle(gp_Ax2(gp_Pnt(12,0,5),gp_Dir(0,1,0),gp_Dir(0,0,-1)),3);
    for (int reversed = 0; reversed < 2; ++reversed) {
      Handle(Geom_Surface) surface = new Geom_SurfaceOfRevolution(circle,gp_Ax1(gp_Pnt(),gp_Dir(0,0,reversed?-1:1)));
      for (int placed = 0; placed < 2; ++placed) {
        gp_Trsf trsf;
        if (placed) { trsf.SetRotation(gp_Ax1(gp_Pnt(),gp_Dir(1,2,3)),.713); trsf.SetTranslationPart(gp_Vec(17,-23,41)); }
        SurfaceBound bound(surface,TopLoc_Location(trsf));
        for (const auto &domain : std::vector<std::array<double,4>>{{{0,6.283185307179586,0,3.141592653589793}},{{.2,.2+6.283185307179586,.3,2.7}},{{.31,.37,.8,.91}},{{-1,1,4,7}}}) {
          const Box box = bound.Bounds({domain[0],domain[1]},{domain[2],domain[3]});
          const auto wholeU=SurfaceJet(bound,Jet(Interval(domain[0],domain[1]),Interval(1)),Jet(Interval(domain[2],domain[3])));
          const auto wholeV=SurfaceJet(bound,Jet(Interval(domain[0],domain[1])),Jet(Interval(domain[2],domain[3]),Interval(1)));
          for (int i=0;i<=4;++i) for (int j=0;j<=4;++j) {
            double u=domain[0]+(domain[1]-domain[0])*i/4,v=domain[2]+(domain[3]-domain[2])*j/4;
            gp_Pnt p; gp_Vec du,dv; surface->D1(u,v,p,du,dv); p.Transform(trsf); du.Transform(trsf); dv.Transform(trsf);
            const auto ju=SurfaceJet(bound,Jet(Interval(u),Interval(1)),Jet(Interval(v)));
            const auto jv=SurfaceJet(bound,Jet(Interval(u)),Jet(Interval(v),Interval(1)));
            for(int k=0;k<3;++k) {
              Check(Contains(box[k],p.Coord(k+1)),"whole-domain revolution image contains native D1 point");
              Check(Contains(ju[k].value,p.Coord(k+1))&&Contains(jv[k].value,p.Coord(k+1)),"point interval preserves native phase and placement");
              Check(Contains(ju[k].derivative,du.Coord(k+1)),"native revolution U derivative enclosed");
              Check(Contains(jv[k].derivative,dv.Coord(k+1)),"native circle V derivative enclosed");
              Check(Contains(wholeU[k].derivative,du.Coord(k+1))&&Contains(wholeV[k].derivative,dv.Coord(k+1)),"whole finite domain derivatives enclosed");
            }
          }
        }
      }
    }
    MalievCircularRevolution::CircleRevolution source; source.axis=gp_Ax1(gp_Pnt(),gp_Dir(0,0,1)); source.circle=circle->Circ();
    MalievCircularRevolution::ProfileMap map;
    Check(MalievCircularRevolution::Meridian(source,map),"whole meridional circle coefficients prove ring profile");
    Check(map.sense==-1&&std::abs(map.phase+std::acos(-1.0)/2)<1e-15&&map.radius==12&&map.minor==3,"nonstandard native phase and negative Jacobian retained");
    source.circle=gp_Circ(gp_Ax2(gp_Pnt(12,0,5),gp_Dir(0,0,1)),3);
    Check(!MalievCircularRevolution::Meridian(source,map),"nonmeridional circle is not a torus profile");
    Handle(Geom_Surface) nonmeridian=new Geom_SurfaceOfRevolution(new Geom_Circle(source.circle),source.axis);
    Check(std::isfinite(SurfaceBound(nonmeridian,TopLoc_Location()).Bounds({.1,.2},{.3,.4})[0].hi),"generic image does not require manufacturing meridian proof");
    source.circle=gp_Circ(gp_Ax2(gp_Pnt(2,0,5),gp_Dir(0,1,0)),3);
    Check(!MalievCircularRevolution::Meridian(source,map),"axis-intersecting circle cannot receive ring role");
    source.circle=gp_Circ(gp_Ax2(gp_Pnt(3,0,5),gp_Dir(0,1,0)),3);
    Check(!MalievCircularRevolution::Meridian(source,map),"axis-tangent circle cannot receive ring role");
    Handle(Geom_Surface) unsupported=new Geom_SurfaceOfRevolution(new Geom_Line(gp_Pnt(2,0,0),gp_Dir(0,0,1)),source.axis);
    bool rejected=false; try {SurfaceBound b(unsupported,TopLoc_Location());}catch(const Standard_Failure&){rejected=true;}
    Check(rejected,"noncircular native basis stays unsupported");
    SurfaceBound b(nonmeridian,TopLoc_Location());
    for(const auto& domain:std::vector<Interval>{{1,0},{0,std::numeric_limits<double>::infinity()},{std::numeric_limits<double>::quiet_NaN(),1}}){
      rejected=false;try{b.Bounds(domain,{0,1});}catch(const Standard_Failure&){rejected=true;}Check(rejected,"invalid finite U interval rejects");
      rejected=false;try{b.Bounds({0,1},domain);}catch(const Standard_Failure&){rejected=true;}Check(rejected,"invalid finite V interval rejects");
    }
    std::cout << "PASS: " << checks << " circular revolution native checks\n";
  } catch (const Standard_Failure &e) { std::cerr << "FAIL after " << checks << ": " << e.GetMessageString() << '\n'; return 1; }
    catch (const std::exception &e) { std::cerr << "FAIL after " << checks << ": " << e.what() << '\n'; return 1; }
}
