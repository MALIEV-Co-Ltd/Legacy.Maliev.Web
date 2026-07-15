// Instant Quotation 3D model viewer and part-configuration UI.
// Loads STL / OBJ / 3MF / GLB / GLTF natively and STEP / IGES via an on-demand
// OpenCascade (occt-import-js) WASM tessellator, measures each part (bounding box,
// solid volume, surface area, facet count, an approximate min. wall thickness, and a
// per-layer cross-sectional area/perimeter profile), and drives a single shared 3D
// viewer plus the part-configuration UI (material, colour, quantity, bulk pricing)
// for whichever part is currently active.

// ---------------------------------------------------------------------------
// Material & colour catalogue (values must match the server pricing catalogue)
// ---------------------------------------------------------------------------

var COLOR_SETS = {
    full: { colors: ['Any', 'Black', 'White', 'Gray', 'Silver', 'Red', 'Orange', 'Yellow', 'Green', 'Blue', 'Purple', 'Pink'], custom: true },
    neutral: { colors: ['Any', 'Natural', 'Black', 'White', 'Gray'], custom: true },
    carbon: { colors: ['Black', 'Natural'], custom: false },
    hips: { colors: ['Black', 'White'], custom: false },
    tpu: { colors: ['Any', 'Black', 'White', 'Clear', 'Red', 'Blue'], custom: true },
    support: { colors: ['Natural'], custom: false },
    conductive: { colors: ['Black'], custom: false },
    resinStandard: { colors: ['Gray', 'Black', 'White'], custom: false },
    resinTough: { colors: ['Gray', 'Black'], custom: false },
    resinClear: { colors: ['Clear'], custom: false },
    resinFlexible: { colors: ['Black', 'Translucent'], custom: false },
    resinCastable: { colors: ['Green'], custom: false }
};

// Approximate swatch colour shown for each named colour value (presentation only).
var COLOR_SWATCH_HEX = {
    Any: '#cfd6dc', Black: '#111111', White: '#f5f5f5', Gray: '#8c8c8c', Silver: '#c9ccd1',
    Red: '#d9433a', Orange: '#e8912b', Yellow: '#e8c93a', Green: '#3f9d55', Blue: '#3568d4',
    Purple: '#7a4fc9', Pink: '#e07bb0', Natural: '#e4dcc8', Clear: '#dfeaf2', Translucent: '#dfeaf2',
};

var CUSTOM_COLOR_VALUE = '__custom__';
var DEFAULT_PREVIEW_COLOR = '#b9c3ca';

function ResolvePreviewColor(value) {
    if (!value || value === 'Any') {
        return DEFAULT_PREVIEW_COLOR;
    }
    if (value.charAt && value.charAt(0) === '#') {
        return value;
    }
    return COLOR_SWATCH_HEX[value] || DEFAULT_PREVIEW_COLOR;
}

var MATERIALS = [
    { key: 'PLA', name: 'PLA — Polylactic Acid', group: 'Fused Deposition Modeling', colors: 'full', desc: 'Easy to print, eco-friendly and dimensionally stable. Best for prototypes, display models and low-stress parts.', descTh: 'พิมพ์ง่าย เป็นมิตรต่อสิ่งแวดล้อม และคงรูปได้ดี เหมาะกับต้นแบบ โมเดลสำหรับจัดแสดง และชิ้นงานที่รับแรงไม่มาก' },
    { key: 'PETG', name: 'PETG — PET Glycol-modified', group: 'Fused Deposition Modeling', colors: 'full', desc: 'Tougher and more heat-resistant than PLA with good layer adhesion. A solid all-round choice for functional parts.', descTh: 'เหนียวและทนความร้อนกว่า PLA ยึดเกาะระหว่างชั้นดี เหมาะกับชิ้นงานใช้งานจริงทั่วไป' },
    { key: 'ABS', name: 'ABS — Acrylonitrile Butadiene Styrene', group: 'Fused Deposition Modeling', colors: 'full', desc: 'Heat resistant and impact-tough, but shrinks and warps more during printing. Common for enclosures and fixtures.', descTh: 'ทนความร้อนและแรงกระแทกดี แต่หดตัวและบิดงอระหว่างพิมพ์ได้มากกว่า เหมาะกับกล่องครอบและฟิกซ์เจอร์' },
    { key: 'ASA', name: 'ASA — Acrylonitrile Styrene Acrylate', group: 'Fused Deposition Modeling', colors: 'full', desc: 'Like ABS but UV-stable — holds up outdoors without yellowing or turning brittle.', descTh: 'คุณสมบัติใกล้ ABS แต่ทนรังสียูวี ใช้งานกลางแจ้งได้นานโดยไม่เหลืองหรือกรอบง่าย' },
    { key: 'HIPS', name: 'HIPS — High Impact Polystyrene', group: 'Fused Deposition Modeling', colors: 'hips', desc: 'Lightweight and easy to machine or sand; also used as a dissolvable support material for ABS.', descTh: 'น้ำหนักเบา ขัดแต่งง่าย และใช้เป็นวัสดุรองรับที่ละลายได้สำหรับ ABS' },
    { key: 'TPU', name: 'TPU — Flexible Thermoplastic Polyurethane', group: 'Fused Deposition Modeling', colors: 'tpu', desc: 'Flexible, rubber-like material for gaskets, grips, wearables and parts that need to bend.', descTh: 'ยืดหยุ่นคล้ายยาง เหมาะกับซีล ด้ามจับ อุปกรณ์สวมใส่ และชิ้นงานที่ต้องโค้งงอ' },
    { key: 'PC', name: 'PC — Polycarbonate', group: 'Fused Deposition Modeling', colors: 'neutral', desc: 'High-strength, heat- and impact-resistant engineering plastic for demanding mechanical parts.', descTh: 'แข็งแรง ทนความร้อนและแรงกระแทก เหมาะกับชิ้นส่วนวิศวกรรมที่รับงานหนัก' },
    { key: 'PC-FR', name: 'PC-FR — Flame-Retardant Polycarbonate', group: 'Fused Deposition Modeling', colors: 'neutral', desc: 'Flame-retardant polycarbonate for enclosures and components with fire-safety requirements.', descTh: 'โพลีคาร์บอเนตหน่วงไฟ สำหรับกล่องและชิ้นส่วนที่ต้องการความปลอดภัยด้านอัคคีภัย' },
    { key: 'PA6', name: 'PA6 — Nylon 6', group: 'Fused Deposition Modeling', colors: 'neutral', desc: 'Tough, wear-resistant nylon with good fatigue resistance — ideal for gears, snap-fits and living hinges.', descTh: 'ไนลอนเหนียว ทนสึกหรอ และทนความล้า เหมาะกับเฟือง สแนปฟิต และบานพับยืดหยุ่น' },
    { key: 'PA12', name: 'PA12 — Nylon 12', group: 'Fused Deposition Modeling', colors: 'neutral', desc: 'Low-moisture-absorption nylon with excellent chemical resistance and durability.', descTh: 'ไนลอนดูดความชื้นต่ำ ทนสารเคมี และทนทาน' },
    { key: 'PLA-CF', name: 'PLA-CF — PLA + Carbon Fiber', group: 'Carbon Fiber Reinforced', colors: 'carbon', desc: 'Carbon-fiber-reinforced PLA — stiffer and lighter than standard PLA, with a matte finish.', descTh: 'PLA เสริมคาร์บอนไฟเบอร์ แข็งและเบากว่า PLA ทั่วไป พร้อมผิวด้าน' },
    { key: 'PETG-CF', name: 'PETG-CF — PETG + Carbon Fiber', group: 'Carbon Fiber Reinforced', colors: 'carbon', desc: 'Carbon-fiber-reinforced PETG for extra stiffness in functional, load-bearing parts.', descTh: 'PETG เสริมคาร์บอนไฟเบอร์ เพิ่มความแข็งสำหรับชิ้นงานใช้งานจริงและรับแรง' },
    { key: 'PET-CF', name: 'PET-CF — PET + Carbon Fiber', group: 'Carbon Fiber Reinforced', colors: 'carbon', desc: 'High-stiffness, low-warp carbon-fiber composite suited to jigs, fixtures and tooling.', descTh: 'คอมโพสิตคาร์บอนไฟเบอร์ที่แข็งสูง บิดงอน้อย เหมาะกับจิ๊ก ฟิกซ์เจอร์ และเครื่องมือ' },
    { key: 'PA-CF', name: 'PA-CF — Nylon + Carbon Fiber', group: 'Carbon Fiber Reinforced', colors: 'carbon', desc: 'Carbon-fiber nylon — very high strength-to-weight ratio for demanding mechanical applications.', descTh: 'ไนลอนเสริมคาร์บอนไฟเบอร์ มีอัตราส่วนความแข็งแรงต่อน้ำหนักสูง เหมาะกับงานวิศวกรรมที่รับแรงมาก' },
    { key: 'ASA-CF', name: 'ASA-CF — ASA + Carbon Fiber', group: 'Carbon Fiber Reinforced', colors: 'carbon', desc: 'Carbon-fiber ASA — UV-stable and rigid, suited to outdoor structural parts.', descTh: 'ASA เสริมคาร์บอนไฟเบอร์ แข็งและทนรังสียูวี เหมาะกับชิ้นส่วนโครงสร้างที่ใช้งานกลางแจ้ง' },
    { key: 'PETG-ESD', name: 'PETG-ESD — ESD-Safe PETG', group: 'Specialty', colors: 'carbon', desc: 'Static-dissipative PETG for parts that handle or house sensitive electronics.', descTh: 'PETG สลายประจุไฟฟ้าสถิต เหมาะกับชิ้นงานที่สัมผัสหรือบรรจุอุปกรณ์อิเล็กทรอนิกส์ที่ไวต่อไฟฟ้าสถิต' },
    { key: 'PC-ESD', name: 'PC-ESD — ESD-Safe Polycarbonate', group: 'Specialty', colors: 'conductive', desc: 'Static-dissipative polycarbonate for high-strength ESD-safe enclosures and fixtures.', descTh: 'โพลีคาร์บอเนตสลายประจุไฟฟ้าสถิต สำหรับกล่องและฟิกซ์เจอร์ที่ต้องการความแข็งแรงสูง' },
    { key: 'ABS-FR', name: 'ABS-FR — Flame-Retardant ABS', group: 'Specialty', colors: 'neutral', desc: 'Flame-retardant ABS for enclosures with fire-safety requirements.', descTh: 'ABS หน่วงไฟ สำหรับกล่องและชิ้นส่วนที่ต้องการความปลอดภัยด้านอัคคีภัย' },
    { key: 'PVA', name: 'PVA — Water-Soluble Support', group: 'Specialty', colors: 'support', desc: 'Water-soluble support material that dissolves away cleanly — ideal for complex overhangs and internal channels.', descTh: 'วัสดุรองรับละลายน้ำได้ ล้างออกได้สะอาด เหมาะกับชิ้นงานที่มีส่วนยื่นหรือช่องภายในซับซ้อน' },
    { key: 'M68', name: 'Standard Resin', group: 'Resin (SLA / DLP)', colors: 'resinStandard', desc: 'Smooth, highly detailed finish. Great for miniatures, visual prototypes and presentation models.', descTh: 'ผิวเรียบ เก็บรายละเอียดได้ดี เหมาะกับโมเดลขนาดเล็ก ต้นแบบเพื่อความสวยงาม และชิ้นงานนำเสนอ' },
    { key: 'K', name: 'Tough Resin', group: 'Resin (SLA / DLP)', colors: 'resinTough', desc: 'Higher impact and flex resistance than standard resin, for functional snap-fit and mechanical parts.', descTh: 'ทนแรงกระแทกและการงอได้ดีกว่าเรซินมาตรฐาน เหมาะกับสแนปฟิตและชิ้นส่วนกลไก' },
    { key: 'G217', name: 'Transparent Resin', group: 'Resin (SLA / DLP)', colors: 'resinClear', desc: 'Optically clear resin for lenses, light guides and see-through prototypes.', descTh: 'เรซินใสสำหรับเลนส์ ตัวนำแสง และต้นแบบที่ต้องการมองทะลุได้' },
    { key: 'F80', name: 'Flexible Resin', group: 'Resin (SLA / DLP)', colors: 'resinFlexible', desc: 'Soft, rubber-like resin for gaskets, seals and flexible details.', descTh: 'เรซินนุ่มคล้ายยาง เหมาะกับปะเก็น ซีล และรายละเอียดที่ต้องการความยืดหยุ่น' },
    { key: 'CASTWAX', name: 'Castable Wax Resin', group: 'Resin (SLA / DLP)', colors: 'resinCastable', desc: 'Burns out cleanly for jewelry and investment-casting workflows.', descTh: 'เผาไหม้หมดจด เหมาะกับงานเครื่องประดับและกระบวนการหล่อแบบขี้ผึ้งหาย' }
];

