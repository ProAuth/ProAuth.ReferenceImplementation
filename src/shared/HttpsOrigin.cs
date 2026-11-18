namespace Mervikon.Transport;

internal static class HttpsOrigin
{
    public static Uri Require(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var origin) || origin.Scheme != Uri.UriSchemeHttps ||
            origin.UserInfo.Length != 0 || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0)
            throw new InvalidOperationException("Configure an absolute HTTPS API origin without credentials, path, query or fragment.");
        return origin;
    }
}
