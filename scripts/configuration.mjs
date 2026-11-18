import fs from 'node:fs';
import path from 'node:path';
import { parseEnv } from 'node:util';
import { parseAllDocuments } from 'yaml';

export function readEnvironment(file) {
  const env = parseEnv(fs.readFileSync(file, 'utf8'));
  // Deliberately no shell evaluation or interpolation. Files can safely contain credential values.
  return env;
}

function manifestsIn(root, folder) {
  const directory = path.join(root, 'config/proauth', folder);
  return fs
    .readdirSync(directory)
    .filter((name) => name.endsWith('.yml'))
    .sort()
    .map((name) => path.join(directory, name));
}

export function brandingManifestFiles(root) {
  return manifestsIn(root, 'branding');
}

export function manifestFiles(
  root,
  includeLab = false,
  includeFederation = false,
  includeProtocols = false,
) {
  if (includeFederation)
    throw new Error(
      'Federation and SCIM synchronization are postponed pending a ProAuth upstream integration.',
    );
  return [
    'base',
    'clients',
    'policies',
    'identities',
    'branding',
    ...(includeLab ? ['unsafe-lab'] : []),
    ...(includeProtocols ? ['protocols'] : []),
  ].flatMap((folder) => manifestsIn(root, folder));
}

export function validateOwnership(documents, ids) {
  const owned = new Set(Object.values(ids));
  const seen = new Set();
  const callbacks = new Set();
  const providers = new Set(
    Object.entries(ids)
      .filter(([k]) => k.startsWith('idp-'))
      .map(([, v]) => v),
  );
  const clients = new Set(
    Object.entries(ids)
      .filter(([k]) => k.startsWith('client-'))
      .map(([, v]) => v),
  );
  const visit = (value, key = '') => {
    if (typeof value === 'string') {
      for (const id of value.matchAll(/[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}/gi))
        if (!owned.has(id[0])) throw new Error(`Unowned resource reference in ${key}`);
      if (/\{\{(?:guid|secret)/.test(value))
        throw new Error('Unstable generated resource identity or credential');
    } else if (Array.isArray(value)) value.forEach((v) => visit(v, key));
    else if (value && typeof value === 'object')
      Object.entries(value).forEach(([k, v]) => visit(v, k));
  };
  for (const d of documents) {
    if (!d || !owned.has(d.metadata?.id)) throw new Error('Resource is not owned by Mervikon');
    const identity = `${d.kind}:${d.metadata.id}`;
    if (seen.has(identity)) throw new Error('Duplicate resource declaration');
    seen.add(identity);
    const s = d.spec;
    if (!s) throw new Error('Missing resource spec');
    if (d.kind === 'IdpInstance') {
      const callback = s.options?.find((o) => o.name === 'CallbackPath')?.value?.toLowerCase();
      if (callback && callbacks.has(callback))
        throw new Error('Identity providers must have distinct callback paths');
      if (callback) callbacks.add(callback);
    }
    if (d.kind === 'User') {
      if (d.apiVersion !== 'userstore.proauth.net/v2' || !providers.has(d.metadata.userStoreId))
        throw new Error('User outside Mervikon UserStore');
    } else if (d.kind === 'Group') {
      if (
        d.apiVersion !== 'userstore.proauth.net/v2' ||
        d.metadata.id !== ids['group-purchasing-approvers'] ||
        d.metadata.userStoreId !== ids['idp-manufacturing'] ||
        JSON.stringify(d.relationships) !==
          JSON.stringify({ memberUsers: { mode: 'set', refs: [{ id: ids['user-amir'] }] } })
      )
        throw new Error('Group outside the direct purchasing fixture');
    } else {
      if (d.apiVersion !== 'management.proauth.net/v2')
        throw new Error('Unsupported management version');
      switch (d.kind) {
        case 'Customer':
          if (d.metadata.id !== ids.customer) throw new Error('Customer boundary');
          break;
        case 'Subscription':
          if (s.customerId !== ids.customer || d.metadata.id !== ids.subscription)
            throw new Error('Subscription boundary');
          break;
        case 'Tenant':
        case 'ClientApp':
        case 'IdpInstance':
        case 'ClaimRule':
          if (s.subscriptionId !== ids.subscription)
            throw new Error('Resource outside Mervikon subscription');
          break;
        case 'CustomView':
        case 'CustomAsset': {
          const allowed = new Map();
          for (const client of ['workplace', 'finance', 'supplier']) {
            const directory = client === 'supplier' ? 'assets/supplier' : 'assets';
            const assets =
              client === 'supplier'
                ? ['supplier.css']
                : ['mervikon-light.svg', 'mervikon-dark.svg', 'mervikon.css'];
            for (const file of assets)
              allowed.set(ids[`branding-${client}-${file}`], {
                kind: 'CustomAsset',
                location: `/assets/app/${ids[`client-${client}`]}/${file}`,
                source: `${directory}/${file}`,
                mime: file.endsWith('.css') ? 'text/css' : 'image/svg+xml',
              });
            for (const file of ['_Head.liquid', '_Layout.liquid'])
              allowed.set(ids[`branding-${client}-${file}`], {
                kind: 'CustomView',
                location: `/Views/app/${ids[`client-${client}`]}/Shared/${file}`,
                source: `${directory}/${file}`,
              });
          }
          const expected = allowed.get(d.metadata.id);
          const keys = [
            'location',
            'dataFileReference',
            ...(d.kind === 'CustomAsset' ? ['mimetype'] : []),
          ];
          if (
            !expected ||
            expected.kind !== d.kind ||
            s.location !== expected.location ||
            s.dataFileReference !== expected.source ||
            s.mimetype !== expected.mime ||
            Object.keys(s).some((key) => !keys.includes(key))
          )
            throw new Error('Branding resource outside approved app paths');
          break;
        }
        case 'ClientAppAuthenticationConfiguration':
          if (!clients.has(d.metadata.id))
            throw new Error('Client authentication outside Mervikon');
          break;
        default:
          throw new Error(`Resource kind needs an ownership review: ${d.kind}`);
      }
    }
    visit(d);
  }
  return documents.length;
}

export function loadManifests(files) {
  return files.flatMap((f) =>
    parseAllDocuments(fs.readFileSync(f, 'utf8'), { uniqueKeys: true }).map((doc) => {
      if (doc.errors.length) throw new Error(`Invalid YAML in ${path.basename(f)}`);
      return doc.toJS();
    }),
  );
}