// Extensions three.js can load directly, plus CAD formats handled through occt.
var SUPPORTED_EXTENSIONS = ['stl', 'obj', '3mf', 'glb', 'gltf', 'stp', 'step', 'igs', 'iges'];

// DFM geometry thresholds (soft, informational checks — final call is manual review).
var MIN_REASONABLE_DIMENSION_MM = 3;
var MAX_BUILD_DIMENSION_MM = 350;

function GetFileExtension(filename) {
    var lower = (filename || '').toLowerCase();
    var dot = lower.lastIndexOf('.');
    return dot >= 0 ? lower.substring(dot + 1) : '';
}

function IsSupportedModelFile(filename) {
    return SUPPORTED_EXTENSIONS.indexOf(GetFileExtension(filename)) !== -1;
}

function GetMaterial(key) {
    for (var i = 0; i < MATERIALS.length; i++) {
        if (MATERIALS[i].key === key) {
            return MATERIALS[i];
        }
    }
    return MATERIALS[0];
}

// A small helper to build an element with an optional class and text content.
function CreateEl(tag, className, text) {
    var el = document.createElement(tag);
    if (className) { el.className = className; }
    if (text !== undefined && text !== null) { el.textContent = text; }
    return el;
}

// Builds the material selection cards (as real DOM nodes, filtered by an optional
// search term matched against the material name and description — immediate,
// case-insensitive substring).
function BuildMaterialCardsFragment(selectedKey, filterText, culture) {
    var needle = (filterText || '').trim().toLowerCase();
    var frag = document.createDocumentFragment();
    var matched = 0;
    for (var i = 0; i < MATERIALS.length; i++) {
        var m = MATERIALS[i];
        if (needle && m.name.toLowerCase().indexOf(needle) === -1 && m.desc.toLowerCase().indexOf(needle) === -1 && m.descTh.toLowerCase().indexOf(needle) === -1) {
            continue;
        }
        matched++;
        var card = CreateEl('div', 'iq-mat-card' + (m.key === selectedKey ? ' sel' : ''));
        card.dataset.key = m.key;
        card.addEventListener('click', (function (key) { return function () { SelectMaterialCard(key); }; })(m.key));
        var parts = m.name.split(' — ');
        var titleRow = CreateEl('div', 'iq-mat-title-row');
        titleRow.appendChild(CreateEl('div', 'name', parts[0]));
        var info = CreateEl('button', 'iq-mat-info');
        info.type = 'button';
        info.title = culture === 'th' ? m.descTh : m.desc;
        info.setAttribute('aria-label', info.title);
        var infoIcon = CreateEl('i', 'fas fa-info-circle');
        infoIcon.setAttribute('aria-hidden', 'true');
        info.appendChild(infoIcon);
        info.addEventListener('click', function (event) { event.stopPropagation(); });
        titleRow.appendChild(info);
        card.appendChild(titleRow);
        card.appendChild(CreateEl('div', 'full', parts[1] || ''));
        frag.appendChild(card);
    }
    if (matched === 0) {
        frag.appendChild(CreateEl('div', 'iq-mat-empty text-muted small', culture === 'th' ? 'ไม่พบวัสดุที่ตรงกับคำค้นหา' : 'No materials match your search.'));
    }
    return frag;
}

