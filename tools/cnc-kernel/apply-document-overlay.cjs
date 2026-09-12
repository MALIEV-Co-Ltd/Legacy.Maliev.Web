// Also runs against the retained prior overlay for an incremental native build.
const fs = require('node:fs');
const path = require('node:path');
const source = path.resolve(process.argv[2]);
function change(name, pairs) {
  const file = path.join(source, 'occt-import-js/src', name);
  let text = fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n');
  for (const [before, after] of pairs) {
    if (text.split(before).length !== 2) throw Error(`Source drift or overlay already applied: ${name}: ${before}`);
    text = text.replace(before, after);
  }
  fs.writeFileSync(file, text);
}
change('importer-xcaf.hpp', [
  ['#include "importer.hpp"', '#include "importer.hpp"\n#include "kernel-transfer.hpp"'],
  ['    ImporterXcaf ();', '    ImporterXcaf ();\n    const Handle(XCAFDoc_ShapeTool)& KernelShapeTool() const { return shapeTool; }\n    const KernelTransferAudit& KernelAudit() const { return kernelAudit; }'],
  ['    Handle (TDocStd_Document) document;', '    KernelTransferAudit kernelAudit;\n    Handle (TDocStd_Document) document;']
]);
change('importer-step.cpp', [['    return true;\n}', '    kernelAudit = AuditKernelTransfer(stepReader, true);\n    return true;\n}']]);
change('importer-iges.cpp', [['    std::remove (dummyFileName.c_str ());\n    return true;', '    kernelAudit = AuditKernelTransfer(igesCafReader, false);\n    std::remove (dummyFileName.c_str ());\n    return true;']]);
change('js-interface.cpp', [
  ['#include "kernel-body.hpp"', '#include "kernel-body.hpp"\n#include "kernel-document.hpp"'],
  ['HierarchyWriter (emscripten::val& meshesArr) :', 'HierarchyWriter (emscripten::val& meshesArr, MalievKernel::DocumentCoverage& document) :\n        mDocument(document),'],
  ['    emscripten::val& mMeshesArr;', '    MalievKernel::DocumentCoverage& mDocument;\n    emscripten::val& mMeshesArr;'],
  ['                MalievKernel::WriteFace(face, brepFaceObj, mMeshCount, brepFaceCount, kernelContext);', '                MalievKernel::WriteFace(face, brepFaceObj, mMeshCount, brepFaceCount, kernelContext, mDocument.MeasuresMillimeterOutput());'],
  ['            MalievKernel::WriteBody(mesh, meshObj, kernelContext);', '            MalievKernel::WriteBody(mesh, meshObj, kernelContext, mDocument.MeasuresMillimeterOutput());\n            mDocument.Associate(mesh, meshObj, kernelContext);'],
  ['    HierarchyWriter hierarchyWriter (meshesArr);', '    MalievKernel::DocumentCoverage documentCoverage(importer.get(), params);\n    HierarchyWriter hierarchyWriter (meshesArr, documentCoverage);'],
  ['    resultObj.set("kernelProvenance", provenance);', '    documentCoverage.Finish(provenance);\n    resultObj.set("kernelProvenance", provenance);']
]);
for (const file of ['kernel-document.hpp', 'kernel-transfer.hpp'])
  fs.copyFileSync(path.join(__dirname, file), path.join(source, 'occt-import-js/src', file));
