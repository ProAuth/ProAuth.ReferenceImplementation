using Microsoft.AspNetCore.Http;
using Procurement.Domain;

namespace Common.Security;

/// <summary>Translates known domain failures without exposing implementation details.</summary>
public static class PolicyErrors
{
    public static int StatusCode(PolicyException error) => error.Message switch
    {
        "membership_configuration_unavailable" => StatusCodes.Status503ServiceUnavailable,
        "purchase_not_found" or "order_not_found" or "payment_not_found" => StatusCodes.Status404NotFound,
        "revision_conflict" or "idempotency_conflict" or "source_revision_conflict" => StatusCodes.Status409Conflict,
        "invalid_idempotency_key" or "unknown_product" or "invalid_approved_purchase" => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status403Forbidden
    };
}
