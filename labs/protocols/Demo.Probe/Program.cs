using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProAuth.Oidc.Client.Authentication;
using ProAuth.Oidc.Client.Configuration;
using ProAuth.Oidc.Client.Dpop;
using Common.Evidence;

// Conference probe for selected OAuth boundaries: token issuance, sender proofs, refresh and
// high-assurance request controls. The configured mode selects a bounded experiment; credentials
// belong to dedicated clients. Business services do not depend on this diagnostic executable.
// See config/proauth/unsafe-lab/91-conference.yml and docs/conference.md for run prerequisites.
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Services.AddDemoEvidence();
using var host = builder.Build();
var cfg = builder.Configuration;
var evidence = host.Services.GetRequiredService<ProtocolPublisher>();
var settings = cfg.GetSection("Authentication").Get<AuthenticationSettings>()!;
var run = Guid.NewGuid();
using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(45));
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
using var signer = new DpopProofService(Options.Create(settings));
async Task Emit(string step, int? status, string outcome, string? error = null, TokenSummary? token = null, ProofSummary? proof = null)
{
    var e = new ProtocolObservation(run, DateTimeOffset.UtcNow, "Protocol probe", step, status, outcome, token, proof, error);
    await evidence.Emit(e, ct.Token);
    Console.WriteLine(JsonSerializer.Serialize(e, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
}
try
{
    using var discovery = JsonDocument.Parse(await http.GetStringAsync(settings.Authority.TrimEnd('/') + "/.well-known/openid-configuration", ct.Token));
    if (discovery.RootElement.GetProperty("issuer").GetString() != settings.Authority)
        throw new InvalidOperationException("Issuer mismatch");
    Uri Endpoint(string key)
    {
        var u = new Uri(discovery.RootElement.GetProperty(key).GetString()!);
        if (u.Scheme != "https")
            throw new InvalidOperationException("HTTPS required");
        return u;
    }
    async Task<(int Status, string? Error, string? Nonce, string? Token)> Post(Uri uri, Dictionary<string, string> values, string step, string? nonce = null, bool proof = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        var fields = values.ToList();
        OidcClientAuthentication.ApplyClientAuthentication(request, fields, settings, uri.ToString());
        request.Content = new FormUrlEncodedContent(fields);
        string? jwt = proof ? signer.CreateProof(new DpopProofRequest(HttpMethod.Post, uri, Nonce: nonce)) : null;
        if (jwt is not null)
            request.Headers.Add("DPoP", jwt);
        using var response = await http.SendAsync(request, ct.Token);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct.Token));
        var error = body.RootElement.TryGetProperty("error", out var e) ? EvidenceProjection.Error(e.GetString()) : null;
        var token = body.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
        var next = response.Headers.TryGetValues("DPoP-Nonce", out var n) ? n.Single() : null;
        await Emit(step, (int)response.StatusCode, "observed", error, token is null ? null : EvidenceProjection.Token(token), EvidenceProjection.Proof(jwt));
        return ((int)response.StatusCode, error, next, token);
    }
    if (cfg["Variant"] == "nonce")
    {
        var endpoint = Endpoint("token_endpoint");
        var form = new Dictionary<string, string> { { "grant_type", "client_credentials" }, { "scope", "conference.quote" }, { "resource", "urn:mervikon:conference" }, { "proauth_tenant", settings.TenantId ?? throw new InvalidOperationException("Tenant required") } };
        var first = await Post(endpoint, form, "nonce");
        if (first.Error != "use_dpop_nonce" || string.IsNullOrEmpty(first.Nonce) || first.Token is not null)
            throw new InvalidOperationException("Nonce challenge not established");
        var retry = await Post(endpoint, form, "nonce", first.Nonce);
        if (retry.Status != 200 || string.IsNullOrEmpty(retry.Token))
            throw new InvalidOperationException("Nonce retry failed");
        using var handler = new DpopProofHandler(signer) { InnerHandler = new HttpClientHandler { AllowAutoRedirect = false } };
        using var client = new HttpClient(handler);
        using var quote = new HttpRequestMessage(HttpMethod.Get, cfg["Target"]);
        quote.Headers.Authorization = new("DPoP", retry.Token);
        using var result = await client.SendAsync(quote, ct.Token);
        await Emit("legitimate", (int)result.StatusCode, result.IsSuccessStatusCode ? "passed" : "failed");
        if (!result.IsSuccessStatusCode)
            throw new InvalidOperationException("Bound token positive control failed");
    }
    else if (cfg["Variant"] == "fapi")
    {
        var par = Endpoint("pushed_authorization_request_endpoint");
        Dictionary<string, string> Form() => new() { { "client_id", settings.ClientId }, { "response_type", "code" }, { "redirect_uri", cfg["RedirectUri"]! }, { "scope", "openid profile treasury.read" }, { "resource", "urn:mervikon:treasury" }, { "proauth_tenant", settings.TenantId ?? throw new InvalidOperationException("Tenant required") }, { "state", Guid.NewGuid().ToString("N") }, { "nonce", Guid.NewGuid().ToString("N") }, { "code_challenge", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_') }, { "code_challenge_method", "S256" } };
        var good = await Post(par, Form(), "fapi-par");
        if (good.Status != 201)
            throw new InvalidOperationException("Positive PAR control failed");
        var missing = Form();
        missing.Remove("code_challenge");
        missing.Remove("code_challenge_method");
        var noPkce = await Post(par, missing, "fapi-no-pkce");
        var plain = Form();
        plain["code_challenge_method"] = "plain";
        var plainResult = await Post(par, plain, "fapi-plain-pkce");
        var noProof = await Post(par, Form(), "fapi-no-proof", proof: false);
        // Front-channel attempt without a request_uri. No user session or credentials are supplied.
        var authorize = Endpoint("authorization_endpoint");
        var query = await new FormUrlEncodedContent(Form()).ReadAsStringAsync(ct.Token);
        using var response = await http.GetAsync(authorize + "?" + query, ct.Token);
        var location = response.Headers.Location;
        var redirectError = location is null ? null : Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(authorize, location).Query).GetValueOrDefault("error").ToString();
        await Emit("fapi-no-par", (int)response.StatusCode, "observed", string.IsNullOrEmpty(redirectError) ? null : EvidenceProjection.Error(redirectError));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            noParRedirectPath = location is null ? null : new Uri(authorize, location).AbsolutePath,
            noParError = EvidenceProjection.Error(redirectError),
            parWithoutProofAccepted = noProof.Status == 201,
            note = "PAR acceptance without a proof does not establish bearer token issuance. Token-endpoint binding is a separate control."
        }));
        if (noPkce.Status != 400 || noPkce.Error != "invalid_request" || plainResult.Status != 400 || plainResult.Error != "invalid_request" || redirectError != "invalid_request")
            throw new InvalidOperationException("At least one guardrail was not established; inspect actual outcomes");
    }
    else
        throw new InvalidOperationException("Unknown variant");
    await Emit("completed", null, "passed");
}
catch (Exception e) when (e is not OperationCanceledException) { await Emit("failed", null, "failed"); Console.Error.WriteLine($"Conference probe incomplete ({e.GetType().Name}); no raw remote response exported."); Environment.ExitCode = 1; }
