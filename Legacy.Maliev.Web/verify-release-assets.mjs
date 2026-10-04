import { lstat, readFile, realpath } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { brotliDecompressSync } from 'node:zlib';

const project = path.dirname(fileURLToPath(import.meta.url));
const requiredFields = ['scripts', 'styles', 'routeScripts', 'routeStyles', 'routeScopedModules'];
const limits = { manifest: 1024 * 1024, css: 4 * 1024 * 1024, asset: 64 * 1024 * 1024 };
const publicFontPaths = new Set(['/lib/outfit/outfit-latin-400-normal.woff2', '/lib/ibm-plex/NotoSansThai-Regular.woff2']);

export class ReleaseAssetError extends Error {
  constructor(code) {
    super(code);
    this.code = code;
  }
}

function requireCondition(condition, code) {
  if (!condition) throw new ReleaseAssetError(code);
}

function contained(root, file) {
  const relative = path.relative(root, file);
  return relative !== '' && !relative.startsWith(`..${path.sep}`) && relative !== '..' && !path.isAbsolute(relative);
}

function requireRelativePath(relative) {
  requireCondition(typeof relative === 'string' && relative.length > 0
    && !relative.includes('\\') && !path.isAbsolute(relative) && !path.win32.isAbsolute(relative)
    && !/^[a-z][a-z0-9+.-]*:/i.test(relative), 'invalid-asset-path');
}

async function assetPath(root, relative) {
  requireRelativePath(relative);
  const file = path.resolve(root, relative);
  requireCondition(contained(root, file), 'asset-outside-release');
  let current = root;
  for (const segment of path.relative(root, file).split(path.sep)) {
    current = path.join(current, segment);
    const info = await lstat(current);
    requireCondition(!info.isSymbolicLink(), 'symlink-asset');
  }
  requireCondition(contained(root, await realpath(file)), 'asset-outside-release');
  const info = await lstat(file);
  requireCondition(info.isFile() && info.size > 0, 'missing-or-empty-asset');
  requireCondition(info.size <= limits.asset, 'asset-size-limit');
  return { file, size: info.size };
}

async function boundedText(root, relative, maximum) {
  const asset = await assetPath(root, relative);
  requireCondition(asset.size <= maximum, 'text-size-limit');
  return new TextDecoder('utf-8', { fatal: true }).decode(await readFile(asset.file));
}

// Bounded structural validation of the standalone WOFF2 fonts shipped by this release.
// This does not reconstruct sfnt tables or replace browser font/glyph acceptance.
const tableTags = 'cmap head hhea hmtx maxp name OS/2 post cvt_ fpgm glyf loca prep CFF_ VORG EBDT EBLC gasp hdmx kern LTSH PCLT VDMX vhea vmtx BASE GDEF GPOS GSUB EBSC JSTF MATH CBDT CBLC COLR CPAL SVG_ sbix acnt avar bdat bloc bsln cvar fdsc feat fmtx fvar gvar hsty just lcar mort morx opbd prop trak Zapf Silf Glat Gloc Feat Sill'.split(' ').map(tag => tag.replace('_', ' '));

function validateWoff2(data) {
  const valid = (condition, code = 'invalid-woff2-directory') => requireCondition(condition, code);
  valid(data.length >= 48 && data.toString('ascii', 0, 4) === 'wOF2'
    && data.readUInt32BE(8) === data.length, 'invalid-woff2-header');
  const flavor = data.readUInt32BE(4);
  valid(flavor === 0x00010000 || flavor === 0x4f54544f, 'invalid-woff2-header');
  const count = data.readUInt16BE(12);
  const compressedSize = data.readUInt32BE(20);
  valid(count > 0 && count <= 4096 && compressedSize > 0, 'invalid-woff2-header');
  // totalSfntSize is reference-only. Reserved is not a rejection criterion (WOFF2 3.2).
  let cursor = 48;
  const byte = () => { valid(cursor < data.length); return data[cursor++]; };
  const base128 = () => {
    let value = 0;
    for (let index = 0; index < 5; index++) {
      const next = byte();
      valid(!(index === 0 && next === 0x80) && value <= 0x01ffffff);
      value = value * 128 + (next & 0x7f);
      if (!(next & 0x80)) return value;
    }
    valid(false);
  };
  const tags = new Set();
  let decodedSize = 0;
  let needsLoca = false;
  for (let index = 0; index < count; index++) {
    const flags = byte();
    let tag = tableTags[flags & 63];
    if ((flags & 63) === 63) {
      valid(cursor + 4 <= data.length);
      tag = data.toString('ascii', cursor, cursor + 4);
      cursor += 4;
    }
    valid(!tags.has(tag));
    tags.add(tag);
    const version = flags >>> 6;
    const glyphTable = tag === 'glyf' || tag === 'loca';
    valid(glyphTable ? version === 0 || version === 3 : version === 0 || (tag === 'hmtx' && version === 1));
    const originalSize = base128();
    const transformed = glyphTable ? version === 0 : version !== 0;
    const tableSize = transformed ? base128() : originalSize;
    valid(!needsLoca || (tag === 'loca' && transformed && tableSize === 0));
    valid(!(tag === 'loca' && transformed) || (needsLoca && tableSize === 0));
    needsLoca = tag === 'glyf' && transformed;
    decodedSize += tableSize;
    valid(decodedSize <= limits.asset, 'woff2-decompressed-size-limit');
  }
  valid(!needsLoca && decodedSize > 0);
  const compressedEnd = cursor + compressedSize;
  valid(compressedEnd <= data.length, 'invalid-woff2-block');
  let blockEnd = compressedEnd;
  const nextBlock = (offset, length) => {
    valid(offset >= blockEnd && offset % 4 === 0 && length > 0
      && offset + length <= data.length && offset - blockEnd <= 3, 'invalid-woff2-block');
    valid(data.subarray(blockEnd, offset).every(value => value === 0), 'invalid-woff2-block');
    blockEnd = offset + length;
  };
  const metaOffset = data.readUInt32BE(28);
  const metaLength = data.readUInt32BE(32);
  const metaOriginal = data.readUInt32BE(36);
  if (metaOffset) {
    valid(metaOriginal > 0 && metaOriginal <= limits.asset, 'invalid-woff2-block');
    nextBlock(metaOffset, metaLength);
  } else valid(metaLength === 0 && metaOriginal === 0, 'invalid-woff2-block');
  const privateOffset = data.readUInt32BE(40);
  const privateLength = data.readUInt32BE(44);
  if (privateOffset) nextBlock(privateOffset, privateLength);
  else valid(privateLength === 0, 'invalid-woff2-block');
  valid(data.length - blockEnd <= 3 && data.subarray(blockEnd).every(value => value === 0), 'invalid-woff2-block');
  try {
    const result = brotliDecompressSync(data.subarray(cursor, compressedEnd), { maxOutputLength: decodedSize, info: true });
    valid(result.buffer.length === decodedSize && result.engine.bytesWritten === compressedSize, 'invalid-woff2-stream');
  } catch {
    throw new ReleaseAssetError('invalid-woff2-stream');
  }
}

