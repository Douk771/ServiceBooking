using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Companies;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §408.2 — parsing of the <c>?kind=</c> filter on the cabinet lists.
/// Names only (case-insensitive): a bare number such as "1" or "5" is not a kind and is refused like any
/// other unknown value, so the query cannot be used to smuggle an out-of-range enum value into a filter.
/// </summary>
public static class CompanyKindQuery
{
    public const string UnknownKindText = "Неизвестный тип компании";

    /// <summary>null/empty → <see cref="CompanyKind.Services"/> (the ezbook frontend never sends the parameter).</summary>
    public static bool TryParse(string? raw, out CompanyKind kind)
    {
        kind = CompanyKind.Services;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        var value = raw.Trim();
        // Enum.TryParse also accepts numbers and comma lists ("Orders,Services") — neither is a kind name.
        if (!value.All(char.IsLetter)) return false;
        return Enum.TryParse(value, ignoreCase: true, out kind) && Enum.IsDefined(kind);
    }
}
