using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Common.Evidence;

/// <summary>
/// DEMO ONLY: bounded background delivery of sanitized API/business observations to Demo Console.
/// Business authorization never consults this queue. This is not a durable audit log.
/// </summary>
public sealed class Publisher(IHttpClientFactory factory, IConfiguration config, ILogger<Publisher> logger) : BackgroundService
{
    private readonly Channel<(bool Protocol, object Data)> queue = Channel.CreateBounded<(bool, object)>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });
    public void Record(Observation value)
    {
        if (value.IsValid())
            queue.Writer.TryWrite((false, value));
    }
    public void RecordProtocol(ProtocolObservation value)
    {
        if (config["Evidence:Endpoint"] is { Length: > 0 } && value.IsValid())
            queue.Writer.TryWrite((true, value));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var endpoint = config["Evidence:Endpoint"];
        var key = config["Evidence:PublisherKey"];
        if (string.IsNullOrEmpty(endpoint))
            return; // Observability is optional; it never decides authorization.
        if (key?.Length < 32 || string.IsNullOrEmpty(key))
            throw new InvalidOperationException("Configure an evidence publisher key of at least 32 characters.");
        var protocolEndpoint = new Uri(new Uri(endpoint), "/protocol-observations").AbsoluteUri;
        await foreach (var (protocol, value) in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, protocol ? protocolEndpoint : endpoint) { Content = JsonContent.Create(value, value.GetType()) };
                request.Headers.Add("X-Mervikon-Publisher", key);
                using var response = await factory.CreateClient("mervikon-evidence").SendAsync(request, stoppingToken);
                if (!response.IsSuccessStatusCode)
                    logger.LogWarning("Evidence delivery failed with HTTP {Status}; the recording is incomplete.", (int)response.StatusCode);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                if (!stoppingToken.IsCancellationRequested)
                    logger.LogWarning("Evidence delivery unavailable; the recording is incomplete.");
            }
        }
    }
}
