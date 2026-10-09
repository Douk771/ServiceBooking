using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>The payment filter of the admin table (API_CONTRACT_CYCLE40.md §40.36).</summary>
public enum AdminChannelPaymentFilter { Paid, NotPaid, Requested, Suspended, Trial }

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.13 — the SuperAdmin's view of the numbers: the table rows, the summary, the card and «Подтвердить оплату». Every status and
/// text comes from the same pure rules the owner sees (<see cref="ChannelPresentation"/>), computed from the facts of <see cref="NumbersOverviewBuilder"/> — the admin
/// and the owner never disagree about what a number is doing.
/// </summary>
public sealed class AdminChannelsBuilder(AppDbContext db, NumbersOverviewBuilder numbers, AccountMessagingReader messagingReader, PlatformSettings platformSettings)
{
    public sealed record Row(NotificationChannel Channel, AdminChannelDto Dto, AdminChannelPaymentFilter Payment, ChannelDisplayFacts Facts);

    public const string ReplacedConflict = "Номер заменён — подтвердите оплату в карточке нового номера";

    /// <summary>Builds the rows of the given channels (one context per owner: the consent facts are the owner's).</summary>
    public async Task<List<Row>> BuildRowsAsync(IReadOnlyList<NotificationChannel> channels, CancellationToken ct)
    {
        var rows = new List<Row>(channels.Count);
        var ownerIds = channels.Select(c => c.OwnerUserId).Distinct().ToList();
        var owners = await db.Users.AsNoTracking().Where(u => ownerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);

        foreach (var group in channels.GroupBy(c => c.OwnerUserId))
        {
            var accountIds = group.Select(c => c.BillingAccountId ?? Guid.Empty).Distinct().ToList();
            var ctx = await numbers.LoadAsync(accountIds, group.Key, ct);
            owners.TryGetValue(group.Key, out var owner);
            foreach (var channel in group)
            {
                if (channel.BillingAccountId is null || !ctx.States.ContainsKey(channel.BillingAccountId.Value)) continue;
                var facts = NumbersOverviewBuilder.FactsOf(channel, ctx);
                var state = ctx.States[channel.BillingAccountId.Value];
                var transport = state.For(channel.Transport);
                var payment = transport.Payment;
                var display = ChannelPresentation.Display(facts);
                var paymentFilter =
                    channel.IsSuspendedByAdmin ? AdminChannelPaymentFilter.Suspended :
                    payment.Paid && payment.IsTrial ? AdminChannelPaymentFilter.Trial :
                    payment.Paid ? AdminChannelPaymentFilter.Paid :
                    facts.RequestNewerThanPayment ? AdminChannelPaymentFilter.Requested : AdminChannelPaymentFilter.NotPaid;
                var fundingState = state.FundingOf(channel);
                var paymentStatus = ChannelPaymentState.Of(channel.IsSuspendedByAdmin, fundingState == ChannelFundingState.Funded);
                var companyCount = ctx.CompaniesByAccount.GetValueOrDefault(channel.BillingAccountId.Value)?.Count ?? 0;

                var dto = new AdminChannelDto(
                    channel.Id, channel.Transport, channel.State, paymentStatus,
                    owner is null ? "" : $"{owner.FirstName} {owner.LastName}",
                    owner?.PhoneNumber is null ? null : PhoneDisplayMask.Mask(owner.PhoneNumber),
                    payment.PaidUntil, companyCount, channel.IdleSinceUtc, channel.RequestedAtUtc, channel.Inn, channel.LegalEntityForm,
                    DisplayStatus: display.Status, DisplayText: display.Text,
                    StateText: ChannelPresentation.StateText(channel.Transport, channel.State, facts.PhoneMasked, ctx.IdleDays, channel.LastStateReason),
                    PhoneMasked: facts.PhoneMasked, PaymentText: PaymentText(paymentFilter, payment, channel), IsSuspended: channel.IsSuspendedByAdmin,
                    CreatedAt: channel.CreatedAt,
                    AvailableActions: ActionsOf(channel, transport.Option.Open));
                rows.Add(new Row(channel, dto, paymentFilter, facts));
            }
        }
        return rows;
    }

