namespace ServiceBooking.API.Services;

/// <summary>
/// Parses the <c>{clientKey}</c> route segment API_CONTRACT_CYCLE5.md §44/§45 introduces — a registered
/// client's <c>userId</c>, or <c>phone:79991234567</c> (canonical) for a guest. One place this format is
/// understood, so every new salon-facing endpoint that takes a client key (photo consent, health note,
/// health consent — and whatever the next one turns out to be) parses it identically.
/// </summary>
public static class ClientKey
{
    private const string PhonePrefix = "phone:";

    public static bool IsPhone(string clientKey) => clientKey.StartsWith(PhonePrefix, StringComparison.Ordinal);

    public static string ExtractPhone(string clientKey) => clientKey[PhonePrefix.Length..];
}
