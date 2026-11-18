namespace Workflow.Domain;

public sealed record ReleasePayment(int Revision, decimal AmountChf, string RecipientOrganizationId);
