// Observational hooks on the pinned native message copy and transfer append path.
const fs = require('node:fs');
const path = require('node:path');
const source = path.resolve(process.argv[2]);
function change(file, pairs) {
  const target = path.join(source, file);
  let text = fs.readFileSync(target, 'utf8').replace(/\r\n/g, '\n');
  for (const [before, after] of pairs) {
    if (text.split(before).length !== 2) throw Error(`Warning lineage source drift: ${file}`);
    text = text.replace(before, after);
  }
  fs.writeFileSync(target, text);
}
const include = '#include "../../../occt-import-js/src/kernel-native-warning-lineage.hpp"\n';
change('occt/src/Message/Message_Msg.cxx', [
  ['#include <Message_Msg.hxx>', include + '#include <Message_Msg.hxx>'],
  ['Message_Msg::Message_Msg ()\n{}', 'Message_Msg::Message_Msg ()\n{ MalievNativeWarning::Constructed(this); }'],
  ['Message_Msg::Message_Msg (const Message_Msg& theMsg)\n{', 'Message_Msg::Message_Msg (const Message_Msg& theMsg)\n{\n  MalievNativeWarning::Copied(this, &theMsg);'],
  ['Message_Msg::Message_Msg (const Standard_CString theMsgCode)\n{', 'Message_Msg::Message_Msg (const Standard_CString theMsgCode)\n{\n  MalievNativeWarning::Constructed(this);'],
  ['Message_Msg::Message_Msg (const TCollection_ExtendedString& theMsgCode)\n{', 'Message_Msg::Message_Msg (const TCollection_ExtendedString& theMsgCode)\n{\n  MalievNativeWarning::Constructed(this);'],
  ['void Message_Msg::Set (const Standard_CString theMsg)\n{', 'void Message_Msg::Set (const Standard_CString theMsg)\n{\n  MalievNativeWarning::Constructed(this);'],
  ['void Message_Msg::Set (const TCollection_ExtendedString& theMsg)\n{', 'void Message_Msg::Set (const TCollection_ExtendedString& theMsg)\n{\n  MalievNativeWarning::Constructed(this);']
]);
change('occt/src/XSAlgo/XSAlgo_AlgoContainer.cxx', [
  ['#include <XSAlgo_AlgoContainer.hxx>', include + '#include <XSAlgo_AlgoContainer.hxx>'],
  ['\t  sb->AddWarning (TCollection_AsciiString(mess.Value()).ToCString(),\n                          TCollection_AsciiString(mess.Original()).ToCString());',
   '          const bool malievCaptureWarning = MalievNativeWarning::Store().enabled;\n          const int malievWarningsBefore = malievCaptureWarning ? sb->Check()->NbWarnings() : 0;\n\t  sb->AddWarning (TCollection_AsciiString(mess.Value()).ToCString(),\n                          TCollection_AsciiString(mess.Original()).ToCString());\n          if (malievCaptureWarning) {\n            const auto malievModel = TP->Model();\n            const auto malievSource = TP->Mapped(i);\n            const int malievEntity = malievModel.IsNull() || malievSource.IsNull() ? 0 : malievModel->Number(malievSource);\n            MalievNativeWarning::Appended(sb->Check(), malievWarningsBefore, MalievNativeWarning::Token(&mess), malievEntity, malievModel, orig, sb->Result());\n          }']
]);
fs.copyFileSync(path.join(__dirname, 'kernel-native-warning-lineage.hpp'), path.join(source, 'occt-import-js/src/kernel-native-warning-lineage.hpp'));
