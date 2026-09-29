using System.Text.RegularExpressions;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// ARCHITECTURE_CYCLE20.md §402.5, API_CONTRACT_CYCLE20.md §432.8 (Т20-04 п. 3, D1 П-13) — the PDn
/// operator details printed on the health-consent paper form (<c>BillingAccount.ConsentOperator*</c>).
/// Pure, no EF/HTTP: a blank/whitespace-only value is normalized to "not provided" (§432.8 "пустая строка
/// и строка из пробелов = null"), and every field is individually optional — filling any of them in is
/// never required to print a form (§402.5's own "заполнять не обязательно").
/// </summary>
public static class ConsentOperatorDetailsValidator
{
    public const int MaxFullNameLength = 300;
    public const int MaxAddressLength = 500;

    // ч. 4 ст. 9 152-ФЗ — full name only, initials ("Иванов И. И.") are not accepted. Matches a single
    // Cyrillic/Latin capital letter followed by a period, at the start of the string or after whitespace.
    private static readonly Regex InitialsPattern = new(@"(^|\s)[А-ЯЁA-Z]\.", RegexOptions.Compiled);
    private static readonly Regex InnPattern = new(@"^(\d{10}|\d{12})$", RegexOptions.Compiled);

    public static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Returns the Russian 400 text, or null when the (already-normalized) triple is acceptable.</summary>
    public static string? Validate(string? fullName, string? address, string? inn)
    {
        if (fullName is { Length: > MaxFullNameLength })
            return $"ФИО не должно превышать {MaxFullNameLength} символов.";
        if (fullName is not null && InitialsPattern.IsMatch(fullName))
            return "Укажите фамилию, имя и отчество полностью";

        if (address is { Length: > MaxAddressLength })
            return $"Адрес не должен превышать {MaxAddressLength} символов.";

        if (inn is not null && !InnPattern.IsMatch(inn))
            return "ИНН должен состоять из 10 или 12 цифр.";

        return null;
    }

    /// <summary>§432.4/§432.8's shared rule: the hint to fill in operator details shows whenever full
    /// name OR address is missing — the INN alone is never enough to clear it, and is itself optional.</summary>
    public static bool IsMissing(string? fullName, string? address) => fullName is null || address is null;
}
