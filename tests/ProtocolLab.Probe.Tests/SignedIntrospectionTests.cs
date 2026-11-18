using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ProAuth.Oidc.ResourceServer.AspNetCore;
using ProAuth.Oidc.ResourceServer.AspNetCore.Introspection;
using Xunit;

public sealed class SignedIntrospectionTests
{
    [Theory]
    [InlineData("valid", true)]
    [InlineData("wrong-key", false)]
    [InlineData("unsigned", false)]
    [InlineData("wrong-issuer", false)]
    [InlineData("wrong-audience", false)]
    [InlineData("trailing-slash", false)]
    [InlineData("access-token-type", false)]
    [InlineData("missing-iat", false)]
    [InlineData("oldest-iat", true)]
    [InlineData("stale", false)]
    [InlineData("latest-iat", true)]
    [InlineData("expired", false)]
    [InlineData("future-iat", false)]
    [InlineData("future-nbf", false)]
    public async Task ValidatesTheSignedIntrospectionResponse(string variant, bool accepted)
    {
        using var trusted = RSA.Create(2048);
        using var other = RSA.Create(2048);
        var key = new RsaSecurityKey(trusted) { KeyId = "trusted" };
        var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };
        var clock = new FixedClock();
        var now = clock.GetUtcNow().UtcDateTime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = variant == "wrong-issuer" ? "https://other.example" : "https://issuer.example",
            Audience = variant == "wrong-audience" ? "other-client" : variant == "trailing-slash" ? "introspection-client/" : "introspection-client",
            TokenType = variant == "access-token-type" ? "at+jwt" : "token-introspection+jwt",
            IssuedAt = variant switch
            {
                "missing-iat" => null,
                "oldest-iat" => now.AddSeconds(-30),
                "stale" => now.AddSeconds(-31),
                "latest-iat" => now.AddSeconds(5),
                "future-iat" => now.AddSeconds(6),
                "expired" => now.AddSeconds(-20),
                _ => now
            },
            Expires = variant == "expired" ? now.AddSeconds(-10) : null,
            NotBefore = variant == "future-nbf" ? now.AddSeconds(20) : null,
            SigningCredentials = variant == "unsigned" ? null : new SigningCredentials(
                variant == "wrong-key" ? new RsaSecurityKey(other) { KeyId = "trusted" } : key, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object> { ["token_introspection"] = new Dictionary<string, object> { ["active"] = true } }
        };
        var options = new ProAuthResourceServerOptions();
        options.Introspection.Endpoint = "https://issuer.example/introspect";
        var profile = options.Introspection.JwtResponse;
        profile.Issuer = "https://issuer.example";
        profile.Audience = "introspection-client";
        profile.SigningKeys = [key];
        profile.MaxAge = TimeSpan.FromSeconds(25);
        profile.ClockSkew = TimeSpan.FromSeconds(5);

        using var http = new HttpClient(new EnvelopeHandler(handler.CreateToken(descriptor)));
        var client = new HttpAccessTokenIntrospectionClient(new ClientFactory(http), clock);
        var result = await client.IntrospectAsync("opaque-fixture", options);
        Assert.Equal(accepted, result.Active);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeSeconds(1800000000);
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class EnvelopeHandler(string envelope) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(envelope, System.Text.Encoding.UTF8, "application/token-introspection+jwt")
            });
    }
}
