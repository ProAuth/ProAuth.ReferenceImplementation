using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Procurement.Api;
using Procurement.Domain;
using Fulfilment.Api;
using ProAuth.Oidc.Client;
using ProAuth.Oidc.Client.Configuration;
using ProAuth.Oidc.Client.Dpop;
using ProAuth.Oidc.Client.Exceptions;
using Common.Evidence;
using Common.Security;
using Workflow.Domain;
using Xunit;
using static Authorization.Http.Tests.ApiFixture;

namespace Authorization.Http.Tests;

public class PurchaseHttpTests
{
    private static IdentityMembership Row(string subject, string person, string org = "buyer", string centre = "lab", int limit = 5000) =>
        new(Issuer, subject, Tenant, new(person, org, centre, ["approver", "requester"], limit));

    [Theory]
    [InlineData("ResourceServer")]
    [InlineData("TrustedProxyIp")]
    [InlineData("MembershipFile")]
    public async Task BusinessDocumentCannotConfigureApiTrust(string injected)
    {
        await using var env = new ApiFixture();
        env.Write(new Dictionary<string, object> { ["Memberships"] = Array.Empty<object>(), [injected] = "untrusted" });
        var b = WebApplication.CreateBuilder();
        b.Configuration["MembershipFile"] = env.FilePath;
        b.Configuration["ResourceServer:Authority"] = Issuer;
        b.Configuration["TrustedProxyIp"] = "127.0.0.1";
        Assert.Throws<PolicyException>(() => b.AddBusinessMemberships());
        Assert.Equal(Issuer, b.Configuration["ResourceServer:Authority"]);
        Assert.Equal("127.0.0.1", b.Configuration["TrustedProxyIp"]);
        Assert.Equal(env.FilePath, b.Configuration["MembershipFile"]);
        env.Write(new
        {
            Memberships = new[] { Row("first", "elena") }
        });
        b.AddBusinessMemberships();
        Assert.Null(b.Configuration["Memberships:0:Subject"]);
    }

