namespace Workflow.Domain;

public sealed record SupplierOrder(ApprovedOrder Purchase, string Status, int Revision);
