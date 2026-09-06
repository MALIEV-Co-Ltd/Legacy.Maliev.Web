const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const modulePath = path.resolve(__dirname,
    '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-cad-surfaces.worker.js');
const context = vm.createContext({});
context.self = context;
if (fs.existsSync(modulePath)) { vm.runInContext(fs.readFileSync(modulePath, 'utf8'), context, { filename: modulePath }); }
function parse(text) {
    assert.equal(typeof context.CncCadSurfaces?.parseStep, 'function', 'STEP surface parser must exist');
    return JSON.parse(JSON.stringify(context.CncCadSurfaces.parseStep(text)));
}
const units = `#90=(LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.));
#91=(GEOMETRIC_REPRESENTATION_CONTEXT(3) GLOBAL_UNIT_ASSIGNED_CONTEXT((#90)) REPRESENTATION_CONTEXT('','3D'));`;
const placement = `#1=CARTESIAN_POINT('',(1.,2.,3.));
#2=DIRECTION('',(0.,0.,2.)); #3=DIRECTION('',(2.,0.,0.));
#4=AXIS2_PLACEMENT_3D('',#1,#2,#3);`;
function step(body, unitText = units) { return `ISO-10303-21;HEADER;ENDSEC;DATA;${body}${unitText}ENDSEC;END-ISO-10303-21;`; }

test('resolves cylinder, sphere and torus placements into normalized local millimetre hints', () => {
    const result = parse(step(`${placement}
        #5=CYLINDRICAL_SURFACE('',#4,2.);
        #6=SPHERICAL_SURFACE('',#4,3.);
        #7=TOROIDAL_SURFACE('',#4,9.5,.5);`));
    assert.deepEqual(result, [
        { kind: 'cylinder', sourceId: 5, centerMm: { x: 1, y: 2, z: 3 }, axis: { x: 0, y: 0, z: 1 }, radiusMm: 2 },
        { kind: 'sphere', sourceId: 6, centerMm: { x: 1, y: 2, z: 3 }, axis: { x: 0, y: 0, z: 1 }, radiusMm: 3 },
        { kind: 'torus', sourceId: 7, centerMm: { x: 1, y: 2, z: 3 }, axis: { x: 0, y: 0, z: 1 }, radiusMm: 9.5, minorRadiusMm: .5 }
    ]);
});

test('quoted names, comments, whitespace and exponents cannot inject entities', () => {
    const result = parse(step(`#1=CARTESIAN_POINT('name; #99=CYLINDRICAL_SURFACE(''x'',#4,99.);',(1.E+1,-2e-1,+.3));
        /* #30=SPHERICAL_SURFACE('',#4,8.); */
        #2=DIRECTION('escaped '' quote',(0,3,0));
        #3=DIRECTION('',(2,0,0)); #4=AXIS2_PLACEMENT_3D('position', #1, #2, #3);
        #5 = CYLINDRICAL_SURFACE ( 'a,b()', #4, 2E0 );`));
    assert.equal(result.length, 1);
    assert.deepEqual(result[0].centerMm, { x: 10, y: -.2, z: .3 });
    assert.deepEqual(result[0].axis, { x: 0, y: 1, z: 0 });
    assert.equal(result[0].radiusMm, 2);
});

test('missing, metre, inch and conflicting assigned length units fail closed', () => {
    const body = `${placement}#5=CYLINDRICAL_SURFACE('',#4,2.);`;
    for (const unitText of ['', units.replace('.MILLI.', '$'),
        units.replace('SI_UNIT(.MILLI.,.METRE.)', "CONVERSION_BASED_UNIT('inch',#95)"),
        `${units}#92=(LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT($,.METRE.));
         #93=GLOBAL_UNIT_ASSIGNED_CONTEXT((#92));`,
        units.replace('((#90))', '((#90,#999))')]) {
        assert.deepEqual(parse(step(body, unitText)), []);
    }
});

test('an unused metre unit does not override the assigned millimetre context', () => {
    const result = parse(step(`${placement}#5=CYLINDRICAL_SURFACE('',#4,2.);
        #92=(LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT($,.METRE.));`));
    assert.equal(result.length, 1);
    assert.equal(result[0].radiusMm, 2);
});

