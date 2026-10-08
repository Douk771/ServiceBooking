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

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.12.3 — queues a messenger message to the guest of a booking through the SAME gate, routing and funding as every other message:
/// opt-out, the paid numbers of the account, the channel, the provider-delivery consent. Never sends by itself; `VisitStartUtc` of the row is the moment it is
/// outdated (queued + <c>Stays:GuestMessageTtlMinutes</c>) — the dispatcher expires it as <c>StayMessageOutdated</c>.
/// </summary>
public sealed class StayMessageScheduler(
    AppDbContext db, SubscriptionResolver subscriptionResolver, ConsentLedger consentLedger, PublicSiteLinks links,
    IOptions<NotificationOptions> notificationOptions, IOptions<StaysOptions> options, IStaysClock clock)
{
    public Task QueueAsync(StayBooking booking, Company company, string marker, NotificationType type, StayTextFacts facts, CancellationToken ct) =>
        QueueAsync(StayNotificationSubject.Of(booking), booking.GuestPhone, booking.GuestName, booking.GuestUserId, booking.PersonalDataErased, company, marker, type,
            unsubscribeUrl => StayNotificationTexts.Messenger(type, facts, links.StayBookingPageUrl(booking.PublicToken), unsubscribeUrl), ct);

    /// <summary>
    /// The one path of every messenger message of «Дома» (a booking or a stand-alone order): the gate, the routing and the funding are the same; <paramref name="bodyOf"/> receives the
    /// guest's unsubscribe link (or null) and returns the text. Nothing is queued for a depersonalised subject.
    /// </summary>
    public async Task QueueAsync(
        StayNotificationSubject subject, string? phone, string? recipientName, string? recipientUserId, bool personalDataErased, Company company, string marker,
        NotificationType type, Func<string?, string> bodyOf, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(phone) || personalDataErased) return; // depersonalised: nobody to write to

        var now = clock.UtcNow;
        var outdatedAtUtc = now.AddMinutes(options.Value.GuestMessageTtlMinutes);
        var channels = await db.ChannelCompanyAssignments.AsNoTracking().Include(a => a.Channel)
            .Where(a => a.CompanyId == company.Id).OrderBy(a => a.Transport).Select(a => a.Channel).ToListAsync(ct);
        var settings = await db.CompanyNotificationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == company.Id, ct);
        var plan = await subscriptionResolver.GetEffectivePlanAsync(company.Id);
        var optedOut = await db.NotificationOptOuts.AsNoTracking().AnyAsync(o => o.Phone == phone, ct);  // SUBJECT-PHONE-GATE: not-account-scoped — dispatch-time check against the message's own recipient phone (the booking's), not an account reading another subject's data

        bool? hasConsent = null;
        if (recipientUserId is not null)
            hasConsent = await consentLedger.CurrentAsync(ConsentSubject.ForUser(recipientUserId), LegalDocumentType.PdnConsent.ToString(), ConsentPurpose.ProviderDelivery, ct) is not null;

        var priorityTransport = settings?.PriorityTransport ?? new CompanyNotificationSettings().PriorityTransport;
        var representativeChannelId = channels.Count > 0 ? channels[0].Id : (Guid?)null;
        var gate = NotificationGate.Evaluate(
            plan, type, companyHasAssignment: channels.Count > 0, channel: channels.Count > 0 ? channels[0] : null, settings, optedOut,
            now, outdatedAtUtc, channelIsFunded: true,
            Enum.TryParse<ProviderDeliveryConsentMode>(notificationOptions.Value.ProviderDeliveryConsent, ignoreCase: true, out var consentMode)
                ? consentMode : ProviderDeliveryConsentMode.AccountsOnly,
            hasConsent);
        if (gate.Outcome == NotificationGateOutcome.Blocked)
        {
            await AddIfNew(subject, phone, recipientName, recipientUserId, company, marker, type, string.Empty, priorityTransport, representativeChannelId, outdatedAtUtc, NotificationStatus.Skipped, gate.Reason, ct);
            return;
        }

        var funding = await FundingAsync(channels, plan, ct);
        var candidates = channels.Select(c => new NotificationRouting.Candidate(c.Id, c.Transport, funding.GetValueOrDefault(c.Id))).ToList();
        var routing = NotificationRouting.SelectTargets(settings?.DeliveryMode ?? new CompanyNotificationSettings().DeliveryMode, priorityTransport, candidates);
        if (routing.Targets.Count == 0)
        {
            await AddIfNew(subject, phone, recipientName, recipientUserId, company, marker, type, string.Empty, priorityTransport, routing.UnavailableChannelId ?? representativeChannelId,
                outdatedAtUtc, NotificationStatus.Skipped, routing.SkipReason ?? NotificationReason.NotOnPaidPlan, ct);
            return;
        }

        var body = bodyOf(UnsubscribeUrl(phone));
        foreach (var target in routing.Targets)
            await AddIfNew(subject, phone, recipientName, recipientUserId, company, marker, type, body, target.Transport, target.ChannelId, outdatedAtUtc, NotificationStatus.Pending, null, ct);
    }

    private string? UnsubscribeUrl(string phone)
    {
        var key = notificationOptions.Value.UnsubscribeKey;
        return string.IsNullOrEmpty(key) ? null : links.UnsubscribeUrl(UnsubscribeTokens.Build(phone, Encoding.UTF8.GetBytes(key)));
    }

    private async Task AddIfNew(
        StayNotificationSubject subject, string phone, string? recipientName, string? recipientUserId, Company company, string marker, NotificationType type, string body,
        NotificationTransport transport, Guid? channelId, DateTime outdatedAtUtc, NotificationStatus status, NotificationReason? reason, CancellationToken ct)
    {
        // One event/marker — one message per channel: the key is the whole guarantee (unique index), the existence check only avoids an exception.
        var key = $"{type}:{subject.KeyPart}:{marker}:{transport}";
        if (await db.OutboundNotifications.AnyAsync(n => n.IdempotencyKey == key, ct)) return;
        db.OutboundNotifications.Add(new OutboundNotification
        {
            Id = Guid.NewGuid(), CompanyId = company.Id, ChannelId = channelId, Transport = transport, StayBookingId = subject.StayBookingId,
            StayServiceOrderId = subject.StayServiceOrderId, Type = type,
            RecipientPhone = phone, RecipientName = recipientName, RecipientUserId = recipientUserId, Body = body,
            DueAtUtc = clock.UtcNow, VisitStartUtc = outdatedAtUtc, Status = status, Reason = reason, Generation = 0, IdempotencyKey = key,
        });
    }

    private async Task<Dictionary<Guid, bool>> FundingAsync(IReadOnlyList<NotificationChannel> channels, EffectivePlan plan, CancellationToken ct)
    {
        var result = new Dictionary<Guid, bool>();
        var accountIds = channels.Where(c => c.BillingAccountId is not null).Select(c => c.BillingAccountId!.Value).Distinct().ToList();
        if (accountIds.Count == 0) return result;
        var siblings = await db.NotificationChannels.AsNoTracking().Where(c => c.BillingAccountId != null && accountIds.Contains(c.BillingAccountId!.Value)).ToListAsync(ct);
        foreach (var accountId in accountIds)
            foreach (var (channelId, state) in ChannelFunding.Rank(siblings.Where(c => c.BillingAccountId == accountId).ToList(), plan.PaidNotificationNumbers))
                result[channelId] = state == ChannelFundingState.Funded;
        return result;
    }
}
