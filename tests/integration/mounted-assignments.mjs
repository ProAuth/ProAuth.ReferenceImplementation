// From the repository root. Uses one existing dispatch credential, never provisioning credentials.
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import https from 'node:https';
import { randomUUID } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { parseEnv } from 'node:util';
import assert from 'node:assert/strict';

const [envFile, published] = process.argv.slice(2);
const e = parseEnv(fs.readFileSync(envFile, 'utf8'));
const issuer = new URL(e.PROAUTH_ISSUER);
assert(issuer.protocol === 'https:' && !issuer.username && !issuer.password);
const id = 'mervikon-test-' + randomUUID();
const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'mervikon-'));
const grants = path.join(dir, 'entitlements'),
  certs = path.join(dir, 'tls');
const run = (cmd, args) =>
  execFileSync(cmd, args, {
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
    timeout: 45000,
  }).trim();
const ca = fs.readFileSync(e.TRUSTED_CA_FILE);
function call(url, ca, { method = 'GET', headers = {}, body } = {}) {
  return new Promise((resolve, reject) => {
    const request = https.request(
      url,
      { method, ca, headers, signal: AbortSignal.timeout(8000) },
      (res) => {
        let text = '';
        res
          .setEncoding('utf8')
          .on('data', (part) => {
            text += part;
          })
          .on('end', () => resolve({ status: res.statusCode, text }));
      },
    );
    request.on('error', reject).end(body);
  });
}
const replace = (doc) => {
  fs.writeFileSync(path.join(grants, 'next'), JSON.stringify(doc), { mode: 0o644 });
  fs.chmodSync(path.join(grants, 'next'), 0o644);
  fs.renameSync(path.join(grants, 'next'), path.join(grants, 'memberships.json'));
};
let started = false;
try {
  fs.mkdirSync(grants);
  fs.chmodSync(grants, 0o755);
  fs.mkdirSync(certs);
  fs.chmodSync(certs, 0o755);
  const empty = { Memberships: [], Workloads: [] };
  replace(empty);
  const crt = path.join(certs, 'localhost.crt'),
    key = path.join(certs, 'localhost.key');
  run('openssl', [
    'req',
    '-x509',
    '-newkey',
    'rsa:2048',
    '-nodes',
    '-days',
    '1',
    '-subj',
    '/CN=localhost',
    '-addext',
    'subjectAltName=DNS:localhost,IP:127.0.0.1',
    '-keyout',
    key,
    '-out',
    crt,
  ]);
  // Ephemeral fixture key: traversable only within this private temp root and its test container.
  fs.chmodSync(key, 0o644);
  const discovery = await call(
    e.PROAUTH_ISSUER.replace(/\/$/, '') + '/.well-known/openid-configuration',
    ca,
  );
  assert.equal(discovery.status, 200, 'issuer discovery HTTP ' + discovery.status);
  const metadata = JSON.parse(discovery.text);
  assert.equal(metadata.issuer, e.PROAUTH_ISSUER);
  assert.equal(new URL(metadata.token_endpoint).origin, issuer.origin);
  const formEncode = (value) => new URLSearchParams([['v', value]]).toString().slice(2);
  const basic = formEncode(e.DISPATCH_CLIENT_ID) + ':' + formEncode(e.DISPATCH_CLIENT_SECRET);
  const response = await call(metadata.token_endpoint, ca, {
    method: 'POST',
    headers: {
      Authorization: 'Basic ' + Buffer.from(basic).toString('base64'),
      'Content-Type': 'application/x-www-form-urlencoded',
    },
    body: new URLSearchParams({
      grant_type: 'client_credentials',
      scope: 'fulfilment.read fulfilment.write',
      resource: 'urn:mervikon:fulfilment',
      proauth_tenant: e.PARTNERS_TENANT_ID,
    }).toString(),
  });
  if (response.status !== 200) {
    let code;
    try {
      code = JSON.parse(response.text).error;
    } catch {
      /* A non-OAuth error still fails the test. */
    }
    const known = [
      'invalid_client',
      'invalid_grant',
      'invalid_scope',
      'invalid_target',
      'unauthorized_client',
      'invalid_request',
    ];
    assert.fail(
      'issuer returned HTTP ' +
        response.status +
        ' (' +
        (known.includes(code) ? code : 'unclassified error') +
        ')',
    );
  }
  const token = JSON.parse(response.text).access_token;
  assert.ok(token);
  run('docker', [
    'run',
    '-d',
    '--rm',
    '--name',
    id,
    '--read-only',
    '--tmpfs',
    '/tmp',
    '--cap-drop',
    'ALL',
    '--add-host',
    issuer.hostname + ':host-gateway',
    '-p',
    '127.0.0.1::8443',
    '-v',
    path.resolve(published) + ':/fixture:ro',
    '-v',
    grants + ':/run/mervikon:ro',
    '-v',
    certs + ':/fixture-tls:ro',
    '-w',
    '/fixture',
    '-e',
    'ASPNETCORE_URLS=https://+:8443',
    '-e',
    'ASPNETCORE_Kestrel__Certificates__Default__Path=/fixture-tls/localhost.crt',
    '-e',
    'ASPNETCORE_Kestrel__Certificates__Default__KeyPath=/fixture-tls/localhost.key',
    '-e',
    'MembershipFile=/run/mervikon/memberships.json',
    '-e',
    'ResourceServer__Authority=' + e.PROAUTH_ISSUER,
    '-e',
    'ResourceServer__Audience=urn:mervikon:fulfilment',
    '-e',
    'DispatchClientId=' + e.DISPATCH_CLIENT_ID,
    '--entrypoint',
    'dotnet',
    'mervikon/fulfilment-api:local',
    'MountedFulfilment.dll',
  ]);
  started = true;
  const origin = 'https://' + run('docker', ['port', id, '8443/tcp']),
    testCa = fs.readFileSync(crt);
  const api = (url, body) =>
    call(origin + url, testCa, {
      method: body ? 'POST' : 'GET',
      headers: { Authorization: 'Bearer ' + token, 'Content-Type': 'application/json' },
      body: body && JSON.stringify(body),
    });
  for (let i = 0; i < 50; i++) {
    try {
      if ((await call(origin + '/health/live', testCa)).status === 200) break;
    } catch {}
    await new Promise((resolve) => setTimeout(resolve, 150));
  }
  const identity = await api('/api/identity');
  assert.equal(identity.status, 200, 'identity HTTP ' + identity.status);
  const who = JSON.parse(identity.text);
  assert.ok(
    who.issuer === e.PROAUTH_ISSUER && who.tenantId === e.PARTNERS_TENANT_ID && who.subject,
  );
  assert.equal((await api('/api/me')).status, 403);
  const assigned = {
    Memberships: [
      {
        Issuer: who.issuer,
        Subject: who.subject,
        TenantId: who.tenantId,
        Member: {
          Id: 'dispatch-diagnostic',
          OrganizationId: 'supplier-northstar',
          CostCentre: 'dispatch',
          Roles: [],
          ApprovalLimitChf: 0,
        },
      },
    ],
    Workloads: [
      {
        Issuer: who.issuer,
        TenantId: who.tenantId,
        ClientId: e.DISPATCH_CLIENT_ID,
        Partitions: [{ OrganizationId: 'buyer-manufacturing', CostCentre: 'engineering' }],
      },
    ],
  };
  replace(assigned);
  // Desktop file sharing can briefly reject an open while propagating rename. A 503 grants nothing.
  async function expectDecision(route, expected) {
    const deadline = performance.now() + 3000;
    while (true) {
      const result = await api(route);
      if (result.status === expected) return;
      assert.equal(result.status, 503, 'unexpected business decision');
      assert.ok(performance.now() < deadline, 'mount did not converge in 3 s');
      await new Promise((resolve) => setTimeout(resolve, 10));
    }
  }
  await expectDecision('/api/me', 200);
  const list = await api('/api/dispatch');
  assert.equal(list.status, 200, 'dispatch list');
  const [order] = JSON.parse(list.text);
  assert.ok(order?.purchaseId && order.revision === 2);
  const start = performance.now();
  replace(empty);
  await expectDecision('/api/me', 403);
  const elapsed = Math.ceil(performance.now() - start);
  assert.equal((await api('/api/dispatch')).status, 403);
  replace({ ...assigned, ResourceServer: { Audience: 'injected' } });
  assert.equal((await api('/api/me')).status, 503);
  replace(assigned);
  await expectDecision('/api/me', 200);
  assert.equal((await api('/api/dispatch/' + order.purchaseId, { revision: 2 })).status, 200);
  console.log(
    'One issuer token, one container. Identity 200; membership 403 -> 200 -> 403; invalid document 503; original order dispatched 200. Removal visible within ' +
      elapsed +
      ' ms in this run.',
  );
} catch (error) {
  // Never dump HTTP response bodies, environment, command arguments or OAuth material.
  console.error(
    'Mounted fixture failed: ' +
      (error instanceof assert.AssertionError ? error.message : error.code || error.name),
  );
  process.exitCode = 1;
} finally {
  try {
    if (started) run('docker', ['rm', '-f', id]);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}
