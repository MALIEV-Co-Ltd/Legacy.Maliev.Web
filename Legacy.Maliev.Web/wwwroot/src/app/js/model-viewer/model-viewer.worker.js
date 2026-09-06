// Instant Quotation model-parsing worker.
// Decodes STL / OBJ / STEP / IGES off the main thread and runs the shared geometry
// analysis (bounding box, volume, surface area, wall-thickness estimate, manifold/body-count
// checks) for every format, including GLB/GLTF meshes the main thread decoded itself (GLTFLoader
// stays on the main thread because it can depend on Image/document for embedded textures, which
// don't exist here). One job runs at a time -- the main thread's ModelWorkerManager (in
// model-viewer.js) queues, times out, and cancels jobs. This file always replies via postMessage
// with {jobId, success, ...} rather than letting anything throw uncaught, so one bad file only
// fails its own job and this worker instance stays usable for the next one.

importScripts('/src/vendor/three/three.js');
importScripts('/src/vendor/three/STLLoader.js');
importScripts('/src/vendor/three/OBJLoader.js');

// A same-origin worker's deployment version is carried in its own URL query. Keep that query
// on the worker-only CNC module too so an edge/browser cache cannot combine parent and child
// code from different deployments.
function ImportCncGeometryModule() {
    var query = self.location && typeof self.location.search === 'string' ? self.location.search : '';
    importScripts('/src/app/js/cnc-quotation/cnc-plan-contracts.js' + query);
    importScripts('/src/app/js/cnc-quotation/cnc-quotation-config.js' + query);
    importScripts('/src/app/js/cnc-quotation/cnc-material-catalog.js' + query);
    importScripts('/src/app/js/cnc-quotation/cnc-tool-library.js' + query);
    importScripts('/src/app/js/cnc-quotation/cnc-spatial-field.worker.js' + query);
    importScripts('/src/app/js/cnc-quotation/cnc-geometry.worker.js' + query);
    importScripts('/src/app/js/cnc-quotation/cnc-cad-surfaces.worker.js' + query);
    importScripts('/src/app/js/cnc-quotation/cnc-topology.worker.js' + query);
    importScripts('/src/app/js/cnc-quotation/cnc-feature-graph.worker.js' + query);
    importScripts('/src/app/js/cnc-quotation/cnc-ball-rest.worker.js' + query);
}

ImportCncGeometryModule();
// ---------------------------------------------------------------------------
// On-demand OpenCascade loader for STEP / IGES (relocated from model-viewer.js;
// importScripts is synchronous and works directly in a Worker, replacing the main-thread
// version's dynamic <script>-tag injection, which needs `document`).
// ---------------------------------------------------------------------------

var occtPromise = null;

function EnsureOcct() {
    if (occtPromise) {
        return occtPromise;
    }

    occtPromise = new Promise(function (resolve, reject) {
        try {
            if (typeof occtimportjs === 'undefined') {
                importScripts('/lib/occt/occt-import-js.js');
            }
            occtimportjs({ locateFile: function (name) { return '/lib/occt/' + name; } }).then(resolve).catch(reject);
        } catch (e) {
            reject(new Error('Unable to load STEP/IGES support.'));
        }
    });

    return occtPromise;
}

// ---------------------------------------------------------------------------
// Preview material helpers (relocated verbatim from model-viewer.js; the worker needs its own
// copy since it has no access to the main thread's module scope).
// ---------------------------------------------------------------------------

// CAD-style, non-metallic materials retain detail against the light canvas.
function CreatePreviewMaterial(color) {
    return new THREE.MeshStandardMaterial({ color: color, metalness: 0.04, roughness: 0.64, flatShading: false, side: THREE.DoubleSide });
}

function DefaultMaterial() {
    return CreatePreviewMaterial(0xb9c3ca);
}

// Builds a THREE.Group from an OCCT read result. Keep the importer-provided normals and
// B-rep face triangle ranges: the main-thread preview uses those ranges to draw trimmed
// CAD-face boundaries without inferring topology from tessellation angles.
function OcctResultToGroup(result) {
    var group = new THREE.Group();
    if (!result || !result.meshes) {
        return group;
    }

    var material = DefaultMaterial();
    for (var i = 0; i < result.meshes.length; i++) {
        var m = result.meshes[i];
        if (!m.attributes || !m.attributes.position) {
            continue;
        }

        var geometry = new THREE.BufferGeometry();
        geometry.setAttribute('position', new THREE.Float32BufferAttribute(m.attributes.position.array, 3));
        if (m.attributes.normal && m.attributes.normal.array) {
            geometry.setAttribute('normal', new THREE.Float32BufferAttribute(m.attributes.normal.array, 3));
        }
        if (m.index) {
            geometry.setIndex(new THREE.Uint32BufferAttribute(m.index.array, 1));
        }
        if (!geometry.attributes.normal) {
            geometry.computeVertexNormals();
        }
        geometry.userData.cadFaceRanges = Array.isArray(m.brep_faces)
            ? m.brep_faces.map(function (face) {
                return { first: Number(face.first), last: Number(face.last) };
            }).filter(function (face) {
                return Number.isSafeInteger(face.first) && Number.isSafeInteger(face.last)
                    && face.first >= 0 && face.last >= face.first;
            })
            : [];
        group.add(new THREE.Mesh(geometry, material));
    }

    return group;
}

