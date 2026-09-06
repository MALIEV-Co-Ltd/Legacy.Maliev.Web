const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function planner() {
    const c = vm.createContext({console}); c.window = c;
    for (const name of ['cnc-quotation-config', 'cnc-material-catalog', 'cnc-tool-library',
        'cnc-reach', 'cnc-fixture-clearance', 'cnc-machine-capability', 'cnc-planning']) {
        const file = name === 'cnc-planning'
            ? path.resolve(__dirname, 'fixtures/cnc-legacy-planning.test-helper.js')
            : path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation', name + '.js');
        let source = fs.readFileSync(file, 'utf8');
        if (name === 'cnc-planning') source = source.replace('/* CNC_TEST_INSTRUMENTATION_POINT */',
            'window.selectOperationTool = operationTool; window.featureOperations = planOperations; window.prepareFeature = clusterFeatureEvidence; window.sortAssignedOperations = compareAssignedOperations; window.CncPlanningDiagnostics = Object.freeze({ plan: plan, planFixture: planFixture });');
        vm.runInContext(source, c);
    }
    return c;
}

test('a drill must match the bore rather than round any diameter to the nearest catalogue tool', () => {
    const c = planner();
    assert.equal(c.selectOperationTool({code:'drilling', targetToolDiameterMm:43.94}, '6061'), null);
    assert.equal(c.selectOperationTool({code:'drilling', targetToolDiameterMm:5.035}, '6061').diameterMm, 5);
});

test('an unsupported cutter diameter limit must not fall back to an oversized tool', () => {
    assert.equal(planner().selectOperationTool({code:'roughing', maximumToolDiameterMm:0.2}, '6061'), null);
});

test('every detected pilot has a spotting operation before HSS drilling', () => {
    const c=planner(), axis={x:0,y:0,z:1};
    const geometry={holeProxies:[{id:'h',diameterMm:5,depthMm:9,axis,surfaceClusterIds:['bore']}],surfaceClusters:[]};
    const ops=Array.from(c.featureOperations(geometry,{}, {},{count:0},100,100,[{id:'top',number:1,direction:axis}],['top']));
    assert.deepEqual(ops.filter(o=>['spot_drilling','drilling'].includes(o.code)).map(o=>o.code),['spot_drilling','drilling']);
    assert.equal(ops.find(o=>o.code==='spot_drilling').drillHoleId,'h');
});

test('verified local thread and chamfer features own their operations instead of ball finishing', () => {
    const c = planner();
    const axis = {x:0,y:0,z:1};
    const geometry = {orientedSizeMm:{x:100,y:25,z:10},
        threadProxies:[{id:'t1',holeId:'h1',axis,majorDiameterMm:6,pitchMm:1,depthMm:9,
            surfaceClusterIds:['thread'],confidence:'High'}],
        chamferProxies:[{id:'c1',axis,includedAngleDegrees:90,majorDiameterMm:10,
            minorDiameterMm:6,depthMm:2,surfaceClusterIds:['chamfer']}],
        holeProxies:[{id:'h1',axis,diameterMm:5,depthMm:9,surfaceClusterIds:['bore']}],
        surfaceClusters:['thread','chamfer'].map(id=>({id,featureType:id,type:'cylindrical',
            isInternal:true,radiusMm:277,areaMm2:20,curvedFinishingByDirection:{front:{triangleIndexes:[1],areaMm2:20}}}))};
    const ops = Array.from(c.featureOperations(geometry, {}, {}, {count:0}, 100, 100,
        [{id:'front',number:1,direction:axis}], ['front']));
    assert.equal(ops.filter(o=>o.code==='freeform_finishing').length, 0);
    const tapping = ops.find(o=>o.code==='tapping');
    assert.ok(tapping, 'a measured metric thread produces tapping, not a generic thread mill');
    assert.equal(tapping.targetToolDiameterMm, 6);
    assert.equal(tapping.pitchMm, 1);
    assert.equal(c.selectOperationTool(tapping,'6061').pitchMm, 1);
    assert.equal(c.selectOperationTool(tapping,'6061').family, 'tap');
    assert.equal(ops.filter(o=>o.code==='thread_milling').length, 0);
    const chamfer = ops.find(o=>o.code==='chamfering');
    assert.deepEqual(Array.from(chamfer.featureClusterIds), ['chamfer']);
    assert.equal(chamfer.includedAngleDegrees,90);
    assert.ok(ops.indexOf(chamfer) < ops.indexOf(tapping), 'finish the hole entry before tapping');
    const prepared = c.prepareFeature(geometry,geometry.surfaceClusters[1],false,axis);
    assert.deepEqual(Array.from(prepared.operationCodes), ['chamfering']);
});

