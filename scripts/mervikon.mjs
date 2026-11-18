import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import https from 'node:https';
import tls from 'node:tls';
import { spawn, spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import {
  readEnvironment,
  manifestFiles,
  brandingManifestFiles,
  loadManifests,
  validateOwnership,
} from './configuration.mjs';
import { buildImages } from './build-images.mjs';
import { generateKeys } from './generate-keys.mjs';

import { demoAssignments, publishDemoAssignments } from './demo-memberships.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const [command, ...args] = process.argv.slice(2);
const option = (name, fallback) => {
  const i = args.indexOf(name);
  return i < 0 ? fallback : args[i + 1];
};
const envFile = path.resolve(option('--env-file', path.join(root, '.local/local.env')));
const productBuild = JSON.parse(fs.readFileSync(path.join(root, 'config/product-build.json')));
const cliImage = `mervikon/cli:${productBuild.version}`;
const ownership = JSON.parse(
  fs.readFileSync(path.join(root, 'config/proauth/ownership.json'), 'utf8'),
).ids;
const local = path.dirname(envFile);
const privateWrite = (file, text) => {
  fs.mkdirSync(path.dirname(file), { recursive: true, mode: 0o700 });
  fs.writeFileSync(file, text, { mode: 0o600 });
  fs.chmodSync(file, 0o600);
};
const fail = (message) => {
  throw new Error(message);
};
const read = (file) => readEnvironment(path.resolve(file));
function run(exe, argv, env, logName, timeoutMs) {
  const result = spawnSync(exe, argv, {
    cwd: root,
    env: { ...process.env, ...env },
    encoding: 'utf8',
    maxBuffer: 20 * 1024 * 1024,
    timeout: timeoutMs,
  });
  if (logName) {
    privateWrite(path.join(local, 'logs', logName), (result.stdout || '') + (result.stderr || ''));
    if (result.status !== 0)
      fail(
        `Command failed; inspect the private ${logName} log locally (it may contain sensitive diagnostics).`,
      );
  } else {
    process.stdout.write(result.stdout || '');
    process.stderr.write(result.stderr || '');
    if (result.status !== 0) fail('Command failed.');
  }
  return result.stdout || '';
}
function runReplicaProbe(compose, container, env) {
  return new Promise((resolve, reject) => {
    const child = spawn(
      'docker',
      [...compose, 'run', '--rm', '-T', '--name', container, 'lab-victim-dpop'],
      { cwd: root, env: { ...process.env, ...env }, stdio: ['pipe', 'pipe', 'pipe'] },
    );
    let output = '',
      errors = '',
      pending = '',
      controlFailed = false;
    const timeout = setTimeout(() => child.kill('SIGTERM'), 120000);
    child.stdout.on('data', (chunk) => {
      output += chunk.toString();
      pending += chunk.toString();
      const lines = pending.split('\n');
      pending = lines.pop();
      for (const line of lines) {
        const marker = line.trim();
        if (!['REPLICA_OUTAGE_REQUEST', 'REPLICA_RECOVERY_REQUEST'].includes(marker)) continue;
        const stop = marker === 'REPLICA_OUTAGE_REQUEST';
        const result = spawnSync(
          'docker',
          [
            ...compose,
            ...(stop
              ? ['stop', '--timeout', '3', 'lab-replay-redis']
              : ['start', 'lab-replay-redis']),
          ],
          { cwd: root, env: { ...process.env, ...env }, encoding: 'utf8', timeout: 15000 },
        );
        errors += result.stderr || '';
        if (result.status !== 0) {
          controlFailed = true;
          child.stdin.end('failed\n');
        } else child.stdin.write(stop ? 'stopped\n' : 'started\n');
      }
    });
    child.stderr.on('data', (chunk) => {
      errors += chunk.toString();
    });
    child.stdin.on('error', () => {
      controlFailed = true;
    });
    child.on('error', (error) => {
      clearTimeout(timeout);
      reject(error);
    });
    child.on('close', (code) => {
      clearTimeout(timeout);
      privateWrite(path.join(local, 'logs/replica-run.log'), output + errors);
      if (code !== 0 || controlFailed)
        reject(new Error('Replica control failed; inspect private replica-run.log.'));
      else resolve(output);
    });
  });
}
function writeControlComparisons(env) {
  const select = (value, fields) =>
    Object.fromEntries(fields.map((key) => [key, value[key] ?? null]));
  const protocols = [];
  for (const variant of ['jwt', 'opaque', 'jwe', 'mtls']) {
    const file = path.join(local, `evidence/protocol-${variant}.json`);
    if (!fs.existsSync(file)) continue;
    if (fs.statSync(file).size > 16000) fail('Oversized protocol evidence.');
    const raw = JSON.parse(fs.readFileSync(file));
    protocols.push({
      variant,
      ...select(raw, [
        'observedAt',
        'legitimate',
        'invalidToken',
        'afterRevocation',
        'expired',
        'revocationResponseProcessed',
        'missingCertificate',
        'wrongCertificate',
        'forgedCertificateHeader',
        'tamperedCiphertext',
      ]),
    });
  }
  const file = path.join(local, 'evidence/replicas.json');
  let replicas = null;
  if (fs.existsSync(file)) {
    if (fs.statSync(file).size > 16000) fail('Oversized replica evidence.');
    const raw = JSON.parse(fs.readFileSync(file));
    if (raw.outageOne !== undefined)
      replicas = select(raw, [
        'observedAt',
        'first',
        'replay',
        'freshOne',
        'freshTwo',
        'concurrentRequests',
        'accepted',
        'rejected',
        'outageOne',
        'outageTwo',
        'recoveredOne',
        'recoveredTwo',
        'replayAfterRestart',
        'instancesVerified',
        'cleanupVerified',
      ]);
  }
  privateWrite(env.CONTROL_RESULTS_FILE, JSON.stringify({ protocols, replicas }, null, 2) + '\n');
  fs.chmodSync(env.CONTROL_RESULTS_FILE, 0o644);
}
async function waitForProtocolApi(env) {
  const target = new URL('/health/live', env.PROTOCOL_ORIGIN);
  if (target.protocol !== 'https:' || target.username || target.password)
    fail('Protocol API requires HTTPS.');
  const ca = [
    ...tls.rootCertificates,
    ...(env.TRUSTED_CA_FILE ? [fs.readFileSync(env.TRUSTED_CA_FILE)] : []),
  ];
  const deadline = Date.now() + 30000;
  while (Date.now() < deadline) {
    const status = await new Promise((resolve, reject) => {
      const request = https.get(target, { ca, timeout: 2000 }, (response) => {
        response.resume();
        resolve(response.statusCode);
      });
      request.on('timeout', () =>
        request.destroy(Object.assign(new Error('Readiness timeout'), { code: 'ETIMEDOUT' })),
      );
      request.on('error', (error) =>
        ['ECONNREFUSED', 'ECONNRESET', 'ETIMEDOUT'].includes(error.code)
          ? resolve(0)
          : reject(error),
      );
    });
    if (status === 200) return;
    if (![0, 404, 502, 503].includes(status)) fail(`Protocol readiness returned HTTP ${status}.`);
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  fail('Protocol API did not become ready; no token acquired.');
}
function validateUserStores(env) {
  // Validate exactly what the importer receives, without exposing credentials in argv.
  const stores = ['MANUFACTURING', 'RESEARCH', 'PARTNERS'];
  run(
    'docker',
    [
      'run',
      '--rm',
      '--network',
      'none',
      ...stores.flatMap((store) => ['-e', `MERVIKON_${store}_CONNECTION_STRING`]),
      '--entrypoint',
      'dotnet',
      cliImage,
      '/setup/Demo.MembershipSetup.dll',
      'validate-userstores',
    ],
    env,
  );
}
function check(env) {
  const files = manifestFiles(
    root,
    args.includes('--lab'),
    args.includes('--federation'),
    args.includes('--protocols'),
  );
  const docs = loadManifests(files);
  const count = validateOwnership(docs, ownership);
  for (const file of files)
    for (const [, key] of fs.readFileSync(file, 'utf8').matchAll(/\{\{env:([^}]+)}}/g))
      if (env && !env[key]) fail(`Missing environment value: ${key}`);
  if (env) {
    validateUserStores(env);
    for (const key of ['WORKPLACE_ORIGIN', 'PROCUREMENT_ORIGIN', 'PROAUTH_HOST', 'PROAUTH_ISSUER'])
      if (new URL(env[key]).protocol !== 'https:') fail(`${key} requires HTTPS.`);
  }
  console.log(`Ownership check passed: ${count} resources, dedicated customer and subscription.`);
  return files;
}
// System.CommandLine expands a response file inside the CLI process. Keep privileged
// values out of host argv, Docker container commands and process-list recordings.
function cli(env, argv, logName, credential) {
  if (!credential) return invokeCli(env, argv, logName);
  const [optionName, value] = credential;
  if (typeof value !== 'string' || !value.length || /["\r\n\0]/.test(value))
    fail(
      `The pinned CLI response-file format requires a single-line ${optionName} value without double quotes.`,
    );
  const dir = fs.mkdtempSync(path.join(local, 'cli-args-'));
  const file = path.join(dir, 'credential.rsp');
  try {
    privateWrite(file, `${optionName}="${value}"\n`);
    return invokeCli(env, argv, logName, file);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}
function invokeCli(env, argv, logName, credentialFile) {
  if (env.CLI_MODE === 'container') {
    const cliRoot = path.join(local, 'cli-container');
    fs.mkdirSync(cliRoot, { recursive: true, mode: 0o700 });
    const argvInside = argv.map((v) =>
      v.startsWith(root + '/') ? '/workspace/' + path.relative(root, v) : v,
    );
    const required = new Set(
      manifestFiles(
        root,
        args.includes('--lab'),
        args.includes('--federation'),
        args.includes('--protocols'),
      ).flatMap((file) =>
        [...fs.readFileSync(file, 'utf8').matchAll(/\{\{env:([^}]+)}}/g)].map((m) => m[1]),
      ),
    );
    const containerEnv = [...required].flatMap((k) => ['--env', k]);
    return run(
      'docker',
      [
        'run',
        '--rm',
        '--network',
        env.PROXY_MODE === 'standalone' ? 'bridge' : env.MERVIKON_EDGE_NETWORK,
        '--mount',
        `type=bind,src=${root}/config,dst=/workspace/config,readonly`,
        '--mount',
        `type=bind,src=${cliRoot},dst=/state`,
        '--mount',
        `type=bind,src=${env.TRUSTED_CA_FILE},dst=/trust/root.crt,readonly`,
        ...(credentialFile
          ? ['--mount', `type=bind,src=${credentialFile},dst=/run/credential.rsp,readonly`]
          : []),
        ...(argv[0] === 'data' ? containerEnv : []),
        cliImage,
        ...argvInside,
        ...(credentialFile ? ['@/run/credential.rsp'] : []),
        '--configurationlocation',
        '/state/profile',
        '--nativetokenstorelocation',
        '/state/tokens',
        '--customTrustedRootCaFilePaths',
        '/trust/root.crt',
      ],
      env,
      logName,
    );
  }
  const ca = env.TRUSTED_CA_FILE ? ['--customTrustedRootCaFilePaths', env.TRUSTED_CA_FILE] : [];
  return run(
    env.PROAUTH_CLI || path.join(root, '.local/tools/proauthcli'),
    [
      ...argv,
      ...(credentialFile ? ['@' + credentialFile] : []),
      '--configurationlocation',
      path.join(local, 'cli'),
      '--nativetokenstorelocation',
      path.join(local, 'cli/tokens'),
      ...ca,
    ],
    env,
    logName,
  );
}
function workflowSettings(env) {
  const suffix = (env.WORKPLACE_HOST || 'workplace.localtest.me').split('.').slice(1).join('.');
  const port = new URL(env.WORKPLACE_ORIGIN || 'https://workplace.localtest.me').port;
  const values = {
    LAB_RESULTS_FILE: path.join(local, 'evidence/lab-comparison.json'),
    LAB_CONTROL_KEY: crypto.randomBytes(48).toString('base64url'),
    LAB_BEARER_CLIENT_ID: ownership['client-lab-bearer'],
    LAB_DPOP_CLIENT_ID: ownership['client-lab-dpop'],
    LAB_BEARER_CLIENT_SECRET: crypto.randomBytes(48).toString('base64url'),
    LAB_DPOP_CLIENT_SECRET: crypto.randomBytes(48).toString('base64url'),
    LAB_TENANT_ID: ownership['tenant-lab'],
    LAB_TLS_CERT_FILE: path.join(local, 'lab.crt'),
    LAB_TLS_KEY_FILE: path.join(local, 'lab.key'),
    EVIDENCE_PUBLISHER_KEY: crypto.randomBytes(48).toString('base64url'),
    DEMO_CONSOLE_PORT: '5080',
    DEMO_CONSOLE_HOST: `democonsole.${suffix}`,
    PARTNERS_TENANT_ID: ownership['tenant-partners'],
    WAREHOUSE_CLIENT_ID: ownership['client-warehouse'],
  };
  for (const name of ['supplier', 'finance', 'fulfilment', 'treasury']) {
    values[name.toUpperCase() + '_HOST'] = `${name}.${suffix}`;
    values[name.toUpperCase() + '_ORIGIN'] = `https://${name}.${suffix}${port ? ':' + port : ''}`;
  }
  for (const key of [
    'supplier',
    'finance',
    'procurement-exchange',
    'treasury-reader',
    'dispatch',
    'reconciliation',
  ]) {
    const prefix = key.toUpperCase().replaceAll('-', '_');
    values[prefix + '_CLIENT_ID'] = ownership['client-' + key];
    if (key !== 'finance')
      values[prefix + '_CLIENT_SECRET'] = crypto.randomBytes(48).toString('base64url');
  }
  values.CONTROL_RESULTS_FILE = path.join(local, 'evidence/control-comparisons.json');
  values.PROTOCOL_ISSUER = env.PROAUTH_ISSUER.replace(/\/$/, '') + '/mervikon-protocols';
  values.PROTOCOL_HOST = `protocols.${suffix}`;
  values.PROTOCOL_ORIGIN = `https://protocols.${suffix}${port ? ':' + port : ''}`;
  values.PROTOCOL_TENANT_ID = ownership['tenant-protocols'];
  for (const kind of ['jwt', 'opaque', 'jwe', 'mtls', 'introspection']) {
    values[`PROTOCOL_${kind.toUpperCase()}_CLIENT_ID`] = ownership['client-protocol-' + kind];
    if (kind !== 'mtls')
      values[`PROTOCOL_${kind.toUpperCase()}_SECRET`] = crypto
        .randomBytes(48)
        .toString('base64url');
  }
  values.CONFERENCE_TENANT_ID = ownership['tenant-conference'];
  for (const mode of ['spa', 'bff', 'nonce']) {
    values[`CONFERENCE_${mode.toUpperCase()}_CLIENT_ID`] = ownership['client-conference-' + mode];
    if (mode !== 'spa')
      values[`CONFERENCE_${mode.toUpperCase()}_SECRET`] = crypto
        .randomBytes(48)
        .toString('base64url');
  }
  for (const role of ['spa', 'bff', 'api', 'wrong', 'attacker']) {
    values[`CONFERENCE_${role.toUpperCase()}_HOST`] = `securitylab-${role}.${suffix}`;
    values[`CONFERENCE_${role.toUpperCase()}_ORIGIN`] =
      `https://securitylab-${role}.${suffix}${port ? ':' + port : ''}`;
  }
  return Object.fromEntries(Object.entries(values).filter(([k]) => !env[k]));
}
function initCustomer() {
  if (fs.existsSync(envFile))
    fail('Environment already exists. Use that file for the remaining steps.');
  const template = read(path.join(root, 'config/environments/local.env.example'));
  const env = {
    COMPOSE_PROJECT_NAME: template.COMPOSE_PROJECT_NAME,
    CLI_MODE: 'container',
    WORKPLACE_HOST: template.WORKPLACE_HOST,
    WORKPLACE_ORIGIN: template.WORKPLACE_ORIGIN,
    PROCUREMENT_HOST: template.PROCUREMENT_HOST,
    PROCUREMENT_ORIGIN: template.PROCUREMENT_ORIGIN,
    BOOTSTRAP_CLIENT_ID: template.BOOTSTRAP_CLIENT_ID,
    PROXY_MODE: 'standalone',
    MERVIKON_EDGE_NETWORK: 'mervikon-edge',
    MERVIKON_EDGE_SUBNET: '172.31.251.0/24',
    TRUSTED_PROXY_IP: '172.31.251.2',
    PROAUTH_HOST: 'https://YOUR-PROAUTH-HOST',
    PROAUTH_ISSUER: 'https://YOUR-PROAUTH-HOST',
    WORKPLACE_CLIENT_ID: ownership['client-workplace'],
    WORKFORCE_TENANT_ID: ownership['tenant-workforce'],
    WORKPLACE_CLIENT_SECRET: crypto.randomBytes(48).toString('base64url'),
    INITIAL_USER_PASSWORD: 'Ref!' + crypto.randomBytes(24).toString('base64url') + 'a1',
    BOOTSTRAP_ENV_FILE: path.join(local, 'bootstrap.env'),
    BOOTSTRAP_SECRET_KEY: 'BOOTSTRAP_SECRET',
    ENTITLEMENT_DIR: path.join(local, 'entitlements'),
  };
  for (const k of [
    'TRUSTED_CA_FILE',
    'TLS_CERT_FILE',
    'TLS_KEY_FILE',
    'MERVIKON_MANUFACTURING_CONNECTION_STRING',
    'MERVIKON_RESEARCH_CONNECTION_STRING',
    'MERVIKON_PARTNERS_CONNECTION_STRING',
  ])
    env[k] = template[k];
  fs.mkdirSync(local, { recursive: true, mode: 0o700 });
  if (!fs.existsSync(env.BOOTSTRAP_ENV_FILE))
    fs.writeFileSync(
      env.BOOTSTRAP_ENV_FILE,
      'BOOTSTRAP_SECRET=REPLACE_WITH_PROVISIONING_SECRET\n',
      { flag: 'wx', mode: 0o600 },
    );
  const feedPath = path.join(root, '.local/nuget.customer.config');
  if (!fs.existsSync(feedPath)) {
    fs.mkdirSync(path.dirname(feedPath), { recursive: true, mode: 0o700 });
    const config = fs.readFileSync(path.join(root, 'NuGet.Config'), 'utf8').replace(
      '</configuration>',
      `  <packageSourceCredentials>
    <ProAuthFeed>
      <add key="Username" value="YOUR_CUSTOMER_FEED_USERNAME" />
      <add key="ClearTextPassword" value="YOUR_CUSTOMER_FEED_PASSWORD" />
    </ProAuthFeed>
  </packageSourceCredentials>
</configuration>`,
    );
    fs.writeFileSync(feedPath, config, { flag: 'wx', mode: 0o600 });
  }
  fs.writeFileSync(
    envFile,
    Object.entries(env)
      .map(([k, v]) => k + '=' + JSON.stringify(v))
      .join('\n') + '\n',
    { flag: 'wx', mode: 0o600 },
  );
  console.log(
    'Created the customer environment. Complete the installation settings in docs/running.md, then run npm start.',
  );
}
function readDemoSubjects(env) {
  const secret = Object.entries(read(env.BOOTSTRAP_ENV_FILE)).find(
    ([k]) => k.toLowerCase() === env.BOOTSTRAP_SECRET_KEY.toLowerCase(),
  )?.[1];
  if (!secret || /[\r\n]/.test(secret))
    fail('Set the single-line provisioning credential in BOOTSTRAP_ENV_FILE.');
  const issuer = env.PROAUTH_ISSUER;
  const runtime = {
    Authentication__Authority: issuer,
    Authentication__ClientId: env.BOOTSTRAP_CLIENT_ID,
    Authentication__TenantId: '00000000-0000-0000-0000-000000000001',
    Authentication__ClientAuthenticationMethod: 'ClientSecretBasic',
    Authentication__SenderConstrainedTokenMode: 'Dpop',
    Authentication__Dpop__GenerateEphemeralKeyWhenMissing: 'true',
    Authentication__Dpop__SigningAlgorithm: 'PS256',
  };
  const lookup = spawnSync(
    'docker',
    [
      'run',
      '--rm',
      '-i',
      '--read-only',
      '--tmpfs',
      '/tmp',
      '--network',
      env.PROXY_MODE === 'standalone' ? 'bridge' : env.MERVIKON_EDGE_NETWORK,
      '--mount',
      `type=bind,src=${root}/config,dst=/workspace/config,readonly`,
      '--mount',
      `type=bind,src=${env.TRUSTED_CA_FILE},dst=/trust/ca.pem,readonly`,
      '-e',
      'SSL_CERT_FILE=/trust/ca.pem',
      ...Object.keys(runtime).flatMap((k) => ['-e', k]),
      '--entrypoint',
      'dotnet',
      cliImage,
      '/setup/Demo.MembershipSetup.dll',
    ],
    {
      cwd: root,
      env: { ...process.env, ...runtime },
      input: secret,
      encoding: 'utf8',
      timeout: 140000,
    },
  );
  if (lookup.status !== 0)
    fail(
      'Demo membership lookup failed. ' +
        (lookup.stderr || 'Rebuild the CLI image and verify management access.').trim(),
    );
  return JSON.parse(lookup.stdout);
}
function setupDemoMemberships(env, restoreMissing = false) {
  const fixture = (name) =>
    JSON.parse(fs.readFileSync(path.join(root, 'config/business', name), 'utf8'));
  publishDemoAssignments(
    env.ENTITLEMENT_DIR,
    (current) =>
      demoAssignments(
        current,
        fixture('demo-members.json'),
        readDemoSubjects(env),
        ownership,
        fixture('workload-assignments.example.json').Workloads,
        env.PROAUTH_ISSUER,
      ),
    { restoreMissing },
  );
  console.log('Demo memberships and workload assignments are ready. Reload the application.');
}

try {
  process.umask(0o077);
  if (command === 'init-customer') initCustomer();
  else if (command === 'check') check(fs.existsSync(envFile) ? read(envFile) : undefined);
  else {
    const env = read(envFile);
    if (command === 'setup-memberships') setupDemoMemberships(env, true);
    else if (command === 'configure-workflows') {
      const additions = workflowSettings(env);
      privateWrite(
        envFile,
        fs.readFileSync(envFile, 'utf8') +
          Object.entries(additions)
            .map(([k, v]) => `${k}='${v}'\n`)
            .join(''),
      );
      const resultFile = additions.LAB_RESULTS_FILE || env.LAB_RESULTS_FILE;
      if (!fs.existsSync(resultFile)) {
        privateWrite(resultFile, '[]\n');
        fs.chmodSync(resultFile, 0o644);
      }
      writeControlComparisons({ ...env, ...additions });
      console.log(
        'Added missing workflow settings without rotating existing credentials. Review hosts before provisioning.',
      );
    } else if (command === 'configure-protocols') {
      const keyFile = path.join(local, 'protocol-mtls.json');
      if (!fs.existsSync(keyFile))
        run(
          'docker',
          [
            'run',
            '--rm',
            '--network',
            'none',
            '--mount',
            `type=bind,src=${local},dst=/state`,
            cliImage,
            'crypto',
            'client-assertion-cert',
            'generate',
            '--subject',
            'CN=mervikon-protocol-client',
            '--kid',
            'mervikon-protocol-mtls',
            '--algorithm',
            'rsa',
            '--format',
            'json',
            '--out',
            '/state/protocol-mtls.json',
            '--configurationlocation',
            '/state/crypto',
          ],
          env,
          'crypto-protocol-mtls.log',
        );
      fs.chmodSync(keyFile, 0o600);
      const key = JSON.parse(fs.readFileSync(keyFile));
      for (const [name, value] of Object.entries({
        PROTOCOL_MTLS_CERTIFICATE: key.base64Pkcs12,
        PROTOCOL_MTLS_PASSWORD: key.password,
        PROTOCOL_MTLS_PUBLIC_JWKS: key.publicJwksJson,
      })) {
        if (!value || /['\r\n]/.test(value)) fail('Invalid certificate output.');
        if (!env[name]) fs.appendFileSync(envFile, `${name}='${value}'\n`);
        else if (env[name] !== value)
          fail('Existing protocol certificate differs; no automatic rotation.');
      }
      const encryptionFile = path.join(local, 'protocol-encryption.json');
      if (!fs.existsSync(encryptionFile)) {
        const pair = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
        const metadata = { kid: 'mervikon-protocol-encryption', use: 'enc', alg: 'RSA-OAEP' };
        privateWrite(
          encryptionFile,
          JSON.stringify({
            privateKey: { ...pair.privateKey.export({ format: 'jwk' }), ...metadata },
            publicKey: { ...pair.publicKey.export({ format: 'jwk' }), ...metadata },
          }),
        );
      }
      fs.chmodSync(encryptionFile, 0o600);
      const encryption = JSON.parse(fs.readFileSync(encryptionFile));
      for (const [name, value] of Object.entries({
        PROTOCOL_JWE_PRIVATE_JWK: JSON.stringify(encryption.privateKey),
        PROTOCOL_JWE_PUBLIC_JWKS: JSON.stringify({ keys: [encryption.publicKey] }),
      })) {
        if (!env[name]) fs.appendFileSync(envFile, `${name}='${value}'\n`);
        else if (env[name] !== value)
          fail('Existing protocol encryption key differs; no automatic rotation.');
      }
      console.log('Stable separate mTLS and token-encryption keys configured.');
    } else if (command === 'configure-lab') {
      if (fs.existsSync(env.LAB_TLS_CERT_FILE) !== fs.existsSync(env.LAB_TLS_KEY_FILE))
        fail('Incomplete lab certificate pair; no automatic replacement.');
      if (!fs.existsSync(env.LAB_TLS_KEY_FILE))
        run(
          'mkcert',
          [
            '-cert-file',
            env.LAB_TLS_CERT_FILE,
            '-key-file',
            env.LAB_TLS_KEY_FILE,
            'lab-api.localtest.me',
          ],
          env,
          'lab-certificate.log',
        );
      fs.chmodSync(env.LAB_TLS_KEY_FILE, 0o600);
      for (const [key, file] of [
        ['LAB_TLS_CERTIFICATE_PEM', env.LAB_TLS_CERT_FILE],
        ['LAB_TLS_PRIVATE_KEY_PEM', env.LAB_TLS_KEY_FILE],
      ]) {
        const value = fs.readFileSync(file).toString('base64');
        if (env[key] && env[key] !== value)
          fail('Existing lab TLS value differs; no automatic rotation.');
        if (!env[key]) fs.appendFileSync(envFile, `${key}='${value}'\n`);
      }
      console.log('Separate lab TLS identity configured; private key remains restricted.');
    } else if (command === 'lab-run') {
      env.CONFERENCE_RUN_ID = crypto.randomUUID();
      console.log(
        `Live sender comparison run ${env.CONFERENCE_RUN_ID}; inspect Protocol Evidence while cleanup runs.`,
      );
      const lock = path.join(local, 'lab-run.lock');
      let descriptor;
      try {
        descriptor = fs.openSync(lock, 'wx', 0o600);
      } catch {
        fail('A lab run is active or its lock needs operator recovery.');
      }
      const compose = [
        'compose',
        '--env-file',
        envFile,
        '-f',
        path.join(root, 'deploy/compose/compose.yml'),
        '-f',
        path.join(root, 'deploy/compose/lab.yml'),
      ];
      const services = ['lab-egress', 'lab-api', 'lab-capture', 'lab-runner'];
      try {
        check(env);
        run('docker', [...compose, 'up', '-d', ...services], env, 'lab-start.log');
        const observations = [];
        for (const variant of ['bearer', 'dpop']) {
          const output = run(
            'docker',
            [...compose, 'run', '--rm', `lab-victim-${variant}`],
            env,
            `lab-${variant}.log`,
          );
          if (
            !output.includes(
              'Cleanup verified: expired token rejected and ephemeral captures removed.',
            )
          )
            fail('Lab cleanup not verified.');
          const raw = JSON.parse(
            output.split('\n').find((line) => line.startsWith('{"scenario":"SCN-11"')) || '{}',
          );
          const statuses = [raw.legitimateBefore, raw.TokenOnly, raw.legitimateAfter];
          if (
            raw.variant !== variant ||
            statuses.some((s) => !Number.isInteger(s) || s < 100 || s > 599) ||
            (raw.CapturedProof !== null &&
              (!Number.isInteger(raw.CapturedProof) ||
                raw.CapturedProof < 100 ||
                raw.CapturedProof > 599))
          )
            fail('Invalid lab observation.');
          const proofControls = raw.proofControls
            ? Object.fromEntries(
                ['WrongMethod', 'WrongUri', 'WrongHash', 'WrongKey'].map((k) => [
                  k[0].toLowerCase() + k.slice(1),
                  raw.proofControls[k],
                ]),
              )
            : null;
          if (
            variant === 'dpop' &&
            (!proofControls || Object.values(proofControls).some((s) => s !== 401))
          )
            fail('DPoP negative controls incomplete.');
          observations.push({
            variant,
            proofControls,
            observedAt: new Date(raw.observedAt).toISOString(),
            legitimateBefore: raw.legitimateBefore,
            tokenOnly: raw.TokenOnly,
            capturedProof: raw.CapturedProof,
            legitimateAfter: raw.legitimateAfter,
            expiredToken: 401,
            cleanupVerified: true,
          });
          console.log(
            `${variant}: legitimate ${raw.legitimateBefore}, stolen token ${raw.TokenOnly}, copied proof ${raw.CapturedProof ?? 'not applicable'}, fresh legitimate ${raw.legitimateAfter}; expiry and capture cleanup verified.`,
          );
        }
        privateWrite(env.LAB_RESULTS_FILE, JSON.stringify(observations, null, 2) + '\n');
        fs.chmodSync(env.LAB_RESULTS_FILE, 0o644);
      } finally {
        // Stops only the isolated lab. Normal applications, identity state and shared proxy are untouched.
        run('docker', [...compose, 'stop', ...services], env, 'lab-stop.log');
        fs.closeSync(descriptor);
        fs.unlinkSync(lock);
      }
    } else if (command === 'replica-run') {
      const lock = path.join(local, 'lab-run.lock');
      let descriptor;
      try {
        descriptor = fs.openSync(lock, 'wx', 0o600);
      } catch {
        fail('A lab run is active or its lock needs operator recovery.');
      }
      const compose = [
        'compose',
        '--env-file',
        envFile,
        '-f',
        'deploy/compose/compose.yml',
        '-f',
        'deploy/compose/lab.yml',
        '-f',
        'deploy/compose/replicas.yml',
      ];
      const services = [
        'lab-egress',
        'lab-api',
        'lab-api-replica',
        'lab-capture',
        'lab-replay-redis',
      ];
      const container = `mervikon-replica-probe-${crypto.randomUUID()}`;
      try {
        run('docker', [...compose, 'up', '-d', ...services], env, 'replica-start.log');
        const output = await runReplicaProbe(compose, container, env);
        if (
          !output.includes(
            'Cleanup verified: expired token rejected and ephemeral captures removed.',
          )
        )
          fail('Replica cleanup not verified.');
        const raw = JSON.parse(
          output.split('\n').find((line) => line.startsWith('{"scenario":"SCN-22"')) || '{}',
        );
        if (
          raw.first !== 200 ||
          raw.replay !== 401 ||
          raw.freshOne !== 200 ||
          raw.freshTwo !== 200 ||
          raw.accepted !== 1 ||
          raw.rejected !== 15 ||
          raw.instancesVerified !== true ||
          raw.outageOne !== 503 ||
          raw.outageTwo !== 503 ||
          raw.recoveredOne !== 200 ||
          raw.recoveredTwo !== 200 ||
          raw.replayAfterRestart !== 401
        )
          fail('Replica evidence incomplete.');
        const result = {
          scenario: 'SCN-22',
          observedAt: new Date(raw.observedAt).toISOString(),
          first: raw.first,
          replay: raw.replay,
          freshOne: raw.freshOne,
          freshTwo: raw.freshTwo,
          concurrentRequests: 16,
          accepted: raw.accepted,
          rejected: raw.rejected,
          outageOne: raw.outageOne,
          outageTwo: raw.outageTwo,
          recoveredOne: raw.recoveredOne,
          recoveredTwo: raw.recoveredTwo,
          replayAfterRestart: raw.replayAfterRestart,
          instancesVerified: true,
          cleanupVerified: true,
        };
        privateWrite(
          path.join(local, 'evidence/replicas.json'),
          JSON.stringify(result, null, 2) + '\n',
        );
        writeControlComparisons(env);
        console.log(JSON.stringify(result));
      } finally {
        spawnSync('docker', ['rm', '-f', container], { stdio: 'ignore', timeout: 10000 });
        try {
          run('docker', [...compose, 'stop', ...services], env, 'replica-stop.log');
        } finally {
          fs.closeSync(descriptor);
          fs.unlinkSync(lock);
        }
      }
    } else if (command === 'generate-keys') {
      generateKeys({ env, local, cliImage, envFile, run });
    } else if (command === 'build') {
      buildImages({
        root,
        env,
        run,
        service: option('--service'),
        includeLabs: args.includes('--lab'),
      });
    } else if (command === 'protocol-run') {
      if (env.PROXY_MODE !== 'shared')
        fail('Protocol controls currently require the shared local ProAuth proxy.');
      const variant = option('--variant', 'jwt');
      if (!['jwt', 'opaque', 'jwe', 'mtls'].includes(variant)) fail('Unknown protocol variant.');
      await waitForProtocolApi(env);
      const runtime = {
        ...env,
        PROTOCOL_VARIANT: variant,
        PROTOCOL_SELECTED_CLIENT_ID: env[`PROTOCOL_${variant.toUpperCase()}_CLIENT_ID`],
        PROTOCOL_SELECTED_SECRET:
          variant === 'mtls' ? '' : env[`PROTOCOL_${variant.toUpperCase()}_SECRET`],
        PROTOCOL_AUTH_METHOD: variant === 'mtls' ? 'SelfSignedTlsClientAuth' : 'ClientSecretBasic',
        PROTOCOL_BINDING: variant === 'mtls' ? 'MutualTls' : 'None',
        PROTOCOL_SELECTED_CERTIFICATE: variant === 'mtls' ? env.PROTOCOL_MTLS_CERTIFICATE : '',
        PROTOCOL_SELECTED_PASSWORD: variant === 'mtls' ? env.PROTOCOL_MTLS_PASSWORD : '',
      };
      const container = `mervikon-protocol-probe-${crypto.randomUUID()}`;
      let output;
      try {
        output = run(
          'docker',
          [
            'compose',
            '--env-file',
            envFile,
            '-f',
            'deploy/compose/compose.yml',
            '-f',
            'deploy/compose/protocols.yml',
            'run',
            '--rm',
            '--name',
            container,
            'protocol-probe',
          ],
          runtime,
          `protocol-${variant}.log`,
          120000,
        );
      } finally {
        spawnSync('docker', ['rm', '-f', container], { stdio: 'ignore', timeout: 10000 });
      }
      const observation = JSON.parse(
        output.split('\n').find((l) => l.startsWith('{"variant":')) || '{}',
      );
      if (
        observation.variant !== variant ||
        observation.legitimate !== 200 ||
        observation.expired !== 401
      )
        fail('Incomplete protocol observation.');
      privateWrite(
        path.join(local, `evidence/protocol-${variant}.json`),
        JSON.stringify(observation, null, 2) + '\n',
      );
      writeControlComparisons(env);
      console.log(JSON.stringify(observation));
    } else if (command === 'build-cli') {
      run(
        'docker',
        [
          'build',
          '-f',
          'deploy/compose/Cli.Dockerfile',
          '--build-arg',
          `PROAUTH_CLI_VERSION=${productBuild.version}`,
          '--secret',
          `id=proauthfeed,src=${path.join(root, '.local/nuget.customer.config')}`,
          '-t',
          cliImage,
          '.',
        ],
        env,
      );
    } else if (
      command === 'parse' ||
      command === 'provision' ||
      command === 'provision-branding' ||
      command === 'provision-approver-group'
    ) {
      const files =
        command === 'provision-approver-group'
          ? [path.join(root, 'config/proauth/optional-groups/45-approvers.yml')]
          : command === 'provision-branding'
            ? brandingManifestFiles(root)
            : check(env);
      validateOwnership(loadManifests(files), ownership);
      const secretEnv = read(env.BOOTSTRAP_ENV_FILE);
      const secret = Object.entries(secretEnv).find(
        ([k]) => k.toLowerCase() === env.BOOTSTRAP_SECRET_KEY.toLowerCase(),
      )?.[1];
      if (!secret) fail('Bootstrap credential is unavailable in the configured secret file.');
      cli(
        env,
        [
          'loginservice',
          '--host',
          env.PROAUTH_HOST,
          '--clientid',
          env.BOOTSTRAP_CLIENT_ID,
          '--dpop',
        ],
        'cli-login.log',
        ['--clientsecret', secret],
      );
      cli(env, ['data', 'parse', ...files], 'cli-parse.log');
      console.log('ProAuth CLI parse passed.');
      if (
        command === 'provision' ||
        command === 'provision-branding' ||
        command === 'provision-approver-group'
      ) {
        cli(
          env,
          [
            'data',
            'import',
            ...files,
            '--waitForUserStoresOperational',
            'true',
            ...(command === 'provision' ? ['--userDefaultPasswordAsInitial', 'true'] : []),
          ],
          'cli-import.log',
          command === 'provision'
            ? ['--userDefaultPassword', env.INITIAL_USER_PASSWORD]
            : undefined,
        );
        console.log('Mervikon resources imported.');
      }
      if (command === 'provision') setupDemoMemberships(env);
    } else if (command === 'compose') {
      const split = args.indexOf('--');
      if (split < 0) fail('Put Docker Compose arguments after --.');
      const files = ['-f', path.join(root, 'deploy/compose/compose.yml')];
      if (args.includes('--group-approvals'))
        files.push('-f', path.join(root, 'deploy/compose/group-approvals.yml'));
      if (args.includes('--conference'))
        files.push('-f', path.join(root, 'deploy/compose/conference.yml'));
      if (args.includes('--lab')) files.push('-f', path.join(root, 'deploy/compose/lab.yml'));
      if (args.includes('--federation'))
        fail(
          'Federation is postponed pending the ProAuth upstream integration. No federation Compose profile is available.',
        );
      if (args.includes('--protocols'))
        files.push('-f', path.join(root, 'deploy/compose/protocols.yml'));
      if (args.includes('--bff-replicas')) {
        if (env.PROXY_MODE !== 'shared')
          fail('BFF replica controls currently require the shared local proxy.');
        files.push('-f', path.join(root, 'deploy/compose/bff-replicas.yml'));
      }
      if (env.PROXY_MODE === 'standalone')
        files.push('-f', path.join(root, 'deploy/compose/standalone.yml'));
      else if (env.PROXY_MODE !== 'shared') fail('PROXY_MODE must be shared or standalone.');
      const result = spawnSync(
        'docker',
        ['compose', '--env-file', envFile, ...files, ...args.slice(split + 1)],
        { cwd: root, env: { ...process.env, ...env }, stdio: 'inherit' },
      );
      if (result.status !== 0) fail('Docker Compose command failed.');
    } else if (command === 'preflight') {
      if (env.PROXY_MODE === 'shared') {
        const proxy = spawnSync(
          'docker',
          [
            'inspect',
            '--format',
            '{{json .NetworkSettings.Networks}}',
            env.SHARED_PROXY_CONTAINER || 'proxy',
          ],
          { encoding: 'utf8' },
        );
        if (proxy.status !== 0)
          fail(
            'Cannot inspect the shared reverse proxy. Set SHARED_PROXY_CONTAINER to its container name.',
          );
        const address = JSON.parse(proxy.stdout)[env.MERVIKON_EDGE_NETWORK]?.IPAddress;
        if (!address || address !== env.TRUSTED_PROXY_IP)
          fail(
            'Trusted proxy address differs from the running shared proxy. Update TRUSTED_PROXY_IP and recreate the affected Mervikon services before sign-in.',
          );
      }
      const ca = [
        ...tls.rootCertificates,
        ...(env.TRUSTED_CA_FILE ? [fs.readFileSync(env.TRUSTED_CA_FILE)] : []),
      ];
      const discovery = await new Promise((resolve, reject) => {
        const request = https.get(
          `${env.PROAUTH_HOST.replace(/\/$/, '')}/.well-known/openid-configuration`,
          { ca, timeout: 10000 },
          (response) => {
            if (response.statusCode !== 200) {
              response.resume();
              reject(new Error(`Discovery HTTP ${response.statusCode}`));
              return;
            }
            let body = '';
            response.on('data', (c) => {
              body += c;
              if (body.length > 1024 * 1024) request.destroy(new Error('Discovery too large'));
            });
            response.on('end', () => {
              try {
                resolve(JSON.parse(body));
              } catch {
                reject(new Error('Invalid discovery JSON'));
              }
            });
          },
        );
        request.on('timeout', () => request.destroy(new Error('Discovery timed out')));
        request.on('error', reject);
      });
      if (discovery.issuer !== env.PROAUTH_ISSUER)
        fail(`Canonical issuer mismatch. Set PROAUTH_ISSUER to ${discovery.issuer}`);
      for (const key of [
        'authorization_endpoint',
        'token_endpoint',
        'jwks_uri',
        'pushed_authorization_request_endpoint',
      ])
        if (!discovery[key]?.startsWith('https://'))
          fail(`Required HTTPS metadata missing: ${key}`);
      privateWrite(
        path.join(local, 'evidence/discovery.json'),
        JSON.stringify(
          {
            observedAt: new Date().toISOString(),
            issuer: discovery.issuer,
            tlsVerified: true,
            endpoints: Object.fromEntries(
              [
                'authorization_endpoint',
                'token_endpoint',
                'jwks_uri',
                'pushed_authorization_request_endpoint',
              ].map((k) => [k, discovery[k]]),
            ),
          },
          null,
          2,
        ),
      );
      console.log(
        'Discovery, canonical issuer and TLS verified. This is not a login or FAPI conformance test.',
      );
    } else
      fail(
        'Commands: init-customer, setup-memberships, configure-workflows, configure-lab, configure-protocols, lab-run, replica-run, protocol-run, check, build-cli, generate-keys, parse, provision, provision-branding, provision-approver-group, build, preflight, compose. Select settings with --env-file; optional manifests: --lab --protocols. Federation is postponed.',
      );
  }
} catch (e) {
  console.error(e.message);
  process.exitCode = 1;
}
