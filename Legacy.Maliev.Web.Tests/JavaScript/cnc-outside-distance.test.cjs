const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const context = vm.createContext({ console });
context.self = context;
const source = fs.readFileSync(path.resolve(__dirname,
    '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation/cnc-spatial-field.worker.js'), 'utf8');
vm.runInContext(source.replace('root.CncSpatialField =',
    'root.TestBuildDistance = buildSliceDistance; root.TestRay = toolCentrePathClear; root.TestToolPathClear = toolPathClear; root.TestOutsideDistance = typeof outsideSliceDistanceSquared === "function" ? outsideSliceDistanceSquared : null; root.CncSpatialField ='), context);

function field(dimensions, seeds) {
    const bits = new Uint32Array(Math.ceil(dimensions.x * dimensions.y * dimensions.z / 32));
    for (const [x, y, z] of seeds) {
        const id = x + dimensions.x * (y + dimensions.y * z);
        bits[id >>> 5] |= 1 << (id & 31);
    }
    return { dimensions, _occupancyBits: bits };
}

function exteriorCylinder(roof = false, reach = 30) {
    const dimensions = { x: 16, y: 16, z: 10 };
    const seeds = [];
    for (let z = 1; z <= 7; z++) for (let y = 0; y < 16; y++) for (let x = 0; x < 16; x++) {
        if ((z <= 6 && (x - 8) ** 2 + (y - 8) ** 2 <= 25) || (roof && z === 7)) seeds.push([x, y, z]);
    }
    const input = field(dimensions, seeds);
    Object.assign(input, { cellSizeMm: 1, origin: { x: 0, y: 0, z: 0 },
        axes: [{ x: 1, y: 0, z: 0 }, { x: 0, y: 1, z: 0 }, { x: 0, y: 0, z: 1 }],
        surfaceSamples: [{ id: 3 + 16 * (8 + 16 * 3), areaMm2: 1, normal: { x: -1, y: 0, z: 0 },
            contactPosition: { x: 3.5, y: 8.5, z: 3.5 } }] });
    const tool = { id: 'flat-10', family: 'flat_end_mill', diameterMm: 10,
        usableCutLengthMm: reach, reachMm: reach, shankDiameterMm: 10, holderDiameterMm: 20 };
    return context.CncSpatialField.classifyToolAccess(input, [tool])['positive-z']['flat-10'];
}

test('external circular profile permits a cutter centre outside the padded radial grid', () => {
    assert.equal(exteriorCylinder().fluteSampleIds.length, 1);
});

test('outside centre still collides with a real roof and oversized holder', () => {
    assert.equal(exteriorCylinder(true).reachableSampleIds.length, 0);
    assert.equal(exteriorCylinder(false, 1).reachableSampleIds.length, 0);
});

for (const axis of [0, 1, 2]) {
    test(`outside slice distances match brute-force occupied cells for axis ${axis}`, () => {
        const dimensions = { x: 7, y: 8, z: 6 }, counts = [7, 8, 6];
        const seeds = [[0, 1, 2], [6, 6, 4], [4, 4, 3], [1, 6, 2], [3, 1, 1], [3, 5, 4]];
        const distances = context.TestBuildDistance(field(dimensions, seeds), axis);
        const uAxis = (axis + 1) % 3, vAxis = (axis + 2) % 3;
        for (let slice = 0; slice < counts[axis]; slice++) {
            for (const u of [-5, -1, 2, counts[uAxis], counts[uAxis] + 4]) {
                for (const v of [-3, -1, 2, counts[vAxis], counts[vAxis] + 6]) {
                    if (u >= 0 && u < counts[uAxis] && v >= 0 && v < counts[vAxis]) continue;
                    const candidates = seeds.filter(seed => seed[axis] === slice);
                    const expected = candidates.length === 0 ? Infinity : 9 * Math.min(...candidates.map(seed =>
                        (seed[uAxis] - u) ** 2 + (seed[vAxis] - v) ** 2));
                    assert.equal(context.TestOutsideDistance(distances, slice, u, v), expected,
                        `slice=${slice}, u=${u}, v=${v}`);
                }
            }
        }
    });
}

