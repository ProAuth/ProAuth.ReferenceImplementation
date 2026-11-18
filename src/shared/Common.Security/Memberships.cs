using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Procurement.Domain;
using Workflow.Domain;

namespace Common.Security;

public sealed record IdentityMembership(string Issuer, string Subject, string TenantId, Member Member, string[]? SubjectAliases = null)
{
    // Pairwise subjects must be independently verified in this same issuer and tenant.
    public IEnumerable<string> Subjects() => (SubjectAliases ?? []).Prepend(Subject);
}

public sealed record WorkloadAssignment(string Issuer, string TenantId, string ClientId, Partition[] Partitions);

/// <summary>
/// Maps a validated (issuer, subject, tenant) to a stable business person, or an authenticated
/// workload to explicit business partitions. OAuth scopes alone do not define these assignments.
/// Read the operator-owned document at each decision so removal takes effect without token renewal.
/// Never add it as an IConfiguration provider: business data cannot configure issuer/proxy trust.
/// </summary>
public sealed class Memberships(IConfiguration configuration)
{
    private readonly string path = configuration["MembershipFile"] ?? "memberships.json";
    private readonly string mode = configuration["ApprovalProfile"] ?? "Direct";
    private readonly string eligibilityPath = configuration["GroupEligibilityFile"] ?? "/run/group-approval/eligibility.json";

    private sealed class Document
    {
        public required IdentityMembership[] Memberships
        {
            get; init;
        }
        public WorkloadAssignment[] Workloads { get; init; } = [];
    }

    public void Validate() => Read();

    private Document Read()
    {
        if (mode is not ("Direct" or "NativeGroup"))
            throw Invalid();
        var doc = AssignmentJson.Read<Document>(path, 256 * 1024);
        foreach (var row in doc.Memberships)
        {
            if (row is null || Blank(row.Issuer) || Blank(row.TenantId) || row.Subjects().Any(Blank))
                throw Invalid();
            var member = row.Member;
            if (member is null || Blank(member.Id) || member.Id.Trim() != member.Id || Blank(member.OrganizationId)
                || Blank(member.CostCentre) || member.ApprovalLimitChf < 0 || member.Roles.Any(Blank))
                throw Invalid();
        }

        var identities = doc.Memberships.SelectMany(m => m.Subjects().Select(subject => (m.Issuer, Subject: subject, m.TenantId, m.Member.Id))).ToArray();
        if (identities.GroupBy(k => (k.Issuer, k.Subject, k.TenantId)).Any(g => g.Count() != 1)
            || identities.GroupBy(k => (k.Issuer, k.Subject)).Any(g => g.Select(k => k.Id).Distinct().Count() != 1)
            || doc.Memberships.GroupBy(m => m.Member.Id, StringComparer.OrdinalIgnoreCase).Any(g => g.Select(m => m.Member.Id).Distinct().Count() != 1))
            throw Invalid();

        foreach (var job in doc.Workloads)
        {
            if (job is null || Blank(job.Issuer) || Blank(job.TenantId) || Blank(job.ClientId)
                || job.Partitions.Any(p => p is null || Blank(p.OrganizationId) || Blank(p.CostCentre))
                || job.Partitions.Distinct().Count() != job.Partitions.Length)
                throw Invalid();
        }
        if (doc.Workloads.GroupBy(w => (w.Issuer, w.TenantId, w.ClientId)).Any(g => g.Count() != 1))
            throw Invalid();
        return doc;
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
    private static PolicyException Invalid() => new("membership_configuration_unavailable");

    // A service subject is not a human membership. Bind its grant to issuer, tenant AND client.
    public IReadOnlySet<Partition> RequireWorkload(ClaimsPrincipal user, string? expectedClient)
    {
        ApiSecurity.RequireClient(user, expectedClient);
        _ = ValidatedContext.One(user, "sub"); // Check cardinality; service subjects are not person assignments.
        var issuer = ValidatedContext.One(user, "iss");
        var tenant = ValidatedContext.One(user, "pa_iss_tenant");
        var grant = Read().Workloads.SingleOrDefault(w => w.Issuer == issuer && w.TenantId == tenant && w.ClientId == expectedClient);
        return grant?.Partitions.ToHashSet() ?? throw new PolicyException("workload_assignment_required");
    }

    // Resolve the stable person before separation-of-duties checks. Verified subject aliases
    // allow pairwise subject identifiers without turning one person into multiple approvers.
    public Member Require(ClaimsPrincipal user)
    {
        var issuer = ValidatedContext.One(user, "iss");
        var subject = ValidatedContext.One(user, "sub");
        var tenant = ValidatedContext.One(user, "pa_iss_tenant");
        var member = Read().Memberships.SingleOrDefault(m => m.Issuer == issuer && m.TenantId == tenant && m.Subjects().Contains(subject, StringComparer.Ordinal))?.Member
            ?? throw new PolicyException("membership_required");
        if (mode == "NativeGroup" && member.Roles.Contains("approver") &&
            !GroupEligibility.Allows(eligibilityPath, issuer, tenant, member.Id, DateTimeOffset.UtcNow))
            return member with
            {
                Roles = member.Roles.Where(r => r != "approver").ToArray()
            };
        return member;
    }
}
