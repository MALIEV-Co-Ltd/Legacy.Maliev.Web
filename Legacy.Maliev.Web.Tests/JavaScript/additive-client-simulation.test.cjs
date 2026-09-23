const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const workerPath = path.resolve(
    __dirname,
    '../../Legacy.Maliev.Web/wwwroot/src/app/js/additive-quotation/additive-simulation.worker.js');

function loadWorker() {
    const context = vm.createContext({
        console,
        Math,
        Number,
        Array,
        ArrayBuffer,
        Float32Array,
        Uint32Array,
        Map,
        Set,
        JSON,
        self: {},
    });
    vm.runInContext(fs.readFileSync(workerPath, 'utf8'), context, { filename: workerPath });
    return context.self.MalievAdditiveSimulation;
}

function loadSectionSegments() {
    const context = vm.createContext({
        console, Math, Number, Array, ArrayBuffer, Float32Array, Uint32Array,
        Map, Set, JSON, self: {},
    });
    const source = fs.readFileSync(workerPath, 'utf8').replace(
        'scope.MalievAdditiveSimulation = api;',
        'api.sectionSegmentsForTest = sectionSegments; scope.MalievAdditiveSimulation = api;');
    vm.runInContext(source, context, { filename: workerPath });
    return context.self.MalievAdditiveSimulation.sectionSegmentsForTest;
}

test('STEP face seams use the same section point for either shared-edge winding', () => {
    const sectionSegments = loadSectionSegments();
    // Two triangles from the same planar wall share the diagonal edge in opposite
    // directions. At z=0.9, floating-point interpolation straddles a 0.001 mm
    // rounding boundary even though the geometric crossing is identical.
    const triangles = new Float64Array([
        96.25, -71.375, 60, -96.25, -71.375, 60, -96.25, -71.375, 0,
        96.25, -71.375, 60, -96.25, -71.375, 0, 96.25, -71.375, 0,
    ]);
    const segments = sectionSegments(triangles, new Set([0, 9]), 0.9);
    const endpoints = segments.flatMap(segment => [
        [segment.firstKey, segment.first], [segment.secondKey, segment.second],
    ]);
    const shared = endpoints.filter(([, point]) => Math.abs(point[0] + 93.3625) < 1e-6);

    assert.equal(shared.length, 2);
    assert.equal(shared[0][0], shared[1][0]);
});

function box(x0, y0, z0, x1, y1, z1) {
    const p = [
        [x0, y0, z0], [x1, y0, z0], [x1, y1, z0], [x0, y1, z0],
        [x0, y0, z1], [x1, y0, z1], [x1, y1, z1], [x0, y1, z1],
    ];
    const faces = [
        [0, 2, 1], [0, 3, 2], [4, 5, 6], [4, 6, 7],
        [0, 1, 5], [0, 5, 4], [1, 2, 6], [1, 6, 5],
        [2, 3, 7], [2, 7, 6], [3, 0, 4], [3, 4, 7],
    ];
    return faces.flatMap(face => face.flatMap(index => p[index]));
}

function mesh(triangles) {
    return [{
        position: new Float32Array(triangles),
        index: null,
        matrix: new Float32Array([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]),
    }];
}

function subdividedBox(size, divisions) {
    const triangles = [];
    const point = (face, u, v) => {
        switch (face) {
        case 0: return [0, u, v];
        case 1: return [size, u, v];
        case 2: return [u, 0, v];
        case 3: return [u, size, v];
        case 4: return [u, v, 0];
        default: return [u, v, size];
        }
    };
    for (let face = 0; face < 6; face += 1) {
        for (let row = 0; row < divisions; row += 1) {
            for (let column = 0; column < divisions; column += 1) {
                const u0 = size * column / divisions;
                const u1 = size * (column + 1) / divisions;
                const v0 = size * row / divisions;
                const v1 = size * (row + 1) / divisions;
                const corners = [
                    point(face, u0, v0), point(face, u1, v0),
                    point(face, u1, v1), point(face, u0, v1),
                ];
                const order = face % 2 === 0
                    ? [0, 2, 1, 0, 3, 2]
                    : [0, 1, 2, 0, 2, 3];
                order.forEach(index => triangles.push(...corners[index]));
            }
        }
    }
    return triangles;
}

