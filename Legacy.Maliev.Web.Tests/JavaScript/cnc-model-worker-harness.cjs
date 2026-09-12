const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const { webcrypto } = require('node:crypto');
const webRoot = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot');
function runtime() {
    const imports = [], fetches = [], factories = [], diagnostics = [];
    const state = { corrupt: null, missing: null, mutateSource: false, mutatePolicy: false, captureMalformed: false, nativeImports: 0 };
    const c = vm.createContext({ console, TextEncoder, TextDecoder, crypto: webcrypto, setTimeout, clearTimeout });
    c.self = c; c.location = { search: '?v=runtime-promotion' };
    c.importScripts = (...urls) => urls.forEach(url => {
        imports.push(url);
        if (url.endsWith('/occt-import-js.js')) {
            const native = url.includes('/occt-cnc/');
            const factory = require(path.join(webRoot, url));
            c.occtimportjs = async options => {
                factories.push(native ? 'cnc' : 'additive');
                const diagnostic = (...parts) => {
                    const text = parts.join(' ');
                    if (state.captureMalformed && text.includes('ERR StepFile : Undefined Parsing:')) diagnostics.push(text);
                    else console.error(...parts);
                };
                const api = await factory(native ? { ...options, print: diagnostic, printErr: diagnostic }
                    : { ...options, wasmBinary: fs.readFileSync(path.join(webRoot, 'lib/occt/occt-import-js.wasm')) });
                if (!native) return api;
                return { ...api, ReadStepFile(bytes, parameters) {
                    state.nativeImports++;
                    const result = api.ReadStepFile(bytes, parameters);
                    if (state.mutateSource) bytes[0] ^= 1;
                    if (state.mutatePolicy && result.success) result.kernelProvenance.nativeInterpretation.policy.assessmentTimeLimitMs = 59999;
                    return result;
                } };
            };
        } else vm.runInContext(fs.readFileSync(path.join(webRoot, url.split('?')[0]), 'utf8'), c, { filename: url });
    });
    c.fetch = async url => {
        fetches.push(url);
        const bytes = fs.readFileSync(path.join(webRoot, url));
        if (state.corrupt && url.endsWith(state.corrupt)) bytes[0] ^= 1;
        return { ok: !(state.missing && url.endsWith(state.missing)), arrayBuffer: async () => bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) };
    };
    c.importScripts('/src/app/js/model-viewer/model-viewer.worker.js');
    return { c, imports, fetches, factories, diagnostics, state, send(data) {
        return new Promise(resolve => { c.postMessage = (message, transfer) => resolve(structuredClone(message, { transfer: transfer || [] })); c.onmessage({ data }); });
    } };
}
module.exports = { runtime };
