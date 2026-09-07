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

// Colour sets describe what is actually held in stock in Thailand, per material —
// not what the polymer could theoretically be pigmented as. Offering a colour we
// cannot source turns an instant quote into a phone call, so these stay narrow.
// 'custom' (an arbitrary hex) belongs only to PLA, the one filament we buy in
// enough colours to honour a specific request.
var COLOR_SETS = {
    plaFull: { colors: ['Any', 'Black', 'White', 'Gray', 'Silver', 'Red', 'Orange', 'Yellow', 'Green', 'Blue', 'Purple', 'Pink'], custom: true },
    // PETG is stocked in the transparent grade it is usually chosen for, plus solids.
    petg: { colors: ['Any', 'Black', 'White', 'Gray', 'Clear', 'Red', 'Orange', 'Yellow', 'Green', 'Blue'], custom: false },
    abs: { colors: ['Any', 'Black', 'White', 'Gray', 'Red', 'Yellow', 'Green', 'Blue'], custom: false },
    asa: { colors: ['Any', 'Black', 'White', 'Gray', 'Natural'], custom: false },
    hips: { colors: ['White'], custom: false },
    tpu: { colors: ['Black', 'White'], custom: false },
    // Polycarbonate is held in black and the natural translucent grade only.
    polycarbonate: { colors: ['Black', 'Translucent'], custom: false },
    // Nylon is rarely pigmented; natural and black are what the market carries.
    nylon: { colors: ['Natural', 'Black'], custom: false },
    // Carbon-filled, ESD-safe and flame-retardant grades are manufactured black.
    blackOnly: { colors: ['Black'], custom: false },
    support: { colors: ['Natural'], custom: false },
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

// Material categories, in the order they are offered. The catalogue is 24 materials
// deep, which is an undifferentiated wall without them: grouping by what the customer
// is trying to achieve (a quick prototype, an outdoor part, an ESD-safe fixture) lets
// them skip straight to the two or three candidates that apply. MATERIALS below is kept
// in this same order so each group renders as one contiguous run of cards.
var MATERIAL_GROUPS = [
    { key: 'prototyping', en: 'Rapid prototyping', th: 'งานต้นแบบรวดเร็ว' },
    { key: 'functional', en: 'Functional & outdoor', th: 'งานใช้งานจริงและกลางแจ้ง' },
    { key: 'flexible', en: 'Flexible', th: 'วัสดุยืดหยุ่น' },
    { key: 'engineering', en: 'Engineering plastics', th: 'พลาสติกวิศวกรรม' },
    { key: 'carbon', en: 'Carbon-fiber reinforced', th: 'เสริมคาร์บอนไฟเบอร์' },
    { key: 'esd', en: 'ESD-safe for electronics', th: 'ป้องกันไฟฟ้าสถิตสำหรับงานอิเล็กทรอนิกส์' },
    { key: 'flameRetardant', en: 'Flame-retardant', th: 'หน่วงไฟ' },
    { key: 'resin', en: 'Resin (SLA / DLP)', th: 'เรซิ่น (SLA / DLP)' },
    { key: 'support', en: 'Support material', th: 'วัสดุรองรับ' }
];

function MaterialGroupLabel(groupKey, culture) {
    for (var i = 0; i < MATERIAL_GROUPS.length; i++) {
        if (MATERIAL_GROUPS[i].key === groupKey) {
            return culture === 'th' ? MATERIAL_GROUPS[i].th : MATERIAL_GROUPS[i].en;
        }
    }
    return '';
}

var MATERIALS = [
    { key: 'PLA', name: 'PLA — Polylactic Acid', group: 'prototyping', colors: 'plaFull', desc: 'Easy to print, eco-friendly and dimensionally stable. Best for prototypes, display models and low-stress parts.', descTh: 'พิมพ์ง่าย เป็นมิตรต่อสิ่งแวดล้อม และคงรูปได้ดี เหมาะกับงานต้นแบบ โมเดลจัดแสดง และชิ้นงานที่รับแรงไม่มาก' },
    { key: 'PETG', name: 'PETG — PET Glycol-modified', group: 'prototyping', colors: 'petg', desc: 'Tougher and more heat-resistant than PLA with good layer adhesion. A solid all-round choice for functional parts.', descTh: 'เหนียวและทนความร้อนกว่า PLA ยึดเกาะระหว่างชั้นได้ดี เป็นตัวเลือกรอบด้านสำหรับชิ้นงานใช้งานจริง' },
    { key: 'HIPS', name: 'HIPS — High Impact Polystyrene', group: 'prototyping', colors: 'hips', desc: 'Lightweight and easy to machine or sand; also used as a dissolvable support material for ABS.', descTh: 'น้ำหนักเบา ขัดแต่งและกลึงง่าย ใช้เป็นวัสดุรองรับที่ละลายออกได้สำหรับ ABS ด้วย' },
    { key: 'ABS', name: 'ABS — Acrylonitrile Butadiene Styrene', group: 'functional', colors: 'abs', desc: 'Heat resistant and impact-tough, but shrinks and warps more during printing. Common for enclosures and fixtures.', descTh: 'ทนความร้อนและแรงกระแทกได้ดี แต่หดตัวและบิดงอระหว่างพิมพ์มากกว่า นิยมใช้ทำกล่องครอบและฟิกซ์เจอร์' },
    { key: 'ASA', name: 'ASA — Acrylonitrile Styrene Acrylate', group: 'functional', colors: 'asa', desc: 'Like ABS but UV-stable — holds up outdoors without yellowing or turning brittle.', descTh: 'คุณสมบัติใกล้เคียง ABS แต่ทนรังสียูวี ใช้งานกลางแจ้งได้นานโดยไม่เหลืองหรือกรอบ' },
    { key: 'TPU', name: 'TPU 95A — Flexible Thermoplastic Polyurethane', group: 'flexible', colors: 'tpu', desc: 'Flexible, rubber-like material at 95 Shore A hardness — for gaskets, grips, wearables and parts that need to bend.', descTh: 'ยืดหยุ่นคล้ายยาง ความแข็ง 95 Shore A เหมาะกับปะเก็น ด้ามจับ อุปกรณ์สวมใส่ และชิ้นงานที่ต้องโค้งงอ' },
    { key: 'PC', name: 'PC — Polycarbonate', group: 'engineering', colors: 'polycarbonate', desc: 'High-strength, heat- and impact-resistant engineering plastic for demanding mechanical parts.', descTh: 'แข็งแรงสูง ทนความร้อนและแรงกระแทก เหมาะกับชิ้นส่วนกลไกที่ใช้งานหนัก' },
    { key: 'PA6', name: 'PA6 — Nylon 6', group: 'engineering', colors: 'nylon', desc: 'Tough, wear-resistant nylon with good fatigue resistance — ideal for gears, snap-fits and living hinges.', descTh: 'ไนลอนเหนียว ทนการสึกหรอและความล้า เหมาะกับเฟือง สแนปฟิต และบานพับยืดหยุ่น' },
    { key: 'PA12', name: 'PA12 — Nylon 12', group: 'engineering', colors: 'nylon', desc: 'Low-moisture-absorption nylon with excellent chemical resistance and durability.', descTh: 'ไนลอนที่ดูดความชื้นต่ำ ทนสารเคมีได้ดีเยี่ยม และมีความทนทานสูง' },
    { key: 'PLA-CF', name: 'PLA-CF — PLA + Carbon Fiber', group: 'carbon', colors: 'blackOnly', desc: 'Carbon-fiber-reinforced PLA — stiffer and lighter than standard PLA, with a matte finish.', descTh: 'PLA เสริมคาร์บอนไฟเบอร์ แข็งกว่าและเบากว่า PLA ทั่วไป ให้ผิวด้าน' },
    { key: 'PETG-CF', name: 'PETG-CF — PETG + Carbon Fiber', group: 'carbon', colors: 'blackOnly', desc: 'Carbon-fiber-reinforced PETG for extra stiffness in functional, load-bearing parts.', descTh: 'PETG เสริมคาร์บอนไฟเบอร์ เพิ่มความแข็งให้ชิ้นงานใช้งานจริงที่ต้องรับแรง' },
    { key: 'PET-CF', name: 'PET-CF — PET + Carbon Fiber', group: 'carbon', colors: 'blackOnly', desc: 'High-stiffness, low-warp carbon-fiber composite suited to jigs, fixtures and tooling.', descTh: 'คอมโพสิตคาร์บอนไฟเบอร์ที่แข็งสูงและบิดงอน้อย เหมาะกับจิ๊ก ฟิกซ์เจอร์ และเครื่องมือช่วยผลิต' },
    { key: 'PA-CF', name: 'PA-CF — Nylon + Carbon Fiber', group: 'carbon', colors: 'blackOnly', desc: 'Carbon-fiber nylon — very high strength-to-weight ratio for demanding mechanical applications.', descTh: 'ไนลอนเสริมคาร์บอนไฟเบอร์ อัตราส่วนความแข็งแรงต่อน้ำหนักสูงมาก เหมาะกับงานกลไกที่รับแรงมาก' },
    { key: 'ASA-CF', name: 'ASA-CF — ASA + Carbon Fiber', group: 'carbon', colors: 'blackOnly', desc: 'Carbon-fiber ASA — UV-stable and rigid, suited to outdoor structural parts.', descTh: 'ASA เสริมคาร์บอนไฟเบอร์ แข็งและทนรังสียูวี เหมาะกับชิ้นส่วนโครงสร้างที่ใช้งานกลางแจ้ง' },
    { key: 'PETG-ESD', name: 'PETG-ESD — ESD-Safe PETG', group: 'esd', colors: 'blackOnly', desc: 'Static-dissipative PETG for parts that handle or house sensitive electronics.', descTh: 'PETG ที่สลายประจุไฟฟ้าสถิต เหมาะกับชิ้นงานที่ต้องจับหรือใส่อุปกรณ์อิเล็กทรอนิกส์ที่ไวต่อไฟฟ้าสถิต' },
    { key: 'PC-ESD', name: 'PC-ESD — ESD-Safe Polycarbonate', group: 'esd', colors: 'blackOnly', desc: 'Static-dissipative polycarbonate for high-strength ESD-safe enclosures and fixtures.', descTh: 'โพลีคาร์บอเนตที่สลายประจุไฟฟ้าสถิต สำหรับกล่องครอบและฟิกซ์เจอร์ที่ต้องแข็งแรงสูง' },
    { key: 'ABS-FR', name: 'ABS-FR — Flame-Retardant ABS', group: 'flameRetardant', colors: 'blackOnly', desc: 'Flame-retardant ABS for enclosures with fire-safety requirements.', descTh: 'ABS หน่วงไฟ สำหรับกล่องครอบที่ต้องผ่านข้อกำหนดด้านความปลอดภัยจากอัคคีภัย' },
    { key: 'PC-FR', name: 'PC-FR — Flame-Retardant Polycarbonate', group: 'flameRetardant', colors: 'blackOnly', desc: 'Flame-retardant polycarbonate for enclosures and components with fire-safety requirements.', descTh: 'โพลีคาร์บอเนตหน่วงไฟ สำหรับกล่องครอบและชิ้นส่วนที่ต้องผ่านข้อกำหนดด้านความปลอดภัยจากอัคคีภัย' },
    { key: 'M68', name: 'Standard Resin', group: 'resin', colors: 'resinStandard', desc: 'Smooth, highly detailed finish. Great for miniatures, visual prototypes and presentation models.', descTh: 'ผิวเรียบ เก็บรายละเอียดได้คมชัด เหมาะกับโมเดลขนาดเล็ก ต้นแบบเพื่อความสวยงาม และงานนำเสนอ' },
    { key: 'K', name: 'Tough Resin', group: 'resin', colors: 'resinTough', desc: 'Higher impact and flex resistance than standard resin, for functional snap-fit and mechanical parts.', descTh: 'ทนแรงกระแทกและการงอได้ดีกว่าเรซิ่นมาตรฐาน เหมาะกับสแนปฟิตและชิ้นส่วนกลไก' },
    { key: 'G217', name: 'Transparent Resin', group: 'resin', colors: 'resinClear', desc: 'Optically clear resin for lenses, light guides and see-through prototypes.', descTh: 'เรซิ่นใสสำหรับเลนส์ ตัวนำแสง และต้นแบบที่ต้องมองทะลุได้' },
    { key: 'F80', name: 'Flexible Resin', group: 'resin', colors: 'resinFlexible', desc: 'Soft, rubber-like resin for gaskets, seals and flexible details.', descTh: 'เรซิ่นนุ่มคล้ายยาง เหมาะกับปะเก็น ซีล และรายละเอียดที่ต้องการความยืดหยุ่น' },
    { key: 'CASTWAX', name: 'Castable Wax Resin', group: 'resin', colors: 'resinCastable', desc: 'Burns out cleanly for jewelry and investment-casting workflows.', descTh: 'เผาไหม้หมดจดไม่เหลือเถ้า เหมาะกับงานเครื่องประดับและการหล่อแบบขี้ผึ้งหาย' },
    { key: 'PVA', name: 'PVA — Water-Soluble Support', group: 'support', colors: 'support', desc: 'Water-soluble support material that dissolves away cleanly — ideal for complex overhangs and internal channels.', descTh: 'วัสดุรองรับที่ละลายน้ำได้ ล้างออกสะอาด เหมาะกับชิ้นงานที่มีส่วนยื่นหรือช่องภายในซับซ้อน' }
];

// Extensions three.js can load directly, plus CAD formats handled through occt.
var SUPPORTED_EXTENSIONS = ['stl', 'obj', '3mf', 'glb', 'gltf', 'stp', 'step', 'igs', 'iges'];

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

// Arrow-key navigation shared by the material and colour radio groups. Moves focus to
// the next or previous sibling option, wrapping at both ends, and leaves selection to
// Space/Enter so arrowing past an option does not silently reprice the quote.
function MoveRadioFocus(event, current, selector) {
    var forward = event.key === 'ArrowRight' || event.key === 'ArrowDown';
    var backward = event.key === 'ArrowLeft' || event.key === 'ArrowUp';
    if (!forward && !backward) { return; }

    var container = current.parentElement;
    if (!container) { return; }
    var options = Array.prototype.slice.call(container.querySelectorAll(selector));
    if (options.length < 2) { return; }

    var index = options.indexOf(current);
    if (index === -1) { return; }

    event.preventDefault();
    var next = options[(index + (forward ? 1 : -1) + options.length) % options.length];
    options.forEach(function (option) { option.tabIndex = option === next ? 0 : -1; });
    next.focus();
}

// Builds the material selection cards (as real DOM nodes, filtered by an optional
// search term matched against the material name and description — immediate,
// case-insensitive substring).
function BuildMaterialCardsFragment(selectedKey, filterText, culture, limit, materialPriceState, materialPrices, currency) {
    var needle = (filterText || '').trim().toLowerCase();
    var frag = document.createDocumentFragment();
    var matched = 0;
    var renderedGroup = null;

    // Two passes. The first decides which materials survive the search and the preview
    // limit; only then can a category heading know whether it has any cards under it,
    // so a filtered list never shows an empty group.
    var visible = [];
    for (var i = 0; i < MATERIALS.length; i++) {
        var candidate = MATERIALS[i];
        if (needle
            && candidate.name.toLowerCase().indexOf(needle) === -1
            && candidate.desc.toLowerCase().indexOf(needle) === -1
            && candidate.descTh.toLowerCase().indexOf(needle) === -1
            && MaterialGroupLabel(candidate.group, culture).toLowerCase().indexOf(needle) === -1) {
            continue;
        }
        // A limit of 0 means no limit. The selected material is always rendered, so
        // collapsing the list can never hide the customer's own choice.
        if (limit && matched >= limit && candidate.key !== selectedKey) {
            continue;
        }
        matched++;
        visible.push(candidate);
    }

    for (var v = 0; v < visible.length; v++) {
        var m = visible[v];
        // Headings are siblings of the cards rather than wrappers around them: MoveRadioFocus
        // resolves its option set from a card's parentElement, so wrapping each category in
        // its own element would stop arrow-key navigation at every category boundary.
        if (m.group !== renderedGroup) {
            renderedGroup = m.group;
            var heading = CreateEl('div', 'iq-mat-cat', MaterialGroupLabel(m.group, culture));
            heading.setAttribute('role', 'presentation');
            frag.appendChild(heading);
        }
        var card = CreateEl('div', 'iq-mat-card' + (m.key === selectedKey ? ' sel' : ''));
        card.dataset.key = m.key;
        // Material is one of the two decisions this page exists to capture, so it has to be
        // operable without a mouse. These cannot be <button> elements because each one holds
        // a nested info button, and nesting buttons is invalid; the radio pattern gives the
        // same keyboard contract and communicates "pick exactly one" more accurately.
        card.setAttribute('role', 'radio');
        card.setAttribute('aria-checked', m.key === selectedKey ? 'true' : 'false');
        // The category heading is decorative, so the card's own label has to carry the
        // category or a screen-reader user loses the grouping entirely.
        card.setAttribute('aria-label', m.name + ' — ' + MaterialGroupLabel(m.group, culture));
        // Roving tabindex: one tab stop for the whole group, arrows move within it, so a
        // keyboard user does not have to tab through two dozen materials to reach the rest
        // of the form.
        card.tabIndex = m.key === selectedKey ? 0 : -1;
        card.addEventListener('click', (function (key) { return function () { SelectMaterialCard(key); }; })(m.key));
        card.addEventListener('keydown', (function (key) {
            return function (event) {
                if (event.key === ' ' || event.key === 'Enter' || event.key === 'Spacebar') {
                    event.preventDefault();
                    SelectMaterialCard(key);
                    return;
                }
                MoveRadioFocus(event, this, '.iq-mat-card');
            };
        })(m.key));
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

        var detailRow = CreateEl('div', 'iq-mat-detail-row');
        detailRow.appendChild(CreateEl('div', 'full', parts[1] || ''));
        var price = CreateEl('div', 'iq-mat-price');
        price.dataset.materialPrice = m.key;
        if (materialPriceState === 'loading') {
            price.classList.add('is-loading');
            price.textContent = '…';
            price.setAttribute('aria-label', culture === 'th' ? 'กำลังโหลดราคา' : 'Loading price');
        } else if (materialPriceState === 'ready' && materialPrices && materialPrices[m.key] != null) {
            price.textContent = Number(materialPrices[m.key]).toLocaleString(
                'en-US',
                { minimumFractionDigits: 2, maximumFractionDigits: 2 })
                + ' ' + (culture === 'th' ? currency + '/ชิ้น' : currency + '/pc');
        } else {
            price.textContent = '—';
            price.setAttribute('aria-label', culture === 'th' ? 'ราคาไม่พร้อมใช้งาน' : 'Price unavailable');
        }
        detailRow.appendChild(price);
        card.appendChild(detailRow);
        frag.appendChild(card);
    }
    if (matched === 0) {
        frag.appendChild(CreateEl('div', 'iq-mat-empty text-muted small', culture === 'th' ? 'ไม่พบวัสดุที่ตรงกับคำค้นหา' : 'No materials match your search.'));
    }
    return frag;
}

// Rebuilds a <select>'s <option> list for the given material's colour set.
function ColorLabel(value, culture) {
    var labels = { Any: 'ไม่ระบุสี', Black: 'ดำ', White: 'ขาว', Gray: 'เทา', Silver: 'เงิน', Red: 'แดง', Orange: 'ส้ม', Yellow: 'เหลือง', Green: 'เขียว', Blue: 'น้ำเงิน', Purple: 'ม่วง', Pink: 'ชมพู', Natural: 'สีธรรมชาติ', Clear: 'ใส', Translucent: 'โปร่งแสง' };
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
        // Colour is carried by a swatch's fill alone, so the name has to reach anyone who
        // cannot see it. The parallel <select> stays as the redundant path.
        swatch.setAttribute('role', 'radio');
        swatch.setAttribute('aria-checked', sel ? 'true' : 'false');
        swatch.setAttribute('aria-label', ColorLabel(value, culture));
        swatch.tabIndex = sel ? 0 : -1;
        swatch.addEventListener('click', (function (v) { return function () { SelectColorSwatch(v); }; })(value));
        swatch.addEventListener('keydown', (function (v) {
            return function (event) {
                if (event.key === ' ' || event.key === 'Enter' || event.key === 'Spacebar') {
                    event.preventDefault();
                    SelectColorSwatch(v);
                    return;
                }
                MoveRadioFocus(event, this, '.iq-swatch');
            };
        })(value));
        nodes.push(swatch);
    }
    if (colorSet.custom) {
        var custom = CreateEl('div', 'iq-swatch iq-swatch-custom' + (isCustom ? ' sel' : ''));
        var customIcon = CreateEl('i', 'fas fa-eye-dropper');
        customIcon.setAttribute('aria-hidden', 'true');
        custom.appendChild(customIcon);
        custom.title = culture === 'th' ? 'กำหนดสีเอง' : 'Custom color';
        custom.style.background = isCustom ? selectedValue : 'conic-gradient(red,#e8c93a,lime,cyan,blue,magenta,red)';
        custom.setAttribute('role', 'radio');
        custom.setAttribute('aria-checked', isCustom ? 'true' : 'false');
        custom.setAttribute('aria-label', custom.title);
        custom.tabIndex = isCustom ? 0 : -1;
        custom.addEventListener('click', function () { OpenCustomColorPicker(); });
        custom.addEventListener('keydown', function (event) {
            if (event.key === ' ' || event.key === 'Enter' || event.key === 'Spacebar') {
                event.preventDefault();
                OpenCustomColorPicker();
                return;
            }
            MoveRadioFocus(event, this, '.iq-swatch');
        });
        nodes.push(custom);
    }
    container.replaceChildren.apply(container, nodes);
}

// ---------------------------------------------------------------------------
// Background workers -- parsing and analysis deliberately use independent queues. A long CNC
// accessibility solve must never sit in front of another file that only needs decoding so its
// body can be shown on the canvas.
// ---------------------------------------------------------------------------

var WORKER_TIMEOUT_MS = 120000;
var MAX_CONSECUTIVE_WORKER_LOAD_FAILURES = 3;