function reverseWinding(triangles) {
    const reversed = [];
    for (let offset = 0; offset < triangles.length; offset += 9) {
        reversed.push(
            ...triangles.slice(offset, offset + 3),
            ...triangles.slice(offset + 6, offset + 9),
            ...triangles.slice(offset + 3, offset + 6));
    }
    return reversed;
}

function build(overrides = {}) {
    return {
        layerHeightMm: 0.2,
        wallLoops: 2,
        sparseInfillDensity: 0.15,
        outerWallLineWidthMm: 0.42,
        innerWallLineWidthMm: 0.45,
        solidInfillLineWidthMm: 0.42,
        topShellLayers: 5,
        bottomShellLayers: 3,
        support: {
            enabled: true,
            mode: 'normal',
            overhangAngleFromHorizontalDegrees: 30,
            topZGapMm: 0.2,
            bodyDensity: 0.15,
            interfaceDensity: 0.8,
            interfaceLayers: 2,
            bodyLineWidthMm: 0.45,
            interfaceLineWidthMm: 0.42,
        },
        speeds: {
            outerWall: 60,
            innerWall: 150,
            solidInfill: 180,
            sparseInfill: 180,
            bridge: 50,
            supportBody: 150,
            supportInterface: 80,
            travel: 500,
        },
        motion: {
            maximumXYSpeedMmPerSecond: 500,
            printingAccelerationMmPerSecondSquared: 20000,
            minimumLayerTimeSeconds: 4,
            minimumCoolingSpeedMmPerSecond: 20,
            maximumVolumetricFlowMm3PerSecond: 12,
            setupSeconds: 30,
            nozzleHeatSeconds: 60,
            bedHeatSeconds: 90,
            maximumZSpeedMmPerSecond: 20,
            firstLayerSpeedMultiplier: 0.5,
            retractLengthMm: 0.8,
            retractSpeedMmPerSecond: 40,
        },
        ...overrides,
    };
}

test('client simulation slices every deposited layer and build settings change physical output', () => {
    const simulator = loadWorker();
    const part = mesh(box(0, 0, 0, 10, 10, 10));
    const standard = simulator.simulateFdm({ meshes: part, build: build(), materialId: 'PLA' });
    const quality = simulator.simulateFdm({
        meshes: part,
        build: build({ layerHeightMm: 0.1 }),
        materialId: 'PLA',
    });
    const strength = simulator.simulateFdm({
        meshes: part,
        build: build({ wallLoops: 6, sparseInfillDensity: 0.25 }),
        materialId: 'PLA',
    });

    assert.equal(standard.layerCount, 50);
    assert.equal(quality.layerCount, 100);
    assert.ok(standard.roles.internalSolid.depositedMm3 > 0);
    assert.equal(standard.roles.solidInfill, undefined);
    assert.ok(quality.totalSeconds > standard.totalSeconds);
    assert.ok(strength.roles.innerWall.depositedMm3 > standard.roles.innerWall.depositedMm3);
    assert.ok(strength.totalDepositedMm3 > standard.totalDepositedMm3);
});

test('client simulation projects overhang support and applies one layer-time budget to model plus support', () => {
    const simulator = loadWorker();
    const stem = box(4, 4, 0, 6, 6, 6);
    const cap = box(0, 0, 6, 10, 10, 8);
    const profile = build({
        motion: {
            maximumXYSpeedMmPerSecond: 500,
            printingAccelerationMmPerSecondSquared: 20000,
            minimumLayerTimeSeconds: 8,
            minimumCoolingSpeedMmPerSecond: 20,
            maximumVolumetricFlowMm3PerSecond: 12,
            setupSeconds: 30,
            nozzleHeatSeconds: 60,
            bedHeatSeconds: 90,
        },
    });
    const result = simulator.simulateFdm({ meshes: mesh(stem.concat(cap)), build: profile, materialId: 'PLA' });

    assert.ok(result.roles.supportBody.depositedMm3 > 0);
    assert.ok(result.roles.supportInterface.depositedMm3 > 0);
    assert.ok(result.coolingSeconds > 0);
    assert.ok(result.totalSeconds >= (result.layerCount * 8) + 90);
});

