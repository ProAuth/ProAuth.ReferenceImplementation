using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;

/// <summary>
/// Application-owned confirmation UI before the package-owned authentication journey.
/// The fixed policy name selects an allowlisted BFF StepUpPolicy; no request body or arbitrary
/// challenge parameters are forwarded. After verification the user reviews and confirms again.
/// </summary>
public static class StepUpEndpoints
{
    // This page issues no authentication request and carries no business request body.
    // Only an explicit form POST enters the product's bounded step-up controller.
    public static void MapStepUpConfirmation(this WebApplication app, string policy, string applicationTitle)
    {
        using var catalogue = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalogue.json")));
        var company = catalogue.RootElement.GetProperty("company");
        var encode = HtmlEncoder.Default;
        var brand = encode.Encode(company.GetProperty("displayName").GetString()!);
        var notice = encode.Encode(company.GetProperty("notice").GetString()!);
        var title = encode.Encode(applicationTitle);
        app.MapGet("/step-up", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            context.Response.Headers.CacheControl = "no-store";
            var home = encode.Encode(context.Request.PathBase + "/");
            var stylesheet = encode.Encode(context.Request.PathBase + "/mervikon-shell.css");
            var identity = encode.Encode(context.Request.PathBase + "/api/identity");
            var logout = encode.Encode(context.Request.PathBase + "/bff/account/logout");
            var action = encode.Encode(context.Request.PathBase + "/bff/account/step-up");
            var returnUrl = encode.Encode(context.Request.PathBase + "/?assurance=returned");
            return Results.Content($"""
                <!doctype html>
                <html lang="en">
                <head>
                  <meta charset="utf-8">
                  <meta name="viewport" content="width=device-width, initial-scale=1">
                  <title>Verify before confirming · {title}</title>
                  <link rel="stylesheet" href="{stylesheet}">
                </head>
                <body>
                  <div class="workspace">
                    <aside>
                      <a class="brand" href="{home}">{brand}</a>
                      <span class="eyebrow">{title}</span>
                      <nav aria-label="Main">
                        <a class="selected" href="{home}">{title}</a>
                        <a href="{identity}">My verified identity</a>
                      </nav>
                      <p class="fiction">{notice}</p>
                    </aside>
                    <main>
                      <header><span>{title}</span><a href="{logout}">Sign out</a></header>
                      <section class="intro">
                        <p class="eyebrow">IDENTITY VERIFICATION</p>
                        <h1>Verify before confirming</h1>
                        <p>Use an enrolled user-verified passkey or complete a configured MFA method.</p>
                      </section>
                      <section class="step-up" aria-label="Verification">
                        <p class="message">No purchase or payment will change during verification. Afterwards, review the current record and confirm it again.</p>
                        <form method="post" action="{action}" class="step-up-actions">
                          <input type="hidden" name="{encode.Encode(tokens.FormFieldName)}" value="{encode.Encode(tokens.RequestToken!)}">
                          <input type="hidden" name="policy" value="{encode.Encode(policy)}">
                          <input type="hidden" name="returnUrl" value="{returnUrl}">
                          <button type="submit">Verify identity</button>
                          <a href="{home}">Cancel and return</a>
                        </form>
                      </section>
                      <footer>Mervikon Group</footer>
                    </main>
                  </div>
                </body>
                </html>
                """, "text/html");
        }).RequireAuthorization("authenticated-user");
    }
}
