import React, { useEffect, useState } from 'react';
type Evidence = {
  runId: string;
  observedAt: string;
  source: string;
  step: string;
  httpStatus: number | null;
  outcome: string;
  token?: Record<string, unknown> | null;
  proof?: Record<string, unknown> | null;
  error?: string | null;
  challenge?: string | null;
  issuer?: string | null;
  verifierPresent?: boolean | null;
  issuerMatches?: boolean | null;
};
type Recording = {
  schemaVersion: number;
  packages: string;
  provenance: string;
  observations: Evidence[];
};
export function ProtocolEvidence() {
  const [data, setData] = useState<Recording | null>(null),
    [offline, setOffline] = useState(false),
    [error, setError] = useState(''),
    [selected, setSelected] = useState(0),
    [run, setRun] = useState('');
  useEffect(() => {
    if (offline) return;
    let active = true;
    const load = () =>
      fetch('/api/protocol-evidence')
        .then((r) => {
          if (!r.ok) throw Error('Protocol evidence unavailable');
          return r.json();
        })
        .then((v) => {
          if (active) {
            setData(v);
            setError('');
          }
        })
        .catch((e) => {
          if (active) setError(String(e));
        });
    void load();
    const timer = setInterval(load, 1000);
    return () => {
      active = false;
      clearInterval(timer);
    };
  }, [offline]);
  const events = (data?.observations || []).filter((e) => !run || e.runId === run),
    current = events[Math.min(selected, events.length - 1)];
  async function importFile(file: File | undefined) {
    if (!file) return;
    try {
      if (file.size > 2_000_000) throw Error('Recording too large');
      const value = JSON.parse(await file.text()) as Recording;
      if (
        value.schemaVersion !== 1 ||
        !Array.isArray(value.observations) ||
        value.observations.length > 500 ||
        value.observations.some(
          (e) =>
            typeof e.runId !== 'string' ||
            typeof e.step !== 'string' ||
            typeof e.observedAt !== 'string' ||
            typeof e.source !== 'string' ||
            typeof e.outcome !== 'string',
        )
      )
        throw Error('Unsupported recording');
      setOffline(true);
      setData(value);
      setSelected(0);
      setRun('');
      setError('');
    } catch (e) {
      setError(String(e));
    }
  }
  function exportFile() {
    if (!data) return;
    const url = URL.createObjectURL(
      new Blob([JSON.stringify({ ...data, provenance: 'recorded' }, null, 2)], {
        type: 'application/json',
      }),
    );
    const a = document.createElement('a');
    a.href = url;
    a.download = 'conference-protocol-evidence.json';
    a.click();
    URL.revokeObjectURL(url);
  }
  return (
    <main className="comparison">
      <p>
        {offline
          ? 'Recorded evidence · imported offline file'
          : 'Live observation feed · actual executions'}
      </p>
      <h1>Follow the protocol</h1>
      <p>
        Decoded fields explain a message. Acceptance is established by the actual server response;
        an HTTP status alone does not identify its rejecting validator.
      </p>
      <div className="tools">
        <label>
          Run{' '}
          <select
            value={run}
            onChange={(e) => {
              setRun(e.target.value);
              setSelected(0);
            }}
          >
            <option value="">All runs</option>
            {[...new Set(data?.observations.map((e) => e.runId))].map((id) => (
              <option key={id}>{id}</option>
            ))}
          </select>
        </label>
        <button onClick={exportFile}>Export this recording</button>
        <label>
          Open recording{' '}
          <input
            type="file"
            accept="application/json,.json"
            onChange={(e) => void importFile(e.target.files?.[0])}
          />
        </label>
        {offline && <button onClick={() => setOffline(false)}>Return to live feed</button>}
      </div>
      {error && <p role="alert">{error}</p>}
      <p>
        {data?.packages} · {events.length} observations · recordings cannot execute operations
      </p>
      <table>
        <thead>
          <tr>
            <th>Time / source</th>
            <th>Step</th>
            <th>Observed outcome</th>
          </tr>
        </thead>
        <tbody>
          {events.map((e, i) => (
            <tr key={i}>
              <td>
                {new Date(e.observedAt).toLocaleTimeString()} · {e.source}
              </td>
              <td>
                <button onClick={() => setSelected(i)}>{e.step}</button>
              </td>
              <td>
                {e.httpStatus ? `HTTP ${e.httpStatus}` : 'No HTTP status collected'} ·{' '}
                {e.error || e.outcome}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {current && (
        <section>
          <h2>
            {current.source} · {current.step}
          </h2>
          <p>
            {current.runId} · {current.observedAt}
          </p>
          <pre style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
            {JSON.stringify(current, null, 2)}
          </pre>
          <button disabled={selected === 0} onClick={() => setSelected(Math.max(0, selected - 1))}>
            ← Previous
          </button>
          <button
            disabled={selected >= events.length - 1}
            onClick={() => setSelected(selected + 1)}
          >
            Next →
          </button>
        </section>
      )}
      <p>
        Live operations run without pauses. Use this evidence afterward. Raw tokens, cookies, codes,
        verifiers and private keys are excluded.
      </p>
    </main>
  );
}
