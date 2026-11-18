using Common.Evidence;
using System.Reflection;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProAuth.Oidc.Client;
using ProAuth.Oidc.Client.Configuration;
using ProAuth.Oidc.Client.Dpop;

// INTENTIONALLY UNSAFE DEMONSTRATION. These dedicated, short-lived tokens traverse a malicious logging hop.
// Deliberately instrumented client for the bearer-versus-DPoP replay experiment. Its trusted
// lab hop can capture actual request credentials. The replay result is meaningful only beside
// the original successful request; this transport is not a pattern for application clients.
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Services.AddHttpClient();
builder.Services.AddDemoEvidence();
builder.Services.AddOidcClient(builder.Configuration, "Authentication");
using var host = builder.Build();
var oidc = host.Services.GetRequiredService<IOidcClient>();
var proof = host.Services.GetRequiredService<IDpopProofService>();
var evidence = host.Services.GetRequiredService<ProtocolPublisher>();
var runId = Guid.TryParse(builder.Configuration["RunId"], out var configuredRun) ? configuredRun : Guid.NewGuid();
var target = new Uri(builder.Configuration["Target"]!);
if (target.Scheme != "https" || target.AbsolutePath != "/api/quote") throw new InvalidOperationException("Fixed HTTPS lab target required.");
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
using var http = new HttpClient(new DpopProofHandler(proof) { InnerHandler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false } });
using var control = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
control.DefaultRequestHeaders.Add("X-Lab-Control", builder.Configuration["ControlKey"]);
Console.WriteLine("INTENTIONALLY UNSAFE DEMONSTRATION: trusted TLS hop captures actual credentials. Normal reference traffic never uses it.");
string? token = null;
var cleanup = false;
var stage = "token acquisition";

var interactive = builder.Configuration["DemoMode"] == "true";
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
async Task DemoWait(string action)
{
    if (!interactive)
        return;
    Console.WriteLine("DEMO_WAIT:" + action);
    if (await Console.In.ReadLineAsync(deadline.Token) != action)
        throw new InvalidOperationException("Expected the next guided demonstration action.");
}
void DemoObserved(string step, int? status, TokenSummary? summary = null, ProofSummary? proofSummary = null, string? certificateThumbprint = null)
{
    if (interactive)
        Console.WriteLine("DEMO_EVENT:" + JsonSerializer.Serialize(
        new ProtocolObservation(runId, DateTimeOffset.UtcNow, "Sender lab", step, status, "observed", summary, proofSummary, PresentedCertificateThumbprint: certificateThumbprint), jsonOptions));
}

