using ProAuth.Oidc.Client.AspNetCore;
using ProAuth.Oidc.Client.Redis;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ProAuth.Bff.Extensions;
using ProAuth.Oidc.Client.Configuration;

// Supplier Portal demonstrates a partner browser application with server-held bearer tokens.
// The BFF handles authorization-code sign-in, refresh, logout and the CSRF-protected API proxy.
// appsettings.json requires PAR but does not select DPoP: Fulfilment accepts this bearer path
// alongside Procurement's DPoP exchange. Supplier organization access is enforced by the API,
// not by navigation or by the existence of a browser session. See 21-workflows.yml.
var builder = WebApplication.CreateBuilder(args);
builder.AddBffSessionInfrastructure("supplier");
// Keep the product integration here so the complete session-storage choice is visible.
// Redis holds a protected ticket per browser session. The token store reads that ticket
// during refresh, and the distributed lock coordinates renewal across replicas.
builder.Services.AddBff(builder.Configuration)
    .WithRedisDistributedLocking()
    .WithRedisTicketStore()
    .WithAuthSessionTokenStore()
    .PersistDataProtectionKeysToRedis();
builder.ConfigureBffCookie("supplier");

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
    <h1>We could not complete sign-in</h1><p>Check the identity service and try again.</p>
    <a href="/">Return to Supplier Portal</a></html>
    """, "text/html")).AllowAnonymous();
app.MapGet("/app-config", () =>
{
    using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalogue.json")));
    return Results.Json(document.RootElement.Clone());
}).AllowAnonymous();
app.MapHealthChecks("/health/live");
app.MapProAuthBff(); // Includes package-owned proxy CSRF validation and authentication endpoints.
app.MapFallbackToFile("index.html");
app.Run();
