#!/usr/bin/env node
// Review-only About public asset generation. Never commits, pushes, or deploys.
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { copyFileSync, existsSync, lstatSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { join, resolve, sep } from 'node:path';

const root = resolve(import.meta.dirname, '..');
const mode = process.argv[2];
const base = '7b5ed742df5ff7ea37d2632b7e5b25d59012d593';
const packetBlob = '4c2566a692351d18eb306db8c2e1653c3ad284ef';
const patchBlob = 'f540898e8e74295bab9a5f016fb4d7b041ea2306';
const lockBlob = '8c392b351e1c1da0e11807ad7fb9ef79514d0c86';
const packetPath = '.migration-asset-inputs/about/packet.json';
const patchPath = '.migration-asset-inputs/about/reviewed.patch';
const outputPath = 'asset-postimages-about';
const metadataPaths = [
  '.github/workflows/reviewed-asset-postimages-about.yml',
  'scripts/build-reviewed-asset-postimages-about.mjs',
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
      !/^(Legacy\.Maliev\.Web|Legacy\.Maliev\.Web\.Tests)\/[A-Za-z0-9_./-]+$/.test(path))
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
  let current = checkout;
  for (const part of target.slice(checkout.length + 1).split(sep).slice(0, -1)) {
    current = join(current, part);
    if (existsSync(current)) {
      const ancestor = lstatSync(current);
      if (!ancestor.isDirectory() || ancestor.isSymbolicLink())
        fail('nonregular path ancestor: ' + path);
    }
  }
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
  if (packet.schema !== 'web-about.inert-source-overlay.v1' ||
      packet.repository !== 'MALIEV-Co-Ltd/Legacy.Maliev.Web' ||
      packet.exactSourceBase !== base ||
      packet.sourceCommit !== '3c052083d04a46bf36a7e19695ffc5d339300c86' ||
      packet.reviewedPackets?.markup !== '6e0bf66d061b586996546c43f351f201ed6f3503' ||
      packet.reviewedPackets?.behavior !== '09ea46a9bb8886fc900b08db594e925f9509fbfb' ||
      packet.patch?.gitBlob !== patchBlob ||
      packet.patch?.paths !== 25 ||
      packet.packageLockGitBlob !== lockBlob ||
      packet.custody?.manifestGitBlob !== custodyFiles[0][1] ||
      packet.custody?.validatorGitBlob !== custodyFiles[1][1] ||
      packet.custody?.testGitBlob !== custodyFiles[2][1] ||
      packet.custody?.assetCount !== 59 ||
      packet.custody?.totalBytes !== 3947803 ||
      !Array.isArray(packet.rows) || packet.rows.length !== 25)
    fail('reviewed packet identity mismatch');
  const paths = packet.rows.map(row => safeSourcePath(row.path));
  if (new Set(paths).size !== 25) fail('duplicate source path');
  const patchText = patchBytes.toString('utf8');
  const headers = [...patchText.matchAll(/^diff --git a\/([^\r\n]+) b\/([^\r\n]+)$/gm)];
  if (headers.length !== 25) fail('patch path count mismatch');
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
function verifyCustody(packet) {
  for (const [path, blob] of custodyFiles)
    if (gitBlob(regular(path)) !== blob) fail('custody file mismatch: ' + path);
  const manifest = JSON.parse(regular(custodyFiles[0][0]).toString('utf8'));
  if (manifest.schema !== 'maliev-company-release-assets.v1' ||
      !Array.isArray(manifest.assets) || manifest.assets.length !== packet.custody.assetCount)
    fail('custody manifest shape mismatch');
  const seen = new Set();
  let total = 0;
  for (const row of manifest.assets) {
    const path = safeAssetPath(row.targetPath);
    if (seen.has(path.toLowerCase())) fail('duplicate custody asset path');
    seen.add(path.toLowerCase());
    const bytes = regular(path);
    if (!Number.isSafeInteger(row.bytes) || row.bytes < 0 ||
        bytes.length !== row.bytes || gitBlob(bytes) !== row.sourceBlob ||
        sha256(bytes) !== row.sha256)
      fail('custody asset mismatch: ' + path);
    total += bytes.length;
  }
  if (seen.size !== 59 || total !== packet.custody.totalBytes ||
      total > 16 * 1024 * 1024) fail('custody asset total mismatch');
  return { count: seen.size, bytes: total, manifestGitBlob: custodyFiles[0][1] };
}
function prepare() {
  const head = verifyBase();
  const { packet, paths } = packetAndPatch();
  verifySource(packet, true);
  const custody = verifyCustody(packet);
  command('git', ['apply', '--check', '--whitespace=error', '--', patchPath]);
  command('git', ['apply', '--whitespace=error', '--', patchPath]);
  verifySource(packet, false);
  sameSet(statusPaths(), paths);
  const output = absolute(outputPath);
  mkdirSync(output, { recursive: false });
  const binding = {
    schema: 1, purpose: 'review-only-ephemeral-about-generated-assets',
    base, metadataHead: head,
    packetGitBlob: packetBlob, patchGitBlob: patchBlob,
    packageLockGitBlob: lockBlob, custody,
    sourceCommit: packet.sourceCommit,
    sourcePostimages: packet.rows.map(row => ({
      path: row.path, gitBlob: row.postimageGitBlob, bytes: row.postimageBytes
    })),
    acceptance: 'NOT ACTIVE RUNTIME; 608 PARENT AND NORMAL HOSTED GATES STILL REQUIRED'
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
      binding.base !== base || binding.packetGitBlob !== packetBlob ||
      binding.patchGitBlob !== patchBlob || binding.packageLockGitBlob !== lockBlob)
    fail('binding changed after build');
  const { packet, paths } = packetAndPatch();
  verifySource(packet, false);
  if (gitBlob(regular('Legacy.Maliev.Web/package-lock.json')) !== lockBlob)
    fail('package lock changed after build');
  const custody = verifyCustody(packet);
  if (JSON.stringify(custody) !== JSON.stringify(binding.custody))
    fail('custody changed after build');
  const changed = statusPaths().filter(path => !path.startsWith(outputPath + '/'));
  if (changed.some(path => !paths.includes(path) &&
      !path.startsWith('Legacy.Maliev.Web/wwwroot/dist/') &&
      !path.startsWith('Legacy.Maliev.Web/wwwroot/src/vendor/three/')))
    fail('build changed path outside reviewed source and public generated roots');
  if (paths.some(path => !changed.includes(path)))
    fail('reviewed staged source path absent from Git status');
  const generated = [];
  walk('Legacy.Maliev.Web/wwwroot/dist', generated);
  walk('Legacy.Maliev.Web/wwwroot/src/vendor/three', generated);
  if (!generated.length) fail('no generated output');
  for (const required of [
    'Legacy.Maliev.Web/wwwroot/dist/route-about.js',
    'Legacy.Maliev.Web/wwwroot/dist/route-about.css',
    'Legacy.Maliev.Web/wwwroot/dist/asset-manifest.json'
  ]) {
    if (!generated.includes(required) || !changed.includes(required))
      fail('required About generated postimage absent: ' + required);
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
  if (!publicPaths.length || publicPaths.length > 64)
    fail('public changed output count outside bound');
  const records = [];
  const record = value => records.push('PUBLIC_ASSET_ABOUT_V1 ' + JSON.stringify(value) + '\n');
  record({
    kind: 'binding', schema: 1, base, metadataHead: head,
    packetGitBlob: packetBlob, patchGitBlob: patchBlob,
    custody, runId: manifest.runId, runAttempt: manifest.runAttempt,
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