test('outside distance reuses each radial-line slice result without rescanning extrema', () => {
    const dimensions = { x: 7, y: 8, z: 6 };
    const distances = context.TestBuildDistance(field(dimensions, [[3, 1, 1], [3, 5, 4]]), 0);
    let reads = 0;
    const original = distances._outsideExtrema.uMinimum;
    distances._outsideExtrema.uMinimum = new Proxy(original, {
        get(target, property) {
            if (/^\d+$/.test(String(property))) reads++;
            return Reflect.get(target, property, target);
        }
    });
    const first = context.TestOutsideDistance(distances, 3, -5, 2);
    const firstReads = reads;
    assert.ok(firstReads > 0);
    assert.equal(first, 333);
    assert.equal(context.TestOutsideDistance(distances, 3, -5, 2), first);
    assert.equal(reads, firstReads, 'Identical radial line and slice must use the cached distance');
    assert.equal(context.TestOutsideDistance(distances, 2, -5, 2), Infinity);
    const emptyReads = reads;
    assert.equal(context.TestOutsideDistance(distances, 2, -5, 2), Infinity);
    assert.equal(reads, emptyReads, 'Empty slices are cached too');
});

test('outside distance cache stays bounded and eviction does not change exact results', () => {
    const dimensions = { x: 7, y: 8, z: 6 };
    const distances = context.TestBuildDistance(field(dimensions, [[3, 1, 1]]), 0);
    for (let i = 1; i <= 1100; i++) {
        assert.equal(context.TestOutsideDistance(distances, 3, -i, 2), 9 * ((i + 1) ** 2 + 1));
    }
    assert.ok(distances._outsideDistanceCache.lines.size <= 1024);
    assert.equal(context.TestOutsideDistance(distances, 3, -1, 2), 45);
});

test('repeated outside cutter centres reuse the per-tool direction ray result', () => {
    const input = field({ x: 5, y: 5, z: 5 }, [[1, 2, 1], [1, 2, 2], [1, 2, 3]]);
    input.cellSizeMm = 1;
    input.axes = [{ x: 1, y: 0, z: 0 }, { x: 0, y: 1, z: 0 }, { x: 0, y: 0, z: 1 }];
    const distances = context.TestBuildDistance(input, 2);
    let reads = 0;
    const trackedDistances = new Proxy(distances, {
        get(target, property) {
            if (/^\d+$/.test(String(property))) reads++;
            return Reflect.get(target, property, target);
        }
    });
    const sample = { id: 36, normal: { x: -1, y: 0, z: 0 } };
    const tool = { family: 'flat_end_mill', diameterMm: 2, usableCutLengthMm: 20,
        reachMm: 20, shankDiameterMm: 2, holderDiameterMm: 2 };
    const cache = new Map();
    assert.equal(context.TestToolPathClear(input, trackedDistances, sample, 2, 1, tool, 'flute', cache), true);
    const firstReads = reads;
    assert.ok(firstReads > 0);
    assert.equal(context.TestToolPathClear(input, trackedDistances, sample, 2, 1, tool, 'flute', cache), true);
    assert.equal(reads, firstReads);
});

test('disk candidates blocked on their first outside slice never enter the full ray cache', () => {
    const seeds = [];
    for (let z = 0; z < 5; z++) for (let y = 0; y < 5; y++) for (let x = 0; x < 5; x++) seeds.push([x,y,z]);
    const input = field({x:5,y:5,z:5}, seeds);
    input.cellSizeMm = 1;
    input.axes = [{x:1,y:0,z:0},{x:0,y:1,z:0},{x:0,y:0,z:1}];
    const tool = { family:'flat_end_mill', diameterMm:8, usableCutLengthMm:10,
        reachMm:10, shankDiameterMm:8, holderDiameterMm:8 };
    for (const axis of [0,1,2]) for (const sign of [-1,1]) {
        const normal = {x:0,y:0,z:0}; normal[['x','y','z'][axis]] = sign;
        const cache = new Map();
        const distances = context.TestBuildDistance(input, axis);
        assert.equal(context.TestToolPathClear(input, distances, {id:62,normal}, axis, sign, tool, 'tip', cache), false);
        assert.equal(cache.size, 1, 'Only the original centre needs a complete ray query');
        assert.equal(cache._outside?.size || 0, 0, 'Impossible outside disk candidates must be filtered before cache allocation');
    }
});

