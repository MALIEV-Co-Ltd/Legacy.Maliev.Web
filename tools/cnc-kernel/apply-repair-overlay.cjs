// Exact source hooks; observational only, default transfer behavior is retained.
const fs = require('node:fs');
const path = require('node:path');
const source = path.resolve(process.argv[2]);
function change(file, pairs) {
  const p = path.join(source, file);
  let text = fs.readFileSync(p, 'utf8').replace(/\r\n/g, '\n');
  for (const [before, after] of pairs) {
    if (text.split(before).length !== 2) throw Error(`Source drift or repeat overlay: ${file}: ${before}`);
    text = text.replace(before, after);
  }
  fs.writeFileSync(p, text);
}
const header = '#include "../../../occt-import-js/src/kernel-repair.hpp"\n';
if (!process.argv.includes('--integration-only')) {
change('occt/src/STEPControl/STEPControl_ActorRead.cxx', [
  ['#include <STEPControl_ActorRead.hxx>',header+'#include <STEPControl_ActorRead.hxx>'],
  ['      Handle(Standard_Transient) info;\n      mappedShape =', '      Handle(Standard_Transient) info;\n      MalievRepair::Begin(mappedShape, TP, start);\n      mappedShape ='],
  ['      XSAlgo::AlgoContainer()->MergeTransferInfo(TP, info, nbTPitems);\n    }\n  }\n  found =', '      MalievRepair::End(mappedShape, info);\n      XSAlgo::AlgoContainer()->MergeTransferInfo(TP, info, nbTPitems);\n    }\n  }\n  found =']
]);
change('occt/src/ShapeProcess/ShapeProcess_ShapeContext.cxx', [
  ['#include <ShapeProcess_ShapeContext.hxx>',header+'#include <ShapeProcess_ShapeContext.hxx>'],
  ['                                                    const Handle(ShapeExtend_MsgRegistrator) &msg)\n{\n  \n  RecModif ( myShape, repl, msg, myMap, myMsg, myUntil );', '                                                    const Handle(ShapeExtend_MsgRegistrator) &msg)\n{\n  MalievRepair::Replacements(repl);\n  RecModif ( myShape, repl, msg, myMap, myMsg, myUntil );']
]);
change('occt/src/StepToTopoDS/StepToTopoDS_TranslateFace.cxx', [
  ['#include <StepToTopoDS_TranslateFace.hxx>',header+'#include <StepToTopoDS_TranslateFace.hxx>'],
  ['    Handle(Geom_Surface) periodicSurf = ShapeAlgo::AlgoContainer()->ConvertToPeriodic(GeomSurf);','    const std::string malievOriginalSurface = MalievRepair::Geometry(GeomSurf);\n    Handle(Geom_Surface) periodicSurf = ShapeAlgo::AlgoContainer()->ConvertToPeriodic(GeomSurf);'],
  ['      TP->AddWarning(StepSurf, "Surface forced to be periodic");','      const auto malievPeriodicToken = MalievRepair::PeriodicChange(malievOriginalSurface, periodicSurf, TP->Model()->Number(StepSurf), TP->Model()->Number(FS));\n      const int malievPeriodicWarnings = MalievNativeWarning::Store().enabled ? MalievRepair::WarningCount(TP, StepSurf) : 0;\n      TP->AddWarning(StepSurf, "Surface forced to be periodic");\n      MalievRepair::PeriodicWarningDelivered(TP, StepSurf, malievPeriodicWarnings, malievPeriodicToken);']
]);
change('occt-import-js/src/importer-step.cpp', [
  ['#include "importer-step.hpp"','#include "importer-step.hpp"\n#include "kernel-repair.hpp"'],
  ['    STEPCAFControl_Reader stepCafReader;','    MalievRepair::Reset();\n    STEPCAFControl_Reader stepCafReader;']
]);
for (const file of ['kernel-repair.hpp','kernel-repair-export.hpp'])
  fs.copyFileSync(path.join(__dirname,file),path.join(source,'occt-import-js/src',file));
change('occt-import-js/src/js-interface.cpp', [
  ['#include "kernel-document.hpp"','#include "kernel-document.hpp"\n#include "kernel-repair-export.hpp"'],
  ['    documentCoverage.Finish(provenance);','    documentCoverage.Finish(provenance);\n    MalievRepair::Export(provenance);']
]);
}
change('occt-import-js/src/js-interface.cpp', [
  ['    ImporterPtr importer = std::make_shared<ImporterStep> ();', '    MalievRepair::ConfigureOptions(params);\n    ImporterPtr importer = std::make_shared<ImporterStep> ();'],
  ['    ImporterPtr importer = std::make_shared<ImporterIges> ();', '    MalievRepair::Configure(0, false);\n    ImporterPtr importer = std::make_shared<ImporterIges> ();'],
  ['    ImporterPtr importer = std::make_shared<ImporterBrep> ();', '    MalievRepair::Configure(0, false);\n    ImporterPtr importer = std::make_shared<ImporterBrep> ();']
]);
const wireFile = path.join(source, 'occt/src/ShapeFix/ShapeFix_Wire.cxx');
let wire = fs.readFileSync(wireFile, 'utf8').replace(/\r\n/g, '\n');
const warning = '    SendWarning ( Message_Msg ( "FixAdvWire.FixIntersection.MSG10" ) );// Edges were intersecting, corrected';
if (wire.split(warning).length !== 3 || wire.includes('MalievRepair::IntersectionOperation')) throw Error('Intersection emitter drift');
wire = wire.replace('#include <ShapeFix_Wire.hxx>',header+'#include <ShapeFix_Wire.hxx>');
for (const branch of ['adjacent','nonadjacent']) wire = wire.replace(warning,
  `    const Message_Msg malievMessage("FixAdvWire.FixIntersection.MSG10");\n    MalievRepair::IntersectionOperation(myShape, myLastFixStatus, "${branch}", malievMessage, Face(), Context());\n    SendWarning(malievMessage);`);
fs.writeFileSync(wireFile, wire);
require('node:child_process').execFileSync(process.execPath,[path.join(__dirname,'apply-repair-source-bounds.cjs'),source],{stdio:'inherit'});
require('node:child_process').execFileSync(process.execPath,[path.join(__dirname,'apply-warning-lineage-overlay.cjs'),source],{stdio:'inherit'});
fs.copyFileSync(path.join(__dirname,'kernel-native-interpretation-export.hpp'),path.join(source,'occt-import-js/src/kernel-native-interpretation-export.hpp'));
for (const file of ['kernel-repair.hpp','kernel-repair-export.hpp','kernel-repair-bounds.hpp','kernel-repair-correlated.hpp','kernel-repair-endpoints.hpp','kernel-repair-policy.hpp','kernel-repair-domains.hpp','kernel-repair-cone-region.hpp','kernel-repair-composition.hpp','kernel-repair-cylinder-composition.hpp','kernel-repair-rational-composition.hpp','kernel-repair-high-axis-composition.hpp','kernel-repair-residual.hpp','kernel-native-interpretation-audit.hpp','kernel-native-interpretation-checks.hpp','kernel-native-interpretation-ledger.hpp','kernel-native-warning-lineage.hpp'])
  fs.copyFileSync(path.join(__dirname,file),path.join(source,'occt-import-js/src',file));