// Rebuilds a <select>'s <option> list for the given material's colour set.
function ColorLabel(value, culture) {
    var labels = { Any: 'สีอะไรก็ได้', Black: 'ดำ', White: 'ขาว', Gray: 'เทา', Silver: 'เงิน', Red: 'แดง', Orange: 'ส้ม', Yellow: 'เหลือง', Green: 'เขียว', Blue: 'น้ำเงิน', Purple: 'ม่วง', Pink: 'ชมพู', Natural: 'ธรรมชาติ', Clear: 'ใส', Translucent: 'โปร่งแสง' };
    return culture === 'th' ? (labels[value] || value) : (value === 'Any' ? 'No preference' : value);
}

function PopulateColorSelect(selectEl, materialKey, selectedValue, culture) {
    var material = GetMaterial(materialKey);
    var colorSet = COLOR_SETS[material.colors] || COLOR_SETS.full;
    var colors = colorSet.colors;
    var isCustom = !!(colorSet.custom && selectedValue && selectedValue.charAt(0) === '#');
    var options = [];
    for (var i = 0; i < colors.length; i++) {
        var value = colors[i];
        var opt = document.createElement('option');
        opt.value = value;
        opt.textContent = ColorLabel(value, culture);
        if (!isCustom && (value === selectedValue || (!selectedValue && i === 0))) { opt.selected = true; }
        options.push(opt);
    }
    if (isCustom) {
        var customOpt = document.createElement('option');
        customOpt.value = selectedValue;
        customOpt.textContent = (culture === 'th' ? 'กำหนดเอง' : 'Custom') + ' (' + selectedValue + ')';
        customOpt.selected = true;
        options.push(customOpt);
    }
    if (colorSet.custom) {
        var trigger = document.createElement('option');
        trigger.value = CUSTOM_COLOR_VALUE;
        trigger.textContent = culture === 'th' ? 'กำหนดเอง…' : 'Custom…';
        options.push(trigger);
    }
    selectEl.replaceChildren.apply(selectEl, options);
}

// Rebuilds the circular colour swatches for the given material's colour set, kept in
// sync with whichever value is selected (named colour or a custom "#rrggbb" hex).
function PopulateColorSwatches(container, materialKey, selectedValue, culture) {
    var material = GetMaterial(materialKey);
    var colorSet = COLOR_SETS[material.colors] || COLOR_SETS.full;
    var colors = colorSet.colors;
    var isCustom = !!(colorSet.custom && selectedValue && selectedValue.charAt(0) === '#');
    var nodes = [];
    for (var i = 0; i < colors.length; i++) {
        var value = colors[i];
        var sel = !isCustom && (value === selectedValue || (!selectedValue && i === 0));
        var swatch = CreateEl('div', 'iq-swatch' + (sel ? ' sel' : ''));
        swatch.title = ColorLabel(value, culture);
        swatch.style.background = COLOR_SWATCH_HEX[value] || '#cccccc';
        swatch.dataset.value = value;
        swatch.addEventListener('click', (function (v) { return function () { SelectColorSwatch(v); }; })(value));
        nodes.push(swatch);
    }
    if (colorSet.custom) {
        var custom = CreateEl('div', 'iq-swatch iq-swatch-custom' + (isCustom ? ' sel' : ''), '✎');
        custom.title = culture === 'th' ? 'กำหนดสีเอง' : 'Custom color';
        custom.style.background = isCustom ? selectedValue : 'conic-gradient(red,#e8c93a,lime,cyan,blue,magenta,red)';
        custom.addEventListener('click', function () { OpenCustomColorPicker(); });
        nodes.push(custom);
    }
    container.replaceChildren.apply(container, nodes);
}

// ---------------------------------------------------------------------------
// On-demand OpenCascade loader for STEP / IGES
// ---------------------------------------------------------------------------

var occtPromise = null;

function EnsureOcct() {
    if (occtPromise) {
        return occtPromise;
    }

    occtPromise = new Promise(function (resolve, reject) {
        if (typeof occtimportjs !== 'undefined') {
            occtimportjs({ locateFile: function (name) { return '/lib/occt/' + name; } }).then(resolve).catch(reject);
            return;
        }

        var script = document.createElement('script');
        script.src = '/lib/occt/occt-import-js.js';
        script.onload = function () {
            occtimportjs({ locateFile: function (name) { return '/lib/occt/' + name; } }).then(resolve).catch(reject);
        };
        script.onerror = function () { reject(new Error('Unable to load STEP/IGES support.')); };
        document.head.appendChild(script);
    });

    return occtPromise;
}

// Builds a THREE.Group from an occt read result (positions + indices per mesh).
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
        if (m.index) {
            geometry.setIndex(new THREE.Uint32BufferAttribute(m.index.array, 1));
        }
        geometry.computeVertexNormals();
        group.add(new THREE.Mesh(geometry, material));
    }

    return group;
}

var MULTI_BODY_PREVIEW_COLORS = [0xb9c3ca, 0x2f6da8, 0xd69d16, 0xc95b2b, 0x4d8c58, 0x7151a8, 0x9a5b62];

function CreatePreviewMaterial(color) {
    // CAD-style, non-metallic materials retain detail against the light canvas.
    return new THREE.MeshStandardMaterial({ color: color, metalness: 0.04, roughness: 0.64, flatShading: false, side: THREE.DoubleSide });
}

function DefaultMaterial() {
    return CreatePreviewMaterial(0xb9c3ca);
}

// Splits disconnected STL shells where practical and gives every separate body
// a stable CAD-style preview colour. CAD imports that already expose one mesh per
// body receive the same distinct material treatment.
function ApplyMultiBodyPreviewColors(object3D) {
    var meshes = [];
    var geometryUseCounts = new Map();
    object3D.traverse(function (child) {
        if (child.isMesh && child.geometry && child.geometry.attributes && child.geometry.attributes.position) {
            meshes.push(child);
            geometryUseCounts.set(child.geometry, (geometryUseCounts.get(child.geometry) || 0) + 1);
        }
    });

    var colorIndex = 0;
    meshes.forEach(function (mesh) {
        var bodies = FindConnectedMeshBodies(mesh.geometry);
        if (bodies && bodies.length > 1) {
            var separated = BuildSeparatedBodyGeometry(mesh.geometry, bodies, colorIndex);
            if (separated) {
                var previousGeometry = mesh.geometry;
                mesh.geometry = separated.geometry;
                mesh.material = separated.materials;
                // A glTF scene can instance one geometry across several meshes. Disposing
                // a shared source geometry here would make the other bodies disappear.
                if (geometryUseCounts.get(previousGeometry) === 1) {
                    previousGeometry.dispose();
                }
                colorIndex += bodies.length;
                return;
            }
        }

        mesh.material = CreatePreviewMaterial(MULTI_BODY_PREVIEW_COLORS[colorIndex % MULTI_BODY_PREVIEW_COLORS.length]);
        colorIndex++;
    });
}

