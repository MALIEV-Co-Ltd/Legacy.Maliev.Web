#pragma once
// Same-transfer immutable diagnostic evidence. This header is shared by the
// pinned OCCT hooks and importer; it never changes transfer settings or shapes.
#include "kernel-repair-bounds.hpp"
#include "kernel-repair-correlated.hpp"
#include "kernel-repair-domains.hpp"
#include "kernel-repair-endpoints.hpp"
#include "kernel-repair-policy.hpp"
#include "kernel-repair-cone-region.hpp"
#include "kernel-repair-residual.hpp"
#include "kernel-native-interpretation-ledger.hpp"
#include "kernel-native-interpretation-checks.hpp"
#include "kernel-native-interpretation-audit.hpp"
#include "kernel-native-warning-lineage.hpp"
#include <BRepCheck_Analyzer.hxx>
#include <BRepTools_WireExplorer.hxx>
#include <BRep_Tool.hxx>
#include <Geom2d_Curve.hxx>
#include <GeomTools.hxx>
#include <Geom_Curve.hxx>
#include <Geom_Surface.hxx>
#include <Interface_InterfaceModel.hxx>
#include <Message_Msg.hxx>
#include <ShapeBuild_ReShape.hxx>
#include <ShapeProcess_ShapeContext.hxx>
#include <TCollection_AsciiString.hxx>
#include <TopExp.hxx>
#include <TopExp_Explorer.hxx>
#include <TopTools_IndexedMapOfShape.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Edge.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Iterator.hxx>
#include <TopoDS_Vertex.hxx>
#include <TopoDS_Wire.hxx>
#include <TransferBRep.hxx>
#include <Transfer_TransientProcess.hxx>
#include <cmath>
#include <cstdlib>
#include <iomanip>
#include <memory>
#include <sstream>
#include <string>
#include <vector>

