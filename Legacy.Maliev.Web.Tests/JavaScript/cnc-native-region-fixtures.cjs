const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const { webcrypto, createHash } = require('node:crypto');
const root = path.resolve(__dirname, '../../Legacy.Maliev.Web/wwwroot');
const fixtureRoot = path.resolve(__dirname, '../TestAssets/CncNativeInterpretation');
const digest = x => createHash('sha256').update(x).digest('hex');
const dispatch = { self: {} }; vm.runInNewContext(fs.readFileSync(path.join(root, 'src/app/js/cnc-quotation/cnc-native-dispatch.js'), 'utf8'), dispatch);
const runtimePin = dispatch.self.CncNativeDispatch.runtimePin, asset = path.join(root, runtimePin.path);
function manifestIdentity(pin) {
    const bytes = fs.readFileSync(path.join(root, pin.path, 'manifest.js'));
    if (digest(bytes) !== pin.manifestSha256) throw new Error('fixture manifest identity mismatch');
    const c = { self: {} }; vm.runInNewContext(bytes.toString('utf8'), c); return c.self.CncNativeBuild;
}
const identity = manifestIdentity(runtimePin);
// Saved original JSON is a receipt from this historical runtime. It must not
// inherit the current public-import default when the production pin advances.
const historicalPin = { path: '/lib/occt-cnc/0f4759e678ea-191da5c8b62d/', manifestSha256: 'f5ee4f75401ce4086e3e8ffb0048f94e3010ac508dc6ae2709f89b4439de8683' };
const historicalIdentity = manifestIdentity(historicalPin);
let importer;
function runtime() {
    const c = vm.createContext({ console, TextEncoder, TextDecoder, crypto: webcrypto }); c.self = c;
    for (const name of ['cnc-plan-contracts', 'cnc-cad-document', 'cnc-native-topology.worker', 'cnc-topology.worker', 'cnc-native-regions', 'cnc-native-thread-evidence', 'cnc-feature-recognition-thread', 'cnc-feature-recognition-prismatic', 'cnc-feature-graph.worker']) {
        vm.runInContext(fs.readFileSync(path.join(root, 'src/app/js/cnc-quotation', name + '.js'), 'utf8'), c);
    }
    return c;
}
// Unit-test source boundary, independent of the payload being mutated. Production
// expectations are supplied privately by the actual model worker's verified loader.
function expectation(n) { return { sourceBytesHash: n.sourceBytesHash, sourceAttachmentVerified: true, buildIdentity: identity,
    manifestUrl: runtimePin.path + 'manifest.js', manifestSha256: runtimePin.manifestSha256,
    errorBudgetMm: .01, assessmentTimeLimitMs: 60000 }; }
function historicalExpectation(n) { return { ...expectation(n), buildIdentity: historicalIdentity,
    manifestUrl: historicalPin.path + 'manifest.js', manifestSha256: historicalPin.manifestSha256 }; }
async function envelope(c, name) {
    const js = fs.readFileSync(path.join(asset, 'occt-import-js.js')), wasm = fs.readFileSync(path.join(asset, 'occt-import-js.wasm'));
    if (digest(js) !== identity.jsSha256 || digest(wasm) !== identity.wasmSha256) throw new Error('fixture production importer identity mismatch');
    importer ||= require(path.join(asset, 'occt-import-js.js'))({ wasmBinary: wasm });
    const bytes = fs.readFileSync(path.join(fixtureRoot, name + '.step'));
    const manifest = JSON.parse(fs.readFileSync(path.join(fixtureRoot, 'region-fixtures.json')));
    if (digest(bytes) !== manifest.sources.find(source => source.file === name + '.step').sha256) throw new Error('fixture source identity mismatch');
    return c.CncCadDocument.create(bytes, 'step', identity, (await importer).ReadStepFile(bytes, c.CncCadDocument.importParameters));
}
async function topology(c, n, expected = expectation(n)) {
    n.importRevision = await c.CncCadDocument.hash(n);
    return c.CncTopology.build({ nativeImport: n }, expected);
}
module.exports = { runtime, envelope, topology, expectation, digest, identity, fixtureRoot, asset, runtimePin, historicalIdentity, historicalExpectation };
