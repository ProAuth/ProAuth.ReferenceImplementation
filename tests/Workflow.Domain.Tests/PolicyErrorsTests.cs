using Procurement.Domain;
using Common.Security;
using Xunit;

namespace Workflow.Domain.Tests;

public sealed class PolicyErrorsTests
{
    [Theory]
    [InlineData("membership_configuration_unavailable", 503)]
    [InlineData("membership_required", 403)]
    [InlineData("order_not_found", 404)]
    [InlineData("revision_conflict", 409)]
    [InlineData("source_revision_conflict", 409)]
    [InlineData("invalid_approved_purchase", 400)]
    [InlineData("unknown_product", 400)]
    [InlineData("unknown_future_policy", 403)]
    public void PolicyFailuresHaveConsistentHttpSemantics(string code, int expected)
    {
        Assert.Equal(expected, PolicyErrors.StatusCode(new PolicyException(code)));
    }
}