test('flat disk tries positive radial escape before scanning blocked negative offsets', () => {
    const input = {dimensions:{x:41,y:41,z:3},cellSizeMm:1,
        axes:[{x:1,y:0,z:0},{x:0,y:1,z:0},{x:0,y:0,z:1}]};
    const values = new Float32Array(41*41*3);
    values[30+41*(20+41)] = 100;
    values[30+41*(20+82)] = 100;
    let reads=0;
    const distances=new Proxy(values,{get(target,property){
        if(/^\d+$/.test(String(property)))reads++;
        return Reflect.get(target,property,target);
    }});
    const tool={family:'flat_end_mill',diameterMm:20,usableCutLengthMm:100,reachMm:100,shankDiameterMm:20,holderDiameterMm:20};
    assert.equal(context.TestToolPathClear(input,distances,{id:20+41*20,normal:{x:0,y:0,z:1}},2,1,tool,'tip',new Map()),true);
    assert.ok(reads<=10, `Radial escape should not scan the entire disk; read ${reads}`);
});

test('a same-axis occupied seed rejects the complete disk without exhaustive ray queries', () => {
    const input=field({x:41,y:41,z:5},[[20,20,2]]);
    input.cellSizeMm=1;
    input.axes=[{x:1,y:0,z:0},{x:0,y:1,z:0},{x:0,y:0,z:1}];
    const values=context.TestBuildDistance(input,2);
    let reads=0;
    const distances=new Proxy(values,{get(target,property){
        if(/^\d+$/.test(String(property)))reads++;
        return Reflect.get(target,property,target);
    }});
    const tool={family:'flat_end_mill',diameterMm:20.2,usableCutLengthMm:100,reachMm:100,shankDiameterMm:20.2,holderDiameterMm:30};
    assert.equal(context.TestToolPathClear(input,distances,{id:20+41*20,normal:{x:0,y:0,z:1}},2,1,tool,'tip',new Map()),false);
    assert.ok(reads<=50,`An occupied axial witness must avoid scanning the disk; read ${reads}`);
});

test('regional clearance proof skips blocked disks without a same-axis occupied seed', () => {
    const seeds=[];
    for(let y=0;y<41;y++)for(let x=0;x<41;x++)if(x!==20||y!==20)seeds.push([x,y,2]);
    const input=field({x:41,y:41,z:5},seeds);
    input.cellSizeMm=1;
    input.axes=[{x:1,y:0,z:0},{x:0,y:1,z:0},{x:0,y:0,z:1}];
    const values=context.TestBuildDistance(input,2);
    let reads=0;
    const distances=new Proxy(values,{get(target,property){
        if(/^\d+$/.test(String(property)))reads++;
        return Reflect.get(target,property,target);
    }});
    const tool={family:'flat_end_mill',diameterMm:20.2,usableCutLengthMm:100,reachMm:100,shankDiameterMm:20.2,holderDiameterMm:30};
    assert.equal(context.TestToolPathClear(input,distances,{id:20+41*20,normal:{x:0,y:0,z:1}},2,1,tool,'tip',new Map()),false);
    assert.ok(reads<=150,`Regions should prove this off-axis obstruction without exhaustive rays; read ${reads}`);
});

test('cold exact query stops when farther rows cannot improve its nearest distance', () => {
    const seeds = Array.from({ length: 384 }, (_, row) => [0, 5, row]);
    const distances = context.TestBuildDistance(field({ x: 1, y: 64, z: 384 }, seeds), 0);
    let reads = 0;
    const original = distances._outsideExtrema.uMinimum;
    distances._outsideExtrema.uMinimum = new Proxy(original, {
        get(target, property) {
            if (/^\d+$/.test(String(property))) reads++;
            return Reflect.get(target, property, target);
        }
    });
    assert.equal(context.TestOutsideDistance(distances, 0, -1, 192), 324);
    assert.ok(reads < 20, `Near-row witness should avoid scanning all384 rows; read ${reads}`);
});