    [Fact]
    public async Task ShippedProcurementPolicyRequiresBoundTokensAndFreshProofs()
    {
        await using var env = new ApiFixture();
        env.Write(new
        {
            Memberships = new[] { Row("first", "elena") }
        });
        using var settings = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "procurement-settings.json")));
        using var api = await env.Start("procurement", Component.Procurement, ["procurement.read"], b =>
        {
            b.Configuration["ResourceServer:AccessTokenPresentationPolicy"] =
                settings.RootElement.GetProperty("ResourceServer").GetProperty("AccessTokenPresentationPolicy").GetString();
        }, _ => { });
        using var key = new DpopProofService(Options.Create(new AuthenticationSettings { SenderConstrainedTokenMode = SenderConstrainedTokenMode.Dpop }));
        var unbound = env.Token("procurement", "procurement.read", "first");
        var bound = env.Token("procurement", "procurement.read", "first", additional: new()
        {
            ["cnf"] = new Dictionary<string, object> { ["jkt"] = key.JwkThumbprint! }
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(api, "/api/me", unbound)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(api, "/api/me", bound)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(api, "/api/me", bound, proof: key)).StatusCode);
    }

    [Fact]
    public async Task PurchaseDecisionUsesCurrentAssignmentAndStablePerson()
    {
        await using var env = new ApiFixture();
        var rows = new[] { Row("first", "elena") with { SubjectAliases = ["pairwise"] }, Row("second", "amir"),
            Row("research", "ria", org: "other"), Row("finance", "fin", centre: "finance"), Row("junior", "jay", limit: 10) };
        env.Write(new
        {
            Memberships = rows
        });
        using var http = await env.Start("procurement", Component.Procurement, ["procurement.read", "procurement.write"], Setup, a => a.MapProcurementEndpoints());
        string TokenFor(string subject) => env.Token("procurement", "procurement.read procurement.write", subject);
        var requester = TokenFor("first");
        var approver = TokenFor("second");
        Assert.Equal(HttpStatusCode.OK, (await Send(http, "/api/identity", TokenFor("new"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(http, "/api/me", TokenFor("new"))).StatusCode);
        foreach (var token in new[] { env.Token("procurement", "procurement.read", "first", tenant: "other"),
            env.Token("procurement", "procurement.read", "first", additional: new() { ["pa_iss_tenant"] = new[] { Tenant, "other" } }) })
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(http, "/api/me", token)).StatusCode);
        foreach (var token in new[] { env.Token("procurement", "procurement.read", "first", issuer: "https://other.example"),
            env.Token("procurement/", "procurement.read", "first"),
            env.Token("procurement", "procurement.read", "first", type: "JWT") })
            Assert.Equal(HttpStatusCode.Unauthorized, (await Send(http, "/api/me", token)).StatusCode);
        var created = (await Send(http, "/api/purchases", requester, new
        {
            productId = "station"
        })).EnsureSuccessStatusCode();
        var purchase = await created.Content.ReadFromJsonAsync<Purchase>();
        string approve = $"/api/purchases/{purchase!.Id}/approve";
        foreach (var sub in new[] { "first", "pairwise", "junior", "research", "finance" })
        {
            var expected = sub is "research" or "finance" ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden;
            Assert.Equal(expected, (await Send(http, approve, TokenFor(sub), new
            {
                revision = 1
            })).StatusCode);
        }
        env.Write(new
        {
            Memberships = rows.Where(r => r.Subject != "second")
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(http, approve, approver, new
        {
            revision = 1
        })).StatusCode);
        env.Write(new
        {
            Memberships = rows,
            TrustedProxyIp = "unexpected"
        });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Send(http, approve, approver, new
        {
            revision = 1
        })).StatusCode);
        env.Write(new
        {
            Memberships = rows
        });
        var approved = (await Send(http, approve, approver, new
        {
            revision = 1
        })).EnsureSuccessStatusCode();
        Assert.Equal("amir", (await approved.Content.ReadFromJsonAsync<Purchase>())!.ApproverId);
    }

    private static void Setup(WebApplicationBuilder b)
    {
        b.Services.AddSingleton(new PurchaseBook([new("station", "Station", 1000)]));
        b.Services.AddSingleton<EvidenceJournal>();
        b.Services.AddSingleton(TokenEndpoint.Stub((_, _) => throw new InvalidOperationException("Unexpected token call")));
    }

    [Theory]
    [InlineData("Bearer", "invalid_scope", 502, 0)]
    [InlineData("dPoP", "invalid_scope", 200, 1)]
    [InlineData("DPoP", "invalid_scope", 502, 0, "urn:ietf:params:oauth:token-type:refresh_token")]
    [InlineData("DPoP", "invalid_scope", 502, 0, null)]
    [InlineData("DPoP", "invalid_client", 502, 0)]
    [InlineData("DPoP", "timeout", 502, 0)]
    [InlineData("DPoP", "accepted", 502, 0)]
    public async Task HandoffChecksProbeAndTokenTypeBeforeSending(string type, string probe, int status, int sends, string? issuedType = "urn:ietf:params:oauth:token-type:access_token")
    {
        await using var env = new ApiFixture();
        env.Write(new
        {
            Memberships = new[] { Row("second", "amir") }
        });
        using var fulfilment = await env.Start("fulfilment", Component.Fulfilment,
            ["fulfilment.read", "fulfilment.write", "fulfilment.create", "fulfilment.receive", "fulfilment.reconcile"],
            b => { b.Services.AddSingleton<OrderBook>(); b.Configuration["ProcurementExchangeClientId"] = "exchange"; }, a => a.MapFulfilmentEndpoints());
        using var proof = new DpopProofService(Options.Create(new AuthenticationSettings { SenderConstrainedTokenMode = SenderConstrainedTokenMode.Dpop }));
        var book = new PurchaseBook([new("station", "Station", 1000)]);
        var order = book.Request(Row("first", "elena").Member, "station", "setup");
        book.Approve(Row("second", "amir").Member, order.Id, 1, false);
        var recorder = new CountRequests();
        using var procurement = await env.Start("procurement", Component.Procurement, ["procurement.read", "procurement.write"], b =>
        {
            Setup(b);
            b.Configuration["ConferenceControls:Enabled"] = "true";
            b.Services.AddSingleton(book);
            b.Services.AddSingleton(TokenEndpoint.Stub((method, args) =>
            {
                Assert.Equal(nameof(IOidcClient.ExchangeTokenAsync), method);
                if (((IEnumerable<string>)args[3]!).Contains("fulfilment.write"))
                {
                    if (probe == "timeout")
                        throw new TaskCanceledException();
                    if (probe != "accepted")
                        throw new OidcErrorResponseException(new()
                        {
                            Error = probe
                        }, 400);
                }
                return new()
                {
                    TokenType = type,
                    IssuedTokenType = issuedType,
                    AccessToken = env.Token("fulfilment", "fulfilment.create", "second", "exchange",
                    additional: new()
                    {
                        ["act"] = new Dictionary<string, object> { ["sub"] = "exchange", ["iss"] = Issuer },
                        ["cnf"] = new Dictionary<string, object> { ["jkt"] = proof.JwkThumbprint! }
                    })
                };
            }));
            b.Services.AddHttpClient("fulfilment", c => c.BaseAddress = fulfilment.BaseAddress)
                .AddHttpMessageHandler(() => new DpopProofHandler(proof)).AddHttpMessageHandler(() => recorder);
        }, a => a.MapProcurementEndpoints());
        using var workplace = new DpopProofService(Options.Create(new AuthenticationSettings { SenderConstrainedTokenMode = SenderConstrainedTokenMode.Dpop }));
        var original = env.Token("procurement", "procurement.write", "second", additional: new()
        {
            ["cnf"] = new Dictionary<string, object> { ["jkt"] = workplace.JwkThumbprint! }
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(fulfilment, "/api/orders", original, new
        {
        }, workplace)).StatusCode);
        var result = await Send(procurement, $"/api/purchases/{order.Id}/handoff", original, new
        {
            revision = 2
        }, workplace);
        Assert.Equal(status, (int)result.StatusCode);
        Assert.Equal(sends, recorder.Count);
    }

    private sealed class CountRequests : DelegatingHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Count);
            return base.SendAsync(request, ct);
        }
    }
}
