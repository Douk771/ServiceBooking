using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Orders.Notifications;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §457.1 — a message about an order to the customer's messenger, queued as an ordinary
/// <see cref="OutboundNotification"/> (<c>OrderId</c> set, <c>BookingId</c> null) and sent by the SAME dispatcher, through the same gate
/// (opt-out, provider-delivery consent, the paid number, the routing of the shop's delivery mode). The text is fixed
/// (<see cref="OrderNotificationTexts"/>): it never runs through the salon template validator. Like <c>NotificationScheduler</c> this only adds
/// rows to the caller's transaction — a queueing failure must not undo the action on the order, and there are no network calls here.
/// </summary>
public sealed class OrderMessageScheduler(
    AppDbContext db, AccountMessagingReader messagingReader, ConsentLedger consentLedger, PublicSiteLinks links,
    IOptions<NotificationOptions> notificationOptions, IOptions<OrdersOptions> ordersOptions)
{
    public async Task QueueAsync(
        Order order, Company shop, Guid orderEventId, NotificationType type, OrderTextFacts facts, CancellationToken ct)
    {
        var phone = order.CustomerPhone;
        if (string.IsNullOrEmpty(phone) || order.PersonalDataErased) return; // depersonalized: nobody to write to

        var now = DateTime.UtcNow;
        // VisitStartUtc of an order row = the moment it becomes outdated (§448.1): the dispatcher expires it as OrderMessageOutdated.
        var outdatedAtUtc = now.AddMinutes(ordersOptions.Value.CustomerMessageTtlMinutes);

        var channels = await db.ChannelCompanyAssignments.AsNoTracking().Include(a => a.Channel)
            .Where(a => a.CompanyId == shop.Id).OrderBy(a => a.Transport).Select(a => a.Channel).ToListAsync(ct);
        var settings = await db.CompanyNotificationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == shop.Id, ct);
        var messaging = await messagingReader.ForCompanyAsync(shop.Id, ct: ct);
        var optedOut = await db.NotificationOptOuts.AsNoTracking().AnyAsync(o => o.Phone == phone, ct);  // SUBJECT-PHONE-GATE: not-account-scoped — dispatch-time check against the message's own recipient phone (the order's), not an account reading another subject's data

        bool? hasConsent = null;
        if (order.CustomerUserId is not null)
            hasConsent = await consentLedger.CurrentAsync(
                ConsentSubject.ForUser(order.CustomerUserId), LegalDocumentType.PdnConsent.ToString(), ConsentPurpose.ProviderDelivery, ct) is not null;

        var priorityTransport = settings?.PriorityTransport ?? new CompanyNotificationSettings().PriorityTransport;
        var representativeChannelId = channels.Count > 0 ? channels[0].Id : (Guid?)null;

        // Channel-independent checks first, exactly like the salon queueing: funding per channel is the router's job below.
        var gate = NotificationGate.Evaluate(
            messaging.AnyPaid, type, companyHasAssignment: channels.Count > 0, channel: channels.Count > 0 ? channels[0] : null, settings, optedOut,
            now, outdatedAtUtc, channelIsFunded: true,
            Enum.TryParse<ProviderDeliveryConsentMode>(notificationOptions.Value.ProviderDeliveryConsent, ignoreCase: true, out var consentMode)
                ? consentMode : ProviderDeliveryConsentMode.AccountsOnly,
            hasConsent);
        if (gate.Outcome == NotificationGateOutcome.Blocked)
        {
            await AddIfNew(order, shop, orderEventId, type, string.Empty, priorityTransport, representativeChannelId, outdatedAtUtc,
                NotificationStatus.Skipped, gate.Reason, ct);
            return;
        }

        var funding = channels.ToDictionary(c => c.Id, c => messaging.FundingOf(c) == ChannelFundingState.Funded);
        var candidates = channels.Select(c => new NotificationRouting.Candidate(c.Id, c.Transport, funding.GetValueOrDefault(c.Id))).ToList();
        var routing = NotificationRouting.SelectTargets(
            settings?.DeliveryMode ?? new CompanyNotificationSettings().DeliveryMode, priorityTransport, candidates);
        if (routing.Targets.Count == 0)
        {
            await AddIfNew(order, shop, orderEventId, type, string.Empty, priorityTransport, routing.UnavailableChannelId ?? representativeChannelId,
                outdatedAtUtc, NotificationStatus.Skipped, routing.SkipReason ?? NotificationReason.NotOnPaidPlan, ct);
            return;
        }

        var body = OrderNotificationTexts.Messenger(type, facts, links.OrderPageUrl(order.PublicToken), UnsubscribeUrl(phone));
        foreach (var target in routing.Targets)
            await AddIfNew(order, shop, orderEventId, type, body, target.Transport, target.ChannelId, outdatedAtUtc, NotificationStatus.Pending, null, ct);
    }

    private string? UnsubscribeUrl(string phone)
    {
        var key = notificationOptions.Value.UnsubscribeKey;
        return string.IsNullOrEmpty(key) ? null : links.UnsubscribeUrl(UnsubscribeTokens.Build(phone, Encoding.UTF8.GetBytes(key)));
    }

    private async Task AddIfNew(
        Order order, Company shop, Guid orderEventId, NotificationType type, string body, NotificationTransport transport, Guid? channelId,
        DateTime outdatedAtUtc, NotificationStatus status, NotificationReason? reason, CancellationToken ct)
    {
        // One event — one message per channel: the key is the whole guarantee (unique index), the existence check only avoids an exception.
        var key = $"{type}:order:{orderEventId}:{transport}";
        if (await db.OutboundNotifications.AnyAsync(n => n.IdempotencyKey == key, ct)) return;

        db.OutboundNotifications.Add(new OutboundNotification
        {
            Id = Guid.NewGuid(),
            CompanyId = shop.Id,
            ChannelId = channelId,
            Transport = transport,
            OrderId = order.Id,
            Type = type,
            RecipientPhone = order.CustomerPhone!,
            RecipientName = order.CustomerName,
            RecipientUserId = order.CustomerUserId,
            Body = body,
            DueAtUtc = DateTime.UtcNow,
            VisitStartUtc = outdatedAtUtc,
            Status = status,
            Reason = reason,
            Generation = 0,
            IdempotencyKey = key,
        });
    }
}