test('far outside queries use the slice radial bound instead of scanning distant rows', () => {
    const seeds = Array.from({ length: 384 }, (_, row) => [0, 5, row]);
    const distances = context.TestBuildDistance(field({ x: 1, y: 64, z: 384 }, seeds), 0);
    let reads = 0;
    const original = distances._outsideExtrema.uMinimum;
    distances._outsideExtrema.uMinimum = new Proxy(original, {
        get(target, property) {
            if (/^\d+$/.test(String(property))) reads++;
            return Reflect.get(target, property, target);
        }
    });
    assert.equal(context.TestOutsideDistance(distances, 0, -100, 192), 99225);
    assert.equal(reads, 1, 'The closest row attains the global radial lower bound');
});

test('empty slice bounds skip row scans while missing optional bounds retain exact fallback', () => {
    const distances = context.TestBuildDistance(field({ x: 2, y: 8, z: 9 }, [[0, 3, 4]]), 0);
    let reads = 0;
    const original = distances._outsideExtrema.uMinimum;
    distances._outsideExtrema.uMinimum = new Proxy(original, { get(target, property) {
        if (/^\d+$/.test(String(property))) reads++;
        return Reflect.get(target, property, target);
    } });
    assert.equal(context.TestOutsideDistance(distances, 1, -100, 4), Infinity);
    assert.equal(reads, 0);
    for (const key of ['sliceUMinimum', 'sliceUMaximum', 'sliceVMinimum', 'sliceVMaximum']) {
        delete distances._outsideExtrema[key];
    }
    assert.equal(context.TestOutsideDistance(distances, 0, -100, 4), 103 ** 2 * 9);
    assert.ok(reads > 0);
});

test('nearest-row pruning preserves exact distances for sparse slices and distant corners', () => {
    const dimensions = { x: 19, y: 23, z: 17 }, counts = [19, 23, 17];
    const seeds = Array.from({ length: 80 }, (_, i) => [i * 7 % 19, i * 11 % 23, i * 13 % 17]);
    for (const axis of [0, 1, 2]) {
        const distances = context.TestBuildDistance(field(dimensions, seeds), axis);
        const uAxis = (axis + 1) % 3, vAxis = (axis + 2) % 3;
        for (let slice = 0; slice < counts[axis]; slice++) {
            for (const u of [-100, -4, -0.5, 2, counts[uAxis] + 0.5, counts[uAxis] + 20]) {
                for (const v of [-60, -1, 2.25, counts[vAxis] + 2, 50]) {
                    if (u >= 0 && u < counts[uAxis] && v >= 0 && v < counts[vAxis]) continue;
                    const candidates = seeds.filter(seed => seed[axis] === slice);
                    const expected = candidates.length === 0 ? Infinity : 9 * Math.min(...candidates.map(seed =>
                        (seed[uAxis] - u) ** 2 + (seed[vAxis] - v) ** 2));
                    assert.equal(context.TestOutsideDistance(distances, slice, u, v), expected,
                        `axis=${axis}, slice=${slice}, u=${u}, v=${v}`);
                }
            }
        }
    }
});

test('repeated long axial lines use exact prefix queries instead of retraversing every slice', () => {
    const input = { dimensions: { x: 3, y: 3, z: 80 }, cellSizeMm: 1 };
    const values = new Float32Array(3 * 3 * 80).fill(100);
    let reads = 0;
    const distances = new Proxy(values, { get(target, property) {
        if (/^\d+$/.test(String(property))) reads++;
        return Reflect.get(target, property, target);
    } });
    const tool = { diameterMm: 2, usableCutLengthMm: 100, reachMm: 100, shankDiameterMm: 2, holderDiameterMm: 20 };
    assert.equal(context.TestRay(input, distances, [1, 1, 0], 2, 1, tool), true);
    assert.ok(reads >= 79, 'First ray stays scalar');
    assert.equal(context.TestRay(input, distances, [1, 1, 1], 2, 1, tool), true);
    const warmedReads = reads;
    assert.equal(context.TestRay(input, distances, [1, 1, 2], 2, 1, tool), true);
    assert.equal(reads, warmedReads, 'Warm line must answer from its blocked-prefix counts');
});