function FindConnectedMeshBodies(geometry) {
    var position = geometry && geometry.attributes && geometry.attributes.position;
    var index = geometry && geometry.index;
    var vertexCount = index ? index.count : (position ? position.count : 0);
    var triangleCount = vertexCount / 3;
    if (!position || triangleCount < 2 || vertexCount % 3 !== 0 || triangleCount > 200000) {
        return null;
    }

    var bounds = new THREE.Box3().setFromBufferAttribute(position);
    var diagonal = bounds.getSize(new THREE.Vector3()).length();
    var grid = Math.max(diagonal * 1e-5, 1e-4);
    var parent = new Map();
    var records = [];

    function keyFor(vertexIndex) {
        return Math.round(position.getX(vertexIndex) / grid) + '_' + Math.round(position.getY(vertexIndex) / grid) + '_' + Math.round(position.getZ(vertexIndex) / grid);
    }
    function add(key) { if (!parent.has(key)) { parent.set(key, key); } }
    function find(key) {
        var root = key;
        while (parent.get(root) !== root) { root = parent.get(root); }
        while (parent.get(key) !== key) {
            var next = parent.get(key);
            parent.set(key, root);
            key = next;
        }
        return root;
    }
    function join(a, b) {
        var rootA = find(a);
        var rootB = find(b);
        if (rootA !== rootB) { parent.set(rootA, rootB); }
    }

    for (var triangle = 0; triangle < triangleCount; triangle++) {
        var offset = triangle * 3;
        var a = index ? index.getX(offset) : offset;
        var b = index ? index.getX(offset + 1) : offset + 1;
        var c = index ? index.getX(offset + 2) : offset + 2;
        var keyA = keyFor(a);
        var keyB = keyFor(b);
        var keyC = keyFor(c);
        add(keyA); add(keyB); add(keyC);
        join(keyA, keyB); join(keyB, keyC);
        records.push({ a: a, b: b, c: c, key: keyA });
    }

    var components = new Map();
    records.forEach(function (record) {
        var root = find(record.key);
        if (!components.has(root)) { components.set(root, []); }
        components.get(root).push(record);
    });

    return Array.from(components.values());
}

function BuildSeparatedBodyGeometry(sourceGeometry, bodies, colorOffset) {
    var position = sourceGeometry.attributes.position;
    var vertexTotal = 0;
    bodies.forEach(function (body) { vertexTotal += body.length * 3; });
    if (vertexTotal === 0) { return null; }

    var positions = new Float32Array(vertexTotal * 3);
    var geometry = new THREE.BufferGeometry();
    var materials = [];
    var write = 0;

    function writeVertex(vertexIndex) {
        positions[write++] = position.getX(vertexIndex);
        positions[write++] = position.getY(vertexIndex);
        positions[write++] = position.getZ(vertexIndex);
    }

    bodies.forEach(function (body, bodyIndex) {
        var groupStart = write / 3;
        body.forEach(function (triangle) {
            writeVertex(triangle.a);
            writeVertex(triangle.b);
            writeVertex(triangle.c);
        });
        geometry.addGroup(groupStart, body.length * 3, bodyIndex);
        materials.push(CreatePreviewMaterial(MULTI_BODY_PREVIEW_COLORS[(colorOffset + bodyIndex) % MULTI_BODY_PREVIEW_COLORS.length]));
    });

    geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
    geometry.computeVertexNormals();
    return { geometry: geometry, materials: materials };
}

// ---------------------------------------------------------------------------
// Geometry analysis: bounding box, volume, surface area, facets, DFM checks
// ---------------------------------------------------------------------------

var AREA_PROFILE_SAMPLES = 64;

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

    // Approximate local wall thickness as 2*area/perimeter (exact for a uniform slab)
    // at each sampled layer, and take the thinnest — a lightweight proxy, not a true
    // distance-transform min-thickness, but enough to flag obviously thin sections.
    var minThickness = null;
    if (profile) {
        for (var s = 0; s < profile.area.length; s++) {
            if (profile.perimeter[s] > 1e-6 && profile.area[s] > 0) {
                var thickness = (2 * profile.area[s]) / profile.perimeter[s];
                if (minThickness === null || thickness < minThickness) {
                    minThickness = thickness;
                }
            }
        }
    }
    if (minThickness === null) {
        minThickness = Math.min(dx || Infinity, dy || Infinity, dz || Infinity);
        if (!isFinite(minThickness)) { minThickness = 0; }
    }

    var maxDimension = Math.max(dx, dy, dz);
    var minDimension = Math.min(dx, dy, dz);

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
        oddlySmall: minDimension > 0 && minDimension < MIN_REASONABLE_DIMENSION_MM,
        oddlyLarge: maxDimension > MAX_BUILD_DIMENSION_MM
    };
}

// ---------------------------------------------------------------------------
// ModelViewer - a single shared canvas/scene; the active part's object3D is
// swapped in and out as the customer switches between uploaded parts.
// ---------------------------------------------------------------------------

