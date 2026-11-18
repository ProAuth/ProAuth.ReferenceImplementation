using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using ProAuth.Oidc.Client;
using ProAuth.Oidc.Client.Authentication;
using ProAuth.Oidc.Client.Configuration;
using ProAuth.Oidc.Client.Exceptions;

using Common.Evidence;

// Executable protocol controls for the isolated quote API. Each selected variant exercises
// issuance and API use, plus the relevant expiry/revocation, tamper or certificate negative case.
// These observations describe the tested configuration, not a general conformance certificate.
// Pair with ProtocolLab.Api, config/proauth/protocols and docs/protocol-controls.md.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDemoEvidence();
builder.Logging.ClearProviders();
builder.Services.AddHttpClient();
builder.Services.AddOidcClient(builder.Configuration, "Authentication");
// Supply the client certificate on the token endpoint transport for the mTLS variant.
// The SDK applies its redirect and cookie policy to this named client.
builder.Services.AddHttpClient("OidcClient").ConfigurePrimaryHttpMessageHandler(sp =>
    OidcClientAuthentication.CreateHttpClientHandler(null, sp.GetRequiredService<IOptions<AuthenticationSettings>>().Value));
using var host = builder.Build();
var cfg = builder.Configuration;
var evidence = host.Services.GetRequiredService<ProtocolPublisher>();
var runId = Guid.NewGuid();
var variant = cfg["Variant"] ?? "jwt";
if (variant is not ("jwt" or "opaque" or "jwe" or "mtls")) throw new InvalidOperationException("Unknown protocol variant.");
var target = new Uri(cfg["Target"]!);
if (target.Scheme != "https" || target.AbsolutePath != "/api/quote") throw new InvalidOperationException("Fixed HTTPS quote target required.");
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(100));
var oidc = host.Services.GetRequiredService<IOidcClient>();

var interactive = builder.Configuration["DemoMode"] == "true";
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
async Task DemoWait(string action)
{
    if (!interactive)
        return;
    Console.WriteLine("DEMO_WAIT:" + action);
    if (await Console.In.ReadLineAsync(deadline.Token) != action)
        throw new InvalidOperationException("Expected the next guided demonstration action.");
}
void DemoObserved(string step, int? status, TokenSummary? summary = null, ProofSummary? proofSummary = null, string? certificateThumbprint = null)
{
    if (interactive)
        Console.WriteLine("DEMO_EVENT:" + JsonSerializer.Serialize(
        new ProtocolObservation(runId, DateTimeOffset.UtcNow, "Protocol probe", step, status, "observed", summary, proofSummary, PresentedCertificateThumbprint: certificateThumbprint), jsonOptions));
}

