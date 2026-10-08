namespace ServiceBooking.API.Services.Billing;

/// <summary>What is already stored in the account's row of a channel option (the facts the closed-option rule compares with).</summary>
public readonly record struct ExistingChannelOptionRow(DateTime? PaidUntilUtc, DateTime? EndsAtUtc);

public enum ChannelOptionLineVerdict
{
    Ok,
    /// <summary>400 «Для опций WhatsApp и MAX укажите дату окончания оплаты».</summary>
    PaidUntilRequired,
    /// <summary>409 «Опция {М} закрыта для подключения…».</summary>
    ClosedForConnection,
}

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.13 / API_CONTRACT_CYCLE40.md §40.31.6 — the decision for ONE line of the two channel options in an admin
/// subscription assignment (<c>PUT /api/admin/billing-accounts/{id}/subscription</c>). The paid-until date is mandatory; the tariff's option
/// rule is not consulted; while the option is closed by the platform switch (§40.7.1) CREATING its row (or reviving an ended one) and
/// EXTENDING its date are refused, an unchanged row passes (what was bought keeps working), and a shortened date passes too. Pure.
/// </summary>
public static class ChannelOptionAssignmentRules
{
    public static ChannelOptionLineVerdict Evaluate(DateOnly? submittedPaidUntil, bool optionOpen, ExistingChannelOptionRow? existing)
    {
        if (submittedPaidUntil is not { } submitted) return ChannelOptionLineVerdict.PaidUntilRequired;
        if (optionOpen) return ChannelOptionLineVerdict.Ok;

        if (existing is not { } row || row.EndsAtUtc is not null) return ChannelOptionLineVerdict.ClosedForConnection; // creation / revival
        var isExtension = row.PaidUntilUtc is not { } current || submitted > DateOnly.FromDateTime(current);
        return isExtension ? ChannelOptionLineVerdict.ClosedForConnection : ChannelOptionLineVerdict.Ok;
    }
}
