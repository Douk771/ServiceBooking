namespace ServiceBooking.API.Services.Orders.Reports;

public enum CustomerSearchKind
{
    /// <summary>No filter.</summary>
    None,

    /// <summary>A fragment of the phone: <see cref="CustomerSearchTerm.Value"/> is the digits.</summary>
    Phone,

    /// <summary>A fragment of the name: <see cref="CustomerSearchTerm.Value"/> is the trimmed text.</summary>
    Name,

    /// <summary>Fewer than 2 name characters or fewer than 4 phone digits — the caller answers 400.</summary>
    TooShort
}

/// <summary>
/// API_CONTRACT_CYCLE25.md §526 — the "покупатель" filter of the order history. A phone if, after removing spaces, '+', '(', ')' and '-',
/// only digits remain and there are at least 4 of them; otherwise a name of at least 2 characters. Pure.
/// </summary>
public sealed record CustomerSearchTerm(CustomerSearchKind Kind, string Value)
{
    public const int MinPhoneDigits = 4;
    public const int MinNameLength = 2;
    public const int MaxLength = 100;
    public const string TooShortText = "Введите не меньше 2 букв имени или 4 цифр телефона";

    public static CustomerSearchTerm Parse(string? input)
    {
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text)) return new(CustomerSearchKind.None, string.Empty);

        var stripped = new string(text.Where(c => !(char.IsWhiteSpace(c) || c is '+' or '(' or ')' or '-')).ToArray());
        if (stripped.Length > 0 && stripped.All(c => c is >= '0' and <= '9'))
            return stripped.Length >= MinPhoneDigits ? new(CustomerSearchKind.Phone, stripped) : new(CustomerSearchKind.TooShort, string.Empty);

        return text.Length >= MinNameLength ? new(CustomerSearchKind.Name, text) : new(CustomerSearchKind.TooShort, string.Empty);
    }
}
