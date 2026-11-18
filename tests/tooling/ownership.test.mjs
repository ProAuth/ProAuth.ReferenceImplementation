import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { manifestFiles, loadManifests, validateOwnership } from '../../scripts/configuration.mjs';
const root = process.cwd();
const ids = JSON.parse(fs.readFileSync('config/proauth/ownership.json')).ids;
const documents = () => loadManifests(manifestFiles(root));
test('all manifests remain inside the dedicated reference boundary', () =>
  assert.ok(validateOwnership(documents(), ids) > 0));
test('changing a subscription parent to another customer fails', () => {
  const docs = documents();
  docs.find((d) => d.kind === 'Subscription').spec.customerId = crypto.randomUUID();
  assert.throws(() => validateOwnership(docs, ids), /Subscription boundary/);
});
test('user cannot be placed in another UserStore', () => {
  const docs = documents();
  docs.find((d) => d.kind === 'User').metadata.userStoreId = crypto.randomUUID();
  assert.throws(() => validateOwnership(docs, ids), /User outside/);
});
test('relationship cannot silently target an unrelated resource', () => {
  const docs = documents();
  docs.find((d) => d.kind === 'Tenant').relationships.idpInstances.refs[0].id = crypto.randomUUID();
  assert.throws(() => validateOwnership(docs, ids), /Unowned/);
});
test('branding changes preserve ownership', () => {
  const docs = documents();
  for (const d of docs) if (d.spec.name) d.spec.name = 'Another fictional company';
  assert.doesNotThrow(() => validateOwnership(docs, ids));
});
test('federation cannot intercept another provider callback', () => {
  const docs = loadManifests(manifestFiles(root));
  const providers = docs.filter((d) => d.kind === 'IdpInstance');
  providers.at(-1).spec.options.find((o) => o.name === 'CallbackPath').value =
    providers[0].spec.options.find((o) => o.name === 'CallbackPath').value.toUpperCase();
  assert.throws(() => validateOwnership(docs, ids), /distinct callback/);
});
test('option IDs are optional across pages and explicit IDs must be owned', () => {
  const docs = loadManifests(manifestFiles(root));
  const provider = docs.find((d) => d.kind === 'IdpInstance');
  const [first, second] = provider.spec.options;
  const fixtureId = crypto.randomUUID();
  const fixtureIds = { ...ids, 'option-test': fixtureId };
  first.id = fixtureId;
  delete second.id;
  assert.doesNotThrow(() => validateOwnership(docs, fixtureIds));
  second.id = crypto.randomUUID();
  assert.throws(() => validateOwnership(docs, fixtureIds), /Unowned/);
});

test('branding cannot target another application or a global view', () => {
  for (const location of [
    '/Views/Shared/_Layout.liquid',
    `/Views/app/${ids['client-supplier']}/Shared/_Layout.liquid`,
  ]) {
    const docs = documents();
    docs.find((d) => d.kind === 'CustomView').spec.location = location;
    assert.throws(() => validateOwnership(docs, ids), /Branding resource/);
  }
});
test('Supplier Portal overrides use supplier sources and stay out of warehouse sign-in', () => {
  const brandingDir = 'config/proauth/branding';
  const docs = loadManifests([brandingDir + '/41-supplier.yml']);
  assert.equal(docs.length, 3);
  for (const d of docs) {
    assert.ok(
      d.spec.location.startsWith(
        `/${d.kind === 'CustomAsset' ? 'assets' : 'Views'}/app/${ids['client-supplier']}/`,
      ),
    );
    assert.ok(fs.existsSync(brandingDir + '/' + d.spec.dataFileReference));
    assert.doesNotThrow(() => validateOwnership([d], ids));
    d.spec.location = d.spec.location.replace(ids['client-supplier'], ids['client-warehouse']);
    assert.throws(() => validateOwnership([d], ids), /Branding resource/);
  }
});
test('branding cannot read an arbitrary local file or override its inline content', () => {
  for (const patch of [
    { dataFileReference: '../../private.env' },
    { content: 'unexpected' },
    { dataBase64: 'unexpected' },
  ]) {
    const docs = documents();
    Object.assign(docs.find((d) => d.kind === 'CustomView').spec, patch);
    assert.throws(() => validateOwnership(docs, ids), /Branding resource/);
  }
});
