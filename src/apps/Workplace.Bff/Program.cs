using ProAuth.Oidc.Client.AspNetCore;
using ProAuth.Oidc.Client.Redis;
using Mervikon.Transport;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ProAuth.Bff.Extensions;
using ProAuth.Oidc.Client.Configuration;
using System.Security.Claims;
using Mervikon.Apps;
using ProAuth.Oidc.Client.TokenHandling;
using System.IdentityModel.Tokens.Jwt;
using ProAuth.Oidc.Client.Dpop;
using Common.Evidence;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

// Workplace is the employee browser boundary. The ProAuth BFF performs authorization-code
// sign-in, refresh and logout, and proxies API requests with server-held tokens. The browser
// uses a session cookie and the SDK's CSRF header, never an OAuth access token.
// appsettings.json requires PAR and DPoP for Procurement; 20-workplace.yml registers the client.
// Approval step-up is a separate, explicit verification journey (see StepUpEndpoints).
var builder = WebApplication.CreateBuilder(args);
builder.AddBffSessionInfrastructure("workplace");
// Keep the product integration here so the complete session-storage choice is visible.
// Redis holds a protected ticket per browser session. The token store reads that ticket
// during refresh, and the distributed lock coordinates renewal across replicas.
builder.Services.AddBff(builder.Configuration)
    .WithRedisDistributedLocking()
    .WithRedisTicketStore()
    .WithAuthSessionTokenStore()
    .PersistDataProtectionKeysToRedis();
builder.ConfigureBffCookie("workplace");

// DEMO ONLY: optional protocol observations for Demo Console; not required for sign-in or API access.
builder.Services.AddBffDemoEvidence(builder.Configuration, "Workplace");
var app = builder.Build();
var replicaInstance = builder.Configuration["ReplicaLab:Instance"];
if (replicaInstance is not (null or "one" or "two")) throw new InvalidOperationException("Unknown replica lab instance.");
if (replicaInstance is not null)
    app.Use(async (context, next) =>
    {
        context.Response.Headers["X-Mervikon-Bff-Instance"] = replicaInstance;
        await next(context);
    });
// Trust only the configured reverse proxy before applying the package's BFF protections.
app.UseForwardedHeaders();
app.AddBffSecurity(app.Services.GetRequiredService<IOptions<AuthenticationSettings>>(), false);
app.MapShellStylesheet();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthenticatedApiBoundary();
app.UseAuthorization();
app.UseSession();
app.MapGet("/Error", () => Results.Content("""
    <!doctype html><html lang="en"><meta charset="utf-8"><title>Sign-in unavailable</title>
    <h1>Sign-in or verification was not completed</h1><p>The requested authentication may have been cancelled, unavailable or insufficient. No purchase or payment was submitted.</p><p>Return to the application to review the current state. An existing session can still be available; verification will not retry automatically.</p>
    <a href="/">Return to Workplace</a></html>
    """, "text/html")).AllowAnonymous();
app.MapGet("/app-config", (HttpContext context) =>
{
    var document = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalogue.json")))!;
    context.Response.Headers.CacheControl = "private, no-store";
    if (context.User.Identity?.IsAuthenticated != true)
        return Results.Json(new
        {
            company = document["company"]
        });
    document["conferenceControls"] = builder.Configuration.GetValue<bool>("ConferenceControls:Enabled");
    return Results.Json(document);
}).AllowAnonymous();
if (builder.Configuration.GetValue<bool>("ConferenceControls:Enabled"))
{
    var target = HttpsOrigin.Require(builder.Configuration["ConferenceControls:FulfilmentOrigin"]);
    app.MapPost("/lab/original-token", async (HttpContext context, ITokenStore store, IDpopProofService proof, Publisher evidence, CancellationToken ct) =>
    {
        if (context.Request.Headers.Origin != builder.Configuration["ConferenceControls:Origin"] || context.Request.Headers["X-ProAuth-BFF"] != "1")
            return Results.BadRequest();
        var subject = context.User.FindFirstValue("sub");
        var tokens = subject is null ? null : await store.GetUserTokens(subject, ct);
        if (tokens?.AccessToken is not { } token)
            return Results.Unauthorized();
        // Use the original holder's key and a fresh proof: no confounding wrong-key replay.
        using var client = new HttpClient(new DpopProofHandler(proof) { InnerHandler = new HttpClientHandler { AllowAutoRedirect = false } });
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(target, "/api/orders"));
        request.Headers.Authorization = new AuthenticationHeaderValue("DPoP", token);
        using var response = await client.SendAsync(request, ct);
        var status = (int)response.StatusCode;
        evidence.RecordProtocol(new(Guid.NewGuid(), DateTimeOffset.UtcNow, "Workplace", "audience", status, status == 401 ? "passed" : "failed", EvidenceProjection.Token(token)));
        return Results.Ok(new
        {
            downstreamStatus = status,
            expectedStatus = 401,
            explanation = "Original Procurement token sent to Fulfilment with its holder's fresh proof. Inspect the audiences; status alone does not identify the validator's rejection reason."
        });
    }).RequireAuthorization("authenticated-user");
}
app.MapHealthChecks("/health/live");
if (replicaInstance is not null)
    app.MapGet("/replica-lab/session", async (HttpContext context, ITokenStore store, CancellationToken ct) =>
    {
        context.Response.Headers.CacheControl = "no-store";
        var subject = context.User.FindFirstValue("sub");
        if (subject is null)
            return Results.Unauthorized();
        var tokens = await store.GetUserTokens(subject, ct);
        // Scheduling observations only. Never expose token material or treat decoded
        // timestamps as proof of authentication strength or API authorization.
        DateTime? expiresAt = null;
        var reader = new JwtSecurityTokenHandler();
        if (tokens?.AccessToken is { } accessToken && reader.CanReadToken(accessToken))
            expiresAt = reader.ReadJwtToken(accessToken).ValidTo;
        return Results.Ok(new
        {
            instance = replicaInstance,
            tokenPresent = tokens is not null,
            refreshAt = tokens?.RefreshAt,
            expiresAt
        });
    }).RequireAuthorization("authenticated-user");
app.MapGet("/account-security", (ClaimsPrincipal user, IOptions<AuthenticationSettings> settings) =>
{
    var destination = AccountSecurityLink.Resolve(user, settings.Value.Authority, settings.Value.TenantId);
    return destination is null ? Results.Problem(statusCode: 409, title: "account_security_context_unavailable") : Results.Redirect(destination);
}).RequireAuthorization("authenticated-user");
app.MapStepUpConfirmation("approve-purchase", "Workplace");
app.MapProAuthBff(); // Includes package-owned proxy CSRF validation and authentication endpoints.
app.MapFallbackToFile("index.html");
app.Run();
