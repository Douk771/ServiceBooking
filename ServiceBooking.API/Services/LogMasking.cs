using System.Text.RegularExpressions;

namespace ServiceBooking.API.Services;

/// <summary>
/// First rung of the three-rung defence against phone numbers reaching the log pipeline in the clear
/// (ARCHITECTURE.md §11.3): a pure function for the (rare, reviewed) case where a log line genuinely
/// needs to reference a phone number. The second rung — <see cref="PhoneMaskingEnricher"/> — is the
/// safety net for the interpolated string someone writes without calling this.
/// </summary>
public static partial class LogMasking
{
    /// <summary>
    /// Masks a phone number for logging: keeps the first 4 and last 4 characters, replaces everything
    /// between with "***" (e.g. "79991234567" → "7999***4567"). Anything too short to leave a
    /// meaningful gap between the two kept ends (8 characters or fewer) is masked completely rather than
    /// risk exposing most of a short number.
    /// </summary>
    public static string? Phone(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        return raw.Length <= 8 ? new string('*', raw.Length) : raw[..4] + "***" + raw[^4..];
    }

    /// <summary>
    /// Scans arbitrary text for runs of 10–15 consecutive digits — the E.164 bounds PhoneNormalizer
    /// enforces — and masks each one the same way as <see cref="Phone"/>. Used by
    /// <see cref="PhoneMaskingEnricher"/> and the Sentry sink's BeforeSend hook, neither of which knows
    /// in advance which substring, if any, is actually a phone number (ARCHITECTURE.md §11.3 p.3).
    /// </summary>
    public static string MaskPhoneSequences(string text) => DigitRunRegex().Replace(text, m => Phone(m.Value)!);

    // Lookaround, not a bare {10,15}: without it, a 16+ digit run (e.g. a card number) would still get
    // its first 10-15 digits matched and masked, silently mangling a value that was never a phone number
    // in the first place instead of leaving it alone.
    [GeneratedRegex(@"(?<!\d)\d{10,15}(?!\d)")]
    private static partial Regex DigitRunRegex();
}
