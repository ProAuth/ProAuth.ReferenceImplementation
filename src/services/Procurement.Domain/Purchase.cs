namespace Procurement.Domain;

public sealed record Purchase(Guid Id, string OrganizationId, string CostCentre, string RequesterId,
    string ProductId, decimal AmountChf, string Status, int Revision, string? ApproverId);
