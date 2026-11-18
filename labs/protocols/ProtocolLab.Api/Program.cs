using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Protocols;
using ProAuth.Oidc.Common.Discovery;
using ProAuth.Oidc.ResourceServer.AspNetCore;

// Isolated protocol comparison, not a business service. The dedicated protocols tenant issues
// JWT, opaque (ReferenceToken), encrypted JWT and certificate-bound variants for one quote API.
// ProAuth performs token/proof validation and introspection; this host configures issuer trust,
// decryption and signed-introspection validation. See config/proauth/protocols and
// deploy/compose/protocols.yml. The mTLS path trusts certificate forwarding only from its proxy.
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
var issuer = builder.Configuration["ResourceServer:Authority"]!;
if (!Uri.TryCreate(issuer, UriKind.Absolute, out var authority) || authority.Scheme != "https") throw new InvalidOperationException("HTTPS issuer required.");
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // Keep RemoteIpAddress as the actual connecting proxy for certificate-forwarding trust checks.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    o.KnownProxies.Add(IPAddress.Parse(builder.Configuration["TrustedProxyIp"]!));
});
var discovery = new IssuerBoundConfigurationManager(issuer + "/.well-known/openid-configuration", new IssuerBoundConfigurationRetriever(issuer), new HttpDocumentRetriever { RequireHttps = true });
builder.Services.AddResourceServerAuthentication(builder.Configuration, o =>
{
    o.TokenValidationParameters.ConfigurationManager = discovery;
    o.TokenValidationParameters.ValidIssuer = issuer;
    o.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
    o.TokenValidationParameters.TokenDecryptionKey = new Microsoft.IdentityModel.Tokens.JsonWebKey(builder.Configuration["DecryptionJwk"]!);
    // The RFC 9701 envelope is addressed to the introspection client. Its signed iat
    // bounds the age of the response independently of the enclosed access token's expiry.
    o.Introspection.JwtResponseValidationParameters.ConfigurationManager = discovery;
    o.Introspection.JwtResponseValidationParameters.ValidTypes = ["token-introspection+jwt"];
    o.Introspection.JwtResponse.Issuer = issuer;
    ArgumentException.ThrowIfNullOrWhiteSpace(o.Introspection.ClientId);
    o.Introspection.JwtResponse.Audience = o.Introspection.ClientId;
    o.Introspection.JwtResponse.MaxAge = TimeSpan.FromSeconds(25);
    o.Introspection.JwtResponse.ClockSkew = TimeSpan.FromSeconds(5);
});
builder.Services.AddAuthorization(o => o.AddPolicy("quote", p => p.RequireAuthenticatedUser()
    .RequireClaim("scope", "protocols.quote").RequireClaim("iss", issuer).RequireClaim("aud", "urn:mervikon:protocols")
    .RequireAssertion(c => long.TryParse(c.User.FindFirst("exp")?.Value, out var expiry) && expiry > DateTimeOffset.UtcNow.ToUnixTimeSeconds())));
var app = builder.Build();
app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health/live", () => Results.Ok());
app.MapGet("/api/quote", () => new { product = "calibration-station", amountChf = 18500, revision = 1 }).RequireAuthorization("quote");
app.Run();
