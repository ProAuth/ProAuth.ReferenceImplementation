import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';

// Refresh only the public subjects for the named demo people. Existing operator policy wins.
export function demoAssignments(document, fixtures, subjects, ids, workloads, issuer) {
  const guid = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;
  const result = structuredClone(document);
  const seen = new Set();
  for (const { user, tenant, Member } of fixtures) {
    const subject = subjects[user];
    if (!guid.test(subject) || seen.has(subject))
      throw Error('Missing or ambiguous demo subject: ' + user);
    seen.add(subject);
    const matches = result.Memberships.filter(
      (row) => row.Issuer === issuer && row.TenantId === ids[tenant] && row.Member.Id === Member.Id,
    );
    if (matches.length > 1) throw Error('Ambiguous assignment for ' + Member.Id);
    const row = matches[0];
    if (
      result.Memberships.some(
        (other) =>
          other !== row &&
          other.Issuer === issuer &&
          (other.Subject === subject || other.SubjectAliases?.includes(subject)) &&
          (other.TenantId === ids[tenant] || other.Member.Id !== Member.Id),
      )
    )
      throw Error('Subject already assigned: ' + user);
    if (!row)
      result.Memberships.push({ Issuer: issuer, TenantId: ids[tenant], Subject: subject, Member });
    else if (row.Subject !== subject) {
      row.Subject = subject;
      if (row.SubjectAliases) row.SubjectAliases = row.SubjectAliases.filter((s) => s !== subject);
    }
  }
  result.Workloads ??= [];
  for (const job of workloads) {
    if (
      !result.Workloads.some(
        (w) => w.Issuer === issuer && w.TenantId === job.TenantId && w.ClientId === job.ClientId,
      )
    )
      result.Workloads.push({ ...job, Issuer: issuer });
  }
  return result;
}

export function publishDemoAssignments(directory, create, { restoreMissing = false } = {}) {
  if (!directory || !path.isAbsolute(directory))
    throw Error(
      'Set ENTITLEMENT_DIR to a dedicated absolute directory in your private environment.',
    );
  if (!fs.existsSync(directory)) {
    fs.mkdirSync(directory, { recursive: true, mode: 0o755 });
    fs.chmodSync(directory, 0o755);
  }
  const lock = path.join(directory, '.demo-setup.lock');
  const fd = fs.openSync(lock, 'wx', 0o600);
  const next = path.join(directory, crypto.randomUUID() + '.next');
  try {
    const file = path.join(directory, 'memberships.json');
    // A published document is operator-owned, including deliberately absent rows.
    if (fs.existsSync(file) && !restoreMissing) return;
    const current = fs.existsSync(file)
      ? JSON.parse(fs.readFileSync(file, 'utf8'))
      : { Memberships: [] };
    const result = create(current);
    if (JSON.stringify(result) === JSON.stringify(current)) return;
    fs.writeFileSync(next, JSON.stringify(result, null, 2) + '\n', { flag: 'wx', mode: 0o600 });
    fs.chmodSync(next, 0o644);
    fs.renameSync(next, file);
  } finally {
    fs.rmSync(next, { force: true });
    fs.closeSync(fd);
    fs.unlinkSync(lock);
  }
}
