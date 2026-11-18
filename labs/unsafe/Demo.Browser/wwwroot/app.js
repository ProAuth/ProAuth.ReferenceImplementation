import { inspectBrowserVisibility } from './browser-visibility.js';

const output = document.querySelector('#result');
const config = await fetch('/lab/config').then((r) => {
  if (!r.ok) throw Error('Discovery unavailable');
  return r.json();
});
const runId = sessionStorage.getItem('mervikon_run') || crypto.randomUUID();
sessionStorage.setItem('mervikon_run', runId);
const show = (v) => (output.textContent = JSON.stringify(v, null, 2));
const base64 = (b) =>
  btoa(String.fromCharCode(...new Uint8Array(b)))
    .replaceAll('+', '-')
    .replaceAll('/', '_')
    .replaceAll('=', '');
const random = () => base64(crypto.getRandomValues(new Uint8Array(32)));
const hash = async (s) =>
  base64(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(s)));
const fingerprint = async (s) =>
  [...new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(s)))]
    .map((x) => x.toString(16).padStart(2, '0'))
    .join('')
    .slice(0, 16);
// Redacted actual values remain legible in recordings; complete credentials never enter the DOM.
async function renderTokens() {
  const list = document.querySelector('#tokens');
  list.replaceChildren();
  if (config.mode !== 'spa') {
    document.querySelector('#token-heading').textContent = 'What JavaScript can read';
    document.querySelector('#token-caption').textContent =
      'Live snapshot · storage keys and cookie names, never values';
    document.querySelector('#browser-visibility').hidden = false;
    document.querySelector('#visibility').textContent = JSON.stringify(
      inspectBrowserVisibility(),
      null,
      2,
    );
    document.querySelector('#token-panel details').hidden = true;
    return;
  }
  for (const [label, value] of [
    ['Current access token', localStorage.getItem('mervikon_access')],
    ['Current refresh token', localStorage.getItem('mervikon_refresh')],
    [
      'Previous refresh token · retained only for the replay experiment',
      sessionStorage.getItem('mervikon_previous_refresh'),
    ],
  ]) {
    const term = document.createElement('dt'),
      detail = document.createElement('dd');
    term.textContent = label;
    const identity = document.createElement('code'),
      preview = document.createElement('small');
    identity.textContent = value ? await fingerprint(value) : 'Not present';
    preview.textContent = value
      ? `${value.slice(0, 12)}…${value.slice(-8)} · ${value.length} characters`
      : '';
    detail.append(identity, preview);
    list.append(term, detail);
  }
  const access = localStorage.getItem('mervikon_access');
  let fields = { note: 'No JWT to decode.' };
  try {
    const payload = JSON.parse(
      atob(access.split('.')[1].replaceAll('-', '+').replaceAll('_', '/')),
    );
    fields = Object.fromEntries(
      ['iss', 'aud', 'scope', 'iat', 'exp']
        .filter((key) => key in payload)
        .map((key) => [key, payload[key]]),
    );
  } catch {
    /* Opaque tokens are not JWTs. */
  }
  document.querySelector('#claims').textContent = JSON.stringify(fields, null, 2);
}
async function emit(step, httpStatus, extra = {}) {
  await fetch('/lab/observations', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Mervikon-Lab': '1' },
    body: JSON.stringify({
      runId,
      observedAt: new Date().toISOString(),
      source: config.mode === 'spa' ? 'Browser SPA' : 'Browser BFF',
      step,
      httpStatus,
      outcome: 'observed',
      ...extra,
    }),
  });
}
function save(t) {
  if (t.access_token) localStorage.setItem('mervikon_access', t.access_token);
  if (t.refresh_token) localStorage.setItem('mervikon_refresh', t.refresh_token);
}
async function token(form) {
  const r = await fetch(config.tokenEndpoint, {
    method: 'POST',
    body: new URLSearchParams({ client_id: config.clientId, ...form }),
  });
  const body = await r.json();
  return { r, body };
}
async function login() {
  if (config.mode === 'bff') {
    location.href = '/bff/account/login?returnUrl=/';
    return;
  }
  const verifier = random() + random(),
    state = random(),
    challenge = await hash(verifier);
  sessionStorage.setItem('mervikon_verifier', verifier);
  sessionStorage.setItem('mervikon_state', state);
  await emit('authorization', null, { challenge });
  // OAuth-only client: no unvalidated ID token is used to sign anyone into an application.
  const u = new URL(config.authorizationEndpoint);
  u.search = new URLSearchParams({
    client_id: config.clientId,
    response_type: 'code',
    redirect_uri: config.redirectUri,
    scope: 'offline_access conference.quote',
    resource: 'urn:mervikon:conference',
    proauth_tenant: config.tenantId,
    code_challenge: challenge,
    code_challenge_method: 'S256',
    state,
  });
  location.href = u;
}
if (location.pathname === '/callback') {
  const q = new URLSearchParams(location.search),
    verifier = sessionStorage.getItem('mervikon_verifier');
  const valid =
    !!q.get('state') &&
    q.get('state') === sessionStorage.getItem('mervikon_state') &&
    !!verifier &&
    q.get('iss') === config.issuer;
  await emit('callback', null, {
    issuer: q.get('iss'),
    issuerMatches: q.get('iss') === config.issuer,
    verifierPresent: !!verifier,
  });
  history.replaceState(null, '', '/');
  sessionStorage.removeItem('mervikon_state');
  sessionStorage.removeItem('mervikon_verifier');
  if (!valid)
    show({ error: 'Callback state, issuer or verifier check failed; no redemption attempted.' });
  else {
    const { r, body } = await token({
      grant_type: 'authorization_code',
      code: q.get('code'),
      code_verifier: verifier,
      redirect_uri: config.redirectUri,
    });
    if (r.ok) {
      sessionStorage.removeItem('mervikon_previous_refresh');
      sessionStorage.removeItem('mervikon_reuse_rejected');
      save(body);
    }
    await emit('token', r.status);
    show({
      tokenEndpoint: r.status,
      accessTokenReceived: !!body.access_token,
      refreshTokenReceived: !!body.refresh_token,
    });
  }
}
document.querySelector('#mode').textContent =
  config.mode === 'spa'
    ? 'Direct browser OAuth client · securitylab-spa · browser-held tokens'
    : 'Package BFF · securitylab-bff · server-held tokens';