function ModelViewer(canvasElement) {

    var liveCanvasColor = 0xf7f9fc;
    var thumbnailCanvasColor = 0xffffff;
    var canvasAspectRatio = 1.35;
    var fieldOfView = 45;
    var nearDistance = 0.1;
    var farDistance = 100000;
    var displayObject = null;
    var activeModelInfo = null;
    var controls;

    var scene = new THREE.Scene();

    var camera = new THREE.PerspectiveCamera(fieldOfView, canvasAspectRatio, nearDistance, farDistance);
    camera.up.set(0, 0, 1);

    var renderer = new THREE.WebGLRenderer({ canvas: canvasElement, antialias: true, preserveDrawingBuffer: true, alpha: false });
    renderer.setClearColor(liveCanvasColor, 1);
    renderer.setPixelRatio(window.devicePixelRatio || 1);
    SizeRenderer();

    AddLights(scene);
    AddOrbitalControls(camera);

    var render = function () {
        requestAnimationFrame(render);
        renderer.render(scene, camera);
    };
    render();

    window.addEventListener('resize', SizeRenderer, false);

    function SizeRenderer() {
        var width = canvasElement.clientWidth || (canvasElement.parentElement ? canvasElement.parentElement.clientWidth : 300);
        var height = canvasElement.clientHeight || (width / canvasAspectRatio);
        camera.aspect = width / height;
        camera.updateProjectionMatrix();
        renderer.setSize(width, height, false);
    }

    this.AdjustCanvasSize = function (refitActivePart) {
        setTimeout(function () {
            SizeRenderer();
            if (refitActivePart && displayObject) {
                RecenterAndFrame();
            }
        }, 1);
    };

    // Parses a model file into an object3D + geometry analysis without displaying it;
    // the caller decides when (and whether) to show it via ShowObject.
    this.ParseFile = function (file, onReady) {
        var extension = GetFileExtension(file.name);

        var handleObject = function (object3D) {
            if (extension === 'glb' || extension === 'gltf') {
                object3D.rotation.x = Math.PI / 2; // Y-up (glTF) -> Z-up (printing)
            }
            var modelInfo = AnalyzeObject(object3D);
            if (modelInfo.bodyCount > 1) {
                ApplyMultiBodyPreviewColors(object3D);
            }
            onReady(modelInfo, object3D, null);
        };

        var onError = function (message) {
            onReady(null, null, message || 'Unable to read this model file.');
        };

        try {
            if (extension === 'stl') {
                ReadArrayBuffer(file, function (buffer) {
                    var geometry = new THREE.STLLoader().parse(buffer);
                    geometry.computeVertexNormals();
                    handleObject(new THREE.Mesh(geometry, DefaultMaterial()));
                }, onError);
            } else if (extension === 'obj') {
                ReadText(file, function (text) {
                    handleObject(new THREE.OBJLoader().parse(text));
                }, onError);
            } else if (extension === '3mf') {
                ReadArrayBuffer(file, function (buffer) {
                    try {
                        handleObject(new THREE.ThreeMFLoader().parse(buffer));
                    } catch (e) {
                        // Composite 3MF files can reference a missing object. The bundled
                        // ThreeMFLoader throws in that case; keep the quotation flow alive
                        // and let the caller show its normal recoverable file-read message.
                        onError('Unable to read this 3MF file. It may contain an unsupported or missing component.');
                    }
                }, onError);
            } else if (extension === 'glb' || extension === 'gltf') {
                ReadArrayBuffer(file, function (buffer) {
                    new THREE.GLTFLoader().parse(buffer, '', function (gltf) {
                        handleObject(gltf.scene);
                    }, function () { onError('Unable to read this glTF/GLB file.'); });
                }, onError);
            } else if (extension === 'stp' || extension === 'step' || extension === 'igs' || extension === 'iges') {
                ReadArrayBuffer(file, function (buffer) {
                    EnsureOcct().then(function (occt) {
                        var data = new Uint8Array(buffer);
                        var result = (extension === 'igs' || extension === 'iges')
                            ? occt.ReadIgesFile(data, null)
                            : occt.ReadStepFile(data, null);
                        if (!result || !result.success) {
                            onError('Unable to tessellate this CAD file.');
                            return;
                        }
                        handleObject(OcctResultToGroup(result));
                    }).catch(function () { onError('STEP/IGES support failed to load.'); });
                }, onError);
            } else {
                onError('Unsupported file type.');
            }
        } catch (e) {
            onError('This model file could not be read.');
        }
    };

    // Displays the given (already-parsed) object3D, or clears the viewer if null.
    this.ShowObject = function (object3D, modelInfo, viewState) {
        if (displayObject) {
            scene.remove(displayObject);
        }
        displayObject = object3D || null;
        activeModelInfo = modelInfo || null;
        if (displayObject) {
            scene.add(displayObject);
            RecenterAndFrame(viewState);
        }
    };

    // Applies the order colour to every mesh in the currently visible part.
    // This intentionally preserves each material's other rendering properties
    // (roughness, opacity, normal maps) while synchronising its visible colour.
    this.SetPreviewColor = function (colorValue) {
        if (!displayObject || (activeModelInfo && activeModelInfo.bodyCount > 1)) { return; }
        var previewColor = ResolvePreviewColor(colorValue);
        displayObject.traverse(function (child) {
            if (!child.isMesh || !child.material) { return; }
            var materials = Array.isArray(child.material) ? child.material : [child.material];
            materials.forEach(function (material) {
                if (material && material.color && material.color.set) {
                    material.color.set(previewColor);
                    material.needsUpdate = true;
                }
            });
        });
    };

    // Frees GPU resources for an object3D that is no longer needed (item removed).
    this.DisposeObject = function (object3D) {
        if (!object3D) { return; }
        object3D.traverse(function (child) {
            if (child.isMesh) {
                if (child.geometry) { child.geometry.dispose(); }
                if (child.material) {
                    if (Array.isArray(child.material)) {
                        child.material.forEach(function (m) { m.dispose(); });
                    } else {
                        child.material.dispose();
                    }
                }
            }
        });
    };

    this.Snapshot = function () {
        var data = null;
        try {
            // Thumbnails sit on white cards, while the persistent viewer blends
            // into the configurator's pale surface. Render the capture once on
            // white, then immediately restore the live canvas colour.
            renderer.setClearColor(thumbnailCanvasColor, 1);
            renderer.render(scene, camera);
            data = canvasElement.toDataURL('image/png');
        } catch (e) {
            data = null;
        } finally {
            renderer.setClearColor(liveCanvasColor, 1);
            renderer.render(scene, camera);
        }
        return (data === 'data:,') ? null : data;
    };

    this.ResetView = function () { RecenterAndFrame(); };
    this.FitView = function () { RecenterAndFrame(); };

    this.CaptureView = function () {
        if (!controls || !displayObject) { return null; }
        return { position: { x: camera.position.x, y: camera.position.y, z: camera.position.z }, target: { x: controls.target.x, y: controls.target.y, z: controls.target.z } };
    };

    function RecenterAndFrame(viewState) {
        if (!displayObject || !activeModelInfo) {
            return;
        }
        // Centre every model around its own bounds before framing. This keeps a tall,
        // long, or offset mesh in the visual centre instead of resting on the canvas floor.
        displayObject.position.set(
            -(activeModelInfo.min.x + activeModelInfo.size.x * 0.5),
            -(activeModelInfo.min.y + activeModelInfo.size.y * 0.5),
            -(activeModelInfo.min.z + activeModelInfo.size.z * 0.5));

        displayObject.updateMatrixWorld(true);
        SizeRenderer();
        var framedBounds = new THREE.Box3().setFromObject(displayObject);
        var framedSphere = framedBounds.getBoundingSphere(new THREE.Sphere());

        var maxXYZ = Math.max(activeModelInfo.size.x, activeModelInfo.size.y, activeModelInfo.size.z, 1);
        if (controls) { controls.dispose(); }
        AddOrbitalControls(camera);
        controls.target.copy(framedSphere.center);
        camera.up.set(0, 0, 1);
        if (viewState && viewState.position && viewState.target) {
            camera.position.set(viewState.position.x, viewState.position.y, viewState.position.z);
            controls.target.set(viewState.target.x, viewState.target.y, viewState.target.z);
        } else {
            var verticalFov = THREE.MathUtils.degToRad(fieldOfView);
            var horizontalFov = 2 * Math.atan(Math.tan(verticalFov / 2) * camera.aspect);
            var radius = Math.max(framedSphere.radius, maxXYZ * 0.01, 1);
            // Leave a deliberate perimeter around the bounding sphere so wide,
            // shallow parts remain completely visible on the initial render.
            var distance = radius / Math.sin(Math.min(verticalFov, horizontalFov) / 2) * 1.22;
            var cameraDirection = new THREE.Vector3(0.9, -0.9, 0.72).normalize();
            camera.position.copy(framedSphere.center).addScaledVector(cameraDirection, distance);
        }
        camera.lookAt(controls.target);
        controls.update();
    }

    function AddOrbitalControls(cam) {
        controls = new THREE.OrbitControls(cam, renderer.domElement);
        controls.enableZoom = true;
        controls.enablePan = true;
        controls.rotateSpeed = 1;
        controls.update();
    }

    function AddLights(target) {
        var hemi = new THREE.HemisphereLight(0xffffff, 0xd6dfe6, 0.56);
        target.add(hemi);

        var key = new THREE.DirectionalLight(0xffffff, 0.64);
        key.position.set(1, 0.6, 1.2).multiplyScalar(50);
        target.add(key);

        var fill = new THREE.DirectionalLight(0xdbe6ef, 0.26);
        fill.position.set(-1, -0.3, 0.6).multiplyScalar(50);
        target.add(fill);

        var rim = new THREE.DirectionalLight(0xb8c8d8, 0.1);
        rim.position.set(-0.4, 1, -0.6).multiplyScalar(50);
        target.add(rim);
    }

    function ReadArrayBuffer(file, onLoad, onError) {
        var reader = new FileReader();
        reader.onload = function () { onLoad(reader.result); };
        reader.onerror = function () { onError('Failed to read the file.'); };
        reader.readAsArrayBuffer(file);
    }

    function ReadText(file, onLoad, onError) {
        var reader = new FileReader();
        reader.onload = function () { onLoad(reader.result); };
        reader.onerror = function () { onError('Failed to read the file.'); };
        reader.readAsText(file);
    }
}

// ---------------------------------------------------------------------------
// ModelViewerUtils - orchestrates part state (one shared config UI, many parts),
// the thumbnail strip, and order totals.
// ---------------------------------------------------------------------------

