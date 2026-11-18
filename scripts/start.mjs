import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { parseEnv } from 'node:util';

const root = fileURLToPath(new URL('../', import.meta.url));
const args = process.argv.slice(2);
if (args.length && (args.length !== 2 || args[0] !== '--env-file' || !args[1])) {
  console.error('Usage: npm start -- [--env-file PATH]');
  process.exit(1);
}
const file = path.resolve(args[1] || path.join(root, '.local/local.env'));
const settings = ['--env-file', file];
function run(command, argv) {
  const result = spawnSync(command, argv, { cwd: root, stdio: 'inherit' });
  if (result.error) console.error(result.error.message);
  if (result.status !== 0) process.exit(result.status ?? 1);
}
function runMervikon(step, ...extra) {
  console.log(`Running ${step}`);
  run(process.execPath, ['--', 'scripts/mervikon.mjs', step, ...settings, ...extra]);
}

// Use the npm CLI that launched this script, including on Windows.
if (process.env.npm_execpath)
  run(process.execPath, [process.env.npm_execpath, 'ci', '--ignore-scripts']);
else run('npm', ['ci', '--ignore-scripts']);
if (!fs.existsSync(file)) {
  runMervikon('init-customer');
  console.log(
    `Complete ${file}, the bootstrap credential and customer feed settings as described in docs/running.md. Then run this command again.`,
  );
  process.exit(0);
}
const env = parseEnv(fs.readFileSync(file, 'utf8'));
if (env.PROAUTH_HOST?.includes('YOUR-PROAUTH-HOST')) {
  console.error(
    `Complete the installation settings in ${file} before starting. See docs/running.md.`,
  );
  process.exit(1);
}
for (const step of [
  'configure-workflows',
  'preflight',
  'build-cli',
  'generate-keys',
  'check',
  'provision',
  'build',
])
  runMervikon(step);
runMervikon('compose', '--', '--profile', 'demo', 'up', '-d', '--no-build');
run(process.execPath, ['--', 'scripts/smoke.mjs', ...settings]);
const configured = parseEnv(fs.readFileSync(file, 'utf8'));
const consoleUrl = new URL(configured.WORKPLACE_ORIGIN);
consoleUrl.hostname = configured.DEMO_CONSOLE_HOST || 'democonsole.localtest.me';
console.log(
  `Workplace: ${configured.WORKPLACE_ORIGIN}\nDemo Console: ${consoleUrl.origin}\nFollow docs/walkthrough-local.md to make your first purchase.`,
);
