using System.Text.Json.Serialization;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LabComparison(string Variant, DateTimeOffset ObservedAt, int LegitimateBefore, int TokenOnly,
    int? CapturedProof, int LegitimateAfter, int ExpiredToken, bool CleanupVerified, ProofControls? ProofControls = null)
{
    public bool IsValid() => Variant is "bearer" or "dpop" && new[] { LegitimateBefore, TokenOnly, LegitimateAfter, ExpiredToken }.All(s => s is >= 100 and <= 599)
        && (CapturedProof is null or >= 100 and <= 599) && (ProofControls is null || ProofControls.IsValid()) && ObservedAt <= DateTimeOffset.UtcNow.AddMinutes(2);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ProofControls(int WrongMethod, int WrongUri, int WrongHash, int WrongKey)
{
    public bool IsValid() => new[] { WrongMethod, WrongUri, WrongHash, WrongKey }.All(s => s is >= 100 and <= 599);
}
