(function (global) {
    'use strict';

    if (global.MalievFinishColorMatcherPreview) {
        return;
    }

    const materialSettings = {
        matte: { roughness: 0.96, clearcoat: 0, clearcoatRoughness: 1, envMapIntensity: 0.12, bumpScale: 0.012, reflectivity: 0.28 },
        satin: { roughness: 0.5, clearcoat: 0.48, clearcoatRoughness: 0.38, envMapIntensity: 0.42, bumpScale: 0.006, reflectivity: 0.5 },
        gloss: { roughness: 0.16, clearcoat: 1, clearcoatRoughness: 0.055, envMapIntensity: 0.92, bumpScale: 0.0015, reflectivity: 0.62 },
    };

    function linearSrgbColor(THREE, value) {
        return new THREE.Color(value).convertSRGBToLinear();
    }

    function createSurfaceTexture(THREE) {
        const textureCanvas = document.createElement('canvas');
        textureCanvas.width = 128;
        textureCanvas.height = 128;
        const context = textureCanvas.getContext('2d');

        if (!context) {
            return null;
        }

        const image = context.createImageData(textureCanvas.width, textureCanvas.height);
        let seed = 4177;
        for (let index = 0; index < image.data.length; index += 4) {
            seed = (seed * 16807) % 2147483647;
            const grain = 232 + (seed % 20);
            image.data[index] = grain;
            image.data[index + 1] = grain;
            image.data[index + 2] = grain;
            image.data[index + 3] = 255;
        }
        context.putImageData(image, 0, 0);

        const texture = new THREE.CanvasTexture(textureCanvas);
        texture.wrapS = THREE.RepeatWrapping;
        texture.wrapT = THREE.RepeatWrapping;
        texture.repeat.set(3.5, 2.1);
        texture.anisotropy = 4;
        return texture;
    }

    function roundedRectangle(THREE, width, height, radius) {
        const left = -width / 2;
        const bottom = -height / 2;
        const right = width / 2;
        const top = height / 2;
        const shape = new THREE.Shape();

        shape.moveTo(left + radius, bottom);
        shape.lineTo(right - radius, bottom);
        shape.quadraticCurveTo(right, bottom, right, bottom + radius);
        shape.lineTo(right, top - radius);
        shape.quadraticCurveTo(right, top, right - radius, top);
        shape.lineTo(left + radius, top);
        shape.quadraticCurveTo(left, top, left, top - radius);
        shape.lineTo(left, bottom + radius);
        shape.quadraticCurveTo(left, bottom, left + radius, bottom);
        return shape;
    }

    function create(options) {
        const THREE = global.THREE;
        const stage = options?.stage;
        const canvas = options?.canvas;

        if (!THREE || !stage || !canvas || !THREE.WebGLRenderer || !THREE.MeshPhysicalMaterial) {
            return null;
        }

        let renderer;
        try {
            renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: false, powerPreference: 'high-performance' });
        } catch (_error) {
            return null;
        }

        renderer.setPixelRatio(Math.min(Math.max(global.devicePixelRatio || 1, 1), 2));
        renderer.outputEncoding = THREE.sRGBEncoding;
        renderer.toneMapping = THREE.ACESFilmicToneMapping;
        renderer.toneMappingExposure = 0.82;
        renderer.physicallyCorrectLights = true;
        renderer.shadowMap.enabled = true;
        renderer.shadowMap.type = THREE.PCFSoftShadowMap;

        const scene = new THREE.Scene();
        scene.background = linearSrgbColor(THREE, 0xe9eff6);

        const camera = new THREE.PerspectiveCamera(31, 1, 0.1, 30);
        camera.position.set(0, 0.15, 6.2);
        camera.lookAt(0, 0, 0);

        const geometry = new THREE.ExtrudeGeometry(roundedRectangle(THREE, 3.5, 2.05, 0.2), {
            depth: 0.14,
            bevelEnabled: true,
            bevelSegments: 8,
            bevelSize: 0.055,
            bevelThickness: 0.045,
            curveSegments: 20,
            steps: 1,
        });
        geometry.center();

        const surfaceTexture = createSurfaceTexture(THREE);
        const environmentResources = [];
        let environmentTarget = null;
        let pmremGenerator = null;

        if (THREE.PMREMGenerator) {
            try {
                const environment = new THREE.Scene();
                environment.background = linearSrgbColor(THREE, 0xcfd9e5);
                const keyCardMaterial = new THREE.MeshBasicMaterial({ color: linearSrgbColor(THREE, 0xffffff), side: THREE.DoubleSide });
                const keyCardGeometry = new THREE.PlaneGeometry(5.2, 1.5);
                const keyCard = new THREE.Mesh(keyCardGeometry, keyCardMaterial);
                keyCard.position.set(-3.8, 4.1, 4.5);
                keyCard.lookAt(0, 0, 0);
                environment.add(keyCard);
                environmentResources.push(keyCardGeometry, keyCardMaterial);
                pmremGenerator = new THREE.PMREMGenerator(renderer);
                environmentTarget = pmremGenerator.fromScene(environment, 0.04, 0.1, 40);
            } catch (_error) {
                environmentTarget = null;
                pmremGenerator?.dispose();
                pmremGenerator = null;
            }
        }

        const material = new THREE.MeshPhysicalMaterial({
            color: linearSrgbColor(THREE, 0xa2474f),
            metalness: 0,
            roughness: materialSettings.satin.roughness,
            clearcoat: materialSettings.satin.clearcoat,
            clearcoatRoughness: materialSettings.satin.clearcoatRoughness,
            reflectivity: materialSettings.satin.reflectivity,
            envMap: environmentTarget?.texture || null,
            envMapIntensity: materialSettings.satin.envMapIntensity,
            bumpMap: surfaceTexture,
            bumpScale: materialSettings.satin.bumpScale,
        });
        const coupon = new THREE.Mesh(geometry, material);
        coupon.castShadow = true;
        coupon.receiveShadow = true;
        coupon.rotation.set(-0.2, -0.16, -0.035);
        coupon.position.y = 0.16;
        scene.add(coupon);

        const ground = new THREE.Mesh(
            new THREE.PlaneGeometry(9, 6),
            new THREE.ShadowMaterial({ color: linearSrgbColor(THREE, 0x102c4e), opacity: 0.14 }));
        ground.rotation.x = -Math.PI / 2;
        ground.position.y = -1.35;
        ground.receiveShadow = true;
        scene.add(ground);

        const key = new THREE.SpotLight(0xffffff, 72, 20, 0.58, 0.78, 1.5);
        key.position.set(-3.4, 4.6, 4.8);
        key.target.position.set(0.55, 0.05, 0);
        key.castShadow = true;
        key.shadow.mapSize.set(2048, 2048);
        key.shadow.bias = -0.00015;
        key.shadow.normalBias = 0.018;
        scene.add(key, key.target);
        scene.add(new THREE.HemisphereLight(0xf6f9fc, 0x8392a5, 0.22));

        const reducedMotion = global.matchMedia('(prefers-reduced-motion: reduce)');
        let sweepFrame = 0;
        let interactionFrame = 0;
        let sweepStartedAt = 0;
        let disposed = false;
        const targetRotation = { x: coupon.rotation.x, y: coupon.rotation.y, z: coupon.rotation.z };

        function render() {
            if (!disposed) {
                renderer.render(scene, camera);
            }
        }

        function resize() {
            const bounds = stage.getBoundingClientRect();
            const width = Math.max(1, Math.round(bounds.width));
            const height = Math.max(1, Math.round(bounds.height));
            renderer.setSize(width, height, false);
            camera.aspect = width / height;
            camera.updateProjectionMatrix();
            render();
        }

        function restingPose() {
            coupon.rotation.set(-0.2, -0.16, -0.035);
            targetRotation.x = coupon.rotation.x;
            targetRotation.y = coupon.rotation.y;
            targetRotation.z = coupon.rotation.z;
            render();
        }

        function cancelSweep() {
            if (sweepFrame) {
                global.cancelAnimationFrame(sweepFrame);
                sweepFrame = 0;
            }
        }

        function animateInteraction() {
            coupon.rotation.x += (targetRotation.x - coupon.rotation.x) * 0.22;
            coupon.rotation.y += (targetRotation.y - coupon.rotation.y) * 0.22;
            coupon.rotation.z += (targetRotation.z - coupon.rotation.z) * 0.22;
            render();

            const remaining = Math.abs(targetRotation.x - coupon.rotation.x)
                + Math.abs(targetRotation.y - coupon.rotation.y)
                + Math.abs(targetRotation.z - coupon.rotation.z);
            if (remaining > 0.001 && !disposed) {
                interactionFrame = global.requestAnimationFrame(animateInteraction);
            } else {
                interactionFrame = 0;
            }
        }

        function sweep(now) {
            const progress = Math.min(1, (now - sweepStartedAt) / 2200);
            const eased = 1 - Math.pow(1 - progress, 4);
            const arc = Math.sin(eased * Math.PI);
            coupon.rotation.x = -0.2 + (arc * 0.035);
            coupon.rotation.y = -0.16 + (arc * 0.34);
            coupon.rotation.z = -0.035 + (arc * 0.018);
            render();

            if (progress < 1 && !disposed) {
                sweepFrame = global.requestAnimationFrame(sweep);
            } else {
                sweepFrame = 0;
                restingPose();
            }
        }

        function startSweep() {
            if (reducedMotion.matches) {
                restingPose();
                return;
            }

            if (interactionFrame) {
                global.cancelAnimationFrame(interactionFrame);
                interactionFrame = 0;
            }
            cancelSweep();
            sweepStartedAt = performance.now();
            sweepFrame = global.requestAnimationFrame(sweep);
        }

        function update(color, sheen) {
            const settings = materialSettings[sheen] || materialSettings.satin;
            material.color.set(color).convertSRGBToLinear();
            material.roughness = settings.roughness;
            material.clearcoat = settings.clearcoat;
            material.clearcoatRoughness = settings.clearcoatRoughness;
            material.envMapIntensity = settings.envMapIntensity;
            material.bumpScale = settings.bumpScale;
            material.reflectivity = settings.reflectivity;
            material.needsUpdate = true;
            startSweep();
        }

        function handlePointer(event) {
            if (reducedMotion.matches) {
                return;
            }
            cancelSweep();
            const bounds = stage.getBoundingClientRect();
            const x = ((event.clientX - bounds.left) / bounds.width) - 0.5;
            const y = ((event.clientY - bounds.top) / bounds.height) - 0.5;
            targetRotation.x = -0.2 - (y * 0.42);
            targetRotation.y = -0.16 + (x * 0.52);
            targetRotation.z = -0.035 + (x * 0.035);
            if (!interactionFrame) {
                interactionFrame = global.requestAnimationFrame(animateInteraction);
            }
        }

        function showFallback() {
            stage.classList.remove('is-pbr-ready');
            canvas.hidden = true;
        }

        const resizeObserver = global.ResizeObserver ? new global.ResizeObserver(resize) : null;
        resizeObserver?.observe(stage);
        stage.addEventListener('pointerenter', cancelSweep);
        stage.addEventListener('pointermove', handlePointer);
        stage.addEventListener('pointerleave', startSweep);
        canvas.addEventListener('webglcontextlost', (event) => {
            event.preventDefault();
            showFallback();
        });
        stage.classList.add('is-pbr-ready');
        resize();
        startSweep();

        return {
            update,
            dispose() {
                disposed = true;
                if (sweepFrame) {
                    global.cancelAnimationFrame(sweepFrame);
                }
                if (interactionFrame) {
                    global.cancelAnimationFrame(interactionFrame);
                }
                resizeObserver?.disconnect();
                stage.removeEventListener('pointerenter', cancelSweep);
                stage.removeEventListener('pointermove', handlePointer);
                stage.removeEventListener('pointerleave', startSweep);
                geometry.dispose();
                material.dispose();
                ground.geometry.dispose();
                ground.material.dispose();
                surfaceTexture?.dispose();
                environmentTarget?.dispose();
                pmremGenerator?.dispose();
                environmentResources.forEach((resource) => resource.dispose());
                renderer.dispose();
            },
        };
    }

    global.MalievFinishColorMatcherPreview = { create };
}(window));
