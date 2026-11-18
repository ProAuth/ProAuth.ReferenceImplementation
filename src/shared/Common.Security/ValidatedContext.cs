using System.Security.Claims;
using Procurement.Domain;

namespace Common.Security;

// Call only after resource-server authentication. Never assemble a key from multiple identities.
public static class ValidatedContext
{
    public static string Client(ClaimsPrincipal user)
    {
        var trusted = One(user, "pa_iss_clientapp");
        if (user.HasClaim(c => string.Equals(c.Type, "client_id", StringComparison.OrdinalIgnoreCase)) && One(user, "client_id") != trusted)
            throw new PolicyException("client_not_authorized");
        return trusted;
    }

    public static string One(ClaimsPrincipal user, string type)
    {
        var identities = user.Identities.ToArray();
        if (identities.Length == 1 && identities[0].IsAuthenticated &&
            identities[0].FindAll(type).ToArray() is [var claim] && claim.Type == type && !string.IsNullOrWhiteSpace(claim.Value))
            return claim.Value;
        throw new PolicyException("ambiguous_or_missing_identity");
    }
}
