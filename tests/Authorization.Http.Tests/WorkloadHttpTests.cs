using Microsoft.Extensions.Options;
using ProAuth.Oidc.Client.Configuration;
using ProAuth.Oidc.Client.Dpop;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Procurement.Domain;
using Fulfilment.Api;
using Treasury.Api;
using Common.Evidence;
using Common.Security;
using Workflow.Domain;
using Xunit;
using static Authorization.Http.Tests.ApiFixture;

namespace Authorization.Http.Tests;

public class WorkloadHttpTests
{
    private static readonly Partition A = new("manufacturing", "engineering"), B = new("research", "lab"),
        Cross = new("research", "engineering");
    private static WorkloadAssignment Grant(string client, params Partition[] pairs) => new(Issuer, Tenant, client, pairs);
    private static Member Buyer(Partition p) => new("amir", p.OrganizationId, p.CostCentre, ["approver"], 2000);
    private static readonly Member Supplier = new("sam", "supplier-northstar", "dispatch", ["supplier"], 0);
    private static readonly Member Warehouse = new("lea", "logistics-alpine", "zurich", ["warehouse"], 0);
    private static Guid Seed(OrderBook orders, Partition pair)
    {
        var p = new ApprovedOrder(Guid.NewGuid(), pair.OrganizationId, pair.CostCentre, "elena", "station", 1100, 2, "amir", "supplier-northstar", "zurich");
        orders.Import(Buyer(pair), p);
        orders.Transition(Supplier, p.PurchaseId, 1, "accept");
        return p.PurchaseId;
    }

    private static async Task<JsonElement[]> Rows(HttpResponseMessage response, params string[] fields)
    {
        response.EnsureSuccessStatusCode();
        var rows = (await response.Content.ReadFromJsonAsync<JsonElement[]>())!;
        foreach (var row in rows)
            Assert.Equal(fields.Order(), row.EnumerateObject().Select(p => p.Name).Order());
        return rows;
    }

