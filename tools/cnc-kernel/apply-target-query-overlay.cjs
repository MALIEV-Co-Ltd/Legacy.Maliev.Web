// Run after the exact document/repair overlays; only the opt-in import retains a solid.
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process');
const root=path.resolve(process.argv[2]);
if(cp.execFileSync('git',['-C',root,'rev-parse','HEAD'],{encoding:'utf8'}).trim()!=='c2148e54b456b571238d35cac037d304053d64b2')throw Error('Unexpected upstream revision');
const file=path.join(root,'occt-import-js/src/js-interface.cpp');let text=fs.readFileSync(file,'utf8').replace(/\r\n/g,'\n');
function change(before,after){if(text.split(before).length!==2)throw Error('Target-query source drift/repeated overlay: '+before);text=text.replace(before,after);}
change('#include "kernel-repair-export.hpp"','#include "kernel-repair-export.hpp"\n#include "kernel-target-query-session.hpp"');
change('HierarchyWriter (emscripten::val& meshesArr, MalievKernel::DocumentCoverage& document) :','HierarchyWriter (emscripten::val& meshesArr, MalievKernel::DocumentCoverage& document, MalievKernel::TargetQuery::Capture* capture) :\n        mTargetCapture(capture),');
change('    MalievKernel::DocumentCoverage& mDocument;','    MalievKernel::TargetQuery::Capture* mTargetCapture;\n    MalievKernel::DocumentCoverage& mDocument;');
change('            mDocument.Associate(mesh, meshObj, kernelContext);',`            mDocument.Associate(mesh, meshObj, kernelContext);
            if (mTargetCapture) {
                MalievKernel::TargetQuery::Inventory inventory;
                inventory.bodyId = kernelContext.prefix;
                for (size_t i = 0; i < kernelContext.sourceFaces.size(); ++i)
                    inventory.faces.emplace_back(kernelContext.sourceFaces[i], kernelContext.sourceFaceRecords[i]["faceId"].as<std::string>());
                for (int i = 1; i <= kernelContext.edges.Extent(); ++i)
                    inventory.edges.emplace_back(kernelContext.edges(i), kernelContext.prefix + "/edge-" + std::to_string(i));
                for (int i = 1; i <= kernelContext.vertices.Extent(); ++i)
                    inventory.vertices.emplace_back(kernelContext.vertices(i), kernelContext.prefix + "/vertex-" + std::to_string(i));
                const KernelMeshSource* source = dynamic_cast<const KernelMeshSource*>(&mesh);
                mTargetCapture->Occurrence(source ? source->KernelShape() : TopoDS_Shape(), inventory,
                    source && !source->IsStandaloneFaceGroup() && kernelContext.complete);
            }`);
change('static emscripten::val ImportFile (ImporterPtr importer, const emscripten::val& buffer, const ImportParams& params)','static emscripten::val ImportFile (ImporterPtr importer, const emscripten::val& buffer, const ImportParams& params, MalievKernel::TargetQuery::Capture* capture = nullptr)');
change('    Importer::Result importResult = importer->LoadFile (bufferArr, params);','    if (capture) capture->Source(bufferArr);\n    Importer::Result importResult = importer->LoadFile (bufferArr, params);');
change('    HierarchyWriter hierarchyWriter (meshesArr, documentCoverage);','    HierarchyWriter hierarchyWriter (meshesArr, documentCoverage, capture);');
change('    resultObj.set ("meshes", meshesArr);','    resultObj.set ("meshes", meshesArr);\n    if (capture) resultObj.set("nativeTargetSession", capture->Finish(provenance));');
change('emscripten::val ReadIgesFile (',`emscripten::val ReadStepFileWithTarget (const emscripten::val& buffer, const emscripten::val& params)
{
    MalievKernel::TargetQuery::Capture capture;
    MalievRepair::ConfigureOptions(params);
    ImporterPtr importer = std::make_shared<ImporterStep> ();
    ImportParams importParams = GetImportParams (params);
    emscripten::val result = ImportFile(importer, buffer, importParams, &capture);
    if (!result.hasOwnProperty("nativeTargetSession"))
        result.set("nativeTargetSession", MalievKernel::TargetQuery::Unavailable("import_not_completed"));
    capture.Commit();
    return result;
}

emscripten::val ReadIgesFile (`);
change('EMSCRIPTEN_BINDINGS (occtimportjs)\n{',`EMSCRIPTEN_BINDINGS (occtimportjs)
{
    emscripten::function("ReadStepFileWithTarget", &ReadStepFileWithTarget);
    emscripten::function("BindNativeTargetSource", &MalievKernel::TargetQuery::BindNativeTargetSource);
    emscripten::function("QueryNativeTargetBatch", &MalievKernel::TargetQuery::QueryNativeTargetBatch);
    emscripten::function("ReleaseNativeTarget", &MalievKernel::TargetQuery::ReleaseNativeTarget);`);
fs.writeFileSync(file,text);
for(const name of ['kernel-target-query.hpp','kernel-target-query-session.hpp'])fs.copyFileSync(path.join(__dirname,name),path.join(root,'occt-import-js/src',name));
