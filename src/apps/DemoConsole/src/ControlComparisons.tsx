import React, { useEffect, useState } from 'react';

type Protocol = {
  variant: string;
  observedAt: string;
  legitimate: number;
  invalidToken: number;
  afterRevocation: number;
  expired: number;
  revocationResponseProcessed: boolean;
  missingCertificate: number | null;
  wrongCertificate: number | null;
  forgedCertificateHeader: number | null;
  tamperedCiphertext: number | null;
};
type Replica = {
  observedAt: string;
  first: number;
  replay: number;
  freshOne: number;
  freshTwo: number;
  concurrentRequests: number;
  accepted: number;
  rejected: number;
  outageOne: number;
  outageTwo: number;
  recoveredOne: number;
  recoveredTwo: number;
  replayAfterRestart: number;
  instancesVerified: boolean;
  cleanupVerified: boolean;
};
type Comparisons = { protocols: Protocol[]; replicas: Replica | null };
export function ControlComparisons() {
  const [data, setData] = useState<Comparisons | null>(null),
    [error, setError] = useState(false);
  useEffect(() => {
    const load = () =>
      fetch('/api/control-comparisons')
        .then((r) => {
          if (!r.ok) throw Error();
          return r.json();
        })
        .then(setData)
        .catch(() => setError(true));
    void load();
    const timer = setInterval(load, 1000);
    return () => clearInterval(timer);
  }, []);
  return (
    <main className="comparison">
      <p>Recorded evidence · SCN-10, SCN-15, SCN-16, SCN-22</p>
      <h1>Token validation and shared replay state</h1>
      <p>
        These are completed local protocol requests. Results describe the observed configuration at
        the recorded time; they do not establish current readiness or certification.
      </p>
      {error ? (
        <p role="alert">Control evidence is unavailable or failed validation.</p>
      ) : !data ? (
        <p>Loading observations…</p>
      ) : (
        <>
          <h2>One protected quote, different token representations</h2>
          {data.protocols.length === 0 ? (
            <p>No completed token controls have been recorded.</p>
          ) : (
            <>
              <table>
                <thead>
                  <tr>
                    <th>Observed request</th>
                    {data.protocols.map((p) => (
                      <th key={p.variant}>{p.variant}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {(
                    [
                      ['Legitimate request', 'legitimate'],
                      ['Invalid token', 'invalidToken'],
                      ['After revocation request', 'afterRevocation'],
                      ['After expiry', 'expired'],
                      ['Missing TLS certificate', 'missingCertificate'],
                      ['Wrong TLS certificate', 'wrongCertificate'],
                      ['Forged certificate header', 'forgedCertificateHeader'],
                      ['Tampered ciphertext', 'tamperedCiphertext'],
                    ] as const
                  ).map(([label, key]) => (
                    <tr key={key}>
                      <th>{label}</th>
                      {data.protocols.map((p) => (
                        <td key={p.variant}>
                          {p[key] === null ? 'Not collected / not applicable' : `HTTP ${p[key]}`}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
              <p>{data.protocols.map((p) => `${p.variant}: ${p.observedAt}`).join(' · ')}</p>
              {data.protocols.some((p) => !p.revocationResponseProcessed) && (
                <p>
                  The client could not process some revocation responses. API outcomes were measured
                  independently; no successful revocation acknowledgement is inferred.
                </p>
              )}
              <p>
                JWT and encrypted JWT consumers validate locally; the opaque-token consumer
                introspects each request. Encryption conceals claims and detects ciphertext
                tampering. It does not prove possession or prevent replay of a stolen bearer token.
              </p>
            </>
          )}
          <h2>Two API instances, one replay decision</h2>
          {!data.replicas ? (
            <p>No completed replica/outage control has been recorded.</p>
          ) : (
            <>
              <table>
                <thead>
                  <tr>
                    <th>Observed control</th>
                    <th>Result</th>
                  </tr>
                </thead>
                <tbody>
                  <tr>
                    <th>First proof on instance one</th>
                    <td>HTTP {data.replicas.first}</td>
                  </tr>
                  <tr>
                    <th>Same proof on instance two</th>
                    <td>HTTP {data.replicas.replay}</td>
                  </tr>
                  <tr>
                    <th>Fresh proof on each instance</th>
                    <td>
                      HTTP {data.replicas.freshOne} / {data.replicas.freshTwo}
                    </td>
                  </tr>
                  <tr>
                    <th>Concurrent use of one fresh proof</th>
                    <td>
                      {data.replicas.accepted} accepted, {data.replicas.rejected} rejected of{' '}
                      {data.replicas.concurrentRequests}
                    </td>
                  </tr>
                  <tr>
                    <th>Replay store stopped</th>
                    <td>
                      HTTP {data.replicas.outageOne} / {data.replicas.outageTwo}
                    </td>
                  </tr>
                  <tr>
                    <th>Fresh proof after recovery</th>
                    <td>
                      HTTP {data.replicas.recoveredOne} / {data.replicas.recoveredTwo}
                    </td>
                  </tr>
                  <tr>
                    <th>Previously used proof after store restart</th>
                    <td>HTTP {data.replicas.replayAfterRestart}</td>
                  </tr>
                </tbody>
              </table>
              <p>
                {data.replicas.observedAt} · named API instances{' '}
                {data.replicas.instancesVerified ? 'verified' : 'unverified'} · expiry and capture
                cleanup {data.replicas.cleanupVerified ? 'verified' : 'incomplete'}
              </p>
            </>
          )}
          <p className="notice">
            The replay experiment runs inside the intentionally unsafe capture lab with dedicated
            Redis state and two stateless quote APIs. Normal business APIs remain single-writer.
            This does not establish BFF refresh coordination, Redis failover, or Kubernetes parity.
            Raw tokens, certificates, proofs and keys are excluded from this view and export.
          </p>
        </>
      )}
    </main>
  );
}
