using Common.Evidence;

namespace DemoConsole;

internal sealed class RecordingStore
{
    private readonly object gate = new();
    private readonly List<Observation> observations = [];
    public void Add(Observation value)
    {
        lock (gate)
        {
            Prune();
            if (observations.Any(o => o.EventId == value.EventId))
                return;
            observations.Add(value);
            var traces = observations.Select(o => o.TraceId).Distinct().TakeLast(20).ToHashSet();
            observations.RemoveAll(o => !traces.Contains(o.TraceId));
            if (observations.Count > 1000)
                observations.RemoveRange(0, observations.Count - 1000);
        }
    }
    public object Export()
    {
        lock (gate)
        {
            Prune();
            return new
            {
                schemaVersion = 1,
                provenance = "recorded",
                exportedAt = DateTimeOffset.UtcNow,
                environment = "local",
                packages = "ProAuth " + BuildIdentity.ProAuthPackageVersion,
                evidenceCompleteness = "Partial: API HTTP observations only. Authentication-server and BFF spans are not collected.",
                observations = observations.ToArray()
            };
        }
    }
    private void Prune() => observations.RemoveAll(o => o.ObservedAt < DateTimeOffset.UtcNow.AddHours(-24));
}
