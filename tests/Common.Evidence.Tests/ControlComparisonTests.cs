using System.Text.Json;
using Common.Evidence;
using Xunit;

public class ControlComparisonTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static ProtocolControl Sample() => new("mtls", DateTimeOffset.UtcNow, 200, 401, 200, 401, false, 401, 401, 401, null);
    [Theory]
    [InlineData("accessToken")]
    [InlineData("headers")]
    [InlineData("privateKey")]
    [InlineData("errorDescription")]
    public void RejectsCredentialCarryingFields(string field)
    {
        var value = JsonSerializer.Serialize(Sample(), Json);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProtocolControl>(value[..^1] + $",\"{field}\":\"secret-canary\"}}", Json));
    }
    [Fact]
    public void RejectsUnboundedStringsAndInvalidStatuses()
    {
        Assert.False((Sample() with
        {
            Variant = "Bearer secret-canary"
        }).IsValid());
        Assert.False((Sample() with
        {
            WrongCertificate = 10000
        }).IsValid());
        Assert.False((Sample() with
        {
            ObservedAt = DateTimeOffset.UtcNow.AddDays(1)
        }).IsValid());
    }
    [Fact]
    public void DuplicateVariantsCannotAppearAsIndependentExperiments()
        => Assert.False(new ControlComparisons([Sample(), Sample()], null).IsValid());
    [Fact]
    public void ObservationsDoNotTurnUnexpectedAcceptanceIntoRejection()
    {
        var result = Sample() with
        {
            WrongCertificate = 200
        };
        Assert.True(result.IsValid());
        Assert.Equal(200, result.WrongCertificate);
    }
    [Fact]
    public void ReplicaCountersMustDescribeTheirRequestCount()
        => Assert.False(new ReplicaControl(DateTimeOffset.UtcNow, 200, 401, 200, 200, 16, 1, 16, 503, 503, 200, 200, 401, true, true).IsValid());
}
