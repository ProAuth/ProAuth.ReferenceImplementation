import { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { api, ApiError, money, type Member, type Catalogue } from '../../shared/api';
import { Shell } from '../../shared/Shell';
type Payment = {
  purchaseId: string;
  id: string;
  recipientOrganizationId: string;
  amountChf: number;
  status: string;
  revision: number;
};
function App() {
  const [catalogue, setCatalogue] = useState<Catalogue>();
  const [member, setMember] = useState<Member>();
  const [authenticated, setAuthenticated] = useState(false);
  const [needsVerification, setNeedsVerification] = useState(false);
  const returned = new URLSearchParams(location.search).get('assurance') === 'returned';
  const [payments, setPayments] = useState<Payment[]>([]);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    void (async () => {
      try {
        setCatalogue(await api('/app-config'));
        const s = await api<{ isAuthenticated: boolean }>('/bff/user');
        setAuthenticated(s.isAuthenticated);
        if (s.isAuthenticated) {
          setMember(await api('/api/me'));
          setPayments(await api('/api/payments'));
        }
      } catch (e) {
        setError((e as Error).message);
      }
    })();
  }, []);
  async function act(path: string, body: unknown) {
    setBusy(true);
    setError('');
    setNeedsVerification(false);
    try {
      await api(path, body);
      setPayments(await api('/api/payments'));
    } catch (e) {
      if (e instanceof ApiError && e.requiresAssurance) {
        setNeedsVerification(true);
        setError('This release needs stronger authentication within the last five minutes.');
      } else setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <Shell title="Finance Desk" {...{ catalogue, member, authenticated, error }}>
      <div className="section-heading">
        <h2>Payment instructions</h2>
        <button
          disabled={busy || !member?.roles.includes('finance')}
          onClick={() => void act('/api/payments/synchronize', {})}
        >
          Import received orders
        </button>
      </div>
      {needsVerification && (
        <p className="message">
          <a className="button" href="/step-up">
            Verify before releasing
          </a>{' '}
          No payment has changed. Review and confirm again after verification.
        </p>
      )}
      {returned && (
        <p className="message" role="status">
          Verification returned. Review the purchase reference, recipient, amount and revision, then
          explicitly release again. Nothing was submitted automatically.
        </p>
      )}
      <p>
        <a href="/account-security">Manage enrolled authentication methods</a>
      </p>
      <p>Internal demonstration records. No bank or payment provider is connected.</p>
      <div className="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Purchase reference</th>
              <th>Recipient</th>
              <th>Amount</th>
              <th>Status</th>
              <th>Action</th>
            </tr>
          </thead>
          <tbody>
            {payments.map((p) => (
              <tr key={p.id}>
                <th scope="row">{p.purchaseId}</th>
                <td>
                  {catalogue?.organizations.find((o) => o.id === p.recipientOrganizationId)
                    ?.displayName || p.recipientOrganizationId}
                  <small>Revision {p.revision}</small>
                </td>
                <td>{money(p.amountChf)}</td>
                <td>{p.status}</td>
                <td>
                  {p.status === 'Pending' && (
                    <button
                      aria-label={`Release purchase ${p.purchaseId}`}
                      disabled={busy}
                      onClick={() =>
                        void act(`/api/payments/${p.id}/release`, {
                          revision: p.revision,
                          amountChf: p.amountChf,
                          recipientOrganizationId: p.recipientOrganizationId,
                        })
                      }
                    >
                      Release
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="message">
        Release requires MFA or phishing-resistant authentication within five minutes, verified by
        Treasury. Authentication does not replace role, amount, recipient or revision checks.
      </p>
    </Shell>
  );
}
createRoot(document.getElementById('root')!).render(<App />);
