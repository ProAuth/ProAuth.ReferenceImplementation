using System.Net;
using Microsoft.Extensions.Options;
using ProAuth.Oidc.Client.Configuration;
using ProAuth.Oidc.Client;
using Xunit;

public sealed class DiscoveryTests
{
    private const string Issuer = "https://issuer.example/mervikon-protocols";
    [Fact]
    public async Task PreservesIssuerPath()
    {
        var factory = new Factory(Issuer, Issuer);
        var result = await Create(factory).GetConfiguration();
        Assert.False(result.HasError);
        Assert.Equal(Issuer + "/.well-known/openid-configuration", factory.Requested);
    }
    [Fact]
    public async Task RejectsRootIssuerSubstitution() => Assert.True((await Create(new Factory("https://issuer.example", Issuer)).GetConfiguration()).HasError);
    [Fact]
    public async Task AcceptsHttpsEndpointFromValidatedIssuerMetadata() => Assert.False((await Create(new Factory(Issuer, "https://other.example")).GetConfiguration()).HasError);
    [Fact]
    public async Task RejectsInsecureIssuerBeforeNetwork()
    {
        var factory = new Factory(Issuer, Issuer);
        var discovery = new OidcDiscoveryClient(Options.Create(new AuthenticationSettings { Authority = "http://issuer.example" }), factory);
        Assert.True((await discovery.GetConfiguration()).HasError);
        Assert.Null(factory.Requested);
    }
    private static OidcDiscoveryClient Create(Factory factory) => new(Options.Create(new AuthenticationSettings { Authority = Issuer }), factory);
    private sealed class Factory(string issuer, string endpoint) : IHttpClientFactory
    {
        public string? Requested
        {
            get; private set;
        }
        public HttpClient CreateClient(string name) => new(new Handler(request =>
        {
            Requested = request.RequestUri!.AbsoluteUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new
                {
                    issuer,
                    jwks_uri = endpoint + "/keys",
                    authorization_endpoint = endpoint + "/authorize",
                    response_types_supported = new[] { "code" },
                    subject_types_supported = new[] { "public" },
                    id_token_signing_alg_values_supported = new[] { "RS256" },
                    token_endpoint = endpoint + "/connect/token",
                    revocation_endpoint = endpoint + "/connect/revoke",
                    introspection_endpoint = endpoint + "/connect/introspect"
                }))
            };
        }));
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
