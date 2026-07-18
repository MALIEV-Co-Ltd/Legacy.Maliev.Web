import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

import {
  assertAnalyticsContract,
  assertAnalyticsPayloadContract,
  assertBuildIdentityContract,
  assertExactBuildSha,
  classifyRequest,
  sanitizeEvidence,
} from './release-gate.mjs';

const parityManifest = JSON.parse(
  await readFile(new URL('./production-parity-manifest.json', import.meta.url), 'utf8'),
);

test('build identity requires the exact endpoint and matching safe response headers', () => {
  const expected = 'b7174be9e3f9dbcce35d61c50248cff23e110196';
  const identity = {
    repository: 'https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Web.git',
    branch: 'codex/instant-quotation-parity-validation',
    commit: expected,
  };
  const headers = {
    'cache-control': 'no-store',
    'x-maliev-build-repository': identity.repository,
    'x-maliev-build-branch': identity.branch,
    'x-maliev-build-commit': expected,
  };

  assert.doesNotThrow(() => assertBuildIdentityContract(expected, identity, headers));
  assert.throws(
    () => assertBuildIdentityContract(expected, { ...identity, commit: 'unknown' }, headers),
    /missing or invalid/,
  );
  assert.throws(
    () => assertBuildIdentityContract(expected, { commit: expected }, {
      'cache-control': 'no-store',
      'x-maliev-build-commit': expected,
    }),
    /missing or invalid/,
  );
  assert.throws(
    () => assertBuildIdentityContract(expected, identity, { ...headers, 'x-maliev-build-commit': 'unknown' }),
    /missing or invalid/,
  );
  assert.throws(
    () => assertBuildIdentityContract(expected, identity, { ...headers, 'cache-control': 'public' }),
    /no-store/,
  );
});

test('exact build identity fails closed when the served SHA differs', () => {
  const expected = '6e00796d263c45be73080fa292929a99dbb9af1d';

  assert.doesNotThrow(() => assertExactBuildSha(expected, expected));
  assert.throws(
    () => assertExactBuildSha(expected, 'bea5ab237ceda0ad1add362c434c90e40821d70e'),
    /does not match expected SHA/,
  );
  assert.throws(() => assertExactBuildSha(expected, ''), /missing or invalid/);
});

test('request policy denies production and permits only local or intercepted analytics traffic', () => {
  assert.equal(classifyRequest('http://127.0.0.1:5088/assets/app.js', 'http://127.0.0.1:5088'), 'local');
  assert.equal(classifyRequest('https://www.googletagmanager.com/gtm.js?id=GTM-test', 'http://127.0.0.1:5088'), 'analytics-intercept');
  assert.equal(classifyRequest('https://www.google-analytics.com/g/collect', 'http://127.0.0.1:5088'), 'analytics-intercept');
  assert.equal(classifyRequest('https://www.maliev.com/instantquotation/3d-printing', 'http://127.0.0.1:5088'), 'deny');
  assert.equal(classifyRequest('https://storage.googleapis.com/private-model.stl', 'http://127.0.0.1:5088'), 'deny');
});

test('analytics contract uses external consent timing, one persisted conversion, and no PII', () => {
  const observation = {
    eventsBeforeConsent: [],
    eventsAfterConsent: [
      { event: 'file_upload_start', service: '3d_printing', file_count: 1 },
      { event: 'file_upload_complete', service: '3d_printing', transaction_id: 'quotation-local-153' },
      {
        event: 'request_quote',
        intent_type: 'quotation_request',
        service: '3d_printing',
        transaction_id: 'quotation-local-153',
        submission_status: 'persisted',
        has_files: true,
        file_upload_completed: true,
      },
    ],
  };

  assert.doesNotThrow(() => assertAnalyticsContract(observation));
  assert.throws(
    () => assertAnalyticsContract({
      ...observation,
      eventsBeforeConsent: [{ event: 'file_upload_start' }],
    }),
    /before consent/,
  );
  assert.throws(
    () => assertAnalyticsContract({
      ...observation,
      eventsAfterConsent: [...observation.eventsAfterConsent, observation.eventsAfterConsent[2]],
    }),
    /exactly once/,
  );
  assert.throws(
    () => assertAnalyticsContract({
      eventsBeforeConsent: [],
      eventsAfterConsent: [{ ...observation.eventsAfterConsent[2], email: 'customer@example.com' }],
    }),
    /forbidden analytics field/,
  );
});

test('analytics payload allowlists reject broader fields and keep pending events inactive', () => {
  const analytics = parityManifest.analytics;
  const persistedQuote = {
    event: 'request_quote',
    intent_type: 'quotation_request',
    service: '3d_printing',
    transaction_id: 'quotation-153',
    submission_status: 'persisted',
    has_files: true,
    file_upload_completed: true,
  };

  assert.doesNotThrow(() => assertAnalyticsPayloadContract(analytics, {
    event: 'file_upload_start',
    service: '3d_printing',
    file_count: 1,
  }));
  assert.doesNotThrow(() => assertAnalyticsPayloadContract(analytics, {
    event: 'file_upload_complete',
    service: '3d_printing',
    transaction_id: 'quotation-153',
  }));
  assert.doesNotThrow(() => assertAnalyticsPayloadContract(analytics, persistedQuote));
  assert.throws(
    () => assertAnalyticsPayloadContract(analytics, { ...persistedQuote, locale: 'th' }),
    /fields do not match/,
  );
  assert.throws(
    () => assertAnalyticsPayloadContract(analytics, {
      event: 'upload_failure',
      service: '3d_printing',
      failure_category: 'validation',
      file_count: 1,
    }),
    /inactive/,
  );
  assert.doesNotThrow(() => assertAnalyticsPayloadContract(analytics, {
    event: 'upload_failure',
    service: '3d_printing',
    failure_category: 'validation',
    file_count: 1,
  }, { allowInactive: true }));
  assert.doesNotThrow(() => assertAnalyticsPayloadContract(analytics, {
    event: 'estimate_shown',
    service: '3d_printing',
  }, { allowInactive: true }));
  assert.doesNotThrow(() => assertAnalyticsPayloadContract(analytics, {
    event: 'review_reached',
    service: '3d_printing',
  }, { allowInactive: true }));
  assert.throws(
    () => assertAnalyticsPayloadContract(analytics, {
      event: 'upload_failure',
      service: '3d_printing',
      failure_category: 'raw-provider-error',
      file_count: 1,
    }, { allowInactive: true }),
    /failure_category/,
  );
  assert.throws(
    () => assertAnalyticsPayloadContract(analytics, {
      event: 'file_upload_failure',
      service: '3d_printing',
      file_count: 1,
    }, { allowInactive: true }),
    /forbidden event name/,
  );
});

test('evidence sanitizer removes secrets and customer data recursively', () => {
  const sanitized = sanitizeEvidence({
    authorization: 'Bearer secret',
    cookie: 'session=secret',
    email: 'customer@example.com',
    filename: 'customer-part.step',
    safe: 'request-local-153',
    nested: { telephone: '+66000000000', status: 201 },
  });

  assert.deepEqual(sanitized, {
    authorization: '[REDACTED]',
    cookie: '[REDACTED]',
    email: '[REDACTED]',
    filename: '[REDACTED]',
    safe: 'request-local-153',
    nested: { telephone: '[REDACTED]', status: 201 },
  });
});
