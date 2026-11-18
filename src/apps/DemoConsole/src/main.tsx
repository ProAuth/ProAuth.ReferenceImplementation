import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import './styles.css';
import { ProtocolEvidence } from './ProtocolEvidence';
import { ControlComparisons } from './ControlComparisons';

type Event = {
  eventId: string;
  observedAt: string;
  source: string;
  operation: string;
  httpStatus: number;
  traceId: string;
  spanId: string;
  parentSpanId: string | null;
  businessId: string | null;
  state: string | null;
  revision: number | null;
};
type Recording = {
  schemaVersion: number;
  provenance: string;
  exportedAt: string;
  environment: string;
  packages: string;
  evidenceCompleteness: string;
  observations: Event[];
};
type Scenario = { id: string; name: string; capabilities: string; profiles: string };
const scenarioFor: Record<string, string> = {
  RequestPurchase: 'SCN-01',
  ApprovePurchase: 'SCN-01',
  ExchangeHandoff: 'SCN-13',
  ImportOrder: 'SCN-13',
  ListOrders: 'SCN-04',
  AcceptOrder: 'SCN-04',
  DispatchOrder: 'SCN-09',
  ReceiveOrder: 'SCN-18',
  ReadReceipts: 'SCN-09',
  ListPayments: 'SCN-12',
  PreparePayments: 'SCN-09',
  ReleasePayment: 'SCN-08',
  Reconcile: 'SCN-09',
};
const human = (s: string) => s.replace(/([a-z])([A-Z])/g, '$1 $2');
type Lab = {
  variant: string;
  observedAt: string;
  legitimateBefore: number;
  tokenOnly: number;
  capturedProof: number | null;
  legitimateAfter: number;
  expiredToken: number;
  cleanupVerified: boolean;
  proofControls?: {
    wrongMethod: number;
    wrongUri: number;
    wrongHash: number;
    wrongKey: number;
  } | null;
};
function Console() {
  const [showProtocol, setShowProtocol] = useState(false);
  const [showControls, setShowControls] = useState(
    new URLSearchParams(location.search).get('view') === 'controls',
  );
  const [lab, setLab] = useState<Lab[]>([]),
    [showLab, setShowLab] = useState(false);
  useEffect(() => {
    const load = () =>
      fetch('/api/lab-comparison')
        .then((r) => (r.ok ? r.json() : []))
        .then(setLab)
        .catch(() => setLab([]));
    void load();
    const timer = setInterval(load, 1000);
    return () => clearInterval(timer);
  }, []);
  const [data, setData] = useState<Recording | null>(null),
    [scenarios, setScenarios] = useState<Scenario[]>([]),
    [selected, setSelected] = useState<string>(''),
    [view, setView] = useState('Business'),
    [live, setLive] = useState(true),
    [product, setProduct] = useState(false),
    [error, setError] = useState(''),
    [presenter, setPresenter] = useState(false);
  const events = data?.observations || [],
    current = events.find((e) => e.eventId === selected) || events.at(-1),
    index = current ? events.indexOf(current) : -1;
  async function load() {
    try {
      const r = await fetch('/api/recordings');
      if (!r.ok) throw Error('Evidence service unavailable');
      const d = await r.json();
      setData(d);
      setError('');
    } catch (e) {
      setError(String(e));
    }
  }
  useEffect(() => {
    fetch('/api/scenarios')
      .then((r) => r.json())
      .then(setScenarios)
      .catch(() => setError('Scenario registry unavailable'));
  }, []);
  useEffect(() => {
    if (!live) return;
    void load();
    const t = setInterval(load, 3000);
    return () => clearInterval(t);
  }, [live]);
  const step = (n: number) => {
    setLive(false);
    if (events.length)
      setSelected(events[Math.max(0, Math.min(events.length - 1, index + n))].eventId);
  };
  useEffect(() => {
    const f = (e: KeyboardEvent) => {
      if ((e.target as HTMLElement).tagName === 'INPUT') return;
      if (e.key === 'ArrowRight') step(1);
      if (e.key === 'ArrowLeft') step(-1);
    };
    window.addEventListener('keydown', f);
    return () => window.removeEventListener('keydown', f);
  }, [events, index]);
  const scenario = scenarios.find((s) => s.id === scenarioFor[current?.operation || '']);
  return (
    <div className={presenter ? 'console presenter' : 'console'}>
      <header>
        <div>
          <strong>Demo Console</strong>
          <span>Mervikon Group</span>
        </div>
        <div className="tools">
          <button
            onClick={() => {
              setShowProtocol(!showProtocol);
              setShowLab(false);
              setShowControls(false);
            }}
          >
            Protocol Evidence
          </button>
          <label>
            <input
              type="checkbox"
              checked={product}
              onChange={(e) => setProduct(e.target.checked)}
            />{' '}
            Product names
          </label>
          <button onClick={() => setPresenter(!presenter)}>
            {presenter ? 'Exit presentation view' : 'Presentation view'}
          </button>
          <button
            onClick={() => {
              setShowProtocol(false);
              setShowControls(false);
              setShowLab(!showLab);
            }}
          >
            {showLab ? 'API observations' : 'Bearer / DPoP comparison'}
          </button>
          <button
            onClick={() => {
              setShowProtocol(false);
              setShowLab(false);
              setShowControls(!showControls);
            }}
          >
            {showControls ? 'API observations' : 'Protocol controls'}
          </button>
          <a
            href={
              showProtocol
                ? '/api/protocol-evidence'
                : showControls
                  ? '/api/control-comparisons'
                  : showLab
                    ? '/api/lab-comparison'
                    : '/api/export'
            }
            download
          >
            {showProtocol
              ? 'Export live protocol feed'
              : showControls
                ? 'Export control results'
                : showLab
                  ? 'Export lab results'
                  : 'Export observed evidence'}
          </a>
        </div>
      </header>
      <div className="status">
        <b>
          {showProtocol
            ? 'Protocol evidence viewer'
            : showControls
              ? 'Recorded evidence · protocol controls'
              : showLab
                ? 'Recorded evidence · lab comparison'
                : live
                  ? 'Live observation feed'
                  : 'Recorded evidence · playback'}
        </b>
        <span>
          {showProtocol
            ? 'Sanitized protocol observations'
            : showControls
              ? 'Local controlled experiments'
              : showLab
                ? 'Two isolated recorded runs'
                : `Local operator · ${data?.environment || 'local'} · ${events.length} observations`}
        </span>
        <button hidden={showLab || showControls || showProtocol} onClick={() => setLive(!live)}>
          {live ? 'Pause for playback' : 'Resume live feed'}
        </button>
      </div>
      {error && (
        <p role="alert">{error}. Existing observations do not establish current readiness.</p>
      )}
      {showProtocol && <ProtocolEvidence />}
      {showControls && <ControlComparisons />}
      {showLab && (
        <main className="comparison">
          <p>Recorded evidence · SCN-11 · intentionally unsafe demonstration</p>
          <h1>Stolen token, same protected quote</h1>
          <p>
            The TLS terminator captured real requests. A separate runner replayed the artifacts
            without a victim private key. The API validates both token types normally.
          </p>
          {lab.length !== 2 ? (
            <p>
              No completed comparison is available. Run the isolated lab with{' '}
              <code>node scripts/mervikon.mjs lab-run --lab</code>.
            </p>
          ) : (
            <>
              <table>
                <thead>
                  <tr>
                    <th>Observed request</th>
                    {lab.map((r) => (
                      <th key={r.variant}>{r.variant}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {(
                    [
                      ['Legitimate request before theft', 'legitimateBefore'],
                      ['Stolen token only', 'tokenOnly'],
                      ['Captured proof replay', 'capturedProof'],
                      ['Fresh legitimate request after attack', 'legitimateAfter'],
                      ['Expired token during cleanup', 'expiredToken'],
                    ] as const
                  ).map(([label, key]) => (
                    <tr key={key}>
                      <th>{label}</th>
                      {lab.map((r) => (
                        <td key={r.variant}>
                          {r[key] === null ? 'Not applicable' : `HTTP ${r[key]}`}
                        </td>
                      ))}
                    </tr>
                  ))}
                  {(
                    [
                      ['Fresh proof with wrong method', 'wrongMethod'],
                      ['Fresh proof with wrong URI', 'wrongUri'],
                      ['Fresh proof with wrong token hash', 'wrongHash'],
                      ['Fresh proof with wrong key', 'wrongKey'],
                    ] as const
                  ).map(([label, key]) => (
                    <tr key={key}>
                      <th>{label}</th>
                      {lab.map((r) => (
                        <td key={r.variant}>
                          {r.variant === 'bearer'
                            ? 'Not applicable'
                            : r.proofControls
                              ? `HTTP ${r.proofControls[key]}`
                              : 'Not collected'}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
              <p>
                {lab
                  .map(
                    (r) =>
                      `${r.variant}: ${r.observedAt}; cleanup ${r.cleanupVerified ? 'verified' : 'incomplete'}`,
                  )
                  .join(' · ')}
              </p>
            </>
          )}
          <p className="notice">
            ProAuth packages · fixed calibration-station quote, revision 1. These are recorded HTTP
            outcomes, not current preflight or certification. Exact rejecting validator is not
            instrumented. Single API process; no replica replay claim. Normal Mervikon applications
            never use the capture proxy.
          </p>
        </main>
      )}
      <div className="workspace" hidden={showLab || showControls || showProtocol}>
        <nav aria-label="Observed operations">
          <h2>Observed operations</h2>
          <p>Actual API requests in receipt order. Missing spans remain missing.</p>
          {events.length === 0 ? (
            <p>
              Run a business action in Workplace, Supplier Portal or Finance Desk to collect
              observations.
            </p>
          ) : (
            events.map((e) => (
              <button
                key={e.eventId}
                className={current?.eventId === e.eventId ? 'selected' : ''}
                onClick={() => {
                  setSelected(e.eventId);
                  setLive(false);
                }}
              >
                <span>{human(e.operation)}</span>
                <small>
                  {e.source} · HTTP {e.httpStatus} · {new Date(e.observedAt).toLocaleTimeString()}
                </small>
              </button>
            ))
          )}
        </nav>
        <main>
          <div className="heading">
            <p>
              Lesson target: {scenario?.id || 'Mervikon workflow'} ·{' '}
              {scenario?.name || 'Execute a business workflow'}
            </p>
            <h1>{current ? human(current.operation) : 'Waiting for observed evidence'}</h1>
            <div className="tabs" role="tablist" aria-label="Evidence views">
              {['Business', 'Protocol', 'ProAuth'].map((v) => (
                <button role="tab" aria-selected={view === v} key={v} onClick={() => setView(v)}>
                  {v === 'ProAuth' && !product ? 'Configuration' : v}
                </button>
              ))}
            </div>
          </div>
          {current && (
            <section role="tabpanel" aria-label={view} key={current.eventId + view}>
              {view === 'Business' && (
                <>
                  <p className="result">
                    HTTP {current.httpStatus} ·{' '}
                    {current.httpStatus < 300
                      ? 'Request completed'
                      : current.httpStatus === 401
                        ? 'Authentication rejected'
                        : current.httpStatus === 403
                          ? 'Access denied'
                          : 'Request did not complete successfully'}
                  </p>
                  <dl>
                    <dt>Business record</dt>
                    <dd>{current.businessId || 'Not observed for this response'}</dd>
                    <dt>Resulting state</dt>
                    <dd>
                      {current.state || 'Not observed'}
                      {current.revision ? ` · revision ${current.revision}` : ''}
                    </dd>
                    <dt>Previous state</dt>
                    <dd>Not collected; no transition is inferred.</dd>
                    <dt>Actor and organization</dt>
                    <dd>
                      Not exported by this producer. Inspect the authenticated business application.
                    </dd>
                  </dl>
                </>
              )}
              {view === 'Protocol' && (
                <>
                  <p className="result">
                    {product ? current.source + '.Api' : 'Resource Server'} observed HTTP{' '}
                    {current.httpStatus}
                  </p>
                  <p>
                    The HTTP result is observed. Specific token validation checks are not
                    instrumented and remain unknown.
                  </p>
                  <ol className="causal">
                    {events
                      .filter((e) => e.traceId === current.traceId)
                      .map((e) => (
                        <li key={e.eventId}>
                          {product ? e.source + '.Api' : 'Resource Server'} → {human(e.operation)} →
                          HTTP {e.httpStatus}
                          <small>
                            span {e.spanId} · parent {e.parentSpanId || 'not observed'}
                          </small>
                        </li>
                      ))}
                  </ol>
                  <dl>
                    <dt>Trace</dt>
                    <dd>{current.traceId}</dd>
                    <dt>Observed at</dt>
                    <dd>{current.observedAt}</dd>
                    <dt>Order</dt>
                    <dd>Receipt order; cross-service clocks are not calibrated.</dd>
                  </dl>
                </>
              )}
              {view === 'ProAuth' && (
                <>
                  <p className="result">{data?.packages}</p>
                  <dl>
                    <dt>Scenario source</dt>
                    <dd>docs/scenarios/registry.md · {scenario?.id || 'Unknown'}</dd>
                    <dt>Capability mapping</dt>
                    <dd>{scenario?.capabilities || 'Unavailable'}</dd>
                    <dt>Expected lesson profiles</dt>
                    <dd>
                      {scenario?.profiles || 'Unavailable'} (registry intent, not effective
                      configuration evidence)
                    </dd>
                    <dt>Effective registration / configuration fingerprint</dt>
                    <dd>
                      Not collected. This observation does not prove the configured profile or
                      certification.
                    </dd>
                  </dl>
                  <p>
                    ProAuth BFF, OIDC client and resource-server packages own protocol handling.
                    Business authorization remains in the APIs.
                  </p>
                </>
              )}
            </section>
          )}
          <footer>
            <button disabled={index <= 0} onClick={() => step(-1)}>
              ← Previous
            </button>
            <span>
              {index < 0 ? 0 : index + 1} / {events.length}
            </span>
            <button disabled={index >= events.length - 1} onClick={() => step(1)}>
              Next →
            </button>
            <button
              onClick={() => {
                const e = events.find((e) => e.httpStatus >= 400);
                if (e) {
                  setSelected(e.eventId);
                  setLive(false);
                }
              }}
            >
              Jump to failure
            </button>
          </footer>
          <p className="notice">
            Incomplete evidence: API HTTP observations only. Playback and export cannot perform
            business actions. Retention: 20 traces / 24 hours; restart clears this local store.
          </p>
        </main>
      </div>
    </div>
  );
}
createRoot(document.getElementById('root')!).render(<Console />);
