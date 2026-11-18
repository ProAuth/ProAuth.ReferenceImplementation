using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Evidence;

/// <summary>
/// DEMO ONLY: shared OIDC event/back-channel adapter for Workplace, Finance Desk and the browser lab.
/// Publishes sanitized observations to Demo Console through Common.Evidence; it does not
/// implement sign-in, token renewal, assurance checks or business authorization.
/// The BFF projects link this one source file so the core evidence library needs no OIDC dependency.
/// </summary>
internal static class BffDemoEvidence
{
    public static void AddBffDemoEvidence(this IServiceCollection services, IConfiguration configuration, string source)
    {
        services.AddDemoEvidence();
        if (string.IsNullOrEmpty(configuration["Evidence:Endpoint"]))
            return;
        // Renewal uses the SDK's named HTTP client. Interactive sign-in uses the separate
        // ASP.NET Core OIDC back channel configured below. Both publish the same projection.
        ProtocolHttpObserver Observer(Publisher publisher) =>
            new(publisher, configuration["Evidence:Source"] ?? source);
        services.AddHttpClient("OidcClient")
            .AddHttpMessageHandler(sp => Observer(sp.GetRequiredService<Publisher>()));
        services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
            .Configure<Publisher>((options, publisher) =>
            {
                Guid Run(Microsoft.AspNetCore.Authentication.AuthenticationProperties? properties)
                {
                    if (properties is null)
                        return Guid.NewGuid();
                    if (!properties.Items.TryGetValue("mervikon-evidence-run", out var value) || !Guid.TryParse(value, out var id))
                    {
                        id = Guid.NewGuid();
                        properties.Items["mervikon-evidence-run"] = id.ToString();
                    }
                    return id;
                }
                var redirect = options.Events.OnRedirectToIdentityProvider;
                options.Events.OnRedirectToIdentityProvider = async c =>
                {
                    await redirect(c);
                    publisher.RecordProtocol(new(Run(c.Properties), DateTimeOffset.UtcNow, source, "authorization", null, "observed",
                        Challenge: c.ProtocolMessage.GetParameter("code_challenge")));
                };
                var code = options.Events.OnAuthorizationCodeReceived;
                options.Events.OnAuthorizationCodeReceived = async c =>
                {
                    await code(c);
                    var issuer = c.ProtocolMessage.GetParameter("iss");
                    publisher.RecordProtocol(new(Run(c.Properties), DateTimeOffset.UtcNow, source, "callback", null, "observed",
                        Issuer: issuer, VerifierPresent: !string.IsNullOrEmpty(c.TokenEndpointRequest?.GetParameter("code_verifier")), IssuerMatches: issuer is null ? null : issuer == c.Options.Authority));
                };
                var validated = options.Events.OnTokenValidated;
                options.Events.OnTokenValidated = async c =>
                {
                    await validated(c);
                    if (c.Result?.Failure is null && c.TokenEndpointResponse?.AccessToken is { } token)
                        publisher.RecordProtocol(new(Run(c.Properties), DateTimeOffset.UtcNow, source, "token", 200, "observed", EvidenceProjection.Token(token)));
                };
                // Observe the interactive flow through the BFF's configured OIDC transport.
                if (options.BackchannelHttpHandler is { } backchannel)
                {
                    var observer = Observer(publisher);
                    observer.InnerHandler = backchannel;
                    options.BackchannelHttpHandler = observer;
                }
            });
    }
}
