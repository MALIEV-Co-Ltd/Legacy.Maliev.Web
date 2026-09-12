#pragma once
#include "kernel-target-query.hpp"
#include <cstdint>
#include <limits>
#include <map>
#include <chrono>
namespace MalievKernel { namespace TargetQuery {
struct Binding {
 std::string sourceGeneration,sourceBytesHash,nativeImportRevision,topologyRevision;
 bool Valid() const { if(sourceGeneration.empty()||nativeImportRevision.empty()||topologyRevision.empty()||sourceBytesHash.size()!=64)return false;for(char c:sourceBytesHash)if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')))return false;return true; }
 bool operator==(const Binding& b) const { return sourceGeneration==b.sourceGeneration&&sourceBytesHash==b.sourceBytesHash&&nativeImportRevision==b.nativeImportRevision&&topologyRevision==b.topologyRevision; }
};
struct Session { Target target;std::vector<uint8_t> bytes;Binding binding;bool bound=false;Session(Target&& t,std::vector<uint8_t>&& b):target(std::move(t)),bytes(std::move(b)){} };
struct SessionResult { std::string id,reason;bool available=false; };
class Registry {
 std::map<std::string,std::unique_ptr<Session>> sessions;size_t retained=0;uint64_t counter=0;
public:
 static constexpr size_t MaxSessions=4,MaxPoints=256,MaxSourceBytes=64*1024*1024;
 size_t Size() const { return sessions.size(); }size_t SourceBytes() const { return retained; }
 bool CanRetain(size_t bytes) const { return sessions.size()<MaxSessions&&bytes>0&&bytes<=MaxSourceBytes-retained; }
 SessionResult Add(Target&& target,std::vector<uint8_t>&& bytes) {
  SessionResult r;
  if(!target.Available()){r.reason=target.reason;return r;}
  if(!CanRetain(bytes.size())){r.reason="session_capacity_exceeded";return r;}
  if(counter==std::numeric_limits<uint64_t>::max()){r.reason="session_id_exhausted";return r;}
  r.id="native-target-"+std::to_string(++counter);
  std::unique_ptr<Session> session(new Session(std::move(target),std::move(bytes)));const size_t count=session->bytes.size();
  sessions.emplace(r.id,std::move(session));retained+=count;r.available=true;return r;
 }
 Session* Find(const std::string& id) { auto found=sessions.find(id);return found==sessions.end()?nullptr:found->second.get(); }
 std::string Bind(const std::string& id,const std::vector<uint8_t>& bytes,const Binding& binding) {
  auto* s=Find(id);if(!s)return "session_unavailable";if(!binding.Valid())return "binding_invalid";
  if(bytes!=s->bytes)return "source_bytes_mismatch";
  if(s->bound&&!(s->binding==binding))return "binding_mismatch";
  s->binding=binding;s->bound=true;return "";
 }
 bool Release(const std::string& id) { auto found=sessions.find(id);if(found==sessions.end())return false;retained-=found->second->bytes.size();sessions.erase(found);return true; }
};
inline Registry& Sessions() { static Registry registry;return registry; }
struct BatchResult { std::vector<PointResult> points;size_t evaluated=0,available=0,requested=0;std::string status; };
// Own the complete ordered result inventory even if evaluation fails partway.
template<class Evaluate> inline BatchResult EvaluateOrdered(const std::vector<gp_Pnt>& points,QueryMode mode,Evaluate evaluate) {
 BatchResult batch;bool interrupted=false;batch.points.reserve(points.size());
 for(const auto& point:points){PointResult result;if(!interrupted)try {result=evaluate(point,mode);++batch.evaluated;}catch(...){interrupted=true;}
  if(interrupted)result.membership.reason=result.distance.reason="batch_evaluation_interrupted";
  if(mode!=QueryMode::Distance){++batch.requested;if(result.membership.available)++batch.available;}
  if(mode!=QueryMode::Membership){++batch.requested;if(result.distance.available)++batch.available;}
  batch.points.push_back(result);
 }
 batch.status=batch.available==batch.requested?"complete":batch.available?"partial":"unavailable";return batch;
}
#ifdef EMSCRIPTEN
} }
#include <emscripten/val.h>
namespace MalievKernel { namespace TargetQuery {
using emscripten::val;
inline bool IsString(const val& v) { return v.typeOf().as<std::string>()=="string"; }
inline bool IsObject(const val& v) { return !v.isNull()&&!v.isUndefined()&&v.typeOf().as<std::string>()=="object"; }
inline bool IsArray(const val& v) { return val::global("Array").call<bool>("isArray",v); }
inline std::string String(const val& v) { return IsString(v)?v.as<std::string>():""; }
inline bool Fields(const val& v,const std::set<std::string>& allowed) {
 if(!IsObject(v)||IsArray(v))return false;const val keys=val::global("Object").call<val>("keys",v);
 for(unsigned i=0;i<keys["length"].as<unsigned>();++i)if(!allowed.count(keys[i].as<std::string>()))return false;
 return true;
}
inline val TextOrNull(const std::string& s) { return s.empty()?val::null():val(s); }
inline val Unavailable(const std::string& reason) { val v=val::object();v.set("status",std::string("unavailable"));v.set("reason",reason);return v; }
inline val BindingValue(const Binding& b) {
 val v=val::object();v.set("contract",std::string("NativeTargetSourceBinding.v1"));v.set("sourceGeneration",b.sourceGeneration);v.set("sourceBytesHash",b.sourceBytesHash);v.set("nativeImportRevision",b.nativeImportRevision);v.set("topologyRevision",b.topologyRevision);return v;
}
inline bool ReadBinding(const val& v,Binding& b) {
 if(!Fields(v,{"contract","sourceGeneration","sourceBytesHash","nativeImportRevision","topologyRevision"})||String(v["contract"])!="NativeTargetSourceBinding.v1")return false;
 b.sourceGeneration=String(v["sourceGeneration"]);b.sourceBytesHash=String(v["sourceBytesHash"]);b.nativeImportRevision=String(v["nativeImportRevision"]);b.topologyRevision=String(v["topologyRevision"]);return b.Valid();
}
inline bool ReadBytes(const val& v,size_t required,std::vector<uint8_t>& bytes) {
 if(!IsObject(v)||(!IsArray(v)&&!val::global("ArrayBuffer").call<bool>("isView",v)))return false;
 if(v["length"].typeOf().as<std::string>()!="number"||v["length"].as<double>()!=double(required))return false;
 bytes.reserve(required);
 for(size_t i=0;i<required;++i){const val x=v[i];if(x.typeOf().as<std::string>()!="number")return false;double n=x.as<double>();if(!std::isfinite(n)||n<0||n>255||std::floor(n)!=n)return false;bytes.push_back(uint8_t(n));}return true;
}
inline void Metadata(val& v,const Target* target=nullptr) {
 v.set("policyVersion",std::string("native-target-numerical-v1"));v.set("coordinateSpace",std::string("import-world-mm"));
 v.set("classificationMethod",std::string("BRepClass3d_SolidClassifier-final-observable-state-with-finite-OUT-source-face-witness"));
 v.set("boundaryDistanceMethod",std::string("BRepExtrema_DistShapeShape-point-to-all-finite-boundary-faces"));
 v.set("classificationToleranceMm",Precision::Confusion());v.set("distanceDeflectionMm",Precision::Confusion());
 v.set("maximumSourceTopologyToleranceMm",target?val(target->maxTopologyToleranceMm):val::null());
 v.set("formalIntervalCertificate",false);v.set("certifiedDistanceErrorBoundMm",val::null());v.set("applicationInterpretationApproved",false);v.set("machiningAuthorized",false);
 v.set("sourceBindingAuthority",std::string("caller-labels-bound-to-exact-imported-bytes"));
 v.set("maxSessions",unsigned(Registry::MaxSessions));v.set("maxPointsPerBatch",unsigned(Registry::MaxPoints));v.set("maxRetainedSourceBytes",double(Registry::MaxSourceBytes));
 v.set("liveSessions",unsigned(Sessions().Size()));v.set("retainedSourceBytes",double(Sessions().SourceBytes()));
 if(target){v.set("bodyId",target->SourceInventory().bodyId);val ids=val::array();size_t i=0;for(const auto& f:target->SourceInventory().faces)ids.set(i++,f.id);v.set("allFaceIds",ids);}
}
inline val BindNativeTargetSource(const val& idValue,const val& bytesValue,const val& bindingValue) {
 const std::string id=String(idValue);Session* s=Sessions().Find(id);if(!s)return Unavailable("session_unavailable");
 Binding b;if(!ReadBinding(bindingValue,b))return Unavailable("binding_invalid");
 std::vector<uint8_t> bytes;if(!ReadBytes(bytesValue,s->bytes.size(),bytes))return Unavailable("source_bytes_mismatch");
 const auto reason=Sessions().Bind(id,bytes,b);if(!reason.empty())return Unavailable(reason);
 val v=val::object();v.set("status",std::string("bound"));v.set("sessionId",id);v.set("binding",BindingValue(b));v.set("byteEqualityVerified",true);v.set("bindingAuthority",std::string("caller-labels-bound-to-exact-imported-bytes"));Metadata(v,&s->target);return v;
}
inline val ReleaseNativeTarget(const val& idValue) { val v=val::object();v.set("released",Sessions().Release(String(idValue)));Metadata(v);return v; }
inline val PointValue(const PointResult& r,const gp_Pnt& p,const std::string& id,QueryMode mode) {
 val v=val::object(),point=val::array(),m=val::object(),d=val::object();point.set(0,p.X());point.set(1,p.Y());point.set(2,p.Z());v.set("point",point);v.set("pointId",id);
 const bool membership=mode!=QueryMode::Distance,distance=mode!=QueryMode::Membership;
 m.set("status",std::string(!membership?"not-requested":r.membership.available?"available":"unavailable"));m.set("state",std::string(StateName(r.membership.state)));
 m.set("rawNativeState",membership&&r.membership.invoked?val(std::string(NativeStateName(r.membership.raw))):val::null());m.set("rejected",membership&&r.membership.invoked?val(r.membership.rejected):val::null());
 m.set("reason",membership?TextOrNull(r.membership.reason):val::null());m.set("completionEvidence",membership?r.membership.completion:std::string("not-requested"));m.set("sourceFaceId",TextOrNull(r.membership.sourceFaceId));
 d.set("status",std::string(!distance?"not-requested":r.distance.available?"available":"unavailable"));d.set("valueMm",distance&&r.distance.available?val(r.distance.valueMm):val::null());d.set("reason",distance?TextOrNull(r.distance.reason):val::null());
 d.set("nearestSupportStatus",distance?r.distance.supportStatus:std::string("not-requested"));val supports=val::array();size_t index=0;
 for(const auto& row:r.distance.supports){val support=val::object(),ids=val::array();for(size_t i=0;i<row.sourceIds.size();++i)ids.set(i,row.sourceIds[i]);support.set("kind",row.kind);support.set("sourceIds",ids);supports.set(index++,support);}d.set("nearestSupports",supports);
 v.set("membership",m);v.set("boundaryDistance",d);return v;
}
inline val QueryNativeTargetBatch(const val& request) {
 // Validate and copy the entire ordered batch before any native evaluation.
 if(!Fields(request,{"contract","sessionId","binding","batchId","policyVersion","mode","coordinateSpace","points"}))return Unavailable("request_invalid");
 if(String(request["contract"])!="NativeTargetQueryBatch.v1"||String(request["policyVersion"])!="native-target-numerical-v1"||String(request["coordinateSpace"])!="import-world-mm")return Unavailable("request_policy_invalid");
 const std::string id=String(request["sessionId"]),batch=String(request["batchId"]),modeString=String(request["mode"]);Session* session=Sessions().Find(id);
 if(!session)return Unavailable("session_unavailable");if(!session->bound)return Unavailable("session_unbound");
 Binding binding;if(!ReadBinding(request["binding"],binding)||!(binding==session->binding))return Unavailable("binding_mismatch");
 if(batch.empty()||(modeString!="membership"&&modeString!="boundary-distance"&&modeString!="membership-and-distance"))return Unavailable("request_mode_or_id_invalid");
 const QueryMode mode=modeString=="membership"?QueryMode::Membership:modeString=="boundary-distance"?QueryMode::Distance:QueryMode::Both;
 const val values=request["points"];if(!IsArray(values))return Unavailable("points_invalid");const unsigned count=values["length"].as<unsigned>();if(count==0||count>Registry::MaxPoints)return Unavailable("point_count_invalid");
 std::vector<gp_Pnt> points;std::vector<std::string> ids;std::set<std::string> unique;
 for(unsigned i=0;i<count;++i){const val row=values[i];if(!Fields(row,{"pointId","point"}))return Unavailable("point_invalid");const std::string pointId=String(row["pointId"]);const val p=row["point"];
  if(pointId.empty()||!unique.insert(pointId).second||!IsArray(p)||p["length"].as<unsigned>()!=3)return Unavailable("point_identity_or_coordinates_invalid");
  double xyz[3];for(unsigned j=0;j<3;++j){if(p[j].typeOf().as<std::string>()!="number")return Unavailable("point_nonfinite");xyz[j]=p[j].as<double>();if(!std::isfinite(xyz[j]))return Unavailable("point_nonfinite");}ids.push_back(pointId);points.emplace_back(xyz[0],xyz[1],xyz[2]);
 }
 val result=val::object(),output=val::array();result.set("contract",std::string("NativeTargetQueryBatchResult.v1"));result.set("sessionId",id);result.set("binding",BindingValue(binding));result.set("batchId",batch);result.set("mode",modeString);result.set("pointCount",count);
 const auto start=std::chrono::steady_clock::now();
 const auto evaluated=EvaluateOrdered(points,mode,[&](const gp_Pnt& p,QueryMode m){return session->target.Query(p,m);});
 for(size_t i=0;i<points.size();++i)output.set(i,PointValue(evaluated.points[i],points[i],ids[i],mode));
 result.set("points",output);result.set("evaluatedPointCount",unsigned(evaluated.evaluated));result.set("elapsedMs",std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count());result.set("status",evaluated.status);Metadata(result,&session->target);return result;
}
// A provisional owning capture, never a reference to Mesh, ExportContext or JS output.
struct Capture {
 std::vector<uint8_t> bytes;TopoDS_Shape solid;Inventory inventory;size_t occurrences=0;bool ready=false,committed=false;std::string reason="import_not_completed",provisionalId;
 ~Capture() { if(!committed&&!provisionalId.empty())Sessions().Release(provisionalId); }
 void Commit() { committed=true; }
 void Source(const std::vector<uint8_t>& source) { if(!Sessions().CanRetain(source.size())){reason="session_capacity_exceeded";return;}bytes=source; }
 void Occurrence(const TopoDS_Shape& shape,const Inventory& source,bool complete) { ++occurrences;if(occurrences==1&&complete){solid=shape;inventory=source;ready=true;}else{ready=false;reason="source_occurrence_coverage_incomplete";} }
 val Finish(const val& provenance) {
  if(bytes.empty())return Unavailable(reason);
  if(!ready||occurrences!=1)return Unavailable("source_occurrence_coverage_incomplete");
  const val coverage=provenance["sourceCoverage"],interpretation=provenance["nativeInterpretation"];
  if(!IsObject(coverage)||String(coverage["status"])!="complete_supported_single_solid")return Unavailable("source_coverage_unavailable");
  const std::string state=IsObject(interpretation)?String(interpretation["status"]):"";
  if(state!="native-interpreted"&&state!="native-interpreted-with-bounded-repair")return Unavailable("native_interpretation_prerequisites_unavailable");
  const bool mm=provenance["millimeterOutput"].typeOf().as<std::string>()=="boolean"&&provenance["millimeterOutput"].as<bool>();
  Target target(solid,inventory,mm);auto admitted=Sessions().Add(std::move(target),std::move(bytes));if(!admitted.available)return Unavailable(admitted.reason);
  provisionalId=admitted.id;val result=val::object();result.set("status",std::string("available"));result.set("sessionId",admitted.id);result.set("sourceBindingStatus",std::string("unbound"));result.set("nativeInterpretationPrerequisiteStatus",state);Metadata(result,&Sessions().Find(admitted.id)->target);return result;
 }
};
#endif
} }