    [Fact]
    public async Task OwnersFilterBeforeSerializingAndSyncIntersectsBothGrants()
    {
        await using var env = new ApiFixture();
        var grants = new[] { Grant("reader", A, B), Grant("dispatch", A, B), Grant("reconcile", A, B) };
        var people = new[] { A, B, Cross }.Select((p, i) => new IdentityMembership(Issuer, "finance" + i, Tenant,
            new Member("person" + i, p.OrganizationId, p.CostCentre, ["finance"], 2000))).ToArray();
        env.Write(new
        {
            Memberships = people,
            Workloads = grants
        });
        var orders = new OrderBook();
        var a = Seed(orders, A);
        var b = Seed(orders, B);
        var cross = Seed(orders, Cross);
        using var fulfilment = await env.Start("fulfilment", Component.Fulfilment,
            ["fulfilment.read", "fulfilment.write", "fulfilment.create", "fulfilment.receive", "fulfilment.reconcile"], s =>
        {
            s.Services.AddSingleton(orders);
            s.Configuration["DispatchClientId"] = "dispatch";
            s.Configuration["TreasuryReaderClientId"] = "reader";
        }, app => app.MapFulfilmentEndpoints());

        var dispatch = env.Token("fulfilment", "fulfilment.read fulfilment.write", "service", "dispatch");
        var queue = await Rows(await Send(fulfilment, "/api/dispatch", dispatch), "purchaseId", "revision");
        Assert.Equal(new[] { a, b }.Order(), queue.Select(x => x.GetProperty("purchaseId").GetGuid()).Order());
        Assert.Equal(HttpStatusCode.NotFound, (await Send(fulfilment, "/api/dispatch/" + cross, dispatch, new
        {
            revision = 2
        })).StatusCode);
        foreach (var id in new[] { a, b })
            (await Send(fulfilment, "/api/dispatch/" + id, dispatch, new
            {
                revision = 2
            })).EnsureSuccessStatusCode();
        orders.Transition(Supplier, cross, 2, "dispatch");
        foreach (var id in new[] { a, b, cross })
            orders.Transition(Warehouse, id, 3, "receive");
        var reader = env.Token("fulfilment", "fulfilment.reconcile", "service", "reader");
        var receipts = await Rows(await Send(fulfilment, "/api/received", reader),
            "purchaseId", "buyerOrganizationId", "costCentre", "supplierOrganizationId", "amountChf", "sourceRevision");
        Assert.Equal(new[] { a, b }.Order(), receipts.Select(x => x.GetProperty("purchaseId").GetGuid()).Order());
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(fulfilment, "/api/received?organizationId=research&costCentre=engineering", reader)).StatusCode);
        foreach (var denied in new[] { env.Token("fulfilment", "fulfilment.reconcile", "service", "other"),
            env.Token("fulfilment", "fulfilment.read", "service", "reader"), env.Token("fulfilment", "fulfilment.reconcile", "service", "reader", tenant: "partners") })
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(fulfilment, "/api/received", denied)).StatusCode);
        env.Write(new
        {
            Memberships = people,
            Workloads = grants.Skip(1)
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(fulfilment, "/api/received", reader)).StatusCode);
        env.Write(new
        {
            Memberships = people,
            Workloads = grants
        });

        var payments = new PaymentBook();
        // A legitimate existing payment outside the reconciliation grant must not leak either.
        payments.Prepare(orders.Receipts(new HashSet<Partition> { Cross }).Single());
        using var treasury = await env.Start("treasury", Component.Treasury, ["treasury.read", "treasury.write", "treasury.reconcile"], s =>
        {
            s.Configuration["FinanceClientId"] = "finance";
            s.Configuration["ReconciliationClientId"] = "reconcile";
            s.Services.AddSingleton(payments);
            s.Services.AddSingleton(TokenEndpoint.Stub((method, _) =>
                method == "ClientCredentialsAsync" ? new()
                {
                    TokenType = "Bearer",
                    AccessToken = reader
                } : throw new InvalidOperationException()));
            s.Services.AddHttpClient("fulfilment", c => c.BaseAddress = fulfilment.BaseAddress);
        }, app => app.MapTreasuryEndpoints());
        using var proof = new DpopProofService(Options.Create(new AuthenticationSettings { SenderConstrainedTokenMode = SenderConstrainedTokenMode.Dpop }));
        string Finance(int i) => env.Token("treasury", "treasury.read treasury.write", "finance" + i, "finance",
            additional: new()
            {
                ["cnf"] = new Dictionary<string, object> { ["jkt"] = proof.JwkThumbprint! }
            });
        var unbound = env.Token("treasury", "treasury.read treasury.write", "finance0", "finance");
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(treasury, "/api/payments", unbound)).StatusCode);
        async Task Sync(int person, Guid purchase)
        {
            var result = (await Send(treasury, "/api/payments/synchronize", Finance(person), new
            {
            }, proof)).EnsureSuccessStatusCode();
            Assert.Equal(purchase, Assert.Single((await result.Content.ReadFromJsonAsync<PaymentInstruction[]>())!).PurchaseId);
        }
        await Sync(0, a);
        Assert.Empty(payments.List(people[1].Member));
        await Sync(0, a);
        await Sync(1, b);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(treasury, "/api/payments/synchronize", Finance(2), new
        {
        }, proof)).StatusCode);
        var reconcile = env.Token("treasury", "treasury.reconcile", "machine", "reconcile");
        var lines = await Rows(await Send(treasury, "/api/reconciliation", reconcile), "purchaseId", "amountChf", "status");
        Assert.Equal(new[] { a, b }.Order(), lines.Select(x => x.GetProperty("purchaseId").GetGuid()).Order());
        var payment = payments.List(people[0].Member).Single();
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(treasury, $"/api/payments/{payment.Id}/release", reconcile,
            new ReleasePayment(1, payment.AmountChf, payment.RecipientOrganizationId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(treasury, "/api/payments/synchronize",
            env.Token("treasury", "treasury.write", "finance0", "reconcile"), new
            {
            })).StatusCode);
        Assert.Equal("Pending", payments.List(people[0].Member).Single().Status);
    }
}
