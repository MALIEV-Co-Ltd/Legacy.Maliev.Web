import assert from 'node:assert/strict';
import test from 'node:test';

import {
  assertAnalyticsContract,
  assertExactBuildSha,
  classifyRequest,
  sanitizeEvidence,
} from './release-gate.mjs';

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

test('analytics contract requires consent, one persisted conversion, and no PII', () => {
  const events = [
    { consent: 'granted', event: 'file_upload_start', file_count: 1 },
    { consent: 'granted', event: 'file_upload_complete', file_count: 1 },
    {
      consent: 'granted',
      event: 'request_quote',
      transaction_id: 'quotation-local-153',
      submission_status: 'persisted',
    },
  ];

  assert.doesNotThrow(() => assertAnalyticsContract(events));
  assert.throws(
    () => assertAnalyticsContract([{ consent: 'denied', event: 'file_upload_start' }]),
    /before consent/,
  );
  assert.throws(
    () => assertAnalyticsContract([...events, events[2]]),
    /exactly once/,
  );
  assert.throws(
    () => assertAnalyticsContract([{ ...events[2], email: 'customer@example.com' }]),
    /forbidden analytics field/,
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