test('client simulation preserves material for disconnected solids with opposite winding', () => {
    const simulator = loadWorker();
    const first = box(0, 0, 0, 10, 10, 2);
    const second = box(20, 0, 0, 30, 10, 2);
    const firstOnly = simulator.simulateFdm({
        meshes: mesh(first),
        build: build({ brimWidthMm: 0 }),
        materialId: 'TPU',
    });
    const secondOnly = simulator.simulateFdm({
        meshes: mesh(second),
        build: build({ brimWidthMm: 0 }),
        materialId: 'TPU',
    });
    const expectedDeposit = firstOnly.totalDepositedMm3 + secondOnly.totalDepositedMm3;
    const sameWinding = simulator.simulateFdm({
        meshes: mesh(first.concat(second)),
        build: build({ brimWidthMm: 0 }),
        materialId: 'TPU',
    });
    const oppositeWinding = simulator.simulateFdm({
        meshes: mesh(first.concat(reverseWinding(second))),
        build: build({ brimWidthMm: 0 }),
        materialId: 'TPU',
    });

    assert.ok(expectedDeposit > 0);
    assert.ok(Math.abs(sameWinding.totalDepositedMm3 - expectedDeposit) / expectedDeposit < 0.05);
    assert.ok(Math.abs(oppositeWinding.totalDepositedMm3 - sameWinding.totalDepositedMm3) < 0.001);
});

test('client simulation rejects a globally open mesh even when every sampled slice is closed', () => {
    const simulator = loadWorker();
    const openTop = box(0, 0, 0, 10, 10, 2);
    openTop.splice(18, 18);

    assert.throws(() => simulator.simulateFdm({
        meshes: mesh(openTop),
        build: build({ brimWidthMm: 0 }),
        materialId: 'PLA',
    }), /geometry_requires_review/);
});

test('client simulation ignores collapsed seam triangles during manifold validation', () => {
    const simulator = loadWorker();
    const closedWithCollapsedSeam = box(0, 0, 0, 10, 10, 2).concat([
        0, 0, 0,
        0, 0, 0,
        0, 0, 0,
    ]);

    const result = simulator.simulateFdm({
        meshes: mesh(closedWithCollapsedSeam),
        build: build({ brimWidthMm: 0 }),
        materialId: 'PLA',
    });

    assert.equal(result.layerCount, 10);
});

test('client simulation prices a valid multi-body mesh above the former intersection cutoff', () => {
    const simulator = loadWorker();
    const manyClosedBodies = [];
    for (let index = 0; index < 1500; index += 1) {
        const x = (index % 50) * 1.2;
        const y = Math.floor(index / 50) * 1.2;
        manyClosedBodies.push(...box(x, y, 0, x + 1, y + 1, 20));
    }

    const result = simulator.simulateFdm({
        meshes: mesh(manyClosedBodies),
        build: build({ brimWidthMm: 0 }),
        materialId: 'PLA',
    });

    assert.equal(result.layerCount, 100);
    assert.ok(result.totalDepositedMm3 > 0);
});

test('pricing analysis simplifies a dense closed mesh without changing the uploaded mesh', () => {
    const simulator = loadWorker();
    const source = mesh(subdividedBox(10, 40));
    const sourcePositionLength = source[0].position.length;
    const prepared = simulator.preparePricingTriangles(source, {
        maxTriangles: 2000,
        maxLayerTriangleIntersections: 12000,
        layerHeightMm: 0.2,
    });

    assert.equal(prepared.sourceTriangleCount, 19200);
    assert.ok(prepared.analysisTriangleCount <= 2000);
    assert.ok(prepared.estimatedLayerTriangleIntersections <= 12000);
    assert.equal(prepared.simplified, true);
    assert.equal(source[0].position.length, sourcePositionLength);

    const denseResult = simulator.simulateFdm({
        triangles: prepared.triangles,
        build: build({ brimWidthMm: 0 }),
        materialId: 'PLA',
    });
    const referenceResult = simulator.simulateFdm({
        meshes: mesh(box(0, 0, 0, 10, 10, 10)),
        build: build({ brimWidthMm: 0 }),
        materialId: 'PLA',
    });
    const relativeDepositDifference = Math.abs(
        denseResult.totalDepositedMm3 - referenceResult.totalDepositedMm3)
        / referenceResult.totalDepositedMm3;

    assert.equal(denseResult.layerCount, 50);
    assert.ok(relativeDepositDifference < 0.08);
});

