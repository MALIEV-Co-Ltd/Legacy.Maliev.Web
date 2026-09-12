// Explicit, nonproduction instrumentation. Never called by the default overlay.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),cp=require('node:child_process');
const expected={
 'occt/src/Extrema/Extrema_ECC_0.cxx':'18b8460017d89567c1e1424ee6ab1c7274f967d04f277bd71cdd0654b0689fe6',
 'occt/src/Extrema/Extrema_GenExtCC.gxx':'175b7b40cb0a517cd922d29624b70986140bb159d6a0c01a3ad996498aa87f09',
 'occt/src/BRepClass3d/BRepClass3d_BndBoxTree.hxx':'efc4dfad2d0b55984cac9792ef055317f8f1cb7560ec7101a21085df8808f62a',
 'occt/src/BRepClass3d/BRepClass3d_BndBoxTree.cxx':'697125741fc32d6d80f23b86e9bce1590f97851d8431ee7ce52a594878ea47e9',
 'occt/src/BRepClass3d/BRepClass3d_SClassifier.cxx':'5e216a184e60bbc1dfe5eda05195369188f1f37a505737cceb04d346833b8d70',
 'occt/src/BRepClass3d/BRepClass3d_SolidExplorer.cxx':'de71a74131c8e90f963e6a942c2c670f708846dccfbab17f47e091728c886b2e',
 'occt/src/IntCurvesFace/IntCurvesFace_Intersector.cxx':'096fbe6bf79345aec82a05f36edd9ef01651106ed94673339265859c0772ab52',
 'occt-import-js/src/kernel-target-query.hpp':'bbcdf452e59894499c0d6d02b66e02c1ca319e4706121d283f947b25af72e8d1',
 'occt-import-js/src/kernel-target-query-session.hpp':'a6300a0fe92fe8e947dcbab389455d749b5d159f0196bde5c64f8b0dc77b954e',
 'occt-import-js/src/js-interface.cpp':'ef5fa41be2b19105826aca0cbf98625eaa218a0e797a1c382c12c3e8eda757a0'
};
const hash=b=>crypto.createHash('sha256').update(b).digest('hex');
function replaceOnce(text,from,to){const count=text.split(from).length-1;if(count!==1)throw Error('profile anchor count '+count+': '+from);return text.replace(from,to);}
const ns='MalievTargetQueryProfile::';
function timer(name,phase,extra=''){return `${ns}PhaseScope ${name}(${ns}Phase::${phase}${extra});`;}
const api=`
// Profiling-only sidecar: caller hash is a diagnostic label, not native authority.
inline val ProfileStats(const MalievTargetQueryProfile::Stats& s) {
 val v=val::object();v.set("calls",double(s.calls));v.set("inclusiveNs",double(s.inclusiveNs));v.set("exclusiveNs",double(s.exclusiveNs));v.set("maxNs",double(s.maxNs));v.set("unwinds",double(s.unwinds));return v;
}
inline val ReadLastNativeTargetQueryProfile() {
 const auto s=MalievTargetQueryProfile::ReadLastCompletedSnapshot();val v=val::object(),phases=val::object(),counts=val::object(),supports=val::array();
 v.set("contract",std::string("NativeTargetQueryProfile.v1"));v.set("status",std::string(s.valid?"available":"unavailable"));v.set("diagnosticOnly",true);v.set("hashAuthority",std::string("caller-diagnostic-label-independently-verified-by-harness"));
 v.set("overflow",s.overflow);v.set("truncated",s.truncated);v.set("bound",s.bound);v.set("labelValid",s.labelValid);
 v.set("sourceGeneration",std::string(s.sourceGeneration));v.set("sourceBytesHash",std::string(s.sourceBytesHash));v.set("nativeImportRevision",std::string(s.nativeImportRevision));v.set("topologyRevision",std::string(s.topologyRevision));
 v.set("sessionId",std::string(s.sessionId));v.set("batchId",std::string(s.batchId));v.set("mode",std::string(s.mode));v.set("diagnosticRequestHash",std::string(s.diagnosticRequestHash));v.set("requestedPoints",double(s.requestedPoints));
 for(size_t i=0;i<MalievTargetQueryProfile::PhaseCount;++i)phases.set(MalievTargetQueryProfile::PhaseName(i),ProfileStats(s.phases[i]));
 for(size_t i=0;i<MalievTargetQueryProfile::CounterCount;++i)counts.set(MalievTargetQueryProfile::CounterName(i),double(s.counters[i]));
 for(size_t g=0;g<size_t(MalievTargetQueryProfile::SupportGroup::Count);++g){val row=val::array();for(size_t t=0;t<MalievTargetQueryProfile::SupportCount;++t)row.set(t,ProfileStats(s.support[g][t]));supports.set(g,row);}
 v.set("phases",phases);v.set("counters",counts);v.set("supportBuckets",supports);return v;
}
inline val QueryNativeTargetBatchProfile(const val& request,const val& diagnosticHash) {
 MalievTargetQueryProfile::BatchScope context(String(diagnosticHash));
 MalievTargetQueryProfile::PhaseScope total(MalievTargetQueryProfile::Phase::Batch);
 return QueryNativeTargetBatch(request);
}
`;
function instrument(input){
 for(const [f,h]of Object.entries(expected))if(typeof input[f]!=='string'||hash(input[f])!==h)throw Error('profile source hash '+f);
 const out={...input};let file;const change=(a,b)=>out[file]=replaceOnce(out[file],a,b);
 for(file of Object.keys(out))if(!file.includes('/Extrema/'))out[file]=`#include "${file.includes('IntCurvesFace/')?'../BRepClass3d/':''}kernel-target-query-profile.hpp"\n`+out[file];
 file='occt-import-js/src/kernel-target-query.hpp';
 change(' PointResult Query(const gp_Pnt& p,QueryMode mode) {',' PointResult Query(const gp_Pnt& p,QueryMode mode) {\n  '+timer('profileQuery','Query')+`\n  ${ns}Count(${ns}Counter::Points);`);
 change('   data->classifier.Perform(p,Precision::Confusion());','   '+timer('profileMembership','Membership')+'\n   data->classifier.Perform(p,Precision::Confusion());\n   profileMembership.Stop();');
 change('   const std::string faceId=JoinShape(data->classifier.Face(),data->inventory.faces);','   '+timer('profileWitness','WitnessJoin')+'\n   const std::string faceId=JoinShape(data->classifier.Face(),data->inventory.faces);\n   profileWitness.Stop();');
 change('  if(mode!=QueryMode::Membership)try {','  if(mode!=QueryMode::Membership)try {\n   '+timer('profileDistance','Distance'));
 change('  return result;\n }\n};',`  ${ns}Count(mode==QueryMode::Distance?${ns}Counter::MembershipNotRequested:result.membership.state==QueryState::In?${ns}Counter::In:result.membership.state==QueryState::On?${ns}Counter::On:result.membership.state==QueryState::Out?${ns}Counter::Out:${ns}Counter::Unknown);\n  return result;\n }\n};`);
 file='occt-import-js/src/kernel-target-query-session.hpp';
 change(' val result=val::object(),output=val::array();result.set("contract",std::string("NativeTargetQueryBatchResult.v1"));',` ${ns}Bind(binding.sourceGeneration,binding.sourceBytesHash,binding.nativeImportRevision,binding.topologyRevision,id,batch,modeString,count);\n val result=val::object(),output=val::array();result.set("contract",std::string("NativeTargetQueryBatchResult.v1"));`);
 change(' for(size_t i=0;i<points.size();++i)output.set(i,PointValue(evaluated.points[i],points[i],ids[i],mode));',' '+timer('profileSerialization','Serialization')+'\n for(size_t i=0;i<points.size();++i)output.set(i,PointValue(evaluated.points[i],points[i],ids[i],mode));\n profileSerialization.Stop();');
 change('// A provisional owning capture, never a reference to Mesh, ExportContext or JS output.',api+'\n// A provisional owning capture, never a reference to Mesh, ExportContext or JS output.');
 file='occt-import-js/src/js-interface.cpp';
 change('EMSCRIPTEN_BINDINGS (occtimportjs)\n{','EMSCRIPTEN_BINDINGS (occtimportjs)\n{\n    emscripten::function("QueryNativeTargetBatchProfile", &MalievKernel::TargetQuery::QueryNativeTargetBatchProfile);\n    emscripten::function("ReadLastNativeTargetQueryProfile", &MalievKernel::TargetQuery::ReadLastNativeTargetQueryProfile);');
 file='occt/src/BRepClass3d/BRepClass3d_SClassifier.cxx';
 for(const [name,phase,anchor]of [['profilePointTree','PointTree','SelsVE = aTree.Select(aSelectorPoint);'],['profileAncestry','Ancestry','TopExp::MapShapesAndAncestors(SolidExplorer.GetShape(), TopAbs_EDGE, TopAbs_FACE, mapEF);'],['profileLineTree','LineTree','SelsEVL = aTree.Select(aSelectorLine);'],['profileFace','FaceIntersector','Intersector3d.Perform(L, minW, maxW);']])change(anchor,timer(name,phase)+'\n  '+anchor+'\n  '+name+'.Stop();');
 change('    if (anIndFace == 0)\n      iFlag = SolidExplorer.Segment(P,L,Par);','    '+timer('profileSegment','Segment')+'\n    const bool profileRetry = anIndFace != 0;\n    if (anIndFace == 0)\n      iFlag = SolidExplorer.Segment(P,L,Par);');
 change('      iFlag = SolidExplorer.OtherSegment(P,L,Par);','      iFlag = SolidExplorer.OtherSegment(P,L,Par);\n    profileSegment.Stop();\n    '+ns+'SegmentResult(iFlag,profileRetry);');
 // Restrict result observations to finite-point Perform, not infinite-point Load.
 const finite=out[file].indexOf('void BRepClass3d_SClassifier::Perform(');if(finite<0)throw Error('finite Perform anchor');
 let tail=out[file].slice(finite);tail=replaceOnce(tail,'if (Intersector3d.IsDone())','if ('+ns+'ObserveDone(Intersector3d.IsDone()))');tail=replaceOnce(tail,'if (Intersector3d.NbPnt() == 0)','if ('+ns+'ObservePoints(Intersector3d.NbPnt()) == 0)');out[file]=out[file].slice(0,finite)+tail;
 change('Extrema_ExtPS aProj(P, aBAS, Precision::PConfusion(), Precision::PConfusion());',timer('profileParallel','ParallelProjection',`,int(${ns}SupportGroup::ParallelProjection),${ns}Active()?int(aBAS.GetType()):-1`)+'\n              Extrema_ExtPS aProj(P, aBAS, Precision::PConfusion(), Precision::PConfusion());');
 file='occt/src/BRepClass3d/BRepClass3d_SolidExplorer.cxx';
 change('      Extrema_ExtPS Ext(P, GA, TolU, TolV);','      '+timer('profileExtrema','SegmentExtrema',`,int(${ns}SupportGroup::SegmentProjection),${ns}Active()?int(GA.GetType()):-1`)+'\n      Extrema_ExtPS Ext(P, GA, TolU, TolV);\n      profileExtrema.Stop();');
 change('      // find point in a face not too far from a projection of P on face\n      do {','      // find point in a face not too far from a projection of P on face\n      '+timer('profileTrim','SegmentTrimSearch')+'\n      do {\n        '+ns+'Count('+ns+'Counter::TrimIterations);');
 change('      while(IndexPoint<200 && NbPointsOK<16);','      while(IndexPoint<200 && NbPointsOK<16);\n      profileTrim.Stop();');
 file='occt/src/IntCurvesFace/IntCurvesFace_Intersector.cxx';
 const start=out[file].indexOf('void IntCurvesFace_Intersector::Perform(const gp_Lin& L,'),end=out[file].indexOf('void IntCurvesFace_Intersector::Perform(',start+10);if(start<0||end<0)throw Error('intersector method anchors');
 let method=out[file].slice(start,end);
 method=replaceOnce(method,'  done = Standard_True;','  '+timer('profileCore','IntersectorCore',`,int(${ns}SupportGroup::Intersector),${ns}Active()?int(Hsurface->GetType()):-1`)+'\n  done = Standard_True;');
 method=replaceOnce(method,'    HICS.Perform(HLL,Hsurface);','    '+timer('profileElementary','ElementaryIntersector')+'\n    HICS.Perform(HLL,Hsurface);');
 method=replaceOnce(method,'    Intf_Tool bndTool;','    '+timer('profileGeneric','GenericIntersector')+'\n    Intf_Tool bndTool;');
 method=replaceOnce(method,'      PtrOnBndBounding = (Bnd_BoundSortBox *) new Bnd_BoundSortBox();','      '+ns+'Count('+ns+'Counter::LazyBounds);\n      PtrOnBndBounding = (Bnd_BoundSortBox *) new Bnd_BoundSortBox();');out[file]=out[file].slice(0,start)+method+out[file].slice(end);
 file='occt/src/BRepClass3d/BRepClass3d_BndBoxTree.hxx';
 change('    return (theBox.IsOut (myL));','    '+timer('profileBox','LineBoxReject')+'\n    const Standard_Boolean profileRejected = (theBox.IsOut (myL));\n    profileBox.Stop();\n    '+ns+'Count('+ns+'Counter::LineBoxes);\n    if (profileRejected) '+ns+'Count('+ns+'Counter::LineBoxesRejected);\n    return profileRejected;');
 file='occt/src/BRepClass3d/BRepClass3d_BndBoxTree.cxx';
 const acceptStart=out[file].indexOf('Standard_Boolean BRepClass3d_BndBoxTreeSelectorLine::Accept (const Standard_Integer& theObj)');if(acceptStart<0)throw Error('line Accept anchor');
 let accept=out[file].slice(acceptStart);
 accept=replaceOnce(accept,'  //box-line collision','  '+timer('profileAccept','LineAccept')+'\n  //box-line collision');
 accept=replaceOnce(accept,'  TopAbs_ShapeEnum sht = shp.ShapeType();','  TopAbs_ShapeEnum sht = shp.ShapeType();\n  '+ns+'Count(sht==TopAbs_EDGE?'+ns+'Counter::LineEdgeCandidates:sht==TopAbs_VERTEX?'+ns+'Counter::LineVertexCandidates:'+ns+'Counter::LineOtherCandidates);');
 accept=replaceOnce(accept,'    const TopoDS_Edge& E = TopoDS::Edge(shp);','    '+timer('profileEdgePrepare','LineEdgePrepare')+'\n    const TopoDS_Edge& E = TopoDS::Edge(shp);');
 accept=replaceOnce(accept,'    BRep_Tool::Range(E, f, l);','    BRep_Tool::Range(E, f, l);\n    profileEdgePrepare.Stop();');
 const constructor='    Extrema_ExtCC ExtCC(C, myLC, f, l, myLC.FirstParameter(), myLC.LastParameter());';
 accept=replaceOnce(accept,constructor,'    '+timer('profileEdgeExtrema','LineEdgeExtrema',`,int(${ns}SupportGroup::LineEdgeExtrema),${ns}Active()?int(C.GetType()):-1`)+'\n'+constructor+'\n    profileEdgeExtrema.Stop();\n    '+timer('profileEdgeResults','LineEdgeResults'));
 accept=replaceOnce(accept,'if (ExtCC.IsDone())','if ('+ns+'ObserveLineDone(ExtCC.IsDone()))');
 accept=replaceOnce(accept,'if (ExtCC.IsParallel())','if ('+ns+'ObserveLineParallel(ExtCC.IsParallel()))');
 accept=replaceOnce(accept,'else if (ExtCC.NbExt() > 0)','else if ('+ns+'ObserveLineNbExt(ExtCC.NbExt()) > 0)');
 accept=replaceOnce(accept,'            myEP.Append(EP);','            myEP.Append(EP);\n            '+ns+'Count('+ns+'Counter::LineNearEdgeExtrema);');
 accept=replaceOnce(accept,'        if (IsInside)\n          return Standard_True;','        if (IsInside)\n          return '+ns+'ObserveLineEdgeAccepted(Standard_True);');
 accept=replaceOnce(accept,'    const TopoDS_Vertex &V = TopoDS::Vertex(shp);','    '+timer('profileVertex','LineVertex')+'\n    const TopoDS_Vertex &V = TopoDS::Vertex(shp);');
 accept=replaceOnce(accept,'        myVP.Append(VP);\n        return Standard_True;','        myVP.Append(VP);\n        return '+ns+'ObserveLineVertexAccepted(Standard_True);');out[file]=out[file].slice(0,acceptStart)+accept;
 file='occt/src/Extrema/Extrema_ECC_0.cxx';
 change('#include <Extrema_GenExtCC.gxx>','#include "../BRepClass3d/kernel-target-query-profile.hpp"\n#define MALIEV_TARGET_PROFILE_ECC3D\n#include <Extrema_GenExtCC.gxx>\n#undef MALIEV_TARGET_PROFILE_ECC3D');
 file='occt/src/Extrema/Extrema_GenExtCC.gxx';
 const guarded=body=>'#ifdef MALIEV_TARGET_PROFILE_ECC3D\n'+body+'\n#endif\n';
 change('void Extrema_GenExtCC::Perform()\n{','void Extrema_GenExtCC::Perform()\n{\n'+guarded('  '+timer('profileGenericEcc3d','GenericEcc3d')));
 change('  Curve1 &C1 = *(Curve1*)myC[0];',guarded('  '+timer('profileGenericEccPrepare','GenericEccPrepare'))+'  Curve1 &C1 = *(Curve1*)myC[0];');
 for(const rank of [1,2]){const original=`    anL[${rank-1}] = GCPnts_AbscissaPoint::Length(C${rank});`,name='profileGenericEccLength'+rank,phase='GenericEccLength'+rank;
  change(original,guarded('    '+timer(name,phase,`,int(${ns}SupportGroup::${phase}),${ns}Active()?int(C${rank}.GetType()):-1`))+original+'\n'+guarded('    '+name+'.Stop();'));
 }
 change('  for(i = 1; i <= aNbInter[0]; i++)',guarded('  profileGenericEccPrepare.Stop();')+'  for(i = 1; i <= aNbInter[0]; i++)');
 change('      aFinder.Perform(GetSingleSolutionFlag());',guarded('      '+timer('profileGenericEccFinder','GenericEccFinder'))+'      aFinder.Perform(GetSingleSolutionFlag());\n'+guarded('      profileGenericEccFinder.Stop();'));
 return out;
}
function apply(root){
 for(const [folder,revision]of [['occt-import-js','c2148e54b456b571238d35cac037d304053d64b2'],['occt','d2abb6d844231cb8f29be6894440874a4700e4a5']])if(cp.execFileSync('git',['-C',path.join(root,folder),'rev-parse','HEAD'],{encoding:'utf8'}).trim()!==revision)throw Error('Unexpected upstream '+folder);
 const input=Object.fromEntries(Object.keys(expected).map(f=>[f,fs.readFileSync(path.join(root,f),'utf8')])),output=instrument(input),header=fs.readFileSync(path.join(__dirname,'kernel-target-query-profile.hpp'));
 // All revisions, hashes and marker counts validate before any composed write.
 for(const [f,text]of Object.entries(output))fs.writeFileSync(path.join(root,f),text);
 for(const dir of ['occt/src/BRepClass3d','occt-import-js/src'])fs.writeFileSync(path.join(root,dir,'kernel-target-query-profile.hpp'),header);
 console.log('Applied explicit profiling-only overlay to ten composed targets; identical header copies '+hash(header));
}
module.exports={expected,instrument,replaceOnce,apply};if(require.main===module)apply(path.resolve(process.argv[2]||'.'));
