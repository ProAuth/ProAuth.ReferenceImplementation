import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawn } from 'node:child_process';
import { readEnvironment } from './configuration.mjs';

// Explicit local operator process. No Docker socket, command endpoint or raw token is added to Demo Console.
const root = path.resolve(import.meta.dirname, '..');
const args = process.argv.slice(2);
const option = (key, fallback) => (args.includes(key) ? args[args.indexOf(key) + 1] : fallback);
const envFile = path.resolve(option('--env-file', path.join(root, '.local/local.env')));
const env = { ...process.env, ...readEnvironment(envFile) };
const port = Number(option('--port', '5091'));
if (!Number.isInteger(port) || port < 1024 || port > 65535) throw Error('Invalid local port');
const origin = `http://127.0.0.1:${port}`;
const state = {
  ready: false,
  variant: null,
  phase: 'starting',
  awaiting: null,
  events: [],
  error: null,
  revision: 0,
};
const publicRoot = path.join(root, 'labs/unsafe/sender-demo');
const token = crypto.randomBytes(32).toString('hex');
let child,
  pending = '',
  currentRun,
  closing = false;
const local = path.dirname(envFile);
fs.mkdirSync(path.join(local, 'logs'), { recursive: true, mode: 0o700 });
const lock = path.join(local, 'lab-run.lock');
const lockFd = fs.openSync(lock, 'wx', 0o600);
fs.writeFileSync(lockFd, String(process.pid));
const base = ['compose', '--env-file', envFile, '-f', 'deploy/compose/compose.yml'];
const lab = [...base, '-f', 'deploy/compose/lab.yml'];
const protocols = [...base, '-f', 'deploy/compose/protocols.yml'];
const services = ['lab-egress', 'lab-api', 'lab-capture', 'lab-runner'];
const changed = () => state.revision++;
async function docker(argv, name) {
  const fd = fs.openSync(path.join(local, 'logs', `sender-demo-${name}.log`), 'w', 0o600);
  try {
    await new Promise((resolve, reject) => {
      const p = spawn('docker', argv, { cwd: root, env, stdio: ['ignore', fd, fd] });
      p.once('error', reject);
      p.once('exit', (code) =>
        code === 0 ? resolve() : reject(Error(`Local ${name} failed; inspect private log.`)),
      );
    });
  } finally {
    fs.closeSync(fd);
  }
}
function safeObservation(value) {
  const allowed = [
    'runId',
    'observedAt',
    'source',
    'step',
    'httpStatus',
    'outcome',
    'token',
    'proof',
    'presentedCertificateThumbprint',
  ];
  const result = Object.fromEntries(
    allowed.filter((k) => value[k] != null).map((k) => [k, value[k]]),
  );
  if (
    !['legitimate', 'capture', 'replay', 'cleanup'].includes(value.step) ||
    typeof value.runId !== 'string' ||
    !Number.isFinite(Date.parse(value.observedAt))
  )
    throw Error('Invalid probe observation');
  // Known DTOs only. Never forward arbitrary child stdout, token responses or errors to the browser.
  for (const [key, fields] of Object.entries({
    token: [
      'fingerprint',
      'issuer',
      'audience',
      'scope',
      'subjectFingerprint',
      'actorFingerprint',
      'jkt',
      'certificateThumbprint',
      'expiresAt',
    ],
    proof: ['method', 'target', 'tokenHash', 'idFingerprint', 'noncePresent'],
  })) {
    if (!result[key]) continue;
    if (Object.keys(result[key]).some((k) => !fields.includes(k)))
      throw Error('Unexpected evidence field');
    if (
      Object.values(result[key]).some(
        (v) =>
          typeof v === 'string' && (v.length > 300 || /^[\w-]{16,}\.[\w-]{16,}\.[\w-]+$/.test(v)),
      )
    )
      throw Error('Unsafe evidence value');
  }
  return result;
}
function start(variant) {
  if (!state.ready || child || !['bearer', 'dpop', 'mtls'].includes(variant))
    throw Error('Another probe is active or variant is invalid');
  state.variant = variant;
  state.events = [];
  state.awaiting = null;
  state.error = null;
  state.phase = 'starting';
  changed();
  const runId = crypto.randomUUID();
  const runtime = { ...env, CONFERENCE_RUN_ID: runId };
  const container = `mervikon-sender-demo-${runId}`;
  let argv;
  if (variant === 'mtls') {
    Object.assign(runtime, {
      PROTOCOL_VARIANT: 'mtls',
      PROTOCOL_SELECTED_CLIENT_ID: env.PROTOCOL_MTLS_CLIENT_ID,
      PROTOCOL_SELECTED_SECRET: '',
      PROTOCOL_AUTH_METHOD: 'SelfSignedTlsClientAuth',
      PROTOCOL_BINDING: 'MutualTls',
      PROTOCOL_SELECTED_CERTIFICATE: env.PROTOCOL_MTLS_CERTIFICATE,
      PROTOCOL_SELECTED_PASSWORD: env.PROTOCOL_MTLS_PASSWORD,
    });
    argv = [
      ...protocols,
      'run',
      '--rm',
      '-T',
      '--name',
      container,
      '-e',
      'DemoMode=true',
      'protocol-probe',
    ];
  } else
    argv = [
      ...lab,
      'run',
      '--rm',
      '-T',
      '--name',
      container,
      '-e',
      'DemoMode=true',
      `lab-victim-${variant}`,
    ];
  const fd = fs.openSync(path.join(local, 'logs', `sender-demo-${variant}.log`), 'w', 0o600);
  child = spawn('docker', argv, { cwd: root, env: runtime, stdio: ['pipe', 'pipe', 'pipe'] });
  pending = '';
  let summary;
  child.stdout.on('data', (data) => {
    fs.writeSync(fd, data);
    pending += data;
    let end;
    while ((end = pending.indexOf('\n')) >= 0) {
      const line = pending.slice(0, end).trim();
      pending = pending.slice(end + 1);
      try {
        if (line.startsWith('{"variant":') || line.startsWith('{"scenario":"SCN-11"'))
          summary = JSON.parse(line);
        else if (line.startsWith('DEMO_WAIT:')) {
          state.awaiting = line.slice(10);
          state.phase = 'waiting';
          changed();
        } else if (line.startsWith('DEMO_EVENT:')) {
          state.events.push(safeObservation(JSON.parse(line.slice(11))));
          changed();
        } else if (line === 'DEMO_CLEANUP') {
          state.phase = 'cleanup';
          changed();
        }
      } catch {
        state.error = 'Probe evidence failed validation.';
        state.phase = 'failed';
        changed();
      }
    }
  });
  child.stderr.on('data', (data) => fs.writeSync(fd, data));
  currentRun = new Promise((resolve) => {
    let finished = false;
    const finish = (code) => {
      if (finished) return;
      finished = true;
      fs.closeSync(fd);
      child = undefined;
      state.awaiting = null;
      const cleaned = state.events.some((e) => e.step === 'cleanup' && e.httpStatus === 401);
      state.phase = code === 0 && cleaned && !state.error ? 'complete' : 'failed';
      if (state.phase === 'complete') {
        try {
          if (!summary) throw Error('Missing completed probe summary');
          if (variant === 'mtls') {
            const comparisons = fs.existsSync(env.CONTROL_RESULTS_FILE)
              ? JSON.parse(fs.readFileSync(env.CONTROL_RESULTS_FILE, 'utf8'))
              : { protocols: [], replicas: null };
            const keys = [
              'variant',
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
            ];
            const result = Object.fromEntries(keys.map((k) => [k, summary[k]]));
            comparisons.protocols = [
              ...comparisons.protocols.filter((p) => p.variant !== 'mtls'),
              result,
            ];
            fs.writeFileSync(
              env.CONTROL_RESULTS_FILE,
              JSON.stringify(comparisons, null, 2) + '\n',
              { mode: 0o644 },
            );
          } else {
            const previous = fs.existsSync(env.LAB_RESULTS_FILE)
              ? JSON.parse(fs.readFileSync(env.LAB_RESULTS_FILE, 'utf8'))
              : [];
            const controls = summary.proofControls
              ? Object.fromEntries(
                  Object.entries(summary.proofControls).map(([k, v]) => [
                    k[0].toLowerCase() + k.slice(1),
                    v,
                  ]),
                )
              : null;
            const result = {
              variant,
              observedAt: summary.observedAt,
              legitimateBefore: summary.legitimateBefore,
              tokenOnly: summary.TokenOnly,
              capturedProof: summary.CapturedProof,
              proofControls: controls,
              legitimateAfter: summary.legitimateAfter,
              expiredToken: 401,
              cleanupVerified: true,
            };
            fs.writeFileSync(
              env.LAB_RESULTS_FILE,
              JSON.stringify([...previous.filter((p) => p.variant !== variant), result], null, 2) +
                '\n',
              { mode: 0o644 },
            );
          }
        } catch {
          state.phase = 'failed';
          state.error = 'Completed result could not be saved.';
        }
      }
      if (state.phase === 'failed')
        state.error ||=
          'Probe or cleanup failed. Inspect the private operator log; no successful run is implied.';
      changed();
      resolve();
    };
    child.once('error', () => finish(-1));
    child.once('exit', finish);
  });
}
function respond(res, status, value) {
  res.writeHead(status, { 'Content-Type': 'application/json' });
  res.end(JSON.stringify(value));
}
const server = http.createServer(async (req, res) => {
  res.setHeader('Cache-Control', 'no-store');
  res.setHeader(
    'Content-Security-Policy',
    "default-src 'self'; script-src 'self'; style-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'",
  );
  res.setHeader('X-Content-Type-Options', 'nosniff');
  if (req.headers.host !== `127.0.0.1:${port}`)
    return respond(res, 403, { error: 'Local operator host required' });
  const route = req.url;
  if (req.method === 'GET' && route === '/api/state')
    return respond(res, 200, {
      ...state,
      consoleOrigin: 'https://' + (env.DEMO_CONSOLE_HOST || 'democonsole.localtest.me'),
      csrf: token,
    });
  if (req.method === 'POST') {
    if (
      req.headers.origin !== origin ||
      req.headers['x-sender-demo'] !== token ||
      req.headers['content-type'] !== 'application/json'
    )
      return respond(res, 403, { error: 'Same-origin operator request required' });
    let body = '';
    try {
      for await (const part of req) {
        body += part;
        if (body.length > 1024) {
          respond(res, 413, { error: 'Oversized command' });
          return;
        }
      }
      const value = JSON.parse(body);
      if (route === '/api/start') start(value.variant);
      else if (
        route === '/api/step' &&
        child &&
        state.phase === 'waiting' &&
        value.action === state.awaiting
      ) {
        state.awaiting = null;
        state.phase = 'running';
        changed();
        child.stdin.write(value.action + '\n');
      } else return respond(res, 409, { error: 'Action is not available' });
      return respond(res, 202, { accepted: true });
    } catch {
      return respond(res, 409, { error: 'Action could not start' });
    }
  }
  const asset = {
    '/': ['index.html', 'text/html'],
    '/app.js': ['app.js', 'text/javascript'],
    '/style.css': ['style.css', 'text/css'],
  }[route];
  if (req.method !== 'GET' || !asset) return respond(res, 404, { error: 'Not found' });
  res.writeHead(200, { 'Content-Type': asset[1] });
  res.end(fs.readFileSync(path.join(publicRoot, asset[0])));
});
async function shutdown() {
  if (closing) return;
  closing = true;
  state.ready = false;
  // Complete fixed outstanding actions and expiry/purge cleanup; never force-kill a probe.
  const drain = setInterval(() => {
    if (child && state.awaiting) {
      const action = state.awaiting;
      state.awaiting = null;
      child.stdin.write(action + '\n');
    }
  }, 200);
  try {
    await currentRun;
    await docker([...lab, 'stop', ...services], 'stop');
  } finally {
    clearInterval(drain);
    server.close();
    fs.closeSync(lockFd);
    fs.unlinkSync(lock);
  }
}
server.once('error', async () => {
  console.error('Local operator could not bind its loopback port.');
  await shutdown();
  process.exitCode = 1;
});
process.once('SIGINT', shutdown);
process.once('SIGTERM', shutdown);
try {
  await docker([...lab, 'up', '-d', ...services], 'start');
  state.ready = true;
  state.phase = 'idle';
  changed();
  server.listen(port, '127.0.0.1', () => console.log(`Sender Lab ready: ${origin}`));
} catch (error) {
  console.error(error.message);
  await shutdown();
  process.exitCode = 1;
}
