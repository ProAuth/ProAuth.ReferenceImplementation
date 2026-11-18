using Procurement.Domain;
using Xunit;

namespace Procurement.Domain.Tests;

public sealed class PurchasePolicyTests
{
    private static readonly Member Elena = new("elena", "buyer-manufacturing", "engineering", ["requester"], 0);
    private static readonly Member Amir = new("amir", "buyer-manufacturing", "engineering", ["approver"], 50_000);
    private static PurchaseBook Create() => new([new("station", "Station", 18_500), new("coffee", "Coffee", 24_900)]);

    [Fact]
    public void RequesterCannotApproveOwnOrderEvenWithBothRoles()
    {
        var book = Create();
        var order = book.Request(Elena, "station", "one");
        Assert.Equal("separation_of_duties", Assert.Throws<PolicyException>(() =>
            book.Approve(Elena with
            {
                Roles = ["requester", "approver"],
                ApprovalLimitChf = 50_000
            }, order.Id, 1, true)).Message);
    }

    [Fact]
    public void AnotherOrganizationCannotListOrApprovePurchase()
    {
        var book = Create();
        var order = book.Request(Elena, "station", "one");
        var researcher = Amir with
        {
            OrganizationId = "buyer-research"
        };
        Assert.Empty(book.List(researcher));
        Assert.Equal("purchase_not_found", Assert.Throws<PolicyException>(() => book.Approve(researcher, order.Id, 1, true)).Message);
    }

    [Fact]
    public void RetriesDoNotDuplicatePurchaseAndChangedPayloadConflicts()
    {
        var book = Create();
        var order = book.Request(Elena, "station", "one");
        Assert.Equal(order, book.Request(Elena, "station", "one"));
        Assert.Equal("idempotency_conflict", Assert.Throws<PolicyException>(() => book.Request(Elena, "coffee", "one")).Message);
        Assert.Single(book.List(Elena));
    }

    [Fact]
    public void ApprovalRequiresFreshAssuranceOnlyAboveThreshold()
    {
        var book = Create();
        var station = book.Request(Elena, "station", "one");
        Assert.Equal("Approved", book.Approve(Amir, station.Id, 1, false).Status);
        var coffee = book.Request(Elena, "coffee", "two");
        Assert.Equal("fresh_strong_assurance_required", Assert.Throws<PolicyException>(() => book.Approve(Amir, coffee.Id, 1, false)).Message);
        Assert.Equal("Approved", book.Approve(Amir, coffee.Id, 1, true).Status);
    }

    [Fact]
    public void ConcurrentApprovalsOnlyCommitOnce()
    {
        var book = Create();
        var order = book.Request(Elena, "station", "one");
        var successes = 0;
        Parallel.For(0, 8, _ =>
        {
            try
            {
                book.Approve(Amir, order.Id, 1, false);
                Interlocked.Increment(ref successes);
            }
            catch (PolicyException e) when (e.Message == "revision_conflict") { }
        });
        Assert.Equal(1, successes);
        Assert.Equal(2, Assert.Single(book.List(Amir)).Revision);
    }

    [Fact]
    public void LimitsAndCostCentreRemainAuthoritative()
    {
        var book = Create();
        var order = book.Request(Elena, "station", "one");
        Assert.Equal("approval_limit", Assert.Throws<PolicyException>(() => book.Approve(Amir with { ApprovalLimitChf = 100 }, order.Id, 1, true)).Message);
        Assert.Equal("purchase_not_found", Assert.Throws<PolicyException>(() => book.Approve(Amir with { CostCentre = "lab" }, order.Id, 1, true)).Message);
    }
}
