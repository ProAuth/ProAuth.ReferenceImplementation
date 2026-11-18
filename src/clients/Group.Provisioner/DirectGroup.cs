using System.Text.Json;

namespace Group.Provisioner;

// The native UserStore v2 Group DTO. Its memberUsers relation is direct; never traverse memberGroups.
public static class DirectGroup
{
    public static bool Contains(JsonElement group, Guid store, Guid groupId, Guid nativeUser)
    {
        if (group.GetProperty("instanceId").GetGuid() != store || group.GetProperty("id").GetGuid() != groupId)
            throw new InvalidDataException();
        var users = group.GetProperty("memberUsers");
        if (users.ValueKind == JsonValueKind.Object)
            users = users.GetProperty("$values"); // Reference-preserving collection serialization.
        if (users.GetArrayLength() > 64)
            throw new InvalidDataException();
        var ids = new HashSet<Guid>();
        foreach (var user in users.EnumerateArray())
        {
            if (user.GetProperty("instanceId").GetGuid() != store || !ids.Add(user.GetProperty("id").GetGuid()))
                throw new InvalidDataException();
        }
        return ids.Contains(nativeUser);
    }
}
