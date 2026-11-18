using Microsoft.AspNetCore.Http;
using ProAuth.Oidc.Common.Assurance;
using ProAuth.Oidc.ResourceServer.AspNetCore.Assurance;

namespace Common.Security;

/// <summary>
/// Shared assurance requirement for high-value purchase approval and payment release.
/// The ProAuth API extension evaluates validated acr/auth_time and emits the authentication
/// challenge. The browser can request verification, but only the API decides if it is sufficient.
/// See each BFF's StepUpPolicies and config/proauth/clients/21-workflows.yml for the other side.
/// </summary>
public static class SensitiveActionAssurance
{
    // Accept these exact authentication contexts with at most five minutes of age.
    // Do not rank arbitrary acr strings or infer strength from a role or successful sign-in.
    // BFF verification requests fresh authentication (MaximumAgeSeconds = 0); this API allows
    // the completed verification to remain usable for five minutes while the user confirms.
    public static AuthenticationAssuranceRequirement Requirement
    {
        get;
    } =
        new(["https://proauth.net/acr/mfa", "phr"], MaximumAgeSeconds: 300);

    public static IResult? Challenge(HttpContext context) => context.RequireAuthenticationAssurance(Requirement);
}
