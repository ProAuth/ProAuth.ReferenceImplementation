using Mervikon.Transport;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Shared session and middleware conventions for the three browser applications.</summary>
internal static class BffHosting
{
    public static void AddBffSessionInfrastructure(this WebApplicationBuilder builder, string application)
    {
        foreach (var route in builder.Configuration.GetSection("ProAuthBff:Routes").GetChildren())
            HttpsOrigin.Require(route["Destination"]);
        builder.Services.AddHealthChecks();
        builder.Services.AddAntiforgery();
        builder.Services.AddAuthorization(options => options.AddPolicy("authenticated-user",
            policy => policy.RequireAuthenticatedUser()));
        // ASP.NET Core session uses its own cache prefix alongside the ProAuth ticket store.
        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = builder.Configuration["ProAuthBff:Redis:Cache:ConnectionString"];
            options.InstanceName = $"mervikon:{application}:cache:";
        });
        builder.Services.AddHttpContextAccessor();
    }

    public static void ConfigureBffCookie(this WebApplicationBuilder builder, string application)
    {
        builder.Services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.Cookie.Name = $"__Host-mervikon-{application}";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
        });
    }

    public static void MapShellStylesheet(this WebApplication app)
    {
        // Share the application stylesheet with the server-rendered step-up confirmation.
        app.MapGet("/mervikon-shell.css", () => Results.Stream(
            typeof(BffHosting).Assembly.GetManifestResourceStream("Mervikon.AppShell.css")!,
            "text/css")).AllowAnonymous();
    }

    public static void UseAuthenticatedApiBoundary(this WebApplication app)
    {
        // These BFFs proxy protected resources under /api. Reject anonymous requests locally
        // even if discovery is unavailable. The SDK's proxy still applies its authorization
        // policy and CSRF checks; this middleware does not replace either protection.
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api") && context.User.Identity?.IsAuthenticated != true)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.CacheControl = "no-store";
                return;
            }
            await next(context);
        });
    }
}
