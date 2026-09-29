using System.Security.Cryptography;

namespace ServiceBooking.API.Services.Orders;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §395.2 step 9, §405 R-6 — the secret in an order link /o/&lt;token&gt;: 32 bytes from the
/// system CSPRNG, base64url without padding (43 characters, 256 bits). Not derivable from anything else.
/// </summary>
public static class PublicOrderToken
{
    public const int ByteLength = 32;
    public const int Length = 43;

    public static string Generate() => Encode(RandomNumberGenerator.GetBytes(ByteLength));

    public static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Cheap pre-filter for the route: the right length and only base64url characters. A miss is a 404 without a DB round trip.</summary>
    public static bool IsWellFormed(string? token) =>
        token is { Length: Length } && token.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');
}
