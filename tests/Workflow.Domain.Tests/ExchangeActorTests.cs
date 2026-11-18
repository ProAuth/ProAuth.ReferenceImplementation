using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Procurement.Domain;
using ProAuth.Oidc.ResourceServer.AspNetCore.Authentication;
using Common.Security;
using Xunit;

public sealed class ExchangeActorTests
{
    private const string Issuer = "https://issuer.example";
    [Theory]
    [InlineData("{\"sub\":\"other-client\",\"iss\":\"https://issuer.example\"}")]
    [InlineData("{\"sub\":\"procurement\",\"iss\":\"https://other.example\"}")]
    [InlineData("{\"sub\":\"procurement\",\"iss\":\"https://issuer.example\",\"act\":{\"sub\":\"other\"}}")]
    [InlineData("{\"sub\":\"other\",\"sub\":\"procurement\",\"iss\":\"https://issuer.example\"}")]
    [InlineData("[]")]
    [InlineData("malformed")]
    public void RejectsWrongAmbiguousOrChainedActor(string actor) => Assert.Throws<PolicyException>(() => ExchangeActor.Require(User(actor), "procurement", Issuer));
    [Fact]
    public void AcceptsExpectedActorAfterAuthentication() => ExchangeActor.Require(User("{\"sub\":\"procurement\",\"iss\":\"https://issuer.example\"}"), "procurement", Issuer);
    [Fact]
    public void RejectsMissingBinding()
    {
        var user = User("{\"sub\":\"procurement\",\"iss\":\"https://issuer.example\"}");
        ((ClaimsIdentity)user.Identity!).RemoveClaim(user.FindFirst("cnf"));
        Assert.Throws<PolicyException>(() => ExchangeActor.Require(user, "procurement", Issuer));
    }
    [Fact]
    public void RawHeaderAndAuthenticatedPrincipalCannotManufactureValidatedToken()
    {
        var context = new DefaultHttpContext { User = User("{}") };
        context.Request.Headers.Authorization = "DPoP unvalidated-input";
        Assert.Throws<InvalidOperationException>(() => context.GetValidatedAccessToken());
    }
    private static ClaimsPrincipal User(string actor) => new(new ClaimsIdentity(new[] {
        new Claim("pa_iss_clientapp","procurement"),new Claim("act",actor),new Claim("cnf","{\"jkt\":\"test-thumbprint\"}") }, "test-authentication"));
}
