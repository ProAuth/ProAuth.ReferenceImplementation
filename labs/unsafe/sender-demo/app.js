let state,
  revision = -1,
  inspected = false,
  busy = false;
const $ = (s) => document.querySelector(s);
const names = {
  legitimate: 'Legitimate API call',
  capture: 'Token obtained',
  replay: 'Independent replay',
  cleanup: 'Expiry / cleanup',
};
async function command(route, value) {
  busy = true;
  render();
  try {
    const r = await fetch(route, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Sender-Demo': state.csrf },
      body: JSON.stringify(value),
    });
    if (!r.ok) throw Error((await r.json()).error);
  } catch (e) {
    $('#error').textContent = e.message;
    $('#error').hidden = false;
  } finally {
    busy = false;
    await poll();
    render();
  }
}
function render() {
  if (!state) return;
  const active = ['starting', 'waiting', 'running', 'cleanup'].includes(state.phase);
  for (const b of document.querySelectorAll('[data-variant]')) {
    b.disabled = busy || active || !state.ready;
    b.setAttribute('aria-pressed', String(b.dataset.variant === state.variant));
  }
  for (const b of document.querySelectorAll('[data-action]')) {
    b.disabled = busy || state.awaiting !== b.dataset.action;
    b.classList.toggle('current', state.awaiting === b.dataset.action);
  }
  const latest = state.events.filter((e) => e.step !== 'cleanup' && e.token).at(-1);
  const capture = state.events.find((e) => e.step === 'capture');
  const evidence = inspected ? capture || state.events.find((e) => e.step === 'legitimate') : null;
  $('#inspect').disabled = !state.events.some((e) => e.token);
  $('#inspect').textContent =
    state.variant === 'dpop'
      ? 'Inspect token + proof'
      : state.variant === 'mtls'
        ? 'Inspect token + certificate'
        : 'Inspect token';
  $('#context').textContent =
    state.variant === 'mtls'
      ? 'mTLS · real certificate-bound token; controlled token possession, no private-key theft.'
      : state.variant === 'dpop'
        ? 'DPoP · real request captured at the isolated TLS terminator; the signing key stays with the client.'
        : state.variant === 'bearer'
          ? 'Bearer · real request captured at the isolated TLS terminator; possession is enough to replay.'
          : 'Choose a token variant to start a fresh, isolated probe.';
  $('#capture-help').textContent =
    state.variant === 'mtls'
      ? 'Give the replay request the real token only'
      : 'Separate attacker reads the captured request';
  $('#actor').textContent =
    latest?.step === 'replay'
      ? 'Replay client · no sender key'
      : latest?.step === 'capture'
        ? 'Token obtained · no sender key'
        : 'Legitimate client';
  $('#outcome').textContent =
    latest?.step === 'capture'
      ? 'Token obtained'
      : latest?.httpStatus
        ? `HTTP ${latest.httpStatus}`
        : 'Not executed';
  $('#progress').textContent =
    state.phase === 'cleanup'
      ? 'Requests complete. Verifying token expiry and cleanup…'
      : state.phase === 'complete'
        ? 'Run complete · expired token rejected · cleanup verified'
        : state.phase === 'running'
          ? 'Executing the real request…'
          : state.awaiting
            ? `Ready: ${names[state.awaiting]}`
            : state.phase === 'failed'
              ? 'Run failed; do not interpret later results as a successful control.'
              : 'Starting the isolated client…';
  const history = $('#history');
  history.replaceChildren();
  for (const e of state.events) {
    const li = document.createElement('li'),
      label = document.createElement('span'),
      value = document.createElement('span');
    label.textContent =
      e.step === 'replay'
        ? state.variant === 'dpop' && !e.token
          ? 'Copied token + proof'
          : 'Stolen token only'
        : names[e.step];
    value.textContent = e.httpStatus ? `HTTP ${e.httpStatus}` : 'Captured';
    li.append(label, value);
    history.append(li);
  }
  const shown = evidence || latest;
  $('#provenance').textContent = shown
    ? `${names[shown.step]} · ${new Date(shown.observedAt).toLocaleTimeString()} · run ${shown.runId}`
    : 'Nothing has executed yet.';
  if (evidence) {
    const fields = ['fingerprint', 'issuer', 'audience', 'scope', 'jkt', 'certificateThumbprint'];
    const view = {
      token: Object.fromEntries(
        fields
          .filter((key) => evidence.token?.[key] != null)
          .map((key) => [key, evidence.token[key]]),
      ),
    };
    if (state.variant === 'dpop' && evidence.proof) view.proof = evidence.proof;
    if (state.variant === 'mtls')
      view.presentedCertificateThumbprint = state.events.find(
        (e) => e.step === 'legitimate',
      )?.presentedCertificateThumbprint;
    $('#evidence').textContent = JSON.stringify(view, null, 2);
  } else
    $('#evidence').textContent = latest
      ? JSON.stringify(
          {
            step: latest.step,
            httpStatus: latest.httpStatus ?? null,
            tokenFingerprint: latest.token?.fingerprint ?? null,
          },
          null,
          2,
        )
      : 'No token or proof has been captured.';
  $('#controls').href = state.consoleOrigin + '/?view=controls';
  if (state.error) {
    $('#error').textContent = state.error;
    $('#error').hidden = false;
  }
}
async function poll() {
  try {
    const r = await fetch('/api/state');
    if (!r.ok) throw Error('Operator unavailable');
    const next = await r.json();
    state = next;
    if (next.revision !== revision) {
      revision = next.revision;
      render();
    }
  } catch {
    $('#error').textContent = 'Local operator disconnected. No new request outcome is implied.';
    $('#error').hidden = false;
  }
}
for (const b of document.querySelectorAll('[data-variant]'))
  b.addEventListener('click', () => {
    inspected = false;
    command('/api/start', { variant: b.dataset.variant });
  });
for (const b of document.querySelectorAll('[data-action]'))
  b.addEventListener('click', () => {
    inspected = false;
    command('/api/step', { action: b.dataset.action });
  });
$('#inspect').addEventListener('click', () => {
  inspected = true;
  render();
});
await poll();
setInterval(poll, 200);
