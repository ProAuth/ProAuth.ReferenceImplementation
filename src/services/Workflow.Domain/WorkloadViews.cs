using Procurement.Domain;

namespace Workflow.Domain;

public sealed record Partition(string OrganizationId, string CostCentre);
public sealed record DispatchItem(Guid PurchaseId, int Revision);
public sealed record Receipt(Guid PurchaseId, string BuyerOrganizationId, string CostCentre,
    string SupplierOrganizationId, decimal AmountChf, int SourceRevision)
{
    public static Receipt FromReceived(SupplierOrder order)
    {
        if (order.Status != "Received")
            throw new PolicyException("receipt_required");
        var p = order.Purchase;
        return new(p.PurchaseId, p.BuyerOrganizationId, p.CostCentre, p.SupplierOrganizationId, p.AmountChf, order.Revision);
    }
}
public sealed record ReconciliationItem(Guid PurchaseId, decimal AmountChf, string Status);
