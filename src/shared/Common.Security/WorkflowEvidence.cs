using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Procurement.Domain;
using Common.Evidence;
using Workflow.Domain;

namespace Common.Security;

/// <summary>
/// DEMO ONLY: projects completed business results into the shared evidence schema. Kept here
/// because it references domain result types; the evidence library stays independent of them.
/// Returns the original endpoint result and does not participate in authorization.
/// </summary>
public static class WorkflowEvidence
{
    public static RouteGroupBuilder ObserveBusinessResults(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(async (context, next) =>
        {
            var result = await next(context);
            var value = result is IValueHttpResult wrapped ? wrapped.Value : result;
            var observation = value switch
            {
                Purchase p => Describe(p.Id, p.Status, p.Revision),
                DispatchItem d => Describe(d.PurchaseId, "Dispatched", d.Revision),
                SupplierOrder o => Describe(o.Purchase.PurchaseId, o.Status, o.Revision),
                PaymentInstruction p => Describe(p.PurchaseId, p.Status, p.Revision),
                _ => null
            };
            if (observation is not null)
                context.HttpContext.Items[typeof(BusinessObservation)] = observation;
            return result;
        });
        return group;
    }
    private static BusinessObservation? Describe(Guid id, string state, int revision) =>
        Enum.TryParse<BusinessState>(state, out var value) && Enum.IsDefined(value) ? new(id, value, revision) : null;
}
