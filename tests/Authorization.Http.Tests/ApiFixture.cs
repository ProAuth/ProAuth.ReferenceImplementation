using Microsoft.AspNetCore.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ProAuth.Oidc.Client;
using ProAuth.Oidc.Client.Dpop;
using ProAuth.Oidc.Common.Protocol;
using ProAuth.Oidc.ResourceServer.AspNetCore;
using Common.Evidence;
using Common.Security;


namespace Authorization.Http.Tests;

// Kestrel + the application's resource-server handler. Only discovery key material and AS responses
// are fixtures. A bad audience, type, signature or sender proof still fails in the product handler.
public sealed class ApiFixture : IAsyncDisposable
{
    public const string Issuer = "https://issuer.example";
    public const string Tenant = "workforce";
    private readonly RSA rsa = RSA.Create(2048);
    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("mervikon-http-");
    private readonly List<WebApplication> apps = [];
    public string FilePath => Path.Combine(dir.FullName, "memberships.json");

    public void Write(object document)
    {
        File.WriteAllText(FilePath + ".next", JsonSerializer.Serialize(document));
        File.Move(FilePath + ".next", FilePath, overwrite: true);
    }

    public string Token(string api, string scope, string subject, string client = "workplace", string tenant = Tenant,
        string issuer = Issuer, string type = "at+jwt", Dictionary<string, object>? additional = null)
    {
        var claims = new Dictionary<string, object> { ["sub"] = subject, ["scope"] = scope, ["pa_iss_tenant"] = tenant, ["pa_iss_clientapp"] = client, ["client_id"] = client };
        foreach (var pair in additional ?? [])
            claims[pair.Key] = pair.Value;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = "urn:mervikon:" + api,
            TokenType = type,
            Claims = claims,
            SigningCredentials = new(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256),
            Expires = DateTime.UtcNow.AddMinutes(4)
        });
    }

    public async Task<HttpClient> Start(string api, Component component, string[] scopes, Action<WebApplicationBuilder> setup, Action<WebApplication> routes)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = dir.FullName });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["MembershipFile"] = FilePath;
        builder.Configuration["ResourceServer:Authority"] = Issuer;
        builder.Configuration["ResourceServer:Audience"] = "urn:mervikon:" + api;
        setup(builder);
        builder.AddBusinessMemberships();
        builder.Services.AddDemoEvidence();
        builder.AddTrustedProxyForwarding();
        builder.Services.AddResourceServerAuthentication(builder.Configuration,
            ApiSecurity.CreateAccessTokenValidation(builder.Configuration));
        builder.Services.AddScopePolicies(scopes);
        builder.Services.PostConfigureAll<ProAuthResourceServerOptions>(o =>
        {
            o.TokenValidationParameters.ConfigurationManager = null;
            o.TokenValidationParameters.IssuerSigningKey = new RsaSecurityKey(rsa.ExportParameters(false));
        });
        var app = builder.Build();
        apps.Add(app);
        app.UseForwardedHeaders();
        app.UseRouting();
        app.UseDemoEvidence(component);
        app.UseSanitizedUpstreamErrors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseBusinessPolicyErrors();
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
        app.MapIdentityEndpoints(scopes[0]);
        routes(app);
        await app.StartAsync();
        return new()
        {
            BaseAddress = new(app.Urls.Single())
        };
    }

    public static async Task<HttpResponseMessage> Send(HttpClient http, string path, string token, object? body = null, IDpopProofService? proof = null)
    {
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, new Uri(http.BaseAddress!, path));
        request.Headers.Authorization = new AuthenticationHeaderValue(proof is null ? "Bearer" : "DPoP", token);
        proof?.ApplyProof(request, token);
        request.Headers.Add("Idempotency-Key", "purchase-1");
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await http.SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var app in apps.AsEnumerable().Reverse())
            await app.DisposeAsync();
        rsa.Dispose();
        dir.Delete(true);
    }
}

public class TokenEndpoint : DispatchProxy
{
    private Func<string, object?[], TokenResponse> issue = null!;
    public static IOidcClient Stub(Func<string, object?[], TokenResponse> issue)
    {
        var stub = Create<IOidcClient, TokenEndpoint>();
        ((TokenEndpoint)(object)stub).issue = issue;
        return stub;
    }
    protected override object Invoke(MethodInfo? method, object?[]? args) => Task.FromResult(issue(method!.Name, args!));
}
