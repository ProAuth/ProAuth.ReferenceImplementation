using Procurement.Domain;

namespace Common.Security;

// One local entitlement from a trusted provisioning job. No directory credentials or group lists in the API.
public sealed record GroupEligibility(string Issuer, string TenantId, string? EligiblePersonId, DateTimeOffset ObservedAt)
{
    public static bool Allows(string path, string issuer, string tenant, string person, DateTimeOffset now)
    {
        try
        {
            var fact = AssignmentJson.Read<GroupEligibility>(path, 2048);
            return fact.Issuer == issuer && fact.TenantId == tenant && fact.EligiblePersonId == person &&
                fact.ObservedAt <= now && now - fact.ObservedAt < TimeSpan.FromMinutes(1);
        }
        catch (PolicyException)
        {
            return false;
        }
    }
}
