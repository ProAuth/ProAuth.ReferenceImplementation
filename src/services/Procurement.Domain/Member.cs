namespace Procurement.Domain;

public sealed record Member(string Id, string OrganizationId, string CostCentre, string[] Roles, decimal ApprovalLimitChf);