test('invalid radii, missing dependencies and degenerate placements produce no candidate', () => {
    for (const body of [
        `${placement}#5=CYLINDRICAL_SURFACE('',#4,0.);`,
        `${placement}#5=SPHERICAL_SURFACE('',#4,-1.);`,
        `${placement}#5=TOROIDAL_SURFACE('',#4,2.,1E999);`,
        `${placement}#5=TOROIDAL_SURFACE('',#4,2.,0.);`,
        `${placement}#5=CYLINDRICAL_SURFACE('',#999,2.);`,
        `${placement.replace('(0.,0.,2.)', '(0.,0.,0.)')}#5=CYLINDRICAL_SURFACE('',#4,2.);`,
        `${placement.replace('(2.,0.,0.)', '(0.,0.,1.)')}#5=CYLINDRICAL_SURFACE('',#4,2.);`,
        `${placement.replace('(1.,2.,3.)', '(1.,2.,1E999)')}#5=CYLINDRICAL_SURFACE('',#4,2.);`
    ]) { assert.deepEqual(parse(step(body)), []); }
});

test('optional placement directions use STEP defaults without applying instance transforms', () => {
    const result = parse(step(`#1=CARTESIAN_POINT('',(1,2,3));
        #4=AXIS2_PLACEMENT_3D('',#1,$,$);#5=SPHERICAL_SURFACE('',#4,2.);
        #10=ITEM_DEFINED_TRANSFORMATION('','',#4,#4);`));
    assert.equal(result.length, 1);
    assert.deepEqual(result[0].axis, { x: 0, y: 0, z: 1 });
    assert.deepEqual(result[0].centerMm, { x: 1, y: 2, z: 3 });
});

test('duplicate IDs and unterminated strings or comments cannot yield a partial trusted-looking result', () => {
    const body = `${placement}#5=CYLINDRICAL_SURFACE('',#4,2.);`;
    for (const suffix of ["#1=CARTESIAN_POINT('',(4,5,6));", "#8=SPHERICAL_SURFACE('unterminated", '/* unfinished']) {
        assert.deepEqual(parse(step(body) + suffix), []);
    }
});

test('input and nesting limits reject oversized input rather than partially parsing it', () => {
    assert.deepEqual(parse(' '.repeat(16 * 1024 * 1024 + 1)), []);
    assert.deepEqual(parse(step(`${placement}#5=CYLINDRICAL_SURFACE('',#4,${'('.repeat(40)}2${')'.repeat(40)});`)), []);
});

test('candidate overflow rejects the batch instead of silently dropping later surfaces', () => {
    const surfaces = Array.from({ length: 4097 }, (_, i) => `#${1000 + i}=CYLINDRICAL_SURFACE('',#4,2.);`).join('');
    assert.deepEqual(parse(step(placement + surfaces)), []);
});

test('unit-like quoted names and malformed references cannot supply missing unit evidence', () => {
    const body = `${placement}#5=CYLINDRICAL_SURFACE('',#4,2.);`;
    assert.deepEqual(parse(step(body, "#91=DESCRIPTIVE_REPRESENTATION_ITEM('GLOBAL_UNIT_ASSIGNED_CONTEXT((#90))','SI_UNIT(.MILLI.,.METRE.)');")), []);
    assert.deepEqual(parse(step(body, units.replace('#90))', '#9007199254740993))'))), []);
    assert.deepEqual(parse(step(body.replace('#4,2.', '#4,2.0foo'))), []);
    assert.deepEqual(parse(step(body.replace("('',#4,2.)", '(globalThis,#4,2.)'))), []);
});

test('parser calls are isolated and returned hints are plain serializable data', () => {
    const text = step(`${placement}#5=CYLINDRICAL_SURFACE('',#4,2.);`);
    const first = parse(text);
    first[0].centerMm.x = 999;
    assert.equal(parse(text)[0].centerMm.x, 1);
    assert.deepEqual(parse(undefined), []);
    assert.deepEqual(parse(text.replace('END-ISO-10303-21;', '')), []);
});

test('actual bracket preserves analytic R2 cylinders and R0.5 torus blends', () => {
    const text = fs.readFileSync(path.resolve(__dirname, '../TestAssets/Cnc/counterbore-pocket-bracket.step'), 'utf8');
    const result = parse(text);
    assert.ok(result.some(surface => surface.kind === 'cylinder' && surface.radiusMm === 2));
    assert.ok(result.some(surface => surface.kind === 'torus' && surface.minorRadiusMm === .5));
    assert.ok(result.some(surface => surface.kind === 'sphere' && surface.radiusMm === 2));
});
