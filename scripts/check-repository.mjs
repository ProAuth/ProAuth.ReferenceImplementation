import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
// Include new files while reviewing a working tree; ignore pending deletions.
const files = [
  ...new Set(
    execFileSync('git', ['ls-files', '-z', '--cached', '--others', '--exclude-standard'], {
      cwd: root,
      encoding: 'utf8',
    })
      .split('\0')
      .filter((file) => file && fs.existsSync(path.join(root, file))),
  ),
];
const errors = [];
const privatePath =
  /^(?:\.local\/|\.demoautomation\/|docs\/(?:verification\/|implementation-|product-roadmap|product-integration-|research-notes|backlog-proposals|assurance-investigation|presentation-routes)|scripts\/(?:build-product-packages|prepare-package-feed)\.py$)/;
const generatedPath =
  /(?:^|\/)(?:node_modules|bin|obj|\.DS_Store)(?:\/|$)|^src\/(?:apps\/[^/]+\.Bff|demo-console\/DemoConsole)\/wwwroot\//;
for (const file of files) {
  if (privatePath.test(file) || generatedPath.test(file))
    errors.push(`${file}: private or generated content`);
  if (!/\.(?:md|json|cs|csproj|mjs|ts|tsx|ya?ml|html|css|props|example)$/.test(file)) continue;
  const text = fs.readFileSync(path.join(root, file), 'utf8');
  if (
    /https:\/\/dev\.azure\.com\/|\/Users\/[^/\s]+\/|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----/.test(
      text,
    )
  ) {
    errors.push(`${file}: private location, tracker link, or key material`);
  }
  if (!file.endsWith('.md')) continue;
  for (const match of text.matchAll(/\[[^\]]*\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g)) {
    const target = match[1].split('#')[0];
    if (!target || /^[a-z][a-z\d+.-]*:/i.test(target)) continue;
    if (!fs.existsSync(path.resolve(root, path.dirname(file), decodeURIComponent(target)))) {
      errors.push(`${file}: broken link to ${target}`);
    }
  }
}
const pin = JSON.parse(fs.readFileSync(path.join(root, 'config/product-build.json'), 'utf8'));
const registry = fs.readFileSync(path.join(root, 'docs/scenarios/registry.md'), 'utf8');
const scenarioIds = new Set([...registry.matchAll(/^\| (SCN-\d+) \|/gm)].map((match) => match[1]));
const consoleSource = fs.readFileSync(path.join(root, 'src/apps/DemoConsole/src/main.tsx'), 'utf8');
for (const match of consoleSource.matchAll(/SCN-\d+/g)) {
  if (!scenarioIds.has(match[0]))
    errors.push(`Demo Console references missing scenario ${match[0]}`);
}
const props = fs.readFileSync(path.join(root, 'Directory.Build.props'), 'utf8');
if (!props.includes(`<ProAuthPackageVersion>${pin.version}</ProAuthPackageVersion>`)) {
  errors.push(
    'ProAuth package version differs between config/product-build.json and Directory.Build.props',
  );
}
if (errors.length) {
  console.error(errors.join('\n'));
  process.exitCode = 1;
} else {
  console.log(
    `Repository boundaries, documentation links, and package pin checked (${files.length} files).`,
  );
}
