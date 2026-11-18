using System.Security.Claims;
using System.Text.Json;
using Procurement.Domain;

namespace Common.Security;

/// <summary>
/// Restricts delegated order import to Procurement acting for a user. The token's subject is
/// the user; act identifies the exchanging service. Mervikon permits exactly this single actor,
/// not an arbitrary delegation chain. Scope and audience checks still apply independently.
/// </summary>
public static class ExchangeActor
{
    // The resource-server package has already validated token signature, audience and
    // DPoP possession. These checks narrow the business endpoint to the configured actor.
    public static void Require(ClaimsPrincipal user, string? expectedClient, string? expectedIssuer)
    {
        ApiSecurity.RequireClient(user, expectedClient);
        var actorClaim = ValidatedContext.One(user, "act");
        var bindingClaim = ValidatedContext.One(user, "cnf");
        if (string.IsNullOrWhiteSpace(expectedIssuer))
            throw new PolicyException("exchange_context_required");
        try
        {
            using var actor = JsonDocument.Parse(actorClaim);
            using var binding = JsonDocument.Parse(bindingClaim);
            if (actor.RootElement.ValueKind != JsonValueKind.Object || binding.RootElement.ValueKind != JsonValueKind.Object
                || actor.RootElement.EnumerateObject().Count(p => p.Name == "sub") != 1
                || actor.RootElement.EnumerateObject().Count(p => p.Name == "iss") != 1
                || actor.RootElement.GetProperty("sub").GetString() != expectedClient
                || actor.RootElement.GetProperty("iss").GetString() != expectedIssuer
                || actor.RootElement.TryGetProperty("act", out _)
                || binding.RootElement.EnumerateObject().Count(p => p.Name == "jkt") != 1
                || !binding.RootElement.TryGetProperty("jkt", out var key) || key.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(key.GetString()))
                throw new PolicyException("exchange_context_required");
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new PolicyException("exchange_context_required");
        }
    }
}