export async function verifyReleaseAssets(directory = path.join(project, 'wwwroot', 'dist'), publicDirectory = path.dirname(directory)) {
  try {
    const root = await realpath(directory);
    const manifest = JSON.parse(await boundedText(root, 'asset-manifest.json', limits.manifest));
    requireCondition(manifest && typeof manifest === 'object' && !Array.isArray(manifest)
      && Object.keys(manifest).length === requiredFields.length
      && requiredFields.every(key => Object.hasOwn(manifest, key)), 'invalid-manifest');
    for (const key of ['scripts', 'styles', 'routeScripts', 'routeStyles']) {
      requireCondition(Array.isArray(manifest[key]) && manifest[key].every(value => typeof value === 'string'), 'invalid-manifest');
    }
    requireCondition(JSON.stringify(manifest.scripts) === JSON.stringify(['vendor.min.js', 'app.min.js'])
      && JSON.stringify(manifest.styles) === JSON.stringify(['site.min.css']), 'invalid-common-bundle');
    requireCondition(manifest.routeScopedModules && typeof manifest.routeScopedModules === 'object'
      && !Array.isArray(manifest.routeScopedModules)
      && Object.values(manifest.routeScopedModules).every(value => typeof value === 'string'), 'invalid-manifest');
    const scripts = [...manifest.scripts, ...manifest.routeScripts, ...Object.values(manifest.routeScopedModules)];
    const styles = [...manifest.styles, ...manifest.routeStyles];
    const outputs = [...scripts, ...styles];
    requireCondition(outputs.length <= 128, 'output-count-limit');
    requireCondition(new Set(outputs).size === outputs.length, 'duplicate-output');
    const outputFiles = new Set();
    for (const output of outputs) {
      const asset = await assetPath(root, output);
      const identity = await realpath(asset.file);
      requireCondition(!outputFiles.has(identity), 'duplicate-output');
      outputFiles.add(identity);
    }
    requireCondition(scripts.every(value => /\.(?:js|mjs)$/.test(value))
      && styles.every(value => value.endsWith('.css')), 'invalid-output-extension');
    const fonts = new Set();
    let fontReferences = 0;
    for (const style of styles) {
      const css = await boundedText(root, style, limits.css);
      for (const match of css.matchAll(/url\(\s*(?:"([^"]*)"|'([^']*)'|([^\s)]+))\s*\)/gi)) {
        const relative = decodeURIComponent((match[1] ?? match[2] ?? match[3]).split(/[?#]/, 1)[0]);
        if (!/\.woff2$/i.test(relative)) continue;
        requireCondition(++fontReferences <= 512, 'font-reference-limit');
        let font;
        if (publicFontPaths.has(relative)) {
          font = await assetPath(await realpath(publicDirectory), relative.slice(1));
        } else {
          requireRelativePath(relative);
          font = await assetPath(root, path.posix.join(path.posix.dirname(style), relative));
        }
        if (fonts.has(font.file)) continue;
        validateWoff2(await readFile(font.file));
        fonts.add(font.file);
      }
    }
    requireCondition([...fonts].some(file => path.basename(file).includes('fa-solid'))
      && [...fonts].some(file => path.basename(file).includes('fa-regular')), 'missing-icon-fonts');
    return { outputs: outputs.length, fonts: fonts.size };
  } catch (error) {
    if (error instanceof ReleaseAssetError) throw error;
    throw new ReleaseAssetError('inspection-incomplete');
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const result = await verifyReleaseAssets();
    console.log(`[release-assets] Validated ${result.outputs} outputs and ${result.fonts} font files`);
  } catch (error) {
    console.error(`[release-assets] FAILED: ${error.code}`);
    process.exitCode = 1;
  }
}
