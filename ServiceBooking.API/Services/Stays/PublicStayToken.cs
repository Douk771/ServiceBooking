using System.Security.Cryptography;

namespace ServiceBooking.API.Services.Stays;

/// <summary>ARCHITECTURE_CYCLE37.md §37.2.4 — 32 random bytes, base64url (43 chars): the secret in a booking link.</summary>
public static class PublicStayToken
{
    public static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
