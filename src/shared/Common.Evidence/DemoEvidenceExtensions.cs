using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Evidence;

/// <summary>
/// DEMO ONLY: shared registrations and API-request adapter for Demo Console observations.
/// APIs and BFFs share these publishers, schemas and projections. APIs observe request outcomes;
/// the shared BffDemoEvidence adapter observes OIDC events and outbound back-channel messages.
/// None of these observations validate credentials or decide business authorization.
/// </summary>
public static class DemoEvidenceExtensions
{
    public static void AddDemoEvidence(this IServiceCollection services)
    {
        services.AddHttpClient("mervikon-evidence", c => c.Timeout = TimeSpan.FromSeconds(2))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<ProtocolPublisher>();
        services.AddSingleton<Publisher>();
        services.AddHostedService(s => s.GetRequiredService<Publisher>());
    }
    // Register after routing so operation metadata is available; wrap the security pipeline
    // to observe rejected requests as well as successful business results.
    public static void UseDemoEvidence(this WebApplication app, Component source)
    {
        app.Use(async (context, next) =>
        {
            await next(context);
            if (context.GetEndpoint()?.Metadata.GetMetadata<EvidenceOperation>() is not { } operation || Activity.Current is not { } activity)
                return;
            var business = context.Items[typeof(BusinessObservation)] as BusinessObservation;
            app.Services.GetRequiredService<Publisher>().Record(new(Guid.NewGuid(), DateTimeOffset.UtcNow, source, operation.Operation,
                context.Response.StatusCode, activity.TraceId.ToString(), activity.SpanId.ToString(), activity.ParentSpanId == default ? null : activity.ParentSpanId.ToString(),
                business?.Id, business?.State, business?.Revision));
        });
    }
}
