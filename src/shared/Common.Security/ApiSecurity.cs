using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using ProAuth.Oidc.Common.Discovery;
using ProAuth.Oidc.ResourceServer.AspNetCore;
using Procurement.Domain;

namespace Common.Security;

/// <summary>
/// Shared API conventions for Mervikon. Each service registers the ProAuth resource server,
/// its scope policies and its middleware explicitly in Program.cs. These helpers supply
/// common trust rules; endpoint and domain code still decide who may act on a business record.
/// </summary>
public static class ApiSecurity
{
    public static void AddBusinessMemberships(this WebApplicationBuilder builder)
    {
        // MembershipFile is operator-owned application data, not a source of host settings.
        // Validate at startup; Memberships also reloads assignments at each decision boundary.
        var assignments = new Memberships(builder.Configuration);
        assignments.Validate();
        builder.Services.AddSingleton(assignments);
    }

    public static void AddTrustedProxyForwarding(this WebApplicationBuilder builder)
    {
        // The external HTTPS URL matters to sender-proof validation. Accept forwarded scheme
        // and client address only through the configured proxy trust, with one forwarded hop.
        builder.Services.Configure<ForwardedHeadersOptions>(builder.Configuration.GetSection("ForwardedHeaders"));
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            if (builder.Configuration["TrustedProxyIp"] is { Length: > 0 } proxy)
                options.KnownProxies.Add(IPAddress.Parse(proxy));
        });
    }

    /// <summary>
    /// Pins discovery to the configured HTTPS issuer and restricts validation to access tokens.
    /// Audience and bearer/DPoP presentation policy come from each service's ResourceServer section.
    /// Signature, expiry and sender-proof validation remain the ProAuth package's responsibility.
    /// </summary>
    public static Action<ProAuthResourceServerOptions> CreateAccessTokenValidation(IConfiguration configuration)
    {
        var authority = configuration["ResourceServer:Authority"];
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Query != "" || uri.Fragment != "" || uri.UserInfo != "")
            throw new InvalidOperationException("Configure the exact HTTPS issuer.");

        // Discovery locates keys; it must not silently select a different authority to trust.
        var discovery = new IssuerBoundConfigurationManager(authority!.TrimEnd('/') + "/.well-known/openid-configuration",
            new IssuerBoundConfigurationRetriever(authority), new HttpDocumentRetriever { RequireHttps = true });
        // Build this callback at registration time so invalid issuer configuration still fails
        // before the host starts, rather than on its first authenticated request.
        return options =>
        {
            options.TokenValidationParameters.ConfigurationManager = discovery;
            options.TokenValidationParameters.ValidIssuer = authority;
            options.TokenValidationParameters.IgnoreTrailingSlashWhenValidatingAudience = false;
            options.TokenValidationParameters.ValidTypes = ["at+jwt", "application/at+jwt"];
            options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
        };
    }

    public static void AddScopePolicies(this IServiceCollection services, params string[] scopes)
    {
        // A scope grants access to an API capability. It does not grant a business role,
        // ownership of an order, an organization assignment or a spending limit.
        services.AddAuthorization(options =>
        {
            foreach (var scope in scopes)
                options.AddPolicy(scope, policy => policy.RequireAuthenticatedUser()
                    .RequireAssertion(context => HasScope(context.User, scope)));
        });
    }

    public static void UseBusinessPolicyErrors(this WebApplication app)
    {
        // Translate domain denials consistently, without exposing exception details.
        // Authentication and missing-scope challenges are handled earlier in the pipeline.
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (PolicyException error)
            {
                await Results.Problem(statusCode: PolicyErrors.StatusCode(error), title: error.Message).ExecuteAsync(context);
            }
        });
    }

    public static void MapIdentityEndpoints(this WebApplication app, string identityScope)
    {
        // /identity describes authenticated token context, including for workloads.
        // /me additionally requires a current human membership in Mervikon's business model.
        app.MapGet("/api/identity", (ClaimsPrincipal user) => new
        {
            issuer = ValidatedContext.One(user, "iss"),
            subject = ValidatedContext.One(user, "sub"),
            tenantId = ValidatedContext.One(user, "pa_iss_tenant")
        }).RequireAuthorization(identityScope);
        app.MapGet("/api/me", (ClaimsPrincipal user, Memberships memberships) => memberships.Require(user))
            .RequireAuthorization(identityScope);
    }

    public static bool HasScope(ClaimsPrincipal user, string scope) => user.FindAll("scope")
        .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .Contains(scope, StringComparer.Ordinal);

    public static void RequireClient(ClaimsPrincipal user, string? clientId)
    {
        // Use the validated issuer-provided client identity, never a caller-supplied header.
        if (string.IsNullOrWhiteSpace(clientId) || ValidatedContext.Client(user) != clientId)
            throw new PolicyException("client_not_authorized");
    }
}
