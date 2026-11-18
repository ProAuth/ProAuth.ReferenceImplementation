using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Protocols;
using ProAuth.Oidc.Common.Discovery;
using ProAuth.Oidc.ResourceServer.AspNetCore;
using ProAuth.Oidc.ResourceServer.Redis;
using StackExchange.Redis;

// Intentionally unsafe demonstration topology: an explicitly trusted TLS terminator logs credentials.
// This target keeps real signature/issuer/audience/scope/binding validation enabled in both variants.
// Isolated replay target: compare bearer reuse with DPoP sender and proof-replay checks.
// Both replicas in deploy/compose/replicas.yml use the same ProAuth Redis replay store.
// Its atomic insert lets only the first use of a proof succeed across those replicas.
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    o.KnownProxies.Add(IPAddress.Parse(builder.Configuration["CaptureProxyIp"]!));
});
var issuer = builder.Configuration["ResourceServer:Authority"]!;
if (!Uri.TryCreate(issuer, UriKind.Absolute, out var authority) || authority.Scheme != "https") throw new InvalidOperationException("HTTPS issuer required.");
var discovery = new IssuerBoundConfigurationManager(issuer.TrimEnd('/') + "/.well-known/openid-configuration",
    new IssuerBoundConfigurationRetriever(issuer), new HttpDocumentRetriever { RequireHttps = true });
if (builder.Configuration["ReplayRedis"] is { Length: > 0 } replayRedis)
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    {
        var connection = ConfigurationOptions.Parse(replayRedis);
        connection.BacklogPolicy = BacklogPolicy.FailFast;
        return ConnectionMultiplexer.Connect(connection);
    });
    builder.Services.AddRedisDpopProofReplayStore(options =>
    {
        options.ApplicationName = "mervikon-replay-lab";
        options.Issuer = issuer;
    });
}
builder.Services.AddResourceServerAuthentication(builder.Configuration, o =>
{
    o.TokenValidationParameters.ConfigurationManager = discovery;
    o.TokenValidationParameters.ValidIssuer = issuer;
    o.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
});
var quoteScope = builder.Configuration["QuoteScope"] ?? "lab.quote";
builder.Services.AddAuthorization(o => o.AddPolicy("quote", p => p.RequireAuthenticatedUser().RequireAssertion(c =>
    c.User.FindAll("scope").SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Contains(quoteScope, StringComparer.Ordinal))));
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins((builder.Configuration["BrowserOrigins"] ?? "https://invalid.localtest.me").Split(';')).WithMethods("GET").WithHeaders("Authorization")));
var app = builder.Build();
app.UseCors();
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Mervikon-Instance"] = builder.Configuration["Instance"] ?? "single";
    try
    {
        await next(context);
    }
    catch (RedisException) when (!context.Response.HasStarted) { context.Response.StatusCode = 503; }
});
app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/api/quote", () => new { product = "calibration-station", amountChf = 18500, revision = 1, warning = "Isolated security demonstration fixture." }).RequireAuthorization("quote");
app.Run();