    private static string PaymentText(AdminChannelPaymentFilter filter, TransportPayment payment, NotificationChannel channel) => filter switch
    {
        AdminChannelPaymentFilter.Suspended => "приостановлен",
        AdminChannelPaymentFilter.Trial => payment.PaidUntil is { } t ? $"пробный до {t:dd.MM.yyyy}" : "пробный",
        AdminChannelPaymentFilter.Paid => payment.PaidUntil is { } p ? $"оплачено до {p:dd.MM.yyyy}" : "оплачено",
        AdminChannelPaymentFilter.Requested => channel.RequestedAtUtc is { } r ? $"заявка от {r:dd.MM}" : "заявка",
        _ => "не оплачено",
    };

    /// <summary>Suspend/Resume — any number except a replaced one; ConfirmPayment — not replaced and the option is open (§40.13).</summary>
    private static List<string> ActionsOf(NotificationChannel channel, bool optionOpen)
    {
        var actions = new List<string>();
        if (channel.State == ChannelState.Replaced) return actions;
        actions.Add(channel.IsSuspendedByAdmin ? "Resume" : "Suspend");
        if (optionOpen) actions.Add("ConfirmPayment");
        return actions;
    }

    public async Task<AdminChannelCardDto?> BuildCardAsync(Guid id, CancellationToken ct)
    {
        var channel = await db.NotificationChannels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (channel is null || channel.BillingAccountId is null) return null;

        var rows = await BuildRowsAsync([channel], ct);
        var row = rows.SingleOrDefault();
        if (row is null) return null;

        var ctx = await numbers.LoadAsync([channel.BillingAccountId.Value], channel.OwnerUserId, ct);
        var state = ctx.States[channel.BillingAccountId.Value];
        var transport = state.For(channel.Transport);
        var payment = transport.Payment;
        var companies = (ctx.CompaniesByAccount.GetValueOrDefault(channel.BillingAccountId.Value) ?? [])
            .Select(c => new ChannelCompanyDto(c.Id, c.Name, c.IsActive)).ToList();

        var stateEvents = (await db.ChannelStateEvents.AsNoTracking().Where(e => e.ChannelId == id)
                .OrderByDescending(e => e.OccurredAtUtc).Take(200).ToListAsync(ct))
            .Select(e => new AdminChannelStateEventDto(e.OccurredAtUtc, e.FromState, e.ToState, e.Reason, ReasonText(e.Reason), e.Detail)).ToList();

        var optionCode = channel.Transport == NotificationTransport.Max ? ChannelOptionCodes.Max : ChannelOptionCodes.WhatsApp;
        var userIds = new HashSet<string>();
        var paymentLogs = await db.ChannelPaymentLogs.AsNoTracking().Where(l => l.ChannelId == id).OrderByDescending(l => l.ChangedAtUtc).Take(200).ToListAsync(ct);
        var optionLogs = await db.ChannelOptionChangeLogs.AsNoTracking()
            .Where(l => l.BillingAccountId == channel.BillingAccountId && l.OptionCode == optionCode).OrderByDescending(l => l.ChangedAtUtc).Take(200).ToListAsync(ct);
        foreach (var l in paymentLogs) userIds.Add(l.ChangedByUserId);
        foreach (var l in optionLogs) if (l.ChangedByUserId is not null) userIds.Add(l.ChangedByUserId);
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);
        string? NameOf(string? userId) => userId is not null && names.TryGetValue(userId, out var n) ? n : null;

