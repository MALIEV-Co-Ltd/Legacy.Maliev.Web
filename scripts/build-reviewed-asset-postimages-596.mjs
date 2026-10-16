#!/usr/bin/env node
// Review-only asset generation for Web #596. This never commits or pushes.
import { createHash } from 'node:crypto';
import { copyFileSync, existsSync, lstatSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { join, resolve, relative, sep } from 'node:path';
import { spawnSync } from 'node:child_process';

const root = resolve(import.meta.dirname, '..');
const mode = process.argv[2];
const base = 'fc3bebeacbcb4f9932c09973a8d4913104b67b68';
const packetSha = 'cfa4b4c974ae05cead02da86b1b3d90e112418a9';
const patchSha = '52a70c24c6826c2d7893197703794561143bccd2';
const lockSha = '8c392b351e1c1da0e11807ad7fb9ef79514d0c86';
const packetPath = '.migration-asset-inputs/596/packet.json';
const patchPath = '.migration-asset-inputs/596/reviewed.patch';
const outputPath = 'asset-postimages-596';
const metadataPaths = [
  '.github/workflows/reviewed-asset-postimages-596.yml',
  'scripts/build-reviewed-asset-postimages-596.mjs',
  packetPath,
  patchPath
].sort();
const maxFiles = 2000;
const maxBytes = 256 * 1024 * 1024;

function fail(message) { throw new Error(message); }
function command(exe, args) {
  const result = spawnSync(exe, args, { cwd: root, encoding: 'utf8', maxBuffer: 8 * 1024 * 1024, windowsHide: true });
  if (result.error || result.status !== 0) {
    // Print only a bounded diagnostic. Never print stdout or the patch contents.
    const diagnostic = Buffer.from(result.stderr || '').subarray(0, 8192).toString('utf8').replaceAll('\0', '');
    fail('command failed: ' + exe + ' ' + args.slice(0, 3).join(' ') +
      ' (exit ' + String(result.status) + ') stderr=' + diagnostic);
  }
  return result.stdout.trimEnd();
}
function sha256(bytes) { return createHash('sha256').update(bytes).digest('hex'); }
function gitBlob(bytes) {
  const prefix = Buffer.from('blob ' + bytes.length + '\0', 'utf8');
  return createHash('sha1').update(prefix).update(bytes).digest('hex');
}
function safePath(path) {
  if (typeof path !== 'string' || (path !== '.github/workflows/_build-and-test.yml' && !/^(Legacy\.Maliev\.Web|Legacy\.Maliev\.Web\.Tests)\/[A-Za-z0-9_./-]+$/.test(path)))
    fail('path outside allowed source roots');
  const normalized = path.replaceAll('\\', '/');
  if (normalized.includes('//') || normalized.split('/').includes('..') || normalized.split('/').includes('.'))
    fail('noncanonical source path');
  return normalized;
}
function absolute(path) {
  const target = resolve(root, path);
  if (!target.startsWith(root + sep)) fail('path escaped checkout');
  return target;
}
function regular(path, allowAbsent = false) {
  const target = absolute(path);
  if (!existsSync(target)) {
    if (allowAbsent) return null;
    fail('required file absent: ' + path);
  }
  const stat = lstatSync(target);
  if (!stat.isFile() || stat.isSymbolicLink()) fail('nonregular file: ' + path);
  return readFileSync(target);
}
function statusPaths() {
  const result = spawnSync('git', ['status', '--porcelain=v1', '-z', '--untracked-files=all'], {
    cwd: root, encoding: 'buffer', maxBuffer: 8 * 1024 * 1024, windowsHide: true
  });
  if (result.error || result.status !== 0) fail('git status failed');
  const entries = result.stdout.toString('utf8').split('\0').filter(Boolean);
  return entries.map(entry => {
    const state = entry.slice(0, 2);
    if (![' M', 'M ', '??', ' A', 'A '].includes(state) || entry[2] !== ' ')
      fail('unexpected Git status form');
    return entry.slice(3).replaceAll('\\', '/');
  });
}
function sameSet(actual, expected) {
  const a = [...actual].sort();
  const e = [...expected].sort();
  if (a.length !== e.length || a.some((value, i) => value !== e[i])) fail('unexpected path set');
}
function verifyInputs() {
  const head = command('git', ['rev-parse', 'HEAD']);
  const firstMetadataHead = '0373344c9a21d49665e3ab6bf656f1c309a6fb6e';
  const secondMetadataHead = 'e5178233a7f031c0cb5ade1af60625a0e3d3b579';
  if (command('git', ['merge-base', base, head]) !== base)
    fail('candidate does not descend from reviewed base');
  if (head === firstMetadataHead) {
    if (command('git', ['rev-parse', 'HEAD^']) !== base) fail('first metadata commit parent changed');
  } else if (head === secondMetadataHead) {
    if (command('git', ['rev-parse', 'HEAD^']) !== firstMetadataHead ||
        command('git', ['rev-parse', 'HEAD^^']) !== base)
      fail('second metadata commit ancestry changed');
  } else if (command('git', ['rev-parse', 'HEAD^']) !== secondMetadataHead ||
             command('git', ['rev-parse', 'HEAD^^']) !== firstMetadataHead ||
             command('git', ['rev-parse', 'HEAD^^^']) !== base) {
    fail('candidate is not the reviewed metadata-only third commit');
  }
  if (process.env.REVIEWED_HEAD_SHA !== head) fail('workflow is not running the exact PR head');
  sameSet(command('git', ['diff', '--name-only', base, head]).split('\n').filter(Boolean), metadataPaths);
  sameSet(statusPaths(), []);
  const packetBytes = regular(packetPath);
  const patchBytes = regular(patchPath);
  if (gitBlob(packetBytes) !== packetSha || gitBlob(patchBytes) !== patchSha) fail('reviewed input blob mismatch');
  if (patchBytes.length > 1024 * 1024 || packetBytes.length > 512 * 1024) fail('reviewed input too large');
  const packet = JSON.parse(packetBytes.toString('utf8'));
  if (packet.schema !== 1 || packet.repository !== 'MALIEV-Co-Ltd/Legacy.Maliev.Web' ||
      packet.exactBase !== base || packet.patch?.blob !== patchSha ||
      !Array.isArray(packet.files) || packet.files.length !== 44) fail('reviewed packet identity mismatch');
  const paths = packet.files.map(file => safePath(file.path));
  if (new Set(paths).size !== 44) fail('duplicate source path');
  const headers = [...patchBytes.toString('utf8').matchAll(/^diff --git a\/([^\r\n]+) b\/([^\r\n]+)$/gm)];
  if (headers.length !== 44) fail('patch path count mismatch');
  sameSet(headers.map(hit => {
    if (hit[1] !== hit[2]) fail('patch rename is forbidden');
    return safePath(hit[1]);
  }), paths);
  if (/^(?:old mode|new mode|rename from|rename to|copy from|copy to|GIT binary patch|Binary files)/m.test(patchBytes.toString('utf8')))
    fail('unsupported patch operation');
  for (const file of packet.files) {
    const before = regular(file.path, file.baseSha === null);
    if (file.baseSha === null) {
      if (before !== null) fail('new file exists before patch');
    } else if (before === null || gitBlob(before) !== file.baseSha) {
      fail('source preimage mismatch: ' + file.path);
    }
  }
  if (gitBlob(regular('Legacy.Maliev.Web/package-lock.json')) !== lockSha)
    fail('package lock differs from reviewed base');
  return { packet, head, paths };
}
function verifyPostimages(packet, paths) {
  for (const file of packet.files) {
    const bytes = regular(file.path);
    if (gitBlob(bytes) !== file.sha || bytes.length !== file.bytes)
      fail('source postimage mismatch: ' + file.path);
  }
  return paths;
}
function prepare() {
  const { packet, head, paths } = verifyInputs();
  command('git', ['apply', '--check', '--whitespace=error', '--', patchPath]);
  command('git', ['apply', '--whitespace=error', '--', patchPath]);
  verifyPostimages(packet, paths);
  sameSet(statusPaths(), paths);
  const destination = absolute(outputPath);
  mkdirSync(destination, { recursive: false });
  const binding = {
    schema: 1, purpose: 'review-only-generated-assets', issue: 596,
    base, head, packetGitBlob: packetSha, patchGitBlob: patchSha,
    packageLockGitBlob: lockSha,
    sourcePostimages: packet.files.map(({ path, sha, bytes }) => ({ path, gitBlob: sha, bytes }))
  };
  writeFileSync(join(destination, 'binding.json'), JSON.stringify(binding, null, 2) + '\n');
}
function walk(dir, collected) {
  const full = absolute(dir);
  if (!existsSync(full) || !lstatSync(full).isDirectory()) fail('expected generated directory absent');
  for (const name of readdirSync(full).sort()) {
    const path = dir + '/' + name;
    const stat = lstatSync(absolute(path));
    if (stat.isSymbolicLink()) fail('generated symlink forbidden');
    if (stat.isDirectory()) walk(path, collected);
    else if (stat.isFile()) {
      if (stat.size > maxBytes) fail('generated file too large');
      collected.push(path);
      if (collected.length > maxFiles) fail('too many generated files');
    } else fail('unsupported generated file');
  }
}
function collect() {
  const binding = JSON.parse(regular(outputPath + '/binding.json').toString('utf8'));
  if (binding.base !== base || binding.head !== command('git', ['rev-parse', 'HEAD']) ||
      binding.packetGitBlob !== packetSha || binding.patchGitBlob !== patchSha)
    fail('preflight binding changed');
  const packetBytes = regular(packetPath);
  if (gitBlob(packetBytes) !== packetSha) fail('packet changed after build');
  const packet = JSON.parse(packetBytes.toString('utf8'));
  const paths = packet.files.map(file => safePath(file.path));
  verifyPostimages(packet, paths);
  if (gitBlob(regular('Legacy.Maliev.Web/package-lock.json')) !== lockSha)
    fail('package lock changed during build');
  const generated = [];
  walk('Legacy.Maliev.Web/wwwroot/dist', generated);
  walk('Legacy.Maliev.Web/wwwroot/src/vendor/three', generated);
  const required = [
    ['Legacy.Maliev.Web/wwwroot/dist/site.min.css', '2a5e800062834c8874fa1622df740126de21a35b'],
    ['Legacy.Maliev.Web/wwwroot/dist/asset-manifest.json', 'd4c20e901d37dde0dca69e918724f61bd0d03638'],
    ['Legacy.Maliev.Web/wwwroot/dist/route-knowledge.js', null]
  ];
  for (const [path, oldBlob] of required) {
    if (!generated.includes(path)) fail('required generated file absent: ' + path);
    const current = gitBlob(regular(path));
    if (oldBlob !== null && current === oldBlob) fail('required generated postimage did not change: ' + path);
    if (oldBlob === null && command('git', ['ls-files', '--', path])) fail('required new output was already tracked');
  }
  if (!generated.length) fail('no generated assets');
  const changed = statusPaths().filter(path => !path.startsWith(outputPath + '/'));
  if (changed.some(path => !paths.includes(path) &&
      !path.startsWith('Legacy.Maliev.Web/wwwroot/dist/') &&
      !path.startsWith('Legacy.Maliev.Web/wwwroot/src/vendor/three/')))
    fail('build changed path outside reviewed sources and generated roots');
  if (paths.some(path => !changed.includes(path))) fail('source patch status missing');
  const destination = absolute(outputPath);
  let totalBytes = 0;
  const files = generated.map(path => {
    const bytes = regular(path);
    totalBytes += bytes.length;
    if (totalBytes > maxBytes) fail('generated output too large');
    const output = join(destination, ...path.split('/'));
    mkdirSync(resolve(output, '..'), { recursive: true });
    copyFileSync(absolute(path), output);
    return { path, bytes: bytes.length, sha256: sha256(bytes), gitBlob: gitBlob(bytes) };
  });
  const manifest = {
    ...binding, generated: files, generatedFileCount: files.length,
    generatedTotalBytes: totalBytes, changedPaths: changed.sort(),
    buildAssetsGitBlob: gitBlob(regular('Legacy.Maliev.Web/build-assets.mjs')),
    runId: process.env.GITHUB_RUN_ID || null,
    runAttempt: process.env.GITHUB_RUN_ATTEMPT || null,
    acceptance: 'UNVALIDATED REVIEW ARTIFACT; NO COMMIT OR MERGE'
  };
  writeFileSync(join(destination, 'manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
}
if (mode === 'prepare') prepare();
else if (mode === 'collect') collect();
else fail('usage: node script prepare|collect');
