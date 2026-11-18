using System.Net.Http.Headers;
using Common.Evidence;
using System.Security.Claims;
using ProAuth.Oidc.Client;
using Procurement.Domain;
using Common.Security;
using Workflow.Domain;

namespace Treasury.Api;

/// <summary>
/// Finance combines DPoP-protected human operations, client-credentials receipt retrieval and
/// a read-only reconciliation workload. These are distinct authorities, not interchangeable
/// tokens. PaymentBook owns the transaction rules; ProAuth owns protocol validation.
/// </summary>
public static class TreasuryEndpoints
{
    public static void MapTreasuryEndpoints(this WebApplication app)
    {
        var routes = app.MapGroup("").ObserveBusinessResults();
        // The resource-server handler has already verified the binding and proof for
        // DPoP presentations. Only reconciliation uses the separate bearer workload.
        var finance = routes.MapGroup("/api/payments").RequireAuthorization(policy =>
            policy.RequireAuthenticatedUser().RequireAssertion(c => c.Resource is HttpContext http &&
                http.Request.Headers.Authorization.ToString().StartsWith("DPoP ", StringComparison.OrdinalIgnoreCase)));
        finance.MapGet("", ListPayments)
            .RequireAuthorization("treasury.read")
            .WithMetadata(new EvidenceOperation(Operation.ListPayments));
        finance.MapPost("/synchronize", SynchronizeReceipts)
            .RequireAuthorization("treasury.write")
            .WithMetadata(new EvidenceOperation(Operation.PreparePayments));
        finance.MapPost("/{id:guid}/release", ReleasePayment)
            .RequireAuthorization("treasury.write")
            .WithMetadata(new EvidenceOperation(Operation.ReleasePayment));
        routes.MapGet("/api/reconciliation", ReadReconciliation)
            .RequireAuthorization("treasury.reconcile")
            .WithMetadata(new EvidenceOperation(Operation.Reconcile));

        PaymentInstruction[] ListPayments(ClaimsPrincipal user, Memberships memberships, PaymentBook book)
        {
            ApiSecurity.RequireClient(user, app.Configuration["FinanceClientId"]);
            return book.List(memberships.Require(user));
        }

        async Task<IResult> SynchronizeReceipts(ClaimsPrincipal user, Memberships memberships, PaymentBook book,
            IOidcClient oidc, IHttpClientFactory factory, CancellationToken ct)
        {
            ApiSecurity.RequireClient(user, app.Configuration["FinanceClientId"]);
            var member = memberships.Require(user);
            if (!member.Roles.Contains("finance"))
                throw new PolicyException("finance_required");
            // This is workload authority, not delegation of the Finance Desk user's token.
            // Treasury authenticates with its own credential; Fulfilment assigns this client
            // a bounded set of buyer/cost-centre partitions (21-workflows.yml + MembershipFile).
            var token = await oidc.ClientCredentialsAsync(scopes: ["fulfilment.reconcile"], resources: ["urn:mervikon:fulfilment"], cancellationToken: ct);
            if (string.IsNullOrWhiteSpace(token.AccessToken) || !string.Equals(token.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
                return Results.Problem(statusCode: 502, title: "workload_authentication_failed");
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/received?organizationId={Uri.EscapeDataString(member.OrganizationId)}&costCentre={Uri.EscapeDataString(member.CostCentre)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            using var response = await factory.CreateClient("fulfilment").SendAsync(request, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                throw new PolicyException("receipt_read_not_authorized");
            if (!response.IsSuccessStatusCode)
                return Results.Problem(statusCode: 502, title: "fulfilment_read_failed");
            var receipts = await response.Content.ReadFromJsonAsync<Receipt[]>(ct);
            // An outbound call can take time. Re-read the human assignment before importing
            // receipts and reject any result outside that same business partition.
            var current = memberships.Require(user);
            if (!current.Roles.Contains("finance") || current.Id != member.Id || current.OrganizationId != member.OrganizationId || current.CostCentre != member.CostCentre)
                throw new PolicyException("finance_assignment_changed");
            if (receipts is null || receipts.Any(r => r is null || r.BuyerOrganizationId != current.OrganizationId || r.CostCentre != current.CostCentre))
                return Results.Problem(statusCode: 502, title: "fulfilment_partition_mismatch");
            foreach (var receipt in receipts)
                book.Prepare(receipt);
            return Results.Ok(book.List(current));
        }

        IResult ReleasePayment(ClaimsPrincipal user, HttpContext context, Memberships memberships, PaymentBook book, Guid id, ReleasePayment body)
        {
            ApiSecurity.RequireClient(user, app.Configuration["FinanceClientId"]);
            // Amount, recipient, revision, finance role and spending limit are business
            // checks. Fresh strong authentication supplements them; it never replaces them.
            book.CheckRelease(memberships.Require(user), id, body);
            if (SensitiveActionAssurance.Challenge(context) is { } challenge)
                return challenge;
            // Verification does not replay a payment. The browser returns to the current record
            // for explicit confirmation, and every new POST repeats all checks before mutation.
            return Results.Ok(book.Release(memberships.Require(user), id, body, freshStrongAssurance: true));
        }

        ReconciliationItem[] ReadReconciliation(ClaimsPrincipal user, Memberships assignments, PaymentBook book)
        {
            return book.Reconcile(assignments.RequireWorkload(user, app.Configuration["ReconciliationClientId"]));
        }
    }
}