test('cheap axial rejection does not eagerly build a full blocked-prefix line', () => {
    const input = { dimensions: { x: 3, y: 3, z: 80 }, cellSizeMm: 1 };
    const values = new Float32Array(3 * 3 * 80);
    let reads = 0;
    const distances = new Proxy(values, { get(target, property) {
        if (/^\d+$/.test(String(property))) reads++;
        return Reflect.get(target, property, target);
    } });
    const tool = { diameterMm: 2, usableCutLengthMm: 100, reachMm: 100, shankDiameterMm: 2, holderDiameterMm: 20 };
    for (let z = 0; z < 20; z++) assert.equal(context.TestRay(input, distances, [1, 1, z], 2, 1, tool), false);
    assert.ok(reads <= 40, `Twenty immediate blockers should not scan80-cell lines; read${reads}`);
});

test('one cold ray merges equal cutter and shank radii without promoting its own prefix', () => {
    const input = { dimensions: { x: 3, y: 3, z: 80 }, cellSizeMm: 1 };
    const values = new Float32Array(3 * 3 * 80).fill(100);
    let reads = 0;
    const distances = new Proxy(values, { get(target, property) {
        if (/^\d+$/.test(String(property))) reads++;
        return Reflect.get(target, property, target);
    } });
    const tool = { diameterMm: 2, usableCutLengthMm: 20, reachMm: 100, shankDiameterMm: 2, holderDiameterMm: 20 };
    assert.equal(context.TestRay(input, distances, [1, 1, 40], 2, 1, tool), true);
    assert.equal(reads, 39, 'Cold ray should read each available cell only once');
    assert.ok([...distances._axialBlockedCache.values()].every(entry => !entry.prefix));
});

function scalarRay(input, distances, coordinates, axis, sign, tool) {
    const counts = ['x', 'y', 'z'].map(key => input.dimensions[key]);
    const rounded = coordinates.map(Math.round);
    rounded[axis] += sign;
    const u = rounded[(axis + 1) % 3], v = rounded[(axis + 2) % 3];
    let outsideSquared = 0;
    for (let i = 0; i < 3; i++) if (i !== axis) {
        const bounded = Math.max(0, Math.min(counts[i] - 1, rounded[i]));
        outsideSquared += (rounded[i] - bounded) ** 2;
        rounded[i] = bounded;
    }
    for (let step = 0; step < counts[axis] + 2; step++) {
        if (rounded[axis] < 0 || rounded[axis] >= counts[axis]) return true;
        const depth = (step + 1) * input.cellSizeMm;
        const diameter = depth <= tool.usableCutLengthMm + 1e-8 ? tool.diameterMm
            : depth <= tool.reachMm + 1e-8 ? tool.shankDiameterMm : tool.holderDiameterMm;
        const required = Math.max(1, (diameter * 0.5 / input.cellSizeMm) * 3) ** 2;
        const id = rounded[0] + counts[0] * (rounded[1] + counts[1] * rounded[2]);
        if (distances[id] ** 2 + outsideSquared * 9 <= required) {
            const exact = outsideSquared > 0 ? context.TestOutsideDistance(distances, rounded[axis], u, v) : null;
            if (exact === null || exact <= required) return false;
        }
        rounded[axis] += sign;
    }
    return true;
}

