import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { loadManifests, manifestFiles, validateOwnership } from '../../scripts/configuration.mjs';
const ids = JSON.parse(fs.readFileSync('config/proauth/ownership.json')).ids;
test('account navigation and API context are selected in parallel without widening disclosure', () => {
  const docs = loadManifests(manifestFiles(process.cwd()));
  const navigation = docs.find((d) => d.metadata.id === ids['claim-account-management']).spec;
  const context = docs.find(
    (d) => d.kind === 'ClaimRule' && d.spec.inputSource === 'IssuerContext',
  ).spec;
  assert.equal(navigation.priority, context.priority);
  assert.equal(navigation.inputSource, 'Pipeline');
  assert.equal(navigation.destination, 'IdentityToken');
  assert.equal(navigation.typeEvaluationRule, '^pa_account_management_url$');
  assert.equal(navigation.applyToTenantId, ids['tenant-workforce']);
  assert.equal(context.destination, 'AccessToken');
});
test('protocol registrations are opt-in and remain in their dedicated tenant', () => {
  assert.ok(manifestFiles(process.cwd()).every((f) => !f.includes('/protocols/')));
  const docs = loadManifests(manifestFiles(process.cwd(), false, false, true));
  for (const d of docs.filter(
    (d) => d.kind === 'ClientApp' && d.spec.name.startsWith('Mervikon protocol '),
  ))
    assert.deepEqual(d.relationships.tenants.refs, [{ id: ids['tenant-protocols'] }]);
  assert.ok(validateOwnership(docs, ids));
});
