using System.Security.Claims;
using Common.Evidence;
using Common.Security;
using Workflow.Domain;
using Procurement.Domain;

namespace Fulfilment.Api;

/// <summary>
/// The API boundary connects OAuth capabilities to Mervikon's order workflow. ProAuth has
/// already validated tokens and any sender proofs before these handlers run. A scope alone
/// is insufficient: each handler restricts the caller, then OrderBook enforces record access
/// and valid state transitions. Demo evidence metadata observes results; it grants no access.
/// </summary>
public static class FulfilmentEndpoints
{
    public static void MapFulfilmentEndpoints(this WebApplication app)
    {
        var routes = app.MapGroup("").ObserveBusinessResults();

        // Human access: supplier and warehouse memberships select which orders are visible.
        routes.MapGet("/api/orders", ListOrders)
            .RequireAuthorization("fulfilment.read")
            .WithMetadata(new EvidenceOperation(Operation.ListOrders));

        // Delegated access: only Procurement's exchanged token can import an approved order.
        routes.MapPost("/api/orders", ImportApprovedOrder)
            .RequireAuthorization("fulfilment.create")
            .WithMetadata(new EvidenceOperation(Operation.ImportOrder));

        routes.MapPost("/api/orders/{id:guid}/accept", AcceptOrder)
            .RequireAuthorization("fulfilment.write")
            .WithMetadata(new EvidenceOperation(Operation.AcceptOrder));
        routes.MapPost("/api/orders/{id:guid}/dispatch", DispatchSupplierOrder)
            .RequireAuthorization("fulfilment.write")
            .WithMetadata(new EvidenceOperation(Operation.DispatchOrder));
        routes.MapPost("/api/orders/{id:guid}/receive", ReceiveOrder)
            .RequireAuthorization("fulfilment.receive")
            .WithMetadata(new EvidenceOperation(Operation.ReceiveOrder));

        // Workload access: separate routes use client assignments rather than human membership.
        routes.MapGet("/api/dispatch", ListDispatchQueue)
            .RequireAuthorization("fulfilment.read");
        routes.MapPost("/api/dispatch/{id:guid}", DispatchWorkloadOrder)
            .RequireAuthorization("fulfilment.write")
            .WithMetadata(new EvidenceOperation(Operation.DispatchOrder));
        routes.MapGet("/api/received", ReadReceipts)
            .RequireAuthorization("fulfilment.reconcile")
            .WithMetadata(new EvidenceOperation(Operation.ReadReceipts));

        SupplierOrder[] ListOrders(ClaimsPrincipal user, Memberships memberships, OrderBook orders)
        {
            var client = ValidatedContext.Client(user);
            if (client != app.Configuration["SupplierClientId"] && client != app.Configuration["WarehouseClientId"])
                throw new PolicyException("client_not_authorized");
            return orders.List(memberships.Require(user));
        }

        SupplierOrder ImportApprovedOrder(ClaimsPrincipal user, Memberships memberships, OrderBook orders, ApprovedOrder purchase)
        {
            // Token exchange preserves the human subject and identifies Procurement in act.
            // ExchangeActor requires the configured client, exact actor issuer and DPoP binding;
            // it does not validate the proof itself. The SDK has already done that work.
            // Only this caller may supply authoritative approved-order data to Fulfilment.
            ExchangeActor.Require(user, app.Configuration["ProcurementExchangeClientId"], app.Configuration["ResourceServer:Authority"]);
            return orders.Import(memberships.Require(user), purchase);
        }

        SupplierOrder AcceptOrder(ClaimsPrincipal user, Memberships memberships, OrderBook orders, Guid id, OrderRevision body)
        {
            // The supplier client and the user's assigned supplier organization must both match.
            // OrderBook also requires the current revision and an Assigned order.
            ApiSecurity.RequireClient(user, app.Configuration["SupplierClientId"]);
            return orders.Transition(memberships.Require(user), id, body.Revision, "accept");
        }

        SupplierOrder DispatchSupplierOrder(ClaimsPrincipal user, Memberships memberships, OrderBook orders, Guid id, OrderRevision body)
        {
            // Manual dispatch uses a supplier's human membership. The automated dispatch path
            // below has its own client registration and operator-owned partition assignments.
            ApiSecurity.RequireClient(user, app.Configuration["SupplierClientId"]);
            return orders.Transition(memberships.Require(user), id, body.Revision, "dispatch");
        }

        SupplierOrder ReceiveOrder(ClaimsPrincipal user, Memberships memberships, OrderBook orders, Guid id, OrderRevision body)
        {
            // Device authorization signs a person in on the terminal. It does not make the
            // terminal a warehouse worker: the validated person still needs a warehouse role
            // and the order's warehouse assignment. A stale revision cannot confirm receipt.
            ApiSecurity.RequireClient(user, app.Configuration["WarehouseClientId"]);
            return orders.Transition(memberships.Require(user), id, body.Revision, "receive");
        }

        DispatchItem[] ListDispatchQueue(ClaimsPrincipal user, Memberships assignments, OrderBook orders)
        {
            var allowed = assignments.RequireWorkload(user, app.Configuration["DispatchClientId"]);
            return orders.DispatchQueue(allowed);
        }

        DispatchItem DispatchWorkloadOrder(ClaimsPrincipal user, Memberships assignments, OrderBook orders, Guid id, OrderRevision body)
        {
            // Client credentials represent the worker itself. RequireWorkload checks issuer,
            // tenant and client before returning allowed buyer/cost-centre partitions.
            var allowed = assignments.RequireWorkload(user, app.Configuration["DispatchClientId"]);
            return orders.Dispatch(allowed, id, body.Revision);
        }

        Receipt[] ReadReceipts(ClaimsPrincipal user, Memberships assignments, OrderBook orders, string? organizationId, string? costCentre)
        {
            var allowed = assignments.RequireWorkload(user, app.Configuration["TreasuryReaderClientId"]);
            if (organizationId is null && costCentre is null)
                return orders.Receipts(allowed);

            // Query parameters may narrow a workload's grant, never enlarge it. Require the
            // complete partition pair, then check it against trusted application assignments.
            if (organizationId is null || costCentre is null || !allowed.Contains(new(organizationId, costCentre)))
                throw new PolicyException("partition_denied");
            return orders.Receipts(new HashSet<Partition> { new(organizationId, costCentre) });
        }
    }
}

internal sealed record OrderRevision(int Revision);
