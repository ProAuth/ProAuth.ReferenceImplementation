import { Fragment, StrictMode, useEffect, useState, type ReactNode } from 'react';
import { createRoot } from 'react-dom/client';
import './styles.css';
import { api, apiResponse, ApiError } from '../../shared/api';

type Product = { id: string; name: string; priceChf: number };
type Catalogue = {
  company: { displayName: string; notice: string };
  products: Product[];
  conferenceControls?: boolean;
};
type Member = {
  id: string;
  organizationId: string;
  costCentre: string;
  roles: string[];
  approvalLimitChf: number;
};
type Purchase = {
  id: string;
  productId: string;
  amountChf: number;
  status: string;
  revision: number;
  requesterId: string;
  approverId: string | null;
};
const chf = new Intl.NumberFormat('de-CH', {
  style: 'currency',
  currency: 'CHF',
  maximumFractionDigits: 0,
});

type ActionResult = {
  state: 'pending' | 'accepted' | 'rejected' | 'error';
  status?: number;
  responseLabel?: string;
  message: string;
};
type HandoffResults = { original?: ActionResult; exchanged?: ActionResult };
function ActionFeedback({
  title,
  responseLabel,
  result,
  idle,
  children,
}: {
  title: string;
  responseLabel: string;
  result?: ActionResult;
  idle: string;
  children?: ReactNode;
}) {
  return (
    <div className={`handoff-result ${result?.state || 'idle'}`} role="status" aria-label={title}>
      <h3>{title}</h3>
      <strong>
        {result?.status
          ? `HTTP ${result.status}`
          : result?.state === 'pending'
            ? 'Running…'
            : result?.state === 'error'
              ? 'Request failed'
              : idle}
      </strong>
      {result?.status && (
        <span className="response-label">{result.responseLabel || responseLabel}</span>
      )}
      <p>{result?.message || 'Run the action above to see its result.'}</p>
      {children}
    </div>
  );
}