        var paymentEvents = new List<AdminChannelPaymentEventDto>();
        foreach (var l in paymentLogs)
        {
            var kind = l.Comment is not null && l.Comment.StartsWith("suspended", StringComparison.Ordinal) ? "Suspended"
                : l.Comment is not null && l.Comment.StartsWith("resumed", StringComparison.Ordinal) ? "Resumed" : "PaymentConfirmed";
            paymentEvents.Add(new AdminChannelPaymentEventDto(l.ChangedAtUtc, kind, null, NameOf(l.ChangedByUserId), l.OldPaidUntil, l.NewPaidUntil, l.Comment));
        }
        foreach (var l in optionLogs)
            paymentEvents.Add(new AdminChannelPaymentEventDto(l.ChangedAtUtc, "OptionChanged", (ChannelOptionChangeSource)l.Source, NameOf(l.ChangedByUserId), l.OldPaidUntilUtc, l.NewPaidUntilUtc, l.Comment));
        paymentEvents = paymentEvents.OrderByDescending(e => e.OccurredAtUtc).ToList();

        var replacesId = await db.NotificationChannels.AsNoTracking().Where(c => c.ReplacedByChannelId == id).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        ChannelTestDto? lastTest = channel.LastTestResult is { } result && channel.LastTestResultAtUtc is { } at
            ? new ChannelTestDto(result, at, ChannelTestTexts.For(result, ctx.OwnerPhoneMasked, null)) : null;

