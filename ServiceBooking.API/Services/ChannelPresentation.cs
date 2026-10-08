using ServiceBooking.API.Services.Notifications;
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
/// Texts for the seven owner-visible states are copied verbatim from SPEC.md §4.3 p.2 (the provider's
/// state mapping table) and p.2's <c>NeedsReconnect</c> paragraph — this is the one place those
/// sentences live; nothing else in the codebase should hardcode them again.
///
/// Deliberately placed outside <c>Services/Notifications/</c> (this cycle's file-ownership split
/// between two backend developers), even though it is conceptually the presentation half of
/// <c>ChannelStateMapper</c> (architecture §25's file map).
/// </summary>
public static class ChannelPresentation
{
    public static string StateText(
        ChannelState state, string? phoneMasked, int idleDays, DateTime? paidUntilUtc, ChannelStateReason? lastReason = null) => state switch
    {
        ChannelState.NotConnected => "Канал не подключён",
        ChannelState.Connecting => "Канал запускается",
        ChannelState.Connected => phoneMasked is null
            ? "Канал работает"
            : $"Канал работает, сообщения уходят с номера {phoneMasked}",
        // T5-B13 (ARCHITECTURE_CYCLE5.md §52.2): a platform-side misconfiguration, not the owner's own
        // action — the wording (and, upstream in ChannelPresentation.CanConnect, the absence of a "retry"
        // path: Disconnected is not one of the three reconnectable states) both say the same thing: this
        // is not something the owner can fix by clicking Connect again.
        ChannelState.Disconnected when lastReason == ChannelStateReason.ServerCountryMismatch =>
            "Требуется вмешательство платформы для восстановления канала",
        ChannelState.Disconnected =>
            "Связь с WhatsApp разорвана — возможно, устройство отключено в приложении. Подключите заново",
        ChannelState.Blocked =>
            "WhatsApp заблокировал этот номер. Восстановить его нельзя — подключите другой номер, оплаченный период сохранится",
        ChannelState.DisabledByOwner => "Канал отключён вами",
        // I1: SecretUnavailable is a platform-side incident (encryption key rotated/lost), not something
        // the owner did or can read anything about "N days unused" into — a different sentence entirely,
        // so nobody is told to change a habit that was never the cause.
        ChannelState.NeedsReconnect when lastReason == ChannelStateReason.SecretUnavailable =>
            "Требуется повторная привязка после технических работ на платформе. Назначенные салоны и оплаченный период сохранены — подключите номер заново, повторная оплата не потребуется",
        ChannelState.NeedsReconnect => paidUntilUtc is { } paidUntil
            ? $"Номер был отключён, потому что каналом {idleDays} {DaysWord(idleDays)} никто не пользовался. " +
              $"Назначенные салоны и оплаченный период до {paidUntil:dd.MM} сохранены — подключите номер заново, " +
              "повторная оплата не потребуется"
            : $"Номер был отключён, потому что каналом {idleDays} {DaysWord(idleDays)} никто не пользовался. " +
              "Назначенные салоны сохранены — подключите номер заново, повторная оплата не потребуется",
        ChannelState.Replaced => "Этот номер заменён после блокировки — используйте новый канал",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    /// <summary>US-31 p.1's blocking reasons (cycle 40, §40.3.4: the tariff-flag reason is gone, three remain) for the notification-settings screen
    /// (API_CONTRACT_CYCLE4.md §28.1) — order matters, it's the priority a caller should be told about
    /// the blocker in. <see langword="null"/> means the option is fully effective for this company.</summary>
    public static string? SettingsBlockedReason(
        bool companyHasAssignment, ChannelPaymentStatus? paymentState, ChannelState? channelState)
    {
        if (!companyHasAssignment) return "Салон не привязан к каналу";
        if (paymentState != ChannelPaymentStatus.Paid) return "Канал не оплачен";
        if (channelState != ChannelState.Connected) return "Канал отвалился";
        return null;
    }

    /// <summary>"Connect" button is only meaningful from the two states §29.1/§29.3 describe as its
    /// starting points: never bound yet, or bound and then reclaimed by idle cleanup — both go through
    /// the same <c>connect</c> endpoint. A paid, risk-accepted channel already <c>Connecting</c> or
    /// <c>Connected</c> would 409 (§24.1); the button is gated off before the click, not after.</summary>
    // I10 / SPEC US-56 п. 2: a channel the OWNER disconnected must be reconnectable again within the
    // same paid period — "отключил, потом передумал" is not the same event as a ban/idle-deletion, and
    // the paid period is explicitly preserved on Disconnect (API_CONTRACT_CYCLE4.md §26) precisely so
    // this path exists.
    public static bool CanConnect(ChannelState state, ChannelPaymentStatus paymentState, bool riskAccepted) =>
        riskAccepted && paymentState == ChannelPaymentStatus.Paid &&
        (state == ChannelState.NotConnected || state == ChannelState.NeedsReconnect || state == ChannelState.DisabledByOwner);

    /// <summary>"Replace number" only makes sense for a banned channel (API_CONTRACT_CYCLE4.md §27's
    /// 409 rule) — every other state either doesn't need a replacement or isn't terminal yet.</summary>
    public static bool CanReplace(ChannelState state) => state == ChannelState.Blocked;

    /// <summary>ARCHITECTURE_CYCLE9.md §114.1 example — the display name printed next to the messenger
    /// picker on the channel-request screen. Russian text collected here, in ONE place, same convention
    /// as everything else in this class.</summary>
    public static string TransportDisplayName(NotificationTransport transport) => transport switch
    {
        NotificationTransport.WhatsApp => "WhatsApp",
        NotificationTransport.Max => "MAX",
        _ => transport.ToString(),
    };

    /// <summary>ARCHITECTURE_CYCLE9.md §104.1/§104.9, §114.1 (US-119 acceptance criterion) — a limitation
    /// the owner is OBLIGED to see BEFORE requesting that transport, not after (B1's researched table:
    /// "для авторизации по QR-коду необходимо отключить пароль для входа в мессенджере MAX", confirmed
    /// against GREEN-API's own "Перед началом работы"/"Важные отличия версии v3" docs). Null for a
    /// transport with no such prerequisite (WhatsApp).</summary>
    public static string? TransportConnectionNotice(NotificationTransport transport) => transport switch
    {
        NotificationTransport.Max => "Для авторизации по QR в MAX нужно отключить пароль входа в мессенджере.",
        _ => null,
    };

    // ---- Cycle 40 (ARCHITECTURE_CYCLE40.md §40.6; texts: API_CONTRACT_CYCLE40.md §40.33) ----

    /// <summary>§40.6.2 — the three-state presentation of one channel (first matching row wins). Pure.
    /// Vectors: <c>display</c>. A <see langword="null"/> status means "do not show to the owner" (row 1, Replaced).</summary>
    public static ChannelDisplay Display(ChannelDisplayFacts f)
    {
        var m = MessengerTexts.DisplayName(f.Transport);
        var mismatch = f.LastStateReason == ChannelStateReason.ServerCountryMismatch;

        if (f.State == ChannelState.Replaced) return ChannelDisplay.Hidden;
        if (!f.PlatformEnabled) return new(ChannelDisplayStatus.Off, MessengerTexts.PlatformDisabled, null);
        if (f.Suspended) return new(ChannelDisplayStatus.Off, "Приостановлен администратором", null);
        if (f.State == ChannelState.DisabledByOwner)
            return new(ChannelDisplayStatus.Off, "Отключён вами",
                f.Paid ? ChannelAction.BindNumber : f.OptionSellable ? ChannelAction.Pay : null);
        if (f.IsDuplicate)
            return new(ChannelDisplayStatus.ActionRequired,
                $"Лишний номер {m}: сообщения уходят с другого номера. Отвяжите этот", ChannelAction.Unbind);
        if (!f.Paid && f.RequestNewerThanPayment)
            return new(ChannelDisplayStatus.ActionRequired, MessengerTexts.PaymentUnderReview, null);
        if (!f.Paid && !f.OptionSellable)
            return new(ChannelDisplayStatus.ActionRequired, $"Подключение {m} временно недоступно", null);
        if (!f.Paid && f.PaidUntil is { } expired)
            return new(ChannelDisplayStatus.ActionRequired, $"Оплата закончилась {MessengerTexts.FormatDate(expired)}", ChannelAction.Pay);
        if (!f.Paid) return new(ChannelDisplayStatus.ActionRequired, "Не оплачено", ChannelAction.Pay);

        var needsTerms = f.State == ChannelState.NotConnected || f.State == ChannelState.NeedsReconnect ||
                         (f.State == ChannelState.Disconnected && !mismatch);
        if (needsTerms && !f.TermsAccepted)
            return new(ChannelDisplayStatus.ActionRequired, $"Примите условия подключения {m}", ChannelAction.AcceptTerms);

        switch (f.State)
        {
            case ChannelState.NotConnected:
                return new(ChannelDisplayStatus.ActionRequired, "Оплата подтверждена — привяжите номер", ChannelAction.BindNumber);
            case ChannelState.Connecting:
                return new(ChannelDisplayStatus.ActionRequired, "Номер привязывается — отсканируйте QR", ChannelAction.BindNumber);
            case ChannelState.Disconnected when mismatch:
                return new(ChannelDisplayStatus.ActionRequired, "Требуется вмешательство платформы", null);
            case ChannelState.Disconnected:
                return new(ChannelDisplayStatus.ActionRequired, $"Связь с {m} разорвана — подключите номер заново", ChannelAction.Reconnect);
            case ChannelState.NeedsReconnect when f.LastStateReason == ChannelStateReason.SecretUnavailable:
                return new(ChannelDisplayStatus.ActionRequired,
                    "Нужна повторная привязка после технических работ. Оплата сохранена", ChannelAction.Reconnect);
            case ChannelState.NeedsReconnect:
                var used = f.IdleDays is { } d
                    ? $"им {d} {DaysWord(d)} никто не пользовался"
                    : "им давно никто не пользовался";
                return new(ChannelDisplayStatus.ActionRequired,
                    $"Номер отключён: {used}. Оплата сохранена — подключите заново", ChannelAction.Reconnect);
            case ChannelState.Blocked:
                return new(ChannelDisplayStatus.ActionRequired,
                    $"{m} заблокировал этот номер. Подключите другой — оплата сохранится", ChannelAction.ReplaceNumber);
            case ChannelState.Connected when f.IdleSinceUtc is not null:
                return new(ChannelDisplayStatus.ActionRequired,
                    f.IdleDeadlineUtc is { } deadline
                        ? $"Номер отключится {MessengerTexts.FormatDayMonth(deadline)}: ни у одной вашей компании не включены сообщения клиентам"
                        : "Номер скоро отключится: ни у одной вашей компании не включены сообщения клиентам",
                    null);
            case ChannelState.Connected:
                return new(ChannelDisplayStatus.Working,
                    f.PhoneMasked is null ? "Номер работает" : $"Сообщения уходят с номера {f.PhoneMasked}", null);
            default:
                throw new ArgumentOutOfRangeException(nameof(f), f.State, null);
        }
    }

    /// <summary>§40.6.3 — which wizard step to open at (first matching row wins). <paramref name="hasChannel"/> = the
    /// account has a live channel of this transport. Pure. Vectors: <c>wizardStep</c>.</summary>
    public static ChannelWizardStep WizardStep(ChannelDisplayFacts f, bool hasChannel)
    {
        if (!f.PlatformEnabled || (!f.Paid && !f.OptionSellable)) return ChannelWizardStep.Unavailable;
        if (!f.Paid && f.RequestNewerThanPayment) return ChannelWizardStep.PaymentPending;
        if (!f.Paid) return ChannelWizardStep.Payment;
        if (f.Suspended || f.State == ChannelState.Blocked ||
            (f.State == ChannelState.Disconnected && f.LastStateReason == ChannelStateReason.ServerCountryMismatch) || f.IsDuplicate)
            return ChannelWizardStep.None;
        // Without a live channel there is no real state (callers pass a placeholder): "Connected" needs a channel.
        if (hasChannel && f.State == ChannelState.Connected) return ChannelWizardStep.Done;
        if (!hasChannel || !f.TermsAccepted) return ChannelWizardStep.Terms;
        return f.State is ChannelState.NotConnected or ChannelState.Connecting or ChannelState.Disconnected
            or ChannelState.NeedsReconnect or ChannelState.DisabledByOwner
            ? ChannelWizardStep.Qr
            : ChannelWizardStep.None;
    }

    /// <summary>§40.6.3: <c>(!Paid ∨ IsTrial) ∧ OptionSellable</c>.</summary>
    public static bool CanRequestPayment(ChannelDisplayFacts f) => (!f.Paid || f.IsTrial) && f.OptionSellable;

    /// <summary>§40.33.2 — detailed <c>stateText</c> naming the right messenger (the transport-aware successor of the
    /// overload above, which stays for old callers until they are moved).</summary>
    public static string StateText(
        NotificationTransport transport, ChannelState state, string? phoneMasked, int idleDays, ChannelStateReason? lastReason = null)
    {
        var m = MessengerTexts.DisplayName(transport);
        return state switch
        {
            ChannelState.NotConnected => "Номер не привязан",
            ChannelState.Connecting => "Номер привязывается",
            ChannelState.Connected => phoneMasked is null ? "Номер работает" : $"Номер работает, сообщения уходят с номера {phoneMasked}",
            ChannelState.Disconnected when lastReason == ChannelStateReason.ServerCountryMismatch =>
                "Требуется вмешательство платформы для восстановления канала",
            ChannelState.Disconnected => $"Связь с {m} разорвана — возможно, устройство отключено в приложении. Подключите заново",
            ChannelState.Blocked =>
                $"{m} заблокировал этот номер. Восстановить его нельзя — подключите другой номер, оплаченный период сохранится",
            ChannelState.DisabledByOwner => "Номер отключён вами",
            ChannelState.NeedsReconnect when lastReason == ChannelStateReason.SecretUnavailable =>
                "Требуется повторная привязка после технических работ на платформе. Оплаченный период сохранён — подключите номер заново, повторная оплата не потребуется",
            ChannelState.NeedsReconnect =>
                $"Номер был отключён, потому что им {idleDays} {DaysWord(idleDays)} никто не пользовался. Оплаченный период сохранён — подключите номер заново, повторная оплата не потребуется",
            ChannelState.Replaced => "Этот номер заменён — используйте новый",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
    }

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

/// <summary>§40.6.1 — everything <see cref="ChannelPresentation.Display"/> and <see cref="ChannelPresentation.WizardStep"/>
/// read. <see cref="IdleDays"/> is the configured idle period (the N in "им {N} дней никто не пользовался"), not derived
/// from the dates.</summary>
public sealed record ChannelDisplayFacts(
    NotificationTransport Transport,
    ChannelState State,
    ChannelStateReason? LastStateReason,
    string? PhoneMasked,
    bool Paid,
    DateTime? PaidUntil,
    bool IsTrial,
    bool RequestNewerThanPayment,
    bool IsDuplicate,
    bool Suspended,
    bool PlatformEnabled,
    bool OptionOpen,
    bool OptionSellable,
    bool TermsAccepted,
    DateTime? IdleSinceUtc,
    DateTime? IdleDeadlineUtc,
    int? IdleDays);

public enum ChannelDisplayStatus { Working, ActionRequired, Off }

public enum ChannelAction { Pay, AcceptTerms, BindNumber, Reconnect, ReplaceNumber, Unbind }

public enum ChannelWizardStep { Payment, PaymentPending, Terms, Qr, Done, Unavailable, None }

/// <summary>A <see langword="null"/> <see cref="Status"/> = the channel is not shown to the owner (Replaced).</summary>
public readonly record struct ChannelDisplay(ChannelDisplayStatus? Status, string? Text, ChannelAction? Action)
{
    public static readonly ChannelDisplay Hidden = new(null, null, null);
}
