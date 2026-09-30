namespace ServiceBooking.API.Services.Orders.Reports;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §501.2 — the phone in the history list: only the last four digits, "+7 (···) ···-12-34". Deliberately not
/// <see cref="PhoneDisplayMask"/> (cycle 14) — that one has another audience and another shape. The full number is shown only in the
/// order card and the customer card. Pure.
/// </summary>
public static class OrderPhoneMask
{
    public static string Mask(string? canonicalPhone)
    {
        var digits = new string((canonicalPhone ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0) return string.Empty;
        if (digits.Length == 11 && digits[0] == '7') return $"+7 (···) ···-{digits[7..9]}-{digits[9..11]}";
        return digits.Length <= 4 ? new string('·', digits.Length) : "···" + digits[^4..];
    }
}
