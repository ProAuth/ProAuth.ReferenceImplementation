using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;

namespace Common.Evidence;

// Deliberately contains no raw token, proof, cookie, authorization code or verifier.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TokenSummary(string Fingerprint, string? Issuer, string? Audience, string? Scope,
    string? SubjectFingerprint, string? ActorFingerprint, string? Jkt, string? CertificateThumbprint, long? ExpiresAt);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProofSummary(string? Method, string? Target, string? TokenHash, string? IdFingerprint, bool NoncePresent);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProtocolObservation(Guid RunId, DateTimeOffset ObservedAt, string Source, string Step, int? HttpStatus,
    string Outcome, TokenSummary? Token = null, ProofSummary? Proof = null, string? Error = null,
    string? Challenge = null, string? Issuer = null, bool? VerifierPresent = null, bool? IssuerMatches = null,
    string? PresentedCertificateThumbprint = null)
{
    private static readonly HashSet<string> Sources = ["Workplace", "Finance", "Procurement", "Browser SPA", "Browser BFF", "Attacker", "Capture", "Sender lab", "Protocol probe", "Conference"];
    private static readonly HashSet<string> Steps = ["authorization", "callback", "token", "refresh", "refresh-reuse", "successor", "capture", "replay", "legitimate", "same-origin", "audience", "exchange-input", "exchange-output", "exchange-denied", "nonce", "fapi-par", "fapi-no-par", "fapi-no-pkce", "fapi-plain-pkce", "fapi-no-proof", "cleanup", "started", "completed", "failed"];
    public bool IsValid() => RunId != Guid.Empty && Sources.Contains(Source) && Steps.Contains(Step)
        && Outcome is "observed" or "passed" or "failed" or "running" or "unavailable"
        && HttpStatus is null or >= 100 and <= 599 && ObservedAt <= DateTimeOffset.UtcNow.AddMinutes(2)
        && Safe(Challenge, 128) && Safe(Issuer, 200) && Safe(PresentedCertificateThumbprint, 100) && Error == EvidenceProjection.Error(Error)
        && (Token is null || Safe(Token.Fingerprint, 64) && Safe(Token.Issuer, 200) && Safe(Token.Audience, 200)
            && Safe(Token.Scope, 300) && Safe(Token.SubjectFingerprint, 64) && Safe(Token.ActorFingerprint, 64)
            && Safe(Token.Jkt, 100) && Safe(Token.CertificateThumbprint, 100))
        && (Proof is null || Safe(Proof.Method, 10) && Safe(Proof.Target, 200) && Safe(Proof.TokenHash, 100) && Safe(Proof.IdFingerprint, 64));
    private static bool Safe(string? s, int max) => s is null || s.Length <= max && !s.Any(char.IsControl) && !System.Text.RegularExpressions.Regex.IsMatch(s, @"^[A-Za-z0-9_-]{16,}\.[A-Za-z0-9_-]{16,}\.[A-Za-z0-9_-]+$");
}
public static class EvidenceProjection
{
    public static string Fingerprint(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
    public static string? Error(string? value) => value switch
    {
        null => null,
        "invalid_grant" or "invalid_client" or "invalid_request" or "invalid_scope" or "invalid_target" or "invalid_dpop_proof" or "use_dpop_nonce" or "unauthorized_client" or "access_denied" or "invalid_request_uri" or "request_uri_not_supported" => value,
        _ => "unclassified_error"
    };
    public static JsonDocument? Decode(string? token)
    {
        try
        {
            var parts = token?.Split('.');
            if (parts?.Length != 3 || parts[1].Length > 24000)
                return null;
            var p = parts[1].Replace('-', '+').Replace('_', '/');
            var document = JsonDocument.Parse(Convert.FromBase64String(p.PadRight((p.Length + 3) / 4 * 4, '=')));
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                return document;
            document.Dispose();
            return null;
        }
        catch (Exception e) when (e is FormatException or JsonException) { return null; }
    }
    private static string? Text(JsonElement e, string key) => e.TryGetProperty(key, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind == JsonValueKind.Array ? string.Join(" ", v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString())) : null : null;
    public static TokenSummary Token(string value)
    {
        using var d = Decode(value);
        if (d is null)
            return new(Fingerprint(value), null, null, null, null, null, null, null, null);
        var e = d.RootElement;
        var cnf = e.TryGetProperty("cnf", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
        var sub = Text(e, "sub");
        var act = e.TryGetProperty("act", out var a) ? a.GetRawText() : null;
        return new(Fingerprint(value), Text(e, "iss"), Text(e, "aud"), Text(e, "scope"), sub is null ? null : Fingerprint(sub), act is null ? null : Fingerprint(act),
            cnf.ValueKind == JsonValueKind.Object ? Text(cnf, "jkt") : null, cnf.ValueKind == JsonValueKind.Object ? Text(cnf, "x5t#S256") : null,
            e.TryGetProperty("exp", out var exp) && exp.ValueKind == JsonValueKind.Number && exp.TryGetInt64(out var n) ? n : null);
    }
    public static ProofSummary? Proof(string? value)
    {
        using var d = Decode(value);
        if (d is null)
            return null;
        var e = d.RootElement;
        var id = Text(e, "jti");
        return new(Text(e, "htm"), Text(e, "htu"), Text(e, "ath"), id is null ? null : Fingerprint(id), !string.IsNullOrEmpty(Text(e, "nonce")));
    }
}
public sealed class ProtocolPublisher(IHttpClientFactory factory, IConfiguration configuration)
{
    public async Task Emit(ProtocolObservation value, CancellationToken ct = default)
    {
        if (!value.IsValid())
            return;
        var endpoint = configuration["Evidence:Endpoint"];
        if (string.IsNullOrEmpty(endpoint))
            return;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(endpoint), "/protocol-observations"));
            request.Headers.Add("X-Mervikon-Publisher", configuration["Evidence:PublisherKey"]);
            request.Content = JsonContent.Create(value);
            using var response = await factory.CreateClient("mervikon-evidence").SendAsync(request, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { /* Observation never changes authorization. */ }
    }
}
