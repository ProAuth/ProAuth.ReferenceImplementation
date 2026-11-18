using System.Net.Http.Headers;
using System.Text.Json;

namespace Group.Provisioner;

public sealed record NativeMapping(string Issuer, string TenantId, Guid Store, Guid Group, Guid NativeUser, string PersonId);

public static class PublishEligibility
{
    // Client has the product proof handler and a bounded response buffer. Each run replaces the whole fact.
    public static async Task FromGroup(HttpClient client, string accessToken, NativeMapping mapping, string path, CancellationToken ct)
    {
        var next = Path.Combine(Path.GetDirectoryName(path)!, Guid.NewGuid().ToString("N") + ".next");
        try
        {
            var uri = new Uri(new Uri(mapping.Issuer), $"/api/userstore/v2/{mapping.Store}/administration/Group/{mapping.Group}");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("DPoP", accessToken);
            var observedAt = DateTimeOffset.UtcNow;
            using var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            using var dto = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct),
                new JsonDocumentOptions { MaxDepth = 12, AllowDuplicateProperties = false });
            var eligible = DirectGroup.Contains(dto.RootElement, mapping.Store, mapping.Group, mapping.NativeUser);
            var snapshot = new
            {
                mapping.Issuer,
                mapping.TenantId,
                EligiblePersonId = eligible ? mapping.PersonId : null,
                ObservedAt = observedAt
            };
            await File.WriteAllTextAsync(next, JsonSerializer.Serialize(snapshot), ct);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(next, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            File.Move(next, path, overwrite: true);
        }
        finally { File.Delete(next); }
    }
}
