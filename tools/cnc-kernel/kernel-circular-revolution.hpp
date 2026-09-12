#pragma once
#include <Adaptor3d_Curve.hxx>
#include <GeomAbs_SurfaceType.hxx>
#include <gp_Ax1.hxx>
#include <gp_Circ.hxx>
#include <gp_Ax3.hxx>
#include <gp_Vec.hxx>
#include <array>
#include <algorithm>
#include <cmath>
#include <limits>

namespace MalievCircularRevolution {
struct CircleRevolution { gp_Ax1 axis; gp_Circ circle; };
struct ProfileMap { gp_Ax3 frame; double radius=0,minor=0,phase=0; int sense=0; };
inline bool Same(double a,double b) { return std::isfinite(a)&&std::isfinite(b)&&std::abs(a-b)<=64*std::numeric_limits<double>::epsilon()*std::max(1.0,std::max(std::abs(a),std::abs(b))); }
// Whole circle coefficients must lie in the axis/radial meridian. This is
// the existing numerical band roundoff policy, not a source metric allowance.
inline bool Meridian(const CircleRevolution &s, ProfileMap &m) {
  const gp_Vec a(s.axis.Direction()),d(s.axis.Location(),s.circle.Location());
  const double h=d.Dot(a); const gp_Vec radial=d-a*h;
  m.radius=radial.Magnitude(); m.minor=s.circle.Radius();
  if (!std::isfinite(m.radius)||m.radius<=m.minor||Same(m.radius,m.minor)) return false;
  const gp_Dir x(radial), y=s.axis.Direction().Crossed(x);
  const gp_Vec cx(s.circle.XAxis().Direction()),cy(s.circle.YAxis().Direction());
  if (!Same(cx.Dot(gp_Vec(y)),0)||!Same(cy.Dot(gp_Vec(y)),0)) return false;
  const double ex=cx.Dot(gp_Vec(x)),ax=cx.Dot(a),ey=cy.Dot(gp_Vec(x)),ay=cy.Dot(a),det=ex*ay-ey*ax;
  if (!Same(ex*ex+ax*ax,1)||!Same(ey*ey+ay*ay,1)||!Same(ex*ey+ax*ay,0)||!Same(std::abs(det),1)) return false;
  m.sense=det>0?1:-1; m.phase=std::atan2(ax,ex);
  if (!Same(ey,-m.sense*std::sin(m.phase))||!Same(ay,m.sense*std::cos(m.phase))) return false;
  m.frame=gp_Ax3(s.axis.Location().Translated(a*h),s.axis.Direction(),x);
  return std::isfinite(m.phase);
}
template<class Adaptor> bool Read(const Adaptor &surface, CircleRevolution &out) {
  if (surface.GetType() != GeomAbs_SurfaceOfRevolution) return false;
  const auto basis = surface.BasisCurve();
  if (basis.IsNull() || basis->GetType() != GeomAbs_Circle) return false;
  out.axis = surface.AxeOfRevolution(); out.circle = basis->Circle();
  if (!std::isfinite(out.circle.Radius()) || out.circle.Radius() <= 0) return false;
  for (int k=1;k<=3;++k)
    for (double v : {out.axis.Location().Coord(k),out.axis.Direction().Coord(k),out.circle.Location().Coord(k),
                     out.circle.XAxis().Direction().Coord(k),out.circle.YAxis().Direction().Coord(k)})
      if (!std::isfinite(v)) return false;
  return true;
}
// Rodrigues about the actual native axis, preserving the circle's V basis.
// T is outward Interval or Jet. All products/subtractions, including the
// translated circle and axis projection, take place in that arithmetic.
template<class T> std::array<T,3> Image(const CircleRevolution &s, T cu,T su,T cv,T sv) {
  std::array<T,3> d,a,cross,out; T dot(0);
  for (int k=0;k<3;++k) {
    a[k]=T(s.axis.Direction().Coord(k+1));
    d[k]=T(s.circle.Location().Coord(k+1))-T(s.axis.Location().Coord(k+1))
      +T(s.circle.Radius())*(cv*T(s.circle.XAxis().Direction().Coord(k+1))+sv*T(s.circle.YAxis().Direction().Coord(k+1)));
    dot=dot+a[k]*d[k];
  }
  for (int k=0;k<3;++k) cross[k]=a[(k+1)%3]*d[(k+2)%3]-a[(k+2)%3]*d[(k+1)%3];
  for (int k=0;k<3;++k) out[k]=T(s.axis.Location().Coord(k+1))+cu*d[k]+su*cross[k]+(T(1)-cu)*a[k]*dot;
  return out;
}
}
