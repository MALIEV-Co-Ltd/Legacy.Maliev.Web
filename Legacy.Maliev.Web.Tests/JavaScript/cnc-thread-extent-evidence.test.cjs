const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const moduleRoot = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot/src/app/js/cnc-quotation');

function runtime() {
    const context = vm.createContext({ console });
    context.self = context; context.window = context;
    for (const name of ['cnc-quotation-config', 'cnc-material-catalog', 'cnc-tool-library',
        'cnc-fixture-catalog', 'cnc-manufacturing-evidence.worker']) {
        vm.runInContext(fs.readFileSync(path.join(moduleRoot, name + '.js'), 'utf8'), context);
    }
    return context;
}

function face(id, bodyId, radiusMm, centerX, minimumZ, maximumZ, adjacentFaceIds) {
    return { id, bodyId, surface: { kind: 'cylinder', axis: { x: 0, y: 0, z: 1 },
        centerMm: { x: centerX, y: 0, z: 0 }, radiusMm }, adjacentFaceIds,
        validationVolume: { minimum: { x: centerX - radiusMm, y: -radiusMm, z: minimumZ },
            maximum: { x: centerX + radiusMm, y: radiusMm, z: maximumZ } } };
}

function evidenceWithProfile(profileOptions = {}) {
    const c = runtime();
    const threadFace = face('thread-face', 'body-1', 7, 0, 0, 12, ['profile-face']);
    const profileFace = face('profile-face', profileOptions.bodyId || 'body-1', 10,
        profileOptions.centerX || 0, 0, 2, ['thread-face']);
    if (profileOptions.connected === false) {
        threadFace.adjacentFaceIds = [];
        profileFace.adjacentFaceIds = [];
    }
    const thread = { id: 'thread', bodyId: 'body-1', kind: 'external_thread',
        primaryFaceIds: ['thread-face'], accessAxes: [{ x: 0, y: 0, z: 1 }],
        nominalDiameterMm: 14, pitchMm: 1, handedness: 'right' };
    const profile = { id: 'profile', bodyId: profileFace.bodyId, kind: 'outside_profile',
        primaryFaceIds: ['profile-face'], accessAxes: [{ x: 0, y: 0, z: 1 }],
        dimensions: { widthMm: 20 } };
    const operation = { id: 'thread-op', featureId: 'thread', kind: 'thread_milling', phase: 'thread',
        toolId: 'thread-mill-4', toolClass: 'thread_mill', accessAxis: { x: 0, y: 0, z: 1 } };
    const faces = [threadFace, profileFace];
    const features = [thread, profile];
    if (profileOptions.ambiguous) {
        const secondFace = face('second-profile-face', 'body-1', 10, 0, 0, 3, ['thread-face']);
        threadFace.adjacentFaceIds.push(secondFace.id);
        faces.push(secondFace);
        features.push({ ...profile, id: 'second-profile', primaryFaceIds: [secondFace.id] });
    }
    const topology = { sourceKind: 'brep', revision: 'geometry-1', bodies: [{ id: 'body-1' }, { id: 'body-2' }], faces };
    const featureGraph = { features };
    const operationGraph = { operations: [operation] };
    const setupPlan = { inputStockState: 'stock-0', setups: [{ id: 'setup-1', inputStockState: 'stock-0',
        outputStockState: 'stock-1', operationIds: ['thread-op'] }] };
    const quotedStock = { stockShape: 'round', stockSizeMm: { x: 24, y: 24, z: 16 } };
    return c.CncManufacturingEvidence.complete(topology, featureGraph, operationGraph, setupPlan, quotedStock);
}

test('connected coaxial shoulder on the same B-Rep body truncates external thread extent', () => {
    const evidence = evidenceWithProfile();
    assert.ok(evidence);
    const semantic = evidence.operationPaths[0].semanticPath;
    assert.equal(semantic.minimumAxialMm, 2);
    assert.equal(semantic.shoulderFeatureId, 'profile');
});

test('cross-body profile cannot truncate external thread extent', () => {
    const evidence = evidenceWithProfile({ bodyId: 'body-2' });
    assert.ok(evidence);
    assert.equal(evidence.operationPaths[0].semanticPath.minimumAxialMm, 0);
    assert.equal(evidence.operationPaths[0].semanticPath.shoulderFeatureId, undefined);
});

test('offset-axis or disconnected profile cannot truncate external thread extent', () => {
    const offset = evidenceWithProfile({ centerX: 1 });
    assert.ok(offset);
    assert.equal(offset.operationPaths[0].semanticPath.minimumAxialMm, 0);
    const disconnected = evidenceWithProfile({ connected: false });
    assert.ok(disconnected);
    assert.equal(disconnected.operationPaths[0].semanticPath.minimumAxialMm, 0);
});

test('conflicting connected shoulders fail closed as ambiguous', () => {
    assert.equal(evidenceWithProfile({ ambiguous: true }), null);
});
