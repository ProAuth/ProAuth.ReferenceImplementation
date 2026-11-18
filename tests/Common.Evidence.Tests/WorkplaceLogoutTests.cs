using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProAuth.Bff.Configuration;
using ProAuth.Bff.Controllers;
using Xunit;

namespace Common.Evidence.Tests;

public class WorkplaceLogoutTests
{
    [Fact]
    public async Task LogoutGetRendersWorkplaceViewWithBothProtectedSignOutChoices()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(StepUpEndpoints).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Services.AddControllersWithViews();
        builder.Services.Configure<BffOptions>(builder.Configuration.GetSection("ProAuthBff"));
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        await using var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Scheme = "https";
        http.Request.PathBase = "/workplace";
        http.Response.Body = new MemoryStream();
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var controller = ActivatorUtilities.CreateInstance<AccountController>(scope.ServiceProvider);
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        controller.Url = new UrlHelper(action);

        var view = Assert.IsType<ViewResult>(controller.LogoutGet("/workplace/?tab=one&item=two"));
        await view.ExecuteResultAsync(action);
        http.Response.Body.Position = 0;
        using var reader = new StreamReader(http.Response.Body);
        var html = await reader.ReadToEndAsync();
        Assert.Contains("href=\"/workplace/mervikon-shell.css\"", html);
        Assert.Contains("method=\"post\" action=\"/workplace/bff/account/logout\"", html);
        Assert.Matches("name=\"__RequestVerificationToken\" value=\"[^\"]+\"", html);
        Assert.Contains("name=\"logoutMode\" value=\"app\"", html);
        Assert.Contains("name=\"logoutMode\" value=\"sso\"", html);
        Assert.Contains("href=\"/workplace/?tab=one&amp;item=two\">Cancel", html);
    }
}