// ---------------------------------------------------------------------------
// Geometry analysis: bounding box, volume, surface area, facets, DFM checks.
// It operates on plain THREE.Object3D/BufferGeometry, which work identically inside a Worker
// (three.js core has no DOM dependency for anything these functions touch).
// ---------------------------------------------------------------------------

var AREA_PROFILE_SAMPLES = 64;
var THICKNESS_EDGE_TRIM_FRACTION = 0.05;
var MIN_REASONABLE_DIMENSION_MM = 3;
var MAX_BUILD_DIMENSION_MM = 350;

// Collects all triangle vertices of an object3D in world space into a flat array.
function ExtractTriangles(object3D) {
    var out = [];
    object3D.updateWorldMatrix(true, true);
    var v = new THREE.Vector3();
    object3D.traverse(function (child) {
        if (!child.isMesh || !child.geometry || !child.geometry.attributes || !child.geometry.attributes.position) {
            return;
        }
        var pos = child.geometry.attributes.position;
        var index = child.geometry.index;
        var mat = child.matrixWorld;
        var count = index ? index.count : pos.count;
        for (var i = 0; i < count; i++) {
            var vi = index ? index.getX(i) : i;
            v.fromBufferAttribute(pos, vi).applyMatrix4(mat);
            out.push(v.x, v.y, v.z);
        }
    });
    return out;
}

// Keeps the full CNC evidence pass at the worker boundary: decoded CAD never crosses to the
// main thread merely to cluster surfaces or evaluate rotational symmetry.
async function AnalyzeCncObject(object3D, modelInfo) {
    var ranges = [], triangleOffset = 0, aligned = true;
    object3D.traverse(function (child) {
        if (!child.isMesh || !child.geometry || !child.geometry.attributes || !child.geometry.attributes.position) { return; }
        var count = child.geometry.index ? child.geometry.index.count : child.geometry.attributes.position.count;
        if (count % 3 !== 0) { aligned = false; }
        CopyAnalysisCadFaceRanges(child.geometry.userData.cadFaceRanges, Math.floor(count / 3)).forEach(function (face) {
            ranges.push({ first: triangleOffset + face.first, last: triangleOffset + face.last });
        });
        triangleOffset += Math.floor(count / 3);
    });
    // Hints and face provenance stay on the analysis boundary, not on the UI's
    // modelInfo response. Hints remain local/untransformed: the geometry worker
    // must strictly match them to complete imported B-rep faces in world space.
    var analysisInfo = Object.assign({}, modelInfo, {
        analyticSurfaces: Array.isArray(object3D.userData.analyticSurfaces) ? object3D.userData.analyticSurfaces : [],
        cadFaceRanges: aligned ? ranges : [],
        occtMeshes: Array.isArray(object3D.userData.occtMeshes) ? object3D.userData.occtMeshes : [],
        validationMeshes: Array.isArray(object3D.userData.canonicalValidationMeshes) ? object3D.userData.canonicalValidationMeshes : [],
        validationDeflectionMm: Number(object3D.userData.validationDeflectionMm),
        sourceFormat: object3D.userData.sourceFormat || 'mesh'
    });
    var topology = await self.CncTopology.build({
        meshes: analysisInfo.occtMeshes,
        validationMeshes: analysisInfo.validationMeshes,
        validationDeflectionMm: analysisInfo.validationDeflectionMm,
        analyticSurfaces: analysisInfo.analyticSurfaces,
        bodyCount: modelInfo.bodyCount,
        sourceFormat: analysisInfo.sourceFormat
    });
    var geometry = AnalyzeCncGeometry(ExtractTriangles(object3D), analysisInfo);
    geometry.cadTopology = topology;
    geometry.geometryRevision = topology.revision;
    geometry.manufacturingFeatureGraph = self.CncFeatureGraph.build(topology, {
        modelScaleMm: Math.max.apply(Math, valuesOrEmpty(geometry.orientedSizeMm))
    });
    AssertManufacturingFeatureGraph(geometry.manufacturingFeatureGraph);
    return geometry;
}

function AssertManufacturingFeatureGraph(graph) {
    if (!graph || graph.contract !== 'ManufacturingFeatureGraph.v1') {
        throw new Error('invalid_manufacturing_feature_graph');
    }
    var serialized = JSON.stringify(graph);
    var prohibited = ['clusterIds', 'sampleIds', 'operationCodes', 'curvedFinishingByDirection'];
    if (prohibited.some(function (field) { return serialized.indexOf(field) >= 0; })) {
        throw new Error('legacy_cluster_evidence_in_feature_graph');
    }
    if ((graph.features || []).some(function (feature) {
        return !Array.isArray(feature.primaryFaceIds) || feature.primaryFaceIds.some(function (id) {
            return typeof id !== 'string' || !id.length;
        });
    })) {
        throw new Error('feature_graph_missing_face_ids');
    }
}

function valuesOrEmpty(value) {
    if (Array.isArray(value)) { return value; }
    if (value && typeof value === 'object') {
        return Object.keys(value).map(function (key) { return Number(value[key]) || 0; });
    }
    return [];
}