function CreateModelWorkerManager(maxConcurrency) {
    maxConcurrency = Math.max(1, Number(maxConcurrency) || 1);
    var slots = [];
    for (var slotIndex = 0; slotIndex < maxConcurrency; slotIndex++) {
        slots.push({ worker: null, activeJobId: null });
    }
    var pending = new Map(); // jobId -> { resolve, reject, timeoutHandle }
    var queue = []; // messages not yet posted
    var nextJobId = 1;
    var consecutiveLoadFailures = 0;
    var permanentlyFailed = false;

    function CreateWorker(slot) {
        // A same-origin worker takes its CSP from its own HTTP response, not from this
        // document, and that response is edge- and browser-cached with the header baked in.
        // The server hands us a per-build versioned URL so the worker's policy can never lag
        // the deployed one; the literal path is only a fallback for pages that don't inject it.
        var workerUrl = window.malievModelWorkerUrl || '/src/app/js/model-viewer/model-viewer.worker.js';
        var w = new Worker(workerUrl);
        w.onmessage = function (event) {
            consecutiveLoadFailures = 0;
            HandleResult(slot, event.data);
        };
        w.onerror = function () {
            // A script-level error (e.g. importScripts failed) rather than a per-job failure.
            var failedJobId = slot.activeJobId;
            consecutiveLoadFailures++;
            if (consecutiveLoadFailures >= MAX_CONSECUTIVE_WORKER_LOAD_FAILURES) {
                // The worker script itself is broken (bad deploy, missing dependency) --
                // respawning again would just fail identically forever. Stop trying and fail
                // every job (queued and future) with a clear message instead of looping.
                permanentlyFailed = true;
                var activeJobIds = slots.map(function (entry) { return entry.activeJobId; })
                    .filter(function (jobId) { return jobId !== null; });
                slots.forEach(function (entry) {
                    if (entry.worker) { entry.worker.terminate(); }
                    entry.worker = null;
                    entry.activeJobId = null;
                });
                activeJobIds.forEach(function (jobId) {
                    Settle(jobId, { success: false, error: 'Unable to load 3D file processing support. Please reload the page.' });
                });
                FailAllQueued('Unable to load 3D file processing support. Please reload the page.');
                return;
            }
            // Respawn so the worker stays usable for the next job.
            RespawnWorker(slot);
            if (failedJobId !== null) {
                Settle(failedJobId, { success: false, error: 'Unable to load 3D file processing support.' });
            }
        };
        return w;
    }

    function RespawnWorker(slot) {
        if (slot.worker) {
            slot.worker.terminate();
        }
        slot.worker = permanentlyFailed ? null : CreateWorker(slot);
        slot.activeJobId = null;
        DispatchNext();
    }

    function FailAllQueued(message) {
        var queued = queue;
        queue = [];
        queued.forEach(function (job) { Settle(job.message.jobId, { success: false, error: message }); });
    }

    function DispatchNext() {
        if (permanentlyFailed || queue.length === 0) { return; }
        slots.forEach(function (slot) {
            if (queue.length === 0 || slot.activeJobId !== null) { return; }
            var job = queue.shift();
            slot.activeJobId = job.message.jobId;
            if (!slot.worker) { slot.worker = CreateWorker(slot); }
            var entry = pending.get(slot.activeJobId);
            var dispatchedJobId = slot.activeJobId;
            entry.timeoutHandle = setTimeout(function () {
                if (slot.activeJobId !== dispatchedJobId) { return; }
                RespawnWorker(slot);
                Settle(dispatchedJobId, { success: false, error: 'This file is too complex to process. Please try a simpler or smaller model.' });
            }, WORKER_TIMEOUT_MS);
            slot.worker.postMessage(job.message, job.transfer || []);
        });
    }

    function HandleResult(slot, data) {
        if (data.jobId !== slot.activeJobId) {
            return; // stale response from a since-respawned worker; ignore
        }
        slot.activeJobId = null;
        Settle(data.jobId, data);
        DispatchNext();
    }

    function Settle(jobId, data) {
        var entry = pending.get(jobId);
        if (!entry) {
            return;
        }
        pending.delete(jobId);
        clearTimeout(entry.timeoutHandle);
        if (data.success) {
            entry.resolve(data);
        } else {
            entry.reject(new Error(data.error || 'Unable to read this model file.'));
        }
    }

    return {
        // Submits a parse/analyze job. `transfer` is the list of Transferable objects (e.g. the
        // ArrayBuffer/typed-array buffers) to move into the worker without copying. Returns
        // { jobId, promise } -- the caller keeps jobId to Cancel(...) this job later (e.g. if the
        // user removes the part before it finishes); the promise resolves/rejects as usual.
        Submit: function (message, transfer) {
            var jobId = 'job-' + (nextJobId++);
            message.jobId = jobId;
            var promise = new Promise(function (resolve, reject) {
                if (permanentlyFailed) {
                    reject(new Error('Unable to load 3D file processing support. Please reload the page.'));
                    return;
                }
                pending.set(jobId, { resolve: resolve, reject: reject, timeoutHandle: null });
                queue.push({ message: message, transfer: transfer });
                DispatchNext();
            });
            return { jobId: jobId, promise: promise };
        },
        // Cancels a job that hasn't produced a result yet -- drops it from the queue if it
        // hasn't started, or terminates/respawns the worker if it's the active job. Safe to call
        // with a jobId that already settled (no-op, since Settle no-ops on an unknown jobId).
        Cancel: function (jobId) {
            var queueIndex = -1;
            for (var i = 0; i < queue.length; i++) {
                if (queue[i].message.jobId === jobId) { queueIndex = i; break; }
            }
            if (queueIndex !== -1) {
                queue.splice(queueIndex, 1);
                Settle(jobId, { success: false, error: 'Cancelled.' });
                return;
            }
            var activeSlot = slots.find(function (slot) { return slot.activeJobId === jobId; });
            if (!activeSlot) { return; }
            RespawnWorker(activeSlot);
            Settle(jobId, { success: false, error: 'Cancelled.' });
        }
    };
}

// Four complete part pipelines are admitted by the quotation page. Matching worker pools
// let those admitted parts actually progress independently instead of serialising behind a
// single parser or accessibility solve.
var ModelParseWorkerManager = CreateModelWorkerManager(4);
var ModelAnalysisWorkerManager = CreateModelWorkerManager(4);
var ModelOverlayWorkerManager = CreateModelWorkerManager(1);
// Retained for diagnostic/browser compatibility; new work must choose a lane explicitly.
var ModelWorkerManager = ModelParseWorkerManager;

// Builds a displayable THREE.Group from the position/index buffers a worker job returned
// (replaces the old OcctResultToGroup, now used for every worker-parsed format uniformly).
function BuildGroupFromMeshBuffers(meshes) {
    var group = new THREE.Group();
    if (!meshes) {
        return group;
    }

    var material = DefaultMaterial();
    for (var i = 0; i < meshes.length; i++) {
        var m = meshes[i];
        if (!m.position) {
            continue;
        }

        var geometry = new THREE.BufferGeometry();
        geometry.setAttribute('position', new THREE.Float32BufferAttribute(m.position, 3));
        if (m.normal) {
            geometry.setAttribute('normal', new THREE.Float32BufferAttribute(m.normal, 3));
        }
        if (m.index) {
            geometry.setIndex(new THREE.Uint32BufferAttribute(m.index, 1));
        }
        if (!geometry.attributes.normal) {
            geometry.computeVertexNormals();
        }
        geometry.userData.cadFaceRanges = Array.isArray(m.cadFaceRanges)
            ? m.cadFaceRanges.map(function (face) { return { first: face.first, last: face.last }; })
            : [];
        group.add(new THREE.Mesh(geometry, material));
    }

    return group;
}

// Materialising worker-returned buffers still allocates Three.js objects on the UI
// thread. Split that work into frame-budgeted batches so a large assembly cannot
// monopolise input and paint while its meshes are attached to the scene.
function BuildGroupFromMeshBuffersAsync(meshes, options) {
    meshes = Array.isArray(meshes) ? meshes : [];
    options = options || {};
    var frameBudgetMs = Number.isFinite(Number(options.frameBudgetMs))
        ? Math.max(0, Number(options.frameBudgetMs)) : 8;
    var group = new THREE.Group();
    var material = DefaultMaterial();
    var index = 0;

    function yieldToBrowser() {
        return new Promise(function (resolve) { setTimeout(resolve, 0); });
    }

    return new Promise(function (resolve, reject) {
        function buildBatch() {
            try {
                var started = performance.now();
                while (index < meshes.length) {
                    var meshBuffer = meshes[index++];
                    if (meshBuffer && meshBuffer.position) {
                        var geometry = new THREE.BufferGeometry();
                        geometry.setAttribute('position', new THREE.Float32BufferAttribute(meshBuffer.position, 3));
                        if (meshBuffer.normal) {
                            geometry.setAttribute('normal', new THREE.Float32BufferAttribute(meshBuffer.normal, 3));
                        }
                        if (meshBuffer.index) {
                            geometry.setIndex(new THREE.Uint32BufferAttribute(meshBuffer.index, 1));
                        }
                        if (!geometry.attributes.normal) { geometry.computeVertexNormals(); }
                        geometry.userData.cadFaceRanges = Array.isArray(meshBuffer.cadFaceRanges)
                            ? meshBuffer.cadFaceRanges.map(function (face) { return { first: face.first, last: face.last }; })
                            : [];
                        group.add(new THREE.Mesh(geometry, material));
                    }
                    if (performance.now() - started >= frameBudgetMs && index < meshes.length) {
                        yieldToBrowser().then(buildBatch, reject);
                        return;
                    }
                }
                resolve(group);
            } catch (error) { reject(error); }
        }
        buildBatch();
    });
}

var MULTI_BODY_PREVIEW_COLORS = [0xb9c3ca, 0x2f6da8, 0xd69d16, 0xc95b2b, 0x4d8c58, 0x7151a8, 0x9a5b62];

function CreatePreviewMaterial(color) {
    // CAD-style, non-metallic materials retain detail against the light canvas.
    return new THREE.MeshStandardMaterial({ color: color, metalness: 0.04, roughness: 0.64, flatShading: false, side: THREE.DoubleSide });
}

function DefaultMaterial() {
    return CreatePreviewMaterial(0xb9c3ca);
}

