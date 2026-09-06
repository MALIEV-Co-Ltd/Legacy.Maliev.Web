const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const up = { x: 0, y: 0, z: 1 };
const ball = { family: 'ball_end_mill', diameterMm: 6, usableCutLengthMm: 12,
    underNeckLengthMm: 12, neckDiameterMm: 6, shankDiameterMm: 6, reachMm: 24, holderDiameterMm: 20 };
function strip(point, nu, nv) {
    const triangles = [];
    for (let u = 0; u < nu; u++) for (let v = 0; v < nv; v++) {
        const a=point(u/nu,v/nv),b=point((u+1)/nu,v/nv),c=point((u+1)/nu,(v+1)/nv),d=point(u/nu,(v+1)/nv);
        triangles.push(...a,...b,...c,...a,...c,...d);
    }
    return triangles;
}
function fixture(triangles, hints, faceCount=triangles.length/9) {
    const c=vm.createContext({console});c.self=c;
    for(const file of ['cnc-geometry.worker','cnc-ball-rest.worker'])vm.runInContext(fs.readFileSync(path.resolve(__dirname,
        '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation',file+'.js'),'utf8'),c);
    const records=c.CncTriangleRecords(new Float32Array(triangles));
    const verifier=c.CncBallRest.createVerifier(records,hints,[{first:0,last:faceCount-1}]);
    return { c,records,verifier };
}
const cylinderHint={kind:'cylinder',sourceId:1,centerMm:{x:0,y:0,z:0},axis:{x:0,y:1,z:0},radiusMm:10};
const cylinder=()=>strip((u,v)=>{const a=-.6+u*1.2;return [10*Math.sin(a),-2+4*v,10*Math.cos(a)];},12,1);

test('outward cylinder finishing uses the exact CAD radius and normal rather than an inscribed chord',()=>{
    const f=fixture(cylinder(),[cylinderHint]),r=f.records[8];
    const pose=f.verifier.contact({sourceTriangleIndex:r.sourceTriangleIndex,contactPosition:r.centroid},up,ball);
    assert.ok(pose);
    assert.ok(Math.abs(Math.hypot(pose.center.x,pose.center.z)-13)<1e-8,
        'the sphere centre must lie at exact surface radius10 plus ball radius3');
    assert.equal(pose.surfaceKind,'cylinder');
});

test('outward toroidal handle skin obtains an exact tube-normal ball pose while retaining full mesh clearance',()=>{
    const hint={kind:'torus',sourceId:2,centerMm:{x:0,y:0,z:0},axis:up,radiusMm:24.772,minorRadiusMm:16.272};
    const triangles=strip((u,v)=>{const theta=-.3+.6*u,phi=1.8+.4*v,r=hint.radiusMm+hint.minorRadiusMm*Math.cos(phi);
        return [r*Math.cos(theta),r*Math.sin(theta),hint.minorRadiusMm*Math.sin(phi)];},8,8);
    const f=fixture(triangles,[hint]),r=f.records[48];
    const pose=f.verifier.contact({sourceTriangleIndex:r.sourceTriangleIndex,contactPosition:r.centroid},up,ball);
    assert.ok(pose);
    assert.ok(Math.abs(Math.hypot(Math.hypot(pose.center.x,pose.center.y)-24.772,pose.center.z)-19.272)<1e-8);
    assert.equal(pose.surfaceKind,'torus');
});

test('an exact outward CAD hint cannot waive a real roof or unrelated surface',()=>{
    const triangles=cylinder(),count=triangles.length/9;
    const f=fixture([...triangles,-20,-20,15,20,-20,15,0,20,15],[cylinderHint],count),r=f.records[8];
    assert.equal(f.verifier.contact({sourceTriangleIndex:r.sourceTriangleIndex,contactPosition:r.centroid},up,ball),null);
    const g=fixture(triangles,[{...cylinderHint,radiusMm:10.01}]),s=g.records[8];
    const pose=g.verifier.contact({sourceTriangleIndex:s.sourceTriangleIndex,contactPosition:s.centroid},up,ball);
    assert.ok(!pose || pose.surfaceKind==='mesh','a nonmatching analytic hint never relocates the contact point');
});

