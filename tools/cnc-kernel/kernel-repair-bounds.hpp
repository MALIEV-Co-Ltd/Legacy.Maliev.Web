#pragma once
// Outward-rounded de Boor envelopes over native coefficients. No samples,
// tessellation, Segment, numerical extrema or native tolerance enlargement.
#include <Standard_Failure.hxx>
#include <TopLoc_Location.hxx>
#include <string>
#include <vector>
#include <functional>
#include <cmath>
#include <GeomAdaptor_Curve.hxx>
#include <Geom2dAdaptor_Curve.hxx>
#include <GeomAdaptor_Surface.hxx>
#include <Geom_BSplineCurve.hxx>
#include <Geom2d_BSplineCurve.hxx>
#include <Geom_BezierCurve.hxx>
#include <Geom2d_BezierCurve.hxx>
#include <Geom_BSplineSurface.hxx>
#include <Geom_BezierSurface.hxx>
#include <BndLib_Add3dCurve.hxx>
#include <BndLib_Add2dCurve.hxx>
#include <BndLib_AddSurface.hxx>
#include <Bnd_Box.hxx>
#include <Bnd_Box2d.hxx>
#include <gp_Lin.hxx>
#include <gp_Lin2d.hxx>
#include <gp_Circ.hxx>
#include <gp_Circ2d.hxx>
#include <gp_Elips.hxx>
#include <gp_Elips2d.hxx>
#include <gp_Pln.hxx>
#include <gp_Cylinder.hxx>
#include <gp_Cone.hxx>
#include <gp_Sphere.hxx>
#include <gp_Torus.hxx>
#include <array>
#include <algorithm>
#include <limits>

