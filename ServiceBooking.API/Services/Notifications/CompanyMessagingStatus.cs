using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>One transport of the company's ACCOUNT as seen by the settings screens.</summary>
public readonly record struct TransportMessagingFacts(
    NotificationTransport Transport, bool Open, bool Paid, bool Routable, bool Working);

public sealed record CompanyMessagingStatusResult(
    bool MessagingActive,
    string? InactiveText,
    IReadOnlyList<NotificationTransport> WorkingTransports,
    bool DeliveryChoiceVisible,
    string? PriorityWarning,
    string? BlockedReason);

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.6.4 / API_CONTRACT_CYCLE40.md §40.29, §40.33.6 — what the three notification-settings
/// screens show about messaging for a company. Pure. "Working" = routable ∧ connected ∧ platform on (computed by the caller).
/// </summary>
public static class CompanyMessagingStatus
{
    public static CompanyMessagingStatusResult Evaluate(
        bool platformEnabled, NotificationDeliveryMode mode, NotificationTransport priorityTransport,
        IReadOnlyList<TransportMessagingFacts> transports)
    {
        var working = transports.Where(t => t.Working).Select(t => t.Transport).Distinct().OrderBy(t => (int)t).ToList();
        var active = platformEnabled && working.Count > 0;

        var routable = transports.Where(t => t.Routable).Select(t => t.Transport).Distinct().ToList();
        var priorityBroken = routable.Count == 2 && mode == NotificationDeliveryMode.PriorityChannel &&
                             !working.Contains(priorityTransport);
        var choiceVisible = working.Count == 2 || priorityBroken;

        return new CompanyMessagingStatusResult(
            active,
            active ? null : InactiveText(platformEnabled, transports),
            working,
            choiceVisible,
            priorityBroken ? MessengerTexts.PriorityWarning : null,
            active ? null : BlockedReason(platformEnabled, transports));
    }

    /// <summary>§40.33.6 salon <c>blockedReason</c>.</summary>
    public static string? BlockedReason(bool platformEnabled, IReadOnlyList<TransportMessagingFacts> transports)
    {
        if (!platformEnabled) return MessengerTexts.PlatformDisabled;
        if (!transports.Any(t => t.Open) && !transports.Any(t => t.Paid)) return MessengerTexts.BlockedSettingsNothingOpen;
        if (!transports.Any(t => t.Paid)) return MessengerTexts.BlockedSettingsNoPaid;
        if (!transports.Any(t => t.Working)) return MessengerTexts.BlockedSettingsNotWorking;
        return null;
    }

    /// <summary>§40.33.6 <c>inactiveText</c>: "Подключите WhatsApp или MAX выше" / "Подключите MAX выше" when only MAX is open.</summary>
    public static string? InactiveText(bool platformEnabled, IReadOnlyList<TransportMessagingFacts> transports)
    {
        if (!platformEnabled) return MessengerTexts.PlatformDisabled;
        var offered = transports.Where(t => t.Open || t.Paid).Select(t => t.Transport).Distinct().ToList();
        return offered.Count switch
        {
            0 => MessengerTexts.BlockedSettingsNothingOpen,
            1 => $"Подключите {MessengerTexts.DisplayName(offered[0])} выше",
            _ => "Подключите WhatsApp или MAX выше",
        };
    }

    /// <summary>§40.33.6 <c>messengerUnavailableText</c> (shop, "Дома"): no paid transport / paid but nothing bound / null.</summary>
    public static string? MessengerUnavailableText(IReadOnlyList<TransportMessagingFacts> transports)
    {
        if (!transports.Any(t => t.Paid)) return "Подключите номер в блоке „Номера“";
        if (!transports.Any(t => t.Routable)) return "Номер оплачен, но ещё не привязан — привяжите его в блоке „Номера“";
        return null;
    }
}
