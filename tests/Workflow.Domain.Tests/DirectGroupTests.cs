using System.Text.Json;
using Group.Provisioner;
using Xunit;

public class DirectGroupTests
{
    [Fact]
    public void OnlyDirectNativeIdsInMappedStoreConveyEligibility()
    {
        var store = Guid.NewGuid();
        var group = Guid.NewGuid();
        var user = Guid.NewGuid();
        JsonElement Source(Guid source, Guid id, Guid[] direct) => JsonSerializer.SerializeToElement(new
        {
            instanceId = source,
            id,
            name = "Purchasing approvers",
            memberUsers = direct.Select(x => new { instanceId = source, id = x }),
            memberGroups = new[] { new { memberUsers = new[] { new { id = user } } } }
        });
        Assert.True(DirectGroup.Contains(Source(store, group, [user]), store, group, user));
        Assert.False(DirectGroup.Contains(Source(store, group, []), store, group, user));
        Assert.Throws<InvalidDataException>(() => DirectGroup.Contains(Source(Guid.NewGuid(), group, [user]), store, group, user));
        Assert.Throws<InvalidDataException>(() => DirectGroup.Contains(Source(store, Guid.NewGuid(), [user]), store, group, user));
    }
}
