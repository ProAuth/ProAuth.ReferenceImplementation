namespace Procurement.Domain;

/// <summary>One process owns this resettable business aggregate. Identity validation belongs to the API.</summary>
// Business authorization starts after token validation. This aggregate knows stable people,
// organizations and spending limits, not OAuth claims. The in-memory lock makes state/revision
// checks and mutation atomic within the owning process; data resets when the process restarts.
public sealed class PurchaseBook(IEnumerable<Product> products)
{
    private readonly Dictionary<string, Product> catalogue = products.ToDictionary(p => p.Id, StringComparer.Ordinal);
    private readonly Dictionary<Guid, Purchase> purchases = [];
    private readonly Dictionary<(string, string), (string Payload, Purchase Result)> requests = [];
    private readonly object gate = new();

    public IReadOnlyList<Product> Products => catalogue.Values.ToArray();

    public IReadOnlyList<Purchase> List(Member member)
    {
        Require(member.Roles.Contains("requester") || member.Roles.Contains("approver"), "role_required");
        lock (gate)
            return purchases.Values.Where(p => p.OrganizationId == member.OrganizationId &&
                p.CostCentre == member.CostCentre &&
                (p.RequesterId == member.Id || member.Roles.Contains("approver"))).ToArray();
    }

    public Purchase Request(Member member, string productId, string idempotencyKey)
    {
        Require(member.Roles.Contains("requester"), "requester_required");
        Require(!string.IsNullOrWhiteSpace(idempotencyKey) && idempotencyKey.Length <= 128, "invalid_idempotency_key");
        Require(!string.IsNullOrWhiteSpace(productId) && catalogue.ContainsKey(productId), "unknown_product");
        lock (gate)
        {
            var key = (member.Id, idempotencyKey);
            var payload = $"{member.OrganizationId}|{member.CostCentre}|{productId}";
            if (requests.TryGetValue(key, out var previous))
            {
                Require(previous.Payload == payload, "idempotency_conflict");
                return previous.Result;
            }
            var purchase = new Purchase(Guid.NewGuid(), member.OrganizationId, member.CostCentre,
                member.Id, productId, catalogue[productId].PriceChf, "Requested", 1, null);
            purchases.Add(purchase.Id, purchase);
            requests.Add(key, (payload, purchase));
            return purchase;
        }
    }

    // Read-only preflight. Approval repeats every business check under the write lock.
    public Purchase CheckApproval(Member member, Guid id, int expectedRevision)
    {
        Require(member.Roles.Contains("approver"), "approver_required");
        lock (gate)
        {
            Require(purchases.TryGetValue(id, out var purchase) && purchase.OrganizationId == member.OrganizationId &&
                purchase.CostCentre == member.CostCentre, "purchase_not_found");
            Require(purchase!.RequesterId != member.Id, "separation_of_duties");
            Require(purchase.AmountChf <= member.ApprovalLimitChf, "approval_limit");
            Require(purchase.Revision == expectedRevision && purchase.Status == "Requested", "revision_conflict");
            return purchase;
        }
    }

    public Purchase Approve(Member member, Guid id, int expectedRevision, bool trustedFreshStrongAssurance)
    {
        lock (gate)
        {
            var purchase = CheckApproval(member, id, expectedRevision);
            Require(purchase.AmountChf <= 20_000 || trustedFreshStrongAssurance, "fresh_strong_assurance_required");
            var approved = purchase with
            {
                Status = "Approved",
                Revision = purchase.Revision + 1,
                ApproverId = member.Id
            };
            purchases[id] = approved;
            return approved;
        }
    }

    public Purchase ApprovedForHandoff(Member member, Guid id, int expectedRevision)
    {
        Require(member.Roles.Contains("approver"), "approver_required");
        lock (gate)
        {
            Require(purchases.TryGetValue(id, out var purchase) && purchase.OrganizationId == member.OrganizationId &&
                purchase.CostCentre == member.CostCentre, "purchase_not_found");
            Require(purchase!.ApproverId == member.Id && purchase.Status == "Approved" && purchase.Revision == expectedRevision, "revision_conflict");
            return purchase;
        }
    }

    private static void Require(bool allowed, string code)
    {
        if (!allowed)
            throw new PolicyException(code);
    }
}