// Some format loaders, notably ThreeMFLoader, provide positions without a normal
// attribute because their default flat-shaded materials derive face normals in the
// shader. The quotation preview replaces those materials with smooth StandardMaterials
// for multi-body colouring, so make the geometry lighting-safe before that replacement.
//
// 3MF is an indexed format -- a cube arrives as 8 shared vertices -- and computing
// normals on shared vertices averages every adjoining face normal together, which
// renders the part as a smooth blob. Every other supported format arrives as triangle
// soup and therefore shades flat. Expanding to non-indexed first keeps the faceted
// preview identical across formats, which is what a manufacturing preview must show:
// the tessellation being quoted, not an idealised surface.
//
// Safe to run here: SubmitObjectForAnalysis already copied the indexed position/index
// buffers for DFM analysis before this callback runs, so topology checks are unaffected.
function EnsurePreviewGeometryNormals(object3D) {
    if (!object3D) { return; }
    object3D.traverse(function (child) {
        if (!child.isMesh || !child.geometry || !child.geometry.attributes || !child.geometry.attributes.position) {
            return;
        }
        if (child.geometry.attributes.normal) {
            return;
        }
        if (child.geometry.index && typeof child.geometry.toNonIndexed === 'function') {
            // Not disposed: 3MF components can share one geometry across several meshes.
            child.geometry = child.geometry.toNonIndexed();
        }
        if (typeof child.geometry.computeVertexNormals === 'function') {
            child.geometry.computeVertexNormals();
        }
    });
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
    var spaceMouseNavigation = null;
    var cncStockOverlay = null;
    var cncDirectionOverlay = null;
    var cncReachOverlay = null;
    var cncResidualOverlay = null;
    var cncOverlayPlan = null;
    var cncOverlayPartId = null;
    var cncOverlayStateByPart = Object.create(null);
    var cncStockMetadata = null;
    var cncSetupLabelCount = 0;
    var cncVisibleSetupIds = [];
    var cncHighlightedClusterCount = 0;
    var cncSetupSelectionAvailable = false;
    var cncResidualGeometryAvailable = false;
    var cncResidualMode = 'unavailable';
    var cncSurfaceHighlightMode = 'unavailable';
    var cncOverlayTriangleCount = 0;
    var cncModelTriangleCount = 0;
    var cncRenderedHighlightedTriangleCount = 0;
    var cncRenderedDimmedTriangleCount = 0;
    var cncRenderedResidualTriangleCount = 0;
    var cncResidualMeshCount = 0;
    var cncResidualConnectedComponentCount = 0;
    var cncResidualWorldBounds = null;
    var cncOverlaySampleCount = 0;
    var cncRenderedHighlightedSampleCount = 0;
    var cncRenderedDimmedSampleCount = 0;
    var cncOverlayFieldChecksum = null;
    var cncOverlayFieldOrigin = null;
    var cncOverlayDrawCallCount = 0;
    var cncFieldClassificationMode = 'none';
    var cncFieldSamplingMode = 'none';
    var cncCadEdgeMode = 'none';
    var cncCadEdgeDrawCallCount = 0;
    var cncFieldSpatialMapCache = null;
    var cncOverlayUnavailableReason = null;
    var cncOverlayWorkload = { triangleCount: 0, clusterCount: 0, triangleClusterComparisons: 0 };
    var cncOverlayBuildGeneration = 0;
    var cncOverlayBuildPending = false;
    var cncOverlayWorkerUsed = false;
    var cncOverlayBuildFrameCount = 0;
    var activeCncOverlayJobId = null;
    var CNC_STOCK_OPACITY = 0.20;
    var CNC_FLUTE_CONTACT_ALIGNMENT = 0.35;
    var overlayThreadScale = Number(navigator.hardwareConcurrency) >= 8 ? 2
        : (Number(navigator.hardwareConcurrency) >= 4 ? 1.5 : 1);
    var overlayMemoryScale = Number(navigator.deviceMemory) >= 8 ? 2
        : (Number(navigator.deviceMemory) >= 4 ? 1.5 : 1);
    var overlayCapacityScale = Math.min(overlayThreadScale, overlayMemoryScale);
    var CNC_OVERLAY_LIMITS = {
        // Trusted B-rep CAD is evaluated off the UI thread. Modeled threads can legitimately
        // produce a dense tessellation without making the source topology unreliable.
        maxTriangles: Math.round(2000000 * overlayCapacityScale),
        maxTrustedClusters: Math.round(4096 * overlayCapacityScale),
        maxTriangleClusterComparisons: Math.round(48000000 * overlayCapacityScale),
        maxSynchronousTriangles: 100000,
        maxSynchronousClusters: 256,
        maxSynchronousComparisons: 4000000
    };
    var CNC_MESH_DERIVED_FACE_FLOOR = 4096;

    var scene = new THREE.Scene();

    function IsCadModelInfo(modelInfo) {
        if (!modelInfo) { return false; }
        if (modelInfo.isCadSource === true) { return true; }
        var format = String(modelInfo.sourceFormat || '').toLowerCase();
        return format === 'step' || format === 'stp' || format === 'iges' || format === 'igs';
    }

    function CncCadTopologyProfile(object3D) {
        var meshCount = 0;
        var triangleCount = 0;
        var faceCount = 0;
        var hasCompleteFaceRanges = true;
        if (!object3D || typeof object3D.traverse !== 'function') {
            return { hasTrustedBrepFaces: false, meshDerived: false, triangleCount: 0, faceCount: 0 };
        }
        object3D.traverse(function (child) {
            if (!child.isMesh || !child.geometry || !child.geometry.attributes
                || !child.geometry.attributes.position) { return; }
            meshCount += 1;
            var count = child.geometry.index
                ? child.geometry.index.count : child.geometry.attributes.position.count;
            triangleCount += Math.floor(count / 3);
            var ranges = child.geometry.userData && child.geometry.userData.cadFaceRanges;
            if (!Array.isArray(ranges) || ranges.length === 0) {
                hasCompleteFaceRanges = false;
                return;
            }
            faceCount += ranges.length;
        });
        // STEP files produced by wrapping a triangle mesh commonly contain one nominal CAD
        // face per source triangle. A high face-to-triangle ratio at this scale is materially
        // different from a genuine B-rep whose curved faces merely need fine tessellation.
        var meshDerived = hasCompleteFaceRanges && triangleCount > 0
            && faceCount >= CNC_MESH_DERIVED_FACE_FLOOR
            && faceCount / triangleCount >= 0.8;
        return {
            hasTrustedBrepFaces: hasCompleteFaceRanges && meshCount > 0 && faceCount > 0 && !meshDerived,
            meshDerived: meshDerived,
            triangleCount: triangleCount,
            faceCount: faceCount
        };
    }

    function CreateCadFaceBoundaryGeometry(geometry) {
        var ranges = geometry && geometry.userData && geometry.userData.cadFaceRanges;
        var position = geometry && geometry.getAttribute && geometry.getAttribute('position');
        var index = geometry && geometry.index;
        if (!Array.isArray(ranges) || ranges.length === 0 || !position || !index) { return null; }

        var bounds = new THREE.Box3().setFromBufferAttribute(position);
        var grid = Math.max(bounds.getSize(new THREE.Vector3()).length() * 1e-7, 1e-7);
        var segments = [];
        var emitted = new Set();

        function coordinateKey(vertexIndex) {
            return Math.round(position.getX(vertexIndex) / grid) + '_'
                + Math.round(position.getY(vertexIndex) / grid) + '_'
                + Math.round(position.getZ(vertexIndex) / grid);
        }

        ranges.forEach(function (range) {
            var localEdges = new Map();
            function addLocalEdge(a, b) {
                if (a === b) { return; }
                var key = a < b ? a + '|' + b : b + '|' + a;
                var record = localEdges.get(key);
                if (record) { record.count += 1; }
                else { localEdges.set(key, { a: a, b: b, count: 1 }); }
            }

            var first = Math.max(0, Number(range.first) || 0);
            var last = Math.min(Math.floor(index.count / 3) - 1, Number(range.last));
            for (var triangle = first; triangle <= last; triangle += 1) {
                var offset = triangle * 3;
                var a = index.getX(offset);
                var b = index.getX(offset + 1);
                var c = index.getX(offset + 2);
                addLocalEdge(a, b);
                addLocalEdge(b, c);
                addLocalEdge(c, a);
            }

            localEdges.forEach(function (edge) {
                if (edge.count !== 1) { return; }
                var aKey = coordinateKey(edge.a);
                var bKey = coordinateKey(edge.b);
                var segmentKey = aKey < bKey ? aKey + '|' + bKey : bKey + '|' + aKey;
                if (emitted.has(segmentKey)) { return; }
                emitted.add(segmentKey);
                segments.push(
                    position.getX(edge.a), position.getY(edge.a), position.getZ(edge.a),
                    position.getX(edge.b), position.getY(edge.b), position.getZ(edge.b));
            });
        });

        if (segments.length === 0) { return null; }
        var edgeGeometry = new THREE.BufferGeometry();
        edgeGeometry.setAttribute('position', new THREE.Float32BufferAttribute(segments, 3));
        return edgeGeometry;
    }

    function CreateCadFeatureEdges(geometry, cachedEdgeGeometry) {
        if (!geometry || typeof THREE.EdgesGeometry !== 'function') { return null; }
        var hasCadFaceRanges = geometry.userData && Array.isArray(geometry.userData.cadFaceRanges)
            && geometry.userData.cadFaceRanges.length > 0;
        var edgeGeometry = cachedEdgeGeometry && cachedEdgeGeometry.clone
            ? cachedEdgeGeometry.clone() : CreateCadFaceBoundaryGeometry(geometry);
        var edgeSource = hasCadFaceRanges ? 'brep-face-boundaries' : 'feature-angle-fallback';
        if (!edgeGeometry) { edgeGeometry = new THREE.EdgesGeometry(geometry, 15); }
        var position = edgeGeometry.getAttribute('position');
        if (!position || position.count === 0) {
            edgeGeometry.dispose();
            return null;
        }
        var line = new THREE.LineSegments(edgeGeometry, new THREE.LineBasicMaterial({
            color: 0x172033,
            transparent: true,
            opacity: 0.86,
            depthTest: true,
            depthWrite: false
        }));
        line.renderOrder = 6;
        line.userData.cncCadFeatureEdges = true;
        line.userData.cncCadEdgeSource = edgeSource;
        return line;
    }

    function AddCadPreviewEdges(object3D, modelInfo) {
        if (!object3D || !IsCadModelInfo(modelInfo)) { return; }
        var meshes = [];
        object3D.traverse(function (child) {
            if (child.isMesh && child.geometry) { meshes.push(child); }
        });
        meshes.forEach(function (mesh) {
            if (mesh.children.some(function (child) { return child.userData && child.userData.cncCadFeatureEdges; })) {
                return;
            }
            var edges = CreateCadFeatureEdges(mesh.geometry);
            if (edges) {
                var materials = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
                materials.forEach(function (material) {
                    if (!material) { return; }
                    // Recess the shaded CAD faces slightly in depth so their coplanar
                    // B-rep boundary lines remain crisp instead of flickering or vanishing.
                    material.polygonOffset = true;
                    material.polygonOffsetFactor = 1;
                    material.polygonOffsetUnits = 1;
                    material.needsUpdate = true;
                });
                mesh.add(edges);
            }
        });
    }

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
        UpdateCncSetupLabelScales();
        renderer.render(scene, camera);
    };
    render();

    window.addEventListener('resize', SizeRenderer, false);
    // The viewer pane can change size after the window resize event has fired
    // (flex layout, scrollbars, orientation changes, and the responsive summary
    // rail can all settle on a later frame). Observe the actual stage so the
    // WebGL drawing buffer and camera follow the rendered canvas dimensions.
    var resizeObserver = null;
    if (typeof window.ResizeObserver === 'function' && canvasElement.parentElement) {
        resizeObserver = new window.ResizeObserver(function () { SizeRenderer(); });
        resizeObserver.observe(canvasElement.parentElement);
    }

    // Sizing the buffer is not enough on its own. The camera distance was chosen to
    // frame the part at the aspect ratio in force when it loaded, so once the aspect
    // changes -- a phone turned on its side is the extreme case -- that framing is
    // wrong and the part sits cropped or marooned. Re-frame after the aspect has
    // actually moved, debounced because resize fires continuously while dragging and
    // orientation settles over several frames.
    var lastFramedAspect = null;
    var refitTimer = null;
    function RefitForAspect() {
        if (refitTimer) { clearTimeout(refitTimer); }
        refitTimer = setTimeout(function () {
            refitTimer = null;
            SizeRenderer();
            if (!displayObject) { return; }
            var aspect = camera.aspect;
            if (!aspect || !isFinite(aspect)) { return; }
            // Ignore sub-pixel churn; only a real change of proportion needs a re-frame.
            if (lastFramedAspect !== null && Math.abs(aspect - lastFramedAspect) < 0.02) { return; }
            lastFramedAspect = aspect;
            RecenterAndFrame();
        }, 180);
    }
    window.addEventListener('resize', RefitForAspect, false);
    window.addEventListener('orientationchange', RefitForAspect, false);
    if (resizeObserver) {
        resizeObserver.disconnect();
        resizeObserver = new window.ResizeObserver(function () { SizeRenderer(); RefitForAspect(); });
        resizeObserver.observe(canvasElement.parentElement);
    }

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

    // Parses a model file into an object3D + geometry analysis without displaying it; the
    // caller decides when (and whether) to show it via ShowObject. STL/OBJ/STEP/IGES decode
    // and analyze entirely inside the background worker. 3MF and GLB/GLTF decode here because
    // their loaders depend on DOM APIs that do not exist in a Worker, but they still submit
    // their decoded mesh buffers to the worker for the shared analysis pass.
    //
    // Returns a handle { cancel() } the caller can use to abandon this parse (e.g. the customer
    // removed the part before it finished) -- cancelling before the worker job starts drops it
    // from the queue immediately; cancelling a job already in flight frees the worker for the
    // next queued file instead of leaving it occupied until the job's own timeout.
    this.ParseFile = function (file, onReady, options) {
        options = options || {};
        var analysisProfile = options.analysisProfile === 'cnc' ? 'cnc' : 'additive';
        var extension = GetFileExtension(file.name);
        var cancelled = false;
        var activeJob = null;
        var handle = {
            cancel: function () {
                cancelled = true;
                if (activeJob !== null) {
                    activeJob.manager.Cancel(activeJob.jobId);
                }
            }
        };

        var prepareObject = function (object3D, modelInfo, includeCadEdges) {
            if (cancelled) { return; }
            modelInfo = modelInfo || {};
            modelInfo.sourceFormat = extension;
            modelInfo.isCadSource = extension === 'step' || extension === 'stp'
                || extension === 'iges' || extension === 'igs';
            object3D.userData = object3D.userData || {};
            if (!object3D.userData.malievPreviewBasePrepared) {
                EnsurePreviewGeometryNormals(object3D);
                if (modelInfo.bodyCount > 1) {
                    ApplyMultiBodyPreviewColors(object3D);
                }
                object3D.userData.malievPreviewBasePrepared = true;
            }
            if (includeCadEdges && !object3D.userData.malievPreviewEdgesPrepared) {
                AddCadPreviewEdges(object3D, modelInfo);
                object3D.userData.malievPreviewEdgesPrepared = true;
            }
            return modelInfo;
        };

        var handleObject = function (object3D, modelInfo, cncGeometry) {
            if (cancelled) { return; }
            modelInfo = prepareObject(object3D, modelInfo, true);
            onReady(modelInfo, object3D, null, cncGeometry);
        };

        var publishPreview = function (object3D, modelInfo) {
            if (cancelled || typeof options.onPreviewReady !== 'function') { return Promise.resolve(); }
            // The progressive preview is the first visible CAD frame. Prepare its feature
            // edges before publishing it so customers never see a temporary edge-less solid
            // while the slower accessibility analysis is still running.
            modelInfo = prepareObject(object3D, modelInfo, true);
            try {
                return Promise.resolve(options.onPreviewReady(modelInfo, object3D));
            } catch (error) {
                if (window.console && typeof window.console.error === 'function') {
                    window.console.error('Unable to publish the progressive model preview.', error);
                }
                return Promise.resolve();
            }
        };

        var onError = function (message) {
            if (cancelled) { return; }
            onReady(null, null, message || 'Unable to read this model file.');
        };

        var CreateAnalysisRequest = function (object3D) {
            object3D.updateWorldMatrix(true, true);
            var meshes = [];
            var transfer = [];
            object3D.traverse(function (child) {
                if (!child.isMesh || !child.geometry || !child.geometry.attributes || !child.geometry.attributes.position) {
                    return;
                }
                // .slice() copies the typed array -- transferring the live buffer would
                // detach it from the geometry this scene still uses for display.
                var position = child.geometry.attributes.position.array.slice();
                var index = child.geometry.index ? child.geometry.index.array.slice() : null;
                var matrix = new Float32Array(child.matrixWorld.elements);
                meshes.push({ position: position, index: index, matrix: matrix });
                transfer.push(position.buffer);
                if (index) { transfer.push(index.buffer); }
                transfer.push(matrix.buffer);
            });
            return { meshes: meshes, transfer: transfer };
        };

        var SubmitAnalysisRequest = function (object3D, request) {
            if (cancelled) { return; }
            var submitted = ModelAnalysisWorkerManager.Submit({ action: 'analyze', meshes: request.meshes, analysisProfile: analysisProfile }, request.transfer);
            activeJob = { manager: ModelAnalysisWorkerManager, jobId: submitted.jobId };
            submitted.promise.then(function (result) {
                handleObject(object3D, result.modelInfo, result.cncGeometry);
            }).catch(function (error) { onError(error.message); });
        };

        var SubmitObjectForAnalysis = function (object3D) {
            if (cancelled) { return; }
            if (!object3D) {
                onError('Unable to read this 3D file.');
                return;
            }
            SubmitAnalysisRequest(object3D, CreateAnalysisRequest(object3D));
        };

        if (extension === 'glb' || extension === 'gltf') {
            ReadArrayBuffer(file, function (buffer) {
                if (cancelled) { return; }
                try {
                    new THREE.GLTFLoader().parse(buffer, '', function (gltf) {
                        if (cancelled) { return; }
                        var object3D = gltf.scene;
                        object3D.rotation.x = Math.PI / 2; // Y-up (glTF) -> Z-up (printing)
                        // Apply the rotation to the world matrices BEFORE extracting them below --
                        // sending pre-rotation matrices to the worker would silently produce a wrong
                        // bounding box/volume/area-profile for every GLB/GLTF upload.
                        SubmitObjectForAnalysis(object3D);
                    }, function () { onError('Unable to read this glTF/GLB file.'); });
                } catch (e) {
                    onError('Unable to read this glTF/GLB file.');
                }
            }, onError);
            return handle;
        }

        if (extension === '3mf') {
            ReadArrayBuffer(file, function (buffer) {
                if (cancelled) { return; }
                try {
                    // ThreeMFLoader uses DOMParser for the package XML, so it must remain
                    // on the window thread. Geometry analysis still runs in the worker.
                    var object3D = new THREE.ThreeMFLoader().parse(buffer);
                    SubmitObjectForAnalysis(object3D);
                } catch (e) {
                    onError(e && e.message ? e.message : 'Unable to read this 3MF file.');
                }
            }, onError);
            return handle;
        }

        if (extension !== 'stl' && extension !== 'obj'
            && extension !== 'stp' && extension !== 'step' && extension !== 'igs' && extension !== 'iges') {
            onError('Unsupported file type.');
            return handle;
        }

        ReadArrayBuffer(file, function (buffer) {
            if (cancelled) { return; }
            var progressiveCncPreview = analysisProfile === 'cnc' && typeof options.onPreviewReady === 'function';
            var submitted = ModelParseWorkerManager.Submit({
                action: 'parse',
                extension: extension,
                buffer: buffer,
                analysisProfile: analysisProfile,
                deferCncAnalysis: progressiveCncPreview
            }, [buffer]);
            activeJob = { manager: ModelParseWorkerManager, jobId: submitted.jobId };
            submitted.promise.then(function (result) {
                // The worker has finished the heavy parse/analysis off-thread.
                // BuildGroupFromMeshBuffers constructs a THREE.BufferGeometry per
                // mesh and is O(N) in triangle count -- for a multi-million-triangle
                // STEP/IGES/STL file it can hold the main thread for several seconds
                // and trigger Chrome's "Page Unresponsive" dialog. Yield once so the
                // browser can paint the "parsed" status and drain pending input events
                // before the heavy geometry construction begins.
                setTimeout(function () {
                    if (cancelled) { return; }
                    BuildGroupFromMeshBuffersAsync(result.meshes).then(function (object3D) {
                        if (cancelled) { return; }
                        if (!progressiveCncPreview) {
                            handleObject(object3D, result.modelInfo, result.cncGeometry);
                            return;
                        }

                        // Snapshot the original indexed CAD topology before the display-only
                        // normal/edge preparation mutates it for crisp rendering.
                        var analysisMeshes = Array.isArray(result.analysisMeshes) ? result.analysisMeshes : null;
                        var analysisRequest = analysisMeshes ? {
                            meshes: analysisMeshes,
                            transfer: analysisMeshes.reduce(function (buffers, mesh) {
                                buffers.push(mesh.position.buffer);
                                if (mesh.index) { buffers.push(mesh.index.buffer); }
                                if (mesh.matrix) { buffers.push(mesh.matrix.buffer); }
                                return buffers;
                            }, [])
                        } : CreateAnalysisRequest(object3D);
                        // onPreviewReady resolves only after the first canvas paint and
                        // thumbnail capture; accessibility analysis starts afterwards.
                        return publishPreview(object3D, result.modelInfo).then(function () {
                            if (!cancelled) { SubmitAnalysisRequest(object3D, analysisRequest); }
                        });
                    }).catch(function (error) { onError(error.message); });
                }, 0);
            }).catch(function (error) { onError(error.message); });
        }, onError);
        return handle;
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
        ClearCncOverlays();
        cncOverlayPlan = null;
        cncOverlayPartId = null;
    };

    function DisposeOverlay(object) {
        if (!object) { return; }
        if (object.userData && object.userData.cncFieldTexture
            && object.userData.cncFieldTexture.dispose) {
            object.userData.cncFieldTexture.dispose();
        }
        object.traverse(function (child) {
            if (child.geometry && child.geometry.dispose) { child.geometry.dispose(); }
            if (child.material) {
                var materials = Array.isArray(child.material) ? child.material : [child.material];
                materials.forEach(function (material) {
                    if (material && material.map && material.map.dispose) { material.map.dispose(); }
                    if (material && material.dispose) { material.dispose(); }
                });
            }
        });
        scene.remove(object);
    }

    function CancelCncOverlayBuild(resetDiagnostics) {
        cncOverlayBuildGeneration += 1;
        if (activeCncOverlayJobId) {
            ModelOverlayWorkerManager.Cancel(activeCncOverlayJobId);
            activeCncOverlayJobId = null;
        }
        cncOverlayBuildPending = false;
        if (resetDiagnostics) {
            cncOverlayWorkerUsed = false;
            cncOverlayBuildFrameCount = 0;
        }
    }

    function ClearCncOverlays() {
        CancelCncOverlayBuild(true);
        DisposeOverlay(cncStockOverlay);
        DisposeOverlay(cncDirectionOverlay);
        DisposeOverlay(cncReachOverlay);
        DisposeOverlay(cncResidualOverlay);
        cncStockOverlay = null;
        cncDirectionOverlay = null;
        cncReachOverlay = null;
        cncResidualOverlay = null;
        cncStockMetadata = null;
        cncSetupLabelCount = 0;
        cncVisibleSetupIds = [];
        cncHighlightedClusterCount = 0;
        cncSetupSelectionAvailable = false;
        cncResidualGeometryAvailable = false;
        cncResidualMode = 'unavailable';
        cncSurfaceHighlightMode = 'unavailable';
        cncOverlayTriangleCount = 0;
        cncModelTriangleCount = 0;
        cncRenderedHighlightedTriangleCount = 0;
        cncRenderedDimmedTriangleCount = 0;
        cncRenderedResidualTriangleCount = 0;
        cncResidualMeshCount = 0;
        cncResidualConnectedComponentCount = 0;
        cncResidualWorldBounds = null;
        cncOverlaySampleCount = 0;
        cncRenderedHighlightedSampleCount = 0;
        cncRenderedDimmedSampleCount = 0;
        cncOverlayFieldChecksum = null;
        cncOverlayFieldOrigin = null;
        cncOverlayDrawCallCount = 0;
        cncFieldClassificationMode = 'none';
        cncFieldSamplingMode = 'none';
        cncCadEdgeMode = 'none';
        cncCadEdgeDrawCallCount = 0;
        cncFieldSpatialMapCache = null;
        cncOverlayUnavailableReason = null;
        cncOverlayWorkload = { triangleCount: 0, clusterCount: 0, triangleClusterComparisons: 0 };
        if (displayObject) { displayObject.visible = true; }
    }

    function NewCncOverlayState(partId) {
        return {
            partId: partId,
            stockVisible: false,
            directionsVisible: false,
            residualVisible: false,
            selectedSetupIds: [],
            selectedSetupId: null,
            selectedSetupNumber: 0,
            selectedSetupName: null,
            selectedSetupDirection: null
        };
    }

    function ClearCncSelectedSetup(state) {
        state.selectedSetupIds = [];
        state.selectedSetupId = null;
        state.selectedSetupNumber = 0;
        state.selectedSetupName = null;
        state.selectedSetupDirection = null;
    }

    function ApplyCncSelectedSetup(state, setup, index) {
        var number = Number.isSafeInteger(setup.number) && setup.number > 0 ? setup.number : index + 1;
        var setupId = String(setup.id || ('setup-' + number));
        state.selectedSetupIds = [setupId];
        state.selectedSetupId = setupId;
        state.selectedSetupNumber = number;
        state.selectedSetupName = setup.name || setup.label || ('Setup ' + number);
        var direction = setup.toolDirection || setup.direction;
        state.selectedSetupDirection = direction && Number.isFinite(Number(direction.x))
            && Number.isFinite(Number(direction.y)) && Number.isFinite(Number(direction.z))
            ? { x: Number(direction.x), y: Number(direction.y), z: Number(direction.z) }
            : null;
    }

    function RefreshCncSelectedSetupSummary(state, setups, preferredId) {
        var validIds = (Array.isArray(state.selectedSetupIds) ? state.selectedSetupIds : []).filter(function (id, index, ids) {
            return ids.indexOf(id) === index && setups.some(function (setup) {
                return String(setup.id || ('setup-' + setup.number)) === String(id);
            });
        });
        state.selectedSetupIds = validIds;
        if (validIds.length === 0) { ClearCncSelectedSetup(state); return; }
        var activeId = validIds.indexOf(String(preferredId)) >= 0 ? String(preferredId) : validIds[validIds.length - 1];
        var index = setups.findIndex(function (setup) {
            return String(setup.id || ('setup-' + setup.number)) === activeId;
        });
        var setup = setups[index];
        var number = Number.isSafeInteger(setup.number) && setup.number > 0 ? setup.number : index + 1;
        state.selectedSetupId = activeId;
        state.selectedSetupNumber = number;
        state.selectedSetupName = setup.name || setup.label || ('Setup ' + number);
        var direction = setup.toolDirection || setup.direction;
        state.selectedSetupDirection = direction && Number.isFinite(Number(direction.x))
            && Number.isFinite(Number(direction.y)) && Number.isFinite(Number(direction.z))
            ? { x: Number(direction.x), y: Number(direction.y), z: Number(direction.z) }
            : null;
    }

    function CncSetupHasReachEvidence(setup, plan, clusterIds) {
        var reachableIds = Array.isArray(setup.reachableClusterIds) ? setup.reachableClusterIds.slice() : [];
        var setupId = String(setup.id || ('setup-' + setup.number));
        (plan.reachMatrix || []).forEach(function (record) {
            if (String(record.setupId || ('setup-' + record.setupNumber)) === setupId
                && record.reachable === true && reachableIds.indexOf(record.clusterId) === -1) {
                reachableIds.push(record.clusterId);
            }
        });
        if (!Array.isArray(clusterIds)) { return reachableIds.length > 0; }
        return reachableIds.some(function (id) { return clusterIds.indexOf(id) >= 0; });
    }

    function CurrentCncOverlayState() {
        if (cncOverlayPartId === null) { return NewCncOverlayState(null); }
        if (!cncOverlayStateByPart[cncOverlayPartId]) {
            cncOverlayStateByPart[cncOverlayPartId] = NewCncOverlayState(cncOverlayPartId);
        }
        return cncOverlayStateByPart[cncOverlayPartId];
    }

    function CncVector(value) {
        if (!value || !Number.isFinite(Number(value.x)) || !Number.isFinite(Number(value.y)) || !Number.isFinite(Number(value.z))) {
            return null;
        }
        var vector = new THREE.Vector3(Number(value.x), Number(value.y), Number(value.z));
        return vector.lengthSq() > 0 ? vector.normalize() : null;
    }

    function CncOrientationQuaternion(frame) {
        var xAxis = frame && CncVector(frame.xAxis);
        var yAxis = frame && CncVector(frame.yAxis);
        if (!xAxis || !yAxis || Math.abs(xAxis.dot(yAxis)) > 0.001) { return null; }
        var zAxis = new THREE.Vector3().crossVectors(xAxis, yAxis);
        if (zAxis.lengthSq() === 0) { return null; }
        zAxis.normalize();
        yAxis = new THREE.Vector3().crossVectors(zAxis, xAxis).normalize();
        return new THREE.Quaternion().setFromRotationMatrix(
            new THREE.Matrix4().makeBasis(xAxis, yAxis, zAxis));
    }

    function CncFitStockToDisplayedPart(shape, orientation, requested) {
        var points = [];
        if (displayObject) {
            displayObject.updateMatrixWorld(true);
            var point = new THREE.Vector3();
            var inverse = orientation.clone().invert();
            displayObject.traverse(function (child) {
                if (!child.isMesh || !child.geometry || !child.geometry.attributes || !child.geometry.attributes.position) { return; }
                var position = child.geometry.attributes.position;
                for (var index = 0; index < position.count; index++) {
                    point.fromBufferAttribute(position, index).applyMatrix4(child.matrixWorld).applyQuaternion(inverse);
                    points.push(point.clone());
                }
            });
        }
        var bounds = new THREE.Box3();
        points.forEach(function (point) { bounds.expandByPoint(point); });
        if (points.length === 0 || bounds.isEmpty()) {
            return { dimensions: requested, position: new THREE.Vector3(), containsPart: true, clippedVertexCount: 0 };
        }
        var center = bounds.getCenter(new THREE.Vector3());
        var span = bounds.getSize(new THREE.Vector3());
        var dimensions;
        if (shape === 'round') {
            var radius = points.reduce(function (maximum, point) {
                var radial = Math.sqrt(Math.pow(point.x - center.x, 2) + Math.pow(point.z - center.z, 2));
                return Math.max(maximum, radial);
            }, 0);
            dimensions = {
                x: Math.max(requested.x, radius * 2),
                y: Math.max(requested.y, span.y),
                z: Math.max(requested.z, radius * 2)
            };
        } else {
            dimensions = {
                x: Math.max(requested.x, span.x),
                y: Math.max(requested.y, span.y),
                z: Math.max(requested.z, span.z)
            };
        }
        var tolerance = 1e-5;
        var clippedVertexCount = points.filter(function (point) {
            var relative = point.clone().sub(center);
            if (shape === 'round') {
                return Math.abs(relative.y) > dimensions.y / 2 + tolerance
                    || Math.sqrt((relative.x * relative.x) + (relative.z * relative.z)) > dimensions.x / 2 + tolerance;
            }
            return Math.abs(relative.x) > dimensions.x / 2 + tolerance
                || Math.abs(relative.y) > dimensions.y / 2 + tolerance
                || Math.abs(relative.z) > dimensions.z / 2 + tolerance;
        }).length;
        return {
            dimensions: dimensions,
            position: center.applyQuaternion(orientation),
            containsPart: clippedVertexCount === 0,
            clippedVertexCount: clippedVertexCount
        };
    }

    function CreateCncStockOverlay(stock, state) {
        var size = stock.stockSizeMm || {};
        var shape = stock.stockShape === 'round' ? 'round' : (stock.stockShape === 'plate' ? 'plate' : 'block');
        var orientation = new THREE.Quaternion();
        if (shape === 'round') {
            var symmetryAxis = CncVector(stock.symmetryAxis) || new THREE.Vector3(0, 0, 1);
            orientation.setFromUnitVectors(new THREE.Vector3(0, 1, 0), symmetryAxis);
        } else {
            orientation.copy(CncOrientationQuaternion(stock.orientationFrame) || new THREE.Quaternion());
        }
        var geometry;
        var diameterMm = Number(stock.diameterMm) || Number(size.x) || 1;
        var lengthMm = Number(stock.lengthMm) || Number(size.z) || 1;
        var requested = shape === 'round'
            ? { x: diameterMm, y: lengthMm, z: diameterMm }
            : { x: Number(size.x) || 1, y: Number(size.y) || 1, z: Number(size.z) || 1 };
        var fitted = CncFitStockToDisplayedPart(shape, orientation, requested);
        diameterMm = shape === 'round' ? fitted.dimensions.x : diameterMm;
        lengthMm = shape === 'round' ? fitted.dimensions.y : lengthMm;
        if (shape === 'round') {
            geometry = new THREE.CylinderGeometry(diameterMm / 2, diameterMm / 2, lengthMm, 48);
        } else {
            geometry = new THREE.BoxGeometry(fitted.dimensions.x, fitted.dimensions.y, fitted.dimensions.z);
        }

        var group = new THREE.Group();
        var fill = new THREE.Mesh(geometry, new THREE.MeshPhongMaterial({
            color: 0x1677ff,
            transparent: true,
            opacity: CNC_STOCK_OPACITY,
            depthWrite: false,
            side: THREE.DoubleSide
        }));
        var outline = new THREE.LineSegments(
            new THREE.EdgesGeometry(geometry),
            new THREE.LineBasicMaterial({ color: 0x075de8, transparent: true, opacity: 0.92 }));
        group.add(fill);
        group.add(outline);

        group.quaternion.copy(orientation);
        group.position.copy(fitted.position);
        group.visible = state.stockVisible;
        cncStockMetadata = {
            shape: shape,
            primitive: shape === 'round' ? 'cylinder' : 'box',
            opacity: CNC_STOCK_OPACITY,
            filled: true,
            outlineVisible: true,
            containsPart: fitted.containsPart,
            clippedVertexCount: fitted.clippedVertexCount,
            orientationFrame: stock.orientationFrame || null,
            dimensions: {
                x: shape === 'round' ? diameterMm : fitted.dimensions.x,
                y: shape === 'round' ? diameterMm : fitted.dimensions.y,
                z: shape === 'round' ? lengthMm : fitted.dimensions.z,
                diameterMm: shape === 'round' ? diameterMm : 0,
                lengthMm: shape === 'round' ? lengthMm : 0
            }
        };
        return group;
    }

    var CNC_SETUP_LABEL_PIXEL_WIDTH = 96;
    var CNC_SETUP_LABEL_PIXEL_HEIGHT = 32;
    var CNC_SETUP_LABEL_CSS_FONT_PX = 12;
    var CNC_SETUP_LABEL_GAP_PX = 12;

    function CreateCncSetupLabel(number, position) {
        var labelCanvas = document.createElement('canvas');
        labelCanvas.width = 192;
        labelCanvas.height = 64;
        var context = labelCanvas.getContext('2d');
        context.fillStyle = 'rgba(15, 32, 54, 0.92)';
        context.beginPath();
        context.roundRect(4, 4, 184, 56, 18);
        context.fill();
        context.strokeStyle = '#ffffff';
        context.lineWidth = 3;
        context.stroke();
        context.fillStyle = '#ffffff';
        context.font = '700 24px system-ui, sans-serif';
        context.textAlign = 'center';
        context.textBaseline = 'middle';
        context.fillText('Setup ' + number, 96, 34);
        var texture = new THREE.CanvasTexture(labelCanvas);
        texture.minFilter = THREE.LinearFilter;
        var sprite = new THREE.Sprite(new THREE.SpriteMaterial({
            map: texture,
            transparent: true,
            depthTest: true,
            depthWrite: false
        }));
        sprite.position.copy(position);
        sprite.scale.set(1, 1, 1);
        sprite.renderOrder = 10;
        sprite.userData.cncSetupLabel = true;
        sprite.userData.cncLabelPixelWidth = CNC_SETUP_LABEL_PIXEL_WIDTH;
        sprite.userData.cncLabelPixelHeight = CNC_SETUP_LABEL_PIXEL_HEIGHT;
        sprite.userData.cncLabelCssFontPx = CNC_SETUP_LABEL_CSS_FONT_PX;
        sprite.userData.cncLabelGapPixels = CNC_SETUP_LABEL_GAP_PX;
        return sprite;
    }

    function CncWorldUnitsPerCssPixel(position) {
        var canvasHeight = Math.max(1, Number(canvasElement.clientHeight) ||
            (Number(canvasElement.height) / Math.max(1, window.devicePixelRatio || 1)) || 1);
        if (camera.isPerspectiveCamera) {
            var cameraPosition = position.clone().applyMatrix4(camera.matrixWorldInverse);
            var depth = Math.max(camera.near, Math.abs(cameraPosition.z));
            var visibleHeight = 2 * depth * Math.tan(THREE.MathUtils.degToRad(camera.fov * 0.5)) /
                Math.max(0.0001, camera.zoom || 1);
            return visibleHeight / canvasHeight;
        }
        if (camera.isOrthographicCamera) {
            return ((camera.top - camera.bottom) / Math.max(0.0001, camera.zoom || 1)) / canvasHeight;
        }
        return 1 / canvasHeight;
    }

    function UpdateCncSetupLabelScales() {
        if (!cncDirectionOverlay || !camera) { return; }
        camera.updateMatrixWorld();
        cncDirectionOverlay.traverse(function (child) {
            if (!child.isSprite || !child.userData || !child.userData.cncSetupLabel) { return; }
            var anchor = child.userData.cncLabelAnchor;
            var direction = child.userData.cncLabelDirection;
            var worldAnchor = anchor
                ? cncDirectionOverlay.localToWorld(anchor.clone())
                : child.getWorldPosition(new THREE.Vector3());
            var worldUnitsPerPixel = CncWorldUnitsPerCssPixel(worldAnchor);
            child.scale.set(
                child.userData.cncLabelPixelWidth * worldUnitsPerPixel,
                child.userData.cncLabelPixelHeight * worldUnitsPerPixel,
                1);
            if (anchor && direction) {
                var offsetPixels = (child.userData.cncLabelPixelWidth * 0.5)
                    + child.userData.cncLabelGapPixels;
                child.position.copy(anchor).add(direction.clone().multiplyScalar(offsetPixels * worldUnitsPerPixel));
            }
        });
    }

    function TagCncSetupObject(object, setup) {
        object.traverse(function (child) {
            child.userData.cncSetupId = setup.id || ('setup-' + setup.number);
            child.userData.cncSetupNumber = setup.number;
        });
    }

    function CncStockMarkerEnvelope(stockOverlay, fallbackDimension) {
        var bounds = stockOverlay ? new THREE.Box3().setFromObject(stockOverlay) : new THREE.Box3();
        if (bounds.isEmpty()) {
            var half = Math.max(1, fallbackDimension) / 2;
            bounds.set(new THREE.Vector3(-half, -half, -half), new THREE.Vector3(half, half, half));
        }
        var size = bounds.getSize(new THREE.Vector3());
        return {
            bounds: bounds,
            center: bounds.getCenter(new THREE.Vector3()),
            scale: Math.max(size.x, size.y, size.z, 1)
        };
    }

    function CncStockBoundaryDistance(envelope, direction) {
        var distances = ['x', 'y', 'z'].map(function (axis) {
            if (Math.abs(direction[axis]) <= 1e-8) { return Infinity; }
            var boundary = direction[axis] > 0 ? envelope.bounds.max[axis] : envelope.bounds.min[axis];
            return (boundary - envelope.center[axis]) / direction[axis];
        }).filter(function (distance) { return Number.isFinite(distance) && distance >= 0; });
        return distances.length > 0 ? Math.min.apply(Math, distances) : envelope.scale / 2;
    }

    function CreateCncDirectionOverlay(setups, state, maxDimension, stockOverlay) {
        var group = new THREE.Group();
        var envelope = CncStockMarkerEnvelope(stockOverlay, maxDimension);
        var markerScale = envelope.scale;
        var arrowLength = markerScale * 0.07;
        var stockMargin = markerScale * 0.025;
        group.userData.cncSetupMarkers = [];
        var selectedIds = Array.isArray(state.selectedSetupIds) ? state.selectedSetupIds : [];
        cncVisibleSetupIds = [];
        (setups || []).forEach(function (setup, index) {
            var direction = CncVector(setup && (setup.toolDirection || setup.direction));
            if (!direction) { return; }
            var number = Number.isSafeInteger(setup.number) && setup.number > 0 ? setup.number : index + 1;
            var setupId = setup.id || ('setup-' + number);
            var selected = selectedIds.indexOf(String(setupId)) >= 0;
            if (selectedIds.length > 0 && !selected) { return; }
            var stockBoundaryDistance = CncStockBoundaryDistance(envelope, direction);
            var tipDistance = stockBoundaryDistance + stockMargin;
            var originDistance = tipDistance + arrowLength;
            var origin = envelope.center.clone().add(direction.clone().multiplyScalar(originDistance));
            var arrow = new THREE.ArrowHelper(
                direction.clone().negate(), origin, arrowLength,
                selected ? 0x075de8 : 0xe07a1f, markerScale * 0.03, markerScale * 0.018);
            var worldUnitsPerPixel = CncWorldUnitsPerCssPixel(origin);
            var labelOffset = ((CNC_SETUP_LABEL_PIXEL_WIDTH * 0.5) + CNC_SETUP_LABEL_GAP_PX)
                * worldUnitsPerPixel;
            var labelDistance = originDistance + labelOffset;
            var labelPosition = origin.clone().add(direction.clone().multiplyScalar(labelOffset));
            var label = CreateCncSetupLabel(number, labelPosition);
            label.userData.cncLabelAnchor = origin.clone();
            label.userData.cncLabelDirection = direction.clone();
            TagCncSetupObject(arrow, { id: setupId, number: number });
            TagCncSetupObject(label, { id: setupId, number: number });
            group.add(arrow);
            group.add(label);
            group.userData.cncSetupMarkers.push({
                setupId: String(setupId),
                stockBoundaryDistance: stockBoundaryDistance,
                originDistance: originDistance,
                tipDistance: tipDistance,
                labelDistance: labelDistance,
                arrowLength: arrowLength,
                labelWidth: label.scale.x,
                labelHeight: label.scale.y,
                labelPixelWidth: label.userData.cncLabelPixelWidth,
                labelPixelHeight: label.userData.cncLabelPixelHeight,
                labelCssFontPx: label.userData.cncLabelCssFontPx,
                labelGapPixels: label.userData.cncLabelGapPixels,
                labelClearsArrow: true,
                outsideStock: tipDistance > stockBoundaryDistance
            });
            cncVisibleSetupIds.push(String(setupId));
            cncSetupLabelCount += 1;
        });
        group.visible = state.directionsVisible;
        cncDirectionOverlay = group;
        UpdateCncSetupLabelScales();
        return group;
    }

    function CncClusterWorldEvidence(cluster) {
        var centroid = cluster && cluster.centroid
            ? new THREE.Vector3(Number(cluster.centroid.x) || 0, Number(cluster.centroid.y) || 0, Number(cluster.centroid.z) || 0)
            : new THREE.Vector3();
        if (displayObject) { centroid.applyMatrix4(displayObject.matrixWorld); }
        var normal = CncVector(cluster && cluster.normal);
        var axis = CncVector(cluster && cluster.axis);
        if (displayObject && (normal || axis)) {
            var normalMatrix = new THREE.Matrix3().getNormalMatrix(displayObject.matrixWorld);
            if (normal) { normal.applyMatrix3(normalMatrix).normalize(); }
            if (axis) { axis.applyMatrix3(normalMatrix).normalize(); }
        }
        return { source: cluster, centroid: centroid, normal: normal, axis: axis };
    }

    function ClosestCncCluster(centroid, normal, clusters, maxDimension) {
        var best = null;
        var bestScore = Infinity;
        clusters.forEach(function (cluster) {
            var normalPenalty = 0.6;
            if (cluster.source.type === 'planar' && cluster.normal) {
                normalPenalty = 1 - Math.max(-1, Math.min(1, normal.dot(cluster.normal)));
            } else if ((cluster.source.type === 'cylindrical' || cluster.source.type === 'conical') && cluster.axis) {
                normalPenalty = Math.abs(normal.dot(cluster.axis));
            }
            var distancePenalty = centroid.distanceTo(cluster.centroid) / Math.max(1, maxDimension);
            var score = (normalPenalty * 2.4) + distancePenalty;
            if (score < bestScore) { bestScore = score; best = cluster; }
        });
        return best;
    }

    function AddCncOverlayMesh(group, positions, color, opacity) {
        if (positions.length === 0) { return; }
        var geometry = new THREE.BufferGeometry();
        geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
        var material = new THREE.MeshBasicMaterial({
            color: color,
            transparent: opacity < 1,
            opacity: opacity,
            depthWrite: opacity >= 1,
            side: THREE.DoubleSide,
            polygonOffset: true,
            polygonOffsetFactor: 1,
            polygonOffsetUnits: 1
        });
        var mesh = new THREE.Mesh(geometry, material);
        mesh.renderOrder = 4;
        group.add(mesh);
    }

    function AddCncCadEdgesToOverlay(group) {
        if (!displayObject || !IsCadModelInfo(activeModelInfo)) { return 0; }
        var count = 0;
        displayObject.traverse(function (child) {
            if (!child.isMesh || !child.geometry) { return; }
            var cachedEdges = child.children.find(function (candidate) {
                return candidate.userData && candidate.userData.cncCadFeatureEdges;
            });
            var edges = CreateCadFeatureEdges(child.geometry, cachedEdges && cachedEdges.geometry);
            if (!edges) { return; }
            edges.matrixAutoUpdate = false;
            edges.matrix.copy(child.matrixWorld);
            group.add(edges);
            count += 1;
        });
        return count;
    }

    function CncFieldCellCoordinates(index, dimensions) {
        var plane = dimensions.x * dimensions.y;
        var z = Math.floor(index / plane);
        var remainder = index - (z * plane);
        var y = Math.floor(remainder / dimensions.x);
        return { x: remainder - (y * dimensions.x), y: y, z: z };
    }

    function BuildCncFieldClassificationTexture(field, reachable, unmachinable, residualClusters,
        hasSelectedSetup, residualVisible) {
        var dimensions = field.dimensions || {};
        var widthCells = Number(dimensions.x) || 0;
        var heightCells = Number(dimensions.y) || 0;
        var depthCells = Number(dimensions.z) || 0;
        var totalCells = widthCells * heightCells * depthCells;
        var runs = Array.isArray(field.occupancyRuns) ? field.occupancyRuns : [];
        if (totalCells <= 0 || runs.length === 0 || !field.origin || !Array.isArray(field.axes)
            || field.axes.length !== 3) {
            return null;
        }

        var maximumTextureSize = Math.max(1, Number(renderer.capabilities.maxTextureSize) || 4096);
        var textureWidth = Math.min(maximumTextureSize, Math.max(1, Math.ceil(Math.sqrt(totalCells))));
        var textureHeight = Math.ceil(totalCells / textureWidth);
        if (textureHeight > maximumTextureSize) { return null; }

        var occupancy = new Uint8Array(totalCells);
        runs.forEach(function (run) {
            var start = Math.max(0, Number(run[0]) || 0);
            var end = Math.min(totalCells, start + Math.max(0, Number(run[1]) || 0));
            occupancy.fill(1, start, end);
        });
        function occupied(x, y, z) {
            return x >= 0 && y >= 0 && z >= 0 && x < widthCells && y < heightCells && z < depthCells
                && occupancy[x + (widthCells * (y + (heightCells * z)))] === 1;
        }
        function isBoundary(index) {
            var point = CncFieldCellCoordinates(index, dimensions);
            return !occupied(point.x - 1, point.y, point.z) || !occupied(point.x + 1, point.y, point.z)
                || !occupied(point.x, point.y - 1, point.z) || !occupied(point.x, point.y + 1, point.z)
                || !occupied(point.x, point.y, point.z - 1) || !occupied(point.x, point.y, point.z + 1);
        }
        var fieldAxis0 = CncVector(field.axes[0]) || new THREE.Vector3(1, 0, 0);
        var fieldAxis1 = CncVector(field.axes[1]) || new THREE.Vector3(0, 1, 0);
        var fieldAxis2 = CncVector(field.axes[2]) || new THREE.Vector3(0, 0, 1);
        function boundaryNormal(index) {
            var point = CncFieldCellCoordinates(index, dimensions);
            var gridX = (occupied(point.x - 1, point.y, point.z) ? 0 : -1)
                + (occupied(point.x + 1, point.y, point.z) ? 0 : 1);
            var gridY = (occupied(point.x, point.y - 1, point.z) ? 0 : -1)
                + (occupied(point.x, point.y + 1, point.z) ? 0 : 1);
            var gridZ = (occupied(point.x, point.y, point.z - 1) ? 0 : -1)
                + (occupied(point.x, point.y, point.z + 1) ? 0 : 1);
            var normal = new THREE.Vector3()
                .addScaledVector(fieldAxis0, gridX)
                .addScaledVector(fieldAxis1, gridY)
                .addScaledVector(fieldAxis2, gridZ);
            return normal.lengthSq() > 1e-8 ? normal.normalize() : null;
        }
        function sampleClassCode(sample) {
            var isResidual = unmachinable.has(sample.id) || residualClusters.has(sample.clusterId);
            if (hasSelectedSetup) {
                if (residualVisible && isResidual && !reachable.has(sample.id)) { return 192; }
                return reachable.has(sample.id) ? 64 : 128;
            }
            return residualVisible && isResidual ? 192 : 255;
        }
        function encodedNormal(sample) {
            var normal = sample.normal || {};
            return ['x', 'y', 'z'].map(function (axis) {
                var value = Math.max(-1, Math.min(1, Number(normal[axis]) || 0));
                return Math.round((value * 0.5 + 0.5) * 255);
            });
        }

        var samples = field.surfaceSamples;
        var sampleCoordinates = samples.map(function (sample) {
            return CncFieldCellCoordinates(Number(sample.id), dimensions);
        });
        var sampleClassCodes = samples.map(sampleClassCode);
        var sampleNormals = samples.map(encodedNormal);
        var sampleUnitNormals = samples.map(function (sample) {
            return CncVector(sample.normal);
        });
        var exactSampleIndex = new Map();
        samples.forEach(function (sample, index) { exactSampleIndex.set(Number(sample.id), index); });
        var sampleStride = Math.max(1, Number(field.surfaceSampleStride) || 1);
        var spatialMapKey = ['surface-normal-v1', field.checksum || '', widthCells, heightCells, depthCells,
            samples.length, sampleStride].join(':');
        var spatialMap = cncFieldSpatialMapCache && cncFieldSpatialMapCache.key === spatialMapKey
            ? cncFieldSpatialMapCache : null;
        if (!spatialMap) {
            var bucketSize = Math.max(3, Math.ceil(Math.sqrt(sampleStride)) * 2);
            var bucketCountX = Math.ceil(widthCells / bucketSize);
            var bucketCountY = Math.ceil(heightCells / bucketSize);
            var bucketCountZ = Math.ceil(depthCells / bucketSize);
            var sampleBuckets = new Map();
            function bucketKey(x, y, z) { return x + (bucketCountX * (y + (bucketCountY * z))); }
            sampleCoordinates.forEach(function (coordinate, sampleIndex) {
                var key = bucketKey(Math.floor(coordinate.x / bucketSize), Math.floor(coordinate.y / bucketSize),
                    Math.floor(coordinate.z / bucketSize));
                var bucket = sampleBuckets.get(key);
                if (!bucket) { bucket = []; sampleBuckets.set(key, bucket); }
                bucket.push(sampleIndex);
            });
            function nearestSpatialSample(cell, cellNormal) {
                var centerX = Math.floor(cell.x / bucketSize);
                var centerY = Math.floor(cell.y / bucketSize);
                var centerZ = Math.floor(cell.z / bucketSize);
                var bestDistance = Infinity;
                var bestSampleIndex = -1;
                var firstRadiusWithSamples = -1;
                var maximumRadius = Math.max(bucketCountX, bucketCountY, bucketCountZ);
                for (var radius = 0; radius <= maximumRadius; radius++) {
                    var minZ = Math.max(0, centerZ - radius);
                    var maxZ = Math.min(bucketCountZ - 1, centerZ + radius);
                    var minY = Math.max(0, centerY - radius);
                    var maxY = Math.min(bucketCountY - 1, centerY + radius);
                    var minX = Math.max(0, centerX - radius);
                    var maxX = Math.min(bucketCountX - 1, centerX + radius);
                    for (var z = minZ; z <= maxZ; z++) {
                        for (var y = minY; y <= maxY; y++) {
                            for (var x = minX; x <= maxX; x++) {
                                if (radius > 0 && Math.max(Math.abs(x - centerX), Math.abs(y - centerY),
                                    Math.abs(z - centerZ)) !== radius) { continue; }
                                var bucket = sampleBuckets.get(bucketKey(x, y, z));
                                if (!bucket) { continue; }
                                bucket.forEach(function (candidateIndex) {
                                    var candidateNormal = sampleUnitNormals[candidateIndex];
                                    if (cellNormal && candidateNormal && cellNormal.dot(candidateNormal) < 0.5) {
                                        return;
                                    }
                                    if (firstRadiusWithSamples < 0) { firstRadiusWithSamples = radius; }
                                    var candidate = sampleCoordinates[candidateIndex];
                                    var dx = candidate.x - cell.x;
                                    var dy = candidate.y - cell.y;
                                    var dz = candidate.z - cell.z;
                                    var distance = (dx * dx) + (dy * dy) + (dz * dz);
                                    if (distance < bestDistance) {
                                        bestDistance = distance;
                                        bestSampleIndex = candidateIndex;
                                    }
                                });
                            }
                        }
                    }
                    // One additional bucket shell protects points near a bucket boundary without
                    // turning the render pass into an all-samples search.
                    if (firstRadiusWithSamples >= 0 && radius > firstRadiusWithSamples) { break; }
                }
                return bestSampleIndex;
            }
            var boundaryIndexes = [];
            var boundarySampleIndexes = [];
            for (var boundaryIndex = 0; boundaryIndex < totalCells; boundaryIndex++) {
                if (occupancy[boundaryIndex] !== 1 || !isBoundary(boundaryIndex)) { continue; }
                var mappedSampleIndex = exactSampleIndex.has(boundaryIndex)
                    ? exactSampleIndex.get(boundaryIndex)
                    : nearestSpatialSample(CncFieldCellCoordinates(boundaryIndex, dimensions),
                        boundaryNormal(boundaryIndex));
                if (mappedSampleIndex < 0) { continue; }
                boundaryIndexes.push(boundaryIndex);
                boundarySampleIndexes.push(mappedSampleIndex);
            }
            spatialMap = {
                key: spatialMapKey,
                boundaryIndexes: new Uint32Array(boundaryIndexes),
                sampleIndexes: new Uint32Array(boundarySampleIndexes)
            };
            cncFieldSpatialMapCache = spatialMap;
        }
        var pixels = new Uint8Array(textureWidth * textureHeight * 4);
        for (var mappedIndex = 0; mappedIndex < spatialMap.boundaryIndexes.length; mappedIndex++) {
            var index = spatialMap.boundaryIndexes[mappedIndex];
            var sampleIndex = spatialMap.sampleIndexes[mappedIndex];
            var normal = sampleNormals[sampleIndex];
            var classCode = sampleClassCodes[sampleIndex];
            if (normal && classCode) {
                var pixelOffset = index * 4;
                pixels[pixelOffset] = normal[0];
                pixels[pixelOffset + 1] = normal[1];
                pixels[pixelOffset + 2] = normal[2];
                pixels[pixelOffset + 3] = classCode;
            }
        }
        var texture = new THREE.DataTexture(pixels, textureWidth, textureHeight, THREE.RGBAFormat,
            THREE.UnsignedByteType);
        texture.minFilter = THREE.NearestFilter;
        texture.magFilter = THREE.NearestFilter;
        texture.generateMipmaps = false;
        texture.needsUpdate = true;
        return { texture: texture, width: textureWidth, height: textureHeight, mode: 'surface-normal-nearest' };
    }

    function CreateCncFieldTextureMaterial(classification, field, worldToPart) {
        var axes = field.axes;
        var fieldOrigin = new THREE.Vector3(
            Number(field.origin.x) || 0,
            Number(field.origin.y) || 0,
            Number(field.origin.z) || 0);
        return new THREE.ShaderMaterial({
            uniforms: {
                cncFieldMap: { value: classification.texture },
                cncTextureSize: { value: new THREE.Vector2(classification.width, classification.height) },
                cncFieldDimensions: { value: new THREE.Vector3(field.dimensions.x, field.dimensions.y, field.dimensions.z) },
                cncFieldOrigin: { value: fieldOrigin },
                cncFieldAxis0: { value: CncVector(axes[0]) || new THREE.Vector3(1, 0, 0) },
                cncFieldAxis1: { value: CncVector(axes[1]) || new THREE.Vector3(0, 1, 0) },
                cncFieldAxis2: { value: CncVector(axes[2]) || new THREE.Vector3(0, 0, 1) },
                cncFieldCellSize: { value: Number(field.cellSizeMm) },
                cncWorldToPart: { value: worldToPart }
            },
            vertexShader: [
                'uniform mat4 cncWorldToPart;',
                'varying vec3 cncPartPosition;',
                'varying vec3 cncPartNormal;',
                'void main() {',
                '  vec4 worldPosition = modelMatrix * vec4(position, 1.0);',
                '  cncPartPosition = (cncWorldToPart * worldPosition).xyz;',
                '  vec3 worldNormal = normalize(mat3(modelMatrix) * normal);',
                '  cncPartNormal = normalize(mat3(cncWorldToPart) * worldNormal);',
                '  gl_Position = projectionMatrix * viewMatrix * worldPosition;',
                '}'
            ].join('\n'),
            fragmentShader: [
                'uniform sampler2D cncFieldMap;',
                'uniform vec2 cncTextureSize;',
                'uniform vec3 cncFieldDimensions;',
                'uniform vec3 cncFieldOrigin;',
                'uniform vec3 cncFieldAxis0;',
                'uniform vec3 cncFieldAxis1;',
                'uniform vec3 cncFieldAxis2;',
                'uniform float cncFieldCellSize;',
                'varying vec3 cncPartPosition;',
                'varying vec3 cncPartNormal;',
                'vec4 cncFieldValue(vec3 cell) {',
                '  if (cell.x < 0.0 || cell.y < 0.0 || cell.z < 0.0',
                '      || cell.x >= cncFieldDimensions.x || cell.y >= cncFieldDimensions.y',
                '      || cell.z >= cncFieldDimensions.z) return vec4(0.0);',
                '  float index = cell.x + cncFieldDimensions.x * (cell.y + cncFieldDimensions.y * cell.z);',
                '  float pixelX = mod(index, cncTextureSize.x);',
                '  float pixelY = floor(index / cncTextureSize.x);',
                '  return texture2D(cncFieldMap, (vec2(pixelX, pixelY) + 0.5) / cncTextureSize);',
                '}',
                'vec4 cncClassColor(float classCode) {',
                '  if (classCode < 1.5) return vec4(24.0 / 255.0, 201.0 / 255.0, 90.0 / 255.0, 1.0);',
                '  if (classCode < 2.5) return vec4(229.0 / 255.0, 57.0 / 255.0, 53.0 / 255.0, 1.0);',
                '  if (classCode < 3.5) return vec4(217.0 / 255.0, 140.0 / 255.0, 16.0 / 255.0, 1.0);',
                '  return vec4(103.0 / 255.0, 117.0 / 255.0, 130.0 / 255.0, 1.0);',
                '}',
                'void main() {',
                '  vec3 cadNormal = normalize(cncPartNormal);',
                '  vec3 relative = cncPartPosition - cncFieldOrigin;',
                '  vec3 fieldPosition = vec3(dot(relative, cncFieldAxis0), dot(relative, cncFieldAxis1),',
                '      dot(relative, cncFieldAxis2)) / cncFieldCellSize - vec3(0.5);',
                '  vec3 center = floor(fieldPosition + vec3(0.5));',
                '  float reachableWeight = 0.0;',
                '  float blockedWeight = 0.0;',
                '  float residualWeight = 0.0;',
                '  float neutralWeight = 0.0;',
                '  float nearestClass = 0.0;',
                '  float nearestDistance = 1000.0;',
                '  for (int z = -1; z <= 1; z++) for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) {',
                '    vec3 offset = vec3(float(x), float(y), float(z));',
                '    vec4 candidate = cncFieldValue(center + offset);',
                '    if (candidate.a <= 0.0) continue;',
                '    vec3 sampleDelta = center + offset - fieldPosition;',
                '    float distance = dot(sampleDelta, sampleDelta);',
                '    if (distance < nearestDistance) {',
                '      nearestClass = floor(candidate.a * 4.0 + 0.5);',
                '      nearestDistance = distance;',
                '    }',
                '    vec3 sampleNormal = normalize(candidate.rgb * 2.0 - 1.0);',
                '    float normalAgreement = max(0.0, dot(sampleNormal, cadNormal));',
                '    if (normalAgreement < 0.2) continue;',
                '    vec3 partDelta = cncFieldAxis0 * sampleDelta.x + cncFieldAxis1 * sampleDelta.y',
                '        + cncFieldAxis2 * sampleDelta.z;',
                '    float depthDelta = dot(partDelta, sampleNormal);',
                '    float weight = exp(-0.42 * (distance + 8.0 * depthDelta * depthDelta))',
                '        * pow(normalAgreement, 8.0);',
                '    float classCode = floor(candidate.a * 4.0 + 0.5);',
                '    if (classCode < 1.5) reachableWeight += weight;',
                '    else if (classCode < 2.5) blockedWeight += weight;',
                '    else if (classCode < 3.5) residualWeight += weight;',
                '    else neutralWeight += weight;',
                '  }',
                '  if (nearestClass <= 0.0) discard;',
                '  float totalWeight = reachableWeight + blockedWeight + residualWeight + neutralWeight;',
                '  if (totalWeight <= 0.0001) { gl_FragColor = cncClassColor(nearestClass); return; }',
                '  float dominantClass = 4.0;',
                '  float dominantWeight = neutralWeight;',
                '  if (residualWeight > dominantWeight) { dominantClass = 3.0; dominantWeight = residualWeight; }',
                '  if (blockedWeight > dominantWeight) { dominantClass = 2.0; dominantWeight = blockedWeight; }',
                '  if (reachableWeight > dominantWeight) { dominantClass = 1.0; dominantWeight = reachableWeight; }',
                '  vec4 selected = cncClassColor(dominantClass);',
                '  float accessWeight = reachableWeight + blockedWeight;',
                '  if (accessWeight > 0.0001 && accessWeight >= residualWeight && accessWeight >= neutralWeight) {',
                '    float accessBalance = reachableWeight / accessWeight;',
                '    float edgeWidth = max(fwidth(accessBalance) * 0.55, 0.003);',
                '    selected = mix(cncClassColor(2.0), cncClassColor(1.0),',
                '        smoothstep(0.5 - edgeWidth, 0.5 + edgeWidth, accessBalance));',
                '  }',
                '  gl_FragColor = selected;',
                '}'
            ].join('\n'),
            extensions: { derivatives: true },
            transparent: false,
            depthWrite: true,
            depthTest: true,
            side: THREE.DoubleSide,
            polygonOffset: true,
            polygonOffsetFactor: 1,
            polygonOffsetUnits: 1
        });
    }

    function CreateCncFieldTextureOverlay(classification, field, selectedSetups) {
        if (!displayObject) { return null; }
        displayObject.updateMatrixWorld(true);
        var worldToPart = new THREE.Matrix4().copy(displayObject.matrixWorld).invert();
        var group = new THREE.Group();
        group.userData.cncFieldTexture = classification.texture;
        displayObject.traverse(function (child) {
            if (!child.isMesh || !child.geometry) { return; }
            var material = CreateCncFieldTextureMaterial(classification, field, worldToPart);
            var mesh = new THREE.Mesh(child.geometry, material);
            mesh.matrixAutoUpdate = false;
            mesh.matrix.copy(child.matrixWorld);
            mesh.renderOrder = 4;
            group.add(mesh);
            if (IsCadModelInfo(activeModelInfo)) {
                var previewEdges = child.children.find(function (candidate) {
                    return candidate.userData && candidate.userData.cncCadFeatureEdges;
                });
                var edges = CreateCadFeatureEdges(child.geometry, previewEdges && previewEdges.geometry);
                if (edges) {
                    edges.matrixAutoUpdate = false;
                    edges.matrix.copy(child.matrixWorld);
                    group.add(edges);
                }
            }
        });
        if (group.children.length > 0) { displayObject.visible = false; }
        return group;
    }

    function BuildCncOccupancyMask(field) {
        var dimensions = field.dimensions || {};
        var totalCells = (Number(dimensions.x) || 0) * (Number(dimensions.y) || 0)
            * (Number(dimensions.z) || 0);
        if (totalCells <= 0) { return null; }
        var occupancy = new Uint8Array(totalCells);
        (Array.isArray(field.occupancyRuns) ? field.occupancyRuns : []).forEach(function (run) {
            var start = Math.max(0, Number(run[0]) || 0);
            var end = Math.min(totalCells, start + Math.max(0, Number(run[1]) || 0));
            occupancy.fill(1, start, end);
        });
        return occupancy;
    }

    // Greedy voxel meshing turns the carved-stock occupancy into one continuous surface.
    // Coplanar cell faces are merged into rectangles, avoiding both per-sample dots and the
    // browser cost of emitting six faces for every occupied field cell.
    function CreateCncGreedyFieldGeometry(field, occupancy, surfaceMask) {
        var dimensions = [Number(field.dimensions.x) || 0, Number(field.dimensions.y) || 0,
            Number(field.dimensions.z) || 0];
        var positions = [];
        var fieldOrigin = new THREE.Vector3(Number(field.origin.x) || 0,
            Number(field.origin.y) || 0, Number(field.origin.z) || 0);
        var fieldAxes = [CncVector(field.axes[0]) || new THREE.Vector3(1, 0, 0),
            CncVector(field.axes[1]) || new THREE.Vector3(0, 1, 0),
            CncVector(field.axes[2]) || new THREE.Vector3(0, 0, 1)];
        var fieldCellSize = Number(field.cellSizeMm) || 1;
        function gridPoint(x, y, z) {
            return fieldOrigin.clone()
                .addScaledVector(fieldAxes[0], x * fieldCellSize)
                .addScaledVector(fieldAxes[1], y * fieldCellSize)
                .addScaledVector(fieldAxes[2], z * fieldCellSize);
        }
        function occupied(x, y, z) {
            return x >= 0 && y >= 0 && z >= 0 && x < dimensions[0] && y < dimensions[1]
                && z < dimensions[2] && occupancy[x + dimensions[0] * (y + dimensions[1] * z)] === 1;
        }
        function pushTriangle(a, b, c) {
            positions.push(a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z);
        }
        for (var axis = 0; axis < 3; axis++) {
            var u = (axis + 1) % 3;
            var v = (axis + 2) % 3;
            var mask = new Int8Array(dimensions[u] * dimensions[v]);
            for (var slice = -1; slice < dimensions[axis]; slice++) {
                for (var j = 0; j < dimensions[v]; j++) {
                    for (var i = 0; i < dimensions[u]; i++) {
                        var beforeOccupied;
                        var afterOccupied;
                        var beforeIndex = -1;
                        var afterIndex = -1;
                        if (axis === 0) {
                            beforeOccupied = occupied(slice, i, j);
                            afterOccupied = occupied(slice + 1, i, j);
                            if (beforeOccupied) { beforeIndex = slice + dimensions[0] * (i + dimensions[1] * j); }
                            if (afterOccupied) { afterIndex = slice + 1 + dimensions[0] * (i + dimensions[1] * j); }
                        } else if (axis === 1) {
                            beforeOccupied = occupied(j, slice, i);
                            afterOccupied = occupied(j, slice + 1, i);
                            if (beforeOccupied) { beforeIndex = j + dimensions[0] * (slice + dimensions[1] * i); }
                            if (afterOccupied) { afterIndex = j + dimensions[0] * (slice + 1 + dimensions[1] * i); }
                        } else {
                            beforeOccupied = occupied(i, j, slice);
                            afterOccupied = occupied(i, j, slice + 1);
                            if (beforeOccupied) { beforeIndex = i + dimensions[0] * (j + dimensions[1] * slice); }
                            if (afterOccupied) { afterIndex = i + dimensions[0] * (j + dimensions[1] * (slice + 1)); }
                        }
                        var surfaceIndex = beforeOccupied ? beforeIndex : afterIndex;
                        mask[i + dimensions[u] * j] = beforeOccupied === afterOccupied
                            || (surfaceMask && surfaceMask[surfaceIndex] !== 1)
                            ? 0 : (beforeOccupied ? 1 : -1);
                    }
                }
                for (var row = 0; row < dimensions[v]; row++) {
                    for (var column = 0; column < dimensions[u];) {
                        var maskIndex = column + dimensions[u] * row;
                        var direction = mask[maskIndex];
                        if (direction === 0) { column++; continue; }
                        var width = 1;
                        while (column + width < dimensions[u]
                            && mask[maskIndex + width] === direction) { width++; }
                        var height = 1;
                        var canGrow = true;
                        while (row + height < dimensions[v] && canGrow) {
                            for (var widthIndex = 0; widthIndex < width; widthIndex++) {
                                if (mask[column + widthIndex + dimensions[u] * (row + height)] !== direction) {
                                    canGrow = false;
                                    break;
                                }
                            }
                            if (canGrow) { height++; }
                        }
                        var point = [0, 0, 0];
                        point[axis] = slice + 1;
                        point[u] = column;
                        point[v] = row;
                        var a = gridPoint(point[0], point[1], point[2]);
                        point[u] += width;
                        var b = gridPoint(point[0], point[1], point[2]);
                        point[v] += height;
                        var c = gridPoint(point[0], point[1], point[2]);
                        point[u] -= width;
                        var d = gridPoint(point[0], point[1], point[2]);
                        if (direction > 0) {
                            pushTriangle(a, b, c); pushTriangle(a, c, d);
                        } else {
                            pushTriangle(a, c, b); pushTriangle(a, d, c);
                        }
                        for (var clearY = 0; clearY < height; clearY++) {
                            for (var clearX = 0; clearX < width; clearX++) {
                                mask[column + clearX + dimensions[u] * (row + clearY)] = 0;
                            }
                        }
                        column += width;
                    }
                }
            }
        }
        if (positions.length === 0) { return null; }
        var geometry = new THREE.BufferGeometry();
        geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
        geometry.computeVertexNormals();
        return geometry;
    }

    function CreateCncResidualVolumeOverlay(residual, field) {
        if (!field.origin || !Array.isArray(field.axes) || field.axes.length !== 3
            || !Array.isArray(field.occupancyRuns) || field.occupancyRuns.length === 0) {
            return null;
        }
        var residualIds = new Set(Array.isArray(residual.cutterLimitedSampleIds)
            ? residual.cutterLimitedSampleIds.map(Number) : []);
        if (residualIds.size === 0) { return null; }
        var samples = field.surfaceSamples.filter(function (sample) {
            return residualIds.has(Number(sample.id)) && CncVector(sample.normal);
        });
        if (samples.length === 0) { return null; }

        var occupancy = BuildCncOccupancyMask(field);
        if (!occupancy) { return null; }
        var addedMaterial = new Uint8Array(occupancy.length);
        var addedCellCount = 0;
        var dimensions = field.dimensions;
        var cellSize = Math.max(0.02, Number(field.cellSizeMm) || 0.5);
        var radiusCells = Math.max(1, Math.min(8,
            Math.ceil((Number(residual.cutterRadiusMm) || cellSize) / cellSize)));
        var axes = field.axes.map(function (axis) { return CncVector(axis); });
        samples.forEach(function (sample) {
            var seed = CncFieldCellCoordinates(Number(sample.id), dimensions);
            var normal = CncVector(sample.normal);
            var gridNormal = [normal.dot(axes[0]), normal.dot(axes[1]), normal.dot(axes[2])];
            for (var z = -radiusCells; z <= radiusCells; z++) {
                for (var y = -radiusCells; y <= radiusCells; y++) {
                    for (var x = -radiusCells; x <= radiusCells; x++) {
                        var projection = x * gridNormal[0] + y * gridNormal[1] + z * gridNormal[2];
                        if (projection < 0.35 || (x * x + y * y + z * z) > radiusCells * radiusCells) { continue; }
                        var targetX = seed.x + x;
                        var targetY = seed.y + y;
                        var targetZ = seed.z + z;
                        if (targetX < 0 || targetY < 0 || targetZ < 0 || targetX >= dimensions.x
                            || targetY >= dimensions.y || targetZ >= dimensions.z) { continue; }
                        var targetIndex = targetX + dimensions.x * (targetY + dimensions.y * targetZ);
                        if (occupancy[targetIndex] === 0) {
                            occupancy[targetIndex] = 1;
                            addedMaterial[targetIndex] = 1;
                            addedCellCount++;
                        }
                    }
                }
            }
        });
        if (addedCellCount === 0) { return null; }
        var geometry = CreateCncGreedyFieldGeometry(field, occupancy, addedMaterial);
        if (!geometry) { return null; }
        var group = new THREE.Group();
        var mesh = new THREE.Mesh(geometry, new THREE.MeshPhongMaterial({
            color: 0xd98c10,
            shininess: 18,
            side: THREE.DoubleSide,
            flatShading: true
        }));
        displayObject.updateMatrixWorld(true);
        mesh.matrixAutoUpdate = false;
        mesh.matrix.copy(displayObject.matrixWorld);
        mesh.renderOrder = 4;
        group.add(mesh);
        cncRenderedResidualTriangleCount = geometry.attributes.position.count / 3;
        cncResidualMeshCount = 1;
        cncResidualConnectedComponentCount = Math.max(1, Number(activeModelInfo && activeModelInfo.bodyCount) || 1);
        group.updateMatrixWorld(true);
        var bounds = new THREE.Box3().setFromObject(group);
        var center = bounds.getCenter(new THREE.Vector3());
        var size = bounds.getSize(new THREE.Vector3());
        cncResidualWorldBounds = {
            center: { x: center.x, y: center.y, z: center.z },
            size: { x: size.x, y: size.y, z: size.z }
        };
        return group;
    }

    function CreateCncFieldSurfaceOverlays(plan, state) {
        var residual = plan.residual || {};
        var field = residual.accessibilityField;
        if (!field || field.degraded === true || !Array.isArray(field.surfaceSamples)
            || field.surfaceSamples.length === 0 || !isFinite(Number(field.cellSizeMm))) {
            return false;
        }
        var selectedIds = Array.isArray(state.selectedSetupIds) ? state.selectedSetupIds : [];
        var selectedSetups = (plan.setups || []).filter(function (setup) {
            return selectedIds.indexOf(String(setup.id || ('setup-' + setup.number))) >= 0;
        });
        var reachable = new Set();
        selectedSetups.forEach(function (setup) {
            (Array.isArray(setup.coveredSampleIds) ? setup.coveredSampleIds : []).forEach(function (id) {
                reachable.add(id);
            });
        });
        var clusters = Array.isArray(residual.surfaceClusters) ? residual.surfaceClusters : [];
        var clustersById = new Map();
        clusters.forEach(function (cluster) { clustersById.set(String(cluster.id), cluster); });
        function isAnalyticAxialCluster(cluster, setup) {
            if (!cluster || (cluster.type !== 'cylindrical' && cluster.type !== 'conical') || !cluster.axis) {
                return false;
            }
            var axis = CncVector(cluster.axis);
            var direction = CncVector(setup.direction || setup.toolDirection);
            return axis && direction && axis.lengthSq() > 1e-8 && direction.lengthSq() > 1e-8
                && Math.abs(axis.normalize().dot(direction.normalize())) >= 0.80;
        }
        var coherentReachableClusterIds = new Set();
        selectedSetups.forEach(function (setup) {
            (Array.isArray(setup.reachableClusterIds) ? setup.reachableClusterIds : []).forEach(function (id) {
                var cluster = clustersById.get(String(id));
                if (isAnalyticAxialCluster(cluster, setup)) { coherentReachableClusterIds.add(String(id)); }
            });
            (Array.isArray(residual.axialFluteClusterIds) ? residual.axialFluteClusterIds : []).forEach(function (id) {
                var cluster = clustersById.get(String(id));
                if (isAnalyticAxialCluster(cluster, setup)) { coherentReachableClusterIds.add(String(id)); }
            });
        });
        field.surfaceSamples.forEach(function (sample) {
            if (coherentReachableClusterIds.has(String(sample.clusterId))) { reachable.add(sample.id); }
        });
        var reachableClusterIds = new Set();
        if (selectedSetups.length > 0) {
            field.surfaceSamples.forEach(function (sample) {
                if (reachable.has(sample.id) && sample.clusterId !== null && sample.clusterId !== undefined) {
                    reachableClusterIds.add(String(sample.clusterId));
                }
            });
        }
        cncHighlightedClusterCount = reachableClusterIds.size;
        var unmachinable = new Set(Array.isArray(residual.unmachinableSampleIds)
            ? residual.unmachinableSampleIds : []);
        var residualClusters = new Set(Array.isArray(residual.clusterIds) ? residual.clusterIds : []);
        var residualVolume = state.residualVisible ? CreateCncResidualVolumeOverlay(residual, field) : null;
        cncOverlaySampleCount = field.surfaceSamples.length;
        cncOverlayFieldChecksum = field.checksum || null;
        cncOverlayFieldOrigin = {
            x: Number(field.origin && field.origin.x) || 0,
            y: Number(field.origin && field.origin.y) || 0,
            z: Number(field.origin && field.origin.z) || 0
        };
        cncSurfaceHighlightMode = 'field-texture';
        cncFieldClassificationMode = 'pending';
        cncFieldSamplingMode = coherentReachableClusterIds.size > 0
            ? 'analytic-cluster-coherent' : 'surface-normal-cell-vote';
        cncResidualGeometryAvailable = !!residualVolume || unmachinable.size > 0 || residualClusters.size > 0;
        cncResidualMode = residualVolume ? 'stock-carve-surface'
            : (cncResidualGeometryAvailable ? 'field-texture' : 'unavailable');
        cncOverlayWorkload = {
            triangleCount: 0,
            clusterCount: 0,
            triangleClusterComparisons: 0,
            sampleCount: cncOverlaySampleCount
        };
        // Keep upload and part switching responsive. The spatial analysis is already
        // complete; surfel geometry is only needed once an overlay becomes visible.
        if (selectedSetups.length === 0 && !state.residualVisible) {
            return true;
        }
        if (state.residualVisible && residualVolume) {
            cncResidualOverlay = residualVolume;
            scene.add(cncResidualOverlay);
            return true;
        }
        field.surfaceSamples.forEach(function (sample) {
            var isReachable = selectedSetups.length > 0 && reachable.has(sample.id);
            if (isReachable) { cncRenderedHighlightedSampleCount += 1; }
            else { cncRenderedDimmedSampleCount += 1; }
        });
        var classification = BuildCncFieldClassificationTexture(field, reachable, unmachinable,
            residualClusters, selectedSetups.length > 0, state.residualVisible);
        if (!classification) {
            cncSurfaceHighlightMode = 'unavailable-field-texture';
            cncOverlayUnavailableReason = 'field-texture-unavailable';
            return true;
        }
        cncFieldClassificationMode = classification.mode;
        var overlay = CreateCncFieldTextureOverlay(classification, field, selectedSetups);
        if (!overlay) { return true; }
        cncOverlayDrawCallCount = overlay.children.length;
        cncCadEdgeDrawCallCount = overlay.children.filter(function (child) {
            return child.userData && child.userData.cncCadFeatureEdges;
        }).length;
        cncCadEdgeMode = cncCadEdgeDrawCallCount > 0 ? 'feature-edges' : 'none';
        if (selectedSetups.length > 0) {
            cncReachOverlay = overlay;
            scene.add(cncReachOverlay);
            if (residualVolume) {
                cncResidualOverlay = residualVolume;
                scene.add(cncResidualOverlay);
            }
        } else {
            cncResidualOverlay = overlay;
            scene.add(cncResidualOverlay);
        }
        return true;
    }

    function YieldCncOverlayFrame(generation) {
        return new Promise(function (resolve) {
            requestAnimationFrame(function () {
                if (generation === cncOverlayBuildGeneration) { cncOverlayBuildFrameCount += 1; }
                resolve(generation === cncOverlayBuildGeneration);
            });
        });
    }

    async function CopyCncOverlayTypedArray(source, generation) {
        if (!source || source.length === 0) { return null; }
        var copy = new source.constructor(source.length);
        var chunkSize = 65536;
        for (var offset = 0; offset < source.length; offset += chunkSize) {
            copy.set(source.subarray(offset, Math.min(source.length, offset + chunkSize)), offset);
            if (offset + chunkSize < source.length && !(await YieldCncOverlayFrame(generation))) { return null; }
        }
        return copy;
    }

    async function CopyCncOverlayIndexes(source, generation) {
        if (!source || source.length === 0) { return new Uint32Array(0); }
        var copy = new Uint32Array(source.length);
        var chunkSize = 65536;
        for (var offset = 0; offset < source.length; offset += chunkSize) {
            copy.set(source.slice(offset, Math.min(source.length, offset + chunkSize)), offset);
            if (offset + chunkSize < source.length && !(await YieldCncOverlayFrame(generation))) { return null; }
        }
        return copy;
    }

    async function CreateCncOverlayWorkerRequest(clusters, selectedSetups, reachableIds, residualIds,
        fluteReachBySetupCluster, generation) {
        if (!displayObject) { return null; }
        displayObject.updateMatrixWorld(true);
        var meshes = [];
        var transfer = [];
        var meshChildren = [];
        displayObject.traverse(function (child) {
            if (child.isMesh && child.geometry && child.geometry.attributes && child.geometry.attributes.position) {
                meshChildren.push(child);
            }
        });
        for (var meshIndex = 0; meshIndex < meshChildren.length; meshIndex++) {
            var child = meshChildren[meshIndex];
            var position = await CopyCncOverlayTypedArray(child.geometry.attributes.position.array, generation);
            if (!position || generation !== cncOverlayBuildGeneration) { return null; }
            var index = child.geometry.index
                ? await CopyCncOverlayTypedArray(child.geometry.index.array, generation) : null;
            if (generation !== cncOverlayBuildGeneration) { return null; }
            var matrix = new Float32Array(child.matrixWorld.elements);
            meshes.push({ position: position, index: index, matrix: matrix });
            transfer.push(position.buffer);
            if (index) { transfer.push(index.buffer); }
            transfer.push(matrix.buffer);
            if (!(await YieldCncOverlayFrame(generation))) { return null; }
        }
        var workerClusters = [];
        for (var clusterIndex = 0; clusterIndex < clusters.length; clusterIndex++) {
            var cluster = clusters[clusterIndex];
            var triangleIndexes = await CopyCncOverlayIndexes(cluster.triangleIndexes, generation);
            if (!triangleIndexes || generation !== cncOverlayBuildGeneration) { return null; }
            var directional = null;
            if (cluster.accessibleTriangleIndexesByDirection
                && typeof cluster.accessibleTriangleIndexesByDirection === 'object') {
                directional = {};
                var setupIds = Object.keys(cluster.accessibleTriangleIndexesByDirection);
                for (var setupIndex = 0; setupIndex < setupIds.length; setupIndex++) {
                    var setupId = setupIds[setupIndex];
                    var directionalIndexes = await CopyCncOverlayIndexes(
                        cluster.accessibleTriangleIndexesByDirection[setupId], generation);
                    if (!directionalIndexes || generation !== cncOverlayBuildGeneration) { return null; }
                    directional[setupId] = directionalIndexes;
                    transfer.push(directionalIndexes.buffer);
                }
            }
            workerClusters.push({ id: cluster.id, triangleIndexes: triangleIndexes,
                accessibleTriangleIndexesByDirection: directional });
            transfer.push(triangleIndexes.buffer);
            if (!(await YieldCncOverlayFrame(generation))) { return null; }
        }
        return {
            message: {
                action: 'classify-cnc-overlay', meshes: meshes, clusters: workerClusters,
                selectedSetups: selectedSetups.map(function (setup) {
                    return { id: String(setup.id || ('setup-' + setup.number)),
                        direction: setup.direction || setup.toolDirection || null };
                }),
                reachableIds: reachableIds.map(String), residualIds: residualIds.map(String),
                fluteReachKeys: Array.from(fluteReachBySetupCluster)
            },
            transfer: transfer
        };
    }

    async function ApplyCncOverlayWorkerResult(overlay, state, selectedSetups, generation) {
        if (!overlay || generation !== cncOverlayBuildGeneration) { return false; }
        var dimmed = overlay.dimmed || new Float32Array(0);
        var highlighted = overlay.highlighted || new Float32Array(0);
        var residualPositions = overlay.residual || new Float32Array(0);
        var reachOverlay = new THREE.Group();
        var residualOverlay = new THREE.Group();
        AddCncOverlayMesh(reachOverlay, dimmed, 0xe53935, 1);
        if (!(await YieldCncOverlayFrame(generation))) { DisposeOverlay(reachOverlay); return false; }
        AddCncOverlayMesh(reachOverlay, highlighted, 0x18c95a, 1);
        if (!(await YieldCncOverlayFrame(generation))) { DisposeOverlay(reachOverlay); return false; }
        AddCncOverlayMesh(residualOverlay, residualPositions, 0xd98c10, 0.78);
        if (!(await YieldCncOverlayFrame(generation))) {
            DisposeOverlay(reachOverlay);
            DisposeOverlay(residualOverlay);
            return false;
        }
        cncRenderedHighlightedTriangleCount = highlighted.length / 9;
        cncRenderedDimmedTriangleCount = dimmed.length / 9;
        cncRenderedResidualTriangleCount = residualPositions.length / 9;
        cncOverlayTriangleCount = cncRenderedHighlightedTriangleCount + cncRenderedDimmedTriangleCount;
        cncResidualGeometryAvailable = cncRenderedResidualTriangleCount > 0;
        cncResidualMode = cncResidualGeometryAvailable ? 'cluster-faces' : 'unavailable';
        cncOverlayDrawCallCount = (dimmed.length > 0 ? 1 : 0) + (highlighted.length > 0 ? 1 : 0)
            + (residualPositions.length > 0 ? 1 : 0);
        cncCadEdgeDrawCallCount = 0;
        if (selectedSetups.length > 0 && cncModelTriangleCount <= 100000) {
            cncCadEdgeDrawCallCount = AddCncCadEdgesToOverlay(reachOverlay);
            cncCadEdgeMode = cncCadEdgeDrawCallCount > 0 ? 'feature-edges' : 'none';
        } else if (selectedSetups.length > 0) {
            cncCadEdgeMode = 'omitted-large-overlay';
        }
        reachOverlay.visible = selectedSetups.length > 0;
        residualOverlay.visible = state.residualVisible || selectedSetups.length > 0;
        cncReachOverlay = reachOverlay;
        cncResidualOverlay = residualOverlay;
        scene.add(cncReachOverlay);
        scene.add(cncResidualOverlay);
        if (displayObject && selectedSetups.length > 0) { displayObject.visible = false; }
        return true;
    }

    function StartCncTrustedOverlayBuild(clusters, selectedSetups, reachableIds, residualIds,
        fluteReachBySetupCluster, state) {
        CancelCncOverlayBuild(false);
        var generation = cncOverlayBuildGeneration;
        cncOverlayBuildPending = true;
        cncOverlayWorkerUsed = true;
        cncOverlayBuildFrameCount = 0;
        cncSurfaceHighlightMode = 'worker-pending';
        CreateCncOverlayWorkerRequest(clusters, selectedSetups, reachableIds, residualIds,
            fluteReachBySetupCluster, generation).then(function (request) {
            if (!request || generation !== cncOverlayBuildGeneration) { return null; }
            var submitted = ModelOverlayWorkerManager.Submit(request.message, request.transfer);
            activeCncOverlayJobId = submitted.jobId;
            return submitted.promise;
        }).then(async function (result) {
            if (!result || generation !== cncOverlayBuildGeneration) { return; }
            activeCncOverlayJobId = null;
            await ApplyCncOverlayWorkerResult(result.overlay, state, selectedSetups, generation);
            if (generation !== cncOverlayBuildGeneration) { return; }
            cncSurfaceHighlightMode = 'worker-cluster-faces';
        }).catch(function (error) {
            if (generation !== cncOverlayBuildGeneration || (error && error.message === 'Cancelled.')) { return; }
            activeCncOverlayJobId = null;
            cncOverlayUnavailableReason = 'overlay-worker-failed';
            cncSurfaceHighlightMode = 'unavailable-worker';
            if (window.console && typeof window.console.error === 'function') {
                window.console.error('Unable to build the CNC surface overlay.', error);
            }
        }).finally(function () {
            if (generation !== cncOverlayBuildGeneration) { return; }
            cncOverlayBuildPending = false;
            ReconcileCncRenderedAvailability(state);
            NotifyCncOverlayState();
        });
    }

    function CreateCncSurfaceOverlays(plan, state, maxDimension) {
        var residual = plan.residual || {};
        var clusters = Array.isArray(residual.surfaceClusters) ? residual.surfaceClusters : [];
        var cadTopology = CncCadTopologyProfile(displayObject);
        var hasTrustedCadFaces = IsCadModelInfo(activeModelInfo) && cadTopology.hasTrustedBrepFaces
            && clusters.length > 0
            && clusters.every(function (cluster) { return Array.isArray(cluster.triangleIndexes); });
        if (IsCadModelInfo(activeModelInfo) && cadTopology.meshDerived) {
            cncModelTriangleCount = cadTopology.triangleCount;
            cncOverlayWorkload = {
                triangleCount: cadTopology.triangleCount,
                clusterCount: clusters.length,
                triangleClusterComparisons: cadTopology.triangleCount
            };
            cncOverlayUnavailableReason = 'performance-budget-mesh-derived-cad';
            cncSetupSelectionAvailable = false;
            cncResidualGeometryAvailable = false;
            cncResidualMode = 'unavailable-performance-budget';
            cncSurfaceHighlightMode = 'unavailable-performance-budget';
            state.residualVisible = false;
            ClearCncSelectedSetup(state);
            return;
        }
        // The fine spatial field remains the machining-access authority. For a STEP/IGES
        // source, present its result on coherent CAD faces so sparse voxels are not interpolated
        // into stripes across an otherwise flat or cylindrical surface.
        if (!hasTrustedCadFaces && CreateCncFieldSurfaceOverlays(plan, state)) { return; }
        var topologyDegraded = residual.topologyDegraded === true;
        var selectedIds = Array.isArray(state.selectedSetupIds) ? state.selectedSetupIds : [];
        var selectedSetups = (plan.setups || []).filter(function (setup) {
            return selectedIds.indexOf(String(setup.id || ('setup-' + setup.number))) >= 0;
        });
        var reachableIds = [];
        selectedSetups.forEach(function (setup) {
            (Array.isArray(setup.reachableClusterIds) ? setup.reachableClusterIds : []).forEach(function (id) {
                if (reachableIds.indexOf(id) === -1) { reachableIds.push(id); }
            });
            var setupDirection = CncVector(setup.direction || setup.toolDirection);
            (Array.isArray(residual.axialFluteClusterIds) ? residual.axialFluteClusterIds : []).forEach(function (id) {
                var cluster = clusters.find(function (candidate) { return String(candidate.id) === String(id); });
                var axis = cluster && (cluster.type === 'cylindrical' || cluster.type === 'conical')
                    ? CncVector(cluster.axis) : null;
                if (axis && setupDirection && Math.abs(axis.dot(setupDirection)) >= 0.80
                    && reachableIds.indexOf(id) === -1) {
                    reachableIds.push(id);
                }
            });
        });
        if (selectedSetups.length > 0) {
            (plan.reachMatrix || []).forEach(function (record) {
                var matchesSetup = selectedIds.indexOf(String(record.setupId || ('setup-' + record.setupNumber))) >= 0;
                if (matchesSetup && record.reachable === true && reachableIds.indexOf(record.clusterId) === -1) {
                    reachableIds.push(record.clusterId);
                }
            });
        }
        var residualIds = Array.isArray(residual.clusterIds) ? residual.clusterIds : [];
        cncHighlightedClusterCount = topologyDegraded ? 0 : reachableIds.filter(function (id, index) {
            return reachableIds.indexOf(id) === index && clusters.some(function (cluster) { return cluster.id === id; });
        }).length;
        cncResidualGeometryAvailable = !topologyDegraded && residualIds.some(function (id) {
            return clusters.some(function (cluster) { return cluster.id === id; });
        });
        cncResidualMode = topologyDegraded ? 'surface-only-degraded'
            : (cncResidualGeometryAvailable ? 'cluster-faces' : 'unavailable');
        cncSurfaceHighlightMode = topologyDegraded || clusters.length === 0 ? 'unavailable' : 'cluster-faces';
        if (!displayObject || topologyDegraded || clusters.length === 0) {
            return;
        }

        var triangleTotal = 0;
        displayObject.traverse(function (child) {
            if (!child.isMesh || !child.geometry || !child.geometry.attributes || !child.geometry.attributes.position) { return; }
            var count = child.geometry.index ? child.geometry.index.count : child.geometry.attributes.position.count;
            triangleTotal += Math.floor(count / 3);
        });
        cncModelTriangleCount = triangleTotal;
        var exactTriangleMembership = clusters.length > 0 && clusters.every(function (cluster) {
            return Array.isArray(cluster.triangleIndexes);
        });
        var directionalTriangleMembership = exactTriangleMembership && clusters.some(function (cluster) {
            return cluster.accessibleTriangleIndexesByDirection
                && typeof cluster.accessibleTriangleIndexesByDirection === 'object';
        });
        if (directionalTriangleMembership) { cncSurfaceHighlightMode = 'directional-triangle-faces'; }
        var comparisonTotal = exactTriangleMembership ? triangleTotal : triangleTotal * clusters.length;
        cncOverlayWorkload = {
            triangleCount: triangleTotal,
            clusterCount: clusters.length,
            triangleClusterComparisons: comparisonTotal
        };
        var workerEligible = hasTrustedCadFaces && exactTriangleMembership;
        var triangleLimit = workerEligible ? CNC_OVERLAY_LIMITS.maxTriangles : CNC_OVERLAY_LIMITS.maxSynchronousTriangles;
        var clusterLimit = workerEligible ? CNC_OVERLAY_LIMITS.maxTrustedClusters : CNC_OVERLAY_LIMITS.maxSynchronousClusters;
        var comparisonLimit = workerEligible ? CNC_OVERLAY_LIMITS.maxTriangleClusterComparisons : CNC_OVERLAY_LIMITS.maxSynchronousComparisons;
        if (triangleTotal > triangleLimit) {
            cncOverlayUnavailableReason = 'performance-budget-triangles';
        } else if (clusters.length > clusterLimit) {
            cncOverlayUnavailableReason = 'performance-budget-clusters';
        } else if (comparisonTotal > comparisonLimit) {
            cncOverlayUnavailableReason = 'performance-budget-comparisons';
        }
        if (cncOverlayUnavailableReason) {
            cncSetupSelectionAvailable = false;
            cncResidualGeometryAvailable = false;
            cncResidualMode = 'unavailable-performance-budget';
            cncSurfaceHighlightMode = 'unavailable-performance-budget';
            state.residualVisible = false;
            ClearCncSelectedSetup(state);
            return;
        }
        if (selectedSetups.length === 0 && !state.residualVisible) { return; }

        var fluteReachBySetupCluster = new Set();
        (plan.reachMatrix || []).forEach(function (record) {
            if (record.reachable !== true || Number(record.fluteSampleCount) <= 0) { return; }
            var recordSetupId = String(record.setupId || ('setup-' + record.setupNumber));
            fluteReachBySetupCluster.add(recordSetupId + '\u0000' + String(record.clusterId));
        });
        if (hasTrustedCadFaces && exactTriangleMembership
            && triangleTotal > CNC_OVERLAY_LIMITS.maxSynchronousTriangles) {
            StartCncTrustedOverlayBuild(clusters, selectedSetups, reachableIds, residualIds,
                fluteReachBySetupCluster, state);
            return;
        }

        displayObject.updateMatrixWorld(true);
        var worldClusters = clusters.map(CncClusterWorldEvidence);
        var exactClusterByTriangleIndex = new Map();
        if (exactTriangleMembership) {
            worldClusters.forEach(function (cluster) {
                cluster.source.triangleIndexes.forEach(function (triangleIndex) {
                    exactClusterByTriangleIndex.set(Number(triangleIndex), cluster);
                });
            });
        }
        var highlighted = [];
        var dimmed = [];
        var residualPositions = [];
        var a = new THREE.Vector3();
        var b = new THREE.Vector3();
        var c = new THREE.Vector3();
        var edgeOne = new THREE.Vector3();
        var edgeTwo = new THREE.Vector3();
        var triangleNormal = new THREE.Vector3();
        var triangleCentroid = new THREE.Vector3();
        var globalTriangleIndex = 0;
        displayObject.traverse(function (child) {
            if (!child.isMesh || !child.geometry || !child.geometry.attributes || !child.geometry.attributes.position) { return; }
            var position = child.geometry.attributes.position;
            var index = child.geometry.index;
            var count = index ? index.count : position.count;
            for (var offset = 0; offset + 2 < count; offset += 3) {
                var ia = index ? index.getX(offset) : offset;
                var ib = index ? index.getX(offset + 1) : offset + 1;
                var ic = index ? index.getX(offset + 2) : offset + 2;
                a.fromBufferAttribute(position, ia).applyMatrix4(child.matrixWorld);
                b.fromBufferAttribute(position, ib).applyMatrix4(child.matrixWorld);
                c.fromBufferAttribute(position, ic).applyMatrix4(child.matrixWorld);
                edgeOne.subVectors(b, a);
                edgeTwo.subVectors(c, a);
                triangleNormal.crossVectors(edgeOne, edgeTwo);
                if (triangleNormal.lengthSq() > 0) { triangleNormal.normalize(); }
                triangleCentroid.copy(a).add(b).add(c).multiplyScalar(1 / 3);
                var cluster = exactTriangleMembership ? exactClusterByTriangleIndex.get(globalTriangleIndex) : null;
                if (!cluster && !exactTriangleMembership) {
                    cluster = ClosestCncCluster(triangleCentroid, triangleNormal, worldClusters, maxDimension);
                }
                globalTriangleIndex += 1;
                if (!cluster) { continue; }
                var selectedSetupCanReach = reachableIds.indexOf(cluster.source.id) >= 0;
                if (selectedSetupCanReach && selectedSetups.length > 0
                    && cluster.source.accessibleTriangleIndexesByDirection
                    && typeof cluster.source.accessibleTriangleIndexesByDirection === 'object') {
                    selectedSetupCanReach = selectedSetups.some(function (setup) {
                        var setupId = String(setup.id || ('setup-' + setup.number));
                        var triangleIndexes = cluster.source.accessibleTriangleIndexesByDirection[setupId];
                        if (Array.isArray(triangleIndexes) && triangleIndexes.indexOf(globalTriangleIndex - 1) >= 0) {
                            return true;
                        }
                        // A directional set (including an empty one) distinguishes exposed triangles
                        // from body-occluded triangles inside this cluster. Do not let coarse
                        // cluster-level flute evidence promote the hidden opposite slot wall.
                        if (Array.isArray(triangleIndexes)) { return false; }
                        // The directional triangle projection is intentionally conservative and
                        // can omit an entire otherwise clear side-cut face. Recover tangent
                        // triangles only when projection supplied no per-triangle evidence for
                        // this setup and the tool field proves reachable flute contact.
                        var setupDirection = CncVector(setup.direction || setup.toolDirection);
                        return !!setupDirection
                            && Math.abs(triangleNormal.dot(setupDirection)) <= CNC_FLUTE_CONTACT_ALIGNMENT
                            && fluteReachBySetupCluster.has(setupId + '\u0000' + String(cluster.source.id));
                    });
                }
                var target = selectedSetupCanReach ? highlighted : dimmed;
                target.push(a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z);
                if (residualIds.indexOf(cluster.source.id) >= 0
                    && (selectedSetups.length === 0 || !selectedSetupCanReach)) {
                    residualPositions.push(a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z);
                }
                cncOverlayTriangleCount += 1;
            }
        });

        cncRenderedHighlightedTriangleCount = highlighted.length / 9;
        cncRenderedDimmedTriangleCount = dimmed.length / 9;
        cncRenderedResidualTriangleCount = residualPositions.length / 9;
        cncResidualGeometryAvailable = cncRenderedResidualTriangleCount > 0;
        cncResidualMode = cncResidualGeometryAvailable ? 'cluster-faces' : 'unavailable';
        cncOverlayDrawCallCount = (dimmed.length > 0 ? 1 : 0)
            + (highlighted.length > 0 ? 1 : 0)
            + (residualPositions.length > 0 ? 1 : 0);

        cncReachOverlay = new THREE.Group();
        AddCncOverlayMesh(cncReachOverlay, dimmed, 0xe53935, 1);
        AddCncOverlayMesh(cncReachOverlay, highlighted, 0x18c95a, 1);
        if (selectedSetups.length > 0) {
            cncCadEdgeDrawCallCount = AddCncCadEdgesToOverlay(cncReachOverlay);
            cncCadEdgeMode = cncCadEdgeDrawCallCount > 0 ? 'feature-edges' : 'none';
            displayObject.visible = false;
        }
        cncReachOverlay.visible = selectedSetups.length > 0;
        scene.add(cncReachOverlay);

        cncResidualOverlay = new THREE.Group();
        AddCncOverlayMesh(cncResidualOverlay, residualPositions, 0xd98c10, 0.78);
        cncResidualOverlay.visible = state.residualVisible || selectedSetups.length > 0;
        scene.add(cncResidualOverlay);
    }

    function ReconcileCncRenderedAvailability(state) {
        if (cncOverlayBuildPending) { return; }
        if (!cncResidualGeometryAvailable) { state.residualVisible = false; }
        var highlightedCount = cncSurfaceHighlightMode === 'field-surfels' || cncSurfaceHighlightMode === 'field-surface'
            || cncSurfaceHighlightMode === 'field-texture'
            ? cncRenderedHighlightedSampleCount : cncRenderedHighlightedTriangleCount;
        if (state.selectedSetupIds.length > 0 && highlightedCount === 0) {
            ClearCncSelectedSetup(state);
            cncSetupSelectionAvailable = false;
        }
        if (cncReachOverlay) { cncReachOverlay.visible = state.selectedSetupIds.length > 0; }
        if (cncResidualOverlay) { cncResidualOverlay.visible = state.residualVisible || state.selectedSetupIds.length > 0; }
    }

    function CncOverlayStateSnapshot() {
        var state = CurrentCncOverlayState();
        var residual = cncOverlayPlan && cncOverlayPlan.residual || {};
        var setupFaceMeshes = cncReachOverlay
            ? cncReachOverlay.children.filter(function (child) { return child.isMesh; }) : [];
        var cadEdgeLines = cncReachOverlay
            ? cncReachOverlay.children.filter(function (child) {
                return child.isLineSegments && child.userData && child.userData.cncCadFeatureEdges;
            }) : [];
        var setupLabelSprites = cncDirectionOverlay
            ? cncDirectionOverlay.children.filter(function (child) {
                return child.isSprite && child.userData && child.userData.cncSetupLabel;
            }) : [];
        return {
            partId: state.partId,
            stockVisible: state.stockVisible && !!cncStockOverlay,
            directionsVisible: state.directionsVisible && !!cncDirectionOverlay,
            residualVisible: state.residualVisible && !!cncOverlayPlan,
            selectedSetupIds: (state.selectedSetupIds || []).slice(),
            selectedSetupId: state.selectedSetupId,
            selectedSetupNumber: state.selectedSetupNumber,
            selectedSetupName: state.selectedSetupName,
            selectedSetupDirection: state.selectedSetupDirection || {},
            highlightedClusterCount: cncHighlightedClusterCount,
            setupLabelCount: cncSetupLabelCount,
            setupLabelsDepthTested: setupLabelSprites.length > 0
                && setupLabelSprites.every(function (sprite) { return sprite.material.depthTest === true; }),
            setupLabelsAvoidDepthWrites: setupLabelSprites.length > 0
                && setupLabelSprites.every(function (sprite) { return sprite.material.depthWrite === false; }),
            setupMarkers: cncDirectionOverlay && Array.isArray(cncDirectionOverlay.userData.cncSetupMarkers)
                ? cncDirectionOverlay.userData.cncSetupMarkers.map(function (marker) { return Object.assign({}, marker); })
                : [],
            visibleSetupIds: cncVisibleSetupIds.slice(),
            stockShape: cncStockMetadata && cncStockMetadata.shape || null,
            stockPrimitive: cncStockMetadata && cncStockMetadata.primitive || null,
            stockOpacity: cncStockMetadata && cncStockMetadata.opacity || 0,
            stockFilled: !!(cncStockMetadata && cncStockMetadata.filled),
            stockOutlineVisible: !!(cncStockMetadata && cncStockMetadata.outlineVisible),
            stockContainsPart: !!(cncStockMetadata && cncStockMetadata.containsPart),
            stockClippedVertexCount: cncStockMetadata && cncStockMetadata.clippedVertexCount || 0,
            stockDimensions: cncStockMetadata && cncStockMetadata.dimensions || {},
            stockOrientationFrame: cncStockMetadata && cncStockMetadata.orientationFrame || null,
            residualCamCertain: residual.camCertain === true,
            residualGeometryAvailable: cncResidualGeometryAvailable,
            setupSelectionAvailable: cncSetupSelectionAvailable,
            residualToggleAvailable: cncResidualGeometryAvailable,
            residualMode: cncResidualMode,
            topologyDegraded: residual.topologyDegraded === true,
            surfaceHighlightMode: cncSurfaceHighlightMode,
            overlayTriangleCount: cncOverlayTriangleCount,
            modelTriangleCount: cncModelTriangleCount,
            renderedHighlightedTriangleCount: cncRenderedHighlightedTriangleCount,
            renderedDimmedTriangleCount: cncRenderedDimmedTriangleCount,
            renderedResidualTriangleCount: cncRenderedResidualTriangleCount,
            residualMeshCount: cncResidualMeshCount,
            residualConnectedComponentCount: cncResidualConnectedComponentCount,
            residualUsesDiscreteSamplePrisms: false,
            residualWorldBounds: cncResidualWorldBounds && {
                center: Object.assign({}, cncResidualWorldBounds.center),
                size: Object.assign({}, cncResidualWorldBounds.size)
            },
            overlaySampleCount: cncOverlaySampleCount,
            renderedHighlightedSampleCount: cncRenderedHighlightedSampleCount,
            renderedDimmedSampleCount: cncRenderedDimmedSampleCount,
            overlayFieldChecksum: cncOverlayFieldChecksum,
            overlayFieldOrigin: cncOverlayFieldOrigin && Object.assign({}, cncOverlayFieldOrigin),
            overlayDrawCallCount: cncOverlayDrawCallCount,
            fieldClassificationMode: cncFieldClassificationMode,
            fieldSamplingMode: cncFieldSamplingMode,
            cadEdgeMode: cncCadEdgeMode,
            cadEdgeDrawCallCount: cncCadEdgeDrawCallCount,
            opaqueSetupFacesWriteDepth: setupFaceMeshes.length > 0
                && setupFaceMeshes.every(function (mesh) { return mesh.material.depthWrite === true; }),
            cadEdgesPreserveDepth: cadEdgeLines.length > 0
                && cadEdgeLines.every(function (line) {
                    return line.material.depthTest === true && line.material.depthWrite === false;
                }),
            overlayUnavailableReason: cncOverlayUnavailableReason,
            overlayBuildPending: cncOverlayBuildPending,
            overlayWorkerUsed: cncOverlayWorkerUsed,
            overlayBuildFrameCount: cncOverlayBuildFrameCount,
            sourceModelVisible: !displayObject || displayObject.visible,
            overlayLimits: {
                maxTriangles: CNC_OVERLAY_LIMITS.maxTriangles,
                maxTrustedClusters: CNC_OVERLAY_LIMITS.maxTrustedClusters,
                maxTriangleClusterComparisons: CNC_OVERLAY_LIMITS.maxTriangleClusterComparisons,
                maxSynchronousTriangles: CNC_OVERLAY_LIMITS.maxSynchronousTriangles,
                maxSynchronousClusters: CNC_OVERLAY_LIMITS.maxSynchronousClusters,
                maxSynchronousComparisons: CNC_OVERLAY_LIMITS.maxSynchronousComparisons
            },
            overlayWorkload: {
                triangleCount: cncOverlayWorkload.triangleCount,
                clusterCount: cncOverlayWorkload.clusterCount,
                triangleClusterComparisons: cncOverlayWorkload.triangleClusterComparisons
            },
            trackedPartStateCount: Object.keys(cncOverlayStateByPart).length
        };
    }

    function NotifyCncOverlayState() {
        canvasElement.dispatchEvent(new CustomEvent('cnc-overlay-state-changed', { detail: CncOverlayStateSnapshot() }));
    }

    function RebuildCncSelectionOverlays() {
        CancelCncOverlayBuild(true);
        if (displayObject) { displayObject.visible = true; }
        DisposeOverlay(cncReachOverlay);
        DisposeOverlay(cncResidualOverlay);
        cncReachOverlay = null;
        cncResidualOverlay = null;
        cncHighlightedClusterCount = 0;
        cncOverlayTriangleCount = 0;
        cncModelTriangleCount = 0;
        cncRenderedHighlightedTriangleCount = 0;
        cncRenderedDimmedTriangleCount = 0;
        cncRenderedResidualTriangleCount = 0;
        cncResidualMeshCount = 0;
        cncResidualConnectedComponentCount = 0;
        cncResidualWorldBounds = null;
        cncOverlaySampleCount = 0;
        cncRenderedHighlightedSampleCount = 0;
        cncRenderedDimmedSampleCount = 0;
        cncOverlayFieldChecksum = null;
        cncOverlayFieldOrigin = null;
        cncOverlayDrawCallCount = 0;
        cncFieldClassificationMode = 'none';
        cncFieldSamplingMode = 'none';
        cncCadEdgeMode = 'none';
        cncCadEdgeDrawCallCount = 0;
        cncOverlayUnavailableReason = null;
        cncOverlayWorkload = { triangleCount: 0, clusterCount: 0, triangleClusterComparisons: 0 };
        if (!cncOverlayPlan) { return; }
        var state = CurrentCncOverlayState();
        CreateCncSurfaceOverlays(cncOverlayPlan, state, cncOverlayPlan.maxDimension);
        ReconcileCncRenderedAvailability(state);
    }

    this.SetCncOverlayPlan = function (stock, setups, reachMatrix, residual) {
        ClearCncOverlays();
        residual = residual || {};
        cncOverlayPartId = residual.partId === undefined || residual.partId === null
            ? (stock && stock.partId !== undefined && stock.partId !== null ? String(stock.partId) : null)
            : String(residual.partId);
        cncOverlayPlan = null;
        if (!displayObject || !stock || !stock.stockSizeMm) { NotifyCncOverlayState(); return; }
        var state = CurrentCncOverlayState();
        var normalizedSetups = Array.isArray(setups) ? setups : [];
        var size = stock.stockSizeMm;
        var maxDimension = Math.max(Number(size.x) || 1, Number(size.y) || 1, Number(size.z) || 1);
        cncOverlayPlan = {
            stock: stock,
            setups: normalizedSetups,
            reachMatrix: Array.isArray(reachMatrix) ? reachMatrix : [],
            residual: residual,
            maxDimension: maxDimension
        };
        var clusters = Array.isArray(residual.surfaceClusters) ? residual.surfaceClusters : [];
        var clusterIds = clusters.map(function (cluster) { return cluster.id; });
        var fieldAvailable = residual.accessibilityField && residual.accessibilityField.degraded !== true
            && Array.isArray(residual.accessibilityField.surfaceSamples)
            && residual.accessibilityField.surfaceSamples.length > 0;
        function setupSelectable(setup) {
            return (fieldAvailable && Array.isArray(setup.coveredSampleIds) && setup.coveredSampleIds.length > 0)
                || (clusters.length > 0 && CncSetupHasReachEvidence(setup, cncOverlayPlan, clusterIds));
        }
        cncSetupSelectionAvailable = residual.topologyDegraded !== true
            && normalizedSetups.some(setupSelectable);
        var previousSelectedIds = Array.isArray(state.selectedSetupIds) ? state.selectedSetupIds.slice()
            : (state.selectedSetupId ? [state.selectedSetupId] : []);
        var hadSelection = previousSelectedIds.length > 0;
        if (!cncSetupSelectionAvailable) {
            ClearCncSelectedSetup(state);
        } else {
            state.selectedSetupIds = previousSelectedIds.filter(function (id) {
                var setup = normalizedSetups.find(function (candidate) {
                    return String(candidate.id || ('setup-' + candidate.number)) === String(id);
                });
                return setup && setupSelectable(setup);
            });
            RefreshCncSelectedSetupSummary(state, normalizedSetups, state.selectedSetupId);
        }
        if (cncSetupSelectionAvailable && hadSelection && state.selectedSetupIds.length === 0) {
            var fallbackIndex = normalizedSetups.findIndex(function (setup) {
                return setupSelectable(setup);
            });
            if (fallbackIndex >= 0) { ApplyCncSelectedSetup(state, normalizedSetups[fallbackIndex], fallbackIndex); }
            else { ClearCncSelectedSetup(state); }
        }
        if (residual.topologyDegraded === true || (!fieldAvailable && clusters.length === 0)) { state.residualVisible = false; }
        cncStockOverlay = CreateCncStockOverlay(stock, state);
        scene.add(cncStockOverlay);
        CreateCncSurfaceOverlays(cncOverlayPlan, state, maxDimension);
        ReconcileCncRenderedAvailability(state);
        cncDirectionOverlay = CreateCncDirectionOverlay(normalizedSetups, state, maxDimension, cncStockOverlay);
        scene.add(cncDirectionOverlay);
        NotifyCncOverlayState();
    };

    this.ToggleCncStockOverlay = function () {
        if (!cncOverlayPlan) { return false; }
        var state = CurrentCncOverlayState();
        state.stockVisible = !state.stockVisible;
        if (cncStockOverlay) { cncStockOverlay.visible = state.stockVisible; }
        NotifyCncOverlayState();
        return state.stockVisible;
    };

    this.ToggleCncDirectionOverlay = function () {
        if (!cncOverlayPlan) { return false; }
        var state = CurrentCncOverlayState();
        state.directionsVisible = !state.directionsVisible;
        if (cncDirectionOverlay) { cncDirectionOverlay.visible = state.directionsVisible; }
        NotifyCncOverlayState();
        return state.directionsVisible;
    };

    this.ToggleCncResidualOverlay = function () {
        if (!cncOverlayPlan || !cncResidualGeometryAvailable) {
            var unavailableState = CurrentCncOverlayState();
            unavailableState.residualVisible = false;
            NotifyCncOverlayState();
            return false;
        }
        var state = CurrentCncOverlayState();
        state.residualVisible = !state.residualVisible;
        RebuildCncSelectionOverlays();
        NotifyCncOverlayState();
        return state.residualVisible;
    };

    this.SelectCncSetup = function (numberOrId) {
        if (!cncOverlayPlan || !cncSetupSelectionAvailable) {
            NotifyCncOverlayState();
            return false;
        }
        var setup = cncOverlayPlan.setups.find(function (candidate) {
            return String(candidate.id || ('setup-' + candidate.number)) === String(numberOrId)
                || Number(candidate.number) === Number(numberOrId);
        });
        if (!setup) { return false; }
        var state = CurrentCncOverlayState();
        var setupId = String(setup.id || ('setup-' + setup.number));
        var selectedIndex = state.selectedSetupIds.indexOf(setupId);
        if (selectedIndex >= 0) { state.selectedSetupIds.splice(selectedIndex, 1); }
        else { state.selectedSetupIds.push(setupId); }
        RefreshCncSelectedSetupSummary(state, cncOverlayPlan.setups, selectedIndex >= 0 ? null : setupId);
        state.directionsVisible = true;
        RebuildCncSelectionOverlays();
        DisposeOverlay(cncDirectionOverlay);
        cncSetupLabelCount = 0;
        cncDirectionOverlay = CreateCncDirectionOverlay(
            cncOverlayPlan.setups, state, cncOverlayPlan.maxDimension, cncStockOverlay);
        scene.add(cncDirectionOverlay);
        NotifyCncOverlayState();
        return state.selectedSetupIds.length > 0;
    };

    this.ForgetCncOverlayState = function (partId) {
        var key = String(partId);
        delete cncOverlayStateByPart[key];
        if (cncOverlayPartId === key) {
            ClearCncOverlays();
            cncOverlayPlan = null;
            cncOverlayPartId = null;
            NotifyCncOverlayState();
        }
    };

    this.GetCncOverlayState = CncOverlayStateSnapshot;

    canvasElement.addEventListener('click', function (event) {
        if (!cncDirectionOverlay || !cncDirectionOverlay.visible) { return; }
        var bounds = canvasElement.getBoundingClientRect();
        if (!bounds.width || !bounds.height) { return; }
        var pointer = new THREE.Vector2(
            ((event.clientX - bounds.left) / bounds.width) * 2 - 1,
            -(((event.clientY - bounds.top) / bounds.height) * 2 - 1));
        var raycaster = new THREE.Raycaster();
        raycaster.params.Line.threshold = Math.max(0.5, cncOverlayPlan ? cncOverlayPlan.maxDimension * 0.025 : 1);
        raycaster.setFromCamera(pointer, camera);
        var hit = raycaster.intersectObjects(cncDirectionOverlay.children, true).find(function (entry) {
            return entry.object && entry.object.userData && entry.object.userData.cncSetupId;
        });
        if (hit) { this.SelectCncSetup(hit.object.userData.cncSetupId); }
    }.bind(this));

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
            if (child.isMesh || child.isLineSegments) {
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

    // Render a thumbnail for a parsed background part without swapping the shared scene or
    // camera. Multi-file completion order must not steal the customer's active canvas, but it
    // must not turn every non-active part into a false "Preview unavailable" warning either.
    this.SnapshotObject = function (object3D, modelInfo) {
        if (!object3D || !modelInfo) { return null; }
        var snapshotRenderer = null;
        try {
            var width = 240;
            var height = Math.round(width / canvasAspectRatio);
            var snapshotCanvas = document.createElement('canvas');
            var snapshotScene = new THREE.Scene();
            var snapshotCamera = new THREE.PerspectiveCamera(fieldOfView, width / height, nearDistance, farDistance);
            snapshotCamera.up.set(0, 0, 1);
            snapshotRenderer = new THREE.WebGLRenderer({
                canvas: snapshotCanvas,
                antialias: true,
                preserveDrawingBuffer: true,
                alpha: false
            });
            snapshotRenderer.setClearColor(thumbnailCanvasColor, 1);
            snapshotRenderer.setPixelRatio(1);
            snapshotRenderer.setSize(width, height, false);
            AddLights(snapshotScene);

            // Cloning the hierarchy keeps geometry and materials shared, while isolating the
            // centring transform needed for thumbnail framing from the live part object.
            var snapshotObject = object3D.clone(true);
            snapshotObject.position.set(
                -(modelInfo.min.x + modelInfo.size.x * 0.5),
                -(modelInfo.min.y + modelInfo.size.y * 0.5),
                -(modelInfo.min.z + modelInfo.size.z * 0.5));
            snapshotScene.add(snapshotObject);
            snapshotObject.updateMatrixWorld(true);

            var bounds = new THREE.Box3().setFromObject(snapshotObject);
            var sphere = bounds.getBoundingSphere(new THREE.Sphere());
            var maxXYZ = Math.max(modelInfo.size.x, modelInfo.size.y, modelInfo.size.z, 1);
            var verticalFov = THREE.MathUtils.degToRad(fieldOfView);
            var horizontalFov = 2 * Math.atan(Math.tan(verticalFov / 2) * snapshotCamera.aspect);
            var radius = Math.max(sphere.radius, maxXYZ * 0.01, 1);
            var distance = radius / Math.sin(Math.min(verticalFov, horizontalFov) / 2) * 1.22;
            var cameraDirection = new THREE.Vector3(0.9, -0.9, 0.72).normalize();
            snapshotCamera.position.copy(sphere.center).addScaledVector(cameraDirection, distance);
            snapshotCamera.lookAt(sphere.center);
            snapshotRenderer.render(snapshotScene, snapshotCamera);

            var data = snapshotCanvas.toDataURL('image/png');
            return data === 'data:,' ? null : data;
        } catch (error) {
            return null;
        } finally {
            if (snapshotRenderer) {
                snapshotRenderer.dispose();
                if (snapshotRenderer.forceContextLoss) { snapshotRenderer.forceContextLoss(); }
            }
        }
    };

    this.ResetView = function () { RecenterAndFrame(); };
    this.FitView = function () { RecenterAndFrame(); };

    this.EnableSpaceMouse = function (DriverCtor) {
        if (!window.MalievSpaceMouseNavigation ||
            typeof window.MalievSpaceMouseNavigation.createSpaceMouseNavigation !== 'function') {
            return false;
        }
        if (!spaceMouseNavigation) {
            spaceMouseNavigation = window.MalievSpaceMouseNavigation.createSpaceMouseNavigation({
                THREE: THREE,
                canvas: canvasElement,
                camera: camera,
                controls: controls,
                getControls: function () { return controls; },
                getModelObject: function () { return displayObject; },
                fitView: this.FitView,
                resetView: this.ResetView,
                applicationName: 'MALIEV 3D Viewer'
            });
        }
        return spaceMouseNavigation.connect(DriverCtor);
    };

    this.GetSpaceMouseStatus = function () {
        return spaceMouseNavigation ? spaceMouseNavigation.getStatus() : 'idle';
    };

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

}

