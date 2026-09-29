using System.Net;

namespace ServiceBooking.API.Services.Notifications.WebPush;

/// <summary>
/// SSRF guard for web-push subscription endpoints: the server later POSTs to this address, so it must be a public https host name.
/// Rejects non-https, credentials in the URL, IP literals, localhost and single-label / internal-looking host names.
/// </summary>
public static class PushEndpointValidator
{
    public const int MaxLength = 500;

    private static readonly string[] InternalSuffixes = [".localhost", ".local", ".internal", ".intranet", ".lan", ".home", ".corp", ".localdomain"];

    public static bool IsValid(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Length > MaxLength) return false;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
        if (!uri.IsDefaultPort && uri.Port != 443) return false;

        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0) return false;
        if (uri.HostNameType != UriHostNameType.Dns) return false;
        if (IPAddress.TryParse(host.Trim('[', ']'), out _)) return false;
        if (host.All(c => char.IsAsciiDigit(c) || c == '.' || c == 'x')) return false;
        if (!host.Contains('.')) return false;
        if (host == "localhost") return false;
        return !InternalSuffixes.Any(host.EndsWith);
    }
}