test('only a verified concave analytic patch exposes an internal finishing radius',()=>{
    const triangles=cylinder(),inward=[];
    for(let i=0;i<triangles.length;i+=9)inward.push(...triangles.slice(i,i+3),...triangles.slice(i+6,i+9),...triangles.slice(i+3,i+6));
    const f=fixture(inward,[cylinderHint]);
    assert.equal(f.verifier.concaveRadiusMm(8),10);
    assert.equal(f.verifier.concaveRadiusMm(-1),null);
    assert.equal(fixture(triangles,[cylinderHint]).verifier.concaveRadiusMm(8),null);
    assert.equal(fixture(inward,[{...cylinderHint,radiusMm:10.01}]).verifier.concaveRadiusMm(8),null);
});

test('an outward tube withdrawal is checked on the analytic patch at a faceted inner-bend seam',()=>{
    const hint={kind:'torus',sourceId:3,centerMm:{x:0,y:0,z:0},axis:up,radiusMm:24.772,minorRadiusMm:16.272};
    const triangles=strip((u,v)=>{const theta=-.4+.8*u,phi=1.8+.5*v,r=hint.radiusMm+hint.minorRadiusMm*Math.cos(phi);
        return [r*Math.cos(theta),r*Math.sin(theta),hint.minorRadiusMm*Math.sin(phi)];},6,4);
    const f=fixture(triangles,[hint]);
    for(const index of [0,1,8,9,16,17,24,25,32,33,40,41]) {
        const record=f.records[index];
        const point=f.c.CncAdd(f.c.CncScale(record.vertices[0],.02),f.c.CncAdd(
            f.c.CncScale(record.vertices[1],.49),f.c.CncScale(record.vertices[2],.49)));
        const pose=f.verifier.contact({sourceTriangleIndex:index,contactPosition:point},up,ball);
        assert.ok(pose, 'reachable exact toroidal skin at source triangle '+index);
        assert.equal(pose.surfaceKind,'torus','the contact must not fall back to an inscribed facet');
    }
});

test('an oversized ball cannot use an outward tube hint to cross the opposite inner wall',()=>{
    const hint={kind:'torus',sourceId:4,centerMm:{x:0,y:0,z:0},axis:up,radiusMm:24.772,minorRadiusMm:16.272};
    const triangles=strip((u,v)=>{const theta=-Math.PI+2*Math.PI*u,phi=2.8+.3*v;
        const r=hint.radiusMm+hint.minorRadiusMm*Math.cos(phi);
        return [r*Math.cos(theta),r*Math.sin(theta),hint.minorRadiusMm*Math.sin(phi)];},48,4);
    const f=fixture(triangles,[hint]),record=f.records[18];
    const sample={sourceTriangleIndex:record.sourceTriangleIndex,contactPosition:record.centroid};
    assert.ok(f.verifier.contact(sample,up,ball),'the D6 ball can withdraw from the inner bend');
    const oversized={...ball,diameterMm:50,usableCutLengthMm:50,underNeckLengthMm:50,
        neckDiameterMm:50,shankDiameterMm:50,reachMm:70,holderDiameterMm:100};
    assert.equal(f.verifier.contact(sample,up,oversized),null,
        'a verified analytic face does not authorize a sphere crossing other parts of the tube');
});

test('sphere withdrawal rejects distant triangle boxes before invoking the precise ray test',()=>{
    const triangles=cylinder(),count=triangles.length/9;
    for(let i=0;i<100;i++)triangles.push(100+i,0,0,100+i,1,0,100+i,0,1);
    const f=fixture(triangles,[cylinderHint],count),record=f.records[8];
    let narrowPhaseCalls=0;
    const closest=f.c.CncTriangleClosestPoint;
    f.c.CncTriangleClosestPoint=(...args)=>{narrowPhaseCalls++;return closest(...args);};
    const pose=f.verifier.contact({sourceTriangleIndex:record.sourceTriangleIndex,
        contactPosition:record.centroid},up,ball);
    assert.equal(pose?.surfaceKind,'cylinder');
    assert.ok(narrowPhaseCalls<25,'distant boxes must not execute closest-point ray tests: '+narrowPhaseCalls);
});