test('disk witness and regional proofs match exhaustive scalar search across randomized fields and Float32 boundaries', () => {
    let state=918237;
    const random=()=>{state=(Math.imul(state,1664525)+1013904223)>>>0;return state/4294967296;};
    const counts=[9,10,11];
    let checked=0;
    for(let fixture=0;fixture<4;fixture++) {
        const seeds=[];
        for(let z=0;z<11;z++)for(let y=0;y<10;y++)for(let x=0;x<9;x++)if(random()<0.12)seeds.push([x,y,z]);
        const input=field({x:9,y:10,z:11},seeds);
        input.cellSizeMm=0.3;input.origin={x:0,y:0,z:0};
        input.axes=[{x:1,y:0,z:0},{x:0,y:1,z:0},{x:0,y:0,z:1}];
        for(const axis of [0,1,2])for(const sign of [-1,1]) {
            const distances=context.TestBuildDistance(input,axis);
            for(let trial=0;trial<48;trial++) {
                const point=counts.map(n=>Math.floor(random()*n));
                const halfOffsets=[0,0.25,0.75,1e-12,0.5,0.5-1e-12,0.5+1e-12,0.1];
                point[(axis+1)%3]+=halfOffsets[trial%8];
                const physical=point.map(value=>(value+0.5)*input.cellSizeMm);
                const actual=physical.map(value=>value/input.cellSizeMm-0.5);
                const radius=[Math.sqrt(2),Math.sqrt(5),2,2+1e-7,3.01,4.1][trial%6];
                const diameter=radius*2*input.cellSizeMm;
                const cut=[0.3,0.6-1e-8,0.6+1e-8,1.2][trial%4];
                const tool={family:'flat_end_mill',diameterMm:diameter,usableCutLengthMm:cut,reachMm:cut+0.3,
                    shankDiameterMm:trial%3===0?diameter*0.75:diameter+0.3,holderDiameterMm:diameter+1.2};
                let expected=scalarRay(input,distances,actual,axis,sign,tool);
                const diskRadius=diameter*0.5/input.cellSizeMm,extent=Math.floor(diskRadius);
                for(let u=-extent;!expected&&u<=extent;u++)for(let v=-extent;!expected&&v<=extent;v++) {
                    if(u*u+v*v>diskRadius*diskRadius||(!u&&!v))continue;
                    const centre=actual.slice();centre[(axis+1)%3]+=u;centre[(axis+2)%3]+=v;
                    expected=scalarRay(input,distances,centre,axis,sign,tool);
                }
                const normal={x:0,y:0,z:0};normal[['x','y','z'][axis]]=sign;
                const id=Math.floor(point[0])+9*(Math.floor(point[1])+10*Math.floor(point[2]));
                const sample={id,normal,contactPosition:{x:physical[0],y:physical[1],z:physical[2]}};
                assert.equal(context.TestToolPathClear(input,distances,sample,axis,sign,tool,'tip',new Map()),expected,
                    `fixture=${fixture},axis=${axis},sign=${sign},point=${point},radius=${radius},cut=${cut}`);
                checked++;
            }
        }
    }
    assert.equal(checked,1152);
});

test('outside disk prefilter preserves scalar-search results including axial exit and holder transitions', () => {
    const counts = [9,10,11], dimensions = {x:9,y:10,z:11};
    const seeds = Array.from({length:110}, (_,i) => [i*7%9,i*3%10,i*5%11]);
    const input = field(dimensions,seeds);
    input.cellSizeMm = 0.5;
    input.axes = [{x:1,y:0,z:0},{x:0,y:1,z:0},{x:0,y:0,z:1}];
    for (const axis of [0,1,2]) for (const sign of [-1,1]) {
        const distances = context.TestBuildDistance(input,axis);
        const normal = {x:0,y:0,z:0}; normal[['x','y','z'][axis]] = sign;
        for (const point of [[0,0,0],[2,3,4],[4,5,5],[8,9,10]]) for (const diameter of [1,3,5]) {
            for (const cut of [0.2,2,20]) {
                const tool = {family:'flat_end_mill',diameterMm:diameter,usableCutLengthMm:cut,
                    reachMm:cut+0.1,shankDiameterMm:diameter+1,holderDiameterMm:diameter+3};
                let expected = scalarRay(input,distances,point,axis,sign,tool);
                const radius = diameter/(2*input.cellSizeMm), extent=Math.floor(radius);
                const uAxis=(axis+1)%3,vAxis=(axis+2)%3;
                for (let u=-extent;!expected && u<=extent;u++) for(let v=-extent;!expected && v<=extent;v++) {
                    if(u*u+v*v>radius*radius || (!u&&!v)) continue;
                    const centre=point.slice(); centre[uAxis]+=u; centre[vAxis]+=v;
                    expected=scalarRay(input,distances,centre,axis,sign,tool);
                }
                const id=point[0]+counts[0]*(point[1]+counts[1]*point[2]);
                assert.equal(context.TestToolPathClear(input,distances,{id,normal},axis,sign,tool,'tip',new Map()),expected,
                    `axis=${axis},sign=${sign},point=${point},diameter=${diameter},cut=${cut}`);
            }
        }
    }
});

