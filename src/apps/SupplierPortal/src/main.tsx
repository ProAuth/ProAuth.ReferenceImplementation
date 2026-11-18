import { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { api, money, type Member, type Catalogue } from '../../shared/api';
import { Shell } from '../../shared/Shell';
type Order = {
  purchase: {
    purchaseId: string;
    productId: string;
    amountChf: number;
    buyerOrganizationId: string;
  };
  status: string;
  revision: number;
};
function App() {
  const [catalogue, setCatalogue] = useState<Catalogue>();
  const [member, setMember] = useState<Member>();
  const [authenticated, setAuthenticated] = useState(false);
  const [orders, setOrders] = useState<Order[]>([]);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    void (async () => {
      try {
        setCatalogue(await api('/app-config'));
        const session = await api<{ isAuthenticated: boolean }>('/bff/user');
        setAuthenticated(session.isAuthenticated);
        if (session.isAuthenticated) {
          setMember(await api('/api/me'));
          setOrders(await api('/api/orders'));
        }
      } catch (e) {
        setError((e as Error).message);
      }
    })();
  }, []);
  async function act(order: Order, action: string) {
    setBusy(true);
    setError('');
    try {
      await api(`/api/orders/${order.purchase.purchaseId}/${action}`, { revision: order.revision });
      setOrders(await api('/api/orders'));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <Shell title="Supplier Portal" {...{ catalogue, member, authenticated, error }}>
      <div className="section-heading">
        <h2>Assigned orders</h2>
        <span>{orders.length} visible</span>
      </div>
      <div className="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Equipment / buyer</th>
              <th>Value</th>
              <th>Status</th>
              <th>Action</th>
            </tr>
          </thead>
          <tbody>
            {orders.map((order) => (
              <tr key={order.purchase.purchaseId}>
                <td>
                  {catalogue?.products.find((p) => p.id === order.purchase.productId)?.name ||
                    order.purchase.productId}
                  <small>
                    {
                      catalogue?.organizations.find(
                        (o) => o.id === order.purchase.buyerOrganizationId,
                      )?.displayName
                    }{' '}
                    · revision {order.revision}
                  </small>
                </td>
                <td>{money(order.purchase.amountChf)}</td>
                <td>{order.status}</td>
                <td>
                  {member?.roles.includes('supplier') &&
                    ['Assigned', 'Accepted'].includes(order.status) && (
                      <button
                        disabled={busy}
                        onClick={() =>
                          void act(order, order.status === 'Assigned' ? 'accept' : 'dispatch')
                        }
                      >
                        {order.status === 'Assigned' ? 'Accept order' : 'Dispatch'}
                      </button>
                    )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {authenticated && member && orders.length === 0 && (
        <p className="empty">
          No orders are assigned to your organization. A buyer must approve and hand off a purchase
          first.
        </p>
      )}
    </Shell>
  );
}
createRoot(document.getElementById('root')!).render(<App />);
