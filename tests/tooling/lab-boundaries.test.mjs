import { selectImages } from '../../scripts/build-images.mjs';
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import YAML from 'yaml';
import { manifestFiles, loadManifests, validateOwnership } from '../../scripts/configuration.mjs';
const lab = YAML.parse(fs.readFileSync('deploy/compose/lab.yml', 'utf8'));
test('normal provisioning excludes intentionally unsafe lab registrations', () => {
  assert.ok(manifestFiles(process.cwd()).every((f) => !f.includes('unsafe-lab')));
  const docs = loadManifests(manifestFiles(process.cwd(), true));
  assert.ok(
    validateOwnership(docs, JSON.parse(fs.readFileSync('config/proauth/ownership.json')).ids),
  );
});
test('no lab service publishes a port or reverse-proxy route', () => {
  for (const service of Object.values(lab.services)) {
    assert.equal(service.ports, undefined);
    assert.equal(service.labels, undefined);
    assert.deepEqual(service.profiles, ['unsafe-lab']);
  }
});
test('attacker has neither OAuth client credentials nor victim keys nor issuer egress', () => {
  const runner = lab.services['lab-runner'];
  assert.deepEqual(runner.networks, ['lab-front']);
  assert.equal(runner.volumes, undefined);
  assert.ok(
    Object.keys(runner.environment).every(
      (k) => !/(Authentication|Certificate|PrivateKey|PROXY)/.test(k),
    ),
  );
  assert.equal(lab.networks['lab-front'].internal, true);
});
test('capture proxy cannot reach the issuer or ordinary application network', () => {
  assert.deepEqual(Object.keys(lab.services['lab-capture'].networks).sort(), [
    'lab-backend',
    'lab-front',
  ]);
  assert.equal(lab.networks['lab-backend'].internal, true);
});
test('postponed federation cannot be enabled by an old flag', () =>
  assert.throws(() => manifestFiles(process.cwd(), true, true), /postponed/));
test('public JWKS substitution remains a valid YAML scalar', () => {
  const source = fs.readFileSync('config/proauth/clients/21-workflows.yml', 'utf8');
  const substituted = source.replaceAll(
    '{{env:FINANCE_ASSERTION_PUBLIC_JWKS}}',
    '{"keys":[{"kty":"RSA","n":"public-test-value","e":"AQAB"}]}',
  );
  assert.ok(YAML.parseAllDocuments(substituted).every((d) => d.errors.length === 0));
});
test('replica probe cannot publish its isolated state or routes', () => {
  const replicas = YAML.parse(fs.readFileSync('deploy/compose/replicas.yml', 'utf8'));
  for (const service of Object.values(replicas.services)) {
    assert.equal(service.ports, undefined);
    assert.equal(service.labels, undefined);
  }
  assert.equal(replicas.networks['replay-state'].internal, true);
  assert.deepEqual(replicas.services['lab-replay-redis'].networks, ['replay-state']);
  assert.ok(replicas.services['lab-replay-redis'].command.includes('noeviction'));
  assert.ok(replicas.services['lab-replay-redis'].command.includes('always'));
  assert.equal(replicas.volumes['mervikon-state'], undefined);
});

test('the console routes only its viewer through Traefik and keeps ingestion on the evidence network', () => {
  const base = YAML.parse(fs.readFileSync('deploy/compose/compose.yml', 'utf8'));
  const viewer = base.services['demo-console'];
  assert.deepEqual(viewer.ports, ['127.0.0.1:${DEMO_CONSOLE_PORT:-5080}:8080']);
  assert.equal(
    viewer.labels['traefik.http.services.mervikon-console.loadbalancer.server.port'],
    '8080',
  );
  assert.equal(viewer.labels['traefik.http.routers.mervikon-console.tls'], 'true');
  assert.deepEqual(viewer.networks.evidence.aliases, [viewer.environment.IngestionAddressName]);
  assert.deepEqual(viewer.networks.edge, {});
});

test('browser attack labs default to a dedicated entry point', () => {
  const lab = YAML.parse(fs.readFileSync('deploy/compose/conference.yml', 'utf8'));
  const listeners = Object.values(lab.services).flatMap((s) =>
    Object.entries(s.labels || {}).filter(([name]) => name.endsWith('.entrypoints')),
  );
  assert.equal(listeners.length, 5);
  assert.ok(listeners.every(([, entry]) => entry === '${MERVIKON_LAB_ENTRYPOINT:-mervikon-lab}'));
});

test('only Procurement holds its exchange proof key', () => {
  const base = YAML.parse(fs.readFileSync('deploy/compose/compose.yml', 'utf8'));
  const holders = Object.entries(base.services).filter(([, service]) =>
    Object.values(service.environment ?? {}).some((value) =>
      String(value).includes('PROCUREMENT_DPOP_JWK'),
    ),
  );
  assert.deepEqual(
    holders.map(([name]) => name),
    ['procurement-api'],
  );
});

test('an ordinary purchase build excludes all optional lab projects', () => {
  assert.ok(selectImages(null, false).every(([, project]) => project.startsWith('src/')));
  assert.equal(selectImages('protocol-api', false).length, 1);
  assert.ok(selectImages(null, true).length > selectImages(null, false).length);
});
