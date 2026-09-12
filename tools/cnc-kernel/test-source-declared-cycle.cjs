'use strict';
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),{createHash}=require('node:crypto');
const hash=bytes=>createHash('sha256').update(bytes).digest('hex');
(async()=>{
 const candidate=path.resolve(process.argv[2]),assets=path.resolve(process.argv[3]);
 const kernel=await require(candidate)();let positive=0,retainedIncomplete=0;
 for(const manifestName of ['analytic-role-fixtures.json','analytic-primitive-fixtures.json']){
  const manifest=JSON.parse(fs.readFileSync(path.join(assets,manifestName)));
  assert.equal(hash(fs.readFileSync(path.join(assets,manifest.generator))),manifest.generatorSha256);
  for(const fixture of manifest.sources){
   const bytes=fs.readFileSync(path.join(assets,fixture.file));assert.equal(hash(bytes),fixture.sha256);
   const start=performance.now(),r=kernel.ReadStepFile(bytes,{linearUnit:'millimeter',linearDeflectionType:'absolute_value',linearDeflection:.1,angularDeflection:.5,repairErrorBudgetMm:.01,repairDiagnostics:true});
   assert.equal(r.success,true,fixture.file);const n=r.kernelProvenance.nativeInterpretation;
   console.log(JSON.stringify({source:fixture.file,sourceSha256:fixture.sha256,elapsedMs:performance.now()-start,status:n.status,reasons:n.reasons,metrics:{required:n.metrics.required,bounded:n.metrics.bounded},nativeChecks:{required:n.nativeChecks.required,passed:n.nativeChecks.passed}}));
   assert.notEqual(n.status,'review-required',fixture.file+' must actually pass interpretation');
   assert.equal(n.metrics.bounded,n.metrics.required);assert.equal(n.nativeChecks.passed,n.nativeChecks.required);
   assert.ok(n.sourceBounds.every(b=>b.composedCoedgeCyclePreserved&&b.sourceCycleProof.complete));
   for(const b of n.sourceBounds)if(!b.cycleDiagnostics.beforeComplete){
    retainedIncomplete++;assert.equal(b.sourceCycleProof.method,'certified-step-oriented-edge-loop');
    assert.equal(b.sourceCycleProof.sourceDeclarationComplete,true);assert.ok(b.sourceCycleProof.declaredOccurrences.length>0);
   }
   assert.equal(n.manufacturingEligibility,'not-assessed');positive++;
  }
 }
 assert.equal(positive,36);assert.ok(retainedIncomplete>0);
 console.log('PASS: '+positive+' exact public normal/placed/reversed native positives including the three original circular revolutions; '+retainedIncomplete+' incomplete pre-repair traversals retained');
})().catch(error=>{console.error(error);process.exitCode=1;});
