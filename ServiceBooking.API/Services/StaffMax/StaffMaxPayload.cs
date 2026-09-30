using System.Security.Cryptography;
using ServiceBooking.API.Services.PhoneVerification;

namespace ServiceBooking.API.Services.StaffMax;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §498.2 — the one-time payload of the staff link ("sm1." + 32 CSPRNG bytes, base64url). A DIFFERENT prefix from the phone
/// confirmation's "v1." is what lets the shared webhook route an update to the right handler. Only its SHA-256 is stored
/// (<see cref="PayloadGenerator.Hash"/>). Pure.
/// </summary>
public static class StaffMaxPayload
{
    public const string Prefix = "sm1.";

    public static string Generate() =>
        Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool IsStaffPayload(string? payload) => payload is not null && payload.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Hash(string payload) => PayloadGenerator.Hash(payload);
}