namespace MalievRepair {
struct Pcurve {
  int edge = -1, orientation = 0;
  double first = 0, last = 0;
  bool degenerate = false;
  Handle(Geom2d_Curve) curve;
  TopoDS_Edge nativeEdge;
};
struct FaceLoop {
  TopoDS_Wire nativeWire;
  std::vector<std::pair<int, int>> uses;
  std::vector<TopoDS_Edge> edges;
  bool complete = true;
};
struct State {
  std::string geometry, placement, membership, pcurves;
  Handle(Geom_Curve) curve;
  Handle(Geom_Surface) surface;
  TopLoc_Location geometryLocation;
  std::vector<Pcurve> uses;
  double first = 0, last = 0;
  int flags = 0;
  std::vector<std::pair<int, int>> orderedUses;
  bool traversalComplete = true;
  std::vector<FaceLoop> faceLoops;
  int orientation = 0;
  double tolerance = 0;
  bool toleranceCaptured = false;
  bool valid = false;
  gp_Pnt vertexPoint;
  bool vertexPointCaptured = false;
};
struct Item {
  TopoDS_Shape original, mapped;
  State before, after;
  std::vector<int> entities;
  std::string association = "native-identity";
  double rangeDeviationUpper = 0;
  bool sourceGeometryUnchanged = false;
};
struct ResidualReference {
  int face = -1, edge = -1, orientation = 0;
  double first = 0, last = 0, sliverUpper = 0;
  double sourceFirst = 0, sourceLast = 0;
  bool sourceRangeCaptured = false;
  std::string path = "unavailable-correspondence";
  std::string metricMethod = "not-assessed";
  size_t obligation = std::numeric_limits<size_t>::max();
  bool evaluatorAttempted = false;
  std::vector<double> pathTerms;
  std::vector<std::string> pathTermKinds;
};
struct ConnectorProof {
  int face = -1, sourceVertex = -1;
  size_t postUse = 0, loop = 0, loopUse = 0, obligation = 0, metric = 0;
  TopoDS_Edge normalizedEdge;
  TopoDS_Wire nativeWire;
};
struct Boundary {
  int sourceEntity = 0;
  std::vector<Item> items;
  bool finalValid = false;
  bool finalCorrespondenceComplete = false;
  std::vector<Residual> residuals;
  std::vector<ResidualReference> residualReferences;
  std::vector<ConnectorProof> connectorProofs;
  CorrelatedDiagnostics correlatedDiagnostics;
  ResidualDispatchDiagnostics residualDispatch;
  int generatedPcurves = 0, changedPcurves = 0, unmappedUses = 0,
      changedSourceGeometry = 0, changedRanges = 0;
  double maximumTolerance = 0, rangeDeviationUpper = 0;
  int changedMembership = 0, verifiedDegenerateUses = 0;
  int totalIntervals = 0, maximumIntervals = 65536;
  std::vector<std::pair<int, ConeRegionResult>> coneRegions;
  MalievNativeInterpretation::RequiredLedger requiredMetrics;
  std::vector<std::vector<size_t>> sourceObligationIds;
  std::vector<std::vector<size_t>> postObligationIds;
  bool sourceObligationsAllocated = false;
  MalievNativeInterpretation::ToleranceLedger tolerances;
  std::vector<MalievNativeInterpretation::NativeCheckRow> nativeChecks;
  bool nativeAnalyzerExecuted = false;
  TopoDS_Shape finalShape;
};
inline void AllocateSourceObligations(Boundary &boundary) {
  if (boundary.sourceObligationsAllocated)
    return;
  boundary.sourceObligationsAllocated = true;
  boundary.sourceObligationIds.resize(boundary.items.size());
  for (size_t face = 0; face < boundary.items.size(); ++face) {
    const auto &item = boundary.items[face];
    if (item.original.ShapeType() != TopAbs_FACE)
      continue;
    for (size_t use = 0; use < item.before.uses.size(); ++use) {
      const std::string occurrence = "source-face-item:" + std::to_string(face) +
                                     "/coedge-occurrence:" + std::to_string(use);
      boundary.sourceObligationIds[face].push_back(
          boundary.requiredMetrics.Require("source-coedge", occurrence));
    }
  }
}
inline void AssociatePostObligations(Boundary &boundary) {
  boundary.postObligationIds.resize(boundary.items.size());
  for (size_t face = 0; face < boundary.items.size(); ++face) {
    const auto &item = boundary.items[face];
    if (item.original.ShapeType() != TopAbs_FACE)
      continue;
    std::vector<int> candidates(item.after.uses.size(), -1);
    std::vector<int> sourceConsumption(item.before.uses.size(), 0);
    for (size_t post = 0; post < item.after.uses.size(); ++post) {
      const auto &use = item.after.uses[post];
      int matches = 0;
      for (size_t prior = 0; prior < item.before.uses.size(); ++prior)
        if (use.edge >= 0 && item.before.uses[prior].edge == use.edge &&
            item.before.uses[prior].orientation == use.orientation) {
          candidates[post] = static_cast<int>(prior);
          ++matches;
        }
      if (matches == 1)
        ++sourceConsumption[candidates[post]];
      else
        candidates[post] = -1;
    }
    for (size_t post = 0; post < item.after.uses.size(); ++post) {
      const int prior = candidates[post];
      if (prior >= 0 && sourceConsumption[prior] == 1)
        boundary.postObligationIds[face].push_back(boundary.sourceObligationIds[face][prior]);
      else {
        const auto &use = item.after.uses[post];
        const std::string occurrence = "post-face-item:" + std::to_string(face) +
                                       "/coedge-occurrence:" + std::to_string(post);
        boundary.postObligationIds[face].push_back(boundary.requiredMetrics.Require(
            use.edge == -1 && use.degenerate ? "new-degenerate-connector" : "unmatched-post-coedge",
            occurrence));
      }
    }
  }
}
inline void FinalizeMetricObligations(Boundary &boundary, double budget) {
  for (size_t i = 0; i < boundary.residuals.size(); ++i) {
    const auto &reference = boundary.residualReferences.at(i);
    const auto &residual = boundary.residuals[i];
    boundary.requiredMetrics.Finish(reference.obligation, reference.evaluatorAttempted,
                                    residual.status, reference.pathTerms, budget, residual.reason);
    boundary.requiredMetrics.rows.at(reference.obligation).pathTermKinds =
        reference.pathTermKinds;
  }
}
struct Operation {
  int boundary = -1, wire = -1, status = 0;
  int face = -1;
  TopoDS_Shape nativeTarget, nativeFace;
  MalievNativeWarning::EmissionToken emission = 0;
  std::string branch, message, originalMessage;
  std::vector<int> entities;
};
struct Periodic {
  int surfaceEntity = 0, faceEntity = 0;
  std::string before, after;
  Residual comparison;
  std::array<double, 4> beforeDomain = {{0,0,0,0}}, afterDomain = {{0,0,0,0}};
  bool domainsCaptured = false, attempted = false;
  MalievNativeWarning::EmissionToken emission = 0;
};
struct SourceBound {
  int faceEntity = 0, boundEntity = 0, loopEntity = 0;
  int insertionParentOrientation = -1;
  bool outer = false, boundSense = false, sourceFaceSense = false,
       effectiveFaceSense = false;
  std::string loopKind;
  TopoDS_Shape face, wire;
};
struct EmittedFace {
  TopoDS_Face face;
  std::string bodyId, faceId, sourceOccurrenceId;
  bool uniqueSource = false, trimsComplete = false, adjacencyComplete = false;
};
struct EmittedUse {
  TopoDS_Face face;
  TopoDS_Wire wire;
  TopoDS_Edge edge;
  std::string faceId, wireId, coedgeId, edgeId, startVertexId, endVertexId;
  bool complete = false;
};
struct EmittedBody {
  TopoDS_Shape shape;
  std::string id, nativeEvidenceJson;
};
struct Evidence {
  MalievNativeAudit::Audit audit;
  bool active = false;
  int current = -1;
  std::vector<Boundary> boundaries;
  std::vector<Periodic> periodic;
  std::vector<Operation> operations;
  std::vector<SourceBound> sourceBounds;
  std::vector<EmittedFace> emittedFaces;
  std::vector<EmittedUse> emittedUses;
  std::vector<EmittedBody> emittedBodies;
  MalievNativeInterpretation::RequiredLedger periodicMetrics;
  int periodicIntervals = 0;
  int assessmentMilliseconds = 60000;
  double budget = 0;
  bool diagnostics = false;
  std::chrono::steady_clock::time_point deadline;
};
inline Evidence &EvidenceStore() {
  static Evidence e;
  return e;
}
inline void Configure(double budget, bool diagnostics,
                      int assessmentMilliseconds = 60000) {
  MalievNativeWarning::Reset(budget > 0 || diagnostics);
  EvidenceStore() = Evidence();
  EvidenceStore().budget = budget;
  EvidenceStore().diagnostics = diagnostics;
  EvidenceStore().assessmentMilliseconds =
      std::max(0, std::min(60000, assessmentMilliseconds));
  EvidenceStore().audit.origin = std::chrono::steady_clock::now();
  EvidenceStore().deadline =
      EvidenceStore().audit.origin +
      std::chrono::milliseconds(EvidenceStore().assessmentMilliseconds);
}
inline bool AssessmentContinues() {
  auto &e = EvidenceStore();
  const auto now = std::chrono::steady_clock::now();
  ++e.audit.counters["assessmentCallbacks"];
  const bool continues = now < e.deadline;
  if (!continues) {
    ++e.audit.counters["assessmentFalseCallbacks"];
    if (e.audit.firstCancellationSite.empty()) {
      e.audit.firstCancellationSite = e.audit.current ? e.audit.current->name : "assessment-entry";
      e.audit.firstCancellation.name = e.audit.firstCancellationSite;
      e.audit.firstCancellation.boundaryIndex = e.current;
      e.audit.firstCancellation.elapsedMs = MalievNativeAudit::Milliseconds(now - e.audit.origin);
      e.audit.firstCancellation.remainingMs = MalievNativeAudit::Milliseconds(e.deadline - now);
    }
  }
  return continues;
}
inline void Reset() {
  auto &e = EvidenceStore();
  e.active = e.budget > 0 || e.diagnostics;
}
inline void SourceBoundDeclaration(const TopoDS_Shape &face, int faceEntity,
                                   int boundEntity, int loopEntity, bool outer,
                                   bool boundSense, bool sourceSense,
                                   bool effectiveSense) {
  auto &e = EvidenceStore();
  if (!e.active)
    return;
  SourceBound b;
  b.face = face;
  b.faceEntity = faceEntity;
  b.boundEntity = boundEntity;
  b.loopEntity = loopEntity;
  b.outer = outer;
  b.boundSense = boundSense;
  b.sourceFaceSense = sourceSense;
  b.effectiveFaceSense = effectiveSense;
  e.sourceBounds.push_back(b);
}
inline void SourceBoundMapped(int faceEntity, int boundEntity,
                              const TopoDS_Shape &wire,
                              TopAbs_Orientation insertionParentOrientation,
                              const std::string &loopKind) {
  auto &e = EvidenceStore();
  if (!e.active)
    return;
  for (auto i = e.sourceBounds.rbegin(); i != e.sourceBounds.rend(); ++i)
    if (i->faceEntity == faceEntity && i->boundEntity == boundEntity) {
      i->wire = wire;
      i->insertionParentOrientation =
          static_cast<int>(insertionParentOrientation);
      i->loopKind = loopKind;
      return;
    }
}
inline std::string Placement(const TopLoc_Location &l) {
  std::ostringstream o;
  o << std::setprecision(17);
  for (int r = 1; r <= 3; ++r)
    for (int c = 1; c <= 4; ++c)
      o << l.Transformation().Value(r, c) << ' ';
  return o.str();
}
template <class T> inline std::string Geometry(const Handle(T) & g) {
  if (g.IsNull())
    return "null";
  std::ostringstream o;
  o << std::setprecision(17);
  GeomTools::Write(g, o);
  return o.str();
}
inline int Identity(const Boundary &b, const TopoDS_Shape &s, bool post) {
  auto &audit = EvidenceStore().audit;
  ++audit.identityCalls;
  const auto started = MalievNativeAudit::Clock::now();
  int found = -1;
  for (size_t i = 0; i < b.items.size(); ++i) {
    ++audit.identityCandidates;
    if (s.IsSame(post ? b.items[i].mapped : b.items[i].original)) {
      if (found != -1) { found = -2; break; }
      found = static_cast<int>(i);
    }
  }
  audit.identityMs += MalievNativeAudit::Milliseconds(MalievNativeAudit::Clock::now() - started);
  ++audit.counters[found == -1 ? "identityAbsent" : found == -2 ? "identityAmbiguous" : "identityFound"];
  return found;
}
inline void IntersectionOperation(const TopoDS_Shape &wire, int status,
                                  const char *branch,
                                  const Message_Msg &message,
                                  const TopoDS_Shape &face = TopoDS_Shape(),
                                  const Handle(ShapeBuild_ReShape) &context = Handle(ShapeBuild_ReShape)()) {
  auto &e = EvidenceStore();
  if (!e.active || e.current < 0)
    return;
  Operation o;
  o.boundary = e.current;
  o.wire = Identity(e.boundaries[e.current], wire, false);
  o.nativeTarget = wire;
  o.nativeFace = face;
  auto &boundary = e.boundaries[e.current];
  for (size_t index = 0; index < boundary.items.size(); ++index) {
    const auto &item = boundary.items[index];
    if (item.original.ShapeType() != TopAbs_FACE || face.IsNull()) continue;
    const auto mapped = context.IsNull() ? item.mapped : context->Apply(item.mapped);
    if (item.original.IsSame(face) || item.mapped.IsSame(face) || mapped.IsSame(face)) {
      if (o.face != -1) { o.face = -2; break; }
      o.face = static_cast<int>(index);
    }
  }
  if (o.wire < 0 && o.face >= 0 && wire.ShapeType() == TopAbs_WIRE) {
    int matches = 0;
    for (const auto &sourceLoop : boundary.items[o.face].before.faceLoops) {
      std::vector<TopoDS_Shape> actual;
      for (TopoDS_Iterator edge(wire); edge.More(); edge.Next()) actual.push_back(edge.Value());
      bool complete = !actual.empty() && actual.size() == sourceLoop.edges.size();
      std::vector<bool> consumed(actual.size(), false);
      for (const auto &sourceEdge : sourceLoop.edges) {
        const auto mapped = context.IsNull() ? TopoDS_Shape(sourceEdge) : context->Apply(sourceEdge);
        int edgeMatch = -1;
        for (size_t index = 0; index < actual.size(); ++index)
          if (!consumed[index] && (actual[index].IsEqual(sourceEdge) || actual[index].IsEqual(mapped))) {
            if (edgeMatch >= 0) { edgeMatch = -2; break; }
            edgeMatch = static_cast<int>(index);
          }
        if (edgeMatch < 0) complete = false;
        else consumed[edgeMatch] = true;
      }
      if (complete) { ++matches; o.wire = Identity(boundary, sourceLoop.nativeWire, false); }
    }
    if (matches != 1) o.wire = -1;
  }
  o.status = status;
  o.branch = branch;
  o.message = TCollection_AsciiString(message.Value()).ToCString();
  o.originalMessage = TCollection_AsciiString(message.Original()).ToCString();
  if (o.wire >= 0)
    o.entities = e.boundaries[e.current].items[o.wire].entities;
  o.emission = MalievNativeWarning::Tag(&message, "intersection", e.operations.size());
  e.operations.push_back(o);
}
inline State Capture(const Boundary &b, const TopoDS_Shape &s, bool post) {
  ++EvidenceStore().audit.captureCalls;
  ++EvidenceStore().audit.counters[post ? "capturePost" : "captureSource"];
  State v;
  if (s.IsNull()) {
    v.geometry = "null";
    return v;
  }
  v.orientation = static_cast<int>(s.Orientation());
  v.placement = Placement(s.Location());
  std::ostringstream g, m, p;
  g << std::setprecision(17);
  p << std::setprecision(17);
  g << static_cast<int>(s.ShapeType()) << ' ';
  for (TopoDS_Iterator i(s); i.More(); i.Next())
    m << Identity(b, i.Value(), post) << ':'
      << static_cast<int>(i.Value().Orientation()) << ' ';
  if (s.ShapeType() == TopAbs_WIRE) {
    int direct = 0;
    for (TopoDS_Iterator i(s); i.More(); i.Next())
      ++direct;
    for (BRepTools_WireExplorer i(TopoDS::Wire(s)); i.More(); i.Next())
      v.orderedUses.emplace_back(Identity(b, i.Current(), post),
                                 static_cast<int>(i.Current().Orientation()));
    v.traversalComplete = static_cast<int>(v.orderedUses.size()) == direct;
  }
  if (s.ShapeType() == TopAbs_VERTEX) {
    const auto vertex = TopoDS::Vertex(s);
    const auto point = BRep_Tool::Pnt(vertex);
    v.vertexPoint = point;
    v.vertexPointCaptured = true;
    g << point.X() << ' ' << point.Y() << ' ' << point.Z();
    v.tolerance = BRep_Tool::Tolerance(vertex);
    v.toleranceCaptured = true;
  } else if (s.ShapeType() == TopAbs_EDGE) {
    const auto edge = TopoDS::Edge(s);
    TopLoc_Location l;
    double first = 0, last = 0;
    const auto c = BRep_Tool::Curve(edge, l, first, last);
    g << Geometry(c) << ' ' << Placement(l);
    if (!c.IsNull())
      v.curve = Handle(Geom_Curve)::DownCast(c->Copy());
    v.geometryLocation = l;
    v.first = first;
    v.last = last;
    v.flags = (BRep_Tool::Degenerated(edge) ? 1 : 0) +
              (BRep_Tool::SameRange(edge) ? 2 : 0) +
              (BRep_Tool::SameParameter(edge) ? 4 : 0);
    v.tolerance = BRep_Tool::Tolerance(edge);
    v.toleranceCaptured = true;
  } else if (s.ShapeType() == TopAbs_FACE) {
    const auto face = TopoDS::Face(s);
    TopLoc_Location l;
    const auto surface = BRep_Tool::Surface(face, l);
    g << Geometry(surface) << ' ' << Placement(l);
    if (!surface.IsNull())
      v.surface = Handle(Geom_Surface)::DownCast(surface->Copy());
    v.geometryLocation = l;
    v.tolerance = BRep_Tool::Tolerance(face);
    v.toleranceCaptured = true;
    const auto forwardFace = TopoDS::Face(face.Oriented(TopAbs_FORWARD));
    for (TopoDS_Iterator wire(forwardFace); wire.More(); wire.Next())
      if (wire.Value().ShapeType() == TopAbs_WIRE) {
        FaceLoop loop;
        loop.nativeWire = TopoDS::Wire(wire.Value());
        std::vector<TopoDS_Edge> direct;
        bool hasUv = true;
        for (TopoDS_Iterator edge(wire.Value()); edge.More(); edge.Next()) {
          const auto occurrence = TopoDS::Edge(edge.Value());
          direct.push_back(occurrence);
          double first, last;
          hasUv = hasUv && !BRep_Tool::CurveOnSurface(occurrence, forwardFace,
                                                      first, last)
                                .IsNull();
        }
        std::vector<bool> visited(direct.size(), false);
        BRepTools_WireExplorer edge;
        if (hasUv)
          edge.Init(TopoDS::Wire(wire.Value()), forwardFace);
        else
          edge.Init(TopoDS::Wire(wire.Value()));
        for (; edge.More(); edge.Next()) {
          int match = -1;
          for (size_t index = 0; index < direct.size(); ++index)
            if (!visited[index] && direct[index].IsEqual(edge.Current())) {
              if (match >= 0)
                loop.complete = false;
              match = static_cast<int>(index);
            }
          if (match < 0)
            loop.complete = false;
          else
            visited[match] = true;
          loop.complete = loop.complete &&
                          (edge.Current().Orientation() == TopAbs_FORWARD ||
                           edge.Current().Orientation() == TopAbs_REVERSED);
          loop.uses.emplace_back(
              Identity(b, edge.Current(), post),
              static_cast<int>(edge.Current().Orientation()));
          loop.edges.push_back(edge.Current());
        }
        loop.complete = loop.complete && loop.uses.size() == direct.size() &&
                        !loop.edges.empty();
        for (size_t index = 0; index < loop.edges.size(); ++index) {
          const auto end = TopExp::LastVertex(loop.edges[index], true);
          const auto start = TopExp::FirstVertex(
              loop.edges[(index + 1) % loop.edges.size()], true);
          loop.complete = loop.complete && !end.IsNull() && !start.IsNull() &&
                          end.IsSame(start);
        }
        v.faceLoops.push_back(loop);
      }
    for (TopExp_Explorer e(face, TopAbs_EDGE); e.More(); e.Next()) {
      const auto edge = TopoDS::Edge(e.Current());
      double first = 0, last = 0;
      const auto c = BRep_Tool::CurveOnSurface(edge, face, first, last);
      Pcurve use;
      use.edge = Identity(b, edge, post);
      use.orientation = static_cast<int>(edge.Orientation());
      use.first = first;
      use.last = last;
      use.degenerate = BRep_Tool::Degenerated(edge);
      use.nativeEdge = edge;
      if (!c.IsNull())
        use.curve = Handle(Geom2d_Curve)::DownCast(c->Copy());
      v.uses.push_back(use);
      p << Identity(b, edge, post) << ':'
        << static_cast<int>(edge.Orientation()) << ' ' << Geometry(c) << ' '
        << first << ' ' << last << '\n';
    }
  }
  v.geometry = g.str();
  v.membership = m.str();
  v.pcurves = p.str();
  return v;
}
inline void Begin(const TopoDS_Shape &shape,
                  const Handle(Transfer_TransientProcess) & tp,
                  const Handle(Standard_Transient) & source) {
  auto &e = EvidenceStore();
  if (!e.active)
    return;
  e.boundaries.emplace_back();
  e.current = static_cast<int>(e.boundaries.size() - 1);
  e.audit.Checkpoint("begin", e.deadline, e.current);
  MalievNativeAudit::Scope beginScope(e.audit, "begin-capture");
  auto &b = e.boundaries.back();
  b.sourceEntity = tp->Model()->Number(source);
  TopTools_IndexedMapOfShape shapes;
  TopExp::MapShapes(shape, shapes);
  for (int i = 1; i <= shapes.Extent(); ++i) {
    Item item;
    item.original = item.mapped = shapes(i);
    b.items.push_back(item);
  }
  {
  MalievNativeAudit::Scope scope(e.audit, "begin-source-association");
  for (int i = 1; i <= tp->NbMapped(); ++i) {
    const auto sourceShape = TransferBRep::ShapeResult(tp, tp->Mapped(i));
    const int id = Identity(b, sourceShape, false);
    if (id >= 0)
      b.items[id].entities.push_back(tp->Model()->Number(tp->Mapped(i)));
  }
  }
  {
  MalievNativeAudit::Scope scope(e.audit, "before-capture");
  for (auto &item : b.items)
    item.before = Capture(b, item.original, false);
  }
  AllocateSourceObligations(b);
  e.audit.Checkpoint("begin-end", e.deadline, e.current);
}
inline void Replacements(const Handle(ShapeBuild_ReShape) & reshaper) {
  auto &e = EvidenceStore();
  if (!e.active || e.current < 0 || reshaper.IsNull())
    return;
  for (auto &item : e.boundaries[e.current].items) {
    const auto mapped = reshaper->Apply(item.mapped);
    if (!mapped.IsSame(item.mapped))
      item.association = "native-reshape-replacement";
    item.mapped = mapped;
  }
}
inline bool SameGeometry(const std::string &a, const std::string &b) {
  std::istringstream x(a), y(b);
  std::string p, q;
  while (x >> p) {
    if (!(y >> q))
      return false;
    if (p == q)
      continue;
    char *px = nullptr, *qy = nullptr;
    const double u = std::strtod(p.c_str(), &px),
                 v = std::strtod(q.c_str(), &qy);
    if (!px || *px || !qy || *qy || !std::isfinite(u) || !std::isfinite(v) ||
        u != v)
      return false;
  }
  return !(y >> q);
}
inline std::vector<std::string> MemberSet(const std::string &s) {
  std::istringstream input(s);
  std::vector<std::string> v;
  std::string token;
  while (input >> token)
    v.push_back(token);
  std::sort(v.begin(), v.end());
  return v;
}
inline bool SameCycle(const std::vector<std::pair<int, int>> &a,
                      const std::vector<std::pair<int, int>> &b) {
  if (a.size() != b.size())
    return false;
  if (a.empty())
    return true;
  for (size_t start = 0; start < b.size(); ++start) {
    bool equal = true;
    for (size_t i = 0; i < a.size(); ++i)
      if (a[i] != b[(i + start) % b.size()]) {
        equal = false;
        break;
      }
    if (equal)
      return true;
  }
  return false;
}
inline bool ConnectorProofValid(const Boundary &boundary,
                                 const ConnectorProof &proof, double budget,
                                 bool requireNativeChecks) {
  using namespace MalievNativeInterpretation;
  try {
    if (!std::isfinite(budget) || budget <= 0 || proof.face < 0 ||
        proof.sourceVertex < 0 || proof.face >= (int)boundary.items.size() ||
        proof.sourceVertex >= (int)boundary.items.size() ||
        proof.obligation >= boundary.requiredMetrics.rows.size() ||
        proof.metric >= boundary.residuals.size() ||
        proof.metric >= boundary.residualReferences.size()) return false;
    const auto &face = boundary.items[proof.face];
    const auto &vertex = boundary.items[proof.sourceVertex];
    if (!boundary.finalCorrespondenceComplete || !face.sourceGeometryUnchanged ||
        face.mapped.IsNull() || face.mapped.ShapeType() != TopAbs_FACE ||
        vertex.mapped.IsNull() || vertex.mapped.ShapeType() != TopAbs_VERTEX ||
        proof.postUse >= face.after.uses.size() ||
        proof.loop >= face.after.faceLoops.size()) return false;
    const auto &post = face.after.uses[proof.postUse];
    const auto &loop = face.after.faceLoops[proof.loop];
    if (post.edge != -1 || !post.degenerate || post.nativeEdge.IsNull() ||
        !BRep_Tool::Degenerated(post.nativeEdge) ||
        Identity(boundary, post.nativeEdge, true) != -1 ||
        !loop.complete || loop.uses.size() != loop.edges.size() ||
        proof.loopUse >= loop.uses.size() ||
        loop.uses[proof.loopUse].first != -1 ||
        !loop.nativeWire.IsEqual(proof.nativeWire) ||
        !loop.edges[proof.loopUse].IsEqual(proof.normalizedEdge)) return false;
    auto normalized = post.nativeEdge;
    if (face.after.orientation == TopAbs_REVERSED) normalized.Reverse();
    if (!normalized.IsEqual(proof.normalizedEdge)) return false;
    Standard_Real nativeFirst = 0, nativeLast = 0;
    const auto nativePC = BRep_Tool::CurveOnSurface(
        post.nativeEdge, TopoDS::Face(face.mapped), nativeFirst, nativeLast);
    if (nativePC.IsNull() || post.curve.IsNull() || nativeFirst != post.first ||
        nativeLast != post.last || !SameGeometry(Geometry(nativePC), Geometry(post.curve)))
      return false;
    int proofMatches = 0, occurrenceMatches = 0, referenceMatches = 0;
    for (const auto &candidate : boundary.connectorProofs)
      if ((candidate.face == proof.face && candidate.postUse == proof.postUse) ||
          (candidate.face == proof.face && candidate.loop == proof.loop &&
           candidate.loopUse == proof.loopUse) ||
          candidate.obligation == proof.obligation) ++proofMatches;
    for (const auto &candidateLoop : face.after.faceLoops)
      for (const auto &edge : candidateLoop.edges)
        if (edge.IsEqual(proof.normalizedEdge)) ++occurrenceMatches;
    for (const auto &reference : boundary.residualReferences)
      if (reference.obligation == proof.obligation) ++referenceMatches;
    if (proofMatches != 1 || occurrenceMatches != 1 || referenceMatches != 1)
      return false;
    // Check retained occurrence against current native traversal, not a pointer
    // or an aggregate number of verified degenerate edges.
    const auto forwardFace = TopoDS::Face(face.mapped.Oriented(TopAbs_FORWARD));
    size_t traversed = 0;
    for (BRepTools_WireExplorer it(loop.nativeWire, forwardFace); it.More(); it.Next()) {
      if (traversed >= loop.edges.size() ||
          !it.Current().IsEqual(loop.edges[traversed++])) return false;
    }
    if (traversed != loop.edges.size()) return false;
    TopoDS_Vertex first, last;
    TopExp::Vertices(post.nativeEdge, first, last);
    if (first.IsNull() || last.IsNull() || !first.IsSame(last) ||
        Identity(boundary, first, true) != proof.sourceVertex ||
        !vertex.before.vertexPointCaptured || !vertex.after.vertexPointCaptured ||
        !SameGeometry(vertex.before.geometry, vertex.after.geometry) ||
        !SameGeometry(vertex.before.placement, vertex.after.placement)) return false;
    const auto point = BRep_Tool::Pnt(first);
    for (int coordinate = 1; coordinate <= 3; ++coordinate)
      if (point.Coord(coordinate) != vertex.before.vertexPoint.Coord(coordinate) ||
          point.Coord(coordinate) != vertex.after.vertexPoint.Coord(coordinate))
        return false;
    const auto &reference = boundary.residualReferences[proof.metric];
    const auto &metric = boundary.residuals[proof.metric];
    const auto &row = boundary.requiredMetrics.rows[proof.obligation];
    if (reference.face != proof.face || reference.edge != -1 ||
        reference.obligation != proof.obligation || !reference.evaluatorAttempted ||
        reference.first != post.first || reference.last != post.last ||
        reference.orientation != post.orientation ||
        reference.path != "whole-native-degenerate-lift-to-preserved-source-vertex" ||
        reference.metricMethod != "outward-degenerate-point-image" ||
        reference.pathTerms != std::vector<double>{metric.upper} ||
        reference.pathTermKinds != std::vector<std::string>{"degenerate-point-image-residual"} ||
        metric.status != "bounded-within-budget" || !std::isfinite(metric.upper) ||
        metric.upper < 0 || metric.upper > budget ||
        row.kind != "new-degenerate-connector" ||
        row.occurrence != "post-face-item:" + std::to_string(proof.face) +
                              "/coedge-occurrence:" + std::to_string(proof.postUse) ||
        proof.face >= (int)boundary.postObligationIds.size() ||
        proof.postUse >= boundary.postObligationIds[proof.face].size() ||
        boundary.postObligationIds[proof.face][proof.postUse] != proof.obligation)
      return false;
    if (requireNativeChecks) {
      if (!boundary.nativeAnalyzerExecuted || !boundary.finalValid ||
          !row.finalized || row.state != ObligationState::Bounded ||
          row.pathTerms != reference.pathTerms || row.pathTermKinds != reference.pathTermKinds ||
          row.upper != metric.upper) return false;
      const auto required = AllocateFaceChecks(forwardFace);
      size_t observed = 0;
      for (const auto &candidate : boundary.nativeChecks)
        if (candidate.face.IsSame(forwardFace)) ++observed;
      if (observed != required.size()) return false;
      for (const auto &expected : required) {
        int matches = 0;
        for (const auto &candidate : boundary.nativeChecks)
          if (candidate.face.IsSame(forwardFace) && candidate.method == expected.method &&
              candidate.subject.IsEqual(expected.subject)) {
            if (candidate.state != NativeCheckState::Passed) return false;
            ++matches;
          }
        if (matches != 1) return false;
      }
    }
    return true;
  } catch (const Standard_Failure &) { return false; }
}
inline const ConnectorProof *ConnectorForLoopUse(
    const Boundary &boundary, const Item &face, const FaceLoop &loop,
    size_t occurrence, double budget, bool requireNativeChecks) {
  const ConnectorProof *found = nullptr;
  for (const auto &proof : boundary.connectorProofs)
    if (proof.face >= 0 && proof.face < (int)boundary.items.size() &&
        &boundary.items[proof.face] == &face && proof.loopUse == occurrence &&
        proof.loop < face.after.faceLoops.size() &&
        &face.after.faceLoops[proof.loop] == &loop &&
        ConnectorProofValid(boundary, proof, budget, requireNativeChecks)) {
      if (found) return nullptr;
      found = &proof;
    }
  return found;
}
struct SourceBoundCycleCheck {
  bool supportedContext = false;
  bool preAddOrientationMatches = false;
  bool beforeOccurrenceUnique = false;
  bool beforeOrientationMatches = false;
  bool composedCyclePreserved = false;
  TopAbs_Orientation expectedPreAdd = TopAbs_EXTERNAL;
  TopAbs_Orientation expectedNormalized = TopAbs_EXTERNAL;
  TopAbs_Orientation beforeNormalized = TopAbs_EXTERNAL;
  bool Positive() const {
    return supportedContext && preAddOrientationMatches &&
           beforeOccurrenceUnique && beforeOrientationMatches &&
           composedCyclePreserved;
  }
};
inline SourceBoundCycleCheck
CheckSourceBoundCycle(const Boundary &boundary, const Item &face,
                      const Item &wire, const SourceBound &source,
                      const FaceLoop &finalLoop) {
  SourceBoundCycleCheck result;
  const auto parent =
      static_cast<TopAbs_Orientation>(source.insertionParentOrientation);
  const bool ordinaryParent =
      parent == TopAbs_FORWARD || parent == TopAbs_REVERSED;
  if (source.loopKind == "StepShape_EdgeLoop")
    result.expectedPreAdd = source.boundSense == source.effectiveFaceSense
                                ? TopAbs_FORWARD
                                : TopAbs_REVERSED;
  else if (source.loopKind == "StepShape_PolyLoop")
    result.expectedPreAdd = source.boundSense ? TopAbs_FORWARD
                                              : TopAbs_REVERSED;
  result.supportedContext =
      ordinaryParent && result.expectedPreAdd != TopAbs_EXTERNAL;
  if (result.supportedContext)
    result.expectedNormalized =
        TopAbs::Compose(parent, result.expectedPreAdd);
  result.preAddOrientationMatches =
      result.supportedContext && !source.wire.IsNull() &&
      source.wire.Orientation() == result.expectedPreAdd;
  int beforeMatches = 0;
  std::vector<std::pair<int, int>> beforeUses;
  bool beforeComplete = false;
  for (const auto &beforeLoop : face.before.faceLoops)
    if (beforeLoop.nativeWire.IsSame(wire.original)) {
      ++beforeMatches;
      result.beforeNormalized = beforeLoop.nativeWire.Orientation();
      beforeUses = beforeLoop.uses;
      beforeComplete = beforeLoop.complete;
    }
  result.beforeOccurrenceUnique = beforeMatches == 1;
  result.beforeOrientationMatches = result.beforeOccurrenceUnique &&
                                    result.beforeNormalized ==
                                        result.expectedNormalized;
  std::vector<std::pair<int, int>> finalSourceUses;
  bool finalAssociationsComplete = !finalLoop.uses.empty();
  for (size_t occurrence = 0; occurrence < finalLoop.uses.size(); ++occurrence) {
    const auto &use = finalLoop.uses[occurrence];
    if (use.first == -1 && ConnectorForLoopUse(boundary, face, finalLoop,
                                               occurrence, EvidenceStore().budget, true))
      continue;
    if (use.first < 0 ||
        static_cast<size_t>(use.first) >= boundary.items.size()) {
      finalAssociationsComplete = false;
      continue;
    }
    finalSourceUses.emplace_back(use.first, use.second);
  }
  result.composedCyclePreserved =
      finalLoop.complete && beforeComplete && finalAssociationsComplete &&
      result.beforeOccurrenceUnique && SameCycle(beforeUses, finalSourceUses);
  return result;
}
// Exact clamped-boundary restriction. Positive rational weights make the
// whole boundary curve lie in its native row's convex hull. Neither a nearly
// constant PC nor any exterior parameter extension is covered by this route.
inline bool ClampedBoundaryImage(const Pcurve &use, const State &face,
                                 double first, double last, Box &image,
                                 const std::function<void()> &charge) {
  const auto native = Handle(Geom_BSplineSurface)::DownCast(face.surface);
  const auto line = Handle(Geom2d_Line)::DownCast(use.curve);
  if (native.IsNull() || line.IsNull() || native->IsUPeriodic() ||
      native->IsVPeriodic() || !std::isfinite(first) || !std::isfinite(last) ||
      first >= last)
    return false;
  const auto origin = line->Lin2d().Location();
  const auto direction = line->Lin2d().Direction();
  if (!std::isfinite(origin.X()) || !std::isfinite(origin.Y()) ||
      !std::isfinite(direction.X()) || !std::isfinite(direction.Y()))
    return false;
  const bool fixedU = direction.X() == 0;
  if (!fixedU && direction.Y() != 0)
    return false;
  const double fixed = fixedU ? origin.X() : origin.Y();
  const int knotCount = fixedU ? native->NbUKnots() : native->NbVKnots();
  const int degree = fixedU ? native->UDegree() : native->VDegree();
  const double low = fixedU ? native->UKnot(1) : native->VKnot(1);
  const double high = fixedU ? native->UKnot(knotCount) : native->VKnot(knotCount);
  const int endpoint = fixed == low ? 1 : fixed == high ? knotCount : 0;
  if (!endpoint || (fixedU ? native->UMultiplicity(endpoint)
                          : native->VMultiplicity(endpoint)) != degree + 1)
    return false;
  const double offset = fixedU ? origin.Y() : origin.X();
  const double slope = fixedU ? direction.Y() : direction.X();
  if (slope != 1 && slope != -1)
    return false;
  auto parameter = [&](double t) {
    // These are exact real arithmetic identities on the native binary values;
    // all other additions use outward intervals, never epsilon clipping.
    if (t == 0) return Interval(offset);
    if (offset == 0) return Interval(slope == 1 ? t : -t);
    if (slope == -1 && offset == t) return Interval(0);
    return Interval(offset) + Interval(slope) * Interval(t);
  };
  const auto a = parameter(first), b = parameter(last);
  double uFirst, uLast, vFirst, vLast;
  native->Bounds(uFirst, uLast, vFirst, vLast);
  if (!std::isfinite(uFirst) || !std::isfinite(uLast) ||
      !std::isfinite(vFirst) || !std::isfinite(vLast) ||
      uFirst >= uLast || vFirst >= vLast)
    return false;
  const double varyingLow = fixedU ? vFirst : uFirst;
  const double varyingHigh = fixedU ? vLast : uLast;
  if (std::min(a.lo, b.lo) < varyingLow || std::max(a.hi, b.hi) > varyingHigh)
    return false;
  const int fixedPole = endpoint == 1 ? 1 : fixedU ? native->NbUPoles() : native->NbVPoles();
  const int count = fixedU ? native->NbVPoles() : native->NbUPoles();
  Box hull = EmptyBox();
  for (int index = 1; index <= count; ++index) {
    charge();
    const int u = fixedU ? fixedPole : index, v = fixedU ? index : fixedPole;
    const double weight = native->Weight(u, v);
    const auto pole = native->Pole(u, v);
    if (!std::isfinite(weight) || weight <= 0 || !std::isfinite(pole.X()) ||
        !std::isfinite(pole.Y()) || !std::isfinite(pole.Z()))
      return false;
    Union(hull, Box{Interval(pole.X()), Interval(pole.Y()), Interval(pole.Z())});
  }
  image = Placed(hull, face.geometryLocation);
  for (const auto &coordinate : image)
    if (!std::isfinite(coordinate.lo) || !std::isfinite(coordinate.hi) ||
        coordinate.lo > coordinate.hi)
      return false;
  return true;
}
inline Residual BoundDegenerate(const Pcurve &use, const State &face,
                                const gp_Pnt &point, double budget,
                                int intervalLimit = 32768) {
  Residual out;
  try {
    if (!std::isfinite(budget) || budget <= 0 || !std::isfinite(use.first) ||
        !std::isfinite(use.last) || use.first >= use.last ||
        !std::isfinite(point.X()) || !std::isfinite(point.Y()) ||
        !std::isfinite(point.Z()))
      throw Standard_Failure("invalid degenerate policy or domain");
    const int maximumIntervals = std::min(32768, std::max(0, intervalLimit));
    if (maximumIntervals == 0)
      throw Standard_Failure("degenerate image resource limit");
    RepresentedCurveBound pc(use.curve);
    RepresentedSurfaceBound surface(face.surface, face.geometryLocation);
    surface.continueAssessment = AssessmentContinues;
    Box vertex = {{{point.X(), point.X()},
                   {point.Y(), point.Y()},
                   {point.Z(), point.Z()}}};
    struct Part {
      double a, b;
      int depth;
    };
    std::vector<Part> todo(1, {use.first, use.last, 0});
    auto charge = [&]() {
      if (!AssessmentContinues())
        throw Standard_Failure("assessment-monotonic-time-limit");
      if (out.intervals >= maximumIntervals)
        throw Standard_Failure("degenerate image resource limit");
      ++out.intervals;
    };
    while (!todo.empty()) {
      if (!AssessmentContinues())
        throw Standard_Failure("assessment-monotonic-time-limit");
      auto q = todo.back();
      todo.pop_back();
      if (out.intervals >= maximumIntervals)
        throw Standard_Failure("degenerate image resource limit");
      ++out.intervals;
      Box s;
      bool enclosureUnavailable = false;
      try {
        if (!ClampedBoundaryImage(use, face, q.a, q.b, s, charge)) {
          const auto uv = pc.Bounds(q.a, q.b);
          s = surface.Bounds(uv[0], uv[1]);
        }
      } catch (const Standard_Failure &failure) {
        const std::string reason = failure.GetMessageString()
                                       ? failure.GetMessageString() : "";
        if (reason != "represented surface denominator unavailable" &&
            reason != "represented surface cell resource limit" &&
            reason != "periodic local-domain resource limit")
          throw;
        enclosureUnavailable = true;
      }
      if (!enclosureUnavailable) {
        const double upper = DistanceUpper(vertex, s);
        out.lower = std::max(out.lower, DistanceLower(vertex, s));
        if (!std::isfinite(upper))
          throw Standard_Failure("nonfinite degenerate image");
        if (out.lower > budget) {
          out.status = "exceeds-budget";
          out.upper = std::max(out.upper, upper);
          return out;
        }
        if (upper <= budget) {
          out.upper = std::max(out.upper, upper);
          continue;
        }
      }
      const double middle = q.a + (q.b - q.a) * .5;
      if (q.depth >= 32 || middle <= q.a || middle >= q.b)
        throw Standard_Failure("degenerate image resolution limit");
      ++out.subdivisions;
      todo.push_back({q.a, middle, q.depth + 1});
      todo.push_back({middle, q.b, q.depth + 1});
    }
    out.status = "bounded-within-budget";
  } catch (const Standard_Failure &e) {
    out.reason = e.GetMessageString() ? e.GetMessageString()
                                      : "degenerate image unavailable";
  }
  return out;
}
// Resolve every source reference term before any dependent face is assessed.
// Item order is transfer enumeration, not a dependency ordering.
inline void PrepareReferenceMetadata(Boundary &b) {
  b.maximumTolerance = 0;
  b.changedSourceGeometry = 0;
  b.changedRanges = 0;
  b.rangeDeviationUpper = 0;
  for (auto &item : b.items) {
    item.rangeDeviationUpper = 0;
    b.maximumTolerance = std::max(b.maximumTolerance, item.after.tolerance);
    item.sourceGeometryUnchanged =
        SameGeometry(item.before.geometry, item.after.geometry) &&
        SameGeometry(item.before.placement, item.after.placement);
    if (!item.sourceGeometryUnchanged)
      ++b.changedSourceGeometry;
    if (item.original.ShapeType() != TopAbs_EDGE ||
        (item.before.first == item.after.first &&
         item.before.last == item.after.last))
      continue;
    ++b.changedRanges;
    try {
      CurveBound curve(item.before.curve, item.before.geometryLocation);
      if (std::max(item.before.first, item.after.first) >
          std::min(item.before.last, item.after.last))
        throw Standard_Failure("no retained range overlap");
      for (const auto range :
           {std::make_pair(item.before.first, item.after.first),
            std::make_pair(item.before.last, item.after.last)})
        if (range.first != range.second) {
          const auto box = EndpointExtendedCurveBox(
              curve, std::min(range.first, range.second),
              std::max(range.first, range.second));
          item.rangeDeviationUpper =
              std::max(item.rangeDeviationUpper, DistanceUpper(box, box));
        }
    } catch (const Standard_Failure &) {
      item.rangeDeviationUpper = std::numeric_limits<double>::infinity();
    }
    b.rangeDeviationUpper =
        std::max(b.rangeDeviationUpper, item.rangeDeviationUpper);
  }
}
inline bool ConeDeclarationMatches(const Boundary &b, const Item &face,
                                   const SourceBound &bound) {
  if (!bound.outer || bound.wire.IsNull() || !bound.face.IsSame(face.original) ||
      face.before.faceLoops.size() != 1 || face.after.faceLoops.size() != 1 ||
      bound.sourceFaceSense != bound.effectiveFaceSense)
    return false; // Native cone conversion has no torus radius-sense inversion.
  const auto expectedWireOrientation = bound.boundSense == bound.effectiveFaceSense
      ? TopAbs_FORWARD : TopAbs_REVERSED;
  if (bound.wire.Orientation() != expectedWireOrientation ||
      !face.before.faceLoops[0].nativeWire.IsEqual(bound.wire))
    return false;
  const int wire = Identity(b, bound.wire, false);
  return wire >= 0 && face.after.faceLoops[0].nativeWire.IsSame(b.items[wire].mapped);
}
inline ConeRegionResult AssessSourceConeRegion(Boundary &b, const Item &face) {
  ConeRegionResult unavailable;
  try {
    ConeRegionInput input;
    input.cone = GeomAdaptor_Surface(face.before.surface).Cone();
    input.location = face.before.geometryLocation;
    input.unchangedSupportAndOrientation = face.sourceGeometryUnchanged &&
        face.before.orientation == face.after.orientation;
    if (face.before.faceLoops.size() != 1 || face.after.faceLoops.size() != 1 ||
        !face.before.faceLoops[0].complete || !face.after.faceLoops[0].complete)
      throw Standard_Failure("source cone does not have one complete loop");
    int declarations = 0;
    for (const auto &bound : EvidenceStore().sourceBounds)
      if (bound.face.IsSame(face.original)) {
        ++declarations;
        input.mappedSourceOuter = ConeDeclarationMatches(b, face, bound);
      }
    input.mappedSourceOuter = input.mappedSourceOuter && declarations == 1;
    std::vector<std::pair<int, int>> retained;
    const auto forwardFace = TopoDS::Face(face.mapped.Oriented(TopAbs_FORWARD));
    const auto &loop = face.after.faceLoops[0];
    for (size_t index = 0; index < loop.edges.size(); ++index) {
      const auto &edge = loop.edges[index];
      ConeRegionUse use;
      use.edge = loop.uses[index].first;
      use.orientation = loop.uses[index].second;
      use.startVertex = Identity(b, TopExp::FirstVertex(edge, true), true);
      use.endVertex = Identity(b, TopExp::LastVertex(edge, true), true);
      if (use.startVertex < 0 || use.endVertex < 0 ||
          !b.items[use.startVertex].sourceGeometryUnchanged ||
          !b.items[use.endVertex].sourceGeometryUnchanged)
        throw Standard_Failure("cone endpoint source correspondence unavailable");
      use.startPoint = b.items[use.startVertex].before.vertexPoint;
      use.endPoint = b.items[use.endVertex].before.vertexPoint;
      use.sourceVerticesCaptured = b.items[use.startVertex].before.vertexPointCaptured &&
          b.items[use.endVertex].before.vertexPointCaptured;
      const auto pcurve = BRep_Tool::CurveOnSurface(edge, forwardFace, use.first, use.last);
      if (!pcurve.IsNull())
        use.pcurve = Handle(Geom2d_Curve)::DownCast(pcurve->Copy());
      use.degenerate = BRep_Tool::Degenerated(edge);
      if (use.edge >= 0) {
        retained.push_back(loop.uses[index]);
        const auto &source = b.items[use.edge];
        if (!source.sourceGeometryUnchanged ||
            MemberSet(source.before.membership) != MemberSet(source.after.membership) ||
            source.before.first != use.first || source.before.last != use.last)
          throw Standard_Failure("cone source curve or full range changed");
        use.source = source.before.curve;
        use.sourceLocation = source.before.geometryLocation;
      } else if (!use.degenerate) {
        throw Standard_Failure("unmatched nondegenerate cone use");
      }
      input.uses.push_back(use);
    }
    input.sourceCyclePreserved = SameCycle(face.before.faceLoops[0].uses, retained);
    return BoundSourceConeCap(input, EvidenceStore().budget);
  } catch (const Standard_Failure &failure) {
    unavailable.reason = failure.GetMessageString();
  }
  return unavailable;
}
inline void ResolveFinalCorrespondence(Boundary &b, const TopoDS_Shape &result) {
  MalievNativeAudit::Scope scope(EvidenceStore().audit, "final-correspondence");
  b.finalCorrespondenceComplete = !result.IsNull();
  for (auto &item : b.items)
    if (item.original.ShapeType() >= TopAbs_SOLID &&
        item.original.ShapeType() <= TopAbs_FACE) {
      TopoDS_Shape actual;
      int matches = 0;
      if (result.IsSame(item.mapped)) {
        actual = result;
        ++matches;
      } else
        for (TopExp_Explorer shape(result, item.original.ShapeType());
             shape.More(); shape.Next())
          if (shape.Current().IsSame(item.mapped)) {
            actual = shape.Current();
            ++matches;
          }
      if (matches == 1)
        item.mapped = actual;
      else {
        item.mapped.Nullify();
        item.association = matches == 0 ? "missing-final-shape" : "ambiguous-final-shape";
        b.finalCorrespondenceComplete = false;
      }
    }
  // Reconstructed wires are not necessarily keys in ShapeProcess::Map.
  // Associate them through exact replaced native edge ownership, never ordinal
  // or geometry scores. New degenerate uses remain explicitly unmatched.
  for (auto &item : b.items)
    if (item.original.ShapeType() == TopAbs_WIRE) {
      const Item *owner = nullptr;
      int ownerCount = 0;
      for (const auto &face : b.items)
        if (face.original.ShapeType() == TopAbs_FACE)
          for (const auto &loop : face.before.faceLoops)
            if (loop.nativeWire.IsSame(item.original)) {
              owner = &face;
              ++ownerCount;
            }
      if (ownerCount != 1 || owner->mapped.IsNull()) {
        item.mapped.Nullify();
        item.association = "unavailable-source-wire-parent";
        b.finalCorrespondenceComplete = false;
        continue;
      }
      // Face-relative loops use a FORWARD parent. Do not compare raw source
      // edge orientation: native repair may legitimately reverse wire storage.
      const auto parent = owner->mapped.Oriented(TopAbs_FORWARD);
      int globalIdentityCount = 0, parentIdentityCount = 0;
      TopoDS_Shape identityCandidate;
      for (TopExp_Explorer wire(result, TopAbs_WIRE); wire.More(); wire.Next())
        globalIdentityCount += wire.Current().IsSame(item.mapped);
      for (TopoDS_Iterator wire(parent); wire.More(); wire.Next())
        if (wire.Value().ShapeType() == TopAbs_WIRE &&
            wire.Value().IsSame(item.mapped)) {
          identityCandidate = wire.Value();
          ++parentIdentityCount;
        }
      if (globalIdentityCount > 0) {
        if (globalIdentityCount == 1 && parentIdentityCount == 1) {
          item.mapped = identityCandidate;
          continue; // Preserve exact native identity/map provenance.
        }
        item.mapped.Nullify();
        item.association = "ambiguous-or-wrong-parent-final-wire";
        b.finalCorrespondenceComplete = false;
        continue;
      }
      std::vector<int> expected;
      for (const auto &use : item.before.orderedUses)
        expected.push_back(use.first);
      std::sort(expected.begin(), expected.end());
      TopoDS_Shape candidate;
      int matches = 0;
      for (TopoDS_Iterator wire(parent); wire.More(); wire.Next()) {
        if (wire.Value().ShapeType() != TopAbs_WIRE)
          continue;
        std::vector<int> actual;
        bool unsupported = false;
        for (TopoDS_Iterator edge(wire.Value()); edge.More(); edge.Next()) {
          const int id = Identity(b, edge.Value(), true);
          if (id < 0) {
            if (edge.Value().ShapeType() != TopAbs_EDGE ||
                !BRep_Tool::Degenerated(TopoDS::Edge(edge.Value())))
              unsupported = true;
          } else
            actual.push_back(id);
        }
        std::sort(actual.begin(), actual.end());
        if (item.before.traversalComplete && !unsupported && actual == expected) {
          candidate = wire.Value();
          ++matches;
        }
      }
      if (matches == 1) {
        int finalOccurrences = 0;
        for (TopExp_Explorer wire(result, TopAbs_WIRE); wire.More(); wire.Next())
          finalOccurrences += wire.Current().IsSame(candidate);
        if (finalOccurrences != 1)
          matches = finalOccurrences;
      }
      if (matches == 1) {
        item.mapped = candidate;
        item.association = "unique-native-replaced-edge-membership";
      } else {
        item.mapped.Nullify();
        item.association = matches == 0 ? "missing-final-wire" : "ambiguous-final-wire";
        b.finalCorrespondenceComplete = false;
      }
    }
  TopTools_IndexedMapOfShape finalFaces;
  TopExp::MapShapes(result, TopAbs_FACE, finalFaces);
  for (int index = 1; index <= finalFaces.Extent(); ++index) {
    int matches = 0;
    for (const auto &item : b.items)
      if (item.original.ShapeType() == TopAbs_FACE &&
          !item.mapped.IsNull() && item.mapped.IsSame(finalFaces(index)))
        ++matches;
    b.finalCorrespondenceComplete = b.finalCorrespondenceComplete && matches == 1;
  }
}
inline void CaptureNativeInterpretationChecks(Boundary &boundary, const TopoDS_Shape &result) {
  auto &audit = EvidenceStore().audit;
  audit.Checkpoint("native-check-start", EvidenceStore().deadline, EvidenceStore().current);
  MalievNativeAudit::Scope scope(audit, "native-checks");
  boundary.finalShape = result;
  std::vector<TopoDS_Face> faces;
  for (const auto &item : boundary.items) {
    const auto type = item.original.ShapeType();
    if (type == TopAbs_VERTEX || type == TopAbs_EDGE || type == TopAbs_FACE) {
      boundary.tolerances.Observe(item.before.toleranceCaptured, item.before.tolerance, EvidenceStore().budget);
      boundary.tolerances.Observe(item.after.toleranceCaptured, item.after.tolerance, EvidenceStore().budget);
    }
    if (type == TopAbs_FACE) {
      const auto face = TopoDS::Face(item.mapped.IsNull() ? item.original : item.mapped);
      faces.push_back(face);
      const auto rows = MalievNativeInterpretation::AllocateFaceChecks(face);
      boundary.nativeChecks.insert(boundary.nativeChecks.end(), rows.begin(), rows.end());
    }
  }
  // New native connector vertices/edges also have observed tolerance obligations.
  TopTools_IndexedMapOfShape finalItems;
  if (!result.IsNull()) TopExp::MapShapes(result, finalItems);
  for (int i = 1; i <= finalItems.Extent(); ++i) {
    const auto type = finalItems(i).ShapeType();
    if (Identity(boundary, finalItems(i), true) >= 0 ||
        (type != TopAbs_VERTEX && type != TopAbs_EDGE && type != TopAbs_FACE))
      continue;
    const auto state = Capture(boundary, finalItems(i), true);
    boundary.tolerances.Observe(state.toleranceCaptured, state.tolerance, EvidenceStore().budget);
  }
  if (result.IsNull()) return;
  try {
    std::unique_ptr<BRepCheck_Analyzer> analyzer;
    {
      MalievNativeAudit::Scope analyzerScope(audit, "native-analyzer");
      analyzer.reset(new BRepCheck_Analyzer(result, Standard_True, Standard_False));
      boundary.finalValid = analyzer->IsValid();
    }
    boundary.nativeAnalyzerExecuted = true;
    if (!boundary.finalCorrespondenceComplete) return;
    size_t offset = 0;
    for (const auto &face : faces) {
      const auto rows = MalievNativeInterpretation::EvaluateFaceChecks(face, *analyzer, AssessmentContinues, &audit);
      for (const auto &row : rows) boundary.nativeChecks[offset++] = row;
    }
  } catch (const Standard_Failure &failure) {
    for (auto &row : boundary.nativeChecks)
      if (row.state == MalievNativeInterpretation::NativeCheckState::NotEvaluated)
        row.reason = failure.GetMessageString() ? failure.GetMessageString() : "native analyzer unavailable";
  }
}
inline void End(const TopoDS_Shape &result,
                const Handle(Standard_Transient) & info) {
  auto &e = EvidenceStore();
  if (!e.active || e.current < 0)
    return;
  e.audit.Checkpoint("end-entry", e.deadline, e.current);
  MalievNativeAudit::Scope endScope(e.audit, "end-assessment");
  auto &b = e.boundaries[e.current];
  const auto context = Handle(ShapeProcess_ShapeContext)::DownCast(info);
  AllocateSourceObligations(b);
  if (!context.IsNull())
    for (auto &item : b.items)
      if (context->Map().IsBound(item.original)) {
        item.mapped = context->Map().Find(item.original);
        item.association = "native-shape-process-map";
      }
  ResolveFinalCorrespondence(b, result);
  {
  MalievNativeAudit::Scope scope(e.audit, "after-capture");
  for (auto &item : b.items)
    item.after = Capture(b, item.mapped, true);
  }
  {
  MalievNativeAudit::Scope scope(e.audit, "post-obligation-association");
  AssociatePostObligations(b);
  }
  if (!b.finalCorrespondenceComplete) {
    CaptureNativeInterpretationChecks(b, result);
    e.current = -1;
    return;
  }
  if (std::isfinite(e.budget) && e.budget > 0) {
    size_t useCount = 0;
    for (const auto &item : b.items)
      useCount += item.after.uses.size();
    b.maximumIntervals = static_cast<int>(std::min<size_t>(
        1000000,
        std::max<size_t>(65536, std::min<size_t>(useCount, 1954) * 512)));
    b.totalIntervals = e.periodicIntervals;
    b.correlatedDiagnostics.continueAssessment = AssessmentContinues;
    std::vector<std::shared_ptr<RepresentedCurveBound>> curveCache(
        b.items.size());
    {
      MalievNativeAudit::Scope scope(e.audit, "reference-preparation");
      PrepareReferenceMetadata(b);
    }
    for (const auto &item : b.items)
      if (item.original.ShapeType() == TopAbs_FACE && !item.before.surface.IsNull() &&
          GeomAdaptor_Surface(item.before.surface).GetType() == GeomAbs_Cone)
        b.coneRegions.emplace_back(Identity(b, item.original, false), AssessSourceConeRegion(b, item));
    // All source identity and edge-sliver terms must exist before any face use
    // consumes them; native item enumeration is not dependency order.
    e.audit.Checkpoint("metric-start", e.deadline, e.current);
    {
    MalievNativeAudit::Scope metricScope(e.audit, "residual-evaluation");
    for (auto &item : b.items) {
      if (item.original.ShapeType() != TopAbs_FACE)
        continue;
      std::shared_ptr<RepresentedSurfaceBound> surfaceCache;
      std::string surfaceFailure;
      try {
        surfaceCache = std::make_shared<RepresentedSurfaceBound>(
            item.after.surface, item.after.geometryLocation);
        surfaceCache->continueAssessment = AssessmentContinues;
      } catch (const Standard_Failure &failure) {
        surfaceFailure = failure.GetMessageString()
                             ? failure.GetMessageString()
                             : "unsupported source support";
      }
      size_t postUseIndex = 0;
      for (const auto &use : item.after.uses) {
        ResidualReference reference;
        reference.face = Identity(b, item.mapped, true);
        reference.edge = use.edge;
        reference.orientation = use.orientation;
        reference.first = use.first;
        reference.last = use.last;
        reference.obligation = b.postObligationIds[static_cast<size_t>(&item - b.items.data())][postUseIndex++];
        b.residualReferences.push_back(reference);
        auto &ref = b.residualReferences.back();
        struct MetricTimer {
          Evidence &e;
          Boundary &b;
          size_t index;
          MalievNativeAudit::Clock::time_point started;
          MetricTimer(Evidence &evidence, Boundary &boundary)
              : e(evidence), b(boundary), index(boundary.residuals.size()),
                started(MalievNativeAudit::Clock::now()) {}
          ~MetricTimer() {
            if (index >= b.residuals.size() || index >= b.residualReferences.size()) return;
            MalievNativeAudit::Metric metric;
            const auto &reference = b.residualReferences[index];
            metric.boundaryIndex = e.current;
            metric.obligationId = b.requiredMetrics.rows.at(reference.obligation).occurrence;
            metric.method = reference.metricMethod;
            metric.status = b.residuals[index].status;
            metric.intervals = b.residuals[index].intervals;
            metric.elapsedMs = MalievNativeAudit::Milliseconds(MalievNativeAudit::Clock::now() - started);
            e.audit.ObserveMetric(metric);
          }
        } metricTimer(e, b);
        const Pcurve *old = nullptr;
        int oldMatches = 0;
        for (const auto &prior : item.before.uses)
          if (prior.edge == use.edge && prior.orientation == use.orientation) {
            ++oldMatches;
            old = &prior;
          }
        if (oldMatches == 1 && old) {
          ref.sourceFirst = old->first;
          ref.sourceLast = old->last;
          ref.sourceRangeCaptured = true;
        }
        if (!old || old->curve.IsNull())
          ++b.generatedPcurves;
        else if (Geometry(old->curve) != Geometry(use.curve))
          ++b.changedPcurves;
        if (!AssessmentContinues()) {
          Residual r;
          r.reason = "assessment-monotonic-time-limit";
          if (use.edge < 0)
            ++b.unmappedUses;
          b.residuals.push_back(r);
          continue;
        }
        if (b.totalIntervals >= b.maximumIntervals) {
          Residual r;
          r.reason = "aggregate-assessment-interval-limit";
          if (use.edge < 0)
            ++b.unmappedUses;
          b.residuals.push_back(r);
          continue;
        }
        if (use.edge < 0) {
          Residual r;
          r.reason = use.degenerate
                         ? "new-degenerate-coedge-needs-point-image-proof"
                         : "unmapped-post-repair-coedge";
          if (use.edge == -1 && use.degenerate) {
            TopoDS_Vertex first, last;
            TopExp::Vertices(use.nativeEdge, first, last);
            const int vertex = Identity(b, first, true);
            if (!first.IsNull() && first.IsSame(last) && vertex >= 0 &&
                SameGeometry(b.items[vertex].before.geometry,
                             b.items[vertex].after.geometry) &&
                SameGeometry(b.items[vertex].before.placement,
                             b.items[vertex].after.placement) &&
                b.items[vertex].before.vertexPointCaptured &&
                b.items[vertex].after.vertexPointCaptured &&
                b.items[vertex].before.vertexPoint.IsEqual(BRep_Tool::Pnt(first), 0) &&
                b.items[vertex].after.vertexPoint.IsEqual(BRep_Tool::Pnt(first), 0)) {
              ref.evaluatorAttempted = true;
              MalievNativeAudit::Scope scope(e.audit, "metric-degenerate");
              r = BoundDegenerate(use, item.after, BRep_Tool::Pnt(first),
                                  e.budget,
                                  std::max(0, b.maximumIntervals - b.totalIntervals));
              if (r.status == "bounded-within-budget") {
                ref.pathTerms = {r.upper};
                ref.pathTermKinds = {"degenerate-point-image-residual"};
                ref.path = "whole-native-degenerate-lift-to-preserved-source-vertex";
                ref.metricMethod = "outward-degenerate-point-image";
                ConnectorProof proof;
                proof.face = ref.face;
                proof.sourceVertex = vertex;
                proof.postUse = postUseIndex - 1;
                proof.obligation = ref.obligation;
                proof.metric = b.residuals.size();
                proof.normalizedEdge = use.nativeEdge;
                if (item.after.orientation == TopAbs_REVERSED)
                  proof.normalizedEdge.Reverse();
                int occurrences = 0;
                for (size_t li = 0; li < item.after.faceLoops.size(); ++li)
                  for (size_t ui = 0; ui < item.after.faceLoops[li].edges.size(); ++ui)
                    if (item.after.faceLoops[li].edges[ui].IsEqual(proof.normalizedEdge)) {
                      ++occurrences;
                      proof.loop = li;
                      proof.loopUse = ui;
                      proof.nativeWire = item.after.faceLoops[li].nativeWire;
                    }
                if (occurrences == 1) {
                  b.connectorProofs.push_back(proof);
                  ++b.verifiedDegenerateUses;
                }
              }
            }
          }
          if (r.status != "bounded-within-budget")
            ++b.unmappedUses;
          b.totalIntervals += r.intervals;
          b.residuals.push_back(r);
          continue;
        }
        Residual r;
        try {
          if (oldMatches != 1)
            throw Standard_Failure("source-coedge-correspondence-unavailable");
          const auto &edge = b.items[use.edge];
          if ((edge.after.flags & 6) != 6)
            throw Standard_Failure("same-parameter-or-range-unverified");
          if (!edge.sourceGeometryUnchanged || !item.sourceGeometryUnchanged)
            throw Standard_Failure("source-reference-geometry-changed");
          if (use.first != edge.after.first || use.last != edge.after.last)
            throw Standard_Failure("pcurve-and-3d-ranges-differ");
          if (!surfaceCache)
            throw Standard_Failure(surfaceFailure.c_str());
          if (!curveCache[use.edge])
            curveCache[use.edge] = std::make_shared<RepresentedCurveBound>(
                edge.after.curve, edge.after.geometryLocation);
          const auto &curve = *curveCache[use.edge];
          RepresentedCurveBound pc(use.curve);
          const auto &surface = *surfaceCache;
          ref.sliverUpper = edge.rangeDeviationUpper;
          const bool generated = !old || old->curve.IsNull();
          const bool unchanged =
              !generated &&
              SameGeometry(Geometry(old->curve), Geometry(use.curve)) &&
              old->first == use.first && old->last == use.last;
          ref.path =
              generated
                  ? "post-lift-to-unchanged-source-3d-plus-endpoint-sliver"
              : unchanged
                  ? "exact-unchanged-trim-with-independent-edge-residual"
                  : "old-lift-via-source-3d-to-new-lift";
          if (!generated && !unchanged) {
            if (std::max(old->first, use.first) > std::min(old->last, use.last))
              throw Standard_Failure(
                  "changed-existing-trim-no-retained-overlap");
            for (const auto range : {std::make_pair(old->first, use.first),
                                     std::make_pair(old->last, use.last)})
              if (range.first != range.second) {
                const auto box =
                    curve.Bounds(std::min(range.first, range.second),
                                 std::max(range.first, range.second));
                ref.sliverUpper =
                    std::max(ref.sliverUpper, DistanceUpper(box, box));
              }
          }
          const double remaining =
              RemainingPathBudget(e.budget, ref.sliverUpper);
          ref.evaluatorAttempted = true;
          {
          MalievNativeAudit::Scope scope(e.audit, "metric-dispatch-post");
          r = BoundRepairMetric(
              curve, pc, surface, use.first, use.last,
              generated || unchanged ? remaining : Down(remaining * .5),
              b.correlatedDiagnostics, b.residualDispatch, &ref.metricMethod,
              std::min(32768, b.maximumIntervals - b.totalIntervals));
          }
          if (r.status == "bounded-within-budget") {
            ref.pathTerms.push_back(r.upper);
            ref.pathTermKinds.push_back("post-trim-lift-residual");
          }
          if (!generated && !unchanged && r.status == "bounded-within-budget") {
            MalievNativeAudit::Scope scope(e.audit, "metric-dispatch-prior");
            std::string priorMethod;
            const auto prior = BoundRepairMetric(
                curve, RepresentedCurveBound(old->curve), surface, old->first,
                old->last, Down(remaining * .5), b.correlatedDiagnostics,
                b.residualDispatch, &priorMethod,
                std::min(32768, b.maximumIntervals - b.totalIntervals - r.intervals));
            ref.metricMethod = "new:" + ref.metricMethod + ";old:" + priorMethod;
            r.intervals += prior.intervals;
            r.subdivisions += prior.subdivisions;
            if (prior.status != "bounded-within-budget") {
              r.status = "unavailable";
              r.reason = "old-trim-reference-residual-" + prior.status + ":" +
                         prior.reason;
            } else {
              ref.pathTerms.push_back(prior.upper);
              ref.pathTermKinds.push_back("source-trim-lift-residual");
              r.upper = ComposePathUpper(r.upper, prior.upper);
            }
          }
          if (r.status == "bounded-within-budget") {
            ref.pathTerms.push_back(ref.sliverUpper);
            ref.pathTermKinds.push_back("endpoint-range-sliver");
            r.upper = ComposePathUpper(r.upper, ref.sliverUpper);
            if (r.upper > e.budget) {
              r.status = "unavailable";
              r.reason = "composed-path-upper-bound-exceeds-budget";
            }
          }
        } catch (const Standard_Failure &failure) {
          r.reason = failure.GetMessageString() ? failure.GetMessageString()
                                                : "residual unavailable";
        }
        b.totalIntervals += r.intervals;
        b.residuals.push_back(r);
      }
    }
    }
    e.audit.Checkpoint("metric-end", e.deadline, e.current);
    MalievNativeAudit::Scope cycleScope(e.audit, "source-cycle-matching");
    for (const auto &item : b.items) {
      if (item.original.ShapeType() == TopAbs_FACE) {
        bool complete =
            item.before.orientation == item.after.orientation &&
            item.before.faceLoops.size() == item.after.faceLoops.size();
        std::vector<bool> matched(item.after.faceLoops.size(), false);
        for (const auto &before : item.before.faceLoops) {
          int match = -1;
          bool ambiguous = false;
          complete = complete && before.complete;
          for (size_t i = 0; i < item.after.faceLoops.size(); ++i)
            if (!matched[i]) {
              const auto &loop = item.after.faceLoops[i];
              std::vector<std::pair<int, int>> uses;
              bool covered = loop.complete;
              for (size_t j = 0; j < loop.uses.size(); ++j) {
                if (loop.uses[j].first >= 0)
                  uses.push_back(loop.uses[j]);
                else {
                  covered = covered && loop.uses[j].first == -1 &&
                      ConnectorForLoopUse(b, item, loop, j, e.budget, false);
                }
              }
              if (covered && SameCycle(before.uses, uses)) {
                if (match >= 0)
                  ambiguous = true;
                match = static_cast<int>(i);
              }
            }
          if (match < 0 || ambiguous)
            complete = false;
          else
            matched[match] = true;
        }
        if (!complete)
          ++b.changedMembership;
        continue;
      }
      if (item.original.ShapeType() == TopAbs_WIRE)
        continue;
      if (MemberSet(item.before.membership) != MemberSet(item.after.membership))
        ++b.changedMembership;
    }
  }
  FinalizeMetricObligations(b, e.budget);
  CaptureNativeInterpretationChecks(b, result);
  e.current = -1;
}
inline MalievNativeWarning::EmissionToken PeriodicChange(const std::string &before,
                           const Handle(Geom_Surface) & after,
                           int surfaceEntity, int faceEntity) {
  auto &e = EvidenceStore();
  if (!e.active)
    return 0;
  MalievNativeAudit::Scope periodicScope(e.audit, "periodic-comparison");
  Periodic p;
  p.before = before;
  p.surfaceEntity = surfaceEntity;
  p.faceEntity = faceEntity;
  const size_t obligation = e.periodicMetrics.Require("periodic-reparameterization",
      "surface:" + std::to_string(surfaceEntity) + "/face:" + std::to_string(faceEntity) +
      "/operation:" + std::to_string(e.periodic.size()));
  try {
  p.after = Geometry(after);
  if (e.budget > 0 && AssessmentContinues()) {
    p.attempted = true;
    Handle(Geom_Surface) original;
    std::istringstream serialized(before);
    GeomTools::Read(original, serialized);
    if (!original.IsNull() && !after.IsNull()) {
      original->Bounds(p.beforeDomain[0],p.beforeDomain[1],p.beforeDomain[2],p.beforeDomain[3]);
      after->Bounds(p.afterDomain[0],p.afterDomain[1],p.afterDomain[2],p.afterDomain[3]);
      p.domainsCaptured = true;
      for (int i=0;i<4;++i) p.domainsCaptured = p.domainsCaptured && std::isfinite(p.beforeDomain[i]) && std::isfinite(p.afterDomain[i]);
    }
    p.comparison = BoundSurfaceDifference(original, after, e.budget * 0.5, AssessmentContinues,
                                          std::max(0, std::min(1024, 65536 - e.periodicIntervals)));
    e.periodicIntervals += p.comparison.intervals;
  }
  } catch (const Standard_Failure &failure) {
    p.comparison.status = "unavailable";
    p.comparison.reason = failure.GetMessageString() ? failure.GetMessageString() : "periodic native observation failed";
  }
  e.periodicMetrics.Finish(obligation, p.attempted, p.comparison.status, {p.comparison.upper}, e.budget,
                          p.attempted ? p.comparison.reason : "periodic evaluator not attempted");
  e.periodicMetrics.rows.at(obligation).pathTermKinds =
      {"periodic-surface-reparameterization-residual"};
  p.emission = MalievNativeWarning::NewEmission("periodic", e.periodic.size());
  e.periodic.push_back(p);
  return p.emission;
}
inline int WarningCount(const Handle(Transfer_TransientProcess) &process,
                        const Handle(Standard_Transient) &source) {
  if (!MalievNativeWarning::Store().enabled || process.IsNull() || source.IsNull()) return 0;
  const auto binder = process->Find(source);
  return binder.IsNull() ? 0 : binder->Check()->NbWarnings();
}
inline void PeriodicWarningDelivered(const Handle(Transfer_TransientProcess) &process,
                                     const Handle(Standard_Transient) &source,
                                     int before, MalievNativeWarning::EmissionToken token) {
  if (!MalievNativeWarning::Store().enabled) return;
  if (process.IsNull() || source.IsNull()) { ++MalievNativeWarning::Store().appendFailures; return; }
  const auto binder = process->Find(source);
  if (binder.IsNull()) { ++MalievNativeWarning::Store().appendFailures; return; }
  const auto model = process->Model();
  const int entity = model.IsNull() || source.IsNull() ? 0 : model->Number(source);
  MalievNativeWarning::Appended(binder->Check(), before, token, entity, model);
}
} // namespace MalievRepair
