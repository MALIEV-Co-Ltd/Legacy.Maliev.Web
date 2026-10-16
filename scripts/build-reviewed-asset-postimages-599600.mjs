#!/usr/bin/env node
// Review-only #599/#600 public asset generation. Never commits, pushes, or deploys.
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { copyFileSync, existsSync, lstatSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { join, resolve, sep } from 'node:path';

const root = resolve(import.meta.dirname, '..');
const assetsRoot = resolve(root, '..', 'assets');
const mode = process.argv[2];
const base = '6fb9850658e8b9a345182dd9b16e6bffeb01aa0a';
const reviewedAssetsHead = '92a52e5ab1e21820bd0adef7a0ca5b2509d68380';
const packetBlob = '8c6ac6c278319124a67f069d3fa02d7e4bb8b3d9';
const patchBlob = 'edd93958766884c54b1cda6670bbb1578b44108d';
const lockBlob = '8c392b351e1c1da0e11807ad7fb9ef79514d0c86';
const packetPath = '.migration-asset-inputs/599600/packet.json';
const patchPath = '.migration-asset-inputs/599600/reviewed.patch';
const outputPath = 'asset-postimages-599600';
const metadataPaths = [
  '.github/workflows/reviewed-asset-postimages-599600.yml',
  'scripts/build-reviewed-asset-postimages-599600.mjs',
  packetPath, patchPath
].sort();
const custodyFiles = [
  ['docs/migration/company-release-assets.v1.json', '6b55e8dd05e009b34fcaeadfd1164a88fd189ed3'],
  ['scripts/verify_company_release_assets.py', 'e1ae5e1f4c7f33e26d585a2d0f885f46e94958e6'],
  ['tests/CompanyReleaseAssetValidator.Tests.py', '4828674b4db33f0a94ea58bb805552b0b91eab78']
];
const maxGeneratedFiles = 2000;
const maxGeneratedBytes = 256 * 1024 * 1024;

function fail(message) { throw new Error(message); }
function command(exe, args, cwd = root) {
  const result = spawnSync(exe, args, { cwd, encoding: 'utf8', maxBuffer: 8 * 1024 * 1024, windowsHide: true });
  if (result.error || result.status !== 0) {
    const diagnostic = Buffer.from(result.stderr || '').subarray(0, 8192).toString('utf8').replaceAll('\0', '');
    fail('command failed: ' + exe + ' ' + args.slice(0, 3).join(' ') +
      ' (exit ' + String(result.status) + ') stderr=' + diagnostic);
  }
  return result.stdout.trimEnd();
}
function sha256(bytes) { return createHash('sha256').update(bytes).digest('hex'); }
function gitBlob(bytes) {
  return createHash('sha1').update(Buffer.from('blob ' + bytes.length + '\0')).update(bytes).digest('hex');
}
function safeSourcePath(path) {
  if (typeof path !== 'string' ||
      (path !== '.github/workflows/_build-and-test.yml' &&
       !/^(Legacy\.Maliev\.Web|Legacy\.Maliev\.Web\.Tests)\/[A-Za-z0-9_./-]+$/.test(path)))
    fail('source path outside allowlist');
  if (path.includes('//') || path.split('/').some(part => part === '.' || part === '..'))
    fail('noncanonical source path');
  return path;
}
function safeAssetPath(path) {
  if (typeof path !== 'string' ||
      !/^Legacy\.Maliev\.Web\/wwwroot\/src\/images\/(?:customers|landing)\/[a-z0-9][a-z0-9-]*\.(?:svg|png|jpe?g|webp|gif)$/.test(path))
    fail('asset path outside reviewed inventory');
  return path;
}
function absolute(path, checkout = root) {
  const target = resolve(checkout, path);
  if (!target.startsWith(checkout + sep)) fail('path escaped checkout');
  return target;
}
function regular(path, checkout = root, allowAbsent = false) {
  const target = absolute(path, checkout);
  if (!existsSync(target)) {
    if (allowAbsent) return null;
    fail('required file absent: ' + path);
  }
  const stat = lstatSync(target);
  if (!stat.isFile() || stat.isSymbolicLink()) fail('nonregular file: ' + path);
  return readFileSync(target);
}
function sameSet(actual, expected) {
  const a = [...actual].sort();
  const e = [...expected].sort();
  if (a.length !== e.length || a.some((item, index) => item !== e[index]))
    fail('unexpected path set');
}
function statusPaths(checkout = root) {
  const result = spawnSync('git', ['status', '--porcelain=v1', '-z', '--untracked-files=all'], {
    cwd: checkout, encoding: 'buffer', maxBuffer: 8 * 1024 * 1024, windowsHide: true
  });
  if (result.error || result.status !== 0) fail('git status failed');
  return result.stdout.toString('utf8').split('\0').filter(Boolean).map(entry => {
    if (![' M', 'M ', '??', ' A', 'A '].includes(entry.slice(0, 2)) || entry[2] !== ' ')
      fail('unexpected Git status form');
    return entry.slice(3).replaceAll('\\', '/');
  });
}
function packetAndPatch() {
  const packetBytes = regular(packetPath);
  const patchBytes = regular(patchPath);
  if (gitBlob(packetBytes) !== packetBlob || gitBlob(patchBytes) !== patchBlob)
    fail('reviewed packet or patch blob mismatch');
  if (packetBytes.length > 512 * 1024 || patchBytes.length > 1024 * 1024)
    fail('reviewed input size exceeded');
  const packet = JSON.parse(packetBytes.toString('utf8'));
  if (packet.schema !== 'web599600.inert-source-overlay.v1' ||
      packet.repository !== 'MALIEV-Co-Ltd/Legacy.Maliev.Web' ||
      packet.exactSourceBase !== base ||
      packet.patch?.gitBlob !== patchBlob ||
      packet['dependency598']?.reviewSourceHead !== reviewedAssetsHead ||
      packet['dependency598']?.assetCount !== 59 ||
      packet['dependency598']?.custodyInventoryGitBlob !== '52aff828aa62d269054b8fa2619d92e3d30f26d9' ||
      !Array.isArray(packet.rows) || packet.rows.length !== 30)
    fail('reviewed packet identity mismatch');
  const paths = packet.rows.map(row => safeSourcePath(row.path));
  if (new Set(paths).size !== 30) fail('duplicate source path');
  const patchText = patchBytes.toString('utf8');
  const headers = [...patchText.matchAll(/^diff --git a\/([^\r\n]+) b\/([^\r\n]+)$/gm)];
  if (headers.length !== 30) fail('patch path count mismatch');
  sameSet(headers.map(hit => {
    if (hit[1] !== hit[2]) fail('patch rename forbidden');
    return safeSourcePath(hit[1]);
  }), paths);
  if (/^(?:old mode|new mode|rename from|rename to|copy from|copy to|GIT binary patch|Binary files)/m.test(patchText))
    fail('unsupported patch operation');
  return { packet, paths };
}
function verifyBase() {
  const head = command('git', ['rev-parse', 'HEAD']);
  if (head !== process.env.REVIEWED_HEAD_SHA ||
      command('git', ['rev-parse', 'HEAD^']) !== base ||
      command('git', ['merge-base', base, head]) !== base)
    fail('metadata-only candidate does not directly descend from reviewed base');
  sameSet(command('git', ['diff', '--name-only', base, head]).split('\n').filter(Boolean), metadataPaths);
  sameSet(statusPaths(), []);
  if (command('git', ['rev-parse', 'HEAD'], assetsRoot) !== reviewedAssetsHead)
    fail('reviewed asset checkout SHA changed');
  sameSet(statusPaths(assetsRoot), []);
  if (gitBlob(regular('Legacy.Maliev.Web/package-lock.json')) !== lockBlob)
    fail('locked dependency preimage mismatch');
  return head;
}
function verifySource(packet, before) {
  for (const row of packet.rows) {
    const path = safeSourcePath(row.path);
    const bytes = regular(path, root, before && row.preimageGitBlob === null);
    if (before && row.preimageGitBlob === null) {
      if (bytes !== null) fail('new source path already exists');
    } else if (bytes === null ||
        gitBlob(bytes) !== (before ? row.preimageGitBlob : row.postimageGitBlob) ||
        (!before && bytes.length !== row.postimageBytes)) {
      fail('source ' + (before ? 'preimage' : 'postimage') + ' mismatch: ' + path);
    }
  }
}
function verifyAssets(packet) {
  const dependency = packet['dependency598'];
  if (!Array.isArray(dependency.assets) || dependency.assets.length !== 59 ||
      !Array.isArray(dependency.custodyFiles) || dependency.custodyFiles.length !== custodyFiles.length)
    fail('asset inventory shape mismatch');
  const seen = new Set();
  let total = 0;
  for (const entry of dependency.assets) {
    const path = safeAssetPath(entry.path);
    if (seen.has(path.toLowerCase())) fail('duplicate asset path');
    seen.add(path.toLowerCase());
    const bytes = regular(path, assetsRoot);
    if (bytes.length !== entry.bytes || gitBlob(bytes) !== entry.gitBlob ||
        sha256(bytes) !== entry.sha256) fail('reviewed asset mismatch: ' + path);
    if (regular(path, root, true) !== null) fail('asset already in source base');
    total += bytes.length;
  }
  if (total > 16 * 1024 * 1024) fail('asset inventory too large');
  sameSet(dependency.custodyFiles.map(row => row.path + ':' + row.gitBlob),
          custodyFiles.map(([path, blob]) => path + ':' + blob));
  for (const [path, blob] of custodyFiles) {
    if (gitBlob(regular(path, assetsRoot)) !== blob || regular(path, root, true) !== null)
      fail('custody file mismatch: ' + path);
  }
  const manifest = JSON.parse(regular(custodyFiles[0][0], assetsRoot).toString('utf8'));
  if (!Array.isArray(manifest.assets) || manifest.assets.length !== 59)
    fail('custody manifest row count changed');
  sameSet(manifest.assets.map(entry =>
    safeAssetPath(entry.targetPath) + ':' + entry.sourceBlob + ':' + entry.bytes + ':' + entry.sha256),
    dependency.assets.map(entry =>
      entry.path + ':' + entry.gitBlob + ':' + entry.bytes + ':' + entry.sha256));
  return { count: seen.size, bytes: total };
}
function stageFiles(packet) {
  for (const row of packet['dependency598'].assets) {
    const path = safeAssetPath(row.path);
    const target = absolute(path);
    mkdirSync(resolve(target, '..'), { recursive: true });
    copyFileSync(absolute(path, assetsRoot), target);
    const bytes = regular(path);
    if (bytes.length !== row.bytes || gitBlob(bytes) !== row.gitBlob ||
        sha256(bytes) !== row.sha256) fail('staged asset differs: ' + path);
  }
  for (const [path, blob] of custodyFiles) {
    const target = absolute(path);
    mkdirSync(resolve(target, '..'), { recursive: true });
    copyFileSync(absolute(path, assetsRoot), target);
    if (gitBlob(regular(path)) !== blob) fail('staged custody file differs: ' + path);
  }
  sameSet(statusPaths(), [
    ...packet['dependency598'].assets.map(entry => entry.path),
    ...custodyFiles.map(([path]) => path)
  ]);
}
function prepare() {
  const head = verifyBase();
  const { packet, paths } = packetAndPatch();
  verifySource(packet, true);
  const assetInventory = verifyAssets(packet);
  stageFiles(packet);
  command('git', ['apply', '--check', '--whitespace=error', '--', patchPath]);
  command('git', ['apply', '--whitespace=error', '--', patchPath]);
  verifySource(packet, false);
  sameSet(statusPaths(), [
    ...packet['dependency598'].assets.map(entry => entry.path),
    ...custodyFiles.map(([path]) => path),
    ...paths
  ]);
  const output = absolute(outputPath);
  mkdirSync(output, { recursive: false });
  const binding = {
    schema: 1, purpose: 'review-only-ephemeral-599600-generated-assets',
    base, metadataHead: head, reviewedAssetsHead,
    packetGitBlob: packetBlob, patchGitBlob: patchBlob,
    packageLockGitBlob: lockBlob, assetInventory,
    sourcePostimages: packet.rows.map(row => ({
      path: row.path, gitBlob: row.postimageGitBlob, bytes: row.postimageBytes
    })),
    custodyGitBlobs: custodyFiles.map(([path, gitBlob]) => ({ path, gitBlob })),
    acceptance: 'NOT ACTIVE RUNTIME; #598 DEPENDENCY AND NORMAL HOSTED GATES STILL REQUIRED'
  };
  writeFileSync(join(output, 'binding.json'), JSON.stringify(binding, null, 2) + '\n');
}
function walk(dir, output) {
  const full = absolute(dir);
  if (!existsSync(full) || !lstatSync(full).isDirectory()) fail('generated directory absent');
  for (const name of readdirSync(full).sort()) {
    const path = dir + '/' + name;
    const stat = lstatSync(absolute(path));
    if (stat.isSymbolicLink()) fail('generated symlink forbidden');
    if (stat.isDirectory()) walk(path, output);
    else if (stat.isFile()) {
      if (stat.size > maxGeneratedBytes) fail('generated file too large');
      output.push(path);
      if (output.length > maxGeneratedFiles) fail('too many generated files');
    } else fail('unsupported generated file');
  }
}
function collect() {
  const binding = JSON.parse(regular(outputPath + '/binding.json').toString('utf8'));
  const head = command('git', ['rev-parse', 'HEAD']);
  if (head !== process.env.REVIEWED_HEAD_SHA || binding.metadataHead !== head ||
      binding.base !== base || binding.reviewedAssetsHead !== reviewedAssetsHead ||
      binding.packetGitBlob !== packetBlob || binding.patchGitBlob !== patchBlob)
    fail('binding changed after build');
  const { packet, paths } = packetAndPatch();
  verifySource(packet, false);
  if (gitBlob(regular('Legacy.Maliev.Web/package-lock.json')) !== lockBlob)
    fail('package lock changed after build');
  for (const entry of packet['dependency598'].assets) {
    const bytes = regular(safeAssetPath(entry.path));
    if (bytes.length !== entry.bytes || gitBlob(bytes) !== entry.gitBlob ||
        sha256(bytes) !== entry.sha256) fail('staged asset changed during build');
  }
  for (const [path, blob] of custodyFiles)
    if (gitBlob(regular(path)) !== blob) fail('custody file changed during build');
  const changed = statusPaths().filter(path => !path.startsWith(outputPath + '/'));
  const allowed = [
    ...paths,
    ...packet['dependency598'].assets.map(entry => entry.path),
    ...custodyFiles.map(([path]) => path)
  ];
  if (changed.some(path => !allowed.includes(path) &&
      !path.startsWith('Legacy.Maliev.Web/wwwroot/dist/') &&
      !path.startsWith('Legacy.Maliev.Web/wwwroot/src/vendor/three/')))
    fail('build changed path outside reviewed source and public generated roots');
  if (allowed.some(path => !changed.includes(path))) fail('reviewed staged path absent from status');
  const generated = [];
  walk('Legacy.Maliev.Web/wwwroot/dist', generated);
  walk('Legacy.Maliev.Web/wwwroot/src/vendor/three', generated);
  if (!generated.length) fail('no generated output');
  for (const required of [
    'Legacy.Maliev.Web/wwwroot/dist/route-home.js',
    'Legacy.Maliev.Web/wwwroot/dist/route-home.css',
    'Legacy.Maliev.Web/wwwroot/dist/instant-quotation-workflow.mjs',
    'Legacy.Maliev.Web/wwwroot/dist/asset-manifest.json'
  ]) {
    if (!generated.includes(required) || !changed.includes(required))
      fail('required generated postimage absent: ' + required);
  }
  const destination = absolute(outputPath);
  let totalBytes = 0;
  const files = generated.map(path => {
    const bytes = regular(path);
    totalBytes += bytes.length;
    if (totalBytes > maxGeneratedBytes) fail('generated inventory too large');
    const target = join(destination, ...path.split('/'));
    mkdirSync(resolve(target, '..'), { recursive: true });
    copyFileSync(absolute(path), target);
    return { path, bytes: bytes.length, sha256: sha256(bytes), gitBlob: gitBlob(bytes) };
  });
  const manifest = {
    ...binding, runId: process.env.GITHUB_RUN_ID || null,
    runAttempt: process.env.GITHUB_RUN_ATTEMPT || null,
    generated: files, generatedFileCount: files.length,
    generatedTotalBytes: totalBytes, changedPaths: changed.sort()
  };
  writeFileSync(join(destination, 'manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
  const publicPaths = changed.filter(path => path.startsWith('Legacy.Maliev.Web/wwwroot/dist/')).sort();
  if (!publicPaths.length || publicPaths.length > 64) fail('public changed output count outside bound');
  const records = [];
  const record = value => records.push('PUBLIC_ASSET_599600_V1 ' + JSON.stringify(value) + '\n');
  record({
    kind: 'binding', schema: 1, base, metadataHead: head, reviewedAssetsHead,
    packetGitBlob: packetBlob, patchGitBlob: patchBlob,
    runId: manifest.runId, runAttempt: manifest.runAttempt,
    generatedFileCount: files.length, generatedTotalBytes: totalBytes, publicPaths
  });
  for (const path of publicPaths) {
    const sealed = files.find(file => file.path === path);
    if (!sealed) fail('public postimage not in generated inventory');
    const bytes = regular(outputPath + '/' + path);
    if (bytes.length !== sealed.bytes || sha256(bytes) !== sealed.sha256 ||
        gitBlob(bytes) !== sealed.gitBlob || bytes.length > 2 * 1024 * 1024)
      fail('public artifact copy invalid or too large');
    const encoded = bytes.toString('base64');
    const length = 16384;
    const count = Math.ceil(encoded.length / length);
    for (let index = 0; index < count; index++)
      record({
        kind: 'chunk', path, gitBlob: sealed.gitBlob, sha256: sealed.sha256,
        bytes: sealed.bytes, partIndex: index, partCount: count,
        base64: encoded.slice(index * length, (index + 1) * length)
      });
  }
  record({ kind: 'complete', schema: 1, metadataHead: head, publicFileCount: publicPaths.length });
  if (records.reduce((sum, line) => sum + Buffer.byteLength(line), 0) > 8 * 1024 * 1024)
    fail('bounded public output transport exceeded 8 MiB');
  for (const line of records) process.stdout.write(line);
}
if (mode === 'prepare') prepare();
else if (mode === 'collect') collect();
else fail('usage: node script prepare|collect');
