import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from 'three';
import * as module from '../wwwroot/src/app/js/instant-quotation/workflow-interop.mjs';
import { analyzeUploadDerivedGeometry } from '../wwwroot/src/app/js/instant-quotation/geometry-analysis.mjs';
import { createModelViewer } from '../wwwroot/src/app/js/instant-quotation/model-viewer.mjs';
import { createWallThicknessAnalyzer } from '../wwwroot/src/app/js/instant-quotation/wall-thickness.mjs';
const sheet={dimensionXmm:20,dimensionYmm:20,dimensionZmm:0,volumeMm3:0,surfaceAreaMm2:400,
  facetCount:2,bodyCount:1,topologyChecked:true,nonWatertight:true,nonManifold:false};
const file={name:'sheet.stl',size:184};
test('only bounded finite unambiguous planar STL is preview-only',()=>assert.equal(module.classifyIncompletePreview?.(file,sheet),true));
for(const [name,f,c] of [
  ['bytes',{...file,size:8*1024*1024+1},sheet],['triangles',file,{...sheet,facetCount:50001}],
  ['Infinity',file,{...sheet,dimensionXmm:Infinity}],['NaN',file,{...sheet,volumeMm3:NaN}],
  ['negative',file,{...sheet,dimensionZmm:-1}],['two zero dimensions',file,{...sheet,dimensionXmm:0}],
  ['ambiguous',file,{...sheet,nonManifold:true}],['unchecked',file,{...sheet,topologyChecked:false}],
  ['multiple bodies',file,{...sheet,bodyCount:2}],['surface',file,{...sheet,surfaceAreaMm2:0}],
  ['format',{...file,name:'sheet.obj'},sheet],['normal volume',file,{...sheet,dimensionZmm:1,volumeMm3:400}],
]) test(`exceptional preview rejects ${name}`,()=>assert.equal(module.classifyIncompletePreview?.(f,c),false));
function harness(){
  const models=[],jobs=[],reports=[];
  const interop=module.createWorkflowPreviewInterop({
    loadModel:async selected=>{
      const geometry=selected.name==='solid.stl'?new THREE.BoxGeometry(20,20,1):new THREE.PlaneGeometry(20,20);
      const object=new THREE.Group();object.add(new THREE.Mesh(geometry,new THREE.MeshBasicMaterial()));
      const state={object,disposed:0};geometry.addEventListener('dispose',()=>state.disposed++);models.push(state);return object;
    },analyzeGeometry:analyzeUploadDerivedGeometry,
    createViewer:()=>createModelViewer({adapter:{show:o=>o.visible=true,hide:o=>o.visible=false}}),
    thicknessAnalyzer:{analyze:()=>new Promise(resolve=>jobs.push(resolve)),dispose(){}},
    reportIncomplete:(...args)=>reports.push(args),
  });
  async function add(name='sheet.stl'){
    const [key]=interop.beginSelection({files:[{name,size:184,arrayBuffer:async()=>new Uint8Array(184).buffer}]});
    await interop.getGeometryClaim(key);return key;
  }return {interop,models,jobs,reports,add};
}
const tick=()=>new Promise(resolve=>setImmediate(resolve));
test('replacement/remove dispose once and late completion cannot resurrect a preview',async()=>{
  const h=harness();try{
    const first=await h.add();assert.equal(h.interop.retainIncompletePreview?.(first,{}),true);
    const second=await h.add();assert.equal(h.interop.retainIncompletePreview?.(second,{}),true);
    assert.equal(h.models[0].disposed,1);h.jobs[0]({summary:{state:'unavailable'}});await tick();
    assert.equal(h.reports.filter(a=>a[0]===first).length,0);
    assert.equal(h.interop.getIncompletePreviewState?.()?.key,second);
    assert.equal(h.interop.removeIncompletePreview?.(second),true);assert.equal(h.models[1].disposed,1);
    h.jobs[1]({summary:{state:'unavailable'}});await tick();
    assert.equal(h.interop.getIncompletePreviewState?.(),null);assert.equal(h.reports.length,0);
  }finally{h.interop.dispose();}
});
test('dispose invalidates exceptional key and late completion cannot double-dispose',async()=>{
  const h=harness();const key=await h.add();try{assert.equal(h.interop.retainIncompletePreview?.(key,{}),true);}finally{h.interop.dispose();}
  assert.equal(h.models[0].disposed,1);h.jobs[0]({summary:{state:'unavailable'}});await tick();
  assert.equal(h.reports.length,0);assert.equal(h.interop.getIncompletePreviewState?.(),null);
});
test('exceptional preview cannot admit and removing it preserves normal admitted part',async()=>{
  const h=harness();try{
    h.interop.attach({});const normal=await h.add('solid.stl');assert.equal(h.interop.admit(normal,'normal','PLA'),true);
    const exceptional=await h.add();assert.equal(h.interop.retainIncompletePreview?.(exceptional,{}),true);
    assert.equal(h.models[0].object.children[0].material.side, THREE.FrontSide);
    assert.equal(h.models[1].object.children[0].material.side, THREE.DoubleSide);
    assert.equal(h.interop.admit(exceptional,'forged','PLA'),false);
    assert.equal(h.interop.removeIncompletePreview?.(exceptional),true);assert.equal(h.models[0].disposed,0);
    assert.equal(h.interop.select('normal'),true);
  }finally{h.interop.dispose();}
});
test('quarantine cancels exceptional preview resources and late completion',async()=>{
  const h=harness();try{
    const key=await h.add();assert.equal(h.interop.retainIncompletePreview(key,{}),true);
    h.interop.quarantine(key);
    assert.equal(h.interop.getIncompletePreviewState(),null);
    assert.equal(h.models[0].disposed,1);
    h.jobs[0]({summary:{state:'unavailable'}});await tick();assert.equal(h.reports.length,0);
  }finally{h.interop.dispose();}
});

for (const limit of ['bytes', 'triangles']) test(`out-of-budget planar ${limit} never launches the real analyzer worker`, async () => {
  let workers = 0, disposed = 0;
  const geometry = limit === 'triangles' ? new THREE.PlaneGeometry(20, 20, 1, 25001) : new THREE.PlaneGeometry(20, 20);
  geometry.addEventListener('dispose', () => disposed++);
  const object = new THREE.Group(); object.add(new THREE.Mesh(geometry, new THREE.MeshBasicMaterial()));
  const analyzer = createWallThicknessAnalyzer({ workerFactory: () => {
    workers++;
    return { postMessage() { queueMicrotask(() => this.onmessage({data:{evidence:null}})); }, terminate() {} };
  }});
  const interop = module.createWorkflowPreviewInterop({loadModel:async()=>object,
    analyzeGeometry:analyzeUploadDerivedGeometry, createViewer:()=>{throw new Error('Rejected planar mesh must not retain a viewer');},
    thicknessAnalyzer:analyzer});
  try {
    const selected = {name:'over-budget.stl',size:limit==='bytes'?8*1024*1024+1:2500184,
      arrayBuffer:async()=>new Uint8Array(184).buffer};
    const [key] = interop.beginSelection({files:[selected]});
    const claim = await interop.getGeometryClaim(key);
    assert.equal(claim.volumeMm3, 0);
    assert.equal(interop.isIncompletePreview(key), false);
    assert.equal(interop.retainIncompletePreview(key, {}), false);
    await tick(); assert.equal(workers, 0);
    interop.quarantine(key); assert.equal(disposed, 1);
    assert.equal(interop.getIncompletePreviewState(), null);
  } finally { interop.dispose(); }
});
