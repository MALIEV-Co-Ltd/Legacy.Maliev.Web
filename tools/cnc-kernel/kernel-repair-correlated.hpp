#pragma once
#include "kernel-repair-bounds.hpp"
#include <chrono>
#include <ctime>
#include <functional>
#include <map>

namespace MalievRepair {
struct Jet {
  Interval value, derivative;
  Jet(Interval v = Interval(), Interval d = Interval())
      : value(v), derivative(d) {}
};
inline Jet operator+(const Jet &a, const Jet &b) {
  return {a.value + b.value, a.derivative + b.derivative};
}
inline Jet operator-(const Jet &a, const Jet &b) {
  return {a.value - b.value, a.derivative - b.derivative};
}
inline Jet operator*(const Jet &a, const Jet &b) {
  return {a.value * b.value, a.derivative * b.value + a.value * b.derivative};
}
inline Jet operator/(const Jet &a, const Jet &b) {
  return {a.value / b.value, (a.derivative * b.value - a.value * b.derivative) /
                                 (b.value * b.value)};
}
inline Jet Sine(const Jet &a) {
  return {Trig(a.value, false), Trig(a.value, true) * a.derivative};
}
inline Jet Cosine(const Jet &a) {
  return {Trig(a.value, true),
          (Interval(0) - Trig(a.value, false)) * a.derivative};
}
using HJet = std::array<Jet, 4>;
using PointJet = std::array<Jet, 3>;
inline HJet ConstantJet(const HPoint &p) {
  HJet j;
  for (int i = 0; i < 4; ++i)
    j[i] = Jet(p[i]);
  return j;
}
inline void UnionJet(HJet &a, const HJet &b) {
  for (int i = 0; i < 4; ++i) {
    a[i].value.lo = std::min(a[i].value.lo, b[i].value.lo);
    a[i].value.hi = std::max(a[i].value.hi, b[i].value.hi);
    a[i].derivative.lo = std::min(a[i].derivative.lo, b[i].derivative.lo);
    a[i].derivative.hi = std::max(a[i].derivative.hi, b[i].derivative.hi);
  }
}
inline HJet EmptyJet() {
  HJet j;
  for (auto &x : j) {
    x.value = x.derivative = {std::numeric_limits<double>::infinity(),
                              -std::numeric_limits<double>::infinity()};
  }
  return j;
}
inline PointJet CartesianJet(HJet h, double minWeight, double maxWeight) {
  // The positive control-weight hull constrains W, never W'.
  h[3].value.lo = std::max(h[3].value.lo, minWeight);
  h[3].value.hi = std::min(h[3].value.hi, maxWeight);
  PointJet p;
  for (int i = 0; i < 3; ++i)
    p[i] = h[i] / h[3];
  return p;
}
inline HJet SeedDeBoorJet(const Spline &s, int span, Jet parameter,
                          const std::vector<HJet> &poles) {
  std::vector<HJet> d;
  for (int j = 0; j <= s.degree; ++j)
    d.push_back(poles.at(span - s.degree + j));
  for (int r = 1; r <= s.degree; ++r)
    for (int j = s.degree; j >= r; --j) {
      const int i = span - s.degree + j;
      const auto denominator = s.knots.at(i + s.degree - r + 1) - s.knots.at(i);
      if (denominator.lo <= 0)
        throw Standard_Failure("invalid jet knot denominator");
      const Jet alpha = (parameter - Jet(s.knots[i])) / Jet(denominator);
      for (int c = 0; c < 4; ++c)
        d[j][c] = (Jet(Interval(1)) - alpha) * d[j - 1][c] + alpha * d[j][c];
    }
  return d[s.degree];
}
inline HJet DeBoorJet(const Spline &s, int span, Jet parameter,
                      const std::vector<HJet> &poles) {
  // Hold this parameter fixed while propagating derivatives already present
  // in the input poles (the first tensor stage's contribution).
  HJet result = SeedDeBoorJet(s, span, Jet(parameter.value), poles);
  if (s.degree == 0)
    return result;
  Spline derivative;
  derivative.degree = s.degree - 1;
  derivative.knots.assign(s.knots.begin() + 1, s.knots.end() - 1);
  derivative.poles.resize(poles.size() - 1);
  // Only these derivative coefficients support the selected original span.
  // Form differences BEFORE interval evaluation to retain shared translation
  // cancellation. No native rounded derivative-spline conversion is used.
  for (int i = span - s.degree; i < span; ++i) {
    const Interval denominator =
        s.knots.at(i + s.degree + 1) - s.knots.at(i + 1);
    if (denominator.lo <= 0)
      throw Standard_Failure("invalid derivative coefficient denominator");
    for (int axis = 0; axis < 4; ++axis)
      derivative.poles[i][axis] =
          Interval(s.degree) *
          (poles[i + 1][axis].value - poles[i][axis].value) / denominator;
  }
  const HPoint slope = DeBoor(derivative, span - 1, parameter.value);
  for (int axis = 0; axis < 4; ++axis)
    result[axis].derivative =
        result[axis].derivative + slope[axis] * parameter.derivative;
  return result;
}
inline void RequireContinuous(const Spline &s, Interval range) {
  for (size_t i = 0; i < s.knots.size();) {
    size_t j = i + 1;
    while (j < s.knots.size() && s.knots[j].lo == s.knots[i].lo &&
           s.knots[j].hi == s.knots[i].hi)
      ++j;
    const bool touchesInteriorKnot =
        s.knots[i].lo >= range.lo && s.knots[i].hi <= range.hi &&
        s.knots[i].lo > s.first && s.knots[i].hi < s.last;
    const int multiplicity = static_cast<int>(j - i);
    if (touchesInteriorKnot && multiplicity > s.degree)
      throw Standard_Failure("discontinuous spline excludes centered bound");
    i = j;
  }
}
inline PointJet SplineJet(const Spline &s, Jet parameter) {
  if (parameter.value.lo < s.first || parameter.value.hi > s.last)
    throw Standard_Failure("jet curve outside basis");
  RequireContinuous(s, parameter.value);
  HJet result = EmptyJet();
  bool used = false;
  std::vector<HJet> poles;
  double minWeight = 1e300, maxWeight = 0;
  for (const auto &p : s.poles) {
    poles.push_back(ConstantJet(p));
    minWeight = std::min(minWeight, p[3].lo);
    maxWeight = std::max(maxWeight, p[3].hi);
  }
  for (int k = s.degree; k < static_cast<int>(poles.size()); ++k) {
    const double low = std::max(parameter.value.lo, s.knots[k].lo),
                 high = std::min(parameter.value.hi, s.knots[k + 1].hi);
    if (low > high || s.knots[k].lo == s.knots[k + 1].hi)
      continue;
    Jet clipped({low, high}, parameter.derivative);
    UnionJet(result, DeBoorJet(s, k, clipped, poles));
    used = true;
  }
  if (!used)
    throw Standard_Failure("uncovered curve jet span");
  return CartesianJet(result, minWeight, maxWeight);
}
inline PointJet PlacedJet(const PointJet &p, const TopLoc_Location &location) {
  PointJet result;
  const auto transform = location.Transformation();
  for (int r = 0; r < 3; ++r) {
    result[r] = Jet(Interval(transform.Value(r + 1, 4)));
    for (int c = 0; c < 3; ++c)
      result[r] =
          result[r] + Jet(Interval(transform.Value(r + 1, c + 1))) * p[c];
  }
  return result;
}
inline PointJet FrameJet(const gp_Pnt &origin, const gp_Dir &x, const gp_Dir &y,
                         const gp_Dir &z, Jet u, Jet v, Jet w) {
  PointJet result;
  for (int i = 0; i < 3; ++i)
    result[i] =
        Jet(Interval(origin.Coord(i + 1))) + Jet(Interval(x.Coord(i + 1))) * u +
        Jet(Interval(y.Coord(i + 1))) * v + Jet(Interval(z.Coord(i + 1))) * w;
  return result;
}
inline PointJet CurveJet(const CurveBound &curve, Interval range) {
  const Jet parameter(range, Interval(1));
  PointJet result;
  if (curve.isSpline)
    result = SplineJet(curve.spline, parameter);
  else if (curve.is2d) {
    Geom2dAdaptor_Curve adaptor(curve.c2);
    result[2] = Jet();
    if (adaptor.GetType() == GeomAbs_Line) {
      const auto line = adaptor.Line();
      for (int i = 0; i < 2; ++i)
        result[i] = Jet(Interval(line.Location().Coord(i + 1))) +
                    parameter * Jet(Interval(line.Direction().Coord(i + 1)));
    } else {
      gp_Ax22d axes;
      double major, minor;
      if (adaptor.GetType() == GeomAbs_Circle) {
        const auto c = adaptor.Circle();
        axes = c.Position();
        major = minor = c.Radius();
      } else {
        const auto c = adaptor.Ellipse();
        axes = c.Axis();
        major = c.MajorRadius();
        minor = c.MinorRadius();
      }
      const Jet cosine = Cosine(parameter), sine = Sine(parameter);
      for (int i = 0; i < 2; ++i)
        result[i] = Jet(Interval(axes.Location().Coord(i + 1))) +
                    cosine * Jet(Interval(major)) *
                        Jet(Interval(axes.XDirection().Coord(i + 1))) +
                    sine * Jet(Interval(minor)) *
                        Jet(Interval(axes.YDirection().Coord(i + 1)));
    }
  } else {
    GeomAdaptor_Curve adaptor(curve.c3);
    if (adaptor.GetType() == GeomAbs_Line) {
      const auto line = adaptor.Line();
      for (int i = 0; i < 3; ++i)
        result[i] = Jet(Interval(line.Location().Coord(i + 1))) +
                    parameter * Jet(Interval(line.Direction().Coord(i + 1)));
    } else {
      gp_Ax2 axes;
      double major, minor;
      if (adaptor.GetType() == GeomAbs_Circle) {
        const auto c = adaptor.Circle();
        axes = c.Position();
        major = minor = c.Radius();
      } else {
        const auto c = adaptor.Ellipse();
        axes = c.Position();
        major = c.MajorRadius();
        minor = c.MinorRadius();
      }
      result =
          FrameJet(axes.Location(), axes.XDirection(), axes.YDirection(),
                   axes.Direction(), Cosine(parameter) * Jet(Interval(major)),
                   Sine(parameter) * Jet(Interval(minor)), Jet());
    }
  }
  return curve.is2d ? result : PlacedJet(result, curve.location);
}
inline PointJet SurfaceJet(const SurfaceBound &surface, Jet u, Jet v) {
  GeomAdaptor_Surface revolutionAdaptor(surface.surface);
  if (revolutionAdaptor.GetType() == GeomAbs_SurfaceOfRevolution) {
    MalievCircularRevolution::CircleRevolution r;
    if (!MalievCircularRevolution::Read(revolutionAdaptor,r)) throw Standard_Failure("unsupported revolution jet basis");
    return PlacedJet(MalievCircularRevolution::Image(r,Cosine(u),Sine(u),Cosine(v),Sine(v)),surface.location);
  }
  PointJet result;
  if (surface.spline) {
    if (u.value.lo < surface.u.first || u.value.hi > surface.u.last ||
        v.value.lo < surface.v.first || v.value.hi > surface.v.last)
      throw Standard_Failure("jet UV outside basis");
    RequireContinuous(surface.u, u.value);
    RequireContinuous(surface.v, v.value);
    HJet total = EmptyJet();
    bool used = false;
    double minWeight = 1e300, maxWeight = 0;
    for (const auto &row : surface.poles)
      for (const auto &p : row) {
        minWeight = std::min(minWeight, p[3].lo);
        maxWeight = std::max(maxWeight, p[3].hi);
      }
    for (int i = surface.u.degree; i < surface.nu; ++i) {
      const double ul = std::max(u.value.lo, surface.u.knots[i].lo),
                   uh = std::min(u.value.hi, surface.u.knots[i + 1].hi);
      if (ul > uh || surface.u.knots[i].lo == surface.u.knots[i + 1].hi)
        continue;
      for (int j = surface.v.degree; j < surface.nv; ++j) {
        const double vl = std::max(v.value.lo, surface.v.knots[j].lo),
                     vh = std::min(v.value.hi, surface.v.knots[j + 1].hi);
        if (vl > vh || surface.v.knots[j].lo == surface.v.knots[j + 1].hi)
          continue;
        std::vector<HJet> row(surface.nv);
        for (int y = j - surface.v.degree; y <= j; ++y) {
          std::vector<HJet> column(surface.nu);
          for (int x = i - surface.u.degree; x <= i; ++x)
            column[x] = ConstantJet(surface.poles[x][y]);
          row[y] = DeBoorJet(surface.u, i, Jet({ul, uh}, u.derivative), column);
        }
        UnionJet(total,
                 DeBoorJet(surface.v, j, Jet({vl, vh}, v.derivative), row));
        used = true;
      }
    }
    if (!used)
      throw Standard_Failure("uncovered surface jet span");
    result = CartesianJet(total, minWeight, maxWeight);
  } else {
    GeomAdaptor_Surface adaptor(surface.surface);
    gp_Ax3 axes;
    Jet x, y, z;
    switch (adaptor.GetType()) {
    case GeomAbs_Plane: {
      auto p = adaptor.Plane();
      axes = p.Position();
      x = u;
      y = v;
      break;
    }
    case GeomAbs_Cylinder: {
      auto p = adaptor.Cylinder();
      axes = p.Position();
      x = Jet(Interval(p.Radius())) * Cosine(u);
      y = Jet(Interval(p.Radius())) * Sine(u);
      z = v;
      break;
    }
    case GeomAbs_Cone: {
      auto p = adaptor.Cone();
      axes = p.Position();
      Jet radius =
          Jet(Interval(p.RefRadius())) + v * Sine(Jet(Interval(p.SemiAngle())));
      x = radius * Cosine(u);
      y = radius * Sine(u);
      z = v * Cosine(Jet(Interval(p.SemiAngle())));
      break;
    }
    case GeomAbs_Sphere: {
      auto p = adaptor.Sphere();
      axes = p.Position();
      x = Jet(Interval(p.Radius())) * Cosine(v) * Cosine(u);
      y = Jet(Interval(p.Radius())) * Cosine(v) * Sine(u);
      z = Jet(Interval(p.Radius())) * Sine(v);
      break;
    }
    case GeomAbs_Torus: {
      auto p = adaptor.Torus();
      axes = p.Position();
      Jet radius = Jet(Interval(p.MajorRadius())) +
                   Jet(Interval(p.MinorRadius())) * Cosine(v);
      x = radius * Cosine(u);
      y = radius * Sine(u);
      z = Jet(Interval(p.MinorRadius())) * Sine(v);
      break;
    }
    default:
      throw Standard_Failure("unsupported jet surface");
    }
    result = FrameJet(axes.Location(), axes.XDirection(), axes.YDirection(),
                      axes.Direction(), x, y, z);
  }
  return PlacedJet(result, surface.location);
}
template <class Curve, class Pcurve, class Surface>
inline Box CenteredResidual(const Curve &curve, const Pcurve &pcurve,
                            const Surface &surface, double first, double last) {
  const double center = first + (last - first) * .5;
  if (!std::isfinite(center) || center <= first || center >= last)
    throw Standard_Failure("no strictly interior representable center");
  const auto curveCenter = curve.Bounds(center, center),
             uvCenter = pcurve.Bounds(center, center),
             surfaceCenter = surface.Bounds(uvCenter[0], uvCenter[1]);
  const auto curveJet = CurveJet(curve, {first, last}),
             pcJet = CurveJet(pcurve, {first, last}),
             surfaceJet = SurfaceJet(surface, pcJet[0], pcJet[1]);
  const Interval offset = Interval(first, last) - Interval(center);
  Box residual;
  for (int i = 0; i < 3; ++i) {
    residual[i] = curveCenter[i] - surfaceCenter[i] +
                  offset * (curveJet[i].derivative - surfaceJet[i].derivative);
    if (!std::isfinite(residual[i].lo) || !std::isfinite(residual[i].hi) ||
        residual[i].lo > residual[i].hi)
      throw Standard_Failure("nonfinite centered enclosure");
  }
  return residual;
}
struct CorrelatedDiagnostics {
  int centeredAttempts = 0;
  int centeredSuccesses = 0;
  std::map<std::string, int> fallbackReasons;
  double elapsedWallMilliseconds = 0;
  double elapsedCpuMilliseconds = 0;
  bool processCpuAvailable = false;
  std::function<bool()> continueAssessment;
};
struct CorrelatedTimer {
  CorrelatedDiagnostics *diagnostics;
  std::chrono::steady_clock::time_point wallStart;
  std::clock_t cpuStart;
  explicit CorrelatedTimer(CorrelatedDiagnostics *target)
      : diagnostics(target), wallStart(std::chrono::steady_clock::now()),
        cpuStart(std::clock()) {}
  ~CorrelatedTimer() {
    if (!diagnostics)
      return;
    diagnostics->elapsedWallMilliseconds +=
        std::chrono::duration<double, std::milli>(
            std::chrono::steady_clock::now() - wallStart)
            .count();
    const auto cpuEnd = std::clock();
#ifndef __EMSCRIPTEN__
    if (cpuStart != std::clock_t(-1) && cpuEnd != std::clock_t(-1)) {
      diagnostics->processCpuAvailable = true;
      diagnostics->elapsedCpuMilliseconds +=
          1000.0 * (cpuEnd - cpuStart) / CLOCKS_PER_SEC;
    }
#else
    // Pinned Emscripten maps process CPU clocks to its monotonic wall clock.
    (void)cpuEnd;
#endif
  }
};
template <class Curve, class Pcurve, class Surface>
inline Residual BoundCorrelatedResidual(
    const Curve &curve, const Pcurve &pcurve, const Surface &surface,
    double first, double last, double budget, int *centeredSuccesses = nullptr,
    CorrelatedDiagnostics *diagnostics = nullptr, int intervalLimit = 32768) {
  Residual result;
  CorrelatedTimer timer(diagnostics);
  struct Part {
    double a, b;
    int depth;
  };
  std::vector<Part> todo(1, {first, last, 0});
  bool unresolved = false;
  const Box zero = {{{0, 0}, {0, 0}, {0, 0}}};
  try {
    if (!std::isfinite(budget) || budget <= 0 || !std::isfinite(first) ||
        !std::isfinite(last) || first >= last)
      throw Standard_Failure("invalid correlated policy or interval");
    while (!todo.empty()) {
      if (diagnostics && diagnostics->continueAssessment &&
          !diagnostics->continueAssessment())
        throw Standard_Failure("assessment-monotonic-time-limit");
      const auto part = todo.back();
      todo.pop_back();
      if (result.intervals >= std::min(32768, intervalLimit))
        throw Standard_Failure("correlated interval resource limit");
      ++result.intervals;
      Box curveBox, uv, surfaceBox;
      try {
        curveBox = curve.Bounds(part.a, part.b);
        uv = pcurve.Bounds(part.a, part.b);
        surfaceBox = surface.Bounds(uv[0], uv[1]);
      } catch (const Standard_Failure &failure) {
        const std::string reason =
            failure.GetMessageString() ? failure.GetMessageString() : "";
        if (reason != "represented surface denominator unavailable" &&
            reason != "represented surface cell resource limit" &&
            reason != "periodic local-domain resource limit")
          throw;
        if (diagnostics)
          ++diagnostics->fallbackReasons[reason];
        const double middle = part.a + (part.b - part.a) * .5;
        if (part.depth >= 32 || middle <= part.a || middle >= part.b) {
          unresolved = true;
          result.reason = reason;
          continue;
        }
        ++result.subdivisions;
        todo.push_back({part.a, middle, part.depth + 1});
        todo.push_back({middle, part.b, part.depth + 1});
        continue;
      }
      for (const auto &box : {curveBox, uv, surfaceBox})
        for (const auto &coordinate : box)
          if (!std::isfinite(coordinate.lo) || !std::isfinite(coordinate.hi) ||
              coordinate.lo > coordinate.hi)
            throw Standard_Failure("nonfinite correlated geometry");
      double upper = DistanceUpper(curveBox, surfaceBox),
             lower = DistanceLower(curveBox, surfaceBox);
      if (upper > budget && lower <= budget) {
        if (diagnostics)
          ++diagnostics->centeredAttempts;
        try {
          const auto centered =
              CenteredResidual(curve, pcurve, surface, part.a, part.b);
          upper = std::min(upper, DistanceUpper(centered, zero));
          lower = std::max(lower, DistanceLower(centered, zero));
          if (upper <= budget && centeredSuccesses)
            ++*centeredSuccesses;
          if (upper <= budget && diagnostics)
            ++diagnostics->centeredSuccesses;
        } catch (const Standard_Failure &failure) {
          // The independent whole-interval box remains valid.
          if (diagnostics)
            ++diagnostics->fallbackReasons[failure.GetMessageString()
                                               ? failure.GetMessageString()
                                               : "centered path unavailable"];
        }
      }
      result.lower = std::max(result.lower, lower);
      if (lower > budget) {
        result.status = "exceeds-budget";
        result.upper = std::max(result.upper, upper);
        return result;
      }
      if (upper <= budget) {
        result.upper = std::max(result.upper, upper);
        continue;
      }
      const double middle = part.a + (part.b - part.a) * .5;
      if (part.depth >= 32 || middle <= part.a || middle >= part.b) {
        unresolved = true;
        result.reason = "correlated resolution limit";
        continue;
      }
      ++result.subdivisions;
      todo.push_back({part.a, middle, part.depth + 1});
      todo.push_back({middle, part.b, part.depth + 1});
    }
    result.status = unresolved ? "unavailable" : "bounded-within-budget";
  } catch (const Standard_Failure &e) {
    result.reason = e.GetMessageString() ? e.GetMessageString()
                                         : "correlated comparison unavailable";
  }
  return result;
}
} // namespace MalievRepair
