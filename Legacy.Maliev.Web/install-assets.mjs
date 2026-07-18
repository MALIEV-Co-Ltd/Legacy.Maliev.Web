import { spawnSync } from 'node:child_process';

const npmCli = process.env.npm_execpath;
if (!npmCli) {
  throw new Error('Run this installer through `npm run ci` so npm can be located safely.');
}

const result = spawnSync(
  process.execPath,
  [npmCli, 'ci', `--os=${process.platform}`, '--ignore-scripts', '--no-audit', '--no-fund'],
  { stdio: 'inherit' });

if (result.error) {
  throw result.error;
}

process.exitCode = result.status ?? 1;
