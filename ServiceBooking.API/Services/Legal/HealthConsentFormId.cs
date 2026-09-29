using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ServiceBooking.API.Services.Legal;

/// <summary>
/// ARCHITECTURE_CYCLE20.md §402.5 (US-20-01) — the printed paper form's number, <c>"HD-"</c> plus 8
/// Crockford base32 characters (digits and uppercase letters, excluding I/L/O/U to avoid confusion with
/// 1/1/0/V). Generated fresh on every <c>GET …/health-consent-form</c> call and never stored until a
/// staff member confirms it on <c>POST …/health-written-consent</c> — there is nothing to look up, only a
/// FORMAT to check, so <see cref="IsValid"/> never touches a database.
/// </summary>
public static class HealthConsentFormId
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // Crockford base32, no I/L/O/U

    private static readonly Regex Pattern = new("^HD-[0-9A-HJKMNP-TV-Z]{8}$", RegexOptions.Compiled);

    public static string New()
    {
        Span<char> suffix = stackalloc char[8];
        for (var i = 0; i < suffix.Length; i++)
            suffix[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return "HD-" + new string(suffix);
    }

    public static bool IsValid(string? value) => value is not null && Pattern.IsMatch(value);
}
