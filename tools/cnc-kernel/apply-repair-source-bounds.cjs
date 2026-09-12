const fs = require('node:fs');
const path = require('node:path');
const file = path.join(path.resolve(process.argv[2]), 'occt/src/StepToTopoDS/StepToTopoDS_TranslateFace.cxx');
let source = fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n');
const declaration = '    Loop      = FaceBound->Bound();';
if (source.split(declaration).length !== 2 || source.includes('MalievRepair::SourceBoundDeclaration'))
  throw Error('Source face-bound declaration drift or repeat');
source = source.replace(declaration, declaration + '\n    MalievRepair::SourceBoundDeclaration(F, TP->Model()->Number(FS), TP->Model()->Number(FaceBound), TP->Model()->Number(Loop), FaceBound->IsKind(STANDARD_TYPE(StepShape_FaceOuterBound)), FaceBound->Orientation(), FS->SameSense(), sameSense);');
const mapping = 'B.Add(F, W);';
if (source.split(mapping).length !== 3) throw Error('Source face-bound mapping drift');
source = source.replaceAll(mapping, 'MalievRepair::SourceBoundMapped(TP->Model()->Number(FS), TP->Model()->Number(FaceBound), W, F.Orientation(), Loop->DynamicType()->Name());\n      B.Add(F, W);');
fs.writeFileSync(file, source);
