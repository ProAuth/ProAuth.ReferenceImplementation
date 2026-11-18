import fs from 'node:fs';
import path from 'node:path';

export function generateKeys({ env, local, cliImage, envFile, run }) {
  for (const app of ['workplace', 'supplier', 'finance']) {
    const keyFile = path.join(local, `${app}-protection.json`);
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
          'data-protection-cert',
          'generate',
          '--subject',
          `CN=mervikon-${app}`,
          '--format',
          'json',
          '--out',
          `/state/${app}-protection.json`,
          '--configurationlocation',
          '/state/crypto',
        ],
        env,
        `crypto-${app}.log`,
      );
    fs.chmodSync(keyFile, 0o600);
    const key = JSON.parse(fs.readFileSync(keyFile, 'utf8'));
    const prefix = app === 'workplace' ? '' : app.toUpperCase() + '_';
    const certificateKey = prefix + 'DATA_PROTECTION_CERTIFICATE',
      passwordKey = prefix + 'DATA_PROTECTION_PASSWORD';
    if (!env[certificateKey] && !env[passwordKey]) {
      if (/['\r\n]/.test(key.password)) fail('Generated key password needs manual dotenv quoting.');
      fs.appendFileSync(
        envFile,
        `${certificateKey}='${key.base64Pkcs12}'\n${passwordKey}='${key.password}'\n`,
      );
    } else if (env[certificateKey] !== key.base64Pkcs12 || env[passwordKey] !== key.password)
      fail('Existing Data Protection settings differ; no automatic key replacement is allowed.');
  }
  for (const name of ['workplace', 'procurement']) {
    const keyFile = path.join(local, `${name}-dpop.json`);
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
          'dpop-jwk',
          'generate',
          '--kid',
          `mervikon-${name}`,
          '--alg',
          'PS256',
          '--format',
          'json',
          '--out',
          `/state/${name}-dpop.json`,
          '--configurationlocation',
          '/state/crypto',
        ],
        env,
        `crypto-${name}-dpop.log`,
      );
    fs.chmodSync(keyFile, 0o600);
    const value = JSON.stringify(JSON.parse(fs.readFileSync(keyFile, 'utf8')));
    const key = name.toUpperCase() + '_DPOP_JWK';
    if (!env[key]) fs.appendFileSync(envFile, `${key}='${value}'\n`);
    else if (env[key] !== value)
      fail('Existing DPoP key differs; no automatic replacement is allowed.');
  }
  const dpopFile = path.join(local, 'finance-dpop.json');
  if (!fs.existsSync(dpopFile))
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
        'dpop-jwk',
        'generate',
        '--kid',
        'mervikon-finance',
        '--alg',
        'PS256',
        '--format',
        'json',
        '--out',
        '/state/finance-dpop.json',
        '--configurationlocation',
        '/state/crypto',
      ],
      env,
      'crypto-finance-dpop.log',
    );
  fs.chmodSync(dpopFile, 0o600);
  const dpop = JSON.parse(fs.readFileSync(dpopFile, 'utf8'));
  const jwk = JSON.stringify(dpop);
  if (!env.FINANCE_DPOP_JWK) fs.appendFileSync(envFile, `FINANCE_DPOP_JWK='${jwk}'\n`);
  else if (env.FINANCE_DPOP_JWK !== jwk)
    fail('Existing DPoP key differs; no automatic replacement is allowed.');
  const assertionFile = path.join(local, 'finance-assertion.json');
  if (!fs.existsSync(assertionFile))
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
        'CN=mervikon-finance-client',
        '--kid',
        'mervikon-finance-assertion-rsa',
        '--algorithm',
        'rsa',
        '--format',
        'json',
        '--out',
        '/state/finance-assertion.json',
        '--configurationlocation',
        '/state/crypto',
      ],
      env,
      'crypto-finance-assertion.log',
    );
  fs.chmodSync(assertionFile, 0o600);
  const assertion = JSON.parse(fs.readFileSync(assertionFile, 'utf8'));
  const publicJwks = JSON.parse(assertion.publicJwksJson);
  if (publicJwks.keys.some((k) => k.kty !== 'RSA'))
    fail('Finance assertion requires the tested RSA/PS256 package path.');
  for (const key of publicJwks.keys) key.alg = 'PS256';
  const values = {
    FINANCE_ASSERTION_CERTIFICATE: assertion.base64Pkcs12,
    FINANCE_ASSERTION_PASSWORD: assertion.password,
    FINANCE_ASSERTION_PUBLIC_JWKS: JSON.stringify(publicJwks),
  };
  for (const [key, value] of Object.entries(values)) {
    if (!value || /['\r\n]/.test(value)) fail('Invalid assertion certificate output.');
    if (!env[key]) fs.appendFileSync(envFile, `${key}='${value}'\n`);
    else if (env[key] !== value)
      fail('Existing assertion key differs; no automatic replacement is allowed.');
  }
  console.log('Stable Data Protection, Finance DPoP and private_key_jwt keys configured.');
}

function fail(message) {
  throw Error(message);
}
