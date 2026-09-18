using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services;

/// <summary>
/// The one place a channel's <c>stateText</c> (API_CONTRACT_CYCLE4.md §19.5), <c>canConnect</c> /
/// <c>canReplace</c> flags, and the notification-settings screen's <c>blockedReason</c> are assembled
/// in Russian — reused by <c>NotificationChannelsController</c> (the channels list/detail) and
/// <c>CompanyNotificationsController</c> (the settings screen), so the two never drift into two
/// different sentences about the same event (the discrepancy the frontend flagged and this cycle's
/// contract fix, §19.5, closes). Pure: no DB, no HTTP — every fact it needs is a parameter.
///
/// Texts for the seven owner-visible states are copied verbatim from SPEC.md §4.3 p.2 (the GREEN-API
/// state mapping table) and p.2's <c>NeedsReconnect</c> paragraph — this is the one place those
/// sentences live; nothing else in the codebase should hardcode them again.
///
/// Deliberately placed outside <c>Services/Notifications/</c> (this cycle's file-ownership split
/// between two backend developers), even though it is conceptually the presentation half of
/// <c>ChannelStateMapper</c> (architecture §25's file map).
/// </summary>
public static class ChannelPresentation
{
    public static string StateText(ChannelState state, string? phoneMasked, int idleDays, DateTime? paidUntilUtc) => state switch
    {
        ChannelState.NotConnected => "Канал не подключён",
        ChannelState.Connecting => "Канал запускается",
        ChannelState.Connected => phoneMasked is null
            ? "Канал работает"
            : $"Канал работает, сообщения уходят с номера {phoneMasked}",
        ChannelState.Disconnected =>
            "Связь с WhatsApp разорвана — возможно, устройство отключено в приложении. Подключите заново",
        ChannelState.Blocked =>
            "WhatsApp заблокировал этот номер. Восстановить его нельзя — подключите другой номер, оплаченный период сохранится",
        ChannelState.DisabledByOwner => "Канал отключён вами",
        ChannelState.NeedsReconnect => paidUntilUtc is { } paidUntil
            ? $"Номер был отключён, потому что каналом {idleDays} {DaysWord(idleDays)} никто не пользовался. " +
              $"Назначенные салоны и оплаченный период до {paidUntil:dd.MM} сохранены — подключите номер заново, " +
              "повторная оплата не потребуется"
            : $"Номер был отключён, потому что каналом {idleDays} {DaysWord(idleDays)} никто не пользовался. " +
              "Назначенные салоны сохранены — подключите номер заново, повторная оплата не потребуется",
        ChannelState.Replaced => "Этот номер заменён после блокировки — используйте новый канал",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    /// <summary>US-31 p.1's four blocking reasons for the notification-settings screen
    /// (API_CONTRACT_CYCLE4.md §28.1) — order matters, it's the priority a caller should be told about
    /// the blocker in. <see langword="null"/> means the option is fully effective for this company.</summary>
    public static string? SettingsBlockedReason(
        bool planAllowsChannel, bool companyHasAssignment, ChannelPaymentStatus? paymentState, ChannelState? channelState)
    {
        if (!planAllowsChannel) return "Недоступно на вашем тарифе";
        if (!companyHasAssignment) return "Салон не привязан к каналу";
        if (paymentState != ChannelPaymentStatus.Paid) return "Канал не оплачен";
        if (channelState != ChannelState.Connected) return "Канал отвалился";
        return null;
    }

    /// <summary>"Connect" button is only meaningful from the two states §29.1/§29.3 describe as its
    /// starting points: never bound yet, or bound and then reclaimed by idle cleanup — both go through
    /// the same <c>connect</c> endpoint. A paid, risk-accepted channel already <c>Connecting</c> or
    /// <c>Connected</c> would 409 (§24.1); the button is gated off before the click, not after.</summary>
    public static bool CanConnect(ChannelState state, ChannelPaymentStatus paymentState, bool riskAccepted) =>
        riskAccepted && paymentState == ChannelPaymentStatus.Paid &&
        (state == ChannelState.NotConnected || state == ChannelState.NeedsReconnect);

    /// <summary>"Replace number" only makes sense for a banned channel (API_CONTRACT_CYCLE4.md §27's
    /// 409 rule) — every other state either doesn't need a replacement or isn't terminal yet.</summary>
    public static bool CanReplace(ChannelState state) => state == ChannelState.Blocked;

    private static string DaysWord(int days)
    {
        var lastTwo = days % 100;
        if (lastTwo is >= 11 and <= 14) return "дней";
        return (days % 10) switch
        {
            1 => "день",
            2 or 3 or 4 => "дня",
            _ => "дней",
        };
    }
}
