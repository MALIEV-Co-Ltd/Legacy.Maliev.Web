const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function planStock({certificate = true, facing = true, badTriangle = false, badTool = false,
    missingBallContact = false, preparationDiameter = 6, stockPlane = false} = {}) {
    const c = vm.createContext({console}); c.window = c;
    for (const name of ['cnc-quotation-config','cnc-material-catalog','cnc-tool-library',
        'cnc-reach','cnc-fixture-clearance','cnc-machine-capability','cnc-planning']) {
        const file = name === 'cnc-planning'
            ? path.resolve(__dirname, 'fixtures/cnc-legacy-planning.test-helper.js')
            : path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation', name + '.js');
        let source = fs.readFileSync(file,'utf8');
        if (name === 'cnc-planning') source = source.replace('/* CNC_TEST_INSTRUMENTATION_POINT */',
            'window.CncPlanningDiagnostics = Object.freeze({ plan: plan, planFixture: planFixture });');
        vm.runInContext(source,c);
    }
    const access = Object.fromEntries(Array.from(c.CncToolLibrary.analysisProfiles()).map(tool => {
        const ids = tool.family === 'face_mill' ? [4]
            : tool.diameterMm <= 1 ? [1,2,3,4] : tool.diameterMm <= 2 ? [2,3,4]
                : tool.diameterMm <= 6 ? [3,4] : [4];
        return [tool.id,{reachableSampleIds:ids,tipSampleIds:ids,fluteSampleIds:[]}];
    }));
    const prepId = preparationDiameter === 6 ? 'flat-6x18' : 'flat-10-2d';
    const handoff = {sampleId:1,sourceTriangleIndex:badTriangle?999:10,directionId:'front',
        ballToolId:'ns-alb225-4',preparationToolId:badTool?'no-such-tool':prepId,
        preparationDiameterMm:preparationDiameter,requiresPreparation:true,requiresFacing:true,
        method:'sampled-ball-stock-handoff',camCertain:false,
        stockClearanceBasis:'prepared-cylinder-and-stock-top',residualAxialCapMm:1.5,
        ballCenterMm:{x:0,y:0,z:2},preparationTipMm:{x:0,y:0,z:1},facedStockTopMm:20};
    return c.CncPlanningDiagnostics.plan({material:'6061',requirements:{quantity:1},
        stock:{stockSizeMm:{x:45,y:55,z:25}},geometry:{bodyCount:1,
            orientedSizeMm:{x:40,y:50,z:20},partVolumeMm3:20000,partSurfaceAreaMm2:400,
            orientationCandidates:[{id:'front',toolDirection:{x:0,y:0,z:1},projectedFaceCoverage:1}],
            surfaceClusters:[{id:'shell',type:'unresolved',areaMm2:300,filletFeatures:[],
                accessibleDirectionIds:['front'],curvedFinishingByDirection:{front:{triangleIndexes:[10,20,30],
                    areaMm2:300,method:'triangle-normal-variation',camCertain:false}}},
                {id:'stock-face',type:'planar',areaMm2:100,normal:facing?{x:0,y:0,z:1}:{x:1,y:0,z:0},
                    accessibleDirectionIds:['front']}],
            generalBallFinishingAccess:[{directionId:'front',toolId:'ns-alb225-4',
                sampleIds:missingBallContact?[2,3]:[1,2,3],triangleIndexes:missingBallContact?[20,30]:[10,20,30],
                method:'sampled-ball-contact',camCertain:false}],
            generalBallRestHandoffs:certificate?[handoff]:[],
            stockFacingRequirements:stockPlane?[{directionId:'front',planeProjectionMm:20,
                method:'model-exterior-plane',requiresFacing:true,camCertain:false}]:[],
            accessibilityField:{surfaceSamples:[
                {id:1,clusterId:'shell',sourceTriangleIndex:10,areaMm2:100},
                {id:2,clusterId:'shell',sourceTriangleIndex:20,areaMm2:100},
                {id:3,clusterId:'shell',sourceTriangleIndex:30,areaMm2:100},
                {id:4,clusterId:'stock-face',sourceTriangleIndex:40,areaMm2:100}],
                toolAccess:{front:access}}}});
}

test('proven exterior stock facing replaces the unassigned fallback rather than adding a duplicate', () => {
    const plan = planStock({facing:false,stockPlane:true});
    const faces = Array.from(plan.operations).filter(op => op.code === 'facing');
    assert.equal(faces.length,1);
    assert.ok(faces[0].reachable);
    assert.ok(faces[0].stockPreparation);
    assert.ok(plan.operations.some(op => op.stockHandoff));
});
const roughs = plan => Array.from(plan.operations).filter(op => op.code === 'roughing' && op.reachable);
const owns = (op,id) => Array.from(op.featureSampleIds || []).includes(id);

for (const preparationDiameter of [6,10]) {
    test(`certified D${preparationDiameter} preparation transfers only the certified small-tool sample and retains cutting cost`, () => {
        const before = planStock({certificate:false}), after = planStock({preparationDiameter});
        assert.equal(roughs(before).find(op => owns(op,1)).toolDiameterMm,1);
        assert.equal(roughs(before).find(op => owns(op,2)).toolDiameterMm,2);
        const prep = roughs(after).find(op => owns(op,1));
        assert.equal(prep.toolDiameterMm,preparationDiameter);
        assert.equal(roughs(after).find(op => owns(op,2)).toolDiameterMm,2,'uncertified neighboring sample remains small-tool work');
        assert.deepEqual(roughs(after).flatMap(op => Array.from(op.featureSampleIds)).sort(),[1,2,3]);
        assert.deepEqual(Array.from(after.operations.find(op => op.code === 'facing').facingFinishedSampleIds),[4],
            'the faced plane is already finished and must not reappear as roughing work');
        const sum = list => list.reduce((total,op) => total+op.estimatedMinutes,0);
        assert.ok(Math.abs(sum(roughs(after))-sum(roughs(before)))<1e-8,'bulk roughing budget must not disappear during transfer');
        const ball = after.operations.find(op => op.sampledSurfaceFinishing && op.stockHandoff);
        assert.ok(ball);
        assert.deepEqual(Array.from(ball.stockHandoff.preparationSampleIds),[1]);
        assert.ok(ball.stockHandoff.restCuttingMinutes>0);
        assert.ok(ball.cuttingMinutes>=ball.stockHandoff.restCuttingMinutes+ball.stockHandoff.finalFinishingMinutes);
        assert.ok(prep.restPreparation.handoffs.length===1);
    });
}

for (const [reason,options] of [['certificate absent',{certificate:false}],['facing absent',{facing:false}],
    ['triangle mismatch',{badTriangle:true}],['unknown preparation cutter',{badTool:true}],
    ['ball contact absent',{missingBallContact:true}]]) {
    test(`general curved stock handoff keeps flat work when ${reason}`, () => {
        const plan = planStock(options);
        assert.equal(roughs(plan).find(op => owns(op,1)).toolDiameterMm,1);
        assert.equal(plan.operations.some(op => op.stockHandoff),false);
    });
}
