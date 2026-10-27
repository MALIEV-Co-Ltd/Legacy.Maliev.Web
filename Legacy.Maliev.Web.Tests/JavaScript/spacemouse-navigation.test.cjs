const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const THREE = require(path.resolve(__dirname, '../../Legacy.Maliev.Web/node_modules/three-cnc-compat'));

const adapterPath = path.resolve(
    __dirname,
    '../../Legacy.Maliev.Web/wwwroot/src/app/js/model-viewer/spacemouse-navigation.js');

assert.ok(
    fs.existsSync(adapterPath),
    'The SpaceMouse navigation adapter must exist before its behavior can be exercised.');

const { createSpaceMouseNavigation } = require(adapterPath);

class FakeActionNode {
    constructor(id, label, description) {
        this.id = id;
        this.label = label;
        this.description = description;
        this.nodes = [];
    }

    push(node) {
        this.nodes.push(node);
        return node;
    }
}

class FakeDriver {
    constructor(client) {
        this.client = client;
        this.created = null;
        this.updates = [];
        this.deleted = false;
        FakeDriver.lastInstance = this;
    }

    connect() {
        this.client.onConnect();
        return true;
    }

    create3dmouse(canvas, applicationName) {
        this.created = { canvas, applicationName };
        this.client.on3dmouseCreated();
    }

    update3dcontroller(update) {
        this.updates.push(update);
        return Promise.resolve(update);
    }

    delete3dmouse() {
        this.deleted = true;
    }
}

FakeDriver.ActionTree = FakeActionNode;
FakeDriver.ActionSet = FakeActionNode;
FakeDriver.Category = FakeActionNode;
FakeDriver.Action = FakeActionNode;

function createCanvas() {
    const listeners = new Map();
    const attributes = new Map();
    return {
        tabIndex: -1,
        addEventListener(name, callback) {
            listeners.set(name, callback);
        },
        removeEventListener(name) {
            listeners.delete(name);
        },
        setAttribute(name, value) {
            attributes.set(name, String(value));
        },
        getAttribute(name) {
            return attributes.get(name) ?? null;
        },
        getBoundingClientRect() {
            return { left: 0, top: 0, width: 800, height: 600 };
        },
    };
}

function createHarness(overrides = {}) {
    const canvas = createCanvas();
    const camera = new THREE.PerspectiveCamera(45, 4 / 3, 0.1, 1000);
    camera.up.set(0, 0, 1);
    camera.position.set(8, -10, 6);
    camera.lookAt(0, 0, 0);
    camera.updateMatrixWorld(true);

    const controls = {
        target: new THREE.Vector3(0, 0, 0),
        updateCount: 0,
        update() {
            this.updateCount += 1;
        },
    };

    const model = new THREE.Mesh(
        new THREE.BoxGeometry(4, 6, 8),
        new THREE.MeshBasicMaterial());
    model.updateMatrixWorld(true);

    let fitCount = 0;
    let resetCount = 0;
    const navigation = createSpaceMouseNavigation({
        THREE,
        canvas,
        camera,
        controls,
        getModelObject: () => model,
        fitView: () => { fitCount += 1; },
        resetView: () => { resetCount += 1; },
        applicationName: 'MALIEV 3D Viewer',
        ...overrides,
    });

    return {
        canvas,
        camera,
        controls,
        model,
        navigation,
        getFitCount: () => fitCount,
        getResetCount: () => resetCount,
    };
}