function ModelViewerUtils(culture, currency, viewer) {

    var currencyString = currency;
    var pleaseWaitText = culture === 'th' ? 'กรุณารอสักครู่' : 'Please wait';
    var submitReviewText = culture === 'th' ? 'ตรวจสอบรายการทั้งหมด' : 'Review all items';
    var errorText = culture === 'th' ? 'เกิดความผิดพลาด' : 'Error occured';

    var unfinishedTasks = 0;
    var hasError = false;
    var localPreviewAfterUploadFailure = false;
    var submitAllow = document.getElementById('submit-gate');

    var items = {}; // id -> { file, object3D, modelInfo, material, color, quantity }
    var activeId = null;
    var materialSearch = '';
    var self = this;

    function IsActiveMultiBody() {
        return !!(activeId && items[activeId] && items[activeId].modelInfo && items[activeId].modelInfo.bodyCount > 1);
    }

    function UpdateColorControlAvailability() {
        var disabled = IsActiveMultiBody();
        var select = document.getElementById('color-select');
        var swatches = document.getElementById('color-swatches');
        var customInput = document.getElementById('custom-color-input');
        var note = document.getElementById('multi-body-color-note');
        if (select) { select.disabled = disabled; }
        if (swatches) { swatches.classList.toggle('iq-color-controls-disabled', disabled); }
        if (customInput) { customInput.disabled = disabled; }
        if (note) { note.hidden = !disabled; }
    }

    this.GetActiveId = function () { return activeId; };
    this.GetItem = function (id) { return items[id]; };
    this.GetAllItemIds = function () { return Object.keys(items); };

    this.RegisterItem = function (id, file) {
        items[id] = {
            file: file,
            object3D: null,
            modelInfo: null,
            material: 'PLA',
            color: null,
            quantity: 1,
            parseComplete: false,
            uploadComplete: false,
            thumbnailComplete: false,
            thumbnailAvailable: false,
            errorMessage: null
        };
        CreateThumbnailNode(id, file);
        CreateSummaryNode(id, file);
    };

    this.SetItemParsed = function (id, object3D, modelInfo) {
        if (!items[id]) { return; }
        items[id].object3D = object3D;
        items[id].modelInfo = modelInfo;
        items[id].parseComplete = true;
        UpdateThumbnailDimensions(id, modelInfo);
        UpdateThumbnailState(id);
    };

    this.SetItemUploadComplete = function (id, succeeded) {
        if (!items[id]) { return; }
        items[id].uploadComplete = true;
        if (!succeeded) {
            this.SetItemFailed(id, culture === 'th' ? 'อัปโหลดไฟล์ไม่สำเร็จ' : 'Upload failed');
            return;
        }
        UpdateThumbnailState(id);
    };

    this.SetItemThumbnailComplete = function (id, available) {
        if (!items[id]) { return; }
        items[id].thumbnailComplete = true;
        items[id].thumbnailAvailable = available === true;
        UpdateThumbnailState(id);
    };

    this.SetItemFailed = function (id, message) {
        if (!items[id]) { return; }
        items[id].errorMessage = message || (culture === 'th' ? 'ไม่สามารถประมวลผลไฟล์ได้' : 'File processing failed');
        hasError = true;
        UpdateThumbnailState(id);
        CheckReadyState();
    };

    // Makes the given part the one shown in the viewer and reflected by the shared
    // configuration controls (material, colour, quantity, file info, DFM analysis).
    this.SetActiveItem = function (id) {
        if (!items[id]) { return; }
        if (activeId && activeId !== id && items[activeId]) { items[activeId].viewState = viewer.CaptureView(); }
        activeId = id;

        materialSearch = '';
        var materialSearchInput = document.getElementById('material-search');
        if (materialSearchInput) { materialSearchInput.value = ''; }

        document.querySelectorAll('.iq-thumb').forEach(function (el) { el.classList.remove('sel'); });
        var thumb = document.getElementById('thumb-' + id);
        if (thumb) { thumb.classList.add('sel'); }

        var item = items[id];
        viewer.ShowObject(item.object3D, item.modelInfo, item.viewState);
        // Fit a newly selected part only after the canvas has settled. A saved
        // per-part view remains authoritative when the customer returns to it.
        setTimeout(function () { if (viewer) { viewer.AdjustCanvasSize(!item.viewState); } }, 1);

        var nameEl = document.getElementById('active-file-name');
        if (nameEl) { nameEl.textContent = item.file.name; }

        this.RenderFileInfo(item.modelInfo);
        this.RenderDfmAlerts(item.modelInfo);
        this.RenderMaterialCards();

        var materialInput = document.getElementById('material-hidden');
        if (materialInput) { materialInput.value = item.material; }
        this.PopulateColors(item.material, item.color);

        var qtyInput = document.getElementById('quantity-input');
        if (qtyInput) { qtyInput.value = item.quantity; }
    };

    this.RenderFileInfo = function (info) {
        info = info || { size: { x: 0, y: 0, z: 0 }, volume: 0, surfaceAreaMm2: 0, minThicknessMm: 0, facets: 0 };
        SetText('file-info-dimensions', ToTwoDecimalPoint(info.size.x) + ' × ' + ToTwoDecimalPoint(info.size.y) + ' × ' + ToTwoDecimalPoint(info.size.z) + ' mm');
        SetText('file-info-volume', ToTwoDecimalPoint(info.volume / 1000) + ' cm³');
        SetText('file-info-surface', ToTwoDecimalPoint(info.surfaceAreaMm2 / 100) + ' cm²');
        SetText('file-info-thickness', ToTwoDecimalPoint(info.minThicknessMm) + ' mm');
        SetText('file-info-facets', Math.round(info.facets).toLocaleString('en-US'));
    };

    this.RenderDfmAlerts = function (info) {
        var container = document.getElementById('dfm-alerts');
        if (!container) { return; }
        if (!info) { container.replaceChildren(); return; }

        var alerts = BuildDfmAlerts(info, culture);
        var nodes = alerts.map(function (a) {
            var row = CreateEl('div', 'iq-alert ' + a.level);
            var icon = CreateEl('span', 'iq-alert-icon');
            var iconClass = a.level === 'danger' ? 'fas fa-times' : 'fas fa-exclamation';
            var iconElement = CreateEl('i', iconClass);
            iconElement.setAttribute('aria-hidden', 'true');
            icon.appendChild(iconElement);
            row.appendChild(icon);
            var textWrap = document.createElement('div');
            textWrap.appendChild(CreateEl('b', null, a.title));
            textWrap.appendChild(document.createElement('br'));
            textWrap.appendChild(document.createTextNode(a.subtitle));
            row.appendChild(textWrap);
            return row;
        });
        if (nodes.length === 0) {
            var okRow = CreateEl('div', 'iq-alert ok');
            var okIcon = CreateEl('span', 'iq-alert-icon');
            var okIconElement = CreateEl('i', 'fas fa-check');
            okIconElement.setAttribute('aria-hidden', 'true');
            okIcon.appendChild(okIconElement);
            okRow.appendChild(okIcon);
            okRow.appendChild(CreateEl('span', null, culture === 'th' ? 'ไม่พบปัญหา' : 'No issues detected'));
            nodes = [okRow];
        }
        container.replaceChildren.apply(container, nodes);

        // Persist the plain-text summary so it reaches the manufacturing review email.
        var warningText = alerts.map(function (a) { return a.title; }).join('; ');
        var hidden = document.getElementById('item-warning-' + activeId);
        if (hidden) { hidden.value = warningText; }
        var thumbWarning = document.getElementById('thumb-warning-' + activeId);
        if (thumbWarning) { thumbWarning.style.display = warningText ? 'block' : 'none'; }
    };

    this.RenderMaterialCards = function () {
        var container = document.getElementById('material-cards');
        if (!container || !activeId) { return; }
        container.replaceChildren(BuildMaterialCardsFragment(items[activeId].material, materialSearch, culture));
    };

    this.SetMaterialSearch = function (text) {
        materialSearch = text || '';
        this.RenderMaterialCards();
    };

    this.SelectMaterial = function (key) {
        if (!activeId) { return; }
        items[activeId].material = key;
        items[activeId].color = null; // colour choices depend on the material; reset to its default
        var materialInput = document.getElementById('material-hidden');
        if (materialInput) { materialInput.value = key; }
        this.RenderMaterialCards();
        this.PopulateColors(key, null);
    };

    this.PopulateColors = function (materialKey, selectedValue) {
        var select = document.getElementById('color-select');
        var swatches = document.getElementById('color-swatches');
        if (select) { PopulateColorSelect(select, materialKey, selectedValue, culture); }
        if (swatches) { PopulateColorSwatches(swatches, materialKey, selectedValue || SelectValue('color-select'), culture); }
        if (activeId) {
            if (IsActiveMultiBody()) {
                items[activeId].color = 'Any';
            } else {
                items[activeId].color = SelectValue('color-select');
                viewer.SetPreviewColor(items[activeId].color);
            }
        }
        UpdateColorControlAvailability();
    };

    // Keeps the dropdown and the circular swatches in sync, in both directions.
    this.SelectColorFromDropdown = function (value) {
        if (IsActiveMultiBody()) { return; }
        if (value === CUSTOM_COLOR_VALUE) {
            this.OpenCustomColorPicker();
            return;
        }
        if (activeId) {
            items[activeId].color = value;
            viewer.SetPreviewColor(value);
        }
        var swatches = document.getElementById('color-swatches');
        if (swatches && activeId) { PopulateColorSwatches(swatches, items[activeId].material, value, culture); }
    };

    this.SelectColorSwatch = function (value) {
        if (!activeId || IsActiveMultiBody()) { return; }
        items[activeId].color = value;
        viewer.SetPreviewColor(value);
        var select = document.getElementById('color-select');
        if (select) { PopulateColorSelect(select, items[activeId].material, value, culture); }
        var swatches = document.getElementById('color-swatches');
        if (swatches) { PopulateColorSwatches(swatches, items[activeId].material, value, culture); }
    };

    this.OpenCustomColorPicker = function () {
        if (IsActiveMultiBody()) { return; }
        var input = document.getElementById('custom-color-input');
        if (input) { input.click(); }
    };

    this.ApplyCustomColor = function (hex) {
        if (!activeId || IsActiveMultiBody()) { return; }
        items[activeId].color = hex;
        viewer.SetPreviewColor(hex);
        var select = document.getElementById('color-select');
        var swatches = document.getElementById('color-swatches');
        if (select) { PopulateColorSelect(select, items[activeId].material, hex, culture); }
        if (swatches) { PopulateColorSwatches(swatches, items[activeId].material, hex, culture); }
    };

    this.RenderBulkTable = function (tiers) {
        var body = document.getElementById('bulk-table-body');
        if (!body) { return; }
        var rows = tiers.map(function (t) {
            var tr = document.createElement('tr');
            if (t.active) { tr.className = 'iq-active-tier'; }
            var tdQty = document.createElement('td');
            tdQty.textContent = t.minQuantity + '+';
            var tdPrice = document.createElement('td');
            tdPrice.className = 'text-end';
            tdPrice.textContent = Number(t.unitPrice).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' ' + currencyString;
            tr.appendChild(tdQty);
            tr.appendChild(tdPrice);
            return tr;
        });
        body.replaceChildren.apply(body, rows);
    };

    this.SaveSelectedSettings = function () {
        if (!activeId) { return; }
        var item = items[activeId];
        item.color = IsActiveMultiBody() ? 'Any' : SelectValue('color-select');
        item.quantity = InputValue('quantity-input');
        SetValue('item-material-' + activeId, item.material);
        SetValue('item-color-' + activeId, item.color);
        SetValue('item-quantity-' + activeId, item.quantity);
        UpdateThumbnailQuote(activeId);
    };

    this.RemoveItem = function (id) {
        var item = items[id];
        if (item && item.object3D) {
            if (viewer && activeId === id) { viewer.ShowObject(null, null); }
            viewer.DisposeObject(item.object3D);
        }
        delete items[id];
        hasError = Object.keys(items).some(function (itemId) { return !!items[itemId].errorMessage; });

        var thumb = document.getElementById('thumb-' + id);
        if (thumb) { thumb.remove(); }
        var summary = document.getElementById('order-summary-item-' + id);
        if (summary) { summary.remove(); }

        if (activeId === id) {
            var remaining = Object.keys(items);
            activeId = null;
            if (remaining.length > 0) {
                this.SetActiveItem(remaining[remaining.length - 1]);
            } else {
                this.RenderFileInfo(null);
                this.RenderDfmAlerts(null);
                var nameEl = document.getElementById('active-file-name');
                if (nameEl) { nameEl.textContent = culture === 'th' ? 'ยังไม่ได้เลือกไฟล์' : 'No file selected'; }
                var cardsEl = document.getElementById('material-cards');
                if (cardsEl) { cardsEl.replaceChildren(); }
                var bulkEl = document.getElementById('bulk-table-body');
                if (bulkEl) { bulkEl.replaceChildren(); }
            }
        }
        CheckReadyState();
    };

    function CreateThumbnailNode(id, file) {
        var strip = document.getElementById('parts-strip');
        var node = CreateEl('div', 'iq-thumb is-processing');
        node.id = 'thumb-' + id;
        node.setAttribute('aria-busy', 'true');
        node.addEventListener('click', function () { SelectItem(id); });

        var removeBtn = CreateEl('button', 'iq-thumb-x', '×');
        removeBtn.type = 'button';
        removeBtn.setAttribute('aria-label', 'Remove');
        removeBtn.addEventListener('click', function (e) { e.stopPropagation(); RemoveOrderItem(id); });
        node.appendChild(removeBtn);

        var img = document.createElement('img');
        img.id = 'item-snapshot-' + id;
        img.className = 'iq-thumb-img';
        img.src = '/src/images/3d-canvas-placeholder.svg';
        img.alt = file.name + ' preview';
        node.appendChild(img);

        var status = CreateEl('div', 'iq-thumb-status');
        status.id = 'thumb-status-' + id;
        status.setAttribute('role', 'status');
        var spinner = CreateEl('span', 'iq-thumb-spinner');
        spinner.setAttribute('aria-hidden', 'true');
        status.appendChild(spinner);
        status.appendChild(CreateEl('span', 'iq-thumb-status-text', culture === 'th' ? 'กำลังประมวลผล…' : 'Processing…'));
        node.appendChild(status);

        var progress = CreateEl('div', 'iq-thumb-progress');
        progress.id = 'upload-progress-item-' + id;
        progress.appendChild(CreateEl('div', 'bar'));
        node.appendChild(progress);

        var meta = CreateEl('div', 'iq-thumb-meta');
        var nameEl = CreateEl('div', 'iq-thumb-name text-truncate', file.name);
        nameEl.title = file.name;
        meta.appendChild(nameEl);

        meta.appendChild(CreateEl('div', 'iq-thumb-size', FormatFileSize(file.size)));

        var quote = CreateEl('div', 'iq-thumb-summary');
        quote.id = 'thumb-quote-' + id;
        meta.appendChild(quote);

        var warn = CreateEl('div', 'iq-thumb-warning', '⚠');
        warn.id = 'thumb-warning-' + id;
        warn.style.display = 'none';
        meta.appendChild(warn);

        node.appendChild(meta);
        strip.appendChild(node);
        UpdateThumbnailQuote(id);
    }

    function UpdateThumbnailState(id) {
        var item = items[id];
        var node = document.getElementById('thumb-' + id);
        var status = document.getElementById('thumb-status-' + id);
        var statusText = status ? status.querySelector('.iq-thumb-status-text') : null;
        if (!item || !node || !status || !statusText) { return; }

        node.classList.toggle('is-error', !!item.errorMessage);
        if (item.errorMessage) {
            node.classList.remove('is-processing');
            node.setAttribute('aria-busy', 'false');
            status.hidden = false;
            statusText.textContent = item.errorMessage;
            return;
        }

        var ready = item.parseComplete && item.uploadComplete && item.thumbnailComplete;
        node.classList.toggle('is-processing', !ready);
        node.setAttribute('aria-busy', ready ? 'false' : 'true');
        if (!ready) {
            status.hidden = false;
            statusText.textContent = culture === 'th' ? 'กำลังประมวลผล…' : 'Processing…';
            return;
        }

        if (!item.thumbnailAvailable) {
            node.classList.add('has-preview-warning');
            status.hidden = false;
            statusText.textContent = culture === 'th' ? 'ไม่มีภาพตัวอย่าง' : 'Preview unavailable';
            return;
        }

        status.hidden = true;
    }

    function UpdateThumbnailQuote(id) {
        var quote = document.getElementById('thumb-quote-' + id);
        var item = items[id];
        if (!quote || !item) { return; }

        var unitCost = InputValue('item-estimated-unit-cost-' + id) || '—';
        var quantityLabel = culture === 'th' ? item.quantity + ' ชิ้น' : item.quantity + ' pcs';
        var priceLabel = culture === 'th' ? 'ราคาต่อชิ้น' : 'Unit price';
        quote.replaceChildren();
        quote.appendChild(CreateEl('div', 'iq-thumb-summary-config', item.material + ' · ' + quantityLabel));
        quote.appendChild(CreateEl('div', 'iq-thumb-summary-price', priceLabel + ' ' + unitCost));
        quote.title = item.material + ' · ' + quantityLabel + ' · ' + priceLabel + ' ' + unitCost;
    }

    function UpdateThumbnailDimensions(id, info) {
        SetText('dimension-x-' + id, ToTwoDecimalPoint(info.size.x));
        SetText('dimension-y-' + id, ToTwoDecimalPoint(info.size.y));
        SetText('dimension-z-' + id, ToTwoDecimalPoint(info.size.z));
        var dim = document.getElementById('item-dimensions-' + id);
        if (dim) {
            dim.value = ToTwoDecimalPoint(info.size.x) + ' x ' + ToTwoDecimalPoint(info.size.y) + ' x ' + ToTwoDecimalPoint(info.size.z);
        }
    }

    function CreateSummaryNode(id, file) {
        var container = document.getElementById('order-summary-items-collection');
        var node = document.createElement('div');
        node.id = 'order-summary-item-' + id;

        function AddHidden(idSuffix, name, value) {
            var input = document.createElement('input');
            input.type = 'hidden';
            if (idSuffix) { input.id = idSuffix; }
            input.name = name;
            if (value !== undefined) { input.value = value; }
            node.appendChild(input);
        }

        AddHidden(null, 'OrderItems.Index', id);
        AddHidden('item-filename-' + id, 'OrderItems[' + id + '].FileName', file.name);
        AddHidden('item-material-' + id, 'OrderItems[' + id + '].Material');
        AddHidden('item-color-' + id, 'OrderItems[' + id + '].Color');
        AddHidden('item-dimensions-' + id, 'OrderItems[' + id + '].Dimension');
        AddHidden('item-quantity-' + id, 'OrderItems[' + id + '].Quantity');
        AddHidden('item-estimated-unit-cost-' + id, 'OrderItems[' + id + '].EstimatedUnitCost');
        AddHidden('item-estimated-unit-time-' + id, 'OrderItems[' + id + '].EstimatedUnitPrintTime');
        AddHidden('item-estimated-total-cost-' + id, 'OrderItems[' + id + '].EstimatedTotalCost');
        AddHidden('item-estimated-total-time-' + id, 'OrderItems[' + id + '].EstimatedTotalPrintTime');
        AddHidden('item-upload-path-' + id, 'OrderItems[' + id + '].StoragePath');
        AddHidden('item-warning-' + id, 'OrderItems[' + id + '].GeometryWarning');

        container.appendChild(node);
    }

    this.IncreasePendingTask = function () { unfinishedTasks += 1; CheckReadyState(); };
    this.DecreasePendingTask = function () { if (unfinishedTasks > 0) { unfinishedTasks -= 1; } CheckReadyState(); };

    function CheckReadyState() {
        if (!submitAllow) { return; }
        var hasItems = Object.keys(items).length > 0;
        if (unfinishedTasks === 0 && !hasError && hasItems) {
            submitAllow.disabled = false;
            submitAllow.textContent = localPreviewAfterUploadFailure
                ? (culture === 'th' ? 'ตรวจสอบรายการทั้งหมด (โหมดทดสอบ)' : 'Review all items (local preview)')
                : submitReviewText;
        } else {
            submitAllow.disabled = true;
            submitAllow.textContent = hasError ? errorText : pleaseWaitText;
        }
    }

    // This is only invoked by the Razor page when its server-side environment
    // is Development. The resulting review flow has no submitting action.
    this.AllowLocalPreviewAfterUploadFailure = function () {
        localPreviewAfterUploadFailure = true;
        CheckReadyState();
    };
    this.BlockSubmission = function () { hasError = true; CheckReadyState(); };
    this.ClearError = function () { hasError = false; CheckReadyState(); };
    this.HasError = function () { return hasError; };
    this.ThrowError = function () {
        alert('There was an error getting an estimate for your part. Please contact us for a direct quotation.');
        this.BlockSubmission();
    };

    this.UpdateEstimatedTime = function () {
        var totalMinutes = 0;
        document.querySelectorAll("[id^=item-estimated-total-time-]").forEach(function (el) {
            var text = (el.value || '').replace(' minutes', '');
            var value = parseFloat(text);
            if (!isNaN(value)) { totalMinutes += value; }
        });

        var daysRequired = Math.max(1, Math.ceil(totalMinutes / 1440));
        var label = document.getElementById('total-estimated-time');
        var estimatedDueDate = document.getElementById('estimated-due-date');
        var text = culture === 'th'
            ? (daysRequired + ' - ' + (daysRequired + 2) + ' วัน')
            : (daysRequired + ' - ' + (daysRequired + 2) + ' days');
        if (label) { label.textContent = text; }
        if (estimatedDueDate) { estimatedDueDate.value = text; }
    };

    this.SetGrandTotal = function (finalPrice) {
        var element = document.getElementById('total-estimated-cost');
        var hidden = document.getElementById('estimated-total-price');
        var text = Number(finalPrice).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' ' + currencyString;
        if (element) { element.textContent = text; }
        if (hidden) { hidden.value = text; }
    };

    function SelectValue(elementId) {
        var el = document.getElementById(elementId);
        return el ? el.options[el.selectedIndex].value : null;
    }

    function InputValue(elementId) {
        var el = document.getElementById(elementId);
        return el ? el.value : '';
    }

    function SetValue(elementId, value) {
        var el = document.getElementById(elementId);
        if (el) { el.value = value; }
    }

    function SetText(elementId, text) {
        var el = document.getElementById(elementId);
        if (el) { el.textContent = text; }
    }

function FormatFileSize(bytes) {
    var size = Number(bytes);
    if (!isFinite(size) || size < 0) { return ''; }
    if (size < 1024) { return Math.round(size) + ' B'; }
    var kilobytes = size / 1024;
    if (kilobytes < 1024) { return kilobytes.toFixed(2) + ' kB'; }
    return (kilobytes / 1024).toFixed(2) + ' MB';
}
}

