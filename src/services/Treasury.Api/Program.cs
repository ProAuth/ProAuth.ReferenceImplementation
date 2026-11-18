using ProAuth.Oidc.ResourceServer.AspNetCore;
using Mervikon.Transport;
using Treasury.Api;
using Common.Evidence;
using ProAuth.Oidc.Client;
using Common.Security;
using Workflow.Domain;

// Treasury serves Finance Desk and a separate reconciliation workload. ResourceServer in
// appsettings.json accepts bearer and sender-constrained tokens; /api/payments additionally
// requires a validated DPoP presentation. The reconciliation worker retains bearer read access.
// Finance Desk uses PAR, private-key client authentication and DPoP (see its BFF settings).
// Payment release also requires fresh strong authentication and exact transaction confirmation;
// TreasuryEndpoints and PaymentBook enforce those requirements independently of the client profile.
var builder = WebApplication.CreateBuilder(args);
builder.AddBusinessMemberships();
builder.Services.AddDemoEvidence(); // Optional sanitized observations for Demo Console.
builder.AddTrustedProxyForwarding();

// Keep the SDK integration visible: shared conventions pin issuer and access-token validation;
// this service's configuration supplies its audience and token presentation policy.
builder.Services.AddResourceServerAuthentication(builder.Configuration,
    ApiSecurity.CreateAccessTokenValidation(builder.Configuration));
builder.Services.AddScopePolicies("treasury.read", "treasury.write", "treasury.reconcile");

builder.Services.AddSingleton<PaymentBook>();
builder.Services.AddHttpClient();
// Receipt synchronization uses Treasury's own client-credentials identity, not token exchange.
// OutboundAuthentication and the treasury receipt-reader registration select a bearer workload;
// Fulfilment limits it to operator-assigned buyer/cost-centre partitions.
builder.Services.AddOidcClient(builder.Configuration, "OutboundAuthentication");
builder.Services.AddHttpClient("fulfilment", c => c.BaseAddress = HttpsOrigin.Require(builder.Configuration["FulfilmentOrigin"]))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
var app = builder.Build();
app.UseForwardedHeaders();
app.UseRouting();
app.UseDemoEvidence(Component.Treasury);
app.UseSanitizedUpstreamErrors();
app.UseAuthentication();
app.UseAuthorization();
app.UseBusinessPolicyErrors();
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapIdentityEndpoints("treasury.read");
app.MapTreasuryEndpoints();
app.Run();
