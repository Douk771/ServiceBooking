namespace ServiceBooking.API.Services.Notifications.Funding;

/// <summary>The facts of one <c>AccountSubscriptionOption</c> row that the "is this transport paid" rule reads.</summary>
public sealed record OptionRowFacts(
    DateTime? EndsAtUtc, DateTime? PaidUntilUtc, bool GrantedByTrial, DateTime? ActivatedAtUtc, int Quantity = 1);

/// <summary>The account's "Записи" subscription as far as a LEGACY option row without its own date needs it.</summary>
public readonly record struct LegacySubscriptionFacts(bool Usable, DateTime? PaidUntil);

/// <summary>§40.3.1 output.</summary>
public readonly record struct TransportPayment(bool Paid, DateTime? PaidUntil, bool IsTrial, DateTime? LastPaymentAt)
{
    public static readonly TransportPayment None = new(false, null, false, null);
}

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.3.1 — "is the transport paid" as one pure function over one option row.
/// Option availability (Р40-Ю1) deliberately does not take part: closing an option forbids selling it, it never
/// switches off what was already bought or granted as a trial. Vectors: <c>optionFunding</c>.
/// </summary>
public static class ChannelOptionFunding
{
    public static TransportPayment Evaluate(
        OptionRowFacts? row, DateTime? accountTrialEndsAtUtc, LegacySubscriptionFacts subscription, DateTime nowUtc)
    {
        if (row is null) return TransportPayment.None;

        var isTrial = row.GrantedByTrial;
        var lastPayment = row.ActivatedAtUtc;

        if (row.EndsAtUtc is { } endsAt && endsAt <= nowUtc)
            return new TransportPayment(false, endsAt, isTrial, lastPayment);

        var (paid, paidUntil) = row switch
        {
            { PaidUntilUtc: { } until } => (until >= nowUtc, (DateTime?)until),
            { GrantedByTrial: true } => (accountTrialEndsAtUtc is { } trialEnd && trialEnd > nowUtc, accountTrialEndsAtUtc),
            _ => (subscription.Usable, subscription.PaidUntil),
        };

        return new TransportPayment(paid && row.Quantity >= 1, paidUntil, isTrial, lastPayment);
    }
}
