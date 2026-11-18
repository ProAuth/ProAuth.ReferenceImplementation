using Mervikon.Transport;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProAuth.Oidc.Client;
using ProAuth.Oidc.Client.Native;
using ProAuth.Oidc.Client.Exceptions;
using Workflow.Domain;

// Device authorization connects a terminal to a human without collecting credentials here.
// ProAuth owns the verification page; AddNativeClient handles polling, expiry and protocol errors.
// Authentication is supplied by deployment; config/proauth/clients/22-warehouse.yml registers
// this public client. It uses bearer tokens for Fulfilment read/receive, not a client secret.
// The API still requires the authenticated person's warehouse membership and order assignment.
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddHttpClient().ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false }));
builder.Services.AddOidcClient(builder.Configuration, "Authentication");
builder.Services.AddNativeClient();
using var host = builder.Build();
var origin = HttpsOrigin.Require(builder.Configuration["ApiOrigin"]);
var operation = builder.Configuration["Operation"] ?? "list";
if (operation is not ("identity" or "list" or "receive"))
    throw new InvalidOperationException("Unknown terminal operation.");
if (operation == "receive" && (!Guid.TryParse(builder.Configuration["OrderId"], out _) || !int.TryParse(builder.Configuration["Revision"], out _)))
    throw new InvalidOperationException("Receive requires OrderId and Revision from a listed order.");
using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    var result = await host.Services.GetRequiredService<INativeAuthClient>().LoginWithDeviceCodeAsync((device, _) =>
    {
        // The user code belongs on this terminal, never in shared evidence/telemetry. The device code is never printed.
        if (!string.IsNullOrWhiteSpace(device.VerificationUriComplete))
            Console.WriteLine($"Open in your browser: {device.VerificationUriComplete}");
        Console.WriteLine($"Manual entry: {device.VerificationUri}");
        Console.WriteLine($"User code: {device.UserCode}");
        Console.WriteLine("This terminal never asks for your password. Waiting for ProAuth authorization…");
        return Task.CompletedTask;
    }, ["openid", "fulfilment.read", "fulfilment.receive"], ["urn:mervikon:fulfilment"], cancellation.Token);
    if (string.IsNullOrWhiteSpace(result.TokenResponse.AccessToken) || !string.Equals(result.TokenResponse.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Expected a bearer access token for this terminal.");
    using var http = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient();
    http.BaseAddress = origin;
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.TokenResponse.AccessToken);
    using var response = operation == "receive"
        ? await http.PostAsJsonAsync($"/api/orders/{Guid.Parse(builder.Configuration["OrderId"]!)}/receive", new
        {
            revision = int.Parse(builder.Configuration["Revision"]!)
        }, cancellation.Token)
        : await http.GetAsync(operation == "identity" ? "/api/identity" : "/api/orders", cancellation.Token);
    Console.WriteLine($"Warehouse {operation}: API HTTP {(int)response.StatusCode}");
    if (!response.IsSuccessStatusCode)
        Environment.ExitCode = 1;
    else if (operation == "identity")
        Console.WriteLine(await response.Content.ReadAsStringAsync(cancellation.Token));
    else if (operation == "list")
    {
        foreach (var order in await response.Content.ReadFromJsonAsync<SupplierOrder[]>(cancellation.Token) ?? [])
            Console.WriteLine($"{order.Purchase.PurchaseId} | {order.Purchase.ProductId} | {order.Status} | revision {order.Revision}");
    }
}
catch (OidcErrorResponseException)
{
    Console.Error.WriteLine("Device authorization was rejected by ProAuth.");
    Environment.ExitCode = 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Device operation cancelled or expired.");
    Environment.ExitCode = 1;
}
catch (HttpRequestException)
{
    Console.Error.WriteLine("Device transport failed.");
    Environment.ExitCode = 1;
}
