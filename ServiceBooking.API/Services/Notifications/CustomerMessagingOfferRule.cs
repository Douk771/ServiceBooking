using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications;

public readonly record struct OfferTransportFacts(NotificationTransport Transport, bool Routable, bool Working);

public sealed record CustomerMessagingOfferInput(
    bool PlatformEnabled, CompanyKind Kind, bool CompanyFlag, bool Showcase, bool CompanyActive,
    NotificationDeliveryMode Mode, NotificationTransport Priority, IReadOnlyList<OfferTransportFacts> Transports);

public sealed record CustomerMessagingOfferResult(bool Offered, IReadOnlyList<NotificationTransport> Transports, string? CheckboxLabel)
{
    public static readonly CustomerMessagingOfferResult NotOffered = new(false, [], null);
}

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.10 — "do messages work for this company's customers". Preconditions (platform on,
/// company active and not a showcase, the company's own flag) → <c>targets</c> = routing over the ROUTABLE transports
/// and the mode → <c>transports = targets ∩ working</c> → <c>offered</c> iff preconditions ∧ transports ≠ ∅. Pure.
/// Vectors: <c>offer</c>.
/// </summary>
public static class CustomerMessagingOfferRule
{
    public static CustomerMessagingOfferResult Evaluate(CustomerMessagingOfferInput input)
    {
        if (!input.PlatformEnabled || !input.CompanyActive || input.Showcase || !input.CompanyFlag)
            return CustomerMessagingOfferResult.NotOffered;

        var targets = NotificationRouting.SelectRoutedTransports(
            input.Mode, input.Priority, input.Transports.Where(t => t.Routable).Select(t => t.Transport));
        var workingSet = input.Transports.Where(t => t.Working).Select(t => t.Transport).ToHashSet();
        var offered = targets.Where(workingSet.Contains).ToList();
        if (offered.Count == 0) return CustomerMessagingOfferResult.NotOffered;

        return new CustomerMessagingOfferResult(true, offered, CheckboxLabel(input.Kind, offered));
    }

    /// <summary>"Получать уведомления о {записи|заказе|брони} в {WhatsApp|MAX|WhatsApp и MAX}".</summary>
    public static string CheckboxLabel(CompanyKind kind, IEnumerable<NotificationTransport> transports)
    {
        var subject = kind switch
        {
            CompanyKind.Services => "записи",
            CompanyKind.Orders => "заказе",
            CompanyKind.Stays or CompanyKind.Baths => "брони",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        return $"Получать уведомления о {subject} в {MessengerTexts.TransportsPhrase(transports)}";
    }
}
