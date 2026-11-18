import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { publishDemoAssignments, demoAssignments } from '../../scripts/demo-memberships.mjs';
const load = (path) => JSON.parse(fs.readFileSync(path, 'utf8'));
const fixtures = load('config/business/demo-members.json');
const ids = load('config/proauth/ownership.json').ids;
const issuer = 'https://issuer.example';
const jobs = load('config/business/workload-assignments.example.json').Workloads;
const subjects = Object.fromEntries(
  fixtures.map(({ user }, i) => [user, `40000000-0000-0000-0000-00000000000${i}`]),
);
const sync = (doc, resolved = subjects) =>
  demoAssignments(doc, fixtures, resolved, ids, jobs, issuer);
test('seed the five journey actors and three workloads, preserve local policy on refresh', () => {
  const fresh = sync({ Memberships: [] });
  assert.equal(fresh.Memberships.length, 5);
  assert.equal(fresh.Workloads.length, 3);
  const elena = fresh.Memberships.find((r) => r.Member.Id === 'elena');
  elena.Member = { ...elena.Member, Roles: [] };
  elena.Subject = 'old-subject';
  fresh.Workloads[0].Partitions = [];
  const updated = sync(fresh);
  assert.equal(updated.Memberships[0].Subject, subjects['user-elena']);
  assert.deepEqual(updated.Memberships[0].Member.Roles, []);
  assert.deepEqual(updated.Workloads[0].Partitions, []);
  assert.deepEqual(sync(updated), updated);
});
test('incomplete or conflicting lookups cannot publish partial grants', () => {
  assert.throws(() => sync({ Memberships: [] }, {}), /Missing/);
  const existing = sync({ Memberships: [] });
  existing.Memberships[0].Member = { ...existing.Memberships[0].Member, Id: 'another-person' };
  assert.throws(() => sync(existing), /already assigned/);
  assert.equal(existing.Memberships.length, 5);
});

test('a second provisioning preserves removed people and jobs until explicit repair', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'membership-seed-'));
  try {
    const publish = (options) => publishDemoAssignments(dir, sync, options);
    publish();
    const file = path.join(dir, 'memberships.json');
    const withdrawn = JSON.parse(fs.readFileSync(file, 'utf8'));
    withdrawn.Memberships.pop();
    withdrawn.Workloads.pop();
    fs.writeFileSync(file, JSON.stringify(withdrawn));
    publish();
    assert.deepEqual(JSON.parse(fs.readFileSync(file, 'utf8')), withdrawn);
    publish({ restoreMissing: true });
    assert.deepEqual(JSON.parse(fs.readFileSync(file, 'utf8')), sync({ Memberships: [] }));
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
