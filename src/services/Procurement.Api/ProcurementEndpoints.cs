using System.Net.Http.Headers;
using System.Security.Claims;
using ProAuth.Oidc.Client;
using ProAuth.Oidc.ResourceServer.AspNetCore.Authentication;
using Procurement.Domain;
using Common.Evidence;
using Common.Security;
using Workflow.Domain;

namespace Procurement.Api;

/// <summary>
/// Employee purchases: scope authorization, business approval, API-driven step-up and delegated
/// token exchange. Follow Program.cs for SDK setup and PurchaseBook for business invariants.
/// </summary>
public static class ProcurementEndpoints
{
    private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    public static void MapProcurementEndpoints(this WebApplication app)
    {
        var memberships = app.Services.GetRequiredService<Memberships>();
        var journal = app.Services.GetRequiredService<EvidenceJournal>();
        var routes = app.MapGroup("").ObserveBusinessResults();
        // Scopes expose API capabilities; RunWithMembership resolves the current business person.
        // Domain checks below enforce organization, cost centre, role and transaction state.
        routes.MapGet("/api/products", ListProducts).RequireAuthorization("procurement.read");
        routes.MapGet("/api/purchases", ListPurchases).RequireAuthorization("procurement.read");
        routes.MapGet("/api/evidence", ReadDecisionHistory).RequireAuthorization("procurement.read");
        routes.MapPost("/api/purchases", RequestPurchase)
            .RequireAuthorization("procurement.write")
            .WithMetadata(new EvidenceOperation(Operation.RequestPurchase));
        routes.MapPost("/api/purchases/{id:guid}/approve", ApprovePurchase)
            .RequireAuthorization("procurement.write")
            .WithMetadata(new EvidenceOperation(Operation.ApprovePurchase));
        routes.MapPost("/api/purchases/{id:guid}/handoff", HandOffApprovedPurchase)
            .RequireAuthorization("procurement.write")
            .WithMetadata(new EvidenceOperation(Operation.ExchangeHandoff));

        IResult ListProducts(ClaimsPrincipal user, PurchaseBook book) =>
            RunWithMembership(user, _ => Results.Ok(book.Products));

        IResult ListPurchases(ClaimsPrincipal user, PurchaseBook book) =>
            RunWithMembership(user, member => Results.Ok(book.List(member)));

        IResult ReadDecisionHistory(ClaimsPrincipal user) =>
            RunWithMembership(user, member => Results.Ok(journal.For(member)));

        IResult RequestPurchase(ClaimsPrincipal user, HttpRequest request, RequestPurchase body, PurchaseBook book)
        {
            // Product price comes from the server catalogue. Idempotency binds a retry to the
            // same person and payload; it is separate from OAuth token or DPoP replay protection.
            return RunWithMembership(user, member => Results.Ok(book.Request(member, body.ProductId,
                request.Headers["Idempotency-Key"].ToString())), "purchase.request");
        }

        IResult ApprovePurchase(ClaimsPrincipal user, HttpContext context, Guid id, ApprovePurchase body, PurchaseBook book)
        {
            return RunWithMembership(user, member =>
            {
                // Check business eligibility before challenging: stronger authentication cannot
                // grant an approver role or permit someone to approve their own purchase.
                var purchase = book.CheckApproval(member, id, body.Revision);
                if (purchase.AmountChf > 20_000 && SensitiveActionAssurance.Challenge(context) is { } challenge)
                    return challenge;

                // The SDK reads validated acr/auth_time claims. A challenge makes no change;
                // after verification the browser must submit a new explicit approval request.
                // Re-read membership and transaction state before committing that request.
                return Results.Ok(book.Approve(memberships.Require(user), id, body.Revision, trustedFreshStrongAssurance: true));
            }, "purchase.approve");
        }

        async Task<IResult> HandOffApprovedPurchase(ClaimsPrincipal user, HttpContext context, Guid id, ApprovePurchase body,
            PurchaseBook book, IOidcClient oidc, IHttpClientFactory factory, Publisher protocol, CancellationToken ct)
        {
            Purchase? purchase = null;
            var check = RunWithMembership(user, member =>
            {
                purchase = book.ApprovedForHandoff(member, id, body.Revision);
                return Results.Ok();
            });
            if (purchase is null)
                return check;
            // The package accessor exists only after full token and sender-proof validation.
            // Explicit AS policy authorizes this API; never forward the original sender's proof.
            var runId = Guid.NewGuid();
            protocol.RecordProtocol(new(runId, DateTimeOffset.UtcNow, "Procurement", "exchange-input", 200, "observed", EvidenceProjection.Token(context.GetValidatedAccessToken())));
            // Opt-in conference negative control: request a scope the exchange policy forbids.
            // This is demonstration instrumentation, not a prerequisite of token exchange.
            if (app.Configuration.GetValue<bool>("ConferenceControls:Enabled"))
            {
                try
                {
                    await oidc.ExchangeTokenAsync(context.GetValidatedAccessToken(), AccessTokenType,
                        scopes: ["fulfilment.write"], resources: ["urn:mervikon:fulfilment"], cancellationToken: ct);
                    protocol.RecordProtocol(new(runId, DateTimeOffset.UtcNow, "Procurement", "exchange-denied", 200, "failed"));
                    return Results.Problem(statusCode: 502, title: "conference_escalation_unexpectedly_accepted");
                }
                catch (ProAuth.Oidc.Client.Exceptions.OidcErrorResponseException e)
                {
                    var expected = e.StatusCode == 400 && e.ErrorResponse.Error == "invalid_scope";
                    protocol.RecordProtocol(new(runId, DateTimeOffset.UtcNow, "Procurement", "exchange-denied", e.StatusCode, expected ? "passed" : "failed", Error: EvidenceProjection.Error(e.ErrorResponse.Error)));
                    if (!expected)
                        return Results.Problem(statusCode: 502, title: "conference_scope_probe_inconclusive");
                }
                catch (Exception e) when (!ct.IsCancellationRequested && e is HttpRequestException or OperationCanceledException or TimeoutException)
                {
                    protocol.RecordProtocol(new(runId, DateTimeOffset.UtcNow, "Procurement", "exchange-denied", null, "failed"));
                    return Results.Problem(statusCode: 502, title: "conference_scope_probe_inconclusive");
                }
            }
            // Exchange instead of forwarding: Procurement's input token targets Procurement.
            // ProAuth issues a Fulfilment token with only create authority, preserves the subject
            // and records Procurement as actor. Its DPoP binding uses Procurement's own key.
            // The matching server policy is in config/proauth/clients/21-workflows.yml.
            var exchanged = await oidc.ExchangeTokenAsync(context.GetValidatedAccessToken(), AccessTokenType,
                requestedTokenType: AccessTokenType,
                scopes: ["fulfilment.create"], resources: ["urn:mervikon:fulfilment"], cancellationToken: ct);
            if (string.IsNullOrWhiteSpace(exchanged.AccessToken) || exchanged.IssuedTokenType != AccessTokenType
                || !string.Equals(exchanged.TokenType, "DPoP", StringComparison.OrdinalIgnoreCase))
                return Results.Problem(statusCode: 502, title: "token_exchange_rejected");
            protocol.RecordProtocol(new(runId, DateTimeOffset.UtcNow, "Procurement", "exchange-output", 200, "observed", EvidenceProjection.Token(exchanged.AccessToken)));
            // The named client in Program.cs attaches a fresh proof through DpopProofHandler.
            // A successful exchange does not itself create an order: Fulfilment independently
            // checks its audience, scope, actor, current membership and approved-order payload.
            using var downstream = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
            downstream.Headers.Authorization = new AuthenticationHeaderValue("DPoP", exchanged.AccessToken);
            downstream.Content = JsonContent.Create(new ApprovedOrder(purchase.Id, purchase.OrganizationId, purchase.CostCentre,
                purchase.RequesterId, purchase.ProductId, purchase.AmountChf, purchase.Revision, purchase.ApproverId!, "supplier-northstar", "zurich"));
            using var response = await factory.CreateClient("fulfilment").SendAsync(downstream, ct);
            protocol.RecordProtocol(new(runId, DateTimeOffset.UtcNow, "Procurement", "legitimate", (int)response.StatusCode, response.IsSuccessStatusCode ? "passed" : "failed", EvidenceProjection.Token(exchanged.AccessToken)));
            if (!response.IsSuccessStatusCode)
                return Results.Problem(statusCode: 502, title: "fulfilment_handoff_rejected", extensions: new Dictionary<string, object?> { ["downstreamStatus"] = (int)response.StatusCode });
            return Results.Ok(await response.Content.ReadFromJsonAsync<SupplierOrder>(ct));
        }

        // Keep the decision journal scoped to the resolved business member. Protocol evidence
        // uses a separate sanitized projection and never supplies authorization decisions.
        IResult RunWithMembership(ClaimsPrincipal user, Func<Member, IResult> action, string? operation = null)
        {
            Member member;
            try
            {
                member = memberships.Require(user);
            }
            catch (PolicyException e)
            {
                return Results.Problem(statusCode: PolicyErrors.StatusCode(e), title: e.Message);
            }
            try
            {
                var result = action(member);
                if (operation is not null && result is IValueHttpResult { Value: Purchase completed })
                    journal.Record(member, operation, "allowed", 200, completed);
                return result;
            }
            catch (PolicyException e)
            {
                var status = PolicyErrors.StatusCode(e);
                if (operation is not null)
                    journal.Record(member, operation, e.Message, status);
                return Results.Problem(statusCode: status, title: e.Message);
            }
        }
    }
}

internal sealed record RequestPurchase(string ProductId);
internal sealed record ApprovePurchase(int Revision);
