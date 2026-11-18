using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using ProAuth.Oidc.Client.Exceptions;

namespace Common.Security;

public static class UpstreamErrors
{
    public static void UseSanitizedUpstreamErrors(this WebApplication app) => app.Use(async (context, next) =>
    {
        try
        {
            await next(context);
        }
        catch (OidcErrorResponseException)
        {
            await Results.Problem(statusCode: 502, title: "upstream_oauth_request_rejected").ExecuteAsync(context);
        }
        catch (HttpRequestException)
        {
            await Results.Problem(statusCode: 502, title: "downstream_transport_failed").ExecuteAsync(context);
        }
        catch (TaskCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            await Results.Problem(statusCode: 504, title: "downstream_timeout").ExecuteAsync(context);
        }
    });
}
