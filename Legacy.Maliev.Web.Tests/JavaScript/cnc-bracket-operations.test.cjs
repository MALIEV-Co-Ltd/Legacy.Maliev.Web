const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

for (const fixture of [
    { file: 'counterbore-pocket-bracket.step', direction: -1 },
    { file: 'mirrored-counterbore-pocket-bracket.step', direction: 1 }
]) {
    test(fixture.file + ': counterbore side needs no small-tool cleanup staircase', async () => {
        const root = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot');
        const occt = await require(path.join(root, 'lib/occt/occt-import-js.js'))({
            wasmBinary: fs.readFileSync(path.join(root, 'lib/occt/occt-import-js.wasm'))
        });
        const model = occt.ReadStepFile(fs.readFileSync(path.resolve(__dirname,
            '../TestAssets/Cnc', fixture.file)), null);
        assert.equal(model.success, true);
        const triangles = [], cadFaceRanges = [];
        let triangleOffset = 0;
        for (const mesh of model.meshes) {
            for (const face of mesh.brep_faces || []) cadFaceRanges.push({first:triangleOffset+face.first,last:triangleOffset+face.last});
            triangleOffset += mesh.index.array.length/3;
        }
        for (const mesh of model.meshes) for (const index of mesh.index.array) {
            triangles.push(...mesh.attributes.position.array.slice(index * 3, index * 3 + 3));
        }
        let volume = 0;
        for (let i = 0; i < triangles.length; i += 9) {
            const [ax,ay,az,bx,by,bz,cx,cy,cz] = triangles.slice(i, i + 9);
            volume += (ax*(by*cz-bz*cy) + ay*(bz*cx-bx*cz) + az*(bx*cy-by*cx))/6;
        }
        const c = vm.createContext({ console });
        c.self = c; c.window = c;
        for (const name of ['cnc-quotation-config','cnc-material-catalog','cnc-tool-library',
            'cnc-reach','cnc-fixture-clearance','cnc-machine-capability','cnc-planning',
            'cnc-spatial-field.worker','cnc-geometry.worker','cnc-cad-surfaces.worker','cnc-ball-rest.worker']) {
            const file = name === 'cnc-planning'
                ? path.resolve(__dirname, 'fixtures/cnc-legacy-planning.test-helper.js')
                : path.join(root, 'src/app/js/cnc-quotation', name + '.js');
            let moduleSource = fs.readFileSync(file, 'utf8');
            if (name === 'cnc-planning') moduleSource = moduleSource.replace('/* CNC_TEST_INSTRUMENTATION_POINT */',
                'window.CncPlanningDiagnostics = Object.freeze({ plan: plan, planFixture: planFixture });');
            vm.runInContext(moduleSource, c);
        }
        const analyticSurfaces = c.CncCadSurfaces.parseStep(fs.readFileSync(path.resolve(__dirname,
            '../TestAssets/Cnc', fixture.file), 'utf8'));
        const geometry = c.AnalyzeCncGeometry(new Float32Array(triangles), {bodyCount:1,volume:Math.abs(volume),analyticSurfaces,cadFaceRanges});
        assert.doesNotThrow(() => structuredClone(geometry), 'worker result must not expose private clearance caches or callbacks');
        if (fixture.file === 'counterbore-pocket-bracket.step') {
            // A measured sloped contact in the long cavity admits a 2 mm flat
            // cutter at its rim. The former centred-envelope test rejected it.
            const contact = geometry.accessibilityField.surfaceSamples.find(sample =>
                sample.sourceTriangleIndex === 2331 && sample.contactPosition.y > 16.3
                && sample.contactPosition.y < 16.5 && sample.contactPosition.z > 39.5
                && sample.contactPosition.z < 39.8);
            assert.ok(contact, 'the actual sloped cavity contact must be sampled');
            assert.ok(geometry.accessibilityField.toolAccess['positive-x']['analysis-flat-2-2d']
                .reachableSampleIds.includes(contact.id), 'the fitting cutter must reach the sloped contact');
        }
        const plan = c.CncPlanningDiagnostics.plan({material:'6061', geometry,
            stock:{stockSizeMm:{x:110,y:65,z:15},confidence:'High'},requirements:{quantity:1}});
        assert.equal(plan.setups.length, 2);
        const counterbore = plan.setups.find(s => s.direction.x * fixture.direction > 0.99);
        assert.ok(counterbore);
        const operations = Array.from(plan.operations).filter(o=>o.setupNumber===counterbore.number);
        const milling = operations.filter(o => ['facing','roughing','finishing'].includes(o.code));
        assert.deepEqual(milling.map(o=>[o.code,o.toolDiameterMm]),
            [['facing',40],['roughing',10],['finishing',10]]);
        assert.ok(operations.every(o => ['facing','spot_drilling','drilling','roughing','finishing','deburring'].includes(o.code)));
        assert.ok(operations.every(o=>o.reachable));
        const drills = Array.from(plan.operations).filter(o=>o.code==='drilling');
        assert.equal(drills.length, 7);
        assert.equal(new Set(drills.map(o=>o.setupNumber)).size, 1);
        assert.ok(drills.every(o=>o.toolDiameterMm===6.6));
        // Curved transition patches may have separate, disjoint finishing passes,
        // but reuse the two radius-matched cutters already needed by the fillets.
        const balls = Array.from(plan.operations).filter(o=>o.toolFamily==='ball_end_mill' && o.reachable);
        assert.deepEqual([...new Set(balls.map(o=>o.toolDiameterMm))].sort((a,b)=>a-b), [1,4]);
        assert.ok(Array.from(plan.operations).filter(o=>o.sampledSurfaceFinishing && !o.reachable)
            .every(o=>o.toolId===null && o.toolDiameterMm===null), 'unverified transition regions must request review, not invent another cutter');
        const sampledBallIds = balls.filter(o=>o.sampledSurfaceFinishing && o.reachable)
            .flatMap(o=>Array.from(o.featureSampleIds || []));
        assert.equal(sampledBallIds.length, new Set(sampledBallIds).size, 'transition finishing cannot duplicate sampled work');
        const cavity = plan.setups.find(s=>s!==counterbore);
        assert.ok(!Array.from(plan.operations).some(o=>o.setupNumber===cavity.number
            && o.toolFamily==='flat_end_mill' && o.toolDiameterMm===1), 'hand off prepared stock to the R0.5 ball, not another 1mm flat pass');
        const ball = Array.from(plan.operations).find(o=>o.toolFamily==='ball_end_mill' && o.toolDiameterMm===1);
        assert.ok(ball.stockHandoff && ball.stockHandoff.passes.length>=2);
        assert.equal(ball.toolId, 'ns-alb225-1-lu5');
        assert.ok(ball.stockHandoff.axialStepMm<=.25);
        assert.ok(ball.stockHandoff.preparationSampleIds.length>0);
        assert.equal(ball.stockHandoff.camCertain, false);
        assert.ok(ball.cuttingMinutes>=ball.stockHandoff.cuttingMinutes);
        const legacyPlan=c.CncPlanningDiagnostics.plan({material:'6061',geometry:{...geometry,ballRestHandoffs:[]},
            stock:{stockSizeMm:{x:110,y:65,z:15},confidence:'High'},requirements:{quantity:1}});
        assert.ok(Array.from(legacyPlan.operations).some(o=>o.toolFamily==='flat_end_mill'&&o.toolDiameterMm===1), 'missing preparation evidence must retain flat cleanup');
        const usedId=ball.stockHandoff.preparationSampleIds[0];
        const partialPlan=c.CncPlanningDiagnostics.plan({material:'6061',geometry:{...geometry,
            ballRestHandoffs:geometry.ballRestHandoffs.filter(h=>h.sampleId!==usedId)},
            stock:{stockSizeMm:{x:110,y:65,z:15},confidence:'High'},requirements:{quantity:1}});
        assert.ok(Array.from(partialPlan.operations).some(o=>o.toolFamily==='flat_end_mill'&&o.toolDiameterMm===1), 'one missing sampled region must not erase an entire flat operation');
        assert.ok(!Array.from(partialPlan.operations).some(o=>o.stockHandoff && !o.sampledSurfaceFinishing),
            'the incomplete radius-matched strip cannot borrow independent curved-surface handoffs');
        const unverifiedBall=c.CncPlanningDiagnostics.plan({material:'6061',geometry:{...geometry,ballRestFinishingAccess:[]},
            stock:{stockSizeMm:{x:110,y:65,z:15},confidence:'High'},requirements:{quantity:1}});
        assert.ok(Array.from(unverifiedBall.operations).some(o=>o.toolFamily==='flat_end_mill'&&o.toolDiameterMm===1), 'new physical ball envelope must cover original finishing work too');
        assert.ok(plan.totalMinutesPerPart>0 && Number.isFinite(plan.totalMinutesPerPart));
        // The legacy plan omitted the extra ball layers, so a lower total is not
        // a sound oracle. Require real, finite incremental cutting time instead.
        assert.ok(ball.stockHandoff.restCuttingMinutes>0);
        assert.ok(ball.cuttingMinutes>=ball.stockHandoff.finalFinishingMinutes+ball.stockHandoff.restCuttingMinutes);
        for (const code of ['roughing','finishing']) {
            const allocated = Array.from(plan.operations).filter(o=>o.code===code);
            const samples = allocated.flatMap(o=>Array.from(o.featureSampleIds || []));
            assert.equal(samples.length, new Set(samples).size, 'no duplicated cutting allocation');
            for (const setup of plan.setups) {
                const diameters=allocated.filter(o=>o.setupNumber===setup.number).map(o=>o.toolDiameterMm);
                assert.deepEqual(diameters,[...diameters].sort((a,b)=>b-a),
                    code+' cutters must remain ordered largest to smallest in setup '+setup.number);
            }
        }
    });
}
