using System.Net;
using Common.Evidence;
using Common.Security;
using Fulfilment.Api;
using Microsoft.Extensions.DependencyInjection;
using Procurement.Domain;
using Workflow.Domain;
using Xunit;
using static Authorization.Http.Tests.ApiFixture;

namespace Authorization.Http.Tests;

public class FulfilmentHttpTests
{
    [Fact]
    public async Task HumanOrderActionsKeepDistinctClientScopeAndMembershipBoundaries()
    {
        await using var env = new ApiFixture();
        var supplier = new Member("sam", "supplier-northstar", "dispatch", ["supplier"], 0);
        var warehouse = new Member("lea", "logistics-alpine", "zurich", ["warehouse"], 0);
        env.Write(new
        {
            Memberships = new[]
            {
                new IdentityMembership(Issuer, "sam", Tenant, supplier),
                new IdentityMembership(Issuer, "lea", Tenant, warehouse),
                new IdentityMembership(Issuer, "other", Tenant, supplier with { OrganizationId = "other-supplier" })
            }
        });
        var orders = new OrderBook();
        var purchase = new ApprovedOrder(Guid.NewGuid(), "buyer", "engineering", "elena", "station", 1100, 2,
            "amir", "supplier-northstar", "zurich");
        orders.Import(new Member("amir", "buyer", "engineering", ["approver"], 2000), purchase);
        using var api = await env.Start("fulfilment", Component.Fulfilment,
            ["fulfilment.read", "fulfilment.write", "fulfilment.receive"], builder =>
            {
                builder.Services.AddSingleton(orders);
                builder.Configuration["SupplierClientId"] = "supplier";
                builder.Configuration["WarehouseClientId"] = "warehouse";
            }, app => app.MapFulfilmentEndpoints());

        string SupplierToken(string scope = "fulfilment.write", string client = "supplier", string subject = "sam") =>
            env.Token("fulfilment", scope, subject, client);
        string WarehouseToken(string scope = "fulfilment.receive", string client = "warehouse") =>
            env.Token("fulfilment", scope, "lea", client);
        var path = $"/api/orders/{purchase.PurchaseId}";

        // Authentication and a valid scope cannot substitute for the expected client or supplier.
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(api, path + "/accept", SupplierToken(client: "warehouse"), new
        {
            revision = 1
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(api, path + "/accept", SupplierToken(scope: "fulfilment.read"), new
        {
            revision = 1
        })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(api, path + "/accept", SupplierToken(subject: "other"), new
        {
            revision = 1
        })).StatusCode);
        Assert.Equal("Assigned", Assert.Single(orders.List(supplier)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(api, path + "/accept", SupplierToken(), new
        {
            revision = 1
        })).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await Send(api, path + "/dispatch", SupplierToken(), new
        {
            revision = 1
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(api, path + "/dispatch", SupplierToken(client: "warehouse"), new
        {
            revision = 2
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(api, path + "/dispatch", SupplierToken(scope: "fulfilment.receive"), new
        {
            revision = 2
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(api, path + "/dispatch", SupplierToken(), new
        {
            revision = 2
        })).StatusCode);

        // Receive has its own scope and terminal client even if the person's membership is valid.
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(api, path + "/receive", WarehouseToken(client: "supplier"), new
        {
            revision = 3
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(api, path + "/receive", WarehouseToken(scope: "fulfilment.write"), new
        {
            revision = 3
        })).StatusCode);
        Assert.Equal("Dispatched", Assert.Single(orders.List(warehouse)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(api, path + "/receive", WarehouseToken(), new
        {
            revision = 3
        })).StatusCode);
        Assert.Equal("Received", Assert.Single(orders.List(warehouse)).Status);
    }
}
