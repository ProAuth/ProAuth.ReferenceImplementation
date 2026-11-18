using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Procurement.Domain;
using Common.Security;
using Workflow.Domain;
using Xunit;

public sealed class AssuranceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
    private static ClaimsPrincipal User(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));
    [Theory]
    [InlineData("https://proauth.net/acr/mfa", 0, true)]
    [InlineData("phr", 300, true)]
    [InlineData("phr", 301, false)]
    [InlineData("phr", -1, false)]
    [InlineData("https://proauth.net/acr/baseline", 0, false)]
    [InlineData("custom-mfa", 0, false)]
    public void ExactContextsAndOriginalAuthenticationAgeDetermineEligibility(string acr, int age, bool expected) =>
        Assert.Equal(expected, SensitiveActionAssurance.Requirement.IsSatisfied(User(new("acr", acr),
            new("auth_time", (Now.ToUnixTimeSeconds() - age).ToString())), Now));

    [Fact]
    public void MissingMalformedOrDuplicateEvidenceDoesNotQualify()
    {
        var acr = new Claim("acr", "phr");
        var time = new Claim("auth_time", Now.ToUnixTimeSeconds().ToString());
        foreach (var user in new[] { User(acr), User(time), User(acr, new("auth_time", "invalid")), User(acr, acr, time), User(acr, time, time) })
            Assert.False(SensitiveActionAssurance.Requirement.IsSatisfied(user, Now));
    }
    [Fact]
    public void FreshTokenIssueTimeDoesNotFreshenOldAuthentication() => Assert.False(
        SensitiveActionAssurance.Requirement.IsSatisfied(User(new("acr", "phr"), new("auth_time", (Now.ToUnixTimeSeconds() - 301).ToString()),
            new("iat", Now.ToUnixTimeSeconds().ToString())), Now));
    [Fact]
    public void CallerConstructedClaimsCannotReplacePackageTokenValidation()
    {
        var context = new DefaultHttpContext { User = User(new("acr", "phr"), new("auth_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())) };
        context.Request.Headers.Authorization = "DPoP forged";
        Assert.Throws<InvalidOperationException>(() => SensitiveActionAssurance.Challenge(context));
    }
    [Fact]
    public void ApprovalPreflightDoesNotWriteAndConfirmationRechecksRoleAndRevision()
    {
        var book = new PurchaseBook([new("coffee", "Coffee", 24900)]);
        var requester = new Member("elena", "buyer", "engineering", ["requester"], 0);
        var approver = new Member("amir", "buyer", "engineering", ["approver"], 50000);
        var p = book.Request(requester, "coffee", "test");
        Assert.Equal("Requested", book.CheckApproval(approver, p.Id, 1).Status);
        Assert.Equal("Requested", Assert.Single(book.List(requester)).Status);
        Assert.Throws<PolicyException>(() => book.Approve(approver with { Roles = [] }, p.Id, 1, true));
        book.Approve(approver, p.Id, 1, true);
        Assert.Equal("revision_conflict", Assert.Throws<PolicyException>(() => book.Approve(approver, p.Id, 1, true)).Message);
    }
    [Fact]
    public void PaymentPreflightDoesNotWriteAndConfirmationRechecksTransaction()
    {
        var book = new PaymentBook();
        var finance = new Member("priya", "buyer", "engineering", ["finance"], 50000);
        var payment = book.Prepare(new(Guid.NewGuid(), "buyer", "engineering", "supplier", 24900, 4));
        var request = new ReleasePayment(1, payment.AmountChf, payment.RecipientOrganizationId);
        Assert.Equal("Pending", book.CheckRelease(finance, payment.Id, request).Status);
        Assert.Equal("Pending", Assert.Single(book.List(finance)).Status);
        Assert.Throws<PolicyException>(() => book.Release(finance, payment.Id, request with { AmountChf = 1 }, true));
        Assert.Throws<PolicyException>(() => book.Release(finance with { ApprovalLimitChf = 1 }, payment.Id, request, true));
        Assert.Equal("Released", book.Release(finance, payment.Id, request, true).Status);
    }
}