for (const id of ['refresh', 'reuse', 'successor', 'inspect', 'audience'])
  document.querySelector('#' + id).hidden = config.mode !== 'spa';
if (config.mode === 'bff') {
  const response = await fetch('/bff/user', { headers: { 'X-ProAuth-BFF': '1' } });
  if (response.ok) {
    const user = await response.json();
    if (user.isAuthenticated) {
      const name =
        user.claims?.find((claim) => claim.type === user.nameClaimType)?.value ||
        user.claims?.find((claim) => claim.type === 'preferred_username')?.value;
      document.querySelector('#account-name').textContent = name
        ? `Signed in as ${name}`
        : 'Signed in';
      document.querySelector('#account').hidden = false;
    }
  }
}
const actions = {
  login,
  'inspect-browser': renderTokens,
  quote: async () => {
    const headers =
      config.mode === 'spa'
        ? { Authorization: `Bearer ${localStorage.getItem('mervikon_access') || ''}` }
        : { 'X-ProAuth-BFF': '1' };
    const r = await fetch(config.api, { headers });
    await emit('legitimate', r.status);
    show({ status: r.status, body: r.ok ? await r.json() : null });
  },
  inject: async () => {
    const script = document.createElement('script');
    script.src = '/widget.js?run=' + encodeURIComponent(runId);
    document.body.append(script);
  },
  audience: async () => {
    const r = await fetch(config.wrongAudience, {
      headers: { Authorization: `Bearer ${localStorage.getItem('mervikon_access') || ''}` },
    });
    await emit('audience', r.status);
    show({ wrongAudience: r.status, expected: 401 });
  },
  refresh: async () => {
    const old = localStorage.getItem('mervikon_refresh');
    if (!old) throw Error('Sign in first.');
    const { r, body } = await token({ grant_type: 'refresh_token', refresh_token: old });
    if (r.ok) {
      sessionStorage.removeItem('mervikon_reuse_rejected');
      sessionStorage.setItem('mervikon_previous_refresh', old);
      save(body);
    }
    await emit('refresh', r.status);
    show({
      status: r.status,
      previousFingerprint: await fingerprint(old),
      successorFingerprint: body.refresh_token ? await fingerprint(body.refresh_token) : null,
      rotated: !!body.refresh_token && body.refresh_token !== old,
    });
  },
  reuse: async () => {
    const previous = sessionStorage.getItem('mervikon_previous_refresh');
    if (!previous || previous === localStorage.getItem('mervikon_refresh'))
      throw Error('Rotate the refresh token first.');
    const { r, body } = await token({ grant_type: 'refresh_token', refresh_token: previous });
    await emit('refresh-reuse', r.status, { error: body.error });
    if (!r.ok && body.error === 'invalid_grant')
      sessionStorage.setItem('mervikon_reuse_rejected', 'true');
    show({
      oldTokenStatus: r.status,
      oldTokenError: body.error || null,
      replayedFingerprint: await fingerprint(previous),
      next: 'Now try the current refresh token.',
    });
  },
  successor: async () => {
    if (sessionStorage.getItem('mervikon_reuse_rejected') !== 'true')
      throw Error('First observe rejection of the replayed old refresh token.');
    const current = localStorage.getItem('mervikon_refresh');
    if (!current) throw Error('Sign in and rotate first.');
    const { r, body } = await token({ grant_type: 'refresh_token', refresh_token: current });
    await emit('successor', r.status, { error: body.error });
    show({
      successorStatus: r.status,
      successorError: body.error || null,
      testedFingerprint: await fingerprint(current),
      familyRejectionObserved: !r.ok && body.error === 'invalid_grant',
    });
    if (r.ok) save(body);
  },
  inspect: async () => {
    const accessToken = localStorage.getItem('mervikon_access');
    if (!accessToken) throw Error('Sign in first.');
    const inspection = await fetch('/lab/introspect', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Mervikon-Lab': '1' },
      body: JSON.stringify({ accessToken }),
    });
    if (!inspection.ok) throw Error(`Introspection probe failed (HTTP ${inspection.status}).`);
    const observed = await inspection.json();
    const api = await fetch(config.api, { headers: { Authorization: `Bearer ${accessToken}` } });
    show({
      accessTokenFingerprint: await fingerprint(accessToken),
      introspectionStatus: observed.status,
      active: observed.active,
      apiStatus: api.status,
      note: 'AS activity and local JWT validation are separate observations.',
    });
  },
  reset: async () => {
    if (config.mode === 'spa') {
      for (const key of ['mervikon_refresh', 'mervikon_access']) {
        const value = localStorage.getItem(key);
        if (value) {
          const r = await fetch(config.revocationEndpoint, {
            method: 'POST',
            body: new URLSearchParams({
              client_id: config.clientId,
              token: value,
              token_type_hint: key === 'mervikon_refresh' ? 'refresh_token' : 'access_token',
            }),
          });
          if (!r.ok) throw Error('Revocation failed; browser credentials preserved for cleanup.');
        }
      }
    }
    for (const key of ['mervikon_access', 'mervikon_refresh']) localStorage.removeItem(key);
    for (const key of [
      'mervikon_previous_refresh',
      'mervikon_reuse_rejected',
      'mervikon_state',
      'mervikon_verifier',
      'mervikon_run',
    ])
      sessionStorage.removeItem(key);
    show({
      browserFixtureCleared: true,
      note: 'SPA revocation responses processed. Self-contained access tokens may remain usable until expiry. BFF sessions require package sign-out or isolated process reset.',
    });
  },
};
for (const [id, action] of Object.entries(actions))
  document.querySelector('#' + id).addEventListener('click', async (event) => {
    const button = event.currentTarget;
    button.disabled = true;
    try {
      await action();
      await renderTokens();
    } catch (e) {
      show({ error: e.message });
    } finally {
      button.disabled = false;
    }
  });
window.addEventListener('mervikon-widget-result', async (e) => {
  show(e.detail);
  await renderTokens();
});

await renderTokens();