// Computes the cross-sectional area (mm^2) and perimeter (mm) at a set of sample
// heights using the oriented plane-cut method over a closed mesh: each triangle
// crossing a sample plane contributes an oriented segment; the shoelace sum of those
// segments gives the enclosed area (no loop ordering needed) and the sum of segment
// lengths gives the contour perimeter (which drives wall/perimeter print time, and a
// local-thickness estimate via 2*area/perimeter). Returns { area: [...], perimeter: [...] }.
function ComputeAreaProfile(tris, minZ, maxZ, samples) {
    var height = maxZ - minZ;
    if (height <= 0 || tris.length < 9) {
        return null;
    }

    var sums = new Float64Array(samples);
    var perims = new Float64Array(samples);
    var triangleCount = tris.length / 9;

    for (var t = 0; t < triangleCount; t++) {
        var o = t * 9;
        var ax = tris[o], ay = tris[o + 1], az = tris[o + 2];
        var bx = tris[o + 3], by = tris[o + 4], bz = tris[o + 5];
        var cx = tris[o + 6], cy = tris[o + 7], cz = tris[o + 8];

        var triMin = Math.min(az, bz, cz);
        var triMax = Math.max(az, bz, cz);
        if (triMax <= triMin) {
            continue; // horizontal triangle, no cross-section contribution
        }

        // In-plane outward normal (x, y components of the triangle normal).
        var nx = (by - ay) * (cz - az) - (bz - az) * (cy - ay);
        var ny = (bz - az) * (cx - ax) - (bx - ax) * (cz - az);
        var nLen = Math.sqrt(nx * nx + ny * ny);
        if (nLen < 1e-9) {
            continue;
        }
        nx /= nLen;
        ny /= nLen;
        // Desired segment direction: outward normal 90deg clockwise from travel.
        var dirX = -ny;
        var dirY = nx;

        var first = Math.max(0, Math.ceil(((triMin - minZ) / height) * (samples - 1)));
        var last = Math.min(samples - 1, Math.floor(((triMax - minZ) / height) * (samples - 1)));

        for (var s = first; s <= last; s++) {
            var z = minZ + (s / (samples - 1)) * height;
            var seg = TrianglePlaneSegment(ax, ay, az, bx, by, bz, cx, cy, cz, z);
            if (!seg) {
                continue;
            }
            // Orient the segment so it runs along dir.
            var ex = seg[2] - seg[0];
            var ey = seg[3] - seg[1];
            var p0x = seg[0], p0y = seg[1], p1x = seg[2], p1y = seg[3];
            if ((ex * dirX + ey * dirY) < 0) {
                p0x = seg[2]; p0y = seg[3]; p1x = seg[0]; p1y = seg[1];
            }
            sums[s] += p0x * p1y - p1x * p0y;
            perims[s] += Math.sqrt(ex * ex + ey * ey);
        }
    }

    var area = new Array(samples);
    var perimeter = new Array(samples);
    for (var i = 0; i < samples; i++) {
        area[i] = Math.abs(sums[i]) * 0.5;
        perimeter[i] = perims[i];
    }
    return { area: area, perimeter: perimeter };
}

// The first and last horizontal slices intersect the mesh's caps. On sloped or rounded
// caps their cross-sectional area collapses towards zero, which is cap geometry rather
// than a printable wall measurement. Ignore a narrow boundary band while retaining every
// interior slice, including genuinely thin local sections.
function EstimateMinimumThickness(profile, dx, dy, dz) {
    var minThickness = null;
    var boundingThickness = Math.min(dx || Infinity, dy || Infinity, dz || Infinity);
    if (!isFinite(boundingThickness)) { boundingThickness = 0; }
    if (profile && profile.area.length === profile.perimeter.length) {
        var sampleCount = profile.area.length;
        var trimCount = Math.max(1, Math.ceil(sampleCount * THICKNESS_EDGE_TRIM_FRACTION));
        var firstSample = trimCount;
        var lastSample = sampleCount - trimCount;

        // A malformed or unusually small profile is still safer to evaluate in full than
        // to discard. Production profiles currently contain either 24 or 64 samples.
        if (firstSample >= lastSample) {
            firstSample = 0;
            lastSample = sampleCount;
        }

        for (var s = firstSample; s < lastSample; s++) {
            if (profile.perimeter[s] > 1e-6 && profile.area[s] > 0) {
                var thickness = (2 * profile.area[s]) / profile.perimeter[s];
                if (minThickness === null || thickness < minThickness) {
                    minThickness = thickness;
                }
            }
        }
    }

    if (minThickness === null) {
        minThickness = boundingThickness;
    } else if (boundingThickness > 0) {
        // A cross-sectional area/perimeter proxy can measure an in-plane width for a
        // broad, flat part. Minimum wall thickness cannot exceed the model's smallest
        // overall extent, so retain that extent as a conservative upper bound.
        minThickness = Math.min(minThickness, boundingThickness);
    }

    return minThickness;
}

// Returns [x0,y0,x1,y1] where the plane z=level cuts the triangle, or null.
function TrianglePlaneSegment(ax, ay, az, bx, by, bz, cx, cy, cz, level) {
    var pts = [];
    AddPlaneEdgeCrossing(pts, ax, ay, az, bx, by, bz, level);
    AddPlaneEdgeCrossing(pts, bx, by, bz, cx, cy, cz, level);
    AddPlaneEdgeCrossing(pts, cx, cy, cz, ax, ay, az, level);
    if (pts.length < 4) {
        return null;
    }
    return [pts[0], pts[1], pts[2], pts[3]];
}

