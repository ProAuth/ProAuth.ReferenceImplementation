using ProAuth.Oidc.ResourceServer.AspNetCore;
using Mervikon.Transport;
using Common.Security;
using Common.Evidence;
using ProAuth.Oidc.Client;
using ProAuth.Oidc.Client.Dpop;
using System.Text.Json;
using Procurement.Api;
using Procurement.Domain;

// Procurement serves Workplace: incoming access tokens must target this API and be DPoP-bound.
// The ProAuth handler validates both the token and the request proof. appsettings.json selects
// DpopRequired; config/proauth/clients/20-workplace.yml registers the browser application's access.
// Business memberships and PurchaseBook then enforce buyer partitions, spending limits and
// separation of duties. High-value approval adds fresh authentication; handoff uses token exchange.
var builder = WebApplication.CreateBuilder(args);
builder.AddBusinessMemberships();
builder.Services.AddDemoEvidence(); // Optional sanitized observations for Demo Console.
builder.AddTrustedProxyForwarding();

// Keep the SDK integration visible: shared conventions pin issuer and access-token validation;
// this service's configuration supplies its audience and token presentation policy.
builder.Services.AddResourceServerAuthentication(builder.Configuration,
    ApiSecurity.CreateAccessTokenValidation(builder.Configuration));
builder.Services.AddScopePolicies("procurement.read", "procurement.write");

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
using var catalogue = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalogue.json")));
builder.Services.AddSingleton(new PurchaseBook(catalogue.RootElement.GetProperty("products").Deserialize<Product[]>(jsonOptions)!));
builder.Services.AddSingleton<EvidenceJournal>();
builder.Services.AddHttpClient();
// Procurement authenticates as its own OAuth client when exchanging the validated user token.
// OutboundAuthentication configures its credential and DPoP key. The AS exchange policy limits
// the result to Fulfilment's create scope; the original Workplace proof cannot be reused.
builder.Services.AddOidcClient(builder.Configuration, "OutboundAuthentication");
// DEMO ONLY: observe exchange requests through the SDK's named client.
if (!string.IsNullOrEmpty(builder.Configuration["Evidence:Endpoint"]))
{
    var source = builder.Configuration["Evidence:Source"] ?? "Procurement";
    builder.Services.AddHttpClient("OidcClient").AddHttpMessageHandler(sp =>
        new ProtocolHttpObserver(sp.GetRequiredService<Publisher>(), source));
}
// The exchanged token is bound to Procurement's key. The SDK creates a fresh proof for each
// downstream HTTP request; disabling redirects prevents forwarding credentials to another origin.
builder.Services.AddTransient<DpopProofHandler>();
builder.Services.AddHttpClient("fulfilment", c => c.BaseAddress = HttpsOrigin.Require(builder.Configuration["FulfilmentOrigin"])).AddHttpMessageHandler<DpopProofHandler>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
var app = builder.Build();
app.UseForwardedHeaders();
app.UseRouting();
app.UseDemoEvidence(Component.Procurement);
app.UseSanitizedUpstreamErrors();
app.UseAuthentication();
app.UseAuthorization();
app.UseBusinessPolicyErrors();
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapIdentityEndpoints("procurement.read");
app.MapProcurementEndpoints();
app.Run();