test('thread and pilot clusters cannot inherit generic flat or ball milling operations', () => {
    const c = planner(), axis = {x:1,y:0,z:0};
    const geometry = {orientedSizeMm:{x:80,y:40,z:20},
        holeProxies:[{id:'pilot',axis,diameterMm:5,depthMm:10,surfaceClusterIds:['thread-side']}],
        threadProxies:[{id:'thread',holeId:'pilot',axis,majorDiameterMm:6,pitchMm:1,depthMm:10,
            surfaceClusterIds:['thread-side'],confidence:'High'}],
        surfaceClusters:[{id:'thread-side',featureType:'thread',type:'cylindrical',isInternal:true,
            radiusMm:3,areaMm2:25,operationCodes:['roughing','finishing','freeform_finishing'],
            curvedFinishingByDirection:{side:{triangleIndexes:[1,2],areaMm2:25}}}]};
    const ops = Array.from(c.featureOperations(geometry,{}, {},{count:0},100,100,
        [{id:'side',number:3,direction:axis}],['side']));
    const prepared=c.prepareFeature(geometry,geometry.surfaceClusters[0],false,axis);
    assert.deepEqual(Array.from(prepared.operationCodes).sort(),['drilling','spot_drilling','tapping'],
        'specialized hole operations replace stale generic cluster classifications');
    assert.deepEqual(ops.filter(o=>['roughing','finishing','freeform_finishing'].includes(o.code)
        && Array.from(o.clusterIds || []).includes('thread-side')),[],
        'modeled thread geometry must not create small flat or ball end-mill work');
    assert.deepEqual(ops.filter(o=>['spot_drilling','drilling','tapping'].includes(o.code)).map(o=>o.code),
        ['spot_drilling','drilling','tapping']);
});

test('internal transition clusters adjoining a verified modeled thread remain thread-owned', () => {
    const c = planner(), axis = {x:1,y:0,z:0};
    const geometry = {orientedSizeMm:{x:80,y:40,z:20},
        holeProxies:[{id:'pilot',axis,diameterMm:5,depthMm:10,surfaceClusterIds:['thread-core']}],
        threadProxies:[{id:'thread',holeId:'pilot',axis,majorDiameterMm:6,pitchMm:1,depthMm:10,
            surfaceClusterIds:['thread-core'],confidence:'High'}],
        surfaceClusters:[
            {id:'thread-core',featureType:'thread',type:'freeform',isInternal:true,axis,
                adjacentClusterIds:['thread-transition']},
            {id:'thread-transition',type:'freeform',isInternal:true,axis,areaMm2:3,
                adjacentClusterIds:['thread-core'],internalCornerRadiusMm:0.5,
                operationCodes:['roughing','finishing','freeform_finishing'],
                curvedFinishingByDirection:{side:{triangleIndexes:[7,8],areaMm2:3}}}
        ]};
    const ops = Array.from(c.featureOperations(geometry,{}, {},{count:0},100,100,
        [{id:'side',number:3,direction:axis}],['side']));
    const transition = c.prepareFeature(geometry, geometry.surfaceClusters[1], false, axis);
    assert.deepEqual(Array.from(transition.operationCodes), ['tapping'],
        'the reach model must not publish flat or ball cutter access for the transition');
    assert.equal(ops.some(o=>o.code==='freeform_finishing'
        && Array.from(o.featureClusterIds || []).includes('thread-transition')),false,
        'a tessellated thread entry/root transition must not become an unresolved ball-end pass');
});

