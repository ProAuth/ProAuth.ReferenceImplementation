using ProAuth.Oidc.ResourceServer.AspNetCore;
using Fulfilment.Api;
using Common.Evidence;
using Common.Security;
using Workflow.Domain;

// Fulfilment joins several trust paths: a supplier's browser session, a warehouse device login,
// an automated dispatch client, Treasury's receipt reader and Procurement's delegated handoff.
// ResourceServer in appsettings.json accepts bearer and sender-constrained tokens because these
// clients use different presentations. ProAuth validates each presentation; endpoints then narrow
// access by scope, validated client identity, delegation actor and business assignment.
// Registrations: config/proauth/clients/21-workflows.yml and 22-warehouse.yml.
var builder = WebApplication.CreateBuilder(args);
builder.AddBusinessMemberships();
builder.Services.AddDemoEvidence(); // Optional sanitized observations for Demo Console.
builder.AddTrustedProxyForwarding();

// Keep the SDK integration visible: shared conventions pin issuer and access-token validation;
// this service's configuration supplies its audience and token presentation policy.
builder.Services.AddResourceServerAuthentication(builder.Configuration,
    ApiSecurity.CreateAccessTokenValidation(builder.Configuration));
builder.Services.AddScopePolicies("fulfilment.read", "fulfilment.write", "fulfilment.create", "fulfilment.receive", "fulfilment.reconcile");

builder.Services.AddSingleton<OrderBook>();
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
