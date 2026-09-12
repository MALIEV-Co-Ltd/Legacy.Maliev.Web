// Exact pinned-source transformations: abort on drift, never silently patch another version.
const fs = require('node:fs');
const path = require('node:path');
const child = require('node:child_process');
const source = path.resolve(process.argv[2]);
const revision = child.execFileSync('git', ['-C', source, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
if (revision !== 'c2148e54b456b571238d35cac037d304053d64b2') throw Error('Unexpected upstream revision');
function change(name, replacements) {
  const file = path.join(source, 'occt-import-js/src', name);
  let text = fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n');
  for (const [before, after] of replacements) {
    if (text.split(before).length !== 2) throw Error(`Source drift or overlay already applied: ${name}`);
    text = text.replace(before, after);
  }
  fs.writeFileSync(file, text);
}
change('importer-utils.hpp', [['class OcctFace : public Face',
  'struct KernelMeshSource {\n    virtual const TopoDS_Shape& KernelShape() const = 0;\n    virtual bool IsStandaloneFaceGroup() const = 0;\n    virtual ~KernelMeshSource() = default;\n};\n\nclass OcctFace : public Face'], ['    OcctFace (const TopoDS_Face& face);',
  '    OcctFace (const TopoDS_Face& face);\n    const TopoDS_Face& KernelFace () const { return face; }']]);
for (const [file, prefix] of [['importer-xcaf.cpp', 'Xcaf'], ['importer-brep.cpp', 'Brep']]) {
  change(file, ['ShapeMesh', 'StandaloneFacesMesh'].map(suffix => {
    const name = prefix + suffix;
    return [`class ${name} : public Mesh\n{\npublic:`,
      `class ${name} : public Mesh, public KernelMeshSource\n{\npublic:\n    const TopoDS_Shape& KernelShape() const override { return shape; }\n    bool IsStandaloneFaceGroup() const override { return ${suffix === 'StandaloneFacesMesh'}; }`];
  }));
}
change('js-interface.cpp', [
  ['#include <emscripten/bind.h>', '#include <emscripten/bind.h>\n#include "kernel-face.hpp"\n#include "kernel-body.hpp"'],
  ['            int brepFaceCount = 0;',
   '            int brepFaceCount = 0;\n            MalievKernel::ExportContext kernelContext(mMeshCount);'],
  ['                brepFaceObj.set ("first", triangleOffset);',
   '                MalievKernel::WriteFace(face, brepFaceObj, mMeshCount, brepFaceCount, kernelContext);\n                brepFaceObj.set ("first", triangleOffset);'],
  ['            meshObj.set ("brep_faces", brepFaceArr);',
   '            meshObj.set ("brep_faces", brepFaceArr);\n            kernelContext.Finish(meshObj);\n            MalievKernel::WriteBody(mesh, meshObj, kernelContext);\n            meshObj.set ("bodyId", "body-" + std::to_string(mMeshCount));'],
  ['    resultObj.set ("root", rootNodeObj);', `    emscripten::val provenance = emscripten::val::object();
    provenance.set("schema", std::string("MalievKernelFaces.v1"));
    provenance.set("identityScope", std::string("single-import-result"));
    provenance.set("completeCadDocument", false);
    const bool sourceUnitsUnknown = dynamic_cast<ImporterBrep*>(importer.get()) != nullptr;
    provenance.set("millimeterOutput", sourceUnitsUnknown ? emscripten::val::null()
        : emscripten::val(params.linearUnit == ImportParams::LinearUnit::Millimeter));
    provenance.set("unitsStatus", std::string(sourceUnitsUnknown ? "unverified" : "normalized-import"));
    provenance.set("unitsPolicy", std::string("BREP-requires-source-unit-evidence-and-normalization"));
    provenance.set("source", std::string("c2148e54b456b571238d35cac037d304053d64b2+maliev-direct-face-v1"));
    resultObj.set("kernelProvenance", provenance);
    resultObj.set ("root", rootNodeObj);`]
]);
fs.copyFileSync(path.join(__dirname, 'kernel-face.hpp'), path.join(source, 'occt-import-js/src/kernel-face.hpp'));
fs.copyFileSync(path.join(__dirname, 'kernel-trims.hpp'), path.join(source, 'occt-import-js/src/kernel-trims.hpp'));
fs.copyFileSync(path.join(__dirname, 'kernel-body.hpp'), path.join(source, 'occt-import-js/src/kernel-body.hpp'));
child.execFileSync(process.execPath, [path.join(__dirname, 'apply-document-overlay.cjs'), source], { stdio: 'inherit' });
