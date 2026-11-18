using System.Security.Claims;
using Microsoft.AspNetCore.WebUtilities;

namespace Mervikon.Apps;

/// <summary>
/// Navigates to ProAuth-managed account security (including passkey enrollment). The BFF does
/// not implement WebAuthn ceremonies. Even a signed navigation claim is checked against the
/// configured issuer origin, fixed path and tenant before being used as a redirect target.
/// Claim configuration: config/proauth/policies/31-account-security.yml.
/// </summary>
public static class AccountSecurityLink
{
    public static string? Resolve(ClaimsPrincipal user, string? authority, string? tenant)
    {
        if (user.Identity?.IsAuthenticated != true || !Guid.TryParse(tenant, out _))
            return null;
        var claims = user.FindAll("pa_account_management_url").ToArray();
        if (claims.Length != 1 || !Uri.TryCreate(claims[0].Value, UriKind.Absolute, out var link)
            || !Uri.TryCreate(authority, UriKind.Absolute, out var issuer) || link.Scheme != "https"
            || link.GetLeftPart(UriPartial.Authority) != issuer.GetLeftPart(UriPartial.Authority)
            || link.AbsolutePath != "/account/security" || link.UserInfo != "" || link.Fragment != "")
            return null;
        var query = QueryHelpers.ParseQuery(link.Query);
        if (query.Count != 2 || !query.TryGetValue("tenantid", out var targetTenant) || targetTenant.Count != 1 || targetTenant[0] != tenant
            || !query.TryGetValue("idpinstanceid", out var provider) || provider.Count != 1 || !Guid.TryParse(provider[0], out _))
            return null;
        return link.AbsoluteUri;
    }
}
