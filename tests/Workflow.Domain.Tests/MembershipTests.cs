using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Procurement.Domain;
using Common.Security;
using Xunit;

public sealed class MembershipTests : IDisposable
{
    private readonly string file = Path.Combine(Path.GetTempPath(), "mervikon-membership-" + Guid.NewGuid() + ".json");
    private static ClaimsPrincipal User(string subject = "subject", string tenant = "tenant") => new(new ClaimsIdentity(new[] {
        new Claim("iss","https://issuer.example"),new Claim("sub",subject),new Claim("pa_iss_tenant",tenant) }, "test"));
    private Memberships Reader() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["MembershipFile"] = file }).Build());
    private void Write(string[] roles) => File.WriteAllText(file, JsonSerializer.Serialize(new
    {
        Memberships = new[] {
        new IdentityMembership("https://issuer.example","subject","tenant",new Member("nina-lifecycle","buyer-research","research",roles,20000)) }
    }));
    [Fact]
    public void MoverLosesApproverRoleWithoutRestartOrNewAuthentication()
    {
        Write(["requester", "approver"]);
        var reader = Reader();
        var user = User();
        Assert.Contains("approver", reader.Require(user).Roles);
        Write(["requester"]);
        Assert.DoesNotContain("approver", reader.Require(user).Roles);
    }
    [Fact]
    public void RemovedMembershipDeniesPreviouslyAcceptedIdentity()
    {
        Write(["requester"]);
        var reader = Reader();
        reader.Require(User());
        File.WriteAllText(file, "{\"Memberships\":[]}");
        Assert.Equal("membership_required", Assert.Throws<PolicyException>(() => reader.Require(User())).Message);
    }
    [Theory]
    [InlineData("{\"ResourceServer\":{\"Authority\":\"https://wrong\"},\"Memberships\":[]}")]
    [InlineData("{\"TrustedProxyIp\":\"127.0.0.1\",\"Memberships\":[]}")]
    [InlineData("{\"MembershipFile\":\"other\",\"Memberships\":[]}")]
    [InlineData("{\"Memberships\":[],\"Memberships\":[]}")]
    [InlineData("{\"Memberships\":[],\"Workloads\":null}")]
    [InlineData("{")]
    [InlineData("{\"Memberships\":null}")]
    [InlineData("{\"Memberships\":[null]}")]
    public void BrokenConfigurationNeverRetainsPreviousGrant(string broken)
    {
        Write(["approver"]);
        var reader = Reader();
        reader.Require(User());
        File.WriteAllText(file, broken);
        Assert.Equal("membership_configuration_unavailable", Assert.Throws<PolicyException>(() => reader.Require(User())).Message);
    }
    [Fact]
    public void SameSubjectInDifferentTenantIsNotMember()
    {
        Write(["approver"]);
        Assert.Throws<PolicyException>(() => Reader().Require(User(tenant: "another")));
    }
    [Theory]
    [InlineData("", "buyer", "engineering", 100)]
    [InlineData("actor", "", "engineering", 100)]
    [InlineData("actor", "buyer", "", 100)]
    [InlineData("actor", "buyer", "engineering", -1)]
    public void InvalidBusinessIdentityFailsClosed(string id, string organization, string costCentre, int limit)
    {
        File.WriteAllText(file, JsonSerializer.Serialize(new
        {
            Memberships = new[] {
            new IdentityMembership("https://issuer.example", "subject", "tenant",
                new Member(id, organization, costCentre, ["approver"], limit)) }
        }));
        Assert.Equal("membership_configuration_unavailable",
            Assert.Throws<PolicyException>(() => Reader().Require(User())).Message);
    }

    [Fact]
    public void UnreadableFileNeverRetainsPreviousGrant()
    {
        Write(["approver"]);
        var reader = Reader();
        reader.Require(User());
        File.Delete(file);
        Assert.Equal("membership_configuration_unavailable",
            Assert.Throws<PolicyException>(() => reader.Require(User())).Message);
    }

    [Fact]
    public void DuplicateIdentityMappingsFailClosed()
    {
        var membership = new IdentityMembership("https://issuer.example", "subject", "tenant",
            new Member("actor", "buyer", "engineering", ["approver"], 100));
        File.WriteAllText(file, JsonSerializer.Serialize(new
        {
            Memberships = new[] { membership, membership }
        }));
        Assert.Equal("membership_configuration_unavailable",
            Assert.Throws<PolicyException>(() => Reader().Require(User())).Message);
    }
    [Theory]
    [InlineData("iss")]
    [InlineData("sub")]
    [InlineData("pa_iss_tenant")]
    public void RepeatedIdentityClaimsCannotSelectEvenMatchingRow(string type)
    {
        Write(["requester"]);
        var user = User();
        ((ClaimsIdentity)user.Identity!).AddClaim(new Claim(type, user.FindFirst(type)!.Value));
        Assert.Throws<PolicyException>(() => Reader().Require(user));
    }

    [Fact]
    public void ExpectedClientCannotBeSelectedFromAmbiguousContext()
    {
        var user = User();
        var identity = (ClaimsIdentity)user.Identity!;
        identity.AddClaim(new("pa_iss_clientapp", "allowed"));
        identity.AddClaim(new("pa_iss_clientapp", "different"));
        Assert.Throws<PolicyException>(() => ApiSecurity.RequireClient(user, "allowed"));
    }

    public void Dispose() => File.Delete(file);
}