namespace MalievRepair {
struct Interval {
    double lo,hi;
    Interval(double value=0):lo(value),hi(value){}
    Interval(double a,double b):lo(a),hi(b){}
};
inline double Down(double x){return std::nextafter(x,-std::numeric_limits<double>::infinity());}
inline double Up(double x){return std::nextafter(x,std::numeric_limits<double>::infinity());}
inline Interval operator+(Interval a,Interval b){return {Down(a.lo+b.lo),Up(a.hi+b.hi)};}
inline Interval operator-(Interval a,Interval b){return {Down(a.lo-b.hi),Up(a.hi-b.lo)};}
inline Interval operator*(Interval a,Interval b){const double x[]={a.lo*b.lo,a.lo*b.hi,a.hi*b.lo,a.hi*b.hi};return {Down(*std::min_element(x,x+4)),Up(*std::max_element(x,x+4))};}
inline Interval operator/(Interval a,Interval b){if(b.lo<=0&&b.hi>=0)throw Standard_Failure("zero interval divisor");return a*Interval(Down(1/b.hi),Up(1/b.lo));}
using HPoint=std::array<Interval,4>;
using Box=std::array<Interval,3>;
inline HPoint Blend(const HPoint&a,const HPoint&b,Interval t){HPoint p;for(int i=0;i<4;++i)p[i]=(Interval(1)-t)*a[i]+t*b[i];return p;}
inline void Union(Box& a,const Box&b){for(int i=0;i<3;++i){a[i].lo=std::min(a[i].lo,b[i].lo);a[i].hi=std::max(a[i].hi,b[i].hi);}}
inline Box EmptyBox(){Box b;for(auto&x:b)x={std::numeric_limits<double>::infinity(),-std::numeric_limits<double>::infinity()};return b;}
inline Box Cartesian(const HPoint&p){Box b;for(int i=0;i<3;++i)b[i]=p[i]/p[3];return b;}
inline Box Placed(Box b,const TopLoc_Location&l){Box out;const auto t=l.Transformation();for(int r=0;r<3;++r){out[r]=Interval(t.Value(r+1,4));for(int c=0;c<3;++c)out[r]=out[r]+Interval(t.Value(r+1,c+1))*b[c];}return out;}
inline double DistanceUpper(const Box&a,const Box&b){Interval sum;for(int i=0;i<3;++i){const auto d=a[i]-b[i];const double m=std::max(std::abs(d.lo),std::abs(d.hi));sum=sum+Interval(m)*Interval(m);}return Up(std::sqrt(sum.hi));}
inline double DistanceLower(const Box&a,const Box&b){Interval sum;for(int i=0;i<3;++i){const double d=std::max(0.0,std::max(Down(a[i].lo-b[i].hi),Down(b[i].lo-a[i].hi)));sum=sum+Interval(d)*Interval(d);}return std::max(0.0,Down(std::sqrt(std::max(0.0,sum.lo))));}
inline Interval Trig(Interval t,bool cosine){
    if(!std::isfinite(t.lo)||!std::isfinite(t.hi)||t.hi-t.lo>=6.28||std::abs(t.lo)>1e12||std::abs(t.hi)>1e12)return {-1,1};
    const Interval pi(3.141592653589793,3.1415926535897936);
    const double n=std::round((t.lo+(t.hi-t.lo)*0.5)/6.283185307179586);
    t=t-Interval(2*n)*pi;
    if(t.lo < -4||t.hi>4)return {-1,1};
    Interval term=cosine?Interval(1):t,sum=term;
    const auto square=t*t;
    for(int k=1;k<24;++k){const int a=cosine?2*k-1:2*k;term=term*(Interval(0)-square)/Interval(a*(a+1));sum=sum+term;}
    // Fixed outward arithmetic, independent of parameter, native geometry and
    // sine/cosine branch. The importer does not vary the floating rounding mode.
    static const Interval remainder=[](){Interval value(1);for(int k=1;k<=48;++k)value=value*Interval(4)/Interval(k);return value;}();
    return {std::max(-1.0,Down(sum.lo-remainder.hi)),std::min(1.0,Up(sum.hi+remainder.hi))};
}
inline Box FrameBox(const gp_Pnt&o,const gp_Dir&x,const gp_Dir&y,const gp_Dir&z,Interval a,Interval b,Interval c){Box v;for(int i=0;i<3;++i)v[i]=Interval(o.Coord(i+1))+Interval(x.Coord(i+1))*a+Interval(y.Coord(i+1))*b+Interval(z.Coord(i+1))*c;return v;}
struct Spline {
    int degree=0; std::vector<Interval> knots;std::vector<HPoint> poles;
    double first=0,last=0;
};
inline HPoint Weighted(double x,double y,double z,double w){if(!std::isfinite(x)||!std::isfinite(y)||!std::isfinite(z)||!std::isfinite(w)||w<=0)throw Standard_Failure("nonfinite coefficient or nonpositive rational weight");return {{Interval(x)*Interval(w),Interval(y)*Interval(w),Interval(z)*Interval(w),Interval(w)}};}
template<class C> inline Spline CurveSpline(const Handle(C)& c) {
    if(c->IsPeriodic())throw Standard_Failure("periodic spline curve bound unsupported");
    Spline s;s.degree=c->Degree();s.first=c->FirstParameter();s.last=c->LastParameter();
    const auto& k=c->KnotSequence();for(int i=k.Lower();i<=k.Upper();++i)s.knots.push_back(k(i));
    for(int i=1;i<=c->NbPoles();++i){const auto p=c->Pole(i);s.poles.push_back(Weighted(p.X(),p.Y(),0,c->Weight(i)));}return s;
}
inline Spline CurveSpline3(const Handle(Geom_BSplineCurve)&c){auto s=CurveSpline(c);for(int i=1;i<=c->NbPoles();++i)s.poles[i-1]=Weighted(c->Pole(i).X(),c->Pole(i).Y(),c->Pole(i).Z(),c->Weight(i));return s;}
inline HPoint DeBoor(const Spline&s,int span,Interval t){
    std::vector<HPoint>d;for(int j=0;j<=s.degree;++j)d.push_back(s.poles.at(span-s.degree+j));
    for(int r=1;r<=s.degree;++r)for(int j=s.degree;j>=r;--j){const int i=span-s.degree+j;const auto den=s.knots.at(i+s.degree-r+1)-s.knots.at(i);if(den.lo<=0)throw Standard_Failure("invalid knot span");const auto a=(t-s.knots.at(i))/den;d[j]=Blend(d[j-1],d[j],a);}return d[s.degree];
}
inline Box SplineBox(const Spline&s,double a,double b){
    if(!std::isfinite(a)||!std::isfinite(b)||a>b||a<s.first||b>s.last)throw Standard_Failure("curve domain outside native basis");
    Box box=EmptyBox();bool used=false;double minWeight=1e300,maxWeight=0;for(const auto&p:s.poles){minWeight=std::min(minWeight,p[3].lo);maxWeight=std::max(maxWeight,p[3].hi);}
    for(int k=s.degree;k<static_cast<int>(s.poles.size());++k){const double l=std::max(a,s.knots.at(k).lo),h=std::min(b,s.knots.at(k+1).hi);if(l>h||s.knots[k].lo==s.knots[k+1].hi)continue;auto p=DeBoor(s,k,{l,h});p[3]={std::max(p[3].lo,minWeight),std::min(p[3].hi,maxWeight)};Union(box,Cartesian(p));used=true;}
    if(!used)throw Standard_Failure("uncovered spline parameter");return box;
}
template<class C> inline Spline BezierSpline(const Handle(C)&c){Spline s;s.degree=c->Degree();s.first=0;s.last=1;for(int i=0;i<=s.degree;++i)s.knots.push_back(0);for(int i=0;i<=s.degree;++i)s.knots.push_back(1);for(int i=1;i<=c->NbPoles();++i){auto p=c->Pole(i);s.poles.push_back(Weighted(p.X(),p.Y(),0,c->Weight(i)));}return s;}
struct CurveBound {
    Handle(Geom_Curve)c3;Handle(Geom2d_Curve)c2;TopLoc_Location location;Spline spline;bool isSpline=false,is2d=false;
    CurveBound(const Handle(Geom_Curve)&c,const TopLoc_Location&l):c3(c),location(l){
        if(c.IsNull())throw Standard_Failure("missing 3d curve");GeomAdaptor_Curve a(c);
        if(a.GetType()==GeomAbs_BSplineCurve){spline=CurveSpline3(a.BSpline());isSpline=true;}
        else if(a.GetType()==GeomAbs_BezierCurve){auto z=a.Bezier();spline=BezierSpline(z);for(int i=1;i<=z->NbPoles();++i)spline.poles[i-1]=Weighted(z->Pole(i).X(),z->Pole(i).Y(),z->Pole(i).Z(),z->Weight(i));isSpline=true;}
        else if(a.GetType()!=GeomAbs_Line&&a.GetType()!=GeomAbs_Circle&&a.GetType()!=GeomAbs_Ellipse)throw Standard_Failure("unsupported exact curve envelope");
    }
    CurveBound(const Handle(Geom2d_Curve)&c):c2(c),is2d(true){
        if(c.IsNull())throw Standard_Failure("missing pcurve");Geom2dAdaptor_Curve a(c);
        if(a.GetType()==GeomAbs_BSplineCurve){spline=CurveSpline(a.BSpline());isSpline=true;}
        else if(a.GetType()==GeomAbs_BezierCurve){spline=BezierSpline(a.Bezier());isSpline=true;}
        else if(a.GetType()!=GeomAbs_Line&&a.GetType()!=GeomAbs_Circle&&a.GetType()!=GeomAbs_Ellipse)throw Standard_Failure("unsupported exact pcurve envelope");
    }
    Box Bounds(double first,double last)const{
        Box b;if(isSpline)b=SplineBox(spline,first,last);
        else if(is2d){Geom2dAdaptor_Curve a(c2);Interval u(first,last);b[2]=Interval(0);
            if(a.GetType()==GeomAbs_Line){auto l=a.Line();for(int i=0;i<2;++i)b[i]=Interval(l.Location().Coord(i+1))+u*Interval(l.Direction().Coord(i+1));}
            else {gp_Ax22d axes;double major,minor;if(a.GetType()==GeomAbs_Circle){auto c=a.Circle();axes=c.Position();major=minor=c.Radius();}else{auto c=a.Ellipse();axes=c.Axis();major=c.MajorRadius();minor=c.MinorRadius();}for(int i=0;i<2;++i)b[i]=Interval(axes.Location().Coord(i+1))+Trig(u,true)*Interval(major)*Interval(axes.XDirection().Coord(i+1))+Trig(u,false)*Interval(minor)*Interval(axes.YDirection().Coord(i+1));}}
        else {GeomAdaptor_Curve a(c3);Interval u(first,last);
            if(a.GetType()==GeomAbs_Line){auto l=a.Line();for(int i=0;i<3;++i)b[i]=Interval(l.Location().Coord(i+1))+u*Interval(l.Direction().Coord(i+1));}
            else {gp_Ax2 axes;double major,minor;if(a.GetType()==GeomAbs_Circle){auto c=a.Circle();axes=c.Position();major=minor=c.Radius();}else{auto c=a.Ellipse();axes=c.Position();major=c.MajorRadius();minor=c.MinorRadius();}b=FrameBox(axes.Location(),axes.XDirection(),axes.YDirection(),axes.Direction(),Trig(u,true)*Interval(major),Trig(u,false)*Interval(minor),Interval(0));}}
        return is2d?b:Placed(b,location);
    }
};
inline std::vector<Interval> SurfaceKnots(const Handle(Geom_BSplineSurface)&s,bool u){
    std::vector<Interval> k;const int n=u?s->NbUKnots():s->NbVKnots(),d=u?s->UDegree():s->VDegree();
    const bool periodic=u?s->IsUPeriodic():s->IsVPeriodic();
    auto knot=[&](int i){return u?s->UKnot(i):s->VKnot(i);};auto mult=[&](int i){return u?s->UMultiplicity(i):s->VMultiplicity(i);};
    const auto period=Interval(knot(n))-Interval(knot(1));
    if(periodic){std::vector<Interval> previous;int need=d+1-mult(1);for(int i=n-1;need>0&&i>=1;--i)for(int j=0;j<mult(i)&&need>0;++j,--need)previous.push_back(Interval(knot(i))-period);if(need)throw Standard_Failure("periodic knot extension unsupported");std::reverse(previous.begin(),previous.end());k=previous;}
    for(int i=1;i<=n;++i)for(int j=0;j<mult(i);++j)k.push_back(Interval(knot(i)));
    if(periodic){int need=d+1-mult(n);for(int i=2;need>0&&i<=n;++i)for(int j=0;j<mult(i)&&need>0;++j,--need)k.push_back(Interval(knot(i))+period);if(need)throw Standard_Failure("periodic knot extension unsupported");}
    return k;
}
struct SurfaceBound {
    Handle(Geom_Surface)surface;TopLoc_Location location;bool spline=false;
    Spline u,v;std::vector<std::vector<HPoint>>poles;int nu=0,nv=0;
    SurfaceBound(const Handle(Geom_Surface)&s,const TopLoc_Location&l):surface(s),location(l){
        if(s.IsNull())throw Standard_Failure("missing surface");GeomAdaptor_Surface a(s);
        if(a.GetType()==GeomAbs_BSplineSurface){auto b=a.BSpline();spline=true;u.degree=b->UDegree();v.degree=b->VDegree();u.knots=SurfaceKnots(b,true);v.knots=SurfaceKnots(b,false);b->Bounds(u.first,u.last,v.first,v.last);nu=static_cast<int>(u.knots.size())-u.degree-1;nv=static_cast<int>(v.knots.size())-v.degree-1;
            for(int i=0;i<nu;++i){std::vector<HPoint>row;for(int j=0;j<nv;++j){const int x=i%b->NbUPoles()+1,y=j%b->NbVPoles()+1;auto p=b->Pole(x,y);row.push_back(Weighted(p.X(),p.Y(),p.Z(),b->Weight(x,y)));}poles.push_back(row);}}
        else if(a.GetType()>GeomAbs_Torus)throw Standard_Failure("unsupported exact surface envelope");
    }
    Box Bounds(Interval U,Interval V)const{
        Box result=EmptyBox();
        if(spline){
            if(U.lo<u.first||U.hi>u.last||V.lo<v.first||V.hi>v.last)throw Standard_Failure("UV interval outside native surface basis");
            bool used=false;double minWeight=1e300,maxWeight=0;for(const auto&row:poles)for(const auto&p:row){minWeight=std::min(minWeight,p[3].lo);maxWeight=std::max(maxWeight,p[3].hi);}
            for(int i=u.degree;i<nu;++i){const double ul=std::max(U.lo,u.knots[i].lo),uh=std::min(U.hi,u.knots[i+1].hi);if(ul>uh||u.knots[i].lo==u.knots[i+1].hi)continue;
                for(int j=v.degree;j<nv;++j){const double vl=std::max(V.lo,v.knots[j].lo),vh=std::min(V.hi,v.knots[j+1].hi);if(vl>vh||v.knots[j].lo==v.knots[j+1].hi)continue;
                    Spline row=v;for(int x=0;x<nv;++x){Spline col=u;for(int y=0;y<nu;++y)col.poles.push_back(poles[y][x]);row.poles.push_back(DeBoor(col,i,{ul,uh}));}
                    auto p=DeBoor(row,j,{vl,vh});p[3]={std::max(p[3].lo,minWeight),std::min(p[3].hi,maxWeight)};Union(result,Cartesian(p));used=true;}}
            if(!used)throw Standard_Failure("uncovered native surface interval");
        }else{GeomAdaptor_Surface a(surface);gp_Ax3 axes;Interval x,y,z;
            switch(a.GetType()){
                case GeomAbs_Plane:{auto p=a.Plane();axes=p.Position();x=U;y=V;z=Interval(0);break;}
                case GeomAbs_Cylinder:{auto p=a.Cylinder();axes=p.Position();x=Interval(p.Radius())*Trig(U,true);y=Interval(p.Radius())*Trig(U,false);z=V;break;}
                case GeomAbs_Cone:{auto p=a.Cone();axes=p.Position();auto r=Interval(p.RefRadius())+V*Trig(Interval(p.SemiAngle()),false);x=r*Trig(U,true);y=r*Trig(U,false);z=V*Trig(Interval(p.SemiAngle()),true);break;}
                case GeomAbs_Sphere:{auto p=a.Sphere();axes=p.Position();x=Interval(p.Radius())*Trig(V,true)*Trig(U,true);y=Interval(p.Radius())*Trig(V,true)*Trig(U,false);z=Interval(p.Radius())*Trig(V,false);break;}
                case GeomAbs_Torus:{auto p=a.Torus();axes=p.Position();auto r=Interval(p.MajorRadius())+Interval(p.MinorRadius())*Trig(V,true);x=r*Trig(U,true);y=r*Trig(U,false);z=Interval(p.MinorRadius())*Trig(V,false);break;}
                default:throw Standard_Failure("unsupported surface interval");
            }result=FrameBox(axes.Location(),axes.XDirection(),axes.YDirection(),axes.Direction(),x,y,z);
        }return Placed(result,location);
    }
};
struct Residual {
    std::string status="unavailable",reason;double lower=0,upper=0;int subdivisions=0,intervals=0;
};
// Power coefficients of homogeneous bivariate maps on a common knot cell.
// Interval arithmetic retains rounding error; no floating knot insertion occurs.
struct Polynomial {
    int width;std::vector<HPoint> coefficients;
    Polynomial(int w=1):width(w),coefficients(w*w){}
};
inline Polynomial Constant(const HPoint&p,int w){Polynomial a(w);a.coefficients[0]=p;return a;}
inline Polynomial PolynomialBlend(const Polynomial&a,const Polynomial&b,Interval base,Interval slope,bool v){
    Polynomial out(a.width);for(int i=0;i<a.width;++i)for(int j=0;j<a.width;++j)for(int c=0;c<4;++c){const int k=i*a.width+j;const auto delta=b.coefficients[k][c]-a.coefficients[k][c];out.coefficients[k][c]=out.coefficients[k][c]+a.coefficients[k][c]+base*delta;const int x=i+(v?0:1),y=j+(v?1:0);if(x<a.width&&y<a.width)out.coefficients[x*a.width+y][c]=out.coefficients[x*a.width+y][c]+slope*delta;}return out;
}
inline Polynomial PolynomialDeBoor(const Spline&s,int span,Interval first,Interval last,const std::vector<Polynomial>&poles,bool v){
    std::vector<Polynomial>d;for(int j=0;j<=s.degree;++j)d.push_back(poles.at(span-s.degree+j));
    for(int r=1;r<=s.degree;++r)for(int j=s.degree;j>=r;--j){const int i=span-s.degree+j;const auto den=s.knots.at(i+s.degree-r+1)-s.knots.at(i);if(den.lo<=0)throw Standard_Failure("invalid polynomial knot denominator");d[j]=PolynomialBlend(d[j-1],d[j],(first-s.knots[i])/den,(last-first)/den,v);}return d[s.degree];
}
inline int Span(const Spline&s,int count,double a,double b){for(int i=s.degree;i<count;++i)if(a>=s.knots[i].hi&&b<=s.knots[i+1].lo&&a<b)return i;throw Standard_Failure("common knot cell not enclosed");}
inline Polynomial SurfacePolynomial(const SurfaceBound&s,double a,double b,double c,double d){
    const int width=s.u.degree+s.v.degree+2,ui=Span(s.u,s.nu,a,b),vi=Span(s.v,s.nv,c,d);std::vector<Polynomial>row;
    for(int y=0;y<s.nv;++y){std::vector<Polynomial>col;for(int x=0;x<s.nu;++x)col.push_back(Constant(s.poles[x][y],width));row.push_back(PolynomialDeBoor(s.u,ui,Interval(a),Interval(b),col,false));}
    return PolynomialDeBoor(s.v,vi,Interval(c),Interval(d),row,true);
}
inline double RationalPolynomialDifference(const Polynomial&a,const Polynomial&b,double minA,double minB){
    const int w=2*std::max(a.width,b.width);std::vector<std::array<Interval,3>>residual(w*w);
    for(int i=0;i<a.width;++i)for(int j=0;j<a.width;++j)for(int k=0;k<b.width;++k)for(int l=0;l<b.width;++l)for(int c=0;c<3;++c){const auto&x=a.coefficients[i*a.width+j];const auto&y=b.coefficients[k*b.width+l];auto&r=residual[(i+k)*w+j+l][c];r=r+x[c]*y[3]-y[c]*x[3];}
    Interval sum;for(int c=0;c<3;++c){Interval bound;for(const auto&coefficient:residual)bound=bound+Interval(std::max(std::abs(coefficient[c].lo),std::abs(coefficient[c].hi)));sum=sum+bound*bound;}
    return (Interval(Up(std::sqrt(sum.hi)))/(Interval(minA)*Interval(minB))).hi;
}
inline Residual BoundSurfaceDifference(const Handle(Geom_Surface)&before,const Handle(Geom_Surface)&after,double budget,
    const std::function<bool()>& continues = {}, int intervalLimit = 1024){
    Residual result;try{
        if(continues && !continues())throw Standard_Failure("assessment-monotonic-time-limit");
        if(intervalLimit<=0)throw Standard_Failure("periodic comparison interval limit");
        if(!std::isfinite(budget)||budget<=0)throw Standard_Failure("invalid surface comparison budget");
        SurfaceBound a(before,TopLoc_Location()),b(after,TopLoc_Location());if(!a.spline||!b.spline)throw Standard_Failure("periodic comparison requires native spline bases");
        if(a.u.first!=b.u.first||a.u.last!=b.u.last||a.v.first!=b.v.first||a.v.last!=b.v.last)throw Standard_Failure("periodic parameter correspondence differs");
        std::vector<double>u={a.u.first,a.u.last},v={a.v.first,a.v.last};
        for(const auto*s:{&a,&b}){for(const auto k:s->u.knots)if(k.lo==k.hi&&k.lo>a.u.first&&k.lo<a.u.last)u.push_back(k.lo);for(const auto k:s->v.knots)if(k.lo==k.hi&&k.lo>a.v.first&&k.lo<a.v.last)v.push_back(k.lo);}
        std::sort(u.begin(),u.end());u.erase(std::unique(u.begin(),u.end()),u.end());std::sort(v.begin(),v.end());v.erase(std::unique(v.begin(),v.end()),v.end());
        double minA=1e300,minB=1e300;for(const auto&r:a.poles)for(const auto&p:r)minA=std::min(minA,p[3].lo);for(const auto&r:b.poles)for(const auto&p:r)minB=std::min(minB,p[3].lo);
        if(u.size()*v.size()>1024)throw Standard_Failure("periodic polynomial resource limit");
        for(size_t i=1;i<u.size();++i)for(size_t j=1;j<v.size();++j){
            if(continues && !continues())throw Standard_Failure("assessment-monotonic-time-limit");
            if(result.intervals>=std::min(intervalLimit,1024))throw Standard_Failure("periodic comparison interval limit");
            ++result.intervals;const double upper=RationalPolynomialDifference(SurfacePolynomial(a,u[i-1],u[i],v[j-1],v[j]),SurfacePolynomial(b,u[i-1],u[i],v[j-1],v[j]),minA,minB);if(!std::isfinite(upper))throw Standard_Failure("nonfinite periodic bound");result.upper=std::max(result.upper,upper);}
        result.status=result.upper<=budget?"bounded-within-budget":"unresolved-upper-bound-exceeds-budget";
    }catch(const Standard_Failure&e){result.reason=e.GetMessageString()?e.GetMessageString():"surface comparison unavailable";}
    return result;
}
inline Residual BoundResidual(const CurveBound&curve,const CurveBound&pcurve,const SurfaceBound&surface,double first,double last,double budget){
    Residual out;struct Part{double a,b;int depth;};std::vector<Part>todo(1,{first,last,0});out.upper=0;bool unresolved=false;
    try{
        if(!std::isfinite(budget)||budget<=0||!std::isfinite(first)||!std::isfinite(last)||first>=last)throw Standard_Failure("invalid residual policy or interval");
        while(!todo.empty()){
            auto q=todo.back();todo.pop_back();++out.intervals;if(out.intervals>32768)throw Standard_Failure("subdivision resource limit");
            const auto c=curve.Bounds(q.a,q.b),uv=pcurve.Bounds(q.a,q.b),s=surface.Bounds(uv[0],uv[1]);
            for(const auto&box:{c,uv,s})for(const auto&coordinate:box)if(!std::isfinite(coordinate.lo)||!std::isfinite(coordinate.hi)||coordinate.lo>coordinate.hi)throw Standard_Failure("nonfinite or invalid geometry envelope");
            const double upper=DistanceUpper(c,s),lower=DistanceLower(c,s);out.lower=std::max(out.lower,lower);
            if(!std::isfinite(upper))throw Standard_Failure("nonfinite residual enclosure");
            if(lower>budget){out.status="exceeds-budget";out.upper=upper;return out;}
            if(upper<=budget){out.upper=std::max(out.upper,upper);continue;}
            const double middle=q.a+(q.b-q.a)*0.5;
            if(q.depth>=32||middle<=q.a||middle>=q.b){unresolved=true;out.reason="subdivision resolution limit";continue;}
            ++out.subdivisions;todo.push_back({q.a,middle,q.depth+1});todo.push_back({middle,q.b,q.depth+1});
        }out.status=unresolved?"unavailable":"bounded-within-budget";
    }catch(const Standard_Failure&err){out.status="unavailable";out.reason=err.GetMessageString()?err.GetMessageString():"native interval failure";}
    return out;
}
}