function AddPlaneEdgeCrossing(pts, x0, y0, z0, x1, y1, z1, level) {
    var d0 = z0 - level;
    var d1 = z1 - level;
    if ((d0 > 0 && d1 > 0) || (d0 < 0 && d1 < 0)) {
        return;
    }
    if (d0 === d1) {
        return;
    }
    var t = d0 / (d0 - d1);
    if (t < 0 || t > 1) {
        return;
    }
    pts.push(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t);
}

// Estimates mesh quality from a triangle soup: whether it has open boundary edges
// (non-watertight), whether any edge is shared by more than two triangles (a true
// non-manifold edge), and how many separate connected bodies it contains. Vertices
// are quantized to a grid so that the independent per-triangle vertices of an STL
// soup merge. Skipped for very large meshes to keep the UI responsive.
function AnalyzeMeshQuality(tris, diag) {
    var triCount = tris.length / 9;
    if (triCount === 0) {
        return { nonWatertight: false, nonManifold: false, bodyCount: 0, checked: false };
    }
    if (triCount > 200000) {
        return { nonWatertight: false, nonManifold: false, bodyCount: 1, checked: false };
    }

    var grid = Math.max((diag || 0) * 1e-5, 1e-4);
    var parent = new Map();
    var edges = new Map();

    function vkey(x, y, z) {
        return Math.round(x / grid) + '_' + Math.round(y / grid) + '_' + Math.round(z / grid);
    }
    function find(a) {
        while (parent.get(a) !== a) {
            parent.set(a, parent.get(parent.get(a)));
            a = parent.get(a);
        }
        return a;
    }
    function addNode(k) { if (!parent.has(k)) { parent.set(k, k); } }
    function union(a, b) { var ra = find(a), rb = find(b); if (ra !== rb) { parent.set(ra, rb); } }
    function addEdge(a, b) {
        if (a === b) { return; }
        var key = a < b ? (a + '|' + b) : (b + '|' + a);
        edges.set(key, (edges.get(key) || 0) + 1);
    }

    for (var t = 0; t < triCount; t++) {
        var o = t * 9;
        var k0 = vkey(tris[o], tris[o + 1], tris[o + 2]);
        var k1 = vkey(tris[o + 3], tris[o + 4], tris[o + 5]);
        var k2 = vkey(tris[o + 6], tris[o + 7], tris[o + 8]);
        // STEP tessellation can contain collapsed triangles at face seams. They carry no
        // surface topology, and counting their repeated edge twice turns an otherwise closed
        // solid into a false non-manifold result (edge use 4 instead of 2).
        if (k0 === k1 || k1 === k2 || k2 === k0) { continue; }
        addNode(k0); addNode(k1); addNode(k2);
        union(k0, k1); union(k1, k2);
        addEdge(k0, k1); addEdge(k1, k2); addEdge(k2, k0);
    }

    var nonWatertight = false;
    var nonManifold = false;
    edges.forEach(function (count) {
        if (count === 1) { nonWatertight = true; }
        else if (count > 2) { nonManifold = true; }
    });

    var roots = new Set();
    parent.forEach(function (_value, key) { roots.add(find(key)); });

    return { nonWatertight: nonWatertight, nonManifold: nonManifold, bodyCount: roots.size, checked: true };
}