test('connects the official driver to the focusable viewer and advertises view commands', async (t) => {
    const harness = createHarness();
    t.after(() => { harness.navigation.disconnect(); harness.model.geometry.dispose(); harness.model.material.dispose(); });

    assert.equal(harness.navigation.connect(FakeDriver), true);
    assert.equal(harness.canvas.tabIndex, 0);
    assert.equal(harness.navigation.getStatus(), 'connected');
    assert.equal(harness.canvas.getAttribute('data-spacemouse-status'), 'connected');
    assert.deepEqual(FakeDriver.lastInstance.created, {
        canvas: harness.canvas,
        applicationName: 'MALIEV 3D Viewer',
    });

    await Promise.resolve();
    const commandUpdate = FakeDriver.lastInstance.updates.find(update => update.commands);
    assert.ok(commandUpdate, 'The SpaceMouse properties panel should receive MALIEV view commands.');
    assert.equal(commandUpdate.commands.activeSet, 'MALIEV_VIEW_COMMANDS');
    assert.deepEqual(
        commandUpdate.commands.tree.nodes[0].nodes.map(command => command.id),
        ['MALIEV_VIEW_FIT', 'MALIEV_VIEW_RESET']);

    FakeDriver.lastInstance.client.setActiveCommand('MALIEV_VIEW_FIT');
    FakeDriver.lastInstance.client.setActiveCommand('MALIEV_VIEW_RESET');
    assert.equal(harness.getFitCount(), 1);
    assert.equal(harness.getResetCount(), 1);
});

test('applies Navigation Library camera, target, field-of-view, bounds, and hit-test callbacks', (t) => {
    const harness = createHarness();
    t.after(() => { harness.navigation.disconnect(); harness.model.geometry.dispose(); harness.model.material.dispose(); });
    harness.navigation.connect(FakeDriver);
    const client = FakeDriver.lastInstance.client;

    assert.deepEqual(client.getCoordinateSystem(), [
        1, 0, 0, 0,
        0, 0, -1, 0,
        0, 1, 0, 0,
        0, 0, 0, 1,
    ]);
    assert.deepEqual(client.getModelExtents(), [-2, -3, -4, 2, 3, 4]);
    assert.deepEqual(client.getViewTarget(), [0, 0, 0]);

    const requestedPose = new THREE.Matrix4()
        .compose(
            new THREE.Vector3(20, -15, 12),
            new THREE.Quaternion().setFromEuler(new THREE.Euler(0.2, -0.3, 0.4)),
            new THREE.Vector3(1, 1, 1));
    client.setViewMatrix(requestedPose.toArray());
    assert.ok(harness.camera.position.distanceTo(new THREE.Vector3(20, -15, 12)) < 1e-9);
    assert.ok(harness.camera.quaternion.angleTo(
        new THREE.Quaternion().setFromEuler(new THREE.Euler(0.2, -0.3, 0.4))) < 1e-7);

    client.setTarget([1, 2, 3]);
    assert.deepEqual(harness.controls.target.toArray(), [1, 2, 3]);
    assert.equal(harness.controls.updateCount, 1);

    client.setFov(Math.PI / 3);
    assert.ok(Math.abs(harness.camera.fov - 60) < 1e-9);

    client.setLookFrom([10, 0, 0]);
    client.setLookDirection([-1, 0, 0]);
    client.setLookAperture(0.1);
    client.setSelectionOnly(false);
    const hit = client.getLookAt();
    assert.ok(hit, 'The automatic pivot ray should hit the displayed part.');
    assert.ok(new THREE.Vector3().fromArray(hit).distanceTo(new THREE.Vector3(2, 0, 0)) < 1e-6);
});

test('fails closed without disturbing mouse navigation when 3DxWare is unavailable', (t) => {
    class UnavailableDriver extends FakeDriver {
        connect() {
            return false;
        }
    }

    const harness = createHarness();
    t.after(() => { harness.navigation.disconnect(); harness.model.geometry.dispose(); harness.model.material.dispose(); });
    assert.equal(harness.navigation.connect(UnavailableDriver), false);
    assert.equal(harness.navigation.getStatus(), 'unavailable');
    assert.equal(harness.controls.updateCount, 0);

    assert.doesNotThrow(() => harness.navigation.disconnect());
    assert.equal(UnavailableDriver.lastInstance.deleted, true);
});