test('cold and warmed prefixes exactly match scalar rays across section boundaries and outside corners', () => {
    const dimensions = { x: 21, y: 23, z: 25 }, counts = [21, 23, 25];
    const seeds = Array.from({ length: 120 }, (_, i) => [i * 7 % 21, i * 11 % 23, i * 13 % 25]);
    const input = field(dimensions, seeds);
    input.cellSizeMm = 0.3;
    for (const axis of [0, 1, 2]) {
        const distances = context.TestBuildDistance(input, axis);
        for (const sign of [-1, 1]) for (const [u, v] of [[2, 3], [-1, 3], [2, -1], [-2, -3], [27, 28]]) {
            for (const [cut, reach] of [[0, 0], [0.9, 1.8], [0.9 - 1e-8, 1.8 + 1e-8], [2.7, 0.6], [100, 100]]) {
                for (const diameter of [0.6, 0.6 * Math.sqrt(2), 1.8]) {
                    const tool = { diameterMm: diameter, usableCutLengthMm: cut, reachMm: reach,
                        shankDiameterMm: diameter + 0.6, holderDiameterMm: diameter + 2.4 };
                    for (const start of [0, 2, counts[axis] - 3, counts[axis] - 1]) {
                        const point = [];
                        point[axis] = start;
                        point[(axis + 1) % 3] = u;
                        point[(axis + 2) % 3] = v;
                        const expected = scalarRay(input, distances, point, axis, sign, tool);
                        assert.equal(context.TestRay(input, distances, point, axis, sign, tool), expected,
                            `cold axis=${axis}, sign=${sign}, point=${point}, diameter=${diameter}, cut=${cut}, reach=${reach}`);
                        // Force promotion eligibility so even immediate-collision
                        // cases exercise the exact cached range decision.
                        for (const entry of distances._axialBlockedCache?.values() || []) entry.scanned = 16;
                        assert.equal(context.TestRay(input, distances, point, axis, sign, tool), expected,
                            `warm axis=${axis}, sign=${sign}, point=${point}, cut=${cut}, reach=${reach}`);
                    }
                }
            }
        }
        assert.ok([...distances._axialBlockedCache.values()].some(entry => entry.prefix));
    }
});

test('axial prefix LRU is bounded and evicted lines retain their result', () => {
    const input = field({ x: 3, y: 3, z: 20 }, []);
    input.cellSizeMm = 1;
    const distances = context.TestBuildDistance(input, 2);
    const tool = { diameterMm: 2, usableCutLengthMm: 100, reachMm: 100, shankDiameterMm: 2, holderDiameterMm: 20 };
    for (let u = -1; u >= -4100; u--) {
        assert.equal(context.TestRay(input, distances, [u, 1, 0], 2, 1, tool), true);
        assert.equal(context.TestRay(input, distances, [u, 1, 1], 2, 1, tool), true);
    }
    assert.equal(distances._axialBlockedCache.size, 4096);
    assert.equal(distances._axialBlockedCache.has('2:-1,1:9'), false);
    assert.equal(context.TestRay(input, distances, [-1, 1, 0], 2, 1, tool), true);
    assert.equal(context.TestRay(input, distances, [-1, 1, 1], 2, 1, tool), true);
    assert.equal(distances._axialBlockedCache.size, 4096);
});