function AnalyzeObject(object3D) {
    var tris = ExtractTriangles(object3D);
    var facets = tris.length / 9;
    var minX = Infinity, minY = Infinity, minZ = Infinity;
    var maxX = -Infinity, maxY = -Infinity, maxZ = -Infinity;
    var volume = 0;
    var surfaceArea = 0;

    for (var i = 0; i < tris.length; i += 9) {
        var ax = tris[i], ay = tris[i + 1], az = tris[i + 2];
        var bx = tris[i + 3], by = tris[i + 4], bz = tris[i + 5];
        var cx = tris[i + 6], cy = tris[i + 7], cz = tris[i + 8];

        if (ax < minX) minX = ax; if (bx < minX) minX = bx; if (cx < minX) minX = cx;
        if (ay < minY) minY = ay; if (by < minY) minY = by; if (cy < minY) minY = cy;
        if (az < minZ) minZ = az; if (bz < minZ) minZ = bz; if (cz < minZ) minZ = cz;
        if (ax > maxX) maxX = ax; if (bx > maxX) maxX = bx; if (cx > maxX) maxX = cx;
        if (ay > maxY) maxY = ay; if (by > maxY) maxY = by; if (cy > maxY) maxY = cy;
        if (az > maxZ) maxZ = az; if (bz > maxZ) maxZ = bz; if (cz > maxZ) maxZ = cz;

        // Signed volume of the tetrahedron (origin, a, b, c).
        volume += (ax * (by * cz - bz * cy) - ay * (bx * cz - bz * cx) + az * (bx * cy - by * cx)) / 6.0;

        // Triangle surface area via the cross product magnitude.
        var ux = bx - ax, uy = by - ay, uz = bz - az;
        var wx = cx - ax, wy = cy - ay, wz = cz - az;
        var crossX = uy * wz - uz * wy, crossY = uz * wx - ux * wz, crossZ = ux * wy - uy * wx;
        surfaceArea += Math.sqrt(crossX * crossX + crossY * crossY + crossZ * crossZ) * 0.5;
    }

    if (!isFinite(minX)) {
        return {
            min: { x: 0, y: 0, z: 0 }, max: { x: 0, y: 0, z: 0 }, size: { x: 0, y: 0, z: 0 }, volume: 0, facets: 0,
            areaProfile: null, perimeterProfile: null, surfaceAreaMm2: 0, minThicknessMm: 0,
            nonWatertight: false, nonManifold: false, bodyCount: 0, oddlySmall: false, oddlyLarge: false
        };
    }

    var dx = maxX - minX, dy = maxY - minY, dz = maxZ - minZ;
    var diagonal = Math.sqrt(dx * dx + dy * dy + dz * dz);
    var samples = facets > 250000 ? 24 : AREA_PROFILE_SAMPLES;
    var profile = ComputeAreaProfile(tris, minZ, maxZ, samples);
    var quality = AnalyzeMeshQuality(tris, diagonal);

    // Approximate local wall thickness as 2*area/perimeter (exact for a uniform annulus)
    // at sampled interior layers. This remains a lightweight advisory proxy rather than
    // an authoritative distance-transform thickness analysis.
    var minThickness = EstimateMinimumThickness(profile, dx, dy, dz);

    var maxDimension = Math.max(dx, dy, dz);

    return {
        min: { x: minX, y: minY, z: minZ },
        max: { x: maxX, y: maxY, z: maxZ },
        size: { x: dx, y: dy, z: dz },
        volume: Math.abs(volume),
        facets: facets,
        areaProfile: profile ? profile.area : null,
        perimeterProfile: profile ? profile.perimeter : null,
        surfaceAreaMm2: surfaceArea,
        minThicknessMm: minThickness,
        nonWatertight: quality.nonWatertight,
        nonManifold: quality.nonManifold,
        bodyCount: quality.bodyCount,
        // "Unusually small" describes the whole part, not one thin axis. Using the
        // largest extent also makes the small and large size classifications exclusive.
        oddlySmall: maxDimension > 0 && maxDimension < MIN_REASONABLE_DIMENSION_MM,
        oddlyLarge: maxDimension > MAX_BUILD_DIMENSION_MM
    };
}

// The progressive CNC response only needs enough information to frame and identify the body.
// Volume, quality, accessibility, and manufacturing evidence belong to the analysis lane.
function AnalyzePreviewObject(object3D) {
    object3D.updateWorldMatrix(true, true);
    var bounds = new THREE.Box3().setFromObject(object3D);
    var size = bounds.getSize(new THREE.Vector3());
    var bodyCount = 0;
    var facets = 0;
    object3D.traverse(function (child) {
        if (!child.isMesh || !child.geometry || !child.geometry.attributes || !child.geometry.attributes.position) { return; }
        bodyCount++;
        facets += child.geometry.index
            ? child.geometry.index.count / 3
            : child.geometry.attributes.position.count / 3;
    });
    if (bounds.isEmpty()) {
        bounds.min.set(0, 0, 0);
        bounds.max.set(0, 0, 0);
        size.set(0, 0, 0);
    }
    return {
        min: { x: bounds.min.x, y: bounds.min.y, z: bounds.min.z },
        max: { x: bounds.max.x, y: bounds.max.y, z: bounds.max.z },
        size: { x: size.x, y: size.y, z: size.z },
        volume: 0,
        facets: facets,
        areaProfile: null,
        perimeterProfile: null,
        surfaceAreaMm2: 0,
        minThicknessMm: 0,
        nonWatertight: false,
        nonManifold: false,
        bodyCount: bodyCount,
        oddlySmall: false,
        oddlyLarge: false,
        previewOnly: true
    };
}

// ---------------------------------------------------------------------------
// Message-boundary bridges
// ---------------------------------------------------------------------------

// Walks a decoded object3D and returns each mesh's own local-space position/index typed
// arrays, ready to transfer back to the main thread for display.
function ExtractMeshBuffers(object3D) {
    var meshes = [];
    object3D.traverse(function (child) {
        if (!child.isMesh || !child.geometry || !child.geometry.attributes || !child.geometry.attributes.position) {
            return;
        }
        var position = child.geometry.attributes.position.array;
        var normal = child.geometry.attributes.normal ? child.geometry.attributes.normal.array : null;
        var index = child.geometry.index ? child.geometry.index.array : null;
        meshes.push({
            position: position instanceof Float32Array ? position : new Float32Array(position),
            normal: normal ? (normal instanceof Float32Array ? normal : new Float32Array(normal)) : null,
            index: index ? (index instanceof Uint32Array ? index : new Uint32Array(index)) : null,
            cadFaceRanges: Array.isArray(child.geometry.userData.cadFaceRanges)
                ? child.geometry.userData.cadFaceRanges.map(function (face) {
                    return { first: face.first, last: face.last };
                })
                : []
        });
    });
    return meshes;
}

