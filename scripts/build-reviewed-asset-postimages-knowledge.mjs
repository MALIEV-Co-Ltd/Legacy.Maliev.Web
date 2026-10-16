#!/usr/bin/env node
// Review-only Knowledge public asset generation. Never commits, pushes, or deploys.
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { copyFileSync, existsSync, lstatSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { join, resolve, sep } from 'node:path';

const root = resolve(import.meta.dirname, '..');
const mode = process.argv[2];
const base = 'ce2a005bae0a7b915f4feb6daf350c81285c1e8f';
const packetBlob = '35c549ac626af582e0aa81304cca8953af0aac18';
const maxInputBytes = 16 * 1024 * 1024;
const lockBlob = '8c392b351e1c1da0e11807ad7fb9ef79514d0c86';
const packetPath = '.migration-asset-inputs/knowledge/packet.json';
const validatorPath = '.migration-asset-inputs/knowledge/validate-custody.py';
const outputPath = 'asset-postimages-knowledge';
const metadataPaths = [
  '.github/workflows/reviewed-asset-postimages-knowledge.yml',
  'scripts/build-reviewed-asset-postimages-knowledge.mjs',
  packetPath, validatorPath
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
  const result = spawnSync(exe, args, { cwd, encoding: 'utf8', maxBuffer: 8 * 1024 * 1024, windowsHide: true, timeout: 120000 });
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
      !/^(?:Legacy\.Maliev\.Web|Legacy\.Maliev\.Web\.Tests|docs|scripts|tests|\.github)\/[A-Za-z0-9_./-]+$/.test(path))
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
    cwd: checkout, encoding: 'buffer', maxBuffer: 8 * 1024 * 1024, windowsHide: true, timeout: 15000
  });
  if (result.error || result.status !== 0) fail('git status failed');
  return result.stdout.toString('utf8').split('\0').filter(Boolean).map(entry => {
    if (![' M', 'M ', '??', ' A', 'A '].includes(entry.slice(0, 2)) || entry[2] !== ' ')
      fail('unexpected Git status form');
    return entry.slice(3).replaceAll('\\', '/');
  });
}
function packetAndRows() {
  const bytes = regular(packetPath);
  if (bytes.length > 512 * 1024 || gitBlob(bytes) !== packetBlob) fail('sealed input mismatch');
  const packet = JSON.parse(bytes.toString('utf8'));
  if (packet.schema !== 'web-knowledge.inert-object-overlay.v1' ||
      packet.repository !== 'MALIEV-Co-Ltd/Legacy.Maliev.Web' ||
      packet.exactSourceBase !== base || packet.packageLockGitBlob !== lockBlob ||
      packet.sourceCommit !== '770cb58032969ae4a0fe169686b53ab61a44fff8' ||
      !Array.isArray(packet.rows) || packet.rows.length !== 182 ||
      !Array.isArray(packet.existingGenerated) || packet.existingGenerated.length !== 47 ||
      gitBlob(regular(validatorPath)) !== packet.custodyValidatorBlob)
    fail('sealed input identity mismatch');
  const paths = packet.rows.map(row => safeSourcePath(row.path));
  if (new Set(paths.map(path => path.toLowerCase())).size !== 182) fail('duplicate source path');
  for (const row of packet.rows) {
    if (!/^[0-9a-f]{40}$/.test(row.postimage) ||
        !(row.preimage === null || /^[0-9a-f]{40}$/.test(row.preimage)) ||
        row.preimage === row.postimage || !Number.isSafeInteger(row.bytes) || row.bytes < 1 || row.bytes > 8 * 1024 * 1024 ||
        (row.sha256 !== undefined && !/^[0-9a-f]{64}$/.test(row.sha256)))
      fail('invalid source binding');
  }
  return { packet, paths };
}
async function acquire(sha) {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 30000);
  let reader;
  try {
    const response = await fetch('https://api.github.com/repos/MALIEV-Co-Ltd/Legacy.Maliev.Web/git/blobs/' + sha, {
      method: 'GET', redirect: 'error', signal: controller.signal,
      headers: { Accept: 'application/vnd.github+json', Authorization: 'Bearer ' + process.env.GITHUB_TOKEN,
        'X-GitHub-Api-Version': '2022-11-28' }
    });
    if (!response.ok || !response.body) fail('sealed object GET failed: ' + response.status);
    reader = response.body.getReader();
    const chunks = []; let count = 0;
    for (;;) {
      const next = await reader.read();
      if (next.done) break;
      count += next.value.byteLength;
      if (count > 12 * 1024 * 1024) fail('object response size exceeded');
      chunks.push(Buffer.from(next.value));
    }
    const object = JSON.parse(Buffer.concat(chunks).toString('utf8'));
    if (object.sha !== sha || object.encoding !== 'base64' ||
        !Number.isSafeInteger(object.size) || object.size < 1 || object.size > 8 * 1024 * 1024 ||
        typeof object.content !== 'string' || !/^[A-Za-z0-9+/=\r\n]+$/.test(object.content))
      fail('invalid sealed object response');
    const bytes = Buffer.from(object.content.replace(/[\r\n]/g, ''), 'base64');
    if (bytes.length !== object.size || gitBlob(bytes) !== sha) fail('sealed object bytes mismatch');
    return bytes;
  } finally {
    controller.abort();
    clearTimeout(timeout);
    try { await reader?.cancel(); } catch { /* Response already closed or aborted. */ }
    reader?.releaseLock();
    controller.abort();
  }
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
    const bytes = regular(safeSourcePath(row.path), root, before && row.preimage === null);
    if (before && row.preimage === null) {
      if (bytes !== null) fail('new source path already exists');
    } else if (bytes === null || gitBlob(bytes) !== (before ? row.preimage : row.postimage) ||
        (!before && row.bytes !== undefined && bytes.length !== row.bytes) ||
        (!before && row.sha256 !== undefined && sha256(bytes) !== row.sha256))
      fail('source binding mismatch: ' + row.path);
  }
}
function verifyCustody(packet) {
  for (const [path, blob] of custodyFiles)
    if (gitBlob(regular(path)) !== blob) fail('custody file mismatch: ' + path);
  const manifest = JSON.parse(regular(custodyFiles[0][0]).toString('utf8'));
  if (manifest.schema !== 'maliev-company-release-assets.v1' ||
      !Array.isArray(manifest.assets) || manifest.assets.length !== 59)
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
  if (seen.size !== 59 || total !== 3947803 ||
      total > 16 * 1024 * 1024) fail('custody asset total mismatch');
  return { count: seen.size, bytes: total, manifestGitBlob: custodyFiles[0][1] };
}
async function prepare() {
  const head = verifyBase();
  const { packet, paths } = packetAndRows();
  verifySource(packet, true);
  const custody = verifyCustody(packet);
  for (const row of packet.existingGenerated)
    if (gitBlob(regular(row.path)) !== row.blob) fail('generated preimage mismatch');
  for (const [path, blob] of [
    ['Legacy.Maliev.Web/package.json', packet.packageGitBlob],
    ['Legacy.Maliev.Web/build-assets.mjs', packet.buildScriptGitBlob]
  ]) if (gitBlob(regular(path)) !== blob) fail('pipeline pin mismatch');
  if (!process.env.GITHUB_TOKEN || process.env.GITHUB_REPOSITORY !== packet.repository)
    fail('read-only object acquisition context unavailable');
  const payloads = new Map(); let total = 0;
  const deadline = Date.now() + 11 * 60 * 1000;
  for (const row of packet.rows) {
    if (Date.now() >= deadline) fail('source acquisition deadline exceeded');
    const bytes = await acquire(row.postimage);
    total += bytes.length;
    if (total > maxInputBytes || (row.bytes !== undefined && bytes.length !== row.bytes) ||
        (row.sha256 !== undefined && sha256(bytes) !== row.sha256)) fail('input byte quota or binding mismatch');
    payloads.set(row.path, bytes);
  }
  // Every object is byte-qualified before the first ephemeral checkout write.
  for (const row of packet.rows) {
    const path = absolute(row.path);
    mkdirSync(resolve(path, '..'), { recursive: true });
    writeFileSync(path, payloads.get(row.path), { flag: row.preimage === null ? 'wx' : 'w' });
  }
  payloads.clear();
  delete process.env.GITHUB_TOKEN;
  verifySource(packet, false);
  sameSet(statusPaths(), paths);
  const output = absolute(outputPath);
  mkdirSync(output, { recursive: false });
  const binding = {
    schema: 1, purpose: 'review-only-ephemeral-knowledge-generated-assets',
    base, metadataHead: head, packetGitBlob: packetBlob,
    packageLockGitBlob: lockBlob, custody,
    sourceCommit: packet.sourceCommit, sourceInputCount: paths.length,
    existingGenerated: packet.existingGenerated,
    sourcePostimages: packet.rows.map(row => ({ path: row.path, gitBlob: row.postimage })),
    acceptance: 'NOT ACTIVE RUNTIME; NORMAL EXACT-HEAD HOSTED GATES STILL REQUIRED'
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
      binding.packageLockGitBlob !== lockBlob)
    fail('binding changed after build');
  const { packet, paths } = packetAndRows();
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
  for (const row of packet.existingGenerated)
    if (!generated.includes(row.path)) fail('existing generated output lost: ' + row.path);
  for (const required of [
    'Legacy.Maliev.Web/wwwroot/dist/route-knowledge.js',
    'Legacy.Maliev.Web/wwwroot/dist/site.min.css'
  ]) {
    if (!generated.includes(required) || !changed.includes(required))
      fail('required Knowledge generated postimage absent: ' + required);
  }
  const assetManifestPath = 'Legacy.Maliev.Web/wwwroot/dist/asset-manifest.json';
  if (!generated.includes(assetManifestPath))
    fail('generated asset manifest absent');
  const assetManifest = JSON.parse(regular(assetManifestPath).toString('utf8'));
  sameSet(Object.keys(assetManifest), [
    'scripts', 'routeScripts', 'routeScopedModules', 'styles', 'routeStyles'
  ]);
  const manifestEntries = [];
  for (const section of ['scripts', 'routeScripts', 'styles', 'routeStyles']) {
    if (!Array.isArray(assetManifest[section]) || !assetManifest[section].length)
      fail('generated asset manifest section invalid: ' + section);
    manifestEntries.push(...assetManifest[section]);
  }
  if (assetManifest.routeScopedModules === null ||
      typeof assetManifest.routeScopedModules !== 'object' ||
      Array.isArray(assetManifest.routeScopedModules))
    fail('generated asset manifest modules invalid');
  sameSet(Object.keys(assetManifest.routeScopedModules), [
    'instantQuotationViewer', 'instantQuotationWorkflow'
  ]);
  manifestEntries.push(...Object.values(assetManifest.routeScopedModules));
  if (!assetManifest.routeScripts.includes('route-knowledge.js') ||
      !assetManifest.styles.includes('site.min.css') ||
      manifestEntries.some(name => typeof name !== 'string' ||
        !/^[a-z0-9][a-z0-9.-]*\.(?:js|mjs|css)$/.test(name) ||
        !generated.includes('Legacy.Maliev.Web/wwwroot/dist/' + name)) ||
      new Set(manifestEntries).size !== manifestEntries.length)
    fail('generated asset manifest references invalid');
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
  const publicPaths = changed.filter(path => path.startsWith('Legacy.Maliev.Web/wwwroot/dist/') ||
    path.startsWith('Legacy.Maliev.Web/wwwroot/src/vendor/three/')).sort();
  if (!publicPaths.length || publicPaths.length > 64)
    fail('public changed output count outside bound');
  const records = [];
  const record = value => records.push('PUBLIC_ASSET_KNOWLEDGE_V1 ' + JSON.stringify(value) + '\n');
  record({
    kind: 'binding', schema: 1, base, metadataHead: head,
    packetGitBlob: packetBlob,
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
if (mode === 'prepare') await prepare();
else if (mode === 'collect') collect();
else fail('usage: node script prepare|collect');
