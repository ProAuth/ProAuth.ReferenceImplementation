namespace Workflow.Domain;

public sealed record ApprovedOrder(Guid PurchaseId, string BuyerOrganizationId, string CostCentre, string RequesterId,
    string ProductId, decimal AmountChf, int PurchaseRevision, string ApproverId, string SupplierOrganizationId, string WarehouseId);