try
{
    await DemoWait("legitimate");
    var start = DateTimeOffset.UtcNow;
    using var certificate = variant == "mtls" ? X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(cfg["Authentication:MutualTls:Certificate"]!), cfg["Authentication:MutualTls:CertificatePassword"]) : null;
    var issued = await oidc.ClientCredentialsAsync(["protocols.quote"], ["urn:mervikon:protocols"], deadline.Token);
    var token = issued.AccessToken ?? throw new InvalidOperationException("No token issued.");
    if (new JsonWebTokenHandler().CanReadToken(token))
    {
        var decoded = new JsonWebToken(token);
        if (variant == "jwe" && !decoded.IsEncrypted)
            throw new InvalidOperationException("Encrypted representation required.");
        Console.WriteLine($"Unvalidated representation: encrypted={decoded.IsEncrypted}; signed={decoded.IsSigned}. API validation determines acceptance.");
    }
    if (issued.ExpiresIn is <= 0 or > 65)
        throw new InvalidOperationException("Dedicated short-lived fixture required.");
    async Task<int> Call(X509Certificate2? cert, bool forgedHeader = false, string? overrideToken = null)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        if (cert is not null)
            handler.ClientCertificates.Add(cert);
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, target);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", overrideToken ?? token);
        if (forgedHeader)
            request.Headers.Add("X-Forwarded-Tls-Client-Cert", Convert.ToBase64String(certificate!.RawData));
        using var response = await client.SendAsync(request, deadline.Token);
        if (!response.IsSuccessStatusCode)
        {
            var challenge = string.Join(' ', response.Headers.WwwAuthenticate);
            Console.WriteLine($"API HTTP {(int)response.StatusCode}; token validation rejected={challenge.Contains("access token validation failed", StringComparison.OrdinalIgnoreCase)}; certificate binding rejected={challenge.Contains("certificate", StringComparison.OrdinalIgnoreCase)}.");
            foreach (var known in new[] { "Introspection endpoint returned 400.", "Introspection endpoint returned 401.", "JWT introspection response validation failed.", "Introspection response is inactive." })
                if (challenge.Contains(known, StringComparison.Ordinal))
                    Console.WriteLine(known);
        }
        await evidence.Emit(new(runId, DateTimeOffset.UtcNow, "Protocol probe", cert is null || forgedHeader || overrideToken is not null ? "replay" : "legitimate", (int)response.StatusCode, "observed",
            EvidenceProjection.Token(overrideToken ?? token), PresentedCertificateThumbprint: cert is null ? null : Convert.ToBase64String(SHA256.HashData(cert.RawData)).TrimEnd('=').Replace('+', '-').Replace('/', '_')), deadline.Token);
        return (int)response.StatusCode;
    }
    var legitimate = await Call(certificate);
    Console.WriteLine($"{variant}: legitimate control HTTP {legitimate}");
    if (legitimate != 200)
        throw new InvalidOperationException($"Legitimate control HTTP {legitimate}.");
    var thumbprint = certificate is null ? null : Convert.ToBase64String(SHA256.HashData(certificate.RawData)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    DemoObserved("legitimate", legitimate, EvidenceProjection.Token(token), certificateThumbprint: thumbprint);
    if (interactive)
    {
        await DemoWait("capture");
        // Controlled token possession: no claim of TLS interception or private-key theft.
        DemoObserved("capture", null, EvidenceProjection.Token(token));
        await DemoWait("replay");
    }
    int? missingCertificate = null, wrongCertificate = null, forgedCertificateHeader = null;
    if (certificate is not null)
    {
        missingCertificate = await Call(null);
        DemoObserved("replay", missingCertificate, EvidenceProjection.Token(token));
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=mervikon-wrong-client", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.2") }, false));
        using var wrong = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10));
        wrongCertificate = await Call(wrong);
        forgedCertificateHeader = await Call(null, true);
        if (missingCertificate != 401 || wrongCertificate != 401 || forgedCertificateHeader != 401)
            throw new InvalidOperationException("Certificate-bound negative control failed.");
    }
    var invalidToken = await Call(certificate, overrideToken: "invalid-mervikon-access-token");
    if (invalidToken != 401)
        throw new InvalidOperationException("Invalid token accepted.");
    int? tamperedCiphertext = null;
    if (variant == "jwe")
    {
        var parts = token.Split('.');
        if (parts.Length != 5 || parts[3].Length == 0)
            throw new InvalidOperationException("Compact JWE required.");
        parts[3] = (parts[3][0] == 'A' ? 'B' : 'A') + parts[3][1..];
        tamperedCiphertext = await Call(certificate, overrideToken: string.Join('.', parts));
        if (tamperedCiphertext != 401)
            throw new InvalidOperationException("Tampered ciphertext accepted.");
    }
    if (interactive)
        Console.WriteLine("DEMO_CLEANUP");
    await oidc.RevokeTokenAsync(token, "access_token", deadline.Token);
    var revocationResponseProcessed = true; // A client error now fails this control; no compatibility exception swallowing.
    var afterRevocation = await Call(certificate);
    if (variant == "opaque" && afterRevocation != 401)
        throw new InvalidOperationException("Reference token not rejected after revocation.");
    var remaining = start.AddSeconds(issued.ExpiresIn + 2) - DateTimeOffset.UtcNow;
    if (remaining > TimeSpan.Zero)
        await Task.Delay(remaining, deadline.Token);
    var expired = await Call(certificate);
    if (expired != 401)
        throw new InvalidOperationException("Expired token accepted.");
    await evidence.Emit(new(runId, DateTimeOffset.UtcNow, "Protocol probe", "cleanup", expired, "passed"), deadline.Token);
    DemoObserved("cleanup", expired);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        variant,
        observedAt = DateTimeOffset.UtcNow,
        legitimate,
        missingCertificate,
        wrongCertificate,
        forgedCertificateHeader,
        invalidToken,
        tamperedCiphertext,
        revocationResponseProcessed,
        afterRevocation,
        expired,
        evidence = "Observed HTTP controls; no token or private key exported"
    }));
}
catch (OidcErrorResponseException e) { Console.Error.WriteLine($"Protocol token request rejected: {e.ErrorResponse.Error switch { "invalid_client" => "invalid_client", "invalid_scope" => "invalid_scope", "invalid_target" => "invalid_target", "invalid_request" => "invalid_request", _ => "unclassified OAuth error" }}."); Environment.ExitCode = 1; }
catch (Exception e) { Console.Error.WriteLine($"Protocol control incomplete ({e.GetType().Name}); no token or remote response body exported."); Environment.ExitCode = 1; }
