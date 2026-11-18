using System.Diagnostics;
using Procurement.Domain;

namespace Procurement.Api;

public sealed record ObservedEvent(long Sequence, DateTimeOffset ObservedAt, string Source,
    string Operation, string Outcome, int HttpStatus, string? BusinessState, int? Revision, string TraceId);

/// <summary>Allowlisted, bounded observations. Never accepts headers, token values, exceptions or arbitrary payloads.</summary>
public sealed class EvidenceJournal
{
    private readonly Queue<(string Actor, string Organization, ObservedEvent Event)> events = new();
    private readonly object gate = new();
    private long sequence;
    public void Record(Member actor, string operation, string outcome, int status, Purchase? purchase = null)
    {
        lock (gate)
        {
            events.Enqueue((actor.Id, actor.OrganizationId, new(++sequence, DateTimeOffset.UtcNow,
                "Procurement.Api", operation, outcome, status, purchase?.Status, purchase?.Revision,
                Activity.Current?.TraceId.ToString() ?? string.Empty)));
            while (events.Count > 500)
                events.Dequeue();
        }
    }
    public ObservedEvent[] For(Member actor)
    {
        lock (gate)
            return events.Where(e => e.Actor == actor.Id && e.Organization == actor.OrganizationId).Select(e => e.Event).ToArray();
    }
}