function CopyAnalysisCadFaceRanges(ranges, triangleCount) {
    if (!Array.isArray(ranges)) { return []; }
    var copied = ranges.filter(function (face) {
        return face && Number.isSafeInteger(face.first) && Number.isSafeInteger(face.last)
            && face.first >= 0 && face.last >= face.first && face.last < triangleCount;
    }).map(function (face) { return { first: face.first, last: face.last }; });
    copied.sort(function (left, right) { return left.first - right.first; });
    for (var index = 1; index < copied.length; index += 1) {
        if (copied[index].first <= copied[index - 1].last) { return []; }
    }
    return copied;
}

// Produces a second transferable snapshot inside the parse worker. Sending this snapshot to the
// independent analysis worker avoids cloning every vertex on the browser's UI thread.
function ExtractAnalysisMeshBuffers(object3D) {
    object3D.updateWorldMatrix(true, true);
    var meshes = [];
    object3D.traverse(function (child) {
        if (!child.isMesh || !child.geometry || !child.geometry.attributes || !child.geometry.attributes.position) { return; }
        var sourcePosition = child.geometry.attributes.position.array;
        var sourceIndex = child.geometry.index ? child.geometry.index.array : null;
        meshes.push({
            position: new Float32Array(sourcePosition),
            index: sourceIndex ? new Uint32Array(sourceIndex) : null,
            matrix: new Float32Array(child.matrixWorld.elements),
            cadFaceRanges: CopyAnalysisCadFaceRanges(child.geometry.userData.cadFaceRanges,
                Math.floor((sourceIndex ? sourceIndex.length : sourcePosition.length / 3) / 3))
        });
    });
    // The existing main-thread bridge forwards these records unchanged. Attach
    // root metadata once, never duplicate the potentially large hint array per mesh.
    if (meshes.length > 0 && Array.isArray(object3D.userData.analyticSurfaces)) {
        meshes[0].analyticSurfaces = object3D.userData.analyticSurfaces;
    }
    if (meshes.length > 0 && Array.isArray(object3D.userData.canonicalValidationMeshes)) {
        meshes[0].canonicalValidationMeshes = object3D.userData.canonicalValidationMeshes.map(function (mesh) {
            var sourcePosition = mesh && mesh.attributes && mesh.attributes.position && mesh.attributes.position.array;
            var sourceIndex = mesh && mesh.index && mesh.index.array;
            return {
                position: new Float32Array(sourcePosition || []),
                index: sourceIndex ? new Uint32Array(sourceIndex) : null,
                brep_faces: Array.isArray(mesh && mesh.brep_faces) ? mesh.brep_faces.map(function (face) {
                    return { first: face.first, last: face.last };
                }) : []
            };
        });
        meshes[0].validationDeflectionMm = Number(object3D.userData.validationDeflectionMm);
    }
    if (meshes.length > 0) { meshes[0].sourceFormat = object3D.userData.sourceFormat || 'mesh'; }
    return meshes;
}

// Reconstructs a throwaway THREE.Group from transferred mesh buffers, purely so the unmodified
// AnalyzeObject can run on it (used only for the GLB/GLTF 'analyze' job, where the main thread
// already decoded the file and only needs the analysis numbers back -- this group is discarded
// after analysis; the main thread keeps and displays its own GLTFLoader-built scene).
function BuildObject3DFromMeshBuffers(meshes) {
    var group = new THREE.Group();
    if (meshes.length > 0 && Array.isArray(meshes[0].analyticSurfaces)) {
        group.userData.analyticSurfaces = meshes[0].analyticSurfaces;
    }
    if (meshes.length > 0) {
        group.userData.occtMeshes = meshes;
        group.userData.sourceFormat = meshes[0].sourceFormat || 'mesh';
        group.userData.canonicalValidationMeshes = Array.isArray(meshes[0].canonicalValidationMeshes)
            ? meshes[0].canonicalValidationMeshes : [];
        group.userData.validationDeflectionMm = Number(meshes[0].validationDeflectionMm);
    }
    meshes.forEach(function (m) {
        var geometry = new THREE.BufferGeometry();
        geometry.setAttribute('position', new THREE.Float32BufferAttribute(m.position, 3));
        if (m.index) {
            geometry.setIndex(new THREE.Uint32BufferAttribute(m.index, 1));
        }
        geometry.userData.cadFaceRanges = CopyAnalysisCadFaceRanges(m.cadFaceRanges,
            Math.floor((m.index ? m.index.length : m.position.length / 3) / 3));
        var mesh = new THREE.Mesh(geometry);
        // Apply the transferred world matrix directly rather than decompose() into
        // position/quaternion/scale and let updateWorldMatrix() recompose it -- decompose cannot
        // represent a sheared matrix (rotation composed with non-uniform parent scale), which
        // would silently produce a wrong bounding box/volume for such a GLB. matrixAutoUpdate =
        // false tells updateWorldMatrix() (called by the analysis code below) to use this matrix
        // as-is instead of recomputing it from position/quaternion/scale.
        mesh.matrix.fromArray(m.matrix);
        mesh.matrixAutoUpdate = false;
        group.add(mesh);
    });
    return group;
}

