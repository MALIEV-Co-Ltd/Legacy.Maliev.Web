import assert from 'node:assert/strict';

function sortedUnique(values) {
  return [...new Set(values)].sort((left, right) => left.localeCompare(right));
}

export function assertWorkflowObservation(checkpoint, observation) {
  if (!checkpoint || !observation || !Array.isArray(observation.markers)) {
    throw new Error('Workflow checkpoint or observation is missing or invalid.');
  }

  const stateMap = new Map(checkpoint.states.map((enumState) => [enumState.toLowerCase(), enumState]));
  const enumState = stateMap.get(observation.state);
  if (!enumState) {
    throw new Error(`Workflow state ${observation.state ?? '<missing>'} is not an approved lower-case enum value.`);
  }

  const expectedMarkers = sortedUnique(checkpoint.stateSections[enumState] ?? []);
  const observedMarkers = sortedUnique(observation.markers);
  try {
    assert.deepEqual(observedMarkers, expectedMarkers);
  } catch {
    throw new Error(
      `Workflow state ${observation.state} markers do not match the approved contract. `
      + `Expected ${expectedMarkers.join(', ') || '<none>'}; observed ${observedMarkers.join(', ') || '<none>'}.`,
    );
  }

  if (enumState === 'Error' && observation.alertCount !== 1) {
    throw new Error('Workflow Error state must expose exactly one alert.');
  }

  if (enumState !== 'Error' && observation.alertCount !== 0) {
    throw new Error(`Workflow ${enumState} state must not expose an error alert.`);
  }
}
