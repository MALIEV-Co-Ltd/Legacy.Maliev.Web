const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function planPatch({overlap = false, missing = false, flatOnly = false, reverse = false, discarded = false} = {}) {
    const c = vm.createContext({console}); c.window = c;
    for (const name of ['cnc-quotation-config','cnc-material-catalog','cnc-tool-library',
        'cnc-reach','cnc-fixture-clearance','cnc-machine-capability','cnc-planning']) {
        const file = name === 'cnc-planning'
            ? path.resolve(__dirname, 'fixtures/cnc-legacy-planning.test-helper.js')
            : path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation', name + '.js');
        let source = fs.readFileSync(file, 'utf8');
        if (name === 'cnc-planning') source = source.replace('/* CNC_TEST_INSTRUMENTATION_POINT */',
            'window.CncPlanningDiagnostics = Object.freeze({ plan: plan, planFixture: planFixture });');
        vm.runInContext(source, c);
    }
    const access = ids => Object.fromEntries(Array.from(c.CncToolLibrary.analysisProfiles())
        .filter(tool => !flatOnly || tool.family === 'flat_end_mill')
        .map(tool => [tool.id,{reachableSampleIds:ids,tipSampleIds:ids,fluteSampleIds:[]} ]));
    const patch = triangleIndexes => ({triangleIndexes,areaMm2:triangleIndexes.length*100,
        method:'triangle-normal-variation',camCertain:false});
    return c.CncPlanningDiagnostics.plan({material:'6061',requirements:{quantity:1},
        stock:{stockSizeMm:{x:45,y:55,z:25}},geometry:{
            bodyCount:1,orientedSizeMm:{x:40,y:50,z:20},partVolumeMm3:20000,partSurfaceAreaMm2:300,
            orientationCandidates:[
                {id:'front',toolDirection:{x:0,y:0,z:1},projectedFaceCoverage:.5},
                {id:'back',toolDirection:{x:0,y:0,z:-1},projectedFaceCoverage:.5}],
            surfaceClusters:[{id:'shell',type:'unresolved',areaMm2:300,filletFeatures:[],
                accessibleDirectionIds:['front','back'],curvedFinishingByDirection: Object.fromEntries([
                    ...(discarded ? [['discarded-side',patch([10,20,30])]] : []),
                    ...(reverse ? [['back',patch(overlap?[20,30]:[30])],['front',patch([10,20])]]
                        : [['front',patch([10,20])],['back',patch(overlap?[20,30]:[30])]])])}],
            generalBallFinishingAccess:flatOnly ? [] : [
                {directionId:'front',toolId:'ns-alb225-4',sampleIds:missing?[1]:[1,2],
                    triangleIndexes:missing?[10]:[10,20],method:'sampled-ball-contact',camCertain:false},
                {directionId:'back',toolId:'ns-alb225-4',sampleIds:overlap?[2,3]:[3],
                    triangleIndexes:overlap?[20,30]:[30],method:'sampled-ball-contact',camCertain:false}],
            accessibilityField:{surfaceSamples:[
                {id:1,clusterId:'shell',sourceTriangleIndex:10,areaMm2:100},
                {id:2,clusterId:'shell',sourceTriangleIndex:20,areaMm2:100},
                {id:3,clusterId:'shell',sourceTriangleIndex:30,areaMm2:100}],
                toolAccess:{front:access(missing?[1]:[1,2]),back:access(overlap?[2,3]:[3])}}
        }});
}

test('overlapping curved visibility patches give each finishing sample exactly one setup owner', () => {
    const plan = planPatch({overlap:true});
    const balls = Array.from(plan.operations).filter(op => op.sampledSurfaceFinishing && op.reachable);
    const ids = balls.flatMap(op => Array.from(op.featureSampleIds || []));
    assert.deepEqual(ids.sort(), [1,2,3], 'the overlap sample must not be charged again from the second setup');
});

test('partially unreachable curved patch retains its missing samples as explicit review work', () => {
    const plan = planPatch({missing:true});
    const balls = Array.from(plan.operations).filter(op => op.sampledSurfaceFinishing);
    assert.ok(balls.some(op => !op.reachable && Array.from(op.featureSampleIds || []).includes(2)),
        'allocating sample 1 must not silently discard unmachinable sample 2');
    assert.ok(plan.reviewReasons.includes('unreachable_tool_access'));
});

test('all-curved surfaces do not restore an unpartitioned flat finishing fallback', () => {
    const plan = planPatch();
    assert.equal(plan.operations.some(op => op.code === 'finishing' && op.reachable), false,
        'zero flat-owned samples means zero flat finishing, not the original whole-surface pass');
});

test('flat-envelope field clearance alone cannot assert ball contact on curved surfaces', () => {
    const plan = planPatch({flatOnly:true});
    assert.equal(plan.operations.some(op => op.sampledSurfaceFinishing && op.reachable), false,
        'a flat-end analysis profile is not evidence of spherical tip contact and gouge clearance');
    assert.ok(plan.reviewReasons.includes('unreachable_tool_access'));
});

test('patch property order cannot give later setups earlier curved work', () => {
    const plan = planPatch({overlap:true,reverse:true});
    const owner = plan.operations.find(op => op.sampledSurfaceFinishing && op.reachable && op.featureSampleIds.includes(2));
    assert.equal(owner.setupNumber,1);
});

test('discarded direction curvature does not create work or remove flat ownership', () => {
    const plan = planPatch({discarded:true});
    assert.equal(plan.operations.some(op => (op.allowedDirectionIds || []).includes('discarded-side')),false);
    assert.equal(plan.operations.some(op => op.sampledSurfaceFinishing && !op.reachable),false);
});
