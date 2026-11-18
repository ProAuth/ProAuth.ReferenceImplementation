using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using ProAuth.Bff.Extensions;
using ProAuth.Oidc.Client.Configuration;
using ProAuth.Oidc.Client.InMemory;
using ProAuth.Oidc.Client.AspNetCore;
using Common.Evidence;
using System.Net;
using System.Text.Json;

// INTENTIONALLY UNSAFE DEMONSTRATION: compromised same-origin widget in both variants.
// Deliberately unsafe browser comparison: contrast a token-exposing SPA with a server-token
// BFF when same-origin script is compromised. A BFF keeps OAuth tokens out of JavaScript but
// cannot stop that script using the victim's active session. This is a separate lab client;
// normal Workplace/Supplier/Finance BFFs live in src/apps. See docs/conference.md.
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
var bff = builder.Configuration["Mode"] == "bff";
var origin = builder.Configuration["Origin"]!;
var issuer = builder.Configuration["Issuer"]!;
var attacker = builder.Configuration["AttackerOrigin"]!;
if (new[] { origin, issuer, attacker }.Any(v => !Uri.TryCreate(v, UriKind.Absolute, out var u) || u.Scheme != "https")) throw new InvalidOperationException("HTTPS lab origins required.");
builder.Services.AddHttpClient("discovery", c => c.Timeout = TimeSpan.FromSeconds(5))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
if (!bff) builder.Services.AddDemoEvidence();
if (bff)
{
    builder.Services.AddAuthorization();
    builder.Services.AddHttpContextAccessor();
    // Tokens stay in the server-side ticket; the browser receives only its handle.
    // Refresh and step-up use this browser login's ticket through the session token store.
    builder.Services.AddBff(builder.Configuration).WithInProcessLocking().WithInMemoryTicketStore().WithAuthSessionTokenStore();
    builder.Services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, o =>
    {
        o.Cookie.Name = "__Host-mervikon-browser-lab";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.Path = "/";
    });
    builder.Services.AddBffDemoEvidence(builder.Configuration, "Browser BFF");
    builder.Services.PostConfigure<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>("OpenIdConnect", o =>
    {
        var previous = o.Events.OnRemoteFailure;
        o.Events.OnRemoteFailure = async c =>
        {
            // Type/stack only: remote error descriptions can contain credentials.
            Console.Error.WriteLine($"Lab sign-in failed: {c.Failure?.GetType().Name}; {c.Failure?.InnerException?.GetType().Name}\n{c.Failure?.InnerException?.StackTrace ?? c.Failure?.StackTrace}");
            await previous(c);
        };
    });
}
else
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
        o.KnownProxies.Add(IPAddress.Parse(builder.Configuration["TrustedProxyIp"]!));
    });
