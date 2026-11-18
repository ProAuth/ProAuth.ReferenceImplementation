import fs from 'node:fs';
import path from 'node:path';
import https from 'node:https';
import tls from 'node:tls';
import { setTimeout as delay } from 'node:timers/promises';
import { readEnvironment } from './configuration.mjs';

const i = process.argv.indexOf('--env-file');
const file = path.resolve(i < 0 ? '.local/local.env' : process.argv[i + 1]);
const env = readEnvironment(file);
const ca = [
  ...tls.rootCertificates,
  ...(env.TRUSTED_CA_FILE ? [fs.readFileSync(env.TRUSTED_CA_FILE)] : []),
];
async function get(origin, route, headers = {}) {
  return new Promise((resolve, reject) => {
    const request = https.get(
      new URL(route, origin),
      { ca, headers, timeout: 10000 },
      (response) => {
        response.resume();
        response.on('end', () =>
          resolve({ status: response.statusCode, location: response.headers.location }),
        );
      },
    );
    request.on('timeout', () => request.destroy(new Error('Request timeout')));
    request.on('error', reject);
  });
}
const cases = [
  ['bff-live', env.WORKPLACE_ORIGIN, '/health/live', {}, 200],
  ['api-live', env.PROCUREMENT_ORIGIN, '/health/live', {}, 200],
  ['api-anonymous-denied', env.PROCUREMENT_ORIGIN, '/api/purchases', {}, 401],
  [
    'api-invalid-token-denied',
    env.PROCUREMENT_ORIGIN,
    '/api/purchases',
    { Authorization: 'Bearer invalid-mervikon-smoke-token' },
    401,
  ],
  ['bff-anonymous-api-denied', env.WORKPLACE_ORIGIN, '/api/purchases', {}, 401],
  [
    'bff-login-form-available',
    env.WORKPLACE_ORIGIN,
    '/bff/account/login?returnUrl=https%3A%2F%2Funrelated.example',
    {},
    200,
  ],
];
// Compose start does not imply that the reverse proxy has discovered the new container.
// Wait only for health; never retry an authorization assertion into a passing result.
const readinessStarted = Date.now();
const readinessDeadline = readinessStarted + 30000;
while (Date.now() < readinessDeadline) {
  const health = await Promise.allSettled([
    get(env.WORKPLACE_ORIGIN, '/health/live'),
    get(env.PROCUREMENT_ORIGIN, '/health/live'),
  ]);
  if (health.every((r) => r.status === 'fulfilled' && r.value.status === 200)) break;
  await delay(1000);
}
const startupWaitMs = Date.now() - readinessStarted;
const results = [];
for (const [id, origin, route, headers, expected] of cases) {
  try {
    const result = await get(origin, route, headers);
    const passed =
      result.status === expected && !result.location?.startsWith('https://unrelated.example');
    results.push({ id, expectedStatus: expected, observedStatus: result.status, passed });
    console.log(`${passed ? 'PASS' : 'FAIL'} ${id}: HTTP ${result.status}`);
  } catch {
    results.push({ id, passed: false, reason: 'transport_error' });
    console.log(`FAIL ${id}: transport error`);
  }
}
const evidence = path.join(path.dirname(file), 'evidence/smoke.json');
fs.mkdirSync(path.dirname(evidence), { recursive: true, mode: 0o700 });
fs.writeFileSync(
  evidence,
  JSON.stringify(
    {
      observedAt: new Date().toISOString(),
      startupWaitMs,
      tlsValidation: 'system trust plus configured CA',
      scope:
        'Anonymous transport/access smoke only; not an authenticated protocol or open-redirect conformance suite.',
      results,
    },
    null,
    2,
  ),
  { mode: 0o600 },
);
if (results.some((r) => !r.passed)) process.exitCode = 1;
