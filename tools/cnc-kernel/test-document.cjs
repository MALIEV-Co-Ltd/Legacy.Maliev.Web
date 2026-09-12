const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const near = (a, b) => assert.ok(Math.abs(a - b) < 1e-7, `${a} != ${b}`);
(async () => {
  const candidate = path.resolve(process.argv[2]);
  const fixtures = path.resolve(process.argv[3]);
  const diagnostics = [];
  const api = await require(candidate)({ wasmBinary: fs.readFileSync(candidate.replace(/\.js$/, '.wasm')), print: x => diagnostics.push(x), printErr: x => diagnostics.push(x) });
  const read = text => api.ReadStepFile(Buffer.from(text), { linearUnit: 'millimeter', linearDeflectionType: 'absolute_value', linearDeflection: 0.1 });
  const source = fs.readFileSync(path.resolve(__dirname, '../../Maliev.Web.Tests/TestAssets/Cnc/box-20x30x40.step'), 'utf8').replace(/\r\n/g, '\n');
  const coverage = result => {
    assert.equal(result.success, true);
    const d = result.kernelProvenance.documentCoverage;
    assert.equal(d.schema, 'MalievKernelDocument.v1');
    assert.equal(d.geometricValidityIsSeparate, true);
    assert.equal(result.kernelProvenance.completeCadDocument, d.status === 'complete_supported_single_solid');
    assert.equal(d.occurrenceCount, d.occurrences.length);
    assert.equal(d.freeRootCount, d.roots.length);
    assert.equal(d.emittedBodyCount, result.meshes.length);
    assert.equal(d.emittedFaceCount, result.meshes.reduce((sum, m) => sum + m.brep_faces.length, 0));
    assert.equal(d.sourceTransfer.diagnostics.length, d.sourceTransfer.warningCount + d.sourceTransfer.failureCount);
    for (const diagnostic of d.sourceTransfer.diagnostics) {
      assert.ok(Number.isInteger(diagnostic.entityNumber) && diagnostic.entityNumber >= 0);
      assert.equal(typeof diagnostic.message, 'string');
      assert.equal(typeof diagnostic.originalMessage, 'string');
      assert.equal(diagnostic.category, 'unclassified-native-transfer');
    }
    return d;
  };
  const cube = read(source), d = coverage(cube);
  assert.equal(cube.kernelProvenance.completeCadDocument, true, JSON.stringify(d));
  assert.equal(d.freeRootCount, 1); assert.equal(d.leafOccurrenceCount, 1);
  assert.equal(d.sourceTransfer.rootCount, 1); assert.equal(d.sourceTransfer.missingRootCount, 0);
  assert.equal(d.sourceFaceCount, 6); assert.equal(d.faceCoverageStatus, 'complete');
  assert.deepEqual(d.occurrences[0].bodyIds, [cube.meshes[0].bodyId]);
  assert.deepEqual(d.occurrences[0].sourceFaces.flatMap(f => f.emittedFaceIds).sort(), cube.meshes[0].brep_faces.map(f => f.faceId).sort());
  for (const f of cube.meshes[0].brep_faces) assert.equal(f.assemblyInstance.occurrenceId, 'occurrence-0');

  const assemblySource = fs.readFileSync(path.join(fixtures, 'cube-units/cube-mm.step'), 'utf8').replace(/\r\n/g, '\n');
  const assembly = read(assemblySource), ad = coverage(assembly);
  assert.equal(assembly.kernelProvenance.completeCadDocument, true, JSON.stringify(ad));
  assert.ok(ad.occurrences.some(o => o.occurrenceKind === 'assembly'));
  assert.equal(ad.leafOccurrenceCount, 1); assert.equal(ad.faceCoverageStatus, 'complete');
  // Real STEP instance placement: rotate 90 degrees about Z and translate.
  const placedText = assemblySource.replace("#16 = CARTESIAN_POINT('',(0.,0.,0.));", "#16 = CARTESIAN_POINT('',(17.,23.,31.));")
    .replace("#18 = DIRECTION('',(1.,0.,0.));", "#18 = DIRECTION('',(0.,1.,0.));");
  assert.notEqual(placedText, assemblySource);
  const placed = read(placedText), pd = coverage(placed);
  assert.equal(placed.kernelProvenance.completeCadDocument, true);
  assert.equal(pd.faceCoverageStatus, 'complete');
  const leaf = pd.occurrences.find(o => o.leaf);
  assert.equal(leaf.sourceKind, 'solid');
  [0, -1, 0, 17, 1, 0, 0, 23, 0, 0, 1, 31].forEach((v, i) => near(leaf.worldPlacement3x4[i], v));
  const before = assembly.meshes[0].attributes.position.array, after = placed.meshes[0].attributes.position.array;
  assert.equal(before.length, after.length);
  for (let i = 0; i < before.length; i += 3) {
    near(after[i], 17 - before[i + 1]); near(after[i + 1], 23 + before[i]); near(after[i + 2], 31 + before[i + 2]);
  }
  for (const face of placed.meshes[0].brep_faces) {
    assert.equal(face.support.coordinateSpace, 'import-world');
    for (let triangle = face.first; triangle <= face.last; triangle++) for (let corner = 0; corner < 3; corner++) {
      const offset = placed.meshes[0].index.array[triangle * 3 + corner] * 3;
      const point = after.slice(offset, offset + 3);
      near(point.reduce((sum, v, axis) => sum + (v - face.support.origin[axis]) * face.support.axis[axis], 0), 0);
    }
  }
  // Two occurrences reference the SAME native definition at distinct placements.
  const duplicateText = assemblySource.replace('ENDSEC;\nEND-ISO', `#900 = CONTEXT_DEPENDENT_SHAPE_REPRESENTATION(#901,#903);
#901 = ( REPRESENTATION_RELATIONSHIP('','',#32,#10) REPRESENTATION_RELATIONSHIP_WITH_TRANSFORMATION(#902) SHAPE_REPRESENTATION_RELATIONSHIP() );
#902 = ITEM_DEFINED_TRANSFORMATION('','',#11,#905);
#903 = PRODUCT_DEFINITION_SHAPE('Placement','',#904);
#904 = NEXT_ASSEMBLY_USAGE_OCCURRENCE('2','Cube duplicate','',#5,#27,$);
#905 = AXIS2_PLACEMENT_3D('',#906,#17,#18);
#906 = CARTESIAN_POINT('',(1500.,0.,0.));
ENDSEC;
END-ISO`);
  assert.notEqual(duplicateText, assemblySource);
  const duplicate = read(duplicateText), dd = coverage(duplicate);
  assert.equal(dd.leafOccurrenceCount, 2); assert.equal(duplicate.meshes.length, 2);
  assert.equal(duplicate.kernelProvenance.completeCadDocument, false);
  assert.equal(dd.faceCoverageStatus, 'complete');
  assert.equal(new Set(dd.occurrences.filter(o => o.leaf).map(o => o.definitionLabel)).size, 1);
  assert.equal(new Set(duplicate.meshes.flatMap(m => m.brep_faces.map(f => f.faceId))).size, 12);
  const coincident = read(duplicateText.replace("#906 = CARTESIAN_POINT('',(1500.,0.,0.));", "#906 = CARTESIAN_POINT('',(0.,0.,0.));"));
  const cd = coverage(coincident);
  assert.equal(cd.leafOccurrenceCount, 2);
  assert.equal(coincident.kernelProvenance.completeCadDocument, false);
  assert.equal(cd.occurrences.filter(o => o.leaf).length, 2, 'coincident references cannot disappear from the source inventory');

  // Free orphan geometry must prevent certification even if XCAF drops it.
  const leftover = read(source.replace('ENDSEC;\nEND-ISO', "#9999 = CARTESIAN_POINT('orphan',(123.,456.,789.));\nENDSEC;\nEND-ISO"));
  const ld = coverage(leftover);
  assert.equal(leftover.kernelProvenance.completeCadDocument, false);
  assert.ok(ld.sourceTransfer.unsupportedEntityNumbers.length > 0);
  assert.ok(ld.reasons.includes('unsupported_source_entities'));
  const mixedSource = source.replace('(#11,#15),#345', '(#11,#15,#9999),#345')
    .replace('ENDSEC;\nEND-ISO', "#9999 = CARTESIAN_POINT('leftover',(123.,456.,789.));\nENDSEC;\nEND-ISO");
  const mixed = read(mixedSource), md = coverage(mixed);
  assert.equal(mixed.kernelProvenance.completeCadDocument, false);
  assert.ok(md.reasons.includes('unsupported_source_entities'));
  // Unsupported representation item survives as source evidence, not a solid claim.
  const freeFace = read(source.replace("ADVANCED_BREP_SHAPE_REPRESENTATION('',(#11,#15),#345)", "MANIFOLD_SURFACE_SHAPE_REPRESENTATION('',(#11,#15),#345)")
    .replace("MANIFOLD_SOLID_BREP('',#16)", "SHELL_BASED_SURFACE_MODEL('',(#16))")
    .replace(/CLOSED_SHELL\('',\(([^)]*)\)\)/, (_, ids) => `OPEN_SHELL('',(${ids.split(',')[0]}))`));
  const fd = coverage(freeFace);
  assert.equal(freeFace.kernelProvenance.completeCadDocument, false);
  assert.ok(fd.reasons.includes('source_leaf_not_single_solid'));
  assert.equal(fd.sourceFaceCount, 1);
  const standalone = read(source.replace('ADVANCED_BREP_SHAPE_REPRESENTATION', 'MANIFOLD_SURFACE_SHAPE_REPRESENTATION')
    .replace('(#11,#15),#345', '(#11,#17),#345'));
  const sd = coverage(standalone);
  assert.equal(sd.occurrences[0].sourceKind, 'face');
  assert.equal(standalone.meshes[0].kernelBody.sourceKind, 'standalone-face-group');
  assert.equal(standalone.kernelProvenance.completeCadDocument, false);
  const untriangulated = read(source.replace('(#20,#55,#83,#111)', '()'));
  const ud = coverage(untriangulated);
  assert.equal(untriangulated.kernelProvenance.completeCadDocument, false);
  assert.ok(ud.sourceTransfer.failureCount > 0);
  assert.equal(ud.sourceFaceCount, 6); assert.equal(ud.faceCoverageStatus, 'complete');
  const zeroFace = untriangulated.meshes.flatMap(m => m.brep_faces).find(f => f.last < f.first);
  assert.ok(zeroFace, 'zero-triangle face is retained');
  assert.ok(ud.occurrences.flatMap(o => o.sourceFaces).some(f => f.emittedTriangleCount === 0 && f.emittedFaceIds.includes(zeroFace.faceId)));

  const iges = api.ReadIgesFile(fs.readFileSync(path.resolve(__dirname, '../../Maliev.Web.Tests/TestAssets/cube.iges')), null);
  const id = coverage(iges);
  assert.equal(iges.kernelProvenance.millimeterOutput, true);
  assert.equal(id.sourceTransfer.status, 'available'); assert.ok(id.sourceTransfer.rootCount > 0);
  assert.equal(iges.kernelProvenance.completeCadDocument, false);
  assert.ok(id.reasons.includes('iges_source_entity_coverage_unverified'));
  const brep = api.ReadBrepFile(fs.readFileSync(path.join(fixtures, 'cax-if-brep/as1_pe_203.brep')), null);
  const bd = coverage(brep);
  assert.equal(brep.kernelProvenance.millimeterOutput, null);
  assert.equal(brep.kernelProvenance.completeCadDocument, false);
  assert.ok(bd.reasons.includes('source_units_unverified'));
  const badStart = diagnostics.length;
  const bad = read('malformed STEP input');
  assert.equal(bad.success, false); assert.equal(bad.kernelProvenance, undefined);
  assert.ok(diagnostics.slice(badStart).some(x => /error|syntax/i.test(x)));
  console.log('PASS: single solid, placed instance, duplicate definition occurrences, assembly, open face shell, orphan geometry, IGES transfer evidence, BREP units and malformed input');

  if (process.argv[4]) {
    const inventory = [];
    const manifest = require(path.resolve(__dirname, '../../Maliev.Web.Tests/TestAssets/CncAcceptance/manifest.json'));
    for (const original of manifest.reportedUploads) {
      const bytes = fs.readFileSync(path.join(path.resolve(process.argv[4]), original.fileName));
      const hash = crypto.createHash('sha256').update(bytes).digest('hex');
      assert.equal(hash, original.sha256);
      const result = api.ReadStepFile(bytes, { linearUnit: 'millimeter', linearDeflectionType: 'absolute_value', linearDeflection: 0.1 });
      const doc = coverage(result);
      inventory.push({ fileName: original.fileName, completeCadDocument: result.kernelProvenance.completeCadDocument, documentCoverage: doc });
      console.log(JSON.stringify({ fileName: original.fileName, sha256: hash, bytes: bytes.length,
        completeCadDocument: result.kernelProvenance.completeCadDocument, freeRoots: doc.freeRootCount,
        occurrences: doc.occurrenceCount, leaves: doc.leafOccurrenceCount, bodies: doc.emittedBodyCount,
        sourceFaces: doc.sourceFaceCount, emittedFaces: doc.emittedFaceCount, faceCoverage: doc.faceCoverageStatus,
        zeroTriangleFaces: result.meshes.flatMap(m => m.brep_faces).filter(f => f.last < f.first).length,
        sourceKinds: doc.occurrences.map(o => o.sourceKind), transfer: { ...doc.sourceTransfer,
          diagnostics: undefined,
          diagnosticGroups: [...new Set(doc.sourceTransfer.diagnostics.map(d => d.message))].map(message => ({ message,
            originalMessage: doc.sourceTransfer.diagnostics.find(d => d.message === message).originalMessage,
            entityNumbers: doc.sourceTransfer.diagnostics.filter(d => d.message === message).map(d => d.entityNumber),
            sourceEntityLabels: doc.sourceTransfer.diagnostics.filter(d => d.message === message).map(d => d.sourceEntityLabel) })) }, reasons: doc.reasons }));
    }
    // Generated diagnostics only; original CAD bytes and fingerprints are not
    // copied into this artifact. Fingerprints belong in the scoped report.
    if (process.argv[5]) fs.writeFileSync(path.resolve(process.argv[5]), JSON.stringify({ schema: 'MalievNativeDocumentInventory.v1', originals: inventory }, null, 2) + '\n');
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
