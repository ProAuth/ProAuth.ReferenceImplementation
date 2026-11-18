using System.Text.Json;
using System.Text.Json.Serialization;
using Procurement.Domain;

namespace Common.Security;

internal static class AssignmentJson
{
    private static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        MaxDepth = 8
    };

    public static T Read<T>(string path, int maxBytes) where T : class
    {
        try
        {
            // Open once: size and bytes refer to the same inode across atomic replacements.
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var size = file.Length;
            if (size <= 0 || size > maxBytes)
                throw new PolicyException("membership_configuration_unavailable");
            var bytes = new byte[(int)size];
            file.ReadExactly(bytes);
            if (file.ReadByte() != -1)
                throw new PolicyException("membership_configuration_unavailable");
            return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new JsonException();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new PolicyException("membership_configuration_unavailable");
        }
    }
}
