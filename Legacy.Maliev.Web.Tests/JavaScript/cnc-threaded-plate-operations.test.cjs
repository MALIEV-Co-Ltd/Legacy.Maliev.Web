const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

test('threaded cover plate spots its two entrances before HSS drilling and M6 tapping without duplicate chamfer passes', async () => {
    const root = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot');
    const source = fs.readFileSync(path.resolve(__dirname,'../TestAssets/Cnc/threaded-cover-plate.step'));
    const occt = await require(path.join(root,'lib/occt/occt-import-js.js'))({
        wasmBinary:fs.readFileSync(path.join(root,'lib/occt/occt-import-js.wasm'))});
    const model=occt.ReadStepFile(source,null);
    assert.equal(model.success,true);
    const triangles=[],cadFaceRanges=[]; let triangleOffset=0, volume=0;
    for(const mesh of model.meshes) {
        for(const face of mesh.brep_faces||[]) cadFaceRanges.push({first:triangleOffset+face.first,last:triangleOffset+face.last});
        triangleOffset+=mesh.index.array.length/3;
        for(const index of mesh.index.array) triangles.push(...mesh.attributes.position.array.slice(index*3,index*3+3));
    }
    for(let i=0;i<triangles.length;i+=9){
        const [ax,ay,az,bx,by,bz,cx,cy,cz]=triangles.slice(i,i+9);
        volume+=(ax*(by*cz-bz*cy)+ay*(bz*cx-bx*cz)+az*(bx*cy-by*cx))/6;
    }
    const c=vm.createContext({console}); c.window=c;c.self=c;
    for(const name of ['cnc-quotation-config','cnc-material-catalog','cnc-tool-library','cnc-reach',
        'cnc-fixture-clearance','cnc-machine-capability','cnc-planning','cnc-spatial-field.worker',
        'cnc-geometry.worker','cnc-cad-surfaces.worker','cnc-ball-rest.worker']) {
        const file=name==='cnc-planning'?path.resolve(__dirname,'fixtures/cnc-legacy-planning.test-helper.js'):path.join(root,'src/app/js/cnc-quotation',name+'.js');
        let moduleSource=fs.readFileSync(file,'utf8');
        if(name==='cnc-planning') moduleSource=moduleSource.replace('/* CNC_TEST_INSTRUMENTATION_POINT */',
            'window.CncPlanningDiagnostics = Object.freeze({ plan: plan, planFixture: planFixture });');
        vm.runInContext(moduleSource,c);
    }
    const geometry=c.AnalyzeCncGeometry(new Float32Array(triangles),{bodyCount:1,volume:Math.abs(volume),cadFaceRanges,
        analyticSurfaces:c.CncCadSurfaces.parseStep(source.toString())});
    const plan=c.CncPlanningDiagnostics.plan({material:'6061',geometry,requirements:{quantity:1},
        stock:{stockSizeMm:{x:650,y:75,z:35},confidence:'High'}});
    const ops=Array.from(plan.operations);
    const describedPlan=c.CncPlanningDiagnostics.plan({material:'6061',geometry,
        requirements:{quantity:1,threads:[{designation:'M6 x 1',count:1}]},
        stock:{stockSizeMm:{x:650,y:75,z:35},confidence:'High'}});
    const describedTaps=Array.from(describedPlan.operations).filter(o=>o.code==='tapping');
    assert.equal(describedTaps.length,2,'a customer description of modeled M6 threads must not add a generic tap');
    assert.ok(describedTaps.every(o=>o.toolDiameterMm===6),JSON.stringify(describedTaps));
    const slotWall = geometry.surfaceClusters.find(cluster => (cluster.triangleIndexes || []).includes(5069));
    assert.equal(slotWall.type, 'cylindrical');
    assert.ok(Math.abs(slotWall.prismaticContourAxis.z) > 0.99999,
        'the rounded slot retains its all-facet axial wall proof despite the coarse cylindrical label');
    for (const code of ['roughing', 'finishing']) {
        const cuts = ops.filter(o => o.code === code && o.setupNumber === 1);
        assert.equal(cuts.length, 2, code + ' needs a bulk cutter and one complete-slot cutter, not a diameter staircase');
        assert.ok([16, 10].includes(cuts[0].toolDiameterMm));
        assert.ok([6, 4].includes(cuts[1].toolDiameterMm));
    }
    assert.deepEqual(ops.filter(o => o.setupNumber === 2).map(o => o.code), ['facing', 'deburring'],
        'the flat reverse face is completed by facing');
    for(const [code,diameter] of [['spot_drilling',12],['drilling',5],['tapping',6]]){
        const selected=ops.filter(o=>o.code===code);
        assert.equal(selected.length,2,code);
        assert.ok(selected.every(o=>o.toolDiameterMm===diameter),JSON.stringify(selected));
        assert.ok(selected.every(o=>o.reachable),JSON.stringify(selected));
        assert.equal(new Set(selected.map(o=>o.setupNumber)).size,1);
    }
    assert.equal(plan.setups.length,2);
    assert.equal(ops.filter(o=>o.code==='chamfering').length,0);
    const holeOperations=ops.filter(o=>['spot_drilling','drilling','tapping'].includes(o.code));
    assert.deepEqual(holeOperations.map(o=>o.code),['spot_drilling','spot_drilling','drilling','drilling','tapping','tapping']);
    assert.equal(new Set(holeOperations.map(o=>o.setupNumber)).size,1);
    const balls=ops.filter(o=>o.code==='freeform_finishing');
    assert.equal(balls.length,0,JSON.stringify(balls.map(o=>({operation:o,clusters:geometry.surfaceClusters.filter(s=>(o.featureClusterIds||[]).includes(s.id)).map(s=>({id:s.id,type:s.type,filletFeatures:s.filletFeatures,curved:s.curvedFinishingByDirection}))}))));
    const specialIds=new Set([...geometry.threadProxies,...geometry.chamferProxies].flatMap(f=>Array.from(f.surfaceClusterIds)));
    for(const operation of ops.filter(o=>['roughing','finishing'].includes(o.code)))
        assert.ok((operation.clusterIds||[]).every(id=>!specialIds.has(id)),'specialized surfaces are not milled twice');
});
