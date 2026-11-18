using Common.Evidence;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

// INTENTIONALLY UNSAFE DEMONSTRATION. Runner has no victim signing keys or OAuth client credentials.
// Replay the captured lab request independently of its legitimate sender. Compare bearer
// reuse, DPoP key possession and proof replay under the selected lab configuration. This runner
// records observations; it is never part of business authorization. See docs/replica-controls.md.
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1024);
builder.Services.AddHttpClient("lab", c => c.Timeout = TimeSpan.FromSeconds(5)).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
var key = builder.Configuration["ControlKey"]!;
if (string.IsNullOrEmpty(key) || key.Length < 32) throw new InvalidOperationException("Control key required.");
var target = new Uri(builder.Configuration["Target"]!);
if (target.Scheme != "https" || target.AbsolutePath != "/api/quote") throw new InvalidOperationException("Fixed HTTPS lab target required.");
var gate = new SemaphoreSlim(1);
var app = builder.Build();
app.MapPost("/{operation:regex(^(replay|inspect)$)}/{id:guid}", async (Guid id, HttpContext context, IHttpClientFactory factory) =>
{
    if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(key)), SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers["X-Lab-Control"].ToString()))))
        return Results.NotFound();
    if (!await gate.WaitAsync(0))
        return Results.StatusCode(429);
    try
    {
        using var client = factory.CreateClient("lab");
        using var read = new HttpRequestMessage(HttpMethod.Get, $"http://lab-capture:8081/captures/{id}");
        read.Headers.Add("X-Lab-Control", key);
        using var response = await client.SendAsync(read, context.RequestAborted);
        if (!response.IsSuccessStatusCode)
            return Results.NotFound();
        var capture = await response.Content.ReadFromJsonAsync<Capture>(context.RequestAborted);
        if (capture is null || capture.ExpiresAt < DateTimeOffset.UtcNow)
            return Results.NotFound();
        if (context.Request.RouteValues["operation"]?.ToString() == "inspect")
            return Results.Ok(new
            {
                tokenSummary = EvidenceProjection.Token(capture.Authorization.Split(' ', 2).Last()),
                proofSummary = EvidenceProjection.Proof(capture.Proof)
            });
        async Task<int> Replay(bool includeProof)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            request.Headers.TryAddWithoutValidation("Authorization", capture.Authorization);
            if (includeProof && capture.Proof.Length > 0)
                request.Headers.TryAddWithoutValidation("DPoP", capture.Proof);
            using var actual = await client.SendAsync(request, context.RequestAborted);
            return (int)actual.StatusCode;
        }
        var tokenOnly = await Replay(false);
        int? capturedProof = capture.Proof.Length > 0 ? await Replay(true) : null;
        return Results.Ok(new
        {
            tokenOnly,
            capturedProof,
            tokenSummary = EvidenceProjection.Token(capture.Authorization.Split(' ', 2).Last()),
            proofSummary = EvidenceProjection.Proof(capture.Proof),
            cause = "HTTP outcomes observed; specific package validator not instrumented",
            attackerHasPrivateKey = false
        });
    }
    finally { gate.Release(); }
});
app.Run();
internal sealed record Capture(string Authorization, string Proof, DateTimeOffset ExpiresAt);
