import { build } from 'esbuild';
import { mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.dirname(fileURLToPath(import.meta.url));
const dist = path.join(root, 'wwwroot', 'dist');
const assets = path.join(root, 'assets');

await rm(dist, { recursive: true, force: true });
await mkdir(dist, { recursive: true });

const common = {
  bundle: true,
  legalComments: 'none',
  minify: true,
  sourcemap: false,
  target: ['es2022'],
};

await build({
  ...common,
  entryPoints: [path.join(assets, 'vendor-entry.js')],
  outfile: path.join(dist, 'vendor.min.js'),
  platform: 'browser',
});

await build({
  ...common,
  entryPoints: [path.join(assets, 'app-entry.js')],
  outfile: path.join(dist, 'app.min.js'),
  platform: 'browser',
});

const instantQuotationViewer = path.join(dist, 'instant-quotation-viewer.mjs');

await build({
  ...common,
  entryPoints: [path.join(root, 'wwwroot', 'src', 'app', 'js', 'instant-quotation', 'model-viewer.mjs')],
  outfile: instantQuotationViewer,
  platform: 'browser',
  format: 'esm',
});

const viewerSource = await readFile(instantQuotationViewer, 'utf8');
await writeFile(
  instantQuotationViewer,
  viewerSource.replace(/[\t ]+$/gm, '').replace(/^ +\t/gm, '\t'));

await build({
  ...common,
  entryPoints: [path.join(assets, 'site-entry.css')],
  external: ['/src/images/*'],
  entryNames: 'site.min',
  assetNames: 'assets/[name]-[hash]',
  loader: {
    '.eot': 'file',
    '.svg': 'file',
    '.ttf': 'file',
    '.woff': 'file',
    '.woff2': 'file',
  },
  outdir: dist,
});

await writeFile(
  path.join(dist, 'asset-manifest.json'),
  `${JSON.stringify({
    scripts: ['vendor.min.js', 'app.min.js'],
    routeScopedModules: {
      instantQuotationViewer: 'instant-quotation-viewer.mjs',
    },
    styles: ['site.min.css'],
  }, null, 2)}\n`);
