using DemoConsole;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Common.Evidence;

// Operator-facing demonstration console. Collect sanitized protocol/business observations
// and show experiment outcomes outside the business request path. Publisher authentication
// protects the collector; recorded evidence never grants API permissions or validates tokens.
// See Common.Evidence projections and docs/demo-console.md for the observation contract.
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 16 * 1024);
var key = builder.Configuration["PublisherKey"];
if (string.IsNullOrEmpty(key) || key.Length < 32) throw new InvalidOperationException("Configure a private publisher key.");
var keyHash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
var viewerHost = builder.Configuration["ViewerHost"] ?? "democonsole.localtest.me";
if (builder.Configuration["IngestionAddressName"] is { } addressName)
{
    // Docker DNS resolves this alias only on the private evidence network.
    var address = (await System.Net.Dns.GetHostAddressesAsync(addressName))
        .Single(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(8080);
        options.Listen(address, 8081);
    });
}
var store = new RecordingStore();
var protocols = new System.Collections.Concurrent.ConcurrentQueue<ProtocolObservation>();
var app = builder.Build();
// Local operator viewer only. The ingestion listener is not published to the host or reverse proxy.
app.Use(async (context, next) =>
{
    var ingest = context.Connection.LocalPort == 8081;
    if (ingest)
    {
        if ((context.Request.Path != "/observations" && context.Request.Path != "/protocol-observations") || context.Request.Method != "POST" ||
            !CryptographicOperations.FixedTimeEquals(keyHash, SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers["X-Mervikon-Publisher"].ToString()))))
        {
            context.Response.StatusCode = 404;
            return;
        }
    }
    else if (context.Request.Method is not ("GET" or "HEAD") || (context.Request.Path == "/observations" || context.Request.Path == "/protocol-observations"))
    {
        context.Response.StatusCode = 404;
        return;
    }
    else if (context.Request.Host.Host is not ("localhost" or "127.0.0.1" or "[::1]") && !string.Equals(context.Request.Host.Host, viewerHost, StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 404;
        return;
    }
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    await next(context);
});
app.MapPost("/observations", (Observation observation) =>
{
    if (!observation.IsValid())
        return Results.BadRequest();
    store.Add(observation);
    return Results.NoContent();
});
app.MapPost("/protocol-observations", (ProtocolObservation value) =>
{
    if (!value.IsValid())
        return Results.BadRequest();
    protocols.Enqueue(value);
    while (protocols.Count > 500)
        protocols.TryDequeue(out _);
    return Results.NoContent();
});
app.MapGet("/api/protocol-evidence", () => new { schemaVersion = 1, packages = BuildIdentity.ProAuthPackageVersion, provenance = "recorded", observations = protocols.ToArray() });
app.MapGet("/api/recordings", () => store.Export());
app.MapGet("/api/control-comparisons", () =>
{
    var file = app.Configuration["ControlComparisonsFile"];
    if (string.IsNullOrEmpty(file) || !File.Exists(file))
        return Results.Ok(new ControlComparisons([], null));
    if (new FileInfo(file).Length > 16000)
        return Results.StatusCode(503);
    try
    {
        var value = JsonSerializer.Deserialize<ControlComparisons>(File.ReadAllText(file), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return value?.IsValid() == true ? Results.Ok(value) : Results.StatusCode(503);
    }
    catch (JsonException) { return Results.StatusCode(503); }
});
app.MapGet("/api/lab-comparison", () =>
{
    var file = app.Configuration["LabComparisonsFile"];
    if (string.IsNullOrEmpty(file) || !File.Exists(file))
        return Results.Ok(Array.Empty<LabComparison>());
    if (new FileInfo(file).Length > 16000)
        return Results.StatusCode(503);
    try
    {
        var values = JsonSerializer.Deserialize<LabComparison[]>(File.ReadAllText(file), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        return values.Length <= 2 && values.All(v => v.IsValid()) ? Results.Ok(values) : Results.StatusCode(503);
    }
    catch (JsonException) { return Results.StatusCode(503); }
});
app.MapGet("/api/scenarios", () => File.ReadLines(Path.Combine(AppContext.BaseDirectory, "registry.md"))
    .Select(l => Regex.Match(l, @"^\| (SCN-\d+) \| ([^|]+) \| ([^|]+) \| ([^|]+) \|$"))
    .Where(m => m.Success).Select(m => new { id = m.Groups[1].Value, name = m.Groups[2].Value.Trim(), capabilities = m.Groups[3].Value.Trim(), profiles = m.Groups[4].Value.Trim() }).ToArray());
app.MapGet("/api/export", () => Results.File(JsonSerializer.SerializeToUtf8Bytes(store.Export(), new JsonSerializerOptions(JsonSerializerDefaults.Web)), "application/json", "mervikon-recorded-evidence.json"));
app.MapGet("/health/live", () => Results.Ok(new { status = "alive", mode = "local-operator" }));
app.UseStaticFiles();
app.MapFallbackToFile("index.html");
app.Run();