// ---------------------------------------------------------------------------
// ModelViewerUtils - orchestrates part state (one shared config UI, many parts),
// the thumbnail strip, and order totals.
// ---------------------------------------------------------------------------

function ModelViewerUtils(culture, currency, viewer) {

    var quotationRoot = document.getElementById('instant-quotation-component');
    var isCncQuotation = !!quotationRoot && quotationRoot.dataset.process === 'cnc';
    var currencyString = currency;
    var pleaseWaitText = culture === 'th' ? 'กรุณารอสักครู่' : 'Please wait';
    var startUploadText = culture === 'th' ? 'เริ่มต้นด้วยการส่งไฟล์' : 'Start by uploading a file';
    var submitReviewText = culture === 'th' ? 'ตรวจสอบรายการทั้งหมด' : 'Review all items';
    var compactProcessingText = culture === 'th' ? 'กำลังประมวลผล…' : 'Processing…';
    var errorText = culture === 'th' ? 'เกิดข้อผิดพลาด' : 'Error occured';

    var unfinishedTasks = 0;
    var hasError = false;
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
            material: isCncQuotation ? null : 'PLA',
            color: null,
            quantity: 1,
            buildPreference: 'standard',
            materialPriceState: 'loading',
            materialPrices: {},
            materialPriceKey: null,
            parseComplete: false,
            uploadComplete: false,
            thumbnailComplete: false,
            thumbnailAvailable: false,
            pricingPending: false,
            processingStages: {
                geometry: 'pending',
                thumbnail: 'pending',
                analysis: 'pending',
                stock: 'pending',
                planning: 'pending',
                pricing: 'pending'
            },
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
        this.SetItemProcessingStage(id, 'analysis', 'ready');
        UpdateThumbnailDimensions(id, modelInfo);
        UpdateThumbnailState(id);
    };

    this.SetItemPreview = function (id, object3D, modelInfo) {
        if (!items[id]) { return; }
        items[id].object3D = object3D;
        items[id].modelInfo = modelInfo;
        items[id].previewComplete = true;
        this.SetItemProcessingStage(id, 'geometry', 'ready');
        UpdateThumbnailDimensions(id, modelInfo);
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
        this.SetItemProcessingStage(id, 'thumbnail', available === true ? 'ready' : 'failed');
        UpdateThumbnailState(id);
    };

    this.SetItemProcessingStage = function (id, stage, status) {
        if (!items[id] || !items[id].processingStages || !Object.prototype.hasOwnProperty.call(items[id].processingStages, stage)) {
            return;
        }
        items[id].processingStages[stage] = status;
        UpdateThumbnailState(id);
    };

    this.SetItemPricingPending = function (id, pending) {
        if (!items[id]) { return; }
        items[id].pricingPending = pending === true;
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

        document.querySelectorAll('.iq-thumb').forEach(function (el) {
            el.classList.remove('sel');
            el.setAttribute('aria-pressed', 'false');
        });
        var thumb = document.getElementById('thumb-' + id);
        if (thumb) {
            thumb.classList.add('sel');
            thumb.setAttribute('aria-pressed', 'true');
        }

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

        ApplyBuildPreferenceToRadios(item.buildPreference);
        if (typeof SyncCncItemControls === 'function') { SyncCncItemControls(item); }
    };

    // The radio group is a shared control that reflects whichever part is active, in the same
    // way the material cards and quantity field are.
    var buildPreferenceOrder = ['quality', 'standard', 'strength'];

    function NormalizeBuildPreference(preference) {
        return buildPreferenceOrder.indexOf(preference) >= 0 ? preference : 'standard';
    }

    function BuildPreferenceFromRadios() {
        var selected = document.querySelector('input[name="build-preference"]:checked');
        return selected ? NormalizeBuildPreference(selected.value) : 'standard';
    }

    function ApplyBuildPreferenceToRadios(preference) {
        var normalized = NormalizeBuildPreference(preference);
        document.querySelectorAll('input[name="build-preference"]').forEach(function (radio) {
            radio.checked = radio.value === normalized;
        });
    }

    this.GetBuildPreference = function () { return BuildPreferenceFromRadios(); };

    this.SetBuildPreference = function (preference) { ApplyBuildPreferenceToRadios(preference); };

    this.RenderFileInfo = function (info) {
        var cncItem = isCncQuotation && activeId ? items[activeId] : null;
        if (isCncQuotation && (!cncItem || cncItem.cncStatus !== 'finalized')) {
            var processing = !!cncItem && cncItem.cncStatus !== 'review_required' && cncItem.cncStatus !== 'failed';
            var nonfinalText = processing ? pleaseWaitText : '—';
            ['file-info-dimensions', 'file-info-volume', 'file-info-surface', 'file-info-thickness'].forEach(function (elementId) {
                var element = document.getElementById(elementId);
                if (!element) { return; }
                element.textContent = nonfinalText;
                element.classList.toggle('iq-metric-skeleton', processing);
                element.setAttribute('aria-busy', processing ? 'true' : 'false');
            });
            return;
        }
        info = info || { size: { x: 0, y: 0, z: 0 }, volume: 0, surfaceAreaMm2: 0, minThicknessMm: 0, facets: 0 };
        SetText('file-info-dimensions', ToTwoDecimalPoint(info.size.x) + ' × ' + ToTwoDecimalPoint(info.size.y) + ' × ' + ToTwoDecimalPoint(info.size.z) + ' mm');
        SetText('file-info-volume', ToTwoDecimalPoint(info.volume / 1000) + ' cm³');
        SetText('file-info-surface', ToTwoDecimalPoint(info.surfaceAreaMm2 / 100) + ' cm²');
        SetText('file-info-thickness', ToTwoDecimalPoint(info.minThicknessMm) + ' mm');
        ['file-info-dimensions', 'file-info-volume', 'file-info-surface', 'file-info-thickness'].forEach(function (elementId) {
            var element = document.getElementById(elementId);
            if (!element) { return; }
            element.classList.remove('iq-metric-skeleton');
            element.setAttribute('aria-busy', 'false');
        });
    };

    this.RenderDfmAlerts = function (info) {
        var container = document.getElementById('dfm-alerts');
        if (!container) { return; }
        if (!info) { container.replaceChildren(); return; }

        var alerts = BuildDfmAlerts(info, culture, isCncQuotation ? 'cnc' : '3d-printing');
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

    // Twenty-four materials on first paint is well past what anyone weighs at once, and
    // it was the page's largest decision point. The catalogue order is curated -- the
    // common FDM materials lead -- so the first few are shown and the rest sit one
    // control away. Deliberately not ranked against the part's geometry: the catalogue
    // carries descriptions, not numeric properties, so any such ranking would be
    // fabricated engineering advice rather than a recommendation MALIEV stands behind.
    // Position in the parts strip, zero-padded, so parts read as 01/02/03 wherever they
    // are referenced. Recomputed on render rather than stored, so removing a part
    // renumbers the rest instead of leaving a gap.
    function PartIndexLabel(id) {
        var order = Object.keys(items);
        var position = order.indexOf(String(id));
        if (position === -1) { position = order.length; }
        var oneBased = position + 1;
        return (oneBased < 10 ? '0' : '') + oneBased;
    }

    var materialsExpanded = false;
    var materialPreviewCount = GetMaterialPreviewCount();
    var lastRevealedMaterialKey = null;

    function GetMaterialPreviewCount() {
        var width = window.innerWidth || document.documentElement.clientWidth || 0;
        var height = window.innerHeight || document.documentElement.clientHeight || 0;

        if (width <= 767) { return height >= 800 ? 6 : 4; }
        if (width <= 1199 || height <= 820) { return 6; }
        return height >= 1000 ? 10 : 8;
    }

    this.RenderMaterialCards = function () {
        var container = document.getElementById('material-cards');
        if (!container || !activeId) { return; }

        materialPreviewCount = GetMaterialPreviewCount();
        var selectedKey = items[activeId].material;
        var searching = !!(materialSearch && materialSearch.trim());
        // A search should look at the whole catalogue, and a selection outside the
        // preview must stay visible rather than vanishing behind the toggle.
        var selectedIndex = -1;
        for (var i = 0; i < MATERIALS.length; i++) {
            if (MATERIALS[i].key === selectedKey) { selectedIndex = i; break; }
        }
        var showAll = materialsExpanded || searching || selectedIndex >= materialPreviewCount;
        var limit = showAll ? 0 : materialPreviewCount;

        var item = items[activeId];
        container.replaceChildren(BuildMaterialCardsFragment(
            selectedKey,
            materialSearch,
            culture,
            limit,
            item.materialPriceState,
            item.materialPrices,
            currencyString));
        RenderMaterialToggle(showAll, searching, materialPreviewCount);

        // The card list is a scroller that never scrolled. Selecting a material further
        // down the catalogue left the chosen card below the visible window -- it looked
        // like the selection was drifting off the bottom of the page -- and because the
        // wheel handler used to swallow vertical scrolling there was no way to bring it
        // back. Only reveal on an actual change of selection, so typing in the search box
        // does not yank the list around while results narrow.
        if (selectedKey !== lastRevealedMaterialKey) {
            lastRevealedMaterialKey = selectedKey;
            RevealSelectedMaterialCard(container);
        }
    };

    // Scrolls the selected card into view *within its own container* by adjusting
    // scrollTop directly. Element.scrollIntoView would also scroll every scrollable
    // ancestor, which on this page means the settings rail and the configuration column
    // move too.
    function RevealSelectedMaterialCard(container) {
        var card = container.querySelector('.iq-mat-card.sel');
        if (!card) { return; }

        // Offsets are measured from bounding rectangles rather than offsetTop: offsetTop is
        // relative to the nearest *positioned* ancestor, which for this list is somewhere up
        // the page rather than the scroller itself, so using it scrolled to an unrelated
        // position and pushed the current choice out of view instead of into it.
        var listBox = container.getBoundingClientRect();
        var cardBox = card.getBoundingClientRect();
        var top = cardBox.top - listBox.top + container.scrollTop;
        var bottom = top + cardBox.height;

        // A category heading immediately above the selected card is part of what the
        // customer needs to see, so reveal from the heading's top when there is one.
        var previous = card.previousElementSibling;
        if (previous && previous.classList.contains('iq-mat-cat')) {
            var headingBox = previous.getBoundingClientRect();
            top = headingBox.top - listBox.top + container.scrollTop;
        }

        if (top < container.scrollTop) {
            container.scrollTop = top;
        } else if (bottom > container.scrollTop + container.clientHeight) {
            container.scrollTop = bottom - container.clientHeight;
        }
    }

    function RenderMaterialToggle(showAll, searching, previewCount) {
        var host = document.getElementById('material-toggle');
        if (!host) { return; }

        // While searching, the full catalogue is already in play, so the control has
        // nothing to reveal and would only be noise.
        if (searching || MATERIALS.length <= previewCount) {
            host.hidden = true;
            return;
        }

        host.hidden = false;
        host.textContent = showAll
            ? (culture === 'th' ? 'แสดงเฉพาะวัสดุที่ใช้บ่อย' : 'Show fewer materials')
            : (culture === 'th' ? 'ดูวัสดุทั้งหมด ' + MATERIALS.length + ' ชนิด' : 'All ' + MATERIALS.length + ' materials');
        host.setAttribute('aria-expanded', showAll ? 'true' : 'false');
    }

    var materialResizeTimer = null;
    window.addEventListener('resize', function () {
        window.clearTimeout(materialResizeTimer);
        materialResizeTimer = window.setTimeout(function () {
            var nextPreviewCount = GetMaterialPreviewCount();
            if (nextPreviewCount === materialPreviewCount) { return; }
            materialPreviewCount = nextPreviewCount;
            if (!materialsExpanded) { self.RenderMaterialCards(); }
        }, 150);
    });

    this.ToggleMaterialList = function () {
        materialsExpanded = !materialsExpanded;
        // Expanding or collapsing changes the scroll height under the customer, so the
        // current choice has to be re-revealed even though the selection itself did not move.
        lastRevealedMaterialKey = null;
        this.RenderMaterialCards();
    };

    this.SetMaterialSearch = function (text) {
        materialSearch = text || '';
        this.RenderMaterialCards();
    };

    // Price comparisons belong to the part whose geometry, quantity and build profile
    // produced them. Store them with that part so switching thumbnails never paints a
    // response from another part into the shared material catalogue.
    this.SetMaterialPrices = function (id, state, prices, priceKey) {
        var item = items[id];
        if (!item || (priceKey && item.materialPriceKey !== priceKey)) { return; }
        item.materialPriceState = state;
        item.materialPrices = prices || {};
        if (activeId === String(id) || activeId === id) {
            this.RenderMaterialCards();
        }
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
        // Review controls also call this method; save must read the same selected color.
        var select = document.getElementById('color-select');
        if (select && activeId) { PopulateColorSelect(select, items[activeId].material, value, culture); }
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
            tdPrice.textContent = t.unitPrice === null || t.unitPrice === undefined || !Number.isFinite(Number(t.unitPrice))
                ? '—'
                : Number(t.unitPrice).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' ' + currencyString;
            tr.appendChild(tdQty);
            tr.appendChild(tdPrice);
            return tr;
        });
        body.replaceChildren.apply(body, rows);
    };

    this.SaveItemSettings = function (id) {
        var item = items[id];
        if (!item) { return; }
        SetValue('item-material-' + id, item.material);
        SetValue('item-color-' + id, item.color);
        SetValue('item-quantity-' + id, item.quantity);
        SetValue('item-build-preference-' + id, item.buildPreference);
        UpdateThumbnailQuote(id);
    };

    this.RefreshItemPresentation = function (id) {
        if (!items[id]) { return; }
        UpdateThumbnailQuote(id);
        if (String(activeId) === String(id)) { this.RenderFileInfo(items[id].modelInfo); }
    };

    this.SaveSelectedSettings = function () {
        if (!activeId) { return; }
        var item = items[activeId];
        item.color = IsActiveMultiBody() ? 'Any' : SelectValue('color-select');
        item.quantity = InputValue('quantity-input');
        item.buildPreference = BuildPreferenceFromRadios();
        this.SaveItemSettings(activeId);
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
        node.setAttribute('role', 'button');
        node.setAttribute('tabindex', '0');
        node.setAttribute('aria-pressed', 'false');
        node.setAttribute('aria-label', (culture === 'th' ? 'เลือกชิ้นงาน ' : 'Select part ') + file.name);
        node.setAttribute('aria-busy', 'true');
        node.addEventListener('click', function () { SelectItem(id); });
        node.addEventListener('keydown', function (event) {
            if (event.key === 'Enter' || event.key === ' ') {
                event.preventDefault();
                SelectItem(id);
            }
        });

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
        status.appendChild(CreateEl('span', 'iq-thumb-status-text', culture === 'th' ? 'ประมวลผล' : 'Processing…'));
        node.appendChild(status);

        var progress = CreateEl('div', 'iq-thumb-progress');
        progress.id = 'upload-progress-item-' + id;
        progress.appendChild(CreateEl('div', 'bar'));
        node.appendChild(progress);

        var meta = CreateEl('div', 'iq-thumb-meta');
        var fileLine = CreateEl('div', 'iq-thumb-fileline');
        // Uploading the same bracket five times produced five identical rows with nothing
        // to tell them apart -- in the strip, in the metrics card, and in the review list.
        // A stable position gives each part an identity independent of its filename.
        // Sits on the tile rather than in the filename line, so it costs the name no width.
        var indexEl = CreateEl('div', 'iq-thumb-index', PartIndexLabel(id));
        node.appendChild(indexEl);
        var nameEl = CreateEl('div', 'iq-thumb-name text-truncate', file.name);
        nameEl.title = file.name;
        fileLine.appendChild(nameEl);
        fileLine.appendChild(CreateEl('div', 'iq-thumb-size', FormatFileSize(file.size)));
        meta.appendChild(fileLine);

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

    // A part whose upload or parse never resolves used to spin "Processing…" forever,
    // with the submit button stuck on a reasonless "Please wait". Give the wait an end.
    var STALL_TIMEOUT_MS = 120000;

    function ClearStallTimer(item) {
        if (item && item.stallTimer) {
            window.clearTimeout(item.stallTimer);
            item.stallTimer = null;
        }
    }

    function ArmStallTimer(id, item) {
        if (item.stallTimer) { return; }
        item.stallTimer = window.setTimeout(function () {
            var current = items[id];
            if (!current) { return; }
            current.stallTimer = null;
            if (current.errorMessage) { return; }
            if (current.parseComplete && current.uploadComplete && current.thumbnailComplete) { return; }
            current.errorMessage = culture === 'th'
                ? 'ไฟล์นี้ใช้เวลานานผิดปกติ กรุณาลบแล้วลองใหม่'
                : 'This file is taking too long. Remove it and try again.';
            UpdateThumbnailState(id);
            CheckReadyState();
        }, STALL_TIMEOUT_MS);
    }

    function UpdateThumbnailState(id) {
        var item = items[id];
        var node = document.getElementById('thumb-' + id);
        var status = document.getElementById('thumb-status-' + id);
        var statusText = status ? status.querySelector('.iq-thumb-status-text') : null;
        if (!item || !node || !status || !statusText) { return; }

        node.classList.toggle('is-error', !!item.errorMessage);
        node.classList.toggle('has-thumbnail', item.thumbnailAvailable === true);
        if (item.errorMessage) {
            ClearStallTimer(item);
            node.classList.remove('is-processing');
            node.setAttribute('aria-busy', 'false');
            status.hidden = false;
            statusText.textContent = item.errorMessage;
            return;
        }

        var stages = item.processingStages || {};
        var ready = item.parseComplete && item.uploadComplete && item.thumbnailComplete && !item.pricingPending
            && stages.pricing !== 'pending' && stages.pricing !== 'processing';
        node.classList.toggle('is-processing', !ready);
        node.setAttribute('aria-busy', ready ? 'false' : 'true');
        if (!ready) {
            ArmStallTimer(id, item);
            status.hidden = false;
            var stage = stages.geometry === 'queued' ? 'queued'
                : (stages.geometry !== 'ready' ? 'geometry'
                    : (stages.thumbnail !== 'ready' && stages.thumbnail !== 'failed' ? 'thumbnail'
                        : (stages.analysis !== 'ready' && stages.analysis !== 'failed' ? 'analysis'
                            : (stages.stock !== 'ready' && stages.stock !== 'failed' ? 'stock'
                                : (stages.planning !== 'ready' && stages.planning !== 'failed' ? 'planning'
                                    : (stages.pricing !== 'ready' && stages.pricing !== 'failed' ? 'pricing'
                                        : (!item.uploadComplete ? 'upload' : 'pricing')))))));
            var labels = culture === 'th' ? {
                queued: 'รอคิวประมวลผล', geometry: 'โหลด CAD', thumbnail: 'สร้างภาพตัวอย่าง',
                analysis: 'วิเคราะห์ชิ้นงาน', stock: 'เลือกสต็อก', planning: 'วางแผนตั้งงาน', pricing: 'คำนวณราคา', upload: 'อัปโหลดไฟล์'
            } : {
                queued: 'Queued', geometry: 'Loading CAD', thumbnail: 'Creating preview',
                analysis: 'Analyzing part', stock: 'Selecting stock', planning: 'Planning setups', pricing: 'Calculating price', upload: 'Uploading file'
            };
            statusText.textContent = labels[stage];
            return;
        }

        ClearStallTimer(item);

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

        if (isCncQuotation && item.cncStatus !== 'finalized') {
            var processing = item.cncStatus === 'processing' || item.cncStatus === 'idle' || !item.cncStatus;
            quote.replaceChildren();
            var configPlaceholder = CreateEl('div', 'iq-thumb-summary-config iq-thumb-summary-placeholder', processing ? pleaseWaitText : '—');
            var pricePlaceholder = CreateEl('div', 'iq-thumb-summary-price iq-thumb-summary-placeholder', processing ? pleaseWaitText : '—');
            if (processing) {
                configPlaceholder.classList.add('iq-skeleton-text');
                pricePlaceholder.classList.add('iq-skeleton-text');
            }
            quote.appendChild(configPlaceholder);
            quote.appendChild(pricePlaceholder);
            quote.title = culture === 'th' ? 'ยังไม่มีราคาสรุป' : 'No finalized price yet';
            return;
        }

        var unitCost = InputValue('item-estimated-unit-cost-' + id) || '—';
        var material = typeof item.material === 'string' ? item.material.trim() : '';
        var quantityLabel = culture === 'th' ? item.quantity + ' ชิ้น' : item.quantity + ' pcs';
        var priceLabel = culture === 'th' ? 'ราคาต่อชิ้น' : 'Unit price';
        quote.replaceChildren();
        // One row on a 117px tile: the configuration reads left, the price reads right.
        // "PLA 1x" rather than the spelled-out quantity because the two have to share a
        // line barely a hundred pixels wide, and the multiplier is read the same in both
        // languages.
        quote.appendChild(CreateEl('div', 'iq-thumb-summary-config', material ? material + ' ' + item.quantity + '×' : '—'));
        // The visible price drops its label to earn that width back; the label survives on
        // the element for assistive technology and in the tile's own tooltip below, so
        // nothing is lost for anyone who cannot infer it from position.
        var priceEl = CreateEl('div', 'iq-thumb-summary-price', unitCost);
        priceEl.setAttribute('aria-label', priceLabel + ' ' + unitCost);
        quote.appendChild(priceEl);
        quote.title = (material || '—') + ' · ' + quantityLabel + ' · ' + priceLabel + ' ' + unitCost;
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
        // Seeded rather than left blank: SaveSelectedSettings only writes the active part, so a
        // part whose first estimate never completed would otherwise submit an empty profile.
        AddHidden('item-build-preference-' + id, 'OrderItems[' + id + '].BuildPreference', 'standard');
        AddHidden('item-estimated-unit-cost-' + id, 'OrderItems[' + id + '].EstimatedUnitCost');
        AddHidden('item-estimated-unit-time-' + id, 'OrderItems[' + id + '].EstimatedUnitPrintTime');
        AddHidden('item-estimated-total-cost-' + id, 'OrderItems[' + id + '].EstimatedTotalCost');
        AddHidden('item-estimated-total-time-' + id, 'OrderItems[' + id + '].EstimatedTotalPrintTime');
        AddHidden('item-upload-path-' + id, 'OrderItems[' + id + '].StoragePath');
        AddHidden('item-cnc-itemid-' + id, 'OrderItems[' + id + '].CncItemId', String(id));
        AddHidden('item-cnc-cncmodeluploadreceipt-' + id, 'OrderItems[' + id + '].CncModelUploadReceipt');
        AddHidden('item-cnc-cncdrawinguploadreceipt-' + id, 'OrderItems[' + id + '].CncDrawingUploadReceipt');
        AddHidden('item-warning-' + id, 'OrderItems[' + id + '].GeometryWarning');

        container.appendChild(node);
    }

    this.IncreasePendingTask = function () { unfinishedTasks += 1; CheckReadyState(); };
    this.DecreasePendingTask = function () { if (unfinishedTasks > 0) { unfinishedTasks -= 1; } CheckReadyState(); };

    function CheckReadyState() {
        if (!submitAllow) { return; }
        var hasItems = Object.keys(items).length > 0;
        if (unfinishedTasks === 0 && !hasError && hasItems) {
            if (isCncQuotation && typeof SyncCncSubmissionSafety === 'function') {
                SyncCncSubmissionSafety();
                return;
            }
            submitAllow.disabled = false;
            submitAllow.setAttribute('aria-disabled', 'false');
            submitAllow.textContent = submitReviewText;
            submitAllow.removeAttribute('aria-label');
            submitAllow.removeAttribute('title');
        } else {
            submitAllow.disabled = true;
            submitAllow.setAttribute('aria-disabled', 'true');
            var blocker = hasError ? errorText : DescribeBlocker(hasItems);
            var progress = !hasError ? DescribeProgress(hasItems) : null;
            submitAllow.textContent = isCncQuotation && progress ? compactProcessingText : blocker;
            if (isCncQuotation && progress) {
                submitAllow.setAttribute('aria-label', progress);
                submitAllow.setAttribute('title', progress);
            } else {
                submitAllow.removeAttribute('aria-label');
                submitAllow.removeAttribute('title');
            }
        }
    }

    // A disabled button that only says "Please wait" leaves the customer with no idea
    // whether anything is happening, how much is left, or whether it has stalled. Name
    // the blocker instead.
    function DescribeBlocker(hasItems) {
        if (!hasItems) {
            return startUploadText || pleaseWaitText;
        }
        var total = Object.keys(items).length;
        if (unfinishedTasks > 0 && total > 0) {
            var done = Math.max(0, total - unfinishedTasks);
            return culture === 'th'
                ? 'กำลังประมวลผล ' + (done + 1) + ' จาก ' + total + ' ไฟล์...'
                : 'Processing ' + (done + 1) + ' of ' + total + '...';
        }
        return pleaseWaitText;
    }

    function DescribeProgress(hasItems) {
        if (!hasItems || unfinishedTasks <= 0) { return null; }
        var total = Object.keys(items).length;
        if (total <= 0) { return null; }
        var done = Math.max(0, total - unfinishedTasks);
        return culture === 'th'
            ? 'กำลังประมวลผล ' + (done + 1) + ' จาก ' + total + ' ไฟล์'
            : 'Processing ' + (done + 1) + ' of ' + total + ' files';
    }

    // This is only invoked by the Razor page when its server-side environment
    // is Development. The resulting review flow has no submitting action.
    this.AllowLocalPreviewAfterUploadFailure = function () {
        CheckReadyState();
    };
    this.BlockSubmission = function () { hasError = true; CheckReadyState(); };
    this.ClearError = function () { hasError = false; CheckReadyState(); };
    this.HasError = function () { return hasError; };
    this.ThrowError = function () {
        // Localized copy is published by the page; the literal is the last-resort fallback.
        alert((window.iqErrorText && window.iqErrorText.estimateFailed)
            || 'There was an error getting an estimate for your part. Please contact us for a direct quotation.');
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
        var isFinal = finalPrice !== null && finalPrice !== undefined && Number.isFinite(Number(finalPrice));
        var text = isFinal
            ? Number(finalPrice).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' ' + currencyString
            : '—';
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
function BuildDfmAlerts(info, culture, process) {
    var th = culture === 'th';
    var cnc = process === 'cnc';
    var alerts = [];
    if (info.nonWatertight) {
        alerts.push({
            level: 'warn', iconChar: '⚠',
            title: th ? 'โมเดลไม่ปิดสนิท (มีรูรั่ว)' : 'Non-watertight mesh',
            subtitle: cnc
                ? (th ? 'ให้วิศวกรตรวจสอบความสมบูรณ์ของโมเดลก่อนวางแผนการกัด' : 'Requires engineering review of model integrity before machining planning')
                : (th ? 'อาจส่งผลให้พิมพ์ผิดพลาด' : 'May cause printing issues')
        });
    }
    if (info.nonManifold) {
        alerts.push({
            level: 'danger', iconChar: '⛔',
            title: th ? 'เส้นขอบไม่สมบูรณ์ (Non-manifold)' : 'Non-manifold edges',
            subtitle: th ? 'ตรวจสอบความสมบูรณ์ของโมเดล' : 'Check model integrity'
        });
    }
    if (info.bodyCount > 1) {
        alerts.push({
            level: 'warn', iconChar: '⚠',
            title: th ? 'โมเดลแบบหลายชิ้นส่วน' : 'Multi-body mesh',
            subtitle: th ? ('ตรวจสอบชิ้นส่วนที่รวมกันโดยไม่ตั้งใจ (' + info.bodyCount + ' ชิ้น)') : ('Check for unintended merged bodies (' + info.bodyCount + ' bodies)')
        });
    }
    if (info.oddlySmall) {
        alerts.push({
            level: 'warn', iconChar: '⚠',
            title: th ? 'ขนาดชิ้นงานเล็กผิดปกติ' : 'Unusually small dimensions',
            subtitle: cnc
                ? (th ? 'ตรวจสอบหน่วยของโมเดล การจับยึด และขนาดเครื่องมือตัด' : 'Check model units, workholding and cutting-tool size')
                : (th ? 'อาจพิมพ์ได้ยากหรือหยิบจับลำบาก' : 'May be difficult to print or handle reliably')
        });
    }
    if (info.oddlyLarge) {
        alerts.push({
            level: 'warn', iconChar: '⚠',
            title: cnc ? (th ? 'ชิ้นงานขนาดใหญ่ — ต้องตรวจสอบขีดความสามารถของเครื่องจักร' : 'Large part — machine capacity review required')
                : (th ? 'ขนาดชิ้นงานใหญ่ผิดปกติ' : 'Unusually large dimensions'),
            subtitle: cnc
                ? (th ? 'ให้วิศวกรตรวจสอบระยะทำงานของเครื่องจักรและการจับยึดก่อนยืนยันการผลิต' : 'Requires engineering review of machine travel and workholding before confirming manufacture')
                : (th ? 'อาจเกินพื้นที่พิมพ์มาตรฐาน และต้องแบ่งชิ้นงาน' : 'May exceed our standard build volume and require splitting')
        });
    }
    return alerts;
}