        return new AdminChannelCardDto(
            row.Dto, channel.BillingAccountId.Value, channel.OwnerUserId, channel.State, channel.LastStateReason,
            channel.LastStateReason is { } reason ? ReasonText(reason) : null,
            channel.LegalEntityForm, channel.Inn, channel.RequestedAtUtc, channel.RiskAcceptedAtUtc, channel.RiskAcceptedVersion,
            NumbersOverviewBuilder.TermsAccepted(channel, ctx), channel.CreatedAt, channel.ConnectedAtUtc, channel.LastStateCheckAtUtc,
            channel.IdleSinceUtc, row.Facts.IdleDeadlineUtc, channel.ProviderServerCountry, lastTest,
            new AdminChannelPaymentDto(payment.Paid, payment.PaidUntil, payment.IsTrial, row.Facts.RequestNewerThanPayment, payment.LastPaymentAt),
            transport.Option.Open, channel.ReplacedByChannelId, replacesId, companies, stateEvents, paymentEvents, row.Dto.AvailableActions);
    }

    public sealed record ConfirmResult(int? Status, string? Text, bool Ok)
    {
        public static ConfirmResult Fail(int status, string text) => new(status, text, false);
        public static readonly ConfirmResult Success = new(null, null, true);
    }

    /// <summary>«Подтвердить оплату» (§40.13): the paid period of the account's option for the number's transport is extended from max(now, current end) by
    /// <paramref name="months"/>; journals: <see cref="ChannelPaymentLog"/> and <see cref="ChannelOptionChangeLog"/>. The caller saves.</summary>
    public async Task<ConfirmResult> ConfirmPaymentAsync(NotificationChannel channel, int months, string? comment, string adminUserId, CancellationToken ct)
    {
        if (channel.State == ChannelState.Replaced) return ConfirmResult.Fail(StatusCodes.Status409Conflict, ReplacedConflict);
        if (channel.BillingAccountId is not { } accountId) return ConfirmResult.Fail(StatusCodes.Status409Conflict, "У номера нет аккаунта — оплату подтвердить нельзя");

        var transport = channel.Transport;
        var display = MessengerTexts.DisplayName(transport);
        var code = transport == NotificationTransport.Max ? ChannelOptionCodes.Max : ChannelOptionCodes.WhatsApp;
        var option = await db.SubscriptionOptions.FirstOrDefaultAsync(o => o.Code == code, ct);
        if (option is null) return ConfirmResult.Fail(StatusCodes.Status409Conflict, $"Опция {display} отсутствует в каталоге");
        if (!await platformSettings.IsOptionOpenAsync(transport, ct))
            return ConfirmResult.Fail(StatusCodes.Status409Conflict, $"Опция {display} закрыта для подключения. Откройте её в блоке „Подключение мессенджеров“");

        var now = DateTime.UtcNow;
        var row = await db.AccountSubscriptionOptions.FirstOrDefaultAsync(o => o.BillingAccountId == accountId && o.OptionId == option.Id, ct);
        var oldPaidUntil = row?.PaidUntilUtc;
        var baseDate = oldPaidUntil is { } current && current > now ? current : now;
        var newPaidUntil = baseDate.AddMonths(months);
        var (oldEndsAt, newEndsAt) = (row?.EndsAtUtc, (DateTime?)null);

        if (row is null)
        {
            db.AccountSubscriptionOptions.Add(new AccountSubscriptionOption
            {
                Id = Guid.NewGuid(), BillingAccountId = accountId, OptionId = option.Id, Quantity = 1, PaidUntilUtc = newPaidUntil,
                ActivatedAtUtc = now, ActivatedByUserId = adminUserId,
            });
        }
        else
        {
            row.Quantity = Math.Max(row.Quantity, 1);
            row.PaidUntilUtc = newPaidUntil;
            row.EndsAtUtc = null;
            row.ActivatedAtUtc = now;
            row.ActivatedByUserId = adminUserId;
            row.GrantedByTrial = false; // a bought-out row is an ordinary one (the invariant of AccountSubscriptionOption.GrantedByTrial)
            row.RequestedQuantity = null;
            row.RequestedAtUtc = null;
            row.RequestedByUserId = null;
        }

        ChannelOptionLog.Write(db, accountId, code, ChannelOptionChangeSource.AdminChannelCard, oldPaidUntil, newPaidUntil, oldEndsAt, newEndsAt,
            adminUserId, now, channel.Id, comment);
        db.ChannelPaymentLogs.Add(new ChannelPaymentLog
        {
            Id = Guid.NewGuid(), ChannelId = channel.Id, ChangedByUserId = adminUserId, OldPaidUntil = oldPaidUntil, NewPaidUntil = newPaidUntil,
            Comment = string.IsNullOrWhiteSpace(comment) ? $"payment confirmed: {months} мес." : $"payment confirmed: {comment}", ChangedAtUtc = now,
        });
        return ConfirmResult.Success;
    }

    /// <summary>Plain Russian line for a state-journal row (the journal keeps the enum, the admin reads this).</summary>
    public static string ReasonText(ChannelStateReason reason) => reason switch
    {
        ChannelStateReason.Authorized => "Владелец завершил привязку по QR",
        ChannelStateReason.ProviderReportsUnauthorized => "Провайдер сообщил: номер не авторизован",
        ChannelStateReason.ProviderReportsBlocked => "Провайдер сообщил: номер заблокирован",
        ChannelStateReason.ConsecutiveSendFailuresExceeded => "Слишком много подряд неудачных отправок",
        ChannelStateReason.DisconnectedByOwner => "Владелец отвязал номер",
        ChannelStateReason.SuspendedByAdmin => "Приостановлен администратором",
        ChannelStateReason.UnauthorizedInstanceTimedOut => "Экземпляр не был авторизован вовремя",
        ChannelStateReason.IdleInstanceDeleted => "Экземпляр удалён после простоя",
        ChannelStateReason.ReplacedAfterBan => "Номер заменён после блокировки",
        ChannelStateReason.SecretUnavailable => "Секрет экземпляра недоступен — нужна повторная привязка",
        ChannelStateReason.ServerCountryMismatch => "Страна сервера провайдера не совпала с ожидаемой",
        ChannelStateReason.ReplacedByOwner => "Владелец заменил номер",
        ChannelStateReason.RebindStarted => "Начата повторная привязка",
        ChannelStateReason.TestMessageSent => "Проверочное сообщение отправлено",
        ChannelStateReason.TestMessageFailed => "Проверочное сообщение не отправилось",
        ChannelStateReason.TestMessageSkipped => "Проверочное сообщение пропущено",
        _ => reason.ToString(),
    };
}
