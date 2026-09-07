const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
function fixture() {
    const c=vm.createContext({console});c.window=c;c.self=c;
    const root=path.resolve(__dirname,'../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation');
    for(const name of ['cnc-quotation-config','cnc-material-catalog','cnc-tool-library','cnc-reach','cnc-fixture-clearance','cnc-machine-capability','cnc-planning']) {
        const file=name==='cnc-planning'?path.resolve(__dirname,'fixtures/cnc-legacy-planning.test-helper.js'):path.join(root,name+'.js');
        let source=fs.readFileSync(file,'utf8');
        // Expose the real private mutation boundary only in this isolated VM.
        // Actual STEP/public-plan regressions cover its production call path.
        if(name==='cnc-planning')source=source.replace('/* CNC_TEST_INSTRUMENTATION_POINT */',
            'window.applyHandoffForTest = applyBallStockHandoffs; window.CncPlanningDiagnostics = Object.freeze({ plan: plan, planFixture: planFixture });');
        vm.runInContext(source,c);
    }
    const make=(id,code,diameter,extra={})=>({id,code,toolId:id,toolDiameterMm:diameter,
        toolFamily:code==='freeform_finishing'?'ball_end_mill':'flat_end_mill',reachable:true,
        setupNumber:1,featureSampleIds:[],featureClusterIds:['a'],featureAreaMm2:0,estimatedMinutes:.1,...extra});
    const operations=[make('face','facing',40),make('prep','roughing',2,{featureSampleIds:[3],featureAreaMm2:30}),
        make('small-rough','roughing',1,{featureSampleIds:[1,2],featureAreaMm2:6}),
        make('small-finish','finishing',1,{featureSampleIds:[1,2],featureAreaMm2:6}),
        make('ball','freeform_finishing',1,{featureTriangleIndexes:[100],featureAreaMm2:5})];
    const setups=[{id:'positive-z',number:1,operationIds:operations.map(o=>o.id),toolIds:operations.map(o=>o.toolId)}];
    const geometry={accessibilityField:{surfaceSamples:[
        {id:1,sourceTriangleIndex:200,areaMm2:2,clusterId:'a'},
        {id:2,sourceTriangleIndex:201,areaMm2:4,clusterId:'a'},
        {id:3,sourceTriangleIndex:202,areaMm2:30,clusterId:'a'}]},
        ballRestFinishingAccess:[{directionId:'positive-z',toolId:'ns-alb225-1-lu5',triangleIndexes:[100],method:'sampled-ball-contact',camCertain:false}],
        ballRestHandoffs:[1,2].map(id=>({sampleId:id,sourceTriangleIndex:199+id,directionId:'positive-z',ballToolId:'ns-alb225-1-lu5',preparationDiameterMm:2,
            requiresFacing:true,requiresPreparation:true,method:'sampled-ball-stock-handoff',camCertain:false,
            residualAxialCapMm:id===1?.5:.75,ballCenterMm:{x:0,y:0,z:0},preparationTipMm:{x:0,y:0,z:.25}}))};
    return {c,operations,setups,geometry,run(material='6061'){c.applyHandoffForTest(operations,setups,geometry,material,12000);}};
}
test('rest allocation transfers preparation, retains one finishing owner and costs only transferred rest area',()=>{
    const f=fixture();f.run();
    assert.equal(f.operations.length,3);
    const prep=f.operations.find(o=>o.id==='prep'),ball=f.operations.find(o=>o.id==='ball');
    assert.deepEqual(Array.from(prep.featureSampleIds),[3,1,2]);
    assert.equal(prep.featureAreaMm2,36);
    assert.equal(ball.featureAreaMm2,11);
    assert.equal(ball.stockHandoff.handoffAreaMm2,6);
    assert.ok(Math.abs(ball.stockHandoff.restCuttingMinutes-12/270)<1e-10);
    assert.ok(Math.abs(ball.stockHandoff.finalFinishingMinutes-11/45)<1e-10);
    assert.ok(Math.abs(ball.stockHandoff.passes.reduce((sum,pass)=>sum+pass.cuttingMinutes,0)
        -ball.stockHandoff.cuttingMinutes)<1e-10, 'serialized pass costs must reconcile with the handoff total');
    assert.equal(ball.stockHandoff.requiresCamVerification,true);
    assert.equal(ball.stockHandoff.stockClearanceBasis,'sampled-advisory');
    assert.deepEqual(Array.from(f.setups[0].operationIds),['face','prep','ball']);
    assert.ok(f.setups[0].toolIds.includes('ns-alb225-1-lu5'));
    assert.ok(!f.setups[0].toolIds.includes('small-rough'));
});
test('missing prerequisites, other setups, partial coverage and unsupported material retain every original operation',()=>{
    const changes=[f=>f.geometry.ballRestHandoffs.pop(),
        f=>f.geometry.ballRestFinishingAccess[0].triangleIndexes=[],
        f=>f.geometry.ballRestHandoffs[0].directionId='negative-z',
        f=>f.geometry.ballRestHandoffs[0].preparationTipMm.x=NaN,
        f=>f.operations.find(o=>o.id==='face').reachable=false,
        f=>f.operations.find(o=>o.id==='prep').reachable=false];
    for(const change of changes){const f=fixture();change(f);const before=JSON.stringify(f.operations);f.run();assert.equal(JSON.stringify(f.operations),before);}
    const f=fixture(),before=JSON.stringify(f.operations);f.run('SUS304');assert.equal(JSON.stringify(f.operations),before);
});

test('page geometry adapter preserves the worker handoff and finishing evidence consumed by planning',()=>{
    const f=fixture();
    const source=fs.readFileSync(path.resolve(__dirname,'../../Legacy.Maliev.Web/Pages/InstantQuotation/CNC-Machining.cshtml'),'utf8');
    const start=source.indexOf('function CncGeometryForItem('),end=source.indexOf('\n        function ',start+1);
    vm.runInContext(source.slice(start,end),f.c);
    const geometry=f.c.CncGeometryForItem({modelInfo:{size:{x:10,y:20,z:30},volume:1000,
        cncGeometry:structuredClone(f.geometry)}});
    f.c.applyHandoffForTest(f.operations,f.setups,geometry,'6061',12000);
    assert.equal(f.operations.length,3,'dropping worker evidence must not silently restore the 1mm flat operations');
    assert.ok(f.operations.find(o=>o.id==='ball').stockHandoff);
    const legacy=f.c.CncGeometryForItem({modelInfo:{size:{x:10,y:20,z:30},volume:1000,cncGeometry:{}}});
    assert.deepEqual(Array.from(legacy.ballRestHandoffs),[]);
    assert.deepEqual(Array.from(legacy.ballRestFinishingAccess),[]);
    for (const key of ['generalBallRestHandoffs','generalBallFinishingAccess','stockFacingRequirements']) {
        const evidence = [{directionId:'front',sampleId:17,toolId:'ns-alb225-6'}];
        const input = {modelInfo:{size:{x:10,y:20,z:30},volume:1000,cncGeometry:{[key]:evidence}}};
        assert.equal(f.c.CncGeometryForItem(input)[key], evidence, `${key} must survive worker-to-planner adaptation`);
        assert.deepEqual(Array.from(legacy[key]),[]);
    }
});
