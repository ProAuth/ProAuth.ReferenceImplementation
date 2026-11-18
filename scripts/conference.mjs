import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import https from 'node:https';
import tls from 'node:tls';
import { spawnSync } from 'node:child_process';
import { readEnvironment } from './configuration.mjs';
const root = path.resolve(import.meta.dirname, '..');
const [command, ...args] = process.argv.slice(2);
const option = (key, fallback) => (args.includes(key) ? args[args.indexOf(key) + 1] : fallback);
const envFile = path.resolve(option('--env-file', path.join(root, '.local/local.env')));
const env = readEnvironment(envFile);
function run(exe, argv, extra = {}) {
  const r = spawnSync(exe, argv, {
    cwd: root,
    env: { ...process.env, ...env, ...extra },
    stdio: 'inherit',
  });
  if (r.status !== 0) throw Error('Conference command failed.');
}
const compose = [
  'compose',
  '--env-file',
  envFile,
  '-f',
  'deploy/compose/compose.yml',
  '-f',
  'deploy/compose/conference.yml',
  '--profile',
  'conference',
  '--profile',
  'demo',
];
const owned = [
  'conference-spa',
  'conference-bff',
  'conference-api',
  'conference-wrong',
  'conference-attacker',
];
const consoleOrigin = 'http://127.0.0.1:' + (env.DEMO_CONSOLE_PORT || 5080);
async function get(url) {
  const ca = [
    ...tls.rootCertificates,
    ...(env.TRUSTED_CA_FILE ? [fs.readFileSync(env.TRUSTED_CA_FILE)] : []),
  ];
  return new Promise((resolve, reject) => {
    const transport = new URL(url).origin === consoleOrigin ? http : https;
    const req = transport.get(url, { ca, timeout: 5000 }, (res) => {
      let body = '';
      res.on('data', (c) => {
        body += c;
        if (body.length > 1_000_000) req.destroy(Error('Response too large'));
      });
      res.on('end', () => resolve({ status: res.statusCode, body }));
    });
    req.on('timeout', () => req.destroy(Error('Timed out')));
    req.on('error', reject);
  });
}
try {
  if (command === 'up') run('docker', [...compose, 'up', '-d', 'demo-console', ...owned]);
  else if (command === 'reset') {
    // Recreate only disposable lab processes. Preserve business records, product identities and Redis.
    run('docker', [...compose, 'up', '-d', '--force-recreate', ...owned]);
    console.log(
      'Lab process state reset. Revoke SPA tokens with its Clear button first. Product SSO and business records are preserved.',
    );
  } else if (command === 'preflight') {
    const checks = [];
    const discovery = await get(
      env.PROAUTH_ISSUER.replace(/\/$/, '') + '/.well-known/openid-configuration',
    );
    const metadata = JSON.parse(discovery.body);
    checks.push({
      check: 'exact issuer and TLS',
      passed: discovery.status === 200 && metadata.issuer === env.PROAUTH_ISSUER,
    });
    const consoleHealth = await get(consoleOrigin + '/health/live');
    checks.push({ check: 'Loopback Demo Console', passed: consoleHealth.status === 200 });
    for (const role of ['SPA', 'BFF', 'API', 'WRONG', 'ATTACKER']) {
      const r = await get(env['CONFERENCE_' + role + '_ORIGIN'] + '/health/live');
      checks.push({ check: role + ' health', passed: r.status === 200 });
    }
    for (const role of ['API', 'WRONG']) {
      const r = await get(env['CONFERENCE_' + role + '_ORIGIN'] + '/api/quote');
      checks.push({ check: role + ' anonymous rejection', passed: r.status === 401 });
    }
    console.log(
      JSON.stringify(
        {
          observedAt: new Date().toISOString(),
          checks,
          manualGates: [
            'Actual presenter passkey login',
            'SPA and BFF legitimate quote',
            'Refresh rotation and family rejection',
            'Fresh sender comparison cleanup',
            'Approved purchase exchange',
            'Offline recording opens without network',
          ],
          infrastructureReady: checks.every((c) => c.passed),
          scope: 'Infrastructure only; manual gates remain required',
        },
        null,
        2,
      ),
    );
    if (checks.some((c) => !c.passed)) process.exitCode = 1;
  } else if (command === 'nonce' || command === 'fapi') {
    const fapi = command === 'fapi';
    const runtime = {
      Variant: command,
      Target: env.CONFERENCE_API_ORIGIN + '/api/quote',
      RedirectUri: env.FINANCE_ORIGIN + '/signin-oidc',
      Evidence__Endpoint: 'http://demo-console-ingest:8081/observations',
      Evidence__PublisherKey: env.EVIDENCE_PUBLISHER_KEY,
      Authentication__Authority: env.PROAUTH_ISSUER,
      Authentication__ClientId: fapi ? env.FINANCE_CLIENT_ID : env.CONFERENCE_NONCE_CLIENT_ID,
      Authentication__ClientSecret: fapi ? '' : env.CONFERENCE_NONCE_SECRET,
      Authentication__TenantId: fapi ? env.WORKFORCE_TENANT_ID : env.CONFERENCE_TENANT_ID,
      Authentication__ClientAuthenticationMethod: fapi ? 'PrivateKeyJwt' : 'ClientSecretBasic',
      Authentication__SenderConstrainedTokenMode: 'Dpop',
      Authentication__Dpop__GenerateEphemeralKeyWhenMissing: 'true',
      Authentication__Dpop__SigningAlgorithm: 'PS256',
      ...(fapi
        ? {
            Authentication__SecurityProfile: 'Fapi2SecurityProfile',
            Authentication__ClientAssertion__Certificate: env.FINANCE_ASSERTION_CERTIFICATE,
            Authentication__ClientAssertion__CertificatePassword: env.FINANCE_ASSERTION_PASSWORD,
            Authentication__ClientAssertion__SigningAlgorithm: 'PS256',
            Authentication__ClientAssertion__KeyId: 'mervikon-finance-assertion-rsa',
          }
        : {}),
    };
    // Two networks: canonical HTTPS endpoints and private evidence ingestion. No host port.
    run(
      'docker',
      [
        'run',
        '--rm',
        '--read-only',
        '--tmpfs',
        '/tmp',
        '--cap-drop',
        'ALL',
        '--security-opt',
        'no-new-privileges:true',
        '--network',
        env.MERVIKON_EDGE_NETWORK,
        '--network',
        (env.COMPOSE_PROJECT_NAME || 'mervikon') + '_evidence',
        '--add-host',
        'idp.localtest.me:' + env.TRUSTED_PROXY_IP,
        '--add-host',
        env.CONFERENCE_API_HOST + ':' + env.TRUSTED_PROXY_IP,
        ...Object.keys(runtime).flatMap((k) => ['--env', k]),
        'mervikon/conference-probe:local',
        'Demo.Probe.dll',
      ],
      runtime,
    );
  } else if (command === 'recording') {
    const response = await fetch(
      'http://127.0.0.1:' + (env.DEMO_CONSOLE_PORT || 5080) + '/api/protocol-evidence',
    );
    if (!response.ok) throw Error('Console evidence unavailable');
    const data = await response.json();
    const selected = option('--run', '').split(',').filter(Boolean);
    if (selected.length) {
      if (
        selected.some(
          (id) => !/^[0-9a-f-]{36}$/i.test(id) || !data.observations.some((e) => e.runId === id),
        )
      )
        throw Error('Unknown recording run ID');
      data.observations = data.observations.filter((e) => selected.includes(e.runId));
    }
    const output = path.join(path.dirname(envFile), 'evidence/conference-recording.html');
    const json = JSON.stringify(data).replaceAll('<', '\\u003c');
    const html = `<!doctype html><html lang="en"><meta charset="utf-8"><title>Recorded conference evidence</title><style>body{font:20px system-ui;max-width:1100px;margin:3rem auto;padding:1rem;background:#101827;color:#e6edf7}pre{white-space:pre-wrap;overflow-wrap:anywhere}button{font:inherit;margin:1rem}</style><h1>Recorded evidence · offline</h1><p>Original executions only. This file cannot issue requests or establish current readiness.</p><button id="prev">Previous</button><button id="next">Next</button><pre id="event"></pre><script type="application/json" id="data">${json}</script><script>const data=JSON.parse(document.querySelector('#data').textContent);let i=0;function show(){document.querySelector('#event').textContent=JSON.stringify({packages:data.packages,step:i+1,total:data.observations.length,observation:data.observations[i]||null},null,2)}document.querySelector('#prev').onclick=()=>{i=Math.max(0,i-1);show()};document.querySelector('#next').onclick=()=>{i=Math.min(data.observations.length-1,i+1);show()};show()</script></html>`;
    fs.mkdirSync(path.dirname(output), { recursive: true });
    fs.writeFileSync(output, html, { mode: 0o600 });
    console.log(output);
  } else
    throw Error(
      'Commands: up, preflight, reset, nonce, fapi, recording. Optional --env-file. Build and provision first.',
    );
} catch (e) {
  console.error(e.message);
  process.exitCode = 1;
}
