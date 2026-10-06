import assert from 'node:assert/strict';
import { mkdtemp, mkdir, readFile, rm, symlink, truncate, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { verifyReleaseAssets } from '../verify-release-assets.mjs';

async function fixture(t) {
  const root = await mkdtemp(path.join(os.tmpdir(), 'maliev-release-test-'));
  t.after(async () => {
    assert.equal(path.dirname(root), path.resolve(os.tmpdir()));
    assert.match(path.basename(root), /^maliev-release-test-/);
    await rm(root, { recursive: true, force: true });
  });
  const manifest = { scripts: ['vendor.min.js', 'app.min.js'], styles: ['site.min.css'],
    routeScripts: ['route-inquiry.js'], routeStyles: [], routeScopedModules: {} };
  await writeFile(path.join(root, 'asset-manifest.json'), JSON.stringify(manifest));
  for (const name of [...manifest.scripts, ...manifest.routeScripts]) await writeFile(path.join(root, name), 'void 0;');
  await mkdir(path.join(root, 'assets'));
  await writeFile(path.join(root, 'site.min.css'), 'a{font-family:test}@font-face{src:url(assets/fa-solid.woff2)}@font-face{src:url("assets/fa-regular.woff2")}');
  // Real checked-in release fonts provide a valid positive control before every mutation.
  for (const [name, source] of [
    ['fa-solid.woff2', 'fa-solid-900-EFITDVWS.woff2'],
    ['fa-regular.woff2', 'fa-regular-400-NCGHE5NP.woff2'],
  ]) await writeFile(path.join(root, 'assets', name), await readFile(new URL(`../wwwroot/dist/assets/${source}`, import.meta.url)));
  return { root, manifest, saveManifest: () => writeFile(path.join(root, 'asset-manifest.json'), JSON.stringify(manifest)) };
}

test('valid release reports every output and referenced font', async t => {
  const { root } = await fixture(t);
  assert.deepEqual(await verifyReleaseAssets(root), { outputs: 4, fonts: 2 });
});

for (const [name, mutate, code] of [
  ['missing manifest', root => rm(path.join(root, 'asset-manifest.json')), 'inspection-incomplete'],
  ['malformed manifest', root => writeFile(path.join(root, 'asset-manifest.json'), '{'), 'inspection-incomplete'],
  ['missing script', root => rm(path.join(root, 'app.min.js')), 'inspection-incomplete'],
  ['empty script', root => writeFile(path.join(root, 'app.min.js'), ''), 'missing-or-empty-asset'],
  ['missing font', root => rm(path.join(root, 'assets', 'fa-solid.woff2')), 'inspection-incomplete'],
  ['empty font', root => writeFile(path.join(root, 'assets', 'fa-solid.woff2'), ''), 'missing-or-empty-asset'],
  ['text in font file', root => writeFile(path.join(root, 'assets', 'fa-solid.woff2'), 'invalid-font'.repeat(8)), 'invalid-woff2-header'],
  ['truncated font', root => truncate(path.join(root, 'assets', 'fa-solid.woff2'), 48), 'invalid-woff2-header'],
  ['oversized CSS', root => truncate(path.join(root, 'site.min.css'), 4 * 1024 * 1024 + 1), 'text-size-limit'],
  ['oversized asset', root => truncate(path.join(root, 'app.min.js'), 64 * 1024 * 1024 + 1), 'asset-size-limit'],
]) {
  test(`rejects ${name} before release acceptance`, async t => {
    const { root } = await fixture(t);
    await mutate(root);
    await assert.rejects(verifyReleaseAssets(root), error => error.code === code);
  });
}

test('route script cannot contaminate the two shared entry points', async t => {
  const value = await fixture(t);
  value.manifest.scripts.push('route-inquiry.js');
  await value.saveManifest();
  await assert.rejects(verifyReleaseAssets(value.root), error => error.code === 'invalid-common-bundle');
});

test('manifest rejects undeclared fields and malformed lists', async t => {
  const value = await fixture(t);
  value.manifest.extra = true;
  await value.saveManifest();
  await assert.rejects(verifyReleaseAssets(value.root), error => error.code === 'invalid-manifest');
  delete value.manifest.extra;
  value.manifest.routeScripts = 'route-inquiry.js';
  await value.saveManifest();
  await assert.rejects(verifyReleaseAssets(value.root), error => error.code === 'invalid-manifest');
});

test('duplicate output is rejected independently of file existence', async t => {
  const value = await fixture(t);
  value.manifest.routeScripts.push('app.min.js');
  await value.saveManifest();
  await assert.rejects(verifyReleaseAssets(value.root), error => error.code === 'duplicate-output');
});

test('manifest escape cannot read an asset outside the release', async t => {
  const value = await fixture(t);
  value.manifest.routeStyles.push('../outside.css');
  await value.saveManifest();
  await assert.rejects(verifyReleaseAssets(value.root), error => error.code === 'asset-outside-release');
});

test('manifest inspection bounds the total output count', async t => {
  const value = await fixture(t);
  value.manifest.routeScripts = Array.from({ length: 129 }, (_, index) => `route-${index}.js`);
  await value.saveManifest();
  await assert.rejects(verifyReleaseAssets(value.root), error => error.code === 'output-count-limit');
});

test('stylesheet inspection bounds repeated font references', async t => {
  const { root } = await fixture(t);
  await writeFile(path.join(root, 'site.min.css'), '@font-face{src:url(assets/fa-solid.woff2)}'.repeat(513));
  await assert.rejects(verifyReleaseAssets(root), error => error.code === 'font-reference-limit');
});

test('font URL cannot escape the release with encoded traversal', async t => {
  const { root } = await fixture(t);
  await writeFile(path.join(root, 'site.min.css'), '@font-face{src:url(%2e%2e/outside.woff2)}');
  await assert.rejects(verifyReleaseAssets(root), error => error.code === 'asset-outside-release');
});

test('font URL cannot introduce a remote fetch or absolute path', async t => {
  const { root } = await fixture(t);
  for (const url of ['https://invalid.example.test/font.woff2', '//invalid.example.test/font.woff2', 'C:/outside/font.woff2']) {
    await writeFile(path.join(root, 'site.min.css'), `@font-face{src:url(${url})}`);
    await assert.rejects(verifyReleaseAssets(root), error => error.code === 'invalid-asset-path');
  }
});

test('font path rejects a directory junction or symlink', async t => {
  const { root } = await fixture(t);
  await symlink(path.join(root, 'assets'), path.join(root, 'linked'), process.platform === 'win32' ? 'junction' : 'dir');
  await writeFile(path.join(root, 'site.min.css'), '@font-face{src:url(linked/fa-solid.woff2)}');
  await assert.rejects(verifyReleaseAssets(root), error => error.code === 'symlink-asset');
});

test('malformed UTF8 stylesheet fails closed', async t => {
  const { root } = await fixture(t);
  await writeFile(path.join(root, 'site.min.css'), Buffer.from([0xff, 0xfe, 0xff]));
  await assert.rejects(verifyReleaseAssets(root), error => error.code === 'inspection-incomplete');
});

test('explicit owned error-route font remains validated under the public root', async t => {
  const { root } = await fixture(t);
  await mkdir(path.join(root, 'lib', 'outfit'), { recursive: true });
  const font = await readFile(path.join(root, 'assets', 'fa-solid.woff2'));
  await writeFile(path.join(root, 'lib', 'outfit', 'outfit-latin-400-normal.woff2'), font);
  await writeFile(path.join(root, 'site.min.css'), '@font-face{src:url(assets/fa-solid.woff2)}@font-face{src:url(assets/fa-regular.woff2)}@font-face{src:url(/lib/outfit/outfit-latin-400-normal.woff2)}');
  assert.deepEqual(await verifyReleaseAssets(root, root), { outputs: 4, fonts: 3 });
  await rm(path.join(root, 'lib', 'outfit', 'outfit-latin-400-normal.woff2'));
  await assert.rejects(verifyReleaseAssets(root, root), error => error.code === 'inspection-incomplete');
});

test('CLI entry point is wired after generation without historical gzip asset lists', async () => {
  const packageFile = new URL('../package.json', import.meta.url);
  const manifest = JSON.parse(await readFile(packageFile, 'utf8'));
  assert.equal(manifest.scripts.build, 'node build-assets.mjs && npm run verify:release-assets');
  assert.equal(manifest.scripts['verify:release-assets'], 'node verify-release-assets.mjs');
});


test('encoded missing font extension is classified after URL decoding', async t => {
  const { root } = await fixture(t);
  assert.deepEqual(await verifyReleaseAssets(root), { outputs: 4, fonts: 2 });
  const css = await readFile(path.join(root, 'site.min.css'), 'utf8');
  await writeFile(path.join(root, 'site.min.css'), `${css}@font-face{src:url(assets/missing.%77off2)}`);
  await assert.rejects(verifyReleaseAssets(root), error => error.code === 'inspection-incomplete');
});

test('resolved manifest alias cannot duplicate a shared output', async t => {
  const value = await fixture(t);
  assert.deepEqual(await verifyReleaseAssets(value.root), { outputs: 4, fonts: 2 });
  value.manifest.routeScripts = ['./app.min.js'];
  await value.saveManifest();
  await assert.rejects(verifyReleaseAssets(value.root), error => error.code === 'duplicate-output');
});

test('magic and length alone cannot validate an empty WOFF2 structure', async t => {
  const { root } = await fixture(t);
  assert.deepEqual(await verifyReleaseAssets(root), { outputs: 4, fonts: 2 });
  const font = Buffer.alloc(64);
  font.write('wOF2');
  font.writeUInt32BE(font.length, 8);
  await writeFile(path.join(root, 'assets', 'fa-solid.woff2'), font);
  await assert.rejects(verifyReleaseAssets(root), error => error.code === 'invalid-woff2-header');
});


for (const [name, mutate, code] of [
  ['unsupported flavor', font => font.writeUInt32BE(0, 4), 'invalid-woff2-header'],
  ['zero table count', font => font.writeUInt16BE(0, 12), 'invalid-woff2-header'],
  ['excessive table count', font => font.writeUInt16BE(4097, 12), 'invalid-woff2-header'],
  ['zero compressed size', font => font.writeUInt32BE(0, 20), 'invalid-woff2-header'],
  ['out-of-bounds compressed block', font => font.writeUInt32BE(font.length, 20), 'invalid-woff2-block'],
  ['duplicate table tag', font => { font[50] = font[48]; }, 'invalid-woff2-directory'],
  ['unknown table transform', font => { font[48] |= 0x80; }, 'invalid-woff2-directory'],
  ['nonminimal base128', font => { font[49] = 0x80; }, 'invalid-woff2-directory'],
  ['base128 overflow', font => { font.fill(0xff, 49, 54); }, 'invalid-woff2-directory'],
  ['metadata length without offset', font => font.writeUInt32BE(1, 32), 'invalid-woff2-block'],
  ['metadata original size without offset', font => font.writeUInt32BE(1, 36), 'invalid-woff2-block'],
  ['metadata overlaps header', font => { font.writeUInt32BE(4, 28); font.writeUInt32BE(4, 32); font.writeUInt32BE(4, 36); }, 'invalid-woff2-block'],
  ['private length without offset', font => font.writeUInt32BE(1, 44), 'invalid-woff2-block'],
  ['private block outside file', font => { font.writeUInt32BE(font.length + 4, 40); font.writeUInt32BE(4, 44); }, 'invalid-woff2-block'],
  ['corrupt compressed stream', font => { font.fill(0, 73); }, 'invalid-woff2-stream'],
]) {
  test(`rejects WOFF2 ${name} with valid other fonts present`, async t => {
    const { root } = await fixture(t);
    const file = path.join(root, 'assets', 'fa-solid.woff2');
    const font = await readFile(file);
    mutate(font);
    await writeFile(file, font);
    await assert.rejects(verifyReleaseAssets(root), error => error.code === code);
  });
}

test('reserved and reference-only sfnt size do not falsely reject a real font', async t => {
  const { root } = await fixture(t);
  const file = path.join(root, 'assets', 'fa-solid.woff2');
  const font = await readFile(file);
  font.writeUInt16BE(1, 14);
  font.writeUInt32BE(1, 16);
  await writeFile(file, font);
  assert.deepEqual(await verifyReleaseAssets(root), { outputs: 4, fonts: 2 });
});

test('valid aligned private block is bounded without interpreting vendor bytes', async t => {
  const { root } = await fixture(t);
  const file = path.join(root, 'assets', 'fa-solid.woff2');
  const original = await readFile(file);
  const font = Buffer.concat([original, Buffer.from([1, 2, 3, 4])]);
  font.writeUInt32BE(font.length, 8);
  font.writeUInt32BE(original.length, 40);
  font.writeUInt32BE(4, 44);
  await writeFile(file, font);
  assert.deepEqual(await verifyReleaseAssets(root), { outputs: 4, fonts: 2 });
});

test('extraneous bytes after compressed font data fail closed', async t => {
  const { root } = await fixture(t);
  const file = path.join(root, 'assets', 'fa-solid.woff2');
  const font = Buffer.concat([await readFile(file), Buffer.alloc(4)]);
  font.writeUInt32BE(font.length, 8);
  await writeFile(file, font);
  await assert.rejects(verifyReleaseAssets(root), error => error.code === 'invalid-woff2-block');
});
