using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Common.Evidence;

// Observe SDK traffic for Demo Console. Projection deliberately drops raw tokens, credentials
// and verifier material. Decoded summaries are educational observations, never trusted claims
// for authorization; only the resource-server authentication handler establishes that trust.
public sealed class ProtocolHttpObserver(Publisher publisher, string source) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var run = Activity.Current is { } activity ? Guid.ParseExact(activity.TraceId.ToString(), "N") : Guid.NewGuid();
        var isForm = request.Content?.Headers.ContentType?.MediaType == "application/x-www-form-urlencoded";
        var form = isForm ? QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(ct)) : null;
        var response = await base.SendAsync(request, ct);
        if (form is null)
            return response;
        var grant = form.GetValueOrDefault("grant_type").ToString();
        var step = grant switch
        {
            "refresh_token" => "refresh",
            "urn:ietf:params:oauth:grant-type:token-exchange" => "exchange-output",
            _ => "token"
        };
        if (form.ContainsKey("code_challenge"))
            step = "authorization";
        TokenSummary? token = null;
        string? error = null;
        if (response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            try
            {
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (body.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (body.RootElement.TryGetProperty("access_token", out var t) && t.ValueKind == JsonValueKind.String)
                        token = EvidenceProjection.Token(t.GetString()!);
                    if (body.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                        error = EvidenceProjection.Error(e.GetString());
                }
            }
            catch (JsonException) { }
        }
        publisher.RecordProtocol(new(run, DateTimeOffset.UtcNow, source, step, (int)response.StatusCode, "observed", token,
            request.Headers.TryGetValues("DPoP", out var proofs) ? EvidenceProjection.Proof(proofs.FirstOrDefault()) : null, error,
            form.GetValueOrDefault("code_challenge").ToString(), VerifierPresent: form.ContainsKey("code_verifier")));
        return response;
    }
}
