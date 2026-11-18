using System.Text.Json;
using Group.Provisioner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProAuth.Oidc.Client;
using ProAuth.Oidc.Client.Dpop;

// Operator-side, one bounded native read. Keep credentials outside both read-only application mounts.
// Optional native-group approval profile: read one configured ProAuth group and publish a
// short-lived eligibility fact. Application APIs never query the directory during a request.
// Client credentials and DPoP protect the management call; config/proauth/optional-groups defines
// this operator client. GroupEligibility consumes the fact alongside business memberships, so
// a group cannot grant an organization, spending limit or permission to approve one's own order.
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Services.AddOidcClient(builder.Configuration, "Authentication");
builder.Services.AddTransient<DpopProofHandler>();
builder.Services.AddHttpClient("native", c => c.MaxResponseContentBufferSize = 256 * 1024)
    .AddHttpMessageHandler<DpopProofHandler>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
using var host = builder.Build();
var config = builder.Configuration;
var dir = config["GroupEligibilityDir"] ?? throw new InvalidOperationException("Set GroupEligibilityDir.");
var path = Path.Combine(dir, "eligibility.json");
// Serialize runs before the source read so an older observation cannot overwrite a removal.
using var writer = new FileStream(Path.Combine(dir, ".writer.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
try
{
    var issuer = config["Authentication:Authority"];
    if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "")
        throw new InvalidOperationException();
    using var ownership = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "ownership.json")));
    var ids = ownership.RootElement.GetProperty("ids");
    var store = ids.GetProperty("idp-manufacturing").GetGuid();
    var group = ids.GetProperty("group-purchasing-approvers").GetGuid();
    var tenant = ids.GetProperty("tenant-workforce").GetGuid().ToString();
    if (config["Authentication:TenantId"] != tenant)
        throw new InvalidOperationException();
    var token = await host.Services.GetRequiredService<IOidcClient>().ClientCredentialsAsync(["openid"], ["ProAuthManagementService"], deadline.Token);
    if (!string.Equals(token.TokenType, "DPoP", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(token.AccessToken))
        throw new InvalidOperationException();
    using var client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("native");
    var mapping = new NativeMapping(issuer!, tenant, store, group, ids.GetProperty("user-amir").GetGuid(), "amir");
    await PublishEligibility.FromGroup(client, token.AccessToken, mapping, path, deadline.Token);
    Console.WriteLine("Purchasing eligibility replaced from the native UserStore.");
}
catch (Exception)
{
    File.Delete(path); // A failed run withdraws eligibility; a crashed/stopped writer also ages out.
    Console.Error.WriteLine("Native group readback or publication failed. No current eligibility published.");
    Environment.ExitCode = 1;
}