test('external modeled threads and their transitions use one pitch-specific thread-milling operation', () => {
    const c = planner(), axis = {x:0,y:0,z:1};
    const geometry = {orientedSizeMm:{x:30,y:30,z:40},holeProxies:[],
        threadProxies:[{id:'external-thread',isInternal:false,axis,majorDiameterMm:20,
            minorDiameterMm:17.3,pitchMm:2.5,depthMm:15,surfaceClusterIds:['thread-crests'],
            confidence:'Medium'}],
        surfaceClusters:[
            {id:'thread-crests',featureType:'thread',type:'freeform',isInternal:false,axis,
                adjacentClusterIds:['thread-runout']},
            {id:'thread-runout',type:'freeform',isInternal:false,axis,areaMm2:8,
                adjacentClusterIds:['thread-crests'],internalCornerRadiusMm:0.5,
                operationCodes:['roughing','finishing','freeform_finishing'],
                curvedFinishingByDirection:{top:{triangleIndexes:[31,32],areaMm2:8}}}
        ]};
    const ops = Array.from(c.featureOperations(geometry,{}, {},{count:0},100,100,
        [{id:'top',number:1,direction:axis}],['top']));
    const transition = c.prepareFeature(geometry, geometry.surfaceClusters[1], false, axis);
    assert.deepEqual(Array.from(transition.operationCodes), ['thread_milling']);
    assert.equal(ops.filter(o=>o.code==='thread_milling').length,1);
    const threadMill = ops.find(o=>o.code==='thread_milling');
    assert.equal(threadMill.pitchMm,2.5);
    assert.equal(threadMill.threadMajorDiameterMm,20);
    assert.equal(ops.some(o=>['tapping','roughing','finishing','freeform_finishing'].includes(o.code)
        && Array.from(o.featureClusterIds || []).includes('thread-runout')),false);
});

test('measured internal threads larger than M12 are thread milled rather than tapped', () => {
    const c = planner(), axis = {x:0,y:0,z:1};
    const geometry = {orientedSizeMm:{x:40,y:40,z:20},holeProxies:[],
        threadProxies:[{id:'large-internal-thread',isInternal:true,axis,majorDiameterMm:16,
            minorDiameterMm:13.8,pitchMm:2,depthMm:12,surfaceClusterIds:['thread']}],
        surfaceClusters:[{id:'thread',featureType:'thread',type:'freeform',isInternal:true,axis}]};
    const ops = Array.from(c.featureOperations(geometry,{}, {},{count:0},100,100,
        [{id:'top',number:1,direction:axis}],['top']));
    assert.equal(ops.filter(o=>o.code==='thread_milling').length,1);
    assert.equal(ops.filter(o=>o.code==='tapping').length,0);
    assert.equal(ops.find(o=>o.code==='thread_milling').pitchMm,2);
});

test('modeled M12 relief remains tap-sized at the thread-milling boundary', () => {
    const c = planner(), axis = {x:0,y:0,z:1};
    const geometry = {orientedSizeMm:{x:35,y:35,z:20},holeProxies:[],
        threadProxies:[{id:'m12-internal',isInternal:true,axis,majorDiameterMm:12.2,
            minorDiameterMm:10.2,pitchMm:1.75,depthMm:12,surfaceClusterIds:['thread']}],
        surfaceClusters:[{id:'thread',featureType:'thread',type:'freeform',isInternal:true,axis}]};
    const ops = Array.from(c.featureOperations(geometry,{}, {},{count:0},100,100,
        [{id:'top',number:1,direction:axis}],['top']));
    assert.equal(ops.filter(o=>o.code==='tapping').length,1);
    assert.equal(ops.filter(o=>o.code==='thread_milling').length,0);
});

