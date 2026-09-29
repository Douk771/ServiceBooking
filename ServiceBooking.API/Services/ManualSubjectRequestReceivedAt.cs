namespace ServiceBooking.API.Services;

/// <summary>
/// Code-review finding (cycle 20) — <c>AdminController.RegisterSubjectRequest</c> (US-20-09, §410) used
/// to do <c>DateTime.SpecifyKind(dto.ReceivedAt.Value, DateTimeKind.Utc)</c>, which only overwrites the
/// <see cref="DateTime.Kind"/> flag without converting the value. System.Text.Json's default
/// <see cref="DateTime"/> converter (<c>DateTimeStyles.RoundtripKind</c>) parses an ISO-8601 string with
/// an explicit non-'Z' offset (e.g. <c>"2026-09-25T00:00:00+05:00"</c>) by converting it to the SERVER's
/// local time zone and marking the result <see cref="DateTimeKind.Local"/> — silently relabelling that as
/// Utc shifts the wall clock by the server's local UTC offset, which then shifts <c>DueAtUtc</c> (the
/// 152-ФЗ response deadline) by the same amount. <see cref="DateTime.ToUniversalTime"/> is the correct,
/// symmetric inverse of that same conversion: a no-op for the documented 'Z' contract shape (API_CONTRACT_CYCLE20.md
/// §438, already Kind=Utc) and the value that correctly recovers the true UTC instant when an explicit
/// offset was supplied. Pure (no EF/HTTP types) so the boundary cases below are unit-testable directly.
/// </summary>
public static class ManualSubjectRequestReceivedAt
{
    /// <summary>No SPEC-given floor exists for this field — this is a sanity bound against garbage input
    /// (e.g. an accidental <c>default(DateTime)</c> reaching here as 0001-01-01), not a business rule.</summary>
    public static readonly DateTime EarliestPlausibleUtc = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Returns the normalized UTC value, or the Russian 400 text to send instead.</summary>
    public static (DateTime? ReceivedAtUtc, string? Error) Validate(DateTime? receivedAt, DateTime nowUtc)
    {
        if (receivedAt is null)
            return (null, "Укажите дату поступления обращения.");

        // Unspecified (ISO string without offset) is read as UTC, as before the fix; only an explicit
        // offset (Kind=Local after System.Text.Json) is converted back to the true UTC instant.
        var receivedAtUtc = receivedAt.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(receivedAt.Value, DateTimeKind.Utc)
            : receivedAt.Value.ToUniversalTime();

        if (receivedAtUtc > nowUtc)
            return (null, "Дата поступления не может быть в будущем.");
        if (receivedAtUtc < EarliestPlausibleUtc)
            return (null, $"Дата поступления не может быть раньше {EarliestPlausibleUtc:dd.MM.yyyy}.");

        return (receivedAtUtc, null);
    }
}
