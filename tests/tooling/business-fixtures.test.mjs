import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';
import { loadManifests, manifestFiles, validateOwnership } from '../../scripts/configuration.mjs';

const ids = JSON.parse(fs.readFileSync('config/proauth/ownership.json')).ids;
test('workload example grants exact pairs to the three registered jobs', () => {
  const doc = JSON.parse(fs.readFileSync('config/business/workload-assignments.example.json'));
  assert.deepEqual(doc.Memberships, []);
  assert.deepEqual(
    doc.Workloads,
    [
      ['dispatch', 'partners'],
      ['treasury-reader', 'workforce'],
      ['reconciliation', 'workforce'],
    ].map(([job, tenant]) => ({
      Issuer: 'EXACT_DISCOVERY_ISSUER',
      ClientId: ids['client-' + job],
      TenantId: ids['tenant-' + tenant],
      Partitions: [{ OrganizationId: 'buyer-manufacturing', CostCentre: 'engineering' }],
    })),
  );
});
test('native group fixture is opt-in and its direct membership is source-bound', () => {
  const file = 'config/proauth/optional-groups/45-approvers.yml';
  assert.ok(!manifestFiles('.').includes(file));
  const docs = loadManifests([file]);
  assert.equal(validateOwnership(docs, ids), 1);
  docs[0].metadata.userStoreId = ids['idp-research'];
  assert.throws(() => validateOwnership(docs, ids), /direct purchasing fixture/);
});