function TransferablesFor(meshes) {
    var transfer = [];
    meshes.forEach(function (m) {
        transfer.push(m.position.buffer);
        if (m.normal) { transfer.push(m.normal.buffer); }
        if (m.index) { transfer.push(m.index.buffer); }
        if (m.matrix) { transfer.push(m.matrix.buffer); }
        (Array.isArray(m.canonicalValidationMeshes) ? m.canonicalValidationMeshes : []).forEach(function (validationMesh) {
            if (validationMesh.position) { transfer.push(validationMesh.position.buffer); }
            if (validationMesh.index) { transfer.push(validationMesh.index.buffer); }
        });
    });
    return transfer;
}

// ---------------------------------------------------------------------------
// Job dispatch
// ---------------------------------------------------------------------------

// Decodes one 'parse' job's file bytes into a THREE.Object3D. STLLoader/OBJLoader
// can all throw SYNCHRONOUSLY on malformed input (matching their main-thread behavior today) --
// callers must invoke this inside the same try/catch that also wraps the async OCCT path, which
// is exactly what self.onmessage below does.
function RunParseJob(data) {
    var extension = data.extension;
    if (extension === 'stl') {
        var geometry = new THREE.STLLoader().parse(data.buffer);
        geometry.computeVertexNormals();
        return Promise.resolve(new THREE.Mesh(geometry, DefaultMaterial()));
    }
    if (extension === 'obj') {
        var text = new TextDecoder().decode(data.buffer);
        return Promise.resolve(new THREE.OBJLoader().parse(text));
    }
    if (extension === 'stp' || extension === 'step' || extension === 'igs' || extension === 'iges') {
        return EnsureOcct().then(function (occt) {
            var bytes = new Uint8Array(data.buffer);
            var result = (extension === 'igs' || extension === 'iges')
                ? occt.ReadIgesFile(bytes, null)
                : occt.ReadStepFile(bytes, null);
            if (!result || !result.success) {
                throw new Error('Unable to tessellate this CAD file.');
            }
            var group = OcctResultToGroup(result);
            group.userData.occtMeshes = result.meshes;
            group.userData.sourceFormat = extension;
            if (data.analysisProfile === 'cnc' && (extension === 'stp' || extension === 'step')) {
                var validationResult = occt.ReadStepFile(bytes, { linearDeflection: 0.1 });
                if (!validationResult || !validationResult.success) {
                    throw new Error('Unable to build canonical CNC validation tessellation.');
                }
                group.userData.canonicalValidationMeshes = validationResult.meshes;
                group.userData.validationDeflectionMm = 0.1;
                group.userData.analyticSurfaces = CncCadSurfaces.parseStep(new TextDecoder().decode(bytes));
            }
            return group;
        });
    }
    return Promise.reject(new Error('Unsupported file type.'));
}

// Classifies trusted CAD triangles for the advisory setup overlay. This deliberately lives in
// the worker because large STEP/IGES tessellations can contain hundreds of thousands of display
// triangles; transforming and partitioning those triangles on the browser thread would stall
// camera controls and part selection.
function ClassifyCncOverlay(data) {
    var meshes = Array.isArray(data.meshes) ? data.meshes : [];
    var clusters = Array.isArray(data.clusters) ? data.clusters : [];
    var selectedSetups = Array.isArray(data.selectedSetups) ? data.selectedSetups : [];
    var reachableIds = new Set(Array.isArray(data.reachableIds) ? data.reachableIds.map(String) : []);
    var residualIds = new Set(Array.isArray(data.residualIds) ? data.residualIds.map(String) : []);
    var fluteReach = new Set(Array.isArray(data.fluteReachKeys) ? data.fluteReachKeys : []);
    var triangleTotal = meshes.reduce(function (sum, mesh) {
        var count = mesh.index ? mesh.index.length : mesh.position.length / 3;
        return sum + Math.floor(count / 3);
    }, 0);
    var clusterIndexByTriangle = new Int32Array(triangleTotal);
    clusterIndexByTriangle.fill(-1);
    clusters.forEach(function (cluster, clusterIndex) {
        var indexes = cluster.triangleIndexes || [];
        for (var index = 0; index < indexes.length; index++) {
            var triangleIndex = Number(indexes[index]);
            if (triangleIndex >= 0 && triangleIndex < triangleTotal) {
                clusterIndexByTriangle[triangleIndex] = clusterIndex;
            }
        }
    });
    var directionalIndexes = new Map();
    clusters.forEach(function (cluster) {
        var byDirection = cluster.accessibleTriangleIndexesByDirection;
        if (!byDirection || typeof byDirection !== 'object') { return; }
        Object.keys(byDirection).forEach(function (setupId) {
            if (Array.isArray(byDirection[setupId]) || ArrayBuffer.isView(byDirection[setupId])) {
                directionalIndexes.set(String(setupId) + '\u0000' + String(cluster.id), new Set(byDirection[setupId]));
            }
        });
    });
    var highlighted = [];
    var dimmed = [];
    var residual = [];
    var a = new THREE.Vector3();
    var b = new THREE.Vector3();
    var c = new THREE.Vector3();
    var edgeOne = new THREE.Vector3();
    var edgeTwo = new THREE.Vector3();
    var normal = new THREE.Vector3();
    var globalTriangleIndex = 0;
    meshes.forEach(function (mesh) {
        var position = mesh.position;
        var index = mesh.index;
        var matrix = new THREE.Matrix4().fromArray(mesh.matrix);
        var count = index ? index.length : position.length / 3;
        function readVertex(target, vertexIndex) {
            var offset = vertexIndex * 3;
            return target.set(position[offset], position[offset + 1], position[offset + 2]).applyMatrix4(matrix);
        }
        for (var offset = 0; offset + 2 < count; offset += 3) {
            var ia = index ? index[offset] : offset;
            var ib = index ? index[offset + 1] : offset + 1;
            var ic = index ? index[offset + 2] : offset + 2;
            readVertex(a, ia);
            readVertex(b, ib);
            readVertex(c, ic);
            edgeOne.subVectors(b, a);
            edgeTwo.subVectors(c, a);
            normal.crossVectors(edgeOne, edgeTwo);
            if (normal.lengthSq() > 0) { normal.normalize(); }
            var clusterIndex = clusterIndexByTriangle[globalTriangleIndex];
            var cluster = clusterIndex >= 0 ? clusters[clusterIndex] : null;
            globalTriangleIndex++;
            if (!cluster) { continue; }
            var clusterId = String(cluster.id);
            var selectedSetupCanReach = reachableIds.has(clusterId);
            if (selectedSetupCanReach && cluster.accessibleTriangleIndexesByDirection
                && typeof cluster.accessibleTriangleIndexesByDirection === 'object') {
                selectedSetupCanReach = selectedSetups.some(function (setup) {
                    var setupId = String(setup.id);
                    var exactIndexes = directionalIndexes.get(setupId + '\u0000' + clusterId);
                    if (exactIndexes && exactIndexes.has(globalTriangleIndex - 1)) { return true; }
                    // Partial directional evidence is authoritative within the cluster. The
                    // setup-level flute fallback must not turn an occluded opposite wall green.
                    if (exactIndexes) { return false; }
                    var direction = setup.direction;
                    if (!direction) { return false; }
                    var directionVector = new THREE.Vector3(Number(direction.x) || 0, Number(direction.y) || 0, Number(direction.z) || 0);
                    return directionVector.lengthSq() > 0
                        && Math.abs(normal.dot(directionVector.normalize())) <= 0.35
                        && fluteReach.has(setupId + '\u0000' + clusterId);
                });
            }
            var target = selectedSetupCanReach ? highlighted : dimmed;
            target.push(a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z);
            if (residualIds.has(clusterId) && !selectedSetupCanReach) {
                residual.push(a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z);
            }
        }
    });
    return {
        highlighted: new Float32Array(highlighted),
        dimmed: new Float32Array(dimmed),
        residual: new Float32Array(residual)
    };
}

