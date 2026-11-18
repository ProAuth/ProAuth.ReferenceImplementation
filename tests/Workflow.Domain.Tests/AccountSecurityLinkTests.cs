using System.Security.Claims;
using Mervikon.Apps;
using Xunit;

public sealed class AccountSecurityLinkTests
{
    private const string Tenant = "00000000-0000-4000-8000-000000000001";
    private const string Query = "?tenantid=" + Tenant + "&idpinstanceid=00000000-0000-4000-8000-000000000002";
    private static ClaimsPrincipal User(params string[] links) => new(new ClaimsIdentity(links.Select(l => new Claim("pa_account_management_url", l)), "session"));
    [Fact]
    public void UsesOnlyAuthenticatedSameIssuerContext()
    {
        var link = "https://issuer.example/account/security" + Query;
        Assert.Equal(link, AccountSecurityLink.Resolve(User(link), "https://issuer.example", Tenant));
        Assert.Null(AccountSecurityLink.Resolve(new ClaimsPrincipal(), "https://issuer.example", Tenant));
        Assert.Null(AccountSecurityLink.Resolve(User(link, link), "https://issuer.example", Tenant));
    }
    [Theory]
    [InlineData("https://other.example/account/security")]
    [InlineData("http://issuer.example/account/security")]
    [InlineData("https://issuer.example/other")]
    [InlineData("https://evil@issuer.example/account/security")]
    public void RejectsUntrustedDestinations(string prefix) => Assert.Null(AccountSecurityLink.Resolve(User(prefix + Query), "https://issuer.example", Tenant));
    [Fact]
    public void RejectsTenantSubstitutionAndAdditionalParameters()
    {
        var link = "https://issuer.example/account/security" + Query;
        Assert.Null(AccountSecurityLink.Resolve(User(link), "https://issuer.example", "another-tenant"));
        Assert.Null(AccountSecurityLink.Resolve(User(link + "&returnUrl=https://other.example"), "https://issuer.example", Tenant));
    }
}
