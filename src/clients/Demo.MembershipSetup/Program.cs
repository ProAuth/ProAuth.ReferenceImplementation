using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProAuth.Oidc.Client;
using ProAuth.Oidc.Client.Dpop;

if (args is ["validate-userstores"])
{
    foreach (var store in new[] { "manufacturing", "research", "partners" })
    {
        var connection = Environment.GetEnvironmentVariable($"MERVIKON_{store.ToUpperInvariant()}_CONNECTION_STRING");
        if (!UserStoreCatalog.Matches(connection, $"mervikon_{store}"))
        {
            Console.Error.WriteLine($"Configure one dedicated database for the {store} UserStore.");
            return 1;
        }
    }
    return 0;
}

// Operator-only readback. The Mervikon applications never receive this management credential.
// Bootstrap tooling links the fictional people to actual ProAuth subjects by reading the
// configured native UserStores. Client credentials plus DPoP protect this management read.
// Its output feeds operator-owned MembershipFile data; no management credential enters an API.
// See config/business/demo-members.json and scripts/demo-memberships.mjs for the mapping.
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Configuration["Authentication:ClientSecret"] = await Console.In.ReadToEndAsync();
builder.Services.AddOidcClient(builder.Configuration, "Authentication");
builder.Services.AddTransient<DpopProofHandler>();
builder.Services.AddHttpClient("management", c => c.MaxResponseContentBufferSize = 256 * 1024)
    .AddHttpMessageHandler<DpopProofHandler>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
using var host = builder.Build();
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
try
{
    var root = builder.Configuration["FixtureDirectory"] ?? "/workspace/config";
    var ids = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(root, "proauth/ownership.json"))).GetProperty("ids");
    var fixtures = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(root, "business/demo-members.json")));
    var issuer = new Uri(builder.Configuration["Authentication:Authority"]!);
    if (issuer.Scheme != "https" || issuer.UserInfo.Length != 0 || issuer.Query.Length != 0 || issuer.Fragment.Length != 0)
        throw new InvalidDataException("Set the exact HTTPS issuer.");
    var token = await host.Services.GetRequiredService<IOidcClient>().ClientCredentialsAsync(["openid"], ["ProAuthManagementService"], timeout.Token);
    if (!string.Equals(token.TokenType, "DPoP", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(token.AccessToken))
        throw new InvalidDataException("A sender-bound management token is required.");
    using var client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("management");
    client.BaseAddress = new Uri(issuer.AbsoluteUri.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("DPoP", token.AccessToken);
    var subjects = new Dictionary<string, Guid>();
    foreach (var fixture in fixtures.EnumerateArray())
    {
        var user = fixture.GetProperty("user").GetString()!;
        var storeId = ids.GetProperty(fixture.GetProperty("provider").GetString()!).GetGuid();
        var nativeId = ids.GetProperty(user).GetGuid();
        // The synchronized user lookup is bound to both the store and its immutable native user ID.
        var route = $"api/management/v2/ProAuthUser/idp/{storeId}/userstore/{nativeId}";
        for (var attempt = 0; ; attempt++)
        {
            using var response = await client.GetAsync(route, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound && attempt < 8)
            {
                await Task.Delay(1500, timeout.Token);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw new InvalidDataException($"{user}: management lookup returned HTTP {(int)response.StatusCode}. Check native UserStore synchronization and operator access.");
            using var data = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(timeout.Token));
            var account = data.RootElement;
            var subject = account.GetProperty("id").GetGuid();
            if (subject == Guid.Empty || account.GetProperty("subscriptionId").GetGuid() != ids.GetProperty("subscription").GetGuid()
                || !account.GetProperty("isUserActive").GetBoolean()
                || !string.Equals(account.GetProperty("scimExternalId").GetString(), $"{storeId}.{nativeId}", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{user}: unexpected linked identity.");
            subjects.Add(user, subject);
            break;
        }
    }
    Console.WriteLine(JsonSerializer.Serialize(subjects));
}
catch (Exception e)
{
    Console.Error.WriteLine(e is InvalidDataException ? e.Message : $"Demo identity lookup failed ({e.GetType().Name}). Check issuer, CA and management access.");
    return 1;
}
return 0;
