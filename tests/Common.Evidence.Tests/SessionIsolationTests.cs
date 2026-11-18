using ProAuth.Oidc.Client.AspNetCore;
using ProAuth.Bff.Extensions;
using ProAuth.Oidc.Client.InMemory;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProAuth.Oidc.Client.TokenHandling;
using Xunit;

namespace Common.Evidence.Tests;

public class SessionIsolationTests
{
    [Fact]
    public async Task VerificationInOneSessionCannotUpgradeAnotherSessionForTheSameSubject()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.Configuration["ProAuthBff:AuthenticationSettings:Authority"] = "https://issuer.example";
        builder.Configuration["ProAuthBff:AuthenticationSettings:ClientId"] = "finance";
        builder.AddBffSessionInfrastructure("finance");
        builder.Services.AddBff(builder.Configuration)
            .WithInProcessLocking()
            .WithInMemoryTicketStore()
            .WithAuthSessionTokenStore();
        builder.ConfigureBffCookie("finance");
        // Only storage, keys and locks are local fixtures. Use the actual cookie and session registrations.
        builder.Services.AddSingleton<IDistributedCache, MemoryDistributedCache>();
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        await using var app = builder.Build();
        var oidc = app.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OpenIdConnectDefaults.AuthenticationScheme);
        var transport = oidc.BackchannelHttpHandler;
        while (transport is DelegatingHandler handler)
            transport = handler.InnerHandler;
        var primary = Assert.IsType<HttpClientHandler>(transport);
        Assert.False(primary.AllowAutoRedirect);
        Assert.False(primary.UseCookies);

        async Task<string> Request(string? cookie, string? signInAs)
        {
            using var scope = app.Services.CreateScope();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Request.Scheme = "https";
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            accessor.HttpContext = context;
            try
            {
                if (cookie is not null)
                    context.Request.Headers.Cookie = cookie;
                if (signInAs is null)
                {
                    var store = scope.ServiceProvider.GetRequiredService<ITokenStore>();
                    return (await store.GetUserTokens("same-person"))!.AccessToken!;
                }
                var props = new AuthenticationProperties();
                props.StoreTokens([
                    new() { Name = "access_token", Value = signInAs },
                    new() { Name = "refresh_token", Value = "fixture-refresh" },
                    new() { Name = "expires_at", Value = DateTimeOffset.UtcNow.AddMinutes(5).ToString("O") }
                ]);
                var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "same-person")], "Cookies"));
                await context.SignInAsync("Cookies", user, props);
                return context.Response.Headers.SetCookie.Single(x => x!.StartsWith("__Host-"))!.Split(';')[0];
            }
            finally { accessor.HttpContext = null; }
        }

        var first = await Request(null, "ordinary-A");
        var second = await Request(null, "ordinary-B");
        Assert.NotEqual(first, second);
        first = await Request(first, "verified-A");
        Assert.Equal("verified-A", await Request(first, null));
        Assert.Equal("ordinary-B", await Request(second, null));
    }
}
