#:sdk Microsoft.NET.Sdk.Web
#:project ../../src/services/Fulfilment.Api/Fulfilment.Api.csproj
using ProAuth.Oidc.ResourceServer.AspNetCore;
using Fulfilment.Api;
using Procurement.Domain;
using Common.Evidence;
using Common.Security;
using Workflow.Domain;

// Disposable host only: one accepted order to prove that remounting grants never clears state.
// Uses the production route definitions, token validator, issuer discovery and domain store.
var builder = WebApplication.CreateBuilder(args);
builder.AddBusinessMemberships();
builder.Services.AddDemoEvidence();
builder.AddTrustedProxyForwarding();
builder.Services.AddResourceServerAuthentication(builder.Configuration,
    ApiSecurity.CreateAccessTokenValidation(builder.Configuration));
builder.Services.AddScopePolicies("fulfilment.read", "fulfilment.write", "fulfilment.create", "fulfilment.receive", "fulfilment.reconcile");
var orders = new OrderBook();
var purchase = new ApprovedOrder(Guid.NewGuid(), "buyer-manufacturing", "engineering", "elena", "station", 1000, 2, "amir", "supplier-northstar", "zurich");
orders.Import(new Member("amir", "buyer-manufacturing", "engineering", ["approver"], 2000), purchase);
orders.Transition(new Member("sam", "supplier-northstar", "dispatch", ["supplier"], 0), purchase.PurchaseId, 1, "accept");
builder.Services.AddSingleton(orders);
var app = builder.Build();
app.UseForwardedHeaders();
app.UseRouting();
app.UseDemoEvidence(Component.Fulfilment);
app.UseSanitizedUpstreamErrors();
app.UseAuthentication();
app.UseAuthorization();
app.UseBusinessPolicyErrors();
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapIdentityEndpoints("fulfilment.read");
app.MapFulfilmentEndpoints();
app.Run();
