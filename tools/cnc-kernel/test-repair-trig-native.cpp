#include "kernel-repair-bounds.hpp"
#include <chrono>
#include <cstring>
#include <iostream>
#include <stdexcept>
using namespace MalievRepair;
// Retained pre-hoist arithmetic, independent of the optimized constant.
static Interval BaselineTrig(Interval t, bool cosine) {
  if (!std::isfinite(t.lo) || !std::isfinite(t.hi) || t.hi-t.lo>=6.28 ||
      std::abs(t.lo)>1e12 || std::abs(t.hi)>1e12) return {-1,1};
  const Interval pi(3.141592653589793,3.1415926535897936);
  const double n=std::round((t.lo+(t.hi-t.lo)*.5)/6.283185307179586);
  t=t-Interval(2*n)*pi;
  if(t.lo < -4 || t.hi > 4) return {-1,1};
  Interval term=cosine?Interval(1):t,sum=term;
  const auto square=t*t;
  for(int k=1;k<24;++k) {
    const int a=cosine?2*k-1:2*k;
    term=term*(Interval(0)-square)/Interval(a*(a+1));sum=sum+term;
  }
  Interval remainder(1);
  for(int k=1;k<=48;++k) remainder=remainder*Interval(4)/Interval(k);
  return {std::max(-1.0,Down(sum.lo-remainder.hi)),std::min(1.0,Up(sum.hi+remainder.hi))};
}
static bool SameBits(double a, double b) { return std::memcmp(&a,&b,sizeof(double))==0; }
int main() {
  try {
    std::vector<Interval> ranges = {{0,0},{-0.,-0.},{-4,4},{-3.14,3.14},
      {0,6.28},{0,std::nextafter(6.28,0.)},{1e12,1e12},
      {std::nextafter(1e12,INFINITY),std::nextafter(1e12,INFINITY)},
      {NAN,NAN},{-INFINITY,INFINITY},{3.141592653589793,3.1415926535897936}};
    for(int i=-512;i<=512;++i) {
      const double value=i*.03125;
      ranges.push_back({value,std::nextafter(value,INFINITY)});
      ranges.push_back({value,value+.000001});
    }
    size_t checks=0;
    for(const auto &range:ranges) for(bool cosine:{false,true}) {
      const auto prior=BaselineTrig(range,cosine),current=Trig(range,cosine);
      if(!SameBits(prior.lo,current.lo)||!SameBits(prior.hi,current.hi))
        throw std::runtime_error("trigonometric interval bits changed");
      ++checks;
    }
    volatile double sink=0;
    auto benchmark=[&](bool baseline) {
      const auto start=std::chrono::steady_clock::now();
      for(int repeat=0;repeat<5;++repeat) for(const auto &range:ranges)
        sink += (baseline?BaselineTrig(range,true):Trig(range,true)).hi;
      return std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count();
    };
    const double before=benchmark(true),after=benchmark(false);
    std::cout<<"PASS: "<<checks<<" bit-exact trigonometric intervals; baselineMs="<<before
             <<" currentMs="<<after<<" sink="<<sink<<'\n';
    return 0;
  } catch(const std::exception &error) { std::cerr<<error.what()<<'\n'; return 1; }
}