function Workplace() {
  const [catalogue, setCatalogue] = useState<Catalogue>();
  const [authenticated, setAuthenticated] = useState(false);
  const [username, setUsername] = useState('');
  const [member, setMember] = useState<Member>();
  const [purchases, setPurchases] = useState<Purchase[]>([]);
  const [busy, setBusy] = useState(false);
  const [demoMode, setDemoMode] = useState(false);
  const [handoffResults, setHandoffResults] = useState<Record<string, HandoffResults>>({});
  const [approvalResults, setApprovalResults] = useState<
    Record<string, ActionResult & { requiresVerification?: boolean }>
  >({});
  const [error, setError] = useState('');
  const [notice, setNotice] = useState(
    new URLSearchParams(location.search).get('assurance') === 'returned'
      ? 'Verification returned. Review the current purchase and explicitly approve again; nothing was submitted automatically.'
      : '',
  );
  const [pending, setPending] = useState<{ productId: string; key: string }>();

  useEffect(() => {
    let active = true;
    async function load() {
      try {
        const config = await api<Catalogue>('/app-config');
        const session = await api<{
          isAuthenticated: boolean;
          nameClaimType: string;
          claims?: { type: string; value: string }[];
        }>('/bff/user');
        if (!active) return;
        setCatalogue(config);
        setAuthenticated(session.isAuthenticated);
        setUsername(
          session.claims?.find((claim) => claim.type === session.nameClaimType)?.value || '',
        );
        if (session.isAuthenticated) {
          const [me, orders] = await Promise.all([
            api<Member>('/api/me'),
            api<Purchase[]>('/api/purchases'),
          ]);
          if (active) {
            setMember(me);
            setPurchases(orders);
          }
        }
      } catch (e) {
        if (active) setError((e as Error).message);
      }
    }
    void load();
    return () => {
      active = false;
    };
  }, []);

  async function act(operation: () => Promise<unknown>, success: string) {
    setBusy(true);
    setError('');
    setNotice('');
    try {
      await operation();
      setPurchases(await api<Purchase[]>('/api/purchases'));
      setNotice(success);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  async function approve(purchase: Purchase) {
    const update = (result: ActionResult & { requiresVerification?: boolean }) =>
      setApprovalResults((previous) => ({ ...previous, [purchase.id]: result }));
    setBusy(true);
    setError('');
    setNotice('');
    update({
      state: 'pending',
      message: 'Checking permission, purchase state and authentication…',
    });
    try {
      const result = await apiResponse<Purchase>(`/api/purchases/${purchase.id}/approve`, {
        revision: purchase.revision,
      });
      setPurchases((previous) => previous.map((p) => (p.id === purchase.id ? result.data : p)));
      update({ state: 'accepted', status: result.status, message: 'Purchase approved.' });
    } catch (e) {
      const requiresVerification = e instanceof ApiError && e.requiresAssurance;
      update({
        state: requiresVerification ? 'rejected' : 'error',
        status: e instanceof ApiError ? e.status : undefined,
        requiresVerification,
        message: requiresVerification
          ? 'This approval needs stronger authentication within the last five minutes.'
          : (e as Error).message === 'fresh_strong_assurance_required'
            ? 'Approval is unavailable until the API can verify authentication strength and freshness. The purchase remains unchanged.'
            : (e as Error).message,
      });
    } finally {
      setBusy(false);
    }
  }
  async function handoff(purchase: Purchase, original = false) {
    const key = original ? 'original' : 'exchanged';
    const update = (result: ActionResult) =>
      setHandoffResults((previous) => ({
        ...previous,
        [purchase.id]: { ...previous[purchase.id], [key]: result },
      }));
    setBusy(true);
    setError('');
    setNotice('');
    update({
      state: 'pending',
      message: original
        ? 'Calling Fulfilment with the original Procurement token…'
        : 'Sending this approved purchase through Procurement…',
    });
    try {
      if (original) {
        const result = await api<{ downstreamStatus: number }>('/lab/original-token', {});
        update({
          state: result.downstreamStatus === 401 ? 'rejected' : 'error',
          status: result.downstreamStatus,
          message:
            result.downstreamStatus === 401
              ? 'Original token rejected at Fulfilment. No purchase changed.'
              : 'Unexpected result from the original-token test. Inspect Protocol Evidence.',
        });
      } else {
        const result = await apiResponse(`/api/purchases/${purchase.id}/handoff`, {
          revision: purchase.revision,
        });
        update({
          state: 'accepted',
          status: result.status,
          message: 'Order handed to Fulfilment.',
        });
      }
    } catch (e) {
      update({
        state: 'error',
        status: e instanceof ApiError ? e.status : undefined,
        responseLabel: original ? 'Workplace test endpoint response' : 'Handoff response',
        message: (e as Error).message,
      });
    } finally {
      setBusy(false);
    }
  }
  function request(productId: string) {
    const attempt =
      pending?.productId === productId ? pending : { productId, key: crypto.randomUUID() };
    setPending(attempt);
    void act(async () => {
      await api('/api/purchases', { productId }, attempt.key);
      setPending(undefined);
    }, 'Purchase request submitted.');
  }

  const canRequest = member?.roles.includes('requester');
  const canReview = member?.roles.includes('approver');
  const canApprove = (p: Purchase) =>
    member &&
    canReview &&
    p.status === 'Requested' &&
    p.requesterId !== member.id &&
    p.amountChf <= member.approvalLimitChf;
  const canHandOff = (p: Purchase) =>
    member && canReview && p.status === 'Approved' && p.approverId === member.id;
  const sections = canReview
    ? [
        {
          title: 'Purchases to approve',
          rows: purchases.filter(canApprove),
          empty: 'No purchases are waiting for your approval.',
        },
        { title: 'Approved purchases', rows: purchases.filter((p) => p.status === 'Approved') },
        {
          title: 'Other purchases',
          rows: purchases.filter((p) => !canApprove(p) && p.status !== 'Approved'),
        },
      ]
    : [{ title: 'Your purchases', rows: purchases, empty: 'You have no purchases yet.' }];

  return (
    <div className="workspace">
      <aside>
        <a className="brand" href="/">
          {catalogue?.company.displayName || 'Workplace'}
        </a>
        <span className="eyebrow">WORKPLACE</span>
        {member && (
          <nav aria-label="Main">
            <a className="selected" href="#purchases">
              Procurement <span>01</span>
            </a>
            {canRequest && (
              <a href="#catalogue">
                Equipment catalogue <span>02</span>
              </a>
            )}
          </nav>
        )}
        <p className="fiction">
          {catalogue?.company.notice || 'Mervikon Group · demonstration and training'}
        </p>
      </aside>
      <main>
        <header>
          <span>Procurement / Workplace</span>
          {authenticated ? (
            <div className="account-actions">
              {catalogue?.conferenceControls && (
                <button
                  className="demo-toggle"
                  role="switch"
                  aria-checked={demoMode}
                  disabled={busy}
                  onClick={() => setDemoMode(!demoMode)}
                >
                  Demo mode <span aria-hidden="true">{demoMode ? 'On' : 'Off'}</span>
                </button>
              )}
              <span className="signed-in-user">
                Signed in as <strong>{username || member?.id || 'authenticated user'}</strong>
              </span>
              <a href="/account-security">Account security</a>
              <a className="button" href="/bff/account/logout">
                Sign out
              </a>
            </div>
          ) : (
            <a className="button" href="/bff/account/login?returnUrl=%2F">
              Sign in
            </a>
          )}
        </header>
        <section className="intro">
          <p className="eyebrow">PURCHASING</p>
          <h1>
            Equipment for
            <br />
            the work ahead.
          </h1>
          {canReview && <p>Review purchase requests and hand approved orders to the supplier.</p>}
          {canRequest && <p>Request equipment from the catalogue and track your purchases.</p>}
          {member && (
            <p className="identity">
              {member.id} · {member.organizationId} · {member.costCentre}
            </p>
          )}
        </section>
        {error && (
          <p className="message error" role="alert">
            {error === 'membership_required'
              ? 'Your account has no purchasing assignment. If you are using a demo account, ask the operator to run the demo membership setup, then reload this page.'
              : error === 'fresh_strong_assurance_required'
                ? 'Approval is unavailable until the API can verify authentication strength and freshness. The purchase remains unchanged.'
                : error}
          </p>
        )}
        {notice && (
          <p className="message" role="status">
            {notice}
          </p>
        )}
        {!authenticated && (
          <p className="message">
            Sign in to request equipment or review purchases assigned to you.
          </p>
        )}
        {member && (
          <>
            <div id="purchases">
              {sections
                .filter((section) => section.empty || section.rows.length)
                .map((section) => (
                  <section key={section.title}>
                    <div className="section-heading">
                      <h2>{section.title}</h2>
                      <span>{section.rows.length} visible</span>
                    </div>
                    {section.rows.length === 0 ? (
                      <p className="empty">{section.empty}</p>
                    ) : (
                      <div className="table-scroll">
                        <table>
                          <thead>
                            <tr>
                              <th>Equipment</th>
                              <th>Value</th>
                              <th>Status</th>
                              <th>
                                <span className="sr-only">Action</span>
                              </th>
                            </tr>
                          </thead>
                          <tbody>
                            {section.rows.map((p) => (
                              <Fragment key={p.id}>
                                <tr>
                                  <td>
                                    {catalogue?.products.find((x) => x.id === p.productId)?.name ||
                                      p.productId}
                                    <small>
                                      {p.id.slice(0, 8)} · revision {p.revision}
                                    </small>
                                  </td>
                                  <td>{chf.format(p.amountChf)}</td>
                                  <td>{p.status}</td>
                                  <td>
                                    {canApprove(p) && (
                                      <button disabled={busy} onClick={() => void approve(p)}>
                                        Approve
                                      </button>
                                    )}
                                    {canHandOff(p) && (
                                      <div className="handoff-actions">
                                        {demoMode && catalogue?.conferenceControls && (
                                          <button
                                            className="original-token"
                                            disabled={busy}
                                            onClick={() => void handoff(p, true)}
                                          >
                                            Try original token
                                          </button>
                                        )}
                                        <button disabled={busy} onClick={() => void handoff(p)}>
                                          Hand to supplier
                                        </button>
                                      </div>
                                    )}
                                  </td>
                                </tr>
                                {approvalResults[p.id] && (
                                  <tr data-approval-feedback={p.id} className="handoff-feedback">
                                    <td colSpan={4}>
                                      <ActionFeedback
                                        title="Purchase approval"
                                        responseLabel="Procurement response"
                                        result={approvalResults[p.id]}
                                        idle="Not submitted"
                                      >
                                        {approvalResults[p.id].requiresVerification && (
                                          <div className="approval-followup">
                                            <a className="button" href="/step-up">
                                              Verify before approving
                                            </a>
                                            <span>
                                              No purchase has changed. After verification, review
                                              and confirm again.
                                            </span>
                                          </div>
                                        )}
                                      </ActionFeedback>
                                    </td>
                                  </tr>
                                )}
                                {((demoMode && catalogue?.conferenceControls && canHandOff(p)) ||
                                  handoffResults[p.id]?.exchanged) && (
                                  <tr data-purchase-feedback={p.id} className="handoff-feedback">
                                    <td colSpan={4}>
                                      <div
                                        className={`handoff-results ${demoMode ? 'comparison' : ''}`}
                                      >
                                        {demoMode && (
                                          <ActionFeedback
                                            title="Original Procurement token"
                                            responseLabel="Fulfilment response"
                                            result={handoffResults[p.id]?.original}
                                            idle="Not tested"
                                          />
                                        )}
                                        <ActionFeedback
                                          title={
                                            demoMode
                                              ? 'Policy-approved token exchange'
                                              : 'Hand to supplier'
                                          }
                                          responseLabel="Handoff response"
                                          result={handoffResults[p.id]?.exchanged}
                                          idle="Not submitted"
                                        />
                                      </div>
                                    </td>
                                  </tr>
                                )}
                              </Fragment>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    )}
                  </section>
                ))}
            </div>
            {canRequest && (
              <section id="catalogue">
                <div className="section-heading">
                  <h2>Equipment catalogue</h2>
                  <span>CHF · demonstration fixtures</span>
                </div>
                {catalogue?.products.map((p, index) => (
                  <article className="product" key={p.id}>
                    <span className="ordinal">0{index + 1}</span>
                    <div>
                      <h3>{p.name}</h3>
                      <p>
                        {p.priceChf > 20000
                          ? 'Approval requires verified MFA or phishing-resistant authentication within five minutes.'
                          : 'Standard approval · separation of duties applies.'}
                      </p>
                    </div>
                    <strong>{chf.format(p.priceChf)}</strong>
                    <button disabled={busy} onClick={() => request(p.id)}>
                      {pending?.productId === p.id ? 'Retry request' : 'Request'}
                    </button>
                  </article>
                ))}
              </section>
            )}
          </>
        )}
        <footer>
          Mervikon Group <span>Authentication via ProAuth BFF</span>
        </footer>
      </main>
    </div>
  );
}
createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <Workplace />
  </StrictMode>,
);
