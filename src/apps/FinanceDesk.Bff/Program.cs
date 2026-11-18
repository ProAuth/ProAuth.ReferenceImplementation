using Common.Evidence;
using ProAuth.Oidc.Client.AspNetCore;
using ProAuth.Oidc.Client.Redis;
using System.Security.Claims;
using Mervikon.Apps;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ProAuth.Bff.Extensions;
using ProAuth.Oidc.Client.Configuration;

// Finance Desk uses the FAPI2SecurityProfile configured in appsettings.json: required PAR,
// private_key_jwt client authentication and DPoP-bound API tokens. These protect different
// boundaries: the authorization request, the client's identity and possession of the token.
// ProAuth's BFF implements the protocol; this host composes session storage and application UI.
// Treasury separately checks payment permissions and fresh authentication on release.
// Client registration, assertion key and assurance policy: config/proauth/clients/21-workflows.yml.
var builder = WebApplication.CreateBuilder(args);
builder.AddBffSessionInfrastructure("finance");
// Keep the product integration here so the complete session-storage choice is visible.
// Redis holds a protected ticket per browser session. The token store reads that ticket
// during refresh, and the distributed lock coordinates renewal across replicas.
builder.Services.AddBff(builder.Configuration)
    .WithRedisDistributedLocking()
    .WithRedisTicketStore()
    .WithAuthSessionTokenStore()
    .PersistDataProtectionKeysToRedis();
builder.ConfigureBffCookie("finance");

// DEMO ONLY: optional protocol observations for Demo Console; not required for sign-in or API access.
builder.Services.AddBffDemoEvidence(builder.Configuration, "Finance");
var app = builder.Build();
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
    <a href="/">Return to Finance Desk</a></html>
    """, "text/html")).AllowAnonymous();
app.MapGet("/app-config", () =>
{
    using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalogue.json")));
    return Results.Json(document.RootElement.Clone());
}).AllowAnonymous();
app.MapHealthChecks("/health/live");
app.MapGet("/account-security", (ClaimsPrincipal user, IOptions<AuthenticationSettings> settings) =>
{
    var destination = AccountSecurityLink.Resolve(user, settings.Value.Authority, settings.Value.TenantId);
    return destination is null ? Results.Problem(statusCode: 409, title: "account_security_context_unavailable") : Results.Redirect(destination);
}).RequireAuthorization("authenticated-user");
app.MapStepUpConfirmation("release-payment", "Finance Desk");
app.MapProAuthBff(); // Includes package-owned proxy CSRF validation and authentication endpoints.
app.MapFallbackToFile("index.html");
app.Run();
