#ifndef MALIEV_TARGET_QUERY_PROFILE_HPP
#define MALIEV_TARGET_QUERY_PROFILE_HPP
// Explicit profiling overlay only. No shape ownership, geometry decisions or
// production authority. All storage is bounded; inactive scopes read no clock.
#include <chrono>
#include <cstdint>
#include <cstring>
#include <exception>
#include <limits>
#include <string>
namespace MalievTargetQueryProfile {
enum class Phase { Batch, Query, Membership, Ancestry, PointTree, Segment,
 SegmentExtrema, SegmentTrimSearch, LineTree, FaceIntersector,
 ParallelProjection, WitnessJoin, Distance, Serialization, IntersectorCore,
 ElementaryIntersector, GenericIntersector, LineBoxReject, LineAccept,
 LineEdgePrepare, LineEdgeExtrema, LineEdgeResults, LineVertex,
 GenericEcc3d, GenericEccPrepare, GenericEccLength1, GenericEccLength2,
 GenericEccFinder, Count };
enum class Counter { Points, In, On, Out, Unknown, MembershipNotRequested,
 SegmentAttempts, SegmentRetries, SegmentFlag0, SegmentFlag1, SegmentFlag2,
 SegmentFlag3, SegmentFlagOther, TrimIterations, FaceDone, FaceNotDone,
 FacePoints, LazyBounds, LineBoxes, LineBoxesRejected, LineEdgeCandidates,
 LineVertexCandidates, LineOtherCandidates, LineExtremaDone, LineExtremaNotDone,
 LineExtremaParallel, LineNbExt, LineNearEdgeExtrema, LineAcceptedEdges,
 LineAcceptedVertices, Count };
enum class SupportGroup { SegmentProjection, ParallelProjection, Intersector, LineEdgeExtrema, GenericEccLength1, GenericEccLength2, Count };
static const size_t PhaseCount=size_t(Phase::Count), CounterCount=size_t(Counter::Count), SupportCount=12;
struct Stats { uint64_t calls=0,inclusiveNs=0,exclusiveNs=0,maxNs=0,unwinds=0; };
struct Snapshot {
 bool valid=false,bound=false,overflow=false,truncated=false,labelValid=false;
 char sourceGeneration[256]={},sourceBytesHash[128]={},nativeImportRevision[128]={},topologyRevision[128]={};
 char sessionId[128]={},batchId[256]={},mode[32]={},diagnosticRequestHash[65]={};
 uint64_t requestedPoints=0;
 Stats phases[PhaseCount],support[size_t(SupportGroup::Count)][SupportCount];
 uint64_t counters[CounterCount]={};
};
struct Frame { uint64_t start=0,children=0; };
struct Context { Snapshot snapshot; Frame stack[32]; size_t depth=0; };
struct Storage { Context* active=nullptr; Snapshot last; uint64_t clockReads=0; };
// C++11 inline-local static has one identity across translation units. The
// linked OCCT/importer regression verifies that identity rather than assuming it.
inline Storage& Store() { static Storage value; return value; }
inline Context* Active() { return Store().active; }
inline uint64_t Now() {
 ++Store().clockReads;
 return uint64_t(std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now().time_since_epoch()).count());
}
inline void Add(Context& c,uint64_t& to,uint64_t value) {
 if(value>std::numeric_limits<uint64_t>::max()-to){c.snapshot.overflow=true;to=std::numeric_limits<uint64_t>::max();}else to+=value;
}
template<size_t N> inline void Copy(Context& c,char (&to)[N],const std::string& from) {
 if(from.size()>=N)c.snapshot.truncated=true;
 const size_t n=from.size()<N?from.size():N-1;std::memcpy(to,from.data(),n);to[n]=0;
}
inline void Bind(const std::string& generation,const std::string& hash,const std::string& importRevision,
 const std::string& topology,const std::string& session,const std::string& batch,const std::string& mode,uint64_t points) {
 Context* c=Active();if(!c)return;auto& s=c->snapshot;
 Copy(*c,s.sourceGeneration,generation);Copy(*c,s.sourceBytesHash,hash);Copy(*c,s.nativeImportRevision,importRevision);Copy(*c,s.topologyRevision,topology);
 Copy(*c,s.sessionId,session);Copy(*c,s.batchId,batch);Copy(*c,s.mode,mode);s.requestedPoints=points;s.bound=true;
}
class BatchScope {
 Context context;Context* previous;
public:
 explicit BatchScope(const std::string& hash):previous(Active()) {
  Copy(context,context.snapshot.diagnosticRequestHash,hash);
  bool valid=hash.size()==64;for(char v:hash)if(!((v>='0'&&v<='9')||(v>='a'&&v<='f')))valid=false;
  context.snapshot.labelValid=valid;Store().active=&context;
 }
 ~BatchScope() {
  if(context.depth)context.snapshot.overflow=true;
  context.snapshot.valid=context.snapshot.bound&&context.snapshot.labelValid&&!context.snapshot.overflow&&!context.snapshot.truncated;
  Store().last=context.snapshot;Store().active=previous;
 }
 BatchScope(const BatchScope&)=delete;BatchScope& operator=(const BatchScope&)=delete;
};
inline void Accumulate(Context& c,Stats& s,uint64_t elapsed,uint64_t exclusive,bool unwind) {
 Add(c,s.calls,1);Add(c,s.inclusiveNs,elapsed);Add(c,s.exclusiveNs,exclusive);if(elapsed>s.maxNs)s.maxNs=elapsed;if(unwind)Add(c,s.unwinds,1);
}
class PhaseScope {
 Context* context;size_t index=0;Phase phase;int group,type;bool stopped=false,enteredException=false,stacked=false;
public:
 explicit PhaseScope(Phase p,int supportGroup=-1,int supportType=-1):context(Active()),phase(p),group(supportGroup),type(supportType) {
  if(!context)return;
  if(context->depth>=32){context->snapshot.overflow=true;return;}
  index=context->depth++;stacked=true;context->stack[index].children=0;context->stack[index].start=Now();enteredException=std::uncaught_exception();
 }
 void Stop() {
  if(stopped||!context||!stacked)return;stopped=true;
  const auto end=Now();Frame& frame=context->stack[index];
  if(context->depth!=index+1||end<frame.start||frame.children>end-frame.start){context->snapshot.overflow=true;context->depth=index;return;}
  const uint64_t elapsed=end-frame.start,exclusive=elapsed-frame.children;--context->depth;
  const bool unwind=std::uncaught_exception()&&!enteredException;
  Accumulate(*context,context->snapshot.phases[size_t(phase)],elapsed,exclusive,unwind);
  if(group>=0&&group<int(SupportGroup::Count))Accumulate(*context,context->snapshot.support[group][type>=0&&type<11?type:11],elapsed,exclusive,unwind);
  if(index)Add(*context,context->stack[index-1].children,elapsed);
 }
 ~PhaseScope(){Stop();}
 PhaseScope(const PhaseScope&)=delete;PhaseScope& operator=(const PhaseScope&)=delete;
};
inline void Count(Counter c,uint64_t n=1) { Context* a=Active();if(a)Add(*a,a->snapshot.counters[size_t(c)],n); }
inline void SegmentResult(int flag,bool retry) {
 Count(Counter::SegmentAttempts);if(retry)Count(Counter::SegmentRetries);
 Count(flag>=0&&flag<=3?Counter(size_t(Counter::SegmentFlag0)+size_t(flag)):Counter::SegmentFlagOther);
}
inline bool ObserveDone(bool done) { Count(done?Counter::FaceDone:Counter::FaceNotDone);return done; }
inline int ObservePoints(int points) { if(points>=0)Count(Counter::FacePoints,uint64_t(points));return points; }
inline bool ObserveLineDone(bool done) { Count(done?Counter::LineExtremaDone:Counter::LineExtremaNotDone);return done; }
inline bool ObserveLineParallel(bool parallel) { if(parallel)Count(Counter::LineExtremaParallel);return parallel; }
inline int ObserveLineNbExt(int n) { if(n>=0)Count(Counter::LineNbExt,uint64_t(n));return n; }
inline bool ObserveLineEdgeAccepted(bool accepted) { if(accepted)Count(Counter::LineAcceptedEdges);return accepted; }
inline bool ObserveLineVertexAccepted(bool accepted) { if(accepted)Count(Counter::LineAcceptedVertices);return accepted; }
inline Snapshot ReadLastCompletedSnapshot() { return Store().last; }
inline const char* PhaseName(size_t n) {
 static const char* names[]={"batch","query","membership","ancestry","pointTree","segment","segmentExtrema","segmentTrimSearch","lineTree","faceIntersector","parallelProjection","witnessJoin","distance","serialization","intersectorCore","elementaryIntersector","genericIntersector","lineBoxReject","lineAccept","lineEdgePrepare","lineEdgeExtrema","lineEdgeResults","lineVertex","genericEcc3d","genericEccPrepare","genericEccLength1","genericEccLength2","genericEccFinder"};return names[n];
}
inline const char* CounterName(size_t n) {
 static const char* names[]={"points","IN","ON","OUT","UNKNOWN","membershipNotRequested","segmentAttempts","segmentRetries","segmentFlag0","segmentFlag1","segmentFlag2","segmentFlag3","segmentFlagOther","trimIterations","faceDone","faceNotDone","facePoints","lazyBounds","lineBoxes","lineBoxesRejected","lineEdgeCandidates","lineVertexCandidates","lineOtherCandidates","lineExtremaDone","lineExtremaNotDone","lineExtremaParallel","lineNbExt","lineNearEdgeExtrema","lineAcceptedEdges","lineAcceptedVertices"};return names[n];
}
}
#endif