try
{
    await evidence.Emit(new(runId, DateTimeOffset.UtcNow, "Sender lab", "started", null, "running"));
    await DemoWait("legitimate");
    var start = DateTimeOffset.UtcNow;
    var issued = await oidc.ClientCredentialsAsync(["lab.quote"], ["urn:mervikon:lab"], deadline.Token);
    Console.WriteLine($"Issued access-token lifetime: {issued.ExpiresIn} seconds.");
    token = issued.AccessToken ?? throw new InvalidOperationException("No token issued");
    if (issued.ExpiresIn is <= 0 or > 45)
        throw new InvalidOperationException("Lab requires a token lifetime at most 45 seconds");
    async Task<HttpResponseMessage> Legitimate()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, target);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Lab-Run", runId.ToString());
        return await http.SendAsync(request, deadline.Token);
    }
    stage = "legitimate control";
    using var before = await Legitimate();
    Console.WriteLine($"Legitimate control: HTTP {(int)before.StatusCode}.");
    if (!before.IsSuccessStatusCode)
        throw new InvalidOperationException("Legitimate control failed; attack result cannot demonstrate the intended lesson");
    await evidence.Emit(new(runId, DateTimeOffset.UtcNow, "Sender lab", "legitimate", (int)before.StatusCode, "observed", EvidenceProjection.Token(token)));
    var handle = Guid.Parse(before.Headers.GetValues("X-Lab-Artifact").Single());
    DemoObserved("legitimate", (int)before.StatusCode, EvidenceProjection.Token(token));
    if (builder.Configuration["ReplicaMode"] == "true")
    {
        if (!proof.IsEnabled)
            throw new InvalidOperationException("Replica control requires DPoP.");
        using var raw = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
        async Task<int> OnReplica(string instance, string signedProof)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            request.Headers.Authorization = new AuthenticationHeaderValue("DPoP", token);
            request.Headers.Add("DPoP", signedProof);
            request.Headers.Add("X-Lab-Replica", instance);
            using var response = await raw.SendAsync(request, deadline.Token);
            if (!response.Headers.TryGetValues("X-Mervikon-Instance", out var values) || values.Single() != instance)
                throw new InvalidOperationException("Actual API instance was not observed.");
            return (int)response.StatusCode;
        }
        string Fresh() => proof.CreateProof(new DpopProofRequest(HttpMethod.Get, target, token));
        var sharedProof = Fresh();
        var first = await OnReplica("one", sharedProof);
        var replay = await OnReplica("two", sharedProof);
        var freshOne = await OnReplica("one", Fresh());
        var freshTwo = await OnReplica("two", Fresh());
        var raceProof = Fresh();
        var race = await Task.WhenAll(Enumerable.Range(0, 16).Select(i => OnReplica(i % 2 == 0 ? "one" : "two", raceProof)));
        if (first != 200 || replay != 401 || freshOne != 200 || freshTwo != 200 || race.Count(s => s == 200) != 1 || race.Count(s => s == 401) != 15)
            throw new InvalidOperationException("Cross-replica replay control failed.");
        // The operator wrapper stops only the dedicated lab Redis and acknowledges through stdin.
        Console.WriteLine("REPLICA_OUTAGE_REQUEST");
        if (await Console.In.ReadLineAsync(deadline.Token) != "stopped")
            throw new InvalidOperationException("Outage was not confirmed.");
        var outageOne = await OnReplica("one", Fresh());
        var outageTwo = await OnReplica("two", Fresh());
        if (outageOne != 503 || outageTwo != 503)
            throw new InvalidOperationException("Unavailable replay store did not fail closed as designed.");
        Console.WriteLine("REPLICA_RECOVERY_REQUEST");
        if (await Console.In.ReadLineAsync(deadline.Token) != "started")
            throw new InvalidOperationException("Recovery was not confirmed.");
        // Redis readiness and each existing client connection recover asynchronously.
        // Keep the original token/proof so expiry cannot masquerade as replay rejection.
        var recoveryDeadline = DateTimeOffset.UtcNow.AddSeconds(10);
        async Task<int> Recover(string instance)
        {
            while (DateTimeOffset.UtcNow < recoveryDeadline)
            {
                var result = await OnReplica(instance, Fresh());
                if (result != 503)
                    return result;
                await Task.Delay(300, deadline.Token);
            }
            return 503;
        }
        stage = "replica recovery";
        var recovered = await Task.WhenAll(Recover("one"), Recover("two"));
        var recoveredOne = recovered[0];
        var recoveredTwo = recovered[1];
        var replayAfterRestart = await OnReplica("two", sharedProof);
        Console.WriteLine($"Recovery: instance one HTTP {recoveredOne}, instance two HTTP {recoveredTwo}, previous proof HTTP {replayAfterRestart}; elapsed {(DateTimeOffset.UtcNow - start).TotalSeconds:F1}s.");
        if (recoveredOne != 200 || recoveredTwo != 200 || replayAfterRestart != 401)
            throw new InvalidOperationException("Replay state did not survive recovery.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            scenario = "SCN-22",
            observedAt = DateTimeOffset.UtcNow,
            first,
            replay,
            freshOne,
            freshTwo,
            concurrentRequests = race.Length,
            accepted = race.Count(s => s == 200),
            rejected = race.Count(s => s == 401),
            outageOne,
            outageTwo,
            recoveredOne,
            recoveredTwo,
            replayAfterRestart,
            instancesVerified = true,
            store = "dedicated Redis atomic SET NX with expiry",
            evidence = "Observed HTTP outcomes from two named API processes"
        }));
    }
    else
    {
        if (interactive)
        {
            await DemoWait("capture");
            using var inspection = await control.PostAsync($"http://lab-runner:8080/inspect/{handle}", null, deadline.Token);
            inspection.EnsureSuccessStatusCode();
            var captured = await inspection.Content.ReadFromJsonAsync<ReplayResult>(deadline.Token) ?? throw new InvalidOperationException("Missing capture summary.");
            DemoObserved("capture", null, captured.TokenSummary, captured.ProofSummary);
            await evidence.Emit(new(runId, DateTimeOffset.UtcNow, "Capture", "capture", null, "observed", captured.TokenSummary, captured.ProofSummary));
            await DemoWait("replay");
        }
        stage = "runner replay";
        using var attack = await control.PostAsync($"http://lab-runner:8080/replay/{handle}", null, deadline.Token);
        if (!attack.IsSuccessStatusCode)
            throw new InvalidOperationException("Runner failed");
        var result = await attack.Content.ReadFromJsonAsync<ReplayResult>(deadline.Token) ?? throw new InvalidOperationException("Missing observation");
        await evidence.Emit(new(runId, DateTimeOffset.UtcNow, "Capture", "capture", null, "observed", result.TokenSummary, result.ProofSummary));
        await evidence.Emit(new(runId, DateTimeOffset.UtcNow, "Sender lab", "replay", result.TokenOnly, "observed", EvidenceProjection.Token(token)));
        if (result.CapturedProof is { } replayStatus)
            await evidence.Emit(new(runId, DateTimeOffset.UtcNow, "Sender lab", "replay", replayStatus, "observed"));
        DemoObserved("replay", result.TokenOnly, result.TokenSummary, result.ProofSummary);
        if (result.CapturedProof is { } copiedStatus)
            DemoObserved("replay", copiedStatus);
        ProofControls? proofControls = null;
        if (proof.IsEnabled)
        {
            // Independent negative controls use newly signed proofs, not previously consumed proofs.
            using var raw = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
            async Task<int> InvalidProof(IDpopProofService signer, HttpMethod method, Uri uri, string hashInput)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, target);
                request.Headers.Authorization = new AuthenticationHeaderValue("DPoP", token);
                request.Headers.Add("DPoP", signer.CreateProof(new DpopProofRequest(method, uri, hashInput)));
                using var response = await raw.SendAsync(request, deadline.Token);
                return (int)response.StatusCode;
            }
            using var wrongKey = new DpopProofService(Options.Create(new AuthenticationSettings
            {
                SenderConstrainedTokenMode = SenderConstrainedTokenMode.Dpop,
                Dpop = new DpopSettings { GenerateEphemeralKeyWhenMissing = true, SigningAlgorithm = "PS256" }
            }));
            proofControls = new(await InvalidProof(proof, HttpMethod.Post, target, token),
                await InvalidProof(proof, HttpMethod.Get, new Uri(target, "/api/other"), token),
                await InvalidProof(proof, HttpMethod.Get, target, "different-access-token"),
                await InvalidProof(wrongKey, HttpMethod.Get, target, token));
            if (new[] { proofControls.WrongMethod, proofControls.WrongUri, proofControls.WrongHash, proofControls.WrongKey }.Any(s => s != 401))
                Environment.ExitCode = 1;
        }
        using var after = await Legitimate();
        await evidence.Emit(new(runId, DateTimeOffset.UtcNow, "Sender lab", "legitimate", (int)after.StatusCode, "observed", EvidenceProjection.Token(token)));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            scenario = "SCN-11",
            variant = proof.IsEnabled ? "dpop" : "bearer",
            legitimateBefore = (int)before.StatusCode,
            result.TokenOnly,
            result.CapturedProof,
            proofControls,
            legitimateAfter = (int)after.StatusCode,
            observedAt = DateTimeOffset.UtcNow,
            package = System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>().Single(a => a.Key == "ProAuthPackageVersion").Value,
            evidence = "HTTP results observed; validator-specific rejection cause unobserved",
            fixture = "calibration-station / revision 1",
            attackerHasPrivateKey = false
        }));
        var expected = before.IsSuccessStatusCode && after.IsSuccessStatusCode && (proof.IsEnabled ? result.TokenOnly == 401 && result.CapturedProof == 401 : result.TokenOnly == 200);
        if (!expected)
            Environment.ExitCode = 1;
    }
    if (interactive)
        Console.WriteLine("DEMO_CLEANUP");
    stage = "revocation and expiry cleanup";
    try
    {
        await oidc.RevokeTokenAsync(token, "access_token", deadline.Token);
        Console.WriteLine("Revocation response processed.");
    }
    catch (Exception) { Console.WriteLine("Revocation response could not be processed; cleanup requires independently observed expiry rejection."); }
    // JWT consumers may validate locally: do not mistake a successful revocation response for immediate API rejection.
    var remaining = start.AddSeconds(issued.ExpiresIn + 2) - DateTimeOffset.UtcNow;
    if (remaining > TimeSpan.Zero)
        await Task.Delay(remaining, deadline.Token);
    using var expired = await Legitimate();
    cleanup = expired.StatusCode == System.Net.HttpStatusCode.Unauthorized;
    Console.WriteLine($"Cleanup: expired-token API HTTP {(int)expired.StatusCode}.");
}
catch (Exception e) { Console.Error.WriteLine($"Lab incomplete during {stage} ({e.GetType().Name}). No token or exception payload is exported."); Environment.ExitCode = 1; }
finally
{
    if (!cleanup && token is not null)
    {
        try
        {
            await oidc.RevokeTokenAsync(token, "access_token", CancellationToken.None);
        }
        catch (Exception) { /* cleanup remains incomplete */ }
    }
    try
    {
        using var purge = await control.DeleteAsync("http://lab-capture:8081/captures");
        cleanup &= purge.IsSuccessStatusCode;
    }
    catch (HttpRequestException) { cleanup = false; }
    Console.WriteLine(cleanup ? "Cleanup verified: expired token rejected and ephemeral captures removed." : "Cleanup incomplete: stop this isolated lab before another run; never publish its artifacts.");
    await evidence.Emit(new(runId, DateTimeOffset.UtcNow, "Sender lab", "cleanup", null, cleanup ? "passed" : "failed"));
    if (!cleanup)
        Environment.ExitCode = 1;
    else
        DemoObserved("cleanup", 401);
}
internal sealed record ReplayResult(int TokenOnly, int? CapturedProof, TokenSummary? TokenSummary, ProofSummary? ProofSummary);
internal sealed record ProofControls(int WrongMethod, int WrongUri, int WrongHash, int WrongKey);
