using Mervikon.Transport;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProAuth.Oidc.Client;
using ProAuth.Oidc.Client.Exceptions;
using Workflow.Domain;

// Dispatch and reconciliation are autonomous jobs: client credentials identify the workload,
// not an employee. Authentication comes from the deployment environment; each job has a distinct
// registration in config/proauth/clients/21-workflows.yml and intentionally uses bearer tokens.
// The API verifies scope and an operator-owned workload assignment before exposing business data.
// Dispatch can advance accepted orders; reconciliation can read but cannot release payments.
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddHttpClient().ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false }));
builder.Services.AddOidcClient(builder.Configuration, "Authentication");
using var host = builder.Build();
var task = builder.Configuration["WorkflowTask"] ?? "reconcile";
if (task is not ("dispatch" or "reconcile" or "identity"))
    throw new InvalidOperationException("Unknown worker task.");
var origin = HttpsOrigin.Require(builder.Configuration["ApiOrigin"]);
var dispatch = task is "dispatch" or "identity";
var oidc = host.Services.GetRequiredService<IOidcClient>();
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
try
{
    var token = await oidc.ClientCredentialsAsync(dispatch ? ["fulfilment.read", "fulfilment.write"] : ["treasury.reconcile"],
        [dispatch ? "urn:mervikon:fulfilment" : "urn:mervikon:treasury"], deadline.Token);
    if (string.IsNullOrWhiteSpace(token.AccessToken) || !string.Equals(token.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Expected a bearer access token for this workload.");
    using var http = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient();
    http.BaseAddress = origin;
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
    var path = task == "identity" ? "/api/identity" : dispatch ? "/api/dispatch" : "/api/reconciliation";
    using var response = await http.GetAsync(path, deadline.Token);
    Console.WriteLine($"{task}: API HTTP {(int)response.StatusCode}");
    if (!response.IsSuccessStatusCode)
    {
        Environment.ExitCode = 1;
        return;
    }
    if (task == "identity")
    {
        Console.WriteLine(await response.Content.ReadAsStringAsync(deadline.Token));
        return;
    }
    if (dispatch)
    {
        var orders = await response.Content.ReadFromJsonAsync<DispatchItem[]>(deadline.Token) ?? [];
        foreach (var order in orders)
        {
            using var result = await http.PostAsJsonAsync($"/api/dispatch/{order.PurchaseId}", new
            {
                order.Revision
            }, deadline.Token);
            Console.WriteLine($"Dispatch {order.PurchaseId}: HTTP {(int)result.StatusCode}");
            if (!result.IsSuccessStatusCode)
                Environment.ExitCode = 1;
        }
    }
    else
    {
        var payments = await response.Content.ReadFromJsonAsync<ReconciliationItem[]>(deadline.Token) ?? [];
        Console.WriteLine($"Read {payments.Length} internal payment instructions; no release authority.");
    }
}
catch (OidcErrorResponseException)
{
    Console.Error.WriteLine("Workload token request rejected by the issuer.");
    Environment.ExitCode = 1;
}
catch (HttpRequestException)
{
    Console.Error.WriteLine("Workload transport failed.");
    Environment.ExitCode = 1;
}
