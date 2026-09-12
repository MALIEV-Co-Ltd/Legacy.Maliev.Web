'use strict';
const fs = require('node:fs');
const path = require('node:path');
const schema = require('./catalog.schema.json');

// Dependency-free validator for the exact JSON Schema keywords used by this
// checked-in schema. Unsupported keywords fail rather than silently disappearing.
const supported = new Set(['$schema', 'title', 'type', 'additionalProperties', 'required', 'properties', 'items',
    'enum', 'const', 'minimum', 'exclusiveMinimum', 'minLength', 'minItems', 'uniqueItems', 'pattern']);
function validateSchema(value, rule, location = '$', errors = []) {
    for (const key of Object.keys(rule)) if (!supported.has(key)) throw new Error(`Unsupported schema keyword ${key}`);
    const fail = message => errors.push(`${location}: ${message}`);
    const actual = value === null ? 'null' : Array.isArray(value) ? 'array' : typeof value;
    if (rule.type) {
        const types = Array.isArray(rule.type) ? rule.type : [rule.type];
        if (!types.some(type => type === actual || (type === 'integer' && Number.isInteger(value)))) {
            fail(`expected ${types.join('|')}, got ${actual}`); return errors;
        }
    }
    if ('const' in rule && value !== rule.const) fail(`expected constant ${JSON.stringify(rule.const)}`);
    if (rule.enum && !rule.enum.includes(value)) fail('value outside enum');
    if (typeof value === 'number') {
        if (!Number.isFinite(value)) fail('number must be finite');
        if (rule.minimum !== undefined && value < rule.minimum) fail(`must be >= ${rule.minimum}`);
        if (rule.exclusiveMinimum !== undefined && value <= rule.exclusiveMinimum) fail(`must be > ${rule.exclusiveMinimum}`);
    }
    if (typeof value === 'string') {
        if (rule.minLength && value.trim().length < rule.minLength) fail('empty string');
        if (rule.pattern && !new RegExp(rule.pattern).test(value)) fail('pattern mismatch');
    }
    if (Array.isArray(value)) {
        if (rule.minItems && value.length < rule.minItems) fail('too few items');
        if (rule.uniqueItems && new Set(value.map(v => JSON.stringify(v))).size !== value.length) fail('duplicate items');
        if (rule.items) value.forEach((v, i) => validateSchema(v, rule.items, `${location}[${i}]`, errors));
    } else if (value && typeof value === 'object') {
        for (const key of rule.required || []) if (!Object.hasOwn(value, key)) fail(`missing ${key}`);
        for (const [key, child] of Object.entries(value)) {
            if (rule.properties && Object.hasOwn(rule.properties, key)) validateSchema(child, rule.properties[key], `${location}.${key}`, errors);
            else if (rule.additionalProperties === false) fail(`unexpected field ${key}`);
        }
    }
    return errors;
}
function validateCatalog(catalog) {
    const errors = validateSchema(catalog, schema);
    if (errors.length) return errors;
    const fail = message => errors.push(message);
    const validDate = value => {
        const parsed = new Date(value);
        return Number.isFinite(parsed.getTime()) && parsed.toISOString().slice(0, 10) === value;
    };
    if (!validDate(catalog.checkedOn)) fail('invalid catalog checked date');
    const sourceMap = new Map();
    const allowedHosts = ['th.misumi-ec.com', 'www.nachi-fujikoshi.co.jp', 'osgtool.com', 'www.ns-tool.com',
        'www.harveytool.com', 'www.mitsubishicarbide.net', 'www.mmc-carbide.com', 'data.mmc-carbide.com'];
    for (const source of catalog.sources) {
        if (sourceMap.has(source.id)) fail(`duplicate source id ${source.id}`);
        sourceMap.set(source.id, source);
        let url;
        try { url = new URL(source.sourceUrl); } catch { fail(`invalid source URL ${source.id}`); }
        if (url && (!allowedHosts.includes(url.hostname) || url.username || url.password || url.protocol !== 'https:')) fail(`non-primary source URL ${source.id}`);
        if (!validDate(source.checkedOn)) fail(`invalid checked date ${source.id}`);
        if (source.checkedOn > catalog.checkedOn) fail(`source date newer than catalog ${source.id}`);
    }
    const toolMap = new Map();
    for (const tool of catalog.tools) {
        if (toolMap.has(tool.id)) fail(`duplicate tool id ${tool.id}`);
        toolMap.set(tool.id, tool);
        const facts = { manufacturer: tool.manufacturer, series: tool.series, orderCode: tool.orderCode,
            flutes: tool.flutes, toolMaterial: tool.toolMaterial, surfaceTreatment: tool.surfaceTreatment };
        for (const [key, value] of Object.entries(tool.geometry)) facts[`geometry.${key}`] = value;
        if (tool.thread) for (const [key, value] of Object.entries(tool.thread)) facts[`thread.${key}`] = value;
        if (tool.threadUsage) for (const [key, value] of Object.entries(tool.threadUsage)) facts[`threadUsage.${key}`] = value;
        const evidenced = new Set();
        for (const evidence of tool.sourceEvidence) {
            const source = sourceMap.get(evidence.sourceId);
            if (!source || source.status !== 'checked_primary_source') fail(`${tool.id}: unavailable or unknown source ${evidence.sourceId}`);
            for (const field of evidence.fields) {
                if (!Object.hasOwn(facts, field) || facts[field] === null) fail(`${tool.id}: evidence names missing/null fact ${field}`);
                evidenced.add(field);
            }
        }
        for (const [field, value] of Object.entries(facts)) {
            if (value !== null && !evidenced.has(field)) fail(`${tool.id}: fact without field provenance ${field}`);
        }
        if (tool.recordStatus === 'requested_unverified' && Object.values(facts).some(v => v !== null)) fail(`${tool.id}: requested coverage must not contain manufacturer facts`);
        if (tool.recordStatus === 'source_verified' && (!tool.manufacturer || !tool.series || tool.sourceEvidence.length === 0)) fail(`${tool.id}: incomplete identity/provenance`);
        const g = tool.geometry;
        if (tool.threadUsage && tool.family !== 'thread_mill') fail(`${tool.id}: thread usage requires thread mill family`);
        if (tool.threadUsage?.minimumPitchMm != null && tool.threadUsage?.maximumPitchMm != null && tool.threadUsage.minimumPitchMm > tool.threadUsage.maximumPitchMm) fail(`${tool.id}: inverted pitch bounds`);
        if (g.fluteLengthMm !== null && g.overallLengthMm !== null && g.fluteLengthMm > g.overallLengthMm) fail(`${tool.id}: flute exceeds overall length`);
        if (g.underNeckLengthMm !== null && g.overallLengthMm !== null && g.underNeckLengthMm > g.overallLengthMm) fail(`${tool.id}: under-neck exceeds overall length`);
        if (g.fluteLengthMm !== null && g.underNeckLengthMm !== null && g.fluteLengthMm > g.underNeckLengthMm) fail(`${tool.id}: flute exceeds under-neck length`);
        if (g.ballRadiusMm !== null && g.diameterMm !== null && Math.abs(g.ballRadiusMm * 2 - g.diameterMm) > 1e-9) fail(`${tool.id}: ball radius/diameter mismatch`);
        if (tool.thread?.threadsPerInch && Math.abs(25.4 / tool.thread.threadsPerInch - tool.thread.pitchMm) > 1e-9) fail(`${tool.id}: inch pitch conversion mismatch`);
    }
    const coverageKeys = new Set();
    const coverageFamilies = new Set();
    for (const coverage of catalog.requestedCoverage) {
        const key = JSON.stringify([coverage.family, coverage.variant]);
        if (coverageKeys.has(key)) fail(`duplicate coverage key ${key}`);
        coverageKeys.add(key);
        coverageFamilies.add(coverage.family);
        for (const id of coverage.verifiedToolIds) {
            const tool = toolMap.get(id);
            if (!tool || tool.family !== coverage.family || tool.recordStatus !== 'source_verified') fail(`coverage references wrong/unverified tool ${id}`);
            if (tool && ['standard', 'long_neck'].includes(coverage.variant) && tool.variant !== coverage.variant) fail(`coverage references wrong variant ${id}`);
        }
        if (coverage.status === 'catalogued' && coverage.gaps.length) fail(`catalogued coverage has gaps: ${coverage.family}/${coverage.variant}`);
        if (coverage.status === 'catalogued' && coverage.verifiedToolIds.length === 0) fail(`catalogued coverage has no verified records: ${coverage.family}/${coverage.variant}`);
        if (coverage.status === 'catalogued') for (const diameter of coverage.requestedDiametersMm || []) {
            if (!coverage.verifiedToolIds.some(id => toolMap.get(id)?.geometry.diameterMm === diameter)) fail(`catalogued coverage lacks diameter ${diameter}: ${coverage.family}/${coverage.variant}`);
        }
    }
    for (const family of schema.properties.tools.items.properties.family.enum) {
        if (!coverageFamilies.has(family)) fail(`missing requested family ${family}`);
    }
    for (const assumption of catalog.quotingAssemblyAssumptions) {
        for (const id of assumption.appliesToToolIds) if (!toolMap.has(id)) fail(`assembly assumption references missing tool ${id}`);
    }
    return errors;
}
if (require.main === module) {
    try {
        const catalog = JSON.parse(fs.readFileSync(process.argv[2] || path.join(__dirname, 'catalog.json'), 'utf8'));
        const errors = validateCatalog(catalog);
        if (errors.length) { console.error(errors.join('\n')); process.exitCode = 1; }
        else console.log(`PASS: ${catalog.tools.length} catalog records, ${catalog.sources.length} primary sources; no machining authorization`);
    } catch (error) { console.error(error.message); process.exitCode = 1; }
}
module.exports = { validateCatalog, validateSchema };
