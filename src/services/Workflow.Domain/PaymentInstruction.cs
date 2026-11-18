namespace Workflow.Domain;

public sealed record PaymentInstruction(Guid Id, Guid PurchaseId, string BuyerOrganizationId, string CostCentre,
    string RecipientOrganizationId, decimal AmountChf, string Status, int Revision, string? ReleasedBy);
