using Procurement.Domain;

namespace Workflow.Domain;

// Internal payment instructions only; no banking integration. API token/assurance checks happen
// before entry here. Business permissions and the exact confirmed transaction are checked again
// under the write lock so a stale amount, recipient or revision cannot be released.
public sealed class PaymentBook
{
    private readonly Dictionary<Guid, PaymentInstruction> payments = [];
    private readonly Dictionary<Guid, Receipt> sources = [];
    private readonly object gate = new();
    public PaymentInstruction Prepare(Receipt p)
    {
        lock (gate)
        {
            if (sources.TryGetValue(p.PurchaseId, out var source))
            {
                Require(source == p, "source_revision_conflict");
                return payments[p.PurchaseId];
            }
            sources[p.PurchaseId] = p;
            return payments[p.PurchaseId] = new(Guid.NewGuid(), p.PurchaseId, p.BuyerOrganizationId, p.CostCentre,
                p.SupplierOrganizationId, p.AmountChf, "Pending", 1, null);
        }
    }
    public PaymentInstruction[] List(Member member)
    {
        Require(member.Roles.Contains("finance"), "finance_required");
        lock (gate)
            return payments.Values.Where(p => p.BuyerOrganizationId == member.OrganizationId && p.CostCentre == member.CostCentre).ToArray();
    }
    public ReconciliationItem[] Reconcile(IReadOnlySet<Partition> allowed)
    {
        lock (gate)
            return payments.Values.Where(p => allowed.Contains(new(p.BuyerOrganizationId, p.CostCentre)))
                .Select(p => new ReconciliationItem(p.PurchaseId, p.AmountChf, p.Status)).ToArray();
    }
    public PaymentInstruction CheckRelease(Member member, Guid id, ReleasePayment expected)
    {
        Require(member.Roles.Contains("finance"), "finance_required");
        lock (gate)
        {
            var payment = payments.Values.SingleOrDefault(p => p.Id == id && p.BuyerOrganizationId == member.OrganizationId && p.CostCentre == member.CostCentre);
            Require(payment is not null, "payment_not_found");
            Require(payment!.AmountChf <= member.ApprovalLimitChf, "approval_limit");
            Require(payment.Status == "Pending" && payment.Revision == expected.Revision && payment.AmountChf == expected.AmountChf &&
                payment.RecipientOrganizationId == expected.RecipientOrganizationId, "revision_conflict");
            return payment;
        }
    }
    public PaymentInstruction Release(Member member, Guid id, ReleasePayment expected, bool freshStrongAssurance)
    {
        lock (gate)
        {
            var payment = CheckRelease(member, id, expected);
            Require(freshStrongAssurance, "fresh_strong_assurance_required");
            return payments[payment.PurchaseId] = payment with
            {
                Status = "Released",
                Revision = payment.Revision + 1,
                ReleasedBy = member.Id
            };
        }
    }
    private static void Require(bool allowed, string reason)
    {
        if (!allowed)
            throw new PolicyException(reason);
    }
}
