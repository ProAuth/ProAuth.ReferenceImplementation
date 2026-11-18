using System.Text.Json;
using Common.Evidence;
using Xunit;

public class SanitizationTests
{
    private static Observation Example() => new(Guid.NewGuid(), DateTimeOffset.UtcNow, Component.Treasury, Operation.ReleasePayment, 403,
        new string('a', 32), new string('b', 16), null, null, null, null);
    [Fact]
    public void ARealStatusDoesNotInventBusinessState()
    {
        var value = Example();
        Assert.True(value.IsValid());
        Assert.Null(value.State);
        Assert.Null(value.BusinessId);
    }
    [Theory]
    [InlineData("accessToken")]
    [InlineData("headers")]
    [InlineData("error")]
    [InlineData("claims")]
    [InlineData("url")]
    public void CredentialCarryingFieldsAreRejected(string field)
    {
        var json = JsonSerializer.Serialize(Example());
        json = json[..^1] + ",\"" + field + "\":\"credential-canary\"}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Observation>(json));
    }
    [Fact]
    public void StringFieldsCannotCarryCredentialsOrUnboundedData()
    {
        Assert.False((Example() with
        {
            TraceId = "Bearer credential-canary"
        }).IsValid());
        Assert.False((Example() with
        {
            SpanId = new string('a', 10000)
        }).IsValid());
        Assert.False((Example() with
        {
            ParentSpanId = "https://issuer/?code=credential-canary"
        }).IsValid());
    }
    [Fact]
    public void UndefinedOperationsAndStaleEventsAreRejected()
    {
        Assert.False((Example() with
        {
            Operation = (Operation)999
        }).IsValid());
        Assert.False((Example() with
        {
            State = (BusinessState)999
        }).IsValid());
        Assert.False((Example() with
        {
            ObservedAt = DateTimeOffset.UtcNow.AddDays(-2)
        }).IsValid());
    }
}