test('an axial externally threaded part remains a two-setup plan without thread-derived small or ball cutters', () => {
    const c = planner(), z = {x:0,y:0,z:1}, reverse = {x:0,y:0,z:-1};
    const geometry = {bodyCount:1,orientedSizeMm:{x:30,y:30,z:40},partVolumeMm3:15000,
        partSurfaceAreaMm2:7000,boxFillRatio:0.45,rotationalEvidence:{eligible:true,axis:z,
            diameterMm:30,lengthMm:40,confidence:'High'},
        orientationCandidates:[{id:'positive-z',toolDirection:z,projectedFaceCoverage:0.5},
            {id:'negative-z',toolDirection:reverse,projectedFaceCoverage:0.5},
            {id:'positive-x',toolDirection:{x:1,y:0,z:0},projectedFaceCoverage:0.1},
            {id:'positive-y',toolDirection:{x:0,y:1,z:0},projectedFaceCoverage:0.1}],
        holeProxies:[],chamferProxies:[{id:'shoulder-chamfer',axis:z,includedAngleDegrees:90,
            majorDiameterMm:16,minorDiameterMm:14,depthMm:1,entryDirections:[z],
            surfaceClusterIds:['shoulder-chamfer']}],
        threadProxies:[{id:'external-thread-1',isInternal:false,axis:z,
            majorDiameterMm:13.89,minorDiameterMm:11.7,pitchMm:1,axialDepthMm:15,
            surfaceClusterIds:['thread-crests']}],
        surfaceClusters:[
            {id:'front',type:'planar',normal:z,areaMm2:3000},
            {id:'back',type:'planar',normal:reverse,areaMm2:3000},
            {id:'shoulder-chamfer',featureType:'chamfer',type:'conical',isInternal:false,axis:z,
                areaMm2:80,adjacentClusterIds:['front']},
            {id:'thread-crests',featureType:'thread',type:'freeform',isInternal:false,axis:z,
                areaMm2:900,adjacentClusterIds:['thread-runout']},
            {id:'thread-runout',type:'freeform',isInternal:false,axis:z,areaMm2:100,
                adjacentClusterIds:['thread-crests','thread-flank'],internalCornerRadiusMm:0.5,
                operationCodes:['roughing','finishing','freeform_finishing'],
                curvedFinishingByDirection:{'positive-x':{triangleIndexes:[31,32],areaMm2:100}}},
            {id:'thread-flank',type:'freeform',isInternal:false,axis:z,areaMm2:140,
                adjacentClusterIds:['thread-runout','thread-root'],internalCornerRadiusMm:0.35,
                operationCodes:['roughing','finishing','freeform_finishing'],
                curvedFinishingByDirection:{'positive-y':{triangleIndexes:[33,34],areaMm2:140}}},
            {id:'thread-root',type:'freeform',isInternal:false,axis:z,areaMm2:80,
                adjacentClusterIds:['thread-flank'],internalCornerRadiusMm:0.25,
                operationCodes:['roughing','finishing','freeform_finishing'],
                curvedFinishingByDirection:{'negative-x':{triangleIndexes:[35,36],areaMm2:80}}}
        ]};
    const plan = c.CncPlanningDiagnostics.plan({material:'6061',geometry,
        stock:{stockSizeMm:{x:35,y:35,z:50},confidence:'High'},requirements:{quantity:1}});
    assert.equal(plan.setups.length,2);
    assert.equal(plan.operations.filter(o=>o.code==='thread_milling').length,1);
    assert.equal(plan.operations.find(o=>o.code==='thread_milling').threadMajorDiameterMm,14);
    assert.equal(plan.operations.find(o=>o.code==='thread_milling').pitchMm,1);
    assert.equal(plan.operations.some(o=>o.code==='freeform_finishing'),false);
    assert.equal(plan.operations.some(o=>o.toolDiameterMm===1),false);
    assert.equal(plan.operations.some(o=>o.toolDiameterMm===2),false);
    assert.ok(plan.operations.some(o=>o.code==='chamfering' && o.toolFamily==='chamfer_mill'));
});

test('late rest-machining cutters remain ordered largest to smallest within an operation phase', () => {
    const c=planner();
    const operations=[16,10,4,2,1,6].map((diameter,index)=>({
        code:'roughing',toolDiameterMm:diameter,id:'rough-'+index
    }));
    operations.sort(c.sortAssignedOperations);
    assert.deepEqual(operations.map(o=>o.toolDiameterMm),[16,10,6,4,2,1]);
});

test('tap matching includes pitch and never substitutes a drill or a different thread', () => {
    const c = planner();
    assert.equal(c.selectOperationTool({code:'tapping',targetToolDiameterMm:6,pitchMm:0.75},'6061').pitchMm,0.75);
    assert.equal(c.selectOperationTool({code:'tapping',targetToolDiameterMm:6,pitchMm:0.6},'6061'),null);
    assert.equal(c.selectOperationTool({code:'tapping',targetToolDiameterMm:6,pitchMm:1,threadHand:'left'},'6061'),null);
});

test('imported nominal drilling uses the matching physical drill inside a verified entry corridor', () => {
    const c = planner(), axis = {x:0,y:0,z:1};
    const result = c.CncReach.evaluate({material:'6061', setups:[{id:'top',number:1,direction:axis}],
        geometry:{surfaceClusters:[{id:'hole',operationCodes:['drilling'],featureAxis:axis,
            featureEntryDirections:[axis],openingWidthMm:5.035,requiredDepthMm:9}]}});
    assert.ok(Array.from(result.records).some(r=>r.toolDiameterMm===5 && r.reachable));
    assert.ok(Array.from(result.records).every(r=>r.toolDiameterMm <= 5.085));
});

