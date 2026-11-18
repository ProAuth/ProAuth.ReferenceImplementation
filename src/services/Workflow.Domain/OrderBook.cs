using Procurement.Domain;

namespace Workflow.Domain;

/// <summary>One owning process; resettable business state, never a security-state store.</summary>
public sealed class OrderBook
{
    private readonly Dictionary<Guid, SupplierOrder> orders = [];
    private readonly object gate = new();

    // The API has established Procurement's actor identity. Independently check that the
    // delegated person is the approver and that the supplied order fits that person's authority.
    public SupplierOrder Import(Member buyer, ApprovedOrder purchase)
    {
        Require(buyer.Roles.Contains("approver") && buyer.Id == purchase.ApproverId && buyer.Id != purchase.RequesterId, "approver_required");
        Require(buyer.OrganizationId == purchase.BuyerOrganizationId && buyer.CostCentre == purchase.CostCentre, "organization_denied");
        Require(purchase.AmountChf > 0 && purchase.AmountChf <= buyer.ApprovalLimitChf && purchase.PurchaseRevision >= 2, "invalid_approved_purchase");
        Require(purchase.SupplierOrganizationId == "supplier-northstar" && purchase.WarehouseId == "zurich", "supplier_assignment_denied");
        lock (gate)
        {
            if (orders.TryGetValue(purchase.PurchaseId, out var existing))
            {
                Require(existing.Purchase == purchase, "idempotency_conflict");
                return existing;
            }
            return orders[purchase.PurchaseId] = new(purchase, "Assigned", 1);
        }
    }

    public SupplierOrder[] List(Member member)
    {
        lock (gate)
            return orders.Values.Where(o => CanRead(member, o)).ToArray();
    }
    public Receipt[] Receipts(IReadOnlySet<Partition> allowed)
    {
        lock (gate)
            return orders.Values.Where(o => o.Status == "Received" && InPartition(o, allowed)).Select(Receipt.FromReceived).ToArray();
    }

    public DispatchItem[] DispatchQueue(IReadOnlySet<Partition> allowed)
    {
        lock (gate)
            return orders.Values.Where(o => o.Status == "Accepted" && InPartition(o, allowed))
                .Select(o => new DispatchItem(o.Purchase.PurchaseId, o.Revision)).ToArray();
    }

    public DispatchItem Dispatch(IReadOnlySet<Partition> allowed, Guid id, int revision)
    {
        lock (gate)
        {
            Require(orders.TryGetValue(id, out var order) && InPartition(order, allowed), "order_not_found");
            var updated = Move(order!, revision, "dispatch");
            return new(id, updated.Revision);
        }
    }

    private static bool InPartition(SupplierOrder order, IReadOnlySet<Partition> allowed) =>
        allowed.Contains(new(order.Purchase.BuyerOrganizationId, order.Purchase.CostCentre));

    public SupplierOrder Transition(Member member, Guid id, int revision, string action)
    {
        lock (gate)
        {
            Require(orders.TryGetValue(id, out var order) && CanRead(member, order), "order_not_found");
            var allowed = action switch
            {
                "accept" => member.Roles.Contains("supplier") && member.OrganizationId == order!.Purchase.SupplierOrganizationId,
                "dispatch" => member.Roles.Contains("supplier") && member.OrganizationId == order!.Purchase.SupplierOrganizationId,
                "receive" => member.Roles.Contains("warehouse") && member.OrganizationId == "logistics-alpine" && member.CostCentre == order!.Purchase.WarehouseId,
                _ => false
            };
            Require(allowed, "operation_denied");
            return Move(order!, revision, action);
        }
    }

    // Optimistic revision checks prevent stale UI or worker requests from overwriting newer
    // state. This is business concurrency control, separate from OAuth sender-proof replay.
    private SupplierOrder Move(SupplierOrder order, int revision, string action)
    {
        var states = action switch
        {
            "accept" => ("Assigned", "Accepted"),
            "dispatch" => ("Accepted", "Dispatched"),
            "receive" => ("Dispatched", "Received"),
            _ => ("", "")
        };
        Require(order.Revision == revision && order.Status == states.Item1, "revision_conflict");
        return orders[order.Purchase.PurchaseId] = order with
        {
            Status = states.Item2,
            Revision = order.Revision + 1
        };
    }
    private static bool CanRead(Member m, SupplierOrder o) =>
        (m.Roles.Contains("supplier") && m.OrganizationId == o.Purchase.SupplierOrganizationId) ||
        (m.Roles.Contains("warehouse") && m.OrganizationId == "logistics-alpine" && m.CostCentre == o.Purchase.WarehouseId) ||
        (m.OrganizationId == o.Purchase.BuyerOrganizationId && m.CostCentre == o.Purchase.CostCentre &&
            (m.Roles.Contains("approver") || (m.Roles.Contains("requester") && m.Id == o.Purchase.RequesterId)));
    private static void Require(bool allowed, string reason)
    {
        if (!allowed)
            throw new PolicyException(reason);
    }
}
