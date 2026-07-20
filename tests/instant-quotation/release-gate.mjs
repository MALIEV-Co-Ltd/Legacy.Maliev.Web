import assert from 'node:assert/strict';

const SHA_PATTERN = /^[0-9a-f]{40}$/i;
const ANALYTICS_HOSTS = new Set([
  'www.googletagmanager.com',
  'www.google-analytics.com',
  'region1.google-analytics.com',
  'googleads.g.doubleclick.net',
  'www.googleadservices.com',
]);
const REDACTED_FIELDS = new Set([
  'authorization',
  'cookie',
  'set-cookie',
  'email',
  'telephone',
  'phone',
  'filename',
  'firstname',
  'lastname',
  'company',
  'taxnumber',
  'description',
  'access_token',
  'refresh_token',
  'session',
  'sessionid',
]);
const FORBIDDEN_ANALYTICS_FIELDS = new Set([
  'email',
  'telephone',
  'phone',
  'filename',
  'firstname',
  'lastname',
  'company',
  'taxnumber',
  'description',
  'access_token',
  'refresh_token',
  'session',
  'sessionid',
]);

export function assertExactBuildSha(expectedSha, servedSha) {
  if (!SHA_PATTERN.test(expectedSha) || !SHA_PATTERN.test(servedSha)) {
    throw new Error('Expected or served build SHA is missing or invalid.');
  }

  if (expectedSha.toLowerCase() !== servedSha.toLowerCase()) {
    throw new Error(`Served build SHA ${servedSha} does not match expected SHA ${expectedSha}.`);
  }
}

export function assertBuildIdentityContract(expectedSha, identity, responseHeaders) {
  const hasIdentityValue = (value) => (
    typeof value === 'string'
    && value.trim().length > 0
    && value.trim().toLowerCase() !== 'unknown'
  );

  if (
    !identity
    || !responseHeaders
    || !hasIdentityValue(identity.repository)
    || !hasIdentityValue(identity.branch)
  ) {
    throw new Error('Served build identity is missing or invalid.');
  }

  const headers = Object.fromEntries(
    Object.entries(responseHeaders).map(([key, value]) => [key.toLowerCase(), value]),
  );

  if (
    !hasIdentityValue(headers['x-maliev-build-repository'])
    || !hasIdentityValue(headers['x-maliev-build-branch'])
  ) {
    throw new Error('Served build identity is missing or invalid.');
  }

  assertExactBuildSha(expectedSha, identity.commit);
  assertExactBuildSha(expectedSha, headers['x-maliev-build-commit']);
  assert.equal(headers['x-maliev-build-repository'], identity.repository);
  assert.equal(headers['x-maliev-build-branch'], identity.branch);
  assert.match(headers['cache-control'] ?? '', /(?:^|,)\s*no-store\s*(?:,|$)/i, 'Build identity must be no-store.');
}

export function classifyRequest(requestUrl, applicationBaseUrl) {
  const request = new URL(requestUrl);
  const application = new URL(applicationBaseUrl);
  if (request.origin === application.origin) {
    return 'local';
  }

  if (request.protocol === 'https:' && ANALYTICS_HOSTS.has(request.hostname)) {
    return 'analytics-intercept';
  }

  return 'deny';
}

export function assertAnalyticsContract(observation) {
  if (
    !observation
    || !Array.isArray(observation.eventsBeforeConsent)
    || !Array.isArray(observation.eventsAfterConsent)
  ) {
    throw new Error('Analytics consent observation is missing or invalid.');
  }

  assert.equal(
    observation.eventsBeforeConsent.length,
    0,
    'Analytics events must not be emitted before consent.',
  );

  const events = observation.eventsAfterConsent;
  for (const event of events) {
    for (const field of Object.keys(event)) {
      if (FORBIDDEN_ANALYTICS_FIELDS.has(field.toLowerCase())) {
        throw new Error(`Analytics event ${event.event ?? 'unknown'} contains forbidden analytics field ${field}.`);
      }
    }
  }

  const conversions = events.filter((event) => event.event === 'request_quote');
  assert.equal(conversions.length, 1, 'Persisted request_quote must be emitted exactly once.');
  assert.equal(conversions[0].submission_status, 'persisted');
  assert.match(conversions[0].transaction_id ?? '', /^[a-z0-9][a-z0-9._-]+$/i);
}

export function assertAnalyticsPayloadContract(analytics, payload, options = {}) {
  if (!analytics || !payload || typeof payload.event !== 'string') {
    throw new Error('Analytics payload contract input is missing or invalid.');
  }

  if ((analytics.forbiddenEventNames ?? []).includes(payload.event)) {
    throw new Error(`Analytics event ${payload.event} is a forbidden event name.`);
  }

  const stable = analytics.stableEventContracts ?? [];
  const pending = analytics.pendingEventContracts ?? [];
  const contract = [...stable, ...pending].find((item) => item.name === payload.event);
  if (!contract) {
    throw new Error(`Analytics event ${payload.event} has no approved payload contract.`);
  }

  if (contract.status?.startsWith('inactive') && options.allowInactive !== true) {
    throw new Error(`Analytics event ${payload.event} is inactive pending implementation review.`);
  }

  const expectedFields = [...contract.exactFields].sort();
  const actualFields = Object.keys(payload).sort();
  try {
    assert.deepEqual(actualFields, expectedFields);
  } catch {
    throw new Error(
      `Analytics event ${payload.event} fields do not match the exact allowlist. `
      + `Expected ${expectedFields.join(', ')}; received ${actualFields.join(', ')}.`,
    );
  }

  for (const [field, expected] of Object.entries(contract.constants ?? {})) {
    assert.equal(payload[field], expected, `Analytics event ${payload.event} requires ${field}=${expected}.`);
  }

  for (const field of contract.positiveIntegerFields ?? []) {
    if (!Number.isInteger(payload[field]) || payload[field] <= 0) {
      throw new Error(`Analytics event ${payload.event} requires positive integer ${field}.`);
    }
  }

  if (contract.fileCount !== undefined && payload.file_count !== contract.fileCount) {
    throw new Error(`Analytics event ${payload.event} requires file_count=${contract.fileCount}.`);
  }

  if (
    contract.failureCategories
    && !contract.failureCategories.includes(payload.failure_category)
  ) {
    throw new Error(`Analytics event ${payload.event} contains unapproved failure_category.`);
  }
}

export function sanitizeEvidence(value) {
  if (Array.isArray(value)) {
    return value.map(sanitizeEvidence);
  }

  if (!value || typeof value !== 'object') {
    return value;
  }

  return Object.fromEntries(
    Object.entries(value).map(([key, child]) => [
      key,
      REDACTED_FIELDS.has(key.toLowerCase()) ? '[REDACTED]' : sanitizeEvidence(child),
    ]),
  );
}