test('90 degree spotting cutters require the correct cutting range and full physical entry', () => {
    const c = planner(), axis = {x:0,y:0,z:1};
    const operation = {code:'chamfering',includedAngleDegrees:90,majorDiameterMm:10,minorDiameterMm:6,requiredDepthMm:2,
        verifiedPilotDepthBelowMm:9,pilotDiameterMm:6,
        spotEntryEvidence:{byDiameterMm:{10:[axis],12:[axis],16:[axis]},holderDiameterMm:25,holderStartAboveEntryMm:20}};
    const tool = c.selectOperationTool(operation,'6061');
    assert.ok(tool, 'a catalogue 90 degree countersink is available');
    assert.equal(tool.diameterMm,10);
    assert.equal(c.selectOperationTool({...operation,majorDiameterMm:11.2},'6061').diameterMm,12);
    assert.equal(c.selectOperationTool({...operation,majorDiameterMm:13},'6061').diameterMm,16);
    assert.equal(c.selectOperationTool({...operation,includedAngleDegrees:82},'6061'),null);
    assert.equal(c.selectOperationTool({...operation,majorDiameterMm:30},'6061'),null);
    for (const [entries,expected] of [[[axis],true],[[],false]]) {
        const result = c.CncReach.evaluate({material:'6061',tools:[tool],setups:[{id:'top',number:1,direction:axis}],
            geometry:{surfaceClusters:[{id:'chamfer',operationCodes:['chamfering'],featureAxis:axis,
                featureEntryDirections:entries,featureChamfer:{...operation,
                    spotEntryEvidence:{...operation.spotEntryEvidence,byDiameterMm:{10:entries}}},requiredDepthMm:2}]}});
        assert.equal(result.records[0].reachable,expected,JSON.stringify(result.records[0]));
    }
    const result = c.CncReach.evaluate({material:'6061',setups:[{id:'top',number:1,direction:axis}],
        geometry:{surfaceClusters:[{id:'large-chamfer',operationCodes:['chamfering'],featureAxis:axis,
            featureEntryDirections:[axis],featureChamfer:{...operation,majorDiameterMm:11.2},requiredDepthMm:2}]}});
    assert.ok(Array.from(result.records).some(r=>r.toolDiameterMm===12 && r.reachable));
    for (const depth of [undefined,0,1]) {
        const blocked=c.CncReach.evaluate({material:'6061',tools:[tool],setups:[{id:'top',number:1,direction:axis}],
            geometry:{surfaceClusters:[{id:'blind-chamfer',operationCodes:['chamfering'],featureAxis:axis,
                featureEntryDirections:[axis],featureChamfer:{...operation,verifiedPilotDepthBelowMm:depth},requiredDepthMm:2}]}});
        assert.equal(blocked.records[0].reachable,false,'the tip needs clearance below the cone, not just an open entrance');
    }
});

test('spotting cannot borrow a smaller cutter entry or the narrow pilot entry', () => {
    const c=planner(),axis={x:0,y:0,z:1};
    const operation={code:'spot_drilling',includedAngleDegrees:90,majorDiameterMm:11.2,minorDiameterMm:5};
    assert.equal(c.selectOperationTool(operation,'6061',[{toolId:'spot-drill-10',analysisProfileId:'flat-16-2d'}]),null);
    const result=c.CncReach.evaluate({material:'6061',setups:[{id:'top',number:1,direction:axis}],
        geometry:{surfaceClusters:[{id:'pilot',operationCodes:['spot_drilling'],featureAxis:axis,
            featureEntryDirections:[axis],featureHole:{diameterMm:5,depthMm:9},openingWidthMm:5}]}});
    assert.ok(Array.from(result.records).every(r=>!r.reachable));
});

test('shared pilot and thread faces check tap diameter against the thread, not the pilot', () => {
    const c=planner(), axis={x:0,y:0,z:1};
    const geometry={holeProxies:[{surfaceClusterIds:['hole'],axis,diameterMm:5.035,depthMm:9,entryDirections:[axis]}],
        threadProxies:[{surfaceClusterIds:['hole'],axis,majorDiameterMm:6.158,minorDiameterMm:5.035,
            pitchMm:1,depthMm:9,entryDirections:[axis]}]};
    const cluster=c.prepareFeature(geometry,{id:'hole'},false,axis);
    const result=c.CncReach.evaluate({material:'6061',setups:[{id:'top',number:1,direction:axis}],geometry:{surfaceClusters:[cluster]}});
    assert.ok(Array.from(result.records).some(r=>r.operationCode==='tapping' && r.toolDiameterMm===6 && r.reachable));
});