test('client simulation does not treat a whole coarse grid cell as valid overhang reach', () => {
    const simulator = loadWorker();
    const shiftedLayers = [];
    for (let layer = 0; layer < 10; layer += 1) {
        shiftedLayers.push(...box(
            layer * 0.5,
            0,
            layer * 0.2,
            10 + (layer * 0.5),
            10,
            (layer + 1) * 0.2));
    }

    // This distant tower widens the occupancy grid. It must not turn a physical 0.5 mm
    // per-layer shift into a supported overhang when the 30-degree reach is only 0.346 mm.
    shiftedLayers.push(...box(60, 0, 0, 61, 1, 2));
    const result = simulator.simulateFdm({
        meshes: mesh(shiftedLayers),
        build: build({ brimWidthMm: 0 }),
        materialId: 'TPU',
    });

    assert.ok(result.supportDepositedMm3 > 0);
});

test('client simulation prices standard support from explicit rectilinear body and interface paths', () => {
    const simulator = loadWorker();
    const part = mesh(box(4, 4, 0, 6, 6, 6).concat(box(0, 0, 6, 10, 10, 8)));
    const sparse = simulator.simulateFdm({
        meshes: part,
        build: build({ support: { ...build().support, bodyDensity: 0.1 } }),
        materialId: 'TPU',
    });
    const dense = simulator.simulateFdm({
        meshes: part,
        build: build({ support: { ...build().support, bodyDensity: 0.3 } }),
        materialId: 'TPU',
    });

    assert.ok(dense.roles.supportBody.depositedMm3 > sparse.roles.supportBody.depositedMm3);
    assert.ok(dense.roles.supportBody.seconds > sparse.roles.supportBody.seconds);
    assert.throws(() => simulator.simulateFdm({
        meshes: part,
        build: build({ support: { ...build().support, mode: 'tree' } }),
        materialId: 'TPU',
    }), /support_mode_unsupported/);
});

test('client simulation accounts for every path transition and versioned machine preparation steps', () => {
    const simulator = loadWorker();
    const part = mesh(box(0, 0, 0, 5, 5, 1).concat(box(20, 0, 0, 25, 5, 1)));
    const baseMotion = build().motion;
    const withoutRetraction = simulator.simulateFdm({
        meshes: part,
        build: build({ motion: { ...baseMotion, retractLengthMm: 0 } }),
        materialId: 'TPU',
    });
    const explicitMachine = simulator.simulateFdm({
        meshes: part,
        build: build({ motion: {
            ...baseMotion,
            machinePrepareCompensationSeconds: 260,
            machineLoadFilamentSeconds: 29,
        } }),
        materialId: 'TPU',
    });

    assert.ok(withoutRetraction.travelSeconds > 0);
    assert.ok(explicitMachine.travelSeconds > withoutRetraction.travelSeconds);
    assert.equal(explicitMachine.preparationSeconds, 409);
});

test('client resin simulation uses real layer count, projected support, raft, and full-layer cycle time', () => {
    const simulator = loadWorker();
    const result = simulator.simulateResin({
        heightMm: 10,
        volumeMm3: 1000,
        footprintMm2: 100,
        areaProfileMm2: [25, 25, 100, 100],
        unsupportedAreaProfileMm2: [0, 0, 75, 0],
    });

    assert.equal(result.layerCount, 220);
    assert.ok(result.supportResinMl > 0);
    assert.ok(result.raftResinMl > 0);
    assert.ok(result.totalResinMl > result.modelResinMl);
    assert.ok(result.printMinutes > 0);
});
