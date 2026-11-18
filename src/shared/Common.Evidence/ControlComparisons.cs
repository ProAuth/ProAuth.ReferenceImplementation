using System.Text.Json.Serialization;

namespace Common.Evidence;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ControlComparisons(ProtocolControl[] Protocols, ReplicaControl? Replicas)
{
    public bool IsValid() => Protocols is not null && Protocols.Length <= 4 && Protocols.All(p => p is not null && p.IsValid())
        && Protocols.Select(p => p.Variant).Distinct().Count() == Protocols.Length && (Replicas is null || Replicas.IsValid());
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProtocolControl(string Variant, DateTimeOffset ObservedAt, int Legitimate, int InvalidToken, int AfterRevocation, int Expired,
    bool RevocationResponseProcessed, int? MissingCertificate, int? WrongCertificate, int? ForgedCertificateHeader, int? TamperedCiphertext)
{
    public bool IsValid() => Variant is "jwt" or "opaque" or "jwe" or "mtls" && EvidenceRange.Time(ObservedAt)
        && new[] { Legitimate, InvalidToken, AfterRevocation, Expired }.All(EvidenceRange.Status)
        && new[] { MissingCertificate, WrongCertificate, ForgedCertificateHeader, TamperedCiphertext }.All(v => v is null || EvidenceRange.Status(v.Value));
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReplicaControl(DateTimeOffset ObservedAt, int First, int Replay, int FreshOne, int FreshTwo, int ConcurrentRequests,
    int Accepted, int Rejected, int OutageOne, int OutageTwo, int RecoveredOne, int RecoveredTwo, int ReplayAfterRestart, bool InstancesVerified, bool CleanupVerified)
{
    public bool IsValid() => EvidenceRange.Time(ObservedAt) && ConcurrentRequests is > 0 and <= 16 && Accepted >= 0 && Rejected >= 0 && Accepted + Rejected == ConcurrentRequests
        && new[] { First, Replay, FreshOne, FreshTwo, OutageOne, OutageTwo, RecoveredOne, RecoveredTwo, ReplayAfterRestart }.All(EvidenceRange.Status);
}
internal static class EvidenceRange
{
    public static bool Status(int value) => value is >= 100 and <= 599;
    public static bool Time(DateTimeOffset value) => value.Year >= 2026 && value <= DateTimeOffset.UtcNow.AddMinutes(2);
}
