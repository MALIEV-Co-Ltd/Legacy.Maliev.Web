import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

import { assertWorkflowObservation } from './workflow-contract.mjs';

const manifest = JSON.parse(
  await readFile(new URL('./production-parity-manifest.json', import.meta.url), 'utf8'),
);
const checkpoint = manifest.reviewedImplementationCheckpoint;

test('approved workflow states expose exactly their reviewed markers', () => {
  for (const [enumState, markers] of Object.entries(checkpoint.stateSections)) {
    const state = enumState.toLowerCase();
    assert.doesNotThrow(() => assertWorkflowObservation(checkpoint, {
      state,
      markers,
      alertCount: enumState === 'Error' ? 1 : 0,
    }));
  }
});

test('workflow observation fails when a critical marker or alert disappears', () => {
  assert.throws(
    () => assertWorkflowObservation(checkpoint, {
      state: 'review',
      markers: checkpoint.stateSections.Review.filter((marker) => marker !== 'data-workflow-review'),
      alertCount: 0,
    }),
    /markers do not match/,
  );
  assert.throws(
    () => assertWorkflowObservation(checkpoint, {
      state: 'error',
      markers: checkpoint.stateSections.Error,
      alertCount: 0,
    }),
    /exactly one alert/,
  );
});

test('workflow observation rejects guessed or differently-cased state values', () => {
  assert.throws(
    () => assertWorkflowObservation(checkpoint, {
      state: 'customer-details',
      markers: checkpoint.stateSections.CustomerDetails,
      alertCount: 0,
    }),
    /not an approved lower-case enum value/,
  );
  assert.throws(
    () => assertWorkflowObservation(checkpoint, {
      state: 'MultiPart',
      markers: checkpoint.stateSections.MultiPart,
      alertCount: 0,
    }),
    /not an approved lower-case enum value/,
  );
});
