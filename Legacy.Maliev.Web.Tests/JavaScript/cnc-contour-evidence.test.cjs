const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

test('prismatic contour evidence requires every facet to share an extrusion direction', () => {
    const c = vm.createContext({ console }); c.self = c;
    vm.runInContext(fs.readFileSync(path.resolve(__dirname,
        '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-geometry.worker.js'), 'utf8'), c);
    const axes = [{x:1,y:0,z:0},{x:0,y:1,z:0},{x:0,y:0,z:1}];
    const cluster = {memberIndexes:[0,1,2]};
    const records = [{normal:{x:0,y:1,z:0}}, {normal:{x:0,y:0,z:1}},
        {normal:{x:0,y:Math.SQRT1_2,z:Math.SQRT1_2}}];
    assert.deepEqual(JSON.parse(JSON.stringify(c.CncPrismaticContourAxis(cluster, records, axes))), axes[0]);
    records.push({normal:{x:Math.SQRT1_2,y:0,z:Math.SQRT1_2}});
    cluster.memberIndexes.push(3);
    assert.equal(c.CncPrismaticContourAxis(cluster, records, axes), null);
});

test('coarse stock clearance cannot erase exact CAD face visibility', () => {
    const c = vm.createContext({ console }); c.self = c;
    vm.runInContext(fs.readFileSync(path.resolve(__dirname,
        '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-geometry.worker.js'), 'utf8'), c);
    const vertices = [[0,0,0],[10,0,0],[10,10,0],[0,10,0],[0,0,10],[10,0,10],[10,10,10],[0,10,10]];
    const faces = [[0,2,1],[0,3,2],[4,5,6],[4,6,7],[0,1,5],[0,5,4],
        [1,2,6],[1,6,5],[2,3,7],[2,7,6],[3,0,4],[3,4,7]];
    const mesh = new Float32Array(faces.flatMap(face => face.flatMap(index => vertices[index])));
    const exact = c.AnalyzeCncGeometry(mesh, {bodyCount:1});
    c.CncToolLibrary = {analysisProfiles:()=>[],get:()=>({family:'flat_end_mill'})};
    c.CncSpatialField = {
        build:()=>({cellSizeMm:1,surfaceSamples:[{id:1,position:{x:6,y:3,z:10},normal:{x:0,y:0,z:1},areaMm2:1}]}),
        classifyToolAccess:field=>{ field.toolAccess = {'positive-z':{flat:{reachableSampleIds:[]}}}; },
        serialize:field=>field
    };
    const withStock = c.AnalyzeCncGeometry(mesh, {bodyCount:1});
    const visibility = result => result.surfaceClusters.map(cluster => cluster.accessibleTriangleIndexesByDirection);
    assert.deepEqual(visibility(withStock), visibility(exact));
    assert.equal(withStock.accessibilityField.surfaceSamples[0].sourceTriangleIndex, 2);
});
