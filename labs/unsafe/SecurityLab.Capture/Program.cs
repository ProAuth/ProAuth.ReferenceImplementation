using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Security.Cryptography.X509Certificates;

// INTENTIONALLY UNSAFE DEMONSTRATION. Never add this component to normal application traffic.
// TLS terminates here: this is malicious logging at a trusted hop, not passive TLS decryption.
// Deliberately unsafe lab boundary: capture credentials from the configured demonstration hop
// so SecurityLab.Runner can attempt independent replay. This component is excluded from normal
// business traffic and must remain opt-in. See docs/security-lab.md and deploy/compose/lab.yml.
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders(); // No raw request/header/exception logging in this capture process.
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = 1024;
    var certificate = X509Certificate2.CreateFromPem(
        Encoding.UTF8.GetString(Convert.FromBase64String(builder.Configuration["TlsCertificatePem"]!)),
        Encoding.UTF8.GetString(Convert.FromBase64String(builder.Configuration["TlsPrivateKeyPem"]!)));
    o.ListenAnyIP(8443, endpoint => endpoint.UseHttps(certificate));
    o.ListenAnyIP(8081);
});
builder.Services.AddHttpClient("target", c => { c.BaseAddress = new Uri("http://lab-api:8080"); c.Timeout = TimeSpan.FromSeconds(5); })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
builder.Services.AddHttpClient("replica", c => { c.BaseAddress = new Uri("http://lab-api-replica:8080"); c.Timeout = TimeSpan.FromSeconds(5); })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
var controlKey = builder.Configuration["ControlKey"] ?? throw new InvalidOperationException("Control key required.");
if (controlKey.Length < 32) throw new InvalidOperationException("Control key is too short.");
var publicAuthority = builder.Configuration["PublicAuthority"] ?? "lab-api.localtest.me:8443";
var captures = new ConcurrentDictionary<Guid, Capture>();
var app = builder.Build();
app.Use(async (context, next) =>
{
    foreach (var (id, value) in captures)
        if (value.ExpiresAt < DateTimeOffset.UtcNow)
            captures.TryRemove(id, out _);
    if (context.Connection.LocalPort == 8081)
    {
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(controlKey)), SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers["X-Lab-Control"].ToString()))))
        {
            context.Response.StatusCode = 404;
            return;
        }
    }
    else if (context.Request.Path != "/api/quote" || context.Request.Method != "GET" || context.Request.Host.Value != publicAuthority)
    {
        context.Response.StatusCode = 404;
        return;
    }
    context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.MapGet("/captures/{id:guid}", (Guid id) => captures.TryGetValue(id, out var value) ? Results.Ok(value) : Results.NotFound());
app.MapDelete("/captures", () => { captures.Clear(); return Results.NoContent(); });
app.MapGet("/api/quote", async (HttpContext context, IHttpClientFactory factory) =>
{
    if (context.Connection.LocalPort != 8443)
        return Results.NotFound();
    var authorization = context.Request.Headers.Authorization.ToString();
    var proof = context.Request.Headers["DPoP"].ToString();
    if (authorization.Length > 16000 || proof.Length > 16000 || captures.Count >= 50)
        return Results.StatusCode(429);
    var handle = Guid.NewGuid();
    // Raw credentials exist only in this bounded ephemeral store and are never returned on the data listener.
    captures[handle] = new(authorization, proof, DateTimeOffset.UtcNow.AddSeconds(90));
    using var request = new HttpRequestMessage(HttpMethod.Get, "/api/quote");
    request.Headers.Host = publicAuthority;
    request.Headers.Add("X-Forwarded-Proto", "https");
    request.Headers.TryAddWithoutValidation("Authorization", authorization);
    if (!string.IsNullOrEmpty(proof))
        request.Headers.TryAddWithoutValidation("DPoP", proof);
    var replica = context.Request.Headers["X-Lab-Replica"].ToString();
    if (replica != "" && (builder.Configuration["ReplicaMode"] != "true" || replica is not ("one" or "two")))
        return Results.BadRequest();
    using var response = await factory.CreateClient(replica == "two" ? "replica" : "target").SendAsync(request, context.RequestAborted);
    context.Response.Headers["X-Lab-Artifact"] = handle.ToString();
    // Only protocol challenge headers pass back. Never reflect arbitrary target headers or exception text.
    foreach (var header in new[] { "DPoP-Nonce", "WWW-Authenticate", "X-Mervikon-Instance" })
        if (response.Headers.TryGetValues(header, out var values))
            context.Response.Headers[header] = values.ToArray();
    context.Response.StatusCode = (int)response.StatusCode;
    if (response.IsSuccessStatusCode)
        return Results.Content(await response.Content.ReadAsStringAsync(context.RequestAborted), "application/json", statusCode: (int)response.StatusCode);
    return Results.StatusCode((int)response.StatusCode);
});
app.Run();
internal sealed record Capture(string Authorization, string Proof, DateTimeOffset ExpiresAt);