var app = builder.Build();
app.Use(async (c, next) =>
{
    await next(c);
    if (c.Features.Get<Yarp.ReverseProxy.Forwarder.IForwarderErrorFeature>() is { } failure)
        Console.Error.WriteLine($"Lab forwarding failed: {failure.Error}; {failure.Exception?.GetType().Name}; {failure.Exception?.InnerException?.GetType().Name}");
});
app.UseForwardedHeaders();
if (bff)
{
    app.AddBffSecurity(app.Services.GetRequiredService<IOptions<AuthenticationSettings>>(), false);
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseSession();
    app.MapProAuthBff();
}
app.Use(async (c, next) =>
{
    c.Response.Headers.CacheControl = "no-store";
    // A compromised script dependency is deliberately allowed in BOTH isolated variants.
    c.Response.Headers.ContentSecurityPolicy = $"default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self' {issuer} {attacker} {builder.Configuration["ApiOrigin"]} {builder.Configuration["WrongAudienceOrigin"]}; frame-ancestors 'none'";
    await next(c);
});
app.MapGet("/health/live", () => Results.Ok(new { mode = bff ? "bff" : "spa" }));
app.MapGet("/Error", () => Results.Content("<h1>Sign-in did not complete</h1><p>No successful demo result is implied. Return to the lab and sign in again using a clean profile.</p><a href='/'>Return to lab</a>", "text/html", statusCode: 502));
app.MapGet("/lab/config", async (IHttpClientFactory factory, CancellationToken ct) =>
{
    using var response = await factory.CreateClient("discovery").GetAsync(issuer.TrimEnd('/') + "/.well-known/openid-configuration", ct);
    response.EnsureSuccessStatusCode();
    using var d = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    var root = d.RootElement;
    if (root.GetProperty("issuer").GetString() != issuer)
        return Results.Problem(statusCode: 502, title: "issuer_mismatch");
    string Endpoint(string key)
    {
        var value = root.GetProperty(key).GetString()!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var u) || u.Scheme != "https")
            throw new InvalidOperationException("Insecure discovery");
        return value;
    }
    return Results.Ok(new
    {
        mode = bff ? "bff" : "spa",
        issuer,
        authorizationEndpoint = Endpoint("authorization_endpoint"),
        tokenEndpoint = Endpoint("token_endpoint"),
        revocationEndpoint = Endpoint("revocation_endpoint"),
        clientId = builder.Configuration["SpaClientId"],
        tenantId = builder.Configuration["TenantId"],
        redirectUri = origin + "/callback",
        attacker,
        api = bff ? "/api/quote" : builder.Configuration["ApiOrigin"] + "/api/quote",
        wrongAudience = builder.Configuration["WrongAudienceOrigin"] + "/api/quote"
    });
});
// Browser observations are explicitly labelled client reports, not authoritative validator evidence.
app.MapPost("/lab/observations", async (HttpContext c, ProtocolObservation observation, ProtocolPublisher publisher) =>
{
    if (c.Request.Headers.Origin != origin || c.Request.Headers["X-Mervikon-Lab"] != "1" || observation.Source != (bff ? "Browser BFF" : "Browser SPA") || !observation.IsValid())
        return Results.BadRequest();
    await publisher.Emit(observation, c.RequestAborted);
    return Results.NoContent();
});
if (!bff)
    app.MapPost("/lab/introspect", async (HttpContext c, LabIntrospection value, IHttpClientFactory factory, CancellationToken ct) =>
    {
        if (c.Request.Headers.Origin != origin || c.Request.Headers["X-Mervikon-Lab"] != "1" || string.IsNullOrEmpty(value.AccessToken) || value.AccessToken.Length > 16000)
            return Results.BadRequest();
        using var client = factory.CreateClient("discovery");
        using var discovery = JsonDocument.Parse(await client.GetStringAsync(issuer.TrimEnd('/') + "/.well-known/openid-configuration", ct));
        if (discovery.RootElement.GetProperty("issuer").GetString() != issuer)
            return Results.Problem(statusCode: 502, title: "issuer_mismatch");
        var endpoint = new Uri(discovery.RootElement.GetProperty("introspection_endpoint").GetString()!);
        if (endpoint.Scheme != "https" || endpoint.Authority != new Uri(issuer).Authority)
            return Results.Problem(statusCode: 502, title: "unexpected_introspection_endpoint");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        var id = builder.Configuration["IntrospectionClientId"] ?? throw new InvalidOperationException("Introspection client required.");
        var secret = builder.Configuration["IntrospectionClientSecret"] ?? throw new InvalidOperationException("Introspection credential required.");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{Uri.EscapeDataString(id)}:{Uri.EscapeDataString(secret)}")));
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = value.AccessToken, ["token_type_hint"] = "access_token" });
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return Results.Problem(statusCode: 502, title: "introspection_failed");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return Results.Ok(new
        {
            status = (int)response.StatusCode,
            active = body.RootElement.GetProperty("active").GetBoolean()
        });
    });
app.UseStaticFiles();
app.MapFallbackToFile("index.html");
app.Run();

internal sealed record LabIntrospection(string AccessToken);
