import { build } from 'esbuild';
import { mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { patchThreeMfLoader } from './three-mf-loader-patch.mjs';

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

const threeMfLoaderPatchPlugin = {
  name: 'maliev-three-mf-external-components',
  setup(assetBuild) {
    assetBuild.onLoad({ filter: /[\\/]3MFLoader\.js$/ }, async args => ({
      contents: patchThreeMfLoader(await readFile(args.path, 'utf8')),
      loader: 'js',
    }));
  },
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
const instantQuotationWorkflow = path.join(dist, 'instant-quotation-workflow.mjs');

await build({
  ...common,
  entryPoints: [path.join(root, 'wwwroot', 'src', 'app', 'js', 'instant-quotation', 'model-viewer.mjs')],
  outfile: instantQuotationViewer,
  platform: 'browser',
  format: 'esm',
  plugins: [threeMfLoaderPatchPlugin],
});

const viewerSource = await readFile(instantQuotationViewer, 'utf8');
await writeFile(
  instantQuotationViewer,
  viewerSource.replace(/[\t ]+$/gm, '').replace(/^ +\t/gm, '\t'));

await build({
  ...common,
  entryPoints: [path.join(root, 'wwwroot', 'src', 'app', 'js', 'instant-quotation', 'workflow-interop.mjs')],
  outfile: instantQuotationWorkflow,
  platform: 'browser',
  format: 'esm',
  external: ['/dist/instant-quotation-viewer.mjs'],
});

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
      instantQuotationWorkflow: 'instant-quotation-workflow.mjs',
    },
    styles: ['site.min.css'],
  }, null, 2)}\n`);
