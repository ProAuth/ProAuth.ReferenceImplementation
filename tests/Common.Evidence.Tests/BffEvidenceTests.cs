using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProAuth.Bff.Extensions;
using Xunit;

namespace Common.Evidence.Tests;

public class BffEvidenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DemoHooksAreOptionalAndPreserveTheExistingOidcTransport(bool enabled)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Evidence:Endpoint"] = enabled ? "http://evidence.invalid/observations" : null
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        using var transport = new HttpClientHandler();
        Func<RedirectContext, Task> redirect = _ => Task.CompletedTask;
        services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
            .Configure(options =>
            {
                options.BackchannelHttpHandler = transport;
                options.Events.OnRedirectToIdentityProvider = redirect;
            });
        services.AddBffDemoEvidence(configuration, "Workplace");
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);

        if (enabled)
        {
            var observer = Assert.IsType<ProtocolHttpObserver>(options.BackchannelHttpHandler);
            Assert.Same(transport, observer.InnerHandler);
            Assert.NotSame(redirect, options.Events.OnRedirectToIdentityProvider);
        }
        else
        {
            Assert.Same(transport, options.BackchannelHttpHandler);
            Assert.Same(redirect, options.Events.OnRedirectToIdentityProvider);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RenewalClientIncludesTheObserverOnlyWhenEvidenceIsEnabled(bool enabled)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ProAuthBff:AuthenticationSettings:Authority"] = "https://issuer.example",
            ["ProAuthBff:AuthenticationSettings:ClientId"] = "test-client",
            ["Evidence:Endpoint"] = enabled ? "http://evidence.invalid/observations" : null
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddBff(configuration);
        services.AddBffDemoEvidence(configuration, "Workplace");
        using var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();
        var handler = factory.CreateHandler("OidcClient");
        var observers = 0;
        while (handler is DelegatingHandler delegating)
        {
            if (handler is ProtocolHttpObserver)
                observers++;
            handler = Assert.IsAssignableFrom<HttpMessageHandler>(delegating.InnerHandler);
        }
        Assert.Equal(enabled ? 1 : 0, observers);
        var primary = Assert.IsType<HttpClientHandler>(handler);
        Assert.False(primary.UseCookies);
        Assert.False(primary.AllowAutoRedirect);
    }
}
