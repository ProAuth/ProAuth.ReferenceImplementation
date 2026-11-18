using System.Diagnostics;
using System.Net;
using Group.Provisioner;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Procurement.Api;
using Procurement.Domain;
using Common.Evidence;
using Common.Security;
using Xunit;
using Xunit.Abstractions;
using static Authorization.Http.Tests.ApiFixture;

namespace Authorization.Http.Tests;

public class GroupApprovalTests(ITestOutputHelper output)
{
    [Fact]
    public async Task IssuedTokenStillNeedsBothIndependentDecisions()
    {
        await using var env = new ApiFixture();
        var requester = new Member("elena", "manufacturing", "engineering", ["requester"], 0);
        var approver = new Member("amir", "manufacturing", "engineering", ["requester", "approver"], 2500);
        var row = new IdentityMembership(Issuer, "amir-sub", Tenant, approver);
        void Assign(params IdentityMembership[] rows) => env.Write(new
        {
            Memberships = rows
        });
        Assign(row);
        string snapshot = Path.ChangeExtension(env.FilePath, ".eligibility");
        void Publish(string? person, string tenant = Tenant, DateTimeOffset? at = null)
        {
            File.WriteAllText(snapshot + ".next", JsonSerializer.Serialize(new GroupEligibility(Issuer, tenant, person, at ?? DateTimeOffset.UtcNow)));
            File.Move(snapshot + ".next", snapshot, true);
        }
        var mapping = new NativeMapping(Issuer, Tenant, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "amir");
        var directory = new NativeGroupResponse(mapping);
        using var native = new HttpClient(directory);
        Task Refresh() => PublishEligibility.FromGroup(native, "fixture-only", mapping, snapshot, CancellationToken.None);
        var book = new PurchaseBook([new("station", "Station", 1000), new("laser", "Laser", 3000)]);
        var first = book.Request(requester, "station", "one");
        var second = book.Request(requester, "station", "two");
        var beforeRefresh = book.Request(requester, "station", "before-refresh");
        var own = book.Request(approver, "station", "own");
        var wrong = book.Request(requester with
        {
            CostCentre = "sales"
        }, "station", "wrong");
        var excess = book.Request(requester, "laser", "excess");
        using var api = await env.Start("procurement", Component.Procurement, ["procurement.read", "procurement.write"], b =>
        {
            b.Configuration["ApprovalProfile"] = "NativeGroup";
            b.Configuration["GroupEligibilityFile"] = snapshot;
            b.Services.AddSingleton(book);
            b.Services.AddSingleton<EvidenceJournal>();
            b.Services.AddSingleton(TokenEndpoint.Stub((_, _) => throw new InvalidOperationException()));
        }, app => app.MapProcurementEndpoints());
        var token = env.Token("procurement", "procurement.write", "amir-sub", additional: new()
        {
            ["groups"] = "Mervikon purchasing approvers"
        });
        async Task Check(Purchase purchase, HttpStatusCode expected) => Assert.Equal(expected,
            (await Send(api, $"/api/purchases/{purchase.Id}/approve", token, new
            {
                revision = 1
            })).StatusCode);
        await Check(first, HttpStatusCode.Forbidden);
        Publish("amir", tenant: "other");
        await Check(first, HttpStatusCode.Forbidden);
        await Refresh();
        Assign();
        await Check(first, HttpStatusCode.Forbidden);
        Assign(row);
        await Check(own, HttpStatusCode.Forbidden);
        await Check(wrong, HttpStatusCode.NotFound);
        await Check(excess, HttpStatusCode.Forbidden);
        await Check(first, HttpStatusCode.OK);
        directory.DirectMember = false;
        // Directory no longer lists Amir, but this already-issued token and the last fact are still usable.
        await Check(beforeRefresh, HttpStatusCode.OK);
        var elapsed = Stopwatch.StartNew();
        await Refresh();
        await Check(second, HttpStatusCode.Forbidden);
        output.WriteLine("Fixture source refresh to first denied HTTP response: {0:F2} ms.", elapsed.Elapsed.TotalMilliseconds);
        Publish("amir", at: DateTimeOffset.UtcNow.AddMinutes(-1)); // Stopped publisher.
        await Check(second, HttpStatusCode.Forbidden);
        directory.DirectMember = true;
        await Refresh();
        Assign(row with
        {
            Member = approver with
            {
                Roles = ["requester"]
            }
        });
        await Check(second, HttpStatusCode.Forbidden);
        Assert.Equal("Requested", book.CheckApproval(approver, second.Id, 1).Status);
    }

    // Native API wire contract fixture, not a ProAuth installation. Group display name never changes.
    private sealed class NativeGroupResponse(NativeMapping source) : HttpMessageHandler
    {
        public bool DirectMember { get; set; } = true;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal($"/api/userstore/v2/{source.Store}/administration/Group/{source.Group}", request.RequestUri!.AbsolutePath);
            Assert.Equal(HttpMethod.Get, request.Method);
            var users = DirectMember ? new[] { source.NativeUser } : [];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    instanceId = source.Store,
                    id = source.Group,
                    name = "Purchasing approvers",
                    memberUsers = users.Select(id => new { instanceId = source.Store, id })
                })
            });
        }
    }
}
