import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

import { patchThreeMfLoader } from '../three-mf-loader-patch.mjs';

test('patches the pinned ThreeMF loader to resolve components across model parts and fail closed', async () => {
  const source = await readFile(
    new URL('../node_modules/three/examples/jsm/loaders/3MFLoader.js', import.meta.url),
    'utf8');

  const patched = patchThreeMfLoader(source);

  assert.match(patched, /function buildComposite\( compositeData, objects, modelData, textureData, objectData, modelsData \)/);
  assert.match(patched, /if \( objectData === undefined && modelsData \)/);
  assert.match(patched, /3MF component references an unknown object/);
  assert.match(patched, /3MF object reference could not be resolved/);
  assert.match(patched, /buildObject\( objectId, objects, modelData, textureData, modelsData \)/);
});

test('rejects an unrecognized upstream loader instead of producing a partially patched bundle', () => {
  assert.throws(
    () => patchThreeMfLoader('export class ThreeMFLoader {}'),
    /pinned ThreeMF loader shape changed/);
});
