using Procurement.Domain;
using Workflow.Domain;
using Xunit;

namespace Workflow.Domain.Tests;

public class WorkflowTests
{
    private static readonly Member Buyer = new("amir", "buyer-manufacturing", "engineering", ["approver"], 50000);
    private static readonly Member Supplier = new("sam", "supplier-northstar", "dispatch", ["supplier"], 0);
    private static readonly Member Warehouse = new("lea", "logistics-alpine", "zurich", ["warehouse"], 0);
    private static readonly Member Finance = new("priya", "buyer-manufacturing", "engineering", ["finance"], 50000);
    private static ApprovedOrder Purchase() => new(Guid.NewGuid(), "buyer-manufacturing", "engineering", "elena", "station", 18500, 2, "amir", "supplier-northstar", "zurich");

    [Fact]
    public void ImportIsIdempotentAndRejectsChangedAmounts()
    {
        var book = new OrderBook();
        var p = Purchase();
        var first = book.Import(Buyer, p);
        Assert.Equal(first, book.Import(Buyer, p));
        Assert.Equal("idempotency_conflict", Assert.Throws<PolicyException>(() => book.Import(Buyer, p with { AmountChf = 19000 })).Message);
    }
    [Fact]
    public void UnassignedSupplierCannotReadOrMutateOrder()
    {
        var book = new OrderBook();
        var p = Purchase();
        book.Import(Buyer, p);
        var stranger = Supplier with
        {
            OrganizationId = "supplier-unapproved"
        };
        Assert.Empty(book.List(stranger));
        Assert.Equal("order_not_found", Assert.Throws<PolicyException>(() => book.Transition(stranger, p.PurchaseId, 1, "accept")).Message);
        Assert.Equal("Assigned", Assert.Single(book.List(Supplier)).Status);
    }
    [Fact]
    public void WorkflowRequiresCorrectRoleLocationAndRevision()
    {
        var book = new OrderBook();
        var p = Purchase();
        book.Import(Buyer, p);
        Assert.Throws<PolicyException>(() => book.Transition(Supplier, p.PurchaseId, 1, "receive"));
        book.Transition(Supplier, p.PurchaseId, 1, "accept");
        book.Transition(Supplier, p.PurchaseId, 2, "dispatch");
        Assert.Throws<PolicyException>(() => book.Transition(Warehouse with { CostCentre = "other" }, p.PurchaseId, 3, "receive"));
        Assert.Equal("Received", book.Transition(Warehouse, p.PurchaseId, 3, "receive").Status);
        Assert.Throws<PolicyException>(() => book.Transition(Warehouse, p.PurchaseId, 3, "receive"));
    }
    [Fact]
    public void NonReceivedOrderCannotCreatePayment()
    {
        var book = new PaymentBook();
        Assert.Throws<PolicyException>(() => Receipt.FromReceived(new(Purchase(), "Dispatched", 3)));
    }
    [Fact]
    public void SupplierWithWarehouseRoleCannotReceiveForAnotherOrganization()
    {
        var book = new OrderBook();
        var purchase = Purchase();
        book.Import(Buyer, purchase);
        book.Transition(Supplier, purchase.PurchaseId, 1, "accept");
        book.Transition(Supplier, purchase.PurchaseId, 2, "dispatch");
        var mixedRoleSupplier = Supplier with
        {
            Roles = ["supplier", "warehouse"],
            CostCentre = "zurich"
        };

        Assert.Equal("operation_denied", Assert.Throws<PolicyException>(() =>
            book.Transition(mixedRoleSupplier, purchase.PurchaseId, 3, "receive")).Message);
        Assert.Equal("Dispatched", Assert.Single(book.List(Supplier)).Status);
        Assert.Equal("Received", book.Transition(Warehouse, purchase.PurchaseId, 3, "receive").Status);
    }
    [Fact]
    public void PaymentRequiresExactTransactionFreshAssuranceAndMembership()
    {
        var book = new PaymentBook();
        var source = new SupplierOrder(Purchase(), "Received", 4);
        var p = book.Prepare(Receipt.FromReceived(source));
        var exact = new ReleasePayment(p.Revision, p.AmountChf, p.RecipientOrganizationId);
        Assert.Throws<PolicyException>(() => book.Release(Finance with { OrganizationId = "buyer-research" }, p.Id, exact, true));
        Assert.Throws<PolicyException>(() => book.Release(Finance, p.Id, exact with { AmountChf = 1 }, true));
        Assert.Throws<PolicyException>(() => book.Release(Finance, p.Id, exact with { RecipientOrganizationId = "attacker" }, true));
        Assert.Equal("fresh_strong_assurance_required", Assert.Throws<PolicyException>(() => book.Release(Finance, p.Id, exact, false)).Message);
        Assert.Equal("Pending", Assert.Single(book.List(Finance)).Status);
        Assert.Equal(p, book.Prepare(Receipt.FromReceived(source)));
        Assert.Equal("Released", book.Release(Finance, p.Id, exact, true).Status);
        Assert.Throws<PolicyException>(() => book.Release(Finance, p.Id, exact, true));
    }
    [Fact]
    public void ConcurrentReleaseCommitsExactlyOnce()
    {
        var book = new PaymentBook();
        var p = book.Prepare(Receipt.FromReceived(new(Purchase(), "Received", 4)));
        var successes = 0;
        Parallel.For(0, 8, _ => { try { book.Release(Finance, p.Id, new(1, p.AmountChf, p.RecipientOrganizationId), true); Interlocked.Increment(ref successes); } catch (PolicyException e) when (e.Message == "revision_conflict") { } });
        Assert.Equal(1, successes);
    }
}