self.onmessage = function (event) {
    var data = event.data;
    var jobId = data.jobId;

    function fail(error) {
        self.postMessage({ jobId: jobId, success: false, error: (error && error.message) || 'Unable to read this model file.' });
    }

    try {
        if (data.action === 'parse') {
            RunParseJob(data).then(function (object3D) {
                var progressiveCncPreview = event.data.analysisProfile === 'cnc'
                    && event.data.deferCncAnalysis === true;
                // Core measurements are cheap enough to finish in the decode worker and
                // must accompany the first visible CAD frame. Only CNC accessibility and
                // setup evidence are deferred to the independent analysis worker.
                var modelInfo = AnalyzeObject(object3D);
                var meshes = ExtractMeshBuffers(object3D);
                if (event.data.analysisProfile === 'cnc' && event.data.deferCncAnalysis !== true) {
                    AnalyzeCncObject(object3D, modelInfo).then(function (cncGeometry) {
                        self.postMessage({ jobId: jobId, success: true, meshes: meshes, modelInfo: modelInfo, cncGeometry: cncGeometry }, TransferablesFor(meshes));
                    }).catch(fail);
                } else {
                    var analysisMeshes = progressiveCncPreview ? ExtractAnalysisMeshBuffers(object3D) : null;
                    var transfer = TransferablesFor(meshes);
                    if (analysisMeshes) { transfer = transfer.concat(TransferablesFor(analysisMeshes)); }
                    self.postMessage({ jobId: jobId, success: true, meshes: meshes, analysisMeshes: analysisMeshes,
                        modelInfo: modelInfo, cncGeometry: null }, transfer);
                }
            }).catch(fail);
        } else if (data.action === 'analyze') {
            var object3D = BuildObject3DFromMeshBuffers(data.meshes);
            var modelInfo = AnalyzeObject(object3D);
            if (event.data.analysisProfile === 'cnc') {
                AnalyzeCncObject(object3D, modelInfo).then(function (cncGeometry) {
                    self.postMessage({ jobId: jobId, success: true, meshes: null, modelInfo: modelInfo, cncGeometry: cncGeometry });
                }).catch(fail);
            } else {
                self.postMessage({ jobId: jobId, success: true, meshes: null, modelInfo: modelInfo, cncGeometry: null });
            }
        } else if (data.action === 'classify-cnc-overlay') {
            var overlay = ClassifyCncOverlay(data);
            self.postMessage({ jobId: jobId, success: true, overlay: overlay },
                [overlay.highlighted.buffer, overlay.dimmed.buffer, overlay.residual.buffer]);
        } else {
            fail(new Error('Unknown job type.'));
        }
    } catch (error) {
        fail(error);
    }
};