// Builds the DFM analysis alert list (title/subtitle/level/icon) for a modelInfo,
// localized to the given culture. Order matches the reference design: watertightness,
// manifold edges, multi-body, then dimension sanity checks.
function BuildDfmAlerts(info, culture) {
    var th = culture === 'th';
    var alerts = [];
    if (info.nonWatertight) {
        alerts.push({
            level: 'warn', iconChar: '⚠',
            title: th ? 'ไม่มีไปปิดผิว (Non-watertight mesh)' : 'Non-watertight mesh',
            subtitle: th ? 'อาจทำให้เกิดปัญหาในการพิมพ์' : 'May cause printing issues'
        });
    }
    if (info.nonManifold) {
        alerts.push({
            level: 'danger', iconChar: '⛔',
            title: th ? 'มีขอบที่ไม่เป็นแมนิโฟลด์ (Non-manifold edges)' : 'Non-manifold edges',
            subtitle: th ? 'ควรตรวจสอบความถูกต้องของโมเดล' : 'Check model integrity'
        });
    }
    if (info.bodyCount > 1) {
        alerts.push({
            level: 'warn', iconChar: '⚠',
            title: th ? 'ตรวจพบส่วนที่แยกเป็นชิ้น (Multi-body mesh)' : 'Multi-body mesh',
            subtitle: th ? ('ควรตรวจสอบการเชื่อมต่อของชิ้นงาน (' + info.bodyCount + ' ชิ้น)') : ('Check for unintended merged bodies (' + info.bodyCount + ' bodies)')
        });
    }
    if (info.oddlySmall) {
        alerts.push({
            level: 'warn', iconChar: '⚠',
            title: th ? 'ชิ้นงานมีขนาดเล็กผิดปกติ' : 'Unusually small dimensions',
            subtitle: th ? 'อาจพิมพ์หรือจัดการได้ยาก' : 'May be difficult to print or handle reliably'
        });
    }
    if (info.oddlyLarge) {
        alerts.push({
            level: 'warn', iconChar: '⚠',
            title: th ? 'ชิ้นงานมีขนาดใหญ่ผิดปกติ' : 'Unusually large dimensions',
            subtitle: th ? 'อาจเกินขนาดพื้นที่พิมพ์มาตรฐาน' : 'May exceed our standard build volume and require splitting'
        });
    }
    return alerts;
}
