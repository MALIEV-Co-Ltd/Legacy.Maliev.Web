(function (root, factory) {
    var api = factory();
    if (typeof module === 'object' && module.exports) {
        module.exports = api;
    } else {
        root.MalievSpaceMouseNavigation = api;
    }
}(typeof self !== 'undefined' ? self : this, function () {
    'use strict';

    var CONNECTION_TIMEOUT_MS = 5000;
    var COMMAND_SET_ID = 'MALIEV_VIEW_COMMANDS';
    var FIT_COMMAND_ID = 'MALIEV_VIEW_FIT';
    var RESET_COMMAND_ID = 'MALIEV_VIEW_RESET';

    function createSpaceMouseNavigation(options) {
        if (!options || !options.THREE || !options.canvas || !options.camera ||
            (!options.controls && typeof options.getControls !== 'function')) {
            throw new Error('SpaceMouse navigation requires THREE, canvas, camera, and controls.');
        }

        var THREE = options.THREE;
        var canvas = options.canvas;
        var camera = options.camera;
        function getControls() {
            return typeof options.getControls === 'function' ? options.getControls() : options.controls;
        }

        var controls = getControls();
        var driver = null;
        var status = 'idle';
        var connectionTimer = null;
        var lookFrom = new THREE.Vector3();
        var lookDirection = new THREE.Vector3(0, 0, -1);
        var lookAperture = 0;
        var selectionOnly = false;
        var pivot = controls.target.clone();
        var pointerClient = null;

        function setStatus(nextStatus) {
            status = nextStatus;
            if (typeof canvas.setAttribute === 'function') {
                canvas.setAttribute('data-spacemouse-status', status);
            }
            if (typeof canvas.dispatchEvent === 'function' && typeof CustomEvent === 'function') {
                canvas.dispatchEvent(new CustomEvent('spacemouse-status-changed', {
                    detail: { status: status }
                }));
            }
        }

        function getModelObject() {
            return typeof options.getModelObject === 'function' ? options.getModelObject() : null;
        }

        function getBounds(object) {
            if (!object) { return null; }
            object.updateMatrixWorld(true);
            var bounds = new THREE.Box3().setFromObject(object);
            return bounds.isEmpty() ? null : bounds;
        }

        function boundsToArray(bounds) {
            return bounds ? [
                bounds.min.x, bounds.min.y, bounds.min.z,
                bounds.max.x, bounds.max.y, bounds.max.z
            ] : [0, 0, 0, 0, 0, 0];
        }

        function getViewMatrix() {
            camera.updateMatrixWorld(true);
            return camera.matrixWorld.toArray();
        }

        function setViewMatrix(columnMajorMatrix) {
            var matrix = new THREE.Matrix4().fromArray(columnMajorMatrix);
            matrix.decompose(camera.position, camera.quaternion, camera.scale);
            camera.updateMatrixWorld(true);
        }

        function setTarget(target) {
            controls = getControls();
            controls.target.fromArray(target);
            pivot.copy(controls.target);
            controls.update();
        }

        function getViewFrustum() {
            var verticalFov = THREE.MathUtils.degToRad(camera.fov);
            var bottom = -camera.near * Math.tan(verticalFov / 2);
            var left = bottom * camera.aspect;
            return [left, -left, bottom, -bottom, camera.near, camera.far];
        }

        function getPointerPosition() {
            var rect = canvas.getBoundingClientRect();
            var clientX = pointerClient ? pointerClient.x : rect.left + rect.width / 2;
            var clientY = pointerClient ? pointerClient.y : rect.top + rect.height / 2;
            var normalized = new THREE.Vector3(
                ((clientX - rect.left) / Math.max(rect.width, 1)) * 2 - 1,
                -(((clientY - rect.top) / Math.max(rect.height, 1)) * 2 - 1),
                -1);
            return normalized.unproject(camera).toArray();
        }

        function getLookAt() {
            var object = getModelObject();
            if (!object || lookDirection.lengthSq() === 0) { return null; }
            object.updateMatrixWorld(true);
            var raycaster = new THREE.Raycaster(
                lookFrom,
                lookDirection.clone().normalize(),
                camera.near,
                camera.far);
            raycaster.params.Line.threshold = Math.max(lookAperture / 2, 0.001);
            raycaster.params.Points.threshold = Math.max(lookAperture / 2, 0.001);
            var hits = raycaster.intersectObject(object, true);
            return hits.length ? hits[0].point.toArray() : null;
        }

        function publishCommands(DriverCtor) {
            if (!driver || typeof driver.update3dcontroller !== 'function' ||
                !DriverCtor.ActionTree || !DriverCtor.ActionSet || !DriverCtor.Action) {
                return;
            }
            var tree = new DriverCtor.ActionTree();
            var actionSet = tree.push(new DriverCtor.ActionSet(COMMAND_SET_ID, 'MALIEV view'));
            actionSet.push(new DriverCtor.Action(FIT_COMMAND_ID, 'Fit part', 'Fit the active part in the viewer'));
            actionSet.push(new DriverCtor.Action(RESET_COMMAND_ID, 'Reset view', 'Reset the active part view'));
            driver.update3dcontroller({ commands: { activeSet: COMMAND_SET_ID, tree: tree } });
        }

        function clearConnectionTimer() {
            if (connectionTimer !== null) {
                clearTimeout(connectionTimer);
                connectionTimer = null;
            }
        }

        function createClient(DriverCtor) {
            return {
                onConnect: function () {
                    clearConnectionTimer();
                    setStatus('connected');
                    driver.create3dmouse(canvas, options.applicationName || 'MALIEV 3D Viewer');
                },
                onDisconnect: function () {
                    clearConnectionTimer();
                    setStatus('disconnected');
                },
                on3dmouseCreated: function () { publishCommands(DriverCtor); },
                onStartMotion: function () {},
                onStopMotion: function () {},
                getCoordinateSystem: function () {
                    return [
                        1, 0, 0, 0,
                        0, 0, -1, 0,
                        0, 1, 0, 0,
                        0, 0, 0, 1
                    ];
                },
                getFov: function () { return THREE.MathUtils.degToRad(camera.fov); },
                setFov: function (fov) {
                    camera.fov = THREE.MathUtils.radToDeg(fov);
                    camera.updateProjectionMatrix();
                },
                getPerspective: function () { return camera.isPerspectiveCamera === true; },
                getViewFrustum: getViewFrustum,
                getViewMatrix: getViewMatrix,
                setViewMatrix: setViewMatrix,
                getViewRotatable: function () { return true; },
                getFrontView: function () {
                    return [1, 0, 0, 0, 0, 0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1];
                },
                getModelExtents: function () { return boundsToArray(getBounds(getModelObject())); },
                getUnitsToMeters: function () { return 0.001; },
                getViewTarget: function () {
                    controls = getControls();
                    return controls.target.toArray();
                },
                setTarget: setTarget,
                getPivotPosition: function () { return pivot.toArray(); },
                setPivotPosition: function (position) { pivot.fromArray(position); },
                setPivotVisible: function () {},
                getPointerPosition: getPointerPosition,
                setLookFrom: function (origin) { lookFrom.fromArray(origin); },
                setLookDirection: function (direction) { lookDirection.fromArray(direction); },
                setLookAperture: function (aperture) { lookAperture = aperture || 0; },
                setSelectionOnly: function (value) { selectionOnly = Boolean(value); },
                getLookAt: getLookAt,
                getSelectionEmpty: function () { return !selectionOnly || !getModelObject(); },
                getSelectionExtents: function () {
                    return boundsToArray(selectionOnly ? getBounds(getModelObject()) : null);
                },
                setActiveCommand: function (commandId) {
                    if (commandId === FIT_COMMAND_ID && typeof options.fitView === 'function') {
                        options.fitView();
                    } else if (commandId === RESET_COMMAND_ID && typeof options.resetView === 'function') {
                        options.resetView();
                    }
                },
                setTransaction: function () {}
            };
        }

        function onPointerMove(event) {
            pointerClient = { x: event.clientX, y: event.clientY };
        }

        canvas.addEventListener('pointermove', onPointerMove);

        return {
            connect: function (DriverCtor) {
                if (typeof DriverCtor !== 'function') {
                    setStatus('unavailable');
                    return false;
                }
                if (driver) { return status === 'connected' || status === 'connecting'; }
                if (canvas.tabIndex < 0) { canvas.tabIndex = 0; }
                setStatus('connecting');
                try {
                    driver = new DriverCtor(createClient(DriverCtor));
                    var result = driver.connect();
                    if (!result) {
                        setStatus('unavailable');
                        return false;
                    }
                    if (status === 'connecting' && typeof setTimeout === 'function') {
                        connectionTimer = setTimeout(function () {
                            if (status === 'connecting') { setStatus('unavailable'); }
                        }, CONNECTION_TIMEOUT_MS);
                    }
                    return true;
                } catch (error) {
                    setStatus('unavailable');
                    return false;
                }
            },
            disconnect: function () {
                clearConnectionTimer();
                canvas.removeEventListener('pointermove', onPointerMove);
                if (driver && typeof driver.delete3dmouse === 'function') {
                    driver.delete3dmouse();
                }
                driver = null;
                setStatus('disconnected');
            },
            getStatus: function () { return status; }
        };
    }

    return { createSpaceMouseNavigation: createSpaceMouseNavigation };
}));

