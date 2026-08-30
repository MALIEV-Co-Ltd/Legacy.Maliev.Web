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

const routeScripts = {
  'route-inquiry': path.join(assets, 'route-inquiry.js'),
  'route-instant-quotation': path.join(assets, 'route-instant-quotation.js'),
  'route-member-order': path.join(assets, 'route-member-order.js'),
  'route-service-finder': path.join(assets, 'route-service-finder.js'),
  'route-service-cnc': path.join(assets, 'route-service-cnc.js'),
  'route-service-finishing': path.join(assets, 'route-service-finishing.js'),
  'route-service-printing': path.join(assets, 'route-service-printing.js'),
  'route-service-scanning': path.join(assets, 'route-service-scanning.js'),
  'route-service-toc': path.join(assets, 'route-service-toc.js'),
};

await build({
  ...common,
  entryPoints: routeScripts,
  outdir: dist,
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

const routeStyles = {
  'route-about': path.join(assets, 'route-about.css'),
  'route-home': path.join(assets, 'route-home.css'),
  'route-inquiry': path.join(assets, 'route-inquiry.css'),
  'route-instant-quotation': path.join(assets, 'route-instant-quotation.css'),
  'route-services': path.join(assets, 'route-services.css'),
  'route-services-index': path.join(assets, 'route-services-index.css'),
};

await build({
  ...common,
  entryPoints: routeStyles,
  external: ['/src/images/*'],
  outdir: dist,
});

await writeFile(
  path.join(dist, 'asset-manifest.json'),
  `${JSON.stringify({
    scripts: ['vendor.min.js', 'app.min.js'],
    routeScripts: Object.keys(routeScripts).map(name => `${name}.js`),
    routeScopedModules: {
      instantQuotationViewer: 'instant-quotation-viewer.mjs',
      instantQuotationWorkflow: 'instant-quotation-workflow.mjs',
    },
    styles: ['site.min.css'],
    routeStyles: Object.keys(routeStyles).map(name => `${name}.css`),
  }, null, 2)}\n`);
